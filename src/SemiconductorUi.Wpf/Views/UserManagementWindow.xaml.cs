using System.Windows;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>사용자 관리 창 (원본 Forms.UserManagementForm, 모달)</summary>
    public partial class UserManagementWindow : Window, IPasswordProvider
    {
        public UserManagementWindow(UserManagementViewModel model)
        {
            InitializeComponent();
            model.PasswordProvider = this;
            DataContext = model;
            model.CloseRequested += (s, e) => Close();
        }

        string IPasswordProvider.Password => PasswordInput.Password;

        void IPasswordProvider.Clear() => PasswordInput.Clear();
    }
}
