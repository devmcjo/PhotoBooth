namespace MCPhoto.App.Services;

/// <summary>
/// 오픈소스 라이선스 고지 폴더(설치 폴더의 <c>licenses/</c>) 경로 산출 + 탐색기로 열기.
/// GPLv3 바이너리(ffmpeg.exe)를 재배포하므로 고지를 사용자가 실제로 찾아볼 수 있어야 한다(GPLv3 §4).
/// (it22 §5.1 1-6 — 설계: docs/design/wpf-ffmpeg-licensing-and-distribution-design.md)
/// </summary>
public interface ILicenseFolderService
{
    /// <summary>라이선스 폴더 절대 경로(표시·수동 탐색용). 폴더가 없어도 경로는 반환한다.</summary>
    string LicenseFolderPath { get; }

    /// <summary>라이선스 폴더가 실제로 존재하는지(배포 누락 진단용).</summary>
    bool Exists { get; }

    /// <summary>라이선스 폴더를 탐색기로 열기(best-effort — 실패해도 예외를 던지지 않는다).</summary>
    void OpenLicenseFolder();
}
