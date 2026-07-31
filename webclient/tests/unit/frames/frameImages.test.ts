import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  downloadFrameImage,
  FRAME_DOWNLOAD_TIMEOUT_MS,
} from "@adapters/frames/frameDownloader";
import {
  frameImageUrl,
  frameImageUrlCount,
  revokeAllFrameImages,
  revokeFrameImage,
} from "@adapters/frames/frameImageCache";
import {
  createFrameThumbnail,
  FRAME_THUMB_WIDTH,
  resetThumbnailProbeForTests,
  thumbnailResizeSupported,
} from "@adapters/frames/frameThumbnails";
import { compose } from "@adapters/compose/compositor";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 프레임 이미지 계층 — 설계 §8 I1~I8 (WM2 · 04 §5.2)
 *
 * 여기서 고정하는 것 셋: ① 다운로드가 **CORS-clean**하다(빠지면 6컷을 다 찍은 뒤 합성이 죽는다)
 * ② object URL이 경로당 1개다 ③ resize 옵션이 **조용히 무시되는** 경우를 결과 width로 잡아낸다.
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

interface Recorded {
  url: string;
  init: RequestInit;
}

function stubFetch(handler: (recorded: Recorded) => Promise<Response> | Response): Recorded[] {
  const calls: Recorded[] = [];
  vi.stubGlobal("fetch", (async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const recorded = { url: String(input), init };
    calls.push(recorded);
    return handler(recorded);
  }) as unknown as typeof fetch);
  return calls;
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
  resetThumbnailProbeForTests();
  revokeAllFrameImages();
});

afterEach(() => {
  vi.unstubAllGlobals();
  revokeAllFrameImages();
  detachLogStore();
});

describe("I1: downloadFrameImage — 실패는 전부 null(예외 0)", () => {
  it("비200은 null이다(이미지 없는 문서는 정상 경로 — analysis/31 §4.10)", async () => {
    stubFetch(() => new Response(null, { status: 404 }));
    expect(await downloadFrameImage("https://cdn.example.com/a.png")).toBeNull();
  });

  it("네트워크 예외는 null이다", async () => {
    stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });
    expect(await downloadFrameImage("https://cdn.example.com/a.png")).toBeNull();
  });

  it("빈 본문은 null이다(0바이트 PNG를 캐시하지 않는다)", async () => {
    stubFetch(() => new Response(new Blob([]), { status: 200 }));
    expect(await downloadFrameImage("https://cdn.example.com/a.png")).toBeNull();
  });

  it("빈 URL은 요청조차 하지 않는다", async () => {
    const calls = stubFetch(() => new Response(new Blob(["x"])));
    expect(await downloadFrameImage("   ")).toBeNull();
    expect(calls).toHaveLength(0);
  });

  it("정상 응답은 Blob을 돌려준다", async () => {
    stubFetch(() => new Response(new Blob(["png-bytes"]), { status: 200 }));
    const blob = await downloadFrameImage("https://cdn.example.com/a.png");
    expect(blob?.size).toBeGreaterThan(0);
  });
});

describe("I2: CORS-clean 옵션이 실제로 전달된다(WM2)", () => {
  it("mode:\"cors\" · credentials:\"omit\" · cache:\"force-cache\"", async () => {
    const calls = stubFetch(() => new Response(new Blob(["x"]), { status: 200 }));
    await downloadFrameImage("https://cdn.example.com/a.png");
    expect(calls[0]!.init.mode).toBe("cors");
    expect(calls[0]!.init.credentials).toBe("omit");
    expect(calls[0]!.init.cache).toBe("force-cache");
  });

  it("게이트 키·Bearer를 붙이지 않는다(다운로드 토큰 URL은 인증 불요)", async () => {
    const calls = stubFetch(() => new Response(new Blob(["x"]), { status: 200 }));
    await downloadFrameImage("https://cdn.example.com/a.png");
    expect(calls[0]!.init.headers).toBeUndefined();
  });
});

describe("I3: 타임아웃에서 abort된다", () => {
  it("한 장이 매달려도 무진행 예산을 태우지 않는다", async () => {
    let seen: AbortSignal | null = null;
    stubFetch(
      ({ init }) =>
        new Promise<Response>((_resolve, reject) => {
          seen = init.signal ?? null;
          init.signal?.addEventListener("abort", () =>
            reject(new DOMException("Aborted", "AbortError")),
          );
        }),
    );

    expect(await downloadFrameImage("https://cdn.example.com/a.png", 10)).toBeNull();
    expect((seen as unknown as AbortSignal | null)?.aborted).toBe(true);
  });

  it("기본 상한은 15초다(무진행 30초보다 짧다)", () => {
    expect(FRAME_DOWNLOAD_TIMEOUT_MS).toBe(15_000);
  });
});

describe("I4: frameImageCache — 경로당 URL 1개", () => {
  it("같은 경로는 URL을 재사용하고 revoke 후에는 새로 만든다", () => {
    const blob = new Blob(["png"]);
    const first = frameImageUrl("frames/a.png", blob);
    const second = frameImageUrl("frames/a.png", new Blob(["other"]));
    expect(second).toBe(first);
    expect(frameImageUrlCount()).toBe(1);

    revokeFrameImage("frames/a.png");
    expect(frameImageUrlCount()).toBe(0);
    const third = frameImageUrl("frames/a.png", blob);
    expect(third).not.toBe(first);
    expect(frameImageUrlCount()).toBe(1);
  });

  it("없는 경로 revoke는 무해하다(멱등)", () => {
    expect(() => revokeFrameImage("frames/none.png")).not.toThrow();
    expect(frameImageUrlCount()).toBe(0);
  });

  it("경로가 다르면 URL도 다르다", () => {
    const a = frameImageUrl("frames/a.png", new Blob(["a"]));
    const b = frameImageUrl("frames/b.png", new Blob(["b"]));
    expect(a).not.toBe(b);
    expect(frameImageUrlCount()).toBe(2);
    revokeAllFrameImages();
    expect(frameImageUrlCount()).toBe(0);
  });
});

// ── 썸네일 프로브: createImageBitmap·OffscreenCanvas를 가짜로 심는다(node에 없다) ──

interface FakeBitmap {
  width: number;
  height: number;
  closed: boolean;
  close(): void;
}

function makeBitmap(width: number, height: number, closed: FakeBitmap[]): FakeBitmap {
  const bitmap: FakeBitmap = {
    width,
    height,
    closed: false,
    close() {
      bitmap.closed = true;
      closed.push(bitmap);
    },
  };
  return bitmap;
}

/** resize 옵션을 실제로 반영하는(=지원하는) 가짜. */
function stubBitmapSupported(closed: FakeBitmap[]): { options: unknown[] } {
  const options: unknown[] = [];
  vi.stubGlobal("createImageBitmap", async (_blob: Blob, opts?: { resizeWidth?: number }) => {
    options.push(opts);
    return opts?.resizeWidth !== undefined
      ? makeBitmap(opts.resizeWidth, Math.round(opts.resizeWidth * (1600 / 1200)), closed)
      : makeBitmap(1200, 1600, closed);
  });
  return { options };
}

/** resize 옵션을 **조용히 무시**하는 가짜(구형 Safari). */
function stubBitmapIgnoresResize(closed: FakeBitmap[]): { options: unknown[] } {
  const options: unknown[] = [];
  vi.stubGlobal("createImageBitmap", async (_blob: Blob, opts?: unknown) => {
    options.push(opts);
    return makeBitmap(1200, 1600, closed);
  });
  return { options };
}

function stubOffscreenCanvas(): { drawn: number[][] } {
  const drawn: number[][] = [];
  class FakeOffscreenCanvas {
    constructor(
      readonly width: number,
      readonly height: number,
    ) {}
    getContext(): unknown {
      return {
        imageSmoothingEnabled: false,
        imageSmoothingQuality: "low",
        drawImage: (_b: unknown, _x: number, _y: number, w: number, h: number) => {
          drawn.push([w, h]);
        },
      };
    }
    transferToImageBitmap(): FakeBitmap {
      return makeBitmap(this.width, this.height, []);
    }
  }
  vi.stubGlobal("OffscreenCanvas", FakeOffscreenCanvas);
  return { drawn };
}

describe("I5: 썸네일 resize 프로브 — 1회 확인 후 판정을 캐시한다", () => {
  it("resize가 실효하면 그 경로를 계속 쓴다", async () => {
    const closed: FakeBitmap[] = [];
    const { options } = stubBitmapSupported(closed);

    const first = await createFrameThumbnail(new Blob(["png"]));
    expect(first?.width).toBe(FRAME_THUMB_WIDTH);
    expect(thumbnailResizeSupported()).toBe(true);

    await createFrameThumbnail(new Blob(["png"]));
    // 두 번 다 resize 옵션을 넘겼고, 폴백 디코드(옵션 없는 호출)는 없다.
    expect(options).toHaveLength(2);
    expect(options.every((o) => (o as { resizeWidth?: number }).resizeWidth === FRAME_THUMB_WIDTH)).toBe(
      true,
    );
  });

  it("width가 어긋나면 폴백으로 전환하고 2회째에는 프로브를 하지 않는다", async () => {
    const closed: FakeBitmap[] = [];
    const { options } = stubBitmapIgnoresResize(closed);
    stubOffscreenCanvas();

    const first = await createFrameThumbnail(new Blob(["png"]));
    expect(first?.width).toBe(FRAME_THUMB_WIDTH);
    expect(thumbnailResizeSupported()).toBe(false);
    // 1회차: 프로브(옵션 있음) + 폴백 디코드(옵션 없음) = 2회
    expect(options).toHaveLength(2);
    expect((options[0] as { resizeWidth?: number }).resizeWidth).toBe(FRAME_THUMB_WIDTH);
    expect(options[1]).toBeUndefined();
    // 무시된 원본 비트맵을 닫았다(WR8).
    expect(closed.length).toBeGreaterThanOrEqual(2);

    options.length = 0;
    await createFrameThumbnail(new Blob(["png"]));
    // 2회차: 폴백만 — resize 옵션 인자가 다시 나타나지 않는다.
    expect(options).toHaveLength(1);
    expect(options[0]).toBeUndefined();
  });
});

describe("I6: 썸네일 실패는 null이고 중간 비트맵을 닫는다", () => {
  it("createImageBitmap이 던지면 null이다(예외 0)", async () => {
    vi.stubGlobal("createImageBitmap", async () => {
      throw new Error("decode failed");
    });
    expect(await createFrameThumbnail(new Blob(["x"]))).toBeNull();
  });

  it("OffscreenCanvas가 없으면 null이고 전체 디코드 비트맵을 닫는다", async () => {
    const closed: FakeBitmap[] = [];
    stubBitmapIgnoresResize(closed);
    vi.stubGlobal("OffscreenCanvas", undefined);

    expect(await createFrameThumbnail(new Blob(["x"]))).toBeNull();
    // 프로브 비트맵 + 폴백 전체 디코드 비트맵 둘 다 닫혔다.
    expect(closed.filter((b) => b.closed)).toHaveLength(2);
  });
});

describe("I7·I8: 합성 경로의 원격/로컬 분기", () => {
  it("I7: compositor.ts에 mode: \"cors\" 문자열이 남아 있다(FR-6)", () => {
    const source = readFileSync(join(SRC, "adapters/compose/compositor.ts"), "utf8")
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/(^|[^:])\/\/.*$/gm, "$1");
    expect(source.includes('mode: "cors"')).toBe(true);
  });

  it("I8: https에는 cors 옵션을, blob:에는 옵션 없이 fetch한다", async () => {
    // 디코드(`createImageBitmap`)는 node에 없어 실패하지만, fetch는 그 **전에** 일어난다.
    const calls = stubFetch(() => new Response(new Blob(["png"]), { status: 200 }));
    const request = {
      slots: [],
      cuts: [],
      filter: "None" as const,
      format: "Jpg" as const,
    };

    await compose({ ...request, frameImageUrl: "https://cdn.example.com/a.png" }).catch(
      () => undefined,
    );
    expect(calls[0]!.init.mode).toBe("cors");
    expect(calls[0]!.init.cache).toBe("force-cache");

    await compose({ ...request, frameImageUrl: "blob:http://localhost/abc" }).catch(
      () => undefined,
    );
    expect(calls[1]!.init.mode).toBeUndefined();
    expect(calls[1]!.init.cache).toBeUndefined();

    await compose({ ...request, frameImageUrl: "/frames/basic4.png" }).catch(() => undefined);
    expect(calls[2]!.init.mode).toBeUndefined();
  });
});
