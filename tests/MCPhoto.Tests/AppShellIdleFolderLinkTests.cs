using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §4 T5~T14 — 유휴 경고 팝업의 [결과물 폴더 열기] 링크.
/// <para>
/// ★ 핵심 불변식: ① 노출은 "옵션 on <b>AND</b> 이 세션 저장 성공"의 AND ② 여는 것은 <b>그 세션 폴더</b>뿐이다
/// ③ <c>Reset()</c> 이후에는 링크가 없다(이전 손님 폴더 노출 금지) ④ 링크 클릭이 <b>카운트다운을 건드리지 않는다</b>.
/// </para>
/// 창은 띄우지 않는다(headless — 셸 상태만 검증).
/// </summary>
public class AppShellIdleFolderLinkTests
{
    private const string SessionFolder = @"C:\ProgramData\MCPhoto\result\mcphoto_260812_1445-2";

    /// <summary>열기 요청 경로를 기록하는 페이크(실제 탐색기 미실행).</summary>
    private sealed class FakeFolderOpener : IFolderOpener
    {
        private readonly bool _result;
        public FakeFolderOpener(bool result = true) => _result = result;

        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        public bool TryOpen(string? path)
        {
            Calls++;
            LastPath = path;
            return _result;
        }
    }

    private static (AppShellViewModel shell, SessionContext session, IniSettingsService settings) MakeShell(
        bool optionOn, string? localSaveFolder, IFolderOpener? opener = null)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"it26idle_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableResultFolderOpen = optionOn;

        var session = new SessionContext { LocalSaveFolder = localSaveFolder };
        var services = new MapServiceProvider();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session,
            logger: null, testMode: null, folderOpener: opener);
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));   // 셸 순환 의존 → 지연 생성
        return (shell, session, settings);
    }

    // ── T5~T7: 가시성 진리표 ──

    [Fact]
    public void Link_Is_Visible_When_Option_On_And_Session_Saved()
    {
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder);

        shell.ShowIdleWarning();

        Assert.True(shell.IsIdleWarningVisible);
        Assert.True(shell.IsResultFolderLinkVisible);
        Assert.False(shell.HasResultFolderOpenError);   // 처음엔 오류 캡션 없음
        shell.Dispose();
    }

    [Fact]
    public void Link_Is_Hidden_When_Option_Off_By_Default()
    {
        // 기본값(off)에서는 저장이 성공했어도 링크가 없다 — 종전과 똑같은 팝업이다.
        var (shell, _, settings) = MakeShell(optionOn: false, localSaveFolder: SessionFolder);
        Assert.False(settings.Current.EnableResultFolderOpen);

        shell.ShowIdleWarning();

        Assert.True(shell.IsIdleWarningVisible);
        Assert.False(shell.IsResultFolderLinkVisible);
        shell.Dispose();
    }

    [Fact]
    public void Link_Is_Hidden_When_Session_Has_No_Save_Folder()
    {
        // 저장 전 상태(FrameSelect~Result) · SaveLocalCopy=false · 저장 실패 — 전부 이 경로다.
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: null);

        shell.ShowIdleWarning();

        Assert.False(shell.IsResultFolderLinkVisible);
        shell.Dispose();
    }

    // ── T8: 여는 것은 그 세션 폴더뿐이다 ──

    [Fact]
    public void Open_Command_Passes_Exact_Session_Folder()
    {
        // ⛔ 저장 루트를 열면 직전 손님들의 사진이 전부 보인다(폴더명이 촬영 시각이다).
        var opener = new FakeFolderOpener();
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder, opener: opener);
        shell.ShowIdleWarning();

        shell.OpenResultFolderCommand.Execute(null);

        Assert.Equal(1, opener.Calls);
        Assert.Equal(SessionFolder, opener.LastPath);
        Assert.NotEqual(@"C:\ProgramData\MCPhoto\result", opener.LastPath);   // 루트가 아니다
        Assert.False(shell.HasResultFolderOpenError);
        shell.Dispose();
    }

    [Fact]
    public void Open_Command_Is_NoOp_Without_Session_Folder()
    {
        var opener = new FakeFolderOpener();
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: null, opener: opener);
        shell.ShowIdleWarning();

        shell.OpenResultFolderCommand.Execute(null);

        Assert.Equal(0, opener.Calls);
        Assert.False(shell.HasResultFolderOpenError);
        shell.Dispose();
    }

    // ── T9: 다음 손님에게 이전 손님 폴더가 새지 않는다 ──

    [Fact]
    public void After_Session_Reset_Link_Is_Hidden_Again()
    {
        var (shell, session, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder);
        shell.ShowIdleWarning();
        Assert.True(shell.IsResultFolderLinkVisible);
        shell.HideIdleWarning();

        session.Reset();          // 홈 복귀·유휴 만료·완료 — 어느 경로로든 세션이 끝나면
        shell.ShowIdleWarning();  // 다음 손님 세션에서 팝업이 떠도

        Assert.Null(session.LocalSaveFolder);
        Assert.False(shell.IsResultFolderLinkVisible);   // ⛔ 이전 손님 폴더가 노출되지 않는다
        shell.Dispose();
    }

    // ── T10·T11: 실패 안내와 초기화 ──

    [Fact]
    public void Open_Failure_Shows_Path_Caption_And_Keeps_Popup()
    {
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder,
            opener: new FakeFolderOpener(result: false));
        shell.ShowIdleWarning();

        shell.OpenResultFolderCommand.Execute(null);

        Assert.True(shell.HasResultFolderOpenError);
        Assert.Equal(AppShellViewModel.FormatResultFolderOpenError(SessionFolder), shell.ResultFolderOpenError);
        Assert.Contains(SessionFolder, shell.ResultFolderOpenError);   // 수동 탐색이 가능해야 한다
        Assert.True(shell.IsIdleWarningVisible);                       // 팝업은 유지(재시도 가능)
        Assert.True(shell.IsResultFolderLinkVisible);
        shell.Dispose();
    }

    [Fact]
    public void Missing_Opener_Falls_Back_To_Error_Caption()
    {
        // IFolderOpener 미주입(기존 테스트 다수·구성 누락) → 크래시 없이 실패 안내로 축퇴한다.
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder, opener: null);
        shell.ShowIdleWarning();

        shell.OpenResultFolderCommand.Execute(null);

        Assert.True(shell.HasResultFolderOpenError);
        shell.Dispose();
    }

    [Fact]
    public void Hide_Clears_Error_And_Link_So_Next_Popup_Is_Clean()
    {
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder,
            opener: new FakeFolderOpener(result: false));
        shell.ShowIdleWarning();
        shell.OpenResultFolderCommand.Execute(null);
        Assert.True(shell.HasResultFolderOpenError);

        shell.HideIdleWarning();

        Assert.False(shell.HasResultFolderOpenError);
        Assert.Equal(string.Empty, shell.ResultFolderOpenError);
        Assert.False(shell.IsResultFolderLinkVisible);
        shell.Dispose();
    }

    [Fact]
    public void Reshow_Does_Not_Carry_Stale_Error()
    {
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder,
            opener: new FakeFolderOpener(result: false));
        shell.ShowIdleWarning();
        shell.OpenResultFolderCommand.Execute(null);
        shell.HideIdleWarning();

        shell.ShowIdleWarning();

        Assert.False(shell.HasResultFolderOpenError);   // 다음 팝업에 이전 손님 경로가 남지 않는다
        shell.Dispose();
    }

    // ── T12: 카운트다운 무간섭(사용자 지시) ──

    [Fact]
    public void Open_Command_Does_Not_Touch_Countdown()
    {
        // ⛔ "친절하게" 카운트다운을 멈추거나 늘리는 코드가 들어오면 무인 부스가 정지한다.
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder,
            opener: new FakeFolderOpener());
        shell.ShowIdleWarning();
        var before = shell.IdleCountdownRemaining;

        shell.OpenResultFolderCommand.Execute(null);

        Assert.Equal(before, shell.IdleCountdownRemaining);
        Assert.True(shell.IsIdleWarningVisible);   // 팝업이 닫히지도 않는다
        shell.Dispose();
    }

    // ── T13·T14: 기존 규격 잠금 ──

    [Fact]
    public void Idle_Constants_Are_Unchanged()
    {
        var (shell, _, _) = MakeShell(optionOn: false, localSaveFolder: null);

        Assert.Equal(120, shell.IdleWarningSeconds);   // 경고까지 2분
        Assert.Equal(10, shell.IdleCountdownSeconds);  // 카운트다운 10초(요구는 이미 충족돼 있었다)
        shell.Dispose();
    }

    [Fact]
    public void Idle_Watch_Does_Not_Run_On_Home()
    {
        // §4.2 판정의 근거: 홈에서는 팝업이 뜨지 않으므로 "메인 화면으로 돌아갑니다"는 항상 참이다.
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Home));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Qr));   // 링크의 실사용 창구
    }

    [Fact]
    public void Countdown_Starts_At_Ten_With_Link()
    {
        var (shell, _, _) = MakeShell(optionOn: true, localSaveFolder: SessionFolder);

        shell.ShowIdleWarning();

        Assert.Equal(10, shell.IdleCountdownRemaining);   // 링크 추가가 카운트다운 초기값을 바꾸지 않는다
        shell.Dispose();
    }
}
