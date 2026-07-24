using System.Text;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Build;

/// <summary>
/// bldinfo.ini 기반 빌드 정보 로더. 실행경로\bldinfo.ini 1순위, 없으면 %ProgramData%\MCPhoto\bldinfo.ini.
/// 브랜딩 로더(IniBrandingService)와 동일한 탐색·폴백 규약. UTF-8 명시 읽기, INI 파서는 IniFile 재사용.
/// 파일/키 부재·손상 시 기본값(Version="0.0.0")으로 폴백(앱 크래시 금지).
/// </summary>
public sealed class IniBuildInfoService : IBuildInfoService
{
    /// <summary>파일/키 부재 시 버전 폴백.</summary>
    public const string DefaultVersion = "0.0.0";

    private const string FileName = "bldinfo.ini";
    private const string Section = "General";
    private const string KeyVersion = "Version";
    private const string KeyBuildDate = "BuildDate";
    private const string KeySite = "Site";

    public string Version { get; } = DefaultVersion;
    public string BuildDate { get; } = string.Empty;
    public string Site { get; } = string.Empty;

    /// <summary>"v{Version}"에 Site가 있으면 " · "로 이어붙인 표기 문자열(예: "v1.0.0 · Beta").</summary>
    public string DisplayText
    {
        get
        {
            var parts = new List<string> { $"v{Version}" };
            if (!string.IsNullOrWhiteSpace(Site)) parts.Add(Site);
            // it12 R4: BuildDate는 표기에서 제외(업데이트 지연 시 오래된 앱으로 보일 위험).
            //          BuildDate 프로퍼티/ini 키는 유지 — 표기에서만 뺀다.
            return string.Join("  ·  ", parts);
        }
    }

    /// <param name="path">테스트/커스텀용 명시 경로. null이면 기본 위치(실행경로→ProgramData) 자동 탐색.</param>
    public IniBuildInfoService(string? path = null, ILogger<IniBuildInfoService>? logger = null)
    {
        try
        {
            var resolved = path ?? ResolveExistingPath();
            if (resolved is null || !File.Exists(resolved))
            {
                logger?.LogInformation("빌드 정보 파일(bldinfo.ini) 없음 — 기본값 사용(Version='{Ver}')", DefaultVersion);
                return;
            }

            // UTF-8 명시(BOM 유무 무관). 손상 라인은 IniFile이 무시. 빈 값은 기본값 유지.
            var text = File.ReadAllText(resolved, Encoding.UTF8);
            var ini = IniFile.Parse(text);

            var ver = ini.GetString(Section, KeyVersion, DefaultVersion);
            if (!string.IsNullOrWhiteSpace(ver)) Version = ver.Trim();

            var buildDate = ini.GetString(Section, KeyBuildDate, string.Empty);
            if (!string.IsNullOrWhiteSpace(buildDate)) BuildDate = buildDate.Trim();

            var site = ini.GetString(Section, KeySite, string.Empty);
            if (!string.IsNullOrWhiteSpace(site)) Site = site.Trim();

            logger?.LogInformation("빌드 정보 로드: v{Ver} · {Site} · {Build} ({Path})", Version, Site, BuildDate, resolved);
        }
        catch (Exception ex)
        {
            // 어떤 실패에도 기본값으로 진행(앱 크래시 금지).
            logger?.LogWarning(ex, "빌드 정보 로드 실패, 기본값 사용(Version='{Ver}')", DefaultVersion);
        }
    }

    /// <summary>실행경로\bldinfo.ini → %ProgramData%\MCPhoto\bldinfo.ini 순으로 존재하는 첫 경로.</summary>
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
