using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 편집·삭제 권한 규칙(역할×출처, 순수). (item2 §3, it16 §4)
/// it16: 프레임 쓰기 권한(생성·편집·삭제)은 AdvancedUser 이상만 갖는다 —
/// advanced_user=본인 로컬 생성분만, power(manager/admin)=본인 로컬 + DB 공용 기본,
/// user·temp_user=**사용만**(읽기 전용, E4), 번들/fallback·게스트=불가.
/// </summary>
public static class FrameEditPolicy
{
    /// <summary>
    /// 이 프레임을 현재 역할·계정으로 편집할 수 있는지.
    /// role=null이면 게스트(비로그인) → 항상 불가. userId=현재 계정 id(UserLocal 소유 판정용).
    /// </summary>
    public static bool CanEdit(FrameTemplate frame, UserRole? role, string? userId)
    {
        if (role is null) return false;                       // 게스트
        if (!role.Value.CanWriteFrames()) return false;       // it16 E4: user·temp_user는 사용만(읽기 전용)

        return FrameOrigin.Classify(frame) switch
        {
            FrameOriginKind.UserLocal => FrameOrigin.IsOwnedLocal(frame, userId), // 본인 것만
            FrameOriginKind.DbDefault => role.Value.IsPower(),                    // power만
            _ => false                                                            // 번들·fallback 불가
        };
    }

    /// <summary>
    /// 이 프레임을 현재 역할로 삭제(로컬 파일 제거)할 수 있는지. (it16 E4)
    /// 게스트·쓰기 권한 없는 역할(user·temp_user) 불가. 로컬 저장분 = 가능, DB 공용 = power만,
    /// 번들·fallback·빈 Id = 불가.
    /// ⚠️ 소유자(userId)를 보지 않는다: power가 fork·저장한 **공용** 로컬 프레임은 UserId=null로 로드되므로
    ///    (LocalFrameStore.cs:112-128) IsOwnedLocal로 판정하면 현행 삭제 능력이 회귀한다.
    ///    타인 개인 프레임은 LoadUser의 `{계정}_` 접두 필터로 목록에 애초에 오르지 않는다.
    /// </summary>
    public static bool CanDelete(FrameTemplate frame, UserRole? role)
    {
        if (role is null || !role.Value.CanWriteFrames()) return false;

        return FrameOrigin.Classify(frame) switch
        {
            FrameOriginKind.UserLocal => true,                 // 로컬 저장분(개인 `local:` / power 공용 fork)
            FrameOriginKind.DbDefault => role.Value.IsPower(), // 공용 DB 프레임은 power만
            _ => false                                         // 번들·fallback·빈 Id
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
