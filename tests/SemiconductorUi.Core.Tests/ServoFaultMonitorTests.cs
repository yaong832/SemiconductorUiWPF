using System.Linq;
using IEG3268_Dll;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 서보 알람 감시가 새로 생긴 알람만 한 번씩 알리는지 확인합니다.
    /// 드라이브 상태는 DLL의 ReadData 버퍼(축1 상태워드 [0..1], 축2 [20..21])로 흉내 냅니다.
    /// </summary>
    [TestClass]
    public class ServoFaultMonitorTests
    {
        private const byte FaultBit = 0x08;
        private const byte OperationEnabled = 0x37;

        private IEG3268 _dll;
        private Ieg3268Device _device;
        private ServoFaultMonitor _monitor;

        [TestInitialize]
        public void Setup()
        {
            _dll = new IEG3268();
            _dll.ReadData = new byte[96];
            _device = new Ieg3268Device(_dll, new NullChannel());
            _monitor = new ServoFaultMonitor(_device);
        }

        private sealed class NullChannel : ICifxChannel
        {
            public void Write(byte[] data) { }

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

        private void SetStatus(byte axis1Low, byte axis2Low)
        {
            // DLL 수신 타이머처럼 버퍼를 통째로 바꿉니다.
            var readData = new byte[96];
            readData[0] = axis1Low;
            readData[20] = axis2Low;
            _dll.ReadData = readData;
        }

        [TestMethod]
        public void NoFault_ReportsNothing()
        {
            SetStatus(OperationEnabled, OperationEnabled);

            Assert.AreEqual(0, _monitor.PollNewFaults().Count);
        }

        [TestMethod]
        public void NewFault_ReportedOnceWhileItStaysOn()
        {
            SetStatus(OperationEnabled, OperationEnabled);
            _monitor.PollNewFaults();

            SetStatus(FaultBit, OperationEnabled);
            CollectionAssert.AreEqual(new[] { 1 }, _monitor.PollNewFaults().ToArray());
            Assert.AreEqual(0, _monitor.PollNewFaults().Count, "같은 알람은 다시 알리지 않습니다.");
            CollectionAssert.AreEqual(new[] { 1 }, _monitor.GetFaultedAxes().ToArray());
        }

        [TestMethod]
        public void SecondAxisFaultLater_IsReportedSeparately()
        {
            SetStatus(FaultBit, OperationEnabled);
            _monitor.PollNewFaults();

            SetStatus(FaultBit, FaultBit);
            CollectionAssert.AreEqual(new[] { 2 }, _monitor.PollNewFaults().ToArray());
        }

        [TestMethod]
        public void FaultClearedThenBack_IsReportedAgain()
        {
            SetStatus(OperationEnabled, FaultBit);
            _monitor.PollNewFaults();

            SetStatus(OperationEnabled, 0x40);
            Assert.AreEqual(0, _monitor.PollNewFaults().Count);
            Assert.AreEqual(0, _monitor.GetFaultedAxes().Count);

            SetStatus(OperationEnabled, FaultBit);
            CollectionAssert.AreEqual(new[] { 2 }, _monitor.PollNewFaults().ToArray());
        }

        [TestMethod]
        public void Reset_ReportsExistingFaultAgain()
        {
            SetStatus(FaultBit, OperationEnabled);
            _monitor.PollNewFaults();

            _monitor.Reset();

            CollectionAssert.AreEqual(new[] { 1 }, _monitor.PollNewFaults().ToArray(), "다시 연결한 뒤 남아 있는 알람은 새 알람으로 알립니다.");
        }

        [TestMethod]
        public void FaultAlreadyPresentAtFirstCheck_IsReported()
        {
            SetStatus(FaultBit, FaultBit);

            CollectionAssert.AreEqual(new[] { 1, 2 }, _monitor.PollNewFaults().ToArray());
        }

        [TestMethod]
        public void GetStatusWord_CombinesLowAndHighBytes()
        {
            var readData = new byte[96];
            readData[0] = 0x18;
            readData[1] = 0x02;
            readData[20] = 0x37;
            readData[21] = 0x16;
            _dll.ReadData = readData;

            Assert.AreEqual((ushort)0x0218, _device.GetStatusWord(1));
            Assert.AreEqual((ushort)0x1637, _device.GetStatusWord(2));
        }

        [TestMethod]
        public void GetStatusWord_MissingReadData_ReturnsZero()
        {
            _dll.ReadData = null;

            Assert.AreEqual((ushort)0, _device.GetStatusWord(1));
        }
    }
}
