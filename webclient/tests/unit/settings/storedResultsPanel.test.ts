import { describe, expect, it } from "vitest";
import type { StorageStatus } from "@adapters/platform/persistStorage";
import { readStorageStatus } from "@adapters/platform/persistStorage";
import type { ResultsUsage } from "@adapters/storage/resultsStore";
import {
  describeRemoveAll,
  loadStoredResults,
  removeAllStoredResults,
  removeStoredResult,
  type StoredResultsDeps,
} from "@screens/settings/storedResultsPanel";
import {
  buildCameraOptions,
  needsPermissionHint,
  resolveSelectedDevice,
  selectCamera,
  selectFacing,
  storedDeviceRef,
} from "@screens/settings/cameraDevicePanel";
import {
  describeServerStatus,
  loadServerStatus,
} from "@screens/settings/serverStatusPanel";
import { createDraft } from "@screens/settings/settingsForm";
import { DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS } from "@domain/settings/appSettings";
import type { SettingsEditContext } from "@domain/settings/settingsEditPolicy";
import type { CameraDevice } from "@adapters/camera/deviceEnumerator";
import type { ServerProbeResult } from "@adapters/http/healthService";

/**
 * 설정 화면의 패널 3종(보관 결과물 · 카메라 장치 · 서버 상태) — 03 §12.1·§12.6 · 05 §5.4
 */

const OPERATOR: SettingsEditContext = { isGuest: false, qrBlocked: false };

function usage(folders: { name: string; bytes: number }[]): ResultsUsage {
  return {
    totalBytes: folders.reduce((sum, folder) => sum + folder.bytes, 0),
    folders,
  };
}

function panelDeps(overrides: Partial<StoredResultsDeps> = {}): StoredResultsDeps {
  return {
    usage: async () => usage([]),
    removeFolder: async () => true,
    storageStatus: async () => null,
    ...overrides,
  };
}

describe("loadStoredResults", () => {
  it("목록·총량을 그대로 싣는다(정렬은 resultsStore가 소유한다)", async () => {
    const view = await loadStoredResults(
      panelDeps({
        usage: async () =>
          usage([
            { name: "mcphoto_260101_0900", bytes: 1000 },
            { name: "mcphoto_260720_1445", bytes: 2000 },
          ]),
      }),
    );

    expect(view.loading).toBe(false);
    expect(view.totalBytes).toBe(3000);
    expect(view.folders.map((f) => f.name)).toEqual([
      "mcphoto_260101_0900",
      "mcphoto_260720_1445",
    ]);
  });

  it("resultsStore가 빈 값을 줘도 크래시하지 않는다", async () => {
    const view = await loadStoredResults(panelDeps());
    expect(view.folders).toEqual([]);
    expect(view.totalBytes).toBe(0);
    expect(view.storageLow).toBe(false);
  });

  it("여유 10% 미만이면 경고 배지 조건이 켜진다", async () => {
    const low: StorageStatus = { persistState: "granted", usage: 950, quota: 1000 };
    const view = await loadStoredResults(panelDeps({ storageStatus: async () => low }));
    expect(view.storageLow).toBe(true);
  });

  it("정확히 임계값이면 경고하지 않는다(정수 비교 — 함정 #3)", async () => {
    const edge: StorageStatus = { persistState: "granted", usage: 900, quota: 1000 };
    const view = await loadStoredResults(panelDeps({ storageStatus: async () => edge }));
    expect(view.storageLow).toBe(false);
  });

  it("저장소 상태를 모르면 경고하지 않는다(거짓 경보 금지)", async () => {
    const unknown: StorageStatus = { persistState: "unsupported", usage: null, quota: null };
    const view = await loadStoredResults(panelDeps({ storageStatus: async () => unknown }));
    expect(view.storageLow).toBe(false);
  });
});

describe("삭제 — 성공 오인 금지(M4)", () => {
  it("개별 삭제 실패는 false를 그대로 돌려준다", async () => {
    const ok = await removeStoredResult(panelDeps({ removeFolder: async () => false }), "x");
    expect(ok).toBe(false);
  });

  it("전체 삭제의 부분 실패를 정직하게 센다", async () => {
    const failing = new Set(["b", "d"]);
    const outcome = await removeAllStoredResults(
      panelDeps({ removeFolder: async (name) => !failing.has(name) }),
      ["a", "b", "c", "d"],
    );
    expect(outcome).toEqual({ removed: 2, failed: 2 });
    expect(describeRemoveAll(outcome)).toBe("2개를 삭제했고 2개는 실패했습니다.");
  });

  it("전부 성공하면 실패 문구를 붙이지 않는다", () => {
    expect(describeRemoveAll({ removed: 3, failed: 0 })).toBe("3개를 삭제했습니다.");
  });

  it("빈 목록 전체 삭제는 0/0이다", async () => {
    expect(await removeAllStoredResults(panelDeps(), [])).toEqual({ removed: 0, failed: 0 });
  });
});

describe("readStorageStatus — 조회 전용(F16)", () => {
  it("persist를 **호출하지 않는다**(화면을 여는 것만으로 권한 창이 뜨면 안 된다)", async () => {
    let persistCalls = 0;
    const status = await readStorageStatus({
      persist: async () => {
        persistCalls++;
        return true;
      },
      persisted: async () => false,
      estimate: async () => ({ usage: 10, quota: 100 }),
    });

    expect(persistCalls).toBe(0);
    expect(status).toEqual({ persistState: "denied", usage: 10, quota: 100 });
  });

  it("이미 승인돼 있으면 granted다", async () => {
    const status = await readStorageStatus({
      persist: async () => true,
      persisted: async () => true,
    });
    expect(status.persistState).toBe("granted");
  });

  it("미지원·부재는 unsupported다", async () => {
    expect(await readStorageStatus(undefined)).toEqual({
      persistState: "unsupported",
      usage: null,
      quota: null,
    });
    expect((await readStorageStatus({})).persistState).toBe("unsupported");
  });

  it("조회가 던져도 상태로 접는다", async () => {
    const status = await readStorageStatus({
      persist: async () => true,
      persisted: () => Promise.reject(new Error("blocked")),
      estimate: () => Promise.reject(new Error("blocked")),
    });
    expect(status).toEqual({ persistState: "denied", usage: null, quota: null });
  });
});

describe("카메라 장치 패널 — 03 §12.6", () => {
  const devices: readonly CameraDevice[] = [
    { deviceId: "id-a", label: "", groupId: "g1" },
    { deviceId: "id-b", label: "Logi C920", groupId: "g2" },
  ];

  it("빈 라벨은 '카메라 N'으로 폴백하고 안내 조건이 켜진다", () => {
    const options = buildCameraOptions(devices);
    expect(options[0]!.label).toBe("카메라 1");
    expect(options[0]!.labelUnknown).toBe(true);
    expect(options[1]!.label).toBe("Logi C920");
    expect(needsPermissionHint(options)).toBe(true);
  });

  it("라벨이 모두 있으면 안내를 띄우지 않는다", () => {
    expect(needsPermissionHint(buildCameraOptions([devices[1]!]))).toBe(false);
  });

  it("장치 선택은 deviceId·label·groupId 3개를 함께 기록한다(WC3)", () => {
    const draft = createDraft(DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS);
    const next = selectCamera(draft, devices[1]!, OPERATOR);

    expect(next.values.CameraDevice).toBe("id-b");
    expect(next.webExtras.CameraDeviceLabel).toBe("Logi C920");
    expect(next.webExtras.CameraDeviceGroupId).toBe("g2");
  });

  it("저장된 참조로 장치를 되찾는다(deviceId → label → groupId → 첫 장치)", () => {
    const draft = selectCamera(
      createDraft(DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS),
      devices[1]!,
      OPERATOR,
    );
    expect(storedDeviceRef(draft.values, draft.webExtras)).toEqual({
      deviceId: "id-b",
      label: "Logi C920",
      groupId: "g2",
    });

    // deviceId가 바뀌어도 라벨로 되찾는다.
    const renumbered: CameraDevice[] = [
      { deviceId: "id-z", label: "Logi C920", groupId: "gX" },
    ];
    expect(resolveSelectedDevice(renumbered, draft)).toEqual({
      device: renumbered[0],
      reason: "label",
    });
  });

  it("전/후면 힌트는 webExtras에만 들어간다", () => {
    const draft = createDraft(DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS);
    const next = selectFacing(draft, "environment");
    expect(next.webExtras.CameraFacing).toBe("environment");
    expect(next.values).toBe(draft.values);
  });
});

describe("서버 상태 패널 — 03 §12.1 고급", () => {
  const reachable: ServerProbeResult = {
    reachable: true,
    deployedAt: "2026-07-26T00:00:00.000Z",
    gateKeyValid: true,
    detail: null,
  };

  it("프로브 결과를 ready로 감싼다", async () => {
    const view = await loadServerStatus({ probe: async () => reachable });
    expect(view).toEqual({ kind: "ready", probe: reachable });
  });

  it("이미 취소된 신호면 요청 결과를 버린다(언마운트 후 setState 금지)", async () => {
    const controller = new AbortController();
    controller.abort();
    const view = await loadServerStatus({ probe: async () => reachable }, controller.signal);
    expect(view).toEqual({ kind: "cancelled" });
  });

  it("대기 중 취소되면 결과를 버린다", async () => {
    const controller = new AbortController();
    const view = await loadServerStatus(
      {
        probe: async () => {
          controller.abort();
          return reachable;
        },
      },
      controller.signal,
    );
    expect(view).toEqual({ kind: "cancelled" });
  });

  it("probe가 던져도 화면은 상태로 받는다(예외 전파 금지)", async () => {
    const view = await loadServerStatus({ probe: () => Promise.reject(new Error("boom")) });
    expect(view.kind).toBe("ready");
    if (view.kind !== "ready") return;
    expect(view.probe.reachable).toBe(false);
  });

  it("'구성'과 '도달'을 **다른 줄**로 보여준다", () => {
    const rows = describeServerStatus({ kind: "ready", probe: reachable });
    const labels = rows.map((row) => row.label);
    expect(labels).toContain("구성");
    expect(labels).toContain("도달");
  });

  it("게이트 키는 상태만 보여준다 — 값이 화면에 나오지 않는다", () => {
    const rows = describeServerStatus({ kind: "ready", probe: reachable });
    const gateRow = rows.find((row) => row.label === "게이트 키");
    expect(gateRow?.value).toBe("설정됨");
    // 401로 거부되면 그 사실을 밝힌다.
    const rejected = describeServerStatus({
      kind: "ready",
      probe: { ...reachable, gateKeyValid: false },
    });
    expect(rejected.find((row) => row.label === "게이트 키")?.value).toBe("거부됨");
  });

  it("조회 중에는 도달 여부를 단정하지 않는다", () => {
    const rows = describeServerStatus({ kind: "loading" });
    expect(rows.find((row) => row.label === "도달")?.value).toBe("확인 중…");
  });
});
