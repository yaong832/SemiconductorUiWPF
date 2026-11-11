using System;
using System.Windows;
using System.Windows.Threading;
using SemiconductorUi.Hardware;
using SemiconductorUi.Wpf.Services;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf
{
    public partial class App : Application
    {
        private MainViewModel _viewModel;

        protected override void OnExit(ExitEventArgs e)
        {
            // 알람 이력 파일을 닫는다 (원본 Program.cs DisposeAlarmHistory)
            _viewModel?.Engine.DisposeAlarmHistory();
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ExceptionHandler.ShowErrorMessage = msg =>
                MessageBox.Show(msg, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var dialogs = new DialogService();
            IEthercatDevice ethercat = new Ieg3268Device(new IEG3268_Dll.IEG3268());
            var viewModel = new MainViewModel(dialogs, new SemiconductorUi.Repositories.UserRepositoryImpl(), ethercat);

            _viewModel = viewModel;
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string message = ExceptionHandler.HandleException(e.Exception, "처리되지 않은 예외");
            MessageBox.Show(message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
