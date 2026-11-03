using System;
using SemiconductorUi.Hardware;
using SemiconductorUi.Controls;
using AppSettings = SemiconductorUi.AppSettings;

namespace SemiconductorUi.Controllers
{
    /// <summary>
    /// TM(Transfer Module) 하드웨어 제어 컨트롤러
    /// 서보모터(Axis1: 상하, Axis2: 좌우), 실린더, 진공 제어를 담당
    /// </summary>
    public class TmHardwareController
    {
        #region Constants - Digital I/O Mapping

        // 웨이퍼 이송 실린더 (전진/후진)
        private const int CYLINDER_FORWARD_OUTPUT = 12;   // 실린더 전진 출력
        private const int CYLINDER_BACKWARD_OUTPUT = 13;  // 실린더 후진 출력
        private const int CYLINDER_FORWARD_SENSOR = 13;   // 실린더 전진 센서 (Input)
        private const int CYLINDER_BACKWARD_SENSOR = 12;  // 실린더 후진 센서 (Input)

        // 진공 제어
        private const int VACUUM_INTAKE_OUTPUT = 14;      // 흡기 출력
        private const int VACUUM_EXHAUST_OUTPUT = 15;     // 배기 출력

        // 서보모터 파라미터 (AppSettings에서 읽어옴)
        private static long DEFAULT_VELOCITY => AppSettings.ServoDefaultVelocity;
        private static long DEFAULT_MAX_VELOCITY => AppSettings.ServoDefaultMaxVelocity;
        private static long DEFAULT_DECELERATION => AppSettings.ServoDefaultDeceleration;
        private static long DEFAULT_ACCELERATION => AppSettings.ServoDefaultAcceleration;

        // 타임아웃 설정 (AppSettings에서 읽어옴, 밀리초)
        private static int SERVO_MOVE_TIMEOUT_MS => AppSettings.ServoMoveTimeoutMs;  // 서보 이동 타임아웃
        private static int CYLINDER_ACTION_TIMEOUT_MS => AppSettings.CylinderActionTimeoutMs;  // 실린더 동작 타임아웃
        private static int VACUUM_STABILIZE_DELAY_MS => AppSettings.VacuumOnSettleDelayMs;    // 진공 안정화 대기 시간
        private static int EXHAUST_DURATION_MS => AppSettings.VacuumOffSettleDelayMs;          // 배기 지속 시간
        
        // 하드웨어 폴링 주기 설정 (AppSettings에서 읽어옴, 밀리초)
        private static int HARDWARE_POLLING_INTERVAL_MS => AppSettings.HardwarePollingIntervalMs;  // 하드웨어 폴링 주기
        private static int SERVO_INIT_DELAY_MS => AppSettings.ServoInitDelayMs;  // 서보 초기화 지연 시간
        private static int SERVO_ON_DELAY_MS => AppSettings.ServoOnDelayMs;  // 서보모터 ON 후 대기 시간
        private static int HOMING_POLLING_INTERVAL_MS => AppSettings.HomingPollingIntervalMs;  // 원점복귀 폴링 주기

        #endregion

        #region Position Definitions

        /// <summary>
        /// TM 위치 좌표 (Axis1: Y축 상하, Axis2: X축 좌우)
        /// 교수님 티칭값 기반
        /// 
        /// 높이 순서: 하강(Descend) < 안착(Land) < 상승(Raise)
        /// - 하강 위치: 실린더 전진/후진용 (안착 - 20000)
        /// - 안착 위치: 웨이퍼가 놓이는 정확한 위치
        /// - 상승 위치: 웨이퍼를 들어올린 이동 위치
        /// </summary>
        public class TmPositionSet
        {
            // 하강 위치 오프셋 (안착 위치 - 이 값 = 하강 위치). UI/TeachingPositions.xml에서 변경 가능.
            public long DescendOffset { get; set; } = 30000;

            // Chamber A 위치 (좌우: -59064)
            public long ChamberA_X { get; set; } = -59064;
            public long ChamberA_LandY { get; set; } = 806931;    // 안착 위치
            public long ChamberA_RaiseY { get; set; } = 1156931;  // 상승 위치
            public long ChamberA_DescendY => ChamberA_LandY - DescendOffset;  // 하강 위치

            // Chamber B 위치 (좌우: -190823)
            public long ChamberB_X { get; set; } = -190823;
            public long ChamberB_LandY { get; set; } = 806931;
            public long ChamberB_RaiseY { get; set; } = 1156931;
            public long ChamberB_DescendY => ChamberB_LandY - DescendOffset;

            // Chamber C 위치 (좌우: -321600)
            public long ChamberC_X { get; set; } = -321600;
            public long ChamberC_LandY { get; set; } = 806931;
            public long ChamberC_RaiseY { get; set; } = 1156931;
            public long ChamberC_DescendY => ChamberC_LandY - DescendOffset;

            // FOUP A 위치 (좌우: 14140)
            public long FoupA_X { get; set; } = 14140;

            // FOUP A 층별 높이 (1~5층)
            public long[] FoupA_LandY { get; set; } = new long[] 
            { 
                102379,   // 1층 안착
                782378,   // 2층 안착
                1432388,  // 3층 안착
                2119399,  // 4층 안착
                2818463   // 5층 안착
            };
            public long[] FoupA_RaiseY { get; set; } = new long[] 
            { 
                302380,   // 1층 상승
                982378,   // 2층 상승
                1627604,  // 3층 상승
                2332102,  // 4층 상승
                3018457   // 5층 상승
            };
            public long[] FoupA_DescendY => new long[]
            {
                FoupA_LandY[0] - DescendOffset,
                FoupA_LandY[1] - DescendOffset,
                FoupA_LandY[2] - DescendOffset,
                FoupA_LandY[3] - DescendOffset,
                FoupA_LandY[4] - DescendOffset
            };

            // FOUP B 위치 (좌우: -394293, 상하는 FOUP A와 동일)
            public long FoupB_X { get; set; } = -394293;

            // FOUP B 층별 높이 (FOUP A와 동일)
            public long[] FoupB_LandY { get; set; } = new long[] 
            { 
                102379,   // 1층 안착
                782378,   // 2층 안착
                1432388,  // 3층 안착
                2119399,  // 4층 안착
                2818463   // 5층 안착
            };
            public long[] FoupB_RaiseY { get; set; } = new long[] 
            { 
                302380,   // 1층 상승
                982378,   // 2층 상승
                1627604,  // 3층 상승
                2332102,  // 4층 상승
                3018457   // 5층 상승
            };
            public long[] FoupB_DescendY => new long[]
            {
                FoupB_LandY[0] - DescendOffset,
                FoupB_LandY[1] - DescendOffset,
                FoupB_LandY[2] - DescendOffset,
                FoupB_LandY[3] - DescendOffset,
                FoupB_LandY[4] - DescendOffset
            };

            // Home / 대기 위치 (원점 복귀 후 기준 좌표)
            public long Home_X { get; set; } = 0;
            public long Home_Y { get; set; } = 0;

            /// <summary>저장 데이터에서 위치값을 덮어씁니다.</summary>
            public void ApplyFrom(SemiconductorUi.Models.TeachingPositionsData data)
            {
                if (data == null) return;
                DescendOffset = data.DescendOffset;
                ChamberA_X = data.ChamberA_X;
                ChamberA_LandY = data.ChamberA_LandY;
                ChamberA_RaiseY = data.ChamberA_RaiseY;
                ChamberB_X = data.ChamberB_X;
                ChamberB_LandY = data.ChamberB_LandY;
                ChamberB_RaiseY = data.ChamberB_RaiseY;
                ChamberC_X = data.ChamberC_X;
                ChamberC_LandY = data.ChamberC_LandY;
                ChamberC_RaiseY = data.ChamberC_RaiseY;
                FoupA_X = data.FoupA_X;
                FoupB_X = data.FoupB_X;
                Home_X = data.Home_X;
                Home_Y = data.Home_Y;
                FoupA_LandY = EnsureSlotArray(FoupA_LandY, data.FoupA_LandY, new long[] { 102379, 782378, 1432388, 2119399, 2818463 });
                FoupA_RaiseY = EnsureSlotArray(FoupA_RaiseY, data.FoupA_RaiseY, new long[] { 302380, 982378, 1627604, 2332102, 3018457 });
                FoupB_LandY = EnsureSlotArray(FoupB_LandY, data.FoupB_LandY, new long[] { 102379, 782378, 1432388, 2119399, 2818463 });
                FoupB_RaiseY = EnsureSlotArray(FoupB_RaiseY, data.FoupB_RaiseY, new long[] { 302380, 982378, 1627604, 2332102, 3018457 });
            }

            /// <summary>현재 위치값을 저장 DTO로 변환합니다.</summary>
            public SemiconductorUi.Models.TeachingPositionsData ToData()
            {
                return new SemiconductorUi.Models.TeachingPositionsData
                {
                    DescendOffset = DescendOffset,
                    ChamberA_X = ChamberA_X,
                    ChamberA_LandY = ChamberA_LandY,
                    ChamberA_RaiseY = ChamberA_RaiseY,
                    ChamberB_X = ChamberB_X,
                    ChamberB_LandY = ChamberB_LandY,
                    ChamberB_RaiseY = ChamberB_RaiseY,
                    ChamberC_X = ChamberC_X,
                    ChamberC_LandY = ChamberC_LandY,
                    ChamberC_RaiseY = ChamberC_RaiseY,
                    FoupA_X = FoupA_X,
                    FoupB_X = FoupB_X,
                    Home_X = Home_X,
                    Home_Y = Home_Y,
                    FoupA_LandY = (long[])FoupA_LandY.Clone(),
                    FoupA_RaiseY = (long[])FoupA_RaiseY.Clone(),
                    FoupB_LandY = (long[])FoupB_LandY.Clone(),
                    FoupB_RaiseY = (long[])FoupB_RaiseY.Clone()
                };
            }

            private static long[] EnsureSlotArray(long[] target, long[] source, long[] defaults)
            {
                if (target == null || target.Length != 5)
                {
                    target = (long[])defaults.Clone();
                }
                if (source == null) return target;
                for (int i = 0; i < 5 && i < source.Length; i++)
                {
                    target[i] = source[i];
                }
                return target;
            }
        }

        /// <summary>
        /// 현재 FOUP 층 인덱스 (0-4, 1~5층에 해당)
        /// </summary>
        private int _currentFoupASlot = 0;
        private int _currentFoupBSlot = 0;

        /// <summary>
        /// FOUP A 현재 층 설정 (1~5)
        /// </summary>
        public int CurrentFoupASlot
        {
            get => _currentFoupASlot + 1;
            set => _currentFoupASlot = Math.Max(0, Math.Min(4, value - 1));
        }

        /// <summary>
        /// FOUP B 현재 층 설정 (1~5)
        /// </summary>
        public int CurrentFoupBSlot
        {
            get => _currentFoupBSlot + 1;
            set => _currentFoupBSlot = Math.Max(0, Math.Min(4, value - 1));
        }

        #endregion

        #region Fields

        private readonly IEthercatDevice _ethercat;
        private readonly Action<string, string> _logCallback;
        private TmPositionSet _positions;
        private bool _isInitialized;
        private bool _isServoOn;
        private bool _isHomed;
        private bool _hasEverHomed; // 세션 내 원점복귀 1회 이상 완료 (이동해도 유지)
        private bool _isVacuumOn;

        // 현재 상태 추적
        private long _currentAxis1Pos;
        private long _currentAxis2Pos;
        private bool _cylinderForward;
        private bool _isCarryingWafer;

        #endregion

        #region Properties

        /// <summary>
        /// 하드웨어 초기화 완료 여부
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 서보모터 ON 상태
        /// </summary>
        public bool IsServoOn => _isServoOn;

        /// <summary>
        /// 현재 원점 위치 여부 (이동하면 false)
        /// </summary>
        public bool IsHomed => _isHomed;

        /// <summary>
        /// 세션 중 원점복귀를 한 번이라도 완료했는지 (이동 인터락용)
        /// </summary>
        public bool HasEverHomed => _hasEverHomed;

        /// <summary>
        /// 진공 흡착 상태
        /// </summary>
        public bool IsVacuumOn => _isVacuumOn;

        /// <summary>
        /// 웨이퍼 보유 상태
        /// </summary>
        public bool IsCarryingWafer => _isCarryingWafer;

        /// <summary>
        /// 실린더 전진 상태
        /// </summary>
        public bool IsCylinderForward => _cylinderForward;

        /// <summary>
        /// 현재 Axis1 위치 (상하)
        /// </summary>
        public long CurrentAxis1Position => _currentAxis1Pos;

        /// <summary>
        /// 현재 Axis2 위치 (좌우)
        /// </summary>
        public long CurrentAxis2Position => _currentAxis2Pos;

        /// <summary>
        /// 위치 설정
        /// </summary>
        public TmPositionSet Positions
        {
            get => _positions;
            set => _positions = value ?? new TmPositionSet();
        }

        #endregion

        #region Constructor

        /// <summary>
        /// TM 하드웨어 컨트롤러 생성
        /// </summary>
        /// <param name="ethercat">EtherCAT 통신 객체</param>
        /// <param name="logCallback">로그 콜백 (메시지, 레벨)</param>
        public TmHardwareController(IEthercatDevice ethercat, Action<string, string> logCallback = null)
        {
            _ethercat = ethercat ?? throw new ArgumentNullException(nameof(ethercat));
            _logCallback = logCallback ?? ((msg, level) => { });
            _positions = new TmPositionSet();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 서보 상태 확인 및 동기화
        /// 실제 하드웨어 상태를 확인하여 _isServoOn 플래그를 업데이트
        /// </summary>
        public void SyncServoStatus()
        {
            try
            {
                // Axis1과 Axis2의 위치 데이터가 읽히면 서보가 ON 상태
                var axis1Pos = _ethercat.Axis1_is_PosData();
                var axis2Pos = _ethercat.Axis2_is_PosData();
                
                bool servoIsOn = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" 
                              && !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-";
                
                _isServoOn = servoIsOn;
                
                if (servoIsOn)
                {
                    try
                    {
                        // 양쪽 HOME_D면 세션 원점복귀 이력으로 인정 (공정/수동 홈 경로 동기화)
                        if (_ethercat.Axis1_Status("HOME_D") && _ethercat.Axis2_Status("HOME_D"))
                        {
                            _isHomed = true;
                            _hasEverHomed = true;
                            Log("서보 상태 동기화: ON + Homed", "INFO");
                        }
                        else
                        {
                            Log("서보 상태 동기화: ON", "INFO");
                        }
                    }
                    catch
                    {
                        Log("서보 상태 동기화: ON (HOME_D 확인 실패)", "INFO");
                    }
                }
                else
                {
                    Log("서보 상태 동기화: OFF", "WARN");
                }
            }
            catch (Exception ex)
            {
                Log($"서보 상태 동기화 오류: {ex.Message}", "ERROR");
                _isServoOn = false; // 오류 시 OFF로 처리
            }
        }

        /// <summary>
        /// TM 하드웨어 초기화
        /// 서보 ON, 파라미터 설정
        /// </summary>
        public TmOperationResult Initialize()
        {
            try
            {
                Log("TM 하드웨어 초기화 시작", "INFO");

                // 1. 실린더 상태 확인 - 후진 상태여야 함
                if (!CheckCylinderRetracted())
                {
                    // 실린더 후진 명령
                    var retractResult = RetractCylinder();
                    if (!retractResult.Success)
                    {
                        return TmOperationResult.Fail("초기화 실패: 실린더 후진 실패 - " + retractResult.ErrorMessage);
                    }
                }

                // 2. 진공 OFF
                SetVacuumOff();

                // 3. 서보모터 OFF 후 ON (초기화)
                _ethercat.Axis1_OFF();
                _ethercat.Axis2_OFF();
                System.Threading.Thread.Sleep(SERVO_INIT_DELAY_MS);

                // 4. 서보모터 파라미터 설정
                _ethercat.Axis1_UD_Config_Update(
                    DEFAULT_VELOCITY,
                    DEFAULT_MAX_VELOCITY,
                    DEFAULT_DECELERATION,
                    DEFAULT_ACCELERATION);

                _ethercat.Axis2_LR_Config_Update(
                    DEFAULT_VELOCITY,
                    DEFAULT_MAX_VELOCITY,
                    DEFAULT_DECELERATION,
                    DEFAULT_ACCELERATION);

                // 5. 서보모터 ON
                _ethercat.Axis1_ON();
                _ethercat.Axis2_ON();
                System.Threading.Thread.Sleep(SERVO_ON_DELAY_MS);

                _isServoOn = true;
                _isInitialized = true;

                Log("TM 하드웨어 초기화 완료", "INFO");
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"TM 하드웨어 초기화 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"초기화 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 원점복귀 수행
        /// 중요: 상하(Axis1) 원점복귀 완료 후 좌우(Axis2) 원점복귀 실행
        /// 실린더 전진 상태에서는 좌우 이동 불가하므로 실린더 확인 필수
        /// </summary>
        public TmOperationResult PerformHoming()
        {
            try
            {
                if (!_isServoOn)
                {
                    return TmOperationResult.Fail("서보모터가 OFF 상태입니다");
                }

                // 실린더 후진 상태 확인 - 좌우 이동 전 필수
                if (!CheckCylinderRetracted())
                {
                    return TmOperationResult.Fail("실린더가 전진 상태입니다. 실린더 전진 시 좌우 이동 불가합니다. 먼저 실린더를 후진해주세요.");
                }

                Log("원점복귀 시작 - 1단계: 상하(Axis1)", "INFO");

                // 1단계: Axis1 (상하) 원점복귀
                _ethercat.Axis1_UD_Homming();

                // Axis1 원점복귀 완료 대기
                var timeout = DateTime.Now.AddMilliseconds(SERVO_MOVE_TIMEOUT_MS);
                while (DateTime.Now < timeout)
                {
                    _ethercat.ThrowIfEmergencyStopped();
                    if (_ethercat.Axis1_Status("HOME_D"))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(HOMING_POLLING_INTERVAL_MS);
                }

                if (!_ethercat.Axis1_Status("HOME_D"))
                {
                    return TmOperationResult.Fail("상하(Axis1) 원점복귀 타임아웃");
                }

                Log("상하(Axis1) 원점복귀 완료 - 2단계: 좌우(Axis2)", "INFO");

                // 2단계: Axis2 (좌우) 원점복귀
                _ethercat.Axis2_LR_Homming();

                // Axis2 원점복귀 완료 대기
                timeout = DateTime.Now.AddMilliseconds(SERVO_MOVE_TIMEOUT_MS);
                while (DateTime.Now < timeout)
                {
                    _ethercat.ThrowIfEmergencyStopped();
                    if (_ethercat.Axis2_Status("HOME_D"))
                    {
                        _isHomed = true;
                        _hasEverHomed = true;
                        _currentAxis1Pos = 0;
                        _currentAxis2Pos = 0;
                        Log("원점복귀 완료 (상하 → 좌우 순서)", "INFO");
                        return TmOperationResult.Ok();
                    }
                    System.Threading.Thread.Sleep(HOMING_POLLING_INTERVAL_MS);
                }

                return TmOperationResult.Fail("좌우(Axis2) 원점복귀 타임아웃");
            }
            catch (Exception ex)
            {
                Log($"원점복귀 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"원점복귀 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 외부 경로(공정 시작/장비제어 폼)에서 원점복귀 성공 시 호출.
        /// MoveToPosition이 요구하는 HasEverHomed 플래그를 맞춘다.
        /// </summary>
        public void MarkHomedAfterSuccessfulHoming()
        {
            _isHomed = true;
            _hasEverHomed = true;
            _isServoOn = true;
            _currentAxis1Pos = 0;
            _currentAxis2Pos = 0;
            Log("원점복귀 상태 플래그 동기화 (HasEverHomed=true)", "INFO");
        }

        /// <summary>
        /// 진공 상태 플래그만 동기화 (Form1 Digital_Output 경로용)
        /// </summary>
        public void SyncVacuumState(bool isOn)
        {
            _isVacuumOn = isOn;
        }

        /// <summary>
        /// TM 하드웨어 종료
        /// </summary>
        public void Shutdown()
        {
            try
            {
                Log("TM 하드웨어 종료", "INFO");

                // 진공 OFF
                SetVacuumOff();

                // 실린더 후진
                RetractCylinder();

                // 서보 OFF
                _ethercat.Axis1_OFF();
                _ethercat.Axis2_OFF();

                _isServoOn = false;
                _isInitialized = false;
                _isHomed = false;
                _hasEverHomed = false;
            }
            catch (Exception ex)
            {
                Log($"TM 종료 오류: {ex.Message}", "ERROR");
            }
        }

        #endregion

        #region Servo Motor Control

        /// <summary>
        /// 지정된 위치로 서보모터 이동
        /// </summary>
        /// <param name="targetX">Axis2 목표 위치 (좌우)</param>
        /// <param name="targetY">Axis1 목표 위치 (상하)</param>
        /// <param name="waitForCompletion">완료 대기 여부</param>
        public TmOperationResult MoveToPosition(long targetX, long targetY, bool waitForCompletion = true)
        {
            try
            {
                // 세션 원점복귀 미완료 시 이동 금지
                if (!_hasEverHomed)
                {
                    return TmOperationResult.Fail("원점복귀가 완료되지 않았습니다");
                }

                // 안전 인터락: 실린더가 후진 상태여야 이동 가능
                if (!CheckCylinderRetracted())
                {
                    return TmOperationResult.Fail("실린더가 전진 상태입니다. 이동할 수 없습니다.");
                }

                // 서보 상태 확인: 플래그와 실제 하드웨어 상태 모두 확인
                // 실제 하드웨어 상태를 우선 확인 (플래그가 잘못되었을 수 있음)
                bool actualServoOn = false;
                try
                {
                    var axis1Pos = _ethercat.Axis1_is_PosData();
                    var axis2Pos = _ethercat.Axis2_is_PosData();
                    actualServoOn = !string.IsNullOrEmpty(axis1Pos) && axis1Pos != "-" 
                                 && !string.IsNullOrEmpty(axis2Pos) && axis2Pos != "-";
                }
                catch (Exception ex)
                {
                    Log($"서보 상태 확인 오류: {ex.Message}", "WARN");
                }
                
                if (!actualServoOn && !_isServoOn)
                {
                    // 실제 하드웨어도 OFF이고 플래그도 OFF인 경우
                    return TmOperationResult.Fail("서보모터가 OFF 상태입니다");
                }
                
                // 실제 하드웨어가 ON인데 플래그가 OFF인 경우 플래그 업데이트
                if (actualServoOn && !_isServoOn)
                {
                    _isServoOn = true;
                    Log("서보 상태 플래그 업데이트: OFF → ON (하드웨어 상태 확인)", "WARN");
                }

                Log($"서보 이동 시작: X={targetX}, Y={targetY}", "INFO");

                // 서보 이동 시 home 상태 해제 (원점에서 벗어남)
                if (_isHomed)
                {
                    _isHomed = false;
                    Log("서보 이동으로 인한 home 상태 해제", "INFO");
                }

                // 위치 업데이트 및 이동 명령
                _ethercat.Axis1_UD_POS_Update(targetY);
                _ethercat.Axis2_LR_POS_Update(targetX);

                _ethercat.Axis1_UD_Move_Send();
                _ethercat.Axis2_LR_Move_Send();

                if (!waitForCompletion)
                {
                    return TmOperationResult.Ok();
                }

                // 이동 완료 대기
                return WaitForServoMoveComplete(targetX, targetY);
            }
            catch (Exception ex)
            {
                Log($"서보 이동 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"서보 이동 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 특정 Region으로 이동 (상승 높이 - 기본값, 드롭 준비용)
        /// </summary>
        public TmOperationResult MoveToRegion(EquipmentRegion region, bool waitForCompletion = true)
        {
            var (x, y) = GetRegionPosition(region);
            return MoveToPosition(x, y, waitForCompletion);
        }

        /// <summary>
        /// 특정 Region의 픽업 위치로 이동 (하강 높이)
        /// 웨이퍼를 꺼내기 위해 하강 위치(실린더 전진 안전 위치)로 이동
        /// </summary>
        public TmOperationResult MoveToRegionForPickup(EquipmentRegion region, bool waitForCompletion = true)
        {
            var x = GetRegionX(region);
            var y = GetRegionDescendHeight(region);  // 하강 높이 (안착 - 20000)
            Log($"픽업 위치로 이동: {region} (X={x}, Y={y} 하강높이)", "INFO");
            return MoveToPosition(x, y, waitForCompletion);
        }

        /// <summary>
        /// 특정 Region의 드롭 위치로 이동 (상승 높이)
        /// 웨이퍼를 놓기 위해 상승 위치로 이동
        /// </summary>
        public TmOperationResult MoveToRegionForDropoff(EquipmentRegion region, bool waitForCompletion = true)
        {
            var x = GetRegionX(region);
            var y = GetRegionRaiseHeight(region);  // 상승 높이
            Log($"드롭 위치로 이동: {region} (X={x}, Y={y} 상승높이)", "INFO");
            return MoveToPosition(x, y, waitForCompletion);
        }

        /// <summary>
        /// Axis1만 이동 (상하)
        /// 주의: 상하 이동은 실린더 상태와 무관하게 동작 가능
        ///       (웨이퍼 픽업 시 실린더 전진 상태에서 상승해야 함)
        ///       좌우 이동만 실린더 인터락 적용
        /// </summary>
        public TmOperationResult MoveAxis1(long targetY, bool waitForCompletion = true)
        {
            try
            {
                if (!_hasEverHomed)
                {
                    return TmOperationResult.Fail("원점복귀가 완료되지 않았습니다");
                }

                // 상하 이동은 실린더 상태와 무관 (좌우 이동만 인터락)
                // 웨이퍼 픽업 시퀀스: 실린더 전진 → 하강 → 진공ON → 상승 → 실린더 후진

                if (!_isServoOn)
                {
                    return TmOperationResult.Fail("서보모터가 OFF 상태입니다");
                }

                Log($"Axis1 이동: Y={targetY}", "INFO");

                // 서보 이동 시 home 상태 해제 (원점에서 벗어남)
                if (_isHomed)
                {
                    _isHomed = false;
                    Log("서보 이동으로 인한 home 상태 해제", "INFO");
                }

                _ethercat.Axis1_UD_POS_Update(targetY);
                _ethercat.Axis1_UD_Move_Send();

                if (!waitForCompletion)
                {
                    return TmOperationResult.Ok();
                }

                return WaitForAxis1MoveComplete(targetY);
            }
            catch (Exception ex)
            {
                Log($"Axis1 이동 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"Axis1 이동 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 서보 이동 완료 대기
        /// </summary>
        private TmOperationResult WaitForServoMoveComplete(long targetX, long targetY)
        {
            var timeout = DateTime.Now.AddMilliseconds(SERVO_MOVE_TIMEOUT_MS);

            while (DateTime.Now < timeout)
            {
                _ethercat.ThrowIfEmergencyStopped();
                UpdateCurrentPositions();

                // PP_D (Position Profile Done) 상태 확인
                bool axis1Done = _ethercat.Axis1_Status("PP_D");
                bool axis2Done = _ethercat.Axis2_Status("PP_D");

                // 목표 위치 도달 확인 (허용 오차 내)
                bool axis1AtTarget = Math.Abs(_currentAxis1Pos - targetY) < 1000;
                bool axis2AtTarget = Math.Abs(_currentAxis2Pos - targetX) < 1000;

                if ((axis1Done && axis2Done) || (axis1AtTarget && axis2AtTarget))
                {
                    Log($"서보 이동 완료: X={_currentAxis2Pos}, Y={_currentAxis1Pos}", "INFO");
                    return TmOperationResult.Ok();
                }

                // 하드웨어 폴링 주기 (AppSettings에서 설정 가능)
                System.Threading.Thread.Sleep(HARDWARE_POLLING_INTERVAL_MS);
            }

            return TmOperationResult.Fail("서보 이동 타임아웃");
        }

        /// <summary>
        /// Axis1 이동 완료 대기
        /// </summary>
        private TmOperationResult WaitForAxis1MoveComplete(long targetY)
        {
            var timeout = DateTime.Now.AddMilliseconds(SERVO_MOVE_TIMEOUT_MS);

            while (DateTime.Now < timeout)
            {
                _ethercat.ThrowIfEmergencyStopped();
                UpdateCurrentPositions();

                bool axis1Done = _ethercat.Axis1_Status("PP_D");
                bool axis1AtTarget = Math.Abs(_currentAxis1Pos - targetY) < 1000;

                if (axis1Done || axis1AtTarget)
                {
                    Log($"Axis1 이동 완료: Y={_currentAxis1Pos}", "INFO");
                    return TmOperationResult.Ok();
                }

                // 하드웨어 폴링 주기 (AppSettings에서 설정 가능)
                System.Threading.Thread.Sleep(HARDWARE_POLLING_INTERVAL_MS);
            }

            return TmOperationResult.Fail("Axis1 이동 타임아웃");
        }

        /// <summary>
        /// 현재 위치 업데이트
        /// </summary>
        public void UpdateCurrentPositions()
        {
            try
            {
                var axis1PosStr = _ethercat.Axis1_is_PosData();
                var axis2PosStr = _ethercat.Axis2_is_PosData();

                if (!string.IsNullOrEmpty(axis1PosStr) && axis1PosStr != "-")
                {
                    _currentAxis1Pos = long.Parse(axis1PosStr);
                }

                if (!string.IsNullOrEmpty(axis2PosStr) && axis2PosStr != "-")
                {
                    _currentAxis2Pos = long.Parse(axis2PosStr);
                }
            }
            catch (Exception ex)
            {
                Log($"위치 읽기 오류: {ex.Message}", "WARN");
            }
        }

        /// <summary>
        /// Region에 해당하는 위치 좌표 반환 (X좌표, 상승 Y좌표)
        /// 이동 시에는 상승 위치로 이동
        /// </summary>
        private (long x, long y) GetRegionPosition(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return (_positions.FoupA_X, _positions.FoupA_RaiseY[_currentFoupASlot]);
                case EquipmentRegion.FoupB:
                    return (_positions.FoupB_X, _positions.FoupB_RaiseY[_currentFoupBSlot]);
                case EquipmentRegion.ChamberA:
                    return (_positions.ChamberA_X, _positions.ChamberA_RaiseY);
                case EquipmentRegion.ChamberB:
                    return (_positions.ChamberB_X, _positions.ChamberB_RaiseY);
                case EquipmentRegion.ChamberC:
                    return (_positions.ChamberC_X, _positions.ChamberC_RaiseY);
                case EquipmentRegion.TM:
                default:
                    return (_positions.Home_X, _positions.Home_Y);
            }
        }

        /// <summary>
        /// Region에 해당하는 안착(Land) 높이 반환 (웨이퍼 픽업/드롭 시)
        /// </summary>
        public long GetRegionLandHeight(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return _positions.FoupA_LandY[_currentFoupASlot];
                case EquipmentRegion.FoupB:
                    return _positions.FoupB_LandY[_currentFoupBSlot];
                case EquipmentRegion.ChamberA:
                    return _positions.ChamberA_LandY;
                case EquipmentRegion.ChamberB:
                    return _positions.ChamberB_LandY;
                case EquipmentRegion.ChamberC:
                    return _positions.ChamberC_LandY;
                default:
                    return _positions.Home_Y;
            }
        }

        /// <summary>
        /// Region에 해당하는 상승(Raise) 높이 반환 (이동 전 상승 시)
        /// </summary>
        public long GetRegionRaiseHeight(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return _positions.FoupA_RaiseY[_currentFoupASlot];
                case EquipmentRegion.FoupB:
                    return _positions.FoupB_RaiseY[_currentFoupBSlot];
                case EquipmentRegion.ChamberA:
                    return _positions.ChamberA_RaiseY;
                case EquipmentRegion.ChamberB:
                    return _positions.ChamberB_RaiseY;
                case EquipmentRegion.ChamberC:
                    return _positions.ChamberC_RaiseY;
                default:
                    return _positions.Home_Y;
            }
        }

        /// <summary>
        /// Region에 해당하는 하강(Descend) 높이 반환 (실린더 전진/후진용)
        /// 안착 위치보다 20000 낮은 위치로, 실린더 동작 시 웨이퍼 파손 방지
        /// </summary>
        public long GetRegionDescendHeight(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return _positions.FoupA_DescendY[_currentFoupASlot];
                case EquipmentRegion.FoupB:
                    return _positions.FoupB_DescendY[_currentFoupBSlot];
                case EquipmentRegion.ChamberA:
                    return _positions.ChamberA_DescendY;
                case EquipmentRegion.ChamberB:
                    return _positions.ChamberB_DescendY;
                case EquipmentRegion.ChamberC:
                    return _positions.ChamberC_DescendY;
                default:
                    return _positions.Home_Y;
            }
        }

        /// <summary>
        /// Region에 해당하는 X 좌표 반환
        /// </summary>
        public long GetRegionX(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return _positions.FoupA_X;
                case EquipmentRegion.FoupB:
                    return _positions.FoupB_X;
                case EquipmentRegion.ChamberA:
                    return _positions.ChamberA_X;
                case EquipmentRegion.ChamberB:
                    return _positions.ChamberB_X;
                case EquipmentRegion.ChamberC:
                    return _positions.ChamberC_X;
                default:
                    return _positions.Home_X;
            }
        }

        #endregion

        #region Cylinder Control

        /// <summary>
        /// 실린더 전진
        /// </summary>
        public TmOperationResult ExtendCylinder()
        {
            try
            {
                Log("실린더 전진", "INFO");

                _ethercat.Digital_Output(CYLINDER_BACKWARD_OUTPUT, false);
                _ethercat.Digital_Output(CYLINDER_FORWARD_OUTPUT, true);

                // 전진 완료 대기
                var timeout = DateTime.Now.AddMilliseconds(CYLINDER_ACTION_TIMEOUT_MS);
                while (DateTime.Now < timeout)
                {
                    _ethercat.ThrowIfEmergencyStopped();
                    if (CheckCylinderExtended())
                    {
                        _cylinderForward = true;
                        Log("실린더 전진 완료", "INFO");
                        return TmOperationResult.Ok();
                    }
                    // 속도 최적화: 폴링 주기 단축 (50ms → 30ms, 더 빠른 완료 감지)
                    System.Threading.Thread.Sleep(30);
                }

                return TmOperationResult.Fail("실린더 전진 타임아웃");
            }
            catch (Exception ex)
            {
                Log($"실린더 전진 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"실린더 전진 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 실린더 후진
        /// </summary>
        public TmOperationResult RetractCylinder()
        {
            try
            {
                Log("실린더 후진", "INFO");

                _ethercat.Digital_Output(CYLINDER_FORWARD_OUTPUT, false);
                _ethercat.Digital_Output(CYLINDER_BACKWARD_OUTPUT, true);

                // 후진 완료 대기
                var timeout = DateTime.Now.AddMilliseconds(CYLINDER_ACTION_TIMEOUT_MS);
                while (DateTime.Now < timeout)
                {
                    _ethercat.ThrowIfEmergencyStopped();
                    if (CheckCylinderRetracted())
                    {
                        _cylinderForward = false;
                        Log("실린더 후진 완료", "INFO");
                        return TmOperationResult.Ok();
                    }
                    // 속도 최적화: 폴링 주기 단축 (50ms → 30ms, 더 빠른 완료 감지)
                    System.Threading.Thread.Sleep(30);
                }

                return TmOperationResult.Fail("실린더 후진 타임아웃");
            }
            catch (Exception ex)
            {
                Log($"실린더 후진 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"실린더 후진 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 실린더 전진 상태 확인 (센서)
        /// </summary>
        public bool CheckCylinderExtended()
        {
            try
            {
                return _ethercat.Digital_Input(CYLINDER_FORWARD_SENSOR);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 실린더 후진 상태 확인 (센서)
        /// </summary>
        public bool CheckCylinderRetracted()
        {
            try
            {
                return _ethercat.Digital_Input(CYLINDER_BACKWARD_SENSOR);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Vacuum Control

        /// <summary>
        /// 진공 흡기 ON (웨이퍼 흡착)
        /// </summary>
        public TmOperationResult SetVacuumOn()
        {
            try
            {
                Log("진공 흡기 ON", "INFO");

                _ethercat.Digital_Output(VACUUM_EXHAUST_OUTPUT, false);  // 배기 OFF
                _ethercat.Digital_Output(VACUUM_INTAKE_OUTPUT, true);    // 흡기 ON

                // 진공 안정화 대기
                System.Threading.Thread.Sleep(VACUUM_STABILIZE_DELAY_MS);

                _isVacuumOn = true;
                Log("진공 흡기 완료", "INFO");
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"진공 흡기 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"진공 흡기 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 진공 OFF (웨이퍼 놓기 전 단계)
        /// </summary>
        public TmOperationResult SetVacuumOff()
        {
            try
            {
                Log("진공 OFF", "INFO");

                _ethercat.Digital_Output(VACUUM_INTAKE_OUTPUT, false);   // 흡기 OFF

                _isVacuumOn = false;
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"진공 OFF 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"진공 OFF 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 배기 실행 (웨이퍼 놓기)
        /// </summary>
        public TmOperationResult PerformExhaust()
        {
            try
            {
                Log("배기 시작", "INFO");

                // 흡기 OFF
                _ethercat.Digital_Output(VACUUM_INTAKE_OUTPUT, false);

                // 배기 ON
                _ethercat.Digital_Output(VACUUM_EXHAUST_OUTPUT, true);

                // 배기 지속
                System.Threading.Thread.Sleep(EXHAUST_DURATION_MS);

                // 배기 OFF
                _ethercat.Digital_Output(VACUUM_EXHAUST_OUTPUT, false);

                _isVacuumOn = false;
                Log("배기 완료", "INFO");
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"배기 오류: {ex.Message}", "ERROR");
                return TmOperationResult.Fail($"배기 오류: {ex.Message}");
            }
        }

        #endregion

        #region High-Level Operations (웨이퍼 픽업/드롭)

        /// <summary>
        /// 웨이퍼 픽업 수행 (하강 위치에서 시작)
        /// 
        /// 픽업 순서:
        /// 1. 하강 위치로 이동 (실린더 전진 전 안전 위치)
        /// 2. 실린더 전진 (블레이드가 웨이퍼 아래로 진입)
        /// 3. 안착 위치로 상승 (블레이드가 웨이퍼와 접촉)
        /// 4. 진공 ON (웨이퍼 흡착)
        /// 5. 상승 위치로 이동 (웨이퍼 들어올림)
        /// 6. 실린더 후진
        /// </summary>
        public TmOperationResult PickupWafer(EquipmentRegion region)
        {
            try
            {
                Log($"웨이퍼 픽업 시작: {region}", "INFO");

                var descendHeight = GetRegionDescendHeight(region);
                var landHeight = GetRegionLandHeight(region);
                var raiseHeight = GetRegionRaiseHeight(region);

                // 1. 하강 위치로 이동 (실린더 전진 전 안전 위치)
                var result = MoveAxis1(descendHeight, true);
                if (!result.Success) return result;

                // 2. 실린더 전진 (블레이드가 웨이퍼 아래로 진입)
                result = ExtendCylinder();
                if (!result.Success) return result;

                // 3. 안착 위치로 상승 (블레이드가 웨이퍼와 접촉)
                result = MoveAxis1(landHeight, true);
                if (!result.Success)
                {
                    RetractCylinder();
                    return result;
                }

                // 4. 진공 ON (웨이퍼 흡착)
                result = SetVacuumOn();
                if (!result.Success)
                {
                    RetractCylinder();
                    return result;
                }

                // 5. 상승 위치로 이동 (웨이퍼 들어올림)
                result = MoveAxis1(raiseHeight, true);
                if (!result.Success)
                {
                    SetVacuumOff();
                    RetractCylinder();
                    return result;
                }

                // 6. 실린더 후진
                result = RetractCylinder();
                if (!result.Success)
                {
                    SetVacuumOff();
                    return result;
                }

                _isCarryingWafer = true;
                Log($"웨이퍼 픽업 완료: {region}", "INFO");
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"웨이퍼 픽업 오류: {ex.Message}", "ERROR");
                // 비상 복구
                SetVacuumOff();
                RetractCylinder();
                return TmOperationResult.Fail($"웨이퍼 픽업 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 웨이퍼 드롭 수행 (상승 위치에서 시작)
        /// 
        /// 드롭 순서:
        /// 1. (이미 상승 위치에 있어야 함)
        /// 2. 실린더 전진 (웨이퍼를 들고 진입)
        /// 3. 안착 위치로 하강 (웨이퍼 내려놓기)
        /// 4. 진공 OFF + 배기 (웨이퍼 분리)
        /// 5. 하강 위치로 이동 (블레이드가 웨이퍼 아래로 - 안전 위치)
        /// 6. 실린더 후진 (안전하게 빠져나옴)
        /// </summary>
        public TmOperationResult DropWafer(EquipmentRegion region)
        {
            try
            {
                Log($"웨이퍼 드롭 시작: {region}", "INFO");

                var landHeight = GetRegionLandHeight(region);
                var descendHeight = GetRegionDescendHeight(region);

                // 1. 실린더 전진 (웨이퍼를 들고 진입)
                var result = ExtendCylinder();
                if (!result.Success) return result;

                // 2. 안착 위치로 하강 (웨이퍼 내려놓기)
                result = MoveAxis1(landHeight, true);
                if (!result.Success)
                {
                    RetractCylinder();
                    return result;
                }

                // 3. 진공 OFF + 배기 (웨이퍼 분리)
                result = PerformExhaust();
                if (!result.Success)
                {
                    RetractCylinder();
                    return result;
                }

                // 4. 하강 위치로 이동 (블레이드가 웨이퍼 아래로 - 안전 위치)
                result = MoveAxis1(descendHeight, true);
                if (!result.Success)
                {
                    RetractCylinder();
                    return result;
                }

                // 5. 실린더 후진 (안전하게 빠져나옴)
                result = RetractCylinder();
                if (!result.Success) return result;

                _isCarryingWafer = false;
                Log($"웨이퍼 드롭 완료: {region}", "INFO");
                return TmOperationResult.Ok();
            }
            catch (Exception ex)
            {
                Log($"웨이퍼 드롭 오류: {ex.Message}", "ERROR");
                // 비상 복구
                RetractCylinder();
                return TmOperationResult.Fail($"웨이퍼 드롭 오류: {ex.Message}");
            }
        }

        #endregion

        #region Safety Interlocks

        /// <summary>
        /// 이동 전 안전 인터락 확인
        /// </summary>
        public TmOperationResult CheckMoveInterlock()
        {
            if (!_isServoOn)
            {
                return TmOperationResult.Fail("서보모터가 OFF 상태입니다");
            }

            if (!_hasEverHomed)
            {
                return TmOperationResult.Fail("원점복귀가 완료되지 않았습니다");
            }

            if (!CheckCylinderRetracted())
            {
                return TmOperationResult.Fail("실린더가 전진 상태입니다. 이동 전 실린더를 후진해주세요.");
            }

            return TmOperationResult.Ok();
        }

        /// <summary>
        /// 픽업 전 안전 인터락 확인
        /// </summary>
        public TmOperationResult CheckPickupInterlock()
        {
            var moveCheck = CheckMoveInterlock();
            if (!moveCheck.Success) return moveCheck;

            if (_isCarryingWafer)
            {
                return TmOperationResult.Fail("이미 웨이퍼를 보유하고 있습니다");
            }

            return TmOperationResult.Ok();
        }

        /// <summary>
        /// 드롭 전 안전 인터락 확인
        /// </summary>
        public TmOperationResult CheckDropInterlock()
        {
            var moveCheck = CheckMoveInterlock();
            if (!moveCheck.Success) return moveCheck;

            if (!_isCarryingWafer)
            {
                return TmOperationResult.Fail("웨이퍼를 보유하고 있지 않습니다");
            }

            if (!_isVacuumOn)
            {
                return TmOperationResult.Fail("진공이 OFF 상태입니다. 웨이퍼가 떨어졌을 수 있습니다.");
            }

            return TmOperationResult.Ok();
        }

        #endregion

        #region Logging

        private void Log(string message, string level)
        {
            _logCallback?.Invoke($"[TM] {message}", level);
        }

        #endregion
    }

    /// <summary>
    /// TM 동작 결과
    /// </summary>
    public class TmOperationResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        private TmOperationResult(bool success, string errorMessage = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
        }

        public static TmOperationResult Ok()
        {
            return new TmOperationResult(true);
        }

        public static TmOperationResult Fail(string errorMessage)
        {
            return new TmOperationResult(false, errorMessage);
        }
    }
}

