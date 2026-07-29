using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests.Http;

/// <summary>
/// 로그아웃 시 백엔드 JWT 폐기 검증(합성 루트 배선). 결함: <see cref="SessionContext"/>만 비워지고
/// <see cref="IBackendSession"/>의 토큰은 남아, 로그아웃 뒤 게스트 업로드에 직전 계정의 Bearer가 붙었다.
/// 업로드는 <c>SendJsonOptionalBearerAsync</c>(선택적 Bearer)라 토큰이 있으면 조용히 부착되고,
/// 서버(uploads.ts optionalBearer)는 그 신원으로 소유자를 판정하며 TempUser면 qrUsedCount까지 증가시킨다.
///
/// 개별 클래스가 아니라 <see cref="ServiceRegistration"/>이 조립한 컨테이너를 대상으로 한다 —
/// "아무도 Clear()를 부르지 않는다"는 결함은 배선을 실제로 조립해야만 재현되기 때문이다.
/// </summary>
public class BackendSessionLogoutTests : IClassFixture<UploadFileFixture>
{
    private const string ApiKey = "test-client-key";
    private const string Bucket = "mcphoto-955fb.firebasestorage.app";
    private const string StoragePath =
        "results/20260101_120000_11111111-2222-3333-4444-555555555555/final.jpg";

    private readonly UploadFileFixture _file;

    public BackendSessionLogoutTests(UploadFileFixture file) => _file = file;

    /// <summary>백엔드 설정 스텁(BaseAddress는 TestHttpClientFactory가 주입하므로 값만 유효하면 된다).</summary>
    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new()
        {
            BackendBaseUrl = "https://backend.test/api/",
            BackendApiKey = ApiKey,
            StorageBucket = Bucket,
        };

        public AppSettings Load() => Current;
        public bool Save() => true;
    }

    /// <summary>
    /// 앱과 동일한 백엔드 서비스 그래프를 조립한다. SessionContext 등록은 ServiceRegistration.Register와 동일.
    /// 실서버 호출 금지 — 명명 클라이언트 대신 Fake 핸들러 팩토리를 마지막에 덮어써(마지막 등록 승리) 가로챈다.
    /// </summary>
    private static ServiceProvider BuildAppContainer(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService>(new StubSettingsService());
        services.AddSingleton<SessionContext>();
        ServiceRegistration.RegisterBackendServices(services);
        services.AddSingleton<IHttpClientFactory>(_ => new TestHttpClientFactory(handler));
        return services.BuildServiceProvider();
    }

    private static User NewOperator(string id) =>
        new() { Id = id, Role = UserRole.TempUser, Email = id + "@x.test", HasPin = true };

    private static ResultSession NewResultSession()
    {
        var now = DateTime.UtcNow;
        return new ResultSession
        {
            Id = "20260101_120000_11111111-2222-3333-4444-555555555555",
            FinalImageUrl = "https://x/final",
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            DownloadPageUrl = "https://p/?s=s1",
        };
    }

    private static string PrepareJson(string token) =>
        "{\"uploads\":[{\"kind\":\"final\",\"putUrl\":\"https://signed.example/put-final\"," +
        "\"downloadUrl\":\"https://firebasestorage.googleapis.com/v0/b/" + Bucket + "/o/" +
        Uri.EscapeDataString(StoragePath) + "?alt=media&token=" + token + "\"," +
        "\"requiredHeaders\":{\"Content-Type\":\"image/jpeg\"}}]," +
        "\"bucket\":\"" + Bucket + "\"}";

    private const string CommitJson =
        "{\"id\":\"s1\",\"finalImageUrl\":\"https://x/final\",\"timelapseUrl\":null," +
        "\"createdAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-01-02T00:00:00Z\"," +
        "\"downloadPageUrl\":\"https://p/?s=s1\"}";

    private static FakeHttpMessageHandler PrepareAndPutHandler()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/prepare", HttpStatusCode.OK, PrepareJson("tok"));
        handler.When(HttpMethod.Put, "put-final", _ => FakeHttpMessageHandler.NoContent(HttpStatusCode.OK));
        return handler;
    }

    // ── 결함 재현: 로그아웃 후 업로드가 익명이어야 한다 ──

    [Fact]
    public async Task Logout_Then_Upload_Prepare_Sends_No_Bearer()
    {
        var handler = PrepareAndPutHandler();
        using var sp = BuildAppContainer(handler);

        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var client = sp.GetRequiredService<IFirebaseClient>();

        // 1) 운영자 로그인: HttpAccountService가 SignIn한 뒤 VM이 SessionContext.Login하는 실제 순서.
        var op = NewOperator("temp-operator");
        backend.SignIn("jwt-operator", op);
        context.Login(op);

        // 2) 로그아웃(AppShellViewModel.Logout이 호출하는 유일한 지점).
        context.Logout();

        // 3) 게스트 촬영 업로드. 직전 계정 JWT가 붙으면 서버가 그 계정 소유로 처리하고,
        //    TempUser면 QR 한도(qrUsedCount)까지 부당 소모된다(it13 과금 방어 훼손).
        await client.UploadFileAsync(StoragePath, _file.JpgPath, "image/jpeg");

        var prepare = handler.Requests[0];
        Assert.Contains("uploads/prepare", prepare.Uri!.ToString());
        Assert.Null(prepare.AuthorizationScheme);     // 익명이어야 한다
        Assert.Null(prepare.AuthorizationParameter);
        Assert.Equal(ApiKey, prepare.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API 키 게이트는 유지
        Assert.Null(backend.Token);                   // 홀더 자체가 비워져야 한다
    }

    [Fact]
    public async Task Logout_Then_Upload_Commit_Sends_No_Bearer()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenJson(HttpMethod.Post, "uploads/commit", HttpStatusCode.Created, CommitJson);
        using var sp = BuildAppContainer(handler);

        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var client = sp.GetRequiredService<IFirebaseClient>();

        var op = NewOperator("temp-operator");
        backend.SignIn("jwt-operator", op);
        context.Login(op);
        context.Logout();

        // commit은 서버가 qrUsedCount를 실제로 증가시키는 지점(uploads.ts 트랜잭션).
        await client.CreateResultSessionAsync(NewResultSession());

        var commit = handler.Requests[0];
        Assert.Contains("uploads/commit", commit.Uri!.ToString());
        Assert.Null(commit.AuthorizationScheme);
        Assert.Null(backend.Token);
    }

    [Fact]
    public async Task Idle_Reset_ClearUser_Also_Clears_Token()
    {
        // CurrentUser를 비우는 지점은 Logout()·Reset(clearUser:true) 둘이고 후자는 전자에 위임한다.
        // CurrentUserChanged 구독으로 배선했으므로 어느 쪽을 타든 토큰이 함께 폐기된다.
        // (현재 clearUser:true의 프로덕션 호출부는 0 — 유휴 타임아웃은 it8 A1로 로그아웃 금지.
        //  이 테스트는 그 경로가 생겨도 토큰이 남지 않음을 미리 고정하는 방어다.)
        var handler = PrepareAndPutHandler();
        using var sp = BuildAppContainer(handler);

        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var client = sp.GetRequiredService<IFirebaseClient>();

        var op = NewOperator("temp-operator");
        backend.SignIn("jwt-operator", op);
        context.Login(op);

        context.Reset(clearUser: true);

        await client.UploadFileAsync(StoragePath, _file.JpgPath, "image/jpeg");
        Assert.Null(handler.Requests[0].AuthorizationScheme);
        Assert.Null(backend.Token);
    }

    // ── 무회귀: 과교정("항상 익명")으로 로그인 신원화가 사라지면 안 된다 ──

    [Fact]
    public async Task Guest_Upload_Without_Login_Stays_Anonymous()
    {
        var handler = PrepareAndPutHandler();
        using var sp = BuildAppContainer(handler);
        var client = sp.GetRequiredService<IFirebaseClient>();

        await client.UploadFileAsync(StoragePath, _file.JpgPath, "image/jpeg");

        var prepare = handler.Requests[0];
        Assert.Null(prepare.AuthorizationScheme);
        Assert.Equal(ApiKey, prepare.HeaderValue(HttpBackendClient.ApiKeyHeader));
    }

    [Fact]
    public async Task LoggedIn_Upload_Attaches_Bearer()
    {
        // 로그인 상태 업로드에는 반드시 JWT가 붙어야 한다 — 서버의 TempUser 한도 적용 근거가 신원이다.
        var handler = PrepareAndPutHandler();
        using var sp = BuildAppContainer(handler);

        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var client = sp.GetRequiredService<IFirebaseClient>();

        var op = NewOperator("temp-operator");
        backend.SignIn("jwt-operator", op);
        context.Login(op);   // 로그인 통지에서 토큰을 지우면 안 된다(SignIn이 먼저 일어난다).

        await client.UploadFileAsync(StoragePath, _file.JpgPath, "image/jpeg");

        var prepare = handler.Requests[0];
        Assert.Equal("Bearer", prepare.AuthorizationScheme);
        Assert.Equal("jwt-operator", prepare.AuthorizationParameter);
    }

    [Fact]
    public async Task Relogin_After_Logout_Attaches_New_Token()
    {
        var handler = PrepareAndPutHandler();
        using var sp = BuildAppContainer(handler);

        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var client = sp.GetRequiredService<IFirebaseClient>();

        var first = NewOperator("temp-first");
        backend.SignIn("jwt-first", first);
        context.Login(first);
        context.Logout();

        var second = NewOperator("temp-second");
        backend.SignIn("jwt-second", second);
        context.Login(second);

        await client.UploadFileAsync(StoragePath, _file.JpgPath, "image/jpeg");

        var prepare = handler.Requests[0];
        Assert.Equal("Bearer", prepare.AuthorizationScheme);
        Assert.Equal("jwt-second", prepare.AuthorizationParameter);
    }

    // ── 이벤트 누수: 구독마다 해제 경로가 있어야 한다 ──

    [Fact]
    public void Dispose_Unsubscribes_From_SessionContext()
    {
        var context = new SessionContext();
        var holder = new CountingBackendSession();
        var sync = new BackendSessionSynchronizer(context, holder);
        var op = NewOperator("temp-operator");

        context.Login(op);
        context.Logout();
        Assert.Equal(1, holder.ClearCount);

        sync.Dispose();
        context.Login(op);
        context.Logout();
        Assert.Equal(1, holder.ClearCount);   // 해제 후에는 반응하지 않는다(구독 잔존 없음)
    }

    [Fact]
    public void Container_Dispose_Releases_Subscription()
    {
        // 동기화기는 컨테이너가 소유(팩토리 등록)하므로 호스트 종료 시 Dispose되어 구독이 끊긴다.
        using var sp = BuildAppContainer(new FakeHttpMessageHandler());
        var context = sp.GetRequiredService<SessionContext>();
        var backend = sp.GetRequiredService<IBackendSession>();
        var op = NewOperator("temp-operator");

        backend.SignIn("jwt-first", op);
        context.Login(op);
        context.Logout();
        Assert.Null(backend.Token);

        sp.Dispose();

        backend.SignIn("jwt-after-dispose", op);
        context.Login(op);
        context.Logout();
        Assert.Equal("jwt-after-dispose", backend.Token);   // 구독이 끊겼음(잔존 핸들러 없음)
    }

    /// <summary>Clear 호출 횟수만 세는 최소 홀더(구독 해제 검증용).</summary>
    private sealed class CountingBackendSession : IBackendSession
    {
        public int ClearCount { get; private set; }
        public string? Token { get; private set; }
        public User? CurrentUser { get; private set; }

        public void SignIn(string token, User user)
        {
            Token = token;
            CurrentUser = user;
        }

        public void Clear()
        {
            ClearCount++;
            Token = null;
            CurrentUser = null;
        }
    }
}

/// <summary>
/// 업로드 PUT용 더미 이미지. 여러 테스트가 읽기 전용으로만 쓰므로 클래스 1회 생성한다
/// (테스트 메서드마다 %TEMP% 쓰기→즉시 읽기를 반복하면 공유 위반으로 간헐 실패한다 — TestImageFile 주석 참고).
/// </summary>
public sealed class UploadFileFixture : IDisposable
{
    public string JpgPath { get; } = TestImageFile.CreateInTemp(8, 8, ".jpg");

    public void Dispose()
    {
        try { if (File.Exists(JpgPath)) File.Delete(JpgPath); } catch { /* 무시 */ }
    }
}
