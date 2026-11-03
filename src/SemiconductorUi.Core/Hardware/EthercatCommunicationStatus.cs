namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// EtherCAT 통신 상태 확인 결과
    /// </summary>
    public sealed class EthercatCommunicationStatus
    {
        /// <summary>
        /// cifX 드라이버 성공 코드 (CIFX_NO_ERROR)
        /// </summary>
        public const uint NoError = 0;

        public EthercatCommunicationStatus(bool isConnected, uint busStateError, uint busState, uint ioReadError)
        {
            IsConnected = isConnected;
            BusStateError = busStateError;
            BusState = busState;
            IoReadError = ioReadError;
        }

        /// <summary>
        /// 연결 시도에 성공한 상태인지 여부
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// 버스 상태 조회 결과 코드 (0 = 정상)
        /// </summary>
        public uint BusStateError { get; }

        /// <summary>
        /// 버스 상태 (0 = OFF, 1 = ON)
        /// </summary>
        public uint BusState { get; }

        /// <summary>
        /// 입력 데이터 읽기 결과 코드 (0 = 정상)
        /// </summary>
        public uint IoReadError { get; }

        /// <summary>
        /// 버스가 켜져 있고 입력 데이터를 정상으로 읽을 수 있는 상태
        /// </summary>
        public bool IsHealthy =>
            IsConnected &&
            BusStateError == NoError &&
            BusState == CifxConstants.BusStateOn &&
            IoReadError == NoError;

        public static EthercatCommunicationStatus NotConnected()
        {
            return new EthercatCommunicationStatus(false, NoError, CifxConstants.BusStateOff, NoError);
        }

        public override string ToString()
        {
            if (!IsConnected)
            {
                return "연결 안 됨";
            }

            return $"버스={(BusState == CifxConstants.BusStateOn ? "ON" : "OFF")}, " +
                   $"버스조회=0x{BusStateError:X8}, 입력읽기=0x{IoReadError:X8}";
        }
    }
}
