namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 비상정지 중에도 보낼 수 있는 디지털 출력.
    ///
    /// 비상정지 중에는 장비를 움직이는 출력(도어, 실린더)과 웨이퍼를 놓치게 하는 출력(진공 OFF, 배기 ON)을 막습니다.
    /// 램프는 상태 표시용이라 허용하고, 진공 ON과 배기 OFF는 웨이퍼를 계속 잡고 있는 방향이라 허용합니다.
    /// </summary>
    public static class EmergencyStopPolicy
    {
        public const int MainLampRedOutput = 0;
        public const int MainLampYellowOutput = 1;
        public const int MainLampGreenOutput = 2;
        public const int ChamberALampOutput = 3;
        public const int ChamberBLampOutput = 6;
        public const int ChamberCLampOutput = 9;
        public const int VacuumOutput = 14;
        public const int ExhaustOutput = 15;

        public static bool IsOutputAllowedWhileStopped(int numbering, bool value)
        {
            switch (numbering)
            {
                case MainLampRedOutput:
                case MainLampYellowOutput:
                case MainLampGreenOutput:
                case ChamberALampOutput:
                case ChamberBLampOutput:
                case ChamberCLampOutput:
                    return true;
                case VacuumOutput:
                    return value;
                case ExhaustOutput:
                    return !value;
                default:
                    return false;
            }
        }
    }
}
