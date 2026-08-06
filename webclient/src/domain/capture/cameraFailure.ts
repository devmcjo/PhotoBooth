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
  noDevice: true,
  inUse: true,
  // 정체는 일시적일 수 있다(첫 프레임이 늦는 기기). 다시 누르면 폴백 경로까지 재시도한다.
  pipelineStalled: true,
  unknown: true,
};

export function isCameraRetryable(reason: CameraFailureReason): boolean {
  return RETRYABLE_BY_REASON[reason];
}
