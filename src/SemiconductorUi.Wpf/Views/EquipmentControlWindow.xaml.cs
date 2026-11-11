using System;
using System.ComponentModel;
using System.Windows;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>장비 직접 제어 창 (원본 EquipmentControlForm, 모달)</summary>
    public partial class EquipmentControlWindow : Window
    {
        private readonly EquipmentControlViewModel _model;

        public EquipmentControlWindow(EquipmentControlViewModel model)
        {
            InitializeComponent();
            _model = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
            Loaded += (s, e) => _model.Start();
            Closing += OnClosing;
            Closed += (s, e) => _model.Dispose();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// 돌고 있는 타이머가 있으면 묻는다 (WPF 추가). 원본은 닫으면 타이머만 취소되어 ON(열림)이 그대로 남았다.
        /// 예 = 지금 OFF(닫기) 후 닫기, 아니요 = 그대로 두고 닫기(원본), 취소 = 돌아가기
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            int count = _model.Control.ActiveTimers.Count;
            if (count == 0 || !IsLoaded) return; // 띄운 적 없는 창(스냅샷)은 묻지 않는다
            var answer = MessageBox.Show(this,
                $"자동 OFF 타이머 {count}개가 돌고 있습니다.\n\n" +
                "[예] 지금 OFF(닫기)하고 창을 닫습니다.\n" +
                "[아니요] 켜진(열린) 채로 두고 창을 닫습니다. 자동 OFF는 취소됩니다.\n" +
                "[취소] 창으로 돌아갑니다.",
                "타이머 실행 중", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == MessageBoxResult.Yes)
            {
                _model.Control.FinishTimedActionsNow();
            }
        }
    }
}
