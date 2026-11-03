using System;
using System.Collections.Generic;
using System.Threading;
using IEG3268_Dll;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 비상정지 잠금이 서보를 끄고, 해제 전까지 명령을 DLL로 보내지 않는지 확인합니다.
    /// 막힌 명령은 DLL까지 가지 않으므로 실제 드라이버 없이 확인할 수 있습니다.
    /// </summary>
    [TestClass]
    public class EmergencyStopTests
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

        private const byte ServoOnControlWord = 15;

        private IEG3268 _dll;
        private FakeCifxChannel _channel;
        private Ieg3268Device _device;

        [TestInitialize]
        public void Setup()
        {
            _dll = new IEG3268();
            _channel = new FakeCifxChannel();
            _device = new Ieg3268Device(_dll, _channel, isConnected: true);
        }

        [TestMethod]
        public void EmergencyStop_WritesServoOffToBothAxesInOneWrite()
        {
            _dll.WriteData[0] = ServoOnControlWord;
            _dll.WriteData[23] = ServoOnControlWord;
            _dll.WriteData[46] = 0x5A; // 디지털 출력은 그대로

            _device.EmergencyStop();

            Assert.IsTrue(_device.IsEmergencyStopped);
            Assert.AreEqual(1, _channel.Writes.Count);
            Assert.AreEqual(0x00, _channel.Writes[0][0], "축1 제어워드");
            Assert.AreEqual(0x00, _channel.Writes[0][23], "축2 제어워드");
            Assert.AreEqual(0x5A, _channel.Writes[0][46], "디지털 출력");
        }

        [TestMethod]
        public void EmergencyStop_NotConnected_LatchesAndClearsBufferWithoutWriting()
        {
            var device = new Ieg3268Device(_dll, _channel);
            _dll.WriteData[0] = ServoOnControlWord;
            _dll.WriteData[23] = ServoOnControlWord;

            device.EmergencyStop();

            Assert.IsTrue(device.IsEmergencyStopped);
            Assert.AreEqual(0, _channel.Writes.Count);
            Assert.AreEqual(0x00, _dll.WriteData[0]);
            Assert.AreEqual(0x00, _dll.WriteData[23]);
        }

        [TestMethod]
        public void WhileStopped_MotionCommandsAreRejectedBeforeReachingDll()
        {
            _device.EmergencyStop();
            int writesAfterStop = _channel.Writes.Count;

            var commands = new Dictionary<string, Action>
            {
                { "Axis1_ON", () => _device.Axis1_ON() },
                { "Axis2_ON", () => _device.Axis2_ON() },
                { "Axis1_UD_Move_Send", () => _device.Axis1_UD_Move_Send() },
                { "Axis2_LR_Move_Send", () => _device.Axis2_LR_Move_Send() },
                { "Axis1_UD_Homming", () => _device.Axis1_UD_Homming() },
                { "Axis2_LR_Homming", () => _device.Axis2_LR_Homming() },
                { "Axis1_UD_POS_Update", () => _device.Axis1_UD_POS_Update(1000) },
                { "Axis2_LR_POS_Update", () => _device.Axis2_LR_POS_Update(1000) },
                { "Axis1_UD_Config_Update", () => _device.Axis1_UD_Config_Update(1, 2, 3, 4) },
                { "Axis2_LR_Config_Update", () => _device.Axis2_LR_Config_Update(1, 2, 3, 4) },
                { "ResetAxisFault", () => _device.ResetAxisFault(1) },
            };

            foreach (var command in commands)
            {
                try
                {
                    command.Value();
                    Assert.Fail(command.Key + " 명령이 막히지 않았습니다.");
                }
                catch (EmergencyStopActiveException)
                {
                }
            }

            Assert.AreEqual(writesAfterStop, _channel.Writes.Count, "막힌 명령은 아무것도 보내지 않아야 합니다.");
            Assert.AreEqual(0x00, _dll.WriteData[0], "축1 제어워드는 0으로 남아야 합니다.");
            Assert.AreEqual(0x00, _dll.WriteData[23], "축2 제어워드는 0으로 남아야 합니다.");
            Assert.AreEqual(0x00, _dll.WriteData[2], "축1 운전 모드는 바뀌지 않아야 합니다.");
            Assert.AreEqual(0x00, _dll.WriteData[25], "축2 운전 모드는 바뀌지 않아야 합니다.");
        }

        [TestMethod]
        public void WhileStopped_OutputsThatMoveOrReleaseAreRejected()
        {
            _device.EmergencyStop();

            var blocked = new[]
            {
                new { Index = 12, Value = true },  // 실린더 전진
                new { Index = 13, Value = true },  // 실린더 후진
                new { Index = 4, Value = true },   // 도어 A 닫기
                new { Index = 5, Value = true },   // 도어 A 열기
                new { Index = 14, Value = false }, // 진공 OFF
                new { Index = 15, Value = true },  // 배기 ON
            };

            foreach (var output in blocked)
            {
                // 막으려는 값의 반대로 기록해 두고, 막힌 뒤에도 그대로인지 봅니다.
                _dll.Digital_Out_Value[output.Index] = !output.Value;

                Assert.ThrowsException<EmergencyStopActiveException>(
                    () => _device.Digital_Output(output.Index, output.Value),
                    "출력 " + output.Index + "=" + output.Value);
                Assert.AreEqual(!output.Value, _dll.Digital_Out_Value[output.Index], "출력 기록이 바뀌지 않아야 합니다.");
            }

            Assert.AreEqual(1, _channel.Writes.Count, "비상정지 쓰기 한 번 외에는 보내지 않아야 합니다.");
        }

        [TestMethod]
        public void Policy_AllowsOnlyLampsVacuumOnAndExhaustOff()
        {
            for (int index = 0; index < 16; index++)
            {
                bool isLamp = index == 0 || index == 1 || index == 2 || index == 3 || index == 6 || index == 9;
                bool allowedOn = isLamp || index == 14;
                bool allowedOff = isLamp || index == 15;

                Assert.AreEqual(allowedOn, EmergencyStopPolicy.IsOutputAllowedWhileStopped(index, true), "출력 " + index + " ON");
                Assert.AreEqual(allowedOff, EmergencyStopPolicy.IsOutputAllowedWhileStopped(index, false), "출력 " + index + " OFF");
            }
        }

        [TestMethod]
        public void ClearEmergencyStop_AllowsNewOperations()
        {
            _device.EmergencyStop();
            _device.ClearEmergencyStop();

            Assert.IsFalse(_device.IsEmergencyStopped);
            _device.ThrowIfEmergencyStopped();
            using (_device.BeginOperation())
            {
                _device.ThrowIfEmergencyStopped();
            }
        }

        [TestMethod]
        public void BeginOperation_WhileStopped_Throws()
        {
            _device.EmergencyStop();

            Assert.ThrowsException<EmergencyStopActiveException>(() => _device.BeginOperation());
        }

        [TestMethod]
        public void OperationStartedBeforeStop_StaysRejectedAfterClear()
        {
            using (_device.BeginOperation())
            {
                _device.EmergencyStop();
                _device.ClearEmergencyStop();

                Assert.ThrowsException<EmergencyStopActiveException>(() => _device.ThrowIfEmergencyStopped());
                Assert.ThrowsException<EmergencyStopActiveException>(() => _device.Axis1_UD_Homming());
            }

            // 범위가 끝나면 이후 호출에는 영향이 없습니다.
            _device.ThrowIfEmergencyStopped();
        }

        [TestMethod]
        public void HardwareOperationRun_TaskStartedBeforeStop_IsRejectedAfterClear()
        {
            var started = new ManualResetEventSlim();
            var proceed = new ManualResetEventSlim();
            Exception observed = null;

            var task = HardwareOperation.Run(_device, () =>
            {
                started.Set();
                proceed.Wait();
                try
                {
                    _device.ThrowIfEmergencyStopped();
                }
                catch (Exception ex)
                {
                    observed = ex;
                }
            });

            Assert.IsTrue(started.Wait(5000));
            _device.EmergencyStop();
            _device.ClearEmergencyStop();
            proceed.Set();
            Assert.IsTrue(task.Wait(5000));

            Assert.IsInstanceOfType(observed, typeof(EmergencyStopActiveException));

            // 작업을 시작한 쪽(테스트 스레드)은 동작 범위 밖이라 영향이 없습니다.
            _device.ThrowIfEmergencyStopped();
        }

        [TestMethod]
        public void HardwareOperationRun_WhileStopped_ThrowsWithoutStarting()
        {
            _device.EmergencyStop();
            bool ran = false;

            Assert.ThrowsException<EmergencyStopActiveException>(
                () => HardwareOperation.Run(_device, () => { ran = true; }));
            Assert.IsFalse(ran);
        }

        [TestMethod]
        public void IsAxisOperationEnabled_ReadsBit2OfEachStatusWord()
        {
            var readData = new byte[96];
            readData[0] = 0x37;  // 축1 Operation enabled
            readData[20] = 0x40; // 축2 Switch on disabled
            _dll.ReadData = readData;

            Assert.IsTrue(_device.IsAxisOperationEnabled(1));
            Assert.IsFalse(_device.IsAxisOperationEnabled(2));
        }
    }
}
