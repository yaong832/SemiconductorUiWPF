using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Hardware;
using SemiconductorUi.Models;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using FakeHost = SemiconductorUi.Tests.ProcessEngineSimulationTests.FakeHost;
using ManualTimer = SemiconductorUi.Tests.ProcessEngineSimulationTests.ManualTimer;
using OfflineDevice = SemiconductorUi.Tests.ProcessEngineSimulationTests.OfflineDevice;

namespace SemiconductorUi.Tests
{
    /// <summary>챔버 환경 값(PV/SV), 환경 알람 판정, 레시피 적용</summary>
    [TestClass]
    public class EnvironmentAndRecipeTests
    {
        private MainFormViewModel _vm;
        private FakeHost _host;
        private ProcessEngine _engine;
        private SimulationController _simulation;

        [TestInitialize]
        public void Setup()
        {
            _vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "test", CurrentRole = "관리자" };
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            _simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(_vm, device, hardware, _simulation, _host);
            _engine.envRandom = new System.Random(1234);
        }

        private void StartAndRunUntilAProcessing(int wafers = 3)
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = wafers;
            _engine.WaferLoadingButton();
            _engine.StartButton();
            for (int i = 0; i < 20000 && !_engine.IsChamberProcessing(_engine.ChamberAState); i++) _engine.OnSimulationTick();
            _engine.OnSimulationTick();
            Assert.IsTrue(_engine.IsChamberProcessing(_engine.ChamberAState));
        }

        private static RecipeSnapshot Recipe() => new RecipeSnapshot
        {
            Name = "TEST",
            WaferCount = 4,
            DurA = 20, DurB = 30, DurC = 40,
            SecondExposure = false,
            PMA = new RecipeSnapshot.Triple { T = 24, P = 700, H = 40 },
            PMB = new RecipeSnapshot.Triple { T = 22, P = 0.01, H = 4 },
            PMC = new RecipeSnapshot.Triple { T = 120, P = 40, H = 2 },
            GasRfPMA = new RecipeSnapshot.GasRf { NF3 = 100, O2 = 50, CF4 = 25, RF = 800 },
        };

        [TestMethod]
        public void BeforeRecipe_ProcessingChamberShowsPvButNoSv()
        {
            StartAndRunUntilAProcessing();
            var rows = _engine.GetEnvironmentRows("PMA");
            Assert.AreEqual(6, rows.Length);
            Assert.IsTrue(rows.All(r => r[2] == "-"), "레시피를 적용하기 전에는 SV가 없습니다");
            Assert.AreNotEqual("-", rows.First(r => r[0] == "온도")[1], "공정 중이면 온도 PV가 나옵니다");
            Assert.AreNotEqual("-", rows.First(r => r[0] == "NF3")[1]);

            // 가스 SV가 0인 챔버(B)는 가스 PV도 "-" (원본 상태 패널과 같음)
            for (int i = 0; i < 20000 && !_engine.IsChamberProcessing(_engine.ChamberBState); i++) _engine.OnSimulationTick();
            _engine.OnSimulationTick();
            var b = _engine.GetEnvironmentRows("PMB");
            Assert.AreEqual("-", b.First(r => r[0] == "NF3")[1]);
            Assert.AreNotEqual("-", b.First(r => r[0] == "압력")[1]);

            // 공정 중이 아닌 챔버는 PV가 없다
            if (!_engine.IsChamberProcessing(_engine.ChamberCState))
                Assert.IsTrue(_engine.GetEnvironmentRows("PMC").All(r => r[1] == "-"));
        }

        [TestMethod]
        public void ApplyRecipe_SetsDurationsAndSv()
        {
            _engine.ApplyRecipeFromSnapshot(Recipe());
            _engine.RecipeApplied = true;
            _engine.UpdateSimulationUi();

            Assert.AreEqual(20, _engine.ChamberAState.Step.DurationSeconds);
            Assert.AreEqual(40, _engine.ChamberCState.Step.DurationSeconds);
            Assert.AreEqual(4, _vm.CurrentRecipeWaferCount);

            var rows = _engine.GetEnvironmentRows("PMA");
            Assert.AreEqual("24.0", rows.First(r => r[0] == "온도")[2]);
            Assert.AreEqual("700.0", rows.First(r => r[0] == "압력")[2]);
            Assert.AreEqual("100", rows.First(r => r[0] == "NF3")[2]);
            Assert.AreEqual("800", rows.First(r => r[0] == "RF")[2]);
            Assert.AreEqual("0.010", _engine.GetEnvironmentRows("PMB").First(r => r[0] == "압력")[2]);
            Assert.AreEqual("-", _engine.GetEnvironmentRows("PMB").First(r => r[0] == "NF3")[2], "SV 0은 '-'");
        }

        [TestMethod]
        public void SimulatedPv_StaysInsideWarnLimits_ForWholeRun()
        {
            _engine.ApplyRecipeFromSnapshot(Recipe());
            _engine.RecipeApplied = true;
            StartAndRunUntilAProcessing(4);
            for (int i = 0; i < 20000 && _simulation.IsRunning; i++) _engine.OnSimulationTick();
            Assert.IsFalse(_simulation.IsRunning);
            Assert.IsFalse(_host.Logs.Any(l => l.StartsWith("ALARM") || l.StartsWith("WARN")),
                string.Join("\n", _host.Logs.Where(l => l.StartsWith("ALARM") || l.StartsWith("WARN"))));
        }

        [TestMethod]
        public void TestAlarm_HoldsProcessAndMarksChamber()
        {
            StartAndRunUntilAProcessing();
            Assert.IsTrue(_engine.CanInjectSimulationTestAlarm("PMA"));
            _engine.InjectSimulationTestAlarm("PMA");

            Assert.IsTrue(_vm.ChamberAlarmStatus["PMA"]);
            Assert.IsTrue(_vm.HasAlarm);
            Assert.IsTrue(_engine.ProcessHeldByAlarm);
            Assert.AreEqual(MainFormViewModel.ProcessState.Error, _vm.CurrentProcessState);
            Assert.IsTrue(_host.Logs.Any(l => l.StartsWith("ALARM") && l.Contains("Chamber A 온도 이탈(알람)")));
            Assert.AreEqual(LampTone.Red, _engine.BuildUnitDetail("PMA").Tone);
        }

        [TestMethod]
        public void GasLeak_WithZeroSv_RaisesAlarm()
        {
            _engine.EvaluateEnvironmentAlarms("PMB", 0, 0, 0, 0, 0, 22.5, 50, 0, 0, 0, 0, 22.5);
            Assert.IsTrue(_vm.ChamberAlarmStatus["PMB"]);
            Assert.IsTrue(_host.Logs.Any(l => l.Contains("NF3 누설 의심(알람)")));
        }

        [TestMethod]
        public void Thresholds_RoundTripThroughSnapshot()
        {
            var snap = _engine.GetAlarmThresholdsSnapshot();
            snap.TempAlarm = 9.5;
            _engine.SetAlarmThresholdsFromSnapshot(snap);
            Assert.AreEqual(9.5, _engine.AlarmThresholds.TempAlarmDiffC);
            Assert.AreEqual(snap.GasLeakAlarm, _engine.GetAlarmThresholdsSnapshot().GasLeakAlarm);
        }
    }
}
