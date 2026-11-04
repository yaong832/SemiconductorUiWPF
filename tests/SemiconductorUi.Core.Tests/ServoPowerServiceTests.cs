using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class ServoPowerServiceTests
    {
        [TestMethod]
        public void TurnOn_SendsOffConfigOnInOrder_AndConfirmsWhenPositionReadable()
        {
            var device = new ServoFakeDevice { PosData = "0" };

            bool confirmed = ServoPowerService.TurnOnAndConfirm(device, TimeSpan.FromMilliseconds(300));

            Assert.IsTrue(confirmed);
            CollectionAssert.AreEqual(
                new[] { "Axis1_OFF", "Axis2_OFF", "Axis1_Config", "Axis2_Config", "Axis1_ON", "Axis2_ON" },
                device.Commands.ToArray());
        }

        [TestMethod]
        public void TurnOn_ReturnsFalse_WhenPositionNeverReadable()
        {
            var device = new ServoFakeDevice { PosData = "-" };

            Assert.IsFalse(ServoPowerService.TurnOnAndConfirm(device, TimeSpan.FromMilliseconds(200)));
        }

        [TestMethod]
        public void TurnOn_PropagatesEmergencyStopDuringConfirmation()
        {
            var device = new ServoFakeDevice { PosData = "-", EmergencyStopped = true };

            Assert.ThrowsException<EmergencyStopActiveException>(
                () => ServoPowerService.TurnOnAndConfirm(device, TimeSpan.FromMilliseconds(200)));
        }

        [TestMethod]
        public void TurnOff_SendsBothAxesOff()
        {
            var device = new ServoFakeDevice();
            ServoPowerService.TurnOff(device);
            CollectionAssert.AreEqual(new[] { "Axis1_OFF", "Axis2_OFF" }, device.Commands.ToArray());
        }

        private sealed class ServoFakeDevice : IEthercatDevice
        {
            public List<string> Commands { get; } = new List<string>();
            public string PosData { get; set; } = "0";
            public bool EmergencyStopped { get; set; }

            public void ThrowIfEmergencyStopped()
            {
                if (EmergencyStopped) throw new EmergencyStopActiveException("비상정지");
            }

            public bool IsEmergencyStopped => EmergencyStopped;
            public void Axis1_ON() => Commands.Add("Axis1_ON");
            public void Axis1_OFF() => Commands.Add("Axis1_OFF");
            public void Axis2_ON() => Commands.Add("Axis2_ON");
            public void Axis2_OFF() => Commands.Add("Axis2_OFF");
            public string Axis1_is_PosData() => PosData;
            public string Axis2_is_PosData() => PosData;
            public void Axis1_UD_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) => Commands.Add("Axis1_Config");
            public void Axis2_LR_Config_Update(long acceleration, long deceleration, long maxVelocity, long velocity) => Commands.Add("Axis2_Config");

            public bool Digital_Input(int numbering) => false;
            public bool GetDigitalOutput(int numbering) => false;
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
