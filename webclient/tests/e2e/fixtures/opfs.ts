import type { Page } from "@playwright/test";

/**
 * OPFS 관측 — `page.evaluate` 기반 (설계 §4.7)
 *
 * 앱은 **메인 스레드에서 OPFS를 쓰지 않는다**(VF-14 — Safari에 `createWritable()`이 없다).
 * 하지만 **테스트가 읽는 것은 무방**하다. 이것이 E8(보관이 업로드보다 먼저)·E19·E21의 관측 수단이다.
 *
 * ⚠️ 여기서 **쓰지 않는다.** 쓰기 경로를 흉내내면 앱의 Worker 프로토콜과 두 벌이 된다.
 */

/**
 * 디렉터리의 직속 자식 이름(정렬). 디렉터리 이름에는 `/`를 붙인다.
 * **경로가 없으면 빈 배열**이다 — "아직 만들어지지 않았다"와 "비어 있다"를 같게 다룬다
 * (E19·E21의 단언은 둘 다 통과여야 한다).
 */
export function listOpfs(page: Page, path: string): Promise<string[]> {
  return page.evaluate(async (target: string) => {
    // `entries()`는 TS DOM lib 선언에 없다(런타임에는 있다) — 앱 코드와 같은 방식으로 좁힌다.
    type DirEntries = { entries(): AsyncIterable<[string, FileSystemHandle]> };

    let dir: FileSystemDirectoryHandle;
    try {
      dir = await navigator.storage.getDirectory();
      for (const segment of target.split("/").filter((s) => s.length > 0)) {
        dir = await dir.getDirectoryHandle(segment);
      }
    } catch {
      return [];
    }

    const out: string[] = [];
    for await (const [name, handle] of (dir as unknown as DirEntries).entries()) {
      out.push(handle.kind === "directory" ? `${name}/` : name);
    }
    return out.sort();
  }, path);
}

/** 파일 1개가 있고 크기가 0보다 큰가. */
export function fileExistsOpfs(page: Page, path: string): Promise<boolean> {
  return page.evaluate(async (target: string) => {
    const segments = target.split("/").filter((s) => s.length > 0);
    const fileName = segments.pop();
    if (fileName === undefined) return false;
    try {
      let dir = await navigator.storage.getDirectory();
      for (const segment of segments) {
        dir = await dir.getDirectoryHandle(segment);
      }
      const handle = await dir.getFileHandle(fileName);
      const file = await handle.getFile();
      return file.size > 0;
    } catch {
      return false;
    }
  }, path);
}
