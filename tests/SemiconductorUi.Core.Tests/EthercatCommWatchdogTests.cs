using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Hardware;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 통신 감시 판정: 한 번 튄 이상은 알리지 않고, 연속 기준 횟수에서 이상, 정상 한 번에 복구.
    /// </summary>
    [TestClass]
    public class EthercatCommWatchdogTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 10, 9, 0, 0);

        [TestMethod]
        public void SingleFailure_IsCountedButNotReported()
        {
            var state = new EthercatWatchdogState(2, TimeSpan.Zero);
            Assert.AreEqual(EthercatWatchdogEvent.FirstCheck, state.Observe(true, "ok", T0));
            Assert.AreEqual(EthercatWatchdogEvent.None, state.Observe(false, "bad", T0.AddSeconds(1)));
            Assert.AreEqual(EthercatWatchdogEvent.None, state.Observe(true, "ok", T0.AddSeconds(2)));
            Assert.IsFalse(state.IsUnhealthy);
            Assert.AreEqual(1, state.FailuresInWindow);
        }

        [TestMethod]
        public void ConsecutiveFailures_BecomeUnhealthyOnce_ThenRecover()
        {
            var state = new EthercatWatchdogState(2, TimeSpan.Zero);
            state.Observe(true, "ok", T0);
            Assert.AreEqual(EthercatWatchdogEvent.None, state.Observe(false, "bad", T0.AddSeconds(1)));
            Assert.AreEqual(EthercatWatchdogEvent.BecameUnhealthy, state.Observe(false, "bad", T0.AddSeconds(2)));
            Assert.AreEqual(EthercatWatchdogEvent.None, state.Observe(false, "bad", T0.AddSeconds(3)));
            Assert.IsTrue(state.IsUnhealthy);
            Assert.AreEqual(EthercatWatchdogEvent.Recovered, state.Observe(true, "ok", T0.AddSeconds(5)));
            Assert.IsFalse(state.IsUnhealthy);
            Assert.AreEqual(3.0, state.LastUnhealthyDuration.TotalSeconds, 0.001);
        }

        [TestMethod]
        public void FirstCheck_CanAlreadyBeUnhealthy_WhenThresholdIsOne()
        {
            var state = new EthercatWatchdogState(1, TimeSpan.Zero);
            Assert.AreEqual(EthercatWatchdogEvent.FirstCheck, state.Observe(false, "bad", T0));
            Assert.IsTrue(state.IsUnhealthy);
            Assert.AreEqual(EthercatWatchdogEvent.Recovered, state.Observe(true, "ok", T0.AddSeconds(1)));
        }

        [TestMethod]
        public void Summary_ReportsChecksAndFailuresPerWindow()
        {
            var state = new EthercatWatchdogState(2, TimeSpan.FromMinutes(1));
            state.Observe(true, "ok", T0);
            for (int i = 1; i < 60; i++)
            {
                Assert.AreEqual(EthercatWatchdogEvent.None, state.Observe(i != 30, "x", T0.AddSeconds(i)));
            }
            Assert.AreEqual(EthercatWatchdogEvent.Summary, state.Observe(true, "ok", T0.AddSeconds(60)));
            Assert.AreEqual(61, state.SummaryChecks);
            Assert.AreEqual(1, state.SummaryFailures);
            Assert.AreEqual(0, state.ChecksInWindow);
        }
    }
}
