using System;
using System.Diagnostics;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 공정 초기화에서 쓰는 원점복귀 순서입니다.
    ///
    /// 이 장비는 상하(축1) 원점복귀가 끝난 뒤에만 좌우(축2, 회전) 원점복귀를 해야 하고,
    /// 두 원점복귀 모두 실린더가 후진한 상태에서만 해야 합니다.
    /// - 실린더 후진 센서(입력 12)가 켜져 있고 전진 출력(출력 12)이 꺼져 있어야 시작합니다.
    ///   센서를 읽지 못해도 시작하지 않습니다.
    /// - 상하 원점복귀 명령 뒤 HOME_D를 기다립니다. 입력이 0.3초마다 갱신되므로
    ///   명령 직후 1초 동안은 예전 값일 수 있어 HOME_D를 믿지 않습니다.
    /// - 좌우 원점복귀 직전에 실린더 상태를 한 번 더 확인합니다.
    /// 실패하면 그 뒤 명령은 보내지 않습니다.
    /// </summary>
    internal sealed class ResetHomingSequence
    {
        internal const int CylinderExtendOutput = 12;
        internal const int CylinderRetractedInput = 12;
        internal const int HomeDoneTrustDelayMs = 1000;
        internal const int AxisTimeoutMs = 120000;
        internal const int PollIntervalMs = 100;

        private readonly IEthercatDevice _device;
        private readonly Func<long> _nowMs;
        private readonly Action<int> _sleep;

        public ResetHomingSequence(IEthercatDevice device)
            : this(device, Stopwatch.StartNew(), ms => System.Threading.Thread.Sleep(ms))
        {
        }

        private ResetHomingSequence(IEthercatDevice device, Stopwatch clock, Action<int> sleep)
            : this(device, () => clock.ElapsedMilliseconds, sleep)
        {
        }

        internal ResetHomingSequence(IEthercatDevice device, Func<long> nowMs, Action<int> sleep)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _nowMs = nowMs;
            _sleep = sleep;
        }

        /// <summary>
        /// 상하 → 좌우 순서로 원점복귀합니다. 성공하면 null, 실패하면 이유를 돌려줍니다.
        /// 비상정지가 걸리면 EmergencyStopActiveException을 그대로 던집니다.
        /// </summary>
        public string Run(Action<string> log)
        {
            string reason = CheckCylinderRetracted("상하 원점복귀");
            if (reason != null) return reason;

            log?.Invoke("공정 리셋: 원점복귀 1단계 - 상하(Axis1)");
            _device.Axis1_UD_Homming();
            if (!WaitHomeDone(() => _device.Axis1_Status("HOME_D")))
            {
                return "상하(Axis1) 원점복귀가 120초 안에 끝나지 않았습니다. 좌우 원점복귀는 하지 않았습니다.";
            }

            reason = CheckCylinderRetracted("좌우 원점복귀");
            if (reason != null) return reason;

            log?.Invoke("공정 리셋: 상하 원점복귀 완료 - 2단계: 좌우(Axis2)");
            _device.Axis2_LR_Homming();
            if (!WaitHomeDone(() => _device.Axis2_Status("HOME_D")))
            {
                return "좌우(Axis2) 원점복귀가 120초 안에 끝나지 않았습니다.";
            }

            log?.Invoke("공정 리셋: 원점복귀 완료 (상하 → 좌우)");
            return null;
        }

        private string CheckCylinderRetracted(string action)
        {
            bool retracted;
            bool extendOutputOn;
            try
            {
                retracted = _device.Digital_Input(CylinderRetractedInput);
                extendOutputOn = _device.GetDigitalOutput(CylinderExtendOutput);
            }
            catch (Exception ex)
            {
                return $"실린더 상태를 읽지 못해 {action}를 하지 않았습니다: {ex.Message}";
            }

            if (extendOutputOn)
            {
                return $"실린더 전진 출력이 켜져 있어 {action}를 하지 않았습니다.";
            }
            if (!retracted)
            {
                return $"실린더가 후진 위치가 아니라(후진 센서 OFF) {action}를 하지 않았습니다.";
            }
            return null;
        }

        private bool WaitHomeDone(Func<bool> isHomeDone)
        {
            long start = _nowMs();
            while (true)
            {
                _device.ThrowIfEmergencyStopped();
                long elapsed = _nowMs() - start;
                if (elapsed >= HomeDoneTrustDelayMs && isHomeDone())
                {
                    return true;
                }
                if (elapsed >= AxisTimeoutMs)
                {
                    return false;
                }
                _sleep(PollIntervalMs);
            }
        }
    }
}
