using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>추이 그래프의 선 하나</summary>
    public sealed class TrendSeries
    {
        public TrendSeries(string label, Color color, bool dashed, IReadOnlyList<(DateTime t, double v)> points)
        {
            Label = label;
            Color = color;
            Dashed = dashed;
            Points = points;
        }

        public string Label { get; }
        public Color Color { get; }
        public bool Dashed { get; }
        public IReadOnlyList<(DateTime t, double v)> Points { get; }
    }

    /// <summary>표 한 줄 (원본 listTrend)</summary>
    public sealed class TrendRow
    {
        public TrendRow(TrendSample s)
        {
            Time = s.Time.ToString("HH:mm:ss");
            Unit = TrendViewModel.UnitLabel(s.Unit);
            PvTemp = s.PvTemp.ToString("0.0");
            SvTemp = s.SvTemp.ToString("0.0");
            PvPress = s.PvPress.ToString("0.###");
            SvPress = s.SvPress.ToString("0.###");
        }

        public string Time { get; }
        public string Unit { get; }
        public string PvTemp { get; }
        public string SvTemp { get; }
        public string PvPress { get; }
        public string SvPress { get; }
    }

    /// <summary>챔버 선택 칩</summary>
    public sealed class TrendUnitFilter : ObservableObject
    {
        private bool _isChecked = true;
        private readonly Action _changed;

        public TrendUnitFilter(string key, string label, Color color, Action changed)
        {
            Key = key;
            Label = label;
            Color = color;
            Brush = new SolidColorBrush(color);
            Brush.Freeze();
            _changed = changed;
        }

        public string Key { get; }
        public string Label { get; }
        public Color Color { get; }
        public Brush Brush { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value)) _changed();
            }
        }
    }

    /// <summary>보여 줄 시간 범위</summary>
    public sealed class TrendWindowOption
    {
        public TrendWindowOption(string label, TimeSpan? span)
        {
            Label = label;
            Span = span;
        }

        public string Label { get; }

        /// <summary>null이면 전체</summary>
        public TimeSpan? Span { get; }

        public override string ToString() => Label;
    }

    /// <summary>
    /// 추이 화면 (원본 Forms.TrendForm). 시뮬레이션 틱마다 쌓인 챔버 온도/압력 PV·SV를 표와 그래프로 보여 준다.
    /// 원본과 다른 점:
    /// - 모달 창이 아니라 메인 화면의 '추이' 메뉴 화면. 운전 화면으로 돌아가도 수집은 계속된다 (원본도 창을 닫아도 계속 수집).
    /// - 원본 창은 샘플 공급(ProvideSamples)이 연결되지 않아 늘 비어 있었다. 여기서는 엔진 샘플을 바로 쓴다.
    /// - 원본은 'ALL'일 때 세 챔버 값을 한 선에 이어 그렸다. 여기서는 챔버마다 색이 다른 선(PV 실선, SV 점선)으로 그린다.
    /// - 챔버를 여러 개 골라 볼 수 있고, 시간 범위(1분/5분/15분/전체)를 고를 수 있다.
    /// - 그래프에 마우스를 올리면 그 시각의 값을 보여 준다 (TrendChart).
    /// - 표는 최신 샘플이 위로 오고, 수집 상태와 샘플 수를 보여 준다. '지우기'로 샘플을 비울 수 있다.
    /// </summary>
    public sealed class TrendViewModel : ObservableObject
    {
        public const int TableRowLimit = 200;
        public const int ChartSampleLimit = 400;

        private readonly ProcessEngine _engine;
        private TrendWindowOption _window;
        private IReadOnlyList<TrendSeries> _tempSeries = Array.Empty<TrendSeries>();
        private IReadOnlyList<TrendSeries> _pressSeries = Array.Empty<TrendSeries>();
        private string _statusText;
        private bool _hasSamples;

        public TrendViewModel(ProcessEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Units = new[]
            {
                new TrendUnitFilter("PMA", UnitLabel("PMA"), Color.FromRgb(0x2F, 0x6F, 0xD6), Refresh),
                new TrendUnitFilter("PMB", UnitLabel("PMB"), Color.FromRgb(0xD9, 0x82, 0x2B), Refresh),
                new TrendUnitFilter("PMC", UnitLabel("PMC"), Color.FromRgb(0x2E, 0x9E, 0x6A), Refresh),
            };
            Windows = new[]
            {
                new TrendWindowOption("최근 1분", TimeSpan.FromMinutes(1)),
                new TrendWindowOption("최근 5분", TimeSpan.FromMinutes(5)),
                new TrendWindowOption("최근 15분", TimeSpan.FromMinutes(15)),
                new TrendWindowOption("전체", null),
            };
            _window = Windows[1];

            StartCommand = new RelayCommand(() => { _engine.StartTrendCapture(); Refresh(); }, () => !_engine.IsTrendCapturing);
            StopCommand = new RelayCommand(() => { _engine.StopTrendCapture(); Refresh(); }, () => _engine.IsTrendCapturing);
            ClearCommand = new RelayCommand(() => { _engine.ClearTrendSamples(); Refresh(); }, () => _engine.TrendSampleCount > 0);
            Refresh();
        }

        public IReadOnlyList<TrendUnitFilter> Units { get; }
        public IReadOnlyList<TrendWindowOption> Windows { get; }

        public TrendWindowOption SelectedWindow
        {
            get => _window;
            set
            {
                if (SetProperty(ref _window, value ?? Windows[Windows.Count - 1])) Refresh();
            }
        }

        public ObservableCollection<TrendRow> Rows { get; } = new ObservableCollection<TrendRow>();

        public IReadOnlyList<TrendSeries> TempSeries
        {
            get => _tempSeries;
            private set => SetProperty(ref _tempSeries, value);
        }

        public IReadOnlyList<TrendSeries> PressSeries
        {
            get => _pressSeries;
            private set => SetProperty(ref _pressSeries, value);
        }

        public bool IsCapturing => _engine.IsTrendCapturing;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool HasSamples
        {
            get => _hasSamples;
            private set => SetProperty(ref _hasSamples, value);
        }

        public string EmptyText => IsCapturing
            ? "아직 샘플이 없습니다. 시뮬레이션이 돌아가는 동안 1초마다 쌓입니다."
            : "수집이 멈춰 있습니다. '수집 시작'을 누르세요.";

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearCommand { get; }

        /// <summary>원본 메뉴 진입 때 StartTrendCapture</summary>
        public void OnShown()
        {
            _engine.StartTrendCapture();
            Refresh();
        }

        internal static string UnitLabel(string unit) => SemiconductorUi.Helpers.EquipmentRegionHelper.FormatUnitLabel(unit);

        /// <summary>엔진 샘플을 다시 읽어 표와 그래프를 갱신 (원본 RefreshSamples + PlotSamples)</summary>
        public void Refresh()
        {
            var all = _engine.GetTrendSamples();
            var chosen = new HashSet<string>(Units.Where(u => u.IsChecked).Select(u => u.Key));
            var filtered = all.Where(s => chosen.Contains(s.Unit)).ToList();

            if (_window?.Span is TimeSpan span && filtered.Count > 0)
            {
                var from = filtered[filtered.Count - 1].Time - span;
                filtered = filtered.Where(s => s.Time >= from).ToList();
            }

            // 표: 최신이 위로, 최근 200개
            Rows.Clear();
            for (int i = filtered.Count - 1, n = 0; i >= 0 && n < TableRowLimit; i--, n++)
            {
                Rows.Add(new TrendRow(filtered[i]));
            }

            // 그래프: 챔버마다 최근 400개
            var temp = new List<TrendSeries>();
            var press = new List<TrendSeries>();
            foreach (var u in Units.Where(u => u.IsChecked))
            {
                var mine = filtered.Where(s => s.Unit == u.Key).ToList();
                if (mine.Count > ChartSampleLimit) mine = mine.GetRange(mine.Count - ChartSampleLimit, ChartSampleLimit);
                if (mine.Count == 0) continue;
                var sv = Color.FromArgb(0xB0, u.Color.R, u.Color.G, u.Color.B);
                temp.Add(new TrendSeries($"{u.Label} PV", u.Color, false, mine.Select(s => (s.Time, s.PvTemp)).ToList()));
                temp.Add(new TrendSeries($"{u.Label} SV", sv, true, mine.Select(s => (s.Time, s.SvTemp)).ToList()));
                press.Add(new TrendSeries($"{u.Label} PV", u.Color, false, mine.Select(s => (s.Time, s.PvPress)).ToList()));
                press.Add(new TrendSeries($"{u.Label} SV", sv, true, mine.Select(s => (s.Time, s.SvPress)).ToList()));
            }
            TempSeries = temp;
            PressSeries = press;

            HasSamples = filtered.Count > 0;
            StatusText = (IsCapturing ? "수집 중" : "수집 멈춤") + $" · 샘플 {all.Count:N0}개";
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(EmptyText));
            RelayCommand.Refresh();
        }
    }
}
