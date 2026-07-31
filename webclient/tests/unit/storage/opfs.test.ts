import { describe, expect, it, vi } from "vitest";
import {
  createOpfsClient,
  purgeSessionLeftovers,
  UNSUPPORTED_OPFS_CLIENT,
  type WorkerLike,
} from "@adapters/storage/opfsClient";
import {
  OPFS_DIRS,
  splitOpfsPath,
  splitParentAndName,
  type OpfsRequest,
  type OpfsResponse,
  type OpfsUsage,
} from "@adapters/storage/opfsProtocol";
import {
  createSessionWorkspace,
  cutFileName,
  timelapseFrameName,
} from "@adapters/storage/sessionWorkspace";

/** 요청을 기록하고 지정한 응답을 돌려주는 가짜 Worker. */
class FakeWorker implements WorkerLike {
  readonly requests: OpfsRequest[] = [];
  private listener: ((event: MessageEvent<OpfsResponse>) => void) | null = null;
  /** 요청별 응답을 만드는 함수. 기본은 성공. */
  respond: (request: OpfsRequest) => OpfsResponse | null = (r) => ({ id: r.id, ok: true });

  postMessage(message: OpfsRequest): void {
    this.requests.push(message);
    const response = this.respond(message);
    if (response !== null) {
      // 실제 Worker처럼 비동기로 돌려준다.
      queueMicrotask(() => this.listener?.({ data: response } as MessageEvent<OpfsResponse>));
    }
  }
  addEventListener(_type: "message", listener: (event: MessageEvent<OpfsResponse>) => void): void {
    this.listener = listener;
  }
}

describe("opfsProtocol — 경로 방어", () => {
  it("경로를 세그먼트로 나눈다", () => {
    expect(splitOpfsPath("sessions/abc/cut1.jpg")).toEqual(["sessions", "abc", "cut1.jpg"]);
    expect(splitOpfsPath("/leading/slash/")).toEqual(["leading", "slash"]);
    expect(splitOpfsPath("a//b")).toEqual(["a", "b"]);
  });

  it("상대 참조를 거부한다 — OPFS 루트 밖을 건드리지 못하게", () => {
    expect(() => splitOpfsPath("../secrets")).toThrow();
    expect(() => splitOpfsPath("a/../../b")).toThrow();
    expect(() => splitOpfsPath("./a")).toThrow();
  });

  it("빈 경로를 거부한다", () => {
    expect(() => splitOpfsPath("")).toThrow();
    expect(() => splitOpfsPath("///")).toThrow();
  });

  it("부모와 파일명을 분리한다", () => {
    expect(splitParentAndName("sessions/x/cut1.jpg")).toEqual({
      dirs: ["sessions", "x"],
      name: "cut1.jpg",
    });
    expect(splitParentAndName("top")).toEqual({ dirs: [], name: "top" });
  });
});

describe("opfsClient — Worker RPC", () => {
  it("write는 버퍼를 넘기고 성공 여부를 돌려준다", async () => {
    const worker = new FakeWorker();
    const client = createOpfsClient(worker);

    const ok = await client.write("sessions/s1/cut1.jpg", new Uint8Array([1, 2, 3]));
    expect(ok).toBe(true);
    expect(worker.requests[0]!.op).toBe("write");
    expect((worker.requests[0] as { path: string }).path).toBe("sessions/s1/cut1.jpg");
  });

  it("Worker가 실패를 보고하면 false다(예외를 전파하지 않는다 — 01 §2.1)", async () => {
    const worker = new FakeWorker();
    worker.respond = (r) => ({ id: r.id, ok: false, error: "NoModificationAllowedError" });
    const client = createOpfsClient(worker);

    expect(await client.write("a/b.jpg", new Uint8Array([1]))).toBe(false);
    expect(await client.remove("a/b.jpg")).toBe(false);
    expect(await client.exists("a/b.jpg")).toBe(false);
    expect(await client.list("a")).toEqual([]);
    expect(await client.capability()).toBe("none");
  });

  it("postMessage가 던져도 false로 축소된다", async () => {
    const worker = new FakeWorker();
    worker.postMessage = () => {
      throw new Error("worker dead");
    };
    expect(await createOpfsClient(worker).write("a/b.jpg", new Uint8Array([1]))).toBe(false);
  });

  it("응답이 오지 않으면 타임아웃으로 false다(영구 대기 금지)", async () => {
    vi.useFakeTimers();
    try {
      const worker = new FakeWorker();
      worker.respond = () => null; // 응답 없음
      const client = createOpfsClient(worker);
      const promise = client.write("a/b.jpg", new Uint8Array([1]));
      await vi.advanceTimersByTimeAsync(15_001);
      expect(await promise).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it("요청 id가 증가하고 응답이 요청과 짝지어진다", async () => {
    const worker = new FakeWorker();
    worker.respond = (r) => ({ id: r.id, ok: true, value: r.op === "list" ? ["x"] : undefined });
    const client = createOpfsClient(worker);

    const [list, exists] = await Promise.all([client.list("sessions"), client.exists("sessions/x")]);
    expect(list).toEqual(["x"]);
    expect(exists).toBe(false); // exists 응답의 value가 true가 아니다
    expect(worker.requests.map((r) => r.id)).toEqual([1, 2]);
  });

  it("Blob도 받아 ArrayBuffer로 변환한다", async () => {
    const worker = new FakeWorker();
    const client = createOpfsClient(worker);
    expect(await client.write("a/b.jpg", new Blob([new Uint8Array([9])]))).toBe(true);
    expect((worker.requests[0] as { bytes: ArrayBuffer }).bytes.byteLength).toBe(1);
  });

  it("미지원 클라이언트는 모든 쓰기가 false다(조용한 성공 금지)", async () => {
    expect(await UNSUPPORTED_OPFS_CLIENT.write("a", new Uint8Array())).toBe(false);
    expect(await UNSUPPORTED_OPFS_CLIENT.capability()).toBe("none");
    expect(await UNSUPPORTED_OPFS_CLIENT.readFile("a")).toBeNull();
    expect(await UNSUPPORTED_OPFS_CLIENT.usage("results")).toEqual({ totalBytes: 0, entries: [] });
  });
});

describe("opfsClient — usage op (05 §5.4 용량 정책)", () => {
  function usageWorker(value: unknown, ok = true): FakeWorker {
    const worker = new FakeWorker();
    worker.respond = (r) =>
      ok ? { id: r.id, ok: true, value } : { id: r.id, ok: false, error: "NotFoundError" };
    return worker;
  }

  it("경로를 담은 usage 요청을 보낸다", async () => {
    const worker = usageWorker({ totalBytes: 0, entries: [] });
    await createOpfsClient(worker).usage(OPFS_DIRS.results);
    expect(worker.requests[0]!.op).toBe("usage");
    expect((worker.requests[0] as { path: string }).path).toBe("results");
  });

  it("성공 응답을 그대로 돌려준다", async () => {
    const value: OpfsUsage = {
      totalBytes: 300,
      entries: [
        { name: "mcphoto_260720_1445", kind: "directory", bytes: 200, fileCount: 2 },
        { name: "stray.txt", kind: "file", bytes: 100, fileCount: 1 },
      ],
    };
    expect(await createOpfsClient(usageWorker(value)).usage("results")).toEqual(value);
  });

  it("실패 응답은 빈 결과로 축소된다(예외 없음 — 정리를 덜 하는 안전한 방향)", async () => {
    expect(await createOpfsClient(usageWorker(null, false)).usage("results")).toEqual({
      totalBytes: 0,
      entries: [],
    });
  });

  it("형태가 어긋난 응답도 빈 결과로 축소된다", async () => {
    expect(await createOpfsClient(usageWorker({ totalBytes: "많음" })).usage("results")).toEqual({
      totalBytes: 0,
      entries: [],
    });
    expect(await createOpfsClient(usageWorker(undefined)).usage("results")).toEqual({
      totalBytes: 0,
      entries: [],
    });
  });

  it("400 엔트리 응답을 왕복 1회로 그대로 통과시킨다(A1 규모)", async () => {
    const entries = Array.from({ length: 400 }, (_, i) => ({
      name: `mcphoto_2607${String(1 + (i % 28)).padStart(2, "0")}_${String(i).padStart(4, "0")}`,
      kind: "directory" as const,
      bytes: 1024,
      fileCount: 2,
    }));
    const worker = usageWorker({ totalBytes: 400 * 1024, entries });
    const result = await createOpfsClient(worker).usage("results");
    expect(result.entries).toHaveLength(400);
    expect(result.totalBytes).toBe(409_600);
    expect(worker.requests).toHaveLength(1);
  });
});

describe("purgeSessionLeftovers — analysis/41 §4", () => {
  it("sessions/ 하위만 재귀 삭제하고 개수를 돌려준다", async () => {
    const removed: string[] = [];
    const listed: string[] = [];
    const count = await purgeSessionLeftovers({
      ...UNSUPPORTED_OPFS_CLIENT,
      async list(path) {
        listed.push(path);
        return ["s1", "s2", "s3"];
      },
      async remove(path) {
        removed.push(path);
        return true;
      },
    });

    expect(listed).toEqual([OPFS_DIRS.sessions]);
    expect(removed).toEqual(["sessions/s1", "sessions/s2", "sessions/s3"]);
    expect(count).toBe(3);
  });

  it("results/·frames/를 건드리지 않는다", async () => {
    const touched: string[] = [];
    await purgeSessionLeftovers({
      ...UNSUPPORTED_OPFS_CLIENT,
      async list(path) {
        touched.push(`list:${path}`);
        return ["a"];
      },
      async remove(path) {
        touched.push(`remove:${path}`);
        return true;
      },
    });
    expect(touched.some((t) => t.includes(OPFS_DIRS.results))).toBe(false);
    expect(touched.some((t) => t.includes(OPFS_DIRS.frames))).toBe(false);
  });

  it("삭제 실패는 개수에 세지 않는다(정직한 보고)", async () => {
    const count = await purgeSessionLeftovers({
      ...UNSUPPORTED_OPFS_CLIENT,
      async list() {
        return ["s1", "s2"];
      },
      async remove(path) {
        return path.endsWith("s1");
      },
    });
    expect(count).toBe(1);
  });

  it("잔재가 없으면 0이다", async () => {
    expect(await purgeSessionLeftovers(UNSUPPORTED_OPFS_CLIENT)).toBe(0);
  });
});

describe("sessionWorkspace", () => {
  it("컷·타임랩스·합성물 경로 규약을 지킨다", async () => {
    const worker = new FakeWorker();
    const client = createOpfsClient(worker);
    const ws = createSessionWorkspace(client, "20260730_210509_uuid");

    await ws.writeCut(1, new Uint8Array([1]));
    await ws.writeTimelapseFrame(7, new Uint8Array([1]));
    await ws.writeComposed("final.jpg", new Uint8Array([1]));

    const paths = worker.requests.map((r) => (r as { path: string }).path);
    expect(paths).toEqual([
      "sessions/20260730_210509_uuid/cut1.jpg",
      "sessions/20260730_210509_uuid/tl/00007.jpg",
      "sessions/20260730_210509_uuid/final.jpg",
    ]);
  });

  it("스풀 파일명이 0 패딩이라 문자열 정렬 = 시간 정렬이다", () => {
    const names = [1, 2, 10, 100, 1000].map(timelapseFrameName);
    expect([...names].sort()).toEqual(names);
    expect(cutFileName(3)).toBe("cut3.jpg");
  });

  it("스풀 목록은 jpg만 걸러 정렬한다", async () => {
    const ws = createSessionWorkspace(
      { ...UNSUPPORTED_OPFS_CLIENT, async list() { return ["00002.jpg", "junk.txt", "00001.jpg"]; } },
      "s1",
    );
    expect(await ws.listTimelapseFrames()).toEqual(["00001.jpg", "00002.jpg"]);
  });

  it("discard는 세션 폴더를 재귀 삭제한다", async () => {
    let removedPath = "";
    let recursive = false;
    const ws = createSessionWorkspace(
      {
        ...UNSUPPORTED_OPFS_CLIENT,
        async remove(path, options) {
          removedPath = path;
          recursive = options?.recursive === true;
          return true;
        },
      },
      "s1",
    );
    expect(await ws.discard()).toBe(true);
    expect(removedPath).toBe("sessions/s1");
    expect(recursive).toBe(true);
  });
});
