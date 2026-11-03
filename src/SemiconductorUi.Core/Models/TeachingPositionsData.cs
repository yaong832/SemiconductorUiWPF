using System;

namespace SemiconductorUi.Models
{
    /// <summary>
    /// TM 티칭 위치 저장용 DTO (TeachingPositions.xml)
    /// </summary>
    [Serializable]
    public class TeachingPositionsData
    {
        public long DescendOffset { get; set; } = 30000;

        public long ChamberA_X { get; set; } = -59064;
        public long ChamberA_LandY { get; set; } = 806931;
        public long ChamberA_RaiseY { get; set; } = 1156931;

        public long ChamberB_X { get; set; } = -190823;
        public long ChamberB_LandY { get; set; } = 806931;
        public long ChamberB_RaiseY { get; set; } = 1156931;

        public long ChamberC_X { get; set; } = -321600;
        public long ChamberC_LandY { get; set; } = 806931;
        public long ChamberC_RaiseY { get; set; } = 1156931;

        public long FoupA_X { get; set; } = 14140;
        public long FoupB_X { get; set; } = -394293;

        public long Home_X { get; set; } = 0;
        public long Home_Y { get; set; } = 0;

        public long[] FoupA_LandY { get; set; } = new long[] { 102379, 782378, 1432388, 2119399, 2818463 };
        public long[] FoupA_RaiseY { get; set; } = new long[] { 302380, 982378, 1627604, 2332102, 3018457 };
        public long[] FoupB_LandY { get; set; } = new long[] { 102379, 782378, 1432388, 2119399, 2818463 };
        public long[] FoupB_RaiseY { get; set; } = new long[] { 302380, 982378, 1627604, 2332102, 3018457 };

        public static TeachingPositionsData CreateDefault()
        {
            return new TeachingPositionsData();
        }
    }
}
