import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { CATALOG_START_LABEL } from "@domain/frames/frameCatalogProgress";
import {
  FRAME_LOAD_DEGRADED_NOTICE,
  FRAME_LOAD_FAILED_NOTICE,
  MAX_TOTAL_WAIT_MS,
  NO_PROGRESS_TIMEOUT_MS,
  type FrameLoadPhase,
} from "@domain/frames/frameLoadPolicy";
import type { FrameTemplate } from "@domain/frames/types";
import {
  FrameLoadCancelledError,
  type FrameCatalogLoadOptions,
  type FrameCatalogResult,
} from "@adapters/frames/frameCatalog";
import { createLoadDeadline, type LoadDeadline } from "@screens/frameSelect/frameLoadDeadline";
import {
  runFrameLoad,
  type FrameLoadDeps,
  type FrameSelectPatch,
} from "@screens/frameSelect/frameLoadRunner";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 로딩 루틴 — 설계 §5·§6 R1~R15
 *
 * 여기서 고정하는 것: **`finally`가 국면을 무조건 닫는다**(오버레이 고착 0) · quiet 재스캔이
 * 오버레이를 켜지 않는다 · 상한이 **실경과**다 · stale 로딩이 화면을 덮지 않는다.
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

function frame(name: string): FrameTemplate {
  return {
    id: `id-${name}`,
    userId: null,
    isDefault: true,
    name,
    imageUrl: `blob:${name}`,
    imageSize: { width: 100, height: 200 },
    slots: [{ index: 0, x: 0, y: 0, width: 10, height: 10 }],
    createdAt: "",
  };
}

function result(names: string[]): FrameCatalogResult {
  return { frames: names.map(frame), unavailable: [], source: "LocalCache" };
}

/** 주입 시계 — 상한 판정이 tick 수가 아니라 **실경과**임을 검증하기 위한 장치. */
interface FakeClock {
  now: number;
  timers: { id: number; fn: () => void; dueAt: number }[];
  /** 시각을 ms만큼 밀고 만기가 지난 타이머를 발화한다. */
  advance(ms: number): void;
  /** 시각만 밀고 타이머는 발화하지 않는다(탭 스로틀 흉내). */
  jump(ms: number): void;
  /** 타이머를 발화시키되 시각은 그대로(늦게 깨어남 흉내는 jump와 조합). */
  flush(): void;
  deadline(abort: () => void): LoadDeadline;
}

function fakeClock(): FakeClock {
  let nextId = 1;
  const clock: FakeClock = {
    now: 0,
    timers: [],
    advance(ms) {
      const target = clock.now + ms;
      // 만기 순서대로 발화한다(재무장으로 새 타이머가 생길 수 있다).
      for (;;) {
        const due = clock.timers
          .filter((t) => t.dueAt <= target)
          .sort((a, b) => a.dueAt - b.dueAt)[0];
        if (due === undefined) break;
        clock.timers = clock.timers.filter((t) => t.id !== due.id);
        clock.now = due.dueAt;
        due.fn();
      }
      clock.now = target;
    },
    jump(ms) {
      clock.now += ms;
    },
    flush() {
      const pending = [...clock.timers];
      clock.timers = [];
      for (const timer of pending) timer.fn();
    },
    deadline(abort) {
      return createLoadDeadline({
        now: () => clock.now,
        abort,
        setTimer: (fn, ms) => {
          const id = nextId++;
          clock.timers.push({ id, fn, dueAt: clock.now + ms });
          return id;
        },
        clearTimer: (handle) => {
          clock.timers = clock.timers.filter((t) => t.id !== handle);
        },
      });
    },
  };
  return clock;
}

interface Harness {
  readonly deps: FrameLoadDeps;
  readonly patches: FrameSelectPatch[];
  readonly clock: FakeClock;
  stale: boolean;
  phase: FrameLoadPhase;
  frameCount: number;
}

function harness(
  overrides: {
    loadPublic?: (options: FrameCatalogLoadOptions) => Promise<FrameCatalogResult>;
    loadLocalOnly?: () => Promise<FrameCatalogResult>;
    loadPersonal?: (userId: string) => Promise<readonly FrameTemplate[]>;
    userId?: string | null;
    initialPhase?: FrameLoadPhase;
    initialFrameCount?: number;
    apply?: (patch: FrameSelectPatch) => void;
  } = {},
): Harness {
  const patches: FrameSelectPatch[] = [];
  const clock = fakeClock();
  const state = {
    stale: false,
    phase: overrides.initialPhase ?? "Loading",
    frameCount: overrides.initialFrameCount ?? 0,
  };

  const deps: FrameLoadDeps = {
    loadPublic: overrides.loadPublic ?? (async () => result(["A"])),
    loadLocalOnly: overrides.loadLocalOnly ?? (async () => result(["local"])),
    loadPersonal: overrides.loadPersonal ?? (async () => []),
    currentUserId: () => overrides.userId ?? null,
    initialPhase: () => state.phase,
    initialFrameCount: () => state.frameCount,
    isStale: () => state.stale,
    apply: (patch) => {
      patches.push(patch);
      overrides.apply?.(patch);
    },
    createDeadline: (abort) => clock.deadline(abort),
  };

  const h = {
    deps,
    patches,
    clock,
    get stale() {
      return state.stale;
    },
    set stale(value: boolean) {
      state.stale = value;
    },
    get phase() {
      return state.phase;
    },
    set phase(value: FrameLoadPhase) {
      state.phase = value;
    },
    get frameCount() {
      return state.frameCount;
    },
    set frameCount(value: number) {
      state.frameCount = value;
    },
  };
  return h;
}

/** 마지막으로 확정된 국면. */
function finalPhase(patches: FrameSelectPatch[]): FrameLoadPhase | undefined {
  return [...patches].reverse().find((p) => p.phase !== undefined)?.phase;
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("R1·R2: enter 정상 경로", () => {
  it("R1: 첫 patch가 Loading + 시작 문구다(빈 목록 + 활성 [다음]이 없다)", async () => {
    const h = harness();
    await runFrameLoad(h.deps, "enter");
    expect(h.patches[0]).toEqual({
      phase: "Loading",
      loadingMessage: CATALOG_START_LABEL,
      notice: "",
    });
  });

  it("R2: 완료하면 Ready + notice 빈 문자열 + selectedId가 첫 항목이다", async () => {
    const h = harness({ loadPublic: async () => result(["A", "B"]) });
    await runFrameLoad(h.deps, "enter");

    const listPatch = h.patches.find((p) => p.frames !== undefined);
    expect(listPatch?.frames?.map((f) => f.name)).toEqual(["A", "B"]);
    expect(listPatch?.selectedId).toBe("id-A");
    expect(h.patches.at(-1)).toEqual({ phase: "Ready", notice: "" });
  });

  it("진행 보고가 문구 patch로 반영된다", async () => {
    const h = harness({
      loadPublic: async ({ onProgress }) => {
        onProgress?.({ phase: "DownloadingImage", index: 1, total: 3 });
        return result(["A"]);
      },
    });
    await runFrameLoad(h.deps, "enter");
    expect(h.patches.some((p) => p.loadingMessage?.includes("(1/3)") === true)).toBe(true);
  });
});

describe("R3·R4·R5: 실패·중단 경로에서도 국면이 닫힌다", () => {
  it("R3: 취소 → Degraded + 규격 문구", async () => {
    const h = harness({
      loadPublic: async () => {
        throw new FrameLoadCancelledError();
      },
      loadLocalOnly: async () => result(["cached"]),
    });
    await runFrameLoad(h.deps, "enter");
    expect(h.patches.at(-1)).toEqual({
      phase: "Degraded",
      notice: FRAME_LOAD_DEGRADED_NOTICE,
    });
  });

  it("R4: loadLocalOnly까지 실패하면 Failed다", async () => {
    const h = harness({
      loadPublic: async () => {
        throw new Error("network");
      },
      loadLocalOnly: async () => {
        throw new Error("opfs도 죽음");
      },
    });
    await runFrameLoad(h.deps, "enter");
    expect(h.patches.at(-1)).toEqual({ phase: "Failed", notice: FRAME_LOAD_FAILED_NOTICE });
  });

  it("R5: 목록 반영 중 예외가 나도 Loading이 남지 않는다", async () => {
    let thrown = false;
    const h = harness({
      apply: (patch) => {
        if (patch.frames !== undefined && !thrown) {
          thrown = true;
          throw new Error("setState 폭발");
        }
      },
    });
    await expect(runFrameLoad(h.deps, "enter")).rejects.toThrow("setState 폭발");
    // `finally`가 국면을 닫았다 — 오버레이 고착 경로가 없다.
    expect(finalPhase(h.patches)).not.toBe("Loading");
    expect(finalPhase(h.patches)).toBe("Degraded");
  });

  it("finalize는 어떤 경로에서도 Loading을 남기지 않는다", async () => {
    for (const loadPublic of [
      async (): Promise<FrameCatalogResult> => result(["A"]),
      async (): Promise<FrameCatalogResult> => {
        throw new FrameLoadCancelledError();
      },
      async (): Promise<FrameCatalogResult> => {
        throw new Error("x");
      },
    ]) {
      const h = harness({ loadPublic });
      await runFrameLoad(h.deps, "enter");
      expect(finalPhase(h.patches)).not.toBe("Loading");
    }
  });
});

describe("R6: stale 로딩은 화면을 건드리지 않는다", () => {
  it("이탈 후에는 finally가 아무 patch도 내지 않는다", async () => {
    const h = harness({
      loadPublic: async () => {
        h.stale = true; // 화면 이탈
        return result(["A"]);
      },
    });
    await runFrameLoad(h.deps, "enter");
    // 첫 Loading patch 하나뿐이다(목록·국면 확정 없음).
    expect(h.patches).toHaveLength(1);
    expect(h.patches[0]?.phase).toBe("Loading");
  });

  it("취소 예외가 stale에서 잡히면 국면을 덮지 않는다", async () => {
    const h = harness({
      loadPublic: async () => {
        h.stale = true;
        throw new FrameLoadCancelledError();
      },
    });
    await runFrameLoad(h.deps, "enter");
    expect(h.patches).toHaveLength(1);
  });
});

describe("R7·R8·R9: refresh(quiet) — 조용한 재스캔", () => {
  it("R7: Loading patch도 진행 문구 patch도 없다", async () => {
    const h = harness({
      initialPhase: "Ready",
      initialFrameCount: 2,
      loadPublic: async ({ onProgress }) => {
        // quiet에서는 구독 자체를 넘기지 않는다.
        expect(onProgress).toBeUndefined();
        return result(["A", "B"]);
      },
    });
    await runFrameLoad(h.deps, "refresh");
    expect(h.patches.some((p) => p.phase === "Loading")).toBe(false);
    expect(h.patches.some((p) => p.loadingMessage !== undefined)).toBe(false);
    expect(h.patches.at(-1)?.phase).toBe("Ready");
  });

  it("R8: 목록 교체 전에 끊기면 화면의 종전 개수로 판정한다(initialFrameCount 근거)", async () => {
    // ⚠️ `frameCount`를 0으로 시작하면 `finalize(current, 0, …)`가 `Failed`를 내는데, 그것은
    //    "재스캔이 실패했으니 목록도 없다"는 뜻이 아니라 **화면에 이미 떠 있는 3개와 어긋난다**.
    const h = harness({
      initialPhase: "Ready",
      initialFrameCount: 3,
      loadPublic: async () => {
        throw new FrameLoadCancelledError();
      },
      loadLocalOnly: async () => result(["cached"]),
      apply: (patch) => {
        // 목록 교체 단계에서 화면이 폭발한 상황(교체가 일어나지 않았다).
        if (patch.frames !== undefined) throw new Error("setState 폭발");
      },
    });
    await expect(runFrameLoad(h.deps, "refresh")).rejects.toThrow("setState 폭발");
    expect(finalPhase(h.patches)).toBe("Ready");
  });

  it("R8b: 중단 후 로컬 폴백이 프레임을 주면 조용히 Ready를 유지한다", async () => {
    const h = harness({
      initialPhase: "Ready",
      initialFrameCount: 3,
      loadPublic: async () => {
        throw new FrameLoadCancelledError();
      },
      loadLocalOnly: async () => result(["cached"]),
    });
    await runFrameLoad(h.deps, "refresh");
    // quiet이므로 Degraded 안내를 띄우지 않는다 — 삭제 조작에 네트워크 안내가 끼어들지 않는다.
    expect(h.patches.at(-1)).toEqual({ phase: "Ready", notice: "" });
  });

  it("R9: 결과 0개면 Failed / 종전 Failed + 결과 ≥1이면 Ready로 회복", async () => {
    const empty = harness({
      initialPhase: "Ready",
      initialFrameCount: 1,
      loadPublic: async () => ({ frames: [], unavailable: [], source: "Fallback" }),
    });
    await runFrameLoad(empty.deps, "refresh");
    expect(empty.patches.at(-1)?.phase).toBe("Failed");

    const recovered = harness({
      initialPhase: "Failed",
      initialFrameCount: 0,
      loadPublic: async () => result(["A"]),
    });
    await runFrameLoad(recovered.deps, "refresh");
    expect(recovered.patches.at(-1)?.phase).toBe("Ready");
  });
});

describe("R10~R12·R15: 상한 — 무진행 30초 + 총 60초(실경과)", () => {
  it("R10: 보고가 없으면 무진행 30초에 abort된다", async () => {
    const h = harness({
      loadPublic: async ({ signal }) =>
        new Promise<FrameCatalogResult>((_resolve, reject) => {
          signal?.addEventListener("abort", () => reject(new FrameLoadCancelledError()));
        }),
      loadLocalOnly: async () => result(["cached"]),
    });

    const running = runFrameLoad(h.deps, "enter");
    h.clock.advance(NO_PROGRESS_TIMEOUT_MS - 1);
    expect(h.patches.at(-1)?.phase).toBe("Loading"); // 아직 대기 중이다
    h.clock.advance(1);
    await running;
    expect(h.patches.at(-1)?.phase).toBe("Degraded");
  });

  it("R11: 25초마다 보고가 오면 총 60초까지 살아 있다", async () => {
    let reporter: ((index: number) => void) | null = null;
    const h = harness({
      loadPublic: async ({ signal, onProgress }) =>
        new Promise<FrameCatalogResult>((_resolve, reject) => {
          reporter = (index) => onProgress?.({ phase: "DownloadingImage", index, total: 9 });
          signal?.addEventListener("abort", () => reject(new FrameLoadCancelledError()));
        }),
      loadLocalOnly: async () => result(["cached"]),
    });

    const running = runFrameLoad(h.deps, "enter");
    // 25초 간격 보고 2회 → 50초 시점까지 무진행 창이 계속 재무장된다.
    for (const _ of [1, 2]) {
      h.clock.advance(25_000);
      (reporter as unknown as ((index: number) => void) | null)?.(1);
    }
    expect(h.clock.now).toBe(50_000);
    expect(h.patches.at(-1)?.phase).toBeUndefined();

    // 총 상한까지 남은 10초가 지나면 끊긴다(무진행 30초가 아니라 총 60초가 먼저다).
    h.clock.advance(10_000);
    await running;
    expect(h.patches.at(-1)?.phase).toBe("Degraded");
  });

  it("R12: 진행이 계속 와도 총 60초에서 abort된다", async () => {
    let reporter: ((index: number) => void) | null = null;
    const h = harness({
      loadPublic: async ({ signal, onProgress }) =>
        new Promise<FrameCatalogResult>((_resolve, reject) => {
          reporter = (index) => onProgress?.({ phase: "DownloadingImage", index, total: 99 });
          signal?.addEventListener("abort", () => reject(new FrameLoadCancelledError()));
        }),
      loadLocalOnly: async () => result(["cached"]),
    });

    const running = runFrameLoad(h.deps, "enter");
    let aborted = false;
    for (let i = 0; i < 100 && !aborted; i++) {
      h.clock.advance(1_000);
      (reporter as unknown as ((index: number) => void) | null)?.(i);
      aborted = h.clock.now >= MAX_TOTAL_WAIT_MS;
    }
    await running;
    expect(h.clock.now).toBeLessThanOrEqual(MAX_TOTAL_WAIT_MS + 1_000);
    expect(h.patches.at(-1)?.phase).toBe("Degraded");
  });

  it("R15: 판정이 실경과다 — 시계를 점프시키면 tick 수와 무관하게 abort된다", async () => {
    const h = harness({
      loadPublic: async ({ signal }) =>
        new Promise<FrameCatalogResult>((_resolve, reject) => {
          signal?.addEventListener("abort", () => reject(new FrameLoadCancelledError()));
        }),
      loadLocalOnly: async () => result(["cached"]),
    });

    const running = runFrameLoad(h.deps, "enter");
    // 탭이 백그라운드로 가 타이머가 늦게 깨어난 상황: 시각만 크게 밀고 1회만 발화한다.
    h.clock.jump(120_000);
    h.clock.flush();
    await running;
    expect(h.patches.at(-1)?.phase).toBe("Degraded");
  });

  it("총 상한이 이미 지난 뒤 arm()은 타이머를 예약하지 않고 즉시 취소한다", () => {
    const clock = fakeClock();
    let aborts = 0;
    const deadline = clock.deadline(() => aborts++);
    clock.jump(MAX_TOTAL_WAIT_MS + 1);
    deadline.arm();
    expect(aborts).toBe(1);
    expect(clock.timers).toHaveLength(0);
    // dispose는 멱등이다.
    deadline.dispose();
    deadline.dispose();
  });
});

describe("R13·R14: 늦은 보고 · 개인 프레임", () => {
  it("R13: stale 상태의 진행 보고는 문구를 덮지 않는다", async () => {
    const h = harness({
      loadPublic: async ({ onProgress }) => {
        h.stale = true;
        onProgress?.({ phase: "DownloadingImage", index: 1, total: 3 });
        h.stale = false;
        return result(["A"]);
      },
    });
    await runFrameLoad(h.deps, "enter");
    expect(h.patches.some((p) => p.loadingMessage?.includes("(1/3)") === true)).toBe(false);
  });

  it("R14: 개인 프레임 로드 실패가 공용 목록을 지우지 않는다", async () => {
    const h = harness({
      userId: "devmcjo",
      loadPublic: async () => result(["공용"]),
      loadPersonal: async () => {
        throw new Error("개인 프레임 폭발");
      },
    });
    await runFrameLoad(h.deps, "enter");
    const listPatch = h.patches.find((p) => p.frames !== undefined);
    expect(listPatch?.frames?.map((f) => f.name)).toEqual(["공용"]);
    expect(h.patches.at(-1)?.phase).toBe("Ready");
  });

  it("로그인 사용자의 개인 프레임이 공용 뒤에 붙는다", async () => {
    const h = harness({
      userId: "devmcjo",
      loadPublic: async () => result(["공용"]),
      loadPersonal: async () => [frame("내것")],
    });
    await runFrameLoad(h.deps, "enter");
    const listPatch = h.patches.find((p) => p.frames !== undefined);
    expect(listPatch?.frames?.map((f) => f.name)).toEqual(["공용", "내것"]);
  });

  it("게스트는 개인 프레임을 조회하지 않는다", async () => {
    let called = 0;
    const h = harness({
      userId: null,
      loadPersonal: async () => {
        called++;
        return [];
      },
    });
    await runFrameLoad(h.deps, "enter");
    expect(called).toBe(0);
  });
});

describe("정적: 화면 로직에 React가 없다(node 검증 가능성 유지)", () => {
  it.each([
    "screens/frameSelect/frameLoadRunner.ts",
    "screens/frameSelect/frameLoadDeadline.ts",
    "screens/frameSelect/frameSelectActions.ts",
  ])("%s가 react를 import하지 않는다", (file) => {
    const source = readFileSync(join(SRC, file), "utf8");
    expect(/from\s+["']react["']/.test(source)).toBe(false);
  });
});
