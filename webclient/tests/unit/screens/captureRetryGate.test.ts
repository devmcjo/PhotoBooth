import { describe, expect, it } from "vitest";
import { createCaptureRetryGate } from "@screens/capture/captureRetryGate";

/**
 * 촬영 진입 재시도 — 03 §6.3 [다시 시도]
 *
 * 게이트는 진입 절차의 **내용**을 모른다. 그래서 여기서는 실제 절차(03 §6.1)와 같은 **모양**의
 * 가짜 절차를 주입해, 재시도가 "카메라 시작 → Ready → 작업 공간 → 컷 루프"를 **처음부터**
 * 다시 태우는지를 이벤트 순서로 관측한다.
 */

interface FakeEntry {
  /** 진입 절차가 밟은 단계가 순서대로 쌓인다. */
  readonly events: string[];
  /** 다음 진입부터 카메라가 열리게 한다(다른 앱이 카메라를 놓은 상황). */
  allowCamera(): void;
  /** 컷 루프를 끝낸다(진행 중 재시도를 관측하려고 손으로 연다). */
  finishCutLoop(): void;
  run(): Promise<void>;
}

function fakeEntry(): FakeEntry {
  const events: string[] = [];
  let cameraOpens = false;
  let releaseCutLoop: (() => void) | null = null;

  return {
    events,
    allowCamera: () => {
      cameraOpens = true;
    },
    finishCutLoop: () => {
      releaseCutLoop?.();
      releaseCutLoop = null;
    },
    run: async () => {
      // 1. 카메라 시작 — 실패하면 시퀀스를 시작하지 않는다.
      events.push("camera.start");
      if (!cameraOpens) {
        events.push("failed");
        return;
      }
      // 2~4. Ready 대기 → 세션 작업 공간 → 타임랩스 수집
      events.push("ready");
      events.push("workspace");
      events.push("timelapse.start");
      // 5. 컷 루프 — 끝날 때까지 resolve하지 않는다(실제 절차와 같다).
      events.push("cutLoop.begin");
      await new Promise<void>((resolve) => {
        releaseCutLoop = resolve;
      });
      events.push("cutLoop.end");
    },
  };
}

/** 마이크로태스크 큐를 비운다(주입한 절차가 await 지점을 넘어가게 한다). */
async function flush(): Promise<void> {
  for (let i = 0; i < 4; i += 1) await Promise.resolve();
}

describe("captureRetryGate — [다시 시도]가 진입 절차를 처음부터 다시 태운다", () => {
  it("카메라 시작 실패 후 재시도하면 컷 루프까지 이어진다", async () => {
    const entry = fakeEntry();
    const gate = createCaptureRetryGate({
      run: entry.run,
      disposed: () => false,
      onError: () => undefined,
    });

    gate.start();
    await flush();
    // 최초 진입은 1단계에서 끝났다 — 시퀀스가 시작되지 않았다.
    expect(entry.events).toEqual(["camera.start", "failed"]);
    expect(gate.running).toBe(false);

    // 손님이 다른 앱을 닫고 [다시 시도]를 눌렀다.
    entry.allowCamera();
    gate.retry();
    await flush();

    // 부분 재개가 아니라 **1단계부터** 다시 밟았고, 컷 루프까지 도달했다.
    expect(entry.events).toEqual([
      "camera.start",
      "failed",
      "camera.start",
      "ready",
      "workspace",
      "timelapse.start",
      "cutLoop.begin",
    ]);
    expect(gate.running).toBe(true);

    entry.finishCutLoop();
    await flush();
    expect(entry.events.at(-1)).toBe("cutLoop.end");
    expect(gate.running).toBe(false);
  });

  it("진행 중 재시도는 무시된다 — 시퀀스가 두 개 생기지 않는다", async () => {
    const entry = fakeEntry();
    entry.allowCamera();
    const gate = createCaptureRetryGate({
      run: entry.run,
      disposed: () => false,
      onError: () => undefined,
    });

    gate.start();
    await flush();
    gate.retry();
    gate.retry();
    await flush();

    // 컷 루프가 도는 동안의 재시도는 두 번째 진입을 만들지 않는다.
    expect(entry.events.filter((e) => e === "camera.start")).toHaveLength(1);
    expect(entry.events.filter((e) => e === "cutLoop.begin")).toHaveLength(1);
  });

  it("화면을 벗어난 뒤의 재시도는 카메라를 되살리지 않는다", async () => {
    const entry = fakeEntry();
    let disposed = false;
    const gate = createCaptureRetryGate({
      run: entry.run,
      disposed: () => disposed,
      onError: () => undefined,
    });

    gate.start();
    await flush();
    disposed = true;
    entry.allowCamera();
    gate.retry();
    await flush();

    expect(entry.events).toEqual(["camera.start", "failed"]);
  });

  it("진입 절차가 예외로 끝나도 게이트가 잠기지 않고 오류가 보고된다", async () => {
    const errors: unknown[] = [];
    let attempt = 0;
    const gate = createCaptureRetryGate({
      run: async () => {
        attempt += 1;
        if (attempt === 1) throw new Error("boom");
        return;
      },
      disposed: () => false,
      onError: (err) => errors.push(err),
    });

    gate.start();
    await flush();
    // 미처리 rejection으로 새지 않고 보고 경로로 왔다.
    expect(errors).toHaveLength(1);
    expect(gate.running).toBe(false);

    // 플래그가 풀려 있어 다시 시도할 수 있다.
    gate.retry();
    await flush();
    expect(attempt).toBe(2);
  });
});
