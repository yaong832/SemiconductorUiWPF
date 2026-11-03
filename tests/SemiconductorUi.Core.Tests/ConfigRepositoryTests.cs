using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// ConfigRepository 단위 테스트
    /// Phase 3.3 추가: Repository 클래스 테스트
    /// </summary>
    [TestClass]
    public class ConfigRepositoryTests
    {
        private string _testFilePath;
        private ConfigRepositoryImpl _repository;

        [TestInitialize]
        public void Setup()
        {
            // 테스트용 임시 파일 경로 생성
            _testFilePath = Path.Combine(Path.GetTempPath(), $"TestConfig_{Guid.NewGuid()}.xml");
            _repository = new ConfigRepositoryImpl(_testFilePath);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // 테스트 파일 정리
            try
            {
                if (File.Exists(_testFilePath))
                {
                    File.Delete(_testFilePath);
                }
                if (File.Exists(_testFilePath + ".tmp"))
                {
                    File.Delete(_testFilePath + ".tmp");
                }
                if (File.Exists(_testFilePath + ".bak"))
                {
                    File.Delete(_testFilePath + ".bak");
                }
            }
            catch
            {
                // 정리 실패는 무시
            }
        }

        [TestMethod]
        public void Load_FileNotExists_ReturnsDefaultConfig()
        {
            // Arrange & Act
            var config = _repository.Load();

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual(2.0, config.TempWarn);
            Assert.AreEqual(5.0, config.TempAlarm);
        }

        [TestMethod]
        public void Save_ValidConfig_SavesToFile()
        {
            // Arrange
            var config = new EnvThresholdSnapshot
            {
                TempWarn = 3.0,
                TempAlarm = 6.0,
                PressWarnRatio = 0.3,
                PressAlarmRatio = 0.6
            };

            // Act
            _repository.Save(config);

            // Assert
            Assert.IsTrue(File.Exists(_testFilePath));
            var loadedConfig = _repository.Load();
            Assert.AreEqual(3.0, loadedConfig.TempWarn);
            Assert.AreEqual(6.0, loadedConfig.TempAlarm);
        }

        [TestMethod]
        public void Save_NullConfig_SavesDefaultConfig()
        {
            // Arrange & Act
            _repository.Save(null);

            // Assert
            var loadedConfig = _repository.Load();
            Assert.IsNotNull(loadedConfig);
            // 기본값이 저장되어야 함
        }

        [TestMethod]
        public void CreateDefault_ReturnsDefaultConfig()
        {
            // Act
            var defaultConfig = _repository.CreateDefault();

            // Assert
            Assert.IsNotNull(defaultConfig);
            Assert.AreEqual(2.0, defaultConfig.TempWarn);
            Assert.AreEqual(5.0, defaultConfig.TempAlarm);
            Assert.AreEqual(0.2, defaultConfig.PressWarnRatio);
            Assert.AreEqual(0.5, defaultConfig.PressAlarmRatio);
        }

        [TestMethod]
        public void SaveAndLoad_RoundTrip_DataPreserved()
        {
            // Arrange
            var originalConfig = new EnvThresholdSnapshot
            {
                TempWarn = 2.5,
                TempAlarm = 5.5,
                PressWarnRatio = 0.25,
                PressAlarmRatio = 0.55,
                PressWarnAbs = 4.0,
                PressAlarmAbs = 11.0,
                RfWarnRatio = 0.15,
                RfAlarmRatio = 0.25,
                GasWarn = 6.0,
                GasAlarm = 11.0,
                GasLeakWarn = 1.5,
                GasLeakAlarm = 3.5
            };

            // Act
            _repository.Save(originalConfig);
            var loadedConfig = _repository.Load();

            // Assert
            Assert.AreEqual(originalConfig.TempWarn, loadedConfig.TempWarn);
            Assert.AreEqual(originalConfig.TempAlarm, loadedConfig.TempAlarm);
            Assert.AreEqual(originalConfig.PressWarnRatio, loadedConfig.PressWarnRatio);
            Assert.AreEqual(originalConfig.PressAlarmRatio, loadedConfig.PressAlarmRatio);
            Assert.AreEqual(originalConfig.PressWarnAbs, loadedConfig.PressWarnAbs);
            Assert.AreEqual(originalConfig.PressAlarmAbs, loadedConfig.PressAlarmAbs);
            Assert.AreEqual(originalConfig.RfWarnRatio, loadedConfig.RfWarnRatio);
            Assert.AreEqual(originalConfig.RfAlarmRatio, loadedConfig.RfAlarmRatio);
            Assert.AreEqual(originalConfig.GasWarn, loadedConfig.GasWarn);
            Assert.AreEqual(originalConfig.GasAlarm, loadedConfig.GasAlarm);
            Assert.AreEqual(originalConfig.GasLeakWarn, loadedConfig.GasLeakWarn);
            Assert.AreEqual(originalConfig.GasLeakAlarm, loadedConfig.GasLeakAlarm);
        }
    }
}

