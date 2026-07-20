using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MCPhoto.App.Converters;

/// <summary>bool → Visibility(true=Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>bool → Visibility(true=Collapsed).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>bool 반전.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>null → Visible(placeholder 표시), 값 있음 → Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>SlotCount(1~6) ↔ ComboBox 인덱스(0~5).</summary>
public sealed class SlotCountIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count ? Math.Clamp(count - 1, 0, 5) : 3;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int index ? index + 1 : 4;
}

/// <summary>선택 상태 → 테두리 색(true=강조).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly Brush Selected = new SolidColorBrush(Color.FromRgb(0xC4, 0x4B, 0x9B));
    private static readonly Brush Unselected = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Selected : Unselected;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
