using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 유지보수 카운터: 출력이 켜질 때만 세는지, 점검 주기 도달·점검 완료가 이력에 남는지, 파일이 깨져도 이어지는지 확인합니다.
    /// </summary>
    [TestClass]
    public class MaintenanceCounterServiceTests
    {
        private string folder;
        private List<string> logs;

        [TestInitialize]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "MaintCounterTests_" + Guid.NewGuid().ToString("N"));
            logs = new List<string>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch
            {
            }
        }

        private MaintenanceCounterService Create(HardwareUsageCounters usage)
        {
            return new MaintenanceCounterService(folder, usage, (m, l) => logs.Add(l + " " + m));
        }

        private static MaintenanceCounterState Get(MaintenanceCounterService service, string key)
        {
            return service.GetCounters().Single(c => c.Key == key);
        }

        [TestMethod]
        public void UsageCounters_CountOnlyOffToOnAndAxisCommands()
        {
            var usage = new HardwareUsageCounters();
            usage.NoteOutput(12, false, true);  // 전진: 셈
            usage.NoteOutput(12, true, true);   // 같은 값 다시 보냄: 안 셈
            usage.NoteOutput(12, true, false);  // 끔: 안 셈
            usage.NoteOutput(12, false, true);  // 다시 전진: 셈
            usage.NoteOutput(40, false, true);  // 범위 밖: 무시
            usage.NoteOutput(-1, false, true);
            usage.NoteAxisCommand(1);
            usage.NoteAxisCommand(1);
            usage.NoteAxisCommand(2);
            usage.NoteAxisCommand(3);

            var first = usage.Drain();
            Assert.AreEqual(2L, first.GetOutputOnEdges(12));
            Assert.AreEqual(0L, first.GetOutputOnEdges(13));
            Assert.AreEqual(2L, first.Axis1Commands);
            Assert.AreEqual(1L, first.Axis2Commands);

            var second = usage.Drain();
            Assert.AreEqual(0L, second.GetOutputOnEdges(12));
            Assert.AreEqual(0L, second.Axis1Commands);
        }

        [TestMethod]
        public void Collect_AddsCounts_AndSurvivesRestart()
        {
            var usage = new HardwareUsageCounters();
            var now = new DateTime(2026, 11, 10, 9, 0, 0);
            using (var service = Create(usage))
            {
                for (int i = 0; i < 3; i++)
                {
                    usage.NoteOutput(MaintenanceCounterItems.CylinderExtendOutput, false, true);
                }
                usage.NoteOutput(MaintenanceCounterItems.DoorBOpenOutput, false, true);
                usage.NoteAxisCommand(2);
                service.Collect(now);

                Assert.AreEqual(3L, Get(service, "CylinderExtend").Total);
                Assert.AreEqual(3L, Get(service, "CylinderExtend").SinceService);
                Assert.AreEqual(1L, Get(service, "DoorB").Total);
                Assert.AreEqual(0L, Get(service, "DoorA").Total);
                Assert.AreEqual(1L, Get(service, "Axis2").Total);
            }

            using (var again = Create(new HardwareUsageCounters()))
            {
                Assert.AreEqual(3L, Get(again, "CylinderExtend").Total);
                Assert.AreEqual(1L, Get(again, "DoorB").SinceService);
                Assert.AreEqual(1L, Get(again, "Axis2").Total);
            }
        }

        [TestMethod]
        public void ReachingLimit_WarnsOnceAndWritesHistory()
        {
            var usage = new HardwareUsageCounters();
            var now = new DateTime(2026, 11, 10, 9, 0, 0);
            using (var service = Create(usage))
            {
                service.SetLimit("Vacuum", 3, "admin", now);
                Assert.AreEqual(MaintenanceCounterStatus.Normal, Get(service, "Vacuum").Status);

                usage.NoteOutput(MaintenanceCounterItems.VacuumOutput, false, true);
                usage.NoteOutput(MaintenanceCounterItems.VacuumOutput, false, true);
                service.Collect(now.AddSeconds(1));
                Assert.AreEqual(0, logs.Count(l => l.StartsWith("WARN")));

                usage.NoteOutput(MaintenanceCounterItems.VacuumOutput, false, true);
                service.Collect(now.AddSeconds(2));
                Assert.AreEqual(MaintenanceCounterStatus.Due, Get(service, "Vacuum").Status);
                Assert.AreEqual(1, logs.Count(l => l.StartsWith("WARN") && l.Contains("진공 흡착")));

                usage.NoteOutput(MaintenanceCounterItems.VacuumOutput, false, true);
                service.Collect(now.AddSeconds(3));
                Assert.AreEqual(1, logs.Count(l => l.StartsWith("WARN")), "도달 경고는 한 번만");

                var history = service.ReadHistory(10);
                Assert.AreEqual(2, history.Count);
                Assert.AreEqual(MaintenanceCounterService.EventDue, history[0].Event);
                Assert.AreEqual("3", history[0].Count);
                Assert.AreEqual(MaintenanceCounterService.EventLimitChanged, history[1].Event);
                Assert.AreEqual("admin", history[1].User);
            }
        }

        [TestMethod]
        public void MarkServiced_ResetsSinceService_KeepsTotal_AndRecordsUser()
        {
            var usage = new HardwareUsageCounters();
            var now = new DateTime(2026, 11, 10, 9, 0, 0);
            using (var service = Create(usage))
            {
                for (int i = 0; i < 5; i++)
                {
                    usage.NoteOutput(MaintenanceCounterItems.DoorAOpenOutput, false, true);
                }
                service.Collect(now);
                service.SetLimit("DoorA", 4, "admin", now);
                Assert.AreEqual(MaintenanceCounterStatus.Due, Get(service, "DoorA").Status);

                Assert.IsTrue(service.MarkServiced("DoorA", "kim", "실린더 패킹 교체, \"A\" 도어", now.AddMinutes(1)));
                var c = Get(service, "DoorA");
                Assert.AreEqual(0L, c.SinceService);
                Assert.AreEqual(5L, c.Total);
                Assert.AreEqual("kim", c.LastServiceUser);
                Assert.AreEqual(now.AddMinutes(1), c.LastServiceTime);
                Assert.AreEqual(MaintenanceCounterStatus.Normal, c.Status);

                var row = service.ReadHistory(10)[0];
                Assert.AreEqual(MaintenanceCounterService.EventServiced, row.Event);
                Assert.AreEqual("5", row.Count);
                Assert.AreEqual("4", row.Limit);
                Assert.AreEqual("kim", row.User);
                Assert.AreEqual("실린더 패킹 교체, \"A\" 도어", row.Note);
                Assert.IsFalse(service.MarkServiced("NoSuchItem", "kim", "", now));
            }
        }

        [TestMethod]
        public void BrokenFile_UsesBackupOrStartsFromZero_AndKeepsBrokenFile()
        {
            var usage = new HardwareUsageCounters();
            var now = new DateTime(2026, 11, 10, 9, 0, 0);
            using (var service = Create(usage))
            {
                usage.NoteAxisCommand(1);
                service.Collect(now);
                service.SaveNow();          // 첫 저장
                usage.NoteAxisCommand(1);
                service.Collect(now.AddSeconds(1));
                service.SaveNow();          // 두 번째 저장: 첫 저장이 .bak으로 남음
            }

            string path = Path.Combine(folder, MaintenanceCounterService.CountersFileName);
            Assert.IsTrue(File.Exists(path + ".bak"));
            File.WriteAllText(path, "<MaintenanceCounters><Counter Key=");

            using (var fromBackup = Create(new HardwareUsageCounters()))
            {
                Assert.IsTrue(Get(fromBackup, "Axis1").Total >= 1L, "백업에서 불러옴");
            }
            Assert.IsTrue(Directory.GetFiles(folder, "MaintenanceCounters.bad-*.xml").Length == 1, "깨진 파일은 이름을 바꿔 남김");

            foreach (var f in Directory.GetFiles(folder, "MaintenanceCounters.xml*"))
            {
                File.Delete(f);
            }
            File.WriteAllText(path, "not xml");
            using (var fresh = Create(new HardwareUsageCounters()))
            {
                Assert.AreEqual(0L, Get(fresh, "Axis1").Total);
                Assert.IsTrue(logs.Any(l => l.Contains("0부터")));
            }
        }
    }
}
