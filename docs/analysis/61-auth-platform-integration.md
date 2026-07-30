# 61 · 플랫폼별 인증 통합 (OAuth · JWT · PIN)

| 항목 | 내용 |
|------|------|
| 문서 | 각 플랫폼에서 Google SSO 로그인을 어떻게 구현하는가, 현재 서버가 무엇까지 받아주는가, 무엇을 확장해야 하는가 |
| 범위 | OAuth 2.0 + PKCE 흐름의 플랫폼별 구현, `POST /auth/google` 계약의 제약, JWT 수명·보관·폐기 규약, 진입 PIN 게이트 |
| 최종 업데이트 | 2026-07-30 (신규 — 데스크톱 loopback 전용 흐름을 멀티플랫폼 관점으로 확장) |
| 관련 문서 | 진입 [05](./05-cross-platform-client-guide.md) · 역할·권한 [60](./60-auth-accounts-and-roles.md) · API 계약 [31 §4.2](./31-backend-api-reference.md) · 화면 동작 [13 §3](./13-client-behavior-spec.md) |
| 갱신 규칙 | 서버 `validateLoopbackRedirectUri`·audience 처리·client_secret 사용이 바뀌면 §2·§4를 갱신한다. 새 IdP를 추가하면 §7을 갱신 |

---

## 1. 인증 모델 전체 그림

MC포토의 자격증명은 **두 개뿐**이다. 서로 다른 목적을 갖고 서로를 대체하지 않는다.

| 자격증명 | 무엇을 증명하나 | 어디서 검증 | 언제 요구 |
|----------|-----------------|-------------|-----------|
| **Google SSO** | 신원(당신이 누구인가) | 서버가 Google id_token 검증 | 로그인 시 1회 |
| **진입 PIN(4자리)** | 물리적 재확인(지금 이 사람이 계정 주인인가) | 서버 bcrypt 해시 대조 | 설정·계정 관리 진입 시 |

- **비밀번호 개념은 존재하지 않는다.** 회원가입·이메일 인증·비밀번호 재설정 API가 모두 없다.
- 신규 계정은 Google SSO **최초 로그인 시 서버가 자동 생성**하며 역할은 항상 `temp_user`다. 미리 만들어 두는 계정은 없다.
- 게스트(비로그인)는 촬영·로컬 저장·업로드·QR을 모두 쓸 수 있다. 로그인이 필요한 것은 커스텀 프레임·계정 관리·관리 기능이다.

---

## 2. 현재 서버 계약과 그 제약 (착수 전 필독)

`POST /auth/google`이 유일한 로그인 엔드포인트다. 요청·응답은 [31 §4.2](./31-backend-api-reference.md)에 있다. 여기서는 **플랫폼 확장에 걸리는 제약**만 정리한다.

| # | 제약 | 근거 | 영향 |
|---|------|------|------|
| **C1** | `redirectUri`는 **http loopback만** 허용한다: scheme `http`, host `127.0.0.1` 또는 `localhost`, 경로 `/` 또는 없음, 쿼리·프래그먼트·인증정보 금지, ≤256자 | `domain/validation.ts` `validateLoopbackRedirectUri` | **커스텀 스킴(`com.example.app:/oauth`)·https 리디렉트가 400으로 거부된다** → iOS·Android·웹 전부 불가 |
| **C2** | audience 검증이 **단일 client_id 고정**이다(`payload.aud !== cfg.clientId`면 거부) | `services/googleAuth.ts` `assertPayloadAndExtractEmail` | 플랫폼별 client_id를 쓸 수 없다 |
| **C3** | code 교환에 **client_secret을 항상 사용**한다 | `services/googleAuth.ts` `defaultClientFactory` | iOS/Android 유형 OAuth 클라이언트는 secret이 없다 → 교환 실패 |
| **C4** | SSO 활성화 신호가 `GOOGLE_OAUTH_CLIENT_ID` 단일 값의 존재 여부다 | `config.ts` | 플랫폼별 부분 활성화가 불가 |
| **C5** | `GOOGLE_ALLOWED_HD`가 설정되면 **단일 도메인만** 허용한다 | 동상 | 여러 조직 도메인 허용 불가(현재 요구 없음) |

> **결론**: 데스크톱(Windows·macOS 앱)은 지금 서버로 바로 붙을 수 있다. **iOS·iPadOS·Android·웹은 서버 확장이 선행돼야 한다.** 필요한 변경은 §4.

---

## 3. 플랫폼별 OAuth 흐름

공통 요소는 모두 동일하다: **Authorization Code + PKCE(S256)**, scope `openid email profile`, `state`·`nonce` 난수.

### 3.0 공통 파라미터 규격

| 항목 | 값 |
|------|-----|
| authorize 엔드포인트 | `https://accounts.google.com/o/oauth2/v2/auth` |
| `response_type` | `code` |
| `scope` | `openid email profile` (공백 구분) |
| `code_challenge_method` | `S256` |
| `code_verifier` | 암호학적 난수 **32바이트 이상** → base64url(패딩 제거) → **43~128자**, 문자 집합 `[A-Za-z0-9-._~]` |
| `code_challenge` | `BASE64URL(SHA256(ASCII(code_verifier)))` — 항상 43자 |
| `state` | 같은 방식의 난수. **콜백에서 반드시 대조**(CSRF 방어) |
| `nonce` | 같은 방식의 난수. 서버가 id_token의 `nonce`와 대조(replay 방어) |
| refresh token | **사용하지 않는다**(`access_type=offline`·`prompt=consent` 미사용) |
| 로그 금지 | code · code_verifier · state · nonce · id_token · 서버 JWT |

**클라이언트가 서버에 보내는 것**: `{code, codeVerifier, redirectUri, nonce?}`. **access_token·id_token을 클라이언트가 직접 검증하거나 서버에 보내지 않는다** — code 교환과 검증은 전부 서버가 한다.

### 3.1 Windows · macOS 데스크톱 앱 (현행 방식, 서버 변경 불요)

```
1. PKCE(verifier/challenge) · state · nonce 생성
2. 빈 loopback 포트 확보 → redirectUri = "http://127.0.0.1:{port}/"
   ※ localhost 대신 127.0.0.1 고정 — 일부 환경에서 localhost가 ::1로 해석되는 문제 회피
3. 로컬 HTTP 리스너 시작
4. 시스템 기본 브라우저로 authorize URL 열기 (앱 내장 WebView 금지 — Google 정책)
5. 브라우저가 리디렉트 → 리스너가 콜백 수신
6. state 대조 → code 추출
7. POST /auth/google {code, codeVerifier, redirectUri, nonce}
8. 응답의 token을 메모리에 보관 + 세션 사용자 설정 → 직전 화면 복귀
9. 리스너·취소 토큰을 try-finally로 항상 정리 (포트·핸들 누수 0)
```

| 항목 | 값 |
|------|-----|
| OAuth 클라이언트 유형 | **Desktop app** |
| 타임아웃 | **3분** — 초과 시 취소로 처리 |
| 취소·실패 처리 | 사용자 취소 / 타임아웃 / `state` 불일치 / 인가 거부 / code 없음 → 모두 **"Google 로그인이 취소되었습니다."** 로 통일하고 화면 유지 |
| 실패 후 이탈 | 로그인 화면의 [닫기]는 **항상 노출**돼 실패·미구성 상태에서도 게스트 촬영으로 돌아갈 수 있어야 한다 |

**macOS 앱 주의**: 샌드박스 앱에서 로컬 HTTP 리스너를 열려면 네트워크 서버 권한(entitlement)이 필요하다. 스토어 배포라면 §3.2의 `ASWebAuthenticationSession` 방식이 더 안전한 선택이며, 그 경우 **서버 확장(§4)이 필요**하다.

### 3.2 iOS · iPadOS (+ macOS 앱 대안) — 서버 확장 필요

```
1. PKCE · state · nonce 생성
2. redirectUri = "{역방향 클라이언트 ID}:/oauth2redirect"
     예: com.googleusercontent.apps.712395684881-xxxx:/oauth2redirect
3. ASWebAuthenticationSession 으로 authorize URL 열기
     - callbackURLScheme 에 역방향 클라이언트 ID 등록
     - prefersEphemeralWebBrowserSession 은 키오스크 특성에 맞춰 결정
       (공용 기기라면 true 권장 — 이전 사용자 세션이 남지 않는다)
4. 콜백 URL 수신 → state 대조 → code 추출
5. POST /auth/google {code, codeVerifier, redirectUri, nonce}
```

| 항목 | 값·주의 |
|------|---------|
| OAuth 클라이언트 유형 | **iOS**(번들 ID 등록). client_secret **없음** → 서버 C3 확장 필요 |
| 리디렉트 형식 | 커스텀 스킴 → **서버 C1 확장 필요** |
| `Info.plist` | `CFBundleURLTypes`에 역방향 클라이언트 ID 스킴 등록 |
| 내장 WebView | **금지**(`WKWebView`로 Google 로그인 페이지를 직접 띄우면 차단될 수 있다). `ASWebAuthenticationSession` 사용 |
| Apple 심사 | 서드파티 SSO만 제공하면 **Sign in with Apple 병행 요구**를 받을 수 있다([05 §9 B8](./05-cross-platform-client-guide.md)) |
| 키오스크 고려 | iPadOS를 키오스크로 쓸 때 `prefersEphemeralWebBrowserSession = true`로 두면 손님 계정이 남지 않는다 |

### 3.3 Android — 서버 확장 필요

```
1. PKCE · state · nonce 생성
2. redirectUri = 커스텀 스킴 또는 App Link
     커스텀 스킴 예: {역방향 클라이언트 ID}:/oauth2redirect
     App Link 예:   https://{도메인}/oauth2redirect   (assetlinks.json 필요)
3. Custom Tabs 로 authorize URL 열기 (WebView 금지)
4. 인텐트 필터로 콜백 수신 → state 대조 → code 추출
5. POST /auth/google {code, codeVerifier, redirectUri, nonce}
```

| 항목 | 값·주의 |
|------|---------|
| OAuth 클라이언트 유형 | **Android**(패키지명 + SHA-1 서명 지문 등록). client_secret **없음** → 서버 C3 확장 필요 |
| 서명 지문 | 디버그·릴리스·Play 앱 서명(App Signing) 지문을 **각각 등록**해야 한다. 누락이 가장 흔한 실패 원인 |
| 리디렉트 형식 | 커스텀 스킴 또는 https App Link → **서버 C1 확장 필요** |
| 내장 WebView | **금지**. Custom Tabs 또는 AppAuth 라이브러리 사용 |
| 인텐트 하이재킹 | 커스텀 스킴은 다른 앱이 가로챌 수 있다. **PKCE가 그 위협을 막는 장치**이므로 절대 생략하지 않는다. 더 강한 보장이 필요하면 App Link |

### 3.4 웹 프론트엔드 — 서버 확장 필요

```
1. PKCE · state · nonce 생성 → sessionStorage 등에 보관
2. redirectUri = "https://{호스팅 도메인}/oauth2callback"  (Google 콘솔에 정확히 등록)
3. 브라우저를 authorize URL로 이동 (전체 페이지 리디렉트 또는 팝업)
4. 콜백 페이지에서 state 대조 → code 추출
5. POST /auth/google {code, codeVerifier, redirectUri, nonce}
6. 응답 token을 메모리(JS 변수)에 보관 — localStorage/sessionStorage 금지 (M2)
```

| 항목 | 값·주의 |
|------|---------|
| OAuth 클라이언트 유형 | **Web application**. client_secret이 있지만 **브라우저에 두지 않는다**(서버가 보관) |
| 리디렉트 형식 | https 절대 URL → **서버 C1 확장 필요** |
| 토큰 보관 | **메모리만.** 새로고침 시 재로그인이 원칙이다. 영속이 필요해지면 HttpOnly 쿠키 기반 세션을 별도로 설계해야 하며, 이는 현재 JWT 모델의 변경이다 |
| 게이트 키 | `X-MCPhoto-Client`가 브라우저에서 완전히 공개된다 → [05 §9 B4](./05-cross-platform-client-guide.md) 결정 필요 |
| CSP | 다운로드 페이지는 현재 엄격한 CSP를 쓴다. 관리 콘솔을 같은 호스팅에 올리면 `connect-src`에 백엔드 함수 도메인을 추가해야 한다 |
| 세션 격리 | 공용 PC 대응으로 명시적 로그아웃과 탭 종료 시 토큰 소멸을 보장한다 |

---

## 4. 서버에 필요한 확장 (설계 제안)

아래는 **제안**이며 아직 구현돼 있지 않다. 착수 시 별 이터레이션으로 설계·구현해야 한다.

### 4.1 리디렉트 URI 허용 목록화 (C1 해소)

현재의 "loopback만" 하드코딩을 **플랫폼별 허용 규칙 목록**으로 바꾼다. SSRF·오용 방어를 잃지 않는 것이 핵심 제약이다.

| 허용 유형 | 검증 규칙(제안) |
|-----------|-----------------|
| loopback(데스크톱) | 현행 그대로 유지 |
| 커스텀 스킴(iOS/Android) | scheme이 **서버에 등록된 스킴 목록**에 포함 + 경로가 등록된 값과 일치. 임의 스킴 거부 |
| https(웹) | **정확한 URI 완전 일치** 목록 대조(prefix 매칭 금지 — open redirect 방지) |

> `redirectUri`는 Google의 code 교환 요청에 그대로 실린다. 임의 값을 통과시키면 서버가 임의 호스트로 요청을 보내는 통로가 된다. **반드시 허용 목록 방식**으로 구현하고 정규식 완화로 처리하지 않는다.

### 4.2 audience 다중 허용 (C2 해소)

`GOOGLE_OAUTH_CLIENT_ID` 단일 값을 **client_id 목록**으로 일반화한다.

- `verifyIdToken`의 `audience`에 배열을 넘기고, 방어적 재확인도 `payload.aud`가 목록에 **포함**되는지로 바꾼다.
- 요청이 어느 플랫폼인지 식별해야 code 교환에 맞는 클라이언트 구성을 골라야 한다(§4.3). 식별 수단으로는 **명시 필드 추가**(`clientKind: "desktop" | "ios" | "android" | "web"`)가 리디렉트 형태 추론보다 명확하다.
- SSO 활성 판정(C4)도 "목록이 비어 있지 않은가"로 바꾼다.

### 4.3 client_secret 조건부 사용 (C3 해소)

| 클라이언트 유형 | code 교환 |
|-----------------|-----------|
| Desktop / Web | client_id + **client_secret** + PKCE (현행) |
| iOS / Android | client_id + **PKCE만**(secret 없음) |

- 유형별 구성을 서버에 등록하고(`client_id` ↔ `secret 유무` ↔ `허용 리디렉트`), 요청의 `clientKind`로 고른다.
- **PKCE는 모든 유형에서 필수**로 유지한다.

### 4.4 회귀 방지

- 서버 인증 게이트 회귀 테스트가 이미 존재한다(권한 라우트가 열리지 않는지 고정). 위 확장에는 **리디렉트 허용 목록 밖 값이 거부되는지**, **audience 목록 밖 aud가 거부되는지** 테스트를 추가한다.
- 데스크톱 흐름이 그대로 통과하는지 확인하는 회귀 테스트를 남긴다(현행 클라이언트가 배포돼 있다).

---

## 5. 서버가 하는 검증 (클라이언트가 신뢰할 수 있는 것)

클라이언트는 아래를 **직접 하지 않는다** — 서버가 이미 한다.

| 순서 | 검증 |
|:----:|------|
| 1 | SSO 구성 여부 → 미구성이면 **501** |
| 2 | 입력 형식(code·codeVerifier·redirectUri·nonce) → 위반 시 **400** |
| 3 | code 교환(`getToken`) — `codeVerifier`·`redirect_uri`가 정확히 일치해야 성공 |
| 4 | id_token 서명·만료·issuer 검증(Google 공개키) |
| 5 | **방어적 재확인**: `aud` 일치 · `iss ∈ {https://accounts.google.com, accounts.google.com}` · `exp > now` · `nonce` 일치(요청에 있으면) · `hd` 일치(설정 시) · **`email_verified === true`** · `email` 존재 |
| 6 | email 소문자 정규화 → 계정 조회/자동 생성 |
| 7 | JWT 발급 |

- 실패 사유는 **서버 로그에만** 남고 응답은 401로 **일반화**된다(계정 열거 방지). 클라이언트는 사유를 알 수 없으므로 단일 안내 문구를 쓴다.
- **`email_verified` 강제**가 중요하다 — 미확인 email로 다른 사람 계정을 선점하는 것을 막는다.

---

## 6. JWT 취급 규약

| 항목 | 규격 |
|------|------|
| 형태 | HS256, 클레임 `sub`(계정 id) · `role` · `iat` · `exp` |
| 만료 | 기본 **8시간**(응답 `expiresIn`으로 함께 내려온다) |
| 보관 위치 | **메모리 전용.** 디스크·keychain·localStorage 금지 |
| 부착 | 로그인 필요 엔드포인트에 `Authorization: Bearer {token}` |
| 업로드 | **선택적 부착** — 로그인 상태면 붙이고, 게스트면 붙이지 않는다 |
| **폐기** | 세션 사용자가 null이 되는 **모든 경로**에서 즉시 폐기. "로그아웃 버튼"에만 걸지 않는다 |
| 갱신 | **없다.** 만료 후 첫 요청이 401 → 재로그인 유도 |
| 역할 변경 반영 | 토큰의 `role`은 발급 시점 값. 승격·강등은 **재로그인 후** 반영된다 |

### 6.1 토큰 폐기가 왜 결정적인가

업로드는 **선택적 Bearer**다. 로그아웃 후에도 토큰이 홀더에 남아 있으면:

```
로그아웃 → (토큰 남음) → 게스트 손님이 촬영 → 업로드에 옛 토큰 부착
  → 서버가 그 결과물을 직전 계정 소유로 기록
  → 그 계정이 TempUser면 무료 사용 횟수까지 차감
```

Windows 구현에서 실제로 있었던 결함이며, **세션 사용자 변경 통지를 구독해 토큰을 지우는 방식**으로 고쳤다. "로그아웃 함수 한 곳에 거는" 대신 통지 지점에 건 이유는 **게스트 전환 경로가 앞으로 늘어도 한 곳이 전부를 덮기 때문**이다. 새 클라이언트도 같은 배선을 권장한다.

### 6.2 세션 유지 규칙

| 트리거 | 로그인 상태 |
|--------|-------------|
| 명시적 로그아웃 | 해제 + **토큰 폐기** |
| 홈 버튼·취소 | 유지 |
| 촬영 완료 | 유지 |
| **유휴 타임아웃** | **유지(로그아웃 금지)** |
| 전역 예외 복구 | 유지 |

전수 표는 [13 §3.3](./13-client-behavior-spec.md).

---

## 7. 진입 PIN 게이트

### 7.1 언제 요구하나

| 진입로 | PIN 보유 | PIN 미설정 | 게스트 |
|--------|----------|------------|--------|
| 설정 화면 | **매번 확인** | **최초 설정 강제** | **무가드**(바로 진입) |
| 계정 관리 / 관리자 도구 | 재확인 없이 진입(진입 시 1회 판정) | 최초 설정 강제, 취소 시 직전 화면 복귀 | 도달 불가(로그인 전용) |

- 두 진입로는 **같은 판정 함수·같은 다이얼로그·같은 PIN**(서버 해시 1개)을 쓴다. 화면마다 다른 PIN을 두지 않는다.
- 판정은 **한 곳에 모은다**. 여러 곳에 흩어지면 한 경로가 게이트를 빼먹는다.

### 7.2 판정 규격

```
ensurePinGate(user):
  if 계정 서비스 또는 PIN 입력 UI 를 쓸 수 없다:  return false      # fail-closed
  if user.hasPin:
      PIN 확인 다이얼로그 → 서버 POST /accounts/me/pin/verify
        200 → true
        401 → 불일치 (실패 카운트 +1, 1.5초 쿨다운, 5회 시 창 닫힘 → false)
        409 → PIN 미설정 → 최초 설정 플로우로 전환
        기타·네트워크 → "확인할 수 없습니다. 네트워크를 확인하세요."
                        (실패 카운트 미가산, 게이트 열지 않음 → false)
  else:
      최초 설정 다이얼로그(새 PIN 2회 일치) → PUT /accounts/me/pin {newPin}
        성공 → 세션의 hasPin = true → true       # 재확인 요구하지 않음(데드락 방지)
        실패 → false
```

| 규칙 | 값 |
|------|-----|
| PIN 형식 | **정확히 4자리 숫자** |
| 연속 실패 상한 | **5회** → 입력 창 닫힘(게이트 미통과) |
| 실패 쿨다운 | 불일치마다 **1.5초** 입력 비활성 |
| 네트워크·서버 오류 | 실패 횟수 **미가산**, 게이트 **미개방** |
| fail 방향 | **fail-closed**(확인 불가 시 거부) |

### 7.3 서버측 제약과 위협 모델

- **서버에 계정 잠금이 없다.** 의도적 선택이다 — 계정 단위 잠금은 남의 PIN을 일부러 5회 틀려 그 계정을 잠그는 **DoS**를 만든다.
- 따라서 4자리(1만 조합)에 대한 온라인 브루트포스가 이론상 가능하다. 완화는 **클라이언트 2건이 전부**이며 앱을 다시 열면 카운터가 초기화된다.
- 검토 중인 대안: **기기(입력 창) 단위 잠금** 또는 IP 단위 rate limit. **물리 접근이 전제인 키오스크에서는 기기 단위가 위협 모델에 더 맞는다.** 사용자 아이디어("5회 실패 시 5분 잠금")는 보류 상태다([90 §2.1](./90-roadmap-and-future-work.md)).
- 새 클라이언트가 **기기 단위 잠금(예: 5회 실패 → 5분 입력 차단, 앱 재시작에도 유지)** 을 구현하는 것은 현행 규약을 강화하는 방향이며 권장된다. 단 **계정 단위로 만들지 말 것**(DoS).

### 7.4 admin PIN 분실

- 앱 내 복구 경로가 **없다**. 자기 자신 대상 PIN 재설정은 서버가 400으로 거부하고, 타 계정 재설정(`canResetPin`)은 **엄격히 낮은 위계**만 대상으로 삼으므로 admin은 물론 **다른 admin도** 복구해 줄 수 없다.
- 현재 유일한 복구는 서버측 마이그레이션 스크립트로 해당 계정의 PIN 해시를 지우는 것이다(운영자 작업).
- 새 클라이언트도 이 제약을 그대로 물려받는다. **"PIN 분실 시 앱에서 복구 불가"** 를 운영 문서에 명시해야 한다.

---

## 8. 로그인 미구성 상태 처리

| 상황 | 판정 | 화면 |
|------|------|------|
| 클라이언트 설정 `GoogleClientId`가 빈 값 | 클라이언트가 판정 | **로그인 버튼을 통째로 숨기고** "로그인이 구성되지 않았습니다. 관리자에게 문의하세요." 정적 안내만 |
| 서버 SSO 미구성 | **501** 응답 | "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." |
| 백엔드 미도달 | 네트워크 실패 | "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요." (화면 유지) |

- `GoogleClientId` 빈 값은 **의도적 opt-out**이다(브라우저가 봉쇄된 키오스크 배려). 이 상태에서도 게스트 촬영은 정상 동작해야 한다.
- 어느 실패든 [닫기]로 게스트 흐름에 복귀할 수 있어야 한다.

---

## 9. 오프라인 동작

| 기능 | 오프라인 |
|------|----------|
| 로그인 | **불가.** 오프라인 폴백·인메모리 계정이 **없다** |
| 게스트 촬영·합성·로컬 저장 | **정상 동작** |
| 프레임 목록(공용) | 로컬 캐시·번들·fallback으로 폴백([13 §5](./13-client-behavior-spec.md)) |
| 업로드·QR | 실패 → 우아 처리(로컬 보존 + 재시도) |
| PIN 게이트 | **fail-closed**(진입 거부) |
| 무료 한도 조회 | **fail-open**(허용, 서버가 업로드에서 최종 거부) |

> 과거에는 시드 계정(고정 id/비밀번호)으로 오프라인 admin 로그인이 가능했다. **비밀번호 폐지와 함께 완전히 제거**됐다. 새 클라이언트가 "오프라인 관리자 모드"를 다시 만들면 **보안 회귀**다 — 필요하다면 서버측 설계로 다루어야 한다.

---

## 10. 이식 체크리스트

- [ ] PKCE(S256)를 사용하고, `code_verifier`가 43~128자·문자 집합을 만족한다
- [ ] `state`를 콜백에서 **반드시 대조**한다
- [ ] `nonce`를 생성해 서버로 보낸다(replay 방어)
- [ ] 인가 UI가 **내장 WebView가 아니다**(시스템 브라우저 / `ASWebAuthenticationSession` / Custom Tabs)
- [ ] code·verifier·state·nonce·토큰이 로그에 남지 않는다
- [ ] JWT를 **메모리에만** 보관한다
- [ ] 세션 사용자가 null이 되는 **모든 경로**에서 토큰이 폐기된다 (M1)
- [ ] 로그아웃 직후 게스트 업로드 요청에 `Authorization`이 붙지 않음을 실제로 확인했다
- [ ] 유휴 타임아웃이 로그아웃하지 않는다
- [ ] 401(자격 실패) / 501(미구성) / 네트워크 실패를 **서로 다른 문구**로 안내한다
- [ ] 로그인 실패·미구성 상태에서도 게스트 흐름으로 복귀할 수 있다
- [ ] PIN 게이트가 fail-closed이고, 네트워크 오류를 실패 횟수로 세지 않는다
- [ ] PIN 형식이 4자리 숫자이고 5회/1.5초 완화가 구현됐다
- [ ] PIN 미설정 시 최초 설정 플로우로 유도한다(409 처리)
- [ ] `GoogleClientId`가 비면 로그인 버튼을 숨긴다
- [ ] 리소스(리스너·세션·취소 토큰)가 실패 경로에서도 정리된다
