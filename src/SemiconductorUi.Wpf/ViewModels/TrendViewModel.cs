using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>추이 그래프의 선 하나. 값이 NaN인 점에서 선이 끊긴다.</summary>
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

    /// <summary>그래프 한 칸 (원본 TrendForm의 ChartArea + Title)</summary>
    public sealed class TrendPanel : ObservableObject
    {
        private string _title;
        private IReadOnlyList<TrendSeries> _series = Array.Empty<TrendSeries>();
        private DateTime? _timeFrom;
        private DateTime? _timeTo;
        private string _emptyText;

        public TrendPanel(string unit, bool isTemperature)
        {
            Unit = unit;
            IsTemperature = isTemperature;
            _title = TrendViewModel.TitleText(unit, isTemperature, double.NaN, double.NaN);
        }

        public string Unit { get; }
        public bool IsTemperature { get; }

        /// <summary>원본 labelFormat: 온도 "0.0", 압력 "G4"</summary>
        public string ValueFormat => IsTemperature ? "0.0" : "0.####";

        /// <summary>"Chamber A 온도 (°C)   PV 23.0 / SV 23.0" (원본 TitleText)</summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public IReadOnlyList<TrendSeries> Series
        {
            get => _series;
            set => SetProperty(ref _series, value);
        }

        /// <summary>시간축 (원본 AxisX.Minimum/Maximum = 구간 시작 ~ 지금)</summary>
        public DateTime? TimeFrom
        {
            get => _timeFrom;
            set => SetProperty(ref _timeFrom, value);
        }

        public DateTime? TimeTo
        {
            get => _timeTo;
            set => SetProperty(ref _timeTo, value);
        }

        /// <summary>그릴 값이 없을 때 안내</summary>
        public string EmptyText
        {
            get => _emptyText;
            set => SetProperty(ref _emptyText, value);
        }
    }

    /// <summary>한 줄 = 챔버 하나 (원본 3행 중 한 행: 온도, 압력)</summary>
    public sealed class TrendChamberRow
    {
        public TrendChamberRow(string unit)
        {
            Unit = unit;
            Temperature = new TrendPanel(unit, true);
            Pressure = new TrendPanel(unit, false);
        }

        public string Unit { get; }
        public TrendPanel Temperature { get; }
        public TrendPanel Pressure { get; }
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
    /// 추이 화면 (원본 Forms.TrendForm, 4777c6c 기준).
    /// Chamber A/B/C × 온도·압력 6칸 그래프. PV 실선(파랑) / SV 점선(주황), 칸 제목에 마지막 값.
    /// 엔진이 공정 타이머가 도는 동안 1초마다 기록하고, 이 화면은 보이는 동안 1초마다 다시 그린다.
    /// PV는 그 챔버가 공정 중일 때만, SV는 레시피 적용 시에만 있어서 없으면 선이 끊긴다.
    /// 원본과 다른 점:
    /// - 모달 창이 아니라 메인 화면의 '추이' 메뉴 화면.
    /// - 그래프에 마우스를 올리면 그 시각의 PV/SV를 보여 준다 (TrendChart).
    /// - '지우기'로 기록을 비울 수 있다.
    /// </summary>
    public sealed class TrendViewModel : ObservableObject
    {
        /// <summary>그래프 선 하나에 그리는 최대 점 수. 넘으면 고르게 솎아 낸다 (2시간 = 7200점).</summary>
        public const int ChartPointLimit = 1500;

        public static readonly Color PvColor = Color.FromRgb(45, 98, 196);
        public static readonly Color SvColor = Color.FromRgb(214, 138, 20);

        private readonly ProcessEngine _engine;
        private TrendWindowOption _window;
        private string _statusText;
        private DispatcherTimer _timer;

        public TrendViewModel(ProcessEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Chambers = ProcessEngine.ChamberUnitKeys.Select(u => new TrendChamberRow(u)).ToArray();
            Windows = new[]
            {
                new TrendWindowOption("최근 5분", TimeSpan.FromMinutes(5)),
                new TrendWindowOption("최근 10분", TimeSpan.FromMinutes(10)),
                new TrendWindowOption("최근 30분", TimeSpan.FromMinutes(30)),
                new TrendWindowOption("최근 1시간", TimeSpan.FromHours(1)),
                new TrendWindowOption("전체", null),
            };
            _window = Windows[1];

            ClearCommand = new RelayCommand(() => { _engine.ClearTrendSamples(); Refresh(); }, () => _engine.TrendSampleCount > 0);
            Refresh();
        }

        /// <summary>Chamber A/B/C (원본 Units)</summary>
        public IReadOnlyList<TrendChamberRow> Chambers { get; }

        public IReadOnlyList<TrendWindowOption> Windows { get; }

        public TrendWindowOption SelectedWindow
        {
            get => _window;
            set
            {
                if (SetProperty(ref _window, value ?? Windows[Windows.Count - 1])) Refresh();
            }
        }

        public Brush PvBrush { get; } = Frozen(PvColor);
        public Brush SvBrush { get; } = Frozen(SvColor);

        /// <summary>원본 labelStatus</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public ICommand ClearCommand { get; }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        /// <summary>화면에 들어올 때: 바로 그리고, 떠날 때까지 1초마다 갱신 (원본 OnLoad + refreshTimer)</summary>
        public void OnShown()
        {
            Refresh();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, e) => Refresh();
            }
            _timer.Start();
        }

        public void OnHidden()
        {
            _timer?.Stop();
        }

        internal static string UnitLabel(string unit) => SemiconductorUi.Helpers.EquipmentRegionHelper.FormatUnitLabel(unit);

        /// <summary>원본 TitleText: "Chamber A 온도 (°C)   PV 23.0 / SV 23.0" (값이 없으면 "-")</summary>
        public static string TitleText(string unit, bool isTemperature, double pv, double sv)
        {
            string what = isTemperature ? "온도 (°C)" : "압력 (Torr)";
            return $"{UnitLabel(unit)} {what}   PV {FormatValue(pv, isTemperature)} / SV {FormatValue(sv, isTemperature)}";
        }

        /// <summary>원본 FormatPressure: 1 Torr 미만은 소수 4자리까지</summary>
        public static string FormatValue(double v, bool isTemperature)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            if (isTemperature) return v.ToString("0.0", CultureInfo.InvariantCulture);
            return v < 1.0 ? v.ToString("0.####", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>기록된 샘플로 6칸 그래프를 다시 그린다 (원본 RefreshChart)</summary>
        public void Refresh()
        {
            var samples = _engine.GetTrendSamples();
            // 스냅샷은 TrendClock을 1초씩 넘기므로 마지막 샘플보다 앞서지 않게 한다
            DateTime now = _engine.TrendClock();
            if (samples.Count > 0 && samples[samples.Count - 1].Time > now) now = samples[samples.Count - 1].Time;
            TimeSpan? range = _window?.Span;
            DateTime from = range.HasValue ? now - range.Value : (samples.Count > 0 ? samples[0].Time : now.AddMinutes(-10));
            if (from >= now) from = now.AddMinutes(-1);
            string emptyText = samples.Count == 0 ? "기록 없음" : "이 구간에는 값이 없습니다";

            foreach (var row in Chambers)
            {
                var mine = samples.Where(s => s.Unit == row.Unit && s.Time >= from).ToList();
                mine = Thin(mine, ChartPointLimit);
                TrendSample? last = mine.Count > 0 ? mine[mine.Count - 1] : (TrendSample?)null;

                row.Temperature.Series = new[]
                {
                    new TrendSeries("PV", PvColor, false, mine.Select(s => (s.Time, s.PvTemp)).ToList()),
                    new TrendSeries("SV", SvColor, true, mine.Select(s => (s.Time, s.SvTemp)).ToList()),
                };
                row.Pressure.Series = new[]
                {
                    new TrendSeries("PV", PvColor, false, mine.Select(s => (s.Time, s.PvPress)).ToList()),
                    new TrendSeries("SV", SvColor, true, mine.Select(s => (s.Time, s.SvPress)).ToList()),
                };
                row.Temperature.Title = TitleText(row.Unit, true, last?.PvTemp ?? double.NaN, last?.SvTemp ?? double.NaN);
                row.Pressure.Title = TitleText(row.Unit, false, last?.PvPress ?? double.NaN, last?.SvPress ?? double.NaN);
                foreach (var panel in new[] { row.Temperature, row.Pressure })
                {
                    panel.TimeFrom = from;
                    panel.TimeTo = now;
                    panel.EmptyText = emptyText;
                }
            }

            if (samples.Count == 0)
            {
                StatusText = "아직 기록이 없습니다. 공정을 시작하면 1초마다 기록됩니다.";
            }
            else
            {
                StatusText = $"기록 {samples[0].Time:HH:mm:ss} ~ {samples[samples.Count - 1].Time:HH:mm:ss} (1초 간격) · PV는 공정 중인 챔버만, SV는 레시피 적용 시에만 표시";
            }
            OnPropertyChanged(nameof(IsRunning));
            RelayCommand.Refresh();
        }

        /// <summary>점이 너무 많으면 고르게 솎아 낸다. 마지막 점은 늘 남긴다.</summary>
        internal static List<TrendSample> Thin(List<TrendSample> list, int limit)
        {
            if (list.Count <= limit) return list;
            int step = (list.Count + limit - 1) / limit;
            var result = new List<TrendSample>(limit + 1);
            for (int i = 0; i < list.Count; i += step) result.Add(list[i]);
            if ((list.Count - 1) % step != 0) result.Add(list[list.Count - 1]);
            return result;
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
