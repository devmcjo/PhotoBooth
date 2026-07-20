namespace MCPhoto.Core.LocalSave;

/// <summary>
/// 로컬 결과물 저장(TTL 무관, 영구). saveLocalCopy on일 때만. QR 전송과 독립. (PRD §F4, §9 #34)
/// </summary>
public interface ILocalSaveService
{
    /// <summary>
    /// 결과물을 {localSavePath}\mcphoto_YYMMDD_HHMM\에 저장.
    /// final.{ext}, timelapse.mp4 규약(firebase-contract §4.2 재사용).
    /// </summary>
    /// <returns>생성된 세션 폴더 경로. 경로 쓰기 불가 시 예외 대신 null(크래시 금지).</returns>
    Task<string?> SaveAsync(
        string localSavePath,
        string finalImagePath,
        string? timelapsePath,
        DateTime sessionTime,
        CancellationToken ct = default);
}
