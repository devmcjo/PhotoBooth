import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import { isSessionActive } from "@domain/navigation/stateMachine";
import { logger } from "@adapters/storage/logStore";
import { currentScreen } from "./shellStore";

/**
 * Service Worker 등록 · 갱신 흐름 — 01 §6 (설계 §8.5)
 *
 * ⚠️ **`location.reload()`가 여기 있는 것은 `main.tsx`의 "리로드 금지" 규약과 충돌하지 않는다.**
 *    그 규약은 `main.tsx` 파일의 **암묵적** 리다이렉트를 금지한 것이고(리로드하면 메모리 전용
 *    JWT가 사라진다 — M2), 여기는 **사용자가 [지금 적용]을 누른** 명시 조작이다. 그래서 버튼 옆에
 *    "적용하면 앱이 새로 시작되고 로그인이 해제됩니다"를 상시 캡션으로 둔다.
 * ⚠️ **촬영 중에는 적용하지 않는다.** 렌더 가드(버튼 미노출) + 액션 첫 줄 가드 2중이다.
 * ⚠️ `controllerchange`에 **1회 가드**를 둔다 — 없으면 리로드 루프가 난다.
 * ⚠️ dev에서는 등록하지 않는다. dev 서버에는 `/sw.js`가 없고, 남은 SW가 dev 자산을 가로채면
 *    원인을 찾을 수 없는 결함이 된다.
 */

export type SwStatus =
  | "unsupported"
  | "disabled"
  | "registering"
  | "active"
  | "waiting"
  | "failed";

export interface SwState {
  readonly status: SwStatus;
  /** 캐시된 셸의 빌드 id(`precache-manifest.json`). 알 수 없으면 null. */
  readonly buildId: string | null;
}

const INITIAL: SwState = { status: "registering", buildId: null };

export const swStateStore = createStore<SwState>()(() => INITIAL);

export function useSwState(): SwState {
  return useStore(swStateStore, (s) => s);
}

/** [지금 적용]이 보내는 메시지 타입. `src/sw.ts`의 상수와 문자열이 같아야 한다. */
export const APPLY_UPDATE_MESSAGE = "MCPHOTO_APPLY_UPDATE";

// ──────────────────────────────────────────────────────────────────────────
// 테스트가 주입할 수 있는 최소 표면(실제 `ServiceWorkerContainer`의 부분집합)
// ──────────────────────────────────────────────────────────────────────────

export interface ServiceWorkerLike {
  readonly state: string;
  postMessage(message: unknown): void;
  addEventListener(type: "statechange", listener: () => void): void;
}

export interface ServiceWorkerRegistrationLike {
  readonly installing: ServiceWorkerLike | null;
  readonly waiting: ServiceWorkerLike | null;
  readonly active: ServiceWorkerLike | null;
  addEventListener(type: "updatefound", listener: () => void): void;
  update(): Promise<void>;
}

export interface ServiceWorkerContainerLike {
  readonly controller: ServiceWorkerLike | null;
  register(url: string): Promise<ServiceWorkerRegistrationLike>;
  addEventListener(type: "controllerchange", listener: () => void): void;
}

export interface SwInstallDeps {
  /** 기본 전역 `navigator.serviceWorker`. `null`이면 미지원. */
  readonly container?: ServiceWorkerContainerLike | null;
  /** 기본 `import.meta.env.PROD`. false면 등록하지 않고 `disabled`. */
  readonly enabled?: boolean;
  /** 기본 `location.reload()`. */
  readonly reload?: () => void;
  /** 촬영 중인가. 기본 `isSessionActive(currentScreen())`. */
  readonly isBusy?: () => boolean;
  /** 셸 빌드 id 조회. 기본 `/precache-manifest.json` 1회 fetch(실패는 null). */
  readonly readBuildId?: () => Promise<string | null>;
}

let installed = false;
let registration: ServiceWorkerRegistrationLike | null = null;
/** [지금 적용]을 눌렀는가. 첫 설치의 `clients.claim()`이 리로드를 유발하지 않게 한다. */
let applyRequested = false;
let reloaded = false;
let reloadPage: () => void = defaultReload;
let sessionBusy: () => boolean = () => isSessionActive(currentScreen());

function defaultReload(): void {
  if (typeof location !== "undefined") location.reload();
}

function resolveContainer(deps: SwInstallDeps): ServiceWorkerContainerLike | null {
  if (deps.container !== undefined) return deps.container;
  if (typeof navigator === "undefined") return null;
  // ⚠️ 타입을 믿지 않고 런타임 감지한다(비보안 컨텍스트·구형 WebView에는 없다).
  const container = navigator.serviceWorker as unknown as ServiceWorkerContainerLike | undefined;
  return typeof container?.register === "function" ? container : null;
}

async function defaultReadBuildId(): Promise<string | null> {
  try {
    const response = await fetch("/precache-manifest.json", { cache: "no-store" });
    if (!response.ok) return null;
    const raw: unknown = await response.json();
    const buildId = (raw as { buildId?: unknown } | null)?.buildId;
    return typeof buildId === "string" ? buildId : null;
  } catch {
    return null;
  }
}

function syncStatus(): void {
  const current = registration;
  if (current === null) return;
  const status: SwStatus =
    current.waiting !== null ? "waiting" : current.active !== null ? "active" : "registering";
  swStateStore.setState({ status });
}

/** 부트스트랩 7단계(`main.tsx`). **멱등** — 두 번 불러도 등록은 1회다. */
export function installServiceWorker(deps: SwInstallDeps = {}): void {
  if (installed) return;
  installed = true;

  reloadPage = deps.reload ?? defaultReload;
  if (deps.isBusy !== undefined) sessionBusy = deps.isBusy;

  const enabled = deps.enabled ?? import.meta.env.PROD;
  if (!enabled) {
    swStateStore.setState({ status: "disabled", buildId: null });
    return;
  }

  const container = resolveContainer(deps);
  if (container === null) {
    swStateStore.setState({ status: "unsupported", buildId: null });
    return;
  }

  swStateStore.setState({ status: "registering", buildId: null });

  container.addEventListener("controllerchange", () => {
    // 첫 설치의 `clients.claim()`도 여기로 온다 — **[지금 적용]을 누른 경우에만** 리로드한다.
    if (!applyRequested || reloaded) return;
    reloaded = true;
    reloadPage();
  });

  void container.register("/sw.js").then(
    (registered) => {
      registration = registered;
      registered.addEventListener("updatefound", () => {
        // 새 워커가 설치를 마치면 `waiting`이 채워진다.
        registered.installing?.addEventListener("statechange", () => syncStatus());
        syncStatus();
      });
      syncStatus();

      const read = deps.readBuildId ?? defaultReadBuildId;
      void read().then((buildId) => swStateStore.setState({ buildId }));
    },
    (err: unknown) => {
      logger.error("Service Worker 등록 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      swStateStore.setState({ status: "failed", buildId: null });
    },
  );
}

/** [앱 업데이트 확인]. 대기 중 갱신이 생겼으면 `true`. */
export async function checkForUpdate(): Promise<boolean> {
  const current = registration;
  if (current === null) return false;
  try {
    await current.update();
  } catch (err) {
    logger.warn("앱 업데이트 확인 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
  syncStatus();
  return swStateStore.getState().status === "waiting";
}

/** [지금 적용]. 실제로 메시지를 보냈으면 `true`. */
export async function applyWaitingUpdate(): Promise<boolean> {
  // 첫 실행문이 촬영 중 가드다(렌더 가드와 2중 — M10).
  if (sessionBusy()) {
    logger.warn("촬영 중에는 앱 갱신을 적용하지 않는다");
    return false;
  }

  const waiting = registration?.waiting ?? null;
  if (waiting === null) return false;

  applyRequested = true;
  waiting.postMessage({ type: APPLY_UPDATE_MESSAGE });
  logger.info("앱 갱신 적용 요청");
  return true;
}

/** 테스트·재초기화용. */
export function resetSwUpdateForTests(): void {
  installed = false;
  registration = null;
  applyRequested = false;
  reloaded = false;
  reloadPage = defaultReload;
  sessionBusy = () => isSessionActive(currentScreen());
  swStateStore.setState(INITIAL);
}
