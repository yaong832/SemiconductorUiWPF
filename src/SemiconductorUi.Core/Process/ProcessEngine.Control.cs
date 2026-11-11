using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.Services;
using SemiconductorUi.ViewModels;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    // 원본 Form1.cs에서 그대로 옮긴 공정 코드. UI 호출만 IProcessHost로 바꿨다.
    public sealed partial class ProcessEngine
    {
        internal void InitializeSimulationState()
        {
            ClearAlarmHold();
            InitializeSimulationStateCore();
        }

        internal void StartSimulation()
        {
            if (_hardwareInitInProgress)
            {
                AddLogMessage("하드웨어 초기화가 진행 중입니다. 완료 후 다시 시도하세요.", "WARN");
                return;
            }

            ClearAlarmHold();

            // EtherCAT 연결 확인 (연결되어 있으면 실제 장비 제어)
            if (EthercatConnected)
            {
                try
                {
                    // 초기 상태 설정: 모든 도어 닫힘, Chamber 램프 OFF
                    ResetDoorStates();
                    
                    // 모든 Chamber 램프 OFF
                    SetChamberLamp(EquipmentRegion.ChamberA, false);
                    SetChamberLamp(EquipmentRegion.ChamberB, false);
                    SetChamberLamp(EquipmentRegion.ChamberC, false);
                    
                    // 공정 시작 전 서보 ON + 원점복귀 — UI 스레드 블로킹 방지(백그라운드)
                    if (!TmHardwareInitialized)
                    {
                        AddLogMessage("공정 시작 - TM 하드웨어 초기화 중 (서보 ON + 원점복귀, UI 비차단)", "INFO");
                        BeginHardwareInitThenStartSimulation();
                        return;
                    }

                    // 이미 초기화된 경우, 서보 상태 동기화
                    if (TmHardwareController != null)
                    {
                        AddLogMessage("공정 시작 - 서보 상태 동기화", "INFO");
                        TmHardwareController.SyncServoStatus();
                        
                        if (!TmHardwareController.IsServoOn)
                        {
                            AddLogMessage("서보가 OFF 상태입니다. 서보를 ON한 후 공정을 시작해주세요.", "ERROR");
                            Host.ShowMessage(
                                "서보가 OFF 상태입니다.\n\n" +
                                "확인 사항:\n" +
                                "1. 서보 모터 ON 확인\n" +
                                "2. 원점복귀 완료 확인\n\n" +
                                "공정을 시작할 수 없습니다.",
                                "서보 OFF", MessageKind.Warning);
                            return;
                        }
                    }
                    
                    if (TmHardwareInitialized)
                    {
                        AddLogMessage("하드웨어 모드로 공정 시작 - 실제 장비 제어 활성화", "INFO");
                    }
                    else
                    {
                        AddLogMessage("시뮬레이션 모드로 시작 (TM 하드웨어 미초기화)", "INFO");
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage($"장비 초기화 오류: {ex.Message}", "ERROR");
                }
            }

            CompleteStartSimulation();
        }

        /// <summary>
        /// 서보 ON + 원점복귀를 백그라운드에서 수행한 뒤 공정 시작을 이어간다.
        /// </summary>
        private void BeginHardwareInitThenStartSimulation()
        {
            _hardwareInitInProgress = true;
            UpdateProcessControlButtons();

            HardwareOperation.Run(EtherCAT_M, () =>
            {
                bool ok = false;
                string failReason = null;
                try
                {
                    ok = PerformAutoServoOnAndHoming(out failReason);
                }
                catch (Exception ex)
                {
                    failReason = ex.Message;
                    ok = false;
                }

                Host.BeginInvoke(() =>
                {
                    _hardwareInitInProgress = false;
                    UpdateProcessControlButtons();

                    if (!ok)
                    {
                        AddLogMessage($"TM 하드웨어 초기화 실패 - 공정 시작 불가: {failReason}", "ERROR");
                        Host.ShowMessage(
                            "TM 하드웨어 초기화에 실패했습니다.\n\n" +
                            (string.IsNullOrEmpty(failReason) ? "" : $"사유: {failReason}\n\n") +
                            "확인 사항:\n" +
                            "1. 서보 모터 상태 확인\n" +
                            "2. 실린더가 후진 상태인지 확인\n" +
                            "3. EtherCAT 연결 상태 확인\n\n" +
                            "공정을 시작할 수 없습니다.",
                            "초기화 실패", MessageKind.Error);
                        return;
                    }

                    AddLogMessage("하드웨어 모드로 공정 시작 - 실제 장비 제어 활성화", "INFO");
                    CompleteStartSimulation();
                });
            });
        }

        /// <summary>
        /// 하드웨어 준비 완료 후(또는 시뮬) 실제 공정 상태 머신 기동.
        /// </summary>
        private void CompleteStartSimulation()
        {
            InitializeSimulationStateCore();
            // 공정 시작 시 환경 텔레메트리 초기화 (PV 값이 이제부터 생성됨)
            InitializeEnvironmentTelemetry();
            // 이전 완료 수량을 기준선으로 저장하여 현재 배치 완료 판정을 분리
            FoupBCompletedBaseline = foupManager?.GetFoupBCount() ?? 0;
            int targetLoadCount = GetEffectiveWaferTarget();
            activeBatchTargetCount = targetLoadCount;
            
            // 웨이퍼 로딩 시 이미 큐에 추가되었을 수 있으므로, 큐가 비어있을 때만 추가
            if (foupManager?.HasWafersInFoupA() != true)
            {
                foupManager?.LoadWafersToFoupA(targetLoadCount, SecondExposureEnabled);
            }

            // SimulationController를 통해 시뮬레이션 시작
            SimulationController?.Start(IsTmHardwareModeAvailable());
            RecordWaferLotStarted(targetLoadCount);
            
            // 하드웨어 모드와 시뮬레이션 모드 구분하여 로그 메시지 표시
            if (IsTmHardwareModeAvailable())
            {
                AddLogMessage($"하드웨어 모드 공정 시작 - 웨이퍼 {targetLoadCount}장 투입", "INFO");
                SetProcessState(ProcessState.Running, $"하드웨어 모드 공정 시작 - 웨이퍼 {targetLoadCount}장 투입");
            }
            else
            {
                AddLogMessage($"시뮬레이션 시작 - 웨이퍼 {targetLoadCount}장 투입", "INFO");
                SetProcessState(ProcessState.Running, $"시뮬레이션 시작 - 웨이퍼 {targetLoadCount}장 투입");
            }
            
            TryScheduleLoadFromFoupA();
            ProcessTm();
            UpdateSimulationUi();
        }

        internal void ResumeSimulation()
        {
            if (!SimulationRunning || !SimulationPaused)
            {
                return;
            }

            // 일시정지 시간만큼 tmHardwareActionStartTime 조정 (타임아웃 계산 시 일시정지 시간 제외)
            if (TmHardwareActionPending && pauseStartTime.HasValue)
            {
                var pauseDuration = DateTime.Now - pauseStartTime.Value;
                tmHardwareActionStartTime = tmHardwareActionStartTime.Add(pauseDuration);
                pauseStartTime = null; // 재개 후 초기화
            }

            // 하드웨어 모드: 서보는 ON 상태 유지되어 있음
            // 일시정지 시 서보를 OFF하지 않았으므로 다시 ON할 필요 없음
            if (IsTmHardwareModeAvailable())
            {
                AddLogMessage("하드웨어 모드 공정 재개: 현재 Phase부터 계속", "INFO");
                SetProcessState(ProcessState.Running, "하드웨어 모드 공정을 재개했습니다.");
            }
            else
            {
                AddLogMessage("시뮬레이션 재개: 현재 Phase부터 계속", "INFO");
                SetProcessState(ProcessState.Running, "시뮬레이션을 재개했습니다.");
            }

            // SimulationController를 통해 시뮬레이션 재개
            SimulationController?.Resume(IsTmHardwareModeAvailable());
            UpdateSimulationUi();
        }

        private int GetEffectiveWaferTarget()
        {
            // 레시피 장수와 사용자 로딩 수 중 더 작은 값을 목표로 사용
            int cappedUser = Math.Max(1, Math.Min(AppSettings.MaxFoupCapacity, UserWaferLoadCount));
            int cappedRecipe = Math.Max(1, Math.Min(AppSettings.MaxFoupCapacity, CurrentRecipeWaferCount));
            return Math.Min(cappedUser, cappedRecipe);
        }

        internal int GetActiveTarget()
        {
            return activeBatchTargetCount > 0 ? activeBatchTargetCount : GetEffectiveWaferTarget();
        }

        internal void PauseSimulation()
        {
            if (!SimulationRunning || SimulationPaused)
            {
                return;
            }

            // 일시정지 시작 시간 저장 (타임아웃 계산 시 일시정지 시간 제외용)
            if (TmHardwareActionPending)
            {
                pauseStartTime = DateTime.Now;
            }

            // SimulationController를 통해 시뮬레이션 일시정지
            SimulationController?.Pause();
            
            // 하드웨어 모드와 시뮬레이션 모드 구분하여 로그 메시지 표시
            if (IsTmHardwareModeAvailable())
            {
                AddLogMessage("하드웨어 모드 공정 일시정지", "INFO");
                SetProcessState(ProcessState.Paused, "하드웨어 모드 공정을 일시 정지했습니다.");
            }
            else
            {
                AddLogMessage("시뮬레이션 일시정지", "INFO");
                SetProcessState(ProcessState.Paused, "시뮬레이션을 일시 정지했습니다.");
            }
            
            UpdateSimulationUi();
        }

        internal void AbortSimulation(string reason, bool setErrorState)
        {
            RecordWaferLotAborted(reason);
            SimulationController?.Abort();
            ClearAlarmHold();
            if (setErrorState)
            {
                SetProcessState(ProcessState.Error, reason);
            }
            else
            {
                SetProcessState(ProcessState.Idle, reason);
            }
            UpdateSimulationUi();
            UpdateTmAnimationIdleTarget();
        }

        /// <summary>
        /// 비상정지. 메인 화면과 장비 제어 창의 긴급 정지 버튼이 부릅니다 (UI 스레드).
        /// 두 축 서보를 끄고, 알람 리셋으로 해제하기 전까지 장비 명령을 막습니다.
        /// 진공은 끄지 않습니다. 웨이퍼를 들고 있을 때 떨어뜨리지 않기 위해서입니다.
        /// </summary>
        /// <param name="source">어디서 눌렀는지 (로그용)</param>
        public void ExecuteEmergencyStop(string source)
        {
            bool servoOffSent = false;
            if (EtherCAT_M != null)
            {
                try
                {
                    EtherCAT_M.EmergencyStop();
                    servoOffSent = EthercatConnected;
                }
                catch (Exception ex)
                {
                    AddLogMessage($"비상정지 서보 OFF 전송 오류: {ex.Message} - 비상 차단 스위치로 전원을 끊으세요", "ALARM");
                    RecordAlarm(AlarmCodes.EmergencyStopSendFailed, "TM", ex.Message);
                }
            }

            IsServoOn = false;
            TmHardwareInitialized = false;
            TmHardwareActionPending = false;
            TmSettleWaiting = false;
            HasAlarm = true;

            AddLogMessage(servoOffSent
                ? $"비상정지 ({source}): 서보 OFF 전송, 장비 명령 차단. 진공은 유지합니다."
                : $"비상정지 ({source}): EtherCAT 미연결 상태, 장비 명령 차단", "ALARM");
            RecordAlarm(AlarmCodes.EmergencyStop, "장비", servoOffSent ? source : $"{source} (EtherCAT 미연결)");

            string reason = "비상정지. 알람 리셋으로 해제한 뒤 서보 ON과 원점복귀를 다시 하세요.";
            if (SimulationRunning)
            {
                AbortSimulation(reason, true);
            }
            else
            {
                SetProcessState(ProcessState.Error, reason);
            }

            UpdateServoStatusLabel();
            UpdateProcessControlButtons();

            if (servoOffSent)
            {
                ScheduleEmergencyStopVerification();
            }
        }

        private void ScheduleEmergencyStopVerification()
        {
            RunOnceAfter(EmergencyStopVerifyDelayMs, VerifyServoOffAfterEmergencyStop);
        }

        /// <summary>
        /// UI 스레드에서 지정 시간 뒤 한 번 실행합니다.
        /// </summary>
        private void RunOnceAfter(int delayMs, Action action)
        {
            Host.RunOnceAfter(delayMs, action);
        }

        /// <summary>
        /// 서보 드라이브 알람이 새로 생기면 비상정지와 같은 경로로 멈춥니다.
        /// 드라이브는 알람이 나면 스스로 출력을 끊지만, 그대로 두면 공정이 다음 이송 명령을 계속 보냅니다.
        /// </summary>
        private void ServoFaultTimer_Tick(object sender, EventArgs e)
        {
            if (EtherCAT_M == null || !EthercatConnected)
            {
                _servoFaultMonitor.Reset();
                return;
            }

            IList<int> newFaults;
            try
            {
                newFaults = _servoFaultMonitor.PollNewFaults();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"서보 알람 확인 오류: {ex.Message}");
                return;
            }

            if (newFaults.Count == 0)
            {
                return;
            }

            string detail = DescribeServoAxes(newFaults);
            RecordServoFaultAlarms(newFaults);
            if (EtherCAT_M.IsEmergencyStopped)
            {
                AddLogMessage($"서보 알람 발생: {detail} (이미 비상정지 중)", "ALARM");
                return;
            }

            // 감시 상태는 이미 갱신되었으므로, 아래 메시지 창이 떠 있는 동안 타이머가 다시 돌아도 중복으로 멈추지 않습니다.
            ExecuteEmergencyStop($"서보 알람: {detail}");
            Host.ShowMessage(
                $"서보 드라이브 알람이 발생해 비상정지했습니다.\n\n{detail}\n\n" +
                "• 서보: OFF, 진행 중이던 공정과 이송은 중단됨\n" +
                "• 진공: 유지\n\n" +
                "드라이브 표시창의 알람 코드를 확인하고 원인을 없앤 뒤,\n" +
                "알람 리셋 → 서보 ON → 원점복귀 순서로 다시 시작하세요.",
                "서보 알람", MessageKind.Error);
        }

        /// <summary>
        /// 지금 드라이브 알람이 켜져 있는 축. 연결되지 않았으면 빈 목록.
        /// </summary>
        internal IList<int> GetFaultedServoAxes()
        {
            var faulted = new List<int>();
            if (EtherCAT_M == null || !EthercatConnected)
            {
                return faulted;
            }

            foreach (int axis in ServoAxes)
            {
                if (EtherCAT_M.IsAxisFault(axis))
                {
                    faulted.Add(axis);
                }
            }
            return faulted;
        }

        /// <summary>
        /// 축 이름과 상태워드 원값. 예: "Axis1(상하) 상태워드 0x0218"
        /// </summary>
        internal string DescribeServoAxes(IList<int> axes)
        {
            var parts = new List<string>();
            foreach (int axis in axes)
            {
                string name = axis == 1 ? "Axis1(상하)" : "Axis2(좌우)";
                ushort statusWord = EtherCAT_M != null ? EtherCAT_M.GetStatusWord(axis) : (ushort)0;
                parts.Add($"{name} 상태워드 0x{statusWord:X4}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// 비상정지를 해제한 직후에 부릅니다. 드라이브 알람이 남아 있으면 알람 리셋 신호를 보내고,
        /// 잠시 뒤에도 알람이 남아 있으면 다시 비상정지를 겁니다.
        /// </summary>
        internal void ResetServoFaultsAfterEmergencyStopCleared()
        {
            IList<int> faulted = GetFaultedServoAxes();
            if (faulted.Count == 0)
            {
                return;
            }

            string detail = DescribeServoAxes(faulted);
            foreach (int axis in faulted)
            {
                try
                {
                    EtherCAT_M.ResetAxisFault(axis);
                }
                catch (Exception ex)
                {
                    AddLogMessage($"드라이브 알람 리셋 전송 오류 (Axis{axis}): {ex.Message}", "ERROR");
                }
            }
            AddLogMessage($"드라이브 알람 리셋 전송: {detail}", "WARN");

            RunOnceAfter(ServoFaultResetVerifyDelayMs, () =>
            {
                IList<int> stillFaulted = GetFaultedServoAxes();
                if (stillFaulted.Count == 0)
                {
                    AddLogMessage("드라이브 알람 해제 확인", "INFO");
                    return;
                }

                string stillDetail = DescribeServoAxes(stillFaulted);
                RecordServoFaultAlarms(stillFaulted);
                if (EtherCAT_M.IsEmergencyStopped)
                {
                    AddLogMessage($"드라이브 알람이 아직 남아 있습니다: {stillDetail}", "ALARM");
                    return;
                }

                ExecuteEmergencyStop($"알람 리셋 후에도 서보 알람 유지: {stillDetail}");
                Host.ShowMessage(
                    $"드라이브 알람 리셋 후에도 알람이 남아 있어 다시 비상정지했습니다.\n\n{stillDetail}\n\n" +
                    "드라이브 표시창의 알람 코드를 확인하고 원인을 없앤 뒤 다시 알람 리셋을 하세요.\n" +
                    "원인을 모르면 드라이브 전원을 껐다 켜야 할 수 있습니다.",
                    "서보 알람 유지", MessageKind.Error);
            });
        }

        /// <summary>
        /// 비상정지 뒤 드라이브가 실제로 서보를 껐는지 상태워드로 확인합니다.
        /// </summary>
        private void VerifyServoOffAfterEmergencyStop()
        {
            if (EtherCAT_M == null || !EthercatConnected)
            {
                return;
            }

            bool axis1On;
            bool axis2On;
            try
            {
                axis1On = EtherCAT_M.IsAxisOperationEnabled(1);
                axis2On = EtherCAT_M.IsAxisOperationEnabled(2);
            }
            catch (Exception ex)
            {
                AddLogMessage($"비상정지 확인 실패: {ex.Message} - 서보가 꺼졌는지 눈으로 확인하세요", "ALARM");
                RecordAlarm(AlarmCodes.EmergencyStopVerifyFailed, "TM", ex.Message);
                return;
            }

            if (!axis1On && !axis2On)
            {
                AddLogMessage("비상정지 확인: 두 축 모두 서보 OFF (드라이브 상태워드)", "INFO");
                return;
            }

            string axes = axis1On && axis2On ? "Axis1(상하), Axis2(좌우)" : axis1On ? "Axis1(상하)" : "Axis2(좌우)";
            AddLogMessage($"비상정지 확인 실패: {axes} 서보가 아직 켜져 있습니다 - 비상 차단 스위치로 전원을 끊으세요", "ALARM");
            RecordAlarm(AlarmCodes.EmergencyStopVerifyFailed, "TM", $"{axes} 서보 ON으로 읽힘");
            Host.ShowMessage(
                $"비상정지 후에도 {axes} 서보가 켜져 있는 것으로 읽힙니다.\n\n" +
                "비상 차단 스위치로 장비 전원을 끊으세요.",
                "비상정지 확인 실패", MessageKind.Error);
        }

        public void SetProcessState(ProcessState state, string logMessage = null)
        {
            // 알람 발생 시 이전 상태 저장 (Error로 변경될 때만 저장)
            if (state == ProcessState.Error && CurrentProcessState != ProcessState.Error)
            {
                var currentState = CurrentProcessState;
                _stateBeforeAlarm = currentState;
                AddLogMessage($"알람 발생: 이전 상태({currentState}) 저장", "INFO");
            }
            
            CurrentProcessState = state;
            if (state != ProcessState.Running)
            {
                mainLampBlinkState = false;
            }

            switch (state)
            {
                case ProcessState.Running:
                    StatusProcessText = "공정 상태: 진행 중";
                    statusPressureText = "챔버 압력: 공정 압력 유지";
                    statusTemperatureText = "온도: 목표 온도 유지";
                    StatusDoorText = "문 상태: 인터락 유지";
                    mainLampBlinkState = true;
                    UpdateMainLampColors(LimeGreen, false, false);
                    break;
                case ProcessState.Paused:
                    StatusProcessText = "공정 상태: 일시 정지";
                    statusPressureText = "챔버 압력: 안정";
                    statusTemperatureText = "온도: 유지 모드";
                    StatusDoorText = "문 상태: 인터락 유지";
                    UpdateMainLampColors(null, true, false);
                    break;
                case ProcessState.Error:
                    StatusProcessText = "공정 상태: 경고 / 정지";
                    statusPressureText = "챔버 압력: 점검 필요";
                    statusTemperatureText = "온도: 점검 필요";
                    StatusDoorText = "문 상태: 확인 필요";
                    UpdateMainLampColors(null, false, true);
                    break;
                default:
                    StatusProcessText = "공정 상태: 대기";
                    statusPressureText = "챔버 압력: 안정";
                    statusTemperatureText = "온도: 정상 범위 유지";
                    StatusDoorText = "문 상태: 모두 닫힘";
                    UpdateMainLampColors(null, true, false);
                    break;
            }

            StatusProcessDetail = StatusProcessText;
            UpdateProcessControlButtons();
            Host.RefreshUi();

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                var level = state == ProcessState.Error ? "ALARM" : "INFO";
                AddLogMessage(logMessage, level);
            }
        }

        internal void SetWaferLoadState(MainFormViewModel.WaferLoadStateType newState, bool logChange = true, bool refreshUi = true)
        {
            if (WaferLoadState == newState)
            {
                // 같은 상태 재설정이어도 사용자가 수량을 바꿀 수 있으므로 재적용/리프레시 수행
                ApplyConfiguredFoupCountsFromState();
                if (refreshUi)
                {
                    UpdateSimulationUi();
                }
                return;
            }

            WaferLoadState = newState;
            ApplyConfiguredFoupCountsFromState();

            if (logChange)
            {
                string message;
                switch (newState)
                {
                    case MainFormViewModel.WaferLoadStateType.Loading:
                        message = "웨이퍼가 로딩 상태로 설정되었습니다.";
                        break;
                    case MainFormViewModel.WaferLoadStateType.Unloading:
                        message = "웨이퍼가 언로딩 상태로 설정되었습니다. (FOUP B 배출)";
                        break;
                    default:
                        message = "웨이퍼 상태가 초기화되었습니다.";
                        break;
                }

                AddLogMessage(message, "INFO");
            }

            if (refreshUi)
            {
                UpdateSimulationUi();
            }
        }

        internal void UnloadFoupB()
        {
            var unloadedCount = foupManager?.UnloadFoupB() ?? 0;
            if (unloadedCount > 0)
            {
                AddLogMessage($"FOUP B 언로딩: {unloadedCount}장 배출 처리", "INFO");
            }
            UpdateSimulationUi();
        }

        internal void SetRecipeParameters(int waferCount, int chamberADuration, int chamberBDuration, int chamberCDuration, bool enableSecondExposure = false)
        {
            // 레시피 검증 로직은 RecipeValidator로 이동됨
            var validationResult = RecipeValidator.ValidateRecipeParameters(waferCount, chamberADuration, chamberBDuration, chamberCDuration);
            
            CurrentRecipeWaferCount = validationResult.WaferCount;
            int durA = validationResult.ChamberADuration;
            int durB = validationResult.ChamberBDuration;
            int durC = validationResult.ChamberCDuration;
            
            // 공정 단계 시간 업데이트를 ChamberController로 위임
            chamberController?.UpdateStepDurations(durA, durB, durC);
            SecondExposureEnabled = enableSecondExposure;
            
            // 이미 FOUP A에 로딩된 웨이퍼들의 2차 노광 설정 업데이트
            if (foupManager?.HasWafersInFoupA() == true)
            {
                var queue = foupManager.FoupAQueue;
                foreach (var wafer in queue)
                {
                    wafer.RequiresSecondExposure = enableSecondExposure;
                }
                AddLogMessage($"FOUP A 웨이퍼 {queue.Count}장의 2차 노광 설정을 '{(enableSecondExposure ? "사용" : "미사용")}'으로 업데이트", "INFO");
            }
            
            // 0초로 입력된 경우 경고 로그
            if (validationResult.HasWarning)
            {
                AddLogMessage($"공정 시간이 {RecipeValidator.MinChamberDuration}초 미만으로 설정되어 최소값으로 조정되었습니다.", "WARNING");
            }
            
            AddLogMessage($"레시피 설정: 웨이퍼 {CurrentRecipeWaferCount}장, A:{durA}s, B:{durB}s, C:{durC}s, 2차 노광: {(SecondExposureEnabled ? "사용" : "미사용")}", "INFO");
        }
    }
}
