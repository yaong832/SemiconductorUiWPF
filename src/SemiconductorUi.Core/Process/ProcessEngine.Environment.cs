using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controllers;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    /// <summary>원본 Form1.EnvAlarmThresholds (환경 경고/알람 임계값)</summary>
    public struct EnvAlarmThresholds
    {
        public double TempWarnDiffC;
        public double TempAlarmDiffC;
        public double PressWarnRatio;
        public double PressAlarmRatio;
        public double PressWarnAbsTorr;
        public double PressAlarmAbsTorr;
        public double RfWarnRatio;
        public double RfAlarmRatio;
        public double GasWarnAbsSccm;
        public double GasAlarmAbsSccm;
        public double GasLeakWarnSccm;
        public double GasLeakAlarmSccm;

        public static EnvAlarmThresholds CreateDefault()
        {
            // AppSettings에서 기본값 읽어옴
            return new EnvAlarmThresholds
            {
                TempWarnDiffC = AppSettings.AlarmTempWarn,
                TempAlarmDiffC = AppSettings.AlarmTempAlarm,
                PressWarnRatio = AppSettings.AlarmPressWarnRatio,
                PressAlarmRatio = AppSettings.AlarmPressAlarmRatio,
                PressWarnAbsTorr = AppSettings.AlarmPressWarnAbs,
                PressAlarmAbsTorr = AppSettings.AlarmPressAlarmAbs,
                RfWarnRatio = AppSettings.AlarmRfWarnRatio,
                RfAlarmRatio = AppSettings.AlarmRfAlarmRatio,
                GasWarnAbsSccm = AppSettings.AlarmGasWarn,
                GasAlarmAbsSccm = AppSettings.AlarmGasAlarm,
                GasLeakWarnSccm = AppSettings.AlarmGasLeakWarn,
                GasLeakAlarmSccm = AppSettings.AlarmGasLeakAlarm
            };
        }
    }

    /// <summary>챔버 하나의 환경 값 (현재값 PV, 설정값 SV). 마지막 화면 갱신 때 구한 값.</summary>
    public sealed class ChamberEnvironmentValues
    {
        public bool IsProcessing;
        public double PvNF3, PvO2, PvCF4, PvPress, PvRf, PvTemp;
        public double SvNF3, SvO2, SvCF4, SvPress, SvRf, SvTemp;
    }

    // 원본 Form1의 환경 텔레메트리(ChamberEnvSpecs/ChamberEnvLive/UnitGasRfSv), AlarmManager 환경 알람 판정,
    // Form1UiUpdater.UpdatePmEnvironmentTable/UpdatePmStatusPanelEnvironment, 레시피 적용(ApplyRecipeFromSnapshot).
    public sealed partial class ProcessEngine
    {
        public static readonly string[] ChamberUnitKeys = { "PMA", "PMB", "PMC" };

        // ChamberEnvironmentSpec과 ChamberEnvironmentLive는 ChamberController의 클래스 사용
        internal readonly Dictionary<string, ChamberController.ChamberEnvironmentSpec> ChamberEnvSpecs = new Dictionary<string, ChamberController.ChamberEnvironmentSpec>
        {
            { "PMA", new ChamberController.ChamberEnvironmentSpec("23.0 ±0.3°C (Spin chuck)", "760 Torr (대기압)", "45 ±5% RH", "PR 도포 · Solvent purge 2.0 m/s", 23.0, 760.0, 45.0) },
            { "PMB", new ChamberController.ChamberEnvironmentSpec("22.5 ±0.2°C (Stage)", "5×10⁻³ Torr (노광 챔버)", "<5% RH (건조 N₂)", "ArF 193nm Dose 120 mJ/cm²", 22.5, 0.005, 5.0) },
            { "PMC", new ChamberController.ChamberEnvironmentSpec("110°C PEB Plate", "50 Torr (N₂ 퍼지)", "<3% RH", "2차 노광/후베이크 · N₂ flow 20 slm", 110.0, 50.0, 3.0) }
        };
        internal readonly Dictionary<string, ChamberController.ChamberEnvironmentLive> ChamberEnvLive = new Dictionary<string, ChamberController.ChamberEnvironmentLive>();
        // 가스/RF Setpoint(레시피/사용자 설정) 저장
        internal readonly Dictionary<string, (double NF3, double O2, double CF4, double RF)> UnitGasRfSv = new Dictionary<string, (double, double, double, double)>
        {
            { "PMA", (200, 200, 200, 1000) },
            { "PMB", (0, 0, 0, 0) },
            { "PMC", (0, 0, 0, 0) }
        };

        private readonly Dictionary<string, ChamberEnvironmentValues> _envValues = new Dictionary<string, ChamberEnvironmentValues>();

        /// <summary>알람 임계값</summary>
        public EnvAlarmThresholds AlarmThresholds { get; private set; } = EnvAlarmThresholds.CreateDefault();

        /// <summary>레시피 적용 여부 (SV는 레시피를 적용해야 표시된다)</summary>
        public bool RecipeApplied { get; internal set; }

        /// <summary>시뮬레이션 PV를 만드는 난수 (시험에서 바꿔 끼운다)</summary>
        internal Random envRandom = new Random();

        /// <summary>원본 Form1.LoadAlarmThresholds (설정 저장소에서 임계값 읽기)</summary>
        public void LoadAlarmThresholds()
        {
            try
            {
                SetAlarmThresholdsFromSnapshot(ConfigRepository.Load());
            }
            catch
            {
                // 기본값 사용
                AlarmThresholds = EnvAlarmThresholds.CreateDefault();
            }
        }

        public EnvThresholdSnapshot GetAlarmThresholdsSnapshot()
        {
            var t = AlarmThresholds;
            return new EnvThresholdSnapshot
            {
                TempWarn = t.TempWarnDiffC,
                TempAlarm = t.TempAlarmDiffC,
                PressWarnRatio = t.PressWarnRatio,
                PressAlarmRatio = t.PressAlarmRatio,
                PressWarnAbs = t.PressWarnAbsTorr,
                PressAlarmAbs = t.PressAlarmAbsTorr,
                RfWarnRatio = t.RfWarnRatio,
                RfAlarmRatio = t.RfAlarmRatio,
                GasWarn = t.GasWarnAbsSccm,
                GasAlarm = t.GasAlarmAbsSccm,
                GasLeakWarn = t.GasLeakWarnSccm,
                GasLeakAlarm = t.GasLeakAlarmSccm
            };
        }

        public void SetAlarmThresholdsFromSnapshot(EnvThresholdSnapshot snapshot)
        {
            if (snapshot == null) return;
            AlarmThresholds = new EnvAlarmThresholds
            {
                TempWarnDiffC = snapshot.TempWarn,
                TempAlarmDiffC = snapshot.TempAlarm,
                PressWarnRatio = snapshot.PressWarnRatio,
                PressAlarmRatio = snapshot.PressAlarmRatio,
                PressWarnAbsTorr = snapshot.PressWarnAbs,
                PressAlarmAbsTorr = snapshot.PressAlarmAbs,
                RfWarnRatio = snapshot.RfWarnRatio,
                RfAlarmRatio = snapshot.RfAlarmRatio,
                GasWarnAbsSccm = snapshot.GasWarn,
                GasAlarmAbsSccm = snapshot.GasAlarm,
                GasLeakWarnSccm = snapshot.GasLeakWarn,
                GasLeakAlarmSccm = snapshot.GasLeakAlarm
            };
        }

        /// <summary>챔버 환경 스펙 (온도/압력/습도 기준)</summary>
        public ChamberController.ChamberEnvironmentSpec GetEnvironmentSpec(string unitKey)
            => ChamberEnvSpecs.TryGetValue(unitKey, out var spec) ? spec : null;

        /// <summary>마지막 화면 갱신 때 구한 챔버 환경 값. 아직 없으면 null</summary>
        public ChamberEnvironmentValues GetEnvironmentValues(string unitKey)
            => _envValues.TryGetValue(unitKey, out var v) ? v : null;

        #region 텔레메트리 (원본 Form1.cs / Form1Initializer)

        /// <summary>공정 시작 시에만 호출: PV 값을 SV 주변에서 시작하도록 초기화 (원본 InitializeEnvironmentTelemetry)</summary>
        internal void InitializeEnvironmentTelemetry()
        {
            foreach (var kvp in ChamberEnvSpecs)
            {
                ChamberEnvLive[kvp.Key] = new ChamberController.ChamberEnvironmentLive
                {
                    TemperatureC = kvp.Value.TargetTemperatureC,
                    PressureTorr = kvp.Value.TargetPressureTorr,
                    HumidityPercent = kvp.Value.TargetHumidityPercent
                };
            }
        }

        /// <summary>가스 PV 값 생성 (SV 근처에서 실시간 변동)</summary>
        internal double GenerateGasPV(double sv)
        {
            if (sv <= 0) return 0;

            // SV의 ±5% 범위에서 랜덤 변동
            double variation = sv * 0.05;
            double deviation = (envRandom.NextDouble() - 0.5) * 2 * variation;
            return Math.Max(0, sv + deviation);
        }

        internal ChamberController.ChamberEnvironmentLive UpdateLiveEnvironment(string unitKey, ChamberController.ChamberState chamberState)
        {
            if (!ChamberEnvSpecs.TryGetValue(unitKey, out var spec))
            {
                return null;
            }

            if (!ChamberEnvLive.TryGetValue(unitKey, out var live))
            {
                live = new ChamberController.ChamberEnvironmentLive
                {
                    TemperatureC = spec.TargetTemperatureC,
                    PressureTorr = spec.TargetPressureTorr,
                    HumidityPercent = spec.TargetHumidityPercent
                };
                ChamberEnvLive[unitKey] = live;
            }

            // 시뮬레이션 PV는 경고 임계값 이내로만 변동 (의도적 알람 주입 없음)
            double tempMaxDev = Math.Min(AlarmThresholds.TempWarnDiffC * 0.3, 0.5);
            live.TemperatureC = UpdateEnvChannel(live.TemperatureC, spec.TargetTemperatureC, 0.15, tempMaxDev);

            // 압력: 고압/저압에 따라 안전 범위만 사용
            double pressMaxDev;
            if (spec.TargetPressureTorr > 10.0)
            {
                pressMaxDev = AlarmThresholds.PressWarnAbsTorr * 0.3;
            }
            else
            {
                pressMaxDev = Math.Max(
                    Math.Min(spec.TargetPressureTorr * AlarmThresholds.PressWarnRatio * 0.3, AlarmThresholds.PressWarnAbsTorr * 0.3),
                    0.0001
                );
            }
            live.PressureTorr = UpdateEnvChannel(live.PressureTorr, spec.TargetPressureTorr, 0.2, pressMaxDev);

            // 습도: 고정 범위로 제한 (알람 없음)
            live.HumidityPercent = UpdateEnvChannel(live.HumidityPercent, spec.TargetHumidityPercent, 0.18, 1.0);

            live.TemperatureC = Math.Round(live.TemperatureC, 2);
            live.PressureTorr = spec.TargetPressureTorr < 1.0
                ? Math.Round(live.PressureTorr, 4)
                : Math.Round(live.PressureTorr, 2);
            live.HumidityPercent = Math.Round(live.HumidityPercent, 2);
            return live;
        }

        private double UpdateEnvChannel(double value, double target, double settleRatio, double maxDeviation)
        {
            // 노이즈 범위: 알람 발생 시에는 더 큰 변동 허용
            double noiseFactor = maxDeviation > (AlarmThresholds.TempWarnDiffC * 0.5) ? 0.25 : 0.15;
            var noise = (envRandom.NextDouble() - 0.5) * (maxDeviation * noiseFactor);
            var updated = value + (target - value) * settleRatio + noise;
            return Math.Max(target - maxDeviation, Math.Min(target + maxDeviation, updated));
        }

        internal double GenerateSafeRandomValue(double sv, double warnThreshold, double alarmThreshold)
        {
            if (sv == 0)
            {
                // SV=0: 누설 경고 임계값의 20% 이내만 (알람 주입 없음)
                double maxLeak = AlarmThresholds.GasLeakWarnSccm * 0.2;
                return envRandom.NextDouble() * maxLeak;
            }

            // 가스/RF: 경고 임계값의 20% 이내만
            double safeRange = Math.Min(warnThreshold * 0.2, alarmThreshold * 0.15);
            double deviation = (envRandom.NextDouble() - 0.5) * safeRange * 2;
            return Math.Max(0, sv + deviation);
        }

        #endregion

        #region 화면 갱신마다 환경 값 구하기 + 알람 판정 (원본 UpdatePmEnvironmentTable)

        /// <summary>세 챔버의 환경 값을 다시 구하고, 공정 중인 챔버는 경고/알람을 판정한다 (UpdateSimulationUi에서).</summary>
        internal void UpdateChamberEnvironments()
        {
            foreach (var unitKey in ChamberUnitKeys)
            {
                UpdateChamberEnvironment(unitKey);
            }
        }

        private void UpdateChamberEnvironment(string unitKey)
        {
            var v = new ChamberEnvironmentValues();

            // SV: 알람 판정용 타겟은 챔버 스펙을 항상 사용
            if (ChamberEnvSpecs.TryGetValue(unitKey, out var spec))
            {
                v.SvTemp = spec.TargetTemperatureC;
                v.SvPress = spec.TargetPressureTorr;
            }

            // 가스/전력 SV: 유닛별 설정값 사용 (없으면 0)
            var gasRf = UnitGasRfSv.ContainsKey(unitKey) ? UnitGasRfSv[unitKey] : (0, 0, 0, 0);
            v.SvNF3 = gasRf.NF3;
            v.SvO2 = gasRf.O2;
            v.SvCF4 = gasRf.CF4;
            v.SvRf = gasRf.RF;

            // PV: 해당 챔버에 웨이퍼가 있고 공정이 실제 진행 중일 때만
            var cs = GetChamberStateForUnit(unitKey);
            v.IsProcessing = SimulationRunning && cs != null && cs.CurrentWafer != null &&
                cs.TotalSeconds > 0 && cs.RemainingSeconds > 0 &&
                ChamberStatusTexts.IsProcessing(cs.StatusText);

            if (v.IsProcessing)
            {
                var live = UpdateLiveEnvironment(unitKey, cs);
                v.PvTemp = live != null ? live.TemperatureC : v.SvTemp;
                v.PvPress = live != null ? live.PressureTorr : v.SvPress;

                // 가스/전력 PV: 알람이 발생하지 않도록 안전하게 랜덤 값 생성
                var thresholds = AlarmThresholds;
                v.PvNF3 = GenerateSafeRandomValue(v.SvNF3, thresholds.GasWarnAbsSccm, thresholds.GasAlarmAbsSccm);
                v.PvO2 = GenerateSafeRandomValue(v.SvO2, thresholds.GasWarnAbsSccm, thresholds.GasAlarmAbsSccm);
                v.PvCF4 = GenerateSafeRandomValue(v.SvCF4, thresholds.GasWarnAbsSccm, thresholds.GasAlarmAbsSccm);
                // RF: 안전 범위만 유지 (실제 알람은 임계값 이탈 시에만)
                if (v.SvRf > 0)
                {
                    double rfWarnThreshold = v.SvRf * thresholds.RfWarnRatio;
                    double rfAlarmThreshold = v.SvRf * thresholds.RfAlarmRatio;
                    double rfSafeRange = Math.Min(rfWarnThreshold * 0.2, rfAlarmThreshold * 0.15);
                    double rfDeviation = (envRandom.NextDouble() - 0.5) * rfSafeRange * 2;
                    v.PvRf = Math.Max(0, v.SvRf + rfDeviation);
                }

                // 경고/알람 판정 (해당 챔버 공정 실행 중일 때만)
                EvaluateEnvironmentAlarms(
                    unitKey,
                    v.SvNF3, v.SvO2, v.SvCF4, v.SvPress, v.SvRf, v.SvTemp,
                    v.PvNF3, v.PvO2, v.PvCF4, v.PvPress, v.PvRf, v.PvTemp);
            }

            _envValues[unitKey] = v;
        }

        /// <summary>
        /// 챔버 환경 표 (원본 PmStatusPanel.GetEnvironmentRows). 이름, PV, SV, 단위.
        /// SV는 레시피를 적용해야, PV는 그 챔버가 공정 중이어야 나온다.
        /// </summary>
        public string[][] GetEnvironmentRows(string unitKey)
        {
            var v = GetEnvironmentValues(unitKey) ?? new ChamberEnvironmentValues();
            // 원본 상태 패널은 가스/RF PV를 SV 근처 값으로 따로 만들어서(GenerateGasPV) SV가 0이면 PV도 "-"였다.
            // 여기서는 판정에 쓴 PV를 그대로 보이되, 가스/RF는 SV가 0이면 누설 확인용 작은 값이라 "-"로 둔다.
            string[] Row(string name, double pv, double sv, string unit, bool needsSv = true) => new[]
            {
                name,
                v.IsProcessing && pv > 0 && (!needsSv || sv > 0) ? FormatEnvValue(pv, unit) : "-",
                RecipeApplied && sv > 0 ? FormatEnvValue(sv, unit) : "-",
                unit
            };
            return new[]
            {
                Row("NF3", v.PvNF3, v.SvNF3, "sccm"),
                Row("O2", v.PvO2, v.SvO2, "sccm"),
                Row("CF4", v.PvCF4, v.SvCF4, "sccm"),
                Row("압력", v.PvPress, v.SvPress, "Torr", needsSv: false),
                Row("RF", v.PvRf, v.SvRf, "W"),
                Row("온도", v.PvTemp, v.SvTemp, "°C", needsSv: false),
            };
        }

        /// <summary>원본 PmStatusPanel.FormatValue</summary>
        public static string FormatEnvValue(double value, string unit)
        {
            if (value == 0) return "-";
            switch (unit)
            {
                case "°C": return $"{value:F1}";
                case "Torr": return value < 1.0 ? $"{value:F3}" : $"{value:F1}";
                case "sccm": return $"{value:F0}";
                case "W": return $"{value:F0}";
                default: return $"{value:F2}";
            }
        }

        #endregion

        #region 환경 알람 판정 (원본 AlarmManager + Form1.EvaluateEnvironmentAlarms)

        internal void EvaluateEnvironmentAlarms(
            string unitKey,
            double svNF3, double svO2, double svCF4, double svPress, double svRf, double svTemp,
            double pvNF3, double pvO2, double pvCF4, double pvPress, double pvRf, double pvTemp)
        {
            bool hasAlarmInChamber = EvaluateEnvironmentAlarmsCore(
                unitKey,
                svNF3, svO2, svCF4, svPress, svRf, svTemp,
                pvNF3, pvO2, pvCF4, pvPress, pvRf, pvTemp);

            // 알람 상태 업데이트 (각 PM별로 독립적으로 관리)
            if (hasAlarmInChamber)
            {
                ViewModel.SetChamberAlarmStatus(unitKey, true);
                HasAlarm = true;
                // 공정을 붙잡습니다: 진행 중인 TM 이송만 마치고, 새 이송과 챔버 공정 시간을 멈춥니다.
                HoldProcessForAlarm(unitKey);
                if (CurrentProcessState != ProcessState.Error)
                {
                    SetProcessState(ProcessState.Error, $"{EquipmentRegionHelper.FormatUnitLabel(unitKey)} 환경 알람 발생");
                }
            }
            // 값이 정상으로 돌아와도 챔버 알람 표시는 알람 리셋 전까지 유지합니다 (리셋에서 해제).

            // 상단 패널 색상 즉시 업데이트 (원본 UpdateHeaderPmStatusColorImmediate)
            Host.RefreshUi();
        }

        private bool EvaluateEnvironmentAlarmsCore(
            string unitKey,
            double svNF3, double svO2, double svCF4, double svPress, double svRf, double svTemp,
            double pvNF3, double pvO2, double pvCF4, double pvPress, double pvRf, double pvTemp)
        {
            var t = AlarmThresholds;
            string label = EquipmentRegionHelper.FormatUnitLabel(unitKey);
            bool hasAlarmInChamber = false;

            // 온도 평가
            var tempDiff = Math.Abs(pvTemp - svTemp);
            if (tempDiff > t.TempAlarmDiffC)
            {
                AddLogMessage($"{label} 온도 이탈(알람): PV={pvTemp:0.0}°C, SV={svTemp:0.0}°C (Δ={tempDiff:0.0}°C)", "ALARM");
                hasAlarmInChamber = true;
            }
            else if (tempDiff > t.TempWarnDiffC)
            {
                AddLogMessage($"{label} 온도 이탈(주의): PV={pvTemp:0.0}°C, SV={svTemp:0.0}°C (Δ={tempDiff:0.0}°C)", "WARN");
            }

            // 압력 평가: 고압/저압에 따라 비율 또는 절대값 기준 선택
            double pressDiff = Math.Abs(pvPress - svPress);
            if (svPress > 0)
            {
                bool isHighPressure = svPress > 10.0; // 10 Torr 이상은 고압으로 간주
                bool isAlarm;
                bool isWarn;

                if (isHighPressure)
                {
                    // 고압: 절대값 기준만 사용 (비율 기준은 너무 큼)
                    isAlarm = pressDiff > t.PressAlarmAbsTorr;
                    isWarn = pressDiff > t.PressWarnAbsTorr && !isAlarm;
                }
                else
                {
                    // 저압: 비율과 절대값 모두 확인 (둘 중 하나라도 초과하면 알람)
                    double pressRatio = pressDiff / svPress;
                    isAlarm = pressRatio > t.PressAlarmRatio || pressDiff > t.PressAlarmAbsTorr;
                    isWarn = (pressRatio > t.PressWarnRatio || pressDiff > t.PressWarnAbsTorr) && !isAlarm;
                }

                if (isAlarm)
                {
                    AddLogMessage($"{label} 압력 이탈(알람): PV={pvPress:0.###} Torr, SV={svPress:0.###} Torr (Δ={pressDiff:0.###} Torr)", "ALARM");
                    hasAlarmInChamber = true;
                }
                else if (isWarn)
                {
                    AddLogMessage($"{label} 압력 이탈(주의): PV={pvPress:0.###} Torr, SV={svPress:0.###} Torr (Δ={pressDiff:0.###} Torr)", "WARN");
                }
            }

            // RF 평가
            if (svRf > 0)
            {
                double rfRatio = Math.Abs(pvRf - svRf) / svRf;
                if (rfRatio > t.RfAlarmRatio)
                {
                    AddLogMessage($"{label} RF 이탈(알람): PV={pvRf:0} W, SV={svRf:0} W", "ALARM");
                    hasAlarmInChamber = true;
                }
                else if (rfRatio > t.RfWarnRatio)
                {
                    AddLogMessage($"{label} RF 이탈(주의): PV={pvRf:0} W, SV={svRf:0} W", "WARN");
                }
            }

            // 가스 평가 (각각)
            hasAlarmInChamber |= EvaluateGas(label, "NF3", svNF3, pvNF3);
            hasAlarmInChamber |= EvaluateGas(label, "O2", svO2, pvO2);
            hasAlarmInChamber |= EvaluateGas(label, "CF4", svCF4, pvCF4);

            return hasAlarmInChamber;
        }

        private bool EvaluateGas(string label, string gasName, double sv, double pv)
        {
            var t = AlarmThresholds;
            if (sv > 0)
            {
                double diff = Math.Abs(pv - sv);
                if (diff > t.GasAlarmAbsSccm)
                {
                    AddLogMessage($"{label} {gasName} 유량 이탈(알람): PV={pv:0.0} sccm, SV={sv:0.0} sccm", "ALARM");
                    return true;
                }
                if (diff > t.GasWarnAbsSccm)
                {
                    AddLogMessage($"{label} {gasName} 유량 이탈(주의): PV={pv:0.0} sccm, SV={sv:0.0} sccm", "WARN");
                }
                return false;
            }

            if (pv > t.GasLeakAlarmSccm)
            {
                AddLogMessage($"{label} {gasName} 누설 의심(알람): PV={pv:0.0} sccm (SV=0)", "ALARM");
                return true;
            }
            if (pv > t.GasLeakWarnSccm)
            {
                AddLogMessage($"{label} {gasName} 누설 의심(주의): PV={pv:0.0} sccm (SV=0)", "WARN");
            }
            return false;
        }

        #endregion

        #region 시뮬레이션 시험 알람 (원본 Form1.AlarmHold.cs)

        /// <summary>시험 알람을 넣을 수 있는지: 시뮬레이션 모드, 공정 진행 중(일시 정지 아님), 그 챔버가 공정 중</summary>
        public bool CanInjectSimulationTestAlarm(string unitKey)
        {
            if (EthercatConnected || !SimulationRunning || SimulationPaused)
            {
                return false;
            }

            var chamber = GetChamberStateForUnit(unitKey);
            return chamber != null && chamber.CurrentWafer != null
                && chamber.RemainingSeconds > 0
                && ChamberStatusTexts.IsProcessing(chamber.StatusText);
        }

        /// <summary>온도를 설정값 +100°C로 한 번 넣어 환경 알람 판정을 탑니다 (실제 값 생성 경로와 같은 판정).</summary>
        public void InjectSimulationTestAlarm(string unitKey)
        {
            if (!CanInjectSimulationTestAlarm(unitKey))
            {
                return;
            }

            if (!ChamberEnvSpecs.TryGetValue(unitKey, out var spec))
            {
                return;
            }

            AddLogMessage($"시험 알람: {EquipmentRegionHelper.FormatUnitLabel(unitKey)} 온도를 기준 밖으로 한 번 넣었습니다 (시뮬레이션 전용)", "WARN");
            double svTemp = spec.TargetTemperatureC;
            double svPress = spec.TargetPressureTorr;
            EvaluateEnvironmentAlarms(
                unitKey,
                0, 0, 0, svPress, 0, svTemp,
                0, 0, 0, svPress, 0, svTemp + 100.0);
        }

        #endregion

        #region 레시피 적용 (원본 ProcessEventHandlers.ButtonApplyRecipe_Click, Form1.ApplyRecipeFromSnapshot)

        /// <summary>레시피 적용 버튼. 레시피 저장소에서 이름이 같은 레시피를 찾아 적용한다.</summary>
        public void ApplyRecipeButton(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                Host.ShowMessage("적용할 레시피를 선택하세요.", "안내", MessageKind.Information);
                return;
            }
            ViewModel.LastSelectedRecipe = recipeName;
            AddLogMessage($"레시피 '{recipeName}' 적용을 준비했습니다.", "INFO");
            var list = RecipeRepository.LoadAll();
            var snap = list.FirstOrDefault(r => string.Equals(r.Name, recipeName, StringComparison.Ordinal));
            if (snap == null)
            {
                Host.ShowMessage("레시피를 찾을 수 없습니다. 레시피 관리에서 다시 저장해 주세요.", "오류", MessageKind.Error);
                return;
            }
            ApplyRecipeFromSnapshot(snap);
            RecipeApplied = true; // 레시피 적용 플래그 설정
            Host.RefreshUi();
        }

        public void ApplyRecipeFromSnapshot(RecipeSnapshot r)
        {
            SetRecipeParameters(
                waferCount: Math.Max(1, r.WaferCount),
                chamberADuration: r.DurA,
                chamberBDuration: r.DurB,
                chamberCDuration: r.DurC,
                enableSecondExposure: r.SecondExposure);

            // 환경 스펙을 레시피 값으로 구성 (간단한 표기)
            string pmaTempStr = $"{r.PMA?.T:0.0}°C";
            string pmaPressStr = $"{(r.PMA?.P ?? 0):0.###} Torr";
            string pmaRhStr = $"{(r.PMA?.H ?? 0):0.#}% RH";
            string pmbTempStr = $"{r.PMB?.T:0.0}°C";
            string pmbPressStr = $"{(r.PMB?.P ?? 0):0.###} Torr";
            string pmbRhStr = $"{(r.PMB?.H ?? 0):0.#}% RH";
            string pmcTempStr = $"{r.PMC?.T:0.0}°C";
            string pmcPressStr = $"{(r.PMC?.P ?? 0):0.###} Torr";
            string pmcRhStr = $"{(r.PMC?.H ?? 0):0.#}% RH";
            const string notes = "Recipe 기반";

            ChamberEnvSpecs["PMA"] = new ChamberController.ChamberEnvironmentSpec(pmaTempStr, pmaPressStr, pmaRhStr, notes, r.PMA?.T ?? 23.0, r.PMA?.P ?? 760.0, r.PMA?.H ?? 45.0);
            ChamberEnvSpecs["PMB"] = new ChamberController.ChamberEnvironmentSpec(pmbTempStr, pmbPressStr, pmbRhStr, notes, r.PMB?.T ?? 22.5, r.PMB?.P ?? 0.005, r.PMB?.H ?? 5.0);
            ChamberEnvSpecs["PMC"] = new ChamberController.ChamberEnvironmentSpec(pmcTempStr, pmcPressStr, pmcRhStr, notes, r.PMC?.T ?? 110.0, r.PMC?.P ?? 50.0, r.PMC?.H ?? 3.0);
            // 레시피 적용 시에는 PV 값을 변경하지 않음 (공정 시작 시에만 초기화됨)

            // Gas/RF SV 반영
            UnitGasRfSv["PMA"] = (r.GasRfPMA?.NF3 ?? 0, r.GasRfPMA?.O2 ?? 0, r.GasRfPMA?.CF4 ?? 0, r.GasRfPMA?.RF ?? 0);
            UnitGasRfSv["PMB"] = (r.GasRfPMB?.NF3 ?? 0, r.GasRfPMB?.O2 ?? 0, r.GasRfPMB?.CF4 ?? 0, r.GasRfPMB?.RF ?? 0);
            UnitGasRfSv["PMC"] = (r.GasRfPMC?.NF3 ?? 0, r.GasRfPMC?.O2 ?? 0, r.GasRfPMC?.CF4 ?? 0, r.GasRfPMC?.RF ?? 0);

            AddLogMessage($"레시피 적용 완료: 웨이퍼 {CurrentRecipeWaferCount}장, A:{r.DurA}s, B:{r.DurB}s, C:{r.DurC}s, 2차:{(r.SecondExposure ? "사용" : "미사용")}", "INFO");
            UpdateSimulationUi();
        }

        #endregion
    }
}
