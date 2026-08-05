import { beforeEach, describe, expect, it } from "vitest";
import { DEFAULT_SETTINGS, type AppSettingsValues } from "@domain/settings/appSettings";
import { isValidSessionId } from "@domain/upload/uploadContract";
import { overallProgress, resolveUploadTargets } from "@domain/upload/uploadOrchestration";
import {
  BackendError,
  NetworkError,
  TempUserLimitError,
  TEMP_USER_COUNT_EXCEEDED,
  TEMP_USER_TIME_EXCEEDED,
} from "@adapters/http/errors";
import type {
  CommitRequest,
  CommitResponse,
  PrepareRequest,
  PrepareResponse,
  SignedPutOutcome,
  SignedPutRequest,
  UploadGateway,
} from "@adapters/http/uploadGateway";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import {
  resolveUploadSessionId,
  runUpload,
  uploadFailureMessage,
  uploadStageLabel,
  type UploadPhase,
  type UploadRunDeps,
} from "@screens/qr/uploadRunner";
import type { FinalImageArtifact } from "@shell/sessionStore";
import { STRINGS } from "@ui/strings";

/**
 * 업로드 3단계 — 06 §4 · M7 · M8
 *
 * `runResultNext`와 같은 하네스 형태다(호출 로그 배열). React를 거치지 않고 순서를 고정한다.
 */

const CAPTURE_SESSION = "20260730_143022_a1b2c3d4-5e6f-4708-9a0b-1c2d3e4f5a6b";
const NEW_UUID = "9f8e7d6c-5b4a-4392-8271-0f1e2d3c4b5a";
const NOW = new Date(2026, 6, 30, 21, 5, 9);

const FINAL_IMAGE: FinalImageArtifact = {
  blob: new Blob([new Uint8Array(120)]),
  format: "Jpg",
};
const TIMELAPSE_BLOB = new Blob([new Uint8Array(4096)]);

interface Harness {
  readonly deps: UploadRunDeps;
  readonly calls: string[];
  readonly prepared: PrepareRequest[];
  readonly puts: SignedPutRequest[];
  readonly commits: CommitRequest[];
  readonly phases: UploadPhase[];
}

interface HarnessOptions {
  readonly settings?: Partial<AppSettingsValues>;
  readonly finalImage?: FinalImageArtifact | null;
  readonly timelapse?: Blob | null;
  readonly captureSessionId?: string | null;
  readonly attempt?: number;
  readonly signal?: AbortSignal;
  readonly onPrepare?: (request: PrepareRequest, index: number) => PrepareResponse | never;
  readonly onPut?: (request: SignedPutRequest, index: number) => SignedPutOutcome;
  readonly onCommit?: (request: CommitRequest) => CommitResponse | never;
  /** 각 gateway 호출 직전에 실행(취소 시나리오 주입용). */
  readonly beforeCall?: (call: string) => void;
}

function preparedResponse(request: PrepareRequest): PrepareResponse {
  return {
    bucket: "mcphoto-955fb.firebasestorage.app",
    uploads: request.files.map((file) => ({
      kind: file.kind,
      putUrl: `https://storage.example/signed/${file.kind}?sig=abc`,
      downloadUrl: `https://firebasestorage.example/o/results%2F${file.kind}.${file.ext}?alt=media&token=tk-${file.kind}`,
      requiredHeaders: {
        "Content-Type": file.contentType,
        "x-goog-meta-firebaseStorageDownloadTokens": `tk-${file.kind}`,
      },
    })),
  };
}

function harness(options: HarnessOptions = {}): Harness {
  const calls: string[] = [];
  const prepared: PrepareRequest[] = [];
  const puts: SignedPutRequest[] = [];
  const commits: CommitRequest[] = [];
  const phases: UploadPhase[] = [];

  const gateway: UploadGateway = {
    async prepare(request) {
      options.beforeCall?.(`prepare:${request.files[0]?.kind ?? "?"}`);
      calls.push(`prepare:${request.files[0]?.kind ?? "?"}`);
      prepared.push(request);
      return options.onPrepare?.(request, prepared.length - 1) ?? preparedResponse(request);
    },
    async put(request) {
      options.beforeCall?.(`put:${request.kind ?? "?"}`);
      calls.push(`put:${request.kind ?? "?"}`);
      puts.push(request);
      return (
        options.onPut?.(request, puts.length - 1) ?? {
          ok: true,
          status: 200,
          bytes: request.body.size,
          elapsedMs: 5,
        }
      );
    },
    async commit(request) {
      options.beforeCall?.("commit");
      calls.push("commit");
      commits.push(request);
      return (
        options.onCommit?.(request) ?? {
          id: request.sessionId,
          finalImageUrl: request.finalImageUrl,
          timelapseUrl: request.timelapseUrl,
          createdAt: "2026-07-30T12:05:09Z",
          expiresAt: "2026-07-31T12:05:09Z",
          downloadPageUrl: request.downloadPageUrl,
        }
      );
    },
  };

  const deps: UploadRunDeps = {
    gateway,
    finalImage: () => (options.finalImage === undefined ? FINAL_IMAGE : options.finalImage),
    timelapse: () => (options.timelapse === undefined ? TIMELAPSE_BLOB : options.timelapse),
    settings: () => ({ ...DEFAULT_SETTINGS, ...options.settings }),
    captureSessionId: () =>
      options.captureSessionId === undefined ? CAPTURE_SESSION : options.captureSessionId,
    attempt: options.attempt ?? 0,
    onPhase: (phase) => phases.push(phase),
    now: () => NOW,
    uuid: () => NEW_UUID,
    ...(options.signal === undefined ? {} : { signal: options.signal }),
  };

  return { deps, calls, prepared, puts, commits, phases };
}

beforeEach(() => {
  detachLogStore();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

describe("runUpload — 3단계 순서", () => {
  it("사진 + 영상은 prepare→put을 파일당 반복한 뒤 commit 1회다", async () => {
    const h = harness();
    const result = await runUpload(h.deps);

    expect(h.calls).toEqual([
      "prepare:final",
      "put:final",
      "prepare:timelapse",
      "put:timelapse",
      "commit",
    ]);
    expect(result.aborted).toBe(false);
    expect(result.phase.kind).toBe("succeeded");
  });

  it("prepare는 파일당 1회씩 부른다(06 §4.1)", async () => {
    const h = harness();
    await runUpload(h.deps);

    expect(h.prepared).toHaveLength(2);
    expect(h.prepared[0]!.files).toEqual([
      { kind: "final", ext: "jpg", contentType: "image/jpeg" },
    ]);
    expect(h.prepared[1]!.files).toEqual([
      { kind: "timelapse", ext: "mp4", contentType: "video/mp4" },
    ]);
  });

  it("합성 당시 포맷을 쓴다(설정이 그 뒤에 바뀌어도 바이트와 어긋나지 않는다)", async () => {
    const h = harness({
      finalImage: { blob: FINAL_IMAGE.blob, format: "Png" },
      // 설정은 Jpg로 되돌아갔지만 이미 만들어진 바이트는 PNG다.
      settings: { OutputFormat: "Jpg", SendTimelapse: false },
    });
    await runUpload(h.deps);

    expect(h.prepared[0]!.files[0]).toEqual({
      kind: "final",
      ext: "png",
      contentType: "image/png",
    });
  });

  it("commit에 prepare가 준 downloadUrl을 문자 단위로 그대로 넘긴다", async () => {
    const h = harness();
    await runUpload(h.deps);

    expect(h.commits[0]!.finalImageUrl).toBe(
      "https://firebasestorage.example/o/results%2Ffinal.jpg?alt=media&token=tk-final",
    );
    expect(h.commits[0]!.timelapseUrl).toBe(
      "https://firebasestorage.example/o/results%2Ftimelapse.mp4?alt=media&token=tk-timelapse",
    );
  });

  it("PUT에 prepare의 requiredHeaders를 그대로 넘긴다(M14는 어댑터가 순회한다)", async () => {
    const h = harness();
    await runUpload(h.deps);

    expect(h.puts[0]!.headers).toEqual({
      "Content-Type": "image/jpeg",
      "x-goog-meta-firebaseStorageDownloadTokens": "tk-final",
    });
    expect(h.puts[0]!.url).toBe("https://storage.example/signed/final?sig=abc");
    expect(h.puts[0]!.body).toBe(FINAL_IMAGE.blob);
  });
});

describe("runUpload — 전송 대상 확정 (M7)", () => {
  it("둘 다 없으면 gateway를 한 번도 부르지 않는다", async () => {
    const h = harness({ finalImage: null, timelapse: null });
    const result = await runUpload(h.deps);

    expect(h.calls).toEqual([]);
    expect(result.phase).toEqual({ kind: "nothing" });
    expect(h.phases).toEqual([{ kind: "nothing" }]);
  });

  it("토글이 둘 다 꺼져 있으면 파일이 있어도 올리지 않는다", async () => {
    const h = harness({ settings: { SendPhoto: false, SendTimelapse: false } });
    const result = await runUpload(h.deps);

    expect(h.calls).toEqual([]);
    expect(result.phase.kind).toBe("nothing");
  });

  it("사진만: prepare 1회 · commit의 timelapseUrl이 null이다", async () => {
    const h = harness({ timelapse: null });
    await runUpload(h.deps);

    expect(h.calls).toEqual(["prepare:final", "put:final", "commit"]);
    expect(h.commits[0]!.finalImageUrl).not.toBeNull();
    expect(h.commits[0]!.timelapseUrl).toBeNull();
  });

  it("영상만(SendPhoto off): commit의 finalImageUrl이 null이다", async () => {
    const h = harness({ settings: { SendPhoto: false } });
    await runUpload(h.deps);

    expect(h.calls).toEqual(["prepare:timelapse", "put:timelapse", "commit"]);
    expect(h.commits[0]!.finalImageUrl).toBeNull();
    expect(h.commits[0]!.timelapseUrl).not.toBeNull();
  });

  it("commit의 두 URL이 동시에 null이 되는 경로가 없다", async () => {
    for (const options of [
      {},
      { timelapse: null },
      { settings: { SendPhoto: false } },
      { settings: { SendTimelapse: false } },
    ]) {
      const h = harness(options);
      await runUpload(h.deps);
      if (h.commits.length === 0) continue;
      const { finalImageUrl, timelapseUrl } = h.commits[0]!;
      expect(finalImageUrl !== null || timelapseUrl !== null).toBe(true);
    }
  });
});

describe("runUpload — 실패 시 commit 금지 (M8)", () => {
  it("사진 PUT이 실패하면 commit을 부르지 않는다", async () => {
    const h = harness({
      onPut: () => ({ ok: false, failure: "http", status: 403, elapsedMs: 3 }),
    });
    const result = await runUpload(h.deps);

    expect(h.calls).toEqual(["prepare:final", "put:final"]);
    expect(h.commits).toHaveLength(0);
    expect(result.phase).toEqual({ kind: "failed", reason: "server" });
  });

  it("영상 PUT만 실패해도 사진만 commit하지 않는다(실패 은폐 금지)", async () => {
    const h = harness({
      onPut: (request) =>
        request.kind === "timelapse"
          ? { ok: false, failure: "network", status: null, elapsedMs: 3 }
          : { ok: true, status: 200, bytes: request.body.size, elapsedMs: 3 },
    });
    const result = await runUpload(h.deps);

    expect(h.commits).toHaveLength(0);
    expect(result.phase).toEqual({ kind: "failed", reason: "network" });
  });

  it("prepare 응답에 요청한 파일이 없으면 server 실패다", async () => {
    const h = harness({
      onPrepare: () => ({ bucket: "b", uploads: [] }),
    });
    const result = await runUpload(h.deps);

    expect(h.calls).toEqual(["prepare:final"]);
    expect(result.phase).toEqual({ kind: "failed", reason: "server" });
  });
});

describe("runUpload — 오류 매핑 (06 §3)", () => {
  it("TempUser 시간 초과는 temp-user-time이고 put·commit이 0회다", async () => {
    const h = harness({
      onPrepare: () => {
        throw new TempUserLimitError("초과", 403, TEMP_USER_TIME_EXCEEDED, "time");
      },
    });
    const result = await runUpload(h.deps);

    expect(result.phase).toEqual({ kind: "failed", reason: "temp-user-time" });
    expect(h.calls).toEqual(["prepare:final"]);
  });

  it("TempUser 횟수 소진은 temp-user-count다", async () => {
    const h = harness({
      onPrepare: () => {
        throw new TempUserLimitError("소진", 403, TEMP_USER_COUNT_EXCEEDED, "count");
      },
    });
    expect((await runUpload(h.deps)).phase).toEqual({
      kind: "failed",
      reason: "temp-user-count",
    });
  });

  it("응답 없음은 network다", async () => {
    const h = harness({
      onPrepare: () => {
        throw new NetworkError("연결 실패");
      },
    });
    expect((await runUpload(h.deps)).phase).toEqual({ kind: "failed", reason: "network" });
  });

  it("PUT 타임아웃도 network다", async () => {
    const h = harness({
      onPut: () => ({ ok: false, failure: "timeout", status: null, elapsedMs: 100_000 }),
    });
    expect((await runUpload(h.deps)).phase).toEqual({ kind: "failed", reason: "network" });
  });

  it("commit 409는 conflict다(이중 실행 의심)", async () => {
    const h = harness({
      onCommit: () => {
        throw new BackendError("중복", 409, "conflict");
      },
    });
    expect((await runUpload(h.deps)).phase).toEqual({ kind: "failed", reason: "conflict" });
  });

  it("알 수 없는 예외는 server다", async () => {
    const h = harness({
      onCommit: () => {
        throw new Error("무슨 일이지");
      },
    });
    expect((await runUpload(h.deps)).phase).toEqual({ kind: "failed", reason: "server" });
  });
});

describe("runUpload — 세션 ID (06 §4.4)", () => {
  it("최초 시도는 촬영 세션 ID를 재사용한다", async () => {
    const h = harness();
    await runUpload(h.deps);
    expect(h.commits[0]!.sessionId).toBe(CAPTURE_SESSION);
  });

  it("[재시도]는 새 세션 ID로 전 과정을 다시 한다", async () => {
    const h = harness({ attempt: 1 });
    await runUpload(h.deps);

    const used = h.commits[0]!.sessionId;
    expect(used).not.toBe(CAPTURE_SESSION);
    expect(isValidSessionId(used)).toBe(true);
    expect(h.prepared.every((p) => p.sessionId === used)).toBe(true);
  });

  it("촬영 세션 ID가 없거나 형식 위반이면 새로 만든다", () => {
    expect(resolveUploadSessionId(null, 0, NOW, NEW_UUID)).toBe(`20260730_210509_${NEW_UUID}`);
    expect(resolveUploadSessionId("nope", 0, NOW, NEW_UUID)).toBe(`20260730_210509_${NEW_UUID}`);
    expect(resolveUploadSessionId(CAPTURE_SESSION, 0, NOW, NEW_UUID)).toBe(CAPTURE_SESSION);
    expect(resolveUploadSessionId(CAPTURE_SESSION, 2, NOW, NEW_UUID)).toBe(
      `20260730_210509_${NEW_UUID}`,
    );
  });

  it("결과물은 재합성하지 않고 같은 Blob을 다시 올린다", async () => {
    const h = harness({ attempt: 3 });
    await runUpload(h.deps);
    expect(h.puts[0]!.body).toBe(FINAL_IMAGE.blob);
  });
});

describe("runUpload — downloadPageUrl (P1 도메인)", () => {
  it("{HostingBaseUrl}/?s={sessionId} 형태다", async () => {
    const h = harness({ settings: { HostingBaseUrl: "https://mcphoto-955fb.web.app" } });
    await runUpload(h.deps);

    expect(h.commits[0]!.downloadPageUrl).toBe(
      `https://mcphoto-955fb.web.app/?s=${CAPTURE_SESSION}`,
    );
  });

  it("설정값을 그대로 쓴다 — 조립 함수가 도메인을 바꾸지 않는다", async () => {
    const h = harness({ settings: { HostingBaseUrl: "https://example.test/base/" } });
    await runUpload(h.deps);

    // 트레일링 슬래시만 제거하고 도메인·경로는 손대지 않는다.
    expect(h.commits[0]!.downloadPageUrl).toBe(`https://example.test/base/?s=${CAPTURE_SESSION}`);
  });

  it("성공 phase가 서버가 돌려준 값을 우선한다", async () => {
    const h = harness({
      onCommit: (request) => ({
        id: request.sessionId,
        finalImageUrl: request.finalImageUrl,
        timelapseUrl: request.timelapseUrl,
        createdAt: "",
        expiresAt: "",
        downloadPageUrl: "https://server.decided/?s=x",
      }),
    });
    const result = await runUpload(h.deps);

    expect(result.phase).toMatchObject({
      kind: "succeeded",
      downloadPageUrl: "https://server.decided/?s=x",
      retentionHours: DEFAULT_SETTINGS.RetentionHours,
    });
  });

  it("서버가 빈 값을 주면 로컬 조립값으로 되돌린다", async () => {
    const h = harness({
      onCommit: (request) => ({
        id: request.sessionId,
        finalImageUrl: request.finalImageUrl,
        timelapseUrl: request.timelapseUrl,
        createdAt: "",
        expiresAt: "",
        downloadPageUrl: "",
      }),
    });
    const result = await runUpload(h.deps);

    expect(result.phase).toMatchObject({
      downloadPageUrl: `${DEFAULT_SETTINGS.HostingBaseUrl}/?s=${CAPTURE_SESSION}`,
    });
  });
});

describe("runUpload — 진행률", () => {
  it("초기 진행률은 불확정이다(06 §4.5)", async () => {
    const h = harness();
    await runUpload(h.deps);

    const first = h.phases.find((p) => p.kind === "uploading");
    expect(first).toMatchObject({ progress: null });
  });

  it("도메인 합산과 같은 값을 통지하고 [0,1]을 벗어나지 않는다", async () => {
    const both = resolveUploadTargets({
      sendPhoto: true,
      sendTimelapse: true,
      hasFinalImage: true,
      hasTimelapse: true,
    });
    const h = harness({
      onPut: (request) => {
        request.onProgress?.({ loaded: 50, total: 100 });
        return { ok: true, status: 200, bytes: request.body.size, elapsedMs: 1 };
      },
    });
    await runUpload(h.deps);

    const values = h.phases
      .filter((p): p is Extract<UploadPhase, { kind: "uploading" }> => p.kind === "uploading")
      .map((p) => p.progress)
      .filter((v): v is number => v !== null);

    expect(values).toContain(overallProgress(both, "Photo", 0.5));
    expect(values).toContain(overallProgress(both, "Timelapse", 0.5));
    for (const value of values) {
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThanOrEqual(1);
    }
  });

  it("total이 0이면 0으로 본다(0 나누기 방지)", async () => {
    const h = harness({
      timelapse: null,
      onPut: (request) => {
        request.onProgress?.({ loaded: 0, total: 0 });
        return { ok: true, status: 200, bytes: 0, elapsedMs: 1 };
      },
    });
    await expect(runUpload(h.deps)).resolves.toMatchObject({ aborted: false });
  });
});

describe("runUpload — 취소", () => {
  it("사전 취소면 아무 요청도 나가지 않는다", async () => {
    const controller = new AbortController();
    controller.abort();
    const h = harness({ signal: controller.signal });

    const result = await runUpload(h.deps);

    expect(result).toEqual({ phase: { kind: "idle" }, aborted: true });
    expect(h.calls).toEqual([]);
    expect(h.phases).toEqual([]);
  });

  it("첫 PUT 뒤 취소하면 commit이 0회다", async () => {
    const controller = new AbortController();
    const h = harness({
      signal: controller.signal,
      beforeCall: (call) => {
        if (call === "prepare:timelapse") controller.abort();
      },
    });

    const result = await runUpload(h.deps);

    expect(result.aborted).toBe(true);
    expect(h.commits).toHaveLength(0);
  });

  it("PUT이 aborted를 돌려주면 실패가 아니라 취소다", async () => {
    const controller = new AbortController();
    const h = harness({
      signal: controller.signal,
      onPut: () => {
        controller.abort();
        return { ok: false, failure: "aborted", status: null, elapsedMs: 1 };
      },
    });

    const result = await runUpload(h.deps);

    expect(result.aborted).toBe(true);
    expect(result.phase.kind).not.toBe("failed");
    expect(h.commits).toHaveLength(0);
  });

  it("취소 중 진행률 콜백이 와도 phase를 더 밀지 않는다", async () => {
    const controller = new AbortController();
    const h = harness({
      timelapse: null,
      onPut: (request) => {
        controller.abort();
        request.onProgress?.({ loaded: 10, total: 100 });
        return { ok: false, failure: "aborted", status: null, elapsedMs: 1 };
      },
      signal: controller.signal,
    });

    await runUpload(h.deps);

    // 취소 이후 통지된 uploading phase가 없어야 한다(초기 불확정 1건만 남는다).
    expect(h.phases.filter((p) => p.kind === "uploading" && p.progress !== null)).toEqual([]);
  });
});

describe("uploadFailureMessage · uploadStageLabel — 문구표 (03 §9.2 · 06 §4.5)", () => {
  it("TempUser 사유는 로컬 저장 토글과 무관하다", () => {
    for (const saveLocalCopy of [true, false]) {
      expect(uploadFailureMessage("temp-user-time", saveLocalCopy)).toBe(
        STRINGS.upload.tempUserTimeExceeded,
      );
      expect(uploadFailureMessage("temp-user-count", saveLocalCopy)).toBe(
        STRINGS.upload.tempUserCountExceeded,
      );
    }
  });

  it("그 밖의 실패는 로컬 저장 토글로 갈린다", () => {
    for (const reason of ["network", "conflict", "server"] as const) {
      expect(uploadFailureMessage(reason, true)).toBe(STRINGS.upload.failedSaved);
      expect(uploadFailureMessage(reason, false)).toBe(STRINGS.upload.failedNotSaved);
    }
  });

  it("단계 라벨이 카탈로그 문구와 같다", () => {
    expect(uploadStageLabel("Photo")).toBe("사진 업로드 중");
    expect(uploadStageLabel("Timelapse")).toBe("영상 업로드 중");
    expect(uploadStageLabel("Finalizing")).toBe("마무리 중");
  });

  it("성공 고지가 카탈로그 문구 전체다", () => {
    expect(STRINGS.upload.retentionNotice).toBe("업로드된 사진·영상은 {n}시간 후 자동 삭제됩니다.");
    expect(STRINGS.upload.inProgress).toBe("업로드 중...");
  });
});
