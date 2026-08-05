import { describe, expect, it } from "vitest";
import {
  base64UrlFromBytes,
  isValidCodeVerifier,
  PKCE_VERIFIER_BYTES,
  PKCE_VERIFIER_RE,
} from "@domain/auth/pkceCodec";

/**
 * base64url 인코딩 — 07 §2.3 · analysis/61 §3.0
 *
 * 자체 구현이므로 **RFC 4648 §10 표준 벡터**로 고정한다. 여기가 틀리면 서버가 400을 내고
 * 원인이 "로그인이 안 된다"로만 보인다.
 */

function ascii(text: string): Uint8Array {
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i);
  return bytes;
}

describe("base64UrlFromBytes — RFC 4648 벡터", () => {
  // RFC 4648 §10의 base64 벡터에서 패딩(`=`)만 제거한 값이다.
  const vectors: readonly [string, string][] = [
    ["", ""],
    ["f", "Zg"],
    ["fo", "Zm8"],
    ["foo", "Zm9v"],
    ["foob", "Zm9vYg"],
    ["fooba", "Zm9vYmE"],
    ["foobar", "Zm9vYmFy"],
  ];

  it.each(vectors)("%o → %o", (input, expected) => {
    expect(base64UrlFromBytes(ascii(input))).toBe(expected);
  });

  it("62·63번째 문자가 `-`·`_`다(`+`·`/`가 아니다 — URL 안전)", () => {
    // 0xFB 0xFF → 111110 111111 11xxxx → 인덱스 62·63이 앞에 온다.
    expect(base64UrlFromBytes(new Uint8Array([0xfb, 0xff, 0xff]))).toBe("-___");
  });

  it("패딩·`+`·`/`가 절대 나오지 않는다(1~64바이트 전수)", () => {
    for (let length = 1; length <= 64; length++) {
      const bytes = new Uint8Array(length);
      for (let i = 0; i < length; i++) bytes[i] = (i * 37 + length * 11) % 256;
      const encoded = base64UrlFromBytes(bytes);
      expect(encoded, `${length}바이트`).not.toMatch(/[=+/]/);
    }
  });
});

describe("code_verifier 형식", () => {
  it("32바이트는 43자다(서버 정규식 하한과 정확히 맞는다)", () => {
    const encoded = base64UrlFromBytes(new Uint8Array(PKCE_VERIFIER_BYTES));
    expect(PKCE_VERIFIER_BYTES).toBe(32);
    expect(encoded).toHaveLength(43);
  });

  it("어떤 32바이트 난수든 서버 정규식을 통과한다", () => {
    for (let seed = 0; seed < 32; seed++) {
      const bytes = new Uint8Array(PKCE_VERIFIER_BYTES);
      for (let i = 0; i < bytes.length; i++) bytes[i] = (i * 61 + seed * 7) % 256;
      const verifier = base64UrlFromBytes(bytes);
      expect(isValidCodeVerifier(verifier), verifier).toBe(true);
    }
  });

  it("42자는 거부하고 128자는 통과한다(경계)", () => {
    expect(isValidCodeVerifier("a".repeat(42))).toBe(false);
    expect(isValidCodeVerifier("a".repeat(43))).toBe(true);
    expect(isValidCodeVerifier("a".repeat(128))).toBe(true);
    expect(isValidCodeVerifier("a".repeat(129))).toBe(false);
  });

  it("허용 문자 집합 밖(`+`·`/`·`=`·공백)을 거부한다", () => {
    for (const bad of ["+", "/", "=", " ", "%"]) {
      expect(isValidCodeVerifier(`${"a".repeat(42)}${bad}`), bad).toBe(false);
    }
    // `-`·`.`·`_`·`~`는 규격상 허용된다.
    expect(isValidCodeVerifier(`${"a".repeat(39)}-._~`)).toBe(true);
  });

  it("정규식이 서버(validation.ts:78)와 같은 문자열이다", () => {
    // ↔ web/functions/src/domain/validation.ts 의 codeVerifier 정규식.
    expect(PKCE_VERIFIER_RE.source).toBe("^[A-Za-z0-9\\-._~]{43,128}$");
  });
});
