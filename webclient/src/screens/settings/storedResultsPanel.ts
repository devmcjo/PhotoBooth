import type { ResultFolderUsage } from "@domain/results/resultsRetention";
import {
  isStorageLow,
  readStorageStatus,
  type StorageStatus,
} from "@adapters/platform/persistStorage";
import { getResultsStore, type ResultsUsage } from "@adapters/storage/resultsStore";
import { logger } from "@adapters/storage/logStore";

/**
 * [보관된 결과물] 패널 — Step 10 이월분 (05 §5.4 · 03 §12.1)
 *
 * ⚠️ **`resultsStore` 위에 얹기만 한다.** 새 저장소 코드를 쓰지 않는다 — 메인 스레드에서
 *    OPFS를 직접 만지면 iOS/iPadOS에서 전 저장 경로가 실패한다(VF-14).
 * ⚠️ 정리 대상은 OPFS `results/`뿐이다. ②(사용자 지정 폴더)는 건드리지 않는다 — 사용자의
 *    파일 시스템이고, 우리가 지울 권한을 가정해서는 안 된다.
 * ⚠️ 삭제 실패를 성공으로 위장하지 않는다(M4). 부분 실패는 개수를 그대로 보고한다.
 */

export interface StoredResultsView {
  readonly loading: boolean;
  readonly totalBytes: number;
  /** 이름 오름차순 = **오래된 순**(0 패딩 규약). `resultsStore.usage()`가 이미 정렬해 준다. */
  readonly folders: readonly ResultFolderUsage[];
  /** 여유 10% 미만 경고 배지(05 §5.4). 알 수 없으면 false(거짓 경보 금지). */
  readonly storageLow: boolean;
}

export const EMPTY_STORED_RESULTS: StoredResultsView = {
  loading: false,
  totalBytes: 0,
  folders: [],
  storageLow: false,
};

export interface StoredResultsDeps {
  readonly usage: () => Promise<ResultsUsage>;
  readonly removeFolder: (name: string) => Promise<boolean>;
  /** 저장소 여유 판정용. **요청하지 않고 조회만** 한다(설정 화면에서 프롬프트를 띄우지 않는다). */
  readonly storageStatus: () => Promise<StorageStatus | null>;
}

/** 목록·총량·여유 경고를 한 번에 읽는다. `resultsStore`는 실패를 빈 값으로 축소한다. */
export async function loadStoredResults(deps: StoredResultsDeps): Promise<StoredResultsView> {
  const [usage, status] = await Promise.all([deps.usage(), deps.storageStatus()]);
  return {
    loading: false,
    totalBytes: usage.totalBytes,
    folders: usage.folders,
    storageLow: status === null ? false : isStorageLow(status),
  };
}

/** 개별 삭제. `false`면 화면이 *"삭제하지 못했습니다."* 를 보여준다. */
export async function removeStoredResult(
  deps: StoredResultsDeps,
  name: string,
): Promise<boolean> {
  const ok = await deps.removeFolder(name);
  if (!ok) logger.warn("보관 결과물 삭제 실패", { folderName: name });
  return ok;
}

export interface RemoveAllOutcome {
  readonly removed: number;
  readonly failed: number;
}

/** 전체 삭제. 실패는 개수에 세지 않고 **정직하게** 함께 보고한다. */
export async function removeAllStoredResults(
  deps: StoredResultsDeps,
  names: readonly string[],
): Promise<RemoveAllOutcome> {
  let removed = 0;
  let failed = 0;
  for (const name of names) {
    // 순차 실행이다 — OPFS Worker가 단일 채널이라 병렬로 보내도 이득이 없고 실패 원인만 섞인다.
    if (await deps.removeFolder(name)) removed++;
    else failed++;
  }
  logger.info("보관 결과물 전체 삭제", { removed, failed });
  return { removed, failed };
}

/** 결과 안내 문구. 부분 실패를 감추지 않는다. */
export function describeRemoveAll(outcome: RemoveAllOutcome): string {
  if (outcome.failed === 0) return `${outcome.removed}개를 삭제했습니다.`;
  return `${outcome.removed}개를 삭제했고 ${outcome.failed}개는 실패했습니다.`;
}

/** 실제 배선. 싱글턴은 **호출 시점**에 해석한다(모듈 로드 부작용 0). */
export function defaultStoredResultsDeps(
  overrides: Partial<StoredResultsDeps> = {},
): StoredResultsDeps {
  return {
    usage: () => getResultsStore().usage(),
    removeFolder: (name) => getResultsStore().removeFolder(name),
    // ⚠️ **조회만** 한다. `requestPersistentStorage`를 쓰면 화면을 여는 것만으로 권한 창이 뜬다.
    storageStatus: () =>
      readStorageStatus(typeof navigator === "undefined" ? undefined : navigator.storage),
    ...overrides,
  };
}
