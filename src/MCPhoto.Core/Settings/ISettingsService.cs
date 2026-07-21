namespace MCPhoto.Core.Settings;

/// <summary>
/// INI 로드/저장, 창 복원. %ProgramData%\MCPhoto\MCPhoto.ini 우선. (architecture §7)
/// </summary>
public interface ISettingsService
{
    /// <summary>현재 설정(로드됨). 최초 접근 시 Load().</summary>
    AppSettings Current { get; }

    /// <summary>INI에서 로드. 파일 없으면 전 항목 기본값. 손상돼도 크래시 금지.</summary>
    AppSettings Load();

    /// <summary>
    /// 현재 설정을 INI에 즉시 flush. 성공 시 true. 쓰기 폴백 체인 실패 시 false(예외는 내부 로깅). (it3 §3)
    /// </summary>
    bool Save();
}
