using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>프레임 선택. 게스트=기본만, 로그인=기본+커스텀. 촬영 전 선택·이후 고정. (PRD §F2, §9 #28)</summary>
public sealed partial class FrameSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly FrameCatalogService _catalog;
    private readonly ILocalFrameStore _localStore;
    private readonly IFrameRepository _repository;
    private readonly ILogger<FrameSelectViewModel>? _logger;

    public ObservableCollection<FrameTemplate> Frames { get; } = new();

    [ObservableProperty] private FrameTemplate? _selectedFrame;
    [ObservableProperty] private bool _isLoggedIn;

    // ── it20: 목록 로딩 국면(대기 UI의 단일 진실 원천) ──
    /// <summary>
    /// 로딩 국면. 종전 <c>bool IsLoading</c> 필드를 대체한다 — 둘을 병존시키면 "로딩 중인데 Ready" 같은
    /// 모순 상태가 만들어진다. 초기값 Loading은 진입 직후 빈 목록이 깜빡이는 것을 막는다(설계 §5.3).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailed))]
    [NotifyPropertyChangedFor(nameof(IsDegraded))]
    [NotifyPropertyChangedFor(nameof(IsInteractive))]
    private FrameLoadPhase _phase = FrameLoadPhase.Loading;

    /// <summary>대기 오버레이 노출 조건.</summary>
    public bool IsLoading => Phase == FrameLoadPhase.Loading;

    /// <summary>전면 실패 카드 노출 조건(쓸 수 있는 프레임이 0개).</summary>
    public bool IsLoadFailed => Phase == FrameLoadPhase.Failed;

    /// <summary>축소 진행 인라인 안내 노출 조건(로컬 프레임만으로 진행 중).</summary>
    public bool IsDegraded => Phase == FrameLoadPhase.Degraded;

    /// <summary>목록·버튼 조작을 허용하는 국면인지. 커맨드 가드의 단일 기준(설계 §5.4).</summary>
    public bool IsInteractive => Phase is FrameLoadPhase.Ready or FrameLoadPhase.Degraded;

    /// <summary>오버레이의 진행 문구. <c>FrameCatalogProgress.ToLabel()</c> 결과를 그대로 담는다.</summary>
    [ObservableProperty] private string _loadingMessage = FrameCatalogProgress.StartLabel;

    /// <summary>로딩 결과 안내(Degraded·Failed에서만 비어 있지 않다). <c>FrameLoadPolicy.NoticeFor</c> 결과.</summary>
    [ObservableProperty] private string _loadNotice = string.Empty;
    [ObservableProperty] private bool _canEditSelected;  // 선택 프레임 편집 가능(역할·프레임 종류에 따라)

    /// <summary>
    /// 프레임 만들기 버튼 노출 여부. it16 E4: **프레임 쓰기 권한**(AdvancedUser 이상)이 있는 로그인 계정만.
    /// user·temp_user는 목록·촬영은 그대로 쓰지만 생성은 불가하다.
    /// </summary>
    [ObservableProperty] private bool _canCreateFrame;

    // A3 삭제 UI 상태
    /// <summary>삭제 ✕ 노출의 1차 입력. it16 E4: 로그인 여부 → **프레임 쓰기 권한**(AdvancedUser 이상)으로 강화.</summary>
    [ObservableProperty] private bool _canDeleteFrames;
    [ObservableProperty] private bool _isPower;
    [ObservableProperty] private bool _isDeleteConfirmVisible;
    [ObservableProperty] private FrameTemplate? _frameToDelete;
    [ObservableProperty] private bool _deleteAlsoServer;  // 파워만 노출·유효

    // 삭제 결과 안내(서버 삭제 성공/실패/미발견). 성공 오인 방지.
    [ObservableProperty] private string _deleteNotice = string.Empty;
    [ObservableProperty] private bool _deleteNoticeIsError;

    /// <param name="loadDeadline">
    /// it20 테스트 이음새: 진행 경과 → 다음 취소 예약까지 남길 시간. 기본값은
    /// <see cref="FrameLoadPolicy.NextDeadline"/>(무진행 30초 / 총 60초 2단 상한).
    /// MS.DI는 기본값 있는 미등록 파라미터를 허용하므로 DI 등록은 그대로다(FrameCatalogService와 같은 형태).
    /// </param>
    public FrameSelectViewModel(AppShellViewModel shell, FrameCatalogService catalog,
        ILocalFrameStore localStore, IFrameRepository repository,
        ILogger<FrameSelectViewModel>? logger = null,
        Func<TimeSpan, TimeSpan>? loadDeadline = null)
    {
        _shell = shell;
        _catalog = catalog;
        _localStore = localStore;
        _repository = repository;
        _logger = logger;
        _loadDeadline = loadDeadline ?? FrameLoadPolicy.NextDeadline;
    }

    private readonly Func<TimeSpan, TimeSpan> _loadDeadline;

    /// <summary>
    /// 이 프레임이 삭제 가능한지. 번들(설치 자산)·fallback은 불가, 그 외 로컬 저장분(user·파워 생성/캐시)은 가능. (it8 §4 A3 정정)
    /// user=local: 접두(로컬 전용), 파워 생성/캐시=실 DB id(접두 없음) — 둘 다 삭제 가능.
    /// </summary>
    public static bool IsDeletable(FrameTemplate frame)
        => !frame.Id.StartsWith("bundle:", StringComparison.Ordinal)
           && !frame.Id.StartsWith("fallback", StringComparison.Ordinal)
           && !string.IsNullOrEmpty(frame.Id);

    /// <summary>목록 재로드 계기. 대기 오버레이·안내 문구 정책이 달라진다. (it20 §6.5)</summary>
    private enum ReloadReason
    {
        /// <summary>화면 진입·[다시 시도]: 오버레이 노출, 중단 시 Degraded 안내.</summary>
        Enter,
        /// <summary>삭제 후 재스캔: 목록이 이미 보이므로 오버레이·안내 없이 조용히 갱신.</summary>
        Refresh
    }

    private CancellationTokenSource? _loadCts;   // 진행 중 로딩의 취소원. Dispose 소유자는 "그 로딩 자신".

    public override Task OnEnterAsync() => ReloadFramesAsync(ReloadReason.Enter);

    /// <summary>화면 이탈 시 진행 중 로딩 취소 — 뒤늦은 완료가 폐기된 VM 상태를 건드리지 않게 한다.</summary>
    public override Task OnLeaveAsync()
    {
        CancelLoad();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 신호만 보낸다. Dispose는 로딩 본체의 finally가 수행(이중 해제 불가).
    /// 취소자가 Dispose하면 진행 중 본체의 finally가 다시 Dispose하거나 Cancel이 예외를 던진다.
    /// </summary>
    private void CancelLoad()
    {
        var cts = _loadCts;
        _loadCts = null;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* 이미 완료·해제된 로딩 — 무해 */ }
    }

    /// <summary>
    /// 디스크·DB 기준으로 프레임 목록을 재로드. 삭제 후에도 호출해 UI를 실제 파일 상태와 일치시킨다. (보완#3)
    /// it20: 무진행·총 상한으로 대기를 유계화하고, <c>finally</c>가 국면을 **무조건** 확정한다
    /// — try 안에서 무엇이 터져도 Loading에 고착되지 않는다(설계 §0.4·§6.6).
    /// </summary>
    private async Task ReloadFramesAsync(ReloadReason reason)
    {
        CancelLoad();                                    // 이전 로딩(재시도 연타 등) 정리
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool quiet = reason == ReloadReason.Refresh;
        bool interrupted = false;
        bool completed = false;

        if (!quiet)
        {
            Phase = FrameLoadPhase.Loading;
            LoadingMessage = FrameCatalogProgress.StartLabel;
            LoadNotice = string.Empty;
        }

        try
        {
            ArmDeadline(cts, clock);

            // UI 스레드에서 생성 → 콜백이 UI 스레드로 마샬링된다(QrPopupViewModel.cs:88-91 관례).
            var progress = new Progress<FrameCatalogProgress>(p =>
            {
                if (!ReferenceEquals(cts, _loadCts)) return;   // stale 보고 차단(늦은 보고가 새 로딩 문구를 덮지 않게)
                LoadingMessage = p.ToLabel();
                ArmDeadline(cts, clock);                       // 진행이 관측됐으니 무진행 타이머 재무장
            });

            var user = _shell.Session.CurrentUser;
            IsLoggedIn = user is not null;
            // it16 E4: 생성·삭제 UI는 프레임 쓰기 권한(AdvancedUser 이상)에 걸린다. 게스트·user·temp_user 미노출.
            // 목록 로딩(아래)은 건드리지 않는다 — 권한을 잃은 계정의 기존 프레임도 그대로 보이고 촬영에 쓸 수 있다.
            CanCreateFrame = user?.Role.CanWriteFrames() == true;
            CanDeleteFrames = user?.Role.CanWriteFrames() == true;
            IsPower = user?.Role.IsPower() == true;

            IReadOnlyList<FrameTemplate> defaults;
            try
            {
                defaults = await _catalog.GetDefaultFramesAsync(cts.Token, quiet ? null : progress);
            }
            catch (OperationCanceledException)
            {
                if (!ReferenceEquals(cts, _loadCts)) return;   // 화면 이탈 취소 → finally가 아무것도 건드리지 않는다
                interrupted = true;
                _logger?.LogWarning(
                    "기본 프레임 대기 중단(무진행 {NoProgress}초/총 {Total}초 상한 또는 사용자 건너뛰기) — 로컬 전용 폴백",
                    FrameLoadPolicy.NoProgressTimeoutSeconds, FrameLoadPolicy.MaxTotalWaitSeconds);
                defaults = await SafeLocalFramesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "기본 프레임 로딩 실패 — 로컬 전용 폴백");
                interrupted = true;
                defaults = await SafeLocalFramesAsync();
            }

            if (!ReferenceEquals(cts, _loadCts)) return;

            // 목록을 **미리 비우지 않는다**. 별도 리스트에 모아 마지막에 한 번 교체한다 —
            // 선행 Clear()는 ① quiet 재스캔(오버레이 없음·Phase는 Ready 유지)에서 "빈 목록 + 조작 열림"
            // 상태를 상한만큼 노출하고(설계 §0.2가 없애려는 그 화면) ② Enter 경로에서도 목록을 깜빡이게 한다.
            var resolved = new List<FrameTemplate>(defaults);

            if (user is not null)
            {
                // 개인 프레임 로드 실패가 공용 목록까지 무너뜨리지 않게 개별 방어(로컬 파일 스캔).
                try
                {
                    resolved.AddRange(await _catalog.GetUserFramesAsync(user.Id, CancellationToken.None));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "개인 프레임 로드 실패(공용 목록은 유지)");
                }
            }

            if (!ReferenceEquals(cts, _loadCts)) return;

            Frames.Clear();
            foreach (var f in resolved)
                Frames.Add(f);

            SelectedFrame = Frames.FirstOrDefault();
            completed = true;
        }
        finally
        {
            // 어떤 예외·어떤 경로에서도 Loading에 고착되지 않는다. try 안에서 무엇이 터져도 여기는 실행된다.
            if (ReferenceEquals(cts, _loadCts))
            {
                Phase = FrameLoadPolicy.Finalize(Phase, Frames.Count, interrupted || !completed, quiet);
                LoadNotice = FrameLoadPolicy.NoticeFor(Phase);
                _loadCts = null;
            }
            clock.Stop();
            cts.Dispose();                                    // 자기 것만 해제 — 항상 1회
        }
    }

    /// <summary>
    /// 무진행·총 상한 중 먼저 오는 시점으로 취소 예약을 재무장한다. 0 이하면 즉시 취소(총 상한 도달).
    /// </summary>
    private void ArmDeadline(CancellationTokenSource cts, System.Diagnostics.Stopwatch clock)
    {
        try
        {
            var due = _loadDeadline(clock.Elapsed);
            if (due <= TimeSpan.Zero) { cts.Cancel(); return; }
            // CancelAfter는 상한(약 49.7일)을 넘는 값에 ArgumentOutOfRangeException을 던진다.
            // 주입된 이음새가 TimeSpan.MaxValue 같은 값을 돌려줘도 로딩이 깨지지 않게 총 상한으로 클램프한다.
            if (due > FrameLoadPolicy.MaxTotalWait) due = FrameLoadPolicy.MaxTotalWait;
            cts.CancelAfter(due);
        }
        catch (ObjectDisposedException) { /* 이미 완료·해제된 로딩 — 무해 */ }
    }

    /// <summary>
    /// 로컬 전용 폴백. 이 호출까지 실패하면(fallback PNG 생성 불가 등) 빈 목록으로 축퇴시켜
    /// Failed 카드가 실제로 도달 가능하게 한다(설계 §0.4·§4.3).
    /// </summary>
    private async Task<IReadOnlyList<FrameTemplate>> SafeLocalFramesAsync()
    {
        try
        {
            return await _catalog.GetLocalDefaultFramesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "로컬 전용 프레임 해석까지 실패 — 사용 가능한 프레임 0개");
            return Array.Empty<FrameTemplate>();
        }
    }

    /// <summary>[기다리지 않고 시작]: 서버 대기를 즉시 포기한다(진행 중 로딩이 로컬 폴백으로 마감).</summary>
    [RelayCommand]
    private void SkipServerWait()
    {
        if (Phase != FrameLoadPhase.Loading) return;
        try { _loadCts?.Cancel(); }
        catch (ObjectDisposedException) { /* 이미 완료·해제된 로딩 — 무해 */ }
    }

    /// <summary>[다시 시도]: 대기 상한을 새로 부여해 처음부터 재시도.</summary>
    [RelayCommand]
    private Task RetryLoad() => ReloadFramesAsync(ReloadReason.Enter);

    // ── A3: 프레임 삭제(로컬 항상 + 파워 서버 옵션) ──

    /// <summary>
    /// 카드 X → 확인 팝업 표시.
    /// it16 §4.4: 판정을 순수 함수 <see cref="FrameEditPolicy.CanDelete"/>에 위임한다 —
    /// 종전에는 커맨드 가드(`CanDeleteFrames`=로그인 여부)가 컨버터보다 느슨해 비power가 DB 공용 프레임의
    /// 로컬 파일을 지울 수 있었다. `IsDeletable`은 출처 판정(빈 Id 방어)이므로 함께 유지한다.
    /// </summary>
    [RelayCommand]
    private void RequestDelete(FrameTemplate? frame)
    {
        if (!IsInteractive) return;      // it20 §5.4: Loading·Failed에서는 목록 조작을 열지 않는다
        if (frame is null) return;
        var user = _shell.Session.CurrentUser;
        if (!FrameEditPolicy.CanDelete(frame, user?.Role) || !IsDeletable(frame)) return;
        FrameToDelete = frame;
        DeleteAlsoServer = false;          // 기본 off
        IsDeleteConfirmVisible = true;
    }

    /// <summary>[확인]: 로컬 삭제 항상, "서버에서도 제거" 체크(파워) 시 DB 삭제(결과를 명확히 안내).</summary>
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        var frame = FrameToDelete;
        if (frame is null) { CancelDelete(); return; }

        bool localOk = _localStore.DeleteLocal(frame);  // 로컬 파일(이미지+슬롯) 삭제
        var alsoServer = DeleteAlsoServer && IsPower;    // 팝업이 곧 닫히며 값이 리셋되므로 미리 확정
        DeleteNotice = string.Empty;
        DeleteNoticeIsError = false;

        Frames.Remove(frame);
        if (SelectedFrame == frame) SelectedFrame = Frames.FirstOrDefault();
        CancelDelete();

        if (alsoServer)
            await DeleteFromServerAsync(frame);

        if (!localOk)
        {
            // 성공 오인 금지: 로컬 파일이 실제로 지워지지 않았음을 알림(사용 중 등).
            DeleteNotice = string.IsNullOrEmpty(DeleteNotice)
                ? "로컬 프레임 파일을 삭제하지 못했습니다(사용 중일 수 있음)."
                : DeleteNotice + " (단, 로컬 파일 삭제 실패)";
            DeleteNoticeIsError = true;
            _logger?.LogWarning("로컬 프레임 삭제 실패: {Name} ({Path})", frame.Name, frame.ImageUrl);
        }

        // 디스크 기준 재스캔으로 목록을 실제 상태와 일치(삭제 성공분은 사라지고, 실패분은 다시 노출). (보완#3)
        // it20 §6.5: 목록이 이미 보이는 상태이므로 조용히 갱신한다 — 삭제마다 대기 오버레이가 번쩍이지 않게.
        await ReloadFramesAsync(ReloadReason.Refresh);
    }

    /// <summary>
    /// 서버(DB+Storage) 삭제. 저장된 서버 id(#dbid=GUID)로 삭제 시도 →
    /// 없으면(로컬 id 불일치·#dbid 누락) 이름으로 서버 기본 프레임을 재탐색해 삭제. 결과를 사용자에게 안내(성공 오인 금지).
    /// </summary>
    private async Task DeleteFromServerAsync(FrameTemplate frame)
    {
        // local: 접두는 로컬 전용 프레임(서버 문서 없음). 그 외는 실 DB 문서 id(GUID)를 담고 있음.
        var serverId = frame.Id.StartsWith("local:", StringComparison.Ordinal)
            ? frame.Id.Substring("local:".Length)
            : frame.Id;
        try
        {
            bool deleted = await _repository.DeleteAsync(serverId);

            // id로 못 찾으면(#dbid 누락/불일치) 이름으로 서버 기본 프레임을 찾아 삭제(파워 공용 프레임 대비).
            if (!deleted)
            {
                var dbFrames = await _repository.GetDefaultFramesAsync();
                var match = dbFrames.FirstOrDefault(f =>
                    string.Equals(f.Name, frame.Name, StringComparison.Ordinal) && !string.IsNullOrEmpty(f.Id));
                if (match is not null)
                {
                    _logger?.LogInformation("서버 삭제 id 불일치 → 이름 매칭 재삭제: {Name} (id={Id})", frame.Name, match.Id);
                    deleted = await _repository.DeleteAsync(match.Id);
                }
            }

            if (deleted)
            {
                DeleteNotice = "서버에서도 삭제되었습니다.";
                DeleteNoticeIsError = false;
            }
            else
            {
                DeleteNotice = $"로컬은 삭제했지만 서버에서 '{frame.Name}' 문서를 찾지 못했습니다.";
                DeleteNoticeIsError = true;
                _logger?.LogWarning("서버 삭제 실패: 문서 미발견 name={Name} triedId={Id}", frame.Name, serverId);
            }
        }
        catch (Exception ex)
        {
            // 성공 오인 금지: 서버 삭제 실패를 사용자에게 노출(미초기화·권한 등).
            DeleteNotice = $"서버 삭제 실패: {ex.Message}";
            DeleteNoticeIsError = true;
            _logger?.LogError(ex, "프레임 서버 삭제 실패 id={Id}", serverId);
        }
    }

    /// <summary>[취소]: 팝업 닫기.</summary>
    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmVisible = false;
        FrameToDelete = null;
        DeleteAlsoServer = false;
    }

    [RelayCommand]
    private async Task Next()
    {
        if (!IsInteractive) return;      // it20 §5.4: 로딩·실패 국면에서 촬영으로 넘어가지 않는다
        if (SelectedFrame is null) return;
        _shell.Session.SelectedFrame = SelectedFrame;
        _shell.Session.Capture.Begin(SelectedFrame, _shell.Settings.Current.CutCount);
        await _shell.NavigateAsync(AppState.Guide);
    }

    /// <summary>프레임 편집기 진입(신규 생성, 프레임 쓰기 권한=AdvancedUser 이상만. it16 E4).</summary>
    [RelayCommand]
    private async Task CreateFrame()
    {
        if (!IsInteractive) return;      // it20 §5.4
        if (!CanCreateFrame) return;
        await _shell.OpenFrameEditor(null);
    }

    /// <summary>선택한 기존 프레임을 편집기로 열기(본인 로컬 or 파워). (기능 요청)</summary>
    [RelayCommand]
    private async Task EditFrame()
    {
        if (!IsInteractive) return;      // it20 §5.4
        if (SelectedFrame is null || !CanEdit(SelectedFrame)) return;
        await _shell.OpenFrameEditor(SelectedFrame);
    }

    /// <summary>
    /// 이 프레임을 현재 역할로 편집 가능한지. 권한 규칙은 순수 함수 <see cref="FrameEditPolicy.CanEdit"/>에 위임.
    /// advanced_user=본인 로컬 생성분(UserId 검증)만, power=본인 로컬+DB 공용 기본,
    /// user·temp_user(it16 E4)·번들/fallback·게스트=불가. (item2 §3, it16 §4)
    /// </summary>
    private bool CanEdit(FrameTemplate f)
    {
        var user = _shell.Session.CurrentUser;
        return FrameEditPolicy.CanEdit(f, user?.Role, user?.Id);
    }

    partial void OnSelectedFrameChanged(FrameTemplate? value)
        => CanEditSelected = value is not null && CanEdit(value);

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("프레임 선택 취소");
}
