using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 편집 권한 규칙(역할×출처, 순수). (item2 §3)
/// user=본인 로컬 생성분만, power(manager/admin)=본인 로컬 + DB 공용 기본, 번들/fallback·게스트=불가.
/// </summary>
public static class FrameEditPolicy
{
    /// <summary>
    /// 이 프레임을 현재 역할·계정으로 편집할 수 있는지.
    /// role=null이면 게스트(비로그인) → 항상 불가. userId=현재 계정 id(UserLocal 소유 판정용).
    /// </summary>
    public static bool CanEdit(FrameTemplate frame, UserRole? role, string? userId)
    {
        if (role is null) return false; // 게스트

        return FrameOrigin.Classify(frame) switch
        {
            FrameOriginKind.UserLocal => FrameOrigin.IsOwnedLocal(frame, userId), // 본인 것만
            FrameOriginKind.DbDefault => role.Value.IsPower(),                    // power만
            _ => false                                                            // 번들·fallback 불가
        };
    }

    /// <summary>
    /// 저장 시 "로컬만 / DB도 업데이트" 확인 팝업을 띄워야 하는 대상인지(power && DB 공용 기본 프레임).
    /// (user 로컬·신규 생성은 팝업 없이 기존 저장 경로.)
    /// </summary>
    public static bool RequiresDbUpdatePrompt(FrameTemplate frame, UserRole? role)
        => role?.IsPower() == true && FrameOrigin.IsDbDefault(frame);
}
