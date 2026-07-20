using System.IO;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;

namespace MCPhoto.Tests;

/// <summary>WBS Step 8: UploadService 오케스트레이션(업로드→문서생성, 타임랩스 분기)을 mock으로 검증.</summary>
public class UploadServiceTests
{
    /// <summary>인메모리 mock Firebase 클라이언트.</summary>
    private sealed class MockFirebaseClient : IFirebaseClient
    {
        public bool IsInitialized { get; set; } = true;
        public string Bucket => "mcphoto.appspot.com";
        public List<string> UploadedPaths { get; } = new();
        public List<ResultSession> CreatedSessions { get; } = new();
        public List<string> DeletedPrefixes { get; } = new();
        public List<string> DeletedSessions { get; } = new();
        public List<ResultSession> ExpiredToReturn { get; } = new();

        public Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, CancellationToken ct = default)
        {
            UploadedPaths.Add(storagePath);
            return Task.FromResult($"token-{UploadedPaths.Count}");
        }

        public Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default)
        {
            DeletedPrefixes.Add(prefix);
            return Task.CompletedTask;
        }

        public Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default)
        {
            CreatedSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResultSession>>(ExpiredToReturn);

        public Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default)
        {
            DeletedSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private static string MakeTempFile(string ext)
    {
        var p = Path.Combine(Path.GetTempPath(), $"mcphoto_up_{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    [Fact]
    public async Task Upload_With_Timelapse_Creates_Session_With_Both_Urls()
    {
        var mock = new MockFirebaseClient();
        var svc = new UploadService(mock);
        var final = MakeTempFile(".jpg");
        var timelapse = MakeTempFile(".mp4");

        try
        {
            var session = await svc.UploadResultAsync(final, timelapse, 24, "https://mcphoto.web.app");

            // 최종 이미지 + 타임랩스 2개 업로드
            Assert.Equal(2, mock.UploadedPaths.Count);
            Assert.Contains(mock.UploadedPaths, p => p.EndsWith("/final.jpg"));
            Assert.Contains(mock.UploadedPaths, p => p.EndsWith("/timelapse.mp4"));

            // ResultSession 문서 1건 생성
            Assert.Single(mock.CreatedSessions);
            Assert.NotNull(session.FinalImageUrl);
            Assert.NotNull(session.TimelapseUrl);
            Assert.Equal($"https://mcphoto.web.app/?s={session.Id}", session.DownloadPageUrl);
            Assert.Equal(session.CreatedAt.AddHours(24), session.ExpiresAt);
        }
        finally
        {
            File.Delete(final); File.Delete(timelapse);
        }
    }

    [Fact]
    public async Task Upload_Without_Timelapse_Only_Final()
    {
        var mock = new MockFirebaseClient();
        var svc = new UploadService(mock);
        var final = MakeTempFile(".png");

        try
        {
            var session = await svc.UploadResultAsync(final, null, 48, "https://x.web.app");

            Assert.Single(mock.UploadedPaths); // final만
            Assert.EndsWith("/final.png", mock.UploadedPaths[0]);
            Assert.Null(session.TimelapseUrl);
        }
        finally { File.Delete(final); }
    }

    [Fact]
    public async Task Upload_Fails_When_Not_Initialized()
    {
        var mock = new MockFirebaseClient { IsInitialized = false };
        var svc = new UploadService(mock);
        var final = MakeTempFile(".jpg");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.UploadResultAsync(final, null, 24, "https://x.web.app"));
        }
        finally { File.Delete(final); }
    }

    [Fact]
    public async Task Purge_Deletes_Both_Storage_And_Document()
    {
        var mock = new MockFirebaseClient();
        mock.ExpiredToReturn.Add(new ResultSession { Id = "expired1" });
        mock.ExpiredToReturn.Add(new ResultSession { Id = "expired2" });
        var svc = new UploadService(mock);

        var count = await svc.PurgeExpiredAsync();

        Assert.Equal(2, count);
        // 불변식: 문서 + Storage 함께 정리
        Assert.Contains("results/expired1/", mock.DeletedPrefixes);
        Assert.Contains("expired1", mock.DeletedSessions);
        Assert.Contains("results/expired2/", mock.DeletedPrefixes);
    }
}
