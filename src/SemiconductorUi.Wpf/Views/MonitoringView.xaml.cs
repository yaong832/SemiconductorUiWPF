using System.Windows;
using System.Windows.Controls;
using SemiconductorUi.Wpf.Controls;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    public partial class MonitoringView : UserControl
    {
        public MonitoringView()
        {
            InitializeComponent();
        }

        /// <summary>시뮬레이션 모드에서 공정 중인 챔버를 오른쪽 클릭하면 시험 알람 메뉴 (원본 AttachSimulationTestAlarmMenus)</summary>
        private void OnRegionRightClicked(object sender, RegionClickedEventArgs e)
        {
            if (!(DataContext is MonitoringViewModel vm)) return;
            var unitKey = MonitoringViewModel.ToUnitKey(e.Region);
            if (unitKey == null || unitKey.StartsWith("FOUP")) return;

            bool canInject = vm.CanInjectTestAlarm(unitKey);
            var item = new MenuItem
            {
                Header = canInject ? "시험 알람 넣기 (시뮬레이션 전용)" : "시험 알람: 시뮬레이션 공정 중인 챔버에서만",
                IsEnabled = canInject,
            };
            item.Click += (s, args) => vm.InjectTestAlarm(unitKey);
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
            menu.Items.Add(item);
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void OnRegionClicked(object sender, RegionClickedEventArgs e)
        {
            (DataContext as MonitoringViewModel)?.OpenRegionDetail(e.Region);
        }
    }
}
