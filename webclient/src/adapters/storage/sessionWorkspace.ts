import { OPFS_DIRS } from "./opfsProtocol";
import type { OpfsClient } from "./opfsClient";

/**
 * 세션 작업 공간 — OPFS `sessions/{sessionId}/` (WD14 · 05 §3)
 *
 * 컷·합성물·타임랩스 스풀을 세션 폴더에 두고 **세션 종료·홈 복귀 시 폴더를 삭제**한다.
 * 결과물 보관(`results/`)은 별 디렉터리이며 이 폴더가 지워져도 남는다(M6-W).
 *
 * ⚠️ 모든 쓰기는 `OpfsClient`(Worker 경계)를 지난다. 여기서 OPFS API를 직접 부르지 않는다.
 */

export interface SessionWorkspace {
  readonly sessionId: string;
  /** 컷 스틸 기록. 실패 시 `false`(성공 오인 금지 — M4). */
  writeCut(index: number, bytes: Blob | Uint8Array): Promise<boolean>;
  /** 타임랩스 스풀 프레임 기록(`tl/` 하위). */
  writeTimelapseFrame(index: number, bytes: Blob | Uint8Array): Promise<boolean>;
  /** 스풀 프레임 목록(정렬된 파일명). */
  listTimelapseFrames(): Promise<string[]>;
  /** 스풀 프레임 1개 삭제(솎아내기). */
  removeTimelapseFrame(name: string): Promise<boolean>;
  /** 합성 결과 기록(세션 작업용 — 보관본은 `resultSaver`가 `results/`에 쓴다). */
  writeComposed(fileName: string, bytes: Blob | Uint8Array): Promise<boolean>;
  readFile(relativePath: string): Promise<File | null>;
  /** 세션 폴더 전체 삭제. 홈 복귀·완료·유휴 만료 시 호출한다. */
  discard(): Promise<boolean>;
}

/** 컷 파일명 규약. Windows 결과물 파일명 규약과 별개인 **작업 파일**이다. */
export function cutFileName(index: number): string {
  return `cut${index}.jpg`;
}

/** 타임랩스 스풀 파일명. 0 패딩으로 **문자열 정렬 = 시간 정렬**을 보장한다. */
export function timelapseFrameName(index: number): string {
  return `${String(index).padStart(5, "0")}.jpg`;
}

/**
 * 타임랩스 스풀 디렉터리(OPFS 절대 경로).
 * 인코딩 Worker가 이 경로로 디렉터리 핸들을 직접 열어 **읽는다**(쓰기는 여전히 `opfsWriter` 전용).
 * 경로 정의를 한 곳에 두려고 `createSessionWorkspace` 내부도 이 함수를 쓴다.
 */
export function timelapseDirPath(sessionId: string): string {
  return `${OPFS_DIRS.sessions}/${sessionId}/tl`;
}

export function createSessionWorkspace(client: OpfsClient, sessionId: string): SessionWorkspace {
  const root = `${OPFS_DIRS.sessions}/${sessionId}`;
  const timelapseDir = timelapseDirPath(sessionId);

  return {
    sessionId,

    writeCut(index, bytes) {
      return client.write(`${root}/${cutFileName(index)}`, bytes);
    },

    writeTimelapseFrame(index, bytes) {
      return client.write(`${timelapseDir}/${timelapseFrameName(index)}`, bytes);
    },

    async listTimelapseFrames() {
      const names = await client.list(timelapseDir);
      return names.filter((n) => n.endsWith(".jpg")).sort();
    },

    removeTimelapseFrame(name) {
      return client.remove(`${timelapseDir}/${name}`);
    },

    writeComposed(fileName, bytes) {
      return client.write(`${root}/${fileName}`, bytes);
    },

    readFile(relativePath) {
      return client.readFile(`${root}/${relativePath}`);
    },

    discard() {
      return client.remove(root, { recursive: true });
    },
  };
}
