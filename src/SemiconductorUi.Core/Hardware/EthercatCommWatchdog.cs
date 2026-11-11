using System;
using System.Threading;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 통신 감시 판정 결과
    /// </summary>
    public enum EthercatWatchdogEvent
    {
        /// <summary>바뀐 것 없음</summary>
        None,
        /// <summary>감시를 시작하고 처음 확인한 결과</summary>
        FirstCheck,
        /// <summary>연속으로 기준 횟수만큼 이상이 나와 "통신 이상"이 됨</summary>
        BecameUnhealthy,
        /// <summary>통신 이상에서 다시 정상으로 돌아옴</summary>
        Recovered,
        /// <summary>정해진 간격마다 남기는 요약</summary>
        Summary
    }

    /// <summary>
    /// EtherCAT 통신 상태를 판정합니다 (시간·장치와 분리해 시험할 수 있게 함).
    /// - 이상이 연속 failThreshold번 나오면 BecameUnhealthy (한 번 튄 값은 세기만 하고 알리지 않음)
    /// - 이상 상태에서 정상이 한 번 나오면 Recovered
    /// - summaryInterval마다 확인 횟수와 이상 횟수를 Summary로 알림
    /// </summary>
    public sealed class EthercatWatchdogState
    {
        private readonly int failThreshold;
        private readonly TimeSpan summaryInterval;

        private bool started;
        private int consecutiveFailures;
        private DateTime unhealthySince;
        private DateTime summaryStart;

        public EthercatWatchdogState(int failThreshold, TimeSpan summaryInterval)
        {
            this.failThreshold = Math.Max(1, failThreshold);
            this.summaryInterval = summaryInterval;
        }

        /// <summary>지금 "통신 이상" 상태인지</summary>
        public bool IsUnhealthy { get; private set; }

        /// <summary>요약 구간 안의 확인 횟수</summary>
        public int ChecksInWindow { get; private set; }

        /// <summary>요약 구간 안의 이상 횟수 (한 번 튄 것 포함)</summary>
        public int FailuresInWindow { get; private set; }

        /// <summary>마지막으로 확인한 결과 설명</summary>
        public string LastDetail { get; private set; }

        /// <summary>Recovered일 때: 이상이 이어진 시간</summary>
        public TimeSpan LastUnhealthyDuration { get; private set; }

        /// <summary>Summary일 때: 방금 끝난 구간의 확인 횟수와 이상 횟수</summary>
        public int SummaryChecks { get; private set; }
        public int SummaryFailures { get; private set; }

        /// <summary>
        /// 확인 결과 하나를 넣습니다. healthy = 버스 ON이고 읽기 오류 없음.
        /// </summary>
        public EthercatWatchdogEvent Observe(bool healthy, string detail, DateTime now)
        {
            LastDetail = detail;

            if (!started)
            {
                started = true;
                summaryStart = now;
                ChecksInWindow = 1;
                FailuresInWindow = healthy ? 0 : 1;
                consecutiveFailures = healthy ? 0 : 1;
                if (!healthy && consecutiveFailures >= failThreshold)
                {
                    IsUnhealthy = true;
                    unhealthySince = now;
                }
                return EthercatWatchdogEvent.FirstCheck;
            }

            ChecksInWindow++;
            if (!healthy)
            {
                FailuresInWindow++;
                consecutiveFailures++;
                if (!IsUnhealthy && consecutiveFailures >= failThreshold)
                {
                    IsUnhealthy = true;
                    unhealthySince = now;
                    return EthercatWatchdogEvent.BecameUnhealthy;
                }
            }
            else
            {
                consecutiveFailures = 0;
                if (IsUnhealthy)
                {
                    IsUnhealthy = false;
                    LastUnhealthyDuration = now - unhealthySince;
                    return EthercatWatchdogEvent.Recovered;
                }
            }

            if (summaryInterval > TimeSpan.Zero && now - summaryStart >= summaryInterval)
            {
                SummaryChecks = ChecksInWindow;
                SummaryFailures = FailuresInWindow;
                summaryStart = now;
                ChecksInWindow = 0;
                FailuresInWindow = 0;
                return EthercatWatchdogEvent.Summary;
            }

            return EthercatWatchdogEvent.None;
        }
    }

    /// <summary>
    /// 1단계 통신 감시: 백그라운드에서 정해진 주기로 CheckCommunication을 불러 결과를 알리기만 합니다.
    /// 장비를 멈추거나 명령을 막지 않습니다 (판정이 실장비에서 맞는지 확인한 뒤 2단계에서 비상정지와 연결).
    /// 화면 스레드를 쓰지 않으므로, 통신 이상으로 드라이버 응답이 늦어져도 화면이 멈추지 않습니다.
    /// </summary>
    public sealed class EthercatCommWatchdog : IDisposable
    {
        private readonly IEthercatDevice device;
        private readonly int intervalMs;
        private readonly EthercatWatchdogState state;
        private readonly Action<EthercatWatchdogEvent, EthercatWatchdogState> onEvent;
        private readonly object runLock = new object();

        private Thread thread;
        private ManualResetEvent stopSignal;

        public EthercatCommWatchdog(
            IEthercatDevice device,
            int intervalMs,
            int failThreshold,
            TimeSpan summaryInterval,
            Action<EthercatWatchdogEvent, EthercatWatchdogState> onEvent)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.intervalMs = Math.Max(100, intervalMs);
            this.onEvent = onEvent;
            state = new EthercatWatchdogState(failThreshold, summaryInterval);
        }

        public bool IsRunning
        {
            get
            {
                lock (runLock)
                {
                    return thread != null;
                }
            }
        }

        public void Start()
        {
            lock (runLock)
            {
                if (thread != null)
                {
                    return;
                }

                stopSignal = new ManualResetEvent(false);
                thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "EtherCAT 통신 감시"
                };
                thread.Start();
            }
        }

        /// <summary>
        /// 감시를 멈추고, 진행 중인 확인이 끝날 때까지 기다립니다 (최대 2초).
        /// 연결을 끊기 전에 불러야 끊긴 채널을 건드리지 않습니다.
        /// </summary>
        public void Stop()
        {
            Thread running;
            ManualResetEvent signal;
            lock (runLock)
            {
                running = thread;
                signal = stopSignal;
                thread = null;
                stopSignal = null;
            }

            if (running == null)
            {
                return;
            }

            signal.Set();
            if (Thread.CurrentThread == running)
            {
                return;
            }

            // 확인이 끝나면 핸들을 닫습니다. 2초 안에 끝나지 않으면 감시 스레드가 스스로 끝나도록 닫지 않고 둡니다.
            if (running.Join(2000))
            {
                signal.Dispose();
            }
        }

        private void Run()
        {
            ManualResetEvent signal;
            lock (runLock)
            {
                signal = stopSignal;
            }

            try
            {
                while (signal != null && !signal.WaitOne(intervalMs))
                {
                    CheckOnce();
                }
            }
            catch (Exception ex)
            {
                // 백그라운드 스레드의 예외가 프로그램을 끝내지 않게 합니다.
                System.Diagnostics.Debug.WriteLine($"통신 감시 종료: {ex.Message}");
            }
        }

        private void CheckOnce()
        {
            bool healthy;
            string detail;
            try
            {
                var status = device.CheckCommunication();
                healthy = status.IsHealthy;
                detail = status.ToString();
            }
            catch (Exception ex)
            {
                healthy = false;
                detail = $"확인 실패: {ex.Message}";
            }

            var evt = state.Observe(healthy, detail, DateTime.Now);
            if (evt == EthercatWatchdogEvent.None)
            {
                return;
            }

            try
            {
                onEvent?.Invoke(evt, state);
            }
            catch
            {
                // 알림 처리 오류가 감시를 멈추지 않게 합니다.
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
