using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Build;

/// <summary>
/// 실행 파일 자신에서 빌드 정보를 읽는 <see cref="IBuildInfoService"/> 구현. 외부 파일 의존 없음. (it18)
///
/// 버전   = 엔트리 어셈블리의 <see cref="AssemblyName.Version"/> 앞 3자리(Directory.Build.props의 Version).
/// 빌드일 = exe 파일의 최종 수정 시각(로컬).
/// </summary>
public sealed class AssemblyBuildInfoService : IBuildInfoService
{
    /// <summary>버전을 얻지 못했을 때의 폴백.</summary>
    public const string DefaultVersion = "0.0.0";

    /// <summary>빌드 시각 표기 포맷. 진단 화면의 Web Deploy Date와 같은 형식으로 맞춰 나란히 읽힌다.</summary>
    public const string BuildDateFormat = "yyyy-MM-dd HH:mm";

    public string Version { get; } = DefaultVersion;
    public string BuildDate { get; } = string.Empty;

    /// <summary>"v{Version}". 배포 채널(종전 Site)은 표기하지 않는다 — §요구 it18.</summary>
    public string DisplayText => $"v{Version}";

    /// <param name="assembly">버전 출처. null이면 엔트리 어셈블리(단일 파일 퍼블리시에서도 유효).</param>
    /// <param name="exePath">
    /// 빌드 시각을 읽을 실행 파일 경로. null이면 <see cref="Environment.ProcessPath"/>.
    /// ⚠️ Assembly.Location은 단일 파일 퍼블리시에서 빈 문자열이므로 쓰지 않는다.
    /// </param>
    public AssemblyBuildInfoService(
        Assembly? assembly = null, string? exePath = null, ILogger<AssemblyBuildInfoService>? logger = null)
    {
        Version = ReadVersion(assembly ?? Assembly.GetEntryAssembly(), logger);
        BuildDate = ReadBuildDate(exePath ?? Environment.ProcessPath, logger);
        logger?.LogInformation("빌드 정보: v{Ver} · {Build}", Version, BuildDate);
    }

    /// <summary>
    /// 어셈블리 버전의 앞 3자리를 "major.minor.patch"로. AssemblyVersion은 4자리(1.1.6.0)로 저장되지만
    /// 표기에서는 항상 0인 revision을 떼어 "1.1.6"으로 읽힌다.
    /// </summary>
    private static string ReadVersion(Assembly? assembly, ILogger? logger)
    {
        try
        {
            if (assembly?.GetName().Version is { } v)
                return v.ToString(fieldCount: 3);

            logger?.LogWarning("어셈블리 버전을 읽을 수 없음 — 기본값 사용(v{Ver})", DefaultVersion);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "어셈블리 버전 읽기 실패 — 기본값 사용(v{Ver})", DefaultVersion);
        }
        return DefaultVersion;
    }

    /// <summary>
    /// exe의 최종 수정 시각. CreationTime을 쓰지 않는 이유: 설치·복사 시점으로 덮어써져
    /// "빌드 시각"이 아니라 "설치 시각"이 된다. LastWriteTime은 인스톨러(Inno Setup)가 원본 시각을
    /// 보존하므로 배포 후에도 빌드 시각으로 남는다.
    /// </summary>
    private static string ReadBuildDate(string? exePath, ILogger? logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                logger?.LogWarning("실행 파일 경로를 찾을 수 없어 빌드 시각 미표기('{Path}')", exePath);
                return string.Empty;
            }

            // 숫자·구분자만 있는 고정 포맷이라 invariant가 안전(로케일에 따라 표기가 흔들리지 않게).
            return File.GetLastWriteTime(exePath).ToString(BuildDateFormat, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "빌드 시각 읽기 실패 — 미표기");
            return string.Empty;
        }
    }
}
