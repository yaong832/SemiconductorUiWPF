using System;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 비상정지 중이라 장비 명령을 보내지 않았을 때 발생합니다.
    /// 비상정지 이전에 시작된 동작이 비상정지 해제 뒤에 이어서 명령을 보내려 할 때도 발생합니다.
    /// </summary>
    public sealed class EmergencyStopActiveException : InvalidOperationException
    {
        public EmergencyStopActiveException(string message)
            : base(message)
        {
        }
    }
}
