using System.IO;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;

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
            // 자동삭제 정합: 업로드 폴더 = results/{session.Id}/ (PurgeExpired가 이 prefix로 삭제)
            Assert.All(mock.UploadedPaths, p => Assert.StartsWith($"results/{session.Id}/", p));

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

    /// <summary>
    /// 진행 보고 수집기(테스트용). <b>스레드 안전이 필수다.</b>
    /// <see cref="UploadService"/>의 stage 경계 보고는 동기 호출이지만, 파일 단위 보고는
    /// <c>MakeStageProgress</c>가 만드는 <see cref="Progress{T}"/>가 캡처된 SynchronizationContext
    /// (테스트 환경엔 없음 → <b>스레드풀</b>)로 <b>비동기 게시</b>한다.
    /// 따라서 서로 다른 스레드가 동시에 Report를 호출할 수 있어, 락 없는 <see cref="List{T}"/> 변경은
    /// 자료구조를 손상시킨다(항목 유실·IndexOutOfRange).
    /// </summary>
    private sealed class CollectingProgress : IProgress<UploadProgress>
    {
        private readonly List<UploadProgress> _reports = new();

        /// <summary>수집분 스냅샷(복사본). 늦게 도착하는 비동기 보고와 열거가 겹쳐도 안전하다.</summary>
        public List<UploadProgress> Snapshot()
        {
            lock (_reports) return new List<UploadProgress>(_reports);
        }

        public void Report(UploadProgress value)
        {
            lock (_reports) _reports.Add(value);
        }
    }

    /// <summary>
    /// 진행 보고에서 <b>제품이 실제로 보장하는</b> 성질만 검증한다.
    ///
    /// ⚠️ "Finalizing이 항상 마지막"은 보장되지 않는다 — <c>MakeStageProgress</c>의
    /// <see cref="Progress{T}"/>가 파일 단위 보고를 스레드풀로 비동기 게시하므로, 그 보고가 늦게 도착해
    /// 동기 호출인 Finalizing 뒤에 끼어들 수 있다. 실제 앱에서 Progress&lt;T&gt;가 UI SynchronizationContext로
    /// 마샬링하는 것은 <b>의도된 올바른 동작</b>이므로 제품 코드를 바꾸지 않고 단언을 계약에 맞춘다.
    /// (전체 스위트 실행 시 스레드풀 경합으로 드러난 기존 잠복 결함 — it15에서 발견·테스트 수정으로 해소.)
    ///
    /// 결정적으로 보장되는 것은 <b>동기 보고끼리의 상대 순서</b>다(같은 스레드에서 프로그램 순서대로 실행 —
    /// mock이 완료된 Task를 돌려줘 await가 동기 계속되므로 스레드 전환도 없다).
    /// 그래서 순서 단언은 동기 보고만 골라서 한다.
    /// </summary>
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

            var reports = progress.Snapshot();
            var stages = reports.Select(r => r.Stage).ToList();

            // 세 단계가 모두 보고된다. Finalizing은 "존재"만 단언한다(위치는 위 주석대로 비결정적).
            Assert.Contains(UploadStage.Photo, stages);
            Assert.Contains(UploadStage.Timelapse, stages);
            Assert.Contains(UploadStage.Finalizing, stages);

            // 단계 시작 마커(Fraction=0.0)는 전부 동기 보고다 — mock의 파일 보고는 0.5/1.0만 쓰므로 섞이지 않는다.
            // 이 부분열의 순서는 결정적이다: 사진 단계가 타임랩스 단계보다 먼저 시작한다.
            var stageStarts = reports.Where(r => r.Fraction == 0.0).Select(r => r.Stage).ToList();
            Assert.Equal(new[] { UploadStage.Photo, UploadStage.Timelapse }, stageStarts);

            // Finalizing도 동기 보고라 타임랩스 단계 시작보다는 반드시 뒤에 온다(동기끼리는 순서 보존).
            int timelapseStart = reports.FindIndex(r => r.Stage == UploadStage.Timelapse && r.Fraction == 0.0);
            int finalizing = reports.FindIndex(r => r.Stage == UploadStage.Finalizing);
            Assert.True(finalizing > timelapseStart,
                $"동기 보고 순서가 깨졌다: timelapseStart={timelapseStart}, finalizing={finalizing}");
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
