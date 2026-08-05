import { describe, expect, it } from "vitest";

import { centerCrop } from "@domain/capture/centerCrop";
import { clampSlotToFrame } from "@domain/capture/slotPlacement";
import { computeSpeedFactor } from "@domain/capture/timelapseSpeed";
import {
  canvasToFrame,
  computeEditorTransform,
  frameToCanvas,
  isValidTransform,
} from "@domain/frames/editorTransform";
import {
  classifyFrameLoad,
  DEFAULT_FRAME_LOAD_PHASE,
  finalizeFrameLoad,
  frameLoadNotice,
  IDLE_WARNING_REFERENCE_SECONDS,
  MAX_TOTAL_WAIT_SECONDS,
  NO_PROGRESS_TIMEOUT_SECONDS,
  nextFrameLoadDeadlineMs,
  type FrameLoadPhase,
} from "@domain/frames/frameLoadPolicy";
import { nextCopyName, stripCopySuffix } from "@domain/frames/frameNaming";
import {
  autoArrange,
  clampToFrame,
  hasAnyOverlap,
  isValidLayout,
  overlaps,
  scaleSlots,
} from "@domain/frames/slotLayout";
import { parseSlotsFile } from "@domain/frames/slotsFile";
import type { Slot } from "@domain/frames/types";
import { assignableRoles } from "@domain/roles/roleChangePolicy";
import {
  canManage,
  canResetPin,
  canWriteFrames,
  hierarchyRank,
  isPower,
  parseRole,
} from "@domain/roles/userRole";
import {
  clampSettings,
  DEFAULT_SETTINGS,
  type AppSettingsValues,
} from "@domain/settings/appSettings";
import { isAutoCutCount, resolveCutCount } from "@domain/settings/cutCountPolicy";
import { normalizeQrToggles, onQrReEnabled } from "@domain/settings/qrDeliveryPolicy";
import {
  computeExpiresAt,
  downloadPageUrl,
  finalImagePath,
  newSessionId,
  stampPrefix,
  timelapsePath,
  tokenDownloadUrl,
} from "@domain/upload/uploadContract";

import { EXPECTED_VECTOR_NAMES, loadCases, vectorFileNames } from "../../vectors/loadVector";

/**
 * 공유 테스트 벡터 검증 — Windows `SpecVectorTests.cs`가 **같은 파일**을 읽는다.
 * 벡터 값을 하나 바꾸면 양쪽 테스트가 동시에 실패해야 한다(10 §3.3).
 */

const TOLERANCE = 1e-9;

function expectClose(actual: number, expected: number): void {
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(TOLERANCE);
}

describe("벡터 파일 목록", () => {
  it("15개 파일이 모두 있고 이름이 기대와 일치한다", () => {
    expect(vectorFileNames()).toEqual([...EXPECTED_VECTOR_NAMES]);
  });
});

describe("center-crop", () => {
  const cases = loadCases<{
    input: { srcWidth: number; srcHeight: number; targetAspect: number };
    expected: { x: number; y: number; width: number; height: number };
  }>("center-crop");

  it("모든 케이스가 일치한다", () => {
    expect(cases.length).toBeGreaterThan(20);
    for (const { input, expected } of cases) {
      expect(centerCrop(input.srcWidth, input.srcHeight, input.targetAspect), JSON.stringify(input))
        .toEqual(expected);
    }
  });

  it("중간값(.5) 케이스가 은행가 반올림을 따른다 — JS Math.round와 다르다", () => {
    // 133 * 0.5 = 66.5 → 66 (Math.round는 67)
    expect(centerCrop(1000, 133, 0.5).width).toBe(66);
    // 100 / 1.6 = 62.5 → 62 (Math.round는 63)
    expect(centerCrop(100, 200, 1.6).height).toBe(62);
  });
});

describe("auto-arrange", () => {
  const cases = loadCases<{
    input: { slotCount: number; frameW: number; frameH: number; targetAspect: number | null };
    expected: Slot[];
  }>("auto-arrange");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      expect(
        autoArrange(input.slotCount, input.frameW, input.frameH, input.targetAspect),
        JSON.stringify(input),
      ).toEqual(expected);
    }
  });
});

describe("scale-slots", () => {
  const cases = loadCases<{
    input: { baseSlots: Slot[]; factor: number; frameW: number; frameH: number };
    expected: Slot[];
  }>("scale-slots");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      expect(
        scaleSlots(input.baseSlots, input.factor, input.frameW, input.frameH),
        JSON.stringify(input),
      ).toEqual(expected);
    }
  });
});

describe("clamp-slot", () => {
  const cases = loadCases<{
    input: { slot: Slot; frameW: number; frameH: number };
    expected: {
      editor: Slot;
      composition: { x: number; y: number; width: number; height: number };
    };
  }>("clamp-slot");

  it("편집기용·합성용 두 식이 각각 일치한다", () => {
    for (const { input, expected } of cases) {
      expect(clampToFrame(input.slot, input.frameW, input.frameH), JSON.stringify(input)).toEqual(
        expected.editor,
      );
      expect(
        clampSlotToFrame(input.slot, input.frameW, input.frameH),
        JSON.stringify(input),
      ).toEqual(expected.composition);
    }
  });

  it("두 식은 실제로 다르다(하나로 합치면 안 된다)", () => {
    const outOfBounds: Slot = { index: 0, x: 0, y: 0, width: 2000, height: 2000 };
    const editor = clampToFrame(outOfBounds, 1200, 1600);
    const composition = clampSlotToFrame(outOfBounds, 1200, 1600);
    expect(editor.width).toBe(1200);
    expect(composition.width).toBe(1200);
    const nearEdge: Slot = { index: 0, x: 1199, y: 1599, width: 100, height: 100 };
    expect(clampToFrame(nearEdge, 1200, 1600).x).toBe(1100); // 슬롯 전체가 안으로
    expect(clampSlotToFrame(nearEdge, 1200, 1600).x).toBe(1199); // 좌표 유지 + 폭 축소
    expect(clampSlotToFrame(nearEdge, 1200, 1600).width).toBe(1);
  });
});

describe("overlap", () => {
  const cases = loadCases<{
    kind: "pair" | "layout";
    input: { a?: Slot; b?: Slot; slots?: Slot[]; frameW?: number; frameH?: number };
    expected: { overlaps?: boolean; hasAnyOverlap?: boolean; isValid?: boolean };
  }>("overlap");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      if (c.kind === "pair") {
        expect(overlaps(c.input.a!, c.input.b!), JSON.stringify(c.input)).toBe(
          c.expected.overlaps,
        );
      } else {
        expect(hasAnyOverlap(c.input.slots!), JSON.stringify(c.input)).toBe(
          c.expected.hasAnyOverlap,
        );
        expect(
          isValidLayout(c.input.slots!, c.input.frameW!, c.input.frameH!),
          JSON.stringify(c.input),
        ).toBe(c.expected.isValid);
      }
    }
  });
});

describe("editor-transform", () => {
  const cases = loadCases<{
    kind: "compute" | "roundTrip";
    input: { canvasW: number; canvasH: number; frameW: number; frameH: number; fx?: number; fy?: number };
    expected: Record<string, number | { x: number; y: number }>;
  }>("editor-transform");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      const t = computeEditorTransform(
        c.input.canvasW,
        c.input.canvasH,
        c.input.frameW,
        c.input.frameH,
      );
      if (c.kind === "compute") {
        expectClose(t.scale, c.expected.scale as number);
        expectClose(t.originX, c.expected.originX as number);
        expectClose(t.originY, c.expected.originY as number);
        expectClose(t.displayWidth, c.expected.displayWidth as number);
        expectClose(t.displayHeight, c.expected.displayHeight as number);
      } else {
        const canvas = frameToCanvas(t, c.input.fx!, c.input.fy!);
        const expectedCanvas = c.expected.canvas as { x: number; y: number };
        expectClose(canvas.x, expectedCanvas.x);
        expectClose(canvas.y, expectedCanvas.y);

        const frame = canvasToFrame(t, canvas.x, canvas.y);
        const expectedFrame = c.expected.frame as { x: number; y: number };
        expectClose(frame.x, expectedFrame.x);
        expectClose(frame.y, expectedFrame.y);
      }
    }
  });

  it("왕복 변환이 원값으로 돌아온다(WYSIWYG 근거)", () => {
    const t = computeEditorTransform(800, 600, 1200, 1600);
    const canvas = frameToCanvas(t, 137, 911);
    const back = canvasToFrame(t, canvas.x, canvas.y);
    expectClose(back.x, 137);
    expectClose(back.y, 911);
  });

  it("무효 변환은 isValidTransform이 false이고 역변환이 (0,0)이다", () => {
    const invalid = computeEditorTransform(0, 600, 1200, 1600);
    expect(isValidTransform(invalid)).toBe(false);
    expect(canvasToFrame(invalid, 100, 100)).toEqual({ x: 0, y: 0 });

    expect(isValidTransform(computeEditorTransform(800, 600, 1200, 1600))).toBe(true);
  });
});

describe("role-matrix", () => {
  const cases = loadCases<{
    input: { actor?: string; current?: string; role?: string };
    expected: {
      assignableRoles?: string[];
      canManage?: boolean;
      canResetPin?: boolean;
      isPower?: boolean;
      canWriteFrames?: boolean;
      hierarchyRank?: number;
    };
  }>("role-matrix");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      if (input.actor !== undefined) {
        const actor = parseRole(input.actor);
        const current = parseRole(input.current);
        expect(assignableRoles(actor, current), JSON.stringify(input)).toEqual(
          expected.assignableRoles,
        );
        expect(canManage(actor, current), JSON.stringify(input)).toBe(expected.canManage);
        expect(canResetPin(actor, current), JSON.stringify(input)).toBe(expected.canResetPin);
      } else {
        const role = parseRole(input.role);
        expect(isPower(role)).toBe(expected.isPower);
        expect(canWriteFrames(role)).toBe(expected.canWriteFrames);
        expect(hierarchyRank(role)).toBe(expected.hierarchyRank);
      }
    }
  });
});

describe("copy-name", () => {
  const cases = loadCases<{
    kind: "nextCopyName" | "stripCopySuffix";
    input: { baseName?: string | null; existingNames?: string[]; name?: string };
    expected: string;
  }>("copy-name");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      if (c.kind === "nextCopyName") {
        expect(
          nextCopyName(c.input.baseName, c.input.existingNames!, () => "UNUSED"),
          JSON.stringify(c.input),
        ).toBe(c.expected);
      } else {
        expect(stripCopySuffix(c.input.name!), JSON.stringify(c.input)).toBe(c.expected);
      }
    }
  });
});

describe("session-id", () => {
  type Case = {
    kind: "stamp" | "paths" | "urls" | "expiresAt";
    input: Record<string, string | number>;
    expected: Record<string, string | number>;
  };
  const cases = loadCases<Case>("session-id");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      switch (c.kind) {
        case "stamp": {
          const local = new Date(
            c.input.year as number,
            (c.input.month as number) - 1,
            c.input.day as number,
            c.input.hour as number,
            c.input.minute as number,
            c.input.second as number,
          );
          expect(stampPrefix(local)).toBe(c.expected.stampPrefix);
          expect(newSessionId(local, c.input.uuid as string)).toBe(c.expected.sessionId);
          break;
        }
        case "paths": {
          expect(
            finalImagePath(c.input.sessionId as string, c.input.format as "Jpg" | "Png"),
          ).toBe(c.expected.finalImagePath);
          expect(timelapsePath(c.input.sessionId as string)).toBe(c.expected.timelapsePath);
          break;
        }
        case "urls": {
          expect(
            tokenDownloadUrl(
              c.input.bucket as string,
              c.input.storagePath as string,
              c.input.downloadToken as string,
            ),
          ).toBe(c.expected.tokenDownloadUrl);
          expect(
            downloadPageUrl(c.input.hostingBaseUrl as string, c.input.token as string),
          ).toBe(c.expected.downloadPageUrl);
          break;
        }
        case "expiresAt": {
          const created = new Date(c.input.createdAtEpochMs as number);
          expect(computeExpiresAt(created, c.input.retentionHours as number).getTime()).toBe(
            c.expected.expiresAtEpochMs,
          );
          break;
        }
      }
    }
  });
});

describe("timelapse-speed", () => {
  const cases = loadCases<{
    input: { sessionSeconds: number };
    expected: { factor: number };
  }>("timelapse-speed");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      expectClose(computeSpeedFactor(input.sessionSeconds), expected.factor);
    }
  });
});

describe("settings-clamp", () => {
  const cases = loadCases<{
    input: Partial<AppSettingsValues>;
    expected: Partial<Record<keyof AppSettingsValues, unknown>>;
  }>("settings-clamp");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      const clamped = clampSettings({ ...DEFAULT_SETTINGS, ...input });
      for (const [key, value] of Object.entries(expected)) {
        expect(clamped[key as keyof AppSettingsValues], `${key} in ${JSON.stringify(input)}`).toEqual(
          value,
        );
      }
    }
  });

  it("자동 sentinel(CutCount=0)이 저장 왕복에 보존된다 — WD19", () => {
    let values = clampSettings({ ...DEFAULT_SETTINGS, CutCount: 0 });
    expect(values.CutCount).toBe(0);
    values = clampSettings(values); // 두 번째 왕복에서도 살아 있어야 한다
    expect(values.CutCount).toBe(0);
  });
});

describe("cut-count", () => {
  const cases = loadCases<{
    input: { configured: number; slotCount: number };
    expected: { resolved: number; isAuto: boolean };
  }>("cut-count");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      expect(resolveCutCount(input.configured, input.slotCount), JSON.stringify(input)).toBe(
        expected.resolved,
      );
      expect(isAutoCutCount(input.configured)).toBe(expected.isAuto);
    }
  });

  it("슬롯 5개 + 자동이면 7컷이다(it17 실질 차이 구간)", () => {
    expect(resolveCutCount(0, 5)).toBe(7);
    expect(resolveCutCount(0, 6)).toBe(8);
  });
});

describe("qr-normalize", () => {
  const cases = loadCases<{
    kind: "normalize" | "reEnable";
    input: { enableQrDelivery?: boolean; sendPhoto?: boolean; sendTimelapse?: boolean };
    expected: { enableQrDelivery?: boolean; sendPhoto: boolean; sendTimelapse: boolean };
  }>("qr-normalize");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      if (c.kind === "normalize") {
        expect(
          normalizeQrToggles({
            enableQrDelivery: c.input.enableQrDelivery!,
            sendPhoto: c.input.sendPhoto!,
            sendTimelapse: c.input.sendTimelapse!,
          }),
        ).toEqual(c.expected);
      } else {
        expect(onQrReEnabled()).toEqual(c.expected);
      }
    }
  });
});

describe("slots-file", () => {
  const cases = loadCases<{
    input: { text: string };
    expected: {
      imageSize: { width: number; height: number };
      slots: Slot[];
      dbId: string | null;
    };
  }>("slots-file");

  it("모든 케이스가 일치한다", () => {
    for (const { input, expected } of cases) {
      expect(parseSlotsFile(input.text), JSON.stringify(input.text)).toEqual(expected);
    }
  });
});

describe("frame-load-policy", () => {
  type Case = {
    kind: "classify" | "finalize" | "nextDeadline" | "notice" | "constants";
    input: {
      frameCount?: number;
      waitInterrupted?: boolean;
      current?: FrameLoadPhase;
      quiet?: boolean;
      elapsedMs?: number;
      phase?: FrameLoadPhase;
    };
    expected: {
      phase?: FrameLoadPhase;
      nextDeadlineMs?: number;
      notice?: string;
      noProgressTimeoutSeconds?: number;
      maxTotalWaitSeconds?: number;
      idleWarningReferenceSeconds?: number;
      defaultPhase?: FrameLoadPhase;
    };
  };
  const cases = loadCases<Case>("frame-load-policy");

  it("모든 케이스가 일치한다", () => {
    for (const c of cases) {
      const where = JSON.stringify(c.input);
      switch (c.kind) {
        case "classify":
          expect(classifyFrameLoad(c.input.frameCount!, c.input.waitInterrupted!), where).toBe(
            c.expected.phase,
          );
          break;
        case "finalize":
          expect(
            finalizeFrameLoad(
              c.input.current!,
              c.input.frameCount!,
              c.input.waitInterrupted!,
              c.input.quiet!,
            ),
            where,
          ).toBe(c.expected.phase);
          break;
        case "nextDeadline":
          expect(nextFrameLoadDeadlineMs(c.input.elapsedMs!), where).toBe(
            c.expected.nextDeadlineMs,
          );
          break;
        case "notice":
          expect(frameLoadNotice(c.input.phase!), where).toBe(c.expected.notice);
          break;
        case "constants":
          expect(NO_PROGRESS_TIMEOUT_SECONDS).toBe(c.expected.noProgressTimeoutSeconds);
          expect(MAX_TOTAL_WAIT_SECONDS).toBe(c.expected.maxTotalWaitSeconds);
          expect(IDLE_WARNING_REFERENCE_SECONDS).toBe(c.expected.idleWarningReferenceSeconds);
          expect(DEFAULT_FRAME_LOAD_PHASE).toBe(c.expected.defaultPhase);
          break;
        default:
          // 오타난 kind가 조용히 건너뛰어지면 검증 자체가 사라진다.
          throw new Error(`알 수 없는 kind: ${String(c.kind)}`);
      }
    }
  });

  it("종류별 케이스 수가 유지된다 — Windows 쪽과 같은 숫자다", () => {
    // 케이스가 통째로 빠져도 "전부 통과"로 보이므로 개수를 고정한다.
    const count = (kind: Case["kind"]): number => cases.filter((c) => c.kind === kind).length;
    expect(count("classify")).toBe(7);
    expect(count("finalize")).toBe(32);
    expect(count("nextDeadline")).toBe(8);
    expect(count("notice")).toBe(4);
    expect(count("constants")).toBe(1);
  });

  it("벡터가 Loading으로 확정되는 케이스를 담고 있지 않다", () => {
    // 벡터 자체가 "finalize는 Loading을 돌려주지 않는다" 불변식을 위반하지 못하게 한다.
    const phases = cases.filter((c) => c.kind === "finalize").map((c) => c.expected.phase);
    expect(phases).not.toContain("Loading");
  });
});
