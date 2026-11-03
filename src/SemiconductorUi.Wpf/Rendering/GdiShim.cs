using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SemiconductorUi.Wpf.Rendering
{
    /// <summary>
    /// WinForms GDI+ 그리기 코드를 WPF DrawingContext로 거의 한 줄씩 옮기기 위한 얇은 호환 계층.
    /// 원본 Controls/*.cs의 OnPaint 로직을 그대로 가져와 모양을 똑같이 유지하는 것이 목적이다.
    /// 각도는 GDI+와 같이 도(degree), 화면 좌표(y 아래) 기준 시계 방향이다.
    /// </summary>
    public sealed class Gdi
    {
        private readonly DrawingContext _dc;
        private readonly double _pixelsPerDip;
        private readonly Stack<int> _saves = new Stack<int>();
        private int _pushed;

        public Gdi(DrawingContext dc, Visual visual)
        {
            _dc = dc;
            _pixelsPerDip = visual != null ? VisualTreeHelper.GetDpi(visual).PixelsPerDip : 1.0;
        }

        #region 상태(변환)

        public int Save()
        {
            _saves.Push(_pushed);
            return _pushed;
        }

        public void Restore(int state)
        {
            while (_pushed > state)
            {
                _dc.Pop();
                _pushed--;
            }
            if (_saves.Count > 0) _saves.Pop();
        }

        /// <summary>남은 Push를 모두 해제 (OnRender 끝에서 호출)</summary>
        public void End()
        {
            while (_pushed > 0)
            {
                _dc.Pop();
                _pushed--;
            }
        }

        public void TranslateTransform(double x, double y) { _dc.PushTransform(new TranslateTransform(x, y)); _pushed++; }
        public void RotateTransform(double degrees) { _dc.PushTransform(new RotateTransform(degrees)); _pushed++; }
        public void ScaleTransform(double sx, double sy) { _dc.PushTransform(new ScaleTransform(sx, sy)); _pushed++; }

        #endregion

        #region 도형

        public void Clear(Color color, Size size) => _dc.DrawRectangle(Brush(color), null, new Rect(size));

        public void FillPath(Brush brush, Geometry path) => _dc.DrawGeometry(brush, null, path);
        public void DrawPath(Pen pen, Geometry path) => _dc.DrawGeometry(null, pen, path);

        public void FillEllipse(Brush brush, Rect r) => _dc.DrawEllipse(brush, null, Center(r), r.Width / 2, r.Height / 2);
        public void FillEllipse(Brush brush, double x, double y, double w, double h) => FillEllipse(brush, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
        public void DrawEllipse(Pen pen, Rect r) => _dc.DrawEllipse(null, pen, Center(r), r.Width / 2, r.Height / 2);
        public void DrawEllipse(Pen pen, double x, double y, double w, double h) => DrawEllipse(pen, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));

        public void FillRectangle(Brush brush, Rect r) => _dc.DrawRectangle(brush, null, r);
        public void FillRectangle(Brush brush, double x, double y, double w, double h) => FillRectangle(brush, R(x, y, w, h));
        public void DrawRectangle(Pen pen, double x, double y, double w, double h) => _dc.DrawRectangle(null, pen, R(x, y, w, h));

        public void DrawLine(Pen pen, double x1, double y1, double x2, double y2) => _dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

        public void DrawArc(Pen pen, Rect r, double startDeg, double sweepDeg)
        {
            var p = new GPath();
            p.AddArc(r, startDeg, sweepDeg);
            _dc.DrawGeometry(null, pen, p.ToGeometry(false));
        }

        public void DrawBezier(Pen pen, Point p1, Point p2, Point p3, Point p4)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(p1, false, false);
                ctx.BezierTo(p2, p3, p4, true, true);
            }
            g.Freeze();
            _dc.DrawGeometry(null, pen, g);
        }

        #endregion

        #region 글자 (원본 CanvasText 대응)

        /// <summary>
        /// 사각형 안에 글자를 그린다. sizePt는 GDI 폰트 크기(포인트)다.
        /// </summary>
        public void DrawText(string text, double sizePt, bool bold, Rect bounds, Color color,
            TextAlignment h = TextAlignment.Left, VAlign v = VAlign.Center, bool ellipsis = false, bool wrap = false)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;
            var ft = Format(text, sizePt, bold, color);
            ft.MaxTextWidth = bounds.Width;
            ft.TextAlignment = h;
            if (ellipsis) { ft.Trimming = TextTrimming.CharacterEllipsis; ft.MaxLineCount = 1; }
            else if (!wrap) { ft.MaxLineCount = 1; }
            double y = bounds.Y;
            if (v == VAlign.Center) y += (bounds.Height - ft.Height) / 2;
            else if (v == VAlign.Bottom) y += bounds.Height - ft.Height;
            _dc.DrawText(ft, new Point(bounds.X, y));
        }

        public Size MeasureText(string text, double sizePt, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return new Size(0, 0);
            var ft = Format(text, sizePt, bold, Colors.Black);
            return new Size(Math.Ceiling(ft.WidthIncludingTrailingWhitespace), Math.Ceiling(ft.Height));
        }

        private FormattedText Format(string text, double sizePt, bool bold, Color color)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                bold ? BoldFace : RegularFace, sizePt * 96.0 / 72.0, Brush(color), _pixelsPerDip);
        }

        private static readonly FontFamily UiFamily = new FontFamily("Segoe UI, Malgun Gothic");
        private static readonly Typeface RegularFace = new Typeface(UiFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface BoldFace = new Typeface(UiFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        #endregion

        #region 브러시/펜/색 팩토리

        public static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
        public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        public static Color WithAlpha(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>WinForms ControlPaint.Dark(color, percOfDarkDark) 근사</summary>
        public static Color Dark(Color c, double perc)
        {
            double f = 1.0 - Math.Max(0, Math.Min(1, 0.5 + perc * 0.5)) * 0.5;
            return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        public static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Pen Pen(Color c, double width = 1.0, bool dashed = false, bool roundCaps = false, bool roundJoin = false)
        {
            var p = new Pen(Brush(c), width);
            if (dashed) p.DashStyle = new DashStyle(new double[] { 5, 2 }, 0);
            if (roundCaps) { p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; }
            if (roundJoin) p.LineJoin = PenLineJoin.Round;
            p.Freeze();
            return p;
        }

        public enum GradientMode { Horizontal, Vertical, ForwardDiagonal, BackwardDiagonal }

        /// <summary>GDI+ LinearGradientBrush(rect, c1, c2, mode)</summary>
        public static Brush Linear(Rect r, Color c1, Color c2, GradientMode mode, double? triangularFocus = null)
        {
            Point s, e;
            switch (mode)
            {
                case GradientMode.Horizontal: s = new Point(r.Left, r.Top); e = new Point(r.Right, r.Top); break;
                case GradientMode.Vertical: s = new Point(r.Left, r.Top); e = new Point(r.Left, r.Bottom); break;
                case GradientMode.BackwardDiagonal: s = new Point(r.Right, r.Top); e = new Point(r.Left, r.Bottom); break;
                default: s = new Point(r.Left, r.Top); e = new Point(r.Right, r.Bottom); break;
            }
            var stops = new GradientStopCollection();
            if (triangularFocus.HasValue)
            {
                // SetBlendTriangularShape(focus): 시작색 → focus에서 끝색 → 다시 시작색
                stops.Add(new GradientStop(c1, 0));
                stops.Add(new GradientStop(c2, triangularFocus.Value));
                stops.Add(new GradientStop(c1, 1));
            }
            else
            {
                stops.Add(new GradientStop(c1, 0));
                stops.Add(new GradientStop(c2, 1));
            }
            var brush = new LinearGradientBrush(stops) { MappingMode = BrushMappingMode.Absolute, StartPoint = s, EndPoint = e };
            brush.Freeze();
            return brush;
        }

        /// <summary>GDI+ PathGradientBrush(타원 경로, CenterColor, SurroundColors, CenterPoint)</summary>
        public static Brush Radial(Rect ellipse, Color center, Color surround, Point centerPoint)
        {
            var brush = new RadialGradientBrush(center, surround)
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = Center(ellipse),
                RadiusX = ellipse.Width / 2,
                RadiusY = ellipse.Height / 2,
                GradientOrigin = centerPoint
            };
            brush.Freeze();
            return brush;
        }

        #endregion

        #region 사각형 유틸

        public static Rect R(double x, double y, double w, double h) => new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
        public static Rect LTRB(double l, double t, double r, double b) => R(l, t, r - l, b - t);
        public static Rect Inflate(Rect r, double dx, double dy) => R(r.X - dx, r.Y - dy, r.Width + dx * 2, r.Height + dy * 2);
        public static Point Center(Rect r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);

        /// <summary>원본 EquipmentCanvasStyler.CreateRoundedRectangle과 같은 모양</summary>
        public static Geometry RoundedRect(Rect rect, double radius)
        {
            double d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            var p = new GPath();
            p.AddArc(R(rect.X, rect.Y, d, d), 180, 90);
            p.AddArc(R(rect.Right - d, rect.Y, d, d), 270, 90);
            p.AddArc(R(rect.Right - d, rect.Bottom - d, d, d), 0, 90);
            p.AddArc(R(rect.X, rect.Bottom - d, d, d), 90, 90);
            p.CloseFigure();
            return p.ToGeometry();
        }

        #endregion
    }

    public enum VAlign { Top, Center, Bottom }

    /// <summary>GDI+ GraphicsPath의 AddLine/AddArc/CloseFigure 동작을 흉내 낸 경로 빌더</summary>
    public sealed class GPath
    {
        private readonly List<List<Action<StreamGeometryContext>>> _figures = new List<List<Action<StreamGeometryContext>>>();
        private readonly List<Point> _starts = new List<Point>();
        private readonly List<bool> _closed = new List<bool>();
        private bool _open;
        private Point _current;

        private void ConnectTo(Point p)
        {
            if (!_open)
            {
                _figures.Add(new List<Action<StreamGeometryContext>>());
                _starts.Add(p);
                _closed.Add(false);
                _open = true;
            }
            else if ((p - _current).Length > 1e-6)
            {
                _figures[_figures.Count - 1].Add(c => c.LineTo(p, true, true));
            }
            _current = p;
        }

        public void AddLine(double x1, double y1, double x2, double y2)
        {
            ConnectTo(new Point(x1, y1));
            var end = new Point(x2, y2);
            _figures[_figures.Count - 1].Add(c => c.LineTo(end, true, true));
            _current = end;
        }

        public void AddArc(double x, double y, double w, double h, double startDeg, double sweepDeg)
            => AddArc(Gdi.R(x, y, w, h), startDeg, sweepDeg);

        public void AddArc(Rect r, double startDeg, double sweepDeg)
        {
            double rx = r.Width / 2, ry = r.Height / 2;
            var center = Gdi.Center(r);
            Point At(double deg)
            {
                double a = deg * Math.PI / 180.0;
                return new Point(center.X + rx * Math.Cos(a), center.Y + ry * Math.Sin(a));
            }

            ConnectTo(At(startDeg));
            // 큰 호는 반으로 나눠 ArcSegment의 모호함을 피한다.
            int pieces = Math.Abs(sweepDeg) > 180 ? 2 : 1;
            double step = sweepDeg / pieces;
            var fig = _figures[_figures.Count - 1];
            for (int i = 1; i <= pieces; i++)
            {
                var end = At(startDeg + step * i);
                var size = new Size(rx, ry);
                var dir = step >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                fig.Add(c => c.ArcTo(end, size, 0, false, dir, true, true));
                _current = end;
            }
        }

        public void AddEllipse(Rect r)
        {
            _open = false;
            AddArc(r, 0, 360);
            CloseFigure();
        }

        public void CloseFigure()
        {
            if (_open) _closed[_closed.Count - 1] = true;
            _open = false;
        }

        public Geometry ToGeometry(bool filled = true)
        {
            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = g.Open())
            {
                for (int i = 0; i < _figures.Count; i++)
                {
                    ctx.BeginFigure(_starts[i], filled, _closed[i]);
                    foreach (var op in _figures[i]) op(ctx);
                }
            }
            g.Freeze();
            return g;
        }
    }
}
