using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class LoginGuestView : UserControl
{
    /// <summary>Mode 변경 시 포커스 이동을 위해 구독한 VM(해제 경로 확보용). null이면 미구독.</summary>
    private LoginGuestViewModel? _subscribedVm;

    public LoginGuestView()
    {
        InitializeComponent();
        // 오버레이 파기 시 VM 이벤트 구독을 반드시 해제(누수 방지).
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    // U8: 진입 시 아이디 입력창 자동 포커스. FocusManager 대신 코드로 현재 모드에 맞춰 포커스. (it5 §7)
    private void OnLoaded(object sender, RoutedEventArgs e) => FocusForMode();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 이전 VM 구독 해제 후 새 VM 구독(재사용/교체 대비).
        DetachVm();
        if (DataContext is LoginGuestViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            _subscribedVm = vm;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachVm();

    private void DetachVm()
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;
        }
    }

    // Mode 변경 시 해당 섹션의 첫 입력으로 포커스(순수 뷰 로직).
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginGuestViewModel.Mode))
            FocusForMode();
    }

    // 현재 모드에 맞는 첫 입력 컨트롤에 포커스. 레이아웃 확정 후 실행되도록 Input 우선순위로 디스패치.
    private void FocusForMode()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (DataContext is not LoginGuestViewModel vm) return;
            Control? target = vm.IsSignUp ? SignUpIdTextBox : IdTextBox;
            if (target is null) return;
            target.Focus();
            Keyboard.Focus(target);
        }));
    }

    // PasswordBox는 보안상 바인딩 불가 → 코드비하인드에서 VM으로 전달.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
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

    // SignUp 비밀번호(PasswordBox) → VM 전달 + 인라인 검증 갱신(바인딩 금지).
    private void OnSignUpPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginGuestViewModel vm && sender is PasswordBox pb)
        {
            vm.SignUpPassword = pb.Password;
            vm.RefreshSignUpValidation();
        }
    }

    private void OnSignUpPasswordConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginGuestViewModel vm && sender is PasswordBox pb)
        {
            vm.SignUpPasswordConfirm = pb.Password;
            vm.RefreshSignUpValidation();
        }
    }
}
