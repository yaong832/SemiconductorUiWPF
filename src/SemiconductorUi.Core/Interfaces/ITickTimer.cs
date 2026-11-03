using System;

namespace SemiconductorUi
{
    /// <summary>
    /// UI 스레드에서 Tick을 발생시키는 타이머 추상화.
    /// WinForms의 System.Windows.Forms.Timer 대신 사용하며,
    /// WPF에서는 DispatcherTimer 어댑터로 구현한다.
    /// </summary>
    public interface ITickTimer
    {
        event EventHandler Tick;
        int Interval { get; set; }
        bool Enabled { get; }
        void Start();
        void Stop();
    }
}
