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

        private void OnRegionClicked(object sender, RegionClickedEventArgs e)
        {
            (DataContext as MonitoringViewModel)?.OpenRegionDetail(e.Region);
        }
    }
}
