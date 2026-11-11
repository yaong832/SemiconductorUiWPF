using System;
using SemiconductorUi.Models;
using System.Configuration;


namespace SemiconductorUi
{
    /// <summary>
    /// 애플리케이션 설정 관리 클래스
    /// App.config에서 설정값을 읽어옴
    /// </summary>
    public static class AppSettings
    {
        #region 기본 설정

        /// <summary>
        /// 기본 사용자명
        /// </summary>
        public static string DefaultUsername => GetSetting("DefaultUsername", "admin");

        /// <summary>
        /// 기본 비밀번호 (사용 안 함 - 보안상 제거 권장)
        /// </summary>
        [Obsolete("보안상 하드코딩된 기본 비밀번호 사용을 권장하지 않습니다.")]
        public static string DefaultPassword => GetSetting("DefaultPassword", "");

        /// <summary>
        /// 데모 모드 활성화 여부
        /// </summary>
        public static bool DemoModeEnabled => GetSetting("DemoModeEnabled", false);

        /// <summary>
        /// 데모 모드 자동 시작 여부
        /// </summary>
        public static bool DemoModeAutoStart => GetSetting("DemoModeAutoStart", false);

        /// <summary>
        /// 데모 자동 종료 초(0이면 종료 안 함). 시뮬레이션 스모크 테스트용.
        /// </summary>
        public static int DemoModeAutoExitSeconds => GetSetting("DemoModeAutoExitSeconds", 0);

        #endregion

        #region UI 설정

        /// <summary>
        /// 최대 로그 항목 수
        /// </summary>
        public static int MaxLogEntries => GetSetting("MaxLogEntries", 200);

        /// <summary>로그를 한 줄씩 날짜별 파일(Log_yyyyMMdd.csv)에 바로 쓸지 여부</summary>
        public static bool LogFileEnabled => GetSetting("LogFileEnabled", true);

        /// <summary>날짜별 로그 폴더. 비우면 문서\SemiconductorUi_Logs\Daily</summary>
        public static string LogFolder => GetSetting("LogFolder", "");

        /// <summary>날짜별 로그 보관 일수 (오늘 포함). 0이면 자동 삭제하지 않는다.</summary>
        public static int LogRetentionDays => GetSetting("LogRetentionDays", 90);

        /// <summary>
        /// FOUP 최대 용량
        /// </summary>
        public static int MaxFoupCapacity => GetSetting("MaxFoupCapacity", 5);

        #endregion

        #region UI 색상 설정

        /// <summary>
        /// FOUP 패널 로딩 색상 (연한 녹색)
        /// </summary>
        public static ArgbColor FoupPanelLoadingColor => GetColorSetting("FoupPanelLoadingColor", 226, 244, 232);

        /// <summary>
        /// FOUP 패널 언로딩 색상 (연한 파란색)
        /// </summary>
        public static ArgbColor FoupPanelUnloadingColor => GetColorSetting("FoupPanelUnloadingColor", 224, 234, 250);

        /// <summary>
        /// FOUP 패널 경고 색상 (연한 주황색)
        /// </summary>
        public static ArgbColor FoupPanelAlertColor => GetColorSetting("FoupPanelAlertColor", 253, 239, 224);

        /// <summary>
        /// 다이얼로그 배경 색상 (매우 밝은 회색)
        /// </summary>
        public static ArgbColor DialogBackgroundColor => GetColorSetting("DialogBackgroundColor", 250, 250, 255);

        /// <summary>
        /// 텍스트 색상 (어두운 회색)
        /// </summary>
        public static ArgbColor TextColor => GetColorSetting("TextColor", 40, 40, 40);

        /// <summary>
        /// 성공 색상 (녹색)
        /// </summary>
        public static ArgbColor SuccessColor => GetColorSetting("SuccessColor", 76, 175, 80);

        /// <summary>
        /// 패널 배경 색상 (밝은 회색)
        /// </summary>
        public static ArgbColor PanelBackgroundColor => GetColorSetting("PanelBackgroundColor", 220, 220, 225);

        /// <summary>
        /// 기본 배경 색상 (밝은 회색)
        /// </summary>
        public static ArgbColor DefaultBackgroundColor => GetColorSetting("DefaultBackgroundColor", 245, 245, 250);

        /// <summary>
        /// 구분선 색상 (밝은 회색)
        /// </summary>
        public static ArgbColor SeparatorLineColor => GetColorSetting("SeparatorLineColor", 200, 200, 210);

        /// <summary>
        /// 웨이퍼 브러시 색상 (MediumBlue RGB 0,0,205 + Alpha 180)
        /// GetColorSetting(key, R, G, B, A?)
        /// </summary>
        public static ArgbColor WaferBrushColor => GetColorSetting("WaferBrushColor", 95, 140, 215, 220);

        /// <summary>
        /// 웨이퍼 펜 색상 (MediumBlue RGB 0,0,205 + Alpha 150)
        /// </summary>
        public static ArgbColor WaferPenColor => GetColorSetting("WaferPenColor", 70, 110, 185, 200);

        /// <summary>
        /// 테두리 색상 (밝은 회색)
        /// </summary>
        public static ArgbColor BorderColor => GetColorSetting("BorderColor", 220, 220, 230);

        /// <summary>
        /// 공정 초반 웨이퍼 색상 (파란색)
        /// </summary>
        public static ArgbColor WaferColorEarlyProcess => GetColorSetting("WaferColorEarlyProcess", 210, 230, 255);

        /// <summary>
        /// 공정 후반 웨이퍼 색상 (보라색)
        /// </summary>
        public static ArgbColor WaferColorLateProcess => GetColorSetting("WaferColorLateProcess", 210, 190, 255);

        /// <summary>
        /// 램프 빨간색 (밝은 상태)
        /// </summary>
        public static ArgbColor LampRedBright => GetColorSetting("LampRedBright", 210, 60, 50);

        /// <summary>
        /// 램프 황색 (밝은 상태)
        /// </summary>
        public static ArgbColor LampYellowBright => GetColorSetting("LampYellowBright", 230, 190, 40);

        /// <summary>
        /// 램프 녹색 (밝은 상태)
        /// </summary>
        public static ArgbColor LampGreenBright => GetColorSetting("LampGreenBright", 55, 180, 90);

        /// <summary>
        /// 램프 빨간색 (어두운 상태)
        /// </summary>
        public static ArgbColor LampRedDark => GetColorSetting("LampRedDark", 50, 15, 12);

        /// <summary>
        /// 램프 황색 (어두운 상태)
        /// </summary>
        public static ArgbColor LampYellowDark => GetColorSetting("LampYellowDark", 60, 50, 15);

        /// <summary>
        /// 램프 녹색 (어두운 상태)
        /// </summary>
        public static ArgbColor LampGreenDark => GetColorSetting("LampGreenDark", 20, 60, 30);

        #endregion

        #region 타이머 설정

        /// <summary>
        /// 시뮬레이션 모드 타이머 주기 (밀리초)
        /// </summary>
        public static int SimulationTickMilliseconds => GetSetting("SimulationTickMilliseconds", 1000);

        /// <summary>
        /// 하드웨어 모드 타이머 주기 (밀리초)
        /// </summary>
        public static int HardwareModeTickMilliseconds => GetSetting("HardwareModeTickMilliseconds", 50);

        /// <summary>
        /// 챔버 공정 시간 처리 속도 (초/틱)
        /// </summary>
        public static double ChamberSecondsPerTick => GetSetting("ChamberSecondsPerTick", 1.0);

        #endregion

        #region TM (Transfer Module) 설정

        /// <summary>
        /// TM 픽업 지속 시간 (틱)
        /// </summary>
        public static int TmPickupDurationTicks => GetSetting("TmPickupDurationTicks", 3);

        /// <summary>
        /// TM 픽업 후퇴 시간 (틱)
        /// </summary>
        public static int TmPickupRetractTicks => GetSetting("TmPickupRetractTicks", 2);

        /// <summary>
        /// TM 드롭오프 지속 시간 (틱)
        /// </summary>
        public static int TmDropoffDurationTicks => GetSetting("TmDropoffDurationTicks", 3);

        /// <summary>
        /// TM 드롭오프 후퇴 시간 (틱)
        /// </summary>
        public static int TmDropoffRetractTicks => GetSetting("TmDropoffRetractTicks", 2);

        /// <summary>
        /// TM 단거리 이동 시간 (틱)
        /// </summary>
        public static int TmMoveShortTicks => GetSetting("TmMoveShortTicks", 3);

        /// <summary>
        /// TM 중거리 이동 시간 (틱)
        /// </summary>
        public static int TmMoveMediumTicks => GetSetting("TmMoveMediumTicks", 5);

        /// <summary>
        /// TM 장거리 이동 시간 (틱)
        /// </summary>
        public static int TmMoveLongTicks => GetSetting("TmMoveLongTicks", 8);

        /// <summary>
        /// TM 도어 동작 시간 (틱)
        /// </summary>
        public static int TmDoorActionTicks => GetSetting("TmDoorActionTicks", 2);

        /// <summary>
        /// 도어 열림 대기 틱 수 (하드웨어 틱 150ms 기준, 기본 14 ≈ 2.1초).
        /// 도어 위치 센서가 없어 시간으로만 대기하므로 실제 개폐 시간보다 짧게 잡지 말 것.
        /// </summary>
        public static int DoorOpenWaitTicks => GetSetting("DoorOpenWaitTicks", 14);

        /// <summary>
        /// 도어 닫힘 대기 틱 수 (하드웨어 틱 150ms 기준, 기본 14 ≈ 2.1초).
        /// </summary>
        public static int DoorCloseWaitTicks => GetSetting("DoorCloseWaitTicks", 14);

        /// <summary>
        /// 하드웨어 모드 도어 열림 대기 시간 (밀리초). 열기 명령 후 이 시간이 지나야 실린더를 전진합니다.
        /// 도어 위치 센서가 없으므로 실제 도어가 다 열리는 시간보다 짧게 잡지 마세요.
        /// </summary>
        public static int DoorOpenWaitMs => GetSetting("DoorOpenWaitMs", 2200);

        /// <summary>
        /// 하드웨어 모드 도어 닫힘 대기 시간 (밀리초). 닫기 명령 후 이 시간이 지나면 닫힌 것으로 보고 다음 단계로 갑니다.
        /// </summary>
        public static int DoorCloseWaitMs => GetSetting("DoorCloseWaitMs", 2100);

        #endregion

        #region 서보모터 파라미터 설정

        // 서보 속도: 티칭 창 속도 탭에서 이번 실행 중 저장한 값 → 설정 파일 → 아래 기본값 순서로 씁니다.
        // 설정 파일에 값이 없을 때 쓰는 서보 속도 기본값 (티칭 창 속도 탭의 기본값 복원도 이 값을 씁니다)
        public const long DefaultServoVelocity = 1000000L;
        public const long DefaultServoMaxVelocity = 1000000L;
        public const long DefaultServoDeceleration = 100000000L;
        public const long DefaultServoAcceleration = 1000000L;
        public const long DefaultServoHomingVelocity = 300000L;
        public const long DefaultServoHomingMaxVelocity = 500000L;

        /// <summary>
        /// 서보모터 기본 속도
        /// </summary>
        public static long ServoDefaultVelocity => Repositories.ServoSpeedRepository.SavedThisSession?.Velocity ?? GetSetting("ServoDefaultVelocity", DefaultServoVelocity);

        /// <summary>
        /// 서보모터 최대 속도
        /// </summary>
        public static long ServoDefaultMaxVelocity => Repositories.ServoSpeedRepository.SavedThisSession?.MaxVelocity ?? GetSetting("ServoDefaultMaxVelocity", DefaultServoMaxVelocity);

        /// <summary>
        /// 서보모터 감속도
        /// </summary>
        public static long ServoDefaultDeceleration => Repositories.ServoSpeedRepository.SavedThisSession?.Deceleration ?? GetSetting("ServoDefaultDeceleration", DefaultServoDeceleration);

        /// <summary>
        /// 서보모터 가속도
        /// </summary>
        public static long ServoDefaultAcceleration => Repositories.ServoSpeedRepository.SavedThisSession?.Acceleration ?? GetSetting("ServoDefaultAcceleration", DefaultServoAcceleration);

        /// <summary>
        /// 서보모터 원점복귀 속도
        /// </summary>
        public static long ServoHomingVelocity => Repositories.ServoSpeedRepository.SavedThisSession?.HomingVelocity ?? GetSetting("ServoHomingVelocity", DefaultServoHomingVelocity);

        /// <summary>
        /// 서보모터 원점복귀 최대 속도
        /// </summary>
        public static long ServoHomingMaxVelocity => Repositories.ServoSpeedRepository.SavedThisSession?.HomingMaxVelocity ?? GetSetting("ServoHomingMaxVelocity", DefaultServoHomingMaxVelocity);

        /// <summary>
        /// 서보 XY 동시 이동 타임아웃 (밀리초)
        /// </summary>
        public static int ServoXyMoveTimeoutMs => GetSetting("ServoXyMoveTimeoutMs", 5000);

        /// <summary>
        /// 서보 이동 최소 대기 시간 (밀리초)
        /// </summary>
        public static int ServoMoveMinWaitMs => GetSetting("ServoMoveMinWaitMs", 500);

        /// <summary>
        /// 서보 Axis1 최소 대기 시간 (밀리초)
        /// </summary>
        public static int ServoAxis1MinWaitMs => GetSetting("ServoAxis1MinWaitMs", 300);

        /// <summary>
        /// 공정 루프 이동 완료 판정: 상하(Axis1) 목표 위치 허용 오차 (펄스).
        /// PP_D가 늦게 켜져도 현재 위치가 목표 ± 이 값 안에 머물면 완료로 본다. 0 이하면 끔(PP_D만 사용)
        /// </summary>
        public static int ServoInPositionToleranceAxis1 => GetSetting("ServoInPositionToleranceAxis1", 1000);

        /// <summary>
        /// 공정 루프 이동 완료 판정: 회전(Axis2) 목표 위치 허용 오차 (펄스). 0 이하면 끔(PP_D만 사용)
        /// </summary>
        public static int ServoInPositionToleranceAxis2 => GetSetting("ServoInPositionToleranceAxis2", 1000);

        /// <summary>
        /// 위치가 허용 오차 안에 연속으로 머물러야 하는 시간 (밀리초). DLL 위치 갱신 주기(300 ms) 이상으로 둔다
        /// </summary>
        public static int ServoInPositionHoldMs => GetSetting("ServoInPositionHoldMs", 300);

        #endregion

        #region 하드웨어 타임아웃 설정

        /// <summary>
        /// TM 하드웨어 동작 타임아웃 (밀리초)
        /// </summary>
        public static int TmHardwareActionTimeoutMs => GetSetting("TmHardwareActionTimeoutMs", 30000);

        /// <summary>
        /// 실린더 동작 타임아웃 (밀리초)
        /// </summary>
        public static int CylinderActionTimeoutMs => GetSetting("CylinderActionTimeoutMs", 5000);

        /// <summary>
        /// 서보 이동 타임아웃 (밀리초)
        /// </summary>
        public static int ServoMoveTimeoutMs => GetSetting("ServoMoveTimeoutMs", 3000);

        /// <summary>
        /// 서보 안정화 지연 시간 (밀리초)
        /// </summary>
        public static int ServoSettleDelayMs => GetSetting("ServoSettleDelayMs", 50);

        /// <summary>
        /// 실린더 안정화 지연 시간 (밀리초)
        /// </summary>
        public static int CylinderSettleDelayMs => GetSetting("CylinderSettleDelayMs", 50);

        /// <summary>
        /// 진공 ON 안정화 지연 시간 (밀리초)
        /// </summary>
        public static int VacuumOnSettleDelayMs => GetSetting("VacuumOnSettleDelayMs", 100);

        /// <summary>
        /// 진공 OFF 안정화 지연 시간 (밀리초)
        /// </summary>
        public static int VacuumOffSettleDelayMs => GetSetting("VacuumOffSettleDelayMs", 100);

        /// <summary>
        /// 도어 안정화 지연 시간 (밀리초)
        /// </summary>
        public static int DoorSettleDelayMs => GetSetting("DoorSettleDelayMs", 30);

        /// <summary>
        /// 하드웨어 폴링 주기 (밀리초)
        /// 서보 이동, 실린더 동작 등의 상태 확인 주기
        /// </summary>
        public static int HardwarePollingIntervalMs => GetSetting("HardwarePollingIntervalMs", 30);

        /// <summary>
        /// 서보 초기화 지연 시간 (밀리초)
        /// 서보모터 OFF 후 ON 전 대기 시간
        /// </summary>
        public static int ServoInitDelayMs => GetSetting("ServoInitDelayMs", 100);

        /// <summary>
        /// 서보모터 ON 후 대기 시간 (밀리초)
        /// 서보모터 ON 명령 후 안정화 대기 시간
        /// </summary>
        public static int ServoOnDelayMs => GetSetting("ServoOnDelayMs", 200);

        /// <summary>
        /// 원점복귀 폴링 주기 (밀리초)
        /// 원점복귀 상태 확인 주기
        /// </summary>
        public static int HomingPollingIntervalMs => GetSetting("HomingPollingIntervalMs", 100);

        #endregion

        #region 알람 임계값 기본 설정

        /// <summary>
        /// 온도 경고 임계값 (°C)
        /// </summary>
        public static double AlarmTempWarn => GetSetting("AlarmTempWarn", 2.0);

        /// <summary>
        /// 온도 알람 임계값 (°C)
        /// </summary>
        public static double AlarmTempAlarm => GetSetting("AlarmTempAlarm", 5.0);

        /// <summary>
        /// 압력 경고 비율 (SV 대비)
        /// </summary>
        public static double AlarmPressWarnRatio => GetSetting("AlarmPressWarnRatio", 0.2);

        /// <summary>
        /// 압력 알람 비율 (SV 대비)
        /// </summary>
        public static double AlarmPressAlarmRatio => GetSetting("AlarmPressAlarmRatio", 0.5);

        /// <summary>
        /// 압력 경고 절대값 (Torr)
        /// </summary>
        public static double AlarmPressWarnAbs => GetSetting("AlarmPressWarnAbs", 3.0);

        /// <summary>
        /// 압력 알람 절대값 (Torr)
        /// </summary>
        public static double AlarmPressAlarmAbs => GetSetting("AlarmPressAlarmAbs", 10.0);

        /// <summary>
        /// RF 경고 비율 (SV 대비)
        /// </summary>
        public static double AlarmRfWarnRatio => GetSetting("AlarmRfWarnRatio", 0.10);

        /// <summary>
        /// RF 알람 비율 (SV 대비)
        /// </summary>
        public static double AlarmRfAlarmRatio => GetSetting("AlarmRfAlarmRatio", 0.20);

        /// <summary>
        /// 가스 경고 절대값 (sccm)
        /// </summary>
        public static double AlarmGasWarn => GetSetting("AlarmGasWarn", 5.0);

        /// <summary>
        /// 가스 알람 절대값 (sccm)
        /// </summary>
        public static double AlarmGasAlarm => GetSetting("AlarmGasAlarm", 10.0);

        /// <summary>
        /// 가스 누설 경고 (sccm)
        /// </summary>
        public static double AlarmGasLeakWarn => GetSetting("AlarmGasLeakWarn", 1.0);

        /// <summary>
        /// 가스 누설 알람 (sccm)
        /// </summary>
        public static double AlarmGasLeakAlarm => GetSetting("AlarmGasLeakAlarm", 3.0);

        #endregion

        #region 설정 읽기 헬퍼 메서드

        /// <summary>
        /// 문자열 설정값 읽기
        /// </summary>
        private static string GetSetting(string key, string defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 정수 설정값 읽기
        /// </summary>
        private static int GetSetting(string key, int defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (int.TryParse(value, out int result))
                {
                    return result;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 부울 설정값 읽기
        /// </summary>
        private static bool GetSetting(string key, bool defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (bool.TryParse(value, out bool result))
                {
                    return result;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Long 정수 설정값 읽기
        /// </summary>
        private static long GetSetting(string key, long defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (long.TryParse(value, out long result))
                {
                    return result;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 실수 설정값 읽기
        /// </summary>
        private static double GetSetting(string key, double defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (double.TryParse(value, out double result))
                {
                    return result;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 색상 설정값 읽기 (RGB 형식: "R,G,B" 또는 "A,R,G,B")
        /// </summary>
        private static ArgbColor GetColorSetting(string key, int defaultR, int defaultG, int defaultB, int? defaultA = null)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var parts = value.Split(',');
                    if (parts.Length == 3)
                    {
                        // RGB 형식
                        if (int.TryParse(parts[0].Trim(), out int r) &&
                            int.TryParse(parts[1].Trim(), out int g) &&
                            int.TryParse(parts[2].Trim(), out int b))
                        {
                            return ArgbColor.FromArgb(255, r, g, b);
                        }
                    }
                    else if (parts.Length == 4)
                    {
                        // ARGB 형식
                        if (int.TryParse(parts[0].Trim(), out int a) &&
                            int.TryParse(parts[1].Trim(), out int r) &&
                            int.TryParse(parts[2].Trim(), out int g) &&
                            int.TryParse(parts[3].Trim(), out int b))
                        {
                            return ArgbColor.FromArgb(a, r, g, b);
                        }
                    }
                }
                
                // 기본값 반환
                if (defaultA.HasValue)
                {
                    return ArgbColor.FromArgb(defaultA.Value, defaultR, defaultG, defaultB);
                }
                return ArgbColor.FromArgb(255, defaultR, defaultG, defaultB);
            }
            catch
            {
                // 기본값 반환
                if (defaultA.HasValue)
                {
                    return ArgbColor.FromArgb(defaultA.Value, defaultR, defaultG, defaultB);
                }
                return ArgbColor.FromArgb(255, defaultR, defaultG, defaultB);
            }
        }

        #endregion
    }
}

