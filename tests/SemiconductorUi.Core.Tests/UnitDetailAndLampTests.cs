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
    /// <summary>원본 b4a5016 반영분: 시뮬레이션 램프 시간 점멸, 챔버/FOUP 상세 창 내용</summary>
    [TestClass]
    public class UnitDetailAndLampTests
    {
        private MainFormViewModel _vm;
        private FakeHost _host;
        private ProcessEngine _engine;
        private SimulationController _simulation;
        private long _now;

        [TestInitialize]
        public void Setup()
        {
            _vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "test", CurrentRole = "관리자" };
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            _simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(_vm, device, hardware, _simulation, _host);
            _engine.LampClockMs = () => _now;
        }

        private void StartWith(int wafers)
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = wafers;
            _engine.WaferLoadingButton();
            _engine.StartButton();
        }

        private ChamberController.ChamberState RunUntilChamberCompleted()
        {
            for (int i = 0; i < 20000 && _simulation.IsRunning; i++)
            {
                _engine.OnSimulationTick();
                var done = new[] { _engine.ChamberAState, _engine.ChamberBState, _engine.ChamberCState }
                    .FirstOrDefault(c => _engine.IsChamberCompleted(c));
                if (done != null && _engine.HasSimLampBlinking) return done;
            }
            return null;
        }

        [TestMethod]
        public void SimulationCompletedLamp_BlinksByTime_StartingOff()
        {
            StartWith(2);
            var chamber = RunUntilChamberCompleted();
            Assert.IsNotNull(chamber, "공정이 끝나 픽업을 기다리는 챔버가 있어야 합니다");

            long phase = 10L * System.Math.Max(1, AppSettings.HardwareModeTickMilliseconds);
            Assert.IsFalse(_engine.IsChamberLampOn(chamber.Region), "점멸은 꺼짐으로 시작합니다");

            _now += phase / 2;
            Assert.IsFalse(_engine.TickSimLampBlink(), "한 주기 안에서는 바뀌지 않습니다");

            _now += phase;
            Assert.IsTrue(_engine.TickSimLampBlink());
            Assert.IsTrue(_engine.IsChamberLampOn(chamber.Region), "한 주기 뒤 켜집니다");

            _now += phase;
            Assert.IsTrue(_engine.TickSimLampBlink());
            Assert.IsFalse(_engine.IsChamberLampOn(chamber.Region), "다음 주기에 다시 꺼집니다");
        }

        [TestMethod]
        public void ChamberDetail_WhileProcessing_ShowsRecipeProgressAndEnvTable()
        {
            StartWith(3);
            for (int i = 0; i < 20000 && !_engine.IsChamberProcessing(_engine.ChamberAState); i++) _engine.OnSimulationTick();
            _engine.OnSimulationTick();
            Assert.IsTrue(_engine.IsChamberProcessing(_engine.ChamberAState));

            var detail = _engine.BuildUnitDetail("PMA");
            Assert.AreEqual("Chamber A", detail.Title);
            Assert.AreEqual(LampTone.Green, detail.Tone);
            Assert.IsTrue(detail.Live);
            var info = detail.Info.ToDictionary(kv => kv.Key, kv => kv.Value);
            StringAssert.StartsWith(info["웨이퍼"], "Wafer #1");
            Assert.AreEqual("1 / 5", info["처리 순서"]);
            Assert.IsTrue(detail.Chips.Any(c => c.Key == "도어 닫힘" && c.Value));
            Assert.IsTrue(detail.Chips.Any(c => c.Key == "램프 켜짐" && c.Value));
            Assert.AreEqual(6, detail.Env.Length);
            Assert.IsTrue(detail.Progress > 0);
            StringAssert.Contains(detail.ProgressText, "공정 진행 중 (#1)");
        }

        [TestMethod]
        public void FoupDetail_ListsSlotsTopDown()
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = 3;
            _engine.WaferLoadingButton();

            var detail = _engine.BuildUnitDetail("FOUP A");
            Assert.AreEqual(AppSettings.MaxFoupCapacity, detail.Slots.Count);
            Assert.AreEqual($"슬롯 {AppSettings.MaxFoupCapacity} · 비어 있음", detail.Slots[0].Key);
            Assert.AreEqual("슬롯 1 · Wafer #1 · 대기", detail.Slots.Last().Key);
            Assert.AreEqual(3, detail.Slots.Count(s => s.Value));
            Assert.AreEqual(LampTone.Blue, detail.Tone);
            Assert.AreEqual("3장", detail.Info.First(kv => kv.Key == "웨이퍼").Value);
            Assert.AreEqual("Released", detail.Info.First(kv => kv.Key == "Lock").Value);

            var b = _engine.BuildUnitDetail("FOUP B");
            Assert.AreEqual(LampTone.Amber, b.Tone, "장착됐지만 비어 있으면 주황");
            Assert.IsTrue(b.Slots.All(s => !s.Value && s.Key.EndsWith("비어 있음")));
        }

        [TestMethod]
        public void FoupDetail_NotMounted()
        {
            var detail = _engine.BuildUnitDetail("FOUP A");
            Assert.AreEqual(LampTone.Off, detail.Tone);
            Assert.IsTrue(detail.Slots.All(s => s.Key.EndsWith("미장착") && !s.Value));
            Assert.IsTrue(detail.Chips.Any(c => c.Key == "미장착" && !c.Value));
        }
    }
}
