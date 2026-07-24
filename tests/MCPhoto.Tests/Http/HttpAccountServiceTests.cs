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
            () => svc.CreateAsync("m2", "pw", UserRole.Manager, actingRole: UserRole.Manager));
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
            () => svc.CreateAsync("dup", "pw", UserRole.User, actingRole: UserRole.Admin));
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
            () => svc.CreateAsync("x", "pw", UserRole.User, actingRole: UserRole.Admin));
    }

    [Fact]
    public async Task Create_Sends_Role_As_Firestore_Value_And_Body()
    {
        var (svc, handler, session) = Make();
        session.SignIn("jwt", new User { Id = "boss", Role = UserRole.Admin });
        handler.WhenJson(HttpMethod.Post, "accounts", HttpStatusCode.Created,
            "{\"id\":\"newuser\",\"role\":\"manager\",\"createdAt\":\"2026-01-01T00:00:00Z\"}");

        var user = await svc.CreateAsync("newuser", "pw", UserRole.Manager, actingRole: UserRole.Admin);

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
}
