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
/// <see cref="IAccountService"/>의 HTTP 구현(설계 §5.2). 백엔드 /auth·/accounts 엔드포인트 호출.
///
/// - 로그인: /auth/login(API키) → JWT 수신·<see cref="IBackendSession"/> 저장. 실패(401) 시 null(현행 계약).
/// - CRUD/역할: Bearer. actingRole은 서버가 토큰에서 도출(클라 전달 무시). 비번은 TLS로 평문 전송(서버가 해시).
/// - 온라인 전용(오프라인 시드 없음 — 레거시 Firebase 경로가 롤백용으로 공존, 설계 §9.1).
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

    public async Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
    {
        try
        {
            var res = await SendJsonAsync<LoginResponse>(
                HttpMethod.Post, "auth/login",
                new LoginRequest { Id = id, Password = password },
                bearer: false, ct).ConfigureAwait(false);

            var user = ToUser(res.User) ?? new User { Id = id, Role = UserRole.User };
            Session.SignIn(res.Token, user);
            return user;
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 로그인 실패 = null(현행 계약, AccountService.cs:44,50). 자격 오류를 예외로 올리지 않는다.
            return null;
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default)
    {
        try
        {
            // API키 게이트(로그인 전 상태, Bearer 불가 — LoginAsync와 동일). 응답은 login과 동일한 {token, expiresIn, user}.
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

            var user = ToUser(res.User) ?? new User { Id = string.Empty, Role = UserRole.User };
            Session.SignIn(res.Token, user);
            return user;
        }
        catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 매핑 실패(등록 안 됨/미검증/Google 검증 실패)는 서버가 401로 일반화(열거 방지, §6.4) → null.
            // LoginAsync 401 처리와 동일 계약: 자격 문제는 예외가 아니라 null로 신호한다.
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

    public async Task<User> CreateAsync(
        string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
    {
        // 현행 계약 보존(AccountService.cs:57): 게이트 위반은 서버 왕복 전에 즉시 거부(동일 예외).
        // 서버도 토큰 role로 재검증하므로 이중 방어(클라 위조 무의미).
        if (!actingRole.CanCreate(role))
            throw new UnauthorizedAccessException(
                $"{actingRole} 권한으로 {role} 계정을 생성할 수 없습니다.");

        try
        {
            // email은 선택. 빈 문자열은 null로 정규화(서버가 미수집으로 처리, item1a §8.1).
            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            var res = await SendJsonAsync<UserResponse>(
                HttpMethod.Post, "accounts",
                new CreateAccountRequest
                {
                    Id = id,
                    Password = password,
                    Role = role.ToFirestoreValue(),
                    Email = normalizedEmail,
                },
                bearer: true, ct).ConfigureAwait(false);

            return ToUser(res) ?? new User { Id = id, Role = role };
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(
                HttpMethod.Patch, $"accounts/{Uri.EscapeDataString(id)}/password",
                new ChangePasswordRequest { NewPassword = newPassword },
                bearer: true, ct).ConfigureAwait(false);
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

    /// <summary>
    /// 시드 계정 보장은 서버 배포 시 1회 부트스트랩으로 이관(설계 §7.3). HTTP 경로에서는 no-op.
    /// </summary>
    public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;

    // ── item1a: 이메일 인증 + 비밀번호 재설정 (§8.2·§8.3·§8.4) ──

    public async Task SetEmailAsync(string id, string email, CancellationToken ct = default)
    {
        try
        {
            // Bearer(본인/파워). 204. 서버가 emailVerified=false 리셋 + 인증 메일 발송.
            await SendNoContentAsync(
                HttpMethod.Patch, $"accounts/{Uri.EscapeDataString(id)}/email",
                new SetEmailRequest { Email = email },
                bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default)
    {
        try
        {
            // API키(비로그인). 서버는 존재/상태 무관 202(열거 방지) — 202는 2xx이므로 그대로 성공 통과.
            await SendNoContentAsync(
                HttpMethod.Post, "auth/password-reset/request",
                new IdOrEmailRequest { IdOrEmail = idOrEmail },
                bearer: false, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default)
    {
        try
        {
            // 링크 경로: {token, id, newPassword}. 성공 200 {reset:true}, 실패 400/401.
            await SendNoContentAsync(
                HttpMethod.Post, "auth/password-reset/confirm",
                new PasswordResetConfirmByTokenRequest { Token = token, Id = id, NewPassword = newPassword },
                bearer: false, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default)
    {
        try
        {
            // 코드 경로: {idOrEmail, code, newPassword}. 성공 200, 실패 400/401(코드 불일치·만료).
            await SendNoContentAsync(
                HttpMethod.Post, "auth/password-reset/confirm",
                new PasswordResetConfirmByCodeRequest { IdOrEmail = idOrEmail, Code = code, NewPassword = newPassword },
                bearer: false, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default)
    {
        try
        {
            // API키. 서버는 존재/상태 무관 202(열거 방지·재발송 겸용).
            await SendNoContentAsync(
                HttpMethod.Post, "auth/verify-email/request",
                new IdOrEmailRequest { IdOrEmail = idOrEmail },
                bearer: false, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default)
    {
        try
        {
            // 코드 경로: {id, code}. 성공 200 {verified:true}.
            var res = await SendJsonAsync<VerifyEmailResponse>(
                HttpMethod.Post, "auth/verify-email/confirm",
                new VerifyEmailConfirmByCodeRequest { Id = id, Code = code },
                bearer: false, ct).ConfigureAwait(false);
            return res.Verified;
        }
        catch (BackendException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            // 코드 불일치·만료는 인증 실패(false)로 다룬다(예외 대신 결과값 — UI가 안내).
            return false;
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default)
    {
        try
        {
            // 링크 경로: {token, id}. 성공 200 {verified:true}.
            var res = await SendJsonAsync<VerifyEmailResponse>(
                HttpMethod.Post, "auth/verify-email/confirm",
                new VerifyEmailConfirmByTokenRequest { Token = token, Id = id },
                bearer: false, ct).ConfigureAwait(false);
            return res.Verified;
        }
        catch (BackendException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            return false;
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    /// <summary>UserResponse(비번 미포함) → 도메인 User. Password는 채우지 않는다(UI 미표시, 설계 §6.2).</summary>
    private static User? ToUser(UserResponse? dto)
    {
        if (dto is null) return null;
        return new User
        {
            Id = dto.Id,
            Password = string.Empty,
            Role = UserRoleExtensions.ParseRole(dto.Role),
            CreatedAt = ParseIso(dto.CreatedAt),
            Email = dto.Email,
            EmailVerified = dto.EmailVerified,
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
