namespace MCPhoto.Core.LocalSave;

/// <summary>
/// 유휴 경고 팝업의 [결과물 폴더 열기] 링크 노출 판정(순수 함수). (it26 §4.6)
/// <para>
/// 두 조건의 AND다: ① 운영자가 ini 옵션을 켰다 ② <b>이 세션의 로컬 저장이 실제로 성공했다</b>
/// (경로는 <c>ILocalSaveService.SaveAsync</c>의 반환값이므로, 저장 off·저장 실패·저장 전 상태는
/// 모두 <c>null</c>이 되어 별도 분기 없이 숨겨진다).
/// </para>
/// <para>
/// ⚠️ 로그인·표시 모드 게이트는 <b>없다</b>(사용자 확정 — 옵션이 유일한 게이트). 그 대가로 옵션의
/// 기본값이 off이고, 설정 화면 캡션이 잠금 키오스크의 위험을 명시한다.
/// </para>
/// </summary>
public static class ResultFolderLinkPolicy
{
    /// <param name="sessionFolder">이 세션의 저장 폴더 절대경로(미저장·실패면 null).</param>
    /// <param name="enableResultFolderOpen">ini <c>EnableResultFolderOpen</c>(기본 false).</param>
    public static bool ShouldShow(string? sessionFolder, bool enableResultFolderOpen)
        => enableResultFolderOpen && !string.IsNullOrEmpty(sessionFolder);
}
