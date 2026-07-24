# MCPhoto — item1b: Google SSO(소셜 로그인) 연동 설계

| 항목 | 값 |
|------|-----|
| 문서 성격 | **설계 문서**(코드/배포 미착수) — item1b 신규분(운영자 Google SSO 로그인). 게스트는 로그인하지 않으므로 대상 외 |
| 대상 | 백엔드 `web/functions/`(신규 `/auth/google`) + 클라 `MCPhoto.Http`·`MCPhoto.App`(브라우저+loopback+버튼·서비스) |
| 아키텍처 전제 | 방향 B(백엔드 경유) 확정. `UseBackend` 기본 OFF, ON일 때만 SSO 노출. Google **OAuth 2.0 for Native/Installed apps**(시스템 브라우저 + loopback + PKCE) |
| 작성일 | 2026-07-24 |
| 상태 | **설계 v1 (리뷰 대기)** |
| 근거 | 모든 "현재 동작"은 `파일:라인` 실측. 실측 파일 목록은 §12 |
| 선행 | item1a(계정 email·emailVerified) **구현 완료**(`web/functions/src/services/{accounts,dto}.ts`, `src/MCPhoto.Core/Models/User.cs`). item1b는 이 위 가산 설계 |
| 후속 | 확정 후 별도 WBS 블루프린트(`docs/templates/WBS_BLUEPRINT.md` 형식)로 단계화 |

> **표기 규칙**
> - `[CODE]` : 내가 코드로 작업(백엔드 `/auth/google`·검증, 클라 브라우저+loopback+버튼·서비스).
> - `[CONSOLE]` : 사용자가 Google Cloud 콘솔·CLI·Functions 시크릿에서 수동 수행(OAuth 클라이언트 ID·리디렉션 URI·동의화면·백엔드 시크릿). §10에 USER-ACTIONS §B2 추가 목록으로 정리.
> - `[CONFIRM]` : 설계자가 합리적 기본안으로 **확정**했으나 리뷰 시 조정 가능한 결정. 근거 명시.
> - `[USER-DECISION-REQUIRED]` : 설계자가 정할 수 없는 순수 제품/운영 판단. 사용자 답변 필요.
> - 근거는 `파일:라인`. **가정**은 소스 미확인 추정.

---

## 0. 요약 (Executive Summary)

### 0.1 무엇을 추가하는가

현행 운영자 로그인은 **id/password → JWT**뿐이다(`web/functions/src/routes/auth.ts:40-68`, `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs:52-79`). item1b는 그 **대안 인증 수단**으로 Google SSO를 얹는다:

1. **데스크톱 OAuth 플로우(§3)**: WPF가 **시스템 기본 브라우저**로 Google 동의 화면을 열고, **loopback 리디렉션**(`http://127.0.0.1:{임의포트}`)으로 `authorization code`를 수신한다. **PKCE**(RFC 7636)로 code 교환을 보호해 **client secret을 클라에 두지 않는다**.
2. **백엔드 code 교환·검증(§4·§5)**: 신규 `POST /auth/google`. 클라가 받은 `code`(+`codeVerifier`+`redirectUri`)를 백엔드로 전달 → 백엔드가 Google token endpoint에 code를 교환(client secret은 **백엔드 시크릿**) → `id_token`을 Google 공개키로 검증 → 검증된 email 획득.
3. **계정 매핑(§6)**: 검증된 email → **기존 MCPhoto 계정(email 일치 + `emailVerified=true`)** 을 찾아 로그인. **자동 계정 생성 금지**(통제된 키오스크 — admin이 계정을 선등록). 매칭 실패는 명확 안내. 역할은 **매칭된 MCPhoto 계정**에서 가져온다(Google에는 역할 개념 없음).
4. **JWT 재사용(§5.4)**: 매핑 성공 시 기존 `issueToken`으로 **동일 형식의 JWT**를 발급. 이후 모든 호출은 현행 Bearer 경로 그대로(신규 인증 상태 없음).
5. **클라 UI(§7)**: 로그인 화면에 "Google로 로그인" 버튼(백엔드 모드 게이트). 브라우저 실행 + loopback 리스너 + `/auth/google` 호출 → `IBackendSession.SignIn` → 기존 복귀 흐름.
6. **보안(§8)**: PKCE·state(CSRF)·nonce, client secret 백엔드 전용, loopback 리스너 안전(포트 무작위·1회성·타임아웃), `id_token`의 `aud`/`iss`/`exp`/`nonce` 검증, 토큰 로그 미노출.

### 0.2 핵심 정책 결정(요약)

| 결정 | 값 | 근거 |
|------|-----|------|
| OAuth 클라이언트 유형 | **Desktop app**(Installed) — 시스템 브라우저 + loopback + PKCE | 데스크톱/키오스크. embedded WebView 지양(Google 권고·정책, §3.1) |
| client secret 위치 | **백엔드만**(code 교환 서버 수행) | 클라 유출 벡터 제거. 방향 B 철학(§8.2) |
| 클라→서버 전달값 | **`authorization code` + `codeVerifier` + `redirectUri`**(code 교환 방식) | id_token 직접 검증보다 표준·견고. 트레이드오프 §4.3 |
| 계정 매핑 | 검증 email == 기존 계정 email **AND** `emailVerified=true` → 로그인 | item1a 유일성 강제(1 email=1 계정)로 1:1 매핑 성립(§6.2) |
| 자동 계정 생성 | **금지**(admin 선등록만) `[CONFIRM]` §6.3 | 통제된 키오스크. 임의 Google 계정 유입 차단 |
| 매칭 실패 처리 | **일반화된 안내**(email 존재 여부 비노출) + 로그만 상세 §6.4 | 열거 방지(item1a §12 철학 계승) |
| 신규 인증 상태 | **없음**(JWT 재사용, stateless 유지) §5.4 | 현행 세션 계약 무변경 — 리스크 최소 |
| SSO 노출 게이트 | **`UseBackend=true`일 때만** 버튼 노출 §7.1 | 레거시 Firebase 경로엔 SSO 인프라 없음(item1a `IsBackendMode` 패턴 계승) |

---

## 1. 스택 컨텍스트 (변경 없음, 확장만)

- **클라**: .NET 8 WPF, CommunityToolkit.Mvvm, `Microsoft.Extensions.DependencyInjection`, `IHttpClientFactory`. item1b는 `MCPhoto.Core`(신규 서비스 인터페이스)·`MCPhoto.App`(loopback 리스너·브라우저 실행·버튼)·`MCPhoto.Http`(신규 DTO·메서드)에 **파일 추가**만 하고, 기존 인프라(세션 홀더 `IBackendSession`, 에러 매핑 `MapToDomainException`, 오버레이 복귀)를 재사용한다.
- **서버**: Cloud Functions 2nd gen + TypeScript + Express, 단일 함수 `api`에 라우터 마운트(`web/functions/src/{index,app}.ts`). JWT(HS256, `domain/jwt.ts`), `requireApiKey`(`http/auth.ts:36-45`), `domain/*` 순수함수 + jest 관례. item1b는 여기에 `/auth/google` 라우트·google 검증 서비스·config를 **추가**한다.
- **신규 서버 의존** `[CONFIRM]`: `google-auth-library`(공식 Node SDK). `OAuth2Client`가 code 교환(`getToken`) + `id_token` 검증(`verifyIdToken`, Google 공개키 자동 캐시)을 모두 제공 → 공개키 fetch·JWK 파싱·서명 검증을 직접 구현하지 않음(보안 리스크 감소). 개발/테스트는 이 클라이언트를 mock으로 대체.
- **신규 클라 의존** `[CONFIRM]`: **없음**. loopback HTTP 리스너는 .NET 내장 `System.Net.HttpListener`, 브라우저 실행은 `Process.Start`(§7.3). PKCE 난수·SHA256은 `System.Security.Cryptography`. 외부 NuGet 불요.

---

## 2. 현행 로그인 흐름 실측 (확장 지점 식별)

| # | 현재 동작 | 근거 | item1b 확장점 |
|---|-----------|------|---------------|
| L1 | 로그인: `{id, password}` → 해시 검증 → `issueToken({id, role}, secret, expiresIn)` → `{token, expiresIn, user}` | `routes/auth.ts:40-68`, `services/accounts.ts:80-101`, `domain/jwt.ts:28-39` | 신규 `/auth/google`가 **동일한 JWT 발급 코드 재사용**(§5.4). login 라우트는 무변경 |
| L2 | `requireApiKey()`(X-MCPhoto-Client)가 로그인 전 엔드포인트 게이트 | `http/auth.ts:36-45`, `routes/auth.ts:41` | `/auth/google`도 **API 키 게이트**(로그인 전 상태, Bearer 불가) |
| L3 | email로 계정 조회: `findByIdOrEmail`(id 우선, 없으면 `where email==소문자`) | `services/accounts.ts:239-248` | Google email 매핑에 **재사용**(email 경로만). 소문자 정규화 정합 |
| L4 | email 유일성 강제(`ensureEmailUnique`), `emailVerified` 필드 | `services/accounts.ts:107-113`, `dto.ts:20-22` | 1 email=1 계정 → 매핑 1:1(§6.2). `emailVerified=true` 조건 추가 |
| L5 | `UserResponse{id, role, createdAt, email, emailVerified}` | `services/dto.ts:74-82`, `services/accounts.ts:30-38` | `/auth/google` 응답도 **동일 UserResponse** 재사용(신규 DTO 불요) |
| L6 | 클라 로그인: `HttpAccountService.LoginAsync` → `Session.SignIn(token, user)` | `HttpAccountService.cs:35-57` | 신규 `LoginWithGoogleAsync`가 **동일 패턴**(토큰·user → SignIn) |
| L7 | 로그인 UI: id/pw + "비밀번호 찾기"(백엔드 게이트) + 취소 | `Views/LoginGuestView.xaml:35-45`, `LoginGuestViewModel.cs:39` | "Google로 로그인" 버튼 추가(`IsBackendMode` 게이트 계승) |
| L8 | 로그인 성공 후 `_shell.Session.Login(user)` + `ReturnFromOverlay()` | `LoginGuestViewModel.cs:69-71` | Google 성공 후 **동일 복귀 흐름** |
| L9 | config: `defineSecret`로 시크릿 선언·주입, `loadConfig()` 캐시 | `index.ts:16-21`, `config.ts:49-103` | `GOOGLE_OAUTH_CLIENT_ID`·`GOOGLE_OAUTH_CLIENT_SECRET` 시크릿 추가 |

**핵심 관찰**: 로그인 성공 이후의 모든 것(JWT 발급, 세션 홀더, 역할 게이트, 복귀 흐름)은 이미 완성돼 있다. item1b는 **"자격 검증 방식(id/pw 대신 Google)"만 하나 더 얹는** 가산 변경이다. JWT·세션·인가 계약은 전부 보존한다.

---

## 3. 데스크톱 OAuth 플로우 (상세)

### 3.1 OAuth 클라이언트 유형 — Desktop app + 시스템 브라우저 + loopback + PKCE `[CONFIRM]`

Google은 데스크톱 앱에 대해 **"OAuth 2.0 for Mobile & Desktop Apps"**(Installed app, RFC 8252)를 규정한다. 두 가지 리디렉션 방식 중:

- **loopback IP 리디렉션**(`http://127.0.0.1:{port}` 또는 `http://localhost:{port}`) — **채택**.
- ~~Custom URI scheme~~ — Windows 레지스트리 프로토콜 핸들러 등록 필요(설치/권한 복잡), 키오스크에 부적합.

**embedded WebView(앱 내 웹뷰)에 Google 로그인 렌더링은 지양** `[CONFIRM]`: Google은 embedded user-agent에서의 OAuth를 정책적으로 차단/경고할 수 있고(피싱 방어), 사용자의 기존 브라우저 세션(SSO)을 활용 못 한다. **시스템 기본 브라우저**를 `Process.Start`로 여는 방식이 표준·안전(RFC 8252 §8.12).

- **client secret은 클라에 두지 않는다.** Installed app의 client secret은 본질적으로 비밀이 아니지만(추출 가능), 방향 B 철학상 **code 교환을 백엔드가 수행**하고 secret을 백엔드 시크릿에만 둔다(§8.2). 클라는 **PKCE**로 code 가로채기를 방어한다.

> **키오스크 주의** `[USER-DECISION-REQUIRED]` §11-3: 전체화면 키오스크 환경에서 "시스템 브라우저를 띄운다"는 것은 키오스크 잠금(브라우저 접근 차단) 정책과 충돌할 수 있다. SSO는 **운영자 로그인** 시점에만 쓰이므로(게스트 흐름 아님) 대개 문제없으나, 브라우저가 앱 위에 뜨고 복귀하는 UX·포커스 처리를 §7.4에서 다룬다. 브라우저가 완전히 봉쇄된 키오스크라면 SSO 대신 id/pw를 쓰도록 운영 결정 필요.

### 3.2 전체 시퀀스 다이어그램

```
[WPF 앱]                          [시스템 브라우저]        [Google]              [백엔드 /auth/google]
   │                                                                                    
   │ (1) codeVerifier 난수 생성                                                          
   │     codeChallenge = BASE64URL(SHA256(codeVerifier))                                 
   │     state 난수, nonce 난수                                                          
   │ (2) HttpListener 시작:                                                              
   │     http://127.0.0.1:{빈 포트 자동 할당}/                                           
   │ (3) authorize URL 조립 →  Process.Start(브라우저)                                   
   │        ────────────────────▶ (4) GET accounts.google.com/o/oauth2/v2/auth          
   │                                    ?client_id=...&redirect_uri=http://127.0.0.1:P   
   │                                    &response_type=code&scope=openid email profile   
   │                                    &code_challenge=...&code_challenge_method=S256   
   │                                    &state=...&nonce=...                             
   │                                        ──────────────────▶ (5) 사용자 동의/로그인   
   │                                        ◀────────────────── (6) 302 → 127.0.0.1:P    
   │                                                                    ?code=AUTH&state 
   │ ◀──────── (7) 브라우저가 loopback으로 code 전달 ───────────                          
   │     HttpListener가 code·state 수신 → state 대조 → "로그인 완료, 창을 닫아도 됩니다"  
   │        응답 후 리스너 종료                                                          
   │                                                                                    
   │ (8) POST /auth/google {code, codeVerifier, redirectUri, nonce}                     
   │        ─────────────────────────────────────────────────────────▶ (9) code 교환    
   │                                                            OAuth2Client.getToken     
   │                                                            (client_secret 백엔드)    
   │                                                            ◀── {id_token, ...}       
   │                                                            (10) verifyIdToken:       
   │                                                                 aud==client_id       
   │                                                                 iss==accounts.google 
   │                                                                 exp>now, nonce 일치   
   │                                                                 email_verified==true 
   │                                                            (11) email → findByIdOr   
   │                                                                 Email → 계정 매핑     
   │                                                                 (emailVerified==true) 
   │                                                            (12) issueToken(JWT)       
   │ ◀───────────── (13) 200 {token, expiresIn, user} ─────────────────                  
   │     Session.SignIn(token, user) → 복귀                                              
```

### 3.3 loopback 리스너 세부 `[CONFIRM]`

- **포트**: OS 자동 할당(포트 0 바인딩 후 실제 포트 조회). 고정 포트는 충돌·선점 위험 → 무작위 사용. 단, Google 콘솔의 "승인된 리디렉션 URI"는 **loopback은 포트 무시**(RFC 8252 §7.3, Google 문서)이므로 콘솔엔 `http://127.0.0.1`(또는 `http://localhost`)만 등록하면 임의 포트 허용 → §10 CONSOLE.
- **주소**: `127.0.0.1` 권장(일부 환경에서 `localhost`가 IPv6 `::1`로 해석되는 문제 회피) `[CONFIRM]`. redirectUri는 클라가 조립한 실제값(`http://127.0.0.1:{port}/`)을 백엔드로 그대로 전달해 code 교환 시 동일값 사용(§4.2).
- **수명**: 리스너는 **1회 요청 수신 후 즉시 종료**(단일 콜백만 받음). 타임아웃(예: 2~3분) 초과 시 취소·정리(§8.5). `CancellationToken`으로 사용자 취소·타임아웃 모두 처리.
- **응답 페이지**: 브라우저에 표시할 최소 HTML("로그인이 완료되었습니다. 이 창을 닫고 앱으로 돌아가세요."). 앱으로 포커스 복귀는 §7.4.
- **스레딩**: `HttpListener.GetContextAsync()`는 비동기. UI 스레드 블로킹 금지 — 전 과정 `async`/`await`, UI 갱신은 Dispatcher 경유(§7.5).

---

## 4. 백엔드: id_token 직접 검증 vs code 교환 (택1 + 트레이드오프)

### 4.1 선택지 A — Authorization Code 교환 (백엔드가 code→token) **채택** `[CONFIRM]`

- 클라가 브라우저에서 받은 **`authorization code`**를 백엔드로 전달. 백엔드가 Google **token endpoint**에 `{code, client_id, client_secret, redirect_uri, code_verifier, grant_type=authorization_code}`로 교환 → `id_token`(+access/refresh) 수신 → `id_token` 검증.
- **장점**: (a) client secret이 교환에 관여해 **기밀 클라이언트(confidential) 수준 보안** 근접, (b) code는 1회성·단수명이라 유출돼도 PKCE 없이 재사용 불가, (c) 표준 웹 플로우와 동일해 라이브러리 지원 견고(`OAuth2Client.getToken`).
- **단점**: 왕복 1회 추가(브라우저→클라→백엔드→Google). 키오스크 규모에선 무시 가능.

### 4.2 code 교환 요청 계약(백엔드 내부)

- `redirect_uri`는 **클라가 실제로 쓴 loopback 주소**와 **정확히 일치**해야 교환 성공(Google 검증). → 클라가 `redirectUri`를 요청 본문에 담아 전달(§7.6). 백엔드는 이를 그대로 `getToken`에 넘긴다. `[CONFIRM]` 백엔드는 redirectUri가 `http://127.0.0.1:{port}/` 또는 `http://localhost:{port}/` loopback 형태인지 **형식 검증**(SSRF/오용 방지) 후 사용.
- `code_verifier`(PKCE)도 클라 → 백엔드 전달. 백엔드가 교환 시 함께 전송.

### 4.3 선택지 B — id_token 직접 검증 (클라가 id_token 획득, 백엔드는 검증만) — 비채택

- `response_type`에 `id_token` 포함(implicit/hybrid) 또는 클라가 직접 token endpoint 호출해 `id_token`만 백엔드로 전달 → 백엔드는 `verifyIdToken`만.
- **장점**: 백엔드 code 교환 왕복 없음, client secret 불요(순수 public 클라이언트).
- **단점**: (a) implicit 흐름은 Google이 사실상 **deprecated**(권장 안 함), (b) 클라가 token endpoint를 직접 치려면 client secret이 클라에 필요해지거나(모순) PKCE public 교환이 되어 **백엔드 통제 상실**, (c) refresh/추가 검증 여지 감소.
- **결론**: 방향 B(백엔드 중심·secret 클라 미보관) 철학상 **A 채택**. B는 "백엔드가 검증만 하는 초경량"이 필요할 때의 대안으로만 기록.

> **하이브리드 참고**: A를 채택하되, 백엔드는 code 교환 결과의 **`id_token`을 반드시 `verifyIdToken`으로 재검증**한다(§5.3). 즉 "code 교환(A) + id_token 검증(B의 핵심)"을 모두 수행 — Google token endpoint 응답도 신뢰 전 검증하는 방어적 이중화.

---

## 5. 백엔드 엔드포인트 `/auth/google` (요청/응답·검증)

### 5.1 계약

| 항목 | 값 |
|------|-----|
| 경로 | `POST /auth/google` (라우터 마운트상 실제 URL `.../api/auth/google`) |
| 인증 | **API 키**(`requireApiKey()`) — 로그인 전 상태, Bearer 불가. `routes/auth.ts:41`(login)과 동일 게이트 |
| 요청 | `{ code: string, codeVerifier: string, redirectUri: string, nonce?: string }` |
| 성공 | 200 `{ token, expiresIn, user }` — **login과 동일 형식**(`routes/auth.ts:62-66`) |
| 실패(매핑 없음/미검증) | **401** `{error:{code, message}}` — 일반화된 메시지("Google 계정으로 로그인할 수 없습니다."), 사유 비노출(§6.4) |
| 실패(입력 형식) | 400(code/verifier/redirectUri 형식 오류) |
| 실패(Google 오류) | 502/503 계열 또는 401 — 상세는 로그만(§8.6) |

### 5.2 입력 검증(신규 순수 함수, `domain/validation.ts` 추가) `[CODE]`

```
validateAuthCode(value): ValidationResult<string>       // 비어있지 않은 문자열, 과길이 방어(≤2048)
validateCodeVerifier(value): ValidationResult<string>   // RFC 7636: 43~128자, [A-Za-z0-9-._~]
validateLoopbackRedirectUri(value): ValidationResult<string>
    // http://127.0.0.1:{port}/ 또는 http://localhost:{port}/ 형태만 허용(SSRF/오용 차단)
```

- 기존 `validateEmail`/`validateAccountId`(`domain/validation.ts`) 옆에 추가. jest 테스트 동반(§9 완료기준).
- `nonce`는 있으면 검증(§5.3), 없으면 nonce 검증 생략 `[CONFIRM]`(state로 CSRF는 이미 클라가 방어 — nonce는 id_token 재생(replay) 추가 방어). **기본안: nonce 필수화 권장**(§8.4) — 리뷰 시 조정.

### 5.3 검증 로직(`services/googleAuth.ts` 신규) `[CODE]`

순수 검증 규칙(라이브러리에 위임하되 명세):

1. **code 교환**: `OAuth2Client({clientId, clientSecret, redirectUri}).getToken({code, codeVerifier})` → `tokens.id_token` 획득. 실패 시 401(로그만 상세).
2. **id_token 검증**: `client.verifyIdToken({idToken, audience: clientId})` → `payload` 획득. 라이브러리가 **서명(Google 공개키, 자동 캐시)·`exp`·`iss`**를 검증. 추가로 코드에서 재확인:
   - `payload.aud === GOOGLE_OAUTH_CLIENT_ID` (audience)
   - `payload.iss ∈ {"https://accounts.google.com", "accounts.google.com"}`
   - `payload.exp > now` (라이브러리도 검증하나 방어적 재확인)
   - `payload.email_verified === true` — **Google이 email 소유를 확인했는지**(미확인 email은 거부, §6.2)
   - `payload.nonce === 요청 nonce` (요청에 nonce가 있으면; §8.4)
   - `payload.hd`(hosted domain) 검증은 **선택**(§6.5 `[USER-DECISION-REQUIRED]` — 특정 Workspace 도메인만 허용할지)
3. **email 추출**: `payload.email`(소문자 정규화 — `findByIdOrEmail`이 소문자 비교, `services/accounts.ts:244`).

> **트레이드오프 재기**: 위 2는 code 교환(A)의 산출물인 id_token을 **다시 verifyIdToken으로 검증**(A+B 이중화). Google token endpoint 응답을 무조건 신뢰하지 않는 방어. 라이브러리 미사용 직접 구현은 JWK fetch·회전·서명 검증 리스크가 커 **비권장**.

### 5.4 계정 매핑 후 JWT 발급 — 기존 코드 재사용 `[CODE]`

- email 매핑 성공 시(§6.2) → 매핑된 계정의 `{id, role}`로 **기존** `issueToken(principal, cfg.jwtSecret, cfg.jwtExpiresInSeconds)`(`domain/jwt.ts:28-39`) 호출. **login과 완전히 동일**한 JWT(같은 클레임·만료).
- 응답도 login과 동일: `{token, expiresIn, user: toResponse(doc)}`(`services/accounts.ts:30-38`). 클라는 Google 로그인인지 id/pw 로그인인지 **구분할 필요 없음** — 세션·인가 계약 무변경(신규 인증 상태 0).

### 5.5 서비스 함수 배치

| 파일 | 신규/수정 | 책임 |
|------|-----------|------|
| `web/functions/src/domain/validation.ts` | 수정 `[CODE]` | `validateAuthCode`·`validateCodeVerifier`·`validateLoopbackRedirectUri` 추가 |
| `web/functions/src/services/googleAuth.ts` | 신규 `[CODE]` | `OAuth2Client`로 code 교환 + `id_token` 검증 → 검증된 email 반환. Google SDK 의존을 이 파일에 격리 |
| `web/functions/src/services/accounts.ts` | 수정 `[CODE]` | `loginWithGoogleEmail(email)` 추가: `findByIdOrEmail`(email 경로) + `emailVerified===true` 확인 → `LoginResult`(§6.2). 자동 생성 없음 |
| `web/functions/src/routes/auth.ts` | 수정 `[CODE]` | `POST /auth/google` 추가(검증→매핑→issueToken). 실패 일반화(§6.4) |
| `web/functions/src/config.ts` | 수정 `[CODE]` | `GOOGLE_OAUTH_CLIENT_ID`·`GOOGLE_OAUTH_CLIENT_SECRET` 로드(§8.2·§10) |
| `web/functions/src/index.ts` | 수정 `[CODE]` | `GOOGLE_OAUTH_CLIENT_SECRET` `defineSecret` 선언 + `api` 함수 `secrets:` 배열 추가(`index.ts:17-34` 패턴) |

---

## 6. 계정 매핑 정책 (상세)

### 6.1 원칙 — Google는 "누구인지"만, 역할은 MCPhoto가 정한다

- Google `id_token`은 **email 소유 증명**일 뿐 역할(user/manager/admin)을 담지 않는다. 역할은 **매핑된 MCPhoto 계정**(`users/{id}.role`)에서 온다(`services/accounts.ts:99`). Google 계정이 어떤 MCPhoto 계정에도 매핑되지 않으면 **로그인 불가**(§6.3).

### 6.2 매핑 규칙 — email 일치 + emailVerified `[CONFIRM]`

```
검증된 Google email
  → findByIdOrEmail(email)  (id 경로는 email 형식이라 실패, email 경로로 매칭)  services/accounts.ts:239-248
  → 계정 존재 AND doc.email == email(소문자) AND doc.emailVerified === true
      → LoginResult{id, role, user: toResponse(doc)}  → JWT 발급
  → 그 외(계정 없음 / emailVerified=false / email 불일치)
      → null → 라우트가 401 일반화 응답(§6.4)
```

- **item1a 유일성 강제**(`ensureEmailUnique`, `services/accounts.ts:107-113`)로 한 email은 최대 1계정 → 매핑이 항상 1:1(모호성 없음).
- **`emailVerified=true` 요구 근거**: MCPhoto 계정의 email이 소유 확인된 상태여야 Google email과 안전히 동일시 가능. admin이 넣기만 하고 미검증(item1a §7-2)인 email은 매핑 대상에서 제외 → SSO로 우회 로그인 방지.

> **주의(정책 정합)** `[CONFIRM]`: item1a는 "MCPhoto 자체 email 인증(코드/링크)"으로 `emailVerified=true`가 된다. Google `email_verified=true`(id_token)만으로 MCPhoto의 `emailVerified`를 **자동 승격시키지 않는다**(§6.6). 즉 SSO 로그인의 **전제**는 "이미 MCPhoto에서 검증된 email을 가진 계정"이다. 검증 안 된 계정은 먼저 item1a 경로로 email을 인증해야 SSO 사용 가능.

### 6.3 자동 계정 생성 — **금지** `[CONFIRM]` (기본안)

- 매핑 실패 시 **새 계정을 만들지 않는다**. 근거: (a) 통제된 키오스크 운영 — 임의 Google 계정이 로그인되면 안 됨, (b) 역할을 부여할 근거 없음(자동 생성 시 최소 권한 user? 정책 부재), (c) admin이 계정을 **선등록**하고 email을 검증해두는 것이 운영 모델(item1a).
- `[USER-DECISION-REQUIRED]` §11-1: 만약 "허용 도메인(hd) 내 임의 Google 계정은 user로 자동 프로비저닝" 정책을 원하면 별도 결정 필요(§6.5의 hd 검증과 결합). **기본안은 금지.**

### 6.4 매칭 실패 안내 — 열거 방지(item1a 철학 계승) `[CONFIRM]`

- 서버 401 응답 메시지는 **일반화**: "이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요." — email이 계정으로 등록됐는지/검증됐는지 **구분 노출 금지**(item1a §12 열거 방지 계승).
- 상세 사유(계정 없음 / 미검증 / hd 불일치)는 **서버 로그에만**(email·토큰은 로그에 남기지 않음 — §8.6).
- 클라 UI도 동일 일반 메시지 표시(§7.7).

### 6.5 hosted domain(hd) 제한 `[USER-DECISION-REQUIRED]` §11-2

- Google Workspace 조직 도메인으로 제한할지(예: `@rsupport.com`만 허용). `id_token.payload.hd` 검증 또는 authorize 요청에 `hd` 파라미터.
- **기본안: 제한 없음**(email 매핑 자체가 화이트리스트 역할 — 등록된 계정 email만 통과). 조직 정책상 도메인 강제가 필요하면 config에 `GOOGLE_ALLOWED_HD` 추가.

### 6.6 SSO ↔ item1a emailVerified 관계 정리(요약표)

| 상황 | SSO 로그인 결과 |
|------|-----------------|
| MCPhoto 계정 있음 + `emailVerified=true` + Google email 일치 + `email_verified=true` | **로그인 성공** |
| MCPhoto 계정 있음 + `emailVerified=false`(admin이 넣기만 함) | **실패**(먼저 item1a로 email 인증 필요) |
| MCPhoto 계정 없음(email 미등록) | **실패**(admin 선등록 필요, 자동생성 금지) |
| Google `email_verified=false`(드묾) | **실패**(§5.3-2) |

---

## 7. 클라이언트 (WPF) 변경

### 7.1 노출 게이트 — 백엔드 모드 전용 (item1a `IsBackendMode` 패턴 계승)

- "Google로 로그인" 버튼은 **`UseBackend=true`일 때만** 노출. 레거시 Firebase 경로엔 `/auth/google`가 없다. `LoginGuestViewModel.IsBackendMode`(`LoginGuestViewModel.cs:39`)와 동일한 게이트 프로퍼티 재사용(신규 프로퍼티 불요 — 이미 존재).
- 서버 미연결(`IsServerOffline`, `LoginGuestViewModel.cs:33`)일 때는 SSO도 무의미하므로 버튼 비활성/숨김 `[CONFIRM]`(id/pw와 동일 취급 — 배너로 안내).

### 7.2 신규 서비스 인터페이스 — `IGoogleSignInService` (`MCPhoto.Core`) `[CODE]`

브라우저 실행·loopback 리스너·PKCE는 **UI·플랫폼 로직**이므로 ViewModel에서 분리하고 서비스로 추상화(테스트 가능성·MVVM 순수성). ViewModel은 `System.Net`/`Process`에 직접 의존하지 않는다.

```csharp
namespace MCPhoto.Core.Accounts;

/// <summary>
/// Google OAuth(데스크톱: 시스템 브라우저 + loopback + PKCE) 상호작용 추상화.
/// 구현(MCPhoto.App)이 HttpListener·Process.Start·PKCE를 담당하고,
/// authorization code + PKCE verifier + 실제 redirectUri를 반환한다.
/// 백엔드 교환·검증은 IAccountService.LoginWithGoogleAsync가 수행한다(관심사 분리).
/// </summary>
public interface IGoogleSignInService
{
    /// <summary>
    /// 시스템 브라우저로 Google 동의 화면을 열고 loopback으로 code를 수신한다.
    /// 사용자 취소·타임아웃·state 불일치는 예외 또는 null 결과로 신호한다.
    /// </summary>
    Task<GoogleAuthCodeResult?> AcquireAuthorizationCodeAsync(CancellationToken ct = default);
}

/// <summary>loopback으로 수신한 결과(백엔드 /auth/google 요청 재료). 토큰·비밀 아님.</summary>
public sealed class GoogleAuthCodeResult
{
    public string Code { get; init; } = "";
    public string CodeVerifier { get; init; } = "";   // PKCE verifier(백엔드가 교환 시 사용)
    public string RedirectUri { get; init; } = "";    // 실제 사용한 http://127.0.0.1:{port}/
    public string? Nonce { get; init; }               // id_token nonce 검증용(§8.4)
}
```

- **client_id는 어디서?** `[CONFIRM]`: authorize URL 조립에 `client_id`가 필요하다. client_id는 **비밀이 아니므로**(client secret과 구분) 클라가 알아야 한다. 두 안:
  - (a) **설정(INI)에 `GoogleClientId` 추가**(`AppSettings`) — 배포별 값 주입. base URL/API 키와 동일 관례(§7.6·`AppSettings.cs:123-140`).
  - (b) 백엔드에 `GET /auth/google/config`로 client_id·scope를 받아옴(런타임 조회) — 왕복 추가·설정 단순화.
  - **기본안 (a)**: 설정에 `GoogleClientId` 필드 추가. base URL·API 키가 이미 배포별 설정이므로 일관. client_secret은 절대 클라에 없음.

### 7.3 loopback 리스너 구현 요지 (`GoogleSignInService`, `MCPhoto.App`) `[CODE]`

- `HttpListener` 시작: `http://127.0.0.1:{port}/` — 포트는 사전 확보(빈 포트 찾기: 임시 `TcpListener`로 포트 0 바인딩→포트 조회→해제→그 포트로 HttpListener). `[CONFIRM]` 경합 창(race) 최소화 위해 확보 즉시 HttpListener 시작.
- PKCE: `codeVerifier` = 32바이트 난수 base64url(43~128자 충족), `codeChallenge` = base64url(SHA256(verifier)). `RandomNumberGenerator`·`SHA256`(System.Security.Cryptography).
- `state`·`nonce` = 각각 난수 base64url. authorize URL 조립(§3.2 (4)). `Uri.EscapeDataString`로 파라미터 인코딩.
- `Process.Start(new ProcessStartInfo(authorizeUrl){ UseShellExecute = true })`로 **시스템 기본 브라우저** 오픈(`UseShellExecute=true` 필수 — .NET Core에서 URL 오픈).
- `await listener.GetContextAsync()`(CancellationToken 연동: `ct.Register(listener.Stop)`) → 쿼리에서 `code`·`state` 파싱 → **state 대조**(불일치 시 거부) → error 파라미터(`error=access_denied` 등) 처리 → 브라우저에 완료 HTML 응답 → 리스너 종료.
- 타임아웃: `CancellationTokenSource(TimeSpan.FromMinutes(3))`와 사용자 취소 CT를 `CreateLinkedTokenSource`로 결합.
- **정리 보장**: `try/finally`로 `listener.Close()` 항상 호출(리스너 누수·포트 점유 방지, §8.5). `HttpListener`는 `IDisposable`.

### 7.4 UX·포커스 (브라우저 ↔ 앱 전환) `[CONFIRM]`

- 버튼 클릭 → "브라우저에서 Google 로그인을 진행하세요…" 진행 표시(`IsBusy`), 취소 버튼 노출(CT 취소).
- 브라우저 로그인 완료 후 앱으로 복귀: loopback 응답 HTML에 "이 창을 닫고 앱으로 돌아가세요" 안내. 앱은 code 수신 즉시 자동 진행(사용자가 앱을 다시 클릭할 필요 없음). 앱 창을 전면으로 가져오는 `Activate()` 호출은 코드비하인드/윈도우 서비스에서 최소 처리 `[CONFIRM]`.
- 키오스크 전체화면에서 브라우저가 위에 뜨는 문제는 §11-3(운영 결정).

### 7.5 스레딩(엄수)

- `AcquireAuthorizationCodeAsync`·`LoginWithGoogleAsync`는 전부 `async`. **UI 스레드 블로킹 금지**(`HttpListener`·HTTP 왕복은 백그라운드). ViewModel 커맨드는 `AsyncRelayCommand` 또는 `[RelayCommand] async Task`(`LoginGuestViewModel.cs:51-79` 패턴).
- 결과 반영(`Session.Login`·에러 메시지·`IsBusy`)은 **UI 스레드**에서(커맨드가 UI 스레드에서 시작되므로 `await` 이후 컨텍스트 복귀로 자동 보장 — `ConfigureAwait` 없이 VM에서 대기). `BackendSession`은 이미 락 보호(`BackendSession.cs:11-41`)라 스레드 안전.

### 7.6 `IAccountService` 확장 + `HttpAccountService` 구현 `[CODE]`

`IAccountService`(`src/MCPhoto.Core/Accounts/IAccountService.cs`)에 추가:

```csharp
/// <summary>
/// Google SSO 로그인. 브라우저에서 받은 authorization code(+PKCE verifier·redirectUri)를
/// 백엔드로 전달해 code 교환·id_token 검증·계정 매핑을 거쳐 JWT를 받는다.
/// 매핑 실패(등록 안 됨/미검증)는 null(현행 LoginAsync 계약과 정합). — HTTP 전용.
/// </summary>
Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
    string? nonce = null, CancellationToken ct = default);
```

`HttpAccountService`(`HttpAccountService.cs:35-57` LoginAsync 패턴 그대로):

```csharp
public async Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
    string? nonce = null, CancellationToken ct = default)
{
    try
    {
        var res = await SendJsonAsync<LoginResponse>(
            HttpMethod.Post, "auth/google",
            new GoogleLoginRequest { Code = code, CodeVerifier = codeVerifier, RedirectUri = redirectUri, Nonce = nonce },
            bearer: false, ct).ConfigureAwait(false);   // API키 게이트(bearer 불요)

        var user = ToUser(res.User) ?? new User { Id = "", Role = UserRole.User };
        Session.SignIn(res.Token, user);
        return user;
    }
    catch (BackendException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
    {
        return null;   // 매핑 실패 = null(§6.4). LoginAsync 401 처리와 동일 계약
    }
    catch (BackendException ex) { throw MapToDomainException(ex); }
}
```

- 신규 DTO(`AccountDtos.cs` 추가): `GoogleLoginRequest{Code, CodeVerifier, RedirectUri, Nonce?}`. 응답은 **기존 `LoginResponse`**(`AccountDtos.cs:11-16`) 재사용.
- **레거시 `AccountService`(Firebase, OFF 경로)**: `LoginWithGoogleAsync`는 `NotSupportedException` 또는 로그 후 null `[CONFIRM]`(HTTP 전용 — SSO 버튼이 백엔드 모드에서만 노출되므로 실제 호출되지 않음. 인터페이스 정합 위해 최소 구현). item1a의 `SetEmailAsync` 등 HTTP 전용 메서드 처리와 동일 방침.
- 설정(`AppSettings.cs`)에 `GoogleClientId`(string, 기본 "") 추가 — §7.2(a). `Clamp`/`Clone`에 반영. 빈 값이면 SSO 버튼 비활성(client_id 없이는 authorize URL 조립 불가) `[CONFIRM]`.

### 7.7 로그인 화면·ViewModel (`LoginGuestView`/`LoginGuestViewModel`) `[CODE]`

- **XAML**(`LoginGuestView.xaml`): "비밀번호 찾기" 버튼(`LoginGuestView.xaml:40-42`) 인근에 **"Google로 로그인"** 버튼 추가. `Button.Ghost` 또는 전용 스타일, `Visibility="{Binding IsBackendMode, Converter={StaticResource BoolToVis}}"`(기존 패턴 계승), `IsEnabled` = `IsBusy` 반전 & client_id 존재.
- **ViewModel**(`LoginGuestViewModel`): 생성자에 `IGoogleSignInService` 주입(DI). 신규 커맨드:

```csharp
[RelayCommand]
private async Task LoginWithGoogle()
{
    if (IsBusy) return;
    ErrorMessage = string.Empty;
    IsBusy = true;
    try
    {
        var codeResult = await _googleSignIn.AcquireAuthorizationCodeAsync(/*ct*/);
        if (codeResult is null)           // 사용자 취소/타임아웃/state 불일치
        {
            ErrorMessage = "Google 로그인이 취소되었습니다.";
            return;
        }
        var user = await _accounts.LoginWithGoogleAsync(
            codeResult.Code, codeResult.CodeVerifier, codeResult.RedirectUri, codeResult.Nonce);
        if (user is null)                 // 매핑 실패(§6.4 일반화)
        {
            ErrorMessage = "이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요.";
            return;
        }
        _shell.Session.Login(user);       // 기존 로그인 성공 경로(LoginGuestViewModel.cs:69)
        await _shell.ReturnFromOverlay(); // 직전 화면 복귀(동일)
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Google 로그인 실패");
        ErrorMessage = "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.";
    }
    finally { IsBusy = false; }
}
```

- 성공/복귀는 id/pw 로그인과 **완전히 동일**(`Session.Login` + `ReturnFromOverlay`). 신규 상태·화면 불요.

### 7.8 DI 등록 (`ServiceRegistration`) `[CODE]`

- `IGoogleSignInService` → `GoogleSignInService`(MCPhoto.App) **Singleton** 등록(`ServiceRegistration.cs` 다이얼로그 서비스들과 동일 위치, 예: `services.AddSingleton<IGoogleSignInService, GoogleSignInService>()`).
- `GoogleSignInService`는 `ISettingsService`(client_id·scope)와 `ILogger`를 주입받는다. `IAccountService`는 이미 팩토리 분기 등록됨(`ServiceRegistration.cs:145-159`) — `LoginWithGoogleAsync`는 그 구현(HttpAccountService)이 처리.
- `LoginGuestViewModel`은 Transient(`ServiceRegistration.cs:166`) — 생성자 파라미터에 `IGoogleSignInService` 추가되면 DI가 자동 주입.

### 7.9 파일 인코딩·관례(엄수)

- 기존 `.cs`는 **UTF-8 no BOM**, XAML은 프로젝트 관례, TS는 웹 관례(ESM, 2-space). 신규/수정 파일 **현재 파일 인코딩 유지**. file-scoped namespace·nullable enable·XML doc 한글 주석(C#), 한글 JSDoc(TS) 관례 따름(item1a 문서 §9.6 계승).

---

## 8. 보안 (엄수)

| 위협 | 방어 | 근거/구현 |
|------|------|-----------|
| authorization code 가로채기 | **PKCE(S256)** — codeVerifier는 클라 메모리에만, challenge만 URL에 노출 | §3.3·§7.3, RFC 7636 |
| client secret 유출 | **클라에 secret 미보관**, code 교환은 백엔드(시크릿 Secret Manager) | §4.1·§8.2 |
| CSRF(응답 위조) | **state** 난수 생성·loopback 콜백에서 대조(불일치 거부) | §3.3·§7.3 |
| id_token 재생(replay) | **nonce** 난수를 authorize에 실어 id_token payload와 대조 | §5.3-2·§8.4 |
| id_token 위조 | Google 공개키 **서명 검증**(라이브러리) + `aud`/`iss`/`exp` 코드 재확인 | §5.3-2 |
| 미확인 email 신뢰 | `id_token.email_verified===true` 강제 + MCPhoto `emailVerified===true` | §5.3-2·§6.2 |
| 임의 Google 계정 로그인 | **자동 생성 금지**, 등록·검증된 계정 email만 매핑 | §6.2·§6.3 |
| 계정 열거 | 매핑 실패 **일반화 401**, 사유 로그만 | §6.4 |
| loopback 리스너 오용 | 127.0.0.1 바인딩(외부 접근 불가), **1회성**·타임아웃·항상 정리 | §3.3·§7.3·§8.5 |
| redirectUri 오용(SSRF) | 백엔드가 loopback 형식만 허용(`validateLoopbackRedirectUri`) | §4.2·§5.2 |
| 토큰/시크릿 로그 노출 | code·id_token·JWT·email을 **로그에 남기지 않음**(현행 관례) | `HttpBackendClient` 주석, §8.6 |

### 8.2 시크릿·설정 위치

- `GOOGLE_OAUTH_CLIENT_SECRET`: **Secret Manager**(`firebase functions:secrets:set`) + `index.ts` `defineSecret`·`secrets:` 배열(`index.ts:17-34` 패턴). **코드/리포/`.env`(프로덕션) 하드코딩 금지**(`config.ts` 관례, JWT_SECRET과 동일).
- `GOOGLE_OAUTH_CLIENT_ID`: 비밀 아님 → 함수 env/param(`STORAGE_BUCKET`과 동일 취급). 단 코드 하드코딩 대신 env 로드(`config.ts`).
- 클라 `GoogleClientId`: INI 설정(배포별 주입, 비밀 아님). client secret은 클라 어디에도 없음.

### 8.4 nonce 필수화 `[CONFIRM]`

- **기본안: nonce 필수**. 클라가 authorize에 nonce를 싣고, `/auth/google` 요청에 동일 nonce를 담아 백엔드가 `id_token.nonce`와 대조. code 교환(A)에선 code 자체가 1회성이라 replay 위험이 낮지만, id_token 검증(B 이중화)의 완전성을 위해 nonce도 검증. 리뷰 시 "state만으로 충분" 판단 시 완화 가능.

### 8.5 리스너 생명주기(누수 방지)

- `HttpListener`·`CancellationTokenSource`는 `using`/`try-finally`로 **항상 Dispose/Close**. `ct.Register(listener.Stop)`로 취소 시 `GetContextAsync` 즉시 해제. 포트 점유·핸들 누수 0.
- 이벤트 구독 없음(리스너는 폴링형 `GetContextAsync`) → 이벤트 누수 표면 없음.

### 8.6 로그 정책

- code·codeVerifier·id_token·access_token·JWT·email·nonce는 **로그 금지**. 실패 시 "google 검증 실패(사유코드)" 수준만(email·토큰 미포함). Google SDK가 상세 오류를 던지면 message만 로깅하되 토큰 substring 미노출.

---

## 9. `[CODE]` / `[CONSOLE]` 분리 (§10 USER-ACTIONS §B2 확장)

### 9.1 `[CODE]` (내가 구현)

- **서버**: `domain/validation.ts`(code·verifier·loopback URI 검증 추가)·`services/googleAuth.ts`(신규, code 교환+id_token 검증)·`services/accounts.ts`(`loginWithGoogleEmail` 추가)·`routes/auth.ts`(`POST /auth/google`)·`config.ts`(client id/secret 로드)·`index.ts`(defineSecret·secrets 배열). jest(validation·googleAuth mock·accounts 매핑 경로) + Emulator 통합(OAuth2Client mock으로 성공/실패/미매핑 검증).
- **클라**: `IAccountService`(`LoginWithGoogleAsync`)·`HttpAccountService`(구현)·`AccountDtos.cs`(`GoogleLoginRequest`)·`IGoogleSignInService`+`GoogleAuthCodeResult`(MCPhoto.Core)·`GoogleSignInService`(MCPhoto.App: HttpListener·Process.Start·PKCE·state·nonce)·`LoginGuestViewModel`(`LoginWithGoogleCommand`)·`LoginGuestView.xaml`(버튼)·`AppSettings`(`GoogleClientId`)·`ServiceRegistration`(DI). 레거시 `AccountService` 최소 대응(NotSupported/null).
- **문서**: `docs/USER-ACTIONS.md §B2` 채움(아래 §10), `docs/analysis/60-auth-accounts-and-roles.md`에 SSO 플로우 추가, `docs/design/backlog-post-backend-migration.md` item1b 앵커 갱신.

### 9.2 `[CONSOLE]` (사용자 수동 — USER-ACTIONS §B2 확장)

> `docs/USER-ACTIONS.md §B2`(현재 자리표시자, `USER-ACTIONS.md:86-87`)를 아래로 채운다. 모두 콘솔/CLI/외부계정 작업.

- **B2-1. OAuth 동의 화면(OAuth consent screen) 구성** `[ ]`: Google Cloud 콘솔 → APIs & Services → OAuth consent screen. User Type(내부 Workspace면 Internal, 외부면 External), 앱 이름·지원 이메일·로고, scope(`openid`, `email`, `profile`) 추가. External이면 테스트 사용자 등록 또는 게시(verification) 필요.
- **B2-2. OAuth 2.0 클라이언트 ID 생성(Desktop app)** `[ ]`: Credentials → Create Credentials → OAuth client ID → **Application type: Desktop app**. 생성 후 **Client ID**와 **Client Secret** 확보.
  - ⚠️ Desktop 클라이언트는 loopback 리디렉션에서 **포트를 무시**하므로 별도 리디렉션 URI 등록 불필요할 수 있으나, 콘솔이 요구하면 `http://127.0.0.1`·`http://localhost` 등록(포트 없이). (Web application 유형이 아님에 주의 — Web은 정확한 URI·포트 매칭을 요구.)
- **B2-3. 백엔드 시크릿 등록** `[ ]`: `cd web/functions` →
  - `firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET` (B2-2의 Client Secret)
  - `GOOGLE_OAUTH_CLIENT_ID`는 비밀 아님 → 함수 env/param에 설정(또는 시크릿으로 통일해도 무방). **코드/리포에 하드코딩 금지.**
  - ⚠️ `index.ts`에 `defineSecret("GOOGLE_OAUTH_CLIENT_SECRET")`가 선언되면 **모든 배포에서 존재해야** 하므로, SSO를 아직 안 켜더라도 최초 배포 전 임시값이라도 등록(SENDGRID_API_KEY와 동일 주의, `USER-ACTIONS.md:24`).
- **B2-4. 클라이언트 설정(배포 PC INI)** `[ ]`: 대상 PC의 `MCPhoto.ini` `[MCPhoto]`에 `GoogleClientId=<B2-2 Client ID>` 추가. (client secret은 클라에 **넣지 않음** — 백엔드 전용.)
- **B2-5. (선택) 허용 도메인(hd) 제한** `[ ]` `[USER-DECISION-REQUIRED]`: 특정 Workspace 도메인만 허용하려면 함수 env `GOOGLE_ALLOWED_HD=<도메인>` 설정 + 서버 검증 활성(§6.5). 미설정이면 email 매핑 화이트리스트로만 통제.
- **B2-6. 사전조건** `[ ]`: item1b는 백엔드 모드(`UseBackend=true`, `USER-ACTIONS §A5`)에서만 동작. A 섹션(백엔드 배포·전환)이 선행돼야 SSO 사용 가능.

---

## 10. 실측 파일 목록 (근거)

**서버(완료·확장 대상)**: `web/functions/src/{index,app,config}.ts`, `routes/auth.ts`, `services/{accounts,dto}.ts`, `domain/{jwt,validation,roles}.ts`, `http/auth.ts`.
**클라(완료·확장 대상)**: `src/MCPhoto.Http/{HttpAccountService,HttpBackendClient,BackendException}.cs`, `Dto/{AccountDtos,BackendJson}.cs`, `Session/{IBackendSession,BackendSession}.cs`, `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Core/Models/{User,UserRole}.cs`, `src/MCPhoto.Core/Navigation/AppState.cs`, `src/MCPhoto.Core/Settings/AppSettings.cs`, `src/MCPhoto.App/{AppShellViewModel,ServiceRegistration}.cs`, `ViewModels/{LoginGuestViewModel,PasswordResetViewModel}.cs`, `Views/{LoginGuestView.xaml,LoginGuestView.xaml.cs}`.
**참고 설계/문서**: `docs/design/{wpf-backend-proxy-migration-design,wpf-accounts-email-verification-design}.md`, `docs/USER-ACTIONS.md`(§B2).

---

## 11. 미해결 결정 사항 집계

### 11.1 `[CONFIRM]` (기본안 확정 — 리뷰 시 조정 가능)

1. OAuth 유형 = **Desktop app + 시스템 브라우저 + loopback + PKCE**(embedded WebView 지양) — §3.1
2. 클라→서버 전달 = **authorization code 교환 방식(A)**, 백엔드가 id_token 재검증(A+B 이중화) — §4.1·§5.3
3. 계정 매핑 = **email 일치 + emailVerified=true**(1:1, item1a 유일성) — §6.2
4. 자동 계정 생성 **금지** — §6.3
5. 매핑 실패 **일반화 401**(열거 방지) — §6.4
6. JWT **재사용**(신규 인증 상태 0, stateless) — §5.4
7. SSO 버튼 **UseBackend 게이트**(`IsBackendMode` 계승) — §7.1
8. client_id는 **클라 INI 설정**(`GoogleClientId`), secret은 백엔드만 — §7.2·§8.2
9. loopback = **127.0.0.1 + OS 자동 포트 + 1회성 + 타임아웃** — §3.3·§7.3
10. **nonce 필수** — §8.4
11. 신규 서버 의존 = **google-auth-library**, 신규 클라 의존 = **없음**(내장) — §1
12. 레거시 `AccountService`는 `LoginWithGoogleAsync` **NotSupported/null** 최소 대응 — §7.6

### 11.2 `[USER-DECISION-REQUIRED]` (순수 제품/운영 판단)

1. **자동 프로비저닝** 허용 여부(hd 내 임의 Google 계정을 user로 자동 생성?) — 기본안 금지 — §6.3
2. **hosted domain(hd) 제한**(특정 Workspace 도메인만 허용?) — 기본안 제한 없음 — §6.5
3. **키오스크 브라우저 정책**(전체화면 잠금 환경에서 시스템 브라우저 오픈 허용? SSO 대신 id/pw 강제?) — §3.1·§7.4

---

## 12. 권장 구현 순서 (서버 → 클라, WBS 단계화 전 개요)

> 확정 후 각 단계를 `WBS_BLUEPRINT.md` 형식으로 self-contained 상세화. 각 단계는 독립 검증 가능·단일 리스크·PASS/FAIL 명확.

1. **S1. 서버 입력 검증(순수)**: `domain/validation.ts`에 `validateAuthCode`·`validateCodeVerifier`·`validateLoopbackRedirectUri` + jest. — 검증: `npm test` PASS, 외부 의존 0.
2. **S2. 서버 Google 검증 서비스**: `services/googleAuth.ts`(google-auth-library 격리: code 교환 + id_token 검증 + aud/iss/exp/nonce/email_verified) + `config.ts`(client id/secret 로드) + `index.ts`(defineSecret·secrets 배열). — 검증: jest(OAuth2Client mock으로 성공/서명실패/aud불일치/미검증email), tsc.
3. **S3. 서버 계정 매핑 + 라우트**: `services/accounts.ts`(`loginWithGoogleEmail`: findByIdOrEmail + emailVerified) + `routes/auth.ts`(`POST /auth/google`: 검증→매핑→issueToken, 실패 일반화). — 검증: Emulator E2E(mock 검증 통과 시: 등록·검증 계정→JWT 발급; 미등록/미검증→401 일반화; JWT가 login과 동일 형식).
4. **S4. 클라 계약·HTTP**: `IAccountService.LoginWithGoogleAsync` + `HttpAccountService` 구현 + `AccountDtos.GoogleLoginRequest` + 레거시 `AccountService` 최소 대응 + `AppSettings.GoogleClientId`(Clamp/Clone). — 검증: `dotnet build` 0경고, 기존 테스트 유지 + 신규 단위(401→null 매핑) 테스트.
5. **S5. 클라 OAuth 서비스**: `IGoogleSignInService`+`GoogleAuthCodeResult`(Core) + `GoogleSignInService`(App: HttpListener·PKCE·state·nonce·Process.Start·타임아웃·정리) + DI 등록. — 검증: `dotnet build`. (관측) 버튼 클릭→브라우저 오픈→loopback code 수신→state 대조. non-goal: 실제 Google 계정 왕복(수동/스테이징). trigger: 사용자 취소→null.
6. **S6. 클라 UI 통합**: `LoginGuestViewModel.LoginWithGoogleCommand` + `LoginGuestView.xaml` 버튼(IsBackendMode 게이트·IsBusy·client_id 존재). — 검증: `dotnet build`. (관측) 백엔드 모드에서 버튼 노출, OFF 모드 숨김; 성공 시 Session.Login+복귀; 매핑 실패 시 일반 안내. non-goal: 실발 Google(스테이징). trigger: 취소·실패 메시지.
7. **S7. 문서 갱신**: `USER-ACTIONS §B2`(§9.2 목록) + `analysis/60` SSO 플로우 + `backlog` item1b. — 검증: 링크·계약 정합.

- **선행 관계**: S1→S2→S3(서버) 순차. S4는 S3 계약 확정 후. S5·S6은 S4 후(S5→S6 순, S6가 S5 서비스 사용). S7 마지막.
- **실제 Google 왕복 검증**은 `[CONSOLE]` B2(콘솔 OAuth 클라이언트·시크릿) 완료에 의존 — 코드는 OAuth2Client mock으로 단위/통합 완결하고, 실왕복은 배포/스테이징에서 B2 설정 후 수동 스모크(운영자 계정 1건 등록→SSO 로그인→화면 진입).

---

## 관련 문서

- `docs/design/wpf-backend-proxy-migration-design.md` — 방향 B 아키텍처(인증 모델 §1.3·JWT·에러 매핑·DI flag). 본 문서는 그 위 가산 설계.
- `docs/design/wpf-accounts-email-verification-design.md` — item1a(email·emailVerified·유일성). SSO 매핑의 전제(§6.2·§6.6).
- `docs/USER-ACTIONS.md` — §B2 Google OAuth 콘솔 작업(본 설계 §9.2로 채움), §A(백엔드 배포 선행).
- `docs/analysis/60-auth-accounts-and-roles.md` — 역할 위계·권한 매트릭스·인증 플로우(SSO 추가 대상).
</content>
</invoke>
