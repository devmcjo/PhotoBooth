namespace MCPhoto.App.Services;

/// <summary>
/// 클립보드 복사(best-effort). 진단·상태 화면의 개발자 이메일 복사에 사용한다.
/// VM이 System.Windows.Clipboard를 직접 만지지 않게 하는 경계(ILogFolderService와 동일 규약) —
/// 테스트에서 페이크로 대체 가능하고, 실패는 예외가 아니라 false로 전달된다.
/// </summary>
public interface IClipboardService
{
    /// <summary>텍스트를 클립보드에 넣는다. 실패해도 예외를 던지지 않고 false를 반환.</summary>
    bool TrySetText(string text);
}
