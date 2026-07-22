using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it7 Step 1 (B9): 슬롯 개수 값 기반 바인딩 회귀. SlotCount 변경이 Slots 개수에 정확 반영되고
/// Save가 그 개수만큼 저장하는지(초기화 clobber로 1개 되던 버그 방지) VM 레벨로 고정.
/// </summary>
public class FrameEditorViewModelTests : IDisposable
{
    private sealed class CapturingFrameRepository : IFrameRepository
    {
        public FrameTemplate? Saved { get; private set; }
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
        {
            Saved = frame;
            return Task.FromResult(frame);
        }
        public Task DeleteAsync(string frameId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingLocalStore : ILocalFrameStore
    {
        public FrameTemplate? SavedFrame { get; private set; }
        public string? SavedOwner { get; private set; }
        public FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName)
        {
            SavedFrame = frame;
            SavedOwner = ownerName;
            return frame;
        }
        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerName) => new List<FrameTemplate>();
        public FrameTemplate CacheFromDb(FrameTemplate frame, byte[] png) => frame;
        public bool DeleteLocal(FrameTemplate frame) => true;
        public IReadOnlySet<string> PublicFrameNames() => new HashSet<string>();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly string _imagePath;

    public FrameEditorViewModelTests()
    {
        // OpenCV가 읽을 실제 PNG 생성(1200×1600, LoadImage 경로용).
        _imagePath = Path.Combine(Path.GetTempPath(), $"mcphoto_frame_{Guid.NewGuid():N}.png");
        using var mat = new OpenCvSharp.Mat(1600, 1200, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(200));
        OpenCvSharp.Cv2.ImWrite(_imagePath, mat);
    }

    public void Dispose()
    {
        try { if (File.Exists(_imagePath)) File.Delete(_imagePath); } catch { /* 무시 */ }
    }

    private static AppShellViewModel MakeShell(SessionContext session)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"fe_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    }

    private (FrameEditorViewModel vm, CapturingFrameRepository repo, CapturingLocalStore local, SessionContext session) MakeVm(UserRole role = UserRole.User)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Password = "pw", Role = role });
        var repo = new CapturingFrameRepository();
        var local = new CapturingLocalStore();
        var vm = new FrameEditorViewModel(MakeShell(session), repo, local);
        return (vm, repo, local, session);
    }

    [Fact]
    public void SlotCountOptions_Is_One_To_Six()
    {
        var (vm, _, _, _) = MakeVm();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, vm.SlotCountOptions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public void SlotCount_Change_Reflects_In_Slots(int count)
    {
        var (vm, _, _, _) = MakeVm();
        Assert.True(vm.LoadImage(_imagePath)); // FrameWidth/Height 세팅 → ArrangeSlots 가능

        vm.SlotCount = count;

        Assert.Equal(count, vm.Slots.Count);
    }

    [Fact]
    public async Task User_Save_Persists_Locally_With_Six_Slots()
    {
        // it8 A2: user는 로컬 전용 저장(DB 미호출). B9: 6 선택이 clobber 없이 유지.
        var (vm, repo, local, _) = MakeVm(UserRole.User);
        Assert.True(vm.LoadImage(_imagePath));

        vm.SlotCount = 6;
        Assert.Equal(6, vm.Slots.Count);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);                    // user는 DB 미저장
        Assert.NotNull(local.SavedFrame);
        Assert.Equal("u1", local.SavedOwner);       // 계정명 prefix
        Assert.Equal(6, local.SavedFrame!.Slots.Count);
    }

    [Fact]
    public async Task Power_Save_Persists_To_Db_And_Local_Cache()
    {
        // it8 A2: 파워는 DB(isDefault=true) + 로컬 캐시(ownerName=null).
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.SlotCount = 6;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.IsDefault);
        Assert.Null(repo.Saved.UserId);
        Assert.Equal(6, repo.Saved.Slots.Count);
        Assert.NotNull(local.SavedFrame);           // 로컬 캐시도
        Assert.Null(local.SavedOwner);              // 파워 캐시는 ownerName null(frameId 기반)
    }
}
