using System;
using SemiconductorUi.Services;
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
        // === TM 하드웨어 제어 관련 ===
        // TmHardwareController는 HardwareManager 속성으로 관리됨
        internal bool TmHardwareActionPending = false;           // 하드웨어 동작 대기 중
        internal int doorOpenConsecutiveChecks = 0;              // 도어 열림 연속 확인 횟수
        private const int REQUIRED_DOOR_OPEN_CHECKS = 1;        // (미사용 - 시간 기반으로 변경됨)
        // DOOR_OPEN_WAIT_TICKS, DOOR_CLOSE_WAIT_TICKS는 AppSettings에서 읽어옴
        internal DateTime tmHardwareActionStartTime;             // 하드웨어 동작 시작 시간
        private DateTime? pauseStartTime;                        // 일시정지 시작 시간 (타임아웃 계산 시 일시정지 시간 제외용)
        internal bool doorOpenCommandSent = false;               // 도어 열기 명령 전송 여부
        internal bool doorCloseCommandSent = false;              // 도어 닫기 명령 전송 여부
        internal int doorCloseWaitTicks = 0;                     // 도어 닫기 대기 틱 수
        internal DateTime doorWaitStartTime = DateTime.MinValue;   // 도어 열기/닫기 명령을 보낸 시각 (시간 기준 대기)
        // TM_HARDWARE_ACTION_TIMEOUT_MS, CYLINDER_ACTION_TIMEOUT_MS는 AppSettings에서 읽어옴
        // TmHardwareInitialized는 HardwareManager 속성으로 관리됨
        internal bool IsServoOn = false;                         // 서보 ON 상태 추적
        
        // === 안정화 대기 시간 (밀리초) ===
        // 속도 최적화: 안전을 유지하면서 불필요한 대기 시간 감소
        // 안정화 지연 시간은 AppSettings에서 읽어옴
        internal DateTime tmSettleStartTime;                     // 안정화 시작 시간
        internal bool TmSettleWaiting = false;                   // 안정화 대기 중
        internal int tmCurrentSettleDelay = 0;                   // 현재 안정화 대기 시간
        


        /// <summary>
        /// TM 하드웨어 동작 완료 확인 (PP_D + 시간 기반)
        /// X/Y 축 동시 이동이므로 더 긴 타임아웃 적용
        /// 중요: 이동 시작 직후에는 PP_D가 이전 상태로 남아있을 수 있으므로 최소 대기 시간 필요
        /// </summary>
        // 서보 XY 동시 이동 타임아웃 및 최소 대기 시간 (AppSettings에서 읽어옴)
        private static int SERVO_XY_MOVE_TIMEOUT_MS => AppSettings.ServoXyMoveTimeoutMs;  // X+Y 동시 이동 최대 시간
        private static int SERVO_MOVE_MIN_WAIT_MS => AppSettings.ServoMoveMinWaitMs;     // PP_D 체크 전 최소 대기 시간
        


        /// <summary>
        /// TM Axis1 이동 완료 확인 (PP_D + 시간 기반)
        /// 실제 서보는 2~3초 내에 이동 완료하지만 PP_D가 일시적으로만 true가 되는 문제 해결
        /// 중요: 이동 시작 직후에는 PP_D가 이전 상태로 남아있을 수 있으므로 최소 대기 시간 필요
        /// </summary>
        // 서보 이동 타임아웃 및 최소 대기 시간 (AppSettings에서 읽어옴)
        private static int SERVO_MOVE_TIMEOUT_MS => AppSettings.ServoMoveTimeoutMs;          // 서보 이동 최대 시간
        private static int SERVO_AXIS1_MIN_WAIT_MS => AppSettings.ServoAxis1MinWaitMs;         // Axis1 PP_D 체크 전 최소 대기 시간
        

        /// <summary>
        /// 원점복귀 시퀀스 실행 (상하 → 좌우 순서)
        /// 재시도 로직 포함 (최대 3회)
        /// </summary>
        /// <param name="maxRetries">최대 재시도 횟수 (기본값: 3)</param>
        internal void PerformHomingSequence(int maxRetries = 3)
        {
            int retryCount = 0;
            
            while (retryCount <= maxRetries)
            {
                try
                {
                    if (retryCount > 0)
                    {
                        Invoke((Action)(() => {
                            AddLogMessage($"원점복귀 재시도 ({retryCount}/{maxRetries})", "WARN");
                        }));
                        
                        // 재시도 전 대기 (1초)
                        System.Threading.Thread.Sleep(1000);
                        
                        // 재시도 전 상태 확인
                        if (!IsServoOn)
                        {
                            Invoke((Action)(() => {
                                AddLogMessage("원점복귀 재시도 실패: 서보가 OFF 상태입니다", "ERROR");
                                Host.ShowMessage(
                                    "원점복귀 재시도 중 서보가 OFF 상태가 되었습니다.\n\n" +
                                    "서보를 ON한 후 다시 시도해주세요.",
                                    "원점복귀 실패", MessageKind.Error);
                            }));
                            return;
                        }
                        
                        // 실린더 상태 확인
                        bool cylinderRetracted = EtherCAT_M.Digital_Input(12);
                        if (!cylinderRetracted)
                        {
                            Invoke((Action)(() => {
                                AddLogMessage("원점복귀 재시도 실패: 실린더가 전진 상태입니다", "ERROR");
                                Host.ShowMessage(
                                    "원점복귀 재시도 중 실린더가 전진 상태가 되었습니다.\n\n" +
                                    "실린더를 후진한 후 다시 시도해주세요.",
                                    "원점복귀 실패", MessageKind.Error);
                            }));
                            return;
                        }
                    }
                    
                    Invoke((Action)(() => {
                        if (retryCount == 0)
                        {
                            AddLogMessage("원점복귀 시작 - 1단계: 상하(Axis1) 원점복귀", "INFO");
                        }
                        SetServoStatus("서보: Homing(1/2)...", ServoStatusLevel.Warning);
                    }));

                    // 1단계: 상하(Axis1) 원점복귀
                    EtherCAT_M.Axis1_UD_Homming();

                    // Axis1 원점복귀 완료 대기 (최대 120초)
                    var timeout = DateTime.Now.AddSeconds(120);
                    while (DateTime.Now < timeout)
                    {
                        EtherCAT_M.ThrowIfEmergencyStopped();
                        if (EtherCAT_M.Axis1_Status("HOME_D"))
                        {
                            break;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    if (!EtherCAT_M.Axis1_Status("HOME_D"))
                    {
                        Invoke((Action)(() => {
                            AddLogMessage($"상하(Axis1) 원점복귀 타임아웃 (시도 {retryCount + 1}/{maxRetries + 1})", "ERROR");
                            UpdateServoStatusLabel();
                        }));
                        
                        retryCount++;
                        if (retryCount > maxRetries)
                        {
                            Invoke((Action)(() => {
                                AddLogMessage("원점복귀 최종 실패: Axis1 타임아웃 (재시도 횟수 초과)", "ERROR");
                                Host.ShowMessage(
                                    "원점복귀가 실패했습니다.\n\n" +
                                    "확인 사항:\n" +
                                    "1. 서보 모터 전원 확인\n" +
                                    "2. Axis1 하드웨어 상태 확인\n" +
                                    "3. EtherCAT 연결 상태 확인\n\n" +
                                    "수동으로 원점복귀를 다시 시도해주세요.",
                                    "원점복귀 실패", MessageKind.Error);
                                UpdateServoStatusLabel();
                            }));
                            return;
                        }
                        continue; // 재시도
                    }

                    Invoke((Action)(() => {
                        AddLogMessage("상하(Axis1) 원점복귀 완료 - 2단계: 좌우(Axis2) 원점복귀", "INFO");
                        SetServoStatus("서보: Homing(2/2)...", ServoStatusLevel.Warning);
                    }));

                    // 2단계: 좌우(Axis2) 원점복귀
                    EtherCAT_M.Axis2_LR_Homming();

                    // Axis2 원점복귀 완료 대기 (최대 120초)
                    timeout = DateTime.Now.AddSeconds(120);
                    while (DateTime.Now < timeout)
                    {
                        EtherCAT_M.ThrowIfEmergencyStopped();
                        if (EtherCAT_M.Axis2_Status("HOME_D"))
                        {
                            break;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    if (!EtherCAT_M.Axis2_Status("HOME_D"))
                    {
                        Invoke((Action)(() => {
                            AddLogMessage($"좌우(Axis2) 원점복귀 타임아웃 (시도 {retryCount + 1}/{maxRetries + 1})", "ERROR");
                            UpdateServoStatusLabel();
                        }));
                        
                        retryCount++;
                        if (retryCount > maxRetries)
                        {
                            Invoke((Action)(() => {
                                AddLogMessage("원점복귀 최종 실패: Axis2 타임아웃 (재시도 횟수 초과)", "ERROR");
                                Host.ShowMessage(
                                    "원점복귀가 실패했습니다.\n\n" +
                                    "확인 사항:\n" +
                                    "1. 서보 모터 전원 확인\n" +
                                    "2. Axis2 하드웨어 상태 확인\n" +
                                    "3. 실린더 후진 상태 확인\n" +
                                    "4. EtherCAT 연결 상태 확인\n\n" +
                                    "수동으로 원점복귀를 다시 시도해주세요.",
                                    "원점복귀 실패", MessageKind.Error);
                                UpdateServoStatusLabel();
                            }));
                            return;
                        }
                        continue; // 재시도 (Axis1부터 다시 시작)
                    }

                    // 원점복귀 완료
                    Invoke((Action)(() => {
                        if (retryCount > 0)
                        {
                            AddLogMessage($"원점복귀 완료 (재시도 {retryCount}회 후 성공)", "INFO");
                        }
                        else
                        {
                            AddLogMessage("원점복귀 완료 (상하 → 좌우 순서)", "INFO");
                        }
                        
                        // TM 하드웨어 초기화 상태 업데이트 (원점복귀 완료 = 초기화 완료)
                        TmHardwareInitialized = true;
                        
                        if (TmHardwareController != null)
                        {
                            TmHardwareController.MarkHomedAfterSuccessfulHoming();
                        }

                        UpdateServoStatusLabel();
                    }));
                    return; // 성공
                }
                catch (EmergencyStopActiveException ex)
                {
                    // 비상정지는 재시도하지 않고 바로 끝냅니다.
                    Invoke((Action)(() => {
                        AddLogMessage($"원점복귀 중단: {ex.Message}", "WARN");
                        UpdateServoStatusLabel();
                    }));
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount > maxRetries)
                    {
                        Invoke((Action)(() => {
                            AddLogMessage($"원점복귀 최종 오류: {ex.Message} (재시도 횟수 초과)", "ERROR");
                            Host.ShowMessage(
                                $"원점복귀 중 오류가 발생했습니다.\n\n" +
                                $"오류 내용: {ex.Message}\n\n" +
                                $"수동으로 원점복귀를 다시 시도해주세요.",
                                "원점복귀 오류", MessageKind.Error);
                            UpdateServoStatusLabel();
                        }));
                        return;
                    }
                    
                    Invoke((Action)(() => {
                        AddLogMessage($"원점복귀 오류: {ex.Message} (재시도 예정)", "WARN");
                    }));
                    // 재시도 계속
                }
            }
        }

        /// <summary>
        /// 공정 시작 시 자동으로 서보 ON + 원점복귀 수행.
        /// 백그라운드 스레드에서 호출 가능 (UI 로그는 AddLogMessage가 마샬링).
        /// </summary>
        private bool PerformAutoServoOnAndHoming(out string failReason)
        {
            failReason = null;
            try
            {
                // 1. 실린더 후진 상태 확인 (인터락)
                bool cylinderRetracted = EtherCAT_M.Digital_Input(12);
                if (!cylinderRetracted)
                {
                    failReason = "실린더가 후진 상태가 아닙니다. 먼저 실린더를 후진시켜 주세요.";
                    AddLogMessage("공정 시작 실패: 실린더가 후진 상태가 아닙니다", "ERROR");
                    return false;
                }

                // 2. 서보 상태 확인 (하드웨어에서 실제 상태 확인)
                // 서보가 이미 ON 상태면 OFF하지 않음 (불필요한 서보 재시작 방지)
                bool servoAlreadyOn = false;
                bool homingAlreadyDone = false;
                
                try
                {
                    // 하드웨어에서 실제 서보 상태 확인 (위치 데이터)
                    var axis1Pos = EtherCAT_M.Axis1_is_PosData();
                    var axis2Pos = EtherCAT_M.Axis2_is_PosData();
                    bool hasPosition = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" &&
                                      !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-";
                    
                    // 원점복귀 상태 확인
                    bool axis1Homed = EtherCAT_M.Axis1_Status("HOME_D");
                    bool axis2Homed = EtherCAT_M.Axis2_Status("HOME_D");
                    bool isHomed = axis1Homed && axis2Homed;
                    
                    if (hasPosition)
                    {
                        // 위치 데이터가 있으면 서보가 ON 상태
                        servoAlreadyOn = true;
                        IsServoOn = true; // 플래그 동기화
                        
                        if (isHomed)
                        {
                            // 원점복귀도 완료된 상태
                            homingAlreadyDone = true;
                            TmHardwareInitialized = true; // 플래그 동기화
                            AddLogMessage("서보가 이미 ON 상태이고 원점복귀 완료됨 - 서보 상태 유지, 원점복귀 스킵", "INFO");
                        }
                        else
                        {
                            // 서보는 ON이지만 원점복귀 미완료
                            AddLogMessage("서보가 이미 ON 상태 - 원점복귀만 수행", "INFO");
                        }
                    }
                    else
                    {
                        // 위치 데이터가 없으면 서보가 OFF 상태
                        servoAlreadyOn = false;
                        IsServoOn = false; // 플래그 동기화
                        AddLogMessage("서보가 OFF 상태 - 서보 ON 및 원점복귀 수행", "INFO");
                    }
                }
                catch (Exception ex)
                {
                    // 하드웨어 상태 확인 실패 시 안전하게 OFF 후 재시작
                    AddLogMessage($"서보 상태 확인 오류: {ex.Message} - 서보 초기화 수행", "WARN");
                    servoAlreadyOn = false;
                }
                
                if (!servoAlreadyOn)
                {
                    // 서보 OFF로 초기화 (불안정 상태 방지)
                    AddLogMessage("서보 모터 초기화 중...", "INFO");
                    try
                    {
                        EtherCAT_M.Axis1_OFF();
                        EtherCAT_M.Axis2_OFF();
                        System.Threading.Thread.Sleep(100);
                    }
                    catch (Exception ex)
                    {
                        AddLogMessage($"서보 OFF 오류 (무시): {ex.Message}", "WARN");
                    }
                }

                // 3. 속도 파라미터 설정
                // 원점복귀 서보 파라미터 설정 (AppSettings에서 읽어옴)
                long velocity = AppSettings.ServoHomingVelocity;
                long maxVelocity = AppSettings.ServoHomingMaxVelocity;
                long deceleration = AppSettings.ServoDefaultDeceleration;
                long acceleration = AppSettings.ServoDefaultAcceleration;

                EtherCAT_M.Axis1_UD_Config_Update(velocity, maxVelocity, deceleration, acceleration);
                EtherCAT_M.Axis2_LR_Config_Update(velocity, maxVelocity, deceleration, acceleration);

                // 4. 서보 ON (이미 ON 상태가 아닌 경우에만)
                if (!servoAlreadyOn)
                {
                    AddLogMessage("서보 모터 ON", "INFO");
                    EtherCAT_M.Axis1_ON();
                    EtherCAT_M.Axis2_ON();
                    System.Threading.Thread.Sleep(500); // 서보 안정화 대기
                }
                else
                {
                    // 이미 ON 상태이면 원점복귀만 수행
                    AddLogMessage("서보가 이미 ON 상태 - 원점복귀만 수행", "INFO");
                }
                
                // 서보 ON 상태 확인 (이미 ON 상태가 아닌 경우에만 확인)
                if (!servoAlreadyOn)
                {
                    bool servoOnConfirmed = false;
                    var servoCheckTimeout = DateTime.Now.AddSeconds(2);
                    while (DateTime.Now < servoCheckTimeout)
                    {
                        EtherCAT_M.ThrowIfEmergencyStopped();
                        try
                        {
                            // 위치 데이터가 읽히면 서보가 ON 상태로 간주
                            var axis1Pos = EtherCAT_M.Axis1_is_PosData();
                            var axis2Pos = EtherCAT_M.Axis2_is_PosData();
                            if (!string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" 
                                && !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-")
                            {
                                servoOnConfirmed = true;
                                break;
                            }
                        }
                        catch { }
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    if (!servoOnConfirmed)
                    {
                        failReason = "서보 ON 확인 실패: 위치 데이터를 읽을 수 없습니다.";
                        AddLogMessage("서보 ON 확인 실패: 위치 데이터를 읽을 수 없습니다", "ERROR");
                        IsServoOn = false; // 플래그 동기화
                        return false;
                    }
                    
                    IsServoOn = true;  // 서보 ON 상태 플래그 설정
                    AddLogMessage("서보모터 ON - 파라미터 설정 완료", "INFO");
                }
                else
                {
                    // 이미 ON 상태이면 확인 스킵
                    AddLogMessage("서보가 이미 ON 상태 - 확인 스킵", "INFO");
                }

                // 5. TmHardwareController 서보 상태 동기화 (서보 이동 시 필요)
                if (TmHardwareController != null)
                {
                    TmHardwareController.SyncServoStatus();
                    AddLogMessage("TmHardwareController 서보 상태 동기화 완료", "INFO");
                }
                
                // 6. 원점복귀 수행 (이미 완료된 경우 스킵)
                if (homingAlreadyDone)
                {
                    // 이미 원점복귀가 완료된 상태면 스킵
                    AddLogMessage("원점복귀가 이미 완료됨 - 스킵", "INFO");
                    UpdateServoStatusLabel(); // UI 업데이트
                    // TmHardwareInitialized가 이미 true로 설정되어 있으므로 바로 반환
                    return true;
                }
                
                // 원점복귀가 필요하면 수행 (서보는 이미 ON 상태이거나 방금 ON함)
                
                // 원점복귀 수행 (완료 대기)
                AddLogMessage("공정 시작 원점복귀 - 1단계: 상하(Axis1)", "INFO");
                EtherCAT_M.Axis1_UD_Homming();
                
                // Axis1 원점복귀 완료 대기 (최대 120초)
                var timeout = DateTime.Now.AddSeconds(120);
                while (DateTime.Now < timeout)
                {
                    EtherCAT_M.ThrowIfEmergencyStopped();
                    if (EtherCAT_M.Axis1_Status("HOME_D"))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                
                if (!EtherCAT_M.Axis1_Status("HOME_D"))
                {
                    failReason = "원점복귀 실패: Axis1 타임아웃 (120초 초과)";
                    AddLogMessage("공정 시작 원점복귀 실패: Axis1 타임아웃 (120초 초과)", "ERROR");
                    TmHardwareInitialized = false;
                    IsServoOn = false; // 타임아웃 시 서보 상태도 초기화
                    return false;
                }
                
                AddLogMessage("공정 시작 원점복귀 - 2단계: 좌우(Axis2)", "INFO");
                EtherCAT_M.Axis2_LR_Homming();
                
                // Axis2 원점복귀 완료 대기 (최대 120초)
                timeout = DateTime.Now.AddSeconds(120);
                while (DateTime.Now < timeout)
                {
                    EtherCAT_M.ThrowIfEmergencyStopped();
                    if (EtherCAT_M.Axis2_Status("HOME_D"))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                
                if (!EtherCAT_M.Axis2_Status("HOME_D"))
                {
                    failReason = "원점복귀 실패: Axis2 타임아웃 (120초 초과)";
                    AddLogMessage("공정 시작 원점복귀 실패: Axis2 타임아웃 (120초 초과)", "ERROR");
                    TmHardwareInitialized = false;
                    IsServoOn = false; // 타임아웃 시 서보 상태도 초기화
                    return false;
                }
                
                // 원점복귀 완료
                TmHardwareInitialized = true;
                IsServoOn = true; // 서보 ON 상태 확실히 설정
                
                // TmHardwareController 서보/원점 상태 동기화 (이동 인터락 HasEverHomed)
                if (TmHardwareController != null)
                {
                    TmHardwareController.MarkHomedAfterSuccessfulHoming();
                    TmHardwareController.SyncServoStatus();
                    AddLogMessage("TmHardwareController 서보/원점 상태 동기화 완료", "INFO");
                }
                
                AddLogMessage("서보 ON + 원점복귀 완료 (상하 → 좌우 순서)", "INFO");
                UpdateServoStatusLabel(); // UI 업데이트 (Home 상태 표시)
                return true;
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                AddLogMessage($"서보 ON + 원점복귀 오류: {ex.Message}", "ERROR");
                return false;
            }
        }

        /// <summary>
        /// 공정 시작 전 원점복귀 수행
        /// 동기식으로 수행하여 원점복귀 완료 후 공정이 시작되도록 함
        /// </summary>
        private void PerformStartupHoming()
        {
            try
            {
                // 실린더 후진 상태 확인 (인터락)
                bool cylinderRetracted = EtherCAT_M.Digital_Input(12); // 실린더 후진 센서
                if (!cylinderRetracted)
                {
                    AddLogMessage("공정 시작 실패: 실린더가 후진 상태가 아닙니다", "ERROR");
                    Host.ShowMessage("실린더가 후진 상태가 아닙니다.\n먼저 실린더를 후진시켜 주세요.", "인터락", MessageKind.Warning);
                    return;
                }

                AddLogMessage("공정 시작 원점복귀 - 1단계: 상하(Axis1)", "INFO");

                // 1단계: Axis1 (상하) 원점복귀
                EtherCAT_M.Axis1_UD_Homming();

                // Axis1 원점복귀 완료 대기 (최대 120초)
                var timeout = DateTime.Now.AddSeconds(120);
                while (DateTime.Now < timeout)
                {
                    EtherCAT_M.ThrowIfEmergencyStopped();
                    if (EtherCAT_M.Axis1_Status("HOME_D"))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(100);
                }

                if (!EtherCAT_M.Axis1_Status("HOME_D"))
                {
                    AddLogMessage("공정 시작 원점복귀 실패: Axis1 타임아웃", "ERROR");
                    return;
                }

                AddLogMessage("공정 시작 원점복귀 - 2단계: 좌우(Axis2)", "INFO");

                // 2단계: Axis2 (좌우) 원점복귀
                EtherCAT_M.Axis2_LR_Homming();

                // Axis2 원점복귀 완료 대기 (최대 120초)
                timeout = DateTime.Now.AddSeconds(120);
                while (DateTime.Now < timeout)
                {
                    EtherCAT_M.ThrowIfEmergencyStopped();
                    if (EtherCAT_M.Axis2_Status("HOME_D"))
                    {
                        TmHardwareInitialized = true;
                        AddLogMessage("공정 시작 원점복귀 완료", "INFO");
                        UpdateServoStatusLabel();
                        return;
                    }
                    System.Threading.Thread.Sleep(100);
                }

                AddLogMessage("공정 시작 원점복귀 실패: Axis2 타임아웃", "ERROR");
            }
            catch (Exception ex)
            {
                AddLogMessage($"공정 시작 원점복귀 오류: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// 공정 종료 후 원점복귀 수행
        /// 비동기로 수행하여 UI가 멈추지 않도록 함
        /// </summary>
        internal void PerformShutdownHoming()
        {
            HardwareOperation.Run(EtherCAT_M, () =>
            {
                try
                {
                    // 실린더 후진 상태 확인
                    bool cylinderRetracted = EtherCAT_M.Digital_Input(12);
                    if (!cylinderRetracted)
                    {
                        Invoke((Action)(() => {
                            AddLogMessage("공정 종료 원점복귀 실패: 실린더가 후진 상태가 아닙니다", "WARN");
                        }));
                        return;
                    }

                    Invoke((Action)(() => {
                        AddLogMessage("공정 종료 원점복귀 - 1단계: 상하(Axis1)", "INFO");
                    }));

                    // 1단계: Axis1 (상하) 원점복귀
                    EtherCAT_M.Axis1_UD_Homming();

                    var timeout = DateTime.Now.AddSeconds(120);
                    while (DateTime.Now < timeout)
                    {
                        EtherCAT_M.ThrowIfEmergencyStopped();
                        if (EtherCAT_M.Axis1_Status("HOME_D"))
                        {
                            break;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    if (!EtherCAT_M.Axis1_Status("HOME_D"))
                    {
                        Invoke((Action)(() => {
                            AddLogMessage("공정 종료 원점복귀 실패: Axis1 타임아웃", "WARN");
                        }));
                        return;
                    }

                    Invoke((Action)(() => {
                        AddLogMessage("공정 종료 원점복귀 - 2단계: 좌우(Axis2)", "INFO");
                    }));

                    // 2단계: Axis2 (좌우) 원점복귀
                    EtherCAT_M.Axis2_LR_Homming();

                    timeout = DateTime.Now.AddSeconds(120);
                    while (DateTime.Now < timeout)
                    {
                        EtherCAT_M.ThrowIfEmergencyStopped();
                        if (EtherCAT_M.Axis2_Status("HOME_D"))
                        {
                            Invoke((Action)(() => {
                                // TM 하드웨어 초기화 상태 업데이트
                                TmHardwareInitialized = true;
                                AddLogMessage("공정 종료 원점복귀 완료 - TM이 원점으로 복귀했습니다", "INFO");
                                UpdateServoStatusLabel();
                            }));
                            return;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    Invoke((Action)(() => {
                        AddLogMessage("공정 종료 원점복귀 실패: Axis2 타임아웃", "WARN");
                    }));
                }
                catch (Exception ex)
                {
                    Invoke((Action)(() => {
                        AddLogMessage($"공정 종료 원점복귀 오류: {ex.Message}", "WARN");
                    }));
                }
            });
        }

        /// <summary>
        /// 서보 상태 라벨 업데이트
        /// </summary>
        public void UpdateServoStatusLabel()
        {
            if (!Host.CheckAccess())
            {
                Host.BeginInvoke(() => UpdateServoStatusLabel());
                return;
            }
            RefreshServoStatusText();
        }

        /// <summary>
        /// 실제 서보 위치를 읽어서 TM 시각화 업데이트
        /// </summary>
        internal void UpdateTmVisualizationFromHardware()
        {
            UpdateTmVisualizationFromHardwareCore();
        }

        /// <summary>
        /// TM 하드웨어 모드 사용 가능 여부 확인
        /// </summary>
        /// <summary>
        /// 하드웨어 모드 사용 가능 여부 확인
        /// 중요: EtherCAT 연결 상태만으로 판단 (원점복귀 여부와 무관)
        /// </summary>
        internal bool IsTmHardwareModeAvailable()
        {
            return EthercatConnected && EtherCAT_M != null;
        }

        /// <summary>
        /// TM 하드웨어 동작 시작 (비동기 방식)
        /// </summary>
        internal void StartTmHardwareAction()
        {
            TmHardwareActionPending = true;
            tmHardwareActionStartTime = DateTime.Now;
            _tmInPositionSince = null;
        }

        // 위치 기반 완료 판정: 허용 오차 안에 처음 들어온 시각 (벗어나면 null)
        private DateTime? _tmInPositionSince;

        /// <summary>
        /// 공정 루프 이동 완료 보조 판정: 현재 위치가 마지막 명령 목표의 허용 오차 안에
        /// ServoInPositionHoldMs 이상 연속으로 머물렀는지 확인.
        /// 실제 장비 로그(2026-11-10)에서 이동은 1초 안팎에 끝나지만 Z가 목표를 수백 펄스 지나친 뒤
        /// 천천히 되돌아와 PP_D가 7~9.5초 뒤에 켜지는 문제가 있어 추가함 (원본 bcd72ee).
        /// TmHardwareController.WaitForServoMoveComplete(수동/티칭 경로)도 ±1000 펄스로 완료 판정함.
        /// </summary>
        private bool IsTmHeldAtCommandedTarget(bool includeAxis2)
        {
            var ctrl = TmHardwareController;
            if (ctrl == null || ctrl.CommandedAxis1Target == null) return false;
            if (includeAxis2 && ctrl.CommandedAxis2Target == null) return false;

            int tolAxis1 = AppSettings.ServoInPositionToleranceAxis1;
            int tolAxis2 = AppSettings.ServoInPositionToleranceAxis2;
            if (tolAxis1 <= 0 || (includeAxis2 && tolAxis2 <= 0)) return false;

            ctrl.UpdateCurrentPositions();
            long diffAxis1 = Math.Abs(ctrl.CurrentAxis1Position - ctrl.CommandedAxis1Target.Value);
            long diffAxis2 = includeAxis2 ? Math.Abs(ctrl.CurrentAxis2Position - ctrl.CommandedAxis2Target.Value) : 0;

            if (diffAxis1 > tolAxis1 || diffAxis2 > tolAxis2)
            {
                _tmInPositionSince = null;
                return false;
            }

            var now = DateTime.Now;
            if (_tmInPositionSince == null)
            {
                _tmInPositionSince = now;
                return false;
            }
            if ((now - _tmInPositionSince.Value).TotalMilliseconds < AppSettings.ServoInPositionHoldMs)
            {
                return false;
            }

            double elapsedMs = (now - tmHardwareActionStartTime).TotalMilliseconds;
            AddLogMessage(includeAxis2
                ? $"서보 위치 도달 (허용범위, PP_D 전, {elapsedMs:F0}ms): X={ctrl.CurrentAxis2Position} (목표 {ctrl.CommandedAxis2Target}, 차 {diffAxis2}), Y={ctrl.CurrentAxis1Position} (목표 {ctrl.CommandedAxis1Target}, 차 {diffAxis1})"
                : $"서보 Axis1 위치 도달 (허용범위, PP_D 전, {elapsedMs:F0}ms): Y={ctrl.CurrentAxis1Position} (목표 {ctrl.CommandedAxis1Target}, 차 {diffAxis1})",
                "INFO");
            return true;
        }

        internal bool CheckTmHardwareActionComplete()
        {
            if (TmHardwareController == null) return true;
            
            // 서보 이동 완료 확인 (PP_D 상태)
            try
            {
                double elapsedMs = 0;
                if (TmHardwareActionPending)
                {
                    elapsedMs = (DateTime.Now - tmHardwareActionStartTime).TotalMilliseconds;
                    
                    // 중요: 이동 시작 후 최소 대기 시간이 지나야 PP_D 체크 시작
                    // 이전 이동의 PP_D=true 상태가 새 이동을 즉시 완료로 판정하는 문제 방지
                    if (elapsedMs < SERVO_MOVE_MIN_WAIT_MS)
                    {
                        return false;  // 최소 대기 시간 미경과 - 아직 이동 중으로 간주
                    }
                }
                
                bool axis1Done = EtherCAT_M.Axis1_Status("PP_D");
                bool axis2Done = EtherCAT_M.Axis2_Status("PP_D");
                
                // PP_D가 둘 다 true면 완료 (최소 대기 시간 이후에만 체크)
                if (axis1Done && axis2Done)
                {
                    return true;
                }

                // PP_D가 늦게 켜지는 경우: 목표 위치 허용 오차 안에 일정 시간 머물면 완료
                if (TmHardwareActionPending && IsTmHeldAtCommandedTarget(includeAxis2: true))
                {
                    return true;
                }
                
                    // 타임아웃: 완료로 위장하지 않음 → 상위 CheckTmHardwareTimeout이 Abort 처리
                    if (TmHardwareActionPending && elapsedMs >= SERVO_XY_MOVE_TIMEOUT_MS)
                    {
                        if (TmHardwareController != null)
                        {
                            TmHardwareController.UpdateCurrentPositions();
                            long currentX = TmHardwareController.CurrentAxis2Position;
                            long currentY = TmHardwareController.CurrentAxis1Position;
                            AddLogMessage($"서보 XY 이동 미완료 ({elapsedMs:F0}ms) - 현재 위치: X={currentX}, Y={currentY} (완료 대기/타임아웃 처리)", "WARN");
                        }
                        else
                        {
                            AddLogMessage($"서보 XY 이동 미완료 ({elapsedMs:F0}ms)", "WARN");
                        }
                        return false;
                    }
                    
                    return false;
            }
            catch (Exception ex)
            {
                AddLogMessage($"PP_D 상태 확인 오류: {ex.Message}", "WARN");
                return false;  // 오류 시 완료가 아닌 대기로 처리 (안전)
            }
        }

        /// <summary>
        /// TM 하드웨어 동작 타임아웃 체크
        /// </summary>
        internal bool CheckTmHardwareTimeout()
        {
            if (!TmHardwareActionPending) return false;
            return (DateTime.Now - tmHardwareActionStartTime).TotalMilliseconds > AppSettings.TmHardwareActionTimeoutMs;
        }

        /// <summary>
        /// 하드웨어 오류 발생 시 자동 일시정지 (안전장치)
        /// </summary>
        internal void HandleHardwareError(string errorMessage, string errorType = "ERROR")
        {
            if (!IsTmHardwareModeAvailable()) return;

            AddLogMessage($"[하드웨어 오류] {errorMessage} - 자동 일시정지", errorType);
            RecordAlarm(AlarmCodes.TmHardwareError, "TM", errorMessage);
            
            // 자동 일시정지
            if (SimulationRunning && !SimulationPaused)
            {
                PauseSimulation();
                AddLogMessage("하드웨어 오류로 인한 자동 일시정지", "WARN");
                
                // 사용자에게 알림
                if (!Host.CheckAccess())
                {
                    Invoke(new Action(() =>
                    {
                        Host.ShowMessage(
                            $"하드웨어 오류가 발생하여 공정이 일시정지되었습니다.\n\n" +
                            $"오류 내용: {errorMessage}\n\n" +
                            $"확인 사항:\n" +
                            $"1. EtherCAT 연결 상태 확인\n" +
                            $"2. 서보 모터 상태 확인\n" +
                            $"3. 실린더 상태 확인\n" +
                            $"4. 장비 상태 점검\n\n" +
                            $"문제 해결 후 '공정 재개' 버튼을 눌러주세요.",
                            "하드웨어 오류 - 자동 일시정지",
                            MessageKind.Warning);
                    }));
                }
                else
                {
                    Host.ShowMessage(
                        $"하드웨어 오류가 발생하여 공정이 일시정지되었습니다.\n\n" +
                        $"오류 내용: {errorMessage}\n\n" +
                        $"확인 사항:\n" +
                        $"1. EtherCAT 연결 상태 확인\n" +
                        $"2. 서보 모터 상태 확인\n" +
                        $"3. 실린더 상태 확인\n" +
                        $"4. 장비 상태 점검\n\n" +
                        $"문제 해결 후 '공정 재개' 버튼을 눌러주세요.",
                        "하드웨어 오류 - 자동 일시정지",
                        MessageKind.Warning);
                }
            }
        }

        /// <summary>
        /// Phase별 안정화 대기 시간 반환
        /// </summary>
        internal int GetSettleDelayForPhase(TransferController.TmPhase phase)
        {
            switch (phase)
            {
                // 서보 이동 완료 후
                case TransferController.TmPhase.MoveToPickup_WaitHardware:
                case TransferController.TmPhase.MoveToDropoff_WaitHardware:
                case TransferController.TmPhase.PickupExtend_ServoDown:
                case TransferController.TmPhase.PickupRetract_ServoUp:
                case TransferController.TmPhase.DropoffExtend_ServoDown:
                case TransferController.TmPhase.DropoffRetract_ServoUp:
                    return AppSettings.ServoSettleDelayMs;
                
                // 실린더 동작 완료 후
                case TransferController.TmPhase.PickupExtend_CylinderForward:
                case TransferController.TmPhase.PickupRetract_CylinderBackward:
                case TransferController.TmPhase.DropoffExtend_CylinderForward:
                case TransferController.TmPhase.DropoffRetract_CylinderBackward:
                    return AppSettings.CylinderSettleDelayMs;
                
                // 진공 ON 후
                case TransferController.TmPhase.PickupExtend_VacuumOn:
                    return AppSettings.VacuumOnSettleDelayMs;
                
                // 진공 OFF/배기 후
                case TransferController.TmPhase.DropoffExtend_VacuumOffExhaust:
                    return AppSettings.VacuumOffSettleDelayMs;
                
                // 도어 동작 완료 후
                case TransferController.TmPhase.WaitDoorPickupOpen:
                case TransferController.TmPhase.WaitDoorPickupClose:
                case TransferController.TmPhase.WaitDoorDropoffOpen:
                case TransferController.TmPhase.WaitDoorDropoffClose:
                    return AppSettings.DoorSettleDelayMs;
                
                default:
                    return 0;
            }
        }

        /// <summary>
        /// TM 서보 이동 명령 발행 - 픽업 위치 (안착 높이)
        /// 웨이퍼를 꺼내기 위해 안착 위치로 이동
        /// </summary>
        internal bool StartTmServoMoveForPickup(EquipmentRegion targetRegion)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                // FOUP 이동 시 현재 층 설정
                UpdateTmFoupSlotForRegion(targetRegion);

                var result = TmHardwareController.MoveToRegionForPickup(targetRegion, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage($"TM 서보 이동 시작 (픽업): {targetRegion} → 안착 높이", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 이동 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 이동 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 서보 이동 명령 발행 - 드롭 위치 (상승 높이)
        /// 웨이퍼를 놓기 위해 상승 위치로 이동
        /// </summary>
        internal bool StartTmServoMoveForDropoff(EquipmentRegion targetRegion)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                // FOUP 이동 시 현재 층 설정
                UpdateTmFoupSlotForRegion(targetRegion);

                var result = TmHardwareController.MoveToRegionForDropoff(targetRegion, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage($"TM 서보 이동 시작 (드롭): {targetRegion} → 상승 높이", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 이동 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 이동 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// FOUP 이동 시 현재 층 설정
        /// </summary>
        private void UpdateTmFoupSlotForRegion(EquipmentRegion region)
        {
            // 원본 Form1UiUpdater.UpdateTmFoupSlotForRegion
            if (TmHardwareController == null) return;

            if (region == EquipmentRegion.FoupA)
            {
                int slotIndex = ConfiguredFoupALoadCount - FoupARemainingInventoryCount;
                slotIndex = Math.Max(0, Math.Min(4, slotIndex)); // 0~4 범위로 제한
                TmHardwareController.CurrentFoupASlot = slotIndex + 1; // 1~5로 변환
                AddLogMessage($"FOUP A 현재 층: {TmHardwareController.CurrentFoupASlot}층", "INFO");
            }
            else if (region == EquipmentRegion.FoupB)
            {
                int slotIndex = foupManager?.GetFoupBCount() ?? 0;
                slotIndex = Math.Max(0, Math.Min(4, slotIndex)); // 0~4 범위로 제한
                TmHardwareController.CurrentFoupBSlot = slotIndex + 1;
                AddLogMessage($"FOUP B 현재 층: {TmHardwareController.CurrentFoupBSlot}층", "INFO");
            }
        }

        /// <summary>
        /// TM 실린더 전진 명령 발행 (비동기)
        /// </summary>
        internal bool StartTmCylinderExtend()
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                // 실린더 전진 명령
                EtherCAT_M.Digital_Output(13, false);
                EtherCAT_M.Digital_Output(12, true);
                StartTmHardwareAction();
                AddLogMessage("TM 실린더 전진 시작", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 실린더 전진 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 실린더 후진 명령 발행 (비동기)
        /// </summary>
        internal bool StartTmCylinderRetract()
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                // 실린더 후진 명령
                EtherCAT_M.Digital_Output(12, false);
                EtherCAT_M.Digital_Output(13, true);
                StartTmHardwareAction();
                AddLogMessage("TM 실린더 후진 시작", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 실린더 후진 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 실린더 전진 완료 확인
        /// </summary>
        internal bool CheckTmCylinderExtended()
        {
            if (!IsTmHardwareModeAvailable()) return true;

            try
            {
                return EtherCAT_M.Digital_Input(13); // 전진 센서
            }
            catch (Exception ex)
            {
                AddLogMessage($"실린더 전진 센서 확인 오류: {ex.Message}", "WARN");
                return false; // fail-closed: 센서 불명이면 미완료
            }
        }

        /// <summary>
        /// TM 실린더 후진 완료 확인
        /// </summary>
        internal bool CheckTmCylinderRetracted()
        {
            if (!IsTmHardwareModeAvailable()) return true;

            try
            {
                return EtherCAT_M.Digital_Input(12); // 후진 센서
            }
            catch (Exception ex)
            {
                AddLogMessage($"실린더 후진 센서 확인 오류: {ex.Message}", "WARN");
                return false; // fail-closed
            }
        }

        /// <summary>
        /// TM 서보 Axis1 하강 (웨이퍼 픽업/드롭 위치로)
        /// </summary>
        internal bool StartTmServoDown(EquipmentRegion region)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                var targetY = TmHardwareController.GetRegionLandHeight(region);
                var result = TmHardwareController.MoveAxis1(targetY, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage($"TM 서보 하강 시작: {region}", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 하강 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 하강 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 서보 Axis1 상승 (해당 Region의 상승 높이로)
        /// </summary>
        internal bool StartTmServoUp(EquipmentRegion region)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                var targetY = TmHardwareController.GetRegionRaiseHeight(region);
                var result = TmHardwareController.MoveAxis1(targetY, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage("TM 서보 상승 시작", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 상승 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 상승 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 서보 하강 (하강 위치로 이동) - 웨이퍼 아래로
        /// 드롭 후 실린더 후진 전에 호출
        /// </summary>
        internal bool StartTmServoToDescend(EquipmentRegion region)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                var targetY = TmHardwareController.GetRegionDescendHeight(region);
                var result = TmHardwareController.MoveAxis1(targetY, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage($"TM 서보 하강 시작 (하강 위치): {region}", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 하강 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 하강 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 서보 상승 (안착 위치로 이동) - 픽업 시 웨이퍼와 접촉
        /// 픽업 시 하강 위치에서 안착 위치로 이동
        /// </summary>
        internal bool StartTmServoToLand(EquipmentRegion region)
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                var targetY = TmHardwareController.GetRegionLandHeight(region);
                var result = TmHardwareController.MoveAxis1(targetY, waitForCompletion: false);
                if (result.Success)
                {
                    // 서보 이동 시 home 상태 해제
                    if (TmHardwareInitialized)
                    {
                        TmHardwareInitialized = false;
                        UpdateServoStatusLabel();
                    }
                    StartTmHardwareAction();
                    AddLogMessage($"TM 서보 이동 시작 (안착 위치): {region}", "INFO");
                    return true;
                }
                else
                {
                    HandleHardwareError($"TM 서보 이동 실패: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 서보 이동 오류: {ex.Message}");
                return false;
            }
        }

        internal bool CheckTmAxis1MoveComplete()
        {
            if (!IsTmHardwareModeAvailable()) return true;

            try
            {
                double elapsedMs = 0;
                if (TmHardwareActionPending)
                {
                    elapsedMs = (DateTime.Now - tmHardwareActionStartTime).TotalMilliseconds;
                    
                    // 중요: 이동 시작 후 최소 대기 시간이 지나야 PP_D 체크 시작
                    if (elapsedMs < SERVO_AXIS1_MIN_WAIT_MS)
                    {
                        return false;  // 최소 대기 시간 미경과 - 아직 이동 중으로 간주
                    }
                }
                
                // PP_D가 true면 완료 (최소 대기 시간 이후에만 체크)
                if (EtherCAT_M.Axis1_Status("PP_D"))
                {
                    return true;
                }

                // PP_D가 늦게 켜지는 경우: 목표 위치 허용 오차 안에 일정 시간 머물면 완료
                if (TmHardwareActionPending && IsTmHeldAtCommandedTarget(includeAxis2: false))
                {
                    return true;
                }
                
                // 시간 기반: soft timeout은 완료로 위장하지 않음 (하드 타임아웃이 Abort)
                if (TmHardwareActionPending && elapsedMs >= SERVO_MOVE_TIMEOUT_MS)
                {
                    AddLogMessage($"서보 Axis1 이동 미완료 ({elapsedMs:F0}ms) - PP_D 대기 중", "WARN");
                    return false;
                }
                
                return false;
            }
            catch
            {
                return false;  // 오류 시 대기 (안전)
            }
        }

        /// <summary>
        /// 이송 Abort 후 하드웨어 안전 자세 시도 (진공 OFF, 실린더 후진)
        /// </summary>
        internal void TrySafeStopTmHardwareAfterAbort()
        {
            if (!IsTmHardwareModeAvailable() || EtherCAT_M == null)
            {
                return;
            }

            try
            {
                EtherCAT_M.Digital_Output(14, false); // 진공 OFF
                EtherCAT_M.Digital_Output(15, false); // 배기 OFF
                TmHardwareController?.SyncVacuumState(false);
                AddLogMessage("Abort 안전정지: 진공/배기 OFF", "WARN");
            }
            catch (Exception ex)
            {
                AddLogMessage($"Abort 안전정지 진공 OFF 실패: {ex.Message}", "ERROR");
            }

            try
            {
                // 실린더가 전진 중이면 후진 명령 (완료 대기는 Abort 경로에서 하지 않음)
                bool retracted = false;
                try { retracted = EtherCAT_M.Digital_Input(12); } catch { }
                if (!retracted)
                {
                    EtherCAT_M.Digital_Output(12, false);
                    EtherCAT_M.Digital_Output(13, true);
                    AddLogMessage("Abort 안전정지: 실린더 후진 명령", "WARN");
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"Abort 안전정지 실린더 후진 실패: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// TM 진공 ON (웨이퍼 흡착)
        /// </summary>
        internal bool SetTmVacuumOn()
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                EtherCAT_M.Digital_Output(15, false); // 배기 OFF
                EtherCAT_M.Digital_Output(14, true);  // 흡기 ON
                TmHardwareController?.SyncVacuumState(true);
                AddLogMessage("TM 진공 흡기 ON", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 진공 ON 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 진공 OFF + 배기 (웨이퍼 놓기)
        /// </summary>
        internal bool SetTmVacuumOffAndExhaust()
        {
            if (!IsTmHardwareModeAvailable()) return false;

            try
            {
                EtherCAT_M.Digital_Output(14, false); // 흡기 OFF
                EtherCAT_M.Digital_Output(15, true);  // 배기 ON
                TmHardwareController?.SyncVacuumState(false);
                StartTmHardwareAction();
                AddLogMessage("TM 진공 OFF + 배기 시작", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                HandleHardwareError($"TM 진공 OFF 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// TM 배기 완료 처리 (배기 OFF)
        /// </summary>
        internal void CompleteTmExhaust()
        {
            if (!IsTmHardwareModeAvailable()) return;

            try
            {
                EtherCAT_M.Digital_Output(15, false); // 배기 OFF
                AddLogMessage("TM 배기 완료", "INFO");
            }
            catch (Exception ex)
            {
                AddLogMessage($"TM 배기 완료 오류: {ex.Message}", "WARN");
            }
        }
    }
}
