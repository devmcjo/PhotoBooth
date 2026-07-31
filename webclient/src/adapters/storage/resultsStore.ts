import { isResultFolderName } from "@domain/results/resultNaming";
import {
  planResultsRetention,
  type ResultFolderUsage,
  type ResultsRetentionLimits,
} from "@domain/results/resultsRetention";
import { getOpfsClient, type OpfsClient } from "./opfsClient";
import { logger } from "./logStore";
import { OPFS_DIRS } from "./opfsProtocol";

/**
 * 보관 결과물 라이브러리 — 05 §5.1 ① · §5.4
 *
 * `results/`는 촬영 결과물의 **영구 보관 위치**다(잔재 정리 대상이 아니다 — analysis/41 §4).
 * 목록·용량·삭제·읽기를 한 곳에 모아 Step 13 [보관된 결과물] 패널과 Step 16 진단이 그대로 쓴다.
 *
 * ⚠️ 이 파일은 저장소를 **직접** 만지지 않는다. 모든 쓰기·삭제·열거가 `OpfsClient`(전용 Worker RPC)를
 *    지나야 한다 — 메인 스레드에서 직접 쓰면 iOS/iPadOS에서 전 저장 경로가 실패한다(VF-14).
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. `false`·빈 값·`null`로 축소하고 상위가 상태로 표현한다.
 */

export interface ResultsUsage {
  readonly totalBytes: number;
  /** 폴더별 용량(이름 오름차순 = 오래된 순). */
  readonly folders: readonly ResultFolderUsage[];
}

export interface ResultsStore {
  /** 보관 폴더명(오름차순 = 오래된 순). 실패는 `[]`. */
  listFolders(): Promise<string[]>;
  /** 폴더별 용량 + 총량. 실패는 `{ totalBytes: 0, folders: [] }`. */
  usage(): Promise<ResultsUsage>;
  /** 폴더 재귀 삭제. **규약 밖 이름은 거부하고 `false`**. */
  removeFolder(name: string): Promise<boolean>;
  /** 보관본 파일 읽기(Step 13 내보내기·미리보기). 메인 스레드 읽기라 Worker 왕복이 없다. */
  readFile(folderName: string, fileName: string): Promise<File | null>;
  /** 보존 정책 집행. **삭제된 폴더 수**를 돌려준다(실패·불필요는 0). */
  enforceRetention(limits?: ResultsRetentionLimits): Promise<number>;
}

/** 경로 조작 2차 방어. `splitOpfsPath`가 `..`를 막지만 경계에서 한 번 더 좁힌다. */
function isSafeFileName(name: string): boolean {
  return name.length > 0 && !name.includes("/") && name !== "." && name !== "..";
}

function byNameAscending(a: string, b: string): number {
  // 0 패딩 규약이라 사전순 = 시간순이다. `localeCompare`는 로케일·ICU에 따라 흔들린다.
  return a < b ? -1 : a > b ? 1 : 0;
}

export function createResultsStore(client: OpfsClient): ResultsStore {
  async function listFolders(): Promise<string[]> {
    try {
      const names = await client.list(OPFS_DIRS.results);
      return [...names].sort(byNameAscending);
    } catch {
      return [];
    }
  }

  async function usage(): Promise<ResultsUsage> {
    try {
      const raw = await client.usage(OPFS_DIRS.results);
      const folders = raw.entries
        .filter((entry) => entry.kind === "directory")
        .map((entry) => ({ name: entry.name, bytes: entry.bytes }))
        .sort((a, b) => byNameAscending(a.name, b.name));
      return { totalBytes: raw.totalBytes, folders };
    } catch {
      return { totalBytes: 0, folders: [] };
    }
  }

  async function removeFolder(name: string): Promise<boolean> {
    if (!isResultFolderName(name)) {
      // 우리 규약 밖 이름은 남의 데이터일 수 있다 — 이름 규약이 두 번째 방어선이다.
      logger.warn("보관 결과물 삭제 거부(규약 밖 이름)", { folderName: name });
      return false;
    }
    try {
      return await client.remove(`${OPFS_DIRS.results}/${name}`, { recursive: true });
    } catch {
      return false;
    }
  }

  async function readFile(folderName: string, fileName: string): Promise<File | null> {
    if (!isResultFolderName(folderName) || !isSafeFileName(fileName)) return null;
    try {
      return await client.readFile(`${OPFS_DIRS.results}/${folderName}/${fileName}`);
    } catch {
      return null;
    }
  }

  async function enforceRetention(limits?: ResultsRetentionLimits): Promise<number> {
    try {
      const current = await usage();
      const decision = planResultsRetention(current.folders, limits);
      if (decision.remove.length === 0) {
        if (decision.stillOverLimit) {
          logger.warn("보관 결과물이 한도를 넘었지만 지울 대상이 없습니다", {
            keptCount: decision.keptCount,
            keptBytes: decision.keptBytes,
            triggers: decision.triggers.join(","),
          });
        }
        return 0;
      }

      let removed = 0;
      for (const name of decision.remove) {
        // 실패는 개수에 세지 않는다(정직한 보고 — `purgeSessionLeftovers`와 같은 방식).
        if (await removeFolder(name)) removed++;
      }
      logger.info("보관 결과물 정리", {
        removed,
        keptCount: decision.keptCount,
        keptBytes: decision.keptBytes,
        triggers: decision.triggers.join(","),
        stillOverLimit: decision.stillOverLimit,
      });
      return removed;
    } catch {
      return 0;
    }
  }

  return { listFolders, usage, removeFolder, readFile, enforceRetention };
}

let singleton: ResultsStore | null = null;

export function getResultsStore(): ResultsStore {
  singleton ??= createResultsStore(getOpfsClient());
  return singleton;
}

export function setResultsStoreForTests(store: ResultsStore | null): void {
  singleton = store;
}
