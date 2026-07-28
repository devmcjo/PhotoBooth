using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 사용자 관리 목록의 한 행. 계정 + 역할 변경 콤보 상태(지정 가능 역할·선택값)를 캡슐화한다(it13 §9.5).
/// 콤보 옵션은 <see cref="RoleChangePolicy.AssignableRoles"/>(서버 setRole 매트릭스 1:1)로 필터 —
/// 빈 목록이거나 자기 계정이면 역할 변경 UI 미노출(CanChangeRole=false).
/// </summary>
public sealed partial class UserRowViewModel : ObservableObject
{
    /// <summary>원본 계정(삭제·pw초기화·표시용).</summary>
    public User User { get; }

    /// <summary>이 행에서 actor가 지정 가능한 역할 목록(콤보 ItemsSource). 자기 계정이면 빈 목록.</summary>
    public IReadOnlyList<UserRole> AssignableRoles { get; }

    /// <summary>콤보 선택값(초기=현재 역할). Apply 시 현재와 다르면 SetRole.</summary>
    [ObservableProperty] private UserRole _selectedRole;

    /// <summary>역할 변경 UI 노출 여부(콤보 옵션이 있고 자기 계정 아님).</summary>
    public bool CanChangeRole => AssignableRoles.Count > 0;

    /// <summary>
    /// it14: PIN 재설정 UI 노출 여부: 백엔드 모드 + 자기 계정 아님 + actor가 대상을 관리 가능(CanManage).
    /// 자기 PIN은 AccountView에서 변경(서버도 자기 자신 E3는 400). 레거시(비백엔드)엔 PIN 인프라 없음.
    /// </summary>
    public bool CanResetPin { get; }

    public UserRowViewModel(User user, UserRole actorRole, bool isSelf, bool isBackend = false)
    {
        User = user;
        // 자기 계정은 역할 변경 금지(대칭·안전) → 빈 목록으로 UI 미노출.
        AssignableRoles = isSelf ? Array.Empty<UserRole>() : RoleChangePolicy.AssignableRoles(actorRole, user.Role);
        _selectedRole = user.Role;
        CanResetPin = isBackend && !isSelf && actorRole.CanManage(user.Role);
    }
}

/// <summary>
/// 사용자 관리(power 전용). 목록·삭제(cascade)·비밀번호 초기화·역할 변경(콤보+Apply, §8.7 매트릭스). (PRD §F8, it13 §9.5)
/// </summary>
public sealed partial class UserMgmtViewModel : ViewModelBase
{
    private const string ResetPassword = "0000";

    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IPinPromptDialogService? _pinPrompt;
    private readonly ILogger<UserMgmtViewModel>? _logger;

    /// <summary>행 목록(계정 + 역할 변경 상태). it13 §9.5로 User 직접 바인딩 → 행 래퍼로 승격.</summary>
    public ObservableCollection<UserRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isAdmin;
    /// <summary>행위자(로그인 계정) 역할. 관리 액션 노출·가드 기준(자기와 같거나 낮은 역할만 관리).</summary>
    [ObservableProperty] private UserRole _actorRole = UserRole.User;

    // it14: PIN 재설정 UI는 백엔드 모드에서만 노출(레거시엔 SSO·PIN 인프라 없음). 프레임 XAML 노출 게이트.
    public bool IsBackendMode => _shell.Settings.Current.UseBackend;

    public UserMgmtViewModel(AppShellViewModel shell, IAccountService accounts,
        ILogger<UserMgmtViewModel>? logger = null, IPinPromptDialogService? pinPrompt = null)
    {
        _shell = shell;
        _accounts = accounts;
        _pinPrompt = pinPrompt;
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
        Rows.Clear();
        try
        {
            var selfId = _shell.Session.CurrentUser?.Id;
            var isBackend = IsBackendMode;
            foreach (var u in await _accounts.GetAllAsync())
                Rows.Add(new UserRowViewModel(u, ActorRole, isSelf: u.Id == selfId, isBackend: isBackend));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "사용자 목록 조회 실패");
            StatusMessage = "사용자 목록을 불러올 수 없습니다.";
        }
    }

    [RelayCommand]
    private async Task DeleteUser(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
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
    private async Task ResetUserPassword(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
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

    /// <summary>
    /// 타 계정 PIN 재설정(it14 §6.2, 권한 기반). 소형 PIN 다이얼로그로 새 4자리 PIN을 입력(2회 확인) → ResetPinAsync.
    /// CanManage 클라 1차 가드(UI 미노출과 이중 방어) + 서버 canManage 최종 강제(403 우아 처리, 비번 초기화와 동형).
    /// 고정값(비번 "0000") 대신 입력값 사용 — PIN 자격성 유지(설계 O4).
    /// </summary>
    [RelayCommand]
    private void ResetUserPin(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
        // 권한 가드: 자기와 같거나 낮은 역할만(예: manager는 admin PIN 재설정 불가). UI 미노출과 이중 방어.
        if (!ActorRole.CanManage(user.Role)) { StatusMessage = "상위 역할 계정은 관리할 수 없습니다."; return; }
        // fail-closed: PIN 다이얼로그 서비스가 없으면(레거시/DI 미구성) 재설정하지 않는다.
        if (_pinPrompt is null) { StatusMessage = "PIN 재설정을 사용할 수 없습니다."; return; }

        // 소형 다이얼로그: 관리자가 대상의 새 PIN을 2회 입력. setAsync가 ResetPinAsync(대상, newPin) 호출.
        // 다이얼로그 내부 예외(403 등)는 fail-closed로 창 유지·인라인 오류. 성공(true) 시에만 상태 메시지.
        var targetId = user.Id;
        bool done = _pinPrompt.PromptSetup(newPin => _accounts.ResetPinAsync(targetId, newPin));
        if (done)
            StatusMessage = $"{targetId}의 PIN을 재설정했습니다.";
    }

    /// <summary>
    /// 역할 변경 적용(콤보 선택값). §8.7 매트릭스로 클라 1차 게이트(자기·권한밖·admin 대상 차단),
    /// 서버가 최종 강제(403이면 우아 처리 — 안내 + 목록 원복). (it13 §9.5)
    /// </summary>
    [RelayCommand]
    private async Task ApplyRoleChange(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
        var target = row.SelectedRole;

        // 무변경(현재==선택)은 no-op(불필요한 서버 왕복 방지).
        if (target == user.Role) return;
        // 자기 계정 역할 변경 방지(이중 방어 — 행 래퍼가 이미 빈 목록으로 UI 미노출).
        if (user.Id == _shell.Session.CurrentUser?.Id) { StatusMessage = "자기 계정의 역할은 변경할 수 없습니다."; return; }
        // 클라 1차 매트릭스 게이트(서버 setRole과 동일 규칙). 위반이면 서버 왕복 전 차단.
        if (!RoleChangePolicy.AssignableRoles(ActorRole, user.Role).Contains(target))
        {
            StatusMessage = "해당 역할로 변경할 권한이 없습니다.";
            return;
        }
        try
        {
            await _accounts.SetRoleAsync(user.Id, target);
            await ReloadAsync();
            StatusMessage = $"{user.Id}의 역할을 '{target.ToLabel()}'(으)로 변경했습니다.";
        }
        catch (UnauthorizedAccessException)
        {
            // 서버 403(매트릭스 위반) — 우아 처리: 안내 + 목록 원복(선택값 되돌림).
            _logger?.LogWarning("역할 변경 거부(서버 403): {Id}", user.Id);
            StatusMessage = "역할을 변경할 권한이 없습니다.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "역할 변경 실패: {Id}", user.Id);
            StatusMessage = "역할 변경에 실패했습니다.";
            await ReloadAsync(); // 실패 시 목록 원복(선택값이 서버 상태와 어긋나지 않게)
        }
    }

    [RelayCommand]
    private async Task Back() => await _shell.ReturnToAdminToolsAsync(); // 관리자 도구(Account)로 복귀(it5 §5 C2)
}
