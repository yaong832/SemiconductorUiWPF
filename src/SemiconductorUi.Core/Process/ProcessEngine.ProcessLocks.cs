using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Process
{
    // 원본 9e7ca9e까지의 "공정 중 잠금" (Form1.IsProcessActiveOrStarting, 원점복귀 필요 플래그).
    // 공정이 도는 동안이나 TM이 원점복귀로 움직이는 동안에는 로그아웃, EtherCAT 해제, 창 닫기,
    // 서보 ON/OFF, 이송 목록 비우기, 장비 제어 창 수동 조작을 막는다.
    public sealed partial class ProcessEngine
    {
        /// <summary>
        /// 공정이 도는 중(실행·일시정지·알람 대기)이거나, 공정 시작의 서보 ON/원점복귀 중이거나,
        /// 공정이 끝난 뒤(완료·공정 리셋) 원점복귀 중이면 true.
        /// 로그아웃, EtherCAT 연결 해제, 프로그램 창 닫기, 장비 제어 창 수동 조작을 이때 막습니다.
        /// 긴급 정지는 공정을 중단(SimulationRunning = false)하고 원점복귀도 멈추므로 긴급 정지 후에는 막지 않습니다.
        /// </summary>
        public bool IsProcessActiveOrStarting()
        {
            return SimulationRunning || IsHardwareInitInProgress || IsPostProcessHomingInProgress;
        }

        /// <summary>
        /// 공정 시작의 서보 ON/원점복귀, 또는 공정이 끝난 뒤 원점복귀가 백그라운드에서 도는 중이면 true.
        /// 이때는 공정 시작과 공정 리셋을 막습니다 (축이 원점복귀 중인데 이송 명령이나 두 번째 원점복귀가 나가지 않도록).
        /// </summary>
        public bool IsBackgroundHomingInProgress()
        {
            return IsHardwareInitInProgress || IsPostProcessHomingInProgress;
        }

        /// <summary>
        /// 공정이 끝난 뒤 원점복귀를 시작할 때 UI 스레드에서 부릅니다. 끝나면 반드시 EndPostProcessHoming을 불러야 합니다.
        /// </summary>
        internal void BeginPostProcessHoming()
        {
            _postProcessHomingInProgress = true;
            RefreshBackgroundHomingStatus();
        }

        /// <summary>
        /// 공정이 끝난 뒤 원점복귀가 끝났을 때(성공·실패·긴급 정지 모두) 어느 스레드에서나 부릅니다.
        /// </summary>
        internal void EndPostProcessHoming()
        {
            Host.BeginInvoke(() =>
            {
                _postProcessHomingInProgress = false;
                RefreshBackgroundHomingStatus();
            });
        }

        private void RefreshBackgroundHomingStatus()
        {
            if (CurrentProcessState == MainFormViewModel.ProcessState.Idle)
            {
                StatusProcessText = GetIdleStatusText();
            }
            // TM 카드·하단 상태 글자·버튼을 다시 그립니다 (긴급 정지로 끝났을 때 TM 카드가 '원점복귀 중'으로 남지 않게).
            UpdateSimulationUi();
            UpdateProcessControlButtons();
        }

        private string GetIdleStatusText()
        {
            return IsBackgroundHomingInProgress() ? "공정 상태: 대기 · TM 원점복귀 중" : "공정 상태: 대기";
        }

        /// <summary>
        /// 서보가 꺼졌을 때(서보 OFF, 비상정지, 서보 알람, 꺼진 것을 확인했을 때) 부릅니다.
        /// 원점복귀 완료 표시를 지우고, 원점복귀를 다시 끝낼 때까지 TM 컨트롤러가 축 이동을 보내지 않게 합니다.
        /// 다음 공정 시작은 서보 ON·원점복귀부터 합니다 (드라이브의 원점복귀 완료 신호만으로 건너뛰지 않음).
        /// </summary>
        public void RequireRehome(string reason)
        {
            TmHardwareInitialized = false;
            TmHardwareController?.RequireRehome(reason);
        }

        /// <summary>
        /// 서보가 꺼진 뒤 아직 원점복귀를 다시 하지 않았는지 (TM 컨트롤러가 없으면 true).
        /// </summary>
        internal bool IsRehomeRequired()
        {
            return TmHardwareController == null || TmHardwareController.RehomeRequired;
        }
    }
}
