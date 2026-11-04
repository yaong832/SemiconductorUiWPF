using System;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using AppSettings = SemiconductorUi.AppSettings;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// Form1의 TM 프로세스 로직을 담당하는 헬퍼 클래스
    /// Form1.cs의 복잡도를 줄이기 위해 TM 프로세스 관련 메서드들을 분리
    /// </summary>
    public class TmProcessor
    {
        private readonly ProcessEngine form;

        /// <summary>
        /// TmProcessor 생성자
        /// </summary>
        /// <param name="form">Form1 인스턴스</param>
        /// <exception cref="ArgumentNullException">form이 null인 경우</exception>
        public TmProcessor(ProcessEngine form)
        {
            this.form = form ?? throw new ArgumentNullException(nameof(form));
        }

        /// <summary>
        /// 같은 틱/스택에서 StartNextTransfer 재귀를 피하기 위한 지연 시작 플래그.
        /// ProcessTm 진입 시 한 번만 소비한다.
        /// </summary>
        private bool _startNextTransferRequested;

        /// <summary>
        /// TM 프로세스 메인 처리 메서드
        /// TransferController의 현재 Phase에 따라 적절한 처리 메서드를 호출합니다.
        /// </summary>
        public void ProcessTm()
        {
            if (_startNextTransferRequested)
            {
                _startNextTransferRequested = false;
                if (form.CurrentTransfer == null && form.TransferService?.QueueCount > 0)
                {
                    StartNextTransfer();
                    // 같은 틱에서 Phase까지 연속 처리하지 않음 (실패 재시도 폭주 방지)
                    return;
                }
            }

            if (form.TmPhase == TransferController.TmPhase.Idle)
            {
                // Idle 상태이고 큐에 작업이 있으면 시작
                if (form.CurrentTransfer == null && form.TransferService?.QueueCount > 0)
                {
                    StartNextTransfer();
                }
                return;
            }

            // CurrentTransfer null 체크 (안전성 강화)
            // 단, 큐에 다음 작업이 있으면 StartNextTransfer() 호출 (Transfer 완료 후 다음 작업 시작)
            if (form.CurrentTransfer == null)
            {
                if (form.TransferService?.QueueCount > 0)
                {
                    // 현재 Transfer가 완료되었고 큐에 다음 작업이 있으면 시작
                    // (하드웨어 모드에서 동작 중이 아닐 때만)
                    if (!(form.IsTmHardwareModeAvailable() && form.TmHardwareActionPending))
                    {
                        form.AddLogMessage("ProcessTm: 현재 Transfer 완료, 다음 작업 시작", "INFO");
                        StartNextTransfer();
                        return;
                    }
                    else
                    {
                        // 하드웨어 동작 중이면 대기
                        return;
                    }
                }
                else
                {
                    // 큐에 작업이 없으면 Idle 상태로 리셋
                    form.AddLogMessage("ProcessTm: CurrentTransfer가 null이고 큐가 비어있습니다. Idle 상태로 리셋합니다.", "WARN");
                    form.TmHardwareActionPending = false;
                    form.TmSettleWaiting = false;
                    form.TransferService?.ResetToIdle();
                    return;
                }
            }

            // 하드웨어 모드에서 동작 대기 중인 경우
            if (form.IsTmHardwareModeAvailable() && form.TmHardwareActionPending)
            {
                // 타임아웃 체크
                if (form.CheckTmHardwareTimeout())
                {
                    form.HandleHardwareError($"TM 하드웨어 동작 타임아웃 (Phase: {form.TmPhase}, 타임아웃: {AppSettings.TmHardwareActionTimeoutMs}ms)");
                    // 일시정지되므로 즉시 다음 이송을 시작하지 않음
                    AbortTransferAndScheduleRetry($"하드웨어 동작 타임아웃 (Phase: {form.TmPhase})", requestNext: false);
                }
                else
                {
                    // Phase별 완료 조건 확인
                    if (!form.CheckTmPhaseHardwareComplete())
                    {
                        return; // 아직 동작 중
                    }
                    
                    // 동작 완료 - 안정화 대기 시작
                    if (!form.TmSettleWaiting)
                    {
                        form.TmSettleWaiting = true;
                        form.tmSettleStartTime = DateTime.Now;
                        form.tmCurrentSettleDelay = form.GetSettleDelayForPhase(form.TmPhase);
                        form.AddLogMessage($"[안정화] {form.TmPhase} 완료 - {form.tmCurrentSettleDelay}ms 대기", "INFO");
                        return;
                    }
                    
                    // 안정화 대기 중
                    if ((DateTime.Now - form.tmSettleStartTime).TotalMilliseconds < form.tmCurrentSettleDelay)
                    {
                        return; // 아직 안정화 대기 중
                    }
                    
                    // 안정화 완료
                    form.TmHardwareActionPending = false;
                    form.TmSettleWaiting = false;
                    // 안정화 완료 후 바로 다음 Phase 처리 (타이머 대기 없이)
                    // break 없이 switch문으로 계속 진행
                }
            }

            // 시뮬레이션 모드: 틱 기반 대기
            if (!form.IsTmHardwareModeAvailable())
            {
                if (form.TransferService != null && form.TmPhaseTicksRemaining > 0)
                {
                    bool phaseCompleted = form.TransferService.DecrementPhaseTick();
                    if (!phaseCompleted)
                    {
                        return;
                    }
                }
            }

            switch (form.TmPhase)
            {
                // ========== 픽업 위치로 이동 ==========
                case TransferController.TmPhase.MoveToPickup:
                    ProcessTmPhase_MoveToPickup();
                    break;

                case TransferController.TmPhase.MoveToPickup_WaitHardware:
                    ProcessTmPhase_MoveToPickup_WaitHardware();
                    break;

                // ========== 픽업 도어 처리 ==========
                case TransferController.TmPhase.WaitDoorPickupOpen:
                    ProcessTmPhase_WaitDoorPickupOpen();
                    break;

                // ========== 픽업 동작 (시뮬레이션 모드) ==========
                case TransferController.TmPhase.PickupExtend:
                    ProcessTmPhase_PickupExtend();
                    break;

                // ========== 픽업 동작 (하드웨어 모드) ==========
                case TransferController.TmPhase.PickupExtend_CylinderForward:
                    ProcessTmPhase_PickupExtend_CylinderForward();
                    break;

                case TransferController.TmPhase.PickupExtend_ServoDown:
                    ProcessTmPhase_PickupExtend_ServoDown();
                    break;

                case TransferController.TmPhase.PickupExtend_VacuumOn:
                    ProcessTmPhase_PickupExtend_VacuumOn();
                    break;

                // ========== 픽업 후 복귀 ==========
                case TransferController.TmPhase.PickupRetract:
                    ProcessTmPhase_PickupRetract();
                    break;

                case TransferController.TmPhase.PickupRetract_ServoUp:
                    ProcessTmPhase_PickupRetract_ServoUp();
                    break;

                case TransferController.TmPhase.PickupRetract_CylinderBackward:
                    ProcessTmPhase_PickupRetract_CylinderBackward();
                    break;

                // ========== 픽업 도어 닫기 ==========
                case TransferController.TmPhase.WaitDoorPickupClose:
                    ProcessTmPhase_WaitDoorPickupClose();
                    break;

                // ========== 드롭오프 위치로 이동 ==========
                case TransferController.TmPhase.MoveToDropoff:
                    ProcessTmPhase_MoveToDropoff();
                    break;

                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                    ProcessTmPhase_MoveToDropoff_WaitHardware();
                    break;

                // ========== 드롭오프 도어 처리 ==========
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                    ProcessTmPhase_WaitDoorDropoffOpen();
                    break;

                // ========== 드롭오프 동작 (시뮬레이션 모드) ==========
                case TransferController.TmPhase.DropoffExtend:
                    ProcessTmPhase_DropoffExtend();
                    break;

                // ========== 드롭오프 동작 (하드웨어 모드) ==========
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                    ProcessTmPhase_DropoffExtend_CylinderForward();
                    break;

                case TransferController.TmPhase.DropoffExtend_ServoDown:
                    ProcessTmPhase_DropoffExtend_ServoDown();
                    break;

                case TransferController.TmPhase.DropoffExtend_VacuumOffExhaust:
                    ProcessTmPhase_DropoffExtend_VacuumOffExhaust();
                    break;

                // ========== 드롭오프 후 복귀 ==========
                case TransferController.TmPhase.DropoffRetract:
                    ProcessTmPhase_DropoffRetract();
                    break;

                case TransferController.TmPhase.DropoffRetract_ServoUp:
                    ProcessTmPhase_DropoffRetract_ServoUp();
                    break;

                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                    ProcessTmPhase_DropoffRetract_CylinderBackward();
                    break;

                // ========== 드롭오프 도어 닫기 ==========
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    ProcessTmPhase_WaitDoorDropoffClose();
                    break;
            }
        }

        #region ProcessTm Phase Handlers

        /// <summary>
        /// ProcessTm: MoveToPickup Phase 처리
        /// </summary>
        private void ProcessTmPhase_MoveToPickup()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 픽업을 위해 안착 높이로 이동
                // 공정 중간에는 서보가 이동하면서 HOME_D가 false가 될 수 있으므로,
                // 원점복귀 체크는 StartNextTransfer()에서 공정 시작 시에만 수행
                if (form.StartTmServoMoveForPickup(form.CurrentTransfer.Pickup))
                {
                    BeginTmPhase(TransferController.TmPhase.MoveToPickup_WaitHardware, 0, form.CurrentTransfer.Pickup, false);
                }
                else
                {
                    // 실패 시: 성공 경로로 진행하지 않고 중단·재시도
                    AbortTransferAndScheduleRetry("픽업 서보 이동 실패");
                }
            }
            else
            {
                // 시뮬레이션 모드: 즉시 완료
                form.ProcessTmMoveToPickupComplete();
            }
        }

        /// <summary>
        /// ProcessTm: MoveToPickup_WaitHardware Phase 처리
        /// </summary>
        private void ProcessTmPhase_MoveToPickup_WaitHardware()
        {
            // 하드웨어 이동 완료 - 다음 단계로
            form.ProcessTmMoveToPickupComplete();
        }

        /// <summary>
        /// ProcessTm: WaitDoorPickupOpen Phase 처리
        /// </summary>
        private void ProcessTmPhase_WaitDoorPickupOpen()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 도어 열기 명령 전송 후 시간 기반 대기
                // 중요: 도어에는 별도의 열림 센서가 없음 (EtherTest 참고)
                
                // 도어 열기 명령이 아직 전송되지 않았으면 전송
                if (!form.doorOpenCommandSent)
                {
                    form.EnsureDoorOpenForRegion(form.CurrentTransfer.Pickup);
                    form.doorOpenCommandSent = true;
                    form.doorOpenConsecutiveChecks = 0; // 틱 카운터로 재사용
                    form.StartTmHardwareAction();
                    form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Pickup)} 도어 열기 명령 전송", "INFO");
                }
                
                form.doorOpenConsecutiveChecks++;
                
                // 도어 열림 대기: 센서 없음 → DoorOpenWaitTicks (기본 14틱 ≈ 2.1초 @150ms)
                if (form.doorOpenConsecutiveChecks < AppSettings.DoorOpenWaitTicks)
                {
                    // 아직 대기 중
                    return;
                }
                
                // 대기 시간 완료 - 도어가 열린 것으로 간주
                form.doorOpenCommandSent = false;
                form.doorOpenConsecutiveChecks = 0;
                form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Pickup)} 도어 열림 완료 (시간 기반) - 실린더 전진 시작", "INFO");
                
                // 도어가 확실히 열렸으면 실린더 전진 시작
                if (form.StartTmCylinderExtend())
                {
                    BeginTmPhase(TransferController.TmPhase.PickupExtend_CylinderForward, 0, form.CurrentTransfer.Pickup, false);
                }
                else
                {
                    AbortTransferAndScheduleRetry("픽업 전 실린더 전진 실패");
                }
            }
            else
            {
                // 시뮬레이션 모드: 즉시 다음 단계로
                form.doorOpenConsecutiveChecks = 0; // 리셋
                BeginTmPhase(TransferController.TmPhase.PickupExtend, AppSettings.TmPickupDurationTicks, form.CurrentTransfer.Pickup, false);
            }
        }

        /// <summary>
        /// ProcessTm: PickupExtend Phase 처리 (시뮬레이션 모드)
        /// </summary>
        private void ProcessTmPhase_PickupExtend()
        {
            if (!form.PerformPickup())
            {
                AbortTransferAndScheduleRetry("PerformPickup 실패");
                return;
            }
            BeginTmPhase(TransferController.TmPhase.PickupRetract, AppSettings.TmPickupRetractTicks, form.CurrentTransfer.Pickup, true);
        }

        /// <summary>
        /// ProcessTm: PickupExtend_CylinderForward Phase 처리 (하드웨어 모드)
        /// </summary>
        private void ProcessTmPhase_PickupExtend_CylinderForward()
        {
            // 실린더 전진 완료 확인 -> 안착 위치로 상승 (웨이퍼와 접촉)
            // 주의: 현재 하강 위치(DescendY)에 있으므로, 안착 위치(LandY)로 상승해야 함
            
            // 실린더 전진 타임아웃 체크
            if (form.TmHardwareActionPending && (DateTime.Now - form.tmHardwareActionStartTime).TotalMilliseconds > AppSettings.CylinderActionTimeoutMs)
            {
                form.HandleHardwareError($"TM 실린더 전진 타임아웃 (Phase: {form.TmPhase}, 타임아웃: {AppSettings.CylinderActionTimeoutMs}ms)");
                AbortTransferAndScheduleRetry("실린더 전진 타임아웃", requestNext: false);
                return;
            }
            
            if (form.CheckTmCylinderExtended())
            {
                // 실린더 전진 완료 -> 하드웨어 동작 플래그 리셋
                form.TmHardwareActionPending = false;
                
                // 실린더 전진 완료 -> 서보 이동
                if (form.StartTmServoToLand(form.CurrentTransfer.Pickup))
                {
                    BeginTmPhase(TransferController.TmPhase.PickupExtend_ServoDown, 0, form.CurrentTransfer.Pickup, false);
                }
                else
                {
                    // 안착 실패 시 진공 ON 폴백 금지 — 웨이퍼 위치 불일치 위험
                    AbortTransferAndScheduleRetry("안착(Land) 서보 이동 실패");
                }
            }
            else
            {
                // 실린더 전진 완료 대기 (타이머로 재시도)
                BeginTmPhase(TransferController.TmPhase.PickupExtend_CylinderForward, 1, form.CurrentTransfer.Pickup, false);
            }
        }

        /// <summary>
        /// ProcessTm: PickupExtend_ServoDown Phase 처리
        /// </summary>
        private void ProcessTmPhase_PickupExtend_ServoDown()
        {
            // 안착 위치 도달 완료 -> 진공 ON (웨이퍼 흡착)
            if (form.IsTmHardwareModeAvailable())
            {
                if (!form.SetTmVacuumOn())
                {
                    AbortTransferAndScheduleRetry("진공 ON 실패");
                    return;
                }
            }
            
            // 논리적 픽업 수행 - 실패 시 Transfer 취소
            if (!form.PerformPickup())
            {
                AbortTransferAndScheduleRetry("PerformPickup 실패");
                return;
            }
            
            BeginTmPhase(TransferController.TmPhase.PickupExtend_VacuumOn, 2, form.CurrentTransfer.Pickup, false);
        }

        /// <summary>
        /// ProcessTm: PickupExtend_VacuumOn Phase 처리
        /// </summary>
        private void ProcessTmPhase_PickupExtend_VacuumOn()
        {
            // 진공 안정화 완료 -> 서보 상승
            if (form.StartTmServoUp(form.CurrentTransfer.Pickup))
            {
                BeginTmPhase(TransferController.TmPhase.PickupRetract_ServoUp, 0, form.CurrentTransfer.Pickup, true);
            }
            else
            {
                AbortTransferAndScheduleRetry("픽업 후 상승(Raise) 서보 이동 실패");
            }
        }

        /// <summary>
        /// ProcessTm: PickupRetract Phase 처리 (시뮬레이션 모드)
        /// </summary>
        private void ProcessTmPhase_PickupRetract()
        {
            form.ProcessTmPickupRetractComplete();
        }

        /// <summary>
        /// ProcessTm: PickupRetract_ServoUp Phase 처리
        /// </summary>
        private void ProcessTmPhase_PickupRetract_ServoUp()
        {
            // 서보 상승 완료 -> 실린더 후진
            if (form.StartTmCylinderRetract())
            {
                BeginTmPhase(TransferController.TmPhase.PickupRetract_CylinderBackward, 0, form.CurrentTransfer.Pickup, true);
            }
            else
            {
                AbortTransferAndScheduleRetry("픽업 후 실린더 후진 실패");
            }
        }

        /// <summary>
        /// ProcessTm: PickupRetract_CylinderBackward Phase 처리
        /// </summary>
        private void ProcessTmPhase_PickupRetract_CylinderBackward()
        {
            // 실린더 후진 완료 확인 -> 도어 닫기 또는 드롭오프 이동
            
            // 실린더 후진 타임아웃 체크
            if (form.TmHardwareActionPending && (DateTime.Now - form.tmHardwareActionStartTime).TotalMilliseconds > AppSettings.CylinderActionTimeoutMs)
            {
                form.HandleHardwareError($"TM 실린더 후진 타임아웃 (Phase: {form.TmPhase}, 타임아웃: {AppSettings.CylinderActionTimeoutMs}ms)");
                AbortTransferAndScheduleRetry("픽업 후 실린더 후진 타임아웃", requestNext: false);
                return;
            }
            
            if (form.CheckTmCylinderRetracted())
            {
                // 실린더 후진 완료 -> 하드웨어 동작 플래그 리셋
                form.TmHardwareActionPending = false;
                form.ProcessTmPickupRetractComplete();
            }
            else
            {
                // 실린더 후진 완료 대기 (타이머로 재시도)
                BeginTmPhase(TransferController.TmPhase.PickupRetract_CylinderBackward, 1, form.CurrentTransfer.Pickup, true);
            }
        }

        /// <summary>
        /// ProcessTm: WaitDoorPickupClose Phase 처리
        /// </summary>
        private void ProcessTmPhase_WaitDoorPickupClose()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 도어 닫기 명령 전송 후 시간 기반 대기
                // 중요: 도어에는 별도의 닫힘 센서가 없음 (EtherTest 참고)
                
                if (!form.doorCloseCommandSent)
                {
                    form.EnsureDoorClosedForRegion(form.CurrentTransfer.Pickup);
                    form.doorCloseCommandSent = true;
                    form.doorCloseWaitTicks = 0;
                    form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Pickup)} 도어 닫기 명령 전송", "INFO");
                }
                
                if (!form.TmHardwareActionPending)
                {
                    form.StartTmHardwareAction();
                }
                
                form.doorCloseWaitTicks++;
                
                // 도어 닫힘 대기 시간: 약 0.75초 (5틱 * 150ms)
                if (form.doorCloseWaitTicks < AppSettings.DoorCloseWaitTicks)
                {
                    // 아직 대기 중
                    return;
                }
                
                // 대기 시간 완료 - 도어가 닫힌 것으로 간주
                form.doorCloseCommandSent = false;
                form.doorCloseWaitTicks = 0;
                form.TmHardwareActionPending = false;
                form.TmSettleWaiting = false;
                form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Pickup)} 도어 닫힘 완료 (시간 기반)", "INFO");
            }
            
            var moveTicksToDropoff = form.GetTmMoveDurationTicks(form.CurrentTransfer.Pickup, form.CurrentTransfer.Dropoff);
            BeginTmPhase(TransferController.TmPhase.MoveToDropoff, moveTicksToDropoff, form.CurrentTransfer.Dropoff, true);
        }

        /// <summary>
        /// ProcessTm: MoveToDropoff Phase 처리
        /// </summary>
        private void ProcessTmPhase_MoveToDropoff()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 드롭을 위해 상승 높이로 이동
                if (form.StartTmServoMoveForDropoff(form.CurrentTransfer.Dropoff))
                {
                    BeginTmPhase(TransferController.TmPhase.MoveToDropoff_WaitHardware, 0, form.CurrentTransfer.Dropoff, true);
                }
                else
                {
                    AbortTransferAndScheduleRetry("드롭오프 서보 이동 실패");
                }
            }
            else
            {
                form.ProcessTmMoveToDropoffComplete();
            }
        }

        /// <summary>
        /// ProcessTm: MoveToDropoff_WaitHardware Phase 처리
        /// </summary>
        private void ProcessTmPhase_MoveToDropoff_WaitHardware()
        {
            form.ProcessTmMoveToDropoffComplete();
        }

        /// <summary>
        /// ProcessTm: WaitDoorDropoffOpen Phase 처리
        /// </summary>
        private void ProcessTmPhase_WaitDoorDropoffOpen()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 도어 열기 명령 전송 후 시간 기반 대기
                // 중요: 도어에는 별도의 열림 센서가 없음 (EtherTest 참고)
                
                // 도어 열기 명령이 아직 전송되지 않았으면 전송
                if (!form.doorOpenCommandSent)
                {
                    form.EnsureDoorOpenForRegion(form.CurrentTransfer.Dropoff);
                    form.doorOpenCommandSent = true;
                    form.doorOpenConsecutiveChecks = 0; // 틱 카운터로 재사용
                    form.StartTmHardwareAction();
                    form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Dropoff)} 도어 열기 명령 전송", "INFO");
                }
                
                form.doorOpenConsecutiveChecks++;
                
                // 도어 열림 대기: 센서 없음 → DoorOpenWaitTicks (기본 14틱 ≈ 2.1초 @150ms)
                if (form.doorOpenConsecutiveChecks < AppSettings.DoorOpenWaitTicks)
                {
                    // 아직 대기 중
                    return;
                }
                
                // 대기 시간 완료 - 도어가 열린 것으로 간주
                form.doorOpenCommandSent = false;
                form.doorOpenConsecutiveChecks = 0;
                form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Dropoff)} 도어 열림 완료 (시간 기반) - 실린더 전진 시작", "INFO");
                
                // 도어가 확실히 열렸으면 실린더 전진 시작
                if (form.StartTmCylinderExtend())
                {
                    BeginTmPhase(TransferController.TmPhase.DropoffExtend_CylinderForward, 0, form.CurrentTransfer.Dropoff, true);
                }
                else
                {
                    AbortTransferAndScheduleRetry("드롭오프 전 실린더 전진 실패");
                }
            }
            else
            {
                // 시뮬레이션 모드: 즉시 다음 단계로
                form.doorOpenConsecutiveChecks = 0; // 리셋
                BeginTmPhase(TransferController.TmPhase.DropoffExtend, AppSettings.TmDropoffDurationTicks, form.CurrentTransfer.Dropoff, true);
            }
        }

        /// <summary>
        /// ProcessTm: DropoffExtend Phase 처리 (시뮬레이션 모드)
        /// </summary>
        private void ProcessTmPhase_DropoffExtend()
        {
            if (!form.PerformDropoff())
            {
                BeginTmPhase(TransferController.TmPhase.DropoffExtend, 1, form.CurrentTransfer.Dropoff, true);
                return;
            }
            BeginTmPhase(TransferController.TmPhase.DropoffRetract, AppSettings.TmDropoffRetractTicks, form.CurrentTransfer.Dropoff, false);
        }

        /// <summary>
        /// ProcessTm: DropoffExtend_CylinderForward Phase 처리 (하드웨어 모드)
        /// </summary>
        private void ProcessTmPhase_DropoffExtend_CylinderForward()
        {
            // 실린더 전진 완료 확인 -> 서보 하강
            
            // 실린더 전진 타임아웃 체크
            if (form.TmHardwareActionPending && (DateTime.Now - form.tmHardwareActionStartTime).TotalMilliseconds > AppSettings.CylinderActionTimeoutMs)
            {
                form.HandleHardwareError($"TM 실린더 전진 타임아웃 (Phase: {form.TmPhase}, 타임아웃: {AppSettings.CylinderActionTimeoutMs}ms)");
                AbortTransferAndScheduleRetry("드롭오프 실린더 전진 타임아웃", requestNext: false);
                return;
            }
            
            if (form.CheckTmCylinderExtended())
            {
                // 실린더 전진 완료 -> 하드웨어 동작 플래그 리셋
                form.TmHardwareActionPending = false;
                
                // 실린더 전진 완료 -> 서보 이동
                if (form.StartTmServoDown(form.CurrentTransfer.Dropoff))
                {
                    BeginTmPhase(TransferController.TmPhase.DropoffExtend_ServoDown, 0, form.CurrentTransfer.Dropoff, true);
                }
                else
                {
                    // 하강(드롭용) 서보 실패 시 논리 드롭으로 폴백하지 않음
                    AbortTransferAndScheduleRetry("드롭오프 하강 서보 이동 실패");
                }
            }
            else
            {
                // 실린더 전진 완료 대기 (타이머로 재시도)
                BeginTmPhase(TransferController.TmPhase.DropoffExtend_CylinderForward, 1, form.CurrentTransfer.Dropoff, true);
            }
        }

        /// <summary>
        /// ProcessTm: DropoffExtend_ServoDown Phase 처리
        /// </summary>
        private void ProcessTmPhase_DropoffExtend_ServoDown()
        {
            // 서보 하강 완료 -> 진공 OFF + 배기
            if (form.IsTmHardwareModeAvailable())
            {
                if (!form.SetTmVacuumOffAndExhaust())
                {
                    // 진공 OFF 실패 시 자동 일시정지 (HandleHardwareError에서 처리됨)
                    return;
                }
            }
            if (!form.PerformDropoff())
            {
                AbortTransferAndScheduleRetry("PerformDropoff 실패 (도어/상태)");
                return;
            }
            BeginTmPhase(TransferController.TmPhase.DropoffExtend_VacuumOffExhaust, 2, form.CurrentTransfer.Dropoff, true); // 배기 대기
        }

        /// <summary>
        /// ProcessTm: DropoffExtend_VacuumOffExhaust Phase 처리
        /// </summary>
        private void ProcessTmPhase_DropoffExtend_VacuumOffExhaust()
        {
            // 배기 완료 -> 하강 위치로 하강 (웨이퍼 아래로 이동)
            // 중요: 안착 위치에서 바로 실린더 후진하면 웨이퍼를 건드릴 수 있음!
            //       반드시 하강 위치(DescendY)로 먼저 하강한 후 실린더 후진해야 함
            form.CompleteTmExhaust();
            if (form.StartTmServoToDescend(form.CurrentTransfer.Dropoff))
            {
                BeginTmPhase(TransferController.TmPhase.DropoffRetract_ServoUp, 0, form.CurrentTransfer.Dropoff, false);
            }
            else
            {
                AbortTransferAndScheduleRetry("드롭 후 하강(Descend) 이동 실패 — 실린더 후진 보류");
            }
        }

        /// <summary>
        /// ProcessTm: DropoffRetract Phase 처리 (시뮬레이션 모드)
        /// </summary>
        private void ProcessTmPhase_DropoffRetract()
        {
            form.ProcessTmDropoffRetractComplete();
        }

        /// <summary>
        /// ProcessTm: DropoffRetract_ServoUp Phase 처리
        /// </summary>
        private void ProcessTmPhase_DropoffRetract_ServoUp()
        {
            // 하강 위치 도달 완료 -> 실린더 후진
            // (웨이퍼를 안착시킨 후 블레이드가 웨이퍼 아래로 내려감)
            if (form.StartTmCylinderRetract())
            {
                BeginTmPhase(TransferController.TmPhase.DropoffRetract_CylinderBackward, 0, form.CurrentTransfer.Dropoff, false);
            }
            else
            {
                AbortTransferAndScheduleRetry("드롭오프 후 실린더 후진 실패");
            }
        }

        /// <summary>
        /// ProcessTm: DropoffRetract_CylinderBackward Phase 처리
        /// </summary>
        private void ProcessTmPhase_DropoffRetract_CylinderBackward()
        {
            // 실린더 후진 완료 확인 -> 도어 닫기 또는 완료
            
            // 실린더 후진 타임아웃 체크
            if (form.TmHardwareActionPending && (DateTime.Now - form.tmHardwareActionStartTime).TotalMilliseconds > AppSettings.CylinderActionTimeoutMs)
            {
                form.HandleHardwareError($"TM 실린더 후진 타임아웃 (Phase: {form.TmPhase}, 타임아웃: {AppSettings.CylinderActionTimeoutMs}ms)");
                AbortTransferAndScheduleRetry("드롭오프 후 실린더 후진 타임아웃", requestNext: false);
                return;
            }
            
            if (form.CheckTmCylinderRetracted())
            {
                // 실린더 후진 완료 -> 하드웨어 동작 플래그 리셋
                form.TmHardwareActionPending = false;
                form.ProcessTmDropoffRetractComplete();
            }
            else
            {
                // 실린더 후진 완료 대기 (타이머로 재시도)
                BeginTmPhase(TransferController.TmPhase.DropoffRetract_CylinderBackward, 1, form.CurrentTransfer.Dropoff, false);
            }
        }

        /// <summary>
        /// ProcessTm: WaitDoorDropoffClose Phase 처리
        /// </summary>
        private void ProcessTmPhase_WaitDoorDropoffClose()
        {
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드: 도어 닫기 명령 전송 후 시간 기반 대기
                // 중요: 도어에는 별도의 닫힘 센서가 없음 (EtherTest 참고)
                // 도어 닫기 명령 전송 후 약 2초 대기 후 완료 처리
                
                if (!form.doorCloseCommandSent)
                {
                    form.EnsureDoorClosedForRegion(form.CurrentTransfer.Dropoff);
                    form.doorCloseCommandSent = true;
                    form.doorCloseWaitTicks = 0;
                    form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Dropoff)} 도어 닫기 명령 전송", "INFO");
                }
                
                if (!form.TmHardwareActionPending)
                {
                    form.StartTmHardwareAction();
                }
                
                form.doorCloseWaitTicks++;
                
                // 도어 닫힘 대기 시간: 약 0.75초 (5틱 * 150ms)
                // 센서가 없으므로 시간 기반으로 처리
                if (form.doorCloseWaitTicks < AppSettings.DoorCloseWaitTicks)
                {
                    // 아직 대기 중
                    return;
                }
                
                // 대기 시간 완료 - 도어가 닫힌 것으로 간주
                form.doorCloseCommandSent = false;
                form.doorCloseWaitTicks = 0;
                form.TmHardwareActionPending = false;
                form.TmSettleWaiting = false;
                form.AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(form.CurrentTransfer.Dropoff)} 도어 닫힘 완료 (시간 기반)", "INFO");
            }
            
            // 도어가 닫혔으므로 드롭오프 완료 처리 및 Chamber 공정 시작
            // 시나리오: T=63~65초 도어 닫기 → 공정 시작
            // 중요: 실제 장비가 돌아가는 로직이므로 정확한 상태 관리 필수
            // 중복 호출 방지: 한 번만 실행되도록 Phase 변경 전에 처리
            if (form.CurrentTransfer != null && form.CurrentTransfer.DestinationChamber != null)
            {
                var destination = form.CurrentTransfer.DestinationChamber;
                // 도어가 닫힌 후에만 공정 시작 (PerformDropoff()에서 웨이퍼는 이미 배치됨)
                // 중복 호출 방지: StatusText가 "Door Closing"이고 아직 공정이 시작되지 않았을 때만
                if (destination.CurrentWafer != null && destination.RemainingSeconds > 0
                    && ChamberStatusTexts.IsAwaitingProcessStart(destination.StatusText))
                {
                    // 중요: 도어 상태를 명시적으로 닫힘으로 설정 (ViewModel 동기화)
                    // EnsureDoorClosedForRegion()이 호출되었지만 ViewModel 상태가 업데이트되지 않았을 수 있음
                    if (EquipmentRegionHelper.RequiresDoor(destination.Region))
                    {
                        form.ViewModel?.SetDoorState(destination.Region, false); // 도어 닫힘 상태로 설정
                    }
                    
                    // 공정 시작 (도어가 닫힌 후) - 일관된 상태 업데이트
                    // StartChamberProcessing() 내부에서 중복 호출 방지 로직 있음
                    form.StartChamberProcessing(destination, destination.CurrentWafer);
                    
                    // Phase를 즉시 변경하여 중복 호출 방지
                    form.TmCurrentPosition = form.CurrentTransfer.Dropoff;
                    form.FinishCurrentTransfer();
                    return; // 즉시 종료하여 중복 실행 방지
                }
            }
            
            // 위 조건에 맞지 않으면 정상적으로 완료 처리
            form.TmCurrentPosition = form.CurrentTransfer.Dropoff;
            form.FinishCurrentTransfer();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 다음 틱에서 StartNextTransfer를 실행하도록 예약 (재귀 호출 금지).
        /// </summary>
        private void RequestStartNextTransfer()
        {
            _startNextTransferRequested = true;
        }

        /// <summary>
        /// 하드웨어/논리 실패 시 현재 이송을 중단하고 재시도 큐에 넣는다.
        /// 성공처럼 다음 Phase로 진행하지 않는다.
        /// </summary>
        private void AbortTransferAndScheduleRetry(string reason, bool requestNext = true)
        {
            var failedTask = form.CurrentTransfer;
            form.TmHardwareActionPending = false;
            form.TmSettleWaiting = false;
            form.doorOpenCommandSent = false;
            form.doorCloseCommandSent = false;
            form.doorOpenConsecutiveChecks = 0;
            form.doorCloseWaitTicks = 0;

            // 하드웨어 모드: 진공 OFF + 실린더 후진 시도 (다음 이동 전 안전 자세)
            form.TrySafeStopTmHardwareAfterAbort();

            form.TransferService?.ResetToIdle();

            form.AddLogMessage($"TM 이송 중단: {reason}", "ERROR");

            if (failedTask == null)
            {
                if (requestNext && form.TransferService?.QueueCount > 0)
                {
                    RequestStartNextTransfer();
                }
                return;
            }

            bool retryScheduled = form.HandleFailedTransfer(failedTask, reason);
            if (requestNext && (retryScheduled || (form.TransferService?.QueueCount > 0)))
            {
                RequestStartNextTransfer();
            }
        }

        /// <summary>Form1에서 하드웨어 실패 Abort를 직접 호출할 때 사용</summary>
        internal void AbortCurrentHardwareTransfer(string reason)
        {
            AbortTransferAndScheduleRetry(reason);
        }

        /// <summary>
        /// 공정 미완료로 아직 픽업할 수 없을 때 — 재시도 카운트 없이 큐에 되돌림.
        /// </summary>
        private void DeferTransferUntilProcessReady(TransferController.TransferTask task, string infoMessage)
        {
            form.AddLogMessage(infoMessage, "INFO");
            form.TmHardwareActionPending = false;
            form.TmSettleWaiting = false;
            form.TransferService?.ResetToIdle();
            if (task != null)
            {
                form.TransferService?.EnqueueTransfer(task);
            }
            // 다음 ProcessTm Idle에서 자연스럽게 재시도 (Request 없이 두어 같은 틱 재진입 방지)
        }

        /// <summary>
        /// 다음 Transfer 작업 시작
        /// </summary>
        public void StartNextTransfer()
        {
            // 알람 홀드 중에는 새 이송을 시작하지 않습니다. 큐는 그대로 두고, 시작을 누른 뒤 이어서 꺼냅니다.
            if (form.ProcessHeldByAlarm)
            {
                return;
            }

            if (form.TransferService?.QueueCount == 0)
            {
                form.TransferService?.ResetToIdle();
                return;
            }

            // 하드웨어 모드: 원점복귀 체크는 공정 시작 시 PerformAutoServoOnAndHoming()에서만 수행
            // 공정 중간에는 서보가 이동하면서 HOME_D가 false가 될 수 있으므로,
            // StartNextTransfer()에서는 원점복귀 체크를 하지 않음
            // 공정 시작 시 PerformAutoServoOnAndHoming()에서 이미 원점복귀를 완료하고
            // TmHardwareInitialized = true로 설정하므로, 여기서는 체크할 필요 없음

            // 우선순위 선택: 챔버 A 반입(드롭오프) 작업을 최우선으로 선택
            // 조건: 큐에 A로 향하는 작업이 있으면 무조건 우선 선택 (ReservedForIncoming 포함)
            TransferController.TransferTask selected = null;
            foreach (var t in form.TransferService.GetQueuedTasks())
            {
                if (t.DestinationChamber == form.ChamberAState)
                {
                    selected = t;
                    break;
                }
            }
            if (selected != null)
            {
                // 선택된 항목을 큐에서 제거하고 다시 추가 (우선순위 처리)
                var tempQueue = new System.Collections.Generic.Queue<TransferController.TransferTask>();
                foreach (var t in form.TransferService.GetQueuedTasks())
                {
                    if (!ReferenceEquals(t, selected))
                    {
                        tempQueue.Enqueue(t);
                    }
                }
                form.TransferService.ClearQueue();
                form.TransferService.EnqueueTransfer(selected); // 선택된 항목을 먼저 추가
                foreach (var t in tempQueue)
                {
                    form.TransferService.EnqueueTransfer(t);
                }
                // TransferController에 선택된 작업을 시작하도록 설정
                form.TransferService.StartNextTransfer();
            }
            else
            {
                // 일반 순서로 시작
                form.TransferService?.StartNextTransfer();
            }
            
            // CurrentTransfer는 이제 속성으로 TransferController에서 읽어옴
            var currentTask = form.CurrentTransfer;
            if (currentTask == null)
            {
                return;
            }
            
            // 중요: Transfer 시작 전에 공정 완료 상태 재확인
            // 스케줄링 후 상태가 변경되었을 수 있음 (예: 공정이 아직 완료되지 않음)
            // 주의: 이미 스케줄된 Transfer는 PickupScheduled=true이므로 IsReadyForTransfer()가 false를 반환함
            // 따라서 RemainingSeconds만 체크해야 함
            if (currentTask.SourceChamber != null)
            {
                var source = currentTask.SourceChamber;
                if (source.CurrentWafer != null && source.RemainingSeconds > 0)
                {
                    DeferTransferUntilProcessReady(currentTask,
                        $"[대기] Transfer 보류: {EquipmentRegionHelper.FormatUnitLabel(source.UnitKey)} 웨이퍼 #{currentTask.Wafer?.Id} 공정 진행 중 " +
                        $"(RemainingSeconds={source.RemainingSeconds})");
                    return;
                }
            }
            
            // 하드웨어 모드: 실제 이동 명령 전송
            if (form.IsTmHardwareModeAvailable())
            {
                // 하드웨어 모드에서는 실제 이동 명령을 보내고 대기 Phase로 설정
                if (form.StartTmServoMoveForPickup(currentTask.Pickup))
                {
                    BeginTmPhase(TransferController.TmPhase.MoveToPickup_WaitHardware, 0, currentTask.Pickup, false);
                }
                else
                {
                    AbortTransferAndScheduleRetry("픽업 서보 이동 시작 실패");
                }
            }
            else
            {
                // 시뮬레이션 모드: 틱 기반 Phase 설정
                var moveTicks = form.GetTmMoveDurationTicks(form.TmCurrentPosition, currentTask.Pickup);
                BeginTmPhase(TransferController.TmPhase.MoveToPickup, moveTicks, currentTask.Pickup, false);
            }
        }

        /// <summary>
        /// TM Phase 시작
        /// </summary>
        internal void BeginTmPhase(TransferController.TmPhase phase, int durationTicks, EquipmentRegion target, bool carrying)
        {
            // Phase 변경 시 도어 확인 카운터 리셋 (새로운 Phase에서 새로 시작)
            if (form.TmPhase != phase)
            {
                form.doorOpenConsecutiveChecks = 0;
            }
            
            // TransferController에 Phase 설정 (TmPhase와 TmPhaseTicksRemaining은 속성으로 자동 반영됨)
            form.TransferService?.BeginPhase(phase, durationTicks, target, false);
            form.TmCarryingVisual = carrying;
            form.TmVisualTarget = target;
            
            // Phase 기반 extensionFactor 설정
            // 하드웨어 모드에서 실린더 전진/후진 Phase일 때는 타이머에서 실제 상태를 확인하여 업데이트
            form.TmBladeExtensionFactor = GetBladeExtensionForPhase(phase);
            
            // UI 업데이트는 SimulationTimer_Tick에서만 수행하여 중복 업데이트 방지
            // (하드웨어 모드에서 실린더 상태는 타이머에서 실제 센서 값을 읽어서 업데이트)
        }

        /// <summary>
        /// Phase에 따른 블레이드 확장 팩터 반환
        /// </summary>
        private float GetBladeExtensionForPhase(TransferController.TmPhase phase)
        {
            switch (phase)
            {
                // 이동 중
                case TransferController.TmPhase.MoveToPickup:
                case TransferController.TmPhase.MoveToPickup_WaitHardware:
                case TransferController.TmPhase.MoveToDropoff:
                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                    return 0.7f;

                // 도어 대기
                case TransferController.TmPhase.WaitDoorPickupOpen:
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                    return 0.7f;

                // 픽업 확장 동작
                case TransferController.TmPhase.PickupExtend:
                case TransferController.TmPhase.PickupExtend_CylinderForward:
                case TransferController.TmPhase.PickupExtend_ServoDown:
                case TransferController.TmPhase.PickupExtend_VacuumOn:
                    return 1.3f;

                // 픽업 후퇴 동작
                case TransferController.TmPhase.PickupRetract:
                case TransferController.TmPhase.PickupRetract_ServoUp:
                case TransferController.TmPhase.PickupRetract_CylinderBackward:
                case TransferController.TmPhase.WaitDoorPickupClose:
                    return 0.65f;

                // 드롭오프 확장 동작
                case TransferController.TmPhase.DropoffExtend:
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                case TransferController.TmPhase.DropoffExtend_ServoDown:
                case TransferController.TmPhase.DropoffExtend_VacuumOffExhaust:
                    return 1.3f;

                // 드롭오프 후퇴 동작
                case TransferController.TmPhase.DropoffRetract:
                case TransferController.TmPhase.DropoffRetract_ServoUp:
                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    return 0.65f;

                default:
                    return 0.55f;
            }
        }

        #endregion
    }
}

