# 31 · 백엔드 API 참조 (와이어 계약)

| 항목 | 내용 |
|------|------|
| 문서 | 모든 클라이언트가 구현해야 하는 **HTTP 와이어 계약** — 경로·헤더·요청/응답 JSON·상태코드·에러 코드·검증 규칙 |
| 범위 | `web/functions/src/{app,config}.ts`, `web/functions/src/http/{auth,errors,async}.ts`, `web/functions/src/routes/*.ts`, `web/functions/src/domain/{validation,session,jwt,roles,tempUserLimit,accountId}.ts`, `web/functions/src/services/{accounts,frames,uploads,signing,config,dto}.ts` |
| 최종 업데이트 | 2026-07-30 (신규 — [30](./30-backend-firebase-integration.md)의 엔드포인트 카탈로그를 클라이언트 구현용 전체 계약으로 확장) |
| 관련 문서 | 연동 설계 의도·실패 정책은 [30](./30-backend-firebase-integration.md), 저장 스키마는 [40](./40-database-firestore-and-storage-schema.md), 권한 매트릭스는 [60](./60-auth-accounts-and-roles.md), 플랫폼별 인증은 [61](./61-auth-platform-integration.md) |
| 갱신 규칙 | 라우트 추가·게이트 변경·요청/응답 필드 변경·검증 범위 변경 시 이 문서를 **먼저** 갱신한다(클라이언트 다수가 이 문서만 보고 구현한다). 저장 스키마가 함께 바뀌면 40번과 동시 갱신 |

> 이 문서는 **서버 소스가 진실원**이다. 필드명·타입·상태코드는 소스에서 직접 확인한 값만 적었다.

---

## 1. 기본 사항

| 항목 | 값 |
|------|-----|
| Base URL(운영) | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` |
| 리전 | `asia-northeast3` (서울) |
| 런타임 | Cloud Functions 2nd gen 단일 HTTPS 함수 `api` + Express. 실제 URL은 `{base}/{path}` |
| 스케일 | `maxInstances: 10`, `memory 256MiB`, `timeoutSeconds 60` |
| 요청 본문 | `application/json`, **상한 256KB**. 파일 바이트는 API를 경유하지 않는다 |
| CORS | `cors({ origin: true })` — 모든 Origin 허용(브라우저 클라이언트 호출 가능) |
| 라우터 | `/auth` `/accounts` `/config` `/frames` `/uploads` `/health` (6개) + 404 핸들러 |
| 권장 클라이언트 타임아웃 | 100초 (현행 Windows 클라이언트 값) |

### 1.1 미매칭 경로

정의되지 않은 경로는 **404 `not_found`** 로 떨어진다. it15에서 제거된 인증 경로(`/auth/login`, `/auth/register`, `/auth/verify-email`, `/auth/password-reset`)와 계정 생성(`POST /accounts`)·비밀번호/이메일 변경은 **스텁 없이 삭제**됐다 — 410 같은 힌트가 없으므로 클라이언트가 이 경로를 호출하면 그냥 404다.

---

## 2. 인증 헤더와 게이트

두 인증은 **독립**이다. 엔드포인트에 따라 하나만, 둘 다, 또는 아무것도 요구하지 않는다.

| 헤더 | 값 | 용도 |
|------|-----|------|
| `X-MCPhoto-Client` | 배포 게이트 키(평문 문자열) | 게스트도 통과해야 하는 엔드포인트의 배포 식별 |
| `Authorization` | `Bearer {JWT}` | 로그인 신원(계정 id + 역할) |

> 헤더 이름 비교는 소문자 기준(`x-mcphoto-client`)이며 값이 배열로 오면 **첫 값만** 사용한다. 현행 Windows 클라이언트는 게이트 키를 **모든 호출에 부착**한다(키가 비어 있지 않을 때) — 새 클라이언트도 같게 하는 편이 안전하다.

### 2.1 게이트 종류

| 게이트 | 통과 조건 | 실패 | 비고 |
|--------|-----------|------|------|
| `requireApiKey` | `X-MCPhoto-Client`가 서버 `CLIENT_API_KEYS`(CSV) 목록에 포함 | **401** `unauthorized` "유효한 클라이언트 키가 필요합니다." | |
| `requireBearer` | 유효 JWT → `principal = {id, role}` 주입 | **401** `unauthorized` (토큰 없음 / 검증 실패 / 역할 클레임 없음) | |
| `optionalBearer` | 토큰 **없음** = 게스트로 통과 / **유효** = principal 주입 / **무효** = 401 | 401(무효 토큰만) | 업로드 전용. 위조 토큰은 거부(과금 우회 차단) |
| `requirePower` | `role ∈ {manager, admin}` | **403** `forbidden` "파워 계정(manager/admin) 권한이 필요합니다." | `requireBearer` 뒤에서만 사용 |
| `requireAdmin` | `role == admin` | **403** `forbidden` "admin 권한이 필요합니다." | 동상 |

### 2.2 JWT

| 항목 | 값 |
|------|-----|
| 알고리즘 | HS256 (서명 시크릿은 Secret Manager) |
| 클레임 | `sub` = 계정 id(문자열, 필수) · `role` = 역할 문자열(화이트리스트 필수) · `iat` · `exp` |
| 만료 | 기본 **28800초(8시간)**. `expiresIn`으로 응답에 함께 내려온다 |
| 갱신 | **갱신(refresh) 엔드포인트 없다.** 만료 후 첫 요청이 401 → 클라이언트가 재로그인을 유도해야 한다 |
| 검증 실패 메시지 | `"토큰 검증 실패: {상세}"` / `"토큰에 계정 식별자(sub)가 없습니다."` / `"토큰에 유효한 역할(role) 클레임이 없습니다."` — 모두 401 |
| 보관 | **메모리 전용**. 디스크·keychain 영속 금지([05 §6 M2](./05-cross-platform-client-guide.md)) |

> ⚠️ `role`은 **토큰 발급 시점의 값**이다. 관리자가 역할을 바꿔도 기존 토큰의 role은 그대로다 — 승격/강등은 재로그인 후 반영된다.

---

## 3. 에러 봉투와 상태코드 매핑

모든 오류 응답은 동일 형태다.

```json
{ "error": { "code": "invalid_argument", "message": "슬롯은 1~6개여야 합니다." } }
```

| `code` | 상태 | 의미 | 클라이언트 권장 처리 |
|--------|:----:|------|----------------------|
| `unauthorized` | 401 | 인증 필요/실패(게이트 키 무효, Bearer 없음·만료·위조, PIN 불일치, 로그인 자격 실패) | **호출부가 결정** — 로그인은 "자격 실패", PIN은 "불일치", 그 외는 "다시 로그인" 유도. ⚠️ 아래 3.2 참고 |
| `forbidden` | 403 | 권한 없음(power/admin/위계 위반, 타 계정 프레임 조회, 자기 자신 삭제) | "권한이 없습니다" 안내 |
| `not_found` | 404 | 대상 없음(계정·프레임) 또는 미정의 엔드포인트 | "대상을 찾을 수 없습니다" |
| `conflict` | 409 | 중복(동일 `sessionId` 재commit, **프레임 이름 중복**) 또는 **PIN 미설정** | 문맥별 분기 필수(§4.5 참고). 프레임 개수 상한은 폐지됐다 |
| `invalid_argument` | 400 | 입력 검증 실패, JSON 파싱 실패, 자기 자신 대상 PIN 재설정 | 입력 오류 안내 |
| `not_implemented` | 501 | 서버 기능 미구성 — **Google SSO 미구성 또는 OAuth 클라이언트 자격 오류**(client_id/secret이 Google에 등록된 값과 불일치) | "로그인이 구성되지 않았습니다. 관리자에게 문의" — 자격 실패·네트워크와 **구분해서** 안내 |
| `internal` | 500 | 서버 오류 | 재시도 가능 안내 |
| `TEMP_USER_TIME_EXCEEDED` | **403** | 무료 사용 시간 경과 | 고정 문구: *"무료 사용 시간이 지났습니다. 관리자에게 문의해주세요."* |
| `TEMP_USER_COUNT_EXCEEDED` | **403** | 무료 사용 횟수 소진 | 고정 문구: *"무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요."* |

> **403의 두 얼굴**: 권한 부족과 무료 한도 초과가 같은 상태코드다. 반드시 `error.code`를 봐야 구분된다. `code`가 `TEMP_USER_*`인 403은 권한 문제가 아니라 **한도 문제**이며 문구가 다르다(위 두 메시지는 설계에서 동결된 문자열이므로 서버 message를 그대로 써도 된다).

### 3.1 네트워크 계층 실패

서버가 응답하지 못한 경우(연결 실패·타임아웃·DNS)는 HTTP 상태코드가 없다. 클라이언트는 이를 **"백엔드에 연결할 수 없습니다"** 류의 별도 상태로 다뤄야 하며, 401/403과 섞지 않는다. 현행 Windows 클라이언트가 이 구분을 유지한다([70 §6.3](./70-logging-and-troubleshooting.md)).

**서버 주소 미설정은 또 다른 상태다.** 상태코드도 없고 네트워크 실패도 아니다(요청을 보내지조차 않는다). 이 셋을 뭉뜽그리면 조치 방법이 달라 사용자가 헤맨다 — 네트워크를 고칠 일인지, 설정에 주소를 넣을 일인지, 다시 로그인할 일인지.

### 3.2 클라이언트 예외 매핑 (Windows)

`MapToDomainException`이 서버 응답을 도메인 예외로 바꾼다. **문구 분기는 이 타입으로 한다** — 예외 메시지 문자열을 되짚으면 문구를 고칠 수 없게 된다.

| 상황 | 예외 타입 | 기반 타입 |
|------|----------|----------|
| 서버 주소 미설정 | `BackendNotConfiguredException` | `InvalidOperationException` |
| 연결 실패·타임아웃·이미지 PUT 실패 | `BackendUnavailableException` | `InvalidOperationException` |
| Bearer 없음 | `BackendLoginRequiredException(Expired=false)` | `UnauthorizedAccessException` |
| **401** | `BackendLoginRequiredException(Expired=true)` | `UnauthorizedAccessException` |
| 403 | `UnauthorizedAccessException` | |
| 400 | `ArgumentException` | |
| 404 · 409 · 5xx | `InvalidOperationException`(서버 message 인용) | |

기반 타입을 유지하는 이유: 기존 `catch (InvalidOperationException)` / `catch (UnauthorizedAccessException)` 코드가 그대로 동작한다(계약 무변경).

> ⚠️ **`PUT /accounts/me/pin`만 401 매핑에서 제외된다.** 이 라우트의 401은 *현재 PIN 불일치*(§4.9)이거나 *토큰 만료*인데 서버가 둘 다 `unauthorized`로 준다. 만료로 단정하면 PIN을 틀린 사용자에게 재로그인을 시키게 되므로, 이 라우트만 일반 `UnauthorizedAccessException`으로 올려 호출부가 두 경우를 함께 덮는 문구를 쓴다.
>
> **서버 개선 후보**: 토큰 검증 실패에 `token_invalid` 같은 별도 code를 주면 이 예외 케이스가 사라진다. 지금 바꾸면 구버전 클라이언트가 401 처리를 잃으므로 클라이언트 배포 이후로 미룬다.

---

## 4. 엔드포인트 상세

표기: `게이트` 열은 통과해야 하는 미들웨어 순서. `—`는 인증 불요.

### 4.0 요약표

| 메서드·경로 | 게이트 | 성공 |
|-------------|--------|------|
| `GET /health` | — | 200 |
| `POST /auth/google` | apiKey | 200 |
| `GET /accounts` | Bearer + power | 200 |
| `GET /accounts/me/qr-usage` | Bearer | 200 |
| `POST /accounts/me/pin/verify` | Bearer | 200 |
| `PUT /accounts/me/pin` | Bearer | **204** |
| `DELETE /accounts/{id}` | Bearer + power | **204** |
| `PATCH /accounts/{id}/role` | Bearer + power | **204** |
| `PUT /accounts/{id}/pin` | Bearer + power | **204** |
| `GET /config/temp-user-limits` | Bearer | 200 |
| `PATCH /config/temp-user-limits` | Bearer + admin | 200 |
| `GET /frames/default` | apiKey | 200 |
| `GET /frames?userId=` | Bearer | 200 |
| `POST /frames` | Bearer + power | **201** |
| `POST /frames/mine` | Bearer + **프레임 저작 권한**(advanced_user 이상) | **201** |
| `PUT /frames/{id}` | Bearer + power | 200 |
| `DELETE /frames/{id}` | Bearer + **프레임 저작 권한**(본인 소유) / 공용은 power | 200 |
| `POST /uploads/prepare` | apiKey + optionalBearer | 200 |
| `POST /uploads/commit` | apiKey + optionalBearer | **201** |

> ⚠️ `/accounts`·`/config` 라우터는 **라우터 레벨에서 `requireBearer`** 를 적용한다 — 하위 모든 경로가 로그인 필수다. `/uploads`는 라우터 레벨에서 `requireApiKey` + `optionalBearer`를 적용한다. `/frames`는 라우터 레벨 게이트가 없고 **경로별로 다르다**(`/default`만 apiKey, 나머지는 Bearer).

---

### 4.1 `GET /health`

도달성 확인용. **인증 없음**.

**응답 200**
```json
{
  "status": "ok",
  "time": "2026-07-30T02:11:03.512Z",
  "deployedAt": "2026-07-29T11:04:22.000Z",
  "oauth": { "web": "ok", "desktop": "ok", "sharedClientId": false, "redirectAllowlistCount": 3 }
}
```

| 필드 | 타입 | 비고 |
|------|------|------|
| `status` | string | 항상 `"ok"` |
| `time` | string(ISO8601 UTC) | 서버 현재 시각 |
| `deployedAt` | string(ISO8601 UTC) **또는 필드 부재** | **유효한 `X-MCPhoto-Client`를 제시했을 때만** 포함. 스탬프가 없으면 키가 유효해도 생략 |
| `oauth` | object **또는 필드 부재** | 동상(유효 키일 때만). 구성 로드가 실패하면 생략된다. **2026-08-01 신설** — 진단 모달의 [웹 OAuth 구성] 신호 |

`oauth` 하위(전부 필수):

| 필드 | 타입 | 값 |
|------|------|-----|
| `web` · `desktop` | string | `"ok"`(형식 정상) · `"malformed"`(값은 있으나 `….apps.googleusercontent.com`이 아니다 — **플레이스홀더 미치환**) · `"unset"`(미구성 = 그 종류는 501) |
| `sharedClientId` | boolean | web·desktop이 **같은 client_id**다(유형이 다르면 공유할 수 없으므로 오구성) |
| `redirectAllowlistCount` | number | `OAUTH_REDIRECT_ALLOWLIST` 항목 수 |

> ⚠️ **`oauth`에는 client_id 값·길이·앞자리가 어떤 형태로도 담기지 않는다**(열거값과 개수뿐 — `domain/oauthStatus.ts`, 테스트가 고정). 게이트 키를 "설정됨/미설정"만 보여 주는 것과 같은 수준이다.
> ⚠️ 클라는 이 필드를 **경계에서 검증**해야 한다 — 구버전 서버에는 없으므로, 없거나 형식이 어긋나면 **"미설정"이 아니라 "알 수 없음"** 으로 접는다(`healthService.parseOAuthConfigStatus`).

- 키가 없거나 틀려도 **200**이다(무인증 헬스 체크를 500/401로 바꾸지 않는 설계). 따라서 **헬스 응답으로 게이트 키 유효성을 판정할 수 없다** — `deployedAt` 유무가 힌트일 뿐이며, 확정하려면 `GET /frames/default` 같은 apiKey 게이트 엔드포인트로 401을 확인해야 한다.

---

### 4.2 `POST /auth/google` — 로그인(유일한 인증 경로)

**게이트**: `requireApiKey` (로그인 전이므로 Bearer 없음)

**요청**
```json
{
  "code": "4/0AeanS0...",
  "codeVerifier": "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
  "redirectUri": "http://127.0.0.1:53412/",
  "nonce": "n-0S6_WzA2Mj",
  "clientKind": "desktop"
}
```

| 필드 | 필수 | 검증 |
|------|:----:|------|
| `code` | ✅ | 문자열, 트림 후 1~2048자 |
| `codeVerifier` | ✅ | RFC 7636 — `^[A-Za-z0-9\-._~]{43,128}$` |
| `redirectUri` | ✅ | **허용 목록(완전 일치) 또는 loopback**. ① `OAUTH_REDIRECT_ALLOWLIST`(CSV env)에 **정확히 일치**하면 통과 ② 아니면 loopback 규칙: scheme `http`, host `127.0.0.1`/`localhost`, 경로 `/` 또는 없음, 쿼리·프래그먼트·인증정보 금지, 포트 1~65535 선택. 총 길이 ≤256 |
| `nonce` | — | 있으면 `^[A-Za-z0-9\-._~]{1,256}$`. id_token의 `nonce`와 대조된다 |
| `clientKind` | — | `desktop` \| `web`. **미지정 = `desktop`**(하위 호환). 그 외 문자열은 400. 선택된 종류의 client_id/secret 쌍으로 code를 교환한다 |

> **검사 순서가 계약이다(허용 목록 먼저).** 웹 개발용 `http://localhost:5173/oauth2callback`은 loopback처럼 보이지만 loopback 규칙은 경로 `/`만 허용한다 — 순서를 뒤집으면 허용 목록에 등록해도 영구히 400이 된다.
>
> **prefix 매칭은 쓰지 않는다.** 허용 목록에 `https://a.web.app/oauth2callback`이 있어도 `https://a.web.app.evil.com/oauth2callback`은 거부된다(open redirect·SSRF 방어). 이 값은 서버가 Google에 보내는 code 교환 요청에 그대로 실린다.
>
> **audience는 목록이다.** 구성된 모든 client_id(`GOOGLE_OAUTH_CLIENT_ID`, `GOOGLE_OAUTH_CLIENT_ID_WEB`)를 `verifyIdToken`에 넘기고 `payload.aud`가 그 목록에 **포함**되는지 확인한다. code 교환이 이미 한 클라이언트로 고정되므로 목록은 **우리 소유 클라이언트끼리만** 넓힌다.

**응답 200**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 28800,
  "user": {
    "id": "devmcjo",
    "role": "admin",
    "createdAt": "2025-11-02T08:31:00.000Z",
    "email": "devmcjo@gmail.com",
    "authMethod": "google",
    "hasPin": true
  }
}
```

**오류**

| 상태·코드 | 원인 | 클라이언트 처리 |
|-----------|------|-----------------|
| 501 `not_implemented` | 구성된 OAuth 클라이언트가 하나도 없음, **또는 요청한 `clientKind`가 미구성**(예: 웹 client_id 없이 `clientKind:"web"`), **또는 Google이 `invalid_client`/`unauthorized_client`로 code 교환을 거부**(운영자 구성 오류 — 계정 존재 여부와 무관한 사유이므로 401 일반화(열거 방지)의 대상이 아니다) | *"Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요."* |
| 400 `invalid_argument` | 위 필드 형식 위반 | 개발 오류(정상 흐름에선 발생하지 않아야 한다) |
| 401 `unauthorized` | code 교환 실패(`invalid_grant` 등 — **`invalid_client`·`unauthorized_client`는 제외**, 위 501) / id_token 검증 실패(aud·iss·exp 불일치, `email_verified=false`, nonce 불일치, 허용 도메인(`GOOGLE_ALLOWED_HD`) 밖) / 계정 매핑 실패 | *"이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요."* — 서버가 사유를 **일반화**한다(계정 열거 방지). 상세 사유는 서버 로그에만 남는다 |

**서버가 하는 계정 처리**

1. 검증된 email(소문자)로 계정 조회.
2. **있으면 그대로 로그인** — `role`·`authMethod`를 갱신하지 않는다(승격된 계정이 재로그인으로 강등되지 않음, DB write 없음).
3. **없으면 자동 생성**, 역할은 **무조건 `temp_user`**.
4. 계정 id는 email local-part에서 파생: 소문자화 → `[A-Za-z0-9._-]` 외 제거 → 3자 미만이면 `0`으로 우측 패딩 → 40자 초과면 절단 → 충돌 시 `-2`/`-3`…(최대 9999) → 전부 제거되면 `g-{uuid8}`.
5. 동시 첫 로그인 경합은 create 실패 → 재조회 → 로그인으로 흡수.

> **플랫폼 주의**: `redirectUri` 검증이 loopback만 허용하므로 **iOS/Android/웹 클라이언트는 현재 이 엔드포인트를 쓸 수 없다.** 필요한 서버 변경은 [05 §9](./05-cross-platform-client-guide.md), 플랫폼별 흐름은 [61](./61-auth-platform-integration.md).

---

### 4.3 `GET /accounts` — 계정 목록

**게이트**: Bearer + power

**응답 200** — `UserResponse[]`
```json
[
  { "id": "devmcjo", "role": "admin", "createdAt": "2025-11-02T08:31:00.000Z",
    "email": "devmcjo@gmail.com", "authMethod": "google", "hasPin": true },
  { "id": "guest-kim", "role": "temp_user", "createdAt": "2026-07-28T02:00:00.000Z",
    "email": "guest.kim@example.com", "authMethod": "google", "hasPin": false }
]
```

| 필드 | 타입 | 비고 |
|------|------|------|
| `id` | string | 계정 id(문서 ID와 동일) |
| `role` | string | `temp_user` \| `user` \| `advanced_user` \| `manager` \| `admin`. **미지원 값은 서버가 `user`로 폴백**해 내려준다 |
| `createdAt` | string(ISO8601 UTC) | TempUser 시간 한도의 기준점 |
| `email` | string \| **null** | 방어적으로 null 허용 |
| `authMethod` | string | 현재 `"google"` 고정. **모르는 값이면 클라는 "알 수 없음"으로 표시**하고 임의 해석하지 않는다 |
| `hasPin` | bool | `pinHash != null` 파생값. **PIN 해시 원문은 어떤 응답에도 실리지 않는다** |

- **403이면 빈 배열로 폴백하지 말고 오류로 표시**한다(권한 없음을 "계정 0개"로 오인시키지 않는다).

---

### 4.4 `GET /accounts/me/qr-usage` — 본인 무료 사용 게이트 상태

**게이트**: Bearer (본인 고정 — `principal.id` 사용)

**응답 200**
```json
{
  "role": "temp_user",
  "blocked": false,
  "reason": "ok",
  "remainingMs": 122400000,
  "remainingCount": 27,
  "limits": { "qrHours": 48, "qrCount": 30 }
}
```

| 필드 | 타입 | 의미 |
|------|------|------|
| `role` | string | 조회자 역할 |
| `blocked` | bool | 초과(거부) 여부 |
| `reason` | `"ok"` \| `"time"` \| `"count"` | 사유. **둘 다 초과면 `time` 우선** |
| `remainingMs` | int | 시간 잔여(ms). 초과 시 0 |
| `remainingCount` | int | 횟수 잔여. 초과 시 0 |
| `limits` | `{qrHours, qrCount}` | 현재 적용 중인 전역 한도 |

- **`role != "temp_user"`이면 계정 문서를 읽지 않고** `blocked:false, reason:"ok", remainingMs:0, remainingCount:0`을 돌려준다. 즉 non-TempUser의 `remaining*`은 **"0"이지만 무제한**이라는 뜻이다 — `role`을 먼저 보고 해석해야 한다.
- 판정식: `timeExceeded = (now - createdAt) >= qrHours*3600000`, `countExceeded = qrUsedCount >= qrCount`. 경계는 **`>=`(초과)**.
- 404 `not_found`: TempUser인데 계정 문서가 없는 비정상 상태.
- **이 조회 실패는 fail-open**으로 다뤄야 한다(허용하고 진행). 과금 안전은 업로드 단계의 서버 거부가 담보한다.

---

### 4.5 PIN 엔드포인트 3종

PIN은 **정확히 4자리 숫자**(`^\d{4}$`)이며 서버에 bcrypt 해시로 저장된다. 형식 위반은 400 `invalid_argument` "PIN은 4자리 숫자여야 합니다."

> `me/pin*` 경로는 파라미터 라우트(`/:id/pin`)보다 **먼저** 등록돼 있어 `"me"`가 `:id`로 잡히지 않는다.

#### `POST /accounts/me/pin/verify` — 진입 게이트 확인

**게이트**: Bearer · **요청** `{ "pin": "1234" }`

| 결과 | 응답 |
|------|------|
| 일치 | **200** `{ "ok": true }` |
| 불일치 | **401** `unauthorized` "PIN이 일치하지 않습니다." |
| **PIN 미설정**(계정 문서 부재도 동일) | **409** `conflict` "설정 진입 PIN이 설정되지 않았습니다." → 클라이언트는 **최초 설정 플로우로 유도** |
| 형식 위반 | 400 |

- **서버에 계정 잠금(lockout)이 없다.** 타인 계정을 일부러 틀려 잠그는 DoS를 피하기 위한 의도적 선택이다. 브루트포스 완화는 **클라이언트 책임**이며 현행 규약은 ① 연속 5회 불일치 시 입력 창 닫기(게이트 미통과) ② 불일치마다 1.5초 입력 비활성이다.
- **네트워크·서버 오류(409 포함)는 실패 횟수에 세지 않는다.** 장애로 정상 사용자가 잠기지 않게 하면서도 게이트는 열지 않는다(fail-closed).

#### `PUT /accounts/me/pin` — 본인 PIN 설정/변경

**게이트**: Bearer · **요청** `{ "newPin": "5678", "currentPin": "1234" }`

| 조건 | 동작 |
|------|------|
| 기존 PIN **있음** | `currentPin` 필수. 누락·불일치 시 **401** "현재 PIN이 올바르지 않습니다." |
| 기존 PIN **없음**(최초 설정) | `currentPin` 생략 가능(`undefined`/`null`/`""` 모두 미제공으로 취급) |
| 성공 | **204 No Content** (본문 없음) |
| 계정 문서 부재 | 404 |

#### `PUT /accounts/{id}/pin` — 타 계정 PIN 재설정

**게이트**: Bearer + **power** + `canResetPin(actor.role, targetRole)` = **대상이 엄격히 낮은 위계**(동급 차단) · **요청** `{ "newPin": "0000" }`

| 조건 | 동작 |
|------|------|
| 성공 | **204** |
| `{id}` == 본인 | **400** `invalid_argument` "본인 PIN은 이 경로로 변경할 수 없습니다(본인 PIN 변경 경로 사용)." |
| 비power | **403** |
| 위계 위반 — 상위 대상(manager→admin) | **403** |
| 위계 위반 — **동급 대상**(manager→manager, admin→admin) | **403** — 매니저 PIN은 **admin만** 재설정 가능 |
| 대상 없음 | 404 |

- 관리자는 **새 PIN을 직접 정한다**(고정값·자동 생성이 아니다). 현행 클라이언트는 2회 입력 일치를 요구한다.
- ⚠️ 형제 라우트(`DELETE /accounts/{id}`·`PATCH /accounts/{id}/role`)가 쓰는 `canManage`는 **동급을 허용**한다. PIN 재설정만 `canResetPin`으로 한 칸 좁다 — PIN이 설정·계정 관리 진입의 유일한 자격증명이기 때문이다(`web/functions/src/domain/roles.ts` `canResetPin`).

---

### 4.6 `DELETE /accounts/{id}` — 계정 삭제(cascade)

**게이트**: Bearer + power + `canManage` + 자기 자신 금지

| 조건 | 동작 |
|------|------|
| 성공 | **204** |
| `{id}` == 본인 | **403** `forbidden` "자기 자신은 삭제할 수 없습니다." |
| id 형식 위반 | 400 (`^[A-Za-z0-9._-]{3,40}$`) |
| 위계 위반 | 403 |
| 대상 없음 | 404 |

**서버가 함께 하는 일 (cascade)**
1. 대상 역할 조회 → `canManage` 검사.
2. 소유 프레임 정리 — Firestore `frameTemplates` 중 `userId == {id}` 문서 **배치 삭제** + Storage `frames/{id}/` 프리픽스 전체 삭제.
3. `users/{id}` 문서 삭제.

> Firestore 배치 삭제가 실패하면 **`deleteAccount` 전체가 예외로 중단**되어 계정 문서가 남는다("계정만 지워지고 프레임이 고아로 남는" 상태를 만들지 않는 방향). Storage 삭제 실패만 무시하고 진행한다. 클라이언트는 별도 프레임 삭제 호출을 하지 않는다.

---

### 4.7 `PATCH /accounts/{id}/role` — 역할 변경

**게이트**: Bearer + power + `canSetRole(actor, current, target)` · **요청** `{ "role": "advanced_user" }`

| 조건 | 동작 |
|------|------|
| 성공 | **204** |
| 역할 화이트리스트 밖 | 400 "역할이 올바르지 않습니다(temp_user/user/advanced_user/manager/admin)." |
| 매트릭스 위반 | **403** |

**`canSetRole` 규칙 (클라이언트 콤보 필터를 이 순서 그대로 구현할 것)**

```
1) target == admin   → 거부  (admin 지정은 누구도 불가 — "최종 1인")
2) current == admin  → 거부  (admin 대상 변경 불가)
3) actor == admin    → 허용  (target ∈ {temp_user, user, advanced_user, manager})
4) actor == manager  → current·target 둘 다 {temp_user, user, advanced_user} 안일 때만 허용(승격 포함)
5) 그 외 actor       → 거부
```

- **no-op(current == target)은 허용**되지만 클라이언트는 무변경을 서버로 보내지 않는다.
- 콤보 옵션 표시 순서는 **위계 오름차순**(`temp_user` → `user` → `advanced_user` → `manager`)으로 고정한다.
- 자기 계정 행은 콤보를 노출하지 않는다.
- 전수 표는 [60 §1.4](./60-auth-accounts-and-roles.md#14-역할-지정변경-매트릭스).

---

### 4.8 `GET /config/temp-user-limits` — 전역 무료 한도 조회

**게이트**: Bearer (모든 로그인 사용자 — 표시용)

**응답 200** `{ "qrHours": 48, "qrCount": 30 }` — 설정 문서가 없으면 이 **기본값**으로 폴백한다.

### 4.9 `PATCH /config/temp-user-limits` — 전역 무료 한도 수정

**게이트**: Bearer + **admin** · **요청**(둘 다 선택, 최소 1개 필수)
```json
{ "qrHours": 72, "qrCount": 50 }
```

| 필드 | 검증 |
|------|------|
| `qrHours` | 정수 **1~8760**(1시간~1년) |
| `qrCount` | 정수 **1~100000** |
| 둘 다 미제공 | 400 "qrHours 또는 qrCount 중 최소 하나가 필요합니다." |

**응답 200** — 갱신된 전체 한도 `{ "qrHours": 72, "qrCount": 50 }`

- 한도는 **전역 1쌍**이고 사용량(`qrUsedCount`)은 **계정별**이다. 한도를 올리면 횟수 초과 계정이 회복되지만, **시간 초과는 계정 `createdAt` 기준이라 `qrHours`를 늘려야 회복**된다.

---

### 4.10 `GET /frames/default` — 공용 기본 프레임 목록

**게이트**: `requireApiKey` (게스트 조회 가능, Bearer 불요)

**응답 200** — `FrameResponse[]`
```json
[
  {
    "id": "8f2c1a90-3d5e-4b17-9c22-0ab7de441f03",
    "userId": null,
    "isDefault": true,
    "name": "베이직 4컷",
    "imageUrl": "https://firebasestorage.googleapis.com/v0/b/mcphoto-955fb.firebasestorage.app/o/frames%2Fdefault%2F8f2c1a90-3d5e-4b17-9c22-0ab7de441f03.png?alt=media&token=1e7c...",
    "imageSize": { "width": 1200, "height": 1600 },
    "slots": [
      { "index": 0, "x": 80,  "y": 140, "width": 480, "height": 640 },
      { "index": 1, "x": 640, "y": 140, "width": 480, "height": 640 },
      { "index": 2, "x": 80,  "y": 840, "width": 480, "height": 640 },
      { "index": 3, "x": 640, "y": 840, "width": 480, "height": 640 }
    ],
    "createdAt": "2026-05-11T04:22:10.000Z"
  }
]
```

| 필드 | 타입 | 비고 |
|------|------|------|
| `id` | string | 서버가 `randomUUID()`로 부여. **클라이언트가 정하지 않는다** |
| `userId` | string \| null | 공용 기본 프레임은 **null** |
| `isDefault` | bool | true면 게스트에게도 노출 |
| `name` | string | 1~100자, `_` 불가 |
| `imageUrl` | string | 다운로드 토큰 URL. 이미지 GET에 인증 불요 |
| `imageSize` | `{width, height}` | 등록 원본 픽셀 크기. **슬롯 좌표계의 기준** |
| `slots` | array | `{index, x, y, width, height}` 1~6개, 프레임 픽셀 좌표 |
| `createdAt` | string(ISO8601) | |

- 클라이언트는 이미지를 **로컬에 캐시**해 재다운로드를 피하는 것이 권장 동작이다(현행 구현은 이름 기준 dedup으로 중복 다운로드를 막는다, [13 §5](./13-client-behavior-spec.md)).
- **`imageUrl`이 있는데 이미지 GET이 실패할 수 있다**: 서버는 문서를 먼저 만들고 이미지 PUT은 클라이언트가 나중에 하므로, PUT 실패 시 이미지 없는 문서가 남는다(수용된 트레이드오프). 클라이언트는 이미지 로드 실패를 **크래시 없이** 처리해야 한다.

### 4.11 `GET /frames?userId={id}` — 특정 계정 프레임 목록

**게이트**: Bearer. `userId != principal.id`인데 비power면 **403** "다른 계정의 프레임을 조회할 수 없습니다."

**응답 200** — `FrameResponse[]` (구조 동일, `userId` non-null, `isDefault` false)

> ℹ️ 현재 정책상 **일반 사용자 커스텀 프레임은 서버에 올라가지 않는다**(클라이언트 로컬 전용, [41 §3](./41-local-data-and-file-formats.md)). 이 엔드포인트는 레거시 문서 조회용으로 남아 있으며 보통 빈 배열을 돌려준다.

### 4.11a `POST /frames/mine` — **개인 프레임 생성** (advanced_user 이상, 2026-08-07 신설)

**게이트**: Bearer + `requireFrameWrite`(advanced_user 이상). ⚠️ `requirePower`와 **다른 축**이다 — 공용은 power, 개인은 저작 권한.

**요청**: `POST /frames`와 동일 DTO(`{name, imageSize, slots}`). `userId`·`isDefault`는 **서버가 강제**한다(body 값 무시) — `userId = principal.id`, `isDefault = false`.

**응답 201**: `{frame, upload}` — `POST /frames`와 동일 형태.

| 검증 | 내용 |
|------|------|
| 이름 | 1~100자, `_` 금지(기존 `validateFrameName`) |
| **이름 중복** | 같은 계정 안에서 같은 이름이면 **409**. 클라 사전 검증만으로는 PC 두 대 동시 생성을 막을 수 없다 |
| **개수 상한** | **없다**(2026-08-07 폐지). 총량 방어는 이미지 8MB뿐 |
| 이미지 크기 | 서명 URL에 `x-goog-content-length-range: 0,8388608` 조건이 포함되어 **GCS가 8MB 초과를 거부**한다(클라 우회 불가) |
| Storage 경로 | `frames/users/{계정id}/{frameId}.png` |

### 4.12 `POST /frames` — 공용 기본 프레임 생성 (power)

**게이트**: Bearer + power

**요청**
```json
{
  "name": "여름 시즌 6컷",
  "isDefault": true,
  "imageSize": { "width": 1200, "height": 1800 },
  "slots": [ { "index": 0, "x": 60, "y": 100, "width": 500, "height": 667 } ]
}
```

| 필드 | 검증 |
|------|------|
| `name` | 문자열, 트림 후 1~100자, **`_` 포함 금지** |
| `imageSize` | `{width, height}` 모두 정수 > 0 |
| `slots` | 배열 1~6개. 각 항목 `index`·`x`·`y` 음이 아닌 정수, `width`·`height` 정수 > 0 |
| `isDefault` | **서버가 무시하고 `true`로 강제** |

- 서버는 클라이언트가 보낸 값과 무관하게 **`userId=null`, `isDefault=true`** 로 고정한다. 즉 이 엔드포인트로 만들 수 있는 것은 **공용 기본 프레임뿐**이다.
- 계정당 10개 제한은 `userId`가 있을 때만 검사한다(공용 생성에는 적용되지 않음). 초과 시 409.

**응답 201**
```json
{
  "frame": { "...FrameResponse..." },
  "upload": {
    "putUrl": "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/frames/default/8f2c...png?X-Goog-Algorithm=...",
    "downloadUrl": "https://firebasestorage.googleapis.com/v0/b/.../o/frames%2Fdefault%2F8f2c...png?alt=media&token=1e7c...",
    "requiredHeaders": {
      "Content-Type": "image/png",
      "x-goog-meta-firebaseStorageDownloadTokens": "1e7c8b3a-..."
    }
  }
}
```

**클라이언트 2단계 절차**
1. 메타를 POST → `{frame, upload}` 수신 (이 시점에 **문서가 이미 생성됨**).
2. `upload.putUrl`에 **PNG 바이트를 PUT**하며 `requiredHeaders`를 전부 부착.

- Storage 경로는 서버가 정한다: `frames/{userId ?? "default"}/{frameId}.png`. 항상 PNG다.
- 2단계가 실패하면 이미지 없는 문서가 남는다 → 같은 이름으로 다시 저장(새 문서)하거나 `DELETE /frames/{id}`로 정리한다.

### 4.13 `PUT /frames/{id}` — 공용 프레임 갱신 (power, 운영 도구 전용)

**게이트**: Bearer + power · **요청** `{name, imageSize, slots, replaceImage?}`

**응답 200** `{ "frame": {...}, "upload": {...}? }` — `replaceImage:true`일 때만 `upload`가 포함된다(false/미지정이면 메타만 갱신, 이미지 보존). `isDefault`·`userId`는 서버가 보존한다.

> ⚠️ **현행 클라이언트는 이 엔드포인트를 호출하지 않는다.** "프레임 편집은 해당 기기에서만 적용" 정책이라 편집 저장은 로컬 분기(사본)로 처리된다([13 §6](./13-client-behavior-spec.md)). 새 클라이언트도 같은 정책을 따라야 한다 — 라우트는 운영/관리 도구를 위해 남아 있을 뿐이다.

### 4.14 `DELETE /frames/{id}` — 프레임 삭제

> ⚠️ **2026-08-07 게이트 완화**: `requirePower` → `requireFrameWrite` + 핸들러 분기.
> **본인 소유 프레임은 본인이** 삭제한다(개인 프레임이 서버 정본이 되면서 필수가 됐다 — 종전에는
> advanced_user가 자기 프레임을 서버에서 지울 방법이 없었다). **공용 기본 프레임은 종전대로 power만.**
> 타인의 개인 프레임 삭제는 403. 문서가 없으면 종전 계약대로 `200 {deleted:false}`.

**게이트**: Bearer + power

**응답 200** `{ "deleted": true }` 또는 `{ "deleted": false }`

- **`deleted:false`는 "문서를 찾지 못했다"는 뜻이며 성공이 아니다.** 클라이언트는 이를 성공으로 오인하지 말고 사용자에게 알려야 한다(M4).
- 서버는 문서 삭제 **전에** owner를 읽어 Storage 경로를 확정한 뒤 이미지도 지운다(고아 이미지 방지).

---

## 5. 업로드 3단계 (P2 촬영 클라이언트의 핵심)

```
① POST /uploads/prepare      → 파일별 서명 PUT URL + 다운로드 URL + 필수 헤더
② PUT {putUrl}               → 파일 바이트 직접 전송 (백엔드 함수 미경유)
③ POST /uploads/commit       → resultSessions 문서 생성 → 다운로드 페이지 활성화
```

파일이 2개(사진 + 타임랩스)면 ①을 **파일별로 호출**하거나 `files` 배열에 둘을 함께 담을 수 있다(서버는 배열을 지원하며 `kind` 중복만 거부한다). 현행 Windows 클라이언트는 **파일당 1회씩 prepare**를 호출한다.

### 5.1 `POST /uploads/prepare`

**게이트**: apiKey + optionalBearer (게스트 = 무토큰 통과)

**요청**
```json
{
  "sessionId": "20260730_111203_9f1c2a44-5b6d-4e7f-8a90-1b2c3d4e5f60",
  "files": [
    { "kind": "final",     "ext": "jpg", "contentType": "image/jpeg" },
    { "kind": "timelapse", "ext": "mp4", "contentType": "video/mp4" }
  ]
}
```

| 필드 | 검증 |
|------|------|
| `sessionId` | 정규식 `^\d{8}_\d{6}_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$` (§7) |
| `files` | 비어 있지 않은 배열. `kind` 중복 시 400 "중복된 파일 종류입니다: {kind}" |
| `files[].kind` | `"final"` 또는 `"timelapse"` |
| `files[].ext` | `final` → `jpg`\|`png` / `timelapse` → `mp4` |
| `files[].contentType` | `final` → `image/jpeg`\|`image/png` / `timelapse` → `video/mp4` |

**응답 200**
```json
{
  "uploads": [
    {
      "kind": "final",
      "putUrl": "https://storage.googleapis.com/...?X-Goog-Algorithm=GOOG4-RSA-SHA256&...",
      "downloadUrl": "https://firebasestorage.googleapis.com/v0/b/{bucket}/o/results%2F{sid}%2Ffinal.jpg?alt=media&token={t}",
      "requiredHeaders": {
        "Content-Type": "image/jpeg",
        "x-goog-meta-firebaseStorageDownloadTokens": "{t}"
      }
    }
  ],
  "bucket": "mcphoto-955fb.firebasestorage.app"
}
```

| 필드 | 의미 |
|------|------|
| `uploads[].putUrl` | GCS **V4 서명 PUT URL. TTL 15분** |
| `uploads[].downloadUrl` | 업로드 후 파일을 읽을 토큰 URL. **commit에 이 값을 그대로 넘긴다** |
| `uploads[].requiredHeaders` | PUT 시 **반드시 그대로 전부** 부착. 하나라도 빠지면 서명 불일치 또는 다운로드 토큰 미설정 |
| `bucket` | 실제 사용 버킷. 클라이언트가 로컬 버킷 설정을 이 값으로 갱신하면 URL 재조립이 서버와 일치한다 |

**오류**

| 상태·코드 | 원인 |
|-----------|------|
| 400 `invalid_argument` | `sessionId` 형식, `files` 누락/빈 배열, kind/ext/contentType 불일치, kind 중복 |
| 401 `unauthorized` | 게이트 키 무효 / **위조된 Bearer**(토큰 없음은 통과) |
| **403 `TEMP_USER_TIME_EXCEEDED`** \| **`TEMP_USER_COUNT_EXCEEDED`** | 로그인 계정이 TempUser이고 한도 초과 → **서명 URL을 아예 발급하지 않는다**(직접 PUT 과금 원천 차단) |

- TempUser 계정 문서가 없으면 prepare는 **거부하지 않고 통과**시킨다(commit 트랜잭션이 최종 권위).

### 5.2 ② 서명 URL로 직접 PUT

```
PUT {putUrl}
Content-Type: image/jpeg
x-goog-meta-firebaseStorageDownloadTokens: {t}

<파일 바이트>
```

| 규칙 | 내용 |
|------|------|
| 인증 | **없다.** 서명 URL 자체가 권한이다. `X-MCPhoto-Client`·`Authorization`을 붙이지 않는다 |
| 헤더 | `requiredHeaders`를 **정확히** 부착. 값·이름 대소문자 변형 금지 |
| 유효 시간 | 15분. 초과 시 GCS가 4xx |
| 진행률 | 바이트 단위 진행률은 클라이언트가 스트림 래핑으로 계산한다(서버 기능 아님) |
| 실패 | HTTP 4xx/5xx 또는 네트워크 오류. **commit을 호출하지 않고** 실패 처리 |
| 브라우저 | 버킷 CORS에 `PUT`·`Content-Type`·`x-goog-meta-firebaseStorageDownloadTokens`가 허용돼야 한다([05 §9 B5](./05-cross-platform-client-guide.md)) |

### 5.3 `POST /uploads/commit`

**게이트**: apiKey + optionalBearer

**요청**
```json
{
  "sessionId": "20260730_111203_9f1c2a44-5b6d-4e7f-8a90-1b2c3d4e5f60",
  "finalImageUrl": "https://firebasestorage.googleapis.com/v0/b/{bucket}/o/results%2F{sid}%2Ffinal.jpg?alt=media&token={t}",
  "timelapseUrl": null,
  "retentionHours": 24,
  "downloadPageUrl": "https://mcphoto-955fb.web.app/?s=20260730_111203_9f1c2a44-5b6d-4e7f-8a90-1b2c3d4e5f60"
}
```

| 필드 | 필수 | 검증 |
|------|:----:|------|
| `sessionId` | ✅ | §7 형식 |
| `finalImageUrl` | △ | 문자열이 아니면 null 취급 |
| `timelapseUrl` | △ | 동상 |
| `retentionHours` | ✅ | **정수 1~72** |
| `downloadPageUrl` | ✅ | 비어 있지 않은 문자열 |

**서버 검증(위조 방어)**
- **최소 1개 불변식**: 둘 다 없으면 400 "전송할 미디어가 없습니다(사진·타임랩스 모두 없음). 최소 1개 필요."
- **URL 소속 검증**: 각 URL이 `https://firebasestorage.googleapis.com/v0/b/{서버버킷}/o/` 로 시작하고, 디코드된 경로가 `results/{sessionId}/final.` (final) 또는 정확히 `results/{sessionId}/timelapse.mp4` (timelapse)여야 한다. 위반 시 400. → **prepare 없이 임의 URL을 심을 수 없다.**

**응답 201**
```json
{
  "id": "20260730_111203_9f1c2a44-5b6d-4e7f-8a90-1b2c3d4e5f60",
  "finalImageUrl": "https://firebasestorage.googleapis.com/...",
  "timelapseUrl": null,
  "createdAt": "2026-07-30T02:12:11.004Z",
  "expiresAt": "2026-07-31T02:12:11.004Z",
  "downloadPageUrl": "https://mcphoto-955fb.web.app/?s=20260730_111203_..."
}
```

- **`createdAt`·`expiresAt`은 서버가 commit 시점에 계산**한다(`expiresAt = createdAt + retentionHours`). 클라이언트가 보낸 시각은 문서에 들어가지 않는다 — 클라이언트는 `retentionHours`(시간 차이)만 보낸다.

**오류**

| 상태·코드 | 원인 |
|-----------|------|
| 400 | 위 검증 실패 |
| 401 | 게이트 키 무효 / 위조 Bearer / (TempUser인데 계정 문서 없음 → `unauthorized` "계정을 찾을 수 없습니다.") |
| **403 `TEMP_USER_*`** | 트랜잭션 재검사에서 한도 초과 |
| **409 `conflict`** | 동일 `sessionId` 재commit — "이미 존재하는 세션입니다: {sid}". **이중집계 차단 장치이므로 정상 동작이다** |

### 5.4 TempUser 무료 한도의 정확한 동작

| 지점 | 동작 |
|------|------|
| `prepare` | TempUser면 선검사 → 초과 시 403, **서명 URL 미발급** |
| `commit` | TempUser면 **단일 트랜잭션**으로 (세션 중복 검사 409 → 한도 재판정 403 → 문서 생성 → `qrUsedCount += 1`) |
| 카운트 단위 | **세션당 1**(파일 개수 무관). "성공 세션 1회 = commit 최초 성공" |
| 게스트·`user` 이상 | 한도·카운트 없음(비트랜잭션 경로) |

---

## 6. 다운로드 웹 페이지가 읽는 것 (P1 소비자 클라이언트)

P1은 백엔드 API를 쓰지 않는다. **Firestore `resultSessions/{token}` 단건 조회**와 **문서에 담긴 토큰 URL 직접 GET**만 한다.

| 항목 | 값 |
|------|-----|
| 진입 URL | `{hostingBaseUrl}/?s={token}` — `token` = 세션 ID |
| 조회 | `resultSessions` 문서 **단건 get만**. 목록·쿼리(list)는 보안 규칙이 거부한다 |
| 읽는 필드 | `finalImageUrl`, `timelapseUrl`, `expiresAt` (3개) |
| 파일 접근 | 토큰 URL(`?alt=media&token=…`)을 브라우저가 직접 GET. **인증·Storage 규칙 불요**(토큰 URL이 곧 권한) |
| 쓰기·삭제 | **하지 않는다** |

- 네이티브 클라이언트로 P1을 만들 때도 같은 계약을 쓴다(Firestore REST/SDK 단건 get + URL GET). 상태 판정 규칙은 [13 §12](./13-client-behavior-spec.md).

---

## 7. 세션 ID · 경로 · URL 조립 규약

클라이언트와 서버 양쪽에 이식돼 있고 **한 글자도 달라선 안 된다.**

| 항목 | 규칙 | 예시 |
|------|------|------|
| 세션 ID | `{yyyyMMdd}_{HHmmss}_{UUIDv4}` — 앞 시각 스탬프 + **완전한 UUIDv4** | `20260730_111203_9f1c2a44-5b6d-4e7f-8a90-1b2c3d4e5f60` |
| 시각 기준 | 클라이언트는 **로컬 시간**으로 만든다(서버는 형식만 검증, 값은 검증하지 않는다) | |
| 검증 정규식 | `^\d{8}_\d{6}_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$` | |
| 사진 경로 | `results/{sessionId}/final.{jpg\|png}` | |
| 타임랩스 경로 | `results/{sessionId}/timelapse.mp4` | 항상 mp4 |
| 프레임 경로 | `frames/{userId ?? "default"}/{frameId}.png` | 항상 png. **TTL 삭제 비대상** |
| 다운로드 토큰 URL | `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}` — 경로는 슬래시까지 퍼센트 인코딩(`%2F`) | `…/o/results%2F{sid}%2Ffinal.jpg?alt=media&token=…` |
| 다운로드 페이지 URL | `{hostingBaseUrl 트레일링 슬래시 제거}/?s={sessionId}` | `https://mcphoto-955fb.web.app/?s={sid}` |
| 만료 시각 | `expiresAt = createdAt + retentionHours` (**서버 계산**) | |

**세션 ID 설계 의도**: 시각 접두는 Storage 콘솔에서 `results/` 하위 폴더를 시각순으로 정렬·검색하기 위한 것이다. 뒤의 완전한 UUIDv4(122비트)가 남아 있어 **열거 방어는 유지**된다. 순차 ID는 여전히 금지다. 트레이드오프로 링크에 생성 시각이 노출된다(포토부스 다운로드 링크 특성상 수용).

---

## 8. 입력 검증 규칙 전수 (400의 원인)

| 값 | 규칙 |
|----|------|
| 계정 id | `^[A-Za-z0-9._-]{3,40}$` (트림 후) |
| 역할 | `temp_user` \| `user` \| `advanced_user` \| `manager` \| `admin` |
| 이메일 | 트림·소문자화 후 `^[^\s@]+@[^\s@]+\.[^\s@]+$`, ≤254자 |
| PIN | `^\d{4}$` |
| OAuth `code` | 문자열, 트림 후 1~2048자 |
| PKCE `codeVerifier` | `^[A-Za-z0-9\-._~]{43,128}$` |
| `redirectUri` | http loopback만(§4.2), ≤256자 |
| `nonce` | `^[A-Za-z0-9\-._~]{1,256}$` |
| `retentionHours` | 정수 1~72 |
| `qrHours` | 정수 1~8760 |
| `qrCount` | 정수 1~100000 |
| `slots` | 배열 1~6. `index`/`x`/`y` ≥ 0 정수, `width`/`height` > 0 정수 |
| `imageSize` | `width` > 0, `height` > 0 정수 |
| 프레임 이름 | 트림 후 1~100자, **`_` 금지** |
| 업로드 파일 | `kind`: `final`\|`timelapse`. `final` ext ∈ {jpg,png} / contentType ∈ {image/jpeg,image/png}. `timelapse` ext = mp4 / contentType = video/mp4 |
| `sessionId` | §7 정규식 |
| JSON 본문 | 파싱 실패(문법 오류) 시 **400** "요청 본문이 올바른 JSON이 아닙니다." |

> ⚠️ **본문 256KB 초과는 400이 아니라 500 `internal`로 응답된다.** 에러 미들웨어가 JSON 문법 오류(`SyntaxError`)만 400으로 특별 처리하고, 본문 크기 초과 오류는 그 분기에 걸리지 않아 일반 500으로 떨어진다. 파일 바이트는 API를 경유하지 않으므로 정상 흐름에서는 발생하지 않지만, **클라이언트는 500을 "서버 오류"로만 안내하고 재시도를 권하게 된다** — 요청 본문을 키우는 변경(예: 슬롯 대량 전송)을 할 때 이 함정을 기억할 것.

---

## 9. 서버 구성값 (운영자용 참고)

시크릿은 Secret Manager, 일반 설정은 환경변수. **필수값 누락 시 로드 시점에 예외로 조기 실패**한다(오구성 배포 방지).

| 키 | 출처 | 필수 | 용도 |
|----|------|:---:|------|
| `JWT_SECRET` | Secret Manager | ✅ | JWT(HS256) 서명 |
| `CLIENT_API_KEYS` | Secret Manager (CSV) | ✅ | 유효한 `X-MCPhoto-Client` 키 목록 |
| `GOOGLE_OAUTH_CLIENT_SECRET` | Secret Manager | SSO 사용 시 | Google code 교환 |
| `STORAGE_BUCKET` | env | ✅ | 서명 URL·토큰 URL 조립 |
| `HOSTING_BASE_URL` | env | — | 다운로드 페이지 base URL |
| `JWT_EXPIRES_IN_SECONDS` | env | — | 기본 `28800`(8시간) |
| `GOOGLE_OAUTH_CLIENT_ID` | env | desktop SSO 사용 시 | **desktop 종류 활성화 신호**(Windows 앱) |
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | env | web SSO 사용 시 | **web 종류 활성화 신호**(웹 클라이언트). Desktop 클라이언트와 유형이 달라 **공유할 수 없다** |
| `GOOGLE_OAUTH_CLIENT_SECRET_WEB` | secret | web SSO 사용 시 | 웹 클라이언트 secret. **선언된 시크릿이라 배포 시 반드시 존재해야 한다**(미사용이어도 placeholder 등록) |
| `OAUTH_REDIRECT_ALLOWLIST` | env | web SSO 사용 시 | 허용 `redirectUri` CSV(**완전 일치**). 예: `https://mcphoto-955fb-kiosk.web.app/oauth2callback,http://localhost:5173/oauth2callback` |
| `GOOGLE_ALLOWED_HD` | env | — | 허용 Workspace 도메인(빈 값 = 제한 없음). **종류 무관 공통 적용** |

- `/auth/google`는 **구성된 종류가 하나 이상**이면 활성이다. 요청한 `clientKind`가 미구성이면 그 요청만 501이다.
- 종류별로 "id를 켰는데 secret이 없으면 **조기 실패**" 규칙이 동일하게 적용된다(오구성 배포 방지).
- `GOOGLE_OAUTH_CLIENT_SECRET`(및 `_WEB`)은 배포 시 항상 존재해야 하므로 SSO 미사용이어도 placeholder를 등록한다. 따라서 "시크릿만 있고 client id 없음"은 **정상 비활성** 상태다.
- 게이트 키는 **여러 개 등록 가능**하다(CSV). 플랫폼별로 다른 키를 발급하면 유출 시 해당 키만 폐기할 수 있다 — 새 클라이언트마다 별도 키를 받는 것을 권장한다.

---

## 10. 서버에 없는 것 (클라이언트가 기대하면 안 되는 것)

| 없는 기능 | 대신 무엇을 하나 |
|-----------|------------------|
| 토큰 갱신(refresh) | 401 발생 시 재로그인 유도 |
| 계정 생성 API | Google SSO 최초 로그인 시 서버가 `temp_user`로 자동 생성 |
| 비밀번호 / 이메일 인증 / 비밀번호 재설정 | **개념 자체가 없다.** 자격증명은 Google SSO(신원) + PIN(진입 게이트) 둘뿐 |
| admin 지정 API | `canSetRole` 규칙 1이 거부. 최초/추가 admin은 서버측 마이그레이션 스크립트로만 |
| PIN 서버측 시도 제한·계정 잠금 | 클라이언트 완화(5회·1.5초 쿨다운)가 전부 |
| 만료 결과물 정리 엔드포인트 | 인프라가 담당(GCS Lifecycle `results/` age 3일 + Firestore 네이티브 TTL `expiresAt`) |
| 프레임 목록 페이지네이션 | 전체 반환. 공용 소수 + 계정당 최대 10개 전제 |
| 서버측 이미지 합성·타임랩스 변환 | **전부 클라이언트 책임**([14](./14-media-pipeline-spec.md)) |
| 사용량 통계·감사 로그 API | 없음([90 §2.1](./90-roadmap-and-future-work.md) 장기 항목) |
| 강제 업데이트·최소 버전 확인 | 없음([05 §9 B7](./05-cross-platform-client-guide.md)) |
