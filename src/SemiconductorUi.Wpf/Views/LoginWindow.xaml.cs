using System.Windows;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>원본 Forms/LoginForm 대응</summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        public string EnteredUsername => UsernameBox.Text.Trim();

        // PasswordBox는 보안상 바인딩을 지원하지 않으므로 코드비하인드에서 읽는다.
        public string EnteredPassword => PasswordBox.Password;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EnteredUsername) || string.IsNullOrEmpty(EnteredPassword))
            {
                ErrorText.Text = "아이디와 비밀번호를 입력";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        }
    }
}
