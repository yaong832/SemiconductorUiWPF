using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 설정 백업·복원 (임시 폴더에서만 동작, 프로그램 폴더는 건드리지 않음)
    /// </summary>
    [TestClass]
    public class SettingsBackupTests
    {
        private string _root;
        private string _program;
        private string _backups;
        private System.Collections.Generic.IReadOnlyList<SettingsBackupComponent> _components;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "SettingsBackupTests_" + Guid.NewGuid().ToString("N"));
            _program = Path.Combine(_root, "program");
            _backups = Path.Combine(_root, "backups");
            Directory.CreateDirectory(_program);
            _components = SettingsBackup.BuildComponents("App.exe.config");
            Write("TeachingPositions.xml", "teach-1");
            Write("Recipes.xml", "recipe-1");
            Write("Config.xml", "config-1");
            Write("App.exe.config", "appconfig-1");
            // Users.xml 없음: 없는 파일은 건너뛰어야 함
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch
            {
            }
        }

        private void Write(string name, string text)
        {
            File.WriteAllText(Path.Combine(_program, name), text);
        }

        private string Read(string name)
        {
            return File.ReadAllText(Path.Combine(_program, name));
        }

        [TestMethod]
        public void CreateBackup_CopiesExistingFilesAndListsThem()
        {
            var now = new DateTime(2026, 11, 10, 9, 30, 0);
            var info = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "메모\n두 줄", now);

            Assert.IsTrue(Directory.Exists(info.Folder));
            Assert.AreEqual("20261110_093000_수동", info.Name);
            Assert.AreEqual(4, info.Components.Count);
            Assert.IsFalse(info.Components.Contains(SettingsBackup.KeyUsers));
            Assert.AreEqual("teach-1", File.ReadAllText(Path.Combine(info.Folder, "TeachingPositions.xml")));

            var list = SettingsBackup.ListBackups(_backups);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(now, list[0].Time);
            Assert.AreEqual(SettingsBackup.KindManual, list[0].Kind);
            Assert.AreEqual("admin", list[0].User);
            Assert.AreEqual("메모 두 줄", list[0].Note);
            CollectionAssert.AreEqual(info.Components, list[0].Components);
        }

        [TestMethod]
        public void CreateBackup_SameSecondGetsSeparateFolders()
        {
            var now = new DateTime(2026, 11, 10, 9, 30, 0);
            var a = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", now);
            var b = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", now);

            Assert.AreNotEqual(a.Folder, b.Folder);
            Assert.AreEqual(2, SettingsBackup.ListBackups(_backups).Count);
        }

        [TestMethod]
        public void DailyAutoBackup_OncePerDayAndKeepsOnlyRecentAutos()
        {
            var manual = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", new DateTime(2026, 11, 1, 8, 0, 0));

            for (int day = 1; day <= 5; day++)
            {
                var created = SettingsBackup.EnsureDailyAutoBackup(_program, _backups, _components, 3, new DateTime(2026, 11, day, 9, 0, 0));
                Assert.IsNotNull(created);
                var again = SettingsBackup.EnsureDailyAutoBackup(_program, _backups, _components, 3, new DateTime(2026, 11, day, 18, 0, 0));
                Assert.IsNull(again);
            }

            var list = SettingsBackup.ListBackups(_backups);
            var autos = list.Where(b => b.Kind == SettingsBackup.KindAuto).ToList();
            Assert.AreEqual(3, autos.Count);
            Assert.AreEqual(new DateTime(2026, 11, 5, 9, 0, 0), autos[0].Time);
            Assert.AreEqual(new DateTime(2026, 11, 3, 9, 0, 0), autos[2].Time);
            Assert.IsTrue(list.Any(b => b.Folder == manual.Folder), "수동 백업은 지우지 않아야 합니다");
        }

        [TestMethod]
        public void PendingRestore_RestoresOnlySelectedAndKeepsBeforeCopy()
        {
            var backup = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", new DateTime(2026, 11, 10, 9, 0, 0));
            Write("TeachingPositions.xml", "teach-2");
            Write("Recipes.xml", "recipe-2");
            Write("App.exe.config", "appconfig-2");

            SettingsBackup.ScheduleRestore(_program, backup, new[] { SettingsBackup.KeyTeaching, SettingsBackup.KeyAppConfig, SettingsBackup.KeyUsers }, "admin", new DateTime(2026, 11, 10, 10, 0, 0));
            var pending = SettingsBackup.GetPendingRestore(_program);
            Assert.IsNotNull(pending);
            // 백업에 없는 사용자 항목은 빠짐
            CollectionAssert.AreEqual(new[] { SettingsBackup.KeyTeaching, SettingsBackup.KeyAppConfig }, pending.Components.ToArray());

            var notes = SettingsBackup.ApplyPendingRestore(_program, _backups, _components, new DateTime(2026, 11, 10, 11, 0, 0), out bool restored);

            Assert.IsTrue(restored);
            Assert.IsTrue(notes.All(n => n.AboutRestore));
            Assert.AreEqual(2, notes.Count, "복원 안내 + 티칭 확인 안내");
            Assert.AreEqual("teach-1", Read("TeachingPositions.xml"));
            Assert.AreEqual("appconfig-1", Read("App.exe.config"));
            Assert.AreEqual("recipe-2", Read("Recipes.xml"), "고르지 않은 항목은 그대로");
            Assert.IsNull(SettingsBackup.GetPendingRestore(_program), "예약은 한 번만 적용");
            Assert.IsFalse(Directory.GetFiles(_program, "*.restore.tmp").Any());

            var before = SettingsBackup.ListBackups(_backups).Single(b => b.Kind == SettingsBackup.KindBeforeRestore);
            Assert.AreEqual("teach-2", File.ReadAllText(Path.Combine(before.Folder, "TeachingPositions.xml")));
            Assert.AreEqual("appconfig-2", File.ReadAllText(Path.Combine(before.Folder, "App.exe.config")));

            // 다음에 켤 때는 아무 일도 없음
            var second = SettingsBackup.ApplyPendingRestore(_program, _backups, _components, new DateTime(2026, 11, 10, 12, 0, 0), out bool restoredAgain);
            Assert.IsFalse(restoredAgain);
            Assert.AreEqual(0, second.Count);
        }

        [TestMethod]
        public void PendingRestore_MissingBackupFolderDoesNothingAndClearsReservation()
        {
            var backup = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", new DateTime(2026, 11, 10, 9, 0, 0));
            SettingsBackup.ScheduleRestore(_program, backup, new[] { SettingsBackup.KeyRecipes }, "admin", new DateTime(2026, 11, 10, 9, 5, 0));
            Directory.Delete(backup.Folder, true);
            Write("Recipes.xml", "recipe-2");

            var notes = SettingsBackup.ApplyPendingRestore(_program, _backups, _components, new DateTime(2026, 11, 10, 10, 0, 0), out bool restored);

            Assert.IsFalse(restored);
            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual("WARN", notes[0].Level);
            Assert.IsTrue(notes[0].AboutRestore);
            Assert.AreEqual("recipe-2", Read("Recipes.xml"));
            Assert.IsNull(SettingsBackup.GetPendingRestore(_program));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ScheduleRestore_NothingToRestoreThrows()
        {
            var backup = SettingsBackup.CreateBackup(_program, _backups, _components, SettingsBackup.KindManual, "admin", "", new DateTime(2026, 11, 10, 9, 0, 0));
            SettingsBackup.ScheduleRestore(_program, backup, new[] { SettingsBackup.KeyUsers }, "admin", new DateTime(2026, 11, 10, 9, 5, 0));
        }
    }
}
