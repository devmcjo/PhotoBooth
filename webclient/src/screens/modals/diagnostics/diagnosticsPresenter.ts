import { describeLoginFailure, type LoginFailureReason } from "@domain/auth/loginFailure";
import {
  formatCameraFailureCode,
  type CameraFailure,
  type CameraFailureReason,
} from "@domain/capture/cameraFailure";
import { formatBytes } from "@domain/results/byteFormat";
import type { CameraPermission } from "@adapters/camera/cameraPermission";
import type {
  CameraSettings,
  CameraState,
  FrameProcessorMode,
  FrameTransferMode,
  PreviewMode,
  ProcessedSize,
} from "@adapters/camera/cameraTypes";
import { displayLabel, type CameraDevice } from "@adapters/camera/deviceEnumerator";
import type { EncoderProbe } from "@adapters/encode/encoderSupport";
import type { OAuthConfigStatus, ServerProbeResult } from "@adapters/http/healthService";
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

/**
 * 카메라 권한 3상태 + 알 수 없음.
 * ⚠️ 정의는 **`adapters/camera/cameraPermission.ts`가 소유한다** — 조회 함수와 같은 파일이어야
 *    폴백 규칙(Safari 미지원·Firefox throw → `null`)이 두 벌로 갈라지지 않는다. 여기서는 재수출만 한다.
 */
export type { CameraPermission };

export interface DiagnosticsDeps {
  readonly listCameras: () => Promise<readonly CameraDevice[]>;
  readonly cameraState: () => CameraState;
  readonly cameraSettings: () => CameraSettings | null;
  readonly processedSize: () => ProcessedSize | null;
  readonly cameraFps: () => number;
  readonly cameraPermission: () => Promise<CameraPermission>;
  /**
   * 마지막 카메라 실패(사유 + 상세). 실패한 적이 없으면 `null`.
   *
   * ⚠️ 사유와 코드를 **한 접근자로** 읽는다(2026-08-07). 별 접근자를 병렬로 두면 두 값이
   *    다른 시점의 `lastFailure`를 읽어 라벨과 코드가 어긋난다.
   */
  readonly cameraFailure: () => CameraFailure | null;
  /** 가공 경로. 카메라가 닫혀 있으면 `null`(04 §2.3.1 "저성능 모드" 표시). */
  readonly pipelineMode: () => FrameProcessorMode | null;
  /** 프리뷰 연결 방식. `none`이면 화면이 검다. */
  readonly previewMode: () => PreviewMode;
  /** 프레임 전달 경로. 카메라가 닫혀 있으면 `null`(04 §2.3.2). */
  readonly frameTransferMode: () => FrameTransferMode | null;
  /** 실제로 열린 제약 사다리 칸. 닫혀 있으면 `null`. */
  readonly constraintStep: () => string | null;
  /** 마지막 로그인 실패 흔적(메모리 전용). 없으면 `null`. */
  readonly lastLoginFailure: () => { reason: LoginFailureReason; at: number } | null;
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

/** 실패 사유 → 진단 표시 문구. 화면이 사유 문자열을 비교하지 않게 여기서 접는다. */
const CAMERA_FAILURE_LABEL: Readonly<Record<CameraFailureReason, string>> = {
  permissionDenied: "권한 거부",
  noDevice: "장치 없음",
  inUse: "사용 중",
  insecureContext: "보안 연결 아님",
  pipelineStalled: "영상 표시 실패(가공 정체)",
  playbackBlocked: "영상 재생 시작 실패",
  pipelineSlow: "영상 지연(Ready 미달)",
  unsupportedBrowser: "브라우저 미지원",
  unknown: "알 수 없음",
};

/**
 * 가공 경로 행 — 04 §2.3.1이 요구한 **"저성능 모드 표시"**.
 * 메인 스레드 경로는 성능 예산을 보장하지 않으므로 `warn`이다(실패는 아니다).
 */
function pipelineModeRow(mode: FrameProcessorMode | null): DiagnosticsRow {
  if (mode === null) {
    return { label: STRINGS.diagnostics.pipelineMode, value: UNKNOWN, tone: "neutral" };
  }
  return mode === "worker"
    ? {
        label: STRINGS.diagnostics.pipelineMode,
        value: STRINGS.diagnostics.pipelineModeWorker,
        tone: "ok",
      }
    : {
        label: STRINGS.diagnostics.pipelineMode,
        value: STRINGS.diagnostics.pipelineModeMain,
        tone: "warn",
      };
}

const PREVIEW_MODE_LABEL: Readonly<Record<PreviewMode, string>> = {
  transferred: STRINGS.diagnostics.previewModeTransferred,
  bitmap: STRINGS.diagnostics.previewModeBitmap,
  direct: STRINGS.diagnostics.previewModeDirect,
  none: STRINGS.diagnostics.previewModeNone,
};

/** `none`은 **화면이 검다**는 뜻이므로 `bad`다 — 카메라가 Ready여도 그렇다. */
function previewModeRow(mode: PreviewMode): DiagnosticsRow {
  return {
    label: STRINGS.diagnostics.previewMode,
    value: PREVIEW_MODE_LABEL[mode],
    tone: mode === "none" ? "bad" : mode === "transferred" ? "ok" : "warn",
  };
}

/**
 * 라벨 **· 코드**로 함께 낸다 — 라벨은 운영자가 읽고, 코드는 우리에게 전달된다.
 * 코드에는 새니타이즈를 통과한 값만 들어간다(계정·서버·기기 식별자와 무관하다 — DIAG-1·AUTH-3).
 */
function failureReasonRow(failure: CameraFailure | null): DiagnosticsRow {
  if (failure === null) {
    return { label: STRINGS.diagnostics.cameraFailureReason, value: "없음", tone: "ok" };
  }
  return {
    label: STRINGS.diagnostics.cameraFailureReason,
    value: `${CAMERA_FAILURE_LABEL[failure.reason]} · ${formatCameraFailureCode(failure)}`,
    tone: "bad",
  };
}

const FRAME_TRANSFER_LABEL: Readonly<Record<FrameTransferMode, string>> = {
  videoFrame: STRINGS.diagnostics.frameTransferVideoFrame,
  imageBitmap: STRINGS.diagnostics.frameTransferBitmap,
  imageBitmapDemoted: STRINGS.diagnostics.frameTransferDemoted,
};

/**
 * `imageBitmapDemoted`가 **`warn`인 이유**: 정상 폴백(`imageBitmap`)과 달리 `VideoFrame`이
 * 있었는데 런타임에 깨진 것이라 브라우저 결함 신호이며 성능 예산 재측정 대상이다.
 */
function frameTransferRow(mode: FrameTransferMode | null): DiagnosticsRow {
  if (mode === null) {
    return { label: STRINGS.diagnostics.frameTransfer, value: UNKNOWN, tone: "neutral" };
  }
  return {
    label: STRINGS.diagnostics.frameTransfer,
    value: FRAME_TRANSFER_LABEL[mode],
    tone: mode === "videoFrame" ? "ok" : mode === "imageBitmap" ? "neutral" : "warn",
  };
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
      failureReasonRow(safeSync(deps.cameraFailure, null)),
      // ↓ 2026-08-06 신설 3행. 이것이 없어서 "카메라가 안 열리는지 / 화면만 검은지"를
      //   현장에서 구분할 수 없었다(04 §2.3.1 · 10 §6.2).
      pipelineModeRow(safeSync(deps.pipelineMode, null)),
      previewModeRow(safeSync(deps.previewMode, "none" as PreviewMode)),
      // ↓ 2026-08-07 신설. zero-copy가 살아 있는지, 아니면 런타임에 강등됐는지를 가른다.
      frameTransferRow(safeSync(deps.frameTransferMode, null)),
      neutral(
        STRINGS.diagnostics.cameraConstraintStep,
        safeSync(deps.constraintStep, null) ?? UNKNOWN,
      ),
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

/**
 * 서버 OAuth 구성 2행(순수) — 2026-08-01 후속.
 *
 * 그 사고에서 배포된 `GOOGLE_OAUTH_CLIENT_ID_WEB`이 플레이스홀더였는데 **운영자가 화면에서
 * 알아챌 방법이 없었다.** 게이트 키가 이미 "설정됨/미설정"을 보여 주므로 OAuth도 같은 수준으로 낸다.
 *
 * ⚠️ **값을 싣지 않는다.** 서버가 보내는 것도 열거값·개수뿐이다(`domain/oauthStatus.ts`).
 * ⚠️ `null`(구버전 서버·도달 실패·키 무효)은 "미설정"이 아니라 **"알 수 없음"** 이다 —
 *    섞으면 멀쩡한 배포를 오구성으로 읽는다.
 */
export function oauthRows(status: OAuthConfigStatus | null): readonly DiagnosticsRow[] {
  if (status === null) {
    return [
      { label: STRINGS.diagnostics.oauthWeb, value: UNKNOWN, tone: "neutral" },
      { label: STRINGS.diagnostics.oauthAllowlist, value: UNKNOWN, tone: "neutral" },
    ];
  }

  const stateText =
    status.web === "ok"
      ? STRINGS.diagnostics.oauthConfigured
      : status.web === "malformed"
        ? STRINGS.diagnostics.oauthMalformed
        : STRINGS.diagnostics.oauthUnset;
  // desktop 값 복사는 형식이 멀쩡해도 로그인이 실패하는 오구성이라 상태 문자열에 함께 붙인다.
  const shared = status.sharedClientId ? ` · ${STRINGS.diagnostics.oauthShared}` : "";

  return [
    {
      label: STRINGS.diagnostics.oauthWeb,
      value: `${stateText}${shared}`,
      tone: status.web === "ok" && !status.sharedClientId ? "ok" : "bad",
    },
    {
      label: STRINGS.diagnostics.oauthAllowlist,
      value: formatCount(
        STRINGS.diagnostics.oauthAllowlistValue,
        status.redirectAllowlistCount,
      ),
      tone: status.redirectAllowlistCount > 0 ? "ok" : "bad",
    },
  ];
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
  rows.push(...oauthRows(probe.oauth));
  rows.push(
    neutral(STRINGS.diagnostics.currentAccount, deps.accountId ?? STRINGS.diagnostics.guest),
  );
  // [마지막 로그인 실패] — 사유 열거값 + 시각뿐이다. email·token·code를 담지 않는다(AUTH-3).
  // 이 행이 없어서 2026-08-01 서버 구성 오류를 현장에서 판별할 방법이 없었다.
  const failure = safeSync(deps.lastLoginFailure, null);
  rows.push(
    failure === null
      ? { label: STRINGS.diagnostics.lastLoginFailure, value: "없음", tone: "ok" }
      : {
          label: STRINGS.diagnostics.lastLoginFailure,
          value: `${describeLoginFailure(failure.reason)} · ${deps.formatTimestamp(failure.at)}`,
          tone: "bad",
        },
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
  oauth: null,
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
