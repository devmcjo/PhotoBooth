import { describe, expect, it } from "vitest";
import {
  applyPinFailure,
  buildPinLockRecord,
  classifyPinSet,
  classifyPinVerify,
  formatPinLockRemaining,
  initialPinAttemptState,
  isPinFormatValid,
  isPinInputBlocked,
  MAX_PIN_FAILS,
  parsePinLockRecord,
  pinInputsMatch,
  pinLockRemainingMs,
  PIN_COOLDOWN_MS,
  PIN_LOCK_MS,
} from "@domain/auth/pinGatePolicy";

/**
 * PIN 게이트 정책(순수) — 07 §6.2 · 03 §15.3 · WD16
 *
 * 여기서 고정하는 것 중 **깨져도 초록으로 남을 수 있는** 것들:
 *   - 최초 설정에서의 401을 "불일치"로 세면 5회 만에 기기가 잠긴다(A5).
 *   - 잠금 레코드의 `until`이 미래로 크게 어긋나면 키오스크가 영구 잠긴다(A3).
 *   - 쿨다운 경계를 `<=`로 쓰면 1.5초가 지나도 키가 살아나지 않는다.
 */

const NOW = 1_700_000_000_000;

describe("PIN 형식", () => {
  it("정확히 4자리 ASCII 숫자만 허용한다", () => {
    expect(isPinFormatValid("1234")).toBe(true);
    expect(isPinFormatValid("0000")).toBe(true);
  });

  it.each(["", "1", "123", "12345", "12a4", "12 4", "12.4", " 1234", "1234 "])(
    "%s — 거부한다",
    (value) => {
      expect(isPinFormatValid(value)).toBe(false);
    },
  );

  it("전각 숫자를 거부한다(`u` 플래그를 붙이면 통과해 서버 400이 난다)", () => {
    expect(isPinFormatValid("１２３４")).toBe(false);
  });

  it("최초 설정 2단계는 형식이 맞고 값이 같을 때만 일치다", () => {
    expect(pinInputsMatch("1234", "1234")).toBe(true);
    expect(pinInputsMatch("1234", "1235")).toBe(false);
    // 빈 값끼리 "일치"로 통과하면 빈 PIN이 서버로 간다.
    expect(pinInputsMatch("", "")).toBe(false);
    expect(pinInputsMatch("12", "12")).toBe(false);
  });
});

describe("classifyPinVerify — POST /accounts/me/pin/verify", () => {
  it("200은 granted다", () => {
    expect(classifyPinVerify({ kind: "ok" })).toBe("granted");
  });

  it("401은 불일치다(만료가 아니다)", () => {
    expect(classifyPinVerify({ kind: "status", status: 401 })).toBe("mismatch");
  });

  it("409는 PIN 미설정이다 — 오류가 아니라 최초 설정 플로우다", () => {
    expect(classifyPinVerify({ kind: "status", status: 409 })).toBe("unset");
  });

  it.each([400, 403, 404, 500, 503])("%s는 unavailable이다(카운트 미가산)", (status) => {
    expect(classifyPinVerify({ kind: "status", status })).toBe("unavailable");
  });

  it("네트워크 실패는 unavailable이다", () => {
    expect(classifyPinVerify({ kind: "network" })).toBe("unavailable");
  });
});

describe("classifyPinSet — PUT /accounts/me/pin (401이 sentCurrentPin 두 축이다)", () => {
  it("204/200은 granted다", () => {
    expect(classifyPinSet({ kind: "ok" }, false)).toBe("granted");
    expect(classifyPinSet({ kind: "ok" }, true)).toBe("granted");
  });

  it("currentPin을 보냈을 때의 401은 불일치다", () => {
    expect(classifyPinSet({ kind: "status", status: 401 }, true)).toBe("mismatch");
  });

  it("currentPin을 보내지 않았을 때의 401은 alreadySet이다(서버에 이미 PIN이 있다)", () => {
    // 이것을 "불일치"로 세면 hasPin=false를 믿는 클라가 5회 만에 기기를 잠근다(A5).
    expect(classifyPinSet({ kind: "status", status: 401 }, false)).toBe("alreadySet");
  });

  it("400은 invalid(형식 — 클라가 먼저 막으므로 계약 불일치 신호)다", () => {
    expect(classifyPinSet({ kind: "status", status: 400 }, false)).toBe("invalid");
  });

  it.each([403, 409, 500])("%s는 unavailable이다", (status) => {
    expect(classifyPinSet({ kind: "status", status }, true)).toBe("unavailable");
  });

  it("네트워크 실패는 unavailable이다", () => {
    expect(classifyPinSet({ kind: "network" }, true)).toBe("unavailable");
  });
});

describe("실패 상태머신", () => {
  it("초기 상태는 쿨다운·잠금이 -Infinity다(0이면 첫 입력이 막힌다 — 함정 #4)", () => {
    const state = initialPinAttemptState();
    expect(state.fails).toBe(0);
    expect(state.cooldownUntilMs).toBe(Number.NEGATIVE_INFINITY);
    expect(state.lockedUntilMs).toBe(Number.NEGATIVE_INFINITY);
    expect(isPinInputBlocked(state, 0)).toBe(false);
    expect(isPinInputBlocked(state, NOW)).toBe(false);
  });

  it("1~4회째는 쿨다운만 걸리고 exhausted가 아니다", () => {
    let state = initialPinAttemptState();
    for (let i = 1; i < MAX_PIN_FAILS; i++) {
      const applied = applyPinFailure(state, NOW);
      state = applied.state;
      expect(applied.exhausted, `${i}회째`).toBe(false);
      expect(state.fails).toBe(i);
      expect(state.cooldownUntilMs).toBe(NOW + PIN_COOLDOWN_MS);
      expect(state.lockedUntilMs).toBe(Number.NEGATIVE_INFINITY);
    }
  });

  it("5회째에만 exhausted이고 잠금 시각이 설정된다", () => {
    let state = initialPinAttemptState();
    let exhausted = false;
    for (let i = 0; i < MAX_PIN_FAILS; i++) {
      const applied = applyPinFailure(state, NOW);
      state = applied.state;
      exhausted = applied.exhausted;
    }
    expect(exhausted).toBe(true);
    expect(state.fails).toBe(MAX_PIN_FAILS);
    expect(state.lockedUntilMs).toBe(NOW + PIN_LOCK_MS);
    expect(isPinInputBlocked(state, NOW)).toBe(true);
  });

  it("쿨다운 경계는 해제 쪽이다(now === cooldownUntil이면 입력 가능)", () => {
    const { state } = applyPinFailure(initialPinAttemptState(), NOW);
    expect(isPinInputBlocked(state, NOW + PIN_COOLDOWN_MS - 1)).toBe(true);
    expect(isPinInputBlocked(state, NOW + PIN_COOLDOWN_MS)).toBe(false);
  });

  it("잠금 남은 시간은 음수가 되지 않고 잠금 없음은 0이다", () => {
    expect(pinLockRemainingMs(NOW + 1000, NOW)).toBe(1000);
    expect(pinLockRemainingMs(NOW - 1000, NOW)).toBe(0);
    expect(pinLockRemainingMs(Number.NEGATIVE_INFINITY, NOW)).toBe(0);
  });
});

describe("formatPinLockRemaining", () => {
  it.each([
    [0, "0초"],
    [1, "1초"],
    [999, "1초"],
    [45_000, "45초"],
    // 올림이 60초를 채우면 분으로 넘어간다("60초"라고 쓰지 않는다).
    [59_999, "1분"],
    [60_000, "1분"],
    [60_001, "1분 1초"],
    [272_000, "4분 32초"],
    [300_000, "5분"],
  ])("%s ms → %s", (ms, expected) => {
    expect(formatPinLockRemaining(ms)).toBe(expected);
  });

  it("음수도 0초로 접는다", () => {
    expect(formatPinLockRemaining(-5000)).toBe("0초");
  });
});

describe("잠금 레코드 파싱", () => {
  it("정상 레코드를 그대로 돌려준다", () => {
    const record = buildPinLockRecord(NOW, MAX_PIN_FAILS);
    expect(record).toEqual({ until: NOW + PIN_LOCK_MS, fails: MAX_PIN_FAILS });
    expect(parsePinLockRecord(record, NOW)).toEqual(record);
  });

  it("만료된 레코드는 null이다(경계 포함)", () => {
    expect(parsePinLockRecord({ until: NOW, fails: 5 }, NOW)).toBeNull();
    expect(parsePinLockRecord({ until: NOW - 1, fails: 5 }, NOW)).toBeNull();
    expect(parsePinLockRecord({ until: NOW + 1, fails: 5 }, NOW)).not.toBeNull();
  });

  it.each([
    ["null", null],
    ["숫자", 42],
    ["문자열", "locked"],
    ["배열", [{ until: 1 }]],
    ["빈 객체", {}],
    ["until이 문자열", { until: "9999999999999", fails: 5 }],
    ["until이 NaN", { until: Number.NaN, fails: 5 }],
    ["until이 Infinity", { until: Number.POSITIVE_INFINITY, fails: 5 }],
  ])("손상 입력(%s)은 null이다", (_label, raw) => {
    expect(parsePinLockRecord(raw, NOW)).toBeNull();
  });

  it("until이 미래로 크게 어긋나면 상한으로 clamp한다(A3 — 시계 왜곡 방어)", () => {
    // 시스템 시각을 과거로 되돌린 기기. clamp가 없으면 영구 잠금이다.
    const parsed = parsePinLockRecord({ until: NOW + 10 * PIN_LOCK_MS, fails: 5 }, NOW);
    expect(parsed).toEqual({ until: NOW + PIN_LOCK_MS, fails: 5 });
  });

  it("fails가 없거나 손상되면 상한으로 본다(잠금이 걸려 있다는 사실은 until이 말한다)", () => {
    expect(parsePinLockRecord({ until: NOW + 1000 }, NOW)?.fails).toBe(MAX_PIN_FAILS);
    expect(parsePinLockRecord({ until: NOW + 1000, fails: "5" }, NOW)?.fails).toBe(MAX_PIN_FAILS);
    expect(parsePinLockRecord({ until: NOW + 1000, fails: 99 }, NOW)?.fails).toBe(MAX_PIN_FAILS);
    expect(parsePinLockRecord({ until: NOW + 1000, fails: -3 }, NOW)?.fails).toBe(0);
  });
});
