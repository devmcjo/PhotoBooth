import { describe, expect, it } from "vitest";
import {
  cameraFailure,
  cameraFailureMessageKey,
  classifyCameraFailure,
  classifyCameraFailureFrom,
  formatCameraFailureCode,
  isCameraRetryable,
  sanitizeFailureDetail,
  type CameraFailureReason,
} from "@domain/capture/cameraFailure";
import { STRINGS } from "@ui/strings";
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

// ─────────── 카메라 실패 사유 분류 (03 §6.3 · 12 C5 — 2026-08-01 신설) ───────────

describe("classifyCameraFailure", () => {
  it("권한 거부 계열", () => {
    expect(classifyCameraFailure("NotAllowedError", true)).toBe("permissionDenied");
    expect(classifyCameraFailure("SecurityError", true)).toBe("permissionDenied");
    expect(classifyCameraFailure("PermissionDeniedError", true)).toBe("permissionDenied");
  });

  it("장치 부재 계열", () => {
    expect(classifyCameraFailure("NotFoundError", true)).toBe("noDevice");
    expect(classifyCameraFailure("OverconstrainedError", true)).toBe("noDevice");
    expect(classifyCameraFailure("DevicesNotFoundError", true)).toBe("noDevice");
  });

  it("점유 계열", () => {
    expect(classifyCameraFailure("NotReadableError", true)).toBe("inUse");
    expect(classifyCameraFailure("TrackStartError", true)).toBe("inUse");
  });

  it("알 수 없는 이름은 unknown이다", () => {
    expect(classifyCameraFailure("", true)).toBe("unknown");
    // ⚠️ `AbortError`는 **의도적으로 unknown이다**(2026-08-07 설계 리뷰). 규격상 잔여 범주라
    //    "다른 앱 점유"로 단정할 근거가 약하다. 실기기 관측 전에는 매핑하지 않는다 —
    //    사유 대신 `CameraFailure.detail`이 `unknown/AbortError`로 이름을 실어 나른다.
    expect(classifyCameraFailure("AbortError", true)).toBe("unknown");
  });

  it("보안 컨텍스트인데 TypeError면 브라우저 미지원이다(인앱브라우저·구형 WebView)", () => {
    // `navigator.mediaDevices`가 없다는 뜻이다. http는 위 insecureContext 선판정이 먼저 걸러낸다.
    expect(classifyCameraFailure("TypeError", true)).toBe("unsupportedBrowser");
  });

  it("보안 컨텍스트가 아니면 **이름과 무관하게** insecureContext가 먼저다", () => {
    // http로 열면 navigator.mediaDevices 자체가 undefined라 name이 TypeError가 되고,
    // 먼저 판정하지 않으면 unknown으로 뭉개진다(현장에서 실제로 발생하는 오구성).
    for (const name of ["TypeError", "NotAllowedError", "NotFoundError", ""]) {
      expect(classifyCameraFailure(name, false), name).toBe("insecureContext");
    }
  });
});

describe("cameraFailureMessageKey · isCameraRetryable", () => {
  /**
   * ⚠️ **손으로 열거하지 않는다.** 전에는 5종을 손으로 적어 두어 `pipelineStalled`가 신설된
   * 뒤에도 **검증에서 통째로 빠져 있었다** — 같은 누락이 반복되는 구조였다.
   * `STRINGS.camera.errors`의 키는 `CameraFailureMessageKey`(= `CameraFailureReason`)와
   * 1:1이므로 여기서 유도하면 사유를 늘릴 때 **테스트가 자동으로 커진다**.
   */
  const ALL = Object.keys(STRINGS.camera.errors) as readonly CameraFailureReason[];

  it("사유 전부가 실제 문구 카탈로그에 매핑된다(빈 문구 없음)", () => {
    expect(ALL.length).toBeGreaterThanOrEqual(9);
    for (const reason of ALL) {
      const message = STRINGS.camera.errors[cameraFailureMessageKey(reason)];
      expect(typeof message, reason).toBe("string");
      expect(message.length, reason).toBeGreaterThan(0);
    }
  });

  it("사유 전부가 retryable 판정을 갖는다(undefined 누락 없음)", () => {
    for (const reason of ALL) {
      expect(typeof isCameraRetryable(reason), reason).toBe("boolean");
    }
  });

  it("권한 거부·비보안 연결·브라우저 미지원에는 [다시 시도]를 붙이지 않는다", () => {
    expect(isCameraRetryable("permissionDenied")).toBe(false);
    expect(isCameraRetryable("insecureContext")).toBe(false);
    // 같은 브라우저에서 다시 눌러도 `mediaDevices`는 생기지 않는다 — 헛도는 버튼이다.
    expect(isCameraRetryable("unsupportedBrowser")).toBe(false);
  });

  it("장치 부재·점유·미상·재생 차단·지연은 재시도 가능하다", () => {
    expect(isCameraRetryable("noDevice")).toBe(true);
    expect(isCameraRetryable("inUse")).toBe(true);
    expect(isCameraRetryable("unknown")).toBe(true);
    // 터치 한 번으로 자동재생 정책이 풀린다 — 재시도에 실효가 있다.
    expect(isCameraRetryable("playbackBlocked")).toBe(true);
    expect(isCameraRetryable("pipelineSlow")).toBe(true);
  });
});

// ─────────── 진단 코드 새니타이즈 (설계 §2.1 — 2026-08-07 신설) ───────────

describe("sanitizeFailureDetail — 화면에 나가는 값의 보안 경계", () => {
  it("브라우저 예외 이름과 우리 경로 토큰은 통과한다", () => {
    for (const value of ["AbortError", "NotAllowedError", "main-none", "f3", "worker-transferred"]) {
      expect(sanitizeFailureDetail(value), value).toBe(value);
    }
  });

  it("이메일·토큰·공백·한글·33자 이상을 전부 null로 접는다", () => {
    // 이 관문이 뚫리면 게이트 키·계정 email·기기 label·예외 **메시지**가 화면 코드로 새어 나간다
    // (기존 정적 검사 DIAG-1·AUTH-3와 같은 계열의 방어다).
    for (const value of [
      "a@b.com",
      "Could not start video source",
      "권한이 거부되었습니다",
      "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc",
      "a".repeat(33),
      "",
      "path/to/thing",
      "key=value",
    ]) {
      expect(sanitizeFailureDetail(value), value).toBeNull();
    }
  });

  it("null·undefined·비문자열은 null이다", () => {
    expect(sanitizeFailureDetail(null)).toBeNull();
    expect(sanitizeFailureDetail(undefined)).toBeNull();
  });
});

describe("cameraFailure · classifyCameraFailureFrom · formatCameraFailureCode", () => {
  it("상세가 없으면 사유만, 있으면 `사유/상세`다", () => {
    expect(formatCameraFailureCode(cameraFailure("insecureContext"))).toBe("insecureContext");
    expect(formatCameraFailureCode(cameraFailure("unknown", "AbortError"))).toBe(
      "unknown/AbortError",
    );
    expect(formatCameraFailureCode(cameraFailure("pipelineStalled", "main-none"))).toBe(
      "pipelineStalled/main-none",
    );
  });

  it("새니타이즈를 통과하지 못한 상세는 코드에서 통째로 사라진다", () => {
    const failure = cameraFailure("unknown", "Could not start video source");
    expect(failure.detail).toBeNull();
    expect(formatCameraFailureCode(failure)).toBe("unknown");
  });

  it("예외에서 만들면 사유는 classifyCameraFailure와 **같은 판정**이고 상세는 `name`이다", () => {
    const err = new DOMException("dev /dev/video0 is busy", "NotReadableError");
    const failure = classifyCameraFailureFrom(err, true);
    expect(failure.reason).toBe(classifyCameraFailure("NotReadableError", true));
    // ⚠️ `message`가 아니라 `name`이다 — 메시지에는 기기명·경로가 섞인다.
    expect(failure.detail).toBe("NotReadableError");
    expect(formatCameraFailureCode(failure)).toBe("inUse/NotReadableError");
  });

  it("Error가 아닌 값도 예외를 던지지 않고 unknown으로 접는다", () => {
    const failure = classifyCameraFailureFrom("문자열 오류", true);
    expect(failure.reason).toBe("unknown");
    expect(failure.detail).toBeNull();
  });

  it("보안 컨텍스트가 아니면 이름과 무관하게 insecureContext다(순서 불변)", () => {
    const failure = classifyCameraFailureFrom(new DOMException("x", "NotAllowedError"), false);
    expect(failure.reason).toBe("insecureContext");
  });
});
