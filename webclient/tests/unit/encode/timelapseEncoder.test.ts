import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  encodeTimelapse,
  type EncodeTimelapseDeps,
} from "@adapters/encode/timelapseEncoder";
import type { EncodeClient, EncodeOutcome } from "@adapters/encode/encodeClient";
import type { EncodeJob, EncodeStats } from "@adapters/encode/encodeProtocol";
import type { EncoderProbe } from "@adapters/encode/encoderSupport";
import type { SessionWorkspace } from "@adapters/storage/sessionWorkspace";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 오케스트레이터 — 가짜 판정·클라이언트·작업 공간으로 검증한다.
 */

const STATS: EncodeStats = {
  encodedFrames: 375,
  droppedFrames: 0,
  skippedFrames: 0,
  elapsedMs: 1200,
};

function workspaceWith(frameCount: number): SessionWorkspace {
  const names = Array.from({ length: frameCount }, (_, i) => `${String(i).padStart(5, "0")}.jpg`);
  return {
    sessionId: "s-1",
    writeCut: async () => true,
    writeTimelapseFrame: async () => true,
    listTimelapseFrames: async () => names,
    removeTimelapseFrame: async () => true,
    writeComposed: async () => true,
    readFile: async () => new File(["jpeg"], "x.jpg"),
    discard: async () => true,
  };
}

function probeOf(
  path: EncoderProbe["path"],
  codec: string | null = null,
): EncodeTimelapseDeps["detect"] {
  return async () => ({ path, codec, reason: `테스트 ${path}`, probed: [] });
}

interface FakeClient extends EncodeClient {
  readonly jobs: EncodeJob[];
}

function clientWith(outcome: EncodeOutcome): FakeClient {
  const jobs: EncodeJob[] = [];
  return {
    jobs,
    async run(job) {
      jobs.push(job);
      return outcome;
    },
    abort() {
      // 이 테스트에서는 중단 경로를 쓰지 않는다.
    },
  };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("encodeTimelapse — null 계약(VF-6)", () => {
  it("스풀이 0장이면 null이다(인코더를 부르지 않는다)", async () => {
    const client = clientWith({ blob: new Blob(), stats: STATS });
    const result = await encodeTimelapse(
      { workspace: workspaceWith(0), actualSeconds: 38, size: { width: 810, height: 1080 } },
      { detect: probeOf("webcodecs", "avc1.42001E"), client },
    );
    expect(result).toBeNull();
    expect(client.jobs).toHaveLength(0);
  });

  it("선별 프레임이 부족하면 null이다", async () => {
    const client = clientWith({ blob: new Blob(), stats: STATS });
    const result = await encodeTimelapse(
      { workspace: workspaceWith(20), actualSeconds: 38, size: { width: 810, height: 1080 } },
      { detect: probeOf("webcodecs", "avc1.42001E"), client },
    );
    expect(result).toBeNull();
    expect(client.jobs).toHaveLength(0);
  });

  it("경로가 none이면 null이고 **Worker를 띄우지 않는다**", async () => {
    const client = clientWith({ blob: new Blob(), stats: STATS });
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      { detect: probeOf("none"), client },
    );
    expect(result).toBeNull();
    expect(client.jobs).toHaveLength(0);
  });

  it("인코딩 실패도 null이다(예외가 아니다)", async () => {
    const client = clientWith({ error: "인코더 설정 거부", stats: STATS });
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      { detect: probeOf("webcodecs", "avc1.42001E"), client },
    );
    expect(result).toBeNull();
  });

  it("작업 공간이 던져도 예외가 새지 않는다", async () => {
    const broken: SessionWorkspace = {
      ...workspaceWith(0),
      listTimelapseFrames: async () => {
        throw new Error("OPFS 붕괴");
      },
    };
    const result = await encodeTimelapse(
      { workspace: broken, actualSeconds: 38, size: { width: 810, height: 1080 } },
      { detect: probeOf("webcodecs", "avc1.42001E") },
    );
    expect(result).toBeNull();
  });
});

describe("encodeTimelapse — 경로 B(Worker)", () => {
  it("성공하면 결과 메타를 채운다", async () => {
    const blob = new Blob([new Uint8Array(2048)], { type: "video/mp4" });
    const client = clientWith({ blob, stats: STATS });
    let clock = 0;
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 811, height: 1081 } },
      {
        detect: probeOf("webcodecs", "avc1.42001E"),
        client,
        now: () => (clock += 100),
      },
    );

    expect(result).not.toBeNull();
    expect(result!.path).toBe("webcodecs");
    // 811×1081 → 짝수 클램프
    expect(result!.width).toBe(810);
    expect(result!.height).toBe(1080);
    expect(result!.frameCount).toBe(375);
    expect(result!.durationSec).toBeCloseTo(12.5, 10);
    expect(result!.speedFactor).toBeCloseTo(3.04, 10);
    expect(result!.bytes).toBe(2048);
    expect(result!.elapsedMs).toBeGreaterThan(0);
  });

  it("job의 파일명이 선별 인덱스 순서대로 매핑된다", async () => {
    const client = clientWith({ blob: new Blob(["x"]), stats: STATS });
    await encodeTimelapse(
      { workspace: workspaceWith(60), actualSeconds: 5, size: { width: 810, height: 1080 } },
      { detect: probeOf("webcodecs", "avc1.42001E"), client },
    );

    const job = client.jobs[0]!;
    // 5초 세션 → target 150, 스풀 60 → 전량 선별.
    expect(job.names).toHaveLength(60);
    expect(job.names[0]).toBe("00000.jpg");
    expect(job.names[59]).toBe("00059.jpg");
    expect([...job.names].sort()).toEqual([...job.names]);
    expect(job.timestampsUs[0]).toBe(0);
    expect(job.dirPath).toBe("sessions/s-1/tl");
  });

  it("job.config의 크기가 짝수이고 비트레이트가 표값이다", async () => {
    const client = clientWith({ blob: new Blob(["x"]), stats: STATS });
    await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 1443, height: 1081 } },
      { detect: probeOf("webcodecs", "avc1.4D001E"), client },
    );
    const config = client.jobs[0]!.config;
    expect(config.width % 2).toBe(0);
    expect(config.height % 2).toBe(0);
    expect(config).toEqual({
      codec: "avc1.4D001E",
      width: 1442,
      height: 1080,
      bitrate: Math.round(1442 * 1080 * 30 * 0.12),
      framerate: 30,
    });
  });

  it("**경로 B 실패 시 경로 A로 재시도하지 않는다**(설계 결정)", async () => {
    const client = clientWith({ error: "하드웨어 인코더 오류", stats: STATS });
    let mediaRecorderCalls = 0;
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      {
        detect: probeOf("webcodecs", "avc1.42001E"),
        client,
        runMediaRecorder: async () => {
          mediaRecorderCalls++;
          return { ok: false, reason: "unused", stats: STATS };
        },
      },
    );
    expect(result).toBeNull();
    expect(mediaRecorderCalls).toBe(0);
  });
});

describe("encodeTimelapse — 경로 A(메인 스레드)", () => {
  it("경로 A가 선택되면 Worker 클라이언트를 부르지 않는다", async () => {
    const client = clientWith({ blob: new Blob(["worker"]), stats: STATS });
    const blob = new Blob([new Uint8Array(512)], { type: "video/mp4" });
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      {
        detect: probeOf("mediarecorder"),
        client,
        runMediaRecorder: async () => ({ ok: true, blob, stats: { ...STATS, encodedFrames: 370 } }),
      },
    );

    expect(client.jobs).toHaveLength(0);
    expect(result).not.toBeNull();
    expect(result!.path).toBe("mediarecorder");
    expect(result!.frameCount).toBe(370);
    expect(result!.bytes).toBe(512);
  });

  it("경로 A가 코덱 없이 판정돼도 기본 코덱으로 config를 채운다", async () => {
    let seen: EncodeJob | null = null;
    await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      {
        detect: probeOf("mediarecorder"),
        runMediaRecorder: async (job) => {
          seen = job;
          return { ok: true, blob: new Blob(["x"]), stats: STATS };
        },
      },
    );
    expect(seen).not.toBeNull();
    expect(seen!.config.codec).toBe("avc1.42001E");
  });

  it("경로 A 실패도 null이다", async () => {
    const result = await encodeTimelapse(
      { workspace: workspaceWith(570), actualSeconds: 38, size: { width: 810, height: 1080 } },
      {
        detect: probeOf("mediarecorder"),
        runMediaRecorder: async () => ({ ok: false, reason: "레코더 정지 실패", stats: STATS }),
      },
    );
    expect(result).toBeNull();
  });
});
