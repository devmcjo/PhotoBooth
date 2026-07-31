import { describe, expect, it } from "vitest";
import {
  FALLBACK_FRAME_ID,
  FALLBACK_HEIGHT,
  FALLBACK_SLOT_COUNT,
  FALLBACK_WIDTH,
  fallbackFrameSlots,
} from "@domain/frames/fallbackFrameSpec";
import {
  buildCatalog,
  dedupeByName,
  hasUnderscoreCacheConflict,
  serverFramesToCache,
} from "@domain/frames/frameCatalogPolicy";
import { canDeleteFrame, canEditFrame, requiresFork } from "@domain/frames/frameEditPolicy";
import {
  classifyFrameOrigin,
  isDbDefault,
  isOwnedLocal,
} from "@domain/frames/frameOrigin";
import {
  MAX_FRAME_NAME_LENGTH,
  underscoreWarning,
  validateFrameName,
  validateFrameNameForServer,
  nextCopyName,
} from "@domain/frames/frameNaming";
import {
  isValidLayout,
  MAX_SLOTS,
  MIN_SLOTS,
  resizeKeepingAspect,
} from "@domain/frames/slotLayout";
import {
  DEFAULT_SLOT_ASPECT,
  slotAspectToLabel,
  slotAspectToRatio,
  SLOT_ASPECTS,
} from "@domain/frames/slotAspect";
import { parseSlotsFile, serializeSlotsFile } from "@domain/frames/slotsFile";
import { slotAspectRatio, type FrameTemplate } from "@domain/frames/types";

function frame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  return {
    id: "local:내프레임",
    userId: "me@example.com",
    isDefault: false,
    name: "내프레임",
    imageUrl: "",
    imageSize: { width: 1200, height: 1600 },
    slots: [],
    createdAt: "2026-07-30T00:00:00.000Z",
    ...overrides,
  };
}

describe("slotAspect", () => {
  it("3종 비율과 라벨을 제공하고 기본은 3:4다", () => {
    expect(SLOT_ASPECTS).toEqual(["Ratio4x3", "Ratio3x4", "Ratio1x1"]);
    expect(DEFAULT_SLOT_ASPECT).toBe("Ratio3x4");
    expect(slotAspectToRatio("Ratio4x3")).toBeCloseTo(4 / 3, 12);
    expect(slotAspectToRatio("Ratio3x4")).toBe(0.75);
    expect(slotAspectToRatio("Ratio1x1")).toBe(1);
    expect(SLOT_ASPECTS.map(slotAspectToLabel)).toEqual(["4:3", "3:4", "1:1"]);
  });
});

describe("slot 기하 보조", () => {
  it("슬롯 종횡비를 계산하고 높이 0을 방어한다", () => {
    expect(slotAspectRatio({ index: 0, x: 0, y: 0, width: 300, height: 400 })).toBe(0.75);
    expect(slotAspectRatio({ index: 0, x: 0, y: 0, width: 300, height: 0 })).toBe(0);
  });

  it("resizeKeepingAspect가 비율을 유지한다", () => {
    const slot = { index: 0, x: 10, y: 20, width: 100, height: 100 };
    expect(resizeKeepingAspect(slot, 300, 0.75)).toEqual({
      index: 0,
      x: 10,
      y: 20,
      width: 300,
      height: 400,
    });
  });

  it("targetAspect가 0 이하면 높이를 유지하고, 폭은 최소 1이다", () => {
    const slot = { index: 0, x: 0, y: 0, width: 100, height: 77 };
    expect(resizeKeepingAspect(slot, 50, 0).height).toBe(77);
    expect(resizeKeepingAspect(slot, -10, 1).width).toBe(1);
  });

  it("슬롯 개수 상·하한이 규격값이다", () => {
    expect(MIN_SLOTS).toBe(1);
    expect(MAX_SLOTS).toBe(6);
    expect(isValidLayout([], 1200, 1600)).toBe(false);
  });
});

describe("fallbackFrameSpec — analysis/14 §4.7", () => {
  it("1200×1600 · 슬롯 4개 · 2×2 격자다", () => {
    expect(FALLBACK_FRAME_ID).toBe("fallback");
    expect(FALLBACK_WIDTH).toBe(1200);
    expect(FALLBACK_HEIGHT).toBe(1600);
    const slots = fallbackFrameSlots();
    expect(slots).toHaveLength(FALLBACK_SLOT_COUNT);
    expect(slots[0]).toEqual({ index: 0, x: 80, y: 117, width: 490, height: 653 });
    expect(slots[3]).toEqual({ index: 3, x: 630, y: 830, width: 490, height: 653 });
  });

  it("겹치지 않고 프레임 경계 안에 있다", () => {
    expect(isValidLayout(fallbackFrameSlots(), FALLBACK_WIDTH, FALLBACK_HEIGHT)).toBe(true);
  });
});

describe("frameOrigin — 출처 판정", () => {
  it("id 접두로 분류한다", () => {
    expect(classifyFrameOrigin(frame({ id: "bundle:베이직" }))).toBe("Bundle");
    expect(classifyFrameOrigin(frame({ id: "fallback" }))).toBe("Fallback");
    expect(classifyFrameOrigin(frame({ id: "" }))).toBe("Fallback");
    expect(classifyFrameOrigin(frame({ id: "local:내것" }))).toBe("UserLocal");
    expect(classifyFrameOrigin(frame({ id: "abc123DB" }))).toBe("DbDefault");
  });

  it("소유 로컬은 userId가 정확히 일치해야 한다", () => {
    const mine = frame({ id: "local:x", userId: "me" });
    expect(isOwnedLocal(mine, "me")).toBe(true);
    expect(isOwnedLocal(mine, "other")).toBe(false);
    expect(isOwnedLocal(mine, null)).toBe(false);
    expect(isOwnedLocal(mine, "")).toBe(false);
    expect(isOwnedLocal(frame({ id: "dbid", userId: "me" }), "me")).toBe(false);
  });

  it("DB 공용 기본은 접두 없는 id + isDefault다", () => {
    expect(isDbDefault(frame({ id: "dbid", isDefault: true }))).toBe(true);
    expect(isDbDefault(frame({ id: "dbid", isDefault: false }))).toBe(false);
    expect(isDbDefault(frame({ id: "local:x", isDefault: true }))).toBe(false);
  });
});

describe("frameEditPolicy — 역할 × 출처 (M10)", () => {
  const mine = frame({ id: "local:내것", userId: "me" });
  const others = frame({ id: "local:남것", userId: "other" });
  const publicFork = frame({ id: "local:공용포크", userId: null });
  const db = frame({ id: "dbid", userId: null, isDefault: true });
  const bundle = frame({ id: "bundle:번들" });
  const fallback = frame({ id: "fallback" });

  it("게스트는 아무것도 편집·삭제할 수 없다", () => {
    expect(canEditFrame(mine, null, "me")).toBe(false);
    expect(canDeleteFrame(mine, null)).toBe(false);
  });

  it("user·temp_user는 사용만 한다(읽기 전용)", () => {
    for (const role of ["user", "temp_user"] as const) {
      expect(canEditFrame(mine, role, "me")).toBe(false);
      expect(canEditFrame(db, role, "me")).toBe(false);
      expect(canDeleteFrame(mine, role)).toBe(false);
    }
  });

  it("advanced_user는 본인 로컬만 편집한다(DB 공용은 불가)", () => {
    expect(canEditFrame(mine, "advanced_user", "me")).toBe(true);
    expect(canEditFrame(others, "advanced_user", "me")).toBe(false);
    expect(canEditFrame(db, "advanced_user", "me")).toBe(false);
  });

  it("power는 DB 공용도 편집한다", () => {
    for (const role of ["manager", "admin"] as const) {
      expect(canEditFrame(db, role, "me")).toBe(true);
      expect(canEditFrame(mine, role, "me")).toBe(true);
    }
  });

  it("번들·fallback은 누구도 편집·삭제하지 못한다", () => {
    for (const f of [bundle, fallback]) {
      expect(canEditFrame(f, "admin", "me")).toBe(false);
      expect(canDeleteFrame(f, "admin")).toBe(false);
    }
  });

  it("삭제 판정은 소유자를 보지 않는다 — power의 공용 fork 삭제 능력 회귀 방지", () => {
    expect(publicFork.userId).toBeNull();
    expect(canDeleteFrame(publicFork, "manager")).toBe(true);
    expect(canDeleteFrame(publicFork, "advanced_user")).toBe(true);
  });

  it("카탈로그 유래는 fork가 필요하고 본인 로컬은 아니다", () => {
    expect(requiresFork(db)).toBe(true);
    expect(requiresFork(bundle)).toBe(true);
    expect(requiresFork(fallback)).toBe(true);
    expect(requiresFork(mine)).toBe(false);
  });
});

describe("frameNaming — 이름 검증", () => {
  it("1~100자를 허용한다", () => {
    expect(validateFrameName("베이직 4컷").ok).toBe(true);
    expect(validateFrameName("a".repeat(MAX_FRAME_NAME_LENGTH)).ok).toBe(true);
    expect(validateFrameName("a".repeat(MAX_FRAME_NAME_LENGTH + 1))).toEqual({
      ok: false,
      reason: "too-long",
    });
    expect(validateFrameName("")).toEqual({ ok: false, reason: "empty" });
    expect(validateFrameName("   ")).toEqual({ ok: false, reason: "empty" });
  });

  it("파일시스템 금지문자를 치환하지 않고 거부한다", () => {
    for (const bad of ["a/b", "a\\b", "a:b", 'a"b', "a<b", "a>b", "a|b", "a?b", "a*b"]) {
      expect(validateFrameName(bad), bad).toEqual({ ok: false, reason: "invalid-chars" });
    }
  });

  it("공백·하이픈은 허용한다(사본 이름이 공백을 포함한다)", () => {
    expect(validateFrameName("새 프레임 사본").ok).toBe(true);
    expect(validateFrameName("베이직-4컷").ok).toBe(true);
  });

  it("로컬 저장에서 `_`는 하드 거부가 아니라 공용 스코프 비차단 경고다", () => {
    expect(validateFrameName("내_프레임").ok).toBe(true);
    expect(underscoreWarning("내_프레임", "public")).toBe(true);
    expect(underscoreWarning("내_프레임", "personal")).toBe(false);
    expect(underscoreWarning("내프레임", "public")).toBe(false);
  });

  it("서버 등록 경로에서는 `_`가 하드 거부다 — 서버가 400으로 거부한다(M15)", () => {
    expect(validateFrameNameForServer("내_프레임")).toEqual({ ok: false, reason: "underscore" });
    expect(validateFrameNameForServer("_앞").ok).toBe(false);
    expect(validateFrameNameForServer("뒤_").ok).toBe(false);
    expect(validateFrameNameForServer("내 프레임").ok).toBe(true);
  });

  it("서버 등록 검증은 로컬 검증을 포함한다(길이·금지문자 먼저)", () => {
    expect(validateFrameNameForServer("")).toEqual({ ok: false, reason: "empty" });
    expect(validateFrameNameForServer("a".repeat(101))).toEqual({ ok: false, reason: "too-long" });
    expect(validateFrameNameForServer("a/b")).toEqual({ ok: false, reason: "invalid-chars" });
  });

  it("사본 번호가 99까지 모두 충돌하면 난수 접미로 폴백한다(저장을 막지 않는다)", () => {
    const taken = ["A 사본", ...Array.from({ length: 98 }, (_, i) => `A 사본 ${i + 2}`)];
    expect(taken).toHaveLength(99);
    expect(nextCopyName("A", taken, () => "deadbeef")).toBe("A 사본 deadbeef");
  });
});

describe("slotsFile — 직렬화 왕복", () => {
  it("직렬화한 것을 다시 파싱하면 같은 값이다", () => {
    const content = {
      imageSize: { width: 1200, height: 1600 },
      slots: fallbackFrameSlots(),
      dbId: null,
    };
    const text = serializeSlotsFile(content);
    expect(text.startsWith("#imagesize=1200,1600\n")).toBe(true);
    expect(text.includes("#dbid=")).toBe(false);
    expect(parseSlotsFile(text)).toEqual(content);
  });

  it("dbId가 있으면 두 번째 줄에 쓴다", () => {
    const content = {
      imageSize: { width: 800, height: 600 },
      slots: [{ index: 0, x: 1, y: 2, width: 3, height: 4 }],
      dbId: "abc123",
    };
    const text = serializeSlotsFile(content);
    expect(text.split("\n")[1]).toBe("#dbid=abc123");
    expect(parseSlotsFile(text)).toEqual(content);
  });

  it("빈 dbId는 줄을 만들지 않는다(로컬 사본은 서버 연결이 끊긴다)", () => {
    const text = serializeSlotsFile({
      imageSize: { width: 1, height: 1 },
      slots: [],
      dbId: "",
    });
    expect(text.includes("#dbid")).toBe(false);
  });
});

describe("frameCatalogPolicy — 우선순위·dedup", () => {
  const local = frame({ id: "local:베이직", name: "베이직 4컷" });
  const server = frame({ id: "dbid1", name: "서버 프레임", isDefault: true, userId: null });
  const serverDup = frame({ id: "dbid2", name: "베이직 4컷", isDefault: true, userId: null });
  const bundle = frame({ id: "bundle:번들", name: "번들 프레임" });
  const fallback = frame({ id: "fallback", name: "기본 프레임" });
  const personal = frame({ id: "local:내것", name: "내 프레임", userId: "me" });

  it("이미 캐시된 이름은 다시 내려받지 않는다", () => {
    const names = new Set(["베이직 4컷"]);
    expect(serverFramesToCache(names, [server, serverDup])).toEqual([server]);
  });

  it("이름 dedup은 먼저 온 것이 이긴다", () => {
    expect(dedupeByName([local, serverDup, server]).map((f) => f.id)).toEqual(["local:베이직", "dbid1"]);
  });

  it("로컬 캐시가 있으면 LocalCache가 출처다", () => {
    const result = buildCatalog({ localCache: [local], server: [server], bundle: [bundle], fallback });
    expect(result.source).toBe("LocalCache");
    expect(result.frames.map((f) => f.name)).toEqual(["베이직 4컷", "서버 프레임"]);
  });

  it("캐시가 비고 서버만 있으면 Server가 출처다", () => {
    const result = buildCatalog({ localCache: [], server: [server], bundle: [bundle], fallback });
    expect(result.source).toBe("Server");
  });

  it("오프라인(서버 빈 배열)이면 번들로 떨어지고 목록이 비지 않는다 — E20", () => {
    const result = buildCatalog({ localCache: [], server: [], bundle: [bundle], fallback });
    expect(result.source).toBe("Bundle");
    expect(result.frames).toHaveLength(1);
  });

  it("아무것도 없으면 fallback 1개를 돌려준다(목록이 절대 비지 않는다)", () => {
    const result = buildCatalog({ localCache: [], server: [], bundle: [], fallback });
    expect(result.source).toBe("Fallback");
    expect(result.frames).toEqual([fallback]);
  });

  it("개인 프레임은 공용 뒤에 붙고 출처 판정에 영향을 주지 않는다", () => {
    const result = buildCatalog({
      localCache: [],
      server: [],
      bundle: [],
      fallback,
      personal: [personal],
    });
    expect(result.source).toBe("Fallback");
    expect(result.frames.map((f) => f.name)).toEqual(["기본 프레임", "내 프레임"]);
  });

  it("`_` 이름 공용 프레임은 캐시 충돌 경고 대상이다", () => {
    expect(hasUnderscoreCacheConflict(frame({ name: "내_프레임" }))).toBe(true);
    expect(hasUnderscoreCacheConflict(frame({ name: "내 프레임" }))).toBe(false);
  });
});
