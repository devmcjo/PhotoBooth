namespace MCPhoto.Core.Settings;

/// <summary>표시 모드. (PRD §6)</summary>
public enum DisplayMode
{
    Fullscreen,
    Windowed
}

/// <summary>출력 이미지 포맷. (PRD §9 #14)</summary>
public enum OutputFormat
{
    Jpg,
    Png
}

/// <summary>창모드 마지막 크기·위치. (PRD §9 #38)</summary>
public sealed class WindowBounds
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;

    /// <summary>위치가 저장되어 있으면 true(NaN이면 미저장 → 화면 중앙).</summary>
    public bool HasPosition => !double.IsNaN(Left) && !double.IsNaN(Top);
}

/// <summary>
/// 앱 인스턴스 단위 로컬 설정. INI 파일 저장·복원. (PRD §6, §9 #38, architecture §7)
/// 값 범위·옵션 제약은 <see cref="Clamp"/>에서 강제한다.
/// </summary>
public sealed class AppSettings
{
    // ── 허용 옵션 목록 (PRD §10 촬영 설정) ──
    public static readonly int[] AllowedCutCounts = { 6, 8, 10 };
    public static readonly int[] AllowedCountdownSecs = { 3, 6, 8, 10 };
    public const int MinRetentionHours = 1;
    public const int MaxRetentionHours = 72;
    public const int MinSlots = 1;
    public const int MaxSlots = 6;

    // ── 촬영 옵션 (PRD §F1) ──
    /// <summary>촬영 컷 수. 기본 6, 옵션 6/8/10(최소 6).</summary>
    public int CutCount { get; set; } = 6;

    /// <summary>컷당 카운트다운 초. 기본 6, 옵션 3/6/8/10.</summary>
    public int CountdownSec { get; set; } = 6;

    /// <summary>거울모드(좌우반전). 기본 on. WYSIWYG(프리뷰=저장 동일).</summary>
    public bool MirrorMode { get; set; } = true;

    /// <summary>플래시(촬영 직전 하양 화면). 기본 off.</summary>
    public bool FlashMode { get; set; }

    /// <summary>셔터음(촬영 순간 효과음). 기본 off. (기능#7)</summary>
    public bool ShutterSound { get; set; }

    // ── 출력 (PRD §F4) ──
    /// <summary>최종 이미지 포맷. 기본 JPG.</summary>
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Jpg;

    // ── 전송·보관 (PRD §F5) ──
    /// <summary>결과물 보관 시간. 기본 24, 범위 1~72.</summary>
    public int RetentionHours { get; set; } = 24;

    /// <summary>QR 전송(업로드+QR+다운로드 페이지) on/off. 기본 on.</summary>
    public bool EnableQrDelivery { get; set; } = true;

    /// <summary>QR 전송 시 사진(최종 합성 이미지) 포함. 기본 on. (it7 F2)</summary>
    public bool SendPhoto { get; set; } = true;

    /// <summary>QR 전송 시 타임랩스 영상 포함. 기본 on. (it7 F2)</summary>
    public bool SendTimelapse { get; set; } = true;

    // ── 필터 노출 (it8 §6 A6). 원본(None)은 항상 제공 — 필드 없음. ──
    /// <summary>흑백 필터 노출. 기본 on.</summary>
    public bool FilterGrayscale { get; set; } = true;

    /// <summary>밝기 필터 노출. 기본 on.</summary>
    public bool FilterBrightness { get; set; } = true;

    /// <summary>뷰티 필터 노출. 기본 on.</summary>
    public bool FilterBeauty { get; set; } = true;

    // ── 로컬 저장 (PRD §F4, §9 #34) ──
    /// <summary>결과물 로컬 저장 on/off. 기본 on. QR 전송과 독립.</summary>
    public bool SaveLocalCopy { get; set; } = true;

    /// <summary>로컬 저장 위치. 기본 {실행경로}\result\. 빈 문자열이면 런타임에 기본값 산출.</summary>
    public string LocalSavePath { get; set; } = string.Empty;

    // ── 표시 (PRD §9 #23) ──
    /// <summary>표시 모드. 개발 기간 기본 창모드(배포 시 전체화면으로 되돌릴 것). (it9 후속)</summary>
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Windowed;

    /// <summary>창모드 마지막 크기·위치.</summary>
    public WindowBounds WindowBounds { get; set; } = new();

    // ── 장치 (PRD §9 #31) ──
    /// <summary>사용할 웹캠 장치 인덱스. 기본 0.</summary>
    public int CameraDevice { get; set; }

    // ── 웹 연동 (firebase-contract §3.5) ──
    /// <summary>다운로드 페이지 Hosting base URL(트레일링 슬래시 제외). downloadPageUrl 조립 기준. 개발 기본값 박음(it9 후속).</summary>
    public string HostingBaseUrl { get; set; } = "https://mcphoto-955fb.web.app";

    /// <summary>
    /// Storage 버킷 이름. 빈 값이면 서비스 계정 project_id에서 유도.
    /// 신규 프로젝트는 보통 {project}.firebasestorage.app, 레거시는 {project}.appspot.com — 프로젝트별로 다르므로 명시 권장.
    /// 개발 기본값 박음(it9 후속).
    /// </summary>
    public string StorageBucket { get; set; } = "mcphoto-955fb.firebasestorage.app";

    /// <summary>
    /// 값 범위·옵션 제약을 강제(로드/저장 시 호출). 잘못된 값은 가장 가까운 허용값으로 보정.
    /// </summary>
    public void Clamp()
    {
        if (Array.IndexOf(AllowedCutCounts, CutCount) < 0)
            CutCount = ClosestFrom(CutCount, AllowedCutCounts, 6);

        if (Array.IndexOf(AllowedCountdownSecs, CountdownSec) < 0)
            CountdownSec = ClosestFrom(CountdownSec, AllowedCountdownSecs, 6);

        RetentionHours = Math.Clamp(RetentionHours, MinRetentionHours, MaxRetentionHours);

        if (WindowBounds.Width < 1280) WindowBounds.Width = 1280;
        if (WindowBounds.Height < 720) WindowBounds.Height = 720;

        if (CameraDevice < 0) CameraDevice = 0;

        HostingBaseUrl = HostingBaseUrl.TrimEnd('/');

        NormalizeQr();
    }

    /// <summary>
    /// QR 세분화 연동 정규화(it7 F2): 사진·타임랩스 둘 다 off면 QR 전송 자체 off.
    /// 저장/로드(Clamp) 시 호출. 하위 토글 값은 보존.
    /// </summary>
    public void NormalizeQr()
    {
        var (enableQr, sendPhoto, sendTimelapse) =
            QrDeliveryPolicy.Normalize(EnableQrDelivery, SendPhoto, SendTimelapse);
        EnableQrDelivery = enableQr;
        SendPhoto = sendPhoto;
        SendTimelapse = sendTimelapse;
    }

    private static int ClosestFrom(int value, int[] allowed, int fallback)
    {
        if (allowed.Length == 0) return fallback;
        var best = allowed[0];
        var bestDist = Math.Abs(value - best);
        foreach (var a in allowed)
        {
            var d = Math.Abs(value - a);
            if (d < bestDist) { best = a; bestDist = d; }
        }
        return best;
    }

    /// <summary>얕은 복제(편집 취소 대비).</summary>
    public AppSettings Clone() => new()
    {
        CutCount = CutCount,
        CountdownSec = CountdownSec,
        MirrorMode = MirrorMode,
        FlashMode = FlashMode,
        ShutterSound = ShutterSound,
        OutputFormat = OutputFormat,
        RetentionHours = RetentionHours,
        EnableQrDelivery = EnableQrDelivery,
        SendPhoto = SendPhoto,
        SendTimelapse = SendTimelapse,
        FilterGrayscale = FilterGrayscale,
        FilterBrightness = FilterBrightness,
        FilterBeauty = FilterBeauty,
        SaveLocalCopy = SaveLocalCopy,
        LocalSavePath = LocalSavePath,
        DisplayMode = DisplayMode,
        WindowBounds = new WindowBounds
        {
            Left = WindowBounds.Left,
            Top = WindowBounds.Top,
            Width = WindowBounds.Width,
            Height = WindowBounds.Height
        },
        CameraDevice = CameraDevice,
        HostingBaseUrl = HostingBaseUrl,
        StorageBucket = StorageBucket
    };
}
