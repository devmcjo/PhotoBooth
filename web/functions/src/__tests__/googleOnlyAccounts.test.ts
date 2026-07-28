/**
 * it15 — Google-only 계정 정책 회귀 (설계 §5.3·§5.5·§10.3 T8~T10).
 *
 * 못박는 것 3가지:
 *   T8. createGoogleAccount가 role:"temp_user" · authMethod:"google"로 생성하고 password를 쓰지 않는다.
 *   T9. toResponse가 emailVerified를 포함하지 않고 authMethod를 저장값 그대로 반환한다(§9.1 와이어 형식).
 *   T10. 기존 계정 SSO 재로그인 시 role이 유지된다(P3 — 승격된 계정이 강등되지 않는다).
 *
 * §9.3 계약 위반 감지: UserResponse 픽스처는 설계 §9.1의 JSON을 그대로 쓴다(클라 DTO 테스트와 동일 문자열).
 */
import { Timestamp } from "firebase-admin/firestore";
import { FakeFirestore } from "./helpers/fakeFirestore";

const fake = new FakeFirestore();

jest.mock("../firebase", () => ({
  db: () => fake,
  storage: () => {
    throw new Error("storage()는 이 테스트에서 사용하지 않습니다.");
  },
}));

jest.mock("../services/frames", () => ({
  deleteAllFramesByUser: async () => undefined,
}));

jest.mock("../config", () => ({
  loadConfig: () => ({
    jwtSecret: "s",
    jwtExpiresInSeconds: 3600,
    clientApiKeys: ["k"],
    storageBucket: "b",
    hostingBaseUrl: "https://example.test",
    googleOAuthClientId: "",
    googleOAuthClientSecret: "",
    googleOAuthEnabled: false,
    googleAllowedHd: "",
  }),
}));

import { loginWithGoogleEmail } from "../services/accounts";

/**
 * 설계 §9.1의 동결 `UserResponse` 와이어 형식(그대로 복사 — 한 글자도 바꾸지 말 것).
 * 클라(C#) `HttpAccountServiceTests`가 같은 문자열을 픽스처로 쓴다(§9.3).
 */
const FROZEN_USER_RESPONSE_JSON = `{
  "id": "devmcjo",
  "role": "admin",
  "createdAt": "2025-11-02T08:31:00.000Z",
  "email": "devmcjo@gmail.com",
  "authMethod": "google",
  "hasPin": true
}`;

beforeEach(() => {
  (fake as unknown as { store: Record<string, unknown> }).store = {};
});

// ── T8: 신규 SSO 계정 생성 정책 ──────────────────────────────────────────────
describe("T8 createGoogleAccount — 신규 SSO 계정은 temp_user + authMethod:'google'", () => {
  test("계정 없음 → role:'temp_user'로 생성되고 LoginResult.role도 temp_user", async () => {
    const res = await loginWithGoogleEmail("newperson@example.com");
    expect(res).not.toBeNull();
    expect(res!.role).toBe("temp_user");
    expect(res!.user.role).toBe("temp_user");
    expect(fake.peek("users", res!.id)?.role).toBe("temp_user");
    // id는 email local-part 기반.
    expect(res!.id).toBe("newperson");
  });

  test("저장 문서에 authMethod:'google'이 기록된다(D2)", async () => {
    const res = await loginWithGoogleEmail("gacct@example.com");
    expect(fake.peek("users", res!.id)?.authMethod).toBe("google");
    expect(res!.user.authMethod).toBe("google");
  });

  test("저장 문서에 password·emailVerified 키가 아예 없다(it15 필드 폐지)", async () => {
    const res = await loginWithGoogleEmail("nopw@example.com");
    const doc = fake.peek("users", res!.id)!;
    expect(Object.prototype.hasOwnProperty.call(doc, "password")).toBe(false);
    expect(Object.prototype.hasOwnProperty.call(doc, "emailVerified")).toBe(false);
  });

  test("저장 문서 키 집합이 it15 UserDoc 스키마와 정확히 일치한다", async () => {
    const res = await loginWithGoogleEmail("schema@example.com");
    const doc = fake.peek("users", res!.id)!;
    expect(Object.keys(doc).sort()).toEqual(
      ["authMethod", "createdAt", "email", "id", "role"].sort()
    );
  });

  test("신규 계정은 qrUsedCount 미설정(0 해석) · pinHash 미설정", async () => {
    const res = await loginWithGoogleEmail("fresh@example.com");
    const doc = fake.peek("users", res!.id)!;
    expect(doc.qrUsedCount).toBeUndefined();
    expect(doc.pinHash).toBeUndefined();
    expect(res!.user.hasPin).toBe(false);
  });
});

// ── T9: toResponse 와이어 형식(§9.1 동결) ────────────────────────────────────
describe("T9 toResponse — 응답 스키마(§9.1 동결 계약)", () => {
  /** §9.1 픽스처와 동일한 상태의 계정을 심는다. */
  function seedFrozenUser(): void {
    fake.seed("users", "devmcjo", {
      id: "devmcjo",
      role: "admin",
      createdAt: Timestamp.fromDate(new Date("2025-11-02T08:31:00.000Z")),
      email: "devmcjo@gmail.com",
      authMethod: "google",
      pinHash: "$2b$10$abcdefghijklmnopqrstuv",
    });
  }

  test("응답이 §9.1 동결 JSON과 정확히 일치한다(키 집합·값 모두)", async () => {
    seedFrozenUser();
    const res = await loginWithGoogleEmail("devmcjo@gmail.com");
    expect(res!.user).toEqual(JSON.parse(FROZEN_USER_RESPONSE_JSON));
  });

  test("응답에 emailVerified·password·pinHash가 없다", async () => {
    seedFrozenUser();
    const res = await loginWithGoogleEmail("devmcjo@gmail.com");
    const wire = res!.user as unknown as Record<string, unknown>;
    expect(Object.prototype.hasOwnProperty.call(wire, "emailVerified")).toBe(false);
    expect(Object.prototype.hasOwnProperty.call(wire, "password")).toBe(false);
    expect(Object.prototype.hasOwnProperty.call(wire, "pinHash")).toBe(false);
  });

  test("authMethod는 저장값 그대로 노출된다(미지원 provider도 오인 없이 전달)", async () => {
    fake.seed("users", "kakaoacct", {
      id: "kakaoacct",
      role: "user",
      createdAt: Timestamp.now(),
      email: "kakao@example.com",
      authMethod: "kakao",
    });
    const res = await loginWithGoogleEmail("kakao@example.com");
    expect(res!.user.authMethod).toBe("kakao");
  });

  test("authMethod 미설정(레거시 미마이그레이션) 문서 → 'google' 폴백", async () => {
    fake.seed("users", "legacy", {
      id: "legacy",
      role: "user",
      createdAt: Timestamp.now(),
      email: "legacy@example.com",
    });
    const res = await loginWithGoogleEmail("legacy@example.com");
    expect(res!.user.authMethod).toBe("google");
  });

  test("pinHash 존재 → hasPin=true(원문 미노출)", async () => {
    seedFrozenUser();
    const res = await loginWithGoogleEmail("devmcjo@gmail.com");
    expect(res!.user.hasPin).toBe(true);
  });
});

// ── T10: 기존 계정 재로그인은 role을 건드리지 않는다(P3) ─────────────────────
describe("T10 loginExistingGoogleAccount — 재로그인 시 role 유지(강등 없음)", () => {
  /** 임의 역할의 기존 계정을 심는다. */
  function seedExisting(id: string, role: string, email: string): void {
    fake.seed("users", id, {
      id,
      role,
      createdAt: Timestamp.now(),
      email,
      authMethod: "google",
    });
  }

  test.each(["admin", "manager", "user", "temp_user"])(
    "%s 계정 재로그인 → role 유지(DB write 없음)",
    async (role) => {
      seedExisting("acct", role, "acct@example.com");
      const before = fake.peek("users", "acct")!;

      const res = await loginWithGoogleEmail("acct@example.com");

      expect(res!.role).toBe(role);
      expect(res!.user.role).toBe(role);
      // 읽기 전용 경로 — 문서가 바이트 단위로 동일해야 한다(승격 write 삭제 확인).
      expect(fake.peek("users", "acct")).toEqual(before);
      expect(fake.all("users").length).toBe(1);
    }
  );

  test("승격된 계정(temp_user→user)이 재로그인으로 temp_user로 되돌아가지 않는다", async () => {
    seedExisting("promoted", "user", "promoted@example.com");
    const res = await loginWithGoogleEmail("promoted@example.com");
    expect(res!.role).toBe("user");
    expect(fake.peek("users", "promoted")?.role).toBe("user");
  });

  test("email 필드가 조회 email과 불일치하면 로그인 거부(방어)", async () => {
    // where 조회는 email로 매칭되지만, 문서 email이 정규화되지 않은 경우를 모사.
    fake.seed("users", "weird", {
      id: "weird",
      role: "user",
      createdAt: Timestamp.now(),
      email: "Mixed@Example.com", // 소문자 정규화 안 됨
      authMethod: "google",
    });
    // 소문자 조회로는 찾히지 않으므로 신규 계정이 생성된다(기존 문서는 불변).
    const res = await loginWithGoogleEmail("mixed@example.com");
    expect(res!.id).not.toBe("weird");
    expect(fake.peek("users", "weird")?.email).toBe("Mixed@Example.com");
  });
});
