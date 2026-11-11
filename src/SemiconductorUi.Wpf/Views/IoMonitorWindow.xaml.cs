using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>
    /// I/O 모니터 별도 창 (원본 Forms.IoMonitorForm). 모달이 아니라 띄워 둔 채로 다른 화면을 쓸 수 있다.
    /// 보이는 동안만 갱신하고, X나 Esc로 닫으면 없애지 않고 숨긴다 (원본처럼).
    /// </summary>
    public partial class IoMonitorWindow : Window
    {
        private bool _allowClose;
        private bool _attached;

        public IoMonitorWindow(IoMonitorViewModel model)
        {
            InitializeComponent();
            DataContext = model;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && !_attached)
                {
                    _attached = true;
                    model.Attach();
                }
                else if (!IsVisible && _attached)
                {
                    _attached = false;
                    model.Detach();
                }
            };
        }

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
                Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }
    }
}
