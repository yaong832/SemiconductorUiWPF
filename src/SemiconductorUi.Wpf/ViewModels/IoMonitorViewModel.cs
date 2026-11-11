using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using SemiconductorUi.Hardware;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    public enum IoMonitorTone { Off, Ok, Danger }

    /// <summary>디지털 입력/출력 한 점</summary>
    public sealed class IoPointRow : ObservableObject
    {
        /// <summary>값이 바뀐 점을 이 시간 동안 강조한다 (WPF 추가)</summary>
        public static readonly TimeSpan ChangeHighlight = TimeSpan.FromSeconds(1.5);

        private bool? _value;
        private DateTime _changedAt = DateTime.MinValue;
        private bool _isRecentlyChanged;

        public IoPointRow(int index, string name)
        {
            Index = index;
            IndexText = index.ToString("00");
            Name = name;
            IsUnused = name == IoMonitorSnapshot.UnusedName;
        }

        public int Index { get; }
        public string IndexText { get; }
        public string Name { get; }
        public bool IsUnused { get; }

        /// <summary>null = 읽지 못함</summary>
        public bool? Value => _value;
        public bool IsOn => _value == true;
        public string StateText => _value == null ? "-" : _value.Value ? "ON" : "OFF";

        public bool IsRecentlyChanged
        {
            get => _isRecentlyChanged;
            private set => SetProperty(ref _isRecentlyChanged, value);
        }

        internal void Apply(bool? value, DateTime now)
        {
            if (_value != value)
            {
                // 처음 값을 읽었을 때(null → 값)는 바뀐 것으로 보지 않는다
                if (_value.HasValue && value.HasValue) _changedAt = now;
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(IsOn));
                OnPropertyChanged(nameof(StateText));
            }
            IsRecentlyChanged = value.HasValue && now - _changedAt < ChangeHighlight;
        }
    }

    /// <summary>서보 상태워드의 주요 비트 하나</summary>
    public sealed class ServoBitItem
    {
        public ServoBitItem(string name, bool isOn, IoMonitorTone tone, bool isWarning)
        {
            Name = name;
            IsOn = isOn;
            Tone = tone;
            IsWarning = isWarning;
        }

        public string Name { get; }
        public bool IsOn { get; }
        public IoMonitorTone Tone { get; }
        /// <summary>경고 비트(7)는 노란색</summary>
        public bool IsWarning { get; }
    }

    /// <summary>서보 축 한 줄</summary>
    public sealed class ServoAxisRow : ObservableObject
    {
        private string _statusWordText = "-";
        private string _stateName = "-";
        private bool _isFault;
        private string _position = "-";
        private IReadOnlyList<ServoBitItem> _bits = Array.Empty<ServoBitItem>();

        public ServoAxisRow(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string StatusWordText { get => _statusWordText; private set => SetProperty(ref _statusWordText, value); }
        public string StateName { get => _stateName; private set => SetProperty(ref _stateName, value); }
        public bool IsFault { get => _isFault; private set => SetProperty(ref _isFault, value); }
        public string Position { get => _position; private set => SetProperty(ref _position, value); }
        public IReadOnlyList<ServoBitItem> Bits { get => _bits; private set => SetProperty(ref _bits, value); }

        internal void Apply(ServoAxisSnapshot axis)
        {
            if (axis == null)
            {
                StatusWordText = "-";
                StateName = "-";
                IsFault = false;
                Position = "-";
                Bits = Array.Empty<ServoBitItem>();
                return;
            }

            string word = $"0x{axis.StatusWord:X4}";
            if (word != StatusWordText)
            {
                Bits = ServoAxisSnapshot.KeyBits
                    .Select(b =>
                    {
                        bool on = axis.Bit(b.Bit);
                        var tone = !on ? IoMonitorTone.Off : b.Bit == 3 ? IoMonitorTone.Danger : IoMonitorTone.Ok;
                        return new ServoBitItem(b.Name, on, tone, on && b.Bit == 7);
                    })
                    .ToList();
            }
            StatusWordText = word;
            StateName = axis.StateName;
            IsFault = axis.IsFault;
            Position = string.IsNullOrWhiteSpace(axis.Position) ? "-" : axis.Position;
        }
    }

    /// <summary>
    /// I/O 모니터 (원본 Forms.IoMonitorForm). 디지털 입력·출력 16점씩과 두 서보 축의 상태.
    /// 읽기 전용이며 장비에 명령을 보내지 않는다. 보이는 곳(유지보수 화면, 별도 창)이 있을 때만 0.3초마다 갱신한다.
    /// WPF 추가: 값이 바뀐 점을 잠깐 강조, 미사용 입력 숨기기, 유지보수 화면 안에서 바로 보기.
    /// </summary>
    public sealed class IoMonitorViewModel : ObservableObject
    {
        public const int RefreshIntervalMs = 300;

        private readonly Func<IEthercatDevice> _deviceProvider;
        private readonly Func<bool> _connectedProvider;
        private readonly Func<DateTime> _clock;
        private DispatcherTimer _timer;
        private int _viewers;
        private string _statusText = "EtherCAT 미연결 · 값을 읽을 수 없습니다";
        private IoMonitorTone _statusTone = IoMonitorTone.Off;
        private bool _hideUnused;

        public IoMonitorViewModel(Func<IEthercatDevice> deviceProvider, Func<bool> connectedProvider, Func<DateTime> clock = null)
        {
            _deviceProvider = deviceProvider ?? throw new ArgumentNullException(nameof(deviceProvider));
            _connectedProvider = connectedProvider ?? throw new ArgumentNullException(nameof(connectedProvider));
            _clock = clock ?? (() => DateTime.Now);

            for (int i = 0; i < IoMonitorSnapshot.PointCount; i++)
            {
                AllInputs.Add(new IoPointRow(i, IoMonitorSnapshot.InputNames[i]));
                Outputs.Add(new IoPointRow(i, IoMonitorSnapshot.OutputNames[i]));
            }
            foreach (var row in AllInputs) Inputs.Add(row);
            Axes.Add(new ServoAxisRow("Axis1 (상하)"));
            Axes.Add(new ServoAxisRow("Axis2 (좌우)"));
        }

        private List<IoPointRow> AllInputs { get; } = new List<IoPointRow>();

        public ObservableCollection<IoPointRow> Inputs { get; } = new ObservableCollection<IoPointRow>();
        public ObservableCollection<IoPointRow> Outputs { get; } = new ObservableCollection<IoPointRow>();
        public ObservableCollection<ServoAxisRow> Axes { get; } = new ObservableCollection<ServoAxisRow>();

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public IoMonitorTone StatusTone { get => _statusTone; private set => SetProperty(ref _statusTone, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        /// <summary>쓰지 않는 입력 번호를 감춘다</summary>
        public bool HideUnused
        {
            get => _hideUnused;
            set
            {
                if (!SetProperty(ref _hideUnused, value)) return;
                Inputs.Clear();
                foreach (var row in AllInputs.Where(r => !value || !r.IsUnused)) Inputs.Add(row);
            }
        }

        public string Footnote => "보기 전용 · 0.3초마다 갱신 · 출력 상태는 실제 단자가 아니라 프로그램이 마지막으로 보낸 값입니다.";

        /// <summary>보는 곳이 하나 생겼다 (화면 표시, 창 열기). 처음이면 갱신을 시작한다.</summary>
        public void Attach()
        {
            _viewers++;
            if (_viewers == 1)
            {
                Refresh();
                if (_timer == null)
                {
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
                    _timer.Tick += (s, e) => Refresh();
                }
                _timer.Start();
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        /// <summary>보는 곳이 하나 사라졌다. 남은 곳이 없으면 갱신을 멈춘다.</summary>
        public void Detach()
        {
            if (_viewers == 0) return;
            _viewers--;
            if (_viewers == 0)
            {
                _timer?.Stop();
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        /// <summary>값을 한 번 읽어 화면에 반영한다</summary>
        public void Refresh()
        {
            var now = _clock();
            var snapshot = IoMonitorSnapshot.Read(_deviceProvider(), _connectedProvider(), now);
            Apply(snapshot, now);
        }

        internal void Apply(IoMonitorSnapshot snapshot, DateTime now)
        {
            if (!snapshot.Connected)
            {
                StatusText = "EtherCAT 미연결 · 값을 읽을 수 없습니다";
                StatusTone = IoMonitorTone.Off;
                ApplyPoints(null, null, now);
                Axes[0].Apply(null);
                Axes[1].Apply(null);
                return;
            }
            if (snapshot.Error != null)
            {
                // 원본처럼 마지막 값은 그대로 두고 오류만 알린다
                StatusText = $"읽기 오류: {snapshot.Error}";
                StatusTone = IoMonitorTone.Danger;
                return;
            }

            StatusText = $"EtherCAT 연결됨{(snapshot.EmergencyStopped ? " · 비상정지 중" : "")} · {now:HH:mm:ss.f} 갱신";
            StatusTone = snapshot.EmergencyStopped ? IoMonitorTone.Danger : IoMonitorTone.Ok;
            ApplyPoints(snapshot.Inputs, snapshot.Outputs, now);
            Axes[0].Apply(snapshot.Axis1);
            Axes[1].Apply(snapshot.Axis2);
        }

        private void ApplyPoints(bool[] inputs, bool[] outputs, DateTime now)
        {
            for (int i = 0; i < IoMonitorSnapshot.PointCount; i++)
            {
                AllInputs[i].Apply(inputs == null ? (bool?)null : inputs[i], now);
                Outputs[i].Apply(outputs == null ? (bool?)null : outputs[i], now);
            }
        }
    }
}
