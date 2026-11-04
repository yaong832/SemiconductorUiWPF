using System;
using System.Collections.Generic;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// 원본 Form1의 공정 진행 코드(스케줄링, TM 단계, 챔버 공정, TM 하드웨어 동작, 원점복귀)를 UI 없이 옮긴 것.
    /// 메서드 본문은 원본과 같게 두고, 화면 호출만 <see cref="IProcessHost"/>로 바꿨다.
    /// 모든 공개 메서드는 UI 스레드에서 부른다 (원본과 같음).
    /// </summary>
    public sealed partial class ProcessEngine : IDisposable
    {
        internal static readonly ChamberController.SimulationStepDefinition[] ChamberProcessFlow =
        {
            new ChamberController.SimulationStepDefinition("PR 도포", 30),
            new ChamberController.SimulationStepDefinition("노광 (Exposure-B)", 45),
            new ChamberController.SimulationStepDefinition("노광 (Exposure-C)", 45)
        };

        internal readonly TmProcessor tmProcessor;
        internal readonly FoupManager foupManager;
        internal readonly WaferTrackingService waferTrackingService;
        private readonly ITickTimer _servoFaultTimer;
        private readonly ServoFaultMonitor _servoFaultMonitor;

        public ProcessEngine(MainFormViewModel viewModel, IEthercatDevice device, HardwareManager hardwareManager,
            SimulationController simulation, IProcessHost host, ITickTimer servoFaultTimer = null)
        {
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            EtherCAT_M = device;
            HardwareManager = hardwareManager ?? throw new ArgumentNullException(nameof(hardwareManager));
            SimulationService = simulation ?? throw new ArgumentNullException(nameof(simulation));
            Host = host ?? throw new ArgumentNullException(nameof(host));

            ChamberService = new ChamberController(ChamberProcessFlow);
            TransferService = new TransferController();
            ServiceContainer.Register<IChamberService>(ChamberService);
            ServiceContainer.Register<ITransferService>(TransferService);

            foupManager = new FoupManager(this);
            waferTrackingService = new WaferTrackingService(this);
            tmProcessor = new TmProcessor(this);
            _servoFaultMonitor = new ServoFaultMonitor(device);

            // 원본 Form1 생성자: 서보 드라이브 알람 감시 타이머 (연결 중에만 확인)
            _servoFaultTimer = servoFaultTimer;
            if (_servoFaultTimer != null)
            {
                _servoFaultTimer.Interval = ServoFaultPollIntervalMs;
                _servoFaultTimer.Tick += ServoFaultTimer_Tick;
                _servoFaultTimer.Start();
            }
        }

        public IProcessHost Host { get; }
        public MainFormViewModel ViewModel { get; }
        internal IEthercatDevice EtherCAT_M { get; }
        public HardwareManager HardwareManager { get; }

        internal IChamberService ChamberService { get; }
        internal ITransferService TransferService { get; }
        internal ISimulationService SimulationService { get; }

        public ChamberController.ChamberState ChamberAState => ChamberService.ChamberA;
        public ChamberController.ChamberState ChamberBState => ChamberService.ChamberB;
        public ChamberController.ChamberState ChamberCState => ChamberService.ChamberC;
        private ChamberController chamberController => ChamberService as ChamberController;
        internal TransferController TransferController => TransferService as TransferController;
        public SimulationController SimulationController => SimulationService as SimulationController;
        public bool SimulationRunning => SimulationService.IsRunning;
        public bool SimulationPaused => SimulationService.IsPaused;

        public TransferController.TransferTask CurrentTransfer => TransferService.CurrentTransfer;
        public TransferController.TmPhase TmPhase => TransferService.CurrentPhase;
        internal int TmPhaseTicksRemaining => TransferService.PhaseTicksRemaining;
        public int TransferQueueCount => TransferService.QueueCount;

        public FoupManager Foups => foupManager;

        public bool EthercatConnected => HardwareManager.IsEthercatConnected;
        internal TmHardwareController TmHardwareController => HardwareManager.TmHardwareController;
        internal bool TmHardwareInitialized
        {
            get => HardwareManager.IsTmHardwareInitialized;
            set => HardwareManager.SetTmHardwareInitialized(value);
        }

        // 원본에서 헤더/패널 갱신 중 하드웨어 램프 출력을 막던 플래그 (WPF에는 동기화 단계가 없어 늘 false)
        internal bool isSyncingEquipmentState = false;

        /// <summary>공정 시작 시 서보/원점복귀 백그라운드 초기화 중</summary>
        private volatile bool _hardwareInitInProgress;
        public bool IsHardwareInitInProgress => _hardwareInitInProgress;

        // 알람 발생 전 프로세스 상태 저장 (알람 리셋 시 원래 상태로 복귀하기 위함)
        internal ProcessState? _stateBeforeAlarm = null;

        #region 원본 Form1 상태 속성 (ViewModel로 매핑)

        internal ProcessState CurrentProcessState { get => ViewModel.CurrentProcessState; set => ViewModel.CurrentProcessState = value; }
        internal bool HasAlarm { get => ViewModel.HasAlarm; set => ViewModel.HasAlarm = value; }
        internal bool IsFoupAMounted => ViewModel.IsFoupAMounted;
        internal bool IsFoupBMounted => ViewModel.IsFoupBMounted;
        internal int ConfiguredFoupALoadCount { get => ViewModel.ConfiguredFoupALoadCount; set => ViewModel.ConfiguredFoupALoadCount = value; }
        internal int FoupARemainingInventoryCount { get => ViewModel.FoupARemainingInventoryCount; set => ViewModel.FoupARemainingInventoryCount = value; }
        internal MainFormViewModel.WaferLoadStateType WaferLoadState { get => ViewModel.WaferLoadState; set => ViewModel.WaferLoadState = value; }
        internal int UserWaferLoadCount { get => ViewModel.UserWaferLoadCount; set => ViewModel.UserWaferLoadCount = value; }
        internal int activeBatchTargetCount { get => ViewModel.ActiveBatchTargetCount; set => ViewModel.ActiveBatchTargetCount = value; }
        internal int CurrentRecipeWaferCount { get => ViewModel.CurrentRecipeWaferCount; set => ViewModel.CurrentRecipeWaferCount = value; }
        internal string StatusProcessText { get => ViewModel.StatusProcessText; set => ViewModel.StatusProcessText = value; }
        internal string StatusProcessDetail { get => ViewModel.StatusProcessDetail; set => ViewModel.StatusProcessDetail = value; }
        private string statusPressureText { get => ViewModel.StatusPressureText; set => ViewModel.StatusPressureText = value; }
        private string statusTemperatureText { get => ViewModel.StatusTemperatureText; set => ViewModel.StatusTemperatureText = value; }
        internal string StatusDoorText { get => ViewModel.StatusDoorText; set => ViewModel.StatusDoorText = value; }
        internal int chamberCompletedCountA { get => ViewModel.ChamberCompletedCountA; set => ViewModel.ChamberCompletedCountA = value; }
        internal int chamberCompletedCountB { get => ViewModel.ChamberCompletedCountB; set => ViewModel.ChamberCompletedCountB = value; }
        internal int chamberCompletedCountC { get => ViewModel.ChamberCompletedCountC; set => ViewModel.ChamberCompletedCountC = value; }
        internal EquipmentRegion TmVisualTarget { get => ViewModel.TmVisualTarget; set => ViewModel.TmVisualTarget = value; }
        internal bool TmCarryingVisual { get => ViewModel.TmCarryingVisual; set => ViewModel.TmCarryingVisual = value; }
        internal EquipmentRegion TmCurrentPosition { get => ViewModel.TmCurrentPosition; set => ViewModel.TmCurrentPosition = value; }
        internal float TmBladeExtensionFactor { get => ViewModel.TmBladeExtensionFactor; set => ViewModel.TmBladeExtensionFactor = value; }
        internal bool mainLampBlinkState { get => ViewModel.MainLampBlinkState; set => ViewModel.MainLampBlinkState = value; }
        public bool SecondExposureEnabled { get => ViewModel.SecondExposureEnabled; set => ViewModel.SecondExposureEnabled = value; }

        internal int FoupBCompletedBaseline
        {
            get => foupManager.FoupBCompletedBaseline;
            set => foupManager.FoupBCompletedBaseline = value;
        }

        #endregion

        #region 화면에 보여 줄 램프 상태 (원본 panelMainLamp*, panelLampChamber*)

        /// <summary>메인 3색 램프. 녹색은 깜빡임 때문에 null(꺼짐)일 수 있다.</summary>
        public ArgbColor? MainLampGreen { get; private set; }
        public bool MainLampYellow { get; private set; }
        public bool MainLampRed { get; private set; }

        private readonly Dictionary<EquipmentRegion, bool> _chamberLampOn = new Dictionary<EquipmentRegion, bool>
        {
            { EquipmentRegion.ChamberA, false }, { EquipmentRegion.ChamberB, false }, { EquipmentRegion.ChamberC, false },
        };
        private readonly Dictionary<EquipmentRegion, int> _chamberLampBlinkCounters = new Dictionary<EquipmentRegion, int>();
        private readonly Dictionary<EquipmentRegion, bool> _chamberLampBlinkStates = new Dictionary<EquipmentRegion, bool>();
        private const int CHAMBER_LAMP_BLINK_INTERVAL = 10; // 10번 갱신마다 깜빡임 (원본과 같음)

        /// <summary>챔버 램프가 켜져 있는지 (공정 중 녹색, 완료 후 깜빡임)</summary>
        public bool IsChamberLampOn(EquipmentRegion region) => _chamberLampOn.TryGetValue(region, out var on) && on;

        #endregion

        // 원본 Color.LimeGreen
        private static readonly ArgbColor LimeGreen = ArgbColor.FromRgb(50, 205, 50);

        // 드라이브 상태는 DLL 수신 타이머(300ms)로 갱신되므로 그보다 충분히 뒤에 확인합니다.
        private const int EmergencyStopVerifyDelayMs = 1000;
        // 서보 알람 감시 주기. 드라이브 상태는 DLL 수신 타이머(300ms)로 갱신되므로 그보다 짧게 둡니다.
        private const int ServoFaultPollIntervalMs = 100;
        // 드라이브 알람 리셋 뒤 상태가 다시 읽힐 때까지 기다리는 시간.
        private const int ServoFaultResetVerifyDelayMs = 1000;
        private static readonly int[] ServoAxes = { 1, 2 };

        /// <summary>원본 Form1Initializer.InitializeSimulationState</summary>
        private void InitializeSimulationStateCore()
        {
            SimulationService?.Reset();
            activeBatchTargetCount = 0;

            // 로딩 상태에서는 사용자가 설정한 FOUP A 큐를 유지
            if (WaferLoadState != MainFormViewModel.WaferLoadStateType.Loading)
            {
                foupManager?.Reset();
            }

            chamberCompletedCountA = 0;
            chamberCompletedCountB = 0;
            chamberCompletedCountC = 0;

            ChamberService.ResetChamberStates();
            TmVisualTarget = EquipmentRegion.TM;
            TmCarryingVisual = false;
            TransferService.ClearQueue();
            TmCurrentPosition = EquipmentRegion.TM;
            TmBladeExtensionFactor = 0.55f;
            UpdateTmAnimationIdleTarget();
            ApplyConfiguredFoupCountsFromState();
            ResetDoorStates();
        }

        /// <summary>원본 Form1Configurator.ApplyConfiguredFoupCountsFromState</summary>
        internal void ApplyConfiguredFoupCountsFromState()
        {
            switch (WaferLoadState)
            {
                case MainFormViewModel.WaferLoadStateType.Loading:
                    ConfiguredFoupALoadCount = Math.Min(UserWaferLoadCount, AppSettings.MaxFoupCapacity);
                    FoupARemainingInventoryCount = ConfiguredFoupALoadCount;
                    break;
                default:
                    ConfiguredFoupALoadCount = 0;
                    FoupARemainingInventoryCount = 0;
                    break;
            }
        }

        #region 서보 상태 표시 (원본 labelServoStatus)

        public enum ServoStatusLevel { Secondary, Success, Warning, Danger, Off }

        public string ServoStatusText { get; private set; } = "서보: - | Home: -";
        public ServoStatusLevel ServoStatus { get; private set; } = ServoStatusLevel.Secondary;

        /// <summary>서보가 ON인지 (원본 Form1.IsServoOn, 화면 상태 플래그)</summary>
        public bool ServoOn { get => IsServoOn; set { IsServoOn = value; } }

        private void SetServoStatus(string text, ServoStatusLevel level)
        {
            ServoStatusText = text;
            ServoStatus = level;
            Host.RefreshServoStatus();
        }

        /// <summary>원본 Form1UiUpdater.UpdateServoStatusLabel</summary>
        private void RefreshServoStatusText()
        {
            try
            {
                if (EtherCAT_M != null && EtherCAT_M.IsEmergencyStopped)
                {
                    SetServoStatus("서보: 비상정지 | 알람 리셋 필요", ServoStatusLevel.Danger);
                    return;
                }

                if (!EthercatConnected)
                {
                    SetServoStatus("서보: - | Home: -", ServoStatusLevel.Secondary);
                    return;
                }

                bool isServoOn = IsServoOn;  // UI 상태 플래그 우선 사용
                bool axis1Homed = false;
                bool axis2Homed = false;
                bool isHomed = false;

                if (isServoOn && EtherCAT_M != null)
                {
                    try
                    {
                        string axis1Pos = EtherCAT_M.Axis1_is_PosData();
                        string axis2Pos = EtherCAT_M.Axis2_is_PosData();
                        bool hasPosition = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" &&
                                          !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-";
                        if (!hasPosition)
                        {
                            isServoOn = false;
                            IsServoOn = false;
                            TmHardwareInitialized = false;
                            AddLogMessage("서보 상태 확인: 하드웨어는 OFF 상태입니다", "WARN");
                        }
                        else
                        {
                            // 원점복귀 시퀀스가 끝났으면 완료로 본다 (HOME_D는 다음 이동 명령 뒤 꺼질 수 있음)
                            bool homedBySequence = TmHardwareInitialized;
                            axis1Homed = homedBySequence || EtherCAT_M.Axis1_Status("HOME_D");
                            axis2Homed = homedBySequence || EtherCAT_M.Axis2_Status("HOME_D");
                            isHomed = axis1Homed && axis2Homed;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLogMessage($"서보 상태 확인 오류: {ex.Message}", "WARN");
                    }
                }

                if (isServoOn && isHomed)
                {
                    SetServoStatus("서보: ON | Home: 완료", ServoStatusLevel.Success);
                }
                else if (isServoOn)
                {
                    string text;
                    if (!axis1Homed && !axis2Homed) text = "서보: ON | Home: 미완료 (Axis1,2)";
                    else if (!axis1Homed) text = "서보: ON | Home: 미완료 (Axis1)";
                    else if (!axis2Homed) text = "서보: ON | Home: 미완료 (Axis2)";
                    else text = "서보: ON | Home: 미완료";
                    SetServoStatus(text, ServoStatusLevel.Warning);
                }
                else
                {
                    SetServoStatus("서보: OFF | Home: -", ServoStatusLevel.Off);
                }
            }
            catch (Exception ex)
            {
                SetServoStatus("서보: 오류", ServoStatusLevel.Danger);
                AddLogMessage($"서보 상태 확인 오류: {ex.Message}", "WARN");
            }
        }

        #endregion

        /// <summary>원본 Form1UiUpdater.UpdateTmVisualizationFromHardware 중 상태 계산 부분</summary>
        private void UpdateTmVisualizationFromHardwareCore()
        {
            if (TmHardwareController == null || !EthercatConnected) return;

            try
            {
                TmHardwareController.UpdateCurrentPositions();
                long currentX = TmHardwareController.CurrentAxis2Position;
                long currentY = TmHardwareController.CurrentAxis1Position;
                var hardwareRegion = EquipmentRegionHelper.DetermineRegionFromPosition(currentX, currentY, TmHardwareController.Positions);

                bool carrying = TmHardwareController.IsVacuumOn;
                TmCarryingVisual = carrying;
                TmVisualTarget = hardwareRegion;

                bool cylinderExtended = CheckTmCylinderExtended();
                bool cylinderRetracted = CheckTmCylinderRetracted();
                if (cylinderExtended && !cylinderRetracted)
                {
                    TmBladeExtensionFactor = 1.3f;
                }
                else if (cylinderRetracted && !cylinderExtended)
                {
                    TmBladeExtensionFactor = 0.55f;
                }
                else
                {
                    TmBladeExtensionFactor = 0.7f; // 중간값
                }

                HardwareAxis1Position = currentY;
                Host.RefreshUi();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TM 시각화 업데이트 오류: {ex.Message}");
            }
        }

        /// <summary>하드웨어 모드에서 읽은 상하(Axis1) 위치 (TM 옆면 그림용)</summary>
        public long HardwareAxis1Position { get; private set; }

        /// <summary>화면에 그릴 블레이드 웨이퍼 여부 (하드웨어: 진공 또는 로직 상태)</summary>
        public bool IsWaferShownOnBlade => (IsTmHardwareModeAvailable() && TmCarryingVisual) || IsWaferOnBlade();

        internal void AddLogMessage(string message, string level = "INFO") => Host.AddLog(message, level);

        internal void Invoke(Action action) => Host.Invoke(action);

        public void UpdateSimulationUi()
        {
            // 원본 Form1UiUpdater.UpdateSimulationUi 중 화면과 무관한 상태 갱신 부분
            GetFoupDisplayCounts(out var displayFoupACount, out var displayFoupBCount);
            var exchangeState = SimulationRunning
                ? (SimulationPaused ? "이송 대기 (Pause)" : "TM 이송 중")
                : GetWaferLoadStateDisplay();
            ViewModel.CurrentFoupExchangeState = exchangeState;
            ViewModel.CurrentFoupQueueInfo = $"웨이퍼 상태: {GetWaferLoadStateDisplay()} · 완료 {displayFoupBCount}/{Math.Max(1, GetActiveTarget())}";
            ViewModel.CurrentFoupACount = displayFoupACount;
            ViewModel.CurrentFoupBCount = displayFoupBCount;

            var closedDoors = 0;
            if (ChamberAState.CurrentWafer != null) closedDoors++;
            if (ChamberBState.CurrentWafer != null) closedDoors++;
            if (ChamberCState.CurrentWafer != null) closedDoors++;
            StatusDoorText = closedDoors == 0 ? "문 상태: 모두 개방" : $"문 상태: {closedDoors}개 닫힘";
            StatusProcessDetail = $"{StatusProcessText} | 완료 {foupManager.GetFoupBCount()}/{AppSettings.MaxFoupCapacity}";

            UpdateChamberProcessLamps();
            Host.RefreshUi();
        }

        /// <summary>원본 Form1.GetWaferLoadStateDisplay</summary>
        public string GetWaferLoadStateDisplay()
        {
            switch (WaferLoadState)
            {
                case MainFormViewModel.WaferLoadStateType.Loading:
                    return $"로딩 ({UserWaferLoadCount}장)";
                case MainFormViewModel.WaferLoadStateType.Unloading:
                    return "언로딩";
                default:
                    return "대기";
            }
        }

        /// <summary>원본 TM 상태 문구 (UpdateSimulationUi의 tmStatus)</summary>
        public string GetTmStatusText()
        {
            if (!SimulationRunning)
            {
                return CurrentProcessState == ProcessState.Error ? "Error" : "대기";
            }
            if (SimulationPaused)
            {
                return "Paused";
            }
            var pickupLabel = EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer != null ? CurrentTransfer.Pickup : TmVisualTarget);
            var dropoffLabel = EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer != null ? CurrentTransfer.Dropoff : TmVisualTarget);
            switch (TmPhase)
            {
                case TransferController.TmPhase.MoveToPickup: return $"이동(픽업) → {pickupLabel}";
                case TransferController.TmPhase.WaitDoorPickupOpen: return $"{pickupLabel} 문 열림 대기";
                case TransferController.TmPhase.PickupExtend: return $"Arm 전진(픽업) → {pickupLabel}";
                case TransferController.TmPhase.PickupRetract: return $"Arm 후퇴(픽업) → {pickupLabel}";
                case TransferController.TmPhase.WaitDoorPickupClose: return $"{pickupLabel} 문 닫힘 대기";
                case TransferController.TmPhase.MoveToDropoff: return $"이동(반입) → {dropoffLabel}";
                case TransferController.TmPhase.WaitDoorDropoffOpen: return $"{dropoffLabel} 문 열림 대기";
                case TransferController.TmPhase.DropoffExtend: return $"Arm 전진(반입) → {dropoffLabel}";
                case TransferController.TmPhase.DropoffRetract: return $"Arm 후퇴(반입) → {dropoffLabel}";
                case TransferController.TmPhase.WaitDoorDropoffClose: return $"{dropoffLabel} 문 닫힘 대기";
                default: return TransferQueueCount > 0 ? "Transfer 준비" : "대기";
            }
        }

        private void UpdateProcessControlButtons() => Host.RefreshServoStatus();

        #region 알람 홀드 (원본 Form1.AlarmHold.cs)

        /// <summary>환경 알람으로 공정을 붙잡고 있는지. 알람 리셋 뒤에도 시작을 누를 때까지 true입니다.</summary>
        public bool ProcessHeldByAlarm { get; private set; }

        /// <summary>환경 알람이 났을 때 부릅니다. 공정 중이 아니면 아무것도 하지 않습니다.</summary>
        public void HoldProcessForAlarm(string unitKey)
        {
            if (!SimulationRunning || ProcessHeldByAlarm)
            {
                return;
            }
            ProcessHeldByAlarm = true;
            string transferNote = CurrentTransfer != null
                ? "진행 중인 TM 이송은 끝까지 마칩니다."
                : "TM은 대기 위치에 있습니다.";
            AddLogMessage(
                $"{EquipmentRegionHelper.FormatUnitLabel(unitKey)} 환경 알람: 공정을 멈춥니다. 새 이송과 챔버 공정 시간을 멈추고, {transferNote} 알람 리셋 후 시작을 누르면 이어 갑니다.",
                "WARN");
        }

        public void ClearAlarmHold()
        {
            ProcessHeldByAlarm = false;
        }

        #endregion

        public void Dispose()
        {
            if (_servoFaultTimer != null)
            {
                _servoFaultTimer.Stop();
                _servoFaultTimer.Tick -= ServoFaultTimer_Tick;
                (_servoFaultTimer as IDisposable)?.Dispose();
            }
        }
    }
}
