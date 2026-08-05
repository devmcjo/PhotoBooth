import {
  base64UrlFromBytes,
  isValidCodeVerifier,
  PKCE_VERIFIER_BYTES,
} from "@domain/auth/pkceCodec";
import { logger } from "@adapters/storage/logStore";

/**
 * PKCE·난수 생성 — Web Crypto 격리 계층 (07 §2.3)
 *
 * ⚠️ `crypto`를 만지는 곳은 여기뿐이다(도메인은 purity 테스트가 금지한다).
 * ⚠️ **예외를 전파하지 않는다**(15 §2). 실패는 `null`(PKCE)·빈 문자열(토큰)이고
 *    상위(`startGoogleSignIn`)가 `network` 사유로 표현한다.
 * ⚠️ `crypto.subtle`은 **보안 컨텍스트(https·localhost)에서만** 존재한다. 타입 선언은
 *    항상 있다고 말하므로 **런타임 감지**로 확인한다(15 §4 함정 #2).
 */

export interface PkceCryptoPort {
  randomBytes(count: number): Uint8Array;
  /** ASCII 문자열의 SHA-256. 미지원 환경에서는 던진다(호출측이 흡수한다). */
  sha256(ascii: string): Promise<Uint8Array>;
}

/** DOM 타입이 `crypto`를 필수로 선언하므로 옵셔널로 좁혀 받는다. */
function cryptoApi(): Crypto | undefined {
  const api: Crypto | undefined = globalThis.crypto;
  return api;
}

/**
 * 실제 Web Crypto 포트. **생성만으로는 `crypto`에 접근하지 않는다** —
 * 모듈 import·기본값 평가가 미지원 환경에서 즉시 터지지 않게 한다.
 */
export function webCryptoPort(): PkceCryptoPort {
  return {
    randomBytes(count) {
      const api = cryptoApi();
      if (api === undefined || typeof api.getRandomValues !== "function") {
        throw new Error("crypto.getRandomValues 미지원");
      }
      return api.getRandomValues(new Uint8Array(count));
    },

    async sha256(ascii) {
      const api = cryptoApi();
      if (api === undefined || typeof api.subtle?.digest !== "function") {
        throw new Error("crypto.subtle 미지원(보안 컨텍스트 아님)");
      }
      const digest = await api.subtle.digest("SHA-256", new TextEncoder().encode(ascii));
      return new Uint8Array(digest);
    },
  };
}

export interface PkcePair {
  readonly codeVerifier: string;
  readonly codeChallenge: string;
}

function reasonOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * `code_verifier`(43자) + `code_challenge`(S256, 43자).
 * 생성한 verifier가 **서버 정규식을 통과하는지 자체 확인**한다 — 여기서 걸러야 400을 안 본다.
 *
 * ⚠️ 생성된 값을 로그에 남기지 않는다(비밀).
 */
export async function createPkce(
  port: PkceCryptoPort = webCryptoPort(),
): Promise<PkcePair | null> {
  try {
    const codeVerifier = base64UrlFromBytes(port.randomBytes(PKCE_VERIFIER_BYTES));
    if (!isValidCodeVerifier(codeVerifier)) {
      logger.error("PKCE 생성 실패", { reason: "code_verifier가 서버 형식 규격과 어긋난다" });
      return null;
    }
    const codeChallenge = base64UrlFromBytes(await port.sha256(codeVerifier));
    return { codeVerifier, codeChallenge };
  } catch (err) {
    logger.error("PKCE 생성 실패", { reason: reasonOf(err) });
    return null;
  }
}

/**
 * `state`·`nonce`용 난수(기본 32바이트 → 43자 base64url).
 * 서버 `nonce` 정규식(`^[A-Za-z0-9\-._~]{1,256}$`)을 만족한다.
 *
 * ⚠️ 실패 시 **빈 문자열**이다(예외를 던지지 않는다 — 15 §2). 호출측이 빈 값을 확인해
 *    리디렉트를 시작하지 않는다.
 */
export function randomUrlSafeToken(
  port: PkceCryptoPort = webCryptoPort(),
  bytes: number = PKCE_VERIFIER_BYTES,
): string {
  try {
    return base64UrlFromBytes(port.randomBytes(bytes));
  } catch (err) {
    logger.error("난수 토큰 생성 실패", { reason: reasonOf(err) });
    return "";
  }
}
