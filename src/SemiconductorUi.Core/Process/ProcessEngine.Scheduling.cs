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
        internal void AdvanceSimulation()
        {
            SimulationService?.IncrementElapsedTime();
            
            // 하드웨어 모드에서 TM 동작 중인 경우
            bool tmBusyInHardwareMode = IsTmHardwareModeAvailable() && TmHardwareActionPending;
            
            // Chamber 시간은 항상 감소 (하드웨어 모드에서도 공정 시간은 흘러야 함)
            DecrementChamberTime(ChamberAState);
            DecrementChamberTime(ChamberBState);
            DecrementChamberTime(ChamberCState);

            // 하드웨어 모드에서 TM 동작 중이면 새로운 스케줄링 스킵
            // TM이 실제 하드웨어 동작을 완료할 때까지 대기
            if (!tmBusyInHardwareMode)
            {
                // 스케줄링 우선순위 (효율성 최적화):
                // 1. Chamber A에서 B/C로 이송 - 빈 Chamber로 웨이퍼 이동 (조건부 최우선)
                //    - A → B/C가 가능할 때만 우선 처리 (B 또는 C가 비어있어야 함)
                //    - A → B/C가 불가능하면 (B와 C 모두 사용 중) → C → FOUP B 우선 처리
                // 2. 완료 웨이퍼를 빼기 (B/C → FOUP B) - Chamber를 비워서 다음 웨이퍼 수용
                // 3. FOUP A에서 Chamber A로 투입 - 새 웨이퍼 로딩
                // 
                // 이유: A → B/C 경로(75초)가 C → FOUP B 경로(45초)보다 전체 프로세스가 더 길기 때문에
                //      A를 먼저 비워서 다음 웨이퍼 투입을 가능하게 하는 것이 전체 처리량 향상에 유리
                //      단, B와 C가 모두 사용 중이면 A → B/C가 불가능하므로 C → FOUP B를 먼저 처리
                // 병렬 가공 모드: Chamber A는 B 또는 C로만 이동 (FOUP B로 직접 이동 불가)
                // 스케줄링 우선순위 (총 가공시간 최소화):
                // 1. A → B/C 스케줄링 (A 비우기)
                // 2. FOUP A → A (새 웨이퍼 투입) ← A를 비웠다면 즉시 새 웨이퍼 투입하여 가공 시작
                // 3. B/C → FOUP B (완료된 웨이퍼 제거) ← 완료된 웨이퍼 제거는 새 웨이퍼 투입보다 우선순위 낮음
                //
                // 이유: A를 비웠다면 즉시 새 웨이퍼를 투입하면 A에서 가공이 시작되어 전체 파이프라인이 더 빨리 돌아감
                //      완료된 웨이퍼 제거는 새 웨이퍼 투입보다 우선순위가 낮음
                
                // 1. A → B/C 스케줄링 시도
                bool aToBCScheduled = TryScheduleTransferFromChamberA();
                
                // 2. A를 비웠다면 즉시 새 웨이퍼 투입 (총 가공시간 최소화)
                // A가 비어있고 FOUP A에 웨이퍼가 있으면 즉시 투입
                TryScheduleLoadFromFoupA();
                
                // 3. 완료된 웨이퍼를 FOUP B로 이송 (새 웨이퍼 투입 후 처리)
                // A → B가 스케줄링되었는지 전달하여 우선순위 가드에 사용
                TryScheduleTransferToFoupB(ChamberBState, aToBCScheduled);
                TryScheduleTransferToFoupB(ChamberCState, aToBCScheduled);
            }
            
            // TM 처리 (하드웨어 모드에서는 실제 동작 완료 대기)
            ProcessTm();

            // 알람 홀드 중에는 공정을 완료로 넘기지 않습니다 (알람이 조용히 사라지지 않도록, 리셋 후 시작하면 완료 처리).
            if (!ProcessHeldByAlarm
                && (foupManager?.GetFoupBCount() ?? 0) - FoupBCompletedBaseline >= GetActiveTarget()
                && ChamberAState.CurrentWafer == null
                && ChamberBState.CurrentWafer == null
                && ChamberCState.CurrentWafer == null
                && (TransferService?.QueueCount ?? 0) == 0
                && CurrentTransfer == null)
            {
                // 공정 종료 전 원점복귀 수행 (하드웨어 모드)
                if (IsTmHardwareModeAvailable())
                {
                    AddLogMessage("공정 종료 - TM 원점복귀 수행", "INFO");
                    PerformShutdownHoming();
                }
                
                SimulationController?.Stop();
                SimulationController?.StopTimer();
                SetProcessState(ProcessState.Idle, "시뮬레이션이 완료되었습니다.");
                UpdateSimulationUi();
            }

            UpdateTmAnimationIdleTarget();
        }

        /// <summary>
        /// Chamber 공정 시간 감소 처리
        /// BeforeFinal 로직으로 복원: 도어 상태만 확인, TM 작업과 무관하게 공정 시간 감소
        /// </summary>
        internal void DecrementChamberTime(ChamberController.ChamberState chamber)
        {
            if (chamber?.CurrentWafer == null)
            {
                return;
            }

            // 알람 홀드 중에는 공정 시간이 흐르지 않습니다 (재개할 때 멈춘 시간이 한꺼번에 빠지지 않도록 기준 시각도 지움).
            if (ProcessHeldByAlarm)
            {
                chamber.LastProcessedTime = DateTime.MinValue;
                return;
            }

            // 도어가 열려있거나 닫는 중이면 마지막 처리 시간 초기화 (다시 시작할 때 누적 방지)
            if (!IsChamberDoorClosed(chamber) || ChamberStatusTexts.IsAwaitingProcessStart(chamber.StatusText))
            {
                chamber.LastProcessedTime = DateTime.MinValue;
                return;
            }

            if (chamber.RemainingSeconds <= 0)
            {
                chamber.RemainingSeconds = 0;
                chamber.ProcessingAccumulator = 0;
                chamber.LastProcessedTime = DateTime.MinValue;
                return;
            }

            // 하드웨어 모드: 실제 경과 시간 기반으로 공정 시간 감소 (Thread.Sleep에 영향받지 않음)
            // 시뮬레이션 모드: 틱 기반 (기존 방식)
            if (IsTmHardwareModeAvailable())
            {
                // 실시간 기반: DateTime을 사용하여 정확한 경과 시간 측정
                DateTime now = DateTime.Now;
                
                if (chamber.LastProcessedTime == DateTime.MinValue)
                {
                    // 첫 호출: 시작 시간 기록
                    chamber.LastProcessedTime = now;
                    return;
                }
                
                // 마지막 처리 이후 경과 시간 (초)
                double elapsedSeconds = (now - chamber.LastProcessedTime).TotalSeconds;
                chamber.ProcessingAccumulator += elapsedSeconds;
                chamber.LastProcessedTime = now;
                
                // 1초 이상 누적되면 RemainingSeconds 감소
                while (chamber.ProcessingAccumulator >= 1.0 && chamber.RemainingSeconds > 0)
                {
                    chamber.RemainingSeconds--;
                    chamber.ProcessingAccumulator -= 1.0;
                    if (chamber.RemainingSeconds == 0)
                    {
                        chamber.StatusText = "Transfer Ready";
                        chamber.ProcessingAccumulator = 0;
                        chamber.LastProcessedTime = DateTime.MinValue;
                        AddLogMessage($"{EquipmentRegionHelper.FormatUnitLabel(chamber.UnitKey)} 공정 완료 - TM 대기 중 (도어 닫힘 유지) (웨이퍼#{chamber.CurrentWafer?.Id})", "INFO");
                        break;
                    }
                }
            }
            else
            {
                // 시뮬레이션 모드: 틱 기반 (기존 방식)
                double secondsPerTick = AppSettings.ChamberSecondsPerTick;  // 1.0초
                chamber.ProcessingAccumulator += secondsPerTick;
                while (chamber.ProcessingAccumulator >= 1.0 && chamber.RemainingSeconds > 0)
                {
                    chamber.RemainingSeconds--;
                    chamber.ProcessingAccumulator -= 1.0;
                    if (chamber.RemainingSeconds == 0)
                    {
                        chamber.StatusText = "Transfer Ready";
                        chamber.ProcessingAccumulator = 0;
                        AddLogMessage($"{EquipmentRegionHelper.FormatUnitLabel(chamber.UnitKey)} 공정 완료 - TM 대기 중 (도어 닫힘 유지) (웨이퍼#{chamber.CurrentWafer?.Id})", "INFO");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Chamber A에서 B/C로 이송 스케줄링 시도
        /// </summary>
        /// <returns>스케줄링 성공 여부 (B 또는 C로 이동 가능했는지)</returns>
        internal bool TryScheduleTransferFromChamberA()
        {
            if (!IsReadyForTransfer(ChamberAState))
            {
                return false;
            }

            ChamberController.ChamberState target = null;
            
            // 큐에 이미 예약된 작업이 있는지 확인
            bool bHasReservedTask = IsChamberReservedInQueue(ChamberBState);
            bool cHasReservedTask = IsChamberReservedInQueue(ChamberCState);
            
            // 디버깅: Chamber 상태 확인
            bool bIsAvailableRaw = IsChamberAvailable(ChamberBState);
            bool cIsAvailableRaw = IsChamberAvailable(ChamberCState);
            
            bool bAvailable = bIsAvailableRaw && !bHasReservedTask;
            bool cAvailable = cIsAvailableRaw && !cHasReservedTask;
            
            // 디버깅 로그 추가
            AddLogMessage($"[병렬가공] Chamber A → B/C 스케줄링 체크: " +
                $"B(CurrentWafer={ChamberBState?.CurrentWafer?.Id}, Reserved={ChamberBState?.ReservedForIncoming}, " +
                $"AvailableRaw={bIsAvailableRaw}, HasReservedTask={bHasReservedTask}, Available={bAvailable}), " +
                $"C(CurrentWafer={ChamberCState?.CurrentWafer?.Id}, Reserved={ChamberCState?.ReservedForIncoming}, " +
                $"AvailableRaw={cIsAvailableRaw}, HasReservedTask={cHasReservedTask}, Available={cAvailable})", "INFO");
            
            // 중요: Chamber B가 곧 비게 될 것인지 확인 (B 우선순위 보장)
            // 웨이퍼가 있지만 픽업이 예약되어 있으면 → TM이 꺼내고 있으므로 곧 비게 됨
            // BeforeFinal 로직으로 복원: IsChamberReservedInQueue 체크 제거
            bool bWillBeAvailableSoon = !bAvailable 
                && ChamberBState != null
                && ChamberBState.CurrentWafer != null 
                && ChamberBState.PickupScheduled
                && !ChamberBState.ReservedForIncoming;
            
            bool cWillBeAvailableSoon = !cAvailable 
                && ChamberCState != null
                && ChamberCState.CurrentWafer != null 
                && ChamberCState.PickupScheduled
                && !ChamberCState.ReservedForIncoming;
            
            if (SecondExposureEnabled)
            {
                // ========== 2차 노광 모드 (순차 처리) ==========
                // 반드시 A → B → C 순서로 가야 함
                // A에서 완료된 웨이퍼는 B로만 갈 수 있음 (C는 B 거쳐서만 갈 수 있음)
                if (bAvailable || bWillBeAvailableSoon)
                {
                    target = ChamberBState;
                }
                // B가 사용 불가면 대기 (C로 직접 가면 안 됨!)
            }
            else
            {
                // ========== 병렬 처리 모드 ==========
                // 병렬 가공 규칙:
                // - Chamber A → Chamber B 또는 C로 이송
                // - Chamber B와 C는 동일한 가공을 수행
                // - 비어있는 chamber를 우선으로 선택
                //   1. B가 비어있으면 B로 (우선순위)
                //   2. C가 비어있으면 C로
                //   3. 둘 다 비어있으면 B로
                // - Chamber A를 먼저 비워서 새 웨이퍼 투입을 우선하여 총 가공시간을 줄임
                
                // 규칙 1: B가 비어있고 예약되지 않았으면 B로
                if (bAvailable)
                {
                    target = ChamberBState;
                    AddLogMessage("[병렬가공] Chamber B가 비어있음 → B로 이동", "INFO");
                }
                // 규칙 2: C가 비어있고 예약되지 않았으면 C로
                else if (cAvailable)
                {
                    target = ChamberCState;
                    AddLogMessage("[병렬가공] Chamber B는 사용 중, C가 비어있음 → C로 이동", "INFO");
                }
                // 규칙 3: 둘 다 비어있지만 예약된 경우 처리
                else if (bIsAvailableRaw && cIsAvailableRaw)
                {
                    // 둘 다 비어있지만 예약된 경우: 예약되지 않은 쪽 선택, 둘 다 예약되면 B 우선
                    if (!bHasReservedTask)
                    {
                        target = ChamberBState;
                        AddLogMessage("[병렬가공] Chamber B와 C 모두 비어있지만 B가 예약 안 됨 → B로 이동", "INFO");
                    }
                    else if (!cHasReservedTask)
                    {
                        target = ChamberCState;
                        AddLogMessage("[병렬가공] Chamber B와 C 모두 비어있지만 C가 예약 안 됨 → C로 이동", "INFO");
                    }
                    else
                    {
                        // 둘 다 예약되어 있으면 대기 (다음 틱에서 재시도)
                        AddLogMessage("[병렬가공] Chamber B와 C 모두 비어있지만 둘 다 예약됨 → 대기", "INFO");
                    }
                }
                // 규칙 4: 둘 다 공정 중이면 곧 비게 될 챔버 확인
                else
                {
                    if (bWillBeAvailableSoon && !cWillBeAvailableSoon)
                    {
                        target = ChamberBState;
                        AddLogMessage("[병렬가공] Chamber B가 곧 비게 됨 → B로 이동", "INFO");
                    }
                    else if (cWillBeAvailableSoon && !bWillBeAvailableSoon)
                    {
                        target = ChamberCState;
                        AddLogMessage("[병렬가공] Chamber C가 곧 비게 됨 → C로 이동", "INFO");
                    }
                    else if (bWillBeAvailableSoon && cWillBeAvailableSoon)
                    {
                        // 둘 다 곧 비게 되면 B 우선
                        target = ChamberBState;
                        AddLogMessage("[병렬가공] 둘 다 곧 비게 됨 → B로 이동 (우선순위)", "INFO");
                    }
                    else
                    {
                        // 둘 다 공정 중이고 곧 비게 되지 않음
                        AddLogMessage("[병렬가공] Chamber B와 C 모두 공정 중 → 대기", "INFO");
                    }
                }
            }

            if (target == null)
            {
                ChamberAState.StatusText = "Transfer Waiting";
                return false; // 스케줄링 실패 (B와 C 모두 사용 불가)
            }

            ScheduleChamberTransfer(ChamberAState, target);
            return true; // 스케줄링 성공
        }

        private bool IsChamberReservedInQueue(ChamberController.ChamberState chamber)
        {
            if (chamber == null)
            {
                return false;
            }
            
            // TransferController의 메서드 사용
            return TransferService?.IsChamberReservedInQueue(chamber) ?? false;
        }

        internal void TryScheduleTransferToFoupB(ChamberController.ChamberState source, bool aToBCScheduled = false)
        {
            if (!IsReadyForTransfer(source))
            {
                return;
            }

            // 우선순위 가드 1: Chamber A가 비어있으면 Chamber A 공정을 최우선으로 수행
            // Chamber A가 비어있고 FOUP A에 웨이퍼가 있으면 B/C에서 FOUP B로 이동하는 것을 보류
            // A로 향하는 예약이 이미 있지 않은 경우에만 B/C→FOUP B 예약을 잠시 보류
            if (IsChamberAvailable(ChamberAState)
                && foupManager?.HasWafersInFoupA() == true
                && !IsWaferEnRouteToChamber(ChamberAState))
            {
                source.StatusText = "A 투입 우선 대기";
                return;
            }

            // 우선순위 가드 2: 병렬 공정 모드에서만 적용
            // Chamber A가 완료되어 B/C로 갈 수 있으면 B/C → FOUP B를 보류
            // 총 시간을 고려하면 A → B/C를 먼저 처리하는 것이 전체 처리량 향상에 유리
            // 2차 노광 모드(직렬 공정)에서는 적용하지 않음 (A → B → C 순서가 고정되어 있음)
            if (!SecondExposureEnabled && IsReadyForTransfer(ChamberAState))
            {
                // B에서 FOUP B로 가는 경우: A → C가 가능하면 보류
                if (source == ChamberBState
                    && IsChamberAvailable(ChamberCState))
                {
                    // A → C가 이미 스케줄링되었거나 큐에 있으면 B → FOUP B를 보류
                    if (aToBCScheduled || IsChamberReservedInQueue(ChamberCState))
                    {
                        // A → C가 이미 큐에 있음: B → FOUP B를 보류하여 A → C를 우선 처리
                        source.StatusText = "A → C 우선 대기";
                        return;
                    }
                    else
                    {
                        // A → C가 가능하지만 아직 스케줄링되지 않음: B → FOUP B를 보류
                        source.StatusText = "A → C 우선 대기";
                        return;
                    }
                }
                
                // C에서 FOUP B로 가는 경우: A → B가 가능하면 보류
                if (source == ChamberCState
                    && IsChamberAvailable(ChamberBState))
                {
                    // A → B가 이미 스케줄링되었거나 큐에 있으면 C → FOUP B를 보류
                    if (aToBCScheduled || IsChamberReservedInQueue(ChamberBState))
                    {
                        // A → B가 이미 큐에 있음: C → FOUP B를 보류하여 A → B를 우선 처리
                        source.StatusText = "A → B 우선 대기";
                        return;
                    }
                    else
                    {
                        // A → B가 가능하지만 아직 스케줄링되지 않음: C → FOUP B를 보류
                        source.StatusText = "A → B 우선 대기";
                        return;
                    }
                }
            }

            // 2차 노광 모드: Chamber B → Chamber C 이동 처리
            if (source == ChamberBState && source.CurrentWafer != null)
            {
                bool needs2ndExp = NeedsSecondExposure(source.CurrentWafer);
                
                if (SecondExposureEnabled && needs2ndExp)
                {
                    // Chamber C가 비어있거나 곧 비게 될 예정인지 확인
                    bool cAvailableForSecondExposure = IsChamberAvailable(ChamberCState);
                    bool cWillBeAvailableSoonForSecondExposure = !cAvailableForSecondExposure 
                        && ChamberCState != null
                        && ChamberCState.CurrentWafer != null 
                        && ChamberCState.PickupScheduled
                        && !ChamberCState.ReservedForIncoming;
                    
                    if (!cAvailableForSecondExposure && !cWillBeAvailableSoonForSecondExposure)
                    {
                        source.StatusText = "2차 노광 대기";
                        return;
                    }

                    source.StatusText = "2차 노광 이동 준비";
                    AddLogMessage($"[2차노광] 웨이퍼#{source.CurrentWafer.Id}: Chamber B → Chamber C 이동 예약", "INFO");
                    ScheduleChamberTransfer(source, ChamberCState);
                    return;
                }
            }
            if (SecondExposureEnabled && source == ChamberCState && source.CurrentWafer != null)
            {
                MarkSecondExposureComplete(source.CurrentWafer);
            }

            var task = new TransferController.TransferTask
            {
                Wafer = source.CurrentWafer,
                Pickup = source.Region,
                Dropoff = EquipmentRegion.FoupB,
                SourceChamber = source,
                DestinationChamber = null,
                OnCompleted = wafer =>
                {
                    wafer.CurrentStage = "FOUP B";
                    // 1층부터 적재 (리스트 끝에 추가하여 순서대로 쌓임)
                    // 1번 웨이퍼가 먼저 Add되면 인덱스 0 (1층)
                    // 2번 웨이퍼가 Add되면 인덱스 1 (2층)
                    // ...
                    foupManager?.AddCompletedWaferToFoupB(wafer);
                }
            };

            source.StatusText = "Transfer 대기";
            source.PickupScheduled = true;
            EnqueueTransfer(task);
            AddLogMessage($"TM 예약: {EquipmentRegionHelper.FormatRegionLabel(task.Pickup)} -> {EquipmentRegionHelper.FormatRegionLabel(task.Dropoff)} (웨이퍼 #{task.Wafer.Id})", "INFO");
        }

        internal void TryScheduleLoadFromFoupA()
        {
            if (foupManager?.HasWafersInFoupA() != true || !IsChamberAvailable(ChamberAState))
            {
                return;
            }

            var wafer = foupManager.PeekWaferFromFoupA();
            if (wafer == null)
            {
                return;
            }
            wafer.CurrentStage = "TM Load";

            var task = new TransferController.TransferTask
            {
                Wafer = wafer,
                Pickup = EquipmentRegion.FoupA,
                Dropoff = EquipmentRegion.ChamberA,
                DestinationChamber = ChamberAState,
                FromFoup = true
            };

            ChamberAState.ReservedForIncoming = true;
            EnqueueTransfer(task);
            AddLogMessage($"TM 예약: {EquipmentRegionHelper.FormatRegionLabel(task.Pickup)} -> {EquipmentRegionHelper.FormatRegionLabel(task.Dropoff)} (웨이퍼 #{wafer.Id})", "INFO");
        }

        /// <summary>
        /// Chamber가 이송 준비 상태인지 확인 (공정 완료 판단)
        /// 중요: 실제 장비가 돌아가는 로직이므로 정확한 조건 체크 필수
        /// </summary>
        private bool IsReadyForTransfer(ChamberController.ChamberState chamber)
        {
            if (chamber == null || chamber.CurrentWafer == null || chamber.RemainingSeconds > 0 || chamber.PickupScheduled)
            {
                return false;
            }

            // 도어가 필요한 챔버는 닫힌 뒤에만 반출 예약 (Door Closing 중 스케줄 방지)
            if (EquipmentRegionHelper.RequiresDoor(chamber.Region) && IsRegionDoorOpen(chamber.Region))
            {
                return false;
            }

            if (ChamberStatusTexts.IsAwaitingProcessStart(chamber.StatusText))
            {
                return false;
            }

            return true;
        }

        private bool IsChamberAvailable(ChamberController.ChamberState chamber)
        {
            return chamber != null
                   && chamber.CurrentWafer == null
                   && !chamber.ReservedForIncoming;
        }

        private void ScheduleChamberTransfer(ChamberController.ChamberState source, ChamberController.ChamberState destination)
        {
            var task = new TransferController.TransferTask
            {
                Wafer = source.CurrentWafer,
                Pickup = source.Region,
                Dropoff = destination.Region,
                SourceChamber = source,
                DestinationChamber = destination
            };

            source.PickupScheduled = true;
            source.StatusText = "Transfer 대기";
            destination.ReservedForIncoming = true;
            EnqueueTransfer(task);
            AddLogMessage($"TM 예약: {EquipmentRegionHelper.FormatRegionLabel(task.Pickup)} -> {EquipmentRegionHelper.FormatRegionLabel(task.Dropoff)} (웨이퍼 #{task.Wafer.Id})", "INFO");
        }

        private void EnqueueTransfer(TransferController.TransferTask task)
        {
            TransferService?.EnqueueTransfer(task);
            if (TmPhase == TransferController.TmPhase.Idle && CurrentTransfer == null)
            {
                tmProcessor?.StartNextTransfer();
            }
        }

        /// <summary>
        /// 실패한 작업 재시도 처리
        /// 재시도 횟수가 최대값을 초과하면 작업을 제거하고 알림
        /// </summary>
        internal bool HandleFailedTransfer(TransferController.TransferTask task, string errorMessage)
        {
            if (task == null) return false;

            task.RetryCount++;
            
            if (task.RetryCount > TransferController.TransferTask.MaxRetryCount)
            {
                // 최대 재시도 횟수 초과 - 작업 제거 및 예약 플래그 해제
                AddLogMessage(
                    $"[작업 실패] {EquipmentRegionHelper.FormatRegionLabel(task.Pickup)} → {EquipmentRegionHelper.FormatRegionLabel(task.Dropoff)} " +
                    $"(웨이퍼 #{task.Wafer?.Id}) - 재시도 횟수 초과 ({task.RetryCount}/{TransferController.TransferTask.MaxRetryCount}) - 작업 제거",
                    "ERROR");
                
                // 웨이퍼 상태 복원 (원래 위치로) + 스케줄 플래그 해제 (고착 방지)
                if (task.SourceChamber != null && task.Wafer != null)
                {
                    task.SourceChamber.CurrentWafer = task.Wafer;
                    task.SourceChamber.PickupScheduled = false;
                    AddLogMessage($"웨이퍼 #{task.Wafer.Id} 상태 복원: {EquipmentRegionHelper.FormatRegionLabel(task.SourceChamber.Region)}", "INFO");
                }
                if (task.DestinationChamber != null)
                {
                    task.DestinationChamber.ReservedForIncoming = false;
                }
                
                return false; // 작업 제거됨
            }
            else
            {
                // 재시도 가능 - 큐에 다시 추가
                AddLogMessage(
                    $"[작업 재시도] {EquipmentRegionHelper.FormatRegionLabel(task.Pickup)} → {EquipmentRegionHelper.FormatRegionLabel(task.Dropoff)} " +
                    $"(웨이퍼 #{task.Wafer?.Id}) - 재시도 {task.RetryCount}/{TransferController.TransferTask.MaxRetryCount}",
                    "WARN");
                
                // 큐에 다시 추가 (재시도)
                TransferService?.EnqueueTransfer(task);
                return true; // 재시도 예정
            }
        }

        internal bool HasPendingPickupFromFoupA()
        {
            // 현재 진행 중인 작업이 FOUP에서 픽업하는지 확인
            if (CurrentTransfer != null && CurrentTransfer.FromFoup)
            {
                return true;
            }
            // 큐에 FOUP에서 픽업하는 작업이 있는지 확인
            if (TransferService != null)
            {
                foreach (var t in TransferService.GetQueuedTasks())
                {
                    if (t.FromFoup && t.Pickup == EquipmentRegion.FoupA) return true;
                }
            }
            return false;
        }
    }
}
