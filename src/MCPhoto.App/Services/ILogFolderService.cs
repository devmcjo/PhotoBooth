namespace MCPhoto.App.Services;

/// <summary>
/// 로그 폴더 경로 노출 + 탐색기로 열기. 진단 화면(#14)이 VM에서 System.Diagnostics.Process·경로를
/// 직접 만지지 않도록 추상화(테스트 가능성 + 관례). 로그 위치 = {App.DataFolder}\logs. (it11 §3.14.5)
/// </summary>
public interface ILogFolderService
{
    /// <summary>로그 폴더 절대 경로(표시용).</summary>
    string LogFolderPath { get; }

    /// <summary>탐색기로 로그 폴더 열기. 실패해도 크래시 금지(로그만).</summary>
    void OpenLogFolder();
}
