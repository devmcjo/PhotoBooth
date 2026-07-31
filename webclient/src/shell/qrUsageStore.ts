import type { SessionUser } from "@domain/accounts/sessionUser";
import {
  createQrUsageService,
  isTempUserBlocked,
  type QrUsage,
  type QrUsageReason,
  type QrUsageService,
} from "@adapters/http/qrUsageService";
import { logger } from "@adapters/storage/logStore";
import { sessionStore } from "./sessionStore";

/**
 * TempUser 무료 한도 캐시 — `isTempUserBlocked`의 공급원 (07 §7 · analysis/31 §4.4)
 *
 * Windows `AppShellViewModel`과 **같은 형태**다: 계정이 바뀔 때 **1회 fire-and-forget**으로
 * 조회해 캐시하고, 판정은 **동기 파생값**이다.
 *
 * ⚠️ **동기 판정이어야 한다.** 비동기로 바꾸면 `Result`의 [다음]이 네트워크를 기다리게 되고,
 *    서버 미도달 환경에서 손님이 최대 100초 멈춘다.
 * ⚠️ **fail-open**이다(M9). 미조회·조회 실패·비TempUser·게스트는 전부 `false`(허용).
 *    과금 안전은 서버가 prepare/commit에서 최종 판정해 담보한다.
 * ⚠️ 비TempUser에게는 **요청하지 않는다.** 서버가 주는 `remaining*: 0`은 "소진"이 아니라
 *    "무제한"이라 오해의 소지가 있고, 애초에 조회할 이유가 없다.
 */

export interface QrUsageSnapshot {
  /** 미조회·비TempUser·게스트는 null. */
  readonly usage: QrUsage | null;
  readonly loading: boolean;
}

export interface QrUsageLifecycleDeps {
  readonly service?: QrUsageService;
  /** 테스트 주입(기본 `sessionStore`의 `currentUser` 구독). */
  readonly subscribe?: (listener: (user: SessionUser | null) => void) => () => void;
}

let usage: QrUsage | null = null;
let loading = false;
/**
 * 조회 세대. 응답이 도착했을 때 값이 달라졌으면 **그 사이 계정이 바뀐 것**이므로 폐기한다.
 * 사용자 id로 비교하지 않는 이유: 같은 계정으로 재로그인해도 조회는 새로 시작돼야 한다.
 */
let generation = 0;
let unsubscribe: (() => void) | null = null;

/** TempUser이고 한도 초과인가. **미조회·조회 실패·비TempUser·게스트는 false**(fail-open — M9). */
export function isTempUserQrBlocked(): boolean {
  return usage !== null && isTempUserBlocked(usage);
}

/** 초과 사유(설정·진단 표시용 — Step 13·16이 소비). 해당 없으면 `"ok"`. */
export function tempUserQrReason(): QrUsageReason {
  return isTempUserQrBlocked() && usage !== null ? usage.reason : "ok";
}

/** 현재 스냅샷(진단용). */
export function qrUsageSnapshot(): QrUsageSnapshot {
  return { usage, loading };
}

function onUserChanged(service: QrUsageService, user: SessionUser | null): void {
  // 캐시를 **먼저** 비운다 — 이전 계정의 판정이 새 계정에 새면 안 된다.
  generation++;
  const requested = generation;
  usage = null;
  loading = false;

  // 게스트·비TempUser는 조회 대상이 아니다(요청 0건).
  if (user === null || user.role !== "temp_user") return;

  loading = true;
  void service
    .fetch()
    .then((next) => {
      // 조회 중 계정이 바뀌었으면 응답을 폐기한다(경합 방어 — Windows와 동일).
      if (generation !== requested) return;
      usage = next;
      loading = false;
      logger.info("무료 한도 조회 반영", {
        blocked: isTempUserBlocked(next),
        limitReason: next.reason,
      });
    })
    .catch(() => {
      // `qrUsageService`가 이미 fail-open이라 여기 오지 않는다. 미처리 rejection만 막는다.
      if (generation !== requested) return;
      loading = false;
    });
}

/**
 * 앱 시작 시 1회 설치(`installTokenLifecycle` 옆). **해제 함수를 돌려준다.**
 * 이미 설치돼 있으면 기존 해제 함수를 그대로 준다(이중 구독 금지).
 */
export function installQrUsageLifecycle(deps: QrUsageLifecycleDeps = {}): () => void {
  if (unsubscribe !== null) return unsubscribe;

  const service = deps.service ?? createQrUsageService();
  const subscribe =
    deps.subscribe ??
    ((listener: (user: SessionUser | null) => void) =>
      sessionStore.subscribe((state) => state.currentUser, listener));

  const remove = subscribe((user) => {
    onUserChanged(service, user);
  });

  unsubscribe = () => {
    remove();
    unsubscribe = null;
    generation++;
    usage = null;
    loading = false;
  };
  return unsubscribe;
}

/** 테스트·재초기화용. 설치돼 있지 않으면 아무 일도 하지 않는다. */
export function uninstallQrUsageLifecycle(): void {
  unsubscribe?.();
}
