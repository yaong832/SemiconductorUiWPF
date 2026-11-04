using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controllers;
using SemiconductorUi.Helpers;
using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Process
{
    /// <summary>상세 창 머리줄 램프와 상태 칩 색 (원본 Controls.LampTone)</summary>
    public enum LampTone
    {
        Off,
        Green,
        Amber,
        Red,
        Blue,
    }

    /// <summary>상세 창 한 번에 보여 줄 내용 (챔버 또는 FOUP). 원본 Controls.UnitDetailContent</summary>
    public sealed class UnitDetailContent
    {
        public string Title = "";
        public string Status = "";
        public LampTone Tone = LampTone.Off;
        public bool Live;
        public readonly List<KeyValuePair<string, string>> Info = new List<KeyValuePair<string, string>>();
        public readonly List<KeyValuePair<string, bool>> Chips = new List<KeyValuePair<string, bool>>();
        /// <summary>환경 표: 이름, PV, SV, 단위. 없으면 null</summary>
        public string[][] Env;
        /// <summary>FOUP 슬롯: 위(슬롯 5)부터 아래(슬롯 1) 순서. 글자와 웨이퍼 있음 여부. 없으면 null</summary>
        public List<KeyValuePair<string, bool>> Slots;
        /// <summary>진행률(0~100). 없으면 null</summary>
        public int? Progress;
        public string ProgressText = "";
    }

    /// <summary>원본 Form1.PmDetailData</summary>
    public sealed class PmDetailData
    {
        public string UnitKey;
        public string StatusText;
        public string RecipeName;
        public string StepName;
        public int RecipeTimeCurrent;
        public int RecipeTimeTotal;
        public int StepTimeCurrent;
        public int StepTimeTotal;
        public int StepIndex;
        public int StepCount;
        public string StepMessage;
        public int Progress;
        public int StepProgress;
        public string ActiveWaferText;
    }

    // 원본 Form1.UnitDetail.cs (BuildChamberDetail, BuildFoupDetail)와 Form1.BuildPmDetail,
    // Form1UiUpdater.UpdateSimulationUi의 FOUP 상태 문구. 화면 표시만 하며 장비에는 아무 명령도 보내지 않는다.
    public sealed partial class ProcessEngine
    {
        public static readonly string[] UnitDetailKeys = { "PMA", "PMB", "PMC", "FOUP A", "FOUP B" };

        public ChamberController.ChamberState GetChamberStateForUnit(string unitKey)
        {
            switch (unitKey)
            {
                case "PMA": return ChamberAState;
                case "PMB": return ChamberBState;
                case "PMC": return ChamberCState;
                default: return null;
            }
        }

        /// <summary>원본 Form1.BuildPmDetail</summary>
        public PmDetailData BuildPmDetail(ChamberController.ChamberState chamber, int completedCount, int totalCount)
        {
            if (chamber == null)
            {
                return new PmDetailData
                {
                    UnitKey = "Unknown",
                    StatusText = ChamberStatusTexts.Idle,
                    RecipeName = "N/A",
                    StepName = "N/A",
                    RecipeTimeCurrent = 0,
                    RecipeTimeTotal = 0,
                    StepTimeCurrent = 0,
                    StepTimeTotal = 0,
                    StepIndex = 0,
                    StepCount = totalCount,
                    StepMessage = "데이터 없음",
                    Progress = 0
                };
            }

            var detail = new PmDetailData
            {
                UnitKey = chamber.UnitKey,
                StatusText = chamber.StatusText,
                RecipeName = chamber.Step.DisplayName,
                StepName = chamber.Step.DisplayName,
                RecipeTimeTotal = chamber.Step.DurationSeconds * Math.Max(1, totalCount),
                RecipeTimeCurrent = completedCount * chamber.Step.DurationSeconds,
                StepTimeTotal = chamber.Step.DurationSeconds,
                StepTimeCurrent = chamber.CurrentWafer != null
                    ? chamber.Step.DurationSeconds - chamber.RemainingSeconds
                    : 0,
                StepIndex = completedCount + (chamber.CurrentWafer != null ? 1 : 0),
                StepCount = Math.Max(1, totalCount),
            };

            if (chamber.CurrentWafer != null)
            {
                detail.RecipeTimeCurrent += detail.StepTimeCurrent;
            }

            if (detail.RecipeTimeTotal > 0)
            {
                detail.Progress = Math.Max(0, Math.Min(100,
                    (int)Math.Round(detail.RecipeTimeCurrent * 100.0 / detail.RecipeTimeTotal)));
            }
            else
            {
                detail.Progress = 0;
            }

            if (chamber.CurrentWafer != null && detail.StepTimeTotal > 0)
            {
                detail.StepProgress = Math.Max(0, Math.Min(100,
                    (int)Math.Round(detail.StepTimeCurrent * 100.0 / detail.StepTimeTotal)));
                detail.ActiveWaferText = $"Wafer #{chamber.CurrentWafer.Id} : {detail.StepProgress}% ({detail.StepTimeCurrent}/{detail.StepTimeTotal}s)";
                if (chamber.RemainingSeconds <= 0 && chamber.PickupScheduled)
                {
                    detail.StepMessage = $"TM 픽업 대기 (Wafer #{chamber.CurrentWafer.Id})";
                }
                else
                {
                    detail.StepMessage = $"공정 진행 중 (#{chamber.CurrentWafer.Id})";
                }
            }
            else if (chamber.ReservedForIncoming)
            {
                detail.StepProgress = 0;
                detail.ActiveWaferText = "웨이퍼 없음";
                detail.StepMessage = "TM 반입 대기";
            }
            else
            {
                detail.StepProgress = 0;
                detail.ActiveWaferText = "웨이퍼 없음";
                detail.StepMessage = chamber == ChamberAState && foupManager?.HasWafersInFoupA() == true
                    ? "웨이퍼 투입 대기"
                    : "유닛 대기";
            }

            return detail;
        }

        /// <summary>챔버 하나의 PmDetailData (원본 UpdateSimulationUi에서 PmDetails에 담던 값)</summary>
        public PmDetailData GetPmDetail(string unitKey)
        {
            switch (unitKey)
            {
                case "PMA": return BuildPmDetail(ChamberAState, chamberCompletedCountA, AppSettings.MaxFoupCapacity);
                case "PMB": return BuildPmDetail(ChamberBState, chamberCompletedCountB, AppSettings.MaxFoupCapacity);
                case "PMC": return BuildPmDetail(ChamberCState, chamberCompletedCountC, AppSettings.MaxFoupCapacity);
                default: return null;
            }
        }

        /// <summary>원본 UpdateSimulationUi의 foupAStatus ("대기 · 웨이퍼 5장")</summary>
        public string GetFoupStatusText(bool isA)
        {
            GetFoupDisplayCounts(out var displayFoupACount, out var displayFoupBCount);
            if (isA)
            {
                var prefix = !IsFoupAMounted
                    ? "FOUP A 미장착"
                    : WaferLoadState == MainFormViewModel.WaferLoadStateType.Loading
                        ? "로딩 준비"
                        : WaferLoadState == MainFormViewModel.WaferLoadStateType.Unloading
                            ? "언로딩 대기"
                            : (SimulationRunning ? "공정 진행" : "대기");
                return $"{prefix} · 웨이퍼 {displayFoupACount}장";
            }

            var prefixB = !IsFoupBMounted
                ? "FOUP B 미장착"
                : WaferLoadState == MainFormViewModel.WaferLoadStateType.Unloading
                    ? "언로딩 진행"
                    : (displayFoupBCount > 0 ? "완료 적재" : "대기");
            return $"{prefixB} · 완료 {displayFoupBCount}/{AppSettings.MaxFoupCapacity}";
        }

        /// <summary>원본 UpdateSimulationUi의 foupALock/foupBLock</summary>
        public string GetFoupLockText(bool isA)
        {
            GetFoupDisplayCounts(out _, out var displayFoupBCount);
            if (isA)
            {
                return !IsFoupAMounted
                    ? "미장착"
                    : (SimulationRunning && !SimulationPaused ? "Locked" : "Released");
            }
            return !IsFoupBMounted
                ? "미장착"
                : (displayFoupBCount > 0 || WaferLoadState == MainFormViewModel.WaferLoadStateType.Unloading)
                    ? "Locked"
                    : "Released";
        }

        /// <summary>
        /// 챔버 환경 표 (원본 PmStatusPanel.GetEnvironmentRows). 이름, PV, SV, 단위.
        /// SV는 레시피를 적용해야, PV는 그 챔버가 공정 중이어야 나온다.
        /// 레시피 적용과 환경 값은 아직 이관 전이라 지금은 모두 "-"다.
        /// </summary>
        public string[][] GetEnvironmentRows(string unitKey)
        {
            string[] Row(string name, string unit) => new[] { name, "-", "-", unit };
            return new[]
            {
                Row("NF3", "sccm"),
                Row("O2", "sccm"),
                Row("CF4", "sccm"),
                Row("압력", "Torr"),
                Row("RF", "W"),
                Row("온도", "°C"),
            };
        }

        /// <summary>상세 창 내용 (원본 Form1.RefreshUnitDetail의 분기)</summary>
        public UnitDetailContent BuildUnitDetail(string unitKey)
        {
            if (string.IsNullOrEmpty(unitKey)) return null;
            return unitKey.StartsWith("FOUP", StringComparison.Ordinal) ? BuildFoupDetail(unitKey) : BuildChamberDetail(unitKey);
        }

        /// <summary>원본 Form1.BuildChamberDetail</summary>
        public UnitDetailContent BuildChamberDetail(string unitKey)
        {
            var content = new UnitDetailContent { Title = EquipmentRegionHelper.FormatUnitLabel(unitKey) };
            var chamber = GetChamberStateForUnit(unitKey);
            var detail = chamber != null ? GetPmDetail(unitKey) : null;

            bool processing = chamber != null && IsChamberProcessing(chamber);
            bool completed = chamber != null && IsChamberCompleted(chamber);
            bool hasWafer = chamber?.CurrentWafer != null;
            var alarms = ViewModel.ChamberAlarmStatus;
            bool alarm = alarms.ContainsKey(unitKey) && alarms[unitKey];

            content.Status = detail?.StatusText ?? chamber?.StatusText ?? ChamberStatusTexts.Idle;
            content.Tone = alarm ? LampTone.Red
                : processing ? LampTone.Green
                : completed ? LampTone.Blue
                : hasWafer ? LampTone.Amber
                : LampTone.Off;
            content.Live = processing && SimulationRunning;

            string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;
            content.Info.Add(new KeyValuePair<string, string>("레시피", Dash(detail?.RecipeName)));
            content.Info.Add(new KeyValuePair<string, string>("스텝", Dash(detail?.StepName)));
            content.Info.Add(new KeyValuePair<string, string>("웨이퍼", hasWafer ? Dash(detail?.ActiveWaferText) : "없음"));
            if (detail != null)
            {
                content.Info.Add(new KeyValuePair<string, string>("처리 순서", $"{detail.StepIndex} / {Math.Max(1, detail.StepCount)}"));
                content.Info.Add(new KeyValuePair<string, string>("이번 시간", detail.StepTimeTotal > 0 ? $"{detail.StepTimeCurrent} / {detail.StepTimeTotal} s" : "-"));
                content.Info.Add(new KeyValuePair<string, string>("전체 시간", detail.RecipeTimeTotal > 0 ? $"{detail.RecipeTimeCurrent} / {detail.RecipeTimeTotal} s" : "-"));
            }

            bool doorOpen = chamber != null && IsRegionDoorOpen(chamber.Region);
            content.Chips.Add(new KeyValuePair<string, bool>(doorOpen ? "도어 열림" : "도어 닫힘", !doorOpen));
            content.Chips.Add(new KeyValuePair<string, bool>(processing ? "램프 켜짐" : completed ? "램프 점멸" : "램프 꺼짐", processing || completed));
            if (alarm)
            {
                content.Chips.Add(new KeyValuePair<string, bool>("알람", false));
            }

            content.Env = chamber != null ? GetEnvironmentRows(unitKey) : null;

            if (detail != null)
            {
                content.Progress = detail.StepProgress;
                string message = string.IsNullOrWhiteSpace(detail.StepMessage) ? content.Status : detail.StepMessage;
                content.ProgressText = $"{message} · {Math.Max(0, Math.Min(100, detail.StepProgress))}%";
            }

            return content;
        }

        /// <summary>원본 Form1.BuildFoupDetail</summary>
        public UnitDetailContent BuildFoupDetail(string unitKey)
        {
            bool isA = unitKey == "FOUP A";
            bool mounted = isA ? IsFoupAMounted : IsFoupBMounted;
            var content = new UnitDetailContent { Title = unitKey };

            content.Status = GetFoupStatusText(isA);

            int waferCount = 0;
            content.Slots = new List<KeyValuePair<string, bool>>();
            for (int layer = AppSettings.MaxFoupCapacity; layer >= 1; layer--)
            {
                bool hasWafer;
                if (isA)
                {
                    hasWafer = foupManager?.HasWafersInFoupA() == true && foupManager.HasWaferAtLayerFoupA(layer);
                }
                else
                {
                    hasWafer = foupManager?.FoupBCompleted != null && foupManager.FoupBCompleted.Any(w => w.Id == layer);
                }

                string text;
                if (!mounted)
                {
                    text = $"슬롯 {layer} · 미장착";
                    hasWafer = false;
                }
                else if (hasWafer)
                {
                    waferCount++;
                    text = $"슬롯 {layer} · Wafer #{layer} · {(isA ? "대기" : "완료")}";
                }
                else
                {
                    text = $"슬롯 {layer} · 비어 있음";
                }
                content.Slots.Add(new KeyValuePair<string, bool>(text, hasWafer));
            }

            content.Tone = !mounted ? LampTone.Off : waferCount > 0 ? LampTone.Blue : LampTone.Amber;

            // 원본 UpdateFoupStatusCards에 넘기는 고정값 (Path, LOTID, MID)과 선택 레시피(PPID)
            string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;
            content.Info.Add(new KeyValuePair<string, string>("웨이퍼", mounted ? $"{waferCount}장" : "-"));
            content.Info.Add(new KeyValuePair<string, string>("Path", isA ? "CM1" : "CM2"));
            content.Info.Add(new KeyValuePair<string, string>("PPID", Dash(ViewModel.LastSelectedRecipe)));
            content.Info.Add(new KeyValuePair<string, string>("LOTID", isA ? "LOT-A01" : "LOT-B01"));
            content.Info.Add(new KeyValuePair<string, string>("MID", isA ? "MID-A" : "MID-B"));
            content.Info.Add(new KeyValuePair<string, string>("Lock", GetFoupLockText(isA)));

            content.Chips.Add(new KeyValuePair<string, bool>(mounted ? "장착됨" : "미장착", mounted));
            return content;
        }
    }
}
