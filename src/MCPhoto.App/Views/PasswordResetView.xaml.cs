using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class PasswordResetView : UserControl
{
    public PasswordResetView() => InitializeComponent();

    // 진입 시 아이디/이메일 입력창 자동 포커스(LoginGuestView 패턴).
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            IdOrEmailTextBox.Focus();
            Keyboard.Focus(IdOrEmailTextBox);
        }));
    }

    // PasswordBox는 보안상 바인딩 불가 → 코드비하인드에서 VM으로 전달(기존 패턴).
    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordResetViewModel vm && sender is PasswordBox pb)
            vm.NewPassword = pb.Password;
    }

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordResetViewModel vm && sender is PasswordBox pb)
            vm.ConfirmPassword = pb.Password;
    }
}
