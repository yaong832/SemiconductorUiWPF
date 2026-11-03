using System.Windows;
using System.Windows.Controls;

namespace SemiconductorUi.Wpf.Views
{
    public partial class PlaceholderView : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlaceholderView));

        public PlaceholderView()
        {
            InitializeComponent();
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
    }
}
