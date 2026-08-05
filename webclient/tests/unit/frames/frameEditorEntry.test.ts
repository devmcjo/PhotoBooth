import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { FrameTemplate } from "@domain/frames/types";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import {
  frameEditorInitFor,
  resolveEntryIntent,
  runEditorEntry,
  type EditorEntryDeps,
} from "@screens/frameEditor/frameEditorEntry";
import type { FrameEditorAction } from "@screens/frameEditor/frameEditorState";
import type { FrameEditorIntent } from "@shell/frameEditorIntent";
import { STRINGS } from "@ui/strings";

/**
 * 편집 진입 — 설계 §9.3 · §13 · §16.2
 *
 * 여기서 고정하는 것 셋: ① **재인코딩하지 않는다**(축소가 붙으면 기존 슬롯이 전부 밀린다)
 * ② fork 세션에만 사본 이름을 제안한다 ③ 권한 밖 프레임의 이미지를 애초에 읽지 않는다.
 */

function frame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  return {
    id: "local:public:내 프레임",
    userId: null,
    isDefault: true,
    name: "내 프레임",
    imageUrl: "blob:frames/a.png",
    imageSize: { width: 1200, height: 1600 },
    slots: [{ index: 0, x: 37, y: 41, width: 411, height: 547 }],
    createdAt: "2026-08-01T00:00:00.000Z",
    ...overrides,
  };
}

interface Harness {
  readonly deps: EditorEntryDeps;
  readonly actions: FrameEditorAction[];
  readonly fetched: string[];
  readonly probed: number[];
}

function harness(
  overrides: {
    scopeNames?: readonly string[] | (() => never);
    bytes?: Blob | null;
    probe?: { width: number; height: number } | null;
    stale?: boolean;
  } = {},
): Harness {
  const actions: FrameEditorAction[] = [];
  const fetched: string[] = [];
  const probed: number[] = [];

  const deps: EditorEntryDeps = {
    async scopeNames() {
      const value = overrides.scopeNames;
      if (typeof value === "function") return value();
      return value ?? [];
    },
    uniqueSuffix: () => "deadbeef",
    async fetchBytes(url) {
      fetched.push(url);
      return overrides.bytes === undefined ? new Blob(["png"]) : overrides.bytes;
    },
    async probeSize() {
      probed.push(1);
      return overrides.probe === undefined ? { width: 800, height: 600 } : overrides.probe;
    },
    dispatch: (action) => actions.push(action),
    isStale: () => overrides.stale === true,
  };

  return { deps, actions, fetched, probed };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("E1: 권한 게이트 — 진입 전에 강등한다", () => {
  it("편집 권한이 없으면 신규 생성으로 강등된다", () => {
    // 타인·번들 프레임의 이미지를 애초에 읽지 않는 것이 목적이다.
    const resolved = resolveEntryIntent({ kind: "edit", frame: frame({ id: "bundle:basic" }) }, "manager", "me");
    expect(resolved.blocked).toBe(true);
    expect(resolved.intent).toEqual({ kind: "new" });
  });

  it("power는 서버 공용 프레임을 편집할 수 있다", () => {
    const resolved = resolveEntryIntent(
      { kind: "edit", frame: frame({ id: "srv-1", isDefault: true }) },
      "manager",
      "me",
    );
    expect(resolved.blocked).toBe(false);
  });

  it("게스트는 강등된다", () => {
    expect(resolveEntryIntent({ kind: "edit", frame: frame() }, null, null).blocked).toBe(true);
  });

  it("kind:'new'는 그대로 통과한다", () => {
    const intent: FrameEditorIntent = { kind: "new" };
    expect(resolveEntryIntent(intent, "advanced_user", "me")).toEqual({ intent, blocked: false });
  });
});

describe("E2: 초기값 — 세션 축과 busy", () => {
  it("신규 생성은 배너 없음·busy false다", () => {
    expect(frameEditorInitFor({ kind: "new" })).toEqual({
      sessionSource: "New",
      sourceName: "",
      busy: false,
    });
  });

  it("본인 로컬 편집은 EditOwnLocal·busy true다", () => {
    expect(frameEditorInitFor({ kind: "edit", frame: frame() })).toEqual({
      sessionSource: "EditOwnLocal",
      sourceName: "내 프레임",
      busy: true,
    });
  });

  it("서버 공용 편집은 ForkFromCatalog다", () => {
    expect(
      frameEditorInitFor({ kind: "edit", frame: frame({ id: "srv-1", name: "봄 4컷" }) }),
    ).toMatchObject({ sessionSource: "ForkFromCatalog", sourceName: "봄 4컷" });
  });
});

describe("E3: 진입 절차", () => {
  it("kind:'new'는 아무 액션도 만들지 않는다", async () => {
    const h = harness();
    await runEditorEntry(h.deps, { kind: "new" });
    expect(h.actions).toEqual([]);
    expect(h.fetched).toEqual([]);
  });

  it("본인 로컬 편집은 원본 이름·원본 슬롯을 그대로 쓴다(재인코딩 없음)", async () => {
    const h = harness();
    const target = frame();
    await runEditorEntry(h.deps, { kind: "edit", frame: target });

    expect(h.actions[0]).toEqual({ type: "entryStarted" });
    const ready = h.actions[1];
    expect(ready?.type).toBe("editSessionReady");
    if (ready?.type !== "editSessionReady") return;
    expect(ready.name).toBe("내 프레임");
    expect(ready.imageSize).toEqual(target.imageSize);
    expect(ready.slots).toBe(target.slots);
    // 크기 메타가 있으면 디코드조차 하지 않는다.
    expect(h.probed).toEqual([]);
    expect(h.fetched).toEqual(["blob:frames/a.png"]);
  });

  it("fork 세션은 사본 이름을 제안한다", async () => {
    const h = harness({ scopeNames: ["봄 4컷", "봄 4컷 사본"] });
    await runEditorEntry(h.deps, { kind: "edit", frame: frame({ id: "srv-1", name: "봄 4컷" }) });
    const ready = h.actions[1];
    if (ready?.type !== "editSessionReady") throw new Error("editSessionReady가 없다");
    expect(ready.name).toBe("봄 4컷 사본 2");
  });

  it("이름 열거가 던져도 사본 이름을 만들어 낸다(저장을 막지 않는다)", async () => {
    const h = harness({
      scopeNames: () => {
        throw new Error("boom");
      },
    });
    await runEditorEntry(h.deps, { kind: "edit", frame: frame({ id: "srv-1", name: "봄 4컷" }) });
    const ready = h.actions[1];
    if (ready?.type !== "editSessionReady") throw new Error("editSessionReady가 없다");
    expect(ready.name).toBe("봄 4컷 사본");
  });

  it("크기 메타가 없으면 디코드로 얻는다", async () => {
    const h = harness();
    await runEditorEntry(h.deps, {
      kind: "edit",
      frame: frame({ imageSize: { width: 0, height: 0 } }),
    });
    const ready = h.actions[1];
    if (ready?.type !== "editSessionReady") throw new Error("editSessionReady가 없다");
    expect(ready.imageSize).toEqual({ width: 800, height: 600 });
    expect(h.probed).toHaveLength(1);
  });

  it("이미지를 못 읽으면 폼만 열고 이미지를 비워 둔다(저장은 ③에서 막힌다)", async () => {
    const h = harness({ bytes: null });
    await runEditorEntry(h.deps, { kind: "edit", frame: frame() });
    expect(h.actions[1]).toEqual({
      type: "entryFailed",
      status: STRINGS.frameEditor.editImageMissing,
    });
  });

  it("크기 프로브 실패도 entryFailed다", async () => {
    const h = harness({ probe: null });
    await runEditorEntry(h.deps, {
      kind: "edit",
      frame: frame({ imageSize: { width: 0, height: 0 } }),
    });
    expect(h.actions[1]).toMatchObject({ type: "entryFailed" });
  });

  it("stale이면 entryStarted 이후 아무것도 dispatch하지 않는다", async () => {
    const h = harness({ stale: true });
    await runEditorEntry(h.deps, { kind: "edit", frame: frame() });
    expect(h.actions).toEqual([{ type: "entryStarted" }]);
  });
});
