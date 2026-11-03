using System;
using System.Collections.Generic;
using IEG3268_Dll;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 회전(축2)과 실린더 전진이 겹치지 않게 막는 규칙을 확인합니다.
    /// 앞부분은 규칙만, 뒷부분은 장비 창구가 규칙을 지키는지 확인합니다.
    /// 장비 창구 쪽은 막히는 경우만 확인합니다. 허용되는 명령은 DLL을 거쳐 드라이버까지 가기 때문입니다.
    /// </summary>
    [TestClass]
    public class TmMotionInterlockTests
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

        // DLL Digital_Input: 입력 0~7은 ReadData[40], 8~15는 ReadData[41]의 비트 0~7.
        private const int DigitalInputHighByte = 41;
        private const byte RetractedSensorBit = 0x10; // 입력 12

        private long _now;
        private TmMotionInterlock _interlock;
        private IEG3268 _dll;
        private FakeCifxChannel _channel;
        private Ieg3268Device _device;

        [TestInitialize]
        public void Setup()
        {
            _now = 10000;
            _interlock = new TmMotionInterlock(() => _now);
            _dll = new IEG3268();
            _dll.ReadData = new byte[96];
            _channel = new FakeCifxChannel();
            _device = new Ieg3268Device(_dll, _channel, true, _interlock);
        }

        #region 규칙: 회전

        [TestMethod]
        public void Rotation_RetractedAndNoRecentExtend_IsAllowed()
        {
            _interlock.CheckRotationAllowed("회전 이동", false, () => true);
        }

        [TestMethod]
        public void Rotation_ExtendOutputOn_IsRefusedEvenIfSensorSaysRetracted()
        {
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckRotationAllowed("회전 이동", true, () => true));
        }

        [TestMethod]
        public void Rotation_RetractedSensorOff_IsRefused()
        {
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckRotationAllowed("회전 이동", false, () => false));
        }

        [TestMethod]
        public void Rotation_SensorReadFails_IsRefused()
        {
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckRotationAllowed("회전 이동", false, () => throw new InvalidOperationException("읽기 실패")));
        }

        [TestMethod]
        public void Rotation_WithinOneSecondOfExtendCommand_IsRefusedEvenIfSensorSaysRetracted()
        {
            // 전진했다가 곧바로 후진한 경우: 입력이 아직 전진 전의 "후진" 값일 수 있습니다.
            _interlock.NoteCylinderOutput(TmMotionInterlock.CylinderExtendOutput, true);
            _interlock.NoteCylinderOutput(TmMotionInterlock.CylinderExtendOutput, false);

            _now += TmMotionInterlock.ExtendToRotationMinMs - 1;
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckRotationAllowed("회전 이동", false, () => true));

            _now += 1;
            _interlock.CheckRotationAllowed("회전 이동", false, () => true);
        }

        #endregion

        #region 규칙: 실린더 전진

        [TestMethod]
        public void ExtendSideOutputs_AreExtendOnAndRetractOff()
        {
            Assert.IsTrue(TmMotionInterlock.IsExtendSideOutput(12, true));
            Assert.IsTrue(TmMotionInterlock.IsExtendSideOutput(13, false));
            Assert.IsFalse(TmMotionInterlock.IsExtendSideOutput(12, false));
            Assert.IsFalse(TmMotionInterlock.IsExtendSideOutput(13, true));
            Assert.IsFalse(TmMotionInterlock.IsExtendSideOutput(14, true));
            Assert.IsFalse(TmMotionInterlock.IsExtendSideOutput(4, true));
        }

        [TestMethod]
        public void Extend_NoRotationPending_IsAllowedWithoutReadingStatus()
        {
            _interlock.CheckCylinderOutputAllowed(12, true, () => throw new AssertFailedException("상태를 읽으면 안 됩니다."));
        }

        [TestMethod]
        public void Extend_RightAfterRotationCommand_IsRefusedEvenIfStatusSaysDone()
        {
            // 명령 직후의 "도착" 값은 명령 전 값일 수 있으므로 믿지 않습니다.
            _interlock.NoteRotationCommand();
            _now += TmMotionInterlock.RotationSettleMinMs - 1;

            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckCylinderOutputAllowed(12, true, () => true));
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckCylinderOutputAllowed(13, false, () => true));
            Assert.IsTrue(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Extend_WhileRotationNotDone_IsRefused()
        {
            _interlock.NoteRotationCommand();
            _now += 5000;

            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckCylinderOutputAllowed(12, true, () => false));
            Assert.IsTrue(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Extend_AfterRotationDone_IsAllowedAndClearsPending()
        {
            _interlock.NoteRotationCommand();
            _now += TmMotionInterlock.RotationSettleMinMs;

            _interlock.CheckCylinderOutputAllowed(13, false, () => true);

            Assert.IsFalse(_interlock.IsRotationPending);
            _interlock.CheckCylinderOutputAllowed(12, true, () => throw new AssertFailedException("다시 읽을 필요가 없습니다."));
        }

        [TestMethod]
        public void Extend_StatusReadFails_IsRefused()
        {
            _interlock.NoteRotationCommand();
            _now += 5000;

            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckCylinderOutputAllowed(12, true, () => throw new InvalidOperationException("읽기 실패")));
        }

        [TestMethod]
        public void RetractAndOtherOutputs_AreAllowedDuringRotation()
        {
            _interlock.NoteRotationCommand();

            Func<bool> mustNotRead = () => throw new AssertFailedException("상태를 읽으면 안 됩니다.");
            _interlock.CheckCylinderOutputAllowed(12, false, mustNotRead); // 전진 OFF
            _interlock.CheckCylinderOutputAllowed(13, true, mustNotRead);  // 후진 ON
            _interlock.CheckCylinderOutputAllowed(14, false, mustNotRead); // 진공
            _interlock.CheckCylinderOutputAllowed(15, true, mustNotRead);  // 배기
            _interlock.CheckCylinderOutputAllowed(5, true, mustNotRead);   // 도어
            Assert.IsTrue(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Extend_AfterRotationStopped_WaitsOneSecond()
        {
            _interlock.NoteRotationCommand();
            _now += 200;
            _interlock.NoteRotationStopped();

            Func<bool> mustNotRead = () => throw new AssertFailedException("멈춘 뒤에는 상태 대신 시간으로 판단합니다.");
            _now += TmMotionInterlock.RotationStopSettleMs - 1;
            Assert.ThrowsException<HardwareInterlockException>(
                () => _interlock.CheckCylinderOutputAllowed(12, true, mustNotRead));

            _now += 1;
            _interlock.CheckCylinderOutputAllowed(12, true, mustNotRead);
            Assert.IsFalse(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void RotationStopped_WithNothingPending_DoesNotBlockExtend()
        {
            _interlock.NoteRotationStopped();

            Assert.IsFalse(_interlock.IsRotationPending);
            _interlock.CheckCylinderOutputAllowed(12, true, () => false);
        }

        #endregion

        #region 장비 창구

        [TestMethod]
        public void Device_RetractedSensorMapsToInput12()
        {
            _dll.ReadData[DigitalInputHighByte] = RetractedSensorBit;

            Assert.IsTrue(_device.Digital_Input(12));
            Assert.IsFalse(_device.Digital_Input(13));
        }

        [TestMethod]
        public void Device_RotationCommands_RetractedSensorOff_AreRefusedBeforeReachingDll()
        {
            _dll.ReadData[DigitalInputHighByte] = 0;

            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Move_Send());
            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Homming());

            AssertRotationNotSent();
            Assert.IsFalse(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Device_RotationCommands_ExtendOutputOn_AreRefusedEvenIfSensorSaysRetracted()
        {
            _dll.ReadData[DigitalInputHighByte] = RetractedSensorBit;
            _dll.Digital_Out_Value[12] = true;

            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Move_Send());
            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Homming());

            AssertRotationNotSent();
        }

        [TestMethod]
        public void Device_RotationCommands_NoInputData_AreRefused()
        {
            _dll.ReadData = null;

            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Move_Send());
            Assert.ThrowsException<HardwareInterlockException>(() => _device.Axis2_LR_Homming());

            AssertRotationNotSent();
        }

        [TestMethod]
        public void Device_EmergencyStopIsCheckedBeforeInterlock()
        {
            _device.EmergencyStop();

            Assert.ThrowsException<EmergencyStopActiveException>(() => _device.Axis2_LR_Move_Send());
        }

        [TestMethod]
        public void Device_ExtendOutputs_WhileRotating_AreRefusedBeforeReachingDll()
        {
            _interlock.NoteRotationCommand();
            _now += 5000;
            _dll.WriteData[25] = 1; // 위치 이동 모드, 상태워드 "도착" 비트는 꺼짐
            _dll.Digital_Out_Value[12] = false;
            _dll.Digital_Out_Value[13] = true;

            Assert.ThrowsException<HardwareInterlockException>(() => _device.Digital_Output(13, false));
            Assert.ThrowsException<HardwareInterlockException>(() => _device.Digital_Output(12, true));

            Assert.IsFalse(_dll.Digital_Out_Value[12], "전진 출력 기록이 바뀌지 않아야 합니다.");
            Assert.IsTrue(_dll.Digital_Out_Value[13], "후진 출력 기록이 바뀌지 않아야 합니다.");
            Assert.AreEqual(0, _channel.Writes.Count);
            Assert.IsTrue(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Device_EmergencyStop_MarksRotationStopped()
        {
            _interlock.NoteRotationCommand();
            _now += 100;

            _device.EmergencyStop();

            _now += TmMotionInterlock.RotationStopSettleMs;
            _interlock.CheckCylinderOutputAllowed(12, true, () => throw new AssertFailedException("멈춘 뒤에는 시간으로 판단합니다."));
            Assert.IsFalse(_interlock.IsRotationPending);
        }

        [TestMethod]
        public void Device_QuickStopAll_MarksRotationStopped()
        {
            _interlock.NoteRotationCommand();
            _now += 100;

            _device.QuickStopAll();

            _now += TmMotionInterlock.RotationStopSettleMs;
            _interlock.CheckCylinderOutputAllowed(12, true, () => throw new AssertFailedException("멈춘 뒤에는 시간으로 판단합니다."));
            Assert.IsFalse(_interlock.IsRotationPending);
        }

        private void AssertRotationNotSent()
        {
            // DLL의 회전 명령은 운전 모드(WriteData[25])부터 바꾸므로, 그대로면 DLL까지 가지 않은 것입니다.
            Assert.AreEqual(0x00, _dll.WriteData[25], "축2 운전 모드는 바뀌지 않아야 합니다.");
            Assert.AreEqual(0x00, _dll.WriteData[23], "축2 제어워드는 바뀌지 않아야 합니다.");
            Assert.AreEqual(0, _channel.Writes.Count);
        }

        #endregion
    }
}
