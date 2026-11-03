using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Models;
using SemiconductorUi.Controls;

namespace SemiconductorUi.Controllers
{
    /// <summary>
    /// 웨이퍼 이송 제어를 담당하는 컨트롤러
    /// TM(Transfer Module) 제어 및 큐 관리
    /// </summary>
    public class TransferController : ITransferService
    {
        #region Nested Classes

        /// <summary>
        /// TM 동작 단계
        /// </summary>
        public enum TmPhase
        {
            Idle,

            // === 픽업 위치로 이동 ===
            MoveToPickup,                    // 서보 이동 명령 발행 (하드웨어) / 이동 시작 (시뮬레이션)
            MoveToPickup_WaitHardware,       // 하드웨어 서보 이동 완료 대기

            // === 픽업 도어 처리 ===
            WaitDoorPickupOpen,              // 도어 열기 대기

            // === 픽업 동작 ===
            PickupExtend,                    // 픽업 동작 시작
            PickupExtend_CylinderForward,    // 실린더 전진
            PickupExtend_ServoDown,          // 서보 상승 (하강위치→안착위치, 웨이퍼 접촉)
            PickupExtend_VacuumOn,           // 진공 ON

            // === 픽업 후 복귀 ===
            PickupRetract,                   // 픽업 후퇴 시작
            PickupRetract_ServoUp,           // 서보 상승 (안착위치→상승위치, 웨이퍼 들어올림)
            PickupRetract_CylinderBackward,  // 실린더 후진

            // === 픽업 도어 닫기 ===
            WaitDoorPickupClose,

            // === 드롭오프 위치로 이동 ===
            MoveToDropoff,
            MoveToDropoff_WaitHardware,

            // === 드롭오프 도어 처리 ===
            WaitDoorDropoffOpen,

            // === 드롭오프 동작 ===
            DropoffExtend,
            DropoffExtend_CylinderForward,   // 실린더 전진
            DropoffExtend_ServoDown,         // 서보 하강 (상승위치→안착위치, 웨이퍼 내려놓기)
            DropoffExtend_VacuumOffExhaust,  // 진공 OFF + 배기

            // === 드롭오프 후 복귀 ===
            DropoffRetract,
            DropoffRetract_ServoUp,          // 서보 하강 (안착위치→하강위치, 웨이퍼 아래로)
            DropoffRetract_CylinderBackward, // 실린더 후진

            // === 드롭오프 도어 닫기 ===
            WaitDoorDropoffClose
        }

        /// <summary>
        /// 이송 작업 정보
        /// </summary>
        public class TransferTask
        {
            public Wafer Wafer { get; set; }
            public EquipmentRegion Pickup { get; set; }
            public EquipmentRegion Dropoff { get; set; }
            public ChamberController.ChamberState SourceChamber { get; set; }
            public ChamberController.ChamberState DestinationChamber { get; set; }
            public Action<Wafer> OnCompleted { get; set; }
            public bool FromFoup { get; set; }
            
            /// <summary>
            /// 재시도 횟수 (실패한 작업의 재시도 추적)
            /// </summary>
            public int RetryCount { get; set; } = 0;
            
            /// <summary>
            /// 최대 재시도 횟수
            /// </summary>
            public const int MaxRetryCount = 3;
        }

        #endregion

        #region Fields

        private readonly Queue<TransferTask> _queue = new Queue<TransferTask>();
        private TransferTask _currentTransfer;
        private TmPhase _currentPhase = TmPhase.Idle;
        private int _phaseTicksRemaining;

        #endregion

        #region Properties

        /// <summary>
        /// 현재 이송 작업
        /// </summary>
        public TransferTask CurrentTransfer => _currentTransfer;

        /// <summary>
        /// 현재 TM 단계
        /// </summary>
        public TmPhase CurrentPhase => _currentPhase;

        /// <summary>
        /// 현재 단계 남은 틱 수
        /// </summary>
        public int PhaseTicksRemaining => _phaseTicksRemaining;

        /// <summary>
        /// 큐에 대기 중인 작업 수
        /// </summary>
        public int QueueCount => _queue.Count;

        /// <summary>
        /// 현재 작업 진행 중 여부
        /// </summary>
        public bool IsBusy => _currentPhase != TmPhase.Idle && _currentTransfer != null;

        /// <summary>
        /// 큐가 비어있는지 여부
        /// </summary>
        public bool IsQueueEmpty => _queue.Count == 0;

        #endregion

        #region Constructor

        /// <summary>
        /// TransferController 생성
        /// </summary>
        public TransferController()
        {
        }

        #endregion

        #region Queue Management

        /// <summary>
        /// 이송 작업을 큐에 추가
        /// </summary>
        public void EnqueueTransfer(TransferTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            _queue.Enqueue(task);
        }

        /// <summary>
        /// 다음 이송 작업 시작
        /// </summary>
        /// <returns>시작된 작업, 없으면 null</returns>
        public TransferTask StartNextTransfer()
        {
            if (_queue.Count == 0)
            {
                _currentPhase = TmPhase.Idle;
                _currentTransfer = null;
                return null;
            }

            // 우선순위에 따라 작업 선택 (현재는 FIFO)
            _currentTransfer = _queue.Dequeue();
            _currentPhase = TmPhase.MoveToPickup;
            _phaseTicksRemaining = 0;

            return _currentTransfer;
        }

        /// <summary>
        /// 큐 초기화
        /// </summary>
        public void ClearQueue()
        {
            _queue.Clear();
            _currentTransfer = null;
            _currentPhase = TmPhase.Idle;
            _phaseTicksRemaining = 0;
        }

        /// <summary>
        /// 큐에 있는 모든 작업 가져오기 (읽기 전용)
        /// </summary>
        public IEnumerable<TransferTask> GetQueuedTasks()
        {
            return _queue.ToArray();
        }

        #endregion

        #region Phase Management

        /// <summary>
        /// TM 단계 시작
        /// </summary>
        /// <param name="phase">시작할 단계</param>
        /// <param name="ticks">단계 지속 틱 수</param>
        /// <param name="region">관련 Region (선택사항)</param>
        /// <param name="waitForCompletion">완료 대기 여부</param>
        public void BeginPhase(TmPhase phase, int ticks, EquipmentRegion region = EquipmentRegion.TM, bool waitForCompletion = false)
        {
            _currentPhase = phase;
            _phaseTicksRemaining = ticks;
        }

        /// <summary>
        /// 현재 단계 틱 감소
        /// </summary>
        /// <returns>단계 완료 여부</returns>
        public bool DecrementPhaseTick()
        {
            if (_phaseTicksRemaining > 0)
            {
                _phaseTicksRemaining--;
            }

            return _phaseTicksRemaining <= 0;
        }

        /// <summary>
        /// 현재 단계 완료 처리
        /// </summary>
        public void CompleteCurrentPhase()
        {
            _phaseTicksRemaining = 0;
        }

        /// <summary>
        /// 현재 작업 완료 처리
        /// BeforeFinal 로직: OnCompleted는 PerformDropoff()에서 이미 호출되었으므로 여기서는 호출하지 않음
        /// </summary>
        public void CompleteCurrentTransfer()
        {
            if (_currentTransfer != null)
            {
                // BeforeFinal 로직: PerformDropoff()에서 이미 OnCompleted 호출됨
                // 단, PerformDropoff()에서 호출되지 않은 경우(예외 상황)를 대비하여 체크
                // 하지만 일반적으로 FOUP B로 드롭오프 시 PerformDropoff()에서 호출되므로 여기서는 호출하지 않음
                // _currentTransfer.OnCompleted?.Invoke(_currentTransfer.Wafer);  // 제거: PerformDropoff()에서 호출됨
                _currentTransfer = null;
            }

            _currentPhase = TmPhase.Idle;
            _phaseTicksRemaining = 0;
        }

        /// <summary>
        /// Idle 상태로 리셋
        /// </summary>
        public void ResetToIdle()
        {
            _currentPhase = TmPhase.Idle;
            _currentTransfer = null;
            _phaseTicksRemaining = 0;
        }

        #endregion

        #region Transfer Scheduling

        /// <summary>
        /// 챔버 간 이송 작업 스케줄링
        /// </summary>
        public TransferTask ScheduleChamberTransfer(
            ChamberController.ChamberState source,
            ChamberController.ChamberState destination,
            Wafer wafer)
        {
            if (source == null || destination == null || wafer == null)
            {
                return null;
            }

            var task = new TransferTask
            {
                Wafer = wafer,
                Pickup = source.Region,
                Dropoff = destination.Region,
                SourceChamber = source,
                DestinationChamber = destination,
                FromFoup = false
            };

            EnqueueTransfer(task);
            return task;
        }

        /// <summary>
        /// FOUP에서 챔버로 이송 작업 스케줄링
        /// </summary>
        public TransferTask ScheduleTransferFromFoup(
            EquipmentRegion foupRegion,
            ChamberController.ChamberState destination,
            Wafer wafer)
        {
            if (destination == null || wafer == null)
            {
                return null;
            }

            var task = new TransferTask
            {
                Wafer = wafer,
                Pickup = foupRegion,
                Dropoff = destination.Region,
                SourceChamber = null,
                DestinationChamber = destination,
                FromFoup = true
            };

            EnqueueTransfer(task);
            return task;
        }

        /// <summary>
        /// 챔버에서 FOUP로 이송 작업 스케줄링
        /// </summary>
        public TransferTask ScheduleTransferToFoup(
            ChamberController.ChamberState source,
            EquipmentRegion foupRegion,
            Wafer wafer)
        {
            if (source == null || wafer == null)
            {
                return null;
            }

            var task = new TransferTask
            {
                Wafer = wafer,
                Pickup = source.Region,
                Dropoff = foupRegion,
                SourceChamber = source,
                DestinationChamber = null,
                FromFoup = false
            };

            EnqueueTransfer(task);
            return task;
        }

        #endregion

        #region Status Checks

        /// <summary>
        /// 특정 챔버가 큐에서 예약되어 있는지 확인
        /// BeforeFinal 로직으로 복원: 직접 참조 비교 사용
        /// 해당 챔버로 향하는 작업(DestinationChamber)만 체크
        /// </summary>
        public bool IsChamberReservedInQueue(ChamberController.ChamberState chamber)
        {
            if (chamber == null)
            {
                return false;
            }

            // 현재 진행 중인 작업이 해당 챔버로 향하는지 확인 (BeforeFinal과 동일: 직접 참조 비교)
            if (_currentTransfer != null && _currentTransfer.DestinationChamber == chamber)
            {
                return true;
            }

            // 큐에 해당 챔버로 향하는 작업이 있는지 확인 (BeforeFinal과 동일: 직접 참조 비교)
            return _queue.Any(t => t.DestinationChamber == chamber);
        }

        /// <summary>
        /// 특정 Region이 큐에서 사용 중인지 확인
        /// </summary>
        public bool IsRegionInUse(EquipmentRegion region)
        {
            if (_currentTransfer != null)
            {
                if (_currentTransfer.Pickup == region || _currentTransfer.Dropoff == region)
                {
                    return true;
                }
            }

            return _queue.Any(t => t.Pickup == region || t.Dropoff == region);
        }

        #endregion
    }
}

