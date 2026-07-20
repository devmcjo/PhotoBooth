using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

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
}
