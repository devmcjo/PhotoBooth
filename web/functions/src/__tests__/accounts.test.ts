import { Timestamp } from "firebase-admin/firestore";
import { FakeFirestore } from "./helpers/fakeFirestore";

// ── 모듈 격리 ─────────────────────────────────────────────────────────────────
// accounts.ts는 Firestore(db)·email·frames·config·tokens에 의존한다.
// 순수 단위 테스트를 위해 이들을 mock하고, db()는 공유 FakeFirestore 인스턴스를 반환한다.

const fake = new FakeFirestore();

jest.mock("../firebase", () => ({
  db: () => fake,
  storage: () => {
    throw new Error("storage()는 이 테스트에서 사용하지 않습니다.");
  },
}));

// email 발송은 no-op sender로 대체(발송 부수효과 격리).
const sentVerifications: Array<{ email: string; accountId: string }> = [];
jest.mock("../services/email", () => ({
  getEmailSender: () => ({
    sendVerification: async (email: string, opts: { accountId: string }) => {
      sentVerifications.push({ email, accountId: opts.accountId });
    },
    sendPasswordReset: async () => undefined,
  }),
}));

// frames cascade는 계정 삭제 테스트 밖이라 no-op.
jest.mock("../services/frames", () => ({
  deleteAllFramesByUser: async () => undefined,
}));

// config는 email provider 등 최소값만.
jest.mock("../config", () => ({
  loadConfig: () => ({
    jwtSecret: "s",
    jwtExpiresInSeconds: 3600,
    clientApiKeys: ["k"],
    storageBucket: "b",
    hostingBaseUrl: "https://example.test",
    emailProvider: "log",
    emailFrom: "",
    sendgridApiKey: "",
    googleOAuthClientId: "",
    googleOAuthClientSecret: "",
    googleOAuthEnabled: false,
    googleAllowedHd: "",
  }),
}));

// tokens 서비스(issue/consume)는 verify 시점 로직만 필요 — issue는 no-op, consume은 테스트가 제어.
let consumeVerifyResult: { ok: true; email: string } | { ok: false; reason: string } = {
  ok: false,
  reason: "not_found",
};
/** consumeByCode가 어떤 userId로 호출됐는지 캡처(라우팅 검증용). */
const consumeCalls: string[] = [];
jest.mock("../services/tokens", () => ({
  issueToken: async () => ({ token: "tok.tok", code: "000000" }),
  consumeByCode: async (userId: string) => {
    consumeCalls.push(userId);
    return consumeVerifyResult;
  },
  consumeByToken: async () => consumeVerifyResult,
}));

// mock 선언 후 import(순서 중요 — jest.mock은 hoist되지만 명시적 후치로 가독성).
import {
  confirmEmailVerificationByCode,
  confirmPasswordResetByCode,
  createAccount,
  getQrUsage,
  loginWithGoogleEmail,
  registerSelf,
  setRole,
} from "../services/accounts";

/** 계정 문서를 fake에 심는 헬퍼. */
function seedUser(
  id: string,
  overrides: Partial<{
    role: string;
    email: string | null;
    emailVerified: boolean;
    password: string;
  }> = {}
): void {
  fake.seed("users", id, {
    id,
    password: overrides.password ?? "$2b$10$abcdefghijklmnopqrstuv", // 해시 형태(로그인 미사용)
    role: overrides.role ?? "user",
    createdAt: Timestamp.now(),
    email: overrides.email ?? null,
    emailVerified: overrides.emailVerified ?? false,
  });
}

beforeEach(() => {
  // 각 테스트마다 새 store로 격리.
  (fake as unknown as { store: Record<string, unknown> }).store = {};
  sentVerifications.length = 0;
  consumeCalls.length = 0;
  consumeVerifyResult = { ok: false, reason: "not_found" };
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

// ── it13: setRole 권한 매트릭스(TempUser + manager 강등) ──────────────────────
describe("it13 setRole — 권한 매트릭스(서버 강제)", () => {
  const admin = { id: "root", role: "admin" as const };
  const manager = { id: "mgr", role: "manager" as const };

  test("admin이 user→temp_user 강등 성공", async () => {
    seedUser("u1", { role: "user" });
    await setRole("u1", "temp_user", admin);
    expect(fake.peek("users", "u1")?.role).toBe("temp_user");
  });

  test("admin이 temp_user→user 승격 성공(승격은 admin 전용)", async () => {
    seedUser("t1", { role: "temp_user" });
    await setRole("t1", "user", admin);
    expect(fake.peek("users", "t1")?.role).toBe("user");
  });

  test("manager가 user→temp_user 강등 성공(유일 허용)", async () => {
    seedUser("u2", { role: "user" });
    await setRole("u2", "temp_user", manager);
    expect(fake.peek("users", "u2")?.role).toBe("temp_user");
  });

  test("manager가 temp_user→user 승격 거부(403, role 불변)", async () => {
    seedUser("t2", { role: "temp_user" });
    await expect(setRole("t2", "user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "t2")?.role).toBe("temp_user");
  });

  test("manager가 user→manager 승격 거부(403)", async () => {
    seedUser("u3", { role: "user" });
    await expect(setRole("u3", "manager", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "u3")?.role).toBe("user");
  });

  test("manager가 manager 대상 변경 거부(403)", async () => {
    seedUser("m2", { role: "manager" });
    await expect(setRole("m2", "temp_user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "m2")?.role).toBe("manager");
  });

  test("manager가 admin 대상 변경 거부(403)", async () => {
    seedUser("a3", { role: "admin" });
    await expect(setRole("a3", "temp_user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "a3")?.role).toBe("admin");
  });

  test("manager가 temp_user 대상 변경 거부(403, no-op도)", async () => {
    seedUser("t3", { role: "temp_user" });
    await expect(setRole("t3", "temp_user", manager)).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "t3")?.role).toBe("temp_user");
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
      password: "$2b$10$x",
      role: "user",
      createdAt: created,
      qrUsedCount: 7,
      email: null,
      emailVerified: false,
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
      password: "$2b$10$x",
      role: "user",
      createdAt: created,
      qrUsedCount: 12,
      email: null,
      emailVerified: false,
    });

    await setRole("d2", "temp_user", manager);

    const doc = fake.peek("users", "d2")!;
    expect(doc.role).toBe("temp_user");
    expect(doc.qrUsedCount).toBe(12);
    expect((doc.createdAt as Timestamp).toMillis()).toBe(created.toMillis());
  });
});

// ── BE-2: Google SSO 자동 생성/승격 ──────────────────────────────────────────
describe("BE-2 loginWithGoogleEmail — 자동 생성/승격", () => {
  test("계정 없음 → user/emailVerified=true 자동 생성 + LoginResult", async () => {
    const res = await loginWithGoogleEmail("newperson@example.com");
    expect(res).not.toBeNull();
    expect(res?.role).toBe("user");
    expect(res?.user.emailVerified).toBe(true);
    expect(res?.user.email).toBe("newperson@example.com");
    // 생성된 문서가 존재하고 email/verified가 맞는지.
    const created = fake.peek("users", res!.id);
    expect(created?.role).toBe("user");
    expect(created?.emailVerified).toBe(true);
    expect(created?.email).toBe("newperson@example.com");
    // id는 local-part 기반.
    expect(res?.id).toBe("newperson");
  });

  test("자동 생성 계정 비번은 sentinel 해시(bcrypt 형태, 로그인 불가)", async () => {
    const res = await loginWithGoogleEmail("sso@example.com");
    const created = fake.peek("users", res!.id) as { password: string };
    expect(created.password).toMatch(/^\$2[aby]\$\d{2}\$/);
  });

  test("local-part 충돌 → -2 suffix id로 생성", async () => {
    seedUser("dup", { email: "other@example.com", emailVerified: true });
    const res = await loginWithGoogleEmail("dup@example.com");
    expect(res?.id).toBe("dup-2");
    expect(fake.peek("users", "dup-2")?.email).toBe("dup@example.com");
  });

  test("빈 local-part → g- 폴백 id 생성", async () => {
    const res = await loginWithGoogleEmail("한글@example.com");
    expect(res?.id.startsWith("g-")).toBe(true);
    expect(res?.user.emailVerified).toBe(true);
  });

  test("미검증 기존 계정 → emailVerified=true 승격 후 로그인(role/pw 불변)", async () => {
    seedUser("existing", {
      role: "manager",
      email: "keep@example.com",
      emailVerified: false,
      password: "$2b$10$originalhashvaluexxxxx",
    });
    const res = await loginWithGoogleEmail("keep@example.com");
    expect(res).not.toBeNull();
    expect(res?.id).toBe("existing");
    expect(res?.role).toBe("manager"); // role 불변(권한 상승 없음)
    expect(res?.user.emailVerified).toBe(true);
    const doc = fake.peek("users", "existing") as { emailVerified: boolean; password: string };
    expect(doc.emailVerified).toBe(true); // 승격됨
    expect(doc.password).toBe("$2b$10$originalhashvaluexxxxx"); // pw 불변
  });

  test("검증된 기존 계정 → 기존대로 로그인(변경 없음)", async () => {
    seedUser("verified", {
      role: "user",
      email: "v@example.com",
      emailVerified: true,
    });
    const res = await loginWithGoogleEmail("v@example.com");
    expect(res?.id).toBe("verified");
    expect(res?.user.emailVerified).toBe(true);
    // 신규 계정이 생기지 않았는지(users 컬렉션에 1건만).
    expect(fake.all("users").length).toBe(1);
  });

  test("대소문자 다른 email도 소문자 정규화로 기존 계정 매핑", async () => {
    seedUser("mixed", { email: "case@example.com", emailVerified: true });
    const res = await loginWithGoogleEmail("Case@Example.COM");
    expect(res?.id).toBe("mixed");
    expect(fake.all("users").length).toBe(1);
  });
});

// ── BE-3: registerSelf(self-signup) ──────────────────────────────────────────
describe("BE-3 registerSelf — self-signup", () => {
  test("role=user 고정 생성(email 없이)", async () => {
    const user = await registerSelf("newbie", "pw1234", null);
    expect(user.role).toBe("user");
    expect(user.email).toBeNull();
    expect(user.emailVerified).toBe(false);
    expect(fake.peek("users", "newbie")?.role).toBe("user");
  });

  test("email 포함 → unverified 생성 + verify 메일 발송", async () => {
    const user = await registerSelf("withmail", "pw1234", "me@example.com");
    expect(user.email).toBe("me@example.com");
    expect(user.emailVerified).toBe(false);
    expect(sentVerifications).toContainEqual({ email: "me@example.com", accountId: "withmail" });
  });

  test("id 중복 → 409", async () => {
    seedUser("taken");
    await expect(registerSelf("taken", "pw1234", null)).rejects.toMatchObject({ status: 409 });
  });

  test("비밀번호는 bcrypt 해시로 저장(평문 미저장)", async () => {
    await registerSelf("hashme", "plainpw", null);
    const doc = fake.peek("users", "hashme") as { password: string };
    expect(doc.password).not.toBe("plainpw");
    expect(doc.password).toMatch(/^\$2[aby]\$\d{2}\$/);
  });

  test("email이 이미 verified인 다른 계정과 충돌 → 409 + 초과 메시지", async () => {
    seedUser("owner", { email: "shared@example.com", emailVerified: true });
    await expect(registerSelf("second", "pw1234", "shared@example.com")).rejects.toMatchObject({
      status: 409,
      message: "해당 이메일로 생성 가능한 계정 수를 초과하였습니다.",
    });
  });

  test("email이 미인증인 다른 계정과 중복 → 허용(생성 성공)", async () => {
    seedUser("owner", { email: "shared@example.com", emailVerified: false });
    const user = await registerSelf("second", "pw1234", "shared@example.com");
    expect(user.email).toBe("shared@example.com");
    expect(fake.all("users").length).toBe(2);
  });
});

// ── it13: TempUser 계정 생성 권한(admin/manager 허용, user 거부) ──────────────
describe("it13 createAccount — temp_user 생성 권한(canCreate 게이트)", () => {
  test("admin이 temp_user 생성 성공", async () => {
    const user = await createAccount("t1", "pw1234", "temp_user", null, "admin");
    expect(user.role).toBe("temp_user");
    expect(fake.peek("users", "t1")?.role).toBe("temp_user");
  });

  test("manager가 temp_user 생성 성공", async () => {
    const user = await createAccount("t2", "pw1234", "temp_user", null, "manager");
    expect(user.role).toBe("temp_user");
    expect(fake.peek("users", "t2")?.role).toBe("temp_user");
  });

  test("user가 temp_user 생성 거부(403)", async () => {
    await expect(
      createAccount("t3", "pw1234", "temp_user", null, "user")
    ).rejects.toMatchObject({ status: 403 });
    expect(fake.peek("users", "t3")).toBeUndefined();
  });

  test("temp_user 신규 계정은 qrUsedCount 미설정(0 해석)", async () => {
    await createAccount("t4", "pw1234", "temp_user", null, "admin");
    const doc = fake.peek("users", "t4") as { qrUsedCount?: number };
    expect(doc.qrUsedCount).toBeUndefined();
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
      password: "$2b$10$x",
      role,
      createdAt: Timestamp.fromMillis(Date.now() - ageHours * HOUR),
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

// ── BE-4: 이메일 유일성 완화 + verify 시점 검사 ───────────────────────────────
describe("BE-4 이메일 유일성 완화 + verify 시점", () => {
  test("createAccount: verified 다른 계정과 email 충돌 → 409 초과 메시지", async () => {
    seedUser("owner", { email: "e@example.com", emailVerified: true });
    await expect(
      createAccount("child", "pw", "user", "e@example.com", "admin")
    ).rejects.toMatchObject({
      status: 409,
      message: "해당 이메일로 생성 가능한 계정 수를 초과하였습니다.",
    });
  });

  test("createAccount: 미인증 다른 계정과 email 중복 → 허용", async () => {
    seedUser("owner", { email: "e@example.com", emailVerified: false });
    const user = await createAccount("child", "pw", "user", "e@example.com", "admin");
    expect(user.email).toBe("e@example.com");
  });

  test("verify: 다른 계정이 이미 verified한 email → taken(마킹 거부)", async () => {
    // owner가 이미 verified, second는 같은 email 미인증.
    seedUser("owner", { email: "one@example.com", emailVerified: true });
    seedUser("second", { email: "one@example.com", emailVerified: false });
    consumeVerifyResult = { ok: true, email: "one@example.com" };

    const result = await confirmEmailVerificationByCode("second", "000000");
    expect(result).toEqual({ verified: false, reason: "taken" });
    // second는 여전히 미인증(마킹 거부됨).
    expect(fake.peek("users", "second")?.emailVerified).toBe(false);
  });

  test("verify: 유일한 소유자면 마킹 성공(verified=true)", async () => {
    seedUser("solo", { email: "solo@example.com", emailVerified: false });
    consumeVerifyResult = { ok: true, email: "solo@example.com" };

    const result = await confirmEmailVerificationByCode("solo", "000000");
    expect(result).toEqual({ verified: true });
    expect(fake.peek("users", "solo")?.emailVerified).toBe(true);
  });

  test("verify: 코드 소비 실패 → mismatch(401 매핑 대상)", async () => {
    seedUser("solo", { email: "solo@example.com", emailVerified: false });
    consumeVerifyResult = { ok: false, reason: "expired" };

    const result = await confirmEmailVerificationByCode("solo", "000000");
    expect(result).toEqual({ verified: false, reason: "mismatch" });
    expect(fake.peek("users", "solo")?.emailVerified).toBe(false);
  });

  test("verify: 두 미인증 계정 중 첫째는 성공, 둘째는 taken", async () => {
    seedUser("first", { email: "dup@example.com", emailVerified: false });
    seedUser("second", { email: "dup@example.com", emailVerified: false });

    consumeVerifyResult = { ok: true, email: "dup@example.com" };
    const r1 = await confirmEmailVerificationByCode("first", "000000");
    expect(r1).toEqual({ verified: true });

    consumeVerifyResult = { ok: true, email: "dup@example.com" };
    const r2 = await confirmEmailVerificationByCode("second", "000000");
    expect(r2).toEqual({ verified: false, reason: "taken" });
  });

  test("email 조회는 verified 소유자 우선 라우팅(중복 email 시 reset/verify 정확도)", async () => {
    // 미인증 dup + verified owner가 같은 email. email로 조회하는 경로는 owner로 가야 한다.
    seedUser("dupacct", { email: "route@example.com", emailVerified: false });
    seedUser("owneracct", { email: "route@example.com", emailVerified: true });
    consumeVerifyResult = { ok: true, email: "route@example.com" };

    await confirmPasswordResetByCode("route@example.com", "000000", "newpw");
    // consumeByCode가 verified 소유자(owneracct)의 id로 호출됐는지.
    expect(consumeCalls).toContain("owneracct");
    expect(consumeCalls).not.toContain("dupacct");
  });
});
