using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;

namespace MCPhoto.App;

/// <summary>
/// 한 키오스크 세션의 공유 상태(프레임 선택 → 촬영 → 합성 → 결과). 세션 종료 시 폐기.
/// 화면 VM들이 이 컨텍스트를 통해 데이터를 주고받는다.
/// </summary>
public sealed class SessionContext
{
    /// <summary>로그인 사용자(게스트면 null).</summary>
    public User? CurrentUser { get; set; }

    /// <summary>선택된 프레임(촬영 전 고정).</summary>
    public FrameTemplate? SelectedFrame { get; set; }

    /// <summary>촬영 세션(컷 버퍼·선택).</summary>
    public CaptureSession Capture { get; } = new();

    /// <summary>선택된 필터.</summary>
    public FilterKind Filter { get; set; } = FilterKind.None;

    /// <summary>합성 최종 이미지 로컬 경로.</summary>
    public string? FinalImagePath { get; set; }

    /// <summary>세션 녹화 원본 경로.</summary>
    public string? SessionVideoPath { get; set; }

    /// <summary>타임랩스 경로.</summary>
    public string? TimelapsePath { get; set; }

    /// <summary>업로드 결과(QR 대상).</summary>
    public ResultSession? Result { get; set; }

    /// <summary>세션 시작 시각(로컬 저장 폴더명).</summary>
    public DateTime SessionTime { get; set; } = DateTime.Now;

    /// <summary>세션 작업 폴더(임시 산출물).</summary>
    public string? WorkFolder { get; set; }

    /// <summary>새 세션 시작(이전 데이터 폐기).</summary>
    public void Reset()
    {
        CurrentUser = null;
        SelectedFrame = null;
        Capture.Discard();
        Filter = FilterKind.None;
        FinalImagePath = null;
        SessionVideoPath = null;
        TimelapsePath = null;
        Result = null;
        SessionTime = DateTime.Now;
        TryCleanupWorkFolder();
        WorkFolder = null;
    }

    private void TryCleanupWorkFolder()
    {
        if (string.IsNullOrEmpty(WorkFolder)) return;
        try
        {
            if (System.IO.Directory.Exists(WorkFolder))
                System.IO.Directory.Delete(WorkFolder, recursive: true);
        }
        catch { /* 임시 폴더 정리 실패 무시 */ }
    }
}
