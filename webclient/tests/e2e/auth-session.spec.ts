import type { Page } from "@playwright/test";
import { LOG_DB_NAME, LOG_STORE_NAME } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { accountButton, fakeLogin, logout, pendingOauth } from "./fixtures/auth";
import { clickResultNext, runCaptureToResult } from "./fixtures/capture";
import { USERS } from "./fixtures/users";

/**
 * 인증·세션 — E3-1 · E3-2 · E3b · E4
 *
 * `10 §5`의 E3("게스트 익명 업로드에 토큰이 붙지 않는다")은 **웹에서 성립하지 않는다**:
 * 게스트는 `Qr`에 도달할 수 없고(VF-11), 인증 호출은 토큰이 없으면 요청 자체가 나가지 않는다.
 * 그래서 설계 §7.1대로 **관측 가능한 3개로 분해**했다.
 */

const TOKEN_A = "e2e-jwt-AAAA-1111";
const TOKEN_B = "e2e-jwt-BBBB-2222";

/** `Qr` 진입 → 업로드 완료(QR 렌더)까지. */
async function waitForQrRendered(page: Page): Promise<void> {
  await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toBeVisible({
    timeout: 30_000,
  });
}

/** `Qr` → [완료] → `Done` → [처음으로] → `Home`. */
async function backHomeFromQr(page: Page): Promise<void> {
  await page.getByRole("button", { name: STRINGS.common.done, exact: true }).click();
  await page.getByRole("button", { name: STRINGS.done.goHome, exact: true }).click();
  await expect(page.getByRole("button", { name: STRINGS.home.start, exact: true })).toBeVisible();
}

/** 브라우저 저장소 전량 덤프(문자열). E4가 여기서 토큰을 찾는다. */
function dumpBrowserStorage(page: Page): Promise<{ supported: boolean; text: string }> {
  return page.evaluate(async () => {
    const parts: string[] = [];

    for (let i = 0; i < window.localStorage.length; i++) {
      const key = window.localStorage.key(i);
      if (key === null) continue;
      parts.push(`local:${key}=${window.localStorage.getItem(key) ?? ""}`);
    }
    for (let i = 0; i < window.sessionStorage.length; i++) {
      const key = window.sessionStorage.key(i);
      if (key === null) continue;
      parts.push(`session:${key}=${window.sessionStorage.getItem(key) ?? ""}`);
    }

    const factory = window.indexedDB as IDBFactory & {
      databases?: () => Promise<{ name?: string }[]>;
    };
    if (typeof factory.databases !== "function") {
      return { supported: false, text: parts.join("\n") };
    }

    for (const info of await factory.databases()) {
      const name = info.name;
      if (name === undefined) continue;
      const db = await new Promise<IDBDatabase | null>((resolve) => {
        const request = window.indexedDB.open(name);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => resolve(null);
        request.onblocked = () => resolve(null);
      });
      if (db === null) continue;
      for (const storeName of Array.from(db.objectStoreNames)) {
        // 레코드가 0건이어도 **열거했다는 사실**은 남긴다(단언이 헛돌지 않게).
        parts.push(`idb-store:${name}/${storeName}`);
        const records = await new Promise<unknown[]>((resolve) => {
          try {
            const request = db.transaction(storeName, "readonly").objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result as unknown[]);
            request.onerror = () => resolve([]);
          } catch {
            resolve([]);
          }
        });
        for (const record of records) {
          // 핸들처럼 직렬화할 수 없는 값이 섞여 있어도 조회를 멈추지 않는다.
          try {
            parts.push(`idb:${name}/${storeName}=${JSON.stringify(record)}`);
          } catch {
            parts.push(`idb:${name}/${storeName}=<unserializable>`);
          }
        }
      }
      db.close();
    }

    return { supported: true, text: parts.join("\n") };
  });
}

test.describe("인증·세션", () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
    await app.seedSettings();
    await app.goto();
  });

  test(
    "E3-1 · E3-2 — 로그인 업로드에는 Bearer가 붙고, 로그아웃하면 업로드 요청 자체가 0건이다",
    { tag: "@camera" },
    async ({ page, app }) => {
      await fakeLogin(page, app.backend, USERS.user, { token: TOKEN_A });
      // 콜백은 1회 소비다 — pending이 남으면 같은 code로 재진입할 수 있다.
      expect(await pendingOauth(page)).toBeNull();
      // 주소창에 `code`·`state`가 남지 않는다(`scrubUrl`).
      expect(new URL(page.url()).search).toBe("");

      await runCaptureToResult(page);
      await clickResultNext(page);
      await waitForQrRendered(page);

      // ★ E3-1 — prepare에 Bearer A가 붙었다.
      // ⚠️ dev 서버에서는 `<StrictMode>`가 effect를 2회 실행해 **prepare만 2건**이 된다
      //    (`useUploadRun`이 첫 실행을 cleanup에서 abort한다 — 설계된 동작이고 운영 빌드에는
      //    없다). PUT·commit은 1건씩이다 — 그 개수는 E2가 고정한다.
      const prepare = app.backend.callsTo("uploads/prepare");
      expect(prepare.length).toBeGreaterThan(0);
      for (const call of prepare) {
        expect(call.headers.authorization).toBe(`Bearer ${TOKEN_A}`);
      }
      expect(app.backend.callsTo("uploads/commit")).toHaveLength(1);

      await backHomeFromQr(page);
      await logout(page, USERS.user);
      app.backend.clearCalls();

      // ★ E3-2 — 게스트로 같은 흐름을 반복하면 업로드 요청이 아예 없다.
      await runCaptureToResult(page);
      await clickResultNext(page);
      await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
      expect(app.backend.callsTo("uploads/")).toEqual([]);
      expect(app.backend.callsTo("__mock-storage/")).toEqual([]);
    },
  );

  test(
    "E3b — 재로그인하면 prepare의 Bearer가 새 토큰으로 교체된다",
    { tag: "@camera" },
    async ({ page, app }) => {
      await fakeLogin(page, app.backend, USERS.user, { token: TOKEN_A });
      await runCaptureToResult(page);
      await clickResultNext(page);
      await waitForQrRendered(page);
      for (const call of app.backend.callsTo("uploads/prepare")) {
        expect(call.headers.authorization).toBe(`Bearer ${TOKEN_A}`);
      }

      await backHomeFromQr(page);
      await logout(page, USERS.user);
      app.backend.clearCalls();

      await fakeLogin(page, app.backend, USERS.manager, { token: TOKEN_B });
      await runCaptureToResult(page);
      await clickResultNext(page);
      await waitForQrRendered(page);

      const prepare = app.backend.callsTo("uploads/prepare");
      expect(prepare.length).toBeGreaterThan(0);
      // A의 잔존이 아니라 B다 — 한 건도 예외가 없어야 한다.
      for (const call of prepare) {
        expect(call.headers.authorization).toBe(`Bearer ${TOKEN_B}`);
      }
    },
  );

  test("E4 — JWT가 어떤 브라우저 저장소에도 남지 않는다(M2)", async ({ page, app }) => {
    await fakeLogin(page, app.backend, USERS.user, { token: TOKEN_A });
    // 로그인 상태에서 저장소를 훑는다(로그아웃 뒤에 보면 아무 의미가 없다).
    await expect(accountButton(page, USERS.user.id)).toBeVisible();

    const cookies = await page.context().cookies();
    expect(JSON.stringify(cookies)).not.toContain(TOKEN_A);

    const dump = await dumpBrowserStorage(page);
    expect(dump.supported, "indexedDB.databases()로 전 DB를 열거하지 못했다").toBe(true);
    // 열거가 헛돌지 않았다는 증거 — 로그 스토어 DB가 실제로 잡힌다.
    expect(dump.text).toContain(`idb-store:${LOG_DB_NAME}/${LOG_STORE_NAME}`);
    // 설정은 localStorage에 있으므로 그쪽 열거도 살아 있음을 함께 본다.
    expect(dump.text).toContain("local:");
    expect(dump.text).not.toContain(TOKEN_A);
  });
});
