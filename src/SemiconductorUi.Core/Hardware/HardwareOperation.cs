using System;
using System.Threading.Tasks;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 백그라운드 장비 동작(서보 ON, 원점복귀 등)을 시작합니다.
    ///
    /// Task.Run 대신 이것으로 시작하면, 동작이 시작된 뒤 비상정지가 한 번이라도 눌렸을 때
    /// 그 동작은 비상정지가 해제된 뒤에도 더 이상 명령을 보내지 못합니다.
    /// 비상정지 중에는 시작하지 않고 EmergencyStopActiveException을 던집니다.
    /// </summary>
    public static class HardwareOperation
    {
        public static Task Run(IEthercatDevice device, Action work)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            // 범위 표시는 실행 컨텍스트에 담겨 Task.Run이 시작하는 작업으로 그대로 넘어갑니다.
            using (device.BeginOperation())
            {
                return Task.Run(work);
            }
        }
    }
}
