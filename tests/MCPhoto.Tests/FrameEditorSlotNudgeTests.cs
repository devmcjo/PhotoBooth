using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// 슬롯 키보드 이동(설계 §12 · T27). 마우스가 튀어 미세 조정이 어렵다는 요구에서 나온 기능.
/// <para>
/// 이동 규칙은 VM 순수 로직으로 검증한다. "텍스트 입력 중 미개입"은 View의 포커스 가드라
/// 여기서 다루지 않는다(`FrameEditorView.OnEditorPreviewKeyDown`).
/// </para>
/// </summary>
[Collection(FallbackCacheCollection.Name)]
public class FrameEditorSlotNudgeTests : IClassFixture<FrameImageFixture>
{
    private sealed class EmptyRepo : IFrameRepository
    {
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate f, byte[] b, CancellationToken ct = default)
            => Task.FromResult(f);
        public Task<FrameTemplate> SaveMineAsync(FrameTemplate f, byte[] b, CancellationToken ct = default)
            => Task.FromResult(f);
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullLocalStore : ILocalFrameStore
    {
        public FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId) => frame;
        public FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId) => frame;
        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail) => new List<FrameTemplate>();
        public bool DeleteLocal(FrameTemplate frame) => true;
        public IReadOnlySet<string> PublicFrameNames() => new HashSet<string>();
        public IReadOnlySet<string> UserFrameNames(string ownerEmail) => new HashSet<string>();
        public IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail) => Array.Empty<LocalFrameEntry>();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly string _imagePath;

    public FrameEditorSlotNudgeTests(FrameImageFixture fixture) => _imagePath = fixture.PngPath;

    /// <summary>이미지를 불러와 슬롯이 배치된 편집기 VM.</summary>
    private FrameEditorViewModel MakeVm(int slotCount = 4)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Role = UserRole.AdvancedUser, Email = "u1@test.com" });
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"nudge_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var repo = new EmptyRepo();
        var local = new NullLocalStore();
        var vm = new FrameEditorViewModel(shell, repo, local, new FramePickerViewModel(new FrameCatalogService(repo, local)));
        Assert.True(vm.LoadImage(_imagePath));
        vm.SlotCount = slotCount;
        return vm;
    }

    [Fact]
    public void No_Selection_Means_No_Move()
    {
        var vm = MakeVm();
        Assert.Equal(-1, vm.SelectedSlotIndex);     // 초기엔 대상이 없다
        Assert.False(vm.NudgeSelectedSlot(1, 0));
    }

    [Theory]
    [InlineData(1, 0)]    // →
    [InlineData(-1, 0)]   // ←
    [InlineData(0, 1)]    // ↓
    [InlineData(0, -1)]   // ↑
    public void Arrow_Moves_One_Pixel(int dx, int dy)
    {
        var vm = MakeVm();
        vm.SelectedSlotIndex = 0;
        var before = vm.Slots[0];

        Assert.True(vm.NudgeSelectedSlot(dx, dy));

        Assert.Equal(before.X + dx, vm.Slots[0].X);
        Assert.Equal(before.Y + dy, vm.Slots[0].Y);
    }

    [Fact]
    public void Shift_Step_Is_Ten_Pixels()
    {
        Assert.Equal(1, FrameEditorViewModel.NudgeStep);
        Assert.Equal(10, FrameEditorViewModel.NudgeStepFast);

        var vm = MakeVm();
        vm.SelectedSlotIndex = 0;
        var before = vm.Slots[0];

        vm.NudgeSelectedSlot(FrameEditorViewModel.NudgeStepFast, 0);

        Assert.Equal(before.X + 10, vm.Slots[0].X);
    }

    /// <summary>요구: "크기는 일관되게" — 이동은 위치만 바꾼다.</summary>
    [Fact]
    public void Nudge_Never_Changes_Size()
    {
        var vm = MakeVm();
        vm.SelectedSlotIndex = 0;
        var before = vm.Slots[0];

        vm.NudgeSelectedSlot(7, -3);

        Assert.Equal(before.Width, vm.Slots[0].Width);
        Assert.Equal(before.Height, vm.Slots[0].Height);
    }

    [Fact]
    public void Nudge_Is_Clamped_To_Frame_Bounds()
    {
        var vm = MakeVm();
        vm.SelectedSlotIndex = 0;

        // 왼쪽·위로 크게 밀어도 프레임 밖으로 나가지 않는다.
        for (int i = 0; i < 500; i++) vm.NudgeSelectedSlot(-10, -10);

        Assert.Equal(0, vm.Slots[0].X);
        Assert.Equal(0, vm.Slots[0].Y);

        // 오른쪽·아래도 마찬가지(우하단 경계에서 멈춘다).
        for (int i = 0; i < 1000; i++) vm.NudgeSelectedSlot(10, 10);

        Assert.Equal(vm.FrameWidth, vm.Slots[0].X + vm.Slots[0].Width);
        Assert.Equal(vm.FrameHeight, vm.Slots[0].Y + vm.Slots[0].Height);
    }

    [Fact]
    public void Tab_Cycles_Selection_Forward_And_Backward()
    {
        var vm = MakeVm(slotCount: 3);

        Assert.True(vm.SelectAdjacentSlot(backward: false));
        Assert.Equal(0, vm.SelectedSlotIndex);       // 미선택 → 첫 슬롯

        vm.SelectAdjacentSlot(backward: false);
        vm.SelectAdjacentSlot(backward: false);
        Assert.Equal(2, vm.SelectedSlotIndex);

        vm.SelectAdjacentSlot(backward: false);
        Assert.Equal(0, vm.SelectedSlotIndex);       // 끝에서 처음으로 순환

        vm.SelectAdjacentSlot(backward: true);
        Assert.Equal(2, vm.SelectedSlotIndex);       // 역방향도 순환
    }

    [Fact]
    public void Backward_From_Nothing_Selects_Last()
    {
        var vm = MakeVm(slotCount: 3);
        Assert.True(vm.SelectAdjacentSlot(backward: true));
        Assert.Equal(2, vm.SelectedSlotIndex);
    }

    /// <summary>슬롯 개수를 줄이면 선택이 범위를 벗어날 수 있다 — 남겨두면 이동이 엉뚱한 곳을 건드린다.</summary>
    [Fact]
    public void Selection_Is_Cleared_When_Out_Of_Range()
    {
        var vm = MakeVm(slotCount: 6);
        vm.SelectedSlotIndex = 5;

        vm.SlotCount = 2;
        vm.ClampSlotSelection();

        Assert.Equal(-1, vm.SelectedSlotIndex);
        Assert.False(vm.NudgeSelectedSlot(1, 0));
    }

    [Fact]
    public void Nudge_Affects_Only_Selected_Slot()
    {
        var vm = MakeVm(slotCount: 4);
        vm.SelectedSlotIndex = 2;
        var others = vm.Slots.Where((_, i) => i != 2).Select(s => (s.X, s.Y)).ToList();

        vm.NudgeSelectedSlot(5, 5);

        var after = vm.Slots.Where((_, i) => i != 2).Select(s => (s.X, s.Y)).ToList();
        Assert.Equal(others, after);
    }
}
