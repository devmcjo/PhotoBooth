using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class LoginGuestView : UserControl
{
    public LoginGuestView() => InitializeComponent();

    // U8: 진입 시 아이디 입력창 자동 포커스. FocusManager 선언 + 오버레이 재진입 보강(로직 없음, MVVM 유지). (it5 §7)
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            IdTextBox.Focus();
            Keyboard.Focus(IdTextBox);
        }));
    }

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
