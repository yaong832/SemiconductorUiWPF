using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class IoMonitorSnapshotTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 11, 14, 30, 0);

        [TestMethod]
        public void Names_HaveSixteenPoints_AndOnlyCylinderSensorsAreUsedInputs()
        {
            Assert.AreEqual(IoMonitorSnapshot.PointCount, IoMonitorSnapshot.InputNames.Length);
            Assert.AreEqual(IoMonitorSnapshot.PointCount, IoMonitorSnapshot.OutputNames.Length);
            Assert.AreEqual("실린더 후진 센서", IoMonitorSnapshot.InputNames[12]);
            Assert.AreEqual("실린더 전진 센서", IoMonitorSnapshot.InputNames[13]);
            for (int i = 0; i < IoMonitorSnapshot.PointCount; i++)
            {
                if (i == 12 || i == 13) continue;
                Assert.AreEqual(IoMonitorSnapshot.UnusedName, IoMonitorSnapshot.InputNames[i], $"입력 {i}");
            }
            Assert.AreEqual("실린더 전진", IoMonitorSnapshot.OutputNames[12]);
            Assert.AreEqual("진공 (흡착)", IoMonitorSnapshot.OutputNames[14]);
        }

        [TestMethod]
        public void Read_WhenNotConnected_ReturnsNoValues_AndDoesNotTouchDevice()
        {
            var device = new IoFakeDevice();

            var snapshot = IoMonitorSnapshot.Read(device, false, Now);

            Assert.IsFalse(snapshot.Connected);
            Assert.IsNull(snapshot.Inputs);
            Assert.IsNull(snapshot.Outputs);
            Assert.AreEqual(0, device.Reads);
            Assert.AreEqual(Now, snapshot.Time);
            Assert.IsFalse(IoMonitorSnapshot.Read(null, true, Now).Connected);
        }

        [TestMethod]
        public void Read_WhenConnected_ReadsAllPointsAndBothAxes()
        {
            var device = new IoFakeDevice { EmergencyStopped = true };
            device.Inputs[13] = true;
            device.Outputs[12] = true;
            device.Outputs[14] = true;
            device.StatusWords[1] = 0x1637;
            device.StatusWords[2] = 0x0218;

            var snapshot = IoMonitorSnapshot.Read(device, true, Now);

            Assert.IsTrue(snapshot.Connected);
            Assert.IsNull(snapshot.Error);
            Assert.IsTrue(snapshot.EmergencyStopped);
            Assert.IsTrue(snapshot.Inputs[13]);
            Assert.IsFalse(snapshot.Inputs[12]);
            Assert.IsTrue(snapshot.Outputs[12]);
            Assert.IsTrue(snapshot.Outputs[14]);
            Assert.IsFalse(snapshot.Outputs[13]);
            Assert.AreEqual(1, snapshot.Axis1.Axis);
            Assert.AreEqual("806874", snapshot.Axis1.Position);
            Assert.AreEqual("-59120", snapshot.Axis2.Position);
            Assert.IsFalse(snapshot.Axis1.IsFault);
            Assert.IsTrue(snapshot.Axis2.IsFault);
        }

        [TestMethod]
        public void Read_WhenDeviceThrows_KeepsErrorAndNoValues()
        {
            var device = new IoFakeDevice { ThrowOnInput = 5 };

            var snapshot = IoMonitorSnapshot.Read(device, true, Now);

            Assert.IsTrue(snapshot.Connected);
            Assert.AreEqual("입력 5 읽기 실패", snapshot.Error);
            Assert.IsNull(snapshot.Inputs);
            Assert.IsNull(snapshot.Outputs);
        }

        [DataTestMethod]
        [DataRow((ushort)0x0000, "Not ready")]
        [DataRow((ushort)0x0250, "Switch on disabled")]
        [DataRow((ushort)0x0231, "Ready to switch on")]
        [DataRow((ushort)0x0233, "Switched on")]
        [DataRow((ushort)0x1637, "Operation enabled (서보 ON)")]
        [DataRow((ushort)0x0217, "Quick stop active")]
        [DataRow((ushort)0x021F, "Fault reaction active")]
        [DataRow((ushort)0x0218, "Fault (알람)")]
        public void StateName_FollowsCia402StatusWord(ushort word, string expected)
        {
            var axis = new ServoAxisSnapshot { StatusWord = word };
            Assert.AreEqual(expected, axis.StateName);
        }

        [TestMethod]
        public void Bit_ReadsSingleBits()
        {
            var axis = new ServoAxisSnapshot { StatusWord = 0x0484 };
            Assert.IsTrue(axis.Bit(2));
            Assert.IsTrue(axis.Bit(7));
            Assert.IsTrue(axis.Bit(10));
            Assert.IsFalse(axis.Bit(3));
            Assert.IsFalse(axis.IsFault);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 7, 10 }, Array.ConvertAll(ServoAxisSnapshot.KeyBits, b => b.Bit));
        }

        private sealed class IoFakeDevice : IEthercatDevice
        {
            public bool[] Inputs { get; } = new bool[IoMonitorSnapshot.PointCount];
            public bool[] Outputs { get; } = new bool[IoMonitorSnapshot.PointCount];
            public Dictionary<int, ushort> StatusWords { get; } = new Dictionary<int, ushort> { [1] = 0, [2] = 0 };
            public bool EmergencyStopped { get; set; }
            public int ThrowOnInput { get; set; } = -1;
            public int Reads { get; private set; }

            public bool Digital_Input(int numbering)
            {
                Reads++;
                if (numbering == ThrowOnInput) throw new InvalidOperationException($"입력 {numbering} 읽기 실패");
                return Inputs[numbering];
            }

            public bool GetDigitalOutput(int numbering)
            {
                Reads++;
                return Outputs[numbering];
            }

            public ushort GetStatusWord(int axis)
            {
                Reads++;
                return StatusWords[axis];
            }

            public string Axis1_is_PosData() => "806874";
            public string Axis2_is_PosData() => "-59120";
            public bool IsEmergencyStopped => EmergencyStopped;

            public void ThrowIfEmergencyStopped() { }
            public void Axis1_ON() { }
            public void Axis1_OFF() { }
            public void Axis2_ON() { }
            public void Axis2_OFF() { }
            public void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) { }
            public void Axis1_UD_Homming() { }
            public void Axis2_LR_Homming() { }
            public bool Axis1_Status(string status) => false;
            public bool Axis2_Status(string status) => false;
            public bool CIFX_50RE_Connect() => true;
            public void CIFX_50RE_Disconnect() { }
            public void ReadData_Send_Start(int interval) { }
            public void ReadData_Timer_Start() { }
            public void Digital_Output(int numbering, bool value) { }
            public void Axis1_UD_POS_Update(long position) { }
            public void Axis1_UD_Move_Send() { }
            public void Axis2_LR_POS_Update(long position) { }
            public void Axis2_LR_Move_Send() { }
            public bool IsAxisFault(int axis) => false;
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
