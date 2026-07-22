namespace MCPhoto.Core.Capture;

/// <summary>
/// 세션 임시 작업 폴더(sessions\{guid}) 관리. (it6 #3, PRD §10)
/// 세션 종료 시 개별 workFolder는 SessionContext.Reset이 삭제하고,
/// 앱 시작 시 남은 잔재(비정상 종료 등)를 이 헬퍼가 일괄 정리한다.
/// result\(로컬 저장분)·logs\는 정리 대상이 아니다 — sessions 루트만 비운다.
/// </summary>
public static class SessionWorkspace
{
    public const string SessionsFolderName = "sessions";

    /// <summary>dataFolder\sessions 경로.</summary>
    public static string SessionsRoot(string dataFolder)
        => Path.Combine(dataFolder, SessionsFolderName);

    /// <summary>
    /// 앱 시작 시 sessions 루트의 잔여 하위 항목을 모두 삭제(활성 세션 없음 전제).
    /// 개별 삭제 실패는 무시(사용 중 파일 등) — 최대한 정리. 삭제한 항목 수 반환.
    /// </summary>
    public static int CleanupOnStartup(string dataFolder)
    {
        var root = SessionsRoot(dataFolder);
        if (!Directory.Exists(root)) return 0;

        int removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try { Directory.Delete(dir, recursive: true); removed++; }
            catch { /* 사용 중·권한 등 개별 실패 무시 */ }
        }
        foreach (var file in Directory.EnumerateFiles(root))
        {
            try { File.Delete(file); removed++; }
            catch { /* 무시 */ }
        }
        return removed;
    }
}
