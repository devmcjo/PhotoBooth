import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * 인증 정적 불변식 — 15 §3.4 관례("문서에만 있으면 언젠가 깨진다")
 *
 * 아래 7건은 깨져도 **테스트가 초록으로 남을 수 있는** 종류다:
 * 로그인이 조용히 desktop 구성으로 교환을 시도하거나, 다른 손님 계정으로 원탭 로그인되거나,
 * JWT가 저장소에 새거나, 개발용 헬퍼가 세션을 위조하거나, 비밀값이 로그에 남는다.
 * 그래서 **소스를 읽어** 검사한다(`purity.test.ts`가 같은 형태의 선례다).
 */

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SRC = join(ROOT, "src");

function collectSourceFiles(dir: string): string[] {
  const result: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      result.push(...collectSourceFiles(full));
    } else if (entry.endsWith(".ts") || entry.endsWith(".tsx")) {
      result.push(full);
    }
  }
  return result;
}

/** 주석을 제거한다 — 설명 문구가 금지 패턴에 걸리지 않게(`purity.test.ts`와 같은 방식). */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
}

function code(path: string): string {
  return stripComments(readFileSync(path, "utf8"));
}

/** `src/` 기준 POSIX 상대 경로(플랫폼 무관 단언). */
function rel(path: string): string {
  return relative(SRC, path).split(sep).join("/");
}

const ALL_SOURCES = collectSourceFiles(SRC);

/** 이번 Step이 신설한 인증 관련 파일 전부. */
const AUTH_FILES: readonly string[] = [
  "domain/auth/pkceCodec.ts",
  "domain/auth/authorizeUrl.ts",
  "domain/auth/oauthCallbackPolicy.ts",
  "domain/auth/loginFailure.ts",
  "adapters/auth/pkce.ts",
  "adapters/auth/oauthStateStore.ts",
  "adapters/auth/googleSignIn.ts",
  "screens/oauthCallback/oauthCallbackRunner.ts",
  "screens/login/useGoogleSignIn.ts",
  "shell/loginStore.ts",
  "shell/sessionExpiry.ts",
];

const OAUTH_STATE_STORE = "adapters/auth/oauthStateStore.ts";

describe("M2 — 저장소 경계", () => {
  it("M2-a: `sessionStorage`는 oauthStateStore.ts 한 파일에만 나온다", () => {
    const users = ALL_SOURCES.filter((file) => /\bsessionStorage\b/.test(code(file))).map(rel);
    // 여기가 늘어나면 임시 상태(또는 최악의 경우 JWT)가 다른 곳에서 저장소에 새는 것이다.
    expect(users).toEqual([OAUTH_STATE_STORE]);
  });

  it("M2-b: 신규 인증 파일에 localStorage·indexedDB·document.cookie가 0건이다", () => {
    for (const file of AUTH_FILES) {
      const source = code(join(SRC, file));
      for (const forbidden of ["localStorage", "indexedDB", "document.cookie", "persist("]) {
        expect(source.includes(forbidden), `${file}: ${forbidden} 금지 — JWT는 메모리 전용(M2)`).toBe(
          false,
        );
      }
    }
  });

  it("검사 대상 파일이 실제로 존재한다(경로 오타로 검사가 무력화되지 않게)", () => {
    for (const file of AUTH_FILES) {
      expect(() => readFileSync(join(SRC, file), "utf8"), file).not.toThrow();
    }
    expect(ALL_SOURCES.length).toBeGreaterThan(40);
  });
});

describe("AUTH — 인증 배선 불변식", () => {
  it("AUTH-1: `sessionStore.login(`을 부르는 제품 코드는 콜백 러너 1곳뿐이다", () => {
    // `devLogin` 류의 세션 위조 헬퍼가 다시 생기는 것을 막는다. 정의 자신(`login(user) {`)은
    // 점(`.`)이 없어 걸리지 않는다.
    const callers = ALL_SOURCES.filter((file) => /\.login\s*\(/.test(code(file))).map(rel);
    expect(callers).toEqual(["screens/oauthCallback/oauthCallbackRunner.ts"]);
  });

  it("AUTH-2: googleSignIn이 `clientKind: \"web\"`을 보낸다", () => {
    // 빠지면 서버가 **desktop** client_id로 교환을 시도해 반드시 실패하고, 증상이
    // "로그인이 안 된다"로만 보여 원인 파악이 어렵다.
    const source = code(join(SRC, "adapters/auth/googleSignIn.ts"));
    const literal = /clientKind\s*:\s*"web"/.test(source);
    const viaConst =
      /OAUTH_CLIENT_KIND\s*=\s*"web"/.test(source) &&
      /clientKind\s*:\s*OAUTH_CLIENT_KIND/.test(source);
    expect(literal || viaConst, "clientKind가 web으로 고정돼 있어야 한다").toBe(true);
  });

  it("AUTH-3: 인증 파일의 logger 컨텍스트에 code·state·nonce·codeVerifier·token·pin 키가 없다", () => {
    // 이 키 이름은 `logPolicy`의 마스킹 대상이라 값이 `[masked]`가 된다 →
    // 진단이 무용해지고, 이름을 바꿔 우회하면 비밀이 실제로 새어 나간다.
    const forbidden = /logger\.\w+\([^)]*\b(code|state|nonce|codeVerifier|token|pin)\s*:/;
    for (const file of AUTH_FILES) {
      const matched = forbidden.exec(code(join(SRC, file)));
      expect(matched?.[0] ?? null, `${file}: 금지 키를 로그 컨텍스트에 담았다`).toBeNull();
    }
  });

  it("AUTH-4: `App.tsx`에 devLogin이 0건이다", () => {
    expect(readFileSync(join(SRC, "App.tsx"), "utf8")).not.toContain("devLogin");
  });

  it("AUTH-5: authorize URL에 `prompt=select_account`가 있다", () => {
    // 빠지면 브라우저에 남은 직전 손님(또는 운영자) Google 세션으로 **자격증명 입력 없이**
    // 원탭 로그인되어 QR 한도·프레임 권한을 획득한다(공용 키오스크 — 07 §2.2).
    expect(code(join(SRC, "domain/auth/authorizeUrl.ts"))).toContain("prompt=select_account");
  });
});

describe("인프라 — 개발 포트 정합(F29)", () => {
  it("dev 서버 포트가 5173이고 strictPort가 켜져 있다", () => {
    // Google Console 등록 URI·서버 `OAUTH_REDIRECT_ALLOWLIST`가 전부 5173이다.
    // 포트가 밀리면(strictPort 없이 5174로 이동) Google이 `redirect_uri_mismatch`로 거부한다.
    const source = readFileSync(join(ROOT, "vite.config.ts"), "utf8");
    expect(source).toMatch(/port:\s*5173/);
    expect(source).toMatch(/strictPort:\s*true/);
    expect(source).not.toContain("5273");
  });
});
