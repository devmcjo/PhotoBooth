import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { FRAME_DB_NAME } from "@adapters/storage/frameStore";
import { DIR_HANDLE_DB_NAME } from "@adapters/storage/dirHandleRepo";
import { LOG_DB_NAME } from "@adapters/storage/logStore";

/**
 * Step 14 정적 불변식 — 15 §3.4 관례("문서에만 있으면 언젠가 깨진다")
 *
 * 아래 6건은 깨져도 **테스트가 초록으로 남을 수 있는** 종류다: iOS에서만 저장이 죽거나(FR-1),
 * power의 삭제 능력이 조용히 사라지거나(FR-2), 프레임 DB가 영구 blocked 되거나(FR-3),
 * Step 15의 모달을 선점하거나(FR-5), 합성이 CORS 오염으로 전면 실패하거나(FR-6),
 * Step 8.5가 벡터로 고정한 판정이 사라진다(FR-7).
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

function code(relPath: string): string {
  return stripComments(readFileSync(join(SRC, relPath), "utf8"));
}

function rel(path: string): string {
  return relative(SRC, path).split(sep).join("/");
}

const ALL_SOURCES = collectSourceFiles(SRC);

const FRAME_SELECT_VIEW = "ui/views/FrameSelectView.tsx";
const COMPOSITOR = "adapters/compose/compositor.ts";
const FRAME_LOAD_POLICY = "domain/frames/frameLoadPolicy.ts";

/** OPFS를 **직접** 만지면 안 되는 파일들(전부 `OpfsClient` Worker RPC를 지나야 한다). */
const OPFS_INDIRECT_ONLY: readonly string[] = [
  "adapters/storage/frameStore.ts",
  "adapters/frames/frameCatalog.ts",
  "adapters/frames/frameImageCache.ts",
];

describe("검사 대상이 실제로 존재한다(경로 오타로 검사가 무력화되지 않게)", () => {
  it.each([...OPFS_INDIRECT_ONLY, FRAME_SELECT_VIEW, COMPOSITOR, FRAME_LOAD_POLICY, "main.tsx"])(
    "%s",
    (file) => {
      expect(() => readFileSync(join(SRC, file), "utf8")).not.toThrow();
    },
  );
});

describe("FR-1: OPFS 직접 접근 0건 (VF-14)", () => {
  it.each(OPFS_INDIRECT_ONLY)("%s", (file) => {
    // 메인 스레드에서 직접 쓰면 iOS/iPadOS Safari에서 **전 저장 경로**가 실패한다.
    const source = code(file);
    for (const forbidden of [
      "navigator.storage",
      "createWritable",
      "createSyncAccessHandle",
      "getDirectory(",
    ]) {
      expect(source.includes(forbidden), `${file}: ${forbidden} 금지`).toBe(false);
    }
  });
});

describe("FR-2: canDeleteFrame 호출이 2인자다", () => {
  it("소유자(userId)를 넘기는 호출이 src 전체에서 0건이다", () => {
    // 소유자 판정을 넣으면 power가 fork 저장한 **공용** 로컬 프레임(userId=null)의 삭제 능력이
    // 회귀한다(analysis/13 §6.1).
    const offenders: string[] = [];
    for (const file of ALL_SOURCES) {
      if (rel(file) === "domain/frames/frameEditPolicy.ts") continue; // 정의부
      const source = stripComments(readFileSync(file, "utf8"));
      const pattern = /canDeleteFrame\s*\(([^)]*)\)/g;
      let match: RegExpExecArray | null;
      while ((match = pattern.exec(source)) !== null) {
        const args = match[1]!.split(",").filter((a) => a.trim().length > 0);
        if (args.length !== 2) offenders.push(`${rel(file)}: ${match[0]}`);
      }
    }
    expect(offenders).toEqual([]);
  });
});

describe("FR-3: IndexedDB 이름이 셋 다 다르다", () => {
  it("프레임 DB ≠ 로그 DB ≠ 폴더 핸들 DB", () => {
    // 로그 스토어가 `mcphoto` 연결을 앱 수명 내내 붙들고 있고 `onversionchange`가 없어,
    // 같은 DB로 버전을 올리면 업그레이드가 **영구 blocked** 된다.
    expect(new Set([FRAME_DB_NAME, LOG_DB_NAME, DIR_HANDLE_DB_NAME]).size).toBe(3);
    expect(FRAME_DB_NAME).toBe("mcphoto-frames");
  });

  it("frameStore가 로그 DB 이름을 열지 않는다", () => {
    const source = code("adapters/storage/frameStore.ts");
    expect(source.includes('indexedDB.open("mcphoto"')).toBe(false);
    expect(source.includes("FRAME_DB_NAME")).toBe(true);
  });

  it("logStore의 `mcphoto` 버전이 여전히 1이다", () => {
    const source = code("adapters/storage/logStore.ts");
    expect(/LOG_DB_VERSION\s*=\s*1\b/.test(source)).toBe(true);
  });
});

describe("FR-5: FrameSelectView가 Step 15의 모달을 선점하지 않는다", () => {
  it("pushModal(·\"confirmDelete\"·\"framePicker\"가 0건이다", () => {
    // 삭제 확인은 **화면 로컬 오버레이**다(03 §790). 공용 모달은 Step 15가 소유한다.
    const source = code(FRAME_SELECT_VIEW);
    for (const forbidden of ["pushModal(", '"confirmDelete"', '"framePicker"']) {
      expect(source.includes(forbidden), `${FRAME_SELECT_VIEW}: ${forbidden} 금지`).toBe(false);
    }
  });

  it("Step 15 디렉터리를 만들지 않았다", () => {
    for (const dir of ["screens/modals/confirmDelete", "screens/modals/framePicker"]) {
      expect(() => readdirSync(join(SRC, dir)), dir).toThrow();
    }
  });
});

describe("FR-6: compositor가 원격 이미지에 CORS 규약을 유지한다 (WM2)", () => {
  it('`mode: "cors"` 문자열이 남아 있다', () => {
    // 없어지면 서버 프레임을 그린 canvas가 오염되어 `convertToBlob`이 SecurityError를 던진다 —
    // 손님은 6컷을 다 찍은 뒤에야 그것을 안다.
    expect(code(COMPOSITOR).includes('mode: "cors"')).toBe(true);
  });

  it("다운로더도 CORS-clean 옵션을 유지한다", () => {
    const source = code("adapters/frames/frameDownloader.ts");
    expect(source.includes('mode: "cors"')).toBe(true);
    expect(source.includes('credentials: "omit"')).toBe(true);
  });
});

describe("FR-7: frameLoadPolicy의 기존 export가 그대로 있다 (Step 8.5 산출물 보호)", () => {
  it.each([
    "FRAME_LOAD_PHASES",
    "DEFAULT_FRAME_LOAD_PHASE",
    "NO_PROGRESS_TIMEOUT_SECONDS",
    "MAX_TOTAL_WAIT_SECONDS",
    "nextFrameLoadDeadlineMs",
    "classifyFrameLoad",
    "finalizeFrameLoad",
    "frameLoadNotice",
  ])("%s가 export되어 있다", (name) => {
    // `docs/spec-vectors/frame-load-policy.json` 52케이스가 Windows와 교차 고정하는 이름들이다.
    const source = code(FRAME_LOAD_POLICY);
    expect(new RegExp(`export\\s+(?:const|function)\\s+${name}\\b`).test(source)).toBe(true);
  });

  it("Step 14가 추가한 것은 `isFrameListInteractive` 하나다", () => {
    const source = code(FRAME_LOAD_POLICY);
    expect(source.includes("export function isFrameListInteractive")).toBe(true);
  });
});

describe("Step 14 배선 불변식", () => {
  it("VF-12: fixFrameAndResolveCutCount 호출부가 여전히 1곳이다", () => {
    // 컷 수 해석 지점이 늘면 설정을 중간에 바꿨을 때 진행 중 세션의 컷 수가 바뀐다(it17).
    const callers: string[] = [];
    for (const file of ALL_SOURCES) {
      if (rel(file) === "shell/captureSessionController.ts") continue; // 정의부
      const source = stripComments(readFileSync(file, "utf8"));
      const matches = source.match(/fixFrameAndResolveCutCount\s*\(/g);
      for (const _ of matches ?? []) callers.push(rel(file));
    }
    expect(callers).toEqual(["screens/frameSelect/useFrameSelect.ts"]);
  });

  it("getUserFrames를 호출하는 곳이 0건이다(설계 이탈 ⑤ — 401 세션 해제 방지)", () => {
    // `auth:"required"`라 401이면 `handleSessionExpired`로 이어진다 — 프레임 목록을 여는 것만으로
    // 로그아웃 토스트가 뜬다. 얻는 것이 빈 배열인데 잃는 것이 세션이다.
    const callers = ALL_SOURCES.filter((file) => {
      if (rel(file) === "adapters/http/frameRepository.ts") return false; // 정의부
      return /getUserFrames\s*\(/.test(stripComments(readFileSync(file, "utf8")));
    }).map(rel);
    expect(callers).toEqual([]);
  });

  it("prefetch가 bootstrap()이 아니라 main.tsx의 startApp 말미에 있다", () => {
    const main = code("main.tsx");
    expect(main.includes("getFrameCatalog()")).toBe(true);
    expect(main.includes(".catch(")).toBe(true);
    expect(code("shell/bootstrap.ts").includes("getFrameCatalog")).toBe(false);
  });

  it("번들 매니페스트 규약 파일이 존재하고 빈 배열이다(VF-10 — 자산 미커밋)", () => {
    const raw = readFileSync(join(ROOT, "public", "frames", "index.json"), "utf8");
    expect(JSON.parse(raw)).toEqual([]);
  });

  it("console.* 직접 호출이 Step 14 신규 파일에 0건이다(logger만 쓴다)", () => {
    const step14 = [
      "domain/frames/frameStorePolicy.ts",
      "domain/frames/bundleManifest.ts",
      "adapters/storage/frameStore.ts",
      "adapters/frames/frameCatalog.ts",
      "adapters/frames/frameDownloader.ts",
      "adapters/frames/frameImageCache.ts",
      "adapters/frames/frameThumbnails.ts",
      "adapters/frames/bundleFrames.ts",
      "screens/frameSelect/frameLoadDeadline.ts",
      "screens/frameSelect/frameLoadRunner.ts",
      "screens/frameSelect/frameSelectActions.ts",
      "screens/frameSelect/useFrameSelect.ts",
      FRAME_SELECT_VIEW,
    ];
    for (const file of step14) {
      expect(/\bconsole\s*\./.test(code(file)), `${file}: console.* 금지`).toBe(false);
    }
  });
});
