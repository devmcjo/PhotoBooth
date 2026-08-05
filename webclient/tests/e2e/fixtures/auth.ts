import { expect, type Page } from "@playwright/test";
import { GOOGLE_AUTHORIZE_ENDPOINT, OAUTH_CALLBACK_PATH } from "@domain/auth/authorizeUrl";
import { OAUTH_PENDING_KEY } from "@adapters/auth/oauthStateStore";
import { STRINGS } from "@ui/strings";
import type { MockBackend } from "./backend";
import type { MockUser } from "./users";

/**
 * OAuth 로그인 하네스 — **백도어가 아니다**(설계 §5)
 *
 * `src/`에 테스트용 로그인 경로를 만들면 정적 검사 AUTH-1(`sessionStore.login(` 호출부 1곳)과
 * AUTH-4(`App.tsx`에 `devLogin` 0건)가 깨진다. 그래서 **브라우저 바깥에서만** 조작한다:
 *
 * ```
 * [Google로 로그인] 클릭
 *   → 앱이 PKCE·state·nonce를 만들어 sessionStorage에 저장하고 location.assign(authorizeUrl)
 *   → 하네스가 그 요청을 가로채 URL 쿼리에서 state·redirect_uri를 **읽고** abort
 *   → page.goto(`${redirect_uri}?code=…&state=…`)
 *   → main.tsx의 captureOauthCallback → oauthCallbackRunner가 **실제로** 실행된다
 * ```
 *
 * 하네스가 값을 지어내지 않는다 — `state`는 앱이 만든 것을 그대로 되돌려준다.
 * PKCE `code_verifier`는 알 필요가 없다(클라이언트가 검증하지 않고 서버=목이 본다).
 */

/** 인가 요청을 가로챌 URL 패턴. 실네트워크로 나가는 트래픽은 0이다. */
const AUTHORIZE_PATTERN = `${GOOGLE_AUTHORIZE_ENDPOINT}**`;

export interface FakeLoginOptions {
  /** 교환 응답에 실을 JWT. E3b가 계정마다 다른 값을 요구한다. */
  readonly token?: string;
  /** 로그인 진입 전 화면. 기본은 Home(상단바 계정 버튼에서 들어간다). */
  readonly from?: "topbar";
}

/** 상단바의 계정 버튼(라벨은 게스트면 "로그인", 로그인 상태면 계정 id다). */
export function accountButton(page: Page, label: string) {
  return page.getByRole("button", { name: `계정: ${label}`, exact: true });
}

/**
 * 실제 로그인 경로를 그대로 태운다. 반환값은 가로챈 authorize URL이다
 * (spec이 `prompt`·`scope` 같은 파라미터를 실행 경로에서 확인할 수 있게).
 */
export async function fakeLogin(
  page: Page,
  backend: MockBackend,
  user: MockUser,
  options: FakeLoginOptions = {},
): Promise<URL> {
  const token = options.token ?? `e2e-token-${user.id}`;
  backend.setUser(user, token);

  // ⚠️ 오리진을 **지금** 붙잡는다. `route.abort()` 뒤에는 탭이 오류 문서(origin `null`)에
  //    잠시 머물러 `page.url()`이 앱 오리진을 돌려주지 않는다.
  const origin = new URL(page.url()).origin;

  let authorizeUrl: URL | null = null;
  await page.route(AUTHORIZE_PATTERN, async (route) => {
    authorizeUrl = new URL(route.request().url());
    // 실제 이동을 막는다. 앱 페이지는 그대로 살아 있고 sessionStorage(pending)도 유지된다.
    await route.abort();
  });

  await accountButton(page, STRINGS.common.login).click();
  await expect(page.getByRole("heading", { name: STRINGS.login.title })).toBeVisible();
  await page.getByRole("button", { name: STRINGS.login.google, exact: true }).click();

  await expect
    .poll(() => authorizeUrl, { message: "authorize 요청이 가로채지지 않았다" })
    .not.toBeNull();
  await page.unroute(AUTHORIZE_PATTERN);

  // ⚠️ 여기서 `authorizeUrl`을 `URL`로 좁힌다 — 위 poll이 non-null을 보장하지만
  //    클로저 대입이라 TS가 추론하지 못한다.
  const captured = authorizeUrl as unknown as URL;
  const state = captured.searchParams.get("state");
  const redirectUri = captured.searchParams.get("redirect_uri");
  expect(state, "authorize URL에 state가 없다").not.toBeNull();
  expect(redirectUri).toBe(`${origin}${OAUTH_CALLBACK_PATH}`);
  // AUTH-5를 **실행 경로에서** 확인한다(정적 검사는 소스만 본다).
  expect(captured.searchParams.get("prompt")).toBe("select_account");

  await page.goto(
    `${redirectUri ?? ""}?code=E2E_CODE&state=${encodeURIComponent(state ?? "")}`,
  );

  // 상단바 계정 라벨이 계정 id로 바뀌면 세션이 섰다는 뜻이다.
  await expect(accountButton(page, user.id)).toBeVisible();
  return captured;
}

/** 계정 팝오버 → [로그아웃]. */
export async function logout(page: Page, user: MockUser): Promise<void> {
  await accountButton(page, user.id).click();
  await page.getByRole("menuitem", { name: STRINGS.common.logout, exact: true }).click();
  await expect(accountButton(page, STRINGS.common.login)).toBeVisible();
}

/** 콜백 소비 뒤 pending이 남지 않았는가(1회 소비 — `takePendingOauth`). */
export function pendingOauth(page: Page): Promise<string | null> {
  return page.evaluate(
    (key: string) => window.sessionStorage.getItem(key),
    OAUTH_PENDING_KEY,
  );
}
