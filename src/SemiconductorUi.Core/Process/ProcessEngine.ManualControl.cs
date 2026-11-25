using SemiconductorUi.Controls;
using SemiconductorUi.Models;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// 장비 제어 창(원본 EquipmentControlForm)이 장비를 직접 움직인 뒤 메인 화면 그림만 맞추는 곳
    /// (원본 Form1.SetChamberLampUIOnly, UpdateMainLampColorsUIOnly, OnDoorStateChanged 처리).
    /// 하드웨어 출력은 창이 이미 보냈으므로 여기서는 다시 보내지 않는다.
    /// </summary>
    public sealed partial class ProcessEngine
    {
        // 원본 Color.ForestGreen (장비 제어 창 녹색 ON)
        private static readonly ArgbColor ForestGreen = ArgbColor.FromRgb(34, 139, 34);

        /// <summary>원본 SetChamberLampUIOnly: 챔버 램프 그림만 바꾼다</summary>
        public void SetChamberLampVisual(EquipmentRegion region, bool on)
        {
            if (!_chamberLampOn.ContainsKey(region)) return;
            _chamberLampOn[region] = on;
            Host.RefreshUi();
        }

        /// <summary>원본 UpdateMainLampColorsUIOnly + UpdateEquipmentDiagramLampState: 3색 램프 그림만 바꾼다</summary>
        public void SetMainLampVisual(bool red, bool yellow, bool green)
        {
            MainLampRed = red;
            MainLampYellow = yellow;
            MainLampGreen = green ? ForestGreen : (ArgbColor?)null;
            Host.RefreshUi();
        }

        /// <summary>원본 OnDoorStateChanged: ViewModel.SetDoorState + ApplyDoorVisualsForRegion (출력은 보내지 않음)</summary>
        public void SetDoorVisual(EquipmentRegion region, bool open)
        {
            ViewModel?.SetDoorState(region, open);
            ApplyDoorVisualsForRegion(region, animate: true);
        }

        /// <summary>장비 제어 창 원점복귀 성공 (원본 OnHomingRequested)</summary>
        public void MarkHomedByManualControl()
        {
            TmHardwareInitialized = true;
            TmHardwareController?.MarkHomedAfterSuccessfulHoming();
            UpdateServoStatusLabel();
        }

        /// <summary>
        /// 장비 제어 창 수동 조작을 막을지 (원본 ecf.IsAutoProcessActive = form.IsProcessActiveOrStarting).
        /// 로그아웃·연결 해제·종료 차단과 같은 판단: 실행·일시정지·알람 대기, 시작의 서보 ON·원점복귀,
        /// 공정이 끝난 뒤(완료·공정 리셋) 원점복귀 중이면 수동 조작을 막습니다.
        /// </summary>
        public bool IsAutoProcessActive => IsProcessActiveOrStarting();

        /// <summary>원점복귀 절차로 원점에 있다고 기록된 상태 (원본 ecf.IsHomedBySequence)</summary>
        public bool IsHomedBySequence => TmHardwareInitialized;
    }
}
