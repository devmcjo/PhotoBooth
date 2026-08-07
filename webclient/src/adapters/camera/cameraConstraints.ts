/**
 * `getUserMedia` 제약 **사다리** — 04 §2.1 (순수 · 어댑터 배치)
 *
 * 순수 함수인데 `domain/`이 아니라 어댑터에 있는 이유: `MediaStreamConstraints`는 DOM 타입이고
 * `domain/`은 브라우저 표면을 참조하지 않는다는 규약이 있다(`adapters/platform/swPolicy.ts`가 같은 선례).
 *
 * ## 왜 사다리인가
 *
 * 2026-08-06까지는 제약이 **한 벌**이었고 `frameRate: { min: 15 }`가 들어 있었다. `min`은 ideal이
 * 아니라 **강제 조건**이라, 저조도에서 15fps 아래로 떨어지는 모드만 가진 안드로이드 기기가
 * `OverconstrainedError`로 튕겼다. 그때의 유일한 폴백이 `{ video: true }` 였는데 그러면
 * **해상도와 `facingMode`가 통째로 사라져** 640×480 후면 카메라가 열렸다.
 *
 * Windows는 이 문제가 없다: `cap.Set()`으로 1080p를 **요청만** 하고 장치가 거절하면 기본값으로
 * 조용히 내려간다(`OpenCvCameraService.cs:88-92`). 사다리는 그 동작을 브라우저에서 재현한다 —
 * 한 칸씩 요구를 낮추되 **의미 있는 것(장치·전후면)을 마지막까지 지킨다**.
 *
 * ⚠️ **`frameRate`에 `min`·`exact`를 넣지 마라.** 실측 fps는 진단에 그대로 표시되므로
 *    (`WC2`) 낮은 fps를 제약으로 막을 이유가 없다. 정적 검사 CAM-2가 재발을 막는다.
 */

export interface ConstraintRequest {
  /** 설정에 저장된 장치. 빈 문자열·null이면 `facingMode`로 요청한다. */
  readonly deviceId?: string | null;
  /** 전면/후면 힌트. 모바일에서만 의미가 있다. */
  readonly facing?: "user" | "environment";
}

/** 사다리 한 칸. 진단·로그가 어느 칸에서 열렸는지 말할 수 있게 이름을 붙인다. */
export interface ConstraintStep {
  /** 로그·진단 표시용 식별자. */
  readonly label: string;
  readonly constraints: MediaStreamConstraints;
}

const FULL_HD = { width: { ideal: 1920 }, height: { ideal: 1080 } } as const;
const HD = { width: { ideal: 1280 }, height: { ideal: 720 } } as const;
/** ⚠️ `ideal`만. `min`을 되살리면 저조도 안드로이드가 튕긴다(위 주석). */
const FPS = { frameRate: { ideal: 30 } } as const;

/**
 * 넓은 것 → 좁은 것 순의 시도 목록.
 *
 * 순서가 계약이다:
 *   1·2 저장된 장치를 **해상도만 낮춰** 두 번 시도한다(장치를 지키는 것이 우선).
 *   3·4 장치를 포기하고 `facingMode`로 내려간다(전후면은 지킨다).
 *   5   마지막 안전망 — 무엇이든 열린다.
 *
 * 저장된 장치가 없으면 1·2를 건너뛴다(같은 요청을 두 번 보내지 않는다).
 */
export function constraintLadder(request: ConstraintRequest): readonly ConstraintStep[] {
  const deviceId = request.deviceId ?? null;
  const hasDevice = deviceId !== null && deviceId.length > 0;
  const facing = request.facing ?? "user";
  const steps: ConstraintStep[] = [];

  if (hasDevice) {
    steps.push({
      label: "device+1080p",
      constraints: { audio: false, video: { deviceId: { exact: deviceId }, ...FULL_HD, ...FPS } },
    });
    steps.push({
      label: "device+720p",
      constraints: { audio: false, video: { deviceId: { exact: deviceId }, ...HD } },
    });
  }

  steps.push({
    label: "facing+1080p",
    constraints: { audio: false, video: { facingMode: { ideal: facing }, ...FULL_HD, ...FPS } },
  });
  steps.push({
    label: "facing",
    constraints: { audio: false, video: { facingMode: { ideal: facing } } },
  });
  steps.push({ label: "any", constraints: { audio: false, video: true } });

  return steps;
}

/**
 * 이 실패에서 사다리를 더 내려갈 의미가 있는가.
 *
 * ⚠️ **권한 거부에서는 즉시 멈춘다.** 제약을 낮춰도 결과가 같은데 시도할수록 프롬프트 관련
 *    상태가 흔들리고, 손님은 "왜 이렇게 오래 걸리나"만 겪는다.
 * ⚠️ `NotReadableError`(점유)는 **계속 내려간다** — 해상도를 낮추면 열리는 안드로이드 기기가
 *    실제로 있다(다른 앱이 고해상도 모드를 잡고 있는 경우).
 * ⚠️ `TypeError`도 **즉시 멈춘다**(2026-08-07). `navigator.mediaDevices`가 없다는 뜻이라
 *    사다리 5칸을 내려가 봐야 5번 같은 예외가 난다 — 권한 거부와 같은 이유다.
 */
export function shouldTryNextStep(errorName: string): boolean {
  return (
    errorName !== "NotAllowedError" &&
    errorName !== "SecurityError" &&
    errorName !== "PermissionDeniedError" &&
    errorName !== "TypeError"
  );
}
