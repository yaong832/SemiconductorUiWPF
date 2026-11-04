using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class LogCsvExporterTests
    {
        [TestMethod]
        public void Escape_QuotesOnlyWhenNeeded()
        {
            Assert.AreEqual("plain", LogCsvExporter.Escape("plain"));
            Assert.AreEqual("\"a,b\"", LogCsvExporter.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", LogCsvExporter.Escape("say \"hi\""));
            Assert.AreEqual("\"line1\nline2\"", LogCsvExporter.Escape("line1\nline2"));
            Assert.AreEqual(string.Empty, LogCsvExporter.Escape(null));
        }

        [TestMethod]
        public void Write_HeaderAndRowsWithDate()
        {
            var sw = new StringWriter();
            LogCsvExporter.Write(sw, new[]
            {
                new LogCsvRow(new DateTime(2026, 11, 4, 9, 5, 7), "WARN", "PMA 온도 이탈, Δ=3.0"),
                new LogCsvRow(new DateTime(2026, 11, 4, 9, 5, 8), "INFO", "시작"),
            });
            var lines = sw.ToString().Replace("\r", "").TrimEnd('\n').Split('\n');
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(LogCsvExporter.Header, lines[0]);
            Assert.AreEqual("2026-11-04 09:05:07,WARN,\"PMA 온도 이탈, Δ=3.0\"", lines[1]);
            Assert.AreEqual("2026-11-04 09:05:08,INFO,시작", lines[2]);
        }

        [TestMethod]
        public void Save_WritesUtf8BomFileNamedByTagAndTime()
        {
            var dir = Path.Combine(Path.GetTempPath(), "logcsv_" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = LogCsvExporter.Save(new[] { new LogCsvRow(DateTime.Now, "INFO", "한글") }, "WARN",
                    new DateTime(2026, 11, 4, 10, 20, 30), dir);
                Assert.AreEqual(Path.Combine(dir, "SemiconductorUi_Logs_WARN_20261104_102030.csv"), path);
                var bytes = File.ReadAllBytes(path);
                CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });
                StringAssert.Contains(File.ReadAllText(path, Encoding.UTF8), "한글");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
