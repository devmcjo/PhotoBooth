import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * `GET /health` · `GET /frames/default` 두 프로브 — 06 §2.1
 *
 * ⚠️ **헬스 응답으로 게이트 키 유효성을 판정할 수 없다.** 키가 없거나 틀려도 200이다
 *    (`deployedAt`은 유효 키일 때만 포함되지만, 서버 버전에 따라 없을 수도 있어 근거로 삼지 않는다).
 *    키 유효성은 `GET /frames/default`(apiKey 게이트)의 **401 여부**로 확정한다.
 */

/**
 * 서버가 보고하는 OAuth 클라이언트 구성 상태(열거값뿐 — client_id 값은 담기지 않는다).
 * 서버 정의는 `web/functions/src/domain/oauthStatus.ts`.
 */
export type OAuthClientConfigState = "ok" | "malformed" | "unset";

export interface OAuthConfigStatus {
  readonly web: OAuthClientConfigState;
  readonly desktop: OAuthClientConfigState;
  /** web·desktop이 같은 client_id다(유형이 다르면 공유할 수 없으므로 오구성). */
  readonly sharedClientId: boolean;
  /** `OAUTH_REDIRECT_ALLOWLIST` 항목 수. 주소 자체는 오지 않는다. */
  readonly redirectAllowlistCount: number;
}

export interface HealthResponse {
  readonly status: string;
  readonly time?: string;
  /** 유효 게이트 키일 때만 온다. 진단 화면의 "Web Deploy Date". */
  readonly deployedAt?: string;
  /** 유효 게이트 키일 때만 온다. **구버전 서버에는 없다** → 파싱 실패는 `null`로 접는다. */
  readonly oauth?: unknown;
}

export interface ServerProbeResult {
  /** `/health`가 200으로 응답했는가(= 서버 도달). */
  readonly reachable: boolean;
  readonly deployedAt: string | null;
  /**
   * 게이트 키가 유효한가. `/frames/default`가 401이 아니면 유효다.
   * 서버에 도달하지 못했으면 `null`(알 수 없음 — "구성됨"과 "도달 성공"을 구분한다).
   */
  readonly gateKeyValid: boolean | null;
  /**
   * 서버 OAuth 구성 상태. **구버전 서버·미인증·도달 실패면 `null`**(= 알 수 없음).
   * "알 수 없음"과 "미설정"을 섞으면 운영자가 멀쩡한 배포를 오구성으로 읽는다.
   */
  readonly oauth: OAuthConfigStatus | null;
  readonly detail: string | null;
}

const CONFIG_STATES: readonly OAuthClientConfigState[] = ["ok", "malformed", "unset"];

function parseConfigState(value: unknown): OAuthClientConfigState | null {
  return typeof value === "string" && (CONFIG_STATES as readonly string[]).includes(value)
    ? (value as OAuthClientConfigState)
    : null;
}

/**
 * 서버 응답의 `oauth`를 **경계에서 검증**한다 — 타입 단언으로 통과시키면 구버전 서버가
 * 보낸 `undefined`가 화면까지 흘러 "미설정"으로 오독된다.
 * 하나라도 형식이 맞지 않으면 통째로 `null`(= 알 수 없음)이다.
 */
export function parseOAuthConfigStatus(raw: unknown): OAuthConfigStatus | null {
  if (typeof raw !== "object" || raw === null) return null;
  const record = raw as Record<string, unknown>;
  const web = parseConfigState(record.web);
  const desktop = parseConfigState(record.desktop);
  const count = record.redirectAllowlistCount;
  if (web === null || desktop === null) return null;
  if (typeof record.sharedClientId !== "boolean") return null;
  if (typeof count !== "number" || !Number.isFinite(count) || count < 0) return null;
  return {
    web,
    desktop,
    sharedClientId: record.sharedClientId,
    redirectAllowlistCount: Math.floor(count),
  };
}

export interface HealthService {
  check(): Promise<HealthResponse>;
  /** 진단 모달용 2프로브. 예외를 던지지 않고 상태로 돌려준다. */
  probe(): Promise<ServerProbeResult>;
}

export function createHealthService(client: BackendClient = getBackendClient()): HealthService {
  async function check(): Promise<HealthResponse> {
    return client.request<HealthResponse>({ path: "health" });
  }

  return {
    check,

    async probe() {
      let reachable = false;
      let deployedAt: string | null = null;
      let oauth: OAuthConfigStatus | null = null;
      let detail: string | null = null;

      try {
        const health = await check();
        reachable = health.status === "ok" || health.status.length > 0;
        deployedAt = health.deployedAt ?? null;
        oauth = parseOAuthConfigStatus(health.oauth);
      } catch (err) {
        detail = err instanceof Error ? err.message : String(err);
        return { reachable: false, deployedAt: null, gateKeyValid: null, oauth: null, detail };
      }

      let gateKeyValid: boolean | null = null;
      try {
        await client.request<unknown>({ path: "frames/default" });
        gateKeyValid = true;
      } catch (err) {
        // 401만 "키 무효"다. 그 외 오류는 키 판정 근거가 되지 못한다.
        const status = (err as { status?: number }).status;
        gateKeyValid = status === 401 ? false : null;
        detail = err instanceof Error ? err.message : String(err);
      }

      return { reachable, deployedAt, gateKeyValid, oauth, detail };
    },
  };
}
