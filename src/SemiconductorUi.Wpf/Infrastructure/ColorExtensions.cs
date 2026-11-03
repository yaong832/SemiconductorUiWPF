using System.Windows.Media;
using SemiconductorUi.Models;

namespace SemiconductorUi.Wpf.Infrastructure
{
    public static class ColorExtensions
    {
        public static Color ToMediaColor(this ArgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        public static SolidColorBrush ToBrush(this ArgbColor c)
        {
            var brush = new SolidColorBrush(c.ToMediaColor());
            brush.Freeze();
            return brush;
        }
    }
}
