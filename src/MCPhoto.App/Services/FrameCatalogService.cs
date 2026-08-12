using System.IO;
using System.Net.Http;
using MCPhoto.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 사용 가능한 프레임 목록 제공. 우선순위: ①서버 isDefault → ②로컬 캐시 → ③fallback. (§9 #11)
/// 오프라인/DB 미초기화 시 ②/③로 폴백(게스트 모드). 로그인 시 커스텀 프레임 추가.
/// <para>
/// ⚠️ it27 §3.2: 종전 ②였던 <b>설치 폴더 <c>{exe}\Frame</c> 번들 스캔은 폐기됐다</b> — 앱은 앱 경로를
/// 읽지 않는다. 기본 프레임의 유일한 출처는 서버이고 로컬은 그 캐시(<c>%ProgramData%\MCPhoto\Frame</c>)다.
/// </para>
/// </summary>
public sealed class FrameCatalogService
{
    private readonly IFrameRepository _repository;
    private readonly ILocalFrameStore _localStore;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _downloadImage;
    private readonly ILogger<FrameCatalogService>? _logger;

    /// <summary>
    /// 이번 실행에서 캐시 기록에 실패한 서버 문서 id(재시도 차단).
    /// <para>
    /// ⚠️ <b>재다운로드 루프 방지</b>(설계 §17-1): 캐시가 손상되거나 기록에 실패하면 그 프레임은
    /// 로컬 목록에 오르지 못한다 → 다음 동기화가 "로컬에 없음"으로 판정 → 다시 내려받는다 → 또 실패…
    /// 목록을 열 때마다 같은 파일을 무한히 받게 된다. 한 번 실패한 id는 <b>앱을 다시 켤 때까지</b>
    /// 건너뛴다(영구 배제가 아니다 — 일시적 디스크 문제였다면 재시작으로 회복된다).
    /// </para>
    /// </summary>
    private readonly HashSet<string> _cacheFailedIds = new(StringComparer.Ordinal);

    // ── it20: 단일 비행(single-flight) — 종전 세마포어 게이트(_defaultFramesGate) 대체 ──
    // it10 S3-2의 목적(중복 다운로드 방지)은 그대로 유지하면서 "줄 세우기"를 없앤다.
    // 종전 게이트는 시작 prefetch(App.OnStartup)가 잡고 있으면 화면 진입이 그 완료까지 대기하고
    // 진행 상황도 알 수 없어, 대기 상한이 전부 줄 서기에 소모되고 문구가 정체됐다(설계 §6.3).
    // 단일 비행은 같은 작업을 **공유**한다 — 동시 호출은 한 번의 다운로드 패스를 나눠 쓴다.
    // 싱글턴 서비스(ServiceRegistration.cs:98)이므로 인스턴스 필드로 충분.
    // 늦게 합류한 호출자는 진행 중인 작업의 최근 국면을 즉시 replay 받는다(_lastProgress).
    private readonly object _sync = new();
    private Task<IReadOnlyList<FrameTemplate>>? _inFlight;
    private readonly List<IProgress<FrameCatalogProgress>> _observers = new();
    private FrameCatalogProgress _lastProgress = new(FrameCatalogPhase.ResolvingLocal);

    /// <summary>fallback 프레임 이미지 캐시 경로(%ProgramData%\MCPhoto\).</summary>
    public string FallbackImagePath { get; }

    public FrameCatalogService(
        IFrameRepository repository,
        ILocalFrameStore localStore,
        ILogger<FrameCatalogService>? logger = null,
        Func<string, CancellationToken, Task<byte[]?>>? downloadImage = null)
    {
        _repository = repository;
        _localStore = localStore;
        _logger = logger;
        _downloadImage = downloadImage ?? DefaultDownloadAsync;
        FallbackImagePath = Path.Combine(App.DataFolder, "cache", "fallback_frame.png");
    }

    /// <summary>
    /// 공용 프레임(게스트 포함). 로컬 공용 캐시 우선 → DB isDefault 중 로컬에 없는 이름만 캐시·병합
    /// (이름 기준 dedup) → 없으면 fallback. 로컬에 이미 있으면 그 이름은 DB 미다운로드. (it8 §3 정정)
    /// it20: 동시 호출은 **하나의 작업을 공유**한다(단일 비행). <paramref name="progress"/>를 주면 진행
    /// 국면을 받고, 늦게 합류해도 최근 국면이 즉시 1회 replay된다.
    /// <paramref name="ct"/>는 **이 호출자만** 취소한다 — 공유 작업은 계속 진행해 캐시를 완성하므로
    /// 다른 호출자나 시작 prefetch가 피해를 입지 않는다.
    /// </summary>
    public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(
        CancellationToken ct = default,
        IProgress<FrameCatalogProgress>? progress = null)
    {
        Task<IReadOnlyList<FrameTemplate>> shared;
        FrameCatalogProgress snapshot;
        lock (_sync)
        {
            if (progress is not null) _observers.Add(progress);
            // ⚠️ 새 패스를 시작하는 호출자에게는 이전 패스의 마지막 국면(Completed = "프레임 목록을
            //    정리하는 중…")을 replay하면 안 된다 — 홈 왕복 후 재진입 때마다 첫 문구가 거짓이 된다.
            //    새 패스면 스냅샷을 시작 국면으로 되돌리고, 합류하는 경우에만 진행 중 국면을 replay한다.
            if (_inFlight is null) _lastProgress = new FrameCatalogProgress(FrameCatalogPhase.ResolvingLocal);
            snapshot = _lastProgress;
            // Task.Run으로 시작 → 호출자(UI 스레드)의 동기 구간은 이 lock 뿐이다(설계 §8.1).
            // 로컬 스캔·fallback 생성이 UI 스레드를 점유하지 않게 하는 경계이기도 하다.
            _inFlight ??= Task.Run(RunSharedLoadAsync);
            shared = _inFlight;
        }
        progress?.Report(snapshot);          // 문구 공백 구간 제거(합류 즉시 현재 국면 표시)
        return AwaitSharedAsync(shared, progress, ct);
    }

    /// <summary>공유 작업의 완료를 이 호출자의 취소 토큰으로 기다린다(공유 작업 자체는 취소하지 않는다).</summary>
    private async Task<IReadOnlyList<FrameTemplate>> AwaitSharedAsync(
        Task<IReadOnlyList<FrameTemplate>> shared,
        IProgress<FrameCatalogProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            // 호출자별 취소: WaitAsync가 경계에서 OperationCanceledException을 던지고,
            // 공유 작업은 그대로 진행해 캐시 워밍을 완성한다(다른 호출자 보호).
            return await shared.WaitAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            // 구독 제거 경로는 이 finally **한 곳**이다(취소·예외·정상 완료 모두 통과) → 누적되지 않는다.
            if (progress is not null)
                lock (_sync) { _observers.Remove(progress); }
        }
    }

    /// <summary>구독 중인 모든 호출자에게 진행을 알리고 replay용 스냅샷을 갱신한다.</summary>
    private void ReportShared(FrameCatalogProgress p)
    {
        IProgress<FrameCatalogProgress>[] targets;
        lock (_sync)
        {
            _lastProgress = p;
            targets = _observers.ToArray();
        }
        foreach (var t in targets)
        {
            // 구독자(UI) 예외가 로딩을 깨지 않게 한다.
            try { t.Report(p); }
            catch (Exception ex) { _logger?.LogWarning(ex, "프레임 진행 보고 실패(무시)"); }
        }
    }

    private async Task<IReadOnlyList<FrameTemplate>> RunSharedLoadAsync()
    {
        try
        {
            return await LoadDefaultFramesCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) { _inFlight = null; }   // 다음 호출은 새 작업을 시작한다(캐시 반영 후 재조회)
        }
    }

    /// <summary>
    /// 공유 작업 본체. 개별 호출자가 취소하지 않으므로 전 구간 <see cref="CancellationToken.None"/>을 쓴다.
    /// </summary>
    private async Task<IReadOnlyList<FrameTemplate>> LoadDefaultFramesCoreAsync()
    {
        ReportShared(new FrameCatalogProgress(FrameCatalogPhase.ResolvingLocal));

        // ① 로컬 공용 캐시(루트 = 서버 default 캐시 + power 공용 생성분)
        var local = _localStore.LoadPublic();

        // ② DB isDefault와 `#dbid` 기준으로 대조 → 없는 것만 받고, 서버에서 지워진 캐시는 삭제한다.
        //    ⚠️ 이름 기준 dedup은 폐기했다(D-20) — 삭제 판정과 기준이 갈리면 "다운로드는 건너뛰는데
        //    삭제 대상으로 잡히는" 모순이 생긴다.
        try
        {
            ReportShared(new FrameCatalogProgress(FrameCatalogPhase.QueryingServer));
            var dbFrames = await _repository.GetDefaultFramesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // 서버 조회가 성공한 경우에만 삭제 판정을 한다(FrameSyncPlan 안전장치 1).
            // 예외로 빠지면 이 블록에 오지 않으므로 오프라인에서 캐시가 지워질 일이 없다.
            local = SyncPublicCache(local, dbFrames);

            var localDbIds = DbIdsOf(local);
            var pending = dbFrames
                .Where(f => !localDbIds.Contains(f.Id) && !IsCacheBlocked(f.Id))
                .ToList();
            for (int i = 0; i < pending.Count; i++)
            {
                ReportShared(new FrameCatalogProgress(
                    FrameCatalogPhase.DownloadingImage, i + 1, pending.Count));
                var cached = await TryCacheAsync(pending[i], CancellationToken.None).ConfigureAwait(false);
                if (cached is not null) local = Append(local, cached);
                else BlockCacheRetry(pending[i].Id);   // 루프 방지: 이번 실행에서는 다시 시도하지 않는다
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DB 기본 프레임 조회 실패 — 로컬 캐시/fallback로 폴백(오프라인 모드)");
        }

        ReportShared(new FrameCatalogProgress(FrameCatalogPhase.Completed));
        return ResolveLocalFrames(local);
    }

    /// <summary>
    /// 서버 정본에 없는 공용 캐시를 지운다(설계 §10 삭제 동기화).
    /// <para>
    /// power가 서버에서 공용 프레임을 지워도, 이미 내려받은 PC에는 파일이 남아 계속 촬영에 쓰인다.
    /// 그 잔재를 정리하는 것이 이 함수다.
    /// </para>
    /// <b>안전장치는 <see cref="FrameSyncPlan"/>이 강제한다</b> — 서버 목록이 비었으면 삭제하지 않고
    /// (장애로 0개를 받았을 때의 참사 방지), <c>#dbid</c>가 없는 로컬 전용(<c>local:</c>) 프레임은
    /// 애초에 대상이 아니다.
    /// </summary>
    private IReadOnlyList<FrameTemplate> SyncPublicCache(
        IReadOnlyList<FrameTemplate> local, IReadOnlyList<FrameTemplate> serverFrames)
    {
        var decision = FrameSyncPlan.Build(
            serverReachable: true,
            serverDbIds: serverFrames.Select(f => f.Id).ToList(),
            localDbIds: DbIdsOf(local).ToList());

        if (decision.DeleteSkipped)
        {
            _logger?.LogInformation("공용 캐시 삭제 동기화 보류: {Reason}", decision.DeleteSkipReason);
            return local;
        }
        if (decision.ToDelete.Count == 0) return local;

        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in decision.ToDelete)
        {
            var victim = local.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
            if (victim is null) continue;

            if (_localStore.DeleteLocal(victim))
            {
                removed.Add(id);
                _logger?.LogInformation("서버에서 삭제된 공용 프레임 캐시 제거: {Name} ({Id})", victim.Name, id);
            }
            else
            {
                // 파일 잠금 등으로 실패 — 목록에서만 빼고 다음 동기화에서 재시도한다.
                removed.Add(id);
                _logger?.LogWarning("공용 프레임 캐시 삭제 실패(다음 동기화에서 재시도): {Name}", victim.Name);
            }
        }

        return removed.Count == 0
            ? local
            : local.Where(f => !removed.Contains(f.Id)).ToList();
    }

    /// <summary>이번 실행에서 캐시에 실패해 재시도를 막아 둔 프레임인가.</summary>
    private bool IsCacheBlocked(string? dbId)
    {
        if (string.IsNullOrEmpty(dbId)) return false;
        lock (_sync) return _cacheFailedIds.Contains(dbId);
    }

    /// <summary>캐시 실패를 기록해 이번 실행 동안 재다운로드를 건너뛰게 한다.</summary>
    private void BlockCacheRetry(string? dbId)
    {
        if (string.IsNullOrEmpty(dbId)) return;
        bool added;
        lock (_sync) added = _cacheFailedIds.Add(dbId);
        if (added)
            _logger?.LogWarning("프레임 캐시 실패 — 이번 실행에서는 재시도하지 않는다(재다운로드 루프 방지): {Id}", dbId);
    }

    /// <summary>
    /// 서버 문서 id를 가진 로컬 프레임의 id 집합(`local:` 접두는 서버 미동기라 제외).
    /// <para>
    /// ⚠️ <c>bundle:</c> 제외는 it27 이후에도 <b>남긴다</b>(fail-safe) — 지우면 그 id가 서버 대조 집합에
    /// 들어가고 서버 목록엔 없으므로 <see cref="FrameSyncPlan"/>이 <b>삭제 대상으로 잡는다</b>
    /// (설계 it27 §4.3 ④). 생성 경로가 없다는 사실은 이 제외를 지울 근거가 되지 않는다.
    /// </para>
    /// </summary>
    private static IReadOnlySet<string> DbIdsOf(IReadOnlyList<FrameTemplate> frames)
        => new HashSet<string>(
            frames.Where(f => !string.IsNullOrEmpty(f.Id)
                              && !f.Id.StartsWith("local:", StringComparison.Ordinal)
                              && !f.Id.StartsWith("bundle:", StringComparison.Ordinal)
                              && !f.Id.StartsWith("fallback", StringComparison.Ordinal))
                  .Select(f => f.Id),
            StringComparer.Ordinal);

    /// <summary>
    /// 네트워크를 전혀 쓰지 않는 기본 프레임 해석(로컬 공용 → fallback). (it20)
    /// 대기 상한 초과·사용자 건너뛰기 후의 축소 진행 경로다. 정상 동작 시 최소 1개를 돌려준다.
    /// ⚠️ 단일 비행에 합류하지 **않는다** — 합류하면 방금 상한을 넘긴 그 작업을 다시 기다려 상한이 무의미해진다(설계 §6.3).
    /// 읽기 안전 근거: LocalFrameStore가 png를 먼저 쓰고 .slots를 나중에 쓰며, 로드는 .slots 없는 항목을
    /// 건너뛴다(LocalFrameStore.cs:46-48, :108-109) → 반쪽 프레임이 노출되지 않는다.
    /// </summary>
    public Task<IReadOnlyList<FrameTemplate>> GetLocalDefaultFramesAsync(CancellationToken ct = default)
        => Task.Run(() => ResolveLocalFrames(preferLoaded: null), ct);

    /// <summary>
    /// 로컬 우선순위 해석(공용 로컬 → fallback). 네트워크를 쓰지 않는다. (it20)
    /// preferLoaded가 비어 있지 않으면 그대로 채택 — 호출측이 이미 스캔·병합을 마친 경우다.
    /// 두 경로(공유 작업 종단·로컬 전용 API)가 같은 코드를 쓰게 해 §9 #11 우선순위 규약이 갈라지지 않게 한다.
    /// </summary>
    private IReadOnlyList<FrameTemplate> ResolveLocalFrames(IReadOnlyList<FrameTemplate>? preferLoaded)
    {
        var local = preferLoaded ?? _localStore.LoadPublic();
        if (local.Count > 0)
        {
            _logger?.LogInformation("공용 프레임 {Count}개(로컬 우선 + DB 캐시 병합)", local.Count);
            return local;
        }

        // ② fallback(코드 생성) — it27 §3.2: 종전 ②였던 번들 폴더({exe}\Frame) 스캔은 폐기됐다.
        _logger?.LogInformation("fallback 프레임 생성");
        return new[] { EnsureFallbackFrame() };
    }

    /// <summary>
    /// 로그인 사용자 개인 프레임. <b>서버가 정본</b>이고 로컬은 캐시다(설계 D-7).
    /// <para>
    /// 서버 조회 성공 시 <c>#dbid</c>로 대조해 ① 없는 것은 내려받고 ② 서버에서 지워진 캐시는 삭제한다
    /// (다른 기기에서 지운 프레임이 이 PC에 남지 않게 — 사용자 요구). 서버에 닿지 못하면
    /// <b>캐시를 그대로 쓴다</b>(오프라인 촬영 불변식, 삭제도 하지 않는다).
    /// </para>
    /// </summary>
    /// <param name="userId">서버 조회용 계정 id.</param>
    /// <param name="ownerEmail">로컬 저장·로드용 소유자 이메일(로컬 소유 판정의 단일 기준).</param>
    public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(
        string userId, string ownerEmail, CancellationToken ct = default)
    {
        IReadOnlyList<FrameTemplate> local;
        try { local = _localStore.LoadUser(ownerEmail); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로컬 개인 프레임 로드 실패");
            local = Array.Empty<FrameTemplate>();
        }

        if (string.IsNullOrWhiteSpace(userId)) return local;

        IReadOnlyList<FrameTemplate> serverFrames;
        try
        {
            serverFrames = await _repository.GetUserFramesAsync(userId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 서버 미도달 → 삭제 판정을 하지 않는다(FrameSyncPlan 안전장치 1). 캐시로 계속 진행.
            _logger?.LogWarning(ex, "개인 프레임 서버 조회 실패 — 로컬 캐시로 진행(오프라인)");
            return local;
        }

        local = SyncUserCache(local, serverFrames, ownerEmail);

        // 서버에만 있는 것 내려받기(다른 기기에서 만든 프레임).
        var localDbIds = DbIdsOf(local);
        foreach (var f in serverFrames.Where(f => !localDbIds.Contains(f.Id) && !IsCacheBlocked(f.Id)))
        {
            var cached = await TryCacheUserFrameAsync(f, ownerEmail, ct).ConfigureAwait(false);
            if (cached is not null) local = Append(local, cached);
            else BlockCacheRetry(f.Id);
        }

        return local;
    }

    /// <summary>서버 정본에 없는 개인 캐시를 지운다(다른 기기에서 삭제된 프레임). 규칙은 공용과 동일.</summary>
    private IReadOnlyList<FrameTemplate> SyncUserCache(
        IReadOnlyList<FrameTemplate> local, IReadOnlyList<FrameTemplate> serverFrames, string ownerEmail)
    {
        var decision = FrameSyncPlan.Build(
            serverReachable: true,
            serverDbIds: serverFrames.Select(f => f.Id).ToList(),
            localDbIds: DbIdsOf(local).ToList());

        if (decision.DeleteSkipped)
        {
            _logger?.LogInformation("개인 캐시 삭제 동기화 보류: {Reason}", decision.DeleteSkipReason);
            return local;
        }
        if (decision.ToDelete.Count == 0) return local;

        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in decision.ToDelete)
        {
            var victim = local.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
            if (victim is null) continue;
            _localStore.DeleteLocal(victim);   // 실패해도 목록에서는 빼고 다음 동기화에서 재시도
            removed.Add(id);
            _logger?.LogInformation("서버에서 삭제된 개인 프레임 캐시 제거: {Name}", victim.Name);
        }

        return removed.Count == 0 ? local : local.Where(f => !removed.Contains(f.Id)).ToList();
    }

    /// <summary>서버 개인 프레임 이미지를 내려받아 개인 캐시로 기록(#owner=이메일, #dbid 보존). 실패 시 null.</summary>
    private async Task<FrameTemplate?> TryCacheUserFrameAsync(FrameTemplate f, string ownerEmail, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(f.ImageUrl)) return null;
            var bytes = await _downloadImage(f.ImageUrl, ct);
            if (bytes is not { Length: > 0 }) return null;

            var cached = _localStore.SaveUserFrame(f, bytes, ownerEmail, dbId: f.Id);
            _logger?.LogInformation("개인 프레임 캐시: {Name} ← 서버({Id})", cached.Name, f.Id);
            return cached;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "개인 프레임 캐시 실패: {Name}", f.Name);
            return null;
        }
    }

    /// <summary>DB 프레임 이미지를 다운로드해 공용 캐시(이름 기반, 접두 없음). 실패 시 null.</summary>
    private async Task<FrameTemplate?> TryCacheAsync(FrameTemplate f, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(f.ImageUrl)) return null;

            // it10 S3-3(D3): 이름에 '_' 포함 기본 프레임은 로컬 공용 규약(접두 '_' = user 파일)과 충돌해
            // 공용 목록·dedup 집합에서 제외 → 매 실행 재다운로드된다. 동작은 현행 유지(캐시·표시 정상), 경고만.
            if (f.Name.Contains('_'))
                _logger?.LogWarning(
                    "기본 프레임 이름에 '_' 포함 — 로컬 공용 규약과 충돌, 매 실행 재다운로드됨: {Name}", f.Name);

            var bytes = await _downloadImage(f.ImageUrl, ct);
            if (bytes is { Length: > 0 })
            {
                // 공용 캐시(#owner=default) + 서버 문서 id 기록 → 삭제 동기화 대조 키가 된다(설계 §10).
                var cached = _localStore.SaveDefaultFrame(f, bytes, dbId: f.Id);
                // it10 S3-3: 다운로드·캐시 성공 로그(기존은 실패 warning만) — QA가 캐시 건수를 로그로 확인.
                _logger?.LogInformation("기본 프레임 캐시: {Name} ← DB({Id})", cached.Name, f.Id);
                return cached;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "프레임 캐시 다운로드 실패: {Id}", f.Id);
        }
        return null;
    }

    private static IReadOnlyList<FrameTemplate> Append(IReadOnlyList<FrameTemplate> list, FrameTemplate item)
    {
        var l = new List<FrameTemplate>(list) { item };
        return l;
    }

    private static readonly HttpClient _http = new();
    private static async Task<byte[]?> DefaultDownloadAsync(string url, CancellationToken ct)
    {
        // 로컬 캐시 파일 경로면 직접 읽기, http면 다운로드.
        if (File.Exists(url)) return await File.ReadAllBytesAsync(url, ct);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        return await _http.GetByteArrayAsync(url, ct);
    }

    // it20: fallback PNG는 프로세스 내 여러 경로(공유 작업 종단 · 로컬 전용 API)에서 동시에 요구될 수 있다.
    // 같은 경로에 두 스레드가 ImWrite하면 공유 위반 실패 또는 반쯤 쓰인 PNG(디코드 실패)가 남는다.
    private static readonly object _fallbackWriteSync = new();

    /// <summary>
    /// fallback 프레임 확보. 이미 있으면 템플릿만 재구성하고, 없으면 **생성**한다. (it20 §7.2)
    /// ⚠️ 이 메서드는 파일을 쓴다 — 전용 lock으로 검사·생성을 직렬화하고 임시 파일에 렌더한 뒤
    /// <see cref="File.Move(string, string, bool)"/>로 원자 교체해 중간 상태 파일이 남지 않게 한다.
    /// 호출은 항상 <c>Task.Run</c> 경계 안에서 일어나므로 lock이 UI 스레드를 막지 않는다(설계 §8.1).
    /// </summary>
    private FrameTemplate EnsureFallbackFrame()
    {
        lock (_fallbackWriteSync)
        {
            if (File.Exists(FallbackImagePath))
                return DefaultFrameProvider.CreateFallbackTemplate(FallbackImagePath);

            // ⚠️ 임시 파일도 .png 확장자를 유지해야 한다 — Cv2.ImWrite는 확장자로 인코더를 고르므로
            //    ".png.tmp" 같은 경로는 "could not find a writer for the specified extension"으로 던진다
            //    (설계 §7.2의 `경로 + ".tmp"`를 그대로 쓰면 최초 실행이 항상 Failed 카드로 떨어진다).
            var tempPath = Path.ChangeExtension(FallbackImagePath, ".tmp.png");
            var template = FallbackFrameRenderer.Create(tempPath);
            Directory.CreateDirectory(Path.GetDirectoryName(FallbackImagePath)!);
            MoveWithRetry(tempPath, FallbackImagePath);
            // 렌더러가 인자 경로를 ImageUrl에 심으므로 최종 경로로 정정한다 —
            // 빠뜨리면 카드 이미지가 사라진 임시 파일을 가리켜 placeholder가 뜬다.
            template.ImageUrl = FallbackImagePath;
            return template;
        }
    }

    /// <summary>
    /// 원자 교체(Move) — 일시적 공유 위반은 짧게 재시도한다.
    /// <para>
    /// ⚠️ 재시도가 필요한 이유: 방금 <c>ImWrite</c>로 만든 임시 파일은 백신·검색 인덱서가 곧바로 열어 볼 수 있고,
    /// 그 순간 <see cref="File.Move(string, string, bool)"/>가 IOException(사용 중)으로 <b>실패한다</b>.
    /// 재시도가 없으면 그 한 번의 실패가 예외로 올라가 최초 실행이 "프레임 0개 → 실패 카드"로 떨어진다
    /// (전량 실패로 보이지만 실제로는 수십 ms만 기다리면 되는 일시 상태다).
    /// 이 저장소의 테스트에서도 같은 원인으로 간헐 실패가 관측됐다(it23 구현 중 발견).
    /// </para>
    /// 호출은 항상 <c>Task.Run</c> 경계 안(전용 lock 안)이라 짧은 <c>Thread.Sleep</c>이 UI를 막지 않는다.
    /// 마지막 시도까지 실패하면 그대로 던진다 — 진짜 실패(권한·디스크)는 숨기지 않는다.
    /// </summary>
    private static void MoveWithRetry(string sourcePath, string destinationPath, int attempts = 5)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (i < attempts)
            {
                System.Threading.Thread.Sleep(30 * i);
            }
        }
    }
}
