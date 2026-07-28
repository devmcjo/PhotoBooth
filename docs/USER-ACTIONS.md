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
- 실행하면 JWT_SECRET·CLIENT_API_KEYS·GOOGLE_OAUTH_CLIENT_SECRET 3개가 등록되고, 마지막에 이런 줄이 출력됩니다:
  ```
  BackendApiKey=1a2b3c...   ← 이 값을 A5에서 WPF MCPhoto.ini 에 넣습니다
  ```
  이 값을 메모하세요(분실 시 `firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb` 로 재확인).
- Google 시크릿은 아직 안 쓰면 placeholder로 들어갑니다(첫 배포 통과용). 실제 값은 §B2에서 교체.
- **it15**: `SENDGRID_API_KEY` 는 더 이상 필요 없습니다(이메일 기능 폐지). 이미 등록돼 있어도 무해하며, 정리하려면
  Google Cloud Console → Secret Manager 에서 삭제하면 됩니다.
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

## B. 계정 기능 — Google SSO 콘솔 작업

> 사전조건: B1·B2 모두 **백엔드 모드(A 섹션)** 가 선행돼야 실제로 동작.

### ~~B1. 이메일 발송 공급자~~ — **it15에서 폐지. 할 일 없음** ✅

> it15로 **이메일 인증·비밀번호 재설정 기능 자체가 제거**됐다. 서버는 더 이상 메일을 보내지 않는다.
> 따라서 SendGrid 가입·API 키·발신자 인증·`EMAIL_PROVIDER`/`EMAIL_FROM` 설정은 **전부 불필요**하다.
>
> - `SENDGRID_API_KEY` 시크릿이 이미 등록돼 있어도 무해하다(어떤 함수도 선언·참조하지 않음). 정리하려면
>   Google Cloud Console → **Secret Manager** 에서 삭제.
> - `web/functions/.env.mcphoto-955fb` 에 `EMAIL_PROVIDER`/`EMAIL_FROM` 줄이 있다면 지워도 된다(무시됨).
> - 비밀번호를 잊었을 때의 복구 경로는 이제 **PIN 재설정**이다: 관리자가 사용자 관리 화면에서 하위 계정 PIN을
>   재설정하고, admin 본인 PIN 분실은 §D1-7 스크립트로 복구한다.

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
- **B2-5. (선택) 허용 도메인(hd) 제한** `[ ]`: 특정 Workspace 도메인만 허용하려면 `.env.mcphoto-955fb`에 `GOOGLE_ALLOWED_HD=<도메인>` (서버가 id_token.hd와 대조). 미설정이면 email 매핑으로만 통제.
- **B2-6. 실왕복 스모크** `[ ]`: 스테이징에서 SSO 로그인 → 화면 진입 수동 확인(실 Google 왕복은 코드로 검증 불가).
  - ⚠️ **it15**: Google SSO가 **유일한 로그인 수단**이다. 계정 선등록은 불요 — 처음 로그인하는 Google 계정은
    서버가 자동으로 만든다. 단 **신규 계정은 무조건 `temp_user`** (QR 전송 48시간/30회 한도)로 생성되므로,
    실사용 계정은 admin이 사용자 관리 화면에서 `user` 이상으로 승격해야 한다.

---

## C. (예정) 장치 연동
### C1. 카메라(DSLR)·프린터 장비 선정 `[ ]` (item3)
- 실제 하드웨어 연동은 특정 모델/SDK/연결방식(BT·WiFi) 의존 → **장비 선정·드라이버·SDK 조사 필요**. 코드엔 추상 인터페이스·설정 자리(`IExternalCamera`/`IPhotoPrinter`·로그인 전용 옵션)만 있고, 실제 연동은 장비 확정 후. 확장 지점: `ServiceRegistration.cs`의 `Null*` 등록을 실 구현으로 교체.

---

## D. it15 계정 마이그레이션 (1회성)

> **배경**: it15로 ID/PW 인증이 폐지되고 Google SSO + 4자리 PIN만 남았다. 기존 Firestore `users` 문서에는
> `password`·`emailVerified` 같은 폐지 필드가 남아 있고, `devmcjo@gmail.com` 으로 SSO 가입한 계정은
> 문서 ID가 `devmcjo-2`(원래 `devmcjo` 를 구 비번 계정이 선점) 상태다.
> Firestore는 문서 ID를 바꿀 수 없으므로 **재생성 → 참조 갱신 → 삭제** 순서의 스크립트로 정리한다.
>
> **이 스크립트가 최초 admin을 만든다.** HTTP API로는 admin을 지정할 수 없으므로(서버 `canSetRole`이 차단),
> 마이그레이션을 돌리기 전까지는 승격 권한을 가진 계정이 존재하지 않는다.

**스크립트**: `web/functions/scripts/migrate-google-only-accounts.mjs`

### D1. 실행 절차

- **D1-1. 사전 — 서비스 중단 창 확보** `[ ]`
  - **키오스크 앱을 종료**한다. 실행 중 Step 2~3 사이에 같은 email을 가진 문서가 잠시 2건 공존하는데,
    그 구간에 SSO 로그인이 들어오면 어느 문서로 매핑될지 비결정적이다(실행 시간은 수 초).
  - (강력 권장) 실행 전 백업: `gcloud firestore export gs://<버킷>/backup-$(date +%Y%m%d)`
    — **스크립트에 undo 경로는 없다.**
- **D1-2. 준비** `[ ]`
  ```
  cd web/functions
  npm ci
  npm run build          # 순수 계획 로직(lib/domain/migration.js)이 필요합니다
  ```
- **D1-3. 인증(ADC)** `[ ]`
  - `gcloud auth application-default login` (또는 서비스 계정 키 경로를 `GOOGLE_APPLICATION_CREDENTIALS` 에 설정)
- **D1-4. dry-run — 반드시 먼저** `[ ]`
  ```
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb
  ```
  - **기본이 dry-run이라 아무것도 바뀌지 않는다.** 출력의 Step 2/3/4 계획과 **Step 5 목록을 육안 확인**한다.
  - Step 5에 뜬 계정은 email이 없어 Google 로그인이 불가능한 계정이다. **지워도 되는지 직접 판단**할 것.
  - 전체 문서를 보려면 `--verbose` 추가.
- **D1-5. 적용(비파괴)** `[ ]`
  ```
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --apply
  ```
  - admin 재생성 + 프레임 참조 갱신 + 필드 정리까지만 한다. **계정 삭제는 하지 않는다.**
- **D1-6. 적용(파괴 — 선택)** `[ ]`
  ```
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --apply --delete-orphans
  ```
  - D1-4에서 확인한 로그인 불가 계정과 그 소유 프레임(문서 + Storage 이미지)을 **영구 삭제**한다.
  - 프레임 이미지 삭제에 버킷명이 필요하다. `web/functions/.env` 의 `STORAGE_BUCKET` 을 읽거나
    `--bucket mcphoto-955fb.firebasestorage.app` 로 직접 지정한다(모르면 스크립트가 중단한다 — 고아 파일 방지).
- **D1-7. admin PIN 분실 시 복구** `[ ]`
  ```
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --clear-pin devmcjo --apply
  ```
  - 해당 계정의 `pinHash` 필드만 지운다(다른 단계는 실행하지 않음). 이후 앱에서 설정/계정 관리에 진입하면
    PIN 최초 설정을 요구하므로 새 PIN을 만들면 된다.
  - ⚠️ admin 본인 PIN은 앱 안에서 복구할 수 없다(자기 자신 PIN 재설정은 서버가 400으로 거부).
    이 CLI 경로가 유일한 복구 수단이다.
- **D1-8. 검증** `[ ]`
  - 멱등 확인: dry-run을 다시 돌려 **계획 0건**이 나오는지 본다.
    ```
    node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb
    ```
  - 앱 실행 → Google 로그인(`devmcjo@gmail.com`) → 상단 바에 `devmcjo` 표시 + **관리자 도구 노출** 확인.
  - Firestore 콘솔에서 `users/devmcjo` 가 `role:"admin"`·`authMethod:"google"` 이고
    `password`·`emailVerified` 필드가 **없는지** 확인.

### D2. 운영 전제 — admin 1인 상시 유지 ⚠️

- 신규 SSO 계정은 **전원 `temp_user`** 로 생성된다. 그리고 **승격(등급 올리기)은 admin만 할 수 있다**
  (manager는 `user → temp_user` 강등만 가능 — it13에서 확정된 매트릭스).
- 따라서 **admin 계정이 하나도 없으면 아무도 승격할 수 없는 상태**가 된다. admin 계정을 삭제하거나
  강등하지 말 것. 부득이하게 admin이 사라졌다면 위 마이그레이션 스크립트로 다시 지정해야 한다.

### D3. 종료 코드

| 코드 | 의미 | 조치 |
|---|---|---|
| 0 | 성공(또는 dry-run 완료) | — |
| 1 | 인자 오류 / admin-email 계정 미발견 / 버킷 미지정 | 메시지대로 인자 수정 후 재실행 |
| 2 | 실행 중 실패(**부분 적용**) | **같은 명령을 그대로 재실행**한다. 스크립트는 멱등이라 남은 작업만 이어서 처리한다 |
