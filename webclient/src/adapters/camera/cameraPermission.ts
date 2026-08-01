import {
  classifyCameraFailure,
  type CameraFailureReason,
} from "@domain/capture/cameraFailure";
import { logger } from "@adapters/storage/logStore";
import { getCameraService } from "./cameraService";

/**
 * 카메라 권한 조회·구독·사전 요청 — 07 §3 · 12 C5
 *
 * ⚠️ **예외를 전파하지 않는다**(15 §2). 실패는 `null` 또는 판별 유니온이다.
 * ⚠️ 조회(`readCameraPermission`)는 **프롬프트를 띄우지 않는다.** 프롬프트를 띄우는 것은
 *    `requestCameraPermission` 하나뿐이고, **사용자 제스처 안에서만** 불러야 한다.
 *
 * 정적 불변식 **CAM-1**: `getUserMedia(`를 부르는 파일은 `cameraService.ts`(실촬영 · 하드웨어
 * 단일 소유)와 이 파일(권한 프라이밍 · 즉시 stop) 정확히 2개다.
 */

/** 카메라 권한 3상태 + 알 수 없음. `navigator.permissions`가 없거나 throw하면 `null`이다(A4). */
export type CameraPermission = "granted" | "denied" | "prompt" | null;

export type CameraPermissionOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: CameraFailureReason };

/** `navigator.permissions`의 우리가 쓰는 최소 표면(타입 lib을 믿지 않는다). */
interface PermissionsLike {
  query?: (descriptor: { name: string }) => Promise<PermissionStatusLike>;
}

interface PermissionStatusLike {
  state: string;
  addEventListener?: (type: string, listener: () => void) => void;
  removeEventListener?: (type: string, listener: () => void) => void;
}

function permissions(): PermissionsLike | undefined {
  if (typeof navigator === "undefined") return undefined;
  return (navigator as { permissions?: PermissionsLike }).permissions;
}

function narrow(state: string): CameraPermission {
  return state === "granted" || state === "denied" || state === "prompt" ? state : null;
}

/**
 * 권한 상태 조회. **프롬프트를 띄우지 않는다.**
 *
 * ⚠️ **타입을 믿지 않고 런타임 감지**하고 throw를 삼킨다 — Firefox는 `{name:"camera"}`를
 *    모르는 이름으로 보고 예외를 던지고, Safari는 아예 미지원이다(A4). 둘 다 `null`이다.
 */
export async function readCameraPermission(): Promise<CameraPermission> {
  try {
    const api = permissions();
    if (typeof api?.query !== "function") return null;
    const status = await api.query({ name: "camera" });
    return narrow(status.state);
  } catch {
    return null;
  }
}

/**
 * 권한 변경 구독. 반환값은 **해제 함수**다(미지원이면 no-op 해제자 — 호출측이 분기하지 않게).
 *
 * ⚠️ `PermissionStatus.onchange` 대신 `addEventListener`를 쓰고 반드시 `removeEventListener`로
 *    해제한다. 구독이 남으면 언마운트 후 setState가 일어난다.
 */
export function watchCameraPermission(fn: (permission: CameraPermission) => void): () => void {
  const api = permissions();
  if (typeof api?.query !== "function") return () => undefined;

  let status: PermissionStatusLike | null = null;
  let listener: (() => void) | null = null;
  let cancelled = false;

  const detach = (): void => {
    if (status !== null && listener !== null && typeof status.removeEventListener === "function") {
      status.removeEventListener("change", listener);
    }
    status = null;
    listener = null;
  };

  void api
    .query({ name: "camera" })
    .then((result) => {
      // 구독이 붙기 전에 해제됐다면 아무것도 하지 않는다(비동기 경합).
      if (cancelled) return;
      if (typeof result.addEventListener !== "function") return;
      status = result;
      listener = () => fn(narrow(result.state));
      result.addEventListener("change", listener);
    })
    .catch(() => undefined); // 미지원·throw는 구독 없음과 같다.

  return () => {
    cancelled = true;
    detach();
  };
}

/**
 * 권한 프롬프트를 띄운다. **사용자 제스처 핸들러 안에서만** 부른다.
 *
 * ⚠️ 카메라가 이미 열려 있으면(`state() !== "Idle"`) **스트림을 열지 않는다** — 하드웨어 단일
 *    소유(01 §2.1)를 프라이밍이 우회하면 실촬영 스트림과 충돌한다. 이미 열려 있다는 것은 곧
 *    허용됐다는 뜻이므로 `ok: true`다.
 * ⚠️ 획득 즉시 **무조건** 모든 트랙을 `stop()` 한다. 빠뜨리면 Guide 화면에 머무는 내내
 *    카메라 LED가 켜진 채 남는다(`cameraService.teardown()`이 같은 이유로 같은 줄을 갖는다).
 * ⚠️ `{ audio: false, video: true }` 고정 — 해상도 제약을 걸면 프라이밍 단계에서
 *    `OverconstrainedError`가 날 수 있고 그것은 권한 문제가 아니다. 오디오를 요구하면 권한
 *    범위가 넓어져 손님이 거부할 확률이 올라간다.
 */
export async function requestCameraPermission(): Promise<CameraPermissionOutcome> {
  if (getCameraService().state() !== "Idle") return { ok: true };

  if (typeof navigator === "undefined" || typeof navigator.mediaDevices?.getUserMedia !== "function") {
    // http로 열었거나 구형 브라우저다. `isSecureContext`가 사유를 갈라 준다.
    const reason = classifyCameraFailure("TypeError", isSecureContextSafe());
    logger.warn("카메라 권한 사전 요청 불가", { failureReason: reason });
    return { ok: false, reason };
  }

  let stream: MediaStream | null = null;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: true });
    return { ok: true };
  } catch (err) {
    const reason = classifyCameraFailure(
      err instanceof Error ? err.name : "",
      isSecureContextSafe(),
    );
    logger.warn("카메라 권한 사전 요청 거부", { failureReason: reason });
    return { ok: false, reason };
  } finally {
    // ★ 즉시 · 무조건 정지. 성공 경로에서도 스트림을 들고 있지 않는다.
    stream?.getTracks().forEach((track) => track.stop());
  }
}

/** `isSecureContext` 미지원 환경(구형 WebView·node 테스트)에서는 `true`로 본다. */
function isSecureContextSafe(): boolean {
  return typeof isSecureContext === "boolean" ? isSecureContext : true;
}
