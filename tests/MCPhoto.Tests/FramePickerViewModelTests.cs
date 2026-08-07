using System.ComponentModel;
using System.IO;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>
/// it15 F2: "기존 프레임 불러오기" 선택 모달의 목록 VM.
/// 모달을 오버레이로 설계했으므로(F2-D1) 모든 로직이 창 없이 검증된다 — Window를 new 하지 않는다.
/// FrameCatalogService는 인터페이스가 아니므로 스텁 repo/localStore를 주입한 실제 인스턴스를 쓴다
/// (FrameCatalogServiceTests와 동일 패턴 — 신규 인터페이스 추출 금지).
/// </summary>
[Collection(FallbackCacheCollection.Name)]   // it20 N2: 공유 fallback 캐시 경로 경합 제거
public class FramePickerViewModelTests : IDisposable
{
    private sealed class EmptyRepo : IFrameRepository
    {
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveMineAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>로컬 조회가 실패하는 저장소(카탈로그 예외 → 안내 문구 경로 검증용).</summary>
    private sealed class ThrowingLocalStore : ILocalFrameStore
    {
        public FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId) => frame;
        public FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId) => frame;
        public IReadOnlyList<FrameTemplate> LoadPublic() => throw new IOException("로컬 프레임 폴더 접근 실패");
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail) => new List<FrameTemplate>();
        public bool DeleteLocal(FrameTemplate frame) => true;
        public IReadOnlySet<string> PublicFrameNames() => new HashSet<string>();
        public IReadOnlySet<string> UserFrameNames(string ownerEmail) => new HashSet<string>();
        public IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail) => Array.Empty<LocalFrameEntry>();
    }

    private readonly string _root;
    private readonly LocalFrameStore _store;

    public FramePickerViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_pick_{Guid.NewGuid():N}");
        _store = new LocalFrameStore(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 무시 */ }
    }

    private static byte[] Png => new byte[] { 1, 2, 3, 4 };

    private static FrameTemplate Frame(string name) => new()
    {
        Name = name,
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = { new Slot { Index = 0, X = 10, Y = 20, Width = 300, Height = 400 } }
    };

    private FramePickerViewModel MakeVm(ILocalFrameStore? localStore = null)
        => new(new FrameCatalogService(new EmptyRepo(), localStore ?? _store));

    [Fact]
    public async Task LoadAsync_Includes_Public_And_Own_User_Frames()
    {
        // D1: 프레임 선택 화면과 동일한 소스(공용 + 로그인 계정 개인 로컬).
        _store.SaveDefaultFrame(Frame("공용1"), Png, dbId: null);
        _store.SaveDefaultFrame(Frame("공용2"), Png, dbId: null);
        _store.SaveUserFrame(Frame("내것"), Png, ownerEmail: "u1", dbId: null);
        var vm = MakeVm();

        await vm.LoadAsync("u1");

        Assert.Equal(3, vm.Frames.Count);
        Assert.Contains(vm.Frames, f => f.Name == "내것");
        Assert.Empty(vm.EmptyNotice);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_Without_UserId_Loads_Public_Only()
    {
        // D2: 게스트/미로그인 스코프 — 개인 프레임 미포함(타인 것도 LoadUser 접두로 자동 제외).
        _store.SaveDefaultFrame(Frame("공용1"), Png, dbId: null);
        _store.SaveUserFrame(Frame("내것"), Png, ownerEmail: "u1", dbId: null);
        var vm = MakeVm();

        await vm.LoadAsync(userId: null);

        Assert.Single(vm.Frames);
        Assert.Equal("공용1", vm.Frames[0].Name);
    }

    [Fact]
    public async Task LoadAsync_Toggles_IsLoading()
    {
        // D3: 로딩 표시가 켜졌다 꺼진다(오버레이의 "불러오는 중" 안내 전제).
        _store.SaveDefaultFrame(Frame("공용1"), Png, dbId: null);
        var vm = MakeVm();
        var seen = new List<bool>();
        void OnChanged(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.IsLoading)) seen.Add(vm.IsLoading);
        }
        vm.PropertyChanged += OnChanged;
        try
        {
            Assert.False(vm.IsLoading);      // 호출 전
            await vm.LoadAsync("u1");
        }
        finally { vm.PropertyChanged -= OnChanged; } // 구독 해제 경로

        Assert.Equal(new[] { true, false }, seen);
        Assert.False(vm.IsLoading);          // 호출 후
    }

    [Fact]
    public async Task LoadAsync_Failure_Sets_EmptyNotice()
    {
        // D4: 후보를 못 얻으면 안내를 남긴다(ListBox 대신 텍스트 표시).
        // ⚠️ FrameCatalogService는 항상 최소 1개(fallback)를 돌려주므로 "목록이 정말 비는" 경로는
        //    실전에서 도달하지 않는다 → 도달 가능한 실패 경로(로컬 조회 예외)로 안내를 검증한다.
        var vm = MakeVm(new ThrowingLocalStore());

        await vm.LoadAsync("u1");

        Assert.NotEmpty(vm.EmptyNotice);
        Assert.Empty(vm.Frames);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void HasSelection_Follows_SelectedFrame()
    {
        // D5: 확인 버튼 활성 조건. SelectedFrame 변경이 HasSelection 알림을 동반해야 한다.
        var vm = MakeVm();
        var notified = new List<string?>();
        void OnChanged(object? _, PropertyChangedEventArgs e) => notified.Add(e.PropertyName);
        vm.PropertyChanged += OnChanged;
        try
        {
            Assert.False(vm.HasSelection);

            vm.SelectedFrame = Frame("고른것");
            Assert.True(vm.HasSelection);
            Assert.Contains(nameof(vm.HasSelection), notified);

            notified.Clear();
            vm.SelectedFrame = null;
            Assert.False(vm.HasSelection);
            Assert.Contains(nameof(vm.HasSelection), notified);
        }
        finally { vm.PropertyChanged -= OnChanged; }
    }

    [Fact]
    public async Task Reset_Clears_Selection_And_List()
    {
        // D6: 모달을 닫을 때 상태 초기화 — 재오픈 시 이전 선택이 남지 않는다.
        _store.SaveDefaultFrame(Frame("공용1"), Png, dbId: null);
        var vm = MakeVm();
        await vm.LoadAsync("u1");
        vm.SelectedFrame = vm.Frames[0];

        vm.Reset();

        Assert.Empty(vm.Frames);
        Assert.Null(vm.SelectedFrame);
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.EmptyNotice);
    }

    [Fact]
    public async Task LoadAsync_Honors_CancellationToken()
    {
        // D7: 취소는 예외를 전파하지 않고 조용히 종료하며 로딩 표시를 반드시 내린다.
        _store.SaveDefaultFrame(Frame("공용1"), Png, dbId: null);
        var vm = MakeVm();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await vm.LoadAsync("u1", cts.Token); // 예외 없이 종료

        Assert.False(vm.IsLoading);
    }
}
