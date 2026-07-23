using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 로그 폴더 경로 산출 + 탐색기로 열기. 경로 = {App.DataFolder}\logs(App.xaml.cs Serilog 싱크와 동일). (it11 §3.14.5)
/// 탐색기 열기는 best-effort — 키오스크 잠금(셸 교체) 등으로 실패해도 크래시 금지(로그만).
/// 경로 텍스트는 UI에 항상 노출되므로 열기 실패 시에도 수동 탐색 가능.
/// </summary>
public sealed class LogFolderService : ILogFolderService
{
    private readonly ILogger<LogFolderService>? _logger;

    public LogFolderService(ILogger<LogFolderService>? logger = null) => _logger = logger;

    public string LogFolderPath => Path.Combine(App.DataFolder, "logs");

    public void OpenLogFolder()
    {
        var path = LogFolderPath;
        try
        {
            Directory.CreateDirectory(path);   // 없으면 생성(폴더 열기 성공 보장)
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로그 폴더 열기 실패: {Path}", path);
        }
    }
}
