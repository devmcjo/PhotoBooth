using System.Windows;
using System.Windows.Controls;

namespace MCPhoto.App.Views;

public partial class HomeView : UserControl
{
    /// <summary>
    /// 이 폭 미만이면 Compact 배치로 접는다. (it21 §7.3)
    /// 1008은 Windows 반응형 브레이크포인트의 Large 하한이며, 기준은 화면이 아니라 **앱 창의 폭**이다.
    /// 창모드 하한(800×600)에서도 주 액션이 잘리지 않게 하는 것이 목적이다.
    /// SettingsView의 폭 기반 폴백(TwoColMinWidth)과 같은 선례 패턴을 따른다 — 새 메커니즘을 들이지 않는다.
    /// </summary>
    private const double CompactMaxWidth = 1008;

    public HomeView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyBreakpoint(ActualWidth);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyBreakpoint(e.NewSize.Width);

    /// <summary>
    /// 가용 폭에 따라 브랜드·CTA를 축소하고 흐름 안내(층3)를 접는다.
    /// 접는 순서는 정보 중요도의 역순이다 — 안내가 먼저 사라지고, 브랜드와 주 액션은 끝까지 남는다.
    /// Compact에서도 CTA 높이 64 > Touch.CTA(56)이라 터치 규격을 위반하지 않는다. (it21 §8.5)
    /// </summary>
    private void ApplyBreakpoint(double width)
    {
        bool compact = width < CompactMaxWidth;

        MarkTile.Width = MarkTile.Height = compact ? 64 : 96;
        MarkGlyph.Width = MarkGlyph.Height = compact ? 30 : 44;
        Wordmark.FontSize = compact ? 44 : 64;
        Cta.Height = compact ? 64 : 80;
        Cta.FontSize = compact ? 22 : 26;
        Cta.MinWidth = compact ? 260 : 320;   // Padding이 템플릿에서 무시되므로 폭은 MinWidth가 정한다
        FlowStrip.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
}
