namespace MCPhoto.Core.Frames;

using MCPhoto.Core.Models;

/// <summary>
/// FrameTemplate CRUD(기본/커스텀). Firestore frameTemplates + Storage frames/. (firebase-contract §2.2/§4.1)
/// </summary>
public interface IFrameRepository
{
    /// <summary>공용 기본 프레임(isDefault=true) 조회.</summary>
    Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default);

    /// <summary>특정 계정 소유 커스텀 프레임 조회(최대 10).</summary>
    Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// <b>공용 기본 프레임</b> 저장(신규 생성, 이미지 업로드 포함). power 전용(`POST /frames`).
    /// 서버가 <c>userId=null · isDefault=true</c>를 강제한다.
    /// </summary>
    Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// <b>개인 프레임</b> 저장(신규 생성, 이미지 업로드 포함). advanced_user 이상(`POST /frames/mine`).
    /// <para>
    /// 서버가 <c>userId=principal.id · isDefault=false</c>를 강제하므로 클라가 소유자를 지정하지 않는다.
    /// 개수 상한은 없고(설계 D-10), 같은 계정 안에서 <b>이름이 중복되면 409</b>다(S8).
    /// </para>
    /// </summary>
    Task<FrameTemplate> SaveMineAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default);

    // it15 F1-D2: "기존 프레임 업데이트(PUT /frames/{id})" 계약은 클라이언트에서 폐지했다.
    // 프레임 **수정** 기능 자체가 폐지됐고(설계 D-16 — 종전 fork 저장 규칙도 함께 사라졌다), 재활용은
    // [기존 프레임 불러오기]로 슬롯을 물려받아 **새로 만드는 것**뿐이다(SaveAsync·SaveMineAsync).
    // 서버 라우트는 운영/관리 전용으로 유지된다.

    /// <summary>프레임 삭제(Firestore 문서 + Storage 이미지). 반환=문서가 실제로 존재해 삭제됐는지(없으면 false).</summary>
    Task<bool> DeleteAsync(string frameId, CancellationToken ct = default);

    /// <summary>계정 소유 프레임 전부 삭제(cascade용).</summary>
    Task DeleteAllByUserAsync(string userId, CancellationToken ct = default);
}
