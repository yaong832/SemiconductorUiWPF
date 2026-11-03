using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SemiconductorUi.Controls;
using SemiconductorUi.Wpf.Rendering;
using static SemiconductorUi.Wpf.Rendering.Gdi;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>
    /// 원본 Controls/TmVisualizationControl: 위에서 본 TM 로봇(베이스, 회전 몸체, 실린더, U자 포크).
    /// 캔버스 전체를 덮는 배경 층으로 쓰이며, 모듈 위치는 EquipmentCanvas가 SetLayout으로 넘긴다.
    /// </summary>
    public sealed class TmTopView : FrameworkElement
    {
        private EquipmentRegion targetRegion = EquipmentRegion.TM;
        private bool carryingWafer;
        private Color waferColor = Palette.DefaultWafer;
        private double currentExtensionFactor = 0.65, targetExtensionFactor = 0.65;
        private double currentAngleRad = Math.PI / 2, targetAngleRad = Math.PI / 2;
        private bool isSimulationMode = true;
        private readonly DispatcherTimer animationTimer;
        private readonly System.Diagnostics.Stopwatch animationClock = new System.Diagnostics.Stopwatch();
        private double lastAnimationSeconds;
        private double angularVelocity;   // rad/s, 부호 포함
        private double extensionVelocity; // factor/s, 부호 포함

        // 원본과 같은 움직임: 천천히 출발해 일정 속도로 돌고, 도착 전에 감속한다.
        private const double MaxAngularSpeed = 150.0 * Math.PI / 180.0;      // 초당 150도
        private const double AngularAcceleration = 400.0 * Math.PI / 180.0;  // 초당² 400도
        private const double MaxExtensionSpeed = 1.6;
        private const double ExtensionAcceleration = 6;
        // 6시 방향(화면 아래). 실제 로봇은 FOUP A와 FOUP B 사이 아래쪽으로 돌지 않는다.
        private const double ForbiddenAngle = Math.PI / 2;
        // 이 값보다 블레이드가 나와 있으면 돌지 않는다.
        private const double RotationSafeExtension = 0.75;
        private readonly Dictionary<EquipmentRegion, Point> anchors = new Dictionary<EquipmentRegion, Point>();
        private readonly List<Rect> moduleBounds = new List<Rect>();
        private Point hubCenter;
        private bool hasLayout;
        private double rangeRadius = 60;

        private const double BodyRadius = 26, BaseRadius = 24, BeamThickness = 12, WaferRadius = 22;
        private const double RetractedLength = BodyRadius + WaferRadius + 4;
        private const double RetractedFactor = 0.7, ExtendedFactor = 1.3;
        private const double RangeTrackWidth = 4, MinRangeRadius = BodyRadius + 14, MaxRangeRadius = 96, RangeGapToModule = 10, WellMargin = 7;

        private static readonly Color WellLight = Rgb(190, 198, 209);
        private static readonly Color WellDark = Rgb(166, 175, 188);
        private static readonly Color WellEdge = Rgb(142, 152, 167);
        private static readonly Color RangeGroove = Argb(150, 118, 130, 148);
        private static readonly Color RangeTick = Rgb(84, 96, 114);
        private static readonly Color BaseLight = Rgb(234, 238, 243);
        private static readonly Color BaseDark = Rgb(170, 179, 192);
        private static readonly Color MetalEdge = Rgb(124, 135, 152);
        private static readonly Color BoltFill = Rgb(232, 236, 241);
        private static readonly Color BeamLight = Rgb(205, 212, 222);
        private static readonly Color BeamDark = Rgb(142, 153, 169);
        private static readonly Color TubeLight = Rgb(236, 239, 243);
        private static readonly Color TubeDark = Rgb(137, 148, 164);
        private static readonly Color RodFill = Rgb(221, 226, 232);
        private static readonly Color TurretLight = Rgb(75, 84, 101);
        private static readonly Color TurretDark = Rgb(38, 44, 55);
        private static readonly Color ForkFill = Rgb(238, 241, 245);
        private static readonly Color ForkEdge = Rgb(142, 152, 167);
        private static readonly Color PadOff = Rgb(154, 164, 178);
        private static readonly Color PadOn = Rgb(59, 209, 126);
        private static readonly Color HoseColor = Rgb(58, 67, 82);
        private static readonly Color WaferEdge = Rgb(181, 154, 69);

        public TmTopView()
        {
            animationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            animationTimer.Tick += AnimationTimer_Tick;
            IsHitTestVisible = false;
        }

        public void UpdateTmState(EquipmentRegion target, bool carrying, double extensionFactor, Color? waferDisplayColor = null, bool isSimulation = true)
        {
            targetRegion = target;
            carryingWafer = carrying;
            isSimulationMode = isSimulation;
            if (waferDisplayColor.HasValue) waferColor = waferDisplayColor.Value;
            targetExtensionFactor = Math.Max(0.4, Math.Min(1.6, extensionFactor));
            targetAngleRad = GetBladeAngle(targetRegion, isSimulation);
            if (!animationTimer.IsEnabled)
            {
                lastAnimationSeconds = 0;
                animationClock.Restart();
                animationTimer.Start();
            }
            InvalidateVisual();
        }

        public void SetLayout(Point center, IDictionary<EquipmentRegion, Point> moduleAnchors, IEnumerable<Rect> modules)
        {
            hubCenter = center;
            anchors.Clear();
            if (moduleAnchors != null) foreach (var pair in moduleAnchors) anchors[pair.Key] = pair.Value;
            moduleBounds.Clear();
            if (modules != null) moduleBounds.AddRange(modules);
            rangeRadius = CalculateRangeRadius(hubCenter);
            hasLayout = true;
            targetAngleRad = GetBladeAngle(targetRegion, isSimulationMode);
            currentAngleRad = targetAngleRad;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var g = new Gdi(dc, this);
            var center = hasLayout ? hubCenter : new Point(RenderSize.Width / 2, RenderSize.Height / 2);
            DrawRobotWell(g, center);
            DrawRotationRange(g, center);
            DrawBase(g, center);
            DrawRobot(g, center);
            DrawCenterMarks(g, center);
            DrawCurrentAngleMarker(g, center);
            g.End();
        }

        private double CalculateRangeRadius(Point center)
        {
            if (moduleBounds.Count == 0) return 60;
            double nearest = double.MaxValue;
            foreach (var rect in moduleBounds)
            {
                double dx = Math.Max(Math.Max(rect.Left - center.X, 0), center.X - rect.Right);
                double dy = Math.Max(Math.Max(rect.Top - center.Y, 0), center.Y - rect.Bottom);
                nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dy * dy));
            }
            return Math.Max(MinRangeRadius, Math.Min(MaxRangeRadius, nearest - RangeGapToModule));
        }

        private void DrawRotationRange(Gdi g, Point center)
        {
            if (anchors.Count == 0) return;
            var ring = new Rect(center.X - rangeRadius, center.Y - rangeRadius, rangeRadius * 2, rangeRadius * 2);
            if (anchors.TryGetValue(EquipmentRegion.FoupA, out var foupA) && anchors.TryGetValue(EquipmentRegion.FoupB, out var foupB))
            {
                double start = ToDegrees(AngleTo(center, foupA));
                double end = ToDegrees(AngleTo(center, foupB));
                double sweep = end - start;
                while (sweep <= 0) sweep += 360;
                g.DrawArc(Pen(RangeGroove, RangeTrackWidth, roundCaps: true), ring, start, sweep);
            }
            else
            {
                g.DrawEllipse(Pen(RangeGroove, RangeTrackWidth), ring);
            }

            var tick = Pen(RangeTick, 2);
            foreach (var anchor in anchors.Values)
            {
                double angle = AngleTo(center, anchor);
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                double inner = rangeRadius - RangeTrackWidth - 2, outer = rangeRadius + RangeTrackWidth + 2;
                g.DrawLine(tick, center.X + inner * cos, center.Y + inner * sin, center.X + outer * cos, center.Y + outer * sin);
            }
        }

        private void DrawCurrentAngleMarker(Gdi g, Point center)
        {
            if (anchors.Count == 0) return;
            const double radius = 5.5;
            var point = new Point(center.X + rangeRadius * Math.Cos(currentAngleRad), center.Y + rangeRadius * Math.Sin(currentAngleRad));
            var rect = new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2);
            g.FillEllipse(Brush(Palette.Primary), rect);
            g.DrawEllipse(Pen(Colors.White, 2), rect);
        }

        private void DrawRobotWell(Gdi g, Point center)
        {
            double radius = rangeRadius + WellMargin;
            var rect = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            g.FillEllipse(Radial(rect, WellLight, WellDark, new Point(center.X, center.Y - radius * 0.1)), rect);
            g.DrawEllipse(Pen(WellEdge, 1.4), rect);
            g.DrawEllipse(Pen(Argb(120, 255, 255, 255), 1), Inflate(rect, -3, -3));
        }

        private static void DrawBase(Gdi g, Point center)
        {
            var rect = new Rect(center.X - BaseRadius, center.Y - BaseRadius, BaseRadius * 2, BaseRadius * 2);
            g.FillEllipse(Brush(Argb(45, 28, 36, 51)), rect.X + 1.5, rect.Y + 2.5, rect.Width, rect.Height);
            g.FillEllipse(Radial(rect, BaseLight, BaseDark, new Point(center.X - BaseRadius * 0.3, center.Y - BaseRadius * 0.35)), rect);
            g.DrawEllipse(Pen(MetalEdge, 1.2), rect);

            const int bolts = 10;
            double boltRadius = BaseRadius - 4;
            var fill = Brush(BoltFill);
            var pen = Pen(MetalEdge, 0.7);
            for (int i = 0; i < bolts; i++)
            {
                double angle = (i + 0.5) * 2 * Math.PI / bolts;
                var bolt = new Rect(center.X + boltRadius * Math.Cos(angle) - 1.6, center.Y + boltRadius * Math.Sin(angle) - 1.6, 3.2, 3.2);
                g.FillEllipse(fill, bolt);
                g.DrawEllipse(pen, bolt);
            }
        }

        private void DrawRobot(Gdi g, Point center)
        {
            double bladeLength = GetBladeLength();
            double forkRoot = bladeLength - WaferRadius - 8;
            double forkTip = bladeLength + WaferRadius - 2;
            bool extended = currentExtensionFactor >= (RetractedFactor + ExtendedFactor) / 2;

            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(ToDegrees(currentAngleRad));

            // 빔
            var beam = new Rect(12, -BeamThickness / 2, Math.Max(10, forkRoot + 6 - 12), BeamThickness);
            var beamPath = RoundedRect(beam, 3);
            g.FillPath(Linear(new Rect(beam.X, beam.Y - 0.5, beam.Width, beam.Height + 1), BeamLight, BeamDark, GradientMode.Vertical), beamPath);
            g.DrawPath(Pen(MetalEdge, 0.8), beamPath);
            g.DrawLine(Pen(MetalEdge, 1), beam.Left + 4, 0, beam.Right - 4, 0);

            // 포크
            var forkPath = CreateForkPath(forkRoot, forkTip);
            if (extended) g.DrawPath(Pen(WithAlpha(70, Palette.Primary), 5, roundJoin: true), forkPath);
            g.FillPath(Brush(ForkFill), forkPath);
            g.DrawPath(Pen(extended ? Palette.Primary : ForkEdge, extended ? 1.6 : 1), forkPath);

            // 진공 패드
            var pad = Brush(carryingWafer ? PadOn : PadOff);
            var padEdge = Pen(Rgb(94, 106, 122), 0.6);
            foreach (var p in new[] { new Point(forkTip - 7, -10), new Point(forkTip - 7, 10), new Point(forkRoot + 7, 0) })
            {
                var r = new Rect(p.X - 2.6, p.Y - 2.6, 5.2, 5.2);
                g.FillEllipse(pad, r);
                g.DrawEllipse(padEdge, r);
            }

            if (carryingWafer)
            {
                var wafer = new Rect(bladeLength - WaferRadius, -WaferRadius, WaferRadius * 2, WaferRadius * 2);
                g.FillEllipse(Brush(waferColor), wafer);
                g.DrawEllipse(Pen(WaferEdge, 1.2), wafer);
            }

            // 공압 실린더
            const double barrelTop = -20, barrelHeight = 10, barrelStart = 4, barrelEnd = 40;
            double rodEnd = Math.Max(barrelEnd + 3, forkRoot - 2);
            g.FillRectangle(Brush(RodFill), barrelEnd - 2, barrelTop + 3, rodEnd - barrelEnd + 2, 4);
            g.DrawRectangle(Pen(ForkEdge, 0.6), barrelEnd - 2, barrelTop + 3, rodEnd - barrelEnd + 2, 4);
            var barrel = new Rect(barrelStart, barrelTop, barrelEnd - barrelStart, barrelHeight);
            var barrelPath = RoundedRect(barrel, 3);
            g.FillPath(Linear(new Rect(barrel.X, barrel.Y - 0.5, barrel.Width, barrel.Height + 1), TubeLight, TubeDark, GradientMode.Vertical), barrelPath);
            g.DrawPath(Pen(MetalEdge, 0.8), barrelPath);
            var cap = Brush(BaseDark);
            g.FillRectangle(cap, barrel.Left, barrel.Top + 0.5, 4, barrel.Height - 1);
            g.FillRectangle(cap, barrel.Right - 4, barrel.Top + 0.5, 4, barrel.Height - 1);

            // 회전 몸체
            var turret = new Rect(-20, -18, 40, 36);
            var turretPath = RoundedRect(turret, 8);
            g.FillPath(Linear(new Rect(turret.X, turret.Y - 0.5, turret.Width, turret.Height + 1), TurretLight, TurretDark, GradientMode.Vertical), turretPath);
            g.DrawPath(Pen(Rgb(31, 37, 48), 1), turretPath);

            // 진공 호스
            g.DrawBezier(Pen(HoseColor, 2.2, roundCaps: true), new Point(10, 14), new Point(26, 20), new Point(forkRoot - 18, 18), new Point(forkRoot + 2, 9));

            g.Restore(state);
        }

        private void DrawCenterMarks(Gdi g, Point center)
        {
            g.DrawText("TM", 8, true, new Rect(Math.Floor(center.X - 16), Math.Floor(center.Y - 8), 32, 16), Rgb(233, 237, 243), TextAlignment.Center);
            if (carryingWafer)
            {
                const double dot = 4;
                var dotRect = new Rect(center.X + 13 - dot, center.Y - 13 - dot, dot * 2, dot * 2);
                g.FillEllipse(Brush(PadOn), dotRect);
                g.DrawEllipse(Pen(Colors.White, 1.2), dotRect);
            }
        }

        private double GetBladeLength()
        {
            double reach = RetractedLength + 120;
            if (anchors.TryGetValue(targetRegion, out var anchor))
            {
                reach = (anchor - hubCenter).Length;
            }
            var norm = (currentExtensionFactor - RetractedFactor) / (ExtendedFactor - RetractedFactor);
            norm = Math.Max(0, Math.Min(1, norm));
            return RetractedLength + norm * Math.Max(0, reach - RetractedLength);
        }

        private static Geometry CreateForkPath(double root, double tip)
        {
            const double halfWidth = 15, prong = 9, corner = 6;
            double slotStart = root + 16;
            var path = new GPath();
            path.AddLine(root, -halfWidth, tip - corner, -halfWidth);
            path.AddArc(tip - corner * 2, -halfWidth, corner * 2, corner * 2, 270, 90);
            path.AddLine(tip, -halfWidth + corner, tip, -halfWidth + prong);
            path.AddLine(tip, -halfWidth + prong, slotStart, -halfWidth + prong);
            path.AddLine(slotStart, halfWidth - prong, tip, halfWidth - prong);
            path.AddLine(tip, halfWidth - prong, tip, halfWidth - corner);
            path.AddArc(tip - corner * 2, halfWidth - corner * 2, corner * 2, corner * 2, 0, 90);
            path.AddLine(tip - corner, halfWidth, root, halfWidth);
            path.CloseFigure();
            return path.ToGeometry();
        }

        private static double AngleTo(Point from, Point to) => Math.Atan2(to.Y - from.Y, to.X - from.X);
        private static double ToDegrees(double radians) => radians * 180 / Math.PI;

        private double GetBladeAngle(EquipmentRegion region, bool isSimulation)
        {
            if (hasLayout && anchors.TryGetValue(region, out var anchor))
            {
                return Math.Atan2(anchor.Y - hubCenter.Y, anchor.X - hubCenter.X);
            }
            switch (region)
            {
                case EquipmentRegion.ChamberA: return Math.PI;
                case EquipmentRegion.ChamberB: return -Math.PI / 2;
                case EquipmentRegion.ChamberC: return 0;
                case EquipmentRegion.FoupA: return 3 * Math.PI / 4;
                case EquipmentRegion.FoupB: return Math.PI / 4;
                default:
                    // 시뮬레이션 모드: 아래 방향(90°)은 갈 수 없는 위치라 FOUP A 방향(135°)으로 대기
                    return isSimulation ? 3 * Math.PI / 4 : Math.PI / 2;
            }
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            // 틱 간격이 들쭉날쭉해도 속도가 같도록 실제 흐른 시간으로 움직인다.
            double now = animationClock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, Math.Max(0.0, now - lastAnimationSeconds));
            lastAnimationSeconds = now;

            const double tolerance = 0.002;
            double extensionRemaining = targetExtensionFactor - currentExtensionFactor;
            double angleRemaining = GetRotationPath(currentAngleRad, targetAngleRad);
            bool needsRotation = Math.Abs(angleRemaining) > tolerance;

            // 블레이드가 나와 있으면 돌기 전에 먼저 접고, 돌아야 하는 동안에는 내밀지 않는다.
            bool holdRotation = needsRotation && angularVelocity == 0
                && currentExtensionFactor > RotationSafeExtension
                && targetExtensionFactor <= RotationSafeExtension;
            bool holdExtension = needsRotation && extensionRemaining > 0;

            bool extensionMoving = false;
            if (holdExtension)
            {
                extensionVelocity = 0;
            }
            else
            {
                extensionMoving = Step(ref currentExtensionFactor, ref extensionVelocity, extensionRemaining,
                    MaxExtensionSpeed, ExtensionAcceleration, dt, tolerance);
            }

            bool rotating = false;
            if (!holdRotation)
            {
                double angle = currentAngleRad;
                rotating = Step(ref angle, ref angularVelocity, angleRemaining,
                    MaxAngularSpeed, AngularAcceleration, dt, tolerance);
                currentAngleRad = rotating ? NormalizeAngle(angle) : targetAngleRad;
            }

            if (!extensionMoving && !rotating && !holdRotation && !holdExtension)
            {
                animationTimer.Stop();
                animationClock.Stop();
            }
            InvalidateVisual();
        }

        /// <summary>가속 → 일정 속도 → 감속으로 남은 거리만큼 한 틱 움직인다. 끝났으면 false.</summary>
        internal static bool Step(ref double value, ref double velocity, double remaining, double maxSpeed, double acceleration, double dt, double tolerance)
        {
            if (Math.Abs(remaining) <= tolerance)
            {
                value += remaining;
                velocity = 0;
                return false;
            }

            double direction = Math.Sign(remaining);
            double brakeSpeed = Math.Sqrt(2.0 * acceleration * Math.Abs(remaining));
            double desired = direction * Math.Min(maxSpeed, brakeSpeed);
            double maxChange = acceleration * dt;
            velocity += Math.Max(-maxChange, Math.Min(maxChange, desired - velocity));

            double move = velocity * dt;
            if (Math.Sign(move) != (int)direction && move != 0)
            {
                // 목표가 반대쪽으로 바뀐 경우: 감속만 하고 기다린다
                value += move;
                return true;
            }
            if (Math.Abs(move) >= Math.Abs(remaining))
            {
                value += remaining;
                velocity = 0;
                return false;
            }
            value += move;
            return true;
        }

        /// <summary>
        /// 현재 각도에서 목표 각도까지 돌 양(부호 포함). 짧은 쪽이 6시 방향을 지나가면 반대쪽으로 돈다.
        /// </summary>
        internal static double GetRotationPath(double from, double to)
        {
            double shortest = NormalizeAngle(to - from);
            double toForbidden = NormalizeAngle(ForbiddenAngle - from);
            bool crosses = shortest > 0
                ? toForbidden > 0.0001 && toForbidden < shortest
                : toForbidden < -0.0001 && toForbidden > shortest;
            if (!crosses) return shortest;
            return shortest > 0 ? shortest - 2 * Math.PI : shortest + 2 * Math.PI;
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }
    }
}
