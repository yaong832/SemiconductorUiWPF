using System;
using System.Windows;
using System.Windows.Media;
using SemiconductorUi.Wpf.Rendering;
using static SemiconductorUi.Wpf.Rendering.Gdi;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>원본 Controls/FoupVisualizationControl: 위에서 본 로드포트 + FOUP</summary>
    public sealed class FoupView : FrameworkElement
    {
        private const double PortHalf = 32;
        private static readonly Color PortLight = Rgb(160, 170, 184);
        private static readonly Color PortDark = Rgb(124, 136, 152);
        private static readonly Color PortEdge = Rgb(104, 115, 130);
        private static readonly Color PinFill = Rgb(211, 217, 225);
        private static readonly Color ShellFill = Argb(108, 78, 88, 104);
        private static readonly Color ShellEdge = Rgb(60, 69, 84);
        private static readonly Color ShellDark = Rgb(60, 69, 84);
        private static readonly Color FlangeFill = Rgb(90, 100, 116);
        private static readonly Color FlangeTop = Rgb(124, 135, 152);
        private static readonly Color SiliconFill = Rgb(206, 213, 225);
        private static readonly Color SiliconRim = Rgb(226, 181, 60);
        private static readonly Color PlateFill = Rgb(42, 50, 64);
        private static readonly Color PlateText = Rgb(233, 237, 243);
        private static readonly Color AlertText = Rgb(196, 72, 60);

        public static readonly DependencyProperty TitleProperty = Reg(nameof(Title), typeof(string), "FOUP");
        public static readonly DependencyProperty WaferCountProperty = Reg(nameof(WaferCount), typeof(int), 0);
        public static readonly DependencyProperty CapacityProperty = Reg(nameof(Capacity), typeof(int), 25);
        public static readonly DependencyProperty MountedProperty = Reg(nameof(Mounted), typeof(bool), true);
        public static readonly DependencyProperty FacingDegreesProperty = Reg(nameof(FacingDegrees), typeof(double), -45.0);

        private static DependencyProperty Reg(string name, Type type, object def) =>
            DependencyProperty.Register(name, type, typeof(FoupView), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public int WaferCount { get => (int)GetValue(WaferCountProperty); set => SetValue(WaferCountProperty, value); }
        public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
        public bool Mounted { get => (bool)GetValue(MountedProperty); set => SetValue(MountedProperty, value); }
        public double FacingDegrees { get => (double)GetValue(FacingDegreesProperty); set => SetValue(FacingDegreesProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            if (size.Width <= 40 || size.Height <= 40) return;
            var g = new Gdi(dc, this);
            // 원본 FoupVisualizationControl.Backdrop: 배경을 칠하지 않아 FOUP 쪽으로 나온 TM 블레이드가
            // 로드포트 판 아래로 들어가 보인다. 투명으로 칠해 빈 곳도 클릭은 받는다.
            g.Clear(Colors.Transparent, size);

            var state = g.Save();
            g.TranslateTransform(size.Width / 2, size.Height / 2);
            g.RotateTransform(FacingDegrees);
            DrawLoadPort(g);
            if (Mounted) DrawPod(g); else DrawEmptyOutline(g);
            g.Restore(state);

            DrawLabels(g, size);
            g.End();
        }

        private static void DrawLoadPort(Gdi g)
        {
            var plate = new Rect(-PortHalf, -PortHalf, PortHalf * 2, PortHalf * 2);
            g.FillPath(Brush(Argb(40, 28, 36, 51)), RoundedRect(new Rect(plate.X + 1.5, plate.Y + 2.5, plate.Width, plate.Height), 7));
            var path = RoundedRect(plate, 7);
            g.FillPath(Linear(plate, PortLight, PortDark, GradientMode.ForwardDiagonal), path);
            g.DrawPath(Pen(PortEdge, 1), path);

            var pin = Brush(PinFill);
            var pinEdge = Pen(Rgb(94, 106, 122), 0.8);
            foreach (var p in new[] { new Point(18, -19), new Point(18, 19), new Point(-21, 0) })
            {
                var r = new Rect(p.X - 2.4, p.Y - 2.4, 4.8, 4.8);
                g.FillEllipse(pin, r);
                g.DrawEllipse(pinEdge, r);
            }
        }

        private void DrawPod(Gdi g)
        {
            const double back = -26, front = 23, half = 26, round = 14;
            if (WaferCount > 0)
            {
                var wafer = new Rect(-22, -21, 42, 42);
                g.FillEllipse(Brush(SiliconFill), wafer);
                g.DrawEllipse(Pen(SiliconRim, 1.6), wafer);
            }

            // 통 몸체: 뒤쪽과 양옆만 테두리. TM 쪽(앞)은 열려 있다.
            var shell = new GPath();
            shell.AddLine(front, -half, back + round, -half);
            shell.AddArc(back, -half, round * 2, round * 2, 270, -90);
            shell.AddLine(back, -half + round, back, half - round);
            shell.AddArc(back, half - round * 2, round * 2, round * 2, 180, -90);
            shell.AddLine(back + round, half, front, half);
            g.FillPath(Brush(ShellFill), shell.ToGeometry());
            g.DrawPath(Pen(ShellEdge, 1.2), shell.ToGeometry(false));

            var dark = Brush(ShellDark);
            g.FillRectangle(dark, front - 3, -half - 1, 4, 4);
            g.FillRectangle(dark, front - 3, half - 3, 4, 4);
            g.FillRectangle(dark, -9, -half - 4, 20, 4);
            g.FillRectangle(dark, -9, half, 20, 4);

            var flange = new Rect(-9, -9, 18, 18);
            var fp = RoundedRect(flange, 4);
            g.FillPath(Brush(FlangeFill), fp);
            g.DrawPath(Pen(Rgb(46, 54, 68), 0.8), fp);
            g.FillPath(Brush(FlangeTop), RoundedRect(Inflate(flange, -4, -4), 2));
        }

        private static void DrawEmptyOutline(Gdi g)
        {
            g.DrawRectangle(Pen(Rgb(225, 230, 237), 1.2, dashed: true), -26, -26, 49, 52);
        }

        private void DrawLabels(Gdi g, Size size)
        {
            bool tmOnRight = Math.Cos(FacingDegrees * Math.PI / 180.0) >= 0;
            const double margin = 3;
            string title = Title ?? string.Empty;
            var textSize = g.MeasureText(title, 8.5, true);
            double plateWidth = textSize.Width + 14;
            const double plateHeight = 19;
            double x = tmOnRight ? margin : size.Width - margin - plateWidth;
            var plate = new Rect(x, margin, plateWidth, plateHeight);
            g.FillPath(Brush(PlateFill), RoundedRect(plate, 4));
            g.DrawText(title, 8.5, true, plate, PlateText, TextAlignment.Center);

            string count = Mounted ? $"{Math.Max(0, WaferCount)}/{Capacity}" : "미장착";
            var countSize = g.MeasureText(count, 9, true);
            double countX = tmOnRight ? plate.Left + 2 : plate.Right - 2 - countSize.Width;
            var countRect = new Rect(countX, plate.Bottom + 4, countSize.Width + 2, countSize.Height + 2);
            g.DrawText(count, 9, true, countRect, Mounted ? Palette.CardTitle : AlertText);
        }
    }
}
