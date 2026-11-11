using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SemiconductorUi.Controls;
using SemiconductorUi.Wpf.Rendering;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>
    /// 원본 panelEquipmentCanvas + Form1Configurator.LayoutCentralEquipment.
    /// 배치는 확대 전 좌표(캔버스 크기 / 배율)로 계산하고, 각 자식은 그 좌표로 그린 뒤 배율만큼 키운다.
    /// </summary>
    public sealed class EquipmentCanvas : FrameworkElement
    {
        private const double DesignCanvasWidth = 1000, DesignCanvasHeight = 560, MaxCanvasScale = 1.8;
        private const double SideViewWidth = 262, SideViewGap = 12, MinSideViewCanvasWidth = 880;
        private const double LampWidth = 262, LampHeight = 34;

        private readonly VisualCollection _children;
        private readonly TmTopView _tm = new TmTopView();
        private readonly TmSideView _side = new TmSideView();
        private readonly LampLegend _lamp = new LampLegend();
        private readonly ChamberCard _chamberA = new ChamberCard { Title = "Chamber A", ValveSide = ValveSide.Right };
        private readonly ChamberCard _chamberB = new ChamberCard { Title = "Chamber B", ValveSide = ValveSide.Bottom };
        private readonly ChamberCard _chamberC = new ChamberCard { Title = "Chamber C", ValveSide = ValveSide.Left };
        private readonly FoupView _foupA = new FoupView { Title = "FOUP A", Cursor = Cursors.Hand };
        private readonly FoupView _foupB = new FoupView { Title = "FOUP B", Cursor = Cursors.Hand };

        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            nameof(Scene), typeof(EquipmentScene), typeof(EquipmentCanvas), new PropertyMetadata(null, OnSceneChanged));

        public static readonly RoutedEvent RegionClickedEvent = EventManager.RegisterRoutedEvent(
            nameof(RegionClicked), RoutingStrategy.Bubble, typeof(EventHandler<RegionClickedEventArgs>), typeof(EquipmentCanvas));

        public static readonly RoutedEvent RegionRightClickedEvent = EventManager.RegisterRoutedEvent(
            nameof(RegionRightClicked), RoutingStrategy.Bubble, typeof(EventHandler<RegionClickedEventArgs>), typeof(EquipmentCanvas));

        public EquipmentCanvas()
        {
            _children = new VisualCollection(this)
            {
                _tm, _chamberA, _chamberB, _chamberC, _foupA, _foupB, _lamp, _side
            };
            Hook(_chamberA, EquipmentRegion.ChamberA);
            Hook(_chamberB, EquipmentRegion.ChamberB);
            Hook(_chamberC, EquipmentRegion.ChamberC);
            Hook(_foupA, EquipmentRegion.FoupA);
            Hook(_foupB, EquipmentRegion.FoupB);
        }

        public EquipmentScene Scene
        {
            get => (EquipmentScene)GetValue(SceneProperty);
            set => SetValue(SceneProperty, value);
        }

        /// <summary>챔버/FOUP 클릭 (원본: 상세 화면 열기)</summary>
        public event EventHandler<RegionClickedEventArgs> RegionClicked
        {
            add => AddHandler(RegionClickedEvent, value);
            remove => RemoveHandler(RegionClickedEvent, value);
        }

        /// <summary>챔버/FOUP 오른쪽 클릭 (원본: 시뮬레이션 시험 알람 메뉴)</summary>
        public event EventHandler<RegionClickedEventArgs> RegionRightClicked
        {
            add => AddHandler(RegionRightClickedEvent, value);
            remove => RemoveHandler(RegionRightClickedEvent, value);
        }

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];

        private void Hook(UIElement element, EquipmentRegion region)
        {
            element.MouseLeftButtonUp += (s, e) => RaiseEvent(new RegionClickedEventArgs(RegionClickedEvent, this, region));
            element.MouseRightButtonUp += (s, e) => RaiseEvent(new RegionClickedEventArgs(RegionRightClickedEvent, this, region));
        }

        private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (EquipmentCanvas)d;
            if (e.OldValue is EquipmentScene oldScene) oldScene.Changed -= canvas.OnSceneUpdated;
            if (e.NewValue is EquipmentScene newScene) newScene.Changed += canvas.OnSceneUpdated;
            canvas.ApplyScene();
        }

        private void OnSceneUpdated(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ApplyScene)); return; }
            ApplyScene();
        }

        private void ApplyScene()
        {
            var s = Scene;
            if (s == null) return;

            ApplyChamber(_chamberA, s.Chambers[EquipmentRegion.ChamberA]);
            ApplyChamber(_chamberB, s.Chambers[EquipmentRegion.ChamberB]);
            ApplyChamber(_chamberC, s.Chambers[EquipmentRegion.ChamberC]);

            ApplyFoup(_foupA, s.FoupA);
            ApplyFoup(_foupB, s.FoupB);

            _lamp.Red = s.LampRed;
            _lamp.Yellow = s.LampYellow;
            _lamp.Green = s.LampGreen;
            _lamp.InvalidateVisual();

            if (!s.IsSimulation && s.TmEncoderX.HasValue && s.TmTeachX != null)
                _tm.UpdateTmStateFromEncoder(s.TmTarget, s.TmEncoderX.Value, s.TmTeachX, s.TmCarrying, s.TmExtension, s.TmWaferColor);
            else
                _tm.UpdateTmState(s.TmTarget, s.TmCarrying, s.TmExtension, s.TmWaferColor, s.IsSimulation);
            _side.SetPositions(s.TeachingPositions);
            _side.UpdateState(s.SideView);
        }

        private static void ApplyChamber(ChamberCard card, EquipmentScene.Chamber c)
        {
            card.Title = c.Title;
            card.LampColor = c.LampColor;
            card.ValveOpen = c.ValveOpen;
            card.HasWafer = c.HasWafer;
            card.WaferColor = c.WaferColor;
            card.InvalidateVisual();
        }

        private static void ApplyFoup(FoupView view, EquipmentScene.Foup f)
        {
            view.WaferCount = f.WaferCount;
            view.Capacity = f.Capacity;
            view.Mounted = f.Mounted;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? DesignCanvasWidth : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? DesignCanvasHeight : availableSize.Height;
            return new Size(w, h);
        }

        /// <summary>원본 Form1Configurator.GetCanvasScale</summary>
        private static double GetCanvasScale(Size size)
        {
            double k = Math.Min(size.Width / DesignCanvasWidth, size.Height / DesignCanvasHeight);
            k = Math.Max(1, Math.Min(MaxCanvasScale, k));
            return Math.Floor(k * 20) / 20;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (finalSize.Width <= 0 || finalSize.Height <= 0) return finalSize;

            double k = GetCanvasScale(finalSize);
            var canvas = new Size(Math.Floor(finalSize.Width / k), Math.Floor(finalSize.Height / k));

            // 배치: Chamber B는 위, A/C는 좌우, FOUP A/B는 아래 양쪽. TM은 그 사이 중앙.
            const double margin = 14, tmClearance = 70;
            double chamberW = Palette.ChamberWidth, chamberH = Palette.ChamberHeight;
            double foupW = Palette.FoupWidth, foupH = Palette.FoupHeight;

            bool showSideView = canvas.Width >= MinSideViewCanvasWidth;
            double clusterWidth = showSideView ? canvas.Width - SideViewWidth - SideViewGap - margin : canvas.Width;

            double cx = clusterWidth / 2;
            double topY = margin;
            double foupY = Math.Max(topY + chamberH + 2 * tmClearance, canvas.Height - margin - foupH);
            double cy = (topY + chamberH + foupY) / 2;

            double sideDistance = Math.Max(tmClearance + chamberW / 2, (cy - topY) * 1.25);
            double maxSideDistance = cx - margin - chamberW / 2;
            double foupOffset = Math.Max(foupW / 2 + 14, Math.Min(sideDistance * 0.62, foupW / 2 + 60));
            if (cy + chamberH / 2 + 10 > foupY)
            {
                sideDistance = Math.Max(sideDistance, foupOffset + foupW / 2 + 16 + chamberW / 2);
            }
            sideDistance = Math.Min(sideDistance, Math.Max(tmClearance + chamberW / 2, maxSideDistance));

            var rB = Centered(new Point(cx, topY + chamberH / 2), chamberW, chamberH, canvas);
            var rA = Centered(new Point(cx - sideDistance, cy), chamberW, chamberH, canvas);
            var rC = Centered(new Point(cx + sideDistance, cy), chamberW, chamberH, canvas);
            var rFa = Centered(new Point(cx - foupOffset, foupY + foupH / 2), foupW, foupH, canvas);
            var rFb = Centered(new Point(cx + foupOffset, foupY + foupH / 2), foupW, foupH, canvas);

            Place(_chamberB, rB, k);
            Place(_chamberA, rA, k);
            Place(_chamberC, rC, k);
            Place(_foupA, rFa, k);
            Place(_foupB, rFb, k);

            var tmCenter = new Point(cx, cy);
            _foupA.FacingDegrees = Facing(rFa, tmCenter);
            _foupB.FacingDegrees = Facing(rFb, tmCenter);

            double sideViewLeft = canvas.Width - margin - SideViewWidth;
            var lampRect = showSideView ? new Rect(sideViewLeft, margin, LampWidth, LampHeight) : new Rect(margin, margin, LampWidth, LampHeight);
            Place(_lamp, lampRect, k);

            if (showSideView)
            {
                double sideTop = margin + LampHeight + 8;
                _side.Visibility = Visibility.Visible;
                Place(_side, new Rect(sideViewLeft, sideTop, SideViewWidth, Math.Max(120, canvas.Height - margin - sideTop)), k);
            }
            else
            {
                _side.Visibility = Visibility.Collapsed;
                _side.Arrange(new Rect(0, 0, 0, 0));
            }

            Place(_tm, new Rect(0, 0, canvas.Width, canvas.Height), k);
            var anchors = new Dictionary<EquipmentRegion, Point>
            {
                [EquipmentRegion.ChamberA] = WellCenter(rA),
                [EquipmentRegion.ChamberB] = WellCenter(rB),
                [EquipmentRegion.ChamberC] = WellCenter(rC),
                [EquipmentRegion.FoupA] = Gdi.Center(rFa),
                [EquipmentRegion.FoupB] = Gdi.Center(rFb),
            };
            _tm.SetLayout(tmCenter, anchors, new[] { rA, rB, rC, rFa, rFb });

            return finalSize;
        }

        private static Rect Centered(Point center, double w, double h, Size canvas)
        {
            double x = Math.Round(center.X - w / 2), y = Math.Round(center.Y - h / 2);
            x = Math.Max(0, Math.Min(x, canvas.Width - w));
            y = Math.Max(0, Math.Min(y, canvas.Height - h));
            return new Rect(x, y, w, h);
        }

        private static Point WellCenter(Rect chamber)
        {
            var well = ChamberPainter.GetWellBounds(new Rect(0, 0, chamber.Width, chamber.Height));
            return new Point(chamber.Left + well.X + well.Width / 2, chamber.Top + well.Y + well.Height / 2);
        }

        private static double Facing(Rect host, Point tmCenter)
        {
            var c = Gdi.Center(host);
            return Math.Atan2(tmCenter.Y - c.Y, tmCenter.X - c.X) * 180.0 / Math.PI;
        }

        /// <summary>논리 좌표로 그리고 배율 k만큼 키워서 배치</summary>
        private static void Place(FrameworkElement child, Rect logical, double k)
        {
            child.Measure(logical.Size);
            child.RenderTransform = k == 1 ? Transform.Identity : new ScaleTransform(k, k);
            child.Arrange(new Rect(logical.X * k, logical.Y * k, logical.Width, logical.Height));
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Gdi.Brush(Palette.CanvasBack), null, new Rect(RenderSize));
        }
    }

    public sealed class RegionClickedEventArgs : RoutedEventArgs
    {
        public RegionClickedEventArgs(RoutedEvent e, object source, EquipmentRegion region) : base(e, source)
        {
            Region = region;
        }

        public EquipmentRegion Region { get; }
    }
}
