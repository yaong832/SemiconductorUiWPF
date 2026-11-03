using System;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;

namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// 모듈별 TM 높이(Axis1) 티칭 값과, 시뮬레이션 모드에서 쓰는 블레이드 높이 추정.
    ///
    /// 하드웨어 모드는 실제 Axis1 위치를 그대로 쓰므로 추정이 필요 없다.
    /// 시뮬레이션 모드에는 Axis1 값이 없어서, 이송 단계와 티칭 값으로 하드웨어와 같은 높이를 고른다.
    /// 집기: 하강 → 안착 → 상승, 놓기: 상승 → 안착 → 하강 (TmHardwareController.PickupWafer/DropWafer).
    /// </summary>
    public static class TmHeightEstimator
    {
        public const int FoupLevels = 5;

        /// <summary>
        /// 웨이퍼가 놓이는 높이. FOUP은 층(1~5)마다 다르다. 모듈이 아니면 원점 높이.
        /// </summary>
        public static long GetLandHeight(EquipmentRegion region, TmHardwareController.TmPositionSet positions, int foupLevel)
        {
            if (positions == null)
            {
                return 0;
            }

            switch (region)
            {
                case EquipmentRegion.ChamberA: return positions.ChamberA_LandY;
                case EquipmentRegion.ChamberB: return positions.ChamberB_LandY;
                case EquipmentRegion.ChamberC: return positions.ChamberC_LandY;
                case EquipmentRegion.FoupA: return positions.FoupA_LandY[ToSlotIndex(foupLevel)];
                case EquipmentRegion.FoupB: return positions.FoupB_LandY[ToSlotIndex(foupLevel)];
                default: return positions.Home_Y;
            }
        }

        /// <summary>
        /// 웨이퍼를 들어 올린 높이. 모듈이 아니면 원점 높이.
        /// </summary>
        public static long GetRaiseHeight(EquipmentRegion region, TmHardwareController.TmPositionSet positions, int foupLevel)
        {
            if (positions == null)
            {
                return 0;
            }

            switch (region)
            {
                case EquipmentRegion.ChamberA: return positions.ChamberA_RaiseY;
                case EquipmentRegion.ChamberB: return positions.ChamberB_RaiseY;
                case EquipmentRegion.ChamberC: return positions.ChamberC_RaiseY;
                case EquipmentRegion.FoupA: return positions.FoupA_RaiseY[ToSlotIndex(foupLevel)];
                case EquipmentRegion.FoupB: return positions.FoupB_RaiseY[ToSlotIndex(foupLevel)];
                default: return positions.Home_Y;
            }
        }

        /// <summary>
        /// 실린더를 넣고 빼는 안전 높이 (안착 - DescendOffset). 모듈이 아니면 원점 높이.
        /// </summary>
        public static long GetDescendHeight(EquipmentRegion region, TmHardwareController.TmPositionSet positions, int foupLevel)
        {
            if (positions == null)
            {
                return 0;
            }

            if (!IsModule(region))
            {
                return positions.Home_Y;
            }

            return GetLandHeight(region, positions, foupLevel) - positions.DescendOffset;
        }

        /// <summary>
        /// 시뮬레이션 모드의 이송 단계로 블레이드 높이를 고른다.
        /// region은 그 단계의 대상 모듈(집기 단계는 Pickup, 놓기 단계는 Dropoff)이다.
        /// </summary>
        public static long EstimateForPhase(TransferController.TmPhase phase, EquipmentRegion region, TmHardwareController.TmPositionSet positions, int foupLevel)
        {
            if (positions == null)
            {
                return 0;
            }

            switch (phase)
            {
                // 집기: 하강 위치에서 실린더를 넣고, 안착 높이에서 웨이퍼에 닿은 뒤, 상승 높이로 들어 올린다.
                case TransferController.TmPhase.MoveToPickup:
                case TransferController.TmPhase.MoveToPickup_WaitHardware:
                case TransferController.TmPhase.WaitDoorPickupOpen:
                case TransferController.TmPhase.PickupExtend_CylinderForward:
                    return GetDescendHeight(region, positions, foupLevel);

                case TransferController.TmPhase.PickupExtend:
                case TransferController.TmPhase.PickupExtend_ServoDown:
                case TransferController.TmPhase.PickupExtend_VacuumOn:
                    return GetLandHeight(region, positions, foupLevel);

                case TransferController.TmPhase.PickupRetract:
                case TransferController.TmPhase.PickupRetract_ServoUp:
                case TransferController.TmPhase.PickupRetract_CylinderBackward:
                case TransferController.TmPhase.WaitDoorPickupClose:
                    return GetRaiseHeight(region, positions, foupLevel);

                // 놓기: 상승 높이에서 실린더를 넣고, 안착 높이에 내려놓은 뒤, 하강 위치에서 실린더를 뺀다.
                case TransferController.TmPhase.MoveToDropoff:
                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                    return GetRaiseHeight(region, positions, foupLevel);

                case TransferController.TmPhase.DropoffExtend:
                case TransferController.TmPhase.DropoffExtend_ServoDown:
                case TransferController.TmPhase.DropoffExtend_VacuumOffExhaust:
                    return GetLandHeight(region, positions, foupLevel);

                case TransferController.TmPhase.DropoffRetract:
                case TransferController.TmPhase.DropoffRetract_ServoUp:
                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    return GetDescendHeight(region, positions, foupLevel);

                default:
                    return positions.Home_Y;
            }
        }

        /// <summary>
        /// 이 단계가 놓기 쪽(드롭오프)인지. 대상 모듈을 Pickup/Dropoff 중 무엇으로 볼지 정할 때 쓴다.
        /// </summary>
        public static bool IsDropoffPhase(TransferController.TmPhase phase)
        {
            switch (phase)
            {
                case TransferController.TmPhase.MoveToDropoff:
                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                case TransferController.TmPhase.DropoffExtend:
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                case TransferController.TmPhase.DropoffExtend_ServoDown:
                case TransferController.TmPhase.DropoffExtend_VacuumOffExhaust:
                case TransferController.TmPhase.DropoffRetract:
                case TransferController.TmPhase.DropoffRetract_ServoUp:
                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsModule(EquipmentRegion region)
        {
            return region == EquipmentRegion.ChamberA || region == EquipmentRegion.ChamberB || region == EquipmentRegion.ChamberC
                || region == EquipmentRegion.FoupA || region == EquipmentRegion.FoupB;
        }

        public static bool IsFoup(EquipmentRegion region)
        {
            return region == EquipmentRegion.FoupA || region == EquipmentRegion.FoupB;
        }

        private static int ToSlotIndex(int foupLevel)
        {
            return Math.Max(1, Math.Min(FoupLevels, foupLevel)) - 1;
        }
    }
}
