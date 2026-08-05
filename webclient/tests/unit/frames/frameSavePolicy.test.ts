import { describe, expect, it } from "vitest";
import {
  DEFAULT_REGISTER_TO_SERVER,
  DEFAULT_SCALE_PERCENT,
  editSessionSource,
  FRAME_SESSION_SOURCES,
  frameSaveScope,
  MAX_SCALE_PERCENT,
  MIN_SCALE_PERCENT,
  requiresServerRegisterPrompt,
  saveScopeNoticeKind,
  showsLocalOnlyBanner,
  validateFrameSave,
  type FrameSaveValidationInput,
  type FrameSessionSource,
} from "@domain/frames/frameSavePolicy";
import type { FrameTemplate, Slot } from "@domain/frames/types";
import { USER_ROLES, type UserRole } from "@domain/roles/userRole";

/**
 * 저장 판정 — 설계 §3·§4 (03 §11.3·§11.4 · analysis/13 §6.3·§6.4)
 *
 * 여기서 고정하는 것은 **순서**다: ④가 ⑦보다 먼저이고, ⑤⑥이 길이를 보지 않으며,
 * ⑧이 7단 뒤에 있다. 순서가 바뀌면 사용자에게 엉뚱한 사유가 안내되고(오안내),
 * ⑦ 열거가 실패한 상황에서 원본 프레임이 조용히 파괴된다.
 */

const OK_SLOTS: readonly Slot[] = [
  { index: 0, x: 0, y: 0, width: 100, height: 100 },
  { index: 1, x: 200, y: 0, width: 100, height: 100 },
];

/** 겹치는 슬롯 — ③에서 막혀야 한다. */
const OVERLAPPING: readonly Slot[] = [
  { index: 0, x: 0, y: 0, width: 100, height: 100 },
  { index: 1, x: 50, y: 50, width: 100, height: 100 },
];

function input(overrides: Partial<FrameSaveValidationInput> = {}): FrameSaveValidationInput {
  return {
    role: "manager",
    sessionSource: "New",
    hasImage: true,
    slots: OK_SLOTS,
    frameWidth: 1200,
    frameHeight: 1600,
    name: "여름 6컷",
    sourceName: "",
    existingNames: [],
    personalCount: 0,
    ...overrides,
  };
}

function frame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  return {
    id: "local:public:내 프레임",
    userId: null,
    isDefault: true,
    name: "내 프레임",
    imageUrl: "blob:x",
    imageSize: { width: 1200, height: 1600 },
    slots: OK_SLOTS,
    createdAt: "2026-08-01T00:00:00.000Z",
    ...overrides,
  };
}

describe("P1: 7단 순서 — 동시에 여러 검사를 위반해도 앞선 사유가 나온다", () => {
  it("fork + 공용 + 이름 == 원본 + 금지문자 + 스코프 충돌 → same-as-source (④ < ⑤⑥⑦)", () => {
    // ⑦은 열거 실패 시 조용히 꺼지므로 ④가 2중 방어로 남아야 한다.
    const result = validateFrameSave(
      input({
        sessionSource: "ForkFromCatalog",
        role: "admin",
        name: "봄<4컷",
        sourceName: "봄<4컷",
        existingNames: ["봄<4컷"],
      }),
    );
    expect(result).toEqual({ ok: false, reason: "same-as-source" });
  });

  it("게스트 + 모든 위반 → not-logged-in", () => {
    expect(
      validateFrameSave(
        input({
          role: null,
          sessionSource: "ForkFromCatalog",
          hasImage: false,
          slots: OVERLAPPING,
          name: "",
          sourceName: "",
          existingNames: [""],
          personalCount: 99,
        }),
      ).reason,
    ).toBe("not-logged-in");
  });

  it("user 역할 + 모든 위반 → no-write-permission", () => {
    expect(
      validateFrameSave(
        input({ role: "user", hasImage: false, slots: OVERLAPPING, name: "" }),
      ).reason,
    ).toBe("no-write-permission");
  });

  it("temp_user도 ②에서 막힌다", () => {
    expect(validateFrameSave(input({ role: "temp_user" })).reason).toBe("no-write-permission");
  });

  it("겹치는 슬롯 + 빈 이름 → invalid-slots", () => {
    expect(validateFrameSave(input({ slots: OVERLAPPING, name: "   " })).reason).toBe(
      "invalid-slots",
    );
  });

  it("이미지 미확보는 invalid-slots다(저장 버튼이 눌려도 바이트가 없으면 막는다)", () => {
    expect(validateFrameSave(input({ hasImage: false })).reason).toBe("invalid-slots");
  });

  it("경계를 벗어난 슬롯도 invalid-slots다", () => {
    const outside: readonly Slot[] = [{ index: 0, x: 1190, y: 0, width: 100, height: 100 }];
    expect(validateFrameSave(input({ slots: outside })).reason).toBe("invalid-slots");
  });

  it("슬롯 0개·7개는 invalid-slots다", () => {
    expect(validateFrameSave(input({ slots: [] })).reason).toBe("invalid-slots");
    const seven: Slot[] = Array.from({ length: 7 }, (_v, i) => ({
      index: i,
      x: i * 150,
      y: 0,
      width: 100,
      height: 100,
    }));
    expect(validateFrameSave(input({ slots: seven })).reason).toBe("invalid-slots");
  });

  it("빈 이름 + 충돌 → name-empty (⑤ < ⑦)", () => {
    expect(validateFrameSave(input({ name: "  ", existingNames: ["  "] })).reason).toBe(
      "name-empty",
    );
  });

  it('"a<b" + 충돌 → name-invalid-chars (⑥ < ⑦)', () => {
    expect(validateFrameSave(input({ name: "a<b", existingNames: ["a<b"] })).reason).toBe(
      "name-invalid-chars",
    );
  });
});

describe("P2: ⑤⑥의 판정 축은 isFileNameSafe다(길이 무관)", () => {
  it("150자 이름이 통과한다", () => {
    // 100자 제한이 묶인 이름 검증을 쓰면 여기서 실패한다 — 축이 다르다(03 §11.3 웹 주의).
    expect(validateFrameSave(input({ name: "a".repeat(150) })).ok).toBe(true);
  });

  it('"이름\\n"은 원문 판정이라 거부된다', () => {
    // 제어문자는 파일시스템 금지문자다. trim 후 판정하면 통과해 버린다.
    expect(validateFrameSave(input({ name: "이름\n" })).reason).toBe("name-invalid-chars");
  });

  it("공백만 있는 이름은 name-empty다", () => {
    expect(validateFrameSave(input({ name: "\t \n" })).reason).toBe("name-empty");
  });
});

describe("P3: ⑦ 스코프 이름 충돌", () => {
  it("EditOwnLocal + 충돌 → ok(덮어쓰기가 의도다)", () => {
    expect(
      validateFrameSave(
        input({ sessionSource: "EditOwnLocal", name: "내 프레임", existingNames: ["내 프레임"] }),
      ).ok,
    ).toBe(true);
  });

  it.each<FrameSessionSource>(["New", "ForkFromCatalog"])("%s + 충돌 → name-conflict", (source) => {
    expect(
      validateFrameSave(
        input({
          sessionSource: source,
          name: "봄 4컷",
          sourceName: "다른 원본",
          existingNames: ["봄 4컷"],
        }),
      ).reason,
    ).toBe("name-conflict");
  });

  it("열거 실패(빈 배열)에서 ⑦은 꺼지고 ④는 여전히 동작한다", () => {
    expect(validateFrameSave(input({ existingNames: [] })).ok).toBe(true);
    expect(
      validateFrameSave(
        input({
          sessionSource: "ForkFromCatalog",
          name: "봄 4컷",
          sourceName: "봄 4컷",
          existingNames: [],
        }),
      ).reason,
    ).toBe("same-as-source");
  });

  it("비교는 정확 일치다(앞뒤 공백이 다르면 충돌이 아니다)", () => {
    expect(validateFrameSave(input({ name: "봄 4컷", existingNames: ["봄 4컷 "] })).ok).toBe(true);
  });
});

describe("P4: ④는 공용 스코프에서만 발동한다", () => {
  it("개인 스코프(advanced_user) fork + 같은 이름 → ④ 발동 안 함", () => {
    // 개인 저장 키에는 소유자가 들어가 공용 원본과 물리적으로 겹치지 않는다.
    expect(
      validateFrameSave(
        input({
          role: "advanced_user",
          sessionSource: "ForkFromCatalog",
          name: "봄 4컷",
          sourceName: "봄 4컷",
        }),
      ).ok,
    ).toBe(true);
  });

  it("New 세션에서는 이름이 sourceName과 같아도 ④가 발동하지 않는다(⑦이 막는다)", () => {
    expect(
      validateFrameSave(input({ sessionSource: "New", name: "봄 4컷", sourceName: "봄 4컷" })).ok,
    ).toBe(true);
    expect(
      validateFrameSave(
        input({
          sessionSource: "New",
          name: "봄 4컷",
          sourceName: "봄 4컷",
          existingNames: ["봄 4컷"],
        }),
      ).reason,
    ).toBe("name-conflict");
  });
});

describe("P5: ⑧ 개인 프레임 10개 상한 — 7단 뒤", () => {
  it("공용 스코프는 상한을 무시한다", () => {
    expect(validateFrameSave(input({ role: "manager", personalCount: 99 })).ok).toBe(true);
  });

  it("개인 + 새 이름 + count 10 → limit-reached", () => {
    expect(
      validateFrameSave(input({ role: "advanced_user", personalCount: 10 })).reason,
    ).toBe("limit-reached");
  });

  it("개인 + 기존 이름(덮어쓰기) + count 10 → ok", () => {
    // 이 예외가 없으면 10개를 채운 계정이 자기 프레임을 수정조차 못 한다.
    expect(
      validateFrameSave(
        input({
          role: "advanced_user",
          sessionSource: "EditOwnLocal",
          name: "내 프레임",
          existingNames: ["내 프레임"],
          personalCount: 10,
        }),
      ).ok,
    ).toBe(true);
  });

  it("빈 이름 + count 10 → name-empty(상한 문구가 앞서지 않는다)", () => {
    expect(
      validateFrameSave(input({ role: "advanced_user", name: "", personalCount: 10 })).reason,
    ).toBe("name-empty");
  });
});

describe("P6: requiresServerRegisterPrompt — 5역할 × 3세션 전수", () => {
  it("true는 (manager|admin) × New 2개뿐이다", () => {
    const truthy: string[] = [];
    for (const role of USER_ROLES) {
      for (const source of FRAME_SESSION_SOURCES) {
        if (requiresServerRegisterPrompt(role, source)) truthy.push(`${role}/${source}`);
      }
    }
    expect(truthy).toEqual(["manager/New", "admin/New"]);
  });

  it("게스트(null)는 언제나 false다", () => {
    for (const source of FRAME_SESSION_SOURCES) {
      expect(requiresServerRegisterPrompt(null, source)).toBe(false);
    }
  });

  it("체크박스 기본값이 true다(뒤집을 때 함께 움직이는 기대값)", () => {
    expect(DEFAULT_REGISTER_TO_SERVER).toBe(true);
  });
});

describe("P7: 파생 판정 전수", () => {
  it("frameSaveScope: power만 공용", () => {
    const scopes = USER_ROLES.map((role: UserRole) => `${role}:${frameSaveScope(role)}`);
    expect(scopes).toEqual([
      "temp_user:personal",
      "user:personal",
      "advanced_user:personal",
      "manager:public",
      "admin:public",
    ]);
    expect(frameSaveScope(null)).toBe("personal");
  });

  it("showsLocalOnlyBanner: 신규 생성 세션에는 배너가 없다", () => {
    expect(showsLocalOnlyBanner("New")).toBe(false);
    expect(showsLocalOnlyBanner("EditOwnLocal")).toBe(true);
    expect(showsLocalOnlyBanner("ForkFromCatalog")).toBe(true);
  });

  it("saveScopeNoticeKind: 역할 × 세션 전수", () => {
    expect(saveScopeNoticeKind("manager", "New")).toBe("public-new");
    expect(saveScopeNoticeKind("admin", "ForkFromCatalog")).toBe("public-fork");
    expect(saveScopeNoticeKind("manager", "EditOwnLocal")).toBe("overwrite");
    expect(saveScopeNoticeKind("advanced_user", "New")).toBe("personal");
    expect(saveScopeNoticeKind("advanced_user", "ForkFromCatalog")).toBe("personal");
    expect(saveScopeNoticeKind("advanced_user", "EditOwnLocal")).toBe("overwrite");
    expect(saveScopeNoticeKind(null, "New")).toBe("personal");
  });

  it("editSessionSource: 출처가 유일한 근거다", () => {
    // 로컬 저장분 = EditOwnLocal, 그 밖(서버 공용·번들·fallback) = ForkFromCatalog.
    expect(editSessionSource(frame({ id: "local:public:내 프레임" }))).toBe("EditOwnLocal");
    expect(editSessionSource(frame({ id: "local:user:me:내 프레임" }))).toBe("EditOwnLocal");
    expect(editSessionSource(frame({ id: "srv-1", isDefault: true }))).toBe("ForkFromCatalog");
    expect(editSessionSource(frame({ id: "bundle:basic" }))).toBe("ForkFromCatalog");
    expect(editSessionSource(frame({ id: "fallback" }))).toBe("ForkFromCatalog");
  });

  it("배율 범위는 Windows 실구현과 같은 10~300이다", () => {
    // 커밋 `0a93b59`가 의도적으로 넓힌 값이다(`FrameEditorViewModel.MinScale/MaxScale`).
    // 규격 문서에 남아 있던 70~130은 폐기된 초기 설계값이라 2026-08-01에 문서를 소스에 맞췄다.
    expect(MIN_SCALE_PERCENT).toBe(10);
    expect(MAX_SCALE_PERCENT).toBe(300);
    expect(DEFAULT_SCALE_PERCENT).toBe(100);
    expect(MIN_SCALE_PERCENT).toBeLessThan(DEFAULT_SCALE_PERCENT);
    expect(DEFAULT_SCALE_PERCENT).toBeLessThan(MAX_SCALE_PERCENT);
  });

  it("세션 축은 3값이다", () => {
    expect(FRAME_SESSION_SOURCES).toEqual(["New", "EditOwnLocal", "ForkFromCatalog"]);
  });
});
