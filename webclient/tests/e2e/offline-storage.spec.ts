import {
  FRAME_LOAD_DEGRADED_NOTICE,
  FRAME_LOAD_FAILED_NOTICE,
} from "@domain/frames/frameLoadPolicy";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { fakeLogin } from "./fixtures/auth";
import { clickResultNext, runCaptureToResult } from "./fixtures/capture";
import { listOpfs } from "./fixtures/opfs";
import { USERS } from "./fixtures/users";

/**
 * 오프라인·저장소 — E8 · E20
 *
 * 축은 **M6-W(로컬 보관이 업로드보다 먼저)** 다. 단위 테스트는 목 위의 순서를 고정하지만
 * 여기서는 **실브라우저의 실제 OPFS**를 prepare 시점에 열어 확인한다 — 그것이 E2E의 존재 이유다.
 *
 * ⚠️ **E6(저장 실패 표시 — M4)은 이 파일에 없다.** 설계 §7.3이 유일한 레버로 잡은
 *    CDP `Storage.overrideQuotaForOrigin(origin, 0)`을 실제로 걸어 본 결과
 *    `navigator.storage.estimate().quota`는 0이 되지만 **OPFS 쓰기(2 MiB)는 그대로 성공**했다
 *    (Chromium 131 실측 — 가정 A5가 거짓). 억지로 통과시키지 않고 **자동화에서 내린다.**
 *    저장 실패 경로는 `resultSaver` 단위 테스트가 판정을 고정하고, 실제 할당량 소진 관측은
 *    실측 **V19-6**이 소유한다(`docs/web-client/10 §5` E6 행 참조).
 */

test.describe("오프라인·저장소", { tag: "@camera" }, () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
  });

  test("E8 — prepare가 나가는 시점에 결과물이 이미 OPFS에 있다(M6-W)", async ({ page, app }) => {
    await app.seedSettings();
    await app.goto();
    await fakeLogin(page, app.backend, USERS.user, { token: "e2e-jwt-m6w" });

    // ★ 관측 지점: prepare **응답 직전**에 OPFS를 연다.
    let captured: string[] | null = null;
    app.backend.onBeforePrepare(async () => {
      if (captured === null) captured = await listOpfs(page, "results");
    });

    await runCaptureToResult(page);
    await clickResultNext(page);
    await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toBeVisible({
      timeout: 30_000,
    });

    // 클로저 안에서만 대입되므로 TS의 흐름 분석이 `null`로 좁힌다 — 관측값을 다시 넓힌다.
    const resultsAtPrepare = captured as string[] | null;
    expect(resultsAtPrepare, "prepare 훅이 실행되지 않았다").not.toBeNull();
    // 업로드가 시작되기 전에 이미 보관 폴더가 있다.
    expect(resultsAtPrepare ?? []).toHaveLength(1);

    // 폴더 안에 최종 이미지가 실제로 들어 있다.
    const folder = (resultsAtPrepare ?? [])[0]?.replace(/\/$/, "") ?? "";
    expect(await listOpfs(page, `results/${folder}`)).toContain("final.jpg");
  });

  test("E20 — 백엔드에 닿지 못해도 촬영이 완주하고 안내 문구가 뜨지 않는다", async ({
    page,
    app,
  }) => {
    await app.seedSettings();

    // ⚠️ `context.setOffline(true)`는 쓰지 않는다 — dev 서버에는 Service Worker가 없어서
    //    앱 문서 자체를 못 받아 온다(오프라인 앱 셸은 배포본 SW의 몫 = 실측 V25-1).
    //    여기서 재현하려는 것은 **백엔드 미도달**이고, 그것이 E20의 실제 축이다.
    app.backend.setNetworkDown(true);
    app.allowConsoleError(/ERR_INTERNET_DISCONNECTED|Failed to load resource|net::/);
    app.allowConsoleError(/백엔드 호출 실패/);
    await app.goto();

    // 게스트 촬영이 그대로 완주한다(코드 생성 fallback 프레임).
    await runCaptureToResult(page);

    // ★ 회귀 방지: 서버 조회 실패는 `Degraded`가 **아니다**
    //   (`loadCore`의 catch가 조회 실패를 삼켜 `Ready`로 유지한다 — `frameLoadPolicy` 주석).
    await expect(page.getByText(FRAME_LOAD_DEGRADED_NOTICE)).toHaveCount(0);
    await expect(page.getByText(FRAME_LOAD_FAILED_NOTICE)).toHaveCount(0);

    await clickResultNext(page);
    await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();

    // 보관은 성공했다 — 서버에 못 닿아도 로컬은 남는다.
    expect(await listOpfs(page, "results")).toHaveLength(1);

    app.backend.setNetworkDown(false);
  });
});
