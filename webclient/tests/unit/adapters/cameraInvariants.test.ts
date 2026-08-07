import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { constraintLadder, shouldTryNextStep } from "@adapters/camera/cameraConstraints";

/**
 * 카메라 정적 불변식 **CAM-1 · CAM-7 · CAM-8 · CAM-9** — 15 §3.4 관례
 *
 * `01 §2.1`의 **하드웨어 단일 소유**를 권한 프라이밍이 우회하지 못하게 한다.
 * `getUserMedia`를 부르는 파일이 늘면 실촬영 스트림과 충돌하거나, 프라이밍 스트림을 멈추지 않아
 * **카메라 LED가 켜진 채** 남는다(Guide 화면에 머무는 내내).
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

const CAMERA_SERVICE = "adapters/camera/cameraService.ts";
const CAMERA_PERMISSION = "adapters/camera/cameraPermission.ts";

describe("CAM-1 — getUserMedia 소유자는 2파일뿐이다", () => {
  it("`getUserMedia(` 를 부르는 파일이 정확히 2개다", () => {
    const callers = ALL_SOURCES.filter((file) => /getUserMedia\s*\(/.test(code(file)))
      .map(rel)
      .sort();
    expect(callers).toEqual([CAMERA_PERMISSION, CAMERA_SERVICE]);
  });

  it("`cameraPermission.ts`에 `.stop()`이 있다 — LED 잔존 회귀 방지", () => {
    // 프라이밍 스트림을 멈추지 않으면 Guide 화면에 머무는 내내 카메라 LED가 켜져 있다.
    expect(code(join(SRC, CAMERA_PERMISSION))).toMatch(/\.stop\s*\(\s*\)/);
  });

  it("`cameraPermission.ts`가 카메라가 Idle일 때만 스트림을 연다", () => {
    // 하드웨어 단일 소유를 프라이밍이 우회하면 실촬영 스트림과 충돌한다.
    const source = code(join(SRC, CAMERA_PERMISSION));
    const guard = source.indexOf('state() !== "Idle"');
    const open = source.indexOf("getUserMedia(");
    expect(guard).toBeGreaterThanOrEqual(0);
    expect(guard).toBeLessThan(open);
  });

  it("진단 모달은 조회만 한다 — `requestCameraPermission`을 부르지 않는다", () => {
    // 모달을 여는 것만으로 권한 프롬프트가 뜨거나 LED가 켜지면 안 된다.
    const source = code(join(SRC, "screens/modals/diagnostics/DiagnosticsModal.tsx"));
    expect(source).toContain("readCameraPermission");
    expect(source).not.toContain("requestCameraPermission");
  });
});

// ─────────── CAM-7·8·9 — 진단 코드와 VideoFrame 경로 (2026-08-07 신설) ───────────

const VIDEO_FRAME_SOURCE = "adapters/camera/videoFrameSource.ts";
const CAMERA_CONSTRAINTS = "adapters/camera/cameraConstraints.ts";

describe("CAM-7 — 실패 기록은 cameraFailure()를 통해서만 만들어진다", () => {
  /**
   * 객체 리터럴로 우회하면(`lastFailure = { reason, detail: err.message }`) 예외 메시지가
   * 그대로 화면 오류 코드로 새어 나간다. 메시지에는 기기명·경로가 섞인다.
   *
   * ⚠️ 이 검사는 **생성 통로만** 고정한다. `cameraFailure()`를 올바로 부르면서 엉뚱한 값을
   *    넘기는 실수는 잡지 못한다 — 그 경우의 방어선은 `DETAIL_PATTERN`(런타임 새니타이즈)뿐이다.
   */
  it("`lastFailure` 대입 우변이 null 또는 승인된 생성 함수 호출뿐이다", () => {
    const source = code(join(SRC, CAMERA_SERVICE));
    // `lastFailure =` (단, `lastFailureXxx` 같은 다른 식별자는 제외)
    const assignments = [...source.matchAll(/\blastFailure\s*=\s*([^;]+);/g)].map((m) =>
      m[1]!.trim(),
    );
    expect(assignments.length).toBeGreaterThan(0);
    for (const rhs of assignments) {
      expect(
        rhs === "null" ||
          /^cameraFailure\(/.test(rhs) ||
          /^classifyCameraFailureFrom\(/.test(rhs),
        `lastFailure 대입 우변이 승인되지 않았다: ${rhs}`,
      ).toBe(true);
    }
  });

  it("`err.message`가 진단 코드로 흘러갈 통로가 없다 — failureCode는 포매터만 쓴다", () => {
    const source = code(join(SRC, CAMERA_SERVICE));
    for (const match of source.matchAll(/failureCode:\s*([^,\n]+)/g)) {
      expect(match[1]!.trim()).toMatch(/^formatCameraFailureCode\(/);
    }
  });

  it("도메인이 상세를 `name`에서만 만든다 — `err.message`를 읽지 않는다", () => {
    const source = code(join(SRC, "domain/capture/cameraFailure.ts"));
    expect(source).not.toMatch(/\.message\b/);
  });
});

describe("CAM-8 — 사다리 어느 칸도 빈 video 제약을 보내지 않는다", () => {
  /**
   * `TypeError → unsupportedBrowser` 매핑의 전제다. 빈 제약(`{audio:false}`)을 보내는 칸이
   * 생기면 규격상 `TypeError`가 나고, 그러면 멀쩡한 브라우저가 "미지원"으로 오진된다.
   */
  it("`constraintLadder`가 만드는 모든 칸의 `video`가 truthy다", () => {
    for (const request of [
      { deviceId: "cam", facing: "user" as const },
      { deviceId: null, facing: "environment" as const },
      {},
    ]) {
      for (const step of constraintLadder(request)) {
        expect(step.constraints.video, step.label).toBeTruthy();
      }
    }
  });

  it("소스에 `video`를 비우는 리터럴이 없다", () => {
    const source = code(join(SRC, CAMERA_CONSTRAINTS));
    expect(source).not.toMatch(/video\s*:\s*(false|undefined|null|\{\s*\})/);
  });

  it("`shouldTryNextStep`이 TypeError에서 멈춘다 — 같은 예외를 5번 반복하지 않는다", () => {
    expect(shouldTryNextStep("TypeError")).toBe(false);
    // 나머지 계약은 그대로다(점유·과제약은 계속 내려간다).
    expect(shouldTryNextStep("NotReadableError")).toBe(true);
  });
});

describe("CAM-9 — VideoFrame은 실증 프로브를 거쳐야 쓰인다", () => {
  /**
   * `typeof VideoFrame !== "undefined"` 존재 검사로 되돌아가면, 생성자만 있고 생성이 실패하는
   * 브라우저에서 매 프레임 throw하며 가공 프레임이 0장이 된다(F-5·F-6).
   */
  it("`new VideoFrame(` 앞에 프로브 게이트가 있다", () => {
    const source = code(join(SRC, VIDEO_FRAME_SOURCE));
    const gate = source.indexOf("videoFramePathUsable(");
    const construct = source.search(/new VideoFrame\(\s*video/);
    expect(gate).toBeGreaterThanOrEqual(0);
    expect(construct).toBeGreaterThanOrEqual(0);
    expect(gate).toBeLessThan(construct);
  });

  it("프로브가 1×1 캔버스로 실제 생성을 시도한다(존재 검사만이 아니다)", () => {
    const source = code(join(SRC, VIDEO_FRAME_SOURCE));
    expect(source).toMatch(/createElement\(\s*["']canvas["']\s*\)/);
    // ⚠️ `<video>`로 프로브하면 재생 시작 전이라 지원 브라우저에서도 던져 거짓 음성이 된다.
    expect(source).toMatch(/new VideoFrame\(\s*canvas/);
  });

  it("강등 상태가 **모듈 레벨**에 있고 단방향이다", () => {
    const source = code(join(SRC, VIDEO_FRAME_SOURCE));
    // 소스 인스턴스 안에 두면 카메라 재시작마다 강등이 초기화된다.
    const stateDecl = source.search(/^let videoFramePath\b/m);
    const factory = source.indexOf("export function createVideoFrameSource");
    expect(stateDecl).toBeGreaterThanOrEqual(0);
    expect(stateDecl).toBeLessThan(factory);
    expect(source).toContain("imageBitmapDemoted");
    // 강등에서 되돌아가는 대입이 없다.
    expect(source).not.toMatch(/videoFramePath\s*=\s*["']videoFrame["']/);
  });

  it("전송 실패 프레임을 닫는다 — VideoFrame은 GC 대상이 아니다", () => {
    expect(code(join(SRC, VIDEO_FRAME_SOURCE))).toMatch(/closeQuietly\(/);
  });
});
