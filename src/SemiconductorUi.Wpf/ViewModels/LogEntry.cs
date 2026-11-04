using System;

namespace SemiconductorUi.Wpf.ViewModels
{
    public sealed class LogEntry
    {
        public LogEntry(DateTime time, string level, string message)
        {
            Time = time;
            Level = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
            Message = message ?? string.Empty;
        }

        public DateTime Time { get; }
        public string Level { get; }
        public string Message { get; }
        public string TimeText => Time.ToString("HH:mm:ss");

        public bool IsError => Level == "ERROR" || Level == "ALARM" || Level == "CRITICAL";
        public bool IsWarning => Level == "WARN" || Level == "WARNING";
    }
}
