import { logger } from "@adapters/storage/logStore";
import { getToken } from "@shell/authStore";
import { env } from "../../env";
import {
  NotAuthenticatedError,
  parseErrorEnvelope,
  toBackendError,
  toNetworkError,
} from "./errors";

/**
 * 백엔드 HTTP 클라이언트 — 06 §3
 *
 * 한 곳에서 조립하는 것: base URL 결합 · 게이트 키 · Bearer · 타임아웃 · 에러 매핑 · 로깅.
 *
 * ⚠️ **자동 재시도를 하지 않는다.** 업로드 commit 같은 비멱등 호출이 중복 집계될 수 있다.
 * ⚠️ 로그에 **본문·토큰·서명 URL을 남기지 않는다**(analysis/41 §8). 메서드·경로·상태·소요만 남긴다.
 */

export const GATE_KEY_HEADER = "X-MCPhoto-Client";
export const REQUEST_TIMEOUT_MS = 100_000;

export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export interface RequestOptions {
  readonly method?: HttpMethod;
  /** base URL에 붙는 상대 경로(선행 `/` 없이). */
  readonly path: string;
  readonly query?: Readonly<Record<string, string | number | undefined>>;
  readonly body?: unknown;
  /**
   * Bearer 요구 수준.
   *   `none`     — 붙이지 않는다(게이트 키만)
   *   `optional` — 토큰이 있으면 붙인다(업로드 — 게스트 허용)
   *   `required` — 없으면 **요청을 보내지 않고** `NotAuthenticatedError`
   */
  readonly auth?: "none" | "optional" | "required";
  readonly timeoutMs?: number;
  readonly signal?: AbortSignal;
}

export interface BackendClientDeps {
  readonly fetchImpl?: typeof fetch;
  readonly baseUrl?: string;
  readonly gateKey?: string;
  readonly tokenProvider?: () => string | null;
  readonly now?: () => number;
}

function buildUrl(baseUrl: string, path: string, query?: RequestOptions["query"]): string {
  // baseUrl은 트레일링 슬래시가 **있다**(env.ts가 보장) → 상대 결합이 안전하다.
  const url = new URL(path.replace(/^\/+/, ""), baseUrl);
  if (query !== undefined) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined) url.searchParams.set(key, String(value));
    }
  }
  return url.toString();
}

export interface BackendClient {
  request<T>(options: RequestOptions): Promise<T>;
}

export function createBackendClient(deps: BackendClientDeps = {}): BackendClient {
  const fetchImpl = deps.fetchImpl ?? fetch;
  const baseUrl = deps.baseUrl ?? env.backendBaseUrl;
  const gateKey = deps.gateKey ?? env.backendApiKey;
  const tokenProvider = deps.tokenProvider ?? getToken;
  const now = deps.now ?? (() => Date.now());

  async function request<T>(options: RequestOptions): Promise<T> {
    const method = options.method ?? "GET";
    const auth = options.auth ?? "none";
    const token = auth === "none" ? null : tokenProvider();

    if (auth === "required" && token === null) {
      // 서버 왕복 없이 즉시 실패한다 — 401을 "만료"로 오해하는 흐름을 만들지 않는다.
      logger.warn("인증 필요 호출을 토큰 없이 시도", { method, path: options.path });
      throw new NotAuthenticatedError();
    }

    const headers: Record<string, string> = { Accept: "application/json" };
    // 게이트 키는 값이 있을 때만 부착한다(미설정 배포에서 빈 헤더를 보내지 않는다).
    if (gateKey.length > 0) headers[GATE_KEY_HEADER] = gateKey;
    if (token !== null) headers.Authorization = `Bearer ${token}`;
    if (options.body !== undefined) headers["Content-Type"] = "application/json";

    const controller = new AbortController();
    const timeoutMs = options.timeoutMs ?? REQUEST_TIMEOUT_MS;
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    // 호출측 취소(화면 이탈)도 함께 반영한다.
    const externalAbort = (): void => controller.abort();
    options.signal?.addEventListener("abort", externalAbort);

    const startedAt = now();
    const url = buildUrl(baseUrl, options.path, options.query);

    let response: Response;
    try {
      response = await fetchImpl(url, {
        method,
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: controller.signal,
        // 서버는 쿠키를 쓰지 않는다(Bearer 전용) — 자격 증명을 보내지 않는다.
        credentials: "omit",
        cache: "no-store",
      });
    } catch (err) {
      const networkError = toNetworkError(err);
      logger.error("백엔드 호출 실패(응답 없음)", {
        method,
        path: options.path,
        elapsedMs: Math.round(now() - startedAt),
        timedOut: networkError.timedOut,
        reason: networkError.message,
      });
      throw networkError;
    } finally {
      clearTimeout(timer);
      options.signal?.removeEventListener("abort", externalAbort);
    }

    const elapsedMs = Math.round(now() - startedAt);

    if (!response.ok) {
      const body = await readJsonSafely(response);
      const envelope = parseErrorEnvelope(body, response.status);
      logger.warn("백엔드 오류 응답", {
        method,
        path: options.path,
        status: response.status,
        // ⚠️ 키 이름이 `code`면 **마스킹된다** — 로그 마스킹 목록의 `code`는 OAuth 인가 코드(비밀)다.
        //    오류 코드는 진단에 필요하므로 이름을 구분한다(`errorCode`).
        errorCode: envelope.code,
        elapsedMs,
      });
      throw toBackendError(response.status, envelope);
    }

    logger.info("백엔드 호출", { method, path: options.path, status: response.status, elapsedMs });

    if (response.status === 204) return undefined as T;
    const body = await readJsonSafely(response);
    return body as T;
  }

  return { request };
}

async function readJsonSafely(response: Response): Promise<unknown> {
  try {
    const text = await response.text();
    return text.length === 0 ? null : JSON.parse(text);
  } catch {
    // 본문이 JSON이 아니어도 상태코드 기반 처리는 계속된다.
    return null;
  }
}

let singleton: BackendClient | null = null;

export function getBackendClient(): BackendClient {
  singleton ??= createBackendClient();
  return singleton;
}

export function setBackendClientForTests(client: BackendClient | null): void {
  singleton = client;
}
