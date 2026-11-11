using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using SemiconductorUi.Repositories;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>레벨 거르기 칩 하나 (개수 표시)</summary>
    public sealed class LogLevelFilter : ObservableObject
    {
        private int _count;

        public LogLevelFilter(string level, string label)
        {
            Level = level;
            Label = label;
        }

        /// <summary>null이면 전체</summary>
        public string Level { get; }
        public string Label { get; }

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }
    }

    /// <summary>
    /// 보고서 화면 (원본 Forms.ReportForm: 로그 보기와 CSV 내보내기).
    /// 원본과 다른 점:
    /// - 모달 창이 아니라 메인 화면 '보고서' 메뉴 화면이고, 새 로그가 생기면 바로 목록에 나온다.
    /// - 레벨 칩마다 개수를 보여 주고, 메시지 검색을 할 수 있다.
    /// - 내보내기는 지금 보이는(거른) 로그를 파일 하나로, 날짜까지 넣어 저장한다 (원본은 레벨마다 파일을 나눔).
    ///   저장 뒤 폴더를 열 수 있다.
    /// </summary>
    public sealed class ReportViewModel : ObservableObject
    {
        private readonly ObservableCollection<LogEntry> _source;
        private readonly IDialogService _dialogs;
        private readonly Func<IEnumerable<LogCsvRow>, string, string> _export;
        private readonly Action<string> _openFolder;
        private LogLevelFilter _selectedLevel;
        private string _search = string.Empty;
        private string _lastExportPath;

        /// <param name="source">메인 화면 로그 (최신이 앞)</param>
        /// <param name="export">행과 태그를 받아 저장하고 경로를 돌려준다</param>
        public ReportViewModel(ObservableCollection<LogEntry> source, IDialogService dialogs,
            Func<IEnumerable<LogCsvRow>, string, string> export, Action<string> openFolder)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _export = export ?? throw new ArgumentNullException(nameof(export));
            _openFolder = openFolder;

            // 원본 comboLevel: ALL, INFO, WARN, ALARM, ERROR, CRITICAL
            Levels = new[]
            {
                new LogLevelFilter(null, "전체"),
                new LogLevelFilter("INFO", "INFO"),
                new LogLevelFilter("WARN", "WARN"),
                new LogLevelFilter("ALARM", "ALARM"),
                new LogLevelFilter("ERROR", "ERROR"),
                new LogLevelFilter("CRITICAL", "CRITICAL"),
            };
            _selectedLevel = Levels[0];

            View = new ListCollectionView(_source) { Filter = o => Matches((LogEntry)o) };
            _source.CollectionChanged += OnSourceChanged;

            ExportCommand = new RelayCommand(Export, () => VisibleCount > 0);
            OpenFolderCommand = new RelayCommand(() => _openFolder?.Invoke(_lastExportPath), () => _lastExportPath != null && _openFolder != null);
            ClearSearchCommand = new RelayCommand(() => Search = string.Empty, () => !string.IsNullOrEmpty(_search));
            UpdateCounts();
        }

        public IReadOnlyList<LogLevelFilter> Levels { get; }

        /// <summary>거른 로그 (최신이 위)</summary>
        public ICollectionView View { get; }

        public LogLevelFilter SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (SetProperty(ref _selectedLevel, value ?? Levels[0])) Reapply();
            }
        }

        public string Search
        {
            get => _search;
            set
            {
                if (SetProperty(ref _search, value ?? string.Empty)) Reapply();
            }
        }

        public int VisibleCount => _source.Count(Matches);

        public string SummaryText => $"{VisibleCount:N0}건 표시 · 전체 {_source.Count:N0}건 (최근 {Math.Max(50, AppSettings.MaxLogEntries):N0}건까지 보관)";

        public string LastExportPath
        {
            get => _lastExportPath;
            private set
            {
                if (SetProperty(ref _lastExportPath, value)) OnPropertyChanged(nameof(HasExport));
            }
        }

        public bool HasExport => _lastExportPath != null;

        public ICommand ExportCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ClearSearchCommand { get; }

        internal bool Matches(LogEntry e)
        {
            if (e == null) return false;
            if (_selectedLevel?.Level != null && !LevelIs(e, _selectedLevel.Level)) return false;
            return string.IsNullOrWhiteSpace(_search) || e.Message.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LevelIs(LogEntry e, string level) =>
            string.Equals(e.Level, level, StringComparison.OrdinalIgnoreCase)
            || (level == "WARN" && e.Level == "WARNING");

        private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateCounts();

        private void Reapply()
        {
            View.Refresh();
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            foreach (var l in Levels)
            {
                l.Count = l.Level == null ? _source.Count : _source.Count(e => LevelIs(e, l.Level));
            }
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(SummaryText));
            RelayCommand.Refresh();
        }

        /// <summary>원본 buttonExport_Click → ExportLogsToCsv. 보이는 로그를 시간 순으로 저장한다.</summary>
        private void Export()
        {
            try
            {
                var rows = _source.Where(Matches).Reverse().Select(e => new LogCsvRow(e.Time, e.Level, e.Message)).ToList();
                var tag = _selectedLevel?.Level ?? "ALL";
                var path = _export(rows, tag);
                LastExportPath = path;
                RelayCommand.Refresh();
                _dialogs.Info($"CSV 저장 완료 ({rows.Count:N0}건):\n{path}", "완료");
            }
            catch (Exception ex)
            {
                _dialogs.Error($"CSV 저장 중 오류가 발생했습니다:\n{ex.Message}", "오류");
            }
        }
    }
}
