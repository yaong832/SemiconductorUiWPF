using System;
using System.Threading;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 서보 ON/OFF 절차. 원본 HardwareEventHandlers.ButtonServoOn_Click의 하드웨어 부분을
    /// UI와 분리해 옮긴 것이다 (메시지 박스/라벨 갱신은 표현 계층이 담당).
    /// </summary>
    public static class ServoPowerService
    {
        /// <summary>
        /// 서보를 OFF → 파라미터 설정 → ON 한 뒤, 두 축의 위치 값이 읽히는지로 ON을 확인한다.
        /// 백그라운드 스레드에서 호출해야 한다(내부에서 Sleep 사용).
        /// </summary>
        /// <returns>위치 값이 확인되면 true, 제한 시간 안에 확인하지 못하면 false</returns>
        public static bool TurnOnAndConfirm(IEthercatDevice ethercat, TimeSpan? confirmTimeout = null)
        {
            if (ethercat == null) throw new ArgumentNullException(nameof(ethercat));

            long velocity = AppSettings.ServoDefaultVelocity;
            long maxVelocity = AppSettings.ServoDefaultMaxVelocity;
            long deceleration = AppSettings.ServoDefaultDeceleration;
            long acceleration = AppSettings.ServoDefaultAcceleration;

            ethercat.Axis1_OFF();
            ethercat.Axis2_OFF();
            Thread.Sleep(100);

            // 주의: 원본과 동일한 인자 순서를 유지한다.
            // IEthercatDevice 시그니처는 (acceleration, deceleration, maxVelocity, velocity)이지만
            // 원본은 (velocity, maxVelocity, deceleration, acceleration) 순서로 넘기고 있다. (확인 필요)
            ethercat.Axis1_UD_Config_Update(velocity, maxVelocity, deceleration, acceleration);
            ethercat.Axis2_LR_Config_Update(velocity, maxVelocity, deceleration, acceleration);

            ethercat.Axis1_ON();
            ethercat.Axis2_ON();

            Thread.Sleep(500);
            var deadline = DateTime.Now + (confirmTimeout ?? TimeSpan.FromSeconds(2));
            while (DateTime.Now < deadline)
            {
                ethercat.ThrowIfEmergencyStopped();
                try
                {
                    string axis1Pos = ethercat.Axis1_is_PosData();
                    string axis2Pos = ethercat.Axis2_is_PosData();
                    if (!string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" &&
                        !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-")
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (!(ex is EmergencyStopActiveException))
                {
                    System.Diagnostics.Debug.WriteLine($"서보 ON 상태 확인 오류: {ex.Message}");
                }
                Thread.Sleep(100);
            }
            return false;
        }

        public static void TurnOff(IEthercatDevice ethercat)
        {
            if (ethercat == null) throw new ArgumentNullException(nameof(ethercat));
            ethercat.Axis1_OFF();
            ethercat.Axis2_OFF();
        }
    }
}
