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

    /// <summary>프레임 저장(신규 생성, 이미지 업로드 포함). 계정당 10개 초과 시 예외.</summary>
    Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// 이 저장소가 "같은 frameId 업데이트(덮어쓰기)"를 지원하는지. (item2 §5)
    /// 레거시(Admin, SetAsync)=true, HTTP(PUT /frames/{id})=true. 미지원 저장소는 false로 두고 호출측이 차단.
    /// </summary>
    bool SupportsUpdateById { get; }

    /// <summary>
    /// 기존 공용 기본 프레임 업데이트(같은 <paramref name="frame"/>.Id 덮어쓰기). power 전용.
    /// name·slots·imageSize를 갱신하고 id·userId(null)·isDefault(true)·createdAt은 보존한다.
    /// <paramref name="replaceImage"/>=true면 이미지 바이트도 교체(같은 Storage 키 덮어쓰기), false면 메타만 갱신.
    /// <see cref="SupportsUpdateById"/>=false인 저장소에서 호출하면 <see cref="NotSupportedException"/>. (item2 §4·§5)
    /// </summary>
    Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default);

    /// <summary>프레임 삭제(Firestore 문서 + Storage 이미지). 반환=문서가 실제로 존재해 삭제됐는지(없으면 false).</summary>
    Task<bool> DeleteAsync(string frameId, CancellationToken ct = default);

    /// <summary>계정 소유 프레임 전부 삭제(cascade용).</summary>
    Task DeleteAllByUserAsync(string userId, CancellationToken ct = default);
}
