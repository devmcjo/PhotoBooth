import { describe, expect, it } from "vitest";
import { APP_STATES, isAppState, type AppState } from "@domain/navigation/appState";
import {
  canTransition,
  isOverlayScreen,
  isSessionActive,
  isTopBarVisible,
} from "@domain/navigation/stateMachine";
import {
  createIdleCountdown,
  isExpired,
  reset,
  tick,
} from "@domain/navigation/idleCountdown";
import {
  isFullscreenButtonVisible,
  type FullscreenButtonInput,
} from "@domain/navigation/fullscreenButtonPolicy";

describe("appState", () => {
  it("13개 상태를 갖는다", () => {
    expect(APP_STATES).toHaveLength(13);
  });

  it("문자열을 판별한다", () => {
    expect(isAppState("Home")).toBe(true);
    expect(isAppState("home")).toBe(false);
    expect(isAppState("Nope")).toBe(false);
  });
});

describe("stateMachine — 정상 흐름", () => {
  const happyPath: [AppState, AppState][] = [
    ["Home", "FrameSelect"],
    ["FrameSelect", "Guide"],
    ["Guide", "Capture"],
    ["Capture", "CutSelect"],
    ["CutSelect", "Result"],
    ["Result", "Qr"],
    ["Qr", "Done"],
    ["Done", "Home"],
  ];

  it.each(happyPath)("%s → %s 는 합법이다", (from, to) => {
    expect(canTransition(from, to)).toBe(true);
  });

  it("Result → Done(QR 건너뛰기)도 합법이다 — 게스트 경로", () => {
    expect(canTransition("Result", "Done")).toBe(true);
  });

  it("CutSelect → Guide(전체 재촬영)가 합법이다", () => {
    expect(canTransition("CutSelect", "Guide")).toBe(true);
  });
});

describe("stateMachine — 불법 전이", () => {
  const illegal: [AppState, AppState][] = [
    ["Home", "Capture"],
    ["Home", "Result"],
    ["Guide", "CutSelect"],
    ["Capture", "Result"],
    ["FrameSelect", "Capture"],
    ["Done", "Qr"],
    ["Qr", "Result"],
    ["UserMgmt", "FrameSelect"],
  ];

  it.each(illegal)("%s → %s 는 거부된다", (from, to) => {
    expect(canTransition(from, to)).toBe(false);
  });

  it("오버레이가 아닌 자기 자신 전이는 거부된다", () => {
    expect(canTransition("Capture", "Capture")).toBe(false);
    expect(canTransition("Result", "Result")).toBe(false);
  });
});

describe("stateMachine — 오버레이 특례", () => {
  it("어느 상태에서든 Home·Settings·Login·Account로 갈 수 있다", () => {
    for (const from of APP_STATES) {
      expect(canTransition(from, "Home"), `${from} → Home`).toBe(true);
      expect(canTransition(from, "Settings"), `${from} → Settings`).toBe(true);
      expect(canTransition(from, "Login"), `${from} → Login`).toBe(true);
      expect(canTransition(from, "Account"), `${from} → Account`).toBe(true);
    }
  });

  it("오버레이 목적지는 자기 자신 전이도 허용한다(Home→Home 복귀)", () => {
    expect(canTransition("Home", "Home")).toBe(true);
    expect(canTransition("Settings", "Settings")).toBe(true);
  });
});

describe("stateMachine — 유휴 감시 대상", () => {
  it("촬영 흐름 6개 화면만 감시 대상이다", () => {
    const active = APP_STATES.filter(isSessionActive);
    expect(active).toEqual(["FrameSelect", "Guide", "Capture", "CutSelect", "Result", "Qr"]);
  });

  it("Settings·Login·FrameEditor는 감시 대상이 아니다", () => {
    expect(isSessionActive("Settings")).toBe(false);
    expect(isSessionActive("Login")).toBe(false);
    expect(isSessionActive("FrameEditor")).toBe(false); // 능동 작업(it4 B5)
    expect(isSessionActive("Home")).toBe(false);
  });
});

describe("stateMachine — 오버레이 화면·상단바", () => {
  it("오버레이 화면 4개가 복귀 지점 저장에서 제외된다", () => {
    expect(APP_STATES.filter(isOverlayScreen)).toEqual([
      "Login",
      "Settings",
      "UserMgmt",
      "Account",
    ]);
  });

  it("상단바는 Capture·Qr에서만 숨는다", () => {
    expect(APP_STATES.filter((s) => !isTopBarVisible(s))).toEqual(["Capture", "Qr"]);
  });
});

describe("idleCountdown", () => {
  it("시작값을 최소 1로 보정한다", () => {
    expect(createIdleCountdown(10).remaining).toBe(10);
    expect(createIdleCountdown(0).remaining).toBe(1);
    expect(createIdleCountdown(-5).remaining).toBe(1);
  });

  it("tick마다 1씩 줄고 0에서 만료 전이를 1회만 보고한다", () => {
    let state = createIdleCountdown(3);
    let result = tick(state);
    expect(result.state.remaining).toBe(2);
    expect(result.justExpired).toBe(false);

    result = tick(result.state);
    expect(result.state.remaining).toBe(1);
    expect(result.justExpired).toBe(false);

    result = tick(result.state);
    expect(result.state.remaining).toBe(0);
    expect(result.justExpired).toBe(true);
    expect(isExpired(result.state)).toBe(true);

    // 이미 0이면 중복 완료를 보고하지 않는다
    const after = tick(result.state);
    expect(after.justExpired).toBe(false);
    expect(after.state.remaining).toBe(0);

    state = reset(after.state);
    expect(state.remaining).toBe(3);
    expect(isExpired(state)).toBe(false);
  });

  it("불변이다 — 원 상태를 바꾸지 않는다", () => {
    const state = createIdleCountdown(5);
    tick(state);
    expect(state.remaining).toBe(5);
  });
});

describe("fullscreenButtonPolicy — 상단바 [전체화면] 버튼 노출(02 §7)", () => {
  const VISIBLE: FullscreenButtonInput = {
    supported: true,
    isFullscreen: false,
    fullscreenLost: false,
    standalone: false,
  };

  it("네 조건이 전부 맞을 때만 보인다 — 16조합 중 1개", () => {
    const flags: (keyof FullscreenButtonInput)[] = [
      "supported",
      "isFullscreen",
      "fullscreenLost",
      "standalone",
    ];
    let visibleCount = 0;
    for (let mask = 0; mask < 16; mask += 1) {
      const input = Object.fromEntries(
        flags.map((key, index) => [key, (mask & (1 << index)) !== 0]),
      ) as unknown as FullscreenButtonInput;
      if (isFullscreenButtonVisible(input)) visibleCount += 1;
    }
    expect(visibleCount).toBe(1);
    expect(isFullscreenButtonVisible(VISIBLE)).toBe(true);
  });

  it("미지원이면 숨긴다 — 죽은 버튼 금지(iOS Safari)", () => {
    expect(isFullscreenButtonVisible({ ...VISIBLE, supported: false })).toBe(false);
  });

  it("이미 전체화면이면 숨긴다", () => {
    expect(isFullscreenButtonVisible({ ...VISIBLE, isFullscreen: true })).toBe(false);
  });

  it("이탈 배너가 떠 있으면 숨긴다 — 배너의 [다시 전체화면으로]와 중복되지 않게", () => {
    expect(isFullscreenButtonVisible({ ...VISIBLE, fullscreenLost: true })).toBe(false);
  });

  it("PWA standalone이면 숨긴다 — 이미 몰입 상태다", () => {
    expect(isFullscreenButtonVisible({ ...VISIBLE, standalone: true })).toBe(false);
  });
});
