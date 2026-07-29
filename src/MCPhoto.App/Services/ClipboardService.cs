using System.Windows;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// WPF <see cref="Clipboard"/> 래퍼. 클립보드는 다른 프로세스가 점유 중이면 열기에 실패할 수 있으므로
/// (CLIPBRD_E_CANT_OPEN → ExternalException) 예외를 흘리지 않고 false를 반환한다 —
/// 키오스크에서 복사 실패가 앱을 죽이면 안 된다(LogFolderService의 best-effort 규약 승계).
/// Clipboard는 STA(UI 스레드) 전용이므로 호출은 커맨드(UI 스레드)에서만 이뤄진다.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private readonly ILogger<ClipboardService>? _logger;

    public ClipboardService(ILogger<ClipboardService>? logger = null) => _logger = logger;

    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "클립보드 복사 실패");
            return false;
        }
    }
}
