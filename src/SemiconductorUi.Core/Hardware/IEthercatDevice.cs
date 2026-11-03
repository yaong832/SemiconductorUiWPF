namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// EtherCAT 장치 접근 창구.
    /// 기존 IEG3268 DLL과 같은 이름의 메서드를 그대로 두어 호출부를 바꾸지 않고,
    /// DLL에 없는 기능(서보 알람, 감속 정지, 통신 상태 확인)을 추가로 제공합니다.
    /// 나중에 DLL을 자체 구현으로 교체할 때 이 인터페이스만 다시 구현하면 됩니다.
    /// </summary>
    public interface IEthercatDevice
    {
        #region 연결 / 데이터 수신

        bool CIFX_50RE_Connect();
        void CIFX_50RE_Disconnect();

        /// <summary>
        /// 입력 데이터 수신 주기 설정 및 시작 (ms)
        /// </summary>
        void ReadData_Send_Start(int interval);

        /// <summary>
        /// 입력 데이터 수신 타이머 등록. 여러 번 호출해도 한 번만 등록됩니다.
        /// </summary>
        void ReadData_Timer_Start();

        #endregion

        #region 디지털 I/O

        bool Digital_Input(int numbering);
        void Digital_Output(int numbering, bool value);

        /// <summary>
        /// 마지막으로 보낸 디지털 출력 값. DLL로는 실제 출력 단자를 다시 읽을 수 없어서
        /// 램프·도어처럼 출력으로만 제어하는 장치의 상태 표시는 이 값을 씁니다.
        /// Digital_Input과 번호 체계가 다르므로 섞어 쓰면 안 됩니다.
        /// </summary>
        bool GetDigitalOutput(int numbering);

        #endregion

        #region 축 1 (상하, UD)

        void Axis1_ON();
        void Axis1_OFF();
        bool Axis1_Status(string status);
        string Axis1_is_PosData();
        void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity);
        void Axis1_UD_POS_Update(long position);
        void Axis1_UD_Move_Send();
        void Axis1_UD_Homming();

        #endregion

        #region 축 2 (좌우, LR)

        void Axis2_ON();
        void Axis2_OFF();
        bool Axis2_Status(string status);
        string Axis2_is_PosData();
        void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity);
        void Axis2_LR_POS_Update(long position);
        void Axis2_LR_Move_Send();
        void Axis2_LR_Homming();

        #endregion

        #region DLL에 없는 추가 기능

        /// <summary>
        /// 서보 드라이브 알람(Fault) 상태. 상태워드 3번 비트를 읽습니다.
        /// </summary>
        /// <param name="axis">1 = 상하, 2 = 좌우</param>
        bool IsAxisFault(int axis);

        /// <summary>
        /// 드라이브 상태워드 원값(16비트). 로그와 진단용입니다. 읽을 수 없으면 0.
        /// </summary>
        /// <param name="axis">1 = 상하, 2 = 좌우</param>
        ushort GetStatusWord(int axis);

        /// <summary>
        /// 두 축 모두 감속 정지(Quick Stop)합니다. 서보 OFF와 달리 드라이브가 감속하며 멈춥니다.
        /// 다시 움직이려면 AxisN_ON()으로 서보를 다시 켜야 합니다.
        /// </summary>
        void QuickStopAll();

        /// <summary>
        /// 서보 드라이브 알람 리셋. 리셋 후 AxisN_ON()으로 서보를 다시 켜야 합니다.
        /// </summary>
        /// <param name="axis">1 = 상하, 2 = 좌우</param>
        void ResetAxisFault(int axis);

        /// <summary>
        /// 드라이버 에러 코드를 포함한 실제 통신 상태를 확인합니다.
        /// </summary>
        EthercatCommunicationStatus CheckCommunication();

        /// <summary>
        /// 서보가 실제로 운전 가능 상태(Operation enabled)인지. 상태워드 2번 비트를 읽습니다.
        /// </summary>
        /// <param name="axis">1 = 상하, 2 = 좌우</param>
        bool IsAxisOperationEnabled(int axis);

        #endregion

        #region 비상정지

        /// <summary>
        /// 비상정지가 걸려 있는지. ClearEmergencyStop()을 부르기 전까지 유지됩니다.
        /// </summary>
        bool IsEmergencyStopped { get; }

        /// <summary>
        /// 비상정지. 두 축 서보를 끄고(제어워드 0, AxisN_OFF와 같은 값) 해제 전까지 명령을 막습니다.
        /// 막는 것: 서보 ON, 이동, 원점복귀, 위치/속도 설정, 알람 리셋, 허용 목록 밖의 디지털 출력.
        /// 허용하는 것: 서보 OFF, 감속 정지, 모든 읽기, EmergencyStopPolicy의 출력.
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// 비상정지 해제. 서보는 꺼진 채로 남으므로 서보 ON과 원점복귀를 다시 해야 합니다.
        /// </summary>
        void ClearEmergencyStop();

        /// <summary>
        /// 비상정지 중이거나, 현재 동작(BeginOperation)이 비상정지 이전에 시작됐으면
        /// EmergencyStopActiveException을 던집니다. 완료 대기 루프에서 부릅니다.
        /// </summary>
        void ThrowIfEmergencyStopped();

        /// <summary>
        /// 백그라운드 장비 동작의 시작을 표시합니다. 이 범위 안에서는 시작 이후 비상정지가
        /// 한 번이라도 눌렸으면 해제된 뒤에도 명령이 거부됩니다. 비상정지 중에는 시작할 수 없습니다.
        /// </summary>
        System.IDisposable BeginOperation();

        #endregion
    }
}
