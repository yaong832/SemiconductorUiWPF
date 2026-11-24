using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Process;

namespace SemiconductorUi.Tests
{
    /// <summary>원본 EquipmentControlForm의 출력 번호·순서, 공정 중 막기, 타이머를 확인한다.</summary>
    [TestClass]
    public class EquipmentManualControlTests
    {
        private RecordingDevice _device;
        private bool _connected;
        private bool _autoProcess;
        private List<(string message, string title, MessageKind kind)> _messages;
        private List<(int ms, Action callback, Handle handle)> _scheduled;
        private List<string> _logs;
        private DateTime _now;
        private EquipmentManualControl _control;

        private sealed class Handle : IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        [TestInitialize]
        public void Setup()
        {
            _device = new RecordingDevice();
            _connected = true;
            _autoProcess = false;
            _messages = new List<(string, string, MessageKind)>();
            _scheduled = new List<(int, Action, Handle)>();
            _logs = new List<string>();
            _now = new DateTime(2026, 11, 11, 10, 0, 0);
            _control = new EquipmentManualControl(_device, () => _connected,
                (m, t, k) => _messages.Add((m, t, k)),
                a => a(),
                (ms, cb) => { var h = new Handle(); _scheduled.Add((ms, cb, h)); return h; },
                () => _now,
                (m, l) => _logs.Add($"{l} {m}"))
            {
                IsAutoProcessActive = () => _autoProcess,
            };
        }

        [TestMethod]
        public void ChamberDoors_UseOriginalOutputOrder()
        {
            var doors = new List<(EquipmentRegion, bool)>();
            _control.OnDoorStateChanged = (r, o) => doors.Add((r, o));

            _control.SetChamberDoor(EquipmentRegion.ChamberA, true);
            _control.SetChamberDoor(EquipmentRegion.ChamberB, true);
            _control.SetChamberDoor(EquipmentRegion.ChamberC, false);

            CollectionAssert.AreEqual(new[] { "DO4=0", "DO5=1", "DO8=1", "DO7=0", "DO10=1", "DO11=0" }, _device.Commands);
            Assert.AreEqual("Chamber C 도어 닫힘", _control.StatusText);
            CollectionAssert.AreEqual(new[] { (EquipmentRegion.ChamberA, true), (EquipmentRegion.ChamberB, true), (EquipmentRegion.ChamberC, false) }, doors);
            Assert.AreEqual("INFO 장비 제어: Chamber A 도어 열림", _logs[0]);
        }

        [TestMethod]
        public void LampsAndBatch_UseOriginalOutputs()
        {
            bool[] mainLamp = null;
            _control.OnMainLampStateChanged = (r, y, g) => mainLamp = new[] { r, y, g };

            _control.SetChamberLamp(EquipmentRegion.ChamberB, true);
            _control.SetMainLamp(1, true);
            CollectionAssert.AreEqual(new[] { false, true, false }, mainLamp);

            _device.Commands.Clear();
            _control.SetAllLamps(false);
            CollectionAssert.AreEqual(new[] { "DO3=0", "DO6=0", "DO9=0", "DO0=0", "DO1=0", "DO2=0" }, _device.Commands);

            _device.Commands.Clear();
            _control.SetAllDoors(false);
            CollectionAssert.AreEqual(new[] { "DO4=1", "DO5=0", "DO7=1", "DO8=0", "DO10=1", "DO11=0" }, _device.Commands);
            Assert.AreEqual("모든 문이 닫혔습니다.", _control.StatusText);
        }

        [TestMethod]
        public void CylinderVacuumExhaust_UseOriginalOutputs()
        {
            _control.CylinderExtend();
            _control.CylinderRetract();
            _control.VacuumOn();
            _control.ExhaustOn();
            _control.ExhaustOff();
            CollectionAssert.AreEqual(new[]
            {
                "DO13=0", "DO12=1",
                "DO12=0", "DO13=1",
                "DO15=0", "DO14=1",
                "DO14=0", "DO15=1",
                "DO15=0"
            }, _device.Commands);
        }

        [TestMethod]
        public void DuringAutoProcess_BlocksManualIo_ButAllowsLampOffAndMainLamp()
        {
            _autoProcess = true;

            _control.SetChamberDoor(EquipmentRegion.ChamberA, false);
            _control.SetChamberLamp(EquipmentRegion.ChamberA, true);
            _control.VacuumOn();
            _control.ServoOff();
            Assert.AreEqual(0, _device.Commands.Count);
            Assert.AreEqual(4, _messages.Count);
            Assert.IsTrue(_messages.All(m => m.title == "수동 제어 제한"));
            StringAssert.Contains(_messages[0].message, "'챔버 도어 닫기' 수동 제어를 할 수 없습니다");

            // 원본: 램프 OFF와 3색 램프, 모든 램프 끄기는 공정 중에도 된다
            _control.SetChamberLamp(EquipmentRegion.ChamberA, false);
            _control.SetMainLamp(0, true);
            _control.SetAllLamps(false);
            Assert.AreEqual("DO3=0", _device.Commands[0]);
            Assert.AreEqual("DO0=1", _device.Commands[1]);
            Assert.AreEqual(8, _device.Commands.Count);
        }

        [TestMethod]
        public void NotConnected_ShowsOriginalMessage_AndSendsNothing()
        {
            _connected = false;
            _control.SetChamberLamp(EquipmentRegion.ChamberA, true);
            _control.CylinderExtend();
            Assert.AreEqual(0, _device.Commands.Count);
            Assert.AreEqual(("EtherCAT이 연결되지 않았습니다.", "오류", MessageKind.Warning), _messages[0]);
            Assert.AreEqual("연결 필요", _messages[1].title);
        }

        [TestMethod]
        public void TimedAction_TurnsOnThenOffAfterSeconds_AndOffButtonCancels()
        {
            string key = EquipmentManualControl.TimedKey(EquipmentRegion.ChamberA, true);
            Assert.AreEqual("ChamberA_Door", key);

            _control.StartTimedAction(key, 5);
            CollectionAssert.AreEqual(new[] { "DO4=0", "DO5=1" }, _device.Commands);
            Assert.AreEqual("Chamber A 도어 ON — 5초 후 자동 OFF", _control.StatusText);
            Assert.AreEqual(5000, _scheduled.Single().ms);
            Assert.AreEqual(1, _control.ActiveTimers.Count);
            Assert.AreEqual(_now.AddSeconds(5), _control.ActiveTimers[0].EndsAt);

            _scheduled[0].callback();
            CollectionAssert.AreEqual(new[] { "DO4=0", "DO5=1", "DO4=1", "DO5=0" }, _device.Commands);
            Assert.AreEqual("Chamber A 도어 타이머 완료 → OFF (5초)", _control.StatusText);
            Assert.AreEqual(0, _control.ActiveTimers.Count);

            // 닫기 버튼은 타이머를 먼저 취소한다
            _control.StartTimedAction(key, 3);
            _control.ChamberDoorCloseButton(EquipmentRegion.ChamberA);
            Assert.IsTrue(_scheduled[1].handle.Disposed);
            Assert.AreEqual(0, _control.ActiveTimers.Count);
            int before = _device.Commands.Count;
            _scheduled[1].callback(); // 취소된 타이머가 늦게 불려도 아무것도 하지 않는다
            Assert.AreEqual(before, _device.Commands.Count);
        }

        [TestMethod]
        public void TimedAction_RejectsOutOfRangeSeconds_AndEmergencyStopCancelsAll()
        {
            _control.StartTimedAction(EquipmentManualControl.VacuumKey, 0);
            Assert.AreEqual(("1초 이상으로 입력하세요.", "입력 오류", MessageKind.Warning), _messages.Last());
            _control.StartTimedAction(EquipmentManualControl.VacuumKey, 3601);
            Assert.AreEqual("3600초 이하로 입력하세요.", _messages.Last().message);
            Assert.AreEqual(0, _device.Commands.Count);

            bool estop = false;
            _control.OnEmergencyStopRequested = () => estop = true;
            _control.StartTimedAction(EquipmentManualControl.VacuumKey, 10);
            _control.StartTimedAction(EquipmentManualControl.MainLampKey(2), 10);
            Assert.AreEqual(2, _control.ActiveTimers.Count);

            _control.EmergencyStop();
            Assert.IsTrue(estop);
            Assert.IsTrue(_device.EmergencyStopCalled);
            Assert.AreEqual(0, _control.ActiveTimers.Count);
            Assert.IsTrue(_scheduled.All(s => s.handle.Disposed));
            Assert.AreEqual("비상정지 - 서보 OFF. 메인 화면의 알람 리셋으로 해제하세요.", _control.StatusText);
        }

        [TestMethod]
        public void FinishTimedActionsNow_SendsOffAndCancels()
        {
            _control.StartTimedAction(EquipmentManualControl.TimedKey(EquipmentRegion.ChamberC, true), 60);
            _control.StartTimedAction(EquipmentManualControl.ExhaustKey, 60);
            _device.Commands.Clear();

            _control.FinishTimedActionsNow();

            Assert.AreEqual(0, _control.ActiveTimers.Count);
            Assert.IsTrue(_scheduled.All(s => s.handle.Disposed));
            CollectionAssert.AreEquivalent(new[] { "DO10=1", "DO11=0", "DO15=0" }, _device.Commands);
        }

        [TestMethod]
        public void Home_RequiresRetractedCylinder_ThenHomesZBeforeRotation()
        {
            _device.Retracted = false;
            _control.HomeAsync().Wait();
            Assert.AreEqual(("실린더가 후진 상태가 아닙니다.\n먼저 실린더를 후진시켜 주세요.", "인터락", MessageKind.Warning), _messages.Last());
            Assert.AreEqual(0, _device.Commands.Count);

            _device.Retracted = true;
            bool homed = false;
            _control.OnHomingRequested = () => homed = true;
            long clock = 0;
            _control.HomeAsync(d => new ResetHomingSequence(d, () => clock, ms => clock += ms)).Wait();

            CollectionAssert.AreEqual(new[] { "Axis1_Home", "Axis2_Home" }, _device.Commands);
            Assert.IsTrue(homed);
            Assert.IsFalse(_control.IsBusy);
            Assert.AreEqual("원점복귀 완료!", _control.StatusText);
            Assert.AreEqual(("원점복귀가 완료되었습니다.\n(상하 → 좌우 순서)", "완료", MessageKind.Information), _messages.Last());
        }

        [TestMethod]
        public void Teaching_IsBlockedDuringAutoProcess()
        {
            int requested = 0;
            _control.OnTeachingRequested = () => requested++;
            _control.RequestTeaching();
            _autoProcess = true;
            _control.RequestTeaching();
            Assert.AreEqual(1, requested);
            StringAssert.Contains(_messages.Single().message, "'티칭 위치 편집'");
        }

        private sealed class RecordingDevice : IEthercatDevice
        {
            private readonly bool[] _outputs = new bool[32];

            public List<string> Commands { get; } = new List<string>();
            public bool Retracted { get; set; } = true;
            public bool EmergencyStopCalled { get; private set; }

            public bool Digital_Input(int numbering) => numbering == 12 && Retracted;

            public void Digital_Output(int numbering, bool value)
            {
                _outputs[numbering] = value;
                Commands.Add($"DO{numbering}={(value ? 1 : 0)}");
            }

            public bool GetDigitalOutput(int numbering) => _outputs[numbering];

            public void Axis1_UD_Homming() => Commands.Add("Axis1_Home");
            public void Axis2_LR_Homming() => Commands.Add("Axis2_Home");
            public bool Axis1_Status(string status) => status == "HOME_D" && Commands.Contains("Axis1_Home");
            public bool Axis2_Status(string status) => status == "HOME_D" && Commands.Contains("Axis2_Home");

            public bool IsEmergencyStopped => false;
            public void EmergencyStop() => EmergencyStopCalled = true;
            public void ClearEmergencyStop() { }
            public void ThrowIfEmergencyStopped() { }
            public IDisposable BeginOperation() => new Handle();

            public bool CIFX_50RE_Connect() => true;
            public void CIFX_50RE_Disconnect() { }
            public void ReadData_Send_Start(int interval) { }
            public void ReadData_Timer_Start() { }
            public void Axis1_ON() => Commands.Add("Axis1_ON");
            public void Axis1_OFF() => Commands.Add("Axis1_OFF");
            public string Axis1_is_PosData() => "0";
            public void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis1_UD_POS_Update(long position) { }
            public void Axis1_UD_Move_Send() { }
            public void Axis2_ON() => Commands.Add("Axis2_ON");
            public void Axis2_OFF() => Commands.Add("Axis2_OFF");
            public string Axis2_is_PosData() => "0";
            public void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis2_LR_POS_Update(long position) { }
            public void Axis2_LR_Move_Send() { }
            public bool IsAxisFault(int axis) => false;
            public ushort GetStatusWord(int axis) => 0;
            public void QuickStopAll() { }
            public void ResetAxisFault(int axis) { }
            public EthercatCommunicationStatus CheckCommunication() => default(EthercatCommunicationStatus);
            public bool IsAxisOperationEnabled(int axis) => true;
        }
    }
}
