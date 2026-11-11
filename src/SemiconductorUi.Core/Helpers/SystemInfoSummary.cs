using System.Text;

namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// 원본 Form1.ShowSystemInfoSummary의 "System Information" 글자 (항목 이름·순서 그대로).
    /// 원본은 메시지 상자로 보였고, WPF는 시스템 화면의 "요약 복사"와 화면 맨 위 요약에 쓴다.
    /// </summary>
    public static class SystemInfoSummary
    {
        public static string Build(string appVersion, string osVersion, string dotnetVersion,
            bool ethercatConnected, bool isLoggedIn, string currentUser, string processState)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"App Version: {appVersion}");
            sb.AppendLine($"OS: {osVersion}");
            sb.AppendLine($".NET: {dotnetVersion}");
            sb.AppendLine($"EtherCAT: {(ethercatConnected ? "Connected" : "Disconnected")}");
            sb.AppendLine($"Login: {(isLoggedIn ? currentUser : "Guest")}");
            sb.AppendLine($"Process: {processState}");
            return sb.ToString();
        }
    }
}
