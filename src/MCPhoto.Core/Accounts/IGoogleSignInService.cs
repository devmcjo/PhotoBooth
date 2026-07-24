namespace MCPhoto.Core.Accounts;

/// <summary>
/// Google OAuth(데스크톱: 시스템 브라우저 + loopback + PKCE) 상호작용 추상화(item1b §7.2).
/// 구현(MCPhoto.App)이 HttpListener·Process.Start·PKCE를 담당하고, authorization code + PKCE verifier +
/// 실제 redirectUri + nonce를 반환한다. 백엔드 교환·검증은 <see cref="IAccountService.LoginWithGoogleAsync"/>가
/// 수행한다(관심사 분리). ViewModel은 System.Net·Process에 직접 의존하지 않는다(MVVM 순수성·테스트 가능성).
/// </summary>
public interface IGoogleSignInService
{
    /// <summary>
    /// 시스템 기본 브라우저로 Google 동의 화면을 열고 loopback으로 authorization code를 수신한다.
    /// PKCE(codeVerifier/challenge S256)·state·nonce를 생성하고, code 수신 시 state를 대조한 뒤 리스너를 종료한다.
    /// 사용자 취소·타임아웃·state 불일치·OAuth error 응답은 <c>null</c>로 신호한다(예외 대신 결과값 — VM이 안내).
    /// 리스너·CancellationTokenSource는 try-finally로 항상 정리한다(포트·핸들 누수 0, §8.5).
    /// </summary>
    /// <param name="ct">사용자 취소 토큰. 서비스 내부 타임아웃과 결합된다.</param>
    /// <returns>백엔드 /auth/google 요청 재료. 취소·실패 시 null.</returns>
    Task<GoogleAuthCodeResult?> AcquireAuthorizationCodeAsync(CancellationToken ct = default);
}

/// <summary>
/// loopback으로 수신한 결과(백엔드 /auth/google 요청 재료, item1b §7.2). 토큰·비밀이 아니다.
/// client secret은 이 결과에 포함되지 않는다(백엔드 전용).
/// </summary>
public sealed class GoogleAuthCodeResult
{
    /// <summary>Google이 loopback으로 반환한 authorization code(1회성·단수명).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>PKCE code_verifier(백엔드가 code 교환 시 사용, RFC 7636 43~128자).</summary>
    public string CodeVerifier { get; init; } = string.Empty;

    /// <summary>실제 사용한 loopback redirect_uri(http://127.0.0.1:{port}/). 백엔드 code 교환이 동일값을 요구.</summary>
    public string RedirectUri { get; init; } = string.Empty;

    /// <summary>id_token nonce 검증용 난수(§8.4). 백엔드가 id_token.nonce와 대조한다.</summary>
    public string? Nonce { get; init; }
}
