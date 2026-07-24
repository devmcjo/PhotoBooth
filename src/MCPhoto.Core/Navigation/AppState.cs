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

    /// <summary>설정 페이지(앱 설정만). 계정·관리자 기능은 Account로 분리. (it5 §5 C1)</summary>
    Settings,

    /// <summary>사용자 관리(power).</summary>
    UserMgmt,

    /// <summary>프레임 편집기.</summary>
    FrameEditor,

    /// <summary>계정 전용 페이지(비번 변경·계정 생성·앱 종료 등, 진입 모드로 분기). (it5 §5 C2)</summary>
    Account,

    /// <summary>비밀번호 찾기(비로그인 재설정). 로그인 화면에서 진입, 백엔드 모드 전용. (item1a §9.4)</summary>
    PasswordReset
}
