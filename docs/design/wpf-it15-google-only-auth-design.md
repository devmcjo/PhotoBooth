# it15 설계 — Google SSO + PIN 전용 인증 / 레거시 Firebase 직결 제거

> 프로젝트 루트: `C:\STUDY\PROJECT\PhotoBooth`
> 입력(단일 진실): it15 요구사항 브리프(사용자 원문 지시 1~5 + 확정 결정 D1~D5)
> 선행 설계: `docs/design/wpf-it14-settings-pin-gate-design.md`(직접 선행), `wpf-it13-temp-user-role-design.md`,
> `wpf-google-sso-design.md`, `wpf-accounts-email-verification-design.md`, `wpf-auth-ux-and-account-rules-design.md`,
> `wpf-backend-proxy-migration-design.md`, `firebase-contract.md`
> **이 문서의 범위**: 지시 1·2·3·4(인증/계정/DB/진단). 지시 5(프레임 UX 2건)는 별도 설계
> `wpf-it15-frame-ux-design.md`가 담당하며 이 문서에서 다루지 않는다.

---

## §0. 개요

### 0.1 한 줄 정의

**ID/PW 기반 인증 자산(로그인·회원가입·이메일 인증·비밀번호 재설정·계정 생성·PW 초기화)을 전량 삭제**하고,
자격증명을 **① Google SSO 로그인**(신원) + **② 4자리 PIN**(설정/계정 관리 진입 게이트) 두 가지로 축소한다.
동시에 **레거시 Firebase 직결 경로(`MCPhoto.Firebase` + `serviceAccountKey.json` + `AppSettings.UseBackend`)를
전면 폐기**하여 앱을 **백엔드(HTTPS API) 전용**으로 단순화한다.

### 0.2 대상·기술 스택 (현행 유지 — 변경 없음)

| 항목 | 값 | 근거 |
|---|---|---|
| .NET | net8.0 / net8.0-windows(WPF) | `src/MCPhoto.App/MCPhoto.App.csproj:5` |
| MVVM | CommunityToolkit.Mvvm 8.3.2 | `MCPhoto.App.csproj:13` |
| DI | Microsoft.Extensions.DependencyInjection 8.0.1 | `MCPhoto.App.csproj:14` |
| 셸 유형 | 단일 창 + 뷰모델 교체(`AppShellViewModel.CurrentViewModel`) | `src/MCPhoto.App/AppShellViewModel.cs:57-58,239-256` |
| 서버 | Cloud Functions 2nd gen + Express + TypeScript | `web/functions/src/app.ts:17-31` |
| 서버 테스트 | jest + ts-jest | `web/functions/package.json` scripts.test |

### 0.3 무회귀 기준선 (실측 완료 — 이 수치를 하한으로 유지)

| 검증 | 기준선 | 판정 |
|---|---|---|
| `dotnet build -c Release` | **경고 0 / 오류 0** | 증가 시 FAIL |
| `dotnet test` | **675 / 675 통과** | 제거 기능 테스트 삭제분만큼 총수 감소 허용, **실패 0** 필수 |
| `web/functions` `npm test`(jest) | **206 / 206 통과 (15 suites)** | 동상 |
| `web/functions` `npm run typecheck` | 오류 0 | 증가 시 FAIL |

> 총 테스트 수는 감소한다(제거 기능 테스트 삭제). **감소분은 삭제 대상 테스트 파일/케이스와 1:1 대응**해야 하며,
> 남는 기능(PIN 게이트·역할 매트릭스·TempUser 한도·Google SSO)의 커버리지는 유지·보강한다(§9.3).

### 0.4 설계 원칙 5개 (이번 이터레이션 판단 기준)

1. **자격증명 단일화** — 신원은 Google이 증명(서버가 id_token 검증), 로컬 권한 승격은 PIN이 담당.
   앱은 어떤 형태의 비밀번호도 보관·전송·검증하지 않는다.
2. **fail-closed 유지** — PIN 게이트는 확인 불가(네트워크 오류·서비스 부재) 시 **진입 차단**.
   it14에서 확립된 규약을 그대로 승계한다(`AppShellViewModel.cs:376-402`).
3. **분기 제거 우선** — `UseBackend` 이중 경로가 사라지므로, "백엔드 모드에서만" 류의 게이트
   (`IsBackendMode`)는 조건이 항상 true가 되어 **프로퍼티째 삭제**한다(죽은 분기 잔존 금지).
4. **데드락 금지** — PIN이 유일한 게이트 자격증명이 되므로, PIN 미설정 계정이 어떤 경로로도
   PIN을 만들 수 없는 상태가 발생해선 안 된다(§6.4).
5. **서버가 최종 강제** — 클라 게이트는 UX 1차 방어. 역할·PIN·QR 한도는 모두 서버 재검증(현행 유지).

### 0.5 산출 요약 (§3 상세)

| 구분 | 삭제 파일 | 수정 파일 | 신규 파일 |
|---|---:|---:|---:|
| 클라이언트(C#/XAML) | 12 | 22 | 1 |
| 서버(TS/rules) | 4 | 9 | 2 |
| 문서 | 0 | 8 | 0 |
| **합계** | **16** | **39** | **3** |

---

## §1. 검증된 사실 (file:line 근거)

이 절의 모든 항목은 현행 코드를 직접 읽어 확인했다. 설계 판단의 전제다.

### 1.1 클라이언트 — 인증 계약

- **F1.** `IAccountService`는 20개 메서드를 가진 단일 인터페이스다.
  `src/MCPhoto.Core/Accounts/IAccountService.cs:11-116`.
  이 중 브리프 §3.1이 지목한 제거 대상 13개(`LoginAsync`:11, `VerifyPasswordAsync`:18, `RegisterAsync`:33,
  `CreateAsync`:41, `ChangePasswordAsync`:44, `EnsureSeedAccountAsync`:56, `SetEmailAsync`:64,
  `RequestPasswordResetAsync`:70, `ConfirmPasswordResetAsync`:73, `ConfirmPasswordResetByCodeAsync`:76,
  `RequestEmailVerificationAsync`:82, `ConfirmEmailVerificationAsync`:88,
  `ConfirmEmailVerificationByTokenAsync`:93)를 빼면 **7개**가 남는다
  (`LoginWithGoogleAsync`:29, `GetAllAsync`:47, `DeleteAsync`:50, `SetRoleAsync`:53,
  `VerifyPinAsync`:102, `SetOwnPinAsync`:109, `ResetPinAsync`:116).

- **F2.** `User` 모델은 `Password`(`Models/User.cs:25`)와 `EmailVerified`(:35)를 노출하고,
  `AuthMethod` enum은 `Password`/`Sso` 2값이다(:7-14). 기본값은 `AuthMethod.Password`(:38).

- **F3.** `AppShellViewModel.OpenSettings`는 `user.AuthMethod == AuthMethod.Sso` 여부로
  **PIN 게이트 / 비번 게이트를 분기**한다(`AppShellViewModel.cs:381-402`). 게스트(user is null)는 무가드(:373-377).
  `IPinPromptDialogService`/`IPasswordPromptDialogService`가 없으면 **진입 거부**(fail-closed, :385,399).

- **F4.** PIN 최초 설정 경로가 이미 존재한다: `pin.PromptSetup(async p => { await account.SetOwnPinAsync(uid, null, p); user.HasPin = true; })`
  (`AppShellViewModel.cs:388-392`). 즉 **"PIN 미설정 → 강제 생성" UX는 설정 진입에 이미 구현돼 있고**,
  계정 관리 진입(브리프 §3.1 신규 요구)은 이 블록을 **재사용**하면 된다.

- **F5.** `IPinPromptDialogService`는 `PromptVerify(Func<string,Task<bool>>)`와
  `PromptSetup(Func<string,Task>)` 2메서드다(`Services/IPinPromptDialogService.cs:19,26`).
  `UserMgmtViewModel.ResetUserPin`이 `PromptSetup`을 타 계정 재설정에 재사용한다(`UserMgmtViewModel.cs:163`).

- **F6.** `AccountViewModel`은 3모드(`PasswordChange`/`AccountCreate`/`Admin`)로 UI를 분기하며
  (`AccountViewModel.cs:16-26,47-57`), PIN 섹션은 `PasswordChange` 모드 하단에 있고
  노출 조건은 `IsBackendMode && AuthMethod == Sso`(:74-75)다.

- **F7.** `LoginGuestViewModel`은 로그인/회원가입 **탭 상태머신**(`AuthMode`, :11-17,44-53),
  self-signup 입력 5종(:58-71), 인라인 검증 파생 3개(:77-87), 커맨드 5개
  (`Login`:155, `SignUp`:189, `LoginWithGoogle`:225, `Cancel`:269, `ForgotPassword`:273)를 가진다.
  `OfflineSeedId = "devmcjo"` 상수는 :27.

- **F8.** Google 버튼 노출 게이트는 `UseBackend && !IsNullOrWhiteSpace(GoogleClientId)`
  (`LoginGuestViewModel.cs:106-108`). `GoogleClientId`는 **운영 기본값이 코드에 내장**돼 있어
  (`AppSettings.cs:159`) 실사용상 빈 값이 되지 않는다.

- **F9.** `UserMgmtViewModel`은 `ResetPassword = "0000"` 상수(:53)와 `ResetUserPassword` 커맨드(:127-143)를
  갖는다. 역할 변경(`ApplyRoleChange`:173)·삭제(`DeleteUser`:105)·PIN 재설정(`ResetUserPin`:151)은 유지 대상.
  `IsBackendMode`(:69)는 PIN 재설정 UI 노출 게이트로만 쓰인다.

### 1.2 클라이언트 — 레거시 Firebase 직결 경로

- **F10.** `MCPhoto.Firebase` 어셈블리는 6개 소스 파일이다: `AccountService.cs`(191줄),
  `FirebaseClient.cs`(235줄), `FrameRepository.cs`(228줄), `TempUserServices.cs`(27줄),
  `UploadService.cs`(123줄), `Dto/{UserDoc,FrameTemplateDoc,ResultSessionDoc}.cs`.

- **F11. ⚠️ 프로젝트를 통째로 삭제할 수 없다.** `UploadService`(`src/MCPhoto.Firebase/UploadService.cs:13`)는
  **`UseBackend`와 무관하게 무조건 등록**되는 유일한 `IUploadService` 구현이다
  (`ServiceRegistration.cs:86` — 팩토리 분기 밖). 의존성은 `IFirebaseClient` + `MCPhoto.Core` 타입 +
  `ILogger`뿐이며(`UploadService.cs:1-5,16-22`) **FirebaseAdmin/Google.Cloud 패키지를 전혀 참조하지 않는다**.
  → 삭제가 아니라 **`MCPhoto.Core`로 이동**해야 한다(§3.2 D-A).

- **F12.** `NullQrUsageService`/`NullTempUserLimitsService`(`TempUserServices.cs:11,22`)도
  FirebaseAdmin 무의존 순수 no-op이며, 프로덕션 DI에서는 `UseBackend=false` 분기 전용
  (`ServiceRegistration.cs:182,195`)이지만 **테스트가 직접 사용**한다
  (`tests/MCPhoto.Tests/AccountViewModelEmailTests.cs:114`).

- **F13.** FirebaseAdmin/Google.Cloud.Firestore/Google.Cloud.Storage.V1 패키지 참조는
  `src/MCPhoto.Firebase/MCPhoto.Firebase.csproj:10-12`에만 있다. 이 3개 패키지를 실제로 쓰는 파일은
  `FirebaseClient.cs`, `FrameRepository.cs`, `AccountService.cs`, `Dto/*.cs` **4종뿐**이다.

- **F14.** `serviceAccountKey.json` 탐색은 `FirebaseClient.KeyCandidatePaths()`
  (`FirebaseClient.cs:99-105`, 실행경로 단일 후보)에 격리돼 있고, 소비자는
  `DiagnosticsViewModel` 생성자(`DiagnosticsViewModel.cs:39-42`) 1곳 + 테스트 2곳
  (`FirebaseClientTests.cs:17`, `AccountTests.cs:37`)이다.

- **F15.** `IFirebaseClient`(`src/MCPhoto.Core/Upload/IFirebaseClient.cs`)는 **Core에 있으므로 유지**된다.
  HTTP 구현 `HttpFirebaseClient`(`src/MCPhoto.Http/HttpFirebaseClient.cs:30`)가 이미 전 계약을 구현하며,
  `IsInitialized`는 "base URL 설정됨"(구성 사실)을 의미한다(:52). `Bucket`은 생성자 주입값(:45).

- **F16.** `AppSettings.UseBackend`의 **기본값은 이미 `true`**(`AppSettings.cs:138`)이고,
  `BackendBaseUrl`의 운영 기본값도 내장(:144)이다. `NormalizeBackend()`는 base URL이 비면
  `UseBackend=false`로 되돌린다(:201-205). 즉 **현재 기본 실행 경로는 이미 백엔드 전용**이며,
  레거시 경로는 ini에서 `BackendBaseUrl`을 명시적으로 비워야만 활성화된다.
  → **오프라인 폴백이 아니다.** D1 제거의 런타임 회귀 위험은 §4.4에서 별도 판정.

- **F17.** `.csproj` 참조: `MCPhoto.App.csproj:25`, `MCPhoto.Tests.csproj:25`.
  `.sln` 프로젝트 등록: `MCPhoto.sln`의 `MCPhoto.Firebase` GUID `{1B84FC9D-810C-4A2C-A6B3-79AEA65D6C75}`
  (Project 선언 1줄 + `ProjectConfigurationPlatforms` 12줄 + `NestedProjects` 1줄).

- **F18.** `serviceAccountKey.json` 관련 배포 자산: `.gitignore:41`(무시 규칙),
  `installer/MCPhoto.iss:39`(`Excludes` 목록에 포함), `publish.ps1:8`(주석 "Backend-only (no serviceAccountKey.json)").
  → **publish.ps1·installer는 이미 키를 포함하지 않는다**(제외 규칙만 존재). 삭제 시 파급 없음.

### 1.3 서버 (Cloud Functions)

- **F19.** 라우터 마운트는 6개: `/auth`, `/accounts`, `/config`, `/frames`, `/uploads`, `/health`
  (`web/functions/src/app.ts:26-31`).

- **F20.** `authRouter`의 6개 라우트 중 **5개가 제거 대상**이다(`routes/auth.ts`):
  `POST /login`(:51), `POST /register`(:85), `POST /verify-email/request`(:203),
  `POST /verify-email/confirm`(:217), `POST /password-reset/request`(:259),
  `POST /password-reset/confirm`(:273). **유지는 `POST /google`(:123) 하나뿐.**

- **F21.** `accountsRouter`의 9개 라우트 중 **3개가 제거 대상**이다(`routes/accounts.ts`):
  `POST /`(생성, :37), `PATCH /:id/password`(:133), `PATCH /:id/email`(:148).
  유지 6개: `GET /`(:72), `GET /me/qr-usage`(:82), `POST /me/pin/verify`(:93),
  `PUT /me/pin`(:112), `DELETE /:id`(:162), `PATCH /:id/role`(:178), `PUT /:id/pin`(:194) — 실제 7개.

- **F22.** `UserDoc` 현행 필드(`services/dto.ts:13-38`): `id`, `password`(필수), `role`, `createdAt`,
  `email?`, `emailVerified?`, `qrUsedCount?`, `authMethod?:"sso"|"password"`, `pinHash?`.
  `UserResponse`(:100-112): `id`, `role`, `createdAt`, `email`, `emailVerified`, `authMethod`, `hasPin`.

- **F23.** `toResponse()`(`services/accounts.ts:45-56`)가 `authMethod: doc.authMethod === "sso" ? "sso" : "password"`로
  폴백하고 `hasPin: typeof doc.pinHash === "string"`을 파생한다.

- **F24.** `createGoogleAccount`(`services/accounts.ts:436-461`)는 신규 SSO 계정을
  **`role:"user"`, `authMethod:"sso"`, `emailVerified:true`, `password: sentinel 해시`**로 만든다(:443-451).
  id는 `deriveAccountId(email, exists)`로 email local-part에서 파생하며
  충돌 시 `-2`,`-3`… suffix를 붙인다(`domain/accountId.ts:70-89`).
  → **devmcjo@gmail.com으로 SSO 가입한 계정 id가 `devmcjo-2`인 것은 `devmcjo`(password 계정)가
  선점돼 있었기 때문**이다. D3 마이그레이션의 근본 원인이 여기 있다.

- **F25.** `loginWithGoogleEmail`(:390-411)은 email 필드로만 계정을 찾고(`findByEmailField`:374),
  없으면 자동 생성, 있으면 `loginExistingGoogleAccount`(:414)로 `emailVerified=true` 승격 후 로그인한다.
  **기존 계정 경로는 `role`·`authMethod`를 건드리지 않는다**(:423-425 주석).

- **F26.** `canSetRole`(`domain/roles.ts:117-138`)은 **admin 지정을 누구에게도 허용하지 않고**(:119),
  **admin 대상 변경도 금지**(:120)한다. → devmcjo를 admin으로 만드는 것은 **HTTP API로 불가능**하며,
  마이그레이션 스크립트(firebase-admin 직결)로만 가능하다. D3가 스크립트여야 하는 이유.

- **F27.** 이메일 인프라 자산: `services/email.ts`(발송), `services/tokens.ts`(토큰 CRUD),
  `domain/tokens.ts`(TTL 상수·만료 판정), `config.ts`의 `emailProvider`/`emailFrom`/`sendgridApiKey`(:9,23-27,97-107).
  `buildLink()`(`services/accounts.ts:62-66`)는 `hostingBaseUrl`을 쓴다.
  `hostingBaseUrl`은 **이메일 링크 전용은 아니다** — 삭제 전 다른 소비자 확인 필요(§3.3 S-7).

- **F28.** `firestore.rules`는 `users`를 전면 차단(`allow read, write: if false`)한다.
  주석에 "평문 pw 보호"라는 **사실과 어긋난 근거**가 남아 있다(bcrypt 해시로 이미 전환됨).
  필드명 참조는 없으므로 **규칙 로직 변경은 불필요, 주석만 갱신**한다.
  `storage.rules`도 필드 참조 0 → **변경 없음**.

- **F29.** `web/functions/scripts/`에는 `post-deploy-smoke.mjs`, `set-secrets.sh`가 이미 있다.
  → 마이그레이션 스크립트를 `.mjs`로 두는 것은 기존 관례와 정합.

### 1.4 테스트·인코딩

- **F30.** `IAccountService` fake 구현은 **테스트 5개 파일**에 있다(it14 계약 메모리 확인,
  `.claude/agent-memory/wpf-developer/it14-pin-gate-contract.md:16`):
  `AccountViewModelTempUserTests.cs`, `UserMgmtViewModelTests.cs`, `AccountViewModelEmailTests.cs`,
  `PasswordResetViewModelTests.cs`, `LoginGuestViewModelTests.cs`.
  **인터페이스 축소 시 5곳 전부 동반 수정**(제거 메서드의 스텁 삭제)이 필요하다.

- **F31.** `MCPhoto.Firebase` 직접 참조 테스트 6개:
  `AccountTests.cs:5`, `FirebaseClientTests.cs:2`, `UploadServiceTests.cs:4`,
  `UploadContractTests.cs:5`, `Http/BackendDiFlagTests.cs:6`, `Http/HttpFirebaseClientTests.cs:10`,
  `AccountViewModelEmailTests.cs:114`.

- **F32.** `UseBackend` 라운드트립·Clamp 테스트: `Http/BackendSettingsTests.cs`(89줄, 6케이스 중 5개가
  `UseBackend` 직접 검증), `Http/BackendDiFlagTests.cs`(96줄, 전부 분기 검증), `SettingsTests.cs`(345줄,
  라운드트립에 `UseBackend` 포함 — grep상 직접 단언은 없으나 `IniSettingsService.cs:162,201`이
  읽기/쓰기하므로 키 제거 시 **파일 포맷 변경**).

- **F33.** 소스 인코딩 규약: `.cs`는 **UTF-8 without BOM**(한글 주석 존재).
  프로젝트 메모리 `.claude/agent-memory/wpf-architect/source-file-encoding.md` 확정 사항.
  → 수정·신규 파일 모두 BOM 없이 저장한다.

- **F34.** `MCPhoto.sln`의 `MCPhoto.Firebase` 관련 줄: 선언 `:14`, 구성 `:66-77`(12줄),
  중첩 `:110`. 총 **14줄 삭제**.

- **F35.** `index.ts:22`가 `SENDGRID_API_KEY`를 `defineSecret`으로 선언하고 `:41`의 `secrets:[]`에 포함한다.
  선언된 시크릿은 **배포 시 존재해야 하므로**, 제거하면 오히려 배포 전제조건이 1개 줄어든다(개선).

- **F36.** `hostingBaseUrl`은 이메일 링크(`services/accounts.ts:63`) 외에
  **`domain/session.ts:81 downloadPageUrl`**(QR 다운로드 페이지 URL 조립)에서도 쓰인다.
  → **config의 `hostingBaseUrl`은 유지**, `buildLink()`만 삭제한다.

- **F37.** `services/email.ts`·`services/tokens.ts`·`domain/tokens.ts`의 **유일한 프로덕션 소비자는
  `services/accounts.ts`의 이메일 인증/재설정 함수들**이다(email:32,75,628 / tokens:28,34).
  이 함수들이 삭제되면 3개 모듈은 전부 고아가 된다 → 삭제 가능.

---

## §2. 미검증 가정 (open assumptions)

| # | 가정 | 위험 | 검증 단계 |
|---|---|---|---|
| A1 | `MCPhoto.Firebase` 삭제 후 `MCPhoto.App`이 FirebaseAdmin/Google.Cloud 패키지를 **전혀** 필요로 하지 않는다(전이 의존 0). | 빌드 실패 | **C-Step 2** (`dotnet build -c Release` 경고 0) |
| A2 | `UploadService`를 `MCPhoto.Core`로 옮겨도 네임스페이스 변경(`MCPhoto.Firebase` → `MCPhoto.Core.Upload`) 외 코드 수정이 불필요하다. | 컴파일 오류 | **C-Step 2** |
| A3 | `IniSettingsService`에서 `UseBackend` 키를 제거해도 **기존 배포본의 ini에 남아 있는 `UseBackend=` 줄**이 로드 실패를 유발하지 않는다(`IniFile`은 미지정 키를 무시). | 런타임 설정 로드 실패 | **C-Step 3** (`SettingsTests` 라운드트립 + 레거시 ini 픽스처 테스트 신규) |
| A4 | 서버에서 `POST /auth/login` 제거 후, **현재 배포된 구버전 클라이언트가 없다**(앱은 단일 배포·자동 업데이트 없음 → 구버전 잔존 가능). | 구버전 앱이 404를 받고 로그인 불가 | **S-Step 1** 결정 반영: 라우트 제거 대신 **410 Gone + 안내 메시지** 유지 여부는 §5.3에서 판정 |
| A5 | Firestore `users` 컬렉션의 실제 문서 수가 스크립트 배치 한도(500 write/batch)를 넘지 않는다. | 마이그레이션 부분 실패 | **D-Step 1** (dry-run 출력의 문서 수 확인) |
| A6 | `devmcjo-2` 계정이 실제로 존재하고 `email == "devmcjo@gmail.com"`이다. | 마이그레이션 no-op 또는 오작동 | **D-Step 1** (dry-run이 대상 문서를 찾지 못하면 명시적 에러) |
| A7 | 프레임 `ownerId` 참조 필드명이 `frameTemplates.userId`다(`services/dto.ts:80`). 다른 컬렉션에 소유자 참조가 없다. | 고아 참조 잔존 | **D-Step 1** (dry-run이 전 컬렉션 스캔 결과를 출력) |
| A8 | ~~가정~~ **확인됨**: `XamlResourceTests.cs:228`이 `PasswordResetView.xaml`을 `InlineData`로 검증하고 `:264` 주석이 `PasswordPromptWindow`를 참조한다. 남은 미지: 삭제 시 다른 케이스가 연쇄 실패하지 않는지. | 테스트 컴파일/실행 실패 | **C-Step 7** (`dotnet test`) |

> A4는 설계 판단이 필요한 유일한 항목이다. §5.3에서 결론을 낸다.

---

## §3. 제거 범위 확정 (파일 단위)

### 3.1 클라이언트 — 삭제할 파일 (12개)

| # | 파일 | 사유 |
|---|---|---|
| 1 | `src/MCPhoto.App/Views/PasswordResetView.xaml` | 비밀번호 찾기 폐지(브리프 §3.1) |
| 2 | `src/MCPhoto.App/Views/PasswordResetView.xaml.cs` | 동상 |
| 3 | `src/MCPhoto.App/ViewModels/PasswordResetViewModel.cs` | 동상 |
| 4 | `src/MCPhoto.App/Views/PasswordPromptWindow.xaml` | 설정 진입 비번 게이트 폐지 → PIN 단일 경로 |
| 5 | `src/MCPhoto.App/Views/PasswordPromptWindow.xaml.cs` | 동상 |
| 6 | `src/MCPhoto.App/Services/PasswordPromptDialogService.cs` | 동상 |
| 7 | `src/MCPhoto.App/Services/IPasswordPromptDialogService.cs` | 동상 |
| 8 | `src/MCPhoto.Firebase/AccountService.cs` | D1: 레거시 직결 계정 서비스 |
| 9 | `src/MCPhoto.Firebase/FirebaseClient.cs` | D1: Admin SDK 직결 + `serviceAccountKey.json` 탐색(F14) |
| 10 | `src/MCPhoto.Firebase/FrameRepository.cs` | D1: 레거시 직결 프레임 저장소 |
| 11 | `src/MCPhoto.Firebase/Dto/{UserDoc,FrameTemplateDoc,ResultSessionDoc}.cs` (3파일) | D1: Firestore 어트리뷰트 DTO(Google.Cloud.Firestore 전용) |
| 12 | `src/MCPhoto.Firebase/MCPhoto.Firebase.csproj` + 프로젝트 폴더 | 잔여 0 → 프로젝트 제거 |

**삭제 테스트 파일(5개, 위 12개와 별도 집계):**
`tests/MCPhoto.Tests/PasswordResetViewModelTests.cs`, `AccountTests.cs`(레거시 `AccountService` 전용),
`FirebaseClientTests.cs`(키 후보 경로 전용), `Http/BackendDiFlagTests.cs`(`UseBackend` 분기 전용),
`AccountViewModelEmailTests.cs`(이메일 인증 섹션 전용 — PIN 관련 케이스는 `AccountViewModelPinTests.cs`로 이관).

### 3.2 클라이언트 — 이동/신규 (D-A: `UploadService` 이관)

| 결정 | 내용 | 근거 |
|---|---|---|
| **D-A** | `src/MCPhoto.Firebase/UploadService.cs` → **`src/MCPhoto.Core/Upload/UploadService.cs`** 로 이동. `namespace MCPhoto.Firebase` → `namespace MCPhoto.Core.Upload`. 클래스 본문 무변경. | F11(무조건 등록·Core 타입만 의존), Core에 `Microsoft.Extensions.Logging.Abstractions` 이미 있음(`MCPhoto.Core.csproj:9`) |
| **D-B** | `src/MCPhoto.Firebase/TempUserServices.cs`의 `NullQrUsageService`/`NullTempUserLimitsService`는 **프로덕션에서 삭제**하고, 테스트가 쓰던 자리는 **테스트 로컬 fake**로 대체한다(`tests/MCPhoto.Tests/Fakes/NullTempUserLimitsService.cs` 신규 1파일). | F12. 프로덕션 DI에 죽은 no-op 구현을 남기면 원칙 3 위반 |

> **D-B 근거 보강**: no-op을 Core에 남기면 "백엔드 미도달 시 조용히 무제한 허용"이라는
> 잘못된 폴백이 부활할 수 있다. TempUser 한도는 과금 방어이므로 fail-open 경로를 코드에서 없앤다.

### 3.3 클라이언트 — 수정할 파일 (22개, 줄범위 명시)

| # | 파일 | 수정 내용 | 현행 줄범위 |
|---|---|---|---|
| C1 | `src/MCPhoto.Core/Accounts/IAccountService.cs` | 13개 메서드 제거 → 7개 유지(§5.1) | `:10-56`, `:58-93` 삭제 |
| C2 | `src/MCPhoto.Core/Models/User.cs` | `Password`·`EmailVerified` 삭제, `AuthMethod` enum 재정의(§5.2) | `:7-14`, `:24-25`, `:34-35` |
| C3 | `src/MCPhoto.Core/Settings/AppSettings.cs` | `UseBackend` 프로퍼티·`Clone` 항목·`NormalizeBackend` 강제 off 삭제 | `:132-138`, `:201-205`, `:270` |
| C4 | `src/MCPhoto.Core/Settings/IniSettingsService.cs` | `UseBackend` 읽기/쓰기 삭제 | `:162`, `:201` |
| C5 | `src/MCPhoto.Core/Navigation/AppState.cs` | `PasswordReset` 열거값 삭제 | `:45-46` |
| C6 | `src/MCPhoto.App/ServiceRegistration.cs` | `RegisterBackendOrFirebase` → `RegisterBackendServices`로 축소(분기 전면 제거, §7.1) | `:14`, `:42-43`, `:83-86`, `:99-203`, `:221-222` |
| C7 | `src/MCPhoto.App/AppShellViewModel.cs` | `OpenSettings` 분기 제거 → PIN 단일 경로(§6.2), `OpenPasswordReset`·`OpenAccountCreate` 삭제, `AccountMode` 축소 반영 | `:368-405`, `:417-426`, `:440-442`, `:254` |
| C8 | `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs` | 탭·self-signup·id/pw 로그인 전면 제거 → Google 단독(§6.1) | 전면 개편(275줄 → 약 110줄) |
| C9 | `src/MCPhoto.App/Views/LoginGuestView.xaml` | 동상 | 전면 개편 |
| C10 | `src/MCPhoto.App/Views/LoginGuestView.xaml.cs` | PasswordBox 코드비하인드 전량 삭제 | `:1-104` 대부분 |
| C11 | `src/MCPhoto.App/ViewModels/AccountViewModel.cs` | 모드 축소·비번/이메일 섹션 삭제·PIN 강제 생성 추가(§6.3) | `:16-26`, `:59-63`, `:91-133`, `:213-244`, `:317-363`, `:413-533`, `:569-622` |
| C12 | `src/MCPhoto.App/Views/AccountView.xaml` | 동상 | 전면 개편 |
| C13 | `src/MCPhoto.App/Views/AccountView.xaml.cs` | 삭제된 PasswordBox 핸들러 정리 | `:1-55` 일부 |
| C14 | `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs` | `ResetPassword` 상수·`ResetUserPassword` 삭제, `IsBackendMode` 삭제 | `:53`, `:69`, `:126-143`, `:44`, `:95` |
| C15 | `src/MCPhoto.App/Views/UserMgmtView.xaml` | "PW 초기화" 버튼 삭제, PIN 재설정 게이트 단순화 | 해당 버튼 블록 |
| C16 | `src/MCPhoto.App/ViewModels/DiagnosticsViewModel.cs` | `FirebaseKeyCandidate` 레코드·프로퍼티·생성자 초기화 삭제, Firebase 섹션 재정의(§6.5) | `:10`, `:38-42`, `:67-73`, `:111-115` |
| C17 | `src/MCPhoto.App/Views/DiagnosticsWindow.xaml` | "서비스 계정 키 탐색 경로" 블록 삭제, 라벨 재정의 | `:127-170` 중 `:162-170` |
| C18 | `src/MCPhoto.App/MainWindow.xaml` | 계정 팝오버: "비밀번호 변경"→"계정 관리", "계정 생성" 항목 삭제 | `:62-69` |
| C19 | `src/MCPhoto.App/MCPhoto.App.csproj` | `MCPhoto.Firebase` ProjectReference 삭제 | `:25` |
| C20 | `tests/MCPhoto.Tests/MCPhoto.Tests.csproj` | 동상 | `:25` |
| C21 | `MCPhoto.sln` | `MCPhoto.Firebase` 14줄 삭제(F34) | `:14`, `:66-77`, `:110` |
| C22 | 잔여 테스트 8개(§9.2 표) | fake 축소·`UseBackend` 참조 제거 | 개별 명시 |

### 3.4 서버 — 삭제/수정 (삭제 4 / 수정 9 / 신규 2)

**삭제할 파일 (4개)**

| # | 파일 | 사유 |
|---|---|---|
| S-D1 | `web/functions/src/services/email.ts` | 유일 소비자가 이메일 인증/재설정(F37) |
| S-D2 | `web/functions/src/services/tokens.ts` | 동상 |
| S-D3 | `web/functions/src/domain/tokens.ts` | 동상 |
| S-D4 | `web/functions/src/__tests__/{email,tokens}.test.ts` (2파일) | 대상 모듈 삭제 |

**수정할 파일 (9개)**

| # | 파일 | 수정 내용 | 현행 줄범위 |
|---|---|---|---|
| S1 | `src/routes/auth.ts` | 5라우트 삭제(F20), `/google`만 유지. import 정리 | `:51-117`, `:200-305`; import `:10-33` |
| S2 | `src/routes/accounts.ts` | 3라우트 삭제(F21). `validatePassword`·`validateEmail` import 제거 | `:37-69`, `:132-159` |
| S3 | `src/services/accounts.ts` | `login`·`createAccount`·`changePassword`·`registerSelf`·`setEmail`·이메일/재설정 함수 전량 삭제. `toResponse` 재정의. `createGoogleAccount` → `temp_user`+`authMethod:"google"`+sentinel 제거 | `:98-119`, `:121-182`, `:197-215`, `:338-678` 중 다수, `:44-56`, `:436-461` |
| S4 | `src/services/dto.ts` | `UserDoc`·`UserResponse` 스키마 축소, `TokenDoc` 삭제 | `:13-38`, `:51-75`, `:99-112` |
| S5 | `src/config.ts` | `EmailProvider`·`emailProvider`·`emailFrom`·`sendgridApiKey` + 강제 검증 삭제. `hostingBaseUrl`은 **유지**(F36) | `:9`, `:21-27`, `:50-52`, `:97-107`, `:135-137` |
| S6 | `src/index.ts` | `SENDGRID_API_KEY` `defineSecret` 및 `secrets:[]` 항목 삭제(F35) | `:22`, `:41` |
| S7 | `src/domain/validation.ts` | `validatePassword`·`validateVerificationCode` 삭제(소비자 0 확인 후). `validatePin` 유지 | 해당 함수 |
| S8 | `src/domain/password.ts` | `verifyPassword`(평문 레거시 마이그레이션) 삭제, `hashPassword`/`verifyHash`는 **PIN에서 계속 사용** → 유지. 파일명은 유지(리네임 시 diff 폭증) | `verifyPassword` 함수 |
| S9 | `web/firestore.rules` | `users` 주석의 "평문 pw 보호" 근거를 "계정 문서 전면 차단(PIN 해시·역할 보호)"으로 갱신. **규칙 로직 무변경**(F28) | 주석부 |

> `web/storage.rules`는 **변경 없음**(F28 — 필드 참조 0).

**신규 파일 (2개)**

| # | 파일 | 내용 |
|---|---|---|
| S-N1 | `web/functions/scripts/migrate-google-only-accounts.mjs` | D3·D4 일회성 마이그레이션(§8) |
| S-N2 | `web/functions/src/__tests__/googleOnlyAccounts.test.ts` | `createGoogleAccount`가 `temp_user`+`"google"`로 생성하는지, `toResponse`가 새 스키마를 내는지 |

### 3.5 문서 동기화 (8개, 수정)

`docs/analysis/60-auth-accounts-and-roles.md`, `docs/analysis/40-database-firestore-and-storage-schema.md`,
`docs/analysis/11-exe-app-features.md`, `docs/analysis/12-exe-app-settings-and-config.md`,
`docs/analysis/90-roadmap-and-future-work.md`, `docs/USER-ACTIONS.md`(마이그레이션 실행 절차 + SENDGRID 시크릿 불요 안내),
`docs/design/firebase-contract.md`(UserDoc 스키마), `README.md:35`(프로젝트 목록에서 `MCPhoto.Firebase` 제거).

---

## §4. D1 파급 정밀 판정 (레거시 Firebase 직결 제거)

### 4.1 `MCPhoto.Firebase` 프로젝트 통째 삭제 — **가능. 단 선행 이관 1건 필수**

소비자 추적 결과(F10~F13, F31):

| 타입 | 프로덕션 소비자 | 판정 |
|---|---|---|
| `FirebaseClient` | `ServiceRegistration.cs:122-128`(팩토리), `DiagnosticsViewModel.cs:40`(`KeyCandidatePaths`) | 둘 다 삭제 대상 → **삭제 가능** |
| `FrameRepository` | `ServiceRegistration.cs:150-152` | 분기 삭제 → **삭제 가능** |
| `AccountService` | `ServiceRegistration.cs:165-168` | 분기 삭제 → **삭제 가능** |
| `UploadService` | `ServiceRegistration.cs:86` — **분기 밖, 무조건 등록** | ⚠️ **이관 필요(D-A)** |
| `NullQrUsageService`/`NullTempUserLimitsService` | `ServiceRegistration.cs:182,195` | 분기 삭제 → **삭제 가능**(테스트 fake로 대체, D-B) |
| `Dto/*` | `FirebaseClient`/`FrameRepository`/`AccountService` 내부 전용 | **삭제 가능** |

**결론**: `UploadService`를 `MCPhoto.Core.Upload`로 옮긴 뒤 프로젝트를 제거하면
FirebaseAdmin(3.1.0)·Google.Cloud.Firestore(3.9.0)·Google.Cloud.Storage.V1(4.10.0) 3개 NuGet이
솔루션에서 완전히 사라진다. **publish 산출물 크기·시작 시간 개선이 부수 효과**로 따라온다.

### 4.2 `IFirebaseClient` ↔ `HttpFirebaseClient` — 인터페이스는 유지

`IFirebaseClient`는 `MCPhoto.Core/Upload/`에 있고(F15) `HttpFirebaseClient`가 전 계약을 구현한다.
`UploadService`(이관 후 Core)는 이 인터페이스에만 의존하므로 **계약 변경 0**이다.

**다만 이름이 오해를 부른다**("Firebase"라는 이름이지만 실제로는 백엔드 업로드 게이트웨이).
이번 이터레이션에서 **리네임하지 않는다** — 근거:
- 리네임은 `IFirebaseClient`/`HttpFirebaseClient`/`FakeFirebaseClient`/`HttpFirebaseClientTests` 등
  10+ 파일에 걸친 순수 기계적 diff를 만들어, **인증 제거라는 본 변경의 리뷰 신호를 희석**한다.
- 기능적 이득 0. 별도 정리 이터레이션에서 처리하도록 `docs/design/backlog-post-backend-migration.md`에 항목만 추가한다.

### 4.3 `AppSettings.UseBackend` 제거 — 설정 라운드트립 영향

| 영향 대상 | 현행 | it15 후 |
|---|---|---|
| `AppSettings.UseBackend` 프로퍼티 | `:138` 기본 true | **삭제** |
| `NormalizeBackend()` 강제 off | `:201-205` `UseBackend=false; return;` | **`return;`만 남김**(base URL 빈 값이면 슬래시 보정 스킵) |
| `Clone()` | `:270` | **항목 삭제** |
| `IniSettingsService` Load/Save | `:162`, `:201` | **삭제** → ini에서 `UseBackend=` 키가 사라짐 |
| `SettingsTests`(345줄) | 라운드트립에 간접 포함 | **직접 단언 없음** → 컴파일 영향 0, 다만 **레거시 ini 호환 테스트 1건 신규**(A3) |
| `Http/BackendSettingsTests.cs`(89줄) | 6케이스 중 5개가 `UseBackend` 단언 | **케이스 재작성**: `NormalizeBackend`의 트림·슬래시 보정만 검증(3케이스로 축소) |
| `Http/BackendDiFlagTests.cs`(96줄) | 전부 분기 검증 | **파일 삭제**(§3.1) — 분기 자체가 사라짐 |

**A3 대응(레거시 ini 호환)**: `IniFile`은 키-값 사전 기반이라 **모르는 키를 무시**한다.
기존 배포본 ini에 `UseBackend=True`가 남아 있어도 Load는 그 키를 읽지 않고, Save 시 자동으로 제거된다.
이를 회귀 테스트로 못박는다 — `SettingsTests`에 **"레거시 키가 포함된 ini를 Load→Save해도 예외 없이
나머지 값이 보존된다"** 케이스 1개 추가.

### 4.4 오프라인 회귀 위험 — **회귀 없음. 근거와 잔존 동작 명세**

브리프가 우려한 "UseBackend=false가 폴백이었다면" 전제는 **성립하지 않는다**(F16):

1. `UseBackend` 기본값이 이미 `true`이고 `BackendBaseUrl` 운영 기본값이 내장돼 있다(`AppSettings.cs:138,144`).
   레거시 경로는 **운영자가 ini에서 `BackendBaseUrl`을 명시적으로 비워야만** 켜진다.
2. 레거시 경로가 켜지더라도 `serviceAccountKey.json`이 실행 폴더에 있어야 동작하는데,
   `publish.ps1`·installer는 **키를 배포하지 않는다**(F18). 즉 **현장 배포본에서 레거시 경로는 이미 죽어 있다**.
3. 따라서 D1 제거는 **동작 중인 폴백을 없애는 것이 아니라, 죽은 코드를 치우는 것**이다.

**백엔드 미도달 시 잔존 동작(제거 후에도 동일 — 이 표를 무회귀 기준으로 삼는다)**

| 기능 | 백엔드 미도달 시 동작 | 근거 |
|---|---|---|
| 프레임 목록 | 로컬 `Frame\` 폴더(번들 + 파워 캐시 + user)로 폴백 | `LocalFrameStore`(`ServiceRegistration.cs:90-91`), `FrameCatalogService` |
| 게스트 촬영 | **가능**(로그인 불요, 카메라·합성·로컬 저장은 전부 로컬) | `AppShellViewModel.OpenSettings`가 게스트 무가드(:373), 촬영 파이프라인은 서버 무의존 |
| 로컬 저장 | 정상(`LocalSaveService`) | `ServiceRegistration.cs:73` |
| QR 전송 | 실패 → 상위가 예외 처리(QR off·로컬 저장 안내) | `UploadService`가 `IsInitialized` false면 `InvalidOperationException`(`UploadService.cs:32-33`) |
| Google 로그인 | 실패 → "네트워크를 확인해 주세요" 인라인 오류 | `LoginGuestViewModel:259-264` |
| 설정 진입(로그인 상태) | PIN 검증 호출 실패 → **fail-closed(진입 차단)** | `IPinPromptDialogService` 계약(§6.2) |

> ⚠️ **의도적 동작 변경 1건**: 제거 전에는 `HttpFirebaseClient.IsInitialized`가 "base URL 설정됨"이라
> 백엔드 미도달이어도 true였다. 제거 후에도 동일하다(구현 무변경). **변경 없음**을 명시해 둔다.

---

## §5. 인증·계정 계약 재정의

### 5.1 `IAccountService` 최종 시그니처 (7메서드)

```csharp
namespace MCPhoto.Core.Accounts;

using MCPhoto.Core.Models;

/// <summary>
/// 계정 조회/역할/PIN. 인증은 Google SSO 단일 경로. 백엔드(HTTPS API) 전용 — 로컬 자격증명 없음.
/// (it15 설계 §5.1)
/// </summary>
public interface IAccountService
{
    // ── 인증(Google SSO 단일 경로) ──

    /// <summary>
    /// Google SSO 로그인. 브라우저 loopback으로 받은 authorization code(+PKCE verifier·redirectUri·nonce)를
    /// 백엔드 POST /auth/google로 전달해 code 교환·id_token 검증 후, 검증된 email로 계정을
    /// 자동 생성(temp_user)/매핑하고 JWT를 받는다. 성공 시 세션에 토큰·사용자를 저장하고 User 반환.
    /// Google 검증 실패(도메인·미검증 등)는 서버 401 → null.
    /// 서버가 SSO 미구성(501)이면 GoogleSsoNotConfiguredException.
    /// </summary>
    Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default);

    // ── 계정 관리(power) ──

    /// <summary>전체 계정 목록(power 전용 사용자 관리).</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>계정 삭제 + 소유 프레임 cascade 삭제.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>역할 변경(it13 매트릭스, 서버 최종 강제).</summary>
    Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default);

    // ── PIN(설정·계정 관리 진입 게이트, it14) ──

    /// <summary>
    /// 진입 게이트: 본인 PIN 대조(E1). 일치 true, 불일치 false.
    /// PIN 미설정(409)·네트워크/서버 오류는 예외로 전파(게이트는 "확인 불가"=차단 — fail-open 금지).
    /// </summary>
    Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default);

    /// <summary>
    /// 본인 PIN 설정/변경(E2). 기존 PIN 있으면 currentPin 확인 필수(불일치는 예외),
    /// null이면 최초 설정. 성공 시 정상 반환, 실패는 예외.
    /// </summary>
    Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default);

    /// <summary>
    /// 타 계정 PIN 재설정(E3, 권한 기반, 대상 현재 PIN 불요).
    /// 위계 위반(서버 403)은 UnauthorizedAccessException.
    /// </summary>
    Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default);
}
```

**변경 요약**: 20 → 7 메서드. 제거 13개는 F1 목록과 동일.

**동반 수정 필수 — fake 구현 5곳 (F30)**

| # | 파일 | 조치 |
|---|---|---|
| 1 | `tests/MCPhoto.Tests/AccountViewModelTempUserTests.cs` | fake에서 제거 메서드 13개 스텁 삭제 |
| 2 | `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs` | 동상 + `ChangePasswordAsync` 호출 검증 케이스 삭제(PW 초기화 폐지) |
| 3 | `tests/MCPhoto.Tests/AccountViewModelEmailTests.cs` | **파일 삭제**(§3.1). PIN 관련 케이스만 `AccountViewModelPinTests.cs`로 이관 |
| 4 | `tests/MCPhoto.Tests/PasswordResetViewModelTests.cs` | **파일 삭제**(§3.1) |
| 5 | `tests/MCPhoto.Tests/LoginGuestViewModelTests.cs` | fake 축소 + id/pw·회원가입·비번찾기 케이스 삭제, Google 케이스 유지·보강 |
| +6 | `tests/MCPhoto.Tests/AccountViewModelPinTests.cs` | (기존 fake 보유) 동일 축소 + PIN 강제 생성 케이스 신규(§6.3) |

> **6곳**이다 — it14 메모리의 "5곳"은 `AccountViewModelPinTests.cs` 생성 이전 시점 기록이다.
> 구현자는 `grep -rn "IAccountService" tests/`로 최종 확인할 것.

### 5.2 `User` 모델 · `AuthMethod` enum 최종 형태

```csharp
namespace MCPhoto.Core.Models;

/// <summary>
/// 계정 인증 방식(D2). DB 저장값은 소문자 provider 문자열("google"), UI 표기는 "Google SSO".
/// 추후 Kakao/Apple 추가 시 enum 값 + 매핑 1줄씩만 늘린다.
/// </summary>
public enum AuthMethod
{
    /// <summary>Google SSO. 현재 유일한 인증 수단. 서버 authMethod="google".</summary>
    Google,

    /// <summary>서버가 미지원/미설정 값을 보낸 경우의 폴백. UI는 "알 수 없음"으로 표기.</summary>
    Unknown
}

/// <summary>계정. 자격증명(비밀번호)은 보관하지 않는다 — 신원은 Google, 게이트는 PIN. (it15 §5.2)</summary>
public sealed class User
{
    public string Id { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.TempUser;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Google 계정 이메일(소문자 정규화). SSO 신원의 근거이므로 항상 존재한다.</summary>
    public string? Email { get; set; }

    /// <summary>인증 방식(D2). 서버 authMethod 파생, 기본 Google.</summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Google;

    /// <summary>진입 PIN 설정 여부(서버 pinHash!=null 파생). false면 최초 설정 강제.</summary>
    public bool HasPin { get; set; }
}
```

**결정 근거**

| 항목 | 결정 | 근거 |
|---|---|---|
| `Password` 삭제 | 앱이 비밀번호를 다루지 않음 | 브리프 §3.1 |
| `EmailVerified` 삭제 | Google이 `email_verified`를 강제 검증(`services/googleAuth.ts`) → 항상 true. 필드는 잉여 | 브리프 지시 3 |
| `AuthMethod` 2값 유지 | `Unknown` 폴백 없이 `Google` 단일 값이면, 서버가 `"kakao"`를 보내기 시작했을 때 클라가 **조용히 Google로 오인**한다. 폴백 값을 남겨 오인을 UI에 드러낸다 | 원칙 5(서버 권위) |
| `Role` 기본값 `TempUser` | 신규 SSO 계정이 `temp_user`로 생성되므로(§5.4) 클라 기본값도 최소 권한으로 정렬 | 브리프 지시 2 |
| `Id` 유지 | 사용자 원문 "id는 필요함(동일한 아이디이지만 뒷 주소가 다를 수 있으므로)" — `deriveAccountId`가 email local-part 기반이라 `a@gmail.com`/`a@corp.com`이 `a`/`a-2`로 분기(F24) | 지시 3 |

**UI 표기 매핑(D2)** — 단일 소스로 확장 메서드 1개를 둔다:

```csharp
// src/MCPhoto.Core/Models/User.cs 하단
public static class AuthMethodExtensions
{
    /// <summary>서버 저장 문자열 → enum. 미지원값은 Unknown(조용한 오인 방지).</summary>
    public static AuthMethod ParseAuthMethod(string? value) =>
        value == "google" ? AuthMethod.Google : AuthMethod.Unknown;

    /// <summary>UI·진단 표기 라벨(D2: DB "google" ↔ 화면 "Google SSO").</summary>
    public static string ToLabel(this AuthMethod m) => m switch
    {
        AuthMethod.Google => "Google SSO",
        _ => "알 수 없음"
    };
}
```

### 5.3 서버 — `UserDoc` / `UserResponse` 최종 스키마

```ts
/** users/{id} — it15: 비밀번호 개념 폐지. 자격증명은 Google(신원) + pinHash(게이트) 뿐. */
export interface UserDoc {
  id: string;
  role: string;                 // "temp_user" | "user" | "manager" | "admin"
  createdAt: Timestamp;
  /** Google 계정 이메일(소문자 정규화). SSO 신원의 근거 — 항상 존재. */
  email: string;
  /** 인증 제공자(D2). 현재 "google" 고정. 추후 "kakao"|"apple" 확장. */
  authMethod: string;
  /** 진입 PIN의 bcrypt 해시. 미설정 시 필드 부재. 응답에 절대 미포함. */
  pinHash?: string | null;
  /** it13: TempUser QR 전송 성공 세션 누적 수. 미설정=0. */
  qrUsedCount?: number;
}

/** 클라 응답용 User(해시 절대 미포함). */
export interface UserResponse {
  id: string;
  role: string;
  createdAt: string;   // ISO8601
  email: string | null;
  authMethod: string;  // "google"
  hasPin: boolean;     // pinHash != null 파생
}
```

**삭제 필드**: `password`(필수→폐지), `emailVerified`, `TokenDoc` 인터페이스 전체.
**`email`을 optional → required로 승격**: 모든 계정이 Google 유래이므로 email 없는 계정은 존재할 수 없다.
D4 마이그레이션이 email 없는 계정을 삭제하므로(§8) 스키마 승격이 성립한다.

**`toResponse()` 재정의**

```ts
function toResponse(doc: UserDoc): UserResponse {
  return {
    id: doc.id,
    role: parseRole(doc.role),
    createdAt: doc.createdAt.toDate().toISOString(),
    email: doc.email ?? null,
    // D2: 저장값 그대로 노출(클라가 파싱). 미설정 레거시는 "google" 폴백
    // — 마이그레이션 후에는 도달 불가하나 방어값으로 남긴다.
    authMethod: typeof doc.authMethod === "string" && doc.authMethod.length > 0
      ? doc.authMethod
      : "google",
    hasPin: typeof doc.pinHash === "string",
  };
}
```

### 5.4 라우트 표 (제거 / 유지)

| 메서드 | 경로 | 게이트 | it15 | 근거 |
|---|---|---|---|---|
| POST | `/auth/login` | API키 | ❌ **제거** | 지시 1 |
| POST | `/auth/register` | API키 | ❌ **제거** | 지시 1(회원가입 폐지) |
| POST | `/auth/verify-email/request` | API키 | ❌ **제거** | 지시 1 |
| POST | `/auth/verify-email/confirm` | API키 | ❌ **제거** | 지시 1 |
| POST | `/auth/password-reset/request` | API키 | ❌ **제거** | 지시 1 |
| POST | `/auth/password-reset/confirm` | API키 | ❌ **제거** | 지시 1 |
| **POST** | **`/auth/google`** | API키 | ✅ **유지**(동작 변경: 신규=temp_user) | §5.5 |
| POST | `/accounts` | Bearer+power | ❌ **제거** | 지시 1(계정 생성 폐지) |
| PATCH | `/accounts/:id/password` | Bearer | ❌ **제거** | 지시 1(PW 초기화 폐지) |
| PATCH | `/accounts/:id/email` | Bearer | ❌ **제거** | 지시 1(이메일 인증 폐지) |
| **GET** | **`/accounts`** | Bearer+power | ✅ 유지 | 사용자 관리 목록 |
| **DELETE** | **`/accounts/:id`** | Bearer+power | ✅ 유지 | 계정 삭제(cascade) |
| **PATCH** | **`/accounts/:id/role`** | Bearer+power | ✅ 유지 | 역할 승격/강등(it13) |
| **GET** | **`/accounts/me/qr-usage`** | Bearer | ✅ 유지 | TempUser 한도(it13) |
| **POST** | **`/accounts/me/pin/verify`** | Bearer | ✅ 유지 | PIN E1 |
| **PUT** | **`/accounts/me/pin`** | Bearer | ✅ 유지 | PIN E2 |
| **PUT** | **`/accounts/:id/pin`** | Bearer | ✅ 유지 | PIN E3 |
| — | `/config`, `/frames`, `/uploads`, `/health` | — | ✅ 유지(무변경) | — |

**A4 판정 — 제거 라우트를 410 Gone으로 남길 것인가: 아니오, 완전 제거한다.**

근거 3가지:
1. 앱은 **키오스크 단일 배포**이며, 서버·클라를 같은 릴리스로 함께 배포한다(`publish.ps1` + `web/deploy-web.bat`).
   구버전 앱이 남아 있어도 그 앱은 이미 UI에 ID/PW 창을 띄우므로, 410을 받아 "구버전입니다"를 표시할
   수신부가 없다(현행 클라는 401을 "아이디/비밀번호가 올바르지 않습니다"로 표시한다 — `LoginGuestViewModel.cs:169`).
2. 라우트를 남기면 `app.ts` 404 핸들러 대신 별도 스텁 6개를 유지해야 하고, 이는 **제거의 목적(공격 표면 축소)을 해친다**.
3. 운영자가 구버전 앱을 발견하면 `publish.ps1` 재배포가 정답이며, 이는 `docs/USER-ACTIONS.md`에 안내한다.

→ **미매칭 경로는 `app.ts:33-35`의 기존 404 핸들러가 처리**한다(추가 코드 0).

### 5.5 `createGoogleAccount` → `temp_user` 전환의 파급

**변경 후 함수(핵심부)**

```ts
async function createGoogleAccount(normalized: string): Promise<LoginResult | null> {
  const id = await deriveAccountId(normalized, async (candidate) =>
    (await db().collection(COLLECTION).doc(candidate).get()).exists);

  const doc: UserDoc = {
    id,
    role: "temp_user",        // it15: 신규 SSO 계정은 무조건 최소 권한. 승격은 관리자가 수행.
    createdAt: Timestamp.now(),
    email: normalized,
    authMethod: "google",     // D2
  };
  try { await db().collection(COLLECTION).doc(id).create(doc); }
  catch { return null; }       // 경합 — 호출측 재조회
  return { id: doc.id, role: "temp_user", user: toResponse(doc) };
}
```

**파급 판정 4건**

| # | 쟁점 | 판정 | 근거 |
|---|---|---|---|
| P1 | **최초 관리자 부트스트랩** — 전원이 temp_user면 승격시킬 admin이 없다 | **D3 마이그레이션이 부트스트랩을 겸한다.** 스크립트가 `devmcjo`를 `role:"admin"`으로 재생성하므로, 마이그레이션 실행 후에는 항상 admin이 1명 존재한다. 서버 코드에 부트스트랩 로직을 넣지 않는다 | F26(HTTP로 admin 지정 불가), §8 |
| P2 | **TempUser QR 한도(it13)가 신규 로그인 전원에 적용되는가** | **그렇다. 그것이 의도다.** 브리프 지시 2가 명시적으로 "최초 생성된 계정(Google)은 무조건 TempUser". it13 한도(기본 48h/30회)는 미승격 계정의 과금 방어로 정확히 이 목적에 부합한다 | 브리프 지시 2, `IQrUsageService` 계약 |
| P3 | **기존 계정 SSO 재로그인은 강등되는가** | **아니다.** `loginExistingGoogleAccount`는 role을 건드리지 않는다(F25). 승격된 계정은 재로그인해도 등급 유지 | `services/accounts.ts:414-429` |
| P4 | **승격 동선** | `UserMgmtView` → 역할 콤보 + [적용]. `canSetRole`상 **admin은 temp_user→user/manager 승격 가능**, manager는 user→temp_user 강등만 가능(승격 불가) | `domain/roles.ts:117-138`, `RoleChangePolicy.AssignableRoles` |

> **P4 보완 필요 여부 검토**: manager가 신규 temp_user를 user로 승격할 수 없으므로,
> admin 부재 시 신규 가입자가 영영 temp_user로 남는다. 이는 it13에서 **의도적으로 확정된 매트릭스**이며
> (승격=admin 전용), it15에서 변경하지 않는다. 운영상 admin 1인이 항상 존재해야 함을
> `docs/USER-ACTIONS.md`에 운영 전제로 명시한다.

**`loginExistingGoogleAccount` 정리**: `emailVerified=true` 승격 로직(`:422-426`)은 필드가 사라지므로 삭제.
남는 것은 email 정규화 대조(`:419`)와 응답 조립뿐 → **DB write 없는 순수 읽기 경로가 된다**(성능 부수 개선).

**`makeSentinelPasswordHash` 삭제**: `password` 필드 폐지로 sentinel 개념 자체가 소멸(`:366-368`).
`randomBytes` import도 제거.

### 5.6 PIN이 유일한 게이트 자격증명이 되는 보안 함의

| 항목 | 결정 | 근거 |
|---|---|---|
| **형식** | 4자리 숫자 `^\d{4}$` 유지 | D5 확정. `validatePin`(`domain/validation.ts`) 무변경 |
| **저장** | bcrypt 해시(`pinHash`), 원문·해시 모두 응답 미노출(`hasPin`만 파생) | 현행 유지(`services/accounts.ts:270,291`) |
| **fail-closed** | 검증 불가(네트워크/서비스 부재) 시 **게이트 차단** | it14 확립. `AppShellViewModel.cs:385,399` 패턴 승계 |
| **재시도 정책** | ⚠️ **서버 잠금 없음(현행)**. 4자리 = 10,000 조합이므로 온라인 브루트포스가 이론상 가능 | `verifyPin`(`:238-245`)에 시도 카운터 없음 |
| **재시도 대응(it15 결정)** | **서버 잠금은 이번 범위 밖. 대신 클라 측 완화 2건을 추가**: ① `PinPromptWindow`에서 **연속 5회 실패 시 창 자동 닫힘**(게이트 미통과), ② 실패 시 **1.5초 입력 비활성**(rate limit). 서버 잠금은 백로그로 이관 | 브리프에 잠금 요구 없음 + 물리적 키오스크(공격자가 화면 앞에 서 있어야 함) + 서버 잠금은 DoS(타인 계정 락아웃) 위험을 새로 도입 |
| **PIN 미설정 데드락 방지** | §6.4 참조 — 3개 경로 모두에서 최초 설정 강제 | 원칙 4 |
| **PIN 분실 복구** | `UserMgmtView`의 PIN 재설정(E3, 권한 기반) — admin/manager가 하위 계정 PIN 재설정 | `UserMgmtViewModel.cs:151-166` 유지 |
| **admin PIN 분실** | ⚠️ **복구 경로 없음**(자기 자신 E3는 서버 400, `routes/accounts.ts:203-205`). admin은 설정 화면에 영구 진입 불가 상태가 될 수 있다 | **운영 리스크로 문서화**: `docs/USER-ACTIONS.md`에 "admin PIN 분실 시 마이그레이션 스크립트와 동일한 firebase-admin 경로로 `pinHash` 필드 삭제" 절차를 기재. 스크립트 `--clear-pin <id>` 옵션 제공(§8.3) |

> **왜 서버 잠금을 넣지 않는가(명시적 근거)**: 계정 단위 잠금은 "타인의 계정을 5회 오입력해 잠그는"
> DoS를 만든다. 키오스크 1대에 admin 1명 환경에서는 이 DoS가 **PIN 브루트포스보다 현실적 위협**이다.
> IP 단위 rate limit은 Cloud Functions 앞단(Cloud Armor)이 적절한 계층이며 앱 코드 범위가 아니다.

---

## §6. 화면 설계

### 6.1 로그인 화면 (`AppState.Login`)

**레이아웃 (세로 스택, 중앙 정렬)**

```
┌─────────────────────────────────────┐
│              [로그인]                │   Text.Heading
│                                     │
│   촬영은 로그인 없이도 가능합니다.      │   Text.Caption (게스트 안내)
│   로그인하면 나만의 프레임을 쓸 수      │
│   있어요.                            │
│                                     │
│   ┌───────────────────────────────┐ │
│   │  [G]  Google로 로그인          │ │   Button.Primary (단독 CTA)
│   └───────────────────────────────┘ │
│                                     │
│   (오류 메시지 인라인)                 │   Text.Error, ErrorMessage != ""
│                                     │
│              [닫기]                  │   Button.Ghost → CancelCommand
└─────────────────────────────────────┘
```

**VM 최종 표면 (`LoginGuestViewModel`, 275줄 → 약 110줄)**

| 멤버 | 종류 | 비고 |
|---|---|---|
| `ErrorMessage` | `[ObservableProperty] string` | 유지 |
| `IsBusy` | `[ObservableProperty] bool` | 유지(재진입 가드 + 버튼 비활성) |
| `IsGoogleSignInAvailable` | `bool` (파생) | **`UseBackend` 조건 삭제** → `!string.IsNullOrWhiteSpace(GoogleClientId)`만. 기본값 내장이므로 실질 상시 true(F8) |
| `LoginWithGoogleCommand` | `[RelayCommand]` | 유지(본문 무변경, `LoginGuestViewModel.cs:225-266`) |
| `CancelCommand` | `[RelayCommand]` | 유지 |

**삭제 멤버**: `AuthMode` enum, `Mode`/`IsSignIn`/`IsSignUp`, `LoginId`/`Password`,
`SignUpId`/`SignUpEmail`/`SignUpPassword`/`SignUpPasswordConfirm`/`SignUpNotice`,
`PasswordsMatch`/`CanSubmitSignUp`/`PasswordRuleText`, `RefreshSignUpValidation()`,
`SwitchModeCommand`/`LoginCommand`/`SignUpCommand`/`ForgotPasswordCommand`,
`OfflineSeedId`/`MinPasswordLength` 상수, `IFirebaseClient` 주입 + `IsServerOffline`, `IsBackendMode`.

> **`IsServerOffline` 배너 삭제 근거**: 이 배너는 "서비스 계정 키 없음 → Firebase 미초기화"를
> 알리는 레거시 신호였다(`LoginGuestViewModel.cs:89-93`). 백엔드 전용에서는
> `HttpFirebaseClient.IsInitialized`가 "base URL 설정됨"만 뜻해(F15) 항상 true → **의미 없는 배너**.
> 실제 도달 실패는 로그인 시도 시 인라인 오류로 이미 안내된다(`:259-264`).

**상태 3종**

| 상태 | 조건 | 화면 |
|---|---|---|
| **정상** | `IsGoogleSignInAvailable && !IsBusy` | Google 버튼 활성 |
| **로딩** | `IsBusy` | 버튼 비활성 + "로그인 중..." 캡션. 브라우저 왕복 중 재클릭 차단 |
| **오류** | `ErrorMessage != ""` | 버튼 아래 인라인(취소/미허용계정/미구성/네트워크 4분기 — 현행 문구 유지) |
| **미구성** | `!IsGoogleSignInAvailable` | 버튼 숨김 + "로그인이 구성되지 않았습니다. 관리자에게 문의하세요." 안내. **[닫기]는 항상 노출**(게스트 촬영 복귀 보장) |

**코드비하인드**: `LoginGuestView.xaml.cs`는 **PasswordBox 전달 로직뿐**이므로 전량 삭제 →
빈 partial 클래스(`InitializeComponent()`만)로 축소.

### 6.2 설정 진입 게이트 — PIN 단일 경로

`AppShellViewModel.OpenSettings`(`:368-405`)를 아래로 대체한다.

```csharp
/// <summary>우상단 설정 버튼 → 설정 페이지(오버레이 진입). 로그인 사용자는 PIN 게이트 통과 필수.</summary>
[RelayCommand]
private async Task OpenSettings()
{
    IsAccountPopupOpen = false;
    // 게스트는 무가드(현행 유지). 로그인 사용자는 PIN 게이트 — 취소/불일치면 진입하지 않음.
    if (_session.CurrentUser is { } user && !await EnsurePinGateAsync(user))
        return;
    await NavigateToOverlayAsync(AppState.Settings);
}

/// <summary>
/// PIN 게이트 공통(it15 §6.2). HasPin이면 확인, 아니면 최초 설정 강제(데드락 방지).
/// 계정 서비스·다이얼로그 서비스 미등록은 fail-closed(진입 거부) — it14 규약 승계.
/// 설정 진입·계정 관리 진입 두 곳이 이 메서드를 공유한다(동일 PIN·동일 다이얼로그).
/// </summary>
public Task<bool> EnsurePinGateAsync(Core.Models.User user)
{
    var account = _services.GetService<IAccountService>();
    var pin = _services.GetService<Services.IPinPromptDialogService>();
    if (account is null || pin is null) return Task.FromResult(false); // fail-closed

    var uid = user.Id;
    bool ok = user.HasPin
        ? pin.PromptVerify(p => account.VerifyPinAsync(uid, p))
        : pin.PromptSetup(async p =>
          {
              await account.SetOwnPinAsync(uid, null, p);
              user.HasPin = true;   // 세션 로컬 반영(재진입 시 확인 경로로 전환)
          });
    return Task.FromResult(ok);
}
```

**변경점**: `AuthMethod == Sso` 분기(`:381`)와 비번 게이트 `else` 블록(`:396-402`)이 사라진다.
`IPasswordPromptDialogService` 참조 제거로 §3.1의 파일 4개가 고아가 된다.

> `EnsurePinGateAsync`가 `Task<bool>`인데 내부가 동기인 이유: `IPinPromptDialogService`는
> `ShowDialog()` 기반이라 동기 반환이다(`IPinPromptDialogService.cs:19,26`). 호출부가 `await` 문맥이므로
> 시그니처를 Task로 두어 향후 비동기 다이얼로그 전환 시 호출부 변경이 없게 한다.

### 6.3 "계정 관리" 화면 (구 `AccountView`)

**`AccountMode` 축소**: `PasswordChange`/`AccountCreate`/`Admin` 3값 → **`Account`/`Admin` 2값**.

```csharp
public enum AccountMode
{
    /// <summary>계정 관리(본인 정보 + PIN 변경).</summary>
    Account,
    /// <summary>관리자 도구(사용자 관리 진입·전역 한도·앱 종료, power).</summary>
    Admin
}
```

`Title`: `Account => "계정 관리"`, `Admin => "관리자"`.

**섹션 구성 (Account 모드)**

| 섹션 | 내용 | 노출 조건 |
|---|---|---|
| ① 내 계정 정보 | 아이디(`Id`), 이메일(`Email`), 로그인 방식(`AuthMethod.ToLabel()` = "Google SSO"), 역할(`Role.ToLabel()`), 가입일(`CreatedAt`) — **모두 읽기 전용 TextBlock** | 항상 |
| ② PIN 변경 | 라벨 **"PIN 변경"**(D: "설정 진입 PIN 변경" → 축약). 현재 PIN / 새 PIN / 새 PIN 확인 3개 `PasswordBox` + [변경] 버튼 + 인라인 메시지 | 항상(`CanChangePin` 삭제 — 모든 계정이 SSO) |
| ③ 닫기 | [닫기] → `ReturnFromOverlay()` | 항상 |

**삭제 섹션**: 비밀번호 변경(`AccountViewModel.cs:215-244`, XAML 대응 블록),
이메일 등록/인증 + 5분 카운트다운(`:100-133`, `:413-533`, `:569-622`), 계정 생성(`:91-98`, `:317-363`).

**Admin 모드 유지 섹션**: 사용자 관리 진입(`OpenUserManagement`), 전역 TempUser 한도(it13 §7.7),
앱 종료(`ExitApp`). `CanEditTempUserLimits`에서 `&& IsBackendMode` 삭제 → `Role == Admin`만.

**⭐ 신규: 진입 시 PIN 미설정이면 PIN 생성 강제**

```csharp
public override async Task OnEnterAsync()
{
    var user = _shell.Session.CurrentUser;

    // it15: 계정 관리 진입 게이트 — PIN 미설정이면 최초 설정을 강제한다.
    // 설정 진입과 "동일 PIN·동일 다이얼로그"(AppShellViewModel.EnsurePinGateAsync 재사용).
    // 취소(false) 시 이 화면에 머물지 않고 직전 화면으로 되돌린다(빈 화면 노출 방지).
    if (user is not null && !user.HasPin)
    {
        if (!await _shell.EnsurePinGateAsync(user))
        {
            await _shell.ReturnFromOverlay();
            return;
        }
    }
    // ... 기존 진입 로직(모드별 프로퍼티 통지, Admin 한도 로드)
}
```

**설계 판단 3건**

| 쟁점 | 결정 | 근거 |
|---|---|---|
| **HasPin=true면 진입 시 PIN을 다시 묻는가** | **묻지 않는다.** 계정 관리 진입은 **PIN 생성 강제만** 수행하고, 이미 PIN이 있으면 그대로 통과 | 브리프 원문: "PIN 번호 최초 입력이 되지 않았다면 계정관리 창 진입시에도 PIN 번호 생성 요구" — **최초 입력 미완 조건부**. 매 진입 확인은 요구되지 않았고, 계정 관리는 설정보다 파괴력이 낮다(읽기 + 본인 PIN 변경뿐) |
| **동일 PIN·동일 다이얼로그 재사용 방법** | `AppShellViewModel.EnsurePinGateAsync`를 `public`으로 노출하고 `AccountViewModel.OnEnterAsync`가 호출 | 서버는 `pinHash` 1개만 저장(§5.3) → 물리적으로 동일 PIN. 다이얼로그는 `IPinPromptDialogService.PromptSetup` 동일 인스턴스 |
| **취소 시 동작** | 계정 관리 화면에 머물지 않고 **`ReturnFromOverlay()`로 직전 화면 복귀** | PIN 없이 머물면 ②섹션의 "현재 PIN" 입력란이 무의미하게 노출되어 혼란. 취소=진입 포기로 해석 |

> ⚠️ **`OnEnterAsync` 내 다이얼로그 호출 주의**: `NavigateInternalAsync`(`AppShellViewModel.cs:206-210`)가
> `OnEnterAsync`를 await하는 도중 모달 `ShowDialog()`가 뜬다. WPF에서 이는 중첩 디스패처 루프를 만들지만
> **`CurrentViewModel`은 이미 세팅된 뒤**(:203)라 바인딩은 안정적이다. `ReturnFromOverlay()`가
> `OnEnterAsync` 완료 전에 재진입 네비게이션을 시작하지 않도록, 위 코드처럼 **`return`으로 즉시 종료**한다.
> 구현자는 이 순서를 반드시 지킬 것(뒤에 코드를 이어 붙이면 이중 네비게이션).

**계정 팝오버(`MainWindow.xaml:62-73`) 변경**

| 현행 | it15 |
|---|---|
| "비밀번호 변경" → `OpenPasswordChangeCommand` | **"계정 관리"** → `OpenAccountCommand`(리네임) |
| "계정 생성" → `OpenAccountCreateCommand` (power) | **삭제** |
| "관리자 도구" → `OpenAdminToolsCommand` (power) | 유지 |
| "로그아웃" | 유지 |

### 6.4 PIN 데드락 방지 — 전 경로 점검 (원칙 4)

| 경로 | HasPin=false일 때 | 결과 |
|---|---|---|
| 설정 진입(`OpenSettings`) | `PromptSetup` 강제 설정 | ✅ 생성 가능 |
| 계정 관리 진입(`AccountViewModel.OnEnterAsync`) | `EnsurePinGateAsync` 강제 설정 | ✅ 생성 가능(신규) |
| 계정 관리 ② PIN 변경 섹션 | `HasPin=false`면 "현재 PIN" 입력란 숨김 → `SetOwnPinAsync(id, null, newPin)` | ✅ 생성 가능 |
| 관리자가 타 계정 PIN 부여 | `UserMgmtView` PIN 재설정(E3) — 대상 현재 PIN 불요 | ✅ 부여 가능 |

**데드락 없음**을 확인. 단 **admin 본인 PIN 분실**은 앱 내 복구 불가(§5.6) → 스크립트 옵션으로 대응.

### 6.5 사용자 관리 화면 (`UserMgmtView`)

**남는 것**

| 열/액션 | 유지 | 비고 |
|---|---|---|
| 아이디 | ✅ | |
| 이메일 | ✅ | Google 이메일 |
| 역할 | ✅ | 콤보(`AssignableRoles`) + [적용] — it13 매트릭스 |
| 가입일 | ✅ | |
| **PIN 설정 여부** | ⭐ **신규 열** | `HasPin` → "설정됨"/"미설정". PIN이 유일 자격증명이 되었으므로 관리자 가시성 필요 |
| [PIN 재설정] | ✅ | `CanResetPin` 게이트에서 `isBackend` 조건 삭제 → `!isSelf && actorRole.CanManage(target)` |
| [삭제] | ✅ | cascade |
| [PW 초기화] | ❌ **삭제** | 지시 1 |

`UserRowViewModel` 생성자에서 `bool isBackend = false` 파라미터 삭제(`UserMgmtViewModel.cs:38,44`),
`ReloadAsync`의 `isBackend: isBackend` 인자 삭제(`:93,95`), `IsBackendMode` 프로퍼티 삭제(`:69`).

### 6.6 진단 창 (`DiagnosticsWindow`) 재구성

**삭제**: "서비스 계정 키 탐색 경로" 섹션 전체 — XAML `DiagnosticsWindow.xaml:162-170`,
VM `FirebaseKeyCandidates` 프로퍼티(`:73`) + 생성자 초기화(`:38-42`) + `FirebaseKeyCandidate` 레코드(`:111-115`)
+ `using MCPhoto.Firebase`(`:10`) + 대응 테스트 케이스(`DiagnosticsViewModelTests.cs`).

**Firebase 섹션 → "서버 연결" 섹션으로 재정의**

| 현행 항목 | it15 | 값 |
|---|---|---|
| "Firebase 초기화" (`FirebaseInitialized`) | **"백엔드 구성"**(`IsBackendConfigured`) | `IFirebaseClient.IsInitialized` = base URL 설정됨 |
| "버킷" (`FirebaseBucket`) | **"스토리지 버킷"** 유지 | `IFirebaseClient.Bucket` |
| — | ⭐ **"백엔드 주소"** 신규 | `ISettingsService.Current.BackendBaseUrl`(공개값) |
| — | ⭐ **"백엔드 키 내장"** 신규 | `!string.IsNullOrEmpty(BackendApiKey)` → "설정됨"/"미설정". **키 값 자체는 절대 표시하지 않는다** |
| — | ⭐ **"로그인 계정"** 신규 | 로그인 시 `{Id} · {AuthMethod.ToLabel()} · {Role.ToLabel()} · PIN {설정됨/미설정}`, 게스트면 "게스트" |
| "서비스 계정 키 탐색 경로" 목록 | ❌ 삭제 | — |

> **"백엔드 도달 확인" 버튼을 넣지 않는 이유**: `HttpFirebaseClient.ProbeReachableAsync`가 이미
> 존재하지만(`HttpFirebaseClient.cs:56`), `DiagnosticsViewModel`은 `IFirebaseClient`(인터페이스)에만
> 의존하며 이 메서드는 인터페이스에 없다. 진단을 위해 인터페이스를 넓히는 것은
> 원칙 3(분기·표면 제거)에 역행 → **백로그로 이관**.

**섹션 제목**: "Firebase(서버 연결)"(`DiagnosticsWindow.xaml:130`) → **"서버 연결(백엔드)"**.

---

## §7. DI 등록 최종 형태

### 7.1 `ServiceRegistration.RegisterBackendOrFirebase` → `RegisterBackendServices`

분기 팩토리 5개(`:131-202`)가 전부 무조건 등록으로 단순화된다.

```csharp
/// <summary>
/// 백엔드 HTTPS API 서비스 등록(it15: UseBackend 분기 폐지 — 백엔드 전용).
/// IHttpClientFactory("backend") + IBackendSession(JWT 홀더) + Http* 구현.
/// </summary>
internal static void RegisterBackendServices(IServiceCollection services)
{
    services.AddHttpClient(HttpBackendClient.HttpClientName, (sp, client) =>
    {
        var s = sp.GetRequiredService<ISettingsService>().Current;
        if (!string.IsNullOrWhiteSpace(s.BackendBaseUrl))
            client.BaseAddress = new Uri(s.BackendBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(100);
    });
    services.AddSingleton<IBackendSession, BackendSession>();

    services.AddSingleton<IFirebaseClient>(sp =>
    {
        var s = sp.GetRequiredService<ISettingsService>().Current;
        return new HttpFirebaseClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IBackendSession>(),
            s.BackendApiKey, s.StorageBucket,
            configured: !string.IsNullOrWhiteSpace(s.BackendBaseUrl),
            sp.GetService<ILogger<HttpFirebaseClient>>());
    });

    services.AddSingleton<IFrameRepository>(sp => new HttpFrameRepository(/* … */));
    services.AddSingleton<IAccountService>(sp => new HttpAccountService(/* … */));
    services.AddSingleton<IQrUsageService>(sp => new HttpQrUsageService(/* … */));
    services.AddSingleton<ITempUserLimitsService>(sp => new HttpTempUserLimitsService(/* … */));
}
```

**동반 변경**

| 위치 | 변경 |
|---|---|
| `ServiceRegistration.cs:14` | `using MCPhoto.Firebase;` 삭제 |
| `:42-43` | `IPasswordPromptDialogService` 등록 삭제 |
| `:83-85` | 주석 갱신 + `RegisterBackendServices(services)` 호출 |
| `:86` | `services.AddSingleton<IUploadService, UploadService>();` — 타입은 동일, **`MCPhoto.Core.Upload.UploadService`로 해석**(D-A 이관) |
| `:122-128` | `FirebaseClient` 구상 등록 삭제 |
| `:221-222` | `PasswordResetViewModel` 등록 삭제 |

> `InternalsVisibleTo("MCPhoto.Tests")`(`MCPhoto.App.csproj:33-35`)는 `RegisterBackendOrFirebase`
> 테스트용이었다. `BackendDiFlagTests` 삭제 후 유일 소비자가 사라지므로 **함께 제거 검토** —
> 단 `XamlResourceTests`가 internal 타입을 참조할 수 있으니 **빌드 통과 확인 후 판단**(구현자 재량,
> 제거하지 않아도 무해).

### 7.2 `HttpAccountService` 축소

`src/MCPhoto.Http/HttpAccountService.cs`(460줄)에서 제거 메서드 13개의 구현을 삭제한다
(`:35-77` Login/VerifyPassword, `:120-190` Register/Create/ChangePassword, `:236-368` Seed/Email/Reset 계열).
유지 7개: `:80-118`(Google), `:191-235`(GetAll/Delete/SetRole), `:371-428`(PIN 3종), `:430-460`(매핑 헬퍼).

**`MapToUser` 헬퍼 수정**(`:430-455`): `Password`·`EmailVerified` 대입 삭제,
`AuthMethod` 파싱을 `AuthMethodExtensions.ParseAuthMethod(dto.AuthMethod)`로 교체(§5.2).

**`src/MCPhoto.Http/Dto/AccountDtos.cs` 축소**: 삭제 DTO 11개 —
`LoginRequest`, `RegisterRequest`, `CreateAccountRequest`, `ChangePasswordRequest`, `SetEmailRequest`,
`IdOrEmailRequest`, `PasswordResetConfirmByCodeRequest`, `PasswordResetConfirmByTokenRequest`,
`VerifyEmailConfirmByCodeRequest`, `VerifyEmailConfirmByTokenRequest`, `VerifyEmailResponse`.
유지: `LoginResponse`(google 응답 형식), `GoogleLoginRequest`, `SetRoleRequest`,
`VerifyPinRequest`/`VerifyPinResponse`/`SetPinRequest`/`ResetPinRequest`, `UserResponse`.
`UserResponse`에서 `EmailVerified` 프로퍼티 삭제.

---

## §8. 마이그레이션 스크립트 설계 (D3 · D4)

### 8.1 개요

| 항목 | 값 |
|---|---|
| 경로 | `web/functions/scripts/migrate-google-only-accounts.mjs` |
| 런타임 | Node 20 ESM, `firebase-admin` (기존 `web/functions/node_modules` 재사용) |
| 실행 위치 | `web/functions/` (`node scripts/migrate-google-only-accounts.mjs …`) |
| 인증 | ADC — `GOOGLE_APPLICATION_CREDENTIALS` 또는 `gcloud auth application-default login` |
| 기본 모드 | **dry-run**(무조건). `--apply` 없으면 어떤 쓰기도 하지 않는다 |

### 8.2 CLI 인자

| 인자 | 필수 | 기본 | 의미 |
|---|---|---|---|
| `--project <id>` | ✅ | — | Firebase 프로젝트 ID(오조작 방지 위해 필수) |
| `--apply` | — | off | 실제 반영. 없으면 dry-run |
| `--admin-email <email>` | — | `devmcjo@gmail.com` | admin으로 승격할 Google 이메일(D3) |
| `--admin-id <id>` | — | `devmcjo` | 최종 admin 문서 ID(D3) |
| `--delete-orphans` | — | off | D4의 "Google 이메일 없는 계정 삭제 + 프레임 cascade"를 수행. **분리 플래그**(파괴적이므로 명시적 옵트인) |
| `--clear-pin <id>` | — | — | 지정 계정의 `pinHash` 필드만 삭제(admin PIN 분실 복구, §5.6). 다른 단계는 실행하지 않는다 |
| `--verbose` | — | off | 문서 단위 상세 로그 |

> **`--delete-orphans`를 기본 off로 두는 이유**: D4는 "계정 + 소유 프레임 영구 삭제"라
> 되돌릴 수 없다. dry-run으로 대상 목록을 눈으로 확인한 뒤 별도 실행하도록 강제한다.
> `--apply` 단독 실행은 **비파괴 단계(1~4)만** 수행한다.

### 8.3 단계별 의사코드

```
main():
  args = parseArgs()
  if args.clearPin: runClearPin(args); return      # 독립 경로(다른 단계 미실행)

  initializeApp({ projectId: args.project })
  report = { scanned:0, planned:[], applied:[], skipped:[], errors:[] }

  users = await db.collection('users').get()       # 전량 로드(A5: 수백 건 규모 가정)
  report.scanned = users.size
  printHeader(args, report.scanned)

  # ── Step 1: 대상 식별(읽기 전용) ──
  adminSource = users.find(d => d.email === args.adminEmail)      # 예: devmcjo-2
  adminTarget = users.find(d => d.id === args.adminId)            # 예: devmcjo(password 계정)
  if !adminSource: ERROR('admin-email 계정을 찾을 수 없습니다') ; exit 1     # A6
  if adminSource.id === args.adminId:
      note('이미 목표 ID로 존재 — Step 2·3 생략(재실행 안전)')

  # ── Step 2: admin 문서 재생성(Firestore는 문서 ID 변경 불가) ──
  # 순서가 핵심: (a) 신규 생성 → (b) 참조 갱신 → (c) 구 문서 삭제.
  # 역순이면 중간 실패 시 계정이 사라진다.
  newDoc = {
      id: args.adminId,
      role: 'admin',
      createdAt: adminSource.createdAt,        # 원본 가입일 보존(TempUser 시간한도 기준 아님 — admin)
      email: adminSource.email,
      authMethod: 'google',
      ...(adminSource.pinHash ? { pinHash: adminSource.pinHash } : {}),   # PIN 승계
      ...(adminSource.qrUsedCount != null ? { qrUsedCount: adminSource.qrUsedCount } : {}),
  }
  plan('DELETE', adminTarget.id, '기존 password 계정 devmcjo')   # (c)로 미룸, 계획만
  plan('SET',    args.adminId, newDoc)

  # ── Step 3: ownerId(=frameTemplates.userId) 참조 갱신 ──   # A7
  frames = await db.collection('frameTemplates').where('userId','==',adminSource.id).get()
  for f in frames: plan('UPDATE', `frameTemplates/${f.id}`, { userId: args.adminId })
  # 다른 컬렉션 소유자 참조 스캔(방어): resultSessions에는 소유자 필드 없음 → 확인만 하고 리포트에 기록

  # ── Step 4: 전 계정 필드 정리 ──
  for u in users:
      patch = {}
      if 'password'      in u: patch.password      = FieldValue.delete()
      if 'emailVerified' in u: patch.emailVerified = FieldValue.delete()
      am = u.authMethod
      if am === 'sso' or am == null or am === '' : patch.authMethod = 'google'
      elif am === 'password':
          # 규칙: password 계정이지만 Google 이메일이 있으면 'google'로 통일,
          #        없으면 로그인 불가 → Step 5 삭제 대상(여기서는 authMethod 미변경)
          if hasGoogleEmail(u): patch.authMethod = 'google'
      if patch: plan('UPDATE', `users/${u.id}`, patch)

  # ── Step 5: 로그인 불가 계정 삭제(--delete-orphans 시에만) ──
  # 판정: email 필드가 없거나 빈 문자열 → Google 로그인 경로가 존재할 수 없음.
  #       (email이 있으면 그 주소로 Google 로그인 시 loginWithGoogleEmail이 매핑하므로 살린다)
  orphans = users.filter(u => !u.email || u.email.trim() === '')
                 .filter(u => u.id !== args.adminId && u.id !== adminSource.id)
  if args.deleteOrphans:
      for o in orphans:
          ofr = await db.collection('frameTemplates').where('userId','==',o.id).get()
          for f in ofr:
              plan('DELETE-STORAGE', f.imageUrl)      # frames/{userId}/… prefix
              plan('DELETE', `frameTemplates/${f.id}`)
          plan('DELETE', `users/${o.id}`)
  else:
      report.skipped.push(...orphans.map(o => `${o.id} (--delete-orphans 미지정)`))

  # ── 실행 ──
  printPlan(report.planned)                       # dry-run은 여기서 종료
  if !args.apply: printSummary('DRY-RUN'); return

  await executeInOrder(report.planned)            # §8.4
  printSummary('APPLIED')
```

### 8.4 실행 순서와 재실행 안전성(idempotency)

**실행 순서(중간 실패 대비)** — 이 순서를 어기면 데이터 손실이 발생한다.

| 순번 | 동작 | 실패 시 상태 | 복구 |
|---|---|---|---|
| 1 | `users/{adminId}` **생성**(신규 admin 문서) | 구 `devmcjo`(password)와 `devmcjo-2`가 공존 | 재실행(2번부터 이어감) |
| 2 | `frameTemplates.userId` **갱신**(`devmcjo-2` → `devmcjo`) | 일부 프레임만 갱신됨 | 재실행(where 쿼리가 남은 것만 잡음) |
| 3 | `users/{adminSource.id}` **삭제**(`devmcjo-2`) | 중복 계정 잔존(email 동일 2건) | 재실행 |
| 4 | 전 계정 필드 정리(batch) | 일부만 정리 | 재실행(이미 정리된 문서는 patch 비어 skip) |
| 5 | orphan 삭제(옵트인) | 일부만 삭제 | 재실행 |

> ⚠️ **1번과 3번 사이에 email 중복(2건)이 존재하는 구간이 생긴다.**
> `findByEmailField`는 `limit(1)`이라(`services/accounts.ts:375`) 이 구간에 SSO 로그인이 들어오면
> 어느 문서로 매핑될지 비결정적이다. → **마이그레이션은 서비스 중단 시간(키오스크 미운영 시간)에
> 실행할 것**을 `docs/USER-ACTIONS.md`에 명시. 실행 시간은 수 초 규모.

**재실행 안전성 보장 규칙 5개**

1. Step 2는 `adminSource.id === adminId`면 전체 생략(이미 완료 상태 감지).
2. Step 2의 `SET`은 `create`가 아닌 `set(doc, {merge:false})` — 재실행 시 동일 결과(멱등).
3. Step 3의 쿼리는 `userId == adminSource.id` — 갱신된 문서는 다음 실행에서 조회되지 않음.
4. Step 4는 `patch`가 비면 write를 발행하지 않음 — 반복 실행해도 write 0.
5. Step 5는 삭제 대상이 없으면 no-op.

**배치 처리(A5)**: Firestore `WriteBatch` 상한 500. `chunk(plans, 400)`로 나눠 순차 커밋하고,
각 배치 커밋 후 진행률을 출력한다. 배치 중간 실패 시 이미 커밋된 배치는 유지되고,
스크립트는 실패 지점을 출력한 뒤 **non-zero exit**한다(재실행으로 이어감).

**Storage 삭제**: `frames/{userId}/` prefix를 `getStorage().bucket().deleteFiles({prefix})`로 처리.
Firestore와 원자성이 없으므로 **Storage 먼저 → Firestore 나중** 순서(고아 파일보다 고아 문서가 낫다는
현행 `deleteAllFramesByUser` 규약과 정합).

### 8.5 출력 형식

```
════════════════════════════════════════════════════════════════
 MCPhoto — Google-only 계정 마이그레이션 (it15)
 project      : mcphoto-955fb
 mode         : DRY-RUN            ← --apply 시 APPLY
 admin-email  : devmcjo@gmail.com
 admin-id     : devmcjo
 delete-orphans: NO
════════════════════════════════════════════════════════════════
 [SCAN] users 컬렉션 문서 수: 14
 [SCAN] frameTemplates 문서 수: 37

 ── Step 2: admin 문서 재생성 ──────────────────────────────────
  + SET    users/devmcjo            role=admin authMethod=google pinHash=승계
  - DELETE users/devmcjo            (기존 password 계정 — 신규 SET가 덮어씀)
  - DELETE users/devmcjo-2          (원본 SSO 계정, 재생성 후 제거)

 ── Step 3: 소유자 참조 갱신 ───────────────────────────────────
  ~ UPDATE frameTemplates/f_8821    userId: devmcjo-2 → devmcjo
  … 총 6건

 ── Step 4: 필드 정리 ──────────────────────────────────────────
  ~ users/alice     -password -emailVerified  authMethod: sso → google
  ~ users/bob       -password -emailVerified  authMethod: password → google (email 보유)
  … 총 12건

 ── Step 5: 로그인 불가 계정 ───────────────────────────────────
  ! users/legacy01  email 없음 → 삭제 대상(프레임 2건 cascade)
  ! users/testuser  email 없음 → 삭제 대상(프레임 0건)
  ⓘ --delete-orphans 미지정 → 건너뜀

════════════════════════════════════════════════════════════════
 요약: 계획 21건 (SET 1 / UPDATE 18 / DELETE 2) · 건너뜀 2 · 오류 0
 ⓘ DRY-RUN 이므로 아무것도 변경되지 않았습니다.
    실제 반영: node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --apply
════════════════════════════════════════════════════════════════
```

**종료 코드**: 0=성공, 1=대상 미발견/인자 오류, 2=실행 중 실패(부분 적용 — 재실행 필요).

### 8.6 `docs/USER-ACTIONS.md` 추가 절

새 절 **"§X. it15 계정 마이그레이션(1회성)"** 에 다음을 기재한다:

1. **사전**: 키오스크 앱 종료(서비스 중단 창 확보). `cd web/functions && npm ci`.
2. **인증**: `gcloud auth application-default login` 또는 서비스 계정 키 경로를 `GOOGLE_APPLICATION_CREDENTIALS`에 설정.
3. **dry-run**: `node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb`
   → 출력의 Step 5 목록을 육안 확인.
4. **적용(비파괴)**: 위 명령에 `--apply` 추가.
5. **적용(파괴 — 선택)**: `--apply --delete-orphans`.
6. **검증**: 앱 실행 → Google 로그인(devmcjo@gmail.com) → 상단 바에 `devmcjo` 표시, 관리자 도구 노출.
7. **admin PIN 분실 시**: `--clear-pin devmcjo --apply` 후 앱에서 재설정.
8. **참고**: `SENDGRID_API_KEY` 시크릿은 더 이상 필요 없다(등록돼 있어도 무해, 삭제 가능).

---

## §9. HTTP 계약 동결 (서버·클라 병렬 작업 경계)

`js-developer`(서버)와 `wpf-developer`(클라)가 **동시에** 작업한다. 아래 계약은 **설계 시점에 확정**되며,
어느 한쪽이 임의로 바꿀 수 없다. 변경이 필요하면 이 문서를 먼저 갱신한다.

### 9.1 동결 계약 표 (it15 이후 전체 계정·인증 API)

| # | 요청 | 헤더 | Body | 성공 | 실패 |
|---|---|---|---|---|---|
| G1 | `POST /auth/google` | `X-MCPhoto-Client: {apiKey}` | `{code, codeVerifier, redirectUri, nonce?}` | `200 {token, expiresIn, user}` | `400` 형식 / `401` 검증실패(일반화) / `501` 미구성 |
| A1 | `GET /accounts` | `Authorization: Bearer {jwt}` | — | `200 UserResponse[]` | `401` / `403` 비power |
| A2 | `DELETE /accounts/:id` | Bearer | — | `204` | `403` 위계/자기자신 / `404` |
| A3 | `PATCH /accounts/:id/role` | Bearer | `{role}` | `204` | `400` 형식 / `403` 매트릭스 / `404` |
| A4 | `GET /accounts/me/qr-usage` | Bearer | — | `200 {role,blocked,reason,remainingMs,remainingCount,limits}` | `401` / `404` |
| P1 | `POST /accounts/me/pin/verify` | Bearer | `{pin}` | `200 {ok:true}` | `400` 형식 / `401` 불일치 / `409` 미설정 |
| P2 | `PUT /accounts/me/pin` | Bearer | `{newPin, currentPin?}` | `204` | `400` 형식 / `401` 현재PIN 불일치 / `404` |
| P3 | `PUT /accounts/:id/pin` | Bearer | `{newPin}` | `204` | `400` 자기자신/형식 / `403` 위계 / `404` |
| L1 | `GET /config/temp-user-limits` · `PATCH` | Bearer | (현행) | (현행) | (현행) |

**`UserResponse` 와이어 형식 (동결)**

```json
{
  "id": "devmcjo",
  "role": "admin",
  "createdAt": "2025-11-02T08:31:00.000Z",
  "email": "devmcjo@gmail.com",
  "authMethod": "google",
  "hasPin": true
}
```

> `emailVerified` 필드는 **응답에서 사라진다**. 클라의 `UserResponse` DTO에서도 프로퍼티를 삭제하므로
> 서버가 잔여 필드를 보내더라도 `System.Text.Json` 기본 설정(미지정 멤버 무시)에서 무해하다.
> → **서버·클라 배포 순서에 의존성이 없다**(어느 쪽이 먼저 나가도 동작).

### 9.2 배포 순서 독립성 검증

| 시나리오 | 결과 |
|---|---|
| **서버 먼저** 배포(구 클라 + 신 서버) | 구 클라의 id/pw 로그인 → `404`. 구 클라는 401만 처리하므로 일반 오류 메시지 표시. **Google 로그인은 정상 동작**(신규 계정만 temp_user로 생성). 허용 가능 |
| **클라 먼저** 배포(신 클라 + 구 서버) | 신 클라는 `/auth/google` + PIN + 역할만 호출 → **전부 구 서버에 존재**. 응답에 `emailVerified`가 섞여 와도 무시. **완전 정상 동작**(신규 계정이 `user`로 생성되는 차이만) |
| 동시 배포 | 정상 |

→ **상호 대기 불필요.** 두 개발자는 독립적으로 진행하고, 각자의 검증 명령(§11)만 통과시키면 된다.

### 9.3 계약 위반 감지

서버 측 신규 테스트(`S-N2`)와 클라 측 DTO 테스트(`HttpAccountServiceTests`)가 **같은 JSON 픽스처 문자열**을
쓰도록 한다. 픽스처는 §9.1의 `UserResponse` 예시를 그대로 사용.

---

## §10. 테스트 전략

### 10.1 삭제할 테스트 (클라 5파일 + 서버 2파일)

| 파일 | 사유 |
|---|---|
| `tests/MCPhoto.Tests/PasswordResetViewModelTests.cs` | VM 삭제 |
| `tests/MCPhoto.Tests/AccountTests.cs` | 레거시 `MCPhoto.Firebase.AccountService` 전용 |
| `tests/MCPhoto.Tests/FirebaseClientTests.cs` | `KeyCandidatePaths` 전용 |
| `tests/MCPhoto.Tests/Http/BackendDiFlagTests.cs` | `UseBackend` 분기 전용 |
| `tests/MCPhoto.Tests/AccountViewModelEmailTests.cs` | 이메일 인증 섹션 전용(PIN 케이스는 이관) |
| `web/functions/src/__tests__/email.test.ts` | 모듈 삭제 |
| `web/functions/src/__tests__/tokens.test.ts` | 모듈 삭제 |

### 10.2 수정할 테스트

| 파일 | 조치 |
|---|---|
| `LoginGuestViewModelTests.cs`(447줄) | id/pw·회원가입·비번찾기 케이스 삭제. **Google 케이스 유지**. 게이트 검증을 `GoogleClientId` 단독으로 재작성 |
| `UserMgmtViewModelTests.cs`(339줄) | `ResetUserPassword` 케이스 삭제, `isBackend` 인자 제거, **`HasPin` 열 표시 케이스 신규** |
| `AccountViewModelPinTests.cs`(224줄) | fake 축소 + `CanChangePin` 무조건 true로 갱신 + **PIN 강제 생성 케이스 신규**(§10.3) |
| `AccountViewModelTempUserTests.cs`(195줄) | fake 축소, `AccountMode.PasswordChange` → `Account` |
| `Http/HttpAccountServiceTests.cs`(790줄) | 삭제 메서드 케이스 제거(약 절반). Google·PIN·역할 케이스 유지·보강 |
| `Http/BackendSettingsTests.cs`(89줄) | `UseBackend` 단언 5건 → 트림·슬래시 보정 3건으로 재작성 |
| `SettingsTests.cs`(345줄) | **레거시 ini 키 호환 케이스 1건 신규**(A3) |
| `DiagnosticsViewModelTests.cs`(172줄) | `FirebaseKeyCandidates` 케이스 삭제, 신규 항목(백엔드 주소·키 내장 여부) 케이스 추가 |
| `XamlResourceTests.cs`(328줄) | 삭제 View 2개(`PasswordResetView`, `PasswordPromptWindow`) 엔트리 제거(A8) |
| `UploadServiceTests.cs` · `UploadContractTests.cs` | `using MCPhoto.Firebase` → `using MCPhoto.Core.Upload`(D-A) |
| `Http/HttpFirebaseClientTests.cs` | `using MCPhoto.Firebase` 제거(참조가 `UploadService`면 Core로) |
| `web/functions/src/__tests__/accounts.test.ts`(717줄) | login/register/email/reset 케이스 삭제. `createGoogleAccount`·`setRole`·PIN·`getQrUsage` 유지 |
| `web/functions/src/__tests__/validation.test.ts` | `validatePassword`·`validateVerificationCode` 케이스 삭제 |
| `web/functions/src/__tests__/password.test.ts` | `verifyPassword`(평문 마이그레이션) 케이스 삭제, `hashPassword`/`verifyHash`는 유지 |
| `web/functions/src/__tests__/config.test.ts` | 이메일 config 검증 케이스가 있으면 삭제(현재 grep상 없음 — 확인만) |

### 10.3 신규 테스트 (커버리지 보강 — 필수)

| # | 대상 | 케이스 |
|---|---|---|
| T1 | `AccountViewModelPinTests` | **PIN 미설정 계정이 계정 관리에 진입하면 `PromptSetup`이 정확히 1회 호출**된다 |
| T2 | `AccountViewModelPinTests` | **PIN 설정 취소 시 `ReturnFromOverlay`가 호출되고 화면에 머물지 않는다** |
| T3 | `AccountViewModelPinTests` | **HasPin=true 계정은 진입 시 어떤 PIN 다이얼로그도 뜨지 않는다**(negative) |
| T4 | `AppShellViewModel` 테스트(신규 또는 기존 확장) | **`IPinPromptDialogService` 미등록 시 `OpenSettings`가 진입하지 않는다**(fail-closed) |
| T5 | `AppShellViewModel` 테스트 | **게스트(CurrentUser=null)는 PIN 없이 설정 진입 가능**(현행 보존) |
| T6 | `SettingsTests` | **`UseBackend=True`가 남은 레거시 ini를 Load→Save해도 예외 없이 나머지 값 보존**(A3) |
| T7 | `UserMgmtViewModelTests` | **`HasPin` 열이 "설정됨"/"미설정"으로 표시**된다 |
| T8 | `web` `googleOnlyAccounts.test.ts` | **`createGoogleAccount`가 `role:"temp_user"`, `authMethod:"google"`로 생성하고 `password` 필드를 쓰지 않는다** |
| T9 | `web` `googleOnlyAccounts.test.ts` | **`toResponse`가 `emailVerified`를 포함하지 않고 `authMethod`를 그대로 반환**한다 |
| T10 | `web` `googleOnlyAccounts.test.ts` | **기존 계정 SSO 재로그인 시 role이 유지된다**(P3, 강등 없음) |
| T11 | `web` `accounts.test.ts` | **`setRole`로 temp_user→user 승격이 admin에게만 허용**(it13 매트릭스 회귀 방어) |

### 10.4 무회귀 게이트

| 게이트 | 명령 | 기준 |
|---|---|---|
| G-1 | `dotnet build -c Release` | 경고 0 / 오류 0 |
| G-2 | `dotnet test` | 실패 0. 총수는 삭제분만큼 감소하되 **§10.3 신규 11건이 반영**돼 있을 것 |
| G-3 | `cd web/functions && npm run typecheck` | 오류 0 |
| G-4 | `cd web/functions && npm test` | 실패 0 |
| G-5 | `grep -rn "MCPhoto.Firebase" src/ tests/ --include=*.cs --include=*.csproj` | **매치 0** |
| G-6 | `grep -rn "UseBackend\|EmailVerified\|serviceAccountKey" src/ --include=*.cs` | **매치 0** |
| G-7 | `grep -rn "password\|emailVerified" web/functions/src --include=*.ts` | `hashPassword`/`verifyHash`(PIN용) 외 **매치 0** |

---

## §11. 구현 WBS

두 트랙(**S = 서버 / `js-developer`**, **C = 클라이언트 / `wpf-developer`**)은 §9의 계약 동결에 따라
**완전 병렬**로 진행한다. 트랙 간 선행 조건은 **없다**. **D 트랙(마이그레이션)** 은 S 트랙 완료 후 실행한다.

> 클라이언트 트랙에는 별도 설계 `wpf-it15-frame-ux-design.md`의 **프레임 UX 2건(F1·F2)** 이 추가로 합쳐진다.
> 본 문서는 그 내용을 다루지 않으며, 통합 시 C-Step 뒤에 이어 붙인다.

### 검증된 사실 / 미검증 가정 (WBS 헤더)

- **검증된 사실**: §1의 F1~F37 (전부 file:line 근거)
- **미검증 가정**: §2의 A1~A8 (전부 아래 단계에 매핑됨 — A1·A2→C-Step 2, A3→C-Step 3,
  A4→§5.4에서 설계 판정 완료, A5·A6·A7→D-Step 1, A8→C-Step 6)

---

### S 트랙 — 서버 (`js-developer`, 4단계)

#### S-Step 1: 인증 라우트·서비스 제거
- **Context Brief**: MCPhoto 백엔드(`web/functions`, Express on Cloud Functions)는 ID/PW 로그인·회원가입·
  이메일 인증·비밀번호 재설정 라우트를 갖고 있다. it15에서 인증 수단을 Google SSO 단독으로 축소하므로
  이 라우트와 뒷단 서비스 함수를 전부 제거한다. `POST /auth/google`만 남는다.
- **대상 파일**: `web/functions/src/routes/auth.ts`, `src/routes/accounts.ts`, `src/services/accounts.ts`,
  `src/services/email.ts`(삭제), `src/services/tokens.ts`(삭제), `src/domain/tokens.ts`(삭제),
  `src/domain/validation.ts`, `src/domain/password.ts`, `src/config.ts`, `src/index.ts`
- **선행 조건**: 없음
- **구현 내용**: §3.4 표 S1~S8 전부. 라우트 제거는 §5.4 표의 ❌ 행 9개.
  `validatePassword`/`validateVerificationCode` 삭제, `verifyPassword` 삭제
  (`hashPassword`/`verifyHash`는 PIN이 쓰므로 **유지**). `config.ts`에서 이메일 3필드 + 강제 검증 삭제하되
  `hostingBaseUrl`은 **유지**(F36 — `domain/session.ts:81`이 사용). `index.ts`에서 `SENDGRID_API_KEY` 선언 삭제.
- **검증 명령**:
  ```bash
  cd web/functions && npm run typecheck && npm run lint
  grep -rn "sendgrid\|emailProvider\|verify-email\|password-reset" src --include=*.ts   # 매치 0 기대
  ```
- **완료 기준**:
  - [관측] `tsc --noEmit` 오류 0, 위 grep 매치 0, `app.ts`의 라우터 마운트 6개는 그대로
  - [non-goal] `/frames`·`/uploads`·`/config`·`/health` 라우트와 `domain/session.ts`의 `downloadPageUrl` 동작 불변
  - [trigger] 제거는 소스 삭제로만 — 라우트를 410 스텁으로 남기지 않는다(§5.4 A4 판정)
- **롤백**: `git checkout -- web/functions/src`
- [ ] 완료

#### S-Step 2: 스키마·SSO 생성 정책 변경
- **Context Brief**: Firestore `users` 문서에서 `password`·`emailVerified`를 폐지하고, Google SSO 신규
  계정을 `role:"user"` 대신 **`role:"temp_user"`**, `authMethod:"sso"` 대신 **`"google"`**으로 만든다(D2·지시 2).
  `pinHash`가 유일한 서버 보관 자격증명이 된다.
- **대상 파일**: `web/functions/src/services/dto.ts`, `src/services/accounts.ts`
- **선행 조건**: S-Step 1
- **구현 내용**: §5.3의 `UserDoc`/`UserResponse`/`toResponse` 정의로 교체. `TokenDoc` 삭제.
  `createGoogleAccount`를 §5.5 코드로 교체(`makeSentinelPasswordHash`·`randomBytes` import 삭제).
  `loginExistingGoogleAccount`에서 `emailVerified=true` 승격 write 삭제 → 읽기 전용 경로화.
  `ensureEmailNotVerifiedElsewhere`·`markEmailVerified`·`findByIdOrEmail` 삭제(소비자 0 확인 후).
- **검증 명령**:
  ```bash
  cd web/functions && npm run typecheck
  grep -rn "emailVerified\|sentinel\|makeSentinel" src --include=*.ts    # 매치 0 기대
  grep -n "temp_user" src/services/accounts.ts                            # createGoogleAccount에 존재
  ```
- **완료 기준**:
  - [관측] `createGoogleAccount`의 doc 리터럴에 `role:"temp_user"`·`authMethod:"google"`이 있고 `password` 키가 없다
  - [non-goal] **기존 계정의 role은 로그인 시 변경되지 않는다**(P3) — `loginExistingGoogleAccount`에 role write 없음
  - [trigger] temp_user 생성은 **email로 계정을 찾지 못한 경우에만** — 기존 계정 경로는 무영향
- **롤백**: `git checkout -- web/functions/src/services`
- [ ] 완료

#### S-Step 3: 테스트 정리·보강
- **Context Brief**: 서버 jest 기준선은 206/206(15 suites). 제거 기능 테스트를 지우고 신규 정책 테스트를 넣는다.
- **대상 파일**: `web/functions/src/__tests__/{accounts,validation,password,config}.test.ts`(수정),
  `{email,tokens}.test.ts`(삭제), `googleOnlyAccounts.test.ts`(신규)
- **선행 조건**: S-Step 2
- **구현 내용**: §10.1·§10.2·§10.3(T8~T11). 신규 파일은 `helpers/fakeFirestore.ts` 재사용.
  `UserResponse` 픽스처는 §9.1의 JSON을 그대로 쓴다(계약 위반 감지, §9.3).
- **검증 명령**: `cd web/functions && npm test`
- **완료 기준**:
  - [관측] 실패 0. `googleOnlyAccounts.test.ts`가 T8·T9·T10 3케이스 이상 포함
  - [non-goal] `uploads.test.ts`·`tempUserLimit.test.ts`·`roles.test.ts`·`googleAuth.test.ts` 통과 수 불변
  - [trigger] 신규 케이스는 fakeFirestore 상태만으로 판정 — 실 Firestore 접속 없음
- **롤백**: `git checkout -- web/functions/src/__tests__`
- [ ] 완료

#### S-Step 4: 보안 규칙 주석·배포 문서 동기화
- **Context Brief**: `firestore.rules`의 `users` 차단 주석이 "평문 pw 보호"라는 낡은 근거를 담고 있다(F28).
  규칙 로직은 바꾸지 않고 근거만 갱신한다. `storage.rules`는 변경 없음.
- **대상 파일**: `web/firestore.rules`, `docs/design/firebase-contract.md`,
  `docs/analysis/40-database-firestore-and-storage-schema.md`
- **선행 조건**: S-Step 2
- **구현 내용**: 주석을 "계정 문서 전면 차단(PIN 해시·역할·이메일 보호)"으로 교체.
  `firebase-contract.md` §2.1의 `UserDoc` 스키마를 §5.3으로 갱신. 40번 분석 문서의 필드 표 갱신.
- **검증 명령**: `grep -n "평문" web/firestore.rules` (매치 0) · `grep -n "password\|emailVerified" docs/design/firebase-contract.md` (스키마 표에 매치 0)
- **완료 기준**:
  - [관측] `users`/`frameTemplates` 규칙의 `allow` 구문이 **바이트 단위로 불변**, 주석만 변경
  - [non-goal] `resultSessions`의 `get:true`/`list:false` 분리 유지(토큰 열거 방어)
  - [trigger] 규칙 배포는 별도(`firebase deploy --only firestore:rules`) — 이 단계는 소스만
- **롤백**: `git checkout -- web/firestore.rules docs/`
- [ ] 완료

---

### C 트랙 — 클라이언트 (`wpf-developer`, 7단계)

> 모든 `.cs`/`.xaml`은 **UTF-8 without BOM**으로 저장한다(F33). 기존 파일 수정 시 인코딩·개행을 보존한다.

#### C-Step 1: Core 계약 축소 (`IAccountService` · `User` · `AppState`)
- **Context Brief**: MCPhoto의 계정 계약은 `MCPhoto.Core`의 `IAccountService`(20메서드) 1개 인터페이스에
  로그인/CRUD/역할/SSO/이메일/PIN이 전부 몰려 있다. it15는 인증을 Google SSO + PIN으로 축소하므로
  13개 메서드를 제거하고, `User`에서 `Password`·`EmailVerified`를 빼고 `AuthMethod` enum을 재정의한다.
  이 단계는 **의도적으로 컴파일이 깨진다**(소비자 수정은 C-Step 3~6). Core만 먼저 확정해 계약을 못박는다.
- **대상 파일**: `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Core/Models/User.cs`,
  `src/MCPhoto.Core/Navigation/AppState.cs`
- **선행 조건**: 없음
- **구현 내용**: §5.1 인터페이스 코드로 교체(7메서드). §5.2 `User`·`AuthMethod`·`AuthMethodExtensions`로 교체.
  `AppState`에서 `PasswordReset` 값 삭제(`AppState.cs:45-46`).
- **검증 명령**: `dotnet build src/MCPhoto.Core/MCPhoto.Core.csproj -c Release`
- **완료 기준**:
  - [관측] `MCPhoto.Core` 단독 빌드 성공(경고 0). `IAccountService`에 `public` 메서드가 정확히 7개
  - [non-goal] `UserRole`·`RoleChangePolicy`·`IQrUsageService`·`ITempUserLimitsService` 무변경
  - [trigger] 솔루션 전체 빌드는 이 단계에서 실패해도 정상 — C-Step 3 이후 복구
- **롤백**: `git checkout -- src/MCPhoto.Core`
- [ ] 완료

#### C-Step 2: `MCPhoto.Firebase` 제거 + `UploadService` 이관 (A1·A2)
- **Context Brief**: `MCPhoto.Firebase`는 Admin SDK 직결 레거시 경로(`serviceAccountKey.json`)다.
  D1로 전면 폐기하지만 **`UploadService`만은 예외** — `UseBackend`와 무관하게 무조건 등록되는 유일한
  `IUploadService` 구현이고 FirebaseAdmin에 의존하지 않는다(§4.1). 먼저 Core로 옮긴 뒤 프로젝트를 지운다.
- **대상 파일**: 이동 `src/MCPhoto.Firebase/UploadService.cs` → `src/MCPhoto.Core/Upload/UploadService.cs`;
  삭제 `src/MCPhoto.Firebase/**`(프로젝트 폴더 전체); 수정 `MCPhoto.sln`(14줄, F34),
  `src/MCPhoto.App/MCPhoto.App.csproj:25`, `tests/MCPhoto.Tests/MCPhoto.Tests.csproj:25`, `README.md:35`
- **선행 조건**: 없음(C-Step 1과 병렬 가능)
- **구현 내용**: `UploadService.cs`의 `namespace MCPhoto.Firebase;` → `namespace MCPhoto.Core.Upload;`
  로 바꾸고 파일만 이동(본문 무변경 — 이미 `MCPhoto.Core.Upload`를 using 중이므로 해당 using 줄 삭제).
  `TempUserServices.cs`의 Null 구현 2개는 **테스트 로컬로 이관**(D-B): `tests/MCPhoto.Tests/Fakes/NullTempUserLimitsService.cs` 신규.
  `.sln`은 `:14`, `:66-77`, `:110` 삭제.
- **검증 명령**:
  ```powershell
  dotnet build -c Release
  Select-String -Path src\**\*.cs,tests\**\*.cs,*.sln,src\**\*.csproj -Pattern "MCPhoto\.Firebase"
  ```
- **완료 기준**:
  - [관측] `dotnet build -c Release` 경고 0/오류 0. 위 검색 결과 **0건**. `dotnet list package --include-transitive`에 `FirebaseAdmin`·`Google.Cloud.*` 없음 (A1)
  - [non-goal] `IUploadService`/`IFirebaseClient` 인터페이스 시그니처 불변 — `HttpFirebaseClient` 무수정 (A2)
  - [trigger] `UploadService`는 여전히 `ServiceRegistration.cs:86`에서 무조건 등록 — 등록 조건을 추가하지 않는다
- **롤백**: `git checkout -- . && git clean -fd src/MCPhoto.Firebase`
- [ ] 완료

#### C-Step 3: `UseBackend` 폐지 + DI 단순화 (A3)
- **Context Brief**: `AppSettings.UseBackend`는 레거시 Firebase 경로와 백엔드 경로를 고르는 feature flag였다.
  레거시가 사라지므로 플래그·DI 분기·`IsBackendMode` 파생 프로퍼티를 전부 제거해 죽은 분기를 없앤다.
  기존 배포본 ini에 남은 `UseBackend=` 줄은 무시되어야 한다.
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`, `src/MCPhoto.Core/Settings/IniSettingsService.cs`,
  `src/MCPhoto.App/ServiceRegistration.cs`, `src/MCPhoto.Http/HttpAccountService.cs`,
  `src/MCPhoto.Http/Dto/AccountDtos.cs`
- **선행 조건**: C-Step 1, C-Step 2
- **구현 내용**: §4.3 표 + §7.1 `RegisterBackendServices` + §7.2 `HttpAccountService`/DTO 축소.
  `IPasswordPromptDialogService` 등록(`ServiceRegistration.cs:42-43`)·`PasswordResetViewModel` 등록(`:221-222`) 삭제 및
  §3.1의 파일 7개(PasswordReset 3 + PasswordPrompt 4) 삭제.
- **검증 명령**:
  ```powershell
  dotnet build -c Release
  Select-String -Path src\**\*.cs,src\**\*.xaml -Pattern "UseBackend|IsBackendMode|PasswordPrompt|PasswordReset"
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0. 위 검색 0건. `RegisterBackendServices`에 `if (!s.UseBackend)` 분기가 **1개도 없다**
  - [non-goal] `BackendBaseUrl`·`BackendApiKey`·`GoogleClientId`·`StorageBucket` 설정 키와 그 기본값 불변 (A3)
  - [trigger] `NormalizeBackend()`는 빈 base URL에서 **슬래시 보정만 스킵** — 다른 설정을 되돌리지 않는다
- **롤백**: `git checkout -- src/`
- [ ] 완료

#### C-Step 4: 로그인 화면 — Google 단독
- **Context Brief**: 로그인 화면(`AppState.Login`)은 현재 로그인/회원가입 탭, id/pw 입력, self-signup,
  "비밀번호 찾기" 링크, Google 버튼을 모두 갖고 있다. it15는 **Google 버튼 단독 화면**으로 축소한다.
  게스트 촬영 경로는 로그인과 무관하므로 [닫기]는 항상 남는다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs`,
  `src/MCPhoto.App/Views/LoginGuestView.xaml`, `LoginGuestView.xaml.cs`
- **선행 조건**: C-Step 3
- **구현 내용**: §6.1 전부. VM은 `ErrorMessage`/`IsBusy`/`IsGoogleSignInAvailable`/
  `LoginWithGoogleCommand`/`CancelCommand`만 남긴다. `IFirebaseClient` 주입·`IsServerOffline` 제거로
  생성자 파라미터가 4개 → 3개(`AppShellViewModel`, `IAccountService`, `IGoogleSignInService`)가 된다.
  코드비하인드는 `InitializeComponent()`만 남긴다.
- **검증 명령**: `dotnet build -c Release` · `Select-String -Path src\MCPhoto.App\Views\LoginGuestView.xaml -Pattern "PasswordBox|TabItem|회원가입|비밀번호"` (0건)
- **완료 기준**:
  - [관측] `LoginGuestView.xaml`에 `PasswordBox`·탭·"회원가입" 문자열이 0개. Google 버튼 1개 + [닫기] 1개
  - [non-goal] `IGoogleSignInService`(PKCE·loopback)와 `LoginWithGoogle` 커맨드 본문 로직 불변 — 오류 문구 4분기 유지
  - [trigger] Google 버튼 숨김은 **`GoogleClientId`가 빈 값일 때만** — 네트워크 상태로 숨기지 않는다. 숨겨져도 [닫기]는 노출
- **롤백**: `git checkout -- src/MCPhoto.App/Views/LoginGuestView.xaml* src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs`
- [ ] 완료

#### C-Step 5: "계정 관리" 화면 + PIN 게이트 단일화
- **Context Brief**: 설정 진입 게이트가 현재 `AuthMethod`에 따라 PIN/비번으로 갈린다. 비번 계정이 사라지므로
  **PIN 단일 경로**가 된다. 동시에 `AccountView`를 "계정 관리"로 개편하고(비번·이메일 섹션 삭제),
  **진입 시 PIN 미설정이면 최초 설정을 강제**하는 신규 요구를 넣는다(설정 진입과 동일 PIN·동일 다이얼로그).
- **대상 파일**: `src/MCPhoto.App/AppShellViewModel.cs`, `src/MCPhoto.App/ViewModels/AccountViewModel.cs`,
  `src/MCPhoto.App/Views/AccountView.xaml`, `AccountView.xaml.cs`, `src/MCPhoto.App/MainWindow.xaml`
- **선행 조건**: C-Step 3
- **구현 내용**: §6.2 `OpenSettings` + `EnsurePinGateAsync`(public) 도입. §6.3 `AccountMode` 2값 축소,
  섹션 ①②③ 재구성, `OnEnterAsync` PIN 강제 생성. §6.3 표대로 `MainWindow.xaml:62-69` 팝오버 항목 교체.
  `AppShellViewModel`의 `OpenPasswordReset`(`:417-426`)·`OpenAccountCreate`(`:440-442`)·
  `AppState.PasswordReset` 팩토리 케이스(`:254`) 삭제.
- **검증 명령**: `dotnet build -c Release` · `Select-String -Path src\MCPhoto.App -Pattern "AuthMethod.Sso|비밀번호 변경|계정 생성|이메일 인증" -Recurse` (0건)
- **완료 기준**:
  - [관측] 계정 팝오버에 "계정 관리"·"관리자 도구"·"로그아웃" 3항목만. `AccountViewModel`에 `ChangePassword`·`CreateAccount`·`RegisterEmail`·`VerifyEmail`·`ResendEmailVerification` 커맨드 부재
  - [non-goal] **게스트는 설정 진입 시 PIN을 요구받지 않는다**(현행 보존). HasPin=true 계정은 계정 관리 진입 시 PIN 다이얼로그가 뜨지 않는다
  - [trigger] PIN 최초 설정 강제는 **`HasPin == false`일 때만**. 취소 시 `ReturnFromOverlay()`로 즉시 복귀하며 계정 관리 화면에 머물지 않는다
- **롤백**: `git checkout -- src/MCPhoto.App`
- [ ] 완료

#### C-Step 6: 사용자 관리 · 진단 창 UI 정리
- **Context Brief**: 사용자 관리에서 "PW 초기화"를 제거하고(지시 1) PIN 설정 여부 열을 추가한다.
  진단 창에서는 `serviceAccountKey.json` 탐색 경로 섹션을 완전히 없애고(지시 4) 백엔드 기준 항목으로 재구성한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`, `src/MCPhoto.App/Views/UserMgmtView.xaml`,
  `src/MCPhoto.App/ViewModels/DiagnosticsViewModel.cs`, `src/MCPhoto.App/Views/DiagnosticsWindow.xaml`
- **선행 조건**: C-Step 3
- **구현 내용**: §6.5(PW 초기화 삭제, `isBackend` 파라미터 삭제, `HasPin` 열 추가),
  §6.6(키 후보 섹션 삭제, 섹션 제목 "서버 연결(백엔드)", 신규 3항목).
  `DiagnosticsViewModel`은 `ISettingsService`·`SessionContext` 주입이 추가로 필요하다(DI 등록은 이미 있음).
- **검증 명령**: `dotnet build -c Release` · `Select-String -Path src\MCPhoto.App -Pattern "FirebaseKeyCandidate|서비스 계정 키|PW 초기화|ResetUserPassword" -Recurse` (0건)
- **완료 기준**:
  - [관측] 진단 창 XAML에 "서비스 계정 키" 문자열 0개, "백엔드 주소"·"로그인 계정" 항목 존재. 사용자 관리 행에 PIN 열 존재
  - [non-goal] 진단 창의 카메라·ffmpeg·로그 폴더 섹션 불변. 사용자 관리의 역할 콤보·삭제·PIN 재설정 동작 불변
  - [trigger] 진단 창은 **백엔드 API 키 값을 절대 표시하지 않는다** — "설정됨/미설정" 부울 표기만
- **롤백**: `git checkout -- src/MCPhoto.App`
- [ ] 완료

#### C-Step 7: 테스트 정리·보강 (A8)
- **Context Brief**: 클라 기준선은 675/675 통과. 제거 기능 테스트를 지우고 fake 6곳을 축소하며,
  §10.3의 신규 케이스로 남는 기능(PIN 게이트·역할·HasPin 표시·레거시 ini 호환) 커버리지를 보강한다.
- **대상 파일**: §10.1 삭제 5파일, §10.2 수정 11파일, 신규 `tests/MCPhoto.Tests/Fakes/NullTempUserLimitsService.cs`
- **선행 조건**: C-Step 1~6 전부
- **구현 내용**: §10.1·§10.2·§10.3(T1~T7). fake 축소 대상은 `grep -rn "IAccountService" tests/`로 최종 확인(§5.1 주).
  `XamlResourceTests`에서 삭제된 View 2종 엔트리 제거(A8).
- **검증 명령**: `dotnet test`
- **완료 기준**:
  - [관측] 실패 0. T1~T7 7케이스가 존재하고 통과. 총 테스트 수 감소분이 삭제 파일의 케이스 수와 일치
  - [non-goal] 촬영·합성·프레임·QR·업로드 관련 테스트 통과 수 불변
  - [trigger] T4(fail-closed)는 **`IPinPromptDialogService`를 DI에서 뺀 상태**에서만 검증 — 실제 다이얼로그를 띄우지 않는다
- **롤백**: `git checkout -- tests/`
- [ ] 완료

---

### D 트랙 — 마이그레이션 (`js-developer`, 2단계 + 문서 1단계)

#### D-Step 1: 마이그레이션 스크립트 작성 + dry-run 검증 (A5·A6·A7)
- **Context Brief**: Firestore에는 (a) `devmcjo`(구 password admin), (b) `devmcjo-2`(devmcjo@gmail.com으로
  SSO 가입) 두 계정이 있다. Firestore는 문서 ID를 바꿀 수 없으므로 **재생성 + 참조 갱신 + 삭제** 순서가
  필요하다. 동시에 전 계정에서 `password`·`emailVerified`를 지우고 `authMethod`를 `"google"`로 통일한다.
- **대상 파일**: `web/functions/scripts/migrate-google-only-accounts.mjs`(신규)
- **선행 조건**: S-Step 2(스키마 확정) — 스크립트가 쓰는 필드가 서버 스키마와 일치해야 한다
- **구현 내용**: §8.1~§8.5 전부. dry-run 기본, `--apply`·`--delete-orphans`·`--clear-pin` 분리.
  Step 순서(생성 → 참조 갱신 → 삭제)를 §8.4 표대로 강제. 배치 400건 청크.
- **검증 명령**:
  ```bash
  cd web/functions
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb          # dry-run
  node --check scripts/migrate-google-only-accounts.mjs                          # 문법
  ```
- **완료 기준**:
  - [관측] dry-run 출력이 §8.5 형식이고 `users`/`frameTemplates` 문서 수를 표시한다. `--apply` 없이 실행하면 Firestore write가 **0건**(콘솔 Usage로 확인 가능)
  - [non-goal] dry-run은 **읽기만** 한다 — `set`/`update`/`delete`/`deleteFiles` 호출 0 (A5: 배치 상한 미도달 확인, A6: admin-email 미발견 시 exit 1, A7: 스캔한 컬렉션 목록 출력)
  - [trigger] 파괴적 삭제는 `--apply --delete-orphans` **두 플래그가 모두 있을 때만**
- **롤백**: 파일 삭제(`rm web/functions/scripts/migrate-google-only-accounts.mjs`). dry-run은 부작용 없음
- [ ] 완료

#### D-Step 2: 실제 적용 (운영자 실행)
- **Context Brief**: dry-run 결과를 사람이 확인한 뒤 실제 반영한다. 실행 중 email 중복 구간이 잠시 생기므로
  **키오스크 미운영 시간**에 수행한다(§8.4 경고).
- **대상 파일**: (없음 — 데이터 변경)
- **선행 조건**: D-Step 1, S-Step 1~4 배포 완료
- **구현 내용**: `--apply` → 검증 → 필요 시 `--apply --delete-orphans`.
- **검증 명령**:
  ```bash
  node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb          # 재실행 dry-run
  ```
- **완료 기준**:
  - [관측] 재실행 dry-run의 계획 건수가 **0건**(멱등 확인). `users/devmcjo`가 `role:"admin"`·`authMethod:"google"`이고 `password`/`emailVerified` 필드 부재
  - [non-goal] `frameTemplates` 문서 수가 (orphan cascade 삭제분을 제외하고) 변하지 않는다
  - [trigger] 실행은 앱 종료 상태에서 — 실행 중 SSO 로그인이 들어오면 email 중복 구간에 비결정적 매핑 발생
- **롤백**: Firestore 콘솔 백업/PITR. **스크립트에 undo 경로는 없다** — 실행 전 export 권장(`gcloud firestore export`)
- [ ] 완료

#### D-Step 3: 문서 동기화
- **Context Brief**: it15는 인증 모델·DB 스키마·설정 키를 바꾸므로 분석·운영 문서가 낡는다.
- **대상 파일**: §3.5의 8개 파일
- **선행 조건**: S-Step 4, C-Step 7
- **구현 내용**: `docs/USER-ACTIONS.md`에 §8.6의 절 추가. 나머지 7개는 §3.5 참조.
- **검증 명령**: `Select-String -Path docs -Pattern "serviceAccountKey|UseBackend|emailVerified|회원가입|비밀번호 찾기" -Recurse` → `docs/design/wpf-it1*`(과거 설계 문서, 이력이므로 유지) 외 매치 0
- **완료 기준**:
  - [관측] `docs/USER-ACTIONS.md`에 마이그레이션 8단계 절이 존재. `docs/analysis/60-auth-accounts-and-roles.md`에 ID/PW 서술 0
  - [non-goal] **과거 이터레이션 설계 문서(`docs/design/wpf-it10~it14-*.md`)는 수정하지 않는다** — 이력 기록
  - [trigger] 문서 갱신은 코드 확정 후 — 설계와 구현이 어긋난 상태로 문서를 먼저 쓰지 않는다
- **롤백**: `git checkout -- docs/ README.md`
- [ ] 완료

---

### 완결성 게이트 (developer 전달 전 확인 — 전부 ✅)

- [x] 모든 단계에 Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준(3문) / 롤백이 채워져 있다
- [x] 미검증 가정 A1~A8이 전부 검증 단계에 매핑돼 있다(A4는 §5.4에서 설계 판정으로 해소)
- [x] 각 단계가 독립 검증 가능하고 단일 리스크를 갖는다
- [x] UI 변경 단계(C-Step 4·5·6)에 non-goal·trigger가 명시돼 있다
- [x] 트랙 간 선행 조건이 없어 병렬 실행 가능하다(§9.2 배포 순서 독립성으로 뒷받침)
- [x] 전체 단계 수: S 4 + C 7 + D 3 = 14 (트랙별 3~12 범위 준수)

---

## §12. 리스크·트레이드오프 요약

| # | 리스크 | 완화 | 잔여 |
|---|---|---|---|
| R1 | PIN 4자리 브루트포스(서버 잠금 없음) | 클라 5회 실패 시 창 닫힘 + 1.5초 지연(§5.6) | 물리 접근자가 반복 시도 가능 — 백로그(Cloud Armor rate limit) |
| R2 | admin PIN 분실 시 앱 내 복구 불가 | 스크립트 `--clear-pin`(§8.3) + USER-ACTIONS 절차 | 운영자가 CLI 접근 가능해야 함 |
| R3 | 마이그레이션 중 email 중복 구간의 비결정적 SSO 매핑 | 앱 종료 상태에서 실행(§8.4) | 창은 수 초 |
| R4 | 마이그레이션 되돌리기 불가(D4 삭제) | `--delete-orphans` 옵트인 분리 + dry-run 필수 + export 권장 | 사용자 실수 시 백업 의존 |
| R5 | 신규 SSO 계정 전원이 temp_user → admin 부재 시 승격 불가 | D3가 admin 부트스트랩 겸함(P1). USER-ACTIONS에 "admin 1인 상시 유지" 운영 전제 명시 | manager는 승격 권한 없음(it13 확정 매트릭스, 변경 안 함) |
| R6 | 구버전 앱이 남아 있으면 로그인 불가 | 라우트 완전 제거 판정(§5.4 A4) + USER-ACTIONS에 재배포 안내 | 오류 문구가 "구버전"임을 알려주지 않음 |
| R7 | `IFirebaseClient` 이름이 실체(백엔드 게이트웨이)와 불일치 | 이번 범위에서 리네임 안 함(§4.2), 백로그 등록 | 신규 개발자 혼동 가능 |

---

## §13. 이번 범위에서 하지 않는 것 (명시적 non-goal)

1. **`IFirebaseClient` → `IBackendStorageClient` 리네임** (§4.2 — 리뷰 신호 희석 방지, 백로그)
2. **PIN 서버 측 시도 제한/잠금** (§5.6 — DoS 도입 위험, 백로그)
3. **진단 창의 "백엔드 도달 확인" 버튼** (§6.6 — 인터페이스 확장 필요, 백로그)
4. **역할 매트릭스 변경**(manager 승격 권한 부여 등) — it13 확정 사항 보존
5. **PIN 자릿수 변경** — D5로 4자리 확정
6. **프레임 UX 2건(F1·F2)** — 별도 설계 `wpf-it15-frame-ux-design.md`
7. **`docs/design/wpf-it10~it14-*.md` 과거 설계 문서 수정** — 이력 보존
