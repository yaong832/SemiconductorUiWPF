using System.Windows;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>설정 창 (원본 Forms.ConfigForm, 모달)</summary>
    public partial class ConfigWindow : Window
    {
        public ConfigWindow(ConfigViewModel model)
        {
            InitializeComponent();
            DataContext = model;
            model.CloseRequested += (s, saved) =>
            {
                DialogResult = saved;
            };
        }
    }
}
