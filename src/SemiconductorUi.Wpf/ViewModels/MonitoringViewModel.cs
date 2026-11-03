using System;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Controls;
using SemiconductorUi.Wpf.Controls;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;
using WaferLoadStateType = SemiconductorUi.ViewModels.MainFormViewModel.WaferLoadStateType;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 운전 > 모니터링 탭. 원본 ProcessEventHandlers / WaferEventHandlers의 제어 패널 동작을 옮긴다.
    /// 공정 진행(TM 이송, 챔버 공정) 자체는 '공정 시퀀스 이관' 단계에서 연결한다.
    /// </summary>
    public sealed class MonitoringViewModel : ObservableObject
    {
        private readonly MainViewModel _shell;
        private readonly IDialogService _dialogs;

        public MonitoringViewModel(MainViewModel shell, IDialogService dialogs)
        {
            _shell = shell;
            _dialogs = dialogs;

            StartCommand = new RelayCommand(Start, () => State.IsLoggedIn);
            PauseCommand = new RelayCommand(Pause, () => State.IsLoggedIn);
            EmergencyStopCommand = new RelayCommand(EmergencyStop);
            ResetAlarmCommand = new RelayCommand(ResetAlarm, () => State.IsLoggedIn);
            ResetProcessCommand = new RelayCommand(ResetProcess, () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            ToggleFoupMountCommand = new RelayCommand(ToggleFoupMount, () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            WaferLoadingCommand = new RelayCommand(() => SetWaferLoadState(WaferLoadStateType.Loading), () => State.IsLoggedIn && State.IsFoupMounted);
            WaferUnloadingCommand = new RelayCommand(() => SetWaferLoadState(WaferLoadStateType.Unloading), () => State.IsLoggedIn && State.IsFoupMounted);
            NotYetPortedCommand = new RelayCommand(p => _shell.AddLog($"'{p}' 기능은 다음 단계에서 이관됩니다.", "INFO"));

            State.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(FoupMountText));
                OnPropertyChanged(nameof(WaferLoadText));
                OnPropertyChanged(nameof(PauseText));
                OnPropertyChanged(nameof(ElapsedText));
                if (e.PropertyName == nameof(State.CurrentProcessState)) ApplyLampForState();
                UpdateScene();
            };
            ApplyLampForState();
        }

        #region 장비 캔버스 / 램프

        // 원본 Form1UiUpdater.UpdateMainLampColors에서 쓰는 색 (Color.LimeGreen/ForestGreen/Goldenrod/Firebrick)
        private static readonly Color LimeGreen = Color.FromRgb(50, 205, 50);
        private static readonly Color Goldenrod = Color.FromRgb(218, 165, 32);
        private static readonly Color Firebrick = Color.FromRgb(178, 34, 34);

        private bool _lampGreen, _lampYellow, _lampRed;

        /// <summary>장비 캔버스에 그릴 상태</summary>
        public EquipmentScene Scene { get; } = new EquipmentScene();

        /// <summary>원본 Form1.SetProcessState의 램프 부분</summary>
        private void ApplyLampForState()
        {
            switch (State.CurrentProcessState)
            {
                case ProcessState.Running: SetMainLamp(true, false, false); break;
                case ProcessState.Error: SetMainLamp(false, false, true); break;
                default: SetMainLamp(false, true, false); break;
            }
        }

        /// <summary>
        /// 원본 Form1UiUpdater.UpdateMainLampColors: 화면 램프 + EtherCAT 연결 시 실제 3색 램프(DO 0=적, 1=황, 2=녹)
        /// </summary>
        private void SetMainLamp(bool green, bool yellow, bool red)
        {
            _lampGreen = green; _lampYellow = yellow; _lampRed = red;
            if (_shell.IsEthercatConnected && _shell.Ethercat != null)
            {
                try
                {
                    _shell.Ethercat.Digital_Output(0, red);
                    _shell.Ethercat.Digital_Output(1, yellow);
                    _shell.Ethercat.Digital_Output(2, green);
                }
                catch (Exception ex)
                {
                    _shell.AddLog($"3색 램프 제어 오류: {ex.Message}", "ERROR");
                }
            }
            UpdateScene();
        }

        /// <summary>뷰모델 상태를 장비 캔버스 상태로 옮긴다.</summary>
        public void UpdateScene()
        {
            var s = Scene;
            s.LampGreen = _lampGreen ? LimeGreen : Rendering.Palette.LampOff;
            s.LampYellow = _lampYellow ? Goldenrod : Rendering.Palette.LampOff;
            s.LampRed = _lampRed ? Firebrick : Rendering.Palette.LampOff;

            int capacity = AppSettings.MaxFoupCapacity;
            s.FoupA.Capacity = capacity;
            s.FoupB.Capacity = capacity;
            s.FoupA.Mounted = State.IsFoupAMounted;
            s.FoupB.Mounted = State.IsFoupBMounted;
            s.FoupA.WaferCount = State.CurrentFoupACount;
            s.FoupB.WaferCount = State.CurrentFoupBCount;

            foreach (var region in new[] { EquipmentRegion.ChamberA, EquipmentRegion.ChamberB, EquipmentRegion.ChamberC })
            {
                var c = s.Chambers[region];
                c.ValveOpen = State.IsDoorOpen(region) ? 1 : 0;
                // TODO(4단계): 원본 UpdateChamberLampUI (공정 중 녹색, 완료 시 깜빡임)은 챔버 공정 이관 때 연결
                c.LampColor = Rendering.Palette.LampOff;
            }

            s.TmTarget = State.TmVisualTarget;
            s.TmCarrying = State.TmCarryingVisual;
            s.TmExtension = State.TmBladeExtensionFactor;
            s.IsSimulation = !_shell.IsEthercatConnected;

            var side = s.SideView;
            side.Target = State.TmVisualTarget;
            side.WaferOnBlade = State.TmCarryingVisual;
            side.VacuumOn = State.TmCarryingVisual;
            side.Extension = State.TmBladeExtensionFactor >= 1.0f ? 1 : 0;
            side.IsHardware = _shell.IsEthercatConnected;

            s.NotifyChanged();
        }

        #endregion

        public MainViewModel Shell => _shell;

        public SemiconductorUi.ViewModels.MainFormViewModel State => _shell.State;

        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand EmergencyStopCommand { get; }
        public ICommand ResetAlarmCommand { get; }
        public ICommand ResetProcessCommand { get; }
        public ICommand ToggleFoupMountCommand { get; }
        public ICommand WaferLoadingCommand { get; }
        public ICommand WaferUnloadingCommand { get; }
        public ICommand NotYetPortedCommand { get; }

        public string FoupMountText => State.IsFoupMounted ? "FOUP 장착됨" : "FOUP 미장착";

        public string WaferLoadText
        {
            get
            {
                switch (State.WaferLoadState)
                {
                    case WaferLoadStateType.Loading: return "로딩 상태";
                    case WaferLoadStateType.Unloading: return "언로딩 상태";
                    default: return "대기";
                }
            }
        }

        public string PauseText => State.CurrentProcessState == ProcessState.Paused ? "재개" : "일시 정지";

        public string ElapsedText => TimeSpan.FromSeconds(_shell.Simulation.ElapsedSeconds).ToString(@"hh\:mm\:ss");

        /// <summary>장비 그림에서 챔버/FOUP 클릭 (원본: PmDetailForm/EquipmentDetailForm 열기)</summary>
        public void OpenRegionDetail(EquipmentRegion region)
        {
            // TODO(5단계): PmDetailForm / EquipmentDetailForm 이관
            _shell.AddLog($"{region} 상세 화면은 다음 단계에서 이관됩니다.", "INFO");
        }

        /// <summary>공정 타이머 Tick (UI 스레드)</summary>
        internal void OnTick()
        {
            OnPropertyChanged(nameof(ElapsedText));
            // 원본 TimerEventHandlers: 시뮬레이션 모드에서 진행 중이면 녹색 램프를 깜빡인다.
            if (!_shell.IsEthercatConnected && State.CurrentProcessState == ProcessState.Running)
            {
                State.MainLampBlinkState = !State.MainLampBlinkState;
                SetMainLamp(State.MainLampBlinkState, false, false);
            }
        }

        private void Start()
        {
            if (!_shell.EnsureLoggedIn()) return;

            if (_shell.IsEmergencyStopped)
            {
                _dialogs.Warn("비상정지 중입니다.\n\n알람 리셋으로 비상정지를 해제하고, 서보 ON과 원점복귀를 한 뒤 시작하세요.", "비상정지 중");
                return;
            }

            var sim = _shell.Simulation;
            if (sim.IsRunning && !sim.IsPaused)
            {
                _dialogs.Info("이미 공정이 진행 중입니다.");
                return;
            }

            if (!sim.IsRunning)
            {
                if (!State.IsFoupAMounted || !State.IsFoupBMounted)
                {
                    _dialogs.Warn("FOUP A/B 장착 상태를 확인해 주세요.", "준비 미완료");
                    return;
                }
                if (State.WaferLoadState != WaferLoadStateType.Loading)
                {
                    _dialogs.Warn("웨이퍼를 '로딩' 상태로 전환한 뒤 공정을 시작할 수 있습니다.", "준비 미완료");
                    return;
                }
                if (!_shell.IsEthercatConnected &&
                    !_dialogs.Confirm("EtherCAT가 연결되지 않았습니다.\n시뮬레이션 모드로 동작합니다.\n계속하시겠습니까?", "EtherCAT 미연결"))
                {
                    return;
                }
            }

            State.HasAlarm = false;
            if (sim.IsRunning && sim.IsPaused)
            {
                sim.Resume(_shell.IsEthercatConnected);
                _shell.SetProcessState(ProcessState.Running, "공정을 재개합니다.");
            }
            else
            {
                sim.Start(_shell.IsEthercatConnected);
                _shell.SetProcessState(ProcessState.Running, "공정을 시작합니다.");
            }
            RelayCommand.Refresh();
        }

        private void Pause()
        {
            if (!_shell.EnsureLoggedIn()) return;
            var sim = _shell.Simulation;

            if (sim.IsRunning && sim.IsPaused)
            {
                Start();
                return;
            }
            if (!sim.IsRunning)
            {
                _dialogs.Info("진행 중인 공정이 없습니다.");
                return;
            }

            if (_shell.HardwareManager.IsTmHardwareInitialized)
            {
                _shell.AddLog("하드웨어 일시정지: 새 명령 중지 (서보 ON 유지)", "WARN");
            }
            sim.Pause();
            _shell.SetProcessState(ProcessState.Paused, "공정을 일시 정지했습니다.");
            RelayCommand.Refresh();
        }

        private void EmergencyStop()
        {
            _shell.ExecuteEmergencyStop("메인 화면 긴급 정지 버튼");
            _dialogs.Warn(
                "비상정지가 실행되었습니다.\n\n" +
                "• 서보: OFF (두 축)\n" +
                "• 진공: 유지 (웨이퍼를 잡고 있으면 그대로)\n" +
                "• 실린더, 도어: 현재 상태 유지\n" +
                "• 장비 명령: 해제 전까지 차단\n\n" +
                "다시 움직이려면:\n" +
                "1. 장비 상태 확인\n" +
                "2. 알람 리셋으로 비상정지 해제\n" +
                "3. 서보 ON → 원점복귀",
                "비상정지");
            RelayCommand.Refresh();
        }

        private void ResetAlarm()
        {
            if (!_shell.EnsureLoggedIn()) return;

            bool emergencyStopCleared = false;
            if (_shell.IsEmergencyStopped)
            {
                bool yes = _dialogs.Confirm(
                    "비상정지를 해제하시겠습니까?\n\n" +
                    "해제하기 전에 확인하세요:\n" +
                    "• 장비 근처에 사람이 없는지\n" +
                    "• 웨이퍼, 블레이드, 실린더 위치\n\n" +
                    "해제해도 서보는 꺼진 채로 남습니다.\n" +
                    "해제한 뒤 서보 ON → 원점복귀를 다시 하세요.",
                    "비상정지 해제");
                if (!yes) return;

                _shell.Ethercat.ClearEmergencyStop();
                emergencyStopCleared = true;
                _shell.AddLog("비상정지 해제 (서보 OFF 유지, 서보 ON과 원점복귀 필요)", "WARN");
                // TODO(공정 시퀀스 이관 단계): ResetServoFaultsAfterEmergencyStopCleared 이관
            }

            if (!State.HasAlarm && !emergencyStopCleared)
            {
                _dialogs.Info("해제할 알람이 없습니다.");
                return;
            }

            State.ClearAllChamberAlarms();
            State.HasAlarm = false;
            State.VerificationAlarmDismissed = true;
            _shell.ClearAlarmBanner();
            if (State.CurrentProcessState == ProcessState.Error)
            {
                _shell.SetProcessState(ProcessState.Idle, "알람을 리셋했습니다.");
            }
            RelayCommand.Refresh();
        }

        private void ResetProcess()
        {
            if (!_shell.EnsureLoggedIn()) return;
            if (!_dialogs.Confirm("공정을 초기 상태로 리셋하시겠습니까?", "공정 리셋")) return;

            // TODO(공정 시퀀스 이관 단계): 원점복귀(ResetHomingSequence), FOUP/챔버 상태 초기화 연결
            _shell.Simulation.Reset();
            State.WaferLoadState = WaferLoadStateType.None;
            _shell.SetProcessState(ProcessState.Idle, "공정을 리셋했습니다.");
            OnPropertyChanged(nameof(ElapsedText));
            RelayCommand.Refresh();
        }

        private void ToggleFoupMount()
        {
            bool mount = !State.IsFoupMounted;
            State.IsFoupMounted = mount;
            State.IsFoupAMounted = mount;
            State.IsFoupBMounted = mount;
            if (!mount) State.WaferLoadState = WaferLoadStateType.None;
            _shell.AddLog(mount ? "FOUP A/B 장착" : "FOUP A/B 해제", "INFO");
            RelayCommand.Refresh();
        }

        private void SetWaferLoadState(WaferLoadStateType state)
        {
            State.WaferLoadState = state;
            _shell.AddLog($"웨이퍼 {(state == WaferLoadStateType.Loading ? "로딩" : "언로딩")} 상태로 전환", "INFO");
        }
    }
}
