/**
 * 카메라 실패 사유 분류 — 03 §6.3 · 12 C5 (순수)
 *
 * 2026-08-01 이전에는 어떤 실패든 한 문구였다("카메라를 사용할 수 없습니다. 권한과 연결을
 * 확인해 주세요."). **권한 거부와 장치 부재는 손님이 할 조치가 완전히 다르다** — 사유를 나눈다.
 *
 * 판정은 `getUserMedia` 예외의 `name`에서만 유도한다(브라우저 메시지 문자열에 의존하지 않는다).
 */

export type CameraFailureReason =
  /** NotAllowedError · SecurityError — 앱이 되돌릴 수 없다. 브라우저 사이트 설정에서만 복구된다. */
  | "permissionDenied"
  /** NotFoundError · OverconstrainedError(제약 없는 재시도 후에도 실패) — 장치가 없다. */
  | "noDevice"
  /** NotReadableError · TrackStartError — 다른 앱이 하드웨어를 점유 중이다. */
  | "inUse"
  /** `isSecureContext === false` — http로 열었다. 현장에서 실제로 발생하는 오구성이다. */
  | "insecureContext"
  /**
   * **스트림은 열렸는데 가공 프레임이 한 장도 오지 않았다**(2026-08-06 신설).
   *
   * `getUserMedia` 예외가 아니라 파이프라인 내부 정체이므로 `classifyCameraFailure`가 만들지
   * 않는다 — 카메라 서비스가 Ready 타임아웃에서 직접 확정한다.
   *
   * 이 사유가 없던 동안 프레임 루프 정체는 전부 `unknown`으로 뭉개졌고, 현장에서 "권한 문제인지
   * 브라우저 능력 문제인지" 구분할 방법이 없었다. 진단의 [가공 경로] 행과 짝을 이룬다.
   */
  | "pipelineStalled"
  /**
   * **스트림은 열렸는데 `video.play()`가 reject됐다**(2026-08-07 신설).
   *
   * `cameraService`가 `FrameSource.attach()`의 `{ok:false}`에서 확정한다 —
   * `getUserMedia` 예외가 아니므로 `classifyCameraFailure`가 만들지 않는다.
   *
   * 권한·장치와 **아무 관계가 없다.** 그런데 이 사유가 없던 동안 "권한과 연결을 확인해
   * 주세요"(`unknown`)로 뭉개져, 손님을 사이트 설정으로 헛돌게 했다. iOS 자동재생 정책이
   * 대표 원인이라 실효 있는 조치는 "화면을 한 번 누르고 다시 시도"뿐이다.
   */
  | "playbackBlocked"
  /**
   * **프레임은 도착하는데 Ready 게이트를 8초 안에 못 넘었다**(2026-08-07 신설).
   *
   * Ready 타임아웃에서 `meter.total > 0`이면 이쪽이다(`0`이면 `pipelineStalled`).
   * 정체(`pipelineStalled`)와 성격이 다르다 — 파이프라인은 돌고 있고 **느릴 뿐**이다.
   */
  | "pipelineSlow"
  /**
   * **`navigator.mediaDevices`가 없다**(2026-08-07 신설). 보안 컨텍스트인데도 `TypeError`가
   * 나는 경우 — 인앱브라우저·구형 WebView가 여기 걸린다.
   *
   * ⚠️ `insecureContext` 선판정 **뒤에서만** 도달한다(http는 그대로 `insecureContext`다).
   * 같은 브라우저에서 다시 눌러도 `mediaDevices`는 생기지 않으므로 재시도 불가다.
   */
  | "unsupportedBrowser"
  /** 그 외 전부. 기존 한 문구를 그대로 재사용한다. */
  | "unknown";

/** `STRINGS.camera.errors`의 키와 1:1이다. */
export type CameraFailureMessageKey = CameraFailureReason;

/**
 * 예외 이름 + 보안 컨텍스트 → 사유.
 *
 * ⚠️ **`insecureContext`를 가장 먼저 판정한다.** `http://`로 열면 `navigator.mediaDevices` 자체가
 *    `undefined`라 예외 `name`이 `TypeError`가 되고, 그러면 `unknown`으로 뭉개진다.
 *
 * @param errorName `err.name`. 예외가 Error가 아니면 빈 문자열을 넘긴다.
 * @param secureContext `isSecureContext`. 판정 불가면 `true`를 넘겨 이 분기를 건너뛴다.
 */
export function classifyCameraFailure(
  errorName: string,
  secureContext: boolean,
): CameraFailureReason {
  if (!secureContext) return "insecureContext";
  switch (errorName) {
    case "NotAllowedError":
    case "SecurityError":
    case "PermissionDeniedError": // 구형 Chrome 별칭
      return "permissionDenied";
    case "NotFoundError":
    case "OverconstrainedError":
    case "DevicesNotFoundError": // 구형 Chrome 별칭
      return "noDevice";
    case "NotReadableError":
    case "TrackStartError": // 구형 Chrome 별칭
      return "inUse";
    /*
     * 보안 컨텍스트인데 `TypeError` = `navigator.mediaDevices`가 없다(인앱브라우저·구형 WebView).
     * 사다리 어느 칸도 빈 `video` 제약을 보내지 않으므로(정적 검사 CAM-8) 규격상 다른 `TypeError`
     * 유발 조건이 없다. http는 위 `insecureContext` 선판정에서 이미 걸러졌다.
     */
    case "TypeError":
      return "unsupportedBrowser";
    /*
     * ⚠️ `AbortError`는 **의도적으로 매핑하지 않는다**(2026-08-07 설계 리뷰).
     *    규격상 `AbortError`는 `NotReadableError`와 분리된 잔여 범주라 "다른 앱 점유"로
     *    단정할 근거가 약하다(탭 백그라운드 전환·권한 플로우 중단도 같은 이름이다).
     *    `unknown`으로 두되 `CameraFailure.detail`이 `unknown/AbortError`로 이름을 실어
     *    나르므로 실기기 관측에는 지장이 없다. 관측으로 확인된 뒤에 매핑한다.
     */
    default:
      return "unknown";
  }
}

/**
 * 사유 → 문구 키. `Record`로 두어 사유를 하나 늘리면 **컴파일이 깨지게** 한다
 * (문구가 화면마다 갈라지지 않게 하는 유일한 장치다).
 */
const MESSAGE_KEY_BY_REASON: Readonly<Record<CameraFailureReason, CameraFailureMessageKey>> = {
  permissionDenied: "permissionDenied",
  noDevice: "noDevice",
  inUse: "inUse",
  insecureContext: "insecureContext",
  pipelineStalled: "pipelineStalled",
  playbackBlocked: "playbackBlocked",
  pipelineSlow: "pipelineSlow",
  unsupportedBrowser: "unsupportedBrowser",
  unknown: "unknown",
};

export function cameraFailureMessageKey(reason: CameraFailureReason): CameraFailureMessageKey {
  return MESSAGE_KEY_BY_REASON[reason];
}

/**
 * 이 사유에 [다시 시도] 버튼을 붙이는가.
 *
 * `permissionDenied`·`insecureContext`는 **붙이지 않는다** — 같은 조건에서 다시 눌러도 반드시
 * 실패한다(권한은 브라우저 설정, https는 배포 구성에서만 바뀐다). 손님을 헛돌게 하는 버튼이다.
 */
const RETRYABLE_BY_REASON: Readonly<Record<CameraFailureReason, boolean>> = {
  permissionDenied: false,
  insecureContext: false,
  // 같은 브라우저에서 다시 눌러도 `mediaDevices`는 생기지 않는다 — 위 둘과 같은 부류다.
  unsupportedBrowser: false,
  noDevice: true,
  inUse: true,
  // 정체는 일시적일 수 있다(첫 프레임이 늦는 기기). 다시 누르면 폴백 경로까지 재시도한다.
  pipelineStalled: true,
  // 재생 차단은 **손님의 터치 한 번**으로 풀린다(iOS 자동재생 정책) — 재시도가 실효를 갖는다.
  playbackBlocked: true,
  // 느린 것은 다음 시도에서 넘어갈 수 있다(첫 프레임 지연·일시적 부하).
  pipelineSlow: true,
  unknown: true,
};

export function isCameraRetryable(reason: CameraFailureReason): boolean {
  return RETRYABLE_BY_REASON[reason];
}

/**
 * 실패 기록 = **사유 + 상세**(2026-08-07 신설 · 설계 §2.1).
 *
 * 사유만으로는 현장에서 원인을 좁힐 수 없다 — `unknown` 하나에 성격이 다른 여러 예외가 모인다.
 * `detail`이 예외 이름(또는 경로 토큰)을 실어 나르면 화면의 짧은 코드 한 줄로 확정된다.
 */
export interface CameraFailure {
  readonly reason: CameraFailureReason;
  /** 예외 이름 또는 경로 토큰. 새니타이즈를 통과하지 못하면 `null`. */
  readonly detail: string | null;
}

/**
 * `detail`로 실어도 되는 값의 형태 — **이것이 보안 경계다**(설계 §2.1).
 *
 * 공백·`@`·한글·32자 초과를 전부 거부한다. 그래서 이메일(`@`)·토큰(길이·`/`·`=`)·게이트 키·
 * 브라우저 예외 **메시지**(공백·한글)·카메라 `label`(공백)이 이 관문을 통과할 수 없다.
 * 통과 가능한 것은 사실상 브라우저 예외 **이름**의 고정 어휘와 우리가 만든 경로 토큰뿐이다.
 *
 * ⚠️ 정적 검사 CAM-7은 `CameraFailure`의 **생성 통로**만 고정한다(객체 리터럴 우회 금지).
 *    `cameraFailure()`를 올바로 부르면서 엉뚱한 값을 넘기는 실수의 마지막 방어선은 이 패턴뿐이다.
 */
const DETAIL_PATTERN = /^[A-Za-z0-9_.:+-]{1,32}$/;

/** 통과하지 못하는 값은 조용히 `null`로 접는다 — 화면에 새어 나가는 것보다 없는 편이 낫다. */
export function sanitizeFailureDetail(rawDetail: string | null | undefined): string | null {
  if (typeof rawDetail !== "string") return null;
  return DETAIL_PATTERN.test(rawDetail) ? rawDetail : null;
}

/**
 * `CameraFailure`를 만드는 **유일한 통로**다(정적 검사 CAM-7).
 *
 * 객체 리터럴로 우회하면 `err.message`가 그대로 담겨 화면 코드로 새어 나간다.
 */
export function cameraFailure(
  reason: CameraFailureReason,
  rawDetail?: string | null,
): CameraFailure {
  return { reason, detail: sanitizeFailureDetail(rawDetail) };
}

/**
 * 예외 → 사유 + 상세.
 *
 * ⚠️ 사유 판정은 `classifyCameraFailure`에 **위임한다** — switch문을 여기 새로 만들면
 *    판정처가 둘로 갈라져 화면 문구와 진단 사유가 어긋난다.
 * ⚠️ 상세는 `err.name`이다. **`err.message`를 읽지 않는다** — 브라우저 예외 메시지에는
 *    기기명·경로가 섞일 수 있고, 그것이 화면에 노출되는 코드로 흘러가면 안 된다.
 */
export function classifyCameraFailureFrom(err: unknown, secureContext: boolean): CameraFailure {
  const name = err instanceof Error ? err.name : "";
  return cameraFailure(classifyCameraFailure(name, secureContext), name);
}

/**
 * 화면·진단·로그에 싣는 짧은 코드. 상세가 없으면 사유만.
 * → `unknown/AbortError` · `playbackBlocked/NotAllowedError` · `pipelineStalled/main-none`
 */
export function formatCameraFailureCode(failure: CameraFailure): string {
  return failure.detail === null ? failure.reason : `${failure.reason}/${failure.detail}`;
}
