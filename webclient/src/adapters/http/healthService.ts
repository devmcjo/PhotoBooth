import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * `GET /health` · `GET /frames/default` 두 프로브 — 06 §2.1
 *
 * ⚠️ **헬스 응답으로 게이트 키 유효성을 판정할 수 없다.** 키가 없거나 틀려도 200이다
 *    (`deployedAt`은 유효 키일 때만 포함되지만, 서버 버전에 따라 없을 수도 있어 근거로 삼지 않는다).
 *    키 유효성은 `GET /frames/default`(apiKey 게이트)의 **401 여부**로 확정한다.
 */

export interface HealthResponse {
  readonly status: string;
  readonly time?: string;
  /** 유효 게이트 키일 때만 온다. 진단 화면의 "Web Deploy Date". */
  readonly deployedAt?: string;
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
  readonly detail: string | null;
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
      let detail: string | null = null;

      try {
        const health = await check();
        reachable = health.status === "ok" || health.status.length > 0;
        deployedAt = health.deployedAt ?? null;
      } catch (err) {
        detail = err instanceof Error ? err.message : String(err);
        return { reachable: false, deployedAt: null, gateKeyValid: null, detail };
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

      return { reachable, deployedAt, gateKeyValid, detail };
    },
  };
}
