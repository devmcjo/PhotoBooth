import { describe, expect, it } from "vitest";
import { runResultNext, type ResultNextDeps } from "@screens/result/resultNext";
import type { ResultSaveInput, ResultSaveOutcome } from "@adapters/storage/resultSaver";
import type { TimelapseResult } from "@adapters/encode/timelapseEncoder";
import { DEFAULT_SETTINGS } from "@domain/settings/appSettings";
import { STRINGS } from "@ui/strings";

/**
 * `Result` [다음] 순서 불변식 — 03 §8.1 · **M6-W**
 *
 * 보관이 전이보다 **앞**이라는 것이 규격이다. Step 11이 업로드를 끼워도 이 상대 순서는 유지돼야 한다.
 */

const SESSION_ID = "20260720_144500_8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f";

function outcome(overrides: Partial<ResultSaveOutcome> = {}): ResultSaveOutcome {
  return {
    status: "saved",
    folderName: "mcphoto_260720_1445",
    finalSaved: true,
    timelapseSaved: true,
    hadTimelapse: true,
    folderCopy: "unsupported",
    folderCopyName: null,
    evicted: 0,
    bytes: 30,
    elapsedMs: 5,
    ...overrides,
  };
}

const TIMELAPSE: TimelapseResult = {
  blob: new Blob([new Uint8Array(20)]),
  path: "webcodecs",
  width: 640,
  height: 480,
  frameCount: 30,
  durationSec: 3,
  speedFactor: 4,
  bytes: 20,
  elapsedMs: 100,
};

interface Harness {
  readonly deps: ResultNextDeps;
  readonly calls: string[];
  readonly saved: ResultSaveInput[];
  readonly toasts: { kind: string; message: string }[];
  readonly went: string[];
}

function harness(overrides: Partial<ResultNextDeps> = {}): Harness {
  const calls: string[] = [];
  const saved: ResultSaveInput[] = [];
  const toasts: { kind: string; message: string }[] = [];
  const went: string[] = [];

  const deps: ResultNextDeps = {
    finishTimelapse: async () => {
      calls.push("finishTimelapse");
      return TIMELAPSE;
    },
    currentTimelapse: () => TIMELAPSE,
    finalBlob: () => new Blob([new Uint8Array(10)]),
    save: async (input) => {
      calls.push("save");
      saved.push(input);
      return outcome();
    },
    settings: () => DEFAULT_SETTINGS,
    sessionId: () => SESSION_ID,
    isLoggedIn: () => true,
    isTempUserBlocked: () => false,
    stillOnResult: () => true,
    go: (to) => {
      calls.push("go");
      went.push(to);
    },
    toast: (kind, message) => {
      calls.push("toast");
      toasts.push({ kind, message });
    },
    now: () => new Date(2026, 6, 20, 14, 45, 0),
    uuid: () => "8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f",
    ...overrides,
  };

  return { deps, calls, saved, toasts, went };
}

describe("runResultNext — 순서 불변식(M6-W)", () => {
  it("정상 완주 순서는 타임랩스 → 보관 → 전이다", async () => {
    const h = harness();
    const result = await runResultNext(h.deps);

    expect(h.calls).toEqual(["finishTimelapse", "save", "go"]);
    expect(result).toEqual({ aborted: false, save: outcome(), destination: "Qr" });
  });

  it("보관이 전이보다 항상 앞이다(Step 11이 업로드를 끼워도 유지돼야 하는 상대 순서)", async () => {
    const h = harness();
    await runResultNext(h.deps);
    expect(h.calls.indexOf("save")).toBeLessThan(h.calls.indexOf("go"));
  });

  it("보관 입력이 설정·세션에서 조립된다", async () => {
    const h = harness();
    await runResultNext(h.deps);

    const input = h.saved[0]!;
    expect(input.format).toBe(DEFAULT_SETTINGS.OutputFormat);
    expect(input.saveLocalCopy).toBe(DEFAULT_SETTINGS.SaveLocalCopy);
    expect(input.sessionId).toBe(SESSION_ID);
    expect(input.timelapseBlob).toBe(TIMELAPSE.blob);
    // 32자 hex — 도메인의 충돌 폴백 접미 규약과 같은 모양이어야 한다.
    expect(input.fallbackToken).toMatch(/^[0-9a-f]{32}$/);
  });

  it("타임랩스가 null이어도 정상 진행한다(VF-6)", async () => {
    const h = harness({ finishTimelapse: async () => null, currentTimelapse: () => null });
    const result = await runResultNext(h.deps);

    expect(h.saved[0]!.timelapseBlob).toBeNull();
    expect(result.destination).toBe("Qr");
    expect(h.toasts).toEqual([]);
  });

  it("타임랩스 생성이 던져도 보관·전이를 계속한다(이중 방어)", async () => {
    const h = harness({
      finishTimelapse: async () => {
        throw new Error("인코더 폭발");
      },
    });
    await expect(runResultNext(h.deps)).resolves.toMatchObject({ aborted: false });
    expect(h.calls).toContain("save");
    expect(h.calls).toContain("go");
  });
});

describe("runResultNext — 홈 복귀 가드", () => {
  it("타임랩스 생성 중 홈 복귀하면 보관도 전이도 하지 않는다", async () => {
    const h = harness({ stillOnResult: () => false });
    const result = await runResultNext(h.deps);

    expect(h.calls).toEqual(["finishTimelapse"]);
    expect(result).toEqual({ aborted: true, save: null, destination: null });
  });

  it("보관 중 홈 복귀하면 보관은 남기되 전이하지 않는다", async () => {
    let onResult = true;
    const h = harness({
      stillOnResult: () => onResult,
      save: async () => {
        onResult = false;
        return outcome();
      },
    });
    const result = await runResultNext(h.deps);

    expect(h.went).toEqual([]);
    expect(result.aborted).toBe(true);
    expect(result.save).not.toBeNull();
    expect(result.destination).toBeNull();
  });
});

describe("runResultNext — 실패 표현(흐름 중단 금지)", () => {
  it("보관 실패면 토스트 1회 + **전이는 계속**한다", async () => {
    const h = harness({ save: async () => outcome({ status: "failed", finalSaved: false }) });
    const result = await runResultNext(h.deps);

    expect(h.toasts).toEqual([{ kind: "error", message: STRINGS.save.failed }]);
    expect(result.destination).toBe("Qr");
    expect(h.went).toEqual(["Qr"]);
  });

  it("partial(타임랩스만 실패)에는 토스트가 없다", async () => {
    const h = harness({ save: async () => outcome({ status: "partial", timelapseSaved: false }) });
    await runResultNext(h.deps);
    expect(h.toasts).toEqual([]);
  });

  it("skipped(SaveLocalCopy off)에도 토스트가 없고 전이는 정상이다", async () => {
    // 게이트는 도메인(`planResultSave`)에 있다 — 화면은 설정을 그대로 넘기고 skip 판정을 받는다.
    const seen: ResultSaveInput[] = [];
    const h = harness({
      settings: () => ({ ...DEFAULT_SETTINGS, SaveLocalCopy: false }),
      save: async (input) => {
        seen.push(input);
        return outcome({ status: "skipped", finalSaved: false, folderName: null });
      },
    });
    const result = await runResultNext(h.deps);

    expect(seen[0]!.saveLocalCopy).toBe(false);
    expect(h.toasts).toEqual([]);
    expect(result.destination).toBe("Qr");
  });

  it("② 권한 문제(permission-required)는 손님 화면에 띄우지 않는다", async () => {
    const h = harness({ save: async () => outcome({ folderCopy: "permission-required" }) });
    await runResultNext(h.deps);
    expect(h.toasts).toEqual([]);
  });
});

describe("runResultNext — QR 분기(effective 판정)", () => {
  it("로그인 + QR 켜짐이면 Qr로 간다", async () => {
    const h = harness();
    expect((await runResultNext(h.deps)).destination).toBe("Qr");
  });

  it("게스트는 Done으로 간다(VF-11)", async () => {
    const h = harness({ isLoggedIn: () => false });
    expect((await runResultNext(h.deps)).destination).toBe("Done");
    expect(h.went).toEqual(["Done"]);
  });

  it("TempUser 한도 초과면 Done으로 간다", async () => {
    const h = harness({ isTempUserBlocked: () => true });
    expect((await runResultNext(h.deps)).destination).toBe("Done");
  });

  it("QR 설정이 꺼져 있으면 Done으로 간다", async () => {
    const h = harness({ settings: () => ({ ...DEFAULT_SETTINGS, EnableQrDelivery: false }) });
    expect((await runResultNext(h.deps)).destination).toBe("Done");
  });
});
