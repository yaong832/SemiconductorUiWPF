using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SemiconductorUi.Wpf.ViewModels;

namespace SemiconductorUi.Wpf.Views
{
    /// <summary>TM 티칭 위치 창 (원본 Forms.TeachingPositionForm, 모달)</summary>
    public partial class TeachingPositionWindow : Window
    {
        /// <summary>현재 서보 위치 갱신 주기 (원본은 '위치 새로고침' 버튼)</summary>
        public const int PositionRefreshMs = 500;

        private readonly TeachingPositionViewModel _model;
        private readonly DispatcherTimer _positionTimer;
        private bool _closingFromModel;

        public TeachingPositionWindow(TeachingPositionViewModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            model.CloseRequested += (s, saved) =>
            {
                _closingFromModel = true;
                DialogResult = saved;
            };

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PositionRefreshMs) };
            _positionTimer.Tick += (s, e) => _model.RefreshPosition();
            Loaded += (s, e) => _positionTimer.Start();
            Closed += (s, e) => _positionTimer.Stop();

            // Enter로 입력 확정 (칸을 벗어나지 않아도 값이 반영되게)
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && e.OriginalSource is TextBox box)
                {
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    box.SelectAll();
                    e.Handled = true;
                }
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 제목 표시줄 X로 닫을 때도 저장 안 한 변경을 묻는다
            if (!_closingFromModel)
            {
                CommitFocusedText();
                if (!_model.ConfirmDiscard()) e.Cancel = true;
            }
            base.OnClosing(e);
        }

        private static void CommitFocusedText()
        {
            if (Keyboard.FocusedElement is TextBox box)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }
    }
}
