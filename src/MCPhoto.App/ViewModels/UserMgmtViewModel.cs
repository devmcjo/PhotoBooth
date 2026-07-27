using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 사용자 관리(power 전용). 목록·삭제(cascade)·비밀번호 초기화·역할 변경(admin만 manager↔user 양방향). (PRD §F8, W-2)
/// </summary>
public sealed partial class UserMgmtViewModel : ViewModelBase
{
    private const string ResetPassword = "0000";

    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ILogger<UserMgmtViewModel>? _logger;

    public ObservableCollection<User> Users { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isAdmin;
    /// <summary>행위자(로그인 계정) 역할. 관리 액션 노출·가드 기준(자기와 같거나 낮은 역할만 관리).</summary>
    [ObservableProperty] private UserRole _actorRole = UserRole.User;

    public UserMgmtViewModel(AppShellViewModel shell, IAccountService accounts, ILogger<UserMgmtViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        ActorRole = _shell.Session.CurrentUser?.Role ?? UserRole.User;
        IsAdmin = ActorRole == UserRole.Admin;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        Users.Clear();
        try
        {
            foreach (var u in await _accounts.GetAllAsync())
                Users.Add(u);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "사용자 목록 조회 실패");
            StatusMessage = "사용자 목록을 불러올 수 없습니다.";
        }
    }

    [RelayCommand]
    private async Task DeleteUser(User? user)
    {
        if (user is null) return;
        // 자기 자신·시드 admin 삭제 방지
        if (user.Id == _shell.Session.CurrentUser?.Id) { StatusMessage = "자기 계정은 삭제할 수 없습니다."; return; }
        // 권한 가드: 자기와 같거나 낮은 역할만 관리(예: manager는 admin 삭제 불가). UI 미노출과 이중 방어.
        if (!ActorRole.CanManage(user.Role)) { StatusMessage = "상위 역할 계정은 관리할 수 없습니다."; return; }
        try
        {
            await _accounts.DeleteAsync(user.Id); // cascade(프레임 문서+Storage)
            await ReloadAsync();
            StatusMessage = $"{user.Id} 삭제됨(소유 프레임 포함).";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "사용자 삭제 실패: {Id}", user.Id);
            StatusMessage = "삭제에 실패했습니다.";
        }
    }

    [RelayCommand]
    private async Task ResetUserPassword(User? user)
    {
        if (user is null) return;
        // 권한 가드: 자기와 같거나 낮은 역할만(예: manager는 admin 비번 초기화 불가). UI 미노출과 이중 방어.
        if (!ActorRole.CanManage(user.Role)) { StatusMessage = "상위 역할 계정은 관리할 수 없습니다."; return; }
        try
        {
            await _accounts.ChangePasswordAsync(user.Id, ResetPassword);
            StatusMessage = $"{user.Id} 비밀번호를 '{ResetPassword}'로 초기화했습니다.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "pw 초기화 실패: {Id}", user.Id);
            StatusMessage = "초기화에 실패했습니다.";
        }
    }

    /// <summary>manager 지정(admin만, 대상은 user). 승격 액션이라 user 외 대상엔 미적용.</summary>
    [RelayCommand]
    private async Task PromoteToManager(User? user)
    {
        if (user is null || !IsAdmin || user.Role != UserRole.User) return;
        // 자기 자신 역할 변경 방지(대칭·안전). admin이 자기를 승격할 일은 없으나 이중 방어.
        if (user.Id == _shell.Session.CurrentUser?.Id) { StatusMessage = "자기 계정의 역할은 변경할 수 없습니다."; return; }
        try
        {
            await _accounts.SetRoleAsync(user.Id, UserRole.Manager);
            await ReloadAsync();
            StatusMessage = $"{user.Id}를 manager로 지정했습니다.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "역할 변경 실패: {Id}", user.Id);
            StatusMessage = "역할 변경에 실패했습니다.";
        }
    }

    /// <summary>user로 강등(admin만, 대상은 manager). 강등 액션이라 manager 외 대상엔 미적용. (W-2)</summary>
    [RelayCommand]
    private async Task DemoteToUser(User? user)
    {
        if (user is null || !IsAdmin || user.Role != UserRole.Manager) return;
        // 자기 자신 역할 변경 방지(승격과 대칭).
        if (user.Id == _shell.Session.CurrentUser?.Id) { StatusMessage = "자기 계정의 역할은 변경할 수 없습니다."; return; }
        try
        {
            await _accounts.SetRoleAsync(user.Id, UserRole.User);
            await ReloadAsync();
            StatusMessage = $"{user.Id}를 user로 강등했습니다.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "역할 변경 실패: {Id}", user.Id);
            StatusMessage = "역할 변경에 실패했습니다.";
        }
    }

    [RelayCommand]
    private async Task Back() => await _shell.ReturnToAdminToolsAsync(); // 관리자 도구(Account)로 복귀(it5 §5 C2)
}
