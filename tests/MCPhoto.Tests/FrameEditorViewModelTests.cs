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

    private (FrameEditorViewModel vm, CapturingFrameRepository repo, SessionContext session) MakeVm()
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Password = "pw", Role = UserRole.User });
        var repo = new CapturingFrameRepository();
        var vm = new FrameEditorViewModel(MakeShell(session), repo);
        return (vm, repo, session);
    }

    [Fact]
    public void SlotCountOptions_Is_One_To_Six()
    {
        var (vm, _, _) = MakeVm();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, vm.SlotCountOptions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public void SlotCount_Change_Reflects_In_Slots(int count)
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.LoadImage(_imagePath)); // FrameWidth/Height 세팅 → ArrangeSlots 가능

        vm.SlotCount = count;

        Assert.Equal(count, vm.Slots.Count);
    }

    [Fact]
    public async Task Save_Persists_Selected_Slot_Count()
    {
        var (vm, repo, _) = MakeVm();
        Assert.True(vm.LoadImage(_imagePath));

        vm.SlotCount = 6; // B9: 6 선택이 clobber 없이 유지되어야
        Assert.Equal(6, vm.Slots.Count);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(repo.Saved);
        Assert.Equal(6, repo.Saved!.Slots.Count); // 저장 문서에 6개 전달
    }
}
