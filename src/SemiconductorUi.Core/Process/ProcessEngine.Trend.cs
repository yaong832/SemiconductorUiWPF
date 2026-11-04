using System;
using System.Collections.Generic;

namespace SemiconductorUi.Process
{
    /// <summary>추이 샘플 하나 (원본 Form1.trendSamples의 튜플)</summary>
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

    // 원본 Form1의 Trend PV/SV 샘플 수집 (StartTrendCapture / StopTrendCapture / CaptureTrendSampleTick)
    public sealed partial class ProcessEngine
    {
        /// <summary>샘플이 이만큼 넘으면 오래된 것부터 TrendTrimCount개를 버린다 (원본 5000 / 1000).</summary>
        public const int TrendSampleLimit = 5000;
        public const int TrendTrimCount = 1000;

        private readonly List<TrendSample> _trendSamples = new List<TrendSample>();

        public bool IsTrendCapturing { get; private set; }

        /// <summary>샘플 시각. 스냅샷/테스트에서 1초 간격 시각을 넣을 때 바꾼다.</summary>
        public Func<DateTime> TrendClock { get; set; } = () => DateTime.Now;

        public int TrendSampleCount => _trendSamples.Count;

        public void StartTrendCapture()
        {
            if (IsTrendCapturing) return;
            IsTrendCapturing = true;
            AddLogMessage("Trend 캡처 시작", "INFO");
        }

        public void StopTrendCapture()
        {
            if (!IsTrendCapturing) return;
            IsTrendCapturing = false;
            AddLogMessage($"Trend 캡처 중지 (샘플 {_trendSamples.Count}개)", "INFO");
        }

        public void ClearTrendSamples() => _trendSamples.Clear();

        /// <summary>지금까지 모은 샘플 (시간 순). 복사본이라 화면에서 마음대로 써도 된다.</summary>
        public IReadOnlyList<TrendSample> GetTrendSamples() => _trendSamples.ToArray();

        /// <summary>원본 CaptureTrendSampleTick: 시뮬레이션 틱마다 챔버별 온도/압력 PV와 SV를 하나씩 쌓는다.</summary>
        internal void CaptureTrendSampleTick(DateTime now)
        {
            if (!IsTrendCapturing) return;
            foreach (var unit in ChamberUnitKeys)
            {
                if (ChamberEnvSpecs.TryGetValue(unit, out var spec) && ChamberEnvLive.TryGetValue(unit, out var live))
                {
                    _trendSamples.Add(new TrendSample(now, unit, live.TemperatureC, spec.TargetTemperatureC, live.PressureTorr, spec.TargetPressureTorr));
                    if (_trendSamples.Count > TrendSampleLimit)
                    {
                        _trendSamples.RemoveRange(0, TrendTrimCount);
                    }
                }
            }
        }
    }
}
