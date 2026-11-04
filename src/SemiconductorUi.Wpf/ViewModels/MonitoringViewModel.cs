using System;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Controls;
using SemiconductorUi.Models;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Controls;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;
using WaferLoadStateType = SemiconductorUi.ViewModels.MainFormViewModel.WaferLoadStateType;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 운전 > 모니터링 탭. 제어 패널 버튼은 Core 공정 엔진(원본 ProcessEventHandlers)으로 넘기고,
    /// 엔진 상태를 장비 그림(EquipmentScene)으로 옮긴다.
    /// </summary>
    public sealed class MonitoringViewModel : ObservableObject
    {
        private readonly MainViewModel _shell;
        private readonly IDialogService _dialogs;

        public MonitoringViewModel(MainViewModel shell, IDialogService dialogs)
        {
            _shell = shell;
            _dialogs = dialogs;

            StartCommand = new RelayCommand(() => Run(Engine.StartButton), () => State.IsLoggedIn && !Engine.IsHardwareInitInProgress);
            PauseCommand = new RelayCommand(Pause, () => State.IsLoggedIn);
            EmergencyStopCommand = new RelayCommand(() => { Engine.EmergencyStopButton(); AfterCommand(); });
            ResetAlarmCommand = new RelayCommand(() => Run(Engine.ResetAlarmButton), () => State.IsLoggedIn);
            ResetProcessCommand = new RelayCommand(() => Run(Engine.ResetProcessButton), () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            ToggleFoupMountCommand = new RelayCommand(() => Run(Engine.ToggleFoupMountButton), () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            WaferLoadingCommand = new RelayCommand(() => Run(Engine.WaferLoadingButton), () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            WaferUnloadingCommand = new RelayCommand(() => Run(Engine.WaferUnloadingButton), () => State.IsLoggedIn && !_shell.Simulation.IsRunning);
            NotYetPortedCommand = new RelayCommand(p => _shell.AddLog($"'{p}' 기능은 다음 단계에서 이관됩니다.", "INFO"));

            State.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(FoupMountText));
                OnPropertyChanged(nameof(WaferLoadText));
                OnPropertyChanged(nameof(PauseText));
                OnPropertyChanged(nameof(ElapsedText));
            };
            UpdateScene();
        }

        private ProcessEngine Engine => _shell.Engine;

        /// <summary>로그인 확인 뒤 엔진 버튼 동작 실행 (원본 EnsureLoggedIn → 핸들러)</summary>
        private void Run(Action action)
        {
            if (!_shell.EnsureLoggedIn()) return;
            action();
            AfterCommand();
        }

        private void AfterCommand()
        {
            OnPropertyChanged(nameof(ElapsedText));
            UpdateScene();
            RelayCommand.Refresh();
        }

        private void Pause()
        {
            // 일시 정지 중에는 같은 버튼이 '재개'가 된다 (원본은 시작 버튼으로 재개)
            if (_shell.Simulation.IsRunning && _shell.Simulation.IsPaused)
            {
                Run(Engine.StartButton);
                return;
            }
            Run(Engine.PauseButton);
        }

        #region 장비 캔버스 / 램프

        // 원본 램프 색 (Color.ForestGreen/Goldenrod/Firebrick)
        private static readonly Color ForestGreen = Color.FromRgb(34, 139, 34);
        private static readonly Color Goldenrod = Color.FromRgb(218, 165, 32);
        private static readonly Color Firebrick = Color.FromRgb(178, 34, 34);

        /// <summary>장비 캔버스에 그릴 상태</summary>
        public EquipmentScene Scene { get; } = new EquipmentScene();

        private static Color ToColor(ArgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        /// <summary>공정 엔진 상태를 장비 캔버스 상태로 옮긴다 (원본 UpdateSimulationUi의 그림 부분).</summary>
        public void UpdateScene()
        {
            var engine = Engine;
            if (engine == null) return;
            var s = Scene;

            s.LampGreen = engine.MainLampGreen.HasValue ? ToColor(engine.MainLampGreen.Value) : Rendering.Palette.LampOff;
            s.LampYellow = engine.MainLampYellow ? Goldenrod : Rendering.Palette.LampOff;
            s.LampRed = engine.MainLampRed ? Firebrick : Rendering.Palette.LampOff;

            int capacity = AppSettings.MaxFoupCapacity;
            s.FoupA.Capacity = capacity;
            s.FoupB.Capacity = capacity;
            s.FoupA.Mounted = State.IsFoupAMounted;
            s.FoupB.Mounted = State.IsFoupBMounted;
            engine.Foups.GetFoupDisplayCounts(out int foupA, out int foupB);
            s.FoupA.WaferCount = foupA;
            s.FoupB.WaferCount = foupB;

            foreach (var region in new[] { EquipmentRegion.ChamberA, EquipmentRegion.ChamberB, EquipmentRegion.ChamberC })
            {
                var c = s.Chambers[region];
                c.ValveOpen = State.IsDoorOpen(region) ? 1 : 0;
                c.LampColor = engine.IsChamberLampOn(region) ? ForestGreen : Rendering.Palette.LampOff;
                var wafer = engine.GetChamberWaferColor(region);
                c.HasWafer = wafer.HasValue;
                if (wafer.HasValue) c.WaferColor = ToColor(wafer.Value);
            }

            bool hardware = _shell.IsEthercatConnected;
            var bladeColor = engine.GetBladeWaferColor();
            s.TmTarget = State.TmVisualTarget;
            s.TmCarrying = bladeColor.HasValue;
            if (bladeColor.HasValue) s.TmWaferColor = ToColor(bladeColor.Value);
            s.TmExtension = State.TmBladeExtensionFactor;
            s.IsSimulation = !hardware;

            var info = engine.BuildTmSideView();
            var side = s.SideView;
            side.Target = info.Target;
            side.Height = info.Height;
            side.Extension = info.Extension;
            side.VacuumOn = info.VacuumOn;
            side.WaferOnBlade = info.WaferOnBlade;
            if (info.WaferColor.HasValue) side.WaferColor = ToColor(info.WaferColor.Value);
            for (int i = 0; i < side.FoupLevels.Length && i < info.FoupLevels.Length; i++) side.FoupLevels[i] = info.FoupLevels[i];
            side.ChamberHasWafer = info.ChamberHasWafer;
            side.IsHardware = info.IsHardware;
            if (info.Positions != null) s.TeachingPositions = info.Positions;

            s.NotifyChanged();
            OnPropertyChanged(nameof(TmStatusText));
            OnPropertyChanged(nameof(ProcessDetailText));
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

        public string FoupMountText => State.IsFoupAMounted && State.IsFoupBMounted ? "FOUP 장착됨" : "FOUP 미장착";

        public string WaferLoadText => Engine?.GetWaferLoadStateDisplay() ?? "대기";

        public string PauseText => State.CurrentProcessState == ProcessState.Paused ? "재개" : "일시 정지";

        public string ElapsedText => TimeSpan.FromSeconds(_shell.Simulation.ElapsedSeconds).ToString(@"hh\:mm\:ss");

        /// <summary>원본 labelSummaryTMStatus ("상태: 이동(픽업) → FOUP A")</summary>
        public string TmStatusText => Engine?.GetTmStatusText() ?? "대기";

        /// <summary>원본 StatusProcessDetail ("공정 상태: 진행 중 | 완료 1/5")</summary>
        public string ProcessDetailText => State.StatusProcessDetail;

        /// <summary>장비 그림에서 챔버/FOUP 클릭 (원본: PmDetailForm/EquipmentDetailForm 열기)</summary>
        public void OpenRegionDetail(EquipmentRegion region)
        {
            // TODO(5단계): PmDetailForm / EquipmentDetailForm 이관
            _shell.AddLog($"{region} 상세 화면은 다음 단계에서 이관됩니다.", "INFO");
        }

        /// <summary>공정 타이머 Tick (UI 스레드, 엔진 처리 뒤)</summary>
        internal void OnTick()
        {
            OnPropertyChanged(nameof(ElapsedText));
            RelayCommand.Refresh();
        }
    }
}
