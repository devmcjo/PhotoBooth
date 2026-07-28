import {
  canCreate,
  canManage,
  canSetRole,
  creatableRoles,
  isPower,
  isUserRole,
  parseRole,
  UserRole,
} from "../domain/roles";

describe("roles — 역할 위계(C# UserRoleExtensions 이식 정합)", () => {
  test("isUserRole 화이트리스트(temp_user 포함)", () => {
    expect(isUserRole("temp_user")).toBe(true);
    expect(isUserRole("user")).toBe(true);
    expect(isUserRole("manager")).toBe(true);
    expect(isUserRole("admin")).toBe(true);
    expect(isUserRole("root")).toBe(false);
    expect(isUserRole("tempuser")).toBe(false); // snake_case만 허용
    expect(isUserRole(1)).toBe(false);
    expect(isUserRole(null)).toBe(false);
  });

  test("parseRole 폴백은 user(temp_user 라운드트립)", () => {
    expect(parseRole("admin")).toBe("admin");
    expect(parseRole("manager")).toBe("manager");
    expect(parseRole("temp_user")).toBe("temp_user");
    expect(parseRole("user")).toBe("user");
    expect(parseRole("")).toBe("user");
    expect(parseRole(null)).toBe("user");
    expect(parseRole("nonsense")).toBe("user");
    expect(parseRole("tempuser")).toBe("user"); // 오탈자 → 최소 권한 폴백
  });

  test("isPower: manager/admin만 power(temp_user 제외)", () => {
    expect(isPower("temp_user")).toBe(false);
    expect(isPower("user")).toBe(false);
    expect(isPower("manager")).toBe(true);
    expect(isPower("admin")).toBe(true);
  });

  test("creatableRoles: admin→[temp_user,user,manager], manager→[temp_user,user], 하위→[]", () => {
    expect(creatableRoles("admin")).toEqual(["temp_user", "user", "manager"]);
    expect(creatableRoles("manager")).toEqual(["temp_user", "user"]);
    expect(creatableRoles("user")).toEqual([]);
    expect(creatableRoles("temp_user")).toEqual([]);
  });

  test("canCreate: admin/manager는 temp_user 생성 가능, user/temp_user는 불가", () => {
    // temp_user 생성 권한
    expect(canCreate("admin", "temp_user")).toBe(true);
    expect(canCreate("manager", "temp_user")).toBe(true);
    expect(canCreate("user", "temp_user")).toBe(false);
    expect(canCreate("temp_user", "temp_user")).toBe(false);

    // 기존 규칙 불변(admin은 admin 생성 불가 — 최종 1인 규칙)
    expect(canCreate("admin", "user")).toBe(true);
    expect(canCreate("admin", "manager")).toBe(true);
    expect(canCreate("admin", "admin")).toBe(false);

    expect(canCreate("manager", "user")).toBe(true);
    expect(canCreate("manager", "manager")).toBe(false);
    expect(canCreate("manager", "admin")).toBe(false);

    expect(canCreate("user", "user")).toBe(false);
  });

  test("canManage: 자신과 같거나 낮은 위계만(temp_user 최하위, 서수 무관)", () => {
    const roles = ["temp_user", "user", "manager", "admin"] as const;
    // MANAGE_RANK: temp_user(0) < user(1) < manager(2) < admin(3)
    const rank: Record<(typeof roles)[number], number> = {
      temp_user: 0,
      user: 1,
      manager: 2,
      admin: 3,
    };
    for (const acting of roles) {
      for (const target of roles) {
        expect(canManage(acting, target)).toBe(rank[target] <= rank[acting]);
      }
    }

    // 대표 케이스 명시(위계표 §3.2)
    expect(canManage("temp_user", "temp_user")).toBe(true);
    expect(canManage("temp_user", "user")).toBe(false);
    expect(canManage("user", "temp_user")).toBe(true);
    expect(canManage("admin", "temp_user")).toBe(true);
    expect(canManage("manager", "admin")).toBe(false);
  });
});

describe("canSetRole — it13 역할 변경 권한 매트릭스(서버 강제)", () => {
  const ROLES: UserRole[] = ["temp_user", "user", "manager", "admin"];

  /** 기대 매트릭스(설계 확정): actor→current→target 허용 여부. */
  function expected(
    actor: UserRole,
    current: UserRole,
    target: UserRole
  ): boolean {
    if (target === "admin") return false; // admin 지정 불가
    if (current === "admin") return false; // admin 대상 변경 불가
    if (actor === "admin") return true; // target은 temp_user/user/manager
    if (actor === "manager") return current === "user" && target === "temp_user";
    return false; // user/temp_user는 전부 불가
  }

  test("전체 4×4×4 조합이 기대 매트릭스와 일치", () => {
    for (const actor of ROLES) {
      for (const current of ROLES) {
        for (const target of ROLES) {
          expect(canSetRole(actor, current, target)).toBe(
            expected(actor, current, target)
          );
        }
      }
    }
  });

  test("Admin: temp_user/user/manager 지정 허용, admin 지정·admin 대상 거부", () => {
    expect(canSetRole("admin", "user", "temp_user")).toBe(true);
    expect(canSetRole("admin", "user", "manager")).toBe(true);
    expect(canSetRole("admin", "manager", "user")).toBe(true);
    expect(canSetRole("admin", "temp_user", "user")).toBe(true); // 승격(admin 전용)
    expect(canSetRole("admin", "user", "admin")).toBe(false); // admin 지정 불가
    expect(canSetRole("admin", "admin", "user")).toBe(false); // admin 대상 불가
  });

  test("Manager: 오직 user→temp_user 강등만", () => {
    expect(canSetRole("manager", "user", "temp_user")).toBe(true);
    // 승격 금지
    expect(canSetRole("manager", "temp_user", "user")).toBe(false);
    expect(canSetRole("manager", "user", "manager")).toBe(false);
    // 대상 제한
    expect(canSetRole("manager", "manager", "user")).toBe(false);
    expect(canSetRole("manager", "admin", "user")).toBe(false);
    expect(canSetRole("manager", "temp_user", "temp_user")).toBe(false); // no-op도 거부
  });

  test("user/temp_user actor는 전부 거부", () => {
    expect(canSetRole("user", "user", "temp_user")).toBe(false);
    expect(canSetRole("temp_user", "user", "temp_user")).toBe(false);
  });
});
