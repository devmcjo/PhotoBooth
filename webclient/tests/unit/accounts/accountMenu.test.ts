import { describe, expect, it } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { buildAccountMenuItems } from "@screens/account/accountMenu";
import {
  buildAccountInfoRows,
  formatIsoDate,
} from "@screens/account/accountInfoRows";
import { STRINGS } from "@ui/strings";

/**
 * 상단바 계정 팝오버 · 내 정보 행 — 02 §5.1 · 03 §13.1
 *
 * ⚠️ Step 16 이전에는 앱 어디에도 **로그아웃 진입점이 없었다**. 항목이 사라지면 교대 시
 *    계정이 그대로 남는다.
 */

function user(role: UserRole, overrides: Partial<SessionUser> = {}): SessionUser {
  return {
    id: "operator-1",
    role,
    createdAt: "2026-03-05T04:05:06.000Z",
    email: "op@example.com",
    authMethod: "google",
    hasPin: true,
    ...overrides,
  };
}

describe("buildAccountMenuItems", () => {
  it("게스트는 **빈 배열**이다(호출측이 곧바로 Login으로 보낸다)", () => {
    expect(buildAccountMenuItems(null)).toEqual([]);
  });

  it("일반 사용자는 2항목이다(관리자 도구 없음)", () => {
    const items = buildAccountMenuItems(user("user"));
    expect(items.map((item) => item.id)).toEqual(["manage", "logout"]);
  });

  it("advanced_user도 관리자 도구가 없다(프레임 저작 권한과 별개 축)", () => {
    expect(buildAccountMenuItems(user("advanced_user")).map((i) => i.id)).toEqual([
      "manage",
      "logout",
    ]);
  });

  it("manager·admin은 3항목이다", () => {
    for (const role of ["manager", "admin"] as const) {
      expect(buildAccountMenuItems(user(role)).map((i) => i.id)).toEqual([
        "manage",
        "adminTools",
        "logout",
      ]);
    }
  });

  it("모든 항목이 문구 카탈로그를 쓴다(빈 라벨 금지)", () => {
    for (const item of buildAccountMenuItems(user("admin"))) {
      expect(item.label.length).toBeGreaterThan(0);
    }
    expect(buildAccountMenuItems(user("admin")).at(-1)?.label).toBe(STRINGS.common.logout);
  });
});

describe("buildAccountInfoRows", () => {
  const deps = { formatDate: (iso: string) => `D(${iso})` };

  it("5행을 규격 순서로 만든다", () => {
    const rows = buildAccountInfoRows(user("manager"), deps);
    expect(rows.map((row) => row.label)).toEqual([
      STRINGS.account.id,
      STRINGS.account.email,
      STRINGS.account.authMethod,
      STRINGS.account.role,
      STRINGS.account.createdAt,
    ]);
  });

  it("로그인 방식은 **'Google SSO'**다(§3.1 판정)", () => {
    const rows = buildAccountInfoRows(user("user"), deps);
    expect(rows[2]!.value).toBe("Google SSO");
  });

  it("이메일이 없으면 '—'다", () => {
    const rows = buildAccountInfoRows(user("user", { email: null }), deps);
    expect(rows[1]!.value).toBe(STRINGS.account.none);
  });

  it("빈 `createdAt`은 '알 수 없음'이고 **서식 함수를 부르지 않는다**", () => {
    let calls = 0;
    const rows = buildAccountInfoRows(user("user", { createdAt: "" }), {
      formatDate: (iso) => {
        calls++;
        return iso;
      },
    });
    expect(rows[4]!.value).toBe(STRINGS.account.unknown);
    expect(calls).toBe(0);
  });

  it("서식 결과가 비면 '알 수 없음'으로 접는다", () => {
    const rows = buildAccountInfoRows(user("user"), { formatDate: () => "" });
    expect(rows[4]!.value).toBe(STRINGS.account.unknown);
  });
});

describe("formatIsoDate", () => {
  it("파싱 실패는 **빈 문자열**이다(예외를 던지지 않는다)", () => {
    expect(formatIsoDate("나쁜 값")).toBe("");
    expect(formatIsoDate("")).toBe("");
  });

  it("유효한 ISO는 YYYY-MM-DD 형태다", () => {
    expect(formatIsoDate("2026-03-05T04:05:06.000Z")).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
