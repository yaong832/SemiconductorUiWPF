using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 웨이퍼 하나가 챔버 하나를 거친 기록
    /// </summary>
    public sealed class WaferStepRecord
    {
        public WaferStepRecord(string chamber)
        {
            Chamber = chamber;
        }

        /// <summary>"A", "B", "C"</summary>
        public string Chamber { get; }

        /// <summary>챔버에 놓은 시각</summary>
        public DateTime? In { get; set; }

        /// <summary>공정 시작 (도어 닫힌 뒤)</summary>
        public DateTime? Start { get; set; }

        /// <summary>공정 끝 (남은 시간 0)</summary>
        public DateTime? End { get; set; }

        /// <summary>챔버에서 꺼낸 시각</summary>
        public DateTime? Out { get; set; }

        /// <summary>레시피 공정 시간 (초)</summary>
        public int SetSeconds { get; set; }

        /// <summary>실제 공정 시간 (초). 알람 홀드로 멈춘 시간도 들어갑니다.</summary>
        public double? ActualSeconds => Start.HasValue && End.HasValue ? (End.Value - Start.Value).TotalSeconds : (double?)null;

        public bool Visited => In.HasValue || Start.HasValue || Out.HasValue;

        public WaferStepRecord Copy()
        {
            return new WaferStepRecord(Chamber) { In = In, Start = Start, End = End, Out = Out, SetSeconds = SetSeconds };
        }
    }

    /// <summary>
    /// 웨이퍼 한 장의 공정 이력 (FOUP A에서 꺼낸 때부터 FOUP B에 넣을 때까지)
    /// </summary>
    public sealed class WaferRunRecord
    {
        public const string ResultDone = "완료";
        public const string ResultAborted = "중단";
        public const string ResultRunning = "진행 중";

        public string Run { get; set; }
        public DateTime LotStart { get; set; }
        public string Recipe { get; set; }
        public string Mode { get; set; }
        public string User { get; set; }
        public int WaferId { get; set; }
        public DateTime? FoupAOut { get; set; }
        public DateTime? FoupBIn { get; set; }

        /// <summary>완료·중단 시각 (진행 중이면 null)</summary>
        public DateTime? EndTime { get; set; }

        public string Result { get; set; } = ResultRunning;
        public string Note { get; set; } = "";

        public WaferStepRecord A { get; set; } = new WaferStepRecord("A");
        public WaferStepRecord B { get; set; } = new WaferStepRecord("B");
        public WaferStepRecord C { get; set; } = new WaferStepRecord("C");

        public IEnumerable<WaferStepRecord> Steps => new[] { A, B, C };

        public WaferStepRecord Step(string chamber)
        {
            switch (chamber)
            {
                case "A": return A;
                case "B": return B;
                case "C": return C;
                default: return null;
            }
        }

        /// <summary>거친 곳 (예: "FOUP A → A → B → FOUP B")</summary>
        public string Route
        {
            get
            {
                var parts = new List<string> { "FOUP A" };
                parts.AddRange(Steps.Where(s => s.Visited).Select(s => s.Chamber));
                if (FoupBIn.HasValue)
                {
                    parts.Add("FOUP B");
                }
                return string.Join(" → ", parts);
            }
        }

        /// <summary>FOUP A에서 꺼낸 뒤 끝날 때까지 (초)</summary>
        public double? TotalSeconds
        {
            get
            {
                var end = FoupBIn ?? EndTime;
                return FoupAOut.HasValue && end.HasValue ? (end.Value - FoupAOut.Value).TotalSeconds : (double?)null;
            }
        }

        public WaferRunRecord Copy()
        {
            return new WaferRunRecord
            {
                Run = Run,
                LotStart = LotStart,
                Recipe = Recipe,
                Mode = Mode,
                User = User,
                WaferId = WaferId,
                FoupAOut = FoupAOut,
                FoupBIn = FoupBIn,
                EndTime = EndTime,
                Result = Result,
                Note = Note,
                A = A.Copy(),
                B = B.Copy(),
                C = C.Copy()
            };
        }
    }

    /// <summary>
    /// 웨이퍼별 공정 이력: 어느 챔버에 언제 들어가 몇 초 공정했고 언제 FOUP B에 들어갔는지 남깁니다.
    /// - 장비 동작은 바꾸지 않습니다. 공정 흐름에서 알려 주는 시각만 기록합니다. 모든 메서드는 예외를 내지 않습니다.
    /// - 파일: Wafer_yyyyMMdd.csv (한 장이 끝나거나 중단될 때 한 줄, 지우지 않음)
    /// </summary>
    public sealed class WaferHistoryService
    {
        public const string FilePrefix = "Wafer_";

        internal static readonly string[] Header =
        {
            "Date", "Time", "Run", "Wafer", "Recipe", "Mode", "User", "Result", "Route", "FoupAOut",
            "A_In", "A_Start", "A_End", "A_Out", "A_SetSec", "A_ActSec",
            "B_In", "B_Start", "B_End", "B_Out", "B_SetSec", "B_ActSec",
            "C_In", "C_Start", "C_End", "C_Out", "C_SetSec", "C_ActSec",
            "FoupBIn", "TotalSec", "Note"
        };

        private readonly object _sync = new object();
        private readonly Action<string, string> _log;
        private readonly Func<DateTime> _clock;
        private readonly Dictionary<int, WaferRunRecord> _current = new Dictionary<int, WaferRunRecord>();
        private string _run;
        private DateTime _lotStart;
        private string _recipe;
        private string _mode;
        private string _user;
        private int _lotCount;
        private int _doneCount;
        private int _version;

        public WaferHistoryService(string folder, Action<string, string> log, Func<DateTime> clock = null)
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
            _log = log;
            _clock = clock ?? (() => DateTime.Now);
        }

        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SemiconductorUi_Logs", "Wafers");

        public string Folder { get; }

        /// <summary>바뀔 때마다 1씩 늘어납니다 (창이 다시 그릴지 판단)</summary>
        public int Version
        {
            get { lock (_sync) { return _version; } }
        }

        /// <summary>지금 공정 중인 작업 번호 (없으면 null)</summary>
        public string CurrentRun
        {
            get { lock (_sync) { return _run; } }
        }

        #region 공정 흐름에서 부르는 곳

        /// <summary>공정 시작: 새 작업 번호를 만듭니다. 끝나지 않은 이전 작업이 있으면 중단으로 닫습니다.</summary>
        public void LotStarted(int waferCount, string recipe, bool secondExposure, bool hardwareMode, string user)
        {
            Safe(() =>
            {
                CloseOpenLot("다음 공정을 시작할 때까지 끝나지 않음");
                var now = _clock();
                _run = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                _lotStart = now;
                _recipe = (recipe ?? "") + (secondExposure ? " (2차 노광)" : "");
                _mode = hardwareMode ? "장비" : "시뮬레이션";
                _user = user ?? "";
                _lotCount = waferCount;
                _doneCount = 0;
                _current.Clear();
                _version++;
                Log($"웨이퍼 이력: 작업 {_run} 시작 ({_mode}, {_recipe}, {waferCount}장)", "INFO");
            });
        }

        /// <summary>TM이 웨이퍼를 집음. from = "FOUP A" 또는 챔버 "A"/"B"/"C"</summary>
        public void NotePickup(int waferId, string from)
        {
            Safe(() =>
            {
                var r = Get(waferId);
                if (r == null)
                {
                    return;
                }

                var now = _clock();
                if (from == "FOUP A")
                {
                    if (!r.FoupAOut.HasValue)
                    {
                        r.FoupAOut = now;
                    }
                }
                else
                {
                    var step = r.Step(from);
                    if (step != null && !step.Out.HasValue)
                    {
                        step.Out = now;
                        // 공정 끝 알림을 못 받았으면 꺼낸 시각을 끝으로 두지 않습니다 (비워 둠).
                    }
                }
                _version++;
            });
        }

        /// <summary>TM이 웨이퍼를 놓음. to = 챔버 "A"/"B"/"C" 또는 "FOUP B"</summary>
        public void NoteDropoff(int waferId, string to)
        {
            Safe(() =>
            {
                var r = Get(waferId);
                if (r == null)
                {
                    return;
                }

                var now = _clock();
                if (to == "FOUP B")
                {
                    if (r.FoupBIn.HasValue)
                    {
                        return;
                    }

                    r.FoupBIn = now;
                    r.EndTime = now;
                    r.Result = WaferRunRecord.ResultDone;
                    _doneCount++;
                    _current.Remove(waferId);
                    Append(r);
                }
                else
                {
                    var step = r.Step(to);
                    if (step != null && !step.In.HasValue)
                    {
                        step.In = now;
                    }
                }
                _version++;
            });
        }

        /// <summary>챔버 공정 시작 (여러 번 불려도 처음 한 번만 남김)</summary>
        public void NoteProcessStart(int waferId, string chamber, int setSeconds)
        {
            Safe(() =>
            {
                var step = Get(waferId)?.Step(chamber);
                if (step == null || step.Start.HasValue)
                {
                    return;
                }

                step.Start = _clock();
                step.SetSeconds = setSeconds;
                _version++;
            });
        }

        /// <summary>챔버 공정 끝 (남은 시간 0)</summary>
        public void NoteProcessEnd(int waferId, string chamber)
        {
            Safe(() =>
            {
                var step = Get(waferId)?.Step(chamber);
                if (step == null || step.End.HasValue)
                {
                    return;
                }

                step.End = _clock();
                _version++;
            });
        }

        /// <summary>모든 웨이퍼가 FOUP B에 들어가 공정이 끝남</summary>
        public void LotFinished()
        {
            Safe(() =>
            {
                if (_run == null)
                {
                    return;
                }

                CloseOpenLot("공정 완료 때 FOUP B에 들어가지 않음");
                _version++;
            });
        }

        /// <summary>긴급 정지·공정 리셋 등으로 공정이 멈춤: 진행 중인 웨이퍼를 중단으로 남깁니다.</summary>
        public void LotAborted(string reason)
        {
            Safe(() =>
            {
                if (_run == null)
                {
                    return;
                }

                CloseOpenLot(reason);
                _version++;
            });
        }

        #endregion

        /// <summary>지금 진행 중인 웨이퍼 (복사본, 웨이퍼 번호 순)</summary>
        public List<WaferRunRecord> GetCurrent()
        {
            lock (_sync)
            {
                return _current.Values.OrderBy(r => r.WaferId).Select(r => r.Copy()).ToList();
            }
        }

        private WaferRunRecord Get(int waferId)
        {
            if (_run == null || waferId <= 0)
            {
                return null;
            }

            if (!_current.TryGetValue(waferId, out var r))
            {
                r = new WaferRunRecord
                {
                    Run = _run,
                    LotStart = _lotStart,
                    Recipe = _recipe,
                    Mode = _mode,
                    User = _user,
                    WaferId = waferId
                };
                _current[waferId] = r;
            }

            return r;
        }

        private void CloseOpenLot(string reason)
        {
            if (_run == null)
            {
                return;
            }

            var now = _clock();
            int closed = 0;
            foreach (var r in _current.Values.OrderBy(x => x.WaferId).ToList())
            {
                r.EndTime = now;
                r.Result = WaferRunRecord.ResultAborted;
                r.Note = reason ?? "";
                Append(r);
                closed++;
            }

            if (closed > 0)
            {
                Log($"웨이퍼 이력: 작업 {_run} 중단 - 완료 {_doneCount}장, 진행 중이던 {closed}장은 '중단'으로 남겼습니다 ({reason})", "WARN");
            }
            else
            {
                Log($"웨이퍼 이력: 작업 {_run} 끝 - 완료 {_doneCount}/{_lotCount}장", "INFO");
            }

            _current.Clear();
            _run = null;
        }

        private void Append(WaferRunRecord r)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var end = r.EndTime ?? _clock();
                string path = Path.Combine(Folder, FilePrefix + end.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
                bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(isNew)))
                {
                    if (isNew)
                    {
                        writer.WriteLine(string.Join(",", Header));
                    }

                    writer.WriteLine(string.Join(",", ToFields(r).Select(Csv)));
                    writer.Flush();
                    stream.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Log($"웨이퍼 이력을 파일에 쓰지 못했습니다: {ex.Message}", "WARN");
            }
        }

        private void Log(string message, string level)
        {
            try
            {
                _log?.Invoke(message, level);
            }
            catch
            {
                // 로그를 못 남겨도 기록은 계속합니다.
            }
        }

        private void Safe(Action action)
        {
            try
            {
                lock (_sync)
                {
                    action();
                }
            }
            catch (Exception ex)
            {
                // 기록 실패가 공정 흐름을 막지 않도록 여기서 끝냅니다.
                Log($"웨이퍼 이력 기록 오류: {ex.Message}", "WARN");
            }
        }

        #region CSV

        internal static IEnumerable<string> ToFields(WaferRunRecord r)
        {
            var end = r.EndTime ?? r.FoupBIn ?? DateTime.Now;
            yield return end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            yield return end.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            yield return r.Run;
            yield return r.WaferId.ToString(CultureInfo.InvariantCulture);
            yield return r.Recipe;
            yield return r.Mode;
            yield return r.User;
            yield return r.Result;
            yield return r.Route;
            yield return T(r.FoupAOut);
            foreach (var s in r.Steps)
            {
                yield return T(s.In);
                yield return T(s.Start);
                yield return T(s.End);
                yield return T(s.Out);
                yield return s.Visited || s.SetSeconds > 0 ? s.SetSeconds.ToString(CultureInfo.InvariantCulture) : "";
                yield return Sec(s.ActualSeconds);
            }
            yield return T(r.FoupBIn);
            yield return Sec(r.TotalSeconds);
            yield return r.Note;
        }

        private static string T(DateTime? t)
        {
            return t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
        }

        private static string Sec(double? s)
        {
            return s.HasValue ? s.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        private static string Csv(string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            return value.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>
        /// 기간 안의 기록 (최신이 앞). 파일을 못 읽으면 건너뜁니다.
        /// </summary>
        public List<WaferRunRecord> ReadRange(DateTime fromDate, DateTime toDate, int maxRows)
        {
            var rows = new List<WaferRunRecord>();
            for (var day = toDate.Date; day >= fromDate.Date && rows.Count < maxRows; day = day.AddDays(-1))
            {
                var dayRows = ReadDay(day);
                dayRows.Reverse();
                rows.AddRange(dayRows.Take(maxRows - rows.Count));
            }

            return rows;
        }

        internal List<WaferRunRecord> ReadDay(DateTime day)
        {
            var rows = new List<WaferRunRecord>();
            string path = Path.Combine(Folder, FilePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
            if (!File.Exists(path))
            {
                return rows;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    bool first = true;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (first)
                        {
                            first = false;
                            if (line.TrimStart('﻿').StartsWith("Date,", StringComparison.Ordinal))
                            {
                                continue;
                            }
                        }

                        var r = Parse(SplitCsv(line));
                        if (r != null)
                        {
                            rows.Add(r);
                        }
                    }
                }
            }
            catch
            {
                // 읽을 수 있는 데까지만 보여 줍니다.
            }

            return rows;
        }

        private static WaferRunRecord Parse(List<string> f)
        {
            if (f.Count < Header.Length)
            {
                return null;
            }

            int i = 0;
            DateTime end;
            if (!DateTime.TryParseExact(f[0] + " " + f[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
            {
                return null;
            }

            i = 2;
            var r = new WaferRunRecord { EndTime = end };
            r.Run = f[i++];
            int.TryParse(f[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out int waferId);
            r.WaferId = waferId;
            r.Recipe = f[i++];
            r.Mode = f[i++];
            r.User = f[i++];
            r.Result = f[i++];
            i++; // Route (다시 계산)
            r.FoupAOut = PT(f[i++]);
            foreach (var s in r.Steps)
            {
                s.In = PT(f[i++]);
                s.Start = PT(f[i++]);
                s.End = PT(f[i++]);
                s.Out = PT(f[i++]);
                int.TryParse(f[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out int set);
                s.SetSeconds = set;
                i++; // ActSec (다시 계산)
            }
            r.FoupBIn = PT(f[i++]);
            i++; // TotalSec (다시 계산)
            r.Note = f[i++];
            DateTime.TryParseExact(r.Run ?? "", "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lotStart);
            r.LotStart = lotStart;
            return r;
        }

        private static DateTime? PT(string s)
        {
            return DateTime.TryParseExact(s ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (DateTime?)null;
        }

        internal static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }

        #endregion
    }
}
