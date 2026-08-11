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

    /// <summary>
    /// 요약·문서 목록·본문 결과를 주입하는 고지 서비스 스텁(파일 미접근).
    /// <see cref="ReadTextCalls"/>가 있는 이유: it24의 핵심 행동 변화가 "<b>열 때 전문을 읽지 않는다</b>"이며,
    /// 그것은 호출 횟수로만 관측된다(속성 값만 보면 "아직 안 읽었다"와 "읽고 비웠다"를 구별할 수 없다).
    /// </summary>
    private sealed class StubLicenseNoticeService : ILicenseNoticeService
    {
        public string FolderPath { get; init; } = @"C:\app\licenses";
        public bool Exists { get; init; } = true;
        public List<LicenseDocument> Documents { get; init; } = new();
        public List<LicenseComponent> Components { get; init; } = new();
        public List<LicenseDocument> Unlisted { get; init; } = new();
        public string? UpdatedOn { get; init; } = "2026-08-11";
        public string? DegradedMessage { get; init; }
        public Dictionary<string, LicenseTextResult> Texts { get; } = new();
        public int ListCalls { get; private set; }
        public int SummaryCalls { get; private set; }
        public int ReadTextCalls { get; private set; }

        public IReadOnlyList<LicenseDocument> ListDocuments() { ListCalls++; return Documents; }

        public LicenseSummary ReadSummary()
        {
            SummaryCalls++;
            return new LicenseSummary(Components, Unlisted, UpdatedOn, DegradedMessage);
        }

        public LicenseTextResult ReadText(LicenseDocument document)
        {
            ReadTextCalls++;
            return Texts.TryGetValue(document.DisplayName, out var r)
                ? r : LicenseTextResult.Ok($"body of {document.DisplayName}");
        }

        public LicenseTextResult ReadText(string fileName)
        {
            ReadTextCalls++;
            return Texts.TryGetValue(fileName, out var r) ? r : LicenseTextResult.Ok($"body of {fileName}");
        }
    }

    /// <summary>요약 카드 1건 만들기(필수 필드만 채우고 나머지는 인자로 조정).</summary>
    private static LicenseComponent Component(
        string name, string spdx, bool isSelf = false,
        string? version = null, string? noticeFile = "Notice.txt",
        bool fullTextMissing = false, bool noticeMissing = false) =>
        new(IsSelf: isSelf, Name: name, Version: version,
            LicenseName: $"{name} license name", SpdxId: spdx,
            Copyright: "Copyright (c) 2026", Purpose: "용도", Distribution: "배포 형태",
            SourceOffer: isSelf ? null : "제6조에 따라 제공합니다.",
            FullTextFile: $"{name}-FULL.txt", NoticeFile: noticeFile,
            IsFullTextMissing: fullTextMissing, IsNoticeMissing: noticeMissing);

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

    /// <summary>정상 배포물을 모사하는 스텁(본체 1 + 동봉 1, 미참조 문서 0건).</summary>
    private static StubLicenseNoticeService NormalStub() => new()
    {
        Components =
        {
            Component("MC포토", "MIT", isSelf: true, noticeFile: null),
            Component("FFmpeg", "GPL-3.0-or-later", version: "8.1.2-essentials"),
        },
    };

    // ── T-V1 ~ T-V9: 2단 구조의 상태 전이 ──

    /// <summary>
    /// T-V1: 열기 → 요약 카드가 종류별로 채워지고 Level 1에 머문다.
    /// ⭐ 이 테스트의 핵심 단정은 <b>ReadText 호출 0회</b>다 — 종전에는 열자마자 색인 본문(수 KB)을 읽었다.
    /// </summary>
    [Fact]
    public async Task T_V1_Open_Builds_Summary_Without_Reading_Any_Body()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);
        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);
        Assert.True(vm.IsLicenseSummaryPage);
        Assert.False(vm.IsLicenseFullTextPage);

        Assert.Single(vm.LicenseSelfComponents);
        Assert.Single(vm.LicenseBundledComponents);
        Assert.True(vm.HasLicenseSelfComponents);
        Assert.True(vm.HasLicenseBundledComponents);
        Assert.True(vm.HasLicenseComponents);
        Assert.False(vm.HasLicenseDocuments);        // 정상 배포물에는 미참조 문서가 없다
        Assert.False(vm.HasLicenseDegraded);
        Assert.False(vm.HasLicenseError);
        Assert.Equal("2026-08-11 기준", vm.LicenseNoticeAsOfText);

        Assert.Equal(0, stub.ReadTextCalls);         // ⭐ 전문을 읽지 않았다
        Assert.Equal(string.Empty, vm.LicenseText);
        Assert.Equal(1, stub.SummaryCalls);
    }

    /// <summary>
    /// T-V2: [라이선스 전문 보기] → Level 2 + 본문. 헤더는 <c>{구성 요소} · {SPDX}</c>이며
    /// <b>파일명이 들어가지 않는다</b>(요구 R1을 값으로 잠근다).
    /// </summary>
    [Fact]
    public async Task T_V2_Show_Full_Text_Enters_Level2_With_Component_Caption()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        var ffmpeg = vm.LicenseBundledComponents[0];

        await vm.ShowLicenseFullTextCommand.ExecuteAsync(ffmpeg);

        Assert.Equal(SettingsViewModel.LicenseViewerPage.FullText, vm.LicensePage);
        Assert.True(vm.IsLicenseFullTextPage);
        Assert.Equal("body of FFmpeg-FULL.txt", vm.LicenseText);
        Assert.Equal("FFmpeg · GPL-3.0-or-later", vm.LicenseFullTextCaption);
        Assert.Equal("라이선스 전문", vm.LicenseFullTextSubtitle);
        Assert.DoesNotContain(".txt", vm.LicenseFullTextCaption);
        Assert.False(vm.IsLicenseLoading);
        Assert.False(vm.HasLicenseError);
    }

    /// <summary>T-V3: [소스 코드 제공 안내] → 같은 헤더 + 다른 부제 + 상세 고지 본문.</summary>
    [Fact]
    public async Task T_V3_Show_Notice_Uses_Notice_File_And_Its_Own_Subtitle()
    {
        var vm = MakeVm(NormalStub());
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        await vm.ShowLicenseNoticeCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);

        Assert.Equal(SettingsViewModel.LicenseViewerPage.FullText, vm.LicensePage);
        Assert.Equal("body of Notice.txt", vm.LicenseText);
        Assert.Equal("FFmpeg · GPL-3.0-or-later", vm.LicenseFullTextCaption);
        Assert.Equal("소스 코드 제공 안내", vm.LicenseFullTextSubtitle);
    }

    /// <summary>상세 고지가 없는 항목(본체)에서는 그 커맨드가 아무 일도 하지 않는다(버튼도 노출되지 않는다).</summary>
    [Fact]
    public async Task Show_Notice_Is_A_No_Op_When_Component_Has_No_Notice_File()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        var self = vm.LicenseSelfComponents[0];
        Assert.False(self.HasNoticeFile);

        await vm.ShowLicenseNoticeCommand.ExecuteAsync(self);

        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);
        Assert.Equal(0, stub.ReadTextCalls);
    }

    /// <summary>T-V4: [← 뒤로] → Level 1 복귀 + 본문 해제. 카드는 유지된다(재열거하지 않는다).</summary>
    [Fact]
    public async Task T_V4_Back_Returns_To_Summary_And_Releases_Text()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);

        vm.BackToLicenseSummaryCommand.Execute(null);

        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);
        Assert.Equal(string.Empty, vm.LicenseText);
        Assert.Equal(string.Empty, vm.LicenseFullTextCaption);
        Assert.Equal(string.Empty, vm.LicenseFullTextSubtitle);
        Assert.Single(vm.LicenseSelfComponents);      // 카드 유지
        Assert.Single(vm.LicenseBundledComponents);
        Assert.Equal(1, stub.SummaryCalls);           // 재구성 없음
    }

    /// <summary>
    /// T-V5: Esc 1키의 3분기. 닫힌 상태에서 눌러도 <b>아무 것도 바뀌지 않는다</b> —
    /// 설정 화면을 Esc로 닫는 동작을 새로 만들지 않는다는 규격이다.
    /// </summary>
    [Fact]
    public async Task T_V5_Escape_Has_Three_Branches()
    {
        var vm = MakeVm(NormalStub());

        // ① 닫힌 상태 → 무동작
        vm.EscapeLicenseViewerCommand.Execute(null);
        Assert.False(vm.IsLicenseViewerOpen);
        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);

        // ② Level 2 → Level 1 (닫히지 않는다)
        vm.EscapeLicenseViewerCommand.Execute(null);
        Assert.True(vm.IsLicenseViewerOpen);
        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);

        // ③ Level 1 → 닫기
        vm.EscapeLicenseViewerCommand.Execute(null);
        Assert.False(vm.IsLicenseViewerOpen);
    }

    /// <summary>
    /// T-V6: 강등(D1·D2) — 배너 + 폴백 목록이 나오고, <b>폴백에서도 전문에 도달</b>한다.
    /// 요약이 깨졌다고 전문을 못 보게 되면 GPLv3 §4 이행이 후퇴한다(이 재설계의 법적 마지막 그물).
    /// </summary>
    [Fact]
    public async Task T_V6_Degraded_Shows_Banner_And_Still_Reaches_Full_Text()
    {
        const string degraded =
            "라이선스 요약 정보를 읽을 수 없어 동봉된 고지 문서를 그대로 표시합니다. "
            + "배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.";
        var stub = new StubLicenseNoticeService
        {
            DegradedMessage = degraded,
            UpdatedOn = null,
            Unlisted = { Doc("NOTICE.txt", 4057), Doc("FFmpeg-COPYING.GPLv3.txt", 35149) },
        };
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.HasLicenseDegraded);
        Assert.Equal(degraded, vm.LicenseDegradedMessage);
        Assert.False(vm.HasLicenseComponents);
        Assert.True(vm.HasLicenseDocuments);
        Assert.Equal(2, vm.LicenseDocuments.Count);
        Assert.False(vm.HasLicenseError);                  // 강등은 오류 배너가 아니다
        Assert.Equal(string.Empty, vm.LicenseNoticeAsOfText);

        // 폴백 목록 선택 → Level 2 도달(파일명·크기 헤더는 이 경로에서만 허용된다).
        vm.SelectedLicenseDocument = vm.LicenseDocuments[1];
        await vm.LicenseLoadTask!;

        Assert.Equal(SettingsViewModel.LicenseViewerPage.FullText, vm.LicensePage);
        Assert.Equal("body of FFmpeg-COPYING.GPLv3.txt", vm.LicenseText);
        Assert.Equal("FFmpeg-COPYING.GPLv3.txt · 34.3 KB", vm.LicenseFullTextCaption);
    }

    /// <summary>
    /// 강등이면서 폴더에 문서조차 없는 경우는 강등이 아니라 배포 누락이다 —
    /// 폴더 부재(F1)와 파일 0건(F2)을 구분해서 알린다(조치가 다르다).
    /// </summary>
    [Theory]
    [InlineData(true, "라이선스 고지 파일을 찾을 수 없습니다. 배포 산출물이 불완전하므로 개발자에게 알려주세요.")]
    [InlineData(false, "라이선스 고지 폴더를 찾을 수 없습니다. 배포 산출물에 licenses 폴더가 누락된 상태이므로 개발자에게 알려주세요.")]
    public async Task Nothing_At_All_Reports_Missing_Files_Or_Folder(bool exists, string expected)
    {
        var vm = MakeVm(new StubLicenseNoticeService { Exists = exists, DegradedMessage = "요약 없음" });

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);       // 화면은 열린다(실패도 사람 말로 알린다)
        Assert.True(vm.HasLicenseError);
        Assert.Equal(expected, vm.LicenseErrorMessage);
        Assert.False(vm.HasLicenseDegraded);       // 강등 배너가 아니라 오류 배너다
        Assert.Equal(string.Empty, vm.LicenseText);
    }

    /// <summary>
    /// T-V7: stale 폐기 — 요청 출처가 3개로 늘어 "선택 객체 비교"로는 부족해졌다.
    /// 전문 A 요청 도중 B를 요청하면 최종 본문은 <b>B</b>여야 한다.
    /// </summary>
    [Fact]
    public async Task T_V7_Stale_Result_Is_Discarded_By_Request_Id()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        var a = vm.LicenseSelfComponents[0];
        var b = vm.LicenseBundledComponents[0];

        var first = vm.ShowLicenseFullTextCommand.ExecuteAsync(a);
        var second = vm.ShowLicenseFullTextCommand.ExecuteAsync(b);
        await Task.WhenAll(first, second);

        Assert.Equal("body of FFmpeg-FULL.txt", vm.LicenseText);
        Assert.Equal("FFmpeg · GPL-3.0-or-later", vm.LicenseFullTextCaption);
        Assert.False(vm.IsLicenseLoading);
    }

    /// <summary>읽기 실패(F3~F5)는 Level 2 본문 자리에 문구로 나오고 카드는 유지된다.</summary>
    [Fact]
    public async Task Read_Failure_Shows_Message_And_Keeps_Cards()
    {
        var stub = NormalStub();
        stub.Texts["FFmpeg-FULL.txt"] =
            LicenseTextResult.Fail("이 파일은 비어 있습니다. 배포 산출물이 불완전할 수 있습니다.");
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);

        Assert.True(vm.HasLicenseError);
        Assert.Contains("비어 있습니다", vm.LicenseErrorMessage);
        Assert.Equal(string.Empty, vm.LicenseText);
        Assert.Single(vm.LicenseBundledComponents);         // 카드는 살아 있다
        Assert.Equal(SettingsViewModel.LicenseViewerPage.FullText, vm.LicensePage);
    }

    /// <summary>
    /// 선언된 파일이 부재인 카드도 버튼이 살아 있고, 누르면 사유가 나온다 —
    /// 버튼을 숨기거나 비활성하면 누락을 감추는 것이다.
    /// </summary>
    [Fact]
    public async Task Missing_File_Card_Still_Reaches_Level2_And_Shows_Reason()
    {
        var stub = new StubLicenseNoticeService
        {
            Components = { Component("FFmpeg", "GPL-3.0-or-later", fullTextMissing: true) },
        };
        stub.Texts["FFmpeg-FULL.txt"] =
            LicenseTextResult.Fail("이 파일을 읽을 수 없습니다. 파일이 사용 중이거나 접근 권한이 없습니다.");
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        var card = vm.LicenseBundledComponents[0];
        Assert.True(card.IsAnyFileMissing);

        await vm.ShowLicenseFullTextCommand.ExecuteAsync(card);

        Assert.Equal(SettingsViewModel.LicenseViewerPage.FullText, vm.LicensePage);
        Assert.True(vm.HasLicenseError);
        Assert.Contains("읽을 수 없습니다", vm.LicenseErrorMessage);
    }

    /// <summary>T-V8: 닫으면 본문·카드·폴백 목록·배너를 모두 놓아주고 Level 1로 되돌아간다.</summary>
    [Fact]
    public async Task T_V8_Close_Resets_Everything()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);

        vm.CloseLicenseViewerCommand.Execute(null);

        Assert.False(vm.IsLicenseViewerOpen);
        Assert.Equal(SettingsViewModel.LicenseViewerPage.Summary, vm.LicensePage);
        Assert.Empty(vm.LicenseSelfComponents);
        Assert.Empty(vm.LicenseBundledComponents);
        Assert.Empty(vm.LicenseDocuments);
        Assert.Null(vm.SelectedLicenseDocument);
        Assert.Equal(string.Empty, vm.LicenseText);
        Assert.Equal(string.Empty, vm.LicenseFullTextCaption);
        Assert.Equal(string.Empty, vm.LicenseFullTextSubtitle);
        Assert.Equal(string.Empty, vm.LicenseNoticeAsOfText);
        Assert.False(vm.HasLicenseError);
        Assert.False(vm.HasLicenseDegraded);
        Assert.False(vm.HasLicenseComponents);
    }

    /// <summary>T-V9: 서비스 미주입이어도 크래시 없이 F6 문구로 축퇴한다.</summary>
    [Fact]
    public async Task T_V9_Null_Service_Does_Not_Crash()
    {
        var vm = MakeVm(notice: null);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLicenseViewerOpen);
        Assert.True(vm.HasLicenseError);
        Assert.Equal("라이선스 고지를 불러올 수 없습니다. 개발자에게 알려주세요.", vm.LicenseErrorMessage);
    }

    /// <summary>다시 열면 요약을 재구성한다(파일 교체·삭제를 반영한다).</summary>
    [Fact]
    public async Task Reopen_Rebuilds_Summary()
    {
        var stub = NormalStub();
        var vm = MakeVm(stub);

        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);
        vm.CloseLicenseViewerCommand.Execute(null);
        await vm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.Equal(2, stub.SummaryCalls);
        Assert.Single(vm.LicenseBundledComponents);
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
        // 실제 파일 + 실제 매니페스트로 요약→카드→전문 경로를 끝까지 걷는다(한글·개행 보존까지 확인).
        var service = RealServiceWith(
            ("notice-manifest.json", """
                {
                  "schemaVersion": 1,
                  "updatedOn": "2026-08-11",
                  "components": [
                    { "kind": "self", "name": "MC포토", "licenseName": "MIT License", "spdxId": "MIT",
                      "copyright": "Copyright (c) 2025 devmcjo", "fullTextFile": "Mit.txt" },
                    { "kind": "redistributed", "name": "FFmpeg", "version": "8.1.2",
                      "licenseName": "GNU General Public License v3.0 or later",
                      "spdxId": "GPL-3.0-or-later", "purpose": "동영상 녹화",
                      "fullTextFile": "FFmpeg-COPYING.GPLv3.txt", "noticeFile": "FFmpeg-NOTICE.txt" }
                  ]
                }
                """),
            ("NOTICE.txt", "라이선스 고지 색인\r\n같은 폴더의 문서를 보세요."),
            ("Mit.txt", "MIT License\r\nCopyright (c) 2025 devmcjo"),
            ("FFmpeg-NOTICE.txt", "FFmpeg 고지 및 소스 코드 제공 안내\r\n3년"),
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
        Assert.False(vm.HasLicenseDegraded);
        Assert.Single(vm.LicenseSelfComponents);
        Assert.Single(vm.LicenseBundledComponents);
        Assert.False(vm.HasLicenseDocuments);      // 색인은 미참조로 세지 않는다

        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", vm.LicenseText);
        Assert.Contains("\r\n", vm.LicenseText);   // 개행을 변환하지 않는다("그대로 노출")
        Assert.Equal("FFmpeg · GPL-3.0-or-later", vm.LicenseFullTextCaption);

        await vm.ShowLicenseNoticeCommand.ExecuteAsync(vm.LicenseBundledComponents[0]);
        Assert.Contains("소스 코드 제공 안내", vm.LicenseText);   // 한글이 온전하다

        vm.BackToLicenseSummaryCommand.Execute(null);
        await vm.ShowLicenseFullTextCommand.ExecuteAsync(vm.LicenseSelfComponents[0]);
        Assert.Contains("MIT License", vm.LicenseText);
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

        // it24: 진입점 라벨과 도착 화면 제목을 같은 문자열로 통일했다(다르면 어디로 가는지 알 수 없다).
        var button = Regex.Match(xaml, @"<Button[^>]*Content=""프로젝트 라이선스 고지""[^>]*/>",
            RegexOptions.Singleline);
        Assert.True(button.Success, "SettingsView.xaml 에서 [프로젝트 라이선스 고지] 버튼을 찾지 못함");
        Assert.DoesNotContain("IsEnabled", button.Value);
        Assert.Contains("OpenLicenseViewerCommand", button.Value);

        // 카드 액션 2종에도 IsEnabled가 붙지 않는다 — 전문 도달 경로에 게이트를 두면 GPLv3 §4 이행이 약화된다.
        var cardTemplate = LicenseCardTemplate(xaml);
        foreach (var action in Regex.Matches(cardTemplate, @"<Button\b[^>]*?/>", RegexOptions.Singleline)
                     .Select(m => m.Value))
        {
            Assert.DoesNotContain("IsEnabled", action);
        }
    }

    /// <summary>
    /// UI에 폴더 경로가 노출되지 않는다(요구: "경로를 적어주지 말고"). 실패·강등 문구에도 경로가 없다.
    /// ⚠️ 강등 문구에 <c>licenses/notice-manifest.json</c> 처럼 쓰면 이 테스트가 깨진다 — 그것이 의도다.
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
                     LicenseNoticeService.SummaryMissingMessage,
                     LicenseNoticeService.SummaryUnreadableMessage,
                 })
        {
            Assert.DoesNotContain(":\\", message);
            Assert.DoesNotContain("/", message);
        }
    }

    /// <summary>
    /// T-X1: 요약 카드가 구성 요소 정보를 바인딩하고 <b>파일명은 바인딩하지 않는다</b>(요구 R1의 정적 잠금).
    /// 카드에 파일명이 새는 회귀는 화면을 열어야만 보이므로 XAML 텍스트로 고정한다.
    /// </summary>
    [Fact]
    public void License_Card_Binds_Component_Info_But_Never_File_Names()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));
        var card = LicenseCardTemplate(xaml);

        foreach (var member in new[] { "Name", "SpdxId", "LicenseName", "Version", "Copyright", "Purpose" })
            Assert.Contains($"{{Binding {member}}}", card);

        // ⛔ 파일명·크기는 카드 어디에도 나오지 않는다(HasNoticeFile 같은 판정용 bool은 값이 아니라 조건이다).
        Assert.DoesNotContain("{Binding FullTextFile", card);
        Assert.DoesNotContain("{Binding NoticeFile", card);
        Assert.DoesNotContain("{Binding DisplayName", card);
        Assert.DoesNotContain("{Binding SizeText", card);

        // 카드 안 커맨드는 UserControl 조상 경유여야 한다 — {Binding ShowLicense…}로 쓰면 항목에서
        // 커맨드를 찾아 조용히 아무 일도 하지 않는다(리포에서 반복되는 함정).
        foreach (var command in new[] { "ShowLicenseFullTextCommand", "ShowLicenseNoticeCommand" })
        {
            Assert.Contains($"DataContext.{command}", card);
            Assert.DoesNotContain($"{{Binding {command}", card);
        }
        Assert.Contains("RelativeSource={RelativeSource AncestorType=UserControl}", card);
        Assert.Contains("CommandParameter=\"{Binding}\"", card);

        // F7 동결 문구(누락을 카드 안에서 알린다 — 카드를 숨기지 않는다).
        Assert.Contains("이 항목의 고지 파일이 배포물에 없습니다. 개발자에게 알려주세요.", card);
    }

    /// <summary>
    /// Esc <c>KeyBinding</c>이 3분기 커맨드를 지목한다. 코드비하인드를 만들지 않았음도 함께 확인한다
    /// (설정 화면 코드비하인드에 키 처리가 생기면 이 규격이 두 곳으로 갈라진다).
    /// </summary>
    [Fact]
    public void Escape_Is_Bound_Through_Input_Bindings_Only()
    {
        var appDir = FindAppDir();
        var xaml = File.ReadAllText(Path.Combine(appDir, "Views", "SettingsView.xaml"));

        var binding = Regex.Match(xaml, @"<KeyBinding\b[^>]*?/>", RegexOptions.Singleline);
        Assert.True(binding.Success, "SettingsView.xaml 에서 Esc KeyBinding을 찾지 못함");
        Assert.Contains(@"Key=""Escape""", binding.Value);
        Assert.Contains("EscapeLicenseViewerCommand", binding.Value);

        var codeBehind = File.ReadAllText(Path.Combine(appDir, "Views", "SettingsView.xaml.cs"));
        Assert.DoesNotContain("KeyDown", codeBehind);
        Assert.DoesNotContain("Key.Escape", codeBehind);
    }

    /// <summary>
    /// 2단 전환이 <b>형제 Grid의 Visibility</b>로 이뤄지고, 전문 TextBox가 Level 1의 ScrollViewer 안에
    /// 들어가지 않았음을 고정한다. 감싸면 TextBox가 무한 높이를 요구해 자체 스크롤이 죽는다(it23 실측 함정).
    /// </summary>
    [Fact]
    public void Level1_And_Level2_Are_Siblings_And_Body_Is_Outside_ScrollViewer()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));

        Assert.Contains("{Binding IsLicenseSummaryPage, Converter={StaticResource BoolToVis}}", xaml);
        Assert.Contains("{Binding IsLicenseFullTextPage, Converter={StaticResource BoolToVis}}", xaml);

        // Level 1의 ScrollViewer가 열린 지점부터 전문 TextBox까지의 사이에 </ScrollViewer>가 있어야 한다
        // (= TextBox가 그 ScrollViewer 밖이다).
        int scroll = xaml.IndexOf("<ScrollViewer Grid.Row=\"4\"", StringComparison.Ordinal);
        Assert.True(scroll > 0, "Level 1 요약 ScrollViewer를 찾지 못함");
        int body = xaml.IndexOf("{Binding LicenseText, Mode=OneWay}", StringComparison.Ordinal);
        Assert.True(body > scroll, "전문 TextBox가 요약 ScrollViewer보다 앞에 있다");
        Assert.Contains("</ScrollViewer>", xaml[scroll..body]);
    }

    /// <summary>
    /// 카드 템플릿이 참조하는 로컬 스타일이 <b>템플릿보다 앞에</b> 선언되어 있다.
    /// <para>
    /// 같은 <c>ResourceDictionary</c> 안에서 <c>StaticResource</c> 전방 참조는 로드 시점에 예외가 되고,
    /// 그 예외는 설정 화면에 실제로 진입할 때만 터진다 — 빌드도 테마 키 검증도 잡지 못하는 사각지대다
    /// (리포에 "창이 안 뜬다" 사고 이력이 있다).
    /// </para>
    /// </summary>
    [Fact]
    public void License_Card_Template_Comes_After_The_Styles_It_References()
    {
        var xaml = File.ReadAllText(Path.Combine(FindAppDir(), "Views", "SettingsView.xaml"));
        int template = xaml.IndexOf(@"<DataTemplate x:Key=""LicenseCard"">", StringComparison.Ordinal);
        Assert.True(template > 0, "요약 카드 DataTemplate(LicenseCard)을 찾지 못함");

        foreach (var key in new[] { "LicenseBadge", "LicenseMetaLabel", "LicenseMetaValue" })
        {
            int declared = xaml.IndexOf($@"x:Key=""{key}""", StringComparison.Ordinal);
            Assert.True(declared > 0, $"로컬 스타일 {key} 를 찾지 못함");
            Assert.True(declared < template,
                $"{key} 가 LicenseCard 뒤에 선언되어 전방 참조가 된다 — 설정 화면 진입 시 예외로 터진다");
        }
    }

    /// <summary>요약 카드 <c>DataTemplate</c> 본문만 잘라낸다(정적 검사의 대상 범위를 좁힌다).</summary>
    private static string LicenseCardTemplate(string xaml)
    {
        var match = Regex.Match(xaml, @"<DataTemplate x:Key=""LicenseCard"">.*?</DataTemplate>",
            RegexOptions.Singleline);
        Assert.True(match.Success, "SettingsView.xaml 에서 요약 카드 DataTemplate(LicenseCard)을 찾지 못함");
        return match.Value;
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
