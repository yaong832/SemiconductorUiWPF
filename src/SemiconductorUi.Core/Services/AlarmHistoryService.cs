using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 알람 코드 표. 실장비의 알람 번호(ALID)처럼 원인마다 번호를 하나씩 둡니다.
    /// 1xxx = 장비(비상정지, 서보, TM), 2xxx = 챔버 환경 (21xx = PMA, 22xx = PMB, 23xx = PMC).
    /// </summary>
    public static class AlarmCodes
    {
        public const int EmergencyStop = 1001;
        public const int EmergencyStopSendFailed = 1002;
        public const int EmergencyStopVerifyFailed = 1003;
        public const int ServoFaultAxis1 = 1101;
        public const int ServoFaultAxis2 = 1102;
        public const int TmHardwareError = 1201;

        // 챔버 환경: 21xx/22xx/23xx 뒤 두 자리
        public const int EnvTemperature = 1;
        public const int EnvPressure = 2;
        public const int EnvRf = 3;
        public const int EnvGasFlowNF3 = 4;
        public const int EnvGasFlowO2 = 5;
        public const int EnvGasFlowCF4 = 6;
        public const int EnvGasLeakNF3 = 7;
        public const int EnvGasLeakO2 = 8;
        public const int EnvGasLeakCF4 = 9;

        /// <summary>
        /// 장비 쪽 알람인지 (비상정지·서보). 비상정지 해제 때 함께 해제합니다.
        /// </summary>
        public static bool IsEmergencyStopGroup(int code)
        {
            return code == EmergencyStop
                || code == EmergencyStopSendFailed
                || code == EmergencyStopVerifyFailed
                || code == ServoFaultAxis1
                || code == ServoFaultAxis2;
        }

        public static int ServoFault(int axis)
        {
            return axis == 1 ? ServoFaultAxis1 : ServoFaultAxis2;
        }

        /// <summary>
        /// 챔버 환경 알람 코드. unitKey = PMA/PMB/PMC, item = EnvTemperature 등.
        /// </summary>
        public static int ForEnvironment(string unitKey, int item)
        {
            int unit;
            switch ((unitKey ?? "").Trim().ToUpperInvariant())
            {
                case "PMA": unit = 1; break;
                case "PMB": unit = 2; break;
                case "PMC": unit = 3; break;
                default: unit = 9; break;
            }
            return 2000 + unit * 100 + item;
        }

        public static int GasFlow(string gasName)
        {
            switch ((gasName ?? "").Trim().ToUpperInvariant())
            {
                case "NF3": return EnvGasFlowNF3;
                case "O2": return EnvGasFlowO2;
                default: return EnvGasFlowCF4;
            }
        }

        public static int GasLeak(string gasName)
        {
            switch ((gasName ?? "").Trim().ToUpperInvariant())
            {
                case "NF3": return EnvGasLeakNF3;
                case "O2": return EnvGasLeakO2;
                default: return EnvGasLeakCF4;
            }
        }

        /// <summary>
        /// 코드에 해당하는 알람 이름
        /// </summary>
        public static string NameOf(int code)
        {
            switch (code)
            {
                case EmergencyStop: return "비상정지";
                case EmergencyStopSendFailed: return "비상정지 서보 OFF 전송 오류";
                case EmergencyStopVerifyFailed: return "비상정지 후 서보 OFF 확인 실패";
                case ServoFaultAxis1: return "서보 알람 Axis1(상하)";
                case ServoFaultAxis2: return "서보 알람 Axis2(좌우)";
                case TmHardwareError: return "TM 하드웨어 오류 (자동 일시정지)";
            }

            if (code >= 2000 && code < 3000)
            {
                switch (code % 100)
                {
                    case EnvTemperature: return "온도 이탈";
                    case EnvPressure: return "압력 이탈";
                    case EnvRf: return "RF 이탈";
                    case EnvGasFlowNF3: return "NF3 유량 이탈";
                    case EnvGasFlowO2: return "O2 유량 이탈";
                    case EnvGasFlowCF4: return "CF4 유량 이탈";
                    case EnvGasLeakNF3: return "NF3 누설 의심";
                    case EnvGasLeakO2: return "O2 누설 의심";
                    case EnvGasLeakCF4: return "CF4 누설 의심";
                }
            }

            return "알람";
        }
    }

    /// <summary>
    /// 알람 한 건 (발생부터 해제까지)
    /// </summary>
    public sealed class AlarmRecord
    {
        public int Code { get; internal set; }
        public string Unit { get; internal set; }
        public string Name { get; internal set; }
        public string Detail { get; internal set; }
        public DateTime SetTime { get; internal set; }
        public DateTime? ClearTime { get; internal set; }
        public string ClearedBy { get; internal set; }

        public bool IsActive => !ClearTime.HasValue;
    }

    /// <summary>
    /// 알람 이력 파일의 한 줄 (화면 표시용)
    /// </summary>
    public sealed class AlarmHistoryRow
    {
        public string Date { get; set; }
        public string Time { get; set; }
        public string Event { get; set; }
        public string Code { get; set; }
        public string Unit { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        public string User { get; set; }
    }

    /// <summary>
    /// 알람 발생과 해제를 한곳에 모아 기록합니다.
    /// - 지금 걸려 있는 알람(현재 알람)과 이번 실행의 이력을 메모리에 둡니다.
    /// - 발생·해제를 한 줄씩 날짜별 파일(Alarm_yyyyMMdd.csv)에 바로 씁니다 (디스크까지).
    /// - 같은 코드·같은 유닛의 알람이 이미 걸려 있으면 다시 기록하지 않습니다 (주기 판정 중복 방지).
    /// - 알람은 알람 리셋으로 해제하며, 해제한 사용자를 남깁니다.
    /// 기록만 하며 장비 동작이나 공정 흐름은 바꾸지 않습니다.
    /// </summary>
    public sealed class AlarmHistoryService : IDisposable
    {
        public const string FilePrefix = "Alarm_";
        public const string FileHeader = "Date,Time,Event,Code,Unit,Name,Detail,User";
        public const string EventSet = "발생";
        public const string EventClear = "해제";

        private const int MaxRecentRecords = 500;

        private readonly object _lock = new object();
        private readonly List<AlarmRecord> _active = new List<AlarmRecord>();
        private readonly List<AlarmRecord> _recent = new List<AlarmRecord>();
        private readonly DailyLogFileWriter _fileWriter;

        /// <param name="fileWriter">이력 파일 기록기. null이면 메모리에만 둡니다 (시험용).</param>
        public AlarmHistoryService(DailyLogFileWriter fileWriter)
        {
            _fileWriter = fileWriter;
            _fileWriter?.DeleteExpiredFiles(DateTime.Today);
        }

        /// <summary>
        /// exe.config 설정(AlarmHistoryFolder, AlarmHistoryRetentionDays)으로 만듭니다.
        /// 파일 기록기를 만들지 못하면 메모리에만 둡니다.
        /// </summary>
        public static AlarmHistoryService CreateFromSettings()
        {
            DailyLogFileWriter writer = null;
            try
            {
                var folder = AppSettings.AlarmHistoryFolder;
                if (string.IsNullOrWhiteSpace(folder))
                {
                    folder = DefaultFolder;
                }
                writer = new DailyLogFileWriter(folder, AppSettings.AlarmHistoryRetentionDays, FilePrefix, FileHeader);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"알람 이력 기록기 생성 실패: {ex.Message}");
            }
            return new AlarmHistoryService(writer);
        }

        /// <summary>
        /// 기본 폴더: 문서\SemiconductorUi_Logs\Alarms
        /// </summary>
        public static string DefaultFolder =>
            Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "SemiconductorUi_Logs",
                "Alarms");

        /// <summary>
        /// 이력 파일 폴더 (파일 기록이 꺼져 있으면 null)
        /// </summary>
        public string Folder => _fileWriter?.Folder;

        /// <summary>
        /// 이력 파일 쓰기가 실패 중인지
        /// </summary>
        public bool IsFileFailing => _fileWriter != null && _fileWriter.IsFailing;

        /// <summary>
        /// 알람이 생기거나 해제되면 알려 줍니다 (부른 스레드에서).
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// 날짜에 해당하는 이력 파일 경로 (파일 기록이 꺼져 있으면 null)
        /// </summary>
        public string GetFilePath(DateTime date)
        {
            return _fileWriter?.GetFilePath(date);
        }

        /// <summary>
        /// 알람 발생을 기록합니다. 같은 코드·유닛이 이미 걸려 있으면 false (기록 안 함).
        /// </summary>
        public bool Raise(int code, string unit, string detail)
        {
            return Raise(code, unit, detail, DateTime.Now);
        }

        internal bool Raise(int code, string unit, string detail, DateTime now)
        {
            var safeUnit = string.IsNullOrWhiteSpace(unit) ? "-" : unit.Trim();
            AlarmRecord record;
            lock (_lock)
            {
                if (_active.Any(a => a.Code == code && a.Unit == safeUnit))
                {
                    return false;
                }

                record = new AlarmRecord
                {
                    Code = code,
                    Unit = safeUnit,
                    Name = AlarmCodes.NameOf(code),
                    Detail = detail ?? "",
                    SetTime = now
                };
                _active.Add(record);
                _recent.Insert(0, record);
                if (_recent.Count > MaxRecentRecords)
                {
                    _recent.RemoveAt(_recent.Count - 1);
                }

                WriteRow(now, EventSet, record, "");
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 걸려 있는 알람을 모두 해제합니다. 해제한 건수를 돌려줍니다.
        /// </summary>
        public int ClearAll(string user)
        {
            return Clear(_ => true, user, DateTime.Now);
        }

        /// <summary>
        /// 비상정지·서보 알람만 해제합니다 (비상정지 해제 때).
        /// </summary>
        public int ClearEmergencyStopGroup(string user)
        {
            return Clear(a => AlarmCodes.IsEmergencyStopGroup(a.Code), user, DateTime.Now);
        }

        internal int Clear(Func<AlarmRecord, bool> match, string user, DateTime now)
        {
            var safeUser = string.IsNullOrWhiteSpace(user) ? "-" : user.Trim();
            int cleared = 0;
            lock (_lock)
            {
                foreach (var record in _active.Where(match).ToList())
                {
                    record.ClearTime = now;
                    record.ClearedBy = safeUser;
                    _active.Remove(record);
                    WriteRow(now, EventClear, record, safeUser);
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                RaiseChanged();
            }
            return cleared;
        }

        /// <summary>
        /// 지금 걸려 있는 알람 (오래된 것부터)
        /// </summary>
        public List<AlarmRecord> GetActive()
        {
            lock (_lock)
            {
                return _active.ToList();
            }
        }

        public int ActiveCount
        {
            get
            {
                lock (_lock)
                {
                    return _active.Count;
                }
            }
        }

        /// <summary>
        /// 이번 실행에서 생긴 알람 (최신부터, 최대 500건)
        /// </summary>
        public List<AlarmRecord> GetRecent()
        {
            lock (_lock)
            {
                return _recent.ToList();
            }
        }

        /// <summary>
        /// 날짜의 이력 파일을 읽습니다. 파일이 없거나 읽지 못하면 빈 목록.
        /// 프로그램이 쓰는 중에도 읽을 수 있습니다.
        /// </summary>
        public List<AlarmHistoryRow> ReadDay(DateTime date)
        {
            var path = GetFilePath(date);
            return string.IsNullOrEmpty(path) ? new List<AlarmHistoryRow>() : ReadFile(path);
        }

        internal static List<AlarmHistoryRow> ReadFile(string path)
        {
            var rows = new List<AlarmHistoryRow>();
            try
            {
                if (!File.Exists(path))
                {
                    return rows;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    bool first = true;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (first)
                        {
                            first = false;
                            if (line.StartsWith("Date,", StringComparison.Ordinal))
                            {
                                continue;
                            }
                        }

                        var fields = SplitCsvLine(line);
                        if (fields.Count < 8)
                        {
                            continue;
                        }

                        rows.Add(new AlarmHistoryRow
                        {
                            Date = fields[0],
                            Time = fields[1],
                            Event = fields[2],
                            Code = fields[3],
                            Unit = fields[4],
                            Name = fields[5],
                            Detail = fields[6],
                            User = fields[7]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"알람 이력 읽기 실패: {ex.Message}");
            }
            return rows;
        }

        /// <summary>
        /// 한 줄 CSV 나누기 (큰따옴표, "" 이스케이프 지원)
        /// </summary>
        internal static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            if (line == null)
            {
                return fields;
            }

            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        private void WriteRow(DateTime time, string eventText, AlarmRecord record, string user)
        {
            // 줄바꿈은 한 줄 기록을 깨뜨리므로 공백으로 바꿉니다.
            _fileWriter?.WriteFields(
                time,
                true,
                eventText,
                record.Code.ToString(CultureInfo.InvariantCulture),
                record.Unit,
                record.Name,
                OneLine(record.Detail),
                user ?? "");
        }

        private static string OneLine(string text)
        {
            return (text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"알람 이력 알림 오류: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _fileWriter?.Dispose();
        }
    }
}
