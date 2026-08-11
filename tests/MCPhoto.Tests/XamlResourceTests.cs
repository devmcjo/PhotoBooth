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
                // it21: 벡터 아이콘 시스템(유니코드 글리프 폐기)과 상단 바 버튼.
                // 키가 사라지면 셸이 XamlParseException으로 뜨지 않는다.
                "Icon.Gear", "Icon.Account", "Icon.Home", "Icon.Camera",
                "Icon.Glyph", "Button.TopBar", "Button.TopBar.Brand",
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
    [InlineData("Icons.xaml")]      // it21: 참조 0건이어야 한다(스타일이 Geometry를 참조하면 여기서 잡힌다)
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

    /// <summary>
    /// it23 §B5.4·§C8: 진단 모달의 신규 행(설정 파일 경로 · 테스트 모드 상태 · 라이선스 고지 상태) 바인딩이
    /// VM 멤버와 일치하고, <b>폐지된 라이선스 진입 UI가 되살아나지 않았는지</b> 고정한다.
    /// 바인딩 오타·폐지된 경로 참조는 예외 없이 조용히 실패한다(빈 칸).
    /// </summary>
    [Fact]
    public void DiagnosticsWindow_New_Rows_Bind_To_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "DiagnosticsWindow.xaml"));
        var vm = typeof(MCPhoto.App.ViewModels.DiagnosticsViewModel);

        foreach (var member in new[]
                 {
                     "SettingsFilePath", "TestModeState", "IsTestModeOn",
                     "LicenseNoticeState", "HasLicenseNotice",
                 })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"[\s,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        // 요구: 진단에서 고지를 **열지 않는다** — 경로 표시·폴더 열기 버튼이 없어야 한다.
        Assert.DoesNotContain("LicenseFolderPath", text);
        Assert.DoesNotContain("OpenLicenseFolderCommand", text);
        Assert.DoesNotContain("라이선스 폴더 열기", text);
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
    [InlineData("SettingsView.xaml")]      // it17: 컷수 콤보 전환(it19: 자동 규칙 캡션은 제거됨)
    [InlineData("GuideView.xaml")]         // it17: 컷수 옆 "(자동)" 배지
    [InlineData("HomeView.xaml")]          // it21: 4층 구조로 전면 재작성(앱 마크·흐름 안내·게스트 힌트)
    [InlineData("QrPopupView.xaml")]       // it21 §8.4: 좁은 창 대비 스크롤 래핑
    [InlineData("CutSelectView.xaml")]     // 배치 프리뷰(프레임+슬롯 오버레이) 신설
    [InlineData("CaptureView.xaml")]       // it23: 외부 카메라 배지·강등 배너·수신 대기 스피너 추가
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
    // FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode 는 삭제했다 —
    // "이 PC에만 적용" 배너 자체가 제거됐다(설계 D-16 수정 폐지 · D-7 서버 정본).

    /// <summary>
    /// 사용자 관리 표의 "개인 프레임" 열 바인딩을 정적으로 고정한다.
    /// 바인딩 경로 오타는 예외 없이 조용히 실패해(빈 셀) 단위 테스트로는 잡히지 않는다.
    /// 함께 범위 경계도 고정한다 — <b>일일 QR 한도 편집 UI는 이번 범위가 아니다</b>
    /// (강제 로직 없는 편집 UI는 "설정했는데 왜 안 막히지"를 만든다. 과금 도입 시 함께 만든다).
    /// 설계: docs/design/wpf-usermgmt-frame-count-design.md §1·D-1
    /// </summary>
    [Fact]
    public void UserMgmtView_Binds_FrameCountText()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "UserMgmtView.xaml"));

        // ⚠️ 부분 문자열 검사로는 부족하다(주석에만 남은 이름도 통과한다) — 실제 `{Binding …}` 형태를 요구한다.
        Assert.Matches(@"\{Binding\s+FrameCountText\s*[,}]", text);
        Assert.NotNull(typeof(MCPhoto.App.ViewModels.UserRowViewModel).GetProperty("FrameCountText"));

        foreach (var forbidden in new[] { "일일", "한도" })
            Assert.DoesNotContain(forbidden, text);
    }


    /// <summary>
    /// R2/§5.7 함정 회귀 방지: 서버 등록 확인 오버레이의 상태·커맨드는 **편집기 VM**이 갖는다.
    /// 오버레이 어느 요소에든 DataContext를 걸면 그 서브트리의 커맨드 바인딩이 **예외 없이 조용히 실패**하고
    /// (버튼만 비활성) 저장이 영구 대기 상태가 된다 — VM 단위 테스트로는 잡을 수 없어 소스 텍스트를 검사한다.
    /// 허용되는 DataContext는 피커 목록 ListBox의 `{Binding Picker}` 단 하나다.
    /// 함께 6개 바인딩 문자열의 존재를 고정해 XAML 쪽 바인딩 소실도 정적으로 검출한다.
    /// </summary>
    [Fact]
    public void FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "FrameEditorView.xaml"));

        var dataContexts = Regex.Matches(text, @"DataContext\s*=\s*""([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.True(dataContexts.Length == 1,
            "FrameEditorView.xaml 의 DataContext 는 피커 목록 ListBox 1곳뿐이어야 한다. 발견: "
            + $"{dataContexts.Length}개 [{string.Join(" | ", dataContexts)}]. "
            + "서버 등록 확인 오버레이(또는 다른 오버레이)에 DataContext 를 걸면 확인/취소 커맨드와 상태가 "
            + "편집기 VM에 있어 바인딩이 조용히 실패한다(예외 없이 버튼만 비활성).");
        Assert.Equal("{Binding Picker}", dataContexts[0]);

        // 오버레이·캡션이 참조하는 편집기 VM 멤버가 XAML에서 사라지지 않았는지 고정.
        foreach (var member in new[]
                 {
                     "IsServerRegisterConfirmVisible", "IsPersonalScope", "IsPublicScope", "CanConfirmSaveScope",
                     "ConfirmServerRegisterCommand", "CancelServerRegisterCommand",
                     "PickedSourceNotice", "HasPickedSource",
                 })
        {
            Assert.Contains(member, text);
        }
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

    // ── it20 Step 5: 대기 스피너 공유 리소스(Spinner.Ring) ──

    /// <summary>T-39: 스피너 템플릿이 테마에 있고 ControlTemplate으로 해석된다(키가 사라지면 대기 오버레이가 깨진다).</summary>
    [Fact]
    public void Spinner_Ring_Template_Exists_In_Theme()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            Assert.IsType<System.Windows.Controls.ControlTemplate>(theme["Spinner.Ring"]);
        });
    }

    /// <summary>
    /// it20 M2: Spinner.Ring의 RotateTransform이 동결되지 않아 애니메이션 가능한지 headless로 확인한다.
    /// 속성 경로 애니메이션((UIElement.RenderTransform).(RotateTransform.Angle))을 쓰면 템플릿 Seal로 동결된
    /// Freezable에서 "Cannot animate on an immutable object instance"가 던져지고, 그 예외는 런타임에
    /// DispatcherUnhandledException → 홈 복귀로 이어진다. x:Name 등록 방식이 이를 막는지 여기서 고정한다.
    /// </summary>
    [Fact]
    public void Spinner_Ring_Transform_Is_Animatable()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            var template = (System.Windows.Controls.ControlTemplate)theme["Spinner.Ring"];

            var ctl = new System.Windows.Controls.Control { Template = template, Width = 56, Height = 56 };
            var host = new System.Windows.Controls.Border { Child = ctl };
            host.Measure(new System.Windows.Size(100, 100));
            host.Arrange(new System.Windows.Rect(0, 0, 100, 100));
            ctl.ApplyTemplate();

            var ring = template.FindName("SpinnerRing", ctl) as System.Windows.Shapes.Ellipse;
            Assert.NotNull(ring);
            var rot = ring!.RenderTransform as System.Windows.Media.RotateTransform;
            Assert.NotNull(rot);
            Assert.False(rot!.IsFrozen, "RotateTransform이 동결되면 Angle 애니메이션이 런타임 예외가 된다");

            // 실제 애니메이션 시작 — immutable이면 여기서 InvalidOperationException.
            var anim = new System.Windows.Media.Animation.DoubleAnimation(
                0, 360, new System.Windows.Duration(TimeSpan.FromSeconds(1)))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            var sb = new System.Windows.Media.Animation.Storyboard();
            System.Windows.Media.Animation.Storyboard.SetTarget(anim, rot);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(
                anim, new System.Windows.PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));
            sb.Children.Add(anim);
            sb.Begin();      // 예외 없이 통과해야 한다
            sb.Stop();
        });
    }

    /// <summary>
    /// it20 Major 1: 템플릿 **자신의 트리거 체인**을 실제로 발화시킨다.
    /// `Spinner_Ring_Transform_Is_Animatable`은 `Storyboard.SetTarget`으로 트랜스폼을 직접 겨냥하므로
    /// 템플릿의 트리거를 한 줄도 실행하지 않는다. 여기서 확인하는 것은 두 가지 —
    /// ① `EventTrigger(Loaded)` → `BeginStoryboard x:Name="SpinnerSpin"` → `Storyboard.TargetName="SpinnerRotate"`의
    ///    **템플릿 namescope 이름 해석**이 성공하고 `Angle`에 애니메이션 클록이 실제로 붙는지
    /// ② `Trigger(IsVisible=False)`의 `PauseStoryboard`/`ResumeStoryboard`가 **다른 종류의 트리거(EventTrigger)에
    ///    선언된 BeginStoryboard를 이름으로 참조**하는 형태에서 예외 없이 수행되는지
    /// ②가 실패하면 `InvalidOperationException`이 Loading→Ready 전이(가장 많이 지나가는 경로)에서 UI 스레드로
    /// 던져져 `DispatcherUnhandledException` → `TryReturnHome()` → **손님이 촬영을 누르면 홈으로 튕긴다.**
    /// XAML 컴파일과 테마 로드는 BAML 파싱만 하므로 `BeginStoryboardName` 해석을 검증하지 않는다.
    /// </summary>
    [Fact]
    public void Spinner_Ring_Trigger_Chain_Runs_Without_Exception()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            var template = (System.Windows.Controls.ControlTemplate)theme["Spinner.Ring"];

            var ctl = new System.Windows.Controls.Control { Template = template, Width = 56, Height = 56 };
            // 실제 사용처와 같은 조건: 부모가 Visibility를 토글하고, 스피너는 처음부터 보이는 상태로 로드된다.
            var host = new System.Windows.Controls.Border { Child = ctl, Width = 100, Height = 100 };
            host.Measure(new System.Windows.Size(100, 100));
            host.Arrange(new System.Windows.Rect(0, 0, 100, 100));
            ctl.ApplyTemplate();

            var ring = (System.Windows.Shapes.Ellipse)template.FindName("SpinnerRing", ctl);
            var rot = (System.Windows.Media.RotateTransform)ring.RenderTransform;

            // ① EventTrigger(Loaded) 발화 — TargetName 해석이 실패하면 여기서 예외가 난다.
            ctl.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.FrameworkElement.LoadedEvent, ctl));

            // Angle에 애니메이션 클록이 실제로 붙었는지: 애니메이션이 걸린 DP는 기본값 대신
            // AnimationBaseValue와 구분되는 현재값을 가지며, HasAnimatedProperties가 true가 된다.
            Assert.True(rot.HasAnimatedProperties,
                "Loaded 트리거의 Storyboard가 SpinnerRotate.Angle에 붙지 않았다 — TargetName 해석 실패");

            // ② IsVisible=False 진입 → PauseStoryboard, 복귀 → ResumeStoryboard.
            //    BeginStoryboardName("SpinnerSpin") 해석이 실패하면 InvalidOperationException.
            host.Visibility = System.Windows.Visibility.Collapsed;
            ctl.UpdateLayout();
            host.Visibility = System.Windows.Visibility.Visible;
            ctl.UpdateLayout();

            // 재개 후에도 애니메이션이 유지된다(Resume이 클록을 떼지 않는다).
            Assert.True(rot.HasAnimatedProperties,
                "Visibility 토글 후 Angle 애니메이션이 사라졌다 — Pause/Resume 이름 해석 확인 필요");
        });
    }

    /// <summary>
    /// it20 M4: 대기 오버레이 바인딩이 ViewModel 멤버와 일치하는지 정적으로 고정한다.
    /// 원래 결함이 "IsLoading 선언은 있는데 바인딩이 없는 조용한 실패"였으므로, XAML 오타
    /// (IsLoadng 등)나 VM 멤버 개명이 테스트로 드러나게 한다. 테마 키 해석 테스트는 Path를 보지 않는다.
    /// (FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm과 같은 계열의 정적 안전망)
    /// </summary>
    [Fact]
    public void FrameSelectView_Waiting_Bindings_Exist_On_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "FrameSelectView.xaml"));
        var vmType = typeof(MCPhoto.App.ViewModels.FrameSelectViewModel);

        foreach (var member in new[]
                 {
                     "IsLoading", "IsLoadFailed", "IsDegraded",
                     "LoadingMessage", "LoadNotice",
                     "SkipServerWaitCommand", "RetryLoadCommand",
                 })
        {
            // ⚠️ 부분 문자열 검사(Assert.Contains)로는 부족하다 — 이 파일 주석에 IsLoading·IsInteractive 같은
            //    이름이 등장하므로 바인딩이 사라져도 통과한다. 실제 `{Binding <멤버>...}` 형태를 요구한다.
            var binding = new Regex(@"\{Binding\s+" + Regex.Escape(member) + @"\s*[,}]");
            Assert.True(binding.IsMatch(text),
                $"FrameSelectView.xaml 에 '{{Binding {member}}}' 바인딩이 없다(주석에만 있는 것은 무효)");
            Assert.NotNull(vmType.GetProperty(member));          // VM에 같은 이름의 public 멤버가 있다
        }
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

    // ── it23: 외부 카메라 UI 바인딩 정적 안전망 (설계 §14.4 T-X1) ──
    // 바인딩 경로 오타는 **예외 없이 조용히 실패**한다(빈 화면·비활성 컨트롤). 단위 테스트로는 잡히지 않아
    // XAML 텍스트와 VM 멤버를 대조한다.

    /// <summary>
    /// 설정 화면 외부 장치 섹션이 참조하는 VM 멤버가 실재한다.
    /// 하나라도 오타면 그 컨트롤이 조용히 비어 있어 "설정을 켰는데 아무 일도 안 난다"가 된다.
    /// </summary>
    [Fact]
    public void SettingsView_External_Camera_Bindings_Exist_On_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));
        var vm = typeof(MCPhoto.App.ViewModels.SettingsViewModel);

        foreach (var member in new[]
                 {
                     // it25 §6: "지원 모델" 콤보가 "인식된 카메라" 콤보로 전환됐다 —
                     //          ItemsSource=RecognizedCameraOptions · SelectedValue=RecognizedCameraSelection.
                     "ExternalCameraEnabled", "RecognizedCameraOptions", "RecognizedCameraSelection",
                     "CanEditExternalCamera", "ExposureParameters", "HasExposureDomain",
                 })
        {
            // ⚠️ 부분 문자열이 아니라 실제 `{Binding …}` 형태를 요구한다(주석에만 남은 이름 통과 방지).
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        // ★ ini 미러(ExternalCameraModel)는 **콤보에 직접 바인딩되지 않는다**(it25 §6.3):
        //   직접 바인딩하면 인식 목록이 비는 순간 WPF가 저장값을 null로 되써서 소멸시킨다.
        //   VM 속성 자체는 남아 있어야 한다(저장·md3 경로·USB 키워드의 기준값).
        Assert.NotNull(vm.GetProperty("ExternalCameraModel"));
        Assert.DoesNotMatch(@"\{Binding\s+ExternalCameraModel\s*[,}]", text);

        // 노출 행 DataTemplate이 참조하는 멤버는 행 VM(ExposureParameterViewModel)에 있다.
        var row = typeof(MCPhoto.App.ViewModels.ExposureParameterViewModel);
        foreach (var member in new[] { "Label", "MaxIndex", "SelectedIndex", "Text", "Hint", "HasHint", "IsDomainAvailable" })
            Assert.NotNull(row.GetProperty(member));

        // 인식 콤보는 값 기반이어야 한다(it7 B9: SelectedIndex 바인딩은 저장값을 0으로 덮어쓴다).
        Assert.Contains("SelectedValuePath=\"Value\"", text);
        Assert.DoesNotContain("SelectedIndex=\"{Binding", text);
        Assert.NotNull(typeof(MCPhoto.App.ViewModels.RecognizedCameraOption).GetProperty("Value"));
        Assert.NotNull(typeof(MCPhoto.App.ViewModels.RecognizedCameraOption).GetProperty("Display"));

        // "(추후 지원)" 딱지는 외부 카메라에서 떼어졌다(프린터 행 문구는 유지).
        Assert.DoesNotContain("외부 장치 (추후 지원)", text);

        // 편집 게이트가 **노출 3행에도** 걸려 있다(§8.3-3 · §9.2 TempUser 읽기 전용).
        // ⚠️ 행 내부 컨트롤의 DataContext는 ExposureParameterViewModel이라 거기서는 이 게이트를 해석할 수
        //    없다 → ItemsControl 자신에 걸어야 한다. 누락하면 TempUser가 값을 고쳐 저장한 뒤(저장은 미기록)
        //    "저장되었습니다" 직후 입력이 조용히 되돌아간다(성공 오인).
        var exposureList = Regex.Match(text, @"<ItemsControl[^>]*\{Binding ExposureParameters\}[^>]*>",
            RegexOptions.Singleline);
        Assert.True(exposureList.Success, "노출 3행 ItemsControl을 찾지 못함");
        Assert.Contains(@"IsEnabled=""{Binding CanEditExternalCamera}""", exposureList.Value);
    }

    // ── it24: 외부 장치 섹션 개편(가시성·장치 검색·지원 모델 캡션·프린터 열거) 정적 안전망 ──
    // 설계: docs/design/wpf-it24-external-device-discovery-design.md §8

    /// <summary>
    /// T-X1'' — 개편 섹션·오버레이가 참조하는 VM 멤버가 전부 실재한다.
    /// 바인딩 경로 오타는 <b>예외 없이 조용히 실패</b>하므로(빈 문구·영구 비활성 버튼) 빌드가 잡지 못한다.
    /// </summary>
    [Fact]
    public void SettingsView_External_Discovery_Bindings_Exist_On_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));
        var vm = typeof(MCPhoto.App.ViewModels.SettingsViewModel);

        foreach (var member in new[]
                 {
                     "IsExternalEditDenied", "IsDiscovering", "DiscoveryHeadline", "DiscoveryDetailLines",
                     // it25: 프린터는 토글 표시값 하나만 남았고, 지원 카메라 오버레이가 신설됐다.
                     "PhotoPrinterEnabled", "IsSupportedCameraListOpen", "SupportedCameraGroups",
                 })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        foreach (var command in new[]
                 {
                     "DiscoverExternalCameraCommand",
                     "OpenSupportedCameraListCommand", "CloseSupportedCameraListCommand",
                 })
        {
            Assert.Matches(@"\{Binding\s+" + command + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(command));
        }

        // 오버레이의 그룹 템플릿이 참조하는 멤버는 그룹 레코드에 있다(제조사 헤더 + 모델 행).
        var group = typeof(MCPhoto.App.ViewModels.SupportedCameraGroup);
        Assert.NotNull(group.GetProperty("Manufacturer"));
        Assert.NotNull(group.GetProperty("Models"));
        Assert.Matches(@"\{Binding\s+Manufacturer\s*[,}]", text);
        Assert.Matches(@"\{Binding\s+Models\s*[,}]", text);

        // ★ it25 §4.1: 프린터 열거 표면이 되살아나지 않았는지 고정한다(멤버·바인딩 모두 부재).
        foreach (var removed in new[]
                 {
                     "PrinterOptions", "PhotoPrinterName", "HasPrinters",
                     "PrinterStateText", "HasPrinterStateText", "RefreshPrintersCommand",
                 })
        {
            Assert.Null(vm.GetProperty(removed));
            Assert.DoesNotMatch(@"\{Binding\s+" + removed + @"\s*[,}]", text);
        }
    }

    /// <summary>
    /// ★ it24 §4.1 — 게스트에게 섹션을 숨기던 게이트가 <b>되살아나지 않았는지</b> 고정한다.
    /// "외부 장치 연결 탭이 사라졌다"는 사용자 피드백의 직접 원인이 그 Visibility 한 줄이었다.
    /// (구 T-V3 "섹션 Collapsed" 테스트를 대체하는 반대 방향의 잠금)
    /// </summary>
    [Fact]
    public void SettingsView_External_Device_Section_Is_Not_Hidden_From_Guests()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));

        // 섹션 제목 바로 앞의 컨테이너에 표시 게이트가 없어야 한다.
        var section = Regex.Match(text,
            @"<StackPanel[^>]*>\s*<Border Style=""\{StaticResource GroupDivider\}"" />\s*<TextBlock Text=""외부 장치""",
            RegexOptions.Singleline);
        Assert.True(section.Success,
            "외부 장치 섹션 컨테이너를 찾지 못했다 — 게스트 가시성 게이트가 다시 붙었는지 확인하라");
        Assert.DoesNotContain("Visibility", section.Value);

        // 편집 게이트 자체는 살아 있어야 한다(보이되 읽기 전용).
        Assert.Contains(@"IsEnabled=""{Binding CanEditExternalCamera}""", text);

        // 섹션 안의 토글은 정확히 2개이며 각각의 게이트가 다르다(it25 §4.1):
        //   ① 외부 카메라 = 편집 게이트(CanEditExternalCamera) — 보이되 권한 없으면 잠긴다.
        //   ② 프린터      = 하드코딩 Disable — 지원되는 항목이 하나도 없어 아무도 편집할 수 없다.
        // 둘을 뒤섞으면 "로그인하면 프린터를 고를 수 있는가"라는 거짓 안내가 생긴다.
        int start = text.IndexOf(@"Text=""외부 장치""", StringComparison.Ordinal);
        int end = text.IndexOf(@"Text=""고급""", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "외부 장치 섹션 경계를 찾지 못함");
        // ⚠️ 주석을 먼저 제거한다 — 이 섹션의 주석이 게이트 변경 이력을 설명하므로,
        //    제거하지 않으면 검사가 자기 자신의 설명문에 걸린다(Nikon csproj 경계 검사와 같은 기법).
        var body = Regex.Replace(text[start..end], @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

        var toggles = Regex.Matches(body, @"<ToggleButton\b.*?/>", RegexOptions.Singleline);
        Assert.Equal(2, toggles.Count);

        var cameraToggle = toggles.Single(t => t.Value.Contains(@"{Binding ExternalCameraEnabled}", StringComparison.Ordinal));
        Assert.Contains(@"IsEnabled=""{Binding CanEditExternalCamera}""", cameraToggle.Value);

        var printerToggle = toggles.Single(t => t.Value.Contains(@"{Binding PhotoPrinterEnabled}", StringComparison.Ordinal));
        Assert.Contains(@"IsEnabled=""False""", printerToggle.Value);
        // 편집 게이트를 프린터에 달면 "권한이 있으면 된다"는 오해가 생긴다 — 그것이 it25가 되돌린 부분이다.
        Assert.DoesNotContain(@"IsEnabled=""{Binding CanEditExternalCamera}""", printerToggle.Value);
    }

    /// <summary>
    /// ★ [지원 카메라 목록] 버튼이 <b>외부 카메라 토글의 Visibility 뒤에 숨지 않는다</b>(팀리드 확정 —
    /// 설계 §7.4의 "하위 패널 안" 배치를 뒤집었다).
    /// <para>
    /// 이 목록이 답하는 질문은 "내 카메라가 지원되나?"이고, 사용자는 그 답을 <b>토글을 켤지 결정하기 전에</b>
    /// 알고 싶어 한다. 하위 패널 안에 두면 지원 여부를 몰라 아직 켜지 않은 사람이 목록에 도달할 수 없다.
    /// 이 프로젝트는 "상태 뒤에 UI를 숨겼다가" 두 번 지적받았으므로(게스트 섹션 Collapsed · 프린터 토글
    /// 강제 Disable) 배치를 위치 단정으로 못박는다 — 주석만 두면 다음 사람이 다시 하위 패널로 옮긴다.
    /// </para>
    /// </summary>
    [Fact]
    public void SettingsView_Supported_Camera_Button_Is_Outside_Toggle_Gated_Panel()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));

        int sectionStart = text.IndexOf(@"Text=""외부 장치""", StringComparison.Ordinal);
        int button = text.IndexOf("OpenSupportedCameraListCommand", StringComparison.Ordinal);
        // 토글 on일 때만 노출되는 하위 패널의 여는 태그.
        int gatedPanel = text.IndexOf(
            @"<StackPanel Visibility=""{Binding ExternalCameraEnabled, Converter={StaticResource BoolToVis}}"">",
            StringComparison.Ordinal);

        Assert.True(sectionStart > 0, "외부 장치 섹션을 찾지 못함");
        Assert.True(button > 0, "[지원 카메라 목록] 버튼 바인딩을 찾지 못함");
        Assert.True(gatedPanel > 0, "외부 카메라 하위 패널(Visibility 게이트)을 찾지 못함");

        // 섹션 안에 있고, **게이트 패널이 열리기 전에** 있어야 한다(= 토글 off에서도 보인다).
        Assert.InRange(button, sectionStart, gatedPanel);

        // 권한 게이트도 붙지 않는다(열람은 편집이 아니다) — 버튼 요소 자체에 IsEnabled가 없어야 한다.
        var element = Regex.Match(text[button..], @"^[^>]*>", RegexOptions.Singleline);
        Assert.True(element.Success);
        Assert.DoesNotContain("IsEnabled", element.Value);
    }

    /// <summary>
    /// 동결 문구(it24 §8.2 W15 + it25 §8.3 W32~W39)가 XAML에 그대로 있고, 폐기된 문구는 사라졌다.
    /// 문구가 바뀌면 운영 문서·테스트와 어긋나므로 텍스트로 고정한다.
    /// </summary>
    [Fact]
    public void SettingsView_External_Discovery_Frozen_Texts()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));

        Assert.Contains("장치 검색", text);                                        // W15 (유지)
        Assert.Contains(@"Text=""추후 지원 예정""", text);                          // W32 (신설)
        Assert.Contains("연결이 인식된 카메라만 표시됩니다. 인식 확인은 [장치 검색], 지원 모델은 [?] 버튼에서 확인하세요.", text);   // W33 (개정)
        // W35 개정: 텍스트 버튼 → 아이콘 전용 버튼(사용자 요구 — 정보 조회 치고 과하게 컸다).
        // 라벨이 사라지므로 **ToolTip·AutomationProperties가 유일한 이름**이 된다 → 둘을 함께 못박는다.
        // 툴팁이 없으면 이 버튼은 화면에서 정체를 알 수 없는 "?" 하나가 되고, AutomationProperties가
        // 없으면 스크린리더 사용자에게는 이름 없는 버튼이 된다.
        Assert.Contains(@"Content=""?"" ToolTip=""지원 카메라 목록 보기""", text);   // W35
        Assert.Contains(@"AutomationProperties.Name=""지원 카메라 목록 보기""", text);
        Assert.Contains(@"Text=""지원 카메라""", text);                             // W36
        Assert.Contains("이 앱이 SDK 연동을 지원하는 카메라 목록입니다. 연결 인식 여부와는 무관합니다 — 연결 확인은 [장치 검색].", text);   // W37
        Assert.Contains(@"Text=""인식된 카메라""", text);                           // §8.1 라벨 변경

        // ★ it25 §8.3 폐기 목록: 콤보의 의미가 지원→인식으로 바뀌어 W24가 거짓이 됐고,
        //   프린터 하위 패널이 사라져 W25·W31이 갈 곳이 없다. 되살아나면 화면이 거짓을 말한다.
        // ⚠️ 주석을 먼저 제거한다 — 이 파일의 주석이 "어떤 문구가 왜 폐기됐는가"를 인용하며 설명하므로,
        //    제거하지 않으면 검사가 자기 자신의 설명문에 걸린다(게스트 가시성 검사와 같은 기법).
        var rendered = Regex.Replace(text, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotContain("연결된 장치 목록이 아닙니다", rendered);              // W24 폐기
        Assert.DoesNotContain("인쇄 기능은 아직 제공되지 않습니다", rendered);       // W25 폐기
        Assert.DoesNotContain(@"Content=""다시 검색""", rendered);                   // W31 폐기
        Assert.DoesNotContain(@"Text=""지원 모델""", rendered);

        // W34는 VM 상수다(sentinel 항목 표시명) — XAML에 하드코딩되면 두 곳이 갈린다.
        Assert.Equal("- 선택안함 -", MCPhoto.App.ViewModels.SettingsViewModel.RecognizedCameraNoneDisplay);
        Assert.DoesNotContain("- 선택안함 -", rendered);
    }

    /// <summary>
    /// ★ 외부 장치 섹션의 <b>모든</b> 바인딩 경로가 실제 멤버로 해석되는지 전수 대조한다.
    /// <para>
    /// 멤버를 하나하나 열거하는 위 테스트는 "내가 적은 것"만 확인한다 — XAML에 오타로 들어간 경로는
    /// 그 목록에 없으므로 잡히지 않는다. 바인딩 오타는 예외도 경고도 없이 빈 칸·영구 비활성으로 나타나므로,
    /// 섹션 안의 경로 집합 자체를 훑어 VM(또는 행 템플릿 VM)에 없는 이름이 있으면 실패시킨다.
    /// </para>
    /// </summary>
    [Fact]
    public void SettingsView_External_Device_Section_Has_No_Unresolved_Binding_Paths()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));

        // 섹션 경계: "외부 장치" 제목 ~ 다음 그룹("고급") 제목.
        int start = text.IndexOf(@"Text=""외부 장치""", StringComparison.Ordinal);
        int end = text.IndexOf(@"Text=""고급""", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "외부 장치 섹션 경계를 찾지 못함");
        var section = text[start..end];

        var vm = typeof(MCPhoto.App.ViewModels.SettingsViewModel);
        var exposureRow = typeof(MCPhoto.App.ViewModels.ExposureParameterViewModel);

        var unresolved = new List<string>();
        foreach (Match m in Regex.Matches(section, @"\{Binding\s*([^},]*)"))
        {
            var path = m.Groups[1].Value.Trim();
            // 빈 경로: 문자열 항목 자신을 표시하는 DataTemplate({Binding}) — 대조할 멤버가 없다.
            if (path.Length == 0) continue;
            if (path.StartsWith("Converter", StringComparison.Ordinal)) continue;

            if (vm.GetProperty(path) is null && exposureRow.GetProperty(path) is null)
                unresolved.Add(path);
        }

        Assert.True(unresolved.Count == 0,
            "외부 장치 섹션이 참조하나 VM에 없는 바인딩 경로: " + string.Join(", ", unresolved));
    }

    /// <summary>
    /// ★ 리소스 키 목록 동결. 병합 딕셔너리 간 <c>StaticResource</c> 교차 참조는 창이 뜨지 않는 사고를
    /// 만들었으므로, 새 키가 <b>테마가 아니라 이 파일의 로컬</b>에만 생기는지를 목록으로 고정한다.
    /// <para>
    /// 로컬 <c>x:Key</c>는 <see cref="SettingsView_StaticResource_Keys_Resolve_In_Theme"/>가 검증 대상에서
    /// 제외하므로 안전하다 — 위험한 것은 <c>Themes/</c>에 키를 추가하는 쪽이다. it24 라이선스 고지가
    /// 로컬 5개(배지·메타 라벨·메타 값·섹션 머리·카드 템플릿)를 더했고, 이 목록에 없는 키가 생기면 실패한다.
    /// </para>
    /// </summary>
    [Fact]
    public void SettingsView_Declares_No_New_Local_Resource_Keys()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));
        var keys = Regex.Matches(text, @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "FullRow", "GroupDivider", "GroupTitle", "GuestGateNote",
                "LicenseBadge", "LicenseCard", "LicenseMetaLabel", "LicenseMetaValue", "LicenseSectionHead",
                "QrLimitNote", "RowLabel", "SettingRow",
            },
            keys);
    }

    /// <summary>
    /// 촬영 화면이 참조하는 it23 VM 멤버가 실재한다(배지 W4·수신 W5·강등 배너 W6·프리뷰 부재 W8).
    /// 동결 문구(§9.4)도 함께 고정한다 — 문구가 바뀌면 운영 문서·테스트와 어긋난다.
    /// </summary>
    [Fact]
    public void CaptureView_External_Camera_Bindings_And_Frozen_Texts()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "CaptureView.xaml"));
        var vm = typeof(MCPhoto.App.ViewModels.CaptureViewModel);

        foreach (var member in new[] { "IsExternalSource", "IsReceiving", "PreviewAbsent", "HasDegradeBanner", "DegradeBanner" })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        Assert.Contains("외부 카메라 촬영 중 — 프리뷰는 참고용입니다", text);   // W4
        Assert.Contains("사진 전송 중…", text);                                  // W5
        Assert.Contains("프리뷰 없음 — 외부 카메라로 촬영됩니다", text);          // W8
    }

    /// <summary>
    /// 카메라 테스트 모달(장치 목록·외부 정보 패널·노출 조정)의 테마 키와 VM 바인딩을 고정한다.
    /// 이 창은 <c>Window</c>라 리소스 키 하나만 어긋나도 <b>창 자체가 뜨지 않는다</b>(XamlParseException).
    /// </summary>
    [Fact]
    public void CameraTestWindow_Keys_And_External_Bindings_Resolve()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "CameraTestWindow.xaml"));
        var referenced = ThemeKeysReferencedBy(text);

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                "CameraTestWindow.xaml 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });

        var vm = typeof(MCPhoto.App.ViewModels.CameraTestViewModel);
        foreach (var member in new[]
                 {
                     "Targets", "SelectedTarget", "PurposeLabel", "IsExternalSelected", "IsWebcamSelected",
                     "ExternalModelName", "ExternalBatteryText", "ExternalCapabilityLines",
                     "ExternalStatus", "HasExternalStatus", "IsExternalConnected",
                     "HasShotImage", "ExposureParameters",
                 })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        // ⚠️ 목록은 값 기반 선택이어야 한다(it7 B9: SelectedIndex는 목록 채움이 초기 선택을 0으로 덮는다).
        Assert.Contains("SelectedItem=\"{Binding SelectedTarget}\"", text);
        Assert.DoesNotContain("SelectedIndex=\"{Binding", text);

        // W9 목적 라벨은 VM이 만든다(문구 하드코딩 금지 — 항목별로 달라진다).
        Assert.Contains("{Binding PurposeLabel}", text);
    }

    // ── it21: 벡터 아이콘 시스템 · 상단 바 재배치 · 창모드 최소 크기 ──
    // 설계: docs/design/wpf-it21-main-visual-redesign-design.md

    /// <summary>…/src/MCPhoto.App 디렉터리.</summary>
    private static string FindAppDir() => Directory.GetParent(FindAppViewsDir())!.FullName;

    /// <summary>
    /// 셸 XAML이 참조하는 테마 키가 전부 해석된다. 상단 바를 벡터 아이콘으로 재작성했으므로
    /// 키 하나만 어긋나도 **창 자체가 뜨지 않는다**(XamlParseException). PinPromptWindow 테스트와 동형.
    /// </summary>
    [Fact]
    public void MainWindow_StaticResource_Keys_Resolve_In_Theme()
    {
        var text = File.ReadAllText(Path.Combine(FindAppDir(), "MainWindow.xaml"));
        var referenced = ThemeKeysReferencedBy(text);

        RunSta(() =>
        {
            var theme = LoadTheme();
            var missing = referenced.Where(k => !theme.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                "MainWindow.xaml 이 참조하나 테마에 없는 StaticResource: " + string.Join(", ", missing));
        });
    }

    /// <summary>
    /// 아이콘 4종이 Geometry로 해석되고, 동결돼 있으며, 24×24 좌표계 안에 있다.
    /// Path 데이터 오타는 빈 Bounds나 좌표계 이탈로 나타난다(빌드는 통과한다).
    /// </summary>
    [Theory]
    [InlineData("Icon.Gear")]
    [InlineData("Icon.Account")]
    [InlineData("Icon.Home")]
    [InlineData("Icon.Camera")]
    public void Icon_Geometries_Resolve_As_Frozen_Geometry(string key)
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            var geo = theme[key] as System.Windows.Media.Geometry;
            Assert.NotNull(geo);
            Assert.True(geo!.IsFrozen, $"{key} 는 po:Freeze=\"True\" 로 동결되어야 한다(공유 렌더 자원)");

            var b = geo.Bounds;
            Assert.False(b.IsEmpty, $"{key} 의 Bounds 가 비어 있다 — Path 데이터 오류");
            Assert.InRange(b.Left, -1.0, 25.0);
            Assert.InRange(b.Top, -1.0, 25.0);
            Assert.InRange(b.Right, -1.0, 25.0);
            Assert.InRange(b.Bottom, -1.0, 25.0);
        });
    }

    /// <summary>
    /// Icon.Glyph 의 Fill 은 조상 버튼의 Foreground 를 따라간다(색 정책을 버튼 하나가 소유).
    /// 이 바인딩이 끊기면 Path.Fill 이 null 이 되어 **아이콘이 투명하게 렌더된다** —
    /// 예외도 경고도 없이 상단 바가 빈 버튼 3개가 되므로, 실제 시각 트리를 만들어 값으로 고정한다.
    /// </summary>
    [Fact]
    public void Icon_Glyph_Fill_Follows_Ancestor_Button_Foreground()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            var path = new System.Windows.Shapes.Path
            {
                Style = (System.Windows.Style)theme["Icon.Glyph"],
                Data = (System.Windows.Media.Geometry)theme["Icon.Gear"],
            };
            var button = new System.Windows.Controls.Button
            {
                Style = (System.Windows.Style)theme["Button.TopBar"],
                Foreground = System.Windows.Media.Brushes.Red,
                Content = path,
            };

            // RelativeSource AncestorType 은 **시각 트리**를 탐색한다 → 템플릿 적용 + 레이아웃이 필요하다.
            var host = new System.Windows.Controls.Border { Child = button, Width = 80, Height = 80 };
            host.Measure(new System.Windows.Size(80, 80));
            host.Arrange(new System.Windows.Rect(0, 0, 80, 80));
            button.ApplyTemplate();
            host.UpdateLayout();

            var fill = path.Fill as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(fill);
            Assert.Equal(System.Windows.Media.Colors.Red, fill!.Color);
        });
    }

    /// <summary>
    /// 요구 2("톱니바퀴가 너무 둥글둥글해 설정임을 모르겠다") 재발 방지.
    /// 톱니가 사라져 단순 원형 실루엣으로 퇴화하면 Bounds 가 뿌리 원 지름(15.2)까지 줄어든다.
    /// 팁 반지름 11.3 → 지름 22.6 을 고정해 "톱니가 있는 형태"를 구조적으로 강제한다.
    /// </summary>
    [Fact]
    public void Gear_Icon_Has_Discernible_Teeth()
    {
        RunSta(() =>
        {
            var theme = LoadTheme();
            var gear = (System.Windows.Media.Geometry)theme["Icon.Gear"];
            var b = gear.Bounds;

            Assert.InRange(b.Width, 22.1, 23.1);    // 팁 지름 22.6 ± 0.5
            Assert.InRange(b.Height, 22.1, 23.1);
            // 축 구멍(지름 7.2) 대비 3배 초과 — 구멍만 남은 도넛으로 퇴화하지 않았음을 고정.
            Assert.True(b.Width > 7.2 * 3,
                $"톱니가 소실된 것으로 보인다(Bounds.Width={b.Width:F2}). 설정 아이콘이 원형으로 읽힌다");
        });
    }

    /// <summary>
    /// 상단 바 3버튼이 아이콘 전용이 되면서 표면 라벨이 사라졌다. 접근 이름과 툴팁이 둘 다 있어야 한다
    /// (NN/g: 아이콘 단독은 거의 항상 모호하다). 하나라도 빠지면 스크린 리더·학습성이 함께 무너진다.
    /// </summary>
    [Fact]
    public void TopBar_Icon_Buttons_Have_Accessibility_Labels()
    {
        var text = File.ReadAllText(Path.Combine(FindAppDir(), "MainWindow.xaml"));

        foreach (var name in new[] { "홈으로", "로그인 또는 계정", "설정" })
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", text);

        Assert.Contains("ToolTip=\"홈으로\"", text);
        Assert.Contains("ToolTip=\"{Binding AccountLabel}\"", text);   // 계정 ID는 툴팁으로 이전됐다
        Assert.Contains("ToolTip=\"설정\"", text);
    }

    // ── it23 B부: 테스트 모드 경고 배너(§B9) ──

    /// <summary>
    /// B-T24: 배너가 루트 Grid의 <b>별 행</b>에 있고, 기존 자식 전부가 row 1로 내려갔는지 고정한다.
    /// <para>
    /// 왜 중요한가: 배너를 상단바 안에 두면 촬영·QR 화면에서 <b>사라진다</b>(상단바가 숨겨진다) → "지울 수 없는
    /// 배너"가 아니게 된다. 또 기존 자식에 <c>Grid.Row="1"</c>이 누락되면 그 요소가 row 0에서 배너와 겹친다.
    /// 겹침은 빌드·단위 테스트로 잡히지 않으므로 XAML 텍스트로 확인한다.
    /// </para>
    /// </summary>
    [Fact]
    public void TestMode_Banner_Occupies_Its_Own_Root_Row()
    {
        var text = File.ReadAllText(Path.Combine(FindAppDir(), "MainWindow.xaml"));

        // 루트 Grid에 행 정의가 생겼다(Auto/*).
        Assert.Contains("<Grid x:Name=\"RootGrid\">", text);
        var rootRows = Regex.Match(text,
            @"<Grid x:Name=""RootGrid"">\s*<Grid\.RowDefinitions>(.*?)</Grid\.RowDefinitions>",
            RegexOptions.Singleline);
        Assert.True(rootRows.Success, "루트 Grid에 RowDefinitions가 없다 — 배너가 기존 화면과 겹친다");
        Assert.Contains(@"Height=""Auto""", rootRows.Groups[1].Value);
        Assert.Contains(@"Height=""*""", rootRows.Groups[1].Value);

        // 배너는 row 0, 표시 조건은 IsTestMode 단독(세션 상태와 무관 — 불변식 TM4).
        var banner = Regex.Match(text, @"<Border Grid\.Row=""0""[^>]*?IsTestMode.*?</Border>", RegexOptions.Singleline);
        Assert.True(banner.Success, "row 0의 테스트 모드 배너를 찾지 못함");
        Assert.Contains("Brush.Danger.Surface", banner.Value);
        Assert.Contains("{Binding TestModeBannerText}", banner.Value);
        Assert.Contains("AutomationProperties.Name", banner.Value);   // 아이콘 없는 경고 → 접근 이름 필수
        // 닫기 버튼·애니메이션이 없다(지울 수 없는 배너라는 것이 이 기능의 대가다).
        Assert.DoesNotContain("<Button", banner.Value);
        Assert.DoesNotContain("Storyboard", banner.Value);

        // 기존 자식 5개가 전부 row 1로 내려갔다. 누락하면 배너와 겹친다(UV-4 검증 항목).
        Assert.Contains(@"<ContentControl Grid.Row=""1""", text);
        Assert.Contains(@"<Grid x:Name=""TopBar"" Grid.Row=""1""", text);
        Assert.Contains(@"<TextBlock Grid.Row=""1"" Text=""{Binding VersionText}""", text);
        Assert.Contains(@"<Border Grid.Row=""1"" Visibility=""{Binding HasToast", text);
        Assert.Contains(@"<Grid Grid.Row=""1"" Background=""{StaticResource Brush.Scrim}""", text);
    }

    /// <summary>
    /// 배너 문구가 VM 상수와 일치하는지(문구가 두 곳에서 갈리지 않게) + 셸에 바인딩 대상이 실재하는지.
    /// </summary>
    [Fact]
    public void TestMode_Banner_Bindings_Exist_On_Shell()
    {
        var shell = typeof(MCPhoto.App.AppShellViewModel);

        Assert.NotNull(shell.GetProperty("IsTestMode"));
        Assert.NotNull(shell.GetProperty("TestModeBannerText"));
        Assert.NotNull(shell.GetProperty("TestLoginLabel"));

        // 로그인 화면의 재로그인 버튼 바인딩(테스트 모드에서만 노출).
        var loginXaml = File.ReadAllText(Path.Combine(FindAppViewsDir(), "LoginGuestView.xaml"));
        foreach (var member in new[] { "TestLoginLabel", "TestLoginCommand", "IsTestMode" })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", loginXaml);
            Assert.NotNull(typeof(MCPhoto.App.ViewModels.LoginGuestViewModel).GetProperty(member));
        }
    }

    // ── it24: 프로젝트 라이선스 고지 오버레이(설계 §3, it23 C부 재설계) ──

    /// <summary>
    /// C-T16: 오버레이가 참조하는 VM 멤버가 실재한다. 바인딩 경로 오타는 <b>예외 없이 조용히 실패</b>해
    /// (빈 카드·빈 본문) 단위 테스트로는 잡히지 않는다. 테마 키 해석은
    /// <see cref="SettingsView_StaticResource_Keys_Resolve_In_Theme"/>가 이미 검사한다.
    /// </summary>
    [Fact]
    public void SettingsView_License_Viewer_Bindings_Exist_On_Vm()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "SettingsView.xaml"));
        var vm = typeof(MCPhoto.App.ViewModels.SettingsViewModel);

        foreach (var member in new[]
                 {
                     "IsLicenseViewerOpen", "IsLicenseSummaryPage", "IsLicenseFullTextPage",
                     "LicenseSelfComponents", "LicenseBundledComponents",
                     "HasLicenseSelfComponents", "HasLicenseBundledComponents",
                     "LicenseDocuments", "HasLicenseDocuments", "SelectedLicenseDocument",
                     "LicenseDegradedMessage", "HasLicenseDegraded",
                     "LicenseErrorMessage", "HasLicenseError",
                     "LicenseText", "IsLicenseLoading",
                     "LicenseFullTextCaption", "LicenseFullTextSubtitle", "LicenseNoticeAsOfText",
                 })
        {
            Assert.Matches(@"\{Binding\s+" + member + @"\s*[,}]", text);
            Assert.NotNull(vm.GetProperty(member));
        }

        foreach (var command in new[]
                 {
                     "OpenLicenseViewerCommand", "CloseLicenseViewerCommand",
                     "ShowLicenseFullTextCommand", "ShowLicenseNoticeCommand",
                     "BackToLicenseSummaryCommand", "EscapeLicenseViewerCommand",
                 })
        {
            Assert.Contains(command, text);
            Assert.NotNull(vm.GetProperty(command));
        }

        // 카드 소스는 두 ItemsControl이 같은 템플릿을 공유한다(카드 규격이 한 곳에만 있다).
        Assert.Equal(2, Regex.Matches(text, @"ItemTemplate=""\{StaticResource LicenseCard\}""").Count);

        // 폴백·미참조 목록은 값 기반 선택이어야 한다(it7 B9: SelectedIndex는 목록 채움이 초기 선택을 0으로 덮는다).
        Assert.Contains("SelectedItem=\"{Binding SelectedLicenseDocument}\"", text);

        // 폴백 목록 항목은 말줄임 + ToolTip으로 전체 이름을 보여준다(리포의 고정폭 잘림 이력 대비).
        var item = Regex.Match(text, @"<TextBlock Text=""\{Binding DisplayName\}"".*?/>", RegexOptions.Singleline);
        Assert.True(item.Success, "폴백 문서 목록 항목 템플릿을 찾지 못함");
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", item.Value);
        Assert.Contains("ToolTip=\"{Binding DisplayName}\"", item.Value);
    }

    /// <summary>
    /// 브랜드-홈 칩은 홈 화면에서 숨어야 한다 — 눌러도 아무 일이 없는 버튼은 어포던스 거짓말이다.
    /// 홈 복귀 커맨드 바인딩도 함께 고정한다(칩 재작성 시 커맨드가 조용히 빠지는 것을 막는다).
    /// </summary>
    [Fact]
    public void TopBar_Home_Button_Is_Gated_By_IsHome()
    {
        var text = File.ReadAllText(Path.Combine(FindAppDir(), "MainWindow.xaml"));

        var brand = Regex.Match(text, @"<Button\b[^>]*?Button\.TopBar\.Brand.*?</Button>", RegexOptions.Singleline);
        Assert.True(brand.Success, "MainWindow.xaml 에서 브랜드-홈 칩(Button.TopBar.Brand)을 찾지 못함");

        Assert.Contains("IsHome", brand.Value);
        Assert.Contains("InverseBoolToVis", brand.Value);
        Assert.Contains("GoHomeCommand", brand.Value);
        // 워드마크는 App.xaml.cs 가 런타임 교체한다 — StaticResource 로 바꾸면 브랜딩 교체가 무효화된다.
        Assert.Contains("{DynamicResource Branding.AppName}", brand.Value);
    }

    /// <summary>
    /// 창모드 최소 크기(요구 5) 회귀 방지. 하한은 XAML 하드코딩이 아니라 표시 모드 분기가 소유한다.
    /// 전체화면에서는 하한을 해제하는데, 그 해제가 Maximized **앞**에 와야 한다 —
    /// 뒤에 두면 작은 패널에서 한 프레임 동안 창이 화면을 넘긴다(설계 §8.3 P3).
    /// </summary>
    [Fact]
    public void MainWindow_Minimum_Size_Is_Mode_Scoped()
    {
        var appDir = FindAppDir();
        var xaml = File.ReadAllText(Path.Combine(appDir, "MainWindow.xaml"));
        var cs = File.ReadAllText(Path.Combine(appDir, "MainWindow.xaml.cs"));

        var windowTag = Regex.Match(xaml, @"<Window\b.*?>", RegexOptions.Singleline);
        Assert.True(windowTag.Success, "MainWindow.xaml 에서 Window 여는 태그를 찾지 못함");
        Assert.DoesNotContain("MinWidth", windowTag.Value);
        Assert.DoesNotContain("MinHeight", windowTag.Value);

        Assert.Matches(@"WindowedMinWidth\s*=\s*800", cs);
        Assert.Matches(@"WindowedMinHeight\s*=\s*600", cs);

        int release = cs.IndexOf("MinWidth = 0", StringComparison.Ordinal);
        int maximized = cs.IndexOf("WindowState.Maximized", StringComparison.Ordinal);
        Assert.True(release > 0, "전체화면 분기에서 MinWidth = 0 하한 해제를 찾지 못함");
        Assert.True(release < maximized,
            "하한 해제(MinWidth = 0)가 Maximized 적용보다 앞에 있어야 한다 — 작은 패널에서 창이 화면을 넘긴다");
    }

    /// <summary>
    /// Home 구조의 바인딩 고정. 층4 안내는 **버튼이 아니라 문구**이며, 문구 자체를 VM이 결정한다
    /// (권한에 따라 참인 문장이 달라지기 때문 — §7.4 개정).
    /// </summary>
    [Fact]
    public void HomeView_Bindings_Exist_And_Hint_Is_Vm_Driven()
    {
        var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "HomeView.xaml"));
        var vmType = typeof(MCPhoto.App.ViewModels.HomeViewModel);

        foreach (var member in new[] { "StartCommand", "FrameStepHint", "HasFrameStepHint" })
        {
            var binding = new Regex(@"\{Binding\s+" + Regex.Escape(member) + @"\s*[,}]");
            Assert.True(binding.IsMatch(text), $"HomeView.xaml 에 '{{Binding {member}}}' 바인딩이 없다");
            Assert.NotNull(vmType.GetProperty(member));
        }

        // 보조 문구는 빈 값일 때 숨겨져야 한다(빈 줄이 남으면 스트립 정렬이 흔들린다).
        var hint = Regex.Match(text, @"<TextBlock\b[^>]*?FrameStepHint.*?/>", RegexOptions.Singleline);
        Assert.True(hint.Success, "HomeView.xaml 에서 보조 문구 TextBlock을 찾지 못함");
        Assert.Contains("HasFrameStepHint", hint.Value);
        Assert.Contains("BoolToVis", hint.Value);

        // 층4 Ghost 버튼은 폐기됐다 — 되살아나면 주 액션이 둘로 보인다(§7.4 개정 사유 ①).
        Assert.DoesNotContain("LoginCommand", text);
        Assert.Null(vmType.GetProperty("LoginCommand"));

        // 흐름 안내(층3)는 비상호작용이어야 한다 — 눌리는 것처럼 보이면 키오스크 오조작이 된다.
        var strip = Regex.Match(text, @"<Grid\b[^>]*?x:Name=""FlowStrip""[^>]*?>", RegexOptions.Singleline);
        Assert.True(strip.Success, "HomeView.xaml 에서 흐름 안내 스트립(FlowStrip)을 찾지 못함");
        Assert.Contains("IsHitTestVisible=\"False\"", strip.Value);
    }
}
