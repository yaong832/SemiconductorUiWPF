namespace SemiconductorUi.Wpf.Services
{
    /// <summary>뷰모델이 창/메시지 박스를 직접 만들지 않도록 하는 대화상자 추상화</summary>
    public interface IDialogService
    {
        void Info(string message, string title = "안내");
        void Warn(string message, string title = "경고");
        void Error(string message, string title = "오류");
        bool Confirm(string message, string title = "확인");

        /// <summary>예/아니오 확인. defaultNo면 '아니오'가 기본 버튼 (원본 MessageBoxDefaultButton.Button2).</summary>
        bool Confirm(string message, string title, bool defaultNo);

        /// <summary>로딩할 웨이퍼 수 입력 (원본 FoupManager.PromptForWaferCount). 취소하면 null.</summary>
        int? PromptWaferCount(int current, int min, int max);

        /// <summary>사용자 관리 창 (원본 UserManagementForm.ShowDialog)</summary>
        void ShowUserManagement(ViewModels.UserManagementViewModel model);

        /// <summary>로그인 창을 띄운다. 취소하면 false.</summary>
        bool TryLogin(out string username, out string password);

        /// <summary>
        /// 챔버/FOUP 상세 창을 띄운다 (원본 ShowUnitDetail). 메인 화면을 잠그지 않는 창 하나를 다시 쓰며,
        /// detail.IsOpen이 false가 되면 숨긴다. 이미 떠 있으면 자리는 그대로 둔다.
        /// </summary>
        void ShowUnitDetail(ViewModels.UnitDetailViewModel detail);

        /// <summary>설정 창 (원본 ConfigForm.ShowDialog)</summary>
        void ShowConfig(ViewModels.ConfigViewModel model);

        /// <summary>레시피 관리 창 (원본 RecipeManagerForm.ShowDialog)</summary>
        void ShowRecipeManager(ViewModels.RecipeManagerViewModel model);
    }
}
