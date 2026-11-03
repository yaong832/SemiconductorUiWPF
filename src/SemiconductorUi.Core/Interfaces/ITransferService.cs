using System.Collections.Generic;
using SemiconductorUi.Controllers;
using SemiconductorUi.Models;
using SemiconductorUi.Controls;

namespace SemiconductorUi
{
    /// <summary>
    /// 웨이퍼 이송 제어 서비스 인터페이스
    /// </summary>
    public interface ITransferService
    {
        /// <summary>
        /// 현재 이송 작업
        /// </summary>
        TransferController.TransferTask CurrentTransfer { get; }

        /// <summary>
        /// 현재 TM 단계
        /// </summary>
        TransferController.TmPhase CurrentPhase { get; }

        /// <summary>
        /// 현재 단계 남은 틱 수
        /// </summary>
        int PhaseTicksRemaining { get; }

        /// <summary>
        /// 큐에 대기 중인 작업 수
        /// </summary>
        int QueueCount { get; }

        /// <summary>
        /// 현재 작업 진행 중 여부
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// 큐가 비어있는지 여부
        /// </summary>
        bool IsQueueEmpty { get; }

        /// <summary>
        /// 이송 작업을 큐에 추가
        /// </summary>
        void EnqueueTransfer(TransferController.TransferTask task);

        /// <summary>
        /// 다음 이송 작업 시작
        /// </summary>
        TransferController.TransferTask StartNextTransfer();

        /// <summary>
        /// 큐 초기화
        /// </summary>
        void ClearQueue();

        /// <summary>
        /// 큐에 있는 모든 작업 가져오기
        /// </summary>
        IEnumerable<TransferController.TransferTask> GetQueuedTasks();

        /// <summary>
        /// TM 단계 시작
        /// </summary>
        void BeginPhase(TransferController.TmPhase phase, int ticks, EquipmentRegion region = EquipmentRegion.TM, bool waitForCompletion = false);

        /// <summary>
        /// 현재 단계 틱 감소
        /// </summary>
        bool DecrementPhaseTick();

        /// <summary>
        /// 현재 작업 완료 처리
        /// </summary>
        void CompleteCurrentTransfer();

        /// <summary>
        /// Idle 상태로 리셋
        /// </summary>
        void ResetToIdle();

        /// <summary>
        /// 챔버 간 이송 작업 스케줄링
        /// </summary>
        TransferController.TransferTask ScheduleChamberTransfer(
            ChamberController.ChamberState source,
            ChamberController.ChamberState destination,
            Wafer wafer);

        /// <summary>
        /// FOUP에서 챔버로 이송 작업 스케줄링
        /// </summary>
        TransferController.TransferTask ScheduleTransferFromFoup(
            EquipmentRegion foupRegion,
            ChamberController.ChamberState destination,
            Wafer wafer);

        /// <summary>
        /// 챔버에서 FOUP로 이송 작업 스케줄링
        /// </summary>
        TransferController.TransferTask ScheduleTransferToFoup(
            ChamberController.ChamberState source,
            EquipmentRegion foupRegion,
            Wafer wafer);

        /// <summary>
        /// 특정 챔버가 큐에서 예약되어 있는지 확인
        /// </summary>
        bool IsChamberReservedInQueue(ChamberController.ChamberState chamber);

        /// <summary>
        /// 특정 Region이 큐에서 사용 중인지 확인
        /// </summary>
        bool IsRegionInUse(EquipmentRegion region);
    }
}

