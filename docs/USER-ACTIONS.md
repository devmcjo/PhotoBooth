# USER ACTIONS — 내가(운영자) 직접 해야 하는 작업

| 항목 | 값 |
|------|-----|
| 목적 | 코드로 자동화할 수 없는 **콘솔·배포·외부계정·하드웨어** 작업을 한곳에 모은 실행 런북. |
| 대상 독자 | 프로젝트 소유자(DB 접근 권한 보유자) |
| 프로젝트 | Firebase `mcphoto-955fb` · 리전 `asia-northeast3` · 버킷 `mcphoto-955fb.firebasestorage.app` |
| 최종 업데이트 | 2026-07-26 |
| 관련 | 설계 `design/wpf-backend-proxy-migration-design.md` · 순차계획 `design/backlog-post-backend-migration.md` |

> 표기: `[ ]` 미완 / `[x]` 완료. **⚠️ 불가역** 표시 항목은 순서를 반드시 지킬 것.
> 명령은 별도 표기 없으면 **`web/` 디렉토리에서** 실행(그 안에 `.firebaserc`·`firebase.json` 있음).
> **순서대로 따라 하는 실행 가이드**는 [`DEPLOY-WALKTHROUGH.md`](./DEPLOY-WALKTHROUGH.md) — 이 문서는 전체 체크리스트/근거, 저 문서는 단계별 워크스루(성공 판정 포함).

---

## 헬퍼 스크립트 (내가 미리 만들어 둠 — 당신 수작업 대폭 축소)

| 스크립트 | 하는 일 | 대체하는 수작업 |
|---------|--------|----------------|
| `web/functions/scripts/set-secrets.sh` | JWT_SECRET·CLIENT_API_KEYS 강한 랜덤 생성 후 Secret Manager 등록. SendGrid/Google 시크릿은 placeholder로 등록(첫 배포 실패 방지). **WPF에 넣을 BackendApiKey 값을 출력.** | §A1 시크릿 4개 수동 등록 |
| `web/functions/scripts/post-deploy-smoke.mjs` | 배포된 실제 URL에 health·API키 게이트·(선택)로그인 왕복을 읽기전용으로 검증. | §A6 도달성 확인 일부 |

> 두 스크립트는 **로컬 에뮬레이터로 동작 검증 완료**. 시크릿 값은 담고 있지 않음(생성/주입만).
> 로컬 개발 환경도 내가 정리해 둠: 시크릿은 `web/functions/.secret.local`(에뮬레이터 전용, gitignore), 비밀-아닌 값은 `web/functions/.env`. **`.env`에서 시크릿을 빼둔 이유** = 같은 이름이 `defineSecret`이면서 `.env`에도 있으면 **배포가 충돌 에러로 막히기** 때문(미리 방지함).

---

## 0. 한 번만 하는 준비

### 0-1. Firebase CLI 로그인 `[ ]`
```
firebase login
```
- 브라우저가 열리면 DB 소유 Google 계정으로 로그인. 이 세션 프롬프트에서 하려면 `! firebase login` 으로 실행하면 출력이 여기로 들어옵니다.
- 확인: `firebase projects:list` 에 `mcphoto-955fb` 가 보이면 OK.

### 0-2. 필요한 Google Cloud API 활성화 `[ ]`
- **Secret Manager API**: `set-secrets.sh`/배포 전에 필요. 보통 `firebase functions:secrets:set` 최초 실행 시 "활성화할까요?" 프롬프트가 뜨며 y로 진행됨. 수동은:
  Google Cloud Console → 프로젝트 `mcphoto-955fb` → **APIs & Services → Enable APIs & Services** → "Secret Manager API" 검색 → **Enable**.
- **첫 `firebase deploy --only functions` 시 자동 프롬프트**로 Cloud Functions·Cloud Build·Artifact Registry·Cloud Run·Eventarc API 활성화 요청이 뜸 → 모두 승인(y). 별도 수동 불필요.

---

## A. 백엔드 경유 마이그레이션 배포 (서비스 계정 키를 클라이언트에서 제거)

> 코드는 완료 — 서버 함수(P1, `web/functions/`)와 클라 HTTP 계층(P3, `MCPhoto.Http`, DI flag **기본 OFF**).
> **핵심 안전 불변식**: 키 폐기(A7)는 **반드시 맨 마지막**, 클라 전환(A5)이 프로덕션에서 검증된 뒤에만. 그 전까지 현행 키 경로가 살아 있어 `UseBackend=false`로 언제든 롤백 가능.

### A1. 시크릿 등록 `[ ]` — 스크립트 한 방
```
firebase login                      # (0-1에서 했으면 생략)
bash functions/scripts/set-secrets.sh
```
- 실행하면 JWT_SECRET·CLIENT_API_KEYS·SENDGRID_API_KEY·GOOGLE_OAUTH_CLIENT_SECRET 4개가 등록되고, 마지막에 이런 줄이 출력됩니다:
  ```
  BackendApiKey=1a2b3c...   ← 이 값을 A5에서 WPF MCPhoto.ini 에 넣습니다
  ```
  이 값을 메모하세요(분실 시 `firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb` 로 재확인).
- SendGrid/Google 시크릿은 아직 안 쓰면 placeholder로 들어갑니다(첫 배포 통과용). 실제 값은 §B1/§B2에서 교체.
- 비밀-아닌 설정(STORAGE_BUCKET·HOSTING_BASE_URL·JWT_EXPIRES_IN_SECONDS)은 `web/functions/.env`에 이미 들어 있어 배포 시 함께 적용됩니다(추가 작업 없음).

### A2. IAM — 서명 URL 권한 (⚠️ Emulator로는 못 잡는 지점) `[ ]`
프레임 저장·업로드 prepare는 v4 **서명 PUT URL**을 발급하는데, 이때 함수 런타임 서비스계정이 `signBlob` 권한(**Service Account Token Creator** 역할)을 자기 자신에 대해 가져야 합니다. 없으면 프로덕션에서 저장/업로드가 실패합니다.

**런타임 서비스계정 이메일 확인** (2nd gen 기본 = Compute 기본 SA):
```
gcloud projects describe mcphoto-955fb --format="value(projectNumber)"
# 출력 예: 123456789012  → SA 이메일 = 123456789012-compute@developer.gserviceaccount.com
```

**방법 ①: gcloud (권장, 정확)** — 위 SA에 자기 자신에 대한 Token Creator 부여:
```
gcloud iam service-accounts add-iam-policy-binding \
  123456789012-compute@developer.gserviceaccount.com \
  --member="serviceAccount:123456789012-compute@developer.gserviceaccount.com" \
  --role="roles/iam.serviceAccountTokenCreator" \
  --project=mcphoto-955fb
```
(`123456789012`를 실제 프로젝트 번호로 치환)

**방법 ②: 웹 콘솔** (gcloud 미설치 시):
1. Google Cloud Console → 프로젝트 `mcphoto-955fb` → **IAM & Admin → Service Accounts**.
2. 목록에서 `...-compute@developer.gserviceaccount.com` 클릭 → 상단 **PERMISSIONS** 탭 → **GRANT ACCESS**.
3. **New principals** = 같은 SA 이메일(자기 자신) 붙여넣기 → **Role** = "Service Account Token Creator" 선택 → **SAVE**.

### A3. 리전 확인 `[ ]`
- 기본 `asia-northeast3`(서울) — `web/functions/src/index.ts`의 `setGlobalOptions`. 그대로 쓰면 할 일 없음.

### A4. 함수 배포 (현행 Admin 경로와 공존) `[ ]`
```
firebase deploy --only functions
```
- 첫 배포는 API 활성화 프롬프트가 뜰 수 있음(0-2) → 승인.
- 배포만으로는 앱 동작 불변(앱 `UseBackend=false`가 기본).
- 배포 성공 후 콘솔에 **함수 URL**이 출력됩니다. 형식은 보통
  `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` (또는 `...run.app` 형태). 이 값이 A5의 `BackendBaseUrl`이며 **끝에 `/api` 포함**입니다.
- 즉시 스모크:
  ```
  export BASE_URL="https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api"
  export API_KEY="<A1에서 출력된 BackendApiKey>"
  node functions/scripts/post-deploy-smoke.mjs
  ```
  `health 200 / frames 키없음 401 / frames 유효키 200` 이 PASS면 도달·키·서명 경로 정상.
  (PowerShell이면 `$env:BASE_URL="..."; $env:API_KEY="..."; node functions/scripts/post-deploy-smoke.mjs`)

### A5. 클라이언트 백엔드 전환 (feature flag ON) `[ ]`
- 대상 PC의 `MCPhoto.ini` `[MCPhoto]` 섹션에:
  ```
  UseBackend=true
  BackendBaseUrl=https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api
  BackendApiKey=<A1에서 출력된 BackendApiKey>
  ```
- 빈 URL이면 앱이 자동으로 off로 되돌림(안전장치). 문제가 생기면 `UseBackend=false`로 즉시 롤백.

### A6. 배포 후 E2E 검증 `[ ]`
- [ ] A4 스모크(health·frames) PASS
- [ ] 로그인(manager/user) → JWT 수신, 화면 진입 — (선택) 스모크로도 확인:
  ```
  export LOGIN_ID="devmcjo"; export LOGIN_PW="<비번>"
  node functions/scripts/post-deploy-smoke.mjs
  ```
- [ ] 프레임: 기본 프레임 조회 + (파워) 저장 시 서명 PUT 성공, 목록 반영
- [ ] 업로드: prepare→PUT→commit 후 **웹 다운로드 페이지(`/?s=...`)가 사진/타임랩스 표시**
- [ ] 서명 PUT의 `x-goog-meta-firebaseStorageDownloadTokens`가 서명과 일치(불일치 시 다운로드 실패)
- [ ] 서버/클라 `StorageBucket` 일치(불일치 시 commit 400)
- [ ] 역할 위계: manager로 admin 삭제/초기화/역할지정 시도 → 서버 403

### A7. ⚠️ 서비스 계정 키 폐기 (불가역, 최후) `[ ]`
- A5·A6가 프로덕션에서 안정 확인된 **후에만**.
- publish 산출물/PC에서 `serviceAccountKey.json` 제거 + **GCP IAM에서 해당 키 회전/삭제**.
- 이후 앱은 백엔드 경유로만 DB 접근 → 키는 함수 런타임(ADC)에만 존재, 배포물엔 없음(목표 달성).
- 되돌릴 수 없으므로, 롤백 필요성이 완전히 사라졌다고 판단될 때 수행.

### A8. 보안 규칙 강화 (P5) `[ ]`
- 서버(Admin=규칙 우회)만 DB에 쓰므로, Firestore/Storage 규칙은 현행 "직접 접근 deny(웹 다운로드 토큰 URL만 허용)" 유지로 충분. WPF 직접 접근 제거(A7) 후 규칙에 추가로 열어둔 경로가 없는지 최종 점검.

### A9. 기존 평문 비밀번호 계정 `[ ]`
- 코드가 **로그인 성공 시 자동으로 bcrypt 해시로 지연 마이그레이션**(별도 배치 불필요).
- 계정 수가 적으면 로그인 유도/비번 초기화로 조기 전환 권장(선택).

---

## B. 계정 기능 — 이메일/SSO 관련 콘솔 작업

> 사전조건: B1·B2 모두 **백엔드 모드(A 섹션)** 가 선행돼야 실제로 동작.

### B1. 이메일 발송 공급자 (item1a 이메일 인증·비밀번호 찾기)
> 서버 코드는 완료. **개발 기본은 `log` sender = 실제 메일 미발송**(콘솔 로그로만 코드/링크 출력). 프로덕션 실발송은 아래 수행.

- **B1-1. SendGrid 계정·API 키** `[ ]`
  1. <https://sendgrid.com> 가입 → 본인 이메일 인증.
  2. 좌측 **Settings → API Keys → Create API Key** → 이름 입력 → 권한 **Restricted Access** 중 "Mail Send" 만 ON(또는 Full Access) → **Create** → **키가 한 번만 표시되니 즉시 복사**.
  3. 이 키를 Secret Manager에 등록(둘 중 하나):
     - 재실행: `SENDGRID_API_KEY="SG.복사한키" bash functions/scripts/set-secrets.sh` (JWT 등도 새 버전으로 회전됨), 또는
     - 개별: `firebase functions:secrets:set SENDGRID_API_KEY` 실행 후 키 붙여넣기.
- **B1-2. 발신 도메인·발신자 인증** `[ ]`
  - **Settings → Sender Authentication** 에서 둘 중 하나:
    - **Authenticate Your Domain**(권장): SendGrid가 준 CNAME(SPF/DKIM) 레코드를 당신 도메인 DNS에 추가 → 인증되면 `no-reply@도메인` 발신 가능.
    - **Single Sender Verification**(간단): 발신 주소 1개를 이메일 확인 → 그 주소만 발신 가능.
  - 인증된 발신 주소를 기억(B1-3의 `EMAIL_FROM`).
- **B1-3. 프로덕션 활성화(env 전환, 소스 수정 불요)** `[ ]`
  - `web/functions/.env.mcphoto-955fb` 파일을 만들어(없으면 새로) 아래 두 줄 추가 — 이 프로젝트 배포에만 적용되는 비밀-아닌 설정:
    ```
    EMAIL_PROVIDER=sendgrid
    EMAIL_FROM=no-reply@your-domain.example   # B1-2에서 인증한 주소
    ```
  - **에뮬레이터는 log로 유지**하려면 `web/functions/.env.local`(에뮬레이터 전용, 배포 안 됨)에 `EMAIL_PROVIDER=log` 한 줄. (안 만들면 로컬 에뮬레이터도 sendgrid를 시도 → EMAIL_FROM/키 없으면 부팅 실패하니, 로컬에서 돌릴 거면 이 파일 권장.)
  - `firebase deploy --only functions` 재배포 → 이후 인증/재설정 메일이 실제로 발송됨.
- **B1-4. (링크 방식 채택 시) 웹 verify/reset 페이지** `[ ]`: 앱 내 6자리 코드 입력 방식만 쓰면 **불요**. 이메일 링크 방식을 원하면 `{hostingBaseUrl}/verify`·`/reset` 정적 페이지 필요.
- **B1-5. (선택) 토큰 TTL 정책** `[ ]`: `users/{id}/tokens` 의 `expiresAt`에 Firestore 네이티브 TTL. 서버가 만료를 코드로도 재확인하므로 미설정이어도 보안엔 무해(청소 목적).

### B2. Google OAuth 클라이언트 (item1b Google SSO)
> 서버 코드는 완료. **개발 기본은 client id/secret 미설정 → `/auth/google` 501(비활성).** id·secret **둘 다** 설정돼야 켜짐.

- **B2-1. OAuth 동의 화면 구성** `[ ]`
  1. Google Cloud Console → 프로젝트 `mcphoto-955fb` → **APIs & Services → OAuth consent screen**.
  2. **User Type**: 사내 Workspace 전용이면 **Internal**, 외부 계정 허용이면 **External** → CREATE.
  3. 앱 이름 / 사용자 지원 이메일 / 개발자 연락 이메일 입력.
  4. **Scopes**: `openid`, `email`, `profile` 추가.
  5. External이면 **Test users**에 로그인할 계정 등록(게시 전) 또는 앱 게시(Publish).
- **B2-2. OAuth 2.0 클라이언트 ID 생성 (Desktop app)** `[ ]`
  1. **APIs & Services → Credentials → Create Credentials → OAuth client ID**.
  2. **Application type: Desktop app** ⚠️(Web application 아님 — Web은 정확한 리디렉션 URI·포트 매칭을 요구) → 이름 → **Create**.
  3. 표시되는 **Client ID**와 **Client Secret** 복사.
- **B2-3. 백엔드 자격 등록** `[ ]`
  - Client Secret(백엔드 전용): `firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET` → 붙여넣기. (또는 `GOOGLE_OAUTH_CLIENT_SECRET="..." bash functions/scripts/set-secrets.sh`)
  - Client ID(비밀 아님): `web/functions/.env.mcphoto-955fb` 에 추가:
    ```
    GOOGLE_OAUTH_CLIENT_ID=<B2-2의 Client ID>
    ```
  - ⚠️ **id와 secret 중 하나만** 설정하면 서버가 "부분 구성" 오류로 전 요청 실패하니 **반드시 둘 다**. 로컬 에뮬레이터에서 Google을 안 켤 거면 `.env.local`엔 넣지 말 것.
  - `firebase deploy --only functions` 재배포.
- **B2-4. 클라이언트 설정(배포 PC INI)** `[ ]`: 대상 PC `MCPhoto.ini` `[MCPhoto]`에 `GoogleClientId=<B2-2 Client ID>` 추가. **client secret은 클라에 넣지 않음**(백엔드 전용). 설정되면 로그인 화면에 "Google로 로그인" 노출.
- **B2-5. (선택) 허용 도메인(hd) 제한** `[ ]`: 특정 Workspace 도메인만 허용하려면 `.env.mcphoto-955fb`에 `GOOGLE_ALLOWED_HD=<도메인>` (서버가 id_token.hd와 대조). 미설정이면 등록·검증된 email 매핑 화이트리스트로만 통제.
- **B2-6. 실왕복 스모크** `[ ]`: 스테이징에서 **운영자 계정 1건 선등록(email + emailVerified=true)** → SSO 로그인 → 화면 진입 수동 확인(실 Google 왕복은 코드로 검증 불가).

---

## C. (예정) 장치 연동
### C1. 카메라(DSLR)·프린터 장비 선정 `[ ]` (item3)
- 실제 하드웨어 연동은 특정 모델/SDK/연결방식(BT·WiFi) 의존 → **장비 선정·드라이버·SDK 조사 필요**. 코드엔 추상 인터페이스·설정 자리(`IExternalCamera`/`IPhotoPrinter`·로그인 전용 옵션)만 있고, 실제 연동은 장비 확정 후. 확장 지점: `ServiceRegistration.cs`의 `Null*` 등록을 실 구현으로 교체.
