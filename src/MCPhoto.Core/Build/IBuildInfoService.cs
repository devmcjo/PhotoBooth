namespace MCPhoto.Core.Build;

/// <summary>
/// 빌드 정보(버전·빌드 시각) 소스. 값은 <b>실행 파일 자신</b>에서 나온다 — 어셈블리 버전 리소스와
/// exe 파일 타임스탬프. 시작 시 1회 확정되며 이후 불변이다. (it18)
///
/// 종전에는 외부 파일 bldinfo.ini에서 읽었으나 폐기했다: ① 산출물의 ini를 따로 교체해야 해서
/// exe 리소스 버전(1.0.0.0)과 표기 버전이 어긋나는 이중 관리였고, ② Site(배포 채널)는 개발·알파
/// 서버를 운영하지 않는 이 프로젝트에서 의미가 없었다.
/// 어떤 실패에도 기본값으로 폴백한다(앱 크래시 금지). 읽기 전용.
/// </summary>
public interface IBuildInfoService
{
    /// <summary>앱 버전(예: "1.1.6"). 어셈블리 버전의 앞 3자리. 확인 불가 시 "0.0.0".</summary>
    string Version { get; }

    /// <summary>
    /// 빌드 시각 문자열(예: "2026-07-30 16:42"). exe 파일의 최종 수정 시각(= 빌드/퍼블리시 시각)이며
    /// 로컬 시간이다. 확인 불가 시 빈 문자열.
    /// </summary>
    string BuildDate { get; }

    /// <summary>UI 표기용 요약(예: "v1.1.6"). 앱 우하단 표기에 쓴다.</summary>
    string DisplayText { get; }
}
