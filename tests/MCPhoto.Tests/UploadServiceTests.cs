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

        public Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, IProgress<double>? fileProgress = null, CancellationToken ct = default)
        {
            UploadedPaths.Add(storagePath);
            // it11 #16: 파일 단위 진행률 시뮬레이트(0.5 → 1.0) — UploadService의 stage 합성 검증용.
            fileProgress?.Report(0.5);
            fileProgress?.Report(1.0);
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

    /// <summary>진행 보고를 순서대로 수집하는 동기 IProgress(테스트용).</summary>
    private sealed class CollectingProgress : IProgress<UploadProgress>
    {
        public List<UploadProgress> Reports { get; } = new();
        // UploadService의 stage 경계 보고는 동기 호출 → await 완료 시점에 전부 수집됨.
        public void Report(UploadProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task Upload_Reports_Stage_Progress_In_Order()
    {
        var mock = new MockFirebaseClient();
        var svc = new UploadService(mock);
        var final = MakeTempFile(".jpg");
        var timelapse = MakeTempFile(".mp4");
        var progress = new CollectingProgress();

        try
        {
            await svc.UploadResultAsync(final, timelapse, 24, "https://mcphoto.web.app", progress);

            // 동기 stage 경계 보고: Photo 0→1, Timelapse 0→1, Finalizing 1(순서 보존).
            var stages = progress.Reports.Select(r => r.Stage).ToList();
            Assert.Contains(UploadStage.Photo, stages);
            Assert.Contains(UploadStage.Timelapse, stages);
            Assert.Equal(UploadStage.Finalizing, stages[^1]); // 마무리는 항상 마지막

            // 사진 단계 시작(0.0)이 타임랩스 단계 시작(0.0)보다 먼저(단계 순서 유지).
            int firstPhoto = stages.IndexOf(UploadStage.Photo);
            int firstTimelapse = stages.IndexOf(UploadStage.Timelapse);
            Assert.True(firstPhoto < firstTimelapse);
        }
        finally { File.Delete(final); File.Delete(timelapse); }
    }

    [Fact]
    public async Task Upload_Without_Progress_Still_Works()
    {
        // 하위호환: progress 미전달(4인자)도 정상 동작.
        var mock = new MockFirebaseClient();
        var svc = new UploadService(mock);
        var final = MakeTempFile(".jpg");
        try
        {
            var session = await svc.UploadResultAsync(final, null, 24, "https://x.web.app");
            Assert.NotNull(session.FinalImageUrl);
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
