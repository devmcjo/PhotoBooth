import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  createPkce,
  randomUrlSafeToken,
  webCryptoPort,
  type PkceCryptoPort,
} from "@adapters/auth/pkce";
import { isValidCodeVerifier } from "@domain/auth/pkceCodec";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogStore,
} from "@adapters/storage/logStore";

/**
 * PKCE 어댑터 — 07 §2.3
 *
 * 난수·해시를 **포트로 주입**해 결정론적으로 검증한다. 미지원 환경(보안 컨텍스트 아님)에서
 * **throw가 아니라 null**이라는 것이 이 계층의 계약이다(15 §2).
 */

/** 결정론적 목 포트. `count`만큼 0,1,2,… 를 채우고 해시는 바이트 반전으로 흉내낸다. */
function mockPort(): PkceCryptoPort {
  return {
    randomBytes(count) {
      const bytes = new Uint8Array(count);
      for (let i = 0; i < count; i++) bytes[i] = (i * 7 + 3) % 256;
      return bytes;
    },
    async sha256(ascii) {
      const digest = new Uint8Array(32);
      for (let i = 0; i < 32; i++) digest[i] = (ascii.charCodeAt(i % ascii.length) + i) % 256;
      return digest;
    },
  };
}

let logStore: LogStore;

beforeEach(() => {
  logStore = createLogStore({ sink: createMemoryLogSink(), now: () => 0 });
  attachLogStore(logStore);
});

afterEach(() => {
  detachLogStore();
});

describe("createPkce — 결정론적 포트", () => {
  it("같은 포트로 같은 값이 나온다(43자 verifier + 43자 challenge)", async () => {
    const first = await createPkce(mockPort());
    const second = await createPkce(mockPort());
    expect(first).not.toBeNull();
    expect(first).toEqual(second);
    expect(first!.codeVerifier).toHaveLength(43);
    expect(first!.codeChallenge).toHaveLength(43);
  });

  it("verifier가 서버 형식 규격을 통과한다", async () => {
    const pair = await createPkce(mockPort());
    expect(isValidCodeVerifier(pair!.codeVerifier)).toBe(true);
  });

  it("challenge가 verifier를 그대로 되풀이하지 않는다(해시를 실제로 쓴다)", async () => {
    const pair = await createPkce(mockPort());
    expect(pair!.codeChallenge).not.toBe(pair!.codeVerifier);
  });

  it("subtle이 없으면 **null**이다(throw 아님)", async () => {
    const port: PkceCryptoPort = {
      randomBytes: mockPort().randomBytes,
      sha256: () => Promise.reject(new Error("crypto.subtle 미지원(보안 컨텍스트 아님)")),
    };
    await expect(createPkce(port)).resolves.toBeNull();

    const text = await logStore.exportText();
    expect(text).toContain("PKCE 생성 실패");
  });

  it("getRandomValues가 던져도 null이다", async () => {
    const port: PkceCryptoPort = {
      randomBytes() {
        throw new Error("crypto.getRandomValues 미지원");
      },
      sha256: mockPort().sha256,
    };
    await expect(createPkce(port)).resolves.toBeNull();
  });

  it("난수가 형식 규격을 못 채우면 null이다(짧은 verifier로 400을 맞지 않는다)", async () => {
    const port: PkceCryptoPort = {
      // 8바이트만 돌려주는 고장난 포트 → 11자 verifier.
      randomBytes: () => new Uint8Array(8),
      sha256: mockPort().sha256,
    };
    await expect(createPkce(port)).resolves.toBeNull();
  });

  it("생성한 비밀값을 로그에 남기지 않는다", async () => {
    const pair = await createPkce(mockPort());
    const text = await logStore.exportText();
    expect(text).not.toContain(pair!.codeVerifier);
    expect(text).not.toContain(pair!.codeChallenge);
  });
});

describe("randomUrlSafeToken", () => {
  it("기본 32바이트 → 43자이고 서버 nonce 정규식을 통과한다", () => {
    const token = randomUrlSafeToken(mockPort());
    expect(token).toHaveLength(43);
    // ↔ 서버 validation.ts 의 nonce 정규식 `^[A-Za-z0-9\-._~]{1,256}$`.
    expect(/^[A-Za-z0-9\-._~]{1,256}$/.test(token)).toBe(true);
  });

  it("바이트 수를 지정할 수 있다", () => {
    expect(randomUrlSafeToken(mockPort(), 16)).toHaveLength(22);
  });

  it("실패는 빈 문자열이다(throw 아님)", () => {
    const port: PkceCryptoPort = {
      randomBytes() {
        throw new Error("미지원");
      },
      sha256: mockPort().sha256,
    };
    expect(randomUrlSafeToken(port)).toBe("");
  });
});

describe("webCryptoPort — 런타임 감지", () => {
  it("생성만으로는 crypto에 접근하지 않는다(미지원 환경에서 import가 터지지 않는다)", () => {
    expect(() => webCryptoPort()).not.toThrow();
  });

  it("node의 Web Crypto로도 실제 PKCE가 만들어진다(계약 자체 검증)", async () => {
    // node 20+ 는 globalThis.crypto(webcrypto)를 제공한다 — 실 브라우저 경로와 같은 코드다.
    const pair = await createPkce(webCryptoPort());
    expect(pair).not.toBeNull();
    expect(isValidCodeVerifier(pair!.codeVerifier)).toBe(true);
    expect(pair!.codeChallenge).toHaveLength(43);
  });
});
