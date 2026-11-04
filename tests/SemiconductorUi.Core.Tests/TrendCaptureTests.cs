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
    /// <summary>추이 샘플 수집 (원본 Form1.CaptureTrendSampleTick)</summary>
    [TestClass]
    public class TrendCaptureTests
    {
        private FakeHost _host;
        private ProcessEngine _engine;

        [TestInitialize]
        public void Setup()
        {
            var vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "test", CurrentRole = "관리자" };
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            var simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(vm, device, hardware, simulation, _host);
            _engine.InitializeEnvironmentTelemetry();
        }

        [TestMethod]
        public void NotCapturing_TickAddsNothing()
        {
            _engine.CaptureTrendSampleTick(DateTime.Now);
            Assert.AreEqual(0, _engine.TrendSampleCount);
        }

        [TestMethod]
        public void Capturing_TickAddsOneSamplePerChamber_WithSpecAsSv()
        {
            _engine.StartTrendCapture();
            var t = new DateTime(2026, 11, 4, 10, 0, 0);
            _engine.CaptureTrendSampleTick(t);

            var samples = _engine.GetTrendSamples();
            CollectionAssert.AreEqual(ProcessEngine.ChamberUnitKeys, samples.Select(s => s.Unit).ToArray());
            foreach (var s in samples)
            {
                Assert.AreEqual(t, s.Time);
                Assert.AreEqual(_engine.ChamberEnvSpecs[s.Unit].TargetTemperatureC, s.SvTemp);
                Assert.AreEqual(_engine.ChamberEnvSpecs[s.Unit].TargetPressureTorr, s.SvPress);
                Assert.AreEqual(_engine.ChamberEnvLive[s.Unit].TemperatureC, s.PvTemp);
            }
        }

        [TestMethod]
        public void OverLimit_DropsOldestBatch()
        {
            _engine.StartTrendCapture();
            var t0 = new DateTime(2026, 11, 4, 10, 0, 0);
            int ticks = ProcessEngine.TrendSampleLimit / ProcessEngine.ChamberUnitKeys.Length + 1;
            for (int i = 0; i < ticks; i++) _engine.CaptureTrendSampleTick(t0.AddSeconds(i));

            Assert.IsTrue(_engine.TrendSampleCount <= ProcessEngine.TrendSampleLimit);
            var samples = _engine.GetTrendSamples();
            Assert.IsTrue(samples[0].Time > t0, "가장 오래된 샘플부터 버린다");
            Assert.AreEqual(t0.AddSeconds(ticks - 1), samples[samples.Count - 1].Time);
        }

        [TestMethod]
        public void StartStop_LogOnceAndStopHaltsCapture()
        {
            _engine.StartTrendCapture();
            _engine.StartTrendCapture();
            _engine.CaptureTrendSampleTick(DateTime.Now);
            _engine.StopTrendCapture();
            _engine.CaptureTrendSampleTick(DateTime.Now);

            Assert.AreEqual(ProcessEngine.ChamberUnitKeys.Length, _engine.TrendSampleCount);
            Assert.AreEqual(1, _host.Logs.Count(l => l.Contains("Trend 캡처 시작")));
            Assert.IsTrue(_host.Logs.Any(l => l.Contains("Trend 캡처 중지 (샘플 3개)")));

            _engine.ClearTrendSamples();
            Assert.AreEqual(0, _engine.TrendSampleCount);
        }
    }
}
