using System.Windows;

namespace MCPhoto.App.Views;

/// <summary>
/// 진단·상태 모달 창. DataContext=DiagnosticsViewModel(다이얼로그 서비스가 주입). (it11 §3.14)
/// 닫기는 code-behind(DialogResult) — VM은 UI 타입 미의존(§1.3), Window 참조 없음.
/// </summary>
public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
