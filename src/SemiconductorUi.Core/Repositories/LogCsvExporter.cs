using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SemiconductorUi.Repositories
{
    /// <summary>로그 한 줄 (CSV 내보내기용)</summary>
    public readonly struct LogCsvRow
    {
        public LogCsvRow(DateTime time, string level, string message)
        {
            Time = time;
            Level = level ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DateTime Time { get; }
        public string Level { get; }
        public string Message { get; }
    }

    /// <summary>
    /// 로그 CSV 내보내기 (원본 Form1.ExportLogsToCsv).
    /// 원본은 레벨마다 파일을 나눠 HH:mm:ss만 적었다. 여기서는 화면에 보이는 로그를 파일 하나로,
    /// 날짜가 들어간 시각과 함께 적는다. 엑셀에서 한글이 깨지지 않도록 UTF-8 BOM을 붙인다.
    /// </summary>
    public static class LogCsvExporter
    {
        public const string Header = "Timestamp,Level,Message";

        /// <summary>원본과 같은 저장 폴더: 문서\SemiconductorUi_Logs</summary>
        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SemiconductorUi_Logs");

        public static void Write(TextWriter writer, IEnumerable<LogCsvRow> rows)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.WriteLine(Header);
            foreach (var r in rows)
            {
                writer.Write(r.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(Escape(r.Level));
                writer.Write(',');
                writer.WriteLine(Escape(r.Message));
            }
        }

        /// <summary>폴더에 SemiconductorUi_Logs_{tag}_{yyyyMMdd_HHmmss}.csv로 저장하고 경로를 돌려준다.</summary>
        public static string Save(IEnumerable<LogCsvRow> rows, string tag, DateTime now, string folder = null)
        {
            folder = folder ?? DefaultFolder;
            Directory.CreateDirectory(folder);
            var safeTag = string.IsNullOrWhiteSpace(tag) ? "ALL" : string.Concat(tag.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(folder, $"SemiconductorUi_Logs_{safeTag}_{now:yyyyMMdd_HHmmss}.csv");
            using (var sw = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                Write(sw, rows);
            }
            return path;
        }

        /// <summary>쉼표, 따옴표, 줄바꿈이 있으면 따옴표로 감싼다 (RFC 4180).</summary>
        public static string Escape(string value)
        {
            value = value ?? string.Empty;
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.StartsWith(" ") || value.EndsWith(" ");
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
