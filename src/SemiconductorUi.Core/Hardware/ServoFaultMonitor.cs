using System.Collections.Generic;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 서보 드라이브 알람(Fault)이 새로 생겼는지 감시합니다.
    ///
    /// 알람이 계속 켜져 있는 동안에는 한 번만 알립니다. 알람이 꺼졌다가 다시 켜지면 다시 알립니다.
    /// 드라이브는 알람이 나면 스스로 출력을 끊지만, 프로그램은 그 사실을 모르고 다음 명령을 보내므로
    /// 새 알람을 받으면 비상정지와 같은 경로로 멈춰야 합니다.
    /// </summary>
    public sealed class ServoFaultMonitor
    {
        private static readonly int[] Axes = { 1, 2 };

        private readonly IEthercatDevice _device;
        private readonly bool[] _faulted = new bool[3];

        public ServoFaultMonitor(IEthercatDevice device)
        {
            _device = device;
        }

        /// <summary>
        /// 지금 읽은 상태로 갱신하고, 지난 확인 때는 없다가 새로 생긴 알람 축 번호를 돌려줍니다.
        /// </summary>
        public IList<int> PollNewFaults()
        {
            var newFaults = new List<int>();
            if (_device == null)
            {
                return newFaults;
            }

            foreach (int axis in Axes)
            {
                bool faulted = _device.IsAxisFault(axis);
                if (faulted && !_faulted[axis])
                {
                    newFaults.Add(axis);
                }
                _faulted[axis] = faulted;
            }

            return newFaults;
        }

        /// <summary>
        /// 마지막 확인 때 알람이 켜져 있던 축 번호.
        /// </summary>
        public IList<int> GetFaultedAxes()
        {
            var faulted = new List<int>();
            foreach (int axis in Axes)
            {
                if (_faulted[axis])
                {
                    faulted.Add(axis);
                }
            }
            return faulted;
        }

        /// <summary>
        /// 기억한 상태를 지웁니다. 연결이 끊기면 부릅니다. 다시 연결했을 때 알람이 있으면 새 알람으로 알립니다.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _faulted.Length; i++)
            {
                _faulted[i] = false;
            }
        }
    }
}
