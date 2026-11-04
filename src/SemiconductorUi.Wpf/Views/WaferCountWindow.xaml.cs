using System;
using System.Windows;

namespace SemiconductorUi.Wpf.Views
{
    public partial class WaferCountWindow : Window
    {
        private readonly int _min;
        private readonly int _max;

        public WaferCountWindow(int current, int min, int max)
        {
            InitializeComponent();
            _min = min;
            _max = max;
            RangeText.Text = $"로딩할 웨이퍼 수 (최소 {min}, 최대 {max})";
            CountBox.Text = Math.Max(min, Math.Min(max, current)).ToString();
            CountBox.SelectAll();
        }

        public int Value { get; private set; }

        private bool TryRead(out int value)
        {
            return int.TryParse(CountBox.Text.Trim(), out value) && value >= _min && value <= _max;
        }

        private void Step(int delta)
        {
            int value = int.TryParse(CountBox.Text.Trim(), out var v) ? v : _min;
            CountBox.Text = Math.Max(_min, Math.Min(_max, value + delta)).ToString();
        }

        private void OnMinus(object sender, RoutedEventArgs e) => Step(-1);

        private void OnPlus(object sender, RoutedEventArgs e) => Step(1);

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!TryRead(out var value))
            {
                ErrorText.Text = $"{_min}부터 {_max}까지의 숫자를 입력하세요.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            Value = value;
            DialogResult = true;
        }
    }
}
