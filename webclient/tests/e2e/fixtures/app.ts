import { expect, test as base } from "@playwright/test";
import type { AppSettingsValues } from "@domain/settings/appSettings";
import { SETTINGS_STORAGE_KEY, SETTINGS_SCHEMA_VERSION } from "@adapters/storage/settingsRepo";
import { mockBackend, type MockBackend } from "./backend";

/**
 * 공용 앱 픽스처 — 설정 시드 · 목 백엔드 · `goto` · **콘솔 오류 수집**
 *
 * 규칙(설계 §4.3): spec에는 **시나리오 서술과 단언만** 남기고 브라우저 조작은 여기 둔다.
 *
 * ⚠️ 콘솔 오류 수집은 CSP 검증이 아니라 **회귀 그물**이다(설계 §10). dev 서버에는 CSP가 없다 —
 *    "CSP 위반 0"은 배포본 실측 V13·V21-2가 소유한다.
 */

/**
 * E2E 기본 시드(설계 §8).
 *
 * · `CountdownSec: 3`  — 허용 최소값. 컷마다 [바로 촬영]을 눌러 실제로는 즉시 넘어간다.
 * · `SendTimelapse: false` — 업로드 파일 수를 1로 고정해 순서 단언을 결정적으로 만든다.
 *   타임랩스 `null`은 계약상 합법이다(VF-6). mp4 실검증은 실측 V18이 소유한다.
 * · `FlashMode`·`ShutterSound: false` — 오디오·타이밍 변수를 없앤다.
 */
export const BASE_SEED: Partial<AppSettingsValues> = {
  CutCount: 6,
  CountdownSec: 3,
  FlashMode: false,
  ShutterSound: false,
  SendPhoto: true,
  SendTimelapse: false,
  SaveLocalCopy: true,
  EnableQrDelivery: true,
  RetakeEnabled: false,
};

/**
 * 브라우저가 **정리 시점에** 스스로 찍는 권고 문구. 앱의 오류가 아니라서 그물에서 뺀다.
 *
 * · VideoFrame — `camera.stop()`이 가공 Worker를 `terminate()`하면 그 Worker가 소유하던
 *   프레임(큐 1장 + 가공 중 1장)이 `close()` 없이 사라진다. Worker가 죽으면서 자원도 함께
 *   회수되므로 누수는 아니지만, Chromium의 GC 파이널라이저가 일반 권고를 남긴다.
 *   발생 여부가 **GC 타이밍에 좌우돼 비결정적**이라 개별 spec에서 허용하면 그물이 흔들린다.
 *   ⚠️ 이 배열을 늘릴 때는 "앱이 만든 오류가 아님"을 근거와 함께 적는다.
 */
const BROWSER_TEARDOWN_ADVISORIES: readonly RegExp[] = [
  /A VideoFrame was garbage collected without being closed/,
];

export interface AppFixture {
  readonly backend: MockBackend;
  /**
   * `localStorage["mcphoto.settings.v1"]`를 심는다. **`goto` 전에** 불러야 한다.
   * 여러 번 부르면 마지막 호출이 이긴다(같은 키를 덮어쓴다).
   */
  seedSettings(values?: Partial<AppSettingsValues>): Promise<void>;
  goto(path?: string): Promise<void>;
  /** 이 spec에서 예상되는 콘솔 오류(목 실패 응답 등)를 통과시킨다. */
  allowConsoleError(pattern: RegExp): void;
  /** 수집된 원문(필터 전). 디버깅·단언용. */
  readonly consoleErrors: readonly string[];
}

export const test = base.extend<{ app: AppFixture }>({
  app: async ({ page }, use) => {
    const consoleErrors: string[] = [];
    const allowed: RegExp[] = [];

    page.on("console", (msg) => {
      if (msg.type() !== "error") return;
      // 브라우저가 문서마다 암묵적으로 요청하는 `/favicon.ico`다. 저장소에 파일이 없고
      // `index.html`에 `<link rel="icon">`도 없어 dev·배포본 모두 404가 난다.
      // 브라우저 내부 요청이라 `page.route`로 가로챌 수 없으므로 **발신 위치로** 걸러낸다
      // (문구 패턴으로 거르면 진짜 404까지 함께 숨는다).
      if (msg.location().url.endsWith("/favicon.ico")) return;
      if (BROWSER_TEARDOWN_ADVISORIES.some((pattern) => pattern.test(msg.text()))) return;
      consoleErrors.push(msg.text());
    });
    page.on("pageerror", (err) => consoleErrors.push(`pageerror: ${err.message}`));

    const backend = await mockBackend(page);

    const app: AppFixture = {
      backend,
      consoleErrors,
      allowConsoleError(pattern) {
        allowed.push(pattern);
      },
      async seedSettings(values = {}) {
        const payload = JSON.stringify({
          schemaVersion: SETTINGS_SCHEMA_VERSION,
          // 저장된 키만 채택되고 나머지는 기본값이다(`settingsRepo.mergeValues`).
          values: { ...BASE_SEED, ...values },
          webExtras: {},
        });
        await page.addInitScript(
          ([key, value]) => {
            // ⚠️ init 스크립트는 **모든 문서**에서 돈다 — OAuth 하네스가 authorize 이동을
            //    abort하면 오리진 `null`인 오류 문서가 잠깐 생기고, 그곳의 `localStorage`
            //    접근은 SecurityError다. 시드 실패가 미처리 예외로 새면 안 된다.
            try {
              window.localStorage.setItem(key, value);
            } catch {
              // 앱 오리진이 아닌 문서다 — 심을 대상이 아니다.
            }
          },
          [SETTINGS_STORAGE_KEY, payload] as const,
        );
      },
      async goto(path = "/") {
        await page.goto(path);
      },
    };

    await use(app);

    const unexpected = consoleErrors.filter(
      (text) => !allowed.some((pattern) => pattern.test(text)),
    );
    expect(unexpected, "예상하지 못한 콘솔 오류가 있다").toEqual([]);
  },
});

export { expect } from "@playwright/test";
