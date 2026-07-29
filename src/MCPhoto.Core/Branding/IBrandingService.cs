namespace MCPhoto.Core.Branding;

/// <summary>
/// 앱 브랜딩(제품 표시명) 소스. 외부 설정(branding.ini)에서 1회 로드한 값을 노출한다. (it9 §4 C3)
/// 파일 부재/빈 값/손상 시 기본값 "MC Photo"로 폴백(크래시 금지).
/// </summary>
public interface IBrandingService
{
    /// <summary>앱 표시명(창 제목·홈 타이틀 등). 기본값 "MC Photo".</summary>
    string AppName { get; }

    /// <summary>홈 화면 소제목(타이틀 아래). 기본값 "self custom photobooth".</summary>
    string Subtitle { get; }
}
