namespace MCPhoto.Http.Dto;

/// <summary>POST /auth/login 요청. (functions src/routes/auth.ts)</summary>
internal sealed class LoginRequest
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>POST /auth/login 응답: {token, expiresIn, user}. (functions src/routes/auth.ts)</summary>
internal sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public UserResponse? User { get; set; }
}

/// <summary>
/// POST /auth/register 요청(self-signup, 설계 §2.2 B-BE-2): {id, password, email?}. API키(Bearer 불요).
/// role은 서버가 "user"로 강제(클라 지정 불가 — 권한 상승 차단). 응답은 login과 동일한 <see cref="LoginResponse"/>.
/// (functions src/routes/auth.ts)
/// </summary>
internal sealed class RegisterRequest
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>계정 이메일(선택). null이면 서버가 미수집으로 처리(미인증 없이 생성).</summary>
    public string? Email { get; set; }
}

/// <summary>
/// POST /auth/google 요청(item1b Google SSO, 설계 §5.1): {code, codeVerifier, redirectUri, nonce?}. API키.
/// 브라우저 loopback으로 받은 authorization code(+PKCE verifier·실제 redirectUri)를 백엔드가 교환·검증한다.
/// client secret은 백엔드 전용 — 이 요청에 담지 않는다. 응답은 login과 동일한 <see cref="LoginResponse"/>.
/// (functions src/routes/auth.ts)
/// </summary>
internal sealed class GoogleLoginRequest
{
    public string Code { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>id_token replay 방어용 nonce(설계 §8.4). 서버는 null이면 nonce 검증을 생략한다.</summary>
    public string? Nonce { get; set; }
}

/// <summary>
/// POST /accounts 요청: {id, password, role, email?}. actingRole은 서버가 토큰에서 도출(클라 전달 무시).
/// email은 선택(null/미포함이면 서버가 미수집 처리, item1a §8.1). (src/routes/accounts.ts)
/// </summary>
internal sealed class CreateAccountRequest
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>계정 이메일(선택). null이면 직렬화에서 제외돼 서버가 미수집으로 처리. (item1a §8.1)</summary>
    public string? Email { get; set; }
}

/// <summary>PATCH /accounts/{id}/password 요청: {newPassword}. (src/routes/accounts.ts)</summary>
internal sealed class ChangePasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>PATCH /accounts/{id}/role 요청: {role}. (src/routes/accounts.ts)</summary>
internal sealed class SetRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

/// <summary>PATCH /accounts/{id}/email 요청: {email}. Bearer(본인/파워). (item1a §8.3, src/routes/accounts.ts)</summary>
internal sealed class SetEmailRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// POST /auth/password-reset/request · /auth/verify-email/request 요청: {idOrEmail}. API키.
/// 서버는 항상 202로 응답(열거 방지). (item1a §8.2·§8.4, src/routes/auth.ts)
/// </summary>
internal sealed class IdOrEmailRequest
{
    public string IdOrEmail { get; set; } = string.Empty;
}

/// <summary>
/// POST /auth/password-reset/confirm 요청(코드 경로): {idOrEmail, code, newPassword}. API키.
/// (item1a §8.4, src/routes/auth.ts)
/// </summary>
internal sealed class PasswordResetConfirmByCodeRequest
{
    public string IdOrEmail { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// POST /auth/password-reset/confirm 요청(링크 경로): {token, id, newPassword}. API키.
/// 서버는 token이 있으면 링크 경로로 처리하고 id로 계정을 특정한다. (item1a §8.4, src/routes/auth.ts)
/// </summary>
internal sealed class PasswordResetConfirmByTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// POST /auth/verify-email/confirm 요청(코드 경로): {id, code}. API키.
/// (item1a §8.2, src/routes/auth.ts)
/// </summary>
internal sealed class VerifyEmailConfirmByCodeRequest
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// POST /auth/verify-email/confirm 요청(링크 경로): {token, id}. API키.
/// (item1a §8.2, src/routes/auth.ts)
/// </summary>
internal sealed class VerifyEmailConfirmByTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

/// <summary>POST /auth/verify-email/confirm 응답: {verified}. (item1a §8.2, src/routes/auth.ts)</summary>
internal sealed class VerifyEmailResponse
{
    public bool Verified { get; set; }
}

/// <summary>클라 응답용 User(비밀번호/해시 절대 미포함, 설계 §6.2 · functions src/services/dto.ts).</summary>
internal sealed class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    /// <summary>ISO8601 문자열.</summary>
    public string? CreatedAt { get; set; }

    /// <summary>계정 이메일(없으면 null). 토큰·해시는 미포함이지만 email 자체는 노출됨(item1a §8.5).</summary>
    public string? Email { get; set; }

    /// <summary>이메일 소유 확인 여부. (item1a §8.5)</summary>
    public bool EmailVerified { get; set; }
}
