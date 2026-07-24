using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 비밀번호 찾기(비로그인 재설정) 2단계 플로우(item1a §9.4). 백엔드 모드 전용.
///
/// 1단계: idOrEmail 입력 → 재설정 요청(서버는 항상 202, 열거 방지) → "메일 확인" 안내로 전환.
/// 2단계: 6자리 코드 + 새 비밀번호(2회 확인, PasswordBox는 code-behind 전달) → 재설정 확인.
/// 성공 시 로그인 화면(오버레이 복귀 지점)으로 돌아간다.
/// </summary>
public sealed partial class PasswordResetViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ILogger<PasswordResetViewModel>? _logger;

    /// <summary>진행 단계. false=요청(1단계), true=코드+새 비번 입력(2단계).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRequestStep))]
    private bool _isConfirmStep;

    public bool IsRequestStep => !IsConfirmStep;

    // 1단계 입력
    [ObservableProperty] private string _idOrEmail = string.Empty;

    // 2단계 입력
    [ObservableProperty] private string _code = string.Empty;
    public string NewPassword { get; set; } = string.Empty;     // code-behind 전달
    public string ConfirmPassword { get; set; } = string.Empty; // code-behind 전달

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private bool _isBusy;

    public PasswordResetViewModel(AppShellViewModel shell, IAccountService accounts,
        ILogger<PasswordResetViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        // 매 진입 시 초기 상태(1단계)로. Transient VM이라 사실상 새 인스턴스지만 방어적으로 리셋.
        IsConfirmStep = false;
        IdOrEmail = string.Empty;
        Code = string.Empty;
        NewPassword = ConfirmPassword = string.Empty;
        SetMessage(string.Empty, isError: false);
        return Task.CompletedTask;
    }

    /// <summary>1단계: 재설정 요청. 서버는 존재/상태 무관 성공(열거 방지) → 항상 2단계로 진행.</summary>
    [RelayCommand]
    private async Task RequestReset()
    {
        if (IsBusy) return;
        var idOrEmail = IdOrEmail.Trim();
        if (string.IsNullOrWhiteSpace(idOrEmail))
        {
            SetMessage("아이디 또는 이메일을 입력하세요.", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            await _accounts.RequestPasswordResetAsync(idOrEmail);
            // 열거 방지: 계정 존재 여부와 무관하게 동일 안내 후 코드 입력 단계로.
            IsConfirmStep = true;
            SetMessage("입력하신 정보로 계정이 있으면 인증 코드를 이메일로 보냈습니다. 코드와 새 비밀번호를 입력하세요.", isError: false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "비밀번호 재설정 요청 실패(네트워크?)");
            SetMessage("요청을 처리할 수 없습니다. 네트워크를 확인해 주세요.", isError: true);
        }
        finally { IsBusy = false; }
    }

    /// <summary>2단계: 코드 + 새 비밀번호로 재설정 확인. 성공 시 로그인 화면 복귀.</summary>
    [RelayCommand]
    private async Task ConfirmReset()
    {
        if (IsBusy) return;
        var code = Code.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SetMessage("인증 코드를 입력하세요.", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            SetMessage("새 비밀번호를 입력하세요.", isError: true);
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            SetMessage("새 비밀번호가 일치하지 않습니다.", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            await _accounts.ConfirmPasswordResetByCodeAsync(IdOrEmail.Trim(), code, NewPassword);
            SetMessage("비밀번호가 재설정되었습니다. 새 비밀번호로 로그인하세요.", isError: false);
            NewPassword = ConfirmPassword = string.Empty;
            await _shell.ReturnFromOverlay(); // 로그인 화면(진입 전 지점)으로 복귀
        }
        catch (ArgumentException)
        {
            // 400: 코드/비번 형식 오류.
            SetMessage("인증 코드 또는 비밀번호 형식이 올바르지 않습니다.", isError: true);
        }
        catch (Exception ex)
        {
            // 401(코드 불일치·만료) 등은 InvalidOperationException으로 매핑됨.
            _logger?.LogWarning(ex, "비밀번호 재설정 확인 실패");
            SetMessage("인증 코드가 올바르지 않거나 만료되었습니다.", isError: true);
        }
        finally { IsBusy = false; }
    }

    /// <summary>취소: 로그인 화면으로 복귀(진입 전 지점).</summary>
    [RelayCommand]
    private async Task Cancel() => await _shell.ReturnFromOverlay();

    private void SetMessage(string text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }
}
