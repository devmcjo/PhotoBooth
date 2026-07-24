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

    /// <summary>Id 접두·IsDefault로 출처 종류를 판정(순수). 우선순위: bundle → fallback/빈Id → local → DbDefault.</summary>
    public static FrameOriginKind Classify(FrameTemplate frame)
    {
        var id = frame.Id ?? string.Empty;
        if (id.StartsWith(BundlePrefix, StringComparison.Ordinal)) return FrameOriginKind.Bundle;
        if (string.IsNullOrEmpty(id) || id.StartsWith(FallbackPrefix, StringComparison.Ordinal))
            return FrameOriginKind.Fallback;
        if (id.StartsWith(LocalPrefix, StringComparison.Ordinal)) return FrameOriginKind.UserLocal;
        return FrameOriginKind.DbDefault;
    }

    /// <summary>이 프레임이 지정 계정이 소유한 로컬 프레임인지(local: 접두 && UserId==userId, 요구 2 엄격 해석).</summary>
    public static bool IsOwnedLocal(FrameTemplate frame, string? userId)
        => Classify(frame) == FrameOriginKind.UserLocal
           && !string.IsNullOrEmpty(userId)
           && string.Equals(frame.UserId, userId, StringComparison.Ordinal);

    /// <summary>이 프레임이 DB 공용 기본 프레임인지(접두 없는 실 DB id && isDefault=true).</summary>
    public static bool IsDbDefault(FrameTemplate frame)
        => Classify(frame) == FrameOriginKind.DbDefault && frame.IsDefault;
}
