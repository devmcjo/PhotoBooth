namespace MCPhoto.Core.Settings;

/// <summary>
/// 민감 설정값(예: 백엔드 게이트 키)의 저장 시 보호(암호화)·복원 추상화.
/// 플랫폼 의존(Windows DPAPI 등) 구현을 Core에서 격리한다(Core는 net8.0 크로스플랫폼).
/// </summary>
public interface ISecretProtector
{
    /// <summary>평문 → 저장용 보호 문자열(예: DPAPI 암호문 base64). 빈 값은 그대로 반환.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// 저장 문자열 → 평문. 보호 형식이 아니면(평문·수기 편집·publish 주입) 평문으로 간주해 그대로 반환(느슨).
    /// 복호화 실패(다른 PC 등)면 빈 문자열(키 없음 취급).
    /// </summary>
    string Unprotect(string stored);
}

/// <summary>보호 없음(패스스루). 테스트·비Windows·기본값용 — 평문 저장/복원.</summary>
public sealed class NullSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string stored) => stored;
}
