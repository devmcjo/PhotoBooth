namespace MCPhoto.Core.Build;

/// <summary>
/// 빌드 정보(버전·빌드일·사이트) 소스. 외부 파일 bldinfo.ini의 [General] 섹션에서 1회 로드한다.
/// 버전을 소스코드에 하드코딩하지 않기 위한 분리(배포 산출물의 bldinfo.ini만 교체하면 표기 변경).
/// 파일 부재/빈 값/손상 시 기본값으로 폴백(크래시 금지). 읽기 전용.
/// </summary>
public interface IBuildInfoService
{
    /// <summary>앱 버전(예: "1.0.0"). 파일/키 부재 시 "0.0.0".</summary>
    string Version { get; }

    /// <summary>빌드일 문자열(예: "2026-07-23"). 부재 시 빈 문자열.</summary>
    string BuildDate { get; }

    /// <summary>배포 사이트·채널(예: "Beta"). 부재 시 빈 문자열.</summary>
    string Site { get; }

    /// <summary>UI 표기용 요약(예: "v1.0.0 · Beta"). 값이 없는 항목은 생략(BuildDate는 표기 제외).</summary>
    string DisplayText { get; }
}
