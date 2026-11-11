using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>
    /// 설정 백업·복원 창 (원본 Forms.SettingsBackupForm). 모달이 아니며 보일 때마다 목록을 새로 읽는다.
    /// Esc는 묻는 중이면 질문을 취소하고, 아니면 창을 숨긴다. X로 닫아도 숨기기만 한다.
    /// </summary>
    public partial class SettingsBackupWindow : Window
    {
        private readonly SettingsBackupViewModel _model;
        private bool _allowClose;

        public SettingsBackupWindow(SettingsBackupViewModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) model.OnShown();
                else model.OnHidden();
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
