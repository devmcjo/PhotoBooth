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
        public FrameTemplate? Updated { get; private set; }
        public bool? LastReplaceImage { get; private set; }
        /// <summary>update-by-id 지원 여부(테스트별 조정). 기본 레거시 의미로 true.</summary>
        public bool SupportsUpdateById { get; set; } = true;

        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
        {
            Saved = frame;
            return Task.FromResult(frame);
        }
        public Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default)
        {
            Updated = frame;
            LastReplaceImage = replaceImage;
            return Task.FromResult(frame);
        }
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
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

    // ── item2 Step 5: manager 기본 프레임 편집 저장 팝업 + diff 플로우 ──

    /// <summary>DB 공용 기본 프레임(접두 없는 실 DB id, isDefault=true)을 편집 대상으로 로드.</summary>
    private FrameTemplate DbDefaultFrame() => new()
    {
        Id = "GUID-abc", Name = "공용프레임", UserId = null, IsDefault = true,
        ImageUrl = _imagePath, // LoadForEdit가 읽을 로컬 png(File.Exists 성립)
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
    };

    [Fact]
    public async Task Power_Editing_Db_Default_Save_Shows_Prompt_And_Defers()
    {
        var (vm, repo, _, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsDbUpdatePromptVisible);   // 팝업 표시
        Assert.Null(repo.Saved);                    // 아직 미저장(신규 생성 경로 안 탐)
        Assert.Null(repo.Updated);                  // 업데이트도 아직 안 함
    }

    [Fact]
    public async Task SaveLocalOnly_Skips_Db_And_Caches_Locally()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        await vm.SaveCommand.ExecuteAsync(null);    // 팝업 표시

        await vm.SaveLocalOnlyCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(repo.Updated);                  // DB 미호출
        Assert.NotNull(local.SavedFrame);           // 로컬 캐시 갱신
        Assert.Equal("GUID-abc", local.SavedFrame!.Id); // 같은 Id(#dbid 보존)
        Assert.Null(local.SavedOwner);              // 공용 캐시
        Assert.False(vm.IsDbUpdatePromptVisible);
    }

    [Fact]
    public async Task SaveToDb_No_Change_Skips_Db_Call()
    {
        // 원본 그대로 저장(아무 조작 없음) → diff 무변경 → DB 미호출, 로컬만.
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        await vm.SaveCommand.ExecuteAsync(null);

        await vm.SaveToDbCommand.ExecuteAsync(null);

        Assert.Null(repo.Updated);                  // 변경 없음 → DB 미호출
        Assert.NotNull(local.SavedFrame);           // 로컬 캐시는 갱신
        Assert.False(vm.DbUpdateNoticeIsError);
        Assert.Contains("변경 사항이 없어", vm.DbUpdateNotice);
    }

    [Fact]
    public async Task SaveToDb_With_Slot_Change_Updates_Same_Id()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        // 슬롯 변경(스케일) → SlotsChanged=true, 이미지 미변경 → replaceImage=false.
        vm.SlotScalePercent = 80;

        await vm.SaveCommand.ExecuteAsync(null);    // 팝업
        await vm.SaveToDbCommand.ExecuteAsync(null);

        Assert.NotNull(repo.Updated);
        Assert.Equal("GUID-abc", repo.Updated!.Id); // 같은 문서 업데이트
        Assert.Null(repo.Updated.UserId);
        Assert.True(repo.Updated.IsDefault);
        Assert.False(repo.LastReplaceImage);        // 슬롯만 변경 → 이미지 미교체
        Assert.NotNull(local.SavedFrame);
        Assert.Null(local.SavedOwner);
        Assert.False(vm.IsDbUpdatePromptVisible);
    }

    [Fact]
    public async Task SaveToDb_With_Image_Change_Sets_ReplaceImage()
    {
        var (vm, repo, _, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        // 새 이미지 로드(다른 크기) → 이미지 바이트 변경 → replaceImage=true.
        var otherPath = Path.Combine(Path.GetTempPath(), $"mcphoto_other_{Guid.NewGuid():N}.png");
        using (var mat = new OpenCvSharp.Mat(1000, 800, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(50)))
            OpenCvSharp.Cv2.ImWrite(otherPath, mat);
        try
        {
            Assert.True(vm.LoadImage(otherPath));

            await vm.SaveCommand.ExecuteAsync(null);
            await vm.SaveToDbCommand.ExecuteAsync(null);

            Assert.NotNull(repo.Updated);
            Assert.True(repo.LastReplaceImage);     // 이미지 변경 → 교체
        }
        finally { try { File.Delete(otherPath); } catch { /* 무시 */ } }
    }

    [Fact]
    public async Task SaveToDb_When_Not_Supported_Falls_Back_To_Local_With_Warning()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        repo.SupportsUpdateById = false;            // 미지원 저장소 가정
        vm.LoadForEdit(DbDefaultFrame());
        vm.SlotScalePercent = 80;                   // 변경 발생

        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SaveToDbCommand.ExecuteAsync(null);

        Assert.Null(repo.Updated);                  // DB 미호출
        Assert.NotNull(local.SavedFrame);           // 로컬만 적용
        Assert.True(vm.DbUpdateNoticeIsError);
    }

    [Fact]
    public async Task CancelDbUpdatePrompt_Keeps_Editing_No_Save()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        await vm.SaveCommand.ExecuteAsync(null);

        vm.CancelDbUpdatePromptCommand.Execute(null);

        Assert.False(vm.IsDbUpdatePromptVisible);
        Assert.Null(repo.Saved);
        Assert.Null(repo.Updated);
        Assert.Null(local.SavedFrame);              // 저장 없음
    }
}
