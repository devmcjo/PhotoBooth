/// <reference lib="webworker" />
/**
 * Service Worker — 앱 셸 precache + 오프라인 폴백 (01 §6)
 *
 * ⚠️ **정책은 여기 없다.** 라우팅 판정은 `adapters/platform/swPolicy`(순수)가 소유하고
 *    이 파일은 그 결과를 실행하는 **얇은 래퍼**다(정적 검사 SW-2가 등장 순서를 고정한다).
 * ⚠️ **API 응답·서명 URL을 캐시하지 않는다.** cross-origin은 `classifySwRequest`가 `bypass`로
 *    떨어뜨리고, bypass면 `respondWith`를 아예 부르지 않는다.
 * ⚠️ **`install`에서 `skipWaiting()`을 부르지 않는다**(SW-1). 자동 갱신이 되살아나면
 *    촬영 중에 앱이 바뀐다. 갱신은 [지금 적용] 메시지 또는 모든 탭이 닫힌 뒤 다음 시작뿐이다.
 * ⚠️ **`cache.addAll`을 쓰지 마라**(15 §4 함정 13). 원자적이라 URL 하나가 404면 install 전체가
 *    실패하고 SW가 영원히 설치되지 않는다(`/sounds/shutter.wav`는 지금 존재하지 않는다).
 * ⚠️ **`logger`를 import하지 않고 `console.*`도 쓰지 않는다**(SW-3). SW에는 로그 스토어가
 *    붙지 않아 여기서 남긴 로그는 진단·내보내기에 도달하지 않는다. 상태는 메인 스레드가
 *    `registration`에서 읽는다.
 */
import {
  cacheNameForBuild,
  classifySwRequest,
  isCacheableResponse,
  PRECACHE_STABLE_URLS,
} from "@adapters/platform/swPolicy";

/** 모듈 스코프 shadow — 이 파일이 모듈이라 전역 `self`(Window)와 충돌하지 않는다. */
declare const self: ServiceWorkerGlobalScope;

/**
 * 빌드 타임 주입(`vite.sw.config.ts`의 `define`).
 *
 * ⚠️ 선언을 `vite-env.d.ts`(전역)에 두지 마라 — 앱 번들에서도 쓸 수 있는 것처럼 보이는데
 *    `vite.config.ts`는 그 이름을 `define`하지 않아 런타임 `ReferenceError`가 된다.
 */
declare const __MCPHOTO_PRECACHE__: {
  readonly buildId: string;
  readonly assets: readonly string[];
};

const CACHE_NAME = cacheNameForBuild(__MCPHOTO_PRECACHE__.buildId);
const SHELL_URL = "/index.html";
/** navigate 요청의 네트워크 대기 상한. 넘으면 셸 캐시로 떨어진다. */
const NAVIGATE_TIMEOUT_MS = 3_000;
/** [지금 적용]이 보내는 메시지. 이 경로에서만 `skipWaiting`을 부른다. */
const APPLY_UPDATE = "MCPHOTO_APPLY_UPDATE";

/** 개별 `cache.add`를 `allSettled`로 감싼다 — 없는 자산 하나가 install을 깨뜨리지 않는다. */
async function precache(): Promise<void> {
  const cache = await caches.open(CACHE_NAME);
  const urls = new Set<string>([...PRECACHE_STABLE_URLS, ...__MCPHOTO_PRECACHE__.assets]);
  await Promise.allSettled([...urls].map((url) => cache.add(url)));
}

async function dropOtherCaches(): Promise<void> {
  const names = await caches.keys();
  await Promise.allSettled(
    names
      .filter((name) => name.startsWith("mcphoto-shell-") && name !== CACHE_NAME)
      .map((name) => caches.delete(name)),
  );
}

async function putIfCacheable(request: Request, response: Response): Promise<void> {
  if (!isCacheableResponse(response.status, response.type)) return;
  const cache = await caches.open(CACHE_NAME);
  await cache.put(request, response.clone());
}

/** network-first + 타임아웃. 실패·시간 초과는 셸 캐시로 떨어진다. */
async function handleNavigate(request: Request): Promise<Response> {
  const timeout = new Promise<null>((resolve) => {
    setTimeout(() => resolve(null), NAVIGATE_TIMEOUT_MS);
  });

  try {
    const network = await Promise.race([fetch(request), timeout]);
    if (network !== null) {
      void putIfCacheable(request, network);
      return network;
    }
  } catch {
    // 아래 캐시 폴백으로 간다.
  }

  const cached = (await caches.match(request)) ?? (await caches.match(SHELL_URL));
  return cached ?? Response.error();
}

async function handleImmutable(request: Request): Promise<Response> {
  const cached = await caches.match(request);
  if (cached !== undefined) return cached;
  const network = await fetch(request);
  await putIfCacheable(request, network);
  return network;
}

async function handleFresh(request: Request): Promise<Response> {
  try {
    const network = await fetch(request);
    await putIfCacheable(request, network);
    return network;
  } catch (err) {
    const cached = await caches.match(request);
    if (cached !== undefined) return cached;
    throw err;
  }
}

async function handleStatic(request: Request): Promise<Response> {
  const cached = await caches.match(request);
  if (cached !== undefined) {
    // 배경 갱신. 실패는 무시한다(캐시본을 이미 돌려줬다).
    void fetch(request)
      .then((network) => putIfCacheable(request, network))
      .catch(() => undefined);
    return cached;
  }
  const network = await fetch(request);
  await putIfCacheable(request, network);
  return network;
}

self.addEventListener("install", (event: ExtendableEvent) => {
  event.waitUntil(precache());
});

self.addEventListener("activate", (event: ExtendableEvent) => {
  event.waitUntil(dropOtherCaches().then(() => self.clients.claim()));
});

self.addEventListener("fetch", (event: FetchEvent) => {
  const route = classifySwRequest({
    method: event.request.method,
    mode: event.request.mode,
    url: event.request.url,
    origin: self.location.origin,
  });

  switch (route.kind) {
    case "navigate":
      event.respondWith(handleNavigate(event.request));
      return;
    case "immutable":
      event.respondWith(handleImmutable(event.request));
      return;
    case "fresh":
      event.respondWith(handleFresh(event.request));
      return;
    case "static":
      event.respondWith(handleStatic(event.request));
      return;
    default:
      // bypass — `respondWith`를 부르지 않는다(브라우저 기본 경로).
      return;
  }
});

self.addEventListener("message", (event: ExtendableMessageEvent) => {
  const data = event.data as { type?: unknown } | null;
  // 사용자 트리거 [지금 적용]에서만 대기 중 SW를 활성화한다.
  if (data !== null && typeof data === "object" && data.type === APPLY_UPDATE) {
    void self.skipWaiting();
  }
});
