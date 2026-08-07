namespace MCPhoto.Http;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IAccountService"/>의 HTTP 구현(설계 §5.2, it15 §7.2). 백엔드 /auth·/accounts 엔드포인트 호출.
///
/// - 인증: /auth/google(API키) → JWT 수신·<see cref="IBackendSession"/> 저장. 검증 실패(401) 시 null.
/// - 조회/역할/PIN: Bearer. actingRole은 서버가 토큰에서 도출(클라 전달 무시).
/// - 온라인 전용 — it15에서 레거시 Firebase 직결 경로가 폐지되어 유일한 구현이다.
/// </summary>
public sealed class HttpAccountService : HttpBackendClient, IAccountService
{
    public HttpAccountService(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        ILogger<HttpAccountService>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
    }

    public async Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default)
    {
        try
        {
            // API키 게이트(로그인 전 상태, Bearer 불가). 응답은 {token, expiresIn, user}.
            var res = await SendJsonAsync<LoginResponse>(
                HttpMethod.Post, "auth/google",
                new GoogleLoginRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    RedirectUri = redirectUri,
                    Nonce = nonce,
                },
                bearer: false, ct).ConfigureAwait(false);

            // 응답에 user가 없는 비정상 경로의 폴백은 최소 권한(TempUser)으로 둔다 — it15 §5.2.
            var user = ToUser(res.User) ?? new User { Id = string.Empty, Role = UserRole.TempUser };
            Session.SignIn(res.Token, user);
            return user;
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 계정 매핑은 자동 생성(temp_user)/매핑이라 정상 검증된 email은 거의 401이 아니다.
            // 401은 Google 검증 실패(도메인·미검증 등)를 서버가 일반화한 것(열거 방지, §6.4) → null.
            return null;
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            // 501 = 서버에 Google SSO 미구성(§5.1). 자격 문제(401→null)·네트워크 오류와 구분되는 전용 예외로 신호한다.
            throw new GoogleSsoNotConfiguredException(
                string.IsNullOrWhiteSpace(ex.Message) ? "Google 로그인이 구성되지 않았습니다." : ex.Message);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await GetJsonAsync<List<UserResponse>>("accounts", bearer: true, ct)
                .ConfigureAwait(false);
            return res.Select(ToUser).Where(u => u is not null).Select(u => u!).ToList();
        }
        catch (BackendException ex)
        {
            // 403(미인가)은 예외로 전달(현행 빈 배열 폴백과 달리 명확히 구분, 설계 §9.2).
            throw MapToDomainException(ex);
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            // 서버가 소유 프레임 cascade까지 수행(F5). 204.
            await SendNoContentAsync(
                HttpMethod.Delete, $"accounts/{Uri.EscapeDataString(id)}",
                body: null, bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(
                HttpMethod.Patch, $"accounts/{Uri.EscapeDataString(id)}/role",
                new SetRoleRequest { Role = role.ToFirestoreValue() },
                bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    // ── it14: 설정·계정 관리 진입 PIN 게이트 (§4.3 E1/E2/E3) ──

    public async Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default)
    {
        try
        {
            // E1: POST /accounts/me/pin/verify {pin}. Bearer(본인 principal.id로만 접근). 200 {ok:true}=일치.
            var res = await SendJsonAsync<VerifyPinResponse>(
                HttpMethod.Post, "accounts/me/pin/verify",
                new VerifyPinRequest { Pin = pin },
                bearer: true, ct).ConfigureAwait(false);
            return res.Ok;
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false; // PIN 불일치(자격 불일치 = false)
        }
        catch (BackendException ex)
        {
            // 409(PIN 미설정)·네트워크/서버 오류는 전파(게이트가 "확인 불가"로 처리 — fail-open 방지, 설계 §5.2).
            // 409는 MapToDomainException이 InvalidOperationException으로 매핑(호출부가 최초 설정 플로우로 유도 가능).
            throw MapToDomainException(ex);
        }
    }

    public async Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default)
    {
        try
        {
            // E2: PUT /accounts/me/pin {newPin, currentPin?}. Bearer(본인). 204. 기존 PIN 있으면 currentPin 확인,
            // null(최초 설정)이면 서버가 currentPin 검사 생략. currentPin이 null이어도 서버가 null을 최초 설정으로 처리.
            var normalizedCurrent = string.IsNullOrEmpty(currentPin) ? null : currentPin;
            await SendNoContentAsync(
                HttpMethod.Put, "accounts/me/pin",
                new SetPinRequest { NewPin = newPin, CurrentPin = normalizedCurrent },
                bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // ⚠️ 이 라우트에서만 401이 두 가지를 뜻한다: **현재 PIN 불일치**(services/accounts.ts setOwnPin)와
            // 토큰 만료(requireBearer). 서버가 둘 다 code="unauthorized"로 주므로 클라가 구분할 수 없다.
            // 공용 매핑(401→BackendLoginRequiredException=만료)을 그대로 쓰면 PIN을 틀린 사용자에게
            // "다시 로그인해 주세요"라는 **틀린 안내**를 하게 된다 → 여기서는 원인을 단정하지 않는
            // 일반 UnauthorizedAccessException으로 올리고, 호출부가 두 경우를 함께 덮는 문구를 쓴다.
            throw new UnauthorizedAccessException(ex.Message);
        }
        catch (BackendException ex)
        {
            // 형식 오류(400)·계정 없음(404) 등 모두 예외로 전파(호출부가 안내).
            throw MapToDomainException(ex);
        }
    }

    public async Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default)
    {
        try
        {
            // E3: PUT /accounts/{id}/pin {newPin}. Bearer(canManage 권한). 대상 현재 PIN 불요. 204.
            await SendNoContentAsync(
                HttpMethod.Put, $"accounts/{Uri.EscapeDataString(targetId)}/pin",
                new ResetPinRequest { NewPin = newPin },
                bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            // 403(canManage 위반)은 MapToDomainException이 UnauthorizedAccessException으로 매핑(UI 우아 처리).
            throw MapToDomainException(ex);
        }
    }

    /// <summary>UserResponse → 도메인 User. 자격증명(비밀번호)은 계약에 존재하지 않는다(it15 §5.2·§9.1).</summary>
    private static User? ToUser(UserResponse? dto)
    {
        if (dto is null) return null;
        return new User
        {
            Id = dto.Id,
            Role = UserRoleExtensions.ParseRole(dto.Role),
            CreatedAt = ParseIso(dto.CreatedAt),
            Email = dto.Email,
            AuthMethod = AuthMethodExtensions.ParseAuthMethod(dto.AuthMethod),
            HasPin = dto.HasPin,
        };
    }

    private static DateTime ParseIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return DateTime.UtcNow;
        return DateTime.TryParse(
            iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : DateTime.UtcNow;
    }
}
