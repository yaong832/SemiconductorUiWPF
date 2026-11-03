using System;
using System.Diagnostics;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// TM 회전(축2)과 실린더 전진이 겹치지 않게 막는 규칙.
    ///
    /// - 실린더가 후진 위치가 아니면 회전 이동과 회전 원점복귀를 보내지 않습니다.
    ///   상하(축1)는 막지 않습니다. 전진한 채로 웨이퍼를 집고 놓아야 하기 때문입니다.
    /// - 회전 명령을 보낸 뒤 회전이 끝났다고 확인될 때까지 실린더 전진을 보내지 않습니다.
    ///
    /// 장비 창구(Ieg3268Device)가 자기 락 안에서 부르므로 이 클래스는 따로 잠그지 않습니다.
    /// 시각은 밀리초 단위 시계 함수로 받습니다(테스트에서 시간을 직접 넘기기 위함).
    /// </summary>
    internal sealed class TmMotionInterlock
    {
        internal const int CylinderExtendOutput = 12;
        internal const int CylinderRetractOutput = 13;
        internal const int CylinderRetractedInput = 12;

        /// <summary>
        /// 회전 명령 뒤 완료 상태를 믿기 전까지 기다리는 최소 시간.
        /// DLL 입력 갱신 주기(300ms)보다 길어야 명령 전의 "도착" 값을 완료로 착각하지 않습니다.
        /// 자동 공정의 이동 완료 판단(ServoMoveMinWaitMs 500)과 같은 값입니다.
        /// </summary>
        internal const long RotationSettleMinMs = 500;

        /// <summary>회전 서보 OFF(또는 비상정지) 뒤 실린더 전진을 허용하기까지의 시간. 관성으로 도는 것을 기다립니다.</summary>
        internal const long RotationStopSettleMs = 1000;

        /// <summary>
        /// 실린더 전진 명령 뒤 이 시간 안에는 후진 센서가 켜져 있어도 회전하지 않습니다.
        /// 입력은 300ms마다 갱신되므로, 전진 직후 후진했을 때 전진 전의 "후진" 값이 남아 있을 수 있습니다.
        /// </summary>
        internal const long ExtendToRotationMinMs = 1000;

        private enum RotationCommand
        {
            None,
            Moving,
            Stopping
        }

        private readonly Func<long> _nowMs;
        private RotationCommand _rotation = RotationCommand.None;
        private long _rotationCommandAtMs;
        private long? _lastExtendCommandAtMs;

        public TmMotionInterlock()
            : this(CreateStopwatchClock())
        {
        }

        internal TmMotionInterlock(Func<long> nowMs)
        {
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        }

        /// <summary>회전 명령이 끝났다고 아직 확인되지 않았으면 true.</summary>
        public bool IsRotationPending => _rotation != RotationCommand.None;

        /// <summary>
        /// 회전 이동/원점복귀를 보내도 되는지 확인합니다. 안 되면 HardwareInterlockException.
        /// </summary>
        /// <param name="action">메시지에 넣을 동작 이름 (예: "회전 이동").</param>
        /// <param name="extendOutputOn">실린더 전진 출력(12번)이 마지막으로 켜짐으로 명령되었는지.</param>
        /// <param name="readRetractedSensor">후진 센서(입력 12번)를 읽는 함수.</param>
        public void CheckRotationAllowed(string action, bool extendOutputOn, Func<bool> readRetractedSensor)
        {
            if (extendOutputOn)
            {
                throw new HardwareInterlockException(
                    $"실린더 전진 출력이 켜져 있어 {action}을(를) 보내지 않았습니다. 실린더를 후진한 뒤 다시 하세요.");
            }

            if (_lastExtendCommandAtMs.HasValue && _nowMs() - _lastExtendCommandAtMs.Value < ExtendToRotationMinMs)
            {
                throw new HardwareInterlockException(
                    $"실린더를 방금 움직여서 후진 센서 값이 아직 갱신되지 않았을 수 있습니다. {action}을(를) 보내지 않았습니다. 잠시 후 다시 하세요.");
            }

            bool retracted;
            try
            {
                retracted = readRetractedSensor();
            }
            catch (Exception ex)
            {
                throw new HardwareInterlockException(
                    $"실린더 후진 센서를 읽지 못해 {action}을(를) 보내지 않았습니다: {ex.Message}");
            }

            if (!retracted)
            {
                throw new HardwareInterlockException(
                    $"실린더가 후진 위치가 아니라(후진 센서 OFF) {action}을(를) 보내지 않았습니다. 실린더를 후진한 뒤 다시 하세요.");
            }
        }

        /// <summary>회전 이동/원점복귀 명령을 보내기 직전과 직후에 부릅니다.</summary>
        public void NoteRotationCommand()
        {
            _rotation = RotationCommand.Moving;
            _rotationCommandAtMs = _nowMs();
        }

        /// <summary>회전 서보 OFF나 비상정지로 회전 축을 멈췄을 때 부릅니다.</summary>
        public void NoteRotationStopped()
        {
            if (_rotation == RotationCommand.None)
            {
                return;
            }

            _rotation = RotationCommand.Stopping;
            _rotationCommandAtMs = _nowMs();
        }

        /// <summary>
        /// 이 출력이 실린더를 전진시키는 쪽인지. 전진 ON과 후진 OFF가 해당합니다.
        /// 후진 OFF만 보내도 후진 유지가 풀리므로 같이 막습니다.
        /// </summary>
        public static bool IsExtendSideOutput(int numbering, bool value)
        {
            return (numbering == CylinderExtendOutput && value)
                || (numbering == CylinderRetractOutput && !value);
        }

        /// <summary>
        /// 실린더 출력을 보내도 되는지 확인합니다. 전진 쪽 출력만 확인하고, 후진 쪽은 언제나 허용합니다.
        /// </summary>
        /// <param name="isRotationDone">회전 축이 목표에 도착했는지 읽는 함수 (PP_D 또는 HOME_D).</param>
        public void CheckCylinderOutputAllowed(int numbering, bool value, Func<bool> isRotationDone)
        {
            if (!IsExtendSideOutput(numbering, value) || _rotation == RotationCommand.None)
            {
                return;
            }

            long elapsed = _nowMs() - _rotationCommandAtMs;

            if (_rotation == RotationCommand.Stopping)
            {
                if (elapsed >= RotationStopSettleMs)
                {
                    _rotation = RotationCommand.None;
                    return;
                }

                throw new HardwareInterlockException(
                    "회전 축을 방금 멈춰서 실린더를 전진하지 않았습니다. 잠시 후 다시 하세요.");
            }

            bool done = false;
            if (elapsed >= RotationSettleMinMs)
            {
                try
                {
                    done = isRotationDone();
                }
                catch (Exception ex)
                {
                    throw new HardwareInterlockException(
                        $"회전 축 상태를 읽지 못해 실린더를 전진하지 않았습니다: {ex.Message}");
                }
            }

            if (!done)
            {
                throw new HardwareInterlockException(
                    "회전 축이 아직 움직이는 중이라 실린더를 전진하지 않았습니다. 회전이 끝난 뒤 다시 하세요.");
            }

            _rotation = RotationCommand.None;
        }

        /// <summary>실린더 출력을 보내기 직전에 부릅니다.</summary>
        public void NoteCylinderOutput(int numbering, bool value)
        {
            if (numbering == CylinderExtendOutput && value)
            {
                _lastExtendCommandAtMs = _nowMs();
            }
        }

        private static Func<long> CreateStopwatchClock()
        {
            var stopwatch = Stopwatch.StartNew();
            return () => stopwatch.ElapsedMilliseconds;
        }
    }
}
