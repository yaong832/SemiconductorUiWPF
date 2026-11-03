using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 공정 초기화 원점복귀가 상하 → 좌우 순서를 지키고, 실린더가 후진일 때만 도는지 확인합니다.
    /// 시간은 가짜 시계로 흉내 내며, sleep 한 번에 PollIntervalMs만큼 흐릅니다.
    /// </summary>
    [TestClass]
    public class ResetHomingSequenceTests
    {
        private FakeDevice _device;
        private long _now;
        private ResetHomingSequence _sequence;

        [TestInitialize]
        public void Setup()
        {
            _device = new FakeDevice(() => _now);
            _now = 0;
            _sequence = new ResetHomingSequence(_device, () => _now, ms => _now += ms);
        }

        [TestMethod]
        public void Success_HomesZFirst_AndRotationOnlyAfterZDone()
        {
            _device.Axis1HomeDurationMs = 3000;
            _device.Axis2HomeDurationMs = 2000;

            Assert.IsNull(_sequence.Run(null));

            CollectionAssert.AreEqual(new[] { "Axis1_Home", "Axis2_Home" }, _device.Commands.ToArray());
            Assert.IsTrue(_device.Axis2HomeSentAt >= _device.Axis1HomeSentAt + 3000,
                "좌우 원점복귀는 상하 원점복귀가 끝난 뒤에 보내야 합니다.");
        }

        [TestMethod]
        public void StaleHomeDoneRightAfterCommand_IsNotTrusted()
        {
            // 명령 직후 입력이 아직 갱신되지 않아 예전 '완료' 값이 남아 있는 경우
            _device.Axis1StaleHomeDoneMs = 300;
            _device.Axis1HomeDurationMs = 800;

            Assert.IsNull(_sequence.Run(null));

            Assert.IsTrue(_device.Axis2HomeSentAt - _device.Axis1HomeSentAt >= ResetHomingSequence.HomeDoneTrustDelayMs);
        }

        [TestMethod]
        public void CylinderSensorOff_SendsNoHoming()
        {
            _device.Retracted = false;

            Assert.IsNotNull(_sequence.Run(null));
            Assert.AreEqual(0, _device.Commands.Count);
        }

        [TestMethod]
        public void ExtendOutputOn_SendsNoHoming()
        {
            _device.ExtendOutput = true;

            Assert.IsNotNull(_sequence.Run(null));
            Assert.AreEqual(0, _device.Commands.Count);
        }

        [TestMethod]
        public void CylinderSensorReadFails_SendsNoHoming()
        {
            _device.ThrowOnInput = true;

            string reason = _sequence.Run(null);

            StringAssert.Contains(reason, "읽지 못해");
            Assert.AreEqual(0, _device.Commands.Count);
        }

        [TestMethod]
        public void CylinderLeavesRetractedDuringZHoming_RotationNotSent()
        {
            _device.Axis1HomeDurationMs = 2000;
            _device.OnAxis1Homing = () => _device.Retracted = false;

            Assert.IsNotNull(_sequence.Run(null));
            CollectionAssert.AreEqual(new[] { "Axis1_Home" }, _device.Commands.ToArray());
        }

        [TestMethod]
        public void ZHomingTimeout_RotationNotSent()
        {
            _device.Axis1HomeDurationMs = long.MaxValue;

            string reason = _sequence.Run(null);

            StringAssert.Contains(reason, "상하");
            CollectionAssert.AreEqual(new[] { "Axis1_Home" }, _device.Commands.ToArray());
        }

        [TestMethod]
        public void EmergencyStopDuringZHoming_StopsWithoutRotation()
        {
            _device.Axis1HomeDurationMs = 5000;
            _device.EmergencyStopAtMs = 1500;

            Assert.ThrowsException<EmergencyStopActiveException>(() => _sequence.Run(null));
            CollectionAssert.AreEqual(new[] { "Axis1_Home" }, _device.Commands.ToArray());
        }

        private sealed class FakeDevice : IEthercatDevice
        {
            private readonly Func<long> _now;

            public FakeDevice(Func<long> now)
            {
                _now = now;
            }

            public List<string> Commands { get; } = new List<string>();
            public bool Retracted { get; set; } = true;
            public bool ExtendOutput { get; set; }
            public bool ThrowOnInput { get; set; }
            public long Axis1HomeDurationMs { get; set; } = 1500;
            public long Axis2HomeDurationMs { get; set; } = 1500;
            public long Axis1StaleHomeDoneMs { get; set; }
            public long EmergencyStopAtMs { get; set; } = long.MaxValue;
            public Action OnAxis1Homing { get; set; }
            public long Axis1HomeSentAt { get; private set; } = -1;
            public long Axis2HomeSentAt { get; private set; } = -1;

            public bool Digital_Input(int numbering)
            {
                if (ThrowOnInput) throw new InvalidOperationException("통신 오류");
                return numbering == 12 && Retracted;
            }

            public bool GetDigitalOutput(int numbering)
            {
                return numbering == 12 && ExtendOutput;
            }

            public void Axis1_UD_Homming()
            {
                Commands.Add("Axis1_Home");
                Axis1HomeSentAt = _now();
                OnAxis1Homing?.Invoke();
            }

            public void Axis2_LR_Homming()
            {
                Commands.Add("Axis2_Home");
                Axis2HomeSentAt = _now();
            }

            public bool Axis1_Status(string status)
            {
                if (status != "HOME_D" || Axis1HomeSentAt < 0) return false;
                long elapsed = _now() - Axis1HomeSentAt;
                if (elapsed < Axis1StaleHomeDoneMs) return true;
                return elapsed >= Axis1HomeDurationMs;
            }

            public bool Axis2_Status(string status)
            {
                if (status != "HOME_D" || Axis2HomeSentAt < 0) return false;
                return _now() - Axis2HomeSentAt >= Axis2HomeDurationMs;
            }

            public void ThrowIfEmergencyStopped()
            {
                if (_now() >= EmergencyStopAtMs) throw new EmergencyStopActiveException("비상정지");
            }

            public bool IsEmergencyStopped => _now() >= EmergencyStopAtMs;

            public bool CIFX_50RE_Connect() => true;
            public void CIFX_50RE_Disconnect() { }
            public void ReadData_Send_Start(int interval) { }
            public void ReadData_Timer_Start() { }
            public void Digital_Output(int numbering, bool value) => Commands.Add($"DO{numbering}={value}");
            public void Axis1_ON() => Commands.Add("Axis1_ON");
            public void Axis1_OFF() => Commands.Add("Axis1_OFF");
            public string Axis1_is_PosData() => "0";
            public void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis1_UD_POS_Update(long position) { }
            public void Axis1_UD_Move_Send() => Commands.Add("Axis1_Move");
            public void Axis2_ON() => Commands.Add("Axis2_ON");
            public void Axis2_OFF() => Commands.Add("Axis2_OFF");
            public string Axis2_is_PosData() => "0";
            public void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis2_LR_POS_Update(long position) { }
            public void Axis2_LR_Move_Send() => Commands.Add("Axis2_Move");
            public bool IsAxisFault(int axis) => false;
            public ushort GetStatusWord(int axis) => 0;
            public void QuickStopAll() { }
            public void ResetAxisFault(int axis) { }
            public EthercatCommunicationStatus CheckCommunication() => null;
            public bool IsAxisOperationEnabled(int axis) => true;
            public void EmergencyStop() { }
            public void ClearEmergencyStop() { }
            public IDisposable BeginOperation() => null;
        }
    }
}
