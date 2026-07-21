using System.Windows.Controls;
using System.Windows.Input;
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

    // U3: PasswordBox에서 Enter → 로그인 커맨드 실행(로직은 VM, 여기선 실행만). (it3 §6)
    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is LoginGuestViewModel vm && vm.LoginCommand.CanExecute(null))
        {
            vm.LoginCommand.Execute(null);
            e.Handled = true;
        }
    }
}
