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
    /// <summary>웨이퍼 한 장이 챔버 하나에서 어떤 상태인지 (색과 글자)</summary>
    public enum WaferStepState { NotVisited, Waiting, Processing, Done, Over, Aborted }

    /// <summary>선택한 웨이퍼의 챔버 하나 (상세 카드, WPF 추가)</summary>
    public sealed class WaferStepDetail
    {
        public WaferStepDetail(WaferStepRecord s, bool aborted, DateTime now)
        {
            Title = "Chamber " + s.Chamber;
            Visited = s.Visited;
            InText = WaferHistoryFormat.Time(s.In, false);
            StartText = WaferHistoryFormat.Time(s.Start, false);
            EndText = s.End.HasValue ? WaferHistoryFormat.Time(s.End, false) : (s.Start.HasValue ? (aborted ? "끝나지 않음" : "진행 중") : "-");
            OutText = WaferHistoryFormat.Time(s.Out, false);
            SetSeconds = s.SetSeconds;
            State = WaferHistoryFormat.StateOf(s, aborted);

            double? actual = s.ActualSeconds;
            if (!actual.HasValue && s.Start.HasValue && State == WaferStepState.Processing)
            {
                actual = Math.Max(0, (now - s.Start.Value).TotalSeconds);
            }

            ActualText = actual.HasValue ? $"{actual.Value.ToString("0.0", CultureInfo.InvariantCulture)}초" : "-";
            SetText = s.SetSeconds > 0 ? $"{s.SetSeconds}초" : "-";
            Ratio = actual.HasValue && s.SetSeconds > 0 ? Math.Min(1.0, actual.Value / s.SetSeconds) : 0;
            if (!Visited) State = WaferStepState.NotVisited;
            StateText = Visited ? WaferHistoryFormat.StateText(State) : "거치지 않음";
        }

        public string Title { get; }
        public bool Visited { get; }
        public string InText { get; }
        public string StartText { get; }
        public string EndText { get; }
        public string OutText { get; }
        public int SetSeconds { get; }
        public string ActualText { get; }
        public string SetText { get; }
        /// <summary>실제/레시피 (0~1, 막대용)</summary>
        public double Ratio { get; }
        public WaferStepState State { get; }
        public string StateText { get; }
    }

    /// <summary>웨이퍼 이력 한 줄. 진행 중인 줄은 1초마다 같은 항목을 고쳐 쓴다 (목록이 다시 그려지지 않도록).</summary>
    public sealed class WaferHistoryItem : ObservableObject
    {
        private WaferRunRecord _record;
        private string _endText;
        private string _resultText;
        private string _route;
        private string _aCell;
        private string _bCell;
        private string _cCell;
        private WaferStepState _aState;
        private WaferStepState _bState;
        private WaferStepState _cState;
        private string _totalText;
        private string _note;

        public WaferHistoryItem(WaferRunRecord r, DateTime now)
        {
            Key = KeyOf(r);
            Run = r.Run;
            WaferText = "#" + r.WaferId;
            Update(r, now);
        }

        public string Key { get; }
        public string Run { get; }
        public string WaferText { get; }
        public WaferRunRecord Record => _record;

        public string Recipe => _record.Recipe;
        public bool IsRunning => _record.Result == WaferRunRecord.ResultRunning;
        public bool IsAborted => _record.Result == WaferRunRecord.ResultAborted;

        public string EndText { get => _endText; private set => SetProperty(ref _endText, value); }
        public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }
        public string Route { get => _route; private set => SetProperty(ref _route, value); }
        public string ACell { get => _aCell; private set => SetProperty(ref _aCell, value); }
        public string BCell { get => _bCell; private set => SetProperty(ref _bCell, value); }
        public string CCell { get => _cCell; private set => SetProperty(ref _cCell, value); }
        public WaferStepState AState { get => _aState; private set => SetProperty(ref _aState, value); }
        public WaferStepState BState { get => _bState; private set => SetProperty(ref _bState, value); }
        public WaferStepState CState { get => _cState; private set => SetProperty(ref _cState, value); }
        public string TotalText { get => _totalText; private set => SetProperty(ref _totalText, value); }
        public string Note { get => _note; private set => SetProperty(ref _note, value); }

        internal static string KeyOf(WaferRunRecord r) => r.Run + "#" + r.WaferId;

        internal void Update(WaferRunRecord r, DateTime now)
        {
            _record = r;
            bool aborted = IsAborted;
            EndText = IsRunning ? "진행 중" : WaferHistoryFormat.Time(r.EndTime, true);
            ResultText = r.Result;
            Route = r.Route;
            ACell = WaferHistoryFormat.StepCell(r.A, aborted, now);
            BCell = WaferHistoryFormat.StepCell(r.B, aborted, now);
            CCell = WaferHistoryFormat.StepCell(r.C, aborted, now);
            AState = r.A.Visited ? WaferHistoryFormat.StateOf(r.A, aborted) : WaferStepState.NotVisited;
            BState = r.B.Visited ? WaferHistoryFormat.StateOf(r.B, aborted) : WaferStepState.NotVisited;
            CState = r.C.Visited ? WaferHistoryFormat.StateOf(r.C, aborted) : WaferStepState.NotVisited;
            double? total = r.TotalSeconds;
            if (!total.HasValue && IsRunning && r.FoupAOut.HasValue) total = (now - r.FoupAOut.Value).TotalSeconds;
            TotalText = WaferHistoryFormat.Duration(total);
            Note = r.Note;
            OnPropertyChanged(nameof(Recipe));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsAborted));
        }

        /// <summary>검색 (WPF 추가): 작업 번호, "#웨이퍼", 레시피, 비고, 사용자, 경로</summary>
        internal static bool Matches(WaferRunRecord r, string search) =>
            Contains(r.Run, search) || Contains("#" + r.WaferId, search) || Contains(r.Recipe, search) ||
            Contains(r.Note, search) || Contains(r.User, search) || Contains(r.Route, search);

        private static bool Contains(string text, string search) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>원본 WaferHistoryForm의 글자 만들기 (StepCell, Time, Duration)</summary>
    public static class WaferHistoryFormat
    {
        /// <summary>실제 시간이 레시피보다 이만큼 넘으면 "초과"로 칠한다 (WPF 추가). 알람 홀드도 실제 시간에 들어간다.</summary>
        public const double OverRatio = 1.10;

        public static WaferStepState StateOf(WaferStepRecord s, bool aborted)
        {
            if (s.ActualSeconds.HasValue)
            {
                return s.SetSeconds > 0 && s.ActualSeconds.Value > s.SetSeconds * OverRatio ? WaferStepState.Over : WaferStepState.Done;
            }
            if (aborted) return WaferStepState.Aborted;
            return s.Start.HasValue ? WaferStepState.Processing : WaferStepState.Waiting;
        }

        public static string StateText(WaferStepState state)
        {
            switch (state)
            {
                case WaferStepState.Done: return "완료";
                case WaferStepState.Over: return "레시피보다 김";
                case WaferStepState.Processing: return "공정 중";
                case WaferStepState.Waiting: return "공정 전";
                case WaferStepState.Aborted: return "중단";
                default: return "";
            }
        }

        /// <summary>원본 StepCell. WPF 추가: 진행 중이면 지금까지 흐른 시간도 보인다.</summary>
        public static string StepCell(WaferStepRecord s, bool aborted, DateTime now)
        {
            if (!s.Visited) return "";
            if (s.ActualSeconds.HasValue)
            {
                return $"{s.ActualSeconds.Value.ToString("0", CultureInfo.InvariantCulture)} / {s.SetSeconds}초";
            }
            if (aborted) return s.Start.HasValue ? "공정 중 중단" : "공정 전 중단";
            if (s.Start.HasValue)
            {
                double elapsed = Math.Max(0, (now - s.Start.Value).TotalSeconds);
                return $"공정 중 {elapsed.ToString("0", CultureInfo.InvariantCulture)} / {s.SetSeconds}초";
            }
            return "대기";
        }

        public static string Time(DateTime? t, bool withDate)
        {
            if (!t.HasValue) return "-";
            return t.Value.ToString(withDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static string Duration(double? seconds)
        {
            if (!seconds.HasValue) return "";
            var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds.Value)));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}시간 {span.Minutes}분"
                : span.TotalMinutes >= 1 ? $"{span.Minutes}분 {span.Seconds}초" : $"{span.Seconds}초";
        }
    }

    /// <summary>
    /// 웨이퍼 이력 (원본 Forms.WaferHistoryForm): 웨이퍼마다 어느 챔버에 언제 들어가 몇 초 공정했고 언제 FOUP B에 들어갔는지.
    /// 읽기 전용. 원본은 보고서 창의 버튼으로 여는 별도 창이었고, WPF는 보고서 화면의 탭이며 별도 창으로도 띄울 수 있다.
    /// 보이는 동안만 1초마다 확인해서, 기록이 바뀌면 파일을 다시 읽고 진행 중인 줄은 흐른 시간을 고쳐 쓴다.
    /// WPF 추가: 검색, 완료·중단 수와 평균 시간, 챔버별 상세 카드(실제/레시피 막대), 진행 중 흐른 시간
    /// </summary>
    public sealed class WaferHistoryViewModel : ObservableObject
    {
        public const int MaxRows = 2000;
        public const string AllResults = "전체";

        private readonly Func<WaferHistoryService> _history;
        private readonly Action<string> _openFolder;
        private readonly Action _openWindow;
        private readonly Func<DateTime> _clock;
        private List<WaferRunRecord> _saved = new List<WaferRunRecord>();
        private bool _capped;
        private int _shownVersion = -1;
        private DispatcherTimer _timer;
        private int _viewers;
        private ChangeHistoryPeriod _selectedPeriod;
        private string _selectedResult = AllResults;
        private string _search = string.Empty;
        private string _countText = "";
        private string _summaryText = "";
        private WaferHistoryItem _selectedRow;
        private string _detailTitle = "";
        private string _detailSubtitle = "";
        private IReadOnlyList<WaferStepDetail> _detailSteps = Array.Empty<WaferStepDetail>();

        public WaferHistoryViewModel(Func<WaferHistoryService> history, Action<string> openFolder, Action openWindow = null,
            Func<DateTime> clock = null)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _openFolder = openFolder;
            _openWindow = openWindow;
            _clock = clock ?? (() => DateTime.Now);

            Periods = new[]
            {
                new ChangeHistoryPeriod("오늘", 1),
                new ChangeHistoryPeriod("최근 7일", 7),
                new ChangeHistoryPeriod("최근 30일", 30),
            };
            _selectedPeriod = Periods[1];
            Results = new[] { AllResults, WaferRunRecord.ResultDone, WaferRunRecord.ResultAborted };

            RefreshCommand = new RelayCommand(Refresh);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => _openFolder != null);
            OpenWindowCommand = new RelayCommand(() => _openWindow?.Invoke(), () => _openWindow != null);
            ClearSearchCommand = new RelayCommand(() => Search = string.Empty, () => Search.Length > 0);
        }

        public IReadOnlyList<ChangeHistoryPeriod> Periods { get; }

        public IReadOnlyList<string> Results { get; }

        public ChangeHistoryPeriod SelectedPeriod
        {
            get => _selectedPeriod;
            set
            {
                if (value != null && SetProperty(ref _selectedPeriod, value)) Refresh();
            }
        }

        public string SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (value != null && SetProperty(ref _selectedResult, value)) Refresh();
            }
        }

        public string Search
        {
            get => _search;
            set
            {
                if (SetProperty(ref _search, value ?? string.Empty)) Rebuild();
            }
        }

        public ObservableCollection<WaferHistoryItem> Rows { get; } = new ObservableCollection<WaferHistoryItem>();

        public bool IsEmpty => Rows.Count == 0;

        public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

        /// <summary>기간 안의 완료·중단 수와 평균 총 시간 (WPF 추가)</summary>
        public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

        public WaferHistoryItem SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value)) UpdateDetail();
            }
        }

        public bool HasSelection => _selectedRow != null;
        public string DetailTitle { get => _detailTitle; private set => SetProperty(ref _detailTitle, value); }
        public string DetailSubtitle { get => _detailSubtitle; private set => SetProperty(ref _detailSubtitle, value); }
        public IReadOnlyList<WaferStepDetail> DetailSteps { get => _detailSteps; private set => SetProperty(ref _detailSteps, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        public string Footnote =>
            "공정 시간은 실제 시간 / 레시피 시간(초)입니다. 알람 홀드로 멈춘 시간도 실제 시간에 들어갑니다. 긴급 정지·공정 리셋 때 진행 중이던 웨이퍼는 \"중단\"으로 남습니다.\n" +
            "FOUP A에서 꺼내기 전인 웨이퍼는 남기지 않습니다. 파일(문서\\SemiconductorUi_Logs\\Wafers\\Wafer_날짜.csv)은 지우지 않습니다.";

        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OpenWindowCommand { get; }
        public ICommand ClearSearchCommand { get; }

        /// <summary>보는 곳이 하나 생겼다 (보고서 탭, 별도 창). 처음이면 다시 읽고 1초 확인을 시작한다.</summary>
        public void Attach()
        {
            _viewers++;
            if (_viewers != 1) return;
            Refresh();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, e) => Tick();
            }
            _timer.Start();
            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>보는 곳이 하나 사라졌다. 남은 곳이 없으면 멈춘다.</summary>
        public void Detach()
        {
            if (_viewers == 0) return;
            _viewers--;
            if (_viewers != 0) return;
            _timer?.Stop();
            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>1초 확인: 기록이 바뀌었으면 파일까지 다시 읽고, 아니면 진행 중인 줄의 흐른 시간만 고친다.</summary>
        internal void Tick()
        {
            var history = _history();
            if (history == null) return;
            if (history.Version != _shownVersion) Refresh();
            else if (Rows.Any(r => r.IsRunning)) Rebuild();
        }

        /// <summary>파일에서 다시 읽는다 (보일 때, 기간·결과를 바꿀 때, 새로고침, 기록이 바뀔 때)</summary>
        public void Refresh()
        {
            var history = _history();
            if (history == null)
            {
                _saved = new List<WaferRunRecord>();
                Rebuild();
                return;
            }

            _shownVersion = history.Version;
            var today = _clock().Date;
            var saved = history.ReadRange(today.AddDays(-(_selectedPeriod.Days - 1)), today, MaxRows) ?? new List<WaferRunRecord>();
            _capped = saved.Count >= MaxRows;
            if (!string.IsNullOrEmpty(_selectedResult) && _selectedResult != AllResults)
            {
                saved = saved.Where(r => r.Result == _selectedResult).ToList();
            }
            _saved = saved;
            Rebuild();
        }

        /// <summary>진행 중(메모리) + 저장된 기록(파일)을 합쳐 목록을 만든다. 순서가 같으면 항목을 고쳐 쓰기만 한다.</summary>
        private void Rebuild()
        {
            var now = _clock();
            var records = new List<WaferRunRecord>();
            if (_selectedResult == null || _selectedResult == AllResults)
            {
                records.AddRange(_history()?.GetCurrent() ?? new List<WaferRunRecord>());
            }
            records.AddRange(_saved);

            string search = _search.Trim();
            var shown = search.Length == 0 ? records : records.Where(r => WaferHistoryItem.Matches(r, search)).ToList();
            if (shown.Select(WaferHistoryItem.KeyOf).SequenceEqual(Rows.Select(r => r.Key)))
            {
                // 같은 줄들: 항목만 고쳐 써서 선택과 스크롤을 그대로 둔다
                for (int i = 0; i < shown.Count; i++) Rows[i].Update(shown[i], now);
            }
            else
            {
                string selectedKey = _selectedRow?.Key;
                Rows.Clear();
                foreach (var r in shown) Rows.Add(new WaferHistoryItem(r, now));
                _selectedRow = selectedKey == null ? null : Rows.FirstOrDefault(r => r.Key == selectedKey);
                OnPropertyChanged(nameof(SelectedRow));
            }
            OnPropertyChanged(nameof(IsEmpty));
            UpdateDetail();

            int running = records.Count(r => r.Result == WaferRunRecord.ResultRunning);
            int savedCount = records.Count - running;
            string count = (running > 0 ? $"진행 중 {running}장 · " : "") +
                           (_capped ? $"기록 {savedCount:N0}장 (최근 {MaxRows:N0}장까지)" : $"기록 {savedCount:N0}장");
            CountText = search.Length == 0 ? count : $"{Rows.Count:N0}장 찾음 · {count}";

            int done = _saved.Count(r => r.Result == WaferRunRecord.ResultDone);
            int aborted = _saved.Count(r => r.Result == WaferRunRecord.ResultAborted);
            var totals = _saved.Where(r => r.Result == WaferRunRecord.ResultDone && r.TotalSeconds.HasValue).Select(r => r.TotalSeconds.Value).ToList();
            SummaryText = savedCount == 0 ? "" :
                $"완료 {done:N0} · 중단 {aborted:N0}" + (totals.Count > 0 ? $" · 완료 평균 {WaferHistoryFormat.Duration(totals.Average())}" : "");
        }

        private void UpdateDetail()
        {
            OnPropertyChanged(nameof(HasSelection));
            var item = _selectedRow;
            if (item == null)
            {
                DetailTitle = "";
                DetailSubtitle = "";
                DetailSteps = Array.Empty<WaferStepDetail>();
                return;
            }

            var r = item.Record;
            var now = _clock();
            DetailTitle = $"웨이퍼 #{r.WaferId} · 작업 {r.Run}";
            var parts = new List<string> { r.Recipe, r.Mode };
            if (!string.IsNullOrEmpty(r.User)) parts.Add("시작한 사용자 " + r.User);
            parts.Add("결과 " + r.Result + (string.IsNullOrEmpty(r.Note) ? "" : $" ({r.Note})"));
            parts.Add("FOUP A에서 꺼냄 " + WaferHistoryFormat.Time(r.FoupAOut, false));
            if (r.FoupBIn.HasValue) parts.Add($"FOUP B에 넣음 {WaferHistoryFormat.Time(r.FoupBIn, false)} · 총 {WaferHistoryFormat.Duration(r.TotalSeconds)}");
            DetailSubtitle = string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
            DetailSteps = r.Steps.Select(s => new WaferStepDetail(s, item.IsAborted, now)).ToList();
        }

        private void OpenFolder()
        {
            try
            {
                var history = _history();
                if (history == null) return;
                System.IO.Directory.CreateDirectory(history.Folder);
                _openFolder?.Invoke(history.Folder);
            }
            catch (Exception ex)
            {
                CountText = $"폴더를 열 수 없습니다: {ex.Message}";
            }
        }
    }
}
