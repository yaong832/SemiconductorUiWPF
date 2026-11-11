using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 변경 이력: 바뀐 값만 남는지, 이름표가 화면과 맞는지, 비밀번호 값이 남지 않는지, 파일을 다시 읽을 수 있는지 확인합니다.
    /// </summary>
    [TestClass]
    public class ChangeHistoryTests
    {
        private string folder;
        private List<string> logs;

        [TestInitialize]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "ChangeHistoryTests_" + Guid.NewGuid().ToString("N"));
            logs = new List<string>();
            ChangeHistory.Initialize(folder, () => "kim", () => "관리자", (m, l) => logs.Add(l + " " + m));
        }

        [TestCleanup]
        public void Cleanup()
        {
            ChangeHistory.Disable();
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

        [TestMethod]
        public void Teaching_OnlyChangedValuesWithScreenNames()
        {
            var before = TeachingPositionsData.CreateDefault();
            var after = TeachingPositionsData.CreateDefault();
            after.ChamberB_X = before.ChamberB_X - 120;
            after.FoupA_LandY = (long[])before.FoupA_LandY.Clone();
            after.FoupA_LandY[2] = before.FoupA_LandY[2] + 50;
            after.DescendOffset = 20000;

            var changes = ChangeHistory.Diff("", before, after, ChangeHistory.TeachingLabel);

            Assert.AreEqual(3, changes.Count);
            Assert.AreEqual("하강 오프셋", changes[0].Item);
            Assert.AreEqual("30000", changes[0].Before);
            Assert.AreEqual("20000", changes[0].After);
            Assert.AreEqual("Chamber B X (회전)", changes[1].Item);
            Assert.AreEqual("-190823", changes[1].Before);
            Assert.AreEqual("-190943", changes[1].After);
            Assert.AreEqual("FOUP A 3층 안착 Y (Land)", changes[2].Item);
        }

        [TestMethod]
        public void SameValues_WriteNothing()
        {
            var data = TeachingPositionsData.CreateDefault();
            Assert.AreEqual(0, ChangeHistory.RecordTeaching(data, TeachingPositionsData.CreateDefault()));
            Assert.IsFalse(File.Exists(ChangeHistory.GetFilePath(DateTime.Today)));
        }

        [TestMethod]
        public void Recipes_AddRemoveAndFieldChanges()
        {
            var keep = new RecipeSnapshot { Name = "PR 코팅", WaferCount = 5, DurA = 30 };
            var gone = new RecipeSnapshot { Name = "옛 레시피", WaferCount = 1 };
            var keepEdited = new RecipeSnapshot { Name = "PR 코팅", WaferCount = 5, DurA = 45, SecondExposure = true };
            keepEdited.PMB.T = 23.5;
            keepEdited.GasRfPMC.RF = 150;
            var added = new RecipeSnapshot { Name = "새 레시피", WaferCount = 2, DurA = 10, DurB = 20, DurC = 30 };

            var changes = ChangeHistory.DiffRecipes(new[] { keep, gone }, new[] { keepEdited, added });

            Assert.IsTrue(changes.Any(c => c.Target == "PR 코팅" && c.Item == "Chamber A 공정 시간 (초)" && c.Before == "30" && c.After == "45"));
            Assert.IsTrue(changes.Any(c => c.Target == "PR 코팅" && c.Item == "2차 노광" && c.Before == "안 씀" && c.After == "사용"));
            Assert.IsTrue(changes.Any(c => c.Target == "PR 코팅" && c.Item == "Chamber B 온도 (°C)" && c.After == "23.5"));
            Assert.IsTrue(changes.Any(c => c.Target == "PR 코팅" && c.Item == "Chamber C RF" && c.After == "150"));
            Assert.IsTrue(changes.Any(c => c.Target == "새 레시피" && c.Item == "레시피 추가"));
            Assert.IsTrue(changes.Any(c => c.Target == "옛 레시피" && c.Item == "레시피 삭제"));
            Assert.AreEqual(6, changes.Count);
        }

        [TestMethod]
        public void Users_NeverStorePasswordValues()
        {
            var before = new List<User>
            {
                new User { Username = "admin", Password = "HASH-OLD", Role = "관리자" },
                new User { Username = "op1", Password = "HASH-1", Role = "작업자" }
            };
            var after = new List<User>
            {
                new User { Username = "admin", Password = "HASH-NEW", Role = "관리자" },
                new User { Username = "op2", Password = "HASH-2", Role = "작업자" }
            };
            after[0].Role = "관리자";

            ChangeHistory.RecordUsers(before, after);

            string text = File.ReadAllText(ChangeHistory.GetFilePath(DateTime.Today));
            Assert.IsFalse(text.Contains("HASH"), "비밀번호 값은 남기지 않음");
            var rows = ChangeHistory.ReadDay(DateTime.Today);
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.Any(r => r.Target == "admin" && r.Item == "비밀번호"));
            Assert.IsTrue(rows.Any(r => r.Target == "op2" && r.Item == "사용자 추가" && r.After == "작업자"));
            Assert.IsTrue(rows.Any(r => r.Target == "op1" && r.Item == "사용자 삭제"));
            Assert.IsTrue(rows.All(r => r.User == "kim" && r.Role == "관리자" && r.Category == ChangeHistory.CategoryUser));
        }

        [TestMethod]
        public void Thresholds_AndSpeeds_RoundTripThroughFile()
        {
            var t0 = new EnvThresholdSnapshot { TempWarn = 2, TempAlarm = 5, GasLeakAlarm = 3 };
            var t1 = new EnvThresholdSnapshot { TempWarn = 2.5, TempAlarm = 5, GasLeakAlarm = 3 };
            Assert.AreEqual(1, ChangeHistory.RecordThresholds(t0, t1));

            var s0 = new ServoSpeedSettings { Velocity = 1000000, MaxVelocity = 1000000, Acceleration = 1000000, Deceleration = 100000000, HomingVelocity = 300000, HomingMaxVelocity = 500000 };
            var s1 = new ServoSpeedSettings { Velocity = 800000, MaxVelocity = 800000, Acceleration = 1000000, Deceleration = 100000000, HomingVelocity = 300000, HomingMaxVelocity = 500000 };
            Assert.AreEqual(2, ChangeHistory.RecordSpeeds(s0, s1));

            var rows = ChangeHistory.ReadRange(DateTime.Today.AddDays(-6), DateTime.Today, 100);
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual(ChangeHistory.CategorySpeed, rows[0].Category, "최신이 앞");
            Assert.IsTrue(rows.Any(r => r.Item == "온도 경고 차이 (°C)" && r.Before == "2" && r.After == "2.5"));
            Assert.IsTrue(rows.Any(r => r.Item == "이송 속도" && r.Before == "1000000" && r.After == "800000"));
            Assert.IsTrue(logs.Any(l => l.StartsWith("INFO") && l.Contains("서보 속도")));
        }

        [TestMethod]
        public void CommasAndQuotes_SurviveCsv()
        {
            ChangeHistory.Record(ChangeHistory.CategoryRecipe,
                new[] { new ChangeEntry("A, \"B\"", "레시피 추가", "", "웨이퍼 1장, 시간 1/2/3초") }, DateTime.Now);

            var row = ChangeHistory.ReadDay(DateTime.Today).Single();
            Assert.AreEqual("A, \"B\"", row.Target);
            Assert.AreEqual("웨이퍼 1장, 시간 1/2/3초", row.After);
        }

        [TestMethod]
        public void NotInitialized_RecordsNothing()
        {
            ChangeHistory.Disable();
            var before = new EnvThresholdSnapshot { TempWarn = 1 };
            var after = new EnvThresholdSnapshot { TempWarn = 2 };
            Assert.AreEqual(0, ChangeHistory.RecordThresholds(before, after));
            Assert.IsFalse(Directory.Exists(folder));
        }
    }
}
