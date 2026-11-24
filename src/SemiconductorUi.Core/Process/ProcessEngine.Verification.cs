using System;
using System.Collections.Generic;
using System.Globalization;
using SemiconductorUi.Controllers;
using SemiconductorUi.Helpers;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    /// <summary>검증 항목 하나의 결과</summary>
    public enum InterlockCheckState
    {
        Pass,
        Fail,

        /// <summary>
        /// 챔버 환경 데이터가 아직 없음. 원본은 이 줄을 FAIL로 보이지만 인터락 판정(EvaluateInterlocks)에는 넣지 않는다.
        /// WPF는 "데이터 없음"으로 따로 보여 요약과 목록이 서로 어긋나 보이지 않게 한다.
        /// </summary>
        NoData
    }

    /// <summary>원본 Form1.BuildVerificationChecklist의 (name, ok) 한 줄 + WPF에서 덧붙인 현재 값 설명</summary>
    public sealed class InterlockCheck
    {
        public InterlockCheck(string name, InterlockCheckState state, string detail)
        {
            Name = name;
            State = state;
            Detail = detail ?? "";
        }

        /// <summary>항목 이름 (원본 그대로, 예: "Chamber A 압력 범위")</summary>
        public string Name { get; }

        public InterlockCheckState State { get; }

        /// <summary>원본 ok 값 (NoData는 원본처럼 false)</summary>
        public bool Ok => State == InterlockCheckState.Pass;

        /// <summary>현재 값과 기준 (WPF 추가, 예: "PV 0.0051 Torr / 기준 ≤ 0.025 Torr")</summary>
        public string Detail { get; }
    }

    /// <summary>이송 대기열 한 줄 (원본 GetTransferQueueLines를 화면에서 나눠 보이도록 쪼갠 것)</summary>
    public sealed class TransferQueueEntry
    {
        /// <summary>0이면 진행 중인 작업, 1부터 대기 순서</summary>
        public int Order { get; set; }
        public bool IsCurrent => Order == 0;
        public string From { get; set; }
        public string To { get; set; }
        public int? WaferId { get; set; }
        public int RetryCount { get; set; }
    }

    /// <summary>
    /// 원본 Form1 운전 화면의 검증·이송 탭 훅 (Form1.cs RunVerificationChecks, EvaluateInterlocks, BuildVerificationChecklist,
    /// AutoPlanTransferIfNeeded, ClearTransferQueue, GetTransferQueueLines, NavigateToSection(Operate)).
    /// 판정 기준과 로그 문구는 원본 그대로다.
    /// </summary>
    public sealed partial class ProcessEngine
    {
        /// <summary>원본 인터락 기준: 목표 1 Torr 미만이면 목표의 5배까지, 그 밖에는 목표 ±15 Torr</summary>
        public const double InterlockPressureAbsTorr = 15.0;
        public const double InterlockLowPressureRatio = 5.0;
        public const double InterlockLowPressureLimitTorr = 1.0;

        /// <summary>원본 인터락 기준: 목표 ±10°C</summary>
        public const double InterlockTemperatureC = 10.0;

        private string _lastOperateInterlockWarning;

        private static bool IsPressureInterlockOk(double pv, double target)
        {
            return target < InterlockLowPressureLimitTorr
                ? pv <= target * InterlockLowPressureRatio
                : Math.Abs(pv - target) <= InterlockPressureAbsTorr;
        }

        private static bool IsTemperatureInterlockOk(double pv, double target)
        {
            return Math.Abs(pv - target) <= InterlockTemperatureC;
        }

        private static string FormatTorr(double value)
        {
            return value < InterlockLowPressureLimitTorr
                ? value.ToString("0.####", CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>원본 Form1.EvaluateInterlocks: 통과하지 못한 까닭을 ", "로 이어 돌려준다.</summary>
        public bool EvaluateInterlocks(out string reasons)
        {
            var failed = new List<string>();
            // EtherCAT 연결
            if (!EthercatConnected)
            {
                failed.Add("EtherCAT 미연결");
            }
            // FOUP 장착 상태
            if (!ViewModel.IsFoupMounted)
            {
                failed.Add("FOUP 미장착");
            }
            // 장비 에러/알람 상태
            if (CurrentProcessState == ProcessState.Error || HasAlarm)
            {
                failed.Add("장비 알람/에러 상태");
            }
            // 챔버 진공/압력(예: PMB가 진공 요구): 스펙 대비 편차 체크
            foreach (var unit in ChamberUnitKeys)
            {
                if (ChamberEnvSpecs.TryGetValue(unit, out var spec) && ChamberEnvLive.TryGetValue(unit, out var live))
                {
                    // 간단 기준: 목표 1 Torr 미만인 경우 5x 이상이면 실패, 그 외 ±15 Torr 이상 실패
                    if (!IsPressureInterlockOk(live.PressureTorr, spec.TargetPressureTorr))
                    {
                        failed.Add($"{EquipmentRegionHelper.FormatUnitLabel(unit)} 압력 인터락 실패");
                    }
                    if (!IsTemperatureInterlockOk(live.TemperatureC, spec.TargetTemperatureC)) // 온도 이탈 과대 시 실패
                    {
                        failed.Add($"{EquipmentRegionHelper.FormatUnitLabel(unit)} 온도 인터락 실패");
                    }
                }
            }
            reasons = string.Join(", ", failed);
            return failed.Count == 0;
        }

        /// <summary>원본 Form1.BuildVerificationChecklist (항목 이름과 순서 그대로, 현재 값 설명을 덧붙임)</summary>
        public IReadOnlyList<InterlockCheck> BuildVerificationChecklist()
        {
            var items = new List<InterlockCheck>();
            items.Add(new InterlockCheck("EtherCAT 연결",
                EthercatConnected ? InterlockCheckState.Pass : InterlockCheckState.Fail,
                EthercatConnected ? "연결됨" : "연결 안 됨"));

            bool foupA = IsFoupAMounted, foupB = IsFoupBMounted;
            items.Add(new InterlockCheck("FOUP 장착",
                ViewModel.IsFoupMounted ? InterlockCheckState.Pass : InterlockCheckState.Fail,
                $"FOUP A {(foupA ? "장착" : "없음")} · FOUP B {(foupB ? "장착" : "없음")}"));

            bool alarm = CurrentProcessState == ProcessState.Error || HasAlarm;
            string alarmDetail = CurrentProcessState == ProcessState.Error ? "공정 상태 Error"
                : HasAlarm ? "알람 있음 (알람 리셋 필요)"
                : "알람 없음";
            items.Add(new InterlockCheck("장비 알람/에러 없음", alarm ? InterlockCheckState.Fail : InterlockCheckState.Pass, alarmDetail));

            foreach (var unit in ChamberUnitKeys)
            {
                string label = EquipmentRegionHelper.FormatUnitLabel(unit);
                if (ChamberEnvSpecs.TryGetValue(unit, out var spec) && ChamberEnvLive.TryGetValue(unit, out var live))
                {
                    bool pressOk = IsPressureInterlockOk(live.PressureTorr, spec.TargetPressureTorr);
                    string pressLimit = spec.TargetPressureTorr < InterlockLowPressureLimitTorr
                        ? $"기준 ≤ {FormatTorr(spec.TargetPressureTorr * InterlockLowPressureRatio)} Torr"
                        : $"기준 {FormatTorr(spec.TargetPressureTorr)} ±{InterlockPressureAbsTorr:0} Torr";
                    items.Add(new InterlockCheck($"{label} 압력 범위",
                        pressOk ? InterlockCheckState.Pass : InterlockCheckState.Fail,
                        $"PV {FormatTorr(live.PressureTorr)} Torr / {pressLimit}"));

                    bool tempOk = IsTemperatureInterlockOk(live.TemperatureC, spec.TargetTemperatureC);
                    items.Add(new InterlockCheck($"{label} 온도 범위",
                        tempOk ? InterlockCheckState.Pass : InterlockCheckState.Fail,
                        string.Format(CultureInfo.InvariantCulture, "PV {0:0.0} °C / 기준 {1:0.0} ±{2:0} °C",
                            live.TemperatureC, spec.TargetTemperatureC, InterlockTemperatureC)));
                }
                else
                {
                    items.Add(new InterlockCheck($"{label} 환경 데이터 준비", InterlockCheckState.NoData,
                        "공정을 시작하면 PV가 들어옵니다 (그 전에는 인터락 판정에서 빠짐)"));
                }
            }
            return items;
        }

        /// <summary>
        /// 원본 Form1.RunVerificationChecks: 검증 탭에 들어올 때 부른다.
        /// 통과하면 INFO, 못 하면 알람 리셋으로 숨기지 않은 동안 WARN을 남긴다 (WARN은 상단 알람 표시에도 뜬다).
        /// </summary>
        public bool RunVerificationChecks()
        {
            if (EvaluateInterlocks(out var reasons))
            {
                AddLogMessage("Verification: 모든 인터락 통과", "INFO");
                ViewModel.VerificationAlarmDismissed = false; // 인터락 통과 시 플래그 리셋
                return true;
            }

            // 알람이 리셋되지 않았을 때만 헤더에 알람 표시
            if (!ViewModel.VerificationAlarmDismissed)
            {
                AddLogMessage($"Verification: 인터락 미통과 - {reasons}", "WARN");
            }
            return false;
        }

        /// <summary>
        /// 원본 NavigateToSection(Operate): 운전 화면에 들어올 때 인터락 미통과면 경고.
        /// WPF는 운전 화면을 다른 화면에서 자주 오가므로, 까닭이 같으면 다시 남기지 않는다 (통과하면 초기화).
        /// </summary>
        public void WarnIfOperateInterlocksFail()
        {
            if (EvaluateInterlocks(out var reasons))
            {
                _lastOperateInterlockWarning = null;
                return;
            }
            if (string.Equals(reasons, _lastOperateInterlockWarning, StringComparison.Ordinal))
            {
                return;
            }
            _lastOperateInterlockWarning = reasons;
            AddLogMessage($"운전 화면 진입: 인터락 미통과 - {reasons}", "WARN");
        }

        /// <summary>
        /// 원본 Form1.AutoPlanTransferIfNeeded (데모): FOUP에 웨이퍼가 있고 TM이 쉬고 있으면 계획 로그만 남긴다.
        /// 로그를 남겼으면 true.
        /// </summary>
        public bool AutoPlanTransferIfNeeded()
        {
            int foupA = ViewModel.CurrentFoupACount;
            int foupB = ViewModel.CurrentFoupBCount;
            if (TmPhase == TransferController.TmPhase.Idle && (foupA > 0 || foupB > 0))
            {
                var source = foupA > 0 ? "FOUP A" : "FOUP B";
                var target = "Chamber A";
                AddLogMessage($"이송 계획 생성: {source} → {target} (데모)", "INFO");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 원본 Form1.ClearTransferQueue.
        /// 공정 중에 비우면 예약 표시(PickupScheduled, ReservedForIncoming)가 풀리지 않아 공정이 멈추므로 화면에서는 공정 중에 막는다.
        /// </summary>
        public void ClearTransferQueue()
        {
            TransferService?.ClearQueue();
            AddLogMessage("이송 큐를 비웠습니다.", "INFO");
        }

        /// <summary>원본 Form1.GetTransferQueueLines (TransferForm 목록 글자 그대로)</summary>
        public List<string> GetTransferQueueLines()
        {
            var lines = new List<string>();
            var current = CurrentTransfer;
            lines.Add(current != null
                ? $"[진행중] {EquipmentRegionHelper.FormatRegionLabel(current.Pickup)} → {EquipmentRegionHelper.FormatRegionLabel(current.Dropoff)} (웨이퍼 #{current.Wafer?.Id})"
                : "[진행중 없음]");
            if (TransferService != null)
            {
                foreach (var t in TransferService.GetQueuedTasks())
                {
                    lines.Add($"{EquipmentRegionHelper.FormatRegionLabel(t.Pickup)} → {EquipmentRegionHelper.FormatRegionLabel(t.Dropoff)} (웨이퍼 #{t.Wafer?.Id})");
                }
            }
            return lines;
        }

        /// <summary>진행 중 작업(있으면 Order 0)과 대기 작업(1부터)을 나눠 돌려준다 (WPF 이송 탭)</summary>
        public IReadOnlyList<TransferQueueEntry> GetTransferQueueEntries()
        {
            var entries = new List<TransferQueueEntry>();
            var current = CurrentTransfer;
            if (current != null)
            {
                entries.Add(ToEntry(current, 0));
            }
            if (TransferService != null)
            {
                int order = 1;
                foreach (var t in TransferService.GetQueuedTasks())
                {
                    entries.Add(ToEntry(t, order++));
                }
            }
            return entries;
        }

        private static TransferQueueEntry ToEntry(TransferController.TransferTask task, int order)
        {
            return new TransferQueueEntry
            {
                Order = order,
                From = EquipmentRegionHelper.FormatRegionLabel(task.Pickup),
                To = EquipmentRegionHelper.FormatRegionLabel(task.Dropoff),
                WaferId = task.Wafer?.Id,
                RetryCount = task.RetryCount
            };
        }
    }
}
