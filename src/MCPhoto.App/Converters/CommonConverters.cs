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

/// <summary>
/// UserRole → 한글 표시 라벨("임시 유저"/"사용자"/"고급 유저"/"매니저"/"관리자"). 생성 콤보·사용자 관리 목록.
/// (it13 §9.1, it16 §6.1 — 라벨 추가는 UserRoleExtensions.ToLabel() 1곳으로 전부 커버되므로 이 코드는 불변)
/// </summary>
public sealed class RoleLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is UserRole role ? role.ToLabel() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// UserRole → 배지 색(사용자 관리 목록). ConverterParameter로 어느 색인지 지정:
///   "Bg"=배지 배경, "Fg"=배지 글자, "Strip"=행 좌측 위계 스트립.
/// 색은 **power 계정에만** 쓴다(관리자=로즈, 매니저=민트). 비power(고급 유저·사용자·임시 유저)는 같은
/// 중립 배경에 글자 명도만 달리한다 — 위계는 좌측 스트립 명도로 읽는다.
/// ⚠️ 앰버(Warning)는 이 화면에서 "PIN 미설정" 전용이다. 역할 배지에 쓰면 같은 행에 뜻이 다른 앰버가
///    두 개 생겨 색의 의미가 무너진다(팔레트가 로즈·민트·앰버 3색이라 5역할을 색으로 다 못 가른다).
/// 테마 토큰만 참조 — 팔레트 교체 시 자동 추종(하드코딩 없음).
/// </summary>
public sealed class RoleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value is UserRole r ? r : UserRole.User;
        var slot = parameter?.ToString() ?? "Bg";
        var key = slot switch
        {
            "Strip" => role switch
            {
                UserRole.Admin => "Brush.Accent",
                UserRole.Manager => "Brush.Accent2",
                UserRole.AdvancedUser => "Brush.Text.Tertiary",
                UserRole.User => "Brush.Divider",
                _ => "Brush.Bg.Elevated"          // 임시 유저: 존재감 최소
            },
            "Fg" => role switch
            {
                UserRole.Admin => "Brush.Accent.Text",
                UserRole.Manager => "Brush.Accent2.Text",
                UserRole.AdvancedUser => "Brush.Text.Primary",
                UserRole.User => "Brush.Text.Secondary",
                _ => "Brush.Text.Muted"
            },
            _ => role switch                      // "Bg"
            {
                UserRole.Admin => "Brush.Accent.Soft",
                UserRole.Manager => "Brush.Accent2.Soft",
                _ => "Brush.Surface.Alt"          // 비power 3역할 공통(글자 명도로 구분)
            }
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
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
        var ownerId = values.Length > 3 ? values[3] as string : null;   // FrameTemplate.UserId(=소유자 이메일)

        if (!canDelete || string.IsNullOrEmpty(id)) return Visibility.Collapsed;
        if (id.StartsWith("bundle:", StringComparison.Ordinal)
            || id.StartsWith("fallback", StringComparison.Ordinal)) return Visibility.Collapsed;

        // ⚠️ 소유자 유무가 개인/공용을 가른다(설계 D-2). id 접두만 보면 **서버 정본 전환 후** 개인 프레임이
        //    실 DB id를 갖게 되어 공용으로 오판되고, advanced_user에게 삭제 ✕가 사라진다.
        //    목록에 오르는 개인 프레임은 이미 CanShow가 본인 것만 통과시켰으므로 소유자가 있으면 표시한다.
        //    (같은 이유로 FrameOrigin.Classify도 UserId 우선 판정으로 고쳤다.)
        if (!string.IsNullOrEmpty(ownerId)) return Visibility.Visible;
        if (id.StartsWith("local:", StringComparison.Ordinal)) return Visibility.Visible; // 서버 미동기 로컬 전용

        return isPower ? Visibility.Visible : Visibility.Collapsed;                        // 공용/DB=파워만
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 사용자 관리 액션 노출 판정. values=[actorRole(UserRole), targetRole(UserRole), isSelf(bool, 선택)], parameter="Manage".
/// - Manage(삭제 등 관리 액션): 대상이 행위자와 **같거나 낮은 역할**일 때만 노출(manager는 admin 관리 불가).
/// - isSelf=true(자기 계정 행)면 무조건 미노출 — 자기 계정 삭제는 명령이 어차피 거부하므로 버튼을 보일 이유가 없다.
///   세 번째 값은 선택이며(생략 시 자기 계정 판정 없음) 기존 2값 호출과 호환된다.
/// 값이 비었거나 형식이 다르면 안전하게 Collapsed. (권한 게이트 — UI 노출; 명령에도 동일 가드 존재)
/// </summary>
public sealed class RoleActionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not UserRole actor || values[1] is not UserRole target)
            return Visibility.Collapsed;
        if (values.Length > 2 && values[2] is true) return Visibility.Collapsed;   // 자기 계정 행
        // 삭제·pw초기화(Manage): 대상이 행위자와 같거나 낮은 역할일 때만 노출.
        return actor.CanManage(target) ? Visibility.Visible : Visibility.Collapsed;
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
