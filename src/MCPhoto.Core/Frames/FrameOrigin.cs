using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>프레임 출처(편집 권한·저장 팝업 판정의 기반).</summary>
public enum FrameOriginKind
{
    /// <summary>본인이 로컬에서 만든 프레임(`local:` 접두).</summary>
    UserLocal,

    /// <summary>DB 공용 기본 프레임(접두 없는 실 DB id + isDefault=true, 자동 다운로드 캐시).</summary>
    DbDefault,

    /// <summary>설치 번들 자산(`bundle:` 접두).</summary>
    Bundle,

    /// <summary>코드 생성 fallback(`fallback` 접두 또는 빈 Id).</summary>
    Fallback
}

/// <summary>
/// FrameTemplate의 출처를 Id 접두·플래그로 판정하는 순수 함수. (item2 §2)
/// 기존 규약 재사용: `local:`=user 로컬(LocalFrameStore), `bundle:`=번들(FrameCatalogService),
/// `fallback`=코드 생성(DefaultFrameProvider), 그 외 접두 없는 실 DB id=공용 기본(CacheFromDb의 #dbid 보존).
/// </summary>
public static class FrameOrigin
{
    private const string LocalPrefix = "local:";
    private const string BundlePrefix = "bundle:";
    private const string FallbackPrefix = "fallback";

    /// <summary>
    /// 출처 종류 판정(순수). 우선순위: bundle → fallback/빈Id → <b>소유자 유무</b> → local 접두 → DbDefault.
    /// <para>
    /// ⚠️ <b>소유자 유무가 개인/공용을 가르는 기준이다</b>(서버 정본 전환). 종전에는 id 접두(<c>local:</c>)만
    /// 봤는데, 개인 프레임이 서버에 저장되면서 <b>실 DB id를 갖게 되어</b> 접두 판정만으로는 DbDefault(공용)로
    /// 오판한다. 그러면 <c>FrameEditPolicy.CanDelete</c>가 power만 허용해 <b>본인이 만든 프레임을 본인이
    /// 지우지 못한다</b>. <c>UserId</c>(=소유자 이메일)가 있으면 무조건 개인이다.
    /// </para>
    /// </summary>
    public static FrameOriginKind Classify(FrameTemplate frame)
    {
        var id = frame.Id ?? string.Empty;
        if (id.StartsWith(BundlePrefix, StringComparison.Ordinal)) return FrameOriginKind.Bundle;
        if (string.IsNullOrEmpty(id) || id.StartsWith(FallbackPrefix, StringComparison.Ordinal))
            return FrameOriginKind.Fallback;

        // 소유자가 있으면 개인 프레임(서버 동기 여부·id 형태와 무관).
        if (!string.IsNullOrEmpty(frame.UserId) && !FrameOwnership.IsDefault(frame.UserId))
            return FrameOriginKind.UserLocal;

        if (id.StartsWith(LocalPrefix, StringComparison.Ordinal)) return FrameOriginKind.UserLocal;
        return FrameOriginKind.DbDefault;
    }

    /// <summary>
    /// 이 프레임을 지정 계정이 소유하는지. 소유자 식별자는 <b>이메일</b>이며 정규화 후 비교한다
    /// (<see cref="FrameOwnership.NormalizeEmail"/> — 대소문자만 다른 이메일이 다른 소유자로 갈리지 않게).
    /// </summary>
    public static bool IsOwnedBy(FrameTemplate frame, string? ownerEmail)
    {
        if (Classify(frame) != FrameOriginKind.UserLocal) return false;
        var me = FrameOwnership.NormalizeEmail(ownerEmail);
        return me.Length > 0
               && string.Equals(FrameOwnership.NormalizeEmail(frame.UserId), me, StringComparison.Ordinal);
    }

    /// <summary>이 프레임이 DB 공용 기본 프레임인지(접두 없는 실 DB id && isDefault=true).</summary>
    public static bool IsDbDefault(FrameTemplate frame)
        => Classify(frame) == FrameOriginKind.DbDefault && frame.IsDefault;
}
