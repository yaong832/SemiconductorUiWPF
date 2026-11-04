using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.ViewModels;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    // 원본 Form1.cs에서 그대로 옮긴 공정 코드. UI 호출만 IProcessHost로 바꿨다.
    public sealed partial class ProcessEngine
    {
        internal int GetTmMoveDurationTicks(EquipmentRegion from, EquipmentRegion to)
        {
            if (from == to)
            {
                return 1;
            }

            bool fromFoup = from == EquipmentRegion.FoupA || from == EquipmentRegion.FoupB;
            bool toFoup = to == EquipmentRegion.FoupA || to == EquipmentRegion.FoupB;
            bool fromChamber = !fromFoup && from != EquipmentRegion.TM;
            bool toChamber = !toFoup && to != EquipmentRegion.TM;

            if ((fromFoup && toChamber) || (fromChamber && toFoup))
            {
                return AppSettings.TmMoveLongTicks;
            }

            if (fromChamber && toChamber)
            {
                return AppSettings.TmMoveMediumTicks;
            }

            return AppSettings.TmMoveShortTicks;
        }

        internal void ProcessTm()
        {
            tmProcessor?.ProcessTm();
        }

        /// <summary>
        /// Chamber 공정 시작 처리 (WaferTrackingService로 위임)
        /// 중요: 실제 장비가 돌아가는 로직이므로 정확한 상태 관리 필수
        /// </summary>
        internal void StartChamberProcessing(ChamberController.ChamberState chamber, Wafer wafer)
        {
            waferTrackingService?.StartChamberProcessing(chamber, wafer);
            
            // UI 업데이트
            Host.RefreshUi();
        }

        /// <summary>
        /// TM Phase별 하드웨어 완료 조건 확인
        /// </summary>
        internal bool CheckTmPhaseHardwareComplete()
        {
            switch (TmPhase)
            {
                case TransferController.TmPhase.MoveToPickup_WaitHardware:
                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                    return CheckTmHardwareActionComplete();

                case TransferController.TmPhase.PickupExtend_CylinderForward:
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                    return CheckTmCylinderExtended();

                case TransferController.TmPhase.PickupRetract_CylinderBackward:
                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                    return CheckTmCylinderRetracted();

                case TransferController.TmPhase.PickupExtend_ServoDown:
                case TransferController.TmPhase.DropoffExtend_ServoDown:
                case TransferController.TmPhase.PickupRetract_ServoUp:
                case TransferController.TmPhase.DropoffRetract_ServoUp:
                    return CheckTmAxis1MoveComplete();

                // 도어 열기 대기 - 실제 도어 센서 확인
                case TransferController.TmPhase.WaitDoorPickupOpen:
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                    return CheckDoorOpenForCurrentRegion();

                // 도어 닫기 대기 - 실제 도어 센서 확인
                case TransferController.TmPhase.WaitDoorPickupClose:
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    return CheckDoorClosedForCurrentRegion();

                default:
                    return true;
            }
        }

        /// <summary>
        /// 현재 작업 영역의 도어가 열렸는지 확인 (하드웨어 센서)
        /// </summary>
        private bool CheckDoorOpenForCurrentRegion()
        {
            if (CurrentTransfer == null) return true;
            
            EquipmentRegion region;
            if (TmPhase == TransferController.TmPhase.WaitDoorPickupOpen)
                region = CurrentTransfer.Pickup;
            else if (TmPhase == TransferController.TmPhase.WaitDoorDropoffOpen)
                region = CurrentTransfer.Dropoff;
            else
                return true;
            
            // 중요: 도어에는 별도의 열림/닫힘 센서가 없음 (EtherTest 참고)
            // 도어 열기 명령 전송 후 시간 기반으로 처리
            return true; // 센서 없음 - 시간 기반으로 처리 (doorOpenConsecutiveChecks 사용)
        }

        /// <summary>
        /// 현재 작업 영역의 도어가 닫혔는지 확인 (하드웨어 센서)
        /// </summary>
        private bool CheckDoorClosedForCurrentRegion()
        {
            if (CurrentTransfer == null) return true;
            
            EquipmentRegion region;
            if (TmPhase == TransferController.TmPhase.WaitDoorPickupClose)
                region = CurrentTransfer.Pickup;
            else if (TmPhase == TransferController.TmPhase.WaitDoorDropoffClose)
                region = CurrentTransfer.Dropoff;
            else
                return true;
            
            // 중요: 도어에는 별도의 열림/닫힘 센서가 없음 (EtherTest 참고)
            // 도어 닫기 명령 전송 후 일정 시간이 지나면 닫힌 것으로 간주
            // 실제 장비에서 도어가 닫히는 데 약 2초 소요 추정
            return true; // 센서 없음 - 시간 기반으로 처리 (doorCloseWaitTicks 사용)
        }

        /// <summary>
        /// 도어 센서 상태 확인 (EtherCAT 하드웨어).
        /// 현재 장비에는 도어 위치 센서가 없음 → 항상 false (열림 미확인).
        /// 이송 대기 로직은 CheckDoorOpenForCurrentRegion의 시간 대기를 사용한다.
        /// </summary>
        private bool CheckDoorSensorOpen(EquipmentRegion region)
        {
            if (!EthercatConnected) return false;
            return ChamberHardwareHelper.CheckDoorSensorOpen(EtherCAT_M, region);
        }

        /// <summary>
        /// 도어 닫힘 센서 상태 확인. 센서 미설치 시 항상 false (닫힘 미확인).
        /// </summary>
        private bool CheckDoorSensorClosed(EquipmentRegion region)
        {
            if (!EthercatConnected) return false;
            return ChamberHardwareHelper.CheckDoorSensorClosed(EtherCAT_M, region);
        }

        /// <summary>
        /// 픽업 위치 이동 완료 처리
        /// </summary>
        internal void ProcessTmMoveToPickupComplete()
        {
            // 하드웨어 모드: 위치 확인 (PP_D는 이미 CheckTmHardwareActionComplete()에서 확인됨)
            // 중요: PP_D 중복 확인 제거 - CheckTmHardwareActionComplete()에서 타임아웃 포함하여 처리함
            if (IsTmHardwareModeAvailable())
            {
                try
                {
                    // 위치 로그만 출력 (검증 목적)
                    TmHardwareController.UpdateCurrentPositions();
                    long currentX = TmHardwareController.CurrentAxis2Position;
                    long currentY = TmHardwareController.CurrentAxis1Position;
                    
                    AddLogMessage($"TM 픽업 위치 도달 확인: X={currentX}, Y={currentY}", "INFO");
                }
                catch (Exception ex)
                {
                    AddLogMessage($"TM 위치 확인 오류: {ex.Message}", "ERROR");
                }
            }
            
            TmCurrentPosition = CurrentTransfer.Pickup;
            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Pickup))
            {
                // 하드웨어 모드: TM이 실제로 도착했을 때 도어 열기 명령 전송
                if (IsTmHardwareModeAvailable())
                {
                    // 중요: 도어 열기 명령을 명시적으로 전송 (센서 상태와 무관하게)
                    EnsureDoorOpenForRegion(CurrentTransfer.Pickup);
                    AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer.Pickup)} 도어 열기 명령 전송 (TM 도착)", "INFO");
                    // 하드웨어 모드에서는 WaitDoorPickupOpen Phase에서 도어 열림 확인
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorPickupOpen, 0, CurrentTransfer.Pickup, false);
                    StartTmHardwareAction();
                }
                else
                {
                    // 시뮬레이션 모드: 즉시 도어 열기
                    EnsureDoorOpenForRegion(CurrentTransfer.Pickup);
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorPickupOpen, AppSettings.TmDoorActionTicks, CurrentTransfer.Pickup, false);
                }
            }
            else
            {
                if (IsTmHardwareModeAvailable())
                {
                    // 하드웨어 모드: 실린더 전진 시작
                    if (StartTmCylinderExtend())
                    {
                        tmProcessor?.BeginTmPhase(TransferController.TmPhase.PickupExtend_CylinderForward, 0, CurrentTransfer.Pickup, false);
                    }
                    else
                    {
                        tmProcessor?.AbortCurrentHardwareTransfer("픽업 전 실린더 전진 실패");
                    }
                }
                else
                {
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.PickupExtend, AppSettings.TmPickupDurationTicks, CurrentTransfer.Pickup, false);
                }
            }
        }

        /// <summary>
        /// 픽업 후퇴 완료 처리
        /// </summary>
        internal void ProcessTmPickupRetractComplete()
        {
            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Pickup))
            {
                EnsureDoorClosedForRegion(CurrentTransfer.Pickup);
                tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorPickupClose, AppSettings.TmDoorActionTicks, CurrentTransfer.Pickup, true);
                // 하드웨어 모드: 도어 닫힘 센서 대기
                if (IsTmHardwareModeAvailable())
                {
                    StartTmHardwareAction();
                }
            }
            else
            {
                var moveTicksDirect = GetTmMoveDurationTicks(CurrentTransfer.Pickup, CurrentTransfer.Dropoff);
                tmProcessor?.BeginTmPhase(TransferController.TmPhase.MoveToDropoff, moveTicksDirect, CurrentTransfer.Dropoff, true);
            }
        }

        /// <summary>
        /// 드롭오프 위치 이동 완료 처리
        /// </summary>
        internal void ProcessTmMoveToDropoffComplete()
        {
            // 하드웨어 모드: 위치 확인 (PP_D는 이미 CheckTmHardwareActionComplete()에서 확인됨)
            // 중요: PP_D 중복 확인 제거 - CheckTmHardwareActionComplete()에서 타임아웃 포함하여 처리함
            if (IsTmHardwareModeAvailable())
            {
                try
                {
                    // 위치 로그만 출력 (검증 목적)
                    TmHardwareController.UpdateCurrentPositions();
                    long currentX = TmHardwareController.CurrentAxis2Position;
                    long currentY = TmHardwareController.CurrentAxis1Position;
                    
                    AddLogMessage($"TM 드롭오프 위치 도달 확인: X={currentX}, Y={currentY}", "INFO");
                }
                catch (Exception ex)
                {
                    AddLogMessage($"TM 위치 확인 오류: {ex.Message}", "ERROR");
                }
            }
            
            TmCurrentPosition = CurrentTransfer.Dropoff;
            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Dropoff))
            {
                // 하드웨어 모드: TM이 실제로 도착했을 때 도어 열기 명령 전송
                if (IsTmHardwareModeAvailable())
                {
                    // 중요: 도어 열기 명령을 명시적으로 전송 (센서 상태와 무관하게)
                    EnsureDoorOpenForRegion(CurrentTransfer.Dropoff);
                    AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer.Dropoff)} 도어 열기 명령 전송 (TM 도착)", "INFO");
                    // 하드웨어 모드에서는 WaitDoorDropoffOpen Phase에서 도어 열림 확인
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorDropoffOpen, 0, CurrentTransfer.Dropoff, true);
                    StartTmHardwareAction();
                }
                else
                {
                    // 시뮬레이션 모드: 즉시 도어 열기
                    EnsureDoorOpenForRegion(CurrentTransfer.Dropoff);
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorDropoffOpen, AppSettings.TmDoorActionTicks, CurrentTransfer.Dropoff, true);
                }
            }
            else
            {
                if (IsTmHardwareModeAvailable())
                {
                    if (StartTmCylinderExtend())
                    {
                        tmProcessor?.BeginTmPhase(TransferController.TmPhase.DropoffExtend_CylinderForward, 0, CurrentTransfer.Dropoff, true);
                    }
                    else
                    {
                        tmProcessor?.AbortCurrentHardwareTransfer("드롭오프 전 실린더 전진 실패");
                    }
                }
                else
                {
                    tmProcessor?.BeginTmPhase(TransferController.TmPhase.DropoffExtend, AppSettings.TmDropoffDurationTicks, CurrentTransfer.Dropoff, true);
                }
            }
        }

        /// <summary>
        /// 드롭오프 후퇴 완료 처리
        /// </summary>
        internal void ProcessTmDropoffRetractComplete()
        {
            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Dropoff))
            {
                EnsureDoorClosedForRegion(CurrentTransfer.Dropoff);
                tmProcessor?.BeginTmPhase(TransferController.TmPhase.WaitDoorDropoffClose, AppSettings.TmDoorActionTicks, CurrentTransfer.Dropoff, false);
                // 하드웨어 모드: 도어 닫힘 센서 대기
                if (IsTmHardwareModeAvailable())
                {
                    StartTmHardwareAction();
                }
            }
            else
            {
                TmCurrentPosition = CurrentTransfer.Dropoff;
                FinishCurrentTransfer();
            }
        }

        internal bool PerformPickup()
        {
            if (CurrentTransfer == null)
            {
                return false;
            }

            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Pickup) && !IsRegionDoorOpen(CurrentTransfer.Pickup))
            {
                EnsureDoorOpenForRegion(CurrentTransfer.Pickup);
                return false;
            }

            if (CurrentTransfer.SourceChamber != null)
            {
                var source = CurrentTransfer.SourceChamber;
                source.CurrentWafer = null;
                source.PickupScheduled = false;
                source.StatusText = ChamberStatusTexts.Idle;
                source.TotalSeconds = 0;
                source.RemainingSeconds = 0;
                source.ProcessingAccumulator = 0;
            }
            else if (CurrentTransfer.FromFoup && foupManager?.HasWafersInFoupA() == true)
            {
                foupManager.DequeueWaferFromFoupA();
                // TM가 FOUP A에서 웨이퍼를 픽업하면 FOUP 실제 잔량 1 감소
                if (FoupARemainingInventoryCount > 0)
                {
                    FoupARemainingInventoryCount--;
                }
            }

            return true;
        }

        internal bool PerformDropoff()
        {
            if (CurrentTransfer == null)
            {
                return false;
            }

            if (EquipmentRegionHelper.RequiresDoor(CurrentTransfer.Dropoff) && !IsRegionDoorOpen(CurrentTransfer.Dropoff))
            {
                EnsureDoorOpenForRegion(CurrentTransfer.Dropoff);
                return false;
            }

            if (CurrentTransfer.SourceChamber == ChamberAState)
            {
                chamberCompletedCountA++;
            }
            else if (CurrentTransfer.SourceChamber == ChamberBState)
            {
                chamberCompletedCountB++;
            }
            else if (CurrentTransfer.SourceChamber == ChamberCState)
            {
                chamberCompletedCountC++;
            }

            if (CurrentTransfer.DestinationChamber != null)
            {
                var destination = CurrentTransfer.DestinationChamber;
                destination.ReservedForIncoming = false;
                destination.CurrentWafer = CurrentTransfer.Wafer;
                destination.TotalSeconds = destination.Step.DurationSeconds;
                destination.RemainingSeconds = destination.Step.DurationSeconds;
                destination.StatusText = ChamberStatusTexts.DoorClosing;
                destination.ProcessingAccumulator = 0;
                Host.RefreshUi();
                // 중요: 공정 시작은 도어가 닫힌 후에만 시작 (WaitDoorDropoffClose Phase에서 처리)
                // 시나리오: T=60초 웨이퍼 투입 → T=63~65초 도어 닫기 → 공정 시작
            }
            else if (CurrentTransfer.OnCompleted != null)
            {
                CurrentTransfer.OnCompleted(CurrentTransfer.Wafer);
            }

            AddLogMessage($"TM 완료: {EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer.Pickup)} -> {EquipmentRegionHelper.FormatRegionLabel(CurrentTransfer.Dropoff)} (웨이퍼 #{CurrentTransfer.Wafer?.Id})", "INFO");
            TmCarryingVisual = false;
            return true;
        }

        internal void FinishCurrentTransfer()
        {
            TransferService?.ResetToIdle();
            if (TransferService?.QueueCount > 0)
            {
                tmProcessor?.StartNextTransfer();
            }
            else
            {
                UpdateTmAnimationIdleTarget();
            }
        }

        private bool IsChamberDoorClosed(ChamberController.ChamberState chamber)
        {
            if (chamber == null)
            {
                return true;
            }

            var region = chamber.Region;
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return true;
            }

            return !IsRegionDoorOpen(region);
        }

        internal void UpdateTmAnimationIdleTarget()
        {
            // 원본 Form1UiUpdater.UpdateTmAnimationIdleTarget
            if (TmPhase != TransferController.TmPhase.Idle || CurrentTransfer != null)
            {
                return;
            }

            var idleTarget = EquipmentRegion.TM;
            if (TmVisualTarget != idleTarget || TmCarryingVisual)
            {
                TmVisualTarget = idleTarget;
                TmCarryingVisual = false;
                TmBladeExtensionFactor = 0.55f;
                if (IsTmHardwareModeAvailable())
                {
                    UpdateTmVisualizationFromHardware();
                }
                else
                {
                    UpdateTmVisualization();
                }
            }
        }
    }
}
