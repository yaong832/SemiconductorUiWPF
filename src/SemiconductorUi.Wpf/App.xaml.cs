using System.Windows;

namespace SemiconductorUi.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ExceptionHandler.ShowErrorMessage = msg =>
                MessageBox.Show(msg, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            base.OnStartup(e);
        }
    }
}
