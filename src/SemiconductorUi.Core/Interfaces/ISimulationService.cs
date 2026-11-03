using System;


namespace SemiconductorUi
{
    /// <summary>
    /// 시뮬레이션 상태 및 타이머 관리 서비스 인터페이스
    /// </summary>
    public interface ISimulationService
    {
        /// <summary>
        /// 시뮬레이션 실행 중 여부
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 시뮬레이션 일시정지 여부
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 경과 시간 (초)
        /// </summary>
        int ElapsedSeconds { get; }

        /// <summary>
        /// 시뮬레이션 타이머
        /// </summary>
        ITickTimer SimulationTimer { get; }

        /// <summary>
        /// 타이머가 실행 중인지 여부
        /// </summary>
        bool IsTimerRunning { get; }

        /// <summary>
        /// 시뮬레이션 상태 초기화
        /// </summary>
        void Reset();

        /// <summary>
        /// 경과 시간 증가
        /// </summary>
        void IncrementElapsedTime();

        /// <summary>
        /// 경과 시간 설정
        /// </summary>
        void SetElapsedTime(int seconds);

        /// <summary>
        /// 시뮬레이션 시작
        /// </summary>
        void Start(bool isHardwareMode);

        /// <summary>
        /// 시뮬레이션 일시정지
        /// </summary>
        void Pause();

        /// <summary>
        /// 시뮬레이션 재개
        /// </summary>
        void Resume(bool isHardwareMode);

        /// <summary>
        /// 시뮬레이션 정지
        /// </summary>
        void Stop();

        /// <summary>
        /// 시뮬레이션 중단 (에러 상태)
        /// </summary>
        void Abort();

        /// <summary>
        /// 타이머 간격 설정
        /// </summary>
        void SetInterval(int milliseconds);

        /// <summary>
        /// 타이머 시작
        /// </summary>
        void StartTimer();

        /// <summary>
        /// 타이머 정지
        /// </summary>
        void StopTimer();
    }
}

