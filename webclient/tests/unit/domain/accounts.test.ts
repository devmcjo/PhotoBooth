import { describe, expect, it } from "vitest";
import {
  buildUserRow,
  buildUserRows,
  canEditGlobalLimits,
  canExitKiosk,
  canOpenUserMgmt,
  sortManagedUsers,
} from "@domain/accounts/accountAdminPolicy";
import { authMethodLabel, type SessionUser } from "@domain/accounts/sessionUser";
import {
  buildLimitsPatch,
  MAX_QR_COUNT,
  MAX_QR_HOURS,
  MIN_QR_COUNT,
  MIN_QR_HOURS,
  parseLimitInput,
  validateTempUserLimits,
} from "@domain/accounts/tempUserLimitsPolicy";
import { pinGateGroup } from "@domain/auth/pinGatePolicy";
import { USER_ROLES, type UserRole } from "@domain/roles/userRole";

/**
 * 계정 관리 도메인 — 역할 게이트 · 한도 정책 · PIN 게이트 그룹 (설계 §4·§5.4.1·§5.5)
 *
 * 여기서 고정하는 것 중 가장 중요한 것: **`canManage`(동급 허용)와 `canResetPin`(동급 차단)의
 * 비대칭**이다. 비대칭은 버그로 보이기 쉬워 "일관성"을 이유로 고쳐질 위험이 있다.
 */

function user(overrides: Partial<SessionUser> & { id: string; role: UserRole }): SessionUser {
  return {
    createdAt: "2026-01-01T00:00:00.000Z",
    email: null,
    authMethod: "google",
    hasPin: true,
    ...overrides,
  };
}

describe("canOpenUserMgmt · canEditGlobalLimits · canExitKiosk", () => {
  it.each([
    ["temp_user", false],
    ["user", false],
    ["advanced_user", false],
    ["manager", true],
    ["admin", true],
  ] as const)("canOpenUserMgmt(%s) = %s (power만)", (role, expected) => {
    expect(canOpenUserMgmt(role)).toBe(expected);
  });

  it("게스트(null)는 어떤 관리자 기능도 열 수 없다", () => {
    expect(canOpenUserMgmt(null)).toBe(false);
    expect(canEditGlobalLimits(null)).toBe(false);
    expect(canExitKiosk(null)).toBe(false);
  });

  it("전역 한도 편집은 **admin만**이다(manager는 서버가 403을 준다)", () => {
    expect(canEditGlobalLimits("admin")).toBe(true);
    expect(canEditGlobalLimits("manager")).toBe(false);
    expect(canEditGlobalLimits("advanced_user")).toBe(false);
  });

  it("키오스크 종료는 사용자 관리와 같은 게이트다(power)", () => {
    for (const role of USER_ROLES) {
      expect(canExitKiosk(role)).toBe(canOpenUserMgmt(role));
    }
  });
});

describe("sortManagedUsers — 위계 내림차순 · 가입일 오름차순 · 빈 값은 맨 뒤", () => {
  it("역할 위계가 우선이다", () => {
    const sorted = sortManagedUsers([
      user({ id: "t", role: "temp_user" }),
      user({ id: "a", role: "admin" }),
      user({ id: "u", role: "user" }),
      user({ id: "m", role: "manager" }),
      user({ id: "v", role: "advanced_user" }),
    ]);
    expect(sorted.map((u) => u.id)).toEqual(["a", "m", "v", "u", "t"]);
  });

  it("동급은 가입일 오름차순이다", () => {
    const sorted = sortManagedUsers([
      user({ id: "late", role: "user", createdAt: "2026-05-01T00:00:00.000Z" }),
      user({ id: "early", role: "user", createdAt: "2026-01-01T00:00:00.000Z" }),
    ]);
    expect(sorted.map((u) => u.id)).toEqual(["early", "late"]);
  });

  it("**빈 `createdAt`은 맨 뒤**다(문자열 비교로 두면 admin 위로 올라간다)", () => {
    const sorted = sortManagedUsers([
      user({ id: "blank", role: "user", createdAt: "" }),
      user({ id: "dated", role: "user", createdAt: "2026-01-01T00:00:00.000Z" }),
    ]);
    expect(sorted.map((u) => u.id)).toEqual(["dated", "blank"]);
  });

  it("같은 역할·같은 가입일이면 id로 결정된다(같은 입력 = 같은 순서)", () => {
    const input = [user({ id: "b", role: "user" }), user({ id: "a", role: "user" })];
    expect(sortManagedUsers(input).map((u) => u.id)).toEqual(["a", "b"]);
    expect(sortManagedUsers(input).map((u) => u.id)).toEqual(["a", "b"]);
  });

  it("입력 배열을 변형하지 않는다(순수)", () => {
    const input = [user({ id: "t", role: "temp_user" }), user({ id: "a", role: "admin" })];
    sortManagedUsers(input);
    expect(input.map((u) => u.id)).toEqual(["t", "a"]);
  });
});

describe("buildUserRow — 전 역할 쌍", () => {
  /** actor 역할 → target 역할 → [canDelete, canResetPin] 기대값(analysis/60 §1.3). */
  const EXPECTED: Record<UserRole, Record<UserRole, readonly [boolean, boolean]>> = {
    temp_user: {
      temp_user: [false, false],
      user: [false, false],
      advanced_user: [false, false],
      manager: [false, false],
      admin: [false, false],
    },
    user: {
      temp_user: [false, false],
      user: [false, false],
      advanced_user: [false, false],
      manager: [false, false],
      admin: [false, false],
    },
    advanced_user: {
      temp_user: [false, false],
      user: [false, false],
      advanced_user: [false, false],
      manager: [false, false],
      admin: [false, false],
    },
    manager: {
      temp_user: [true, true],
      user: [true, true],
      advanced_user: [true, true],
      // ↔ analysis/60 §1.3.1 — 동급 삭제는 허용, 동급 PIN 재설정은 차단이다.
      manager: [true, false],
      admin: [false, false],
    },
    admin: {
      temp_user: [true, true],
      user: [true, true],
      advanced_user: [true, true],
      manager: [true, true],
      admin: [true, false],
    },
  };

  for (const actorRole of USER_ROLES) {
    for (const targetRole of USER_ROLES) {
      it(`${actorRole} → ${targetRole}`, () => {
        const row = buildUserRow(
          user({ id: "actor", role: actorRole }),
          user({ id: "target", role: targetRole }),
        );
        const [canDelete, canResetPin] = EXPECTED[actorRole][targetRole];
        expect(row.canDelete).toBe(canDelete);
        expect(row.canResetPin).toBe(canResetPin);
      });
    }
  }

  it("manager → manager: **삭제는 있고 PIN은 없다**(비대칭이 규격이다)", () => {
    // ↔ analysis/60 §1.3.1. "일관성"을 이유로 고치지 마라.
    const row = buildUserRow(
      user({ id: "m1", role: "manager" }),
      user({ id: "m2", role: "manager" }),
    );
    expect(row.canDelete).toBe(true);
    expect(row.canResetPin).toBe(false);
    expect(row.assignableRoles).toEqual([]);
  });

  it("temp_user가 다른 temp_user를 삭제할 수 없다(canManage 동급 허용의 함정)", () => {
    const row = buildUserRow(
      user({ id: "t1", role: "temp_user" }),
      user({ id: "t2", role: "temp_user" }),
    );
    expect(row.canDelete).toBe(false);
  });

  it("자기 행은 전부 false / 빈 목록이다", () => {
    const me = user({ id: "same", role: "admin" });
    const row = buildUserRow(me, me);
    expect(row.isSelf).toBe(true);
    expect(row.canDelete).toBe(false);
    expect(row.canResetPin).toBe(false);
    expect(row.assignableRoles).toEqual([]);
  });

  it("admin 대상은 역할 콤보가 비어 있다(누구도 admin을 바꿀 수 없다)", () => {
    const row = buildUserRow(
      user({ id: "a1", role: "admin" }),
      user({ id: "a2", role: "admin" }),
    );
    expect(row.assignableRoles).toEqual([]);
  });

  it("manager는 하위 대역만 지정할 수 있다", () => {
    const row = buildUserRow(
      user({ id: "m", role: "manager" }),
      user({ id: "u", role: "user" }),
    );
    expect(row.assignableRoles).toEqual(["temp_user", "user", "advanced_user"]);
    expect(row.assignableRoles).not.toContain("admin");
  });
});

describe("buildUserRows — 정렬을 내장한다", () => {
  it("정렬된 순서로 행을 만든다", () => {
    const actor = user({ id: "admin", role: "admin" });
    const rows = buildUserRows(actor, [
      user({ id: "t", role: "temp_user" }),
      user({ id: "m", role: "manager" }),
    ]);
    expect(rows.map((row) => row.user.id)).toEqual(["m", "t"]);
  });
});

describe("tempUserLimitsPolicy", () => {
  it.each([
    ["48", 48],
    [" 48 ", 48],
    ["+7", 7],
    ["-1", -1],
    ["1.5", null],
    ["1e3", null],
    ["", null],
    ["12abc", null],
    ["0x10", null],
  ])("parseLimitInput(%j) = %j", (raw, expected) => {
    expect(parseLimitInput(raw)).toBe(expected);
  });

  const current = { qrHours: 48, qrCount: 30 };

  it("범위 안이고 값이 달라지면 통과한다", () => {
    expect(validateTempUserLimits({ qrHours: 24, qrCount: 30 }, current)).toEqual({ ok: true });
  });

  it("qrHours 범위 밖은 거부한다", () => {
    expect(validateTempUserLimits({ qrHours: MIN_QR_HOURS - 1, qrCount: 30 }, current)).toEqual({
      ok: false,
      reason: "qrHours-range",
    });
    expect(validateTempUserLimits({ qrHours: MAX_QR_HOURS + 1, qrCount: 30 }, current)).toEqual({
      ok: false,
      reason: "qrHours-range",
    });
  });

  it("qrCount 범위 밖은 거부한다", () => {
    expect(validateTempUserLimits({ qrHours: 48, qrCount: MIN_QR_COUNT - 1 }, current)).toEqual({
      ok: false,
      reason: "qrCount-range",
    });
    expect(validateTempUserLimits({ qrHours: 48, qrCount: MAX_QR_COUNT + 1 }, current)).toEqual({
      ok: false,
      reason: "qrCount-range",
    });
  });

  it("파싱 실패(null)는 '바꾸지 않음'이 아니라 **오류**다", () => {
    expect(validateTempUserLimits({ qrHours: null, qrCount: 30 }, current).ok).toBe(false);
    expect(validateTempUserLimits({ qrHours: 48, qrCount: null }, current).ok).toBe(false);
  });

  it("변경이 없으면 no-change다(서버로 보내지 않는다)", () => {
    expect(validateTempUserLimits({ qrHours: 48, qrCount: 30 }, current)).toEqual({
      ok: false,
      reason: "no-change",
    });
  });

  it("buildLimitsPatch는 **달라진 키만** 담는다", () => {
    expect(buildLimitsPatch({ qrHours: 24, qrCount: 30 }, current)).toEqual({ qrHours: 24 });
    expect(buildLimitsPatch({ qrHours: 48, qrCount: 10 }, current)).toEqual({ qrCount: 10 });
    expect(buildLimitsPatch({ qrHours: 24, qrCount: 10 }, current)).toEqual({
      qrHours: 24,
      qrCount: 10,
    });
    expect(buildLimitsPatch({ qrHours: 48, qrCount: 30 }, current)).toEqual({});
  });
});

describe("pinGateGroup — Account ↔ UserMgmt만 한 그룹이다", () => {
  it("UserMgmt는 Account 그룹이다", () => {
    expect(pinGateGroup("UserMgmt")).toBe("Account");
    expect(pinGateGroup("Account")).toBe("Account");
  });

  it("**Settings는 자기 자신 그룹**이다(매번 확인 — 여기에 묶지 마라)", () => {
    expect(pinGateGroup("Settings")).toBe("Settings");
  });

  it("그 외 화면은 자기 자신이다", () => {
    for (const screen of ["Home", "Login", "FrameEditor", "Capture"] as const) {
      expect(pinGateGroup(screen)).toBe(screen);
    }
  });
});

describe("authMethodLabel — Windows `AuthMethodExtensions.ToLabel()`과 문자열 일치", () => {
  it("google은 'Google SSO'다", () => {
    expect(authMethodLabel(user({ id: "a", role: "user", authMethod: "google" }))).toBe(
      "Google SSO",
    );
  });

  it("null은 '알 수 없음'이다", () => {
    expect(authMethodLabel(user({ id: "a", role: "user", authMethod: null }))).toBe("알 수 없음");
  });

  it("**'password'도 '알 수 없음'**이다(it15에서 폐지된 개념 — 분기를 되살리지 마라)", () => {
    expect(authMethodLabel(user({ id: "a", role: "user", authMethod: "password" }))).toBe(
      "알 수 없음",
    );
  });
});
