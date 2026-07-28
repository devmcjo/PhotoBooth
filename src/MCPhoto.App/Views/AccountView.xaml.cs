using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class AccountView : UserControl
{
    public AccountView() => InitializeComponent();

    // PasswordBox는 보안상 바인딩 불가 → 코드비하인드에서 VM으로 전달(기존 패턴)
    private void OnNewPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.NewPassword = pb.Password;
    }

    private void OnConfirmPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.ConfirmPassword = pb.Password;
    }

    private void OnNewAccountPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.NewAccountPassword = pb.Password;
    }

    // ── it14: PIN 변경 PasswordBox → VM 전달(비번과 동일 패턴) + 숫자만 입력 제한 ──
    private void OnCurrentPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.CurrentPin = pb.Password;
    }

    private void OnNewPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.NewPin = pb.Password;
    }

    private void OnConfirmPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox pb)
            vm.ConfirmPin = pb.Password;
    }

    /// <summary>PIN 입력란은 숫자만 허용(4자리 숫자 PIN). 비숫자 입력을 차단한다.</summary>
    private void OnPinPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
