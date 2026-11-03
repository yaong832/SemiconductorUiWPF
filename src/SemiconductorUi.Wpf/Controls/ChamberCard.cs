using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Wpf.Rendering;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>원본 EquipmentCanvasStyler.ApplyChamberCard로 꾸민 챔버 패널</summary>
    public sealed class ChamberCard : FrameworkElement
    {
        public ChamberCard()
        {
            Cursor = Cursors.Hand;
        }

        public string Title { get; set; } = "Chamber";
        public ValveSide ValveSide { get; set; } = ValveSide.Right;
        public Color LampColor { get; set; } = Palette.LampOff;
        public double ValveOpen { get; set; }
        public bool HasWafer { get; set; }
        public Color WaferColor { get; set; } = Palette.DefaultWafer;

        protected override void OnRender(DrawingContext dc)
        {
            var g = new Gdi(dc, this);
            g.Clear(Palette.CanvasBack, RenderSize);
            ChamberPainter.PaintChamberCard(g, new Rect(RenderSize), Title, LampColor, ValveOpen, ValveSide, HasWafer, WaferColor);
            g.End();
        }
    }

    /// <summary>원본 ApplyMainLampStyle로 꾸민 상태 램프 범례</summary>
    public sealed class LampLegend : FrameworkElement
    {
        public Color Red { get; set; } = Palette.LampOff;
        public Color Yellow { get; set; } = Palette.LampOff;
        public Color Green { get; set; } = Palette.LampOff;

        protected override void OnRender(DrawingContext dc)
        {
            var g = new Gdi(dc, this);
            g.Clear(Palette.CanvasBack, RenderSize);
            ChamberPainter.PaintLampLegend(g, RenderSize, Red, Yellow, Green);
            g.End();
        }
    }
}
