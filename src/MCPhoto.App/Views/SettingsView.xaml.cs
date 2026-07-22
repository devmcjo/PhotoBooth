using System.Windows;
using System.Windows.Controls;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class SettingsView : UserControl
{
    // 이 폭 미만이면 2열(촬영|장치·표시)을 1열로 폴백(세로 창·좁은 폭, it4 §5.2 R6).
    private const double TwoColMinWidth = 760;

    public SettingsView() => InitializeComponent();

    /// <summary>가용 폭에 따라 우열을 2열(우측)↔1열(좌열 아래)로 재배치.</summary>
    private void OnTwoColSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool oneColumn = e.NewSize.Width < TwoColMinWidth;
        if (oneColumn)
        {
            Grid.SetColumn(RightCol, 0);
            Grid.SetRow(RightCol, 1);
            ColGap.Visibility = Visibility.Collapsed;
        }
        else
        {
            Grid.SetColumn(RightCol, 2);
            Grid.SetRow(RightCol, 0);
            ColGap.Visibility = Visibility.Visible;
        }
    }
    // 계정 관련 PasswordBox 핸들러는 계정 페이지(AccountView)로 이전됨(it5 §5 C1).
}
