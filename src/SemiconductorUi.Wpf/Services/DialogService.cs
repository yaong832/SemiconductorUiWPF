using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SemiconductorUi.Wpf.ViewModels;
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

        public void ShowUserManagement(UserManagementViewModel model)
        {
            var window = new UserManagementWindow(model) { Owner = Owner };
            window.ShowDialog();
        }

        public bool TryLogin(out string username, out string password)
        {
            var window = new LoginWindow { Owner = Owner };
            bool ok = window.ShowDialog() == true;
            username = ok ? window.EnteredUsername : null;
            password = ok ? window.EnteredPassword : null;
            return ok;
        }

        public void ShowConfig(ConfigViewModel model)
        {
            var window = new ConfigWindow(model) { Owner = Owner };
            window.ShowDialog();
        }

        public void ShowRecipeManager(RecipeManagerViewModel model)
        {
            var window = new RecipeManagerWindow(model) { Owner = Owner };
            window.ShowDialog();
        }

        public void ShowTeachingPositions(TeachingPositionViewModel model)
        {
            var window = new TeachingPositionWindow(model) { Owner = Owner };
            window.ShowDialog();
        }

        private IoMonitorWindow _ioMonitor;

        public void ShowIoMonitor(IoMonitorViewModel model)
        {
            var main = Application.Current?.MainWindow;
            if (_ioMonitor == null)
            {
                _ioMonitor = new IoMonitorWindow(model);
                if (main != null && main.IsLoaded)
                {
                    _ioMonitor.Owner = main;
                    main.Closing += (s, e) => _ioMonitor?.ForceClose();
                }
                else
                {
                    _ioMonitor.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            if (!_ioMonitor.IsVisible) _ioMonitor.Show();
            if (_ioMonitor.WindowState == WindowState.Minimized) _ioMonitor.WindowState = WindowState.Normal;
            _ioMonitor.Activate();
        }

        private MaintenanceCounterWindow _counters;

        public void ShowMaintenanceCounters(MaintenanceCounterViewModel model)
        {
            var main = Application.Current?.MainWindow;
            if (_counters == null)
            {
                _counters = new MaintenanceCounterWindow(model);
                if (main != null && main.IsLoaded)
                {
                    _counters.Owner = main;
                    main.Closing += (s, e) => _counters?.ForceClose();
                }
                else
                {
                    _counters.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            if (!_counters.IsVisible) _counters.Show();
            if (_counters.WindowState == WindowState.Minimized) _counters.WindowState = WindowState.Normal;
            _counters.Activate();
        }

        private UnitDetailWindow _unitDetail;

        public void ShowUnitDetail(UnitDetailViewModel detail)
        {
            var main = Application.Current?.MainWindow;
            if (_unitDetail == null)
            {
                _unitDetail = new UnitDetailWindow
                {
                    DataContext = detail,
                    ForbiddenArea = () => ScreenRectOf(main, "ControlPanel"),
                };
                if (main != null && main.IsLoaded)
                {
                    _unitDetail.Owner = main;
                    main.Closing += (s, e) => _unitDetail?.ForceClose();
                }
                detail.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(UnitDetailViewModel.IsOpen) && !detail.IsOpen) _unitDetail?.Hide();
                };
            }

            if (_unitDetail.IsVisible || !detail.IsOpen) return;

            // 처음 띄울 때만 기본 자리: 장비 그림 칸 위쪽, 오른쪽 제어 패널(긴급 정지 포함) 바로 왼쪽
            var panel = ScreenRectOf(main, "ControlPanel");
            var canvas = ScreenRectOf(main, "EquipmentCanvasHost");
            if (panel.HasValue && canvas.HasValue)
            {
                _unitDetail.Left = Math.Max(canvas.Value.Left + 8, panel.Value.Left - 8 - _unitDetail.Width);
                _unitDetail.Top = canvas.Value.Top + 8;
            }
            else
            {
                _unitDetail.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            _unitDetail.Show();
        }

        /// <summary>창 안에서 이름이 name인 요소의 화면 좌표(DIP)</summary>
        private static Rect? ScreenRectOf(Window window, string name)
        {
            if (window == null || !window.IsVisible) return null;
            var element = FindByName(window, name);
            var source = PresentationSource.FromVisual(window);
            if (element == null || !element.IsVisible || source?.CompositionTarget == null) return null;
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var topLeft = fromDevice.Transform(element.PointToScreen(new Point(0, 0)));
            var bottomRight = fromDevice.Transform(element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
            return new Rect(topLeft, bottomRight);
        }

        private static FrameworkElement FindByName(DependencyObject root, string name)
        {
            if (root is FrameworkElement fe && fe.Name == name) return fe;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindByName(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void Show(string message, string title, MessageBoxImage image)
        {
            var owner = Owner;
            if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
            else MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }
    }
}
