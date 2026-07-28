# MCPhoto 이터레이션14 설계 — 설정 진입 PIN 게이트 (wpf-it14-settings-pin-gate-design.md)

> 대상: MCPhoto (WPF / .NET 8, MVVM=CommunityToolkit.Mvvm) + Firebase Cloud Functions 백엔드(Express/TS)
> 루트: `E:\Study\photobooth`
> 성격: **설계 문서(코드 구현 금지)**. 구현 단계 WBS는 §11에 포함.
> 선행 문서: `docs/design/wpf-it13-temp-user-role-design.md`, `docs/design/wpf-google-sso-design.md`, `docs/design/wpf-accounts-email-verification-design.md`, `docs/design/wpf-auth-ux-and-account-rules-design.md`

---

## 0. 개요

설정창 진입 게이트를 **계정 인증 방식에 따라 분기**한다.

| 계정 유형 | 현재(문제) | it14(목표) |
|-----------|-----------|-----------|
| 비밀번호(password) 계정 | 비밀번호 재확인 | **비밀번호 재확인(현행 유지)** |
| SSO 자동생성 계정 | 비밀번호 재확인 → **불가**(sentinel 해시라 아무도 모름) | **전용 PIN 확인** |

### 문제의 뿌리

`OpenSettings`가 `VerifyPasswordAsync(id, password)`로 재인증하는데(`AppShellViewModel.cs:383`), SSO 자동생성 계정은 비밀번호가 **랜덤 sentinel 해시**(`accounts.ts:285-287` `makeSentinelPasswordHash`)라서 id/pw 로그인·비번 재확인이 **원천적으로 불가능**하다. 그 결과 SSO 로그인 운영자는 설정에 못 들어간다. 이를 **전용 PIN**으로 해결한다.

### 확정 스펙(사용자 승인)

1. **게이트 분기**: SSO 계정 → PIN 입력, 비SSO(비번) 계정 → 기존 비밀번호 입력(현행 유지).
2. **SSO 계정 최초 설정 진입 시 PIN 설정 필수**: PIN 미설정 상태면 새 PIN 생성 강제(현재 PIN 확인 없이).
3. **비밀번호 변경 창에서 현재 PIN 확인 후 본인 PIN 변경** 가능.
4. **PIN 변경 권한 매트릭스**(위계 = `CanManage` 계열):
   - admin·manager → user/TempUser 계정 PIN 재설정 가능.
   - admin → manager 포함 모든 하위 계정 PIN 재설정 가능(admin이 최상위).
   - 자신보다 낮은 위계 대상 PIN 재설정 = **대상 현재 PIN 불요, 권한 기반**(비번 재설정과 동형).

### 이 이터레이션의 최우선 제약

**PIN 게이트가 "SSO 계정이 설정에 들어갈 유일한 문"이 된다.** 따라서 (a) 게이트 분기가 계정 유형을 정확히 판정해야 하고, (b) PIN 미설정 SSO 계정이 설정을 영구히 못 여는 데드락이 없어야 하며, (c) fail-closed 원칙(현행 비번 게이트와 동일)을 유지해 잘못된 통과(fail-open)를 만들지 않아야 한다.

---

## 1. 검증된 사실 (verified facts — 근거 file:line)

### 1.1 현행 설정 진입 게이트

- 진입점 `OpenSettings`(`AppShellViewModel.cs:367-387`): `_session.CurrentUser`가 null이 아니면(로그인 상태) `IPasswordPromptDialogService.Prompt(pw => account.VerifyPasswordAsync(uid, pw))` 호출. **게스트(null)는 무가드**. 취소/불일치면 `NavigateToOverlayAsync(AppState.Settings)` 미도달.
- fail-closed 방어: prompt/account 서비스가 DI에 없으면(`prompt is null || account is null`) 재인증 없이 진입 차단(`:379-380`).
- 다이얼로그 서비스: `IPasswordPromptDialogService.Prompt(Func<string, Task<bool>> verifyAsync)` → bool(`IPasswordPromptDialogService.cs:17`). 구현 `PasswordPromptDialogService`가 `PasswordPromptWindow`를 모달로 띄움(`PasswordPromptDialogService.cs:11-18`).
- `PasswordPromptWindow`(`PasswordPromptWindow.xaml.cs`): `PasswordBox` 입력 → `_verify(Pw.Password)` 성공 시 `DialogResult=true`, 실패는 인라인 오류(창 안 닫음), 네트워크 오류도 **fail-closed**(`DialogResult` 미설정, `:41-45`). DI 등록 `AddSingleton<IPasswordPromptDialogService, PasswordPromptDialogService>`(`ServiceRegistration.cs:43`).

### 1.2 인증 서비스 계약

- `IAccountService`(`IAccountService.cs`): `VerifyPasswordAsync(id, password)`(`:18`) — 백엔드는 `/auth/login` 재사용(세션 갱신 없음), 401→false, 네트워크 오류→예외(fail-closed 신호). `ChangePasswordAsync(id, newPassword)`(`:44`). `LoginWithGoogleAsync(...)`(`:29`) — SSO는 HTTP 전용, 레거시는 `NotSupportedException`.
- HTTP 구현 `HttpAccountService`(`HttpAccountService.cs`): `VerifyPasswordAsync`(`:59-78`)는 `/auth/login` POST, 401→false. `ChangePasswordAsync`(`:176-189`)는 `PATCH /accounts/{id}/password`. `ToUser(UserResponse)`(`:370-382`)가 서버 응답을 도메인 `User`로 매핑 — **여기서 새 필드가 흐른다**.

### 1.3 SSO 자동생성 계정 = sentinel 비번

- `makeSentinelPasswordHash`(`accounts.ts:285-287`): `hashPassword(randomBytes(32).toString("base64url"))` — 아무도 모르는 랜덤 값의 bcrypt 해시. id/pw 로그인과 절대 매칭 안 됨.
- `createGoogleAccount`(`accounts.ts:355-379`): 계정 없을 때 `password: sentinel, role: "user", emailVerified: true`로 자동 생성. **이 함수가 "SSO 대상" 계정을 만드는 유일한 지점** → 여기서 SSO 신호를 세팅해야 한다.
- `loginWithGoogleEmail`(`accounts.ts:309-330`): 검증된 email로 (기존 승격 | 신규 생성 | 경합 재조회) 후 `LoginResult` 반환. `loginExistingGoogleAccount`(`:333-348`)는 미검증 기존 계정을 승격(role/pw 불변).
- **주의**: SSO는 "email 신원"만 신뢰한다. 기존 **비번 계정에 같은 email이 등록되어 있으면**, SSO 로그인이 그 기존 계정으로 매핑될 수 있다(`findByEmailField` → `loginExistingGoogleAccount`). 즉 "SSO로 로그인했다 ≠ 그 계정이 sentinel 비번이다". → §3.2 판별 신호 논의의 핵심.

### 1.4 역할 위계(PIN 매트릭스가 재사용)

- C# `UserRole`(`UserRole.cs:4-20`): `TempUser(0) < User(1) < Manager(2) < Admin(3)`. 서수 아닌 `ManageRank` switch로 비교.
- `CanManage(actingRole, targetRole)`(`UserRole.cs:89-90`): `ManageRank(target) <= ManageRank(acting)` — 자신과 같거나 낮은 위계만 관리. **PIN 재설정 권한이 이것과 정확히 동형**(비번 재설정·삭제와 같은 계열).
- TS 이식 `roles.ts`: `canManage`(`:91-93`) — C#과 1:1. `MANAGE_RANK{temp_user:0,user:1,manager:2,admin:3}`(`:18-23`).
- 서버 `changePassword`(`accounts.ts:197-211`): `isSelf || canManage(actor.role, targetRole)` 게이트. **PIN 재설정 서버 로직의 참조 원형**.
- 클라 `RoleChangePolicy.AssignableRoles`(`RoleChangePolicy.cs:18-27`) — UI 필터의 참조 패턴.

### 1.5 서버 구조

- `users/{id}` 문서(`dto.ts:13-28`): `{id, password(bcrypt), role, createdAt, email?, emailVerified?, qrUsedCount?}`. **`pinHash`/신호 필드 없음** → 추가 대상.
- `UserResponse`(`dto.ts:90-98`): `{id, role, createdAt, email, emailVerified}` — 비번/해시 절대 미포함. **PIN 신호(authMethod/hasPin)를 여기에 노출**(pinHash 자체는 절대 미노출).
- 라우트 인증: `requireApiKey`(비로그인 게이트), `requireBearer`(로그인 JWT→`req.principal{id,role}`), `requirePower`/`requireAdmin`(`http/auth.ts`). JWT claims = `{sub:id, role}`만(`jwt.ts:34`).
- `accounts.ts` 라우트(`accounts.ts:26-146`): `POST /accounts`(파워), `GET /accounts`(파워), `GET /accounts/me/qr-usage`, `PATCH /:id/password`(본인/파워), `PATCH /:id/email`, `DELETE /:id`(파워), `PATCH /:id/role`(파워). **PIN 라우트를 여기 또는 `auth.ts`에 추가**.
- 비번 검증 `verifyPassword(plain, stored)`(`password.ts:35-45`) + `hashPassword`(`:19`) — **PIN 해시도 동일 bcrypt 인프라 재사용**(별도 라이브러리 불요).
- 입력 검증 `validation.ts`: `validateVerificationCode`(`:60-65`)는 `^\d{6}$`. **PIN 형식 검증(`validatePin`)의 참조**.

### 1.6 클라 비번 변경·타 계정 관리 UI

- 비번 변경: `AccountViewModel`(`AccountViewModel.cs`) `AccountMode.PasswordChange` 섹션 + `ChangePassword` 커맨드(`:195-224`). PasswordBox는 바인딩 불가 → code-behind 전달(`NewPassword`/`ConfirmPassword` 일반 프로퍼티, `:59-60`). **PIN 변경 UI를 이 섹션에 추가**.
- 타 계정 관리: `UserMgmtViewModel`(`UserMgmtViewModel.cs`) — `Rows`(계정별 `UserRowViewModel`), `DeleteUser`/`ResetUserPassword`/`ApplyRoleChange` 커맨드. `ResetUserPassword`(`:111-128`)가 `ChangePasswordAsync(id, "0000")` + `CanManage` 가드(`:117`). **PIN 재설정(타 계정) UI를 여기 추가**(비번 초기화와 동형).
- 세션 단일 소스: `SessionContext.CurrentUser`(`SessionContext.cs:14`, `private set`) — `Login(User)`/`Logout()`로만 변경. `User` 도메인 모델(`User.cs:7-23`): `{Id, Password, Role, CreatedAt, Email?, EmailVerified}`. **PIN 신호 필드(AuthMethod/HasPin)를 User에 추가**.

### 1.7 레거시(Firebase 직결) 경로

- `MCPhoto.Firebase.AccountService`(`AccountService.cs`): `VerifyPasswordAsync`(`:54`)는 `LoginAsync` 재사용. SSO 미지원(SSO 버튼은 백엔드 모드에서만 노출 — `LoginGuestViewModel.cs:106-108` `IsGoogleSignInAvailable`). → **레거시엔 SSO 계정이 없으므로 PIN 불필요, 기존 비번 게이트 유지**.

### 1.8 테스트 자산(무회귀 기준)

- 클라 xUnit: `tests/MCPhoto.Tests/`(다수) + `tests/MCPhoto.Tests/Http/`. 관련: `AccountTests.cs`(VerifyPasswordAsync `:63-77`), `HttpAccountServiceTests.cs`(`:531-599`), `UserMgmtViewModelTests.cs`, `RoleManagementTests.cs`, `AccountViewModelEmailTests.cs`.
- 서버 jest: `web/functions/src/__tests__/`(`accounts.test.ts`, `roles.test.ts`, `validation.test.ts`, `password.test.ts` 등).
- 무회귀 목표: **기존 클라 646 / 서버 185 테스트 전부 유지**(팀리드 명시).

---

## 2. 미검증 가정 (open assumptions) — 검증 단계 매핑

- **(A1)** 기존 클라 테스트 수 646 / 서버 185 → 실제 baseline은 구현 착수 시 `dotnet test` / `npm test`로 재확인. 검증 단계: **Step 0(baseline 캡처)**.
- **(A2)** PIN 형식은 "6자리 숫자"로 확정한다는 가정 → **오픈이슈 O1**(사용자 승인 필요). 서버 `validatePin`·클라 입력 마스크가 이 결정에 의존. 검증 단계: **Step 3, Step 6**.
- **(A3)** SSO 판별 신호명 `authMethod:"sso"|"password"` + 파생 `hasPin` → **오픈이슈 O3**(명칭 확정 필요). 검증 단계: **Step 1(서버), Step 5(클라 매핑)**.
- **(A4)** PIN 게이트도 서버 검증(온라인 의존)이며 오프라인 설정 접근은 범위 밖 → **오픈이슈 O2**. 검증 단계: 없음(설계 결정 — 오픈이슈로 사용자 확인).
- **(A5)** PIN 변경 UI 위치: 본인 PIN = 비번 변경 창(`AccountView`), 타 계정 PIN 재설정 = 사용자 관리(`UserMgmtView`) → **오픈이슈 O4**. 검증 단계: **Step 7, Step 8**.
- **(A6)** 잘못된 PIN 재시도 정책(잠금/횟수 제한)은 현행 비번 게이트 수준(재시도 무제한, 서버 무잠금)을 따른다는 가정 → **오픈이슈 O5**. 검증 단계: **Step 3**(서버 verify 로직).
- **(A7)** 기존 비번 계정에 SSO email이 겹쳐 SSO가 그 계정으로 매핑되는 경우(§1.3), 그 계정은 `authMethod="password"`이므로 **비번 게이트로 남는다**(PIN 강제 없음). 이 동작이 수용 가능하다는 가정 → **오픈이슈 O6**.

---

## 3. 게이트 분기 설계

### 3.1 판정 규칙 (클라)

`OpenSettings`(`AppShellViewModel.cs:367-387`)에서 로그인 사용자에 대해:

```
user = _session.CurrentUser
if user is null            → 게스트, 무가드(현행 유지)
else if user.AuthMethod == Sso:
    if user.HasPin         → PIN 확인 다이얼로그(verify)
    else                   → PIN 설정 다이얼로그(최초 설정, 현재 PIN 확인 없음) — 성공 시 진입
else (password)            → 비밀번호 확인 다이얼로그(현행 VerifyPasswordAsync, 무변경)
```

- **fail-closed 보존**: PIN 다이얼로그 서비스가 DI에 없으면 진입 차단(현행 `prompt is null` 패턴 유지).
- **네트워크 오류**: PIN verify/set 실패(예외)는 창을 닫지 않고 인라인 오류, `DialogResult` 미설정 → 진입 안 됨(현행 `PasswordPromptWindow` fail-closed 계약 그대로).

### 3.2 판정 데이터 흐름 (서버 → 클라)

게이트가 계정 유형을 알려면 서버가 신호를 내려줘야 한다. 세 값이 필요하다:

| 신호 | 의미 | 소스 |
|------|------|------|
| `authMethod` | `"sso"` \| `"password"` | 서버 `UserDoc`에 저장(생성 시점 결정) |
| `hasPin` | PIN 설정 여부(파생) | `UserDoc.pinHash != null` → 서버가 계산해 응답에 노출 |

**흐름**:
1. 서버 `UserDoc`에 `authMethod?: "sso" | "password"` 저장. `createGoogleAccount`(`accounts.ts:355`)만 `"sso"`, 그 외 생성 경로(`createAccount`/`registerSelf`)는 `"password"`(또는 미설정=`"password"` 폴백).
2. `UserResponse`(`dto.ts:90`)에 `authMethod: string`(폴백 `"password"`) + `hasPin: boolean`(`doc.pinHash != null` 파생, **pinHash 원문은 절대 미노출**) 추가.
3. HTTP DTO `UserResponse`(`AccountDtos.cs:138`)에 `AuthMethod`/`HasPin` 추가.
4. `HttpAccountService.ToUser`(`:370-382`)가 `User.AuthMethod`/`User.HasPin`으로 매핑.
5. 로그인(`Login`/`LoginWithGoogle`)이 `SessionContext.Login(user)`로 세팅 → `CurrentUser`에 신호 상주 → `OpenSettings`가 참조.

> **판별 근거의 명확성(§1.3 함정 대응)**: "SSO로 로그인했다"가 아니라 "**계정 문서의 `authMethod`**"로 판정한다. sentinel 비번 계정(=SSO 자동생성)만 `"sso"`. 기존 비번 계정에 email이 겹쳐 SSO 흐름으로 로그인해도 그 계정 `authMethod`는 `"password"`라 비번 게이트로 남는다(→ 오픈이슈 O6). 이 방식은 "이 계정이 비번을 알 수 있는가"를 정확히 반영한다.

### 3.3 하위호환(레거시 문서)

- `authMethod` 미설정 기존 문서 → 서버가 `"password"`로 폴백(`parseAuthMethod`). 기존 계정은 전부 비번 게이트 유지.
- `pinHash` 없는 계정 → `hasPin=false`. 비번 계정에 `hasPin=false`는 무의미(비번 게이트만 사용). SSO 계정에 `hasPin=false`는 "최초 설정 필요" 신호.

---

## 4. 서버 스키마·엔드포인트 설계

### 4.1 스키마 변경 (`dto.ts`)

```ts
export interface UserDoc {
  // ... 기존 필드 ...
  /** it14: 인증 방식. "sso"=자동생성(sentinel 비번, PIN 게이트), "password"=일반. 미설정=password 폴백. */
  authMethod?: "sso" | "password";
  /** it14: 설정 진입 PIN의 bcrypt 해시. 미설정 null. 응답에 절대 미포함(hasPin으로만 노출). */
  pinHash?: string | null;
}

export interface UserResponse {
  // ... 기존 필드 ...
  authMethod: string;   // "sso" | "password"(폴백)
  hasPin: boolean;      // pinHash != null 파생(원문 미노출)
}
```

### 4.2 도메인 순수 로직 (신규 `web/functions/src/domain/pin.ts`)

- `validatePin(value): ValidationResult<string>` — `^\d{6}$`(O1 확정 시). `validateVerificationCode`(`validation.ts:60`)와 동형이나 **의미 분리를 위해 별도 함수**(코드 = 일회성 이메일 코드, PIN = 영속 자격).
- PIN 해시/검증은 `password.ts`의 `hashPassword`/`verifyHash` **재사용**(별도 bcrypt 도입 불요). 순수 함수 계층엔 새 해시 로직 없음.
- **권한 판정은 기존 `canManage`(`roles.ts:91`) 재사용** — PIN 전용 매트릭스 함수 불필요(비번 재설정과 동일 규칙).

### 4.3 엔드포인트 (4종)

모두 `accounts.ts` 라우터에 추가(계정 자원). 게이트 verify만 예외적으로 로그인 상태에서 호출되므로 `requireBearer`.

| # | 메서드·경로 | 인증 | body | 용도 | 서버 게이트 |
|---|-------------|------|------|------|-------------|
| E1 | `POST /accounts/me/pin/verify` | Bearer | `{pin}` | **게이트 확인**(본인 PIN 대조) | 본인만(principal.id) |
| E2 | `PUT /accounts/me/pin` | Bearer | `{currentPin?, newPin}` | **본인 PIN 설정/변경** | 본인. 기존 PIN 있으면 `currentPin` 확인 필수. 없으면(최초) `currentPin` 불요 |
| E3 | `PUT /accounts/:id/pin` | Bearer | `{newPin}` | **타 계정 PIN 재설정**(권한 기반) | `canManage(actor.role, targetRole)` && `actor.id != id`(자기 자신은 E2로) |
| — | (하위 파생) | — | — | — | — |

> **E2/E3 분리 이유**: E2는 본인(현재 PIN 확인 필요), E3는 타 계정(권한 기반, 대상 현재 PIN 불요) — 비번의 `changePassword`(본인/파워 통합)와 달리, "현재 PIN 확인"이 본인에게만 필요하므로 명시적으로 나눈다. 서버 `changePassword`(`accounts.ts:197`)를 참조하되 **PIN은 본인 변경 시 현재 PIN 확인이 추가**되는 점이 다르다.

**E1 verify 응답**: `200 {verified:true}` | `401`(불일치) | `409/400`(PIN 미설정 — 클라가 설정 플로우로 유도). 서버 무잠금(현행 비번 게이트 수준, O5).

**E2 응답**: `204`. `currentPin` 불일치 시 `401`. 형식 오류 `400`.

**E3 응답**: `204`. 권한 없음 `403`(`canManage` 위반). 대상 없음 `404`. 자기 자신 대상이면 `400`(E2 사용 유도).

### 4.4 서비스 로직 (`accounts.ts` 추가 함수)

```
verifyPin(actorId, pin) -> boolean:
    doc = getDoc(actorId); if !doc.pinHash -> {ok:false, reason:"unset"}
    return verifyHash(pin, doc.pinHash)

setOwnPin(actorId, currentPin, newPin) -> void:
    doc = getDoc(actorId)
    if doc.pinHash != null:                          # 기존 PIN 있으면 현재 PIN 확인
        if currentPin == null || !verifyHash(currentPin, doc.pinHash) -> 401
    update(actorId, {pinHash: hashPassword(newPin)})

resetOtherPin(targetId, newPin, actor) -> void:      # 권한 기반, 대상 현재 PIN 불요
    targetRole = getRole(targetId)                   # 없으면 404
    if !canManage(actor.role, targetRole) -> 403
    update(targetId, {pinHash: hashPassword(newPin)})
```

- `createGoogleAccount`(`accounts.ts:355`)의 신규 문서에 `authMethod: "sso"` 추가(`pinHash`는 미설정=null → `hasPin:false` → 최초 설정 유도).
- `createAccount`/`registerSelf`의 신규 문서에 `authMethod: "password"` 추가(명시).

### 4.5 서버 강제(위계) 요약

- E1(verify)·E2(본인 PIN): principal.id로만 자기 문서 접근 → 타인 PIN 조회/설정 불가.
- E3(타 계정 재설정): `canManage` 서버 재검증(클라 UI 필터와 이중 방어). admin→전체 하위, manager→user/temp_user, user/temp_user→불가(actor 위계로 차단).

---

## 5. 클라 계약·매핑 설계

### 5.1 도메인 모델 (`User.cs`)

```csharp
public enum AuthMethod { Password, Sso }   // 신규(Core.Models)

public sealed class User {
    // ... 기존 ...
    /// <summary>it14: 인증 방식. Sso=설정 진입 PIN 게이트, Password=비번 게이트. 기본 Password.</summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
    /// <summary>it14: 설정 진입 PIN 설정 여부(서버 파생). SSO+false=최초 설정 유도.</summary>
    public bool HasPin { get; set; }
}
```

### 5.2 IAccountService 확장 (`IAccountService.cs`)

```csharp
/// <summary>설정 진입 게이트: 본인 PIN 대조(SSO 계정). 일치 true, 불일치 false, PIN 미설정/네트워크 오류는 예외.</summary>
Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default);

/// <summary>본인 PIN 설정/변경. 기존 PIN 있으면 currentPin 확인(null이면 최초 설정). HTTP 전용.</summary>
Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default);

/// <summary>타 계정 PIN 재설정(권한 기반, 대상 현재 PIN 불요). 위계 위반은 UnauthorizedAccessException. HTTP 전용.</summary>
Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default);
```

- HTTP 구현: E1/E2/E3 호출. 401→false 또는 예외 매핑(비번 계약과 동형 — `VerifyPasswordAsync` 패턴 재사용, 네트워크 오류는 fail-closed 전파).
- **레거시 구현(`MCPhoto.Firebase.AccountService`)**: 세 메서드 모두 `NotSupportedException`(SSO·PIN은 백엔드 전용, `LoginWithGoogleAsync`와 동일 정책). 레거시엔 SSO 계정이 없어 호출되지 않음.
- **테스트 fake**(다수 파일의 `IAccountService` 스텁): 새 메서드 3종 추가 구현 필요(무회귀 — 컴파일 유지). 근거: `AccountViewModelTempUserTests.cs:35` 등이 인터페이스 전체를 구현.

### 5.3 HTTP DTO·매핑 (`AccountDtos.cs`, `HttpAccountService.cs`)

- `UserResponse`(`AccountDtos.cs:138`)에 `AuthMethod`(string)·`HasPin`(bool) 추가.
- `ToUser`(`HttpAccountService.cs:370`)에서 `AuthMethod = ParseAuthMethod(dto.AuthMethod)`, `HasPin = dto.HasPin` 매핑. `ParseAuthMethod`: `"sso"→Sso`, 그 외→`Password`(폴백).
- 신규 요청 DTO: `VerifyPinRequest{Pin}`, `SetPinRequest{CurrentPin?, NewPin}`, `ResetPinRequest{NewPin}`, 응답 `VerifyPinResponse{Verified}`.

### 5.4 다이얼로그 서비스 (신규 `IPinPromptDialogService`)

**결정: `IPasswordPromptDialogService` 확장이 아니라 신규 서비스**. 이유:
- PIN은 (a) 입력 마스크/키패드가 다르고(6자리 숫자), (b) "설정 vs 확인" 2모드가 필요하며, (c) 실패 UX 문구가 다르다. 기존 `Prompt(verifyAsync)` 단일 시그니처에 우겨넣으면 응집도가 깨진다.
- 기존 `PasswordPromptWindow`는 **무변경**(비번 계정 게이트 그대로) → 회귀 위험 격리.

```csharp
public interface IPinPromptDialogService {
    /// <summary>PIN 확인(게이트). 입력을 verifyAsync로 대조. 성공 확인 시 true. 실패·오류는 fail-closed(창 유지).</summary>
    bool PromptVerify(Func<string, Task<bool>> verifyAsync);

    /// <summary>PIN 최초 설정(SSO 첫 진입). 새 PIN 2회 입력 → setAsync(newPin). 성공 시 true.</summary>
    bool PromptSetup(Func<string, Task> setAsync);
}
```

- 구현 `PinPromptDialogService` + `PinPromptWindow`(신규 XAML). `PasswordPromptWindow`의 fail-closed 패턴(`:25-57`)을 그대로 계승(중복 제출 가드, 인라인 오류, 네트워크 오류 fail-closed).
- DI: `AddSingleton<IPinPromptDialogService, PinPromptDialogService>`(`ServiceRegistration.cs:43` 인근).

### 5.5 게이트 진입 로직 (`OpenSettings` 개정)

```csharp
private async Task OpenSettings() {
    IsAccountPopupOpen = false;
    var user = _session.CurrentUser;
    if (user is not null) {
        var account = _services.GetService<IAccountService>();
        if (account is null) return;                        // fail-closed
        if (user.AuthMethod == AuthMethod.Sso) {
            var pin = _services.GetService<IPinPromptDialogService>();
            if (pin is null) return;                        // fail-closed
            var uid = user.Id;
            bool ok = user.HasPin
                ? pin.PromptVerify(p => account.VerifyPinAsync(uid, p))
                : pin.PromptSetup(async p => {              // 최초 설정
                    await account.SetOwnPinAsync(uid, null, p);
                    user.HasPin = true;                     // 로컬 세션 반영
                });
            if (!ok) return;
        } else {
            var prompt = _services.GetService<IPasswordPromptDialogService>();
            if (prompt is null) return;                     // fail-closed(현행)
            var uid = user.Id;
            if (!prompt.Prompt(pw => account.VerifyPasswordAsync(uid, pw))) return;
        }
    }
    await NavigateToOverlayAsync(AppState.Settings);
}
```

---

## 6. 클라 UI 설계 (PIN 변경)

### 6.1 본인 PIN 변경 (AccountView, PasswordChange 모드)

- `AccountViewModel.cs`에 PIN 변경 섹션 추가(비번 변경 섹션과 병렬). **백엔드 모드 + SSO 계정에서만 노출**(`IsBackendMode && CurrentUser.AuthMethod==Sso`).
  - 비번 계정은 PIN이 없으므로 이 섹션 미노출(비번 변경만).
- 입력: `CurrentPin`, `NewPin`, `ConfirmPin`(PasswordBox → code-behind 전달, 기존 `NewPassword` 패턴 `:59`).
- 커맨드 `ChangePin`: `NewPin==ConfirmPin` 확인 → `SetOwnPinAsync(id, CurrentPin, NewPin)` → 성공/실패 메시지(기존 `SetAccountMessage` 재사용).
- 최초 설정(HasPin=false)이면 `CurrentPin` 입력란 숨김·`SetOwnPinAsync(id, null, NewPin)`.

### 6.2 타 계정 PIN 재설정 (UserMgmtView)

- `UserMgmtViewModel.cs`에 `ResetUserPin` 커맨드 추가(`ResetUserPassword` `:111-128`와 동형).
  - `CanManage(ActorRole, user.Role)` 가드(UI 미노출 + 이중 방어).
  - 고정 PIN? **아니오** — PIN은 자격이므로 고정값(비번의 `"0000"`) 대신 **입력 받거나** 관리자가 지정한 값 사용. → **오픈이슈 O4**(UX 상세: 인라인 입력 vs 소형 다이얼로그).
  - `UserRowViewModel`(`:17-38`)에 PIN 재설정 UI 노출 조건 추가: `CanManage && !isSelf`.
- 서버 `ResetPinAsync` → E3 → 403 시 우아 처리(`ApplyRoleChange`의 403 패턴 `:157-163` 재사용).

### 6.3 문구(초안 — 확정 시 §0 문구표에 편입)

| 상황 | 문구(초안) |
|------|-----------|
| SSO 최초 진입 PIN 설정 | "설정 진입에 사용할 PIN을 설정하세요(6자리 숫자)." |
| PIN 확인 게이트 | "설정 진입 PIN을 입력하세요." |
| PIN 불일치 | "PIN이 일치하지 않습니다." |
| PIN 확인 불가(네트워크) | "확인할 수 없습니다. 네트워크를 확인하세요."(비번 게이트와 동일) |

---

## 7. 엣지 케이스

| # | 케이스 | 처리 |
|---|--------|------|
| EC1 | **PIN 미설정 SSO 계정** 설정 진입 | `HasPin=false` → `PromptSetup` 강제. 성공해야 진입. **데드락 없음**(설정 밖에서 설정 가능). |
| EC2 | PIN 설정 중 취소 | `DialogResult != true` → 진입 안 됨. 다음 진입 시 다시 설정 유도(상태 불변). |
| EC3 | 잘못된 PIN 반복 | 현행 비번 게이트와 동일(무잠금, 재시도 허용). 강화는 O5. |
| EC4 | 네트워크 오류(verify/set) | fail-closed — 창 유지, 인라인 오류, 진입 차단(`PasswordPromptWindow` 계약 계승). |
| EC5 | **유휴 재잠금** | 설정은 오버레이 진입마다 `OpenSettings`가 게이트를 다시 태움(현행과 동일 — 세션 내 재진입도 매번 재인증). 별도 재잠금 로직 신설 불요. |
| EC6 | 비번 계정이 SSO email로도 로그인(§1.3) | 계정 `authMethod="password"` 유지 → 비번 게이트. PIN 강제 없음(O6). |
| EC7 | admin이 자기 PIN을 E3로 재설정 시도 | 서버가 `actor.id==id` → `400`(E2 사용 유도). 클라도 `isSelf` 미노출. |
| EC8 | manager가 admin PIN 재설정 시도 | `canManage(manager, admin)=false` → `403`. 클라 UI 미노출 + 서버 방어. |
| EC9 | 레거시(UseBackend off) | SSO 계정 없음 → 모든 계정 `authMethod=password`(폴백) → 비번 게이트. PIN 서비스 미호출. |
| EC10 | PIN 설정 후 로그아웃→재로그인 | 재로그인 시 서버 `hasPin=true` 응답 → `PromptVerify` 경로. 정상. |

---

## 8. 스레딩·안전

- 모든 서버 왕복은 async 커맨드/다이얼로그 내부. UI 스레드 블로킹 없음(`.Result`/`.Wait()` 금지 — 기존 관례 준수).
- `PinPromptWindow`의 유일한 `async void`는 WPF 이벤트 핸들러(확인 버튼) — 예외를 삼키지 않고 인라인 오류(`PasswordPromptWindow.xaml.cs:25-57` 패턴 그대로).
- PIN 원문·pinHash는 **로그·응답·세션에 미보관**(비번 정책과 동일). `User`에는 `HasPin`(bool)만.
- fail-closed 불변: 어떤 오류든 게이트를 열지 않는다.

---

## 9. 파일별 역할 (변경/신규)

### 서버(web/functions)
| 파일 | 변경 |
|------|------|
| `src/services/dto.ts` | `UserDoc.authMethod?`·`pinHash?`, `UserResponse.authMethod`·`hasPin` 추가 |
| `src/domain/pin.ts` (신규) | `validatePin` 순수 함수 |
| `src/services/accounts.ts` | `verifyPin`/`setOwnPin`/`resetOtherPin` 추가, `createGoogleAccount`/`createAccount`/`registerSelf`에 `authMethod` 세팅, `toResponse`에 authMethod·hasPin |
| `src/routes/accounts.ts` | E1/E2/E3 라우트 추가 |
| `src/domain/validation.ts` | (선택) `validatePin` 여기 두거나 pin.ts |

### 클라(src)
| 파일 | 변경 |
|------|------|
| `MCPhoto.Core/Models/User.cs` | `AuthMethod`(enum 신규)·`HasPin` 추가 |
| `MCPhoto.Core/Accounts/IAccountService.cs` | `VerifyPinAsync`/`SetOwnPinAsync`/`ResetPinAsync` 추가 |
| `MCPhoto.Http/Dto/AccountDtos.cs` | `UserResponse`에 필드 + PIN 요청/응답 DTO |
| `MCPhoto.Http/HttpAccountService.cs` | 3 메서드 구현 + `ToUser` 매핑 + `ParseAuthMethod` |
| `MCPhoto.Firebase/AccountService.cs` | 3 메서드 `NotSupportedException` |
| `MCPhoto.App/Services/IPinPromptDialogService.cs` (신규) | 인터페이스 |
| `MCPhoto.App/Services/PinPromptDialogService.cs` (신규) | 구현 |
| `MCPhoto.App/Views/PinPromptWindow.xaml(.cs)` (신규) | PIN 확인/설정 모달 |
| `MCPhoto.App/AppShellViewModel.cs` | `OpenSettings` 게이트 분기 |
| `MCPhoto.App/ServiceRegistration.cs` | `IPinPromptDialogService` 등록 |
| `MCPhoto.App/ViewModels/AccountViewModel.cs` | 본인 PIN 변경 섹션·커맨드 |
| `MCPhoto.App/Views/AccountView.xaml` | PIN 변경 UI(SSO+백엔드 노출) |
| `MCPhoto.App/ViewModels/UserMgmtViewModel.cs` | `ResetUserPin` 커맨드 |
| `MCPhoto.App/Views/UserMgmtView.xaml` | 타 계정 PIN 재설정 UI |

### 테스트
| 파일 | 변경 |
|------|------|
| `web/functions/src/__tests__/pin.test.ts` (신규) | `validatePin` 순수 |
| `web/functions/src/__tests__/accounts.test.ts` | verify/set/reset + authMethod·hasPin |
| `tests/MCPhoto.Tests/Http/HttpAccountServiceTests.cs` | 3 메서드 HTTP 계약 |
| `tests/MCPhoto.Tests/*` (fake 다수) | `IAccountService` 새 메서드 스텁 추가(컴파일 유지) |
| `tests/MCPhoto.Tests/AccountViewModelPinTests.cs` (신규, 선택) | PIN 변경 VM 로직 |

---

## 10. 테스트 계획

### 순수 로직(무의존)
- 서버 `validatePin`: 6자리 숫자 통과/비숫자·길이 위반 거부(jest).
- 권한 매트릭스: **기존 `canManage` 테스트 재사용**(PIN이 별도 매트릭스를 두지 않으므로 신규 매트릭스 테스트 불요). resetOtherPin이 `canManage`를 호출하는지만 서비스 테스트로 확인.

### 서버 서비스(jest, Firestore mock)
- verifyPin: 일치→true, 불일치→false, pinHash 없음→unset.
- setOwnPin: 최초(currentPin null)→설정, 기존 있음+currentPin 불일치→401, 일치→변경.
- resetOtherPin: `canManage` 통과→변경, 위반→403, 자기 자신→400, 대상 없음→404.
- createGoogleAccount→`authMethod:"sso"`, createAccount/registerSelf→`"password"`. toResponse에 `hasPin` 파생 정확.

### 클라(xUnit)
- `HttpAccountServiceTests`: E1/E2/E3 요청 형태·상태코드 매핑(기존 `VerifyPasswordAsync` 테스트 `:531-599` 패턴).
- `ToUser` 매핑: authMethod/hasPin 왕복.
- (선택) `OpenSettings` 게이트 분기: SSO+hasPin→verify, SSO+!hasPin→setup, password→비번. VM 단위(다이얼로그 서비스 fake).

### 무회귀
- **모든 기존 fake `IAccountService`에 3 메서드 추가**(컴파일 필수). baseline 646/185 유지 확인(Step 0 캡처 → 최종 재확인).

---

## 11. 구현 단계 (WBS)

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 단계 self-contained.

### 검증된 사실 / 미검증 가정
- **사실**: §1 전체(근거 file:line).
- **가정→검증**: A1→Step 0, A2(PIN 형식)→Step 3·6, A3(신호명)→Step 1·5, A5(UI 위치)→Step 7·8, A6(재시도)→Step 3. **A2/A3/A5/O1~O6은 사용자 승인 후 확정**(아래 §12).

---

### Step 0: baseline 캡처
- **Context Brief**: it14 PIN 게이트 구현 전, 무회귀 기준을 확정한다. 팀리드 명시 646(클라)/185(서버)를 실측으로 검증.
- **대상 파일**: 없음(측정만).
- **선행 조건**: 없음.
- **구현 내용**: `dotnet test`(MCPhoto.sln), `cd web/functions && npm test` 실행해 통과 수 기록.
- **검증 명령**: `dotnet test src/../MCPhoto.sln` / `npm --prefix web/functions test`.
- **완료 기준**:
  - [관측] 클라·서버 테스트 전부 green, 통과 수를 문서에 기록.
  - [non-goal] 코드 변경 없음.
  - [trigger] 측정만.
- **롤백**: 없음.
- [ ] 완료

### Step 1: 서버 스키마·authMethod 세팅
- **Context Brief**: SSO 자동생성 계정을 게이트가 식별할 수 있도록 `UserDoc.authMethod`·`pinHash`를 추가하고, 생성 경로에서 authMethod를 세팅한다. `createGoogleAccount`(`accounts.ts:355`)만 `"sso"`.
- **대상 파일**: `web/functions/src/services/dto.ts`, `web/functions/src/services/accounts.ts`
- **선행 조건**: 없음.
- **구현 내용**: `UserDoc`에 `authMethod?`·`pinHash?` 추가. `UserResponse`에 `authMethod`·`hasPin`. `toResponse`(`accounts.ts:45`)가 `authMethod ?? "password"`, `hasPin = doc.pinHash != null`. `createGoogleAccount`→`authMethod:"sso"`, `createAccount`/`registerSelf`→`"password"`.
- **검증 명령**: `npm --prefix web/functions run build` + `npm --prefix web/functions test`.
- **완료 기준**:
  - [관측] `accounts.test.ts`에 authMethod/hasPin 단언 추가 → green. 기존 185 유지.
  - [non-goal] 로그인/기존 CRUD 응답의 다른 필드 불변.
  - [trigger] 계정 생성 시에만 authMethod 기록.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 2: 서버 PIN 도메인(validatePin)
- **Context Brief**: PIN 형식 검증 순수 함수. O1(6자리 숫자) 확정 전제.
- **대상 파일**: `web/functions/src/domain/pin.ts`(신규), `web/functions/src/__tests__/pin.test.ts`(신규)
- **선행 조건**: O1 확정.
- **구현 내용**: `validatePin(value): ValidationResult<string>` — `^\d{6}$`(`validation.ts:57-65` 패턴). 해시는 `password.ts` 재사용(신규 없음).
- **검증 명령**: `npm --prefix web/functions test -- pin`.
- **완료 기준**:
  - [관측] 6자리 숫자 통과, 비숫자·5/7자리 거부 테스트 green.
  - [non-goal] 기존 validation 함수 불변.
  - [trigger] 함수 호출 시 판정.
- **롤백**: 신규 파일 삭제.
- [ ] 완료

### Step 3: 서버 PIN 서비스·라우트(E1/E2/E3)
- **Context Brief**: PIN verify(게이트)/본인 설정·변경/타 계정 재설정 엔드포인트. 권한은 `canManage`(`roles.ts:91`) 재사용. `changePassword`(`accounts.ts:197`)가 참조 원형.
- **대상 파일**: `web/functions/src/services/accounts.ts`, `web/functions/src/routes/accounts.ts`, `web/functions/src/__tests__/accounts.test.ts`
- **선행 조건**: Step 1, Step 2.
- **구현 내용**: `verifyPin`/`setOwnPin`/`resetOtherPin`(§4.4). 라우트 E1 `POST /me/pin/verify`, E2 `PUT /me/pin`, E3 `PUT /:id/pin`(§4.3). E3는 `canManage` + `actor.id != id`(자기 400). verify는 서버 무잠금(A6/O5).
- **검증 명령**: `npm --prefix web/functions run build` + `npm --prefix web/functions test -- accounts`.
- **완료 기준**:
  - [관측] verify 일치/불일치, setOwn 최초/현재PIN확인, resetOther 권한통과/403/400/404 테스트 green.
  - [non-goal] 기존 password/role/email 라우트 응답 불변.
  - [trigger] Bearer 인증된 요청만. E3는 권한 통과 시에만 pinHash 갱신.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 4: 클라 도메인·인터페이스(User·IAccountService)
- **Context Brief**: 클라 도메인에 AuthMethod/HasPin, 서비스 계약에 PIN 3메서드 추가. 모든 fake 구현 컴파일 유지가 관건.
- **대상 파일**: `src/MCPhoto.Core/Models/User.cs`, `src/MCPhoto.Core/Accounts/IAccountService.cs`, 모든 테스트/프로덕션 `IAccountService` 구현체
- **선행 조건**: 없음(서버와 독립).
- **구현 내용**: `AuthMethod` enum(Core.Models) + `User.AuthMethod`/`HasPin`. `IAccountService`에 3메서드. `MCPhoto.Firebase.AccountService`는 `NotSupportedException`. **모든 fake(`AccountViewModelTempUserTests.cs:35` 등)에 스텁 추가**.
- **검증 명령**: `dotnet build MCPhoto.sln`(error 0).
- **완료 기준**:
  - [관측] 솔루션 빌드 통과(모든 구현체가 인터페이스 충족).
  - [non-goal] 기존 메서드 시그니처·동작 불변.
  - [trigger] 컴파일 시점.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 5: 클라 HTTP 구현·DTO 매핑
- **Context Brief**: E1/E2/E3 호출 + UserResponse→User 매핑에 authMethod/hasPin 흐름.
- **대상 파일**: `src/MCPhoto.Http/Dto/AccountDtos.cs`, `src/MCPhoto.Http/HttpAccountService.cs`, `tests/MCPhoto.Tests/Http/HttpAccountServiceTests.cs`
- **선행 조건**: Step 3(서버 계약), Step 4(인터페이스).
- **구현 내용**: `UserResponse`에 `AuthMethod`/`HasPin`. PIN 요청/응답 DTO. `VerifyPinAsync`/`SetOwnPinAsync`/`ResetPinAsync` 구현(401→false/예외, `VerifyPasswordAsync` `:59-78` 패턴). `ToUser`에 `ParseAuthMethod`·`HasPin`.
- **검증 명령**: `dotnet test --filter HttpAccountServiceTests`.
- **완료 기준**:
  - [관측] E1/E2/E3 요청 URL·body·상태코드 매핑 테스트 green. ToUser 왕복 테스트 green.
  - [non-goal] 기존 로그인/CRUD 매핑 불변.
  - [trigger] 해당 메서드 호출 시.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 6: PIN 다이얼로그 서비스·창
- **Context Brief**: SSO 게이트용 PIN 확인/설정 모달. 기존 `PasswordPromptWindow` fail-closed 패턴 계승, 비번 창은 무변경.
- **대상 파일**: `src/MCPhoto.App/Services/IPinPromptDialogService.cs`(신규), `PinPromptDialogService.cs`(신규), `src/MCPhoto.App/Views/PinPromptWindow.xaml(.cs)`(신규), `src/MCPhoto.App/ServiceRegistration.cs`
- **선행 조건**: O1(형식) 확정.
- **구현 내용**: `PromptVerify`/`PromptSetup`(§5.4). `PinPromptWindow`는 6자리 입력(마스크·키패드 O1 확정 시), 확인/설정 2모드, 중복제출 가드·인라인 오류·네트워크 fail-closed(`PasswordPromptWindow.xaml.cs:25-57`). DI 등록.
- **검증 명령**: `dotnet build` + (헤드리스 XAML 회귀 테스트가 있으면) `dotnet test --filter Xaml`.
- **완료 기준**:
  - [관측] 빌드 통과, 창 리소스 로드(XamlResourceTests류 green).
  - [non-goal] `PasswordPromptWindow`·기존 다이얼로그 불변.
  - [trigger] 서비스 호출 시에만 창 표시. 취소/오류 시 `DialogResult != true`.
- **롤백**: 신규 파일 삭제 + DI 등록 revert.
- [ ] 완료

### Step 7: 게이트 분기(OpenSettings) + 본인 PIN 변경 UI
- **Context Brief**: 설정 진입 게이트를 authMethod로 분기하고, SSO 계정에 본인 PIN 변경 섹션을 제공한다.
- **대상 파일**: `src/MCPhoto.App/AppShellViewModel.cs`, `src/MCPhoto.App/ViewModels/AccountViewModel.cs`, `src/MCPhoto.App/Views/AccountView.xaml`
- **선행 조건**: Step 5, Step 6.
- **구현 내용**: `OpenSettings`(`:367-387`) §5.5 분기. AccountView PasswordChange 모드에 PIN 변경 섹션(SSO+백엔드 노출), `ChangePin` 커맨드(§6.1).
- **검증 명령**: `dotnet test`(전체) + 수동 시나리오(§7 EC1/EC10).
- **완료 기준**:
  - [관측] SSO+hasPin→PIN verify 창, SSO+!hasPin→설정 창, password→비번 창 진입. PIN 변경 성공 시 메시지.
  - [non-goal] 게스트 무가드·비번 계정 게이트 동작 불변. 취소 시 설정 미진입.
  - [trigger] 설정 버튼 클릭 시에만 게이트. PIN 변경은 "변경" 클릭 시에만.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 8: 타 계정 PIN 재설정 UI(UserMgmt)
- **Context Brief**: admin/manager가 하위 계정 PIN을 재설정. `ResetUserPassword`(`:111-128`)와 동형.
- **대상 파일**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`, `src/MCPhoto.App/Views/UserMgmtView.xaml`, `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs`
- **선행 조건**: Step 5, O4(UI 상세) 확정.
- **구현 내용**: `ResetUserPin` 커맨드(`CanManage` 가드, `ResetPinAsync`, 403 우아처리 `:157-163`). `UserRowViewModel`에 노출 조건(`CanManage && !isSelf`). PIN 입력 방식은 O4.
- **검증 명령**: `dotnet test --filter UserMgmt`.
- **완료 기준**:
  - [관측] 권한 있는 대상에 PIN 재설정 성공, manager→admin 미노출/403.
  - [non-goal] 삭제·비번초기화·역할변경 동작 불변.
  - [trigger] 재설정 버튼 클릭 시에만.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 9: 무회귀·최종 검증
- **Context Brief**: 전체 스위트 재실행, baseline 대비 무회귀 + 신규 테스트 증가 확인.
- **대상 파일**: 없음.
- **선행 조건**: Step 1~8.
- **구현 내용**: 전체 `dotnet test` + `npm test`.
- **검증 명령**: `dotnet test MCPhoto.sln` / `npm --prefix web/functions test`.
- **완료 기준**:
  - [관측] Step 0 baseline 전부 여전히 green + PIN 신규 테스트 green.
  - [non-goal] 기존 테스트 0 실패.
  - [trigger] 측정.
- **롤백**: 실패 단계로 회귀.
- [ ] 완료

### 완결성 게이트 (self-check)
- [x] 검증된 사실/미검증 가정 분리(§1·§2)
- [x] 모든 가정에 검증 단계 매핑(§2)
- [x] 모든 단계 7필드 채움
- [x] 완료 기준 관측 3문(UI 단계 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능
- ⚠️ **오픈이슈 O1~O6 미해결 → developer 전달 전 사용자 승인 필요**(§12)

---

## 12. 사용자 승인 필요한 오픈이슈

| # | 이슈 | 선택지 | 권장 | 영향 단계 |
|---|------|--------|------|-----------|
| **O1** | **PIN 형식** | (a) 6자리 숫자 (b) 4자리 (c) 영숫자 N자 | **(a) 6자리 숫자** — 이메일 코드(`^\d{6}$`)와 일관, 키패드 입력 용이 | Step 2·3·6 |
| **O2** | **오프라인 설정 접근** | (a) 범위 밖(현행 비번 게이트도 서버 의존) (b) 로컬 PIN 폴백 신설 | **(a)** — 현행과 동일 수준 유지, 복잡도·보안 노출 회피 | 없음(결정만) |
| **O3** | **SSO 판별 신호명** | (a) `authMethod:"sso"\|"password"` + `hasPin` (b) `pinRequired` (c) `hasUsablePassword` | **(a)** — 의미가 명확하고 확장 가능(향후 인증방식 추가 대비) | Step 1·5 |
| **O4** | **타 계정 PIN 재설정 UI 위치·방식** | (a) UserMgmt 행에 인라인 입력+버튼 (b) 소형 다이얼로그 (c) 고정값 초기화(비번 `"0000"` 방식) | **(b) 소형 다이얼로그** — 6자리 정확 입력 UX, 고정값은 PIN 자격성 훼손 | Step 8 |
| **O5** | **잘못된 PIN 재시도 정책** | (a) 무잠금(현행 비번 수준) (b) N회 실패 시 지연/잠금 | **(a)** — 현행 일관, 키오스크 물리 접근 전제. 강화는 후속 | Step 3 |
| **O6** | **비번계정+SSO email 겹침**(§1.3/EC6) | (a) authMethod=password 유지→비번 게이트(현동작) (b) SSO 로그인 시 PIN 강제 | **(a)** — 계정 정체성을 문서 authMethod로 고정, 예측 가능 | Step 1 |

> **O1·O3·O4는 구현 착수 전 반드시 확정**(스키마·라우트·UI에 직접 반영). O2·O5·O6은 "현행 유지" 권장이 수용되면 즉시 진행 가능.

---

## 13. 요약

- **문제**: SSO 자동생성 계정은 sentinel 비번(`accounts.ts:285`)이라 설정 진입 비번 재확인(`AppShellViewModel.cs:383`)이 불가능 → 설정 접근 불가.
- **해법**: 계정 문서의 **`authMethod`** 로 게이트를 분기. SSO=PIN(서버 `pinHash` bcrypt), password=비번(현행). PIN 미설정 SSO는 최초 진입 시 강제 설정.
- **권한**: PIN 재설정은 **기존 `canManage` 위계 재사용**(비번 재설정과 동형) — 별도 매트릭스 불요. 본인 변경만 현재 PIN 확인 추가.
- **격리**: 신규 `IPinPromptDialogService`/`PinPromptWindow`로 비번 게이트(`PasswordPromptWindow`) 무변경, 회귀 위험 최소화. 레거시 경로는 SSO 없어 비번 게이트 유지.
- **무회귀**: 모든 fake `IAccountService`에 3메서드 스텁 추가가 컴파일 관건(Step 4). baseline 646/185 유지.
- **미결**: O1(PIN 형식)·O3(신호명)·O4(재설정 UI)는 착수 전 사용자 확정 필수.
