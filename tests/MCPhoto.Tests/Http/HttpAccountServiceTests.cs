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
    public async Task ConfirmEmailVerification_409_Throws_InvalidOperation_Not_False()
    {
        var (svc, handler, _) = Make();
        // 설계 §3.4 C4·§6: "이미 다른 계정이 인증한 이메일"은 인증 실패(false)가 아니라 사유 노출이 필요한 초과 케이스.
        // 서버 409 → 흡수하지 않고 InvalidOperationException("…초과…")로 전파(UI가 메시지 표시).
        handler.WhenJson(HttpMethod.Post, "auth/verify-email/confirm", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"해당 이메일로 생성 가능한 계정 수를 초과하였습니다.\"}}");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.ConfirmEmailVerificationAsync("u1", "123456"));
        Assert.Contains("초과", ex.Message);
    }

    [Fact]
    public async Task ConfirmEmailVerificationByToken_409_Throws_InvalidOperation_Not_False()
    {
        var (svc, handler, _) = Make();
        // 링크 경로도 코드 경로와 동일: 409(초과)는 흡수하지 않고 전파.
        handler.WhenJson(HttpMethod.Post, "auth/verify-email/confirm", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"해당 이메일로 생성 가능한 계정 수를 초과하였습니다.\"}}");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.ConfirmEmailVerificationByTokenAsync("u1", "tok.secret"));
        Assert.Contains("초과", ex.Message);
    }

    // ── W-1: self-signup(비로그인 회원가입, §2.3) ──

    [Fact]
    public async Task Register_Success_Returns_User_And_Signs_In_With_ApiKey_Not_Bearer()
    {
        var (svc, handler, session) = Make();
        // 서버는 role="user" 강제 + 가입 즉시 로그인(JWT 발급). 응답은 login과 동일한 {token, expiresIn, user}.
        handler.WhenJson(HttpMethod.Post, "auth/register", HttpStatusCode.Created,
            "{\"token\":\"jwt-reg\",\"expiresIn\":3600,\"user\":{\"id\":\"newbie\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":\"n@x.com\",\"emailVerified\":false}}");

        var user = await svc.RegisterAsync("newbie", "pw1234", "n@x.com");

        Assert.NotNull(user);
        Assert.Equal("newbie", user!.Id);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal("n@x.com", user.Email);
        Assert.False(user.EmailVerified);
        Assert.Equal(string.Empty, user.Password); // 비번은 응답에 없음
        Assert.Equal("jwt-reg", session.Token);     // 가입 즉시 세션 로그인(§D-B3)

        var req = handler.Requests[0];
        Assert.Contains("auth/register", req.Uri!.ToString());
        Assert.Contains("\"id\":\"newbie\"", req.Body!);
        Assert.Contains("\"password\":\"pw1234\"", req.Body!);
        Assert.Contains("\"email\":\"n@x.com\"", req.Body!);
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API키 게이트
        Assert.Null(req.AuthorizationScheme);        // 비로그인(Bearer 아님)
    }

    [Fact]
    public async Task Register_Without_Email_Sends_Null_Email()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/register", HttpStatusCode.Created,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"n2\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var user = await svc.RegisterAsync("n2", "pw1234", email: "   "); // 공백=미수집

        Assert.NotNull(user);
        Assert.Contains("\"email\":null", handler.Requests[0].Body!); // 빈/공백 email은 null로 정규화
    }

    [Fact]
    public async Task Register_Conflict_409_Maps_To_InvalidOperation()
    {
        var (svc, handler, session) = Make();
        // id 중복은 사유 노출(가입 UX). 로그인(401→null)과 달리 register는 실패를 예외로 전파.
        handler.WhenJson(HttpMethod.Post, "auth/register", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"이미 존재하는 아이디입니다: dup\"}}");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.RegisterAsync("dup", "pw1234", null));
        Assert.Contains("이미 존재", ex.Message);
        Assert.Null(session.Token); // 실패 시 세션 미변경
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

    // ── item1b: Google SSO (§5·§7.6) ──

    [Fact]
    public async Task LoginWithGoogle_Success_Stores_Token_And_Sends_ApiKey_Not_Bearer()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt-g\",\"expiresIn\":3600,\"user\":{\"id\":\"boss\",\"role\":\"manager\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":\"boss@x.com\",\"emailVerified\":true}}");

        var user = await svc.LoginWithGoogleAsync("auth-code", "verifier-123", "http://127.0.0.1:5000/", "nonce-1");

        Assert.NotNull(user);
        Assert.Equal("boss", user!.Id);
        Assert.Equal(UserRole.Manager, user.Role); // 역할은 매핑된 MCPhoto 계정에서(Google 아님)
        Assert.Equal("boss@x.com", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal(string.Empty, user.Password);  // 비번은 응답에 없음
        Assert.Equal("jwt-g", session.Token);        // 토큰 세션 저장

        var req = handler.Requests[0];
        Assert.Contains("auth/google", req.Uri!.ToString());
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API키 게이트
        Assert.Null(req.AuthorizationScheme);        // Bearer 아님(로그인 전 상태)
    }

    [Fact]
    public async Task LoginWithGoogle_Sends_Code_Verifier_RedirectUri_Nonce_In_Body()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"u1\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        await svc.LoginWithGoogleAsync("the-code", "the-verifier", "http://127.0.0.1:5000/", "the-nonce");

        var body = handler.Requests[0].Body!;
        Assert.Contains("\"code\":\"the-code\"", body);
        Assert.Contains("\"codeVerifier\":\"the-verifier\"", body);
        Assert.Contains("\"redirectUri\":\"http://127.0.0.1:5000/\"", body);
        Assert.Contains("\"nonce\":\"the-nonce\"", body);
    }

    [Fact]
    public async Task LoginWithGoogle_Failure_401_Returns_Null()
    {
        var (svc, handler, session) = Make();
        // Google 검증 실패(도메인·미검증 등) → 서버 401 일반화(§6.4). 계정 매핑은 자동가입(BE-2)이라 매핑 실패는 사실상 없음.
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.\"}}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.Null(user);            // 자격 문제 = null(LoginAsync와 동일 계약)
        Assert.Null(session.Token);   // 세션 미변경
    }

    [Fact]
    public async Task LoginWithGoogle_501_Throws_NotConfigured_Not_Null()
    {
        var (svc, handler, _) = Make();
        // 서버 SSO 미구성 → 501(HttpError.notImplemented) → 전용 예외(자격 문제·네트워크와 구분).
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.NotImplemented,
            "{\"error\":{\"code\":\"not_implemented\",\"message\":\"Google 로그인이 구성되지 않았습니다.\"}}");

        var ex = await Assert.ThrowsAsync<MCPhoto.Core.Accounts.GoogleSsoNotConfiguredException>(
            () => svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce"));
        Assert.Contains("구성되지 않았습니다", ex.Message);
    }

    [Fact]
    public async Task LoginWithGoogle_Null_Nonce_Serialized_As_Null()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"u1\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", nonce: null);

        // nonce 없이 호출 시 null 직렬화(서버가 nonce 검증 생략).
        Assert.Contains("\"nonce\":null", handler.Requests[0].Body!);
    }

    // ── 재인증 게이트: VerifyPasswordAsync(설정 진입 전, 백엔드 모드 버그 수정) ──

    [Fact]
    public async Task VerifyPassword_Success_200_Returns_True()
    {
        var (svc, handler, _) = Make();
        // /auth/login 재사용(서버 잠금 없음). 200 = 자격 유효.
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"jwt-verify\",\"expiresIn\":3600,\"user\":{\"id\":\"devmcjo\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var ok = await svc.VerifyPasswordAsync("devmcjo", "1111");

        Assert.True(ok);
        var req = handler.Requests[0];
        Assert.Contains("auth/login", req.Uri!.ToString());
        Assert.Contains("\"id\":\"devmcjo\"", req.Body!);
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API키 게이트(비로그인 엔드포인트)
        Assert.Null(req.AuthorizationScheme); // Bearer 아님
    }

    [Fact]
    public async Task VerifyPassword_Wrong_401_Returns_False_Not_Throws()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"아이디 또는 비밀번호가 올바르지 않습니다.\"}}");

        var ok = await svc.VerifyPasswordAsync("devmcjo", "wrong");

        Assert.False(ok); // 자격 불일치 = false(예외 아님)
    }

    [Fact]
    public async Task VerifyPassword_ServerError_500_Throws_FailClosed()
    {
        var (svc, handler, _) = Make();
        // 네트워크/서버 오류는 전파(fail-closed — 게이트가 "확인 불가"로 처리, 오allow 방지).
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.InternalServerError,
            "{\"error\":{\"code\":\"internal\",\"message\":\"서버 오류\"}}");

        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => svc.VerifyPasswordAsync("devmcjo", "1111"));
    }

    [Fact]
    public async Task VerifyPassword_Does_Not_Touch_Session_Token()
    {
        var (svc, handler, session) = Make();
        // 재인증 목적: 현재 로그인 상태(토큰·사용자)를 보존한다. 검증 성공/실패 어느 쪽도 세션을 갱신하지 않는다.
        session.SignIn("existing-token", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"new-token-should-be-ignored\",\"expiresIn\":3600,\"user\":{\"id\":\"devmcjo\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var ok = await svc.VerifyPasswordAsync("devmcjo", "1111");

        Assert.True(ok);
        Assert.Equal("existing-token", session.Token);    // 토큰 불변(SignIn 미호출)
        Assert.Equal("boss", session.CurrentUser!.Id);    // 사용자 불변
    }

    [Fact]
    public async Task VerifyPassword_Without_Session_Leaves_Session_Empty()
    {
        var (svc, handler, session) = Make();
        // 세션이 비어 있어도 검증이 세션을 채우지 않음(SignIn 미호출 재확인).
        handler.WhenJson(HttpMethod.Post, "auth/login", HttpStatusCode.OK,
            "{\"token\":\"leak-token\",\"expiresIn\":3600,\"user\":{\"id\":\"devmcjo\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        await svc.VerifyPasswordAsync("devmcjo", "1111");

        Assert.Null(session.Token);
        Assert.Null(session.CurrentUser);
    }
}
