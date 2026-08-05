# Step 12 · Google SSO 리디렉트 로그인 + JWT 구현 설계

| 항목 | 값 |
|------|-----|
| 대상 | WBS **Step 12** — [11 §Step 12](../web-client/11-wbs.md) |
| 규격 | [07 §2~§5](../web-client/07-auth-and-permissions-web.md) · [03 §3](../web-client/03-screens-spec.md) · [06 §1.1·§3](../web-client/06-backend-integration-web.md) · [02 §5](../web-client/02-app-shell-and-navigation.md) · [12 B11·C6·C10](../web-client/12-web-vs-windows-differences.md) · [analysis/61 §3.4·§5·§6](../analysis/61-auth-platform-integration.md) · [analysis/31 §4.2](../analysis/31-backend-api-reference.md) · [analysis/13 §14](../analysis/13-client-behavior-spec.md) |
| 관례 | [15 · 구현 관례](../web-client/15-implementation-conventions.md) — 계층·테스트 전략(§3.1·§3.2)·정적 불변식(§3.4)·함정 12건(§4) |
| 작성 | js-architect (설계만. 구현은 js-developer, 검증은 js-code-reviewer) |
| 작성일 | 2026-07-31 |
| 전제 | Step 0~11 완료(★마일스톤 A) · 웹 테스트 **873**(34파일) 녹색 · 사용자 액션 **A1~A5 완료**([14 §1](../web-client/14-handoff-and-user-actions.md)) · 서버 **B1·B2·B4 배포 완료**(`7808e83`) · 브랜치 `feature/web-client-foundation` |

> **이 Step의 한 줄 요약**: `Login`에서 **전체 페이지 리디렉트**로 Google 인가를 받고,
> `/oauth2callback`에서 **state 대조 → `POST /auth/google`(`clientKind:"web"`) → 메모리 토큰 + 세션 사용자**를 세우고
> **URL의 code·state를 즉시 지운다.** 새로고침하면 로그아웃되는 것이 규격이다(C6).
> 여기에 **401 → 세션 해제**(C10)를 한 곳에 배선한다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (코드·문서를 직접 읽어 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | 서버 `POST /auth/google`은 **API 키 게이트만** 요구한다(Bearer 불요). `clientKind` 미지정 = `desktop`, `desktop\|web` 밖 문자열은 400. 요청한 종류가 미구성이면 **501**(401로 감추지 않는다) | `web/functions/src/routes/auth.ts:34-65` |
| F2 | `validateRedirectUri`가 **허용 목록을 먼저** 보고, loopback 형태일 때만 loopback 규칙에 위임한다(함정 #7의 수정본). 완전 일치만 통과 | `web/functions/src/domain/validation.ts:161-191` |
| F3 | audience는 **목록**이다(`acceptableAudiences`). `nonce`는 요청에 있을 때만 대조하고, `email_verified !== true`는 거부한다 | `web/functions/src/services/googleAuth.ts:64-136` |
| F4 | 서버 `codeVerifier` 검증은 `^[A-Za-z0-9\-._~]{43,128}$`, `nonce`는 `^[A-Za-z0-9\-._~]{1,256}$`, `code`는 트림 후 1~2048자 | `validation.ts:78-86,197-205,69-75` |
| F5 | `optionalBearer`는 **무토큰이면 통과, 무효 토큰이면 401**이다 → 업로드 중 토큰이 만료되면 `/uploads/*`가 401을 낸다 | `web/functions/src/http/auth.ts:94-113` |
| F6 | 응답 200 형태는 `{token, expiresIn, user{id,role,createdAt,email,authMethod,hasPin}}` | `auth.ts:119-123` · `analysis/31 §4.2` |
| F7 | `authStore`는 메모리 전용이고 `installTokenLifecycle()`이 **`main.tsx`에서 이미 호출**된다. `clearToken`은 구독과 401 처리만 부른다 | `src/shell/authStore.ts:16-72` · `src/main.tsx:37` |
| F8 | **401 → 세션 해제 배선이 없다.** `backendClient`는 401을 `toBackendError`로 던질 뿐 세션을 건드리지 않는다 | `src/adapters/http/backendClient.ts:127-140` |
| F9 | `accountService.ts`에 **`/auth/google` 메서드가 없다**(`list`/`verifyMyPin`/`setMyPin`/`deleteAccount`/`setRole`/`resetOtherPin` 6개뿐) | `src/adapters/http/accountService.ts:16-84` |
| F10 | `errors.ts`에 `SsoNotConfiguredError`(501 자동 매핑)·`NetworkError.timedOut`·`isUnauthorized`가 이미 있다 | `src/adapters/http/errors.ts:65-71,116-142` |
| F11 | 백엔드 오류 코드는 **`errorCode` 키**로 로깅하는 관례가 이미 있다(`code`는 마스킹 대상) | `backendClient.ts:134-136` · 15 §4 함정 #1 |
| F12 | 로그 금지 키 목록에 `code`·`codeverifier`·`state`·`nonce`·`token`·`pin`이 **정규화 후 정확 일치**로 들어 있다 | `src/adapters/storage/logPolicy.ts:31-66` |
| F13 | `sessionStore.logout()`은 `currentUser=null` **+ `discardCaptureData()`** 를 함께 한다. 사용자 변경 진입점은 `login`/`logout` 2개뿐이라는 주석이 있다 | `src/shell/sessionStore.ts:89-96` |
| F14 | [02 §5.2] 매트릭스: **JWT 만료 감지(401) → 사용자 해제 + 토큰 폐기, 촬영 데이터는 유지**(화면 유지, 게스트로 계속) | `docs/web-client/02-app-shell-and-navigation.md` §5.2 표 |
| F15 | `classifyRoute(pathname)`가 이미 있고 `/oauth2callback` → `"oauthCallback"`을 돌려준다. **그러나 아무도 호출하지 않는다** | `src/shell/router.ts:17-19` (전 저장소 grep 0건) |
| F16 | `installRouter()`는 설치 즉시 `history.pushState({mcphoto:true},"")`로 **현재 URL 위에 더미 엔트리**를 쌓는다 | `router.ts:53` |
| F17 | `env.googleClientId`가 이미 있고 빈 값이면 경고만 남긴다(크래시 없음). **소비 지점이 아직 없다** | `src/env.ts:75-79` (grep: 소비처 0건) |
| F18 | `App.tsx`의 `TopBar.onAccount`가 이미 `go(user===null ? "Login" : "Account")`로 배선돼 있다. `ScreenRouter`의 `Login`은 `DummyScreen`이다 | `src/App.tsx:202-204,235-237` |
| F19 | `devLogin(id)`가 `App.tsx` 끝에 있고 **아무도 import하지 않는다**(grep 1건 = 정의 자신) | `src/App.tsx:277-287` |
| F20 | `APP_STATES`에 `Login`·`Account`가 있고 **`OauthCallback` 상태는 없다**. `canTransition("Home", x)`가 허용하는 x는 `Home`·`Settings`·`Login`·`Account`·`FrameSelect` **5개뿐** | `src/domain/navigation/appState.ts` · `stateMachine.ts:11-38` |
| F21 | `shellStore.go`는 오버레이 진입 시 `overlayReturnTo`를 **메모리에** 저장한다 → 리디렉트로 페이지가 떠나면 **소실된다** | `src/shell/shellStore.ts:109-118` |
| F22 | `web/firebase.json`의 kiosk 타깃에 **SPA rewrite(`**→/index.html`)와 `/oauth2callback` no-cache 헤더가 이미 있다.** `hosting:default`(P1)는 별 타깃이라 무관 | `web/firebase.json:35,57-60` |
| F23 | kiosk CSP: `default-src 'self'` · `connect-src`에 cloudfunctions·storage·firebasestorage · **`form-action 'none'`** · `frame-ancestors 'none'` | `web/firebase.json:42` |
| F24 | 도메인 순수성 테스트가 금지하는 것: `Date.now`·`new Date()`·`Math.random`·**`crypto.`**·`fetch(`·`localStorage`·**`sessionStorage`**·`indexedDB`·`window`·`document.`·`navigator`·`performance.`·`console.`·`process.`. **`URL`·`URLSearchParams`·`TextEncoder`는 금지 목록에 없다** | `tests/unit/domain/purity.test.ts:48-63` |
| F25 | `domain/index.ts`는 평면 `export *` 배럴인데 **`accounts/sessionUser`는 등재돼 있지 않다** → 배럴 미등재 도메인 모듈이 이미 선례로 존재한다 | `src/domain/index.ts` |
| F26 | `STRINGS.error.sessionExpired`는 **"로그인이 만료되었습니다…"** 인데 [07 §4.3]·[12 C10]은 **"세션이 만료되었습니다…"** 다. 그리고 **아무도 이 문구를 쓰지 않는다**(grep 1건 = 정의) | `src/ui/strings.ts:56` |
| F27 | `shell/globalErrorHandler.ts`가 `@ui/strings`를 import한다 → **shell → ui/strings 참조는 기존 선례**다 | `src/shell/globalErrorHandler.ts:2` |
| F28 | 화면 컴포넌트는 `src/ui/views/*`에, 화면 로직은 `src/screens/*`에 두는 것이 Step 8~11의 실제 배치다(`QrView.tsx` ↔ `screens/qr/useUploadRun.ts`) | `src/ui/views/` · `src/screens/` 목록 |
| F29 | **`vite.config.ts`의 dev 포트는 `5273`인데**, Google Console 등록·`OAUTH_REDIRECT_ALLOWLIST`·서버 테스트는 전부 **`5173`** 이다 | `webclient/vite.config.ts:27` ↔ `14 §2.2·§3.3` · `web/functions/src/__tests__/webOAuth.test.ts:27` |
| F30 | `qrUsageStore`는 `currentUser` 구독으로 동작하고 **`user===null`이면 요청하지 않는다** → 세션 해제가 재조회 루프를 만들지 않는다 | `src/shell/qrUsageStore.ts:62-70` |
| F31 | vitest 환경은 **node**(jsdom은 파일 상단 주석 opt-in), 커버리지 임계는 `src/domain`에만 걸린다(95/95/95/90) | `vitest.config.ts` |
| F32 | `main.tsx`는 `<StrictMode>`로 마운트한다 → 개발 빌드에서 **effect가 2회 실행**된다 | `main.tsx:26-30` |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 |
|---|------|------|
| A1 | **OA-1**: 실제 Google 계정으로 로그인이 끝까지 성공한다(리디렉트 → 콜백 → 200 → 계정 라벨 변경) | **브라우저·실계정 필요 — 자동화 불가.** [14 §10.7]에 **V21-1**로 등재(S12-8) |
| A2 | CSP `form-action 'none'`이 **`location.assign`에 의한 top-level 이동을 막지 않는다**(폼 제출에만 적용되고 `navigate-to`는 어느 브라우저에도 없다) | 코드로 증명 불가 → **V21-2**(배포본에서 콘솔 CSP 위반 0건 + 이동 성공). 만약 막히면 대안은 §7.3 |
| A3 | `crypto.subtle`이 대상 브라우저의 보안 컨텍스트에서 사용 가능하다(https·localhost) | **S12-2** — 포트 목으로 계약 고정 + 미지원 시 `null` 경로 테스트. 실기기 관측은 **V21-3** |
| A4 | 서버 `OAUTH_REDIRECT_ALLOWLIST`에 실제로 등록된 3개 URI가 클라이언트가 조립하는 값과 **문자 하나까지 같다** | **S12-3**(조립 규칙 단위 테스트) + **V21-1**(실 로그인). 400이 나면 로그의 `redirectRejected` 힌트가 원인을 지목한다 |
| A5 | 개발 서버 포트를 5173으로 되돌려도 다른 문서·스크립트가 깨지지 않는다 | **S12-7** — `grep -rn 5273` 0건 확인 후 변경 |
| A6 | StrictMode 이중 마운트가 콜백을 2회 실행하지 않는다 | **S12-4** — 콜백 소비를 **React 밖 동기 1회**로 구조화(§4.3). 테스트가 2회 호출 시 2번째가 `no-pending`임을 고정 |

---

## 1. 전체 흐름 — 누가 무엇을 언제

```
[어느 화면이든] 상단바 [로그인] 탭   ← 이미 배선돼 있다(F18): go("Login")
   │
Login 화면
   ├ env.googleClientId 빈 값 → 버튼 미노출 + "로그인이 구성되지 않았습니다…" (07 §3)
   └ [Google로 로그인] 탭
        startGoogleSignIn({ returnTo: 현재 overlayReturnTo ?? "Home" })
          1. PKCE(verifier/challenge) · state · nonce 생성        ← Web Crypto (어댑터)
          2. sessionStorage에 {codeVerifier,state,nonce,returnTo,startedAt} 저장
          3. location.assign(authorizeUrl)   prompt=select_account 포함  ← 페이지를 떠난다
                                                    ⚠️ URL을 로그에 남기지 않는다(state·nonce 포함)
   ▼ Google 인증
GET https://{kiosk}/oauth2callback?code=…&state=…
   │  Hosting rewrite → /index.html (F22 — 이미 구성돼 있다)
   ▼
main.tsx 부트스트랩
   [1~6] bootstrap()  (설정·OPFS·로그·브랜딩)
   [9a] captureOauthCallback()        ★ 동기 · 1회성의 원천
          a. location.search 스냅샷
          b. sessionStorage 값 **읽고 즉시 삭제**(takePending)
          c. decideOauthCallback(params, pending, now)  ← 순수 판정
          d. history.replaceState(null,"","/")          ← code·state 제거
   [8]  installShellHandlers()        ← installRouter가 이제 "/" 위에 더미를 쌓는다(F16)
   [9b] runOauthCallback(decision)    비동기
          abort   → { failed, reason: "cancelled" }
          exchange→ POST /auth/google { code, codeVerifier, redirectUri, nonce, clientKind:"web" }
                      200 → setToken(메모리) + sessionStore.login(user)
                      401 → rejected / 501 → notConfigured / 400 → redirectRejected / 그 외 → network
        applyOauthCallbackOutcome()
          success → shellStore.go(returnTo)
          failed  → loginStore.fail(reason) + shellStore.go("Login")
   [10] React 마운트  ← 콜백이 끝난 뒤 <App>이 처음 그려진다(계정 라벨이 첫 페인트부터 정확)
   [11] 첫 제스처
```

**동시에 배선하는 것(C10)**: Bearer가 실제로 붙은 요청이 401이면 → `sessionStore.expireSession()` + 토스트.
PIN 검증만 예외(불일치이지 만료가 아니다).

---

## 2. 설계 이탈 — 지시문·WBS와 다른 4가지 (근거 포함)

### 2.1 이탈 ① `sessionStore.logout()`이 아니라 **새 `expireSession()`** 을 만든다

오케스트레이터 지시문은 "401 감지 → `sessionStore.getState().logout()` 한 곳"이었다. **그대로 하면 규격을 어긴다.**

| # | 근거 |
|---|------|
| 1 | `logout()`은 `currentUser=null` **+ `discardCaptureData()`** 다(F13). 그런데 [02 §5.2] 매트릭스는 **JWT 만료 감지 행의 촬영 데이터를 "유지"** 로 못박는다(F14). [07 §4.3]도 "촬영 중이면 촬영·합성·로컬 보관은 계속된다"고 쓴다 |
| 2 | 실제 피해가 크다. 401이 가장 잘 나는 지점은 **`Qr` 화면의 업로드**(F5 — `optionalBearer`도 무효 토큰이면 401)인데, 거기서 `discardCaptureData()`가 돌면 `finalImage`가 사라져 **[기기에 저장]까지 죽는다.** [07 §4.3]은 반대로 "결과물이 로컬에 남아 있음을 알린다"고 요구한다 |
| 3 | **M1은 그대로 성립한다.** M1 구독은 "`logout()` 호출"이 아니라 **`currentUser`가 null이 되는 것**을 본다(F7). `expireSession()`도 같은 필드를 null로 만들므로 구독 한 곳이 여전히 전부를 덮는다 — 지시문의 취지("직접 `clearToken`을 부르지 않는다")를 **정확히 지킨다** |
| 4 | "진입점은 login/logout뿐"이라는 주석은 [02 §5.1]에서 이미 **"login / logout / resetUserForTest 3개"** 로 쓰여 있다 — 진입점 개수가 아니라 **`currentUser`를 통해서만 바꾼다**가 규칙이다 |

→ `sessionStore`에 `expireSession()`을 추가하고 주석을 "login / logout / expireSession"으로 갱신한다.

### 2.2 이탈 ② 화면 컴포넌트는 `src/ui/views/`에, 로직만 `src/screens/`에

WBS 대상 파일은 `src/screens/login/*`·`src/screens/oauthCallback/*`이다. Step 8~11의 실제 배치는
**뷰 = `ui/views/*.tsx`, 로직 = `screens/*/*.ts`** 다(F28 — `QrView.tsx` ↔ `screens/qr/uploadRunner.ts`).
새 화면만 다른 배치를 쓰면 `ScreenRouter`의 import가 두 갈래가 된다. → **선례를 따른다**(§3 표에 최종 경로).

### 2.3 이탈 ③ `googleSignIn.ts`는 `adapters/auth/`에 두되 **`accountService`에 넣지 않는다**

지시문이 인용한 "`accountService.ts`에 `/auth/google`이 이미 있다"는 **사실이 아니다**(F9).
WBS가 지정한 `src/adapters/auth/googleSignIn.ts`를 그대로 채택한다. `accountService`에 넣지 않는 이유:
`accountService`는 전 메서드가 `auth:"required"`인 **로그인 후** API 묶음인데, `/auth/google`만 `auth:"none"`이라
같은 파일에 두면 "이 서비스는 Bearer가 필요하다"는 불변식이 흐려진다.

### 2.4 이탈 ④ URL 스크럽을 **성공 후가 아니라 판정 직후**에 한다

[07 §2.2]의 절차는 `f. 성공 → … h. history.replaceState`다. 구현은 **판정 직후·교환 전**으로 앞당긴다.

| # | 근거 |
|---|------|
| 1 | 실패 경로에서도 URL에 `code`가 남으면 안 된다. 규격의 순서대로면 401·네트워크 실패 시 주소창에 인가 코드가 남는다 |
| 2 | 교환은 최대 100초 걸릴 수 있다(`REQUEST_TIMEOUT_MS`). 그 사이 새로고침하면 같은 code로 재진입한다 — 스크럽을 먼저 하면 **구조적으로 불가능**해진다 |
| 3 | `installRouter`가 더미 history 엔트리를 쌓기 **전에** 스크럽해야 `/oauth2callback`이 히스토리에 남지 않는다(F16) |

**규격 문서도 이 순서로 정정한다**(S12-8에서 [07 §2.2] h를 e 앞으로 옮김).

---

## 3. 파일별 역할과 시그니처

> 계층: `ui → screens → shell → domain ← adapters`. 신규 파일 **12개** + 수정 **8개**.

### 3.1 도메인 (순수 · node 테스트 · 브라우저 API 0건)

| 파일 | 공개 API |
|------|----------|
| `src/domain/auth/pkceCodec.ts` | `PKCE_VERIFIER_BYTES = 32` · `PKCE_VERIFIER_RE` · `base64UrlFromBytes(bytes: Uint8Array): string` · `isValidCodeVerifier(v: string): boolean` |
| `src/domain/auth/authorizeUrl.ts` | `GOOGLE_AUTHORIZE_ENDPOINT` · `OAUTH_SCOPE` · `OAUTH_CALLBACK_PATH = "/oauth2callback"` · `oauthRedirectUri(origin: string): string` · `buildAuthorizeUrl(input: AuthorizeUrlInput): string` |
| `src/domain/auth/oauthCallbackPolicy.ts` | `OAUTH_FLOW_TIMEOUT_MS = 180_000` · `OauthPendingState` · `OauthCallbackParams` · `OauthAbortReason` · `OauthCallbackDecision` · `parseOauthCallbackParams(search: string)` · `parseOauthPendingState(raw: unknown)` · `decideOauthCallback(params, pending, nowMs)` · `resolveOauthReturnTo(raw: string \| null): AppState` |
| `src/domain/auth/loginFailure.ts` | `LoginFailureReason` · `LoginMessageKey` · `loginFailureMessageKey(r)` · `abortReasonToLoginFailure(r)` |

```ts
// pkceCodec.ts — btoa를 쓰지 않는다(node 테스트에서 동일 동작 보장 + 도메인 무의존).
export const PKCE_VERIFIER_BYTES = 32;
export const PKCE_VERIFIER_RE = /^[A-Za-z0-9\-._~]{43,128}$/;   // ↔ 서버 validation.ts:78
/** RFC 4648 §5 base64url, 패딩 제거. 32바이트 → 43자. */
export function base64UrlFromBytes(bytes: Uint8Array): string;
export function isValidCodeVerifier(value: string): boolean;
```

```ts
// authorizeUrl.ts
export const GOOGLE_AUTHORIZE_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
export const OAUTH_SCOPE = "openid email profile";
export const OAUTH_CALLBACK_PATH = "/oauth2callback";

/** `https://host` → `https://host/oauth2callback`. 트레일링 슬래시를 제거한 뒤 붙인다. */
export function oauthRedirectUri(origin: string): string;

export interface AuthorizeUrlInput {
  readonly clientId: string;
  readonly redirectUri: string;
  readonly codeChallenge: string;
  readonly state: string;
  readonly nonce: string;
}
/**
 * 파라미터 **순서와 인코딩이 계약이다**(테스트가 문자열 전체를 고정한다).
 *   client_id · redirect_uri · response_type=code · scope · code_challenge ·
 *   code_challenge_method=S256 · state · nonce · prompt=select_account
 * ⚠️ `URLSearchParams`를 쓰지 않는다 — 공백을 `+`로 인코딩해 규격(`openid%20email%20profile`)과 어긋난다.
 *    각 값에 `encodeURIComponent`를 적용해 직접 조립한다.
 * ⚠️ `prompt=select_account`는 **생략 불가**다(공용 키오스크 — 07 §2.2).
 * ⚠️ `access_type`·`prompt=consent`를 넣지 않는다(refresh token 미사용 — analysis/61 §3.0).
 */
export function buildAuthorizeUrl(input: AuthorizeUrlInput): string;
```

```ts
// oauthCallbackPolicy.ts
export const OAUTH_FLOW_TIMEOUT_MS = 180_000;               // 3분 — Windows 타임아웃과 동일

export interface OauthPendingState {
  readonly codeVerifier: string;
  readonly state: string;
  readonly nonce: string;
  /** 복귀 화면 이름(문자열 그대로 보관하고 소비 시 clamp한다). */
  readonly returnTo: string;
  /** epoch ms. */
  readonly startedAt: number;
}
export interface OauthCallbackParams {
  readonly code: string | null;
  readonly state: string | null;
  /** Google의 `error` 파라미터(`access_denied` 등). */
  readonly error: string | null;
}
export type OauthAbortReason =
  | "no-pending"       // sessionStorage에 값이 없다(직접 진입·새로고침·재진입)
  | "state-mismatch"   // CSRF 방어 — 대조 실패
  | "provider-error"   // Google이 error를 돌려줬다(사용자 취소 포함)
  | "timeout"          // startedAt +3분 초과
  | "no-code";         // code 파라미터 부재

export type OauthCallbackDecision =
  | { readonly kind: "exchange"; readonly code: string; readonly codeVerifier: string;
      readonly nonce: string; readonly returnTo: AppState }
  | { readonly kind: "abort"; readonly reason: OauthAbortReason };

export function parseOauthCallbackParams(search: string): OauthCallbackParams;
export function parseOauthPendingState(raw: unknown): OauthPendingState | null;

/**
 * **검사 순서가 계약이다**(테스트가 각 분기를 고정한다):
 *   1) pending 없음        → no-pending
 *   2) state 불일치·부재    → state-mismatch   ★ 무엇보다 먼저 CSRF를 끊는다
 *   3) error 파라미터 존재  → provider-error
 *   4) 3분 초과            → timeout
 *   5) code 없음/빈 문자열  → no-code
 *   6) 그 외               → exchange
 * ⚠️ 2)를 3)보다 앞에 두는 이유: 검증되지 않은 콜백의 **어떤 파라미터도 해석하지 않는다**.
 *    사용자에게 보이는 문구는 다섯 사유가 모두 같으므로(07 §2.6) 순서가 UX를 바꾸지 않는다.
 */
export function decideOauthCallback(
  params: OauthCallbackParams,
  pending: OauthPendingState | null,
  nowMs: number,
): OauthCallbackDecision;

/**
 * 복귀 화면 clamp. **리디렉트로 앱이 통째로 재시작됐으므로** 촬영 세션에 의존하는 화면으로는
 * 돌아갈 수 없다. 콜드 스타트(`Home`)에서 합법인 화면만 허용한다:
 *   Home · FrameSelect · Settings · Account   (= canTransition("Home", x) 참인 집합 − Login)
 * 그 외·미지의 문자열·null은 전부 `"Home"`.
 */
export function resolveOauthReturnTo(raw: string | null): AppState;
```

```ts
// loginFailure.ts
export type LoginFailureReason =
  | "cancelled"            // 취소·state 불일치·code 없음·3분 초과 (abort 5종 전부)
  | "rejected"             // 서버 401 — 계정·도메인 거부
  | "notConfigured"        // 서버 501 — SSO 미구성
  | "redirectRejected"     // 서버 400 — redirectUri 거부(B1 미적용 의심). 문구는 network과 같다
  | "network"              // 네트워크·타임아웃·응답 형식 오류·PKCE 불가
  | "clientNotConfigured"; // env.googleClientId 빈 값(버튼 미노출 — 방어용)

export type LoginMessageKey = "cancelled" | "rejected" | "notConfigured" | "network" | "clientNotConfigured";
/** redirectRejected → "network". 그 외는 동일 이름. */
export function loginFailureMessageKey(reason: LoginFailureReason): LoginMessageKey;
/** abort 5종 → 전부 "cancelled"(07 §2.6). 함수로 두어 매핑을 테스트로 고정한다. */
export function abortReasonToLoginFailure(reason: OauthAbortReason): LoginFailureReason;
```

> **배럴 미등재**: `src/domain/index.ts`에 `./auth/*`를 **추가하지 않는다.** 평면 `export *` 배럴이라
> `OAUTH_SCOPE`·`parse*` 같은 짧은 이름이 충돌 위험이고, `accounts/sessionUser`가 이미 미등재 선례다(F25).
> 소비자는 `@domain/auth/...` 직접 경로를 쓴다.

### 3.2 어댑터 (브라우저 API 격리 · **예외를 전파하지 않는다**)

| 파일 | 공개 API |
|------|----------|
| `src/adapters/auth/pkce.ts` | `PkceCryptoPort` · `webCryptoPort()` · `createPkce(port?)` · `randomUrlSafeToken(port?, bytes?)` |
| `src/adapters/auth/oauthStateStore.ts` | `OAUTH_PENDING_KEY` · `savePendingOauth(state)` · `takePendingOauth()` · `clearPendingOauth()` |
| `src/adapters/auth/googleSignIn.ts` | `OAUTH_CLIENT_KIND` · `startGoogleSignIn(input, deps?)` · `exchangeGoogleCode(req, deps?)` · `defaultStartDeps()` |

```ts
// pkce.ts — crypto는 여기서만 만진다(도메인 금지 — F24).
export interface PkceCryptoPort {
  randomBytes(count: number): Uint8Array;
  sha256(ascii: string): Promise<Uint8Array>;
}
/** `crypto.getRandomValues` + `crypto.subtle.digest("SHA-256", TextEncoder)`. 보안 컨텍스트 필요. */
export function webCryptoPort(): PkceCryptoPort;

export interface PkcePair { readonly codeVerifier: string; readonly codeChallenge: string; }
/**
 * 실패(보안 컨텍스트 아님·subtle 부재)는 **예외가 아니라 `null`** 이다(15 §2).
 * 성공 시 `isValidCodeVerifier(codeVerifier)`가 참임을 자체 확인하고, 거짓이면 null + error 로그.
 */
export async function createPkce(port?: PkceCryptoPort): Promise<PkcePair | null>;
/** state·nonce용 난수(기본 32바이트 → 43자 base64url). 서버 nonce 정규식(F4)을 만족한다. */
export function randomUrlSafeToken(port?: PkceCryptoPort, bytes?: number): string;
```

```ts
// oauthStateStore.ts
/**
 * ⚠️ **이 파일이 `sessionStorage`를 만지는 유일한 곳이다**(정적 테스트가 고정 — §6.2).
 * ⚠️ **JWT를 쓰지 않는다.** 여기 들어가는 값은 code_verifier·state·nonce·returnTo·startedAt뿐이고
 *    콜백 처리 시작 시 **즉시 소비·삭제**된다. M2 위반이 아닌 근거는 07 §2.4.
 */
export const OAUTH_PENDING_KEY = "mcphoto.oauth.pending.v1";
/** 실패(프라이빗 모드·용량)는 예외가 아니라 `false`. */
export function savePendingOauth(state: OauthPendingState): boolean;
/** **읽고 즉시 지운다**(원자적 소비 — 재진입 시 반드시 null). 파싱 실패도 null. */
export function takePendingOauth(): OauthPendingState | null;
export function clearPendingOauth(): void;
```

```ts
// googleSignIn.ts
export const OAUTH_CLIENT_KIND = "web" as const;   // ★ 미지정은 desktop 취급(F1)

export interface StartSignInDeps {
  readonly clientId: string;                 // env.googleClientId
  readonly origin: string;                   // location.origin
  readonly createPkce: () => Promise<PkcePair | null>;
  readonly randomToken: () => string;
  readonly savePending: (s: OauthPendingState) => boolean;
  readonly assign: (url: string) => void;    // location.assign
  readonly now: () => number;
}
export type StartSignInOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: LoginFailureReason };
/**
 * 리디렉트 개시. 성공하면 **곧 페이지가 사라진다**(호출자는 버튼을 비활성 상태로 두고 기다린다).
 * ⚠️ authorize URL·state·nonce·verifier를 **로그에 남기지 않는다**. 남기는 것은 `{ returnTo }`뿐.
 */
export async function startGoogleSignIn(
  input: { readonly returnTo: AppState },
  deps?: StartSignInDeps,
): Promise<StartSignInOutcome>;

export interface GoogleLoginResult {
  readonly token: string;
  readonly expiresInSeconds: number;
  readonly user: SessionUser;
}
export type GoogleExchangeOutcome =
  | { readonly ok: true; readonly result: GoogleLoginResult }
  | { readonly ok: false; readonly reason: LoginFailureReason };
/**
 * `POST /auth/google` — **`auth: "none"`**(로그인 전이라 Bearer가 없다. 게이트 키만 붙는다 — F1).
 * 본문: `{ code, codeVerifier, redirectUri, nonce, clientKind: "web" }`
 * **던지지 않는다.** 매핑:
 *   SsoNotConfiguredError(501) → notConfigured
 *   BackendError 401          → rejected
 *   BackendError 400          → redirectRejected  + logger.error("서버가 redirectUri를 거부했다(B1 미적용 가능)")
 *   NetworkError / 그 외       → network
 *   200이지만 token 없음·user 파싱 실패 → network + logger.error("로그인 응답 형식 오류")
 */
export async function exchangeGoogleCode(
  req: { readonly code: string; readonly codeVerifier: string; readonly redirectUri: string; readonly nonce: string },
  client?: BackendClient,
): Promise<GoogleExchangeOutcome>;
```

### 3.3 화면 로직 (React 무관 · node 테스트)

| 파일 | 공개 API |
|------|----------|
| `src/screens/oauthCallback/oauthCallbackRunner.ts` | `captureOauthCallback(deps?)` · `runOauthCallback(decision, deps?)` · `applyOauthCallbackOutcome(outcome, deps?)` · `defaultCaptureDeps()` · `defaultRunDeps()` · `defaultApplyDeps()` |
| `src/screens/login/useGoogleSignIn.ts` | `useGoogleSignIn(): { available, phase, notice, signIn(), close() }` |

```ts
// oauthCallbackRunner.ts — React를 import하지 않는다(uploadRunner와 같은 형태 — 15 §3.1).
export interface CaptureDeps {
  readonly search: () => string;                       // location.search
  readonly takePending: () => OauthPendingState | null;
  readonly now: () => number;
  readonly scrubUrl: () => void;                       // history.replaceState(null,"","/")
}
/**
 * **동기 1회성 소비**. `main.tsx`가 React 마운트·`installRouter` **이전에** 정확히 한 번 부른다.
 * 순서가 계약이다: search 스냅샷 → takePending(읽고 삭제) → decide → scrubUrl.
 * 두 번째 호출은 pending이 없으므로 반드시 `{kind:"abort", reason:"no-pending"}`이다.
 */
export function captureOauthCallback(deps?: CaptureDeps): OauthCallbackDecision;

export type OauthCallbackOutcome =
  | { readonly kind: "success"; readonly returnTo: AppState }
  | { readonly kind: "failed"; readonly reason: LoginFailureReason };

export interface RunDeps {
  readonly redirectUri: string;
  readonly exchange: (req: {...}) => Promise<GoogleExchangeOutcome>;
  /** 기본: setToken(token, expiresInSeconds, Date.now()) → sessionStore.getState().login(user) */
  readonly applySession: (result: GoogleLoginResult) => void;
  readonly now: () => number;
}
/** abort면 교환하지 않는다. 성공 시 **토큰을 먼저 넣고** 사용자 설정(그래야 첫 요청에 Bearer가 붙는다). */
export async function runOauthCallback(decision: OauthCallbackDecision, deps?: RunDeps): Promise<OauthCallbackOutcome>;

export interface ApplyDeps {
  readonly go: (to: AppState) => void;                  // shellStore.getState().go
  readonly fail: (reason: LoginFailureReason) => void;  // loginStore.getState().fail
}
/** success → go(returnTo) · failed → fail(reason) + go("Login"). */
export function applyOauthCallbackOutcome(outcome: OauthCallbackOutcome, deps?: ApplyDeps): void;
```

```ts
// useGoogleSignIn.ts
export type SignInPhase = "idle" | "redirecting";
export interface GoogleSignInBinding {
  /** env.googleClientId가 비어 있지 않은가 — false면 버튼을 렌더하지 않는다(07 §3). */
  readonly available: boolean;
  readonly phase: SignInPhase;
  /** 콜백이 실어 보낸 오류(없으면 null). */
  readonly notice: LoginMessageKey | null;
  /** 탭 → phase="redirecting" → startGoogleSignIn. 실패하면 phase를 idle로 되돌리고 notice를 세운다. */
  signIn(): void;
  /** [닫기] — notice를 지우고 오버레이 복귀. */
  close(): void;
}
export function useGoogleSignIn(): GoogleSignInBinding;
```

### 3.4 셸

| 파일 | 내용 |
|------|------|
| `src/shell/loginStore.ts` **(신규)** | `loginStore`(zustand vanilla) · `useLoginStore` · 상태 `{ notice: LoginFailureReason \| null }` · `fail(reason)` · `clear()` |
| `src/shell/sessionExpiry.ts` **(신규)** | `handleSessionExpired(): void` — 멱등(이미 게스트면 no-op) → `sessionStore.expireSession()` + `shellStore.toast("error", STRINGS.error.sessionExpired)` + `logger.warn("세션 만료 감지(401) — 세션 해제")` |
| `src/shell/sessionStore.ts` **(수정)** | `expireSession(): void` 추가 — `set({currentUser: null})`만. **`discardCaptureData()`를 부르지 않는다**(§2.1) |

### 3.5 UI

| 파일 | 내용 |
|------|------|
| `src/ui/views/LoginView.tsx` **(신규)** | 제목 · [Google로 로그인] 1개(미구성이면 미노출 + 정적 안내) · 인라인 오류 문구(`aria-live="polite"`) · **[닫기] 항상 노출** |
| `src/ui/views/OauthCallbackView.tsx` **(신규)** | `OauthCallbackView`(Spinner + "로그인 처리 중…", **조작 요소 0개**) · `OauthCallbackGate({pending, children})` |
| `src/ui/strings.ts` **(수정)** | `login` 절 신설 + `error.sessionExpired` 문구 정정(F26) |
| `src/App.tsx` **(수정)** | `ScreenRouter`에 `case "Login": return <LoginView />` 추가 · **`devLogin` 제거**(F19) |
| `src/main.tsx` **(수정)** | 부트스트랩 9단계 실제 배선(§4.3) |

```ts
// strings.ts 추가분 — 문구는 analysis/13 §14와 문자 단위로 같다.
login: {
  title: "로그인",
  google: "Google로 로그인",
  redirecting: "Google로 이동하는 중…",
  processing: "로그인 처리 중…",
  errors: {
    cancelled: "Google 로그인이 취소되었습니다.",
    rejected: "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.",
    notConfigured: "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
    network: "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.",
    clientNotConfigured: "로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
  },
},
// 수정: 07 §4.3·12 C10의 규격 문구로 맞춘다(현재 값은 "로그인이 만료되었습니다…" — 소비자 0건이라 안전하다).
error.sessionExpired: "세션이 만료되었습니다. 다시 로그인해 주세요.",
```

### 3.6 HTTP 계층 (C10)

`src/adapters/http/backendClient.ts` **수정** — 2가지만 더한다.

```ts
export interface RequestOptions {
  // …기존…
  /**
   * 401의 의미. 기본값은 **요청에 Bearer가 실제로 붙었는가**로 정한다:
   *   토큰 부착됨 → "expired"(세션 만료 → 해제)
   *   토큰 없음   → "reject"(호출부가 해석 — 예: /auth/google의 401 = 계정 거부)
   * **PIN 검증만 명시적으로 "reject"** 를 넘긴다 — 한 번 틀렸다고 로그아웃되면 안 된다(07 §4.3 · E17).
   */
  readonly unauthorized?: "expired" | "reject";
}
export interface BackendClientDeps {
  // …기존…
  /** 기본 `handleSessionExpired`(shell/sessionExpiry). 테스트 주입점. */
  readonly onSessionExpired?: () => void;
}
```

401 처리는 **오류를 던지기 직전** 한 곳에서만 한다.

```ts
if (response.status === 401) {
  const meaning = options.unauthorized ?? (token !== null ? "expired" : "reject");
  if (meaning === "expired") onSessionExpired();     // ← sessionStore.expireSession() → M1 구독이 토큰 폐기
}
throw toBackendError(response.status, envelope);
```

| 호출 | 결과 | 왜 |
|------|------|-----|
| `/auth/google`(`auth:"none"`) 401 | **reject** | 토큰이 없다. 이건 "계정 거부"다 |
| `/accounts/me/pin/verify`(명시 `reject`) | **reject** | PIN 불일치. 세션 불변(E17) |
| `/accounts/*`·`/frames`(`auth:"required"`) 401 | **expired** | 만료·위조 |
| `/uploads/prepare`(`auth:"optional"`, 로그인 상태) 401 | **expired** | F5 — 서버가 무효 토큰을 401로 거부한다 |
| `/uploads/prepare`(게스트, 토큰 없음) 401 | **reject** | 게이트 키 문제이지 세션 문제가 아니다 |

`src/adapters/http/accountService.ts` **수정** — `verifyMyPin`에 `unauthorized: "reject"` 한 줄 + 이유 주석.

### 3.7 인프라

| 파일 | 변경 | 이유 |
|------|------|------|
| `webclient/vite.config.ts` | `server.port` **5273 → 5173** + `strictPort: true` | F29 — Google Console·서버 허용 목록이 전부 5173이라 현재 설정으로는 **로컬 로그인이 절대 성공할 수 없다**. `strictPort`가 없으면 포트 충돌 시 5174로 조용히 옮겨가 같은 실패를 재현한다 |
| `web/firebase.json` | **변경 없음** | F22 — kiosk 타깃에 SPA rewrite와 `/oauth2callback` no-cache가 이미 있다. `hosting:default`는 손대지 않는다(함정 #9) |

---

## 4. 핵심 설계 판단

### 4.1 콜백은 화면 상태가 아니라 **URL 경로**다

`APP_STATES`에 `OauthCallback`을 **추가하지 않는다**(F20).

| 근거 |
|------|
| 상태 머신은 "손님이 오가는 화면"의 전이 규칙이다. 콜백은 200ms~2초 존재하는 **부트스트랩 국면**이고 전이 대상이 아니다 |
| 상태로 만들면 `canTransition` 표 13×13에 아무도 못 가는 행/열이 생기고, 뒤로가기 가로채기(`isOverlayScreen`)·유휴 감시(`isSessionActive`) 판정에 의미 없는 분기가 는다 |
| [02 §3] 라우팅 주석이 "경로는 2개뿐"이라고 이미 규정한다 — 경로 분기는 `classifyRoute`(F15)가 담당한다 |

→ 콜백은 `ScreenRouter` **밖**에서, `<App>`을 감싼 게이트가 렌더한다.

### 4.2 콜백 처리 중에는 `<App>`을 **아예 마운트하지 않는다**

`OauthCallbackGate`가 `pending`이 끝날 때까지 스피너만 렌더한다.

| 근거 |
|------|
| 마운트하면 손님이 **Home을 잠깐 보고** 계정 라벨이 [로그인]→id로 튄다(깜빡임) |
| `App`의 effect(유휴 감시 시작 등)가 세션 확정 전에 돌아 순서 의존 버그의 씨앗이 된다 |
| 스피너는 "사용자 조작 요소 없음"이 규격이다([07 §2.5]) — 게이트가 그걸 구조적으로 보장한다 |

### 4.3 StrictMode 이중 실행 방어는 **React 밖 동기 소비**로 한다

`useEffect` 안에서 콜백을 처리하면 개발 빌드에서 2회 실행되고, 2번째는 `sessionStorage`가 이미 비어
**"취소되었습니다"** 로 끝난다(성공 직후 실패 문구가 덮이는 최악의 형태다).

```tsx
// main.tsx (부트스트랩 9단계) — React가 개입하기 전에 끝낸다.
const route = classifyRoute(window.location.pathname);
const decision = route === "oauthCallback" ? captureOauthCallback() : null;   // ★ 동기·1회
installShellHandlers();                                                       // installRouter는 이제 "/" 위에 쌓는다
const callbackPending =
  decision === null ? null : runOauthCallback(decision).then(applyOauthCallbackOutcome);
mount(result.branding, callbackPending);
```

`OauthCallbackGate`는 **이미 만들어진 promise 하나**를 구독할 뿐이라, effect가 두 번 붙어도
부수효과는 한 번만 일어난다(두 번째 `.then`은 같은 결과를 다시 받아 `setDone(true)`를 반복할 뿐 — 멱등).

### 4.4 `returnTo`는 **sessionStorage로 옮기고 콜드 스타트 기준으로 clamp**한다

`overlayReturnTo`는 메모리라 리디렉트로 사라진다(F21). 그렇다고 아무 화면이나 복귀시킬 수 없다 —
**앱이 통째로 재시작됐으므로 촬영 세션·합성 결과가 전부 없다.**
`resolveOauthReturnTo`가 `Home·FrameSelect·Settings·Account` 4개로 clamp하는 이유가 이것이고,
이 집합은 마침 `canTransition("Home", x)`가 참인 집합에서 `Login`을 뺀 것과 정확히 같다(F20) →
`go()`가 거부당하는 경우가 **구조적으로 없다**.

> 손님이 `Capture` 도중 로그인할 수는 없다(그 화면엔 상단바가 없다 — F18의 `isTopBarVisible`).
> 실제로 발생 가능한 `returnTo`는 `Home`·`FrameSelect`·`Settings`·`Result`·`CutSelect` 정도이며,
> 뒤 둘은 세션이 사라졌으니 `Home`이 옳은 복귀점이다.

### 4.5 토큰 만료 감지의 **단일 지점**

`backendClient`의 401 분기 한 곳만 `onSessionExpired`를 부른다. 화면·서비스 어디에도 `isUnauthorized(err)` 기반의
세션 해제를 두지 않는다(두 곳이 되면 토스트가 2번 뜨고, `handleSessionExpired`가 멱등이어도 진단 로그가 중복된다 —
15 §4 함정 #6과 동종).

### 4.6 로그에 남기는 것과 남기지 않는 것

| 남긴다 | 남기지 않는다 |
|--------|---------------|
| `logger.info("Google 로그인 리디렉트", { returnTo })` | authorize **URL 전체**(state·nonce 포함) |
| `logger.warn("Google 로그인 중단", { abortReason })` | `code`·`state`·`nonce`·`codeVerifier`·`token` — **키 이름 자체가 마스킹 대상**(F12) |
| `logger.info("로그인 성공", { userId, role, expiresInSec })` | `email`(개인정보 — 표시에만 쓴다) |
| `logger.error("서버가 redirectUri를 거부했다(B1 미적용 가능)", { status: 400, errorCode })` | ⚠️ `errorCode`로 쓴다. `code`로 쓰면 `[masked]`가 된다(F11·함정 #1) |
| `logger.warn("세션 만료 감지(401) — 세션 해제", { path })` | — |

---

## 5. 데이터 흐름 · 생명주기

```
                     ┌──────────────┐
   [Google로 로그인]  │ sessionStorage│  mcphoto.oauth.pending.v1
        │            │  (단발·1회성)  │  {codeVerifier,state,nonce,returnTo,startedAt}
        ├── save ───▶└──────┬───────┘
        │                   │ takePendingOauth() = 읽기+삭제 (콜백 최초 진입 1회)
   location.assign          ▼
        ⇢ 페이지 이탈 ⇢ Google ⇢ /oauth2callback
                            │
                    decideOauthCallback (순수)
                            │ exchange
                            ▼
                    POST /auth/google  (auth:"none" · 게이트 키만 · clientKind:"web")
                            │ 200
              ┌─────────────┴─────────────┐
              ▼                           ▼
   authStore.setToken(메모리)   sessionStore.login(user)
              ▲                           │
              │  M1 구독(이미 설치됨)      │ currentUser 변경 통지
              └───────── clearToken ◀─────┘   (null이 될 때만)
                                          └─▶ qrUsageStore 재조회(temp_user일 때만 — F30)
```

| 자원 | 획득 | 해제 |
|------|------|------|
| `sessionStorage` 항목 | `startGoogleSignIn` | `takePendingOauth`(콜백 최초 진입) · `clearPendingOauth`(시작 실패 시 즉시) |
| JWT(메모리) | `setToken` | M1 구독(`currentUser` null) — `logout`·`expireSession`·새로고침(프로세스 소멸) |
| `history` 엔트리 | 브라우저 | `replaceState("/")`가 콜백 URL을 덮는다(`installRouter` 이전에) |
| 이벤트 리스너 | `OauthCallbackGate`의 effect | cleanup의 `alive=false`(마운트 해제 후 setState 금지) |

> **타이머·구독·`AbortController`를 새로 만들지 않는다.** 교환 요청의 취소는 `backendClient`의
> 100초 타임아웃이 담당하고, 그 사이 화면은 스피너 하나뿐이라 이탈 경로가 없다(콜백 페이지엔 조작 요소가 없다).

---

## 6. 테스트 전략

### 6.1 단위 테스트 (신규 7파일 · `tests/unit/auth/`)

| 파일 | 고정하는 것 |
|------|-------------|
| `pkceCodec.test.ts` | base64url 벡터(RFC 4648 `""`·`"f"`·`"fo"`…를 바이트로) · 32바이트 → **43자** · 결과가 `PKCE_VERIFIER_RE` 통과 · 패딩(`=`)·`+`·`/` 부재 |
| `authorizeUrl.test.ts` | 조립 문자열 **전체 일치**(파라미터 순서 포함) · `scope=openid%20email%20profile`(`+` 아님) · `prompt=select_account` 존재 · `code_challenge_method=S256` · `access_type` **부재** · `oauthRedirectUri("https://h/")` = `https://h/oauth2callback` |
| `oauthCallbackPolicy.test.ts` | abort 5분기 각각 · 검사 순서(예: error+state불일치 → **state-mismatch**) · 경계 `elapsed === 180000`은 통과, `180001`은 timeout · `parseOauthPendingState` 방어(타입 오류·필드 누락 → null) · `resolveOauthReturnTo` 허용 4종 + 그 외 전부 Home |
| `loginFailure.test.ts` | `redirectRejected → "network"` · abort 5종 → 전부 `cancelled` |
| `pkce.test.ts` | 목 포트로 결정론적 verifier/challenge · `subtle` 부재 시 **null**(throw 아님) · `randomUrlSafeToken`이 서버 nonce 정규식 통과 |
| `oauthStateStore.test.ts` | save→take 왕복 · **take 2회째는 null** · 저장소 예외 시 `false`/`null`(throw 아님) · 손상 JSON → null |
| `googleSignIn.test.ts` | **본문에 `clientKind:"web"`** · **`auth:"none"`** · `path === "auth/google"` · 401/501/400/네트워크/형식오류 5매핑 · start: clientId 빈값·PKCE null·save 실패 각각의 reason과 **`assign` 미호출** · 성공 시 `assign` 1회 + URL에 저장한 state 포함 |
| `oauthCallbackRunner.test.ts` | 순서 계약(`search`→`takePending`→`scrubUrl` **before** `exchange`) · abort면 `exchange` 0회 · 성공 시 `applySession` → outcome.returnTo · **2회 호출 시 2번째가 no-pending** · `applyOauthCallbackOutcome` 분기 |
| `sessionExpiry.test.ts` | `auth:"required"` 401 → `expireSession` 호출 + **`session.cuts` 불변** + 토큰 null(M1 경유) · PIN verify(`unauthorized:"reject"`) → `currentUser` 불변 · `auth:"none"` 401 → 세션 불변 · 이미 게스트면 토스트 0건(멱등) |

> **§3.2 관례 준수**: 시간(`now`)·난수(`randomBytes`)·저장소·`location`·`history`를 **전부 주입**한다.
> `vi.useFakeTimers()`를 쓰지 않는다 — 3분 판정은 `now()` 값을 바꿔 직접 검증한다.

### 6.2 정적 불변식 (`tests/unit/auth/authInvariants.test.ts`) — 15 §3.4 관례

| # | 불변식 | 검사 |
|---|--------|------|
| **M2-a** | `sessionStorage`는 **`adapters/auth/oauthStateStore.ts` 한 파일에만** 나온다 | `src/` 전체 grep(자기 자신 + 기존 `settingsRepo`의 `localStorage`는 대상 아님) |
| **M2-b** | 신규 auth 파일 전부에 `localStorage`·`indexedDB`·`document.cookie` **0건** | 파일 목록 순회 |
| **M2-c** | `authStore.ts`에 저장소 API 0건 | **기존 테스트 유지**(깨뜨리지 않는다) |
| **AUTH-1** | `sessionStore.login(`을 부르는 제품 코드는 **`oauthCallbackRunner.ts` 1곳** | `src/` grep — `devLogin` 류의 세션 위조 헬퍼 재발 방지 |
| **AUTH-2** | `googleSignIn.ts` 소스에 `clientKind: "web"` 리터럴이 존재한다 | 문자열 포함 검사(누락 = 데스크톱 구성으로 교환 시도 → 원인 파악이 어려운 실패) |
| **AUTH-3** | `auth/` 신규 파일의 `logger.*` 호출 컨텍스트에 `code:`·`state:`·`nonce:`·`codeVerifier:`·`token:`·`pin:` 키가 없다 | 정규식 `/logger\.\w+\([^)]*\b(code\|state\|nonce\|codeVerifier\|token\|pin)\s*:/` 0건 |
| **AUTH-4** | `src/App.tsx`에 `devLogin` 0건 | 문자열 검사 |
| **AUTH-5** | `buildAuthorizeUrl` 소스에 `prompt=select_account`가 있다 | 문자열 검사(키오스크 필수 — 빠지면 손님이 직전 운영자 계정으로 원탭 로그인된다) |

### 6.3 E2E (Step 17로 이월 — 여기서는 시나리오만 확정)

E3(로그아웃 후 무토큰 업로드)·E3b(재로그인 토큰 교체)·E4(저장소 토큰 0건)·E17(PIN 401이 로그아웃 아님)은
**Playwright 도입이 Step 17**이므로([15 §1] 표) 이번 Step에서는 **단위 테스트로 등가 보장**을 만든다:
E3 = `sessionExpiry.test.ts`의 M1 경유 검증 + 기존 `authStore.test.ts`, E4 = §6.2 M2-a/b, E17 = `sessionExpiry.test.ts`.

### 6.4 `docs/spec-vectors/`를 만들지 않는 이유 (판단 근거)

만들지 **않는다.** 지시문의 판단이 옳다고 확인했다.

| # | 근거 |
|---|------|
| 1 | 벡터의 목적은 **Windows ↔ 웹 교차 고정**이다. 그런데 웹의 판정 대상(`state` 대조·`sessionStorage` 소비·`returnTo` clamp)은 **웹에만 있는 개념**이다 — Windows는 loopback 리스너가 콜백을 받고 `returnTo`가 메모리에 남는다(B11) |
| 2 | 양쪽에 공통인 것(`code_verifier` 43~128자·문자 집합, `code_challenge` 43자, scope, `prompt`)은 이미 **서버 정규식**(`validation.ts`)이 진실원이고, 서버 테스트(`webOAuth.test.ts`)가 고정한다. 벡터를 더하면 진실원이 셋이 된다 |
| 3 | PKCE 값은 **난수**라 고정 벡터를 만들 수 없다. 고정할 수 있는 것은 `base64UrlFromBytes` 같은 인코딩 함수인데, 이건 RFC 4648 표준 벡터를 웹 단위 테스트에 직접 쓰는 편이 낫다(Windows는 .NET `Base64Url`을 쓰므로 대조 가치가 없다) |
| 4 | 15 §3.3의 규칙("규격을 바꿀 때 벡터를 먼저 고친다")이 적용될 **공유 규격 자체가 없다** |

---

## 7. 위험과 대응

### 7.1 실패 모드 표

| 증상 | 원인 | 진단에 남는 것 | 대응 |
|------|------|----------------|------|
| Google이 `redirect_uri_mismatch` 화면을 띄운다 | Console 등록 URI ≠ 조립값. **개발에서는 포트(5173/5273)가 첫 용의자**(F29) | 없음(우리 페이지에 도달하지 못한다) | S12-7의 포트 정합. 배포본은 `location.origin` 그대로라 문제없다 |
| 콜백에서 즉시 "취소되었습니다" | `sessionStorage` 미보존(프라이빗 모드·다른 브라우저로 복귀) 또는 **다른 도메인으로 복귀**(`web.app`으로 시작해 `firebaseapp.com`으로 돌아옴 — 저장소가 오리진별) | `Google 로그인 중단 { abortReason: "no-pending" }` | 문구는 규격대로. 운영 문서에 "로그인은 접속한 도메인 그대로 진행" 명시(S12-8) |
| 서버 **400** | `OAUTH_REDIRECT_ALLOWLIST` 미등록·오타 | `서버가 redirectUri를 거부했다(B1 미적용 가능) { status:400, errorCode }` | 손님에겐 네트워크 문구, 운영자는 로그로 원인 특정 |
| 서버 **501** | 웹 client_id/secret 미구성(F1) | `백엔드 오류 응답 { status:501, errorCode:"not_implemented" }` | "구성되지 않았습니다" 문구 |
| 로그인은 됐는데 새로고침하면 게스트 | **정상**이다(C6·M2) | — | 안내하지 않는다([07 §4.4] — 과잉 안내) |
| `Qr`에서 갑자기 게스트가 된다 | 토큰 8시간 만료 → 401(F5) | `세션 만료 감지(401) — 세션 해제` | 토스트 + 촬영 데이터 유지(§2.1) → [기기에 저장]은 계속 동작 |

### 7.2 키오스크 특유의 위험

| 위험 | 대응 |
|------|------|
| 이전 손님의 Google 세션이 브라우저에 남아 **원탭으로 남의 계정에 로그인** | `prompt=select_account` **필수**(정적 테스트 AUTH-5로 고정) |
| 전체화면(kiosk)에서 리디렉트로 나갔다가 돌아오면 **전체화면이 풀린다** | 이미 대비돼 있다 — `fullscreenLost` 배너 + 첫 제스처 재요청(`installFirstGesture`). 콜백 게이트가 끝난 뒤 `<App>`이 배너를 렌더한다 |
| 손님이 리디렉트 도중 이탈 → `sessionStorage`에 pending이 남는다 | 3분 타임아웃 판정이 다음 콜백을 막고, 값 자체는 탭 종료 시 사라진다. 추가로 `startGoogleSignIn`이 매번 **덮어쓴다**(키 1개 고정) |

### 7.3 CSP가 리디렉트를 막는다면 (A2 반증 시 대안)

`form-action`은 **폼 제출에만** 적용되고 `navigate-to`는 명세에서 제거돼 어느 브라우저에도 없다 →
`location.assign`은 CSP의 통제를 받지 않는다는 것이 설계 전제다(A2).
만약 배포본에서 이동이 차단되면(콘솔에 CSP 위반이 찍히면) 대응은 **`form-action 'none'` → `form-action 'self' https://accounts.google.com`**
한 줄이며, `web/firebase.json`의 **kiosk 타깃만** 고친다(`hosting:default` 금지 — 함정 #9).
`connect-src`는 **건드릴 필요가 없다** — 우리는 Google에 `fetch`하지 않는다(code 교환은 서버가 한다).

---

## 8. 구현 단계 (WBS 블루프린트)

> 전체 8단계. 각 단계는 self-contained이며 다른 단계 없이 검증된다.
> 공통 검증 명령: `cd webclient && npx tsc --noEmit && npx vitest run` (기준선 **873 통과 / 34파일**).

### Step 12-1: 도메인 인증 판정 계층 (순수)
- **Context Brief**: MC포토 웹 클라이언트(`webclient/`)에 Google SSO 리디렉트 로그인을 넣는다. 이 단계는 **브라우저 API를 전혀 쓰지 않는 판정·조립 함수**만 만든다. `src/domain`은 `tests/unit/domain/purity.test.ts`가 import·API 사용을 기계적으로 검사하므로 `crypto`·`sessionStorage`·`Date.now`를 쓰면 즉시 실패한다. 규격은 `docs/web-client/07 §2`, 서버 검증 규칙은 `web/functions/src/domain/validation.ts`.
- **대상 파일**: `webclient/src/domain/auth/pkceCodec.ts`(신규) · `webclient/src/domain/auth/authorizeUrl.ts`(신규) · `webclient/src/domain/auth/oauthCallbackPolicy.ts`(신규) · `webclient/src/domain/auth/loginFailure.ts`(신규) · `webclient/tests/unit/auth/pkceCodec.test.ts`·`authorizeUrl.test.ts`·`oauthCallbackPolicy.test.ts`·`loginFailure.test.ts`(신규)
- **선행 조건**: 없음
- **구현 내용**: 설계 §3.1의 시그니처 그대로 4파일을 만든다.
  - `base64UrlFromBytes`는 **`btoa`를 쓰지 않고** 자체 알파벳 루프로 구현한다(도메인 무의존 + node 동일 동작).
  - `buildAuthorizeUrl`은 `URLSearchParams`를 쓰지 않고 `encodeURIComponent`로 직접 조립한다(공백이 `+`가 되면 규격 위반).
  - `decideOauthCallback`의 검사 순서는 §3.1 주석 그대로(**state 대조가 error보다 먼저**).
  - `resolveOauthReturnTo`는 `Home·FrameSelect·Settings·Account`만 통과시키고 나머지는 `"Home"`.
  - **`src/domain/index.ts`에 추가하지 않는다**(평면 배럴 이름 충돌 회피 — `accounts/sessionUser` 선례).
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/domain/purity.test.ts tests/unit/auth`
- **완료 기준**:
  - [관측] 새 도메인 4파일이 `purity.test.ts`의 파일 목록에 자동 포함돼 **통과**한다. `authorizeUrl.test.ts`가 조립 문자열 전체 일치로 `prompt=select_account`·`scope=openid%20email%20profile`·`code_challenge_method=S256`을 고정한다. `decideOauthCallback`의 abort 5분기가 각각 단언된다.
  - [non-goal] `src/domain/index.ts`·기존 도메인 파일·기존 873 테스트가 **무변경**이다. 어떤 신규 파일도 `crypto`·`sessionStorage`·`Date.now`를 부르지 않는다.
  - [trigger] 이 단계에서 만든 함수는 **아무도 호출하지 않는다**(다음 단계가 연결). 앱 동작은 변하지 않는다.
- **롤백**: 신규 8파일 삭제(다른 파일 무변경이므로 부작용 없음).
- [ ] 완료

### Step 12-2: PKCE·pending 상태 어댑터 (브라우저 API 격리)
- **Context Brief**: Step 12-1의 순수 함수 위에 **브라우저 전용 얇은 래퍼**를 얹는다. 이 저장소의 어댑터 규칙은 "**예외를 전파하지 않는다** — `false`/`null`을 돌려주고 상위가 상태로 표현한다"(`docs/web-client/15 §2`)이며 `console.*` 금지·`logger.*`만 쓴다. `sessionStorage`에 PKCE 임시값을 두는 것은 M2(JWT 메모리 전용) 위반이 **아니다**(근거: `07 §2.4` — 저장하는 값에 JWT가 없고 콜백 즉시 삭제된다).
- **대상 파일**: `webclient/src/adapters/auth/pkce.ts`(신규) · `webclient/src/adapters/auth/oauthStateStore.ts`(신규) · `webclient/tests/unit/auth/pkce.test.ts`·`oauthStateStore.test.ts`(신규)
- **선행 조건**: Step 12-1(도메인 4파일)
- **구현 내용**: 설계 §3.2의 시그니처대로 2파일.
  - `webCryptoPort()`는 `crypto.getRandomValues` + `crypto.subtle.digest("SHA-256", new TextEncoder().encode(v))`. **런타임 감지**로 부재 시 실패 경로를 탄다(타입을 믿지 않는다 — 15 §4 함정 #2).
  - `createPkce`는 실패 시 `null` + `logger.error("PKCE 생성 실패", { reason })`. 생성한 verifier가 `isValidCodeVerifier`를 통과하는지 **자체 확인**한다.
  - `takePendingOauth`는 `getItem` → `removeItem` → `JSON.parse` → `parseOauthPendingState` 순서(삭제를 파싱보다 먼저 — 손상 값도 반드시 사라진다).
  - 두 파일 모두 `try/catch`로 저장소 예외를 흡수한다.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/auth`
- **완료 기준**:
  - [관측] 목 포트 주입으로 결정론적 `codeVerifier`/`codeChallenge`가 나오고, `subtle` 부재 시 **`null`이 반환된다(throw 아님)**. `savePendingOauth`→`takePendingOauth` 왕복이 성립하고 **두 번째 `take`는 `null`** 이다.
  - [non-goal] 저장소가 예외를 던지는 환경에서도 **어떤 함수도 throw하지 않는다**. `authStore.ts`는 무변경(M2 기존 테스트 유지).
  - [trigger] 저장·삭제는 명시 호출에서만 일어난다 — 모듈 import만으로 `sessionStorage`에 접근하지 않는다.
- **롤백**: 신규 4파일 삭제.
- [ ] 완료

### Step 12-3: `googleSignIn` 어댑터 (리디렉트 개시 + code 교환)
- **Context Brief**: `POST /auth/google`을 부르는 유일한 어댑터를 만든다. 서버는 이 경로에 **API 키 게이트만** 요구하고 Bearer를 요구하지 않으므로 `backendClient.request`에 **`auth: "none"`** 을 넘겨야 한다(`auth:"required"`면 토큰이 없어 요청조차 나가지 않는다). 본문에 **`clientKind: "web"`이 없으면 서버가 desktop client_id로 교환을 시도해 반드시 실패한다**(`web/functions/src/routes/auth.ts:49-65`). 오류 타입은 `src/adapters/http/errors.ts`에 이미 있다(`SsoNotConfiguredError`=501 자동 매핑). 로그 컨텍스트 키가 `code`면 마스킹되므로 서버 오류 코드는 **`errorCode`** 로 남긴다.
- **대상 파일**: `webclient/src/adapters/auth/googleSignIn.ts`(신규) · `webclient/tests/unit/auth/googleSignIn.test.ts`(신규)
- **선행 조건**: Step 12-1, Step 12-2
- **구현 내용**: 설계 §3.2의 `startGoogleSignIn`·`exchangeGoogleCode`.
  - `startGoogleSignIn`: clientId 빈값 → `clientNotConfigured` / PKCE null → `network` / save 실패 → `network`(+`clearPendingOauth()`) / 성공 → `assign(buildAuthorizeUrl(...))`. **실패 경로에서 `assign`을 부르지 않는다.**
  - `exchangeGoogleCode`: `client.request({ method:"POST", path:"auth/google", auth:"none", body:{code,codeVerifier,redirectUri,nonce,clientKind:OAUTH_CLIENT_KIND} })`. 오류 매핑 5종(§3.2 주석). 200 응답은 `parseSessionUser(raw.user)`로 파싱하고 `token`이 비었거나 user가 null이면 `network` + `logger.error("로그인 응답 형식 오류")`. `expiresIn`이 양수가 아니면 `28800` 폴백 + `logger.warn`.
  - 로그: §4.6 표 그대로. **authorize URL·code·state·nonce·verifier를 남기지 않는다.**
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/auth`
- **완료 기준**:
  - [관측] 가짜 `BackendClient`가 기록한 요청이 `{method:"POST", path:"auth/google", auth:"none"}`이고 본문에 **`clientKind:"web"`** 이 있다. 401→`rejected`, 501→`notConfigured`, 400→`redirectRejected`, `NetworkError`→`network`가 각각 단언된다.
  - [non-goal] 어떤 실패 경로에서도 **예외가 밖으로 나가지 않는다**. clientId 빈값·PKCE 실패·save 실패에서 `assign` 호출 **0회**.
  - [trigger] 리디렉트(`assign`)는 PKCE 생성과 pending 저장이 **둘 다 성공했을 때만** 일어난다.
- **롤백**: 신규 2파일 삭제(다른 코드가 아직 import하지 않는다).
- [ ] 완료

### Step 12-4: 콜백 러너 + `main.tsx` 부트스트랩 배선
- **Context Brief**: `/oauth2callback`로 돌아온 페이지를 처리한다. `src/shell/router.ts`의 `classifyRoute(pathname)`가 이미 존재하지만 **아무도 호출하지 않는다** — 이 단계가 첫 소비자다. 핵심 제약 3가지: ① `<StrictMode>`가 개발 빌드에서 effect를 2회 실행하므로 **콜백 소비를 React 밖 동기 코드로** 해야 한다(2회째는 `sessionStorage`가 비어 "취소" 처리된다). ② `installRouter()`가 설치 즉시 현재 URL 위에 더미 history 엔트리를 쌓으므로 **URL 스크럽을 그 전에** 해야 한다. ③ 토큰이 메모리 전용이라 **처리 후 페이지를 다시 로드하면 안 된다** — `location.replace`가 아니라 `history.replaceState`를 쓴다.
- **대상 파일**: `webclient/src/screens/oauthCallback/oauthCallbackRunner.ts`(신규) · `webclient/src/ui/views/OauthCallbackView.tsx`(신규) · `webclient/src/main.tsx`(수정) · `webclient/tests/unit/auth/oauthCallbackRunner.test.ts`(신규)
- **선행 조건**: Step 12-3, Step 12-5의 `loginStore`(순서를 바꾸려면 12-5를 먼저 해도 된다. 그렇지 않다면 `applyOauthCallbackOutcome`의 `fail` dep을 **주입 필수**로 두고 기본값 배선만 12-5에서 채운다)
- **구현 내용**:
  - `oauthCallbackRunner.ts`: §3.3의 `captureOauthCallback`(동기) / `runOauthCallback`(비동기) / `applyOauthCallbackOutcome`. **React를 import하지 않는다.**
  - `OauthCallbackView.tsx`: `Spinner` + `STRINGS.login.processing`만. **버튼·링크 0개.** 같은 파일에 `OauthCallbackGate({pending, children})` — `pending`이 settle될 때까지 뷰를, 이후 `children`을 렌더한다. cleanup에서 `alive=false`로 언마운트 후 setState를 막는다.
  - `main.tsx`: §4.3 코드 그대로. 순서는 `bootstrap()` → `classifyRoute` → `captureOauthCallback()` → `installShellHandlers()` → `runOauthCallback().then(apply)` → `mount(branding, pending)`. `mount`는 `<OauthCallbackGate pending={…}><App/></OauthCallbackGate>`를 렌더한다. 실패 폴백 경로(`bootstrap` reject)도 **같은 순서**를 유지한다.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run`
- **완료 기준**:
  - [관측] `oauthCallbackRunner.test.ts`가 호출 순서 `search → takePending → scrubUrl → exchange`를 배열로 고정하고, abort 판정 시 `exchange` 호출 **0회**를 단언한다. `captureOauthCallback`을 연속 2회 부르면 2번째가 `{kind:"abort", reason:"no-pending"}`이다. 성공 outcome에서 `applySession`이 1회 불리고 `returnTo`가 clamp된 값이다.
  - [non-goal] `main.tsx`에 `location.assign`·`location.replace`·`window.location.href =` **0건**(리로드하면 메모리 토큰이 즉시 사라진다). 일반 경로(`/`) 접속 시 `captureOauthCallback`이 **호출되지 않고** 기존 부팅 동작·873 테스트가 그대로 통과한다.
  - [trigger] 콜백 처리는 `classifyRoute(location.pathname) === "oauthCallback"`일 때만 시작된다. `<App>`은 콜백 promise가 settle된 **뒤에** 처음 마운트된다.
- **롤백**: `main.tsx`를 이전 버전으로 되돌리고 신규 3파일 삭제 → 앱은 게스트 전용으로 정상 동작.
- [ ] 완료

### Step 12-5: `Login` 화면 + 오류 전달 스토어 + `devLogin` 제거
- **Context Brief**: `App.tsx`의 `ScreenRouter`에서 `Login`은 아직 `DummyScreen`이다. 상단바 [로그인] → `go("Login")` 배선은 **이미 있다**(`App.tsx:235-237`). 화면 규격은 `docs/web-client/03 §3`: 버튼 1개(`Google로 로그인`), `GoogleClientId`가 비면 **버튼을 통째로 숨기고** 정적 안내, **[닫기]는 항상 노출**, 오류 문구 5종 구분. 문구는 `src/ui/strings.ts` 카탈로그에만 둔다(컴포넌트에 문자열을 흩뿌리지 않는다). `App.tsx` 끝의 `devLogin(id)`는 "Step 12가 실 로그인으로 대체"라고 주석에 적힌 개발용 헬퍼이며 참조가 0건이다.
- **대상 파일**: `webclient/src/shell/loginStore.ts`(신규) · `webclient/src/screens/login/useGoogleSignIn.ts`(신규) · `webclient/src/ui/views/LoginView.tsx`(신규) · `webclient/src/ui/strings.ts`(수정) · `webclient/src/App.tsx`(수정) · `webclient/tests/unit/auth/loginBinding.test.ts`(신규)
- **선행 조건**: Step 12-3
- **구현 내용**:
  - `loginStore.ts`: zustand vanilla + `useLoginStore`. 상태 `{ notice: LoginFailureReason | null }`, 액션 `fail(reason)`·`clear()`.
  - `useGoogleSignIn.ts`: §3.3 시그니처. `available = env.googleClientId.length > 0`. `signIn()`은 `clear()` → `phase="redirecting"` → `startGoogleSignIn({returnTo: shellStore.getState().overlayReturnTo ?? "Home"})`; 실패면 `phase="idle"` + `fail(reason)`. `close()`는 `clear()` + `shellStore.getState().closeOverlay()`.
  - `LoginView.tsx`: 제목 · (available일 때만) 기본 버튼 — `phase==="redirecting"`이면 `disabled` + 라벨 `STRINGS.login.redirecting` · 인라인 오류 `<p aria-live="polite">` · **[닫기] 항상 렌더**. `available===false`면 버튼 대신 `STRINGS.login.errors.clientNotConfigured` 정적 안내.
  - `strings.ts`: §3.5의 `login` 절 추가 + `error.sessionExpired`를 **"세션이 만료되었습니다. 다시 로그인해 주세요."** 로 정정(현재 소비자 0건이라 안전).
  - `App.tsx`: `ScreenRouter`에 `case "Login": return <LoginView />;` 추가, **`devLogin` 함수 삭제**.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run`
- **완료 기준**:
  - [관측] `useGoogleSignIn` 로직 테스트(주입 목)에서 `available===false`면 `signIn()`이 `startGoogleSignIn`을 **부르지 않고** notice가 `clientNotConfigured`다. 실패 반환 시 `phase`가 `idle`로 복귀하고 notice가 세워진다. `grep -c devLogin src/App.tsx` = 0.
  - [non-goal] `GoogleClientId`가 비어도 **앱이 크래시하지 않고 게스트 촬영이 그대로 동작**한다(기존 873 테스트 무변경). [닫기]는 미구성·오류·리디렉트 중 **어느 상태에서도 사라지지 않는다**.
  - [trigger] 리디렉트는 **[Google로 로그인] 탭에서만** 일어난다 — 화면 진입·notice 표시·[닫기]로는 어떤 네트워크·저장소 부수효과도 없다.
- **롤백**: `App.tsx`의 2줄(case 추가·devLogin 삭제)을 되돌리고 신규 4파일 삭제.
- [ ] 완료

### Step 12-6: 401 → 세션 해제 배선 (C10)
- **Context Brief**: JWT는 8시간 만료인데 **만료를 감지해 세션을 푸는 코드가 지금 없다**(`backendClient`는 401을 던지기만 한다). 로그인 표시가 남으면 운영자가 "QR이 되는 상태"로 오인한다. 규격은 `docs/web-client/07 §4.3`·`12 C10`·`02 §5.2`. 두 가지 함정이 있다: ① **PIN 검증의 401은 불일치이지 만료가 아니다** — 세션을 건드리면 PIN을 한 번 틀렸다고 로그아웃되는 회귀가 난다. ② `sessionStore.logout()`은 `discardCaptureData()`를 함께 하는데 **만료 시 촬영 데이터는 유지**가 규격이다(`02 §5.2` 표) → 새 `expireSession()`을 쓴다. 토큰 폐기는 **직접 하지 않는다** — `authStore`의 M1 구독이 `currentUser`가 null이 되는 것을 보고 처리한다(이미 설치돼 있다).
- **대상 파일**: `webclient/src/shell/sessionStore.ts`(수정) · `webclient/src/shell/sessionExpiry.ts`(신규) · `webclient/src/adapters/http/backendClient.ts`(수정) · `webclient/src/adapters/http/accountService.ts`(수정) · `webclient/tests/unit/auth/sessionExpiry.test.ts`(신규)
- **선행 조건**: 없음(12-1~12-5와 병렬 가능)
- **구현 내용**:
  - `sessionStore.ts`: `expireSession(): void { set({ currentUser: null }); }` 추가. 파일 상단 주석의 "진입점은 `login`/`logout`뿐"을 **"`login` / `logout` / `expireSession`"** 으로 갱신하고, `expireSession`이 촬영 데이터를 지우지 않는 이유(`02 §5.2`)를 한 줄로 남긴다.
  - `sessionExpiry.ts`: `handleSessionExpired()` — 이미 `currentUser===null`이면 **즉시 return**(멱등), 아니면 `expireSession()` + `shellStore.toast("error", STRINGS.error.sessionExpired)` + `logger.warn("세션 만료 감지(401) — 세션 해제")`.
  - `backendClient.ts`: `RequestOptions.unauthorized?: "expired" | "reject"` + `BackendClientDeps.onSessionExpired?: () => void`(기본 `handleSessionExpired`). 401 분기에서 `options.unauthorized ?? (token !== null ? "expired" : "reject")`가 `"expired"`일 때만 호출한 뒤 기존대로 throw.
  - `accountService.ts`: `verifyMyPin`에 `unauthorized: "reject"` + 주석("PIN 401은 불일치다 — 07 §4.3").
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/http tests/unit/shell tests/unit/auth`
- **완료 기준**:
  - [관측] 가짜 fetch로 401을 돌려주면 `auth:"required"` 호출에서 `currentUser`가 null이 되고 `getToken()`이 null이며(M1 경유) **`session.cuts`·`sessionId`는 그대로**다. `verifyMyPin`의 401에서는 `currentUser`가 **불변**이다. `auth:"none"`(토큰 미부착) 401에서도 **불변**이다.
  - [non-goal] `authStore.clearToken`을 직접 호출하는 제품 코드가 **늘지 않는다**(구독과 기존 1곳뿐). 기존 `backendClient.test.ts`·`shell.test.ts`가 무수정 통과한다.
  - [trigger] 세션 해제는 **401 응답을 실제로 받았고 그 요청에 Bearer가 붙어 있었을 때만** 일어난다. 403·404·500·네트워크 실패는 세션을 건드리지 않는다.
- **롤백**: 4파일의 변경을 되돌린다(신설 `sessionExpiry.ts` 삭제). 만료 미감지 상태로 회귀할 뿐 다른 기능은 무영향.
- [ ] 완료

### Step 12-7: 정적 불변식 테스트 + 개발 포트 정합
- **Context Brief**: 이 저장소는 "문서에만 있으면 언젠가 깨진다"는 전제로 **소스를 읽어 검사하는 테스트**로 불변식을 고정한다(`docs/web-client/15 §3.4`에 10건이 이미 있다). 인증에서 깨지면 치명적인 것 4가지를 같은 방식으로 고정한다. 함께, `webclient/vite.config.ts`의 dev 포트가 **5273**인데 Google Console 등록·서버 `OAUTH_REDIRECT_ALLOWLIST`·서버 테스트는 전부 **5173**이다 — 이 상태로는 로컬 개발 로그인이 **구조적으로 불가능**하다(Google이 `redirect_uri_mismatch`로 거부).
- **대상 파일**: `webclient/tests/unit/auth/authInvariants.test.ts`(신규) · `webclient/vite.config.ts`(수정)
- **선행 조건**: Step 12-2 ~ Step 12-6
- **구현 내용**:
  - `authInvariants.test.ts`: 설계 §6.2의 AUTH-1~5 + M2-a/M2-b. `readFileSync`로 소스를 읽어 정규식·문자열 검사(`purity.test.ts`가 같은 형태의 선례다).
  - `vite.config.ts`: `server: { port: 5173, strictPort: true }`. `strictPort`를 켜는 이유를 주석으로 남긴다("포트가 밀리면 등록된 리디렉트 URI와 어긋나 로그인이 조용히 실패한다").
- **검증 명령**: `cd webclient && npx vitest run tests/unit/auth/authInvariants.test.ts && cd .. && grep -rn "5273" --include=*.ts --include=*.md --include=*.json webclient docs web/functions/src | grep -v node_modules` (마지막 grep은 **0건**이어야 한다)
- **완료 기준**:
  - [관측] 정적 테스트 7건이 통과한다. `sessionStorage`를 다른 파일에 한 줄 넣으면 M2-a가, `clientKind:"web"`을 지우면 AUTH-2가, `prompt=select_account`를 지우면 AUTH-5가 **실패한다**(각 1건씩 일시 수정으로 확인 후 되돌린다).
  - [non-goal] `web/firebase.json`은 **무변경**이다(kiosk rewrite·`/oauth2callback` 헤더가 이미 있다 — `firebase.json:35,57-60`). `hosting:default`(P1 다운로드 페이지)에 **어떤 변경도 없다**.
  - [trigger] 포트 변경은 `npm run dev`에만 영향을 준다 — `npm run build` 산출물(`web/kiosk/`)과 배포 경로는 동일하다.
- **롤백**: 신규 테스트 파일 삭제 + `vite.config.ts` 포트 되돌리기.
- [ ] 완료

### Step 12-8: 문서 갱신 · 실측 항목 등재 · 전체 검증
- **Context Brief**: 이 저장소의 관례상 Step을 끝내면 **`docs/web-client/11-wbs.md`의 해당 체크박스에 산출물·검증 수치·설계 이탈·남은 실측을 적고**, 사람이 해야 하는 일은 `14`에 절차까지 적는다(`15 §5`). 다른 Step의 서술을 stale로 만들지 않아야 한다. 이번 Step에서 규격 문서 자체를 2곳 정정한다(URL 스크럽 시점, 세션 만료 문구).
- **대상 파일**: `docs/web-client/11-wbs.md`(Step 12 절) · `docs/web-client/15-implementation-conventions.md`(§6 Step 12 절·§7 상태표) · `docs/web-client/14-handoff-and-user-actions.md`(§10.6·§10.7 신설) · `docs/web-client/07-auth-and-permissions-web.md`(§2.2 h 위치·§4.3 문구) · `docs/web-client/12-web-vs-windows-differences.md`(C10 문구 일치 확인) · `docs/design/README.md`(§3.1 2곳)
- **선행 조건**: Step 12-1 ~ Step 12-7
- **구현 내용**:
  - `11-wbs.md` Step 12: 체크박스 `[x]` + 산출물 파일 목록 + 테스트 수치(신규 N건 → 총계) + **설계 이탈 4건**(§2) + 남은 실측 V21.
  - `15 §6`의 "Step 12 인증" 절을 **완료 형태**로 바꾼다(Step 9~11 절과 같은 서술 형식: "완료(2026-07-31). 뒤 Step이 알아야 할 것만 남긴다"). 뒤 Step이 알아야 할 것: ① `sessionStore.expireSession()`이 생겼고 촬영 데이터를 지우지 않는다 ② PIN 검증은 `unauthorized:"reject"`를 반드시 넘긴다(Step 13) ③ `sessionStorage`는 `oauthStateStore.ts` 전용이다 ④ `sessionStore.login()` 호출은 콜백 러너 1곳뿐이라는 정적 테스트가 있다 ⑤ `Login` 화면이 실물이 됐다.
  - `15 §7` 상태표: 완료에 **12** 추가, 테스트 수치 갱신, "남은 더미는 Step 13·15·16이 채운다"로 문장 조정(**Step 12를 stale하게 남기지 않는다**).
  - `14 §10.7` 신설(V21) — 아래 §9 표 그대로. `§10.6`의 "폰으로 QR 스캔(V20-4) — 선행: Step 12"를 **"지금 가능"** 으로 갱신.
  - `07 §2.2`: 절차 h(`history.replaceState`)를 e 앞으로 옮기고 이유 한 줄(§2.4). `07 §4.3`·`12 C10`의 만료 문구가 `STRINGS.error.sessionExpired`와 문자 단위로 같은지 확인.
  - `docs/design/README.md`: §0 표에 "웹 클라이언트(키오스크)의 로그인·JWT를 바꾼다 → 이 문서" 행 추가, §3.1 목록 표에 Step 9/10/11과 같은 형식으로 이 문서 등재.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run` · `cd web/functions && npm test` · `cd ../.. && dotnet test tests/MCPhoto.Tests`
- **완료 기준**:
  - [관측] 세 스위트가 전부 녹색이고 웹 테스트 수가 873 → 증가한 값으로 `11-wbs`·`15 §7`에 **같은 숫자**로 기록된다. `docs/design/README.md`에서 이 문서가 **2곳**(§0 안내 표 + §3.1 목록 표)에 등재된다.
  - [non-goal] 서버(316)·Windows(938) 수치는 **변하지 않는다**(이번 Step은 서버·WPF 코드를 건드리지 않는다). `docs/spec-vectors/`에 **파일이 추가되지 않는다**(§6.4).
  - [trigger] 문서 갱신은 Step 12-1~12-7이 전부 녹색인 뒤에만 한다 — 미완 상태를 완료로 기록하지 않는다.
- **롤백**: 문서 커밋만 되돌린다(코드 무영향).
- [ ] 완료

---

## 9. 남는 사용자 액션 (실측 · `14 §10.7`로 등재)

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| **V21-1** | 실제 Google 계정으로 **로그인 완주** | 상단 계정 라벨이 계정 id로 바뀌고 **직전 화면으로 복귀**한다. 주소창에 `code`·`state`가 **없다** | 실 Google 인증·실계정 필요 |
| **V21-2** | 배포본(kiosk)에서 **CSP 위반 0건** + 리디렉트가 막히지 않는다 | 콘솔에 CSP 오류 없음. `accounts.google.com`으로 이동 성공 | 배포 헤더가 붙은 환경이 필요(로컬 dev엔 CSP가 없다) |
| **V21-3** | **`prompt=select_account`가 실제로 계정 선택 화면을 띄운다** | 직전 손님 계정으로 자동 로그인되지 않고 **매번 계정 선택**이 뜬다 | 브라우저에 Google 세션이 남은 상태를 만들어야 한다 |
| **V21-4** | **새로고침하면 게스트로 돌아간다**(C6) + DevTools Application에서 **토큰 문자열 검색 0건**(E4) | localStorage·sessionStorage·IndexedDB·쿠키 전부 0건. `mcphoto.oauth.pending.v1`도 콜백 후 **없다** | 브라우저 저장소 관측 |
| **V21-5** | **PIN 1회 오입력이 로그아웃을 유발하지 않는다**(E17) | 계정 라벨 유지 | Step 13(PIN 모달) 이후 가능 → **Step 13 실측으로 이월** |
| **V21-6** | **로그인 상태에서 QR 완주 → 폰 스캔**(V20-4 선행 해소) | P1 다운로드 페이지가 열린다 | 물리 폰 필요 |
| **V21-7** | `firebaseapp.com` 도메인으로 접속해도 로그인이 된다 | 두 도메인 모두 성공(A1에 둘 다 등록돼 있다) | 도메인별 접속 필요 |

---

## 10. 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F32) / 미검증 가정(A1~A6) 목록이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다(A1→V21-1 · A2→V21-2 · A3→S12-2/V21-3 · A4→S12-3/V21-1 · A5→S12-7 · A6→S12-4)
- [x] 8개 단계 전부에 7개 필수 필드가 채워져 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이다(UI 단계 12-4·12-5는 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능한 CLI다
- [x] 부수효과(sessionStorage·history·리디렉트·구독)에 해제 경로가 명시돼 있다(§5)
- [x] 비동기 흐름에 오류·취소 경로가 있다(교환 실패 5종 + 100초 타임아웃)
- [x] 보안: 토큰 메모리 전용(M2) 유지 · 로그 마스킹(§4.6) · `innerHTML` 미사용 · CSP 영향 검토(§7.3)
