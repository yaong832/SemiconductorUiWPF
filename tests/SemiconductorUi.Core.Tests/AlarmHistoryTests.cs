using System;
using System.IO;
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
    /// <summary>알람 이력 (원본 Services/AlarmHistoryService, 8baff6e)</summary>
    [TestClass]
    public class AlarmHistoryTests
    {
        private string _folder;
        private AlarmHistoryService _service;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "AlarmHistoryTests_" + Guid.NewGuid().ToString("N"));
            _service = new AlarmHistoryService(new DailyLogFileWriter(_folder, 0, AlarmHistoryService.FilePrefix, AlarmHistoryService.FileHeader));
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service.Dispose();
            try { Directory.Delete(_folder, true); } catch { }
        }

        [TestMethod]
        public void Raise_SameCodeAndUnitWhileActive_RecordsOnce()
        {
            var t = new DateTime(2026, 11, 11, 9, 0, 0);
            Assert.IsTrue(_service.Raise(2101, "Chamber A", "PV=31", t));
            Assert.IsFalse(_service.Raise(2101, "Chamber A", "PV=32", t.AddSeconds(1)));
            Assert.IsTrue(_service.Raise(2101, "Chamber B", "PV=31", t.AddSeconds(2)), "다른 유닛은 따로 기록");
            Assert.AreEqual(2, _service.ActiveCount);
            Assert.AreEqual(2, _service.ReadDay(t).Count);
        }

        [TestMethod]
        public void ClearEmergencyStopGroup_LeavesEnvironmentAlarms()
        {
            var t = new DateTime(2026, 11, 11, 9, 0, 0);
            _service.Raise(AlarmCodes.EmergencyStop, "장비", "버튼", t);
            _service.Raise(AlarmCodes.ServoFaultAxis2, "TM", "상태워드", t);
            _service.Raise(AlarmCodes.ForEnvironment("PMC", AlarmCodes.EnvRf), "Chamber C", "RF", t);

            Assert.AreEqual(2, _service.Clear(a => AlarmCodes.IsEmergencyStopGroup(a.Code), "operator", t.AddMinutes(1)));
            var active = _service.GetActive();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(2303, active[0].Code);

            var cleared = _service.ReadDay(t).Where(r => r.Event == AlarmHistoryService.EventClear).ToList();
            Assert.AreEqual(2, cleared.Count);
            Assert.IsTrue(cleared.All(r => r.User == "operator"));
        }

        [TestMethod]
        public void ReadDay_RoundTripsCommasQuotesAndNewlines()
        {
            var t = new DateTime(2026, 11, 11, 9, 0, 0, 250);
            _service.Raise(1201, "TM", "축 \"1\" 오류,\n다시 시도", t);
            var row = _service.ReadDay(t).Single();
            Assert.AreEqual("2026-11-11", row.Date);
            Assert.AreEqual("09:00:00.250", row.Time);
            Assert.AreEqual(AlarmHistoryService.EventSet, row.Event);
            Assert.AreEqual("1201", row.Code);
            Assert.AreEqual("TM 하드웨어 오류 (자동 일시정지)", row.Name);
            Assert.AreEqual("축 \"1\" 오류, 다시 시도", row.Detail);
        }

        [TestMethod]
        public void ReadDay_NoFile_ReturnsEmpty()
        {
            Assert.AreEqual(0, _service.ReadDay(new DateTime(2020, 1, 1)).Count);
        }

        [TestMethod]
        public void AlarmCodes_EnvironmentCodes()
        {
            Assert.AreEqual(2101, AlarmCodes.ForEnvironment("PMA", AlarmCodes.EnvTemperature));
            Assert.AreEqual(2208, AlarmCodes.ForEnvironment("pmb", AlarmCodes.GasLeak("O2")));
            Assert.AreEqual(2306, AlarmCodes.ForEnvironment("PMC", AlarmCodes.GasFlow("CF4")));
            Assert.AreEqual("O2 누설 의심", AlarmCodes.NameOf(2208));
        }

        private ProcessEngine CreateEngine(MainFormViewModel vm)
        {
            var host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => host.AddLog(m, l));
            var simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            var engine = new ProcessEngine(vm, device, hardware, simulation, host);
            engine.InitializeEnvironmentTelemetry();
            engine.AlarmHistory = _service;
            return engine;
        }

        [TestMethod]
        public void Engine_EnvironmentAlarm_RecordedOnceUntilReset_ThenClearedByUser()
        {
            var vm = new MainFormViewModel { IsLoggedIn = true, CurrentUser = "kim", CurrentRole = "관리자" };
            var engine = CreateEngine(vm);

            // 온도만 크게 벗어남 (가스 SV=PV, 압력 SV=PV)
            for (int i = 0; i < 3; i++)
            {
                engine.EvaluateEnvironmentAlarms("PMA", 100, 50, 30, 0.5, 300, 23, 100, 50, 30, 0.5, 300, 80);
            }
            var active = _service.GetActive();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(2101, active[0].Code);
            Assert.AreEqual("Chamber A", active[0].Unit);

            engine.ResetAlarmButton();
            Assert.AreEqual(0, _service.ActiveCount);
            var clear = _service.ReadDay(DateTime.Today).Last();
            Assert.AreEqual(AlarmHistoryService.EventClear, clear.Event);
            Assert.AreEqual("kim", clear.User);
        }
    }
}
