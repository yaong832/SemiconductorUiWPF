namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// 챔버 StatusText 상수. 문자열 불일치로 공정 시작/램프가 스킵되는 것을 방지한다.
    /// </summary>
    public static class ChamberStatusTexts
    {
        public const string Idle = "대기";
        public const string Processing = "처리 중";
        public const string SecondExposure = "2차 노광 중";
        public const string DoorClosing = "Door Closing";
        public const string DoorOpenWaiting = "Door Open 대기";
        public const string TmWaiting = "TM 대기";
        public const string Completed = "Completed";

        /// <summary>실제 공정 진행 중(램프 ON / 시간 감소 대상)</summary>
        public static bool IsProcessing(string statusText)
        {
            if (string.IsNullOrEmpty(statusText))
            {
                return false;
            }

            return statusText == Processing
                || statusText == SecondExposure
                || statusText.Contains(Processing)
                || statusText.Contains(SecondExposure);
        }

        /// <summary>도어 닫힘 후 공정 시작을 기다려야 하는 상태</summary>
        public static bool IsAwaitingProcessStart(string statusText)
        {
            return statusText == DoorClosing;
        }
    }
}
