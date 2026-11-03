using System;
using System.IO;
using System.Xml.Serialization;
using SemiconductorUi.Models;

namespace SemiconductorUi.Repositories
{
    /// <summary>
    /// TM 티칭 위치 저장소 (TeachingPositions.xml)
    /// </summary>
    public static class TeachingPositionsRepository
    {
        private static readonly object LockObject = new object();

        public static string DefaultFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TeachingPositions.xml");

        public static TeachingPositionsData Load(string filePath = null)
        {
            lock (LockObject)
            {
                var path = filePath ?? DefaultFilePath;
                return ExceptionHandler.SafeFileOperation(() =>
                {
                    if (!File.Exists(path))
                    {
                        return TeachingPositionsData.CreateDefault();
                    }

                    using (var fs = File.OpenRead(path))
                    {
                        var ser = new XmlSerializer(typeof(TeachingPositionsData));
                        var data = (TeachingPositionsData)ser.Deserialize(fs);
                        return Normalize(data) ?? TeachingPositionsData.CreateDefault();
                    }
                }, TeachingPositionsData.CreateDefault(), "TeachingPositions.xml");
            }
        }

        public static void Save(TeachingPositionsData data, string filePath = null)
        {
            lock (LockObject)
            {
                var path = filePath ?? DefaultFilePath;
                ExceptionHandler.SafeFileOperation(() =>
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var payload = Normalize(data) ?? TeachingPositionsData.CreateDefault();
                    var tempPath = path + ".tmp";
                    using (var fs = File.Create(tempPath))
                    {
                        var ser = new XmlSerializer(typeof(TeachingPositionsData));
                        ser.Serialize(fs, payload);
                    }

                    if (File.Exists(path))
                    {
                        File.Copy(path, path + ".bak", true);
                        File.Delete(path);
                    }

                    File.Move(tempPath, path);

                    try
                    {
                        if (File.Exists(path + ".bak"))
                        {
                            File.Delete(path + ".bak");
                        }
                    }
                    catch
                    {
                    }
                }, "TeachingPositions.xml");
            }
        }

        private static TeachingPositionsData Normalize(TeachingPositionsData data)
        {
            if (data == null) return null;
            data.FoupA_LandY = EnsureFive(data.FoupA_LandY, new long[] { 102379, 782378, 1432388, 2119399, 2818463 });
            data.FoupA_RaiseY = EnsureFive(data.FoupA_RaiseY, new long[] { 302380, 982378, 1627604, 2332102, 3018457 });
            data.FoupB_LandY = EnsureFive(data.FoupB_LandY, new long[] { 102379, 782378, 1432388, 2119399, 2818463 });
            data.FoupB_RaiseY = EnsureFive(data.FoupB_RaiseY, new long[] { 302380, 982378, 1627604, 2332102, 3018457 });
            if (data.DescendOffset <= 0) data.DescendOffset = 30000;
            return data;
        }

        private static long[] EnsureFive(long[] source, long[] defaults)
        {
            var result = (long[])defaults.Clone();
            if (source == null) return result;
            for (int i = 0; i < 5 && i < source.Length; i++)
            {
                result[i] = source[i];
            }
            return result;
        }
    }
}
