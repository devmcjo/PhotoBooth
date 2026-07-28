import { Timestamp } from "firebase-admin/firestore";
import { FakeFirestore } from "./helpers/fakeFirestore";

// ── 모듈 격리 ─────────────────────────────────────────────────────────────────
// accounts.ts는 Firestore(db)·frames·config에 의존한다.
// 순수 단위 테스트를 위해 이들을 mock하고, db()는 공유 FakeFirestore 인스턴스를 반환한다.
// it15: email/tokens 서비스는 삭제되어 mock 대상이 아니다.

const fake = new FakeFirestore();

jest.mock("../firebase", () => ({
  db: () => fake,
  storage: () => {
    throw new Error("storage()는 이 테스트에서 사용하지 않습니다.");
  },
}));

// frames cascade는 계정 삭제 테스트 밖이라 no-op.
jest.mock("../services/frames", () => ({
  deleteAllFramesByUser: async () => undefined,
}));

// config는 최소값만(accounts.ts는 loadTempUserLimits 경유로만 config에 닿는다).
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

// mock 선언 후 import(순서 중요 — jest.mock은 hoist되지만 명시적 후치로 가독성).
import {
  getQrUsage,
  loginWithGoogleEmail,
  resetOtherPin,
  setOwnPin,
  setRole,
  verifyPin,
} from "../services/accounts";
import { hashPassword } from "../domain/password";

/** it15 스키마의 계정 문서를 fake에 심는 헬퍼(password·emailVerified 없음). */
function seedUser(
  id: string,
  overrides: Partial<{
    role: string;
    email: string;
    authMethod: string;
  }> = {}
): void {
  fake.seed("users", id, {
    id,
    role: overrides.role ?? "user",
    createdAt: Timestamp.now(),
    email: overrides.email ?? `${id}@example.com`,
    authMethod: overrides.authMethod ?? "google",
  });
}

beforeEach(() => {
  // 각 테스트마다 새 store로 격리.
  (fake as unknown as { store: Record<string, unknown> }).store = {};
});

// ── BE-1: setRole 강등/승격/거부 회귀(소스 무변경, 못박기) ────────────────────
describe("BE-1 setRole — 역할 양방향 변경 회귀", () => {
  const admin = { id: "root", role: "admin" as const };

  test("admin이 manager→user 강등 성공(role 필드 user로 갱신)", async () => {
    seedUser("m1", { role: "manager" });
    await setRole("m1", "user", admin);
    expect(fake.peek("users", "m1")?.role).toBe("user");
  });

  test("admin이 user→manager 승격 성공", async () => {
    seedUser("u1", { role: "user" });
    await setRole("u1", "manager", admin);
    expect(fake.peek("users", "u1")?.role).toBe("manager");
  });

  test("admin 대상 역할 변경 거부(403)", async () => {
    seedUser("a2", { role: "admin" });
    await expect(setRole("a2", "user", admin)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "a2")?.role).toBe("admin");
  });

  test('role="admin" 지정 거부(403)', async () => {
    seedUser("u2", { role: "user" });
    await expect(setRole("u2", "admin", admin)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "u2")?.role).toBe("user");
  });

  test("non-admin actor(user→manager 승격)는 거부(403)", async () => {
    seedUser("u3", { role: "user" });
    const manager = { id: "mgr", role: "manager" as const };
    await expect(setRole("u3", "manager", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "u3")?.role).toBe("user");
  });
});

// ── it16: setRole 권한 매트릭스(설계 §3.3 전수 표) ────────────────────────────
// it13의 "승격=admin 전용"이 **하위 3역할 대역(temp_user·user·advanced_user)에서 완화**된다:
// manager가 그 대역 안에서 자유 지정(승격 포함). manager·admin 지정과 manager·admin 대상은 여전히 admin 전용.
// 순수 판정 전수 검증은 roles.test.ts(canSetRole 125조합)이고, 여기서는 서비스 레벨(문서 write·에러 상태)을 본다.
describe("it16 setRole — 권한 매트릭스(서버 강제)", () => {
  const admin = { id: "root", role: "admin" as const };
  const manager = { id: "mgr", role: "manager" as const };

  test("admin이 user→temp_user 강등 성공", async () => {
    seedUser("u1", { role: "user" });
    await setRole("u1", "temp_user", admin);
    expect(fake.peek("users", "u1")?.role).toBe("temp_user");
  });

  test("admin이 temp_user→user 승격 성공", async () => {
    seedUser("t1", { role: "temp_user" });
    await setRole("t1", "user", admin);
    expect(fake.peek("users", "t1")?.role).toBe("user");
  });

  test("admin이 temp_user→manager 승격 성공(it15 신규 SSO 계정 승격 동선)", async () => {
    seedUser("t0", { role: "temp_user" });
    await setRole("t0", "manager", admin);
    expect(fake.peek("users", "t0")?.role).toBe("manager");
  });

  test("admin이 user→advanced_user 승격 성공(it16 운영 동선)", async () => {
    seedUser("ua1", { role: "user" });
    await setRole("ua1", "advanced_user", admin);
    expect(fake.peek("users", "ua1")?.role).toBe("advanced_user");
  });

  test("admin이 advanced_user→manager 승격 성공", async () => {
    seedUser("aa1", { role: "advanced_user" });
    await setRole("aa1", "manager", admin);
    expect(fake.peek("users", "aa1")?.role).toBe("manager");
  });

  test("manager가 user→temp_user 강등 성공", async () => {
    seedUser("u2", { role: "user" });
    await setRole("u2", "temp_user", manager);
    expect(fake.peek("users", "u2")?.role).toBe("temp_user");
  });

  test("manager가 temp_user→user 승격 성공(it16 완화 — it13에서는 403이었다)", async () => {
    seedUser("t2", { role: "temp_user" });
    await setRole("t2", "user", manager);
    expect(fake.peek("users", "t2")?.role).toBe("user");
  });

  test("manager가 temp_user→advanced_user 승격 성공(it16 §8.4-35)", async () => {
    seedUser("t4", { role: "temp_user" });
    await setRole("t4", "advanced_user", manager);
    expect(fake.peek("users", "t4")?.role).toBe("advanced_user");
  });

  test("manager가 user→advanced_user 승격 성공(it16 §8.4-35)", async () => {
    seedUser("u4", { role: "user" });
    await setRole("u4", "advanced_user", manager);
    expect(fake.peek("users", "u4")?.role).toBe("advanced_user");
  });

  test("manager가 advanced_user→user 강등 성공(it16 §8.4-35)", async () => {
    seedUser("a4", { role: "advanced_user" });
    await setRole("a4", "user", manager);
    expect(fake.peek("users", "a4")?.role).toBe("user");
  });

  test("manager가 advanced_user→temp_user 강등 성공", async () => {
    seedUser("a5", { role: "advanced_user" });
    await setRole("a5", "temp_user", manager);
    expect(fake.peek("users", "a5")?.role).toBe("temp_user");
  });

  test("manager가 temp_user 대상 no-op(멱등 write) 성공", async () => {
    seedUser("t3", { role: "temp_user" });
    await setRole("t3", "temp_user", manager);
    expect(fake.peek("users", "t3")?.role).toBe("temp_user");
  });

  test("manager가 user→manager 승격 거부(403, manager 지정은 admin 전용)", async () => {
    seedUser("u3", { role: "user" });
    await expect(setRole("u3", "manager", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "u3")?.role).toBe("user");
  });

  test("manager가 advanced_user→manager 승격 거부(403, it16 §8.4-35)", async () => {
    seedUser("a6", { role: "advanced_user" });
    await expect(setRole("a6", "manager", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "a6")?.role).toBe("advanced_user");
  });

  test("manager가 manager 대상 변경 거부(403)", async () => {
    seedUser("m2", { role: "manager" });
    await expect(setRole("m2", "temp_user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "m2")?.role).toBe("manager");
  });

  test("manager가 manager 대상을 advanced_user로 강등 거부(403, it16 §8.4-35)", async () => {
    seedUser("m3", { role: "manager" });
    await expect(setRole("m3", "advanced_user", manager)).rejects.toMatchObject({
      status: 403,
    });
    expect(fake.peek("users", "m3")?.role).toBe("manager");
  });

  test("manager가 admin 대상 변경 거부(403)", async () => {
    seedUser("a3", { role: "admin" });
    await expect(setRole("a3", "temp_user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "a3")?.role).toBe("admin");
  });

  test("advanced_user actor는 역할 변경 전부 거부(403 — 계정 관리 권한 없음)", async () => {
    const advanced = { id: "adv", role: "advanced_user" as const };
    seedUser("t5", { role: "temp_user" });
    await expect(setRole("t5", "user", advanced)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "t5")?.role).toBe("temp_user");
  });

  test("존재하지 않는 대상 → 404", async () => {
    await expect(setRole("ghost", "temp_user", admin)).rejects.toMatchObject({ status: 404 });
  });

  // O5(사용자 승인): 강등은 role 필드만 변경 — createdAt·qrUsedCount 절대 불변.
  // 의도: 강등=미결제 회수. createdAt 기준이라 기존 계정은 강등 즉시 시간 초과=QR OFF(회수 취지).
  test("admin이 user→temp_user 강등 시 createdAt·qrUsedCount 불변(사용량 리셋 없음)", async () => {
    const created = Timestamp.fromMillis(1_700_000_000_000);
    fake.seed("users", "d1", {
      id: "d1",
      role: "user",
      createdAt: created,
      qrUsedCount: 7,
      email: "d1@example.com",
      authMethod: "google",
    });

    await setRole("d1", "temp_user", admin);

    const doc = fake.peek("users", "d1")!;
    expect(doc.role).toBe("temp_user"); // role만 변경
    expect(doc.qrUsedCount).toBe(7); // 사용량 리셋 없음
    expect((doc.createdAt as Timestamp).toMillis()).toBe(created.toMillis()); // createdAt 불변(회수 취지)
  });

  test("manager가 user→temp_user 강등 시에도 createdAt·qrUsedCount 불변", async () => {
    const created = Timestamp.fromMillis(1_650_000_000_000);
    fake.seed("users", "d2", {
      id: "d2",
      role: "user",
      createdAt: created,
      qrUsedCount: 12,
      email: "d2@example.com",
      authMethod: "google",
    });

    await setRole("d2", "temp_user", manager);

    const doc = fake.peek("users", "d2")!;
    expect(doc.role).toBe("temp_user");
    expect(doc.qrUsedCount).toBe(12);
    expect((doc.createdAt as Timestamp).toMillis()).toBe(created.toMillis());
  });
});

// ── BE-2: Google SSO 계정 id 파생·매핑(정책 검증은 googleOnlyAccounts.test.ts) ─
describe("BE-2 loginWithGoogleEmail — id 파생과 기존 계정 매핑", () => {
  test("local-part 충돌 → -2 suffix id로 생성", async () => {
    seedUser("dup", { email: "other@example.com" });
    const res = await loginWithGoogleEmail("dup@example.com");
    expect(res?.id).toBe("dup-2");
    expect(fake.peek("users", "dup-2")?.email).toBe("dup@example.com");
  });

  test("빈 local-part → g- 폴백 id 생성", async () => {
    const res = await loginWithGoogleEmail("한글@example.com");
    expect(res?.id.startsWith("g-")).toBe(true);
  });

  test("기존 계정 → 신규 생성 없이 그대로 로그인", async () => {
    seedUser("existing", { role: "manager", email: "keep@example.com" });
    const res = await loginWithGoogleEmail("keep@example.com");
    expect(res?.id).toBe("existing");
    expect(fake.all("users").length).toBe(1);
  });

  test("대소문자 다른 email도 소문자 정규화로 기존 계정 매핑", async () => {
    seedUser("mixed", { email: "case@example.com" });
    const res = await loginWithGoogleEmail("Case@Example.COM");
    expect(res?.id).toBe("mixed");
    expect(fake.all("users").length).toBe(1);
  });

  test("빈 email → null(방어값)", async () => {
    await expect(loginWithGoogleEmail("   ")).resolves.toBeNull();
  });
});

// ── it13: getQrUsage — QR 사용 게이트 상태 조회(§5.3) ─────────────────────────
describe("it13 getQrUsage — QR 사용 게이트 상태", () => {
  const HOUR = 3600_000;

  /** createdAt·qrUsedCount 제어 시드. */
  function seedWithUsage(
    id: string,
    role: string,
    ageHours: number,
    qrUsedCount?: number
  ): void {
    fake.seed("users", id, {
      id,
      role,
      createdAt: Timestamp.fromMillis(Date.now() - ageHours * HOUR),
      email: `${id}@example.com`,
      authMethod: "google",
      qrUsedCount,
    });
  }

  test("정상 TempUser: blocked=false, 잔여 시간·횟수 반영", async () => {
    seedWithUsage("t1", "temp_user", 10, 5); // 10h 경과, 5회 사용
    const res = await getQrUsage({ id: "t1", role: "temp_user" });
    expect(res.role).toBe("temp_user");
    expect(res.blocked).toBe(false);
    expect(res.reason).toBe("ok");
    expect(res.remainingCount).toBe(25); // 30-5
    expect(res.limits).toEqual({ qrHours: 48, qrCount: 30 });
    // 잔여 시간은 ~38h(약간의 실행 지연 허용).
    expect(res.remainingMs).toBeGreaterThan(37 * HOUR);
    expect(res.remainingMs).toBeLessThanOrEqual(38 * HOUR);
  });

  test("시간 초과 TempUser: blocked, reason=time", async () => {
    seedWithUsage("t2", "temp_user", 49, 0);
    const res = await getQrUsage({ id: "t2", role: "temp_user" });
    expect(res.blocked).toBe(true);
    expect(res.reason).toBe("time");
    expect(res.remainingMs).toBe(0);
  });

  test("횟수 초과 TempUser: blocked, reason=count", async () => {
    seedWithUsage("t3", "temp_user", 1, 30);
    const res = await getQrUsage({ id: "t3", role: "temp_user" });
    expect(res.blocked).toBe(true);
    expect(res.reason).toBe("count");
    expect(res.remainingCount).toBe(0);
  });

  test("비TempUser(user)는 항상 blocked:false(무제한, 계정 문서 불요)", async () => {
    // users에 시드하지 않아도(계정 문서 없어도) user는 무제한 응답.
    const res = await getQrUsage({ id: "someone", role: "user" });
    expect(res.blocked).toBe(false);
    expect(res.reason).toBe("ok");
    expect(res.role).toBe("user");
  });

  test("admin도 무제한", async () => {
    const res = await getQrUsage({ id: "root", role: "admin" });
    expect(res.blocked).toBe(false);
    expect(res.role).toBe("admin");
  });

  test("커스텀 config 한도 반영", async () => {
    fake.seed("config", "tempUserLimits", { qrHours: 24, qrCount: 10 });
    seedWithUsage("t4", "temp_user", 1, 3);
    const res = await getQrUsage({ id: "t4", role: "temp_user" });
    expect(res.limits).toEqual({ qrHours: 24, qrCount: 10 });
    expect(res.remainingCount).toBe(7); // 10-3
  });

  test("TempUser 계정 문서 부재 → 404", async () => {
    await expect(getQrUsage({ id: "ghost", role: "temp_user" })).rejects.toMatchObject({
      status: 404,
    });
  });
});

// ── it14: verifyPin(E1) — 게이트 검증(일치/불일치/미설정) ─────────────────────
describe("it14 verifyPin — 설정 진입 게이트 검증", () => {
  /** pinHash가 설정된 계정을 심는다. */
  async function seedWithPin(id: string, pin: string): Promise<void> {
    fake.seed("users", id, {
      id,
      role: "user",
      createdAt: Timestamp.now(),
      email: `${id}@example.com`,
      authMethod: "google",
      pinHash: await hashPassword(pin),
    });
  }

  test("PIN 일치 → {ok:true}", async () => {
    await seedWithPin("v1", "0134");
    await expect(verifyPin("v1", "0134")).resolves.toEqual({ ok: true });
  });

  test("PIN 불일치 → {ok:false, reason:'mismatch'}", async () => {
    await seedWithPin("v2", "0134");
    await expect(verifyPin("v2", "9999")).resolves.toEqual({ ok: false, reason: "mismatch" });
  });

  test("PIN 미설정(pinHash 없음) → {ok:false, reason:'unset'}", async () => {
    seedUser("v3");
    await expect(verifyPin("v3", "0134")).resolves.toEqual({ ok: false, reason: "unset" });
  });

  test("계정 문서 부재 → {ok:false, reason:'unset'}", async () => {
    await expect(verifyPin("ghost", "0134")).resolves.toEqual({ ok: false, reason: "unset" });
  });
});

// ── it14: setOwnPin(E2) — 본인 설정/변경(최초/현재 PIN 확인) ──────────────────
describe("it14 setOwnPin — 본인 PIN 설정/변경", () => {
  test("최초 설정(pinHash 없음, currentPin null) → pinHash 저장", async () => {
    seedUser("s1");
    await setOwnPin("s1", null, "0134");
    const hash = fake.peek("users", "s1")?.pinHash as string;
    expect(hash).toMatch(/^\$2[aby]\$\d{2}\$/); // bcrypt 해시
    // 새 PIN으로 검증 통과.
    await expect(verifyPin("s1", "0134")).resolves.toEqual({ ok: true });
  });

  test("기존 PIN 있음 + currentPin 일치 → 변경 성공", async () => {
    seedUser("s2");
    await setOwnPin("s2", null, "0134"); // 최초 설정
    await setOwnPin("s2", "0134", "5678"); // 변경
    await expect(verifyPin("s2", "5678")).resolves.toEqual({ ok: true });
    await expect(verifyPin("s2", "0134")).resolves.toEqual({ ok: false, reason: "mismatch" });
  });

  test("기존 PIN 있음 + currentPin 불일치 → 401(변경 안 됨)", async () => {
    seedUser("s3");
    await setOwnPin("s3", null, "0134");
    await expect(setOwnPin("s3", "9999", "5678")).rejects.toMatchObject({ status: 401 });
    // PIN 불변.
    await expect(verifyPin("s3", "0134")).resolves.toEqual({ ok: true });
  });

  test("기존 PIN 있음 + currentPin null → 401(현재 PIN 확인 필수)", async () => {
    seedUser("s4");
    await setOwnPin("s4", null, "0134");
    await expect(setOwnPin("s4", null, "5678")).rejects.toMatchObject({ status: 401 });
  });

  test("계정 문서 부재 → 404", async () => {
    await expect(setOwnPin("ghost", null, "0134")).rejects.toMatchObject({ status: 404 });
  });
});

// ── it14: resetOtherPin(E3) — 타 계정 재설정(권한 기반) ───────────────────────
describe("it14 resetOtherPin — 타 계정 PIN 재설정(canManage)", () => {
  const admin = { id: "root", role: "admin" as const };
  const manager = { id: "mgr", role: "manager" as const };

  test("admin이 user PIN 재설정 성공(대상 현재 PIN 불요)", async () => {
    seedUser("u1", { role: "user" });
    await resetOtherPin("u1", "0134", admin);
    await expect(verifyPin("u1", "0134")).resolves.toEqual({ ok: true });
  });

  test("manager가 user PIN 재설정 성공", async () => {
    seedUser("u2", { role: "user" });
    await resetOtherPin("u2", "5678", manager);
    await expect(verifyPin("u2", "5678")).resolves.toEqual({ ok: true });
  });

  test("manager가 admin PIN 재설정 거부(403, canManage 위반)", async () => {
    seedUser("a1", { role: "admin" });
    await expect(resetOtherPin("a1", "0134", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "a1")?.pinHash).toBeUndefined(); // 미변경
  });

  test("manager가 manager PIN 재설정 성공(같은 위계 관리 가능)", async () => {
    seedUser("m2", { role: "manager" });
    await resetOtherPin("m2", "0134", manager);
    await expect(verifyPin("m2", "0134")).resolves.toEqual({ ok: true });
  });

  test("manager가 temp_user PIN 재설정 성공(it15 신규 계정 PIN 부여 동선)", async () => {
    seedUser("t9", { role: "temp_user" });
    await resetOtherPin("t9", "2468", manager);
    await expect(verifyPin("t9", "2468")).resolves.toEqual({ ok: true });
  });

  test("대상 계정 부재 → 404", async () => {
    await expect(resetOtherPin("ghost", "0134", admin)).rejects.toMatchObject({ status: 404 });
  });
});
