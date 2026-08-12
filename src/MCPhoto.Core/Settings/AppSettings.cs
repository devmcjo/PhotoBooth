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
    // it17: 자동 sentinel(CutCountPolicy.AutoCutCount=0)은 이 배열에 넣지 않는다
    //       (넣으면 CutCount=3 오입력이 6이 아니라 0으로 보정됨 — 설계 §4.3).
    public static readonly int[] AllowedCutCounts = { 6, 8, 10 };
    public static readonly int[] AllowedCountdownSecs = { 3, 6, 8, 10 };
    public static readonly int[] AllowedRetakeLimits = { 1, 2, 3 };
    public const int MinRetentionHours = 1;
    public const int MaxRetentionHours = 72;
    public const int MinSlots = 1;
    public const int MaxSlots = 6;

    // ── 촬영 옵션 (PRD §F1) ──
    /// <summary>
    /// 촬영 컷 수. **기본 자동**(<see cref="CutCountPolicy.AutoCutCount"/>=0 → 실제 컷 수는 프레임 슬롯 수 확정 후
    /// CaptureSession.Begin이 산출), 고정 옵션 6/8/10. (it17 · 기본값 자동 전환)
    /// ⚠️ ini에 <c>CutCount=6</c>이 이미 기록된 기존 설치는 그 명시값이 우선이다(기본값 변경은 신규·키 누락 시에만 적용).
    /// </summary>
    public int CutCount { get; set; } = CutCountPolicy.AutoCutCount;

    /// <summary>컷당 카운트다운 초. 기본 6, 옵션 3/6/8/10.</summary>
    public int CountdownSec { get; set; } = 6;

    /// <summary>거울모드(좌우반전). 기본 on. WYSIWYG(프리뷰=저장 동일).</summary>
    public bool MirrorMode { get; set; } = true;

    /// <summary>플래시(촬영 직전 하양 화면). 기본 off.</summary>
    public bool FlashMode { get; set; }

    /// <summary>셔터음(촬영 순간 효과음). 기본 off. (기능#7)</summary>
    public bool ShutterSound { get; set; }

    // ── 재촬영 (it11 #13) ──
    /// <summary>재촬영 사용(상위 토글). 기본 off. off면 재촬영 UI 전부 미노출.</summary>
    public bool RetakeEnabled { get; set; }

    /// <summary>재촬영 횟수 제한(전체 재촬영 상한). 기본 1, 범위 1~3. RetakeEnabled on일 때만 의미.</summary>
    public int RetakeLimit { get; set; } = 1;

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

    /// <summary>
    /// 로컬 저장 위치. <b>빈 문자열이면 런타임 기본값</b>(<see cref="LocalSave.LocalSavePathResolver"/>).
    /// it26 §3.3: 그 기본값이 <c>{실행경로}\result</c> → <c>%ProgramData%\MCPhoto\result</c>로 바뀌었다
    /// (비승격 실행에서 설치 폴더에 못 써 조용히 미저장되던 결함). ⚠️ <b>명시값은 항상 우선</b>이며 이관이 건드리지 않는다.
    /// </summary>
    public string LocalSavePath { get; set; } = string.Empty;

    /// <summary>
    /// 유휴 경고 팝업에 [결과물 폴더 열기] 링크를 노출한다. <b>기본 off.</b> (it26 §5.2)
    /// <para>
    /// 기본값이 off인 이유: 이 링크가 붙는 팝업은 <b>손님 앞에서 무인으로</b> 뜨고, 탐색기는 잠금 키오스크에서
    /// 파일시스템 통로가 된다(세션 폴더로 열려도 상위 이동이 가능하다). 설치 직후의 부스가 모르는 채로
    /// 손님에게 파일 브라우저를 건네는 상태가 되어서는 안 된다 — fail-safe 기본값이다.
    /// </para>
    /// <para>
    /// ⚠️ 링크에는 로그인·표시 모드 게이트가 <b>없다</b>(사용자 확정: "옵션화했으니 … 지원해도돼").
    /// 따라서 이 키가 유일한 방어선이고, 게스트는 설정 화면에 PIN 없이 들어오므로 <b>게스트 편집 금지</b>가
    /// 필수다(SettingsViewModel: Load 강제 off · Save 미기록).
    /// </para>
    /// </summary>
    public bool EnableResultFolderOpen { get; set; }

    // ── 표시 (PRD §9 #23) ──
    /// <summary>표시 모드. 개발 기간 기본 창모드(배포 시 전체화면으로 되돌릴 것). (it9 후속)</summary>
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Windowed;

    /// <summary>창모드 마지막 크기·위치.</summary>
    public WindowBounds WindowBounds { get; set; } = new();

    // ── 장치 (PRD §9 #31) ──
    /// <summary>사용할 웹캠 장치 인덱스. 기본 0.</summary>
    public int CameraDevice { get; set; }

    // ── 외부 장치. 로그인 전용 옵션 자리. ──
    // it23: 외부 카메라는 placeholder에서 **실배선으로 승격**됐다(설계 §7.1). 값이 on이면 촬영 세션이
    //       DSLR 스틸 경로를 시도하고, SDK·장비가 없으면 웹캠 단독으로 강등된다(크래시·중단 없음).
    //       프린터는 여전히 placeholder다(범위 밖).
    /// <summary>
    /// 외부 카메라(DSLR) 사용. 기본 false.
    /// <para>
    /// on이어도 SDK 모듈·장비가 없으면 촬영은 웹캠 단독으로 강등된다(설계 §11 E1·E2) — 즉 이 값은
    /// "쓸 수 있으면 쓴다"는 의도 표명이지 보장이 아니다. 프리뷰·타임랩스는 항상 웹캠 전담이다.
    /// </para>
    /// </summary>
    public bool ExternalCameraEnabled { get; set; }

    /// <summary>
    /// 외부 카메라 모델 Id(<see cref="Devices.ExternalCameraModels"/> 레지스트리 키). 기본 D5300.
    /// 미지 Id는 <see cref="Clamp"/>가 기본 모델로 보정한다 — SDK 모듈 파일명은 이 Id에서 유도되므로
    /// 보정 없이 두면 "존재하지 않는 모듈"을 찾다가 영구 강등된다.
    /// </summary>
    public string ExternalCameraModel { get; set; } = Devices.ExternalCameraModels.Default.Id;

    /// <summary>
    /// 셔터 속도(카메라 표기 그대로의 표시 문자열, 예 <c>1/125</c>). <b>빈 값 = 미지정</b>(카메라 현재값 유지).
    /// <para>
    /// 인덱스가 아니라 문자열로 저장하는 이유(설계 §7.1): 카메라가 주는 이산 목록은 노출 모드·렌즈·SDK
    /// 버전에 따라 달라진다 — 인덱스는 그때 조용히 다른 값을 가리키지만, 문자열은 "지금 지원하면 적용,
    /// 아니면 건너뜀"이라는 안전한 재매칭 의미론을 갖는다.
    /// </para>
    /// </summary>
    public string ExternalShutterSpeed { get; set; } = string.Empty;

    /// <summary>조리개(예 <c>f/5.6</c>). 빈 값 = 미지정. 저장 규약은 <see cref="ExternalShutterSpeed"/>와 동일.</summary>
    public string ExternalAperture { get; set; } = string.Empty;

    /// <summary>ISO(예 <c>400</c>). 빈 값 = 미지정. 저장 규약은 <see cref="ExternalShutterSpeed"/>와 동일.</summary>
    public string ExternalIso { get; set; } = string.Empty;

    /// <summary>
    /// 사진 프린터 사용. 기본 false.
    /// <para>
    /// it24: "추후 지원" placeholder에서 <b>준비 플래그</b>로 승격됐다 — 의미는 "인쇄 기능이 도입되면 이
    /// 프린터 구성을 사용한다"이고, 이번 이터레이션의 런타임 효과는 설정 화면의 프린터 하위 패널 노출뿐이다
    /// (실제 인쇄는 명시적 비목표 — 설정 화면이 그 사실을 상시 고지한다).
    /// </para>
    /// </summary>
    public bool PhotoPrinterEnabled { get; set; }

    /// <summary>
    /// 선택된 설치 프린터 이름(Windows 프린터명 — 시스템 내 유일 식별자). <b>빈 값 = 미선택.</b>
    /// <para>
    /// it24 §7.3 P5: 열거 목록에 없더라도 <b>값을 지우지 않는다</b>. 프린터가 일시적으로 꺼져 있거나
    /// 스풀러가 멈춘 상태에서 관리자가 맞춰 둔 이름을 삭제해 버리면, 복구 뒤에도 설정이 사라져 있다.
    /// 유효성 검증은 사용 시점(인쇄 구현)의 몫이다 — 노출값 문자열의 "적용 시 검증" 철학과 동일하다.
    /// </para>
    /// </summary>
    public string PhotoPrinterName { get; set; } = string.Empty;

    // ── 웹 연동 (firebase-contract §3.5) ──
    /// <summary>다운로드 페이지 Hosting base URL(트레일링 슬래시 제외). downloadPageUrl 조립 기준. 개발 기본값 박음(it9 후속).</summary>
    public string HostingBaseUrl { get; set; } = "https://mcphoto-955fb.web.app";

    /// <summary>
    /// Storage 버킷 이름. 빈 값이면 서비스 계정 project_id에서 유도.
    /// 신규 프로젝트는 보통 {project}.firebasestorage.app, 레거시는 {project}.appspot.com — 프로젝트별로 다르므로 명시 권장.
    /// 개발 기본값 박음(it9 후속).
    /// </summary>
    public string StorageBucket { get; set; } = "mcphoto-955fb.firebasestorage.app";

    // ── 백엔드 프록시(방향 B, 설계 §8.1). it15: 레거시 직결 경로 폐지 → 백엔드 전용(플래그 없음). ──
    /// <summary>
    /// 백엔드 base URL(엔드포인트 주소, 공개값). 운영 프로젝트 기본값 내장 → 운영자 ini 입력 불요
    /// (다른 백엔드는 ini의 BackendBaseUrl로 오버라이드). 트레일링 슬래시는 Clamp가 보정(HttpClient BaseAddress 상대결합 안전).
    /// </summary>
    public string BackendBaseUrl { get; set; } = "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api";

    /// <summary>
    /// 배포별 클라이언트 API 키(반비밀, 게스트 엔드포인트 게이트). X-MCPhoto-Client 헤더로 전송.
    /// ⚠️ INI에 평문 저장 — exe/설정 유출 시 서버에서 해당 키만 폐기(설계 §8.1 트레이드오프).
    /// </summary>
    public string BackendApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Google SSO OAuth 클라이언트 ID(item1b §7.2·§8.2). **비밀 아님**(client secret은 백엔드 전용, 클라에 미보관).
    /// authorize URL 조립에 사용된다. 빈 값이면 SSO opt-out — 로그인 화면에 "Google로 로그인" 버튼을 숨긴다
    /// (잠금 키오스크 배려).
    /// 기본값에 운영 프로젝트(mcphoto-955fb) Desktop 클라이언트 ID를 내장 → 운영자 ini 입력 불요.
    /// 다른 구글 프로젝트를 쓰려면 ini의 GoogleClientId로 오버라이드(HostingBaseUrl과 동일 패턴). 공개값이라 하드코딩 무해.
    /// </summary>
    public string GoogleClientId { get; set; } = "712395684881-l66ogdns5ppcc91ojaap4ju9ta3hc6d3.apps.googleusercontent.com";

    /// <summary>
    /// 값 범위·옵션 제약을 강제(로드/저장 시 호출). 잘못된 값은 가장 가까운 허용값으로 보정.
    /// </summary>
    public void Clamp()
    {
        // it17: 자동(sentinel 0)은 최근접 보정 대상이 아니다. 가드가 없으면 ClosestFrom이 0을 6으로
        //       덮어써 저장 왕복 한 번에 "자동" 설정이 소멸한다. -1 등 다른 값은 종전대로 보정된다.
        if (!CutCountPolicy.IsAuto(CutCount) && Array.IndexOf(AllowedCutCounts, CutCount) < 0)
            CutCount = ClosestFrom(CutCount, AllowedCutCounts, 6);

        if (Array.IndexOf(AllowedCountdownSecs, CountdownSec) < 0)
            CountdownSec = ClosestFrom(CountdownSec, AllowedCountdownSecs, 6);

        if (Array.IndexOf(AllowedRetakeLimits, RetakeLimit) < 0)
            RetakeLimit = ClosestFrom(RetakeLimit, AllowedRetakeLimits, 1);

        RetentionHours = Math.Clamp(RetentionHours, MinRetentionHours, MaxRetentionHours);

        if (WindowBounds.Width < 1280) WindowBounds.Width = 1280;
        if (WindowBounds.Height < 720) WindowBounds.Height = 720;

        if (CameraDevice < 0) CameraDevice = 0;

        HostingBaseUrl = HostingBaseUrl.TrimEnd('/');

        NormalizeBackend();

        NormalizeQr();

        NormalizeExternalCamera();

        // it24: 프린터 이름은 Trim만 한다. 목록 대조는 여기서 하지 않는다 — ini에는 설치 프린터 목록이
        //       없고(열거는 스풀러 조회다), 목록 부재를 이유로 값을 지우면 관리자 설정이 파괴된다(§7.3 P5).
        PhotoPrinterName = (PhotoPrinterName ?? string.Empty).Trim();
    }

    /// <summary>
    /// 외부 카메라 설정 정규화(it23 §7.1): 모델 Id 보정 + 노출 3키 Trim.
    /// <para>
    /// 노출값은 <b>도메인 검증을 하지 않는다</b> — 허용 목록은 카메라에 연결해야 알 수 있고 ini에는 없다.
    /// 검증은 적용 시점(<c>SetExposureAsync</c>)이 담당하고, 여기서는 저장 형태만 정리한다.
    /// 빈 값은 "미지정"이라는 의미가 있으므로 기본값으로 덮지 않는다.
    /// </para>
    /// </summary>
    public void NormalizeExternalCamera()
    {
        // 미지 Id(오탈자·구버전 값)는 기본 모델로 보정. 보정하지 않으면 존재하지 않는 SDK 모듈을 찾는다.
        ExternalCameraModel = Devices.ExternalCameraModels.Resolve(ExternalCameraModel).Id;

        ExternalShutterSpeed = (ExternalShutterSpeed ?? string.Empty).Trim();
        ExternalAperture = (ExternalAperture ?? string.Empty).Trim();
        ExternalIso = (ExternalIso ?? string.Empty).Trim();
    }

    /// <summary>
    /// 백엔드 설정 정규화(it15 §4.3): 값 트림 + base URL이 슬래시로 끝나게 보정
    /// (HttpClient.BaseAddress가 상대경로를 안전히 결합하도록). base URL이 비면 보정할 것이 없어 그대로 둔다
    /// — 미구성 상태는 런타임 호출 실패로 드러나며(HttpFirebaseClient.configured=false), 다른 설정을 되돌리지 않는다.
    /// </summary>
    public void NormalizeBackend()
    {
        BackendBaseUrl = (BackendBaseUrl ?? string.Empty).Trim();
        BackendApiKey = (BackendApiKey ?? string.Empty).Trim();
        // GoogleClientId는 비밀이 아니지만 배포별 값이므로 앞뒤 공백만 정리(빈 값이면 SSO opt-out, §7.2).
        GoogleClientId = (GoogleClientId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(BackendBaseUrl))
            return;

        if (!BackendBaseUrl.EndsWith('/'))
            BackendBaseUrl += "/";
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
        RetakeEnabled = RetakeEnabled,
        RetakeLimit = RetakeLimit,
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
        // ⚠️ it26 신설 키를 여기서 빼면 설정 편집 취소 시 값이 조용히 유실된다(T-S3·T-S4와 동형).
        EnableResultFolderOpen = EnableResultFolderOpen,
        DisplayMode = DisplayMode,
        WindowBounds = new WindowBounds
        {
            Left = WindowBounds.Left,
            Top = WindowBounds.Top,
            Width = WindowBounds.Width,
            Height = WindowBounds.Height
        },
        CameraDevice = CameraDevice,
        ExternalCameraEnabled = ExternalCameraEnabled,
        // ⚠️ it23 신설 4필드를 여기서 빼면 설정 편집 취소 시 값이 조용히 유실된다(T-S3이 회귀 잠금).
        ExternalCameraModel = ExternalCameraModel,
        ExternalShutterSpeed = ExternalShutterSpeed,
        ExternalAperture = ExternalAperture,
        ExternalIso = ExternalIso,
        PhotoPrinterEnabled = PhotoPrinterEnabled,
        // ⚠️ it24 신설 1필드도 여기서 빠지면 설정 편집 취소 시 값이 조용히 유실된다(T-S4가 회귀 잠금).
        PhotoPrinterName = PhotoPrinterName,
        HostingBaseUrl = HostingBaseUrl,
        StorageBucket = StorageBucket,
        BackendBaseUrl = BackendBaseUrl,
        BackendApiKey = BackendApiKey,
        GoogleClientId = GoogleClientId
    };
}
