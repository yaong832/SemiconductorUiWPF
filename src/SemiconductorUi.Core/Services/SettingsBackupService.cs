using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 백업에 넣는 설정 파일 하나
    /// </summary>
    public sealed class SettingsBackupComponent
    {
        public SettingsBackupComponent(string key, string name, string fileName)
        {
            Key = key;
            Name = name;
            FileName = fileName;
        }

        public string Key { get; }
        public string Name { get; }
        public string FileName { get; }
    }

    /// <summary>
    /// 백업 하나 (폴더 하나)
    /// </summary>
    public sealed class SettingsBackupInfo
    {
        public string Folder { get; set; }
        public DateTime Time { get; set; }
        public string Kind { get; set; }
        public string User { get; set; }
        public string Note { get; set; }

        /// <summary>들어 있는 항목 키</summary>
        public List<string> Components { get; set; } = new List<string>();

        public string Name => Path.GetFileName(Folder);
    }

    /// <summary>
    /// 다음 시작 때 할 복원
    /// </summary>
    public sealed class PendingRestoreInfo
    {
        public string BackupFolder { get; set; }
        public List<string> Components { get; set; } = new List<string>();
        public string User { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// 시작할 때 한 일 (로그로 남김)
    /// </summary>
    public sealed class StartupNote
    {
        public StartupNote(string level, string message, bool aboutRestore = false)
        {
            Level = level;
            Message = message;
            AboutRestore = aboutRestore;
        }

        public string Level { get; }
        public string Message { get; }

        /// <summary>예약된 복원에 대한 내용 (복원함/못 함) — 켤 때 화면에도 알립니다</summary>
        public bool AboutRestore { get; }
    }

    /// <summary>
    /// 설정 백업·복원: 티칭 위치, 레시피, 알람 임계값, 사용자, 프로그램 설정(exe.config, 서보 속도 포함) 파일을 폴더째 보관합니다.
    /// - 백업: 문서\SemiconductorUi_Logs\Backups\yyyyMMdd_HHmmss_종류\ (backup.txt에 시각·종류·사용자·메모)
    /// - 자동 백업: 프로그램을 켤 때 하루 한 번, 최근 AutoBackupKeepCount개만 남김. 수동·복원전 백업은 지우지 않음
    /// - 복원은 바로 하지 않고 "다음 시작 때" 합니다. 프로그램이 파일을 읽기 전에 바꿔야 화면·장비가 쓰는 값과 파일이 어긋나지 않기 때문입니다.
    ///   복원하기 전에 지금 파일을 "복원전" 백업으로 먼저 남깁니다.
    /// </summary>
    public static class SettingsBackup
    {
        public const string KindAuto = "자동";
        public const string KindManual = "수동";
        public const string KindBeforeRestore = "복원전";
        public const string ManifestFileName = "backup.txt";
        public const string PendingRestoreFileName = "PendingRestore.txt";

        public const string KeyTeaching = "Teaching";
        public const string KeyRecipes = "Recipes";
        public const string KeyThresholds = "Thresholds";
        public const string KeyUsers = "Users";
        public const string KeyAppConfig = "AppConfig";

        /// <summary>프로그램 폴더 (설정 파일이 있는 곳)</summary>
        public static string ProgramFolder => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>문서\SemiconductorUi_Logs\Backups</summary>
        public static string DefaultBackupRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SemiconductorUi_Logs", "Backups");

        public static string BackupRoot
        {
            get
            {
                string configured = AppSettings.BackupFolder;
                return string.IsNullOrWhiteSpace(configured) ? DefaultBackupRoot : configured;
            }
        }

        public static IReadOnlyList<SettingsBackupComponent> Components => BuildComponents(AppConfigFileName);

        private static string AppConfigFileName
        {
            get
            {
                // WPF(.NET 8): AppDomainSetup.ConfigurationFile이 없어서 설정 관리자에게 파일 경로를 묻는다 (SemiconductorUi.Wpf.dll.config)
                string path = null;
                try
                {
                    path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                }
                catch (ConfigurationErrorsException)
                {
                }
                return string.IsNullOrEmpty(path) ? "SemiconductorUi.Wpf.dll.config" : Path.GetFileName(path);
            }
        }

        internal static IReadOnlyList<SettingsBackupComponent> BuildComponents(string appConfigFileName)
        {
            return new[]
            {
                new SettingsBackupComponent(KeyTeaching, "티칭 위치", "TeachingPositions.xml"),
                new SettingsBackupComponent(KeyRecipes, "레시피", "Recipes.xml"),
                new SettingsBackupComponent(KeyThresholds, "알람 임계값", "Config.xml"),
                new SettingsBackupComponent(KeyUsers, "사용자", "Users.xml"),
                new SettingsBackupComponent(KeyAppConfig, "프로그램 설정 (서보 속도 포함)", appConfigFileName)
            };
        }

        public static string ComponentName(string key)
        {
            return Components.FirstOrDefault(c => c.Key == key)?.Name ?? key;
        }

        #region 백업

        /// <summary>
        /// 지금 설정 파일을 새 백업 폴더에 복사합니다. 없는 파일은 건너뜁니다.
        /// </summary>
        public static SettingsBackupInfo CreateBackup(string kind, string user, string note, DateTime now)
        {
            return CreateBackup(ProgramFolder, BackupRoot, Components, kind, user, note, now);
        }

        internal static SettingsBackupInfo CreateBackup(string sourceFolder, string backupRoot,
            IReadOnlyList<SettingsBackupComponent> components, string kind, string user, string note, DateTime now)
        {
            Directory.CreateDirectory(backupRoot);
            string baseName = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + kind;
            string folder = Path.Combine(backupRoot, baseName);
            for (int i = 2; Directory.Exists(folder); i++)
            {
                folder = Path.Combine(backupRoot, baseName + "_" + i);
            }

            Directory.CreateDirectory(folder);
            var info = new SettingsBackupInfo { Folder = folder, Time = now, Kind = kind, User = user ?? "", Note = note ?? "" };
            foreach (var c in components)
            {
                string source = Path.Combine(sourceFolder, c.FileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                File.Copy(source, Path.Combine(folder, c.FileName), false);
                info.Components.Add(c.Key);
            }

            WriteManifest(info);
            return info;
        }

        /// <summary>
        /// 오늘 자동 백업이 없으면 만들고, 오래된 자동 백업을 지웁니다.
        /// </summary>
        /// <returns>만들었으면 그 백업, 아니면 null</returns>
        internal static SettingsBackupInfo EnsureDailyAutoBackup(string sourceFolder, string backupRoot,
            IReadOnlyList<SettingsBackupComponent> components, int keepCount, DateTime now)
        {
            var autos = ListBackups(backupRoot).Where(b => b.Kind == KindAuto).ToList();
            SettingsBackupInfo created = null;
            if (!autos.Any(b => b.Time.Date == now.Date))
            {
                created = CreateBackup(sourceFolder, backupRoot, components, KindAuto, "(자동)", "프로그램 시작", now);
                autos.Insert(0, created);
            }

            if (keepCount > 0)
            {
                foreach (var old in autos.OrderByDescending(b => b.Time).Skip(keepCount))
                {
                    try
                    {
                        Directory.Delete(old.Folder, true);
                    }
                    catch
                    {
                        // 지우지 못한 백업은 다음에 다시 시도합니다.
                    }
                }
            }

            return created;
        }

        /// <summary>백업 목록 (최신이 앞)</summary>
        public static List<SettingsBackupInfo> ListBackups()
        {
            return ListBackups(BackupRoot);
        }

        internal static List<SettingsBackupInfo> ListBackups(string backupRoot)
        {
            var list = new List<SettingsBackupInfo>();
            if (!Directory.Exists(backupRoot))
            {
                return list;
            }

            foreach (var dir in Directory.GetDirectories(backupRoot))
            {
                var info = ReadManifest(dir);
                if (info != null)
                {
                    list.Add(info);
                }
            }

            return list.OrderByDescending(b => b.Time).ThenByDescending(b => b.Name, StringComparer.Ordinal).ToList();
        }

        private static void WriteManifest(SettingsBackupInfo info)
        {
            var lines = new[]
            {
                "Time=" + info.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Kind=" + OneLine(info.Kind),
                "User=" + OneLine(info.User),
                "Note=" + OneLine(info.Note),
                "Components=" + string.Join(",", info.Components)
            };
            File.WriteAllLines(Path.Combine(info.Folder, ManifestFileName), lines, new UTF8Encoding(true));
        }

        private static SettingsBackupInfo ReadManifest(string folder)
        {
            try
            {
                string path = Path.Combine(folder, ManifestFileName);
                if (!File.Exists(path))
                {
                    return null;
                }

                var values = ReadKeyValues(path);
                if (!values.TryGetValue("Time", out string time) ||
                    !DateTime.TryParseExact(time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    return null;
                }

                values.TryGetValue("Components", out string comps);
                return new SettingsBackupInfo
                {
                    Folder = folder,
                    Time = t,
                    Kind = values.TryGetValue("Kind", out string k) ? k : "",
                    User = values.TryGetValue("User", out string u) ? u : "",
                    Note = values.TryGetValue("Note", out string n) ? n : "",
                    Components = (comps ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 복원 (다음 시작 때)

        /// <summary>
        /// 다음 시작 때 이 백업의 선택한 항목으로 되돌리도록 예약합니다.
        /// </summary>
        public static void ScheduleRestore(SettingsBackupInfo backup, IEnumerable<string> componentKeys, string user, DateTime now)
        {
            ScheduleRestore(ProgramFolder, backup, componentKeys, user, now);
        }

        internal static void ScheduleRestore(string programFolder, SettingsBackupInfo backup, IEnumerable<string> componentKeys, string user, DateTime now)
        {
            if (backup == null)
            {
                throw new ArgumentNullException(nameof(backup));
            }

            var keys = (componentKeys ?? Enumerable.Empty<string>()).Where(k => backup.Components.Contains(k)).Distinct().ToList();
            if (keys.Count == 0)
            {
                throw new InvalidOperationException("복원할 항목이 없습니다.");
            }

            var lines = new[]
            {
                "Backup=" + backup.Folder,
                "Components=" + string.Join(",", keys),
                "User=" + OneLine(user),
                "RequestedAt=" + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
            string path = Path.Combine(programFolder, PendingRestoreFileName);
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines, new UTF8Encoding(true));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        public static PendingRestoreInfo GetPendingRestore()
        {
            return GetPendingRestore(ProgramFolder);
        }

        internal static PendingRestoreInfo GetPendingRestore(string programFolder)
        {
            string path = Path.Combine(programFolder, PendingRestoreFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var values = ReadKeyValues(path);
            values.TryGetValue("Backup", out string folder);
            values.TryGetValue("Components", out string comps);
            values.TryGetValue("User", out string user);
            values.TryGetValue("RequestedAt", out string at);
            DateTime.TryParseExact(at ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedAt);
            return new PendingRestoreInfo
            {
                BackupFolder = folder ?? "",
                Components = (comps ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                User = user ?? "",
                RequestedAt = requestedAt
            };
        }

        public static bool CancelPendingRestore()
        {
            string path = Path.Combine(ProgramFolder, PendingRestoreFileName);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }

        /// <summary>
        /// 예약된 복원이 있으면 합니다: 지금 파일을 "복원전"으로 백업한 뒤, 고른 항목만 백업에서 복사합니다.
        /// 예약 파일은 성공·실패와 관계없이 지웁니다 (켤 때마다 다시 복원하지 않게).
        /// </summary>
        internal static List<StartupNote> ApplyPendingRestore(string programFolder, string backupRoot,
            IReadOnlyList<SettingsBackupComponent> components, DateTime now, out bool restored)
        {
            restored = false;
            var notes = new List<StartupNote>();
            PendingRestoreInfo pending;
            try
            {
                pending = GetPendingRestore(programFolder);
            }
            catch (Exception ex)
            {
                notes.Add(new StartupNote("WARN", $"설정 복원 예약을 읽지 못해 복원하지 않았습니다: {ex.Message}", true));
                TryDelete(Path.Combine(programFolder, PendingRestoreFileName));
                return notes;
            }

            if (pending == null)
            {
                return notes;
            }

            TryDelete(Path.Combine(programFolder, PendingRestoreFileName));

            if (string.IsNullOrEmpty(pending.BackupFolder) || !Directory.Exists(pending.BackupFolder))
            {
                notes.Add(new StartupNote("WARN", $"설정 복원을 하지 않았습니다: 백업 폴더가 없습니다 ({pending.BackupFolder})", true));
                return notes;
            }

            var targets = components.Where(c => pending.Components.Contains(c.Key)
                                                && File.Exists(Path.Combine(pending.BackupFolder, c.FileName))).ToList();
            if (targets.Count == 0)
            {
                notes.Add(new StartupNote("WARN", "설정 복원을 하지 않았습니다: 백업에 고른 항목의 파일이 없습니다.", true));
                return notes;
            }

            SettingsBackupInfo before;
            try
            {
                before = CreateBackup(programFolder, backupRoot, components, KindBeforeRestore, pending.User,
                    "복원 전 자동 보관 (백업 " + Path.GetFileName(pending.BackupFolder) + "에서 복원)", now);
            }
            catch (Exception ex)
            {
                notes.Add(new StartupNote("WARN", $"설정 복원을 하지 않았습니다: 지금 설정을 먼저 백업하지 못했습니다 ({ex.Message})", true));
                return notes;
            }

            var done = new List<string>();
            bool teachingRestored = false;
            foreach (var c in targets)
            {
                try
                {
                    string dest = Path.Combine(programFolder, c.FileName);
                    string temp = dest + ".restore.tmp";
                    File.Copy(Path.Combine(pending.BackupFolder, c.FileName), temp, true);
                    if (File.Exists(dest))
                    {
                        File.Replace(temp, dest, null, true);
                    }
                    else
                    {
                        File.Move(temp, dest);
                    }
                    done.Add(c.Name);
                    teachingRestored |= c.Key == KeyTeaching;
                }
                catch (Exception ex)
                {
                    notes.Add(new StartupNote("WARN", $"설정 복원 실패: {c.Name} ({ex.Message})", true));
                }
            }

            if (done.Count > 0)
            {
                restored = true;
                notes.Insert(0, new StartupNote("WARN",
                    $"설정 복원: {string.Join(", ", done)} ← 백업 {Path.GetFileName(pending.BackupFolder)} (요청 {pending.User}). " +
                    $"복원 전 설정은 {before.Name}에 남겼습니다.", true));
                if (teachingRestored)
                {
                    notes.Add(new StartupNote("WARN", "티칭 위치를 되돌렸습니다. 장비를 움직이기 전에 티칭 값을 확인해 주세요.", true));
                }
            }

            return notes;
        }

        /// <summary>
        /// 프로그램을 켤 때 (설정 파일을 읽기 전에) 부릅니다: 예약된 복원, 하루 한 번 자동 백업. 예외를 내지 않습니다.
        /// </summary>
        public static List<StartupNote> RunStartupTasks(DateTime now, out bool restored)
        {
            restored = false;
            var notes = new List<StartupNote>();
            try
            {
                var components = Components;
                notes.AddRange(ApplyPendingRestore(ProgramFolder, BackupRoot, components, now, out restored));
                if (restored)
                {
                    // 복원한 exe.config를 다시 읽습니다.
                    ConfigurationManager.RefreshSection("appSettings");
                }

                var auto = EnsureDailyAutoBackup(ProgramFolder, BackupRoot, components, AppSettings.AutoBackupKeepCount, now);
                if (auto != null)
                {
                    notes.Add(new StartupNote("INFO", $"설정 자동 백업: {auto.Folder} ({auto.Components.Count}개 파일)"));
                }
            }
            catch (Exception ex)
            {
                notes.Add(new StartupNote("WARN", $"설정 백업/복원 준비 중 오류: {ex.Message}"));
            }

            return notes;
        }

        #endregion

        #region 도우미

        private static Dictionary<string, string> ReadKeyValues(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                values[line.Substring(0, eq).Trim().TrimStart('﻿')] = line.Substring(eq + 1);
            }

            return values;
        }

        private static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}
