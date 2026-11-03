namespace SemiconductorUi.Models
{
    /// <summary>
    /// UI 프레임워크에 독립적인 ARGB 색상 값.
    /// Core는 System.Drawing/WPF에 의존하지 않도록 이 타입을 사용하고,
    /// 표현 계층(WPF)에서 System.Windows.Media.Color로 변환해 사용한다.
    /// </summary>
    public readonly struct ArgbColor
    {
        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public ArgbColor(byte a, byte r, byte g, byte b)
        {
            A = a; R = r; G = g; B = b;
        }

        public static ArgbColor FromArgb(int a, int r, int g, int b)
            => new ArgbColor(Clamp(a), Clamp(r), Clamp(g), Clamp(b));

        public static ArgbColor FromRgb(int r, int g, int b) => FromArgb(255, r, g, b);

        private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }
}
