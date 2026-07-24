namespace MCPhoto.Core.Accounts;

/// <summary>
/// 백엔드가 Google SSO 미구성(HTTP 501)을 응답했을 때(item1b §5.1·§7.6). 자격 문제(401→null)나 네트워크 오류와
/// 명확히 구분하기 위한 전용 예외 — UI가 "SSO 미구성" 전용 안내로 매핑한다(설정/배포 오류이지 사용자 잘못 아님).
/// </summary>
public sealed class GoogleSsoNotConfiguredException : Exception
{
    public GoogleSsoNotConfiguredException(string message) : base(message)
    {
    }
}
