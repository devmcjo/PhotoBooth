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
  },

  kiosk: {
    exit: "키오스크 종료",
  },
} as const;

/** `{n}` 치환. 문구 카탈로그를 문자열 조립으로 오염시키지 않기 위한 단일 헬퍼. */
export function formatCount(template: string, count: number): string {
  return template.replace("{n}", String(count));
}
