import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { GUEST_LOCKED_KEYS } from "@domain/settings/appSettings";
import { PIN_LOCK_STORAGE_KEY } from "@adapters/storage/pinLockRepo";

/**
 * PIN 게이트 · 설정 화면 정적 불변식 — 15 §3.4 관례("문서에만 있으면 언젠가 깨진다")
 *
 * 아래 8건은 깨져도 **테스트가 초록으로 남을 수 있는** 종류다:
 * PIN이 로그로 새거나, PIN 1회 오입력이 로그아웃을 유발하거나, 게이트를 우회해 모달만 뜨거나,
 * 화면이 도메인을 우회해 clamp하거나, 게스트 조작이 운영자 값을 덮는다.
 * 그래서 **소스를 읽어** 검사한다(`authInvariants.test.ts`가 같은 형태의 선례다).
 */

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SRC = join(ROOT, "src");

function collectSourceFiles(dir: string): string[] {
  const result: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) result.push(...collectSourceFiles(full));
    else if (entry.endsWith(".ts") || entry.endsWith(".tsx")) result.push(full);
  }
  return result;
}

/** 주석 제거 — 설명 문구가 금지 패턴에 걸리지 않게(`authInvariants.test.ts`와 같은 방식). */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
}

function code(path: string): string {
  return stripComments(readFileSync(path, "utf8"));
}

function rel(path: string): string {
  return relative(SRC, path).split(sep).join("/");
}

const ALL_SOURCES = collectSourceFiles(SRC);

/** PIN을 다루는 파일 전부. 새 파일이 생기면 여기에 추가한다. */
const PIN_FILES: readonly string[] = [
  "domain/auth/pinGatePolicy.ts",
  "adapters/storage/pinLockRepo.ts",
  "shell/pinGate.ts",
  "screens/modals/pinPrompt/pinPromptRunner.ts",
  "screens/modals/pinPrompt/PinPromptModal.tsx",
  "screens/settings/settingsForm.ts",
];

const ACCOUNT_SERVICE = "adapters/http/accountService.ts";
const SETTINGS_VIEW = "ui/views/SettingsView.tsx";
const SETTINGS_FORM = "screens/settings/settingsForm.ts";
const SETTINGS_STORE = "shell/settingsStore.ts";
const PIN_GATE = "shell/pinGate.ts";

describe("검사 대상이 실제로 존재한다(경로 오타로 검사가 무력화되지 않게)", () => {
  it.each([...PIN_FILES, ACCOUNT_SERVICE, SETTINGS_VIEW, SETTINGS_STORE, "App.tsx"])(
    "%s",
    (file) => {
      expect(() => readFileSync(join(SRC, file), "utf8")).not.toThrow();
    },
  );
});

describe("PIN — 비밀 취급·로그아웃·게이트 우회", () => {
  it("PIN-1: PIN 파일의 logger 컨텍스트에 pin·code·state·token 키가 0건이다", () => {
    // 이 키 이름은 `logPolicy`의 마스킹 대상이라 값이 `[masked]`가 되고,
    // 마스킹을 피하려 이름을 바꾸면 **PIN이 실제로 로그에 남는다**.
    const forbidden =
      /logger\.\w+\([^)]*\b(pin|newPin|currentPin|code|state|nonce|token)\s*:/i;
    for (const file of PIN_FILES) {
      const matched = forbidden.exec(code(join(SRC, file)));
      expect(matched?.[0] ?? null, `${file}: 금지 키를 로그 컨텍스트에 담았다`).toBeNull();
    }
  });

  it("PIN-2: verifyMyPin·setMyPin 둘 다 `unauthorized: \"reject\"`를 넘긴다", () => {
    // 빠지면 `backendClient`의 기본값(Bearer가 붙었으면 `expired`)이 적용되어
    // **PIN을 한 번 틀렸을 때 로그아웃**된다(E17).
    const source = code(join(SRC, ACCOUNT_SERVICE));
    for (const method of ["verifyMyPin", "setMyPin"]) {
      const start = source.indexOf(`async ${method}`);
      expect(start, `${method}를 찾지 못했다`).toBeGreaterThanOrEqual(0);
      const rest = source.slice(start + 1);
      const end = rest.indexOf("async ");
      const block = end < 0 ? rest : rest.slice(0, end);
      expect(block, `${method}에 unauthorized: "reject"가 없다`).toContain(
        'unauthorized: "reject"',
      );
    }
  });

  it("PIN-2b: resetOtherPin은 손대지 않았다(그 라우트의 401은 진짜 만료뿐이다)", () => {
    const source = code(join(SRC, ACCOUNT_SERVICE));
    const start = source.indexOf("async resetOtherPin");
    const block = source.slice(start);
    expect(block).not.toContain('unauthorized: "reject"');
  });

  it("PIN-3: 잠금 저장 키 문자열이 pinLockRepo.ts 한 파일에만 있다", () => {
    // 두 곳이 쓰면 형식이 갈라져 잠금이 조용히 무력화된다.
    const users = ALL_SOURCES.filter((file) => code(file).includes(PIN_LOCK_STORAGE_KEY)).map(rel);
    expect(users).toEqual(["adapters/storage/pinLockRepo.ts"]);
  });

  it("PIN-4: `pinPrompt` 모달을 pushModal 하는 곳은 shell/pinGate.ts 뿐이다", () => {
    // 게이트를 우회해 모달만 띄우는 경로가 생기면 승인 없이 설정이 열린다.
    const pattern = /pushModal\s*\([^)]*pinPrompt/;
    const users = ALL_SOURCES.filter((file) => pattern.test(code(file))).map(rel);
    expect(users).toEqual([PIN_GATE]);
  });

  it("PIN-5: 게이트 판정 파일이 localStorage를 직접 만지지 않는다(저장은 어댑터가 소유)", () => {
    for (const file of ["shell/pinGate.ts", "screens/modals/pinPrompt/pinPromptRunner.ts"]) {
      expect(code(join(SRC, file)).includes("localStorage"), file).toBe(false);
    }
  });
});

describe("SET — 설정 화면이 규격을 우회하지 않는다", () => {
  it("SET-1: 화면·폼이 clamp·최근접 보정·QR 정규화를 직접 부르지 않는다", () => {
    // 화면이 보정하면 진실원(analysis/41 §2)이 둘이 되어 Windows와 값이 갈라진다.
    for (const file of [SETTINGS_VIEW, SETTINGS_FORM]) {
      const source = code(join(SRC, file));
      for (const forbidden of ["clampSettings(", "closestFrom(", "normalizeQrToggles("]) {
        expect(source.includes(forbidden), `${file}: ${forbidden} 금지`).toBe(false);
      }
    }
  });

  it("SET-2: GUEST_LOCKED_KEYS 전부가 SettingsView의 잠금 표시를 지난다", () => {
    // 새 제한 키가 생겼을 때 렌더 가드만 빠지는 것을 막는다.
    const source = code(join(SRC, SETTINGS_VIEW));
    for (const key of GUEST_LOCKED_KEYS) {
      expect(source.includes(`badge("${key}")`), `${key}에 잠금 배지가 없다`).toBe(true);
      expect(source.includes(`locked("${key}")`), `${key}에 렌더 가드가 없다`).toBe(true);
    }
  });

  it("SET-3: App.tsx에 Step 6·10의 임시 진입점이 남아 있지 않다", () => {
    // 남아 있으면 진입로가 둘이 되고, 설정 화면 밖에서 폴더가 바뀐다.
    const source = readFileSync(join(SRC, "App.tsx"), "utf8");
    for (const forbidden of ["로컬 저장 폴더 선택", "카메라 테스트 열기", "pickLocalSaveFolder"]) {
      expect(source.includes(forbidden), `App.tsx에 ${forbidden}가 남아 있다`).toBe(false);
    }
  });

  it("SET-4: settingsStore에 `isGuest: false` 하드코딩이 0건이다", () => {
    // 하드코딩이 재발하면 게스트 조작이 운영자 값으로 기록된다(F6 회귀).
    const source = code(join(SRC, SETTINGS_STORE));
    expect(/isGuest\s*:\s*false/.test(source)).toBe(false);
  });

  it("SET-5: 설정 화면이 settingsRepo에 직접 쓰지 않는다(clamp·게스트 제한 우회 금지)", () => {
    // 저장은 반드시 `settingsStore.save`를 지나야 한다.
    for (const file of ALL_SOURCES.filter((f) => rel(f).startsWith("screens/settings/"))) {
      expect(/\brepo\.save\s*\(|settingsRepo\.save\s*\(/.test(code(file)), rel(file)).toBe(false);
    }
  });
});
