using System;
using System.Collections.Generic;

namespace SemiconductorUi.Process
{
    /// <summary>추이 샘플 하나 (원본 Form1.trendSamples의 튜플). 값이 없으면 double.NaN.</summary>
    public readonly struct TrendSample
    {
        public TrendSample(DateTime time, string unit, double pvTemp, double svTemp, double pvPress, double svPress)
        {
            Time = time;
            Unit = unit;
            PvTemp = pvTemp;
            SvTemp = svTemp;
            PvPress = pvPress;
            SvPress = svPress;
        }

        public DateTime Time { get; }

        /// <summary>PMA / PMB / PMC</summary>
        public string Unit { get; }

        public double PvTemp { get; }
        public double SvTemp { get; }
        public double PvPress { get; }
        public double SvPress { get; }
    }

    // 원본 Form1의 Trend PV/SV 샘플 수집 (원본 4777c6c 기준).
    // 공정 타이머가 도는 동안 1초마다 Chamber A/B/C 값을 기록한다. 추이 화면을 열지 않아도 기록하므로
    // 나중에 열어도 그동안의 그래프가 보인다. 값은 PM 상태 카드와 같은 기준:
    // PV는 그 챔버가 공정 중일 때만, SV는 레시피 적용 시에만 (없으면 NaN → 선이 끊긴다).
    public sealed partial class ProcessEngine
    {
        /// <summary>기록 간격 (원본 TrendSampleIntervalMs)</summary>
        public const int TrendSampleIntervalMs = 1000;

        /// <summary>최근 2시간만 보관 (원본 TrendKeepDuration)</summary>
        public static readonly TimeSpan TrendKeepDuration = TimeSpan.FromHours(2);

        private readonly List<TrendSample> _trendSamples = new List<TrendSample>();
        private DateTime _lastTrendSampleTime = DateTime.MinValue;

        /// <summary>샘플 시각. 스냅샷/테스트에서 1초 간격 시각을 넣을 때 바꾼다.</summary>
        public Func<DateTime> TrendClock { get; set; } = () => DateTime.Now;

        public int TrendSampleCount => _trendSamples.Count;

        /// <summary>WPF 추가: 기록을 비운다 (추이 화면 '지우기')</summary>
        public void ClearTrendSamples()
        {
            _trendSamples.Clear();
            _lastTrendSampleTime = DateTime.MinValue;
        }

        /// <summary>지금까지 모은 샘플 (시간 순). 복사본이라 화면에서 마음대로 써도 된다 (원본 GetTrendSamplesSnapshot).</summary>
        public IReadOnlyList<TrendSample> GetTrendSamples() => _trendSamples.ToArray();

        /// <summary>원본 CaptureTrendSampleTick: 1초마다 챔버별 온도/압력 PV와 SV를 하나씩 쌓는다.</summary>
        internal void CaptureTrendSampleTick(DateTime now)
        {
            if ((now - _lastTrendSampleTime).TotalMilliseconds < TrendSampleIntervalMs)
            {
                return;
            }
            _lastTrendSampleTime = now;

            foreach (var unit in ChamberUnitKeys)
            {
                double svTemp = double.NaN, svPress = double.NaN;
                if (RecipeApplied && ChamberEnvSpecs.TryGetValue(unit, out var spec))
                {
                    svTemp = spec.TargetTemperatureC;
                    svPress = spec.TargetPressureTorr;
                }

                double pvTemp = double.NaN, pvPress = double.NaN;
                var cs = GetChamberStateForUnit(unit);
                bool processing = SimulationRunning && cs != null && IsChamberProcessing(cs);
                if (processing && ChamberEnvLive.TryGetValue(unit, out var live) && live != null)
                {
                    pvTemp = live.TemperatureC;
                    pvPress = live.PressureTorr;
                }

                _trendSamples.Add(new TrendSample(now, unit, pvTemp, svTemp, pvPress, svPress));
            }

            // 최근 2시간만 보관
            var cutoff = now - TrendKeepDuration;
            int remove = 0;
            while (remove < _trendSamples.Count && _trendSamples[remove].Time < cutoff)
            {
                remove++;
            }
            if (remove > 0)
            {
                _trendSamples.RemoveRange(0, remove);
            }
        }
    }
}
