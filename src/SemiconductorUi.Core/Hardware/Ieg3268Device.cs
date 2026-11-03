using System;
using System.Threading;
using IEG3268_Dll;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// IEG3268 DLL을 감싸는 장치 창구.
    ///
    /// DLL은 출력 버퍼(WriteData) 하나에 두 축의 명령과 디지털 출력을 모두 담고
    /// 호출할 때마다 버퍼 전체를 보냅니다. 여러 스레드가 동시에 부르면 명령이 섞이므로
    /// 모든 호출을 하나의 락으로 직렬화합니다.
    ///
    /// 버퍼 위치는 DLL을 분석해 확인한 값입니다(보고서 8장).
    /// - 축1: 제어워드 WriteData[0], 상태워드 ReadData[0..1]
    /// - 축2: 제어워드 WriteData[23], 상태워드 ReadData[20..21]
    /// 제어워드/상태워드 비트는 CiA402 표준을 따릅니다.
    ///
    /// 비상정지 확인은 락 안에서 합니다. 락 밖에서 확인하면, 확인을 통과하고 락을 기다리던
    /// 명령이 비상정지의 서보 OFF 다음에 보내져 서보가 다시 켜질 수 있기 때문입니다.
    /// (DLL의 이동/원점복귀 명령은 제어워드 15를 써서 서보를 켭니다.)
    ///
    /// 회전과 실린더 인터락(TmMotionInterlock)도 같은 락 안에서 확인합니다.
    /// 화면이나 공정 코드가 확인을 빠뜨려도 실린더가 전진한 채로 회전하지 않게 하기 위함입니다.
    /// </summary>
    public sealed class Ieg3268Device : IEthercatDevice
    {
        internal const int Axis1ControlWordIndex = 0;
        internal const int Axis2ControlWordIndex = 23;
        internal const int Axis1StatusWordIndex = 0;
        internal const int Axis2StatusWordIndex = 20;

        internal const byte ControlWordDisableVoltage = 0x00;
        internal const byte ControlWordQuickStop = 0x02;
        internal const byte ControlWordFaultReset = 0x80;
        internal const byte StatusWordOperationEnabledBit = 0x04;
        internal const byte StatusWordFaultBit = 0x08;

        private const int FaultResetStepDelayMs = 100;

        private readonly IEG3268 _dll;
        private readonly ICifxChannel _channel;
        private readonly object _sync = new object();
        private readonly TmMotionInterlock _interlock;
        private bool _isConnected;
        private bool _readTimerRegistered;

        // 비상정지 잠금. 읽기는 락 없이 하므로 volatile입니다.
        private volatile bool _emergencyStopped;
        // 비상정지가 눌린 횟수. 동작이 시작된 뒤 비상정지가 있었는지 판단하는 데 씁니다.
        private long _emergencyStopCount;
        // 현재 동작(BeginOperation)이 시작될 때의 비상정지 횟수. 동작 범위 밖이면 null.
        private readonly AsyncLocal<long?> _operationStartStopCount = new AsyncLocal<long?>();

        public Ieg3268Device(IEG3268 dll)
            : this(dll, new CifxNativeChannel())
        {
        }

        public Ieg3268Device(IEG3268 dll, ICifxChannel channel)
            : this(dll, channel, new TmMotionInterlock())
        {
        }

        private Ieg3268Device(IEG3268 dll, ICifxChannel channel, TmMotionInterlock interlock)
        {
            _dll = dll ?? throw new ArgumentNullException(nameof(dll));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _interlock = interlock ?? throw new ArgumentNullException(nameof(interlock));
        }

        /// <summary>
        /// 테스트용. 실제 드라이버 연결 없이 연결된 상태로 시작합니다.
        /// </summary>
        internal Ieg3268Device(IEG3268 dll, ICifxChannel channel, bool isConnected)
            : this(dll, channel)
        {
            _isConnected = isConnected;
        }

        /// <summary>
        /// 테스트용. 인터락을 직접 넘깁니다.
        /// </summary>
        internal Ieg3268Device(IEG3268 dll, ICifxChannel channel, bool isConnected, TmMotionInterlock interlock)
            : this(dll, channel, interlock)
        {
            _isConnected = isConnected;
        }

        #region 연결 / 데이터 수신

        public bool CIFX_50RE_Connect()
        {
            lock (_sync)
            {
                _isConnected = _dll.CIFX_50RE_Connect();
                return _isConnected;
            }
        }

        public void CIFX_50RE_Disconnect()
        {
            lock (_sync)
            {
                _dll.CIFX_50RE_Disconnect();
                _isConnected = false;
            }
        }

        public void ReadData_Send_Start(int interval)
        {
            lock (_sync)
            {
                _dll.ReadData_Send_Start(interval);
            }
        }

        public void ReadData_Timer_Start()
        {
            lock (_sync)
            {
                // DLL은 호출할 때마다 수신 이벤트를 하나씩 더 붙이므로 한 번만 등록합니다.
                if (_readTimerRegistered)
                {
                    return;
                }

                _dll.ReadData_Timer_Start();
                _readTimerRegistered = true;
            }
        }

        #endregion

        #region 디지털 I/O

        public bool Digital_Input(int numbering)
        {
            lock (_sync)
            {
                return _dll.Digital_Input(numbering);
            }
        }

        public void Digital_Output(int numbering, bool value)
        {
            lock (_sync)
            {
                if (!EmergencyStopPolicy.IsOutputAllowedWhileStopped(numbering, value))
                {
                    ThrowIfEmergencyStopped();
                }

                _interlock.CheckCylinderOutputAllowed(numbering, value, IsRotationAtTarget);
                _interlock.NoteCylinderOutput(numbering, value);
                _dll.Digital_Output(numbering, value);
            }
        }

        public bool GetDigitalOutput(int numbering)
        {
            lock (_sync)
            {
                bool[] outputs = _dll.Digital_Out_Value;
                if (outputs == null || numbering < 0 || numbering >= outputs.Length)
                {
                    return false;
                }

                return outputs[numbering];
            }
        }

        #endregion

        #region 축 1

        public void Axis1_ON()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis1_ON();
            }
        }

        public void Axis1_OFF()
        {
            lock (_sync)
            {
                _dll.Axis1_OFF();
            }
        }

        public bool Axis1_Status(string status)
        {
            lock (_sync)
            {
                return _dll.Axis1_Status(status);
            }
        }

        public string Axis1_is_PosData()
        {
            lock (_sync)
            {
                return _dll.Axis1_is_PosData();
            }
        }

        public void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity)
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis1_UD_Config_Update(acceleration, deceleration, maxVelocity, velocity);
            }
        }

        public void Axis1_UD_POS_Update(long position)
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis1_UD_POS_Update(position);
            }
        }

        public void Axis1_UD_Move_Send()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis1_UD_Move_Send();
            }
        }

        public void Axis1_UD_Homming()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis1_UD_Homming();
            }
        }

        #endregion

        #region 축 2

        public void Axis2_ON()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis2_ON();
            }
        }

        public void Axis2_OFF()
        {
            lock (_sync)
            {
                _dll.Axis2_OFF();
                _interlock.NoteRotationStopped();
            }
        }

        public bool Axis2_Status(string status)
        {
            lock (_sync)
            {
                return _dll.Axis2_Status(status);
            }
        }

        public string Axis2_is_PosData()
        {
            lock (_sync)
            {
                return _dll.Axis2_is_PosData();
            }
        }

        public void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity)
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis2_LR_Config_Update(acceleration, deceleration, maxVelocity, velocity);
            }
        }

        public void Axis2_LR_POS_Update(long position)
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                _dll.Axis2_LR_POS_Update(position);
            }
        }

        public void Axis2_LR_Move_Send()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                CheckRotationAllowed("회전 이동");

                _interlock.NoteRotationCommand();
                _dll.Axis2_LR_Move_Send();
                // DLL 호출 안에 0.3초 대기가 있으므로 완료 대기 시간은 호출이 끝난 뒤부터 셉니다.
                _interlock.NoteRotationCommand();
            }
        }

        public void Axis2_LR_Homming()
        {
            lock (_sync)
            {
                ThrowIfEmergencyStopped();
                CheckRotationAllowed("회전 원점복귀");

                _interlock.NoteRotationCommand();
                _dll.Axis2_LR_Homming();
                _interlock.NoteRotationCommand();
            }
        }

        /// <summary>락 안에서 부릅니다.</summary>
        private void CheckRotationAllowed(string action)
        {
            bool extendOutputOn = false;
            bool[] outputs = _dll.Digital_Out_Value;
            if (outputs != null && TmMotionInterlock.CylinderExtendOutput < outputs.Length)
            {
                extendOutputOn = outputs[TmMotionInterlock.CylinderExtendOutput];
            }

            _interlock.CheckRotationAllowed(action, extendOutputOn,
                () => _dll.Digital_Input(TmMotionInterlock.CylinderRetractedInput));
        }

        /// <summary>회전 축이 목표(이동 위치 또는 원점)에 도착했는지. 락 안에서 부릅니다.</summary>
        private bool IsRotationAtTarget()
        {
            return _dll.Axis2_Status("PP_D") || _dll.Axis2_Status("HOME_D");
        }

        #endregion

        #region DLL에 없는 추가 기능

        public bool IsAxisFault(int axis)
        {
            // ReadData는 DLL 수신 타이머가 통째로 교체하므로 참조를 한 번만 잡아서 읽습니다.
            byte[] readData = _dll.ReadData;
            int index = GetStatusWordIndex(axis);

            if (readData == null || readData.Length <= index)
            {
                return false;
            }

            return (readData[index] & StatusWordFaultBit) != 0;
        }

        public ushort GetStatusWord(int axis)
        {
            byte[] readData = _dll.ReadData;
            int index = GetStatusWordIndex(axis);

            if (readData == null || readData.Length <= index + 1)
            {
                return 0;
            }

            // 상태워드는 하위 바이트가 앞에 옵니다 (DLL의 Axis1_Status도 같은 순서로 읽습니다).
            return (ushort)(readData[index] | (readData[index + 1] << 8));
        }

        public void QuickStopAll()
        {
            lock (_sync)
            {
                byte[] writeData = _dll.WriteData;
                writeData[Axis1ControlWordIndex] = ControlWordQuickStop;
                writeData[Axis2ControlWordIndex] = ControlWordQuickStop;
                _channel.Write(writeData);
                _interlock.NoteRotationStopped();
            }
        }

        public void ResetAxisFault(int axis)
        {
            int index = GetControlWordIndex(axis);

            lock (_sync)
            {
                ThrowIfEmergencyStopped();

                // 알람 리셋은 제어워드 7번 비트의 상승 에지로 동작합니다.
                byte[] writeData = _dll.WriteData;
                writeData[index] = ControlWordDisableVoltage;
                _channel.Write(writeData);
                Thread.Sleep(FaultResetStepDelayMs);

                writeData[index] = ControlWordFaultReset;
                _channel.Write(writeData);
                Thread.Sleep(FaultResetStepDelayMs);

                writeData[index] = ControlWordDisableVoltage;
                _channel.Write(writeData);

                if (axis == 2)
                {
                    _interlock.NoteRotationStopped();
                }
            }
        }

        public bool IsAxisOperationEnabled(int axis)
        {
            byte[] readData = _dll.ReadData;
            int index = GetStatusWordIndex(axis);

            if (readData == null || readData.Length <= index)
            {
                return false;
            }

            return (readData[index] & StatusWordOperationEnabledBit) != 0;
        }

        public EthercatCommunicationStatus CheckCommunication()
        {
            lock (_sync)
            {
                if (!_isConnected)
                {
                    return EthercatCommunicationStatus.NotConnected();
                }

                uint busState;
                uint busStateError = _channel.GetBusState(out busState);
                uint ioReadError = _channel.ReadIo(new byte[CifxConstants.IoReadLength]);

                return new EthercatCommunicationStatus(true, busStateError, busState, ioReadError);
            }
        }

        #endregion

        #region 비상정지

        public bool IsEmergencyStopped => _emergencyStopped;

        public void EmergencyStop()
        {
            // 잠금을 먼저 걸어서, 지금 다른 스레드가 보내는 중인 명령이 끝나면 그다음 명령부터 막힙니다.
            _emergencyStopped = true;
            Interlocked.Increment(ref _emergencyStopCount);

            lock (_sync)
            {
                // 다른 스레드의 DLL 명령이 진행 중이면(내부 대기 최대 약 0.3초) 그 명령이 끝난 뒤 여기로 들어옵니다.
                // 그 명령이 서보를 켰더라도 아래 쓰기로 다시 끕니다.
                // Axis1_OFF / Axis2_OFF와 같은 값(제어워드 0)을 두 축에 한 번에 씁니다.
                byte[] writeData = _dll.WriteData;
                writeData[Axis1ControlWordIndex] = ControlWordDisableVoltage;
                writeData[Axis2ControlWordIndex] = ControlWordDisableVoltage;
                _interlock.NoteRotationStopped();

                // 연결이 없으면 버퍼만 0으로 두고 보내지 않습니다. 다시 연결해도 서보 ON 값이 남아 있지 않게 합니다.
                if (_isConnected)
                {
                    _channel.Write(writeData);
                }
            }
        }

        public void ClearEmergencyStop()
        {
            _emergencyStopped = false;
        }

        public void ThrowIfEmergencyStopped()
        {
            if (_emergencyStopped)
            {
                throw new EmergencyStopActiveException("비상정지 중입니다. 알람 리셋으로 비상정지를 해제한 뒤 서보 ON과 원점복귀를 다시 하세요.");
            }

            long? startStopCount = _operationStartStopCount.Value;
            if (startStopCount.HasValue && startStopCount.Value != Interlocked.Read(ref _emergencyStopCount))
            {
                throw new EmergencyStopActiveException("비상정지로 중단된 동작입니다. 처음부터 다시 실행하세요.");
            }
        }

        public IDisposable BeginOperation()
        {
            ThrowIfEmergencyStopped();

            long? previous = _operationStartStopCount.Value;
            if (!previous.HasValue)
            {
                _operationStartStopCount.Value = Interlocked.Read(ref _emergencyStopCount);
            }

            return new OperationScope(this, previous);
        }

        private sealed class OperationScope : IDisposable
        {
            private readonly Ieg3268Device _owner;
            private readonly long? _previous;
            private bool _disposed;

            public OperationScope(Ieg3268Device owner, long? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _owner._operationStartStopCount.Value = _previous;
                _disposed = true;
            }
        }

        #endregion

        private static int GetControlWordIndex(int axis)
        {
            switch (axis)
            {
                case 1: return Axis1ControlWordIndex;
                case 2: return Axis2ControlWordIndex;
                default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "축 번호는 1 또는 2입니다.");
            }
        }

        private static int GetStatusWordIndex(int axis)
        {
            switch (axis)
            {
                case 1: return Axis1StatusWordIndex;
                case 2: return Axis2StatusWordIndex;
                default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "축 번호는 1 또는 2입니다.");
            }
        }
    }
}
