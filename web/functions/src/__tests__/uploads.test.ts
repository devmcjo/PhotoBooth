/**
 * uploads 서비스 TempUser 한도 강제 테스트(설계 §5.1·§8.3, Step 4).
 *
 * prepare 선검사(초과 403), commit 트랜잭션(재판정 403 / 성공 시 qrUsedCount +1), 게스트·User↑ 무제한,
 * 동시 경합 시 마지막 1회만 통과. db()는 FakeFirestore, signing·루트 config는 mock.
 * services/config(loadTempUserLimits)는 실제로 두고 fake에 config 문서를 시드해 검증한다.
 */
import { Timestamp } from "firebase-admin/firestore";
import { FakeFirestore } from "./helpers/fakeFirestore";

const fake = new FakeFirestore();
const BUCKET = "test-bucket";

jest.mock("../firebase", () => ({
  db: () => fake,
  storage: () => {
    throw new Error("storage()는 이 테스트에서 사용하지 않습니다.");
  },
}));

// 루트 config(loadConfig): storageBucket만 필요.
jest.mock("../config", () => ({
  loadConfig: () => ({ storageBucket: BUCKET }),
}));

// signing: 서명 없이 결정적 URL 반환(Emulator·IAM 의존 제거).
jest.mock("../services/signing", () => ({
  createSignedUpload: async (bucket: string, path: string) => ({
    putUrl: `https://put.test/${path}`,
    downloadUrl: `https://firebasestorage.googleapis.com/v0/b/${bucket}/o/${encodeURIComponent(path)}?alt=media&token=t`,
    requiredHeaders: {},
  }),
}));

import { AuthPrincipal } from "../domain/jwt";
import { commitUpload, prepareUpload, CommitInput } from "../services/uploads";
import { UploadFileSpec } from "../domain/validation";

const HOUR = 3600_000;
const SESSION = "20260101_120000_11111111-1111-1111-1111-111111111111";

/** downloadUrl과 동일 규약의 final URL(commit용). */
function finalUrl(sessionId: string): string {
  const path = `results/${sessionId}/final.jpg`;
  return `https://firebasestorage.googleapis.com/v0/b/${BUCKET}/o/${encodeURIComponent(path)}?alt=media&token=t`;
}

function commitInput(sessionId: string): CommitInput {
  return {
    sessionId,
    finalImageUrl: finalUrl(sessionId),
    timelapseUrl: null,
    retentionHours: 24,
    downloadPageUrl: "https://dl.test/?s=t",
  };
}

const FINAL_FILE: UploadFileSpec[] = [
  { kind: "final", ext: "jpg", contentType: "image/jpeg" },
];

const tempPrincipal = (id: string): AuthPrincipal => ({ id, role: "temp_user" });
const userPrincipal = (id: string): AuthPrincipal => ({ id, role: "user" });

/** TempUser 계정 시드(createdAt·qrUsedCount 제어). */
function seedTempUser(
  id: string,
  opts: { ageHours?: number; qrUsedCount?: number } = {}
): void {
  const ageHours = opts.ageHours ?? 1;
  fake.seed("users", id, {
    id,
    password: "$2b$10$x",
    role: "temp_user",
    createdAt: Timestamp.fromMillis(Date.now() - ageHours * HOUR),
    qrUsedCount: opts.qrUsedCount,
  });
}

/** 전역 한도 config 시드(미시드 시 기본 48h/30회 폴백). */
function seedLimits(qrHours: number, qrCount: number): void {
  fake.seed("config", "tempUserLimits", { qrHours, qrCount });
}

beforeEach(() => {
  (fake as unknown as { store: Record<string, unknown> }).store = {};
});

describe("uploads prepare — TempUser 한도 선검사", () => {
  test("정상 TempUser: prepare 통과(서명 URL 발급)", async () => {
    seedTempUser("t1", { ageHours: 1, qrUsedCount: 0 });
    const res = await prepareUpload(SESSION, FINAL_FILE, tempPrincipal("t1"));
    expect(res.bucket).toBe(BUCKET);
    expect(res.uploads).toHaveLength(1);
  });

  test("시간 초과 TempUser: prepare 403 TEMP_USER_TIME_EXCEEDED", async () => {
    seedTempUser("t2", { ageHours: 49, qrUsedCount: 0 }); // 기본 48h 초과
    await expect(prepareUpload(SESSION, FINAL_FILE, tempPrincipal("t2"))).rejects.toMatchObject({
      status: 403,
      code: "TEMP_USER_TIME_EXCEEDED",
      message: "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.",
    });
  });

  test("횟수 초과 TempUser: prepare 403 TEMP_USER_COUNT_EXCEEDED", async () => {
    seedTempUser("t3", { ageHours: 1, qrUsedCount: 30 }); // 기본 30회 소진
    await expect(prepareUpload(SESSION, FINAL_FILE, tempPrincipal("t3"))).rejects.toMatchObject({
      status: 403,
      code: "TEMP_USER_COUNT_EXCEEDED",
      message: "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.",
    });
  });

  test("둘 다 초과: 시간 우선(TEMP_USER_TIME_EXCEEDED)", async () => {
    seedTempUser("t4", { ageHours: 50, qrUsedCount: 40 });
    await expect(prepareUpload(SESSION, FINAL_FILE, tempPrincipal("t4"))).rejects.toMatchObject({
      code: "TEMP_USER_TIME_EXCEEDED",
    });
  });

  test("게스트(무 principal): prepare 통과(무제한)", async () => {
    const res = await prepareUpload(SESSION, FINAL_FILE, undefined);
    expect(res.uploads).toHaveLength(1);
  });

  test("User(비TempUser): prepare 통과(무제한)", async () => {
    const res = await prepareUpload(SESSION, FINAL_FILE, userPrincipal("u1"));
    expect(res.uploads).toHaveLength(1);
  });

  test("커스텀 config 한도 반영(qrCount=2, 2회 사용 시 초과)", async () => {
    seedLimits(48, 2);
    seedTempUser("t5", { ageHours: 1, qrUsedCount: 2 });
    await expect(prepareUpload(SESSION, FINAL_FILE, tempPrincipal("t5"))).rejects.toMatchObject({
      code: "TEMP_USER_COUNT_EXCEEDED",
    });
  });
});

describe("uploads commit — TempUser 카운트 증가·재검사(트랜잭션)", () => {
  test("정상 TempUser: commit 성공 + qrUsedCount +1", async () => {
    seedTempUser("c1", { ageHours: 1, qrUsedCount: 5 });
    const res = await commitUpload(commitInput(SESSION), tempPrincipal("c1"));
    expect(res.id).toBe(SESSION);
    // 세션 문서 생성됨.
    expect(fake.peek("resultSessions", SESSION)).toBeDefined();
    // 카운트 5 → 6.
    expect(fake.peek("users", "c1")?.qrUsedCount).toBe(6);
  });

  test("qrUsedCount 미설정(0) → commit 후 1", async () => {
    seedTempUser("c2", { ageHours: 1, qrUsedCount: undefined });
    await commitUpload(commitInput(SESSION), tempPrincipal("c2"));
    expect(fake.peek("users", "c2")?.qrUsedCount).toBe(1);
  });

  test("시간 초과 TempUser: commit 403 + 세션 미생성 + 카운트 불변", async () => {
    seedTempUser("c3", { ageHours: 49, qrUsedCount: 5 });
    await expect(commitUpload(commitInput(SESSION), tempPrincipal("c3"))).rejects.toMatchObject({
      status: 403,
      code: "TEMP_USER_TIME_EXCEEDED",
    });
    expect(fake.peek("resultSessions", SESSION)).toBeUndefined();
    expect(fake.peek("users", "c3")?.qrUsedCount).toBe(5); // 미증가
  });

  test("횟수 초과 TempUser: commit 403 + 카운트 불변", async () => {
    seedTempUser("c4", { ageHours: 1, qrUsedCount: 30 });
    await expect(commitUpload(commitInput(SESSION), tempPrincipal("c4"))).rejects.toMatchObject({
      code: "TEMP_USER_COUNT_EXCEEDED",
    });
    expect(fake.peek("users", "c4")?.qrUsedCount).toBe(30);
  });

  test("중복 sessionId 재commit: 409 + 카운트 이중증가 없음", async () => {
    seedTempUser("c5", { ageHours: 1, qrUsedCount: 0 });
    await commitUpload(commitInput(SESSION), tempPrincipal("c5"));
    expect(fake.peek("users", "c5")?.qrUsedCount).toBe(1);
    // 동일 sessionId 재commit → 409, 카운트 여전히 1.
    await expect(commitUpload(commitInput(SESSION), tempPrincipal("c5"))).rejects.toMatchObject({
      status: 409,
    });
    expect(fake.peek("users", "c5")?.qrUsedCount).toBe(1);
  });

  test("계정 문서 없음(비정상 principal): commit 401", async () => {
    // users에 시드하지 않음.
    await expect(commitUpload(commitInput(SESSION), tempPrincipal("ghost"))).rejects.toMatchObject({
      status: 401,
    });
    expect(fake.peek("resultSessions", SESSION)).toBeUndefined();
  });

  test("게스트(무 principal): commit 성공 + users 미접근(카운트 없음)", async () => {
    const res = await commitUpload(commitInput(SESSION), undefined);
    expect(res.id).toBe(SESSION);
    expect(fake.peek("resultSessions", SESSION)).toBeDefined();
    // users 컬렉션에 아무것도 없어야(게스트는 계정 무관).
    expect(fake.all("users")).toHaveLength(0);
  });

  test("User(비TempUser): commit 성공 + 카운트 로직 미적용", async () => {
    fake.seed("users", "u2", {
      id: "u2",
      password: "$2b$10$x",
      role: "user",
      createdAt: Timestamp.now(),
    });
    await commitUpload(commitInput(SESSION), userPrincipal("u2"));
    expect(fake.peek("resultSessions", SESSION)).toBeDefined();
    // User는 qrUsedCount를 건드리지 않는다.
    expect(fake.peek("users", "u2")?.qrUsedCount).toBeUndefined();
  });

  test("마지막 1회 경합: 순차 2세션 중 두 번째는 count 초과로 거부", async () => {
    // 한도 1회. 첫 세션 성공(count 0→1), 두 번째는 count=1로 초과.
    seedLimits(48, 1);
    seedTempUser("c6", { ageHours: 1, qrUsedCount: 0 });
    const s1 = "20260101_120000_aaaaaaaa-1111-1111-1111-111111111111";
    const s2 = "20260101_120001_bbbbbbbb-2222-2222-2222-222222222222";
    await commitUpload(commitInput(s1), tempPrincipal("c6"));
    expect(fake.peek("users", "c6")?.qrUsedCount).toBe(1);
    await expect(commitUpload(commitInput(s2), tempPrincipal("c6"))).rejects.toMatchObject({
      code: "TEMP_USER_COUNT_EXCEEDED",
    });
    // 두 번째 세션 미생성.
    expect(fake.peek("resultSessions", s2)).toBeUndefined();
  });
});
