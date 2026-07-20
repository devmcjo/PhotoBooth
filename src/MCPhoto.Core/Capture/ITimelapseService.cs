namespace MCPhoto.Core.Capture;

/// <summary>
/// 세션 녹화본을 배속 타임랩스 mp4로 변환. 목표 10~15초·무음·H.264. (architecture §2.5)
/// </summary>
public interface ITimelapseService
{
    /// <summary>
    /// sessionVideoPath를 배속 처리해 outputPath에 타임랩스 생성.
    /// 배속 N은 원본 길이에서 목표 길이(10~15초)로 역산.
    /// </summary>
    /// <returns>생성된 타임랩스 경로. 실패 시 null.</returns>
    Task<string?> CreateTimelapseAsync(string sessionVideoPath, string outputPath, CancellationToken ct = default);
}
