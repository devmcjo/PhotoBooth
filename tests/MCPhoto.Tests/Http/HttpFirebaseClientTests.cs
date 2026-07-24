using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using MCPhoto.Tests.Http;

namespace MCPhoto.Tests.Http;

/// <summary>P3: HttpFirebaseClient(업로드 prepare→PUT→commit) + UploadService 무변경 재사용 검증.</summary>
public class HttpFirebaseClientTests
{
    private const string ApiKey = "test-client-key";
    private const string Bucket = "mcphoto-955fb.firebasestorage.app";

    private static HttpFirebaseClient Make(FakeHttpMessageHandler handler, BackendSession? session = null, bool configured = true)
        => new(new TestHttpClientFactory(handler), session ?? new BackendSession(), ApiKey, Bucket, configured);

    private static string TempFile(string ext, int size = 16)
    {
        var p = Path.Combine(Path.GetTempPath(), $"mcphoto_http_{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    /// <summary>prepare 응답 JSON(kind별 서명 URL). downloadUrl 토큰이 UploadFileAsync 반환값이 된다.</summary>
    private static string PrepareJson(string kind, string storagePath, string token)
        => "{\"uploads\":[{\"kind\":\"" + kind + "\",\"putUrl\":\"https://signed.example/put-" + kind + "\"," +
           "\"downloadUrl\":\"https://firebasestorage.googleapis.com/v0/b/" + Bucket + "/o/" +
           Uri.EscapeDataString(storagePath) + "?alt=media&token=" + token + "\"," +
           "\"requiredHeaders\":{\"Content-Type\":\"image/jpeg\",\"x-goog-meta-firebaseStorageDownloadTokens\":\"" + token + "\"}}]," +
           "\"bucket\":\"" + Bucket + "\"}";

    [Fact]
    public async Task UploadFile_Prepares_Puts_And_Returns_Token()
    {
        var handler = new FakeHttpMessageHandler();
        var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.OK,
            PrepareJson("final", storagePath, "tok-final"));
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var client = Make(handler);
        var local = TempFile(".jpg");
        try
        {
            var token = await client.UploadFileAsync(storagePath, local, "image/jpeg");

            Assert.Equal("tok-final", token); // downloadUrl에서 토큰 추출
            Assert.Equal(Bucket, client.Bucket); // prepare 응답 버킷 반영

            // 순서: prepare → PUT
            Assert.Equal(2, handler.Requests.Count);
            var prep = handler.Requests[0];
            Assert.Contains("uploads/prepare", prep.Uri!.ToString());
            Assert.Contains("\"kind\":\"final\"", prep.Body!);
            Assert.Contains("20260101_120000", prep.Body!); // sessionId 전달
            Assert.Equal(ApiKey, prep.HeaderValue(HttpBackendClient.ApiKeyHeader)); // 게스트 = API 키

            var put = handler.Requests[1];
            Assert.Equal(HttpMethod.Put, put.Method);
            Assert.Equal("tok-final", put.HeaderValue("x-goog-meta-firebaseStorageDownloadTokens"));

            // 반환 토큰으로 UploadContract가 재조립한 URL == 서버 downloadUrl.
            var rebuilt = UploadContract.TokenDownloadUrl(client.Bucket, storagePath, token);
            Assert.Contains("token=tok-final", rebuilt);
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task UploadFile_Reports_Progress()
    {
        var handler = new FakeHttpMessageHandler();
        var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.OK,
            PrepareJson("final", storagePath, "tok"));
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var client = Make(handler);
        var local = TempFile(".jpg", size: 4096);
        var reports = new List<double>();
        var progress = new Progress<double>(reports.Add);
        try
        {
            await client.UploadFileAsync(storagePath, local, "image/jpeg", progress);
            // 비동기 Progress는 SynchronizationContext에 게시 → 완료 보장 위해 잠깐 양보.
            await Task.Yield();
            Assert.Contains(reports, r => r >= 0.99); // 최종 100% 근처 보고
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task CreateResultSession_Commits_With_Derived_RetentionHours()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/commit", HttpStatusCode.Created,
            "{\"id\":\"s1\",\"finalImageUrl\":\"https://x/final\",\"timelapseUrl\":null," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-01-02T00:00:00Z\",\"downloadPageUrl\":\"https://p/?s=s1\"}");

        var client = Make(handler);
        var now = DateTime.UtcNow;
        var session = new ResultSession
        {
            Id = "s1",
            FinalImageUrl = "https://x/final",
            TimelapseUrl = null,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            DownloadPageUrl = "https://p/?s=s1",
        };

        await client.CreateResultSessionAsync(session);

        var req = handler.Requests[0];
        Assert.Contains("uploads/commit", req.Uri!.ToString());
        Assert.Contains("\"retentionHours\":24", req.Body!); // (ExpiresAt-CreatedAt) 역산
        Assert.Contains("\"sessionId\":\"s1\"", req.Body!);
        Assert.Contains("\"timelapseUrl\":null", req.Body!); // 명시적 null 유지(it7 F2 계약)
    }

    [Fact]
    public async Task UploadService_Reuses_HttpFirebaseClient_Full_Flow()
    {
        // UploadService 무변경 목표: prepare(final)→PUT→prepare(timelapse)→PUT→commit 순.
        var handler = new FakeHttpMessageHandler();
        var final = TempFile(".jpg");
        var timelapse = TempFile(".mp4");

        handler.When(HttpMethod.Post, "uploads/prepare", req =>
        {
            var kind = req.Body!.Contains("\"kind\":\"timelapse\"") ? "timelapse" : "final";
            var ext = kind == "timelapse" ? "mp4" : "jpg";
            var name = kind == "timelapse" ? "timelapse.mp4" : "final.jpg";
            // sessionId를 본문에서 추출(20260101_120000_...) — 경로 조립엔 임의 sessionId면 되지만 계약 형식 유지.
            var sid = ExtractSessionId(req.Body!);
            var path = $"results/{sid}/{name}";
            return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PrepareJson(kind, path, "tok-" + kind));
        });
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));
        handler.When(HttpMethod.Put, "put-timelapse", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));
        handler.When(HttpMethod.Post, "uploads/commit", _ =>
            FakeHttpMessageHandler.JsonResponse(HttpStatusCode.Created,
                "{\"id\":\"s\",\"finalImageUrl\":null,\"timelapseUrl\":null,\"createdAt\":\"2026-01-01T00:00:00Z\"," +
                "\"expiresAt\":\"2026-01-02T00:00:00Z\",\"downloadPageUrl\":\"https://p/?s=s\"}"));

        var client = Make(handler);
        var svc = new UploadService(client);
        try
        {
            var session = await svc.UploadResultAsync(final, timelapse, 24, "https://mcphoto.web.app");

            Assert.NotNull(session.FinalImageUrl);
            Assert.NotNull(session.TimelapseUrl);
            Assert.EndsWith("/?s=" + session.Id, session.DownloadPageUrl);

            // 요청 순서: prepare, PUT, prepare, PUT, commit (총 5).
            Assert.Equal(5, handler.Requests.Count);
            Assert.Contains("uploads/prepare", handler.Requests[0].Uri!.ToString());
            Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
            Assert.Contains("uploads/prepare", handler.Requests[2].Uri!.ToString());
            Assert.Equal(HttpMethod.Put, handler.Requests[3].Method);
            Assert.Contains("uploads/commit", handler.Requests[4].Uri!.ToString());

            // final URL이 세션 경로를 가리켜야 서버 commit이 수락(경로 정합).
            Assert.Contains($"results/{session.Id}", Uri.UnescapeDataString(session.FinalImageUrl!));
        }
        finally { File.Delete(final); File.Delete(timelapse); }
    }

    [Fact]
    public async Task Upload_Network_Failure_Throws_InvalidOperation()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Fallback = _ => throw new HttpRequestException("연결 실패");
        var client = Make(handler);
        var local = TempFile(".jpg");
        try
        {
            var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.UploadFileAsync(storagePath, local, "image/jpeg"));
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public void IsInitialized_False_When_Not_Configured()
    {
        var handler = new FakeHttpMessageHandler();
        var client = Make(handler, configured: false);
        Assert.False(client.IsInitialized); // base URL 미설정 = 오프라인 폴백
    }

    [Fact]
    public async Task QueryExpired_Is_NotSupported_Over_Http()
    {
        var handler = new FakeHttpMessageHandler();
        var client = Make(handler);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.QueryExpiredSessionsAsync(DateTime.UtcNow));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.DeleteResultSessionAsync("x"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.DeleteStoragePrefixAsync("results/x/"));
    }

    private static string ExtractSessionId(string body)
    {
        // "sessionId":"20260101_120000_uuid"
        const string marker = "\"sessionId\":\"";
        var i = body.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return "20260101_120000_11111111-2222-3333-4444-555555555555";
        var start = i + marker.Length;
        var end = body.IndexOf('"', start);
        return body.Substring(start, end - start);
    }
}
