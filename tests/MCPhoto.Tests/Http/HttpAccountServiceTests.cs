using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.Core.Models;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using MCPhoto.Tests.Http;

namespace MCPhoto.Tests.Http;

/// <summary>P3: HttpAccountService 단위 테스트(FakeHttpMessageHandler, 실서버 호출 없음).</summary>
public class HttpAccountServiceTests
{
    private const string ApiKey = "test-client-key";

    private static (HttpAccountService svc, FakeHttpMessageHandler handler, BackendSession session) Make()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new BackendSession();
        var factory = new TestHttpClientFactory(handler);
        var svc = new HttpAccountService(factory, session, ApiKey);
        return (svc, handler, session);
    }

    [Fact]
    public async Task Login_Success_Stores_Token_And_Sends_ApiKey()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"jwt-abc\",\"expiresIn\":3600,\"user\":{\"id\":\"devmcjo\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var user = await svc.LoginAsync("devmcjo", "1111");

        Assert.NotNull(user);
        Assert.Equal("devmcjo", user!.Id);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(string.Empty, user.Password); // 비번은 응답에 없고 채우지 않음
        Assert.Equal("jwt-abc", session.Token); // 토큰 보관
        // 로그인은 API 키 헤더로(공개 엔드포인트).
        Assert.Equal(ApiKey, handler.Requests[0].HeaderValue(HttpBackendClient.ApiKeyHeader));
        Assert.Null(handler.Requests[0].AuthorizationScheme); // Bearer 아님
    }

    [Fact]
    public async Task Login_Failure_401_Returns_Null()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"아이디 또는 비밀번호가 올바르지 않습니다.\"}}");

        var user = await svc.LoginAsync("devmcjo", "wrong");

        Assert.Null(user); // 현행 계약: 실패 = null(예외 아님)
        Assert.Null(session.Token);
    }

    [Fact]
    public async Task Authenticated_Call_Reuses_Stored_Token_As_Bearer()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"jwt-xyz\",\"expiresIn\":3600,\"user\":{\"id\":\"boss\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");
        handler.WhenJson(HttpMethod.Get, "accounts", HttpStatusCode.OK,
            "[{\"id\":\"boss\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}]");

        await svc.LoginAsync("boss", "pw");
        var all = await svc.GetAllAsync();

        Assert.Single(all);
        var getReq = handler.Requests[1];
        Assert.Equal("Bearer", getReq.AuthorizationScheme);
        Assert.Equal("jwt-xyz", getReq.AuthorizationParameter); // 저장된 토큰 재사용
    }

    [Fact]
    public async Task GetAll_Without_Login_Throws_Unauthorized()
    {
        var (svc, _, _) = Make();
        // 토큰 없음 → Bearer 요청 조립 단계에서 UnauthorizedAccessException.
        await Assert.ThrowsAsync<System.UnauthorizedAccessException>(() => svc.GetAllAsync());
    }

    [Fact]
    public async Task Create_Gate_Violation_Throws_Before_Server_Call()
    {
        var (svc, handler, _) = Make();
        // manager가 manager 생성 시도 → 클라 게이트에서 즉시 거부(서버 왕복 없음).
        await Assert.ThrowsAsync<System.UnauthorizedAccessException>(
            () => svc.CreateAsync("m2", "pw", UserRole.Manager, email: null, actingRole: UserRole.Manager));
        Assert.Empty(handler.Requests); // 서버 호출 안 함
    }

    [Fact]
    public async Task Create_Conflict_409_Maps_To_InvalidOperation()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"이미 존재하는 아이디입니다: dup\"}}");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.CreateAsync("dup", "pw", UserRole.User, email: null, actingRole: UserRole.Admin));
        Assert.Contains("이미 존재", ex.Message);
    }

    [Fact]
    public async Task Create_Forbidden_403_Maps_To_Unauthorized()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"forbidden\",\"message\":\"권한 없음\"}}");

        await Assert.ThrowsAsync<System.UnauthorizedAccessException>(
            () => svc.CreateAsync("x", "pw", UserRole.User, email: null, actingRole: UserRole.Admin));
    }

    [Fact]
    public async Task Create_Sends_Role_As_Firestore_Value_And_Body()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Created,
            "{\"id\":\"newuser\",\"role\":\"manager\",\"createdAt\":\"2026-01-01T00:00:00Z\"}");

        var user = await svc.CreateAsync("newuser", "pw", UserRole.Manager, email: null, actingRole: UserRole.Admin);

        Assert.Equal(UserRole.Manager, user.Role);
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"role\":\"manager\"", body);
        Assert.Contains("\"id\":\"newuser\"", body);
        Assert.Contains("\"password\":\"pw\"", body); // 비번은 TLS로 평문 전송(클라 해시 안 함)
    }

    [Fact]
    public async Task ChangePassword_Uses_Patch_And_Path()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.When(HttpMethod.Patch, "accounts/boss/password", _ => FakeHttpMessageHandler.NoContent());

        await svc.ChangePasswordAsync("boss", "newpw");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.Contains("accounts/boss/password", req.Uri!.ToString());
        Assert.Contains("\"newPassword\":\"newpw\"", req.Body!);
    }

    [Fact]
    public async Task Delete_Uses_Delete_And_Path()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.When(HttpMethod.Delete, "accounts/victim", _ => FakeHttpMessageHandler.NoContent());

        await svc.DeleteAsync("victim");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.Contains("accounts/victim", req.Uri!.ToString());
    }

    [Fact]
    public async Task SetRole_Uses_Patch_Role_Path_And_Body()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.When(HttpMethod.Patch, "accounts/u1/role", _ => FakeHttpMessageHandler.NoContent());

        await svc.SetRoleAsync("u1", UserRole.Manager);

        var req = handler.Requests[0];
        Assert.Contains("accounts/u1/role", req.Uri!.ToString());
        Assert.Contains("\"role\":\"manager\"", req.Body!);
    }

    [Fact]
    public async Task EnsureSeed_Is_NoOp_Over_Http()
    {
        var (svc, handler, _) = Make();
        await svc.EnsureSeedAccountAsync();
        Assert.Empty(handler.Requests); // 서버 배포 시 1회 부트스트랩으로 이관(클라 no-op)
    }

    // ── item1a: 이메일 인증 + 비밀번호 재설정 (§8.2·§8.3·§8.4) ──

    [Fact]
    public async Task Create_With_Email_Sends_Email_And_Maps_Response()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Created,
            "{\"id\":\"newuser\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":\"u@x.com\",\"emailVerified\":false}");

        var user = await svc.CreateAsync("newuser", "pw", UserRole.User, "u@x.com", actingRole: UserRole.Admin);

        // 요청 본문에 email 포함(서버 계약 {id,password,role,email}).
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"email\":\"u@x.com\"", body);
        // 응답의 email·emailVerified가 도메인 User로 매핑됨.
        Assert.Equal("u@x.com", user.Email);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task Create_Without_Email_Sends_Null_Email()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Created,
            "{\"id\":\"newuser\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":null,\"emailVerified\":false}");

        var user = await svc.CreateAsync("newuser", "pw", UserRole.User, email: null, actingRole: UserRole.Admin);

        // 빈/미지정 email은 null로 직렬화(서버가 미수집으로 처리).
        Assert.Contains("\"email\":null", handler.Requests[0].Body!);
        Assert.Null(user.Email);
    }

    [Fact]
    public async Task SetEmail_Uses_Patch_Email_Path_And_Bearer()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "u1", Role = UserRole.User });
        handler.When(HttpMethod.Patch, "accounts/u1/email", _ => FakeHttpMessageHandler.NoContent());

        await svc.SetEmailAsync("u1", "new@x.com");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.Contains("accounts/u1/email", req.Uri!.ToString());
        Assert.Contains("\"email\":\"new@x.com\"", req.Body!);
        Assert.Equal("Bearer", req.AuthorizationScheme); // 본인/파워 → Bearer
    }

    [Fact]
    public async Task RequestPasswordReset_Posts_IdOrEmail_With_ApiKey_And_Accepts_202()
    {
        var (svc, handler, _) = Make();
        // 서버는 항상 202(열거 방지). 202는 2xx이므로 성공 통과(예외 없음).
        handler.When(HttpMethod.Post, "auth/password-reset/request",
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.Accepted, "{\"accepted\":true}"));

        await svc.RequestPasswordResetAsync("someone@x.com");

        var req = handler.Requests[0];
        Assert.Contains("auth/password-reset/request", req.Uri!.ToString());
        Assert.Contains("\"idOrEmail\":\"someone@x.com\"", req.Body!);
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader));
        Assert.Null(req.AuthorizationScheme); // 비로그인(API키만)
    }

    [Fact]
    public async Task ConfirmPasswordResetByCode_Posts_Code_Fields()
    {
        var (svc, handler, _) = Make();
        handler.When(HttpMethod.Post, "auth/password-reset/confirm",
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "{\"reset\":true}"));

        await svc.ConfirmPasswordResetByCodeAsync("u1", "123456", "newpw");

        var body = handler.Requests[0].Body!;
        Assert.Contains("\"idOrEmail\":\"u1\"", body);
        Assert.Contains("\"code\":\"123456\"", body);
        Assert.Contains("\"newPassword\":\"newpw\"", body);
    }

    [Fact]
    public async Task ConfirmPasswordResetByCode_401_Maps_To_InvalidOperation()
    {
        var (svc, handler, _) = Make();
        // 코드 불일치·만료 → 서버 401 → MapToDomainException으로 InvalidOperationException.
        handler.WhenJson(HttpMethod.Post, "auth/password-reset/confirm", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"재설정 코드가 올바르지 않거나 만료되었습니다.\"}}");

        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.ConfirmPasswordResetByCodeAsync("u1", "000000", "newpw"));
    }

    [Fact]
    public async Task ConfirmPasswordResetByToken_Posts_Token_And_Id()
    {
        var (svc, handler, _) = Make();
        handler.When(HttpMethod.Post, "auth/password-reset/confirm",
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "{\"reset\":true}"));

        await svc.ConfirmPasswordResetAsync("u1", "tok.secret", "newpw");

        var body = handler.Requests[0].Body!;
        Assert.Contains("\"token\":\"tok.secret\"", body);
        Assert.Contains("\"id\":\"u1\"", body);
        Assert.Contains("\"newPassword\":\"newpw\"", body);
    }

    [Fact]
    public async Task RequestEmailVerification_Posts_IdOrEmail_Accepts_202()
    {
        var (svc, handler, _) = Make();
        handler.When(HttpMethod.Post, "auth/verify-email/request",
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.Accepted, "{\"accepted\":true}"));

        await svc.RequestEmailVerificationAsync("u1");

        var req = handler.Requests[0];
        Assert.Contains("auth/verify-email/request", req.Uri!.ToString());
        Assert.Contains("\"idOrEmail\":\"u1\"", req.Body!);
    }

    [Fact]
    public async Task ConfirmEmailVerification_By_Code_Returns_True_On_Verified()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/verify-email/confirm", HttpStatusCode.OK,
            "{\"verified\":true}");

        var ok = await svc.ConfirmEmailVerificationAsync("u1", "123456");

        Assert.True(ok);
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"id\":\"u1\"", body);
        Assert.Contains("\"code\":\"123456\"", body);
    }

    [Fact]
    public async Task ConfirmEmailVerification_401_Returns_False_Not_Throws()
    {
        var (svc, handler, _) = Make();
        // 코드 불일치·만료는 인증 실패(false)로 다룬다(예외 대신 결과값).
        handler.WhenJson(HttpMethod.Post, "auth/verify-email/confirm", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"인증 코드가 올바르지 않거나 만료되었습니다.\"}}");

        var ok = await svc.ConfirmEmailVerificationAsync("u1", "000000");

        Assert.False(ok);
    }

    [Fact]
    public async Task ConfirmEmailVerification_By_Token_Returns_True_On_Verified()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/verify-email/confirm", HttpStatusCode.OK,
            "{\"verified\":true}");

        var ok = await svc.ConfirmEmailVerificationByTokenAsync("u1", "tok.secret");

        Assert.True(ok);
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"token\":\"tok.secret\"", body);
        Assert.Contains("\"id\":\"u1\"", body);
    }

    [Fact]
    public async Task Login_Maps_Email_Fields_From_Response()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"u1\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":\"u@x.com\",\"emailVerified\":true}}");

        var user = await svc.LoginAsync("u1", "pw");

        Assert.NotNull(user);
        Assert.Equal("u@x.com", user!.Email);
        Assert.True(user.EmailVerified); // 로그인 응답의 emailVerified가 세션 User에 반영
    }
}
