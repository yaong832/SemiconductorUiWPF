using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>
    /// 시간축 꺾은선 그래프 (원본 TrendForm의 MS Chart 대신 직접 그림).
    /// 선마다 색과 실선/점선을 정하고, 마우스를 올리면 가장 가까운 시각의 값을 보여 준다.
    /// </summary>
    public sealed class TrendChart : FrameworkElement
    {
        public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
            nameof(Series), typeof(IReadOnlyList<TrendSeries>), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(TrendChart),
            new FrameworkPropertyMetadata("0.0", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
            nameof(EmptyText), typeof(string), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<TrendSeries> Series
        {
            get => (IReadOnlyList<TrendSeries>)GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>축 눈금과 마우스 표시 값의 숫자 형식</summary>
        public string ValueFormat
        {
            get => (string)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        public string EmptyText
        {
            get => (string)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
        }

        private const double PadLeft = 62, PadRight = 14, PadTop = 34, PadBottom = 24;

        private static readonly Brush Back = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush TextPrimary = Frozen(Color.FromRgb(0x21, 0x29, 0x36));
        private static readonly Brush TextSecondary = Frozen(Color.FromRgb(0x64, 0x70, 0x80));
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0xE4, 0xE8, 0xEE), 1);
        private static readonly Pen AxisPen = FrozenPen(Color.FromRgb(0xD0, 0xD7, 0xE1), 1);
        private static readonly Pen CursorPen = FrozenPen(Color.FromRgb(0x96, 0xA0, 0xAE), 1);
        private static readonly Brush TipBack = Frozen(Color.FromArgb(0xF2, 0x21, 0x29, 0x36));
        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Typeface FaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private Point? _mouse;

        public TrendChart()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.GetPosition(this);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            if (!string.IsNullOrEmpty(Title)) dc.DrawText(Text(Title, 13, TextPrimary, dpi, bold: true), new Point(12, 8));

            var series = (Series ?? Array.Empty<TrendSeries>()).Where(s => s.Points != null && s.Points.Count > 0).ToList();
            var plot = new Rect(PadLeft, PadTop, Math.Max(10, w - PadLeft - PadRight), Math.Max(10, h - PadTop - PadBottom));

            DrawLegend(dc, series, w, dpi);

            if (series.Count == 0)
            {
                dc.DrawRectangle(null, AxisPen, plot);
                var empty = Text(EmptyText ?? "샘플이 없습니다.", 12, TextSecondary, dpi);
                dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
                return;
            }

            // 범위
            DateTime t0 = series.Min(s => s.Points[0].t), t1 = series.Max(s => s.Points[s.Points.Count - 1].t);
            if (t1 <= t0) t1 = t0.AddSeconds(1);
            double vMin = series.Min(s => s.Points.Min(p => p.v)), vMax = series.Max(s => s.Points.Max(p => p.v));
            NiceRange(ref vMin, ref vMax, out double step);

            double X(DateTime t) => plot.Left + (t - t0).TotalMilliseconds / (t1 - t0).TotalMilliseconds * plot.Width;
            double Y(double v) => plot.Bottom - (v - vMin) / (vMax - vMin) * plot.Height;

            // 눈금: 값
            string fmt = ValueFormat ?? "0.0";
            for (double v = vMin; v <= vMax + step / 2; v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var label = Text(v.ToString(StepFormat(step, fmt), CultureInfo.CurrentCulture), 11, TextSecondary, dpi);
                dc.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
            }

            // 눈금: 시간 (5개 안팎)
            var span = t1 - t0;
            var tStep = NiceTimeStep(span, Math.Max(2, (int)(plot.Width / 110)));
            var first = new DateTime((t0.Ticks + tStep.Ticks - 1) / tStep.Ticks * tStep.Ticks);
            for (var t = first; t <= t1; t += tStep)
            {
                double x = Math.Round(X(t)) + 0.5;
                dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = Text(t.ToString("HH:mm:ss"), 11, TextSecondary, dpi);
                dc.DrawText(label, new Point(Math.Min(plot.Right - label.Width, Math.Max(plot.Left, x - label.Width / 2)), plot.Bottom + 4));
            }
            dc.DrawRectangle(null, AxisPen, plot);

            // 선
            dc.PushClip(new RectangleGeometry(plot));
            foreach (var s in series)
            {
                var pen = new Pen(Frozen(s.Color), s.Dashed ? 1.5 : 2) { LineJoin = PenLineJoin.Round };
                if (s.Dashed) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(X(s.Points[0].t), Y(s.Points[0].v)), false, false);
                    if (s.Points.Count == 1)
                    {
                        g.LineTo(new Point(X(s.Points[0].t) + 1, Y(s.Points[0].v)), true, false);
                    }
                    for (int i = 1; i < s.Points.Count; i++) g.LineTo(new Point(X(s.Points[i].t), Y(s.Points[i].v)), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
            dc.Pop();

            // 마우스 위치의 값
            if (_mouse is Point m && plot.Contains(m))
            {
                var at = t0 + TimeSpan.FromMilliseconds((m.X - plot.Left) / plot.Width * span.TotalMilliseconds);
                var nearest = series.SelectMany(s => s.Points).OrderBy(p => Math.Abs((p.t - at).Ticks)).First().t;
                double cx = Math.Round(X(nearest)) + 0.5;
                dc.DrawLine(CursorPen, new Point(cx, plot.Top), new Point(cx, plot.Bottom));

                var lines = new List<(FormattedText text, Color color)>
                {
                    (Text(nearest.ToString("HH:mm:ss"), 11.5, Brushes.White, dpi, bold: true), Colors.Transparent),
                };
                foreach (var s in series)
                {
                    var hit = s.Points.Where(p => p.t == nearest).Select(p => (double?)p.v).FirstOrDefault();
                    if (hit == null) continue;
                    dc.DrawEllipse(Frozen(s.Color), null, new Point(cx, Y(hit.Value)), 3, 3);
                    lines.Add((Text($"{s.Label}  {hit.Value.ToString(fmt, CultureInfo.CurrentCulture)}", 11.5, Brushes.White, dpi), s.Color));
                }
                double tipW = lines.Max(l => l.text.Width) + 30, tipH = lines.Sum(l => l.text.Height) + 12;
                double tx = cx + 12 + tipW > plot.Right ? cx - 12 - tipW : cx + 12;
                double ty = Math.Max(plot.Top + 4, Math.Min(plot.Bottom - tipH - 4, m.Y - tipH / 2));
                dc.DrawRoundedRectangle(TipBack, null, new Rect(tx, ty, tipW, tipH), 4, 4);
                double ly = ty + 6;
                foreach (var (text, color) in lines)
                {
                    if (color != Colors.Transparent) dc.DrawRectangle(Frozen(color), null, new Rect(tx + 8, ly + text.Height / 2 - 1.5, 12, 3));
                    dc.DrawText(text, new Point(tx + (color == Colors.Transparent ? 8 : 24), ly));
                    ly += text.Height;
                }
            }
        }

        private static void DrawLegend(DrawingContext dc, List<TrendSeries> series, double width, double dpi)
        {
            double x = width - PadRight;
            for (int i = series.Count - 1; i >= 0; i--)
            {
                var s = series[i];
                var label = Text(s.Label, 11, TextSecondary, dpi);
                x -= label.Width;
                dc.DrawText(label, new Point(x, 10));
                x -= 22;
                var pen = new Pen(Frozen(s.Color), 2);
                if (s.Dashed) pen.DashStyle = new DashStyle(new double[] { 2, 1.5 }, 0);
                dc.DrawLine(pen, new Point(x, 10 + label.Height / 2), new Point(x + 16, 10 + label.Height / 2));
                x -= 12;
            }
        }

        /// <summary>값 범위를 보기 좋은 눈금(1, 2, 5 × 10ⁿ)에 맞춘다.</summary>
        internal static void NiceRange(ref double min, ref double max, out double step)
        {
            if (double.IsNaN(min) || double.IsNaN(max)) { min = 0; max = 1; }
            if (max - min < 1e-9)
            {
                double pad = Math.Abs(min) > 1e-9 ? Math.Abs(min) * 0.05 : 1;
                min -= pad;
                max += pad;
            }
            double raw = (max - min) / 4;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
            if (max - min < step) max = min + step;
        }

        private static string StepFormat(double step, string fallback)
        {
            if (step >= 1) return "0";
            int decimals = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            return "0." + new string('0', Math.Min(6, decimals));
        }

        private static readonly TimeSpan[] TimeSteps =
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
        };

        private static TimeSpan NiceTimeStep(TimeSpan span, int maxLabels)
        {
            foreach (var s in TimeSteps)
            {
                if (span.Ticks / s.Ticks <= maxLabels) return s;
            }
            return TimeSteps[TimeSteps.Length - 1];
        }

        private static FormattedText Text(string s, double size, Brush brush, double dpi, bool bold = false) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, brush, dpi);

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Pen FrozenPen(Color c, double thickness)
        {
            var p = new Pen(Frozen(c), thickness);
            p.Freeze();
            return p;
        }
    }
}
