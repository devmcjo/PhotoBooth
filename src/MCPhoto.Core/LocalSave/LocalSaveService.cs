using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.LocalSave;

/// <summary>
/// 로컬 결과물 저장. {localSavePath}\mcphoto_YYMMDD_HHMM\ 폴더에 final·timelapse 복사. (PRD §F4, §9 #34)
/// TTL 무관(영구). 경로 쓰기 불가 시 예외 대신 null(크래시 금지).
/// </summary>
public sealed class LocalSaveService : ILocalSaveService
{
    private readonly ILogger<LocalSaveService>? _logger;

    public LocalSaveService(ILogger<LocalSaveService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>세션 폴더명 규약: mcphoto_YYMMDD_HHMM (예: mcphoto_260720_1445).</summary>
    public static string SessionFolderName(DateTime sessionTime)
        => "mcphoto_" + sessionTime.ToString("yyMMdd_HHmm", CultureInfo.InvariantCulture);

    public Task<string?> SaveAsync(
        string localSavePath,
        string finalImagePath,
        string? timelapsePath,
        DateTime sessionTime,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(localSavePath))
            {
                _logger?.LogWarning("localSavePath 미설정 — 로컬 저장 건너뜀");
                return Task.FromResult<string?>(null);
            }

            // 폴더 충돌 시 접미사(-2, -3…)로 유니크 확보
            var baseFolder = Path.Combine(localSavePath, SessionFolderName(sessionTime));
            var sessionFolder = MakeUniqueFolder(baseFolder);
            Directory.CreateDirectory(sessionFolder);

            // final.{ext}: 원본 확장자 유지(outputFormat 반영)
            var finalExt = Path.GetExtension(finalImagePath);
            if (string.IsNullOrEmpty(finalExt)) finalExt = ".jpg";
            var finalDest = Path.Combine(sessionFolder, "final" + finalExt);
            File.Copy(finalImagePath, finalDest, overwrite: true);

            // timelapse.mp4 (있을 때만)
            if (!string.IsNullOrEmpty(timelapsePath) && File.Exists(timelapsePath))
            {
                var timelapseDest = Path.Combine(sessionFolder, "timelapse.mp4");
                File.Copy(timelapsePath, timelapseDest, overwrite: true);
            }

            _logger?.LogInformation("로컬 저장 완료: {Folder}", sessionFolder);
            return Task.FromResult<string?>(sessionFolder);
        }
        catch (Exception ex)
        {
            // 보호 위치/쓰기 불가 등 → 크래시 금지, 호출부가 안내
            _logger?.LogError(ex, "로컬 저장 실패: {Path}", localSavePath);
            return Task.FromResult<string?>(null);
        }
    }

    private static string MakeUniqueFolder(string baseFolder)
    {
        if (!Directory.Exists(baseFolder)) return baseFolder;
        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseFolder}-{i}";
            if (!Directory.Exists(candidate)) return candidate;
        }
        // 극단 상황: 타임스탬프로 폴백
        return $"{baseFolder}-{Guid.NewGuid():N}";
    }
}
