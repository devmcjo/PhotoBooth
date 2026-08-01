# web-fix (2026-08-01) · 로그인 실패 · 전체화면 진입 · 카메라 권한 UX

| 항목 | 값 |
|------|-----|
| 문서 | 사용자 실사용 이슈 **①(로그인)·③(전체화면)·④(카메라 권한)** 의 진단 결과 + 구현 설계 |
| 이슈 ②(Windows 디자인 정합) | 별 문서 → [web-fix-20260801-windows-visual-parity](./web-fix-20260801-windows-visual-parity.md) |
| 작성 | `js-architect` (2026-08-01) |
| 다음 단계 | `js-developer` → `js-code-reviewer` |
| 대상 저장소 | `E:\Study\photobooth` · 브랜치 `feature/web-client-foundation` |
| 전제 문서 | [`docs/web-client/15`](../web-client/15-implementation-conventions.md)(불변식 50+) · [`11`](../web-client/11-wbs.md) · [`README`](../web-client/README.md) |

---

## 0. 계획 헤더 — 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (verified facts)

| # | 사실 | 근거 |
|---|------|------|
| **V-1** | **배포된 서버가 Google code 교환에서 `invalid_client`로 실패하고 있다.** 사용자의 실제 시도 시각에 정확히 남았다 | `firebase functions:log --project mcphoto-955fb` 출력<br>`2026-08-01T05:27:50.167768Z ? api: Google 로그인 검증 실패: code 교환 실패: invalid_client` (= 2026-08-01 14:27 KST) |
| **V-2** | **웹 OAuth client_id가 배포 env에 플레이스홀더 문자열 그대로 들어가 있다** | `web/functions/.env.mcphoto-955fb:7` → `GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>` (문서 [`14 §3`](../web-client/14-handoff-and-user-actions.md) 113행의 예시 명령을 치환 없이 실행한 결과와 문자 단위로 일치) |
| **V-3** | 실제 웹 client_id는 클라이언트 쪽에만 들어가 있다 | `webclient/.env.production.local:2` → `VITE_GOOGLE_CLIENT_ID=712395684881-s3u9fh10bgugb38kagu81u2in2f5hd1h.apps.googleusercontent.com` |
| **V-4** | 그 env 파일이 실제로 배포에 실렸다 | 파일 mtime `2026-07-31 14:43` → 배포 audit log `UpdateFunction`(update_mask에 `service_config.environment_variables` 포함) `2026-07-31T06:03:02Z`(=15:03 KST) · `lib/build-stamp.json` `deployedAt: 2026-07-31T06:04:12.404Z` |
| **V-5** | `GOOGLE_OAUTH_CLIENT_SECRET_WEB` 시크릿은 **등록돼 있다**(version 1). 즉 `googleOAuthClients.web`이 **활성**이고 요청은 501이 아니라 code 교환까지 간다 | 배포 audit log의 `secretEnvironmentVariables`에 `{"secret":"GOOGLE_OAUTH_CLIENT_SECRET_WEB","version":"1"}` |
| **V-6** | `redirectUri` 허용 목록은 정상이다 | `.env.mcphoto-955fb:8` — kiosk 2도메인 + `http://localhost:5173/oauth2callback`. 또한 `validateRedirectUri` 실패였다면 code 교환 **이전에** 400이므로 V-1 로그가 남지 않았을 것이다(`web/functions/src/routes/auth.ts:53-57` → `:78` 순서) |
| **V-7** | 클라이언트는 `location.assign`으로 authorize URL에 이동한다 → kiosk CSP의 `form-action 'none'`에 걸리지 않는다 | `webclient/src/adapters/auth/googleSignIn.ts:63,121` · `web/firebase.json:43` |
| **V-8** | 첫 제스처(`pointerdown`/`keydown`, once)가 전체화면을 요청한다 | `webclient/src/App.tsx:289-310`(`installFirstGestureHandlers`) ← `webclient/src/main.tsx:68-76`(`installFirstGesture`) |
| **V-9** | 같은 첫 제스처가 **Wake Lock도** 요청한다. 오디오 unlock은 여기가 **아니라** `Guide`의 [촬영 시작]에 있다 | `main.tsx:71-75`(`requestWakeLock`) · `FlowViews.tsx:88-89`(`unlockAudio()` + `requestWakeLock()`) |
| **V-10** | 전체화면 이탈 배너는 `fullscreenchange` 구독으로만 켜진다 — **한 번도 전체화면이 아니었으면 뜨지 않는다** | `shell/fullscreenController.ts:75-84` |
| **V-11** | 카메라 실패는 **사유 구분 없이 한 문구**다 | `cameraService.ts:252` → `setState("Failed", "카메라를 열 수 없습니다.")` · `CameraPreview.tsx:70-74` → `STRINGS.camera.failed` = "카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요."(`strings.ts:206`) |
| **V-12** | `NotFoundError`/`OverconstrainedError`는 `{audio:false, video:true}`로 **1회 재시도**한다. `NotAllowedError`는 재시도하지 않는다 | `cameraService.ts:188-207` |
| **V-13** | **진단 모달에 카메라 권한 행이 이미 있다**(granted/denied/prompt/알 수 없음 + tone) | `diagnosticsPresenter.ts:93-104,142` · 조회는 `DiagnosticsModal.tsx:44-63`의 **파일 로컬** `readCameraPermission()` |
| **V-14** | 카메라는 `Capture` 화면 마운트에서 열린다. 권한 프롬프트는 그때 뜬다 | `screens/capture/useCaptureRunner.ts:75,93` |
| **V-15** | 권한 프롬프트가 8초 Ready 타임아웃을 잡아먹지는 **않는다**(`readyTimer`는 스트림 획득 **후** 설치된다) | `cameraService.ts:250-256` → `:295` |
| **V-16** | 규격은 이미 **권한 거부 전용 안내 + [다시 시도]** 를 요구한다 — 구현이 규격에 미달인 상태다 | [`03 §6.3`](../web-client/03-screens-spec.md) 265행 · [`12 C5`](../web-client/12-web-vs-windows-differences.md) |
| **V-17** | 상단바는 `Capture`·`Qr` 두 화면에서만 숨는다 | `domain/navigation/stateMachine.ts:65-67` |
| **V-18** | `isStandaloneDisplay()`가 `display-mode: standalone|fullscreen`과 iOS `navigator.standalone`을 이미 판정한다 | `adapters/platform/appInstall.ts` |
| **V-19** | `analysis/31`의 501 의미는 현재 **"Google SSO 미구성"만**으로 좁게 적혀 있다 | `docs/analysis/31-backend-api-reference.md:85,197` |

### 0.2 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| **A-1** | Secret Manager의 `GOOGLE_OAUTH_CLIENT_SECRET_WEB`(version 1) 값이 **A1에서 만든 웹 클라이언트의 실제 secret**이다(플레이스홀더가 아니다). 값 조회가 도구 정책으로 차단돼 확인하지 못했다 | **Step F1** — 사용자가 재등록·재배포하며 확인한다 |
| **A-2** | Google Cloud Console의 웹 OAuth 클라이언트에 승인된 리디렉션 URI 3개가 `.env.mcphoto-955fb:8`의 CSV와 **문자 단위로 일치**한다 | **Step F1** |
| **A-3** | `document.documentElement.requestFullscreen`이 대상 기기(태블릿 Safari 포함)에서 존재/미존재로 갈린다 — 미존재 시 버튼을 숨겨야 한다 | **Step F5**(런타임 감지로 구현하므로 코드로 닫힌다) + 실측 V26-2 |
| **A-4** | `navigator.permissions.query({name:"camera"})`가 Safari에서 throw 또는 미지원이다(→ `null`) | **Step F6**(폴백 경로를 구현으로 닫는다) + 실측 V26-3 |
| **A-5** | 짧게 열었다 즉시 `stop()`하는 권한 프라이밍 스트림이 이후 `cameraService.start()`를 방해하지 않는다 | **Step F7** + 실측 V26-4 |

---

## 1. 설계 규범 (developer가 반드시 지킬 것)

이 문서의 모든 단계에 적용된다. [`15 §2·§3.4`](../web-client/15-implementation-conventions.md)의 재확인이다.

| 규범 | 내용 |
|------|------|
| 계층 | `ui → screens → shell → domain ← adapters`. **도메인은 아무것도 import하지 않는다**(`tests/unit/domain/purity.test.ts`가 강제) |
| 로깅 | **`console.*` 금지 · `logger.*`만**. Worker/SW는 `logger`도 금지(도달하지 않는다) |
| 로그 키 | `code`·`state`·`nonce`·`token`·`pin`·`codeVerifier`는 **마스킹된다** → 진단값은 `errorCode`처럼 이름을 구분한다([15 §4 함정 1]) |
| 어댑터 | **예외를 전파하지 않는다.** `false`/`null`/판별 유니온을 돌려주고 상위가 상태로 표현한다 |
| 순수 판정 | 분기 규칙은 **도메인 순수 함수**가 소유한다. 화면이 문자열을 비교하지 않는다(ACC-1 정신) |
| 주석 | **한국어**. "왜"를 남긴다 — 특히 되돌리기 쉬운 결정 |
| StrictMode | effect가 2회 돈다. **cleanup에서 취소하는 형태를 만들지 마라**([15 §6] Step 12·13 함정) |
| 리소스 | 모든 `addEventListener`·`MediaStream`·타이머에 **해제 경로**를 함께 설계한다 |
| 새 불변식 | 만들면 **정적 테스트로 고정한다**(이 저장소의 관례). 이 문서는 `CAM-1`·`FS-1` 2건을 신설한다 |

### 1.1 진실원 판정 (충돌 시)

**실제 소스 > `docs/analysis` > `docs/design`** ([`design/README §4`](./README.md)).
단 [메모리 규칙](../../.claude/agent-memory/js-architect/truth-source-judgment.md)대로 **"실행된 적 있는 코드"에만** 적용한다 — 호출자 0인 헬퍼나 미구현 요구사항은 규격이 이긴다.

이 문서에서 실제로 적용한 판정 3건:

| # | 충돌 | 판정 | 왜 |
|---|------|------|-----|
| J-1 | 팀 리드 지시문은 카메라 실패 문구를 "카메라를 찾을 수 없습니다"로 적었으나, 소스는 **"카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요."** 다 | **소스가 사실** | `strings.ts:206`. 설계·리뷰가 같은 문자열을 봐야 한다 |
| J-2 | [`03 §6.3`](../web-client/03-screens-spec.md)은 권한 거부 전용 안내 + [다시 시도]를 요구하지만 **구현에 없다** | **규격이 이긴다 → 구현이 결함이다** | 이 요구사항은 "실행된 적 있는 코드"가 아니라 **미구현 요구사항**이다. 소스 우선 규칙의 적용 대상이 아니다 |
| J-3 | [`03 §2`](../web-client/03-screens-spec.md) 86행은 Home에서 "권한 요청은 여기서 하지 않는다"고 못박는다. 이번 이슈 ④는 **사전 요청 버튼**을 요구한다 | **규격을 고친다**(코드가 아니라 문서를 먼저) | 규격 변경이므로 [`web-client/README §3`](../web-client/README.md)의 갱신 규칙을 따른다. 단 위치를 **`Guide`** 로 옮겨 Home의 CTA 1개 규격은 보존한다(§4.3) |

---

## 2. 이슈 ① — Windows 앱으로 가입한 계정이 웹에서 로그인되지 않는다

### 2.1 결론 — **원인을 특정했다**

> **웹 OAuth client_id가 배포 서버에 플레이스홀더 문자열(`<A1의 웹 client_id>`)로 들어가 있어, 서버가 Google 토큰 엔드포인트에서 `invalid_client`로 거부당한다. 이것은 계정 문제가 아니라 서버 구성 오류이며, "특정 계정"이 아니라 웹 로그인 시도 100%가 실패한다.**

근거는 §0.1의 **V-1 ~ V-6**. 특히 V-1의 서버 로그가 결정적이다 — 추정이 아니라 **관측된 실패 사유**다.

### 2.2 후보를 어떻게 배제했는가

라우트 `web/functions/src/routes/auth.ts`는 **순서가 곧 진단 도구**다. 어디까지 갔는지가 응답으로 드러난다.

```
requireApiKey ──✗→ 401 (게이트 키)
  ↓ 통과
googleOAuthEnabled ──✗→ 501
  ↓
validateAuthCode / validateCodeVerifier / validateClientKind ──✗→ 400
  ↓
validateRedirectUri(allowlist) ──✗→ 400          ← ①redirectUri 후보는 여기서 죽는다
  ↓
googleOAuthClients["web"] 부재 ──✗→ 501          ← ②"web 미구성" 후보는 여기서 죽는다
  ↓
verifyGoogleCodeAndGetEmail ──✗→ 401(일반화)     ← ★ 실제로 여기까지 왔다 (V-1)
  ↓                                                  로그: "code 교환 실패: invalid_client"
loginWithGoogleEmail ──✗→ 401(일반화)            ← ③doc.email 방어 · ④findByEmailField
  ↓                                                  → **도달조차 하지 않았다**
JWT 발급 200
```

| 후보 | 판정 | 근거 |
|------|------|------|
| **`loginExistingGoogleAccount`의 `(doc.email ?? null) !== normalized` 방어** | **무죄 · 도달하지 않음** | 이 코드는 `verifyGoogleCodeAndGetEmail` **성공 후**에만 실행된다(`auth.ts:105`). code 교환이 `invalid_client`로 죽었으므로 실행된 적이 없다 |
| **`findByEmailField`의 비결정성**(인덱스·복수 매칭) | **무죄 · 도달하지 않음** | 동상 |
| **웹 client_id가 `GOOGLE_OAUTH_AUDIENCES`에 없다 / 배포 안 됨** | **유죄 (원인)** | `googleOAuthAudiences`는 `Object.values(googleOAuthClients).map(c => c.clientId)`(config.ts:157)이므로 **audience 목록에도 플레이스홀더가 들어가 있다.** 다만 실제로 터진 지점은 audience 검증이 아니라 그 **앞의 code 교환**이다 |
| **`redirectUri` 허용목록** | **무죄** | V-6. 걸렸다면 400이고 V-1 로그가 없다 |
| **kiosk CSP(`form-action 'none'` / `connect-src`에 accounts.google.com 없음)** | **무죄** | V-7. 최상위 내비게이션은 `form-action`·`connect-src`의 대상이 아니다. 게다가 콜백이 서버까지 도달했다는 것이 곧 반증이다 |
| **웹 게이트 키(`VITE_BACKEND_API_KEY`) 불일치** | **무죄** | 걸렸다면 `requireApiKey`에서 401이고 code 교환 로그가 없다 |

### 2.3 왜 사용자에게는 "Windows로 가입한 계정이 안 된다"로 보였나

서버가 `GoogleAuthError`를 **일반화 401**로 바꾸면서 붙이는 문구가

> "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요."

이고(`auth.ts:96-98`), 클라이언트가 이를 `rejected`로 매핑해 **같은 문구를 그대로** 보여준다(`loginFailure.ts` → `strings.ts:47-48`).
즉 **서버 구성 오류가 계정 탓으로 표시**됐다. 이 오귀인이 이슈 ①의 절반이다 — §2.5에서 코드로 고친다.

### 2.4 수정 방향 판정 — 코드 vs 데이터

| 축 | 판정 | 근거 |
|----|------|------|
| **데이터 보정(Firestore `users.email` 채우기)** | **불필요** | 원인이 아니다. 로그인이 그 코드에 도달하지 못한다 |
| **`(doc.email ?? null) !== normalized` 방어 완화** | **하지 않는다 (금지)** | 팀 리드 지시대로다. 이 방어는 **계정 탈취 방지**다 — `email` 필드가 없는 문서가 검증된 Google email로 로그인되면, 문서 id만 아는 사람이 남의 계정에 들어갈 통로가 열린다. 게다가 원인이 아니므로 완화해도 증상이 낫지 않는다 |
| **구성(env) 교정** | **✅ 본 원인 제거** | Step **F1** — 사용자 액션 |
| **코드 수정** | **✅ 재발·오귀인 방지** | Step **F2**(사유 구분) · **F3**(플레이스홀더 배포 차단) · **F4**(클라 진단 흔적) |

> ⚠️ **`(doc.email ?? null) !== normalized`를 건드리지 마라.** 이번 작업의 명시적 비목표다.
> 만약 F1 이후에도 특정 계정만 401이 남으면 그때 §2.6의 절차 D로 별건 진단한다.

### 2.5 코드 설계 — 사유를 구분한다 (Step F2 · F4)

#### F2. 서버: OAuth **클라이언트 자격 오류**를 401에서 분리한다

401 일반화의 목적은 **계정 열거 방지**(설계 §6.4)다. 그런데 Google 토큰 엔드포인트가 돌려주는
`invalid_client` / `unauthorized_client`는 **어느 계정이 존재하는지와 무관**하다 — 우리 서버의 client_id/secret이 틀렸다는 뜻뿐이다.
따라서 이 둘만 분리해도 **열거 방어는 한 톨도 약해지지 않는다.**

```
GoogleAuthError 에 kind 추가:  "clientConfig" | "rejected"
  ├ getToken 실패 메시지에 invalid_client | unauthorized_client 포함 → kind:"clientConfig"
  └ 그 외 전부(invalid_grant · 만료 code · nonce 불일치 · hd 불일치 · email 미검증 …) → kind:"rejected"

routes/auth.ts:
  kind === "clientConfig" → HttpError.notImplemented(
      "Google 로그인이 구성되지 않았습니다.")        ← 501
  kind === "rejected"     → 기존 401 문구 그대로     ← 변경 없음
```

**왜 501을 재사용하나 (새 상태코드를 만들지 않는 이유)**

- 클라이언트에 **이미 501 → `notConfigured` → "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." 경로가 배선돼 있다**(`googleSignIn.ts:146` · `strings.ts:49`). 새 코드를 만들면 3계층(서버 매핑·클라 분류·문구)을 모두 늘려야 한다.
- 의미도 맞다: **운영자가 고쳐야 하는 구성 문제**다.
- `analysis/31 §197`이 이미 501을 "요청한 `clientKind`가 미구성"에 쓰고 있어 **같은 축**이다.

⚠️ **`invalid_grant`를 여기 넣지 마라.** 만료·재사용된 code에서도 나오며 그것은 손님 흐름의 문제(재시도로 해결)다. 구성 오류로 표시하면 운영자가 없는 문제를 찾는다.

#### F4. 클라이언트: 사유가 **화면과 진단 양쪽**에 남게 한다

| 경로 | 지금 | 바꿀 것 |
|------|------|---------|
| 화면 문구 | 501 → "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." | **변경 없음**(이미 옳다). F2가 서버 매핑만 고치면 저절로 맞는다 |
| 로그 | 400에만 `logger.error`가 있고 **501·401에는 없다**(`googleSignIn.ts:145-161`) | 501·401 각각에 `logger.error`/`logger.warn` 추가. 키는 `status`·`errorCode`(⚠️ `code`가 아니다 — 마스킹된다) |
| 진단 모달 | 로그인 실패 흔적이 **전혀 없다** | 서버 섹션에 **[마지막 로그인 실패]** 행 1개 추가 |

**[마지막 로그인 실패] 행의 데이터 경로**

```
shell/loginStore.ts (기존 파일)
  └ lastFailure: { reason: LoginFailureReason; at: number } | null   ← 메모리 전용, 새로고침에 사라짐
      ▲ applyOauthCallbackOutcome(실패) · runSignIn(실패) 가 기록
      ▼
screens/modals/diagnostics/diagnosticsPresenter.ts
  └ deps.lastLoginFailure: () => { reason; at } | null
      → buildServerSection 마지막 행:  "마지막 로그인 실패" / "구성 오류 · 14:27" / tone "bad"
        없으면 "없음" / tone "ok"
```

- 값은 **열거형 사유 + 시각**뿐이다. email·token·code를 담지 않는다(AUTH-3 정신).
- 사유 → 표시 문구 매핑은 **도메인 순수 함수** `describeLoginFailure(reason)`으로 `domain/auth/loginFailure.ts`에 둔다(화면이 문자열을 비교하지 않는다).
- ⚠️ `loginStore`는 이미 있는 셸 스토어다. **새 스토어를 만들지 마라.**

### 2.6 진단 절차 — 원인이 이미 특정됐으므로 "확인 절차"다 (3분)

> F1을 수행하기 **전에** 아래 A를 돌려 현재 상태를 눈으로 확인하면, 고친 뒤 B에서 사라지는 것을 볼 수 있다.

**절차 A — 지금 무엇이 실패하는지 본다 (30초 · 서버 로그 접근 가능)**

```powershell
cd E:\Study\photobooth\web
firebase functions:log --project mcphoto-955fb | Select-String "Google 로그인"
```

| 보이는 것 | 뜻 |
|-----------|-----|
| `code 교환 실패: invalid_client` | **이번 원인.** client_id 또는 secret이 Google에 등록된 값과 다르다 → F1 |
| `code 교환 실패: invalid_grant` | code 만료·재사용. 구성 문제가 아니다 → 다시 로그인해 보라 |
| `id_token audience 불일치` | 교환은 됐는데 audience 목록에 그 client_id가 없다 → `GOOGLE_OAUTH_CLIENT_ID_WEB` 값 확인 |
| `허용되지 않은 hosted domain` | `GOOGLE_ALLOWED_HD` 설정이 계정 도메인과 다르다 |
| `계정 자동 생성/매핑 실패(경합 또는 방어값)` | **여기가 나와야** `doc.email` 방어·경합 후보가 살아난다 → 절차 D |
| 아무것도 없다 | 요청이 서버에 닿지 못했다 → 절차 C |

**절차 B — 서버 로그를 볼 수 없을 때 (2분 · 브라우저만으로)**

배포본(`https://mcphoto-955fb-kiosk.web.app`)에서 [로그인] → Google 계정 선택 → 돌아온 뒤 화면 문구로 갈린다.
**F2 적용 후**에는 이 표만으로 원인이 갈린다(적용 전에는 위 3행이 전부 "계정" 문구로 뭉쳐 보인다).

| 화면 문구 | 원인 | 다음 조치 |
|-----------|------|-----------|
| "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." | 서버 OAuth 구성 오류(**이번 건**) 또는 web 클라이언트 미구성 | F1 |
| "이 Google 계정으로는 로그인할 수 없습니다…" | **진짜 계정·도메인 거부**(hd 불일치 · email 미검증 · 경합 · `doc.email` 방어) | 절차 D |
| "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요." | 네트워크 · **또는 서버 400(redirectUri 거부)** | 절차 C |
| "Google 로그인이 취소되었습니다." | state 불일치 · code 없음 · 3분 초과 | 다시 시도. 반복되면 `sessionStorage` 차단(프라이빗 모드) 의심 |
| 로그인 버튼 자체가 없다 | `VITE_GOOGLE_CLIENT_ID` 빈 값으로 빌드됨 | `webclient/.env.production.local` 확인 후 재빌드·재배포 |

보조 확인: **설정 → 진단·상태 → [서버] 섹션의 [마지막 로그인 실패] 행**(F4로 신설). 사유가 그대로 남는다.
그 아래 [로그 내보내기]를 누르면 `errorCode`가 포함된 `.log`가 떨어진다.

**절차 C — "네트워크" 문구가 나올 때 (1분)**

DevTools → Network → `auth/google` 요청의 상태코드를 본다.
`400` = redirectUri 거부 → 주소창의 오리진(`https://…kiosk.web.app` vs `…firebaseapp.com` vs `http://localhost:5173`)이
`OAUTH_REDIRECT_ALLOWLIST` CSV에 **문자 단위로** 있는지 확인. 로컬은 **포트 5173 고정**이다(`vite.config.ts` `strictPort:true`).

**절차 D — F1 이후에도 특정 계정만 401일 때 (별건)**

이때 비로소 `doc.email` 방어·`findByEmailField`가 후보가 된다. 순서대로:
1. 절차 A로 `계정 자동 생성/매핑 실패` 로그가 실제로 나오는지 확인한다(안 나오면 다른 원인이다).
2. Firebase 콘솔 → Firestore → `users`에서 그 계정 문서를 연다.
3. `email` 필드가 **없거나** 대문자·앞뒤 공백이 섞였는지 본다.
4. 있으면 **그 문서의 `email`을 소문자·트림해 채우는 것이 정답**이다(코드 완화가 아니라). 서버 코드는 그대로 둔다.

### 2.7 재발 방지 — 플레이스홀더가 배포되는 것을 막는다 (Step F3)

이번 사고의 실제 메커니즘은 "문서의 예시 명령을 치환 없이 실행 → 배포 → 조용히 401"이다.
**같은 실수를 기계가 잡게 한다.**

`web/functions/scripts/check-env-placeholders.mjs`(신설)가 `.env`·`.env.<project>`를 읽어
값에 `<` 또는 `>`가 있거나 값이 비어 있는 필수 키가 있으면 **비영 종료**한다.
`deploy-web.bat`의 `[2/3] Building functions` 직후에 호출해 **배포 전에** 멈춘다.

> `.env.*`는 gitignore이므로 이 검사는 로컬 파일만 본다. 그래도 이번 경로(로컬 파일 → firebase deploy)를 정확히 덮는다.

### 2.8 규격 문서 갱신 (Step F8에서 함께)

[`web-client/README §3`](../web-client/README.md)의 규칙대로 **`docs/analysis`를 먼저** 고친다.

| 문서 | 고칠 것 |
|------|---------|
| `docs/analysis/31-backend-api-reference.md:85` | `not_implemented` 501 설명을 "서버 기능 미구성 (현재는 **Google SSO 미구성**만)" → "**Google SSO 미구성 또는 OAuth 클라이언트 자격 오류**(client_id/secret이 Google에 등록된 값과 불일치)" |
| `docs/analysis/31-backend-api-reference.md:197` | 501 행에 "**Google이 `invalid_client`/`unauthorized_client`로 code 교환을 거부**(운영자 구성 오류 — 계정 열거와 무관하므로 401로 감추지 않는다)" 추가 |
| `docs/analysis/61-auth-platform-integration.md:312` 부근 | 같은 취지 1행 |
| `docs/web-client/07-auth-and-permissions-web.md` | 오류 매핑 표에 위 501 확장 반영 |
| `docs/web-client/14-handoff-and-user-actions.md` §3(A2) | 113행의 예시 명령에 **"⚠️ `<…>` 를 실제 값으로 치환하라. 치환하지 않으면 배포는 성공하지만 웹 로그인이 100% `invalid_client`로 실패한다(2026-08-01 실제 발생)"** 경고 추가. A1~A5 "완료" 표기 옆에 **A2 재수행 필요** 주석 |
| `docs/web-client/15-implementation-conventions.md` §4(함정) | **함정 17** 신설: "문서의 예시 명령을 치환 없이 실행해 `<플레이스홀더>`가 배포 env에 실렸다. 배포는 성공하고 로그인만 조용히 401이 됐다 → 배포 전 플레이스홀더 검사를 스크립트로 고정" |

### 2.9 서버 테스트 — 무엇을 갱신하나

`cd web/functions && npm test` (기준선 **316건**, jest — ⚠️ vitest의 `expect(actual, msg)`가 통하지 않는다, [15 §4 함정 10])

| 파일 | 추가/수정 |
|------|-----------|
| `src/__tests__/googleAuth.test.ts` | **추가 4건** — ① `getToken`이 `invalid_client` 메시지로 throw → `GoogleAuthError.kind === "clientConfig"` ② `unauthorized_client` → 동상 ③ `invalid_grant` → `kind === "rejected"` ④ 기존 "code 교환 실패(getToken throws) → GoogleAuthError"(179행대)는 **kind 단언만 추가**하고 의미를 바꾸지 않는다 |
| `src/__tests__/webOAuth.test.ts` | **추가 2건** — 라우트 레벨: `clientConfig` → **501** 응답 · `rejected` → **401** 응답(기존 문구 유지). ⚠️ 기존 26건은 한 건도 바뀌지 않아야 한다(회귀 확인) |
| `src/__tests__/googleOnlyAccounts.test.ts` | **변경 없음.** `doc.email` 방어를 건드리지 않으므로 |
| 신설 `src/__tests__/envPlaceholder.test.ts` | Step F3 검사기의 순수 함수(`findPlaceholderKeys(text)`)에 대해 4건: 플레이스홀더 검출 · 정상 통과 · 주석 줄 무시 · 빈 필수값 검출 |

**기대 총계: 316 → 326건.**

---

## 3. 이슈 ③ — 아무 데나 터치하면 전체화면이 된다 → 버튼으로만

### 3.1 진단

`main.tsx:68-76`의 `installFirstGesture()`가 첫 `pointerdown`/`keydown`에서 **전체화면 + Wake Lock**을 한꺼번에 요청한다(V-8·V-9).
손님이 화면 어디를 만지든 즉시 전체화면으로 들어가고, 이는 손님 입장에서 **원인 없는 상태 변화**다.

### 3.2 설계 판정

| 판정 | 내용 | 왜 |
|------|------|-----|
| **P3-1** | 첫 제스처에서 **전체화면 요청만 제거**한다 | Wake Lock은 그대로 유지해야 한다(V-9). 화면 꺼짐 회귀 금지 |
| **P3-2** | `installFirstGestureHandlers`(App.tsx) **자체는 남긴다** | 콜백을 받는 범용 함수다. 지우면 Wake Lock 배선이 갈 곳을 잃는다. 바꾸는 것은 `main.tsx`의 **콜백 내용**뿐이다 |
| **P3-3** | 오디오 unlock은 **손대지 않는다** | 이미 첫 제스처가 아니라 `Guide`의 [촬영 시작]에 있다(V-9). 셔터음 회귀 위험 없음 |
| **P3-4** | 버튼 위치는 **상단바(`TopBar`)** 다 | `Capture`·`Qr` 두 화면에서만 숨는다(V-17) — 그 두 화면은 전체화면을 새로 켤 자리가 아니다. 떠 있는 FAB로 두면 `Capture` 프리뷰를 가린다. `03`·`02` 규격에서 상단바는 "타이틀 + 계정 + 설정"이므로 **아이콘 버튼 1개 추가는 구조 변경이 아니다** |
| **P3-5** | 버튼은 **`canEnterFullscreen()`이 참일 때만** 렌더한다 | 죽은 버튼 금지. ① `requestFullscreen`이 함수가 아니면 숨긴다(iOS Safari) ② **이미 전체화면이면** 숨긴다 ③ **PWA standalone/fullscreen 표시 모드면 숨긴다**(이미 몰입 상태인데 버튼이 뜨면 오작동으로 보인다 — `isStandaloneDisplay()` 재사용, V-18) |
| **P3-6** | 기존 **이탈 배너와 중복되지 않는다** | 배너는 `fullscreenLost === true`일 때만 뜬다. 자동 진입을 없애면 **한 번도 전체화면이 아니었던 세션에서는 배너가 뜨지 않는다**(V-10) → 버튼과 배너는 **상호 배타**다. 그래도 방어적으로 `fullscreenLost`가 참인 동안에는 **상단바 버튼을 숨긴다**(배너의 [다시 전체화면으로]가 같은 일을 한다) |
| **P3-7** | 키오스크 모드와의 관계를 **운영 문서에 적는다** | Chrome `--kiosk`로 띄우면 브라우저가 이미 전체화면이라 `document.fullscreenElement`는 null이지만 화면은 꽉 차 있다. 이때 버튼은 보이되 눌러도 시각적 변화가 없다 → **"키오스크 모드로 기동하면 이 버튼을 쓸 필요가 없다"** 를 [`09 §2`](../web-client/09-kiosk-operations.md)에 명시 |

### 3.3 구현 형태

**① `shell/fullscreenController.ts` — 표면 2개 추가 (기존 동작 무변경)**

```ts
export interface FullscreenController {
  request(): Promise<boolean>;
  exit(): Promise<void>;
  isFullscreen(): boolean;
  /** ★신설: Fullscreen API가 이 문서에서 쓸 수 있는가(런타임 감지 — 타입을 믿지 않는다). */
  isSupported(): boolean;
  install(): () => void;
}
```

- `isSupported()` = `doc !== undefined && typeof doc.documentElement.requestFullscreen === "function"`.
- ⚠️ TS DOM lib은 `requestFullscreen`을 **필수 멤버로 선언**한다([15 §4 함정 2]) — 타입만 보고 있다고 판단하면 안 된다. 위 형태를 그대로 쓴다.
- `request()`·`exit()`·`install()`은 **한 글자도 바꾸지 않는다.**

**② `shell/shellStore.ts` — 전체화면 여부를 상태로 노출**

지금 `install()`의 `onChange`는 `setFullscreenLost(lost)`만 부른다. 버튼의 표시 여부를 렌더에서 알아야 하므로
같은 핸들러에서 `isFullscreen` 불리언도 함께 갱신한다.

```
shellStore:  fullscreenLost: boolean   (기존)
             isFullscreen:   boolean   (★신설 — 초기값은 controller.isFullscreen())
```

⚠️ **`fullscreenLost`의 의미를 바꾸지 마라.** "한 번 들어갔다가 나왔다"이고 배너의 유일한 조건이다.
`isFullscreen`은 "지금 전체화면인가"로 **별 축**이다. 두 값을 하나로 합치면 배너가 초기 상태에서 뜬다.

**③ `domain/navigation/fullscreenButtonPolicy.ts` — 순수 판정 (신설)**

```ts
export interface FullscreenButtonInput {
  readonly supported: boolean;      // controller.isSupported()
  readonly isFullscreen: boolean;   // shellStore.isFullscreen
  readonly fullscreenLost: boolean; // shellStore.fullscreenLost — 배너와 중복 방지
  readonly standalone: boolean;     // isStandaloneDisplay()
}
/** 상단바 [전체화면] 버튼을 렌더할 것인가. 네 조건이 전부 아니어야 보인다. */
export function isFullscreenButtonVisible(input: FullscreenButtonInput): boolean;
```

- 반환 = `supported && !isFullscreen && !fullscreenLost && !standalone`.
- **도메인에 두는 이유**: 조건이 4개다. 화면에 인라인으로 쓰면 다음에 조건을 하나 더할 때 축이 갈라진다(ACC-1 정신).
- 브라우저 API를 부르지 않는 **순수 함수**다 → `purity.test.ts`에 자동 포함된다.

**④ `ui/components/index.tsx`의 `TopBar` — props 2개 추가**

```ts
/** 참이면 [전체화면] 버튼을 렌더한다. 판정은 호출자(App)가 도메인 함수로 한다. */
readonly showFullscreen: boolean;
readonly onFullscreen: () => void;
```

- 렌더 위치: `topBarActions` 안, **계정 버튼 왼쪽**(설정·계정이라는 기존 순서를 흔들지 않는다).
- `<Button variant="ghost" aria-label={STRINGS.fullscreen.enter}>` — 아이콘만 두지 말고 **`aria-label`을 반드시** 준다.
- `STRINGS.fullscreen`에 **`enter: "전체화면"`** 1개 추가(`lost`·`reenter`는 그대로).

**⑤ `App.tsx` — 배선**

```
const showFullscreen = isFullscreenButtonVisible({
  supported: getFullscreenController().isSupported(),
  isFullscreen: useShellStore(s => s.isFullscreen),
  fullscreenLost,
  standalone: isStandaloneDisplay(),
});
…
<TopBar … showFullscreen={showFullscreen}
        onFullscreen={() => void getFullscreenController().request()} />
```

**⑥ `main.tsx` — 첫 제스처에서 전체화면 제거**

```ts
function installFirstGesture(): void {
  // 11. Wake Lock은 사용자 제스처를 요구한다. 전체화면은 **여기서 하지 않는다** —
  //     손님이 화면 아무 곳이나 만졌을 때 전체화면으로 들어가는 것은 원인 없는 상태 변화다.
  //     진입은 상단바 [전체화면] 버튼 1곳뿐이다(02 §7 · 12 C4).
  installFirstGestureHandlers(() => {
    void requestWakeLock();
    logger.info("첫 제스처 처리(WakeLock 요청)");
  });
}
```

⚠️ `getFullscreenController` import는 **남긴다** — `installShellHandlers()`의 `.install()`이 계속 쓴다.

### 3.4 신설 정적 불변식 `FS-1`

```
FS-1  `getFullscreenController().request(` 호출부가 정확히 2곳이다:
        ① App.tsx 의 이탈 배너 [다시 전체화면으로]
        ② App.tsx 의 상단바 [전체화면] 버튼
      → `main.tsx` 를 포함한 어디에도 3번째 호출이 없다.
```

- 검사: `tests/unit/shell/fullscreenInvariants.test.ts`(신설) — `src/` 전체를 읽어 주석 제거 후 grep. `authInvariants.test.ts`와 같은 형태.
- **왜**: 이 이슈는 "부르는 곳이 하나 늘어난 것"이 원인이었다. 문서로만 두면 다음 사람이 다시 `main.tsx`에 넣는다.

---

## 4. 이슈 ④ — 카메라 접근 허용 알림이 자동으로 떴으면 좋겠다

### 4.1 사실 관계 — 무엇이 가능하고 무엇이 불가능한가

| 요구 | 가능? | 근거 |
|------|-------|------|
| 페이지가 뜨자마자 **자동으로** 권한 팝업 | **불가능** | 프롬프트는 `getUserMedia()` 호출로만 뜨고, 그 호출은 **사용자 제스처(user activation)** 를 요구한다. 자동 호출은 브라우저가 조용히 거부하거나(NotAllowedError) 프롬프트 없이 실패한다 |
| **버튼 한 번**으로 촬영 전에 미리 받기 | **가능 · 이번에 만든다** | 명시적 제스처가 있으므로 정상 경로다 |
| 현재 권한 상태를 **묻지 않고 조회** | **가능(부분)** | `navigator.permissions.query({name:"camera"})`. Chromium은 지원, **Firefox는 throw**, **Safari는 미지원**(A-4) → `null` 폴백 필수 |
| `denied`를 앱이 되돌리기 | **불가능** | 브라우저 사이트 설정에서만 가능([`12 F4`](../web-client/12-web-vs-windows-differences.md)) → **복구 방법을 화면에 안내**하는 것이 앱이 할 수 있는 전부 |
| **매번 묻지 않게** 하기 | **가능 · 운영으로만** | Chrome 정책 `VideoCaptureAllowedUrls` 또는 키오스크 기동 옵션 → §4.5, [`09 §3`](../web-client/09-kiosk-operations.md) |

> 이 표를 **[`12 C5`](../web-client/12-web-vs-windows-differences.md)에도 그대로 반영**한다. "자동으로 뜨게 해 달라"는 요구는 다시 나온다.

### 4.2 실패 사유 분류 — 도메인 순수 함수가 소유한다

지금은 어떤 실패든 `Failed` + 한 문구다(V-11). **사유를 나눈다.**

`domain/capture/cameraFailure.ts` (신설 · 순수)

```ts
/** 카메라를 열지 못한 사유. `getUserMedia` 예외의 `name`에서만 유도한다. */
export type CameraFailureReason =
  | "permissionDenied"  // NotAllowedError · SecurityError(권한 거부) — 앱이 되돌릴 수 없다
  | "noDevice"          // NotFoundError · OverconstrainedError(재시도 후에도)
  | "inUse"             // NotReadableError · TrackStartError — 다른 앱이 점유
  | "insecureContext"   // isSecureContext === false (http:// 로 열었다)
  | "unknown";          // 그 외 전부

export function classifyCameraFailure(
  errorName: string,
  secureContext: boolean,
): CameraFailureReason;

/** 사유 → `STRINGS.camera.errors` 키. 문구가 화면마다 갈라지지 않게 한다. */
export function cameraFailureMessageKey(reason: CameraFailureReason): CameraFailureMessageKey;

/** 이 사유에 [다시 시도] 버튼을 붙이는가. `permissionDenied`·`insecureContext`는 **붙이지 않는다**
 *  (같은 조건에서 다시 눌러도 반드시 실패한다 — 손님을 헛돌게 한다). */
export function isCameraRetryable(reason: CameraFailureReason): boolean;
```

⚠️ **`insecureContext`를 먼저 판정한다.** `http://`로 열면 `navigator.mediaDevices` 자체가 `undefined`라
예외 `name`이 `TypeError`가 되어 `unknown`으로 뭉개진다. 현장에서 실제로 발생하는 오구성이다.

**문구 (`ui/strings.ts`의 `camera.errors`)** — [`03 §6.3`](../web-client/03-screens-spec.md) 265행의 규격 문구를 기준으로 한다.

| 키 | 문구 | 보조 안내 |
|----|------|-----------|
| `permissionDenied` | "카메라 권한이 거부되었습니다. 브라우저 설정에서 허용해 주세요." | §4.4의 브라우저별 복구 절차 |
| `noDevice` | "사용할 수 있는 카메라를 찾지 못했습니다. 연결을 확인해 주세요." | [다시 시도] |
| `inUse` | "카메라를 다른 앱이 사용 중입니다. 그 앱을 닫고 다시 시도해 주세요." | [다시 시도] |
| `insecureContext` | "보안 연결(https)에서만 카메라를 사용할 수 있습니다." | 관리자 문의 |
| `unknown` | 기존 `STRINGS.camera.failed` 문구를 그대로 재사용 | [다시 시도] |

**`cameraService`의 변경(최소)**

- `CameraState`의 `"Failed"` 유지. `setState("Failed", detail)`의 **`detail`에 `CameraFailureReason`을 싣는다**(지금은 한국어 문장).
- `open()`의 catch에서 `classifyCameraFailure(err.name, isSecureContext)`를 부르고, 그 값을 서비스가 보관해 `failureReason()`으로 노출한다.
- ⚠️ **`NotFoundError`/`OverconstrainedError`의 `{video:true}` 재시도(V-12)를 없애지 마라.** 저장된 `deviceId`가 사라진 정상 경로다. 재시도까지 실패했을 때만 `noDevice`로 확정한다.
- ⚠️ 어댑터는 여전히 **예외를 전파하지 않는다**([15 §2]).

### 4.3 권한 사전 요청 — 어디에 둘 것인가

**판정: `Guide` 화면.** Home이 아니다.

| 후보 | 채택? | 이유 |
|------|-------|------|
| `Home` | ✕ (문구만 상태화) | [`03 §2`](../web-client/03-screens-spec.md)가 Home을 **CTA 1개** 화면으로 규정한다. 손님 대면 첫 화면에 버튼이 2개가 되면 "촬영 시작"이 흐려진다 |
| **`Guide`** | **✅** | 이미 **[촬영 시작] 직전의 "준비 확인" 화면**이고 컷 수·카운트다운·거울모드를 확인시킨다 — **권한도 준비 항목**이다. 게다가 [촬영 시작] 제스처에서 오디오·Wake Lock을 확보하는 배선이 이미 여기 있다(V-9) |
| `Capture` 진입 시(현행) | ✕ 유지하되 최후 | 손님이 이미 흐름에 들어온 뒤 프롬프트가 뜬다. 거부하면 되돌릴 방법이 없다 |

**Home 쪽 변경(문구만)**: 지금 `"촬영을 시작하면 카메라 사용 권한을 묻습니다."` 가 **무조건** 렌더된다(`FlowViews.tsx:51`).
[`03 §2`](../web-client/03-screens-spec.md) 86행 규격은 **`prompt` 상태일 때만**이다 → 규격에 맞춘다.
`granted`면 숨기고, `denied`면 "카메라 권한이 거부되어 있습니다. 아래 안내를 확인해 주세요."로 바꾼다.

**`Guide` 화면 추가 블록**

```
┌ 촬영 안내 ────────────────────────────┐
│  컷 수 / 카운트다운 / 슬롯 수 / 거울모드   │   ← 기존
│                                        │
│  ▸ permission === "granted"            │
│      (아무것도 렌더하지 않는다)            │
│  ▸ permission === "prompt" | null      │
│      "촬영 전에 카메라 사용을 허용해 주세요."│
│      [ 카메라 사용 허용 ]  ← Button primary│
│  ▸ permission === "denied"             │
│      "카메라 권한이 거부되었습니다…"        │
│      + §4.4 브라우저별 복구 안내(펼침)      │
│                                        │
│  [취소]            [촬영 시작]           │   ← 기존
└────────────────────────────────────────┘
```

- **[촬영 시작]은 막지 않는다.** `denied`여도 누를 수 있다 — 손님이 갇히면 안 되고, `Capture`가 사유를 다시 보여준다.
- `null`(조회 불가 · Safari/Firefox)에도 버튼을 **보여준다.** 눌러서 손해가 없고, 안 보이면 그 브라우저에서 이 기능이 통째로 사라진다.

### 4.4 권한 어댑터 (신설) — `adapters/camera/cameraPermission.ts`

```ts
export type CameraPermission = "granted" | "denied" | "prompt" | null;

/** 조회만 한다. 프롬프트를 띄우지 않는다. 미지원·throw는 `null`. */
export function readCameraPermission(): Promise<CameraPermission>;

/** 변경 구독. 반환값은 **해제 함수**(미지원이면 no-op 해제자). */
export function watchCameraPermission(fn: (p: CameraPermission) => void): () => void;

/** 프롬프트를 띄운다. **사용자 제스처 안에서만** 부른다. */
export function requestCameraPermission(): Promise<CameraPermissionOutcome>;

export type CameraPermissionOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: CameraFailureReason };
```

**설계 제약 — 반드시 지킬 것**

1. **`readCameraPermission()`은 `DiagnosticsModal.tsx:44-63`에서 이 파일로 옮긴다**(복사가 아니라 이동).
   진단 모달은 새 어댑터를 import한다. 같은 조회가 두 벌이 되면 폴백 규칙이 갈라진다.
2. **`requestCameraPermission()`은 `cameraService`가 Idle일 때만 실제로 스트림을 연다.**
   ```
   if (getCameraService().state() !== "Idle") return { ok: true };   // 이미 열려 있다 = 이미 허용됐다
   const stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: true });
   stream.getTracks().forEach(t => t.stop());        // ★ 즉시 · 무조건 정지
   return { ok: true };
   ```
   - ⚠️ **`stop()`을 빠뜨리면 카메라 LED가 켜진 채 남는다.** `cameraService.teardown()`이 같은 이유로 이 줄을 갖고 있다(`cameraService.ts:177`).
   - ⚠️ **`{video:true}`만 쓴다.** 해상도 제약을 걸면 프라이밍 단계에서 `OverconstrainedError`가 날 수 있고, 그건 권한 문제가 아니다.
   - ⚠️ `audio: false` — 오디오를 요구하면 권한 범위가 넓어지고 손님이 거부할 확률이 올라간다(`cameraService.ts:78`의 이유와 동일).
   - 실패는 `classifyCameraFailure`로 분류해 돌려준다. **throw하지 않는다.**
3. **`watchCameraPermission`은 `PermissionStatus.onchange`를 쓰고 반드시 `removeEventListener`로 해제**한다.
   미지원이면 `() => undefined`를 돌려준다(호출측이 분기하지 않게).

**신설 정적 불변식 `CAM-1`**

```
CAM-1  `src/` 전체에서 `getUserMedia(` 를 부르는 파일은 정확히 2개다:
         adapters/camera/cameraService.ts  (실촬영 · 단일 하드웨어 소유자)
         adapters/camera/cameraPermission.ts (권한 프라이밍 · 즉시 stop)
       그리고 `cameraPermission.ts` 소스에는 `.stop()` 이 존재한다.
```

- 검사: `tests/unit/adapters/cameraInvariants.test.ts`(신설).
- **왜**: [`01 §2.1`](../web-client/01-tech-stack-and-structure.md)의 **하드웨어 단일 소유** 불변식을 프라이밍이 우회하지 못하게 한다. `.stop()` 존재 검사는 LED 잔존 회귀를 막는다.

### 4.5 운영자용 사전 승인 — [`09 §3`](../web-client/09-kiosk-operations.md) 확장 (Step F8)

현재 §3은 표 4행뿐이고 **복사해 쓸 수 있는 명령·정책 경로가 없다.** 사용자가 "어떻게 허용시킬지 모르겠다"고 한 것의 실질적 답이므로 아래를 그대로 넣는다.

**(1) Chrome/Edge 기업 정책 — 가장 확실 · 프롬프트 자체가 사라진다**

레지스트리 경로
```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome\VideoCaptureAllowedUrls
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge\VideoCaptureAllowedUrls
```
PowerShell(관리자):
```powershell
$p = "HKLM:\SOFTWARE\Policies\Google\Chrome\VideoCaptureAllowedUrls"
New-Item -ItemType Directory -Force $p | Out-Null
New-ItemProperty -Path $p -Name "1" -Value "https://mcphoto-955fb-kiosk.web.app" -PropertyType String -Force
New-ItemProperty -Path $p -Name "2" -Value "https://mcphoto-955fb-kiosk.firebaseapp.com" -PropertyType String -Force
gpupdate /force
```
확인: 주소창에 `chrome://policy` → `VideoCaptureAllowedUrls`가 목록에 보이고 상태가 OK.
⚠️ **오리진 단위**다. 경로(`/oauth2callback` 등)를 붙이면 매칭되지 않는다.
⚠️ 정책은 **관리형 브라우저에서만** 먹는다. `chrome://policy`에 안 뜨면 (2)로 간다.

**(2) 키오스크 기동 옵션 — 정책을 못 쓰는 현장**

```bat
:: 전용 프로필 + 키오스크 + 카메라 사전 허용
"C:\Program Files\Google\Chrome\Application\chrome.exe" ^
  --kiosk "https://mcphoto-955fb-kiosk.web.app" ^
  --user-data-dir="C:\mcphoto-kiosk-profile" ^
  --autoplay-policy=no-user-gesture-required
```
- **전용 프로필(`--user-data-dir`)이 핵심**이다. 여기서 [허용]을 **한 번만** 누르면 그 프로필에 영구 기억된다. 기본 프로필을 쓰면 사용자가 브라우저 데이터를 지울 때 함께 날아간다.
- ⚠️ **`--use-fake-ui-for-media-stream`은 운영에 쓰지 마라.** 프롬프트를 없애는 대신 **가짜(테스트용) 장치를 물릴 수 있고**, 실제 카메라 대신 초록 패턴이 찍히는 사고가 난다. 이 플래그는 CI 전용이다.
- ⚠️ `--kiosk` 로 띄우면 이슈 ③의 상단바 [전체화면] 버튼이 **필요 없다**(브라우저가 이미 전체화면). §3.2 P3-7.

**(3) 최초 1회 수동 승인 절차(정책·플래그 없이)**
1. 부스 브라우저로 kiosk URL을 연다 → **설정 → 진단·상태 → [카메라] 섹션**의 [권한] 행이 `확인 전`인지 본다.
2. 홈 → [촬영 시작] → 프레임 선택 → **촬영 안내 화면의 [카메라 사용 허용]** 을 누른다(Step F7 신설).
3. 브라우저 팝업에서 **[허용]** — ⚠️ **[이번만 허용]을 누르지 마라.** 매번 다시 묻는다.
4. 진단 [권한] 행이 `허용됨`으로 바뀌면 끝. 부스를 재부팅해도 유지되는지 1회 확인한다.

**(4) 거부 상태 복구 (화면에도 같은 내용을 띄운다 — §4.3)**

| 브라우저 | 절차 |
|----------|------|
| Chrome/Edge(데스크톱) | 주소창 왼쪽 **자물쇠(또는 ⓘ)** → [사이트 설정] → **카메라 → 허용** → 페이지 새로고침 |
| Android Chrome | 주소창 왼쪽 자물쇠 → [권한] → 카메라 → 허용 |
| iOS/iPadOS Safari | **설정 앱 → Safari → 카메라 → 허용**, 또는 주소창 `ᴀA` → [웹사이트 설정] → 카메라 → 허용 |
| macOS Safari | Safari → 설정 → 웹사이트 → 카메라 → 해당 도메인 → 허용. **추가로** macOS 시스템 설정 → 개인정보 보호 → 카메라에서 Safari 체크 |
| 공통 | 위를 해도 안 되면 **OS 레벨 카메라 권한**(Windows: 설정 → 개인정보 → 카메라 → "앱이 카메라에 액세스하도록 허용")을 확인한다 |

### 4.6 진단 모달 — **이미 있음**

카메라 권한 행은 **이미 구현돼 있다**(V-13). `granted`→"허용됨"/ok, `denied`→"거부됨"/bad, `prompt`→"확인 전"/warn, `null`→"알 수 없음"/neutral.

**바꿀 것은 하나뿐**: 조회 함수를 `DiagnosticsModal.tsx`에서 `adapters/camera/cameraPermission.ts`로 옮기고 import로 바꾼다(§4.4-1).
**추가**: `[카메라] 섹션`에 **[실패 사유]** 행 1개 — `cameraService.failureReason()`이 `null`이 아니면 표시(`권한 거부`·`장치 없음`·`사용 중`·`보안 연결 아님`). 실패한 적이 없으면 "없음"/ok.

---

## 5. 구현 단계 (WBS)

> 형식: [`docs/templates/WBS_BLUEPRINT.md`](../templates/WBS_BLUEPRINT.md). 각 단계는 **self-contained** — 이 문서를 안 읽은 fresh 에이전트가 그 단계만 보고 실행할 수 있어야 한다.
>
> **공통 검증 기준선**(착수 전 확인):
> `cd webclient && npx tsc --noEmit && npx vitest run` → **1926 통과 / 84파일**
> `cd web/functions && npm test` → **316 통과**

### Step F1: [사용자 액션 · 코드 아님] 웹 OAuth client_id 교정 + 재배포

- **Context Brief**: 배포된 백엔드가 Google code 교환에서 `invalid_client`로 실패한다. 원인은 `web/functions/.env.mcphoto-955fb:7`의 `GOOGLE_OAUTH_CLIENT_ID_WEB` 값이 문서 예시의 플레이스홀더 문자열 `<A1의 웹 client_id>` 그대로이기 때문이다. 실제 값은 `webclient/.env.production.local:2`의 `VITE_GOOGLE_CLIENT_ID`에 있다. **이 단계는 js-developer가 아니라 사용자(운영자)가 수행한다** — Secret Manager 접근과 배포 권한이 필요하다.
- **대상 파일**: `web/functions/.env.mcphoto-955fb`(gitignore — 커밋되지 않는다)
- **선행 조건**: 없음. **F2~F8과 병렬 가능**하며 **이 단계만으로 이슈 ①의 증상이 사라진다.**
- **구현 내용**:
  1. `.env.mcphoto-955fb:7`을 `GOOGLE_OAUTH_CLIENT_ID_WEB=712395684881-s3u9fh10bgugb38kagu81u2in2f5hd1h.apps.googleusercontent.com` 로 교체(= `webclient/.env.production.local:2`의 값).
  2. Google Cloud Console → API 및 서비스 → 사용자 인증 정보 → 그 **웹 애플리케이션** 클라이언트를 열어
     ① client_id가 위와 같은지 ② **승인된 리디렉션 URI**에 `.env.mcphoto-955fb:8`의 CSV 3개가 문자 단위로 들어 있는지 확인(가정 A-2).
  3. 같은 화면의 **client secret**을 확인해 Secret Manager 값과 같은지 판단한다(가정 A-1). 다르면 재등록:
     `firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET_WEB --project mcphoto-955fb`
  4. 재배포: `cd E:\Study\photobooth\web && deploy-web.bat functions`
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\web
  Select-String -Path functions\.env.mcphoto-955fb -Pattern "[<>]"          # 출력 0줄이어야 한다
  firebase functions:log --project mcphoto-955fb | Select-String "Google 로그인"
  ```
  그다음 `https://mcphoto-955fb-kiosk.web.app` 에서 실제 Google 계정으로 로그인.
- **완료 기준**:
  - [관측] 로그인 후 상단 계정 라벨이 계정 id로 바뀌고 직전 화면으로 복귀한다. **재시도 후 `firebase functions:log`에 `invalid_client`가 더 이상 추가되지 않는다.**
  - [non-goal] Windows 데스크톱 앱(clientKind 미지정=desktop) 로그인이 **영향받지 않는다** — `GOOGLE_OAUTH_CLIENT_ID`(desktop) 줄은 손대지 않는다.
  - [trigger] 재배포(`deploy-web.bat functions`) 완료 후에만 반영된다. env 파일만 고치고 배포하지 않으면 아무것도 바뀌지 않는다(이번 사고의 교훈).
- **롤백**: 이전 값으로 되돌리고 재배포. 배포 자체가 멱등이며 데이터 변경이 없다.
- [ ] 완료

### Step F2: 서버 — OAuth 클라이언트 자격 오류를 401에서 501로 분리

- **Context Brief**: `web/functions/src/services/googleAuth.ts`의 `verifyGoogleCodeAndGetEmail`이 Google 토큰 교환에 실패하면 전부 `GoogleAuthError`를 던지고, `routes/auth.ts:94-99`가 이를 **일반화 401**("이 Google 계정으로는 로그인할 수 없습니다…")로 바꾼다. 401 일반화의 목적은 **계정 열거 방지**다. 그런데 Google이 돌려주는 `invalid_client`·`unauthorized_client`는 **계정과 무관한 서버 구성 오류**라서, 이 둘만 501로 분리해도 열거 방어가 약해지지 않는다. 지금은 구성 오류가 "계정 탓"으로 표시돼 운영자가 원인을 찾지 못한다(2026-08-01 실제 발생).
- **대상 파일**: `web/functions/src/services/googleAuth.ts` · `web/functions/src/routes/auth.ts` · `web/functions/src/__tests__/googleAuth.test.ts` · `web/functions/src/__tests__/webOAuth.test.ts` · `docs/analysis/31-backend-api-reference.md` · `docs/analysis/61-auth-platform-integration.md`
- **선행 조건**: 없음(F1과 독립)
- **구현 내용**:
  1. **규격 먼저**(저장소 규칙 — [`web-client/README §3`](../web-client/README.md)): `analysis/31:85`·`:197`, `analysis/61:312` 부근의 501 의미를 "**SSO 미구성 또는 OAuth 클라이언트 자격 오류**"로 확장. 근거 1줄: *"계정 존재 여부와 무관한 사유이므로 401 일반화(열거 방지)의 대상이 아니다."*
  2. `GoogleAuthError`에 `readonly kind: "clientConfig" | "rejected"` 추가. **기본값 `"rejected"`** — 기존 throw 지점을 전부 고치지 않아도 동작이 안 바뀐다.
  3. `verifyGoogleCodeAndGetEmail`의 `getToken` catch에서 오류 메시지에 `invalid_client` 또는 `unauthorized_client`가 포함되면 `kind:"clientConfig"`로 던진다. **`invalid_grant`는 포함하지 않는다**(만료·재사용 code에서도 나오며 그것은 손님 흐름 문제다).
  4. `routes/auth.ts`의 `catch (err)`에서 `err.kind === "clientConfig"` → `HttpError.notImplemented("Google 로그인이 구성되지 않았습니다.")`. 그 외는 **기존 401 문구 그대로**.
  5. 테스트 6건 추가(§2.9 표).
- **검증 명령**: `cd E:\Study\photobooth\web\functions && npm test && npx tsc --noEmit`
- **완료 기준**:
  - [관측] `npm test`가 **326건 통과**(기준선 316 + 6 추가 + F3의 4건은 Step F3 몫이므로 이 단계에서는 **322건**). `invalid_client` 메시지로 throw하는 mock 팩토리를 주입하면 라우트 응답이 **501**이고 body의 문구가 "Google 로그인이 구성되지 않았습니다."다.
  - [non-goal] `invalid_grant`·`nonce 불일치`·`email_verified:false`·`hd 불일치`는 **여전히 401**이고 문구가 한 글자도 바뀌지 않는다. `googleOnlyAccounts.test.ts`·`session.test.ts`가 무수정으로 통과한다.
  - [trigger] 분기는 **Google 토큰 엔드포인트의 오류 메시지 문자열**로만 일어난다. 요청 필드·env로 분기하지 않는다.
- **롤백**: 이 단계 커밋 revert. `kind` 기본값이 `"rejected"`라 중간 상태에서도 종전 동작이다.
- [ ] 완료

### Step F3: 서버 — 배포 전 플레이스홀더 차단 가드

- **Context Brief**: 2026-08-01 사고의 실제 메커니즘은 "인수인계 문서([`14 §3`](../web-client/14-handoff-and-user-actions.md) 113행)의 예시 명령을 값 치환 없이 실행 → `.env.mcphoto-955fb`에 `<A1의 웹 client_id>`가 그대로 저장 → 배포 성공 → 로그인만 조용히 401"이다. 배포는 성공했기 때문에 아무도 눈치채지 못했다. 같은 실수를 기계가 잡게 한다.
- **대상 파일**: `web/functions/scripts/check-env-placeholders.mjs`(신설) · `web/functions/src/__tests__/envPlaceholder.test.ts`(신설) · `web/functions/package.json` · `web/deploy-web.bat`
- **선행 조건**: 없음
- **구현 내용**:
  1. 순수 함수 `findPlaceholderKeys(text: string): string[]` — dotenv 텍스트를 줄 단위로 읽어 ① `#`로 시작하는 줄 무시 ② `KEY=VALUE`에서 VALUE에 `<` 또는 `>`가 있으면 KEY 수집 ③ 필수 키 목록(`GOOGLE_OAUTH_CLIENT_ID_WEB` 등)의 값이 빈 문자열이면 수집. **이 함수만 테스트한다**(파일 I/O는 얇은 껍데기).
  2. `check-env-placeholders.mjs` — `functions/.env`, `functions/.env.<projectId>`를 읽어(없으면 건너뜀) `findPlaceholderKeys`가 비지 않으면 키 이름을 출력하고 `process.exit(1)`. ⚠️ **값을 출력하지 않는다**(시크릿 유출 방지).
  3. `package.json`에 `"check:env": "node scripts/check-env-placeholders.mjs"`.
  4. `deploy-web.bat`의 `[2/3] Building functions (tsc)` 성공 직후에
     `call npm --prefix functions run check:env` + `if errorlevel 1 goto :fail` 삽입. **hosting-only 경로(`:deployHosting`)에는 넣지 않는다**(functions env와 무관).
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/web/functions && npm test
  node scripts/check-env-placeholders.mjs && echo "PASS(현재 env 정상)"
  ```
- **완료 기준**:
  - [관측] `.env.mcphoto-955fb`에 `FOO=<bar>` 한 줄을 임시로 넣으면 스크립트가 **exit 1**과 함께 `FOO`를 출력한다(값 `<bar>`는 출력하지 않는다). 그 줄을 지우면 exit 0. `npm test`가 **320건**(316+4) 통과.
  - [non-goal] `deploy-web.bat hosting` 경로는 이 검사를 **거치지 않는다**(functions를 배포하지 않으므로). `.env` 파일이 없는 CI 환경에서도 exit 0이다.
  - [trigger] 검사는 `deploy-web.bat`의 functions 경로와 `npm run check:env`를 명시적으로 실행할 때만 돈다. `npm test`·`npm run build`를 막지 않는다.
- **롤백**: `deploy-web.bat`의 2줄 제거. 스크립트 파일은 남겨도 무해하다.
- [ ] 완료

### Step F4: 웹 — 로그인 실패 사유를 로그·진단에 남긴다

- **Context Brief**: `webclient`의 로그인 실패 분류는 `adapters/auth/googleSignIn.ts:145-161`의 `classifyExchangeError`가 소유한다. 지금 400(redirectRejected)에만 `logger.error`가 있고 **401·501에는 로그가 없다**. 그래서 운영자가 현장에서 "왜 안 되는지" 알 방법이 진단 모달에도 없다. 사유 enum(`LoginFailureReason` — `domain/auth/loginFailure.ts`)과 셸 스토어 `shell/loginStore.ts`는 이미 있다.
- **대상 파일**: `webclient/src/adapters/auth/googleSignIn.ts` · `webclient/src/domain/auth/loginFailure.ts` · `webclient/src/shell/loginStore.ts` · `webclient/src/screens/oauthCallback/oauthCallbackRunner.ts` · `webclient/src/screens/modals/diagnostics/diagnosticsPresenter.ts` · `webclient/src/screens/modals/diagnostics/DiagnosticsModal.tsx` · `webclient/src/ui/strings.ts` · 대응 테스트
- **선행 조건**: 없음
- **구현 내용**:
  1. `classifyExchangeError`에 501·401 로그 추가.
     ⚠️ **키 이름을 `code`로 쓰지 마라** — 마스킹된다([15 §4 함정 1]). `errorCode`를 쓴다.
     501 → `logger.error("서버 OAuth 구성 오류 — 운영자 확인 필요", { status: 501, errorCode })`
     401 → `logger.warn("로그인 거부(계정·도메인)", { status: 401, errorCode })`
     ⚠️ **email·token·code·state·nonce를 절대 싣지 마라**(AUTH-3).
  2. `domain/auth/loginFailure.ts`에 순수 함수 `describeLoginFailure(reason): string` 추가 — 사유 → 진단 표시 문구(`Record`로 두어 사유 추가 시 컴파일이 깨지게 한다. 기존 `MESSAGE_KEY_BY_REASON`과 같은 형태).
  3. `shell/loginStore.ts`(zustand vanilla store — 현재 필드는 `notice`/`fail`/`clear`)에 **`lastFailure: { reason: LoginFailureReason; at: number } | null`** 추가.
     ⚠️ **`clear()`가 `lastFailure`를 지우게 하지 마라.** `notice`는 `Login` 화면이 마운트하면서 소비·소거한다(`useGoogleSignIn.ts:84`) — 진단 흔적이 화면을 여는 것만으로 사라지면 쓸모가 없다. `lastFailure`는 **로그인 성공 시에만** null로 돌아간다.
     기록 지점은 기존 `fail(reason)` 안이 가장 안전하다(호출부가 이미 전부 거기로 모인다). `at`은 **주입**한다(`Date.now`를 스토어가 직접 부르지 않는다 — [15 §3.2]).
     **메모리 전용**(M2 정신 — 저장소 API를 쓰지 않는다).
  4. 기존 `fail(reason)` 호출부(`useGoogleSignIn.ts:75` · `oauthCallbackRunner.ts`의 실패 경로)는 **수정 없이** 그대로 두고, 성공 경로에만 초기화를 붙인다.
  5. `diagnosticsPresenter.ts`: `DiagnosticsDeps`에 `lastLoginFailure: () => { reason; at } | null` 추가 → `buildServerSection` 마지막 행 **[마지막 로그인 실패]**. 없으면 `"없음"`/tone `ok`, 있으면 `` `${describeLoginFailure(reason)} · ${formatTimestamp(at)}` ``/tone `bad`. `DiagnosticsModal.tsx`의 `buildDeps`에 배선.
- **검증 명령**: `cd E:/Study/photobooth/webclient && npx tsc --noEmit && npx vitest run`
- **완료 기준**:
  - [관측] `classifyExchangeError`에 401/501 오류를 넣으면 `logStore`에 해당 엔트리가 쌓이고 `errorCode` 값이 `[masked]`가 **아니다**. `collectDiagnostics`에 `lastLoginFailure`를 주입하면 서버 섹션 마지막 행의 value가 사유 문구 + 시각이다.
  - [non-goal] 화면에 보이는 로그인 실패 **문구 5종은 한 글자도 바뀌지 않는다**(`STRINGS.login.errors`). `AUTH-1`(`sessionStore.login(` 1곳)·`AUTH-3`(인증 파일 로그 키)·`M2`(authStore에 저장소 API 0건) 정적 테스트가 그대로 통과한다.
  - [trigger] `lastFailure`는 **실제 로그인 실패에서만** 기록된다. 진단 모달을 여는 것만으로 값이 생기지 않고, 로그인 성공 시 `null`로 돌아간다.
- **롤백**: 이 단계 커밋 revert. 다른 단계와 파일이 겹치지 않는다.
- [ ] 완료

### Step F5: 웹 — 첫 제스처 전체화면 제거 + 상단바 [전체화면] 버튼

- **Context Brief**: `webclient/src/main.tsx:68-76`의 `installFirstGesture()`가 화면 첫 `pointerdown`/`keydown`에서 **전체화면 + Wake Lock**을 함께 요청한다. 손님이 어디를 만지든 전체화면으로 들어가 버려 "원인 없는 상태 변화"가 된다. 전체화면만 떼어내고 **명시적 버튼**으로 옮긴다. ⚠️ **Wake Lock 요청은 반드시 유지**해야 한다(제거하면 촬영 중 화면이 꺼진다). 오디오 unlock은 첫 제스처가 아니라 `Guide`의 [촬영 시작]에 있으므로(`ui/views/FlowViews.tsx:88`) 이 단계에서 건드리지 않는다. 전체화면 이탈 배너(`App.tsx:256-262`)는 `shellStore.fullscreenLost`가 참일 때만 뜨며, 자동 진입이 사라지면 한 번도 전체화면이 아니었던 세션에서는 뜨지 않는다 — 버튼과 배너는 상호 배타다.
- **대상 파일**: `webclient/src/main.tsx` · `webclient/src/shell/fullscreenController.ts` · `webclient/src/shell/shellStore.ts` · `webclient/src/domain/navigation/fullscreenButtonPolicy.ts`(신설) · `webclient/src/ui/components/index.tsx`(TopBar) · `webclient/src/App.tsx` · `webclient/src/ui/strings.ts` · `webclient/tests/unit/shell/fullscreenInvariants.test.ts`(신설) · 대응 단위 테스트
- **선행 조건**: 없음
- **구현 내용**: §3.3의 ①~⑥ 전부.
  - `fullscreenController`에 `isSupported()` 추가(**런타임 감지** — TS DOM lib은 `requestFullscreen`을 필수 멤버로 선언한다, [15 §4 함정 2]). `request`/`exit`/`install`은 무변경.
  - `shellStore`에 `isFullscreen: boolean` 추가. `fullscreenController.install()`의 `onChange`에서 `setFullscreenLost`와 **함께** 갱신. ⚠️ `fullscreenLost`의 의미를 바꾸지 마라.
  - 도메인 순수 함수 `isFullscreenButtonVisible({supported,isFullscreen,fullscreenLost,standalone})` 신설(브라우저 API 미접촉 — `purity.test.ts`가 자동 포함).
  - `TopBar`에 `showFullscreen: boolean` · `onFullscreen: () => void` props. 계정 버튼 **왼쪽**에 `<Button variant="ghost" aria-label={STRINGS.fullscreen.enter}>`. `STRINGS.fullscreen.enter = "전체화면"` 추가.
  - `main.tsx`의 콜백에서 `getFullscreenController().request()` **한 줄만 제거**. `requestWakeLock()`과 `logger.info`는 유지(로그 문구를 "첫 제스처 처리(WakeLock 요청)"로 정정).
  - 정적 불변식 **FS-1** 테스트 신설(§3.4).
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient && npx tsc --noEmit && npx vitest run
  ```
- **완료 기준**:
  - [관측] `installFirstGestureHandlers`의 콜백을 실행해도 전체화면 컨트롤러의 `request`가 호출되지 않고 `requestWakeLock`은 호출된다(단위 테스트로 주입 검증). `isFullscreenButtonVisible`이 `{supported:true, isFullscreen:false, fullscreenLost:false, standalone:false}`에서만 참이고 나머지 15조합에서 거짓이다. FS-1 테스트가 `request(` 호출 2곳을 확인한다.
  - [non-goal] **Wake Lock 요청이 사라지지 않는다**(화면 꺼짐 회귀 금지). **오디오 unlock 경로(`FlowViews.tsx:88` `unlockAudio()`)가 무수정**이다(셔터음 회귀 금지). 전체화면 이탈 배너는 종전대로 `fullscreenLost === true`에서만 렌더되며 버튼과 **동시에 보이지 않는다**. `isTopBarVisible` 규칙(Capture·Qr 숨김)이 바뀌지 않는다.
  - [trigger] 전체화면 진입은 **상단바 [전체화면] 버튼 클릭** 또는 **이탈 배너의 [다시 전체화면으로] 클릭**, 이 둘뿐이다. 화면의 다른 곳을 터치·클릭·키 입력해도 전체화면으로 들어가지 않는다.
- **롤백**: 이 단계 커밋 revert. `main.tsx` 한 줄 복원만으로도 종전 동작으로 돌아간다.
- [ ] 완료

### Step F6: 웹 — 카메라 실패 사유 분류 + 권한 어댑터 신설

- **Context Brief**: `webclient/src/adapters/camera/cameraService.ts:252`가 어떤 실패든 `setState("Failed", "카메라를 열 수 없습니다.")` 하나로 뭉갠다. 화면에는 `STRINGS.camera.failed`("카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요.") 한 문구만 뜬다(`ui/views/CameraPreview.tsx:70-74`). **권한 거부와 장치 부재는 손님이 할 조치가 완전히 다르다.** 규격 [`03 §6.3`](../web-client/03-screens-spec.md)은 이미 거부 전용 안내 + [다시 시도]를 요구하고 있어 **구현이 규격에 미달인 상태**다. 또한 카메라 권한 조회 함수가 `screens/modals/diagnostics/DiagnosticsModal.tsx:44-63`에 파일 로컬로 갇혀 있어 다른 화면이 쓸 수 없다.
- **대상 파일**: `webclient/src/domain/capture/cameraFailure.ts`(신설) · `webclient/src/adapters/camera/cameraPermission.ts`(신설) · `webclient/src/adapters/camera/cameraService.ts` · `webclient/src/adapters/camera/cameraTypes.ts` · `webclient/src/ui/views/CameraPreview.tsx` · `webclient/src/ui/strings.ts` · `webclient/src/screens/modals/diagnostics/DiagnosticsModal.tsx` · `webclient/src/screens/modals/diagnostics/diagnosticsPresenter.ts` · `webclient/tests/unit/adapters/cameraInvariants.test.ts`(신설) · 대응 단위 테스트
- **선행 조건**: 없음
- **구현 내용**: §4.2 + §4.4 + §4.6.
  - 도메인 `cameraFailure.ts`: `CameraFailureReason` 5종 · `classifyCameraFailure(errorName, secureContext)` · `cameraFailureMessageKey` · `isCameraRetryable`. **`insecureContext`를 가장 먼저 판정**(http에서는 `navigator.mediaDevices`가 undefined라 name이 `TypeError`가 된다).
  - 어댑터 `cameraPermission.ts`: `readCameraPermission` (DiagnosticsModal에서 **이동**) · `watchCameraPermission`(해제 함수 반환 필수) · `requestCameraPermission`(⚠️ `cameraService.state() !== "Idle"`이면 스트림을 열지 않는다 · `{audio:false, video:true}` · **획득 즉시 `getTracks().forEach(t => t.stop())`** · throw 금지).
  - `cameraService`: `open()`의 catch에서 사유를 분류해 보관, `failureReason(): CameraFailureReason | null` 노출. `setState("Failed", reason)`. ⚠️ **`NotFoundError`/`OverconstrainedError`의 `{video:true}` 재시도를 없애지 마라** — 저장된 `deviceId`가 사라진 정상 복구 경로다.
  - `CameraPreview`: `failedMessage` 기본값을 사유 기반 문구로. `isCameraRetryable(reason)`이 참일 때만 [다시 시도] 버튼을 렌더.
  - `STRINGS.camera.errors` 5키 추가(§4.2 표 문구 그대로).
  - 진단 [카메라] 섹션에 **[실패 사유]** 행 추가.
  - 정적 불변식 **CAM-1** 테스트 신설(§4.4).
- **검증 명령**: `cd E:/Study/photobooth/webclient && npx tsc --noEmit && npx vitest run`
- **완료 기준**:
  - [관측] `classifyCameraFailure("NotAllowedError", true) === "permissionDenied"`, `("NotFoundError", true) === "noDevice"`, `("NotReadableError", true) === "inUse"`, `(anything, false) === "insecureContext"`. `openStream`이 `NotAllowedError`를 던지도록 주입하면 `cameraService.failureReason() === "permissionDenied"`이고 `CameraPreview`가 "카메라 권한이 거부되었습니다…"를 렌더하며 **[다시 시도]가 없다**. CAM-1 테스트가 `getUserMedia(` 보유 파일 2개를 확인한다.
  - [non-goal] `WM1`(CSS 반전 금지 — `scaleX(-1)`·`rotateY(180deg)` 0건 + `<video>` 미렌더)이 그대로 통과한다. `requestCameraPermission` 호출 후 `cameraService.start()`가 여전히 성공한다(스트림 점유 잔존 없음). 어댑터는 **예외를 전파하지 않는다**.
  - [trigger] `requestCameraPermission()`은 **사용자 제스처 핸들러 안에서만** 호출된다. 모듈 로드·마운트·진단 모달 열기로는 절대 호출되지 않는다(진단은 `readCameraPermission`만 쓴다 — 여는 것만으로 LED가 켜지면 안 된다).
- **롤백**: 이 단계 커밋 revert. 신설 파일 2개 삭제 + `cameraService`의 detail 문자열 복원.
- [ ] 완료

### Step F7: 웹 — Guide 화면 권한 사전 요청 + Home 안내 문구 상태화

- **Context Brief**: 브라우저 권한 프롬프트는 자동으로 뜰 수 없고 `getUserMedia()` 호출 + 사용자 제스처가 필요하다. 지금은 `Capture` 화면 마운트에서 처음 호출되어(`screens/capture/useCaptureRunner.ts:93`) 손님이 이미 흐름에 들어온 뒤 프롬프트를 만난다. **촬영에 들어가기 전에 받도록** `Guide` 화면(`ui/views/FlowViews.tsx`의 `GuideView`)에 [카메라 사용 허용] 버튼을 둔다. Home이 아니라 Guide인 이유: [`03 §2`](../web-client/03-screens-spec.md)가 Home을 CTA 1개 화면으로 규정하고, Guide는 이미 [촬영 시작] 직전의 준비 확인 화면이며 오디오·Wake Lock 확보 배선이 여기 있다.
- **대상 파일**: `webclient/src/ui/views/FlowViews.tsx`(`HomeView`·`GuideView`) · `webclient/src/screens/capture/useCameraPermission.ts`(신설 훅) · `webclient/src/ui/views/screens.module.css` · `webclient/src/ui/strings.ts` · 대응 테스트
- **선행 조건**: **Step F6**(`adapters/camera/cameraPermission.ts`·`domain/capture/cameraFailure.ts`)
- **구현 내용**:
  1. 훅 `useCameraPermission()` — 마운트 시 `readCameraPermission()` 1회 + `watchCameraPermission` 구독. **반환된 해제 함수를 cleanup에서 반드시 호출**한다. 언마운트 후 `setState` 방지 가드 필요. ⚠️ StrictMode 이중 effect에서 구독이 두 번 붙지 않게 하고, **cleanup에서 진행 중 요청을 취소하는 형태를 만들지 마라**([15 §6] Step 12·13 함정).
  2. `GuideView`에 §4.3의 블록 추가:
     - `granted` → 아무것도 렌더하지 않는다.
     - `prompt` | `null` → 안내 1줄 + `<Button variant="primary">카메라 사용 허용</Button>` → `requestCameraPermission()`.
     - `denied` → 거부 안내 + **브라우저별 복구 절차**(§4.5-(4) 표를 `<details>`/접기 UI로. ⚠️ `innerHTML` 금지 — JSX 텍스트 노드로 렌더한다).
     - **[촬영 시작]은 어떤 상태에서도 비활성화하지 않는다.**
  3. `HomeView`의 note를 상태별로: `prompt`→기존 문구, `granted`→렌더 안 함, `denied`→"카메라 권한이 거부되어 있습니다. 촬영 안내 화면에서 복구 방법을 확인해 주세요."
  4. `STRINGS.camera`에 `allowButton`·`allowHint`·`deniedHint`·`recovery.*` 추가.
- **검증 명령**: `cd E:/Study/photobooth/webclient && npx tsc --noEmit && npx vitest run`
- **완료 기준**:
  - [관측] `readCameraPermission`이 `"prompt"`를 반환하도록 주입하면 Guide에 [카메라 사용 허용] 버튼이 렌더되고, 클릭 시 `requestCameraPermission`이 **정확히 1회** 호출된다. `"granted"`면 버튼이 **렌더되지 않는다**. `"denied"`면 버튼 대신 복구 안내가 렌더된다.
  - [non-goal] Home 화면의 **CTA는 여전히 [촬영 시작] 1개**다(버튼이 늘지 않는다). `Guide`의 [촬영 시작]은 `denied` 상태에서도 **활성**이며 누르면 종전대로 `Capture`로 간다(손님이 갇히지 않는다). `Capture` 진입 시의 기존 카메라 시작 경로가 **무수정**이다. `unlockAudio()`·`requestWakeLock()` 호출이 [촬영 시작] 핸들러에 그대로 남는다.
  - [trigger] 권한 프롬프트는 **[카메라 사용 허용] 버튼 클릭** 또는 **`Capture` 진입** 시에만 뜬다. `Guide` 화면에 들어가는 것만으로는 프롬프트가 뜨지 않고 카메라 LED도 켜지지 않는다.
- **롤백**: 이 단계 커밋 revert. Guide/Home이 종전 렌더로 돌아간다(F6은 독립적으로 유효).
- [ ] 완료

### Step F8: 문서 갱신 (규격·운영·차이 보고서·관례)

- **Context Brief**: 이 저장소는 **문서와 코드가 함께 움직인다.** [`web-client/README §3`](../web-client/README.md)이 갱신 규칙을 정하고 있고, 웹에서만 다르게 동작하는 것은 반드시 [`12`](../web-client/12-web-vs-windows-differences.md)에 등재해야 한다(등재되지 않은 차이는 버그로 취급). F2·F5·F6·F7이 규격을 건드리므로 문서를 맞춘다.
- **대상 파일**: `docs/analysis/31-backend-api-reference.md` · `docs/analysis/61-auth-platform-integration.md` · `docs/web-client/02-app-shell-and-navigation.md` · `docs/web-client/03-screens-spec.md` · `docs/web-client/07-auth-and-permissions-web.md` · `docs/web-client/09-kiosk-operations.md` · `docs/web-client/12-web-vs-windows-differences.md` · `docs/web-client/14-handoff-and-user-actions.md` · `docs/web-client/15-implementation-conventions.md`
- **선행 조건**: F2·F5·F6·F7 (구현 확정 후 문서를 맞춘다)
- **구현 내용**:
  | 문서 | 고칠 것 |
  |------|---------|
  | `analysis/31:85,197` · `analysis/61:312` | 501 의미 확장(§2.8). **F2와 같은 커밋에 넣어도 좋다** |
  | `web-client/02 §7`(262~275행) | "첫 사용자 제스처 → requestFullscreen" 행을 **삭제**하고 "**상단바 [전체화면] 버튼**(전체화면이 아니고 · API 지원 · standalone 아님 · 이탈 배너 미표시일 때만 노출)"로 교체. `02 §2` 상태표 40행의 `Home` 행 "첫 제스처에서 전체화면·오디오·WakeLock 시도"도 정정(전체화면 제거) |
  | `web-client/03 §2`(86행) | Home 안내 문구를 **`prompt` 상태에서만** 노출로 명확화 + `denied` 문구 추가 |
  | `web-client/03 §5`(Guide) | **[카메라 사용 허용] 버튼 규격 신설**(노출 조건 3상태 · [촬영 시작] 비활성화 금지) |
  | `web-client/03 §6.3`(265행) | 실패 사유 **5종 표**로 확장(현재 1행) + `isCameraRetryable` 규칙 |
  | `web-client/07` 오류 매핑 | 501 확장 반영 |
  | `web-client/09 §2` | 키오스크 기동 절차에 **"`--kiosk`로 띄우면 상단바 [전체화면] 버튼이 필요 없다"** 관계 명시 |
  | `web-client/09 §3` | §4.5의 (1)~(4) 전량으로 교체(복사 가능한 PowerShell·레지스트리 경로·bat 포함). ⚠️ **`--use-fake-ui-for-media-stream`은 운영 금지**를 경고로 남긴다 |
  | `web-client/12 C4` | "몰입 모드" 행을 "**명시적 [전체화면] 버튼**으로만 진입(첫 터치 자동 진입은 2026-08-01 폐지 — 원인 없는 상태 변화)"로 |
  | `web-client/12 C5` | §4.1의 가능/불가능 표를 반영. "거부 시 → 전용 안내 + [다시 시도]"를 **사유 5종**으로 확장 |
  | `web-client/12 §E` | **E10 신설** — "상단바 [전체화면] 버튼"(Windows에는 없는 웹 전용 진입점) · **E11 신설** — "카메라 권한 사전 요청 버튼(Guide)" |
  | `web-client/14 §3` | A2 절차 113행에 치환 경고 + A1~A5 표에 **A2 재수행 필요** 주석(§2.8) |
  | `web-client/15 §3.4` | 불변식 표에 **FS-1**·**CAM-1** 2행 추가 |
  | `web-client/15 §4` | **함정 17** 추가(플레이스홀더 배포) |
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth
  grep -rn "첫 제스처에서 전체화면\|첫 사용자 제스처" docs/web-client/     # 0줄이어야 한다
  grep -n "FS-1\|CAM-1" docs/web-client/15-implementation-conventions.md   # 각 1줄 이상
  grep -n "VideoCaptureAllowedUrls" docs/web-client/09-kiosk-operations.md # 1줄 이상
  ```
- **완료 기준**:
  - [관측] 위 3개 grep이 기대대로 나온다. `12`의 §E 항목 수가 9 → **11**로 늘고 문서 제목의 "(9건)"이 "(11건)"으로 함께 바뀐다.
  - [non-goal] `docs/analysis`의 **401 일반화 규칙 자체**는 바뀌지 않는다(열거 방지 유지). `docs/spec-vectors/*` 파일은 **한 개도 건드리지 않는다**(순수 로직 규격 무변경).
  - [trigger] 문서 갱신은 F2·F5·F6·F7 구현이 확정된 뒤에만 한다 — 구현이 바뀌면 문서를 두 번 고쳐야 한다.
- **롤백**: 이 단계 커밋 revert(문서만).
- [ ] 완료

### 5.1 실행 순서와 병렬성

```
F1 (사용자 액션) ──────────────────────── 즉시 · 단독으로 이슈 ① 증상 해소
F2 ─┬─ F3        (서버 · 병렬 가능)
    └─ F4        (웹 · 병렬 가능)
F5              (웹 · 완전 독립)
F6 ── F7        (F7은 F6의 어댑터 필요)
                 ↓
                F8 (문서 — F2·F5·F6·F7 확정 후)
```

**우선순위**: F1 ≫ F5 ≈ F6·F7 > F2·F4 > F3 > F8.
F1은 사용자가 수행하므로 developer는 **F5부터 착수해도 된다.**

### 5.2 완결성 게이트 (self-check)

- [x] 검증된 사실(19) / 미검증 가정(5) 목록이 분리돼 있다
- [x] 모든 가정(A-1~A-5)에 검증 단계가 매핑돼 있다
- [x] 8개 단계 전부에 7개 필수 필드가 있다
- [x] 완료 기준이 전부 관측 기반 3문 형식이다(UI 단계 F5·F7은 non-goal·trigger 포함)
- [x] 검증 명령이 전부 자동 실행 가능한 CLI다

---

## 6. 기존 불변식과의 관계

| 불변식 | 영향 | 판정 |
|--------|------|------|
| `M2`·`M2-a`·`M2-b`(JWT 메모리 전용 · sessionStorage 1파일) | F4가 `loginStore`에 실패 사유를 **메모리로만** 둔다 | **무위반** — 저장소 API 미사용 |
| `AUTH-1`(`sessionStore.login(` 1곳) | F4는 `login`을 부르지 않는다 | **무위반** |
| `AUTH-2`(`clientKind: "web"` 고정) | 변경 없음 | **무위반** |
| `AUTH-3`(인증 로그에 `code`·`state`·`nonce`·`token`·`pin` 0건) | F4가 로그를 **추가**한다 → 키 이름을 `errorCode`·`status`로 쓴다 | **무위반**(설계에 명시). 리뷰에서 반드시 확인 |
| `AUTH-4`(App.tsx에 `devLogin` 0건) · `AUTH-5`(`prompt=select_account`) | 변경 없음 | **무위반** |
| `WM1`(CSS 반전 금지 · `<video>` 미렌더) | F6이 `CameraPreview`를 건드린다 | **무위반** — 오버레이 문구·버튼만 바뀐다 |
| `DIAG-1`(진단에서 `backendApiKey` 줄에 `.length`/`.trim()` 필수) | F4가 진단 서버 섹션에 행을 추가한다 | **무위반** — 게이트 키를 만지지 않는다 |
| `SET-3`(App.tsx에 임시 진입점 문자열 0건) | F5가 `App.tsx`에 **[전체화면] 버튼**을 추가한다 | **무위반 · 소스로 확인함.** 이 검사는 금지 문자열 **3개**(`"로컬 저장 폴더 선택"`·`"카메라 테스트 열기"`·`"pickLocalSaveFolder"`)만 본다(`tests/unit/…/settingsInvariants.test.ts:149-155`). `"전체화면"`은 목록에 없다. **금지 목록에 새 문자열을 추가하지도 마라** — 그 목록은 Step 6·10의 임시 진입점 재발 방지 전용이다 |
| 도메인 순수성(`purity.test.ts`) | 신설 `fullscreenButtonPolicy.ts`·`cameraFailure.ts`가 도메인에 들어간다 | **자동 포함됨**(glob). 브라우저 API·`Date.now`·`Math.random`·`console` 미사용이어야 한다 |
| `01 §2.1` 하드웨어 단일 소유 | F6이 `getUserMedia`를 부르는 **두 번째 파일**을 만든다 | **의도된 확장.** `cameraService`가 Idle일 때만 열고 즉시 stop한다. **신설 `CAM-1`이 이 계약을 기계로 고정**한다 |

**깨야 하는 불변식: 없음.** 신설 2건(`FS-1`·`CAM-1`)은 모두 **제약을 더하는** 방향이다.

---

## 7. 남는 실측 항목 (사람 몫 · [`14 §10`](../web-client/14-handoff-and-user-actions.md)에 V26으로 신설 제안)

| # | 항목 | 확인 |
|---|------|------|
| V26-1 | **실 Google 계정으로 배포본 로그인 완주**(F1 이후) | 계정 라벨 변경 + 서버 로그에 `invalid_client` 미추가. **V21-1을 다시 여는 것과 같다** |
| V26-2 | iPad Safari·Android Chrome에서 [전체화면] 버튼의 **노출/미노출이 올바른가**(가정 A-3) | 미지원 기기에서 버튼이 **보이지 않는다**. 지원 기기에서 눌러 전체화면이 되고 버튼이 사라진다 |
| V26-3 | Safari·Firefox에서 권한 조회가 `null`로 폴백하는가(가정 A-4) | Guide에 [카메라 사용 허용]이 **보인다**(숨지 않는다). 진단 [권한] 행이 "알 수 없음" |
| V26-4 | [카메라 사용 허용] → 허용 → [촬영 시작]에서 **카메라가 정상 시작**되는가(가정 A-5) | 프리뷰가 뜬다. 프라이밍 직후 카메라 **LED가 꺼진다**(Guide 머무는 동안 켜져 있으면 `stop()` 누락) |
| V26-5 | 권한을 **거부**하고 [촬영 시작] → 전용 안내가 뜨고 [다시 시도]가 **없다** | 문구가 "카메라 권한이 거부되었습니다…" |
| V26-6 | Chrome 정책 `VideoCaptureAllowedUrls` 적용 후 **프롬프트가 뜨지 않는다** | `chrome://policy`에 정책이 보이고, 첫 접속에서 바로 프리뷰 |
