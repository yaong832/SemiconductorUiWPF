using System.Linq;
using System.Windows;
using SemiconductorUi.Wpf.Views;

namespace SemiconductorUi.Wpf.Services
{
    public sealed class DialogService : IDialogService
    {
        private static Window Owner =>
            Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;

        public void Info(string message, string title = "안내") => Show(message, title, MessageBoxImage.Information);
        public void Warn(string message, string title = "경고") => Show(message, title, MessageBoxImage.Warning);
        public void Error(string message, string title = "오류") => Show(message, title, MessageBoxImage.Error);

        public bool Confirm(string message, string title = "확인") => Confirm(message, title, false);

        public bool Confirm(string message, string title, bool defaultNo)
        {
            var owner = Owner;
            var image = defaultNo ? MessageBoxImage.Warning : MessageBoxImage.Question;
            var def = defaultNo ? MessageBoxResult.No : MessageBoxResult.Yes;
            var result = owner != null
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, image, def)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, image, def);
            return result == MessageBoxResult.Yes;
        }

        public int? PromptWaferCount(int current, int min, int max)
        {
            var window = new WaferCountWindow(current, min, max) { Owner = Owner };
            return window.ShowDialog() == true ? window.Value : (int?)null;
        }

        public bool TryLogin(out string username, out string password)
        {
            var window = new LoginWindow { Owner = Owner };
            bool ok = window.ShowDialog() == true;
            username = ok ? window.EnteredUsername : null;
            password = ok ? window.EnteredPassword : null;
            return ok;
        }

        private static void Show(string message, string title, MessageBoxImage image)
        {
            var owner = Owner;
            if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
            else MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }
    }
}
