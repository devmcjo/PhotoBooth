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

/// <summary>POST /accounts 요청: {id, password, role}. actingRole은 서버가 토큰에서 도출(클라 전달 무시). (src/routes/accounts.ts)</summary>
internal sealed class CreateAccountRequest
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
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

/// <summary>클라 응답용 User(비밀번호/해시 절대 미포함, 설계 §6.2 · functions src/services/dto.ts).</summary>
internal sealed class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    /// <summary>ISO8601 문자열.</summary>
    public string? CreatedAt { get; set; }
}
