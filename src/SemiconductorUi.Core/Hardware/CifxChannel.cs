using System.Runtime.InteropServices;
using CIFX_50RE;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// Hilscher cifX 드라이버 상수 (cifXUser.h)
    /// </summary>
    public static class CifxConstants
    {
        public const uint BusStateOff = 0;
        public const uint BusStateOn = 1;
        public const uint BusStateGetState = 2;

        /// <summary>
        /// IEG3268 DLL이 입력 데이터를 읽을 때 쓰는 길이와 같게 맞춥니다.
        /// </summary>
        public const int IoReadLength = 100;

        public const uint IoTimeoutMs = 100;
    }

    /// <summary>
    /// cifX 채널에 직접 접근하는 저수준 창구. 테스트에서는 가짜 구현으로 바꿉니다.
    /// </summary>
    public interface ICifxChannel
    {
        /// <summary>
        /// 출력 버퍼 전체를 장치로 보냅니다.
        /// </summary>
        void Write(byte[] data);

        /// <summary>
        /// 버스 상태를 조회하고 드라이버 결과 코드를 반환합니다.
        /// </summary>
        uint GetBusState(out uint busState);

        /// <summary>
        /// 입력 데이터를 읽고 드라이버 결과 코드를 반환합니다.
        /// </summary>
        uint ReadIo(byte[] buffer);
    }

    /// <summary>
    /// IEG3268 DLL이 연 채널 핸들을 그대로 써서 cifx32dll.dll을 직접 호출합니다.
    /// 시그니처는 IEG3268 DLL 내부의 DllImport 선언과 같게 맞췄습니다.
    /// </summary>
    public sealed class CifxNativeChannel : ICifxChannel
    {
        private const string DriverDll = "cifx32dll.dll";

        [DllImport(DriverDll, EntryPoint = "xChannelBusState")]
        private static extern uint xChannelBusState(uint hChannel, uint ulCmd, ref uint pulState, uint ulTimeout);

        [DllImport(DriverDll, EntryPoint = "xChannelIORead")]
        private static extern uint xChannelIORead(uint hChannel, uint ulAreaNumber, uint ulOffset, uint ulDataLen, byte[] pvData, uint ulTimeout);

        public void Write(byte[] data)
        {
            CIFX.xChannelWrite(data);
        }

        public uint GetBusState(out uint busState)
        {
            uint state = 0;
            uint result = xChannelBusState(CIFX.hChannel, CifxConstants.BusStateGetState, ref state, CifxConstants.IoTimeoutMs);
            busState = state;
            return result;
        }

        public uint ReadIo(byte[] buffer)
        {
            return xChannelIORead(CIFX.hChannel, 0, 0, (uint)buffer.Length, buffer, CifxConstants.IoTimeoutMs);
        }
    }
}
