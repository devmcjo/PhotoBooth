namespace MCPhoto.Core.Accounts;

using MCPhoto.Core.Models;

/// <summary>
/// 계정 조회/역할/PIN. 인증은 Google SSO 단일 경로. 백엔드(HTTPS API) 전용 — 로컬 자격증명 없음.
/// (it15 설계 §5.1)
/// </summary>
public interface IAccountService
{
    // ── 인증(Google SSO 단일 경로) ──

    /// <summary>
    /// Google SSO 로그인. 브라우저 loopback으로 받은 authorization code(+PKCE verifier·redirectUri·nonce)를
    /// 백엔드 POST /auth/google로 전달해 code 교환·id_token 검증 후, 검증된 email로 계정을
    /// 자동 생성(temp_user)/매핑하고 JWT를 받는다. 성공 시 세션에 토큰·사용자를 저장하고 <see cref="User"/> 반환.
    /// Google 검증 실패(도메인·미검증 등)는 서버 401 → <c>null</c>.
    /// 서버가 SSO 미구성(501)이면 <see cref="GoogleSsoNotConfiguredException"/>.
    /// </summary>
    Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default);

    // ── 계정 관리(power) ──

    /// <summary>전체 계정 목록(power 전용 사용자 관리).</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>계정 삭제 + 소유 프레임 cascade 삭제.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>역할 변경(it13 매트릭스, 서버 최종 강제).</summary>
    Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default);

    // ── PIN(설정·계정 관리 진입 게이트, it14) ──

    /// <summary>
    /// 진입 게이트: 본인 PIN 대조(E1). 일치 true, 불일치 false.
    /// PIN 미설정(409)·네트워크/서버 오류는 예외로 전파(게이트는 "확인 불가"=차단 — fail-open 금지).
    /// </summary>
    Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default);

    /// <summary>
    /// 본인 PIN 설정/변경(E2). 기존 PIN 있으면 <paramref name="currentPin"/> 확인 필수(불일치는 예외),
    /// null이면 최초 설정. 성공 시 정상 반환, 실패는 예외.
    /// </summary>
    Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default);

    /// <summary>
    /// 타 계정 PIN 재설정(E3, 권한 기반, 대상 현재 PIN 불요).
    /// 위계 위반(서버 403)은 <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default);
}
