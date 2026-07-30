using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it2 리뷰 사이클1 회귀: XAML 리소스 해석 실패(Color.Bg 미해결 등)를 headless로 잡는다.
/// 테마 병합 딕셔너리를 로드하고 View들이 참조하는 핵심 키가 예외 없이 해석되는지 검증.
/// ⚠️ 창을 표시하지 않는다(Show 호출 없음) — UI 노출 없이 XamlParseException만 검출.
/// build·일반 단위 테스트가 못 잡던 StaticResource 런타임 해석 실패를 이 테스트가 잡는다.
/// </summary>
public class XamlResourceTests
{
    /// <summary>STA 스레드에서 액션을 실행하고 예외를 전파(WPF 리소스 로드는 STA 필요).</summary>
    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (captured is not null)
            throw new Exception("STA 실행 중 예외", captured);
    }

    private static ResourceDictionary LoadTheme()
    {
        // pack:// 스킴과 리소스 어셈블리 컨텍스트는 Application 인스턴스가 등록한다.
        // ⚠️ new Application()은 창을 띄우지 않는다(Run/Show 호출 없음) — headless 유지.
        EnsureApplication();
        return new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MCPhoto;component/Themes/Theme.xaml", UriKind.Absolute)
        };
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            // 창을 만들지 않는 순수 리소스 컨텍스트용 Application. ShutdownMode로 자동 종료 방지.
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }

    /// <summary>테마 딕셔너리가 예외 없이 로드되고 핵심 브러시/토큰 키가 전부 해석된다.</summary>
    [Fact]
    public void Theme_Loads_And_Core_Keys_Resolve()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();

            // Color.Bg 교차 참조 실패 회귀를 직접 겨냥: Brush.Bg 조회 시 내부 Color.Bg가 해석돼야 함.
            var required = new[]
            {
                "Brush.Bg", "Brush.Surface", "Brush.Surface.Alt", "Brush.Border",
                "Brush.Text.Primary", "Brush.Text.Secondary", "Brush.Text.Tertiary", "Brush.Text.Muted",
                "Brush.Accent", "Brush.Accent.Hover", "Brush.Accent.Press", "Brush.Accent.Text", "Brush.Accent.Soft",
                "Brush.OnAccent", "Brush.Accent2", "Brush.Success", "Brush.Danger", "Brush.Danger.Surface",
                // it10 S2-1: 로그인 오프라인 배너가 사용하는 경고 톤 리소스 회귀(미해결 시 배너 XamlParseException).
                "Brush.Warning", "Brush.Warning.Surface",
                "Brush.Scrim", "Brush.CaptureBg", "Brush.Disabled.Bg", "Brush.Disabled.Fg",
                "Shadow.Sm", "Shadow.Card", "Shadow.Pop",
                "Radius.S", "Radius.M", "Radius.Pill", "Touch.Min", "Touch.CTA", "Touch.IconBtn",
                "Font.Primary", "Text.Display", "Text.H1", "Text.H2", "Text.Body", "Text.Label", "Text.Caption",
                "Button.Primary", "Button.Secondary", "Button.Ghost", "Button.Danger",
                "Button.Icon", "Button.Icon.Pill", "Button.Filter", "Button.FrameCard", "Button.Shutter",
                "Card", "ScreenTitle", "Toggle", "Segment",
            };

            var missing = new List<string>();
            foreach (var key in required)
            {
                var val = theme[key]; // 미해결/미정의면 여기서 예외 또는 null
                if (val is null) missing.Add(key);
            }
            Assert.True(missing.Count == 0, "테마에서 해석 안 된 키: " + string.Join(", ", missing));
        });
    }

    /// <summary>브러시 토큰이 실제 SolidColorBrush로 해석된다(Color 교차 참조가 색으로 완성됨).</summary>
    [Fact]
    public void Brush_Tokens_Are_Resolved_Brushes()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            // Brush.Bg는 흰색이어야(Color.Bg=#FFFFFF 해석 성공 증명)
            var bg = theme["Brush.Bg"] as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(bg);
            Assert.Equal(System.Windows.Media.Colors.White, bg!.Color);

            var accent = theme["Brush.Accent"] as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(accent);
            Assert.Equal((byte)0xFF, accent!.Color.R); // 로즈 #FF4D79
        });
    }

    // ── it4: sibling merged dictionary 교차 참조 정적 안전망 ──
    // it2 버그: Brushes.xaml이 {StaticResource Color.Bg}를 형제 딕셔너리에서 참조 → 런타임 XamlParseException.
    // 각 Themes 파일이 자기 안에서(자체 MergedDictionaries 포함) 참조 키를 모두 해석할 수 있어야 한다.
    // 창을 띄우지 않고(pack URI 로드만) StaticResource 미해결을 잡는다.

    // 소스 트리에서 Themes 원본 XAML을 찾는다(테스트 실행 디렉터리 기준 상위 탐색).
    private static string FindThemesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MCPhoto.App", "Themes");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/MCPhoto.App/Themes 를 찾지 못함");
    }

    [Theory]
    [InlineData("Colors.xaml")]
    [InlineData("Brushes.xaml")]
    [InlineData("Typography.xaml")]
    [InlineData("Metrics.xaml")]
    [InlineData("Controls.xaml")]
    public void Each_Theme_File_Resolves_Its_Own_StaticResource_References(string file)
    {
        var themesDir = FindThemesDir();
        var text = File.ReadAllText(Path.Combine(themesDir, file));

        // 이 파일이 참조하는 모든 StaticResource 키 추출.
        var referenced = Regex.Matches(text, @"\{StaticResource\s+([^\}]+?)\s*\}")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(k => k.Length > 0)
            .Distinct()
            .ToArray();
        if (referenced.Length == 0) return; // 참조 없으면 통과(Colors 등)

        RunSta(() =>
        {
            EnsureApplication();
            // 개별 파일을 로드(자체 MergedDictionaries 포함) — 형제 교차 참조면 여기서/조회에서 실패.
            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/MCPhoto;component/Themes/{file}", UriKind.Absolute)
            };

            var unresolved = new List<string>();
            foreach (var key in referenced)
            {
                try
                {
                    if (!dict.Contains(key)) unresolved.Add(key);
                }
                catch (Exception ex)
                {
                    unresolved.Add($"{key} ({ex.GetType().Name})");
                }
            }
            Assert.True(unresolved.Count == 0,
                $"{file} 이 자체적으로 해석 못 하는 StaticResource: {string.Join(", ", unresolved)}");
        });
    }

    // ── it11 #14: 진단 모달 XAML의 StaticResource 키가 테마에서 전부 해석되는지 정적 검증 ──
    // Window 인스턴스화(Application/스레드 친화 제약)를 피하고, 소스에서 참조 키를 추출해 테마 조회로만 검증.

    private static string FindAppViewsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MCPhoto.App", "Views");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/MCPhoto.App/Views 를 찾지 못함");
    }

    /// <summary>
    /// App.xaml에 정의된 리소스 키(공용 컨버터·브랜딩 문자열). 테마 딕셔너리 **밖**이라 이 검증 대상이 아니다.
    /// 하드코딩 목록이 아니라 App.xaml에서 직접 읽는다 — 컨버터가 추가될 때 화이트리스트를 잊어 테스트가
    /// 엉뚱하게 깨지던 문제를 없앤다.
    /// </summary>
    private static HashSet<string> LoadAppResourceKeys()
    {
        var appDir = Directory.GetParent(FindAppViewsDir())!.FullName;   // …/src/MCPhoto.App
        var text = File.ReadAllText(Path.Combine(appDir, "App.xaml"));
        return Regex.Matches(text, @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    /// <summary>
    /// XAML 텍스트에서 "테마에 있어야 하는" StaticResource 키만 추출.
    /// 제외: 파일이 자체 정의한 키(x:Key), App.xaml 키, 그리고 `{StaticResource {x:Type Foo}}`처럼
    /// 중첩 마크업 확장으로 지정한 암묵 스타일 키(정규식이 `{x:Type Foo`로 캡처하는 형태 — 이름 키가 아니다).
    /// </summary>
    private static string[] ThemeKeysReferencedBy(string text)
    {
        var localKeys = Regex.Matches(text, @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToHashSet();
        var appKeys = LoadAppResourceKeys();

        return Regex.Matches(text, @"\{StaticResource\s+([^\}]+?)\s*\}")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(k => k.Length > 0
                        && !k.StartsWith("{x:Type", StringComparison.Ordinal)
                        && !localKeys.Contains(k)
                        && !appKeys.Contains(k))
            .Distinct()
            .ToArray();
    }

    [Fact]
    public void DiagnosticsWindow_StaticResource_Keys_Resolve_In_Theme()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "DiagnosticsWindow.xaml"));
        var referenced = ThemeKeysReferencedBy(text);

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                "DiagnosticsWindow.xaml 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });
    }

    // ── it12 R2/R3: SettingsView 레이아웃 재배치 + 게스트 게이트 노티(GuestGateNote) 정적 안전망 ──
    // SettingsView가 참조하는 모든 테마 StaticResource가
    // 해석되는지 headless로 검증(창 미표시). 로컬 키·App 컨버터 키는 제외.

    // ── 계정·로그인·사용자 관리 화면 StaticResource 정적 안전망 ──
    // 신규/수정 View가 참조하는 모든 테마 StaticResource가 해석되는지 headless로 검증(창 미표시).
    // it15: PasswordResetView 폐지로 엔트리 삭제(§3.1).

    [Theory]
    [InlineData("AccountView.xaml")]
    [InlineData("LoginGuestView.xaml")]
    [InlineData("UserMgmtView.xaml")]      // it13 §9.5: 역할 변경 콤보+Apply 재작업 StaticResource 회귀 안전망
    [InlineData("FrameEditorView.xaml")]   // it15 F1/F2: 안내 배너 + 저장 캡션 + 피커 오버레이(공유 카드 리소스)
    [InlineData("FrameSelectView.xaml")]   // it15 F2-D3: 카드 시각을 공유 리소스로 교체
    public void Item1a_View_StaticResource_Keys_Resolve_In_Theme(string file)
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), file));
        var referenced = ThemeKeysReferencedBy(text);

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                $"{file} 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });
    }

    /// <summary>
    /// it15 F1-D1(정정): "해당 PC에서만 적용됩니다" 배너는 **기존 프레임 수정 시에만** 노출해야 한다.
    /// 신규 생성(특히 power=서버 등록)에서 배너가 보이면 문구가 거짓이 되고 같은 화면의
    /// SaveScopeNotice("서버에 등록됩니다")와 모순되므로, Visibility 게이트가 사라지는 회귀를 정적으로 막는다.
    /// (VM 단위 테스트로는 XAML 바인딩 소실을 잡을 수 없어 소스 텍스트를 직접 검사한다.)
    /// </summary>
    [Fact]
    public void FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "FrameEditorView.xaml"));

        // 배너 Border = Brush.Warning.Surface 배경 엘리먼트. 그 엘리먼트 안에 Visibility 게이트가 있어야 한다.
        var banner = Regex.Match(text, @"<Border\b[^>]*Brush\.Warning\.Surface[^>]*>", RegexOptions.Singleline);
        Assert.True(banner.Success, "FrameEditorView.xaml 에서 정책 배너(Brush.Warning.Surface Border)를 찾지 못함");

        Assert.Contains("IsCreateMode", banner.Value);
        Assert.Contains("InverseBoolToVis", banner.Value);  // IsCreateMode=true(신규) → Collapsed

        // 배너가 숨어도 콘텐츠가 상단 바(오프셋 88)에 파고들지 않도록 행 MinHeight가 남아 있어야 한다.
        Assert.Matches(@"<RowDefinition\s+Height=""Auto""\s+MinHeight=""88""\s*/>", text);
    }

    /// <summary>
    /// it15 F2-D3: 프레임 카드 공유 리소스가 테마(Controls.xaml)에 있고 기대 타입으로 해석된다.
    /// FrameSelectView와 편집기 피커가 같은 시각을 쓰기 위한 전제 — 키가 사라지면 두 화면이 함께 깨진다.
    /// </summary>
    [Fact]
    public void FrameCard_Shared_Resources_Exist_In_Theme()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            Assert.IsType<System.Windows.Style>(theme["FrameCard.ItemContainer"]);
            Assert.IsType<System.Windows.DataTemplate>(theme["FrameCard.Content"]);
            // 카드 본체가 쓰는 컨버터는 Controls.xaml 자체 정의(형제 딕셔너리 교차 참조 회피).
            Assert.NotNull(theme["FrameCard.FilePathToImage"]);
        });
    }

    // ── it14: PinPromptWindow(설정 진입 PIN 게이트 모달) StaticResource 정적 안전망 ──
    // 창 인스턴스화를 피하고 소스에서 참조 키를 추출해 테마 조회로만 검증(Application 싱글턴 충돌 회피).

    [Fact]
    public void PinPromptWindow_StaticResource_Keys_Resolve_In_Theme()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "PinPromptWindow.xaml"));

        // 자체 정의 리소스(Window.Resources)는 제외(현재 없음).
        var localKeys = Regex.Matches(text, @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToHashSet();

        var appKeys = new HashSet<string>
        {
            "BoolToVis", "InverseBoolToVis", "InverseBool", "BoolToBrush", "NullToVis",
            "BoolToNoticeBrush", "CameraStateToVis", "SlotAspectLabel", "AspectRatioToHeight",
            "StartsWithToVis", "AllTrueToVis", "FrameDeleteVis", "RoleActionVis", "RoleLabel", "FilePathToImage",
        };

        var referenced = Regex.Matches(text, @"\{StaticResource\s+([^\}]+?)\s*\}")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(k => k.Length > 0 && !localKeys.Contains(k) && !appKeys.Contains(k))
            .Distinct()
            .ToArray();

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                "PinPromptWindow.xaml 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });
    }

    [Fact]
    public void SettingsView_StaticResource_Keys_Resolve_In_Theme()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));

        // 자체 정의 리소스(RowLabel/SettingRow/FullRow/GroupTitle/GroupDivider/GuestGateNote 등 UserControl.Resources)는 제외.
        var localKeys = Regex.Matches(text, @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToHashSet();

        // App.xaml에 정의된 공용 컨버터 키(테마 딕셔너리 밖)는 이 검증 대상이 아님.
        var appKeys = new HashSet<string>
        {
            "BoolToVis", "InverseBoolToVis", "InverseBool", "BoolToBrush", "NullToVis",
            "BoolToNoticeBrush", "CameraStateToVis", "SlotAspectLabel", "AspectRatioToHeight",
            "StartsWithToVis", "AllTrueToVis", "FrameDeleteVis", "FilePathToImage",
        };

        var referenced = Regex.Matches(text, @"\{StaticResource\s+([^\}]+?)\s*\}")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(k => k.Length > 0 && !localKeys.Contains(k) && !appKeys.Contains(k))
            .Distinct()
            .ToArray();

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                "SettingsView.xaml 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });
    }
}
