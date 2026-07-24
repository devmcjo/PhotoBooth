using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.Core.Models;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using MCPhoto.Tests.Http;

namespace MCPhoto.Tests.Http;

/// <summary>P3: HttpFrameRepository 단위 테스트(FakeHttpMessageHandler).</summary>
public class HttpFrameRepositoryTests
{
    private const string ApiKey = "test-client-key";

    private static (HttpFrameRepository repo, FakeHttpMessageHandler handler, BackendSession session) Make()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new BackendSession();
        var svc = new HttpFrameRepository(new TestHttpClientFactory(handler), session, ApiKey);
        return (svc, handler, session);
    }

    [Fact]
    public async Task GetDefault_Uses_ApiKey_No_Bearer_And_Parses()
    {
        var (repo, handler, _) = Make();
        handler.WhenJson(HttpMethod.Get, "frames/default", HttpStatusCode.OK,
            "[{\"id\":\"f1\",\"userId\":null,\"isDefault\":true,\"name\":\"기본\",\"imageUrl\":\"https://x/o?alt=media&token=t1\"," +
            "\"imageSize\":{\"width\":1200,\"height\":1800},\"slots\":[{\"index\":0,\"x\":10,\"y\":20,\"width\":300,\"height\":400}]," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\"}]");

        var frames = await repo.GetDefaultFramesAsync();

        Assert.Single(frames);
        var f = frames[0];
        Assert.Equal("f1", f.Id);
        Assert.True(f.IsDefault);
        Assert.Equal(1200, f.ImageSize.Width);
        Assert.Single(f.Slots);
        Assert.Equal(300, f.Slots[0].Width);

        var req = handler.Requests[0];
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader));
        Assert.Null(req.AuthorizationScheme); // 공개 조회 — Bearer 없음
    }

    [Fact]
    public async Task GetUser_Uses_Bearer_And_Query()
    {
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "u1", Role = UserRole.User });
        handler.WhenJson(HttpMethod.Get, "frames?userId=u1", HttpStatusCode.OK, "[]");

        var frames = await repo.GetUserFramesAsync("u1");

        Assert.Empty(frames);
        var req = handler.Requests[0];
        Assert.Contains("frames?userId=u1", req.Uri!.ToString());
        Assert.Equal("Bearer", req.AuthorizationScheme);
    }

    [Fact]
    public async Task Save_Posts_Meta_Then_Puts_Image_With_Required_Headers()
    {
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });

        handler.WhenJson(HttpMethod.Post, "frames", HttpStatusCode.Created,
            "{\"frame\":{\"id\":\"nf\",\"userId\":null,\"isDefault\":true,\"name\":\"새프레임\"," +
            "\"imageUrl\":\"https://fb/o/frames%2Fdefault%2Fnf.png?alt=media&token=dtok\"," +
            "\"imageSize\":{\"width\":100,\"height\":200},\"slots\":[],\"createdAt\":\"2026-01-01T00:00:00Z\"}," +
            "\"upload\":{\"putUrl\":\"https://signed.example/put\",\"downloadUrl\":\"https://fb/o/frames%2Fdefault%2Fnf.png?alt=media&token=dtok\"," +
            "\"requiredHeaders\":{\"Content-Type\":\"image/png\",\"x-goog-meta-firebaseStorageDownloadTokens\":\"dtok\"}}}");
        handler.When(HttpMethod.Put, "signed.example/put", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var frame = new FrameTemplate
        {
            Name = "새프레임",
            ImageSize = new MCPhoto.Core.Models.ImageSize { Width = 100, Height = 200 },
        };
        frame.Slots.Add(new Slot { Index = 0, X = 1, Y = 2, Width = 3, Height = 4 });

        var saved = await repo.SaveAsync(frame, new byte[] { 1, 2, 3, 4 });

        Assert.Equal("nf", saved.Id);
        Assert.Contains("token=dtok", saved.ImageUrl);

        // 순서: POST /frames → PUT 서명URL
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("Bearer", handler.Requests[0].AuthorizationScheme);
        // 서버가 공용 기본 프레임만 만들므로 isDefault=true로 전송.
        Assert.Contains("\"isDefault\":true", handler.Requests[0].Body!);

        var put = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://signed.example/put", put.Uri!.ToString());
        // Content-Type은 콘텐츠 헤더, 다운로드 토큰 메타는 요청 헤더로 전송(서명 정합).
        Assert.Equal("image/png", put.HeaderValue("Content-Type"));
        Assert.Equal("dtok", put.HeaderValue("x-goog-meta-firebaseStorageDownloadTokens"));
    }

    [Fact]
    public async Task Save_MaxFrames_409_Maps_To_InvalidOperation()
    {
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "frames", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"프레임은 계정당 최대 10개까지 저장할 수 있습니다.\"}}");

        var frame = new FrameTemplate { Name = "x", ImageSize = new MCPhoto.Core.Models.ImageSize { Width = 1, Height = 1 } };
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => repo.SaveAsync(frame, new byte[] { 1 }));
        Assert.Contains("최대 10개", ex.Message);
    }

    // ── item2 §5: PUT /frames/{id} 업데이트 계약 정합(fake-handler) ──

    [Fact]
    public async Task Update_ReplaceImage_Puts_Meta_Then_Image_With_Headers()
    {
        // replaceImage=true: PUT /frames/{id} → 응답 upload 서명 URL로 이미지 바이트 별도 PUT.
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });

        handler.WhenJson(HttpMethod.Put, "frames/GUID-abc", HttpStatusCode.OK,
            "{\"frame\":{\"id\":\"GUID-abc\",\"userId\":null,\"isDefault\":true,\"name\":\"수정됨\"," +
            "\"imageUrl\":\"https://fb/o/frames%2Fdefault%2FGUID-abc.png?alt=media&token=newtok\"," +
            "\"imageSize\":{\"width\":800,\"height\":1000},\"slots\":[{\"index\":0,\"x\":5,\"y\":6,\"width\":7,\"height\":8}]," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\"}," +
            "\"upload\":{\"putUrl\":\"https://signed.example/put2\",\"downloadUrl\":\"https://fb/o/frames%2Fdefault%2FGUID-abc.png?alt=media&token=newtok\"," +
            "\"requiredHeaders\":{\"Content-Type\":\"image/png\",\"x-goog-meta-firebaseStorageDownloadTokens\":\"newtok\"}}}");
        handler.When(HttpMethod.Put, "signed.example/put2", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));

        var frame = new FrameTemplate
        {
            Id = "GUID-abc", UserId = null, IsDefault = true, Name = "수정됨",
            ImageSize = new MCPhoto.Core.Models.ImageSize { Width = 800, Height = 1000 },
        };
        frame.Slots.Add(new Slot { Index = 0, X = 5, Y = 6, Width = 7, Height = 8 });

        var updated = await repo.UpdateAsync(frame, new byte[] { 9, 9, 9 }, replaceImage: true);

        Assert.Equal("GUID-abc", updated.Id);              // 같은 문서 id 보존
        Assert.Contains("token=newtok", updated.ImageUrl); // 갱신된 다운로드 URL

        // 순서: PUT /frames/{id} → PUT 서명URL(이미지)
        Assert.Equal(2, handler.Requests.Count);
        var meta = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, meta.Method);
        Assert.Contains("frames/GUID-abc", meta.Uri!.ToString());
        Assert.Equal("Bearer", meta.AuthorizationScheme);
        Assert.Contains("\"replaceImage\":true", meta.Body!);
        Assert.Contains("\"slots\":[{\"index\":0", meta.Body!); // 슬롯 메타 전송
        Assert.DoesNotContain("\"isDefault\"", meta.Body!);  // 서버가 보존 — 요청 본문에 없음
        Assert.DoesNotContain("\"userId\"", meta.Body!);

        var put = handler.Requests[1];
        Assert.Equal("https://signed.example/put2", put.Uri!.ToString());
        Assert.Equal("image/png", put.HeaderValue("Content-Type"));
        Assert.Equal("newtok", put.HeaderValue("x-goog-meta-firebaseStorageDownloadTokens"));
    }

    [Fact]
    public async Task Update_MetaOnly_Skips_Image_Put_When_No_Upload()
    {
        // replaceImage=false: 메타만 PUT, 응답 upload 없음 → 이미지 PUT 안 함(요청 1건).
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });

        handler.WhenJson(HttpMethod.Put, "frames/GUID-abc", HttpStatusCode.OK,
            "{\"frame\":{\"id\":\"GUID-abc\",\"userId\":null,\"isDefault\":true,\"name\":\"슬롯만\"," +
            "\"imageUrl\":\"https://fb/o/frames%2Fdefault%2FGUID-abc.png?alt=media&token=orig\"," +
            "\"imageSize\":{\"width\":100,\"height\":200},\"slots\":[],\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var frame = new FrameTemplate
        {
            Id = "GUID-abc", UserId = null, IsDefault = true, Name = "슬롯만",
            ImageSize = new MCPhoto.Core.Models.ImageSize { Width = 100, Height = 200 },
        };

        var updated = await repo.UpdateAsync(frame, new byte[] { 1, 2 }, replaceImage: false);

        Assert.Equal("GUID-abc", updated.Id);
        Assert.Contains("token=orig", updated.ImageUrl); // 기존 이미지 URL 보존
        Assert.Single(handler.Requests);                  // PUT 메타만(이미지 PUT 없음)
        Assert.Contains("\"replaceImage\":false", handler.Requests[0].Body!);
    }

    [Fact]
    public async Task Update_Missing_404_Maps_To_InvalidOperation()
    {
        // 서버가 대상 문서 없음(404) → InvalidOperationException으로 매핑(화면 유지 경로).
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Put, "frames/gone", HttpStatusCode.NotFound,
            "{\"error\":{\"code\":\"not_found\",\"message\":\"프레임을 찾을 수 없습니다.\"}}");

        var frame = new FrameTemplate { Id = "gone", Name = "x", ImageSize = new MCPhoto.Core.Models.ImageSize { Width = 1, Height = 1 } };
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => repo.UpdateAsync(frame, new byte[] { 1 }, replaceImage: false));
        Assert.Contains("찾을 수 없습니다", ex.Message);
    }

    [Fact]
    public async Task Delete_Returns_Deleted_Flag()
    {
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Delete, "frames/f1", HttpStatusCode.OK, "{\"deleted\":true}");

        var deleted = await repo.DeleteAsync("f1");

        Assert.True(deleted);
        Assert.Equal("Bearer", handler.Requests[0].AuthorizationScheme);
    }

    [Fact]
    public async Task Delete_Missing_Returns_False()
    {
        var (repo, handler, session) = Make();
        session.SignIn("jwt-1", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Delete, "frames/gone", HttpStatusCode.OK, "{\"deleted\":false}");

        Assert.False(await repo.DeleteAsync("gone"));
    }

    [Fact]
    public async Task DeleteAllByUser_Is_NoOp_Over_Http()
    {
        var (repo, handler, _) = Make();
        await repo.DeleteAllByUserAsync("u1");
        Assert.Empty(handler.Requests); // 서버가 계정 삭제 cascade로 처리
    }
}
