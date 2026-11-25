using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Hardware;
using SemiconductorUi.Process;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using static SemiconductorUi.Tests.ProcessEngineSimulationTests;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 원본 9e7ca9e까지의 "공정 중 잠금"과 "서보가 꺼지면 원점복귀 다시" 규칙.
    /// </summary>
    [TestClass]
    public class ProcessLockTests
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
            _engine.RecipeApplied = true;
        }

        private void StartLot()
        {
            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = 3;
            _engine.WaferLoadingButton();
            _engine.StartButton();
            Assert.IsTrue(_simulation.IsRunning);
        }

        [TestMethod]
        public void IdleEngine_IsNotLocked()
        {
            Assert.IsFalse(_engine.IsProcessActiveOrStarting());
            Assert.IsFalse(_engine.IsBackgroundHomingInProgress());
            Assert.IsFalse(_engine.IsAutoProcessActive);
            Assert.AreEqual("대기", _engine.GetTmStatusText());
        }

        [TestMethod]
        public void RunningAndPausedLot_LockManualControl_AndTransferClear()
        {
            StartLot();
            Assert.IsTrue(_engine.IsProcessActiveOrStarting());
            Assert.IsTrue(_engine.IsAutoProcessActive, "장비 제어 창 수동 조작도 같은 기준으로 막습니다");

            _engine.PauseButton();
            Assert.IsTrue(_engine.IsProcessActiveOrStarting(), "일시정지도 공정 중입니다");

            int queued = _engine.TransferQueueCount;
            _engine.ClearTransferQueue();
            Assert.AreEqual(queued, _engine.TransferQueueCount, "공정 중에는 이송 목록을 비우지 않습니다");
            Assert.IsTrue(_host.Messages.Any(m => m.Contains("이송 목록을 비울 수 없습니다")));
            Assert.IsTrue(_host.Logs.Any(l => l == "WARN 공정 진행 중 이송 목록 비우기 시도 차단 (사용자: test)"));
        }

        [TestMethod]
        public void PostProcessHoming_ShowsHomingAndBlocksStartAndReset()
        {
            _engine.BeginPostProcessHoming();

            Assert.IsTrue(_engine.IsProcessActiveOrStarting());
            Assert.IsTrue(_engine.IsBackgroundHomingInProgress());
            Assert.AreEqual("원점복귀 중", _engine.GetTmStatusText());
            Assert.AreEqual("공정 상태: 대기 · TM 원점복귀 중", _vm.StatusProcessText);

            _engine.ToggleFoupMountButton();
            _host.WaferCountAnswer = 3;
            _engine.WaferLoadingButton();
            _engine.StartButton();
            Assert.IsFalse(_simulation.IsRunning, "원점복귀가 끝나기 전에는 시작하지 않습니다");
            Assert.IsTrue(_host.Messages.Any(m => m.StartsWith("TM이 원점으로 돌아가는 중입니다.")));

            _engine.ResetProcessButton();
            Assert.IsTrue(_host.Messages.Any(m => m.StartsWith("서보 ON·원점복귀가 진행 중이라 공정 리셋을 할 수 없습니다.")));

            _engine.EndPostProcessHoming();
            Assert.IsFalse(_engine.IsProcessActiveOrStarting());
            Assert.AreEqual("공정 상태: 대기", _vm.StatusProcessText);
            Assert.AreEqual("대기", _engine.GetTmStatusText());

            _engine.StartButton();
            Assert.IsTrue(_simulation.IsRunning, "원점복귀가 끝나면 시작됩니다");
        }

        [TestMethod]
        public void ResetConfirmMessage_FollowsWhatResetDoes()
        {
            string keep = ProcessEngine.BuildResetProcessConfirmMessage(true, false);
            StringAssert.StartsWith(keep, "진행 중인 공정을 중단하고 초기화하시겠습니까?");
            StringAssert.Contains(keep, "FOUP 표시는 그대로 둡니다 (웨이퍼 로딩 중)");
            Assert.IsFalse(keep.Contains("[하드웨어 모드]"));

            string hw = ProcessEngine.BuildResetProcessConfirmMessage(false, true);
            StringAssert.Contains(hw, "FOUP 표시를 비웁니다.");
            StringAssert.Contains(hw, "1. 진공 OFF\n2. 실린더가 후진 상태가 아니면 후진 (최대 5초)\n3. 원점복귀: 상하 → 회전");
        }

        [TestMethod]
        public void TmController_RequiresRehomeAfterConnect_AndAfterServoOff()
        {
            var device = new StoppableDevice();
            var tm = new TmHardwareController(device);
            Assert.IsTrue(tm.RehomeRequired);
            Assert.AreEqual("프로그램 연결 후 원점복귀 전", tm.RehomeReason);
            StringAssert.Contains(tm.MoveToPosition(0, 0).ErrorMessage, "원점복귀가 필요합니다 (프로그램 연결 후 원점복귀 전)");

            tm.MarkHomedAfterSuccessfulHoming();
            Assert.IsFalse(tm.RehomeRequired);
            Assert.IsTrue(tm.HasEverHomed);

            tm.RequireRehome("상단 바 서보 OFF");
            Assert.IsTrue(tm.RehomeRequired);
            Assert.IsFalse(tm.HasEverHomed, "서보가 꺼지면 원점복귀 이력도 지웁니다");
            tm.RequireRehome("두 번째 원인");
            Assert.AreEqual("상단 바 서보 OFF", tm.RehomeReason, "처음 원인을 그대로 둡니다");
        }

        [TestMethod]
        public void TmController_HomingDuringEmergencyStop_IsNotTrusted()
        {
            var device = new StoppableDevice { Stopped = true };
            var tm = new TmHardwareController(device);
            tm.MarkHomedAfterSuccessfulHoming();
            Assert.IsTrue(tm.RehomeRequired);
            Assert.IsFalse(tm.HasEverHomed);

            device.Stopped = false;
            tm.MarkHomedAfterSuccessfulHoming();
            Assert.IsFalse(tm.RehomeRequired);
        }

        [TestMethod]
        public void EngineRequireRehome_ClearsHomedFlag_AndForcesHomingOnNextStart()
        {
            _engine.TmHardwareInitialized = true;
            _engine.RequireRehome("상단 바 서보 OFF");
            Assert.IsFalse(_engine.TmHardwareInitialized);
            Assert.IsTrue(_engine.IsRehomeRequired(), "TM 컨트롤러가 없으면 원점복귀가 필요한 것으로 봅니다");
        }

        private sealed class StoppableDevice : IEthercatDevice
        {
            public bool Stopped { get; set; }
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
            public bool IsEmergencyStopped => Stopped;
            public void EmergencyStop() => Stopped = true;
            public void ClearEmergencyStop() => Stopped = false;
            public void ThrowIfEmergencyStopped() { }
            public IDisposable BeginOperation() => null;
        }
    }
}
