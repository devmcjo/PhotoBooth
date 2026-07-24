import {
  canCreate,
  canManage,
  creatableRoles,
  isPower,
  isUserRole,
  parseRole,
} from "../domain/roles";

describe("roles — 역할 위계(C# UserRoleExtensions 이식 정합)", () => {
  test("isUserRole 화이트리스트", () => {
    expect(isUserRole("user")).toBe(true);
    expect(isUserRole("manager")).toBe(true);
    expect(isUserRole("admin")).toBe(true);
    expect(isUserRole("root")).toBe(false);
    expect(isUserRole(1)).toBe(false);
    expect(isUserRole(null)).toBe(false);
  });

  test("parseRole 폴백은 user", () => {
    expect(parseRole("admin")).toBe("admin");
    expect(parseRole("manager")).toBe("manager");
    expect(parseRole("user")).toBe("user");
    expect(parseRole("")).toBe("user");
    expect(parseRole(null)).toBe("user");
    expect(parseRole("nonsense")).toBe("user");
  });

  test("isPower: manager/admin만 power", () => {
    expect(isPower("user")).toBe(false);
    expect(isPower("manager")).toBe(true);
    expect(isPower("admin")).toBe(true);
  });

  test("creatableRoles: admin→[user,manager], manager→[user], user→[]", () => {
    expect(creatableRoles("admin")).toEqual(["user", "manager"]);
    expect(creatableRoles("manager")).toEqual(["user"]);
    expect(creatableRoles("user")).toEqual([]);
  });

  test("canCreate: admin은 admin 생성 불가(최종 1인 규칙)", () => {
    expect(canCreate("admin", "user")).toBe(true);
    expect(canCreate("admin", "manager")).toBe(true);
    expect(canCreate("admin", "admin")).toBe(false);

    expect(canCreate("manager", "user")).toBe(true);
    expect(canCreate("manager", "manager")).toBe(false);
    expect(canCreate("manager", "admin")).toBe(false);

    expect(canCreate("user", "user")).toBe(false);
  });

  test("canManage: 자신과 같거나 낮은 역할만(manager는 admin 관리 불가)", () => {
    // admin은 전부 관리
    expect(canManage("admin", "user")).toBe(true);
    expect(canManage("admin", "manager")).toBe(true);
    expect(canManage("admin", "admin")).toBe(true);

    // manager는 admin 관리 불가
    expect(canManage("manager", "user")).toBe(true);
    expect(canManage("manager", "manager")).toBe(true);
    expect(canManage("manager", "admin")).toBe(false);

    // user는 자기(user)만
    expect(canManage("user", "user")).toBe(true);
    expect(canManage("user", "manager")).toBe(false);
    expect(canManage("user", "admin")).toBe(false);
  });
});
