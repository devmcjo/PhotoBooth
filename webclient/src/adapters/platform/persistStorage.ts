/**
 * 저장소 영속성 요청 — 05 §5.5 · 00 §3.2
 *
 * WebKit은 "설치되지 않은 사이트의 script-writable 저장소를 7일 무상호작용 시 삭제"한다.
 * 매일 영업하는 키오스크는 트리거되지 않지만, **행사용으로 몇 주 쉬는 기기는 위험하다**.
 * 그래서 ① 영속을 요청하고 ② **결과를 진단 화면에 정직하게 표시**하고 ③ 내보내기 백업을 제공한다.
 *
 * 실패·미지원은 정상 경로다 — 부팅을 막지 않는다.
 */

export type PersistState = "granted" | "denied" | "unsupported";

export interface StorageStatus {
  readonly persistState: PersistState;
  /** 사용 바이트. 알 수 없으면 null. */
  readonly usage: number | null;
  /** 할당 바이트. 알 수 없으면 null. */
  readonly quota: number | null;
}

/** 여유가 이 비율 미만이면 설정·진단에 경고 배지를 띄운다(05 §5.4). */
export const LOW_STORAGE_THRESHOLD = 0.1;

/** `navigator.storage`의 최소 표면. 테스트가 가짜를 주입한다. */
export interface StorageManagerLike {
  persist?: () => Promise<boolean>;
  persisted?: () => Promise<boolean>;
  estimate?: () => Promise<{ usage?: number; quota?: number }>;
}

/**
 * 영속을 요청하고 현재 상태를 돌려준다. **이미 승인돼 있으면 다시 요청하지 않는다**
 * (재요청이 일부 브라우저에서 프롬프트를 반복 노출한다).
 */
export async function requestPersistentStorage(
  manager: StorageManagerLike | undefined,
): Promise<StorageStatus> {
  if (manager === undefined || typeof manager.persist !== "function") {
    return { persistState: "unsupported", usage: null, quota: null };
  }

  let persistState: PersistState = "denied";
  try {
    const already = typeof manager.persisted === "function" ? await manager.persisted() : false;
    persistState = (already ? true : await manager.persist()) ? "granted" : "denied";
  } catch {
    persistState = "denied";
  }

  let usage: number | null = null;
  let quota: number | null = null;
  if (typeof manager.estimate === "function") {
    try {
      const estimate = await manager.estimate();
      usage = typeof estimate.usage === "number" ? estimate.usage : null;
      quota = typeof estimate.quota === "number" ? estimate.quota : null;
    } catch {
      // 추정 실패는 무시한다(진단에 "알 수 없음"으로 표시된다).
    }
  }

  return { persistState, usage, quota };
}

/**
 * **요청하지 않고** 현재 상태만 읽는다(설정·진단 화면 표시용).
 *
 * ⚠️ `requestPersistentStorage`와 섞지 마라 — 그쪽은 미승인 시 `persist()`를 **실제로 호출**해
 *    일부 브라우저에서 프롬프트를 띄운다. 화면을 열었을 뿐인데 권한 창이 뜨면 안 된다.
 */
export async function readStorageStatus(
  manager: StorageManagerLike | undefined,
): Promise<StorageStatus> {
  if (manager === undefined || typeof manager.persist !== "function") {
    return { persistState: "unsupported", usage: null, quota: null };
  }

  let persistState: PersistState = "denied";
  if (typeof manager.persisted === "function") {
    try {
      persistState = (await manager.persisted()) ? "granted" : "denied";
    } catch {
      persistState = "denied";
    }
  }

  let usage: number | null = null;
  let quota: number | null = null;
  if (typeof manager.estimate === "function") {
    try {
      const estimate = await manager.estimate();
      usage = typeof estimate.usage === "number" ? estimate.usage : null;
      quota = typeof estimate.quota === "number" ? estimate.quota : null;
    } catch {
      // 추정 실패는 무시한다("알 수 없음"으로 표시된다).
    }
  }

  return { persistState, usage, quota };
}

/** 남은 여유 비율(0~1). 알 수 없으면 null. */
export function freeRatio(status: StorageStatus): number | null {
  if (status.quota === null || status.usage === null || status.quota <= 0) return null;
  return Math.max(0, 1 - status.usage / status.quota);
}

/**
 * 저장소 여유가 부족한가(경고 배지 조건). 알 수 없으면 경고하지 않는다(거짓 경보 금지).
 *
 * ⚠️ `1 - usage/quota < 0.1`로 쓰면 안 된다 — 900/1000에서 `0.09999999999999998`이 나와
 *    **정확히 임계값인 경우가 경고로 넘어간다**. 바이트 정수끼리 비교해 오차를 없앤다.
 */
export function isStorageLow(status: StorageStatus): boolean {
  if (status.quota === null || status.usage === null || status.quota <= 0) return false;
  return status.quota - status.usage < status.quota * LOW_STORAGE_THRESHOLD;
}

/** 진단 화면 문구(정직한 고지 — 00 §3.2). */
export function describePersistState(state: PersistState): string {
  switch (state) {
    case "granted":
      return "영속 승인됨";
    case "denied":
      return "미승인 — 장기 미사용 시 삭제될 수 있음";
    case "unsupported":
      return "미지원 — 장기 미사용 시 삭제될 수 있음";
    default:
      return "알 수 없음";
  }
}
