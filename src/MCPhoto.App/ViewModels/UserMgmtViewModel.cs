using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Backend;
using MCPhoto.Core.Frames;
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

    /// <summary>
    /// 이 계정이 소유한 개인 프레임 개수. <b>null = 아직 모른다</b>(미조회 또는 조회 실패).
    /// 목록 로드를 막지 않기 위해 뒤늦게 채워지며, 실패해도 null로 남는다 — 실패를 사용자에게 알리지 않는다.
    /// ⚠️ 0(진짜 0개)과 null(모름)은 다른 값이다. 기본값 0으로 두면 조회 전 화면이 "전원 0개"라고 거짓말한다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameCountText))]
    private int? _frameCount;

    /// <summary>개인 프레임 개수 표시값. 모르면 "—"(다른 셀의 미해당 표기와 같은 문자).</summary>
    public string FrameCountText => FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "—";

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

    /// <summary>개인 프레임 개수 조회용. 미주입(null)이면 개수 기능만 조용히 꺼진다(fail-soft).</summary>
    private readonly IFrameRepository? _frames;

    /// <summary>진행 중인 개수 조회의 취소원. Dispose 소유자는 "그 조회 자신"(FrameSelectViewModel 관례).</summary>
    private CancellationTokenSource? _frameCountCts;

    /// <summary>
    /// 연속 실패 상한. HttpClient.Timeout=100초라(ServiceRegistration) 서버가 죽은 채 전 계정을 돌면
    /// 백그라운드 루프가 수십 분 살아 있게 된다. 결과는 어차피 전부 "—"이므로 조기에 포기한다.
    /// 산발적 실패 1~2건은 상한에 닿지 않고 다음 행으로 넘어간다(성공 시 카운터 리셋).
    /// </summary>
    private const int MaxConsecutiveFrameCountFailures = 3;

    /// <summary>
    /// 진행 중(또는 직전) 개수 채우기 작업. <b>테스트·진단용 관측점</b>이며 절대 faulted가 되지 않는다
    /// (본체가 모든 예외를 삼킨다). 목록 로드는 이 작업을 기다리지 않는다 — 기다리면 [사용자 관리] 버튼이
    /// N회 HTTP 동안 잠긴다. 폴링 대기는 플래키하므로 결정적 검증을 위해 핸들로 노출한다.
    /// </summary>
    public Task FrameCountLoadTask { get; private set; } = Task.CompletedTask;

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

    /// <summary>
    /// <paramref name="frames"/>는 <b>마지막 선택 파라미터</b>다 — 기존 위치 인수 호출부를 그대로 두기 위함이며,
    /// 미등록/미주입이면 개수 열만 "—"로 남고 화면은 완전히 동작한다(fail-soft).
    /// </summary>
    public UserMgmtViewModel(AppShellViewModel shell, IAccountService accounts,
        ILogger<UserMgmtViewModel>? logger = null, IPinPromptDialogService? pinPrompt = null,
        IFrameRepository? frames = null)
    {
        _shell = shell;
        _accounts = accounts;
        _pinPrompt = pinPrompt;
        _logger = logger;
        _frames = frames;
    }

    public override async Task OnEnterAsync()
    {
        ActorRole = _shell.Session.CurrentUser?.Role ?? UserRole.User;
        IsAdmin = ActorRole == UserRole.Admin;
        await ReloadAsync();
    }

    /// <summary>화면 이탈 시 진행 중 개수 조회 취소 — 뒤늦은 완료가 폐기된 VM 상태를 건드리지 않게 한다.</summary>
    public override Task OnLeaveAsync()
    {
        CancelFrameCounts();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 신호만 보낸다. Dispose는 조회 본체의 finally가 수행(이중 해제 불가) —
    /// 취소자가 Dispose하면 진행 중 본체의 Cancel/Token 접근이 ObjectDisposedException으로 터진다.
    /// </summary>
    private void CancelFrameCounts()
    {
        var cts = _frameCountCts;
        _frameCountCts = null;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* 이미 완료·해제된 조회 — 무해 */ }
    }

    private async Task ReloadAsync()
    {
        CancelFrameCounts();   // 이전 개수 조회 취소(새로고침·삭제·역할변경 재로드 모두 이 경로를 지난다)
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
            // 원인을 결합한다(it23 §B7.3): 종전 문구는 "불러올 수 없습니다."로 끝나 **원인을 말하지 않았다** —
            // 오프라인·서버 주소 미설정·토큰 만료·토큰 부재를 구분하지 못해 관리자가 여기서 막힌다.
            // ⚠️ 테스트 모드 전용 분기를 넣지 않는다. 원인 기반 문구 하나가 두 상황(실운영 무토큰·테스트 모드)을
            //    모두 정확히 설명하며, 화면에 테스트 모드 조건 분기를 심으면 그것이 프로덕션 문구로 번진다.
            SetStatus("사용자 목록을 불러올 수 없습니다. " + BackendFailureMessage.Describe(ex), isError: true);
        }
        UpdateSummary();
        StartFrameCountLoad();   // 행이 다 채워진 뒤에 개수 조회를 띄운다(await하지 않는다 — 목록을 막지 않는다)
    }

    /// <summary>
    /// 행이 채워진 뒤 개인 프레임 개수를 순차로 채운다(fire-and-forget). 목록 로드를 막지 않는 것이 요점이다.
    /// 저장소 미주입(_frames=null)이면 전 행이 "—"로 남고 화면은 정상 동작한다(fail-soft).
    /// </summary>
    private void StartFrameCountLoad()
    {
        if (_frames is null || Rows.Count == 0) return;
        var cts = new CancellationTokenSource();
        _frameCountCts = cts;
        // 스냅샷: 루프 도중 Rows가 교체돼도 컬렉션을 순회하지 않는다(InvalidOperationException 방지).
        FrameCountLoadTask = LoadFrameCountsAsync(Rows.ToArray(), cts);
    }

    /// <summary>
    /// 계정별 개인 프레임 개수 조회. <b>순차</b>(동시 발사 금지 — 계정 수만큼 요청이 나간다),
    /// <b>취소 가능</b>(화면 이탈·새로고침), <b>실패는 조용히</b>(행은 "—" 유지, Warning 로그만).
    /// 어떤 경로로도 예외를 던지지 않는다 — 호출자가 await하지 않으므로 던지면 관측되지 않는 예외가 된다.
    /// </summary>
    private async Task LoadFrameCountsAsync(IReadOnlyList<UserRowViewModel> rows, CancellationTokenSource cts)
    {
        int consecutiveFailures = 0;
        try
        {
            foreach (var row in rows)
            {
                // 이 조회가 아직 "현재" 조회인지 매 회 확인 — 새 로드가 시작됐으면 즉시 손을 뗀다.
                if (!ReferenceEquals(cts, _frameCountCts) || cts.IsCancellationRequested) return;

                try
                {
                    // ⚠️ ConfigureAwait(false)를 쓰지 않는다 — UI 스레드로 돌아와야 아래 대입(PropertyChanged)이
                    //    UI 스레드에서 일어난다. 의도를 남기려고 명시적으로 true를 붙인다.
                    var frames = await _frames!.GetUserFramesAsync(row.User.Id, cts.Token).ConfigureAwait(true);
                    if (!ReferenceEquals(cts, _frameCountCts)) return;   // stale 결과가 새 목록을 덮지 않게
                    row.FrameCount = frames.Count;
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException)
                {
                    return;   // 이탈·새로고침에 의한 정상 종료. 로그도 남기지 않는다.
                }
                catch (Exception ex)
                {
                    // 관리 화면 전체가 프레임 조회 실패로 막히면 안 된다.
                    // 사용자에게는 아무것도 알리지 않는다(StatusMessage 불변) — 행은 "—"로 남는다.
                    consecutiveFailures++;
                    _logger?.LogWarning(ex, "개인 프레임 개수 조회 실패: {Id}", row.User.Id);

                    if (IsHopelessForRemaining(ex) || consecutiveFailures >= MaxConsecutiveFrameCountFailures)
                    {
                        _logger?.LogWarning("개인 프레임 개수 조회 중단 — 남은 계정은 '—'로 둔다(연속 실패 {Count}회)",
                            consecutiveFailures);
                        return;
                    }
                }
            }
        }
        finally
        {
            if (ReferenceEquals(cts, _frameCountCts)) _frameCountCts = null;
            cts.Dispose();
        }
    }

    /// <summary>남은 계정도 같은 이유로 반드시 실패하는 예외인가(주소 미설정·인증 없음/만료).</summary>
    private static bool IsHopelessForRemaining(Exception ex)
        => ex is BackendNotConfiguredException || ex is BackendLoginRequiredException;

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
