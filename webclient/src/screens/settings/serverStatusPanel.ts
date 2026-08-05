import { createHealthService, type ServerProbeResult } from "@adapters/http/healthService";
import { logger } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";
import { env } from "../../env";

/**
 * 서버 연결 상태(읽기 전용) — 03 §12.1 고급 섹션 · 06 §2.1
 *
 * ⚠️ **"구성됨"은 "도달 성공"이 아니다.** 두 줄로 나눠 표시한다 — 합치면 운영자가
 *    "주소가 있으니 되는 거겠지"로 오독한다.
 * ⚠️ **게이트 키는 "설정됨/미설정"만** 보여준다. 값은 어떤 경우에도 화면·로그에 내지 않는다
 *    (analysis/41 §2.5).
 */

export type ServerStatusView =
  | { readonly kind: "idle" }
  | { readonly kind: "loading" }
  | { readonly kind: "ready"; readonly probe: ServerProbeResult }
  /** 화면을 떠나 결과를 버렸다(언마운트 후 setState 금지). */
  | { readonly kind: "cancelled" };

export interface ServerStatusDeps {
  readonly probe: () => Promise<ServerProbeResult>;
}

/**
 * 프로브 1회.
 *
 * ⚠️ `healthService.probe()`는 취소 신호를 받지 않는다. 그래서 **결과를 버리는 방식**으로
 *    취소한다 — 진행 중인 요청은 완료되지만 화면 상태를 건드리지 않는다. (요청 자체를 끊으려면
 *    `healthService`에 `AbortSignal`을 뚫어야 하고, 그것은 이 Step의 범위 밖이다.)
 */
export async function loadServerStatus(
  deps: ServerStatusDeps,
  signal?: AbortSignal,
): Promise<ServerStatusView> {
  // 함수로 감싼다 — 인라인 비교는 `await` 뒤에도 TS 좁힘이 남아 두 번째 검사가 죽는다.
  const aborted = (): boolean => signal?.aborted === true;
  if (aborted()) return { kind: "cancelled" };

  // `probe()`는 던지지 않도록 만들어져 있지만(어댑터 규약), 전제가 깨져도 화면은 살아 있어야 한다.
  let probe: ServerProbeResult;
  try {
    probe = await deps.probe();
  } catch (err) {
    logger.warn("서버 상태 조회 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    probe = { reachable: false, deployedAt: null, gateKeyValid: null, oauth: null, detail: null };
  }

  if (aborted()) return { kind: "cancelled" };
  return { kind: "ready", probe };
}

export interface ServerStatusRow {
  readonly label: string;
  readonly value: string;
}

/** 표시 행 조립. **주소·버킷은 값을 보여주고, 게이트 키는 상태만 보여준다.** */
export function describeServerStatus(view: ServerStatusView): readonly ServerStatusRow[] {
  const configured = env.backendBaseUrl.trim().length > 0;
  const rows: ServerStatusRow[] = [
    {
      label: "서버 주소",
      value: configured ? env.backendBaseUrl : STRINGS.settings.serverNotConfigured,
    },
    {
      label: "구성",
      value: configured ? STRINGS.settings.serverConfigured : STRINGS.settings.serverNotConfigured,
    },
  ];

  if (view.kind === "loading" || view.kind === "idle") {
    rows.push({ label: "도달", value: STRINGS.settings.serverChecking });
    return rows;
  }
  if (view.kind === "cancelled") {
    rows.push({ label: "도달", value: STRINGS.settings.serverUnknown });
    return rows;
  }

  rows.push({
    label: "도달",
    value: view.probe.reachable
      ? STRINGS.settings.serverReachable
      : STRINGS.settings.serverUnreachable,
  });
  rows.push({ label: "게이트 키", value: gateKeyLabel(view.probe.gateKeyValid) });
  if (view.probe.deployedAt !== null) {
    rows.push({ label: STRINGS.settings.deployedAt, value: view.probe.deployedAt });
  }
  return rows;
}

/** 키 유효성은 3상태다: 유효 / 거부(401) / 알 수 없음(도달 실패·판정 불가). */
function gateKeyLabel(valid: boolean | null): string {
  if (valid === true) return STRINGS.settings.gateKeySet;
  if (valid === false) return STRINGS.settings.gateKeyInvalid;
  return env.backendApiKey.trim().length > 0
    ? STRINGS.settings.gateKeySet
    : STRINGS.settings.gateKeyUnset;
}

export function defaultServerStatusDeps(
  overrides: Partial<ServerStatusDeps> = {},
): ServerStatusDeps {
  return { probe: () => createHealthService().probe(), ...overrides };
}
