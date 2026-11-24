using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>"n초 ON 후 자동 OFF" 입력 한 칸 (원본 NumericUpDown 1~3600 + [실행])</summary>
    public sealed class TimedControlRow : ObservableObject
    {
        private readonly EquipmentManualControl _control;
        private readonly Func<bool> _canRun;
        private string _secondsText = "5";
        private string _remainingText = "";
        private bool _isEnabled;

        public TimedControlRow(EquipmentManualControl control, string key, Func<bool> canRun)
        {
            _control = control;
            _canRun = canRun;
            Key = key;
            RunCommand = new RelayCommand(Run, canRun);
            _isEnabled = canRun();
        }

        public string Key { get; }

        /// <summary>초 입력 칸 사용 여부 (원본: 연결되어 있을 때만)</summary>
        public bool IsEnabled { get => _isEnabled; private set => SetProperty(ref _isEnabled, value); }

        internal void RefreshEnabled() => IsEnabled = _canRun();

        public string SecondsText { get => _secondsText; set => SetProperty(ref _secondsText, value); }

        /// <summary>도는 중이면 "남은 3초" (WPF 추가)</summary>
        public string RemainingText { get => _remainingText; private set => SetProperty(ref _remainingText, value); }

        public bool IsRunning => !string.IsNullOrEmpty(RemainingText);

        public ICommand RunCommand { get; }

        private void Run()
        {
            // 숫자가 아니면 0으로 넘겨 원본과 같은 "1초 이상으로 입력하세요." 안내를 받는다
            if (!int.TryParse((SecondsText ?? "").Trim(), out int seconds)) seconds = 0;
            _control.StartTimedAction(Key, seconds);
        }

        internal void UpdateRemaining(ManualTimedAction active, DateTime now)
        {
            string text = "";
            if (active != null)
            {
                int left = (int)Math.Ceiling((active.EndsAt - now).TotalSeconds);
                text = $"남은 {Math.Max(0, left)}초";
            }
            if (text != _remainingText)
            {
                RemainingText = text;
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    /// <summary>챔버 하나 (원본 CreateChamberGroup: 램프 ON/OFF + 타이머, 도어 열기/닫기 + 타이머)</summary>
    public sealed class ChamberControlGroup : ObservableObject
    {
        private bool? _lampOutput;
        private bool? _doorOpen;

        public ChamberControlGroup(string title, EquipmentRegion region, EquipmentManualControl control, Func<bool> connected)
        {
            Title = title;
            Region = region;
            LampOnCommand = new RelayCommand(() => control.SetChamberLamp(region, true), connected);
            LampOffCommand = new RelayCommand(() => control.ChamberLampOffButton(region), connected);
            DoorOpenCommand = new RelayCommand(() => control.SetChamberDoor(region, true), connected);
            DoorCloseCommand = new RelayCommand(() => control.ChamberDoorCloseButton(region), connected);
            LampTimer = new TimedControlRow(control, EquipmentManualControl.TimedKey(region, false), connected);
            DoorTimer = new TimedControlRow(control, EquipmentManualControl.TimedKey(region, true), connected);
        }

        public string Title { get; }
        public EquipmentRegion Region { get; }
        public ICommand LampOnCommand { get; }
        public ICommand LampOffCommand { get; }
        public ICommand DoorOpenCommand { get; }
        public ICommand DoorCloseCommand { get; }
        public TimedControlRow LampTimer { get; }
        public TimedControlRow DoorTimer { get; }

        /// <summary>램프 출력 (연결 안 됨이면 null)</summary>
        public bool? LampOutput
        {
            get => _lampOutput;
            internal set
            {
                if (SetProperty(ref _lampOutput, value)) OnPropertyChanged(nameof(LampStateText));
            }
        }

        /// <summary>도어 출력으로 본 열림 (두 출력이 맞지 않으면 null)</summary>
        public bool? DoorOpen
        {
            get => _doorOpen;
            internal set
            {
                if (SetProperty(ref _doorOpen, value)) OnPropertyChanged(nameof(DoorStateText));
            }
        }

        public string LampStateText => _lampOutput == null ? "-" : _lampOutput.Value ? "출력 ON" : "출력 OFF";
        public string DoorStateText => _doorOpen == null ? "-" : _doorOpen.Value ? "열림 출력" : "닫힘 출력";
    }

    /// <summary>3색 램프 한 색 (원본 CreateMainLampGroup)</summary>
    public sealed class MainLampControlRow : ObservableObject
    {
        private bool? _output;

        public MainLampControlRow(string name, int outputIndex, Brush color, EquipmentManualControl control, Func<bool> connected)
        {
            Name = name;
            OutputIndex = outputIndex;
            Color = color;
            OnCommand = new RelayCommand(() => control.SetMainLamp(outputIndex, true), connected);
            OffCommand = new RelayCommand(() => control.MainLampOffButton(outputIndex), connected);
            Timer = new TimedControlRow(control, EquipmentManualControl.MainLampKey(outputIndex), connected);
        }

        public string Name { get; }
        public int OutputIndex { get; }
        public Brush Color { get; }
        public ICommand OnCommand { get; }
        public ICommand OffCommand { get; }
        public TimedControlRow Timer { get; }

        public bool? Output
        {
            get => _output;
            internal set
            {
                if (SetProperty(ref _output, value)) OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText => _output == null ? "-" : _output.Value ? "출력 ON" : "출력 OFF";
    }

    /// <summary>
    /// 장비 제어 창 (원본 Forms.EquipmentControlForm "장비 직접 제어").
    /// 명령은 Core <see cref="EquipmentManualControl"/>이 보내고, 이 모델은 버튼과 0.5초 상태 갱신을 맡는다.
    /// WPF 추가: 각 줄 옆에 지금 출력 상태(읽기만), 도는 타이머의 남은 초, 서보 ON·원점복귀 중 버튼 끄기.
    /// </summary>
    public sealed class EquipmentControlViewModel : ObservableObject, IDisposable
    {
        private readonly IEthercatDevice _device;
        private readonly Func<DateTime> _now;
        private DispatcherTimer _timer;
        private bool _lastConnected;
        private string _cylinderStateText = "-";
        private string _vacuumStateText = "-";
        private string _exhaustStateText = "-";

        public EquipmentControlViewModel(EquipmentManualControl control, IEthercatDevice device, Func<DateTime> now = null)
        {
            Control = control ?? throw new ArgumentNullException(nameof(control));
            _device = device;
            _now = now ?? (() => DateTime.Now);
            _lastConnected = control.IsConnected;
            Func<bool> connected = () => Control.IsConnected;

            Chambers = new[]
            {
                new ChamberControlGroup("Chamber A", EquipmentRegion.ChamberA, control, connected),
                new ChamberControlGroup("Chamber B", EquipmentRegion.ChamberB, control, connected),
                new ChamberControlGroup("Chamber C", EquipmentRegion.ChamberC, control, connected),
            };
            MainLamps = new[]
            {
                new MainLampControlRow("적색", 0, Res("Danger"), control, connected),
                new MainLampControlRow("황색", 1, Res("Warning"), control, connected),
                new MainLampControlRow("녹색", 2, Res("Success"), control, connected),
            };

            AllDoorsOpenCommand = new RelayCommand(() => control.SetAllDoors(true), connected);
            AllDoorsCloseCommand = new RelayCommand(() => control.SetAllDoors(false), connected);
            AllLampsOnCommand = new RelayCommand(() => control.SetAllLamps(true), connected);
            AllLampsOffCommand = new RelayCommand(() => control.SetAllLamps(false), connected);

            Func<bool> servoReady = () => Control.IsConnected && !Control.IsBusy;
            ServoOnCommand = new RelayCommand(() => _ = control.ServoOnAsync(), servoReady);
            ServoOffCommand = new RelayCommand(control.ServoOff, connected);
            HomeCommand = new RelayCommand(() => _ = control.HomeAsync(), servoReady);
            // 원본: 티칭 위치 버튼은 연결과 관계없이 누를 수 있다 (오프라인 편집)
            TeachingCommand = new RelayCommand(control.RequestTeaching);
            CylinderExtendCommand = new RelayCommand(control.CylinderExtend, connected);
            CylinderRetractCommand = new RelayCommand(control.CylinderRetract, connected);
            VacuumOnCommand = new RelayCommand(control.VacuumOn, connected);
            VacuumOffCommand = new RelayCommand(control.VacuumOff, connected);
            ExhaustOnCommand = new RelayCommand(control.ExhaustOn, connected);
            ExhaustOffCommand = new RelayCommand(control.ExhaustOff, connected);
            VacuumTimer = new TimedControlRow(control, EquipmentManualControl.VacuumKey, connected);
            ExhaustTimer = new TimedControlRow(control, EquipmentManualControl.ExhaustKey, connected);
            EmergencyStopCommand = new RelayCommand(control.EmergencyStop);

            control.Changed += OnControlChanged;
            RefreshIo();
        }

        public EquipmentManualControl Control { get; }

        public IReadOnlyList<ChamberControlGroup> Chambers { get; }
        public IReadOnlyList<MainLampControlRow> MainLamps { get; }

        public string StatusText => Control.StatusText;
        public Brush StatusBrush => ToneBrush(Control.StatusTone);
        public string ServoStatusText => Control.ServoStatusText;
        public Brush ServoStatusBrush => ToneBrush(Control.ServoStatusTone);
        public bool IsConnected => Control.IsConnected;
        public bool IsBusy => Control.IsBusy;

        public string CylinderStateText { get => _cylinderStateText; private set => SetProperty(ref _cylinderStateText, value); }
        public string VacuumStateText { get => _vacuumStateText; private set => SetProperty(ref _vacuumStateText, value); }
        public string ExhaustStateText { get => _exhaustStateText; private set => SetProperty(ref _exhaustStateText, value); }

        public string TimerHint => "타이머: 초 입력 후 [실행] → 해당 시간만큼 ON(열림) 후 자동 OFF(닫힘)";

        public string ProcessHint =>
            "자동 공정 실행/일시정지 중에는 도어·램프 ON·실린더·진공·서보 수동 제어를 막습니다. 급히 멈춰야 하면 긴급 정지를 누르세요.";

        public ICommand AllDoorsOpenCommand { get; }
        public ICommand AllDoorsCloseCommand { get; }
        public ICommand AllLampsOnCommand { get; }
        public ICommand AllLampsOffCommand { get; }
        public ICommand ServoOnCommand { get; }
        public ICommand ServoOffCommand { get; }
        public ICommand HomeCommand { get; }
        public ICommand TeachingCommand { get; }
        public ICommand CylinderExtendCommand { get; }
        public ICommand CylinderRetractCommand { get; }
        public ICommand VacuumOnCommand { get; }
        public ICommand VacuumOffCommand { get; }
        public ICommand ExhaustOnCommand { get; }
        public ICommand ExhaustOffCommand { get; }
        public TimedControlRow VacuumTimer { get; }
        public TimedControlRow ExhaustTimer { get; }
        public ICommand EmergencyStopCommand { get; }

        private IEnumerable<TimedControlRow> AllTimers =>
            Chambers.SelectMany(c => new[] { c.LampTimer, c.DoorTimer })
                .Concat(MainLamps.Select(l => l.Timer))
                .Concat(new[] { VacuumTimer, ExhaustTimer });

        /// <summary>창이 열렸다: 0.5초마다 서보 상태와 출력을 읽는다 (원본 statusUpdateTimer 500ms)</summary>
        public void Start()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EquipmentManualControl.StatusPollIntervalMs) };
                _timer.Tick += (s, e) => Poll();
            }
            _timer.Start();
        }

        /// <summary>한 번 읽기 (타이머 틱, 시험·스냅샷)</summary>
        public void Poll()
        {
            bool connected = Control.IsConnected;
            if (connected != _lastConnected)
            {
                _lastConnected = connected;
                Control.UpdateConnectionStatus();
                OnPropertyChanged(nameof(IsConnected));
                RelayCommand.Refresh();
            }
            if (connected)
            {
                try
                {
                    Control.UpdateServoStatusFromDevice();
                }
                catch (Exception ex)
                {
                    // 타이머 오류는 조용히 처리 (로그 스팸 방지)
                    System.Diagnostics.Debug.WriteLine($"EquipmentControl 상태 업데이트 오류: {ex.Message}");
                }
            }
            RefreshIo();
            UpdateTimers();
        }

        private void OnControlChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(ServoStatusText));
            OnPropertyChanged(nameof(ServoStatusBrush));
            OnPropertyChanged(nameof(IsBusy));
            UpdateTimers();
            RefreshIo();
            RelayCommand.Refresh();
        }

        private void UpdateTimers()
        {
            var active = Control.ActiveTimers.ToDictionary(t => t.Key);
            DateTime now = _now();
            foreach (var row in AllTimers)
            {
                active.TryGetValue(row.Key, out var a);
                row.UpdateRemaining(a, now);
                row.RefreshEnabled();
            }
        }

        /// <summary>지금 출력·센서를 읽어 줄 옆에 보인다 (읽기만 한다)</summary>
        private void RefreshIo()
        {
            if (!Control.IsConnected || _device == null)
            {
                foreach (var c in Chambers)
                {
                    c.LampOutput = null;
                    c.DoorOpen = null;
                }
                foreach (var l in MainLamps) l.Output = null;
                CylinderStateText = "-";
                VacuumStateText = "-";
                ExhaustStateText = "-";
                return;
            }
            try
            {
                bool Out(int i) => _device.GetDigitalOutput(i);
                SetChamber(Chambers[0], Out(3), Out(4), Out(5));
                SetChamber(Chambers[1], Out(6), Out(7), Out(8));
                SetChamber(Chambers[2], Out(9), Out(10), Out(11));
                for (int i = 0; i < MainLamps.Count; i++) MainLamps[i].Output = Out(MainLamps[i].OutputIndex);

                bool extend = Out(12), retract = Out(13);
                bool retractedSensor = _device.Digital_Input(12);
                string output = extend && !retract ? "전진 출력" : retract && !extend ? "후진 출력" : "출력 없음";
                CylinderStateText = $"{output} · 후진 센서 {(retractedSensor ? "ON" : "OFF")}";
                VacuumStateText = Out(14) ? "진공 출력 ON" : "진공 출력 OFF";
                ExhaustStateText = Out(15) ? "배기 출력 ON" : "배기 출력 OFF";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EquipmentControl 출력 읽기 오류: {ex.Message}");
            }
        }

        private static void SetChamber(ChamberControlGroup group, bool lamp, bool doorFirst, bool doorSecond)
        {
            group.LampOutput = lamp;
            // 열기: first OFF + second ON / 닫기: first ON + second OFF (원본 ControlChamberDoor)
            group.DoorOpen = doorSecond && !doorFirst ? true : doorFirst && !doorSecond ? false : (bool?)null;
        }

        private static Brush ToneBrush(ManualStatusTone tone)
        {
            switch (tone)
            {
                case ManualStatusTone.Success: return Res("Success");
                case ManualStatusTone.Warning: return Res("Warning");
                case ManualStatusTone.Danger: return Res("Danger");
                case ManualStatusTone.Primary: return Res("Primary");
                case ManualStatusTone.Secondary: return Res("TextSecondary");
                default: return Res("TextPrimary");
            }
        }

        private static Brush Res(string key) =>
            Application.Current?.TryFindResource(key) as Brush ?? Brushes.Black;

        /// <summary>창이 닫혔다: 타이머를 멈추고 돌던 "n초 후 OFF"도 취소한다 (원본 OnFormClosed)</summary>
        public void Dispose()
        {
            _timer?.Stop();
            Control.Changed -= OnControlChanged;
            Control.Dispose();
        }
    }
}
