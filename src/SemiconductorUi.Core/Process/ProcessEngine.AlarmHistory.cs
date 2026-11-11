using System;
using SemiconductorUi.Helpers;
using SemiconductorUi.Services;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// 원본 Form1.AlarmHistory (8baff6e): 알람이 생기는 곳에서 RecordAlarm을, 알람 리셋에서 ClearRecordedAlarms를 부른다.
    /// 기록만 하며 장비 동작, 공정 흐름, 알람 리셋 판정은 바꾸지 않는다.
    /// </summary>
    public partial class ProcessEngine
    {
        private readonly object _alarmHistoryLock = new object();
        private AlarmHistoryService _alarmHistory;

        /// <summary>알람 이력 서비스 (처음 쓸 때 설정으로 만든다. 시험에서는 미리 넣을 수 있다)</summary>
        public AlarmHistoryService AlarmHistory
        {
            get
            {
                lock (_alarmHistoryLock)
                {
                    return _alarmHistory ?? (_alarmHistory = AlarmHistoryService.CreateFromSettings());
                }
            }
            set
            {
                lock (_alarmHistoryLock)
                {
                    _alarmHistory = value;
                }
            }
        }

        /// <summary>알람 발생을 이력에 남긴다. 같은 알람이 이미 걸려 있으면 다시 남기지 않는다. 실패는 무시한다.</summary>
        internal void RecordAlarm(int code, string unit, string detail)
        {
            try
            {
                AlarmHistory.Raise(code, unit, detail);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"알람 이력 기록 실패: {ex.Message}");
            }
        }

        private void RecordEnvironmentAlarm(string unitKey, int item, string detail)
        {
            RecordAlarm(AlarmCodes.ForEnvironment(unitKey, item), EquipmentRegionHelper.FormatUnitLabel(unitKey), detail);
        }

        private void RecordServoFaultAlarms(System.Collections.Generic.IList<int> axes)
        {
            foreach (int axis in axes)
            {
                RecordAlarm(AlarmCodes.ServoFault(axis), "TM", DescribeServoAxes(new[] { axis }));
            }
        }

        /// <summary>알람 리셋 때 부른다. emergencyStopGroupOnly이면 비상정지·서보 알람만 해제한다.</summary>
        internal void ClearRecordedAlarms(bool emergencyStopGroupOnly)
        {
            try
            {
                string user = ViewModel.IsLoggedIn ? ViewModel.CurrentUser : "-";
                int cleared = emergencyStopGroupOnly
                    ? AlarmHistory.ClearEmergencyStopGroup(user)
                    : AlarmHistory.ClearAll(user);
                if (cleared > 0)
                {
                    AddLogMessage($"알람 이력: {cleared}건 해제 (사용자: {user})", "INFO");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"알람 이력 해제 기록 실패: {ex.Message}");
            }
        }

        /// <summary>프로그램 종료 때 이력 파일을 닫는다.</summary>
        public void DisposeAlarmHistory()
        {
            lock (_alarmHistoryLock)
            {
                _alarmHistory?.Dispose();
            }
        }
    }
}
