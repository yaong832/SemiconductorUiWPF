namespace SemiconductorUi.Models
{
    /// <summary>
    /// 웨이퍼 정보를 나타내는 클래스
    /// </summary>
    public class Wafer
    {
        /// <summary>
        /// 웨이퍼 ID
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// 현재 단계
        /// </summary>
        public string CurrentStage { get; set; }

        /// <summary>
        /// 2차 노광 필요 여부
        /// </summary>
        public bool RequiresSecondExposure { get; set; }

        /// <summary>
        /// 2차 노광 완료 여부
        /// </summary>
        public bool SecondExposureCompleted { get; set; }

        /// <summary>
        /// Wafer 생성
        /// </summary>
        /// <param name="id">웨이퍼 ID</param>
        /// <param name="requiresSecondExposure">2차 노광 필요 여부</param>
        public Wafer(int id, bool requiresSecondExposure)
        {
            Id = id;
            RequiresSecondExposure = requiresSecondExposure;
            CurrentStage = "FOUP A";
        }
    }
}

