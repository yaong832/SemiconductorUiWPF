using System;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// TM 회전과 실린더 전진이 겹칠 수 있어 장비 명령을 보내지 않았을 때 발생합니다.
    /// 명령은 장비로 나가지 않았으므로 출력과 축 상태는 그대로입니다.
    /// </summary>
    public sealed class HardwareInterlockException : InvalidOperationException
    {
        public HardwareInterlockException(string message)
            : base(message)
        {
        }
    }
}
