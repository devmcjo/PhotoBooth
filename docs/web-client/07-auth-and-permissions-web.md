# 07 · 인증과 권한 (웹)

| 항목 | 값 |
|------|-----|
| 문서 | Google SSO 리디렉트 흐름·JWT 취급·PIN 게이트·역할 게이트의 웹 구현 |
| 규격 진실원 | **`docs/analysis/61-auth-platform-integration.md`**(플랫폼별 OAuth·JWT·PIN) · **`docs/analysis/60`**(역할·권한 매트릭스) · `docs/analysis/31 §4.2·§4.5`(와이어) |
| Windows 참조 | `src/MCPhoto.App/Services/GoogleSignInService.cs`, `src/MCPhoto.Core/Accounts/GoogleOAuthPkce.cs`, `src/MCPhoto.App/AppShellViewModel.cs`(`EnsurePinGateAsync`), `src/MCPhoto.App/Views/PinPromptWindow.xaml.cs` |
| 선행 조건 | **서버 확장 B1·B2 + 웹 OAuth 클라이언트 등록이 끝나야 로그인이 동작한다** → [08 §3](./08-server-and-infra-prerequisites.md) |
| 갱신 규칙 | 서버 인증 계약이 바뀌면 `docs/analysis/61`을 먼저 고친다 |

---

## 1. 인증 모델 (변경 없음)

자격증명은 **두 개뿐**이며 서로를 대체하지 않는다(`analysis/61 §1`).

| 자격증명 | 무엇을 증명 | 검증 위치 | 언제 |
|----------|-------------|-----------|------|
| **Google SSO** | 신원 | 서버가 Google id_token 검증 | 로그인 시 1회 |
| **진입 PIN(4자리)** | 물리적 재확인 | 서버 bcrypt 해시 대조 | 설정·계정 관리 진입 시 |

- **비밀번호 개념은 존재하지 않는다.** 회원가입·이메일 인증·비밀번호 재설정 UI를 만들지 않는다.
- 신규 계정은 **SSO 최초 로그인 시 서버가 자동 생성**하고 역할은 항상 `temp_user`다.
- **게스트(비로그인)는 촬영·합성·필터·로컬 저장까지 전부 쓸 수 있다.** 로그인이 필요한 것은 **QR 전송**·커스텀 프레임·계정·관리 기능이다.
  - ⚠️ **게스트에게는 QR이 제공되지 않는다**(Windows와 동일): `QrEffectivePolicy.IsQrEnabled`가 미로그인이면 `false`를 돌려주므로 `Result → Done`으로 끝난다([03 §8.1](./03-screens-spec.md)). 서버는 게스트 업로드를 허용하지만(`optionalBearer`) **클라이언트가 업로드 자체를 시작하지 않는다** — QR은 계정 단위 과금·한도(TempUser)의 대상이기 때문이다.
  - 이 판정은 `domain/settings/qrEffectivePolicy.ts` **단일 지점**에서만 한다. 화면에서 `isLoggedIn`으로 QR을 직접 분기하지 않는다(TempUser 한도 분기가 빠진다).

---

## 2. Google SSO — 웹 리디렉트 흐름

### 2.1 왜 Windows와 다른가

| | Windows | 웹 |
|---|---------|-----|
| 인가 UI | 시스템 기본 브라우저 + **loopback 리스너**(`http://127.0.0.1:{port}/`) | **같은 브라우저의 전체 페이지 리디렉트** |
| `redirectUri` | `http://127.0.0.1:{port}/` | `https://{kiosk 도메인}/oauth2callback` |
| OAuth 클라이언트 유형 | Desktop app | **Web application** |
| 서버 수용 여부 | **현행 그대로 통과** | **서버 확장 B1·B2 필요**(현재 loopback만 허용하므로 400) |
| client_secret | 서버가 보관·사용 | **동일**(Web application도 secret이 있고 서버가 보관한다) → **B3는 웹에 불필요** |

### 2.2 흐름 (전체)

```
[Login 화면] "Google로 로그인" 탭
 1. code_verifier(43~128자) · code_challenge(S256) · state · nonce 생성   ← Web Crypto
 2. sessionStorage에 저장: { codeVerifier, state, nonce, returnTo, startedAt }
 3. location.assign(authorizeUrl)                                        ← 페이지를 떠난다
      https://accounts.google.com/o/oauth2/v2/auth
        ?client_id={웹 client_id}
        &redirect_uri=https://{kiosk}/oauth2callback
        &response_type=code
        &scope=openid%20email%20profile
        &code_challenge={challenge}&code_challenge_method=S256
        &state={state}&nonce={nonce}
 4. Google 인증 → /oauth2callback?code=…&state=…  로 복귀
 5. 콜백 처리:
      a. sessionStorage에서 값 복원 (없으면 → 오류 화면 + 홈)
      b. state 대조 (불일치 → "Google 로그인이 취소되었습니다." + 홈)
      c. error 파라미터 있으면 → 취소로 처리
      d. startedAt이 3분 초과면 → 취소로 처리 (Windows 타임아웃과 동일)
      e. POST /auth/google { code, codeVerifier, redirectUri, nonce, clientKind: "web" }
           ↑ clientKind는 서버 확장 B2가 도입하는 필드다. **웹은 반드시 "web"을 보낸다** —
             미지정은 "desktop"(하위 호환)이라 서버가 데스크톱 client_id/secret으로 code를
             교환해 실패한다([08 §4.2](./08-server-and-infra-prerequisites.md)).
      f. 성공 → 토큰을 메모리에 보관 + 세션 사용자 설정
      g. sessionStorage 값 즉시 삭제
      h. history.replaceState로 URL의 code·state 제거          ← 흔적·재사용 방지
      i. returnTo 화면으로 복귀 (없으면 Home)
```

### 2.3 PKCE 생성 (Web Crypto)

```ts
function base64UrlEncode(buf: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(buf)))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
export async function createPkce() {
  const verifierBytes = crypto.getRandomValues(new Uint8Array(32));   // 32바이트 이상
  const codeVerifier = base64UrlEncode(verifierBytes.buffer);         // 43자 → 서버 정규식 통과
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(codeVerifier));
  return { codeVerifier, codeChallenge: base64UrlEncode(digest) };
}
```

| 항목 | 규격(`analysis/61 §3.0`) |
|------|--------------------------|
| `code_verifier` | 32바이트 이상 난수 → base64url(패딩 제거) → **43~128자**, 문자 집합 `[A-Za-z0-9-._~]` |
| `code_challenge` | `BASE64URL(SHA256(ASCII(verifier)))` — 항상 43자 |
| `state` | 같은 방식의 난수. **콜백에서 반드시 대조**(CSRF) |
| `nonce` | 같은 방식의 난수. 서버가 id_token의 `nonce`와 대조(replay 방어) |
| refresh token | **사용하지 않는다**(`access_type=offline`·`prompt=consent` 미사용) |
| scope | `openid email profile` |

### 2.4 임시 값을 `sessionStorage`에 두는 것이 M2 위반이 아닌 이유

| 값 | 보관 | 근거 |
|----|------|------|
| `code_verifier`·`state`·`nonce`·`returnTo` | **`sessionStorage`** | 페이지가 리디렉트로 완전히 떠나므로 메모리로는 전달 불가. 이 값들은 **JWT가 아니고**, 단발성이며 **콜백 처리 직후 즉시 삭제**된다. `code_verifier`는 code와 짝이 맞아야만 의미가 있고 code는 1회용이다 |
| **JWT(서버 토큰)** | **메모리 전용 — 저장 금지** | M2. 어떤 저장소에도 쓰지 않는다 |

> 리디렉트 대신 **팝업**(`window.open`)을 써서 부모 페이지 메모리를 유지하는 방법도 있으나, **키오스크 전체화면에서 팝업은 차단·혼란 위험**이 크고 iOS에서 신뢰도가 낮다. 리디렉트를 기본으로 하고 팝업은 채택하지 않는다.

### 2.5 콜백 라우트 규격

| 항목 | 규격 |
|------|------|
| 경로 | `/oauth2callback` (Google Console에 **정확히 이 URI**로 등록 — 완전 일치 필요) |
| 렌더 | "로그인 처리 중…" 스피너만. **사용자 조작 요소 없음** |
| 성공 | 세션 설정 → `history.replaceState("/")` → `returnTo` 화면 |
| 실패 | 오류 문구를 `Login` 화면에 실어 전달하고 `Login`(또는 `Home`)으로 이동 |
| 새로고침 방어 | 콜백 처리는 **1회만**(처리 시작 시 `sessionStorage`의 값을 즉시 소비·삭제). 재진입 시 값이 없으므로 오류 처리 |
| 개발 환경 | `http://localhost:5173/oauth2callback`도 Google Console에 등록해야 로컬 개발이 된다 |

### 2.6 오류 문구 (`analysis/13 §14` 그대로)

| 상황 | 문구 |
|------|------|
| 사용자 취소 / `state` 불일치 / code 없음 / 3분 타임아웃 | Google 로그인이 취소되었습니다. |
| 서버 **401** | 이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요. |
| 서버 **501** | Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요. |
| 서버 **400**(`redirectUri` 거부 등) | Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요. + **로그에 "서버가 redirectUri를 거부했다(B1 미적용 가능)"** 를 남긴다 |
| 네트워크 | Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요. |
| `GoogleClientId` 빈 값 | 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.(버튼 자체 미노출) |

> **400은 정상 흐름에서 발생하지 않아야 한다.** 발생하면 서버 확장(B1·B2)이 안 됐거나 리디렉트 URI 불일치다 → 진단 로그로 원인을 남긴다.

### 2.7 서버가 하는 검증 (클라이언트가 하지 않는 것)

`analysis/61 §5` 그대로. 클라이언트는 **id_token을 받지도, 검증하지도, 서버에 보내지도 않는다.**

1. SSO 구성 여부(미구성 → 501) → 2. 입력 형식(400) → 3. code 교환 → 4. id_token 서명·만료·issuer 검증 → 5. 재확인(`aud`·`iss`·`exp`·`nonce`·`hd`·**`email_verified === true`**) → 6. email 정규화 → 계정 조회/자동 생성 → 7. JWT 발급.

실패 사유는 **서버 로그에만** 남고 응답은 401로 일반화된다(계정 열거 방지) → 클라이언트는 단일 문구를 쓴다.

---

## 3. 로그인 미구성 상태 (`analysis/61 §8`)

| 상황 | 화면 |
|------|------|
| `GoogleClientId` 빈 값 | **로그인 버튼을 통째로 숨기고** 정적 안내만. **이 상태에서도 게스트 촬영은 정상 동작해야 한다** |
| 서버 SSO 미구성(501) | "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." |
| 백엔드 미도달 | "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요."(화면 유지) |

어느 실패든 **[닫기]로 게스트 흐름에 복귀**할 수 있어야 한다.

---

## 4. JWT 취급

### 4.1 규약 (`analysis/61 §6`)

| 항목 | 값 |
|------|-----|
| 형태 | HS256, 클레임 `sub`(계정 id)·`role`·`iat`·`exp` |
| 만료 | 기본 **8시간**(응답 `expiresIn`) |
| 보관 | **메모리 전용**(모듈 스코프 변수 1개). localStorage·sessionStorage·쿠키·IndexedDB **전부 금지**(M2) |
| 부착 | 로그인 필요 엔드포인트에 `Authorization: Bearer` |
| 업로드 | **선택적 부착** — 로그인 상태면 붙이고 게스트면 붙이지 않는다 |
| 폐기 | 세션 사용자가 null이 되는 **모든 경로**에서 즉시([02 §5.1](./02-app-shell-and-navigation.md)) |
| 갱신 | **없다.** 만료 후 첫 요청이 401 → 재로그인 유도 |
| 역할 변경 반영 | 토큰의 `role`은 발급 시점 값. 승격·강등은 **재로그인 후** 반영 |

### 4.2 M1 배선 (필수)

```ts
// authStore 초기화 시 1회 — "로그아웃 버튼"이 아니라 "사용자가 null이 되는 통지"에 건다
sessionStore.subscribe((s) => s.currentUser, (user) => {
  if (user === null) { token = null; logger.info("JWT 폐기(세션 사용자 null)"); }
});
```

**검증**: E2E로 "로그아웃 → 게스트 촬영 → prepare 요청 헤더에 `Authorization` 없음"을 고정한다([10 §5](./10-testing-and-acceptance.md)).

### 4.3 만료(401) 처리

| 상황 | 처리 |
|------|------|
| Bearer 필수 호출이 401 | 토큰 폐기 + 세션 사용자 해제 + 토스트 *"세션이 만료되었습니다. 다시 로그인해 주세요."* → 현재 화면 유지(촬영 중이면 **촬영·합성·로컬 보관은 계속**되고, 게스트가 되었으므로 `Result` 이후 **QR 없이 `Done`** 으로 끝난다) |
| 진행 중이던 업로드 | **재시도할 수 없다.** 세션 사용자가 해제되면 effective QR이 off가 되므로 재시도 버튼을 노출하지 않고, *"세션이 만료되었습니다. 다시 로그인해 주세요."* 안내 + **결과물이 로컬에 남아 있음**을 알린다. 서버 계약상 무토큰 업로드는 가능하지만 **클라이언트 정책(게스트 QR 차단)을 우회하지 않는다** |
| PIN 검증의 401 | **만료가 아니라 PIN 불일치**다 — 세션을 건드리지 않는다(§6) |

> 두 401을 구분하지 않으면 PIN을 한 번 틀렸을 때 로그아웃되는 회귀가 생긴다. **PIN 검증 호출만 401을 "불일치"로 해석**한다.

### 4.4 새로고침 시 동작 (WD9)

| 항목 | 동작 |
|------|------|
| 새로고침·탭 복구 | 토큰 소실 → **게스트 상태로 시작**한다. 이것이 정상 흐름이다 |
| 안내 | 새로고침 감지(진입 시 토큰 없음 + 직전 세션 흔적)에 대해 별도 안내를 하지 않는다(과잉 안내) |
| 운영 영향 | 운영자는 설정·관리 작업 전에 로그인하면 된다. 촬영은 게스트로 계속 가능 |

---

## 5. 역할·권한 게이트

### 5.1 역할 위계 (`analysis/60 §1`)

`temp_user`(0) < `user`(1) < `advanced_user`(2) < `manager`(3) < `admin`(4) + **게스트**(비로그인).

| 판정 함수 | 정의 | 쓰이는 곳 |
|-----------|------|-----------|
| `isPower(role)` | `manager` 또는 `admin` | 사용자 관리·공용 DB 프레임 관리 |
| `canWriteFrames(role)` | `advanced_user`·`manager`·`admin` | 프레임 생성·편집·삭제 |
| `canManage(actor, target)` | `rank(target) <= rank(actor)` | 계정 **삭제**(동급 허용) |
| `canResetPin(actor, target)` | `isPower(actor) && rank(target) < rank(actor)` | 타 계정 PIN 재설정(**동급 차단**) |
| `assignableRoles(actor, current)` | `analysis/60 §1.4` 매트릭스 | 역할 콤보 필터 |

| 주의 | 내용 |
|------|------|
| **두 축을 혼용하지 않는다** | `isPower` = 계정 관리, `canWriteFrames` = 프레임 저작. `advanced_user`는 프레임을 만들지만 계정 관리 권한이 **전혀 없다** |
| `canResetPin`이 한 칸 좁다 | **매니저 PIN은 admin만** 재설정 가능. 삭제는 동급 허용 → **두 액션의 게이트가 다르다** |
| 알 수 없는 역할 문자열 | **`user`로 폴백**(fail-closed 방향 — `user`는 프레임 쓰기 권한이 없다) |
| 구현 | `domain/roles/*.ts` 순수 함수. **컴포넌트에서 역할 문자열을 직접 비교하지 않는다** |

### 5.2 권한 매트릭스 (`analysis/60 §2` 요약 — 웹 동일)

| 기능 | 게스트 | T | U | **A** | M | D |
|------|:------:|:-:|:-:|:-----:|:-:|:-:|
| 촬영 흐름(프레임 선택→촬영→결과→로컬 저장) | ○ | ○ | ○ | ○ | ○ | ○ |
| **QR 전송·업로드**(effective QR) | **✕** | △(한도 내) | ○ | ○ | ○ | ○ |
| 공용 프레임 사용 | ○ | ○ | ○ | ○ | ○ | ○ |
| 본인 커스텀 프레임 사용 | ✕ | ○ | ○ | ○ | ○ | ○ |
| 프레임 생성·편집·로컬 삭제 | ✕ | ✕ | ✕ | **○** | ○ | ○ |
| 프레임 서버 삭제·공용 등록 | ✕ | ✕ | ✕ | ✕ | ○ | ○ |
| 계정 관리(내 정보·PIN 변경) | ✕ | ○ | ○ | ○ | ○ | ○ |
| 관리자 도구·사용자 목록·삭제 | ✕ | ✕ | ✕ | ✕ | ○ | ○ |
| 타 계정 PIN 재설정 | ✕ | ✕ | ✕ | ✕ | △(엄격히 낮은 위계) | ○ |
| 역할 변경 | ✕ | ✕ | ✕ | ✕ | △ | △ |
| 전역 TempUser 한도 편집 | ✕ | ✕ | ✕ | ✕ | ✕ | ○ |
| 설정 화면 접근 | ○(무가드) | △(PIN) | △ | △ | △ | △ |
| 설정 항목 편집 | △(일부 제한) | △(QR 한도 시 추가 제한) | ○ | ○ | ○ | ○ |

> ⚠️ **`analysis/60 §2`와의 표기 차이(문서 버그 보고 대상)**: `analysis/60 §2`는 "촬영(프레임 선택→촬영→결과→**QR**)"을 게스트 ○ 한 행으로 묶어 두었으나, 실제 구현은 `QrEffectivePolicy.IsQrEnabled`로 **게스트의 QR을 차단**한다(`src/MCPhoto.App/ViewModels/ResultViewModel.cs:149`, `src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`). 진실원 우선순위(실제 소스 > analysis)에 따라 **위 표가 맞고 `analysis/60 §2`의 행 분리가 필요**하다. `analysis/13 §4.7`의 "QR 전송 설정 on?"도 effective 값임을 명시해야 한다. 웹 클라이언트는 소스 동작을 따른다.

### 5.3 3중 방어 구현 패턴 (M10)

```tsx
// 1) 렌더 가드
{canWriteFrames(role) && <Button onClick={createFrame}>프레임 만들기</Button>}

// 2) 커맨드 가드 (액션 함수 첫 줄)
async function createFrame() {
  if (!canWriteFrames(role)) { logger.warn("권한 없는 프레임 생성 시도"); toast.error("프레임을 만들 권한이 없습니다."); return; }
  ...
}

// 3) 서버 강제 — 403을 우아하게 안내 (빈 목록 폴백 금지)
```

---

## 6. 진입 PIN 게이트 (`analysis/61 §7`)

### 6.1 언제 요구하나

| 진입로 | PIN 보유 | PIN 미설정 | 게스트 |
|--------|----------|------------|--------|
| 설정 화면 | **매번 확인** | **최초 설정 강제** | **무가드**(바로 진입) |
| 계정 관리 / 관리자 도구 | 진입 시 1회 판정 | 최초 설정 강제, 취소 시 직전 화면 복귀 | 도달 불가 |

두 진입로는 **같은 판정 함수·같은 모달·같은 PIN**(서버 해시 1개)을 쓴다. **판정을 한 곳에 모은다** — 흩어지면 한 경로가 게이트를 빼먹는다.

### 6.2 판정 규격

```
ensurePinGate(user):
  if (기기 잠금 중)            → 안내 후 false                     # WD16
  if (계정 서비스 또는 PIN UI 사용 불가) → false                     # fail-closed
  if (user.hasPin):
      확인 모달 → POST /accounts/me/pin/verify
        200 → true
        401 → 불일치 (실패 카운트 +1, 1.5초 쿨다운, 5회 → 모달 닫힘 + 5분 기기 잠금 → false)
        409 → PIN 미설정 → 최초 설정 플로우로 전환
        기타·네트워크 → "확인할 수 없습니다. 네트워크를 확인하세요."
                        (실패 카운트 미가산, 게이트 미개방 → false)
  else:
      최초 설정 모달(새 PIN 2회 일치) → PUT /accounts/me/pin { newPin }
        성공 → 세션의 hasPin = true → true      # 재확인 요구하지 않음(데드락 방지)
        실패 → false
```

| 규칙 | 값 |
|------|-----|
| PIN 형식 | **정확히 4자리 숫자**(`^\d{4}$`) |
| 연속 실패 상한 | **5회** → 모달 닫힘(게이트 미통과) |
| 실패 쿨다운 | 불일치마다 **1.5초** 입력 비활성 |
| 네트워크·서버 오류 | 실패 횟수 **미가산**, 게이트 **미개방** |
| fail 방향 | **fail-closed** |

### 6.3 기기 단위 잠금 (WD16 — 웹 강화)

`analysis/61 §7.3`이 권장하는 강화를 웹에서 구현한다.

| 항목 | 규격 |
|------|------|
| 트리거 | 연속 5회 불일치 |
| 잠금 | **5분간 PIN 입력 차단** |
| 저장 | `localStorage["mcphoto.pinLock.v1"] = { until: <epoch ms>, fails: 5 }` — **앱 재시작에도 유지** |
| 안내 | *"PIN 입력이 일시적으로 차단되었습니다. {남은 시간} 후 다시 시도해 주세요."* |
| 성공 시 | 카운터·잠금 초기화 |
| **계정 단위 금지** | 서버에 잠금을 요청하지 않는다 — 남의 PIN을 일부러 틀려 그 계정을 잠그는 **DoS**가 된다 |
| 한계 고지 | 브라우저 저장소를 지우면 초기화된다. 물리 접근이 전제인 키오스크의 위협 모델에서는 수용 가능하며, 근본 대응은 서버 rate limit(후속) |

### 6.4 PIN 입력 UI (키오스크 고려)

| 항목 | 규격 |
|------|------|
| 입력 | **자체 온스크린 숫자 키패드**(0~9, 지우기, 확인) — 물리 키보드가 없는 태블릿 대응 |
| 표시 | 4칸 마스킹 인디케이터(입력된 자리 수만 표시) |
| 물리 키보드 | 숫자 키 입력도 함께 허용(`inputmode="numeric"`) |
| 보안 | `autocomplete="off"`, 값은 상태에만 두고 **로그·에러 리포트에 절대 남기지 않는다** |
| 접근성 | 각 키에 `aria-label`, 실패 안내는 `aria-live="assertive"` |

### 6.5 PIN 분실 (`analysis/61 §7.4`)

- **앱 내 복구 경로가 없다.** 자기 자신 대상 PIN 재설정은 서버가 400으로 거부하고, 타 계정 재설정은 **엄격히 낮은 위계**만 대상이라 admin은 다른 admin도 복구해 줄 수 없다.
- 유일한 복구는 서버측 스크립트: `node web/functions/scripts/migrate-google-only-accounts.mjs --clear-pin <id> --apply`
- **운영 문서에 "PIN 분실 시 앱에서 복구 불가"를 명시**한다([09 §7](./09-kiosk-operations.md)).

---

## 7. TempUser 무료 한도 (`analysis/31 §4.4·§5.4`)

| 항목 | 규격 |
|------|------|
| 조회 | `GET /accounts/me/qr-usage` — 세션 사용자 변경 시 1회(fire-and-forget) |
| **조회 실패** | **fail-open**(허용). 표시만 생략한다(M9) |
| 진실원 | **서버**. prepare 선검사(서명 URL 미발급) + commit 트랜잭션 재검사·카운트 증가 |
| 클라 역할 | 표시·안내뿐. **로컬 카운터로 한도를 관리하지 않는다** |
| `role != "temp_user"` | `remaining*`이 0이지만 **무제한**을 뜻한다 — `role`을 먼저 보고 해석 |
| 카운트 단위 | **세션당 1**(파일 개수 무관) |
| 초과 시 UI | 설정에서 QR 관련 편집만 추가 차단 + `Qr` 화면에서 사유별 문구 |

---

## 8. 체크리스트 (`analysis/61 §10` + 웹)

- [ ] PKCE(S256)를 쓰고 `code_verifier`가 43~128자·문자 집합을 만족한다
- [ ] `state`를 콜백에서 **반드시 대조**한다
- [ ] `nonce`를 생성해 서버로 보낸다
- [ ] `POST /auth/google` 본문에 **`clientKind: "web"`** 이 포함된다(누락 시 서버가 desktop 구성으로 처리해 실패)
- [ ] 인가 UI가 **전체 페이지 리디렉트**다(팝업·iframe 아님)
- [ ] code·verifier·state·nonce·토큰이 **로그에 남지 않는다**
- [ ] 콜백 처리 후 `sessionStorage` 임시 값이 **즉시 삭제**되고 URL에서 code·state가 제거된다
- [ ] JWT를 **메모리에만** 보관한다(코드 검색으로 저장소 접근 0 확인 — M2)
- [ ] 세션 사용자가 null이 되는 **모든 경로**에서 토큰이 폐기된다(M1)
- [ ] 로그아웃 직후 업로드 요청에 `Authorization`이 붙지 않음을 **실제로 확인**했다([10 §5](./10-testing-and-acceptance.md) E3 — effective QR 목으로 업로드를 실행시켜 관측)
- [ ] 유휴 타임아웃이 로그아웃하지 않는다(M3)
- [ ] 401(자격 실패) / 501(미구성) / 400(리디렉트 거부) / 네트워크를 **서로 다른 문구**로 안내한다
- [ ] **PIN 검증의 401을 세션 만료로 처리하지 않는다**
- [ ] 로그인 실패·미구성 상태에서도 게스트 흐름으로 복귀할 수 있다
- [ ] **게스트로 촬영을 완주하면 `Qr`을 건너뛰고 `Done`으로 간다**(업로드 요청 0건 — Network으로 확인)
- [ ] effective QR 판정이 `qrEffectivePolicy` **한 곳**에만 있다(화면에서 `isLoggedIn` 직접 분기 없음)
- [ ] PIN 게이트가 fail-closed이고 네트워크 오류를 실패 횟수로 세지 않는다
- [ ] PIN 5회/1.5초 + **기기 단위 5분 잠금**이 구현됐다(계정 단위 아님)
- [ ] PIN 미설정 시 최초 설정 플로우로 유도한다(409 처리)
- [ ] `GoogleClientId`가 비면 로그인 버튼을 숨긴다
- [ ] 권한 판정이 **UI 미노출 + 커맨드 가드 + 서버 403 처리** 3중이다(M10)
- [ ] 역할 콤보가 서버 `canSetRole` 매트릭스와 1:1이다
- [ ] PIN 재설정 대상이 **엄격히 낮은 위계**만 노출된다(삭제와 게이트가 다르다)
