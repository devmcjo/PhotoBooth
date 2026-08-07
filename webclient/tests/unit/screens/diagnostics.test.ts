import { afterEach, describe, expect, it, vi } from "vitest";
import type { EncoderProbe } from "@adapters/encode/encoderSupport";
import type { ServerProbeResult } from "@adapters/http/healthService";
import {
  collectDiagnostics,
  swStatusLabel,
  type DiagnosticsDeps,
  type DiagnosticsSnapshot,
} from "@screens/modals/diagnostics/diagnosticsPresenter";
import { STRINGS } from "@ui/strings";

/**
 * 진단·상태 모달 데이터 — 03 §15.2 (설계 §7)
 *
 * ⚠️ 가장 중요한 고정: **게이트 키 값이 어떤 행에도 없다.** 값이 새면 로그 내보내기 파일로
 *    그대로 흘러 나간다(analysis/41 §2.5).
 * ⚠️ **모달이 카메라를 열지 않는다** — deps에 `start`가 없고 `state`/`settings`만 읽는다.
 */

const PROBE: ServerProbeResult = {
  reachable: true,
  deployedAt: "2026-08-01T00:00:00.000Z",
  gateKeyValid: true,
  oauth: { web: "ok", desktop: "ok", sharedClientId: false, redirectAllowlistCount: 3 },
  detail: null,
};

const ENCODER: EncoderProbe = {
  path: "webcodecs",
  codec: "avc1.42001f",
  reason: "WebCodecs 지원",
  probed: [{ codec: "avc1.42001f", supported: true }],
};

function deps(overrides: Partial<DiagnosticsDeps> = {}): DiagnosticsDeps {
  return {
    listCameras: async () => [{ deviceId: "cam-1", label: "USB Camera", groupId: "g1" }],
    cameraState: () => "Ready",
    cameraSettings: () => ({
      deviceId: "cam-1",
      label: "USB Camera",
      width: 1920,
      height: 1080,
      frameRate: 30,
    }),
    processedSize: () => ({ width: 1080, height: 1080 }),
    cameraFps: () => 29.5,
    cameraPermission: async () => "granted",
    cameraFailure: () => null,
    pipelineMode: () => "worker",
    previewMode: () => "transferred",
    frameTransferMode: () => "videoFrame",
    constraintStep: () => "device+1080p",
    lastLoginFailure: () => null,
    encoderProbe: () => ENCODER,
    serverProbe: async () => PROBE,
    storageBucket: "mcphoto-955fb.firebasestorage.app",
    accountId: "operator-1",
    logStats: async () => ({ count: 42, oldestTs: 1_700_000_000_000, newestTs: 1_700_000_100_000 }),
    storageStatus: async () => ({ persistState: "granted", usage: 1024, quota: 4096 }),
    sessionLeftovers: async () => 0,
    storedResults: async () => ({ totalBytes: 2048, folderCount: 3 }),
    frameCacheBytes: async () => 512,
    appVersion: "1.2.3",
    buildDate: "2026-08-01T00:00:00.000Z",
    swStatus: "active",
    standalone: false,
    formatTimestamp: (ms) => `T${ms}`,
    ...overrides,
  };
}

function findRow(snapshot: DiagnosticsSnapshot, label: string): string | undefined {
  for (const section of snapshot.sections) {
    for (const row of section.rows) {
      if (row.label === label) return row.value;
    }
  }
  return undefined;
}

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
});

describe("collectDiagnostics — 6섹션", () => {
  it("섹션 6개를 규격 순서로 만든다", async () => {
    const snapshot = await collectDiagnostics(deps());
    expect(snapshot.sections.map((section) => section.id)).toEqual([
      "camera",
      "encoder",
      "server",
      "logStorage",
      "contact",
      "app",
    ]);
    expect(snapshot.cancelled).toBe(false);
  });

  it("모든 행에 라벨·값이 있다(빈 칸을 만들지 않는다)", async () => {
    const snapshot = await collectDiagnostics(deps());
    for (const section of snapshot.sections) {
      expect(section.rows.length).toBeGreaterThan(0);
      for (const row of section.rows) {
        expect(row.label.length).toBeGreaterThan(0);
        expect(row.value.length).toBeGreaterThan(0);
      }
    }
  });

  it("카메라 해상도·가공 크기·fps를 표시한다", async () => {
    const snapshot = await collectDiagnostics(deps());
    expect(findRow(snapshot, STRINGS.diagnostics.cameraResolution)).toBe("1920×1080");
    expect(findRow(snapshot, STRINGS.diagnostics.processedSize)).toBe("1080×1080");
    expect(findRow(snapshot, STRINGS.diagnostics.cameraFps)).toBe("29.5");
  });

  it("`encoderProbe()`가 null이면 '아직 판정 전'이다", async () => {
    const snapshot = await collectDiagnostics(deps({ encoderProbe: () => null }));
    expect(findRow(snapshot, STRINGS.diagnostics.encoderPath)).toBe(
      STRINGS.diagnostics.encoderNotProbed,
    );
  });

  it("인코더 경로 none은 '미지원'으로 표기한다", async () => {
    const snapshot = await collectDiagnostics(
      deps({
        encoderProbe: () => ({ path: "none", codec: null, reason: "지원 없음", probed: [] }),
      }),
    );
    expect(findRow(snapshot, STRINGS.diagnostics.encoderPath)).toBe(
      STRINGS.diagnostics.encoderNone,
    );
  });

  it("권한 조회가 throw하면 '알 수 없음'이다(Firefox는 이름을 모른다 — A4)", async () => {
    const snapshot = await collectDiagnostics(
      deps({
        cameraPermission: async () => {
          throw new TypeError("'camera' is not a valid PermissionName");
        },
      }),
    );
    expect(findRow(snapshot, STRINGS.diagnostics.cameraPermission)).toBe(STRINGS.account.unknown);
  });

  it("소스 하나가 죽어도 나머지 섹션은 만들어진다", async () => {
    const snapshot = await collectDiagnostics(
      deps({
        listCameras: async () => {
          throw new Error("열거 실패");
        },
        logStats: async () => {
          throw new Error("로그 실패");
        },
        frameCacheBytes: async () => {
          throw new Error("OPFS 실패");
        },
      }),
    );
    expect(snapshot.sections).toHaveLength(6);
    expect(findRow(snapshot, STRINGS.diagnostics.cameraCount)).toBe("0");
  });

  it("**'구성'과 '도달'이 별 행**이다", async () => {
    const snapshot = await collectDiagnostics(deps());
    const server = snapshot.sections.find((section) => section.id === "server");
    const labels = server?.rows.map((row) => row.label) ?? [];
    expect(labels).toContain("구성");
    expect(labels).toContain("도달");
    expect(labels).toContain("게이트 키");
  });

  it("게스트는 현재 계정이 '게스트'다", async () => {
    const snapshot = await collectDiagnostics(deps({ accountId: null }));
    expect(findRow(snapshot, STRINGS.diagnostics.currentAccount)).toBe(
      STRINGS.diagnostics.guest,
    );
  });

  it("취소되면 `cancelled`가 true다(호출측이 결과를 버린다)", async () => {
    const controller = new AbortController();
    controller.abort();
    const snapshot = await collectDiagnostics(deps(), controller.signal);
    expect(snapshot.cancelled).toBe(true);
  });

  it("Build Date는 진단에만 있다(하단 캡션 금지 — it18)", async () => {
    const snapshot = await collectDiagnostics(deps());
    expect(findRow(snapshot, STRINGS.diagnostics.buildDate)).toBe("2026-08-01T00:00:00.000Z");
  });
});

describe("게이트 키 값 노출 금지", () => {
  it("**주입한 키 문자열이 스냅샷 어디에도 없다**", async () => {
    const SECRET = "GATE-KEY-DO-NOT-LEAK-9f3a";
    vi.stubEnv("VITE_BACKEND_API_KEY", SECRET);
    vi.resetModules();

    // env는 모듈 로드 시점에 값을 굳힌다 — 스텁 뒤 다시 로드해야 반영된다.
    const presenter = await import("@screens/modals/diagnostics/diagnosticsPresenter");
    const snapshot = await presenter.collectDiagnostics(deps());

    expect(JSON.stringify(snapshot)).not.toContain(SECRET);
    // 키가 실제로 주입됐는지 확인한다(테스트가 공회전하지 않게).
    const envModule = await import("../../../src/env");
    expect(envModule.env.backendApiKey).toBe(SECRET);
  });

  /**
   * 2026-08-01 후속: 플레이스홀더 배포를 **운영자가 화면에서 알아챌 수 있어야** 한다.
   * ⚠️ 게이트 키와 같은 규칙 — 값은 절대 싣지 않는다.
   */
  it("웹 OAuth 구성 행이 상태를 보여 준다(형식 오류가 보인다)", async () => {
    const snapshot = await collectDiagnostics(
      deps({
        serverProbe: async () => ({
          ...PROBE,
          oauth: {
            web: "malformed",
            desktop: "ok",
            sharedClientId: false,
            redirectAllowlistCount: 3,
          },
        }),
      }),
    );

    expect(findRow(snapshot, STRINGS.diagnostics.oauthWeb)).toBe(
      STRINGS.diagnostics.oauthMalformed,
    );
    expect(findRow(snapshot, STRINGS.diagnostics.oauthAllowlist)).toBe("3개");
  });

  it("desktop client_id를 그대로 쓴 오구성이 드러난다", async () => {
    const snapshot = await collectDiagnostics(
      deps({
        serverProbe: async () => ({
          ...PROBE,
          oauth: {
            web: "ok",
            desktop: "ok",
            sharedClientId: true,
            redirectAllowlistCount: 3,
          },
        }),
      }),
    );

    expect(findRow(snapshot, STRINGS.diagnostics.oauthWeb)).toContain(
      STRINGS.diagnostics.oauthShared,
    );
  });

  it("서버가 신호를 주지 않으면 '미설정'이 아니라 '알 수 없음'이다", async () => {
    const snapshot = await collectDiagnostics(
      deps({ serverProbe: async () => ({ ...PROBE, oauth: null }) }),
    );

    expect(findRow(snapshot, STRINGS.diagnostics.oauthWeb)).toBe(STRINGS.account.unknown);
    expect(findRow(snapshot, STRINGS.diagnostics.oauthAllowlist)).toBe(STRINGS.account.unknown);
  });

  it("게이트 키 행은 '설정됨/거부됨/미설정' 중 하나다", async () => {
    for (const gateKeyValid of [true, false, null]) {
      const snapshot = await collectDiagnostics(
        deps({ serverProbe: async () => ({ ...PROBE, gateKeyValid }) }),
      );
      const value = findRow(snapshot, "게이트 키");
      expect([
        STRINGS.settings.gateKeySet,
        STRINGS.settings.gateKeyInvalid,
        STRINGS.settings.gateKeyUnset,
      ]).toContain(value);
    }
  });
});

describe("swStatusLabel", () => {
  it.each([
    ["active", STRINGS.pwa.swActive],
    ["waiting", STRINGS.pwa.swWaiting],
    ["registering", STRINGS.pwa.swRegistering],
    ["disabled", STRINGS.pwa.swDisabled],
    ["failed", STRINGS.pwa.swFailed],
    ["unsupported", STRINGS.pwa.swUnsupported],
  ] as const)("%s → %s", (status, expected) => {
    expect(swStatusLabel(status)).toBe(expected);
  });
});
