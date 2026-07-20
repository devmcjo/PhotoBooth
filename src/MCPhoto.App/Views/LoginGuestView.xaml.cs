using System.Windows.Controls;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class LoginGuestView : UserControl
{
    public LoginGuestView() => InitializeComponent();

    // PasswordBox는 보안상 바인딩 불가 → 코드비하인드에서 VM으로 전달
    private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LoginGuestViewModel vm && sender is PasswordBox pb)
            vm.Password = pb.Password;
    }
}
