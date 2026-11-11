using System;
using System.Windows.Input;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 유지보수 화면 (원본 Forms.MaintenanceForm). 원본은 모달 창이고 I/O 모니터 버튼을 누르면 창을 닫은 뒤
    /// 별도 창으로 I/O 모니터를 열었다. WPF는 셸 안의 화면이라 I/O 모니터를 바로 보여 주고, 별도 창으로도 열 수 있다.
    /// 원본의 Leak Test/Pump Purge/Sensor Calib/Door Cycle 버튼은 상태 글자만 바꾸고 아무 동작도 하지 않아 옮기지 않았다.
    /// </summary>
    public sealed class MaintenanceViewModel : ObservableObject
    {
        private bool _isShown;

        public MaintenanceViewModel(IoMonitorViewModel io, Action openIoWindow)
        {
            Io = io ?? throw new ArgumentNullException(nameof(io));
            if (openIoWindow == null) throw new ArgumentNullException(nameof(openIoWindow));
            OpenIoWindowCommand = new RelayCommand(openIoWindow);
        }

        public IoMonitorViewModel Io { get; }

        /// <summary>I/O 모니터를 별도 창으로 연다 (원본 ShowIoMonitorWindow)</summary>
        public ICommand OpenIoWindowCommand { get; }

        /// <summary>메뉴에서 이 화면으로 들어왔다. 화면 안의 I/O 모니터를 갱신하기 시작한다.</summary>
        public void OnShown()
        {
            if (_isShown) return;
            _isShown = true;
            Io.Attach();
        }

        /// <summary>다른 화면으로 나갔다. 별도 창이 없으면 갱신을 멈춘다.</summary>
        public void OnHidden()
        {
            if (!_isShown) return;
            _isShown = false;
            Io.Detach();
        }
    }
}
