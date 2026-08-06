using System.IO;
using System.Linq;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it11 #13: 전체 재촬영 게이트(설정 off 미노출·횟수 제한 도달 차단). 컷별 재촬영은 후속 이터레이션(제외).
/// </summary>
public class CutSelectViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static FrameTemplate MakeFrame(int slots)
    {
        var f = new FrameTemplate { Id = "f1", Name = "test" };
        for (int i = 0; i < slots; i++)
            f.Slots.Add(new Slot { Index = i, X = i * 10, Y = 0, Width = 100, Height = 133 });
        return f;
    }

    /// <summary>재촬영 설정을 지정해 세션·셸을 구성한 CutSelectViewModel 생성.</summary>
    private static (CutSelectViewModel vm, SessionContext session) MakeVm(bool retakeEnabled, int retakeLimit)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"csvm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.RetakeEnabled = retakeEnabled;
        s.RetakeLimit = retakeLimit;

        var session = new SessionContext();
        session.Capture.Begin(MakeFrame(2), 6);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return (new CutSelectViewModel(shell), session);
    }

    [Fact]
    public void RetakeDisabled_Hides_And_Blocks_FullRetake()
    {
        var (vm, _) = MakeVm(retakeEnabled: false, retakeLimit: 3);
        Assert.False(vm.RetakeEnabled);   // 버튼 미노출
        Assert.False(vm.CanFullRetake);   // 방어(설정 off면 항상 불가)
    }

    [Fact]
    public void RetakeEnabled_Allows_FullRetake_Until_Limit()
    {
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 1);
        Assert.True(vm.RetakeEnabled);
        Assert.True(vm.CanFullRetake);    // 0회 소진 → 가능

        session.Capture.BeginFullRetake(); // 1회 소진(limit=1)
        Assert.False(vm.CanFullRetake);    // 도달 → 초과 차단
    }

    [Fact]
    public void RetakeEnabled_Higher_Limit_Allows_Multiple()
    {
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 3);
        Assert.True(vm.CanFullRetake);

        session.Capture.BeginFullRetake();
        session.Capture.BeginFullRetake();
        Assert.True(vm.CanFullRetake);     // 2/3 → 아직 여유

        session.Capture.BeginFullRetake();
        Assert.False(vm.CanFullRetake);    // 3/3 도달 → 차단
    }

    [Fact]
    public async Task Retake_At_Limit_Does_Not_Increment()
    {
        // 방어: limit 도달 후 커맨드를 눌러도 카운터 증가·전이 없음(no-op).
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 1);
        session.Capture.BeginFullRetake(); // 1회 소진(limit=1 도달)
        Assert.Equal(1, session.Capture.FullRetakeCount);

        await vm.RetakeCommand.ExecuteAsync(null);

        Assert.Equal(1, session.Capture.FullRetakeCount); // 증가하지 않음
    }

    // ── 배치 프리뷰: 고른 컷이 어느 슬롯에 들어가는지 실시간 표시 ──

    /// <summary>단색 스틸(썸네일 변환용 최소 데이터).</summary>
    private static CapturedStill MakeStill(byte v = 128)
    {
        const int w = 6, h = 8;
        var px = new byte[w * h * 3];
        Array.Fill(px, v);
        return new CapturedStill { Width = w, Height = h, Pixels = px };
    }

    /// <summary>컷을 촬영한 상태로 진입까지 마친 VM.</summary>
    private static async Task<(CutSelectViewModel vm, SessionContext session)> MakeEnteredVm(int cutCount)
    {
        var (vm, session) = MakeVm(retakeEnabled: false, retakeLimit: 1);
        for (int i = 0; i < cutCount; i++)
            session.Capture.AddCut(MakeStill((byte)(30 * (i + 1))));
        await vm.OnEnterAsync();
        return (vm, session);
    }

    [Fact]
    public async Task SlotPreview_Starts_Empty_With_One_Cell_Per_Slot()
    {
        var (vm, _) = await MakeEnteredVm(cutCount: 3);   // 슬롯 2개(MakeFrame) + 3컷 촬영

        Assert.True(vm.HasSlotPreview);
        Assert.Equal(2, vm.SlotPreviews.Count);                                  // 칸 수 = 슬롯 수
        Assert.All(vm.SlotPreviews, p => Assert.False(p.IsFilled));              // 선택 전엔 전부 빈 칸
        Assert.Equal(new[] { 1, 2 }, vm.SlotPreviews.Select(p => p.Number).ToArray());
    }

    [Fact]
    public async Task SlotPreview_Fills_In_Selection_Order_And_Pulls_Forward_On_Deselect()
    {
        var (vm, _) = await MakeEnteredVm(cutCount: 3);

        vm.ToggleCutCommand.Execute(vm.Cuts[2]);          // 첫 선택 → 첫 슬롯
        Assert.Same(vm.Cuts[2].Image, vm.SlotPreviews[0].Image);
        Assert.False(vm.SlotPreviews[1].IsFilled);

        vm.ToggleCutCommand.Execute(vm.Cuts[0]);          // 둘째 선택 → 둘째 슬롯
        Assert.Same(vm.Cuts[0].Image, vm.SlotPreviews[1].Image);

        vm.ToggleCutCommand.Execute(vm.Cuts[2]);          // 첫 선택 해제 → 뒤 컷이 앞 슬롯으로 당겨짐
        Assert.Same(vm.Cuts[0].Image, vm.SlotPreviews[0].Image);
        Assert.False(vm.SlotPreviews[1].IsFilled);
    }

    [Fact]
    public async Task Preview_Canvas_Falls_Back_To_Slot_Bounds_When_ImageSize_Missing()
    {
        // MakeFrame은 ImageSize를 기록하지 않는다(이상 데이터 방어) → 슬롯 bounding box가 좌표계가 된다.
        var (vm, _) = await MakeEnteredVm(cutCount: 2);

        Assert.Equal(110, vm.CanvasWidth);    // 슬롯1 X=10..110
        Assert.Equal(133, vm.CanvasHeight);
        Assert.Equal(0, vm.SlotPreviews[0].X);
        Assert.Equal(10, vm.SlotPreviews[1].X);
    }

    [Fact]
    public async Task Preview_Cell_Number_Font_Scales_With_Slot()
    {
        var (vm, _) = await MakeEnteredVm(cutCount: 2);

        // 프레임 픽셀 좌표계라 Viewbox 축소 후에도 읽히도록 슬롯 크기에 비례해야 한다.
        Assert.Equal(Math.Max(12, 100 * 0.42), vm.SlotPreviews[0].NumberFontSize, precision: 6);
    }

    [Fact]
    public async Task No_Frame_Hides_Preview()
    {
        var (vm, session) = MakeVm(retakeEnabled: false, retakeLimit: 1);
        session.Capture.Discard();      // 프레임 없음(이상 상태)
        await vm.OnEnterAsync();

        Assert.False(vm.HasSlotPreview);
        Assert.Empty(vm.SlotPreviews);
    }
}
