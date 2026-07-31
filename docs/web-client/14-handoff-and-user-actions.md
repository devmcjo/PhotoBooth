# 14 · 인수인계와 사용자 액션 (Handoff & User Actions)

| 항목 | 값 |
|------|-----|
| 문서 | **코드로는 끝낼 수 없는 작업**(콘솔·시크릿·배포)의 실행 절차와, 그 시점까지 완료된 구현 상태 |
| 대상 독자 | 저장소 소유자(콘솔·시크릿 권한 보유자) |
| 작성일 | 2026-07-31 |
| 브랜치 | `feature/web-client-foundation` |
| 상태 | **WBS Step 1~5 + 서버 선행작업(B1·B2·B4) 코드 완료.** 아래 A1~A5를 처리하면 Step 6 이후 개발과 실기기 검증이 열린다 |

> **왜 이 문서가 따로 있는가**: 남은 작업이 "덜 만든 코드"가 아니라 **Google Cloud Console 조작 · Secret Manager 등록 · 버킷 CORS · 공개 배포**다. 이들은 자동화 도구가 대신할 수 없거나(콘솔 UI), 대신하면 안 되는(공개 배포) 작업이다. 순서를 틀리면 **배포가 실패**하므로 A1 → A5 순서를 지킨다.

---

## 1. 30초 요약

| # | 사용자 액션 | 없으면 막히는 것 | 소요 |
|---|-------------|------------------|------|
| **A1** | Google Cloud Console에 **Web application** OAuth 클라이언트 생성 | 웹 로그인(Step 12) 전체 | 5분 |
| **A2** | 웹 OAuth **시크릿·env 등록** + functions 재배포 | ⚠️ **A2 없이 functions를 배포하면 실패한다**(§3.2) | 10분 |
| **A3** | 웹 전용 **배포 게이트 키** 발급·등록 | 백엔드 호출 전부 401 | 5분 |
| **A4** | Storage 버킷 **CORS**(업로드 PUT) | 업로드 3단계(Step 11) | 10분 |
| **A5** | kiosk 사이트 **첫 배포** + P1 무변경 확인 | 실기기 검증·CSP 실측 | 5분 |

- A1~A3은 서로 이어진다(A1의 산출물이 A2의 입력).
- **A4·A5는 A1~A3과 독립**이다. 급하면 A4·A5만 먼저 해도 된다 — 게스트 촬영 경로(마일스톤 A)는 로그인이 필요 없다.
- **Step 6(카메라 파이프라인) 개발은 이 중 아무것도 기다리지 않는다.**

---

## 2. A1 · Google OAuth 웹 클라이언트 생성

### 2.1 왜 새로 만드나

현재 등록된 것은 **Desktop app** 유형 1개(Windows 앱용)다. OAuth 클라이언트는 **유형이 다르면 공유할 수 없다** — 웹은 `https://` 리디렉트를 쓰고 Desktop은 loopback을 쓴다. 동의 화면은 프로젝트 단위라 **추가 작업이 없다**.

### 2.2 절차

1. [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → 프로젝트 `mcphoto-955fb`
2. **APIs & Services → Credentials → Create Credentials → OAuth client ID**
3. Application type: **Web application**
4. Name: `MCPhoto Web Kiosk`
5. **Authorized JavaScript origins**
   ```
   https://mcphoto-955fb-kiosk.web.app
   http://localhost:5173
   ```
6. **Authorized redirect URIs** — **완전 일치**해야 한다(문자 하나 다르면 Google이 거부한다)
   ```
   https://mcphoto-955fb-kiosk.web.app/oauth2callback
   https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback
   http://localhost:5173/oauth2callback
   ```
7. 발급된 **client_id**와 **client_secret**을 보관한다(A2·A5에서 쓴다)

| 주의 | 이유 |
|------|------|
| `firebaseapp.com` 도메인을 **빠뜨리지 않는다** | Hosting은 `web.app`·`firebaseapp.com` 두 도메인을 함께 서빙한다. 누락하면 그 도메인으로 접속한 기기에서만 로그인이 실패해 원인 파악이 어렵다 |
| Hosting preview channel 도메인 | 채널 URL에 해시가 붙어 고정할 수 없다. **개발은 `localhost`, 실기기 검증은 운영 사이트**를 쓴다 |
| `GOOGLE_ALLOWED_HD` | 도메인 제한을 쓰고 있다면 웹에도 **그대로 적용**된다(종류 무관 공통). 변경 불필요 |

---

## 3. A2 · 웹 OAuth 시크릿·env 등록

### 3.1 서버가 이미 준비된 것 (이 브랜치에서 완료)

`POST /auth/google`이 **종류별 OAuth 클라이언트**를 지원한다(`clientKind: "desktop" | "web"`, 미지정 = `desktop`). 계약 상세는 [analysis/31 §4.2](../analysis/31-backend-api-reference.md).

| 환경변수 | 종류 | 값 |
|----------|------|-----|
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | env | A1의 client_id |
| `GOOGLE_OAUTH_CLIENT_SECRET_WEB` | **secret** | A1의 client_secret |
| `OAUTH_REDIRECT_ALLOWLIST` | env | A1의 redirect URI 3개를 **CSV로** |

### 3.2 ⚠️ 배포 순서 주의 (먼저 읽을 것)

이 브랜치는 `src/index.ts`에 **`defineSecret("GOOGLE_OAUTH_CLIENT_SECRET_WEB")`을 추가했다.** 선언된 시크릿은 **배포 시점에 반드시 존재해야 한다** — 등록하지 않고 `firebase deploy --only functions`를 실행하면 **배포가 실패한다**.

이것은 기존 `GOOGLE_OAUTH_CLIENT_SECRET`과 **동일한 패턴**이다(그때도 SSO 미사용 상태에서 placeholder를 등록해 뒀다). SSO를 아직 안 쓸 계획이면 **placeholder라도 등록**해 두면 된다 — `GOOGLE_OAUTH_CLIENT_ID_WEB`(env)이 비어 있으면 웹 종류는 그냥 비활성이고(요청 시 501), **Windows(desktop) 경로는 영향받지 않는다**.

### 3.3 절차

```bash
cd web

# 1) 시크릿 등록(프롬프트에 client_secret 입력). 당장 SSO를 안 쓸 거면 placeholder라도 넣는다.
npx firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET_WEB

# 2) env 등록 — functions/.env (gitignore 대상, 커밋 금지)
#    OAUTH_REDIRECT_ALLOWLIST 는 A1에서 등록한 URI와 **완전히 같은 문자열**이어야 한다.
cat >> functions/.env <<'EOF'
GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 client_id>
OAUTH_REDIRECT_ALLOWLIST=https://mcphoto-955fb-kiosk.web.app/oauth2callback,https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback,http://localhost:5173/oauth2callback
EOF

# 3) 재배포(시크릿·env 변경은 재배포가 필요하다)
deploy-web.bat functions
```

### 3.4 검증

| 확인 | 방법 | 기대 |
|------|------|------|
| desktop 경로 무회귀 | Windows 앱으로 로그인 | 종전과 동일하게 성공 |
| 웹 종류 활성 | `POST /auth/google`에 `clientKind:"web"` + 임의 code | **501이 아니라 401**(구성은 됐고 code가 가짜라서 거부) |
| 허용목록 밖 거부 | `redirectUri`를 `https://evil.com/oauth2callback`로 | **400** |
| 서버 회귀 | `cd web/functions && npm test` | 316개 통과 |

> `clientKind`를 **미지정**하면 desktop으로 해석된다 — 배포된 Windows 클라이언트는 **무변경으로 계속 동작**한다.

---

## 4. A3 · 웹 전용 배포 게이트 키

### 4.1 왜 별 키인가

게이트 키는 **인증이 아니라 배포 식별자**다(WD10). 웹 앱의 키는 브라우저에 **공개될 것을 전제**하며, 유출되면 **그 키만 폐기**하고 웹을 재배포한다. 역할·과금 한도는 서버가 JWT로 강제하므로 키 공개로 권한이 새지 않는다.

### 4.2 절차

```bash
# 1) 새 키 생성(32바이트 base64url)
node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"

# 2) CLIENT_API_KEYS 에 CSV로 **추가**한다
cd web
npx firebase functions:secrets:set CLIENT_API_KEYS
#    → 프롬프트에 "<기존 Windows 키>,<새 웹 키>" 를 함께 입력한다

# 3) 재배포
deploy-web.bat functions
```

> ⚠️ **기존 키를 지우면 배포된 Windows 앱이 즉시 죽는다.** 반드시 기존 값 + 콤마 + 새 키다. 기존 키는 `backend-apikey.local`(gitignore)에 있다.

### 4.3 검증

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "X-MCPhoto-Client: <웹 키>" \
  https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/frames/default   # → 200
curl -s -o /dev/null -w "%{http_code}\n" -H "X-MCPhoto-Client: bogus" \
  https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/frames/default   # → 401
```

> `GET /health`로는 키 유효성을 판정할 수 없다 — **키가 없거나 틀려도 200이다**(06 §2.1). 위처럼 `/frames/default`의 401 여부로 확인한다.

---

## 5. A4 · Storage 버킷 CORS (업로드 PUT)

### 5.1 현재 상태

- **다운로드 GET은 CORS 구성이 불필요하다** — `firebasestorage.googleapis.com`이 항상 `Access-Control-Allow-Origin: *`를 반환한다(실측 기록: [`web/OPS-cors.md`](../../web/OPS-cors.md)). 따라서 **서버 프레임 이미지를 canvas로 합성하는 경로(WM2)는 이미 열려 있다.**
- **업로드 PUT은 구성이 필요하다** — 서명 URL의 호스트는 `storage.googleapis.com`이고 브라우저 PUT은 preflight를 보낸다.
- 이 PC에 **`gcloud`가 설치돼 있지 않다**(같은 실측 기록). → **Cloud Shell**을 쓰는 것이 가장 빠르다.

### 5.2 절차 (Cloud Shell)

[Cloud Shell 열기](https://console.cloud.google.com/?cloudshell=true) → 아래를 붙여넣는다.

```bash
cat > /tmp/cors.json <<'EOF'
[
  {
    "origin": [
      "https://mcphoto-955fb-kiosk.web.app",
      "https://mcphoto-955fb-kiosk.firebaseapp.com",
      "http://localhost:5173"
    ],
    "method": ["GET", "PUT", "HEAD"],
    "responseHeader": [
      "Content-Type",
      "x-goog-meta-firebaseStorageDownloadTokens",
      "x-goog-resumable"
    ],
    "maxAgeSeconds": 3600
  }
]
EOF

gcloud storage buckets update gs://mcphoto-955fb.firebasestorage.app --cors-file=/tmp/cors.json
gcloud storage buckets describe gs://mcphoto-955fb.firebasestorage.app --format="default(cors_config)"
```

| 항목 | 이유 |
|------|------|
| `responseHeader`에 `x-goog-meta-firebaseStorageDownloadTokens` | 서명 PUT의 `requiredHeaders`에 이 헤더가 들어 있다. 허용하지 않으면 preflight가 막혀 **PUT이 아예 안 나간다**(M14 파손) |
| `method`에 `PUT` | 업로드 본체 |
| `origin`에 `firebaseapp.com` | Hosting 두 번째 기본 도메인 |
| `localhost:5173` | 로컬 개발에서 업로드를 시험할 때 |

### 5.3 검증

Step 11 구현 후 브라우저 Network 탭에서 `OPTIONS 204 → PUT 200`을 확인한다. 그 전에는 구성 조회(`describe`)로 충분하다.

---

## 6. A5 · kiosk 사이트 첫 배포

### 6.1 준비된 것 (이 브랜치에서 완료)

| 항목 | 상태 |
|------|------|
| kiosk 사이트(`mcphoto-955fb-kiosk`) | **생성 완료** |
| `.firebaserc` 타깃 2개(`default`·`kiosk`) | **등록 완료** |
| `web/firebase.json` hosting 배열화 + kiosk CSP·캐시 헤더 | **완료**(기존 default 블록 무변경) |
| `webclient/deploy.bat` | **완료**(빌드 + `--only hosting:kiosk`) |
| `web/deploy-web.bat` | **`hosting:default`로 고정**(§6.3) |

### 6.2 절차

```bash
# 1) 빌드 주입값 — webclient/.env.production.local (gitignore 대상, 커밋 금지)
cd webclient
cat > .env.production.local <<'EOF'
VITE_BACKEND_API_KEY=<A3의 웹 키>
VITE_GOOGLE_CLIENT_ID=<A1의 client_id>
VITE_APP_VERSION=0.1.0
EOF

# 2) 빌드 + 배포
deploy.bat
```

### 6.3 ⚠️ 기존 P1 배포 스크립트가 바뀌었다

Hosting이 멀티사이트가 되면서 `firebase deploy --only hosting`은 **두 사이트를 동시에** 배포한다. `web/kiosk/`는 gitignore된 빌드 산출물이므로, 클론 직후·CI에서는 **배포가 실패하거나 낡은 빌드가 공개된다**. 그래서 `deploy-web.bat`의 배포 대상을 **`hosting:default`로 고정**했다(근거: [analysis/80 §6.5](../analysis/80-build-and-deployment.md)).

**한 번 실행해 P1 페이지가 그대로인지 확인해 주세요**:

```bash
cd web && deploy-web.bat hosting
```

스크립트가 배포 후 `public/`과 실서버 바이트를 대조한다(기존 검증 로직 그대로).

### 6.4 검증

```bash
# kiosk 사이트가 서빙되고 CSP·nosniff가 붙는가
curl -sI https://mcphoto-955fb-kiosk.web.app/ | grep -iE "content-security-policy|x-content-type-options"

# P1 사이트 무변경
curl -sI https://mcphoto-955fb.web.app/ | head -1
```

| 확인 | 기대 |
|------|------|
| kiosk 화면 | 브랜딩 타이틀 + 하단 캡션 **`v0.1.0`**(배포 채널·빌드 시각 문자열이 **없다** — it18) |
| 브라우저 콘솔 | **CSP 위반 0건** |
| P1 페이지 | 기존과 동일하게 동작(`?s=<유효토큰>`으로 확인) |

> 첫 배포에서는 더미 13화면 전이 UI가 보인다(Step 7부터 실제 화면으로 교체된다). **의도된 상태**다.

---

## 7. 지금까지 완료된 구현 (이 브랜치)

Step별 상세 산출물·검증·이탈 사항은 [11 · WBS](./11-wbs.md)의 각 Step 체크박스에 기록했다. 요약:

| Step | 산출물 | 검증 |
|------|--------|------|
| **1** 스캐폴드 | Vite 5 + React 18 + TS strict, `outDir=../web/kiosk`, `env.ts`(두 URL 정규화 방향 반대·빈 값 폴백), manifest·아이콘·branding.json, Hosting 멀티사이트 | `tsc` + 빌드 |
| **2** 도메인 + 벡터 | `src/domain/**` 28개 모듈(`MCPhoto.Core` 순수 로직 전량), `roundHalfToEven`, **`docs/spec-vectors/` 14파일 271케이스**, `SpecVectorTests.cs` | 웹 커버리지 **domain 100%**, **Windows·웹이 같은 벡터를 읽어 동시 통과**(트리거 확인 완료) |
| **3** 저장 계층 | OPFS **단일 Worker 경계**(3단 능력 판정), `settingsRepo`(알 수 없는 키 보존·게스트 값 보존·저장 boolean), IndexedDB 로그 링버퍼 + 마스킹, 브랜딩 800ms, 부트스트랩 1~6단계 | 79 테스트 |
| **4** 앱 셸 | **M1 배선**(구독 1곳이 모든 로그아웃 경로를 덮음), **M2 메모리 전용 토큰**, 전이·오버레이 복귀·`returnHome` 6단계, **실경과 유휴 감시**, 탭 hidden 취소(WM4), M16 복구, UI 기본 + 더미 13화면 | 36 테스트 |
| **5** HTTP | 예외 5종 매핑(`TEMP_USER_*` 403을 권한 오류와 분리), 게이트 키 전 호출·Bearer 3수준·100초 타임아웃, 서비스 6종, **`PUT /frames/{id}` 미구현**(정책) | 48 테스트 |
| **서버** B1·B2·B4 | `redirectUri` 허용목록(완전 일치), audience 목록 + `clientKind`, 웹 게이트 키 배선 | 서버 316 테스트 |

### 7.1 전체 테스트 현황

```bash
cd webclient     && npx tsc --noEmit && npx vitest run     # 420 통과 (도메인 커버리지 100%)
cd web/functions && npm test                                # 316 통과
dotnet test tests/MCPhoto.Tests                             # 839 통과 (기존 823 무회귀 + 벡터 16)
```

### 7.2 구현 중 발견해 고친 결함 4건

| # | 무엇 | 왜 위험했나 |
|---|------|-------------|
| 1 | `redirectUri` 검사 순서(loopback 우선) | 개발용 `http://localhost:5173/oauth2callback`이 loopback 규칙(경로 `/`만 허용)에 걸려 **허용목록에 등록해도 영구히 400**이었다 → 허용목록 우선으로 교체 |
| 2 | `isStorageLow`의 부동소수 오차 | `1 - 900/1000 = 0.0999…` 때문에 **정확히 임계값(여유 10%)이 경고로 넘어갔다** → 바이트 정수 비교 |
| 3 | `globalErrorHandler` 쿨다운 초기값 `0` | `now()`가 작은 값을 주는 시계에서 **첫 오류 복구가 쿨다운에 먹혔다**(화이트스크린 위험) → `-Infinity` |
| 4 | 로그 마스킹의 `code` 키 | OAuth 인가 코드용 마스킹이 **오류 코드까지 가려** 진단이 불가능했다 → 마스킹 유지 + 로그 필드를 `errorCode`로 분리 |

### 7.3 규격 해석을 확정한 것

**프레임 이름의 `_`** 는 문서 내 모순이 아니라 **스코프가 다른 두 규칙**이었다. 서버가 `POST /frames`의 이름에 `_`가 있으면 **400으로 거부**한다(`web/functions/src/domain/validation.ts`). 따라서:

- **서버 등록 경로**(power 신규 공용 프레임) → **하드 거부**(M15·E13이 맞다) = `validateFrameNameForServer`
- **로컬 전용 저장**(advanced_user 개인) → 서버를 거치지 않으므로 거부 사유가 없고, 공용 파일명 규약 충돌만 **비차단 경고** = `validateFrameName` + `underscoreWarning`

문서 정정은 필요하지 않다.

---

## 8. 다음 개발 단계

**Step 6(카메라 파이프라인 + 카메라 테스트 모달)** 이 다음이며 **A1~A5를 기다리지 않는다.**

크리티컬 패스: `6 → 7 → 8 → 10 → 11`(★ 마일스톤 A = 게스트 촬영 완주). 이 경로는 **A4(CORS)·A5(배포)만** 필요하고 로그인 관련 A1~A3은 Step 12에서 필요해진다.

| 단계 | 선행 사용자 액션 |
|------|------------------|
| Step 6·7·8·9·10 | **없음** |
| Step 11(업로드·QR) | A4(CORS) · A5(배포, 실기기 검증용) |
| Step 12(로그인) 이후 | A1 · A2 · A3 |

---

## 9. 롤백

| 대상 | 방법 |
|------|------|
| 이 브랜치 전체 | `git checkout main` — `main`은 건드리지 않았다 |
| kiosk 사이트 | `npx firebase hosting:sites:delete mcphoto-955fb-kiosk` + `firebase.json`의 kiosk 블록·`.firebaserc` 타깃 제거 |
| 서버 OAuth 확장 | 해당 커밋 revert + `deploy-web.bat functions`. `defineSecret` 선언을 되돌리면 시크릿 등록도 불필요해진다 |
| 웹 게이트 키 | `CLIENT_API_KEYS`에서 그 키만 제거 + 재배포 |
| 버킷 CORS | 이전 구성으로 재적용(원래 **미설정** 상태였다) |
