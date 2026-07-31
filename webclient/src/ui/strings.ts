/**
 * 사용자 문구 카탈로그 — `analysis/13 §14`와 **1:1**로 맞춘다 (01 §8)
 *
 * 문구를 컴포넌트에 흩뿌리지 않는 이유: E22 테스트가 규격 문구와 문자열 일치를 검사하고,
 * 브랜딩·번역 교체 지점이 한 곳이어야 한다.
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
    failed: "카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요.",
    testNotSaved: "저장되지 않았습니다.",
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
    nameEmpty: "이름을 입력해 주세요.",
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

  kiosk: {
    exit: "키오스크 종료",
  },
} as const;

/**
 * `{n}` 치환. 문구 카탈로그를 문자열 조립으로 오염시키지 않기 위한 단일 헬퍼.
 *
 * 문자열도 받는다 — 잠금 남은 시간처럼 **이미 서식이 끝난 값**("4분 32초")을 끼울 때 쓴다
 * (여기서 다시 조립하면 서식 규칙이 두 곳으로 갈라진다).
 */
export function formatCount(template: string, count: number | string): string {
  return template.replace("{n}", String(count));
}
