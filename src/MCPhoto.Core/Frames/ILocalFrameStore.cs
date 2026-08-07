using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>진단·동기화용 로컬 프레임 파일 1건의 상태.</summary>
/// <param name="ImagePath">PNG 경로.</param>
/// <param name="DisplayName">프레임 이름(= 파일 base name).</param>
/// <param name="Status">`.slots` 해석 결과(서명 검증 포함).</param>
/// <param name="Owner">`#owner` 값. 해석 실패 시 null.</param>
/// <param name="DbId">서버 문서 id. null이면 서버 미동기.</param>
/// <param name="SlotCount">슬롯 수. 해석 실패 시 0.</param>
public sealed record LocalFrameEntry(
    string ImagePath,
    string DisplayName,
    SlotsDecodeStatus Status,
    string? Owner,
    string? DbId,
    int SlotCount);

/// <summary>
/// 로컬 프레임 저장소(png + `.slots` v2). 루트 = 실행 폴더 <c>Frame\</c>.
/// <para>
/// <b>레이아웃</b>: 공용(번들·DB default 캐시) = <c>{루트}\{이름}.png</c>,
/// 개인 = <c>{루트}\users\{이메일 해시}\{이름}.png</c>.
/// </para>
/// <para>
/// <b>소유권의 권위는 파일 위치가 아니라 서명된 <c>#owner</c></b>다(<see cref="SlotsFileCodec"/>).
/// 폴더를 옮기거나 파일명을 바꿔도 노출 판정은 바뀌지 않는다 — 종전 <c>{계정}_{이름}</c> 접두 규약이
/// 파일명만 고치면 뚫렸던 문제를 없앤 것이다.
/// </para>
/// <para>
/// ⚠️ 로드 결과 <see cref="FrameTemplate.UserId"/>에는 <b>소유자 이메일</b>이 들어간다(서버 DTO의
/// <c>userId</c>=계정 id와 다르다). 소유 판정 기준을 하나로 두기 위함이므로, 서버에서 받은 객체를
/// 목록에 그대로 쓰지 말고 반드시 <see cref="SaveUserFrame"/>를 거친 반환값을 쓸 것.
/// </para>
/// </summary>
public interface ILocalFrameStore
{
    /// <summary>
    /// 공용 프레임 저장(루트, <c>#owner=default</c>). 번들 캐시·DB default 캐시·power 공용 생성이 쓴다.
    /// </summary>
    /// <param name="dbId">서버 문서 id(있으면 기록 — 동기화·삭제 대조 키).</param>
    FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId);

    /// <summary>
    /// 개인 프레임 저장(<c>users/{해시}/</c>, <c>#owner={이메일}</c>).
    /// </summary>
    /// <param name="ownerEmail">소유자 이메일(정규화 전 값을 넘겨도 된다).</param>
    /// <param name="dbId">서버 문서 id. 서버 등록 후 호출하면 기록된다.</param>
    FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId);

    /// <summary>공용 프레임 로딩(루트). 게스트 포함 전원 노출.</summary>
    IReadOnlyList<FrameTemplate> LoadPublic();

    /// <summary>개인 프레임 로딩. 서명 검증 + <c>#owner</c> 일치를 통과한 것만 돌려준다.</summary>
    IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail);

    /// <summary>로컬 프레임 삭제(png + `.slots`). 성공 여부.</summary>
    bool DeleteLocal(FrameTemplate frame);

    /// <summary>공용 프레임 이름 집합(이름 충돌 검사용).</summary>
    IReadOnlySet<string> PublicFrameNames();

    /// <summary>본인 개인 프레임 이름 집합(이름 충돌 검사용).</summary>
    IReadOnlySet<string> UserFrameNames(string ownerEmail);

    /// <summary>
    /// 파일 단위 상태 열거(진단 화면·동기화용). 서명이 깨진 파일도 <b>상태와 함께</b> 돌려준다
    /// — 로드 계열은 조용히 제외하지만 진단은 "왜 안 보이는지"를 보여야 한다.
    /// </summary>
    /// <param name="ownerEmail">null이면 공용만, 값이 있으면 공용 + 그 계정 개인.</param>
    IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail);
}
