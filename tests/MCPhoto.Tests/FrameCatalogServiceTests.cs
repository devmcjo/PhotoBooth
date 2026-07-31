using System.IO;
using System.Reflection;
using MCPhoto.App.Services;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>it8 Step 3 (A2): 로컬 우선 로딩 + 파워 캐시. 캐시 히트 시 DB 미조회, 미스 시 다운로드·캐시.</summary>
[Collection(FallbackCacheCollection.Name)]   // it20 N2: 공유 fallback 캐시 경로 경합 제거
public class FrameCatalogServiceTests : IDisposable
{
    private sealed class CountingFrameRepository : IFrameRepository
    {
        public int DefaultCalls { get; private set; }
        public IReadOnlyList<FrameTemplate> DefaultFrames { get; set; } = new List<FrameTemplate>();

        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
        {
            DefaultCalls++;
            return Task.FromResult(DefaultFrames);
        }
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly string _root;
    private readonly LocalFrameStore _store;

    public FrameCatalogServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_cat_{Guid.NewGuid():N}");
        _store = new LocalFrameStore(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 무시 */ }
    }

    private int _downloadCalls;
    private FrameCatalogService MakeService(CountingFrameRepository repo)
        => new(repo, _store, logger: null,
            downloadImage: (_, _) => { _downloadCalls++; return Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 }); });

    private static FrameTemplate DbFrame(string name) => new()
    {
        Id = "doc-" + name,   // DB 문서 id
        Name = name,          // 이름 기준 dedup
        IsDefault = true,
        ImageUrl = "https://example/frame.png",
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = { new Slot { Index = 0, X = 0, Y = 0, Width = 100, Height = 100 } }
    };

    [Fact]
    public async Task Cache_Hit_Skips_Download()
    {
        // 로컬에 같은 이름 공용 프레임이 이미 있으면 그 이름은 다운로드 스킵(이름 dedup, 정정 §3.3).
        _store.CacheFromDb(DbFrame("f1"), new byte[] { 9 }); // 미리 캐시(이름 f1)

        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var svc = MakeService(repo);

        var result = await svc.GetDefaultFramesAsync();

        Assert.Equal(0, _downloadCalls);       // 이름 로컬 존재 → 다운로드 0(캐시 히트)
        Assert.Single(result);                 // 로컬 공용 f1
    }

    [Fact]
    public async Task Cache_Miss_Downloads_And_Dedups()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") } };
        var svc = MakeService(repo);

        var result = await svc.GetDefaultFramesAsync();

        Assert.Equal(2, _downloadCalls);       // 둘 다 로컬에 없어 다운로드 2회
        Assert.Equal(2, result.Count);

        // 두 번째 호출: 이제 로컬 공용에 f1·f2 존재 → 이름 dedup으로 다운로드 추가 0, 중복 집계 없음
        _downloadCalls = 0;
        var again = await svc.GetDefaultFramesAsync();
        Assert.Equal(0, _downloadCalls);
        Assert.Equal(2, again.Count);          // 중복 없이 2개
    }

    // ── it10 S3-2: 동시 호출 직렬화(중복 다운로드 방지) ──

    [Fact]
    public async Task Concurrent_Calls_Download_Each_Frame_Once()
    {
        // 다운로드에 인위적 지연(게이트)을 걸어 두 호출을 확실히 경합시킨다.
        // 직렬화가 없으면 두 호출이 dedup 검사(캐시 쓰기 전)를 동시에 통과 → 프레임당 2회 다운로드.
        // 게이트가 있으면 두 번째 호출은 첫 호출의 캐시를 보고 다운로드 0.
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") } };

        var release = new TaskCompletionSource();
        int downloadCount = 0;
        var svc = new FrameCatalogService(repo, _store, logger: null,
            downloadImage: async (_, _) =>
            {
                Interlocked.Increment(ref downloadCount);
                await release.Task; // 첫 호출이 게이트 안에서 대기 → 두 번째 호출이 진입을 시도하게 함
                return new byte[] { 1, 2, 3 };
            });

        var call1 = svc.GetDefaultFramesAsync();
        var call2 = svc.GetDefaultFramesAsync();

        // 두 호출이 스케줄될 시간을 준 뒤 다운로드 게이트 해제.
        await Task.Delay(50);
        release.SetResult();

        var r1 = await call1;
        var r2 = await call2;

        // 직렬화로 프레임당 다운로드 1회(f1·f2 = 2회)만 발생해야 한다.
        Assert.Equal(2, downloadCount);
        Assert.Equal(2, r1.Count);
        Assert.Equal(2, r2.Count);
    }

    // ── it10 S3-3(D3): 이름에 '_' 포함 기본 프레임 — 동작 불변(경고만) ──

    [Fact]
    public async Task Underscore_Name_Default_Frame_Still_Downloaded_And_Displayed()
    {
        // '_' 포함 이름은 로컬 공용 규약과 충돌해 매 실행 재다운로드되지만, 세션 표시는 정상이어야 한다(동작 불변).
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("bad_name") } };
        var svc = MakeService(repo);

        var result = await svc.GetDefaultFramesAsync();

        Assert.Equal(1, _downloadCalls);                     // '_' 이름은 공용 dedup에서 제외 → 다운로드됨
        Assert.Contains(result, f => f.Name == "bad_name");  // 반환 목록에 정상 포함(표시 가능)
    }

    // ── it20 Step 2: 세마포어 줄 세우기 → 단일 비행(공유 작업) + 로컬 전용 해석 ──

    /// <summary>
    /// 다운로드를 붙잡아 공유 작업을 진행 중 상태로 고정하는 하네스.
    /// ⚠️ repo.DefaultFrames에 DB 프레임이 있어야 downloadImage가 호출된다(비어 있으면 붙잡기가 성립하지 않는다).
    /// </summary>
    private (FrameCatalogService svc, TaskCompletionSource release, Func<int> downloads) MakeHeldService(
        CountingFrameRepository repo)
    {
        var release = new TaskCompletionSource();
        int count = 0;
        var svc = new FrameCatalogService(repo, _store, logger: null,
            downloadImage: async (_, _) =>
            {
                Interlocked.Increment(ref count);
                await release.Task;
                return new byte[] { 1, 2, 3 };
            });
        return (svc, release, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// T-19: 취소는 **경계에서 정직하게** 전파된다(단일 비행의 Task.WaitAsync(ct)).
    /// 종전 세마포어 구조에서는 취소가 서비스 내부에서 삼켜져 부분 목록이 반환됐다 —
    /// 편집기 피커(FramePickerViewModel)는 이미 catch (OperationCanceledException)을 갖고 있어
    /// 모달 종료·재오픈 시 조용히 끝난다(설계 VF-24 정정).
    /// </summary>
    [Fact]
    public async Task Picker_Style_Cancellation_Surfaces_As_OperationCanceled()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var (svc, release, _) = MakeHeldService(repo);
        using var cts = new CancellationTokenSource();

        var call = svc.GetDefaultFramesAsync(cts.Token);
        await Task.Delay(50);          // 공유 작업이 다운로드 붙잡기에 도달할 시간
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        // 공유 작업을 끝까지 기다린다 — await하지 않으면 고아 태스크가 클래스 Dispose(임시 _root 삭제)와
        // 경쟁해 다른 테스트에 간헐적 실패를 흘린다(호출자 취소는 공유 작업을 죽이지 않으므로 계속 돌고 있다).
        release.SetResult();
        await svc.GetDefaultFramesAsync();
    }

    /// <summary>
    /// T-20: 호출자 A의 취소가 공유 작업을 죽이지 않는다 — 취소 없는 호출자 B는 정상 완료하고,
    /// 다운로드 패스는 여전히 프레임당 1회다(캐시 워밍 유지·다른 호출자 보호, 설계 §7.1).
    /// </summary>
    [Fact]
    public async Task Caller_Cancellation_Does_Not_Kill_Shared_Work()
    {
        var repo = new CountingFrameRepository
        {
            DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") }
        };
        var (svc, release, downloads) = MakeHeldService(repo);
        using var cts = new CancellationTokenSource();

        var callA = svc.GetDefaultFramesAsync(cts.Token);
        var callB = svc.GetDefaultFramesAsync();     // 취소 없음 — 같은 공유 작업에 합류
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callA);

        release.SetResult();
        var resultB = await callB;

        Assert.Equal(2, resultB.Count);              // A의 취소와 무관하게 완주
        Assert.Equal(2, downloads());                // 프레임당 1회(중복 0)
    }

    /// <summary>T-21: 동시 호출은 하나의 다운로드 패스를 공유한다(it10 S3-2의 목적을 단일 비행이 그대로 달성).</summary>
    [Fact]
    public async Task Concurrent_Callers_Share_One_Pass()
    {
        var repo = new CountingFrameRepository
        {
            DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") }
        };
        var (svc, release, downloads) = MakeHeldService(repo);

        var call1 = svc.GetDefaultFramesAsync();
        var call2 = svc.GetDefaultFramesAsync();
        await Task.Delay(50);
        release.SetResult();

        var r1 = await call1;
        var r2 = await call2;

        Assert.Equal(2, downloads());
        Assert.Equal(2, r1.Count);
        Assert.Equal(2, r2.Count);
    }

    /// <summary>T-25: 로컬에 아무것도 없으면 로컬 전용 해석이 fallback 1개를 돌려주고 **DB를 조회하지 않는다**.</summary>
    [Fact]
    public async Task LocalOnly_Returns_Fallback_When_Nothing_Local()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var svc = MakeService(repo);

        var result = await svc.GetLocalDefaultFramesAsync();

        Assert.Single(result);
        Assert.StartsWith("fallback", result[0].Id, StringComparison.Ordinal);
        Assert.Equal(0, repo.DefaultCalls);   // 네트워크·DB 미사용
        Assert.Equal(0, _downloadCalls);
    }

    /// <summary>T-26: 로컬 캐시가 있으면 그것을 그대로 돌려준다(DB 조회·다운로드 0).</summary>
    [Fact]
    public async Task LocalOnly_Returns_Cached_Public_Frames()
    {
        _store.CacheFromDb(DbFrame("f1"), new byte[] { 9 });

        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var svc = MakeService(repo);

        var result = await svc.GetLocalDefaultFramesAsync();

        Assert.Single(result);
        Assert.Equal("f1", result[0].Name);
        Assert.Equal(0, repo.DefaultCalls);
        Assert.Equal(0, _downloadCalls);
    }

    /// <summary>
    /// T-27: 로컬 전용 해석은 **진행 중인 공유 작업에 합류하지 않는다**. 합류하면 방금 상한을 넘긴
    /// 그 작업을 다시 기다려 대기 상한이 무의미해진다(설계 §6.3·§7.2).
    /// </summary>
    [Fact]
    public async Task LocalOnly_Does_Not_Join_Shared_Work()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var (svc, release, _) = MakeHeldService(repo);

        var held = svc.GetDefaultFramesAsync();      // 공유 작업 시작(다운로드에서 붙잡힘)
        await Task.Delay(50);

        // 붙잡기를 해제하지 않은 상태에서 2초 내 완료해야 한다.
        var localOnly = await svc.GetLocalDefaultFramesAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotEmpty(localOnly);

        release.SetResult();
        await held;
    }

    /// <summary>
    /// T-29: 완료된 공유 작업은 서비스에 상주하지 않는다(_inFlight 해제) → 다음 호출은 새 패스를 시작한다.
    /// 그때는 로컬 캐시가 채워져 있어 다운로드가 0회다(이름 dedup).
    /// </summary>
    [Fact]
    public async Task Completed_Work_Is_Not_Cached_In_Service()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var svc = MakeService(repo);

        var first = await svc.GetDefaultFramesAsync();
        Assert.Single(first);
        Assert.Equal(1, _downloadCalls);

        var field = typeof(FrameCatalogService)
            .GetField("_inFlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field!.GetValue(svc));           // 완료된 목록이 서비스에 영구 상주하지 않는다

        var second = await svc.GetDefaultFramesAsync();
        Assert.Equal(2, repo.DefaultCalls);          // 새 패스를 시작했다(재조회)
        Assert.Equal(1, _downloadCalls);             // 로컬 캐시 히트 → 추가 다운로드 0
        Assert.Single(second);
    }

    // ── it20 Step 4: fallback PNG 생성의 쓰기 경합 제거 ──

    /// <summary>
    /// T-28: 서로 다른 서비스 인스턴스 2개가 같은 fallback 경로를 향해 동시에 로컬 전용 해석을 수행해도
    /// 예외가 없고, 최종 PNG가 **디코드 가능**하며, 임시 파일 잔재가 남지 않고,
    /// 두 결과의 ImageUrl이 모두 최종 경로다(임시 경로를 그대로 반환하면 카드가 placeholder로 뜬다).
    /// </summary>
    [Fact]
    public async Task Fallback_Concurrent_Creation_Produces_One_Valid_File()
    {
        var repoA = new CountingFrameRepository();
        var repoB = new CountingFrameRepository();
        var svcA = MakeService(repoA);
        var svcB = new FrameCatalogService(repoB, _store, logger: null,
            downloadImage: (_, _) => Task.FromResult<byte[]?>(null));

        var finalPath = svcA.FallbackImagePath;
        var cacheDir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(cacheDir);
        // 생성 경로를 강제한다(이미 있으면 lock은 경합하지 않는다).
        try { if (File.Exists(finalPath)) File.Delete(finalPath); } catch { /* 다른 테스트가 쓰는 중 — 무해 */ }

        var callA = svcA.GetLocalDefaultFramesAsync();
        var callB = svcB.GetLocalDefaultFramesAsync();
        var resultA = await callA;   // 예외가 나면 여기서 테스트가 실패한다
        var resultB = await callB;

        Assert.Single(resultA);
        Assert.Single(resultB);
        Assert.Equal(finalPath, resultA[0].ImageUrl);
        Assert.Equal(finalPath, resultB[0].ImageUrl);
        Assert.DoesNotContain(".tmp", resultA[0].ImageUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".tmp", resultB[0].ImageUrl, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(finalPath), "fallback PNG가 최종 경로에 없다");
        Assert.Empty(Directory.GetFiles(cacheDir, "*.tmp*"));   // 임시 파일 잔재 0개

        using var mat = OpenCvSharp.Cv2.ImRead(finalPath, OpenCvSharp.ImreadModes.Color);
        Assert.False(mat.Empty(), "최종 fallback PNG가 디코드되지 않는다(반쯤 쓰인 파일)");
        Assert.Equal(MCPhoto.Core.Frames.DefaultFrameProvider.FallbackWidth, mat.Width);
        Assert.Equal(MCPhoto.Core.Frames.DefaultFrameProvider.FallbackHeight, mat.Height);
    }

    // ── it20 Step 3: 진행 중계와 replay ──

    /// <summary>
    /// 동기 수집 IProgress 스텁. 보고는 스레드풀(공유 작업)과 호출 스레드(replay) 양쪽에서 오므로 lock으로 보호한다.
    /// (실제 앱에서는 Progress&lt;T&gt;가 UI 스레드로 마샬링한다 — 설계 §8.1)
    /// </summary>
    private sealed class ProgressCollector : IProgress<FrameCatalogProgress>
    {
        private readonly List<FrameCatalogProgress> _items = new();
        public void Report(FrameCatalogProgress value) { lock (_items) _items.Add(value); }
        public FrameCatalogProgress[] Snapshot() { lock (_items) return _items.ToArray(); }
    }

    /// <summary>
    /// T-22: 다운로드 진행 중에 합류한 두 번째 호출자의 **첫 보고**가 진행 중 국면(DownloadingImage)이다.
    /// 종전 줄 세우기에서는 문구가 정체돼 정상 다운로드를 "실패"로 오진했다(설계 §6.3 M1).
    /// </summary>
    [Fact]
    public async Task Late_Joiner_Gets_Replay_Of_Last_Progress()
    {
        var repo = new CountingFrameRepository
        {
            DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") }
        };
        var (svc, release, _) = MakeHeldService(repo);

        var collectorA = new ProgressCollector();
        var callA = svc.GetDefaultFramesAsync(CancellationToken.None, collectorA);

        // 고정 Delay는 부하·콜드 JIT에서 QueryingServer에 머물러 플레이키가 된다 →
        // "다운로드 국면 도달"을 실제로 관측할 때까지 짧게 폴링한다.
        await WaitForPhaseAsync(collectorA, FrameCatalogPhase.DownloadingImage);

        var collectorB = new ProgressCollector();
        var callB = svc.GetDefaultFramesAsync(CancellationToken.None, collectorB);

        var firstOfB = collectorB.Snapshot();
        Assert.NotEmpty(firstOfB);
        Assert.Equal(FrameCatalogPhase.DownloadingImage, firstOfB[0].Phase);

        release.SetResult();
        await callA;
        await callB;
    }

    /// <summary>T-23: 보고 순서가 로컬 확인 → 서버 조회 → 이미지 (1/2) → (2/2) → 완료다.</summary>
    [Fact]
    public async Task Progress_Reports_Local_Then_Server_Then_Downloads()
    {
        var repo = new CountingFrameRepository
        {
            DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") }
        };
        var svc = MakeService(repo);
        var collector = new ProgressCollector();

        await svc.GetDefaultFramesAsync(CancellationToken.None, collector);

        var reports = collector.Snapshot();
        AssertOrderedSubsequence(reports, new[]
        {
            new FrameCatalogProgress(FrameCatalogPhase.ResolvingLocal),
            new FrameCatalogProgress(FrameCatalogPhase.QueryingServer),
            new FrameCatalogProgress(FrameCatalogPhase.DownloadingImage, 1, 2),
            new FrameCatalogProgress(FrameCatalogPhase.DownloadingImage, 2, 2),
            new FrameCatalogProgress(FrameCatalogPhase.Completed),
        });
    }

    /// <summary>T-24: (n/m)의 분모에서 캐시 히트를 제외한다 — 이미 있는 프레임을 "내려받는 중"으로 세지 않는다.</summary>
    [Fact]
    public async Task Progress_Counter_Excludes_Cache_Hits()
    {
        _store.CacheFromDb(DbFrame("f1"), new byte[] { 9 });   // f1은 로컬 캐시 히트

        var repo = new CountingFrameRepository
        {
            DefaultFrames = new List<FrameTemplate> { DbFrame("f1"), DbFrame("f2") }
        };
        var svc = MakeService(repo);
        var collector = new ProgressCollector();

        await svc.GetDefaultFramesAsync(CancellationToken.None, collector);

        var downloads = collector.Snapshot()
            .Where(p => p.Phase == FrameCatalogPhase.DownloadingImage)
            .ToArray();
        Assert.Single(downloads);
        Assert.Equal(1, downloads[0].Total);
        Assert.Equal(1, downloads[0].Index);
    }

    /// <summary>수집기가 해당 국면을 관측할 때까지 폴링(기본 2초). 고정 Delay 가정의 플레이키를 없앤다.</summary>
    private static async Task WaitForPhaseAsync(
        ProgressCollector collector, FrameCatalogPhase phase, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (collector.Snapshot().Any(p => p.Phase == phase)) return;
            await Task.Delay(10);
        }
        Assert.Fail($"{timeoutMs}ms 안에 {phase} 국면 보고가 관측되지 않았다. 실제: "
            + string.Join(" → ", collector.Snapshot().Select(p => p.Phase.ToString())));
    }

    /// <summary>
    /// it20 N1: 새 패스를 시작하는 호출자에게 **이전 패스의 마지막 국면이 replay되지 않는다**.
    /// `_lastProgress`를 리셋하지 않으면 패스 종료 시 남은 `Completed`("프레임 목록을 정리하는 중…")가
    /// 다음 진입의 **첫 문구**로 재생된다 — 홈 왕복 후 재진입 때마다 거짓 문구가 보인다.
    /// </summary>
    [Fact]
    public async Task New_Pass_Does_Not_Replay_Previous_Completed()
    {
        var repo = new CountingFrameRepository { DefaultFrames = new List<FrameTemplate> { DbFrame("f1") } };
        var svc = MakeService(repo);

        var first = new ProgressCollector();
        await svc.GetDefaultFramesAsync(CancellationToken.None, first);
        Assert.Contains(FrameCatalogPhase.Completed, first.Snapshot().Select(p => p.Phase));  // 전제 확인

        // 두 번째 진입: 앞 패스는 끝났으므로 새 패스가 시작된다.
        var second = new ProgressCollector();
        await svc.GetDefaultFramesAsync(CancellationToken.None, second);

        var firstReportOfSecond = second.Snapshot()[0];
        Assert.NotEqual(FrameCatalogPhase.Completed, firstReportOfSecond.Phase);
        Assert.Equal(FrameCatalogPhase.ResolvingLocal, firstReportOfSecond.Phase);
    }

    /// <summary>수집된 보고 안에 기대 보고들이 **이 순서대로** 등장하는지 확인(중간의 replay·중복 보고는 허용).</summary>
    private static void AssertOrderedSubsequence(
        FrameCatalogProgress[] actual, FrameCatalogProgress[] expected)
    {
        int cursor = 0;
        foreach (var want in expected)
        {
            while (cursor < actual.Length && !actual[cursor].Equals(want)) cursor++;
            Assert.True(cursor < actual.Length,
                $"기대 보고 {want.Phase}({want.Index}/{want.Total}) 를 순서대로 찾지 못함. 실제: "
                + string.Join(" → ", actual.Select(a => $"{a.Phase}({a.Index}/{a.Total})")));
            cursor++;
        }
    }

    [Fact]
    public async Task User_Frames_Loaded_From_Local_Not_Db()
    {
        _store.SaveLocal(
            new FrameTemplate { Name = "mine", ImageSize = new ImageSize { Width = 100, Height = 100 },
                                Slots = { new Slot { Index = 0, X = 0, Y = 0, Width = 10, Height = 10 } } },
            new byte[] { 1 }, ownerName: "alice");

        var repo = new CountingFrameRepository();
        var svc = MakeService(repo);

        var user = await svc.GetUserFramesAsync("alice");

        Assert.Single(user);
        Assert.Equal("mine", user[0].Name);
        Assert.Equal(0, repo.DefaultCalls); // user 로딩은 DB 무관
    }
}
