# 14 · 인수인계와 사용자 액션 (Handoff & User Actions)

| 항목 | 값 |
|------|-----|
| 문서 | **코드로는 끝낼 수 없는 작업**(콘솔·시크릿·배포)의 실행 절차와, 그 시점까지 완료된 구현 상태 |
| 대상 독자 | 저장소 소유자(콘솔·시크릿 권한 보유자) |
| 작성일 | 2026-07-31 작성 · **2026-08-01 갱신(Step 12 인증 완료)** |
| 브랜치 | `feature/web-client-foundation` |
| 상태 | **WBS Step 0~12 코드 완료(★마일스톤 A + 로그인) + 사용자 액션 A1~A5 전부 완료·검증(2026-07-31).** 남은 것은 Step 13~17 개발과 §10의 실측뿐이다 |

> **왜 이 문서가 따로 있는가**: 코드로 끝낼 수 없는 작업(콘솔 UI·시크릿·공개 배포)의 절차를 남긴다.
> **A1~A5는 2026-07-31에 모두 완료됐다.** 아래 절차는 재구축·다른 프로젝트 이관·문제 발생 시 참조용으로 유지한다.
> 지금 남은 사용자 작업은 **§10의 실측뿐**이다.

---

## 1. 30초 요약

| # | 사용자 액션 | 없으면 막히는 것 | 상태 |
|---|-------------|------------------|------|
| ~~**A1**~~ | Google Cloud Console에 **Web application** OAuth 클라이언트 생성 | — | **✅ 완료** |
| ~~**A2**~~ | 웹 OAuth **시크릿·env 등록** + functions 재배포 | — | **✅ 완료** |
| ~~**A3**~~ | 웹 전용 **배포 게이트 키** 발급·등록 | — | **✅ 완료** |
| ~~**A4**~~ | Storage 버킷 **CORS**(업로드 PUT) | — | **✅ 2026-07-31 완료·검증됨**(§5.3) |
| ~~**A5**~~ | kiosk 사이트 **첫 배포** + P1 무변경 확인 | — | **✅ 2026-07-31 완료**(CSP·nosniff·캐시 헤더 실측) |

- 재수행이 필요하면 A1~A3은 순서대로(A1의 산출물이 A2의 입력), A4·A5는 독립이다.
- **Step 14(프레임 저장소·프레임 선택)까지 코드가 완성됐다**(2026-08-01). 게스트 완주 경로(마일스톤 A) + Google SSO 로그인 + 설정 화면 + 프레임 카탈로그·대기 4국면·삭제가 구현돼 있고, 남은 실측은 §10.1~§10.6·**§10.8**·**§10.9**다.
  - ⚠️ 로컬 개발 로그인은 **`http://localhost:5173`** 에서만 성공한다(A1 등록 URI·서버 허용 목록과 완전 일치해야 한다). `vite.config.ts`가 `strictPort: true`로 포트를 고정한다.

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
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | env (`functions/.env.mcphoto-955fb`) | A1의 client_id |
| `GOOGLE_OAUTH_CLIENT_SECRET_WEB` | **secret** | A1의 client_secret |
| `OAUTH_REDIRECT_ALLOWLIST` | env (같은 파일) | A1의 redirect URI 3개를 **CSV로** |

### 3.2 ⚠️ 배포 순서 주의 (먼저 읽을 것)

이 브랜치는 `src/index.ts`에 **`defineSecret("GOOGLE_OAUTH_CLIENT_SECRET_WEB")`을 추가했다.** 선언된 시크릿은 **배포 시점에 반드시 존재해야 한다** — 등록하지 않고 `firebase deploy --only functions`를 실행하면 **배포가 실패한다**.

이것은 기존 `GOOGLE_OAUTH_CLIENT_SECRET`과 **동일한 패턴**이다(그때도 SSO 미사용 상태에서 placeholder를 등록해 뒀다). SSO를 아직 안 쓸 계획이면 **placeholder라도 등록**해 두면 된다 — `GOOGLE_OAUTH_CLIENT_ID_WEB`(env)이 비어 있으면 웹 종류는 그냥 비활성이고(요청 시 501), **Windows(desktop) 경로는 영향받지 않는다**.

### 3.3 절차

> ⚠️ **`web/` 안에서 실행한다.** 프로젝트를 지정하는 `.firebaserc`가 거기에만 있어서,
> 다른 폴더(예: `webclient/`)에서 실행하면 *"No currently active project"* 로 실패한다.
> 아래 명령은 `--project`를 명시해 두었으므로 위치와 무관하게 동작한다(`firebase use --add`는 불필요).

```bash
cd web

# 1) 시크릿 등록(프롬프트에 client_secret 입력). 당장 SSO를 안 쓸 거면 placeholder라도 넣는다.
npx firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET_WEB --project mcphoto-955fb

# 2) env 등록 — functions/.env.mcphoto-955fb 에 **두 줄 추가**
#    (이 저장소는 env가 둘로 나뉜다: .env = 공통, .env.<프로젝트id> = 프로젝트별.
#     기존 GOOGLE_OAUTH_CLIENT_ID(Desktop)가 프로젝트별 파일에 있으므로 웹 값도 같은 파일에 넣는다.
#     Firebase가 두 파일을 모두 읽어 배포한다. gitignore 대상이라 커밋되지 않는다.)
#
#    OAUTH_REDIRECT_ALLOWLIST 는 A1에 등록한 URI 3개와 **문자 하나까지 같아야** 한다(콤마 구분·공백 없음).
#    기존 GOOGLE_OAUTH_CLIENT_ID 는 지우지 않는다 — Windows(desktop) 로그인이 그 값을 쓴다.
#    (PowerShell 명령은 아래 참조)

# 3) 재배포(시크릿·env 변경은 재배포가 필요하다)
deploy-web.bat functions
```

**2)를 PowerShell로** — 편집기로 열어 두 줄을 붙여 넣어도 결과는 같다.

```powershell
cd E:\Study\photobooth\web\functions
Add-Content .env.mcphoto-955fb "GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>"
Add-Content .env.mcphoto-955fb "OAUTH_REDIRECT_ALLOWLIST=https://mcphoto-955fb-kiosk.web.app/oauth2callback,https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback,http://localhost:5173/oauth2callback"

# 확인(값은 보지 않고 키만)
Select-String -Path .env.mcphoto-955fb -Pattern '^[A-Z_]+' | ForEach-Object { ($_ -split '=')[0] }
```

| 값 | 무엇인가 |
|-----|----------|
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | A1에서 만든 **Web application** 클라이언트의 client_id(`….apps.googleusercontent.com`). 비밀이 아니다 |
| `OAUTH_REDIRECT_ALLOWLIST` | 위 문자열 그대로. 서버가 이 목록과 **완전 일치**하는 `redirectUri`만 통과시킨다([analysis/31 §4.2](../analysis/31-backend-api-reference.md)) |

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

### 4.2 기존 키를 어디서 얻는가 (먼저 읽을 것)

`CLIENT_API_KEYS`는 **하나가 아니라 CSV 목록**이다. 웹 키를 "추가"하는 것이므로 **기존 값을 먼저 읽어야** 한다.
값이 있는 곳은 두 군데인데 **진실원은 Secret Manager**다.

| 출처 | 무엇 | 신뢰도 |
|------|------|--------|
| **Secret Manager** (`CLIENT_API_KEYS`) | **배포된 서버가 실제로 쓰는 전체 목록** | ★ 진실원 |
| `backend-apikey.local` (저장소 루트, gitignore) | Windows exe에 빌드 시 심는 **키 1개** | 참고용(교차 확인) |

```powershell
# 현재 등록된 전체 값을 출력한다 — 이 명령이 "기존 키"의 답이다
cd E:\Study\photobooth\web
npx firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb
```

- 출력이 한 줄이면 키가 1개(Windows용), 콤마가 있으면 이미 여러 개다.
- `backend-apikey.local`의 값이 **그 목록 안에 있어야** 정상이다(없다면 exe와 서버가 어긋난 상태 — 그것부터 확인).
- 출력된 값은 반비밀이다. 화면 공유·이슈·채팅에 붙여 넣지 않는다.

### 4.3 절차 (PowerShell, 복사해서 그대로)

> 안전장치: `secrets:set`은 **새 버전을 만들 뿐**이고, 배포된 함수는 재배포 전까지 **기존 버전을 계속 쓴다.**
> 즉 3단계(재배포) 전에는 실서비스에 영향이 없다 — **2단계에서 값을 눈으로 확인한 뒤** 배포한다.

```powershell
cd E:\Study\photobooth\web

# 1) 현재 값 읽기 + 새 웹 키 생성 + 합치기
$existing = (npx firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb | Out-String).Trim()
$webKey   = (node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))").Trim()
$combined = "$existing,$webKey"

# 2) ★ 배포 전 확인 — 키 개수가 (기존 + 1)인지, 줄바꿈·공백이 섞이지 않았는지
"키 개수: " + ($combined -split ',').Count
"길이: " + $combined.Length
$combined -split ',' | ForEach-Object { "  - " + $_.Substring(0, 6) + "…(" + $_.Length + "자)" }

# 3) 확인됐으면 등록(긴 문자열 오타를 막으려 임시 파일로 넘긴다)
$tmp = Join-Path $env:TEMP "mcphoto-client-keys.txt"
Set-Content -Path $tmp -Value $combined -NoNewline -Encoding ascii
npx firebase functions:secrets:set CLIENT_API_KEYS --project mcphoto-955fb --data-file $tmp
Remove-Item $tmp

# 4) 재배포(이때부터 새 키가 유효해진다)
.\deploy-web.bat functions

# 5) 새 웹 키를 확인해 둔다 — A5의 .env.production.local 에 넣을 값이다
$webKey
```

| 단계에서 확인할 것 | 정상 |
|---|---|
| 2)의 "키 개수" | **기존 개수 + 1**. 1이 나오면 `$existing`이 비었다는 뜻이니 **중단**하고 4.2부터 다시 |
| 2)의 각 키 길이 | 모두 40자 이상. 한 자리 수가 있으면 줄바꿈이 섞인 것이다 |
| 3) 출력 | `Created a new secret version …` |

> ⚠️ **기존 키를 지우면 배포된 Windows 앱이 즉시 401로 죽는다.** 위 절차는 기존 값을 읽어 뒤에 덧붙이는 형태라 안전하다. 수동으로 입력할 때는 **기존 값 + 콤마 + 새 키** 전체를 넣어야 한다(새 키만 넣으면 기존 키가 사라진다).

### 4.4 잘못 등록했다면

재배포 전이면 **아무 일도 일어나지 않았다** — 올바른 값으로 3)을 다시 실행하면 된다(새 버전이 또 생기고 최신 것이 쓰인다).
이미 재배포해 Windows 앱이 401이 된다면, 4.2로 이전 값을 확인해 올바른 CSV로 다시 등록하고 재배포한다.
Secret Manager는 버전을 보관하므로 이전 값도 조회할 수 있다:

```powershell
npx firebase functions:secrets:get CLIENT_API_KEYS --project mcphoto-955fb          # 버전 목록
npx firebase functions:secrets:access CLIENT_API_KEYS@1 --project mcphoto-955fb     # 1번 버전 값
```

### 4.5 검증

재배포가 끝난 뒤 실행한다(배포 전에는 이전 키 목록이 살아 있어 의미가 없다).

```powershell
$url = "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/frames/default"

# 새 웹 키 → 200
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = $webKey } -SkipHttpErrorCheck).StatusCode

# 아무 문자열 → 401 (게이트가 실제로 동작하는지)
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = "bogus" } -SkipHttpErrorCheck).StatusCode

# 기존 Windows 키도 여전히 200인지 (회귀 확인 — 가장 중요하다)
$winKey = (Get-Content ..\backend-apikey.local -Raw).Trim()
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = $winKey } -SkipHttpErrorCheck).StatusCode
```

기대: **200 · 401 · 200**. 세 번째가 401이면 기존 키가 목록에서 빠진 것이니 4.4로 되돌린다.

> `$webKey` 변수가 사라졌다면 4.2 명령으로 목록을 다시 읽어 **마지막 키**를 쓴다.
> `-SkipHttpErrorCheck`는 PowerShell 7 이상에서 쓸 수 있다(없으면 401에서 예외가 난다).

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

**✅ 2026-07-31 적용·검증 완료.** preflight를 직접 쏘면 `gcloud` 없이도 확인할 수 있다(어느 PC에서든).

```powershell
# 허용 오리진 → Access-Control-* 헤더가 돌아온다
curl.exe -s -o NUL -D - -X OPTIONS `
  -H "Origin: https://mcphoto-955fb-kiosk.web.app" `
  -H "Access-Control-Request-Method: PUT" `
  -H "Access-Control-Request-Headers: content-type,x-goog-meta-firebasestoragedownloadtokens" `
  "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/cors-probe-nonexistent" |
  Select-String -Pattern "HTTP/|access-control"

# 허용 목록 밖 → Access-Control 헤더가 **없어야** 한다(차단)
curl.exe -s -o NUL -D - -X OPTIONS `
  -H "Origin: https://evil.example.com" -H "Access-Control-Request-Method: PUT" `
  "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/cors-probe-nonexistent" |
  Select-String -Pattern "HTTP/|access-control"
```

실측 결과(허용 오리진):

```
Access-Control-Allow-Origin: https://mcphoto-955fb-kiosk.web.app
Access-Control-Allow-Methods: GET,PUT,HEAD
Access-Control-Allow-Headers: Content-Type,x-goog-meta-firebaseStorageDownloadTokens,x-goog-resumable
```

> 객체가 없어도(`cors-probe-nonexistent`) preflight는 **버킷 수준**에서 처리되므로 인증 없이 확인된다.
> M14가 요구하는 `x-goog-meta-firebaseStorageDownloadTokens`가 `Allow-Headers`에 있어야 서명 PUT이 통과한다.

Step 11 구현 후에는 브라우저 Network 탭에서 실제 `OPTIONS 204 → PUT 200`을 최종 확인한다.

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

### 6.2 절차 (PowerShell)

```powershell
cd E:\Study\photobooth\webclient

# 1) 빌드 주입값 — webclient/.env.production.local (gitignore 대상, 커밋되지 않는다)
Set-Content .env.production.local -Encoding ascii -Value @(
  "VITE_BACKEND_API_KEY=<A3의 웹 키>",
  "VITE_GOOGLE_CLIENT_ID=<A1의 client_id>",
  "VITE_APP_VERSION=0.1.0"
)

# 값이 아니라 키 이름만 확인
Get-Content .env.production.local | ForEach-Object { ($_ -split '=')[0] }

# 2) 빌드 + 배포
.\deploy.bat
```

메모장으로 `webclient\.env.production.local`을 만들어 세 줄을 넣어도 결과는 같다.

| 값 | 어디서 | 비밀인가 |
|-----|--------|:--------:|
| `VITE_BACKEND_API_KEY` | A3에서 만든 **웹** 게이트 키(`$webKey`) | **아니다** — JS 번들에 그대로 들어가 브라우저에서 보인다(WD10). Windows 키를 넣지 않도록 주의 |
| `VITE_GOOGLE_CLIENT_ID` | A1의 client_id(`….apps.googleusercontent.com`) | 아니다 |
| `VITE_APP_VERSION` | 하단 캡션에 `v0.1.0`으로 표시된다 | — |

> ⚠️ **`VITE_*` 값은 전부 브라우저에 공개된다.** client_secret·JWT_SECRET·Windows 게이트 키를 여기에 넣으면 안 된다.
> 비밀은 서버(Secret Manager)에만 둔다.

### 6.3 ⚠️ 기존 P1 배포 스크립트가 바뀌었다

Hosting이 멀티사이트가 되면서 `firebase deploy --only hosting`은 **두 사이트를 동시에** 배포한다. `web/kiosk/`는 gitignore된 빌드 산출물이므로, 클론 직후·CI에서는 **배포가 실패하거나 낡은 빌드가 공개된다**. 그래서 `deploy-web.bat`의 배포 대상을 **`hosting:default`로 고정**했다(근거: [analysis/80 §6.5](../analysis/80-build-and-deployment.md)).

**한 번 실행해 P1 페이지가 그대로인지 확인해 주세요**:

```bash
cd web && deploy-web.bat hosting
```

스크립트가 배포 후 `public/`과 실서버 바이트를 대조한다(기존 검증 로직 그대로).

### 6.4 검증

```powershell
# curl.exe 를 쓴다 — PowerShell의 `curl` 은 Invoke-WebRequest 별칭이라 -sI 가 통하지 않고,
# grep 도 없다(Select-String 을 쓴다).
curl.exe -sI https://mcphoto-955fb-kiosk.web.app/ |
  Select-String -Pattern "HTTP/|content-security-policy|x-content-type-options|cache-control"

# P1 사이트 무변경
curl.exe -sI https://mcphoto-955fb.web.app/ | Select-Object -First 1
```

| 경로 | 기대 `Cache-Control` | 왜 |
|------|----------------------|-----|
| `/` · `/oauth2callback` · `/index.html` | **`no-cache, max-age=0`** | 재배포가 즉시 반영돼야 한다 |
| `/assets/**` | `public, max-age=31536000, immutable` | 파일명에 해시가 있어 영구 캐시가 안전하다 |

> ⚠️ **`/`가 `max-age=3600`으로 나오면 안 된다.** Hosting은 헤더를 **rewrite 이전의 요청 경로**로 매칭하므로
> `/index.html` 규칙만으로는 손님이 실제로 여는 `/`가 덮이지 않는다(2026-07-31 실측으로 발견해 `firebase.json`에
> `/`·`/oauth2callback` 규칙을 추가했다). 그대로 두면 재배포 후 최대 1시간 동안 옛 HTML이 서빙된다.

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
| **6** 카메라 | Worker에서 **프레임당 1회** 거울+중앙 크롭 → 프리뷰·스틸·타임랩스가 같은 결과 공유(WM1), Ready 게이트(가공 8프레임+500ms+fps>0, **8초 타임아웃**), `deviceId`→label→groupId→첫 장치 폴백, 카메라 테스트 모달 | 35 테스트 + **WM1 정적 검사** |
| **7** 촬영 | 컷 루프 a~f 순서(플래시 off는 **캡처 후**), **실경과 카운트다운**(WM3), [바로 촬영] 매 컷, 취소 시 부분 결과 없음(WM4), **컷 수 N 하드코딩 없음**(7·9 수용), 화면 5종(Home·FrameSelect 최소판·Guide·Capture·CutSelect) | 26 테스트 |
| **8** 합성 | 순수 RGBA 합성 코어(브라우저·테스트 동일 경로), BT.601 흑백·밝게·뷰티, INTER_AREA 축소, **골든 이미지 체계**(Windows 생성 → 웹 대조) | 4필터 허용 오차 통과 + 슬롯 0px |
| **서버** B1·B2·B4 | `redirectUri` 허용목록(완전 일치), audience 목록 + `clientKind`, 웹 게이트 키 배선 | 서버 316 테스트 |

### 7.1 전체 테스트 현황

```bash
cd webclient     && npx tsc --noEmit && npx vitest run     # 492 통과 (도메인 커버리지 100%)
cd web/functions && npm test                                # 316 통과
dotnet test tests/MCPhoto.Tests                             # 839 통과 (기존 823 무회귀 + 벡터 16)
```

### 7.2 구현 중 발견해 고친 결함 4건

| # | 무엇 | 왜 위험했나 |
|---|------|-------------|
| 0 | `requestVideoFrameCallback`을 TS 타입대로 필수로 다룸 | DOM lib은 필수라고 선언하지만 **Safari 15.4 미만에는 없다** — 타입을 믿고 분기를 빼면 그 기기에서 프레임 루프가 시작되지 않는다 → 옵셔널 타입으로 감싸 런타임 감지 유지 |
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

**Step 9(타임랩스 인코더)** 가 다음이며 **A1~A5를 기다리지 않는다.**

크리티컬 패스: `10 → 11`(★ 마일스톤 A = 게스트 촬영 완주). 이 경로는 **A4(CORS)·A5(배포)만** 필요하고 로그인 관련 A1~A3은 Step 12에서 필요해진다.

| 단계 | 선행 사용자 액션 |
|------|------------------|
| Step 9·10 | **없음** |
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

---

## 10. 실기기·브라우저 실측 (코드로 대체할 수 없는 검증)

단위 테스트는 **판정 규칙**을 고정하지만 **하드웨어 동작**은 고정하지 못한다. 아래는 사람이 눈으로 봐야 하는 항목이며, 자동화가 불가능한 것만 남겼다.

### 10.1 Step 6·7(카메라·촬영) 실측 — 지금 가능

`npm run dev`(localhost는 보안 컨텍스트라 카메라가 열린다) 또는 A5 배포 후 실기기에서, 더미 화면의 **[카메라 테스트 열기]** 로 확인한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V1 | 프리뷰가 뜨고 부드럽다 | 24fps 이상. 하단 캡션에 실제 해상도·실측 fps 표시 | 실제 카메라 프레임이 필요하다 |
| V2 | **거울모드 on/off가 프리뷰에 즉시 반영** | 설정을 바꾸면 재시작 없이 좌우가 바뀐다 | 픽셀 결과 확인이 필요하다 |
| V3 | **모달을 닫으면 카메라 LED가 꺼진다** | 즉시 소등 | 하드웨어 표시등 |
| V4 | 카메라를 막은 환경(권한 거부·장치 없음)에서 **8초 내 실패 문구** | 무한 로딩이 아니다 | 권한 거부 조작 |
| V5 | 장치가 2개 이상이면 목록이 뜨고 전환된다 | 전환 시 정지 후 재시작된다 | 다중 카메라 하드웨어 |
| V6 | 셔터를 누르면 플래시가 번쩍이고 **"저장되지 않았습니다"** | 결과물이 남지 않는다 | 시각 확인 |
| V7 | iOS/iPadOS Safari에서 전체화면으로 전환되지 않는다 | 인라인 재생 유지(`playsinline`) | iOS 실기기 |
| **V14** | **6컷 세션 완주** — 홈 → 프레임 선택 → 가이드 → 촬영 | 카운트다운·플래시·300ms 간격을 지키며 6장 | 실제 프레임·타이밍 |
| **V15** | DevTools → OPFS에 `sessions/{id}/cut1..6.jpg` 생성 | 6개 파일 | 저장소 관측 |
| **V16** | **촬영 중 탭 전환** → 홈 복귀 + **부분 컷 미잔존** | OPFS 세션 폴더가 사라진다(WM4) | 탭 전환 조작 |
| **V17** | [바로 촬영]을 **매 컷** 눌러 세션을 짧게 완주 | 남은 카운트다운을 건너뛰고 즉시 촬영 | 연속 입력 |

> V2가 실패하면 **WM1 파손**이다(프리뷰만 반전되고 저장은 원본). 정적 검사가 `scaleX(-1)`을 막고 있으므로 발생 가능성은 낮지만, 이 항목이 실기기에서 확인해야 하는 이유는 "**저장 결과와 프리뷰가 같은가**"가 계약이기 때문이다 — Step 7·8이 완성되면 저장본과 대조한다.

### 10.2 Step 3·4 실측 — 지금 가능

| # | 확인 | 방법 |
|---|------|------|
| V8 | OPFS 잔재 정리 | DevTools → Application → OPFS에 더미 `sessions/x/` 폴더를 만들고 새로고침 → **사라진다**. `results/`·`frames/`는 남는다 |
| V9 | 설정 영속 | localStorage에 `mcphoto.settings.v1` 존재. 값을 손상시켜도 기본값으로 뜬다 |
| V10 | 13화면 전이 | 더미 화면 버튼으로 순회. 불법 전이는 버튼이 없다 |
| V11 | 유휴 경고 | 촬영 흐름 화면에서 2분 무동작 → 경고 → 10초 → 홈. **로그인이 유지된다** |
| V12 | 전체화면 배너 | ESC로 이탈 → 상단 배너 → [다시 전체화면으로] |
| V13 | 콘솔 CSP 위반 0건 | 배포본(A5)에서 확인. 로컬 dev 서버는 CSP가 적용되지 않는다 |

### 10.3 V18 · Step 9(타임랩스) 실기기 실측 — 지금 가능

Step 9 구현이 끝났다(2026-07-31). **단위 테스트는 판정 규칙만 고정한다** — 브라우저 인코더가
실제로 재생 가능한 mp4를 만드는지는 코드로 증명할 수 없다. 아래 7건은 **사람이 눈으로 봐야 한다**.

`npm run dev`(localhost는 보안 컨텍스트라 카메라가 열린다) 또는 A5 배포본에서 **6컷 세션을 완주**한 뒤
`Result`에서 [다음]을 누른다. 진단 로그는 DevTools → Application → IndexedDB(`mcphoto-logs`)에서 본다
(운영 기기에서는 Step 16 진단 모달의 [로그 내보내기]).

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V18-1 | 생성된 mp4가 **재생되고 컨테이너가 정상 종료**됐다 | `<video>`로 끝까지 재생. `ffprobe`가 `moov`를 읽고 duration을 보고한다 | 실제 H.264 인코더 출력이 필요하다(가짜 인코더로는 컨테이너가 안 나온다) |
| V18-2 | 코덱이 `h264`, **오디오 트랙 0개**, 길이 10~15초(6컷 ~38초 세션) | `ffprobe -show_streams`에 video 1개뿐 | 동상 |
| V18-3 | **[바로 촬영]을 매 컷 눌러 ~5초로 줄인 세션**도 원속 ~5초 mp4가 나온다(`null`이 아니다) | 로그 `타임랩스 생성`의 `speedFactor: 1`, `durationSec ≈ 5` | 연속 입력 조작 |
| V18-4 | **모바일(iOS Safari 16.4+)·데스크톱 양쪽에서 재생**된다 | 두 기기 모두 재생 | 실기기 |
| V18-5 | 인코딩 소요 **≤6초**(A4), 촬영 중 프리뷰 **≥24fps 유지**(A5), `droppedSpool` 수치(A7) | 로그 `타임랩스 생성`의 `elapsedMs ≤ 6000`, `타임랩스 수집 종료`의 `droppedSpool`이 `spooled`에 비해 작다 | 하드웨어 성능 계측 |
| V18-6 | 인코더 미지원 브라우저(Firefox 등)에서 **촬영이 완주하고 타임랩스만 없다** | 로그 `타임랩스 미제공(브라우저 H.264 인코더 없음)` + 화면은 `Qr`/`Done`으로 정상 전이 | 브라우저별 `isConfigSupported` 결과 |
| V18-7 | 경로 A(`MediaRecorder`) 실제 동작(A6) | 로그 `타임랩스 인코더 경로 판정`의 `path` 확인. **지원 매트릭스상 도달하지 않으면 "미도달"로 기록해도 합격**이다([04 §7.3] 이유 ③) | 경로 A가 선택되는 브라우저를 인위적으로 만들 수 없다 |

> **진단에 남는 항목**(05 §7.2): `타임랩스 인코더 경로 판정`(`path`·`codecName`·`reason`) ·
> `타임랩스 수집 시작/종료`(`intervalMs`·`spooled`·`droppedSpool`·`decimations`·`elapsedSec`) ·
> `타임랩스 생성`(`spooled`·`selected`·`encodedFrames`·`droppedFrames`·`skippedFrames`·
> `speedFactor`·`durationSec`·`bytes`·`elapsedMs`) · 실패 시 `타임랩스 생성 실패` + `reason`.
>
> ⚠️ **`ffprobe`는 개발 PC에서 돌린다.** 브라우저에서 생성한 mp4를 내려받는 경로는 Step 10·11이
> 붙기 전까지 없으므로, DevTools 콘솔에서 `Blob`을 직접 저장하거나 Step 10 완료 후 확인한다.

### 10.4 V19 · Step 10(결과물 로컬 보관) 실기기 실측 — 지금 가능

Step 10 구현이 끝났다(2026-07-31). 단위 테스트는 **순서·판정·경로 문자열**만 고정한다 —
브라우저가 실제로 파일을 남기는지, `usage` walk가 [다음] 체감을 해치지 않는지는 코드로 증명할 수 없다.

`npm run dev` 또는 A5 배포본에서 세션을 완주하고 `Result`에서 [다음]을 누른 뒤 확인한다.
OPFS는 DevTools → Application → Storage → **Origin Private File System**에서 본다(Chromium).

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V19-1 | **네트워크를 끊고**(DevTools → Network → Offline) 촬영을 완주하면 OPFS `results/mcphoto_YYMMDD_HHMM/final.jpg`가 **존재**한다(E8·A5) | 파일이 보이고 크기가 0이 아니다. 타임랩스가 있으면 `timelapse.mp4`도 같은 폴더에 있다 | 실제 OPFS 쓰기가 필요하다 |
| V19-2 | 로그 `결과물 로컬 보관`의 `status`가 `saved`이고 `elapsedMs`가 **[다음] 체감을 해치지 않는다**(A1 — `usage` 재귀 walk 포함) | `elapsedMs`가 수백 ms 이내. **300ms를 넘으면** 보존 정리(`enforceRetention`)를 `void`로 돌리고 `evicted`를 `-1`로 보고하도록 후속 조치한다([설계 §5.3]) | 하드웨어·저장소 성능 계측 |
| V19-3 | 데스크톱 Chromium에서 `Settings` 화면의 **[로컬 저장 폴더 선택]** 으로 폴더를 지정하면, 다음 촬영부터 **그 폴더에도 같은 파일이 생긴다** | 탐색기에서 `mcphoto_YYMMDD_HHMM/final.jpg` 확인. 로그 `folderCopy: "copied"` | `showDirectoryPicker`는 사용자 제스처가 필요하다 |
| V19-4 | 폴더 권한을 잃은 뒤(브라우저 재시작 등) 촬영해도 **흐름이 멈추지 않는다** | 로그 `folderCopy: "permission-required"`, 손님 화면에 토스트 **없음**, `Qr`/`Done`으로 정상 전이 | 권한 상실 상태를 인위적으로 만들 수 없다 |
| V19-5 | 폴더 저장 **미지원 브라우저**(Safari·Firefox·모바일)에서 [로컬 저장 폴더 선택] 버튼이 **렌더되지 않는다** | 버튼 부재 + 로그 `folderCopy: "unsupported"` | 브라우저별 기능 감지 결과 |
| V19-6 | OPFS 쓰기 실패를 유발하면(저장소 할당량 소진 등) **실패 토스트**가 뜨고 그래도 전이한다 | "저장 위치에 쓸 수 없습니다." + 화면은 계속 진행 | 할당량 소진 상황 재현 |

> **진단에 남는 항목**: `결과물 로컬 보관`(`status`·`folderName`·`finalSaved`·`timelapseSaved`·
> `hadTimelapse`·`folderCopy`·`folderCopyName`·`evicted`·`bytes`·`elapsedMs`) ·
> `결과물 로컬 보관 건너뜀`(`reason`) · `보관 결과물 정리`(`removed`·`keptCount`·`keptBytes`·`triggers`).
>
> ⚠️ **V18-1·V18-2(`ffprobe`)를 여기서 함께 처리할 수 있다.** V19-3으로 폴더를 지정해 두면
> 타임랩스 mp4가 실제 파일로 떨어져 개발 PC에서 바로 `ffprobe`를 돌릴 수 있다.

### 10.5 V20 · Step 11(업로드 3단계 · QR · 완료) 실기기 실측 — 지금 가능

Step 11 구현이 끝났다(2026-07-31). 단위 테스트는 **호출 순서·헤더 부착·판정·문구**만 고정한다 —
**브라우저가 실제로 서명 URL에 PUT을 성공시키는지**(OA-1)와 **폰이 QR을 읽는지**는 코드로 증명할 수 없다.

`npm run dev` 또는 A5 배포본에서 확인한다. ⚠️ **게스트는 `Qr`에 도달하지 않는다**(VF-11) —
V20-1~V20-3은 **실제로 Google 로그인한 뒤**(Step 12 완료로 지금 가능하다) `Qr`까지 가면 된다.
목으로 대체하려면 effective QR을 `true`로 고정하거나 세션 사용자를 주입한다.
**제품 코드에 게이트 우회 플래그를 남기지 않는다**(정적 grep이 0건을 고정한다).

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V20-1 | **서명 PUT이 브라우저에서 성공한다**(OA-1) | DevTools Network에 `OPTIONS 204` → `PUT 200`(또는 `204`). 로그 `서명 PUT 완료`의 `status`·`bytes`·`headerNames` | 실제 GCS 서명·버킷 CORS 왕복이 필요하다 | A4(CORS — 구성 완료) |
| V20-2 | **`requiredHeaders`가 전량 붙는다**(M14) | 요청 헤더에 `Content-Type`과 `x-goog-meta-firebaseStorageDownloadTokens`가 **둘 다** 있고, 인증 헤더는 **없다**. 로그 `headerNames`가 prepare 응답 키와 같다 | 실제 prepare 응답이 필요하다 | V20-1 |
| V20-3 | **진행률이 실제로 온다**(A2) | `lengthComputable: true`로 진행률이 0→100 증가한다(타임랩스처럼 큰 파일에서 확실히 보인다). 0에서 100으로 **점프하지 않는다** | 브라우저·네트워크 속도에 의존한다 | V20-1 |
| V20-4 | **폰으로 QR을 스캔해 다운로드 페이지가 열린다**(A4-가정) | 기본 카메라 앱이 인식 → P1 페이지 → 사진·영상 다운로드. **kiosk 도메인이 아니라 P1 도메인**이 열린다 | 물리 폰과 카메라가 필요하다 | ~~Step 12~~ **해소(2026-08-01)** → V21-5 |
| V20-5 | **[기기에 저장]이 실제 파일을 남긴다** | 다운로드 폴더에 `MCPhoto_{yyyyMMdd}_{HHmmss}.jpg` / `…_timelapse.mp4`. **파일명에 UUID가 없다.** 파일이 2개면 버튼도 2개 | 브라우저 다운로드 동작·사용자 제스처가 필요하다 | 없음(지금 가능) |

**실패 경로도 함께 본다**(코드로는 목으로만 검증했다):

| # | 확인 | 기대 |
|---|------|------|
| V20-6 | 네트워크를 끊고 `Qr`에 들어가면 | QR이 **뜨지 않고** "전송 실패 — 사진은 기기에 저장되었습니다."(로컬 저장 on) + [재시도]·[완료]가 보인다. 로그 `서명 PUT 실패`의 `hint`에 CORS 안내 |
| V20-7 | [재시도]를 누르면 | 진행률이 0에서 재시작하고 **새 세션 ID**로 prepare부터 다시 간다(로그 `업로드 대상 확정`의 `sameAsCaptureSession: false`). 409가 나지 않는다 |
| V20-8 | `Done`에서 6초 기다리면 | 자동으로 홈에 간다. **로그아웃되지 않는다**(상단 계정 라벨 유지 — M3). 탭을 숨겼다 6초 뒤 돌아오면 **즉시** 홈이다 |

> **진단에 남는 항목**: `업로드 대상 확정`(`uploadPhoto`·`uploadTimelapse`·`attempt`·`sameAsCaptureSession`) ·
> `업로드 prepare`(`kind`·`attempt`·`bucket`) · `서명 PUT 완료`(`kind`·`bytes`·`status`·`elapsedMs`·`headerNames`) ·
> `서명 PUT 실패`(`kind`·`failure`·`status`·`elapsedMs`·`headerNames`·`hint`) · `업로드 commit 완료`(`hasFinal`·`hasTimelapse`·`retentionHours`·`elapsedMs`) ·
> `업로드 실패`(`reason`·`attempt`·`elapsedMs`) · `QR 렌더`(`moduleCount`·`modulePx`·`canvasPx`).
>
> ⚠️ **서명 URL·다운로드 URL·세션 ID 원문은 로그에 없다**(전부 capability다 — analysis/41 §8).
> 그래서 진단만으로는 "어느 파일이 어디로 갔는지"를 알 수 없다 — 그 확인은 DevTools Network로 한다.

### 10.6 V21 · Step 12(Google SSO 리디렉트 · JWT) 실측 — 지금 가능

Step 12 구현이 끝났다(2026-08-01). 단위 테스트는 **URL 조립·판정 순서·오류 매핑·저장소 경계**만 고정한다 —
**실제 Google 인증이 끝까지 도는지**와 **배포본 CSP가 리디렉트를 막지 않는지**는 코드로 증명할 수 없다.

`npm run dev`(**포트 5173 고정** — `strictPort: true`) 또는 A5 배포본에서 확인한다.
⚠️ **로그인은 접속한 도메인 그대로 진행한다.** `web.app`으로 시작해 `firebaseapp.com`으로 돌아오면
`sessionStorage`가 오리진별이라 콜백이 **"취소되었습니다"** 로 끝난다(로그 `abortReason: "no-pending"`).

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V21-1 | **실 Google 계정으로 로그인 완주**(OA-5) | 상단 계정 라벨이 계정 id로 바뀌고 **직전 화면으로 복귀**한다. 주소창에 `code`·`state`가 **없다**. 로그 `로그인 성공`(`userId`·`role`·`expiresInSec`) | 실 Google 인증·실계정이 필요하다 | A1~A3 |
| V21-2 | **배포본(kiosk)에서 CSP 위반 0건** + 리디렉트가 막히지 않는다 | 콘솔에 CSP 오류 없음. `accounts.google.com`으로 이동 성공 | 배포 헤더가 붙은 환경이 필요하다(로컬 dev엔 CSP가 없다) | A5 |
| V21-3 | **`prompt=select_account`가 계정 선택 화면을 띄운다** | 브라우저에 Google 세션이 남아 있어도 **매번 계정 선택**이 뜬다(직전 손님 계정으로 자동 로그인되지 않는다) | 브라우저에 Google 세션이 남은 상태를 만들어야 한다 | V21-1 |
| V21-4 | **새로고침하면 게스트로 돌아간다**(C6) + **저장소에 토큰 0건**(E4) | DevTools Application에서 토큰 문자열 검색 0건(localStorage·sessionStorage·IndexedDB·쿠키). `mcphoto.oauth.pending.v1`도 콜백 후 **없다** | 브라우저 저장소 관측이 필요하다 | V21-1 |
| V21-5 | **로그인 상태에서 QR 완주 → 폰 스캔**(V20-4 선행 해소) | P1 다운로드 페이지가 열린다 | 물리 폰이 필요하다 | V21-1 |
| V21-6 | **`firebaseapp.com` 도메인으로 접속해도 로그인이 된다** | 두 도메인 모두 성공(A1에 둘 다 등록돼 있다) | 도메인별 접속이 필요하다 | A1 |

**실패 경로도 함께 본다**:

| # | 확인 | 기대 |
|---|------|------|
| V21-7 | Google 계정 선택 화면에서 **취소**하면 | `Login` 화면에 "Google 로그인이 취소되었습니다." + **[닫기]로 게스트 복귀 가능**. 로그 `Google 로그인 중단`(`abortReason: "provider-error"`) |
| V21-8 | 콜백 URL(`/oauth2callback`)에 **직접 접속**하면 | 같은 취소 문구. 로그 `abortReason: "no-pending"`. 스피너에 고착되지 않는다 |
| V21-9 | 서버 `OAUTH_REDIRECT_ALLOWLIST`가 어긋나 있으면 | 손님에겐 "네트워크를 확인해 주세요." + 로그 `서버가 redirectUri를 거부했다(B1 미적용 가능)`(`status: 400`·`errorCode`) |
| V21-10 | `VITE_GOOGLE_CLIENT_ID`를 비우고 빌드하면 | `Login`에 **버튼이 없고** "로그인이 구성되지 않았습니다…"만. **게스트 촬영은 정상 동작**한다 |

> **진단에 남는 항목**: `Google 로그인 리디렉트`(`returnTo`) · `Google 로그인 중단`(`abortReason`) ·
> `Google 로그인 교환 실패`(`failureReason`·`elapsedMs`) · `로그인 성공`(`userId`·`role`·`expiresInSec`) ·
> `서버가 redirectUri를 거부했다(B1 미적용 가능)`(`status`·`errorCode`) · `PKCE 생성 실패`(`reason`) ·
> `세션 만료 감지(401) — 세션 해제`(`path`) · `JWT 폐기`(`reason`).
>
> ⚠️ **`code`·`state`·`nonce`·`code_verifier`·토큰·authorize URL 전체·email은 로그에 없다**(비밀·개인정보).
> 그래서 "state가 왜 어긋났는지"는 진단으로 알 수 없다 — 그 확인은 DevTools로 한다.

> **PIN 1회 오입력이 로그아웃을 유발하지 않는다(E17)** 의 화면 관측은 Step 13 완료로 **지금 가능**해졌다 →
> **V22-3**(§10.8)로 이동. 코드 쪽 보장은 `verifyMyPin`·`setMyPin` 양쪽의 `unauthorized: "reject"`(정적 PIN-2) +
> `tests/unit/auth/sessionExpiry.test.ts`의 PIN 절이 고정한다.

### 10.7 아직 불가능한 실측

| 항목 | 필요 선행 |
|------|-----------|
| 10컷 세션 메모리(iOS 탭 생존) | Step 7 |
| 프레임 내보내기/가져오기 | **Step 16**(zip 번들 — `frameStore.saveLocal`은 Step 14에서 준비됨) |
| 앱 업데이트 확인 · [진단·상태] 모달 | **Step 16**(Service Worker · 진단 모달) |

> **해소됨**: "업로드 `OPTIONS 204 → PUT 200`"은 Step 11 완료로 **V20-1**로, "폰으로 QR 스캔 → 다운로드"는
> Step 12 완료로 **V21-5**로, "PIN 1회 오입력이 로그아웃을 유발하지 않는다(E17)"는 Step 13 완료로 **V22-3**으로 이동했다.

### 10.8 V22 · Step 13(진입 PIN 게이트 · 설정 화면) 실측 — 지금 가능

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 1297 통과). 아래는 **브라우저·실계정·실기기가 있어야만** 확인되는 항목이다.
> ⚠️ **추정으로 통과 처리하지 않았다.** V22-4는 **PIN이 설정되지 않은 실계정**이 있어야 한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V22-1 | 로그인 사용자가 [설정]을 누르면 **매번** PIN 모달이 뜬다 | 통과 → 설정 렌더. [닫기]로 나갔다 다시 들어가면 **또** 묻는다 | 실계정 로그인 + 브라우저 | V21-1 |
| V22-2 | **게스트**가 [설정]을 누르면 모달 없이 즉시 진입한다 | 네트워크 탭에 `accounts/me/pin*` 요청 **0건** | 브라우저 | — |
| V22-3 | **PIN 1회 오입력이 로그아웃을 유발하지 않는다**(E17 — Step 12에서 이월) | 상단 계정 라벨 **유지** + "PIN이 일치하지 않습니다. (1/5)" + 1.5초 키 비활성 | 실계정 + 화면 | V21-1 |
| V22-4 | **PIN 미설정 계정의 최초 설정 플로우**: 새 PIN 2회 → 저장 → **재확인 없이 진입**. 화면을 나갔다 다시 들어가면 **확인(verify) 모드** | 2회차에 401 데드락이 나지 않는다(`markPinSet` 검증 — A5) | PIN 없는 실계정이 필요하다 | V21-1 |
| V22-5 | **5회 실패 → 모달 닫힘 + 5분 잠금**, **탭을 닫았다 열어도 잠금 유지**(E16) | `localStorage["mcphoto.pinLock.v1"]`에 `{until,fails}` 존재. 재진입 시 모달 없이 "…{남은 시간} 후 다시 시도해 주세요." | 브라우저 저장소 관측 | V21-1 |
| V22-6 | 네트워크를 끊고 PIN 입력 | "확인할 수 없습니다. 네트워크를 확인하세요." + **실패 카운트 미증가** + **진입 차단**. 3회 시도해도 잠기지 않는다 | 오프라인 전환이 필요하다 | V21-1 |
| V22-7 | **게스트로 설정 저장 → 로그인 후 확인**(E23) | 거울모드·재촬영·필터 3종·QR 3종·URL 2종의 **운영자 값이 그대로**다. 게스트 화면에서는 이 항목이 OFF·비활성 + "로그인 필요" | 저장소 관측 + 계정 전환 | V21-1 |
| V22-8 | 컷 수 **8** 저장 → `Guide` 반영 · 컷 수 **자동**(sentinel 0) 저장 왕복 후 소멸하지 않음 | Guide에 "(자동)" 배지. 설정 재진입 시 "자동"이 선택돼 있다 | 실촬영 흐름 | — |
| V22-9 | 카메라 장치 선택·[재검색]·[카메라 테스트] · **권한 전 라벨이 "카메라 N"** | 장치 전환이 실제 프리뷰에 반영된다. 권한 허용 후 실제 장치명이 나온다 | 실 카메라 2대 | V1 |
| V22-10 | [보관된 결과물] 목록·용량·개별/전체 삭제 · 여유 10% 미만 경고 배지 | 삭제 후 목록·총량이 줄어든다. 전체 삭제는 **인라인 2단 확인**을 거친다 | 실제 촬영 결과물이 필요하다 | V19 |
| V22-11 | [폴더 선택]/[폴더 해제] · **미지원 브라우저(Safari·Firefox)에서 버튼 미노출** | Chromium에서만 보인다. 지정 즉시 저장되고 표시값이 폴더명으로 바뀐다 | 브라우저 3종 | — |
| V22-12 | [설정 내보내기] 파일이 열리고 **`BackendApiKey`가 없다** · [가져오기] 미리보기 → [적용] | 파일명 `mcphoto-settings-{YYMMDD_HHMM}.json`. `schemaVersion`을 99로 고치면 "더 새 버전의 설정 파일입니다." | 파일 다운로드·선택 | — |
| V22-13 | 온스크린 키패드 터치 타깃(48px) · `aria-live` 안내 | 스크린리더가 실패 사유("PIN이 일치하지 않습니다.")를 읽는다 | 실기기·보조기술 | — |

> **진단에 남는 항목**: `PIN 확인 통과`·`PIN 설정 완료`(`gateMode`) · `PIN 불일치`·`PIN 시도 소진`(`gateMode`·`failCount`) ·
> `PIN을 확인할 수 없습니다`(`gateMode`·`attemptOutcome`·`errorStatus`) · `PIN 연속 실패로 기기 잠금`(`lockUntil`·`failCount`) ·
> `PIN 게이트 거부`(`gateScreen`·`denyReason`) · `PIN 승인 폐기`(`discardReason`) · `PIN 모달이 표시되지 않았습니다`(`gateMode`) ·
> `제한된 설정 항목 편집 시도`(`settingKey`·`guest`) · `보관 결과물 삭제 실패`(`folderName`) · `설정 가져오기 적용`(`changeCount`).
>
> ⚠️ **PIN 값은 어디에도 없다**(정적 PIN-1이 고정한다). "무엇을 입력했는지"는 진단으로 알 수 없다 — 그것이 규격이다.

---

### 10.9 V23 · Step 14(프레임 저장소 · 프레임 선택 · it20 대기 국면) 실측 — 지금 가능

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 **1469** 통과). 아래는 **브라우저·실기기·실계정이 있어야만** 확인되는 항목이다.
> ⚠️ **추정으로 통과 처리하지 않았다.** V23-3·V23-8은 온라인 서버 프레임과 power 계정이 있어야 한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V23-1 | DevTools에서 **OPFS·IndexedDB를 비우고** Slow 3G로 `FrameSelect` 진입 | 진입 즉시 대기 오버레이 + `(n/m)` 카운터. **"빈 목록 + 활성 [다음]"이 한 프레임도 나타나지 않는다** | 저장소 초기화 + 네트워크 스로틀 | A5 |
| V23-2 | **오프라인 전환** 후 진입 | 안내 없이 조용히 목록 표시(`Ready`). *"…가져오지 못해…"* 문구가 **없다**(E20) | 오프라인 전환 | V23-1 |
| V23-3 | 온라인에서 **서버 프레임**을 골라 촬영·합성 | 합성 성공(canvas 오염 없음 — **WM2 · OA-2 종결**). 이 항목이 200 응답의 CORS-clean 여부(A-4)를 실증한다 | 실 서버 프레임 + 완주 | V21-1 |
| V23-4 | **두 번째 진입** | Network 패널에 프레임 이미지 요청 **0건**(이름 dedup) · `blob:` URL로 합성 성공(A-1) | Network 패널 관측 | V23-3 |
| V23-5 | **Safari(iOS 17)** 에서 목록 진입 | 썸네일이 보이고 콘솔 오류 0. `createImageBitmap` resize 미실효(A-3)여도 캔버스 폴백으로 카드가 정상이다 | 실 iOS 기기 | A5 |
| V23-6 | 프레임 20개 상태에서 **목록 왕복 10회** | 메모리 증가가 누적되지 않는다(썸네일 `close()` · object URL 경로당 1개 재사용 — A-2) | 메모리 프로파일러 | V23-3 |
| V23-7 | DevTools → Application → IndexedDB | `mcphoto`·`mcphoto-handles`·**`mcphoto-frames`** 3개가 각각 존재하고 **로그가 계속 쌓인다**(A-5 — blocked 없음) | 저장소 관측 | V23-1 |
| V23-8 | **power 계정**으로 서버 프레임 삭제(“서버에서도 제거” 체크) | 결과 문구가 4종 중 정확히 하나. 목록이 **오버레이 없이** 갱신된다(조용한 재스캔). 체크를 **끄면** 재스캔에서 카드가 되돌아온다(의도된 종전 동작 — K3) | manager/admin 실계정 | V21-1 |

> **진단에 남는 항목**: `기본 프레임 서버 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)` ·
> `기본 프레임 대기 중단 — 로컬 전용 폴백`(`reason`·`noProgressSec`·`totalSec`) · `개인 프레임 로드 실패(공용 목록은 유지)` ·
> `프레임 이미지 다운로드 실패(HTTP)`(`status`) · `프레임 이미지 다운로드 실패(네트워크 또는 CORS 차단 가능)` ·
> `createImageBitmap resize 옵션 미실효 — 캔버스 축소 폴백으로 전환`(`requested`·`got`) ·
> `프레임 이미지 파일이 없어 목록에서 제외`(`key`·`imageFile`) · `프레임 이미지 삭제 실패(파일이 남아 있음)` ·
> `서버 삭제 id 불일치 → 이름 매칭 재삭제`(`name`·`id`) · `서버 프레임 삭제 실패: 문서 미발견`(`name`·`triedId`) ·
> `공용 프레임 이름에 '_'가 있어 매 실행 재다운로드됩니다`(`name`) · `번들 프레임 로드`(`count`).

---
