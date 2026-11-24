using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Helpers;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class SystemInfoSummaryTests
    {
        [TestMethod]
        public void Build_UsesOriginalLinesAndOrder()
        {
            string text = SystemInfoSummary.Build("1.2.3", "Microsoft Windows NT 10.0.19045.0", "8.0.11", true, true, "kim", "Running");
            Assert.AreEqual(
                "App Version: 1.2.3\r\nOS: Microsoft Windows NT 10.0.19045.0\r\n.NET: 8.0.11\r\nEtherCAT: Connected\r\nLogin: kim\r\nProcess: Running\r\n"
                    .Replace("\r\n", System.Environment.NewLine),
                text);
        }

        [TestMethod]
        public void Build_GuestAndDisconnected()
        {
            string text = SystemInfoSummary.Build("1.0", "os", "8.0", false, false, "kim", "Idle");
            StringAssert.Contains(text, "EtherCAT: Disconnected");
            StringAssert.Contains(text, "Login: Guest");
        }
    }
}
