using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SemiconductorUi.Controls;
using SemiconductorUi.Process;

namespace SemiconductorUi.Hardware
{
    /// <summary>장비 제어 창 상태 글자 색 (원본 UiTheme 색 이름)</summary>
    public enum ManualStatusTone
    {
        Normal,
        Secondary,
        Success,
        Warning,
        Danger,
        Primary
    }

    /// <summary>지금 돌고 있는 "n초 ON 후 자동 OFF" 타이머 하나</summary>
    public sealed class ManualTimedAction
    {
        public ManualTimedAction(string key, string displayName, int seconds, DateTime endsAt)
        {
            Key = key;
            DisplayName = displayName;
            Seconds = seconds;
            EndsAt = endsAt;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public int Seconds { get; }
        public DateTime EndsAt { get; }
    }

    /// <summary>
    /// 원본 Forms.EquipmentControlForm("장비 직접 제어")의 장비 명령 부분.
    /// 출력 번호와 순서, 자동 공정 중 막는 동작, 안내 문구는 원본 그대로다. 화면(버튼, 메시지 상자)은 WPF가 맡는다.
    /// - 출력: 0 적색, 1 황색, 2 녹색, 3/6/9 Chamber A/B/C 램프, 4·5/7·8/10·11 도어, 12 실린더 전진, 13 후진, 14 진공, 15 배기
    /// - 입력: 12 실린더 후진 센서
    /// 모든 공개 메서드는 UI 스레드에서 부른다. 서보 ON과 원점복귀만 백그라운드에서 돌고, 결과는 post로 UI 스레드에 돌려준다.
    /// </summary>
    public sealed class EquipmentManualControl : IDisposable
    {
        public const int MinTimerSeconds = 1;
        public const int MaxTimerSeconds = 3600;
        public const int StatusPollIntervalMs = 500;

        private readonly IEthercatDevice _device;
        private readonly Func<bool> _isConnected;
        private readonly Action<string, string, MessageKind> _showMessage;
        private readonly Action<Action> _post;
        private readonly Func<int, Action, IDisposable> _schedule;
        private readonly Func<DateTime> _now;
        private readonly Action<string, string> _log;
        private readonly Dictionary<string, (IDisposable handle, ManualTimedAction info)> _timers =
            new Dictionary<string, (IDisposable, ManualTimedAction)>();
        private readonly Dictionary<string, (string name, Action on, Action off)> _timedDefinitions;

        private string _statusText;
        private ManualStatusTone _statusTone;
        private string _servoStatusText = "";
        private ManualStatusTone _servoStatusTone = ManualStatusTone.Secondary;
        private bool _isBusy;
        private bool _disposed;

        /// <param name="device">EtherCAT 장치</param>
        /// <param name="isConnected">EtherCAT 연결 여부 (메인 화면과 같은 값)</param>
        /// <param name="showMessage">메시지 상자 (제목, 내용 아님: 내용, 제목, 종류)</param>
        /// <param name="post">백그라운드 작업 결과를 UI 스레드로 보낸다</param>
        /// <param name="schedule">ms 뒤에 한 번 부르기. 돌려준 값을 Dispose하면 취소</param>
        /// <param name="log">운영 로그 (메시지, 수준). WPF 추가: 수동 명령을 로그에 남긴다</param>
        public EquipmentManualControl(IEthercatDevice device, Func<bool> isConnected,
            Action<string, string, MessageKind> showMessage, Action<Action> post,
            Func<int, Action, IDisposable> schedule, Func<DateTime> now = null, Action<string, string> log = null)
        {
            _device = device;
            _isConnected = isConnected ?? (() => false);
            _showMessage = showMessage ?? ((m, t, k) => { });
            _post = post ?? (a => a());
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _now = now ?? (() => DateTime.Now);
            _log = log;

            _timedDefinitions = new Dictionary<string, (string, Action, Action)>
            {
                { TimedKey(EquipmentRegion.ChamberA, false), ("Chamber A 램프", () => SetChamberLamp(EquipmentRegion.ChamberA, true), () => SetChamberLamp(EquipmentRegion.ChamberA, false)) },
                { TimedKey(EquipmentRegion.ChamberA, true), ("Chamber A 도어", () => SetChamberDoor(EquipmentRegion.ChamberA, true), () => SetChamberDoor(EquipmentRegion.ChamberA, false)) },
                { TimedKey(EquipmentRegion.ChamberB, false), ("Chamber B 램프", () => SetChamberLamp(EquipmentRegion.ChamberB, true), () => SetChamberLamp(EquipmentRegion.ChamberB, false)) },
                { TimedKey(EquipmentRegion.ChamberB, true), ("Chamber B 도어", () => SetChamberDoor(EquipmentRegion.ChamberB, true), () => SetChamberDoor(EquipmentRegion.ChamberB, false)) },
                { TimedKey(EquipmentRegion.ChamberC, false), ("Chamber C 램프", () => SetChamberLamp(EquipmentRegion.ChamberC, true), () => SetChamberLamp(EquipmentRegion.ChamberC, false)) },
                { TimedKey(EquipmentRegion.ChamberC, true), ("Chamber C 도어", () => SetChamberDoor(EquipmentRegion.ChamberC, true), () => SetChamberDoor(EquipmentRegion.ChamberC, false)) },
                { MainLampKey(0), ("3색 적색", () => SetMainLamp(0, true), () => SetMainLamp(0, false)) },
                { MainLampKey(1), ("3색 황색", () => SetMainLamp(1, true), () => SetMainLamp(1, false)) },
                { MainLampKey(2), ("3색 녹색", () => SetMainLamp(2, true), () => SetMainLamp(2, false)) },
                { VacuumKey, ("진공", VacuumOn, VacuumOffCore) },
                { ExhaustKey, ("배기", ExhaustOn, ExhaustOffCore) },
            };

            _statusText = IsConnected ? "EtherCAT 연결됨" : "EtherCAT 미연결";
            _statusTone = IsConnected ? ManualStatusTone.Success : ManualStatusTone.Danger;
            UpdateServoStatusFromDevice();
        }

        #region 메인 화면에 알리는 콜백 (원본 그대로)

        public Action<EquipmentRegion, bool> OnDoorStateChanged; // region, isOpen
        public Action<EquipmentRegion, bool> OnChamberLampStateChanged; // region, isOn
        public Action<bool, bool, bool> OnMainLampStateChanged; // red, yellow, green
        public Action<bool> OnServoStateChanged; // isOn
        public Action OnHomingRequested; // 원점복귀 완료
        public Action OnTeachingRequested; // 티칭 위치 편집
        public Action<bool> OnCylinderStateChanged; // isExtended
        public Action<bool> OnVacuumStateChanged; // isOn
        public Action OnEmergencyStopRequested; // 긴급 정지 (메인 화면 상태 갱신)

        /// <summary>자동 공정(Running/Paused) 중이면 true. 챔버 램프·도어·진공 수동 제어를 막을 때 사용.</summary>
        public Func<bool> IsAutoProcessActive;

        /// <summary>원점복귀 절차가 성공해 아직 원점에 있다고 기록된 상태인지 (메인 화면과 같은 기준)</summary>
        public Func<bool> IsHomedBySequence;

        #endregion

        /// <summary>상태 글자, 서보 상태, 타이머, 바쁨이 바뀌었다 (UI 스레드)</summary>
        public event Action Changed;

        public bool IsConnected => _device != null && _isConnected();

        /// <summary>원본 labelStatus</summary>
        public string StatusText => _statusText;
        public ManualStatusTone StatusTone => _statusTone;

        /// <summary>원본 labelServoStatus</summary>
        public string ServoStatusText => _servoStatusText;
        public ManualStatusTone ServoStatusTone => _servoStatusTone;

        /// <summary>서보 ON이나 원점복귀가 백그라운드에서 도는 중 (WPF: 그동안 두 버튼을 끈다)</summary>
        public bool IsBusy => _isBusy;

        public IReadOnlyList<ManualTimedAction> ActiveTimers => _timers.Values.Select(t => t.info).OrderBy(t => t.EndsAt).ToList();

        public const string VacuumKey = "Vacuum";
        public const string ExhaustKey = "Exhaust";

        /// <summary>원본 타이머 키: "ChamberA_Lamp", "ChamberA_Door" …</summary>
        public static string TimedKey(EquipmentRegion region, bool door) => $"{RegionKey(region)}_{(door ? "Door" : "Lamp")}";

        /// <summary>원본 타이머 키: "MainLamp_Red" / "MainLamp_Yellow" / "MainLamp_Green"</summary>
        public static string MainLampKey(int outputIndex) => "MainLamp_" + (outputIndex == 0 ? "Red" : outputIndex == 1 ? "Yellow" : "Green");

        public string TimedDisplayName(string key) => _timedDefinitions.TryGetValue(key, out var d) ? d.name : key;

        private static string RegionKey(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return "ChamberA";
                case EquipmentRegion.ChamberB: return "ChamberB";
                case EquipmentRegion.ChamberC: return "ChamberC";
                default: return region.ToString();
            }
        }

        private static int LampOutput(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return 3;
                case EquipmentRegion.ChamberB: return 6;
                case EquipmentRegion.ChamberC: return 9;
                default: throw new ArgumentOutOfRangeException(nameof(region));
            }
        }

        /// <summary>도어 출력 (outputIndex1, outputIndex2): A 4·5, B 7·8, C 10·11</summary>
        private static (int first, int second) DoorOutputs(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return (4, 5);
                case EquipmentRegion.ChamberB: return (7, 8);
                case EquipmentRegion.ChamberC: return (10, 11);
                default: throw new ArgumentOutOfRangeException(nameof(region));
            }
        }

        private static string ChamberLetter(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return "A";
                case EquipmentRegion.ChamberB: return "B";
                default: return "C";
            }
        }

        #region 상태 글자

        private void SetStatus(string text, ManualStatusTone tone, bool log = true)
        {
            _statusText = text;
            _statusTone = tone;
            if (log && _log != null)
            {
                _log($"장비 제어: {text}", tone == ManualStatusTone.Danger ? "ERROR" : "INFO");
            }
            RaiseChanged();
        }

        private void SetServoStatus(string text, ManualStatusTone tone)
        {
            _servoStatusText = text;
            _servoStatusTone = tone;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (!_disposed) Changed?.Invoke();
        }

        private void Fail(string what, Exception ex)
        {
            _showMessage($"{what} 오류: {ex.Message}", "오류", MessageKind.Error);
            SetStatus($"오류: {ex.Message}", ManualStatusTone.Danger);
        }

        /// <summary>원본 UpdateConnectionStatus: 연결이 바뀌면 상태 글자를 다시 쓴다</summary>
        public void UpdateConnectionStatus()
        {
            bool connected = IsConnected;
            SetStatus(connected ? "EtherCAT 연결됨" : "EtherCAT 미연결", connected ? ManualStatusTone.Success : ManualStatusTone.Danger, log: false);
            UpdateServoStatusFromDevice();
        }

        #endregion

        #region 막기 (원본 EnsureManualIoAllowed / 연결 확인)

        /// <summary>원본 EnsureManualIoAllowed: 자동 공정 실행/일시정지 중이면 안내하고 false</summary>
        public bool EnsureManualIoAllowed(string actionName)
        {
            if (IsAutoProcessActive != null && IsAutoProcessActive())
            {
                _showMessage(
                    $"자동 공정 실행/일시정지 중에는 '{actionName}' 수동 제어를 할 수 없습니다.\n" +
                    "공정 정지 후 장비 점검용으로 사용해 주세요.\n" +
                    "(공정 중 수동 제어는 자동 시퀀스와 충돌합니다. 급히 멈춰야 하면 긴급 정지를 누르세요.)",
                    "수동 제어 제한",
                    MessageKind.Warning);
                return false;
            }
            return true;
        }

        private bool EnsureConnected(string title = "오류")
        {
            if (!IsConnected)
            {
                _showMessage("EtherCAT이 연결되지 않았습니다.", title, MessageKind.Warning);
                return false;
            }
            return true;
        }

        private void ShowEmergencyStopActive()
        {
            _showMessage(
                "비상정지 중입니다.\n메인 화면의 알람 리셋으로 비상정지를 해제한 뒤 다시 시도하세요.",
                "비상정지 중", MessageKind.Warning);
        }

        #endregion

        #region 챔버 램프 · 도어 · 3색 램프 · 일괄

        /// <summary>원본 ControlChamberLamp: ON만 공정 중에 막는다 (OFF는 수동 안전 해제)</summary>
        public void SetChamberLamp(EquipmentRegion region, bool on)
        {
            if (on && !EnsureManualIoAllowed("챔버 램프 ON")) return;
            if (!EnsureConnected()) return;
            try
            {
                _device.Digital_Output(LampOutput(region), on);
                SetStatus($"Chamber {ChamberLetter(region)} 램프 {(on ? "ON" : "OFF")}", ManualStatusTone.Success);
                OnChamberLampStateChanged?.Invoke(region, on);
            }
            catch (Exception ex)
            {
                Fail("램프 제어", ex);
            }
        }

        /// <summary>OFF 버튼: 그 램프의 타이머를 먼저 취소한다 (원본)</summary>
        public void ChamberLampOffButton(EquipmentRegion region)
        {
            CancelTimedAction(TimedKey(region, false));
            SetChamberLamp(region, false);
        }

        /// <summary>원본 ControlChamberDoor: 닫기도 공정 중에 막는다 (TM이 챔버 안에 들어가 있을 수 있다)</summary>
        public void SetChamberDoor(EquipmentRegion region, bool open)
        {
            if (!EnsureManualIoAllowed(open ? "챔버 도어 열기" : "챔버 도어 닫기")) return;
            if (!EnsureConnected()) return;
            try
            {
                var (first, second) = DoorOutputs(region);
                if (open)
                {
                    // Chamber A는 특별 처리 (4=false, 5=true)
                    if (first == 4)
                    {
                        _device.Digital_Output(4, false);
                        _device.Digital_Output(5, true);
                    }
                    else
                    {
                        _device.Digital_Output(second, true);
                        _device.Digital_Output(first, false);
                    }
                }
                else
                {
                    // Chamber A는 특별 처리 (4=true, 5=false)
                    if (first == 4)
                    {
                        _device.Digital_Output(4, true);
                        _device.Digital_Output(5, false);
                    }
                    else
                    {
                        _device.Digital_Output(first, true);
                        _device.Digital_Output(second, false);
                    }
                }
                SetStatus($"Chamber {ChamberLetter(region)} 도어 {(open ? "열림" : "닫힘")}", ManualStatusTone.Success);
                OnDoorStateChanged?.Invoke(region, open);
            }
            catch (Exception ex)
            {
                Fail("도어 제어", ex);
            }
        }

        /// <summary>닫기 버튼: 그 도어의 타이머를 먼저 취소한다 (원본)</summary>
        public void ChamberDoorCloseButton(EquipmentRegion region)
        {
            CancelTimedAction(TimedKey(region, true));
            SetChamberDoor(region, false);
        }

        /// <summary>원본 ControlMainLamp (0 적색, 1 황색, 2 녹색). 공정 중에도 된다.</summary>
        public void SetMainLamp(int outputIndex, bool on)
        {
            if (!EnsureConnected()) return;
            try
            {
                _device.Digital_Output(outputIndex, on);
                string colorName = outputIndex == 0 ? "적색" : outputIndex == 1 ? "황색" : "녹색";
                SetStatus($"3색 램프 {colorName} {(on ? "ON" : "OFF")}", ManualStatusTone.Success);
                NotifyMainLampFromOutputs();
            }
            catch (Exception ex)
            {
                Fail("3색 램프 제어", ex);
            }
        }

        public void MainLampOffButton(int outputIndex)
        {
            CancelTimedAction(MainLampKey(outputIndex));
            SetMainLamp(outputIndex, false);
        }

        private void NotifyMainLampFromOutputs()
        {
            // 3색 램프 상태 읽기 (현재 상태 확인)
            bool red = false, yellow = false, green = false;
            try
            {
                red = _device.GetDigitalOutput(0);
                yellow = _device.GetDigitalOutput(1);
                green = _device.GetDigitalOutput(2);
            }
            catch
            {
            }
            OnMainLampStateChanged?.Invoke(red, yellow, green);
        }

        /// <summary>원본 ControlAllDoorsOpen / ControlAllDoorsClose</summary>
        public void SetAllDoors(bool open)
        {
            if (!EnsureManualIoAllowed(open ? "모든 문 열기" : "모든 문 닫기")) return;
            if (!EnsureConnected()) return;
            try
            {
                if (open)
                {
                    _device.Digital_Output(4, false);
                    _device.Digital_Output(5, true);
                    _device.Digital_Output(7, false);
                    _device.Digital_Output(8, true);
                    _device.Digital_Output(10, false);
                    _device.Digital_Output(11, true);
                }
                else
                {
                    _device.Digital_Output(4, true);
                    _device.Digital_Output(5, false);
                    _device.Digital_Output(7, true);
                    _device.Digital_Output(8, false);
                    _device.Digital_Output(10, true);
                    _device.Digital_Output(11, false);
                }
                SetStatus(open ? "모든 문이 열렸습니다." : "모든 문이 닫혔습니다.", ManualStatusTone.Success);
                OnDoorStateChanged?.Invoke(EquipmentRegion.ChamberA, open);
                OnDoorStateChanged?.Invoke(EquipmentRegion.ChamberB, open);
                OnDoorStateChanged?.Invoke(EquipmentRegion.ChamberC, open);
            }
            catch (Exception ex)
            {
                Fail(open ? "모든 문 열기" : "모든 문 닫기", ex);
            }
        }

        /// <summary>원본 ControlAllLampsOn / ControlAllLampsOff (끄기는 공정 중에도 된다)</summary>
        public void SetAllLamps(bool on)
        {
            if (on && !EnsureManualIoAllowed("모든 램프 켜기")) return;
            if (!EnsureConnected()) return;
            try
            {
                _device.Digital_Output(3, on);  // Chamber A
                _device.Digital_Output(6, on);  // Chamber B
                _device.Digital_Output(9, on);  // Chamber C
                _device.Digital_Output(0, on);  // 적색
                _device.Digital_Output(1, on);  // 황색
                _device.Digital_Output(2, on);  // 녹색
                SetStatus(on ? "모든 램프가 켜졌습니다." : "모든 램프가 꺼졌습니다.", ManualStatusTone.Success);
                OnChamberLampStateChanged?.Invoke(EquipmentRegion.ChamberA, on);
                OnChamberLampStateChanged?.Invoke(EquipmentRegion.ChamberB, on);
                OnChamberLampStateChanged?.Invoke(EquipmentRegion.ChamberC, on);
                NotifyMainLampFromOutputs();
            }
            catch (Exception ex)
            {
                Fail(on ? "모든 램프 켜기" : "모든 램프 끄기", ex);
            }
        }

        #endregion

        #region 실린더 · 진공 · 배기

        public void CylinderExtend()
        {
            if (!EnsureManualIoAllowed("실린더 전진")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(13, false); // 후진 OFF
                _device.Digital_Output(12, true);  // 전진 ON
                SetStatus("실린더 전진", ManualStatusTone.Success);
                OnCylinderStateChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                Fail("실린더 전진", ex);
            }
        }

        public void CylinderRetract()
        {
            if (!EnsureManualIoAllowed("실린더 후진")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(12, false); // 전진 OFF
                _device.Digital_Output(13, true);  // 후진 ON
                SetStatus("실린더 후진", ManualStatusTone.Success);
                OnCylinderStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                Fail("실린더 후진", ex);
            }
        }

        public void VacuumOn()
        {
            if (!EnsureManualIoAllowed("진공 ON")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(15, false); // 배기 OFF
                _device.Digital_Output(14, true);  // 진공 ON
                SetStatus("진공 ON (흡착)", ManualStatusTone.Success);
                OnVacuumStateChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                Fail("진공 ON", ex);
            }
        }

        /// <summary>진공 OFF 버튼: 진공 타이머를 먼저 취소한다 (원본)</summary>
        public void VacuumOff()
        {
            CancelTimedAction(VacuumKey);
            VacuumOffCore();
        }

        private void VacuumOffCore()
        {
            if (!EnsureManualIoAllowed("진공 OFF")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(14, false); // 진공 OFF
                SetStatus("진공 OFF", ManualStatusTone.Warning);
                OnVacuumStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                Fail("진공 OFF", ex);
            }
        }

        public void ExhaustOn()
        {
            if (!EnsureManualIoAllowed("배기 ON")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(14, false); // 진공 OFF
                _device.Digital_Output(15, true);  // 배기 ON
                SetStatus("배기 ON", ManualStatusTone.Primary);
            }
            catch (Exception ex)
            {
                Fail("배기 ON", ex);
            }
        }

        /// <summary>배기 OFF 버튼: 배기 타이머를 먼저 취소한다 (원본)</summary>
        public void ExhaustOff()
        {
            CancelTimedAction(ExhaustKey);
            ExhaustOffCore();
        }

        private void ExhaustOffCore()
        {
            if (!EnsureManualIoAllowed("배기 OFF")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Digital_Output(15, false); // 배기 OFF
                SetStatus("배기 OFF", ManualStatusTone.Warning);
            }
            catch (Exception ex)
            {
                Fail("배기 OFF", ex);
            }
        }

        #endregion

        #region 서보 · 원점복귀 · 티칭 · 긴급 정지

        /// <summary>원본 UpdateServoStatusFromDevice (0.5초마다)</summary>
        public void UpdateServoStatusFromDevice()
        {
            if (_isBusy) return; // 서보 ON·원점복귀 중에는 그 진행 글자를 둔다
            if (!IsConnected)
            {
                SetServoStatus("서보 상태: 미연결", ManualStatusTone.Secondary);
                return;
            }
            if (_device.IsEmergencyStopped)
            {
                SetServoStatus("서보 상태: 비상정지", ManualStatusTone.Danger);
                return;
            }
            try
            {
                // 원점복귀 상태 확인
                bool homedBySequence = IsHomedBySequence != null && IsHomedBySequence();
                bool axis1Home = homedBySequence || _device.Axis1_Status("HOME_D");
                bool axis2Home = homedBySequence || _device.Axis2_Status("HOME_D");
                bool isHomed = axis1Home && axis2Home;

                // 위치 데이터 확인 (서보 ON 여부 추정)
                string axis1Pos = _device.Axis1_is_PosData();
                bool hasPosition = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-";

                if (isHomed) SetServoStatus("서보 상태: ON+Homed", ManualStatusTone.Success);
                else if (hasPosition) SetServoStatus("서보 상태: ON", ManualStatusTone.Warning);
                else SetServoStatus("서보 상태: 대기", ManualStatusTone.Secondary);
            }
            catch
            {
                SetServoStatus("서보 상태: 확인 불가", ManualStatusTone.Secondary);
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            RaiseChanged();
        }

        /// <summary>원본 ControlServoOn: 설정 → 두 축 ON → 위치 값이 읽히는지 2초 동안 확인</summary>
        public Task ServoOnAsync()
        {
            if (!EnsureManualIoAllowed("서보 ON")) return Task.CompletedTask;
            if (!EnsureConnected("연결 필요")) return Task.CompletedTask;
            if (_device.IsEmergencyStopped)
            {
                ShowEmergencyStopActive();
                return Task.CompletedTask;
            }
            if (_isBusy) return Task.CompletedTask;

            SetStatus("서보 ON 중...", ManualStatusTone.Warning, log: false);
            SetBusy(true);
            var device = _device;
            // 메인 화면 서보 ON과 같은 설정 파일 값을 쓴다 (ServoDefault*). 인자 순서는 원본 그대로.
            long velocity = AppSettings.ServoDefaultVelocity;
            long maxVelocity = AppSettings.ServoDefaultMaxVelocity;
            long deceleration = AppSettings.ServoDefaultDeceleration;
            long acceleration = AppSettings.ServoDefaultAcceleration;

            Task task;
            try
            {
                task = HardwareOperation.Run(device, () =>
                {
                    try
                    {
                        device.Axis1_UD_Config_Update(velocity, maxVelocity, deceleration, acceleration);
                        device.Axis2_LR_Config_Update(velocity, maxVelocity, deceleration, acceleration);
                        device.Axis1_ON();
                        device.Axis2_ON();

                        Thread.Sleep(500);
                        bool servoOnConfirmed = false;
                        var servoCheckTimeout = DateTime.Now.AddSeconds(2);
                        while (DateTime.Now < servoCheckTimeout)
                        {
                            device.ThrowIfEmergencyStopped();
                            try
                            {
                                string axis1Pos = device.Axis1_is_PosData();
                                string axis2Pos = device.Axis2_is_PosData();
                                bool hasPosition = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" &&
                                                   !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-";
                                if (hasPosition)
                                {
                                    servoOnConfirmed = true;
                                    break;
                                }
                            }
                            catch (Exception checkEx) when (!(checkEx is EmergencyStopActiveException))
                            {
                                System.Diagnostics.Debug.WriteLine($"서보 ON 상태 확인 오류: {checkEx.Message}");
                            }
                            Thread.Sleep(100);
                        }

                        _post(() =>
                        {
                            if (_disposed) return;
                            SetBusy(false);
                            if (servoOnConfirmed)
                            {
                                SetStatus("서보 모터 ON", ManualStatusTone.Success);
                                SetServoStatus("서보 상태: ON", ManualStatusTone.Success);
                                OnServoStateChanged?.Invoke(true);
                            }
                            else
                            {
                                SetStatus("서보 ON 명령 전송 완료, 상태 확인 실패", ManualStatusTone.Warning);
                                SetServoStatus("서보 상태: 확인 불가", ManualStatusTone.Warning);
                                _showMessage(
                                    "서보 ON 명령을 전송했지만 상태 확인에 실패했습니다.\n\n" +
                                    "확인 사항:\n" +
                                    "1. 서보 모터 전원 확인\n" +
                                    "2. EtherCAT 연결 상태 확인\n" +
                                    "3. 서보 모터 상태 수동 확인",
                                    "서보 ON 확인", MessageKind.Warning);
                                OnServoStateChanged?.Invoke(false);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _post(() =>
                        {
                            if (_disposed) return;
                            SetBusy(false);
                            Fail("서보 ON", ex);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                // 비상정지 중이라 시작하지 못함 (HardwareOperation.Run)
                SetBusy(false);
                Fail("서보 ON", ex);
                return Task.CompletedTask;
            }
            return task;
        }

        /// <summary>원본 ControlServoOff (공정 중에는 막고 긴급 정지를 안내)</summary>
        public void ServoOff()
        {
            if (!EnsureManualIoAllowed("서보 OFF")) return;
            if (!EnsureConnected("연결 필요")) return;
            try
            {
                _device.Axis1_OFF();
                _device.Axis2_OFF();
                SetStatus("서보 모터 OFF", ManualStatusTone.Warning);
                SetServoStatus("서보 상태: OFF", ManualStatusTone.Warning);
                OnServoStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                Fail("서보 OFF", ex);
            }
        }

        /// <summary>
        /// 원본 ControlServoHome: 실린더 후진 확인 → 상하(Axis1) → 좌우(Axis2).
        /// WPF는 공정 리셋과 같은 <see cref="ResetHomingSequence"/>를 써서, 명령 직후 1초 동안 예전 HOME_D 값을 믿지 않고
        /// 좌우 원점복귀 직전에 실린더를 한 번 더 확인한다.
        /// </summary>
        public Task HomeAsync()
        {
            return HomeAsync(device => new ResetHomingSequence(device));
        }

        internal Task HomeAsync(Func<IEthercatDevice, ResetHomingSequence> createSequence)
        {
            if (!EnsureManualIoAllowed("원점복귀")) return Task.CompletedTask;
            if (!EnsureConnected("연결 필요")) return Task.CompletedTask;
            if (_device.IsEmergencyStopped)
            {
                ShowEmergencyStopActive();
                return Task.CompletedTask;
            }
            if (_isBusy) return Task.CompletedTask;

            try
            {
                bool cylinderRetracted = _device.Digital_Input(12);
                if (!cylinderRetracted)
                {
                    _showMessage("실린더가 후진 상태가 아닙니다.\n먼저 실린더를 후진시켜 주세요.", "인터락", MessageKind.Warning);
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                // 후진을 확인하지 못하면 회전할 수 있는지 알 수 없으므로 원점복귀를 하지 않는다.
                _showMessage($"실린더 후진 센서를 읽지 못했습니다: {ex.Message}\n원점복귀를 하지 않았습니다.", "인터락", MessageKind.Warning);
                return Task.CompletedTask;
            }

            SetStatus("원점복귀 시작 - 1단계: 상하(Axis1)", ManualStatusTone.Warning);
            SetServoStatus("원점복귀 중 (1/2)...", ManualStatusTone.Warning);
            SetBusy(true);

            var device = _device;
            Task task;
            try
            {
                task = HardwareOperation.Run(device, () =>
                {
                    try
                    {
                        string failReason = createSequence(device).Run(null, stage =>
                        {
                            if (stage != 2) return;
                            _post(() =>
                            {
                                if (_disposed) return;
                                SetStatus("원점복귀 - 2단계: 좌우(Axis2)", ManualStatusTone.Warning, log: false);
                                _servoStatusText = "원점복귀 중 (2/2)...";
                                RaiseChanged();
                            });
                        });

                        _post(() =>
                        {
                            if (_disposed) return;
                            _isBusy = false;
                            if (failReason != null)
                            {
                                SetStatus($"원점복귀 실패: {failReason}", ManualStatusTone.Danger);
                                SetServoStatus("원점복귀 실패", ManualStatusTone.Danger);
                                _showMessage(
                                    failReason + "\n\n" +
                                    "확인 사항:\n1. 서보 모터 전원 확인\n2. 실린더 후진 상태 확인\n3. EtherCAT 연결 상태 확인",
                                    "원점복귀 실패", MessageKind.Error);
                                return;
                            }

                            SetStatus("원점복귀 완료!", ManualStatusTone.Success);
                            SetServoStatus("원점복귀 완료", ManualStatusTone.Success);
                            OnHomingRequested?.Invoke();
                            _showMessage("원점복귀가 완료되었습니다.\n(상하 → 좌우 순서)", "완료", MessageKind.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        _post(() =>
                        {
                            if (_disposed) return;
                            _isBusy = false;
                            Fail("원점복귀", ex);
                            SetServoStatus("원점복귀 오류", ManualStatusTone.Danger);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _isBusy = false;
                Fail("원점복귀", ex);
                SetServoStatus("원점복귀 오류", ManualStatusTone.Danger);
                return Task.CompletedTask;
            }
            return task;
        }

        /// <summary>티칭 위치 버튼: 공정 중에 위치를 바꾸면 진행 중인 이송이 바뀐 위치로 움직이므로 막는다 (원본)</summary>
        public void RequestTeaching()
        {
            if (!EnsureManualIoAllowed("티칭 위치 편집")) return;
            OnTeachingRequested?.Invoke();
        }

        /// <summary>
        /// 원본 ControlEmergencyStop: 진행 중인 타이머를 취소하고 두 축 서보를 끈 뒤 메인 화면에 알린다.
        /// 출력(진공, 실린더, 도어)은 지금 상태 그대로 둔다. 연결과 관계없이 장비부터 멈춘다.
        /// </summary>
        public void EmergencyStop()
        {
            DisposeAllTimedActions();

            Exception stopError = null;
            if (_device != null)
            {
                try
                {
                    _device.EmergencyStop();
                }
                catch (Exception ex)
                {
                    stopError = ex;
                }
            }

            OnEmergencyStopRequested?.Invoke();

            SetStatus(stopError != null
                ? $"비상정지 서보 OFF 전송 오류: {stopError.Message} - 비상 차단 스위치로 전원을 끊으세요"
                : "비상정지 - 서보 OFF. 메인 화면의 알람 리셋으로 해제하세요.",
                ManualStatusTone.Danger, log: false);
            UpdateServoStatusFromDevice();
        }

        #endregion

        #region 타이머 (n초 ON 후 자동 OFF)

        /// <summary>원본 StartTimedAction: 초 입력 후 [실행] → 그 시간만큼 ON(열림) 후 자동 OFF(닫힘)</summary>
        public void StartTimedAction(string key, int seconds)
        {
            if (!_timedDefinitions.TryGetValue(key, out var def)) throw new ArgumentException("알 수 없는 타이머: " + key, nameof(key));
            string displayName = def.name;

            if (!EnsureManualIoAllowed(displayName + " 타이머")) return;
            if (!EnsureConnected()) return;
            if (seconds < MinTimerSeconds)
            {
                _showMessage("1초 이상으로 입력하세요.", "입력 오류", MessageKind.Warning);
                return;
            }
            if (seconds > MaxTimerSeconds)
            {
                _showMessage($"{MaxTimerSeconds}초 이하로 입력하세요.", "입력 오류", MessageKind.Warning);
                return;
            }

            CancelTimedAction(key);

            try
            {
                def.on?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus($"타이머 ON 실패: {ex.Message}", ManualStatusTone.Danger);
                return;
            }

            var info = new ManualTimedAction(key, displayName, seconds, _now().AddSeconds(seconds));
            IDisposable handle = null;
            handle = _schedule(seconds * 1000, () =>
            {
                // 이미 취소되었거나 다른 실행으로 바뀐 타이머면 무시
                if (!_timers.TryGetValue(key, out var current) || !ReferenceEquals(current.handle, handle)) return;
                CancelTimedAction(key);
                try
                {
                    def.off?.Invoke();
                    SetStatus($"{displayName} 타이머 완료 → OFF ({seconds}초)", ManualStatusTone.Warning);
                }
                catch (Exception ex)
                {
                    SetStatus($"{displayName} 타이머 OFF 오류: {ex.Message}", ManualStatusTone.Danger);
                }
            });
            _timers[key] = (handle, info);
            SetStatus($"{displayName} ON — {seconds}초 후 자동 OFF", ManualStatusTone.Warning);
        }

        public void CancelTimedAction(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_timers.TryGetValue(key, out var timer))
            {
                _timers.Remove(key);
                try
                {
                    timer.handle?.Dispose();
                }
                catch
                {
                }
                RaiseChanged();
            }
        }

        /// <summary>
        /// 도는 타이머를 모두 지금 끝낸다: 취소하고 OFF(닫기)를 바로 보낸다.
        /// WPF 추가: 창을 닫을 때 원본은 타이머만 취소해 ON(열림)이 그대로 남았다. 닫을 때 이것을 고를 수 있게 한다.
        /// </summary>
        public void FinishTimedActionsNow()
        {
            foreach (var key in _timers.Keys.ToList())
            {
                CancelTimedAction(key);
                if (_timedDefinitions.TryGetValue(key, out var def))
                {
                    try
                    {
                        def.off?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"{def.name} 타이머 OFF 오류: {ex.Message}", ManualStatusTone.Danger);
                    }
                }
            }
        }

        private void DisposeAllTimedActions()
        {
            foreach (var key in _timers.Keys.ToList())
            {
                CancelTimedAction(key);
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            DisposeAllTimedActions();
            _disposed = true;
        }
    }
}
