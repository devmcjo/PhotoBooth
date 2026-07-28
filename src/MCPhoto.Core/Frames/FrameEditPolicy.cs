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
    /// 이 프레임을 편집·복사해 저장할 때 원본을 보존하고 새 이름으로 분기(fork)해야 하는지.
    /// DbDefault·Bundle·Fallback(=카탈로그 유래) = true, UserLocal = false. (it15 F1-D4)
    /// 역할과 무관한 규칙이므로 role 인자를 받지 않는다.
    /// </summary>
    public static bool RequiresFork(FrameTemplate frame)
        => FrameOrigin.Classify(frame) != FrameOriginKind.UserLocal;
}
