using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>서보 현재 위치 문자열 (X=Axis2, Y=Axis1). 원본 Axis2_is_PosData / Axis1_is_PosData.</summary>
    public sealed class ServoPositionReading
    {
        public ServoPositionReading(string rawX, string rawY)
        {
            RawX = rawX;
            RawY = rawY;
        }

        public string RawX { get; }
        public string RawY { get; }
    }

    /// <summary>티칭 위치와 서보 속도를 저장하는 곳 (원본 TeachingPositionsRepository / ServoSpeedRepository)</summary>
    public interface ITeachingStore
    {
        void SaveTeaching(TeachingPositionsData data);

        /// <summary>속도를 저장하고, 저장 뒤 프로그램이 쓰는 값을 돌려준다.</summary>
        ServoSpeedSettings SaveSpeeds(ServoSpeedSettings speeds);
    }

    /// <summary>
    /// TM 티칭 위치 화면 (원본 Forms.TeachingPositionForm).
    /// 원본과 다른 점:
    /// - 칸마다 '현재값' 버튼이 있어 그 칸의 축(X=Axis2, Y=Axis1)으로 바로 읽는다 (원본은 포커스 칸을 이름으로 추측).
    /// - 현재 서보 위치는 창이 떠 있는 동안 계속 갱신된다 (원본은 '위치 새로고침' 버튼).
    /// - 바뀐 칸은 표시하고 저장값을 옆에 보여 주며, 왼쪽 목록에 구역별로 바뀐 개수를 보여 준다.
    /// - 하강 Y(안착 Y − 하강 오프셋)를 바로 계산해 보여 준다.
    /// - 숫자가 아닌 입력, 0 이하 하강 오프셋은 저장 전에 빨갛게 보여 주고 저장을 막는다.
    ///   상승 Y가 안착 Y보다 낮으면 주황색으로 경고만 한다.
    /// - 저장하지 않은 변경이 있으면 닫을 때 한 번 묻는다.
    /// </summary>
    public sealed class TeachingPositionViewModel : ObservableObject
    {
        public const int FoupLevelCount = 5;

        // 원본: 위치 칸 ±5,000,000, 속도 칸 1,000 ~ 기본값의 5배
        private const long PosLimit = 5000000;
        private const long SpeedMin = 1000;
        private const long MaxSpeedValue = 5000000;
        private const long MaxDecelerationValue = 500000000;

        private readonly IDialogService _dialogs;
        private readonly Func<ServoPositionReading> _readPosition;
        private readonly ITeachingStore _store;
        private ServoSpeedSettings _savedSpeeds;
        private TeachingSection _selected;
        private string _currentPositionText = "현재 서보 위치: (미연결)";
        private bool _isConnected;
        private bool _suppress;

        /// <summary>저장한 뒤 (런타임 컨트롤러/시뮬레이션에 반영). 원본 onApplied.</summary>
        public Action<TeachingPositionsData> OnApplied;

        /// <summary>창 닫기. true면 저장하고 닫음</summary>
        public event EventHandler<bool> CloseRequested;

        /// <param name="readPosition">EtherCAT이 연결돼 있으면 현재 위치, 아니면 null</param>
        public TeachingPositionViewModel(IDialogService dialogs, TeachingPositionsData initial, ServoSpeedSettings savedSpeeds,
            Func<ServoPositionReading> readPosition, ITeachingStore store)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _readPosition = readPosition;
            _savedSpeeds = savedSpeeds ?? ServoSpeedSettings.CreateDefault();

            Common = new TeachingSection("공통 / Home",
                "하강 위치 = 안착(Land) Y − 하강 오프셋.\n" +
                "Home X/Y는 대기(TM) 위치 좌표입니다. 서보 '원점복귀' 버튼은 드라이브 HOME 센서 동작이라 Home 좌표와는 별개입니다.",
                new TeachingFieldGroup(null,
                    Pos("DescendOffset", "하강 오프셋", TeachAxis.None),
                    Pos("Home_X", "Home X (좌우)", TeachAxis.X),
                    Pos("Home_Y", "Home Y (상하)", TeachAxis.Y)));

            ChamberA = Chamber("A");
            ChamberB = Chamber("B");
            ChamberC = Chamber("C");
            FoupA = Foup("A");
            FoupB = Foup("B");

            Speed = new TeachingSection("속도",
                "저장하면 설정 파일에 기록되고, 다음 서보 ON(원점복귀 값은 다음 원점복귀)부터 적용됩니다.\n" +
                "지금 저장된 값보다 큰 값으로 바꾸면 저장할 때 한 번 더 확인합니다.",
                new TeachingFieldGroup("이송 (서보 ON 때 보냄)",
                    Spd("Velocity", "속도", MaxSpeedValue),
                    Spd("MaxVelocity", "최대 속도", MaxSpeedValue),
                    Spd("Acceleration", "가속", MaxSpeedValue),
                    Spd("Deceleration", "감속", MaxDecelerationValue)),
                new TeachingFieldGroup("원점복귀 (원점복귀 때 보냄)",
                    Spd("HomingVelocity", "원점복귀 속도", MaxSpeedValue),
                    Spd("HomingMaxVelocity", "원점복귀 최대 속도", MaxSpeedValue)));

            Sections = new[] { Common, ChamberA, ChamberB, ChamberC, FoupA, FoupB, Speed };
            foreach (var f in Fields)
            {
                f.ReadCurrentCommand = new RelayCommand(() => ReadCurrentInto(f), () => f.Axis != TeachAxis.None);
                f.PropertyChanged += (s, e) =>
                {
                    if (!_suppress && (e.PropertyName == nameof(TeachingField.Value) || e.PropertyName == nameof(TeachingField.HasParseError)))
                        Revalidate();
                };
            }

            SaveCommand = new RelayCommand(SaveAndApply, () => !HasErrors);
            CloseCommand = new RelayCommand(() => { if (ConfirmDiscard()) CloseRequested?.Invoke(this, false); });
            ResetCommand = new RelayCommand(Reset);

            Load(initial ?? TeachingPositionsData.CreateDefault(), _savedSpeeds, asBaseline: true);
            _selected = Common;
            RefreshPosition();
        }

        public TeachingSection Common { get; }
        public TeachingSection ChamberA { get; }
        public TeachingSection ChamberB { get; }
        public TeachingSection ChamberC { get; }
        public TeachingSection FoupA { get; }
        public TeachingSection FoupB { get; }
        public TeachingSection Speed { get; }
        public IReadOnlyList<TeachingSection> Sections { get; }

        public TeachingSection SelectedSection
        {
            get => _selected;
            set => SetProperty(ref _selected, value ?? Common);
        }

        public string CurrentPositionText
        {
            get => _currentPositionText;
            private set => SetProperty(ref _currentPositionText, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        public bool HasErrors => Fields.Any(f => f.HasError);
        public bool HasChanges => Fields.Any(f => f.IsModified);
        public int ChangeCount => Fields.Count(f => f.IsModified);

        public string StatusText =>
            HasErrors ? "빨간 칸을 고쳐야 저장할 수 있습니다."
            : HasChanges ? $"바뀐 칸 {ChangeCount}개 (아직 저장 안 됨)"
            : "바뀐 칸 없음";

        public ICommand SaveCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ResetCommand { get; }

        internal IEnumerable<TeachingField> Fields => Sections.SelectMany(s => s.AllFields);

        internal TeachingField F(string key) => Fields.First(f => f.Key == key);

        private static TeachingField Pos(string key, string label, TeachAxis axis) => new TeachingField(key, label, axis, -PosLimit, PosLimit);

        private static TeachingField Spd(string key, string label, long max) => new TeachingField(key, label, TeachAxis.None, SpeedMin, max);

        private static TeachingSection Chamber(string k) =>
            new TeachingSection("Chamber " + k, "하강 Y는 안착 Y − 하강 오프셋으로 자동 계산됩니다.",
                new TeachingFieldGroup(null,
                    Pos($"Chamber{k}_X", $"{k} X (좌우)", TeachAxis.X),
                    Pos($"Chamber{k}_LandY", $"{k} 안착 Y (Land)", TeachAxis.Y),
                    Pos($"Chamber{k}_RaiseY", $"{k} 상승 Y (Raise)", TeachAxis.Y)));

        private static TeachingSection Foup(string k)
        {
            var levels = Enumerable.Range(0, FoupLevelCount)
                .Select(i => new FoupLevelRow($"{i + 1}층",
                    Pos($"Foup{k}_LandY{i}", $"{i + 1}층 안착 Y", TeachAxis.Y),
                    Pos($"Foup{k}_RaiseY{i}", $"{i + 1}층 상승 Y", TeachAxis.Y)))
                .ToArray();
            return new TeachingSection("FOUP " + k, "층마다 안착 Y와 상승 Y를 넣습니다. 하강 Y는 안착 Y − 하강 오프셋입니다.",
                levels, new TeachingFieldGroup(null, Pos($"Foup{k}_X", $"FOUP {k} X (좌우)", TeachAxis.X)));
        }

        private void Load(TeachingPositionsData d, ServoSpeedSettings s, bool asBaseline)
        {
            _suppress = true;
            try
            {
                void Set(string key, long v)
                {
                    var f = F(key);
                    if (asBaseline) f.SetBaseline(v);
                    else f.Value = v;
                }

                Set("DescendOffset", d.DescendOffset);
                Set("Home_X", d.Home_X);
                Set("Home_Y", d.Home_Y);
                Set("ChamberA_X", d.ChamberA_X);
                Set("ChamberA_LandY", d.ChamberA_LandY);
                Set("ChamberA_RaiseY", d.ChamberA_RaiseY);
                Set("ChamberB_X", d.ChamberB_X);
                Set("ChamberB_LandY", d.ChamberB_LandY);
                Set("ChamberB_RaiseY", d.ChamberB_RaiseY);
                Set("ChamberC_X", d.ChamberC_X);
                Set("ChamberC_LandY", d.ChamberC_LandY);
                Set("ChamberC_RaiseY", d.ChamberC_RaiseY);
                Set("FoupA_X", d.FoupA_X);
                Set("FoupB_X", d.FoupB_X);
                for (int i = 0; i < FoupLevelCount; i++)
                {
                    Set($"FoupA_LandY{i}", At(d.FoupA_LandY, i));
                    Set($"FoupA_RaiseY{i}", At(d.FoupA_RaiseY, i));
                    Set($"FoupB_LandY{i}", At(d.FoupB_LandY, i));
                    Set($"FoupB_RaiseY{i}", At(d.FoupB_RaiseY, i));
                }

                Set("Velocity", s.Velocity);
                Set("MaxVelocity", s.MaxVelocity);
                Set("Acceleration", s.Acceleration);
                Set("Deceleration", s.Deceleration);
                Set("HomingVelocity", s.HomingVelocity);
                Set("HomingMaxVelocity", s.HomingMaxVelocity);
            }
            finally
            {
                _suppress = false;
            }
            Revalidate();
        }

        private static long At(long[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : 0;

        public TeachingPositionsData ToTeachingData()
        {
            var d = new TeachingPositionsData
            {
                DescendOffset = F("DescendOffset").Value,
                Home_X = F("Home_X").Value,
                Home_Y = F("Home_Y").Value,
                ChamberA_X = F("ChamberA_X").Value,
                ChamberA_LandY = F("ChamberA_LandY").Value,
                ChamberA_RaiseY = F("ChamberA_RaiseY").Value,
                ChamberB_X = F("ChamberB_X").Value,
                ChamberB_LandY = F("ChamberB_LandY").Value,
                ChamberB_RaiseY = F("ChamberB_RaiseY").Value,
                ChamberC_X = F("ChamberC_X").Value,
                ChamberC_LandY = F("ChamberC_LandY").Value,
                ChamberC_RaiseY = F("ChamberC_RaiseY").Value,
                FoupA_X = F("FoupA_X").Value,
                FoupB_X = F("FoupB_X").Value,
                FoupA_LandY = new long[FoupLevelCount],
                FoupA_RaiseY = new long[FoupLevelCount],
                FoupB_LandY = new long[FoupLevelCount],
                FoupB_RaiseY = new long[FoupLevelCount],
            };
            for (int i = 0; i < FoupLevelCount; i++)
            {
                d.FoupA_LandY[i] = F($"FoupA_LandY{i}").Value;
                d.FoupA_RaiseY[i] = F($"FoupA_RaiseY{i}").Value;
                d.FoupB_LandY[i] = F($"FoupB_LandY{i}").Value;
                d.FoupB_RaiseY[i] = F($"FoupB_RaiseY{i}").Value;
            }
            return d;
        }

        public ServoSpeedSettings ToSpeeds() => new ServoSpeedSettings
        {
            Velocity = F("Velocity").Value,
            MaxVelocity = F("MaxVelocity").Value,
            Acceleration = F("Acceleration").Value,
            Deceleration = F("Deceleration").Value,
            HomingVelocity = F("HomingVelocity").Value,
            HomingMaxVelocity = F("HomingMaxVelocity").Value,
        };

        private void Revalidate()
        {
            long offset = F("DescendOffset").Value;
            var offsetField = F("DescendOffset");

            foreach (var f in Fields)
            {
                f.Problem = f.HasParseError ? "숫자를 넣어 주세요." : null;
                f.Warning = null;
            }
            if (!offsetField.HasParseError && offset <= 0) offsetField.Problem = "1 이상이어야 합니다.";

            void Pair(TeachingField land, TeachingField raise, Action<string> setDescend)
            {
                setDescend(land.HasParseError || offsetField.HasError ? "-" : (land.Value - offset).ToString("N0", CultureInfo.CurrentCulture));
                if (!land.HasError && !raise.HasError && raise.Value <= land.Value)
                {
                    raise.Warning = "상승 Y가 안착 Y보다 높지 않습니다.";
                }
            }

            foreach (var c in new[] { (ChamberA, "A"), (ChamberB, "B"), (ChamberC, "C") })
            {
                var section = c.Item1;
                Pair(F($"Chamber{c.Item2}_LandY"), F($"Chamber{c.Item2}_RaiseY"), t => section.DescendText = $"하강 Y = {t}");
            }
            foreach (var foup in new[] { FoupA, FoupB })
            {
                foreach (var row in foup.Levels) Pair(row.Land, row.Raise, t => row.DescendText = t);
            }

            foreach (var s in Sections) s.RefreshSummary();
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(ChangeCount));
            OnPropertyChanged(nameof(StatusText));
            RelayCommand.Refresh();
        }

        /// <summary>원본 RefreshCurrentPositionLabel. 창이 떠 있는 동안 주기적으로 부른다.</summary>
        public void RefreshPosition()
        {
            try
            {
                var pos = _readPosition?.Invoke();
                IsConnected = pos != null;
                CurrentPositionText = pos == null
                    ? "현재 서보 위치: EtherCAT 미연결 (수동 입력만 가능)"
                    : $"현재 서보 위치: X(Axis2) = {pos.RawX}, Y(Axis1) = {pos.RawY}";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                CurrentPositionText = $"현재 서보 위치 읽기 실패: {ex.Message}";
            }
        }

        /// <summary>원본 ReadCurrentIntoFocused. 칸의 축을 알고 있으니 추측하지 않는다.</summary>
        internal void ReadCurrentInto(TeachingField field)
        {
            if (field == null || field.Axis == TeachAxis.None) return;
            ServoPositionReading pos;
            try
            {
                pos = _readPosition?.Invoke();
            }
            catch (Exception ex)
            {
                _dialogs.Error($"현재 위치 반영 오류: {ex.Message}", "오류");
                return;
            }
            RefreshPosition();
            if (pos == null)
            {
                _dialogs.Warn("EtherCAT이 연결되어야 현재 위치를 읽을 수 있습니다.", "연결 필요");
                return;
            }
            var raw = field.Axis == TeachAxis.X ? pos.RawX : pos.RawY;
            if (!TryParseLong(raw, out long value))
            {
                _dialogs.Warn("서보 위치 문자열을 숫자로 변환하지 못했습니다.", "읽기 실패");
                return;
            }
            field.Value = value;
        }

        private void SaveAndApply()
        {
            if (HasErrors) return;
            try
            {
                var data = ToTeachingData();
                var speeds = ToSpeeds();
                bool speedsChanged = !speeds.SameAs(_savedSpeeds);
                if (speedsChanged && speeds.AnyGreaterThan(_savedSpeeds)
                    && !_dialogs.Confirm(
                        "속도에서 지금 저장된 값보다 큰 값이 있습니다.\n다음 서보 ON부터 장비가 더 빠르거나 급하게 움직일 수 있습니다.\n\n그래도 저장하겠습니까?",
                        "속도 확인", defaultNo: true))
                {
                    return;
                }

                _store.SaveTeaching(data);
                OnApplied?.Invoke(data);

                if (speedsChanged)
                {
                    _savedSpeeds = _store.SaveSpeeds(speeds) ?? speeds;
                }

                Load(data, speedsChanged ? speeds : _savedSpeeds, asBaseline: true);
                _dialogs.Info(
                    speedsChanged
                        ? "티칭 위치를 저장하고 적용했습니다.\n속도는 설정 파일에 저장했고, 다음 서보 ON부터 적용됩니다."
                        : "티칭 위치를 저장하고 적용했습니다.",
                    "저장 완료");
                CloseRequested?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                _dialogs.Error($"저장 오류: {ex.Message}", "오류");
            }
        }

        private void Reset()
        {
            if (!_dialogs.Confirm("코드에 내장된 기본 티칭값과 기본 속도로 되돌리겠습니까?\n(아직 저장되지 않습니다)", "기본값 복원")) return;
            Load(TeachingPositionsData.CreateDefault(), ServoSpeedSettings.CreateDefault(), asBaseline: false);
        }

        /// <summary>저장 안 한 변경이 있으면 닫을지 묻는다. 닫아도 되면 true.</summary>
        public bool ConfirmDiscard()
        {
            if (!HasChanges) return true;
            return _dialogs.Confirm($"저장하지 않은 변경이 {ChangeCount}개 있습니다.\n저장하지 않고 닫겠습니까?", "닫기", defaultNo: true);
        }

        internal static bool TryParseLong(string raw, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-") return false;
            var t = raw.Trim();
            return long.TryParse(t, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
                || long.TryParse(t, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
        }
    }

    public enum TeachAxis
    {
        None,
        /// <summary>좌우 (Axis2)</summary>
        X,
        /// <summary>상하 (Axis1)</summary>
        Y,
    }

    /// <summary>왼쪽 목록의 구역 하나 (원본 탭 하나)</summary>
    public sealed class TeachingSection : ObservableObject
    {
        private string _descendText;
        private int _modifiedCount;
        private bool _hasError;

        public TeachingSection(string title, string hint, params TeachingFieldGroup[] groups)
            : this(title, hint, Array.Empty<FoupLevelRow>(), groups)
        {
        }

        public TeachingSection(string title, string hint, IReadOnlyList<FoupLevelRow> levels, params TeachingFieldGroup[] groups)
        {
            Title = title;
            Hint = hint;
            Levels = levels;
            Groups = groups;
        }

        public string Title { get; }
        public string Hint { get; }
        public IReadOnlyList<TeachingFieldGroup> Groups { get; }
        public IReadOnlyList<FoupLevelRow> Levels { get; }
        public bool HasLevels => Levels.Count > 0;

        public IEnumerable<TeachingField> AllFields =>
            Groups.SelectMany(g => g.Fields).Concat(Levels.SelectMany(l => new[] { l.Land, l.Raise }));

        /// <summary>챔버 구역의 하강 Y 계산 결과</summary>
        public string DescendText
        {
            get => _descendText;
            set
            {
                if (SetProperty(ref _descendText, value)) OnPropertyChanged(nameof(HasDescendText));
            }
        }

        public bool HasDescendText => !string.IsNullOrEmpty(_descendText);

        public int ModifiedCount
        {
            get => _modifiedCount;
            private set
            {
                if (SetProperty(ref _modifiedCount, value)) OnPropertyChanged(nameof(Badge));
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set => SetProperty(ref _hasError, value);
        }

        public string Badge => _modifiedCount > 0 ? _modifiedCount.ToString(CultureInfo.InvariantCulture) : null;

        internal void RefreshSummary()
        {
            ModifiedCount = AllFields.Count(f => f.IsModified);
            HasError = AllFields.Any(f => f.HasError);
        }
    }

    public sealed class TeachingFieldGroup
    {
        public TeachingFieldGroup(string header, params TeachingField[] fields)
        {
            Header = header;
            Fields = fields;
        }

        public string Header { get; }
        public bool HasHeader => !string.IsNullOrEmpty(Header);
        public IReadOnlyList<TeachingField> Fields { get; }
    }

    /// <summary>FOUP 한 층 (안착 Y / 상승 Y / 계산된 하강 Y)</summary>
    public sealed class FoupLevelRow : ObservableObject
    {
        private string _descendText;

        public FoupLevelRow(string name, TeachingField land, TeachingField raise)
        {
            Name = name;
            Land = land;
            Raise = raise;
        }

        public string Name { get; }
        public TeachingField Land { get; }
        public TeachingField Raise { get; }

        public string DescendText
        {
            get => _descendText;
            set => SetProperty(ref _descendText, value);
        }
    }

    /// <summary>정수 입력 칸 하나. 범위는 원본 NumericUpDown의 Minimum/Maximum과 같다.</summary>
    public sealed class TeachingField : ObservableObject
    {
        private long _value;
        private long _baseline;
        private string _text = "0";
        private bool _hasParseError;
        private string _problem;
        private string _warning;
        private long _min;
        private long _max;

        public TeachingField(string key, string label, TeachAxis axis, long min, long max)
        {
            Key = key;
            Label = label;
            Axis = axis;
            _min = min;
            _max = max;
        }

        public string Key { get; }
        public string Label { get; }
        public TeachAxis Axis { get; }
        public bool CanReadCurrent => Axis != TeachAxis.None;
        public string ReadCurrentToolTip => Axis == TeachAxis.X ? "현재 X(Axis2) 위치를 넣습니다" : "현재 Y(Axis1) 위치를 넣습니다";
        public long Min => _min;
        public long Max => _max;

        public ICommand ReadCurrentCommand { get; internal set; }

        public long Value
        {
            get => _value;
            set
            {
                long v = Math.Max(_min, Math.Min(_max, value));
                bool changed = SetProperty(ref _value, v);
                SetText(Format(v));
                SetParseError(false);
                if (changed)
                {
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        /// <summary>입력 칸 문자열. 숫자가 아니면 값은 그대로 두고 오류로 표시한다.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (TeachingPositionViewModel.TryParseLong(value, out long v))
                {
                    Value = v;
                    // 범위를 넘으면 잘린 값으로 다시 보여 준다
                    SetText(Format(_value));
                }
                else
                {
                    SetText(value);
                    SetParseError(true);
                }
            }
        }

        public bool HasParseError => _hasParseError;

        /// <summary>저장값 (이번 창을 열 때 또는 마지막 저장 때 값)</summary>
        public long Baseline => _baseline;

        public string BaselineText => $"저장값 {Format(_baseline)}";

        public bool IsModified => _hasParseError || _value != _baseline;

        /// <summary>저장을 막는 문제 (빨강)</summary>
        public string Problem
        {
            get => _problem;
            set
            {
                if (SetProperty(ref _problem, value)) OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_problem);

        /// <summary>저장은 되지만 확인해 볼 것 (주황)</summary>
        public string Warning
        {
            get => _warning;
            set
            {
                if (SetProperty(ref _warning, value)) OnPropertyChanged(nameof(HasWarning));
            }
        }

        public bool HasWarning => !string.IsNullOrEmpty(_warning);

        /// <summary>파일 값이 칸 범위 밖이어도 잘리지 않게 범위를 넓힌다 (원본 SetSpeedNum).</summary>
        internal void SetBaseline(long v)
        {
            if (v < _min) _min = v;
            if (v > _max) _max = v;
            _baseline = v;
            Value = v;
            OnPropertyChanged(nameof(Baseline));
            OnPropertyChanged(nameof(BaselineText));
            OnPropertyChanged(nameof(IsModified));
        }

        private void SetText(string text)
        {
            if (_text == text) return;
            _text = text;
            OnPropertyChanged(nameof(Text));
        }

        private void SetParseError(bool value)
        {
            if (_hasParseError == value) return;
            _hasParseError = value;
            OnPropertyChanged(nameof(HasParseError));
            OnPropertyChanged(nameof(IsModified));
        }

        private static string Format(long v) => v.ToString("N0", CultureInfo.CurrentCulture);
    }
}
