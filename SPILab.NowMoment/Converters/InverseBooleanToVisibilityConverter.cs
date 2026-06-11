// ════════════════════════════════════════════════════════════
// InverseBooleanToVisibilityConverter — v3.0 F-001 Step 1.5
//
// EditDialog의 KG 패널에서 "자산이 미저장 상태일 때만 안내 문구 표시"
// 같은 역방향 가시성 바인딩에 사용한다.
// ════════════════════════════════════════════════════════════
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SPILab.NowMoment.Converters
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility vis && vis == Visibility.Visible ? false : true;
        }
    }
}
