using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    public enum MaintenanceMessageTone { Info, Success, Danger }

    /// <summary>유지보수 카운터 한 줄 (항목 하나)</summary>
    public sealed class MaintenanceCounterRow : ObservableObject
    {
        private long _sinceService;
        private long _limit;
        private long _total;
        private string _sinceServiceText = "0";
        private string _limitText = "없음";
        private string _statusText = "주기 없음";
        private MaintenanceCounterStatus _status = MaintenanceCounterStatus.NoLimit;
        private string _totalText = "0";
        private string _lastServiceText = "기록 없음";
        private double _ratio;

        public MaintenanceCounterRow(string key, string name, string source)
        {
            Key = key;
            Name = name;
            Source = source;
        }

        public string Key { get; }
        public string Name { get; }
        /// <summary>무엇을 세는지 (예: 출력 12 켜짐)</summary>
        public string Source { get; }

        public long SinceService => _sinceService;
        public long Limit => _limit;
        public long Total => _total;

        public string SinceServiceText { get => _sinceServiceText; private set => SetProperty(ref _sinceServiceText, value); }
        public string LimitText { get => _limitText; private set => SetProperty(ref _limitText, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public MaintenanceCounterStatus Status { get => _status; private set => SetProperty(ref _status, value); }
        public string TotalText { get => _totalText; private set => SetProperty(ref _totalText, value); }
        public string LastServiceText { get => _lastServiceText; private set => SetProperty(ref _lastServiceText, value); }

        /// <summary>주기 대비 진행 (0~1, 막대용). 주기가 없으면 0 (WPF 추가)</summary>
        public double Ratio { get => _ratio; private set => SetProperty(ref _ratio, value); }

        internal void Apply(MaintenanceCounterState c)
        {
            _sinceService = c.SinceService;
            _limit = c.Limit;
            _total = c.Total;
            SinceServiceText = c.SinceService.ToString("N0");
            LimitText = MaintenanceCounterService.FormatLimit(c.Limit);
            Status = c.Status;
            StatusText = FormatStatus(c);
            TotalText = c.Total.ToString("N0");
            LastServiceText = c.LastServiceTime.HasValue
                ? $"{c.LastServiceTime.Value:yyyy-MM-dd HH:mm}{(string.IsNullOrEmpty(c.LastServiceUser) ? "" : " · " + c.LastServiceUser)}"
                : "기록 없음";
            Ratio = Math.Min(1.0, Math.Max(0.0, c.Ratio));
        }

        /// <summary>원본 MaintenanceCounterForm.StatusText</summary>
        public static string FormatStatus(MaintenanceCounterState c)
        {
            int percent = (int)Math.Floor(c.Ratio * 100);
            switch (c.Status)
            {
                case MaintenanceCounterStatus.Due: return $"점검 필요 · {percent}%";
                case MaintenanceCounterStatus.Soon: return $"곧 점검 · {percent}%";
                case MaintenanceCounterStatus.Normal: return $"정상 · {percent}%";
                default: return "주기 없음";
            }
        }
    }

    /// <summary>점검 이력 한 줄</summary>
    public sealed class MaintenanceHistoryItem
    {
        public MaintenanceHistoryItem(MaintenanceHistoryRow r)
        {
            TimeText = r.Time.ToString("yyyy-MM-dd HH:mm:ss");
            Event = r.Event;
            Item = r.Item;
            CountText = long.TryParse(r.Count, out long count) ? count.ToString("N0") : r.Count;
            LimitText = long.TryParse(r.Limit, out long limit) ? MaintenanceCounterService.FormatLimit(limit) : r.Limit;
            User = r.User;
            Note = r.Note;
            IsDue = r.Event == MaintenanceCounterService.EventDue;
        }

        public string TimeText { get; }
        public string Event { get; }
        public string Item { get; }
        public string CountText { get; }
        public string LimitText { get; }
        public string User { get; }
        public string Note { get; }
        /// <summary>주기 도달 줄은 빨간 글자</summary>
        public bool IsDue { get; }
    }

    /// <summary>
    /// 유지보수 카운터 (원본 Forms.MaintenanceCounterForm). 실린더·진공·배기·도어·축의 동작 횟수, 점검 주기, 점검 이력.
    /// 점검 완료는 로그인한 사용자, 점검 주기 변경은 관리자만 할 수 있다. 확인 질문은 메시지 상자 대신 화면 안에서 묻는다.
    /// 보이는 곳(유지보수 화면의 카운터 탭, 별도 창)이 있을 때만 1초마다 갱신하고, 이력은 5초마다 다시 읽는다.
    /// </summary>
    public sealed class MaintenanceCounterViewModel : ObservableObject
    {
        public const int RefreshIntervalMs = 1000;
        public const int HistoryRows = 200;
        public const long MaxLimit = 1000000000;

        private readonly Func<MaintenanceCounterService> _serviceProvider;
        private readonly Func<bool> _isLoggedIn;
        private readonly Func<string> _currentUser;
        private readonly Func<bool> _isAdmin;
        private readonly Action<string> _openFolder;
        private readonly Func<DateTime> _clock;
        private DispatcherTimer _timer;
        private int _viewers;
        private int _ticks;

        private MaintenanceCounterRow _selectedRow;
        private string _limitInput = "";
        private string _note = "";
        private string _message = "";
        private MaintenanceMessageTone _messageTone = MaintenanceMessageTone.Info;
        private string _pendingServiceKey;
        private string _confirmText = "";
        private bool _isAvailable;
        private int _dueCount;
        private int _soonCount;

        public MaintenanceCounterViewModel(Func<MaintenanceCounterService> serviceProvider, Func<bool> isLoggedIn,
            Func<string> currentUser, Func<bool> isAdmin, Action<string> openFolder, Func<DateTime> clock = null)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _isLoggedIn = isLoggedIn ?? (() => false);
            _currentUser = currentUser ?? (() => "");
            _isAdmin = isAdmin ?? (() => false);
            _openFolder = openFolder;
            _clock = clock ?? (() => DateTime.Now);

            SaveLimitCommand = new RelayCommand(SaveLimit, () => HasSelection);
            AskServicedCommand = new RelayCommand(AskServiced, () => HasSelection);
            ConfirmServicedCommand = new RelayCommand(ConfirmServiced, () => IsConfirming);
            CancelConfirmCommand = new RelayCommand(() => CancelConfirm("취소했습니다."), () => IsConfirming);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => Service != null && _openFolder != null);
        }

        private MaintenanceCounterService Service => _serviceProvider();

        /// <summary>카운터가 시작되었는지 (시뮬레이션처럼 장비 창구가 없으면 false)</summary>
        public bool IsAvailable { get => _isAvailable; private set => SetProperty(ref _isAvailable, value); }

        public string UnavailableText => "유지보수 카운터가 시작되지 않았습니다. 실제 장비로 보낸 명령만 세므로 장비 창구가 있어야 합니다.";

        public string Title => "동작 횟수 · 실제 장비로 보낸 명령만 셉니다 (시뮬레이션은 세지 않음)";

        public string HistoryTitle => $"점검 이력 (최근 {HistoryRows}건)";

        public string Footnote =>
            "점검 주기에 도달하면 경고 로그만 남기고 장비는 멈추지 않습니다. 점검 완료는 로그인한 사용자, 점검 주기 변경은 관리자만 할 수 있습니다.\n" +
            "점검 완료는 '점검 후 횟수'만 0으로 되돌리고 누적 횟수는 그대로 둡니다.";

        public ObservableCollection<MaintenanceCounterRow> Rows { get; } = new ObservableCollection<MaintenanceCounterRow>();

        public ObservableCollection<MaintenanceHistoryItem> History { get; } = new ObservableCollection<MaintenanceHistoryItem>();

        public MaintenanceCounterRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetProperty(ref _selectedRow, value)) return;
                CancelConfirm(null);
                if (value != null)
                {
                    LimitInput = Math.Min(MaxLimit, value.Limit).ToString("N0");
                    Note = "";
                }
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedName));
            }
        }

        public bool HasSelection => _selectedRow != null;

        public string SelectedName => _selectedRow?.Name ?? "항목을 고르세요";

        /// <summary>점검 주기 입력 (회, 0 = 없음). 1,000 같은 자리 구분 쉼표를 써도 된다.</summary>
        public string LimitInput { get => _limitInput; set => SetProperty(ref _limitInput, value ?? ""); }

        public string Note { get => _note; set => SetProperty(ref _note, value ?? ""); }

        public string Message { get => _message; private set => SetProperty(ref _message, value); }

        public MaintenanceMessageTone MessageTone { get => _messageTone; private set => SetProperty(ref _messageTone, value); }

        public bool IsConfirming => _pendingServiceKey != null;

        public string ConfirmText { get => _confirmText; private set => SetProperty(ref _confirmText, value); }

        /// <summary>점검 주기에 도달한 항목 수 (유지보수 화면 탭 배지, WPF 추가)</summary>
        public int DueCount { get => _dueCount; private set { if (SetProperty(ref _dueCount, value)) OnPropertyChanged(nameof(HasDue)); } }

        public bool HasDue => DueCount > 0;

        public int SoonCount { get => _soonCount; private set => SetProperty(ref _soonCount, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        public ICommand SaveLimitCommand { get; }
        public ICommand AskServicedCommand { get; }
        public ICommand ConfirmServicedCommand { get; }
        public ICommand CancelConfirmCommand { get; }
        public ICommand OpenFolderCommand { get; }

        /// <summary>보는 곳이 하나 생겼다. 처음이면 1초마다 갱신을 시작한다.</summary>
        public void Attach()
        {
            _viewers++;
            if (_viewers != 1) return;
            RefreshAll();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
                _timer.Tick += (s, e) =>
                {
                    _ticks++;
                    RefreshCounters();
                    if (_ticks % 5 == 0) RefreshHistory();
                };
            }
            _timer.Start();
            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>보는 곳이 하나 사라졌다. 남은 곳이 없으면 갱신을 멈추고 묻던 질문을 거둔다.</summary>
        public void Detach()
        {
            if (_viewers == 0) return;
            _viewers--;
            if (_viewers != 0) return;
            _timer?.Stop();
            CancelConfirm(null);
            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>Esc: 묻는 중이면 취소하고 true</summary>
        public bool CancelIfConfirming()
        {
            if (!IsConfirming) return false;
            CancelConfirm("취소했습니다.");
            return true;
        }

        public void RefreshAll()
        {
            RefreshCounters();
            RefreshHistory();
        }

        public void RefreshCounters()
        {
            var service = Service;
            IsAvailable = service != null;
            if (service == null)
            {
                if (Rows.Count > 0) Rows.Clear();
                DueCount = 0;
                SoonCount = 0;
                return;
            }

            var counters = service.GetCounters();
            // 항목 목록은 바뀌지 않으므로 줄을 다시 만들지 않고 값만 바꾼다 (선택 유지)
            if (Rows.Count != counters.Count || Rows.Select(r => r.Key).Where((k, i) => k != counters[i].Key).Any())
            {
                string selectedKey = _selectedRow?.Key;
                Rows.Clear();
                foreach (var c in counters) Rows.Add(new MaintenanceCounterRow(c.Key, c.Name, c.Source));
                SelectedRow = Rows.FirstOrDefault(r => r.Key == selectedKey);
            }
            for (int i = 0; i < counters.Count; i++) Rows[i].Apply(counters[i]);
            DueCount = counters.Count(c => c.Status == MaintenanceCounterStatus.Due);
            SoonCount = counters.Count(c => c.Status == MaintenanceCounterStatus.Soon);
        }

        public void RefreshHistory()
        {
            var service = Service;
            var rows = service == null ? (IReadOnlyList<MaintenanceHistoryRow>)Array.Empty<MaintenanceHistoryRow>() : service.ReadHistory(HistoryRows);
            History.Clear();
            foreach (var r in rows) History.Add(new MaintenanceHistoryItem(r));
        }

        /// <summary>"10,000"이나 "10000"을 읽는다. 0 이상, 최대 10억.</summary>
        public static bool TryParseLimit(string text, out long limit)
        {
            limit = 0;
            string s = (text ?? "").Trim().Replace(",", "").Replace("회", "").Trim();
            if (s.Length == 0) return false;
            return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit <= MaxLimit;
        }

        private MaintenanceCounterState CurrentState(string key) =>
            Service?.GetCounters().FirstOrDefault(c => c.Key == key);

        private void SaveLimit()
        {
            var service = Service;
            var c = _selectedRow == null ? null : CurrentState(_selectedRow.Key);
            if (service == null || c == null) return;

            if (!_isLoggedIn() || !_isAdmin())
            {
                ShowMessage("점검 주기 변경은 관리자로 로그인한 뒤에 할 수 있습니다.", MaintenanceMessageTone.Danger);
                return;
            }
            if (!TryParseLimit(LimitInput, out long limit))
            {
                ShowMessage($"점검 주기는 0부터 {MaxLimit:N0} 사이의 횟수로 입력하세요 (0 = 없음).", MaintenanceMessageTone.Danger);
                return;
            }
            if (limit == c.Limit)
            {
                ShowMessage($"{c.Name}: 점검 주기가 이미 {MaintenanceCounterService.FormatLimit(limit)}입니다.", MaintenanceMessageTone.Info);
                return;
            }

            service.SetLimit(c.Key, limit, _currentUser(), _clock());
            ShowMessage($"{c.Name}: 점검 주기를 {MaintenanceCounterService.FormatLimit(c.Limit)} → {MaintenanceCounterService.FormatLimit(limit)}로 바꿨습니다.",
                MaintenanceMessageTone.Success);
            LimitInput = limit.ToString("N0");
            RefreshAll();
        }

        private void AskServiced()
        {
            var c = _selectedRow == null ? null : CurrentState(_selectedRow.Key);
            if (c == null) return;

            if (!_isLoggedIn())
            {
                ShowMessage("점검 완료는 로그인한 뒤에 할 수 있습니다.", MaintenanceMessageTone.Danger);
                return;
            }

            _pendingServiceKey = c.Key;
            ConfirmText = $"{c.Name}: 점검 후 횟수 {c.SinceService:N0}회를 0으로 되돌리고 이력에 남길까요? (누적 {c.Total:N0}회는 그대로)";
            OnPropertyChanged(nameof(IsConfirming));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ConfirmServiced()
        {
            string key = _pendingServiceKey;
            var service = Service;
            var c = key == null ? null : CurrentState(key);
            CancelConfirm(null);
            if (service == null || c == null) return;

            if (!_isLoggedIn())
            {
                ShowMessage("점검 완료는 로그인한 뒤에 할 수 있습니다.", MaintenanceMessageTone.Danger);
                return;
            }

            service.MarkServiced(key, _currentUser(), Note.Trim(), _clock());
            Note = "";
            ShowMessage($"{c.Name}: 점검 완료로 기록했습니다 ({c.SinceService:N0}회 → 0).", MaintenanceMessageTone.Success);
            RefreshAll();
        }

        private void CancelConfirm(string message)
        {
            if (_pendingServiceKey != null)
            {
                _pendingServiceKey = null;
                OnPropertyChanged(nameof(IsConfirming));
                CommandManager.InvalidateRequerySuggested();
            }
            if (message != null) ShowMessage(message, MaintenanceMessageTone.Info);
        }

        private void ShowMessage(string text, MaintenanceMessageTone tone)
        {
            Message = text;
            MessageTone = tone;
        }

        private void OpenFolder()
        {
            var service = Service;
            if (service == null) return;
            try
            {
                System.IO.Directory.CreateDirectory(service.Folder);
                _openFolder?.Invoke(service.Folder);
            }
            catch (Exception ex)
            {
                ShowMessage($"폴더를 열 수 없습니다: {ex.Message}", MaintenanceMessageTone.Danger);
            }
        }
    }
}
