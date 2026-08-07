using System.IO;
using System.Net.Http;
using MCPhoto.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 사용 가능한 프레임 목록 제공. 우선순위: ①DB isDefault → ②설치 Frame/ 번들 → ③fallback. (§9 #11)
/// 오프라인/DB 미초기화 시 ②/③로 폴백(게스트+번들 모드). 로그인 시 커스텀 프레임 추가.
/// </summary>
public sealed class FrameCatalogService
{
    private readonly IFrameRepository _repository;
    private readonly ILocalFrameStore _localStore;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _downloadImage;
    private readonly ILogger<FrameCatalogService>? _logger;

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

    /// <summary>번들 프레임 폴더(설치 경로/Frame).</summary>
    public string BundleFolder { get; }

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
        BundleFolder = Path.Combine(AppContext.BaseDirectory, "Frame");
        FallbackImagePath = Path.Combine(App.DataFolder, "cache", "fallback_frame.png");
    }

    /// <summary>
    /// 공용 프레임(게스트 포함). 로컬 공용(번들+파워캐시) 우선 → DB isDefault 중 로컬에 없는 이름만 캐시·병합
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
            // 로컬 스캔·번들 디코드·fallback 생성이 UI 스레드를 점유하지 않게 하는 경계이기도 하다.
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

        // ① 로컬 공용(접두 없는 파일 = 번들 + 파워 캐시)
        var local = _localStore.LoadPublic();
        var localNames = _localStore.PublicFrameNames();

        // ② DB isDefault 중 로컬에 이름이 없는 것만 다운로드·캐시(이름 기준 dedup, 중복 집계 없음)
        try
        {
            ReportShared(new FrameCatalogProgress(FrameCatalogPhase.QueryingServer));
            var dbFrames = await _repository.GetDefaultFramesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // 로컬에 이미 있는 이름(캐시 히트)은 분모에서 제외해 (n/m)을 정직하게 만든다.
            var pending = dbFrames.Where(f => !localNames.Contains(f.Name)).ToList();
            for (int i = 0; i < pending.Count; i++)
            {
                ReportShared(new FrameCatalogProgress(
                    FrameCatalogPhase.DownloadingImage, i + 1, pending.Count));
                var cached = await TryCacheAsync(pending[i], CancellationToken.None).ConfigureAwait(false);
                if (cached is not null) local = Append(local, cached);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DB 기본 프레임 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)");
        }

        ReportShared(new FrameCatalogProgress(FrameCatalogPhase.Completed));
        return ResolveLocalFrames(local);
    }

    /// <summary>
    /// 네트워크를 전혀 쓰지 않는 기본 프레임 해석(로컬 공용 → 번들 → fallback). (it20)
    /// 대기 상한 초과·사용자 건너뛰기 후의 축소 진행 경로다. 정상 동작 시 최소 1개를 돌려준다.
    /// ⚠️ 단일 비행에 합류하지 **않는다** — 합류하면 방금 상한을 넘긴 그 작업을 다시 기다려 상한이 무의미해진다(설계 §6.3).
    /// 읽기 안전 근거: LocalFrameStore가 png를 먼저 쓰고 .slots를 나중에 쓰며, 로드는 .slots 없는 항목을
    /// 건너뛴다(LocalFrameStore.cs:46-48, :108-109) → 반쪽 프레임이 노출되지 않는다.
    /// </summary>
    public Task<IReadOnlyList<FrameTemplate>> GetLocalDefaultFramesAsync(CancellationToken ct = default)
        => Task.Run(() => ResolveLocalFrames(preferLoaded: null), ct);

    /// <summary>
    /// 로컬 우선순위 해석(공용 로컬 → 번들 → fallback). 네트워크를 쓰지 않는다. (it20)
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

        // ③ 번들 폴더에 .slots 없는 이미지가 있으면 자동 격자 배치로 로드(기존 폴백)
        var bundled = LoadBundleFrames();
        if (bundled.Count > 0)
        {
            _logger?.LogInformation("번들 프레임 {Count}개 사용", bundled.Count);
            return bundled;
        }

        // ④ fallback(코드 생성)
        _logger?.LogInformation("fallback 프레임 생성");
        return new[] { EnsureFallbackFrame() };
    }

    /// <summary>로그인 사용자 커스텀 프레임(로컬 전용, `{계정}_` 접두). DB 미조회. (it8 §3 정정)</summary>
    public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        try { return Task.FromResult(_localStore.LoadUser(userId)); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로컬 커스텀 프레임 로드 실패: {User}", userId);
            return Task.FromResult((IReadOnlyList<FrameTemplate>)Array.Empty<FrameTemplate>());
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
        // 로컬 파일 경로(번들/기존 캐시)면 직접 읽기, http면 다운로드.
        if (File.Exists(url)) return await File.ReadAllBytesAsync(url, ct);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        return await _http.GetByteArrayAsync(url, ct);
    }

    private List<FrameTemplate> LoadBundleFrames()
    {
        var list = new List<FrameTemplate>();
        if (!Directory.Exists(BundleFolder)) return list;

        // 번들 규약: Frame/{name}.png + Frame/{name}.slots(선택). slots 없으면 fallback 격자 배치.
        foreach (var img in Directory.EnumerateFiles(BundleFolder)
                     .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var (w, h) = ReadImageSize(img);
                var name = Path.GetFileNameWithoutExtension(img);
                var template = new FrameTemplate
                {
                    Id = $"bundle:{name}",
                    Name = name,
                    IsDefault = true,
                    ImageUrl = img,
                    ImageSize = new ImageSize { Width = w, Height = h }
                };
                LoadOrGenerateSlots(template, img);
                list.Add(template);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "번들 프레임 로드 실패: {Path}", img);
            }
        }
        return list;
    }

    private void LoadOrGenerateSlots(FrameTemplate template, string imagePath)
    {
        var slotFile = Path.ChangeExtension(imagePath, ".slots");
        if (File.Exists(slotFile))
        {
            // 규약: 한 줄에 "index,x,y,w,h"
            foreach (var line in File.ReadAllLines(slotFile))
            {
                var parts = line.Split(',');
                if (parts.Length == 5
                    && int.TryParse(parts[0], out var idx) && int.TryParse(parts[1], out var x)
                    && int.TryParse(parts[2], out var y) && int.TryParse(parts[3], out var w)
                    && int.TryParse(parts[4], out var h))
                {
                    template.Slots.Add(new Slot { Index = idx, X = x, Y = y, Width = w, Height = h });
                }
            }
        }

        if (template.Slots.Count == 0)
        {
            // slots 파일 없으면 이미지 크기에 맞춰 2×2 격자 자동 배치
            GenerateGridSlots(template);
        }
    }

    private static void GenerateGridSlots(FrameTemplate template)
    {
        int fw = template.ImageSize.Width, fh = template.ImageSize.Height;
        int margin = fw / 15, gap = fw / 20;
        int cellW = (fw - margin * 2 - gap) / 2;
        int cellH = (int)(cellW * 4.0 / 3.0);
        int totalH = cellH * 2 + gap;
        int top = Math.Max(margin, (fh - totalH) / 2);
        int[,] o = { { margin, top }, { margin + cellW + gap, top },
                     { margin, top + cellH + gap }, { margin + cellW + gap, top + cellH + gap } };
        for (int i = 0; i < 4; i++)
            template.Slots.Add(new Slot { Index = i, X = o[i, 0], Y = o[i, 1], Width = cellW, Height = cellH });
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
            File.Move(tempPath, FallbackImagePath, overwrite: true);
            // 렌더러가 인자 경로를 ImageUrl에 심으므로 최종 경로로 정정한다 —
            // 빠뜨리면 카드 이미지가 사라진 임시 파일을 가리켜 placeholder가 뜬다.
            template.ImageUrl = FallbackImagePath;
            return template;
        }
    }

    /// <summary>이미지 헤더에서 크기 읽기(OpenCvSharp 디코드).</summary>
    private static (int w, int h) ReadImageSize(string path)
    {
        using var mat = OpenCvSharp.Cv2.ImRead(path, OpenCvSharp.ImreadModes.Color);
        return (mat.Width, mat.Height);
    }
}
