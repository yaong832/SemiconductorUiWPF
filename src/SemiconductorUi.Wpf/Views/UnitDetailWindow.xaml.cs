using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>
    /// 챔버/FOUP 상세 창 (원본 Forms.UnitDetailForm). 모달이 아니라서 떠 있는 동안에도
    /// 긴급 정지, 일시 정지 같은 버튼을 그대로 누를 수 있다. 창을 띄워도 메인 화면의 입력 초점을 빼앗지 않는다.
    /// X나 Esc로 닫으면 없애지 않고 숨겨 두었다가 다시 쓴다.
    /// </summary>
    public partial class UnitDetailWindow : Window
    {
        private const int WM_EXITSIZEMOVE = 0x0232;
        private bool _allowClose;

        public UnitDetailWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
        }

        /// <summary>창이 올라가면 안 되는 자리(제어 패널, 화면 DIP 좌표). 없으면 null</summary>
        public Func<Rect?> ForbiddenArea { get; set; }

        private UnitDetailViewModel Model => DataContext as UnitDetailViewModel;

        /// <summary>메인 화면이 닫힐 때는 숨기지 않고 정말 닫는다.</summary>
        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Model?.Close();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Model?.Close();
                return;
            }
            base.OnClosing(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 사용자가 창을 옮기다 놓았을 때 제어 패널 위에 걸쳐 있으면 왼쪽으로 비켜 놓는다 (원본 KeepUnitDetailOffControlPanel).
            if (msg == WM_EXITSIZEMOVE)
            {
                KeepOffForbiddenArea();
            }
            return IntPtr.Zero;
        }

        public void KeepOffForbiddenArea()
        {
            var area = ForbiddenArea?.Invoke();
            if (area == null || !IsVisible) return;
            var bounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            if (!bounds.IntersectsWith(area.Value)) return;
            Left = area.Value.Left - 8 - bounds.Width;
        }
    }
}
