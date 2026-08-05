import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { FRAME_LOAD_PHASES, type FrameLoadPhase } from "@domain/frames/frameLoadPolicy";
import type { FrameTemplate } from "@domain/frames/types";
import type { UserRole } from "@domain/roles/userRole";
import {
  canEditSelected,
  canOpenDelete,
  frameSelectPermissions,
  guardInteractive,
  resolveNext,
  runFrameDelete,
  type FrameDeleteDeps,
} from "@screens/frameSelect/frameSelectActions";
import type { FrameLoadReason } from "@screens/frameSelect/frameLoadRunner";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";

/**
 * 권한·게이트·삭제 흐름 — 설계 §9 A1~A11 (03 §4·§15.5)
 *
 * 삭제는 **성공 오인이 가장 비싼** 조작이다: 서버 문서가 남았는데 "삭제되었습니다"를 띄우면
 * 운영자가 같은 프레임을 계속 본다. 4문구가 각각 정확히 언제 나오는지를 여기서 고정한다.
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

function frame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  return {
    id: "srv-1",
    userId: null,
    isDefault: true,
    name: "베이직",
    imageUrl: "blob:a",
    imageSize: { width: 100, height: 200 },
    slots: [{ index: 0, x: 0, y: 0, width: 10, height: 10 }],
    createdAt: "",
    ...overrides,
  };
}

const LOCAL_FRAME = frame({
  id: "local:user:devmcjo:내것",
  userId: "devmcjo",
  isDefault: false,
  name: "내것",
});
/** power가 fork 저장한 **공용** 로컬 프레임 — `userId=null`이다(소유자 판정을 넣으면 회귀한다). */
const FORKED_PUBLIC_LOCAL = frame({ id: "local:public:포크", userId: null, name: "포크" });
const BUNDLE_FRAME = frame({ id: "bundle:번들", name: "번들" });

interface DeleteHarness {
  readonly deps: FrameDeleteDeps;
  readonly log: {
    deleteLocal: string[];
    deleteServer: string[];
    serverFrames: number;
    removed: string[];
    notices: string[];
    reloads: FrameLoadReason[];
    order: string[];
  };
}

function deleteHarness(
  overrides: {
    localOk?: boolean;
    serverResults?: boolean[];
    serverThrows?: unknown;
    serverList?: FrameTemplate[];
    /** applyRemoved가 체크박스 상태를 리셋하는 상황을 흉내낸다(A5). */
    onApplyRemoved?: () => void;
  } = {},
): DeleteHarness {
  const log: DeleteHarness["log"] = {
    deleteLocal: [],
    deleteServer: [],
    serverFrames: 0,
    removed: [],
    notices: [],
    reloads: [],
    order: [],
  };
  let serverCall = 0;

  const deps: FrameDeleteDeps = {
    async deleteLocal(f) {
      log.deleteLocal.push(f.id);
      log.order.push("deleteLocal");
      return overrides.localOk ?? true;
    },
    async deleteServer(id) {
      log.deleteServer.push(id);
      log.order.push("deleteServer");
      if (overrides.serverThrows !== undefined) throw overrides.serverThrows;
      const results = overrides.serverResults ?? [true];
      return results[serverCall++] ?? false;
    },
    async serverFrames() {
      log.serverFrames++;
      return overrides.serverList ?? [];
    },
    applyRemoved(f) {
      log.removed.push(f.id);
      log.order.push("applyRemoved");
      overrides.onApplyRemoved?.();
    },
    setNotice(notice) {
      log.notices.push(notice);
      log.order.push("setNotice");
    },
    async reload(reason) {
      log.reloads.push(reason);
      log.order.push("reload");
    },
  };
  return { deps, log };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("A1: 권한 2축(canWriteFrames × isPower)", () => {
  it.each<[UserRole | null, boolean, boolean]>([
    ["advanced_user", true, false],
    ["manager", true, true],
    ["admin", true, true],
    ["user", false, false],
    ["temp_user", false, false],
    [null, false, false],
  ])("%s → create/delete=%s · isPower=%s", (role, write, power) => {
    const perms = frameSelectPermissions(role);
    expect(perms.canCreateFrame).toBe(write);
    expect(perms.canDeleteFrames).toBe(write);
    expect(perms.isPower).toBe(power);
  });
});

describe("A2: 국면 가드 — Loading·Failed에서 목록 조작이 전부 no-op", () => {
  it.each(FRAME_LOAD_PHASES)("%s", (phase: FrameLoadPhase) => {
    const open = phase === "Ready" || phase === "Degraded";
    expect(guardInteractive(phase)).toBe(open);
    expect(canOpenDelete(LOCAL_FRAME, "manager", phase)).toBe(open);
    expect(canEditSelected(LOCAL_FRAME, "advanced_user", "devmcjo", phase)).toBe(open);

    let called = 0;
    const proceeded = resolveNext({
      phase,
      selected: LOCAL_FRAME,
      configuredCutCount: 6,
      fixFrame: () => called++,
      go: () => undefined,
    });
    expect(proceeded).toBe(open);
    expect(called).toBe(open ? 1 : 0);
  });

  it("선택이 없으면 [다음]이 아무것도 하지 않는다", () => {
    let called = 0;
    expect(
      resolveNext({
        phase: "Ready",
        selected: null,
        configuredCutCount: 6,
        fixFrame: () => called++,
        go: () => called++,
      }),
    ).toBe(false);
    expect(called).toBe(0);
  });

  it("삭제 게이트가 출처를 본다(번들은 power도 못 지운다)", () => {
    expect(canOpenDelete(BUNDLE_FRAME, "admin", "Ready")).toBe(false);
    expect(canOpenDelete(null, "admin", "Ready")).toBe(false);
    // 공용 DB 프레임은 power만.
    expect(canOpenDelete(frame(), "advanced_user", "Ready")).toBe(false);
    expect(canOpenDelete(frame(), "manager", "Ready")).toBe(true);
    // power가 fork한 공용 로컬 프레임은 userId=null이어도 지울 수 있다(§9.3).
    expect(canOpenDelete(FORKED_PUBLIC_LOCAL, "manager", "Ready")).toBe(true);
    expect(canOpenDelete(FORKED_PUBLIC_LOCAL, "advanced_user", "Ready")).toBe(true);
  });
});

describe("A3: [다음]이 컷 수를 정확히 1회 해석한다(VF-12)", () => {
  it("fixFrame 1회 + go 1회", () => {
    const calls: unknown[][] = [];
    let went = 0;
    resolveNext({
      phase: "Ready",
      selected: LOCAL_FRAME,
      configuredCutCount: 8,
      fixFrame: (f, cut) => calls.push([f.id, cut]),
      go: () => went++,
    });
    expect(calls).toEqual([["local:user:devmcjo:내것", 8]]);
    expect(went).toBe(1);
  });
});

describe("A4·A6·A7·A8: 삭제 결과 4문구", () => {
  it("A4-1: 로컬 실패 + 서버 미시도 → 로컬 실패 문구만", async () => {
    const h = deleteHarness({ localOk: false });
    const outcome = await runFrameDelete(h.deps, {
      frame: LOCAL_FRAME,
      alsoServer: false,
      isPower: false,
    });
    expect(outcome.localOk).toBe(false);
    expect(h.log.notices).toEqual([STRINGS.frames.deleteLocalFailed]);
    expect(h.log.deleteServer).toEqual([]);
  });

  it("A4-2: 서버 삭제 성공 → 성공 문구", async () => {
    const h = deleteHarness({ serverResults: [true] });
    const outcome = await runFrameDelete(h.deps, {
      frame: frame(),
      alsoServer: true,
      isPower: true,
    });
    expect(outcome.notice).toBe(STRINGS.frames.deleteServerOk);
    expect(h.log.deleteServer).toEqual(["srv-1"]);
  });

  it("A6: {deleted:false} → 이름 매칭 재시도 → 그래도 없으면 문서 미발견", async () => {
    // 재시도 성공
    const retried = deleteHarness({
      serverResults: [false, true],
      serverList: [frame({ id: "real-id", name: "베이직" })],
    });
    const ok = await runFrameDelete(retried.deps, {
      frame: frame({ id: "local:public:베이직" }),
      alsoServer: true,
      isPower: true,
    });
    // `local:` 접두를 떼고 시도한 뒤, 이름 매칭으로 실 id에 재시도한다.
    expect(retried.log.deleteServer).toEqual(["public:베이직", "real-id"]);
    expect(retried.log.serverFrames).toBe(1);
    expect(ok.notice).toBe(STRINGS.frames.deleteServerOk);

    // 재시도해도 없음
    const missing = deleteHarness({ serverResults: [false], serverList: [] });
    const notFound = await runFrameDelete(missing.deps, {
      frame: frame({ name: "없는것" }),
      alsoServer: true,
      isPower: true,
    });
    expect(notFound.notice).toBe("로컬은 삭제했지만 서버에서 '없는것' 문서를 찾지 못했습니다.");
  });

  it("A7: 서버 삭제 예외 → 사유가 담긴 실패 문구", async () => {
    const h = deleteHarness({ serverThrows: new Error("권한이 없습니다.") });
    const outcome = await runFrameDelete(h.deps, {
      frame: frame(),
      alsoServer: true,
      isPower: true,
    });
    expect(outcome.notice).toBe("서버 삭제 실패: 권한이 없습니다.");
    expect(outcome.localOk).toBe(true);
  });

  it("A8: 로컬 실패 + 서버 성공 → 두 사실이 함께 보고된다", async () => {
    const h = deleteHarness({ localOk: false, serverResults: [true] });
    const outcome = await runFrameDelete(h.deps, {
      frame: frame(),
      alsoServer: true,
      isPower: true,
    });
    expect(outcome.notice).toBe(
      STRINGS.frames.deleteServerOk + STRINGS.frames.deleteLocalFailedSuffix,
    );
  });

  it("성공 경로는 안내를 비운다(직전 실패 문구가 남지 않게)", async () => {
    const h = deleteHarness();
    const outcome = await runFrameDelete(h.deps, {
      frame: LOCAL_FRAME,
      alsoServer: false,
      isPower: false,
    });
    expect(outcome.notice).toBe("");
    expect(h.log.notices).toEqual([""]);
  });
});

describe("A5·A9·A10: 순서와 게이트", () => {
  it("A5: alsoServer가 오버레이 닫기 전에 확정된다", async () => {
    let checkbox = true;
    const h = deleteHarness({
      serverResults: [true],
      // 오버레이가 닫히며 체크가 리셋되는 상황.
      onApplyRemoved: () => {
        checkbox = false;
      },
    });
    const outcome = await runFrameDelete(h.deps, {
      frame: frame(),
      alsoServer: checkbox,
      isPower: true,
    });
    expect(checkbox).toBe(false); // 실제로 리셋됐다
    expect(outcome.serverAttempted).toBe(true);
    expect(h.log.deleteServer).toEqual(["srv-1"]);
  });

  it("순서: 로컬 삭제 → 목록 제거 → 서버 삭제 → 안내 → 재스캔", async () => {
    const h = deleteHarness({ serverResults: [true] });
    await runFrameDelete(h.deps, { frame: frame(), alsoServer: true, isPower: true });
    expect(h.log.order).toEqual([
      "deleteLocal",
      "applyRemoved",
      "deleteServer",
      "setNotice",
      "reload",
    ]);
  });

  it("A9: 삭제 후 재스캔이 refresh(조용한 갱신)로 호출된다", async () => {
    const h = deleteHarness();
    await runFrameDelete(h.deps, { frame: LOCAL_FRAME, alsoServer: false, isPower: false });
    expect(h.log.reloads).toEqual(["refresh"]);
  });

  it("A10: 비power가 alsoServer=true를 넣어도 서버 삭제가 일어나지 않는다", async () => {
    const h = deleteHarness();
    const outcome = await runFrameDelete(h.deps, {
      frame: LOCAL_FRAME,
      alsoServer: true,
      isPower: false,
    });
    expect(outcome.serverAttempted).toBe(false);
    expect(h.log.deleteServer).toEqual([]);
    expect(h.log.serverFrames).toBe(0);
  });
});

describe("A11: 정적 FR-2 — canDeleteFrame 호출이 2인자다", () => {
  function collect(dir: string): string[] {
    const result: string[] = [];
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry);
      if (statSync(full).isDirectory()) result.push(...collect(full));
      else if (entry.endsWith(".ts") || entry.endsWith(".tsx")) result.push(full);
    }
    return result;
  }

  it("src 전체에서 소유자(userId)를 넘기는 호출이 0건이다", () => {
    // 소유자 판정을 넣으면 power가 fork 저장한 **공용** 로컬 프레임(userId=null)의 삭제 능력이
    // 회귀한다(analysis/13 §6.1의 회귀 경고 그대로).
    const files = collect(SRC).filter((f) => !f.endsWith("frameEditPolicy.ts"));
    const offenders: string[] = [];
    for (const file of files) {
      const source = readFileSync(file, "utf8")
        .replace(/\/\*[\s\S]*?\*\//g, "")
        .replace(/(^|[^:])\/\/.*$/gm, "$1");
      const pattern = /canDeleteFrame\s*\(([^)]*)\)/g;
      let match: RegExpExecArray | null;
      while ((match = pattern.exec(source)) !== null) {
        const args = match[1]!.split(",").filter((a) => a.trim().length > 0);
        if (args.length !== 2) {
          offenders.push(`${relative(SRC, file).split(sep).join("/")}: ${match[0]}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });
});
