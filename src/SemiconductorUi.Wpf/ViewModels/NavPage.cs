namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>왼쪽 세로 메뉴 (원본 buttonNav*)</summary>
    public enum NavPage
    {
        Operate,
        Recipe,
        Maintenance,
        Config,
        Trend,
        Report,
        System
    }

    /// <summary>운전 화면 위 작은 탭 (원본 buttonTabMain/Verification/Transfer)</summary>
    public enum OperateTab
    {
        Monitoring,
        Verification,
        Transfer
    }

    public enum EthercatLinkState
    {
        Disconnected,
        Connected,
        ConnectionFailed,
        Error
    }
}
