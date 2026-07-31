import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { FrameTemplate } from "@domain/frames/types";
import {
  FrameLoadCancelledError,
  type FrameCatalogResult,
} from "@adapters/frames/frameCatalog";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import type { FramePickerPatch, FramePickerState } from "@screens/frameEditor/frameEditorState";
import { INITIAL_PICKER_STATE } from "@screens/frameEditor/frameEditorState";
import {
  runFramePickerLoad,
  type FramePickerDeps,
} from "@screens/frameEditor/framePickerRunner";
import { STRINGS } from "@ui/strings";

/**
 * 피커 목록 로더 — 설계 §7.2 (03 §15.4)
 *
 * `finally`가 국면을 **무조건** 확정하므로 `loading` 고착이 구조적으로 불가능하다.
 * 자동 선택을 하지 않는 것도 규격이다 — 적용이 파괴적이라 오조작 시 편집 중인 작업이 날아간다.
 */

function frame(id: string, name = id): FrameTemplate {
  return {
    id,
    userId: null,
    isDefault: true,
    name,
    imageUrl: `blob:${id}`,
    imageSize: { width: 1200, height: 1600 },
    slots: [{ index: 0, x: 0, y: 0, width: 10, height: 10 }],
    createdAt: "2026-08-01T00:00:00.000Z",
  };
}

function result(frames: readonly FrameTemplate[]): FrameCatalogResult {
  return { frames, unavailable: [], source: "Server" };
}

interface Harness {
  readonly deps: FramePickerDeps;
  readonly patches: FramePickerPatch[];
  /** 최종 상태(모든 patch를 순서대로 적용한 결과). */
  state(): FramePickerState;
  readonly armed: number[];
  readonly disposed: number[];
}

function harness(
  overrides: {
    publicResult?: FrameCatalogResult | (() => never);
    localOnly?: FrameCatalogResult | (() => never);
    personal?: readonly FrameTemplate[] | (() => never);
    userId?: string | null;
    stale?: boolean;
  } = {},
): Harness {
  const patches: FramePickerPatch[] = [];
  const armed: number[] = [];
  const disposed: number[] = [];

  const deps: FramePickerDeps = {
    async loadPublic() {
      const value = overrides.publicResult;
      if (typeof value === "function") return value();
      return value ?? result([frame("srv-1")]);
    },
    async loadLocalOnly() {
      const value = overrides.localOnly;
      if (typeof value === "function") return value();
      return value ?? result([]);
    },
    async loadPersonal() {
      const value = overrides.personal;
      if (typeof value === "function") return value();
      return value ?? [];
    },
    currentUserId: () => overrides.userId ?? null,
    isStale: () => overrides.stale === true,
    apply: (patch) => patches.push(patch),
    createDeadline: () => ({
      arm: () => armed.push(1),
      dispose: () => disposed.push(1),
    }),
    registerAbort: () => undefined,
  };

  function state(): FramePickerState {
    return patches.reduce<FramePickerState>(
      (prev, patch) => ({
        phase: patch.phase ?? prev.phase,
        frames: patch.frames ?? prev.frames,
        notice: patch.notice ?? prev.notice,
        selectedId: "selectedId" in patch ? (patch.selectedId ?? null) : prev.selectedId,
      }),
      INITIAL_PICKER_STATE,
    );
  }

  return { deps, patches, state, armed, disposed };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("K1: 정상 경로", () => {
  it("공용 + 개인이 합쳐지고 자동 선택하지 않는다", async () => {
    const h = harness({
      publicResult: result([frame("srv-1"), frame("srv-2")]),
      personal: [frame("local:user:me:내것", "내것")],
      userId: "me",
    });
    await runFramePickerLoad(h.deps);

    const state = h.state();
    expect(state.phase).toBe("ready");
    expect(state.frames.map((f) => f.id)).toEqual(["srv-1", "srv-2", "local:user:me:내것"]);
    // ★ 자동 선택 금지 — [불러오기]는 selectedId가 있을 때만 활성한다(§7.4).
    expect(state.selectedId).toBeNull();
    expect(state.notice).toBe("");
  });

  it("이미지가 없는 후보는 걸러진다", async () => {
    const broken = { ...frame("srv-3"), imageUrl: "" };
    const h = harness({ publicResult: result([frame("srv-1"), broken]) });
    await runFramePickerLoad(h.deps);
    expect(h.state().frames.map((f) => f.id)).toEqual(["srv-1"]);
  });

  it("비로그인이면 개인 프레임을 조회하지 않는다", async () => {
    const h = harness({
      userId: null,
      personal: () => {
        throw new Error("호출되면 안 된다");
      },
    });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).toBe("ready");
  });

  it("상한 타이머를 무장하고 finally에서 해제한다", async () => {
    const h = harness();
    await runFramePickerLoad(h.deps);
    expect(h.armed).toHaveLength(1);
    expect(h.disposed).toHaveLength(1);
  });
});

describe("K2: 폴백·실패", () => {
  it("loadPublic 실패 → loadLocalOnly 폴백", async () => {
    const h = harness({
      publicResult: () => {
        throw new Error("network");
      },
      localOnly: result([frame("local-1")]),
    });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).toBe("ready");
    expect(h.state().frames.map((f) => f.id)).toEqual(["local-1"]);
  });

  it("둘 다 실패 → failed + pickerFailed", async () => {
    const h = harness({
      publicResult: () => {
        throw new FrameLoadCancelledError();
      },
      localOnly: () => {
        throw new Error("boom");
      },
    });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).toBe("failed");
    expect(h.state().notice).toBe(STRINGS.frameEditor.pickerFailed);
  });

  it("정상 조회인데 후보가 0개 → failed + pickerEmpty(사유가 다르다)", async () => {
    const h = harness({ publicResult: result([]) });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).toBe("failed");
    expect(h.state().notice).toBe(STRINGS.frameEditor.pickerEmpty);
  });

  it("개인 로드 실패가 공용 목록을 무너뜨리지 않는다", async () => {
    const h = harness({
      publicResult: result([frame("srv-1")]),
      userId: "me",
      personal: () => {
        throw new Error("개인 실패");
      },
    });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).toBe("ready");
    expect(h.state().frames.map((f) => f.id)).toEqual(["srv-1"]);
  });

  it("상한 abort에서도 finally가 국면을 확정한다(loading 고착 0)", async () => {
    const h = harness({
      publicResult: () => {
        throw new FrameLoadCancelledError();
      },
      localOnly: result([frame("local-1")]),
    });
    await runFramePickerLoad(h.deps);
    expect(h.state().phase).not.toBe("loading");
    expect(h.disposed).toHaveLength(1);
  });
});

describe("K3: stale — 폐기된 오버레이의 상태를 쓰지 않는다", () => {
  it("isStale이 true면 finally가 아무것도 apply하지 않는다", async () => {
    const h = harness({ stale: true });
    await runFramePickerLoad(h.deps);
    // 첫 초기화 patch 1건만 남는다(그 시점에는 아직 오버레이가 살아 있었다).
    expect(h.patches).toHaveLength(1);
    expect(h.patches[0]!.phase).toBe("loading");
    expect(h.disposed).toHaveLength(1);
  });

  it("stale이어도 타이머는 반드시 해제된다", async () => {
    const h = harness({
      stale: true,
      publicResult: () => {
        throw new Error("boom");
      },
    });
    await runFramePickerLoad(h.deps);
    expect(h.disposed).toHaveLength(1);
  });
});
