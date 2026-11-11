using System.Windows;
using System.Windows.Controls;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>웨이퍼 이력 (보고서 탭과 별도 창이 같이 쓴다)</summary>
    public partial class WaferHistoryView : UserControl
    {
        public static readonly DependencyProperty ShowOpenWindowButtonProperty = DependencyProperty.Register(
            nameof(ShowOpenWindowButton), typeof(bool), typeof(WaferHistoryView), new PropertyMetadata(true));

        public WaferHistoryView()
        {
            InitializeComponent();
        }

        /// <summary>[별도 창으로 열기] 버튼을 보일지 (별도 창 안에서는 숨긴다)</summary>
        public bool ShowOpenWindowButton
        {
            get => (bool)GetValue(ShowOpenWindowButtonProperty);
            set => SetValue(ShowOpenWindowButtonProperty, value);
        }
    }
}
