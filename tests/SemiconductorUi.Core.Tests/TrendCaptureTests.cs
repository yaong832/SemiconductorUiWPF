using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using FakeHost = SemiconductorUi.Tests.ProcessEngineSimulationTests.FakeHost;
using ManualTimer = SemiconductorUi.Tests.ProcessEngineSimulationTests.ManualTimer;
using OfflineDevice = SemiconductorUi.Tests.ProcessEngineSimulationTests.OfflineDevice;

namespace SemiconductorUi.Tests
{
    /// <summary>추이 샘플 수집 (원본 Form1.CaptureTrendSampleTick, 4777c6c 기준)</summary>
    [TestClass]
    public class TrendCaptureTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 4, 10, 0, 0);

        private FakeHost _host;
        private ProcessEngine _engine;
        private SimulationController _simulation;

        [TestInitialize]
        public void Setup()
        {
            var vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "test", CurrentRole = "관리자" };
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            _simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(vm, device, hardware, _simulation, _host);
            _engine.InitializeEnvironmentTelemetry();
        }

        [TestMethod]
        public void Idle_WithoutRecipe_RecordsGapsForEveryChamber()
        {
            _engine.CaptureTrendSampleTick(T0);

            var samples = _engine.GetTrendSamples();
            CollectionAssert.AreEqual(ProcessEngine.ChamberUnitKeys, samples.Select(s => s.Unit).ToArray());
            foreach (var s in samples)
            {
                Assert.AreEqual(T0, s.Time);
                Assert.IsTrue(double.IsNaN(s.PvTemp) && double.IsNaN(s.PvPress), "공정 중이 아닌 챔버는 PV가 없습니다");
                Assert.IsTrue(double.IsNaN(s.SvTemp) && double.IsNaN(s.SvPress), "레시피를 적용하기 전에는 SV가 없습니다");
            }
        }

        [TestMethod]
        public void RecipeApplied_RecordsSpecAsSv()
        {
            _engine.RecipeApplied = true;
            _engine.CaptureTrendSampleTick(T0);

            foreach (var s in _engine.GetTrendSamples())
            {
                Assert.AreEqual(_engine.ChamberEnvSpecs[s.Unit].TargetTemperatureC, s.SvTemp);
                Assert.AreEqual(_engine.ChamberEnvSpecs[s.Unit].TargetPressureTorr, s.SvPress);
            }
        }

        [TestMethod]
        public void RecordsOncePerSecond()
        {
            _engine.CaptureTrendSampleTick(T0);
            _engine.CaptureTrendSampleTick(T0.AddMilliseconds(500));
            Assert.AreEqual(3, _engine.TrendSampleCount, "1초 안의 두 번째 틱은 기록하지 않습니다");
            _engine.CaptureTrendSampleTick(T0.AddSeconds(1));
            Assert.AreEqual(6, _engine.TrendSampleCount);
        }

        [TestMethod]
        public void ProcessingChamber_HasPv_OthersDoNot()
        {
            _engine.RecipeApplied = true;
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = 3;
            _engine.WaferLoadingButton();
            _engine.StartButton();
            for (int i = 0; i < 20000 && !_engine.IsChamberProcessing(_engine.ChamberAState); i++) _engine.OnSimulationTick();
            Assert.IsTrue(_engine.IsChamberProcessing(_engine.ChamberAState));

            _engine.ClearTrendSamples();
            _engine.CaptureTrendSampleTick(T0);
            var samples = _engine.GetTrendSamples().ToDictionary(s => s.Unit);
            Assert.AreEqual(_engine.ChamberEnvLive["PMA"].TemperatureC, samples["PMA"].PvTemp);
            Assert.AreEqual(_engine.ChamberEnvLive["PMA"].PressureTorr, samples["PMA"].PvPress);
            if (!_engine.IsChamberProcessing(_engine.ChamberCState))
            {
                Assert.IsTrue(double.IsNaN(samples["PMC"].PvTemp));
            }
        }

        [TestMethod]
        public void KeepsOnlyLastTwoHours()
        {
            _engine.CaptureTrendSampleTick(T0);
            _engine.CaptureTrendSampleTick(T0.AddHours(1));
            _engine.CaptureTrendSampleTick(T0.AddHours(2).AddSeconds(1));

            var samples = _engine.GetTrendSamples();
            Assert.AreEqual(6, samples.Count);
            Assert.AreEqual(T0.AddHours(1), samples[0].Time, "2시간보다 오래된 샘플은 버립니다");
        }

        [TestMethod]
        public void Clear_EmptiesAndRecordsAgainRightAway()
        {
            _engine.CaptureTrendSampleTick(T0);
            _engine.ClearTrendSamples();
            Assert.AreEqual(0, _engine.TrendSampleCount);
            _engine.CaptureTrendSampleTick(T0.AddMilliseconds(100));
            Assert.AreEqual(3, _engine.TrendSampleCount);
        }
    }
}
