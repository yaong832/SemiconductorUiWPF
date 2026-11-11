using System;
using System.Windows.Threading;

namespace SemiconductorUi.Wpf.Infrastructure
{
    /// <summary>UI 스레드에서 ms 뒤 한 번 부른다. 돌려준 값을 Dispose하면 취소 (장비 제어 창 "n초 후 자동 OFF")</summary>
    public static class DispatcherOnce
    {
        public static IDisposable Schedule(int milliseconds, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds)) };
            var handle = new Handle(timer);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (!handle.Cancelled) action();
            };
            timer.Start();
            return handle;
        }

        private sealed class Handle : IDisposable
        {
            private readonly DispatcherTimer _timer;

            public Handle(DispatcherTimer timer)
            {
                _timer = timer;
            }

            public bool Cancelled { get; private set; }

            public void Dispose()
            {
                Cancelled = true;
                _timer.Stop();
            }
        }
    }
}
