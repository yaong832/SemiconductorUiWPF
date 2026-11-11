using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Hardware;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using FakeHost = SemiconductorUi.Tests.ProcessEngineSimulationTests.FakeHost;
using ManualTimer = SemiconductorUi.Tests.ProcessEngineSimulationTests.ManualTimer;
using OfflineDevice = SemiconductorUi.Tests.ProcessEngineSimulationTests.OfflineDevice;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 공정 엔진이 웨이퍼 이력에 알리는 곳(시작, 집기, 놓기, 챔버 공정 시작·끝, 완료, 긴급 정지)을
    /// 시뮬레이션 모드로 끝까지 돌려 확인한다. 시각은 틱마다 1초씩 흐르게 한다.
    /// </summary>
    [TestClass]
    public class WaferHistoryEngineTests
    {
        private const int MaxTicks = 20000;

        private string _folder;
        private DateTime _now;
        private MainFormViewModel _vm;
        private FakeHost _host;
        private ProcessEngine _engine;
        private SimulationController _simulation;
        private WaferHistoryService _history;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "WaferHistoryEngineTests_" + Guid.NewGuid().ToString("N"));
            _now = new DateTime(2026, 11, 11, 9, 0, 0);
            _vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "kim", CurrentRole = "작업자" };
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            _simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(_vm, device, hardware, _simulation, _host);
            _history = new WaferHistoryService(_folder, (m, l) => _host.AddLog(m, l), () => _now);
            _engine.WaferHistory = _history;
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

        private void PrepareFoups(int wafers)
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = wafers;
            _engine.WaferLoadingButton();
        }

        private void Tick()
        {
            _engine.OnSimulationTick();
            _now = _now.AddSeconds(1);
        }

        [TestMethod]
        public void FullLot_EveryWaferIsSavedAsDone_WithRouteAndChamberTimes()
        {
            _engine.SetRecipeParameters(4, 10, 20, 20);
            PrepareFoups(3);
            _engine.StartButton();
            Assert.IsNotNull(_history.CurrentRun, "공정 시작 때 작업 번호가 생겨야 합니다");

            int ticks = 0;
            while (_simulation.IsRunning && ticks++ < MaxTicks) Tick();

            Assert.AreEqual(3, _engine.Foups.GetFoupBCount());
            Assert.IsNull(_history.CurrentRun, "공정 완료 때 작업이 닫혀야 합니다");
            Assert.AreEqual(0, _history.GetCurrent().Count);

            var rows = _history.ReadRange(_now.Date, _now.Date, 100);
            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, rows.Select(r => r.WaferId).ToArray());
            foreach (var r in rows)
            {
                Assert.AreEqual(WaferRunRecord.ResultDone, r.Result, $"웨이퍼 #{r.WaferId}");
                Assert.AreEqual("시뮬레이션", r.Mode);
                Assert.AreEqual("kim", r.User);
                Assert.IsTrue(r.FoupAOut.HasValue && r.FoupBIn.HasValue);
                Assert.IsTrue(r.Route.StartsWith("FOUP A → A → ", StringComparison.Ordinal) && r.Route.EndsWith("→ FOUP B", StringComparison.Ordinal),
                    $"웨이퍼 #{r.WaferId} 경로: {r.Route}");
                Assert.AreEqual(10, r.A.SetSeconds);
                Assert.IsTrue(r.A.ActualSeconds >= 10, $"A 실제 공정 시간 {r.A.ActualSeconds}");
                Assert.IsTrue(r.A.In <= r.A.Start && r.A.Start < r.A.End && r.A.End <= r.A.Out);
                var second = r.B.Visited ? r.B : r.C;
                Assert.IsTrue(second.Visited, "병렬 모드는 A 다음 B나 C를 거쳐야 합니다");
                Assert.AreEqual(20, second.SetSeconds);
            }
        }

        [TestMethod]
        public void EmergencyStop_DuringLot_SavesWafersInProgressAsAborted()
        {
            PrepareFoups(2);
            _engine.StartButton();

            // 첫 웨이퍼가 Chamber A에서 공정을 시작할 때까지
            int ticks = 0;
            while (_simulation.IsRunning && ticks++ < MaxTicks && !_history.GetCurrent().Any(r => r.A.Start.HasValue)) Tick();
            Assert.IsTrue(_history.GetCurrent().Any(r => r.A.Start.HasValue), "Chamber A 공정이 시작되어야 합니다");

            _engine.EmergencyStopButton();

            Assert.IsNull(_history.CurrentRun);
            var rows = _history.ReadRange(_now.Date, _now.Date, 100);
            Assert.IsTrue(rows.Count >= 1);
            Assert.IsTrue(rows.All(r => r.Result == WaferRunRecord.ResultAborted));
            Assert.IsTrue(rows.All(r => r.Note.StartsWith("비상정지", StringComparison.Ordinal)), string.Join(" | ", rows.Select(r => r.Note)));
            var first = rows.Single(r => r.A.Start.HasValue);
            Assert.IsFalse(first.A.End.HasValue, "공정 중 중단이면 끝 시각이 없어야 합니다");
        }
    }
}
