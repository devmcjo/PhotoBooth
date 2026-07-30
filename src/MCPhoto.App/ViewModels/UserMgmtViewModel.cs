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
    /// <summary>원본 계정(삭제·PIN 재설정·표시용).</summary>
    public User User { get; }

    /// <summary>이 행에서 actor가 지정 가능한 역할 목록(콤보 ItemsSource). 자기 계정이면 빈 목록.</summary>
    public IReadOnlyList<UserRole> AssignableRoles { get; }

    /// <summary>콤보 선택값(초기=현재 역할). Apply 시 현재와 다르면 SetRole.</summary>
    [ObservableProperty] private UserRole _selectedRole;

    /// <summary>역할 변경 UI 노출 여부(콤보 옵션이 있고 자기 계정 아님).</summary>
    public bool CanChangeRole => AssignableRoles.Count > 0;

    /// <summary>
    /// it14: PIN 재설정 UI 노출 여부: 자기 계정 아님 + actor가 대상의 PIN을 재설정 가능.
    /// 자기 PIN은 계정 관리 화면에서 변경(서버도 자기 자신 E3는 400).
    /// it15: 백엔드 전용이 되어 isBackend 조건 삭제.
    /// it16 §3.5: **power 항 추가** — 서버가 `PUT /accounts/:id/pin`에 requirePower()를 붙였으므로 클라도 대칭.
    ///   CanManage만으로는 비power(temp_user·user·advanced_user)가 같은 위계의 남의 PIN을 만질 수 있었다.
    /// 판정은 <see cref="UserRoleExtensions.CanResetPin"/>(power + **엄격히 낮은 위계**)로 위임한다 —
    ///   동급 차단이므로 매니저는 다른 매니저의 PIN을 재설정할 수 없다(관리자 전용).
    /// </summary>
    public bool CanResetPin { get; }

    /// <summary>
    /// it15 §6.5: PIN 설정 여부 표시("설정됨"/"미설정"). PIN이 유일한 진입 자격증명이 되어 관리자 가시성이 필요하다.
    /// </summary>
    public string PinStateLabel => User.HasPin ? "설정됨" : "미설정";

    /// <summary>현재 역할 한글 배지 텍스트(목록 표시용).</summary>
    public string RoleLabel => User.Role.ToLabel();

    /// <summary>로그인 중인 계정 자신의 행인지(목록에서 "나" 배지로 표시 — 관리 오조작 방지).</summary>
    public bool IsSelf { get; }

    /// <summary>가입 날짜(로컬 시간, yyyy-MM-dd). 서버 createdAt은 UTC라 표시 시 로컬로 변환한다.</summary>
    public string CreatedDateText => User.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd");

    /// <summary>가입 시각(로컬 시간, HH:mm). 같은 날 가입 계정의 선후 판별용(정렬 기준과 동일한 값).</summary>
    public string CreatedTimeText => User.CreatedAt.ToLocalTime().ToString("HH:mm");

    /// <summary>이메일(Google SSO 신원). 없으면 빈 문자열 — 목록 보조 줄에 표시.</summary>
    public string EmailText => User.Email ?? string.Empty;

    public UserRowViewModel(User user, UserRole actorRole, bool isSelf)
    {
        User = user;
        IsSelf = isSelf;
        // 자기 계정은 역할 변경 금지(대칭·안전) → 빈 목록으로 UI 미노출.
        AssignableRoles = isSelf ? Array.Empty<UserRole>() : RoleChangePolicy.AssignableRoles(actorRole, user.Role);
        _selectedRole = user.Role;
        CanResetPin = !isSelf && actorRole.CanResetPin(user.Role);   // power + 엄격히 낮은 위계(동급 차단)
    }
}

/// <summary>
/// 사용자 관리(power 전용). 목록·삭제(cascade)·PIN 재설정·역할 변경(콤보+Apply, §8.7 매트릭스).
/// it15: "PW 초기화"는 비밀번호 개념 폐지로 삭제. (PRD §F8, it13 §9.5, it15 §6.5)
/// 목록 정렬은 관리 편의를 위해 **역할 위계 내림차순 → 같은 역할은 가입 시각 오름차순**이다
/// (높은 역할이 위, 최근 가입일수록 아래). 서버는 정렬을 보장하지 않으므로 클라에서 확정한다.
/// </summary>
public sealed partial class UserMgmtViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IPinPromptDialogService? _pinPrompt;
    private readonly ILogger<UserMgmtViewModel>? _logger;

    /// <summary>행 목록(계정 + 역할 변경 상태). it13 §9.5로 User 직접 바인딩 → 행 래퍼로 승격.</summary>
    public ObservableCollection<UserRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    /// <summary>상태 메시지가 오류인지(true=Danger, false=Success 색). 실패 안내가 초록으로 보이던 문제 교정.</summary>
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isAdmin;
    /// <summary>행위자(로그인 계정) 역할. 관리 액션 노출·가드 기준(자기와 같거나 낮은 역할만 관리).</summary>
    [ObservableProperty] private UserRole _actorRole = UserRole.User;

    /// <summary>역할별 인원 요약("총 12명 · 관리자 1 · 매니저 2 …"). 목록 상단 부제.</summary>
    [ObservableProperty] private string _summaryText = string.Empty;

    /// <summary>목록이 비었는지(빈 상태 안내 노출 조건).</summary>
    [ObservableProperty] private bool _isEmpty;

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
            // 관리용 정렬: ① 역할 위계 내림차순(높은 역할이 위) ② 같은 역할은 가입 시각 오름차순
            // (오래된 계정이 위 = 최근 가입일수록 아래) ③ 동시각 타이브레이크는 아이디(표시 순서 안정화).
            // ⚠️ enum 서수가 아니라 HierarchyRank로 정렬 — 역할이 추가돼도 위계 한 곳만 갱신되면 따라온다.
            var ordered = (await _accounts.GetAllAsync())
                .OrderByDescending(u => u.Role.HierarchyRank())
                .ThenBy(u => u.CreatedAt)
                .ThenBy(u => u.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var u in ordered)
                Rows.Add(new UserRowViewModel(u, ActorRole, isSelf: u.Id == selfId));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "사용자 목록 조회 실패");
            SetStatus("사용자 목록을 불러올 수 없습니다.", isError: true);
        }
        UpdateSummary();
    }

    /// <summary>역할별 인원 요약 갱신(위계 높은 역할부터, 0명 역할은 생략).</summary>
    private void UpdateSummary()
    {
        IsEmpty = Rows.Count == 0;
        if (IsEmpty) { SummaryText = string.Empty; return; }
        var byRole = Rows
            .GroupBy(r => r.User.Role)
            .OrderByDescending(g => g.Key.HierarchyRank())
            .Select(g => $"{g.Key.ToLabel()} {g.Count()}");
        SummaryText = $"총 {Rows.Count}명 · {string.Join(" · ", byRole)}";
    }

    /// <summary>상태 메시지 + 성공/오류 색을 함께 설정(둘이 어긋나지 않게 한 곳에서).</summary>
    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    /// <summary>목록 새로고침(역할 변경·삭제가 다른 단말에서 일어난 경우 등 서버 상태 재확인).</summary>
    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync();
        if (!StatusIsError) SetStatus("목록을 새로 불러왔습니다.");
    }

    [RelayCommand]
    private async Task DeleteUser(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
        // 자기 자신·시드 admin 삭제 방지
        if (user.Id == _shell.Session.CurrentUser?.Id) { SetStatus("자기 계정은 삭제할 수 없습니다.", isError: true); return; }
        // 권한 가드: 자기와 같거나 낮은 역할만 관리(예: manager는 admin 삭제 불가). UI 미노출과 이중 방어.
        if (!ActorRole.CanManage(user.Role)) { SetStatus("상위 역할 계정은 관리할 수 없습니다.", isError: true); return; }
        try
        {
            await _accounts.DeleteAsync(user.Id); // cascade(프레임 문서+Storage)
            await ReloadAsync();
            SetStatus($"{user.Id} 삭제됨(소유 프레임 포함).");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "사용자 삭제 실패: {Id}", user.Id);
            SetStatus("삭제에 실패했습니다.", isError: true);
        }
    }

    /// <summary>
    /// 타 계정 PIN 재설정(it14 §6.2, 권한 기반). 소형 PIN 다이얼로그로 새 4자리 PIN을 입력(2회 확인) → ResetPinAsync.
    /// CanResetPin 클라 1차 가드(UI 미노출과 이중 방어) + 서버 requirePower + canResetPin 최종 강제(403 우아 처리).
    /// 고정값(비번 "0000") 대신 입력값 사용 — PIN 자격성 유지(설계 O4).
    /// </summary>
    [RelayCommand]
    private void ResetUserPin(UserRowViewModel? row)
    {
        if (row is null) return;
        var user = row.User;
        // 권한 가드: power + 자기보다 **낮은** 역할만(동급 차단 — 매니저 PIN은 관리자 전용).
        // UI 미노출과 이중 방어. 서버 canResetPin과 동일 판정(UserRoleExtensions.CanResetPin).
        if (!ActorRole.CanResetPin(user.Role))
        {
            SetStatus("동급·상위 역할 계정의 PIN은 재설정할 수 없습니다.", isError: true);
            return;
        }
        // fail-closed: PIN 다이얼로그 서비스가 없으면(레거시/DI 미구성) 재설정하지 않는다.
        if (_pinPrompt is null) { SetStatus("PIN 재설정을 사용할 수 없습니다.", isError: true); return; }

        // 소형 다이얼로그: 관리자가 대상의 새 PIN을 2회 입력. setAsync가 ResetPinAsync(대상, newPin) 호출.
        // 다이얼로그 내부 예외(403 등)는 fail-closed로 창 유지·인라인 오류. 성공(true) 시에만 상태 메시지.
        var targetId = user.Id;
        bool done = _pinPrompt.PromptSetup(newPin => _accounts.ResetPinAsync(targetId, newPin));
        if (done)
            SetStatus($"{targetId}의 PIN을 재설정했습니다.");
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
        if (user.Id == _shell.Session.CurrentUser?.Id) { SetStatus("자기 계정의 역할은 변경할 수 없습니다.", isError: true); return; }
        // 클라 1차 매트릭스 게이트(서버 setRole과 동일 규칙). 위반이면 서버 왕복 전 차단.
        if (!RoleChangePolicy.AssignableRoles(ActorRole, user.Role).Contains(target))
        {
            SetStatus("해당 역할로 변경할 권한이 없습니다.", isError: true);
            return;
        }
        try
        {
            await _accounts.SetRoleAsync(user.Id, target);
            await ReloadAsync();
            SetStatus($"{user.Id}의 역할을 '{target.ToLabel()}'(으)로 변경했습니다.");
        }
        catch (UnauthorizedAccessException)
        {
            // 서버 403(매트릭스 위반) — 우아 처리: 안내 + 목록 원복(선택값 되돌림).
            _logger?.LogWarning("역할 변경 거부(서버 403): {Id}", user.Id);
            await ReloadAsync();
            SetStatus("역할을 변경할 권한이 없습니다.", isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "역할 변경 실패: {Id}", user.Id);
            await ReloadAsync(); // 실패 시 목록 원복(선택값이 서버 상태와 어긋나지 않게)
            SetStatus("역할 변경에 실패했습니다.", isError: true);
        }
    }

    [RelayCommand]
    private async Task Back() => await _shell.ReturnToAdminToolsAsync(); // 관리자 도구(Account)로 복귀(it5 §5 C2)
}
