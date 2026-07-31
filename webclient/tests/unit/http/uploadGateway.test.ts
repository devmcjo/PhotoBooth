import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  createUploadGateway,
  SIGNED_PUT_TIMEOUT_MS,
  type SignedPutProgress,
  type UploadGateway,
} from "@adapters/http/uploadGateway";
import type { BackendClient } from "@adapters/http/backendClient";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * ② 서명 PUT — 06 §4.2 · M14
 *
 * 여기서 고정하는 것: **헤더 전량 순회 부착** · **자격 증명 미부착** · **예외 대신 판별 유니온**
 * · 진행률 계약 · 취소 시 리스너 해제.
 */

/**
 * ⚠️ **주석을 제거하고 검사한다.** 불변식은 *코드*에 대한 것이라, 규칙을 설명하는 주석이
 * 그 규칙을 깨뜨린 것처럼 보이면 안 된다(`purity.test.ts`가 같은 이유로 같은 처리를 한다).
 */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
}

const GATEWAY_SOURCE = stripComments(
  readFileSync(
    join(
      dirname(fileURLToPath(import.meta.url)),
      "..",
      "..",
      "..",
      "src",
      "adapters",
      "http",
      "uploadGateway.ts",
    ),
    "utf8",
  ),
);

/** 실제 prepare 응답이 주는 모양. 다운로드 토큰 헤더가 빠지면 파일 GET이 불가능해진다. */
const REQUIRED_HEADERS = {
  "Content-Type": "image/jpeg",
  "x-goog-meta-firebaseStorageDownloadTokens": "0b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d",
  "x-goog-resumable": "start",
} as const;

class FakeXhr {
  headers: [string, string][] = [];
  method = "";
  url = "";
  timeout = 0;
  status = 0;
  sent: Blob | null = null;
  sendCount = 0;
  abortCount = 0;
  upload = { onprogress: null as ((event: ProgressEvent) => void) | null };
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  ontimeout: (() => void) | null = null;
  onabort: (() => void) | null = null;

  open(method: string, url: string): void {
    this.method = method;
    this.url = url;
  }

  setRequestHeader(name: string, value: string): void {
    this.headers.push([name, value]);
  }

  send(body: Blob): void {
    this.sent = body;
    this.sendCount++;
  }

  abort(): void {
    this.abortCount++;
    this.onabort?.();
  }

  /** `xhr.upload.onprogress` 1회분을 흉내낸다. */
  emitProgress(lengthComputable: boolean, loaded: number, total: number): void {
    this.upload.onprogress?.({ lengthComputable, loaded, total } as ProgressEvent);
  }
}

/** prepare/commit을 부르지 않는 테스트에서 쓰는 스텁. 호출되면 실패한다. */
const NO_HTTP: BackendClient = {
  request: async () => {
    throw new Error("이 테스트는 backendClient를 호출하지 않아야 한다");
  },
};

function gatewayWith(fake: FakeXhr, nowValues: number[] = [0, 12]): UploadGateway {
  let index = 0;
  return createUploadGateway(NO_HTTP, {
    createXhr: () => fake as unknown as XMLHttpRequest,
    now: () => nowValues[Math.min(index++, nowValues.length - 1)] ?? 0,
  });
}

function blob(bytes: number): Blob {
  return new Blob([new Uint8Array(bytes)]);
}

beforeEach(() => {
  detachLogStore();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

describe("uploadGateway.put — M14 헤더 전량 순회 부착", () => {
  it("requiredHeaders를 개수·이름·값·순서 그대로 붙인다", async () => {
    const fake = new FakeXhr();
    const gateway = gatewayWith(fake);

    const promise = gateway.put({ url: "https://signed.example/put", body: blob(10), headers: REQUIRED_HEADERS });
    fake.status = 200;
    fake.onload?.();
    await promise;

    expect(fake.headers).toEqual(Object.entries(REQUIRED_HEADERS));
    expect(fake.headers).toHaveLength(3);
    expect(fake.headers.map(([name]) => name)).toContain(
      "x-goog-meta-firebaseStorageDownloadTokens",
    );
  });

  it("헤더가 2개면 setRequestHeader도 정확히 2회다(하드코딩 금지)", async () => {
    const fake = new FakeXhr();
    const two = {
      "Content-Type": "video/mp4",
      "x-goog-meta-firebaseStorageDownloadTokens": "tok",
    };

    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: two });
    fake.status = 204;
    fake.onload?.();
    await promise;

    expect(fake.headers).toEqual([
      ["Content-Type", "video/mp4"],
      ["x-goog-meta-firebaseStorageDownloadTokens", "tok"],
    ]);
  });

  it("서명 PUT에 자격 증명을 붙이지 않는다(서명 URL 자체가 권한)", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({
      url: "https://s/x",
      body: blob(1),
      headers: REQUIRED_HEADERS,
    });
    fake.status = 200;
    fake.onload?.();
    await promise;

    const names = fake.headers.map(([name]) => name.toLowerCase());
    expect(names).not.toContain("authorization");
    expect(names).not.toContain("x-mcphoto-client");
  });

  it("PUT 메서드와 타임아웃 100초를 설정한다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: {} });
    fake.status = 200;
    fake.onload?.();
    await promise;

    expect(fake.method).toBe("PUT");
    expect(fake.url).toBe("https://s/x");
    expect(fake.timeout).toBe(SIGNED_PUT_TIMEOUT_MS);
    expect(SIGNED_PUT_TIMEOUT_MS).toBe(100_000);
  });
});

describe("uploadGateway.put — 진행률 (WM5)", () => {
  it("lengthComputable일 때만 진행률을 통지한다", async () => {
    const fake = new FakeXhr();
    const seen: SignedPutProgress[] = [];

    const promise = gatewayWith(fake).put({
      url: "https://s/x",
      body: blob(100),
      headers: {},
      onProgress: (p) => seen.push(p),
    });

    fake.emitProgress(false, 10, 100); // 무시돼야 한다
    fake.emitProgress(true, 50, 100);
    fake.status = 200;
    fake.onload?.();
    await promise;

    expect(seen).toEqual([{ loaded: 50, total: 100 }]);
  });

  it("onProgress를 주지 않아도 진행률 이벤트에서 깨지지 않는다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: {} });
    expect(() => fake.emitProgress(true, 1, 1)).not.toThrow();
    fake.status = 200;
    fake.onload?.();
    await expect(promise).resolves.toMatchObject({ ok: true });
  });
});

describe("uploadGateway.put — 실패는 예외가 아니라 판별 유니온 (15 §2)", () => {
  it("2xx는 성공이고 바이트 수를 보고한다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake, [0, 12]).put({
      url: "https://s/x",
      body: blob(2048),
      headers: {},
    });
    fake.status = 204;
    fake.onload?.();

    expect(await promise).toEqual({ ok: true, status: 204, bytes: 2048, elapsedMs: 12 });
  });

  it("403은 던지지 않고 http 실패로 축소한다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: {} });
    fake.status = 403;
    fake.onload?.();

    await expect(promise).resolves.toMatchObject({ ok: false, failure: "http", status: 403 });
  });

  it("네트워크 오류(= CORS 차단 가능)는 network다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: {} });
    fake.onerror?.();

    await expect(promise).resolves.toMatchObject({
      ok: false,
      failure: "network",
      status: null,
    });
  });

  it("타임아웃은 timeout이다", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(1), headers: {} });
    fake.ontimeout?.();

    await expect(promise).resolves.toMatchObject({ ok: false, failure: "timeout" });
  });

  it("open이 동기 예외를 던져도 전파하지 않는다", async () => {
    const fake = new FakeXhr();
    vi.spyOn(fake, "open").mockImplementation(() => {
      throw new Error("InvalidStateError");
    });

    await expect(
      gatewayWith(fake).put({ url: "bad", body: blob(1), headers: {} }),
    ).resolves.toMatchObject({ ok: false, failure: "network" });
  });

  it("결과는 정확히 한 번만 확정된다(onload 후 onerror는 무시)", async () => {
    const fake = new FakeXhr();
    const promise = gatewayWith(fake).put({ url: "https://s/x", body: blob(7), headers: {} });
    fake.status = 200;
    fake.onload?.();
    fake.onerror?.();
    fake.ontimeout?.();

    await expect(promise).resolves.toMatchObject({ ok: true, status: 200, bytes: 7 });
  });
});

describe("uploadGateway.put — 취소", () => {
  it("이미 취소된 signal이면 요청을 보내지 않는다", async () => {
    const fake = new FakeXhr();
    const controller = new AbortController();
    controller.abort();

    const outcome = await gatewayWith(fake).put({
      url: "https://s/x",
      body: blob(1),
      headers: REQUIRED_HEADERS,
      signal: controller.signal,
    });

    expect(outcome).toMatchObject({ ok: false, failure: "aborted" });
    expect(fake.sendCount).toBe(0);
    expect(fake.headers).toEqual([]);
  });

  it("진행 중 취소하면 xhr.abort를 부르고 aborted로 끝난다", async () => {
    const fake = new FakeXhr();
    const controller = new AbortController();
    const promise = gatewayWith(fake).put({
      url: "https://s/x",
      body: blob(1),
      headers: {},
      signal: controller.signal,
    });

    expect(fake.sendCount).toBe(1);
    controller.abort();

    await expect(promise).resolves.toMatchObject({ ok: false, failure: "aborted" });
    expect(fake.abortCount).toBe(1);
  });

  it("settle 후 abort 리스너가 제거된다(누수 방지)", async () => {
    const fake = new FakeXhr();
    const controller = new AbortController();
    const removeSpy = vi.spyOn(controller.signal, "removeEventListener");

    const promise = gatewayWith(fake).put({
      url: "https://s/x",
      body: blob(1),
      headers: {},
      signal: controller.signal,
    });
    fake.status = 200;
    fake.onload?.();
    await promise;

    expect(removeSpy).toHaveBeenCalledTimes(1);

    // 리스너가 남아 있으면 여기서 xhr.abort()가 한 번 더 불린다.
    controller.abort();
    expect(fake.abortCount).toBe(0);
  });
});

describe("uploadGateway — 정적 불변식 (15 §3.4)", () => {
  it("서명 PUT 경로에 자격 증명 조립이 없다", () => {
    for (const forbidden of ["Authorization", "X-MCPhoto-Client", "GATE_KEY_HEADER", "getToken"]) {
      expect(GATEWAY_SOURCE, `${forbidden} — 서명 URL 자체가 권한이다`).not.toContain(forbidden);
    }
  });

  it("서명 URL을 로그에 넘기지 않는다", () => {
    const loggerCallsWithUrl = GATEWAY_SOURCE.match(/logger\.[a-z]+\([^)]*\burl\b/g) ?? [];
    expect(loggerCallsWithUrl).toEqual([]);
  });

  it("PUT을 fetch로 하지 않는다(진행률을 얻을 수 없다 — WM5)", () => {
    expect(GATEWAY_SOURCE).not.toContain("fetch(");
    expect(GATEWAY_SOURCE).toContain("xhr.upload.onprogress");
  });
});
