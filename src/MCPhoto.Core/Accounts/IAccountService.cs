namespace MCPhoto.Core.Accounts;

using MCPhoto.Core.Models;

/// <summary>
/// 계정 로그인/CRUD/역할. Firestore users. ⚠️ MVP 평문 비교. (PRD §F8, firebase-contract §2.1)
/// </summary>
public interface IAccountService
{
    /// <summary>id/pw 로그인. 성공 시 User, 실패 시 null(평문 비교, MVP).</summary>
    Task<User?> LoginAsync(string id, string password, CancellationToken ct = default);

    /// <summary>
    /// Google SSO 로그인(item1b §5·§7.6). 브라우저 loopback으로 받은 authorization code(+PKCE verifier·실제
    /// redirectUri·nonce)를 백엔드 POST /auth/google로 전달해 code 교환·id_token 검증·계정 매핑을 거쳐 JWT를 받는다.
    /// 성공 시 세션에 토큰·사용자를 저장하고 <see cref="User"/>를 반환한다.
    /// 매핑 실패(등록 안 됨/미검증/Google 오류)는 서버 401 → <c>null</c>(현행 <see cref="LoginAsync"/> 계약과 정합).
    /// 서버가 SSO 미구성(501)이면 <see cref="GoogleSsoNotConfiguredException"/>(자격 문제·네트워크 오류와 구분).
    /// — HTTP 전용. 레거시 Firebase 경로는 <see cref="NotSupportedException"/>(SSO 버튼이 백엔드 모드에서만 노출됨).
    /// </summary>
    Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default);

    /// <summary>
    /// 계정 생성. actingRole(호출자 역할) 기준으로 권한 게이트를 서비스가 강제한다(it2 §7):
    /// admin→{user,manager}, manager→{user}만, 그 외 거부. admin→admin 거부(최종 1인).
    /// 위반 시 <see cref="UnauthorizedAccessException"/>. 중복 id면 예외.
    /// <paramref name="email"/>이 주어지면 unverified로 생성하고 서버가 인증 메일을 발송한다(item1a §8.1).
    /// </summary>
    Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default);

    /// <summary>비밀번호 변경.</summary>
    Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default);

    /// <summary>전체 계정 목록(power 전용 사용자 관리).</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>계정 삭제 + 소유 프레임 cascade 삭제(§F8).</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>역할 변경(admin만 manager 지정).</summary>
    Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default);

    /// <summary>시드 계정(devmcjo/1111/admin) 없으면 생성.</summary>
    Task EnsureSeedAccountAsync(CancellationToken ct = default);

    // ── item1a: 이메일 인증 + 비밀번호 재설정(백엔드 전용 기능, HTTP 구현만 지원) ──

    /// <summary>
    /// 계정 이메일 등록/변경(본인/파워). 서버가 emailVerified=false로 리셋하고 새 email 소유 확인 메일을 발송한다.
    /// (item1a §8.3) — HTTP 전용. 레거시 Firebase 경로는 <see cref="NotSupportedException"/>.
    /// </summary>
    Task SetEmailAsync(string id, string email, CancellationToken ct = default);

    /// <summary>
    /// 비밀번호 재설정 요청(비로그인). idOrEmail로 계정 조회 → 검증된 이메일로 재설정 코드/링크 발송.
    /// 열거 방지: 존재/상태 무관 성공(202)으로 반환한다. (item1a §8.4) — HTTP 전용.
    /// </summary>
    Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default);

    /// <summary>비밀번호 재설정 확인(링크 경로): 결합 토큰 + 계정 id + 새 비번. (item1a §8.4) — HTTP 전용.</summary>
    Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default);

    /// <summary>비밀번호 재설정 확인(코드 경로, 키오스크): idOrEmail + 6자리 코드 + 새 비번. (item1a §8.4) — HTTP 전용.</summary>
    Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// 이메일 인증 재발송 요청. idOrEmail로 계정 조회 → 미인증이면 코드/링크 재발송.
    /// 열거 방지: 존재/상태 무관 성공(202)으로 반환한다. (item1a §8.2) — HTTP 전용.
    /// </summary>
    Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default);

    /// <summary>
    /// 이메일 인증 확인(코드 경로, 키오스크): 계정 id + 6자리 코드. 성공 시 true(emailVerified=true).
    /// (item1a §8.2) — HTTP 전용.
    /// </summary>
    Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default);

    /// <summary>
    /// 이메일 인증 확인(링크 경로): 계정 id + 결합 토큰. 성공 시 true. (item1a §8.2) — HTTP 전용.
    /// </summary>
    Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default);
}
