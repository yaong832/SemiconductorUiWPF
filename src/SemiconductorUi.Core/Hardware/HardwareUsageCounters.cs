using System.Threading;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 장비 창구(Ieg3268Device)가 실제로 보낸 명령의 횟수를 셉니다 (유지보수 카운터용).
    /// 장비 명령 경로 안에서 불리므로 숫자만 올립니다. 파일 저장, 로그, 알림은 하지 않고 예외도 내지 않습니다.
    /// 유지보수 쪽이 주기적으로 Drain()으로 가져가 더합니다.
    /// </summary>
    public sealed class HardwareUsageCounters
    {
        /// <summary>셀 수 있는 디지털 출력 개수 (0번부터)</summary>
        public const int OutputCount = 32;

        private readonly long[] outputOnEdges = new long[OutputCount];
        private long axis1Commands;
        private long axis2Commands;

        /// <summary>
        /// 디지털 출력을 보낸 뒤 부릅니다. 꺼져 있던 출력이 켜질 때만 1 올립니다.
        /// 같은 값을 다시 보내거나 끄는 것은 세지 않습니다.
        /// </summary>
        public void NoteOutput(int numbering, bool previous, bool value)
        {
            if (!value || previous || numbering < 0 || numbering >= OutputCount)
            {
                return;
            }

            Interlocked.Increment(ref outputOnEdges[numbering]);
        }

        /// <summary>
        /// 축 이동 또는 원점복귀 명령을 보낸 뒤 부릅니다.
        /// </summary>
        /// <param name="axis">1 = 상하, 2 = 회전</param>
        public void NoteAxisCommand(int axis)
        {
            if (axis == 1)
            {
                Interlocked.Increment(ref axis1Commands);
            }
            else if (axis == 2)
            {
                Interlocked.Increment(ref axis2Commands);
            }
        }

        /// <summary>
        /// 지금까지 센 값을 가져가고 0으로 되돌립니다.
        /// </summary>
        public HardwareUsageSnapshot Drain()
        {
            var outputs = new long[OutputCount];
            for (int i = 0; i < OutputCount; i++)
            {
                outputs[i] = Interlocked.Exchange(ref outputOnEdges[i], 0);
            }

            return new HardwareUsageSnapshot(
                outputs,
                Interlocked.Exchange(ref axis1Commands, 0),
                Interlocked.Exchange(ref axis2Commands, 0));
        }
    }

    /// <summary>
    /// Drain()이 돌려준 구간의 횟수
    /// </summary>
    public sealed class HardwareUsageSnapshot
    {
        private readonly long[] outputOnEdges;

        public HardwareUsageSnapshot(long[] outputOnEdges, long axis1Commands, long axis2Commands)
        {
            this.outputOnEdges = outputOnEdges ?? new long[0];
            Axis1Commands = axis1Commands;
            Axis2Commands = axis2Commands;
        }

        /// <summary>상하축 이동·원점복귀 명령 횟수</summary>
        public long Axis1Commands { get; }

        /// <summary>회전축 이동·원점복귀 명령 횟수</summary>
        public long Axis2Commands { get; }

        /// <summary>출력이 꺼짐에서 켜짐으로 바뀐 횟수</summary>
        public long GetOutputOnEdges(int numbering)
        {
            return numbering >= 0 && numbering < outputOnEdges.Length ? outputOnEdges[numbering] : 0;
        }
    }
}
