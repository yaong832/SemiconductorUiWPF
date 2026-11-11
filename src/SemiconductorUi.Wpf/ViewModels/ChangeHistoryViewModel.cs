using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>보고서 화면 위 작은 탭</summary>
    public enum ReportTab
    {
        Log,
        Changes
    }

    /// <summary>기간 고르기 (원본 ChangeHistoryForm.PeriodNames/PeriodDays)</summary>
    public sealed class ChangeHistoryPeriod
    {
        public ChangeHistoryPeriod(string name, int days)
        {
            Name = name;
            Days = days;
        }

        public string Name { get; }
        public int Days { get; }
        public override string ToString() => Name;
    }

    /// <summary>변경 이력 한 줄</summary>
    public sealed class ChangeHistoryItem
    {
        public ChangeHistoryItem(ChangeHistoryRow r)
        {
            TimeText = r.Time.ToString("yyyy-MM-dd HH:mm:ss");
            UserText = string.IsNullOrEmpty(r.Role) ? r.User : $"{r.User} ({r.Role})";
            Category = r.Category;
            Target = r.Target;
            Item = r.Item;
            Before = r.Before;
            After = r.After;
        }

        public string TimeText { get; }
        public string UserText { get; }
        public string Category { get; }
        public string Target { get; }
        public string Item { get; }
        public string Before { get; }
        public string After { get; }

        internal bool Matches(string search) =>
            Contains(UserText, search) || Contains(Target, search) || Contains(Item, search) ||
            Contains(Before, search) || Contains(After, search) || Contains(Category, search);

        private static bool Contains(string text, string search) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 변경 이력 (원본 Forms.ChangeHistoryForm): 누가 언제 티칭 위치, 서보 속도, 레시피, 알람 임계값, 사용자를 바꿨는지.
    /// 읽기 전용. 원본은 보고서 창의 버튼으로 여는 별도 창이었고, WPF는 보고서 화면의 탭이다.
    /// WPF 추가: 사용자·대상·항목·값 검색
    /// </summary>
    public sealed class ChangeHistoryViewModel : ObservableObject
    {
        public const int MaxRows = 2000;
        public const string AllCategories = "전체";

        private readonly Func<DateTime, DateTime, int, List<ChangeHistoryRow>> _readRange;
        private readonly Func<string> _folder;
        private readonly Action<string> _openFolder;
        private readonly Func<DateTime> _today;
        private List<ChangeHistoryItem> _all = new List<ChangeHistoryItem>();
        private ChangeHistoryPeriod _selectedPeriod;
        private string _selectedCategory = AllCategories;
        private string _search = string.Empty;
        private string _countText = "";

        public ChangeHistoryViewModel(Action<string> openFolder,
            Func<DateTime, DateTime, int, List<ChangeHistoryRow>> readRange = null, Func<string> folder = null, Func<DateTime> today = null)
        {
            _openFolder = openFolder;
            _readRange = readRange ?? ChangeHistory.ReadRange;
            _folder = folder ?? (() => ChangeHistory.Folder);
            _today = today ?? (() => DateTime.Today);

            Periods = new[]
            {
                new ChangeHistoryPeriod("오늘", 1),
                new ChangeHistoryPeriod("최근 7일", 7),
                new ChangeHistoryPeriod("최근 30일", 30),
                new ChangeHistoryPeriod("최근 1년", 365),
            };
            _selectedPeriod = Periods[2];
            Categories = new[] { AllCategories }.Concat(ChangeHistory.Categories).ToList();

            RefreshCommand = new RelayCommand(Refresh);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => _openFolder != null);
            ClearSearchCommand = new RelayCommand(() => Search = string.Empty, () => Search.Length > 0);
        }

        public IReadOnlyList<ChangeHistoryPeriod> Periods { get; }

        public IReadOnlyList<string> Categories { get; }

        public ChangeHistoryPeriod SelectedPeriod
        {
            get => _selectedPeriod;
            set
            {
                if (value != null && SetProperty(ref _selectedPeriod, value)) Refresh();
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (value != null && SetProperty(ref _selectedCategory, value)) Refresh();
            }
        }

        public string Search
        {
            get => _search;
            set
            {
                if (SetProperty(ref _search, value ?? string.Empty)) ApplySearch();
            }
        }

        public ObservableCollection<ChangeHistoryItem> Rows { get; } = new ObservableCollection<ChangeHistoryItem>();

        public bool IsEmpty => Rows.Count == 0;

        public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

        public string Footnote =>
            "티칭 위치·서보 속도·레시피·알람 임계값·사용자를 저장할 때 바뀐 값만 남깁니다. 비밀번호 값은 남기지 않습니다.\n" +
            "파일은 지우지 않습니다 (문서\\SemiconductorUi_Logs\\Changes\\Change_날짜.csv).";

        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ClearSearchCommand { get; }

        /// <summary>파일에서 다시 읽는다 (탭을 열 때, 기간·구분을 바꿀 때, 새로고침)</summary>
        public void Refresh()
        {
            var today = _today().Date;
            var rows = _readRange(today.AddDays(-(_selectedPeriod.Days - 1)), today, MaxRows) ?? new List<ChangeHistoryRow>();
            bool capped = rows.Count >= MaxRows;
            if (!string.IsNullOrEmpty(_selectedCategory) && _selectedCategory != AllCategories)
            {
                rows = rows.Where(r => r.Category == _selectedCategory).ToList();
            }
            _all = rows.Select(r => new ChangeHistoryItem(r)).ToList();
            _capped = capped;
            ApplySearch();
        }

        private bool _capped;

        private void ApplySearch()
        {
            string search = _search.Trim();
            var shown = search.Length == 0 ? _all : _all.Where(r => r.Matches(search)).ToList();
            Rows.Clear();
            foreach (var r in shown) Rows.Add(r);
            OnPropertyChanged(nameof(IsEmpty));

            string count = _capped ? $"{_all.Count:N0}건 (최근 {MaxRows:N0}건까지)" : $"{_all.Count:N0}건";
            CountText = search.Length == 0 ? count : $"{shown.Count:N0}건 찾음 · 전체 {count}";
        }

        private void OpenFolder()
        {
            try
            {
                string folder = _folder();
                System.IO.Directory.CreateDirectory(folder);
                _openFolder?.Invoke(folder);
            }
            catch (Exception ex)
            {
                CountText = $"폴더를 열 수 없습니다: {ex.Message}";
            }
        }
    }
}
