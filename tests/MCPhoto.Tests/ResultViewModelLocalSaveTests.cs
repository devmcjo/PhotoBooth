using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.LocalSave;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §3.3·§4.5 T22 — 로컬 저장의 <b>루트 경로 해석</b>과 <b>세션 폴더 보관</b>.
/// <para>
/// ★ 핵심 불변식 두 개: ① <c>LocalSavePath</c> 명시값은 항상 우선이다(이관이 운영자 설정을 덮어쓰지 않는다)
/// ② <c>SaveAsync</c>의 <b>반환값</b>이 세션에 담긴다(시각으로 재계산하면 <c>-2</c> 접미 때문에 다른 손님
/// 폴더를 가리킨다).
/// </para>
/// 저장 실패는 예외가 아니라 <c>null</c>이므로(<c>LocalSaveService</c> 규약) 링크는 자동으로 숨겨진다.
/// </summary>
public class ResultViewModelLocalSaveTests
{
    private sealed class StubComposition : ICompositionService
    {
        public Task<string> ComposeAsync(FrameTemplate frame, IReadOnlyList<CapturedStill> cuts,
            FilterKind filter, string outputPath, CancellationToken ct = default)
            => Task.FromResult(outputPath);
    }

    private sealed class StubTimelapse : ITimelapseService
    {
        public Task<string?> CreateTimelapseAsync(string sessionVideoPath, string outputPath,
            CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    /// <summary>호출 인자를 기록하고 반환값을 지정할 수 있는 로컬 저장 페이크(파일을 만들지 않는다).</summary>
    private sealed class RecordingLocalSave : ILocalSaveService
    {
        private readonly string? _result;
        public RecordingLocalSave(string? result) => _result = result;

        public int Calls { get; private set; }
        public string? LastRoot { get; private set; }

        public Task<string?> SaveAsync(string localSavePath, string finalImagePath, string? timelapsePath,
            DateTime sessionTime, CancellationToken ct = default)
        {
            Calls++;
            LastRoot = localSavePath;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// QR 분기까지 진행되는 최소 하네스. 로그인 + QR on으로 두어 <c>Qr</c> 전이를 타게 한다 —
    /// 전이 자체는 화면 VM 미등록으로 실패하지만 <c>Next</c>의 catch가 삼키며(기존 테스트와 동일 패턴),
    /// <b>세션이 Reset되지 않는다</b>. 게스트 완료 경로는 <c>CompleteSession</c> → <c>Reset()</c>이라
    /// 검증 대상인 <c>LocalSaveFolder</c>가 정상적으로 지워져 관측이 불가능하다.
    /// </summary>
    private static (ResultViewModel vm, SessionContext session, RecordingLocalSave save) MakeVm(
        string? saveResult, string? configuredPath)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"it26save_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.SaveLocalCopy = true;
        settings.Current.LocalSavePath = configuredPath ?? string.Empty;
        settings.Current.EnableQrDelivery = true;

        var session = new SessionContext { FinalImagePath = "final.jpg" };   // video 없음 → 타임랩스 스킵
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new MapServiceProvider(), session);
        session.Login(new User { Id = "u1", Role = UserRole.User, AuthMethod = AuthMethod.Google });

        var save = new RecordingLocalSave(saveResult);
        var vm = new ResultViewModel(shell, new StubComposition(), new StubTimelapse(), save,
            new PumpingCameraService { PumpFrames = false });
        return (vm, session, save);
    }

    [Fact]
    public async Task Save_Success_Stores_Returned_Session_Folder()
    {
        // MakeUniqueFolder가 붙인 "-2" 접미까지 그대로 보관돼야 한다(재계산 금지의 실증).
        const string actual = @"C:\ProgramData\MCPhoto\result\mcphoto_260812_1445-2";
        var (vm, session, save) = MakeVm(actual, configuredPath: null);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(1, save.Calls);
        Assert.Equal(actual, session.LocalSaveFolder);
    }

    [Fact]
    public async Task Save_Failure_Stores_Null()
    {
        var (vm, session, save) = MakeVm(saveResult: null, configuredPath: null);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(1, save.Calls);
        Assert.Null(session.LocalSaveFolder);   // 실패는 예외가 아니라 null → 링크 미노출로 이어진다
    }

    [Fact]
    public async Task Default_Root_Is_DataFolder_Result_Not_Exe_Folder()
    {
        var (vm, _, save) = MakeVm(saveResult: null, configuredPath: null);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(MCPhoto.App.App.DataFolder, "result"), save.LastRoot);
        Assert.NotEqual(Path.Combine(AppContext.BaseDirectory, "result"), save.LastRoot);  // 구 기본값 회귀 잠금
    }

    [Fact]
    public async Task Configured_Root_Is_Honored_Verbatim()
    {
        // ⛔ 이관이 운영자 설정을 덮어쓰면 안 된다(설계 A-3).
        var (vm, _, save) = MakeVm(saveResult: null, configuredPath: @"D:\booth\photos");

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\booth\photos", save.LastRoot);
    }

    [Fact]
    public async Task SaveLocalCopy_Off_Leaves_Session_Folder_Null()
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"it26save_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.SaveLocalCopy = false;
        settings.Current.EnableQrDelivery = true;

        var session = new SessionContext { FinalImagePath = "final.jpg" };
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new MapServiceProvider(), session);
        session.Login(new User { Id = "u1", Role = UserRole.User, AuthMethod = AuthMethod.Google });
        var save = new RecordingLocalSave(@"C:\never");
        var vm = new ResultViewModel(shell, new StubComposition(), new StubTimelapse(), save,
            new PumpingCameraService { PumpFrames = false });

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(0, save.Calls);
        Assert.Null(session.LocalSaveFolder);
    }

    [Fact]
    public void Reset_Clears_LocalSaveFolder()
    {
        // ⚠️ 누락하면 다음 손님의 유휴 팝업이 이전 손님 폴더를 여는 링크를 노출한다(it26 §4.5 · E10).
        var session = new SessionContext { LocalSaveFolder = @"C:\ProgramData\MCPhoto\result\mcphoto_260812_1445" };

        session.Reset();

        Assert.Null(session.LocalSaveFolder);
    }
}
