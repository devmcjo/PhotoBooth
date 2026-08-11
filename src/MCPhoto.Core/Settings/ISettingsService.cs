namespace MCPhoto.Core.Settings;

/// <summary>
/// INI 로드/저장, 창 복원. %ProgramData%\MCPhoto\MCPhoto.ini 우선. (architecture §7)
/// </summary>
public interface ISettingsService
{
    /// <summary>현재 설정(로드됨). 최초 접근 시 Load().</summary>
    AppSettings Current { get; }

    /// <summary>
    /// 활성 INI 파일의 절대 경로. 후보(실행경로 → ProgramData → LocalAppData) 중 <b>쓰기 가능한 첫 곳</b>이며
    /// 저장 폴백이 성공하면 그 경로로 승격된다.
    /// <para>
    /// 왜 계약에 노출하는가(it23 §B5.4): "파일이 있는 곳"이 아니라 "쓸 수 있는 곳"이 선택되므로 사람이 편집한
    /// ini가 앱이 읽는 ini와 다를 수 있다. 그 판정 결과를 앱이 어디에도 표시하지 않아 원인 추적이 불가능했다.
    /// 진단 화면과 <c>ITestModeService</c>가 이 값을 쓴다 — 별도의 경로 해석을 새로 만들면 판정이 둘로 갈린다.
    /// </para>
    /// </summary>
    string IniPath { get; }

    /// <summary>INI에서 로드. 파일 없으면 전 항목 기본값. 손상돼도 크래시 금지.</summary>
    AppSettings Load();

    /// <summary>
    /// 현재 설정을 INI에 즉시 flush. 성공 시 true. 쓰기 폴백 체인 실패 시 false(예외는 내부 로깅). (it3 §3)
    /// </summary>
    bool Save();
}
