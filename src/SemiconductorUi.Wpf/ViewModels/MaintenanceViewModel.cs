using System;
using System.Windows.Input;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>유지보수 화면 위 작은 탭</summary>
    public enum MaintenanceTab
    {
        IoMonitor,
        Counters
    }

    /// <summary>
    /// 유지보수 화면 (원본 Forms.MaintenanceForm). 원본은 모달 창이고 I/O 모니터·유지보수 카운터 버튼을 누르면 창을 닫은 뒤
    /// 별도 창을 열었다. WPF는 셸 안의 화면이라 두 가지를 탭으로 바로 보여 주고, 각각 별도 창으로도 열 수 있다.
    /// 원본의 Leak Test/Pump Purge/Sensor Calib/Door Cycle 버튼은 상태 글자만 바꾸고 아무 동작도 하지 않아 옮기지 않았다.
    /// </summary>
    public sealed class MaintenanceViewModel : ObservableObject
    {
        private readonly Action _openIoWindow;
        private readonly Action _openCounterWindow;
        private MaintenanceTab _currentTab = MaintenanceTab.IoMonitor;
        private bool _isShown;

        public MaintenanceViewModel(IoMonitorViewModel io, MaintenanceCounterViewModel counters, Action openIoWindow, Action openCounterWindow)
        {
            Io = io ?? throw new ArgumentNullException(nameof(io));
            Counters = counters ?? throw new ArgumentNullException(nameof(counters));
            _openIoWindow = openIoWindow ?? throw new ArgumentNullException(nameof(openIoWindow));
            _openCounterWindow = openCounterWindow ?? throw new ArgumentNullException(nameof(openCounterWindow));
            OpenWindowCommand = new RelayCommand(OpenWindow);
        }

        public IoMonitorViewModel Io { get; }

        public MaintenanceCounterViewModel Counters { get; }

        public MaintenanceTab CurrentTab
        {
            get => _currentTab;
            set
            {
                var previous = _currentTab;
                if (!SetProperty(ref _currentTab, value)) return;
                if (_isShown)
                {
                    Detach(previous);
                    Attach(value);
                }
                OnPropertyChanged(nameof(IsIoTab));
                OnPropertyChanged(nameof(IsCountersTab));
                OnPropertyChanged(nameof(TabDescription));
            }
        }

        public bool IsIoTab => CurrentTab == MaintenanceTab.IoMonitor;

        public bool IsCountersTab => CurrentTab == MaintenanceTab.Counters;

        public string TabDescription => IsIoTab
            ? "디지털 입력·출력과 서보 상태를 읽기만 합니다. 장비에 아무 명령도 보내지 않습니다."
            : "실린더·진공·배기·도어·축의 동작 횟수와 점검 주기. 점검 주기에 도달해도 장비는 멈추지 않습니다.";

        /// <summary>지금 탭을 별도 창으로 연다 (원본 ShowIoMonitorWindow / ShowMaintenanceCounterWindow)</summary>
        public ICommand OpenWindowCommand { get; }

        /// <summary>메뉴에서 이 화면으로 들어왔다. 보이는 탭을 갱신하기 시작한다.</summary>
        public void OnShown()
        {
            if (_isShown) return;
            _isShown = true;
            Attach(_currentTab);
        }

        /// <summary>다른 화면으로 나갔다. 별도 창이 없으면 갱신을 멈춘다.</summary>
        public void OnHidden()
        {
            if (!_isShown) return;
            _isShown = false;
            Detach(_currentTab);
        }

        private void OpenWindow()
        {
            if (IsIoTab) _openIoWindow();
            else _openCounterWindow();
        }

        private void Attach(MaintenanceTab tab)
        {
            if (tab == MaintenanceTab.IoMonitor) Io.Attach();
            else Counters.Attach();
        }

        private void Detach(MaintenanceTab tab)
        {
            if (tab == MaintenanceTab.IoMonitor) Io.Detach();
            else Counters.Detach();
        }
    }
}
