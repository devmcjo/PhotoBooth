using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;

namespace MCPhoto.App;

/// <summary>
/// 한 키오스크 세션의 공유 상태(프레임 선택 → 촬영 → 합성 → 결과) + 계정 단일 소스. 싱글턴.
/// 계정(CurrentUser)은 촬영 세션보다 상위 수명(앱 사용 동안 유지) — Login/Logout으로만 변경. (it3 §2)
/// 화면 VM·셸·상단바가 이 컨텍스트를 유일한 계정 진실 소스로 구독한다.
/// </summary>
public sealed class SessionContext
{
    /// <summary>로그인 사용자(게스트면 null). 진입점은 Login/Logout/Reset(clearUser)만 — 직접 set 금지.</summary>
    public User? CurrentUser { get; private set; }

    /// <summary>계정 변경(로그인/로그아웃) 통지. 셸·설정·상단바가 구독해 자동 갱신. (it3 §2.2)</summary>
    public event EventHandler? CurrentUserChanged;

    /// <summary>선택된 프레임(촬영 전 고정).</summary>
    public FrameTemplate? SelectedFrame { get; set; }

    /// <summary>촬영 세션(컷 버퍼·선택).</summary>
    public CaptureSession Capture { get; } = new();

    /// <summary>선택된 필터.</summary>
    public FilterKind Filter { get; set; } = FilterKind.None;

    /// <summary>합성 최종 이미지 로컬 경로.</summary>
    public string? FinalImagePath { get; set; }

    /// <summary>세션 녹화 원본 경로.</summary>
    public string? SessionVideoPath { get; set; }

    /// <summary>타임랩스 경로.</summary>
    public string? TimelapsePath { get; set; }

    /// <summary>업로드 결과(QR 대상).</summary>
    public ResultSession? Result { get; set; }

    /// <summary>세션 시작 시각(로컬 저장 폴더명).</summary>
    public DateTime SessionTime { get; set; } = DateTime.Now;

    /// <summary>세션 작업 폴더(임시 산출물).</summary>
    public string? WorkFolder { get; set; }

    /// <summary>
    /// 이 세션의 로컬 저장 폴더 절대경로. 저장을 하지 않았거나 실패했으면 null. (it26 §4.5)
    /// <para>
    /// 값의 유일한 출처는 <c>ILocalSaveService.SaveAsync</c>의 <b>반환값</b>이다 —
    /// ⚠️ 폴더명을 <c>SessionTime</c>으로 재계산하면 안 된다: 같은 분에 두 세션이 겹치면 실제 폴더에
    /// <c>-2</c> 접미가 붙어(<c>LocalSaveService.MakeUniqueFolder</c>) <b>직전 손님의 폴더</b>를 가리킨다.
    /// </para>
    /// 수명은 현재 세션뿐이다(<see cref="Reset"/>에서 null) — 다음 손님의 유휴 팝업이 이전 손님 폴더를
    /// 가리키는 경로를 만들지 않기 위한 필수 규약이다.
    /// </summary>
    public string? LocalSaveFolder { get; set; }

    /// <summary>로그인. CurrentUser 설정 + 변경 통지.</summary>
    public void Login(User user)
    {
        CurrentUser = user;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>명시적 로그아웃. CurrentUser 해제 + 변경 통지.</summary>
    public void Logout()
    {
        if (CurrentUser is null) return;
        CurrentUser = null;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 촬영 세션 데이터 폐기(새 세션 시작). 계정은 기본 보존(clearUser=false).
    /// clearUser=true는 유휴 타임아웃·세션 완료(다음 손님)에서만 — 그때만 로그아웃 통지. (it3 §2.2)
    /// </summary>
    public void Reset(bool clearUser = false)
    {
        SelectedFrame = null;
        Capture.Discard();
        Filter = FilterKind.None;
        FinalImagePath = null;
        SessionVideoPath = null;
        TimelapsePath = null;
        Result = null;
        SessionTime = DateTime.Now;
        TryCleanupWorkFolder();
        WorkFolder = null;
        // ⚠️ it26 §4.5: 누락하면 다음 손님의 유휴 팝업이 **이전 손님 폴더**를 여는 링크를 노출한다.
        LocalSaveFolder = null;

        if (clearUser)
            Logout();
    }

    private void TryCleanupWorkFolder()
    {
        if (string.IsNullOrEmpty(WorkFolder)) return;
        try
        {
            if (System.IO.Directory.Exists(WorkFolder))
                System.IO.Directory.Delete(WorkFolder, recursive: true);
        }
        catch { /* 임시 폴더 정리 실패 무시 */ }
    }
}
