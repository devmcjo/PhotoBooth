import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createBackendClient, type BackendClient } from "@adapters/http/backendClient";
import { BackendError, isForbidden, isUnauthorized } from "@adapters/http/errors";
import { createFrameRepository } from "@adapters/http/frameRepository";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * `frameRepository.deleteFrame` 계약 — 설계 §9.4 H1~H4 (analysis/31 §4.14)
 *
 * 종전 구현은 응답 본문을 **버렸다**. 서버는 `{ deleted: true|false }`를 주고 **`false`는 성공이
 * 아니다**(문서 미발견) — 버리면 "서버에서도 삭제되었습니다"를 띄우고 문서는 그대로 남는다.
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");
const BASE = "https://api.example.com/api/";

interface Recorded {
  url: string;
  init: RequestInit;
}

function repoWith(
  handler: (recorded: Recorded) => Response,
): { repo: ReturnType<typeof createFrameRepository>; calls: Recorded[]; client: BackendClient } {
  const calls: Recorded[] = [];
  const impl = (async (input: RequestInfo | URL, init: RequestInit = {}) => {
    calls.push({ url: String(input), init });
    return handler({ url: String(input), init });
  }) as unknown as typeof fetch;
  const client = createBackendClient({
    fetchImpl: impl,
    baseUrl: BASE,
    gateKey: "gate",
    tokenProvider: () => "jwt-abc",
    now: () => 0,
  });
  return { repo: createFrameRepository(client), calls, client };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("H1: deleteFrame이 {deleted}를 그대로 돌려준다", () => {
  it("{deleted:true} → true", async () => {
    const { repo, calls } = repoWith(() => json({ deleted: true }));
    expect(await repo.deleteFrame("srv-1")).toBe(true);
    expect(calls[0]!.init.method).toBe("DELETE");
    expect(calls[0]!.url).toContain("frames/srv-1");
  });

  it("{deleted:false} → false (성공이 아니다)", async () => {
    const { repo } = repoWith(() => json({ deleted: false }));
    expect(await repo.deleteFrame("srv-1")).toBe(false);
  });

  it("id를 URL 인코딩한다(이름 유래 id·슬래시 방어)", async () => {
    const { repo, calls } = repoWith(() => json({ deleted: true }));
    await repo.deleteFrame("a/b c");
    expect(calls[0]!.url).toContain("frames/a%2Fb%20c");
  });
});

describe("H2: 응답 형태가 어긋나면 false다(성공 오인 금지)", () => {
  it.each([
    ["빈 객체", {}],
    ["문자열 deleted", { deleted: "true" }],
    ["배열", []],
    ["null 본문", null],
  ])("%s → false", async (_label, body) => {
    const { repo } = repoWith(() => json(body));
    expect(await repo.deleteFrame("srv-1")).toBe(false);
  });

  it("본문이 없는 204도 false다", async () => {
    const { repo } = repoWith(() => new Response(null, { status: 204 }));
    expect(await repo.deleteFrame("srv-1")).toBe(false);
  });
});

describe("H3: 401/403은 타입 있는 예외로 전파된다", () => {
  it("403 → BackendError(forbidden)", async () => {
    const { repo } = repoWith(() =>
      json({ error: { code: "forbidden", message: "권한 없음" } }, 403),
    );
    await expect(repo.deleteFrame("srv-1")).rejects.toBeInstanceOf(BackendError);
    const err = await repo.deleteFrame("srv-1").catch((e: unknown) => e);
    expect(isForbidden(err)).toBe(true);
  });

  it("401 → BackendError(unauthorized)", async () => {
    const { repo } = repoWith(() =>
      json({ error: { code: "unauthorized", message: "만료" } }, 401),
    );
    const err = await repo.deleteFrame("srv-1").catch((e: unknown) => e);
    expect(isUnauthorized(err)).toBe(true);
  });
});

describe("H4: `PUT /frames/{id}` 함수가 여전히 없다(정적 검사)", () => {
  it("소스에 PUT 메서드·update 함수가 0건이다", () => {
    // 프레임 편집은 로컬 전용 정책이다(analysis/13 §6.4). 함수가 존재하는 것만으로
    // 나중에 누군가 호출하는 경로가 생긴다 — 없는 것이 방어다.
    const source = readFileSync(join(SRC, "adapters/http/frameRepository.ts"), "utf8");
    expect(source.includes('method: "PUT"')).toBe(false);
    expect(/updateFrame\s*[(:]/.test(source)).toBe(false);
  });
});
