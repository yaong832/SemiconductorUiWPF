using System;
using SemiconductorUi.Hardware;
using SemiconductorUi.Controls;

namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// 챔버 하드웨어 제어 관련 Helper 클래스
    /// </summary>
    public static class ChamberHardwareHelper
    {
        /// <summary>
        /// 챔버 도어 제어를 위한 I/O 인덱스 정보
        /// </summary>
        public struct ChamberDoorIoIndices
        {
            public int CloseOutputIndex;
            public int OpenOutputIndex;
        }

        /// <summary>
        /// Region에 따른 챔버 도어 I/O 인덱스 가져오기
        /// </summary>
        /// <param name="region">EquipmentRegion</param>
        /// <returns>도어 I/O 인덱스 (Region이 챔버가 아니면 null)</returns>
        public static ChamberDoorIoIndices? GetChamberDoorIoIndices(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA:
                    return new ChamberDoorIoIndices
                    {
                        CloseOutputIndex = 4,
                        OpenOutputIndex = 5
                    };
                case EquipmentRegion.ChamberB:
                    return new ChamberDoorIoIndices
                    {
                        CloseOutputIndex = 7,
                        OpenOutputIndex = 8
                    };
                case EquipmentRegion.ChamberC:
                    return new ChamberDoorIoIndices
                    {
                        CloseOutputIndex = 10,
                        OpenOutputIndex = 11
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// 챔버 도어 제어 (EtherCAT)
        /// </summary>
        /// <param name="ethercatDevice">EtherCAT 장치</param>
        /// <param name="region">EquipmentRegion</param>
        /// <param name="open">열림 여부</param>
        /// <returns>성공 여부</returns>
        public static bool ControlChamberDoor(IEthercatDevice ethercatDevice, EquipmentRegion region, bool open)
        {
            if (ethercatDevice == null)
            {
                return false;
            }

            var ioIndices = GetChamberDoorIoIndices(region);
            if (!ioIndices.HasValue)
            {
                return false;
            }

            try
            {
                var indices = ioIndices.Value;
                
                // EtherTest 기준 실제 동작 (I/O 테이블 명칭과 반대):
                // "상승 SOL" ON = 도어 닫힘, "하강 SOL" ON = 도어 열림
                if (open)
                {
                    ethercatDevice.Digital_Output(indices.CloseOutputIndex, false);  // 닫기 OFF
                    ethercatDevice.Digital_Output(indices.OpenOutputIndex, true);    // 열기 ON
                }
                else
                {
                    ethercatDevice.Digital_Output(indices.OpenOutputIndex, false);   // 열기 OFF
                    ethercatDevice.Digital_Output(indices.CloseOutputIndex, true);   // 닫기 ON
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 챔버 램프 제어를 위한 I/O 인덱스 가져오기
        /// </summary>
        /// <param name="region">EquipmentRegion</param>
        /// <returns>램프 I/O 인덱스 (Region이 챔버가 아니면 -1)</returns>
        public static int GetChamberLampIoIndex(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA:
                    return 3;
                case EquipmentRegion.ChamberB:
                    return 6;
                case EquipmentRegion.ChamberC:
                    return 9;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 챔버 램프 제어 (EtherCAT)
        /// </summary>
        /// <param name="ethercatDevice">EtherCAT 장치</param>
        /// <param name="region">EquipmentRegion</param>
        /// <param name="on">켜기 여부</param>
        /// <returns>성공 여부</returns>
        public static bool ControlChamberLamp(IEthercatDevice ethercatDevice, EquipmentRegion region, bool on)
        {
            if (ethercatDevice == null)
            {
                return false;
            }

            int outputIndex = GetChamberLampIoIndex(region);
            if (outputIndex < 0)
            {
                return false;
            }

            try
            {
                ethercatDevice.Digital_Output(outputIndex, on);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 도어 열림 센서 입력 인덱스.
        /// EtherTest 기준: 챔버 도어에는 별도의 열림/닫힘 센서가 없음.
        /// 센서가 추가되면 여기에 실제 Digital_Input 인덱스를 넣고,
        /// HasDoorPositionSensors를 true로 바꾸면 됩니다.
        /// 출력 인덱스(4/5 등)와 같은 번호를 입력으로 쓰면 안 됩니다.
        /// </summary>
        public static int GetDoorOpenSensorInputIndex(EquipmentRegion region)
        {
            // 센서 미설치 — 확인 불가
            return -1;
        }

        /// <summary>
        /// 도어 닫힘 센서 입력 인덱스. 센서 미설치 시 -1.
        /// </summary>
        public static int GetDoorClosedSensorInputIndex(EquipmentRegion region)
        {
            return -1;
        }

        /// <summary>
        /// 도어 위치 센서가 실제로 연결되어 있는지.
        /// false이면 이송 시퀀스는 시간 대기만 사용해야 한다.
        /// </summary>
        public static bool HasDoorPositionSensors => false;

        /// <summary>
        /// 도어 열림 센서 확인.
        /// 센서 없음/오류/미연결 시 false (fail-closed: "열림 완료"로 오인하지 않음).
        /// </summary>
        public static bool CheckDoorSensorOpen(IEthercatDevice ethercatDevice, EquipmentRegion region)
        {
            if (!HasDoorPositionSensors || ethercatDevice == null)
            {
                return false;
            }

            int inputIndex = GetDoorOpenSensorInputIndex(region);
            if (inputIndex < 0)
            {
                return false;
            }

            try
            {
                return ethercatDevice.Digital_Input(inputIndex);
            }
            catch
            {
                return false; // fail-closed
            }
        }

        /// <summary>
        /// 도어 닫힘 센서 확인.
        /// 센서 없음/오류/미연결 시 false (fail-closed: "닫힘 완료"로 오인하지 않음).
        /// </summary>
        public static bool CheckDoorSensorClosed(IEthercatDevice ethercatDevice, EquipmentRegion region)
        {
            if (!HasDoorPositionSensors || ethercatDevice == null)
            {
                return false;
            }

            int inputIndex = GetDoorClosedSensorInputIndex(region);
            if (inputIndex < 0)
            {
                return false;
            }

            try
            {
                return ethercatDevice.Digital_Input(inputIndex);
            }
            catch
            {
                return false; // fail-closed
            }
        }
    }
}

