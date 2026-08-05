import {
  buildCatalog,
  hasUnderscoreCacheConflict,
  hasUsableImage,
  serverFramesToCache,
  type CatalogSource,
} from "@domain/frames/frameCatalogPolicy";
import type { FrameCatalogProgress } from "@domain/frames/frameCatalogProgress";
import type { FrameTemplate } from "@domain/frames/types";
import { createFrameRepository, type FrameRepository } from "@adapters/http/frameRepository";
import { getFrameStore, type FrameStore } from "@adapters/storage/frameStore";
import { logger } from "@adapters/storage/logStore";
import { loadBundleFrames } from "./bundleFrames";
import { createFallbackFrame, ensureFallbackImageUrl } from "./fallbackFrame";
import { downloadFrameImage } from "./frameDownloader";

/**
 * 프레임 카탈로그 로더 — **단일 비행 + 진행 replay** (06 §6.1 · 03 §4.1, it20)
 *
 * 부트스트랩 prefetch와 화면 진입이 **하나의 작업을 공유**한다(중복 다운로드 0). 늦게 합류한
 * 구독자는 최근 진행 보고를 **즉시 replay** 받고, 취소는 **호출자별**이라 [기다리지 않고 시작]으로
 * 화면이 대기를 접어도 공유 작업은 계속 진행해 캐시를 완성한다.
 *
 * Windows `App/Services/FrameCatalogService.cs`가 참조 구현이다. `lock`은 단일 스레드라 불요지만
 * **JS 고유 함정 두 가지**(§4.2 함정 A·B)가 그 자리를 대신한다 — 아래 주석 참조.
 *
 * ⚠️ 어댑터 규약: `loadPublic`은 **reject하지 않는다**(취소 예외만 예외다).
 */

/** 호출자별 취소 신호. 상위는 이것과 그 밖의 실패를 **같은 갈래**로 다룬다(로그 문구만 구분). */
export class FrameLoadCancelledError extends Error {
  constructor(message = "프레임 로딩 대기가 취소되었습니다.") {
    super(message);
    this.name = "FrameLoadCancelledError";
  }
}

export interface FrameCatalogLoadOptions {
  /** 이 호출자만 취소한다. 공유 작업은 계속 진행해 캐시를 완성한다. */
  readonly signal?: AbortSignal;
  /** 진행 보고. 합류 즉시 최근 보고가 **동기 1회** replay된다. */
  readonly onProgress?: (progress: FrameCatalogProgress) => void;
}

/** 서버 목록에는 있으나 이미지를 가져오지 못한 프레임(카드는 보이되 **선택 불가**). */
export interface UnavailableFrame {
  readonly id: string;
  readonly name: string;
  /** 원격 URL — 썸네일만 `<img>`로 보여준다(canvas 오염은 합성에만 영향 — 06 §6). */
  readonly imageUrl: string;
}

export interface FrameCatalogResult {
  /** **선택 가능한** 프레임만(=`hasUsableImage` 통과). */
  readonly frames: readonly FrameTemplate[];
  readonly unavailable: readonly UnavailableFrame[];
  readonly source: CatalogSource;
}

export interface FrameCatalog {
  /** 공용 프레임. 동시 호출은 **한 작업을 공유**한다. */
  loadPublic(options?: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  /** 네트워크를 쓰지 않는 로컬 해석(캐시 → 번들 → fallback). **단일 비행에 합류하지 않는다.** */
  loadLocalOnly(): Promise<FrameCatalogResult>;
  /** 개인 로컬 프레임. **서버를 조회하지 않는다**(아래 §4.5 주석). */
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
}

export interface FrameCatalogDeps {
  readonly store: FrameStore;
  /** `getDefaultFrames`만 쓴다 — `getUserFrames`는 **의도적으로 계약에 없다**(아래 주석). */
  readonly repository: Pick<FrameRepository, "getDefaultFrames">;
  readonly download: (url: string) => Promise<Blob | null>;
  readonly bundle: () => Promise<FrameTemplate[]>;
  readonly fallback: () => Promise<FrameTemplate>;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * 공유 작업과 취소 신호를 겨룬다.
 *
 * ⚠️ **함정 B**: `Promise.race`의 패자는 영원히 pending이므로 abort 리스너가 그대로 남는다.
 *    어느 쪽이 이기든 `cleanup()`으로 리스너를 제거한다(구독 누적 방지).
 */
function raceAbort<T>(shared: Promise<T>, signal: AbortSignal): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const onAbort = (): void => {
      cleanup();
      reject(new FrameLoadCancelledError());
    };
    const cleanup = (): void => {
      signal.removeEventListener("abort", onAbort);
    };
    signal.addEventListener("abort", onAbort, { once: true });
    shared.then(
      (value) => {
        cleanup();
        resolve(value);
      },
      (err: unknown) => {
        cleanup();
        reject(err instanceof Error ? err : new Error(String(err)));
      },
    );
  });
}

export function createFrameCatalog(deps: FrameCatalogDeps): FrameCatalog {
  let inFlight: Promise<FrameCatalogResult> | null = null;
  const observers = new Set<(progress: FrameCatalogProgress) => void>();
  let lastProgress: FrameCatalogProgress = { phase: "ResolvingLocal" };

  /** 구독자 전원에게 알리고 replay용 스냅샷을 갱신한다. 구독자 예외가 로딩을 깨지 않는다. */
  function report(progress: FrameCatalogProgress): void {
    lastProgress = progress;
    for (const observer of [...observers]) {
      try {
        observer(progress);
      } catch (err) {
        logger.warn("프레임 진행 보고 실패(무시)", { reason: describe(err) });
      }
    }
  }

  /** 서버 프레임 1장을 CORS-clean하게 받아 OPFS+메타에 캐시한다. 실패는 `null`. */
  async function cacheOne(frame: FrameTemplate): Promise<FrameTemplate | null> {
    if (!hasUsableImage(frame)) return null;
    const bytes = await deps.download(frame.imageUrl);
    if (bytes === null) return null;
    return deps.store.cacheServerFrame(frame, bytes);
  }

  /** 4단 우선순위 조립 + 이미지 없는 프레임 제외. */
  async function resolveCatalog(
    localCache: readonly FrameTemplate[],
    fromServer: readonly FrameTemplate[],
    unavailable: readonly UnavailableFrame[],
  ): Promise<FrameCatalogResult> {
    let bundle: FrameTemplate[] = [];
    try {
      bundle = await deps.bundle();
    } catch (err) {
      logger.warn("번들 프레임 로드 실패 — 번들 0개로 진행", { reason: describe(err) });
    }

    const fallback = await deps.fallback();
    const catalog = buildCatalog({ localCache, server: fromServer, bundle, fallback });
    return {
      // 빈 URL 프레임을 목록에 올리면 손님이 6컷을 다 찍은 뒤 `Result`에서야 합성 실패를 만난다.
      frames: catalog.frames.filter(hasUsableImage),
      unavailable,
      source: catalog.source,
    };
  }

  /** 공유 작업 본체(§4.3). 개별 호출자가 취소하지 않으므로 전 구간 취소 신호를 쓰지 않는다. */
  async function loadCore(): Promise<FrameCatalogResult> {
    report({ phase: "ResolvingLocal" });
    const local = await deps.store.listPublic();

    const cached: FrameTemplate[] = [];
    const unavailable: UnavailableFrame[] = [];

    try {
      report({ phase: "QueryingServer" });
      // 게이트 키만 필요하다(게스트도 조회 가능) — Bearer가 붙지 않으므로 401 세션 해제 위험이 없다.
      const server = await deps.repository.getDefaultFrames();
      const localNames = new Set(local.map((frame) => frame.name));
      const pending = serverFramesToCache(localNames, server);

      for (let i = 0; i < pending.length; i++) {
        const frame = pending[i]!;
        // 분모는 `pending.length`다(로컬 캐시 히트를 뺀 수) — 정직한 카운터.
        report({ phase: "DownloadingImage", index: i + 1, total: pending.length });
        if (hasUnderscoreCacheConflict(frame)) {
          logger.warn("공용 프레임 이름에 '_'가 있어 매 실행 재다운로드됩니다", { name: frame.name });
        }
        const stored = await cacheOne(frame);
        if (stored !== null) cached.push(stored);
        else unavailable.push({ id: frame.id, name: frame.name, imageUrl: frame.imageUrl });
      }
    } catch (err) {
      // ⚠️ 서버 조회·다운로드 실패는 **여기서 삼킨다** → `waitInterrupted=false` → `Ready`(E20).
      //    이 catch를 지우거나 rethrow로 바꾸면 **오프라인 부스가 매 진입마다 안내를 띄운다.**
      //    "즉시 실패(오프라인)"와 "잘라낸 대기(상한 초과)"를 가르는 유일한 축이 이것이다.
      logger.warn("기본 프레임 서버 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)", {
        reason: describe(err),
      });
    }

    report({ phase: "Completed" });
    return resolveCatalog(local, cached, unavailable);
  }

  /**
   * ⚠️ **절대 reject하지 않는다.** race의 패자가 unhandled rejection을 만들지 않게 하는 성질이며,
   *    합류한 다른 호출자가 한 명의 실패로 함께 죽지 않게 하는 성질이기도 하다.
   */
  async function runSharedPass(): Promise<FrameCatalogResult> {
    try {
      return await loadCore();
    } catch (err) {
      logger.error("프레임 카탈로그 로딩 실패 — 빈 결과로 축퇴", { reason: describe(err) });
      return { frames: [], unavailable: [], source: "Fallback" };
    }
  }

  async function awaitShared(
    shared: Promise<FrameCatalogResult>,
    onProgress: ((progress: FrameCatalogProgress) => void) | undefined,
    signal: AbortSignal | undefined,
  ): Promise<FrameCatalogResult> {
    try {
      if (signal === undefined) return await shared;
      if (signal.aborted) throw new FrameLoadCancelledError();
      return await raceAbort(shared, signal);
    } finally {
      // 구독 제거 경로는 이 finally **한 곳**이다(취소·예외·정상 완료 모두 통과) → 누적되지 않는다.
      if (onProgress !== undefined) observers.delete(onProgress);
    }
  }

  function loadPublic(options: FrameCatalogLoadOptions = {}): Promise<FrameCatalogResult> {
    const { signal, onProgress } = options;

    // ⚠️ 새 패스를 시작하는 호출자에게 이전 패스의 마지막 국면(`Completed` = "정리하는 중…")을
    //    replay하면 안 된다 — 홈 왕복 후 재진입마다 첫 문구가 거짓이 된다.
    if (inFlight === null) lastProgress = { phase: "ResolvingLocal" };
    const snapshot = lastProgress;

    if (onProgress !== undefined) observers.add(onProgress);

    if (inFlight === null) {
      const task = runSharedPass();
      inFlight = task;
      // ⚠️ **함정 A**: `finally`를 task 내부(`inFlight ??= (async () => { try … finally … })()`)에
      //    두면, 첫 await 이전에 동기 throw가 났을 때 정리가 `inFlight = task` 대입보다 **먼저**
      //    일어나 이미 해결된 promise가 영구히 남는다(그 뒤로는 캐시가 갱신돼도 재조회가 없다).
      //    바깥에서 붙이고 **동일성으로 가드**한다.
      void task.finally(() => {
        if (inFlight === task) inFlight = null;
      });
    }
    const shared = inFlight;

    // 문구 공백 구간 제거(합류 즉시 표시). ⚠️ 구독자 예외가 **로딩 자체를 깨지 않게** 감싼다 —
    // 이 replay는 동기 호출이라 감싸지 않으면 `loadPublic`이 그대로 던진다(`report`와 같은 방어).
    if (onProgress !== undefined) {
      try {
        onProgress(snapshot);
      } catch (err) {
        logger.warn("프레임 진행 replay 실패(무시)", { reason: describe(err) });
      }
    }
    return awaitShared(shared, onProgress, signal);
  }

  /**
   * ⚠️ **단일 비행에 합류하지 않는다.** 방금 상한을 넘긴 그 작업을 다시 기다리면 상한이 무의미해진다.
   *    백엔드를 **한 번도** 부르지 않는다(번들 매니페스트는 same-origin 정적 자산이라 허용).
   */
  async function loadLocalOnly(): Promise<FrameCatalogResult> {
    const local = await deps.store.listPublic();
    return resolveCatalog(local, [], []);
  }

  /**
   * ⚠️ **`frameRepository.getUserFrames`를 부르지 않는다.** ① 정책상 개인 커스텀 프레임은 서버에
   *    올라가지 않아 보통 빈 배열이고(analysis/41 §3) ② 그 호출은 `auth:"required"`라 토큰 만료 시
   *    401 → `handleSessionExpired` → **프레임 목록을 여는 것만으로 로그아웃 토스트**가 뜬다.
   *    얻는 것이 빈 배열인데 잃는 것이 세션이다.
   */
  async function loadPersonal(userId: string): Promise<readonly FrameTemplate[]> {
    const frames = await deps.store.listPersonal(userId);
    return frames.filter(hasUsableImage);
  }

  return { loadPublic, loadLocalOnly, loadPersonal };
}

let singleton: FrameCatalog | null = null;

/** 앱 전역 카탈로그. 싱글턴이어야 단일 비행이 성립한다(인스턴스가 늘면 중복 다운로드가 돌아온다). */
export function getFrameCatalog(): FrameCatalog {
  singleton ??= createFrameCatalog({
    store: getFrameStore(),
    repository: createFrameRepository(),
    download: downloadFrameImage,
    bundle: loadBundleFrames,
    fallback: async () => createFallbackFrame(await ensureFallbackImageUrl(), new Date().toISOString()),
  });
  return singleton;
}

export function setFrameCatalogForTests(catalog: FrameCatalog | null): void {
  singleton = catalog;
}
