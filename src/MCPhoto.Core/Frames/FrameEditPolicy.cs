using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 <b>삭제</b> 권한 규칙(역할×출처, 순수). (item2 §3, it16 §4)
/// <para>
/// ⚠️ <b>편집(수정) 판정은 폐지됐다</b>(설계 D-16). 프레임 수정 기능 자체가 사라졌고 — 잘못 만들었으면
/// [기존 프레임 불러오기]로 새로 만든다 — 쓰이지 않는 판정을 남겨두면 "편집 기능이 있나 보다"라는
/// 오해와 잘못된 부활을 부른다. 종전 <c>CanEdit</c>·<c>RequiresFork</c>는 삭제했다.
/// </para>
/// 프레임 쓰기 권한(생성·삭제)은 AdvancedUser 이상만 갖는다 —
/// advanced_user=본인 소유분만, power(manager/admin)=본인 소유 + DB 공용 기본,
/// user·temp_user=**사용만**(읽기 전용, E4), 번들/fallback·게스트=불가.
/// </summary>
public static class FrameEditPolicy
{
    /// <summary>
    /// 이 프레임을 현재 역할로 삭제할 수 있는지. (it16 E4)
    /// <para>
    /// 삭제는 <b>서버 정본 + 로컬 캐시를 모두</b> 지운다(설계 D-19: 서버 먼저 → 성공 시 로컬).
    /// 즉 "영구 삭제"이며 다른 기기에서도 사라진다.
    /// </para>
    /// ⚠️ 소유자(이메일)를 여기서 보지 않는다: 목록에 오르는 개인 프레임은 이미
    /// <see cref="FrameOwnership.CanShow"/>가 본인 것만 통과시켰다. power가 fork·저장한 <b>공용</b>
    /// 로컬 프레임은 UserId가 없어 <see cref="FrameOriginKind.DbDefault"/>로 분류되므로 power 판정을 탄다.
    /// </summary>
    public static bool CanDelete(FrameTemplate frame, UserRole? role)
    {
        if (role is null || !role.Value.CanWriteFrames()) return false;

        return FrameOrigin.Classify(frame) switch
        {
            FrameOriginKind.UserLocal => true,                 // 본인 소유(로컬 전용 또는 서버 동기)
            FrameOriginKind.DbDefault => role.Value.IsPower(), // 공용 기본 프레임은 power만
            _ => false                                         // 번들·fallback·빈 Id
        };
    }
}
