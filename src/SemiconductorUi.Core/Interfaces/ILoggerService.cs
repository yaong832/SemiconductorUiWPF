namespace SemiconductorUi
{
    /// <summary>
    /// 로깅 서비스 인터페이스
    /// </summary>
    public interface ILoggerService
    {
        /// <summary>
        /// 정보 로그 기록
        /// </summary>
        void Info(string message);

        /// <summary>
        /// 경고 로그 기록
        /// </summary>
        void Warn(string message);

        /// <summary>
        /// 오류 로그 기록
        /// </summary>
        void Error(string message);

        /// <summary>
        /// 알람 로그 기록
        /// </summary>
        void Alarm(string message);

        /// <summary>
        /// 치명적 오류 로그 기록
        /// </summary>
        void Critical(string message);

        /// <summary>
        /// 로그 기록 (레벨 지정)
        /// </summary>
        void Log(string message, string level);

        /// <summary>
        /// 로그 엔트리 목록 가져오기 (UI 표시용)
        /// </summary>
        System.Collections.Generic.List<string> GetLogEntries();

        /// <summary>
        /// 로그 초기화
        /// </summary>
        void Clear();
    }
}

