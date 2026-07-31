import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { FrameSessionSource } from "@domain/frames/frameSavePolicy";
import type { Slot } from "@domain/frames/types";
import type { UserRole } from "@domain/roles/userRole";
import type { CreateFrameResponse } from "@adapters/http/frameRepository";
import type { SignedPutOutcome } from "@adapters/http/uploadGateway";
import type { SaveFrameInput } from "@adapters/storage/frameStore";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import {
  runFrameSave,
  type FrameSaveDeps,
  type FrameSaveRequest,
} from "@screens/frameEditor/frameEditorSave";
import { STRINGS } from "@ui/strings";

/**
 * 저장 파이프라인 — 설계 §5 (03 §11.3·§11.4)
 *
 * 여기서 고정하는 것 둘: ① **검증 재실행**(오버레이 경로 우회 차단)
 * ② **원자성**(서버 등록 실패 시 `saveLocal`에 도달하지 않는다). ②가 깨지면 재시도가
 * ⑦ 이름 충돌 가드와 **자기 자신과 충돌**해 저장이 영구히 막힌다.
 */

const SLOTS: readonly Slot[] = [
  { index: 0, x: 0, y: 0, width: 100, height: 100 },
  { index: 1, x: 200, y: 0, width: 100, height: 100 },
];

const PNG = new Blob(["png"], { type: "image/png" });

interface Harness {
  readonly deps: FrameSaveDeps;
  readonly order: string[];
  readonly saved: SaveFrameInput[];
  readonly deleted: string[];
  readonly statuses: string[];
  readonly navigated: number[];
  readonly putHeaders: Record<string, string>[];
}

function harness(
  overrides: {
    scopeNames?: readonly string[];
    personalCount?: number;
    created?: CreateFrameResponse | (() => never);
    put?: SignedPutOutcome;
    saveLocalFails?: boolean;
    deleteThrows?: boolean;
  } = {},
): Harness {
  const order: string[] = [];
  const saved: SaveFrameInput[] = [];
  const deleted: string[] = [];
  const statuses: string[] = [];
  const navigated: number[] = [];
  const putHeaders: Record<string, string>[] = [];

  const deps: FrameSaveDeps = {
    async scopeNames() {
      return overrides.scopeNames ?? [];
    },
    async personalCount() {
      return overrides.personalCount ?? 0;
    },
    async createServerFrame() {
      order.push("createServerFrame");
      const value = overrides.created;
      if (typeof value === "function") return value();
      return (
        value ?? {
          frame: {
            id: "abc",
            userId: null,
            isDefault: true,
            name: "여름 6컷",
            imageUrl: "https://cdn/x.png",
            imageSize: { width: 1200, height: 1600 },
            slots: SLOTS,
            createdAt: "2026-08-01T00:00:00.000Z",
          },
          putUrl: "https://signed.example.com/put",
          requiredHeaders: { "Content-Type": "image/png", "x-goog-meta-a": "b" },
        }
      );
    },
    async putImage(request) {
      order.push("putImage");
      putHeaders.push({ ...request.headers });
      return overrides.put ?? { ok: true, status: 200, bytes: 3, elapsedMs: 1 };
    },
    async deleteServerFrame(id) {
      order.push("deleteServerFrame");
      deleted.push(id);
      if (overrides.deleteThrows === true) throw new Error("정리 실패");
      return true;
    },
    async saveLocal(input) {
      order.push("saveLocal");
      saved.push(input);
      return overrides.saveLocalFails === true ? null : { id: "local:public:여름 6컷" };
    },
    setStatus(message) {
      statuses.push(message);
    },
    goToFrameSelect() {
      order.push("goToFrameSelect");
      navigated.push(1);
    },
  };

  return { deps, order, saved, deleted, statuses, navigated, putHeaders };
}

function request(overrides: Partial<FrameSaveRequest> = {}): FrameSaveRequest {
  return {
    role: "manager" as UserRole,
    userId: "devmcjo",
    sessionSource: "New" as FrameSessionSource,
    name: "여름 6컷",
    sourceName: "",
    slots: SLOTS,
    imageSize: { width: 1200, height: 1600 },
    png: PNG,
    registerToServer: false,
    ...overrides,
  };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("V1: 검증 재실행 — 오버레이 경로 우회 차단", () => {
  it("검증에 실패하면 서버·로컬 호출이 0회다", async () => {
    const h = harness();
    const outcome = await runFrameSave(h.deps, request({ name: "  ", registerToServer: true }));
    expect(outcome).toEqual({ status: "rejected", reason: "name-empty" });
    expect(h.order).toEqual([]);
    expect(h.statuses).toEqual([STRINGS.frames.nameEmpty]);
  });

  it("권한이 없는 역할은 오버레이 경로로 들어와도 막힌다", async () => {
    // 오버레이는 이미 닫혔고 registerToServer=true로 들어온 상태를 흉내낸다.
    const h = harness();
    const outcome = await runFrameSave(
      h.deps,
      request({ role: "user", registerToServer: true }),
    );
    expect(outcome).toEqual({ status: "rejected", reason: "no-write-permission" });
    expect(h.order).toEqual([]);
  });

  it("게스트는 not-logged-in이다", async () => {
    const h = harness();
    expect((await runFrameSave(h.deps, request({ role: null }))).status).toBe("rejected");
    expect(h.order).toEqual([]);
  });

  it("스코프 이름 충돌이 저장을 막는다(다른 프레임 파괴 방지)", async () => {
    const h = harness({ scopeNames: ["여름 6컷"] });
    const outcome = await runFrameSave(h.deps, request());
    expect(outcome).toEqual({ status: "rejected", reason: "name-conflict" });
    expect(h.order).toEqual([]);
  });

  it("이름 열거가 던져도 저장은 진행된다(⑦ 비차단)", async () => {
    const h = harness();
    h.deps.scopeNames = async () => {
      throw new Error("boom");
    };
    h.deps.personalCount = async () => {
      throw new Error("boom");
    };
    expect((await runFrameSave(h.deps, request())).status).toBe("saved");
  });
});

describe("V2: 등록 축 — requiresServerRegisterPrompt와 같은 함수", () => {
  it("advanced_user는 registerToServer:true여도 서버를 부르지 않는다", async () => {
    const h = harness();
    const outcome = await runFrameSave(
      h.deps,
      request({ role: "advanced_user", registerToServer: true }),
    );
    expect(outcome).toEqual({ status: "saved", registeredToServer: false });
    expect(h.order).toEqual(["saveLocal", "goToFrameSelect"]);
    expect(h.saved[0]!.scope).toBe("user");
    expect(h.saved[0]!.ownerId).toBe("devmcjo");
    expect(h.saved[0]!.dbId).toBeNull();
  });

  it("power + 편집 세션(ForkFromCatalog)도 서버를 부르지 않는다", async () => {
    const h = harness();
    const outcome = await runFrameSave(
      h.deps,
      request({ sessionSource: "ForkFromCatalog", sourceName: "봄 4컷", registerToServer: true }),
    );
    expect(outcome).toEqual({ status: "saved", registeredToServer: false });
    expect(h.order).toEqual(["saveLocal", "goToFrameSelect"]);
  });

  it("power 공용 저장은 scope:'public'·ownerId:null이다", async () => {
    const h = harness();
    await runFrameSave(h.deps, request());
    expect(h.saved[0]!.scope).toBe("public");
    expect(h.saved[0]!.ownerId).toBeNull();
  });

  it("체크 off면 서버 호출 0회 + dbId:null이다", async () => {
    const h = harness();
    await runFrameSave(h.deps, request({ registerToServer: false }));
    expect(h.order).toEqual(["saveLocal", "goToFrameSelect"]);
    expect(h.saved[0]!.dbId).toBeNull();
  });
});

describe("V3: 성공 순서 + `_` 하드 거부", () => {
  it("createServerFrame → putImage → saveLocal → goToFrameSelect", async () => {
    const h = harness();
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome).toEqual({ status: "saved", registeredToServer: true });
    expect(h.order).toEqual([
      "createServerFrame",
      "putImage",
      "saveLocal",
      "goToFrameSelect",
    ]);
    expect(h.saved[0]!.dbId).toBe("abc");
    expect(h.statuses[0]).toBe(STRINGS.frameEditor.saving);
    expect(h.statuses.at(-1)).toBe("");
  });

  it("requiredHeaders를 그대로 PUT에 넘긴다(M14 — 골라 담지 않는다)", async () => {
    const h = harness();
    await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(h.putHeaders[0]).toEqual({ "Content-Type": "image/png", "x-goog-meta-a": "b" });
  });

  it("이름에 '_'가 있으면 서버를 부르지 않는다(400 왕복 낭비 방지)", async () => {
    const h = harness();
    const outcome = await runFrameSave(
      h.deps,
      request({ name: "a_b", registerToServer: true }),
    );
    expect(outcome.status).toBe("server-failed");
    expect(h.order).toEqual([]);
    expect(h.statuses.at(-1)).toBe(STRINGS.frames.nameUnderscoreRejected);
  });

  it("'_' 이름도 체크 off면 로컬 저장은 된다(비차단 경고 축)", async () => {
    const h = harness();
    expect((await runFrameSave(h.deps, request({ name: "a_b" }))).status).toBe("saved");
  });
});

describe("V4: 원자성 — 서버 실패 시 saveLocal 0회", () => {
  it("createServerFrame 예외 → saveLocal 0회 · 화면 전환 없음", async () => {
    const h = harness({
      created: () => {
        throw new Error("500 서버 오류");
      },
    });
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome.status).toBe("server-failed");
    expect(h.order).toEqual(["createServerFrame"]);
    expect(h.navigated).toEqual([]);
    expect(h.statuses.at(-1)).toContain("500 서버 오류");
    // 원자성 안내 문구가 붙어 있어야 재시도 경로를 안내할 수 있다.
    expect(h.statuses.at(-1)).toContain("'서버에도 등록'을 해제하고");
  });

  it("frame === null → saveLocal 0회", async () => {
    const h = harness({ created: { frame: null, putUrl: "https://x", requiredHeaders: {} } });
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome.status).toBe("server-failed");
    expect(h.order).toEqual(["createServerFrame"]);
  });

  it("putUrl === null → 문서를 정리하고 saveLocal 0회", async () => {
    const h = harness({
      created: {
        frame: {
          id: "abc",
          userId: null,
          isDefault: true,
          name: "여름 6컷",
          imageUrl: "",
          imageSize: { width: 1200, height: 1600 },
          slots: SLOTS,
          createdAt: "",
        },
        putUrl: null,
        requiredHeaders: {},
      },
    });
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome.status).toBe("server-failed");
    expect(h.order).toEqual(["createServerFrame", "deleteServerFrame"]);
    expect(h.deleted).toEqual(["abc"]);
  });

  it("이미지 PUT 실패 → 문서를 정리하고 saveLocal 0회", async () => {
    const h = harness({ put: { ok: false, failure: "http", status: 403, elapsedMs: 5 } });
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome.status).toBe("server-failed");
    expect(h.order).toEqual(["createServerFrame", "putImage", "deleteServerFrame"]);
    expect(h.deleted).toEqual(["abc"]);
    expect(h.navigated).toEqual([]);
    expect(h.statuses.at(-1)).toContain("403");
  });

  it("정리(DELETE)가 던져도 결과 문구가 바뀌지 않는다", async () => {
    const h = harness({
      put: { ok: false, failure: "network", status: null, elapsedMs: 5 },
      deleteThrows: true,
    });
    const outcome = await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(outcome.status).toBe("server-failed");
    expect(h.statuses.at(-1)).toContain("network");
    expect(h.order).toEqual(["createServerFrame", "putImage", "deleteServerFrame"]);
  });
});

describe("V5: 로컬 저장 실패", () => {
  it("saveLocal이 null이면 local-failed이고 화면 전환이 없다", async () => {
    const h = harness({ saveLocalFails: true });
    const outcome = await runFrameSave(h.deps, request());
    expect(outcome).toEqual({ status: "local-failed" });
    expect(h.navigated).toEqual([]);
    expect(h.statuses.at(-1)).toBe(STRINGS.frameEditor.saveLocalFailed);
  });

  it("서버 등록 뒤 로컬이 실패해도 서버 문서를 정리하지 않는다(문구는 하나만)", async () => {
    const h = harness({ saveLocalFails: true });
    await runFrameSave(h.deps, request({ registerToServer: true }));
    expect(h.order).toEqual(["createServerFrame", "putImage", "saveLocal"]);
    expect(h.deleted).toEqual([]);
  });
});

describe("V6: ⑧ 개인 상한", () => {
  it("개인 10개 + 새 이름 → limit-reached(서버·로컬 호출 0회)", async () => {
    const h = harness({ personalCount: 10 });
    const outcome = await runFrameSave(h.deps, request({ role: "advanced_user" }));
    expect(outcome).toEqual({ status: "rejected", reason: "limit-reached" });
    expect(h.order).toEqual([]);
  });

  it("개인 10개 + 덮어쓰기(EditOwnLocal)는 저장된다", async () => {
    const h = harness({ personalCount: 10, scopeNames: ["여름 6컷"] });
    const outcome = await runFrameSave(
      h.deps,
      request({ role: "advanced_user", sessionSource: "EditOwnLocal" }),
    );
    expect(outcome.status).toBe("saved");
  });
});
