namespace MCPhoto.Core.Navigation;

/// <summary>키오스크 세션 상태. (architecture §4.1, PRD §4/§5)</summary>
public enum AppState
{
    /// <summary>대기/홈.</summary>
    Home,

    /// <summary>로그인/게스트 선택.</summary>
    Login,

    /// <summary>프레임 선택.</summary>
    FrameSelect,

    /// <summary>촬영 안내.</summary>
    Guide,

    /// <summary>촬영/카운트다운.</summary>
    Capture,

    /// <summary>컷 선택.</summary>
    CutSelect,

    /// <summary>결과(미리보기+필터).</summary>
    Result,

    /// <summary>QR 팝업.</summary>
    Qr,

    /// <summary>완료/감사.</summary>
    Done,

    /// <summary>설정 페이지(앱 설정·계정·관리자 섹션). 관리자 모드를 흡수. (it2 §4)</summary>
    Settings,

    /// <summary>사용자 관리(power).</summary>
    UserMgmt,

    /// <summary>프레임 편집기.</summary>
    FrameEditor
}
