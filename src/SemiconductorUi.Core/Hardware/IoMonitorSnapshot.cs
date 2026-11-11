using System;

namespace SemiconductorUi.Hardware
{
    /// <summary>
    /// 원본 Forms.IoMonitorForm (b791ae9, a9d1610)의 화면과 무관한 부분: 입출력 이름표, 서보 상태워드 해석, 한 번 읽기.
    /// 읽기 전용이다. 장비에 아무 명령도 보내지 않고, DLL이 0.3초마다 받아 둔 값을 읽기만 한다.
    /// 출력은 DLL로 단자 상태를 다시 읽을 수 없어서 "마지막으로 보낸 값"이다.
    /// </summary>
    public sealed class IoMonitorSnapshot
    {
        public const int PointCount = 16;
        public const string UnusedName = "미사용";

        /// <summary>입력 번호별 이름. 코드에서 쓰지 않는 번호는 "미사용"</summary>
        public static readonly string[] InputNames = BuildInputNames();

        /// <summary>출력 번호별 이름</summary>
        public static readonly string[] OutputNames =
        {
            "적색 램프",
            "황색 램프",
            "녹색 램프",
            "Chamber A 램프",
            "Chamber A 도어 닫힘 (상승 SOL)",
            "Chamber A 도어 열림 (하강 SOL)",
            "Chamber B 램프",
            "Chamber B 도어 닫힘 (상승 SOL)",
            "Chamber B 도어 열림 (하강 SOL)",
            "Chamber C 램프",
            "Chamber C 도어 닫힘 (상승 SOL)",
            "Chamber C 도어 열림 (하강 SOL)",
            "실린더 전진",
            "실린더 후진",
            "진공 (흡착)",
            "배기"
        };

        private static string[] BuildInputNames()
        {
            var names = new string[PointCount];
            for (int i = 0; i < PointCount; i++) names[i] = UnusedName;
            names[12] = "실린더 후진 센서";
            names[13] = "실린더 전진 센서";
            return names;
        }

        public bool Connected { get; private set; }
        public bool EmergencyStopped { get; private set; }
        /// <summary>읽다가 오류가 나면 그 이유 (정상이면 null)</summary>
        public string Error { get; private set; }
        public bool[] Inputs { get; private set; }
        public bool[] Outputs { get; private set; }
        public ServoAxisSnapshot Axis1 { get; private set; }
        public ServoAxisSnapshot Axis2 { get; private set; }
        public DateTime Time { get; private set; }

        /// <summary>값을 한 번 읽는다 (원본 IoMonitorForm.RefreshValues). 미연결이면 값 없이 돌려준다.</summary>
        public static IoMonitorSnapshot Read(IEthercatDevice device, bool connected, DateTime now)
        {
            var snapshot = new IoMonitorSnapshot { Time = now };
            if (device == null || !connected) return snapshot;

            snapshot.Connected = true;
            var inputs = new bool[PointCount];
            var outputs = new bool[PointCount];
            try
            {
                for (int i = 0; i < PointCount; i++)
                {
                    inputs[i] = device.Digital_Input(i);
                    outputs[i] = device.GetDigitalOutput(i);
                }
                snapshot.Axis1 = ServoAxisSnapshot.Read(device, 1);
                snapshot.Axis2 = ServoAxisSnapshot.Read(device, 2);
                snapshot.EmergencyStopped = device.IsEmergencyStopped;
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
                return snapshot;
            }
            snapshot.Inputs = inputs;
            snapshot.Outputs = outputs;
            return snapshot;
        }
    }

    /// <summary>축 하나의 상태 (CiA402 상태워드 해석 포함). 원본 IoMonitorForm.ServoAxisSnapshot</summary>
    public sealed class ServoAxisSnapshot
    {
        /// <summary>화면에 보여 줄 주요 비트 (원본 a9d1610: 5개)</summary>
        public static readonly (int Bit, string Name)[] KeyBits =
        {
            (2, "서보 ON"),
            (3, "알람"),
            (4, "전압"),
            (7, "경고"),
            (10, "목표 도달"),
        };

        public int Axis { get; set; }
        public ushort StatusWord { get; set; }
        public string Position { get; set; }

        public static ServoAxisSnapshot Read(IEthercatDevice device, int axis)
        {
            return new ServoAxisSnapshot
            {
                Axis = axis,
                StatusWord = device.GetStatusWord(axis),
                Position = axis == 1 ? device.Axis1_is_PosData() : device.Axis2_is_PosData()
            };
        }

        public bool Bit(int bit) => (StatusWord & (1 << bit)) != 0;

        public bool IsFault => Bit(3);

        /// <summary>CiA402 상태 이름 (상태워드 하위 비트 조합)</summary>
        public string StateName
        {
            get
            {
                int masked = StatusWord & 0x6F;
                if ((StatusWord & 0x4F) == 0x00) return "Not ready";
                if ((StatusWord & 0x4F) == 0x40) return "Switch on disabled";
                if (masked == 0x21) return "Ready to switch on";
                if (masked == 0x23) return "Switched on";
                if (masked == 0x27) return "Operation enabled (서보 ON)";
                if (masked == 0x07) return "Quick stop active";
                if ((StatusWord & 0x4F) == 0x0F) return "Fault reaction active";
                if ((StatusWord & 0x4F) == 0x08) return "Fault (알람)";
                return "알 수 없음";
            }
        }
    }
}
