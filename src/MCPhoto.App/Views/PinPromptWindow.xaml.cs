using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MCPhoto.App.Views;

/// <summary>
/// 설정 진입 PIN 게이트 모달(SSO 계정 전용, it14 §5.4). 두 모드:
///  - Verify(HasPin=true): PIN 1회 입력 → verifyAsync 대조. 성공 확인 시 DialogResult=true.
///  - Setup(HasPin=false, 최초 설정): 새 PIN 2회 입력(일치 확인) → setAsync(newPin). 성공 시 DialogResult=true.
/// 실패·오류는 창을 닫지 않고 인라인 오류로 안내한다(fail-closed, PasswordPromptWindow 패턴 계승).
/// </summary>
public partial class PinPromptWindow : Window
{
    private readonly Func<string, Task<bool>>? _verify;
    private readonly Func<string, Task>? _setup;
    private readonly bool _isSetup;
    private bool _busy;

    /// <summary>PIN 확인 모드(HasPin=true). 입력값을 verify로 대조.</summary>
    public PinPromptWindow(Func<string, Task<bool>> verify)
    {
        InitializeComponent();
        _verify = verify ?? throw new ArgumentNullException(nameof(verify));
        _isSetup = false;
        ConfigureVerifyUi();
        Loaded += (_, _) => Pin1.Focus();
    }

    /// <summary>PIN 최초 설정 모드(HasPin=false). 2회 입력 일치 확인 후 setup(newPin).</summary>
    public PinPromptWindow(Func<string, Task> setup)
    {
        InitializeComponent();
        _setup = setup ?? throw new ArgumentNullException(nameof(setup));
        _isSetup = true;
        ConfigureSetupUi();
        Loaded += (_, _) => Pin1.Focus();
    }

    private void ConfigureVerifyUi()
    {
        Title = "PIN 확인";
        TitleText.Text = "설정 잠김";
        DescText.Text = "설정 진입 PIN을 입력하세요(4자리 숫자).";
        Pin1Label.Text = "PIN";
        ConfirmSection.Visibility = Visibility.Collapsed;
    }

    private void ConfigureSetupUi()
    {
        Title = "PIN 설정";
        TitleText.Text = "PIN 설정";
        DescText.Text = "설정 진입에 사용할 PIN을 설정하세요(4자리 숫자).";
        Pin1Label.Text = "새 PIN";
        ConfirmSection.Visibility = Visibility.Visible;
    }

    /// <summary>숫자만 입력 허용(4자리 숫자 PIN). 비숫자 입력을 차단한다.</summary>
    private void OnPinPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    // WPF 이벤트 핸들러(유일한 async void). 내부 예외를 반드시 삼키지 않고 인라인 오류로 처리한다.
    private async void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_busy) return; // 중복 제출 방지(Enter 연타·버튼 재클릭)

        var pin = Pin1.Password;
        // 형식 사전 검증(4자리 숫자). 서버도 검증하지만 왕복 전에 즉시 안내한다.
        if (pin.Length != 4 || !pin.All(char.IsDigit))
        {
            ShowError("PIN은 4자리 숫자여야 합니다.");
            return;
        }
        if (_isSetup && pin != Pin2.Password)
        {
            ShowError("PIN이 일치하지 않습니다. 다시 입력하세요.");
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            if (_isSetup)
            {
                await _setup!(pin);
                DialogResult = true; // 설정 성공 → 창 닫힘(진입 허용)
                return;
            }

            bool ok = await _verify!(pin);
            if (ok)
            {
                DialogResult = true; // 검증 성공 & 확인 → 창 닫힘
                return;
            }

            ShowError("PIN이 일치하지 않습니다.");
        }
        catch (Exception)
        {
            // 네트워크/서버 오류(PIN 미설정 409 포함) → fail-closed. 게이트를 열지 않는다(DialogResult 미설정).
            ShowError("확인할 수 없습니다. 네트워크를 확인하세요.");
        }
        finally
        {
            // DialogResult=true로 이미 닫히는 경우가 아니면 재활성화하고 포커스를 돌려준다.
            if (DialogResult != true)
            {
                SetBusy(false);
                _busy = false;
                Pin1.SelectAll();
                Pin1.Focus();
            }
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_busy) return; // 검증 중에는 취소도 막아 상태 혼선 방지
        DialogResult = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConfirm(sender, e);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        ConfirmButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        Pin1.IsEnabled = !busy;
        Pin2.IsEnabled = !busy;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            // 새 검증 시작 시 이전 오류 메시지를 감춘다.
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
