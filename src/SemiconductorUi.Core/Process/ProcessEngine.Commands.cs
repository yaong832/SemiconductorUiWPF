using System;
using System.Linq;
using SemiconductorUi.Hardware;
using SemiconductorUi.Helpers;
using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Process
{
    // 원본 EventHandlers/ProcessEventHandlers, Form1 웨이퍼/FOUP 버튼, TimerEventHandlers를 옮긴 것.
    // 로그인 확인(EnsureLoggedIn)은 화면 쪽에서 먼저 한다.
    public sealed partial class ProcessEngine
    {
        private bool IsEmergencyStopped()
        {
            return EtherCAT_M != null && EtherCAT_M.IsEmergencyStopped;
        }

        public bool IsEmergencyStopActive => IsEmergencyStopped();

        /// <summary>원본 ProcessEventHandlers.ButtonStart_Click</summary>
        public void StartButton()
        {
            if (IsEmergencyStopped())
            {
                Host.ShowMessage(
                    "비상정지 중입니다.\n\n알람 리셋으로 비상정지를 해제하고, 서보 ON과 원점복귀를 한 뒤 시작하세요.",
                    "비상정지 중", MessageKind.Warning);
                return;
            }

            // 환경 알람으로 멈춘 공정: 알람 리셋을 먼저 해야 이어 갈 수 있습니다.
            if (ProcessHeldByAlarm && SimulationRunning)
            {
                if (CurrentProcessState == MainFormViewModel.ProcessState.Error)
                {
                    Host.ShowMessage(
                        "환경 알람으로 공정을 멈춘 상태입니다.\n\n알람 원인을 확인하고 알람 리셋을 누른 뒤 시작하세요.",
                        "알람 리셋 필요", MessageKind.Warning);
                    return;
                }

                ClearAlarmHold();
                HasAlarm = false;
                if (SimulationPaused)
                {
                    ResumeSimulation();
                }
                else
                {
                    SetProcessState(MainFormViewModel.ProcessState.Running, "알람 리셋 후 공정을 이어 갑니다.");
                    UpdateSimulationUi();
                }
                return;
            }

            if (SimulationRunning && !SimulationPaused)
            {
                Host.ShowMessage("이미 공정이 진행 중입니다.", "안내", MessageKind.Information);
                return;
            }

            if (!SimulationRunning)
            {
                if (!IsFoupAMounted || !IsFoupBMounted)
                {
                    Host.ShowMessage("FOUP A/B 장착 상태를 확인해 주세요.", "준비 미완료", MessageKind.Warning);
                    return;
                }

                if (WaferLoadState != MainFormViewModel.WaferLoadStateType.Loading)
                {
                    Host.ShowMessage("웨이퍼를 '로딩' 상태로 전환한 뒤 공정을 시작할 수 있습니다.", "준비 미완료", MessageKind.Warning);
                    return;
                }

                // EtherCAT 연결 확인 (경고만 표시, 시뮬레이션은 계속 진행)
                if (!EthercatConnected)
                {
                    if (!Host.Confirm(
                        "EtherCAT가 연결되지 않았습니다.\n시뮬레이션 모드로 동작합니다.\n계속하시겠습니까?",
                        "EtherCAT 미연결"))
                    {
                        return;
                    }
                }
            }

            HasAlarm = false;

            if (SimulationRunning && SimulationPaused)
            {
                ResumeSimulation();
            }
            else
            {
                StartSimulation();
            }
        }

        /// <summary>원본 ProcessEventHandlers.ButtonPause_Click</summary>
        public void PauseButton()
        {
            if (!SimulationRunning || SimulationPaused)
            {
                Host.ShowMessage("진행 중인 공정이 없습니다.", "안내", MessageKind.Information);
                return;
            }

            // 알람으로 이미 멈춘 공정은 일시정지로 상태를 덮지 않습니다 (일시정지 → 시작으로 알람이 풀리지 않도록).
            if (ProcessHeldByAlarm)
            {
                Host.ShowMessage("환경 알람으로 이미 공정을 멈춘 상태입니다.\n알람 리셋 후 시작을 누르면 이어 갑니다.", "안내", MessageKind.Information);
                return;
            }

            // 하드웨어 모드: 새 명령만 중지 (서보 ON 유지)
            if (IsTmHardwareModeAvailable())
            {
                AddLogMessage("하드웨어 일시정지: 새 명령 중지 (서보 ON 유지)", "WARN");
            }

            PauseSimulation();
        }

        /// <summary>원본 ProcessEventHandlers.ButtonStop_Click</summary>
        public void EmergencyStopButton()
        {
            ExecuteEmergencyStop("메인 화면 긴급 정지 버튼");
            Host.ShowMessage(
                "비상정지가 실행되었습니다.\n\n" +
                "• 서보: OFF (두 축)\n" +
                "• 진공: 유지 (웨이퍼를 잡고 있으면 그대로)\n" +
                "• 실린더, 도어: 현재 상태 유지\n" +
                "• 장비 명령: 해제 전까지 차단\n\n" +
                "다시 움직이려면:\n" +
                "1. 장비 상태 확인\n" +
                "2. 알람 리셋으로 비상정지 해제\n" +
                "3. 서보 ON → 원점복귀",
                "비상정지", MessageKind.Warning);
        }

        /// <summary>원본 ProcessEventHandlers.ButtonResetAlarm_Click</summary>
        public void ResetAlarmButton()
        {
            bool emergencyStopCleared = false;
            if (IsEmergencyStopped())
            {
                var faultedAxes = GetFaultedServoAxes();
                string faultNotice = faultedAxes.Count > 0
                    ? $"드라이브 알람이 남아 있어 알람 리셋 신호도 함께 보냅니다:\n{DescribeServoAxes(faultedAxes)}\n\n"
                    : "";
                bool confirm = Host.Confirm(
                    "비상정지를 해제하시겠습니까?\n\n" +
                    faultNotice +
                    "해제하기 전에 확인하세요:\n" +
                    "• 장비 근처에 사람이 없는지\n" +
                    "• 웨이퍼, 블레이드, 실린더 위치\n\n" +
                    "해제해도 서보는 꺼진 채로 남습니다.\n" +
                    "해제한 뒤 서보 ON → 원점복귀를 다시 하세요.",
                    "비상정지 해제",
                    defaultNo: true);
                if (!confirm)
                {
                    return;
                }

                EtherCAT_M.ClearEmergencyStop();
                emergencyStopCleared = true;
                AddLogMessage("비상정지 해제 (서보 OFF 유지, 서보 ON과 원점복귀 필요)", "WARN");
                // 알람 이력: 비상정지·서보 알람 해제 기록 (서보 알람이 남아 있으면 1초 뒤 다시 발생으로 남는다)
                ClearRecordedAlarms(true);
                ResetServoFaultsAfterEmergencyStopCleared();
            }

            // 헤더에 알람이 표시되어 있는지 확인
            if (!Host.HasHeaderAlarm())
            {
                if (!emergencyStopCleared)
                {
                    Host.ShowMessage("해제할 알람이 없습니다.", "안내", MessageKind.Information);
                }
                UpdateServoStatusLabel();
                UpdateProcessControlButtons();
                return;
            }

            // 모든 알람 상태 리셋
            HasAlarm = false;
            ViewModel.ClearAllChamberAlarms();
            // verification 알람도 리셋됨을 표시
            ViewModel.VerificationAlarmDismissed = true;

            // 알람 리셋: 저장된 이전 상태로 복귀 또는 적절한 상태로 설정
            MainFormViewModel.ProcessState targetState;
            string resetMessage;

            var stateBeforeAlarm = _stateBeforeAlarm;
            if (ProcessHeldByAlarm && SimulationRunning)
            {
                // 환경 알람으로 멈춘 공정: 바로 이어 가지 않고, 시작을 누를 때까지 기다립니다.
                targetState = MainFormViewModel.ProcessState.Paused;
                resetMessage = "알람을 리셋했습니다. 장비 상태를 확인하고 시작을 누르면 공정을 이어 갑니다.";
                _stateBeforeAlarm = null;
            }
            else if (stateBeforeAlarm.HasValue)
            {
                var savedState = stateBeforeAlarm.Value;
                // 저장된 상태가 Running이지만 공정이 중단된 경우(AbortSimulation 호출됨)는 Idle로 설정
                if (savedState == MainFormViewModel.ProcessState.Running && !SimulationRunning)
                {
                    targetState = MainFormViewModel.ProcessState.Idle;
                    resetMessage = "알람을 리셋했습니다. 공정이 중단되었으므로 대기 상태로 설정되었습니다.";
                }
                else
                {
                    targetState = savedState;
                    resetMessage = $"알람을 리셋했습니다. 이전 상태({targetState})로 복귀합니다.";
                }
                _stateBeforeAlarm = null;
            }
            else
            {
                if (SimulationRunning)
                {
                    targetState = MainFormViewModel.ProcessState.Running;
                    resetMessage = "알람을 리셋했습니다. 공정을 계속합니다.";
                }
                else
                {
                    targetState = MainFormViewModel.ProcessState.Idle;
                    resetMessage = "알람을 리셋했습니다.";
                }
            }

            // 공정 중이 아닐 때만 시뮬레이션 상태 초기화
            if (!SimulationRunning)
            {
                if (CurrentProcessState == MainFormViewModel.ProcessState.Error)
                {
                    InitializeSimulationState();
                }
            }

            SetProcessState(targetState, resetMessage);
            AddLogMessage("알람이 리셋되었습니다.", "INFO");
            // 알람 이력: 걸려 있던 알람 모두 해제 기록 (해제한 사용자 포함)
            ClearRecordedAlarms(false);
            Host.ClearHeaderAlarm();
            UpdateSimulationUi();
            UpdateServoStatusLabel();
            UpdateProcessControlButtons();
        }

        /// <summary>원본 ProcessEventHandlers.ButtonResetProcess_Click</summary>
        public void ResetProcessButton()
        {
            if (IsEmergencyStopped())
            {
                Host.ShowMessage(
                    "비상정지 중에는 공정 리셋을 할 수 없습니다.\n공정 리셋은 진공 OFF, 실린더 후진, 원점복귀를 함께 수행합니다.\n\n먼저 알람 리셋으로 비상정지를 해제하세요.",
                    "비상정지 중", MessageKind.Warning);
                return;
            }

            string confirmMessage = "현재 공정을 완전히 초기화하시겠습니까?\nTM 대기 상태, FOUP 표시, 챔버 상태 등이 리셋됩니다.";
            if (EthercatConnected)
            {
                confirmMessage += "\n\n[하드웨어 모드]\n• 실린더 후진 확인\n• 진공 OFF\n• 원점복귀 수행";
            }

            if (!Host.Confirm(confirmMessage, "공정 리셋 확인"))
            {
                return;
            }

            SimulationController?.StopTimer();

            if (TransferService.CurrentTransfer != null)
            {
                AddLogMessage($"공정 리셋: 진행 중인 작업 취소 - {EquipmentRegionHelper.FormatRegionLabel(TransferService.CurrentTransfer.Pickup)} → {EquipmentRegionHelper.FormatRegionLabel(TransferService.CurrentTransfer.Dropoff)}", "WARN");
            }

            // 하드웨어 동작 플래그 리셋 (진행 중인 작업 중지)
            TmHardwareActionPending = false;
            TmSettleWaiting = false;

            TransferService.ResetToIdle();
            TransferService.ClearQueue();

            // 소프트웨어 상태는 즉시 리셋 (UI 블로킹 방지)
            InitializeSimulationState();
            SetProcessState(MainFormViewModel.ProcessState.Idle, "공정을 초기화했습니다.");
            UpdateSimulationUi();
            UpdateTmAnimationIdleTarget();
            UpdateServoStatusLabel();
            AddLogMessage("사용자가 공정을 수동 리셋했습니다.", "INFO");

            // 하드웨어 초기화(대기 포함)는 백그라운드에서 수행
            if (EthercatConnected && EtherCAT_M != null)
            {
                AddLogMessage("공정 리셋: 하드웨어 초기화 시작 (백그라운드)", "INFO");
                var ethercat = EtherCAT_M;
                var servoOn = IsServoOn;
                HardwareOperation.Run(ethercat, () =>
                {
                    try
                    {
                        ethercat.Digital_Output(14, false);
                        ethercat.Digital_Output(15, false);
                        AddLogMessage("공정 리셋: 진공 OFF", "INFO");

                        bool cylinderRetracted = false;
                        try { cylinderRetracted = ethercat.Digital_Input(12); }
                        catch (Exception ex) { AddLogMessage($"실린더 상태 확인 오류: {ex.Message}", "WARN"); }

                        if (!cylinderRetracted)
                        {
                            AddLogMessage("공정 리셋: 실린더 후진 시도", "INFO");
                            ethercat.Digital_Output(12, false);
                            ethercat.Digital_Output(13, true);

                            var timeout = DateTime.Now.AddSeconds(5);
                            while (DateTime.Now < timeout)
                            {
                                ethercat.ThrowIfEmergencyStopped();
                                try
                                {
                                    if (ethercat.Digital_Input(12)) { cylinderRetracted = true; break; }
                                }
                                catch { }
                                System.Threading.Thread.Sleep(100);
                            }

                            if (!cylinderRetracted)
                            {
                                AddLogMessage("공정 리셋: 실린더 후진 타임아웃 - 수동 확인 필요", "WARN");
                                Host.BeginInvoke(() => Host.ShowMessage(
                                    "실린더 후진이 완료되지 않았습니다.\n수동으로 실린더 상태를 확인해주세요.",
                                    "경고", MessageKind.Warning));
                            }
                            else
                            {
                                AddLogMessage("공정 리셋: 실린더 후진 완료", "INFO");
                            }
                        }

                        if (servoOn && cylinderRetracted)
                        {
                            // 상하 원점복귀가 끝난 것을 확인한 뒤에 좌우를 보냅니다. 실린더는 두 단계 모두 후진이어야 합니다.
                            AddLogMessage("공정 리셋: 원점복귀 시작", "INFO");
                            string homingFailReason;
                            bool emergencyStopped = false;
                            try
                            {
                                homingFailReason = new ResetHomingSequence(ethercat).Run(msg => AddLogMessage(msg, "INFO"));
                            }
                            catch (EmergencyStopActiveException stopEx)
                            {
                                emergencyStopped = true;
                                homingFailReason = stopEx.Message;
                            }
                            catch (Exception homingEx)
                            {
                                homingFailReason = $"원점복귀 오류: {homingEx.Message}";
                            }

                            if (emergencyStopped)
                            {
                                AddLogMessage($"공정 리셋 원점복귀 중단: {homingFailReason}", "WARN");
                                Host.BeginInvoke(() => TmHardwareInitialized = false);
                            }
                            else if (homingFailReason == null)
                            {
                                Host.BeginInvoke(() =>
                                {
                                    TmHardwareInitialized = true;
                                    TmHardwareController?.MarkHomedAfterSuccessfulHoming();
                                });
                            }
                            else
                            {
                                AddLogMessage($"공정 리셋 원점복귀 실패: {homingFailReason}", "ERROR");
                                Host.BeginInvoke(() =>
                                {
                                    TmHardwareInitialized = false;
                                    Host.ShowMessage(
                                        $"공정 초기화 중 원점복귀를 끝내지 못했습니다.\n\n{homingFailReason}\n\n장비 상태를 확인한 뒤 원점복귀를 다시 해 주세요.",
                                        "원점복귀 실패", MessageKind.Warning);
                                });
                            }
                        }
                        else if (!servoOn)
                        {
                            Host.BeginInvoke(() =>
                            {
                                AddLogMessage("공정 리셋: 서보 OFF 상태 - 서보 ON 후 원점복귀 필요", "WARN");
                                TmHardwareInitialized = false;
                            });
                        }

                        TmHardwareActionPending = false;
                        TmSettleWaiting = false;

                        Host.BeginInvoke(() =>
                        {
                            if (TmHardwareController != null)
                            {
                                try
                                {
                                    TmHardwareController.UpdateCurrentPositions();
                                    long currentX = TmHardwareController.CurrentAxis2Position;
                                    long currentY = TmHardwareController.CurrentAxis1Position;
                                    var hardwareRegion = EquipmentRegionHelper.DetermineRegionFromPosition(currentX, currentY, TmHardwareController.Positions);
                                    TmVisualTarget = hardwareRegion;
                                    TmCurrentPosition = hardwareRegion;
                                    UpdateTmVisualizationFromHardware();
                                    AddLogMessage($"공정 리셋: TM 위치 업데이트 완료 - {EquipmentRegionHelper.FormatRegionLabel(hardwareRegion)}", "INFO");
                                }
                                catch (Exception ex)
                                {
                                    AddLogMessage($"공정 리셋: TM 위치 읽기 오류: {ex.Message}", "WARN");
                                }
                            }
                            UpdateServoStatusLabel();
                        });
                    }
                    catch (Exception ex)
                    {
                        AddLogMessage($"공정 리셋 하드웨어 오류: {ex.Message}", "ERROR");
                    }
                });
            }
        }

        /// <summary>원본 Form1.buttonServoHome_Click: 상하(Axis1) → 좌우(Axis2) 순서로 원점복귀</summary>
        public void HomeButton()
        {
            if (!EthercatConnected)
            {
                Host.ShowMessage("먼저 EtherCAT을 연결해주세요.", "연결 필요", MessageKind.Warning);
                return;
            }

            try
            {
                // 실린더 상태 확인 - 좌우 이동 전 필수
                bool cylinderRetracted = EtherCAT_M.Digital_Input(12); // 후진 센서
                if (!cylinderRetracted)
                {
                    Host.ShowMessage("웨이퍼 이송 실린더가 전진되어 있습니다.\n실린더 전진 상태에서는 좌우 이동이 불가합니다.\n먼저 실린더를 후진해주세요.", "안전 경고", MessageKind.Warning);
                    return;
                }

                // 비동기로 원점복귀 실행 (UI 블로킹 방지)
                HardwareOperation.Run(EtherCAT_M, () => PerformHomingSequence());
            }
            catch (Exception ex)
            {
                AddLogMessage($"원점복귀 오류: {ex.Message}", "ERROR");
                Host.ShowMessage($"원점복귀 오류: {ex.Message}", "오류", MessageKind.Error);
            }
        }

        /// <summary>원본 Form1.buttonToggleFoupMount_Click</summary>
        public void ToggleFoupMountButton()
        {
            // 기존 단일 토글은 숨김 처리 예정. 눌렸다면 A/B를 함께 토글하는 보조 동작만 수행.
            var newState = !(IsFoupAMounted && IsFoupBMounted);
            ViewModel.IsFoupAMounted = newState;
            ViewModel.IsFoupBMounted = newState;
            ViewModel.IsFoupMounted = IsFoupAMounted && IsFoupBMounted;
            AddLogMessage($"FOUP 장착 상태 일괄 변경: A={BoolToMountText(IsFoupAMounted)}, B={BoolToMountText(IsFoupBMounted)}", "INFO");
            UpdateSimulationUi();
        }

        private static string BoolToMountText(bool mounted) => mounted ? "장착" : "미장착";

        /// <summary>원본 Form1.buttonWaferLoading_Click</summary>
        public void WaferLoadingButton()
        {
            if (!IsFoupAMounted)
            {
                Host.ShowMessage("FOUP A가 미장착 상태입니다. 먼저 FOUP A를 장착해 주세요.", "준비 미완료", MessageKind.Warning);
                return;
            }

            var count = Host.PromptWaferCount(UserWaferLoadCount, 1, AppSettings.MaxFoupCapacity);
            if (count == null)
            {
                return;
            }

            UserWaferLoadCount = count.Value;
            SetWaferLoadState(MainFormViewModel.WaferLoadStateType.Loading);
            AddLogMessage($"웨이퍼 로딩: {UserWaferLoadCount}장 설정", "INFO");

            // 웨이퍼 로딩 시 즉시 UI에 반영
            foupManager?.LoadWafersToFoupA(UserWaferLoadCount, SecondExposureEnabled);
            UpdateSimulationUi();
        }

        /// <summary>원본 Form1.buttonWaferUnloading_Click</summary>
        public void WaferUnloadingButton()
        {
            if (!IsFoupBMounted)
            {
                Host.ShowMessage("FOUP B가 미장착 상태입니다. 먼저 FOUP B를 장착해 주세요.", "준비 미완료", MessageKind.Warning);
                return;
            }

            // FOUP B 언로딩: 완료 웨이퍼 배출 처리
            var unloadedCount = foupManager?.UnloadFoupB() ?? 0;
            if (unloadedCount > 0)
            {
                AddLogMessage($"FOUP B 언로딩: {unloadedCount}장 배출 처리", "INFO");
            }

            SetWaferLoadState(MainFormViewModel.WaferLoadStateType.Unloading);
        }

        /// <summary>
        /// 원본 TimerEventHandlers.SimulationTimer_Tick. 하드웨어 모드와 시뮬레이션 모드를 구분하여 공정 로직을 처리합니다.
        /// </summary>
        private const int HardwareScreenRefreshMs = 150;
        private DateTime _lastHardwareScreenRefresh = DateTime.MinValue;

        public void OnSimulationTick()
        {
            // EtherCAT 연결 시 서보 상태 라벨 항상 업데이트
            if (EthercatConnected)
            {
                UpdateServoStatusLabel();
            }

            if (!SimulationRunning || SimulationPaused)
            {
                // 하드웨어 모드: 일시정지 중에도 실제 위치 표시
                if (IsTmHardwareModeAvailable())
                {
                    UpdateTmVisualizationFromHardware();
                }
                return;
            }

            if (IsTmHardwareModeAvailable())
            {
                // ========== 하드웨어 모드 ==========
                // Phase는 로직 제어용으로만 사용하고, UI는 항상 실제 하드웨어 상태 사용
                SimulationController?.IncrementElapsedTime();

                DecrementChamberTime(ChamberAState);
                DecrementChamberTime(ChamberBState);
                DecrementChamberTime(ChamberCState);

                // 스케줄링을 ProcessTm() 전에 수행 → 다음 작업을 먼저 큐에 넣고, ProcessTm()에서 바로 시작
                bool tmBusyInHardwareMode = TmHardwareActionPending;
                if (!tmBusyInHardwareMode)
                {
                    // 우선순위: 완료 웨이퍼 빼기 > A→B/C 이송 > 새 웨이퍼 투입
                    TryScheduleTransferToFoupB(ChamberBState);
                    TryScheduleTransferToFoupB(ChamberCState);
                    TryScheduleTransferFromChamberA();
                    TryScheduleLoadFromFoupA();
                }

                ProcessTm();

                // 공정 완료 확인
                if (!ProcessHeldByAlarm
                    && (foupManager?.GetFoupBCount() ?? 0) - FoupBCompletedBaseline >= GetActiveTarget()
                    && ChamberAState.CurrentWafer == null
                    && ChamberBState.CurrentWafer == null
                    && ChamberCState.CurrentWafer == null
                    && (TransferController?.QueueCount ?? 0) == 0
                    && CurrentTransfer == null)
                {
                    if (IsTmHardwareModeAvailable())
                    {
                        AddLogMessage("공정 종료 - TM 원점복귀 수행", "INFO");
                        PerformShutdownHoming();
                    }

                    SimulationController?.Stop();

                    if (IsTmHardwareModeAvailable())
                    {
                        AddLogMessage("하드웨어 모드 공정 완료", "INFO");
                        SetProcessState(MainFormViewModel.ProcessState.Idle, "하드웨어 모드 공정이 완료되었습니다.");
                    }
                    else
                    {
                        AddLogMessage("시뮬레이션 완료", "INFO");
                        SetProcessState(MainFormViewModel.ProcessState.Idle, "시뮬레이션이 완료되었습니다.");
                    }

                    UpdateSimulationUi();
                }
            }
            else
            {
                // ========== 시뮬레이션 모드 ==========
                // 녹색 깜빡임은 진행 중일 때만. 알람(적색)이나 알람 리셋 후 대기(황색) 램프를 덮어쓰지 않습니다.
                if (CurrentProcessState == MainFormViewModel.ProcessState.Running)
                {
                    mainLampBlinkState = !mainLampBlinkState;
                    UpdateMainLampColors(mainLampBlinkState ? LimeGreen : (Models.ArgbColor?)null, false, false);
                }
                AdvanceSimulation();

                // 시뮬레이션 모드: Phase 기반으로 TM 시각화 업데이트
                UpdateTmVisualization();
            }

            // 하드웨어 모드는 공정 루프가 50ms마다 돌지만, 화면 전체 갱신은 예전처럼 150ms 간격으로만 합니다.
            // (공정 판단만 자주 하고, 화면 다시 그리기 부담은 늘리지 않기 위함. 원본 51f4792)
            if (IsTmHardwareModeAvailable())
            {
                var now = DateTime.Now;
                if ((now - _lastHardwareScreenRefresh).TotalMilliseconds < HardwareScreenRefreshMs)
                {
                    return;
                }
                _lastHardwareScreenRefresh = now;
            }

            CaptureTrendSampleTick(TrendClock());
            UpdateSimulationUi();
        }

        private bool _lastHardwareUiUpdateErrorLogged;
        private int _hardwareSyncCounter;

        /// <summary>원본 TimerEventHandlers.HardwareUiUpdateTimer_Tick (50ms 주기)</summary>
        public void OnHardwareUiTick()
        {
            if (IsTmHardwareModeAvailable() && EthercatConnected)
            {
                try
                {
                    UpdateTmVisualizationFromHardware();
                    // 주기적으로 서보 상태 동기화 (5초마다)
                    if (_hardwareSyncCounter % 100 == 0)
                    {
                        UpdateServoStatusLabel();
                    }
                    _hardwareSyncCounter++;
                }
                catch (Exception ex)
                {
                    if (!_lastHardwareUiUpdateErrorLogged)
                    {
                        AddLogMessage($"UI 업데이트 타이머 오류: {ex.Message}", "WARN");
                        _lastHardwareUiUpdateErrorLogged = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"UI 업데이트 타이머 오류 (반복): {ex.Message}");
                    }
                }
            }
            else
            {
                _lastHardwareUiUpdateErrorLogged = false;
                _hardwareSyncCounter = 0;
            }
        }
    }
}
