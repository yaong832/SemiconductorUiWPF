using System.Collections.Generic;
using SemiconductorUi.Controllers;

namespace SemiconductorUi
{
    /// <summary>
    /// 챔버 상태 및 공정 제어 서비스 인터페이스
    /// </summary>
    public interface IChamberService
    {
        /// <summary>
        /// Chamber A 상태
        /// </summary>
        ChamberController.ChamberState ChamberA { get; }

        /// <summary>
        /// Chamber B 상태
        /// </summary>
        ChamberController.ChamberState ChamberB { get; }

        /// <summary>
        /// Chamber C 상태
        /// </summary>
        ChamberController.ChamberState ChamberC { get; }

        /// <summary>
        /// 모든 챔버 상태
        /// </summary>
        IEnumerable<ChamberController.ChamberState> AllChambers { get; }

        /// <summary>
        /// 챔버 상태 초기화
        /// </summary>
        void ResetChamberStates();

        /// <summary>
        /// 공정 단계 시간 업데이트
        /// </summary>
        void UpdateStepDurations(int durationA, int durationB, int durationC);

        /// <summary>
        /// 특정 Unit의 챔버 상태 가져오기
        /// </summary>
        ChamberController.ChamberState GetChamberStateForUnit(string unitKey);

        /// <summary>
        /// 환경 텔레메트리 초기화
        /// </summary>
        void InitializeEnvironmentTelemetry();
    }
}

