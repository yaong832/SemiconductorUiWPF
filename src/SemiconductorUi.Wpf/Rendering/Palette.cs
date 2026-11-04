using System.Windows.Media;

namespace SemiconductorUi.Wpf.Rendering
{
    /// <summary>원본 Helpers/UiTheme + Helpers/EquipmentCanvasStyler 색상과 치수</summary>
    public static class Palette
    {
        // UiTheme
        public static readonly Color Primary = Color.FromRgb(45, 98, 196);
        public static readonly Color TextSecondary = Color.FromRgb(100, 112, 128);
        public static readonly Color Success = Color.FromRgb(36, 150, 90);

        // EquipmentCanvasStyler
        public static readonly Color CanvasBack = Color.FromRgb(236, 239, 244);
        public static readonly Color CardFill = Colors.White;
        public static readonly Color CardBorder = Color.FromRgb(184, 192, 205);
        public static readonly Color CardTitle = Color.FromRgb(38, 46, 60);
        public static readonly Color CardSubText = Color.FromRgb(104, 114, 130);
        public static readonly Color ValveClosed = Color.FromRgb(72, 84, 102);
        public static readonly Color LampOff = Color.FromRgb(60, 60, 60);

        public const double ChamberWidth = 176;
        public const double ChamberHeight = 100;
        public const double FoupWidth = 176;
        public const double FoupHeight = 96;
        public const double CardRadius = 10;
        public const double HeaderHeight = 28;
        public const double WellSize = 56;

        // 램프 색 (원본 AppSettings.Lamp*Bright)
        public static readonly Color LampRed = Color.FromRgb(210, 60, 50);
        public static readonly Color LampYellow = Color.FromRgb(230, 190, 40);
        public static readonly Color LampGreen = Color.FromRgb(55, 180, 90);

        public static readonly Color DefaultWafer = Color.FromRgb(250, 232, 168);
    }
}
