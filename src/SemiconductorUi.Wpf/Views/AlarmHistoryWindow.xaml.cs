using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>
    /// 현재 알람과 알람 이력 창 (원본 Forms.AlarmHistoryForm). 모달이 아니라서 떠 있는 동안에도 메인 화면을 쓸 수 있다.
    /// X나 Esc로 닫으면 숨겨 두었다가 다시 쓴다.
    /// </summary>
    public partial class AlarmHistoryWindow : Window
    {
        private bool _allowClose;

        public AlarmHistoryWindow()
        {
            InitializeComponent();
        }

        private AlarmHistoryViewModel Model => DataContext as AlarmHistoryViewModel;

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
    }
}
