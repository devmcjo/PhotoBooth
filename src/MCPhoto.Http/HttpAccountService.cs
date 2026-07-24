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

    public async Task<User> CreateAsync(
        string id, string password, UserRole role, UserRole actingRole, CancellationToken ct = default)
    {
        // 현행 계약 보존(AccountService.cs:57): 게이트 위반은 서버 왕복 전에 즉시 거부(동일 예외).
        // 서버도 토큰 role로 재검증하므로 이중 방어(클라 위조 무의미).
        if (!actingRole.CanCreate(role))
            throw new UnauthorizedAccessException(
                $"{actingRole} 권한으로 {role} 계정을 생성할 수 없습니다.");

        try
        {
            var res = await SendJsonAsync<UserResponse>(
                HttpMethod.Post, "accounts",
                new CreateAccountRequest { Id = id, Password = password, Role = role.ToFirestoreValue() },
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
