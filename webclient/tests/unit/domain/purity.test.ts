import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * 도메인 순수성 기계 검증 — 01 §2.1 계층 규칙
 *
 * `src/domain`은 **아무것도 import하지 않는다**(도메인 내부 상대 경로만 허용)고,
 * 브라우저·Node API·시각·난수를 **직접 부르지 않는다**(전부 인자·포트로 주입).
 * 이 테스트가 없으면 나중에 누군가 편의상 `Date.now()`를 넣고 단위 테스트가 시간에 의존하게 된다.
 */

const DOMAIN_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src", "domain");

function collectTsFiles(dir: string): string[] {
  const result: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      result.push(...collectTsFiles(full));
    } else if (entry.endsWith(".ts")) {
      result.push(full);
    }
  }
  return result;
}

const files = collectTsFiles(DOMAIN_DIR);

/** import·export-from 문의 모듈 지정자를 모두 뽑는다. */
function moduleSpecifiers(source: string): string[] {
  const specifiers: string[] = [];
  const pattern = /(?:^|\n)\s*(?:import|export)\b[^;\n]*?from\s+["']([^"']+)["']/g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(source)) !== null) {
    specifiers.push(match[1]!);
  }
  // side-effect import: `import "x";`
  const bare = /(?:^|\n)\s*import\s+["']([^"']+)["']/g;
  while ((match = bare.exec(source)) !== null) {
    specifiers.push(match[1]!);
  }
  return specifiers;
}

const FORBIDDEN_PATTERNS: readonly { readonly name: string; readonly pattern: RegExp }[] = [
  { name: "Date.now()", pattern: /\bDate\.now\s*\(/ },
  { name: "new Date() (인자 없는 현재 시각)", pattern: /new\s+Date\s*\(\s*\)/ },
  { name: "Math.random()", pattern: /\bMath\.random\s*\(/ },
  { name: "crypto", pattern: /\bcrypto\s*\./ },
  { name: "fetch()", pattern: /\bfetch\s*\(/ },
  { name: "localStorage", pattern: /\blocalStorage\b/ },
  { name: "sessionStorage", pattern: /\bsessionStorage\b/ },
  { name: "indexedDB", pattern: /\bindexedDB\b/ },
  { name: "window", pattern: /\bwindow\b/ },
  { name: "document", pattern: /\bdocument\s*\./ },
  { name: "navigator", pattern: /\bnavigator\b/ },
  { name: "performance", pattern: /\bperformance\s*\./ },
  { name: "console (로그 스토어 우회)", pattern: /\bconsole\s*\./ },
  { name: "process", pattern: /\bprocess\s*\./ },
];

/** 주석을 제거한다(주석 안의 설명 문구가 금지 패턴에 걸리지 않게). */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
}

describe("도메인 순수성", () => {
  it("이식할 파일이 실제로 존재한다", () => {
    expect(files.length).toBeGreaterThanOrEqual(20);
  });

  it.each(files.map((f) => [f.slice(DOMAIN_DIR.length + 1), f] as const))(
    "%s — 도메인 밖을 import하지 않는다",
    (_label, file) => {
      const specifiers = moduleSpecifiers(readFileSync(file, "utf8"));
      for (const specifier of specifiers) {
        expect(
          specifier.startsWith("."),
          `${specifier} — 도메인은 상대 경로(도메인 내부)만 import할 수 있다`,
        ).toBe(true);
      }
    },
  );

  it.each(files.map((f) => [f.slice(DOMAIN_DIR.length + 1), f] as const))(
    "%s — 브라우저·Node API·시각·난수를 직접 부르지 않는다",
    (_label, file) => {
      const code = stripComments(readFileSync(file, "utf8"));
      for (const { name, pattern } of FORBIDDEN_PATTERNS) {
        expect(pattern.test(code), `${name} 사용 금지 — 인자·포트로 주입한다`).toBe(false);
      }
    },
  );
});
