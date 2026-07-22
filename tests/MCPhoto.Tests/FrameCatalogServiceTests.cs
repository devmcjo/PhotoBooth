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
        public Task DeleteAsync(string frameId, CancellationToken ct = default) => Task.CompletedTask;
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
