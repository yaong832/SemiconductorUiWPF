using System;
using System.Collections.Generic;
using IEG3268_Dll;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// Ieg3268Device의 추가 기능이 DLL 버퍼의 올바른 위치에 올바른 값을 쓰는지 확인합니다.
    /// 실제 드라이버는 부르지 않고 가짜 채널로 보낸 버퍼를 기록합니다.
    /// </summary>
    [TestClass]
    public class Ieg3268DeviceTests
    {
        private sealed class FakeCifxChannel : ICifxChannel
        {
            public readonly List<byte[]> Writes = new List<byte[]>();

            public void Write(byte[] data)
            {
                Writes.Add((byte[])data.Clone());
            }

            public uint GetBusState(out uint busState)
            {
                busState = CifxConstants.BusStateOn;
                return EthercatCommunicationStatus.NoError;
            }

            public uint ReadIo(byte[] buffer)
            {
                return EthercatCommunicationStatus.NoError;
            }
        }

        private IEG3268 _dll;
        private FakeCifxChannel _channel;
        private Ieg3268Device _device;

        [TestInitialize]
        public void Setup()
        {
            _dll = new IEG3268();
            _channel = new FakeCifxChannel();
            _device = new Ieg3268Device(_dll, _channel);
        }

        [TestMethod]
        public void QuickStopAll_WritesQuickStopToBothControlWords()
        {
            _dll.WriteData[46] = 0x5A; // 디지털 출력 바이트는 그대로 유지되어야 함

            _device.QuickStopAll();

            Assert.AreEqual(1, _channel.Writes.Count);
            byte[] sent = _channel.Writes[0];
            Assert.AreEqual(0x02, sent[0], "축1 제어워드");
            Assert.AreEqual(0x02, sent[23], "축2 제어워드");
            Assert.AreEqual(0x5A, sent[46], "디지털 출력");
        }

        [TestMethod]
        public void ResetAxisFault_Axis1_SendsRisingEdgeOnBit7()
        {
            _device.ResetAxisFault(1);

            Assert.AreEqual(3, _channel.Writes.Count);
            Assert.AreEqual(0x00, _channel.Writes[0][0]);
            Assert.AreEqual(0x80, _channel.Writes[1][0]);
            Assert.AreEqual(0x00, _channel.Writes[2][0]);
            Assert.AreEqual(0x00, _channel.Writes[1][23], "축2 제어워드는 건드리지 않음");
        }

        [TestMethod]
        public void ResetAxisFault_Axis2_UsesAxis2ControlWord()
        {
            _device.ResetAxisFault(2);

            Assert.AreEqual(0x80, _channel.Writes[1][23]);
            Assert.AreEqual(0x00, _channel.Writes[1][0], "축1 제어워드는 건드리지 않음");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ResetAxisFault_InvalidAxis_Throws()
        {
            _device.ResetAxisFault(3);
        }

        [TestMethod]
        public void IsAxisFault_ReadsBit3OfEachStatusWord()
        {
            var readData = new byte[96];
            readData[0] = 0x08;  // 축1 Fault
            readData[20] = 0x27; // 축2 운전 중 (Fault 비트 없음)
            _dll.ReadData = readData;

            Assert.IsTrue(_device.IsAxisFault(1));
            Assert.IsFalse(_device.IsAxisFault(2));
        }

        [TestMethod]
        public void IsAxisFault_ShortOrMissingReadData_ReturnsFalse()
        {
            _dll.ReadData = new byte[10];
            Assert.IsFalse(_device.IsAxisFault(2));

            _dll.ReadData = null;
            Assert.IsFalse(_device.IsAxisFault(1));
        }

        [TestMethod]
        public void GetDigitalOutput_ReturnsLastCommandedValue()
        {
            _dll.Digital_Out_Value[6] = true; // 챔버 B 램프 출력

            Assert.IsTrue(_device.GetDigitalOutput(6));
            Assert.IsFalse(_device.GetDigitalOutput(3));
        }

        [TestMethod]
        public void GetDigitalOutput_OutOfRange_ReturnsFalse()
        {
            Assert.IsFalse(_device.GetDigitalOutput(-1));
            Assert.IsFalse(_device.GetDigitalOutput(16));
        }

        [TestMethod]
        public void CheckCommunication_NotConnected_DoesNotTouchDriver()
        {
            var status = _device.CheckCommunication();

            Assert.IsFalse(status.IsConnected);
            Assert.IsFalse(status.IsHealthy);
        }

        [TestMethod]
        public void CommunicationStatus_IsHealthy_OnlyWhenBusOnAndNoErrors()
        {
            Assert.IsTrue(new EthercatCommunicationStatus(true, 0, CifxConstants.BusStateOn, 0).IsHealthy);
            Assert.IsFalse(new EthercatCommunicationStatus(true, 0, CifxConstants.BusStateOff, 0).IsHealthy, "버스 OFF");
            Assert.IsFalse(new EthercatCommunicationStatus(true, 0x800C0021, CifxConstants.BusStateOn, 0).IsHealthy, "버스 조회 실패");
            Assert.IsFalse(new EthercatCommunicationStatus(true, 0, CifxConstants.BusStateOn, 0x800C0021).IsHealthy, "입력 읽기 실패");
        }
    }
}
