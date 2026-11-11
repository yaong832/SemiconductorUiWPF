using System;
using System.Linq;
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
            // 날짜별 로그 파일을 닫는다. 종료 직전 로그까지 이미 파일에 쓰여 있다 (원본 Program.cs).
            var logger = _viewModel?.Logger;
            if (logger != null)
            {
                logger.Log("프로그램 종료", "INFO");
                logger.Dispose();
            }
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ExceptionHandler.ShowErrorMessage = msg =>
                MessageBox.Show(msg, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 설정 파일을 읽기 전에: 예약된 설정 복원, 하루 한 번 자동 백업 (원본 Program.Main)
            var startupNotes = SemiconductorUi.Services.SettingsBackup.RunStartupTasks(DateTime.Now, out bool settingsRestored);
            var restoreNotes = startupNotes.Where(n => n.AboutRestore).Select(n => n.Message).ToList();
            if (restoreNotes.Count > 0)
            {
                MessageBox.Show(string.Join("\n\n", restoreNotes), "설정 복원", MessageBoxButton.OK,
                    settingsRestored ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }

            var dialogs = new DialogService();
            IEthercatDevice ethercat = new Ieg3268Device(new IEG3268_Dll.IEG3268());
            var viewModel = new MainViewModel(dialogs, new SemiconductorUi.Repositories.UserRepositoryImpl(), ethercat);

            _viewModel = viewModel;
            foreach (var note in startupNotes) viewModel.AddLog(note.Message, note.Level);
            if (viewModel.Logger.FileLoggingStartMessage != null)
                viewModel.AddLog(viewModel.Logger.FileLoggingStartMessage, "INFO");

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
