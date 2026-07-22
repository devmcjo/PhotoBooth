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
}
