import { env, envWarnings, versionCaption } from "../env";
import {
  applyBrandingToDocument,
  DEFAULT_BRANDING,
  loadBranding,
  type Branding,
} from "@adapters/platform/branding";
import {
  requestPersistentStorage,
  type StorageStatus,
} from "@adapters/platform/persistStorage";
import {
  attachLogStore,
  createIndexedDbLogSink,
  createLogStore,
  createMemoryLogSink,
  logger,
  type LogStore,
} from "@adapters/storage/logStore";
import { getOpfsClient, purgeSessionLeftovers, type OpfsClient } from "@adapters/storage/opfsClient";
import type { OpfsWriteCapability } from "@adapters/storage/opfsProtocol";
import { createSettingsRepo, type SettingsRepo } from "@adapters/storage/settingsRepo";
import { attachSettingsRepo, useSettingsStore } from "./settingsStore";

/**
 * 부트스트랩 — 01 §4.2의 순서가 **규격**이다.
 *
 *   1 env 검증·정규화
 *   2 로그 스토어 초기화(이후 모든 단계가 로깅 가능해야 한다)
 *   3 /branding.json fetch (800ms 타임아웃 → 기본값)        ← 첫 렌더 전
 *   4 설정 로드 + clamp (손상 시 기본값 + 경고)
 *   5 navigator.storage.persist() 요청
 *   6 OPFS sessions/ 잔재 일괄 삭제                          ← analysis/41 §4 규격
 *   7 Service Worker 등록 (Step 16)
 *   8 전역 예외 핸들러 설치 (Step 4 — M16)
 *   9 OAuth 콜백 경로 처리 (Step 12)
 *  10 React 마운트
 *  11 첫 제스처에서 전체화면·오디오·Wake Lock (Step 4)
 *
 * ⚠️ 순서를 바꾸면 깨지는 것들:
 *   - 2를 뒤로 미루면 1·3·4의 경고가 사라진다(운영자가 오구성을 못 본다).
 *   - 3을 렌더 뒤로 미루면 홈 타이틀이 기본값으로 번쩍인다.
 *   - 6을 빼면 임시 파일이 무한 누적된다(규격 위반).
 */

export interface BootstrapResult {
  readonly branding: Branding;
  readonly storage: StorageStatus;
  readonly opfsCapability: OpfsWriteCapability;
  /** 정리된 세션 잔재 폴더 수. */
  readonly purgedSessions: number;
  readonly logStore: LogStore;
  readonly settingsRepo: SettingsRepo | null;
}

export interface BootstrapDeps {
  readonly storageManager?: typeof navigator.storage;
  readonly localStorage?: Storage;
  readonly opfs?: OpfsClient;
  readonly fetchImpl?: typeof fetch;
  readonly doc?: Document;
  readonly mirrorLogsToConsole?: boolean;
}

export async function bootstrap(deps: BootstrapDeps = {}): Promise<BootstrapResult> {
  // ── 1. env (모듈 로드 시 이미 정규화됨). 경고는 2단계 직후 흘린다. ──
  const doc = deps.doc ?? (typeof document !== "undefined" ? document : undefined);

  // ── 2. 로그 스토어 ──
  const sink = (await createIndexedDbLogSink()) ?? createMemoryLogSink();
  const logStore = createLogStore({
    sink,
    mirrorToConsole: deps.mirrorLogsToConsole ?? import.meta.env.DEV === true,
  });
  attachLogStore(logStore);

  logger.info(`앱 시작 ${versionCaption(env.appVersion)}`, {
    buildDate: env.buildDate,
    backendBaseUrl: env.backendBaseUrl,
    hostingBaseUrl: env.hostingBaseUrl,
    storageBucket: env.storageBucket,
    // 게이트 키·client id 값은 남기지 않는다 — 설정됨 여부만(analysis/41 §8).
    gateKeyConfigured: env.backendApiKey.length > 0,
    googleClientIdConfigured: env.googleClientId.length > 0,
  });
  for (const warning of envWarnings) logger.warn(warning);

  // ── 3. 브랜딩(첫 렌더 전) ──
  const brandingResult = await loadBranding(deps.fetchImpl ?? fetch);
  if (brandingResult.usedFallback) {
    logger.warn("브랜딩 기본값 사용", { reason: brandingResult.reason ?? "값 없음" });
  } else {
    logger.info("브랜딩 로드", { appName: brandingResult.branding.appName });
  }
  if (doc !== undefined) applyBrandingToDocument(brandingResult.branding, doc);

  // ── 4. 설정 로드 + clamp ──
  let settingsRepo: SettingsRepo | null = null;
  const storage = deps.localStorage ?? safeLocalStorage();
  if (storage !== null) {
    settingsRepo = createSettingsRepo(storage, {
      backendBaseUrl: env.backendBaseUrl,
      hostingBaseUrl: env.hostingBaseUrl,
      storageBucket: env.storageBucket,
      googleClientId: env.googleClientId,
    });
    attachSettingsRepo(settingsRepo);

    const loaded = settingsRepo.load();
    for (const warning of loaded.warnings) logger.warn(`설정: ${warning}`);
    useSettingsStore.getState().hydrate(loaded.values, loaded.webExtras);
    logger.info("설정 로드", {
      firstRun: loaded.firstRun,
      cutCount: loaded.values.CutCount,
      isAutoCutCount: loaded.values.CutCount === 0,
      countdownSec: loaded.values.CountdownSec,
      mirrorMode: loaded.values.MirrorMode,
      enableQrDelivery: loaded.values.EnableQrDelivery,
      saveLocalCopy: loaded.values.SaveLocalCopy,
    });
  } else {
    logger.error("설정을 저장·복원할 수 없습니다(localStorage 사용 불가). 기본값으로 동작합니다.");
  }

  // ── 5. 저장소 영속 요청 ──
  const storageStatus = await requestPersistentStorage(
    deps.storageManager ?? (typeof navigator !== "undefined" ? navigator.storage : undefined),
  );
  logger.info("저장소 영속 상태", {
    persistState: storageStatus.persistState,
    usage: storageStatus.usage,
    quota: storageStatus.quota,
  });

  // ── 6. OPFS 세션 잔재 정리(sessions/ 만) ──
  const opfs = deps.opfs ?? getOpfsClient();
  const opfsCapability = await opfs.capability();
  if (opfsCapability === "none") {
    logger.warn("OPFS에 쓸 수 없습니다 — 결과물 로컬 보관이 불가합니다(업로드만 가능).");
  }
  const purgedSessions = await purgeSessionLeftovers(opfs);
  logger.info("세션 잔재 정리", { purgedSessions, opfsCapability });

  // 7~9·11단계는 Step 4·12·16에서 이 함수 뒤에 이어 붙는다(main.tsx 참조).

  return {
    branding: brandingResult.branding,
    storage: storageStatus,
    opfsCapability,
    purgedSessions,
    logStore,
    settingsRepo,
  };
}

/** 프라이빗 모드·차단 설정에서 `localStorage` 접근 자체가 던질 수 있다. */
function safeLocalStorage(): Storage | null {
  try {
    if (typeof localStorage === "undefined") return null;
    const probe = "__mcphoto_probe__";
    localStorage.setItem(probe, "1");
    localStorage.removeItem(probe);
    return localStorage;
  } catch {
    return null;
  }
}

/** 브랜딩 기본값(부트스트랩 실패 시 화면이 쓸 값). */
export { DEFAULT_BRANDING };
