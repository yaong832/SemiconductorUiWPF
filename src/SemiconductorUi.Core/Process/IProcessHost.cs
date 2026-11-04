using System;

namespace SemiconductorUi.Process
{
    /// <summary>원본 MessageBoxIcon 중 공정 코드가 쓰는 것</summary>
    public enum MessageKind
    {
        Information,
        Warning,
        Error,
    }

    /// <summary>
    /// 공정 엔진이 화면에 바라는 것. 원본 Form1의 UI 호출(MessageBox, BeginInvoke, 화면 갱신)을 이 인터페이스로 모았다.
    /// </summary>
    public interface IProcessHost
    {
        /// <summary>로그 한 줄 (원본 AddLogMessage). 어느 스레드에서 불려도 된다.</summary>
        void AddLog(string message, string level);

        /// <summary>안내/경고 창 (원본 MessageBox.Show(text, title, OK, icon))</summary>
        void ShowMessage(string text, string title, MessageKind kind);

        /// <summary>예/아니오 확인 창 (원본 MessageBox YesNo). defaultNo면 '아니오'가 기본 버튼.</summary>
        bool Confirm(string text, string title, bool defaultNo = false);

        /// <summary>로딩할 웨이퍼 수 입력 (원본 FoupManager.PromptForWaferCount). 취소하면 null.</summary>
        int? PromptWaferCount(int current, int min, int max);

        /// <summary>헤더(상단 바)에 알람이 떠 있는지 (원본 Form1.HasHeaderAlarm)</summary>
        bool HasHeaderAlarm();

        /// <summary>헤더 알람 표시를 지운다 (원본 Form1.SetHeaderAlarmIdle)</summary>
        void ClearHeaderAlarm();

        /// <summary>UI 스레드에서 비동기로 실행 (원본 SafeBeginInvoke)</summary>
        void BeginInvoke(Action action);

        /// <summary>지금 UI 스레드인지 (원본 !InvokeRequired)</summary>
        bool CheckAccess();

        /// <summary>UI 스레드에서 동기로 실행 (원본 this.Invoke)</summary>
        void Invoke(Action action);

        /// <summary>delayMs 뒤 UI 스레드에서 한 번 실행 (원본 RunOnceAfter)</summary>
        void RunOnceAfter(int delayMs, Action action);

        /// <summary>
        /// 공정 상태, 챔버/TM/FOUP 표시가 바뀌었다 (원본 UpdateSimulationUi, UpdateTmVisualization, 램프 갱신 등).
        /// 화면은 엔진 상태를 다시 읽어 그린다.
        /// </summary>
        void RefreshUi();

        /// <summary>서보/원점 상태가 바뀌었다 (원본 UpdateServoStatusLabel, UpdateProcessControlButtons)</summary>
        void RefreshServoStatus();
    }
}
