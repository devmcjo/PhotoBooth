/**
 * OPFS Worker 메시지 프로토콜 — 05 §3.1
 *
 * **모든 OPFS 쓰기는 이 프로토콜을 통해 Worker에서 일어난다.**
 * 이유: `createSyncAccessHandle()`은 전용 Worker 전용 API이고, **Safari 17에는 `createWritable()`이 없다**.
 * 메인 스레드에서 `createWritable()`을 먼저 시도하는 구조로 만들면 **iOS/iPadOS에서 전 저장 경로가 실패**한다
 * (M6-W 파손 → E8 실패). 이 파일은 순수 타입·경로 유틸이라 Worker와 메인 양쪽이 함께 쓴다.
 */

export type OpfsRequest =
  | { readonly id: number; readonly op: "write"; readonly path: string; readonly bytes: ArrayBuffer }
  | { readonly id: number; readonly op: "remove"; readonly path: string; readonly recursive: boolean }
  | { readonly id: number; readonly op: "list"; readonly path: string }
  | { readonly id: number; readonly op: "exists"; readonly path: string }
  | { readonly id: number; readonly op: "probe" };

/**
 * id를 뺀 요청(클라이언트가 id를 붙인다).
 * ⚠️ `Omit<OpfsRequest, "id">`를 쓰면 유니온이 **하나의 객체로 뭉개져** `path` 같은 분기별 필드가 사라진다.
 *    조건부 타입으로 분배(distributive)해야 각 변형이 유지된다.
 */
export type OpfsRequestWithoutId = OpfsRequest extends infer T
  ? T extends { id: number }
    ? Omit<T, "id">
    : never
  : never;

export type OpfsResponse =
  | { readonly id: number; readonly ok: true; readonly value?: unknown }
  | { readonly id: number; readonly ok: false; readonly error: string };

/** Worker가 보고하는 쓰기 능력. `none`이면 OPFS 미지원으로 취급한다(10 §6.2 축소 동작). */
export type OpfsWriteCapability = "sync-access-handle" | "writable-stream" | "none";

/**
 * 경로를 세그먼트로 나눈다. 빈 세그먼트·`.`·`..`를 **거부**한다 —
 * 경로 조작으로 OPFS 루트 밖(다른 앱 데이터)을 건드리지 못하게 하는 경계 방어다.
 */
export function splitOpfsPath(path: string): string[] {
  const segments = path.split("/").filter((s) => s.length > 0);
  if (segments.length === 0) throw new Error(`빈 OPFS 경로: ${JSON.stringify(path)}`);
  for (const segment of segments) {
    if (segment === "." || segment === "..") {
      throw new Error(`OPFS 경로에 상대 참조를 쓸 수 없습니다: ${path}`);
    }
  }
  return segments;
}

/** 마지막 세그먼트를 파일명으로, 앞을 디렉터리로 분리한다. */
export function splitParentAndName(path: string): { dirs: string[]; name: string } {
  const segments = splitOpfsPath(path);
  const name = segments[segments.length - 1]!;
  return { dirs: segments.slice(0, -1), name };
}

/** OPFS 상단 디렉터리 규약(05 §3·§4·§5). 잔재 정리는 `sessions/`만 건드린다. */
export const OPFS_DIRS = {
  sessions: "sessions",
  results: "results",
  frames: "frames",
} as const;
