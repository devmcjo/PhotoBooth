using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it5 Step 1(B6 정정): QR on 업로드 실패의 우아한 처리. 목 IUploadService로 상태 전이 검증.
/// 실패 → UploadFailed·QR 없음(흐름 비차단), 성공 → QR 생성. OnEnterAsync는 성공/실패 모두 Navigate 안 함.
/// </summary>
public class QrPopupUploadTests
{
    // ── 테스트용 최소 스텁 ──

    private sealed class StubUploadService : IUploadService
    {
        private readonly bool _throw;
        public StubUploadService(bool @throw) => _throw = @throw;

        public Task<ResultSession> UploadResultAsync(string? finalImagePath, string? timelapsePath,
            int retentionHours, string hostingBaseUrl, CancellationToken ct = default)
        {
            if (_throw) throw new InvalidOperationException("버킷 없음(404) 모사");
            return Task.FromResult(new ResultSession
            {
                Id = "s1",
                DownloadPageUrl = "https://example.web.app/?s=token"
            });
        }

        public Task<int> PurgeExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubQrService : IQrService
    {
        // 1×1 PNG(디코드 가능한 최소 바이트) — 실제 렌더는 안 하되 non-null 생성 확인용.
        public byte[] GenerateQrPng(string text, int pixelsPerModule = 20)
            => Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static AppShellViewModel MakeShell(SessionContext session, ISettingsService settings)
        => new(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);

    private static (QrPopupViewModel vm, SessionContext session) MakeVm(bool uploadThrows, bool saveLocalCopy)
    {
        var session = new SessionContext { FinalImagePath = "final.jpg" };
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"qr_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableQrDelivery = true;
        settings.Current.SaveLocalCopy = saveLocalCopy;

        var shell = MakeShell(session, settings);
        var vm = new QrPopupViewModel(shell, new StubUploadService(uploadThrows), new StubQrService());
        return (vm, session);
    }

    [Fact]
    public async Task Upload_Failure_Sets_UploadFailed_No_Qr()
    {
        var (vm, _) = MakeVm(uploadThrows: true, saveLocalCopy: true);
        await vm.OnEnterAsync();

        Assert.True(vm.UploadFailed);
        Assert.False(vm.UploadSucceeded);
        Assert.Null(vm.QrImage);
        Assert.False(vm.IsUploading);
    }

    [Fact]
    public async Task Upload_Failure_With_LocalSave_Says_Saved_On_Device()
    {
        var (vm, _) = MakeVm(uploadThrows: true, saveLocalCopy: true);
        await vm.OnEnterAsync();
        Assert.Contains("기기에 저장", vm.StatusMessage); // 비위협 문구(로컬 보존 안내)
    }

    [Fact]
    public async Task Upload_Failure_Without_LocalSave_Suggests_Enabling_It()
    {
        var (vm, _) = MakeVm(uploadThrows: true, saveLocalCopy: false);
        await vm.OnEnterAsync();
        Assert.Contains("로컬 저장", vm.StatusMessage);
    }

    [Fact]
    public async Task Upload_Success_Generates_Qr()
    {
        var (vm, session) = MakeVm(uploadThrows: false, saveLocalCopy: true);
        await vm.OnEnterAsync();

        Assert.True(vm.UploadSucceeded);
        Assert.False(vm.UploadFailed);
        Assert.NotNull(vm.QrImage);
        Assert.NotNull(session.Result);
        Assert.False(vm.IsUploading);
    }
}
