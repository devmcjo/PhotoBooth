using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
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

    // ── it13 §5.1: 업로드 선택적 Bearer(로그인=JWT 부착, 게스트=무토큰) + 한도 초과 403 매핑 ──

    [Fact]
    public async Task Guest_Upload_Sends_No_Bearer()
    {
        // 세션 미로그인(토큰 없음) → prepare에 Authorization 헤더 없음(익명), 정상 통과.
        var handler = new FakeHttpMessageHandler();
        var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.OK,
            PrepareJson("final", storagePath, "tok"));
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var client = Make(handler, session: new BackendSession()); // 미로그인
        var local = TempFile(".jpg");
        try
        {
            await client.UploadFileAsync(storagePath, local, "image/jpeg");
            var prep = handler.Requests[0];
            Assert.Null(prep.AuthorizationScheme);                       // Bearer 미부착(게스트)
            Assert.Equal(ApiKey, prep.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API 키는 유지
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task LoggedIn_Upload_Attaches_Bearer()
    {
        // 로그인(토큰 보유) → prepare·commit에 Bearer <jwt> 부착(선택적 신원화).
        var handler = new FakeHttpMessageHandler();
        var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.OK,
            PrepareJson("final", storagePath, "tok"));
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var session = new BackendSession();
        session.SignIn("jwt-abc", new User { Id = "temp1", Role = UserRole.TempUser });
        var client = Make(handler, session: session);
        var local = TempFile(".jpg");
        try
        {
            await client.UploadFileAsync(storagePath, local, "image/jpeg");
            var prep = handler.Requests[0];
            Assert.Equal("Bearer", prep.AuthorizationScheme);
            Assert.Equal("jwt-abc", prep.AuthorizationParameter);
        }
        finally { File.Delete(local); }
    }

    [Theory]
    [InlineData("TEMP_USER_TIME_EXCEEDED", QrGateReason.Time)]
    [InlineData("TEMP_USER_COUNT_EXCEEDED", QrGateReason.Count)]
    public async Task Prepare_Limit_Exceeded_403_Maps_To_QrLimitException(string code, QrGateReason expected)
    {
        // 서버가 prepare에서 한도 초과 403(사유 code) → QrLimitExceededException(사유 보존).
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"" + code + "\",\"message\":\"limit\"}}");

        var session = new BackendSession();
        session.SignIn("jwt", new User { Id = "temp1", Role = UserRole.TempUser });
        var client = Make(handler, session: session);
        var local = TempFile(".jpg");
        try
        {
            var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
            var ex = await Assert.ThrowsAsync<QrLimitExceededException>(
                () => client.UploadFileAsync(storagePath, local, "image/jpeg"));
            Assert.Equal(expected, ex.Reason);
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task Commit_Count_Exceeded_403_Maps_To_QrLimitException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/commit", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"TEMP_USER_COUNT_EXCEEDED\",\"message\":\"limit\"}}");

        var session = new BackendSession();
        session.SignIn("jwt", new User { Id = "temp1", Role = UserRole.TempUser });
        var client = Make(handler, session: session);

        var now = DateTime.UtcNow;
        var rs = new ResultSession { Id = "s1", CreatedAt = now, ExpiresAt = now.AddHours(24), DownloadPageUrl = "https://p/?s=s1" };
        var ex = await Assert.ThrowsAsync<QrLimitExceededException>(() => client.CreateResultSessionAsync(rs));
        Assert.Equal(QrGateReason.Count, ex.Reason);
    }

    [Fact]
    public async Task Non_TempUser_403_Other_Code_Maps_To_Unauthorized()
    {
        // TempUser 사유 code가 아닌 403(예: forbidden)은 기존 계약(UnauthorizedAccessException) 유지 — 드리프트 없음.
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"forbidden\",\"message\":\"nope\"}}");

        var client = Make(handler, session: new BackendSession());
        var local = TempFile(".jpg");
        try
        {
            var storagePath = "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
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
