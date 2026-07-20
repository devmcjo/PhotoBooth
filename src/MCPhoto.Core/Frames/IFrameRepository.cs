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

    /// <summary>프레임 저장(이미지 업로드 포함). 계정당 10개 초과 시 예외.</summary>
    Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>프레임 삭제(Firestore 문서 + Storage 이미지).</summary>
    Task DeleteAsync(string frameId, CancellationToken ct = default);

    /// <summary>계정 소유 프레임 전부 삭제(cascade용).</summary>
    Task DeleteAllByUserAsync(string userId, CancellationToken ct = default);
}
