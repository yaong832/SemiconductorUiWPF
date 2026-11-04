using System;
using System.Windows;
using System.Windows.Media;
using SemiconductorUi.Wpf.Rendering;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>원본 ShellTowerLamp: 상단 바 오른쪽 끝의 3색 신호탑 (22x44)</summary>
    public sealed class TowerLamp : FrameworkElement
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            nameof(Scene), typeof(EquipmentScene), typeof(TowerLamp), new PropertyMetadata(null, OnSceneChanged));

        public TowerLamp()
        {
            Width = 22;
            Height = 44;
        }

        public EquipmentScene Scene
        {
            get => (EquipmentScene)GetValue(SceneProperty);
            set => SetValue(SceneProperty, value);
        }

        private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var lamp = (TowerLamp)d;
            if (e.OldValue is EquipmentScene oldScene) oldScene.Changed -= lamp.OnSceneUpdated;
            if (e.NewValue is EquipmentScene newScene) newScene.Changed += lamp.OnSceneUpdated;
            lamp.InvalidateVisual();
        }

        private void OnSceneUpdated(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(InvalidateVisual)); return; }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var s = Scene;
            var g = new Gdi(dc, this);
            ChamberPainter.DrawSignalTower(g, new Rect(4, 1, RenderSize.Width - 8, RenderSize.Height - 2),
                s?.LampRed ?? Palette.LampOff,
                s?.LampYellow ?? Palette.LampOff,
                s?.LampGreen ?? Palette.LampOff);
            g.End();
        }
    }
}
