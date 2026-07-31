import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * 계정 · 사용자 관리 · 진단 · SW 정적 불변식 — 15 §3.4 관례("문서에만 있으면 언젠가 깨진다")
 *
 * 아래 9건은 깨져도 **테스트가 초록으로 남을 수 있는** 종류다:
 * 화면이 역할 문자열을 비교하거나, 권한 가드가 서버 왕복 뒤로 밀리거나, SW가 촬영 중 앱을
 * 갱신하거나, 게이트 키 값이 화면·로그로 샌다. 그래서 **소스를 읽어** 검사한다.
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

/** 주석 제거 — 설명 문구가 금지 패턴에 걸리지 않게(`settingsInvariants.test.ts`와 같은 방식). */
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

/** 계정·사용자 관리 화면과 그 러너 전부. 새 파일이 생기면 자동 포함된다(디렉터리 스캔). */
const ACCOUNT_SCREEN_FILES = ALL_SOURCES.filter((file) => {
  const path = rel(file);
  return (
    path.startsWith("screens/account/") ||
    path.startsWith("screens/userMgmt/") ||
    path === "ui/views/AccountView.tsx" ||
    path === "ui/views/UserMgmtView.tsx"
  );
});

const SW_FILE = join(SRC, "sw.ts");
const APP_FILE = join(SRC, "App.tsx");

/**
 * 게이트 키를 다룰 수 있는 파일 전부. `serverStatusPanel.ts`를 포함하는 이유:
 * **실제로 `env.backendApiKey`를 읽는 유일한 파일**이라 빼면 DIAG-1이 공회전한다.
 */
const DIAGNOSTIC_FILES = ALL_SOURCES.filter((file) => {
  const path = rel(file);
  return (
    path.startsWith("screens/modals/diagnostics/") ||
    path.startsWith("ui/views/") ||
    path === "screens/settings/serverStatusPanel.ts"
  );
});

describe("검사 대상이 실제로 존재한다(경로 오타로 검사가 무력화되지 않게)", () => {
  it("계정·사용자 관리 파일이 6개 이상이다", () => {
    expect(ACCOUNT_SCREEN_FILES.length).toBeGreaterThanOrEqual(6);
  });

  it.each([
    "ui/views/AccountView.tsx",
    "ui/views/UserMgmtView.tsx",
    "screens/account/accountMenu.ts",
    "screens/userMgmt/userActions.ts",
    "screens/userMgmt/pinResetRunner.ts",
    "screens/modals/diagnostics/diagnosticsPresenter.ts",
    "sw.ts",
  ])("%s", (file) => {
    expect(() => readFileSync(join(SRC, file), "utf8")).not.toThrow();
  });
});

describe("ACC — 역할 게이트가 도메인에 남아 있다", () => {
  it("ACC-1: 계정·사용자 관리 화면에 역할 문자열 리터럴이 0건이다", () => {
    // 판정은 `accountAdminPolicy`가 소유한다. 화면이 비교하면 서버 매트릭스와 조용히 갈라진다.
    const roleLiteral = /["'](manager|admin|advanced_user|temp_user)["']/;
    for (const file of ACCOUNT_SCREEN_FILES) {
      const matched = roleLiteral.exec(code(file));
      expect(matched?.[0] ?? null, `${rel(file)}: 역할 문자열 리터럴 금지`).toBeNull();
    }
  });

  it("ACC-1: 계정·사용자 관리 화면에 `.role ===` 비교가 0건이다", () => {
    const roleCompare = /\.role\s*(===|!==)/;
    for (const file of ACCOUNT_SCREEN_FILES) {
      const matched = roleCompare.exec(code(file));
      expect(matched?.[0] ?? null, `${rel(file)}: 역할 직접 비교 금지`).toBeNull();
    }
  });

  it("ACC-2: 액션 3종이 **첫 실행문에서** 도메인 판정을 부른다", () => {
    // 가드가 뒤로 밀리면 권한 없는 요청이 먼저 서버로 나간다(FR-10 선례).
    const targets = [
      { file: "screens/userMgmt/userActions.ts", fn: "runDeleteAccount" },
      { file: "screens/userMgmt/userActions.ts", fn: "runSetRole" },
      { file: "screens/userMgmt/pinResetRunner.ts", fn: "runPinReset" },
    ];

    for (const target of targets) {
      const source = code(join(SRC, target.file));
      const start = source.indexOf(`export async function ${target.fn}`);
      expect(start, `${target.fn}을 찾지 못했다`).toBeGreaterThanOrEqual(0);

      const rest = source.slice(start);
      const nextExport = rest.indexOf("\nexport ", 1);
      const body = nextExport < 0 ? rest : rest.slice(0, nextExport);

      const guardIndex = Math.min(
        ...["buildUserRow(", "canResetPin("]
          .map((needle) => body.indexOf(needle))
          .filter((index) => index >= 0),
      );
      const depsIndex = body.indexOf("deps.");

      expect(Number.isFinite(guardIndex), `${target.fn}: 도메인 판정 호출이 없다`).toBe(true);
      expect(depsIndex, `${target.fn}: deps 사용이 없다`).toBeGreaterThanOrEqual(0);
      expect(
        guardIndex < depsIndex,
        `${target.fn}: 권한 가드가 첫 실행문이 아니다`,
      ).toBe(true);
    }
  });

  it("ACC-3: 계정·사용자 관리 화면에 `pushModal(`가 0건이다", () => {
    // PIN 재설정·삭제 확인·키오스크 종료는 전부 **화면 로컬 오버레이**다(FR-5·FR-8 계열).
    for (const file of ACCOUNT_SCREEN_FILES) {
      expect(code(file).includes("pushModal("), `${rel(file)}: 셸 모달 금지`).toBe(false);
    }
  });

  it("ACC-4: App.tsx의 Account·UserMgmt 케이스가 둘 다 `<PinGate`로 감싸져 있다", () => {
    const source = code(APP_FILE);
    for (const screen of ["Account", "UserMgmt"]) {
      const start = source.indexOf(`case "${screen}":`);
      expect(start, `case "${screen}"을 찾지 못했다`).toBeGreaterThanOrEqual(0);
      const rest = source.slice(start);
      const end = rest.indexOf("case ", 1);
      const block = end < 0 ? rest : rest.slice(0, end);
      expect(block.includes("<PinGate"), `${screen}에 PIN 게이트가 없다`).toBe(true);
    }
  });
});

describe("SW — 촬영 중 갱신·API 캐시를 구조적으로 막는다", () => {
  it("SW-1: `install` 리스너에 `skipWaiting`이 0건이고 파일 전체에 1회만 등장한다", () => {
    // 자동 갱신이 되살아나면 **촬영 중 앱이 바뀐다**.
    const source = code(SW_FILE);
    const installStart = source.indexOf('addEventListener("install"');
    expect(installStart).toBeGreaterThanOrEqual(0);
    const rest = source.slice(installStart);
    const installEnd = rest.indexOf("addEventListener(", 1);
    const installBlock = installEnd < 0 ? rest : rest.slice(0, installEnd);
    expect(installBlock.includes("skipWaiting")).toBe(false);

    const occurrences = source.split("skipWaiting").length - 1;
    expect(occurrences, "skipWaiting은 message 핸들러 1곳뿐이다").toBe(1);
  });

  it("SW-2: `classifySwRequest(`가 `respondWith(`보다 먼저 등장한다", () => {
    // 분류를 건너뛴 `respondWith`는 API 응답까지 캐시한다.
    const source = code(SW_FILE);
    const classify = source.indexOf("classifySwRequest(");
    const respond = source.indexOf("respondWith(");
    expect(classify).toBeGreaterThanOrEqual(0);
    expect(respond).toBeGreaterThanOrEqual(0);
    expect(classify < respond).toBe(true);
  });

  it("SW-3: `sw.ts`에 `logger` import·`console.`이 0건이다", () => {
    // SW에는 로그 스토어가 붙지 않아 여기서 남긴 로그는 진단에 도달하지 않는다(15 §4 함정 #12).
    const source = code(SW_FILE);
    expect(/from\s+["'][^"']*logStore["']/.test(source)).toBe(false);
    expect(source.includes("logger.")).toBe(false);
    expect(source.includes("console.")).toBe(false);
  });

  it("WD5: 소스 전체에 `window.close`가 0건이다(탭은 스크립트로 닫을 수 없다)", () => {
    const users = ALL_SOURCES.filter((file) => code(file).includes("window.close")).map(rel);
    expect(users).toEqual([]);
  });
});

describe("DIAG — 게이트 키 값이 새지 않는다", () => {
  it("DIAG-1: `backendApiKey`가 등장하는 줄에 `.length`·`.trim()`이 반드시 있다", () => {
    // 검사가 공회전하지 않는지 먼저 확인한다(대상 줄이 실제로 존재해야 한다).
    const mentions = DIAGNOSTIC_FILES.filter((file) => code(file).includes("backendApiKey"));
    expect(mentions.length, "게이트 키를 읽는 파일이 검사 대상에 없다").toBeGreaterThan(0);

    for (const file of DIAGNOSTIC_FILES) {
      const lines = code(file).split("\n");
      for (const [index, line] of lines.entries()) {
        if (!line.includes("backendApiKey")) continue;
        expect(
          line.includes(".length") || line.includes(".trim()"),
          `${rel(file)}:${index + 1} — 게이트 키 **값**이 문자열에 들어갈 수 있다`,
        ).toBe(true);
      }
    }
  });

  it("DIAG-2: 진단·계정 모듈의 logger 컨텍스트에 비밀 키가 0건이다", () => {
    // `logPolicy`의 마스킹 대상 이름을 바꿔 우회하면 **진짜로 샌다**(PIN-1과 같은 축).
    const forbidden =
      /logger\.\w+\([^)]*\b(pin|newPin|currentPin|apiKey|backendApiKey|code|state|nonce|token)\s*:/i;
    const files = [...ACCOUNT_SCREEN_FILES, ...DIAGNOSTIC_FILES];
    for (const file of files) {
      const matched = forbidden.exec(code(file));
      expect(matched?.[0] ?? null, `${rel(file)}: 금지 키를 로그 컨텍스트에 담았다`).toBeNull();
    }
  });
});

describe("FR — 저장 경로 규약(Step 16 신규분)", () => {
  it("`exportImport.ts`에 `fetch(`가 0건이다(이미지는 OPFS에서 직접 읽는다)", () => {
    // blob URL을 fetch하면 CSP `connect-src`에 걸릴 수 있고(A1), 디스크→메모리 왕복이 한 번 더 생긴다.
    const source = code(join(SRC, "adapters/storage/exportImport.ts"));
    expect(/\bfetch\s*\(/.test(source)).toBe(false);
  });

  it("`frameStore.ts`가 여전히 저장소를 직접 만지지 않는다(FR-1)", () => {
    const source = code(join(SRC, "adapters/storage/frameStore.ts"));
    for (const forbidden of [
      "navigator.storage",
      "createWritable",
      "createSyncAccessHandle",
      "getDirectory(",
    ]) {
      expect(source.includes(forbidden), `frameStore.ts: ${forbidden} 금지`).toBe(false);
    }
  });

  it("`zipStore.ts`는 **import 0**이다(순수 코덱)", () => {
    const source = code(join(SRC, "adapters/storage/zipStore.ts"));
    expect(/(^|\n)\s*import\s/.test(source)).toBe(false);
  });

  it("`swPolicy.ts`는 **import 0**이다(순수 분류기)", () => {
    const source = code(join(SRC, "adapters/platform/swPolicy.ts"));
    expect(/(^|\n)\s*import\s/.test(source)).toBe(false);
  });
});
