using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>
    /// 유지보수 카운터 별도 창 (원본 Forms.MaintenanceCounterForm). 모달이 아니며 보이는 동안만 갱신한다.
    /// Esc는 묻는 중이면 질문을 취소하고, 아니면 창을 숨긴다. X로 닫아도 숨기기만 한다.
    /// </summary>
    public partial class MaintenanceCounterWindow : Window
    {
        private readonly MaintenanceCounterViewModel _model;
        private bool _allowClose;
        private bool _attached;

        public MaintenanceCounterWindow(MaintenanceCounterViewModel model)
        {
            InitializeComponent();
            _model = model;
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
                if (!_model.CancelIfConfirming()) Hide();
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
