using System;
using System.Windows;
using System.Windows.Media;
using static SemiconductorUi.Wpf.Rendering.Gdi;

namespace SemiconductorUi.Wpf.Rendering
{
    public enum ValveSide { Left, Top, Right, Bottom }

    /// <summary>
    /// 원본 EquipmentCanvasStyler.PaintChamberCard / DrawLampDot / DrawSignalTower를 옮긴 것.
    /// </summary>
    public static class ChamberPainter
    {
        private static readonly Color BodyLight = Rgb(246, 248, 250);
        private static readonly Color BodyDark = Rgb(205, 212, 221);
        private static readonly Color BodyEdge = Rgb(150, 160, 174);
        private static readonly Color LidCenter = Rgb(252, 253, 254);
        private static readonly Color LidEdge = Rgb(178, 187, 199);
        private static readonly Color LidRim = Rgb(142, 152, 167);
        private static readonly Color BoltFill = Rgb(232, 236, 241);
        private static readonly Color BoltEdge = Rgb(140, 151, 166);
        private static readonly Color GlassLight = Rgb(78, 94, 120);
        private static readonly Color GlassDark = Rgb(26, 35, 50);
        private static readonly Color PlateFill = Rgb(42, 50, 64);
        private static readonly Color PlateText = Rgb(233, 237, 243);
        private static readonly Color ValveBody = Rgb(140, 151, 166);
        private static readonly Color ValveOpenGlow = Rgb(59, 209, 126);
        private const int ChamberLidBolts = 12;

        public static Rect GetWellBounds(Rect card)
        {
            double bodyTop = card.Y + Palette.HeaderHeight;
            double bodyHeight = card.Height - Palette.HeaderHeight;
            double size = Math.Min(Palette.WellSize, Math.Max(16, bodyHeight - 10));
            return new Rect(
                Math.Floor(card.X + (card.Width - size) / 2),
                Math.Floor(bodyTop + (bodyHeight - size) / 2 - 2),
                size, size);
        }

        public static void PaintChamberCard(Gdi g, Rect client, string title, Color lampColor, double valveOpen, ValveSide valveSide,
            bool hasWafer, Color waferColor)
        {
            var card = new Rect(client.X + 1, client.Y + 1, client.Width - 4, client.Height - 4);

            g.FillPath(Brush(Argb(38, 28, 36, 51)), RoundedRect(new Rect(card.X + 1.5, card.Y + 2.5, card.Width, card.Height), Palette.CardRadius));

            var body = RoundedRect(card, Palette.CardRadius);
            g.FillPath(Linear(card, BodyLight, BodyDark, GradientMode.ForwardDiagonal), body);
            g.DrawPath(Pen(BodyEdge, 1), body);

            const int boltInset = 8;
            foreach (var corner in new[]
            {
                new Point(card.Right - boltInset, card.Top + Palette.HeaderHeight + 4),
                new Point(card.Left + boltInset, card.Bottom - boltInset),
                new Point(card.Right - boltInset, card.Bottom - boltInset),
                new Point(card.Left + boltInset, card.Top + Palette.HeaderHeight + 4)
            })
            {
                DrawBolt(g, corner, 2.6);
            }

            DrawNameplate(g, card, title);
            DrawLampDot(g, new Rect(card.Right - 24, card.Top + (Palette.HeaderHeight - 12) / 2, 12, 12), lampColor);

            var well = GetWellBounds(client);
            DrawChamberLid(g, well);
            DrawSlitValve(g, card, valveOpen, valveSide);

            if (hasWafer)
            {
                // 원본: 48x48 웨이퍼 패널을 뷰포트 가운데에 두고 3px 안쪽에 원을 그림 (WaferPanel_Paint)
                var c = Center(well);
                var rect = new Rect(c.X - 21, c.Y - 21, 42, 42);
                g.FillEllipse(Brush(waferColor), rect);
                g.DrawEllipse(Pen(Argb(180, 80, 80, 80), 1.2), rect);
            }
        }

        private static void DrawNameplate(Gdi g, Rect card, string title)
        {
            title = title ?? string.Empty;
            double maxWidth = card.Width - 52;
            var textSize = g.MeasureText(title, 9, true);
            var plate = new Rect(card.Left + 8, card.Top + 5, Math.Min(maxWidth, textSize.Width) + 16, Palette.HeaderHeight - 9);
            g.FillPath(Brush(PlateFill), RoundedRect(plate, 4));
            g.DrawText(title, 9, true, Inflate(plate, -8, 0), PlateText, TextAlignment.Left, VAlign.Center, ellipsis: true);
        }

        private static void DrawChamberLid(Gdi g, Rect well)
        {
            var lid = Inflate(well, 4, 4);
            g.FillEllipse(Radial(lid, LidCenter, LidEdge, new Point(lid.Left + lid.Width * 0.36, lid.Top + lid.Height * 0.32)), lid);
            g.DrawEllipse(Pen(LidRim, 1.2), lid);

            double cx = lid.Left + lid.Width / 2, cy = lid.Top + lid.Height / 2;
            double boltRadius = lid.Width / 2 - 4;
            for (int i = 0; i < ChamberLidBolts; i++)
            {
                double angle = (i + 0.5) * 2 * Math.PI / ChamberLidBolts;
                DrawBolt(g, new Point(cx + boltRadius * Math.Cos(angle), cy + boltRadius * Math.Sin(angle)), 1.7);
            }

            var frame = Inflate(well, -3, -3);
            g.FillEllipse(Brush(Rgb(211, 217, 225)), frame);
            g.DrawEllipse(Pen(LidRim, 1), frame);

            var glass = Inflate(frame, -3, -3);
            g.FillEllipse(Radial(glass, GlassLight, GlassDark, new Point(glass.Left + glass.Width * 0.35, glass.Top + glass.Height * 0.3)), glass);

            var arc = Inflate(glass, -5, -5);
            if (arc.Width > 4 && arc.Height > 4)
            {
                g.DrawArc(Pen(Argb(90, 255, 255, 255), 1.6, roundCaps: true), arc, 200, 60);
            }
        }

        public static void DrawBolt(Gdi g, Point center, double radius)
        {
            var rect = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            g.FillEllipse(Brush(BoltFill), rect);
            g.DrawEllipse(Pen(BoltEdge, 0.8), rect);
        }

        private static void DrawSlitValve(Gdi g, Rect card, double openRatio, ValveSide side)
        {
            const double bodyThickness = 11, bodyLength = 58, slotThickness = 4, slotLength = 46;
            bool horizontal = side == ValveSide.Top || side == ValveSide.Bottom;
            double bodyTop = card.Top + Palette.HeaderHeight + (card.Height - Palette.HeaderHeight - bodyLength) / 2;

            Rect body;
            switch (side)
            {
                case ValveSide.Bottom: body = new Rect(card.Left + (card.Width - bodyLength) / 2, card.Bottom - bodyThickness, bodyLength, bodyThickness); break;
                case ValveSide.Top: body = new Rect(card.Left + (card.Width - bodyLength) / 2, card.Top, bodyLength, bodyThickness); break;
                case ValveSide.Left: body = new Rect(card.Left, bodyTop, bodyThickness, bodyLength); break;
                default: body = new Rect(card.Right - bodyThickness, bodyTop, bodyThickness, bodyLength); break;
            }

            var bodyPath = RoundedRect(body, 3);
            g.FillPath(Brush(ValveBody), bodyPath);
            g.DrawPath(Pen(Rgb(110, 121, 135), 0.8), bodyPath);

            var slot = horizontal
                ? new Rect(body.Left + (bodyLength - slotLength) / 2, body.Top + (bodyThickness - slotThickness) / 2, slotLength, slotThickness)
                : new Rect(body.Left + (bodyThickness - slotThickness) / 2, body.Top + (bodyLength - slotLength) / 2, slotThickness, slotLength);
            g.FillRectangle(Brush(Palette.ValveClosed), slot);

            double gap = (horizontal ? slot.Width : slot.Height) * Math.Max(0, Math.Min(1, openRatio));
            if (gap > 0.5)
            {
                var open = horizontal
                    ? new Rect(slot.X + (slot.Width - gap) / 2, slot.Y, gap, slot.Height)
                    : new Rect(slot.X, slot.Y + (slot.Height - gap) / 2, slot.Width, gap);
                g.FillRectangle(Brush(WithAlpha(80, ValveOpenGlow)), Inflate(open, 2, 2));
                g.FillRectangle(Brush(ValveOpenGlow), open);
            }
        }

        public static void DrawLampDot(Gdi g, Rect rect, Color color)
        {
            bool off = IsLampOff(color);
            var fill = off ? Rgb(214, 219, 227) : color;
            g.FillEllipse(Brush(fill), rect);
            g.DrawEllipse(Pen(off ? Rgb(176, 184, 197) : Dark(color, 0.1), 1), rect);
            if (!off)
            {
                g.DrawEllipse(Pen(WithAlpha(70, color), 3), Inflate(rect, 2, 2));
            }
        }

        public static bool IsLampOff(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            return max - min < 12;
        }

        /// <summary>원본 ApplyMainLampStyle의 Paint: 신호탑 + "상태 램프" + 오류/대기/진행 점</summary>
        public static void PaintLampLegend(Gdi g, Size size, Color red, Color yellow, Color green)
        {
            var rect = new Rect(0, 0, size.Width - 1, size.Height - 1);
            var path = RoundedRect(rect, rect.Height / 2);
            g.FillPath(Brush(Palette.CardFill), path);
            g.DrawPath(Pen(Palette.CardBorder), path);

            DrawSignalTower(g, new Rect(16, 3, 12, size.Height - 6), red, yellow, green);
            g.DrawText("상태 램프", 8.5, true, new Rect(36, 0, 60, size.Height), Palette.CardSubText);

            double x = 96;
            x = DrawLampItem(g, x, size.Height, red, "오류");
            x = DrawLampItem(g, x, size.Height, yellow, "대기");
            DrawLampItem(g, x, size.Height, green, "진행");
        }

        private static void DrawSignalTower(Gdi g, Rect area, Color red, Color yellow, Color green)
        {
            const double poleHeight = 4;
            double segment = (area.Height - poleHeight - 2) / 3;
            var lit = new[] { red, yellow, green };
            var offs = new[] { Rgb(232, 196, 196), Rgb(236, 222, 190), Rgb(196, 226, 206) };
            for (int i = 0; i < 3; i++)
            {
                bool on = !IsLampOff(lit[i]);
                var r = new Rect(area.X, area.Y + i * segment, area.Width, Math.Max(0, segment - 1));
                var p = RoundedRect(r, 2);
                g.FillPath(Brush(on ? lit[i] : offs[i]), p);
                g.DrawPath(Pen(Rgb(140, 151, 166), 0.8), p);
            }
            var pole = Brush(Rgb(94, 106, 122));
            g.FillRectangle(pole, area.X + area.Width / 2 - 1.5, area.Bottom - poleHeight - 2, 3, poleHeight);
            g.FillRectangle(pole, area.X - 1, area.Bottom - 2, area.Width + 2, 2);
        }

        private static double DrawLampItem(Gdi g, double x, double height, Color color, string text)
        {
            const double dot = 12;
            DrawLampDot(g, new Rect(x, Math.Floor((height - dot) / 2), dot, dot), color);
            var textSize = g.MeasureText(text, 8.5, false);
            g.DrawText(text, 8.5, false, new Rect(x + dot + 6, 0, textSize.Width + 4, height), Palette.CardTitle);
            return x + dot + 6 + textSize.Width + 14;
        }
    }
}
