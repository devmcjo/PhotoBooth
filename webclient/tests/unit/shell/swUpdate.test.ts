import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  APPLY_UPDATE_MESSAGE,
  applyWaitingUpdate,
  checkForUpdate,
  installServiceWorker,
  resetSwUpdateForTests,
  swStateStore,
  type ServiceWorkerContainerLike,
  type ServiceWorkerLike,
  type ServiceWorkerRegistrationLike,
} from "@shell/swUpdate";

/**
 * Service Worker 등록·갱신 — 01 §6 (설계 §8.5)
 *
 * ⚠️ 가장 중요한 고정: **`skipWaiting`은 사용자 트리거 1경로뿐**이고, `controllerchange`가
 *    여러 번 와도 **리로드는 1회**다(없으면 리로드 루프가 난다).
 * ⚠️ 촬영 중에는 적용하지 않는다.
 */

interface Fake {
  readonly container: ServiceWorkerContainerLike;
  readonly registration: ServiceWorkerRegistrationLike & {
    waiting: ServiceWorkerLike | null;
    active: ServiceWorkerLike | null;
    installing: ServiceWorkerLike | null;
  };
  readonly posted: unknown[];
  readonly fireControllerChange: () => void;
  readonly fireUpdateFound: () => void;
  registerCalls: number;
  updateCalls: number;
}

function fakeWorker(posted: unknown[]): ServiceWorkerLike {
  return {
    state: "installed",
    postMessage: (message) => posted.push(message),
    addEventListener: () => undefined,
  };
}

function makeFake(options: { registerFails?: boolean } = {}): Fake {
  const posted: unknown[] = [];
  const controllerListeners: (() => void)[] = [];
  const updateListeners: (() => void)[] = [];

  const registration = {
    installing: null as ServiceWorkerLike | null,
    waiting: null as ServiceWorkerLike | null,
    active: fakeWorker(posted),
    addEventListener: (_type: "updatefound", listener: () => void) => {
      updateListeners.push(listener);
    },
    update: async () => {
      fake.updateCalls++;
    },
  };

  const container: ServiceWorkerContainerLike = {
    controller: null,
    register: async () => {
      fake.registerCalls++;
      if (options.registerFails === true) throw new Error("등록 실패");
      return registration;
    },
    addEventListener: (_type: "controllerchange", listener: () => void) => {
      controllerListeners.push(listener);
    },
  };

  const fake: Fake = {
    container,
    registration,
    posted,
    fireControllerChange: () => {
      for (const listener of [...controllerListeners]) listener();
    },
    fireUpdateFound: () => {
      for (const listener of [...updateListeners]) listener();
    },
    registerCalls: 0,
    updateCalls: 0,
  };

  return fake;
}

beforeEach(() => {
  resetSwUpdateForTests();
});

afterEach(() => {
  resetSwUpdateForTests();
});

describe("installServiceWorker", () => {
  it("**dev에서는 등록하지 않는다**(status = disabled)", () => {
    const fake = makeFake();
    installServiceWorker({ container: fake.container, enabled: false });
    expect(fake.registerCalls).toBe(0);
    expect(swStateStore.getState().status).toBe("disabled");
  });

  it("지원하지 않는 브라우저는 unsupported다", () => {
    installServiceWorker({ container: null, enabled: true });
    expect(swStateStore.getState().status).toBe("unsupported");
  });

  it("등록 성공 시 active다", async () => {
    const fake = makeFake();
    installServiceWorker({
      container: fake.container,
      enabled: true,
      readBuildId: async () => "abc12345",
    });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(fake.registerCalls).toBe(1);
    expect(swStateStore.getState().status).toBe("active");
    await Promise.resolve();
    expect(swStateStore.getState().buildId).toBe("abc12345");
  });

  it("등록 실패는 failed다(앱이 죽지 않는다)", async () => {
    const fake = makeFake({ registerFails: true });
    installServiceWorker({ container: fake.container, enabled: true });
    await Promise.resolve();
    await Promise.resolve();
    expect(swStateStore.getState().status).toBe("failed");
  });

  it("**멱등**이다 — 두 번 불러도 등록은 1회", async () => {
    const fake = makeFake();
    installServiceWorker({ container: fake.container, enabled: true, readBuildId: async () => null });
    installServiceWorker({ container: fake.container, enabled: true, readBuildId: async () => null });
    await Promise.resolve();
    await Promise.resolve();
    expect(fake.registerCalls).toBe(1);
  });

  it("대기 중 워커가 생기면 waiting을 감지한다", async () => {
    const fake = makeFake();
    installServiceWorker({ container: fake.container, enabled: true, readBuildId: async () => null });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    fake.registration.waiting = fakeWorker(fake.posted);
    fake.fireUpdateFound();
    expect(swStateStore.getState().status).toBe("waiting");
  });
});

describe("applyWaitingUpdate", () => {
  async function installed(isBusy: () => boolean = () => false): Promise<Fake> {
    const fake = makeFake();
    installServiceWorker({
      container: fake.container,
      enabled: true,
      isBusy,
      reload: () => reloads.push(1),
      readBuildId: async () => null,
    });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    return fake;
  }

  let reloads: number[] = [];
  beforeEach(() => {
    reloads = [];
  });

  it("대기 중 워커가 없으면 `false`다", async () => {
    const fake = await installed();
    expect(await applyWaitingUpdate()).toBe(false);
    expect(fake.posted).toHaveLength(0);
  });

  it("[지금 적용]은 `postMessage`를 **1회** 보낸다", async () => {
    const fake = await installed();
    fake.registration.waiting = fakeWorker(fake.posted);
    expect(await applyWaitingUpdate()).toBe(true);
    expect(fake.posted).toEqual([{ type: APPLY_UPDATE_MESSAGE }]);
  });

  it("**촬영 중에는 차단**되고 메시지를 보내지 않는다", async () => {
    const fake = await installed(() => true);
    fake.registration.waiting = fakeWorker(fake.posted);
    expect(await applyWaitingUpdate()).toBe(false);
    expect(fake.posted).toHaveLength(0);
  });

  it("`controllerchange`가 2회여도 **reload는 1회**다", async () => {
    const fake = await installed();
    fake.registration.waiting = fakeWorker(fake.posted);
    await applyWaitingUpdate();

    fake.fireControllerChange();
    fake.fireControllerChange();
    expect(reloads).toHaveLength(1);
  });

  it("[지금 적용]을 누르지 않았으면 `controllerchange`가 와도 리로드하지 않는다", async () => {
    const fake = await installed();
    // 첫 설치의 `clients.claim()`이 이 이벤트를 발생시킨다 — 여기서 리로드하면 첫 방문이 깜빡인다.
    fake.fireControllerChange();
    expect(reloads).toHaveLength(0);
  });
});

describe("checkForUpdate", () => {
  it("등록 전에는 `false`다", async () => {
    expect(await checkForUpdate()).toBe(false);
  });

  it("갱신이 없으면 `false`다", async () => {
    const fake = makeFake();
    installServiceWorker({ container: fake.container, enabled: true, readBuildId: async () => null });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    expect(await checkForUpdate()).toBe(false);
    expect(fake.updateCalls).toBe(1);
  });

  it("대기 중 워커가 생기면 `true`다", async () => {
    const fake = makeFake();
    installServiceWorker({ container: fake.container, enabled: true, readBuildId: async () => null });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    fake.registration.waiting = fakeWorker(fake.posted);
    expect(await checkForUpdate()).toBe(true);
    expect(swStateStore.getState().status).toBe("waiting");
  });
});
