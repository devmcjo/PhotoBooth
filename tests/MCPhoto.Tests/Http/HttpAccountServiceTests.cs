using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MCPhoto.Core.Backend;
using MCPhoto.Core.Models;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using MCPhoto.Tests.Http;

namespace MCPhoto.Tests.Http;

/// <summary>
/// P3: HttpAccountService 단위 테스트(FakeHttpMessageHandler, 실서버 호출 없음).
/// it15 §7.2: 계약이 7메서드(Google 로그인 / 목록·삭제·역할 / PIN 3종)로 축소되어
/// id/pw 로그인·회원가입·계정 생성·비번 변경·이메일 인증·재설정 케이스가 전부 삭제됐다.
/// UserResponse 와이어 형식은 §9.1에서 동결(서버 테스트와 같은 픽스처).
/// </summary>
public class HttpAccountServiceTests
{
    private const string ApiKey = "test-client-key";

    /// <summary>§9.1 동결 계약의 UserResponse 예시(서버 googleOnlyAccounts.test.ts와 동일 형식).</summary>
    private const string FrozenUserJson =
        "{\"id\":\"devmcjo\",\"role\":\"admin\",\"createdAt\":\"2025-11-02T08:31:00.000Z\"," +
        "\"email\":\"devmcjo@gmail.com\",\"authMethod\":\"google\",\"hasPin\":true}";

    private static (HttpAccountService svc, FakeHttpMessageHandler handler, BackendSession session) Make()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new BackendSession();
        var factory = new TestHttpClientFactory(handler);
        var svc = new HttpAccountService(factory, session, ApiKey);
        return (svc, handler, session);
    }

    /// <summary>Bearer가 필요한 호출용: Google 로그인 응답을 등록하고 토큰을 세팅한다.</summary>
    private static async Task SignInAsync(HttpAccountService svc, FakeHttpMessageHandler handler)
    {
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt-pin\",\"expiresIn\":3600,\"user\":{\"id\":\"me\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");
        await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");
    }

    // ── 세션·인가 ──

    [Fact]
    public async Task Authenticated_Call_Reuses_Stored_Token_As_Bearer()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt-xyz\",\"expiresIn\":3600,\"user\":{\"id\":\"boss\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");
        handler.WhenJson(HttpMethod.Get, "accounts", HttpStatusCode.OK,
            "[{\"id\":\"boss\",\"role\":\"admin\",\"createdAt\":\"2026-01-01T00:00:00Z\"}]");

        await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");
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
        // ThrowsAny인 이유: 실제 타입은 파생 BackendLoginRequiredException이다(UI가 "로그인 만료"와
        // "토큰 없음"을 구분한 문구를 쓰기 위한 타입). 계약은 여전히 UnauthorizedAccessException 계열.
        var ex = await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => svc.GetAllAsync());
        Assert.False(Assert.IsType<BackendLoginRequiredException>(ex).Expired);   // 만료가 아니라 무토큰
    }

    // ── 계정 관리(power) ──

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
    public async Task SetRole_Forbidden_403_Maps_To_Unauthorized()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "mgr", Role = UserRole.Manager });
        // it13 매트릭스 위반(승격 불가) → 서버 403 → UnauthorizedAccessException(UI 우아 처리).
        handler.WhenJson(HttpMethod.Patch, "accounts/t1/role", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"forbidden\",\"message\":\"권한이 없습니다.\"}}");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.SetRoleAsync("t1", UserRole.User));
    }

    // ── item1b: Google SSO (§5·§7.6) ──

    [Fact]
    public async Task LoginWithGoogle_Success_Stores_Token_And_Sends_ApiKey_Not_Bearer()
    {
        var (svc, handler, session) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt-g\",\"expiresIn\":3600,\"user\":{\"id\":\"boss\",\"role\":\"manager\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"email\":\"boss@x.com\",\"authMethod\":\"google\"}}");

        var user = await svc.LoginWithGoogleAsync("auth-code", "verifier-123", "http://127.0.0.1:5000/", "nonce-1");

        Assert.NotNull(user);
        Assert.Equal("boss", user!.Id);
        Assert.Equal(UserRole.Manager, user.Role); // 역할은 매핑된 MCPhoto 계정에서(Google 아님)
        Assert.Equal("boss@x.com", user.Email);
        Assert.Equal(AuthMethod.Google, user.AuthMethod);
        Assert.Equal("jwt-g", session.Token);      // 토큰 세션 저장

        var req = handler.Requests[0];
        Assert.Contains("auth/google", req.Uri!.ToString());
        Assert.Equal(ApiKey, req.HeaderValue(HttpBackendClient.ApiKeyHeader)); // API키 게이트
        Assert.Null(req.AuthorizationScheme);      // Bearer 아님(로그인 전 상태)
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
        // Google 검증 실패(도메인·미검증 등) → 서버 401 일반화(§6.4). 계정 매핑은 자동가입이라 매핑 실패는 사실상 없음.
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"이 Google 계정으로는 로그인할 수 없습니다.\"}}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.Null(user);            // 자격 문제 = null
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

    // ── it14: 진입 PIN 게이트(E1 verify / E2 본인 설정·변경 / E3 타 계정 재설정) ──

    [Fact]
    public async Task VerifyPin_Success_200_Returns_True_And_Sends_Bearer()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // E1: POST /accounts/me/pin/verify {pin}. 200 {ok:true} = 일치.
        handler.WhenJson(HttpMethod.Post, "accounts/me/pin/verify", HttpStatusCode.OK, "{\"ok\":true}");

        var ok = await svc.VerifyPinAsync("me", "1234");

        Assert.True(ok);
        var req = handler.Requests[1];
        Assert.Contains("accounts/me/pin/verify", req.Uri!.ToString());
        Assert.Contains("\"pin\":\"1234\"", req.Body!);
        Assert.Equal("Bearer", req.AuthorizationScheme); // 로그인 상태(본인 principal.id로만 접근)
    }

    [Fact]
    public async Task VerifyPin_Wrong_401_Returns_False_Not_Throws()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        handler.WhenJson(HttpMethod.Post, "accounts/me/pin/verify", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"PIN이 일치하지 않습니다.\"}}");

        var ok = await svc.VerifyPinAsync("me", "0000");

        Assert.False(ok); // PIN 불일치 = false(예외 아님)
    }

    [Fact]
    public async Task VerifyPin_Unset_409_Throws_FailClosed()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // 409(PIN 미설정)는 전파(게이트가 "확인 불가"로 처리 — fail-open 방지). 호출부가 최초 설정 플로우로 유도.
        handler.WhenJson(HttpMethod.Post, "accounts/me/pin/verify", HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"conflict\",\"message\":\"진입 PIN이 설정되지 않았습니다.\"}}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.VerifyPinAsync("me", "1234"));
    }

    [Fact]
    public async Task VerifyPin_ServerError_500_Throws_FailClosed()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        handler.WhenJson(HttpMethod.Post, "accounts/me/pin/verify", HttpStatusCode.InternalServerError,
            "{\"error\":{\"code\":\"internal\",\"message\":\"서버 오류\"}}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.VerifyPinAsync("me", "1234"));
    }

    [Fact]
    public async Task SetOwnPin_Initial_Sends_Put_With_Null_CurrentPin()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // E2: PUT /accounts/me/pin {newPin, currentPin:null}. 최초 설정 → 204.
        handler.When(HttpMethod.Put, "accounts/me/pin", _ => FakeHttpMessageHandler.NoContent());

        await svc.SetOwnPinAsync("me", currentPin: null, newPin: "1234");

        var req = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Contains("accounts/me/pin", req.Uri!.ToString());
        Assert.Contains("\"newPin\":\"1234\"", req.Body!);
        // currentPin은 null로 직렬화(서버가 null을 최초 설정으로 처리). BackendJson은 null을 생략하지 않는다.
        Assert.Contains("\"currentPin\":null", req.Body!);
        Assert.Equal("Bearer", req.AuthorizationScheme);
    }

    [Fact]
    public async Task SetOwnPin_Change_Sends_CurrentPin()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        handler.When(HttpMethod.Put, "accounts/me/pin", _ => FakeHttpMessageHandler.NoContent());

        await svc.SetOwnPinAsync("me", currentPin: "1111", newPin: "2222");

        var req = handler.Requests[1];
        Assert.Contains("\"currentPin\":\"1111\"", req.Body!);
        Assert.Contains("\"newPin\":\"2222\"", req.Body!);
    }

    [Fact]
    public async Task SetOwnPin_WrongCurrent_401_Throws()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // 현재 PIN 불일치(401)는 예외로 전파(호출부가 안내).
        handler.WhenJson(HttpMethod.Put, "accounts/me/pin", HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"현재 PIN이 올바르지 않습니다.\"}}");

        // 이 라우트의 401은 PIN 불일치 **또는** 토큰 만료다(서버가 둘 다 code="unauthorized"로 준다).
        // 공용 매핑(401→BackendLoginRequiredException="로그인이 만료되었습니다")을 쓰면 PIN을 틀린
        // 사용자에게 재로그인을 시키는 틀린 안내가 되므로, 여기서는 원인을 단정하지 않는
        // 일반 UnauthorizedAccessException으로 올린다.
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.SetOwnPinAsync("me", currentPin: "0000", newPin: "2222"));
        Assert.IsNotType<BackendLoginRequiredException>(ex);   // 만료로 단정하지 않았는지
    }

    [Fact]
    public async Task ResetPin_Sends_Put_To_Target_Id()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // E3: PUT /accounts/{id}/pin {newPin}. 대상 현재 PIN 불요 → 204.
        handler.When(HttpMethod.Put, "accounts/u1/pin", _ => FakeHttpMessageHandler.NoContent());

        await svc.ResetPinAsync("u1", "5678");

        var req = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Contains("accounts/u1/pin", req.Uri!.ToString());
        Assert.Contains("\"newPin\":\"5678\"", req.Body!);
        Assert.Equal("Bearer", req.AuthorizationScheme);
    }

    [Fact]
    public async Task ResetPin_Forbidden_403_Throws_Unauthorized()
    {
        var (svc, handler, _) = Make();
        await SignInAsync(svc, handler);
        // canManage 위반(403) → UnauthorizedAccessException(UI 우아 처리).
        handler.WhenJson(HttpMethod.Put, "accounts/boss/pin", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"forbidden\",\"message\":\"해당 계정의 PIN을 재설정할 권한이 없습니다.\"}}");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ResetPinAsync("boss", "5678"));
    }

    // ── it15 §9.1·§9.3: ToUser 매핑(동결 와이어 형식) ──

    [Fact]
    public async Task Frozen_UserResponse_Maps_All_Fields()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":" + FrozenUserJson + "}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.NotNull(user);
        Assert.Equal("devmcjo", user!.Id);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal("devmcjo@gmail.com", user.Email);
        Assert.Equal(AuthMethod.Google, user.AuthMethod);
        Assert.True(user.HasPin);
        Assert.Equal(new DateTime(2025, 11, 2, 8, 31, 0, DateTimeKind.Utc), user.CreatedAt);
    }

    [Fact]
    public async Task Unknown_AuthMethod_Maps_To_Unknown_Not_Google()
    {
        var (svc, handler, _) = Make();
        // D2: 서버가 미지원 provider를 보내면 조용히 Google로 오인하지 않고 Unknown으로 드러낸다.
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"k\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"authMethod\":\"kakao\"}}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.NotNull(user);
        Assert.Equal(AuthMethod.Unknown, user!.AuthMethod);
    }

    [Fact]
    public async Task Missing_AuthMethod_Maps_To_Unknown_And_HasPin_False()
    {
        var (svc, handler, _) = Make();
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"leg\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.NotNull(user);
        Assert.Equal(AuthMethod.Unknown, user!.AuthMethod);
        Assert.False(user.HasPin);
    }

    [Fact]
    public async Task Residual_EmailVerified_Field_Is_Ignored()
    {
        var (svc, handler, _) = Make();
        // §9.2 배포 순서 독립성: 구 서버가 폐지 필드를 보내도 System.Text.Json 기본 설정이 무시한다.
        handler.WhenJson(HttpMethod.Post, "auth/google", HttpStatusCode.OK,
            "{\"token\":\"jwt\",\"expiresIn\":3600,\"user\":{\"id\":\"u\",\"role\":\"user\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"authMethod\":\"google\",\"emailVerified\":true}}");

        var user = await svc.LoginWithGoogleAsync("code", "verifier", "http://127.0.0.1:5000/", "nonce");

        Assert.NotNull(user);
        Assert.Equal(AuthMethod.Google, user!.AuthMethod);
    }
}
