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
/// it7 Step 1 (B9): 슬롯 개수 값 기반 바인딩 회귀. SlotCount 변경이 Slots 개수에 정확 반영되고
/// Save가 그 개수만큼 저장하는지(초기화 clobber로 1개 되던 버그 방지) VM 레벨로 고정.
/// </summary>
public class FrameEditorViewModelTests : IClassFixture<FrameImageFixture>
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
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingLocalStore : ILocalFrameStore
    {
        public FrameTemplate? SavedFrame { get; private set; }
        public string? SavedOwner { get; private set; }
        /// <summary>공용 이름 집합(사본 이름 충돌 시나리오용 주입).</summary>
        public HashSet<string> PublicNames { get; } = new(StringComparer.Ordinal);
        /// <summary>개인 프레임 목록(계정별, 사본 이름 충돌 시나리오용 주입).</summary>
        public Dictionary<string, List<FrameTemplate>> UserFrames { get; } = new(StringComparer.Ordinal);

        public FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName)
        {
            SavedFrame = frame;
            SavedOwner = ownerName;
            return frame;
        }
        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerName)
            => UserFrames.TryGetValue(ownerName, out var list) ? list : new List<FrameTemplate>();
        public FrameTemplate CacheFromDb(FrameTemplate frame, byte[] png) => frame;
        public bool DeleteLocal(FrameTemplate frame) => true;
        public IReadOnlySet<string> PublicFrameNames() => PublicNames;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>진단용: VM이 catch로 삼킨 예외(LoadForEdit/LoadImage)를 테스트에서 볼 수 있게 한다.</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<FrameEditorViewModel>
    {
        public List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add($"{logLevel}: {formatter(state, exception)} | {exception}");
    }

    /// <summary>
    /// OpenCV가 읽을 실제 PNG(1200×1600). <see cref="FrameImageFixture"/>가 클래스 단위로 1회 만들고
    /// 읽기 가능까지 확인한 파일이다 — 메서드마다 재생성하면 쓰기 직후 읽기가 반복되어 공유 위반으로
    /// 간헐 실패한다(원인·경위는 <see cref="TestImageFile"/> 주석).
    /// </summary>
    private readonly string _imagePath;
    private readonly CapturingLogger _vmLog = new();

    public FrameEditorViewModelTests(FrameImageFixture fixture) => _imagePath = fixture.PngPath;

    private static AppShellViewModel MakeShell(SessionContext session)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"fe_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    }

    private (FrameEditorViewModel vm, CapturingFrameRepository repo, CapturingLocalStore local, SessionContext session) MakeVm(UserRole role = UserRole.User)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Role = role });
        var repo = new CapturingFrameRepository();
        var local = new CapturingLocalStore();
        var picker = new FramePickerViewModel(new FrameCatalogService(repo, local));
        var vm = new FrameEditorViewModel(MakeShell(session), repo, local, picker, _vmLog);
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
        // it15 C3 무회귀: 파워의 **신규 생성** 저장은 F1 이후에도 서버에 등록된다(공용 기본 프레임 배포 경로).
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

    // ── it15 F1: 프레임 편집은 로컬 전용(DB 업데이트 팝업·diff 경로 폐지) + fork 저장 ──

    /// <summary>DB 공용 기본 프레임(접두 없는 실 DB id, isDefault=true)을 편집 대상으로 로드.</summary>
    private FrameTemplate DbDefaultFrame() => new()
    {
        Id = "GUID-abc", Name = "공용프레임", UserId = null, IsDefault = true,
        ImageUrl = _imagePath, // LoadForEdit가 읽을 로컬 png(File.Exists 성립)
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
    };

    [Fact]
    public async Task Power_Editing_Db_Default_Saves_Local_Only_With_Fork_Name()
    {
        // C1: 팝업 없이 즉시 저장. DB 미호출 + 공용 스코프 + #dbid 미기록(Id="") + 이름은 "{원본} 사본".
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());

        Assert.Equal("공용프레임 사본", vm.FrameName);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);                        // 서버 미호출(로컬 전용)
        Assert.NotNull(local.SavedFrame);
        Assert.Null(local.SavedOwner);                  // 공용 스코프 유지(F1-D5)
        Assert.Equal(string.Empty, local.SavedFrame!.Id); // #dbid 미기록 → 서버 문서와 연결 끊김
    }

    [Fact]
    public async Task User_Editing_Own_Local_Overwrites_Same_Name()
    {
        // C2: 본인 로컬(local: 접두) 편집은 fork 아님 — 이름 그대로 같은 파일 덮어쓰기.
        var (vm, repo, local, _) = MakeVm(UserRole.User);
        vm.LoadForEdit(new FrameTemplate
        {
            Id = "local:u1_내프레임", Name = "내프레임", UserId = "u1", IsDefault = false,
            ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 },
            Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
        });

        Assert.Equal("내프레임", vm.FrameName);          // "사본" 접미 없음

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Equal("u1", local.SavedOwner);           // 개인 스코프
        Assert.Equal("내프레임", local.SavedFrame!.Name);
    }

    [Fact]
    public async Task Fork_Save_Blocked_When_Name_Equals_Source_In_Public_Scope()
    {
        // C4: 공용 스코프 fork에서 원본과 같은 이름은 원본 파일을 덮어쓰므로 차단.
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        vm.LoadForEdit(DbDefaultFrame());
        // 전제 확인: LoadForEdit가 완주해야 아래 단언이 '이름 충돌' 가드를 검사한다. 이미지 로드가
        // 조용히 실패하면 Save가 "슬롯이 겹치거나..."로 먼저 빠져 원인이 가려지므로 여기서 잡는다.
        Assert.True(vm.FrameImage is not null && vm.Slots.Count == 4,
            $"LoadForEdit 미완료: status='{vm.StatusMessage}', image={(vm.FrameImage is null ? "null" : "ok")}, " +
            $"slots={vm.Slots.Count}, w={vm.FrameWidth}, h={vm.FrameHeight}, " +
            $"fileExists={File.Exists(_imagePath)}, log=[{string.Join(" ;; ", _vmLog.Entries)}]");
        vm.FrameName = "공용프레임"; // 원본 이름으로 되돌림

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);                  // 저장 중단
        Assert.Contains("원본과 같은 이름", vm.StatusMessage);
    }

    [Fact]
    public void SaveScopeNotice_Reflects_Scope()
    {
        // C6: 배너(정책)와 별개로 이번 저장의 실제 결과를 안내.
        var (powerNew, _, _, _) = MakeVm(UserRole.Admin);
        Assert.Contains("서버에 등록", powerNew.SaveScopeNotice);

        var (powerFork, _, _, _) = MakeVm(UserRole.Admin);
        powerFork.LoadForEdit(DbDefaultFrame());
        Assert.Contains("원본은 그대로", powerFork.SaveScopeNotice);

        var (userNew, _, _, _) = MakeVm(UserRole.User);
        Assert.Contains("내 프레임", userNew.SaveScopeNotice);
    }

    [Fact]
    public void IsCreateMode_Gates_LocalOnly_Banner()
    {
        // it15 F1-D1(정정): "해당 PC에서만" 배너는 기존 프레임 수정 시에만 노출한다(배너 Visibility = !IsCreateMode).
        // 신규 생성은 기존 로직(power=서버 등록 / user=개인 로컬) 그대로라 배너 문구가 사실과 어긋난다.

        // ① 신규 생성(power) → 배너 숨김. SaveScopeNotice가 "서버에 등록"을 안내하므로 모순이 없다.
        var (powerNew, _, _, _) = MakeVm(UserRole.Admin);
        Assert.True(powerNew.IsCreateMode);
        Assert.Contains("서버에 등록", powerNew.SaveScopeNotice);

        // ② 기존 프레임 수정(power, DB 기본 → fork) → 배너 노출.
        var (powerEdit, _, _, _) = MakeVm(UserRole.Admin);
        powerEdit.LoadForEdit(DbDefaultFrame());
        Assert.False(powerEdit.IsCreateMode);

        // ③ 기존 프레임 수정(user, 본인 로컬 → 덮어쓰기) → 배너 노출.
        var (userEdit, _, _, _) = MakeVm(UserRole.User);
        userEdit.LoadForEdit(new FrameTemplate
        {
            Id = "local:u1_내프레임", Name = "내프레임", UserId = "u1",
            ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 },
            Slots = SlotLayout.AutoArrange(2, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
        });
        Assert.False(userEdit.IsCreateMode);

        // ④ F2로 카탈로그 프레임을 불러온 세션은 정체성이 "새 프레임"(fork 저장 → 원본 불변) → 배너 계속 숨김.
        var (picked, _, _, _) = MakeVm(UserRole.Admin);
        Assert.True(picked.ApplyPickedFrame(DbDefaultFrame()));
        Assert.True(picked.IsCreateMode);
    }

    [Fact]
    public async Task SaveScopeNotice_Warns_Before_Save_When_Public_Name_Has_Underscore()
    {
        // §3.4: 공용 스코프에서 이름에 '_'가 있으면 저장은 되지만 LoadPublic('_'=user 접두)에서 탈락한다.
        // 저장 직후 StatusMessage는 화면 전환으로 읽을 수 없으므로 저장 전 캡션에서 경고해야 한다.
        var (vm, _, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.FrameName = "내_프레임";
        Assert.Contains("'_'가 있어", vm.SaveScopeNotice);

        vm.FrameName = "내프레임";
        Assert.DoesNotContain("'_'가 있어", vm.SaveScopeNotice);

        // user 스코프는 파일명이 '{계정}_{이름}'이라 '_'가 문제되지 않는다 → 경고 없음.
        var (userVm, _, _, _) = MakeVm(UserRole.User);
        Assert.True(userVm.LoadImage(_imagePath));
        userVm.FrameName = "내_프레임";
        Assert.DoesNotContain("'_'가 있어", userVm.SaveScopeNotice);

        // 저장은 차단되지 않는다(비차단 경고).
        vm.FrameName = "내_프레임";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(local.SavedFrame);
    }

    [Fact]
    public void Fork_Name_Avoids_Existing_Names_In_Scope()
    {
        // 사본 이름은 같은 스코프의 기존 이름과 충돌하지 않는다(power=공용 집합).
        var (vm, _, local, _) = MakeVm(UserRole.Admin);
        local.PublicNames.Add("공용프레임 사본");

        vm.LoadForEdit(DbDefaultFrame());

        Assert.Equal("공용프레임 사본 2", vm.FrameName);
    }

    // ── it15 F2: 기존 프레임 불러오기(이미지·슬롯 메모리 복사, 원본 불변) ──

    /// <summary>지정 크기의 실제 이미지 파일을 만든다(OpenCV 디코드 경로 검증용). 호출측이 삭제 책임.</summary>
    private static string MakeImageFile(int width, int height, string extension)
        => TestImageFile.CreateInTemp(width, height, extension, gray: 160);

    [Fact]
    public void ApplyPickedFrame_Copies_Slots_And_Suggests_Copy_Name()
    {
        // C7: 축소 없는 크기(1200×1600) → 슬롯 좌표가 원본과 일치, 이름은 사본, 생성 모드 유지.
        var (vm, _, _, _) = MakeVm(UserRole.User);
        var src = new FrameTemplate
        {
            Id = "bundle:classic", Name = "클래식", IsDefault = true,
            ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 },
            Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
        };

        Assert.True(vm.ApplyPickedFrame(src));

        Assert.Equal(4, vm.Slots.Count);
        for (int i = 0; i < src.Slots.Count; i++)
        {
            Assert.Equal(src.Slots[i].X, vm.Slots[i].X);
            Assert.Equal(src.Slots[i].Y, vm.Slots[i].Y);
            Assert.Equal(src.Slots[i].Width, vm.Slots[i].Width);
            Assert.Equal(src.Slots[i].Height, vm.Slots[i].Height);
        }
        Assert.Equal("클래식 사본", vm.FrameName);
        Assert.True(vm.IsCreateMode);          // 편집 모드로 바뀌지 않는다(_isEditing 불변)
        Assert.Equal("새 프레임 만들기", vm.EditorTitle);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void ApplyPickedFrame_Scales_Slots_When_Image_Downscaled()
    {
        // C8(A3 검증): 장변 4000 초과 → LoadImage가 축소, 슬롯도 같은 배율로 보정되어 프레임 안에 들어온다.
        const int srcW = 4500, srcH = 2000;
        var bigPath = MakeImageFile(srcW, srcH, ".png");
        try
        {
            var (vm, _, _, _) = MakeVm(UserRole.User);
            var src = new FrameTemplate
            {
                Id = "bundle:wide", Name = "와이드", IsDefault = true,
                ImageUrl = bigPath,
                ImageSize = new ImageSize { Width = srcW, Height = srcH },
                Slots = { new Slot { Index = 0, X = 900, Y = 400, Width = 900, Height = 1200 } }
            };

            Assert.True(vm.ApplyPickedFrame(src));

            Assert.Equal(4000, vm.FrameWidth);           // 장변 4000으로 축소
            double scale = (double)vm.FrameWidth / srcW;
            var s = Assert.Single(vm.Slots);
            Assert.Equal((int)Math.Round(900 * scale), s.X);
            Assert.Equal((int)Math.Round(400 * scale), s.Y);
            Assert.Equal((int)Math.Round(900 * scale), s.Width);
            Assert.Equal((int)Math.Round(1200 * scale), s.Height);
            Assert.NotEqual(900, s.X);                   // 미보정(원본 좌표 그대로)이 아님
            Assert.True(SlotLayout.IsValid(vm.Slots, vm.FrameWidth, vm.FrameHeight)); // 프레임 이탈 없음
        }
        finally { try { File.Delete(bigPath); } catch { /* 무시 */ } }
    }

    [Fact]
    public void ApplyPickedFrame_Accepts_Jpeg_Source()
    {
        // C9(A7 검증): 번들 프레임은 .jpg일 수 있다 → LoadImage 경유로 PNG 재인코딩이 성공해야 한다.
        var jpgPath = MakeImageFile(1200, 1600, ".jpg");
        try
        {
            var (vm, _, _, _) = MakeVm(UserRole.User);
            var src = new FrameTemplate
            {
                Id = "bundle:jpegframe", Name = "제이펙", IsDefault = true,
                ImageUrl = jpgPath,
                ImageSize = new ImageSize { Width = 1200, Height = 1600 },
                Slots = SlotLayout.AutoArrange(2, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
            };

            Assert.True(vm.ApplyPickedFrame(src));

            Assert.NotNull(vm.FrameImage);   // PNG 재인코딩 성공
            Assert.Equal(2, vm.Slots.Count);
        }
        finally { try { File.Delete(jpgPath); } catch { /* 무시 */ } }
    }

    [Fact]
    public void ApplyPickedFrame_Does_Not_Modify_Source_File()
    {
        // C10(F2-D4 검증): 원본 png 바이트·.slots 파일이 불변이고 새 파일이 생기지 않는다(임시 파일 부재).
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_srcdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var pngPath = Path.Combine(dir, "원본.png");
            TestImageFile.Write(pngPath, 1200, 1600, gray: 90);
            var slotsPath = Path.Combine(dir, "원본.slots");
            File.WriteAllText(slotsPath, "#imagesize=1200,1600\n0,10,20,300,400\n");

            var beforeHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pngPath));
            var beforeSlotsWrite = File.GetLastWriteTimeUtc(slotsPath);
            var beforeFiles = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToArray();

            var (vm, _, _, _) = MakeVm(UserRole.User);
            var src = new FrameTemplate
            {
                Id = "local:원본", Name = "원본", UserId = "u1",
                ImageUrl = pngPath,
                ImageSize = new ImageSize { Width = 1200, Height = 1600 },
                Slots = { new Slot { Index = 0, X = 10, Y = 20, Width = 300, Height = 400 } }
            };

            Assert.True(vm.ApplyPickedFrame(src));

            Assert.Equal(beforeHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pngPath)));
            Assert.Equal(beforeSlotsWrite, File.GetLastWriteTimeUtc(slotsPath));
            Assert.Equal(beforeFiles, Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToArray());
            // 원본 슬롯 객체도 값 복사만 — src는 그대로.
            Assert.Equal(10, src.Slots[0].X);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* 무시 */ } }
    }

    [Fact]
    public void ApplyPickedFrame_Missing_Image_Reports_Status()
    {
        // C11: 이미지 파일이 없으면 편집 세션을 건드리지 않고 안내만 남긴다.
        var (vm, _, _, _) = MakeVm(UserRole.User);
        var src = new FrameTemplate { Id = "local:gone", Name = "없음", ImageUrl = string.Empty };

        Assert.False(vm.ApplyPickedFrame(src));

        Assert.Contains("찾을 수 없습니다", vm.StatusMessage);
        Assert.Null(vm.FrameImage);
        Assert.Equal("새 프레임", vm.FrameName);
    }

    [Fact]
    public async Task CancelPickFrame_Leaves_Editor_Untouched()
    {
        // C12: 취소는 모달만 닫는다 — 편집기 상태(이미지·슬롯·이름) 전부 유지.
        var (vm, _, _, _) = MakeVm(UserRole.User);
        Assert.True(vm.LoadImage(_imagePath));
        vm.SlotCount = 3;
        vm.FrameName = "작업중";
        var image = vm.FrameImage;
        var slots = vm.Slots.ToList();

        await vm.OpenFramePickerCommand.ExecuteAsync(null);
        Assert.True(vm.IsFramePickerVisible);
        vm.Picker.SelectedFrame = new FrameTemplate
        {
            Id = "bundle:other", Name = "다른프레임", ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 }
        };

        vm.CancelPickFrameCommand.Execute(null);

        Assert.False(vm.IsFramePickerVisible);
        Assert.Same(image, vm.FrameImage);
        Assert.Equal("작업중", vm.FrameName);
        Assert.Equal(slots.Count, vm.Slots.Count);
        Assert.Null(vm.Picker.SelectedFrame);   // 모달 상태 초기화
    }

    [Fact]
    public void ConfirmPickFrame_With_No_Selection_Is_Noop()
    {
        // C13: 선택 없이 확인 → 편집기 무변경, 모달만 닫힘.
        var (vm, _, _, _) = MakeVm(UserRole.User);
        Assert.True(vm.LoadImage(_imagePath));
        vm.FrameName = "작업중";
        var image = vm.FrameImage;

        vm.ConfirmPickFrameCommand.Execute(null);

        Assert.False(vm.IsFramePickerVisible);
        Assert.Same(image, vm.FrameImage);
        Assert.Equal("작업중", vm.FrameName);
    }
}
