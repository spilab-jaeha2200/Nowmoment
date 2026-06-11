// ════════════════════════════════════════════════════════════
// EqualsToVisibilityConverter — v4 Phase 8 (설정창)
//
// 바인딩 값(string)이 ConverterParameter(string)와 일치하면 Visible,
// 아니면 Collapsed.  설정창에서 SelectedCategoryKey 가 "python" 일 때만
// Python 패널을 보이게 하는 등 카테고리 스위칭에 사용한다.
// ════════════════════════════════════════════════════════════
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SPILab.NowMoment.Converters
{
    public class EqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString() ?? "";
            var p = parameter?.ToString() ?? "";
            return string.Equals(s, p, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
