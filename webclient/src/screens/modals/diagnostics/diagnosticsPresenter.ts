import { formatBytes } from "@domain/results/byteFormat";
import type {
  CameraSettings,
  CameraState,
  ProcessedSize,
} from "@adapters/camera/cameraTypes";
import { displayLabel, type CameraDevice } from "@adapters/camera/deviceEnumerator";
import type { EncoderProbe } from "@adapters/encode/encoderSupport";
import type { ServerProbeResult } from "@adapters/http/healthService";
import { describePersistState, type StorageStatus } from "@adapters/platform/persistStorage";
import { describeServerStatus } from "@screens/settings/serverStatusPanel";
import type { SwStatus } from "@shell/swUpdate";
import { formatCount, STRINGS } from "@ui/strings";

/**
 * 진단·상태 모달 데이터 조립 — 03 §15.2 (React 무관 · deps 주입)
 *
 * ⚠️ **게이트 키 값을 어떤 행에도 싣지 않는다.** 표시는 `describeServerStatus`의 3상태
 *    (설정됨/거부됨/미설정)뿐이다(정적 검사 DIAG-1 · analysis/41 §2.5).
 * ⚠️ **"구성됨"과 "도달"은 별 행**이다 — 합치면 운영자가 "주소가 있으니 되는 거겠지"로 오독한다.
 * ⚠️ **카메라를 열지 않는다.** `state()`/`settings()`를 **읽기만** 한다 — `start()`를 부르면
 *    모달을 여는 것만으로 카메라 LED가 켜진다.
 * ⚠️ `tone`은 **색 + 문자**를 함께 쓰기 위한 보조다. 값 문자열만으로도 판독 가능해야 한다(01 §8).
 */

export type DiagnosticsTone = "ok" | "warn" | "bad" | "neutral";

export interface DiagnosticsRow {
  readonly label: string;
  readonly value: string;
  readonly tone: DiagnosticsTone;
}

export type DiagnosticsSectionId =
  | "camera"
  | "encoder"
  | "server"
  | "logStorage"
  | "contact"
  | "app";

export interface DiagnosticsSection {
  readonly id: DiagnosticsSectionId;
  readonly title: string;
  readonly rows: readonly DiagnosticsRow[];
}

export interface DiagnosticsSnapshot {
  readonly sections: readonly DiagnosticsSection[];
  /** 수집 중 화면을 떠났다. 호출측은 결과를 **버린다**. */
  readonly cancelled: boolean;
}

/** 카메라 권한 3상태 + 알 수 없음. `navigator.permissions`가 없거나 throw하면 `null`이다(A4). */
export type CameraPermission = "granted" | "denied" | "prompt" | null;

export interface DiagnosticsDeps {
  readonly listCameras: () => Promise<readonly CameraDevice[]>;
  readonly cameraState: () => CameraState;
  readonly cameraSettings: () => CameraSettings | null;
  readonly processedSize: () => ProcessedSize | null;
  readonly cameraFps: () => number;
  readonly cameraPermission: () => Promise<CameraPermission>;
  /** `null`이면 아직 촬영이 없어 판정 전이다. */
  readonly encoderProbe: () => EncoderProbe | null;
  readonly serverProbe: () => Promise<ServerProbeResult>;
  readonly storageBucket: string;
  /** 게스트는 `null`. */
  readonly accountId: string | null;
  readonly logStats: () => Promise<{
    count: number;
    oldestTs: number | null;
    newestTs: number | null;
  } | null>;
  readonly storageStatus: () => Promise<StorageStatus>;
  readonly sessionLeftovers: () => Promise<number>;
  readonly storedResults: () => Promise<{ totalBytes: number; folderCount: number }>;
  readonly frameCacheBytes: () => Promise<number>;
  readonly appVersion: string;
  readonly buildDate: string;
  readonly swStatus: SwStatus;
  readonly standalone: boolean;
  /** epoch ms → 표시 문자열. 결정성을 위해 주입한다. */
  readonly formatTimestamp: (ms: number) => string;
}

const UNKNOWN = STRINGS.account.unknown;

function neutral(label: string, value: string): DiagnosticsRow {
  return { label, value, tone: "neutral" };
}

function permissionText(permission: CameraPermission): DiagnosticsRow {
  if (permission === "granted") {
    return { label: STRINGS.diagnostics.cameraPermission, value: "허용됨", tone: "ok" };
  }
  if (permission === "denied") {
    return { label: STRINGS.diagnostics.cameraPermission, value: "거부됨", tone: "bad" };
  }
  if (permission === "prompt") {
    return { label: STRINGS.diagnostics.cameraPermission, value: "확인 전", tone: "warn" };
  }
  return { label: STRINGS.diagnostics.cameraPermission, value: UNKNOWN, tone: "neutral" };
}

function cameraStateTone(state: CameraState): DiagnosticsTone {
  if (state === "Ready") return "ok";
  if (state === "Failed") return "bad";
  return "warn";
}

function buildCameraSection(
  devices: readonly CameraDevice[],
  permission: CameraPermission,
  deps: DiagnosticsDeps,
): DiagnosticsSection {
  const settings = deps.cameraSettings();
  const processed = deps.processedSize();
  const state = deps.cameraState();

  return {
    id: "camera",
    title: STRINGS.diagnostics.sections.camera,
    rows: [
      neutral(STRINGS.diagnostics.cameraCount, String(devices.length)),
      neutral(
        STRINGS.diagnostics.cameraList,
        devices.length === 0
          ? STRINGS.settings.cameraNone
          : devices.map((device, index) => displayLabel(device, index)).join(", "),
      ),
      { label: STRINGS.diagnostics.cameraState, value: state, tone: cameraStateTone(state) },
      neutral(
        STRINGS.diagnostics.cameraResolution,
        settings === null ? UNKNOWN : `${settings.width}×${settings.height}`,
      ),
      neutral(
        STRINGS.diagnostics.processedSize,
        processed === null ? UNKNOWN : `${processed.width}×${processed.height}`,
      ),
      neutral(STRINGS.diagnostics.cameraFps, deps.cameraFps().toFixed(1)),
      permissionText(permission),
    ],
  };
}

function buildEncoderSection(probe: EncoderProbe | null): DiagnosticsSection {
  if (probe === null) {
    return {
      id: "encoder",
      title: STRINGS.diagnostics.sections.encoder,
      rows: [
        {
          label: STRINGS.diagnostics.encoderPath,
          value: STRINGS.diagnostics.encoderNotProbed,
          tone: "neutral",
        },
      ],
    };
  }

  const pathValue = probe.path === "none" ? STRINGS.diagnostics.encoderNone : probe.path;
  return {
    id: "encoder",
    title: STRINGS.diagnostics.sections.encoder,
    rows: [
      {
        label: STRINGS.diagnostics.encoderPath,
        value: pathValue,
        tone: probe.path === "none" ? "warn" : "ok",
      },
      neutral(STRINGS.diagnostics.encoderCodec, probe.codec ?? UNKNOWN),
      neutral(STRINGS.diagnostics.encoderReason, probe.reason),
      neutral(
        STRINGS.diagnostics.encoderCandidates,
        probe.probed.length === 0
          ? UNKNOWN
          : probe.probed
              .map((item) => `${item.codec}=${item.supported ? "O" : "X"}`)
              .join(", "),
      ),
    ],
  };
}

function buildServerSection(
  probe: ServerProbeResult,
  deps: DiagnosticsDeps,
): DiagnosticsSection {
  // 기존 설정 화면 패널을 그대로 재사용한다 — "구성 ≠ 도달" 2행 분리와 게이트 키 3상태가
  // 이미 거기서 구현돼 있고, 문구가 둘로 갈라지면 안 된다(F6).
  const rows: DiagnosticsRow[] = describeServerStatus({ kind: "ready", probe }).map((row) =>
    neutral(row.label, row.value),
  );
  rows.push(
    neutral(
      STRINGS.diagnostics.bucket,
      deps.storageBucket.trim().length > 0
        ? deps.storageBucket
        : STRINGS.settings.serverNotConfigured,
    ),
  );
  rows.push(
    neutral(STRINGS.diagnostics.currentAccount, deps.accountId ?? STRINGS.diagnostics.guest),
  );
  return { id: "server", title: STRINGS.diagnostics.sections.server, rows };
}

function buildLogStorageSection(
  stats: { count: number; oldestTs: number | null; newestTs: number | null } | null,
  storage: StorageStatus,
  leftovers: number,
  results: { totalBytes: number; folderCount: number },
  frameCacheBytes: number,
  deps: DiagnosticsDeps,
): DiagnosticsSection {
  const range =
    stats === null || stats.oldestTs === null || stats.newestTs === null
      ? UNKNOWN
      : `${deps.formatTimestamp(stats.oldestTs)} ~ ${deps.formatTimestamp(stats.newestTs)}`;

  return {
    id: "logStorage",
    title: STRINGS.diagnostics.sections.logStorage,
    rows: [
      neutral(
        STRINGS.diagnostics.logCount,
        stats === null ? UNKNOWN : formatCount(STRINGS.diagnostics.logCountValue, stats.count),
      ),
      neutral(STRINGS.diagnostics.logRange, range),
      {
        label: STRINGS.diagnostics.persistState,
        value: describePersistState(storage.persistState),
        tone: storage.persistState === "granted" ? "ok" : "warn",
      },
      neutral(
        STRINGS.diagnostics.storageUsage,
        storage.usage === null
          ? UNKNOWN
          : `${formatBytes(storage.usage)} / ${
              storage.quota === null ? UNKNOWN : formatBytes(storage.quota)
            }`,
      ),
      {
        label: STRINGS.diagnostics.sessionLeftovers,
        value: String(leftovers),
        tone: leftovers > 0 ? "warn" : "ok",
      },
      neutral(
        STRINGS.diagnostics.storedResults,
        `${formatCount(STRINGS.settings.storedResultsCount, results.folderCount)} · ${formatBytes(
          results.totalBytes,
        )}`,
      ),
      neutral(STRINGS.diagnostics.frameCacheUsage, formatBytes(frameCacheBytes)),
    ],
  };
}

function buildContactSection(probe: ServerProbeResult, deps: DiagnosticsDeps): DiagnosticsSection {
  return {
    id: "contact",
    title: STRINGS.diagnostics.sections.contact,
    rows: [
      neutral(STRINGS.diagnostics.developer, STRINGS.diagnostics.developerEmail),
      neutral(STRINGS.diagnostics.version, deps.appVersion),
      // ⚠️ Build Date는 **여기서만** 노출한다(하단 캡션 금지 — it18 · 05 §8.2).
      neutral(
        STRINGS.diagnostics.buildDate,
        deps.buildDate.trim().length > 0 ? deps.buildDate : UNKNOWN,
      ),
      neutral(STRINGS.diagnostics.webDeployDate, probe.deployedAt ?? UNKNOWN),
    ],
  };
}

export function swStatusLabel(status: SwStatus): string {
  switch (status) {
    case "active":
      return STRINGS.pwa.swActive;
    case "waiting":
      return STRINGS.pwa.swWaiting;
    case "registering":
      return STRINGS.pwa.swRegistering;
    case "disabled":
      return STRINGS.pwa.swDisabled;
    case "failed":
      return STRINGS.pwa.swFailed;
    default:
      return STRINGS.pwa.swUnsupported;
  }
}

function buildAppSection(deps: DiagnosticsDeps): DiagnosticsSection {
  return {
    id: "app",
    title: STRINGS.diagnostics.sections.app,
    rows: [
      neutral(STRINGS.diagnostics.version, deps.appVersion),
      {
        label: STRINGS.diagnostics.serviceWorker,
        value: swStatusLabel(deps.swStatus),
        tone:
          deps.swStatus === "active" ? "ok" : deps.swStatus === "failed" ? "bad" : "warn",
      },
      neutral(
        STRINGS.diagnostics.installed,
        deps.standalone ? STRINGS.pwa.installed : STRINGS.pwa.notInstalled,
      ),
    ],
  };
}

const EMPTY_PROBE: ServerProbeResult = {
  reachable: false,
  deployedAt: null,
  gateKeyValid: null,
  detail: null,
};

/**
 * 6섹션 수집. **예외를 전파하지 않는다** — 소스 하나가 죽어도 나머지 섹션은 보여야 한다.
 * 취소는 결과 폐기 방식이다(`loadServerStatus`와 같은 형태).
 */
export async function collectDiagnostics(
  deps: DiagnosticsDeps,
  signal?: AbortSignal,
): Promise<DiagnosticsSnapshot> {
  const aborted = (): boolean => signal?.aborted === true;

  const [devices, permission, probe, stats, storage, leftovers, results, frameCacheBytes] =
    await Promise.all([
      safe(deps.listCameras, [] as readonly CameraDevice[]),
      safe(deps.cameraPermission, null as CameraPermission),
      safe(deps.serverProbe, EMPTY_PROBE),
      safe(deps.logStats, null),
      safe(deps.storageStatus, {
        persistState: "unsupported",
        usage: null,
        quota: null,
      } as StorageStatus),
      safe(deps.sessionLeftovers, 0),
      safe(deps.storedResults, { totalBytes: 0, folderCount: 0 }),
      safe(deps.frameCacheBytes, 0),
    ]);

  const sections: DiagnosticsSection[] = [
    buildCameraSection(devices, permission, deps),
    buildEncoderSection(safeSync(deps.encoderProbe, null)),
    buildServerSection(probe, deps),
    buildLogStorageSection(stats, storage, leftovers, results, frameCacheBytes, deps),
    buildContactSection(probe, deps),
    buildAppSection(deps),
  ];

  return { sections, cancelled: aborted() };
}

async function safe<T>(run: () => Promise<T>, fallback: T): Promise<T> {
  try {
    return await run();
  } catch {
    return fallback;
  }
}

function safeSync<T>(run: () => T, fallback: T): T {
  try {
    return run();
  } catch {
    return fallback;
  }
}
