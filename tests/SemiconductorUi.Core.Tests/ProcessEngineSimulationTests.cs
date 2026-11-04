using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Hardware;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 원본 Form1에서 옮긴 공정 엔진을 EtherCAT 없이(시뮬레이션 모드) 처음부터 끝까지 돌려 본다.
    /// 타이머 대신 OnSimulationTick()을 직접 부른다 (1틱 = 챔버 공정 1초).
    /// </summary>
    [TestClass]
    public class ProcessEngineSimulationTests
    {
        private const int MaxTicks = 20000;

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
        }

        private void PrepareFoups(int wafers)
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = wafers;
            _engine.WaferLoadingButton();
        }

        private int RunUntilFinished()
        {
            int ticks = 0;
            while (_simulation.IsRunning && ticks < MaxTicks)
            {
                _engine.OnSimulationTick();
                ticks++;
            }
            return ticks;
        }

        [TestMethod]
        public void ParallelMode_AllWafersEndInFoupB_AndProcessReturnsToIdle()
        {
            PrepareFoups(3);
            Assert.AreEqual(3, _engine.Foups.GetFoupACount());

            _engine.StartButton();
            Assert.IsTrue(_simulation.IsRunning, "시작 후 공정이 진행 중이어야 합니다");
            Assert.AreEqual(MainFormViewModel.ProcessState.Running, _vm.CurrentProcessState);

            int ticks = RunUntilFinished();

            Assert.IsFalse(_simulation.IsRunning, $"{ticks}틱 안에 공정이 끝나야 합니다");
            Assert.AreEqual(3, _engine.Foups.GetFoupBCount());
            Assert.AreEqual(0, _engine.Foups.GetFoupACount());
            Assert.IsNull(_engine.ChamberAState.CurrentWafer);
            Assert.IsNull(_engine.ChamberBState.CurrentWafer);
            Assert.IsNull(_engine.ChamberCState.CurrentWafer);
            Assert.AreEqual(MainFormViewModel.ProcessState.Idle, _vm.CurrentProcessState);
            Assert.IsTrue(_host.Logs.Any(l => l.Contains("시뮬레이션이 완료되었습니다")));
            Assert.IsFalse(_host.Logs.Any(l => l.StartsWith("ERROR") || l.StartsWith("ALARM")),
                "정상 공정에는 오류 로그가 없어야 합니다:\n" + string.Join("\n", _host.Logs.Where(l => l.StartsWith("ERROR") || l.StartsWith("ALARM"))));
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, _engine.Foups.FoupBCompleted.Select(w => w.Id).ToArray());
        }

        [TestMethod]
        public void ParallelMode_UsesChamberC_WhenBIsBusy()
        {
            // A가 B보다 훨씬 빨리 끝나면 B가 공정 중일 때 다음 웨이퍼는 C로 간다
            _engine.SetRecipeParameters(4, 10, 90, 90);
            PrepareFoups(4);
            _engine.StartButton();

            var seen = new HashSet<string>();
            int ticks = 0;
            while (_simulation.IsRunning && ticks++ < MaxTicks)
            {
                _engine.OnSimulationTick();
                if (_engine.ChamberBState.CurrentWafer != null) seen.Add("B");
                if (_engine.ChamberCState.CurrentWafer != null) seen.Add("C");
            }

            Assert.AreEqual(4, _engine.Foups.GetFoupBCount());
            CollectionAssert.AreEquivalent(new[] { "B", "C" }, seen.ToArray(), "병렬 모드는 B와 C를 함께 써야 합니다");
        }

        [TestMethod]
        public void SecondExposureMode_EveryWaferGoesThroughBThenC()
        {
            _vm.SecondExposureEnabled = true;
            PrepareFoups(2);
            _engine.StartButton();

            var visits = new Dictionary<int, List<string>>();
            void Note(ChamberController.ChamberState chamber, string name)
            {
                var wafer = chamber.CurrentWafer;
                if (wafer == null) return;
                if (!visits.TryGetValue(wafer.Id, out var list)) visits[wafer.Id] = list = new List<string>();
                if (list.Count == 0 || list[list.Count - 1] != name) list.Add(name);
            }

            int ticks = 0;
            while (_simulation.IsRunning && ticks++ < MaxTicks)
            {
                _engine.OnSimulationTick();
                Note(_engine.ChamberAState, "A");
                Note(_engine.ChamberBState, "B");
                Note(_engine.ChamberCState, "C");
            }

            Assert.AreEqual(2, _engine.Foups.GetFoupBCount());
            foreach (var kv in visits)
            {
                CollectionAssert.AreEqual(new[] { "A", "B", "C" }, kv.Value.ToArray(), $"웨이퍼 #{kv.Key} 경로");
            }
        }

        [TestMethod]
        public void Start_WithoutFoupMounted_ShowsWarningAndDoesNotRun()
        {
            _engine.StartButton();

            Assert.IsFalse(_simulation.IsRunning);
            Assert.IsTrue(_host.Messages.Any(m => m.Contains("FOUP A/B 장착 상태")));
        }

        [TestMethod]
        public void Start_WithoutLoading_ShowsWarningAndDoesNotRun()
        {
            _engine.ToggleFoupMountButton();
            _engine.StartButton();

            Assert.IsFalse(_simulation.IsRunning);
            Assert.IsTrue(_host.Messages.Any(m => m.Contains("'로딩' 상태")));
        }

        [TestMethod]
        public void Pause_StopsProgress_AndStartResumes()
        {
            PrepareFoups(2);
            _engine.StartButton();
            for (int i = 0; i < 40; i++) _engine.OnSimulationTick();

            _engine.PauseButton();
            Assert.AreEqual(MainFormViewModel.ProcessState.Paused, _vm.CurrentProcessState);
            int elapsed = _simulation.ElapsedSeconds;
            var phase = _engine.TmPhase;
            int remainingA = _engine.ChamberAState.RemainingSeconds;
            for (int i = 0; i < 20; i++) _engine.OnSimulationTick();
            Assert.AreEqual(elapsed, _simulation.ElapsedSeconds, "일시 정지 중에는 시간이 흐르지 않아야 합니다");
            Assert.AreEqual(phase, _engine.TmPhase);
            Assert.AreEqual(remainingA, _engine.ChamberAState.RemainingSeconds);

            _engine.StartButton();
            Assert.AreEqual(MainFormViewModel.ProcessState.Running, _vm.CurrentProcessState);
            RunUntilFinished();
            Assert.AreEqual(2, _engine.Foups.GetFoupBCount());
        }

        [TestMethod]
        public void EmergencyStop_AbortsProcess_WithErrorState()
        {
            PrepareFoups(2);
            _engine.StartButton();
            for (int i = 0; i < 10; i++) _engine.OnSimulationTick();

            _engine.EmergencyStopButton();

            Assert.IsFalse(_simulation.IsRunning);
            Assert.AreEqual(MainFormViewModel.ProcessState.Error, _vm.CurrentProcessState);
            Assert.IsTrue(_engine.MainLampRed);
        }

        [TestMethod]
        public void ResetProcess_ClearsChambersAndQueue()
        {
            PrepareFoups(3);
            _engine.StartButton();
            for (int i = 0; i < 60; i++) _engine.OnSimulationTick();
            _engine.PauseButton();
            _engine.EmergencyStopButton();

            _engine.ResetProcessButton();

            Assert.AreEqual(MainFormViewModel.ProcessState.Idle, _vm.CurrentProcessState);
            Assert.IsNull(_engine.ChamberAState.CurrentWafer);
            Assert.IsNull(_engine.CurrentTransfer);
            Assert.AreEqual(0, _engine.TransferQueueCount);
            Assert.AreEqual(TransferController.TmPhase.Idle, _engine.TmPhase);
        }

        [TestMethod]
        public void WaferIsDrawnOnBladeOnlyAfterPickup()
        {
            // 원본 9415660: 블레이드 웨이퍼는 픽업 뒤부터 보인다
            PrepareFoups(1);
            _engine.StartButton();

            bool sawMoveToPickup = false;
            int ticks = 0;
            while (_simulation.IsRunning && ticks++ < MaxTicks)
            {
                _engine.OnSimulationTick();
                var phase = _engine.TmPhase;
                if (phase == TransferController.TmPhase.MoveToPickup || phase == TransferController.TmPhase.PickupExtend)
                {
                    sawMoveToPickup = true;
                    Assert.IsFalse(_engine.IsWaferOnBlade(), $"{phase} 단계에는 블레이드에 웨이퍼가 없어야 합니다");
                }
                if (phase == TransferController.TmPhase.MoveToDropoff)
                {
                    Assert.IsTrue(_engine.IsWaferOnBlade(), "반입 이동 중에는 블레이드에 웨이퍼가 있어야 합니다");
                }
            }
            Assert.IsTrue(sawMoveToPickup);
        }

        internal sealed class FakeHost : IProcessHost
        {
            public List<string> Logs { get; } = new List<string>();
            public List<string> Messages { get; } = new List<string>();
            public int? WaferCountAnswer { get; set; }
            private bool _headerAlarm;

            public void AddLog(string message, string level)
            {
                Logs.Add($"{level} {message}");
                if (level == "ALARM" || level == "ERROR" || level == "WARN") _headerAlarm = true;
            }

            public void ShowMessage(string text, string title, MessageKind kind) => Messages.Add(text);
            public bool Confirm(string text, string title, bool defaultNo = false) => true;
            public int? PromptWaferCount(int current, int min, int max) => WaferCountAnswer;
            public bool HasHeaderAlarm() => _headerAlarm;
            public void ClearHeaderAlarm() => _headerAlarm = false;
            public void BeginInvoke(Action action) => action();
            public bool CheckAccess() => true;
            public void Invoke(Action action) => action();
            public void RunOnceAfter(int delayMs, Action action) { }
            public void RefreshUi() { }
            public void RefreshServoStatus() { }
        }

        internal sealed class ManualTimer : ITickTimer
        {
            public event EventHandler Tick { add { } remove { } }
            public int Interval { get; set; }
            public bool Enabled { get; private set; }
            public void Start() => Enabled = true;
            public void Stop() => Enabled = false;
        }

        internal sealed class OfflineDevice : IEthercatDevice
        {
            public bool CIFX_50RE_Connect() => false;
            public void CIFX_50RE_Disconnect() { }
            public void ReadData_Send_Start(int interval) { }
            public void ReadData_Timer_Start() { }
            public bool Digital_Input(int numbering) => false;
            public bool GetDigitalOutput(int numbering) => false;
            public void Digital_Output(int numbering, bool value) { }
            public void Axis1_ON() { }
            public void Axis1_OFF() { }
            public string Axis1_is_PosData() => "-";
            public void Axis1_UD_Config_Update(long a, long d, long m, long v) { }
            public void Axis1_UD_POS_Update(long position) { }
            public void Axis1_UD_Move_Send() { }
            public void Axis1_UD_Homming() { }
            public bool Axis1_Status(string status) => false;
            public void Axis2_ON() { }
            public void Axis2_OFF() { }
            public string Axis2_is_PosData() => "-";
            public void Axis2_LR_Config_Update(long a, long d, long m, long v) { }
            public void Axis2_LR_POS_Update(long position) { }
            public void Axis2_LR_Move_Send() { }
            public void Axis2_LR_Homming() { }
            public bool Axis2_Status(string status) => false;
            public bool IsAxisFault(int axis) => false;
            public ushort GetStatusWord(int axis) => 0;
            public void QuickStopAll() { }
            public void ResetAxisFault(int axis) { }
            public EthercatCommunicationStatus CheckCommunication() => null;
            public bool IsAxisOperationEnabled(int axis) => false;
            public bool IsEmergencyStopped => false;
            public void EmergencyStop() { }
            public void ClearEmergencyStop() { }
            public void ThrowIfEmergencyStopped() { }
            public IDisposable BeginOperation() => null;
        }
    }
}
