import { afterEach, describe, expect, it, vi } from "vitest";
import {
  readCameraPermission,
  requestCameraPermission,
  watchCameraPermission,
} from "@adapters/camera/cameraPermission";
import { setCameraServiceForTests, type CameraService } from "@adapters/camera/cameraService";
import type { CameraState } from "@adapters/camera/cameraTypes";

/**
 * 카메라 권한 어댑터 — 07 §3 · 12 C5
 *
 * 못박는 것 3가지:
 *   ① **조회는 프롬프트를 띄우지 않는다**(미지원·throw → `null` 폴백).
 *   ② **프라이밍 스트림은 즉시 정지한다** — 빠뜨리면 카메라 LED가 켜진 채 남는다.
 *   ③ **카메라가 Idle이 아니면 스트림을 열지 않는다** — 하드웨어 단일 소유(01 §2.1).
 */

interface PermissionStatusStub {
  state: string;
  addEventListener?: (type: string, listener: () => void) => void;
  removeEventListener?: (type: string, listener: () => void) => void;
}

const originalNavigator = globalThis.navigator;

function setNavigator(value: unknown): void {
  Object.defineProperty(globalThis, "navigator", {
    value,
    configurable: true,
    writable: true,
  });
}

afterEach(() => {
  setNavigator(originalNavigator);
  setCameraServiceForTests(null);
});

/** `state()`만 쓰는 최소 스텁. 나머지 표면은 호출되면 테스트가 깨지도록 둔다. */
function stubCamera(state: CameraState): void {
  setCameraServiceForTests({ state: () => state } as unknown as CameraService);
}

// ───────────────────────────── readCameraPermission ─────────────────────────────

describe("readCameraPermission — 조회만 한다", () => {
  it("granted/denied/prompt를 그대로 돌려준다", async () => {
    for (const state of ["granted", "denied", "prompt"] as const) {
      setNavigator({ permissions: { query: async () => ({ state }) } });
      await expect(readCameraPermission()).resolves.toBe(state);
    }
  });

  it("permissions API가 없으면 null이다(Safari)", async () => {
    setNavigator({});
    await expect(readCameraPermission()).resolves.toBeNull();
  });

  it("query가 throw하면 null이다(Firefox는 name:'camera'를 모른다)", async () => {
    setNavigator({
      permissions: {
        query: () => Promise.reject(new TypeError("unsupported permission name")),
      },
    });
    await expect(readCameraPermission()).resolves.toBeNull();
  });

  it("알 수 없는 state 문자열도 null로 좁힌다", async () => {
    setNavigator({ permissions: { query: async () => ({ state: "unknown-state" }) } });
    await expect(readCameraPermission()).resolves.toBeNull();
  });
});

// ───────────────────────────── watchCameraPermission ─────────────────────────────

describe("watchCameraPermission — 해제 함수를 반드시 돌려준다", () => {
  it("미지원이면 no-op 해제자다(호출측이 분기하지 않게)", () => {
    setNavigator({});
    const off = watchCameraPermission(() => undefined);
    expect(typeof off).toBe("function");
    expect(() => off()).not.toThrow();
  });

  it("change 이벤트를 전달하고 해제하면 리스너를 뗀다(누수 금지)", async () => {
    const box: { fn: (() => void) | null } = { fn: null };
    const status: PermissionStatusStub = {
      state: "prompt",
      addEventListener: (_type, listener) => {
        box.fn = listener;
      },
      removeEventListener: () => {
        box.fn = null;
      },
    };
    setNavigator({ permissions: { query: async () => status } });

    const seen: (string | null)[] = [];
    const off = watchCameraPermission((next) => seen.push(next));
    await vi.waitFor(() => expect(box.fn).not.toBeNull());

    status.state = "granted";
    box.fn?.();
    expect(seen).toEqual(["granted"]);

    off();
    expect(box.fn).toBeNull();
  });

  it("구독이 붙기 전에 해제해도 리스너가 남지 않는다(비동기 경합)", async () => {
    let attached = 0;
    const status: PermissionStatusStub = {
      state: "prompt",
      addEventListener: () => {
        attached += 1;
      },
      removeEventListener: () => {
        attached -= 1;
      },
    };
    setNavigator({ permissions: { query: async () => status } });

    const off = watchCameraPermission(() => undefined);
    off(); // query가 resolve하기 전에 해제
    await Promise.resolve();
    await Promise.resolve();
    expect(attached).toBe(0);
  });
});

// ──────────────────────────── requestCameraPermission ────────────────────────────

describe("requestCameraPermission — 프라이밍은 즉시 정지한다", () => {
  it("획득 즉시 모든 트랙을 stop한다 — LED 잔존 금지", async () => {
    stubCamera("Idle");
    const stopped: string[] = [];
    const stream = {
      getTracks: () => [
        { stop: () => stopped.push("a") },
        { stop: () => stopped.push("b") },
      ],
    };
    let constraints: MediaStreamConstraints | null = null;
    setNavigator({
      mediaDevices: {
        getUserMedia: (c: MediaStreamConstraints) => {
          constraints = c;
          return Promise.resolve(stream);
        },
      },
    });

    await expect(requestCameraPermission()).resolves.toEqual({ ok: true });
    expect(stopped).toEqual(["a", "b"]);
    // 해상도 제약을 걸면 프라이밍에서 OverconstrainedError가 날 수 있다 → video:true 고정.
    expect(constraints).toEqual({ audio: false, video: true });
  });

  it("카메라가 Idle이 아니면 스트림을 열지 않는다(하드웨어 단일 소유)", async () => {
    stubCamera("Ready");
    let called = 0;
    setNavigator({
      mediaDevices: {
        getUserMedia: () => {
          called += 1;
          return Promise.resolve({ getTracks: () => [] });
        },
      },
    });

    await expect(requestCameraPermission()).resolves.toEqual({ ok: true });
    expect(called).toBe(0);
  });

  it("거부는 throw하지 않고 사유를 돌려준다", async () => {
    stubCamera("Idle");
    const err = new Error("denied");
    err.name = "NotAllowedError";
    setNavigator({ mediaDevices: { getUserMedia: () => Promise.reject(err) } });

    await expect(requestCameraPermission()).resolves.toEqual({
      ok: false,
      reason: "permissionDenied",
    });
  });

  it("mediaDevices 자체가 없으면(구형·http) unknown 사유로 실패한다 — 예외 전파 금지", async () => {
    stubCamera("Idle");
    setNavigator({});
    const outcome = await requestCameraPermission();
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) expect(outcome.reason).toBe("unknown");
  });
});
