import { describe, expect, it } from "vitest";
import { DEFAULT_REGISTER_TO_SERVER } from "@domain/frames/frameSavePolicy";
import { slotAspectToRatio } from "@domain/frames/slotAspect";
import { autoArrange, rescaleSlots, scaleSlots } from "@domain/frames/slotLayout";
import type { Slot } from "@domain/frames/types";
import {
  frameEditorReducer,
  initialFrameEditorState,
  type FrameEditorAction,
  type FrameEditorState,
} from "@screens/frameEditor/frameEditorState";
import { STRINGS } from "@ui/strings";

/**
 * 편집기 reducer — 설계 §8 (03 §11.2 · analysis/14 §4)
 *
 * 여기서 고정하는 것 셋: ① 배율이 **항상 `baseSlots`에서** 계산된다(누적 오차 0)
 * ② 드래그가 `baseSlots` 중심을 함께 갱신한다(배율 조작 시 슬롯이 튀지 않는다)
 * ③ 피커 적용·[선택 편집] 진입이 **자동 배치를 하지 않는다**(WYSIWYG 좌표 보존).
 */

const SIZE = { width: 1200, height: 1600 };
const PNG = new Blob(["png"], { type: "image/png" });

function run(state: FrameEditorState, ...actions: FrameEditorAction[]): FrameEditorState {
  return actions.reduce(frameEditorReducer, state);
}

/** 파일 이미지를 올린 신규 생성 세션(슬롯 4개 자동 배치 상태). */
function loaded(): FrameEditorState {
  return run(initialFrameEditorState(), { type: "imageLoaded", png: PNG, imageSize: SIZE });
}

describe("초기 상태", () => {
  it("신규 생성은 배너 없음·이미지 없음·오버레이 없음이다", () => {
    const state = initialFrameEditorState();
    expect(state.sessionSource).toBe("New");
    expect(state.png).toBeNull();
    expect(state.slots).toEqual([]);
    expect(state.overlay).toBe("none");
    expect(state.registerToServer).toBe(DEFAULT_REGISTER_TO_SERVER);
    expect(state.picker.phase).toBe("loading");
  });

  it("편집 진입은 세션 축·원본 이름을 들고 busy로 시작한다", () => {
    const state = initialFrameEditorState({
      sessionSource: "ForkFromCatalog",
      sourceName: "봄 4컷",
      busy: true,
    });
    expect(state.sessionSource).toBe("ForkFromCatalog");
    expect(state.sourceName).toBe("봄 4컷");
    expect(state.busy).toBe(true);
  });
});

describe("자동 배치 — 슬롯 개수·종횡비", () => {
  it("setSlotCount(6)은 autoArrange와 좌표가 일치한다", () => {
    const state = run(loaded(), { type: "setSlotCount", slotCount: 6 });
    expect(state.slotCount).toBe(6);
    expect(state.baseSlots).toHaveLength(6);
    expect(state.baseSlots).toEqual(
      autoArrange(6, SIZE.width, SIZE.height, slotAspectToRatio(state.aspect)),
    );
    // 배율 100%에서는 slots == scaleSlots(base, 1, …)
    expect(state.slots).toEqual(scaleSlots(state.baseSlots, 1, SIZE.width, SIZE.height));
  });

  it("슬롯 개수는 1~6으로 클램프된다", () => {
    expect(run(loaded(), { type: "setSlotCount", slotCount: 0 }).slotCount).toBe(1);
    expect(run(loaded(), { type: "setSlotCount", slotCount: 9 }).slotCount).toBe(6);
  });

  it("setAspect('Ratio1x1')이 재배치한다", () => {
    const state = run(loaded(), { type: "setAspect", aspect: "Ratio1x1" });
    expect(state.aspect).toBe("Ratio1x1");
    expect(state.baseSlots).toEqual(
      autoArrange(state.slotCount, SIZE.width, SIZE.height, slotAspectToRatio("Ratio1x1")),
    );
  });

  it("이미지가 없으면 배치하지 않는다(0px 프레임 방어)", () => {
    const state = run(initialFrameEditorState(), { type: "setSlotCount", slotCount: 4 });
    expect(state.slots).toEqual([]);
    expect(state.baseSlots).toEqual([]);
  });
});

describe("배율 — 항상 baseSlots에서 계산한다", () => {
  it("70 → 130 → 100 왕복이 원래 값으로 정확히 복귀한다(누적 오차 0)", () => {
    const base = loaded();
    const back = run(
      base,
      { type: "setScale", scalePercent: 70 },
      { type: "setScale", scalePercent: 130 },
      { type: "setScale", scalePercent: 100 },
    );
    expect(back.slots).toEqual(base.slots);
    expect(back.baseSlots).toEqual(base.baseSlots);
  });

  it("범위 밖 값은 10~300으로 클램프된다", () => {
    expect(run(loaded(), { type: "setScale", scalePercent: 5 }).scalePercent).toBe(10);
    expect(run(loaded(), { type: "setScale", scalePercent: 500 }).scalePercent).toBe(300);
    // 경계값 자체는 통과한다.
    expect(run(loaded(), { type: "setScale", scalePercent: 10 }).scalePercent).toBe(10);
    expect(run(loaded(), { type: "setScale", scalePercent: 300 }).scalePercent).toBe(300);
  });

  it("배율을 바꾼 뒤 슬롯 개수를 바꿔도 그 배율이 유지된다", () => {
    const state = run(
      loaded(),
      { type: "setScale", scalePercent: 130 },
      { type: "setSlotCount", slotCount: 2 },
    );
    expect(state.scalePercent).toBe(130);
    expect(state.slots).toEqual(scaleSlots(state.baseSlots, 1.3, SIZE.width, SIZE.height));
  });
});

describe("드래그 — 클램프 + baseSlots 중심 동기화(§8.4)", () => {
  it("경계 밖 좌표가 클램프된다", () => {
    const state = run(loaded(), { type: "dragSlot", index: 0, x: -500, y: 99999 });
    const slot = state.slots[0]!;
    expect(slot.x).toBe(0);
    expect(slot.y).toBe(SIZE.height - slot.height);
  });

  it("드래그 뒤 배율을 바꿔도 드래그한 위치가 유지된다(회귀 — 슬롯이 원래 자리로 튀지 않는다)", () => {
    const dragged = run(loaded(), { type: "dragSlot", index: 0, x: 300, y: 400 });
    const moved = dragged.slots[0]!;
    const after = run(dragged, { type: "setScale", scalePercent: 100 });
    // 배율 100% 재적용은 baseSlots에서 다시 계산하지만 중심이 동기화돼 있어 같은 자리다.
    expect(after.slots[0]).toEqual(moved);
  });

  it("baseSlots는 원본 크기를 유지한 채 중심만 옮긴다", () => {
    const base = loaded();
    const scaled = run(base, { type: "setScale", scalePercent: 70 });
    // 프레임 안쪽으로 옮긴다 — 경계 근처면 큰 `baseSlots`가 클램프에 걸려 중심이 어긋난다
    // (그것은 규격대로의 동작이라 여기서 볼 축이 아니다).
    const dragged = run(scaled, { type: "dragSlot", index: 1, x: 300, y: 400 });
    const b = dragged.baseSlots[1]!;
    expect(b.width).toBe(base.baseSlots[1]!.width);
    expect(b.height).toBe(base.baseSlots[1]!.height);
    const s = dragged.slots[1]!;
    // 중심은 정수 반올림 오차(≤0.5px) 안에서 일치한다 — 그 이상 벌어지면 배율 조작 시 슬롯이 튄다.
    expect(Math.abs(b.x + b.width / 2 - (s.x + s.width / 2))).toBeLessThanOrEqual(0.5);
    expect(Math.abs(b.y + b.height / 2 - (s.y + s.height / 2))).toBeLessThanOrEqual(0.5);
  });

  it("없는 인덱스·이미지 없음은 상태를 바꾸지 않는다", () => {
    const base = loaded();
    expect(run(base, { type: "dragSlot", index: 99, x: 0, y: 0 })).toBe(base);
    const empty = initialFrameEditorState();
    expect(run(empty, { type: "dragSlot", index: 0, x: 0, y: 0 })).toBe(empty);
  });
});

describe("피커 적용 — 세션 축·이름 불변, 슬롯은 좌표계 환산", () => {
  const SOURCE_SLOTS: readonly Slot[] = [
    { index: 0, x: 100, y: 200, width: 400, height: 500 },
    { index: 1, x: 600, y: 200, width: 400, height: 500 },
  ];

  function applied(): FrameEditorState {
    const typed = run(loaded(), { type: "setName", name: "내가 친 이름" });
    return run(typed, {
      type: "pickedApplied",
      png: PNG,
      imageSize: { width: 600, height: 800 },
      sourceName: "봄 4컷",
      sourceSlots: SOURCE_SLOTS,
      sourceWidth: 1200,
    });
  }

  it("name을 바꾸지 않고 캡션을 채우며 세션 축을 유지한다", () => {
    const state = applied();
    expect(state.name).toBe("내가 친 이름");
    expect(state.sessionSource).toBe("New"); // ★ 사본이 아니다(2026-07-30 재정의)
    expect(state.sourceName).toBe("봄 4컷");
    expect(state.pickedSourceNotice).toBe(
      STRINGS.frameEditor.pickedSourceNotice.replace("{n}", "봄 4컷"),
    );
    expect(state.overlay).toBe("none");
  });

  it("autoArrange가 아니라 rescaleSlots를 쓴다(좌표 보존)", () => {
    const state = applied();
    expect(state.baseSlots).toEqual(rescaleSlots(SOURCE_SLOTS, 0.5, 600, 800));
    expect(state.slotCount).toBe(2);
    expect(state.scalePercent).toBe(100);
  });

  it("원본에 슬롯 메타가 없으면 자동 배치로 떨어진다", () => {
    const state = run(loaded(), {
      type: "pickedApplied",
      png: PNG,
      imageSize: SIZE,
      sourceName: "메타 없음",
      sourceSlots: [],
      sourceWidth: 0,
    });
    expect(state.baseSlots).toEqual(
      autoArrange(state.slotCount, SIZE.width, SIZE.height, slotAspectToRatio(state.aspect)),
    );
  });

  it("이후 파일 이미지를 직접 넣으면 캡션을 비운다(사실과 어긋나므로)", () => {
    const state = run(applied(), { type: "imageLoaded", png: PNG, imageSize: SIZE });
    expect(state.pickedSourceNotice).toBe("");
    expect(state.sourceName).toBe("");
  });

  it("fork 세션의 sourceName은 파일 교체로 지워지지 않는다(④ 가드 근거)", () => {
    const fork = initialFrameEditorState({ sessionSource: "ForkFromCatalog", sourceName: "봄 4컷" });
    const state = run(fork, { type: "imageLoaded", png: PNG, imageSize: SIZE });
    expect(state.sourceName).toBe("봄 4컷");
  });
});

describe("[선택 편집] 진입 — 자동 배치를 하지 않는다(§9.3)", () => {
  const ORIGINAL: readonly Slot[] = [
    { index: 0, x: 37, y: 41, width: 411, height: 547 },
    { index: 1, x: 700, y: 41, width: 411, height: 547 },
    { index: 2, x: 37, y: 900, width: 411, height: 547 },
  ];

  it("원본 슬롯을 그대로 쓰고 개수·배율을 맞춘다", () => {
    const state = run(
      initialFrameEditorState({ sessionSource: "EditOwnLocal", sourceName: "내 프레임", busy: true }),
      { type: "editSessionReady", name: "내 프레임", png: PNG, imageSize: SIZE, slots: ORIGINAL },
    );
    expect(state.slots).toEqual(ORIGINAL);
    expect(state.baseSlots).toEqual(ORIGINAL);
    expect(state.slotCount).toBe(3);
    expect(state.scalePercent).toBe(100);
    expect(state.busy).toBe(false);
    expect(state.name).toBe("내 프레임");
  });

  it("진입 실패는 폼만 열고 이미지를 비워 둔다(저장은 ③에서 막힌다)", () => {
    const state = run(
      initialFrameEditorState({ sessionSource: "EditOwnLocal", busy: true }),
      { type: "entryFailed", status: STRINGS.frameEditor.editImageMissing },
    );
    expect(state.busy).toBe(false);
    expect(state.png).toBeNull();
    expect(state.status).toBe(STRINGS.frameEditor.editImageMissing);
  });
});

describe("오버레이 — 상호배타 + 체크박스 리셋", () => {
  it("서버 등록 오버레이를 열 때마다 체크가 기본값으로 리셋된다", () => {
    const state = run(
      loaded(),
      { type: "openOverlay", overlay: "serverRegister" },
      { type: "setRegisterToServer", registerToServer: false },
      { type: "closeOverlay" },
      { type: "openOverlay", overlay: "serverRegister" },
    );
    expect(state.overlay).toBe("serverRegister");
    expect(state.registerToServer).toBe(true);
  });

  it("[취소](closeOverlay)는 오버레이만 닫고 체크를 리셋한다", () => {
    const state = run(
      loaded(),
      { type: "openOverlay", overlay: "serverRegister" },
      { type: "setRegisterToServer", registerToServer: false },
      { type: "closeOverlay" },
    );
    expect(state.overlay).toBe("none");
    expect(state.registerToServer).toBe(true);
    // 저장·전환·저장소 무변경 — 편집 상태는 그대로다.
    expect(state.slots).toEqual(loaded().slots);
  });

  it("피커를 열면 이전 목록이 초기화된다(상호배타 단일 필드)", () => {
    const state = run(
      loaded(),
      { type: "openOverlay", overlay: "picker" },
      { type: "pickerPatch", patch: { phase: "ready", frames: [], selectedId: "a" } },
      { type: "closeOverlay" },
      { type: "openOverlay", overlay: "picker" },
    );
    expect(state.overlay).toBe("picker");
    expect(state.picker.phase).toBe("loading");
    expect(state.picker.selectedId).toBeNull();
  });

  it("pickerPatch는 정의된 키만 덮고 selectedId의 null을 존중한다", () => {
    const state = run(
      loaded(),
      { type: "pickerPatch", patch: { phase: "ready", notice: "n", selectedId: "a" } },
      { type: "pickerPatch", patch: { selectedId: null } },
    );
    expect(state.picker.phase).toBe("ready");
    expect(state.picker.notice).toBe("n");
    expect(state.picker.selectedId).toBeNull();
  });
});
