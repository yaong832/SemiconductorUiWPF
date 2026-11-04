using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using SemiconductorUi.Wpf.Rendering;
using static SemiconductorUi.Wpf.Rendering.Gdi;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>원본 TmSideViewState</summary>
    public sealed class TmSideViewState
    {
        public EquipmentRegion Target { get; set; } = EquipmentRegion.TM;
        public long Height { get; set; }
        public double Extension { get; set; }
        public bool VacuumOn { get; set; }
        public bool WaferOnBlade { get; set; }
        public Color WaferColor { get; set; } = Palette.DefaultWafer;
        public bool[] FoupLevels { get; set; } = new bool[TmHeightEstimator.FoupLevels];
        public bool ChamberHasWafer { get; set; }
        public bool IsHardware { get; set; }
    }

    /// <summary>원본 Controls/TmSideViewControl: TM 높이 기준 정면/옆면 그림 (머리글 버튼으로 전환)</summary>
    public sealed class TmSideView : FrameworkElement
    {
        public enum ViewMode { Front, Cut }

        private const double HeaderHeight = 30, FooterHeight = 22, AxisWidth = 26, Pad = 10;
        private const double ToggleWidth = 38, ToggleHeight = 20, BladeThickness = 5, WaferThickness = 5;

        private static readonly Color GridColor = Rgb(222, 227, 235);
        private static readonly Color AxisText = Rgb(140, 150, 164);
        private static readonly Color ShelfColor = Rgb(170, 179, 193);
        private static readonly Color BladeFill = Rgb(238, 241, 245);
        private static readonly Color BladeEdge = Rgb(142, 152, 167);
        private static readonly Color PadOff = Rgb(154, 164, 178);
        private static readonly Color PadOn = Rgb(59, 209, 126);
        private static readonly Color CombColor = Rgb(90, 100, 116);
        private static readonly Color ChamberInside = Rgb(29, 37, 50);
        private static readonly Color SteelLight = Rgb(238, 241, 245);
        private static readonly Color SteelDark = Rgb(190, 198, 209);
        private static readonly Color SteelEdge = Rgb(142, 152, 167);
        private static readonly Color SmokeFill = Argb(70, 95, 106, 122);
        private static readonly Color SmokeEdge = Rgb(60, 69, 84);
        private static readonly Color CarriageColor = Rgb(91, 106, 128);
        private static readonly Color MastFill = Rgb(214, 220, 229);
        private static readonly Color MastEdge = Rgb(174, 184, 198);
        private static readonly Color PedestalColor = Rgb(214, 220, 229);
        private static readonly Color WaferEdge = Rgb(181, 154, 69);
        private static readonly Color ToggleBack = Rgb(242, 245, 249);
        private static readonly Color ToggleOn = Rgb(232, 239, 250);

        private TmHardwareController.TmPositionSet positions = new TmHardwareController.TmPositionSet();
        private TmSideViewState state = new TmSideViewState();
        private ViewMode mode = ViewMode.Front;

        public ViewMode Mode
        {
            get => mode;
            set { if (mode != value) { mode = value; InvalidateVisual(); } }
        }

        public void SetPositions(TmHardwareController.TmPositionSet teaching)
        {
            positions = teaching ?? new TmHardwareController.TmPositionSet();
            InvalidateVisual();
        }

        public void UpdateState(TmSideViewState newState)
        {
            state = newState ?? new TmSideViewState();
            InvalidateVisual();
        }

        private Rect CardRect => new Rect(0, 0, Math.Max(1, RenderSize.Width - 1), Math.Max(1, RenderSize.Height - 1));
        private Rect FrontToggleRect => new Rect(CardRect.Right - Pad - ToggleWidth * 2 - 2, (HeaderHeight - ToggleHeight) / 2 + 1, ToggleWidth, ToggleHeight);
        private Rect CutToggleRect => new Rect(CardRect.Right - Pad - ToggleWidth, (HeaderHeight - ToggleHeight) / 2 + 1, ToggleWidth, ToggleHeight);

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            var p = e.GetPosition(this);
            if (FrontToggleRect.Contains(p)) Mode = ViewMode.Front;
            else if (CutToggleRect.Contains(p)) Mode = ViewMode.Cut;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            Cursor = FrontToggleRect.Contains(p) || CutToggleRect.Contains(p) ? Cursors.Hand : null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var g = new Gdi(dc, this);
            g.Clear(Palette.CanvasBack, RenderSize);

            var cardPath = RoundedRect(CardRect, Palette.CardRadius);
            g.FillPath(Brush(Palette.CardFill), cardPath);
            g.DrawPath(Pen(Palette.CardBorder), cardPath);

            DrawHeader(g);

            var body = R(Pad, HeaderHeight + 12, CardRect.Width - Pad * 2, CardRect.Height - HeaderHeight - FooterHeight - 34);
            if (body.Width < 60 || body.Height < 60) { g.End(); return; }

            double maxHeight = GetMaxHeight();
            DrawHeightAxis(g, body, maxHeight);
            var drawArea = LTRB(body.Left + AxisWidth, body.Top, body.Right, body.Bottom);
            if (mode == ViewMode.Front) DrawFront(g, drawArea, body, maxHeight);
            else DrawCut(g, drawArea, body, maxHeight);

            DrawFooter(g);
            g.End();
        }

        private void DrawHeader(Gdi g)
        {
            string title = (mode == ViewMode.Front ? "TM 정면" : "TM 옆면") + " · " + GetTargetName(state.Target);
            g.DrawText(title, 9, true, R(Pad, 0, FrontToggleRect.Left - Pad - 4, HeaderHeight), Palette.CardTitle, ellipsis: true);
            DrawToggle(g, FrontToggleRect, "정면", mode == ViewMode.Front);
            DrawToggle(g, CutToggleRect, "옆면", mode == ViewMode.Cut);
            g.DrawLine(Pen(Rgb(232, 236, 242)), Pad, HeaderHeight, CardRect.Right - Pad, HeaderHeight);
        }

        private static void DrawToggle(Gdi g, Rect rect, string text, bool on)
        {
            g.FillPath(Brush(on ? ToggleOn : ToggleBack), RoundedRect(rect, 5));
            g.DrawText(text, 8, on, rect, on ? Palette.Primary : Palette.TextSecondary, TextAlignment.Center);
        }

        private double GetMaxHeight()
        {
            long max = Math.Max(positions.ChamberA_RaiseY, Math.Max(positions.ChamberB_RaiseY, positions.ChamberC_RaiseY));
            foreach (var v in positions.FoupA_RaiseY) max = Math.Max(max, v);
            foreach (var v in positions.FoupB_RaiseY) max = Math.Max(max, v);
            max = Math.Max(max, state.Height);
            return Math.Max(1, max * 1.08);
        }

        private static double MapY(Rect body, double maxHeight, long height)
        {
            double clamped = Math.Max(0, Math.Min(maxHeight, height));
            return body.Bottom - clamped / maxHeight * body.Height;
        }

        private static void DrawHeightAxis(Gdi g, Rect body, double maxHeight)
        {
            var grid = Pen(GridColor, 1, dashed: true);
            for (long h = 0; h <= maxHeight; h += 1000000)
            {
                double y = MapY(body, maxHeight, h);
                g.DrawLine(grid, body.Left + AxisWidth - 2, y, body.Right, y);
                string label = h == 0 ? "0" : (h / 1000000) + "M";
                g.DrawText(label, 7, false, R(body.Left, Math.Floor(y) - 7, AxisWidth - 6, 14), AxisText, TextAlignment.Right);
            }
        }

        private void DrawFront(Gdi g, Rect area, Rect body, double maxHeight)
        {
            var frame = LTRB(area.Left + 4, area.Top, area.Right - 4, area.Bottom);
            double bladeY = MapY(body, maxHeight, state.Height);

            if (TmHeightEstimator.IsFoup(state.Target))
            {
                double top = MapY(body, maxHeight, GetFoupTopHeight()) - 10;
                var box = LTRB(frame.Left, top, frame.Right, body.Bottom + 2);
                FillFoupShell(g, box);
                var shelf = Brush(CombColor);
                for (int level = 1; level <= TmHeightEstimator.FoupLevels; level++)
                {
                    double y = MapY(body, maxHeight, TmHeightEstimator.GetLandHeight(state.Target, positions, level));
                    g.FillRectangle(shelf, box.Left + 6, y, 12, 3);
                    g.FillRectangle(shelf, box.Right - 18, y, 12, 3);
                    g.DrawText(level.ToString(), 7, false, R(Math.Floor(box.Left) + 6, Math.Floor(y) - 13, 12, 12), AxisText, TextAlignment.Center, VAlign.Top);
                    if (state.FoupLevels != null && level - 1 < state.FoupLevels.Length && state.FoupLevels[level - 1])
                    {
                        DrawFlatWafer(g, box.Left + 16, box.Right - 16, y, state.WaferColor);
                    }
                }
            }
            else if (TmHeightEstimator.IsModule(state.Target))
            {
                long land = TmHeightEstimator.GetLandHeight(state.Target, positions, 1);
                long raise = TmHeightEstimator.GetRaiseHeight(state.Target, positions, 1);
                long margin = Math.Max(150000, (raise - land) / 2);
                double bodyTop = MapY(body, maxHeight, raise + margin * 2);
                double bodyBottom = MapY(body, maxHeight, Math.Max(0, land - margin * 2));
                var box = LTRB(frame.Left, bodyTop, frame.Right, bodyBottom);
                FillSteelBody(g, box);

                var slit = LTRB(box.Left + 14, MapY(body, maxHeight, raise + margin), box.Right - 14, MapY(body, maxHeight, Math.Max(0, land - margin)));
                var slitPath = RoundedRect(slit, 5);
                g.FillPath(Linear(Inflate(slit, 0, 1), ChamberInside, Rgb(58, 70, 92), GradientMode.Vertical), slitPath);
                g.DrawPath(Pen(Rgb(110, 121, 135)), slitPath);

                double stageY = MapY(body, maxHeight, land);
                double mid = (box.Left + box.Right) / 2;
                g.FillRectangle(Brush(ShelfColor), mid - slit.Width * 0.35, stageY, slit.Width * 0.7, 4);
                g.FillRectangle(Brush(PedestalColor), mid - 6, box.Bottom, 12, Math.Max(0, body.Bottom + 2 - box.Bottom));
                if (state.ChamberHasWafer)
                {
                    DrawFlatWafer(g, mid - slit.Width * 0.32, mid + slit.Width * 0.32, stageY, state.WaferColor);
                }
            }
            else
            {
                DrawCenteredNote(g, frame, "TM이 모듈을 향하고 있지 않습니다");
            }

            bool extended = state.Extension >= 0.5;
            double bladeMid = (frame.Left + frame.Right) / 2;
            double bladeHalf = frame.Width * 0.3;
            g.DrawLine(Pen(WithAlpha(120, Palette.Primary), 1, dashed: true), area.Left - 2, bladeY, frame.Right, bladeY);
            if (state.WaferOnBlade)
            {
                DrawFlatWafer(g, bladeMid - bladeHalf - 6, bladeMid + bladeHalf + 6, bladeY, state.WaferColor);
            }
            g.FillRectangle(Brush(WithAlpha((byte)(extended ? 90 : 255), CarriageColor)), bladeMid - 12, bladeY + BladeThickness, 24, 12);
            DrawBlade(g, new Rect(bladeMid - bladeHalf, bladeY, bladeHalf * 2, BladeThickness), extended,
                new[] { bladeMid - bladeHalf * 0.7, bladeMid + bladeHalf * 0.7 });
            if (state.VacuumOn) DrawVacuumDot(g, new Point(bladeMid + 22, bladeY + BladeThickness + 6));
        }

        private void DrawCut(Gdi g, Rect area, Rect body, double maxHeight)
        {
            double mastX = area.Left + 14;
            double moduleLeft = area.Left + area.Width * 0.46;
            double moduleRight = area.Right - 2;
            double moduleMid = (moduleLeft + moduleRight) / 2;
            double bladeY = MapY(body, maxHeight, state.Height);

            g.FillRectangle(Brush(MastFill), mastX - 5, body.Top, 10, body.Height);
            g.DrawRectangle(Pen(MastEdge), mastX - 5, body.Top, 10, body.Height);
            g.FillRectangle(Brush(Rgb(43, 53, 70)), mastX - 16, body.Bottom, 32, 7);

            if (TmHeightEstimator.IsFoup(state.Target))
            {
                double top = MapY(body, maxHeight, GetFoupTopHeight()) - 10;
                var box = LTRB(moduleLeft, top, moduleRight, body.Bottom + 2);
                FillFoupShell(g, box, true);
                var shelf = Brush(CombColor);
                for (int level = 1; level <= TmHeightEstimator.FoupLevels; level++)
                {
                    double y = MapY(body, maxHeight, TmHeightEstimator.GetLandHeight(state.Target, positions, level));
                    g.FillRectangle(shelf, box.Right - box.Width * 0.35, y, box.Width * 0.35 - 4, 3);
                    if (state.FoupLevels != null && level - 1 < state.FoupLevels.Length && state.FoupLevels[level - 1])
                    {
                        DrawFlatWafer(g, moduleMid - box.Width * 0.3, moduleMid + box.Width * 0.3, y, state.WaferColor);
                    }
                }
            }
            else if (TmHeightEstimator.IsModule(state.Target))
            {
                long land = TmHeightEstimator.GetLandHeight(state.Target, positions, 1);
                long raise = TmHeightEstimator.GetRaiseHeight(state.Target, positions, 1);
                long margin = Math.Max(150000, (raise - land) / 2);
                double bodyTop = MapY(body, maxHeight, raise + margin * 2);
                double bodyBottom = MapY(body, maxHeight, Math.Max(0, land - margin * 2));
                double slitTop = MapY(body, maxHeight, raise + margin);
                double slitBottom = MapY(body, maxHeight, Math.Max(0, land - margin));

                g.FillRectangle(Brush(ChamberInside), moduleLeft, bodyTop, moduleRight - moduleLeft, bodyBottom - bodyTop);
                var pen = Pen(Palette.CardBorder, 1.5);
                g.DrawLine(pen, moduleLeft, bodyTop, moduleRight, bodyTop);
                g.DrawLine(pen, moduleRight, bodyTop, moduleRight, bodyBottom);
                g.DrawLine(pen, moduleRight, bodyBottom, moduleLeft, bodyBottom);
                g.DrawLine(pen, moduleLeft, bodyTop, moduleLeft, slitTop);
                g.DrawLine(pen, moduleLeft, slitBottom, moduleLeft, bodyBottom);

                var valve = Brush(Palette.ValveClosed);
                g.FillRectangle(valve, moduleLeft - 2, slitTop - 8, 4, 8);
                g.FillRectangle(valve, moduleLeft - 2, slitBottom, 4, 8);

                double stageY = MapY(body, maxHeight, land);
                g.FillRectangle(Brush(ShelfColor), moduleMid - (moduleRight - moduleLeft) * 0.3, stageY, (moduleRight - moduleLeft) * 0.6, 4);
                g.FillRectangle(Brush(PedestalColor), moduleMid - 5, stageY + 4, 10, Math.Max(0, bodyBottom - stageY - 4));
                if (state.ChamberHasWafer)
                {
                    DrawFlatWafer(g, moduleMid - (moduleRight - moduleLeft) * 0.28, moduleMid + (moduleRight - moduleLeft) * 0.28, stageY, state.WaferColor);
                }
            }
            else
            {
                DrawCenteredNote(g, LTRB(moduleLeft, area.Top, moduleRight, area.Bottom), "모듈 없음");
            }

            bool extended = state.Extension >= 0.5;
            double bladeLength = Math.Max(30, (moduleRight - moduleLeft) * 0.55);
            double tipRetracted = mastX + 14 + bladeLength;
            double tipExtended = moduleMid + bladeLength / 2;
            double tip = tipRetracted + (tipExtended - tipRetracted) * Math.Max(0, Math.Min(1, state.Extension));
            var carriage = Brush(CarriageColor);
            g.FillRectangle(carriage, mastX - 11, bladeY - 2, 22, 16);
            g.FillRectangle(carriage, mastX + 11, bladeY + BladeThickness, Math.Max(2, tip - bladeLength - mastX - 11), 5);
            DrawBlade(g, new Rect(tip - bladeLength, bladeY, bladeLength, BladeThickness), extended, new[] { tip - bladeLength + 8, tip - 8 });
            if (state.WaferOnBlade) DrawFlatWafer(g, tip - bladeLength, tip, bladeY, state.WaferColor);
            if (state.VacuumOn) DrawVacuumDot(g, new Point(mastX, bladeY + 6));
        }

        private long GetFoupTopHeight() => TmHeightEstimator.GetRaiseHeight(state.Target, positions, TmHeightEstimator.FoupLevels);

        private void DrawFooter(Gdi g)
        {
            string cylinder = state.Extension >= 0.5 ? "전진" : "후진";
            string vacuum = state.VacuumOn ? "진공 ON" : "진공 OFF";
            string height = state.IsHardware ? state.Height.ToString("N0") : "약 " + state.Height.ToString("N0");
            string text = "높이 " + height + " · " + cylinder + " · " + vacuum;
            g.DrawText(text, 8, false, R(Pad, CardRect.Bottom - FooterHeight, CardRect.Width - Pad * 2, FooterHeight - 4), Palette.CardSubText, ellipsis: true);
        }

        private static void FillSteelBody(Gdi g, Rect rect)
        {
            var path = RoundedRect(rect, 6);
            g.FillPath(Linear(Inflate(rect, 1, 0), SteelDark, SteelLight, GradientMode.Horizontal, 0.35), path);
            g.DrawPath(Pen(SteelEdge), path);
            var bolt = Brush(Rgb(232, 236, 241));
            var boltPen = Pen(SteelEdge, 0.7);
            foreach (var p in new[] { new Point(rect.Left + 7, rect.Top + 7), new Point(rect.Right - 7, rect.Top + 7), new Point(rect.Left + 7, rect.Bottom - 7), new Point(rect.Right - 7, rect.Bottom - 7) })
            {
                g.FillEllipse(bolt, p.X - 2.2, p.Y - 2.2, 4.4, 4.4);
                g.DrawEllipse(boltPen, p.X - 2.2, p.Y - 2.2, 4.4, 4.4);
            }
        }

        private static void FillFoupShell(Gdi g, Rect rect, bool openLeft = false)
        {
            g.FillPath(Brush(SmokeFill), RoundedRect(rect, 8));
            var pen = Pen(SmokeEdge, 1.4);
            if (openLeft)
            {
                const double r = 8;
                var outline = new GPath();
                outline.AddLine(rect.Left, rect.Top, rect.Right - r, rect.Top);
                outline.AddArc(rect.Right - r * 2, rect.Top, r * 2, r * 2, 270, 90);
                outline.AddLine(rect.Right, rect.Top + r, rect.Right, rect.Bottom - r);
                outline.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                outline.AddLine(rect.Right - r, rect.Bottom, rect.Left, rect.Bottom);
                g.DrawPath(pen, outline.ToGeometry(false));
            }
            else
            {
                g.DrawPath(pen, RoundedRect(rect, 8));
            }
            double w = Math.Min(48, rect.Width * 0.3);
            g.FillRectangle(Brush(SmokeEdge), rect.Left + (rect.Width - w) / 2, rect.Top - 6, w, 6);
        }

        private void DrawBlade(Gdi g, Rect rect, bool extended, double[] padCenters)
        {
            var path = RoundedRect(rect, 2);
            g.FillPath(Brush(BladeFill), path);
            g.DrawPath(Pen(extended ? Palette.Primary : BladeEdge, extended ? 1.6 : 1), path);
            var pad = Brush(state.VacuumOn ? PadOn : PadOff);
            foreach (var x in padCenters) g.FillRectangle(pad, x - 4, rect.Top - 2, 8, 2.5);
        }

        private static void DrawFlatWafer(Gdi g, double left, double right, double bottomY, Color color)
        {
            var rect = LTRB(left, bottomY - WaferThickness, right, bottomY);
            var path = RoundedRect(rect, WaferThickness / 2);
            g.FillPath(Brush(color), path);
            g.DrawPath(Pen(WaferEdge, 0.8), path);
        }

        private static void DrawVacuumDot(Gdi g, Point center)
        {
            const double r = 3.5;
            g.FillEllipse(Brush(Palette.Success), center.X - r, center.Y - r, r * 2, r * 2);
            g.DrawEllipse(Pen(Colors.White, 1.2), center.X - r, center.Y - r, r * 2, r * 2);
        }

        private static void DrawCenteredNote(Gdi g, Rect rect, string text)
        {
            g.DrawText(text, 8, false, rect, Palette.CardSubText, TextAlignment.Center, VAlign.Center, wrap: true);
        }

        private static string GetTargetName(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return "Chamber A";
                case EquipmentRegion.ChamberB: return "Chamber B";
                case EquipmentRegion.ChamberC: return "Chamber C";
                case EquipmentRegion.FoupA: return "FOUP A";
                case EquipmentRegion.FoupB: return "FOUP B";
                default: return "원점";
            }
        }
    }
}
