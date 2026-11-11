using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>현재 알람 한 줄</summary>
    public sealed class ActiveAlarmRow
    {
        public ActiveAlarmRow(AlarmRecord record, DateTime now)
        {
            Code = record.Code.ToString();
            Unit = record.Unit;
            Name = record.Name;
            Detail = record.Detail;
            SetTime = record.SetTime;
            SetTimeText = record.SetTime.ToString(record.SetTime.Date == now.Date ? "HH:mm:ss" : "MM-dd HH:mm:ss");
            Elapsed = AlarmHistoryViewModel.FormatElapsed(now - record.SetTime);
        }

        public string Code { get; }
        public string Unit { get; }
        public string Name { get; }
        public string Detail { get; }
        public DateTime SetTime { get; }
        public string SetTimeText { get; }
        /// <summary>걸려 있은 시간 (WPF 추가: "12분째")</summary>
        public string Elapsed { get; }
    }

    /// <summary>이력 한 줄 (파일에서 읽은 값)</summary>
    public sealed class AlarmHistoryItem
    {
        public AlarmHistoryItem(AlarmHistoryRow row)
        {
            Row = row;
        }

        public AlarmHistoryRow Row { get; }
        public string Time => Row.Time;
        public string Event => Row.Event;
        public string Code => Row.Code;
        public string Unit => Row.Unit;
        public string Name => Row.Name;
        public string Detail => Row.Detail;
        public string User => Row.User;
        public bool IsSet => Row.Event == AlarmHistoryService.EventSet;
    }

    /// <summary>
    /// 원본 Forms.AlarmHistoryForm (8baff6e): 현재 알람과 날짜별 알람 이력. 보기 전용이며
    /// 알람 해제는 메인 화면의 [알람 리셋]으로 한다.
    /// WPF 추가: 이전/다음 날 버튼, 발생만 보기, 검색, 현재 알람이 걸려 있은 시간.
    /// </summary>
    public sealed class AlarmHistoryViewModel : ObservableObject
    {
        private readonly AlarmHistoryService _service;
        private readonly Action<string> _openFolder;
        private readonly Func<DateTime> _clock;
        private List<AlarmHistoryItem> _dayRows = new List<AlarmHistoryItem>();
        private DateTime _selectedDate;
        private bool _onlySet;
        private string _search = "";
        private string _historyInfo = "";
        private bool _isOpen;

        public AlarmHistoryViewModel(AlarmHistoryService service, Action<string> openFolder, Func<DateTime> clock = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _openFolder = openFolder;
            _clock = clock ?? (() => DateTime.Now);
            _selectedDate = _clock().Date;

            TodayCommand = new RelayCommand(() => SelectedDate = _clock().Date, () => SelectedDate != _clock().Date);
            PreviousDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(-1));
            NextDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(1), () => SelectedDate < _clock().Date);
            RefreshCommand = new RelayCommand(RefreshAll);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => !string.IsNullOrEmpty(_service.Folder));
            CloseCommand = new RelayCommand(Close);
        }

        public ObservableCollection<ActiveAlarmRow> Active { get; } = new ObservableCollection<ActiveAlarmRow>();
        public ObservableCollection<AlarmHistoryItem> History { get; } = new ObservableCollection<AlarmHistoryItem>();

        public ICommand TodayCommand { get; }
        public ICommand PreviousDayCommand { get; }
        public ICommand NextDayCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CloseCommand { get; }

        public bool HasActive => Active.Count > 0;
        public string ActiveTitle => Active.Count == 0 ? "현재 알람: 없음" : $"현재 알람: {Active.Count}건";

        /// <summary>창이 떠 있어야 하는지 (false가 되면 창을 숨긴다)</summary>
        public bool IsOpen
        {
            get => _isOpen;
            private set => SetProperty(ref _isOpen, value);
        }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                var date = value.Date;
                if (date > _clock().Date) date = _clock().Date;
                if (SetProperty(ref _selectedDate, date))
                {
                    OnPropertyChanged(nameof(SelectedDateText));
                    RefreshHistory();
                }
            }
        }

        public string SelectedDateText => _selectedDate.ToString("yyyy-MM-dd (ddd)", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));

        public bool OnlySet
        {
            get => _onlySet;
            set
            {
                if (SetProperty(ref _onlySet, value)) ApplyFilter();
            }
        }

        public string Search
        {
            get => _search;
            set
            {
                if (SetProperty(ref _search, value ?? "")) ApplyFilter();
            }
        }

        public string HistoryInfo
        {
            get => _historyInfo;
            private set => SetProperty(ref _historyInfo, value);
        }

        public bool HasNoHistory => History.Count == 0;

        public string EmptyHistoryText => _dayRows.Count == 0 ? "이 날짜에는 알람 기록이 없습니다." : "조건에 맞는 기록이 없습니다.";

        public void Open()
        {
            RefreshAll();
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public void RefreshAll()
        {
            RefreshActive();
            RefreshHistory();
        }

        /// <summary>서비스에서 알람이 바뀌었을 때 (UI 스레드에서 부를 것)</summary>
        public void OnServiceChanged()
        {
            RefreshActive();
            if (_selectedDate == _clock().Date) RefreshHistory();
        }

        public void RefreshActive()
        {
            var now = _clock();
            Active.Clear();
            // 최신 알람이 위로
            foreach (var record in _service.GetActive().OrderByDescending(a => a.SetTime))
            {
                Active.Add(new ActiveAlarmRow(record, now));
            }
            OnPropertyChanged(nameof(HasActive));
            OnPropertyChanged(nameof(ActiveTitle));
        }

        public void RefreshHistory()
        {
            // 파일은 시간순이므로 뒤집어 최신이 위로
            var rows = _service.ReadDay(_selectedDate);
            _dayRows = rows.AsEnumerable().Reverse().Select(r => new AlarmHistoryItem(r)).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var search = (_search ?? "").Trim();
            History.Clear();
            foreach (var item in _dayRows)
            {
                if (_onlySet && !item.IsSet) continue;
                if (search.Length > 0 && !Matches(item, search)) continue;
                History.Add(item);
            }

            int setCount = _dayRows.Count(r => r.IsSet);
            int clearCount = _dayRows.Count - setCount;
            string info = $"발생 {setCount}건 · 해제 {clearCount}건";
            if (History.Count != _dayRows.Count) info += $" · {History.Count}건 표시";
            if (_service.Folder == null) info += " · 이력 파일 기록이 꺼져 있습니다";
            else if (_service.IsFileFailing) info += " · 이력 파일 쓰기 실패 중";
            HistoryInfo = info;
            OnPropertyChanged(nameof(HasNoHistory));
            OnPropertyChanged(nameof(EmptyHistoryText));
        }

        private static bool Matches(AlarmHistoryItem item, string search)
        {
            bool Has(string s) => s != null && s.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(item.Code) || Has(item.Unit) || Has(item.Name) || Has(item.Detail) || Has(item.User);
        }

        private void OpenFolder()
        {
            var path = _service.GetFilePath(_selectedDate);
            if (string.IsNullOrEmpty(path)) return;
            _openFolder?.Invoke(System.IO.File.Exists(path) ? path : _service.Folder);
        }

        internal static string FormatElapsed(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalMinutes < 1) return "방금";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}분째";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}시간 {span.Minutes}분째";
            return $"{(int)span.TotalDays}일 {span.Hours}시간째";
        }
    }
}
