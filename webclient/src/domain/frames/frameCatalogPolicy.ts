import type { FrameTemplate } from "./types";

/**
 * 프레임 카탈로그 우선순위·dedup — Windows `App/Services/FrameCatalogService.cs` 이식 (analysis/13 §5)
 *
 * 웹 우선순위(05 §4): ① 로컬 OPFS 캐시(공용) → ② 서버 공용(`GET /frames/default`) → ③ 번들 자산
 * → ④ 코드 생성 fallback. **이름 기준 dedup**이라 이미 캐시된 프레임은 재다운로드하지 않는다.
 *
 * ⚠️ 서버 미도달(오프라인)이면 ②만 건너뛴다 — 목록이 비지 않아야 한다(E20).
 */

/** 카탈로그가 실제로 채워진 출처(진단·로그 표시용). */
export const CATALOG_SOURCES = ["LocalCache", "Server", "Bundle", "Fallback"] as const;
export type CatalogSource = (typeof CATALOG_SOURCES)[number];

/**
 * 서버 프레임 중 **다운로드해야 할 것**만 고른다(이름이 로컬 캐시에 없는 것).
 * 두 번째 진입에서 재다운로드가 발생하면 이 판정이 깨진 것이다(Step 14 완료 기준).
 */
export function serverFramesToCache(
  localPublicNames: ReadonlySet<string>,
  serverFrames: readonly FrameTemplate[],
): FrameTemplate[] {
  return serverFrames.filter((f) => !localPublicNames.has(f.name));
}

/**
 * 이름 기준 dedup — **먼저 온 것이 이긴다**(우선순위가 높은 출처가 앞에 온다).
 * 비교는 정확 일치(Ordinal)다.
 */
export function dedupeByName(frames: readonly FrameTemplate[]): FrameTemplate[] {
  const seen = new Set<string>();
  const result: FrameTemplate[] = [];
  for (const frame of frames) {
    if (seen.has(frame.name)) continue;
    seen.add(frame.name);
    result.push(frame);
  }
  return result;
}

/**
 * 목록에 올릴 수 있는 프레임인지 — **이미지 URL이 실제로 있는가**만 본다.
 *
 * 웹 전용 판정이다: 이미지는 object URL·OPFS 캐시·서버 URL 중 하나로 **늦게** 채워지고,
 * 생성·다운로드가 실패하면 어댑터는 예외 대신 **빈 문자열**을 돌려준다(어댑터 규약 — 15 §2).
 * 빈 URL 프레임을 목록에 올리면 손님이 **6컷을 다 찍은 뒤 `Result`에서야** 합성 실패를 만난다.
 * 여기서 걸러 "프레임 0개 = `Failed`"(03 §4.1)로 **선택 화면에서** 끝낸다.
 */
export function hasUsableImage(frame: FrameTemplate): boolean {
  return frame.imageUrl.trim().length > 0;
}

export interface CatalogInput {
  /** ① OPFS에 캐시된 공용 프레임. */
  readonly localCache: readonly FrameTemplate[];
  /** ② 서버에서 새로 받은 공용 프레임(캐시에 없던 것만). 서버 미도달이면 빈 배열. */
  readonly server: readonly FrameTemplate[];
  /** ③ 번들 자산. */
  readonly bundle: readonly FrameTemplate[];
  /** ④ 코드 생성 fallback(항상 1개 준비된다). */
  readonly fallback: FrameTemplate;
  /** 로그인 사용자의 개인 로컬 프레임(공용 뒤에 붙는다). 게스트는 빈 배열. */
  readonly personal?: readonly FrameTemplate[];
}

export interface CatalogResult {
  readonly frames: readonly FrameTemplate[];
  /** 공용 목록이 어디서 채워졌는가. */
  readonly source: CatalogSource;
}

/**
 * 카탈로그 조립. 공용은 ①+② → 비면 ③ → 비면 ④ 순으로 채우고, 그 뒤에 개인 프레임을 붙인다.
 * 개인 프레임은 공용 목록을 대체하지 않으므로 `source` 판정에 영향을 주지 않는다.
 */
export function buildCatalog(input: CatalogInput): CatalogResult {
  const merged = dedupeByName([...input.localCache, ...input.server]);
  const personal = dedupeByName(input.personal ?? []);

  if (merged.length > 0) {
    return {
      frames: [...merged, ...personal],
      source: input.localCache.length > 0 ? "LocalCache" : "Server",
    };
  }

  const bundle = dedupeByName(input.bundle);
  if (bundle.length > 0) {
    return { frames: [...bundle, ...personal], source: "Bundle" };
  }

  return { frames: [input.fallback, ...personal], source: "Fallback" };
}

/**
 * 이름에 `_`가 있는 **공용** 프레임은 로컬 공용 파일명 규약(`{계정}_` = 개인 접두)과 충돌해
 * dedup 집합에 오르지 못하고 **매 실행 재다운로드**된다. 동작은 유지하고 경고만 남긴다(analysis/13 §5).
 */
export function hasUnderscoreCacheConflict(frame: FrameTemplate): boolean {
  return frame.name.includes("_");
}
