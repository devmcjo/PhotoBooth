using System.Windows.Controls;
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
}
