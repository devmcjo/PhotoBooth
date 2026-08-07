using System.Security.Cryptography;
using System.Text;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 소유권 판정(순수 로직 — UI·파일시스템 무의존).
/// <para>
/// 소유자의 <b>권위는 서명된 <c>#owner</c> 필드</b>(<see cref="SlotsFileCodec"/>)이며 파일명·폴더명이 아니다.
/// 종전에는 파일명 접두(<c>{계정}_{이름}</c>)가 유일한 근거여서 파일 이름만 바꾸면 남의 프레임을 볼 수 있었다.
/// </para>
/// <para>
/// 식별자로 <b>계정 id가 아니라 이메일</b>을 쓰는 이유(설계 §1): 계정 id는 재가입 시 재사용 여부가 서버
/// 규칙에 좌우돼 "삭제 후 같은 이메일로 재가입하면 예전 프레임에 접근한다"(D-5)가 불확실해진다.
/// 이메일은 확정적이다.
/// </para>
/// </summary>
public static class FrameOwnership
{
    /// <summary>공용(전원 노출) 프레임의 소유자 예약어. 게스트에게도 보인다.</summary>
    public const string DefaultOwner = "default";

    /// <summary>폴더명에 쓰는 이메일 해시 길이(hex 문자 수).</summary>
    private const int FolderHashLength = 16;

    /// <summary>
    /// 이메일 정규화: 트림 + 소문자(invariant). 비교·해시 양쪽이 반드시 이 함수를 거쳐야
    /// "대소문자만 다른 이메일"이 다른 소유자로 갈리지 않는다.
    /// </summary>
    public static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>공용 프레임 소유자인가(대소문자 무시).</summary>
    public static bool IsDefault(string? owner)
        => string.Equals((owner ?? string.Empty).Trim(), DefaultOwner, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 이 프레임을 현재 사용자에게 보여도 되는가.
    /// <list type="number">
    /// <item>공용(<see cref="DefaultOwner"/>) → 게스트 포함 전원</item>
    /// <item>게스트(이메일 없음) → 개인 프레임 미노출</item>
    /// <item>소유자 일치 → 노출</item>
    /// </list>
    /// ⚠️ 서명 검증은 <b>호출 전에</b> 끝나 있어야 한다. 이 함수는 owner 값이 신뢰 가능하다고 전제한다.
    /// </summary>
    /// <param name="owner">`.slots`의 `#owner` 값.</param>
    /// <param name="currentUserEmail">로그인 계정 이메일. 게스트면 null·빈 문자열.</param>
    public static bool CanShow(string? owner, string? currentUserEmail)
    {
        if (IsDefault(owner)) return true;

        var me = NormalizeEmail(currentUserEmail);
        if (me.Length == 0) return false;   // 게스트는 개인 프레임을 보지 않는다

        return string.Equals(NormalizeEmail(owner), me, StringComparison.Ordinal);
    }

    /// <summary>
    /// 개인 프레임 저장 폴더명 = <c>SHA256(정규화 이메일)</c> 앞 16 hex.
    /// <para>
    /// 이메일을 그대로 폴더명에 쓰지 않는 이유: 파일시스템에 개인정보를 평문으로 남기지 않기 위함이다.
    /// <c>#owner</c>는 base64+서명 안에 있으므로, 이 해시까지 쓰면 <b>디스크 어디에도 이메일 평문이 없다</b>.
    /// </para>
    /// 폴더는 <b>저장 위치일 뿐 권위가 아니다</b> — 폴더를 옮겨도 서명된 owner가 그대로라 노출 판정은 바뀌지 않는다.
    /// </summary>
    public static string FolderNameFor(string? email)
    {
        var normalized = NormalizeEmail(email);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..FolderHashLength].ToLowerInvariant();
    }
}
