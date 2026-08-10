using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it23 C부: 설정 화면의 오픈소스 라이선스 뷰어(전문 직접 렌더링).
/// <para>
/// 수락 기준 AC-C2가 이 테스트 묶음의 축이다 — <b>뷰어가 계정·역할·테스트 모드를 전혀 읽지 않는다.</b>
/// 그래야 "어떤 로그인 상태에서 못 보이나"라는 질문이 구조적으로 성립하지 않고(GPLv3 §4는 고지 전달을
/// 요구하므로 게스트도 전문을 볼 수 있어야 한다), 남는 변수는 상위 진입 게이트 하나가 된다.
/// </para>
/// </summary>
public class SettingsViewModelLicenseTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ── 최소 페이크(뷰어와 무관한 의존만 채운다) ──

    private sealed class FakeCameraService : ICameraService
    {
        public event EventHandler<CameraFrame>? FrameReady { add { } remove { } }
        public double CurrentFps => 30;
        public bool IsRunning => false;
        public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default) => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public void SetMirror(bool mirror) { }
        public void SetTargetAspect(double aspect) { }
        public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default) => Task.FromResult(new CapturedStill());
        public void StartRecording(string outputPath) { }
        public Task StopRecordingAsync() => Task.CompletedTask;
        public IReadOnlyList<CameraDevice> EnumerateDevices() => Array.Empty<CameraDevice>();
        public void Dispose() { }
    }

    private sealed class FakeCameraTestDialog : ICameraTestDialogService
    {
        public Task ShowAsync(int deviceIndex) => Task.CompletedTask;
        public Task ShowAsync(CameraTestTarget target) => Task.CompletedTask;
    }

    private sealed class FakeDiagnosticsDialog : IDiagnosticsDialogService
    {
        public Task ShowAsync() => Task.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>문서 목록·본문 결과를 주입하는 고지 서비스 스텁(파일 미접근).</summary>
    private sealed class StubLicenseNoticeService : ILicenseNoticeService
    {
        public string FolderPath { get; init; } = @"C:\app\licenses";
        public bool Exists { get; init; } = true;
        public List<LicenseDocument> Documents { get; init; } = new();
        public Dictionary<string, LicenseTextResult> Texts { get; } = new();
        public int ListCalls { get; private set; }

        public IReadOnlyList<LicenseDocument> ListDocuments() { ListCalls++; return Documents; }

        public LicenseTextResult ReadText(LicenseDocument document)
            => Texts.TryGetValue(document.DisplayName, out var r) ? r : LicenseTextResult.Ok($"body of {document.DisplayName}");
    }

    /// <summary>
    /// 임시 고지 폴더를 만들고 실제 서비스를 돌려준다(C-T14의 "실제 읽기"용).
    /// </summary>
    private ILicenseNoticeService RealServiceWith(params (string name, string content)[] files)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_vmlic_{Guid.NewGuid():N}");
        _tempDirs.Add(baseDir);
        var dir = Path.Combine(baseDir, "licenses");
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));
        return new LicenseNoticeService(baseDirectory: baseDir);
    }

    /// <summary>
    /// 설정 VM 조립. <paramref name="loginUser"/>로 게스트·실계정·테스트 계정 3상태를 만들 수 있다 —
    /// 뷰어가 어떤 상태에서도 같게 동작해야 하기 때문이다(AC-C1).
    /// </summary>
    private static SettingsViewModel MakeVm(ILicenseNoticeService? notice, User? loginUser = null)
    {
        var iniPath = Path.Combine(Path.GetTempPath(), $"mcphoto_vmlic_{Guid.NewGuid():N}.ini");
        var settings = new IniSettingsService(iniPath: iniPath, fallbackCandidates: new[] { iniPath });
        settings.Load();

        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return new SettingsViewModel(shell, settings, new FakeCameraService(), new FakeCameraTestDialog(),
            new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = true },
            new NullExternalCamera(), logger: null, licenseNotice: notice);
    }

    private static LicenseDocument Doc(string name, long size = 100) =>
        new(name, Path.Combine(@"C:\app\licenses", name), size);

    // ── C-T10 ~ C-T13 ──

    /// <summary>
    /// C-T10: 열기 → 목록 채움 → 첫 항목 자동 선택 → 본문 표시. 열자마자 빈 화면을 보여주지 않는다.
    /// </summary>
    [Fact]
    public async Task T10_Open_Populates_List_Selects_First_And_Loads_Body()
    {
        var stub = new StubLicenseNoticeService
        {
            Documents = { Doc("README.txt", 2376), Doc("FFmpeg-COPYING.GPLv3.txt", 35149) },
        };
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);
        Assert.Equal(2, vm.LicenseDocuments.Count);
        Assert.Equal("README.txt", vm.SelectedLicenseDocument!.DisplayName);
        Assert.Equal("body of README.txt", vm.LicenseText);
        Assert.False(vm.HasLicenseError);
        Assert.False(vm.IsLicenseLoading);
        Assert.Equal("README.txt · 2.3 KB", vm.LicenseSelectionSummary);
    }

    /// <summary>C-T11: 다른 문서를 고르면 본문·요약이 갱신된다(선택마다 다시 읽는다 — 캐시하지 않는다).</summary>
    [Fact]
    public async Task T11_Selecting_Another_Document_Reloads_Body()
    {
        var stub = new StubLicenseNoticeService
        {
            Documents = { Doc("README.txt", 2376), Doc("FFmpeg-COPYING.GPLv3.txt", 35149) },
        };
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        vm.SelectedLicenseDocument = vm.LicenseDocuments[1];
        await vm.LicenseLoadTask!;

        Assert.Equal("body of FFmpeg-COPYING.GPLv3.txt", vm.LicenseText);
        Assert.Equal("FFmpeg-COPYING.GPLv3.txt · 34.3 KB", vm.LicenseSelectionSummary);
    }

    /// <summary>
    /// C-T12: 폴더는 있으나 문서가 0건이면 F2 문구. 폴더 자체가 없으면 F1 문구 —
    /// 조치가 다르므로 뭉개지 않는다(누락을 감추지 않는다는 원칙).
    /// </summary>
    [Theory]
    [InlineData(true, "라이선스 고지 파일을 찾을 수 없습니다. 배포 산출물이 불완전하므로 개발자에게 알려주세요.")]
    [InlineData(false, "라이선스 고지 폴더를 찾을 수 없습니다. 배포 산출물에 licenses 폴더가 누락된 상태이므로 개발자에게 알려주세요.")]
    public async Task T12_Empty_List_Reports_Missing_Files_Or_Folder(bool exists, string expected)
    {
        var vm = MakeVm(new StubLicenseNoticeService { Exists = exists });

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);       // 화면은 열린다(실패도 사람 말로 알린다)
        Assert.True(vm.HasLicenseError);
        Assert.Equal(expected, vm.LicenseErrorMessage);
        Assert.Equal(string.Empty, vm.LicenseText);
    }

    /// <summary>C-T13: 서비스 미주입이어도 크래시 없이 F6 문구로 축퇴한다.</summary>
    [Fact]
    public async Task T13_Null_Service_Does_Not_Crash()
    {
        var vm = MakeVm(notice: null);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);
        Assert.True(vm.HasLicenseError);
        Assert.Equal("라이선스 고지를 불러올 수 없습니다. 개발자에게 알려주세요.", vm.LicenseErrorMessage);
    }

    /// <summary>읽기 실패(F3~F5)는 본문 자리에 문구로 나오고 목록은 유지된다.</summary>
    [Fact]
    public async Task Read_Failure_Shows_Message_And_Keeps_List()
    {
        var stub = new StubLicenseNoticeService { Documents = { Doc("Broken.txt") } };
        stub.Texts["Broken.txt"] = LicenseTextResult.Fail("이 파일은 비어 있습니다. 배포 산출물이 불완전할 수 있습니다.");
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.Single(vm.LicenseDocuments);
        Assert.True(vm.HasLicenseError);
        Assert.Contains("비어 있습니다", vm.LicenseErrorMessage);
        Assert.Equal(string.Empty, vm.LicenseText);
    }

    /// <summary>닫으면 본문(최대 수십 KB)과 목록을 놓아준다 — 오버레이가 메모리에 상주하지 않는다.</summary>
    [Fact]
    public async Task Close_Releases_Text_And_List()
    {
        var stub = new StubLicenseNoticeService { Documents = { Doc("README.txt") } };
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        vm.CloseLicenseViewerCommand.Execute(null);

        Assert.False(vm.IsLicenseViewerOpen);
        Assert.Empty(vm.LicenseDocuments);
        Assert.Null(vm.SelectedLicenseDocument);
        Assert.Equal(string.Empty, vm.LicenseText);
        Assert.Equal(string.Empty, vm.LicenseSelectionSummary);
        Assert.False(vm.HasLicenseError);
    }

    /// <summary>다시 열면 재열거한다(파일 교체·삭제를 반영한다).</summary>
    [Fact]
    public async Task Reopen_Re_Enumerates()
    {
        var stub = new StubLicenseNoticeService { Documents = { Doc("README.txt") } };
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        vm.CloseLicenseViewerCommand.Execute(null);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.Equal(2, stub.ListCalls);
    }

    // ── C-T14 / C-T14b: 로그인 무관 접근(AC-C1·AC-C2) ──

    /// <summary>
    /// C-T14: 게스트·실계정·테스트 계정 3상태에서 열기·열람이 <b>전부 동일하게</b> 동작한다(AC-C1).
    /// 실제 파일을 읽어 한글·개행 보존까지 함께 확인한다.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("real")]
    [InlineData("test")]
    public async Task T14_Works_For_Guest_Real_And_Test_Account(string? kind)
    {
        var service = RealServiceWith(
            ("README.txt", "오픈소스 라이선스 색인\r\nFFmpeg-COPYING.GPLv3.txt 를 보세요."),
            ("FFmpeg-COPYING.GPLv3.txt", "GNU GENERAL PUBLIC LICENSE\r\nVersion 3"));

        User? user = kind switch
        {
            "real" => new User { Id = "real", Email = "real@example.com", Role = UserRole.User },
            // 테스트 계정도 결국 SessionContext에 들어간 User 하나다 — 뷰어는 그 차이를 보지 않는다.
            "test" => TestModeOptions.FromIni(IniFile.Parse("[Test]\nTestMode=1\nRole=admin\n")).CreateUser(),
            _ => null,
        };
        var vm = MakeVm(service, user);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);
        Assert.False(vm.HasLicenseError);
        Assert.Equal("README.txt", vm.SelectedLicenseDocument!.DisplayName);
        Assert.Contains("오픈소스 라이선스 색인", vm.LicenseText);
        Assert.Contains("\r\n", vm.LicenseText);   // 개행을 변환하지 않는다("그대로 노출")

        vm.SelectedLicenseDocument = vm.LicenseDocuments.First(d => d.DisplayName.StartsWith("FFmpeg", StringComparison.Ordinal));
        await vm.LicenseLoadTask!;
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", vm.LicenseText);
    }

    /// <summary>
    /// C-T14b(AC-C2 정적 검사): 뷰어 구역이 계정·역할·테스트 모드를 참조하지 않는다.
    /// <para>
    /// 왜 정적 검사인가: 나중에 누군가 "권한 있는 사람만 보게" 게이트를 붙이는 것은 <b>추가되는 코드</b>이므로
    /// 기존 단위 테스트가 잡지 못한다. 그런 게이트는 GPLv3 §4 이행을 약화시킨다.
    /// </para>
    /// </summary>
    [Fact]
    public void T14b_License_Region_Has_No_Account_Or_Role_References()
    {
        var source = File.ReadAllText(Path.Combine(FindAppDir(), "ViewModels", "SettingsViewModel.cs"));

        int begin = source.IndexOf("[license-viewer:begin]", StringComparison.Ordinal);
        int end = source.IndexOf("[license-viewer:end]", StringComparison.Ordinal);
        Assert.True(begin > 0 && end > begin,
            "SettingsViewModel.cs 의 라이선스 뷰어 구역 표식([license-viewer:begin]/[end])을 찾지 못했다 — "
            + "표식을 지우면 AC-C2를 자동으로 지킬 수 없다");

        var region = source[begin..end];
        foreach (var forbidden in new[] { "CurrentUser", "IsLoggedIn", "IsGuest", "IsTempUser", "Role", "TestMode" })
        {
            Assert.False(region.Contains(forbidden, StringComparison.Ordinal),
                $"라이선스 뷰어 구역이 '{forbidden}' 을 참조한다 — 고지 접근은 로그인·역할과 무관해야 한다(AC-C2)");
        }
    }

    /// <summary>
    /// 설정 화면 버튼에 <c>IsEnabled</c> 바인딩이 없다(게스트도 활성이어야 한다).
    /// 바인딩 유무는 XAML 텍스트로만 확인할 수 있고, 붙어 있으면 게스트에게 조용히 비활성으로 보인다.
    /// </summary>
    [Fact]
    public void License_Button_Is_Always_Enabled()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));

        var button = Regex.Match(xaml, @"<Button[^>]*Content=""오픈소스 라이선스""[^>]*/>", RegexOptions.Singleline);
        Assert.True(button.Success, "SettingsView.xaml 에서 [오픈소스 라이선스] 버튼을 찾지 못함");
        Assert.DoesNotContain("IsEnabled", button.Value);
        Assert.Contains("OpenLicenseViewerCommand", button.Value);
    }

    /// <summary>
    /// UI에 폴더 경로가 노출되지 않는다(요구: "경로를 적어주지 말고"). 실패 문구에도 경로가 없다.
    /// </summary>
    [Fact]
    public void No_Folder_Path_In_Ui()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));
        Assert.DoesNotContain("FolderPath", xaml);

        foreach (var message in new[]
                 {
                     SettingsViewModel.LicenseFolderMissingMessage,
                     SettingsViewModel.LicenseFilesMissingMessage,
                     SettingsViewModel.LicenseUnavailableMessage,
                 })
        {
            Assert.DoesNotContain(":\\", message);
            Assert.DoesNotContain("/", message);
        }
    }

    /// <summary>
    /// 본문 <c>TextBox</c>가 <c>ScrollViewer</c>로 감싸이지 않았고 <c>NoWrap</c>·선택 가능하다.
    /// 감싸면 TextBox가 무한 높이를 요구해 자체 스크롤이 죽고, Wrap을 걸면 원문 정렬이 깨진다("그대로" 위반).
    /// </summary>
    [Fact]
    public void License_Body_TextBox_Is_Selectable_NoWrap_And_Self_Scrolling()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));

        var body = Regex.Match(xaml, @"<TextBox[^>]*\{Binding LicenseText[^>]*/>", RegexOptions.Singleline);
        Assert.True(body.Success, "라이선스 본문 TextBox를 찾지 못함");
        Assert.Contains(@"IsReadOnly=""True""", body.Value);
        Assert.Contains(@"AcceptsReturn=""True""", body.Value);
        Assert.Contains(@"TextWrapping=""NoWrap""", body.Value);
        Assert.Contains(@"VerticalScrollBarVisibility=""Auto""", body.Value);
        Assert.Contains(@"HorizontalScrollBarVisibility=""Auto""", body.Value);

        // 오버레이가 sticky 저장 바까지 덮는다(모달 성립 조건).
        var overlay = Regex.Match(xaml, @"<Grid[^>]*IsLicenseViewerOpen[^>]*>", RegexOptions.Singleline);
        Assert.True(overlay.Success, "라이선스 오버레이 루트 Grid를 찾지 못함");
        Assert.Contains(@"Grid.RowSpan=""2""", overlay.Value);
    }

    private static string FindAppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MCPhoto.App");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/MCPhoto.App 를 찾지 못함");
    }
}
