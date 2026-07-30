import {
  canCreate,
  canManage,
  canResetPin,
  canSetRole,
  creatableRoles,
  isPower,
  isUserRole,
  parseRole,
  UserRole,
} from "../domain/roles";

/** it16: 전 역할(위계 오름차순). 새 역할 추가 시 이 배열만 갱신하면 전수 루프가 따라온다. */
const ALL_ROLES: UserRole[] = [
  "temp_user",
  "user",
  "advanced_user",
  "manager",
  "admin",
];

describe("roles — 역할 위계(C# UserRoleExtensions 이식 정합)", () => {
  test("isUserRole 화이트리스트(temp_user·advanced_user 포함)", () => {
    expect(isUserRole("temp_user")).toBe(true);
    expect(isUserRole("user")).toBe(true);
    expect(isUserRole("advanced_user")).toBe(true);
    expect(isUserRole("manager")).toBe(true);
    expect(isUserRole("admin")).toBe(true);
    expect(isUserRole("root")).toBe(false);
    expect(isUserRole("tempuser")).toBe(false); // snake_case만 허용
    expect(isUserRole("advanceduser")).toBe(false); // snake_case만 허용
    expect(isUserRole("advanced-user")).toBe(false); // kebab-case 금지(동결표는 snake_case)
    expect(isUserRole(1)).toBe(false);
    expect(isUserRole(null)).toBe(false);
  });

  test("parseRole 폴백은 user(temp_user·advanced_user 라운드트립)", () => {
    expect(parseRole("admin")).toBe("admin");
    expect(parseRole("manager")).toBe("manager");
    expect(parseRole("advanced_user")).toBe("advanced_user");
    expect(parseRole("temp_user")).toBe("temp_user");
    expect(parseRole("user")).toBe("user");
    expect(parseRole("")).toBe("user");
    expect(parseRole(null)).toBe("user");
    expect(parseRole("nonsense")).toBe("user");
    expect(parseRole("tempuser")).toBe("user"); // 오탈자 → 최소 권한 폴백
    expect(parseRole("advanceduser")).toBe("user"); // 오탈자 → user(프레임 쓰기 권한 없음 = fail-closed)
  });

  test("isPower: manager/admin만 power(temp_user·advanced_user 제외)", () => {
    expect(isPower("temp_user")).toBe(false);
    expect(isPower("user")).toBe(false);
    // it16 §3.2 동결표: 고급 유저는 power가 아니다. 이 단정이 깨지면 프레임 쓰기 라우트가 열린다.
    expect(isPower("advanced_user")).toBe(false);
    expect(isPower("manager")).toBe(true);
    expect(isPower("admin")).toBe(true);
  });

  test("creatableRoles: 위계 오름차순 목록(it16 advanced_user 포함)", () => {
    expect(creatableRoles("admin")).toEqual([
      "temp_user",
      "user",
      "advanced_user",
      "manager",
    ]);
    expect(creatableRoles("manager")).toEqual([
      "temp_user",
      "user",
      "advanced_user",
    ]);
    expect(creatableRoles("advanced_user")).toEqual([]);
    expect(creatableRoles("user")).toEqual([]);
    expect(creatableRoles("temp_user")).toEqual([]);
  });

  test("canCreate: admin/manager는 하위 대역 생성 가능, 하위 대역 actor는 불가", () => {
    // temp_user 생성 권한
    expect(canCreate("admin", "temp_user")).toBe(true);
    expect(canCreate("manager", "temp_user")).toBe(true);
    expect(canCreate("advanced_user", "temp_user")).toBe(false);
    expect(canCreate("user", "temp_user")).toBe(false);
    expect(canCreate("temp_user", "temp_user")).toBe(false);

    // it16: advanced_user 생성 권한(admin·manager만)
    expect(canCreate("admin", "advanced_user")).toBe(true);
    expect(canCreate("manager", "advanced_user")).toBe(true);
    expect(canCreate("advanced_user", "advanced_user")).toBe(false);
    expect(canCreate("user", "advanced_user")).toBe(false);

    // 기존 규칙 불변(admin은 admin 생성 불가 — 최종 1인 규칙)
    expect(canCreate("admin", "user")).toBe(true);
    expect(canCreate("admin", "manager")).toBe(true);
    expect(canCreate("admin", "admin")).toBe(false);

    expect(canCreate("manager", "user")).toBe(true);
    expect(canCreate("manager", "manager")).toBe(false);
    expect(canCreate("manager", "admin")).toBe(false);

    expect(canCreate("user", "user")).toBe(false);
  });

  test("canManage: 자신과 같거나 낮은 위계만(advanced_user 랭크 2, 서수 무관)", () => {
    // MANAGE_RANK: temp_user(0) < user(1) < advanced_user(2) < manager(3) < admin(4)
    const rank: Record<UserRole, number> = {
      temp_user: 0,
      user: 1,
      advanced_user: 2,
      manager: 3,
      admin: 4,
    };
    for (const acting of ALL_ROLES) {
      for (const target of ALL_ROLES) {
        expect(canManage(acting, target)).toBe(rank[target] <= rank[acting]);
      }
    }

    // 대표 케이스 명시(위계표 §3.2)
    expect(canManage("temp_user", "temp_user")).toBe(true);
    expect(canManage("temp_user", "user")).toBe(false);
    expect(canManage("user", "temp_user")).toBe(true);
    expect(canManage("admin", "temp_user")).toBe(true);
    expect(canManage("manager", "admin")).toBe(false);

    // it16 확장 행(설계 §8.2-6과 대칭)
    expect(canManage("advanced_user", "user")).toBe(true);
    expect(canManage("advanced_user", "temp_user")).toBe(true);
    expect(canManage("advanced_user", "advanced_user")).toBe(true);
    expect(canManage("advanced_user", "manager")).toBe(false);
    expect(canManage("advanced_user", "admin")).toBe(false);
    expect(canManage("user", "advanced_user")).toBe(false);
    expect(canManage("manager", "advanced_user")).toBe(true);
    expect(canManage("admin", "advanced_user")).toBe(true);
  });

  test("canResetPin: power + 엄격히 낮은 위계만(동급 차단 — manager PIN은 admin 전용)", () => {
    const rank: Record<UserRole, number> = {
      temp_user: 0,
      user: 1,
      advanced_user: 2,
      manager: 3,
      admin: 4,
    };
    for (const acting of ALL_ROLES) {
      for (const target of ALL_ROLES) {
        const expected =
          (acting === "manager" || acting === "admin") &&
          rank[target] < rank[acting];
        expect(canResetPin(acting, target)).toBe(expected);
      }
    }

    // 요구사항 핵심 케이스 명시.
    expect(canResetPin("manager", "manager")).toBe(false); // 동급 매니저 차단
    expect(canResetPin("admin", "manager")).toBe(true); // 매니저 PIN의 유일한 경로
    expect(canResetPin("admin", "admin")).toBe(false); // 동급 admin도 차단
    expect(canResetPin("manager", "advanced_user")).toBe(true);
    expect(canResetPin("advanced_user", "user")).toBe(false); // 비power 전원 차단

    // canManage는 삭제와 공유되므로 동급 허용이 유지돼야 한다(좁히면 삭제가 회귀).
    expect(canManage("manager", "manager")).toBe(true);
    expect(canManage("admin", "admin")).toBe(true);
  });
});

// ── it16 §3.3 역할 지정 전수 표(설계 문서를 데이터로 그대로 옮긴다) ──────────────
//
// 열 순서 = TARGET_ORDER, 문자 1=허용(○) 0=거부(✕).
// 구현 규칙을 테스트에서 다시 계산하지 않는다(같은 실수를 두 번 하지 않기 위해) —
// 설계 표의 25행을 리터럴로 두고 125조합을 기계 비교한다.
const TARGET_ORDER: UserRole[] = [
  "temp_user",
  "user",
  "advanced_user",
  "manager",
  "admin",
];

/** [actor, current, 허용비트(열=TARGET_ORDER)] — 설계 §3.3 전수 표. */
const SET_ROLE_MATRIX: Array<[UserRole, UserRole, string]> = [
  // admin: 하위 대역·manager 자유 지정. admin 지정 불가, admin 대상 변경 불가.
  ["admin", "temp_user", "11110"],
  ["admin", "user", "11110"],
  ["admin", "advanced_user", "11110"],
  ["admin", "manager", "11110"],
  ["admin", "admin", "00000"],
  // manager: 하위 3역할 대역(temp_user·user·advanced_user) 내에서만 자유 지정(승격 포함).
  ["manager", "temp_user", "11100"],
  ["manager", "user", "11100"],
  ["manager", "advanced_user", "11100"],
  ["manager", "manager", "00000"],
  ["manager", "admin", "00000"],
  // advanced_user actor: 계정 관리 권한 0(전부 거부).
  ["advanced_user", "temp_user", "00000"],
  ["advanced_user", "user", "00000"],
  ["advanced_user", "advanced_user", "00000"],
  ["advanced_user", "manager", "00000"],
  ["advanced_user", "admin", "00000"],
  // user actor: 전부 거부.
  ["user", "temp_user", "00000"],
  ["user", "user", "00000"],
  ["user", "advanced_user", "00000"],
  ["user", "manager", "00000"],
  ["user", "admin", "00000"],
  // temp_user actor: 전부 거부.
  ["temp_user", "temp_user", "00000"],
  ["temp_user", "user", "00000"],
  ["temp_user", "advanced_user", "00000"],
  ["temp_user", "manager", "00000"],
  ["temp_user", "admin", "00000"],
];

describe("canSetRole — it16 역할 변경 권한 매트릭스(서버 강제)", () => {
  test("전수 표가 25행 × 5열(125조합)을 빠짐없이 덮는다", () => {
    expect(SET_ROLE_MATRIX).toHaveLength(ALL_ROLES.length * ALL_ROLES.length);
    for (const [, , bits] of SET_ROLE_MATRIX) {
      expect(bits).toMatch(/^[01]{5}$/);
    }
  });

  test.each(SET_ROLE_MATRIX)(
    "actor=%s, current=%s → 허용비트 %s (설계 §3.3)",
    (actor, current, bits) => {
      TARGET_ORDER.forEach((target, i) => {
        expect(canSetRole(actor, current, target)).toBe(bits[i] === "1");
      });
    }
  );

  test("Admin: 하위 대역·manager 지정 허용, admin 지정·admin 대상 거부", () => {
    expect(canSetRole("admin", "user", "temp_user")).toBe(true);
    expect(canSetRole("admin", "user", "advanced_user")).toBe(true);
    expect(canSetRole("admin", "user", "manager")).toBe(true);
    expect(canSetRole("admin", "manager", "user")).toBe(true);
    expect(canSetRole("admin", "temp_user", "user")).toBe(true);
    expect(canSetRole("admin", "advanced_user", "temp_user")).toBe(true);
    expect(canSetRole("admin", "user", "admin")).toBe(false); // admin 지정 불가
    expect(canSetRole("admin", "admin", "user")).toBe(false); // admin 대상 불가
  });

  test("Manager: 하위 3역할 대역 자유 지정(it16 완화 — 승격 허용)", () => {
    // it13에서는 거부였던 승격 3조합이 허용으로 반전(설계 §3.3 변경점 표).
    expect(canSetRole("manager", "temp_user", "user")).toBe(true);
    expect(canSetRole("manager", "temp_user", "advanced_user")).toBe(true);
    expect(canSetRole("manager", "user", "advanced_user")).toBe(true);
    // 강등도 허용(it13의 유일 허용이던 U→T 포함).
    expect(canSetRole("manager", "user", "temp_user")).toBe(true);
    expect(canSetRole("manager", "advanced_user", "user")).toBe(true);
    expect(canSetRole("manager", "advanced_user", "temp_user")).toBe(true);
    // no-op(멱등 write)도 허용 — 규칙 4가 대역 전체를 명시하므로 일관적(설계 §3.3).
    expect(canSetRole("manager", "temp_user", "temp_user")).toBe(true);
    expect(canSetRole("manager", "advanced_user", "advanced_user")).toBe(true);
  });

  test("Manager: manager·admin 지정과 manager·admin 대상은 여전히 거부", () => {
    expect(canSetRole("manager", "user", "manager")).toBe(false);
    expect(canSetRole("manager", "advanced_user", "manager")).toBe(false);
    expect(canSetRole("manager", "user", "admin")).toBe(false);
    expect(canSetRole("manager", "manager", "user")).toBe(false);
    expect(canSetRole("manager", "manager", "advanced_user")).toBe(false);
    expect(canSetRole("manager", "admin", "user")).toBe(false);
  });

  test("advanced_user/user/temp_user actor는 전부 거부", () => {
    expect(canSetRole("advanced_user", "user", "temp_user")).toBe(false);
    expect(canSetRole("advanced_user", "temp_user", "advanced_user")).toBe(false);
    expect(canSetRole("user", "user", "temp_user")).toBe(false);
    expect(canSetRole("temp_user", "user", "temp_user")).toBe(false);
  });
});
