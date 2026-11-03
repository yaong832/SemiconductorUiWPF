using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SemiconductorUi.Repositories
{
    /// <summary>
    /// 서보 속도값 6개 (설정 파일 appSettings의 ServoDefault*, ServoHoming*)
    /// </summary>
    public sealed class ServoSpeedSettings
    {
        public long Velocity { get; set; }
        public long MaxVelocity { get; set; }
        public long Acceleration { get; set; }
        public long Deceleration { get; set; }
        public long HomingVelocity { get; set; }
        public long HomingMaxVelocity { get; set; }

        /// <summary>지금 프로그램이 쓰고 있는 값 (설정 파일, 없으면 코드 기본값)</summary>
        public static ServoSpeedSettings FromAppSettings()
        {
            return new ServoSpeedSettings
            {
                Velocity = AppSettings.ServoDefaultVelocity,
                MaxVelocity = AppSettings.ServoDefaultMaxVelocity,
                Acceleration = AppSettings.ServoDefaultAcceleration,
                Deceleration = AppSettings.ServoDefaultDeceleration,
                HomingVelocity = AppSettings.ServoHomingVelocity,
                HomingMaxVelocity = AppSettings.ServoHomingMaxVelocity
            };
        }

        /// <summary>코드에 내장된 기본값</summary>
        public static ServoSpeedSettings CreateDefault()
        {
            return new ServoSpeedSettings
            {
                Velocity = AppSettings.DefaultServoVelocity,
                MaxVelocity = AppSettings.DefaultServoMaxVelocity,
                Acceleration = AppSettings.DefaultServoAcceleration,
                Deceleration = AppSettings.DefaultServoDeceleration,
                HomingVelocity = AppSettings.DefaultServoHomingVelocity,
                HomingMaxVelocity = AppSettings.DefaultServoHomingMaxVelocity
            };
        }

        public bool SameAs(ServoSpeedSettings other)
        {
            return other != null
                && Velocity == other.Velocity
                && MaxVelocity == other.MaxVelocity
                && Acceleration == other.Acceleration
                && Deceleration == other.Deceleration
                && HomingVelocity == other.HomingVelocity
                && HomingMaxVelocity == other.HomingMaxVelocity;
        }

        /// <summary>하나라도 기준값보다 큰 값이 있으면 true (더 빠르거나 거칠어지는 변경)</summary>
        public bool AnyGreaterThan(ServoSpeedSettings baseline)
        {
            return baseline != null
                && (Velocity > baseline.Velocity
                    || MaxVelocity > baseline.MaxVelocity
                    || Acceleration > baseline.Acceleration
                    || Deceleration > baseline.Deceleration
                    || HomingVelocity > baseline.HomingVelocity
                    || HomingMaxVelocity > baseline.HomingMaxVelocity);
        }
    }

    /// <summary>
    /// 서보 속도값을 설정 파일(SemiconductorUi.exe.config)의 appSettings에 기록합니다.
    /// 해당 add 항목의 value만 바꾸고, 주석과 다른 항목은 그대로 둡니다.
    /// </summary>
    public static class ServoSpeedRepository
    {
        private static readonly object LockObject = new object();

        /// <summary>
        /// 이번 실행 중에 저장한 값. 설정 파일 다시 읽기(RefreshSection)에 기대지 않고
        /// AppSettings가 저장 직후부터 이 값을 쓰게 합니다. 다음 실행부터는 설정 파일에서 읽습니다.
        /// </summary>
        internal static ServoSpeedSettings SavedThisSession { get; private set; }

        public static string DefaultConfigPath => System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None).FilePath;

        public static void Save(ServoSpeedSettings speeds, string configPath = null)
        {
            if (speeds == null) throw new ArgumentNullException(nameof(speeds));

            lock (LockObject)
            {
                var path = configPath ?? DefaultConfigPath;
                var doc = File.Exists(path)
                    ? XDocument.Load(path, LoadOptions.PreserveWhitespace)
                    : new XDocument(new XElement("configuration"));

                var root = doc.Root ?? throw new InvalidOperationException("설정 파일에 configuration 항목이 없습니다.");
                var appSettings = root.Element("appSettings");
                if (appSettings == null)
                {
                    appSettings = new XElement("appSettings");
                    root.Add(appSettings);
                }

                SetValue(appSettings, "ServoDefaultVelocity", speeds.Velocity);
                SetValue(appSettings, "ServoDefaultMaxVelocity", speeds.MaxVelocity);
                SetValue(appSettings, "ServoDefaultDeceleration", speeds.Deceleration);
                SetValue(appSettings, "ServoDefaultAcceleration", speeds.Acceleration);
                SetValue(appSettings, "ServoHomingVelocity", speeds.HomingVelocity);
                SetValue(appSettings, "ServoHomingMaxVelocity", speeds.HomingMaxVelocity);

                // 임시 파일에 쓴 뒤 바꿔치기 (쓰는 도중 꺼져도 원래 파일이 남도록)
                var tempPath = path + ".tmp";
                doc.Save(tempPath, SaveOptions.DisableFormatting);
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(tempPath, path);
                try
                {
                    if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                }
                catch
                {
                }

                if (configPath == null)
                {
                    SavedThisSession = new ServoSpeedSettings
                    {
                        Velocity = speeds.Velocity,
                        MaxVelocity = speeds.MaxVelocity,
                        Acceleration = speeds.Acceleration,
                        Deceleration = speeds.Deceleration,
                        HomingVelocity = speeds.HomingVelocity,
                        HomingMaxVelocity = speeds.HomingMaxVelocity
                    };
                }
            }
        }

        private static void SetValue(XElement appSettings, string key, long value)
        {
            var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var add = appSettings.Elements("add").FirstOrDefault(e => (string)e.Attribute("key") == key);
            if (add != null)
            {
                add.SetAttributeValue("value", text);
                return;
            }
            appSettings.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", text)));
        }
    }
}
