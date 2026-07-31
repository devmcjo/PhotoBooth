/**
 * PKCE 인코딩·형식 판정(순수) — 07 §2.3 · analysis/61 §3.0
 *
 * ⚠️ `btoa`를 쓰지 않는다. 도메인은 브라우저 API에 의존할 수 없고(purity 테스트),
 *    `btoa`는 바이트를 문자로 옮기는 과정에서 실수하기 쉽다. 자체 알파벳 루프가
 *    node 테스트와 브라우저에서 **같은 값**을 낸다는 것을 RFC 4648 벡터로 고정한다.
 */

/** 난수 바이트 수. 32바이트 → base64url 43자 → 서버 정규식 하한과 정확히 맞는다. */
export const PKCE_VERIFIER_BYTES = 32;

/** ↔ 서버 `web/functions/src/domain/validation.ts:78`의 `codeVerifier` 정규식과 같은 값이다. */
export const PKCE_VERIFIER_RE = /^[A-Za-z0-9\-._~]{43,128}$/;

/** RFC 4648 §5 base64url 알파벳(62·63번째 문자가 `-`·`_`). */
const BASE64URL_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

/**
 * RFC 4648 §5 base64url, **패딩(`=`) 제거**. 32바이트 → 43자.
 * `+`·`/`·`=`가 절대 나오지 않으므로 URL 인코딩 없이 쿼리에 실을 수 있다.
 */
export function base64UrlFromBytes(bytes: Uint8Array): string {
  let out = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const b0 = bytes[i]!;
    out += BASE64URL_ALPHABET[b0 >> 2]!;

    if (i + 1 >= bytes.length) {
      // 1바이트 남음 → 2문자(패딩 `==`를 붙이지 않는다).
      out += BASE64URL_ALPHABET[(b0 & 0x03) << 4]!;
      break;
    }
    const b1 = bytes[i + 1]!;
    out += BASE64URL_ALPHABET[((b0 & 0x03) << 4) | (b1 >> 4)]!;

    if (i + 2 >= bytes.length) {
      // 2바이트 남음 → 3문자(패딩 `=` 없음).
      out += BASE64URL_ALPHABET[(b1 & 0x0f) << 2]!;
      break;
    }
    const b2 = bytes[i + 2]!;
    out += BASE64URL_ALPHABET[((b1 & 0x0f) << 2) | (b2 >> 6)]!;
    out += BASE64URL_ALPHABET[b2 & 0x3f]!;
  }
  return out;
}

/**
 * 서버가 400으로 거부할 값을 **보내기 전에** 걸러낸다.
 * 어댑터가 생성 직후 자체 확인에 쓴다(형식이 어긋나면 리디렉트를 시작하지 않는다).
 */
export function isValidCodeVerifier(value: string): boolean {
  return PKCE_VERIFIER_RE.test(value);
}
