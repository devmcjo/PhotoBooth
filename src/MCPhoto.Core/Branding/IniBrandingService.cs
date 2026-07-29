using System.Text;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Branding;

/// <summary>
/// branding.ini 기반 브랜딩 로더. 실행경로\branding.ini 1순위, 없으면 %ProgramData%\MCPhoto\branding.ini. (it9 §4 C3)
/// 읽기 전용(고객이 편집). 파일 부재/빈 값/손상 시 기본값 "MC Photo"로 폴백.
/// 한글 AppName 대비 UTF-8 명시 읽기(메모장 저장 인코딩 편차 대비). INI 파서는 기존 IniFile 재사용.
/// </summary>
public sealed class IniBrandingService : IBrandingService
{
    /// <summary>브랜딩 기본 표시명(설정 부재 시).</summary>
    public const string DefaultAppName = "MC Photo";
    /// <summary>홈 소제목 기본값(설정 부재 시).</summary>
    public const string DefaultSubtitle = "self custom photobooth";

    private const string FileName = "branding.ini";
    private const string Section = "Branding";
    private const string KeyAppName = "AppName";
    private const string KeySubtitle = "Subtitle";

    public string AppName { get; } = DefaultAppName;
    public string Subtitle { get; } = DefaultSubtitle;

    /// <param name="path">테스트/커스텀용 명시 경로. null이면 기본 위치(실행경로→ProgramData) 자동 탐색.</param>
    public IniBrandingService(string? path = null, ILogger<IniBrandingService>? logger = null)
    {
        try
        {
            var resolved = path ?? ResolveExistingPath();
            if (resolved is null || !File.Exists(resolved))
            {
                logger?.LogInformation("브랜딩 설정 파일 없음 — 기본값 사용(AppName='{App}', Subtitle='{Sub}')",
                    DefaultAppName, DefaultSubtitle);
                return;
            }

            // UTF-8 명시(BOM 유무 무관 안전). 손상 라인은 IniFile이 무시. 빈 값은 기본값 유지.
            var text = File.ReadAllText(resolved, Encoding.UTF8);
            var ini = IniFile.Parse(text);

            var name = ini.GetString(Section, KeyAppName, DefaultAppName);
            if (!string.IsNullOrWhiteSpace(name)) AppName = name.Trim();

            var subtitle = ini.GetString(Section, KeySubtitle, DefaultSubtitle);
            if (!string.IsNullOrWhiteSpace(subtitle)) Subtitle = subtitle.Trim();

            logger?.LogInformation("브랜딩 로드: AppName='{App}', Subtitle='{Sub}' ({Path})", AppName, Subtitle, resolved);
        }
        catch (Exception ex)
        {
            // 어떤 실패에도 기본값으로 진행(앱 크래시 금지).
            logger?.LogWarning(ex, "브랜딩 로드 실패, 기본값 사용(AppName='{App}', Subtitle='{Sub}')",
                DefaultAppName, DefaultSubtitle);
        }
    }

    /// <summary>실행경로\branding.ini → %ProgramData%\MCPhoto\branding.ini 순으로 존재하는 첫 경로.</summary>
    private static string? ResolveExistingPath()
    {
        foreach (var c in Candidates())
            if (File.Exists(c)) return c;
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto", FileName);
    }
}
