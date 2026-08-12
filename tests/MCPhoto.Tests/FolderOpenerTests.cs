using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §5.3 T16·T17 — 폴더 열기는 best-effort다.
/// ★ 핵심 불변식: ① 예외가 밖으로 나가지 않는다(잠금 키오스크에서 explorer가 차단될 수 있다)
/// ② <b>폴더를 만들지 않는다</b> — 여기서는 폴더 부재가 "사진이 없다"는 정보이므로 빈 폴더를 만들면 거짓이다.
/// 실제 탐색기는 띄우지 않는다(opener 주입 — LogFolderService와 같은 이음새).
/// </summary>
public class FolderOpenerTests : IDisposable
{
    private readonly string _dir;

    public FolderOpenerTests()
        => _dir = Path.Combine(Path.GetTempPath(), $"mcphoto_open_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    [Fact]
    public void Opens_Existing_Folder_And_Passes_Exact_Path()
    {
        Directory.CreateDirectory(_dir);
        string? opened = null;
        var svc = new FolderOpener(opener: p => opened = p);

        Assert.True(svc.TryOpen(_dir));
        Assert.Equal(_dir, opened);          // 상위 폴더가 아니라 준 경로 그대로 연다
    }

    [Fact]
    public void Missing_Folder_Returns_False_And_Does_Not_Create_It()
    {
        var calls = 0;
        var svc = new FolderOpener(opener: _ => calls++);

        Assert.False(svc.TryOpen(_dir));
        Assert.False(Directory.Exists(_dir));   // ⚠️ CreateDirectory 금지
        Assert.Equal(0, calls);                 // 열기 시도조차 하지 않는다
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Path_Returns_False(string? path)
    {
        var calls = 0;
        var svc = new FolderOpener(opener: _ => calls++);

        Assert.False(svc.TryOpen(path));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Invalid_Path_Does_Not_Throw()
    {
        var svc = new FolderOpener();
        Assert.False(svc.TryOpen("C:\\\0invalid"));
        Assert.False(svc.TryOpen(new string('x', 400)));
    }

    [Fact]
    public void Opener_Exception_Is_Swallowed()
    {
        // 잠금 키오스크(셸 교체·정책)에서 explorer 실행이 차단되는 경우 — 크래시 대신 false.
        Directory.CreateDirectory(_dir);
        var svc = new FolderOpener(opener: _ => throw new InvalidOperationException("차단됨"));

        Assert.False(svc.TryOpen(_dir));
    }

    [Fact]
    public void Construction_Has_No_Side_Effects()
    {
        // DI 해석만으로 프로세스를 만들거나 폴더를 건드리지 않는다.
        var calls = 0;
        _ = new FolderOpener(opener: _ => calls++);
        Assert.Equal(0, calls);
    }

    /// <summary>열기 요청만 세는 스파이(합성 루트 배선 검증용).</summary>
    private sealed class SpyOpener : IFolderOpener
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public bool TryOpen(string? path) { Calls++; LastPath = path; return true; }
    }

    /// <summary>
    /// 합성 루트 배선 검증. <c>AppShellViewModel</c>의 <see cref="IFolderOpener"/>는 <b>선택 파라미터</b>라
    /// 등록을 잊어도 컴파일·단위 테스트가 조용히 통과하고, 링크는 실행 환경에서만 "항상 실패"로 나타난다.
    /// 그래서 앱과 같은 형태로 조립한 컨테이너에서 커맨드를 실제로 실행해 주입을 관측한다
    /// (스파이를 <c>Register</c> <b>뒤</b>에 등록해 실제 explorer 실행은 막는다 — 마지막 등록이 이긴다).
    /// </summary>
    [Fact]
    public void Composition_Root_Injects_FolderOpener_Into_Shell()
    {
        var iniPath = Path.Combine(_dir, "MCPhoto.ini");
        Directory.CreateDirectory(_dir);

        var services = new ServiceCollection();
        services.AddLogging();
        ServiceRegistration.Register(services);
        // 실제 ini 경로 탐색(쓰기 프로브)을 피한다.
        var settings = new IniSettingsService(iniPath: iniPath, fallbackCandidates: new[] { iniPath });
        settings.Load();
        services.AddSingleton<ISettingsService>(settings);

        // 등록 자체는 실구현이어야 한다 — 그것을 먼저 확인하고 나서 스파이로 덮는다.
        using (var real = services.BuildServiceProvider())
            Assert.IsType<FolderOpener>(real.GetRequiredService<IFolderOpener>());

        var spy = new SpyOpener();
        services.AddSingleton<IFolderOpener>(spy);
        using var provider = services.BuildServiceProvider();

        var shell = provider.GetRequiredService<AppShellViewModel>();
        provider.GetRequiredService<SessionContext>().LocalSaveFolder = _dir;
        shell.OpenResultFolderCommand.Execute(null);

        Assert.Equal(1, spy.Calls);          // 셸이 주입된 오프너를 실제로 쓴다
        Assert.Equal(_dir, spy.LastPath);
        Assert.False(shell.HasResultFolderOpenError);
        shell.Dispose();
    }
}
