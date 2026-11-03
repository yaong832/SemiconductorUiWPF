using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SemiconductorUi.Wpf.Infrastructure
{
    /// <summary>
    /// 값이 ConverterParameter와 같은지 비교한다 (열거형/bool 모두 문자열로 비교).
    /// ToVisibility=true면 Visible/Collapsed를, 아니면 bool을 돌려준다.
    /// RadioButton.IsChecked 양방향 바인딩에서는 체크될 때 파라미터 값을 되돌려 준다.
    /// </summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public bool ToVisibility { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool equal = value != null && parameter != null &&
                         string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            if (ToVisibility) return equal ? Visibility.Visible : Visibility.Collapsed;
            return equal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                return targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()) : parameter;
            }
            return Binding.DoNothing;
        }
    }
}
