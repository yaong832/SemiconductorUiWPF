using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    /// <summary>날짜별 로그 파일 (원본 Services/DailyLogFileWriter, fc62c3a)</summary>
    [TestClass]
    public class DailyLogFileWriterTests
    {
        private string _folder;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "DailyLogTests_" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        [TestMethod]
        public void Write_CreatesDatedFileWithBomHeaderAndEscapedMessage()
        {
            var time = new DateTime(2026, 11, 11, 9, 5, 7, 123);
            using (var writer = new DailyLogFileWriter(_folder, 90))
            {
                writer.Write(time, "WARN", "도어 \"A\" 열림, 확인");
                writer.Write(time.AddSeconds(1), "INFO", "다음 줄");
            }

            var path = Path.Combine(_folder, "Log_20261111.csv");
            var bytes = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual("Date,Time,Level,Message", lines[0]);
            Assert.AreEqual("2026-11-11,09:05:07.123,WARN,\"도어 \"\"A\"\" 열림, 확인\"", lines[1]);
            Assert.AreEqual(3, lines.Length);
        }

        [TestMethod]
        public void Write_AppendsToExistingFileWithoutSecondHeader()
        {
            var time = new DateTime(2026, 11, 11, 10, 0, 0);
            using (var writer = new DailyLogFileWriter(_folder, 90)) writer.Write(time, "INFO", "첫 실행");
            using (var writer = new DailyLogFileWriter(_folder, 90)) writer.Write(time, "INFO", "두 번째 실행");

            var lines = File.ReadAllLines(Path.Combine(_folder, "Log_20261111.csv"), Encoding.UTF8);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(1, lines.Count(l => l.StartsWith("Date,")));
        }

        [TestMethod]
        public void Write_NewDay_SwitchesFile()
        {
            using (var writer = new DailyLogFileWriter(_folder, 0))
            {
                writer.Write(new DateTime(2026, 11, 10, 23, 59, 59), "INFO", "어제");
                writer.Write(new DateTime(2026, 11, 11, 0, 0, 1), "INFO", "오늘");
            }
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "Log_20261110.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "Log_20261111.csv")));
        }

        [TestMethod]
        public void DeleteExpiredFiles_KeepsRetentionDaysIncludingToday_AndIgnoresOtherFiles()
        {
            Directory.CreateDirectory(_folder);
            foreach (var name in new[] { "Log_20261102.csv", "Log_20261103.csv", "Log_20261111.csv", "Log_bad.csv", "Other_20200101.csv" })
                File.WriteAllText(Path.Combine(_folder, name), "x");

            using (var writer = new DailyLogFileWriter(_folder, 9))
            {
                // 9일 보관: 11/11 포함 11/03까지 남는다
                Assert.AreEqual(1, writer.DeleteExpiredFiles(new DateTime(2026, 11, 11)));
            }
            Assert.IsFalse(File.Exists(Path.Combine(_folder, "Log_20261102.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "Log_20261103.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "Log_bad.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "Other_20200101.csv")));
        }

        [TestMethod]
        public void DeleteExpiredFiles_ZeroRetention_DeletesNothing()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "Log_20000101.csv"), "x");
            using (var writer = new DailyLogFileWriter(_folder, 0))
                Assert.AreEqual(0, writer.DeleteExpiredFiles(new DateTime(2026, 11, 11)));
        }

        [TestMethod]
        public void Write_Failure_DoesNotThrowAndReportsOnce()
        {
            // 폴더 자리에 파일이 있으면 폴더를 만들 수 없어 쓰기가 실패한다
            File.WriteAllText(_folder, "not a folder");
            try
            {
                int failures = 0;
                using (var writer = new DailyLogFileWriter(_folder, 0))
                {
                    writer.FailureStateChanged += (failing, _) => { if (failing) failures++; };
                    writer.Write(DateTime.Now, "INFO", "a");
                    writer.Write(DateTime.Now, "INFO", "b");
                    Assert.IsTrue(writer.IsFailing);
                }
                Assert.AreEqual(1, failures);
            }
            finally
            {
                File.Delete(_folder);
            }
        }

        [TestMethod]
        public void LoggerService_WritesToFileWithSameTime()
        {
            var time = new DateTime(2026, 11, 11, 8, 0, 0, 5);
            using (var logger = new LoggerService(new DailyLogFileWriter(_folder, 90)))
            {
                StringAssert.StartsWith(logger.FileLoggingStartMessage, "로그 파일 기록 시작");
                logger.Log(time, "공정 시작", "INFO");
            }
            var lines = File.ReadAllLines(Path.Combine(_folder, "Log_20261111.csv"), Encoding.UTF8);
            Assert.AreEqual("2026-11-11,08:00:00.005,INFO,\"공정 시작\"", lines[1]);
        }

        [TestMethod]
        public void LoggerService_WithoutWriter_HasNoStartMessage()
        {
            using (var logger = new LoggerService(null))
            {
                Assert.IsNull(logger.FileLoggingStartMessage);
                logger.Log("화면만", "INFO");
                Assert.AreEqual(1, logger.Count);
            }
        }
    }
}
