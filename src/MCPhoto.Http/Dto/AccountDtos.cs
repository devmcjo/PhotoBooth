namespace MCPhoto.Http.Dto;

/// <summary>POST /auth/google 응답: {token, expiresIn, user}. (functions src/routes/auth.ts)</summary>
internal sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public UserResponse? User { get; set; }
}

/// <summary>
/// POST /auth/google 요청(item1b Google SSO, 설계 §5.1): {code, codeVerifier, redirectUri, nonce?}. API키.
/// 브라우저 loopback으로 받은 authorization code(+PKCE verifier·실제 redirectUri)를 백엔드가 교환·검증한다.
/// client secret은 백엔드 전용 — 이 요청에 담지 않는다. 응답은 <see cref="LoginResponse"/>.
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

/// <summary>PATCH /accounts/{id}/role 요청: {role}. (src/routes/accounts.ts)</summary>
internal sealed class SetRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

/// <summary>POST /accounts/me/pin/verify 요청(E1, 게이트 검증): {pin}. Bearer. (it14 §4.3, src/routes/accounts.ts)</summary>
internal sealed class VerifyPinRequest
{
    public string Pin { get; set; } = string.Empty;
}

/// <summary>POST /accounts/me/pin/verify 응답: {ok}. 일치 200 {ok:true}, 불일치 401, 미설정 409. (it14 §4.3)</summary>
internal sealed class VerifyPinResponse
{
    public bool Ok { get; set; }
}

/// <summary>
/// PUT /accounts/me/pin 요청(E2, 본인 PIN 설정/변경): {newPin, currentPin?}. Bearer.
/// 기존 PIN 있으면 currentPin 확인 필수(불일치 401), 미설정이면 최초 설정(currentPin null/생략). (it14 §4.3)
/// </summary>
internal sealed class SetPinRequest
{
    public string NewPin { get; set; } = string.Empty;

    /// <summary>현재 PIN(최초 설정 시 null → 직렬화 제외). 값이 있으면 서버가 본인 재인증에 사용.</summary>
    public string? CurrentPin { get; set; }
}

/// <summary>PUT /accounts/{id}/pin 요청(E3, 타 계정 PIN 재설정): {newPin}. Bearer(canManage 권한). (it14 §4.3)</summary>
internal sealed class ResetPinRequest
{
    public string NewPin { get; set; } = string.Empty;
}

/// <summary>
/// 클라 응답용 User(해시 절대 미포함). 와이어 형식은 it15 §9.1에서 동결.
/// 서버가 잔여 필드(예: 폐지된 emailVerified)를 보내도 System.Text.Json 기본 설정이 무시하므로 배포 순서 독립.
/// (functions src/services/dto.ts)
/// </summary>
internal sealed class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>ISO8601 문자열.</summary>
    public string? CreatedAt { get; set; }

    /// <summary>Google 계정 이메일. SSO 신원의 근거이므로 정상 계정에는 항상 존재한다.</summary>
    public string? Email { get; set; }

    /// <summary>it15 D2: 인증 제공자 문자열("google"). 미지원값은 클라가 Unknown으로 파싱한다.</summary>
    public string? AuthMethod { get; set; }

    /// <summary>it14: 진입 PIN 설정 여부(pinHash!=null 파생, 원문 미노출). (it14 §5.3)</summary>
    public bool HasPin { get; set; }
}
