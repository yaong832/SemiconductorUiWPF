using System;

// 원본 WinForms 프로젝트와의 호환을 위해 네임스페이스는 SemiconductorUi.Controls를 유지한다.
namespace SemiconductorUi.Controls
{
    /// <summary>장비 다이어그램의 영역 구분</summary>
    public enum EquipmentRegion
    {
        None,
        TM,
        ChamberA,
        ChamberB,
        ChamberC,
        FoupA,
        FoupB
    }

    public sealed class EquipmentRegionClickedEventArgs : EventArgs
    {
        public EquipmentRegionClickedEventArgs(EquipmentRegion region)
        {
            Region = region;
        }

        public EquipmentRegion Region { get; }
    }
}
