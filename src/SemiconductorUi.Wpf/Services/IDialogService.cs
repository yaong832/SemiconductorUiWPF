namespace SemiconductorUi.Wpf.Services
{
    /// <summary>뷰모델이 창/메시지 박스를 직접 만들지 않도록 하는 대화상자 추상화</summary>
    public interface IDialogService
    {
        void Info(string message, string title = "안내");
        void Warn(string message, string title = "경고");
        void Error(string message, string title = "오류");
        bool Confirm(string message, string title = "확인");

        /// <summary>로그인 창을 띄운다. 취소하면 false.</summary>
        bool TryLogin(out string username, out string password);
    }
}
