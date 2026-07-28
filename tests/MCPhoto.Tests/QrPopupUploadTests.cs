using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
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
            int retentionHours, string hostingBaseUrl, IProgress<UploadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (_throw) throw new InvalidOperationException("버킷 없음(404) 모사");
            // it11 #16: 진행률 배선 검증 — 사진 단계 진행 → 마무리 순으로 보고(성공 경로).
            progress?.Report(new UploadProgress(UploadStage.Photo, 0.0));
            progress?.Report(new UploadProgress(UploadStage.Photo, 1.0));
            progress?.Report(new UploadProgress(UploadStage.Finalizing, 1.0));
            return Task.FromResult(new ResultSession
            {
                Id = "s1",
                DownloadPageUrl = "https://example.web.app/?s=token"
            });
        }

        public Task<int> PurgeExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>TempUser 한도 초과(서버 403 매핑) 모사 — 지정 사유로 QrLimitExceededException을 던진다. (it13 §9.3)</summary>
    private sealed class LimitExceededUploadService : IUploadService
    {
        private readonly QrGateReason _reason;
        public LimitExceededUploadService(QrGateReason reason) => _reason = reason;

        public Task<ResultSession> UploadResultAsync(string? finalImagePath, string? timelapsePath,
            int retentionHours, string hostingBaseUrl, IProgress<UploadProgress>? progress = null,
            CancellationToken ct = default)
            => throw new QrLimitExceededException(_reason, "서버 403(테스트 모사)");

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

    private static QrPopupViewModel MakeLimitExceededVm(QrGateReason reason)
    {
        var session = new SessionContext { FinalImagePath = "final.jpg" };
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"qr_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableQrDelivery = true;
        settings.Current.SaveLocalCopy = true;
        var shell = MakeShell(session, settings);
        return new QrPopupViewModel(shell, new LimitExceededUploadService(reason), new StubQrService());
    }

    // ── it13 §9.3: TempUser 한도 초과(업로드 시점 403) 우아 처리 — 사유별 §0 정확 문구 ──

    [Fact]
    public async Task Time_Limit_Exceeded_Shows_Exact_Time_Message()
    {
        var vm = MakeLimitExceededVm(QrGateReason.Time);
        await vm.OnEnterAsync();

        Assert.True(vm.UploadFailed);
        Assert.False(vm.UploadSucceeded);
        Assert.Null(vm.QrImage);
        Assert.Equal("무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.", vm.StatusMessage);
    }

    [Fact]
    public async Task Count_Limit_Exceeded_Shows_Exact_Count_Message()
    {
        var vm = MakeLimitExceededVm(QrGateReason.Count);
        await vm.OnEnterAsync();

        Assert.True(vm.UploadFailed);
        Assert.Equal("무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.", vm.StatusMessage);
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

    // ── it11 #16: 업로드 진행률 배선 ──

    [Fact]
    public async Task Upload_Reports_Progress_And_Clears_Indeterminate()
    {
        // StubUploadService가 Photo 0→1, Finalizing 1을 Report → VM 진행률 갱신 확인.
        var (vm, _) = MakeVm(uploadThrows: false, saveLocalCopy: true);
        await vm.OnEnterAsync();

        Assert.False(vm.IsIndeterminate);      // 세밀 진행 콜백 도착 후 무한 표시 해제
        Assert.True(vm.UploadProgress > 0);    // 진행률 갱신됨(사진만 전송 → 마지막 1.0)
        Assert.Equal(1.0, vm.UploadProgress);  // Finalizing 단계는 전체 100%
    }

    [Theory]
    // 둘 다 전송: 사진 구간 [0,0.5], 타임랩스 구간 [0.5,1.0]
    [InlineData(UploadStage.Photo, 0.0, true, true, 0.0)]
    [InlineData(UploadStage.Photo, 1.0, true, true, 0.5)]
    [InlineData(UploadStage.Timelapse, 0.0, true, true, 0.5)]
    [InlineData(UploadStage.Timelapse, 1.0, true, true, 1.0)]
    [InlineData(UploadStage.Timelapse, 0.5, true, true, 0.75)]
    // 사진만: 사진 단계가 전체 100%
    [InlineData(UploadStage.Photo, 0.5, true, false, 0.5)]
    [InlineData(UploadStage.Photo, 1.0, true, false, 1.0)]
    // 타임랩스만: 타임랩스 단계가 전체 100%
    [InlineData(UploadStage.Timelapse, 0.5, false, true, 0.5)]
    [InlineData(UploadStage.Timelapse, 1.0, false, true, 1.0)]
    // Finalizing은 구성 무관 항상 100%
    [InlineData(UploadStage.Finalizing, 0.0, true, true, 1.0)]
    [InlineData(UploadStage.Finalizing, 1.0, true, false, 1.0)]
    // 경계: fraction 범위 밖은 클램프
    [InlineData(UploadStage.Photo, -0.5, true, false, 0.0)]
    [InlineData(UploadStage.Photo, 1.5, true, false, 1.0)]
    public void ComputeOverall_Normalizes_By_Media_Composition(
        UploadStage stage, double fraction, bool hasPhoto, bool hasTimelapse, double expected)
    {
        var actual = QrPopupViewModel.ComputeOverall(stage, fraction, hasPhoto, hasTimelapse);
        Assert.Equal(expected, actual, precision: 6);
    }
}
