import { describe, expect, it } from "vitest";
import {
  addCut,
  beginFullRetake,
  beginSession,
  canFullRetake,
  createEmptySession,
  discardSession,
  getSelectedCuts,
  isCaptureComplete,
  isSelectionComplete,
  resetForRetake,
  slotCount,
  toggleSelection,
} from "@domain/capture/captureSession";
import {
  createPreviewReadiness,
  DEFAULT_MIN_ELAPSED_MS,
  DEFAULT_REQUIRED_FRAMES,
  onFrame,
} from "@domain/capture/previewReadiness";
import { sourceCropForSlot } from "@domain/capture/slotPlacement";
import { expectedOutputSeconds } from "@domain/capture/timelapseSpeed";
import { autoArrange } from "@domain/frames/slotLayout";
import type { FrameTemplate } from "@domain/frames/types";

function frameWithSlots(slots: number): FrameTemplate {
  return {
    id: "fallback",
    userId: null,
    isDefault: true,
    name: "테스트 프레임",
    imageUrl: "",
    imageSize: { width: 1200, height: 1600 },
    slots: autoArrange(slots, 1200, 1600, 3 / 4),
    createdAt: "2026-07-30T00:00:00.000Z",
  };
}

describe("previewReadiness — Ready 게이트", () => {
  it("기본값은 누적 8프레임 + 500ms다", () => {
    expect(DEFAULT_REQUIRED_FRAMES).toBe(8);
    expect(DEFAULT_MIN_ELAPSED_MS).toBe(500);
  });

  it("세 조건(프레임 수·경과·fps>0)을 모두 채워야 Ready다", () => {
    let state = createPreviewReadiness(3, 100);

    // 프레임은 찼지만 경과가 부족
    for (let i = 0; i < 3; i++) {
      const result = onFrame(state, 10, 30);
      state = result.state;
      expect(result.becameReady).toBe(false);
    }
    expect(state.isReady).toBe(false);

    // 경과는 찼지만 fps가 0
    let result = onFrame(state, 500, 0);
    state = result.state;
    expect(result.becameReady).toBe(false);

    // 세 조건 충족
    result = onFrame(state, 500, 30);
    expect(result.becameReady).toBe(true);
    expect(result.state.isReady).toBe(true);
  });

  it("Ready 이후에는 becameReady를 다시 보고하지 않는다(1회만)", () => {
    let state = createPreviewReadiness(1, 0);
    let result = onFrame(state, 0, 30);
    expect(result.becameReady).toBe(true);
    state = result.state;

    result = onFrame(state, 100, 30);
    expect(result.becameReady).toBe(false);
    expect(result.state.frameCount).toBe(1); // Ready 후에는 카운트도 증가시키지 않는다
  });

  it("생성 인자를 하한 보정한다", () => {
    const state = createPreviewReadiness(0, -100);
    expect(state.requiredFrames).toBe(1);
    expect(state.minElapsedMs).toBe(0);
  });
});

describe("captureSession — 세션 수명", () => {
  it("빈 세션은 프레임·컷이 없다", () => {
    const state = createEmptySession();
    expect(state.frame).toBeNull();
    expect(state.cutCount).toBe(0);
    expect(slotCount(state)).toBe(0);
    expect(isSelectionComplete(state)).toBe(false);
  });

  it("beginSession이 프레임을 고정하고 컷 수를 해석한다(고정 6컷)", () => {
    const state = beginSession(frameWithSlots(4), 6);
    expect(state.cutCount).toBe(6);
    expect(state.isAutoCutCount).toBe(false);
    expect(slotCount(state)).toBe(4);
  });

  it("자동 컷 수(0)를 해석하고 세션이 그 사실을 기억한다 — WD19", () => {
    const state = beginSession(frameWithSlots(5), 0);
    expect(state.cutCount).toBe(7); // max(6, 5+2)
    expect(state.isAutoCutCount).toBe(true);
  });

  it("고정 컷 수가 슬롯보다 작으면 슬롯 수로 올린다(컷 ≥ 슬롯 불변)", () => {
    const state = beginSession(frameWithSlots(6), 6);
    expect(state.cutCount).toBe(6);
  });

  it("cutCount를 넘는 컷은 추가되지 않는다", () => {
    let state = beginSession<string>(frameWithSlots(4), 6);
    for (let i = 0; i < 10; i++) state = addCut(state, `cut${i}`);
    expect(state.cuts).toHaveLength(6);
    expect(isCaptureComplete(state)).toBe(true);
  });
});

describe("captureSession — 컷 선택(M12)", () => {
  function sessionWithCuts(slots: number, cuts: number) {
    let state = beginSession<string>(frameWithSlots(slots), 8);
    for (let i = 0; i < cuts; i++) state = addCut(state, `cut${i}`);
    return state;
  }

  it("슬롯 수만큼만 선택할 수 있다", () => {
    let state = sessionWithCuts(4, 8);
    for (const i of [0, 1, 2, 3]) state = toggleSelection(state, i);
    expect(state.selection).toEqual([0, 1, 2, 3]);
    expect(isSelectionComplete(state)).toBe(true);

    // 5번째 선택은 거부
    state = toggleSelection(state, 4);
    expect(state.selection).toEqual([0, 1, 2, 3]);
  });

  it("선택 순서가 곧 슬롯 순서다", () => {
    let state = sessionWithCuts(3, 8);
    for (const i of [5, 1, 7]) state = toggleSelection(state, i);
    expect(state.selection).toEqual([5, 1, 7]);
    expect(getSelectedCuts(state)).toEqual(["cut5", "cut1", "cut7"]);
  });

  it("같은 컷을 다시 누르면 해제되고 나머지 순서가 유지된다", () => {
    let state = sessionWithCuts(3, 8);
    for (const i of [2, 4, 6]) state = toggleSelection(state, i);
    state = toggleSelection(state, 4);
    expect(state.selection).toEqual([2, 6]);
    expect(isSelectionComplete(state)).toBe(false);
  });

  it("범위 밖 인덱스는 무시한다", () => {
    const state = sessionWithCuts(4, 3);
    expect(toggleSelection(state, -1)).toBe(state);
    expect(toggleSelection(state, 3)).toBe(state);
    expect(toggleSelection(state, 99)).toBe(state);
  });

  it("슬롯이 0이면 선택 완료가 되지 않는다", () => {
    const state = createEmptySession();
    expect(isSelectionComplete(state)).toBe(false);
  });
});

describe("captureSession — 재촬영·폐기", () => {
  it("전체 재촬영은 컷·선택을 버리고 카운터를 올린다(프레임·컷 수 유지)", () => {
    let state = beginSession<string>(frameWithSlots(4), 6);
    state = addCut(state, "a");
    state = toggleSelection(state, 0);
    state = beginFullRetake(state);

    expect(state.cuts).toHaveLength(0);
    expect(state.selection).toHaveLength(0);
    expect(state.fullRetakeCount).toBe(1);
    expect(state.cutCount).toBe(6);
    expect(state.frame).not.toBeNull();
  });

  it("재촬영 상한을 넘으면 canFullRetake가 false다", () => {
    let state = beginSession(frameWithSlots(4), 6);
    expect(canFullRetake(state, 1)).toBe(true);
    state = beginFullRetake(state);
    expect(canFullRetake(state, 1)).toBe(false);
    expect(canFullRetake(state, 2)).toBe(true);
  });

  it("resetForRetake는 카운터를 올리지 않는다(레거시 경로)", () => {
    let state = beginSession<string>(frameWithSlots(4), 6);
    state = addCut(state, "a");
    state = resetForRetake(state);
    expect(state.cuts).toHaveLength(0);
    expect(state.fullRetakeCount).toBe(0);
  });

  it("폐기하면 프레임까지 사라진다(cutCount 0은 '세션 없음')", () => {
    const state = discardSession();
    expect(state.frame).toBeNull();
    expect(state.cutCount).toBe(0);
    expect(state.isAutoCutCount).toBe(false);
  });
});

describe("slotPlacement — 소스 크롭", () => {
  it("슬롯 종횡비로 중앙 크롭한다", () => {
    expect(sourceCropForSlot(1920, 1080, 300, 400)).toEqual({
      x: 555,
      y: 0,
      width: 810,
      height: 1080,
    });
  });

  it("이미 슬롯 비율이면 전체를 쓴다", () => {
    expect(sourceCropForSlot(600, 800, 300, 400)).toEqual({
      x: 0,
      y: 0,
      width: 600,
      height: 800,
    });
  });

  it("잘못된 크기는 방어적으로 전체를 돌려준다(예외 없음)", () => {
    expect(sourceCropForSlot(0, 0, 100, 100)).toEqual({ x: 0, y: 0, width: 0, height: 0 });
    expect(sourceCropForSlot(-5, 100, 100, 100)).toEqual({ x: 0, y: 0, width: 0, height: 100 });
    expect(sourceCropForSlot(100, 100, 0, 100)).toEqual({ x: 0, y: 0, width: 100, height: 100 });
  });
});

describe("timelapseSpeed — 예상 결과 길이", () => {
  it("배속으로 나눈 길이를 돌려준다", () => {
    expect(expectedOutputSeconds(50, 4)).toBe(12.5);
    expect(expectedOutputSeconds(10, 1)).toBe(10);
  });

  it("배속이 0 이하면 원 길이를 돌려준다(0 나눗셈 방어)", () => {
    expect(expectedOutputSeconds(10, 0)).toBe(10);
    expect(expectedOutputSeconds(10, -2)).toBe(10);
  });
});
