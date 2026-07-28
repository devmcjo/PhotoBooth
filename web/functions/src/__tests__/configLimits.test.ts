/**
 * services/config(loadTempUserLimits/setTempUserLimits) + validation(qrHours/qrCount) 테스트(설계 §4.3·§5.4, Step 5).
 *
 * 문서 부재 시 기본값 폴백, 부분 갱신 병합, 잘못 저장된 값의 방어적 폴백, 범위 검증.
 */
import { FakeFirestore } from "./helpers/fakeFirestore";

const fake = new FakeFirestore();

jest.mock("../firebase", () => ({
  db: () => fake,
  storage: () => {
    throw new Error("storage()는 이 테스트에서 사용하지 않습니다.");
  },
}));

import { loadTempUserLimits, setTempUserLimits } from "../services/config";
import { validateQrCount, validateQrHours } from "../domain/validation";

beforeEach(() => {
  (fake as unknown as { store: Record<string, unknown> }).store = {};
});

describe("services/config — TempUser 전역 한도", () => {
  test("문서 부재 → 기본값 48h/30회 폴백", async () => {
    expect(await loadTempUserLimits()).toEqual({ qrHours: 48, qrCount: 30 });
  });

  test("저장된 문서 값 로드", async () => {
    fake.seed("config", "tempUserLimits", { qrHours: 24, qrCount: 10 });
    expect(await loadTempUserLimits()).toEqual({ qrHours: 24, qrCount: 10 });
  });

  test("잘못된 저장값(비정수/범위 밖) → 방어적 기본값 폴백(과금 전면 개방/차단 방지)", async () => {
    fake.seed("config", "tempUserLimits", { qrHours: -1, qrCount: 999999999 });
    // qrHours 범위 밖 → 48, qrCount 범위 밖 → 30.
    expect(await loadTempUserLimits()).toEqual({ qrHours: 48, qrCount: 30 });
  });

  test("일부 필드만 결손 → 결손 필드만 기본값", async () => {
    fake.seed("config", "tempUserLimits", { qrHours: 12 }); // qrCount 결손
    expect(await loadTempUserLimits()).toEqual({ qrHours: 12, qrCount: 30 });
  });

  test("setTempUserLimits: 부분 갱신은 기존값 병합(qrHours만 변경)", async () => {
    fake.seed("config", "tempUserLimits", { qrHours: 48, qrCount: 30 });
    const res = await setTempUserLimits({ qrHours: 72 });
    expect(res).toEqual({ qrHours: 72, qrCount: 30 });
    expect(fake.peek("config", "tempUserLimits")).toEqual({ qrHours: 72, qrCount: 30 });
  });

  test("setTempUserLimits: 문서 부재 상태에서 qrCount만 갱신 → 기본 qrHours 유지", async () => {
    const res = await setTempUserLimits({ qrCount: 5 });
    expect(res).toEqual({ qrHours: 48, qrCount: 5 });
    expect(fake.peek("config", "tempUserLimits")).toEqual({ qrHours: 48, qrCount: 5 });
  });

  test("setTempUserLimits: 둘 다 갱신", async () => {
    const res = await setTempUserLimits({ qrHours: 100, qrCount: 200 });
    expect(res).toEqual({ qrHours: 100, qrCount: 200 });
  });
});

describe("validation — qrHours/qrCount 범위", () => {
  test("qrHours: 정수 1~8760", () => {
    expect(validateQrHours(1)).toEqual({ ok: true, value: 1 });
    expect(validateQrHours(8760)).toEqual({ ok: true, value: 8760 });
    expect(validateQrHours(0).ok).toBe(false);
    expect(validateQrHours(8761).ok).toBe(false);
    expect(validateQrHours(1.5).ok).toBe(false);
    expect(validateQrHours("48").ok).toBe(false);
    expect(validateQrHours(null).ok).toBe(false);
  });

  test("qrCount: 정수 1~100000", () => {
    expect(validateQrCount(1)).toEqual({ ok: true, value: 1 });
    expect(validateQrCount(100000)).toEqual({ ok: true, value: 100000 });
    expect(validateQrCount(0).ok).toBe(false);
    expect(validateQrCount(100001).ok).toBe(false);
    expect(validateQrCount(2.5).ok).toBe(false);
    expect(validateQrCount(undefined).ok).toBe(false);
  });
});
