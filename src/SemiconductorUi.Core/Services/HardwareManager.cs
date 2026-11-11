using System;
using SemiconductorUi.Hardware;
using SemiconductorUi.Controllers;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 하드웨어 초기화, 종료, 연결 상태 관리를 담당하는 클래스
    /// Phase 2.3: 하드웨어 제어 분리
    /// </summary>
    public class HardwareManager : IDisposable
    {
        #region Fields

        private IEthercatDevice _ethercat;
        private TmHardwareController _tmHardwareController;
        private bool _isEthercatConnected;
        private bool _isTmHardwareInitialized;
        private Action<string, string> _logCallback;
        private bool _disposed = false;
        private EthercatCommWatchdog _commWatchdog;
        private volatile bool _isCommUnhealthy;

        #endregion

        #region Properties

        /// <summary>
        /// EtherCAT 연결 상태
        /// </summary>
        public bool IsEthercatConnected => _isEthercatConnected;

        /// <summary>
        /// EtherCAT 인스턴스 (읽기 전용)
        /// </summary>
        public IEthercatDevice EtherCAT => _ethercat;

        /// <summary>
        /// TM 하드웨어 컨트롤러 (읽기 전용)
        /// </summary>
        public TmHardwareController TmHardwareController => _tmHardwareController;

        /// <summary>
        /// TM 하드웨어 초기화 완료 여부
        /// </summary>
        public bool IsTmHardwareInitialized => _isTmHardwareInitialized;

        #endregion

        #region Constructor

        /// <summary>
        /// HardwareManager 생성자
        /// </summary>
        /// <param name="ethercat">EtherCAT 인스턴스</param>
        /// <param name="logCallback">로깅 콜백 함수</param>
        public HardwareManager(IEthercatDevice ethercat, Action<string, string> logCallback)
        {
            _ethercat = ethercat ?? throw new ArgumentNullException(nameof(ethercat));
            _logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));
            _isEthercatConnected = false;
            _isTmHardwareInitialized = false;
        }

        #endregion

        #region EtherCAT Connection Management

        /// <summary>
        /// EtherCAT 연결 시도
        /// </summary>
        /// <returns>연결 성공 여부</returns>
        public bool ConnectEthercat()
        {
            if (_isEthercatConnected)
            {
                Log("이미 EtherCAT에 연결되어 있습니다.", "INFO");
                return true;
            }

            try
            {
                bool connected = _ethercat.CIFX_50RE_Connect();

                if (connected)
                {
                    _isEthercatConnected = true;

                    // ReadData Timer 설정 및 시작
                    _ethercat.ReadData_Send_Start(300); // Timer interval 300ms
                    _ethercat.ReadData_Timer_Start();

                    Log("EtherCAT 연결 성공 - 실제 장비 제어 모드 활성화", "INFO");
                    LogCommunicationStatus();
                    StartCommWatchdog();
                    return true;
                }
                else
                {
                    Log("EtherCAT 연결 실패", "ERROR");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"EtherCAT 연결 오류: {ex.Message}", "ERROR");
                return false;
            }
        }

        /// <summary>
        /// EtherCAT 연결 해제
        /// </summary>
        public void DisconnectEthercat()
        {
            if (!_isEthercatConnected)
            {
                return;
            }

            // 통신 감시를 먼저 멈춘다 (끊긴 채널을 건드리지 않게).
            StopCommWatchdog();

            try
            {
                // TM 하드웨어 컨트롤러 종료
                ShutdownTmHardwareController();

                // ReadData Timer는 DLL에 중지 메서드가 없습니다.
                // 연결을 해제하면 타이머는 돌지만 데이터를 읽지 않습니다.

                // EtherCAT 연결 해제
                if (_ethercat != null)
                {
                    _ethercat.CIFX_50RE_Disconnect();
                }

                _isEthercatConnected = false;
                Log("EtherCAT 연결 해제 완료", "INFO");
            }
            catch (Exception ex)
            {
                Log($"EtherCAT 연결 해제 오류: {ex.Message}", "ERROR");
            }
        }

        #endregion

        #region TM Hardware Controller Management

        /// <summary>
        /// TM 하드웨어 컨트롤러 초기화
        /// </summary>
        /// <returns>초기화 성공 여부</returns>
        public bool InitializeTmHardwareController()
        {
            if (_tmHardwareController != null)
            {
                return true; // 이미 초기화됨
            }

            if (!_isEthercatConnected)
            {
                Log("EtherCAT가 연결되지 않아 TM 하드웨어 컨트롤러를 초기화할 수 없습니다.", "WARN");
                return false;
            }

            try
            {
                _tmHardwareController = new TmHardwareController(_ethercat, Log);

                // TeachingPositions.xml 티칭값 적용 (없으면 코드 기본값)
                var teaching = SemiconductorUi.Repositories.TeachingPositionsRepository.Load();
                _tmHardwareController.Positions.ApplyFrom(teaching);

                Log("TM 하드웨어 컨트롤러 생성 완료 (티칭값 적용됨)", "INFO");
                Log($"  DescendOffset={_tmHardwareController.Positions.DescendOffset}", "INFO");
                Log($"  Chamber A: X={_tmHardwareController.Positions.ChamberA_X}", "INFO");
                Log($"  Chamber B: X={_tmHardwareController.Positions.ChamberB_X}", "INFO");
                Log($"  Chamber C: X={_tmHardwareController.Positions.ChamberC_X}", "INFO");
                Log($"  FOUP A: X={_tmHardwareController.Positions.FoupA_X}", "INFO");
                Log($"  FOUP B: X={_tmHardwareController.Positions.FoupB_X}", "INFO");
                Log($"  Home: X={_tmHardwareController.Positions.Home_X}, Y={_tmHardwareController.Positions.Home_Y}", "INFO");

                return true;
            }
            catch (Exception ex)
            {
                Log($"TM 하드웨어 컨트롤러 생성 오류: {ex.Message}", "ERROR");
                _tmHardwareController = null;
                return false;
            }
        }

        /// <summary>
        /// TM 하드웨어 초기화 및 원점복귀 수행
        /// </summary>
        /// <returns>초기화 및 원점복귀 성공 여부</returns>
        public bool InitializeAndHomeTmHardware()
        {
            if (_tmHardwareController == null)
            {
                Log("TM 하드웨어 컨트롤러가 없습니다", "ERROR");
                return false;
            }

            try
            {
                // 하드웨어 초기화
                var initResult = _tmHardwareController.Initialize();
                if (!initResult.Success)
                {
                    Log($"TM 초기화 실패: {initResult.ErrorMessage}", "ERROR");
                    return false;
                }

                // 원점복귀
                var homeResult = _tmHardwareController.PerformHoming();
                if (!homeResult.Success)
                {
                    Log($"TM 원점복귀 실패: {homeResult.ErrorMessage}", "ERROR");
                    return false;
                }

                _isTmHardwareInitialized = true;
                Log("TM 하드웨어 초기화 및 원점복귀 완료", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                Log($"TM 하드웨어 초기화 오류: {ex.Message}", "ERROR");
                return false;
            }
        }

        /// <summary>
        /// TM 하드웨어 컨트롤러 종료
        /// </summary>
        public void ShutdownTmHardwareController()
        {
            if (_tmHardwareController == null)
            {
                return;
            }

            try
            {
                // 서보 OFF
                if (_isEthercatConnected && _ethercat != null)
                {
                    _ethercat.Axis1_OFF();
                    _ethercat.Axis2_OFF();
                }

                // 진공 OFF (비상정지 중에는 웨이퍼를 놓치지 않도록 진공을 유지합니다)
                if (_isEthercatConnected && _ethercat != null && !_ethercat.IsEmergencyStopped)
                {
                    _ethercat.Digital_Output(14, false); // 진공 OFF
                    _ethercat.Digital_Output(15, false); // 배기 OFF
                }

                _tmHardwareController = null;
                _isTmHardwareInitialized = false;
                Log("TM 하드웨어 컨트롤러 종료 완료", "INFO");
            }
            catch (Exception ex)
            {
                Log($"TM 하드웨어 컨트롤러 종료 오류: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// TM 하드웨어 초기화 상태 리셋
        /// </summary>
        public void ResetTmHardwareInitialized()
        {
            _isTmHardwareInitialized = false;
        }

        /// <summary>
        /// TM 하드웨어 초기화 완료 여부 설정 (원점복귀 성공 시 true)
        /// </summary>
        public void SetTmHardwareInitialized(bool value)
        {
            _isTmHardwareInitialized = value;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 실제 통신 상태를 확인해 로그로 남깁니다.
        /// DLL의 연결 성공은 드라이버가 열렸다는 뜻일 뿐이라, 버스와 입력 읽기를 따로 확인합니다.
        /// 아직 실장비에서 검증 전이므로 연결을 막지 않고 경고만 남깁니다.
        /// </summary>
        private void LogCommunicationStatus()
        {
            try
            {
                var status = _ethercat.CheckCommunication();
                if (status.IsHealthy)
                {
                    Log($"EtherCAT 통신 상태 정상 ({status})", "INFO");
                }
                else
                {
                    Log($"EtherCAT 통신 상태 이상 ({status}) - 장비 배선과 드라이버 설정을 확인하세요.", "WARN");
                }
            }
            catch (Exception ex)
            {
                Log($"EtherCAT 통신 상태 확인 실패: {ex.Message}", "WARN");
            }
        }

        /// <summary>
        /// 로그 기록
        /// </summary>
        private void Log(string message, string level)
        {
            _logCallback?.Invoke(message, level);
        }

        #region 통신 감시 (원본 684c244, 1단계: 기록만)

        /// <summary>통신 감시가 지금 "이상"으로 보고 있는지 (WPF 상단 바 표시용)</summary>
        public bool IsCommUnhealthy => _isCommUnhealthy;

        /// <summary>통신 이상/복구가 바뀔 때 (감시 스레드에서 불린다)</summary>
        public event Action<bool> CommHealthChanged;

        /// <summary>
        /// 1단계 통신 감시를 시작한다: 정해진 주기로 통신 상태를 확인해 이상·복구를 로그에 남기기만 한다.
        /// 장비를 멈추거나 명령을 막지 않는다. EthercatWatchdogIntervalMs가 0이면 시작하지 않는다.
        /// </summary>
        private void StartCommWatchdog()
        {
            StopCommWatchdog();

            int intervalMs = AppSettings.EthercatWatchdogIntervalMs;
            if (intervalMs <= 0)
            {
                Log("EtherCAT 통신 감시 꺼짐 (EthercatWatchdogIntervalMs = 0)", "INFO");
                return;
            }

            try
            {
                int failCount = Math.Max(1, AppSettings.EthercatWatchdogFailCount);
                var summary = TimeSpan.FromMinutes(Math.Max(0, AppSettings.EthercatWatchdogSummaryMinutes));
                _commWatchdog = new EthercatCommWatchdog(_ethercat, intervalMs, failCount, summary, OnCommWatchdogEvent);
                _commWatchdog.Start();
                Log($"EtherCAT 통신 감시 시작: {intervalMs}ms마다 확인, 연속 {failCount}회 이상이면 기록 (1단계: 기록만, 장비는 멈추지 않음)", "INFO");
            }
            catch (Exception ex)
            {
                _commWatchdog = null;
                Log($"EtherCAT 통신 감시 시작 실패: {ex.Message}", "WARN");
            }
        }

        private void StopCommWatchdog()
        {
            var watchdog = _commWatchdog;
            _commWatchdog = null;
            SetCommUnhealthy(false);
            if (watchdog == null)
            {
                return;
            }

            try
            {
                watchdog.Stop();
            }
            catch (Exception ex)
            {
                Log($"EtherCAT 통신 감시 정지 오류: {ex.Message}", "WARN");
            }
        }

        private void SetCommUnhealthy(bool unhealthy)
        {
            if (_isCommUnhealthy == unhealthy) return;
            _isCommUnhealthy = unhealthy;
            try { CommHealthChanged?.Invoke(unhealthy); } catch { }
        }

        /// <summary>감시 스레드에서 불린다. 로그 콜백은 어느 스레드에서 불러도 된다.</summary>
        internal void OnCommWatchdogEvent(EthercatWatchdogEvent evt, EthercatWatchdogState state)
        {
            switch (evt)
            {
                case EthercatWatchdogEvent.FirstCheck:
                    Log(state.IsUnhealthy || state.FailuresInWindow > 0
                        ? $"EtherCAT 통신 감시 첫 확인: 이상 ({state.LastDetail})"
                        : $"EtherCAT 통신 감시 첫 확인: 정상 ({state.LastDetail})",
                        state.FailuresInWindow > 0 ? "WARN" : "INFO");
                    SetCommUnhealthy(state.IsUnhealthy);
                    break;
                case EthercatWatchdogEvent.BecameUnhealthy:
                    Log($"EtherCAT 통신 이상 감지 ({state.LastDetail}) - 1단계라 장비는 멈추지 않습니다. 배선, 드라이브 전원, 드라이버 상태를 확인하세요.", "WARN");
                    SetCommUnhealthy(true);
                    break;
                case EthercatWatchdogEvent.Recovered:
                    Log($"EtherCAT 통신 복구 ({state.LastDetail}), 이상 지속 {state.LastUnhealthyDuration.TotalSeconds:0.0}초", "INFO");
                    SetCommUnhealthy(false);
                    break;
                case EthercatWatchdogEvent.Summary:
                    Log($"EtherCAT 통신 감시 요약: {state.SummaryChecks}회 확인, 이상 {state.SummaryFailures}회 ({(state.IsUnhealthy ? "지금 이상" : "지금 정상")})",
                        state.SummaryFailures > 0 ? "WARN" : "INFO");
                    break;
            }
        }

        #endregion

        #endregion

        #region Cleanup

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 리소스 정리 (protected)
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 해제할지 여부</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 관리되는 리소스 해제
                    DisconnectEthercat();
                    ShutdownTmHardwareController();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}

