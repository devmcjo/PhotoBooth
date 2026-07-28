using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MCPhoto.App.Views;

/// <summary>
/// 설정·계정 관리 진입 PIN 게이트 모달(it14 §5.4). 두 모드:
///  - Verify(HasPin=true): PIN 1회 입력 → verifyAsync 대조. 성공 확인 시 DialogResult=true.
///  - Setup(HasPin=false, 최초 설정): 새 PIN 2회 입력(일치 확인) → setAsync(newPin). 성공 시 DialogResult=true.
/// 실패·오류는 창을 닫지 않고 인라인 오류로 안내한다(fail-closed).
///
/// it15 §5.6(R1 완화): PIN이 유일한 게이트 자격증명이 되어 4자리=10,000 조합의 온라인 브루트포스가
/// 이론상 가능하다. 서버 잠금은 타인 계정 락아웃(DoS)을 새로 만들므로 범위 밖으로 두고,
/// 클라에서 ① 연속 <see cref="MaxFailedAttempts"/>회 불일치 시 창 자동 닫힘(게이트 미통과),
/// ② 불일치마다 <see cref="FailureCooldown"/> 입력 비활성으로 시도 속도를 낮춘다.
/// </summary>
public partial class PinPromptWindow : Window
{
    /// <summary>연속 PIN 불일치 허용 횟수. 초과 시 창을 닫아 게이트를 통과시키지 않는다(it15 §5.6).</summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>불일치 후 입력 비활성 시간(rate limit, it15 §5.6).</summary>
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(1.5);

    private readonly Func<string, Task<bool>>? _verify;
    private readonly Func<string, Task>? _setup;
    private readonly bool _isSetup;
    private bool _busy;

    /// <summary>연속 PIN 불일치 횟수(형식 오류·네트워크 오류는 세지 않는다 — 서버에 도달한 시도만).</summary>
    private int _failedAttempts;

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

            // it15 §5.6: 서버에 도달한 불일치만 카운트(형식 오류는 위에서 이미 return).
            _failedAttempts++;
            if (_failedAttempts >= MaxFailedAttempts)
            {
                ShowError($"PIN을 {MaxFailedAttempts}회 연속 틀렸습니다. 창을 닫습니다.");
                await Task.Delay(FailureCooldown);   // 안내를 읽을 시간
                DialogResult = false;                // 게이트 미통과로 닫는다
                return;
            }

            ShowError($"PIN이 일치하지 않습니다. ({_failedAttempts}/{MaxFailedAttempts})");
            await ApplyFailureCooldownAsync();
        }
        catch (Exception)
        {
            // 네트워크/서버 오류(PIN 미설정 409 포함) → fail-closed. 게이트를 열지 않는다(DialogResult 미설정).
            // 자격 문제가 아니므로 실패 횟수에 포함하지 않는다(정상 사용자가 네트워크 장애로 잠기지 않게).
            ShowError("확인할 수 없습니다. 네트워크를 확인하세요.");
        }
        finally
        {
            // 창이 닫히는 중(DialogResult가 true/false로 확정)이면 컨트롤을 되살리지 않는다.
            if (DialogResult is null)
            {
                SetBusy(false);
                _busy = false;
                Pin1.SelectAll();
                Pin1.Focus();
            }
        }
    }

    /// <summary>
    /// 불일치 후 입력 비활성 유지(rate limit, it15 §5.6). 입력·버튼은 SetBusy(true)로 이미 잠겨 있고,
    /// "확인 중" 표시만 내려 쿨다운임을 구분한다. finally가 대기 종료 후 재활성화한다.
    /// </summary>
    private async Task ApplyFailureCooldownAsync()
    {
        BusyText.Visibility = Visibility.Collapsed;
        await Task.Delay(FailureCooldown);
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
