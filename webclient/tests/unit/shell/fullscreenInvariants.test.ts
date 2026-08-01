import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { installFirstGestureHandlers } from "../../../src/App";

/**
 * 전체화면 정적 불변식 **FS-1** — 15 §3.4 관례("문서에만 있으면 언젠가 깨진다")
 *
 * 2026-08-01 이슈 ③의 원인은 **전체화면을 부르는 곳이 하나 늘어난 것**이었다
 * (`main.tsx`의 첫 제스처 콜백). 손님이 화면 아무 곳이나 만지면 전체화면으로 들어가
 * "원인 없는 상태 변화"가 됐다. 문서로만 두면 다음 사람이 다시 `main.tsx`에 넣는다.
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
const REQUEST_CALL = /getFullscreenController\(\)\s*\.\s*request\s*\(/g;

describe("FS-1 — 전체화면 진입점은 App.tsx 2곳뿐이다", () => {
  it("`getFullscreenController().request(` 호출이 정확히 2곳이고 둘 다 App.tsx다", () => {
    const hits: string[] = [];
    for (const file of ALL_SOURCES) {
      const matches = code(file).match(REQUEST_CALL);
      for (let i = 0; i < (matches?.length ?? 0); i += 1) hits.push(rel(file));
    }
    // ① 이탈 배너 [다시 전체화면으로]  ② 상단바 [전체화면] 버튼
    expect(hits).toEqual(["App.tsx", "App.tsx"]);
  });

  it("`main.tsx`에 전체화면 요청이 0건이다(첫 제스처 자동 진입 폐지)", () => {
    const source = code(join(SRC, "main.tsx"));
    expect(source).not.toMatch(/\.request\s*\(/);
    // 컨트롤러 import 자체는 남아 있어야 한다 — `installShellHandlers`의 `.install()`이 쓴다.
    expect(source).toContain("getFullscreenController().install()");
  });

  it("첫 제스처 콜백이 Wake Lock만 요청한다(화면 꺼짐 회귀 금지)", () => {
    // `main.tsx`는 부트스트랩을 실행하므로 import할 수 없다 → 콜백 계약만 여기서 확인하고,
    // 콜백 내용은 위 정적 검사가 고정한다.
    const listeners = new Map<string, EventListener>();
    const target = {
      addEventListener: (type: string, fn: EventListener) => listeners.set(type, fn),
      removeEventListener: (type: string) => listeners.delete(type),
    };

    let calls = 0;
    const remove = installFirstGestureHandlers(() => {
      calls += 1;
    }, target);

    expect([...listeners.keys()].sort()).toEqual(["keydown", "pointerdown"]);
    listeners.get("pointerdown")?.(new Event("pointerdown"));
    expect(calls).toBe(1);
    // 1회성이다 — 두 리스너 모두 해제된다(누수·중복 호출 방지).
    expect(listeners.size).toBe(0);
    remove();
  });
});
