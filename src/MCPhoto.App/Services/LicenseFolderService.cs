using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 라이선스 고지 폴더 = {설치 폴더}\licenses. 로그 폴더(App.DataFolder)와 달리 **실행 파일 옆**이다 —
/// 배포물에 동봉되는 정적 문서이고, 사용자가 수정할 대상이 아니기 때문이다.
/// 탐색기 열기는 best-effort — 키오스크 잠금(셸 교체) 등으로 실패해도 크래시 금지(로그만).
/// 경로 텍스트는 UI에 항상 노출되므로 열기가 실패해도 수동 탐색이 가능하다.
/// 열기 동작(<paramref name="opener"/>)은 주입 가능 — 테스트가 실제 explorer 실행 부작용 없이 검증하도록.
/// (LogFolderService와 같은 패턴)
/// </summary>
public sealed class LicenseFolderService : ILicenseFolderService
{
    private readonly ILogger<LicenseFolderService>? _logger;
    private readonly Action<string> _open;
    private readonly string _baseDirectory;

    public LicenseFolderService(
        ILogger<LicenseFolderService>? logger = null,
        Action<string>? opener = null,
        string? baseDirectory = null)
    {
        _logger = logger;
        _open = opener ?? OpenInExplorer;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public string LicenseFolderPath => Path.Combine(_baseDirectory, "licenses");

    public bool Exists => Directory.Exists(LicenseFolderPath);

    public void OpenLicenseFolder()
    {
        var path = LicenseFolderPath;
        try
        {
            // ⚠️ 로그 폴더와 달리 **생성하지 않는다.** 없다는 것은 배포 산출물에서 고지가 누락됐다는
            //    뜻이고, 빈 폴더를 만들어 열면 그 사실을 감춘다. 없으면 열지 않고 경고만 남긴다.
            if (!Directory.Exists(path))
            {
                _logger?.LogWarning("라이선스 폴더 없음(배포 누락 가능): {Path}", path);
                return;
            }
            _open(path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "라이선스 폴더 열기 실패: {Path}", path);
        }
    }

    private static void OpenInExplorer(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
}
