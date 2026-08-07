import type {
  FrameSaveRejection,
  SaveScopeNoticeKind,
} from "@domain/frames/frameSavePolicy";
import type { FrameImageFailure } from "@adapters/frames/frameImageLoader";

/**
 * 사용자 문구 카탈로그 — `analysis/13 §14`와 **1:1**로 맞춘다 (01 §8)
 *
 * 문구를 컴포넌트에 흩뿌리지 않는 이유: E22 테스트가 규격 문구와 문자열 일치를 검사하고,
 * 브랜딩·번역 교체 지점이 한 곳이어야 한다.
 *
 * ⚠️ 위 import는 **전부 `import type`** 이다(런타임 의존 0) — 판정 유니온과 문구를 1:1로 묶어
 *    새 사유가 생겼을 때 `switch`가 컴파일 오류로 잡히게 하기 위함이다.
 */

export const STRINGS = {
  common: {
    next: "다음",
    back: "뒤로",
    close: "닫기",
    cancel: "취소",
    save: "저장",
    delete: "삭제",
    retry: "재시도",
    done: "완료",
    apply: "지금 적용",
    login: "로그인",
    logout: "로그아웃",
    guest: "게스트",
    settings: "설정",
    account: "계정",
    loading: "잠시만 기다려 주세요…",
  },

  home: {
    start: "촬영 시작",
  },

  login: {
    title: "로그인",
    google: "Google로 로그인",
    /** 리디렉트 개시 후 버튼 라벨(비활성 상태 — 중복 클릭 방지 · 03 §3). */
    redirecting: "Google로 이동하는 중…",
    /** `/oauth2callback` 처리 중 스피너 문구(조작 요소 없음 — 07 §2.5). */
    processing: "로그인 처리 중…",
    /** 5종을 **구분해서** 표시한다(07 §2.6 · 03 §3.1). 키는 `LoginMessageKey`와 1:1이다. */
    errors: {
      cancelled: "Google 로그인이 취소되었습니다.",
      rejected:
        "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.",
      notConfigured: "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
      network: "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.",
      clientNotConfigured: "로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
    },
  },

  /** 진입 PIN 게이트 — 03 §15.3 · 07 §6.4. ⚠️ PIN 값을 담는 문구는 만들지 않는다. */
  pin: {
    /** 모드 3종 제목. */
    titleVerify: "설정 진입 PIN을 입력하세요.",
    titleSetup: "새 PIN을 설정하세요. (숫자 4자리)",
    titleSetupConfirm: "새 PIN을 다시 입력하세요.",
    confirm: "확인",
    backspace: "지움",
    /** `{n}`을 입력된 자리 수로 치환한다(스크린리더 안내). */
    indicator: "입력 4자리 중 {n}자리",
    /** `{n}`을 남은 시간 문구("4분 32초")로 치환한다. */
    locked: "PIN 입력이 일시적으로 차단되었습니다. {n} 후 다시 시도해 주세요.",
    /** `{n}`을 남은 쿨다운 초로 치환한다. */
    cooldown: "{n}초 후 다시 시도할 수 있습니다.",
    /** `{n}`을 연속 실패 횟수로 치환한다 — "(2/5)". */
    failCount: "({n}/5)",
    /** 키는 `PinMessageKey`와 1:1이다. */
    messages: {
      mismatch: "PIN이 일치하지 않습니다.",
      /** ⚠️ 규격 문구다(03 §15.3) — 줄이지 않는다. */
      unavailable: "확인할 수 없습니다. 네트워크를 확인하세요.",
      alreadySet: "이미 설정된 PIN이 있습니다. 기존 PIN을 입력해 주세요.",
      invalidFormat: "PIN은 숫자 4자리입니다.",
      confirmMismatch: "두 번 입력한 PIN이 서로 다릅니다.",
    },
  },

  /** 설정 화면 — 03 §12 · analysis/41 §2. */
  settings: {
    title: "설정",
    guestBanner: "게스트로 사용 중입니다. 일부 항목은 로그인한 뒤에 변경할 수 있습니다.",
    loginRequired: "로그인 필요",
    qrLimitBadge: "한도 초과",
    qrLimitNotice: "무료 사용 한도를 초과해 QR 전송 설정을 변경할 수 없습니다.",
    sections: {
      capture: "촬영",
      device: "장치",
      output: "출력·전송",
      filters: "필터",
      advanced: "고급",
      storage: "저장소·데이터",
    },
    cutCount: "컷 수",
    cutCountAuto: "자동",
    countdown: "컷당 카운트다운(초)",
    mirrorMode: "거울모드",
    flashMode: "플래시",
    shutterSound: "셔터음",
    retakeEnabled: "재촬영 사용",
    retakeLimit: "재촬영 횟수",

    cameraDevice: "카메라 장치",
    cameraRescan: "재검색",
    cameraTest: "카메라 테스트",
    cameraFacing: "전면/후면",
    cameraFacingUser: "전면",
    cameraFacingEnvironment: "후면",
    cameraLabelHint: "권한을 허용하면 장치 이름이 표시됩니다.",
    cameraNone: "사용할 수 있는 카메라를 찾지 못했습니다.",

    outputFormat: "출력 포맷",
    enableQrDelivery: "QR 전송",
    sendPhoto: "사진 전송",
    sendTimelapse: "타임랩스 전송",
    retentionHours: "보관 시간(시간)",
    saveLocalCopy: "기기에 로컬 저장",
    localSaveFolder: "로컬 저장 폴더",
    localSaveFolderPick: "폴더 선택",
    localSaveFolderClear: "폴더 해제",
    localSaveFolderNone: "지정되지 않음",

    filterNone: "원본",
    filterNoneNote: "원본은 항상 표시됩니다.",
    filterGrayscale: "흑백",
    filterBrightness: "밝게",
    filterBeauty: "뷰티",

    hostingBaseUrl: "다운로드 페이지 Base URL",
    storageBucket: "Storage 버킷",
    serverStatus: "서버 연결 상태",
    serverRecheck: "다시 확인",
    serverChecking: "확인 중…",
    /** ⚠️ "구성됨"은 "도달 성공"이 아니다 — 두 줄로 나눠 표시한다. */
    serverConfigured: "구성됨",
    serverNotConfigured: "미구성",
    serverReachable: "도달 성공",
    serverUnreachable: "도달 실패",
    serverUnknown: "알 수 없음",
    /** ⚠️ 게이트 키는 "설정됨/미설정"만 표시한다. 값은 절대 표시하지 않는다(analysis/41 §2.5). */
    gateKeySet: "설정됨",
    gateKeyUnset: "미설정",
    gateKeyInvalid: "거부됨",
    deployedAt: "서버 배포 시각",

    storagePersist: "저장소 영속",
    storagePersistRequest: "영속 요청",
    storageUsage: "사용량",
    storageLowWarning: "저장소 여유가 10% 미만입니다. 보관된 결과물을 정리해 주세요.",
    storedResults: "보관된 결과물",
    /** `{n}`을 폴더 수로 치환한다. */
    storedResultsCount: "{n}개 세션",
    storedResultsEmpty: "보관된 결과물이 없습니다.",
    storedResultsDeleteAll: "전체 삭제",
    storedResultsConfirm: "정말 삭제할까요?",
    storedResultsDeleteFailed: "삭제하지 못했습니다.",
    /** `{n}`을 "N개를 삭제했고 M개는 실패했습니다."로 조립하지 않는다 — 아래 두 문구를 쓴다. */
    exportSettings: "설정 내보내기",
    importSettings: "설정 가져오기",
    importPreviewTitle: "적용될 항목",
    importApply: "지금 적용",
    importCancel: "가져오기 취소",
    importTooNew: "더 새 버전의 설정 파일입니다.",
    importMalformed: "설정 파일을 읽을 수 없습니다.",
    importNoChanges: "변경될 항목이 없습니다.",
    /** 게스트가 제한 항목을 조작하려 했을 때(액션 가드에서). */
    editBlocked: "이 항목은 변경할 수 없습니다.",
  },

  idle: {
    title: "계속 진행하시겠어요?",
    /** `{n}`을 남은 초로 치환한다. */
    body: "{n}초 후 처음 화면으로 돌아갑니다.",
    continue: "이어서 진행하기",
    goHome: "메인 화면으로",
  },

  fullscreen: {
    lost: "전체화면이 해제되었습니다.",
    reenter: "다시 전체화면으로",
    /** 상단바 버튼(02 §7) — 첫 터치 자동 진입을 폐지하고 만든 **유일한** 명시적 진입점이다. */
    enter: "전체화면",
  },

  save: {
    /** 웹에서는 용량 초과·프라이빗 모드가 원인이다(05 §2.2). */
    failed: "저장 위치에 쓸 수 없습니다.",
    succeeded: "저장했습니다.",
  },

  error: {
    temporary: "일시적인 오류가 발생했습니다.",
    network: "네트워크에 연결할 수 없습니다.",
    server: "서버에 문제가 발생했습니다. 잠시 후 다시 시도해 주세요.",
    notConfigured: "서버가 구성되지 않았습니다. 관리자에게 문의하세요.",
    forbidden: "권한이 없습니다.",
    /** 규격 문구는 "세션"이다(07 §4.3 · 12 C10) — "로그인이 만료"로 쓰지 않는다. */
    sessionExpired: "세션이 만료되었습니다. 다시 로그인해 주세요.",
  },

  camera: {
    notReady: "카메라를 준비하고 있습니다…",
    /** 사유를 알 수 없을 때의 기본 문구. 사유별 문구는 `errors`가 소유한다(03 §6.3). */
    failed: "카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요.",
    testNotSaved: "저장되지 않았습니다.",
    /**
     * 실패 사유별 문구 — 키는 `CameraFailureReason`과 1:1이다(03 §6.3 · 12 C5).
     * ⚠️ `permissionDenied`·`insecureContext`에는 [다시 시도]를 붙이지 않는다
     *    (`isCameraRetryable`) — 같은 조건에서 다시 눌러도 반드시 실패한다.
     */
    errors: {
      permissionDenied: "카메라 권한이 거부되었습니다. 브라우저 설정에서 허용해 주세요.",
      noDevice: "사용할 수 있는 카메라를 찾지 못했습니다. 연결을 확인해 주세요.",
      inUse: "카메라를 다른 앱이 사용 중입니다. 그 앱을 닫고 다시 시도해 주세요.",
      insecureContext: "보안 연결(https)에서만 카메라를 사용할 수 있습니다.",
      /**
       * 정체 — 권한·장치는 멀쩡하고 **화면에 그릴 단계에서 막혔다**. 손님이 할 수 있는 조치가
       * 실제로 있으므로(다시 시도 · 다른 브라우저) 그것만 말한다. 원인 진단은 진단 모달의
       * [가공 경로]·[프리뷰 경로] 행이 담당한다.
       */
      pipelineStalled:
        "카메라 영상을 표시하지 못했습니다. 다시 시도하거나 다른 브라우저에서 열어 주세요.",
      /**
       * 재생 차단 — 스트림은 열렸는데 `video.play()`가 reject됐다. 권한·장치 문제가 아니다.
       * iOS 자동재생 정책이 대표 원인이고, 그 정책에서 **실효가 있는 유일한 조치**가
       * "사용자 제스처 한 번"이라 그것만 말한다.
       */
      playbackBlocked:
        "카메라 영상을 시작하지 못했습니다. 화면을 한 번 누른 뒤 다시 시도해 주세요.",
      /**
       * 느림 — 프레임은 도착하는데 8초 안에 Ready 게이트를 못 넘었다. 정체와 달리 파이프라인은
       * 돌고 있으므로 "표시하지 못했습니다"가 아니라 "원활하지 않습니다"다.
       */
      pipelineSlow:
        "카메라 영상이 원활하지 않습니다. 다시 시도하거나 다른 브라우저에서 열어 주세요.",
      /**
       * `navigator.mediaDevices`가 없다 — 인앱브라우저·구형 WebView. 같은 브라우저에서 다시
       * 눌러도 반드시 실패하므로([다시 시도] 없음) **나가는 방법**만 말한다.
       */
      unsupportedBrowser:
        "이 브라우저에서는 카메라를 사용할 수 없습니다. Safari·Chrome 등 기본 브라우저에서 열어 주세요.",
      unknown: "카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요.",
    },
    retry: "다시 시도",
    /**
     * 실패 오버레이 하단의 오류 코드 캡션(2026-08-07 신설 · 03 §6.3).
     *
     * 진단 모달은 **로그인 전용**이고 클라이언트 로그는 기기 IndexedDB에만 쌓인다 —
     * 게스트 손님·현장 테스터가 실패 원인을 우리에게 전할 수 있는 **유일한 창구**다.
     * ⚠️ 값에는 `CameraFailure.detail`(새니타이즈 통과분)만 붙는다 — 예외 메시지가 아니다.
     */
    failureCodeLabel: "오류 코드",
    /** Guide 화면 권한 사전 요청 블록(03 §5 · 07 §3). */
    allowButton: "카메라 사용 허용",
    allowHint: "촬영 전에 카메라 사용을 허용해 주세요.",
    deniedHint: "카메라 권한이 거부되었습니다. 아래 안내를 확인해 주세요.",
    /** Home 안내(03 §2 86행) — `prompt` 상태에서만 노출한다. */
    homePromptNote: "촬영을 시작하면 카메라 사용 권한을 묻습니다.",
    homeDeniedNote: "카메라 권한이 거부되어 있습니다. 촬영 안내 화면에서 복구 방법을 확인해 주세요.",
    /** 거부 상태 복구 절차(09 §3-(4)와 같은 내용). ⚠️ `innerHTML` 금지 — JSX 텍스트 노드다. */
    recovery: {
      title: "권한 복구 방법 보기",
      chrome:
        "Chrome·Edge: 주소창 왼쪽 자물쇠(또는 ⓘ) → [사이트 설정] → 카메라 → 허용 → 페이지 새로고침",
      android: "Android Chrome: 주소창 왼쪽 자물쇠 → [권한] → 카메라 → 허용",
      ios: "iPhone·iPad Safari: 설정 앱 → Safari → 카메라 → 허용 (또는 주소창 ᴀA → 웹사이트 설정 → 카메라)",
      macos:
        "macOS Safari: Safari → 설정 → 웹사이트 → 카메라 → 이 사이트를 허용. 추가로 시스템 설정 → 개인정보 보호 → 카메라에서 Safari를 켠다",
      os: "위를 해도 안 되면 OS의 카메라 권한을 확인해 주세요(Windows: 설정 → 개인정보 → 카메라).",
    },
  },

  result: {
    /** [다음] 1단계(타임랩스 생성) 대기 문구. 실패해도 흐름은 계속된다(03 §8.1). */
    timelapseBusy: "타임랩스를 만드는 중입니다…",
  },

  upload: {
    nothingToSend: "전송할 결과물이 없습니다.",
    /** ⚠️ 카탈로그 표기(analysis/13 §14)는 말줄임표 3점 "업로드 중..."이다. */
    inProgress: "업로드 중...",
    stagePhoto: "사진 업로드 중",
    stageTimelapse: "영상 업로드 중",
    stageFinalizing: "마무리 중",
    /**
     * `{n}`을 보관 시간으로 치환한다.
     * ⚠️ 카탈로그 문구 **전체**를 쓴다(analysis/13 §14) — 짧게 줄이면 무엇이 삭제되는지 모호해진다.
     */
    retentionNotice: "업로드된 사진·영상은 {n}시간 후 자동 삭제됩니다.",
    tempUserTimeExceeded: "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.",
    tempUserCountExceeded: "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.",
    failedSaved: "전송 실패 — 사진은 기기에 저장되었습니다.",
    failedNotSaved: "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다.",
    qrRenderFailed: "QR을 만들 수 없습니다. 아래 [기기에 저장]으로 받아 주세요.",
    qrAltText: "다운로드 페이지 QR 코드",
    saveToDevice: "기기에 저장",
    saveToDevicePhoto: "사진 저장",
    saveToDeviceVideo: "영상 저장",
  },

  done: {
    /** `{n}`을 앱 이름으로 치환하지 않는다 — 브랜딩은 화면이 앞에 붙인다. */
    thanks: "이용해 주셔서 감사합니다.",
    goHome: "처음으로",
  },

  storage: {
    persistDenied: "저장소 영속이 승인되지 않았습니다. 장기간 사용하지 않으면 데이터가 삭제될 수 있습니다.",
    opfsUnavailable:
      "이 브라우저에서는 결과물을 기기에 보관할 수 없습니다. 전송(QR)만 사용할 수 있습니다.",
    folderUnsupported:
      "이 브라우저에서는 폴더 저장을 지원하지 않습니다. 결과물은 앱 내부에 보관되며 [기기에 저장]으로 내보낼 수 있습니다.",
  },

  frames: {
    /** 편집기 상시 배너(analysis/13 §6.4). */
    localOnlyBanner:
      "이 프레임 편집은 해당 기기에서만 적용됩니다. 서버의 기본 프레임은 변경되지 않으며, 다른 기기에는 반영되지 않습니다.",
    underscoreWarning: "이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다.",
    sameNameRejected: "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요.",
    /** ⚠️ 규격 문구는 "프레임 이름을…"이다(03 §11.3 ⑤). Step 15에서 정정했다. */
    nameEmpty: "프레임 이름을 입력해 주세요.",
    nameTooLong: "이름은 100자까지 입력할 수 있습니다.",
    nameInvalidChars: "이름에 사용할 수 없는 문자가 있습니다.",
    nameUnderscoreRejected: "프레임 이름에 '_'는 사용할 수 없습니다.",
    limitReached: "프레임은 최대 10개까지 저장할 수 있습니다.",

    /** 프레임 준비 대기 오버레이(03 §4.1). ⚠️ 새 로딩을 시작하지 않는다 — 현재 대기만 접는다. */
    skipWait: "기다리지 않고 시작",
    /** 프레임 준비 실패 카드의 두 번째 탈출구(03 §4.1). */
    goHome: "메인으로",
    /** CORS·404로 이미지를 못 가져온 서버 프레임 카드의 캡션(06 §6). 카드는 보이되 선택 불가다. */
    unavailableImage: "이 프레임을 불러올 수 없습니다.",

    /** 삭제 확인 오버레이(03 §15.5). 셸 모달이 아니라 **화면 로컬 오버레이**다. */
    deleteConfirmTitle: "정말 삭제할까요?",
    /** `{n}`을 프레임 이름으로 치환한다. */
    deleteConfirmBody: "'{n}' 프레임을 이 기기에서 삭제합니다.",
    /** power에게만 노출. 기본 off이며 열 때마다 리셋된다. */
    deleteAlsoServer: "서버에서도 제거",

    /**
     * 삭제 결과 4문구(03 §15.5 — 문자열 일치). **성공 오인 금지**가 이 문구들의 존재 이유다.
     * `{n}`은 각각 프레임 이름 / 실패 사유다.
     */
    deleteLocalFailed: "로컬 프레임 파일을 삭제하지 못했습니다(사용 중일 수 있음).",
    deleteServerOk: "서버에서도 삭제되었습니다.",
    deleteServerNotFound: "로컬은 삭제했지만 서버에서 '{n}' 문서를 찾지 못했습니다.",
    deleteServerFailed: "서버 삭제 실패: {n}",
    /** 서버 결과가 이미 있을 때 로컬 실패를 **덧붙인다**(두 사실을 함께 보고 — Windows와 동형). */
    deleteLocalFailedSuffix: " (단, 로컬 파일 삭제 실패)",
  },

  /** 프레임 편집기 — 03 §11 · §15.4 · §15.7 (Step 15 신설). */
  frameEditor: {
    titleNew: "프레임 만들기",
    titleEdit: "프레임 편집",

    loadImage: "이미지 불러오기",
    /** 생성 모드에서만 노출한다(03 §11.5). */
    pickExisting: "기존 프레임에서 불러오기",
    slotCount: "슬롯 개수",
    slotAspect: "슬롯 종횡비",
    slotScale: "슬롯 크기",
    nameLabel: "프레임 이름",
    namePlaceholder: "예: 여름 6컷",
    /** `{n}`을 슬롯 번호·좌표로 치환한다(스크린리더). */
    slotAriaLabel: "슬롯 {n}",
    noImage: "이미지를 불러와 주세요.",

    /** 저장 전 검증 차단 문구(03 §11.3 — 문자열 일치). 나머지 4종은 `frames.*`에 있다. */
    rejectNotLoggedIn: "로그인이 필요합니다.",
    rejectNoPermission: "프레임을 만들 권한이 없습니다.",
    rejectInvalidSlots: "슬롯이 겹치거나 프레임을 벗어났습니다.",
    rejectNameConflict: "이미 같은 이름의 프레임이 있습니다. 다른 이름을 입력해 주세요.",

    /** 이미지 로드 실패(`FrameImageFailure`와 1:1). 앞 3종은 analysis/13 §14 카탈로그와 문자열 일치다. */
    imageUnsupported: "PNG/JPG/JPEG만 지원합니다.",
    imageTooLarge: "이미지가 10MB를 초과합니다.",
    imageDecodeFailed: "이미지를 읽을 수 없습니다.",
    /** 웹 전용 실패(재인코딩 경로 부재 — A15-1). 카탈로그에 대응 항목이 없다. */
    imageEncodeFailed: "이 브라우저에서는 이미지를 변환할 수 없습니다.",
    editImageMissing: "이 프레임의 이미지를 불러올 수 없습니다.",
    pickedImageMissing: "선택한 프레임의 이미지를 불러올 수 없습니다.",

    /** 기존 프레임 불러오기 오버레이(03 §15.4). */
    pickerTitle: "기존 프레임에서 불러오기",
    pickerApply: "불러오기",
    pickerEmpty: "불러올 수 있는 프레임이 없습니다.",
    pickerFailed: "프레임 목록을 불러오지 못했습니다.",
    /** `{n}`을 원본 이름으로 치환한다. 이미지를 직접 다시 불러오면 **비운다**. */
    pickedSourceNotice: "'{n}'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요.",

    /** 서버 등록 확인 오버레이(03 §15.7). 체크박스는 **기본 on**이고 열 때마다 리셋된다. */
    registerTitle: "서버에도 등록할까요?",
    registerCheckbox: "서버에도 등록",
    /** ⚠️ 체크 상태와 무관한 **고정 문구**다(두 결과를 모두 명시 — 03 §11.4). */
    registerCaption:
      "체크하면 서버에 공용 기본 프레임으로 등록되어 다른 기기에서도 내려받습니다. 체크하지 않으면 이 기기에만 저장됩니다.",
    /** `{n}`을 실패 사유로 치환한다. 원자성 안내가 뒤에 붙는다(03 §11.4). */
    registerFailed:
      "서버 등록 실패: {n} 이 기기에만 저장하려면 '서버에도 등록'을 해제하고 다시 저장해 주세요.",
    saving: "저장 중...",
    /** analysis/13 §14 "프레임 저장 실패"와 문자열 일치. */
    saveLocalFailed: "저장에 실패했습니다.",

    /** 저장 스코프 캡션 4종(`SaveScopeNoticeKind`와 1:1). `{n}`은 프레임 이름이다. */
    scopePublicNew:
      "저장 시 '{n}'을(를) 이 기기의 공용 목록에 만듭니다. 서버 등록 여부는 저장할 때 선택합니다.",
    scopePublicFork: "원본은 그대로 두고 '{n}'(으)로 이 기기의 공용 목록에 저장됩니다.",
    scopeOverwrite: "'{n}'을(를) 덮어씁니다.",
    scopePersonal: "'{n}'을(를) 내 프레임으로 저장합니다.",

    /** 권한 게이트(렌더 가드). */
    noPermission: "프레임을 만들 권한이 없습니다.",
    editNotAllowed: "이 프레임은 편집할 수 없어 새 프레임으로 시작합니다.",
    backToFrameSelect: "프레임 선택으로",
  },

  /**
   * 계정 화면 — 03 §13.
   * ⚠️ 로그인 방식 값("Google SSO"/"알 수 없음")은 **도메인** `authMethodLabel`이 소유한다
   *    (`roleLabel`과 같은 자리 — 카탈로그 중복을 만들지 않는다. 설계 §3.1).
   */
  account: {
    title: "계정",
    tabInfo: "내 정보",
    tabAdmin: "관리자 도구",

    id: "계정 id",
    email: "이메일",
    authMethod: "로그인 방식",
    role: "역할",
    createdAt: "가입일",
    /** 값이 없거나 파싱 실패. */
    unknown: "알 수 없음",
    /** 이메일 미보유 표시. */
    none: "—",

    changePin: "PIN 변경",
    pinCurrent: "현재 PIN을 입력하세요.",
    pinNew: "새 PIN을 입력하세요. (숫자 4자리)",
    pinConfirm: "새 PIN을 다시 입력하세요.",
    pinChanged: "PIN을 변경했습니다.",
    /** ⚠️ 규격 문구다(analysis/13 §14) — 줄이지 않는다. */
    pinCurrentWrong: "현재 PIN이 올바르지 않습니다.",

    adminTitle: "관리자 도구",
    openUserMgmt: "사용자 관리",
    globalLimits: "전역 무료 한도",
    qrHours: "무료 사용 시간(시간)",
    qrCount: "무료 사용 횟수",
    limitsSaved: "한도를 저장했습니다.",
    limitsRange: "한도 값이 허용 범위를 벗어났습니다.",
    limitsNoChange: "변경된 항목이 없습니다.",
    limitsLoadFailed: "현재 한도를 불러올 수 없습니다.",
    limitsSaveFailed: "한도를 저장하지 못했습니다.",
    logoutDone: "로그아웃했습니다.",
  },

  /** 사용자 관리 — 03 §14. */
  userMgmt: {
    title: "사용자 관리",
    /** `{n}`을 인원 수로 치환한다. */
    total: "총 {n}명",
    colId: "계정 id",
    colEmail: "이메일",
    colRole: "역할",
    colCreatedAt: "가입일",
    colActions: "작업",
    resetPin: "PIN",
    /** ⚠️ 규격 문구다(analysis/13 §10.3) — 실패를 빈 목록으로 위장하지 않는다. */
    loadFailed: "사용자 목록을 불러올 수 없습니다.",
    /** `{n}`을 계정 id로 치환한다. */
    deleteConfirm: "'{n}' 계정을 삭제할까요? 소유 프레임도 함께 삭제됩니다.",
    /** `{n}`을 계정 id로 치환한다. cascade를 명시한다(03 §14). */
    deleted: "{n} 삭제됨(소유 프레임 포함).",
    roleChanged: "역할을 변경했습니다.",
    roleLabel: "역할 변경",
    pinResetTitle: "PIN 재설정",
    pinResetDone: "PIN을 재설정했습니다.",
    notFound: "대상 계정을 찾을 수 없습니다.",
    empty: "표시할 계정이 없습니다.",
    back: "뒤로",
  },

  /** 진단·상태 모달 — 03 §15.2. ⚠️ 게이트 키 **값**을 담는 문구는 만들지 않는다. */
  diagnostics: {
    title: "진단·상태",
    open: "진단·상태",
    recheck: "다시 확인",
    sections: {
      camera: "카메라",
      encoder: "비디오 인코더",
      server: "서버 연결",
      logStorage: "로그·저장소",
      contact: "개발자 문의",
      app: "앱",
    },

    cameraCount: "장치 수",
    cameraList: "장치 목록",
    cameraState: "상태",
    cameraPermission: "권한",
    cameraResolution: "획득 해상도",
    processedSize: "가공 해상도",
    cameraFps: "fps",
    cameraFailureReason: "실패 사유",
    /**
     * 가공·프리뷰 경로 2행 — 04 §2.3.1이 요구한 **"저성능 모드 표시"** 의 실물이다(2026-08-06).
     *
     * 이 두 행이 없던 동안, `OffscreenCanvas`가 없는 기기에서 카메라가 안 열리는지 프리뷰만
     * 안 보이는지 현장에서 구분할 수 없었다. 값 문자열만으로 판독되게 쓴다(01 §8).
     */
    pipelineMode: "가공 경로",
    pipelineModeWorker: "Worker",
    pipelineModeMain: "메인 스레드(저성능)",
    previewMode: "프리뷰 경로",
    previewModeTransferred: "캔버스 이관(zero-copy)",
    previewModeBitmap: "비트맵 전송(폴백)",
    previewModeDirect: "직접 렌더",
    previewModeNone: "미연결",
    /**
     * 프레임 전달 경로 — 04 §2.3.2(2026-08-07 신설).
     *
     * ⚠️ `frameTransferBitmap`(애초에 `VideoFrame`이 없음)과 `frameTransferDemoted`(있었는데
     *    런타임에 깨져 강등됨)는 **성격이 다르다.** 전자는 정상 폴백, 후자는 브라우저 결함
     *    신호이자 성능 예산 재측정 대상이다 — 합치면 그 구분이 현장에서 사라진다.
     */
    frameTransfer: "프레임 전달",
    frameTransferVideoFrame: "VideoFrame(zero-copy)",
    frameTransferBitmap: "ImageBitmap(폴백)",
    frameTransferDemoted: "ImageBitmap(강등)",
    /** 실제로 열린 제약 사다리 칸(04 §2.1). 요청 해상도가 왜 낮은지 설명해 준다. */
    cameraConstraintStep: "적용된 제약",

    encoderPath: "경로",
    encoderCodec: "코덱",
    encoderReason: "판정 사유",
    encoderCandidates: "후보",
    encoderNotProbed: "아직 판정 전(촬영 후 표시)",
    encoderNone: "미지원",

    bucket: "Storage 버킷",
    currentAccount: "현재 계정",
    guest: "게스트",
    /**
     * 서버 OAuth 구성 신호(2026-08-01 후속). 게이트 키의 "설정됨/미설정"과 **같은 수준**이다 —
     * ⚠️ client_id 값·길이·앞자리를 문구에 담지 않는다. 열거값과 개수뿐이다.
     */
    oauthWeb: "웹 OAuth 구성",
    oauthConfigured: "설정됨",
    /** 값은 있으나 `….apps.googleusercontent.com` 형식이 아니다 — 플레이스홀더 미치환이 여기 걸린다. */
    oauthMalformed: "형식 오류(값 미치환 의심)",
    oauthUnset: "미설정",
    /** desktop client_id를 그대로 넣은 오구성. 유형이 다르면 OAuth 클라이언트를 공유할 수 없다. */
    oauthShared: "desktop과 같은 값",
    oauthAllowlist: "redirect 허용목록",
    /** `{n}`을 항목 수로 치환한다. 주소 자체는 표시하지 않는다. */
    oauthAllowlistValue: "{n}개",
    /** 서버 구성 오류를 현장에서 판별하는 유일한 화면 흔적(07 §2.5). 메모리 전용이다. */
    lastLoginFailure: "마지막 로그인 실패",

    logCount: "로그 건수",
    /** `{n}`을 건수로 치환한다. */
    logCountValue: "{n}건",
    logRange: "로그 기간",
    exportLogs: "로그 내보내기",
    exportLogsDone: "로그를 내보냈습니다.",
    exportLogsFailed: "로그를 내보내지 못했습니다.",
    persistState: "저장소 영속",
    storageUsage: "사용량",
    sessionLeftovers: "세션 잔재",
    storedResults: "보관 결과물",
    frameCacheUsage: "프레임 캐시",

    developer: "개발자",
    developerEmail: "devmcjo@gmail.com",
    copy: "복사",
    copied: "복사했습니다.",
    copyFailed: "복사할 수 없습니다. 주소를 길게 눌러 복사해 주세요.",

    version: "Version",
    buildDate: "Build Date",
    webDeployDate: "Web Deploy Date",
    serviceWorker: "Service Worker",
    installed: "PWA 설치",
  },

  /** PWA·Service Worker — 01 §6. */
  pwa: {
    swActive: "최신 상태",
    swWaiting: "업데이트 대기 중",
    swRegistering: "등록 중…",
    swUnsupported: "미지원",
    swDisabled: "개발 모드(등록 안 함)",
    swFailed: "등록 실패",
    applyNow: "지금 적용",
    /** ⚠️ 상시 캡션이다 — 누르기 전에 결과를 알려야 한다. */
    applyCaption: "적용하면 앱이 새로 시작되고 로그인이 해제됩니다.",
    applyBlocked: "촬영이 끝난 뒤 적용할 수 있습니다.",
    checkUpdate: "앱 업데이트 확인",
    upToDate: "최신 버전입니다.",
    updateFound: "새 버전을 찾았습니다.",
    installed: "설치됨",
    notInstalled: "브라우저에서 실행 중",
  },

  /** 프레임 내보내기 / 가져오기 — 05 §2.5·§4.6·§7. */
  transfer: {
    exportFrames: "프레임 내보내기",
    importFrames: "프레임 가져오기",
    /** `{n}`을 개수로 치환한다. */
    exportedFrames: "{n}개를 내보냈습니다.",
    /** `{n}`을 성공 개수로 치환한다. 부분 실패를 숨기지 않는다(M4). */
    exportedPartial: "{n}개를 내보냈고 일부는 이미지를 읽지 못했습니다.",
    exportFailed: "프레임을 내보내지 못했습니다.",
    exportEmpty: "내보낼 프레임이 없습니다.",
    importPreviewTitle: "가져올 프레임",
    importRenamed: "이름 변경됨",
    importApply: "지금 적용",
    importCancel: "가져오기 취소",
    /** `{n}`을 개수로 치환한다. */
    importDone: "{n}개를 가져왔습니다.",
    /** `{n}`을 실패 개수로 치환한다. */
    importPartial: "{n}개는 저장하지 못했습니다.",
    malformedZip: "zip 파일을 읽을 수 없습니다.",
    noEntries: "가져올 프레임이 없습니다.",
    compressionUnsupported:
      "압축된 zip은 이 브라우저에서 읽을 수 없습니다. 압축 없이 저장한 zip을 사용해 주세요.",
    noWritePermission: "프레임을 가져올 권한이 없습니다.",
    notLoggedIn: "로그인이 필요합니다.",
  },

  kiosk: {
    exit: "키오스크 종료",
    exitConfirm: "키오스크를 종료할까요? 로그아웃되고 처음 화면으로 돌아갑니다.",
    /** ⚠️ 탭은 스크립트로 닫을 수 없다 — 마지막은 안내다(WD5). */
    exitNotice: "키오스크를 종료했습니다. 브라우저(또는 앱)를 직접 닫아 주세요.",
  },
} as const;

/**
 * 저장 전 검증 사유 → 문구(03 §11.3 표와 1:1).
 * 도메인은 문자열을 갖지 않으므로 매핑은 여기 한 곳이다.
 */
export function frameSaveRejectionMessage(reason: FrameSaveRejection): string {
  switch (reason) {
    case "not-logged-in":
      return STRINGS.frameEditor.rejectNotLoggedIn;
    case "no-write-permission":
      return STRINGS.frameEditor.rejectNoPermission;
    case "invalid-slots":
      return STRINGS.frameEditor.rejectInvalidSlots;
    case "same-as-source":
      return STRINGS.frames.sameNameRejected;
    case "name-empty":
      return STRINGS.frames.nameEmpty;
    case "name-invalid-chars":
      return STRINGS.frames.nameInvalidChars;
    case "name-conflict":
      return STRINGS.frameEditor.rejectNameConflict;
    case "limit-reached":
      return STRINGS.frames.limitReached;
    default:
      return STRINGS.error.temporary;
  }
}

/** 저장 스코프 캡션. 문구 **종류**는 도메인이 고르고 조립만 여기서 한다. */
export function frameSaveScopeNotice(kind: SaveScopeNoticeKind, name: string): string {
  const label = name.trim().length === 0 ? STRINGS.frameEditor.namePlaceholder : name;
  switch (kind) {
    case "public-new":
      return formatCount(STRINGS.frameEditor.scopePublicNew, label);
    case "public-fork":
      return formatCount(STRINGS.frameEditor.scopePublicFork, label);
    case "overwrite":
      return formatCount(STRINGS.frameEditor.scopeOverwrite, label);
    case "personal":
      return formatCount(STRINGS.frameEditor.scopePersonal, label);
    default:
      return "";
  }
}

/** 이미지 로드 실패 → 문구(`FrameImageFailure`와 1:1). 조용한 실패를 만들지 않는다. */
export function frameImageFailureMessage(failure: FrameImageFailure): string {
  switch (failure) {
    case "unsupported-type":
      return STRINGS.frameEditor.imageUnsupported;
    case "too-large":
      return STRINGS.frameEditor.imageTooLarge;
    case "decode-failed":
      return STRINGS.frameEditor.imageDecodeFailed;
    case "encode-failed":
      return STRINGS.frameEditor.imageEncodeFailed;
    case "fetch-failed":
      return STRINGS.frameEditor.pickedImageMissing;
    default:
      return STRINGS.error.temporary;
  }
}

/**
 * `{n}` 치환. 문구 카탈로그를 문자열 조립으로 오염시키지 않기 위한 단일 헬퍼.
 *
 * 문자열도 받는다 — 잠금 남은 시간처럼 **이미 서식이 끝난 값**("4분 32초")을 끼울 때 쓴다
 * (여기서 다시 조립하면 서식 규칙이 두 곳으로 갈라진다).
 */
export function formatCount(template: string, count: number | string): string {
  return template.replace("{n}", String(count));
}
