using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 바뀐 값 하나
    /// </summary>
    public sealed class ChangeEntry
    {
        public ChangeEntry(string target, string item, string before, string after)
        {
            Target = target ?? "";
            Item = item ?? "";
            Before = before ?? "";
            After = after ?? "";
        }

        /// <summary>무엇의 값인지 (예: 레시피 이름, 사용자 이름). 하나뿐이면 비움</summary>
        public string Target { get; }

        public string Item { get; }
        public string Before { get; }
        public string After { get; }
    }

    /// <summary>
    /// 변경 이력 파일의 한 줄
    /// </summary>
    public sealed class ChangeHistoryRow
    {
        public DateTime Time { get; set; }
        public string User { get; set; }
        public string Role { get; set; }
        public string Category { get; set; }
        public string Target { get; set; }
        public string Item { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    /// <summary>
    /// 변경 이력: 티칭 위치, 서보 속도, 레시피, 알람 임계값, 사용자를 저장할 때 바뀐 값만 누가 언제 바꿨는지 남깁니다.
    /// - 문서\SemiconductorUi_Logs\Changes\Change_yyyyMMdd.csv (지우지 않음)
    /// - 저장 동작은 바꾸지 않습니다. 기록이 실패해도 저장은 그대로 되고, 경고 로그만 남깁니다.
    /// - 비밀번호는 값을 남기지 않고 "바뀜"만 남깁니다.
    /// - Initialize 전에는 아무것도 기록하지 않습니다 (단위 시험에서 저장소를 써도 파일이 생기지 않게).
    /// </summary>
    public static class ChangeHistory
    {
        public const string FilePrefix = "Change_";
        public const string FileHeader = "Date,Time,User,Role,Category,Target,Item,Before,After";

        public const string CategoryTeaching = "티칭 위치";
        public const string CategorySpeed = "서보 속도";
        public const string CategoryRecipe = "레시피";
        public const string CategoryThreshold = "알람 임계값";
        public const string CategoryUser = "사용자";

        public static readonly string[] Categories =
        {
            CategoryTeaching, CategorySpeed, CategoryRecipe, CategoryThreshold, CategoryUser
        };

        private static readonly object Sync = new object();
        private static string folder;
        private static Func<string> userProvider;
        private static Func<string> roleProvider;
        private static Action<string, string> log;

        /// <summary>문서\SemiconductorUi_Logs\Changes</summary>
        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SemiconductorUi_Logs", "Changes");

        public static bool IsEnabled
        {
            get
            {
                lock (Sync)
                {
                    return folder != null;
                }
            }
        }

        public static string Folder
        {
            get
            {
                lock (Sync)
                {
                    return folder ?? DefaultFolder;
                }
            }
        }

        /// <summary>
        /// 기록을 켭니다. 메인 화면이 시작할 때 한 번 부릅니다.
        /// </summary>
        public static void Initialize(string targetFolder, Func<string> user, Func<string> role, Action<string, string> logger)
        {
            lock (Sync)
            {
                folder = string.IsNullOrWhiteSpace(targetFolder) ? DefaultFolder : targetFolder;
                userProvider = user;
                roleProvider = role;
                log = logger;
            }
        }

        /// <summary>기록을 끕니다 (시험용).</summary>
        public static void Disable()
        {
            lock (Sync)
            {
                folder = null;
                userProvider = null;
                roleProvider = null;
                log = null;
            }
        }

        public static string GetFilePath(DateTime date)
        {
            return Path.Combine(Folder, FilePrefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
        }

        #region 기록

        /// <summary>
        /// 바뀐 값들을 한 번에 남깁니다. 바뀐 것이 없으면 아무것도 하지 않습니다. 예외를 내지 않습니다.
        /// </summary>
        /// <returns>남긴 줄 수</returns>
        public static int Record(string category, IList<ChangeEntry> changes, DateTime now)
        {
            if (changes == null || changes.Count == 0)
            {
                return 0;
            }

            string user;
            string role;
            Action<string, string> logger;
            string path;
            lock (Sync)
            {
                if (folder == null)
                {
                    return 0;
                }

                user = SafeCall(userProvider);
                role = SafeCall(roleProvider);
                logger = log;
                path = Path.Combine(folder, FilePrefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");

                try
                {
                    Directory.CreateDirectory(folder);
                    bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    using (var writer = new StreamWriter(fs, new UTF8Encoding(isNew)))
                    {
                        if (isNew)
                        {
                            writer.WriteLine(FileHeader);
                        }

                        foreach (var c in changes)
                        {
                            writer.WriteLine(string.Join(",", new[]
                            {
                                now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                                user,
                                role,
                                category,
                                c.Target,
                                c.Item,
                                c.Before,
                                c.After
                            }.Select(Escape)));
                        }

                        writer.Flush();
                        fs.Flush(true);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog(logger, $"변경 이력 기록 실패 ({category} {changes.Count}건): {ex.Message}", "WARN");
                    return 0;
                }
            }

            var targets = changes.Select(c => c.Target).Where(t => t.Length > 0).Distinct().ToList();
            string targetText = targets.Count == 0 ? "" : targets.Count == 1 ? $" '{targets[0]}'" : $" {targets.Count}개";
            SafeLog(logger, $"변경 이력: {category}{targetText} {changes.Count}개 항목 (사용자 {user})", "INFO");
            return changes.Count;
        }

        /// <summary>
        /// 두 객체의 공개 값(필드·속성·배열)을 비교해 바뀐 것만 남깁니다.
        /// </summary>
        public static int RecordObjectChanges(string category, string target, object before, object after, Func<string, string> label)
        {
            try
            {
                return Record(category, Diff(target, before, after, label), DateTime.Now);
            }
            catch (Exception ex)
            {
                SafeLog(GetLogger(), $"변경 이력 비교 실패 ({category}): {ex.Message}", "WARN");
                return 0;
            }
        }

        public static int RecordTeaching(TeachingPositionsData before, TeachingPositionsData after)
        {
            return RecordObjectChanges(CategoryTeaching, "", before, after, TeachingLabel);
        }

        public static int RecordSpeeds(ServoSpeedSettings before, ServoSpeedSettings after)
        {
            return RecordObjectChanges(CategorySpeed, "", before, after, SpeedLabel);
        }

        public static int RecordThresholds(EnvThresholdSnapshot before, EnvThresholdSnapshot after)
        {
            return RecordObjectChanges(CategoryThreshold, "", before, after, ThresholdLabel);
        }

        public static int RecordRecipes(IEnumerable<RecipeSnapshot> before, IEnumerable<RecipeSnapshot> after)
        {
            try
            {
                return Record(CategoryRecipe, DiffRecipes(before, after), DateTime.Now);
            }
            catch (Exception ex)
            {
                SafeLog(GetLogger(), $"변경 이력 비교 실패 (레시피): {ex.Message}", "WARN");
                return 0;
            }
        }

        public static int RecordUsers(IEnumerable<User> before, IEnumerable<User> after)
        {
            try
            {
                return Record(CategoryUser, DiffUsers(before, after), DateTime.Now);
            }
            catch (Exception ex)
            {
                SafeLog(GetLogger(), $"변경 이력 비교 실패 (사용자): {ex.Message}", "WARN");
                return 0;
            }
        }

        #endregion

        /// <summary>
        /// 저장 전 값을 읽습니다. 읽지 못하면 기본값(null)을 돌려주고, 저장은 그대로 진행되게 합니다.
        /// </summary>
        public static T TryLoad<T>(Func<T> load) where T : class
        {
            try
            {
                return load?.Invoke();
            }
            catch (Exception ex)
            {
                SafeLog(GetLogger(), $"변경 이력: 저장 전 값을 읽지 못했습니다 ({ex.Message})", "WARN");
                return null;
            }
        }

        #region 비교

        public static List<ChangeEntry> Diff(string target, object before, object after, Func<string, string> label)
        {
            var b = new List<KeyValuePair<string, string>>();
            var a = new List<KeyValuePair<string, string>>();
            Flatten(before, "", b, 0);
            Flatten(after, "", a, 0);
            var beforeMap = new Dictionary<string, string>();
            foreach (var kv in b)
            {
                beforeMap[kv.Key] = kv.Value;
            }

            var result = new List<ChangeEntry>();
            var seen = new HashSet<string>();
            foreach (var kv in a)
            {
                seen.Add(kv.Key);
                beforeMap.TryGetValue(kv.Key, out string old);
                if (old != kv.Value)
                {
                    result.Add(new ChangeEntry(target, label != null ? label(kv.Key) : kv.Key, old ?? "(없음)", kv.Value));
                }
            }

            foreach (var kv in b)
            {
                if (!seen.Contains(kv.Key))
                {
                    result.Add(new ChangeEntry(target, label != null ? label(kv.Key) : kv.Key, kv.Value, "(없음)"));
                }
            }

            return result;
        }

        public static List<ChangeEntry> DiffRecipes(IEnumerable<RecipeSnapshot> before, IEnumerable<RecipeSnapshot> after)
        {
            var b = ByName(before);
            var a = ByName(after);
            var result = new List<ChangeEntry>();
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var old))
                {
                    result.Add(new ChangeEntry(pair.Key, "레시피 추가", "", RecipeSummary(pair.Value)));
                    continue;
                }

                result.AddRange(Diff(pair.Key, WithoutName(old), WithoutName(pair.Value), RecipeLabel));
            }

            foreach (var pair in b)
            {
                if (!a.ContainsKey(pair.Key))
                {
                    result.Add(new ChangeEntry(pair.Key, "레시피 삭제", RecipeSummary(pair.Value), ""));
                }
            }

            return result;
        }

        public static List<ChangeEntry> DiffUsers(IEnumerable<User> before, IEnumerable<User> after)
        {
            var b = (before ?? Enumerable.Empty<User>()).Where(u => u?.Username != null)
                .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var a = (after ?? Enumerable.Empty<User>()).Where(u => u?.Username != null)
                .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var result = new List<ChangeEntry>();
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var old))
                {
                    result.Add(new ChangeEntry(pair.Value.Username, "사용자 추가", "", pair.Value.Role));
                    continue;
                }

                if (!string.Equals(old.Role, pair.Value.Role, StringComparison.Ordinal))
                {
                    result.Add(new ChangeEntry(pair.Value.Username, "역할", old.Role, pair.Value.Role));
                }

                // 비밀번호는 값을 남기지 않습니다.
                if (!string.Equals(old.Password, pair.Value.Password, StringComparison.Ordinal))
                {
                    result.Add(new ChangeEntry(pair.Value.Username, "비밀번호", "", "바뀜 (값은 남기지 않음)"));
                }
            }

            foreach (var pair in b)
            {
                if (!a.ContainsKey(pair.Key))
                {
                    result.Add(new ChangeEntry(pair.Value.Username, "사용자 삭제", pair.Value.Role, ""));
                }
            }

            return result;
        }

        private static Dictionary<string, RecipeSnapshot> ByName(IEnumerable<RecipeSnapshot> list)
        {
            var map = new Dictionary<string, RecipeSnapshot>(StringComparer.Ordinal);
            foreach (var r in list ?? Enumerable.Empty<RecipeSnapshot>())
            {
                if (r == null)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(r.Name) ? "(이름없음)" : r.Name;
                if (!map.ContainsKey(name))
                {
                    map[name] = r;
                }
            }

            return map;
        }

        private static object WithoutName(RecipeSnapshot r)
        {
            // 이름은 대상(Target)으로 쓰므로 항목에서 뺍니다.
            return new
            {
                r.WaferCount,
                r.SecondExposure,
                r.DurA,
                r.DurB,
                r.DurC,
                r.PMA,
                r.PMB,
                r.PMC,
                r.GasRfPMA,
                r.GasRfPMB,
                r.GasRfPMC
            };
        }

        private static string RecipeSummary(RecipeSnapshot r)
        {
            return string.Format(CultureInfo.InvariantCulture, "웨이퍼 {0}장, 시간 A/B/C {1}/{2}/{3}초, 2차 노광 {4}",
                r.WaferCount, r.DurA, r.DurB, r.DurC, r.SecondExposure ? "사용" : "안 씀");
        }

        /// <summary>
        /// 값을 "이름 → 글자" 목록으로 펼칩니다. 순서는 선언 순서를 따릅니다.
        /// </summary>
        internal static void Flatten(object value, string prefix, List<KeyValuePair<string, string>> output, int depth)
        {
            if (depth > 6)
            {
                return;
            }

            if (value == null)
            {
                if (prefix.Length > 0)
                {
                    output.Add(new KeyValuePair<string, string>(prefix, "(없음)"));
                }
                return;
            }

            var type = value.GetType();
            if (IsSimple(type))
            {
                output.Add(new KeyValuePair<string, string>(prefix, FormatValue(value)));
                return;
            }

            if (value is IEnumerable sequence && !(value is string))
            {
                int i = 0;
                foreach (var item in sequence)
                {
                    Flatten(item, $"{prefix}[{i}]", output, depth + 1);
                    i++;
                }
                return;
            }

            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m is FieldInfo || (m is PropertyInfo p && p.CanRead && p.GetIndexParameters().Length == 0))
                .OrderBy(m => m.MetadataToken);
            foreach (var m in members)
            {
                object v = m is FieldInfo f ? f.GetValue(value) : ((PropertyInfo)m).GetValue(value, null);
                Flatten(v, prefix.Length == 0 ? m.Name : prefix + "." + m.Name, output, depth + 1);
            }
        }

        private static bool IsSimple(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);
        }

        internal static string FormatValue(object value)
        {
            if (value is bool b)
            {
                return b ? "사용" : "안 씀";
            }

            if (value is double d)
            {
                return d.ToString("0.######", CultureInfo.InvariantCulture);
            }

            if (value is float f)
            {
                return f.ToString("0.######", CultureInfo.InvariantCulture);
            }

            if (value is decimal m)
            {
                return m.ToString("0.######", CultureInfo.InvariantCulture);
            }

            if (value is DateTime t)
            {
                return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        #endregion

        #region 이름표

        private static readonly Regex IndexPattern = new Regex(@"\[(\d+)\]");

        private static string UnitName(string code)
        {
            switch (code)
            {
                case "ChamberA": return "Chamber A";
                case "ChamberB": return "Chamber B";
                case "ChamberC": return "Chamber C";
                case "FoupA": return "FOUP A";
                case "FoupB": return "FOUP B";
                case "PMA": return "Chamber A";
                case "PMB": return "Chamber B";
                case "PMC": return "Chamber C";
                default: return code;
            }
        }

        public static string TeachingLabel(string key)
        {
            if (key == "DescendOffset")
            {
                return "하강 오프셋";
            }

            if (key == "Home_X")
            {
                return "Home X";
            }

            if (key == "Home_Y")
            {
                return "Home Y";
            }

            int us = key.IndexOf('_');
            if (us < 0)
            {
                return key;
            }

            string unit = UnitName(key.Substring(0, us));
            string rest = key.Substring(us + 1);
            string floor = "";
            var m = IndexPattern.Match(rest);
            if (m.Success)
            {
                floor = $" {int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + 1}층";
                rest = rest.Substring(0, m.Index);
            }

            switch (rest)
            {
                case "X": return $"{unit} X (회전)";
                case "LandY": return $"{unit}{floor} 안착 Y (Land)";
                case "RaiseY": return $"{unit}{floor} 상승 Y (Raise)";
                default: return $"{unit}{floor} {rest}";
            }
        }

        public static string SpeedLabel(string key)
        {
            switch (key)
            {
                case "Velocity": return "이송 속도";
                case "MaxVelocity": return "이송 최대 속도";
                case "Acceleration": return "이송 가속";
                case "Deceleration": return "이송 감속";
                case "HomingVelocity": return "원점복귀 속도";
                case "HomingMaxVelocity": return "원점복귀 최대 속도";
                default: return key;
            }
        }

        public static string ThresholdLabel(string key)
        {
            switch (key)
            {
                case "TempWarn": return "온도 경고 차이 (°C)";
                case "TempAlarm": return "온도 알람 차이 (°C)";
                case "PressWarnRatio": return "압력 경고 비율";
                case "PressAlarmRatio": return "압력 알람 비율";
                case "PressWarnAbs": return "압력 경고 절대값 (Torr)";
                case "PressAlarmAbs": return "압력 알람 절대값 (Torr)";
                case "RfWarnRatio": return "RF 경고 비율";
                case "RfAlarmRatio": return "RF 알람 비율";
                case "GasWarn": return "가스 경고 절대값 (sccm)";
                case "GasAlarm": return "가스 알람 절대값 (sccm)";
                case "GasLeakWarn": return "가스 누설 경고 (sccm)";
                case "GasLeakAlarm": return "가스 누설 알람 (sccm)";
                default: return key;
            }
        }

        public static string RecipeLabel(string key)
        {
            switch (key)
            {
                case "WaferCount": return "웨이퍼 수 (장)";
                case "SecondExposure": return "2차 노광";
                case "DurA": return "Chamber A 공정 시간 (초)";
                case "DurB": return "Chamber B 공정 시간 (초)";
                case "DurC": return "Chamber C 공정 시간 (초)";
            }

            int dot = key.IndexOf('.');
            if (dot < 0)
            {
                return key;
            }

            string group = key.Substring(0, dot);
            string field = key.Substring(dot + 1);
            if (group.StartsWith("GasRf", StringComparison.Ordinal))
            {
                string unit = UnitName(group.Substring(5));
                return field == "RF" ? $"{unit} RF" : $"{unit} {field} 가스";
            }

            string chamber = UnitName(group);
            switch (field)
            {
                case "T": return $"{chamber} 온도 (°C)";
                case "P": return $"{chamber} 압력 (Torr)";
                case "H": return $"{chamber} 습도 (%)";
                default: return $"{chamber} {field}";
            }
        }

        #endregion

        #region 읽기

        /// <summary>
        /// from~to 날짜의 기록 (최신이 앞). 최대 maxRows줄.
        /// </summary>
        public static List<ChangeHistoryRow> ReadRange(DateTime from, DateTime to, int maxRows)
        {
            var rows = new List<ChangeHistoryRow>();
            for (var day = to.Date; day >= from.Date && rows.Count < maxRows; day = day.AddDays(-1))
            {
                var dayRows = ReadDay(day);
                for (int i = dayRows.Count - 1; i >= 0 && rows.Count < maxRows; i--)
                {
                    rows.Add(dayRows[i]);
                }
            }

            return rows;
        }

        /// <summary>하루치 기록 (파일 순서)</summary>
        public static List<ChangeHistoryRow> ReadDay(DateTime date)
        {
            var rows = new List<ChangeHistoryRow>();
            string path = GetFilePath(date);
            try
            {
                if (!File.Exists(path))
                {
                    return rows;
                }

                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    text = reader.ReadToEnd();
                }

                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < lines.Length; i++)
                {
                    var f = SplitCsvLine(lines[i]);
                    if (f.Count < 9)
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(f[0] + " " + f[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var time))
                    {
                        continue;
                    }

                    rows.Add(new ChangeHistoryRow
                    {
                        Time = time,
                        User = f[2],
                        Role = f[3],
                        Category = f[4],
                        Target = f[5],
                        Item = f[6],
                        Before = f[7],
                        After = f[8]
                    });
                }
            }
            catch (Exception ex)
            {
                SafeLog(GetLogger(), $"변경 이력 읽기 실패: {ex.Message}", "WARN");
            }

            return rows;
        }

        #endregion

        #region 도우미

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

        private static string SafeCall(Func<string> provider)
        {
            try
            {
                return provider?.Invoke() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static Action<string, string> GetLogger()
        {
            lock (Sync)
            {
                return log;
            }
        }

        private static void SafeLog(Action<string, string> logger, string message, string level)
        {
            try
            {
                logger?.Invoke(message, level);
            }
            catch
            {
                // 로그 오류가 저장을 막지 않게 합니다.
            }
        }

        #endregion
    }
}
