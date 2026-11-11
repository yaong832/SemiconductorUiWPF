using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 유지보수 카운터 항목 정의. 어떤 명령을 셀지 정합니다.
    /// </summary>
    public sealed class MaintenanceCounterDefinition
    {
        public MaintenanceCounterDefinition(string key, string name, string source, Func<HardwareUsageSnapshot, long> extract)
        {
            Key = key;
            Name = name;
            Source = source;
            Extract = extract;
        }

        /// <summary>파일에 저장하는 이름 (바꾸면 안 됨)</summary>
        public string Key { get; }

        /// <summary>화면에 보이는 이름</summary>
        public string Name { get; }

        /// <summary>무엇을 세는지 설명</summary>
        public string Source { get; }

        public Func<HardwareUsageSnapshot, long> Extract { get; }
    }

    /// <summary>
    /// 세는 항목 목록. 출력 번호는 TmHardwareController와 장비 제어 창의 배선 번호를 따릅니다.
    /// </summary>
    public static class MaintenanceCounterItems
    {
        public const int CylinderExtendOutput = 12;
        public const int VacuumOutput = 14;
        public const int ExhaustOutput = 15;
        public const int DoorAOpenOutput = 5;
        public const int DoorBOpenOutput = 8;
        public const int DoorCOpenOutput = 11;

        public static readonly IReadOnlyList<MaintenanceCounterDefinition> All = new[]
        {
            new MaintenanceCounterDefinition("CylinderExtend", "실린더 전진", "출력 12 켜짐 (실린더 전진)",
                s => s.GetOutputOnEdges(CylinderExtendOutput)),
            new MaintenanceCounterDefinition("Vacuum", "진공 흡착", "출력 14 켜짐 (진공)",
                s => s.GetOutputOnEdges(VacuumOutput)),
            new MaintenanceCounterDefinition("Exhaust", "배기", "출력 15 켜짐 (배기)",
                s => s.GetOutputOnEdges(ExhaustOutput)),
            new MaintenanceCounterDefinition("DoorA", "Chamber A 도어", "출력 5 켜짐 (도어 열림)",
                s => s.GetOutputOnEdges(DoorAOpenOutput)),
            new MaintenanceCounterDefinition("DoorB", "Chamber B 도어", "출력 8 켜짐 (도어 열림)",
                s => s.GetOutputOnEdges(DoorBOpenOutput)),
            new MaintenanceCounterDefinition("DoorC", "Chamber C 도어", "출력 11 켜짐 (도어 열림)",
                s => s.GetOutputOnEdges(DoorCOpenOutput)),
            new MaintenanceCounterDefinition("Axis1", "상하축 (Z)", "이동·원점복귀 명령",
                s => s.Axis1Commands),
            new MaintenanceCounterDefinition("Axis2", "회전축", "이동·원점복귀 명령",
                s => s.Axis2Commands),
        };
    }

    /// <summary>점검 주기와 비교한 상태</summary>
    public enum MaintenanceCounterStatus
    {
        /// <summary>점검 주기를 정하지 않음</summary>
        NoLimit,
        Normal,
        /// <summary>주기의 90% 이상</summary>
        Soon,
        /// <summary>주기에 도달</summary>
        Due
    }

    /// <summary>
    /// 한 항목의 현재 값 (화면 표시용 복사본)
    /// </summary>
    public sealed class MaintenanceCounterState
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }

        /// <summary>처음부터 센 횟수 (되돌리지 않음)</summary>
        public long Total { get; set; }

        /// <summary>마지막 점검 후 횟수</summary>
        public long SinceService { get; set; }

        /// <summary>점검 주기 (횟수). 0 = 정하지 않음</summary>
        public long Limit { get; set; }

        public DateTime? LastServiceTime { get; set; }
        public string LastServiceUser { get; set; }

        public MaintenanceCounterStatus Status => GetStatus(SinceService, Limit);

        /// <summary>주기 대비 비율 (0~). 주기가 없으면 0</summary>
        public double Ratio => Limit > 0 ? (double)SinceService / Limit : 0;

        public static MaintenanceCounterStatus GetStatus(long sinceService, long limit)
        {
            if (limit <= 0)
            {
                return MaintenanceCounterStatus.NoLimit;
            }

            if (sinceService >= limit)
            {
                return MaintenanceCounterStatus.Due;
            }

            return sinceService >= limit * MaintenanceCounterService.SoonRatio
                ? MaintenanceCounterStatus.Soon
                : MaintenanceCounterStatus.Normal;
        }

        internal MaintenanceCounterState Clone()
        {
            return (MaintenanceCounterState)MemberwiseClone();
        }
    }

    /// <summary>
    /// 유지보수 이력 한 줄
    /// </summary>
    public sealed class MaintenanceHistoryRow
    {
        public DateTime Time { get; set; }
        public string Event { get; set; }
        public string Item { get; set; }
        public string Count { get; set; }
        public string Limit { get; set; }
        public string User { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// 유지보수 카운터. 장비 창구가 센 명령 횟수를 1초마다 가져와 항목별로 더하고 파일에 저장합니다.
    /// - MaintenanceCounters.xml: 현재 값 (10초마다, 점검 완료·주기 변경 때는 바로 저장)
    /// - MaintenanceHistory.csv: 점검 완료, 주기 변경, 주기 도달 기록 (지우지 않음)
    /// 장비 명령에는 관여하지 않습니다. 점검 주기에 도달해도 경고 로그만 남기고 장비를 멈추지 않습니다.
    /// </summary>
    public sealed class MaintenanceCounterService : IDisposable
    {
        public const string CountersFileName = "MaintenanceCounters.xml";
        public const string HistoryFileName = "MaintenanceHistory.csv";
        public const string HistoryHeader = "Date,Time,Event,Item,Count,Limit,User,Note";
        public const string EventServiced = "점검 완료";
        public const string EventLimitChanged = "주기 변경";
        public const string EventDue = "주기 도달";
        public const double SoonRatio = 0.9;

        private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);
        private const int CollectIntervalMs = 1000;

        private readonly object sync = new object();
        private readonly HardwareUsageCounters usage;
        private readonly Action<string, string> log;
        private readonly Dictionary<string, MaintenanceCounterState> counters = new Dictionary<string, MaintenanceCounterState>();
        private readonly IReadOnlyList<MaintenanceCounterDefinition> definitions;

        private Timer timer;
        private bool dirty;
        private DateTime lastSave = DateTime.MinValue;
        private bool saveFailing;
        private bool disposed;

        public MaintenanceCounterService(string folder, HardwareUsageCounters usage, Action<string, string> log)
            : this(folder, usage, log, MaintenanceCounterItems.All)
        {
        }

        internal MaintenanceCounterService(string folder, HardwareUsageCounters usage, Action<string, string> log,
            IReadOnlyList<MaintenanceCounterDefinition> definitions)
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
            this.usage = usage;
            this.log = log;
            this.definitions = definitions;

            foreach (var d in definitions)
            {
                counters[d.Key] = new MaintenanceCounterState { Key = d.Key, Name = d.Name, Source = d.Source };
            }

            Load();
        }

        /// <summary>문서\SemiconductorUi_Logs\Maintenance</summary>
        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SemiconductorUi_Logs", "Maintenance");

        public string Folder { get; }

        public string CountersFilePath => Path.Combine(Folder, CountersFileName);

        public string HistoryFilePath => Path.Combine(Folder, HistoryFileName);

        public static MaintenanceCounterService CreateFromSettings(HardwareUsageCounters usage, Action<string, string> log)
        {
            return new MaintenanceCounterService(AppSettings.MaintenanceFolder, usage, log);
        }

        /// <summary>1초마다 가져오기를 시작합니다.</summary>
        public void Start()
        {
            lock (sync)
            {
                if (timer != null || disposed)
                {
                    return;
                }

                timer = new Timer(_ => SafeCollect(), null, CollectIntervalMs, CollectIntervalMs);
            }

            var due = GetCounters().Where(c => c.Status == MaintenanceCounterStatus.Due).ToList();
            Log($"유지보수 카운터 시작: {Folder}" + (due.Count > 0 ? $" (점검 필요 {due.Count}개)" : ""), "INFO");
            foreach (var c in due)
            {
                Log($"유지보수 점검 필요: {c.Name} 점검 후 {c.SinceService:N0}회 (주기 {c.Limit:N0}회)", "WARN");
            }
        }

        private void SafeCollect()
        {
            try
            {
                Collect(DateTime.Now);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"유지보수 카운터 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 장비 창구가 센 값을 가져와 더합니다. 점검 주기에 막 도달한 항목은 경고 로그와 이력을 남깁니다.
        /// </summary>
        public void Collect(DateTime now)
        {
            var reached = new List<MaintenanceCounterState>();
            lock (sync)
            {
                if (usage != null)
                {
                    var snapshot = usage.Drain();
                    foreach (var d in definitions)
                    {
                        long added = Math.Max(0, d.Extract(snapshot));
                        if (added == 0)
                        {
                            continue;
                        }

                        var c = counters[d.Key];
                        bool wasDue = c.Status == MaintenanceCounterStatus.Due;
                        c.Total += added;
                        c.SinceService += added;
                        dirty = true;
                        if (!wasDue && c.Status == MaintenanceCounterStatus.Due)
                        {
                            reached.Add(c.Clone());
                        }
                    }
                }

                if (dirty && now - lastSave >= SaveInterval)
                {
                    SaveLocked(now);
                }
            }

            foreach (var c in reached)
            {
                AppendHistory(now, EventDue, c.Name, c.SinceService, c.Limit, "", "");
                Log($"유지보수 점검 시기: {c.Name} 점검 후 {c.SinceService:N0}회 (주기 {c.Limit:N0}회). " +
                    "점검한 뒤 유지보수 카운터 창에서 [점검 완료]를 눌러 주세요. 장비는 멈추지 않습니다.", "WARN");
            }
        }

        public IReadOnlyList<MaintenanceCounterState> GetCounters()
        {
            lock (sync)
            {
                return definitions.Select(d => counters[d.Key].Clone()).ToList();
            }
        }

        /// <summary>
        /// 점검 완료: 점검 후 횟수를 0으로 되돌리고 이력에 남깁니다. 누적 횟수는 그대로 둡니다.
        /// </summary>
        public bool MarkServiced(string key, string user, string note, DateTime now)
        {
            MaintenanceCounterState before;
            lock (sync)
            {
                if (!counters.TryGetValue(key, out var c))
                {
                    return false;
                }

                before = c.Clone();
                c.SinceService = 0;
                c.LastServiceTime = now;
                c.LastServiceUser = user;
                dirty = true;
                SaveLocked(now);
            }

            AppendHistory(now, EventServiced, before.Name, before.SinceService, before.Limit, user, note);
            Log($"유지보수 점검 완료: {before.Name} (점검 후 {before.SinceService:N0}회 → 0, 사용자 {user})", "INFO");
            return true;
        }

        /// <summary>
        /// 점검 주기를 바꿉니다 (0 = 정하지 않음). 이력에 이전 값과 함께 남깁니다.
        /// </summary>
        public bool SetLimit(string key, long limit, string user, DateTime now)
        {
            MaintenanceCounterState after;
            long oldLimit;
            bool becameDue;
            lock (sync)
            {
                if (!counters.TryGetValue(key, out var c))
                {
                    return false;
                }

                limit = Math.Max(0, limit);
                oldLimit = c.Limit;
                if (oldLimit == limit)
                {
                    return true;
                }

                bool wasDue = c.Status == MaintenanceCounterStatus.Due;
                c.Limit = limit;
                becameDue = !wasDue && c.Status == MaintenanceCounterStatus.Due;
                after = c.Clone();
                dirty = true;
                SaveLocked(now);
            }

            AppendHistory(now, EventLimitChanged, after.Name, after.SinceService, limit, user, $"이전 주기 {FormatLimit(oldLimit)}");
            Log($"유지보수 점검 주기 변경: {after.Name} {FormatLimit(oldLimit)} → {FormatLimit(limit)} (사용자 {user})", "INFO");
            if (becameDue)
            {
                Log($"유지보수 점검 필요: {after.Name} 점검 후 {after.SinceService:N0}회 (주기 {limit:N0}회)", "WARN");
            }

            return true;
        }

        public static string FormatLimit(long limit)
        {
            return limit > 0 ? $"{limit:N0}회" : "없음";
        }

        /// <summary>지금 값을 바로 저장합니다.</summary>
        public void SaveNow()
        {
            lock (sync)
            {
                SaveLocked(DateTime.Now);
            }
        }

        /// <summary>
        /// 이력 파일의 최근 줄 (최신이 앞)
        /// </summary>
        public IReadOnlyList<MaintenanceHistoryRow> ReadHistory(int maxRows)
        {
            var rows = new List<MaintenanceHistoryRow>();
            try
            {
                if (!File.Exists(HistoryFilePath))
                {
                    return rows;
                }

                string[] lines;
                using (var fs = new FileStream(HistoryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }

                for (int i = lines.Length - 1; i >= 1 && rows.Count < maxRows; i--)
                {
                    var f = SplitCsvLine(lines[i]);
                    if (f.Count < 8)
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(f[0] + " " + f[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var time))
                    {
                        continue;
                    }

                    rows.Add(new MaintenanceHistoryRow
                    {
                        Time = time,
                        Event = f[2],
                        Item = f[3],
                        Count = f[4],
                        Limit = f[5],
                        User = f[6],
                        Note = f[7]
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"유지보수 이력 읽기 실패: {ex.Message}", "WARN");
            }

            return rows;
        }

        #region 파일

        private void Load()
        {
            string path = CountersFilePath;
            string backup = path + ".bak";
            if (!File.Exists(path) && !File.Exists(backup))
            {
                return;
            }

            if (TryLoadFrom(path, out string error))
            {
                return;
            }

            // 본 파일이 깨졌으면 직전 백업으로, 그것도 안 되면 0부터 셉니다. 깨진 파일은 지우지 않고 이름을 바꿔 둡니다.
            if (File.Exists(path))
            {
                try
                {
                    string bad = Path.Combine(Folder, $"MaintenanceCounters.bad-{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                    File.Move(path, bad);
                    Log($"유지보수 카운터 파일을 읽지 못해 {Path.GetFileName(bad)}로 옮겼습니다: {error}", "WARN");
                }
                catch (Exception ex)
                {
                    Log($"유지보수 카운터 파일을 읽지 못했습니다: {error} ({ex.Message})", "WARN");
                }
            }

            if (TryLoadFrom(backup, out string backupError))
            {
                Log("유지보수 카운터: 직전 백업(.bak)에서 값을 불러왔습니다.", "WARN");
                dirty = true;
                return;
            }

            ResetAll();
            Log("유지보수 카운터: 저장된 값을 읽지 못해 0부터 셉니다.", "WARN");
        }

        private void ResetAll()
        {
            foreach (var c in counters.Values)
            {
                c.Total = 0;
                c.SinceService = 0;
                c.Limit = 0;
                c.LastServiceTime = null;
                c.LastServiceUser = null;
            }
        }

        private bool TryLoadFrom(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "파일 없음";
                    return false;
                }

                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null || root.Name.LocalName != "MaintenanceCounters")
                {
                    error = "형식이 다름";
                    return false;
                }

                var loaded = new Dictionary<string, MaintenanceCounterState>();
                foreach (var e in root.Elements("Counter"))
                {
                    string key = (string)e.Attribute("Key");
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    var c = new MaintenanceCounterState
                    {
                        Total = ParseLong(e, "Total"),
                        SinceService = ParseLong(e, "SinceService"),
                        Limit = ParseLong(e, "Limit"),
                        LastServiceUser = (string)e.Attribute("LastServiceUser")
                    };
                    string last = (string)e.Attribute("LastServiceTime");
                    if (!string.IsNullOrEmpty(last) &&
                        DateTime.TryParseExact(last, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    {
                        c.LastServiceTime = t;
                    }

                    loaded[key] = c;
                }

                foreach (var pair in counters)
                {
                    if (loaded.TryGetValue(pair.Key, out var c))
                    {
                        pair.Value.Total = c.Total;
                        pair.Value.SinceService = c.SinceService;
                        pair.Value.Limit = c.Limit;
                        pair.Value.LastServiceTime = c.LastServiceTime;
                        pair.Value.LastServiceUser = c.LastServiceUser;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static long ParseLong(XElement e, string name)
        {
            return long.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
                ? Math.Max(0, v)
                : 0;
        }

        /// <summary>
        /// 임시 파일에 쓴 뒤 바꿔 끼웁니다. 쓰는 도중 꺼져도 이전 파일이나 .bak이 남습니다. sync 안에서 부릅니다.
        /// </summary>
        private void SaveLocked(DateTime now)
        {
            lastSave = now;
            try
            {
                Directory.CreateDirectory(Folder);
                var root = new XElement("MaintenanceCounters", new XAttribute("Saved", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                foreach (var d in definitions)
                {
                    var c = counters[d.Key];
                    var e = new XElement("Counter",
                        new XAttribute("Key", c.Key),
                        new XAttribute("Name", c.Name),
                        new XAttribute("Total", c.Total.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("SinceService", c.SinceService.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("Limit", c.Limit.ToString(CultureInfo.InvariantCulture)));
                    if (c.LastServiceTime.HasValue)
                    {
                        e.Add(new XAttribute("LastServiceTime", c.LastServiceTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                    }

                    if (!string.IsNullOrEmpty(c.LastServiceUser))
                    {
                        e.Add(new XAttribute("LastServiceUser", c.LastServiceUser));
                    }

                    root.Add(e);
                }

                string path = CountersFilePath;
                string temp = path + ".tmp";
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new XDocument(root).Save(fs);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, path + ".bak", true);
                }
                else
                {
                    File.Move(temp, path);
                }

                dirty = false;
                if (saveFailing)
                {
                    saveFailing = false;
                    Log("유지보수 카운터 저장이 다시 됩니다.", "INFO");
                }
            }
            catch (Exception ex)
            {
                if (!saveFailing)
                {
                    saveFailing = true;
                    Log($"유지보수 카운터 저장 실패 (10초 뒤 다시 시도): {ex.Message}", "WARN");
                }
            }
        }

        private void AppendHistory(DateTime time, string evt, string item, long count, long limit, string user, string note)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = HistoryFilePath;
                bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                using (var writer = new StreamWriter(fs, new UTF8Encoding(isNew)))
                {
                    if (isNew)
                    {
                        writer.WriteLine(HistoryHeader);
                    }

                    writer.WriteLine(string.Join(",", new[]
                    {
                        time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                        evt,
                        item,
                        count.ToString(CultureInfo.InvariantCulture),
                        limit.ToString(CultureInfo.InvariantCulture),
                        user ?? "",
                        note ?? ""
                    }.Select(Escape)));
                    writer.Flush();
                    fs.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Log($"유지보수 이력 기록 실패: {ex.Message}", "WARN");
            }
        }

        internal static string Escape(string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            return value.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        internal static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }

        #endregion

        private void Log(string message, string level)
        {
            try
            {
                log?.Invoke(message, level);
            }
            catch
            {
                // 로그 오류가 카운터를 멈추지 않게 합니다.
            }
        }

        /// <summary>
        /// 멈추고 남은 값을 가져와 저장합니다 (프로그램 종료 때).
        /// </summary>
        public void Dispose()
        {
            Timer t;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                t = timer;
                timer = null;
            }

            if (t != null)
            {
                // 진행 중인 가져오기가 끝날 때까지 최대 2초 기다립니다.
                // 2초 안에 끝나지 않으면 타이머가 나중에 신호를 보낼 수 있으므로 핸들을 닫지 않고 둡니다.
                var done = new ManualResetEvent(false);
                if (!t.Dispose(done) || done.WaitOne(2000))
                {
                    done.Dispose();
                }
            }

            try
            {
                Collect(DateTime.Now);
                SaveNow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"유지보수 카운터 종료 저장 오류: {ex.Message}");
            }
        }
    }
}
