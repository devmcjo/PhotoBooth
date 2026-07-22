using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 로컬 프레임 저장소(png + .slots). 루트 = 실행 폴더 Frame\ (번들+파워캐시+user 공존, it8 §3 정정).
/// 공용(번들·파워캐시)=접두 없는 `{이름}.png`, user 전용=`{계정}_{이름}.png`. 이름 원문 그대로(sanitize 없음).
/// </summary>
public interface ILocalFrameStore
{
    /// <summary>
    /// 로컬 저장. ownerName 있으면 user 전용(`{owner}_{이름}`), 없으면 공용(`{이름}`). png + .slots(#imagesize 메타) 기록.
    /// 파일시스템 금지문자가 이름/계정에 있으면 <see cref="System.IO.IOException"/>(저장 거부). 반환=저장된 프레임(로컬 경로 반영).
    /// </summary>
    FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName);

    /// <summary>공용 프레임 로딩: 접두 없는 파일(번들 + 파워 캐시). 게스트 포함 노출. (it8 §3.1.1)</summary>
    IReadOnlyList<FrameTemplate> LoadPublic();

    /// <summary>user 전용 프레임 로딩: `{ownerName}_` 접두 파일만(본인 것). (it8 §3.1.1)</summary>
    IReadOnlyList<FrameTemplate> LoadUser(string ownerName);

    /// <summary>DB 프레임 이미지를 다운로드받아 공용 캐시(`{이름}.png`, 접두 없음)로 기록. (it8 §3.3)</summary>
    FrameTemplate CacheFromDb(FrameTemplate frame, byte[] png);

    /// <summary>로컬 프레임 삭제(png + .slots). 성공 여부.</summary>
    bool DeleteLocal(FrameTemplate frame);

    /// <summary>공용 프레임 이름 집합(이름 기준 dedup용). 로컬에 이미 있으면 DB 캐시 스킵.</summary>
    IReadOnlySet<string> PublicFrameNames();
}
