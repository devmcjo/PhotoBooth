# 계정·인증 UX / 규칙 재설계 (A~D)

> 대상 저장소: `E:\Study\photobooth` (MCPhoto WPF/.NET 8 키오스크 + Firebase Cloud Functions 백엔드).
> 백엔드는 실배포됨(`UseBackend` 모드 사용 중). 본 문서는 **설계 + 파일 단위 구현 계획**이며 코드/커밋을 포함하지 않는다.
> 관례 준수: MVVM CommunityToolkit(`[ObservableProperty]`/`[RelayCommand]`), DI, 역할 위계, 디자인 시스템(Themes/*.xaml), `.cs`/`.ts` = UTF-8 **no BOM** + LF.

---

## 0. 계획 헤더 — 검증된 사실 / 미검증 가정

### 검증된 사실 (verified facts)

**A. 역할 변경**
- `UserMgmtViewModel.PromoteToManager`는 단방향(user→manager)만: `user.Role != UserRole.User`면 조기 return. (`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:99-113`)
- `UserRole.CanManage(target)` = `(int)target <= (int)actor` (User<Manager<Admin). (`src/MCPhoto.Core/Models/UserRole.cs:56-57`)
- 백엔드 `setRole`은 **이미 user↔manager 양방향 지원**: `actor.role !== "admin"`, `role === "admin"`, `currentRole === "admin"`만 차단하고, 그 외 role 값(user/manager)을 그대로 저장. → user→manager(승격)와 manager→user(강등) 모두 통과. (`web/functions/src/services/accounts.ts:215-232`)
- 클라 HTTP `SetRoleAsync(id, role)`은 role을 `ToFirestoreValue()`로 그대로 `PATCH accounts/{id}/role`에 전달. (`src/MCPhoto.Http/HttpAccountService.cs:196-209`)
- `UserMgmtView.xaml`의 액션 열은 `RoleActionVisibilityConverter`(param `Manage`/`Promote`)로 노출 제어. `Promote`=actor==Admin && target==User, `Manage`=actor.CanManage(target). (`src/MCPhoto.App/Converters/CommonConverters.cs:201-216`)
- 자기 자신·상위 역할 관리 방지 가드는 `DeleteUser`/`ResetUserPassword`에 존재(역할 변경에도 동일 패턴 필요). (`UserMgmtViewModel.cs:63-65,83-84`)

**B. Google SSO**
- 백엔드 `loginWithGoogleEmail(email)`은 **매핑되는 검증 계정만** 반환, 없으면 null → 라우트가 401 일반화. **자동 계정 생성 없음**. (`web/functions/src/services/accounts.ts:258-268`, `routes/auth.ts:137-143`)
- Google id_token 검증 시 `email_verified === true` 강제 → SSO를 통과한 email은 Google이 소유 확인함. (`web/functions/src/services/googleAuth.ts:110-117`)
- `GOOGLE_ALLOWED_HD` 도메인 제한 이미 지원(빈 값=제한 없음). (`config.ts:43-47,127`, `googleAuth.ts:104-108`)
- 클라 `LoginWithGoogleCommand`·`IsGoogleSignInAvailable`(UseBackend && GoogleClientId 설정) 이미 존재. (`LoginGuestViewModel.cs:47-49,96-137`)
- 로그인 View에 "Google로 로그인" 버튼 이미 있으나 `Visibility={Binding IsGoogleSignInAvailable}`로 게이트. (`LoginGuestView.xaml:39-44`)
- **로그인 화면에 회원가입(이메일/비번 self-signup) 경로가 없음** — 계정 생성은 power 전용 AccountView뿐. (`AccountView.xaml:85-109`)
- 디자인 시스템 사용 가능 키: `Button.Primary/Secondary/Ghost/Danger`, `Card`, `Text.H1/H2/Body/Label/Caption`, `Brush.Accent/Success/Danger/Warning`(+`.Surface`), `Radius.S/M/L`, `Toggle`, `Segment`, `Brush.Divider`. (`Themes/Controls.xaml`, `Typography.xaml`, `Brushes.xaml`)
- 2단계 상태머신 UI 참조 패턴 존재: `PasswordResetView`(IsRequestStep/IsConfirmStep + Visibility 스왑). (`Views/PasswordResetView.xaml`)

**C. 이메일 인증 규칙**
- `VERIFY_TTL_SECONDS = 24*60*60`(24시간), `RESET_TTL_SECONDS = 60*60`(1시간), `MAX_CODE_ATTEMPTS = 5`. (`web/functions/src/domain/tokens.ts:103-109`)
- 서버는 verify 만료를 이미 강제: `consumeByCode`/`consumeInternal`이 `isExpired`면 토큰 삭제 + 실패 반환. (`web/functions/src/services/tokens.ts:124-127,163-167`)
- `ensureEmailUnique(email, excludeId?)`는 **생성(createAccount)·이메일변경(setEmail) 시점**에 email 중복이면 409 "이미 사용 중인 이메일입니다." (`accounts.ts:107-113,138-140,285`)
- 계정 생성 시 email이 주어지면 `emailVerified=false`로 저장 후 verify 메일 발송. (`accounts.ts:138-158`)
- `requestPasswordReset`은 `emailVerified===true`인 계정만 실제 토큰 발급, 그 외 no-op(열거 방지 202). (`accounts.ts:349-366`)
- 로그인 후 비밀번호 변경(`changePassword`)은 이메일 인증과 무관하게 동작(본인/파워 위계만 검사). (`accounts.ts:178-192`, `AccountViewModel.ChangePassword`)
- 클라 AccountView 이메일 인증 섹션은 이미 존재(등록/코드입력/재발송), 카운트다운은 없음. (`AccountView.xaml:29-83`, `AccountViewModel.cs:228-338`)
- `IsEmailVerified` 상태로 인증 코드 입력칸을 미인증에만 노출(InverseBoolToVis). (`AccountView.xaml:63`)

**D. 설정 ini 정리**
- SettingsView "고급" 그룹에 `HostingBaseUrl`·`StorageBucket` 편집 TextBox 존재. (`Views/SettingsView.xaml:388-400`)
- `BackendBaseUrl`/`BackendApiKey`는 SettingsView XAML에 없음(ini 전용). (grep 확인: XAML 내 문자열 미존재)
- `SettingsViewModel`은 `HostingBaseUrl`/`StorageBucket`을 LoadSettings에서 읽고 SaveSettings에서 게스트 아닐 때만 기록. (`SettingsViewModel.cs:48,55,189,196,282,293`)
- 앱 런타임은 `Settings.Current.StorageBucket`/`HostingBaseUrl`을 계속 사용(제거 대상은 편집 UI만). — ini 인프라 메모리 [[mcphoto-settings-ini-infra]].

### 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| G1 | 백엔드 `setRole`이 manager→user 강등을 별도 수정 없이 통과한다(위 사실이 실제 배포본에도 유효). | Step B1 (서버 유닛테스트 추가로 확인) |
| G2 | verify TTL을 300초로 낮춰도 `password_reset`(1h)·기존 verify 흐름 테스트가 깨지지 않는다. | Step C1 (서버 테스트) |
| G3 | `ensureEmailUnique`를 생성 시점에서 제거해도 다른 호출부(setEmail 등)에 회귀가 없다. | Step C2 (서버 테스트 + grep) |
| G4 | Google 자동가입 시 생성되는 계정 id 규칙(email local-part 기반)이 `validateAccountId`(3~40자 `[A-Za-z0-9._-]`)를 항상 만족하거나, 불만족 시 폴백이 동작한다. | Step B2 (서버 테스트: 충돌·비정상 local-part 케이스) |
| G5 | 클라 로그인/가입 통합 화면 재설계가 기존 오버레이 네비게이션(NavigateToOverlayAsync/ReturnFromOverlay)과 FocusManager 관례를 깨지 않는다. | Step B6 (headless XAML 회귀 + 수동 확인) |
| G6 | 카운트다운 타이머(DispatcherTimer)를 AccountViewModel/신규 SignUp VM에 추가해도 오버레이 이탈 시 누수 없이 정리된다. | Step C4 (OnLeave/Close에서 Stop 확인) |

---

## 1. 기능 A — 역할 양방향 변경 (manager ↔ user)

### 1.1 현재 → 목표
- **현재**: admin이 `manager 지정` 버튼으로 user→manager 승격만 가능. manager→user 강등 UI 없음.
- **목표**: admin이 **user 행에서 manager로 승격**, **manager 행에서 user로 강등**을 명확한 컨트롤로 수행. 위계·가드 유지.

### 1.2 백엔드/HTTP
- **무변경**. 백엔드 `setRole`·클라 `SetRoleAsync`가 이미 임의 non-admin role 값을 처리(사실 근거 위). manager→user 강등은 role="user" 전달만 하면 통과.
- 단, **회귀 방지 테스트만 추가**(G1): manager→user, user→manager, admin 대상 거부, non-admin actor 거부.

### 1.3 클라 (VM + XAML)
`UserMgmtViewModel`:
- `PromoteToManager`(user→manager) **유지**하되 명칭·게이트 정리. 신규 커맨드 추가:
  - `[RelayCommand] SetUserRole(User? user, ...)` **또는** 두 커맨드 `PromoteToManager`(user→manager) + `DemoteToUser`(manager→user). **권장: 두 커맨드**(파라미터 파싱 단순 + 기존 컨버터 재사용 용이).
- `DemoteToUser` 가드(승격과 대칭):
  ```
  if (user is null || !IsAdmin || user.Role != UserRole.Manager) return;
  if (user.Id == _shell.Session.CurrentUser?.Id) { StatusMessage="자기 계정의 역할은 변경할 수 없습니다."; return; }
  await _accounts.SetRoleAsync(user.Id, UserRole.User);
  await ReloadAsync();
  ```
- **자기 자신 역할 변경 금지 가드**를 승격에도 추가(현재 PromoteToManager에는 자기 방지 없음 — admin이 자기 자신을 대상으로 할 일은 없으나 대칭·안전상 추가).

`UserMgmtView.xaml` "작업" 열:
- 기존 `manager 지정` 버튼(param `Promote`) 유지.
- 신규 `user로 강등` 버튼 추가. Visibility는 **신규 컨버터 파라미터 `Demote`** 로: `actor==Admin && target==Manager`.
  - `RoleActionVisibilityConverter`에 `"Demote"` 분기 추가:
    ```
    "Promote" => actor==Admin && target==User,
    "Demote"  => actor==Admin && target==Manager,
    _         => actor.CanManage(target)   // Manage(기존)
    ```
- **권장 UX 개선(선택)**: 두 버튼이 상호배타(같은 행에 동시 노출 안 됨)이므로 현행 버튼 나열 방식으로 충분. 드롭다운(ComboBox)으로 통합할 수도 있으나, 위계상 불가 대상 필터링·자기 자신 제외 로직이 ComboBox SelectionChanged에 얽히면 복잡도↑. **버튼 2개 방식 채택**(USER-DECISION D-A1, 기본안=버튼).

### 1.4 테스트
- 서버: `setRole` 강등/승격/거부 케이스(신규).
- 클라: `UserMgmtViewModel`에 `Demote_ManagerToUser_CallsSetRole`, `Demote_NonManager_NoOp`, `Demote_Self_NoOp` (기존 VM 테스트 관례 복제).

---

## 2. 기능 B — Google SSO 가입 + 상용 로그인/가입 UX

### 2.1 현재 → 목표
- **현재**: SSO는 등록·검증 계정만 로그인. self-signup 경로 없음. 로그인 화면은 단순 카드 1개.
- **목표**:
  1. Google email에 계정 없으면 **user 역할로 자동 생성**(Google 검증 email → `emailVerified=true`).
  2. 로그인/가입 화면을 **상용 수준**으로 재설계(탭 전환, Google 상단 강조, 인라인 검증).
  3. 이메일/비번 self-signup(비-SSO) 경로 추가.

### 2.2 백엔드 계약 변경

#### B-BE-1. `loginWithGoogleEmail` → 자동 생성
`accounts.ts`의 `loginWithGoogleEmail(email)` 재설계:
```
1. normalized = email.toLowerCase()
2. doc = findByIdOrEmail(normalized)
3. doc 있음:
   - doc.email === normalized 아님 → null (방어)
   - doc.emailVerified === true → LoginResult (기존)
   - doc.emailVerified === false (미검증 충돌):
       → USER-DECISION D-B1. 권장 기본안(B): **미검증 기존 계정을 SSO가 승격**
         (email 소유를 Google이 방금 증명했으므로 emailVerified=true로 마킹 후 로그인).
         대안(A): null(로그인 거부, 관리자 개입). 대안(C): 별도 신규계정 생성(email 유일성 위배 → 불가).
       → 채택: **B** — emailVerified=true 업데이트 후 LoginResult. 근거: Google가 email 소유를 검증했으므로
         "미검증 로컬 계정"보다 신뢰도가 높다. 단 role·password는 기존 계정 것 유지(권한 상승 없음).
4. doc 없음 → 자동 생성:
   - id = deriveAccountId(normalized)  // 아래 규칙
   - createDoc { id, password: <불가한 랜덤 해시>, role: "user", email: normalized, emailVerified: true, createdAt }
   - LoginResult(생성 계정)
```
- **자동 생성 계정의 비밀번호**: SSO 전용 계정은 id/pw 로그인을 못 하도록 **로그인 불가 sentinel**을 저장(권장: `hashPassword(randomBytes(32))` — 아무도 모르는 해시. 평문 매칭 불가·bcrypt 검증 불가). id/pw 로그인 시 항상 실패 → SSO로만 진입. (USER-DECISION D-B2, 기본안=랜덤 해시 sentinel)
- **`deriveAccountId(email)`** (신규 순수 함수, `domain/`에 배치 권장):
  - base = email local-part(`@` 앞)에서 `[A-Za-z0-9._-]` 외 문자를 제거, 소문자, 3~40자로 clamp(3자 미만이면 padding, 40 초과 절단).
  - 충돌 시 `-2`,`-3`… suffix 부여(문서 존재 확인 루프, 40자 초과 시 절단 후 재부여). **email이 문서 id 후보와 다름 주의**: email은 별도 필드로만 unique.
  - **주의(G4)**: local-part가 전부 제거되어 빈 문자열이면 폴백 `g-{uuid 앞 8자}`. `validateAccountId` 규칙(3~40, `[A-Za-z0-9._-]`)을 항상 만족하도록 보정.
- **경합 방지**: 자동 생성은 `create`(문서 부재 시에만)로 원자적 시도, 이미 존재 시 재조회 후 로그인(동시 첫 로그인 2회 대비).

#### B-BE-2. 이메일/비번 self-signup 엔드포인트
로그인 화면에서 비로그인 사용자가 user 계정을 직접 만들 수 있어야 한다. **기존 `POST /accounts`는 Bearer+requirePower** 라서 비로그인 self-signup 불가. → **신규 라우트 추가**:
- `POST /auth/register` (API키 게이트, Bearer 불요):
  - body `{ id, password, email? }`
  - role은 **서버가 "user"로 강제**(클라가 role 못 지정 — 권한 상승 차단).
  - 로직: `validateAccountId`/`validatePassword`/(email 있으면)`validateEmail` → `createAccount(id, password, "user", email, actingRole="user")`.
    - `canCreate("user","user")` = true(자기 역할 생성 허용? — **확인 필요**: `CreatableRoles(User)` = `[]`(빈 배열). 사실 근거 `UserRole.cs:41-46`). → **`createAccount`의 canCreate 게이트가 self-signup을 막음.**
    - 따라서 self-signup은 `createAccount`를 우회하는 전용 경로가 필요: 신규 `registerSelf(id, password, email)` 서비스 함수(역할 고정 "user", canCreate 게이트 없음, id 중복 409, email 있으면 unverified 생성 + verify 메일).
  - 응답: 201 + `{ token?, user }` — **권장: 가입 즉시 로그인(JWT 발급)** 해 UX 매끄럽게. (USER-DECISION D-B3, 기본안=가입 즉시 로그인)
  - **보안(열린 가입)**: `/auth/register`는 API키 게이트 + rate 방어(과길이·형식). 이메일 인증은 옵션(C 규칙과 정합 — 미인증이어도 가입 가능, 단 비번 찾기 불가).

#### B-BE-3. SSO 열린 가입 도메인 제한
- 기본: 열림(`GOOGLE_ALLOWED_HD` 빈 값). 운영에서 사내 한정이 필요하면 `GOOGLE_ALLOWED_HD=회사도메인` 설정 → id_token.hd 불일치 시 401(이미 구현). **문서화만**(설계 §7 보안 노트).

### 2.3 클라 계약 변경 (IAccountService)
- 신규: `Task<User?> RegisterAsync(string id, string password, string? email, CancellationToken ct = default)` — self-signup. HTTP 구현은 `POST auth/register`(bearer:false), 성공 시 응답에 token 있으면 `Session.SignIn`. 레거시 Firebase 구현은 `NotSupportedException`(백엔드 전용) — self-signup은 백엔드 모드에서만.
- `LoginWithGoogleAsync`: **시그니처 무변경**(자동 생성은 서버 책임). 단 성공 시 신규/기존 구분 없이 동일 User 반환.

### 2.4 상용 로그인/가입 UX 상세 레이아웃

**결정: 단일 화면 + 모드 탭(로그인/회원가입) 전환** (별도 상태 신설 없이 `LoginGuestViewModel` 확장 + `AuthMode` enum). PasswordResetView의 Step 스왑 패턴과 동일하게 Visibility로 섹션 전환.

`LoginGuestViewModel` 신규 상태:
- `[ObservableProperty] private AuthMode _mode = AuthMode.SignIn;` (enum: SignIn, SignUp) + `IsSignIn`/`IsSignUp` 파생.
- `[ObservableProperty] private string _signUpEmail`, `_signUpId` (PasswordBox 2개는 code-behind 전달: `SignUpPassword`, `SignUpPasswordConfirm`).
- 인라인 검증 파생 속성(모두 계산형, NotifyPropertyChangedFor로 갱신):
  - `PasswordRuleText`(길이 등 규칙 안내), `PasswordsMatch`, `CanSubmitSignUp`(id 비어있지 않음 && 비번 규칙 통과 && 일치).
- 신규 커맨드: `[RelayCommand] SwitchMode(AuthMode)`(탭), `[RelayCommand] SignUp()`(RegisterAsync 호출 → 성공 시 세션 로그인 + ReturnFromOverlay).

**레이아웃 (LoginGuestView.xaml, Card Width 440 유지)**:
```
[서버 미연결 배너]  (기존 IsServerOffline, 유지)

[탭 헤더]  ─ "로그인" | "회원가입"  (Segment 스타일 재사용, 2-way 토글 → Mode 바인딩)

┌ Google 영역 (IsGoogleSignInAvailable=true일 때만) ─────────────┐
│  [ Google로 계속하기 ]  (Button.Primary 급 강조, 아이콘 텍스트) │
│  ──────────  또는  ──────────   (구분선: Border+TextBlock)      │
└────────────────────────────────────────────────────────────┘

┌ SignIn 섹션 (IsSignIn) ───────────────────────────────────────┐
│  아이디            [TextBox  LoginId]                          │
│  비밀번호          [PasswordBox → Password]                    │
│  {ErrorMessage}                                                │
│  [ 로그인 ]  (Button.Primary, IsDefault)                       │
│  [ 비밀번호를 잊으셨나요? ]  (Ghost, IsBackendMode)            │
└───────────────────────────────────────────────────────────────┘

┌ SignUp 섹션 (IsSignUp) ───────────────────────────────────────┐
│  아이디            [TextBox  SignUpId]  {id 규칙 캡션}         │
│  이메일 (선택)     [TextBox  SignUpEmail]  {인증 안내 캡션}    │
│  비밀번호          [PasswordBox → SignUpPassword]              │
│  비밀번호 확인     [PasswordBox → SignUpPasswordConfirm]       │
│  {PasswordRuleText / 불일치 경고 (인라인, 실시간)}            │
│  {ErrorMessage / 성공 노티}                                    │
│  [ 회원가입 ]  (Button.Primary, IsEnabled=CanSubmitSignUp)     │
└───────────────────────────────────────────────────────────────┘

[ 취소 ]  (Ghost, 항상)   → ReturnFromOverlay (게스트 진입 경로 유지)
```
- **Google 버튼 노출 정책 (USER-DECISION D-B4)**: `IsGoogleSignInAvailable=false`(GoogleClientId 미설정)면 Google 영역 **전체 숨김**(구분선 포함). 기본안=숨김(키오스크 브라우저 봉쇄 배려 + 혼란 방지). 대안=비활성+안내는 채택 안 함(미구성은 사용자 잘못이 아니므로 숨김이 깔끔).
- **인라인 검증**: `UpdateSourceTrigger=PropertyChanged`로 즉시 반영. 비번 규칙은 서버 `validatePassword`(1~200자 비어있지 않음)와 정합하되 클라 UX 규칙은 **최소 4자 권장**(USER-DECISION D-B5, 기본안=4자 이상 안내만, 하드 차단은 서버 규칙 준수). 규칙을 서버보다 엄격히 하면 서버 통과 계정을 클라가 못 만드는 불일치는 없음(가입은 클라가 먼저 검사).
- **탭 전환 시** ErrorMessage/입력 초기화(모드 오염 방지).
- **FocusManager**: SignIn 진입 시 IdTextBox 포커스(기존 유지). SignUp 전환 시 SignUpId 포커스(code-behind에서 Mode 변경 감지 — 순수 뷰 로직).

### 2.5 리소스 키
- 신규 스타일 최소화: 탭은 `Segment` 재사용, "또는" 구분선은 로컬(Grid + `Brush.Divider` Border + TextBlock). 신규 키 필요 시 `Auth.Divider` 등 **접두어 `Auth.`** 로 충돌 방지.

---

## 3. 기능 C — 이메일 인증 규칙

### 3.1 규칙 C1 — 인증 옵션 + 미인증 시 비번 찾기 불가
- **현재 이미 정합**: 계정 생성 email은 옵션(미인증 생성), `requestPasswordReset`은 `emailVerified===true`만 실제 발송(no-op은 열거 방지 202). → **미인증 계정은 재설정 코드가 안 옴 = 사실상 불가.**
- **추가 필요(UX)**: 사용자에게 "미인증이면 재설정 불가, 관리자 강제 변경만 가능"을 **명시**. 위치:
  - AccountView 이메일 섹션(미인증 상태): 안내 문구 추가 — "이메일 인증을 완료해야 비밀번호 찾기(재설정)를 사용할 수 있어요. 미인증 상태에서는 관리자를 통한 비밀번호 강제 변경만 가능합니다." (이미 유사 문구 있음 `AccountView.xaml:39` → 강화).
  - PasswordResetView 요청 단계: 코드 요청 후 202 성공이지만 "가입한 이메일이 인증되지 않았다면 코드가 발송되지 않습니다. 관리자에게 문의하세요." 안내(열거 방지 유지하면서 사용자 교육).
- **관리자 강제 변경**은 이미 `ResetUserPassword`(UserMgmt, "0000"으로 초기화)로 존재 → 재사용.

### 3.2 규칙 C2 — 로그인 후 비번 변경은 인증 불요 / 인증칸은 미인증에만
- **현재 이미 정합**: `changePassword`는 이메일 인증과 무관. AccountView 인증 코드 입력칸은 `IsEmailVerified=false`에만 노출(`AccountView.xaml:63`).
- **추가**: 이메일 인증 섹션 안내를 "비밀번호 변경은 지금 바로 가능합니다. 이메일 인증은 **비밀번호 찾기(재설정)** 를 쓰려는 경우에만 필요합니다." 로 명확화. — VM 변경 없음, XAML 문구만.

### 3.3 규칙 C3 — 인증 타임아웃 5분 (서버 폐기 + 클라 카운트다운)
- **서버(B-BE 무관, C 핵심)**: `VERIFY_TTL_SECONDS`를 `24*60*60` → **`5*60`(300초)** 로 변경. (`tokens.ts:103`)
  - `RESET_TTL_SECONDS`(1h)는 그대로(요구는 verify만). 만료 강제는 이미 `consume*`가 수행 → 300초 초과 시 자동 폐기(코드 변경 불요).
  - 주석의 `(§5.4)` 근거 문구도 5분으로 업데이트.
- **클라 카운트다운**: AccountView 이메일 인증 섹션 + (self-signup 후 인증 유도 시) 5:00 → 0:00 카운트다운 표시.
  - `AccountViewModel`에 `DispatcherTimer _verifyCountdown` + `[ObservableProperty] private string _verifyCountdownText`(mm:ss) + `_verifyDeadline`.
  - 코드 발송(`RegisterEmail`/`ResendEmailVerification`) 성공 시 `StartCountdown(TimeSpan.FromMinutes(5))`. 0 도달 시 정지 + "코드가 만료되었습니다. 재발송하세요." 안내 + 인증 버튼 비활성(선택).
  - **타이머 정리(G6, 누수 방지)**: `Close()`(오버레이 이탈)와 `OnEnterAsync` 재진입 시 `_verifyCountdown?.Stop()`. AccountViewModel은 오버레이 재사용 VM이므로 **진입마다 기존 타이머 정지 후 재구성**. `DispatcherTimer`는 UI 스레드 바인딩이므로 Dispatcher 접근 안전.
  - 카운트다운은 **표시용**(서버가 실제 만료 판정). 클라 시계 오차로 서버가 먼저/나중 만료할 수 있으므로, 만료 후 인증 시도는 서버 응답(false)으로 확정 안내.

### 3.4 규칙 C4 — 이메일 1개당 1계정만 인증 + 메시지
- **요구**: 생성은 옵션 이메일 허용(미인증), **인증(verify) 시점에 "이미 다른 계정이 인증한 이메일"이면 인증 거부** → 메시지 정확히 **"해당 이메일로 생성 가능한 계정 수를 초과하였습니다."**
- **현재와의 충돌**: `ensureEmailUnique`가 **생성/변경 시점**에 email 중복이면 무조건 409(미인증 포함). 요구는 "생성은 미인증이면 중복 허용, 인증 시점에 이미 **인증된** 계정이 있으면 거부".

#### C4 서버 재설계
1. **생성/변경 시점 유일성 완화**: `ensureEmailUnique`를 "이미 **인증 완료(emailVerified=true)** 한 계정이 같은 email을 가지면 409"로 좁힌다(미인증 동일 email은 허용).
   - 재작성:
     ```
     ensureEmailNotVerifiedElsewhere(email, excludeId?):
       snap = where("email","==",email)
       conflict = snap.docs.find(d => d.id !== excludeId && d.data().emailVerified === true)
       if (conflict) throw HttpError.conflict("해당 이메일로 생성 가능한 계정 수를 초과하였습니다.")
     ```
   - 호출부: `createAccount`(email 있을 때)·`setEmail`·`registerSelf`(B) 모두 이 검사로 교체.
   - **메시지 변경**: 기존 "이미 사용 중인 이메일입니다." → "해당 이메일로 생성 가능한 계정 수를 초과하였습니다."
2. **인증(verify) 시점 유일성 강제(핵심)**: `markEmailVerified(userId, verifiedEmail)`에서 verified=true로 마킹하기 **직전**에 재검사:
   ```
   markEmailVerified:
     현재 doc.email === verifiedEmail 확인(기존)
     + 다른 계정 중 email===verifiedEmail && emailVerified===true 존재하면 → false 반환(마킹 거부)
     else update emailVerified=true
   ```
   - `confirmEmailVerificationByCode`/`ByToken`이 `markEmailVerified` false → 라우트가 401 "인증 코드가 올바르지 않거나 만료되었습니다." (현행). **단 이 케이스는 "초과" 사유**이므로 **전용 메시지 분기 필요**:
     - `markEmailVerified`가 3-값(`{ok:true} | {ok:false, reason:"mismatch"|"taken"}`) 반환하도록 확장.
     - 라우트에서 `reason==="taken"` → **409 + "해당 이메일로 생성 가능한 계정 수를 초과하였습니다."** / 그 외 → 기존 401.
   - **경합(동시 2계정이 같은 email 인증)**: Firestore 트랜잭션으로 "다른 verified 없음 확인 + 마킹"을 원자화(권장). 트랜잭션 미도입 시 최소 read-then-write지만 rare race는 허용(USER-DECISION D-C1, 기본안=트랜잭션. 복잡하면 read-then-write + 로그).
3. **클라 메시지 매핑**: `ConfirmEmailVerificationAsync`가 현재 401/400을 `false`로 흡수(코드 불일치 취급). "초과"(409)는 **false가 아니라 사유 노출** 필요 → HTTP 구현에서 409를 `InvalidOperationException(ex.Message)`로 전파, `AccountViewModel.VerifyEmail`이 catch해서 그 메시지 표시.
   - `ConfirmEmailVerificationAsync`의 catch 필터에서 `HttpStatusCode.Conflict`는 흡수하지 말고 `MapToDomainException`으로 전파(→ InvalidOperationException). VM은 이미 `InvalidOperationException` catch 패턴 보유(RegisterEmail 참조).

### 3.5 TTL/규칙 요약표

| 항목 | 현재 | 목표 | 위치 |
|------|------|------|------|
| VERIFY_TTL_SECONDS | 86400 | **300** | `domain/tokens.ts:103` |
| RESET_TTL_SECONDS | 3600 | 유지 | `domain/tokens.ts:106` |
| 생성 시 email 유일성 | 무조건 409 | **verified 계정만 409** | `accounts.ts:ensureEmailUnique` |
| verify 시 유일성 | 없음 | **verified 중복이면 거부(409, "…초과…")** | `accounts.ts:markEmailVerified` |
| 중복 메시지 | "이미 사용 중인 이메일입니다." | "해당 이메일로 생성 가능한 계정 수를 초과하였습니다." | `accounts.ts` |

---

## 4. 기능 D — 설정 ini 정리

### 4.1 현재 → 목표
- **현재**: SettingsView "고급" 그룹에 `HostingBaseUrl`("다운로드 페이지 Base URL")·`StorageBucket`("Storage 버킷") TextBox 편집 UI 존재.
- **목표**: 두 편집 TextBox **제거**. ini/기본값으로만 유지(앱은 계속 읽음). `BackendBaseUrl`/`BackendApiKey`는 이미 UI에 없음(ini 전용) — 그대로.

### 4.2 변경
`SettingsView.xaml` (388-400 "고급" 그룹):
- `다운로드 페이지 Base URL` TextBlock + `HostingBaseUrl` TextBox 제거.
- `Storage 버킷` TextBlock + `StorageBucket` TextBox + 예시 캡션 제거.
- "고급" 그룹이 비면 그룹 제목/GroupDivider도 함께 제거(빈 그룹 방지). — 다른 항목이 고급 그룹에 있는지 확인 후 처리(현재 확인된 것은 이 둘뿐 → 그룹 전체 제거 가능).

`SettingsViewModel.cs`:
- `HostingBaseUrl`/`StorageBucket` `[ObservableProperty]` **유지**(제거 안 함). 이유: 앱 런타임이 `Settings.Current`를 쓰고, VM은 LoadSettings/SaveSettings에서 이 필드를 라운드트립한다. **SaveSettings에서 이 두 필드를 ini에 그대로 다시 써야 값이 보존**된다(제거하면 저장 시 클로버 위험).
  - **정밀 확인 필요**: SaveSettings는 `s.HostingBaseUrl = HostingBaseUrl`(282줄)로 VM값→ini. VM값은 LoadSettings에서 ini→VM(189/196줄)으로 채워지므로 **편집 UI가 없어도 라운드트립은 원값 보존**(로드한 값을 그대로 되씀). → **VM 변경 불필요**, XAML만 제거.
  - 게이트(`if (!IsGuest)`)도 그대로 두면 됨(게스트는 미기록=원값 보존, 로그인=원값 되씀).
- **결론**: VM 로직 무변경, XAML의 TextBox 2개(+ 소속 그룹) 제거만.

### 4.3 회귀
- `SettingsViewModelTests`의 라운드트립 테스트가 `HostingBaseUrl`/`StorageBucket`을 검사하면 **그대로 통과**(VM 필드 유지). 편집 UI 제거는 XAML만이라 VM 테스트 무영향.
- **주의**: headless XAML 회귀 테스트가 있으면 "고급" 그룹 제거 후에도 SettingsView가 로드되는지 확인.

---

## 5. 백엔드 계약 변경 요약 (js 구현자용)

| 엔드포인트/함수 | 변경 | 요지 |
|-----------------|------|------|
| `POST /auth/register` | **신규** | API키 게이트, `{id,password,email?}` → role="user" 강제 self-signup. 성공 201 `{token?, user}`(가입 즉시 로그인 권장). |
| `registerSelf(id,password,email)` | **신규 서비스** | canCreate 게이트 없이 user 고정 생성, id 중복 409, email 있으면 unverified + verify 메일. |
| `loginWithGoogleEmail(email)` | **변경** | 계정 없으면 user 자동생성(emailVerified=true, pw=랜덤 sentinel). 미검증 기존계정은 승격(emailVerified=true) 후 로그인. |
| `deriveAccountId(email)` | **신규 순수함수** | local-part → `[A-Za-z0-9._-]` 3~40자, 충돌 suffix, 폴백 `g-{uuid8}`. |
| `VERIFY_TTL_SECONDS` | **변경** | 86400 → 300. |
| `ensureEmailUnique` → `ensureEmailNotVerifiedElsewhere` | **변경** | verified 계정만 중복 판정. 메시지 "…생성 가능한 계정 수를 초과…". |
| `markEmailVerified` | **변경** | verified 중복 존재 시 `{ok:false, reason:"taken"}` 반환. verify 라우트가 409 + 초과 메시지. |
| `setRole` | **무변경** | (강등 이미 지원; 테스트만 추가) |

**DTO 신규(js)**: `RegisterRequest {id,password,email?}`, verify confirm 응답에 taken 사유 반영은 status code(409)로.

---

## 6. 클라 변경 요약 (wpf 구현자용)

| 파일 | 변경 |
|------|------|
| `MCPhoto.Core/Accounts/IAccountService.cs` | `RegisterAsync(id,password,email?,ct)` 추가. |
| `MCPhoto.Http/HttpAccountService.cs` | `RegisterAsync` → `POST auth/register`(bearer:false), token 있으면 SignIn. `ConfirmEmailVerificationAsync` 409는 흡수 말고 전파(초과 메시지). |
| `MCPhoto.Http/Dto/*` | `RegisterRequest` DTO 추가. |
| `MCPhoto.Firebase/AccountService.cs` | `RegisterAsync` → `NotSupportedException`(백엔드 전용). |
| `MCPhoto.App/ViewModels/LoginGuestViewModel.cs` | `AuthMode` enum, `Mode`/`IsSignIn`/`IsSignUp`, SignUp 입력/검증 파생, `SwitchMode`/`SignUp` 커맨드. |
| `MCPhoto.App/Views/LoginGuestView.xaml(.cs)` | 탭 + Google 강조 + SignIn/SignUp 섹션 재구성. SignUp PasswordBox 2개 code-behind 전달, Mode 변경 시 포커스. |
| `MCPhoto.App/ViewModels/UserMgmtViewModel.cs` | `DemoteToUser` 커맨드 + 자기 방지 가드(승격에도). |
| `MCPhoto.App/Views/UserMgmtView.xaml` | `user로 강등` 버튼(param `Demote`). |
| `MCPhoto.App/Converters/CommonConverters.cs` | `RoleActionVisibilityConverter`에 `Demote` 분기. |
| `MCPhoto.App/ViewModels/AccountViewModel.cs` | 5분 카운트다운(DispatcherTimer + mm:ss + 정리), verify 409(InvalidOperationException) 메시지 표시. |
| `MCPhoto.App/Views/AccountView.xaml` | 카운트다운 표시, C1/C2 안내 문구 강화. |
| `MCPhoto.App/Views/SettingsView.xaml` | "고급" HostingBaseUrl/StorageBucket TextBox 제거. |
| `MCPhoto.App/Views/PasswordResetView.xaml` | (선택) 미인증 안내 문구 추가. |

**DI**: `RegisterAsync`는 기존 `IAccountService` 확장이라 DI 신규 등록 없음. `IGoogleSignInService`는 이미 등록됨.

---

## 7. 보안 노트

1. **SSO 열린 가입**: `/auth/google` 자동 생성은 Google이 `email_verified=true`로 검증한 email에만 발생(googleAuth.ts 강제). 임의 email 위조 불가.
2. **도메인 제한**: 사내 한정 필요 시 `GOOGLE_ALLOWED_HD=<도메인>`(id_token.hd 대조). 기본 열림. env로만 제어(코드 무변경).
3. **self-signup role 고정**: `/auth/register`는 서버가 role="user" 강제. 클라가 role 지정 못 함(권한 상승 차단). 파워 계정 생성은 여전히 Bearer+requirePower `/accounts`.
4. **SSO 계정 비번**: 랜덤 sentinel 해시 저장 → id/pw 로그인 불가. SSO 경로로만 진입.
5. **ApiKey 비노출**: `BackendApiKey`는 ini 전용(UI 없음) 유지. D 변경에서 UI 추가 금지. `HostingBaseUrl`/`StorageBucket`은 자격이 아니라 UI 제거는 UX 정리 목적.
6. **열거 방지 유지**: register 실패(id 중복)는 409로 사유 노출되나(가입 UX 필수), 로그인/재설정/verify request는 기존 일반화(401/202) 유지.
7. **verify 5분 단축**: 브루트포스 창 축소(MAX_CODE_ATTEMPTS=5와 결합). 만료 토큰 자동 폐기 이미 동작.

---

## 8. 구현 단계 (WBS 블루프린트)

> 백엔드(js) / 클라(wpf) 작업으로 분리. 오케스트레이터가 나눠 지시 가능.
> 검증 명령: 백엔드 `cd web/functions && npm run build && npm test`, 클라 `build-verify` 스킬 또는 `dotnet build MCPhoto.sln` + `dotnet test`.

### Step BE-1: 역할 강등 서버 회귀 테스트 (백엔드)
- **Context Brief**: 백엔드 `setRole`은 user↔manager 양방향을 이미 처리하나(admin 대상·admin 지정만 차단), 강등 경로에 테스트가 없다. UI(A) 확장 전 서버 동작을 못박는다.
- **대상 파일**: `web/functions/src/__tests__/accounts.test.ts`(또는 setRole 테스트 파일).
- **선행 조건**: 없음.
- **구현 내용**: `setRole`에 대해 (1) admin이 manager→user 강등 성공, (2) admin이 user→manager 승격 성공, (3) admin 대상 거부, (4) role="admin" 지정 거부, (5) non-admin actor 거부 테스트 추가.
- **검증 명령**: `cd web/functions && npm test -- accounts`
- **완료 기준**:
  - [관측] 5개 테스트 신규 통과, 강등 케이스가 Firestore role 필드를 "user"로 업데이트.
  - [non-goal] `setRole` 소스 로직 무변경(테스트만 추가).
  - [trigger] 테스트 실행 시에만; 프로덕션 동작 변화 없음.
- **롤백**: 테스트 파일 변경 revert.
- [ ] 완료

### Step BE-2: SSO 자동 생성 + deriveAccountId (백엔드)
- **Context Brief**: `loginWithGoogleEmail`이 매핑 계정 없으면 null. 요구는 없으면 user 자동 생성(emailVerified=true), 미검증 기존계정은 승격. 계정 id는 email local-part에서 파생.
- **대상 파일**: `web/functions/src/services/accounts.ts`, 신규 `web/functions/src/domain/accountId.ts`(deriveAccountId 순수함수), `web/functions/src/__tests__/*`.
- **선행 조건**: 없음.
- **구현 내용**: (1) `deriveAccountId(email)` 순수함수(§2.2 규칙, validateAccountId 만족 보장, 폴백 `g-{uuid8}`). (2) `loginWithGoogleEmail`: 계정 없음→`create`로 원자 생성(user/verified/sentinel pw), 미검증 기존→emailVerified=true 승격 후 로그인, 검증 기존→기존. (3) 충돌 시 suffix, 동시 생성 대비 재조회.
- **검증 명령**: `cd web/functions && npm run build && npm test`
- **완료 기준**:
  - [관측] 신규 email → user/emailVerified=true 계정 생성 + LoginResult; 미검증 기존 → verified 승격; local-part 충돌 → `-2` suffix id; 빈 local-part → `g-` 폴백. 테스트 통과.
  - [non-goal] 검증된 기존 계정 로그인 경로·role은 불변(권한 상승 없음). id/pw 로그인은 sentinel pw로 여전히 불가.
  - [trigger] `/auth/google`에서 Google 검증 통과 email이 매핑 안 될 때만 생성.
- **롤백**: `loginWithGoogleEmail` 이전 버전 복원 + accountId.ts 삭제.
- [ ] 완료

### Step BE-3: self-signup 라우트 + registerSelf (백엔드)
- **Context Brief**: 로그인 화면 회원가입은 비로그인이므로 Bearer 필수 `/accounts`를 못 쓴다. API키 게이트 전용 `/auth/register`(role=user 강제)를 추가한다.
- **대상 파일**: `web/functions/src/routes/auth.ts`, `web/functions/src/services/accounts.ts`(registerSelf), `web/functions/src/__tests__/*`.
- **선행 조건**: BE-4(유일성 완화)와 독립이나 email 검사 정합 위해 BE-4 후 병합 권장.
- **구현 내용**: `registerSelf(id,password,email?)` — id 중복 409, canCreate 게이트 없음, role="user" 고정, email 있으면 unverified + verify 메일. 라우트 `POST /auth/register`(requireApiKey, validateAccountId/Password/(email)) → 201 `{token, user}`(JWT 발급).
- **검증 명령**: `cd web/functions && npm run build && npm test`
- **완료 기준**:
  - [관측] `/auth/register {id,password}` → 201 user(role=user) + JWT; email 포함 시 verify 메일 발송(log sender); 중복 id → 409.
  - [non-goal] role은 body로 지정 불가(항상 user); `/accounts` power 경로 불변.
  - [trigger] API키 헤더 있는 POST /auth/register만.
- **롤백**: 라우트·서비스 함수 revert.
- [ ] 완료

### Step BE-4: 이메일 유일성 완화 + verify 시점 검사 + TTL 300 (백엔드)
- **Context Brief**: 요구 = 생성은 미인증 중복 허용, 인증 시점에 이미 verified인 계정 있으면 거부("…초과…"), verify TTL 5분.
- **대상 파일**: `web/functions/src/services/accounts.ts`, `web/functions/src/domain/tokens.ts`, `web/functions/src/routes/auth.ts`, `web/functions/src/__tests__/*`.
- **선행 조건**: 없음(BE-3와 email 정책 공유).
- **구현 내용**: (1) `ensureEmailUnique`→`ensureEmailNotVerifiedElsewhere`(verified만 409, 메시지 "해당 이메일로 생성 가능한 계정 수를 초과하였습니다."), 호출부 교체(create/setEmail/registerSelf). (2) `markEmailVerified`가 verified 중복이면 `{ok:false,reason:"taken"}`, verify 라우트에서 taken→409+초과메시지. (3) `VERIFY_TTL_SECONDS=300`. (4) 경합은 트랜잭션(권장) 또는 read-then-write.
- **검증 명령**: `cd web/functions && npm run build && npm test`
- **완료 기준**:
  - [관측] 같은 email 미인증 2계정 생성 성공; 첫 계정 verify 성공; 둘째 verify → 409 "…초과…"; verify 토큰 300초 후 만료.
  - [non-goal] password_reset TTL(1h)·기존 reset 흐름 불변; 미인증 중복 생성은 허용.
  - [trigger] verify confirm 시점에만 유일성 강제.
- **롤백**: accounts.ts/tokens.ts/auth.ts 해당 diff revert.
- [ ] 완료

### Step W-1: IAccountService.RegisterAsync + HTTP/Firebase 구현 (클라)
- **Context Brief**: self-signup(BE-3)을 클라에서 호출할 계약. HTTP는 `POST auth/register`, 레거시는 미지원.
- **대상 파일**: `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Http/HttpAccountService.cs`, `src/MCPhoto.Http/Dto/*.cs`(RegisterRequest), `src/MCPhoto.Firebase/AccountService.cs`, `tests/MCPhoto.Tests/Http/HttpAccountServiceTests.cs`.
- **선행 조건**: BE-3(엔드포인트 존재) — 클라 테스트는 mock으로 독립 가능.
- **구현 내용**: `RegisterAsync(id,password,email?,ct)` 인터페이스 + XML doc. HTTP: `SendJsonAsync<LoginResponse>(POST auth/register, bearer:false)`, token 있으면 `Session.SignIn`. `ConfirmEmailVerificationAsync` 409 흡수 제거→전파. Firebase: `NotSupportedException`. RegisterRequest DTO(no BOM).
- **검증 명령**: `dotnet build src/MCPhoto.Http/MCPhoto.Http.csproj` + `dotnet test tests/MCPhoto.Tests --filter HttpAccountService`
- **완료 기준**:
  - [관측] 빌드 통과; RegisterAsync mock 테스트 통과; 409 verify가 InvalidOperationException으로 전파되는 테스트 통과.
  - [non-goal] 기존 LoginAsync/CreateAsync 시그니처·동작 불변.
  - [trigger] 명시적 RegisterAsync 호출 시에만 HTTP 발생.
- **롤백**: 인터페이스/구현/DTO/테스트 diff revert.
- [ ] 완료

### Step W-2: 역할 양방향 변경 UI (클라)
- **Context Brief**: admin이 manager↔user 양방향 변경. 서버(BE-1)·SetRoleAsync 이미 지원. VM+XAML+컨버터만.
- **대상 파일**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`, `src/MCPhoto.App/Views/UserMgmtView.xaml`, `src/MCPhoto.App/Converters/CommonConverters.cs`, `tests/MCPhoto.Tests/*UserMgmt*`.
- **선행 조건**: 없음(BE-1 독립).
- **구현 내용**: `DemoteToUser(User?)` 커맨드(가드: IsAdmin && Role==Manager && 자기 아님 → SetRoleAsync(User) → Reload). `PromoteToManager`에 자기 방지 가드 추가. 컨버터 `Demote` 분기(actor==Admin && target==Manager). XAML `user로 강등` 버튼(Button.Ghost 또는 Secondary, MultiBinding param `Demote`).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests --filter UserMgmt` + headless XAML 로드 테스트.
- **완료 기준**:
  - [관측] manager 행에 `user로 강등` 노출·클릭 시 SetRoleAsync(User) 호출·목록 갱신; user 행엔 미노출; 강등 성공 테스트 통과.
  - [non-goal] manager/user가 아닌 대상·비-admin actor엔 강등 버튼 미노출; 삭제/pw초기화 버튼 노출 규칙 불변; 자기 자신 역할 변경 no-op.
  - [trigger] admin이 manager 행의 강등 버튼 클릭 시에만 role 변경.
- **롤백**: VM/XAML/컨버터 diff revert.
- [ ] 완료

### Step W-3: 상용 로그인/가입 UX 재설계 (클라)
- **Context Brief**: 로그인 화면을 탭(로그인/회원가입) + Google 강조 + 인라인 검증으로 재설계. self-signup은 W-1의 RegisterAsync 사용. Google 자동가입은 서버(BE-2) 처리라 클라 커맨드 변경 없음(버튼 노출만).
- **대상 파일**: `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs`, `src/MCPhoto.App/Views/LoginGuestView.xaml(.cs)`, (필요 시)`src/MCPhoto.App/Themes/Controls.xaml`(Auth.Divider 등), `tests/MCPhoto.Tests/*Login*`.
- **선행 조건**: W-1(RegisterAsync).
- **구현 내용**: `AuthMode` enum + Mode/IsSignIn/IsSignUp; SignUp 입력(SignUpId/SignUpEmail + PasswordBox 2개 code-behind); 파생 검증(PasswordsMatch/CanSubmitSignUp/PasswordRuleText); `SwitchMode`/`SignUp` 커맨드; §2.4 레이아웃(탭=Segment, Google 영역 IsGoogleSignInAvailable 게이트+구분선, SignIn/SignUp Visibility 스왑, 취소=ReturnFromOverlay). code-behind: PasswordBox 전달 + Mode 변경 시 포커스(순수 뷰 로직).
- **검증 명령**: `dotnet build` + headless XAML 로드 테스트(`WpfMergedDictionary` 관례 [[wpf-merged-dict-staticresource]]) + `dotnet test --filter Login`.
- **완료 기준**:
  - [관측] 탭 전환 시 SignIn/SignUp 섹션 스왑; SignUp 입력 완료 시 회원가입 버튼 활성·클릭 시 RegisterAsync 호출·성공 시 세션 로그인+복귀; Google 영역은 IsGoogleSignInAvailable=true에만 노출; 비번 불일치 시 인라인 경고.
  - [non-goal] IsGoogleSignInAvailable=false면 Google 버튼·구분선 완전 숨김; 게스트 취소(ReturnFromOverlay) 경로 유지; 기존 LoginCommand/ForgotPassword 동작 불변; PasswordBox 값 바인딩 미사용(code-behind만).
  - [trigger] 회원가입은 회원가입 버튼 클릭 시에만(입력 중 서버 호출 없음); 탭 전환은 탭 클릭 시에만.
- **롤백**: LoginGuestView(.cs)/VM/테마 diff revert.
- [ ] 완료

### Step W-4: 이메일 인증 5분 카운트다운 + 규칙 안내/메시지 (클라)
- **Context Brief**: verify 5분(BE-4)에 맞춰 클라 카운트다운 표시, C1/C2 안내 강화, verify 409("…초과…") 메시지 표시.
- **대상 파일**: `src/MCPhoto.App/ViewModels/AccountViewModel.cs`, `src/MCPhoto.App/Views/AccountView.xaml`, (선택)`src/MCPhoto.App/Views/PasswordResetView.xaml`, `tests/MCPhoto.Tests/*Account*`.
- **선행 조건**: W-1(409 전파), BE-4(서버 300초/409).
- **구현 내용**: `DispatcherTimer _verifyCountdown` + `VerifyCountdownText`(mm:ss) + deadline; RegisterEmail/Resend 성공 시 5분 시작, 0 도달 시 정지+만료 안내; `OnEnterAsync`/`Close`에서 `Stop()`(누수 방지). VerifyEmail의 InvalidOperationException(409) catch→메시지 표시. XAML: 카운트다운 TextBlock(미인증 코드 섹션 내), C1/C2 안내 문구(§3.1/§3.2). PasswordResetView 요청 단계 미인증 안내(선택).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests --filter Account` + headless XAML.
- **완료 기준**:
  - [관측] 코드 발송 후 5:00→0:00 카운트다운; 0에서 만료 안내; verify 409 시 "…생성 가능한 계정 수를 초과…" 표시; 오버레이 이탈 후 타이머 정지.
  - [non-goal] 인증 완료(IsEmailVerified) 상태에선 카운트다운·코드칸 미노출; 로그인 후 비번 변경은 인증 없이 동작 유지; 타이머 누수 없음(재진입 시 재구성).
  - [trigger] 카운트다운은 코드 발송(등록/재발송) 성공 시에만 시작.
- **롤백**: AccountViewModel/AccountView diff revert.
- [ ] 완료

### Step W-5: 설정 ini 편집 UI 제거 (클라)
- **Context Brief**: SettingsView "고급"의 HostingBaseUrl/StorageBucket TextBox 제거(값은 ini 전용 유지). VM 라운드트립은 그대로라 값 보존됨.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`, `tests/MCPhoto.Tests/*Settings*`.
- **선행 조건**: 없음.
- **구현 내용**: 388-400 "고급" 그룹의 두 TextBlock+TextBox(+예시 캡션) 제거. 그룹에 이 둘만 있으면 GroupTitle "고급"·GroupDivider도 제거. VM/모델 무변경.
- **검증 명령**: `dotnet build` + headless XAML 로드 + `dotnet test --filter Settings`(라운드트립 보존 확인).
- **완료 기준**:
  - [관측] SettingsView에 두 TextBox 미표시; 저장→재로드 시 ini의 HostingBaseUrl/StorageBucket 값 불변(라운드트립 테스트 통과).
  - [non-goal] `Settings.Current.StorageBucket/HostingBaseUrl` 런타임 사용처 불변; BackendBaseUrl/BackendApiKey는 이미 UI 없음 유지; VM 필드 제거 안 함.
  - [trigger] 없음(순수 UI 제거).
- **롤백**: SettingsView.xaml diff revert.
- [ ] 완료

---

## 9. USER-DECISION 요약 (기본안 채택, 필요 시 변경)

| ID | 결정 | 기본안(채택) | 대안 |
|----|------|--------------|------|
| D-A1 | 역할 변경 컨트롤 형태 | 버튼 2개(승격/강등) | ComboBox 드롭다운 |
| D-B1 | SSO 시 미검증 기존 계정 | 승격(emailVerified=true) 후 로그인 | 거부(null) |
| D-B2 | SSO 자동생성 계정 비번 | 랜덤 sentinel 해시(id/pw 불가) | 없음(null 필드) |
| D-B3 | self-signup 후 처리 | 가입 즉시 로그인(JWT) | 로그인 화면 복귀 |
| D-B4 | Google 미구성 시 버튼 | 완전 숨김 | 비활성+안내 |
| D-B5 | 클라 비번 규칙 | 4자 이상 안내(하드 차단은 서버 규칙) | 서버와 동일(1자+) |
| D-C1 | verify 유일성 경합 | Firestore 트랜잭션 | read-then-write+로그 |

---

## 10. 완결성 게이트 (자체 검사)

- [x] 검증된 사실 / 미검증 가정 분리 (§0)
- [x] 모든 가정(G1~G6)에 검증 단계 매핑 (G1→BE-1, G2→BE-4, G3→BE-4, G4→BE-2, G5→W-3, G6→W-4)
- [x] 모든 단계 7필드 채움 (BE-1~4, W-1~5)
- [x] 완료 기준 관측 3문(UI 단계 non-goal·trigger 포함: W-2/W-3/W-4/W-5)
- [x] 검증 명령 자동 실행 가능(npm test / dotnet build·test / build-verify)
- [x] 인코딩·LF 보존 명시(신규 .cs/.ts no BOM)
