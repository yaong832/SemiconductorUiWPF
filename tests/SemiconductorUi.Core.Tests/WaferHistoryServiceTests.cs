using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 웨이퍼별 공정 이력 (임시 폴더, 시각은 테스트에서 정함)
    /// </summary>
    [TestClass]
    public class WaferHistoryServiceTests
    {
        private string _folder;
        private DateTime _now;
        private List<string> _logs;
        private WaferHistoryService _history;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "WaferHistoryTests_" + Guid.NewGuid().ToString("N"));
            _now = new DateTime(2026, 11, 10, 9, 0, 0);
            _logs = new List<string>();
            _history = new WaferHistoryService(_folder, (m, l) => _logs.Add(l + " " + m), () => _now);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch
            {
            }
        }

        private void At(int minute, int second)
        {
            _now = new DateTime(2026, 11, 10, 9, minute, second);
        }

        [TestMethod]
        public void FullRoute_WritesOneRowWithChamberTimes()
        {
            _history.LotStarted(1, "Default Recipe", true, true, "admin");
            At(0, 10); _history.NotePickup(1, "FOUP A");
            At(0, 20); _history.NoteDropoff(1, "A");
            At(0, 23); _history.NoteProcessStart(1, "A", 60);
            At(0, 24); _history.NoteProcessStart(1, "A", 60); // 중복 호출은 무시
            At(1, 23); _history.NoteProcessEnd(1, "A");
            At(1, 30); _history.NotePickup(1, "A");
            At(1, 40); _history.NoteDropoff(1, "B");
            At(1, 43); _history.NoteProcessStart(1, "B", 30);
            At(2, 13); _history.NoteProcessEnd(1, "B");
            At(2, 20); _history.NotePickup(1, "B");
            At(2, 30); _history.NoteDropoff(1, "C");
            At(2, 33); _history.NoteProcessStart(1, "C", 30);
            At(3, 5); _history.NoteProcessEnd(1, "C");
            At(3, 10); _history.NotePickup(1, "C");
            Assert.AreEqual(1, _history.GetCurrent().Count);
            At(3, 20); _history.NoteDropoff(1, "FOUP B");
            Assert.AreEqual(0, _history.GetCurrent().Count);
            _history.LotFinished();

            var rows = _history.ReadDay(_now.Date);
            Assert.AreEqual(1, rows.Count);
            var r = rows[0];
            Assert.AreEqual("20261110-090000", r.Run);
            Assert.AreEqual(1, r.WaferId);
            Assert.AreEqual("Default Recipe (2차 노광)", r.Recipe);
            Assert.AreEqual("장비", r.Mode);
            Assert.AreEqual("admin", r.User);
            Assert.AreEqual(WaferRunRecord.ResultDone, r.Result);
            Assert.AreEqual("FOUP A → A → B → C → FOUP B", r.Route);
            Assert.AreEqual(new DateTime(2026, 11, 10, 9, 0, 23), r.A.Start.Value);
            Assert.AreEqual(60.0, r.A.ActualSeconds.Value, 0.01);
            Assert.AreEqual(60, r.A.SetSeconds);
            Assert.AreEqual(32.0, r.C.ActualSeconds.Value, 0.01);
            Assert.AreEqual(190.0, r.TotalSeconds.Value, 0.01);
            Assert.IsNull(_history.CurrentRun);
        }

        [TestMethod]
        public void Abort_WritesRunningWafersAsAborted()
        {
            _history.LotStarted(3, "R1", false, false, "");
            At(0, 10); _history.NotePickup(1, "FOUP A");
            At(0, 20); _history.NoteDropoff(1, "A");
            At(0, 23); _history.NoteProcessStart(1, "A", 60);
            At(0, 30); _history.LotAborted("긴급 정지, 테스트");

            var rows = _history.ReadDay(_now.Date);
            Assert.AreEqual(1, rows.Count, "FOUP A에서 꺼내지 않은 웨이퍼는 남기지 않음");
            Assert.AreEqual(WaferRunRecord.ResultAborted, rows[0].Result);
            Assert.AreEqual("긴급 정지, 테스트", rows[0].Note);
            Assert.AreEqual("시뮬레이션", rows[0].Mode);
            Assert.IsFalse(rows[0].A.End.HasValue);
            Assert.IsTrue(_logs.Any(l => l.StartsWith("WARN") && l.Contains("중단")));

            // 이미 닫힌 뒤의 알림은 무시
            _history.NotePickup(2, "FOUP A");
            _history.LotAborted("공정 리셋");
            Assert.AreEqual(1, _history.ReadDay(_now.Date).Count);
        }

        [TestMethod]
        public void NewLot_ClosesUnfinishedPreviousLot()
        {
            _history.LotStarted(2, "R1", false, true, "op");
            At(0, 10); _history.NotePickup(1, "FOUP A");
            At(5, 0); _history.LotStarted(2, "R2", false, true, "op");

            var rows = _history.ReadDay(_now.Date);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("20261110-090000", rows[0].Run);
            Assert.AreEqual(WaferRunRecord.ResultAborted, rows[0].Result);
            Assert.AreEqual("20261110-090500", _history.CurrentRun);
        }

        [TestMethod]
        public void NotesBeforeAnyLot_AreIgnored()
        {
            _history.NotePickup(1, "FOUP A");
            _history.NoteDropoff(1, "FOUP B");
            _history.LotFinished();
            Assert.IsFalse(Directory.Exists(_folder) && Directory.GetFiles(_folder).Any());
        }

        [TestMethod]
        public void ReadRange_NewestFirstAcrossDays()
        {
            _history.LotStarted(1, "R1", false, true, "");
            _history.NotePickup(1, "FOUP A");
            _history.NoteDropoff(1, "FOUP B");
            _now = new DateTime(2026, 11, 11, 8, 0, 0);
            _history.LotStarted(1, "R2", false, true, "");
            _history.NotePickup(1, "FOUP A");
            _history.NoteDropoff(1, "FOUP B");

            var rows = _history.ReadRange(new DateTime(2026, 11, 10), new DateTime(2026, 11, 11), 100);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("R2", rows[0].Recipe);
            Assert.AreEqual("R1", rows[1].Recipe);
            Assert.AreEqual(1, _history.ReadRange(new DateTime(2026, 11, 10), new DateTime(2026, 11, 11), 1).Count);
        }

        [TestMethod]
        public void LogCallbackFailure_DoesNotThrow()
        {
            var h = new WaferHistoryService(_folder, (m, l) => { throw new InvalidOperationException("log down"); }, () => _now);
            h.LotStarted(1, "R", false, true, "");
            h.NotePickup(1, "FOUP A");
            h.LotAborted("x");
            h.LotAborted("y");
            Assert.IsNull(h.CurrentRun);
            Assert.AreEqual(1, h.ReadDay(_now.Date).Count, "로그가 실패해도 한 번만 남김");
        }
    }
}
