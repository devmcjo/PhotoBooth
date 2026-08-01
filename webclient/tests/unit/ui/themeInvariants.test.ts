import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * 테마 정적 불변식 **THEME-1** — 15 §3.4 관례
 *
 * 색을 CSS 변수로 두지 않으면 **토큰을 바꿔도 따라오지 않는다.** 2026-08-01 팔레트 정합 작업 전
 * 모듈 CSS에는 `#fff`·`#000`이 16곳 박혀 있었고, 라이트 팔레트로 뒤집는 순간 흰 배경 위에 검은
 * 카드가 얹히는 식으로 전부 어긋났다.
 *
 * 남겨야 하는 예외는 **정확히 4곳**이다(전부 팔레트와 무관한 물리적/호환성 요구):
 *   ① `screens.module.css` `.flash`        — 물리적 플래시(터지는 흰빛)
 *   ② `screens.module.css` `.qrCanvas`     — QR 스캐너 호환(다크에서도 반전 금지)
 *   ③ `screens.module.css` `.countdown`    — 다크 프리뷰 위 가독성 text-shadow
 *   ④ `cameraTest.module.css` `.flash`     — ①과 동일
 *
 * ⚠️ `ui/theme/tokens.css`는 **토큰 정의처**라 검사 대상이 아니다.
 */

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SRC = join(ROOT, "src");

/** 토큰 정의처. 여기 색 리터럴이 있는 것이 정상이다. */
const TOKENS = "ui/theme/tokens.css";

function collectCssFiles(dir: string): string[] {
  const result: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      result.push(...collectCssFiles(full));
    } else if (entry.endsWith(".css")) {
      result.push(full);
    }
  }
  return result;
}

function rel(path: string): string {
  return relative(SRC, path).split(sep).join("/");
}

/** 색 리터럴: hex(#abc/#aabbcc) 또는 rgb()/rgba()에 숫자 3개가 직접 들어간 형태. */
const COLOR_LITERAL = /#[0-9a-fA-F]{3,8}\b|rgba?\(\s*\d/g;

const TARGET_DIRS = [join(SRC, "ui"), join(SRC, "screens")];

describe("THEME-1 — 모듈 CSS의 색 리터럴은 정확히 4곳뿐이다", () => {
  const hits: { file: string; line: number; text: string }[] = [];
  for (const dir of TARGET_DIRS) {
    for (const file of collectCssFiles(dir)) {
      if (rel(file) === TOKENS) continue;
      const lines = readFileSync(file, "utf8").split(/\r?\n/);
      lines.forEach((text, index) => {
        // 주석 줄은 무시한다 — 설명에 색 값을 적을 수 있어야 한다.
        const trimmed = text.trim();
        if (trimmed.startsWith("*") || trimmed.startsWith("/*")) return;
        if (COLOR_LITERAL.test(text)) hits.push({ file: rel(file), line: index + 1, text: trimmed });
        COLOR_LITERAL.lastIndex = 0;
      });
    }
  }

  it("남은 색 리터럴이 4곳이고 전부 알려진 예외다", () => {
    expect(hits.map((h) => `${h.file}:${h.line}`)).toHaveLength(4);
    const bodies = hits.map((h) => h.text);
    // ①④ 플래시 2곳 · ② QR 캔버스 1곳 · ③ 카운트다운 text-shadow 1곳.
    expect(bodies.filter((t) => t === "background: #fff;")).toHaveLength(3);
    expect(bodies.filter((t) => t.startsWith("text-shadow:"))).toHaveLength(1);
    expect(hits.filter((h) => h.file.endsWith("cameraTest.module.css"))).toHaveLength(1);
    expect(hits.filter((h) => h.file === "ui/views/screens.module.css")).toHaveLength(3);
  });

  it("예외 4곳 바로 위에 유지 근거 주석이 있다 — '토큰화 누락'으로 오해하지 않게", () => {
    for (const hit of hits) {
      const lines = readFileSync(join(SRC, hit.file), "utf8").split(/\r?\n/);
      // 선언이 속한 규칙 블록 바로 위 주석에 근거가 있어야 한다(최대 12줄 위까지 본다).
      const above = lines.slice(Math.max(0, hit.line - 13), hit.line - 1).join("\n");
      expect(
        above.includes("토큰화 금지") || above.includes("스캐너") || above.includes("가독성"),
        `${hit.file}:${hit.line} 에 유지 근거 주석이 없다`,
      ).toBe(true);
    }
  });
});

describe("THEME-1 보조 — 팔레트 회귀 방지", () => {
  it("종전 다크 배경 `#0e0e12`가 저장소 어디에도 없다", () => {
    const files = [
      ...collectCssFiles(SRC),
      join(ROOT, "index.html"),
      join(ROOT, "public", "manifest.webmanifest"),
    ];
    for (const file of files) {
      expect(readFileSync(file, "utf8"), file).not.toContain("0e0e12");
    }
  });

  it("`main.css`가 `:root`에 색 토큰을 다시 정의하지 않는다(라이트 모드 파손의 원인이었다)", () => {
    const source = readFileSync(join(SRC, "main.css"), "utf8");
    expect(source).not.toMatch(/^\s*--bg:/m);
    expect(source).not.toMatch(/^\s*--fg:/m);
    expect(source).not.toMatch(/^\s*--muted:/m);
  });

  it("`tokens.css`가 라이트 기본 + 다크 파생이다", () => {
    const source = readFileSync(join(SRC, TOKENS), "utf8");
    expect(source).toContain("color-scheme: light dark");
    expect(source).toContain("--bg: #ffffff");
    expect(source).toContain("@media (prefers-color-scheme: dark)");
    // 기존 토큰 이름이 별칭으로 살아 있어야 한다(모듈 CSS 13개가 쓴다).
    for (const alias of ["--accent-fg:", "--bg-scrim:", "--shadow:", "--gap:", "--fs-title:"]) {
      expect(source, alias).toContain(alias);
    }
  });

  it("터치 타깃이 48px 미만으로 내려간 선언이 없다", () => {
    const suspicious = /min-height:\s*(\d+)px/g;
    for (const dir of TARGET_DIRS) {
      for (const file of collectCssFiles(dir)) {
        const source = readFileSync(file, "utf8");
        let match: RegExpExecArray | null;
        while ((match = suspicious.exec(source)) !== null) {
          const px = Number(match[1]);
          // 터치 대상이 아닌 장식·행 높이는 여기 걸리지 않는다(48 미만 min-height는 쓰지 않는다).
          expect(px >= 48, `${rel(file)}: min-height ${px}px`).toBe(true);
        }
      }
    }
  });

  it("`outline: none`·`outline: 0`이 어디에도 없다(포커스 링 제거 금지 — 12 B-n3)", () => {
    for (const dir of TARGET_DIRS) {
      for (const file of collectCssFiles(dir)) {
        const source = readFileSync(file, "utf8");
        expect(source, rel(file)).not.toMatch(/outline:\s*(none|0)\b/);
      }
    }
    expect(readFileSync(join(SRC, "main.css"), "utf8")).not.toMatch(/outline:\s*(none|0)\b/);
  });

  it("토글 스위치에 `transition`이 없다(WPF는 즉시 스냅이다)", () => {
    const source = readFileSync(join(SRC, "ui/components/fields.module.css"), "utf8");
    const toggleBlock = source.slice(source.indexOf(".toggle {"), source.indexOf(".choiceGroup"));
    expect(toggleBlock).not.toContain("transition");
  });
});

/**
 * **THEME-2** — accent 배경 위 텍스트의 명암비 근거를 코드로 고정한다(2026-08-01 팀 리드 조건).
 *
 * `#FFFFFF` on `#FF4D79` = **3.19:1**. 이 배경 위에서 WCAG AA 일반 텍스트(4.5:1)를 만족하는
 * **밝은 색은 존재하지 않으므로**, 흰 글자를 쓰는 곳은 large text(≥18.66px = 14pt Bold)여야
 * 3:1 기준으로 통과한다. 그렇게 키울 수 없는 작은 요소는 `--on-accent-ink`(5.05:1)를 쓴다.
 *
 * 이 테스트가 깨지면 **접근성 판정이 무너진 것**이다 — 값을 되돌리거나 §H8을 다시 판정해야 한다.
 */
describe("THEME-2 — accent 위 흰 텍스트는 large text여야 한다", () => {
  /** WCAG large text 하한: 14pt Bold = 18.66…px. */
  const LARGE_TEXT_MIN_PX = 18.66;

  function blockOf(source: string, selector: string): string {
    const start = source.indexOf(selector);
    expect(start, `${selector} 규칙을 찾지 못했다`).toBeGreaterThanOrEqual(0);
    const open = source.indexOf("{", start);
    const close = source.indexOf("}", open);
    return source.slice(open, close);
  }

  it("`.primary`의 font-size가 18.66px 이상이다", () => {
    const source = readFileSync(join(SRC, "ui/components/components.module.css"), "utf8");
    const match = /font-size:\s*([\d.]+)rem/.exec(blockOf(source, ".primary {"));

    expect(match, ".primary에 font-size 선언이 없다(기본 16px로 떨어져 AA가 깨진다)").not.toBeNull();
    expect(Number(match?.[1]) * 16).toBeGreaterThanOrEqual(LARGE_TEXT_MIN_PX);
  });

  it("`--on-accent`는 흰색이고 `--on-accent-ink`는 잉크다", () => {
    const source = readFileSync(join(SRC, TOKENS), "utf8");
    expect(source).toMatch(/^\s*--on-accent:\s*#ffffff;/m);
    expect(source).toMatch(/^\s*--on-accent-ink:\s*#241f2b;/m);
  });

  it("`--on-accent-ink`는 다크에서 뒤집히지 않는다(accent가 그대로라 잉크여야 한다)", () => {
    const source = readFileSync(join(SRC, TOKENS), "utf8");
    const dark = source.slice(source.indexOf("@media (prefers-color-scheme: dark)"));
    expect(dark).not.toContain("--on-accent-ink:");
    expect(dark).not.toMatch(/^\s*--accent:/m);
  });

  it("작은 요소 3곳은 `--on-accent`가 아니라 `--on-accent-ink`를 쓴다", () => {
    const fields = readFileSync(join(SRC, "ui/components/fields.module.css"), "utf8");
    const screens = readFileSync(join(SRC, "ui/views/screens.module.css"), "utf8");

    expect(blockOf(fields, '.choice.choice[aria-pressed="true"] {')).toContain(
      "var(--on-accent-ink)",
    );
    expect(blockOf(screens, ".autoBadge {")).toContain("var(--on-accent-ink)");
    expect(blockOf(screens, ".cutOrder {")).toContain("var(--on-accent-ink)");
  });
});
