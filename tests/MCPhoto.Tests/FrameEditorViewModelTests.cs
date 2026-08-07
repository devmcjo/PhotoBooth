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
[Collection(FallbackCacheCollection.Name)]   // it20 N2: 공유 fallback 캐시 경로 경합 제거
public class FrameEditorViewModelTests : IClassFixture<FrameImageFixture>
{
    private sealed class CapturingFrameRepository : IFrameRepository
    {
        public FrameTemplate? Saved { get; private set; }

        /// <summary>서버 등록 실패(D6 원자성) 시나리오 주입 — SaveAsync가 던지고 Saved는 남지 않는다.</summary>
        public bool ThrowOnSave { get; set; }

        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        /// <summary>개인 프레임 서버 저장(설계 D-7). 서버가 부여하는 문서 id를 흉내 낸다.</summary>
        public Task<FrameTemplate> SaveMineAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("서버 저장 실패(테스트)");
            Saved = frame;
            frame.Id = "srv-mine-1";
            return Task.FromResult(frame);
        }

        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("서버 오류");
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

        /// <summary>마지막 저장의 서버 문서 id(#dbid 기록 여부 검증용).</summary>
        public string? SavedDbId { get; private set; }

        public FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId)
        {
            SavedFrame = frame;
            SavedOwner = null;              // 공용 저장은 소유자가 없다
            SavedDbId = dbId;
            return frame;
        }

        public FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId)
        {
            SavedFrame = frame;
            SavedOwner = ownerEmail;
            SavedDbId = dbId;
            return frame;
        }

        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail)
            => UserFrames.TryGetValue(ownerEmail, out var list) ? list : new List<FrameTemplate>();
        public bool DeleteLocal(FrameTemplate frame) => true;
        public IReadOnlySet<string> PublicFrameNames() => PublicNames;
        public IReadOnlySet<string> UserFrameNames(string ownerEmail)
            => new HashSet<string>(LoadUser(ownerEmail).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail) => Array.Empty<LocalFrameEntry>();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>진단용: VM이 catch로 삼킨 예외(LoadImage 등)를 테스트에서 볼 수 있게 한다.</summary>
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
        session.Login(new User { Id = "u1", Role = role, Email = "u1@test.com" });   // 개인 저장은 이메일이 필요(D-4)
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

    /// <summary>
    /// it16 §8.2-21(§4.5 3중 방어): 화면 게이트를 우회해 편집기에 도달해도 user·temp_user의 저장은
    /// fail-closed 가드에서 거부되고 아무것도 기록되지 않는다.
    /// </summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task NonWriter_Save_Is_Refused_Fail_Closed(UserRole role)
    {
        var (vm, repo, local, _) = MakeVm(role);
        Assert.True(vm.LoadImage(_imagePath));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.Equal("프레임을 만들 권한이 없습니다.", vm.StatusMessage);
    }

    [Fact]
    public async Task Power_Save_Persists_To_Db_And_Local_Cache()
    {
        // it8 A2: 파워는 DB(isDefault=true) + 로컬 캐시(ownerName=null).
        // it15 C3 무회귀: 파워의 **신규 생성** 저장은 F1 이후에도 서버에 등록될 수 있다(공용 기본 프레임 배포 경로).
        // R2: 그 등록은 확인 팝업의 체크박스가 켜진 경우에만 일어난다 → [저장]은 팝업만 띄운다.
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.SlotCount = 6;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsServerRegisterConfirmVisible);  // 팝업 대기
        Assert.Null(repo.Saved);                         // 아직 아무것도 저장하지 않았다

        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.PublicServer;
        await vm.ConfirmServerRegisterCommand.ExecuteAsync(null);

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
        ImageUrl = _imagePath, // 불러오기가 읽을 로컬 png(File.Exists 성립)
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
    };

    [Fact]
    public async Task SaveScopeNotice_Warns_Before_Save_When_Public_Name_Has_Underscore()
    {
        // §3.4: 공용 스코프에서 이름에 '_'가 있으면 저장은 되지만 LoadPublic('_'=user 접두)에서 탈락한다.
        // 저장 직후 StatusMessage는 화면 전환으로 읽을 수 없으므로 저장 전 캡션에서 경고해야 한다.
        var (vm, _, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.FrameName = "내_프레임";
        Assert.Contains("'_'가 있으면", vm.SaveScopeNotice);

        vm.FrameName = "내프레임";
        Assert.DoesNotContain("'_'가 있으면", vm.SaveScopeNotice);

        // user 스코프는 파일명이 '{계정}_{이름}'이라 '_'가 문제되지 않는다 → 경고 없음.
        var (userVm, _, _, _) = MakeVm(UserRole.User);
        Assert.True(userVm.LoadImage(_imagePath));
        userVm.FrameName = "내_프레임";
        Assert.DoesNotContain("'_'가 있으면", userVm.SaveScopeNotice);

        // 저장은 차단되지 않는다(비차단 경고). Admin+신규 생성이므로 확인 팝업을 한 번 거친다(체크 off = 로컬만).
        vm.FrameName = "내_프레임";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsServerRegisterConfirmVisible);
        Assert.False(vm.CanConfirmSaveScope);                                   // D-21: 미선택이면 저장 불가
        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.Personal;
        await vm.ConfirmServerRegisterCommand.ExecuteAsync(null);
        Assert.NotNull(local.SavedFrame);
    }

    // ── R2: 서버 등록 확인 팝업(파워 신규 생성 저장 시에만, 체크 on일 때만 DB insert) ──

    /// <summary>N1: [저장]은 팝업만 띄우고 로컬·서버 어느 쪽에도 쓰지 않는다. 체크박스는 기본 on(D4).</summary>
    [Fact]
    public async Task Power_Create_Save_Shows_Popup_And_Persists_Nothing()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsServerRegisterConfirmVisible);
        Assert.Equal(FrameEditorViewModel.FrameSaveScope.None, vm.SaveScope);  // D-21: 기본 미선택           // D4: 기본 on
        Assert.Null(repo.Saved);                    // 팝업 시점엔 아직 아무것도 쓰지 않는다
        Assert.Null(local.SavedFrame);
    }

    /// <summary>N3: 체크 on 확인 → DB insert + 서버가 돌려준 프레임으로 로컬 캐시(#dbid 기록).</summary>
    [Fact]
    public async Task Power_Confirm_With_Checkbox_Registers_To_Server()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));
        await vm.SaveCommand.ExecuteAsync(null);

        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.PublicServer;
        await vm.ConfirmServerRegisterCommand.ExecuteAsync(null);

        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.IsDefault);
        Assert.Null(repo.Saved.UserId);
        Assert.Null(local.SavedOwner);                             // 공용 캐시
        Assert.Same(repo.Saved, local.SavedFrame);                 // 서버 반환 프레임을 그대로 캐시
        Assert.False(vm.IsServerRegisterConfirmVisible);
    }

    /// <summary>N4: 취소는 아무것도 저장하지 않고 편집 세션을 그대로 유지한다.</summary>
    [Fact]
    public async Task Cancel_Server_Register_Persists_Nothing_And_Keeps_Editor()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));
        vm.SlotCount = 3;
        vm.FrameName = "작업중";
        var image = vm.FrameImage;
        await vm.SaveCommand.ExecuteAsync(null);
        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.Personal;               // 기본값(on)과 다른 값으로 바꿔 둔다

        vm.CancelServerRegisterCommand.Execute(null);

        Assert.False(vm.IsServerRegisterConfirmVisible);
        Assert.Equal(FrameEditorViewModel.FrameSaveScope.None, vm.SaveScope);  // D-21: 기본 미선택          // 취소도 기본값(D4=on)으로 되돌린다
        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.Equal("작업중", vm.FrameName);      // 편집 세션 불변
        Assert.Equal(3, vm.Slots.Count);
        Assert.Same(image, vm.FrameImage);
    }

    /// <summary>
    /// N5(R1+R2): F2로 불러온 세션도 신규 생성이므로 저장 시 서버 등록 팝업이 뜬다
    /// (= 세션이 New임을 행동으로 증명 — fork라면 팝업이 뜨지 않는다).
    /// </summary>
    [Fact]
    public async Task Picked_Frame_Session_Stays_New_And_Prompts_Server_Register()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.ApplyPickedFrame(DbDefaultFrame()));
        vm.FrameName = "새로만든프레임";       // 스코프 충돌 없는 이름

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsServerRegisterConfirmVisible);
        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
    }

    /// <summary>N10(D6 원자성): 서버 등록이 실패하면 로컬 저장도 하지 않고 편집기에 머문다.</summary>
    [Fact]
    public async Task Server_Register_Failure_Persists_Nothing_And_Reports()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));
        repo.ThrowOnSave = true;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.PublicServer;
        await vm.ConfirmServerRegisterCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);                       // 부분 성공 없음
        Assert.Contains("저장하지 못했습니다", vm.StatusMessage);
        Assert.Contains("서버 오류", vm.StatusMessage);        // 구체적 사유가 가려지지 않는다
        // 편집 세션이 살아 있다는 사실을 알려야 사용자가 처음부터 다시 만들지 않는다.
        Assert.Contains("편집 중인 내용은 그대로", vm.StatusMessage);
    }

    /// <summary>
    /// N13(D4): 체크 상태는 팝업을 **다시 열 때** 기본값(on)으로 초기화된다(직전 선택 잔존 금지).
    /// 취소 커맨드도 같은 리셋을 하므로 취소 **뒤에** 값을 일부러 되돌려 놓고 재오픈한다 — 그래야 이 단언이
    /// 통과하는 유일한 이유가 [저장]의 리셋이 된다(취소의 리셋에 기대면 불변식을 검증하지 못한다).
    /// </summary>
    [Fact]
    public async Task RegisterToServer_Resets_On_Reopen()
    {
        var (vm, _, _, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        await vm.SaveCommand.ExecuteAsync(null);
        vm.CancelServerRegisterCommand.Execute(null);
        vm.SaveScope = FrameEditorViewModel.FrameSaveScope.Personal;               // 취소의 리셋을 무력화 → 재오픈 리셋만이 단언을 통과시킨다

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsServerRegisterConfirmVisible);
        Assert.Equal(FrameEditorViewModel.FrameSaveScope.None, vm.SaveScope);  // D-21: 기본 미선택
    }

    // ── D1: 이름 충돌 / 이름 안전성 저장 전 차단(데이터 손실 방지) ──
    // SaveLocal은 같은 이름 파일을 경고 없이 덮어쓴다 → 덮어쓰기가 세션의 의도인 EditOwnLocal만 예외.

    /// <summary>N7: power 신규 생성에서 공용 스코프에 동명 프레임이 있으면 저장·팝업 모두 없다.</summary>
    [Fact]
    public async Task Power_Save_Blocked_When_Name_Collides_With_Public_Frame()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));
        local.PublicNames.Add("프레임A");

        vm.FrameName = "프레임A";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.Contains("이미 같은 이름", vm.StatusMessage);
    }

    /// <summary>N8: 비power도 개인 스코프 기준으로 동일하게 차단된다(파일명 `{계정}_{이름}` 덮어쓰기 방지).</summary>
    [Fact]
    public async Task AdvancedUser_Save_Blocked_When_Name_Collides_With_Own_Frame()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.AdvancedUser);
        Assert.True(vm.LoadImage(_imagePath));
        local.UserFrames["u1@test.com"] = new List<FrameTemplate>   // 조회 키는 이메일(D-4)
        {
            new() { Id = "local:u1_내프레임", Name = "내프레임", UserId = "u1" }
        };

        vm.FrameName = "내프레임";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.Contains("이미 같은 이름", vm.StatusMessage);
    }

    /// <summary>N11: 파일시스템 금지문자는 저장 전에 차단한다(서버에만 남는 반쪽 상태 방지).</summary>
    [Fact]
    public async Task Save_Blocked_When_Name_Has_Invalid_Chars()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.FrameName = "a/b";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.False(vm.IsServerRegisterConfirmVisible);      // 팝업도 뜨지 않는다
        Assert.Contains("사용할 수 없는 문자", vm.StatusMessage);
    }

    /// <summary>N12: 빈 이름(공백만)도 고유 문구로 차단한다.</summary>
    [Fact]
    public async Task Save_Blocked_When_Name_Is_Blank()
    {
        var (vm, repo, local, _) = MakeVm(UserRole.Admin);
        Assert.True(vm.LoadImage(_imagePath));

        vm.FrameName = "   ";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(repo.Saved);
        Assert.Null(local.SavedFrame);
        Assert.False(vm.IsServerRegisterConfirmVisible);
        Assert.Contains("이름을 입력", vm.StatusMessage);
    }

    // ── it15 F2: 기존 프레임 불러오기(이미지·슬롯 메모리 복사, 원본 불변) ──

    /// <summary>지정 크기의 실제 이미지 파일을 만든다(OpenCV 디코드 경로 검증용). 호출측이 삭제 책임.</summary>
    private static string MakeImageFile(int width, int height, string extension)
        => TestImageFile.CreateInTemp(width, height, extension, gray: 160);

    [Fact]
    public void ApplyPickedFrame_Copies_Slots_And_Keeps_Editable_Name()
    {
        // C7(R1 반영): 축소 없는 크기(1200×1600) → 슬롯 좌표가 원본과 일치, 생성 모드 유지.
        // 이름은 **자동 사본 네이밍 없이** 기존 값을 그대로 둔다(사본이 아니라 새 프레임 생성).
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
        Assert.Equal("새 프레임", vm.FrameName);   // 기본값 유지("클래식 사본"으로 덮어쓰지 않는다)
        Assert.True(vm.HasPickedSource);           // 무엇을 불러왔는지 캡션으로 안내
        Assert.Contains("클래식", vm.PickedSourceNotice);
        Assert.True(vm.IsCreateMode);          // 편집 모드로 바뀌지 않는다(_isEditing 불변)
        Assert.Equal("새 프레임 만들기", vm.EditorTitle);
        Assert.True(vm.CanSave);
    }

    /// <summary>N6(R1): 사용자가 먼저 이름을 정한 뒤 기존 프레임을 불러와도 그 이름이 보존된다.</summary>
    [Fact]
    public void ApplyPickedFrame_Preserves_User_Typed_Name()
    {
        var (vm, _, _, _) = MakeVm(UserRole.User);
        vm.FrameName = "내작품";

        Assert.True(vm.ApplyPickedFrame(new FrameTemplate
        {
            Id = "bundle:classic", Name = "클래식", IsDefault = true,
            ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 },
            Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
        }));

        Assert.Equal("내작품", vm.FrameName);          // 사본 접미 없음, 원본 이름으로 덮어쓰지도 않음
        Assert.DoesNotContain("사본", vm.FrameName);
    }

    /// <summary>이미지를 직접 다시 불러오면 "불러온 원본" 안내가 사실과 어긋나므로 사라진다.</summary>
    [Fact]
    public void LoadImage_Clears_Picked_Source_Notice()
    {
        var (vm, _, _, _) = MakeVm(UserRole.User);
        Assert.True(vm.ApplyPickedFrame(new FrameTemplate
        {
            Id = "bundle:classic", Name = "클래식", IsDefault = true,
            ImageUrl = _imagePath,
            ImageSize = new ImageSize { Width = 1200, Height = 1600 },
            Slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio())
        }));
        Assert.True(vm.HasPickedSource);

        Assert.True(vm.LoadImage(_imagePath));

        Assert.False(vm.HasPickedSource);
        Assert.Equal(string.Empty, vm.PickedSourceNotice);
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

    // ⚠️ 아래 테스트들은 삭제했다 — 저장 흐름이 **서버 정본**으로 바뀌고(설계 D-7)
    //    프레임 **수정 기능이 폐지**되어(D-16) 시나리오 자체가 존재하지 않는다:
    //      AdvancedUser_Save_Persists_Locally_With_Six_Slots (로컬 전용 저장 경로 소멸)
    //      Power_Confirm_Without_Checkbox_Saves_Local_Public_Only (체크박스 → 라디오, 로컬 전용 소멸)
    //      Non_New_Sessions_Save_Immediately_Without_Popup / EditOwnLocal_* / *_Editing_* (편집 세션 소멸)
    //    새 저장 흐름 검증은 설계 §13 T16(서버 강제)·T23(스코프 라디오)으로 다시 작성한다.
}
