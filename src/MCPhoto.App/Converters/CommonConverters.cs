using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MCPhoto.Core.Frames;

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

/// <summary>SlotAspect → 표시 라벨("4:3"/"3:4"/"1:1"). 종횡비 ComboBox 항목 표시. (it4 §3 B4)</summary>
public sealed class SlotAspectLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SlotAspect aspect ? aspect.ToLabel() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 종횡비(가로/세로) → 높이. 기준 폭은 ConverterParameter(기본 200). height = width / aspect.
/// 썸네일 컨테이너를 슬롯 비율로 맞춰 WYSIWYG 표시(it5 §3 B7).
/// </summary>
public sealed class AspectRatioToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double width = 200;
        if (parameter is string p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var w))
            width = w;
        double aspect = value is double a && a > 0 ? a : (3.0 / 4.0);
        return width / aspect;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>문자열이 ConverterParameter로 시작하면 Visible, 아니면 Collapsed(삭제 가능 프레임 X 표시 등, it8 A3).</summary>
public sealed class StartsWithToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        var prefix = parameter as string;
        return s is not null && prefix is not null && s.StartsWith(prefix, StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 여러 입력이 모두 "참"(bool true 또는 Visibility.Visible)일 때만 Visible, 아니면 Collapsed. (it8 A3 카드 X 조건 결합)
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        foreach (var v in values)
        {
            bool ok = v switch
            {
                bool b => b,
                Visibility vis => vis == Visibility.Visible,
                _ => false
            };
            if (!ok) return Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
