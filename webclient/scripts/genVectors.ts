/**
 * 공유 테스트 벡터 생성기 — `docs/spec-vectors/*.json` (10 §3)
 *
 * 실행: `npx vite-node scripts/genVectors.ts`
 *
 * ⚠️ `node scripts/genVectors.ts`로는 **동작하지 않는다.** Node의 TS 타입 스트리핑은 TS 문법은 지우지만
 *    모듈 해석은 Node ESM 규칙이라 도메인 내부의 확장자 없는 상대 import(`../mathCompat`)를 못 찾는다.
 *    `vite-node`(vitest 의존성에 포함)가 Vite의 해석기를 쓰므로 그것으로 실행한다.
 *
 * ## 이 스크립트의 역할과 한계
 * 기대값(`expected`)은 **웹 구현이 만든다**. 그것만으로는 "웹이 자기 자신과 일치한다"는 동어반복이므로,
 * **C# 쪽 `SpecVectorTests.cs`가 같은 파일을 읽어 검증하는 것이 진짜 교차 검증**이다.
 * C#이 불일치하면 규격 진실원은 C#이므로 **웹을 고친다**(10 §3.3).
 *
 * 이후 규격 변경은 **벡터 파일을 먼저 고친다** → 양쪽 테스트가 동시에 실패 → 양쪽을 고친다.
 * (그 시점부터 이 생성기를 다시 돌리면 안 된다 — 웹 구현으로 기대값을 덮어써 교차 검증이 무력화된다.)
 */

import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { centerCrop } from "../src/domain/capture/centerCrop.ts";
import { clampSlotToFrame } from "../src/domain/capture/slotPlacement.ts";
import { computeSpeedFactor } from "../src/domain/capture/timelapseSpeed.ts";
import {
  autoArrange,
  clampToFrame,
  hasAnyOverlap,
  isValidLayout,
  overlaps,
  scaleSlots,
} from "../src/domain/frames/slotLayout.ts";
import { canvasToFrame, computeEditorTransform } from "../src/domain/frames/editorTransform.ts";
import { nextCopyName, stripCopySuffix } from "../src/domain/frames/frameNaming.ts";
import { parseSlotsFile } from "../src/domain/frames/slotsFile.ts";
import type { Slot } from "../src/domain/frames/types.ts";
import {
  clampSettings,
  DEFAULT_SETTINGS,
  type AppSettingsValues,
} from "../src/domain/settings/appSettings.ts";
import { isAutoCutCount, resolveCutCount } from "../src/domain/settings/cutCountPolicy.ts";
import { normalizeQrToggles, onQrReEnabled } from "../src/domain/settings/qrDeliveryPolicy.ts";
import { assignableRoles } from "../src/domain/roles/roleChangePolicy.ts";
import {
  canManage,
  canResetPin,
  canWriteFrames,
  hierarchyRank,
  isPower,
  USER_ROLES,
  type UserRole,
} from "../src/domain/roles/userRole.ts";
import {
  computeExpiresAt,
  downloadPageUrl,
  finalImagePath,
  newSessionId,
  stampPrefix,
  timelapsePath,
  tokenDownloadUrl,
} from "../src/domain/upload/uploadContract.ts";

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "docs", "spec-vectors");

interface VectorFile {
  readonly name: string;
  readonly spec: string;
  readonly note?: string;
  readonly cases: readonly unknown[];
}

function write(file: VectorFile): void {
  mkdirSync(OUT_DIR, { recursive: true });
  const path = join(OUT_DIR, `${file.name}.json`);
  writeFileSync(path, `${JSON.stringify(file, null, 2)}\n`, "utf8");
  console.log(`  ${file.name}.json — ${file.cases.length} cases`);
}

const slot = (index: number, x: number, y: number, width: number, height: number): Slot => ({
  index,
  x,
  y,
  width,
  height,
});

// ── 1. center-crop ────────────────────────────────────────────────────────────
{
  const inputs: [number, number, number][] = [
    // 일반 케이스
    [1920, 1080, 3 / 4],
    [1920, 1080, 4 / 3],
    [1920, 1080, 1],
    [1280, 720, 3 / 4],
    [1080, 1920, 3 / 4],
    [640, 480, 1],
    [4032, 3024, 3 / 4],
    [1600, 1200, 4 / 3], // 이미 목표 비율
    [1000, 1000, 1], // 정사각 → 무크롭
    // 반올림 중간값(.5) — 은행가 반올림 검증의 핵심
    [1000, 133, 0.5], // srcH*aspect = 66.5 → 66 (JS Math.round는 67)
    [1000, 135, 0.5], // 67.5 → 68 (양쪽 동일 — 대조군)
    [100, 200, 1.6], // srcW/aspect = 62.5 → 62 (JS는 63)
    [100, 200, 0.8], // srcW/aspect = 125 (정수)
    [333, 1000, 0.666], // srcW/aspect = 499.99… (중간값 아님)
    [1000, 1333, 0.75], // srcW/aspect = 1333.33…
    [999, 1000, 0.666], // 홀수 폭
    // 경계·방어
    [0, 0, 1],
    [-10, 100, 1],
    [1920, 1080, 0],
    [1920, 1080, -1],
    [1, 1, 1],
    [1920, 1, 3 / 4],
    [1, 1080, 3 / 4],
  ];

  write({
    name: "center-crop",
    spec: "docs/analysis/14 §3 (centerCrop) · 04 §9 정수 연산 대응표",
    note: "cropW = roundHalfToEven(srcH * targetAspect), x = floor((srcW - cropW) / 2). .5 케이스 필수.",
    cases: inputs.map(([srcWidth, srcHeight, targetAspect]) => ({
      input: { srcWidth, srcHeight, targetAspect },
      expected: centerCrop(srcWidth, srcHeight, targetAspect),
    })),
  });
}

// ── 2. auto-arrange ───────────────────────────────────────────────────────────
{
  const inputs: [number, number, number, number | null][] = [
    [1, 1200, 1600, null],
    [2, 1200, 1600, null],
    [3, 1200, 1600, null],
    [4, 1200, 1600, null],
    [5, 1200, 1600, null],
    [6, 1200, 1600, null],
    [4, 1200, 1600, 3 / 4],
    [4, 1200, 1600, 4 / 3],
    [4, 1200, 1600, 1],
    [6, 1200, 1600, 3 / 4],
    [2, 1600, 1200, 4 / 3],
    // 세로 스트립(frameAspect < 0.6) → 1열
    [4, 400, 1600, null],
    [4, 400, 1600, 3 / 4],
    [2, 480, 1600, 1],
    // 경계: clamp 1~6
    [0, 1200, 1600, 3 / 4],
    [7, 1200, 1600, 3 / 4],
    // 작은 프레임(margin·gap 하한 20/12가 걸린다)
    [4, 200, 300, 3 / 4],
    [1, 100, 100, 1],
  ];

  write({
    name: "auto-arrange",
    spec: "docs/analysis/14 §4.1 (autoArrange · fitInCell)",
    note: "margin=max(20, floor(frameW/20)), gap=max(12, floor(frameW/40)), cell=floor(...), 비율맞춤=roundHalfToEven.",
    cases: inputs.map(([slotCount, frameW, frameH, targetAspect]) => ({
      input: { slotCount, frameW, frameH, targetAspect },
      expected: autoArrange(slotCount, frameW, frameH, targetAspect),
    })),
  });
}

// ── 3. scale-slots ────────────────────────────────────────────────────────────
{
  const base4 = autoArrange(4, 1200, 1600, 3 / 4);
  const inputs: { baseSlots: Slot[]; factor: number; frameW: number; frameH: number }[] = [
    { baseSlots: base4, factor: 1.0, frameW: 1200, frameH: 1600 },
    { baseSlots: base4, factor: 0.7, frameW: 1200, frameH: 1600 },
    { baseSlots: base4, factor: 1.3, frameW: 1200, frameH: 1600 },
    { baseSlots: base4, factor: 0.85, frameW: 1200, frameH: 1600 },
    // newX 중간값: cx - newW/2 = 196.5 → 은행가 196 (JS Math.round는 197)
    { baseSlots: [slot(0, 199, 199, 100, 100)], factor: 1.05, frameW: 1200, frameH: 1600 },
    // newW 중간값: 100 * 1.505 = 150.5 → 은행가 150 (JS는 151)
    { baseSlots: [slot(0, 300, 300, 100, 100)], factor: 1.505, frameW: 1200, frameH: 1600 },
    // 경계 클램프가 개입하는 케이스
    { baseSlots: [slot(0, 0, 0, 100, 100)], factor: 1.5, frameW: 120, frameH: 120 },
    { baseSlots: [slot(0, 1100, 1500, 100, 100)], factor: 1.3, frameW: 1200, frameH: 1600 },
    // 하한 1px
    { baseSlots: [slot(0, 10, 10, 2, 2)], factor: 0.1, frameW: 100, frameH: 100 },
  ];

  write({
    name: "scale-slots",
    spec: "docs/analysis/14 §4.2 (scaleSlots) · 04 §9",
    note: "cx는 부동소수(floor 금지). newX = roundHalfToEven(cx - newW/2). 결과는 편집기 clampToFrame 적용 후.",
    cases: inputs.map((input) => ({
      input,
      expected: scaleSlots(input.baseSlots, input.factor, input.frameW, input.frameH),
    })),
  });
}

// ── 4. clamp-slot (편집기용·합성용 두 식) ─────────────────────────────────────
{
  const inputs: { slot: Slot; frameW: number; frameH: number }[] = [
    { slot: slot(0, 100, 100, 200, 200), frameW: 1200, frameH: 1600 },
    { slot: slot(0, -50, -50, 200, 200), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 1100, 1500, 200, 200), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 0, 0, 2000, 2000), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 1199, 1599, 100, 100), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 1200, 1600, 100, 100), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 50, 50, 0, 0), frameW: 1200, frameH: 1600 },
    { slot: slot(0, 50, 50, -10, -10), frameW: 1200, frameH: 1600 },
  ];

  write({
    name: "clamp-slot",
    spec: "docs/analysis/14 §4.3(편집기) · §5.2(합성) — 두 식이 다르다",
    note: "editor: 슬롯 전체가 프레임 안(x ≤ frameW-w). composition: x ≤ frameW-1 후 폭을 남은 공간으로 클램프.",
    cases: inputs.map((input) => ({
      input,
      expected: {
        editor: clampToFrame(input.slot, input.frameW, input.frameH),
        composition: clampSlotToFrame(input.slot, input.frameW, input.frameH),
      },
    })),
  });
}

// ── 5. overlap ────────────────────────────────────────────────────────────────
{
  const pairs: [Slot, Slot][] = [
    [slot(0, 0, 0, 100, 100), slot(1, 100, 0, 100, 100)], // 경계 접촉 → 겹침 아님
    [slot(0, 0, 0, 100, 100), slot(1, 0, 100, 100, 100)], // 상하 접촉
    [slot(0, 0, 0, 100, 100), slot(1, 99, 0, 100, 100)], // 1px 겹침
    [slot(0, 0, 0, 100, 100), slot(1, 50, 50, 100, 100)], // 대각 겹침
    [slot(0, 0, 0, 100, 100), slot(1, 200, 200, 100, 100)], // 분리
    [slot(0, 0, 0, 100, 100), slot(1, 10, 10, 10, 10)], // 포함
    [slot(0, 0, 0, 100, 100), slot(1, 0, 0, 100, 100)], // 완전 일치
  ];

  const layouts: { slots: Slot[]; frameW: number; frameH: number }[] = [
    { slots: autoArrange(4, 1200, 1600, 3 / 4), frameW: 1200, frameH: 1600 },
    { slots: [slot(0, 0, 0, 100, 100), slot(1, 99, 0, 100, 100)], frameW: 1200, frameH: 1600 },
    { slots: [], frameW: 1200, frameH: 1600 },
    { slots: [slot(0, -1, 0, 100, 100)], frameW: 1200, frameH: 1600 },
    { slots: [slot(0, 1150, 0, 100, 100)], frameW: 1200, frameH: 1600 },
    {
      slots: [0, 1, 2, 3, 4, 5, 6].map((i) => slot(i, i * 110, 0, 100, 100)),
      frameW: 1200,
      frameH: 1600,
    },
  ];

  write({
    name: "overlap",
    spec: "docs/analysis/14 §4.4 (overlaps · hasAnyOverlap · isValid)",
    note: "경계 접촉은 겹침이 아니다. isValid = 개수 1~6 AND 전부 경계 내 AND 겹침 없음.",
    cases: [
      ...pairs.map(([a, b]) => ({
        kind: "pair",
        input: { a, b },
        expected: { overlaps: overlaps(a, b) },
      })),
      ...layouts.map((input) => ({
        kind: "layout",
        input,
        expected: {
          hasAnyOverlap: hasAnyOverlap(input.slots),
          isValid: isValidLayout(input.slots, input.frameW, input.frameH),
        },
      })),
    ],
  });
}

// ── 6. editor-transform ───────────────────────────────────────────────────────
{
  const inputs: [number, number, number, number][] = [
    [800, 600, 1200, 1600],
    [600, 800, 1200, 1600],
    [1200, 1600, 1200, 1600],
    [2400, 3200, 1200, 1600],
    [1000, 1000, 1200, 1600],
    [900, 400, 400, 1600],
    [0, 600, 1200, 1600],
    [800, 0, 1200, 1600],
    [800, 600, 0, 1600],
    [800, 600, 1200, 0],
  ];

  const roundTrip: [number, number, number, number, number, number][] = [
    [800, 600, 1200, 1600, 0, 0],
    [800, 600, 1200, 1600, 600, 800],
    [800, 600, 1200, 1600, 1200, 1600],
    [800, 600, 1200, 1600, 137, 911],
  ];

  write({
    name: "editor-transform",
    spec: "docs/analysis/14 §4.5 (EditorTransform)",
    note: "scale·origin은 부동소수 그대로(floor 금지). 왕복(frame→canvas→frame)이 원값으로 돌아와야 한다.",
    cases: [
      ...inputs.map(([canvasW, canvasH, frameW, frameH]) => ({
        kind: "compute",
        input: { canvasW, canvasH, frameW, frameH },
        expected: computeEditorTransform(canvasW, canvasH, frameW, frameH),
      })),
      ...roundTrip.map(([canvasW, canvasH, frameW, frameH, fx, fy]) => {
        const t = computeEditorTransform(canvasW, canvasH, frameW, frameH);
        const canvas = { x: t.originX + fx * t.scale, y: t.originY + fy * t.scale };
        return {
          kind: "roundTrip",
          input: { canvasW, canvasH, frameW, frameH, fx, fy },
          expected: { canvas, frame: canvasToFrame(t, canvas.x, canvas.y) },
        };
      }),
    ],
  });
}

// ── 7. role-matrix ────────────────────────────────────────────────────────────
{
  const cases: unknown[] = [];
  for (const actor of USER_ROLES) {
    for (const current of USER_ROLES) {
      cases.push({
        input: { actor, current },
        expected: {
          assignableRoles: assignableRoles(actor, current),
          canManage: canManage(actor, current),
          canResetPin: canResetPin(actor, current),
        },
      });
    }
  }
  for (const role of USER_ROLES) {
    cases.push({
      input: { role },
      expected: {
        isPower: isPower(role),
        canWriteFrames: canWriteFrames(role),
        hierarchyRank: hierarchyRank(role as UserRole),
      },
    });
  }

  write({
    name: "role-matrix",
    spec: "docs/analysis/60 §1 · §1.4 — 서버 setRole 매트릭스와 1:1",
    note: "assignableRoles 순서는 위계 오름차순 고정. canResetPin만 '엄격히 낮은 위계'.",
    cases,
  });
}

// ── 8. copy-name ──────────────────────────────────────────────────────────────
{
  const inputs: { baseName: string | null; existingNames: string[] }[] = [
    { baseName: "베이직 4컷", existingNames: [] },
    { baseName: "베이직 4컷", existingNames: ["베이직 4컷 사본"] },
    { baseName: "베이직 4컷", existingNames: ["베이직 4컷 사본", "베이직 4컷 사본 2"] },
    { baseName: "베이직 4컷 사본", existingNames: [] }, // 이미 사본 → 원형으로 되돌림
    { baseName: "베이직 4컷 사본 3", existingNames: [] },
    { baseName: "베이직 4컷 사본", existingNames: ["베이직 4컷 사본"] },
    { baseName: "", existingNames: [] }, // 빈 이름 → "새 프레임 사본"
    { baseName: "   ", existingNames: [] },
    { baseName: null, existingNames: [] },
    { baseName: "사본", existingNames: [] }, // 떼면 비므로 원문 유지
    { baseName: "사본 2", existingNames: [] },
    { baseName: "A", existingNames: ["A 사본", "A 사본 2", "A 사본 3"] },
    { baseName: "  공백 트림  ", existingNames: [] },
  ];

  const stripInputs = [
    "베이직 4컷",
    "베이직 4컷 사본",
    "베이직 4컷 사본 2",
    "베이직 4컷 사본 99",
    "베이직 4컷 사본 100", // 3자리는 접미로 보지 않는다
    "사본",
    "사본 2",
    "",
    "  ",
    "사본이야기",
  ];

  write({
    name: "copy-name",
    spec: "docs/analysis/13 §6.4 (FrameNaming)",
    note: "99까지 모두 충돌하는 케이스는 난수 접미라서 **결정적이지 않으므로 벡터에 넣지 않는다**(웹 단위 테스트에서만 확인).",
    cases: [
      ...inputs.map((input) => ({
        kind: "nextCopyName",
        input,
        expected: nextCopyName(input.baseName, input.existingNames, () => "UNUSED"),
      })),
      ...stripInputs.map((name) => ({
        kind: "stripCopySuffix",
        input: { name },
        expected: stripCopySuffix(name),
      })),
    ],
  });
}

// ── 9. session-id ─────────────────────────────────────────────────────────────
{
  const stamps: [number, number, number, number, number, number][] = [
    [2026, 7, 30, 21, 5, 9],
    [2026, 1, 1, 0, 0, 0],
    [2026, 12, 31, 23, 59, 59],
    [2026, 2, 28, 9, 30, 0],
  ];
  const uuid = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

  const paths = [
    { sessionId: "20260730_210509_" + uuid, format: "Jpg" as const },
    { sessionId: "20260730_210509_" + uuid, format: "Png" as const },
  ];

  write({
    name: "session-id",
    spec: "docs/analysis/31 §7 (UploadContract) · M13",
    note: "stampPrefix는 로컬 시각 성분으로 조립한다(타임존 무관하게 재현되도록 성분을 직접 전달). expiresAt은 epoch ms로 비교.",
    cases: [
      ...stamps.map(([year, month, day, hour, minute, second]) => {
        const date = new Date(year, month - 1, day, hour, minute, second);
        return {
          kind: "stamp",
          input: { year, month, day, hour, minute, second, uuid },
          expected: { stampPrefix: stampPrefix(date), sessionId: newSessionId(date, uuid) },
        };
      }),
      ...paths.map((input) => ({
        kind: "paths",
        input,
        expected: {
          finalImagePath: finalImagePath(input.sessionId, input.format),
          timelapsePath: timelapsePath(input.sessionId),
        },
      })),
      {
        kind: "urls",
        input: {
          bucket: "mcphoto-955fb.firebasestorage.app",
          storagePath: `results/20260730_210509_${uuid}/final.jpg`,
          downloadToken: "tok-123",
          hostingBaseUrl: "https://mcphoto-955fb.web.app/",
          token: `20260730_210509_${uuid}`,
        },
        expected: {
          tokenDownloadUrl: tokenDownloadUrl(
            "mcphoto-955fb.firebasestorage.app",
            `results/20260730_210509_${uuid}/final.jpg`,
            "tok-123",
          ),
          downloadPageUrl: downloadPageUrl(
            "https://mcphoto-955fb.web.app/",
            `20260730_210509_${uuid}`,
          ),
        },
      },
      {
        kind: "urls",
        input: {
          bucket: "b",
          storagePath: "results/x/timelapse.mp4",
          downloadToken: "t",
          hostingBaseUrl: "https://host///",
          token: "abc",
        },
        expected: {
          tokenDownloadUrl: tokenDownloadUrl("b", "results/x/timelapse.mp4", "t"),
          downloadPageUrl: downloadPageUrl("https://host///", "abc"),
        },
      },
      ...[
        { createdAtEpochMs: Date.UTC(2026, 6, 30, 12, 0, 0), retentionHours: 24 },
        { createdAtEpochMs: Date.UTC(2026, 6, 30, 12, 0, 0), retentionHours: 1 },
        { createdAtEpochMs: Date.UTC(2026, 6, 30, 12, 0, 0), retentionHours: 72 },
      ].map((input) => ({
        kind: "expiresAt",
        input,
        expected: {
          expiresAtEpochMs: computeExpiresAt(
            new Date(input.createdAtEpochMs),
            input.retentionHours,
          ).getTime(),
        },
      })),
    ],
  });
}

// ── 10. timelapse-speed ───────────────────────────────────────────────────────
{
  const inputs = [0, 1, 5, 9.9, 10, 12.5, 15, 15.0001, 20, 25, 38, 50, 125, 1000];

  write({
    name: "timelapse-speed",
    spec: "docs/analysis/14 §7.2 (ComputeSpeedFactor)",
    note: "세션이 목표 상한(15초) 이하면 1.0(원속). 그보다 길면 sessionSeconds / 12.5, 최소 1.",
    cases: inputs.map((sessionSeconds) => ({
      input: { sessionSeconds },
      expected: { factor: computeSpeedFactor(sessionSeconds) },
    })),
  });
}

// ── 11. settings-clamp ────────────────────────────────────────────────────────
{
  // 각 케이스는 **부분 패치**다: 양쪽이 자기 기본값에 패치를 얹고 clamp한 뒤 expected 키만 비교한다.
  const patches: Partial<AppSettingsValues>[] = [
    { CutCount: 0 }, // 자동 sentinel 보존(WD19) — 6으로 덮이면 실패
    { CutCount: 6 },
    { CutCount: 3 },
    { CutCount: 7 }, // 동률 → 앞선 값(6)
    { CutCount: 9 }, // 동률 → 8
    { CutCount: 11 },
    { CutCount: -1 }, // 자동이 아니다 → 6
    { CutCount: 100 },
    { CountdownSec: 4 },
    { CountdownSec: 5 }, // 3까지 2, 6까지 1 → 6
    { CountdownSec: 7 }, // 동률(6·8) → 6
    { CountdownSec: 9 }, // 동률(8·10) → 8
    { CountdownSec: 0 },
    { RetakeLimit: 0 },
    { RetakeLimit: 4 },
    { RetentionHours: 0 },
    { RetentionHours: 1 },
    { RetentionHours: 72 },
    { RetentionHours: 100 },
    { RetentionHours: -5 },
    { HostingBaseUrl: "https://a.web.app/" },
    { HostingBaseUrl: "https://a.web.app///" },
    { HostingBaseUrl: "https://a.web.app" },
    { BackendBaseUrl: "https://api.example.com/api" },
    { BackendBaseUrl: "https://api.example.com/api/" },
    { BackendBaseUrl: "  https://api.example.com/api  " },
    { BackendBaseUrl: "" },
    { GoogleClientId: "  abc.apps.googleusercontent.com  " },
    { EnableQrDelivery: true, SendPhoto: false, SendTimelapse: false },
    { EnableQrDelivery: true, SendPhoto: false, SendTimelapse: true },
    { EnableQrDelivery: false, SendPhoto: false, SendTimelapse: false },
  ];

  write({
    name: "settings-clamp",
    spec: "docs/analysis/41 §2.1 · §2.7 (AppSettings.Clamp)",
    note: "input은 기본값 위에 얹는 부분 패치. expected에 있는 키만 비교한다(플랫폼 고유 키를 벡터에 넣지 않기 위함). DisplayMode·WindowBounds는 웹에 UI가 없어 제외.",
    cases: patches.map((patch) => {
      const clamped = clampSettings({ ...DEFAULT_SETTINGS, ...patch });
      const expected: Record<string, unknown> = {};
      for (const key of Object.keys(patch)) {
        expected[key] = clamped[key as keyof AppSettingsValues];
      }
      // QR 정규화는 세 키가 연동되므로 항상 셋을 함께 비교한다.
      if ("EnableQrDelivery" in patch || "SendPhoto" in patch || "SendTimelapse" in patch) {
        expected.EnableQrDelivery = clamped.EnableQrDelivery;
        expected.SendPhoto = clamped.SendPhoto;
        expected.SendTimelapse = clamped.SendTimelapse;
      }
      return { input: patch, expected };
    }),
  });
}

// ── 12. cut-count ─────────────────────────────────────────────────────────────
{
  const inputs: [number, number][] = [];
  for (const configured of [0, 6, 8, 10, -1]) {
    for (const slotCount of [0, 1, 2, 3, 4, 5, 6, 8, 10, -3]) {
      inputs.push([configured, slotCount]);
    }
  }

  write({
    name: "cut-count",
    spec: "docs/analysis/41 §2.7 · 13 §4.2·§4.3 (CutCountPolicy, it17)",
    note: "자동(0) = max(6, 슬롯+2), 고정 = max(설정, 슬롯). 슬롯 음수는 0으로 취급.",
    cases: inputs.map(([configured, slotCount]) => ({
      input: { configured, slotCount },
      expected: { resolved: resolveCutCount(configured, slotCount), isAuto: isAutoCutCount(configured) },
    })),
  });
}

// ── 13. qr-normalize ──────────────────────────────────────────────────────────
{
  const cases: unknown[] = [];
  for (const enableQrDelivery of [true, false]) {
    for (const sendPhoto of [true, false]) {
      for (const sendTimelapse of [true, false]) {
        const input = { enableQrDelivery, sendPhoto, sendTimelapse };
        cases.push({ kind: "normalize", input, expected: normalizeQrToggles(input) });
      }
    }
  }
  cases.push({ kind: "reEnable", input: {}, expected: onQrReEnabled() });

  write({
    name: "qr-normalize",
    spec: "docs/analysis/41 §2.4 (QrDeliveryPolicy)",
    note: "QR on인데 하위 둘 다 off면 QR off(하위 값 보존). 재활성 시 하위 둘 다 on.",
    cases,
  });
}

// ── 14. slots-file ────────────────────────────────────────────────────────────
{
  const texts: string[] = [
    "#imagesize=1200,1600\n0,80,117,490,653\n1,630,117,490,653\n",
    "#imagesize=1200,1600\n#dbid=abc123\n0,0,0,100,100\n",
    "#IMAGESIZE=800,600\n#DBID=Xyz\n0,1,2,3,4\n", // 대문자 프리픽스
    "0,0,0,100,100\n", // imagesize 없음 → 0,0
    "#imagesize=1200\n0,0,0,100,100\n", // 필드 부족 → 무시
    "#imagesize=abc,def\n0,0,0,100,100\n", // 숫자 아님 → 무시
    "#imagesize=1200,1600\n0,0,0,100\n1,0,0,100,100\n", // 4필드 손상 줄 스킵
    "#imagesize=1200,1600\n0,0,0,100,100,999\n1,0,0,100,100\n", // 6필드 스킵
    "#imagesize=1200,1600\nx,0,0,100,100\n1,0,0,100,100\n", // 비숫자 스킵
    "#imagesize=1200,1600\n\n\n0,0,0,100,100\n", // 빈 줄
    "#imagesize=1200,1600\n# 주석\n0,0,0,100,100\n", // 기타 주석
    "  #imagesize=1200,1600  \n  0 , 0 , 0 , 100 , 100  \n", // 공백 트림
    "#imagesize=1200,1600\n0,-10,-20,100,100\n", // 음수 좌표(파싱은 통과)
    "#imagesize=1200,1600\r\n0,0,0,100,100\r\n", // CRLF
    "", // 빈 파일
    "#imagesize=1200,1600\n0,0,0,1.5,100\n", // 소수 → 거부
    "#imagesize=1200,1600\n0,0,0,99999999999,100\n", // int 범위 초과 → 거부
  ];

  write({
    name: "slots-file",
    spec: "docs/analysis/41 §3.3 (.slots 포맷)",
    note: "손상 줄은 예외 없이 건너뛴다. int 파싱은 C# int.TryParse와 같은 판정(소수·범위 초과 거부).",
    cases: texts.map((text) => ({ input: { text }, expected: parseSlotsFile(text) })),
  });
}

console.log(`\n벡터 생성 완료 → ${OUT_DIR}`);
