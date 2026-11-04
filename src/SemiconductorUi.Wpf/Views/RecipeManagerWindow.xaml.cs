using System.Windows;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>레시피 관리 창 (원본 Forms.RecipeManagerForm, 모달)</summary>
    public partial class RecipeManagerWindow : Window
    {
        public RecipeManagerWindow(RecipeManagerViewModel model)
        {
            InitializeComponent();
            DataContext = model;
            model.CloseRequested += (s, e) => Close();
        }
    }
}
