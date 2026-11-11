using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SemiconductorUi.Helpers;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>값 글자 색</summary>
    public enum InfoTone
    {
        Normal,
        Good,
        Warn,
        Bad
    }

    /// <summary>시스템 화면 한 줄 (이름 / 값)</summary>
    public sealed class SystemInfoRow : ObservableObject
    {
        private string _value = "";
        private InfoTone _tone;

        public SystemInfoRow(string label)
        {
            Label = label;
        }

        public string Label { get; }
        public string Value { get => _value; internal set => SetProperty(ref _value, value ?? ""); }
        public InfoTone Tone { get => _tone; internal set => SetProperty(ref _tone, value); }

        internal void Set(string value, InfoTone tone = InfoTone.Normal)
        {
            Value = value;
            Tone = tone;
        }
    }

    /// <summary>기록 폴더 한 줄 (이름 / 경로 / 열기)</summary>
    public sealed class SystemInfoFolderRow : ObservableObject
    {
        private readonly Action<string> _open;
        private string _path = "";

        public SystemInfoFolderRow(string label, Action<string> open)
        {
            Label = label;
            _open = open;
            OpenCommand = new RelayCommand(() => _open?.Invoke(Path), () => IsAvailable);
        }

        public string Label { get; }

        public string Path
        {
            get => _path;
            internal set
            {
                if (SetProperty(ref _path, value ?? "")) OnPropertyChanged(nameof(IsAvailable));
            }
        }

        /// <summary>경로가 없으면 (기록을 시작하지 못함) 열기 버튼을 끈다</summary>
        public bool IsAvailable => !string.IsNullOrEmpty(Path) && !Path.StartsWith("(", StringComparison.Ordinal);

        public ICommand OpenCommand { get; }
    }

    /// <summary>
    /// 아래 메뉴 줄 "시스템" 화면 (원본 Form1.ShowSystemInfoSummary).
    /// 원본은 메시지 상자 한 번(App Version, OS, .NET, EtherCAT, Login, Process)이었다.
    /// WPF는 같은 창 안 화면으로 두고 프로그램·장비·운전 상태와 기록 폴더를 보이는 동안 1초마다 고친다.
    /// "요약 복사"는 원본 메시지 상자 글자를 그대로 복사한다.
    /// </summary>
    public sealed class SystemInfoViewModel : ObservableObject
    {
        private readonly MainViewModel _shell;
        private readonly Func<DateTime> _now;
        private readonly DateTime? _processStart;
        private DispatcherTimer _timer;
        private string _summaryText = "";
        private string _statusText = "";

        private readonly SystemInfoRow _version = new SystemInfoRow("프로그램 버전");
        private readonly SystemInfoRow _runtime = new SystemInfoRow(".NET");
        private readonly SystemInfoRow _os = new SystemInfoRow("OS");
        private readonly SystemInfoRow _machine = new SystemInfoRow("컴퓨터");
        private readonly SystemInfoRow _started = new SystemInfoRow("시작 시각");
        private readonly SystemInfoRow _uptime = new SystemInfoRow("실행 시간");
        private readonly SystemInfoRow _memory = new SystemInfoRow("메모리");

        private readonly SystemInfoRow _ethercat = new SystemInfoRow("EtherCAT");
        private readonly SystemInfoRow _mode = new SystemInfoRow("운전 모드");
        private readonly SystemInfoRow _servo = new SystemInfoRow("서보");
        private readonly SystemInfoRow _homed = new SystemInfoRow("원점복귀");
        private readonly SystemInfoRow _estop = new SystemInfoRow("비상정지");

        private readonly SystemInfoRow _login = new SystemInfoRow("로그인");
        private readonly SystemInfoRow _role = new SystemInfoRow("권한");
        private readonly SystemInfoRow _process = new SystemInfoRow("공정 상태");
        private readonly SystemInfoRow _recipe = new SystemInfoRow("레시피");
        private readonly SystemInfoRow _alarms = new SystemInfoRow("현재 알람");

        private readonly SystemInfoFolderRow _logFolder;
        private readonly SystemInfoFolderRow _alarmFolder;
        private readonly SystemInfoFolderRow _changeFolder;
        private readonly SystemInfoFolderRow _waferFolder;
        private readonly SystemInfoFolderRow _counterFolder;
        private readonly SystemInfoFolderRow _backupFolder;

        public SystemInfoViewModel(MainViewModel shell, Action<string> openFolder, Func<DateTime> now = null)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _now = now ?? (() => DateTime.Now);
            try
            {
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    _processStart = p.StartTime;
                }
            }
            catch (Exception)
            {
                _processStart = null;
            }

            ProgramRows = new[] { _version, _runtime, _os, _machine, _started, _uptime, _memory };
            EquipmentRows = new[] { _ethercat, _mode, _servo, _homed, _estop };
            OperationRows = new[] { _login, _role, _process, _recipe, _alarms };

            _logFolder = new SystemInfoFolderRow("운영 로그", openFolder);
            _alarmFolder = new SystemInfoFolderRow("알람 이력", openFolder);
            _changeFolder = new SystemInfoFolderRow("변경 이력", openFolder);
            _waferFolder = new SystemInfoFolderRow("웨이퍼 이력", openFolder);
            _counterFolder = new SystemInfoFolderRow("유지보수 카운터", openFolder);
            _backupFolder = new SystemInfoFolderRow("설정 백업", openFolder);
            FolderRows = new[] { _logFolder, _alarmFolder, _changeFolder, _waferFolder, _counterFolder, _backupFolder };

            CopySummaryCommand = new RelayCommand(CopySummary);
            CopyAllCommand = new RelayCommand(CopyAll);
        }

        public IReadOnlyList<SystemInfoRow> ProgramRows { get; }
        public IReadOnlyList<SystemInfoRow> EquipmentRows { get; }
        public IReadOnlyList<SystemInfoRow> OperationRows { get; }
        public IReadOnlyList<SystemInfoFolderRow> FolderRows { get; }

        /// <summary>원본 "System Information" 메시지 상자 글자</summary>
        public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

        /// <summary>복사 결과 안내</summary>
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        public ICommand CopySummaryCommand { get; }
        public ICommand CopyAllCommand { get; }

        public void OnShown()
        {
            StatusText = "";
            Refresh();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, e) => Refresh();
            }
            _timer.Start();
            OnPropertyChanged(nameof(IsRunning));
        }

        public void OnHidden()
        {
            _timer?.Stop();
            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>WPF 프로그램 버전 (원본 Application.ProductVersion 대응: 정보 버전, 없으면 어셈블리 버전)</summary>
        public static string AppVersion
        {
            get
            {
                var asm = typeof(SystemInfoViewModel).Assembly;
                string info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(info)) return asm.GetName().Version?.ToString() ?? "-";
                // "1.0.0+커밋" → "1.0.0 (커밋 앞 7자리)"
                int plus = info.IndexOf('+');
                if (plus > 0 && info.Length > plus + 1)
                {
                    string rev = info.Substring(plus + 1);
                    return $"{info.Substring(0, plus)} ({(rev.Length > 7 ? rev.Substring(0, 7) : rev)})";
                }
                return info;
            }
        }

        internal static string FormatUptime(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}일 {span.Hours}시간 {span.Minutes}분";
            if (span.TotalHours >= 1) return $"{span.Hours}시간 {span.Minutes}분";
            if (span.TotalMinutes >= 1) return $"{span.Minutes}분 {span.Seconds}초";
            return $"{span.Seconds}초";
        }

        public void Refresh()
        {
            var state = _shell.State;
            var engine = _shell.Engine;
            DateTime now = _now();

            SummaryText = SystemInfoSummary.Build(AppVersion, Environment.OSVersion.ToString(), Environment.Version.ToString(),
                _shell.IsEthercatConnected, state.IsLoggedIn, state.CurrentUser, state.CurrentProcessState.ToString());

            // 프로그램
            _version.Set(AppVersion);
            _runtime.Set($"{RuntimeInformation.FrameworkDescription} · {(Environment.Is64BitProcess ? "64비트" : "32비트")} 프로세스");
            _os.Set(RuntimeInformation.OSDescription.Trim());
            _machine.Set(SafeMachineName());
            _started.Set(_processStart.HasValue ? _processStart.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-");
            _uptime.Set(_processStart.HasValue ? FormatUptime(now - _processStart.Value) : "-");
            _memory.Set(WorkingSetText());

            // 장비
            bool connected = _shell.IsEthercatConnected;
            InfoTone ethTone = !connected
                ? (_shell.EthercatState == EthercatLinkState.Disconnected ? InfoTone.Normal : InfoTone.Bad)
                : (_shell.HardwareManager.IsCommUnhealthy ? InfoTone.Warn : InfoTone.Good);
            _ethercat.Set(_shell.EthercatStatusText, ethTone);
            _mode.Set(connected ? "하드웨어 (실제 장비)" : "시뮬레이션 (EtherCAT 미연결)");
            InfoTone servoTone;
            switch (engine.ServoStatus)
            {
                case ProcessEngine.ServoStatusLevel.Success: servoTone = InfoTone.Good; break;
                case ProcessEngine.ServoStatusLevel.Warning: servoTone = InfoTone.Warn; break;
                case ProcessEngine.ServoStatusLevel.Danger: servoTone = InfoTone.Bad; break;
                default: servoTone = InfoTone.Normal; break;
            }
            _servo.Set(_shell.ServoStatusText, servoTone);
            _homed.Set(!connected ? "-" : _shell.IsHomed ? "완료" : "필요",
                !connected ? InfoTone.Normal : _shell.IsHomed ? InfoTone.Good : InfoTone.Warn);
            _estop.Set(_shell.IsEmergencyStopped ? "비상정지 중" : "정상", _shell.IsEmergencyStopped ? InfoTone.Bad : InfoTone.Normal);

            // 운전
            _login.Set(state.IsLoggedIn ? state.CurrentUser : "Guest");
            _role.Set(_shell.RoleText);
            var ps = state.CurrentProcessState;
            _process.Set($"{_shell.StatePillText} ({ps})",
                ps == SemiconductorUi.ViewModels.MainFormViewModel.ProcessState.Error ? InfoTone.Bad
                : ps == SemiconductorUi.ViewModels.MainFormViewModel.ProcessState.Running ? InfoTone.Good
                : ps == SemiconductorUi.ViewModels.MainFormViewModel.ProcessState.Paused ? InfoTone.Warn
                : InfoTone.Normal);
            _recipe.Set(string.IsNullOrWhiteSpace(state.LastSelectedRecipe) ? "선택 안 됨" : state.LastSelectedRecipe);
            int alarms = _shell.ActiveAlarmCount;
            _alarms.Set(alarms > 0 ? $"{alarms}건" : "없음", alarms > 0 ? InfoTone.Bad : InfoTone.Normal);

            // 기록 폴더
            _logFolder.Path = _shell.Logger.DailyLogFolder ?? "(파일 기록 꺼짐)";
            _alarmFolder.Path = SafePath(() => engine.AlarmHistory?.Folder) ?? "(기록 안 함)";
            _changeFolder.Path = ChangeHistory.IsEnabled ? ChangeHistory.Folder : "(기록 안 함)";
            _waferFolder.Path = SafePath(() => engine.WaferHistory?.Folder) ?? "(기록 안 함)";
            _counterFolder.Path = _shell.MaintenanceCounters?.Folder ?? "(시작 안 됨)";
            _backupFolder.Path = SafePath(() => SettingsBackup.BackupRoot) ?? "(알 수 없음)";
        }

        private static string SafePath(Func<string> get)
        {
            try
            {
                string path = get();
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (Exception)
            {
                return "-";
            }
        }

        private static string WorkingSetText()
        {
            try
            {
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    return $"{p.WorkingSet64 / (1024 * 1024):N0} MB";
                }
            }
            catch (Exception)
            {
                return "-";
            }
        }

        private void CopySummary()
        {
            Refresh();
            Copy(SummaryText, "원본 System Information 글자를 복사했습니다.");
        }

        private void CopyAll()
        {
            Refresh();
            var sb = new StringBuilder();
            sb.Append(SummaryText);
            void Section(string title, IEnumerable<(string label, string value)> rows)
            {
                sb.AppendLine();
                sb.AppendLine($"[{title}]");
                foreach (var (label, value) in rows) sb.AppendLine($"{label}: {value}");
            }
            Section("프로그램", ProgramRows.Select(r => (r.Label, r.Value)));
            Section("장비", EquipmentRows.Select(r => (r.Label, r.Value)));
            Section("운전", OperationRows.Select(r => (r.Label, r.Value)));
            Section("기록 위치", FolderRows.Select(r => (r.Label, r.Path)));
            Copy(sb.ToString(), "시스템 정보 전체를 복사했습니다.");
        }

        private void Copy(string text, string done)
        {
            try
            {
                Clipboard.SetText(text);
                StatusText = done;
            }
            catch (Exception ex)
            {
                StatusText = $"복사하지 못했습니다: {ex.Message}";
            }
        }
    }
}
