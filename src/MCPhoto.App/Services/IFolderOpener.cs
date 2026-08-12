namespace MCPhoto.App.Services;

/// <summary>
/// 임의 폴더를 탐색기로 열기(best-effort). VM이 <c>System.Diagnostics.Process</c>를 직접 만지지 않게 하는
/// 경계 규약(<see cref="IClipboardService"/>·<see cref="ILogFolderService"/>와 동형). (it26 §5.3)
/// <para>
/// <see cref="ILogFolderService"/>를 일반화하지 않은 이유: 그 인터페이스는 "로그 폴더"라는 고정 대상과
/// 진단 화면이라는 검증된 소비자를 갖는다 — 중복은 몇 줄이고, 그것을 건드려 얻는 이득이 없다.
/// </para>
/// </summary>
public interface IFolderOpener
{
    /// <summary>
    /// 폴더 열기 시도. <b>예외를 밖으로 내보내지 않는다</b> — 성공 여부만 돌려주고 호출부가 안내한다.
    /// 잠금 키오스크(셸 교체·정책 제한)에서 <c>explorer.exe</c> 실행이 차단될 수 있으며, 그때 크래시는 금지다.
    /// </summary>
    /// <param name="path">열 폴더 절대경로. 공백·부재 폴더는 false(⚠️ 폴더를 만들지 않는다).</param>
    bool TryOpen(string? path);
}
