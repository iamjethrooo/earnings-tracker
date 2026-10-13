using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace nerve_earnings_tracker.Converters;

public class MinutesToHoursConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal minutes ? (minutes / 60m).ToString("0.##", culture) : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
