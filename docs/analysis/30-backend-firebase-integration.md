# 30 · 백엔드 연동 — 앱 ↔ 백엔드 API(Cloud Functions)

| 항목 | 내용 |
|------|------|
| 문서 | 앱의 백엔드 접근(HTTP 클라이언트)과 서버(Cloud Functions) 계약 — 인증·업로드·프레임·계정·한도 |
| 범위 | `src/MCPhoto.Http/*` 전체 + `src/MCPhoto.Core/{Upload,Accounts,Frames}` 계약 + `web/functions/src/*` 엔드포인트 + DI 등록. 스키마 상세는 [40 · DB/Storage 스키마](./40-database-firestore-and-storage-schema.md), 권한 매트릭스는 [60 · 인증·계정·역할](./60-auth-accounts-and-roles.md) |
| 최종 업데이트 | 2026-07-29 (it15·it16 반영 — **전면 재작성**: `MCPhoto.Firebase`(Admin SDK 직결) 폐지 → 백엔드 API 경유) |
| 관련 소스 | `src/MCPhoto.Http/{HttpBackendClient,HttpFirebaseClient,HttpFrameRepository,HttpAccountService,HttpQrUsageService,HttpTempUserLimitsService}.cs`, `src/MCPhoto.Http/Session/{IBackendSession,BackendSession}.cs`, `src/MCPhoto.Core/Upload/{IFirebaseClient,IUploadService,UploadService,UploadContract,QrService}.cs`, `src/MCPhoto.App/{ServiceRegistration.cs,Services/BackendSessionSynchronizer.cs,ViewModels/QrPopupViewModel.cs}`, `web/functions/src/{index,app,config}.ts`, `web/functions/src/http/auth.ts`, `web/functions/src/routes/*.ts`, `web/functions/src/services/{uploads,signing,frames,accounts}.ts`, `web/functions/src/domain/tempUserLimit.ts` |
| 갱신 규칙 | 엔드포인트 경로·게이트(`requireApiKey`/`requireBearer`/`optionalBearer`/`requirePower`/`requireAdmin`), 업로드 3단계(prepare→PUT→commit) 계약, 토큰 URL·세션 ID 규약(`UploadContract`↔`domain/session.ts`), `BackendException` 상태코드 매핑, DI 조립(`RegisterBackendServices`)이 바뀌면 이 문서를 갱신한다. 스키마 변경은 40번과 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정.
>
> ⚠️ 파일명 `30-backend-firebase-integration.md`는 타 문서 링크 보존을 위해 유지한다. 내용의 기준은 "Admin SDK 직결"이 아니라 **백엔드 API 경유**다.
>
> 🆕 **새 클라이언트를 구현한다면 [31 · 백엔드 API 참조](./31-backend-api-reference.md)를 보라.** 이 문서는 **설계 의도와 실패 정책**(왜 선택적 Bearer인가, 왜 한도는 서버가 진실원인가, 미도달 시 무엇이 fail-open/fail-closed인가)을 다루고, 31번이 **요청/응답 JSON·헤더·상태코드·검증 규칙**을 전수로 다룬다. 둘은 보완 관계이며 §4의 엔드포인트 카탈로그는 31번 §4의 요약이다.

---

## 0. it15 이전과 무엇이 달라졌나 (이력)

`MCPhoto.Firebase` 어셈블리는 **삭제됐다**. 솔루션의 `src/`는 `MCPhoto.Core`·`MCPhoto.Capture`·`MCPhoto.Http`·`MCPhoto.App` 4개이며, 백엔드 접근은 전부 `MCPhoto.Http`가 담당한다(`MCPhoto.sln`, `MCPhoto.App.csproj` ProjectReference).

| 축 | it15 이전(폐지) | 현행 |
|----|-----------------|------|
| 앱의 DB/Storage 접근 | `FirestoreDb`/`StorageClient` 직결(Admin SDK) | 백엔드 HTTPS API(`https://…/api/{path}`) 호출, 파일 바이트만 서명 URL로 직접 PUT |
| 앱의 자격증명 | `serviceAccountKey.json`(관리자 권한, 규칙 우회) | **없음**. 배포 게이트 키(`X-MCPhoto-Client`) + 로그인 JWT(Bearer) |
| Admin 권한 위치 | WPF 프로세스 | 서버(Cloud Functions)만 — ADC(런타임 기본 서비스계정)로 초기화, 키 파일 없음(`web/functions/src/index.ts:4`) |
| 로그인 | id/pw 평문 비교 + 시드 계정 인메모리 폴백 | Google SSO 단일 경로(`POST /auth/google`) → 서버 발급 JWT. **오프라인 로그인 폴백 없음** |
| 가용성 축 | `IsInitialized`(키 로드 여부) | **백엔드 도달 가능/불가**(§11) |
| feature flag | `AppSettings.UseBackend` 분기 | 없음 — 백엔드 전용 경로 하나(`ServiceRegistration.cs:80`) |

---

## 1. 구성 요소 개요

`MCPhoto.Core`가 계약(인터페이스)을, `MCPhoto.Http`가 HTTP 구현을 제공하고, WPF 앱은 인터페이스에만 의존한다.

| 계약(Core) | 구현 | 책임 | 근거 |
|------------|------|------|------|
| `IFirebaseClient` | `HttpFirebaseClient`(Http) | 업로드 prepare/PUT/commit 게이트웨이 | `src/MCPhoto.Core/Upload/IFirebaseClient.cs:11`, `src/MCPhoto.Http/HttpFirebaseClient.cs:28` |
| `IUploadService` | `UploadService`(**Core**) | 업로드 오케스트레이션 + `ResultSession` 조립 | `src/MCPhoto.Core/Upload/UploadService.cs:13` |
| `IFrameRepository` | `HttpFrameRepository`(Http) | `/frames` 조회·저장·삭제 | `src/MCPhoto.Http/HttpFrameRepository.cs:27` |
| `IAccountService` | `HttpAccountService`(Http) | `/auth/google`·`/accounts` 로그인·목록·역할·PIN | `src/MCPhoto.Http/HttpAccountService.cs:24` |
| `IQrUsageService` | `HttpQrUsageService`(Http) | 본인 QR 사용 게이트 상태 조회 | `src/MCPhoto.Core/Accounts/IQrUsageService.cs:30`, `src/MCPhoto.Http/HttpQrUsageService.cs:18` |
| `ITempUserLimitsService` | `HttpTempUserLimitsService`(Http) | 전역 TempUser 한도 조회·수정 | `src/MCPhoto.Core/Accounts/IQrUsageService.cs:50`, `src/MCPhoto.Http/HttpTempUserLimitsService.cs:17` |
| `IQrService` | `QrService`(**Core**) | 다운로드 페이지 URL → QR PNG | `src/MCPhoto.Core/Upload/QrService.cs:8` |
| `UploadContract`(순수 로직) | — | 세션 ID·Storage 경로·토큰 URL·downloadPageUrl·expiresAt 조립 | `src/MCPhoto.Core/Upload/UploadContract.cs:9` |

- `UploadService`·`QrService`는 백엔드에 의존하지 않아 **it15에서 Core로 이관**됐다(`UploadService.cs:11` 주석). `UploadService` 본문은 무변경이며 `IFirebaseClient`에만 의존한다.
- ⚠️ **`IFirebaseClient`라는 이름은 실체(백엔드 게이트웨이)와 어긋난다.** 이번 범위에서 리네임하지 않기로 한 백로그 항목이다(`IFirebaseClient.cs:6-9` 주석).
- `QrService`는 QRCoder `PngByteQRCode` 래퍼(System.Drawing 불필요), ECC 레벨 Q, 기본 모듈 20px(`QrService.cs:10-15`).

### 1.1 공통 기반 `HttpBackendClient`

Http 구현 4종(`HttpFirebaseClient`·`HttpFrameRepository`·`HttpAccountService`·`HttpQrUsageService`·`HttpTempUserLimitsService`)이 모두 상속한다(`HttpBackendClient.cs:21`).

| 요소 | 내용 | 근거 |
|------|------|------|
| 명명 HttpClient | `"backend"` — `IHttpClientFactory`에서 획득. BaseAddress·타임아웃(100초)은 DI가 주입 | `HttpBackendClient.cs:24,52`, `ServiceRegistration.cs:103-109` |
| API 키 헤더 | `X-MCPhoto-Client` — **모든 호출에 부착**(키가 비어 있지 않으면). 서버 `API_KEY_HEADER`와 정합 | `HttpBackendClient.cs:27,103-104`, `web/functions/src/http/auth.ts:17` |
| Bearer 모드 | `None` / `Optional`(토큰 있으면 부착, 없으면 익명 통과) / `Required`(없으면 `UnauthorizedAccessException`) | `HttpBackendClient.cs:55,106-120` |
| 네트워크 실패 | `HttpRequestException`·`TaskCanceledException` → `InvalidOperationException("백엔드에 연결할 수 없습니다.")` | `HttpBackendClient.cs:136-141` |
| 오류 응답 | 표준 에러 봉투 파싱 → `BackendException(StatusCode, ServerCode, Message)` | `HttpBackendClient.cs:158-182` |
| 로깅 | 시크릿·토큰은 절대 로그에 남기지 않는다 | `HttpBackendClient.cs:19` |

---

## 2. 서버(Cloud Functions) 개요

| 항목 | 값 | 근거 |
|------|-----|------|
| 런타임 | Cloud Functions **2nd gen**, TypeScript, Express | `web/functions/src/index.ts`, `app.ts` |
| 함수 | 단일 HTTPS 함수 `api` — 실제 URL은 `.../api/{path}` | `index.ts:29-42` |
| 리전·스케일 | `asia-northeast3`(서울), `maxInstances: 10`, `memory 256MiB`, `timeoutSeconds 60` | `index.ts:26,36-40` |
| 라우터 | `/auth` `/accounts` `/config` `/frames` `/uploads` `/health` 6개 + 404 + 에러 미들웨어 | `app.ts:27-53` |
| 본문 제한 | `express.json({ limit: "256kb" })` — 파일 바이트는 함수를 경유하지 않으므로 충분 | `app.ts:25` |
| Admin 초기화 | **ADC**(런타임 기본 서비스계정). 키 파일 없음 | `index.ts:4`, `firebase.ts` |

### 2.1 서버 구성값 (`loadConfig`)

시크릿은 Secret Manager, 일반 설정은 env/param. 필수값 누락이면 **로드 시점에 예외로 조기 실패**(오구성 배포 방지, `config.ts:55-112`).

| 키 | 출처 | 필수 | 용도 |
|----|------|:---:|------|
| `JWT_SECRET` | Secret Manager | ✅ | JWT(HS256) 서명 |
| `CLIENT_API_KEYS` | Secret Manager (CSV) | ✅ | `X-MCPhoto-Client` 유효 키 목록 |
| `GOOGLE_OAUTH_CLIENT_SECRET` | Secret Manager | SSO 사용 시 | Google code 교환(백엔드 전용) |
| `STORAGE_BUCKET` | env | ✅ | 서명 URL·토큰 URL 조립 |
| `HOSTING_BASE_URL` | env | — | 다운로드 페이지 base URL |
| `JWT_EXPIRES_IN_SECONDS` | env | — | 기본 `28800`(8시간) |
| `GOOGLE_OAUTH_CLIENT_ID` | env | SSO 사용 시 | **SSO 활성화 신호** — 없으면 `/auth/google`는 501 |
| `GOOGLE_ALLOWED_HD` | env | — | 허용 Workspace 도메인(빈 값이면 제한 없음) |

- `GOOGLE_OAUTH_CLIENT_SECRET`은 `defineSecret` 모델상 배포 시 항상 존재해야 하므로, SSO 미사용이어도 placeholder 등록이 필요하다. 따라서 "시크릿만 있고 id 없음"은 **정상 비활성** 상태이며, `id`만 켜고 시크릿이 없을 때만 오구성으로 조기 실패한다(`config.ts:83-97`).
- 로컬/Emulator는 `functions/.env`(git-ignored)에서 읽는다(`index.ts:8`, `web/functions/.env.example`).

---

## 3. 인증 모델

### 3.1 앱(생산자) — 배포 게이트 키 + 로그인 JWT

두 인증은 **독립**이다(`http/auth.ts:8`).

| 게이트 | 미들웨어 | 통과 조건 | 실패 | 근거 |
|--------|----------|-----------|------|------|
| 배포 키 | `requireApiKey()` | `X-MCPhoto-Client`가 `CLIENT_API_KEYS`에 포함 | 401 | `http/auth.ts:36-45` |
| 로그인 | `requireBearer()` | 유효 JWT → `req.principal={id, role}` 주입 | 401 | `http/auth.ts:50-67` |
| 선택 로그인 | `optionalBearer()` | 토큰 없음=게스트 통과 / 유효=principal 주입 / **무효=401**(위조 거부) | 401(무효 토큰만) | `http/auth.ts:77-96` |
| 파워 | `requirePower()` | `role ∈ {manager, admin}` | 403 | `http/auth.ts:99-108` |
| 관리자 | `requireAdmin()` | `role == admin` | 403 | `http/auth.ts:111-120` |

- **API 키 출처(앱 측)**: publish 시 exe에 내장된 `AssemblyMetadata("MCPhoto.BackendApiKey")`가 기본값이고, `MCPhoto.ini`의 `BackendApiKey=`가 있으면 그 값이 우선한다(`ServiceRegistration.cs:52-55,195-202`, `IniSettingsService.cs:16-18,36-37`). 일반 빌드에는 내장 키가 없어 빈 문자열이다.
- **JWT 보관**: `IBackendSession`(메모리 홀더). 로그인 성공 시 `HttpAccountService`가 `Session.SignIn(token, user)`으로 저장한다(`HttpAccountService.cs:54`).
- **로그아웃 시 토큰 폐기**: `BackendSessionSynchronizer`가 `SessionContext.CurrentUserChanged`를 구독해 `CurrentUser == null`이 되는 **모든** 경로에서 `Session.Clear()`를 호출한다(`BackendSessionSynchronizer.cs:44-48`). 이 배선이 없으면 로그아웃 후에도 JWT가 남아, 선택적 Bearer 업로드가 그 토큰을 조용히 부착해 **다음 게스트 촬영물이 직전 계정 소유로 기록**되고 TempUser 계정이면 QR 횟수까지 차감된다(`BackendSessionSynchronizer.cs:10-12`).

### 3.2 웹(소비자) — 공개 API 키 + 보안 규칙

- 웹 다운로드 페이지는 공개 Firebase JS SDK config로 접근하며 **보안 규칙이 유일한 방어선**이다. 웹은 `resultSessions` 단건 get만 하고, 파일은 문서에 담긴 토큰 URL로 직접 GET한다(상세는 [40 §4·§5](./40-database-firestore-and-storage-schema.md)).

### 3.3 접근 방식 대비

| 주체 | 인증 | 보안 규칙 | 접근 범위 |
|------|------|-----------|-----------|
| WPF 앱 | 배포 게이트 키 + (필요 시) JWT | **무관** — 앱은 Firestore/Storage를 직접 만지지 않는다 | 백엔드가 허용한 엔드포인트 + 서명 URL PUT(경로·Content-Type·TTL 15분 고정) |
| 백엔드(Functions) | ADC 서비스계정 | 우회(Admin) | 서버 코드가 정의한 범위 전체 |
| 웹 | 공개 API 키(비인증) | **종속** | `resultSessions` 단건 get + 토큰 URL 직접 GET |

> 규칙 파일(`web/firestore.rules`·`web/storage.rules`)의 "웹 쓰기 금지"는 그대로 유효하다. 달라진 점은 **쓰기 주체가 WPF에서 서버로 이동**했다는 것이다.

---

## 4. 엔드포인트 카탈로그

| 메서드·경로 | 게이트 | 서버 | 앱 호출부 |
|-------------|--------|------|-----------|
| `POST /auth/google` | API키 | `routes/auth.ts:33` | `HttpAccountService.LoginWithGoogleAsync`(`:35`) |
| `GET /accounts` | Bearer + 파워 | `routes/accounts.ts:33` | `GetAllAsync`(`:75`) |
| `DELETE /accounts/{id}` | Bearer + 파워(+위계·자기삭제 금지) | `routes/accounts.ts:94` | `DeleteAsync`(`:90`) |
| `PATCH /accounts/{id}/role` | Bearer + 파워(+매트릭스는 서비스가 강제) | `routes/accounts.ts:110` | `SetRoleAsync`(`:105`) |
| `GET /accounts/me/qr-usage` | Bearer | `routes/accounts.ts:43` | `HttpQrUsageService.GetStatusAsync`(`:29`) |
| `POST /accounts/me/pin/verify` | Bearer(본인) | `routes/accounts.ts:54` | `VerifyPinAsync`(`:122`) |
| `PUT /accounts/me/pin` | Bearer(본인) | `routes/accounts.ts:73` | `SetOwnPinAsync`(`:145`) |
| `PUT /accounts/{id}/pin` | Bearer + 파워(+`canResetPin` — 엄격히 낮은 위계) | `routes/accounts.ts:131` | `ResetPinAsync`(`:164`) |
| `GET /config/temp-user-limits` | Bearer | `routes/config.ts:27` | `GetLimitsAsync`(`:28`) |
| `PATCH /config/temp-user-limits` | Bearer + admin | `routes/config.ts:35` | `SetLimitsAsync`(`:42`) |
| `GET /frames/default` | API키(게스트 가능) | `routes/frames.ts:32` | `GetDefaultFramesAsync`(`:41`) |
| `GET /frames?userId=` | Bearer(본인 또는 파워) | `routes/frames.ts:41` | `GetUserFramesAsync`(`:55`) |
| `POST /frames` | Bearer + 파워 | `routes/frames.ts:59` | `SaveAsync`(`:70`) |
| `PUT /frames/{id}` | Bearer + 파워 | `routes/frames.ts:88` | **없음** — 운영/관리 도구 전용(`HttpFrameRepository.cs:99-100`) |
| `DELETE /frames/{id}` | Bearer + 파워 | `routes/frames.ts:120` | `DeleteAsync`(`:102`) |
| `POST /uploads/prepare` | API키 + 선택 Bearer | `routes/uploads.ts:23` | `HttpFirebaseClient.UploadFileAsync`(`:96`) |
| `POST /uploads/commit` | API키 + 선택 Bearer | `routes/uploads.ts:48` | `CreateResultSessionAsync`(`:143`) |
| `GET /health` | 없음 | `routes/health.ts` | `ProbeReachableAsync`(`:55`, 선택적 상태 표시용) |

- it15에서 제거된 인증 라우트(`login`·`register`·`verify-email`·`password-reset`)와 계정 생성(`POST /accounts`)·비밀번호/이메일 변경은 **스텁을 남기지 않았다** — 미매칭 경로는 404 핸들러가 처리한다(`app.ts:6-7`, `routes/accounts.ts:5`).

---

## 5. 업로드 흐름 (prepare → 직접 PUT → commit)

트리거는 결과 화면 이후 QR 팝업 진입(`QrPopupViewModel.OnEnterAsync`)이며, 오케스트레이션은 `UploadService.UploadResultAsync`가 담당한다.

### 5.1 사전 조건 및 미디어 선택

| 단계 | 동작 | 근거 |
|------|------|------|
| 진입 | QR off면 애초에 QR 상태로 오지 않음(ResultViewModel 분기) | `QrPopupViewModel.cs` |
| 미디어 선택 | `SendPhoto`/`SendTimelapse` 옵션에 따라 경로 전달 | `QrPopupViewModel.cs` |
| 구성 가드 | `IFirebaseClient.IsInitialized=false`(백엔드 base URL 미설정)면 예외 | `UploadService.cs:32-33` |
| 존재 가드 | 각 미디어는 "옵션 on(경로 non-null) & `File.Exists`"일 때만 업로드 | `UploadService.cs:36-37` |
| 최소 1개 불변식 | 사진·타임랩스 모두 부재면 예외("전송할 미디어가 없습니다") | `UploadService.cs:38-39` |

- ⚠️ `IsInitialized`는 이제 **구성 사실**(base URL 설정됨)이지 서버 도달 성공이 아니다. 실시간 헬스체크로 흔들지 않는다(`HttpFirebaseClient.cs:47-52`).

### 5.2 세션 ID·경로 규약 (`UploadContract`)

| 항목 | 값 | 근거 |
|------|-----|------|
| 세션 ID | `{yyyyMMdd_HHmmss}_{uuid}` (**로컬 시간** stamp + UUIDv4) — 폴더명·문서 ID·다운로드 페이지 토큰을 겸한다 | `UploadContract.cs:18,25`, `UploadService.cs:43` |
| 사진 경로 | `results/{sessionId}/final.{png\|jpg}` | `UploadContract.cs:28-29` |
| 타임랩스 경로 | `results/{sessionId}/timelapse.mp4` | `UploadContract.cs:32-33` |
| 토큰 URL | `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{escaped}?alt=media&token={t}` | `UploadContract.cs:39-43` |
| 다운로드 페이지 | `{hostingBaseUrl 트레일링슬래시제거}/?s={sessionId}` | `UploadContract.cs:49-53` |
| 만료 | `expiresAt = createdAt + retentionHours` | `UploadContract.cs:56-57` |

서버도 동일 규약을 `web/functions/src/domain/session.ts`에 이식해 두어, 클라가 조립한 URL과 서버가 발급한 `downloadUrl`이 일치한다.

### 5.3 3단계 상세

| 단계 | 클라 | 서버 | 근거 |
|------|------|------|------|
| ① prepare | `POST /uploads/prepare {sessionId, files:[{kind, ext, contentType}]}` — 파일 1개씩 호출 | sessionId 형식 검증 → (TempUser면 한도 선검사) → GCS **V4 서명 PUT URL**(TTL 15분) + 다운로드 토큰 URL 발급 → `{uploads[], bucket}` | `HttpFirebaseClient.cs:82-97`, `routes/uploads.ts:23-45`, `services/uploads.ts:60-97`, `services/signing.ts:19,58-101` |
| ② PUT | 서명 URL로 **파일 바이트 직접 전송**. `requiredHeaders`(Content-Type + `x-goog-meta-firebaseStorageDownloadTokens`)를 서명대로 부착. 진행률은 `ProgressStream`이 보고 | (함수 미경유) | `HttpFirebaseClient.cs:180-234`, `services/signing.ts:14-16` |
| ③ commit | `POST /uploads/commit {sessionId, finalImageUrl?, timelapseUrl?, retentionHours, downloadPageUrl}` | 최소 1개 불변식 + **URL 위조 검증**(버킷·`results/{sid}/` 경로 일치) → `resultSessions/{sid}` 생성(중복이면 409). TempUser면 트랜잭션 | `HttpFirebaseClient.cs:124-150`, `routes/uploads.ts:48-80`, `services/uploads.ts:129-152,163-222` |

- 클라는 prepare 응답의 `bucket`으로 `HttpFirebaseClient.Bucket`을 갱신한 뒤 토큰 URL을 재조립하므로, 서버 `downloadUrl`과 동일한 URL이 나온다(`HttpFirebaseClient.cs:104-121`).
- `UploadFileAsync`는 Storage 경로에서 `(sessionId, kind)`를 역파싱한다 — `results/{sid}/…` 형식이 아니면 예외(`HttpFirebaseClient.cs:237-250`).
- `retentionHours`는 `(ExpiresAt - CreatedAt)`을 정수 시간으로 역산해 전달하며 최소 1시간으로 clamp된다(`HttpFirebaseClient.cs:127-128`).
- **Emulator 예외**: Storage Emulator는 V4 서명이 불가하므로(`client_email` 없음) `FIREBASE_STORAGE_EMULATOR_HOST`가 있으면 서명을 우회하고 Emulator 업로드 URL을 반환한다. 배포 환경엔 이 env가 없어 항상 서명 경로를 탄다(`services/signing.ts:22-41`).

### 5.4 QR 생성 및 실패 처리

- 업로드 성공 후에만 QR 노출: `QrService.GenerateQrPng(result.DownloadPageUrl, 12)`(`QrPopupViewModel.cs:94-97`).
- **TempUser 한도 초과**(403): `QrLimitExceededException`으로 잡아 사유별 문구를 노출한다 — 시간 초과 "무료 사용 시간이 지났습니다…", 횟수 소진 "무료 사용 횟수가 소진되었습니다…"(`QrPopupViewModel.cs:101-112`).
- **그 밖의 실패**: 예외를 삼키지 않고 `UploadFailed=true`, QR 숨김, 로컬 보존 안내. 결과물은 QR 분기 이전에 로컬 저장되어 손실 0이며 `[재시도]`를 제공한다(`QrPopupViewModel.cs:113-118`).

---

## 6. TempUser QR 한도 (it13)

과금 안전이 목적이므로 **서버가 진실원**이다. 앱 판정은 표시용이다.

| 지점 | 동작 | 근거 |
|------|------|------|
| prepare 선검사 | TempUser면 한도 초과 시 **서명 URL을 아예 내주지 않는다**(403) — 직접 PUT 과금 원천 차단 | `services/uploads.ts:70-73,104-115` |
| commit 재검사 | 트랜잭션으로 (중복 409 검사 → 한도 재판정 → 세션 생성 → `qrUsedCount +1`)을 원자화 | `services/uploads.ts:231-265` |
| 판정 규칙 | 시간(`createdAt + qrHours` 경과)과 횟수(`qrUsedCount >= qrCount`)를 **독립 OR**, 둘 다 초과면 `time` 우선. 경계는 `>=`(초과) | `domain/tempUserLimit.ts:44-58` |
| 전역 한도 기본값 | `qrHours=48`, `qrCount=30`(config 문서 부재 시 폴백) | `domain/tempUserLimit.ts:23-26` |
| 한도 조회·수정 | `GET`은 모든 로그인 사용자, `PATCH`는 admin만 | `routes/config.ts:24-60` |
| 사용량 조회 | `GET /accounts/me/qr-usage` → `{blocked, reason, remainingMs, remainingCount}` | `routes/accounts.ts:43-48`, `HttpQrUsageService.cs:52-63` |
| 클라 실패 정책 | 사용량 조회 실패는 **fail-open**(null 반환 → 셸이 허용). 과금 안전은 업로드 거부가 담보 | `HttpQrUsageService.cs:37-48` |

- "성공 세션 1회 = commit 최초 성공"이며, 동일 `sessionId` 재commit은 409로 **이중집계가 차단**된다. 카운트는 파일 개수와 무관하게 세션당 1이다(`services/uploads.ts:159-161,240-263`).
- 서버 403의 사유 코드(`TEMP_USER_TIME_EXCEEDED`/`TEMP_USER_COUNT_EXCEEDED`)는 클라에서 `QrLimitExceededException(Reason)`으로 매핑된다(`HttpFirebaseClient.cs:156-161`).

---

## 7. 프레임 (`HttpFrameRepository` ↔ `/frames`)

컬렉션 `frameTemplates`, Storage 규약 `frames/{owner}/{frameId}.png`(`services/frames.ts:2,105,192`).

| 메서드 | 호출 | 게이트 | 비고 |
|--------|------|--------|------|
| `GetDefaultFramesAsync` | `GET /frames/default` | API키 | 게스트도 조회 가능 |
| `GetUserFramesAsync(userId)` | `GET /frames?userId=` | Bearer | 본인만(파워는 임의 계정 조회 허용, `routes/frames.ts:51-53`) |
| `SaveAsync(frame, imageBytes)` | `POST /frames` → 서명 PUT | Bearer + 파워 | 2단계: 메타 POST로 `{frame, upload}` 수신 → 이미지 바이트를 서명 URL로 직접 PUT(`HttpFrameRepository.cs:74-91,129-167`) |
| `DeleteAsync(frameId)` | `DELETE /frames/{id}` | Bearer + 파워 | `{deleted:bool}` 반환 — 없는 문서 삭제를 성공으로 오인하지 않는다 |
| `DeleteAllByUserAsync` | — | — | **클라 no-op**. 계정 삭제(`DELETE /accounts/{id}`)와 함께 서버가 cascade 수행(`HttpFrameRepository.cs:117-126`, `services/frames.ts:201-211`) |

- 서버는 **공용 기본 프레임만 생성**한다: `userId=null`, `isDefault=true`를 강제하며 클라가 보낸 값을 신뢰하지 않는다(`routes/frames.ts:71-80`). 일반 사용자 커스텀 프레임은 it8 A2로 **로컬 전용**(`ILocalFrameStore`, 실행 폴더 `Frame\`)이다(`ServiceRegistration.cs:85-87`).
- `PUT /frames/{id}`(메타 갱신 + 선택적 이미지 교체)는 서버에 존재하지만 **앱 호출 코드는 0**이다 — it15 F1-D2/D3에서 "편집은 해당 PC에서만 적용"으로 정리됐다(`HttpFrameRepository.cs:99-100`).
- 프레임 저작 권한(누가 생성·편집·삭제할 수 있는가)은 [60 §1.2](./60-auth-accounts-and-roles.md)의 `CanWriteFrames()` 축이 담당한다. 서버의 `requirePower()`는 **공용 DB 프레임** 게이트이며 두 축은 별개다.

---

## 8. 계정 (`HttpAccountService` ↔ `/auth`·`/accounts`)

| 메서드 | 호출 | 특기사항 | 근거 |
|--------|------|----------|------|
| `LoginWithGoogleAsync(code, verifier, redirectUri, nonce?)` | `POST /auth/google` | 응답 `{token, expiresIn, user}` → `Session.SignIn`. **401 → `null`**(자격 실패 일반화), **501 → `GoogleSsoNotConfiguredException`** | `HttpAccountService.cs:35-73`, `routes/auth.ts:33-108` |
| `GetAllAsync` | `GET /accounts` | 403은 빈 배열 폴백 없이 예외 전파 | `HttpAccountService.cs:75-88` |
| `DeleteAsync(id)` | `DELETE /accounts/{id}` | 서버가 소유 프레임 cascade까지 수행. 자기 자신 삭제는 서버가 403 | `HttpAccountService.cs:90-103`, `routes/accounts.ts:100-103` |
| `SetRoleAsync(id, role)` | `PATCH /accounts/{id}/role` | `actingRole`은 서버가 JWT에서 도출(클라 전달값 무시) | `HttpAccountService.cs:105-118` |
| `VerifyPinAsync` | `POST /accounts/me/pin/verify` | 401 → `false`(불일치). 409(PIN 미설정)·기타는 예외 전파 → 게이트는 **fail-closed** | `HttpAccountService.cs:122-143` |
| `SetOwnPinAsync` | `PUT /accounts/me/pin` | 기존 PIN 있으면 `currentPin` 필수, 최초 설정이면 생략 | `HttpAccountService.cs:145-162` |
| `ResetPinAsync(targetId, newPin)` | `PUT /accounts/{id}/pin` | 파워 + `canResetPin`(대상이 **엄격히 낮은 위계**, 동급 403 — 매니저 PIN은 admin 전용) 필요. 본인 대상은 400 | `HttpAccountService.cs:164-179`, `routes/accounts.ts:124-151` |

- Google 로그인은 서버가 code를 교환하고 id_token을 검증한 뒤, 검증된 email로 계정을 **자동 생성(temp_user)/매핑**한다(`routes/auth.ts:86-95`). 검증 실패 사유는 로그에만 남기고 401로 일반화한다(계정 열거 방지).
- 자격증명 계약에 **비밀번호는 존재하지 않는다**(`HttpAccountService.cs:181`). 권한 매트릭스·PIN 게이트 흐름은 [60번](./60-auth-accounts-and-roles.md)이 단일 진실이다.

---

## 9. 예외·상태코드 매핑

`BackendException` → 도메인 예외(`HttpBackendClient.cs:189-196`):

| 상태 | 매핑 | UI 처리 |
|------|------|---------|
| 400 | `ArgumentException` | 입력 오류 안내 |
| 403 | `UnauthorizedAccessException` | 권한 없음 안내 |
| 404 | `InvalidOperationException` | 대상 없음 |
| 409 | `InvalidOperationException` | 중복(세션·PIN 미설정 등) |
| 401 | **호출부가 결정** — 로그인은 `null`, PIN 검증은 `false`, 그 외는 전파 | |
| 그 외·5xx | `InvalidOperationException` | |
| 네트워크·타임아웃 | `InvalidOperationException("백엔드에 연결할 수 없습니다.")` | §11 |
| 업로드 403(사유 코드) | `QrLimitExceededException(Time\|Count)` | 한도 문구(§5.4) |

---

## 10. 만료 정리 — 앱·서버 모두 미제공(인프라 담당)

| API | 현행 동작 | 근거 |
|-----|-----------|------|
| `IFirebaseClient.QueryExpiredSessionsAsync` | `NotSupportedException` | `HttpFirebaseClient.cs:168-171` |
| `IFirebaseClient.DeleteResultSessionAsync` | `NotSupportedException` | `HttpFirebaseClient.cs:173-176` |
| `IFirebaseClient.DeleteStoragePrefixAsync` | `NotSupportedException` | `HttpFirebaseClient.cs:163-166` |
| `IUploadService.PurgeExpiredAsync` | Core에 코드는 남아 있으나 **앱 런타임 호출부 0**(테스트만). HTTP 경로에서 호출하면 위 `NotSupportedException`에 걸린다 | `UploadService.cs:100-122`, `tests/MCPhoto.Tests/UploadServiceTests.cs:219` |

- 운영은 **인프라로 대체**한다: GCS Object Lifecycle(파일, `results/` age 3일) + Firestore 네이티브 TTL(문서, `expiresAt`). 상세는 [50번](./50-infra-gcp-lifecycle-and-ttl.md)·[40 §5](./40-database-firestore-and-storage-schema.md), 운영 절차는 `web/OPS-ttl.md`.
- 서버에도 만료 정리 엔드포인트는 없다(`app.ts:27-32`의 라우터 6종에 없음).

---

## 11. 백엔드 미도달 시 동작

과거의 "Firebase 초기화됨/미초기화" 축은 소멸했다. 현재 축은 **백엔드 도달 가능/불가**다.

| 상황 | 동작 | 근거 |
|------|------|------|
| `BackendBaseUrl`이 빈 값 | `IsInitialized=false` → 업로드 시도 자체가 예외("Firebase 미초기화 — 업로드 불가"). `BaseAddress` 미설정이라 상대 URL 조립 불가 | `ServiceRegistration.cs:106-108,126`, `UploadService.cs:32-33` |
| 네트워크 오류·타임아웃(100초) | `InvalidOperationException("백엔드에 연결할 수 없습니다.")` | `HttpBackendClient.cs:136-141` |
| 미로그인 상태에서 Bearer 필수 호출 | `UnauthorizedAccessException("로그인이 필요합니다(토큰 없음).")` | `HttpBackendClient.cs:109-113` |
| 업로드(게스트 포함) | 선택적 Bearer — 토큰 있으면 부착, 없으면 익명 통과. 실패는 QR 팝업이 우아 처리(로컬 보존) | `HttpFirebaseClient.cs:96,143`, `QrPopupViewModel.cs:113-118` |
| QR 사용량 조회 실패 | **fail-open**(허용) — 서버가 업로드에서 최종 거부 | `HttpQrUsageService.cs:37-48` |
| PIN 게이트 확인 불가 | **fail-closed** — 진입 거부 | [60 §4.5](./60-auth-accounts-and-roles.md#45-백엔드-미도달-시-동작-구-미초기화-폴백-재정의) |
| 로그인 | 오프라인에서는 어떤 계정으로도 로그인할 수 없다(인메모리 폴백 없음). **게스트 촬영·로컬 저장은 계속 동작** | 같은 문서 §4.5 |

> 로그 기반 진단 절차는 [70 §6 "백엔드 연결 실패 진단"](./70-logging-and-troubleshooting.md#6-백엔드-연결-실패-진단)에 정리돼 있다(2026-07-29 재작성 — 종전의 "Firebase 초기화 실패 진단"을 대체).

---

## 12. DI 등록 (`ServiceRegistration.RegisterBackendServices`)

| 등록 | 방식 | 근거 |
|------|------|------|
| `IHttpClientFactory` 명명 클라이언트 `"backend"` | `AddHttpClient` — `BackendBaseUrl`을 `BaseAddress`로, 타임아웃 100초. 빈 값이면 BaseAddress 미설정 | `ServiceRegistration.cs:103-109` |
| `BackendSessionSynchronizer` | Singleton. JWT 홀더를 **소유**하고 `IBackendSession`으로 노출 — 토큰이 존재할 수 있는 모든 시점에 로그아웃 구독이 살아 있게 보장 | `ServiceRegistration.cs:113-116` |
| `IFirebaseClient`→`HttpFirebaseClient` | Singleton. `BackendApiKey`·`StorageBucket` 주입, `configured = BackendBaseUrl 비어있지 않음` | `ServiceRegistration.cs:118-128` |
| `IFrameRepository`→`HttpFrameRepository` | Singleton | `ServiceRegistration.cs:130-138` |
| `IAccountService`→`HttpAccountService` | Singleton | `ServiceRegistration.cs:140-148` |
| `IQrUsageService`→`HttpQrUsageService` | Singleton | `ServiceRegistration.cs:151-159` |
| `ITempUserLimitsService`→`HttpTempUserLimitsService` | Singleton | `ServiceRegistration.cs:161-169` |
| `IUploadService`→`UploadService`, `IQrService`→`QrService` | Singleton(Core 구현) | `ServiceRegistration.cs:82-83` |
| `ILocalFrameStore`→`LocalFrameStore` | Singleton, 루트=`{BaseDirectory}\Frame` | `ServiceRegistration.cs:86-87` |

- 팩토리 람다는 첫 해석 시점에 `ISettingsService.Current`를 읽는다(설정 로드 후라 안전, `ServiceRegistration.cs:98`).
- **feature flag 분기는 없다** — it15에서 레거시 Admin SDK 직결 경로가 폐지되어 백엔드 전용이다(`ServiceRegistration.cs:80`).

---

## 관련 문서

- [40 · 데이터베이스(Firestore)/Storage 스키마](./40-database-firestore-and-storage-schema.md) — 컬렉션 필드·경로 규약·보안 규칙·TTL 상세
- [60 · 인증·계정·역할](./60-auth-accounts-and-roles.md) — 역할 위계·권한 매트릭스·PIN 게이트·백엔드 미도달 시 동작 상세
- [50 · 인프라(GCP) 보관/만료](./50-infra-gcp-lifecycle-and-ttl.md) — Lifecycle·TTL로 대체된 만료 정리
- 인덱스: [README](./README.md)
