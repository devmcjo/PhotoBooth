using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// <see cref="IFolderOpener"/> 기본 구현 — <c>explorer.exe "경로"</c>. (it26 §5.3)
/// <para>
/// 열기 동작(<paramref name="opener"/>)은 주입 가능하다 — 테스트가 실제 탐색기를 띄우는 부작용 없이
/// 실패·부재 경로를 검증하도록(<see cref="LogFolderService"/>와 같은 이음새).
/// </para>
/// <para>
/// ⚠️ <c>Directory.CreateDirectory</c>를 <b>하지 않는다</b>(LogFolderService와 다른 점). 여기서는
/// <b>폴더가 없다는 사실 자체가 정보</b>다 — 사진이 없다는 뜻이므로, 빈 폴더를 만들어 "저장된 것처럼"
/// 보이게 하면 거짓이 된다.
/// </para>
/// </summary>
public sealed class FolderOpener : IFolderOpener
{
    private readonly ILogger<FolderOpener>? _logger;
    private readonly Action<string> _open;

    public FolderOpener(ILogger<FolderOpener>? logger = null, Action<string>? opener = null)
    {
        _logger = logger;
        _open = opener ?? OpenInExplorer;
    }

    public bool TryOpen(string? path)
    {
        // 호출부가 이미 링크를 숨겼어야 하는 상태(경로 없음) — 조용히 실패한다.
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (!Directory.Exists(path))
            {
                _logger?.LogWarning("폴더가 없어 열 수 없습니다: {Path}", path);
                return false;
            }

            _open(path);
            return true;
        }
        catch (Exception ex)
        {
            // 잠금 키오스크·정책 차단·잘못된 경로 문자 등. 크래시 금지 — 호출부가 경로를 화면에 노출한다.
            _logger?.LogWarning(ex, "폴더 열기 실패: {Path}", path);
            return false;
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
