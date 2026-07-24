using System.IO;
using MCPhoto.App.Services;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>it8 Step 3 (A2): 로컬 우선 로딩 + 파워 캐시. 캐시 히트 시 DB 미조회, 미스 시 다운로드·캐시.</summary>
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
        public bool SupportsUpdateById => true;
        public Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default)
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
