/**
 * Service Worker 라우팅 정책 — 01 §6 (순수 · **import 0**)
 *
 * 이 파일이 순수한데도 `domain/`이 아니라 어댑터에 있는 이유: `domain/index.ts`가 평면
 * `export *` 배럴이라 `cacheNameFor` 같은 일반명이 재수출 충돌을 만든다
 * (`adapters/storage/logPolicy.ts`가 같은 선례다).
 *
 * ⚠️ **기본은 bypass다.** 알 수 없는 경로는 SW가 손대지 않는다 — API 응답·서명 URL이
 *    캐시되면 만료된 URL을 오프라인에서 재생하거나 남의 세션 데이터를 되돌려줄 수 있다.
 * ⚠️ 판정 **순서가 계약**이다. 3(cross-origin)·4(API)가 5(navigate)보다 앞이어야 한다.
 */

export type SwRoute =
  /** SW가 손대지 않는다(`respondWith` 미호출). */
  | { readonly kind: "bypass" }
  /** network-first(타임아웃) → 셸 폴백. */
  | { readonly kind: "navigate" }
  /** cache-first. 내용 해시가 붙은 자산 전용. */
  | { readonly kind: "immutable" }
  /** network-first → 캐시 폴백. 운영자가 교체하는 파일. */
  | { readonly kind: "fresh" }
  /** stale-while-revalidate. */
  | { readonly kind: "static" };

export interface SwRequestInput {
  readonly method: string;
  /** `Request.mode`. `"navigate"`면 문서 요청이다. */
  readonly mode: string;
  readonly url: string;
  /** SW 스코프의 출처(`self.location.origin`). */
  readonly origin: string;
}

/** 셸 precache 대상 중 **해시가 붙지 않는** 고정 경로. */
export const PRECACHE_STABLE_URLS: readonly string[] = [
  "/",
  "/index.html",
  "/manifest.webmanifest",
  "/branding.json",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/icon-512-maskable.png",
  "/frames/index.json",
];

/** `fresh`로 다루는 경로(운영자가 교체하거나 배포마다 바뀌는 작은 JSON). */
const FRESH_PATHS: readonly string[] = [
  "/branding.json",
  "/manifest.webmanifest",
  "/frames/index.json",
  "/precache-manifest.json",
];

/** `static`(stale-while-revalidate) 접두. */
const STATIC_PREFIXES: readonly string[] = ["/icons/", "/frames/", "/sounds/"];

export function classifySwRequest(input: SwRequestInput): SwRoute {
  // 1. GET이 아니면 손대지 않는다(업로드 PUT·POST).
  if (input.method !== "GET") return { kind: "bypass" };

  // 2. URL 파싱 실패 → bypass.
  let url: URL;
  try {
    url = new URL(input.url);
  } catch {
    return { kind: "bypass" };
  }

  // 3. 다른 출처 → bypass. 백엔드·서명 PUT·Storage가 전부 여기서 끝난다.
  if (url.origin !== input.origin) return { kind: "bypass" };

  // 4. 동일 출처 프록시가 생겨도 안전하도록 API 경로를 명시적으로 제외한다.
  const path = url.pathname;
  if (path.startsWith("/api/") || path.includes("/uploads")) return { kind: "bypass" };

  // 5. 문서 요청 → 셸 폴백이 가능한 network-first.
  if (input.mode === "navigate") return { kind: "navigate" };

  // 6. 내용 해시가 붙은 번들 자산.
  if (path.startsWith("/assets/")) return { kind: "immutable" };

  // 7. 운영자 교체 파일.
  if (FRESH_PATHS.includes(path)) return { kind: "fresh" };

  // 8. 정적 자산.
  if (path === "/favicon.ico") return { kind: "static" };
  if (STATIC_PREFIXES.some((prefix) => path.startsWith(prefix))) return { kind: "static" };

  // 9. **기본 거부** — 알 수 없는 경로는 캐시하지 않는다.
  return { kind: "bypass" };
}

/**
 * 캐시에 넣어도 되는 응답인가.
 * `opaque`(cross-origin no-cors)는 **절대** 캐시하지 않는다 — 용량이 과대 계산되고
 * 실패 응답인지 알 수 없다.
 */
export function isCacheableResponse(status: number, type: string): boolean {
  return status === 200 && (type === "basic" || type === "default");
}

/** 셸 캐시 이름. 빌드 id가 바뀌면 새 캐시가 생기고 `activate`가 옛 것을 지운다. */
export const CACHE_PREFIX = "mcphoto-shell-";

export function cacheNameForBuild(buildId: string): string {
  return `${CACHE_PREFIX}${buildId}`;
}
