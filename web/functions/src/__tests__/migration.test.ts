/**
 * it15 마이그레이션 순수 계획 로직 단위 테스트(설계 §8.3·§8.4).
 *
 * 이 스크립트는 운영 DB를 만지므로 실행으로 검증할 수 없다. 대신 "무엇을 바꿀지" 판정을
 * 전부 순수 함수로 뽑아(`src/domain/migration.ts`) 여기서 못박는다.
 * 특히 **멱등성**(재실행 시 write 0)과 **dry-run 기본**은 회귀하면 데이터 손실로 직결된다.
 */
import {
  BATCH_SIZE,
  DEFAULT_ADMIN_EMAIL,
  DEFAULT_ADMIN_ID,
  adminDocMatches,
  buildAdminDoc,
  chunk,
  frameStoragePrefix,
  hasLoginEmail,
  isOrphanAccount,
  normalizeEmail,
  parseArgs,
  planFieldCleanup,
} from "../domain/migration";

// ── parseArgs — dry-run 기본과 오조작 방지 ───────────────────────────────────
describe("parseArgs — CLI 인자", () => {
  test("--apply 없으면 dry-run이 기본(이 기본값은 절대 뒤집히면 안 된다)", () => {
    const r = parseArgs(["--project", "mcphoto-955fb"]);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.value.apply).toBe(false);
    expect(r.value.deleteOrphans).toBe(false);
    expect(r.value.clearPin).toBeNull();
  });

  test("--project 누락 → 실패(오조작 방지)", () => {
    const r = parseArgs(["--apply"]);
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.error).toContain("--project");
  });

  test("기본 admin-email·admin-id가 설계 §8.2 값과 일치", () => {
    const r = parseArgs(["--project", "p"]);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.value.adminEmail).toBe(DEFAULT_ADMIN_EMAIL);
    expect(r.value.adminId).toBe(DEFAULT_ADMIN_ID);
  });

  test("admin-email은 소문자 정규화된다", () => {
    const r = parseArgs(["--project", "p", "--admin-email", "  DevMcJo@Gmail.COM "]);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.value.adminEmail).toBe("devmcjo@gmail.com");
  });

  test("모든 플래그 조합 파싱", () => {
    const r = parseArgs([
      "--project", "p",
      "--apply",
      "--delete-orphans",
      "--admin-id", "root",
      "--bucket", "b.appspot.com",
      "--verbose",
    ]);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.value).toMatchObject({
      project: "p",
      apply: true,
      deleteOrphans: true,
      adminId: "root",
      bucket: "b.appspot.com",
      verbose: true,
    });
  });

  test("오타 플래그는 조용히 무시하지 않고 실패시킨다(--aply가 dry-run으로 넘어가면 안 됨)", () => {
    const r = parseArgs(["--project", "p", "--aply"]);
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.error).toContain("--aply");
  });

  test("값이 필요한 플래그에 값 누락 → 실패", () => {
    expect(parseArgs(["--project"]).ok).toBe(false);
    expect(parseArgs(["--project", "p", "--clear-pin"]).ok).toBe(false);
    // 다음 토큰이 또 플래그면 값으로 먹지 않는다.
    expect(parseArgs(["--project", "p", "--admin-id", "--apply"]).ok).toBe(false);
  });

  test("--clear-pin 지정 시 계정 id가 담긴다", () => {
    const r = parseArgs(["--project", "p", "--clear-pin", "devmcjo", "--apply"]);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.value.clearPin).toBe("devmcjo");
    expect(r.value.apply).toBe(true);
  });
});

// ── planFieldCleanup — Step 4 필드 정리와 멱등성 ─────────────────────────────
describe("planFieldCleanup — Step 4 전 계정 필드 정리", () => {
  test("password·emailVerified 삭제 + authMethod sso→google", () => {
    const p = planFieldCleanup({
      id: "a",
      password: "$2b$10$x",
      emailVerified: true,
      authMethod: "sso",
      email: "a@example.com",
    });
    expect(p).not.toBeNull();
    expect(p!.deleteFields.sort()).toEqual(["emailVerified", "password"]);
    expect(p!.setAuthMethod).toBe("google");
  });

  test("authMethod 미설정 → google", () => {
    expect(planFieldCleanup({ id: "a", email: "a@example.com" })?.setAuthMethod).toBe("google");
  });

  test("authMethod 빈 문자열 → google", () => {
    expect(planFieldCleanup({ id: "a", authMethod: "", email: "a@x.com" })?.setAuthMethod).toBe(
      "google"
    );
  });

  test("authMethod null → google", () => {
    expect(planFieldCleanup({ id: "a", authMethod: null, email: "a@x.com" })?.setAuthMethod).toBe(
      "google"
    );
  });

  test("authMethod password + email 보유 → google로 통일", () => {
    const p = planFieldCleanup({ id: "b", authMethod: "password", email: "b@example.com" });
    expect(p?.setAuthMethod).toBe("google");
  });

  test("authMethod password + email 없음 → authMethod 미변경(Step 5 삭제 대상이라 손대지 않는다)", () => {
    const p = planFieldCleanup({ id: "c", authMethod: "password", password: "$2b$10$x" });
    expect(p).not.toBeNull();
    expect(p!.deleteFields).toEqual(["password"]);
    expect(p!.setAuthMethod).toBeNull();
  });

  test("미지원 provider(kakao)는 덮어쓰지 않는다(미래 확장 보호)", () => {
    expect(planFieldCleanup({ id: "d", authMethod: "kakao", email: "d@x.com" })).toBeNull();
  });

  // ★ 멱등성: 이미 it15 스키마인 문서는 write를 발행하지 않는다(§8.4 규칙 4).
  test("이미 마이그레이션된 문서 → null(재실행 시 write 0)", () => {
    expect(
      planFieldCleanup({
        id: "e",
        role: "user",
        email: "e@example.com",
        authMethod: "google",
        qrUsedCount: 3,
      })
    ).toBeNull();
  });

  test("planFieldCleanup 결과를 적용한 문서를 다시 넣으면 null(수렴 확인)", () => {
    const doc: Record<string, unknown> = {
      id: "f",
      password: "$2b$10$x",
      emailVerified: false,
      authMethod: "sso",
      email: "f@example.com",
    };
    const first = planFieldCleanup(doc)!;
    for (const k of first.deleteFields) delete doc[k];
    if (first.setAuthMethod !== null) doc.authMethod = first.setAuthMethod;
    expect(planFieldCleanup(doc)).toBeNull();
  });

  test("emailVerified=false여도 키가 있으면 삭제 대상", () => {
    const p = planFieldCleanup({ id: "g", emailVerified: false, authMethod: "google", email: "g@x.com" });
    expect(p?.deleteFields).toEqual(["emailVerified"]);
  });
});

// ── isOrphanAccount / hasLoginEmail — Step 5 판정 ────────────────────────────
describe("isOrphanAccount — 로그인 불가 계정 판정(D4)", () => {
  test("email 없음 → orphan", () => {
    expect(isOrphanAccount({ id: "x" })).toBe(true);
  });

  test("email null/빈 문자열/공백 → orphan", () => {
    expect(isOrphanAccount({ id: "x", email: null })).toBe(true);
    expect(isOrphanAccount({ id: "x", email: "" })).toBe(true);
    expect(isOrphanAccount({ id: "x", email: "   " })).toBe(true);
  });

  test("email 보유 → orphan 아님(그 주소로 Google 로그인 가능하므로 살린다)", () => {
    expect(isOrphanAccount({ id: "x", email: "x@example.com" })).toBe(false);
    expect(hasLoginEmail({ id: "x", email: "X@Example.COM" })).toBe(true);
  });

  test("email이 문자열이 아니면 orphan(방어)", () => {
    expect(isOrphanAccount({ id: "x", email: 123 })).toBe(true);
  });
});

describe("normalizeEmail", () => {
  test("소문자·트림, 비문자열은 빈 문자열", () => {
    expect(normalizeEmail("  A@B.COM ")).toBe("a@b.com");
    expect(normalizeEmail(null)).toBe("");
    expect(normalizeEmail(undefined)).toBe("");
    expect(normalizeEmail(42)).toBe("");
  });
});

// ── buildAdminDoc — Step 2 문서 조립 ────────────────────────────────────────
describe("buildAdminDoc — admin 문서 재생성(D3)", () => {
  const CREATED = { __ts: 1_700_000_000_000 };

  test("role=admin·authMethod=google·createdAt 승계·email 정규화", () => {
    const doc = buildAdminDoc(
      { id: "devmcjo-2", role: "user", email: "DevMcJo@Gmail.com" },
      "devmcjo",
      CREATED
    );
    expect(doc).toEqual({
      id: "devmcjo",
      role: "admin",
      createdAt: CREATED,
      email: "devmcjo@gmail.com",
      authMethod: "google",
    });
  });

  test("pinHash·qrUsedCount는 있을 때만 승계(부재 필드를 undefined로 만들지 않는다)", () => {
    const withExtras = buildAdminDoc(
      { id: "s", email: "a@b.com", pinHash: "$2b$10$hash", qrUsedCount: 7 },
      "devmcjo",
      CREATED
    );
    expect(withExtras.pinHash).toBe("$2b$10$hash");
    expect(withExtras.qrUsedCount).toBe(7);

    const without = buildAdminDoc({ id: "s", email: "a@b.com" }, "devmcjo", CREATED);
    expect(Object.prototype.hasOwnProperty.call(without, "pinHash")).toBe(false);
    expect(Object.prototype.hasOwnProperty.call(without, "qrUsedCount")).toBe(false);
  });

  test("qrUsedCount=0도 승계된다(0은 falsy지만 유효값)", () => {
    const doc = buildAdminDoc({ id: "s", email: "a@b.com", qrUsedCount: 0 }, "devmcjo", CREATED);
    expect(doc.qrUsedCount).toBe(0);
  });

  test("원본의 password·emailVerified는 절대 옮기지 않는다", () => {
    const doc = buildAdminDoc(
      { id: "s", email: "a@b.com", password: "$2b$10$x", emailVerified: true },
      "devmcjo",
      CREATED
    );
    expect(Object.prototype.hasOwnProperty.call(doc, "password")).toBe(false);
    expect(Object.prototype.hasOwnProperty.call(doc, "emailVerified")).toBe(false);
  });
});

// ── adminDocMatches — Step 2 멱등 판정 ──────────────────────────────────────
describe("adminDocMatches — 재실행 시 SET 생략 판정", () => {
  const eq = (a: unknown, b: unknown) => a === b;
  const target = {
    id: "devmcjo",
    role: "admin",
    createdAt: "T",
    email: "devmcjo@gmail.com",
    authMethod: "google",
  };

  test("현재 문서 없음 → 불일치(SET 필요)", () => {
    expect(adminDocMatches(null, target, eq)).toBe(false);
  });

  test("완전히 동일 → 일치(SET 생략 → 재실행 write 0)", () => {
    expect(adminDocMatches({ ...target }, target, eq)).toBe(true);
  });

  test("role이 admin이 아니면 불일치(부트스트랩 보장 — 설계 §8.3의 무조건 생략보다 안전)", () => {
    expect(adminDocMatches({ ...target, role: "user" }, target, eq)).toBe(false);
  });

  test("잔여 레거시 필드(password)가 남아 있으면 불일치(SET으로 덮어써 제거)", () => {
    expect(adminDocMatches({ ...target, password: "$2b$10$x" }, target, eq)).toBe(false);
  });

  test("pinHash가 목표에만 있으면 불일치", () => {
    expect(adminDocMatches({ ...target }, { ...target, pinHash: "h" }, eq)).toBe(false);
  });

  test("createdAt 비교는 주입된 콜백에 위임(Timestamp.isEqual 대응)", () => {
    const tsA = { isEqual: (o: unknown) => o === tsB };
    const tsB = {};
    const cb = (a: unknown, b: unknown) =>
      a === b || (!!a && typeof (a as { isEqual?: unknown }).isEqual === "function"
        ? (a as { isEqual: (o: unknown) => boolean }).isEqual(b)
        : false);
    expect(adminDocMatches({ ...target, createdAt: tsA }, { ...target, createdAt: tsB }, cb)).toBe(
      true
    );
  });
});

// ── chunk — 배치 상한 대응 ──────────────────────────────────────────────────
describe("chunk — WriteBatch 500 상한 대응", () => {
  test("BATCH_SIZE는 Firestore 상한 500 미만", () => {
    expect(BATCH_SIZE).toBeLessThan(500);
    expect(BATCH_SIZE).toBeGreaterThan(0);
  });

  test("정확히 size 단위로 분할되고 원소가 보존된다", () => {
    const items = Array.from({ length: 1000 }, (_, i) => i);
    const groups = chunk(items, BATCH_SIZE);
    expect(groups.length).toBe(Math.ceil(1000 / BATCH_SIZE));
    expect(groups.every((g) => g.length <= BATCH_SIZE)).toBe(true);
    expect(groups.flat()).toEqual(items);
  });

  test("빈 배열 → 빈 그룹", () => {
    expect(chunk([], BATCH_SIZE)).toEqual([]);
  });

  test("size보다 작으면 1묶음", () => {
    expect(chunk([1, 2, 3], 400)).toEqual([[1, 2, 3]]);
  });
});

describe("frameStoragePrefix", () => {
  test("frames/{userId}/ 규약(services/frames.ts와 동일)", () => {
    expect(frameStoragePrefix("devmcjo")).toBe("frames/devmcjo/");
  });
});
