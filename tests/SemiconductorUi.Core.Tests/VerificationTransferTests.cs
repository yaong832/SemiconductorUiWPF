using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Models;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using FakeHost = SemiconductorUi.Tests.ProcessEngineSimulationTests.FakeHost;
using ManualTimer = SemiconductorUi.Tests.ProcessEngineSimulationTests.ManualTimer;
using OfflineDevice = SemiconductorUi.Tests.ProcessEngineSimulationTests.OfflineDevice;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 운전 화면 검증·이송 탭 훅 (원본 Form1 EvaluateInterlocks, BuildVerificationChecklist, RunVerificationChecks,
    /// NavigateToSection(Operate), AutoPlanTransferIfNeeded, GetTransferQueueLines)
    /// </summary>
    [TestClass]
    public class VerificationTransferTests
    {
        private MainFormViewModel _vm;
        private FakeHost _host;
        private ProcessEngine _engine;

        [TestInitialize]
        public void Setup()
        {
            _vm = new MainFormViewModel();
            _host = new FakeHost();
            var device = new OfflineDevice();
            var hardware = new HardwareManager(device, (m, l) => _host.AddLog(m, l));
            var simulation = new SimulationController(new ManualTimer(), (s, e) => { });
            _engine = new ProcessEngine(_vm, device, hardware, simulation, _host);
        }

        private void SetLive(string unit, double pressure, double temperature)
        {
            _engine.ChamberEnvLive[unit] = new ChamberController.ChamberEnvironmentLive
            {
                PressureTorr = pressure,
                TemperatureC = temperature
            };
        }

        [TestMethod]
        public void Offline_NoFoup_ReasonsAndChecklistMatchOriginal()
        {
            Assert.IsFalse(_engine.EvaluateInterlocks(out var reasons));
            Assert.AreEqual("EtherCAT 미연결, FOUP 미장착", reasons);

            var checks = _engine.BuildVerificationChecklist();
            CollectionAssert.AreEqual(new[]
            {
                "EtherCAT 연결", "FOUP 장착", "장비 알람/에러 없음",
                "Chamber A 환경 데이터 준비", "Chamber B 환경 데이터 준비", "Chamber C 환경 데이터 준비"
            }, checks.Select(c => c.Name).ToArray());
            Assert.AreEqual(InterlockCheckState.Fail, checks[0].State);
            Assert.AreEqual(InterlockCheckState.Fail, checks[1].State);
            Assert.AreEqual(InterlockCheckState.Pass, checks[2].State);
            // 원본처럼 ok는 false지만, 인터락 판정에는 들어가지 않는다
            Assert.IsTrue(checks.Skip(3).All(c => c.State == InterlockCheckState.NoData && !c.Ok));
        }

        [TestMethod]
        public void AlarmOrError_FailsAlarmItem()
        {
            _vm.HasAlarm = true;
            _engine.EvaluateInterlocks(out var reasons);
            StringAssert.Contains(reasons, "장비 알람/에러 상태");
            Assert.AreEqual(InterlockCheckState.Fail, _engine.BuildVerificationChecklist()[2].State);

            _vm.HasAlarm = false;
            _vm.CurrentProcessState = MainFormViewModel.ProcessState.Error;
            _engine.EvaluateInterlocks(out reasons);
            StringAssert.Contains(reasons, "장비 알람/에러 상태");
        }

        [TestMethod]
        public void Environment_UsesOriginalPressureAndTemperatureLimits()
        {
            _vm.IsFoupMounted = true;
            // PMA 760 Torr ±15, 23 °C ±10 / PMB 0.005 Torr → 5배(0.025)까지 / PMC 50 Torr ±15, 110 °C ±10
            SetLive("PMA", 775.0, 33.0);   // 경계 안
            SetLive("PMB", 0.025, 22.5);   // 경계 안
            SetLive("PMC", 50.0, 110.0);
            Assert.IsFalse(_engine.EvaluateInterlocks(out var reasons));
            Assert.AreEqual("EtherCAT 미연결", reasons);

            SetLive("PMA", 760.0, 33.5);   // 온도 이탈
            SetLive("PMB", 0.0251, 22.5);  // 진공 5배 초과
            SetLive("PMC", 65.5, 110.0);   // 압력 ±15 초과
            _engine.EvaluateInterlocks(out reasons);
            Assert.AreEqual("EtherCAT 미연결, Chamber A 온도 인터락 실패, Chamber B 압력 인터락 실패, Chamber C 압력 인터락 실패", reasons);

            var checks = _engine.BuildVerificationChecklist();
            var byName = checks.ToDictionary(c => c.Name);
            Assert.AreEqual(InterlockCheckState.Pass, byName["Chamber A 압력 범위"].State);
            Assert.AreEqual(InterlockCheckState.Fail, byName["Chamber A 온도 범위"].State);
            Assert.AreEqual(InterlockCheckState.Fail, byName["Chamber B 압력 범위"].State);
            Assert.AreEqual(InterlockCheckState.Fail, byName["Chamber C 압력 범위"].State);
            Assert.AreEqual(InterlockCheckState.Pass, byName["Chamber C 온도 범위"].State);
            StringAssert.Contains(byName["Chamber B 압력 범위"].Detail, "기준 ≤ 0.025 Torr");
            StringAssert.Contains(byName["Chamber A 온도 범위"].Detail, "PV 33.5 °C / 기준 23.0 ±10 °C");
        }

        [TestMethod]
        public void RunVerificationChecks_WarnsUnlessDismissed_AndPassResetsDismissal()
        {
            Assert.IsFalse(_engine.RunVerificationChecks());
            Assert.AreEqual("WARN Verification: 인터락 미통과 - EtherCAT 미연결, FOUP 미장착", _host.Logs.Last());

            _host.Logs.Clear();
            _vm.VerificationAlarmDismissed = true;
            Assert.IsFalse(_engine.RunVerificationChecks());
            Assert.AreEqual(0, _host.Logs.Count, "알람 리셋으로 숨긴 동안에는 다시 경고하지 않습니다");
        }

        [TestMethod]
        public void OperateEntryWarning_IsNotRepeatedForSameReasons()
        {
            _engine.WarnIfOperateInterlocksFail();
            _engine.WarnIfOperateInterlocksFail();
            Assert.AreEqual(1, _host.Logs.Count(l => l.StartsWith("WARN 운전 화면 진입: 인터락 미통과 - ")));

            _vm.IsFoupMounted = true;
            _engine.WarnIfOperateInterlocksFail();
            Assert.AreEqual("WARN 운전 화면 진입: 인터락 미통과 - EtherCAT 미연결", _host.Logs.Last());
        }

        [TestMethod]
        public void AutoPlan_LogsOnlyWhenTmIdleAndFoupHasWafers()
        {
            Assert.IsFalse(_engine.AutoPlanTransferIfNeeded());
            Assert.AreEqual(0, _host.Logs.Count);

            _vm.CurrentFoupBCount = 2;
            Assert.IsTrue(_engine.AutoPlanTransferIfNeeded());
            Assert.AreEqual("INFO 이송 계획 생성: FOUP B → Chamber A (데모)", _host.Logs.Last());

            _vm.CurrentFoupACount = 5;
            Assert.IsTrue(_engine.AutoPlanTransferIfNeeded());
            Assert.AreEqual("INFO 이송 계획 생성: FOUP A → Chamber A (데모)", _host.Logs.Last());
        }

        [TestMethod]
        public void QueueLinesAndEntries_ListQueuedTasksInOrder()
        {
            CollectionAssert.AreEqual(new[] { "[진행중 없음]" }, _engine.GetTransferQueueLines());
            Assert.AreEqual(0, _engine.GetTransferQueueEntries().Count);

            _engine.TransferService.EnqueueTransfer(new TransferController.TransferTask
            {
                Wafer = new Wafer(1, false),
                Pickup = EquipmentRegion.FoupA,
                Dropoff = EquipmentRegion.ChamberA
            });
            _engine.TransferService.EnqueueTransfer(new TransferController.TransferTask
            {
                Wafer = new Wafer(2, false),
                Pickup = EquipmentRegion.ChamberA,
                Dropoff = EquipmentRegion.ChamberB,
                RetryCount = 1
            });

            CollectionAssert.AreEqual(new[]
            {
                "[진행중 없음]",
                "FOUP A → Chamber A (웨이퍼 #1)",
                "Chamber A → Chamber B (웨이퍼 #2)"
            }, _engine.GetTransferQueueLines());

            var entries = _engine.GetTransferQueueEntries();
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(1, entries[0].Order);
            Assert.IsFalse(entries[0].IsCurrent);
            Assert.AreEqual("Chamber B", entries[1].To);
            Assert.AreEqual(1, entries[1].RetryCount);

            _engine.ClearTransferQueue();
            Assert.AreEqual(0, _engine.TransferQueueCount);
            Assert.AreEqual("INFO 이송 큐를 비웠습니다.", _host.Logs.Last());
        }
    }
}
