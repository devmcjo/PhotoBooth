using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>계정 페이지 진입 모드. 팝오버 항목이 지정. (it5 §5 C2)</summary>
public enum AccountMode
{
    /// <summary>비밀번호 변경(로그인 사용자 자기 비번, 2회 확인).</summary>
    PasswordChange,

    /// <summary>계정 생성(power — 역할 게이트).</summary>
    AccountCreate,

    /// <summary>관리자 도구(사용자 관리 진입·앱 종료, power).</summary>
    Admin
}

/// <summary>
/// 계정 전용 페이지 VM. 단일 상태(AppState.Account) + 진입 모드로 UI 분기(상태 폭증 방지, it5 §5 C2).
/// 비번 변경·계정 생성·관리자 도구를 담당(SettingsViewModel에서 이전). 역할 게이트(it2 §7) 유지.
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ILogger<AccountViewModel>? _logger;

    /// <summary>현재 진입 모드. 셸이 진입 전 세팅. UI가 모드별 섹션 표시.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordChange))]
    [NotifyPropertyChangedFor(nameof(IsAccountCreate))]
    [NotifyPropertyChangedFor(nameof(IsAdmin))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private AccountMode _mode = AccountMode.PasswordChange;

    public bool IsPasswordChange => Mode == AccountMode.PasswordChange;
    public bool IsAccountCreate => Mode == AccountMode.AccountCreate;
    public bool IsAdmin => Mode == AccountMode.Admin;

    public string Title => Mode switch
    {
        AccountMode.PasswordChange => "비밀번호 변경",
        AccountMode.AccountCreate => "계정 생성",
        AccountMode.Admin => "관리자",
        _ => "계정"
    };

    // ── 비밀번호 변경 (PasswordBox는 바인딩 불가 → code-behind 전달) ──
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    [ObservableProperty] private string _accountMessage = string.Empty;
    [ObservableProperty] private bool _accountMessageIsError;

    // ── 계정 생성 (power) ──
    [ObservableProperty] private string _newAccountId = string.Empty;
    public string NewAccountPassword { get; set; } = string.Empty; // code-behind 전달
    /// <summary>신규 계정 이메일(백엔드 모드에서만 노출·전달). 생성 시 인증 코드가 발송된다. (item1a §9.3)</summary>
    [ObservableProperty] private string _newAccountEmail = string.Empty;
    [ObservableProperty] private UserRole _selectedNewRole = UserRole.User;
    [ObservableProperty] private string _adminMessage = string.Empty;
    [ObservableProperty] private bool _adminMessageIsError;

    // ── 이메일 등록/인증 섹션 (PasswordChange 모드 하단, 백엔드 모드 전용, item1a §9.3) ──
    /// <summary>이메일 등록 입력(본인 email 추가/변경).</summary>
    [ObservableProperty] private string _emailInput = string.Empty;
    /// <summary>이메일 인증 코드 입력(6자리).</summary>
    [ObservableProperty] private string _emailVerifyCode = string.Empty;
    [ObservableProperty] private string _emailMessage = string.Empty;
    [ObservableProperty] private bool _emailMessageIsError;

    /// <summary>로그인 계정의 현재 이메일(없으면 null). 진입 시 세션에서 로드.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmail))]
    private string? _currentEmail;

    /// <summary>로그인 계정의 이메일 인증 여부. 진입 시 세션에서 로드.</summary>
    [ObservableProperty]
    private bool _isEmailVerified;

    /// <summary>이메일이 등록돼 있는지(등록/미등록 UI 분기).</summary>
    public bool HasEmail => !string.IsNullOrWhiteSpace(CurrentEmail);

    /// <summary>로그인 역할이 생성 가능한 역할 목록(admin→[User,Manager], manager→[User]).</summary>
    public ObservableCollection<UserRole> CreatableRoles { get; } = new();

    public bool IsLoggedIn => _shell.Session.CurrentUser is not null;
    public bool IsPower => _shell.Session.CurrentUser?.Role.IsPower() == true;

    /// <summary>
    /// 백엔드 모드 여부(item1a §9.3 게이트). 이메일 인증·비밀번호 재설정 인프라는 백엔드 전용이므로
    /// 계정 생성 email 필드·이메일 인증 섹션은 이 값이 true일 때만 노출·활성한다.
    /// </summary>
    public bool IsBackendMode => _shell.Settings.Current.UseBackend;

    public AccountViewModel(AppShellViewModel shell, IAccountService accounts, ILogger<AccountViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsPower));
        OnPropertyChanged(nameof(IsBackendMode));

        // 생성 가능 역할 갱신(로그인 역할 기반)
        CreatableRoles.Clear();
        var user = _shell.Session.CurrentUser;
        var role = user?.Role;
        if (role is { } r)
            foreach (var cr in r.CreatableRoles())
                CreatableRoles.Add(cr);
        SelectedNewRole = CreatableRoles.Count > 0 ? CreatableRoles[0] : UserRole.User;

        // 이메일 인증 섹션 상태 로드(백엔드 모드에서 로그인 시 채워짐).
        CurrentEmail = user?.Email;
        IsEmailVerified = user?.EmailVerified == true;
        EmailInput = user?.Email ?? string.Empty;
        EmailVerifyCode = string.Empty;
        SetEmailMessage(string.Empty, isError: false);

        return Task.CompletedTask;
    }

    // ── 비밀번호 변경 (it2 §4.3, 2회 확인) ──

    [RelayCommand]
    private async Task ChangePassword()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            SetAccountMessage("새 비밀번호를 입력하세요.", isError: true);
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            SetAccountMessage("새 비밀번호가 일치하지 않습니다.", isError: true);
            return;
        }

        try
        {
            await _accounts.ChangePasswordAsync(user.Id, NewPassword);
            user.Password = NewPassword;
            NewPassword = ConfirmPassword = string.Empty;
            SetAccountMessage("비밀번호가 변경되었습니다.", isError: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "비밀번호 변경 실패");
            SetAccountMessage("변경에 실패했습니다.", isError: true);
        }
    }

    // ── 계정 생성 (it2 §4.4·§7, 역할 게이트) ──

    [RelayCommand]
    private async Task CreateAccount()
    {
        var acting = _shell.Session.CurrentUser?.Role;
        if (acting is not { } actingRole || !actingRole.IsPower())
        {
            SetAdminMessage("권한이 없습니다.", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(NewAccountId) || string.IsNullOrWhiteSpace(NewAccountPassword))
        {
            SetAdminMessage("아이디와 비밀번호를 입력하세요.", isError: true);
            return;
        }

        // 백엔드 모드에서만 email 수집(레거시 경로엔 이메일 인프라 없음, item1a §9.3).
        var email = IsBackendMode && !string.IsNullOrWhiteSpace(NewAccountEmail)
            ? NewAccountEmail.Trim()
            : null;

        try
        {
            var createdId = NewAccountId.Trim(); // 비우기 전에 보존(메시지 조립용)
            await _accounts.CreateAsync(createdId, NewAccountPassword, SelectedNewRole, email, actingRole);
            NewAccountId = string.Empty;
            NewAccountPassword = string.Empty;
            NewAccountEmail = string.Empty;
            var suffix = email is not null ? " 인증 코드를 이메일로 발송했습니다." : string.Empty;
            SetAdminMessage($"'{createdId}' 계정을 생성했습니다.{suffix}", isError: false);
        }
        catch (UnauthorizedAccessException)
        {
            SetAdminMessage("해당 역할을 생성할 권한이 없습니다.", isError: true);
        }
        catch (InvalidOperationException ex)
        {
            // 중복 id 또는 미초기화
            SetAdminMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "계정 생성 실패");
            SetAdminMessage("생성에 실패했습니다.", isError: true);
        }
    }

    /// <summary>사용자 관리 화면 진입(power).</summary>
    [RelayCommand]
    private async Task OpenUserManagement()
    {
        if (IsPower)
            await _shell.NavigateAsync(AppState.UserMgmt);
    }

    /// <summary>앱 종료(관리자).</summary>
    [RelayCommand]
    private void ExitApp() => Application.Current.Shutdown();

    // ── 이메일 등록/인증 (PasswordChange 모드 하단 섹션, 백엔드 전용, item1a §9.3) ──

    /// <summary>본인 이메일 등록/변경. 서버가 emailVerified=false 리셋 + 인증 코드 발송.</summary>
    [RelayCommand]
    private async Task RegisterEmail()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }
        var email = EmailInput.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            SetEmailMessage("이메일을 입력하세요.", isError: true);
            return;
        }

        try
        {
            await _accounts.SetEmailAsync(user.Id, email);
            // 로컬 세션 반영(등록 직후는 미인증 상태).
            user.Email = email;
            user.EmailVerified = false;
            CurrentEmail = email;
            IsEmailVerified = false;
            EmailVerifyCode = string.Empty;
            SetEmailMessage("이메일로 인증 코드를 발송했습니다. 코드를 입력해 인증을 완료하세요.", isError: false);
        }
        catch (ArgumentException)
        {
            SetEmailMessage("이메일 형식이 올바르지 않습니다.", isError: true);
        }
        catch (InvalidOperationException ex)
        {
            // 이메일 중복(409) 등.
            SetEmailMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이메일 등록 실패");
            SetEmailMessage("이메일 등록에 실패했습니다.", isError: true);
        }
    }

    /// <summary>인증 코드 확인(6자리). 성공 시 emailVerified=true 반영.</summary>
    [RelayCommand]
    private async Task VerifyEmail()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }
        var code = EmailVerifyCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SetEmailMessage("인증 코드를 입력하세요.", isError: true);
            return;
        }

        try
        {
            var verified = await _accounts.ConfirmEmailVerificationAsync(user.Id, code);
            if (verified)
            {
                user.EmailVerified = true;
                IsEmailVerified = true;
                EmailVerifyCode = string.Empty;
                SetEmailMessage("이메일 인증이 완료되었습니다.", isError: false);
            }
            else
            {
                SetEmailMessage("인증 코드가 올바르지 않거나 만료되었습니다.", isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이메일 인증 실패");
            SetEmailMessage("인증에 실패했습니다.", isError: true);
        }
    }

    /// <summary>인증 메일 재발송(현재 등록된 이메일로).</summary>
    [RelayCommand]
    private async Task ResendEmailVerification()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }

        try
        {
            // 서버는 열거 방지로 항상 성공 응답(no-op 포함). 사용자에겐 동일 안내.
            await _accounts.RequestEmailVerificationAsync(user.Id);
            SetEmailMessage("인증 코드를 다시 발송했습니다.", isError: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "인증 메일 재발송 실패");
            SetEmailMessage("재발송에 실패했습니다.", isError: true);
        }
    }

    /// <summary>[닫기/뒤로]: 오버레이 복귀(직전 화면). 세션 보존.</summary>
    [RelayCommand]
    private async Task Close() => await _shell.ReturnFromOverlay();

    private void SetAccountMessage(string text, bool isError)
    {
        AccountMessage = text;
        AccountMessageIsError = isError;
    }

    private void SetAdminMessage(string text, bool isError)
    {
        AdminMessage = text;
        AdminMessageIsError = isError;
    }

    private void SetEmailMessage(string text, bool isError)
    {
        EmailMessage = text;
        EmailMessageIsError = isError;
    }
}
