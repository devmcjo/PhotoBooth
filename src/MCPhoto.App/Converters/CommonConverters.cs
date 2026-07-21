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

/// <summary>선택 상태 → 테두리 색(true=강조 로즈, 테마 토큰). (it3: 하드코딩 제거)</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true
            ? (Application.Current?.TryFindResource("Brush.Accent") as Brush ?? Brushes.DeepPink)
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 저장 안내 색: true(오류)=Brush.Danger, false(성공)=Brush.Success. 테마 토큰 참조. (it3 §3)
/// </summary>
public sealed class BoolToNoticeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "Brush.Danger" : "Brush.Success";
        return Application.Current?.TryFindResource(key) as Brush
               ?? (value is true ? Brushes.Red : Brushes.Green);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// CameraLoadState → Visibility. ConverterParameter로 지정한 상태명과 일치하면 Visible. (it3 §7 U4)
/// 예: ConverterParameter=Initializing → 로딩 오버레이, =Failed → 오류 메시지.
/// </summary>
public sealed class CameraStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var target = parameter?.ToString();
        return value?.ToString() == target ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
