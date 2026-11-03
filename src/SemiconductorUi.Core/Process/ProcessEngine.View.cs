using System;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;

namespace SemiconductorUi.Process
{
    /// <summary>TM 정면/옆면 보기에 그릴 값 (원본 Form1UiUpdater.BuildSideViewState)</summary>
    public sealed class TmSideViewInfo
    {
        public EquipmentRegion Target { get; set; } = EquipmentRegion.TM;
        public long Height { get; set; }
        public double Extension { get; set; }
        public bool VacuumOn { get; set; }
        public bool WaferOnBlade { get; set; }
        public ArgbColor? WaferColor { get; set; }
        public bool[] FoupLevels { get; } = new bool[TmHeightEstimator.FoupLevels];
        public bool ChamberHasWafer { get; set; }
        public bool IsHardware { get; set; }
        public TmHardwareController.TmPositionSet Positions { get; set; }
    }

    // 원본 Form1UiUpdater의 TM 정면 보기 계산 부분 (표시 전용)
    public sealed partial class ProcessEngine
    {
        private TmHardwareController.TmPositionSet simulatedTeachingPositions;

        /// <summary>시뮬레이션 모드가 쓸 티칭 값을 바꾼다 (티칭 화면에서 저장했을 때).</summary>
        public void SetSimulatedTeachingPositions(TmHardwareController.TmPositionSet positions)
        {
            if (positions == null)
            {
                return;
            }

            var copy = new TmHardwareController.TmPositionSet();
            copy.ApplyFrom(positions.ToData());
            simulatedTeachingPositions = copy;
        }

        private TmHardwareController.TmPositionSet GetSimulatedTeachingPositions()
        {
            if (simulatedTeachingPositions == null)
            {
                var loaded = new TmHardwareController.TmPositionSet();
                try
                {
                    loaded.ApplyFrom(Repositories.TeachingPositionsRepository.Load());
                }
                catch (Exception ex)
                {
                    // 티칭 파일을 못 읽으면 기본 티칭 값으로 그린다 (표시 전용)
                    System.Diagnostics.Debug.WriteLine($"TM 정면 보기 티칭 값 로드 오류: {ex.Message}");
                }
                simulatedTeachingPositions = loaded;
            }
            return simulatedTeachingPositions;
        }

        /// <summary>블레이드에 그릴 웨이퍼 색 (없으면 null)</summary>
        public ArgbColor? GetBladeWaferColor()
        {
            if (IsTmHardwareModeAvailable())
            {
                return IsWaferShownOnBlade ? GetWaferColorForRegion(TmVisualTarget) : (ArgbColor?)null;
            }
            return IsWaferOnBlade() ? GetWaferColorForRegion(TmVisualTarget) : (ArgbColor?)null;
        }

        /// <summary>
        /// 원본 UpdateTmSideViewForSimulation / UpdateTmSideViewFromHardware.
        /// 시뮬레이션 모드는 실제 Axis1 값이 없으므로 이송 단계와 티칭 값으로 높이를 정한다.
        /// </summary>
        public TmSideViewInfo BuildTmSideView()
        {
            bool hardware = IsTmHardwareModeAvailable() && TmHardwareController != null;
            bool waferOnBlade = hardware ? IsWaferShownOnBlade : IsWaferOnBlade();
            var waferColor = GetBladeWaferColor();

            if (hardware)
            {
                return BuildSideViewState(TmVisualTarget, HardwareAxis1Position, TmCarryingVisual, waferOnBlade, waferColor, true,
                    TmHardwareController.Positions);
            }

            var phase = TmPhase;
            var transfer = CurrentTransfer;
            EquipmentRegion target;
            if (transfer == null || phase == TransferController.TmPhase.Idle)
            {
                target = TmVisualTarget;
            }
            else
            {
                target = TmHeightEstimator.IsDropoffPhase(phase) ? transfer.Dropoff : transfer.Pickup;
            }

            int level = GetFoupLevelForTransfer(target, transfer);
            var positions = GetSimulatedTeachingPositions();
            long height = transfer == null
                ? positions.Home_Y
                : TmHeightEstimator.EstimateForPhase(phase, target, positions, level);

            return BuildSideViewState(target, height, waferOnBlade, waferOnBlade, waferColor, false, positions);
        }

        private TmSideViewInfo BuildSideViewState(EquipmentRegion target, long height, bool vacuumOn, bool waferOnBlade,
            ArgbColor? waferColor, bool isHardware, TmHardwareController.TmPositionSet positions)
        {
            var state = new TmSideViewInfo
            {
                Target = target,
                Height = height,
                Extension = Math.Max(0f, Math.Min(1f, (TmBladeExtensionFactor - 0.7f) / 0.6f)),
                VacuumOn = vacuumOn,
                WaferOnBlade = waferOnBlade,
                WaferColor = waferColor,
                IsHardware = isHardware,
                Positions = positions,
            };

            var foupB = foupManager.FoupBCompleted;
            int foupBCount = foupB?.Count ?? 0;
            for (int level = 1; level <= TmHeightEstimator.FoupLevels; level++)
            {
                if (target == EquipmentRegion.FoupA)
                {
                    state.FoupLevels[level - 1] = foupManager.HasWaferAtLayerFoupA(level);
                }
                else if (target == EquipmentRegion.FoupB)
                {
                    // FOUP B는 1층부터 차례로 쌓인다
                    state.FoupLevels[level - 1] = level <= foupBCount;
                }
            }

            switch (target)
            {
                case EquipmentRegion.ChamberA: state.ChamberHasWafer = ChamberAState?.CurrentWafer != null; break;
                case EquipmentRegion.ChamberB: state.ChamberHasWafer = ChamberBState?.CurrentWafer != null; break;
                case EquipmentRegion.ChamberC: state.ChamberHasWafer = ChamberCState?.CurrentWafer != null; break;
            }

            return state;
        }

        /// <summary>
        /// 이번 이송이 FOUP의 몇 층을 쓰는지 (1~5).
        /// FOUP A는 웨이퍼 번호가 층 번호, FOUP B는 1층부터 쌓이므로 이미 놓였으면 그 자리, 아니면 다음 빈 층.
        /// </summary>
        private int GetFoupLevelForTransfer(EquipmentRegion target, TransferController.TransferTask transfer)
        {
            var wafer = transfer?.Wafer;
            if (target == EquipmentRegion.FoupA)
            {
                if (wafer != null && transfer.Pickup == EquipmentRegion.FoupA)
                {
                    return wafer.Id;
                }
                return ConfiguredFoupALoadCount - FoupARemainingInventoryCount + 1;
            }

            if (target == EquipmentRegion.FoupB)
            {
                var foupB = foupManager.FoupBCompleted;
                int index = wafer != null && foupB != null ? foupB.IndexOf(wafer) : -1;
                if (index >= 0)
                {
                    // 놓기 직후(뒤로 빼는 중)에는 이미 목록에 들어가 있다
                    return index + 1;
                }
                return (foupB?.Count ?? 0) + 1;
            }

            return 1;
        }

        /// <summary>챔버 그림에 그릴 웨이퍼 색 (원본 UpdateChamberWaferIndicator). 그릴 웨이퍼가 없으면 null.</summary>
        public ArgbColor? GetChamberWaferColor(EquipmentRegion region)
        {
            ChamberController.ChamberState chamber;
            switch (region)
            {
                case EquipmentRegion.ChamberA: chamber = ChamberAState; break;
                case EquipmentRegion.ChamberB: chamber = ChamberBState; break;
                case EquipmentRegion.ChamberC: chamber = ChamberCState; break;
                default: return null;
            }

            // 반입 전진 중(DropoffExtend)에는 웨이퍼가 아직 블레이드 위에 있으므로 챔버에 그리지 않습니다.
            // 웨이퍼는 전진이 끝나 챔버에 놓일 때(PerformDropoff) 챔버로 옮겨 그립니다.
            bool hasWafer =
                chamber?.CurrentWafer != null
                || (IsWaferEnRouteToChamber(chamber) && TmPhase != TransferController.TmPhase.DropoffExtend)
                || IsWaferAwaitingPickup(chamber);
            if (!hasWafer)
            {
                return null;
            }

            if (chamber?.CurrentWafer == null)
            {
                return ArgbColor.FromRgb(240, 230, 80); // 삽입/회수 중(문 열림) 웨이퍼
            }
            var color = GetWaferColorForState(chamber);
            return color.IsTransparent ? ArgbColor.FromRgb(210, 230, 255) : color;
        }
    }
}
