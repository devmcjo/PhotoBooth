using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 사용자 관리(power 전용). 목록·삭제(cascade)·비밀번호 초기화·역할 지정(admin만 manager 지정). (PRD §F8)
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

    public UserMgmtViewModel(AppShellViewModel shell, IAccountService accounts, ILogger<UserMgmtViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        IsAdmin = _shell.Session.CurrentUser?.Role == UserRole.Admin;
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

    /// <summary>manager 지정(admin만).</summary>
    [RelayCommand]
    private async Task PromoteToManager(User? user)
    {
        if (user is null || !IsAdmin) return;
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

    [RelayCommand]
    private async Task Back() => await _shell.ReturnToAdminToolsAsync(); // 관리자 도구(Account)로 복귀(it5 §5 C2)
}
