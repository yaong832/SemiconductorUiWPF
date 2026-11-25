using System;
using System.ComponentModel;
using System.Windows;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 원본 9252123: 사용자가 창을 닫을 때(X 버튼, Alt+F4) 공정 중이면 닫지 않는다.
            // Windows 종료·로그오프로 닫힐 때는 막지 않는다 (원본 CloseReason.UserClosing만 막음).
            if (!App.IsSessionEnding && DataContext is MainViewModel vm && !vm.ConfirmWindowClose())
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            (DataContext as IDisposable)?.Dispose();
            base.OnClosed(e);
        }
    }
}
