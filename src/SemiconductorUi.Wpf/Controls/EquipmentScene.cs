using System;
using System.Collections.Generic;
using System.Windows.Media;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Wpf.Rendering;

namespace SemiconductorUi.Wpf.Controls
{
    /// <summary>장비 캔버스에 그릴 상태 한 묶음. 뷰모델이 값을 바꾼 뒤 NotifyChanged()를 부른다.</summary>
    public sealed class EquipmentScene
    {
        public sealed class Chamber
        {
            public string Title { get; set; }
            public Color LampColor { get; set; } = Palette.LampOff;
            public double ValveOpen { get; set; }
            public bool HasWafer { get; set; }
            public Color WaferColor { get; set; } = Palette.DefaultWafer;
        }

        public sealed class Foup
        {
            public int WaferCount { get; set; }
            public int Capacity { get; set; } = 25;
            public bool Mounted { get; set; } = true;
        }

        public Dictionary<EquipmentRegion, Chamber> Chambers { get; } = new Dictionary<EquipmentRegion, Chamber>
        {
            [EquipmentRegion.ChamberA] = new Chamber { Title = "Chamber A" },
            [EquipmentRegion.ChamberB] = new Chamber { Title = "Chamber B" },
            [EquipmentRegion.ChamberC] = new Chamber { Title = "Chamber C" },
        };

        public Foup FoupA { get; } = new Foup();
        public Foup FoupB { get; } = new Foup();

        public Color LampRed { get; set; } = Palette.LampOff;
        public Color LampYellow { get; set; } = Palette.LampOff;
        public Color LampGreen { get; set; } = Palette.LampOff;

        public EquipmentRegion TmTarget { get; set; } = EquipmentRegion.TM;
        public bool TmCarrying { get; set; }
        public double TmExtension { get; set; } = 0.55;
        public bool IsSimulation { get; set; } = true;
        /// <summary>하드웨어 모드의 회전축(Axis2) 엔코더 값. 있으면 TM 각도를 이 값으로 그린다.</summary>
        public long? TmEncoderX { get; set; }
        /// <summary>엔코더 값을 각도로 바꿀 때 쓰는 모듈별 티칭 X값</summary>
        public IList<KeyValuePair<EquipmentRegion, long>> TmTeachX { get; set; }
        public Color TmWaferColor { get; set; } = Palette.DefaultWafer;

        public TmSideViewState SideView { get; set; } = new TmSideViewState();
        public TmHardwareController.TmPositionSet TeachingPositions { get; set; } = new TmHardwareController.TmPositionSet();

        public event EventHandler Changed;

        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
