import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * 카메라 정적 불변식 **CAM-1** — 15 §3.4 관례
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
