using System;
using System.Windows.Threading;

namespace SemiconductorUi.Wpf.Infrastructure
{
    /// <summary>
    /// Core의 ITickTimer를 WPF DispatcherTimer로 구현 (WinForms Timer 대체).
    /// Tick은 UI 스레드에서 발생한다.
    /// </summary>
    public sealed class DispatcherTickTimer : ITickTimer
    {
        private readonly DispatcherTimer _timer;

        public DispatcherTickTimer(DispatcherPriority priority = DispatcherPriority.Normal)
        {
            _timer = new DispatcherTimer(priority);
            _timer.Tick += (s, e) => Tick?.Invoke(this, e);
        }

        public event EventHandler Tick;

        public int Interval
        {
            get => (int)_timer.Interval.TotalMilliseconds;
            set => _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, value));
        }

        public bool Enabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}
