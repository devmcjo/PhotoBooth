using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.App.Converters;

/// <summary>
/// 파일 경로 → BitmapImage. OnLoad + IgnoreImageCache 로 로드해 **파일을 잠그지 않는다**
/// (기본 바인딩은 파일 핸들을 유지해 삭제 실패 유발). 경로 없음/부재 시 null(placeholder). (it9 후속 — 프레임 삭제 수정)
/// </summary>
public sealed class FilePathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var isHttp = path.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;          // 즉시 메모리로 로드 → 파일 핸들 해제
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; } // 로드 실패 시 placeholder
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

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
/// 프레임 삭제 ✕ 노출 판정. values=[CanDeleteFrames(bool), IsPower(bool), Id(string)]. (it9 후속 — A3 정정)
/// 규칙: 비로그인/게스트=미노출, 번들·fallback·빈 Id=삭제 불가.
/// user 로컬(local: 접두)=본인 것이라 로그인 사용자면 노출, 공용/DB 프레임(접두 없음)=파워만 노출.
/// </summary>
public sealed class FrameDeleteVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool canDelete = values.Length > 0 && values[0] is true;
        bool isPower = values.Length > 1 && values[1] is true;
        var id = values.Length > 2 ? values[2] as string : null;

        if (!canDelete || string.IsNullOrEmpty(id)) return Visibility.Collapsed;
        if (id.StartsWith("bundle:", StringComparison.Ordinal)
            || id.StartsWith("fallback", StringComparison.Ordinal)) return Visibility.Collapsed;

        if (id.StartsWith("local:", StringComparison.Ordinal)) return Visibility.Visible; // 본인 로컬
        return isPower ? Visibility.Visible : Visibility.Collapsed;                        // 공용/DB=파워만
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 사용자 관리 액션 노출 판정. values=[actorRole(UserRole), targetRole(UserRole)], parameter="Manage"|"Promote".
/// - Manage(삭제·pw 초기화): 대상이 행위자와 **같거나 낮은 역할**일 때만 노출(manager는 admin 관리 불가).
/// - Promote(manager 지정): admin이 **user 대상**일 때만 노출(승격 대상은 user).
/// 값이 비었거나 형식이 다르면 안전하게 Collapsed. (권한 게이트 — UI 노출; 명령에도 동일 가드 존재)
/// </summary>
public sealed class RoleActionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not UserRole actor || values[1] is not UserRole target)
            return Visibility.Collapsed;

        bool ok = (parameter as string) == "Promote"
            ? actor == UserRole.Admin && target == UserRole.User
            : actor.CanManage(target);
        return ok ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
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
