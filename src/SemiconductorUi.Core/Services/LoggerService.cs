using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 로깅 서비스 구현
    /// 기존 AddLogMessage와 호환되는 로깅 서비스
    /// </summary>
    public class LoggerService : ILoggerService
    {
        #region Fields

        private readonly List<string> _logEntries = new List<string>();
        private readonly object _lock = new object();

        #endregion

        #region Properties

        /// <summary>
        /// 최대 로그 엔트리 수
        /// </summary>
        public int MaxLogEntries { get; set; } = AppSettings.MaxLogEntries;

        #endregion

        #region ILoggerService Implementation

        /// <summary>
        /// 정보 로그 기록
        /// </summary>
        public void Info(string message)
        {
            Log(message, "INFO");
        }

        /// <summary>
        /// 경고 로그 기록
        /// </summary>
        public void Warn(string message)
        {
            Log(message, "WARN");
        }

        /// <summary>
        /// 오류 로그 기록
        /// </summary>
        public void Error(string message)
        {
            Log(message, "ERROR");
        }

        /// <summary>
        /// 알람 로그 기록
        /// </summary>
        public void Alarm(string message)
        {
            Log(message, "ALARM");
        }

        /// <summary>
        /// 치명적 오류 로그 기록
        /// </summary>
        public void Critical(string message)
        {
            Log(message, "CRITICAL");
        }

        /// <summary>
        /// 로그 기록 (레벨 지정)
        /// </summary>
        public void Log(string message, string level)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_lock)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var entry = $"[{timestamp}] [{level}] {message}";
                _logEntries.Insert(0, entry);

                // 최대 로그 엔트리 수 초과 시 자동 저장 및 초기화
                if (_logEntries.Count > MaxLogEntries)
                {
                    AutoSaveAndClearLogs();
                }
            }
        }

        /// <summary>
        /// 로그 엔트리 목록 가져오기 (UI 표시용)
        /// </summary>
        public List<string> GetLogEntries()
        {
            lock (_lock)
            {
                return new List<string>(_logEntries);
            }
        }

        #endregion

        #region Log Management

        /// <summary>
        /// 로그 자동 저장 및 초기화
        /// 레벨별로 분리된 파일로 저장 (AutoSave_INFO_YYYYMMDD_HHmmss.csv 형식)
        /// </summary>
        private void AutoSaveAndClearLogs()
        {
            try
            {
                // 기본 저장 위치: Documents 폴더의 SemiconductorUi_Logs 폴더
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var logFolder = Path.Combine(documentsPath, "SemiconductorUi_Logs", "AutoSave");

                // 폴더가 없으면 생성
                if (!Directory.Exists(logFolder))
                {
                    Directory.CreateDirectory(logFolder);
                }

                // 타임스탬프 (모든 파일에 동일한 타임스탬프 사용)
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                var savedFiles = new List<string>();

                // 레벨별로 로그 분류
                var logsByLevel = new Dictionary<string, List<string>>();
                
                lock (_lock)
                {
                    var entriesToSave = _logEntries.ToList();
                    foreach (var entry in entriesToSave)
                    {
                        // entry 포맷: [HH:mm:ss] [LEVEL] message
                        var levelStart = entry.IndexOf(']', 2) + 3; // +3 to skip "] [" and get actual level
                        var levelEnd = entry.IndexOf(']', levelStart);
                        var level = (levelStart > 2 && levelEnd > levelStart) ? entry.Substring(levelStart, levelEnd - levelStart) : "INFO";

                        if (!logsByLevel.ContainsKey(level))
                        {
                            logsByLevel[level] = new List<string>();
                        }
                        logsByLevel[level].Add(entry);
                    }

                    // 레벨별로 파일 저장
                    foreach (var kvp in logsByLevel)
                    {
                        var level = kvp.Key;
                        var entries = kvp.Value;

                        var fileName = $"AutoSave_{level}_{timestamp}.csv";
                        var filePath = Path.Combine(logFolder, fileName);

                        using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
                        {
                            sw.WriteLine("Timestamp,Level,Message");

                            // entries는 최신이 맨 앞이므로 역순으로 저장 (시간순)
                            for (int i = entries.Count - 1; i >= 0; i--)
                            {
                                var entry = entries[i];
                                // entry 포맷: [HH:mm:ss] [LEVEL] message
                                var ts = entry.Length >= 10 ? entry.Substring(1, 8) : "";
                                var msgStart = entry.IndexOf(']', entry.IndexOf(']') + 1) + 2;
                                var msg = msgStart > 1 && msgStart < entry.Length ? entry.Substring(msgStart) : entry;
                                sw.WriteLine($"{ts},{level},\"{msg.Replace("\"", "\"\"")}\"");
                            }
                        }
                        savedFiles.Add(fileName);
                    }

                    // 로그 초기화 (새로 시작)
                    _logEntries.Clear();

                    // 자동 저장 완료 메시지 추가 (새 로그의 첫 항목)
                    var newTimestamp = DateTime.Now.ToString("HH:mm:ss");
                    var fileCount = savedFiles.Count;
                    _logEntries.Insert(0, $"[{newTimestamp}] [INFO] 이전 로그 자동 저장 완료: {fileCount}개 파일 (레벨별 분리)");
                }
            }
            catch (Exception ex)
            {
                // 로그 저장 실패 시 무시 (무한 루프 방지)
                System.Diagnostics.Debug.WriteLine($"로그 자동 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 로그 엔트리를 CSV 형식으로 변환
        /// 형식: [HH:mm:ss] [LEVEL] Message -> HH:mm:ss,LEVEL,"Message"
        /// </summary>
        private string ConvertLogEntryToCsv(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return null;

            try
            {
                // 정규식으로 파싱: [HH:mm:ss] [LEVEL] Message
                var match = Regex.Match(entry, @"\[(\d{2}:\d{2}:\d{2})\]\s+\[(\w+)\]\s+(.+)");
                if (match.Success)
                {
                    var timestamp = match.Groups[1].Value;
                    var level = match.Groups[2].Value;
                    var message = match.Groups[3].Value;

                    // CSV 형식: 메시지에 쉼표나 따옴표가 있으면 이스케이프 처리
                    message = message.Replace("\"", "\"\""); // 따옴표 이스케이프
                    if (message.Contains(",") || message.Contains("\"") || message.Contains("\n"))
                    {
                        message = $"\"{message}\"";
                    }

                    return $"{timestamp},{level},{message}";
                }
                else
                {
                    // 파싱 실패 시 전체를 메시지로 처리
                    var escapedMessage = entry.Replace("\"", "\"\"");
                    if (escapedMessage.Contains(",") || escapedMessage.Contains("\"") || escapedMessage.Contains("\n"))
                    {
                        escapedMessage = $"\"{escapedMessage}\"";
                    }
                    return $"{DateTime.Now:HH:mm:ss},INFO,{escapedMessage}";
                }
            }
            catch
            {
                // 변환 실패 시 원본 반환 (안전)
                var escapedMessage = entry.Replace("\"", "\"\"");
                if (escapedMessage.Contains(",") || escapedMessage.Contains("\"") || escapedMessage.Contains("\n"))
                {
                    escapedMessage = $"\"{escapedMessage}\"";
                }
                return $"{DateTime.Now:HH:mm:ss},INFO,{escapedMessage}";
            }
        }

        /// <summary>
        /// 로그 초기화
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _logEntries.Clear();
            }
        }

        /// <summary>
        /// 로그 엔트리 수
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _logEntries.Count;
                }
            }
        }

        #endregion
    }
}

