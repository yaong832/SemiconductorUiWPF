using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SemiconductorUi.Controllers;
using SemiconductorUi.Hardware;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;
using CoreState = SemiconductorUi.ViewModels.MainFormViewModel;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 메인 창(셸) 뷰모델. 원본 Form1 + Form1.Shell + Login/Hardware/Process 이벤트 핸들러 중
    /// 셸에 해당하는 부분을 옮긴 것이다. 공정 상태 값은 Core의 MainFormViewModel을 그대로 사용한다.
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable, IProcessHost
    {
        private readonly IDialogService _dialogs;
        private readonly IUserRepository _users;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _hardwareUiTimer;
        private readonly DispatcherTimer _simLampBlinkTimer;
        private readonly Dispatcher _dispatcher;

        private NavPage _currentPage = NavPage.Operate;
        private OperateTab _currentTab = OperateTab.Monitoring;
        private EthercatLinkState _ethercatState = EthercatLinkState.Disconnected;
        private bool _servoBusy;
        private DateTime _now = DateTime.Now;

        public MainViewModel(IDialogService dialogs, IUserRepository users, IEthercatDevice ethercat)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _dispatcher = Dispatcher.CurrentDispatcher;

            State = new CoreState();
            State.PropertyChanged += (s, e) => OnCoreStateChanged(e.PropertyName);

            Ethercat = ethercat;
            Logger = new LoggerService();
            HardwareManager = new HardwareManager(ethercat, AddLog);
            Simulation = new SimulationController(new DispatcherTickTimer(), OnSimulationTick);

            ServiceContainer.Register<ILoggerService>(Logger);
            ServiceContainer.Register<ISimulationService>(Simulation);

            // 원본 Form1의 공정 진행 코드 (스케줄링, TM 단계, 챔버 공정, 하드웨어 동작)
            Engine = new ProcessEngine(State, ethercat, HardwareManager, Simulation, this, new DispatcherTickTimer());

            LoginCommand = new RelayCommand(Login, () => !State.IsLoggedIn);
            LogoutCommand = new RelayCommand(Logout, () => State.IsLoggedIn);
            UserManagementCommand = new RelayCommand(OpenUserManagement, () => State.IsLoggedIn);
            // 장비를 움직이기 시작하는 버튼(EtherCAT 연결/해제, 서보 ON)은 로그인해야 누를 수 있다.
            // 서보 OFF는 정지 동작이라 로그인하지 않아도 언제든 누를 수 있게 둔다 (원본 484e8e0).
            ConnectEthercatCommand = new RelayCommand(ConnectEthercat, () => State.IsLoggedIn && !IsEthercatConnected);
            DisconnectEthercatCommand = new RelayCommand(DisconnectEthercat, () => State.IsLoggedIn && IsEthercatConnected);
            ServoOnCommand = new RelayCommand(() => _ = ServoOnAsync(), () => State.IsLoggedIn && IsEthercatConnected && !_servoBusy);
            ServoOffCommand = new RelayCommand(ServoOff, () => IsEthercatConnected && !_servoBusy);
            HomeCommand = new RelayCommand(() => Engine.HomeButton(), () => IsEthercatConnected && !_servoBusy);
            NavigateCommand = new RelayCommand(p =>
            {
                if (p is NavPage page)
                {
                    // 원본: 레시피 메뉴는 화면을 바꾸지 않고 레시피 관리 창(모달)을 연다
                    if (page == NavPage.Recipe) OpenRecipeManager();
                    else if (page == NavPage.Config) OpenConfig();
                    else CurrentPage = page;
                }
            });

            Monitoring = new MonitoringViewModel(this, dialogs);
            Trend = new TrendViewModel(Engine);

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => Now = DateTime.Now;
            _clockTimer.Start();

            // 원본 HardwareUiUpdateTimer: 하드웨어 모드에서 실제 TM 위치를 50ms마다 화면에 반영
            _hardwareUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _hardwareUiTimer.Tick += (s, e) => Engine.OnHardwareUiTick();
            _hardwareUiTimer.Start();

            // 원본 SimLampBlinkTimer: 시뮬레이션 모드 챔버 램프 점멸을 1초 틱 사이에도 시간 기준으로 바꾼다
            _simLampBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProcessEngine.SimLampBlinkRefreshMs) };
            _simLampBlinkTimer.Tick += (s, e) =>
            {
                if (Engine.TickSimLampBlink()) Monitoring.UpdateScene();
            };
            _simLampBlinkTimer.Start();

            State.CurrentUser = "Guest";
            State.CurrentRole = "없음";
            AddLog("프로그램 시작 (WPF)", "INFO");

            // 초기 레시피 목록과 환경 알람 임계값 (원본 Form1_Load)
            try
            {
                ReloadRecipeList();
            }
            catch (Exception ex)
            {
                string errorMessage = ExceptionHandler.HandleException(ex, "레시피 로드", AddLog);
                AddLog($"레시피 목록을 로드하는 중 오류가 발생했습니다: {errorMessage}", "WARN");
            }
            Engine.LoadAlarmThresholds();
        }

        #region Core 서비스

        public CoreState State { get; }
        public IEthercatDevice Ethercat { get; }
        public HardwareManager HardwareManager { get; }
        public SimulationController Simulation { get; }
        public ProcessEngine Engine { get; }
        public LoggerService Logger { get; }
        public MonitoringViewModel Monitoring { get; }

        /// <summary>추이 화면 (원본 TrendForm)</summary>
        public TrendViewModel Trend { get; }

        #endregion

        #region 화면 전환

        public NavPage CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(IsOperatePage));
                    OnPropertyChanged(nameof(IsTrendPage));
                    OnPropertyChanged(nameof(IsPlaceholderPage));
                    OnPropertyChanged(nameof(CurrentPageTitle));
                    OnPropertyChanged(nameof(ViewTitle));
                    // 원본 NavigateToSection(Trend): 들어갈 때 수집 시작
                    if (value == NavPage.Trend) Trend.OnShown();
                }
            }
        }

        public bool IsOperatePage => CurrentPage == NavPage.Operate;

        public bool IsTrendPage => CurrentPage == NavPage.Trend;

        /// <summary>아직 이관하지 않은 메뉴</summary>
        public bool IsPlaceholderPage => !IsOperatePage && !IsTrendPage;

        public string CurrentPageTitle
        {
            get
            {
                switch (CurrentPage)
                {
                    case NavPage.Operate: return "운전";
                    case NavPage.Recipe: return "레시피";
                    case NavPage.Maintenance: return "유지보수";
                    case NavPage.Config: return "설정";
                    case NavPage.Trend: return "추이";
                    case NavPage.Report: return "보고서";
                    default: return "시스템";
                }
            }
        }

        public OperateTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value)) OnPropertyChanged(nameof(ViewTitle));
            }
        }

        /// <summary>장비 이름 아래 작은 글자: 지금 보는 화면 (원본 Form1.Shell.GetShellViewTitle, 예: "운전 · 모니터링")</summary>
        public string ViewTitle
        {
            get
            {
                if (CurrentPage != NavPage.Operate) return CurrentPageTitle;
                switch (CurrentTab)
                {
                    case OperateTab.Verification: return "운전 · 검증";
                    case OperateTab.Transfer: return "운전 · 이송";
                    default: return "운전 · 모니터링";
                }
            }
        }

        /// <summary>하단 메뉴 줄 가운데 "현재 상태" (원본 labelFoupSummaryInfo: 교환 상태 | 대기열)</summary>
        public string StatusLineText => $"{State.CurrentFoupExchangeState} | {State.CurrentFoupQueueInfo}";

        public ICommand NavigateCommand { get; }

        #endregion

        #region 상단 바

        public DateTime Now
        {
            get => _now;
            private set
            {
                if (SetProperty(ref _now, value)) OnPropertyChanged(nameof(ClockText));
            }
        }

        public string ClockText => Now.ToString("yyyy-MM-dd HH:mm:ss");

        public bool IsEmergencyStopped => Ethercat?.IsEmergencyStopped ?? false;

        /// <summary>상단 바 상태 알약 (원본 Form1.Shell.GetShellStatePill)</summary>
        public string StatePillText
        {
            get
            {
                if (IsEmergencyStopped) return "비상정지";
                switch (State.CurrentProcessState)
                {
                    case ProcessState.Running: return "공정 진행 중";
                    case ProcessState.Paused: return "일시 정지";
                    case ProcessState.Error: return "경고 · 정지";
                    default: return "대기";
                }
            }
        }

        public Brush StatePillForeground => Pill().fg;
        public Brush StatePillBackground => Pill().bg;
        public Brush StatePillDot => Pill().dot;

        private (Brush dot, Brush fg, Brush bg) Pill()
        {
            if (IsEmergencyStopped || State.CurrentProcessState == ProcessState.Error)
                return (Res("Danger"), Rgb(168, 38, 48), Rgb(251, 233, 234));
            switch (State.CurrentProcessState)
            {
                case ProcessState.Running: return (Res("Primary"), Res("Primary"), Rgb(232, 239, 250));
                case ProcessState.Paused: return (Res("Warning"), Rgb(150, 92, 8), Rgb(252, 241, 222));
                default: return (Rgb(140, 150, 164), Rgb(58, 69, 85), Rgb(238, 241, 245));
            }
        }

        public EthercatLinkState EthercatState
        {
            get => _ethercatState;
            private set
            {
                if (SetProperty(ref _ethercatState, value))
                {
                    OnPropertyChanged(nameof(EthercatStatusText));
                    OnPropertyChanged(nameof(EthercatStatusBrush));
                    OnPropertyChanged(nameof(IsEthercatConnected));
                    RelayCommand.Refresh();
                }
            }
        }

        public bool IsEthercatConnected => HardwareManager.IsEthercatConnected;

        public string EthercatStatusText
        {
            get
            {
                switch (EthercatState)
                {
                    case EthercatLinkState.Connected: return "EtherCAT 연결됨";
                    case EthercatLinkState.ConnectionFailed: return "EtherCAT 연결 실패";
                    case EthercatLinkState.Error: return "EtherCAT 오류";
                    default: return "EtherCAT 끊김";
                }
            }
        }

        public Brush EthercatStatusBrush =>
            EthercatState == EthercatLinkState.Connected ? Res("Success")
            : EthercatState == EthercatLinkState.Disconnected ? Res("TextSecondary")
            : Res("Danger");

        /// <summary>서보 ON 상태 (원본 Form1.IsServoOn, 공정 엔진이 들고 있음)</summary>
        public bool IsServoOn
        {
            get => Engine.ServoOn;
            set
            {
                if (Engine.ServoOn == value) return;
                Engine.ServoOn = value;
                if (!value) HardwareManager.ResetTmHardwareInitialized();
                Engine.UpdateServoStatusLabel();
                NotifyServo();
            }
        }

        public bool IsHomed => HardwareManager.IsTmHardwareInitialized;

        /// <summary>원본 labelServoStatus ("서보: ON | Home: 완료")를 상단 바 칩 문구로</summary>
        public string ServoStatusText
        {
            get
            {
                if (IsEmergencyStopped) return "서보 비상정지";
                if (!IsEthercatConnected) return "서보 미연결";
                return Engine.ServoStatusText.Replace("서보: ", "서보 ").Replace(" | Home: ", " · 원점 ");
            }
        }

        public Brush ServoStatusBrush
        {
            get
            {
                switch (Engine.ServoStatus)
                {
                    case ProcessEngine.ServoStatusLevel.Success: return Res("Success");
                    case ProcessEngine.ServoStatusLevel.Warning: return Res("Warning");
                    case ProcessEngine.ServoStatusLevel.Danger: return Res("Danger");
                    default: return Res("TextSecondary");
                }
            }
        }

        public string LoginStatusText => State.IsLoggedIn ? $"{State.CurrentUser}" : "Guest";
        public string UserInitial => string.IsNullOrEmpty(LoginStatusText) ? "?" : LoginStatusText.Substring(0, 1).ToUpperInvariant();
        public string RoleText => State.IsLoggedIn ? State.CurrentRole : "로그인 필요";
        public bool IsAdmin => State.IsLoggedIn && State.CurrentRole == "관리자";

        #endregion

        #region 로그 / 알람

        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        private LogEntry _lastAlarm;

        public string AlarmText => _lastAlarm?.Message ?? "알람 없음";

        public Brush AlarmBrush =>
            _lastAlarm == null ? Res("TextSecondary")
            : _lastAlarm.IsError ? Res("Danger")
            : Res("Warning");

        /// <summary>원본 Form1.AddLogMessage 대응. 어느 스레드에서 불러도 된다.</summary>
        public void AddLog(string message, string level)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => AddLog(message, level)));
                return;
            }

            var entry = new LogEntry(DateTime.Now, level, message);
            Logger.Log(message, entry.Level);
            Logs.Insert(0, entry);
            int max = Math.Max(50, AppSettings.MaxLogEntries);
            while (Logs.Count > max) Logs.RemoveAt(Logs.Count - 1);

            if (entry.IsError || entry.IsWarning)
            {
                _lastAlarm = entry;
                OnPropertyChanged(nameof(AlarmText));
                OnPropertyChanged(nameof(AlarmBrush));
            }
        }

        public void ClearAlarmBanner()
        {
            _lastAlarm = null;
            OnPropertyChanged(nameof(AlarmText));
            OnPropertyChanged(nameof(AlarmBrush));
        }

        #endregion

        #region 로그인 (원본 LoginEventHandlers)

        public ICommand LoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand UserManagementCommand { get; }

        private void Login()
        {
            _users.EnsureSeedDefaults();
            while (_dialogs.TryLogin(out var username, out var password))
            {
                if (_users.ValidateUser(username, password))
                {
                    var user = _users.FindByUsername(username);
                    State.CurrentUser = username;
                    State.CurrentRole = user?.Role ?? "작업자";
                    State.IsLoggedIn = true;
                    AddLog($"로그인: {username} ({State.CurrentRole})", "INFO");
                    return;
                }
                _dialogs.Warn("아이디 또는 비밀번호가 올바르지 않습니다.", "로그인 실패");
            }
        }

        private void Logout()
        {
            if (!State.IsLoggedIn) return;
            AddLog($"로그아웃: {State.CurrentUser}", "INFO");
            State.IsLoggedIn = false;
            State.CurrentUser = "Guest";
            State.CurrentRole = "없음";
            SetProcessState(ProcessState.Idle, "로그아웃으로 인해 공정을 대기 상태로 전환했습니다.");
        }

        private void OpenUserManagement()
        {
            if (!IsAdmin)
            {
                _dialogs.Warn("사용자 관리는 관리자만 접근할 수 있습니다.", "권한 없음");
                return;
            }
            _dialogs.ShowUserManagement(new UserManagementViewModel(_dialogs, _users, State.CurrentUser));
        }

        #region 레시피

        /// <summary>제어 패널 레시피 목록 (원본 comboRecipeSelect)</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> RecipeNames { get; } = new System.Collections.ObjectModel.ObservableCollection<string>();

        /// <summary>원본 Form1.ReloadRecipeCombo</summary>
        public void ReloadRecipeList(List<RecipeSnapshot> provided = null)
        {
            var list = provided ?? RecipeRepository.LoadAll();
            var selected = Monitoring?.SelectedRecipeName;
            RecipeNames.Clear();
            foreach (var r in list.OrderBy(r => r.Name))
            {
                if (!string.IsNullOrWhiteSpace(r.Name)) RecipeNames.Add(r.Name);
            }
            if (Monitoring != null)
            {
                Monitoring.SelectedRecipeName = !string.IsNullOrEmpty(selected) && RecipeNames.Contains(selected) ? selected : null;
            }
        }

        /// <summary>원본 Form1.NavigateToSection(AppSection.Config): 관리자만 알람 기준 설정 창을 연다</summary>
        private void OpenConfig()
        {
            if (!EnsureLoggedIn()) return;
            if (State.CurrentRole != "관리자")
            {
                _dialogs.Warn("설정 변경은 관리자만 접근할 수 있습니다.", "권한 없음");
                return;
            }
            try
            {
                var model = new ConfigViewModel(_dialogs, Engine.GetAlarmThresholdsSnapshot(), SaveThresholds)
                {
                    OnSaved = snapshot =>
                    {
                        Engine.SetAlarmThresholdsFromSnapshot(snapshot);
                        AddLog("알람 임계값 설정이 업데이트되었습니다.", "INFO");
                    },
                    // TODO(5단계): TeachingPositionForm 이관 뒤 연결
                    OnOpenTeaching = () => _dialogs.Info("TM 티칭 위치 화면은 다음 단계에서 이관됩니다."),
                };
                _dialogs.ShowConfig(model);
            }
            catch (Exception ex)
            {
                _dialogs.Error($"설정 폼을 여는 중 오류가 발생했습니다.\n{ex.Message}", "오류");
                AddLog($"Config 폼 오류: {ex.Message}", "ERROR");
            }
        }

        /// <summary>저장 후 파일에서 다시 읽어 주요 값이 같은지 확인 (원본 ConfigForm.btnSave_Click)</summary>
        private static bool SaveThresholds(EnvThresholdSnapshot thresholds)
        {
            ConfigRepository.Save(thresholds);
            var saved = ConfigRepository.Load();
            return saved != null
                && Math.Abs(saved.TempWarn - thresholds.TempWarn) < 0.001
                && Math.Abs(saved.TempAlarm - thresholds.TempAlarm) < 0.001;
        }

        /// <summary>원본 NavigationEventHandlers.ButtonNavRecipe_Click: 관리자만 레시피 관리 창을 연다</summary>
        private void OpenRecipeManager()
        {
            if (!EnsureLoggedIn()) return;
            if (State.CurrentRole != "관리자")
            {
                _dialogs.Warn("레시피 직접 수정은 관리자만 접근할 수 있습니다.", "권한 없음");
                return;
            }
            try
            {
                var model = new RecipeManagerViewModel(_dialogs, RecipeRepository.LoadAll, RecipeRepository.SaveAll)
                {
                    OnSavedAll = list =>
                    {
                        try { ReloadRecipeList(list); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"레시피 리로드 오류: {ex.Message}"); }
                    }
                };
                _dialogs.ShowRecipeManager(model);
                // 닫힌 뒤에도 한 번 더 로드 보정
                ReloadRecipeList();
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Recipe 관리 화면을 여는 중 문제가 발생했습니다.\r\n{ex.Message}\r\n\r\n{ex.GetType().FullName}", "오류");
            }
        }

        /// <summary>
        /// 원본 Form1.OpenTeachingPositionEditor: TM 티칭 위치 창.
        /// 원본은 설정 창(관리자 전용)에서만 열리므로 여기서도 관리자만 연다.
        /// </summary>
        public void OpenTeachingPositions()
        {
            if (!EnsureLoggedIn()) return;
            if (State.CurrentRole != "관리자")
            {
                _dialogs.Warn("티칭 위치 변경은 관리자만 접근할 수 있습니다.", "권한 없음");
                return;
            }
            try
            {
                var tm = HardwareManager.TmHardwareController;
                var initial = tm != null ? tm.Positions.ToData() : TeachingPositionsRepository.Load();
                var model = new TeachingPositionViewModel(_dialogs, initial, ServoSpeedSettings.FromAppSettings(),
                    ReadServoPosition, new RepositoryTeachingStore())
                {
                    OnApplied = ApplyTeachingPositions,
                };
                _dialogs.ShowTeachingPositions(model);
            }
            catch (Exception ex)
            {
                _dialogs.Error($"티칭 위치 폼 오류: {ex.Message}", "오류");
                AddLog($"티칭 위치 폼 오류: {ex.Message}", "ERROR");
            }
        }

        /// <summary>연결돼 있으면 현재 서보 위치 (X=Axis2, Y=Axis1), 아니면 null</summary>
        private ServoPositionReading ReadServoPosition() =>
            IsEthercatConnected && Ethercat != null
                ? new ServoPositionReading(Ethercat.Axis2_is_PosData(), Ethercat.Axis1_is_PosData())
                : null;

        /// <summary>원본 Form1.ApplyTeachingPositions: 저장한 티칭값을 런타임 컨트롤러와 시뮬레이션에 반영</summary>
        private void ApplyTeachingPositions(TeachingPositionsData data)
        {
            if (data == null) return;
            var positions = new TmHardwareController.TmPositionSet();
            positions.ApplyFrom(data);
            HardwareManager.TmHardwareController?.Positions.ApplyFrom(data);
            Engine.SetSimulatedTeachingPositions(positions);
            AddLog(
                $"TM 티칭 위치 적용: Home=({positions.Home_X},{positions.Home_Y}), " +
                $"CA_X={positions.ChamberA_X}, CB_X={positions.ChamberB_X}, CC_X={positions.ChamberC_X}, " +
                $"FA_X={positions.FoupA_X}, FB_X={positions.FoupB_X}, DescendOffset={positions.DescendOffset}",
                "INFO");
        }

        private sealed class RepositoryTeachingStore : ITeachingStore
        {
            public void SaveTeaching(TeachingPositionsData data) => TeachingPositionsRepository.Save(data);

            public ServoSpeedSettings SaveSpeeds(ServoSpeedSettings speeds)
            {
                ServoSpeedRepository.Save(speeds);
                return ServoSpeedSettings.FromAppSettings();
            }
        }

        #endregion

        /// <summary>원본 Form1.EnsureLoggedIn</summary>
        public bool EnsureLoggedIn()
        {
            if (State.IsLoggedIn) return true;
            _dialogs.Warn("먼저 로그인해 주세요.", "로그인 필요");
            return false;
        }

        #endregion

        #region EtherCAT / 서보 (원본 HardwareEventHandlers)

        public ICommand ConnectEthercatCommand { get; }
        public ICommand DisconnectEthercatCommand { get; }
        public ICommand ServoOnCommand { get; }
        public ICommand ServoOffCommand { get; }
        public ICommand HomeCommand { get; }

        private void ConnectEthercat()
        {
            if (IsEthercatConnected)
            {
                _dialogs.Info("이미 EtherCAT에 연결되어 있습니다.");
                return;
            }

            try
            {
                if (HardwareManager.ConnectEthercat())
                {
                    IsServoOn = false;
                    EthercatState = EthercatLinkState.Connected;
                    try
                    {
                        HardwareManager.InitializeTmHardwareController();
                    }
                    catch (Exception tmEx)
                    {
                        AddLog($"TM 하드웨어 컨트롤러 초기화 오류: {tmEx.Message}", "WARN");
                    }

                    Simulation.SetInterval(AppSettings.HardwareModeTickMilliseconds);
                    if (!Simulation.IsTimerRunning) Simulation.StartTimer();
                }
                else
                {
                    EthercatState = EthercatLinkState.ConnectionFailed;
                    _dialogs.Warn("EtherCAT 연결에 실패했습니다.\n시뮬레이션 모드로 동작합니다.", "연결 오류");
                }
            }
            catch (Exception ex)
            {
                EthercatState = EthercatLinkState.Error;
                AddLog($"EtherCAT 연결 오류: {ex.Message}", "ERROR");
                _dialogs.Error($"EtherCAT 연결 중 오류가 발생했습니다: {ex.Message}\n시뮬레이션 모드로 동작합니다.", "연결 오류");
            }
            NotifyServo();
        }

        private void DisconnectEthercat()
        {
            try
            {
                HardwareManager.DisconnectEthercat();
                IsServoOn = false;
                EthercatState = EthercatLinkState.Disconnected;
                if (!Simulation.IsRunning && Simulation.IsTimerRunning) Simulation.StopTimer();
                AddLog("EtherCAT 연결 해제됨", "WARN");
            }
            catch (Exception ex)
            {
                AddLog($"EtherCAT 연결 해제 오류: {ex.Message}", "ERROR");
            }
            NotifyServo();
        }

        private async Task ServoOnAsync()
        {
            if (!IsEthercatConnected)
            {
                _dialogs.Warn("먼저 EtherCAT을 연결해주세요.", "연결 필요");
                return;
            }
            if (IsEmergencyStopped)
            {
                _dialogs.Warn("비상정지 중입니다.\n알람 리셋으로 비상정지를 해제한 뒤 서보 ON을 하세요.", "비상정지 중");
                return;
            }

            AddLog("서보모터 ON 시작...", "INFO");
            _servoBusy = true;
            RelayCommand.Refresh();
            try
            {
                bool confirmed = false;
                await HardwareOperation.Run(Ethercat, () => confirmed = ServoPowerService.TurnOnAndConfirm(Ethercat));
                IsServoOn = confirmed;
                if (confirmed)
                {
                    AddLog("서보모터 ON - 파라미터 설정 완료", "INFO");
                }
                else
                {
                    AddLog("서보모터 ON 명령 전송 완료, 하지만 상태 확인 실패 - 수동 확인 필요", "WARN");
                    _dialogs.Warn(
                        "서보 ON 명령을 전송했지만 상태 확인에 실패했습니다.\n\n" +
                        "확인 사항:\n1. 서보 모터 전원 확인\n2. EtherCAT 연결 상태 확인\n3. 서보 모터 상태 수동 확인",
                        "서보 ON 확인");
                }
            }
            catch (Exception ex)
            {
                AddLog($"서보 ON 오류: {ex.Message}", "ERROR");
                _dialogs.Error($"서보 ON 오류: {ex.Message}");
            }
            finally
            {
                _servoBusy = false;
                NotifyServo();
                RelayCommand.Refresh();
            }
        }

        private void ServoOff()
        {
            if (!IsEthercatConnected)
            {
                _dialogs.Warn("EtherCAT이 연결되지 않았습니다.", "연결 필요");
                return;
            }
            try
            {
                ServoPowerService.TurnOff(Ethercat);
                IsServoOn = false;
                HardwareManager.ResetTmHardwareInitialized();
                AddLog("서보모터 OFF", "INFO");
            }
            catch (Exception ex)
            {
                AddLog($"서보 OFF 오류: {ex.Message}", "ERROR");
                _dialogs.Error($"서보 OFF 오류: {ex.Message}");
            }
            NotifyServo();
        }

        /// <summary>원본 Form1.ExecuteEmergencyStop (공정 엔진이 처리)</summary>
        public void ExecuteEmergencyStop(string source)
        {
            Engine.ExecuteEmergencyStop(source);
            NotifyServo();
            OnPropertyChanged(nameof(IsEmergencyStopped));
        }

        private void NotifyServo()
        {
            OnPropertyChanged(nameof(ServoStatusText));
            OnPropertyChanged(nameof(ServoStatusBrush));
            OnPropertyChanged(nameof(IsEthercatConnected));
            NotifyPill();
            Monitoring?.UpdateScene();
        }

        #endregion

        #region 공정 상태

        /// <summary>원본 Form1.SetProcessState</summary>
        public void SetProcessState(ProcessState state, string reason = null)
        {
            Engine.SetProcessState(state, reason);
        }

        private void OnSimulationTick(object sender, EventArgs e)
        {
            // 원본 TimerEventHandlers.SimulationTimer_Tick
            Engine.OnSimulationTick();
            Monitoring.OnTick();
            if (IsTrendPage) Trend.Refresh();
            OnPropertyChanged(nameof(IsEmergencyStopped));
        }

        private void OnCoreStateChanged(string propertyName)
        {
            switch (propertyName)
            {
                case nameof(CoreState.IsLoggedIn):
                case nameof(CoreState.CurrentUser):
                case nameof(CoreState.CurrentRole):
                    OnPropertyChanged(nameof(LoginStatusText));
                    OnPropertyChanged(nameof(UserInitial));
                    OnPropertyChanged(nameof(RoleText));
                    OnPropertyChanged(nameof(IsAdmin));
                    if (!State.IsLoggedIn) Monitoring?.CloseUnitDetail();
                    RelayCommand.Refresh();
                    break;
                case nameof(CoreState.CurrentFoupExchangeState):
                case nameof(CoreState.CurrentFoupQueueInfo):
                    OnPropertyChanged(nameof(StatusLineText));
                    break;
                case nameof(CoreState.CurrentProcessState):
                    NotifyPill();
                    RelayCommand.Refresh();
                    break;
            }
        }

        private void NotifyPill()
        {
            OnPropertyChanged(nameof(StatePillText));
            OnPropertyChanged(nameof(StatePillForeground));
            OnPropertyChanged(nameof(StatePillBackground));
            OnPropertyChanged(nameof(StatePillDot));
        }

        #endregion

        #region IProcessHost (공정 엔진 → 화면)

        void IProcessHost.AddLog(string message, string level) => AddLog(message, level);

        void IProcessHost.ShowMessage(string text, string title, MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Error: _dialogs.Error(text, title); break;
                case MessageKind.Warning: _dialogs.Warn(text, title); break;
                default: _dialogs.Info(text, title); break;
            }
        }

        bool IProcessHost.Confirm(string text, string title, bool defaultNo) => _dialogs.Confirm(text, title, defaultNo);

        int? IProcessHost.PromptWaferCount(int current, int min, int max) => _dialogs.PromptWaferCount(current, min, max);

        bool IProcessHost.HasHeaderAlarm() => _lastAlarm != null;

        void IProcessHost.ClearHeaderAlarm() => ClearAlarmBanner();

        void IProcessHost.BeginInvoke(Action action) => _dispatcher.BeginInvoke(action);

        bool IProcessHost.CheckAccess() => _dispatcher.CheckAccess();

        void IProcessHost.Invoke(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }

        void IProcessHost.RunOnceAfter(int delayMs, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(delayMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        private bool _refreshQueued;

        void IProcessHost.RefreshUi()
        {
            // 한 틱 안에서 여러 번 불리므로 모아서 한 번만 그린다
            if (_refreshQueued) return;
            _refreshQueued = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _refreshQueued = false;
                Monitoring?.UpdateScene();
            }));
        }

        void IProcessHost.RefreshServoStatus()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => ((IProcessHost)this).RefreshServoStatus()));
                return;
            }
            NotifyServo();
            RelayCommand.Refresh();
        }

        #endregion

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private static Brush Rgb(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public void Dispose()
        {
            _clockTimer.Stop();
            _hardwareUiTimer.Stop();
            _simLampBlinkTimer.Stop();
            Engine.Dispose();
            Simulation.Dispose();
            HardwareManager.Dispose();
        }
    }
}
