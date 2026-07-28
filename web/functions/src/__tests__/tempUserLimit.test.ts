import {
  DEFAULT_TEMP_USER_LIMITS,
  evaluateQrGate,
  TempUserLimits,
} from "../domain/tempUserLimit";

const HOUR = 3600_000;

describe("tempUserLimit — evaluateQrGate(순수 QR 한도 판정)", () => {
  const limits: TempUserLimits = { qrHours: 48, qrCount: 30 };
  // createdAt 기준 시각(임의 epoch).
  const createdAt = 1_700_000_000_000;

  test("기본값 상수는 48h/30회(설계 §0)", () => {
    expect(DEFAULT_TEMP_USER_LIMITS).toEqual({ qrHours: 48, qrCount: 30 });
  });

  test("미초과: 잔여 시간·횟수 정확", () => {
    const now = createdAt + 10 * HOUR; // 10시간 경과
    const r = evaluateQrGate(now, createdAt, 5, limits);
    expect(r.blocked).toBe(false);
    expect(r.reason).toBe("ok");
    expect(r.remainingMs).toBe(38 * HOUR); // 48-10
    expect(r.remainingCount).toBe(25); // 30-5
  });

  test("시간만 초과: reason=time, remainingMs=0", () => {
    const now = createdAt + 49 * HOUR; // 한도(48h) 초과
    const r = evaluateQrGate(now, createdAt, 5, limits);
    expect(r.blocked).toBe(true);
    expect(r.reason).toBe("time");
    expect(r.remainingMs).toBe(0);
    expect(r.remainingCount).toBe(25); // 횟수는 아직 남음
  });

  test("횟수만 초과: reason=count, remainingCount=0", () => {
    const now = createdAt + 10 * HOUR; // 시간은 여유
    const r = evaluateQrGate(now, createdAt, 30, limits); // 정확히 한도
    expect(r.blocked).toBe(true);
    expect(r.reason).toBe("count");
    expect(r.remainingCount).toBe(0);
    expect(r.remainingMs).toBe(38 * HOUR); // 시간은 남음
  });

  test("둘 다 초과: 시간 우선(reason=time)", () => {
    const now = createdAt + 50 * HOUR;
    const r = evaluateQrGate(now, createdAt, 40, limits);
    expect(r.blocked).toBe(true);
    expect(r.reason).toBe("time"); // 시간 우선
    expect(r.remainingMs).toBe(0);
    expect(r.remainingCount).toBe(0);
  });

  test("경계: 경과 시간이 정확히 한도 = 초과(>=)", () => {
    const now = createdAt + 48 * HOUR; // 정확히 48h
    const r = evaluateQrGate(now, createdAt, 0, limits);
    expect(r.blocked).toBe(true);
    expect(r.reason).toBe("time");
    expect(r.remainingMs).toBe(0);
  });

  test("경계: 경과 시간이 한도 직전 = 미초과", () => {
    const now = createdAt + 48 * HOUR - 1; // 1ms 전
    const r = evaluateQrGate(now, createdAt, 0, limits);
    expect(r.blocked).toBe(false);
    expect(r.reason).toBe("ok");
    expect(r.remainingMs).toBe(1);
  });

  test("경계: usedCount가 정확히 한도 = 초과(>=)", () => {
    const now = createdAt + 1 * HOUR;
    expect(evaluateQrGate(now, createdAt, 30, limits).blocked).toBe(true);
    expect(evaluateQrGate(now, createdAt, 29, limits).blocked).toBe(false);
    expect(evaluateQrGate(now, createdAt, 31, limits).blocked).toBe(true);
  });

  test("usedCount 미설정(0) 취급: 초과 아님", () => {
    const now = createdAt + 1 * HOUR;
    const r = evaluateQrGate(now, createdAt, 0, limits);
    expect(r.blocked).toBe(false);
    expect(r.remainingCount).toBe(30);
  });

  test("remaining은 음수로 내려가지 않음(0 클램프)", () => {
    const now = createdAt + 100 * HOUR;
    const r = evaluateQrGate(now, createdAt, 100, limits);
    expect(r.remainingMs).toBe(0);
    expect(r.remainingCount).toBe(0);
  });
});
