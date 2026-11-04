using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Models;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 설정 화면: 환경 알람 기준 (원본 Forms.ConfigForm, 관리자 전용).
    /// 원본과 다른 점: 경고 값이 알람 값보다 크면 저장하지 않고 그 칸을 빨갛게 보여 준다.
    /// </summary>
    public sealed class ConfigViewModel : ObservableObject
    {
        private readonly IDialogService _dialogs;
        private readonly Func<EnvThresholdSnapshot, bool> _save;

        /// <summary>저장한 뒤 (엔진 임계값 갱신)</summary>
        public Action<EnvThresholdSnapshot> OnSaved;

        /// <summary>TM 티칭 위치 버튼 (원본 OnOpenTeaching)</summary>
        public Action OnOpenTeaching;

        /// <summary>창 닫기. true면 저장하고 닫음</summary>
        public event EventHandler<bool> CloseRequested;

        /// <param name="save">저장하고 다시 읽어 확인까지 한 결과 (원본 btnSave_Click의 Save → Load 비교)</param>
        public ConfigViewModel(IDialogService dialogs, EnvThresholdSnapshot initial, Func<EnvThresholdSnapshot, bool> save)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _save = save ?? throw new ArgumentNullException(nameof(save));

            Temp = new ThresholdGroup("온도", null,
                new ThresholdField("경고 차이 (°C)", 0, 50, 2, "TempWarn"),
                new ThresholdField("알람 차이 (°C)", 0, 50, 2, "TempAlarm"));
            Rf = new ThresholdGroup("RF", RatioHint,
                new ThresholdField("경고 비율 (SV 대비)", 0, 1, 3, "RfWarnRatio"),
                new ThresholdField("알람 비율 (SV 대비)", 0, 1, 3, "RfAlarmRatio"));
            Press = new ThresholdGroup("압력", RatioHint,
                new ThresholdField("경고 비율 (SV 대비)", 0, 2, 3, "PressWarnRatio"),
                new ThresholdField("알람 비율 (SV 대비)", 0, 2, 3, "PressAlarmRatio"),
                new ThresholdField("경고 절대값 (Torr)", 0, 100, 2, "PressWarnAbs"),
                new ThresholdField("알람 절대값 (Torr)", 0, 100, 2, "PressAlarmAbs"));
            Gas = new ThresholdGroup("가스", null,
                new ThresholdField("경고 절대값 (sccm)", 0, 100, 1, "GasWarn"),
                new ThresholdField("알람 절대값 (sccm)", 0, 100, 1, "GasAlarm"),
                new ThresholdField("누설 경고 (sccm)", 0, 10, 1, "GasLeakWarn"),
                new ThresholdField("누설 알람 (sccm)", 0, 10, 1, "GasLeakAlarm"));

            foreach (var f in Fields) f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ThresholdField.Value)) Validate(); };

            SaveCommand = new RelayCommand(Save, () => !HasErrors);
            CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
            ResetCommand = new RelayCommand(Reset);
            TeachingCommand = new RelayCommand(() => OnOpenTeaching?.Invoke());

            Load(initial ?? CreateDefault());
        }

        private const string RatioHint = "비율은 소수로 입력합니다 (0.20 = SV의 20%).";

        public ThresholdGroup Temp { get; }
        public ThresholdGroup Rf { get; }
        public ThresholdGroup Press { get; }
        public ThresholdGroup Gas { get; }

        private IEnumerable<ThresholdField> Fields => new[] { Temp, Rf, Press, Gas }.SelectMany(g => g.Fields);

        private ThresholdField F(string key) => Fields.First(f => f.Key == key);

        public bool HasErrors => Fields.Any(f => f.HasError);

        public string ErrorText => HasErrors ? "경고 값은 알람 값보다 클 수 없습니다. 빨간 칸을 고쳐 주세요." : string.Empty;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand TeachingCommand { get; }

        /// <summary>원본 ConfigForm.CreateDefaultThresholds (AppSettings 기본값)</summary>
        public static EnvThresholdSnapshot CreateDefault() => new EnvThresholdSnapshot
        {
            TempWarn = AppSettings.AlarmTempWarn,
            TempAlarm = AppSettings.AlarmTempAlarm,
            PressWarnRatio = AppSettings.AlarmPressWarnRatio,
            PressAlarmRatio = AppSettings.AlarmPressAlarmRatio,
            PressWarnAbs = AppSettings.AlarmPressWarnAbs,
            PressAlarmAbs = AppSettings.AlarmPressAlarmAbs,
            RfWarnRatio = AppSettings.AlarmRfWarnRatio,
            RfAlarmRatio = AppSettings.AlarmRfAlarmRatio,
            GasWarn = AppSettings.AlarmGasWarn,
            GasAlarm = AppSettings.AlarmGasAlarm,
            GasLeakWarn = AppSettings.AlarmGasLeakWarn,
            GasLeakAlarm = AppSettings.AlarmGasLeakAlarm
        };

        private void Load(EnvThresholdSnapshot t)
        {
            F("TempWarn").Value = t.TempWarn;
            F("TempAlarm").Value = t.TempAlarm;
            F("PressWarnRatio").Value = t.PressWarnRatio;
            F("PressAlarmRatio").Value = t.PressAlarmRatio;
            F("PressWarnAbs").Value = t.PressWarnAbs;
            F("PressAlarmAbs").Value = t.PressAlarmAbs;
            F("RfWarnRatio").Value = t.RfWarnRatio;
            F("RfAlarmRatio").Value = t.RfAlarmRatio;
            F("GasWarn").Value = t.GasWarn;
            F("GasAlarm").Value = t.GasAlarm;
            F("GasLeakWarn").Value = t.GasLeakWarn;
            F("GasLeakAlarm").Value = t.GasLeakAlarm;
            Validate();
        }

        public EnvThresholdSnapshot ToSnapshot() => new EnvThresholdSnapshot
        {
            TempWarn = F("TempWarn").Value,
            TempAlarm = F("TempAlarm").Value,
            PressWarnRatio = F("PressWarnRatio").Value,
            PressAlarmRatio = F("PressAlarmRatio").Value,
            PressWarnAbs = F("PressWarnAbs").Value,
            PressAlarmAbs = F("PressAlarmAbs").Value,
            RfWarnRatio = F("RfWarnRatio").Value,
            RfAlarmRatio = F("RfAlarmRatio").Value,
            GasWarn = F("GasWarn").Value,
            GasAlarm = F("GasAlarm").Value,
            GasLeakWarn = F("GasLeakWarn").Value,
            GasLeakAlarm = F("GasLeakAlarm").Value
        };

        private static readonly (string warn, string alarm)[] Pairs =
        {
            ("TempWarn", "TempAlarm"), ("RfWarnRatio", "RfAlarmRatio"), ("PressWarnRatio", "PressAlarmRatio"),
            ("PressWarnAbs", "PressAlarmAbs"), ("GasWarn", "GasAlarm"), ("GasLeakWarn", "GasLeakAlarm"),
        };

        private void Validate()
        {
            foreach (var (warn, alarm) in Pairs)
            {
                bool bad = F(warn).Value > F(alarm).Value;
                F(warn).HasError = bad;
                F(alarm).HasError = bad;
            }
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(ErrorText));
            RelayCommand.Refresh();
        }

        private void Save()
        {
            if (HasErrors) return;
            try
            {
                var thresholds = ToSnapshot();
                if (_save(thresholds))
                {
                    OnSaved?.Invoke(thresholds);
                    _dialogs.Info("설정이 저장되었습니다.", "저장 완료");
                    CloseRequested?.Invoke(this, true);
                }
                else
                {
                    _dialogs.Error("설정 저장에 실패했습니다. 파일 저장에 문제가 있을 수 있습니다.", "저장 실패");
                }
            }
            catch (Exception ex)
            {
                _dialogs.Error($"설정 저장 중 오류가 발생했습니다:\n{ex.Message}", "오류");
            }
        }

        private void Reset()
        {
            if (_dialogs.Confirm("모든 설정을 기본값으로 복원하시겠습니까?", "확인"))
            {
                Load(CreateDefault());
            }
        }
    }

    public sealed class ThresholdGroup
    {
        public ThresholdGroup(string title, string hint, params ThresholdField[] fields)
        {
            Title = title;
            Hint = hint;
            Fields = fields;
        }

        public string Title { get; }
        public string Hint { get; }
        public bool HasHint => !string.IsNullOrEmpty(Hint);
        public IReadOnlyList<ThresholdField> Fields { get; }
    }

    /// <summary>임계값 입력 칸 하나. 범위는 원본 NumericUpDown의 Minimum/Maximum과 같다.</summary>
    public sealed class ThresholdField : ObservableObject
    {
        private double _value;
        private bool _hasError;

        public ThresholdField(string label, double min, double max, int decimals, string key)
        {
            Label = label;
            Min = min;
            Max = max;
            Decimals = decimals;
            Key = key;
        }

        public string Label { get; }
        public double Min { get; }
        public double Max { get; }
        public int Decimals { get; }
        public string Key { get; }

        public double Value
        {
            get => _value;
            set => SetProperty(ref _value, Math.Round(Math.Max(Min, Math.Min(Max, value)), Decimals));
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }
    }
}
