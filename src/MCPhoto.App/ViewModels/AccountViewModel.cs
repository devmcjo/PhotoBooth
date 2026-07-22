using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
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
    [ObservableProperty] private UserRole _selectedNewRole = UserRole.User;
    [ObservableProperty] private string _adminMessage = string.Empty;
    [ObservableProperty] private bool _adminMessageIsError;

    /// <summary>로그인 역할이 생성 가능한 역할 목록(admin→[User,Manager], manager→[User]).</summary>
    public ObservableCollection<UserRole> CreatableRoles { get; } = new();

    public bool IsLoggedIn => _shell.Session.CurrentUser is not null;
    public bool IsPower => _shell.Session.CurrentUser?.Role.IsPower() == true;

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

        // 생성 가능 역할 갱신(로그인 역할 기반)
        CreatableRoles.Clear();
        var role = _shell.Session.CurrentUser?.Role;
        if (role is { } r)
            foreach (var cr in r.CreatableRoles())
                CreatableRoles.Add(cr);
        SelectedNewRole = CreatableRoles.Count > 0 ? CreatableRoles[0] : UserRole.User;

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

        try
        {
            var createdId = NewAccountId.Trim(); // 비우기 전에 보존(메시지 조립용)
            await _accounts.CreateAsync(createdId, NewAccountPassword, SelectedNewRole, actingRole);
            NewAccountId = string.Empty;
            NewAccountPassword = string.Empty;
            SetAdminMessage($"'{createdId}' 계정을 생성했습니다.", isError: false);
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
}
