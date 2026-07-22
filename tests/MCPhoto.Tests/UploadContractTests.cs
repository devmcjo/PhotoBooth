using System.IO;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;

namespace MCPhoto.Tests;

/// <summary>WBS Step 8: Storage 경로·토큰 URL·downloadPageUrl·expiresAt 조립이 계약과 일치.</summary>
public class UploadContractTests
{
    [Fact]
    public void SessionToken_Is_Uuid()
    {
        var token = UploadContract.NewSessionToken();
        Assert.True(Guid.TryParse(token, out _)); // UUIDv4 형식(추측 불가)
    }

    [Fact]
    public void FinalImagePath_Follows_Convention()
    {
        Assert.Equal("results/abc123/final.jpg", UploadContract.FinalImagePath("abc123", OutputFormat.Jpg));
        Assert.Equal("results/abc123/final.png", UploadContract.FinalImagePath("abc123", OutputFormat.Png));
    }

    [Fact]
    public void TimelapsePath_Follows_Convention()
    {
        Assert.Equal("results/xyz/timelapse.mp4", UploadContract.TimelapsePath("xyz"));
    }

    [Fact]
    public void TokenDownloadUrl_Encodes_Slashes()
    {
        var url = UploadContract.TokenDownloadUrl("mcphoto.appspot.com", "results/sid/final.jpg", "tok-123");

        Assert.StartsWith("https://firebasestorage.googleapis.com/v0/b/mcphoto.appspot.com/o/", url);
        Assert.Contains("results%2Fsid%2Ffinal.jpg", url); // 슬래시 %2F 인코딩
        Assert.Contains("?alt=media&token=tok-123", url);
    }

    [Fact]
    public void DownloadPageUrl_Query_Form()
    {
        // 기본안: 쿼리형 /?s={token}
        var url = UploadContract.DownloadPageUrl("https://mcphoto.web.app", "TOKEN");
        Assert.Equal("https://mcphoto.web.app/?s=TOKEN", url);
    }

    [Fact]
    public void DownloadPageUrl_Trims_Trailing_Slash()
    {
        var url = UploadContract.DownloadPageUrl("https://mcphoto.web.app/", "TOKEN");
        Assert.Equal("https://mcphoto.web.app/?s=TOKEN", url);
    }

    [Fact]
    public void ExpiresAt_Is_CreatedAt_Plus_RetentionHours()
    {
        var created = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var expires = UploadContract.ComputeExpiresAt(created, 24);
        Assert.Equal(created.AddHours(24), expires);
    }

    [Fact]
    public void ExpiresAt_Respects_Custom_Retention()
    {
        var created = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(created.AddHours(1), UploadContract.ComputeExpiresAt(created, 1));
        Assert.Equal(created.AddHours(72), UploadContract.ComputeExpiresAt(created, 72));
    }

    // ── it7 Step 3 (F2): 미디어 선택 업로드 — 켠 미디어만 URL non-null ──

    /// <summary>업로드 파일마다 고정 토큰을 부여하고 ResultSession을 캡처하는 목.</summary>
    private sealed class FakeFirebaseClient : IFirebaseClient
    {
        public bool IsInitialized => true;
        public string Bucket => "mcphoto.firebasestorage.app";
        public ResultSession? Created { get; private set; }

        public Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, CancellationToken ct = default)
            => Task.FromResult("tok-" + Path.GetFileName(storagePath));
        public Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default)
        {
            Created = session;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ResultSession>)new List<ResultSession>());
        public Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string MakeTempFile(string ext)
    {
        var p = Path.Combine(Path.GetTempPath(), $"mcphoto_up_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public async Task Photo_Only_Sets_FinalUrl_Timelapse_Null()
    {
        var client = new FakeFirebaseClient();
        var svc = new UploadService(client);
        var photo = MakeTempFile(".jpg");
        try
        {
            var r = await svc.UploadResultAsync(photo, timelapsePath: null, retentionHours: 24, hostingBaseUrl: "https://x.web.app");
            Assert.NotNull(r.FinalImageUrl);
            Assert.Null(r.TimelapseUrl);
        }
        finally { File.Delete(photo); }
    }

    [Fact]
    public async Task Timelapse_Only_Sets_TimelapseUrl_Final_Null()
    {
        var client = new FakeFirebaseClient();
        var svc = new UploadService(client);
        var tl = MakeTempFile(".mp4");
        try
        {
            var r = await svc.UploadResultAsync(finalImagePath: null, timelapsePath: tl, retentionHours: 24, hostingBaseUrl: "https://x.web.app");
            Assert.Null(r.FinalImageUrl);
            Assert.NotNull(r.TimelapseUrl);
        }
        finally { File.Delete(tl); }
    }

    [Fact]
    public async Task Both_On_Sets_Both_Urls()
    {
        var client = new FakeFirebaseClient();
        var svc = new UploadService(client);
        var photo = MakeTempFile(".jpg");
        var tl = MakeTempFile(".mp4");
        try
        {
            var r = await svc.UploadResultAsync(photo, tl, 24, "https://x.web.app");
            Assert.NotNull(r.FinalImageUrl);
            Assert.NotNull(r.TimelapseUrl);
        }
        finally { File.Delete(photo); File.Delete(tl); }
    }

    [Fact]
    public async Task Both_Off_Throws()
    {
        var svc = new UploadService(new FakeFirebaseClient());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UploadResultAsync(finalImagePath: null, timelapsePath: null, retentionHours: 24, hostingBaseUrl: "https://x.web.app"));
    }
}
