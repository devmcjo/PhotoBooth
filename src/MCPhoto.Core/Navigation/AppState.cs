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

    // ⚠️ 종전 Done(완료/감사 전체화면)은 폐지됐다 — 세션 완료는 홈 복귀 + 완료 토스트로 처리한다
    //    (AppShellViewModel.CompleteSession). 상태를 되살리지 말 것.

    /// <summary>설정 페이지(앱 설정만). 계정·관리자 기능은 Account로 분리. (it5 §5 C1)</summary>
    Settings,

    /// <summary>사용자 관리(power).</summary>
    UserMgmt,

    /// <summary>프레임 편집기.</summary>
    FrameEditor,

    /// <summary>계정 전용 페이지(계정 관리·관리자 도구, 진입 모드로 분기). (it5 §5 C2, it15 §6.3)</summary>
    Account
}
