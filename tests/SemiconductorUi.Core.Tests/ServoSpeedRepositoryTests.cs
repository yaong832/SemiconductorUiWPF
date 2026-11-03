using System;
using System.IO;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 티칭 창 속도 탭이 설정 파일의 속도 항목만 바꾸고, 주석과 다른 항목은 그대로 두는지 확인합니다.
    /// </summary>
    [TestClass]
    public class ServoSpeedRepositoryTests
    {
        private string _dir;
        private string _path;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ServoSpeedRepositoryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "SemiconductorUi.exe.config");
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static ServoSpeedSettings Sample()
        {
            return new ServoSpeedSettings
            {
                Velocity = 300000,
                MaxVelocity = 310000,
                Acceleration = 320000,
                Deceleration = 90000000,
                HomingVelocity = 200000,
                HomingMaxVelocity = 250000
            };
        }

        private string Value(string key)
        {
            foreach (var add in XDocument.Load(_path).Root.Element("appSettings").Elements("add"))
            {
                if ((string)add.Attribute("key") == key) return (string)add.Attribute("value");
            }
            return null;
        }

        [TestMethod]
        public void Save_ChangesOnlySpeedValues_KeepsCommentsAndOtherKeys()
        {
            File.WriteAllText(_path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<configuration>\n" +
                "    <appSettings>\n" +
                "        <add key=\"MaxFoupCapacity\" value=\"5\" />\n" +
                "        <!-- 서보모터 파라미터 설정 -->\n" +
                "        <add key=\"ServoDefaultVelocity\" value=\"1000000\" />\n" +
                "        <add key=\"ServoDefaultMaxVelocity\" value=\"1000000\" />\n" +
                "        <add key=\"ServoDefaultDeceleration\" value=\"100000000\" />\n" +
                "        <add key=\"ServoDefaultAcceleration\" value=\"1000000\" />\n" +
                "        <add key=\"ServoHomingVelocity\" value=\"300000\" />\n" +
                "        <add key=\"ServoHomingMaxVelocity\" value=\"500000\" />\n" +
                "    </appSettings>\n" +
                "</configuration>\n");

            ServoSpeedRepository.Save(Sample(), _path);

            Assert.AreEqual("300000", Value("ServoDefaultVelocity"));
            Assert.AreEqual("310000", Value("ServoDefaultMaxVelocity"));
            Assert.AreEqual("320000", Value("ServoDefaultAcceleration"));
            Assert.AreEqual("90000000", Value("ServoDefaultDeceleration"));
            Assert.AreEqual("200000", Value("ServoHomingVelocity"));
            Assert.AreEqual("250000", Value("ServoHomingMaxVelocity"));
            Assert.AreEqual("5", Value("MaxFoupCapacity"));

            var text = File.ReadAllText(_path);
            StringAssert.Contains(text, "<!-- 서보모터 파라미터 설정 -->");
            Assert.IsFalse(File.Exists(_path + ".tmp"));
            Assert.IsFalse(File.Exists(_path + ".bak"));
        }

        [TestMethod]
        public void Save_AddsMissingSpeedKeys()
        {
            File.WriteAllText(_path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<configuration>\n" +
                "    <appSettings>\n" +
                "        <add key=\"MaxFoupCapacity\" value=\"5\" />\n" +
                "    </appSettings>\n" +
                "</configuration>\n");

            ServoSpeedRepository.Save(Sample(), _path);

            Assert.AreEqual("300000", Value("ServoDefaultVelocity"));
            Assert.AreEqual("250000", Value("ServoHomingMaxVelocity"));
            Assert.AreEqual("5", Value("MaxFoupCapacity"));
        }

        [TestMethod]
        public void AnyGreaterThan_DetectsFasterValue()
        {
            var saved = Sample();
            var same = Sample();
            Assert.IsTrue(same.SameAs(saved));
            Assert.IsFalse(same.AnyGreaterThan(saved));

            var slower = Sample();
            slower.Velocity = 100000;
            Assert.IsFalse(slower.SameAs(saved));
            Assert.IsFalse(slower.AnyGreaterThan(saved));

            var faster = Sample();
            faster.Deceleration = saved.Deceleration + 1;
            Assert.IsTrue(faster.AnyGreaterThan(saved));
        }
    }
}
