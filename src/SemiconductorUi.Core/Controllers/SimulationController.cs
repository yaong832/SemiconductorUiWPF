using System;

namespace SemiconductorUi.Controllers
{
    /// <summary>
    /// 시뮬레이션 상태 및 타이머 관리를 담당하는 컨트롤러
    /// </summary>
    public class SimulationController : ISimulationService, IDisposable
    {
        #region Fields

        private readonly ITickTimer _simulationTimer;
        private bool _isRunning;
        private bool _isPaused;
        private int _elapsedSeconds;
        private bool _disposed = false;

        #endregion

        #region Properties

        /// <summary>
        /// 시뮬레이션 실행 중 여부
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 시뮬레이션 일시정지 여부
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 경과 시간 (초)
        /// </summary>
        public int ElapsedSeconds => _elapsedSeconds;

        /// <summary>
        /// 시뮬레이션 타이머
        /// </summary>
        public ITickTimer SimulationTimer => _simulationTimer;

        /// <summary>
        /// 타이머가 실행 중인지 여부
        /// </summary>
        public bool IsTimerRunning => _simulationTimer?.Enabled ?? false;

        #endregion

        #region Constructor

        /// <summary>
        /// SimulationController 생성
        /// </summary>
        /// <param name="timer">UI 스레드 타이머 (WPF: DispatcherTimer 어댑터)</param>
        /// <param name="tickHandler">타이머 Tick 이벤트 핸들러</param>
        public SimulationController(ITickTimer timer, EventHandler tickHandler)
        {
            _simulationTimer = timer ?? throw new ArgumentNullException(nameof(timer));
            _simulationTimer.Tick += tickHandler;
            Reset();
        }

        #endregion

        #region State Management

        /// <summary>
        /// 시뮬레이션 상태 초기화
        /// </summary>
        public void Reset()
        {
            _isRunning = false;
            _isPaused = false;
            _elapsedSeconds = 0;
            _simulationTimer?.Stop();
        }

        /// <summary>
        /// 경과 시간 증가
        /// </summary>
        public void IncrementElapsedTime()
        {
            _elapsedSeconds++;
        }

        /// <summary>
        /// 경과 시간 설정
        /// </summary>
        public void SetElapsedTime(int seconds)
        {
            _elapsedSeconds = seconds;
        }

        #endregion

        #region Control Methods

        /// <summary>
        /// 시뮬레이션 시작
        /// </summary>
        /// <param name="isHardwareMode">하드웨어 모드 여부</param>
        public void Start(bool isHardwareMode)
        {
            if (_isRunning && !_isPaused)
            {
                return; // 이미 실행 중
            }

            _isRunning = true;
            _isPaused = false;

            // 하드웨어 모드에서는 더 빠른 타이머 주기 사용
            if (isHardwareMode)
            {
                _simulationTimer.Interval = AppSettings.HardwareModeTickMilliseconds;
            }
            else
            {
                _simulationTimer.Interval = AppSettings.SimulationTickMilliseconds;
            }

            _simulationTimer.Start();
        }

        /// <summary>
        /// 시뮬레이션 일시정지
        /// </summary>
        public void Pause()
        {
            if (!_isRunning || _isPaused)
            {
                return;
            }

            _isPaused = true;
            _simulationTimer.Stop();
        }

        /// <summary>
        /// 시뮬레이션 재개
        /// </summary>
        /// <param name="isHardwareMode">하드웨어 모드 여부</param>
        public void Resume(bool isHardwareMode)
        {
            if (!_isRunning || !_isPaused)
            {
                return;
            }

            _isPaused = false;

            // 하드웨어 모드에서는 더 빠른 타이머 주기 사용
            if (isHardwareMode)
            {
                _simulationTimer.Interval = AppSettings.HardwareModeTickMilliseconds;
            }
            else
            {
                _simulationTimer.Interval = AppSettings.SimulationTickMilliseconds;
            }

            _simulationTimer.Start();
        }

        /// <summary>
        /// 시뮬레이션 정지
        /// </summary>
        public void Stop()
        {
            _simulationTimer.Stop();
            _isRunning = false;
            _isPaused = false;
        }

        /// <summary>
        /// 시뮬레이션 중단 (에러 상태)
        /// </summary>
        public void Abort()
        {
            _simulationTimer.Stop();
            _isRunning = false;
            _isPaused = false;
        }

        #endregion

        #region Timer Management

        /// <summary>
        /// 타이머 간격 설정
        /// </summary>
        public void SetInterval(int milliseconds)
        {
            if (_simulationTimer != null)
            {
                _simulationTimer.Interval = milliseconds;
            }
        }

        /// <summary>
        /// 타이머 시작
        /// </summary>
        public void StartTimer()
        {
            _simulationTimer?.Start();
        }

        /// <summary>
        /// 타이머 정지
        /// </summary>
        public void StopTimer()
        {
            _simulationTimer?.Stop();
        }

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
                    _simulationTimer?.Stop();
                    (_simulationTimer as IDisposable)?.Dispose();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}

