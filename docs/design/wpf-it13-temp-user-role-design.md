# MCPhoto 이터레이션13 설계 — 임시 유저(TempUser) 역할 (wpf-it13-temp-user-role-design.md)

> 대상: MCPhoto (WPF / .NET 8, MVVM=CommunityToolkit.Mvvm) + Firebase Cloud Functions 백엔드(Express/TS)
> 루트: `E:\Study\photobooth`
> 성격: **설계 문서(코드 구현 금지)**. 구현 단계 WBS는 §13에 포함.
> 선행 문서: `docs/design/wpf-it12-design.md`, `docs/design/wpf-auth-ux-and-account-rules-design.md`, `docs/design/wpf-it10-server-connectivity-design.md`

---

## 0. 개요

신규 역할 **`TempUser`(임시 유저)** 를 추가한다. 위계는 **Guest < TempUser < User < Manager < Admin**.
TempUser는 **User의 모든 기능을 그대로** 부여받되, **QR 전송(업로드+다운로드 = 서버 과금)만 한도**가 걸린다.

| 항목 | 결정 |
|------|------|
| 한도 종류 | **시간 한도**(계정 `createdAt`부터 N시간, 전역 기본 48h) + **횟수 한도**(QR 전송 성공 세션 수, 전역 기본 30회) |
| 한도 관계 | **독립(OR)** — 먼저 소진되는 쪽이 QR 강제 OFF |
| 권위 주체 | **서버**(과금 안전). 서버가 계정별 사용량을 추적·판정하고 업로드를 초과 시 거부. 클라는 상태를 받아 표시·차단만(클라 신뢰 금지) |
| 전역 한도 | **1쌍**(48h·30회), **Admin이 수정 가능**. 사용량은 **계정별** 추적 |
| 생성 권한 | **Admin·Manager**(User와 동일 위계에 TempUser 추가). role은 서버 강제 |
| 마이그레이션 | 없음(신규 계정만 TempUser). 기존 User↔TempUser 전환 없음 |

**과금 정확성이 이 이터레이션의 최우선 제약이다.** 클라 UI가 QR을 막는 것은 UX·1차 방어일 뿐이고,
**실제 과금 안전은 서버가 업로드를 거부**함으로써 성립한다. 따라서 설계의 무게중심은
"서버가 업로드 요청의 주체(계정)를 신원화하고 한도를 강제하는 경로"에 있다.

### 문구 (정확히 — 변경 금지)

| 사유 | 설정 페이지 문구 |
|------|-----------------|
| 시간 초과 | `무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.` |
| 횟수 소진 | `무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.` |

**둘 다 초과 시 우선순위: 시간 우선**(§8.1에서 근거). 즉 시간과 횟수가 모두 초과면 시간 문구를 표시한다.

---

## 1. 검증된 사실 (verified facts — 근거 file:line)

### 1.1 역할 모델 (C# / TS 이중 정의, 문자열 영속화)

- C# enum: `src/MCPhoto.Core/Models/UserRole.cs:4-14` — `User, Manager, Admin`(암묵 서수 0/1/2).
- 위계 함수 `UserRoleExtensions`(`:17-58`):
  - `ToFirestoreValue`(`:19-25`): enum→`"user"|"manager"|"admin"`(고정 문자열, 서수 무관).
  - `ParseRole`(`:27-32`): 문자열→enum, 미지원값은 `User` 폴백.
  - `IsPower`(`:35`): `is Manager or Admin`(패턴 매칭, 서수 무관 — **안전**).
  - `CreatableRoles`(`:41-46`): `switch`(서수 무관 — **안전**).
  - `CanCreate`(`:49-50`): `CreatableRoles().Contains` — **안전**.
  - **`CanManage`(`:56-57`): `(int)targetRole <= (int)actingRole`** — ⚠️ **서수 대소 비교. 재배치 시 붕괴**.
- TS 이식: `web/functions/src/domain/roles.ts` — `UserRole="user"|"manager"|"admin"`, `RANK{user:0,manager:1,admin:2}`(`:12-16`), `isUserRole`/`parseRole`/`isPower`/`creatableRoles`/`canCreate`/`canManage`(`:19-77`). `canManage`는 `RANK[t] <= RANK[a]`(`:76`) — C#과 동일한 서수 비교.
- **영속화는 전부 문자열**: Firestore `UserDoc.role: string`(`web/.../services/dto.ts:16`), HTTP DTO도 문자열. **enum 서수를 저장·전송하는 곳은 없음**(Explore 전수조사 결과). System.Text.Json은 `JsonStringEnumConverter` 미사용이나 enum을 DTO에 직접 싣지 않고 항상 `ToFirestoreValue()` 문자열로 변환해 전송(`HttpAccountService.cs:163,227`).

**결론**: 문자열 매핑만 확장하면 저장·전송 계약은 안전하다. **유일한 위험은 `CanManage`/`canManage`의 서수 비교** → §3에서 명시적 배치값 + switch 재작성으로 제거한다.

### 1.2 서버가 역할을 재검증 (클라 신뢰 안 함)

- 계정 CRUD 라우트는 전부 `requireBearer()`(`web/.../routes/accounts.ts:29`), 생성은 `requirePower()`(`:33`).
- `actingRole`은 **JWT의 role**에서 도출(`:54` `req.principal!.role`), 클라 전달 무시.
- `createAccount`가 `canCreate(actingRole, role)` 게이트(`web/.../services/accounts.ts:140`).
- JWT claims: `{sub:id, role}`(`web/.../domain/jwt.ts:34,58-65`), `AuthPrincipal{id,role}`. 역할은 `isUserRole` 화이트리스트로 검증(`:62`).

### 1.3 업로드(과금 지점)는 **JWT 없이 API 키만** — TempUser 한도의 핵심 난제

- 서버 업로드 라우트 `web/.../routes/uploads.ts`: `router.use(requireApiKey())`(`:18`) — **`requireBearer()` 없음**. 게스트(촬영자) 흐름 전제.
- 클라 업로드 호출도 `bearer: false`: prepare(`HttpFirebaseClient.cs:94`), commit(`:139`).
- `sessionId`는 **클라가 생성**: `UploadContract.NewSessionId(DateTime.Now)`(`src/MCPhoto.Firebase/UploadService.cs:43`). 계정과 무관한 `날짜_시간_uuid`. 이 ID가 폴더·문서ID·다운로드토큰·삭제 prefix를 공유.
- prepare→PUT(직접 Storage)→commit(resultSession 문서 생성) 3단계. **commit 성공 = 전송 세션 완성**(`uploads.ts:116-168`, 중복 sessionId면 409 `:143`).

**함의**: 서버가 "이 업로드가 누구 것인지" 알려면 **업로드 경로에 계정 신원(JWT)을 실어야 한다**. 현재는 익명. → §4·§5의 중심 설계.

### 1.4 QR 흐름은 로그인 상태에서만 진입 + 클라 게이트 3지점 선례

- `ResultViewModel.Next()`: `if (settings.EnableQrDelivery && _shell.IsLoggedIn) → NavigateAsync(Qr)` 아니면 `Done`(`src/MCPhoto.App/ViewModels/ResultViewModel.cs:148-151`). **TempUser도 로그인 상태 → 이 게이트 통과**.
- QR 실제 전송은 `QrPopupViewModel.OnEnterAsync` → `_upload.UploadResultAsync`(`QrPopupViewModel.cs:83`). 실패 시 **우아 처리**(로컬 보존, [완료]/[재시도], `:100-111`) — 이미 존재하는 실패 경로.
- 게스트 편집 차단 = **3지점 패턴**(it12 §1.1 확립):
  1. 소스단 강제 off: `SettingsViewModel.LoadSettings`의 `if (IsGuest){ EnableQrDelivery=false; … }`(`SettingsViewModel.cs:199-211`).
  2. 저장 시 ini 미기록(클로버 방지): `SaveSettings`의 `if (!IsGuest){ s.EnableQrDelivery=…; }`(`:267-272`).
  3. XAML `IsEnabled="{Binding IsLoggedIn}"` + `GuestGateNote` 노티(`SettingsView.xaml:246-248`).
- 권한 프로퍼티: `SettingsViewModel.IsLoggedIn/IsGuest`(`:67,69`, 설정 진입 중 불변 → `INotifyPropertyChanged` 불필요).

### 1.5 세션·클라 상태

- 로그인 상태 단일 소스: `SessionContext.CurrentUser`, 셸이 미러 없이 직접 읽음(`AppShellViewModel.cs:66-69`). `IsLoggedIn/IsGuest/IsPower`(`:67-69`). `IsPower => CurrentUser?.Role.IsPower()`(`:69`).
- 로그인 시 JWT·User는 `IBackendSession`(메모리, 디스크 미저장, `src/MCPhoto.Http/Session/IBackendSession.cs`)에 보관. `SignIn`이 저장(`HttpAccountService.cs:45`).
- `User` 모델(`src/MCPhoto.Core/Models/User.cs`): `Id, Role, CreatedAt, Email, EmailVerified`. **`CreatedAt` 존재**(시간 한도 판정에 활용 가능, 단 §8.4에서 서버 UTC 기준으로 권위 판정).
- AppSettings는 **로컬 MCPhoto.ini(장치별)**(`AppSettings.cs`). QR 토글(`EnableQrDelivery/SendPhoto/SendTimelapse`)은 장치 설정. **TempUser 한도는 계정별(서버)** — 두 개념은 별개 저장소이며 섞지 않는다(§7.1).

### 1.6 DI 분기(레거시 공존)

- `ServiceRegistration.RegisterBackendOrFirebase`(`src/MCPhoto.App/ServiceRegistration.cs:106-174`): `AppSettings.UseBackend`(기본 ON, `AppSettings.cs:138`)로 HTTP vs Firebase(Admin) 구현 분기. `IAccountService`/`IFrameRepository`/`IFirebaseClient` 팩토리.
- 레거시 Firebase 경로(`MCPhoto.Firebase.*`)는 롤백용으로 공존. **TempUser는 백엔드 전용 기능**(§12에서 레거시 방침 명시).

### 1.7 테스트 인프라

- C#: `tests/MCPhoto.Tests/*`. 역할 위계 테스트 `RoleManagementTests.cs`(현재 `CanManage` 서수 위계를 InlineData로 검증 — §3.3에서 확장). 순수 로직·VM 단위 테스트 관례.
- web: `web/functions/src/__tests__/*.test.ts`(jest). `roles.test.ts`, `accounts.test.ts` 등. `fakeFirestore.ts` 헬퍼로 Firestore 목킹.
- 헤드리스 XAML 회귀: `tests/MCPhoto.Tests/XamlResourceTests.cs`(StaticResource 미해결 검출).

---

## 2. 미검증 가정 (open assumptions → 검증 단계 매핑)

| # | 가정 | 검증 단계 |
|---|------|----------|
| A1 | `CanManage`/`canManage` 외에 서수 대소 비교에 의존하는 위계 로직이 더 없다(Explore가 `(int)` 캐스트 전수조사했으나 신규 코드/테스트 InlineData 재확인 필요) | WBS Step 1(C#), Step 6(TS) |
| A2 | 업로드 경로에 JWT를 실어도 **게스트(비로그인) 촬영 흐름이 깨지지 않는다**(게스트는 애초에 QR 미진입 §1.4 → 업로드 자체를 안 함). 즉 업로드 JWT는 "있으면 검증, 없으면 게스트로 통과"가 가능 | WBS Step 3(서버 미들웨어), Step 8(회귀) |
| A3 | commit 최초 성공 시점에 카운트 1 증가가 "성공 세션 1회"의 올바른 정의다(재시도는 동일 sessionId→409라 이중집계 없음, §8.2) | WBS Step 4, Step 5 |
| A4 | 전역 한도 config를 Firestore 단일 문서(`config/tempUserLimits`)로 두고 Admin이 수정하는 방식이 기존 패턴과 정합(기존에 config 컬렉션 없음 → 신규) | WBS Step 4 |
| A5 | TempUser 로그인 시 사용량 조회 엔드포인트 1회 호출로 설정 페이지 게이트에 충분(설정 진입 중 불변 전제, §7.3) | WBS Step 9, Step 10 |
| A6 | `RANK`/enum 배치값 변경이 기존 jest·xUnit 테스트의 명시적 기대치(admin=2 등)를 깨지 않거나, 깨는 테스트를 식별해 갱신 | WBS Step 1·6(테스트 갱신 포함) |

---

## 3. 역할 모델 변경 (C# + TS)

### 3.1 C# — 명시적 배치값 + `CanManage` switch 재작성

`src/MCPhoto.Core/Models/UserRole.cs`:

```csharp
public enum UserRole
{
    // ⚠️ 서수를 위계 비교에 쓰지 않는다(§3.3 CanManage는 switch). 배치값은 가독성용으로 위계 순 명시.
    TempUser = 0,   // 신규: User와 동기능 + QR 한도
    User     = 1,
    Manager  = 2,
    Admin    = 3
}
```

> **왜 명시 배치값인가**: 서수를 위계 근거로 삼지 **않도록** 코드를 바꾸지만(§3.3), 미래 독자가
> enum 순서를 위계로 오독하지 않도록 위계 순으로 값을 명시한다. 저장은 문자열이라 값 변경은 무해(§1.1).

문자열 매핑 확장:

```csharp
public static string ToFirestoreValue(this UserRole role) => role switch
{
    UserRole.TempUser => "temp_user",   // 신규 매핑(C#↔TS↔Firestore 일관)
    UserRole.User     => "user",
    UserRole.Manager  => "manager",
    UserRole.Admin    => "admin",
    _ => "user"
};

public static UserRole ParseRole(string? value) => value switch
{
    "admin"     => UserRole.Admin,
    "manager"   => UserRole.Manager,
    "temp_user" => UserRole.TempUser,   // 신규
    "user"      => UserRole.User,       // 명시(기존 default 폴백에서 승격)
    _           => UserRole.User        // 미지원값 폴백 유지
};
```

**Firestore 저장 문자열 = `"temp_user"`** (snake_case, C#/TS/Firestore 3자 일관. `parseRole` 미지원값은 `user` 폴백이므로 오탈자 시 최소권한).

`IsPower` — TempUser는 power 아님(변경 불필요, `is Manager or Admin` 그대로 TempUser 제외). ✔

`CreatableRoles` — Admin·Manager가 TempUser 생성 가능하도록 확장:

```csharp
public static IReadOnlyList<UserRole> CreatableRoles(this UserRole actingRole) => actingRole switch
{
    UserRole.Admin   => new[] { UserRole.TempUser, UserRole.User, UserRole.Manager },
    UserRole.Manager => new[] { UserRole.TempUser, UserRole.User },
    _ => Array.Empty<UserRole>()
};
```

> 스펙 7: "User와 동일 위계에 TempUser 추가" → Manager도 TempUser 생성 가능(User를 만들 수 있으면 TempUser도).

### 3.2 C# — `CanManage` 서수 비교 제거 (핵심 안전 변경)

`(int)` 비교를 명시적 위계 함수로 대체한다. **서수에 의존하지 않으므로 향후 역할 추가에도 안전**:

```csharp
/// <summary>위계 랭크(관리 판정 기준). 서수(enum 값)와 분리해 명시 — 역할 추가 시 여기만 갱신.</summary>
private static int ManageRank(UserRole role) => role switch
{
    UserRole.TempUser => 0,
    UserRole.User     => 1,
    UserRole.Manager  => 2,
    UserRole.Admin    => 3,
    _ => 0
};

/// <summary>actingRole이 targetRole을 관리(삭제·비번초기화 등)할 수 있는지: 자신과 같거나 낮은 위계만.</summary>
public static bool CanManage(this UserRole actingRole, UserRole targetRole)
    => ManageRank(targetRole) <= ManageRank(actingRole);
```

**위계 검증표**(§3.3 테스트로 고정):

| acting \ target | TempUser | User | Manager | Admin |
|-----------------|:---:|:---:|:---:|:---:|
| TempUser | ✓ | ✗ | ✗ | ✗ |
| User | ✓ | ✓ | ✗ | ✗ |
| Manager | ✓ | ✓ | ✓ | ✗ |
| Admin | ✓ | ✓ | ✓ | ✓ |

> TempUser가 TempUser를 관리(✓)하는 건 이론적 대칭일 뿐 실제 노출 없음 — TempUser는 관리 UI(UserMgmt) 자체에 진입 못 함(IsPower=false). 안전.

### 3.3 C# — 테스트 갱신

`tests/MCPhoto.Tests/RoleManagementTests.cs`: 기존 `CanManage_Only_Equal_Or_Lower` InlineData를 위 표로 확장(TempUser 행/열 6+4개 추가). `CreatableRoles`/`CanCreate` 테스트에 `Admin→TempUser`, `Manager→TempUser` 케이스 추가. `ToFirestoreValue`/`ParseRole` 라운드트립에 `TempUser↔"temp_user"` 추가.

### 3.4 TS — `roles.ts` 대칭 변경

`web/functions/src/domain/roles.ts`:

```ts
export type UserRole = "temp_user" | "user" | "manager" | "admin";

// 관리 위계 랭크(서수 아님 — canManage 전용). C# ManageRank와 1:1.
const MANAGE_RANK: Record<UserRole, number> = {
  temp_user: 0, user: 1, manager: 2, admin: 3,
};

export function isUserRole(value: unknown): value is UserRole {
  return value === "temp_user" || value === "user" || value === "manager" || value === "admin";
}

export function parseRole(value: string | null | undefined): UserRole {
  switch (value) {
    case "admin": return "admin";
    case "manager": return "manager";
    case "temp_user": return "temp_user";
    default: return "user";  // "user" 및 미지원값
  }
}

export function isPower(role: UserRole): boolean {
  return role === "manager" || role === "admin";   // TempUser 제외(변경 없음)
}

export function creatableRoles(actingRole: UserRole): UserRole[] {
  switch (actingRole) {
    case "admin": return ["temp_user", "user", "manager"];
    case "manager": return ["temp_user", "user"];
    default: return [];
  }
}

export function canManage(actingRole: UserRole, targetRole: UserRole): boolean {
  return MANAGE_RANK[targetRole] <= MANAGE_RANK[actingRole];
}
```

- 기존 `RANK`(`:12-16`)는 `canManage` 전용이었으므로 `MANAGE_RANK`로 대체(이름 변경으로 서수 오해 방지). `canCreate`는 `creatableRoles` 기반이라 무변경.
- `validateRole`(`web/.../domain/validation.ts:36-39`)은 `isUserRole` 기반이므로 자동으로 `temp_user` 허용. 에러 문구만 갱신(`user/manager/admin` → `temp_user/user/manager/admin`).
- JWT `isUserRole` 검증(`jwt.ts:62`)도 자동으로 `temp_user` 역할 토큰 허용.

### 3.5 클라 UI 위계 소비 지점 영향(Explore 조사 기반)

| 지점 | 영향 | 조치 |
|------|------|------|
| `UserMgmtViewModel.cs:65,84` `CanManage` 호출 | switch 재작성으로 자동 정합 | 없음(로직 무변경, 표만 확장) |
| `UserMgmtViewModel.cs:101,121` `Role != User/Manager` | TempUser 계정 표시 시 라벨 필요 | §9.5 역할 라벨 매핑 추가 |
| `Converters/CommonConverters.cs:213` `RoleActionVisibilityConverter` | `CanManage` 기반 → 자동 정합 | 없음 |
| `AccountViewModel.cs:139` `CreatableRoles` 소비(생성 UI 역할 목록) | TempUser 옵션 자동 등장 | §9.4 라벨·순서 확인 |

---

## 4. 서버 스키마 & 사용량 추적

### 4.1 계정별 사용량 (users doc 확장)

`UserDoc`에 필드 2개 추가(`web/functions/src/services/dto.ts`):

```ts
export interface UserDoc {
  // ... 기존 id, password, role, createdAt, email, emailVerified
  /** it13: TempUser QR 전송 성공 세션 누적 수. 원자 increment. 미설정=0(레거시/비TempUser). */
  qrUsedCount?: number;
  /** it13: 이중집계 방지용 — 카운트에 이미 반영된 sessionId 집합의 서브컬렉션 참조는 별도(§4.2). */
}
```

- `createdAt`(기존 Timestamp)이 **시간 한도의 기준**(서버 UTC). 신규 필드 불요.
- `qrUsedCount`는 **원자 증가**(`FieldValue.increment(1)`)로만 갱신. 초기 미설정은 0으로 해석.

### 4.2 이중집계 방지 — 카운트 증가의 멱등성

**"성공 세션 1회" = commit이 최초로 성공해 resultSession 문서가 생성된 시점 1건**(§8.2).
commit은 sessionId 중복 시 409(`uploads.ts:143`)이므로 **동일 sessionId로 두 번 카운트될 수 없다** —
commit 성공 경로에서만 increment하면 자연히 멱등. 별도 dedup 집합 불필요(§8.2 근거).

> ⚠️ 단, prepare는 여러 번 호출될 수 있고(사진+타임랩스 2파일 → prepare 2회 §1.3) 과금은 PUT/Storage에서
> 발생하지만, **한도 단위는 "세션"**(스펙 3: "prepare→commit 성공 세션 수")이므로 **commit 1회 = 1 카운트**가 정의에 부합.
> 파일 개수와 무관하게 세션당 1.

### 4.3 전역 한도 config

Firestore 신규 문서 `config/tempUserLimits`:

```ts
export interface TempUserLimitsDoc {
  /** 시간 한도(시간). 기본 48. */
  qrHours: number;
  /** 횟수 한도(성공 세션 수). 기본 30. */
  qrCount: number;
}
```

- 문서 부재 시 서버가 **기본값(48h, 30회)** 사용(`loadTempUserLimits()`가 부재를 기본으로 폴백). Admin이 수정하면 문서 생성/갱신.
- 순수 판정 로직은 `web/functions/src/domain/tempUserLimit.ts`(신규)로 분리(테스트 용이, §11):

```ts
export type QrGateReason = "ok" | "time" | "count";

/** 초과 판정(순수). 시간 우선(§8.1). now·createdAt은 ms epoch. */
export function evaluateQrGate(
  now: number, createdAtMs: number, usedCount: number,
  limits: { qrHours: number; qrCount: number }
): { blocked: boolean; reason: QrGateReason; remainingMs: number; remainingCount: number } {
  const elapsedMs = now - createdAtMs;
  const limitMs = limits.qrHours * 3600_000;
  const timeExceeded = elapsedMs >= limitMs;
  const countExceeded = usedCount >= limits.qrCount;
  const remainingMs = Math.max(0, limitMs - elapsedMs);
  const remainingCount = Math.max(0, limits.qrCount - usedCount);
  // 시간 우선: 둘 다 초과면 time.
  const reason: QrGateReason = timeExceeded ? "time" : countExceeded ? "count" : "ok";
  return { blocked: timeExceeded || countExceeded, reason, remainingMs, remainingCount };
}
```

---

## 5. 서버 엔드포인트/로직

### 5.1 업로드 신원화 — 선택적 Bearer 미들웨어 (아키텍처 중심)

**문제**: 업로드는 익명(API 키만, §1.3). TempUser 한도를 강제하려면 계정 신원이 필요.

**설계**: 업로드 라우트에 **선택적 Bearer**(optional principal) 미들웨어를 추가한다. 게스트 촬영은 익명 유지, 로그인(특히 TempUser) 업로드는 신원 부착:

```ts
// web/functions/src/http/auth.ts — 신규
/** Bearer가 있으면 검증해 principal 주입, 없으면 그대로 통과(익명). 무효 토큰은 401(위조 거부). */
export function optionalBearer(): RequestHandler { /* extractBearer→verifyToken, 실패 시 401, 없으면 next() */ }
```

`uploads.ts` 라우트: `router.use(requireApiKey()); router.use(optionalBearer());`

- **prepare**: principal이 TempUser면 **한도 선검사**(초과면 403 거부 — Storage 서명 URL을 아예 안 내줌 = 과금 원천 차단). principal 없음(게스트)/User↑는 통과.
- **commit**: principal이 TempUser면 **재검사 후** resultSession 생성 성공 시 **`qrUsedCount` 원자 증가**(§4.2). 트랜잭션으로 (한도 재확인 + increment + 문서 생성)을 원자화하면 경합 안전(§8.3).

> **왜 prepare·commit 양쪽 검사인가**: prepare 거부가 과금(Storage PUT)을 원천 차단하는 1차 방어.
> commit 재검사는 prepare~commit 사이 시간 경과/동시 세션으로 한도가 소진된 경우의 최종 방어 + 카운트 증가 지점.

### 5.2 업로드 초과 거부 계약

- 초과 시 **403** + 에러 봉투 `{error:{code, message}}`. code로 사유 구분:
  - `TEMP_USER_TIME_EXCEEDED` → 클라가 시간 문구 표시.
  - `TEMP_USER_COUNT_EXCEEDED` → 클라가 횟수 문구 표시.
- 클라 `HttpBackendClient.MapToDomainException`은 403→`UnauthorizedAccessException`(`:168`). QR 실패는 이미 우아 처리(`QrPopupViewModel.cs:100-111`)되므로 **업로드 시점 초과도 기존 실패 경로로 흡수**되며, 추가로 code를 읽어 정확한 문구를 노출(§9.3).

### 5.3 사용량 조회 엔드포인트 (클라 게이트용)

`GET /accounts/me/qr-usage` (requireBearer):

```
200 {
  role: "temp_user",
  blocked: boolean,
  reason: "ok"|"time"|"count",
  remainingMs: number,      // 시간 잔여(ms)
  remainingCount: number,   // 횟수 잔여
  limits: { qrHours, qrCount }
}
```

- 서버가 principal.id로 users doc 로드 → `createdAt`·`qrUsedCount` + config로 `evaluateQrGate` 실행해 응답.
- **비TempUser**(user/manager/admin)는 `blocked:false, reason:"ok"`(한도 없음) — 클라가 무제한 처리.
- 클라는 로그인 직후·설정 진입 시 조회(§7.3).

### 5.4 Admin 전역 한도 조회·수정

- `GET /config/temp-user-limits` (requireBearer) — 현재 한도 조회(모든 로그인 사용자 가능, 표시용). 문서 부재면 기본값 반환.
- `PATCH /config/temp-user-limits` (requireAdmin) — `{qrHours?, qrCount?}` 갱신. 범위 검증(예: qrHours 1~8760, qrCount 1~100000). Admin만(`requireAdmin`, `auth.ts:82`).

### 5.5 신규 라우트 마운트

`app.ts`에 `app.use("/config", configRouter())` 추가(`:24-28` 인근). `qr-usage`는 accounts 라우터에 서브경로로 추가하거나 별도 `me` 라우터. **권장**: `accountsRouter`에 `GET /me/qr-usage` 추가(이미 requireBearer 적용, `accounts.ts:29`).

---

## 6. 서버 계정 생성 + 역할 변경 — TempUser

### 6.1 생성

- `createAccount`(`accounts.ts:133-171`)는 `canCreate(actingRole, role)` 게이트(`:140`)를 이미 사용 → §3.4 `creatableRoles` 확장으로 **Admin·Manager가 TempUser 생성 자동 허용**. 로직 무변경.
- `validateRole`이 `temp_user` 허용(§3.4) → 라우트(`accounts.ts:40`) 자동 정합.
- **self-signup(`registerSelf`)은 role="user" 서버 강제**(`accounts.ts:395`) → TempUser는 self-signup 불가(스펙 7: Admin·Manager만). 변경 불필요. ✔
- **SSO 자동생성(`createGoogleAccount`)도 role="user"**(`accounts.ts:355`) → SSO로 TempUser 안 생김. 정합. ✔
- 신규 TempUser는 `createdAt=now`(기존 코드 `:154`) → 시간 한도 기준 자동 확보. `qrUsedCount`는 미설정(0 해석).

### 6.2 역할 변경(setRole) — 라우트·서비스 변경 (스펙 확대)

권한 매트릭스는 §8.7. 서버 구현 변경 2곳:

**(a) 라우트 게이트 완화**(`web/functions/src/routes/accounts.ts:120-131`): `PATCH /accounts/:id/role`에
현재는 라우트 레벨 역할 게이트가 없고 서비스(`setRole`)가 `actor.role !== "admin"`을 강제(`accounts.ts:232`).
스펙에 맞춰 **`requirePower()`를 라우트에 명시**(admin+manager 진입 허용)하고, **세부 매트릭스는 서비스에서 강제**.
(라우트에 `requirePower()` 추가는 manager가 아닌 user/temp_user의 role 변경 시도를 라우트 단에서 조기 403.)

**(b) 서비스 매트릭스 강제**(`accounts.ts:227-244` `setRole` 재작성): 현재 "admin만"(`:232`)을 §8.7 판정 순서로 확장:

```ts
export async function setRole(
  targetId: string, role: UserRole, actor: { id: string; role: UserRole }
): Promise<void> {
  if (!isPower(actor.role)) throw HttpError.forbidden("역할 변경은 파워 계정만 가능합니다.");
  if (role === "admin") throw HttpError.forbidden("admin 역할은 지정할 수 없습니다(최종 1인 규칙).");

  const currentRole = await getRole(targetId);       // 없으면 404
  if (currentRole === "admin") throw HttpError.forbidden("admin 계정의 역할은 변경할 수 없습니다.");
  if (currentRole === role) throw HttpError.invalid("이미 해당 역할입니다.");   // 무변경 거부(선택)

  if (actor.role === "admin") {
    // admin: (admin 지정/대상 제외) 모든 승격·강등 허용.
  } else {
    // manager: 오직 user→temp_user 강등만. 그 외 403.
    if (!(currentRole === "user" && role === "temp_user")) {
      throw HttpError.forbidden("매니저는 사용자를 임시 유저로 강등하는 것만 가능합니다.");
    }
  }
  await db().collection(COLLECTION).doc(targetId).update({ role });
  // 사용량 필드 처리(§8.7): user→temp_user 시 qrUsedCount 미설정 유지(0 해석), createdAt 불변.
}
```

- `isPower` 재사용(TempUser·user 제외). `validateRole`이 `temp_user` 허용하므로 라우트 role 파싱 정합.
- **admin 지정 불가·admin 대상 변경 불가**는 기존 유지(`:235-242` 로직 보존).
- 무변경(current==target) 거부는 선택 — 400 권장(UX상 불필요한 no-op 방지). 구현 재량.

---

## 7. 클라이언트 변경 (WPF)

### 7.1 개념 분리 재확인 (혼동 금지)

| 개념 | 저장소 | 성격 |
|------|--------|------|
| QR 토글(`EnableQrDelivery` 등) | 로컬 MCPhoto.ini(장치별) | 운영자가 이 부스에서 QR 쓸지 결정 |
| TempUser 한도/사용량 | 서버 users doc + config(계정별/전역) | 계정 과금 한도 |

**최상위 불변식(사용자 명시 제약)**: TempUser 한도 초과의 QR 강제 OFF는 **로컬 MCPhoto.ini의
`AppSettings.EnableQrDelivery`(및 SendPhoto/SendTimelapse) 값을 어떤 경우에도 변경하지 않는다.**
게스트 로그인 시 옵션이 강제 off되는 기존 패턴(§1.4-3)과 **정확히 동일하게**, 런타임 effective 오버라이드로만
처리한다. **한도 해제·역할 변경 시 저장값이 그대로 원복**되어야 한다(오버라이드가 걷히면 raw ini 값이 다시 유효).

**게스트와의 결정적 차이(반드시 반영)**:
- 게스트는 QR을 애초에 **진입조차 안 함** — 런타임 미실행 이유는 `ResultViewModel.Next`의 `&& _shell.IsLoggedIn`
  조건(`:148`)이지 `settings.EnableQrDelivery`가 false여서가 **아니다**(검증: 게스트여도 `Settings.Current.EnableQrDelivery`
  raw는 true일 수 있고 SettingsViewModel의 off는 표시 전용). 즉 **런타임 QR 게이트는 이미 "raw 설정값 + 로그인 여부"를
  조합**하고 있다(단일 조합 지점 `ResultViewModel.Next:148`, 미디어 선택은 `QrPopupViewModel:56-57`의 raw SendPhoto/SendTimelapse).
- **TempUser는 로그인 상태이므로 `IsLoggedIn` 조건을 통과한다** → 게스트처럼 "표시만 off"로는 런타임 QR을 못 막는다.
  따라서 TempUser 초과는 **설정 화면 표시 off(§7.3)에 더해 런타임 촬영/업로드 흐름도 effective OFF**여야 한다(스펙 6·사용자 요구 2).

**해법**: raw `AppSettings.EnableQrDelivery`를 직접 참조하던 산발적 지점들을 **역할+한도상태를 반영한
"effective QR enabled"를 계산하는 단일 지점(§7.1b `QrEffectivePolicy`)**으로 통일한다. ResultViewModel·QrPopupViewModel·
(향후 캡처 흐름)이 이 단일 지점을 참조하고, 서버 강제(§5)와 이중화한다. ini는 어디서도 미변경.

### 7.1b Effective QR 정책 — 단일 계산 지점 (신설, 사용자 요구 2 핵심)

`src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`(신규, 순수 로직 — 테스트 대상). raw 설정 + 역할 + 한도상태를
입력받아 **런타임에서 실제로 적용할 effective 값**을 계산한다. **ini를 읽거나 쓰지 않는다**(입력은 이미 로드된 값).

```csharp
namespace MCPhoto.Core.Settings;

/// <summary>
/// QR 전송의 런타임 effective 값 계산(순수). raw ini 설정(AppSettings) + 로그인 역할 + TempUser 한도상태를
/// 조합해 "지금 이 세션에서 QR을 실제로 켤지"를 결정한다. ⚠️ AppSettings(ini)를 절대 변경하지 않는다 — 오버라이드만.
/// 게스트(미로그인)·TempUser 초과 시 effective=false, 그 외에는 raw 값 그대로.
/// </summary>
public static class QrEffectivePolicy
{
    /// <summary>
    /// effective QR enabled. 규칙(우선순위 순):
    ///   1) 미로그인(게스트) → false (기존 ResultViewModel.Next의 IsLoggedIn 조건 흡수).
    ///   2) TempUser이고 한도 초과(blocked) → false (신규).
    ///   3) 그 외(User/Manager/Admin, 정상 TempUser) → raw EnableQrDelivery 그대로.
    /// </summary>
    public static bool IsQrEnabled(bool rawEnableQr, bool isLoggedIn, bool isTempUserBlocked)
    {
        if (!isLoggedIn) return false;
        if (isTempUserBlocked) return false;   // role==TempUser && blocked를 호출측이 이미 판정해 넘긴다
        return rawEnableQr;
    }
}
```

- `isTempUserBlocked`는 "역할이 TempUser이고 한도 초과"를 이미 합성한 bool(비TempUser는 항상 false). 셸이 계산해 노출(§7.5).
- 이 함수가 **런타임 QR on/off의 유일한 권위(클라측)**. 기존 `ResultViewModel.Next:148`의 인라인 조합을 이 호출로 대체한다(§7.4).
- SendPhoto/SendTimelapse는 QR이 effective on일 때만 의미가 있으므로, effective off면 미디어 경로도 자연히 null(진입 자체가 없음). 별도 오버라이드 불필요.

### 7.2 사용량 상태 서비스

신규 `IQrUsageService`(`MCPhoto.Core` 인터페이스 + `MCPhoto.Http` 구현):

```csharp
public interface IQrUsageService
{
    /// <summary>현재 로그인 계정의 QR 사용 게이트 상태 조회. 비TempUser·게스트는 Unlimited. 서버 미도달 시 null(호출측 폴백).</summary>
    Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default);
}

public sealed record QrUsageStatus(bool Blocked, QrGateReason Reason, TimeSpan RemainingTime, int RemainingCount)
{
    public static QrUsageStatus Unlimited => new(false, QrGateReason.Ok, TimeSpan.MaxValue, int.MaxValue);
}
public enum QrGateReason { Ok, Time, Count }
```

- HTTP 구현은 `GET accounts/me/qr-usage`(bearer:true) 호출. DI 팩토리는 UseBackend on일 때만 실구현, off면 `Unlimited` 반환 no-op(§12).
- **캐싱**: 로그인 세션 동안 1회 조회 후 보관(설정 진입 중 불변 전제 A5). QR 전송 성공 후·재로그인 시 무효화(§7.4).

### 7.3 설정 페이지 게이트 (SettingsViewModel)

게스트 3지점 패턴(§1.4)을 **TempUser 초과**로 확장:

1. **소스단 강제 off + 문구**: `LoadSettings` 말미에 TempUser이고 `Blocked`면 `EnableQrDelivery=false; SendPhoto=false; SendTimelapse=false;` + `QrLimitNotice` 문구 세팅.

```csharp
// LoadSettings 내 (게스트 블록과 별개, 로그인 TempUser 전용)
if (IsTempUserBlocked)   // 셸 사용량 상태에서 파생
{
    EnableQrDelivery = false; SendPhoto = false; SendTimelapse = false;
}
```

2. **저장 시 미기록(ini 원값 보존 — 최상위 불변식)**: `SaveSettings`에서 `if (!IsGuest && !IsTempUserBlocked){ s.EnableQrDelivery=…; s.SendPhoto=…; s.SendTimelapse=…; }` — 초과 TempUser의 표시 off가 **`s.EnableQrDelivery`를 절대 덮어쓰지 않도록** 게이트한다(게스트 `if (!IsGuest)`와 동일 형태로 `&& !IsTempUserBlocked` 추가). 이로써 한도 해제 시 다음 로드에서 raw ini 값이 그대로 원복(§7.1 불변식).
3. **XAML IsEnabled(read-only/disabled) + 문구**: QR 토글 `IsEnabled="{Binding CanEditQr}"`(= `IsLoggedIn && !IsTempUserBlocked`). TempUser 초과 시 토글은 disabled(수정 불가) + `QrLimitNotice`(시간/횟수 사유별 정확한 문구) 노출. 게스트 `GuestGateNote`(it12 R3)와 동형으로, TempUser 초과 노티는 별도 스타일 or 재사용.

```csharp
// 셸이 역할+한도를 이미 합성(§7.5) — SettingsViewModel은 읽기만(설정 진입 중 불변).
public bool IsTempUser => _shell.CurrentUser?.Role == UserRole.TempUser;
public bool IsTempUserBlocked => _shell.IsTempUserQrBlocked;
public bool CanEditQr => IsLoggedIn && !IsTempUserBlocked;
public string QrLimitNotice => _shell.TempUserQrReason switch
{
    QrGateReason.Time  => "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.",
    QrGateReason.Count => "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.",
    _ => string.Empty
};
```

> 문구는 §0 표와 **정확히 일치**(변경 금지). 시간 우선(§8.1)이므로 `Reason`이 이미 우선순위 반영.

### 7.4 런타임 QR 흐름 차단 — 단일 effective 지점 참조 (ResultViewModel / QrPopupViewModel)

**raw `settings.EnableQrDelivery` 직접 참조를 `QrEffectivePolicy.IsQrEnabled`(§7.1b) 호출로 대체**한다.
이것이 사용자 요구 2("설정 표시뿐 아니라 실제 QR 전송 차단 + 단일 effective 계산 지점")의 구현이다.

- `ResultViewModel.Next()`(`:148`) 인라인 조합 `settings.EnableQrDelivery && _shell.IsLoggedIn`을 단일 지점 호출로 치환:

```csharp
// 기존: if (settings.EnableQrDelivery && _shell.IsLoggedIn) → Qr; else → Done
// 개정: raw 설정·로그인·TempUser 한도상태를 QrEffectivePolicy 단일 지점에서 조합(ini 미변경).
bool qrEffective = QrEffectivePolicy.IsQrEnabled(
    rawEnableQr: settings.EnableQrDelivery,        // ini raw — 읽기만, 변경 없음
    isLoggedIn: _shell.IsLoggedIn,
    isTempUserBlocked: _shell.IsTempUserQrBlocked); // 셸이 역할+한도 합성(§7.5)
if (qrEffective) await _shell.NavigateAsync(AppState.Qr);
else             await _shell.NavigateAsync(AppState.Done);
```

  초과 TempUser는 QR 상태로 진입하지 않고 바로 Done(우아 — 팝업 없이 완료). **로컬 저장은 그대로**(QR 분기 이전에 실행, `:138-145`). ini의 `EnableQrDelivery`는 여전히 원값.

- **업로드 시점 초과 방어(이중화)**(effective 통과했으나 prepare~commit 사이 소진, 또는 클라 상태 stale): `QrPopupViewModel`이 이미 업로드 실패를 우아 처리(`:100-111`). 서버 403(사유 code) 수신 시 code를 읽어 문구를 정확히 표시(기존 "전송 실패" 문구를 사유별 §0 문구로 대체). 카운트 증가는 서버 commit 성공 시에만 발생하므로, 거부된 세션은 카운트 미증가(정합). **서버 강제가 최종 권위, effective는 UX·1차 방어**.
- **성공 후 무효화**: QR 전송 1회 성공 시 셸 캐시 상태 무효화(다음 진입 시 재조회 또는 `RemainingCount` 1 감소). 정확한 값은 서버가 권위 — 클라는 표시용.

> **effective vs ini 요약**: `IsQrEnabled`는 런타임 판정값일 뿐 어디에도 저장되지 않는다. `AppSettings.EnableQrDelivery`(ini)는 SettingsViewModel 게이트(§7.3-2)·이 정책 어디서도 write되지 않으므로, 한도 해제/역할 변경 즉시 raw 값이 다시 effective에 반영된다(원복).

### 7.5 셸 상태 — effective 데이터 흐름의 원천 (AppShellViewModel)

셸이 **역할+한도상태를 단일 bool `IsTempUserQrBlocked`로 합성**해 하위(§7.1b·§7.3·§7.4)에 공급한다. 데이터 흐름:

```
로그인 성공(SessionContext.CurrentUser 세팅)
  → 역할이 TempUser면 IQrUsageService.GetStatusAsync() 1회 호출(서버 사용량 상태)
  → 셸에 QrUsageStatus 보관(_tempUserQrStatus)
  → IsTempUserQrBlocked = (CurrentUser?.Role == TempUser) && (_tempUserQrStatus?.Blocked == true)
       └ 비TempUser(User/Manager/Admin/게스트)는 항상 false
  → ResultViewModel.Next / QrPopupViewModel / SettingsViewModel이 이 값을 QrEffectivePolicy·게이트에 주입
```

```csharp
// AppShellViewModel — 신규
private QrUsageStatus? _tempUserQrStatus;
/// <summary>TempUser이고 QR 한도 초과인지(역할+한도 합성). 비TempUser는 항상 false. effective QR 계산 입력(§7.1b).</summary>
public bool IsTempUserQrBlocked =>
    CurrentUser?.Role == UserRole.TempUser && _tempUserQrStatus?.Blocked == true;
/// <summary>초과 사유(설정 문구용). TempUser 아니거나 미초과면 Ok.</summary>
public QrGateReason TempUserQrReason => _tempUserQrStatus?.Reason ?? QrGateReason.Ok;
```

- **조회 시점**: `OnCurrentUserChanged`(`:117-124`)에서 TempUser로 로그인 시 `_ = LoadTempUserQrStatusAsync()`(fire-and-forget, UI 스레드 컨텍스트). 비TempUser·로그아웃 시 `_tempUserQrStatus = null` 클리어. (신규 이벤트 구독 없음 — 기존 구독에 로직 추가만이라 누수 0, `Dispose`는 그대로.)
- **서버 미도달로 조회 실패(null) 시**: `_tempUserQrStatus`가 null → `IsTempUserQrBlocked`가 false → **fail-open**(클라는 허용, 서버가 업로드 시 최종 거부). 서버가 권위이므로 클라 조회 실패로 정상 사용자를 막지 않는다(오픈이슈 O3 — 사용자 확정 필요). 과금 안전은 서버 업로드 거부(§5)가 담보.
- `SettingsViewModel`은 진입 시 `_shell.IsTempUserQrBlocked`/`_shell.TempUserQrReason`을 읽어 `IsTempUserBlocked`/`QrLimitNotice` 파생(설정 진입 중 불변 → `INotifyPropertyChanged` 불필요, 게스트 게이트와 동일 관례).

### 7.6 계정 생성 UI (AccountViewModel)

- `CreatableRoles`(§3.5) 확장으로 역할 콤보에 TempUser 자동 등장. 라벨 "임시 유저" 매핑 추가(§9.4).
- 생성 요청은 기존 `CreateAsync`(`HttpAccountService.cs:144`) 재사용 — role만 TempUser. 서버가 재검증.

### 7.7 Admin 전역 한도 수정 UI

- 관리자 도구(AccountMode.Admin, `AppShellViewModel.cs:377`) 또는 UserMgmt 화면에 "임시 유저 한도" 섹션 추가: 시간(h)·횟수 입력 2개 + 저장. Admin만 노출(`IsPower && Role==Admin`).
- `IQrUsageService`(또는 신규 `ITempUserLimitsService`)에 `GetLimitsAsync`/`SetLimitsAsync`(PATCH, requireAdmin) 추가. 서버가 403으로 비Admin 거부(이중 방어).

---

## 8. 엣지케이스 결정

### 8.1 둘 다 초과 → 시간 우선 (근거)

시간 초과는 **회복 불가**(경과 시간은 되돌릴 수 없음), 횟수는 관리자가 한도를 올리면 회복 가능. 더 근본적인
차단 사유인 시간을 우선 표시하는 것이 사용자 안내로 정확. `evaluateQrGate`가 `timeExceeded ? "time" : ...`로 구현(§4.3).

### 8.2 "성공 세션 1회" 확정 지점 = commit 최초 성공

- prepare는 URL 발급일 뿐 세션 미완성. **commit이 resultSession 문서를 만든 순간이 세션 완성**(§1.3).
- commit은 sessionId 중복 시 409(`uploads.ts:143`) → **재시도·재촬영으로 같은 sessionId 재commit 불가** → 이중집계 원천 차단.
- **재촬영/재시도로 새 세션**을 만들면 새 sessionId → 정당하게 새 카운트(사용자가 실제로 QR을 다시 전송 = 과금 발생). 정합.
- **commit만 실패한 경우**(PUT 성공, commit 네트워크 실패): resultSession 미생성 → **카운트 미증가**(Storage에 고아 파일은 남지만 세션 미완성 = 사용자에게 QR 미제공 = 카운트 안 함). 과금 관점 보수적(고아 파일은 TTL 정리 담당).

### 8.3 카운트 증가 원자성·경합

- commit 시 Firestore **트랜잭션**으로 (users doc 재읽기 → `evaluateQrGate` 재판정 → 초과면 abort/403 → 아니면 resultSession set + `qrUsedCount` increment)을 원자화. 동시 다중 세션이 마지막 1회를 두고 경합해도 트랜잭션 직렬화로 한 건만 통과.
- 단순 `FieldValue.increment`는 원자적이나 "증가 전 한도 재확인"이 필요하므로 트랜잭션 사용(read-modify-write).

### 8.4 시계/타임존 — 서버 UTC 권위

- 시간 한도는 **서버가 `Timestamp.now()` vs `createdAt`(둘 다 UTC)** 로 판정(`evaluateQrGate`에 서버 now 주입). 클라 시계 신뢰 안 함(과금 안전).
- 클라 표시용 `RemainingTime`은 서버 응답값 그대로 사용(클라 재계산 금지 — 시계 오차 회피).

### 8.5 오프라인/서버 미도달

- 조회 실패(§7.5): 권장 fail-open(클라 표시상 허용) + **업로드 시 서버가 최종 거부**. 서버 미도달이면 업로드 자체가 실패(기존 우아 처리) → 어차피 QR 미전송. 과금 없음.
- **서버가 최종 권위이므로 클라 오프라인이 과금 우회를 만들지 않는다**(업로드가 서버 경유 = prepare/commit 없으면 Storage URL 없음).

### 8.6 게스트(비로그인) 업로드

- 게스트는 QR 미진입(§1.4) → 업로드 안 함. optionalBearer는 게스트 익명 통과(principal 없음 → 한도 미적용). 기존 게스트 흐름 무영향(A2).

### 8.7 역할 변경(setRole) — 권한 매트릭스 (스펙 확대: 범위 안)

기존 마이그레이션은 없으나(자동 전환 없음), **관리자 UI를 통한 명시적 역할 변경은 지원**한다(콤보박스+Apply, TempUser 포함).
서버 `setRole`(`web/functions/src/services/accounts.ts:227-244`)가 세부 매트릭스를 강제한다. 라우트 게이트는
`requireAdmin`→**`requirePower`**(admin+manager)로 열되, **manager의 세부 제한은 서비스 계층에서 강제**한다(§10.1).

**권한 매트릭스** (actor = 요청자 역할, 셀 = current→target 전이 허용 여부):

| 전이(current → target) | 방향 | 허용 actor | 근거 |
|------------------------|------|-----------|------|
| TempUser → User | 승격(랭크↑) | **admin 전용** | 승격은 admin만 |
| TempUser → Manager | 승격(랭크↑) | **admin 전용** | 승격은 admin만 |
| User → Manager | 승격(랭크↑) | **admin 전용** | 승격은 admin만(기존과 정합) |
| User → TempUser | 강등(랭크↓) | **admin + manager** | 강등은 파워 |
| Manager → User | 강등(랭크↓) | **admin 전용** | manager는 아래 제한으로 배제 |
| Manager → TempUser | 강등(랭크↓) | **admin 전용** | manager는 아래 제한으로 배제 |
| → Admin (임의) | 승격 | **불가**(누구도) | admin 지정 불가(최종 1인, 기존 유지) |
| Admin → 임의 | — | **불가**(누구도) | admin 대상 변경 불가(기존 유지) |

**manager 세부 제한(서비스 강제)**: manager는 **오직 `current==user && target==temp_user`(현재 user 대상 temp_user 강등)만** 허용, 그 외 전이 요청은 **403**. admin은 위 표의 admin 관련 셀 전부 허용.

**판정 로직**(순수, 서버 `domain/roles.ts` 또는 `setRole` 내):
```ts
// 서버 setRole 강제 순서:
// 1) actor는 power(requirePower). 아니면 403(라우트).
// 2) target==="admin" → 403(admin 지정 불가).
// 3) current==="admin" → 403(admin 대상 변경 불가).
// 4) actor==="admin": (2)(3) 외 모든 전이 허용(승격·강등).
// 5) actor==="manager": current==="user" && target==="temp_user" 만 허용, 그 외 403.
// 6) current===target(무변경)도 거부하거나 무해 no-op(구현 선택 — 권장 400/무변경 방지).
```

> **강등 vs 승격 판정**: `MANAGE_RANK`(§3.2)로 `rank(target) > rank(current)`이면 승격. 승격은 admin 전용이라
> manager는 (5)의 유일 케이스(user→temp_user, 강등)만 통과 — 승격 요청은 자동 배제된다. rank 비교와 (5)의
> 명시 화이트리스트가 이중으로 manager를 좁힌다.

**비대칭 note(사용자 인지·추후 개선 예정)**: manager는 **user 계정을 생성**할 수 있으나(creatableRoles §3.1),
**temp_user→user 승격은 불가**(승격은 admin 전용)하다. 즉 manager가 만든 temp_user를 manager 스스로 user로
올릴 수 없는 비대칭이 존재한다. 이는 "승격은 admin만"이라는 안전 규칙의 부수효과로, 사용자가 인지하고 있으며
추후 개선 대상이다(예: manager에게 자신이 만든/관리하는 user↔temp_user 승강등 한정 허용). **이번 범위에서는 표대로 강제**.

**creatableRoles 불변**: 생성 게이트는 그대로 — admin→[user, manager, temp_user], manager→[user, temp_user](§3.1). 변경 없음.

**사용량 필드 처리(전환 시)**: user→temp_user 강등 시 `qrUsedCount`는 **미설정(0 해석)으로 시작하거나 기존 값 유지** —
권장은 **강등 시점을 새 시작으로 보지 않고 `createdAt` 기준 유지**(시간 한도는 계정 생성 시각 기준, §8.4·O6와 정합).
`qrUsedCount`가 없으면 0부터. temp_user→user 승격(admin) 시 한도 필드는 무의미해지므로 그대로 둔다(참조 안 함).
이 처리는 오픈이슈 **O5**에서 사용자 확정(강등 시 사용량 리셋 여부).

---

## 9. 클라 문구·라벨·UI 상세

### 9.1 역할 표시 라벨

`UserRole → 한글 라벨` 매핑(신규 헬퍼 또는 컨버터): TempUser="임시 유저", User="사용자", Manager="매니저", Admin="관리자". UserMgmt 목록·계정 생성 콤보·계정 팝오버에서 소비.

### 9.2 설정 페이지 QR 섹션

- TempUser 정상(미초과): QR 토글 정상 편집 가능(User와 동일).
- TempUser 초과: QR 토글 disabled + 사유 문구(§7.3). 게스트 `GuestGateNote`와 유사 위치에 `QrLimitNotice` 노출(별도 스타일 or 재사용).

### 9.3 QR 팝업 초과 안내(업로드 시점 거부)

- 서버 403 `TEMP_USER_TIME_EXCEEDED`/`TEMP_USER_COUNT_EXCEEDED` 수신 시 `StatusMessage`를 §0 정확 문구로. 로컬 저장은 유지(기존 `:108-110` 폴백 문구 로직 확장).

### 9.4 계정 생성 콤보

- Admin: [임시 유저, 사용자, 매니저], Manager: [임시 유저, 사용자] (§3.1 순서). 기본 선택은 기존 정책 유지.

### 9.5 UserMgmt 목록 + 역할 변경 UI

- TempUser 계정 행에 "임시 유저" 라벨 + (선택) 남은 시간/횟수 표시(Admin/Manager가 사용량 파악). 사용량 표시는 §5.3 조회를 계정별로 확장하거나 목록 응답에 포함(오픈이슈 O4 — MVP는 라벨만).
- **역할 변경 UI(스펙 확대)**: 각 계정 행(또는 상세)에 **역할 콤보박스 + [Apply] 버튼**. 콤보 항목은 §8.7 매트릭스로 필터한 **actor가 이 target에 대해 지정 가능한 역할만** 노출(클라 1차 필터, 서버가 최종 강제):
  - actor=admin, target=user → [user, temp_user, manager] (admin 제외, 승격·강등 모두).
  - actor=admin, target=temp_user → [temp_user, user, manager].
  - actor=admin, target=manager → [manager, user, temp_user].
  - actor=manager, target=user → [user, temp_user] (temp_user 강등만 실제 변경 가능; user는 무변경).
  - actor=manager, target=temp_user/manager → **역할 변경 UI 미노출**(manager는 승격·manager강등 불가).
  - target=admin → **역할 변경 UI 미노출**(admin 대상 변경 불가, 누구도).
- **Apply 동작**: 콤보 선택값으로 `IAccountService.SetRoleAsync(id, role)` 호출(기존 시그니처, `HttpAccountService.cs:221`). 성공 시 목록 갱신, 서버 403(매트릭스 위반) 시 사유 노출(우아). manager가 클라 필터를 우회해도 서버가 최종 거부(이중 방어).
- **클라 매트릭스 헬퍼**(순수, 테스트 대상): `RoleChangePolicy.AssignableRoles(actorRole, currentTargetRole)` → 콤보에 넣을 역할 목록. §8.7 판정을 클라에도 이식(C# `MCPhoto.Core`). 서버 `setRole` 매트릭스와 1:1(계약 정합).

```csharp
// src/MCPhoto.Core/Models 또는 Accounts — 순수, 서버 §8.7과 대칭.
public static class RoleChangePolicy
{
    /// <summary>actor가 target(현재 currentRole)에게 지정 가능한 역할 목록(콤보 필터). 빈 목록이면 역할변경 UI 미노출.</summary>
    public static IReadOnlyList<UserRole> AssignableRoles(UserRole actorRole, UserRole currentRole)
    {
        if (currentRole == UserRole.Admin) return Array.Empty<UserRole>();       // admin 대상 불가
        if (actorRole == UserRole.Admin)
            // admin: admin 제외 전부(승격·강등). currentRole 자신 포함 여부는 UI에서 무변경 처리.
            return new[] { UserRole.TempUser, UserRole.User, UserRole.Manager };
        if (actorRole == UserRole.Manager && currentRole == UserRole.User)
            return new[] { UserRole.User, UserRole.TempUser };                    // user→temp_user 강등만 유효
        return Array.Empty<UserRole>();                                           // 그 외 manager·비파워 미노출
    }
}
```

> **UI 비대칭 note 재확인(§8.7)**: manager는 자신이 만든 temp_user를 user로 되돌릴 수 없다(승격 admin 전용).
> 콤보에서 manager+target=temp_user는 미노출되므로 UI상 시도 자체가 막힌다. 추후 개선 시 이 헬퍼·서버 매트릭스를 함께 완화.

---

## 10. View ↔ ViewModel 매핑 (변경분)

| View | ViewModel | 변경 |
|------|-----------|------|
| (없음 — Core) | `QrEffectivePolicy` | **신규 단일 effective 지점**(raw ini + 로그인 + 한도 → 런타임 QR on/off). ini 미변경 |
| SettingsView.xaml | SettingsViewModel | QR 토글 `IsEnabled` → `CanEditQr`, `QrLimitNotice` 바인딩. SaveSettings `s.EnableQrDelivery` write에 `&& !IsTempUserBlocked` 게이트(ini 원값 보존) |
| ResultView(암묵) | ResultViewModel | `Next`의 raw `EnableQrDelivery && IsLoggedIn` 인라인 조합 → `QrEffectivePolicy.IsQrEnabled(...)` 단일 호출로 치환 |
| QrPopupView | QrPopupViewModel | 403 사유 code → §0 정확 문구(이중화 방어) |
| AccountView.xaml | AccountViewModel | 역할 콤보 TempUser 라벨(자동 등장) |
| UserMgmtView.xaml | UserMgmtViewModel | 역할 라벨 매핑 + (선택)한도 UI |
| (Admin 도구) | AccountViewModel(Admin) or UserMgmtViewModel | 전역 한도 수정 섹션 |
| (셸) | AppShellViewModel | `IsTempUserQrBlocked`(역할+한도 합성)·`TempUserQrReason` + 사용량 조회/무효화 |

바인딩·명령 누락 점검: `CanEditQr`/`IsTempUser`/`QrLimitNotice`는 설정 진입 중 불변 → `INotifyPropertyChanged` 불필요(게스트 게이트와 동일 관례, it12 §1.1). `IsTempUserQrBlocked`/`TempUserQrReason`은 로그인 변경 시 셸이 갱신(기존 `OnCurrentUserChanged` `:117-124`에 통지·조회 추가 — 신규 이벤트 구독 없음, 누수 0).

**ini 불변 검증 포인트(사용자 제약)**: `AppSettings.EnableQrDelivery`가 write되는 클라 지점은 오직 `SettingsViewModel.SaveSettings`(로그인·비초과 시)와 `IniSettingsService.Save`(그 값 직렬화). TempUser 게이트·`QrEffectivePolicy`·ResultViewModel·QrPopupViewModel 어디서도 write 없음 → 한도 상태와 무관하게 raw 값 보존.

---

## 11. 테스트 계획

**기준선 유지**: C# 기존 + web jest 기존 전부 green.

### C# 신규/수정
- `RoleManagementTests`: `CanManage` 위계표(§3.2) 전체, `CreatableRoles`/`CanCreate`에 TempUser, `ToFirestoreValue`/`ParseRole` `temp_user` 라운드트립.
- **(신규) `QrEffectivePolicyTests`**(순수, §7.1b): `IsQrEnabled` 진리표 — 미로그인→false(raw 무관), TempUser 초과→false, 정상 TempUser·User↑→raw 그대로(raw true→true, raw false→false). **핵심: raw=true·TempUser초과 시 effective=false지만 입력 raw는 불변**(오버라이드 확인).
- `SettingsViewModelTests`: TempUser 초과 시 `LoadSettings`가 QR 3필드 표시 강제 off + `CanEditQr=false` + `QrLimitNotice` 사유별 §0 정확 문구. **`SaveSettings`가 `s.EnableQrDelivery` 미기록(ini 원값 보존 — 기존 `Guest_Save_Preserves_Ini_Qr_And_Firebase` 동형으로 `TempUserBlocked_Save_Preserves_Ini_Qr` 신설)**. 정상 TempUser는 User와 동일(편집 가능·저장됨).
- `ResultViewModelTests`(있으면): TempUser 초과 시 `Next`가 Done으로(Qr 미진입) **하지만 `Settings.Current.EnableQrDelivery`는 여전히 true**(ini 미변경 단언); 정상 TempUser·User는 Qr 진입.
- (신규) `QrUsageStatus`/셸 `IsTempUserQrBlocked` 합성(역할+한도) 파생 로직 테스트.
- **(신규) `RoleChangePolicyTests`**(순수, §9.5·§8.7): `AssignableRoles` 진리표 — admin+비admin대상→[temp_user,user,manager], manager+user대상→[user,temp_user], manager+temp_user/manager대상→빈목록, 임의+admin대상→빈목록. **서버 setRole 매트릭스와 1:1 정합**(계약 드리프트 방지).

### web jest 신규/수정
- `roles.test.ts`: `temp_user` 파싱·isPower(false)·creatableRoles·canManage 위계표.
- `tempUserLimit.test.ts`(신규): `evaluateQrGate` — 시간만 초과/횟수만 초과/둘 다(시간 우선)/미초과/경계값(정확히 한도=초과), remaining 계산.
- `accounts.test.ts`: TempUser 생성(Admin/Manager 허용, User 거부), qrUsedCount increment. **setRole 매트릭스(§8.7)**: admin이 temp_user↔user·user→manager 등 승격·강등 성공; manager가 user→temp_user 성공; manager가 temp_user→user(승격)·user→manager·manager→user 403; admin 지정 403; admin 대상 변경 403; 비파워(user/temp_user) 라우트 403.
- `uploads.test.ts`(신규 또는 확장): optionalBearer로 TempUser 초과 시 prepare/commit 403(사유 code), commit 성공 시 카운트 증가, 게스트(무토큰) 통과, User↑ 무제한, 트랜잭션 경합(마지막 1회 단일 통과).
- config 라우트: PATCH requireAdmin(비Admin 403), GET 기본값 폴백.

---

## 12. 하위호환·롤백 (UseBackend off 레거시)

- TempUser는 **백엔드 전용 기능**. 레거시 Firebase(Admin) 경로(`MCPhoto.Firebase.*`)는 한도 강제 인프라(서버 트랜잭션·config)가 없다.
- **레거시 방침**: `IQrUsageService` Firebase 구현은 **`Unlimited` 반환 no-op**(TempUser 한도 미적용). 레거시 `AccountService`는 TempUser 생성 시 role 문자열만 저장(위계는 §3.1 매핑으로 정합), 단 **한도 강제는 하지 않음**(과금 방어는 백엔드 온라인 운영 전제 — it10에서 백엔드 기본 ON 확정).
- 롤백: 서버 변경은 additive(신규 필드·라우트·optionalBearer는 게스트/기존 사용자 무영향 A2). 클라 변경도 비TempUser·게스트 경로 불변. UseBackend off 시 TempUser 기능 자연 비활성.
- **파일 인코딩**: 기존 `.cs`는 **UTF-8 no BOM** 보존(한글 주석), TS도 기존 관례 유지. 신규 파일도 no BOM.

---

## 13. 구현 단계 (WBS)

### 검증된 사실 (요약 — 상세 §1)
- 역할 영속화는 문자열, `CanManage`/`canManage`만 서수 대소 비교(§1.1).
- 업로드는 익명(API 키만), sessionId는 클라 생성, commit 성공=세션 완성(§1.3).
- QR 진입은 로그인 상태에서만, 게스트 게이트 3지점 패턴 존재(§1.4). **런타임 QR 게이트는 이미 "raw 설정값(`EnableQrDelivery`) + 로그인 여부"를 조합**하며(단일 지점 `ResultViewModel.Next:148`), 게스트가 런타임 QR을 안 도는 이유는 `IsLoggedIn` 조건이지 ini 값이 아님(검증: grep). 미디어 선택은 raw `SendPhoto`/`SendTimelapse`(`QrPopupViewModel:56-57`). **`AppSettings.EnableQrDelivery`를 write하는 클라 지점은 `SettingsViewModel.SaveSettings`(로그인·비게이트 시)뿐** → TempUser 게이트를 여기서만 막으면 ini 불변 보장.

### 미검증 가정 → 검증 단계 (§2 표 참조: A1→S1/S6, A2→S3/S8, A3→S4/S5, A4→S4, A5→S9/S10, A6→S1/S6). **사용자 제약(ini 불변)은 Step 8(런타임 effective, ini 미write)·Step 9(SaveSettings 원값 보존)에서 회귀 테스트로 검증.**

---

### Step 1: C# 역할 모델 확장 + CanManage 서수 제거
- **Context Brief**: `UserRole` enum에 TempUser 최하위 추가. 위계 비교가 enum 서수에 의존하면(`CanManage`의 `(int)` 비교) 붕괴하므로, 서수와 분리한 명시 랭크(switch)로 재작성한다. 저장은 문자열(`temp_user`)이라 배치값 변경은 무해.
- **대상 파일**: `src/MCPhoto.Core/Models/UserRole.cs`, `tests/MCPhoto.Tests/RoleManagementTests.cs`
- **선행 조건**: 없음
- **구현 내용**: enum에 `TempUser=0`(§3.1) + 나머지 명시값; `ToFirestoreValue`/`ParseRole`에 `temp_user`; `CreatableRoles`에 Admin/Manager→TempUser; `CanManage`를 `ManageRank` switch로 재작성(§3.2); 테스트에 위계표·라운드트립 추가(§3.3).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~RoleManagement`
- **완료 기준**:
  - [관측] `CanManage` 위계표(§3.2) 12+ 케이스 전부 PASS; `temp_user` 라운드트립 PASS.
  - [non-goal] 기존 User/Manager/Admin 간 관리 판정 결과 불변(기존 InlineData 그대로 PASS).
  - [trigger] 없음(순수 로직).
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 2: 서버 스키마·순수 게이트 로직 + 문자열 매핑(TS)
- **Context Brief**: 계정별 사용량(`qrUsedCount`)·전역 한도(config)·초과 판정 순수 함수를 추가한다. 판정은 시간 우선(둘 다 초과 시 time).
- **대상 파일**: `web/functions/src/services/dto.ts`(UserDoc.qrUsedCount, TempUserLimitsDoc), `web/functions/src/domain/tempUserLimit.ts`(신규 `evaluateQrGate`), `web/functions/src/__tests__/tempUserLimit.test.ts`(신규)
- **선행 조건**: 없음(Step 6과 독립)
- **구현 내용**: DTO 필드 추가(§4.1·§4.3); `evaluateQrGate`(§4.3); 경계·시간우선·remaining 테스트(§11).
- **검증 명령**: `cd web/functions && npm test -- tempUserLimit`
- **완료 기준**:
  - [관측] 시간만/횟수만/둘다(→time)/미초과/경계(=한도→blocked) 케이스 PASS.
  - [non-goal] 기존 dto 소비 코드 컴파일 유지(`npm run build`).
  - [trigger] 없음(순수 함수).
- **롤백**: 신규 파일 삭제 + dto 필드 제거.
- [ ] 완료

### Step 3: 업로드 선택적 Bearer 미들웨어
- **Context Brief**: 업로드는 현재 익명(API 키만). TempUser 한도를 강제하려면 계정 신원이 필요하다. Bearer가 있으면 검증·주입, 없으면 익명 통과(게스트 무영향), 무효 토큰은 401.
- **대상 파일**: `web/functions/src/http/auth.ts`(optionalBearer), `web/functions/src/routes/uploads.ts`(미들웨어 장착)
- **선행 조건**: 없음
- **구현 내용**: `optionalBearer()` 추가(§5.1); uploads 라우터에 `requireApiKey()` 뒤 `optionalBearer()` 장착. 이 단계에선 principal 주입까지만(한도 적용은 Step 4).
- **검증 명령**: `cd web/functions && npm test -- uploads && npm run build`
- **완료 기준**:
  - [관측] 유효 Bearer → req.principal 주입; 무토큰 → principal 없이 통과(prepare/commit 정상); 무효 Bearer → 401.
  - [non-goal] 게스트(무토큰) 업로드 기존 동작 불변(prepare/commit 200/201).
  - [trigger] principal은 Authorization 헤더 존재 시에만.
- **롤백**: uploads 라우터에서 optionalBearer 제거.
- [ ] 완료

### Step 4: 업로드 한도 강제 + 카운트 증가(트랜잭션) + config 로더
- **Context Brief**: TempUser principal이면 prepare에서 한도 선검사(초과 403), commit에서 트랜잭션으로 재검사+resultSession 생성+qrUsedCount increment. 전역 한도 config 로더는 문서 부재 시 기본값(48h/30회).
- **대상 파일**: `web/functions/src/services/uploads.ts`, `web/functions/src/services/config.ts`(신규 loadTempUserLimits or 기존 config 확장), `web/functions/src/routes/uploads.ts`, `web/functions/src/__tests__/uploads.test.ts`
- **선행 조건**: Step 2(evaluateQrGate), Step 3(principal)
- **구현 내용**: prepare 선검사(§5.1); commit 트랜잭션(한도 재판정→abort/403 or set+increment, §8.3); 403 사유 code(§5.2); config 로더 기본값 폴백(§4.3).
- **검증 명령**: `cd web/functions && npm test -- uploads`
- **완료 기준**:
  - [관측] TempUser 시간/횟수 초과 시 prepare·commit 403(정확 code); commit 성공 시 qrUsedCount +1; 게스트/User↑ 무제한 통과; 동시 마지막 1회 경합 시 1건만 성공.
  - [non-goal] 비TempUser·게스트 업로드 카운트·거부 없음.
  - [trigger] 한도 적용은 principal.role==="temp_user"일 때만.
- **롤백**: uploads 한도 분기 제거(Step 3 상태로).
- [ ] 완료

### Step 5: 사용량 조회 + Admin 한도 config 라우트
- **Context Brief**: 클라 게이트용 `GET /accounts/me/qr-usage`(requireBearer)와 Admin 전역 한도 `GET/PATCH /config/temp-user-limits`.
- **대상 파일**: `web/functions/src/routes/accounts.ts`(me/qr-usage), `web/functions/src/routes/config.ts`(신규), `web/functions/src/app.ts`(마운트), `web/functions/src/__tests__/accounts.test.ts`·(config 테스트)
- **선행 조건**: Step 2, Step 4
- **구현 내용**: qr-usage 응답(§5.3); config GET(기본값)·PATCH(requireAdmin, 범위검증, §5.4); app.ts `/config` 마운트(§5.5).
- **검증 명령**: `cd web/functions && npm test -- accounts config && npm run build`
- **완료 기준**:
  - [관측] TempUser qr-usage가 blocked/reason/remaining 정확; 비TempUser는 blocked:false; PATCH가 Admin만(비Admin 403); GET 문서부재 시 48h/30회.
  - [non-goal] 기존 accounts 라우트 동작 불변.
  - [trigger] PATCH는 requireAdmin 통과 시에만.
- **롤백**: 신규 라우트/마운트 제거.
- [ ] 완료

### Step 6: TS roles.ts 위계 재작성 + 서버 계정생성/역할변경 매트릭스
- **Context Brief**: `roles.ts`를 C#과 대칭으로(temp_user, MANAGE_RANK, creatableRoles 확장). createAccount는 canCreate 게이트를 이미 쓰므로 로직 무변경. **역할변경(setRole)은 스펙 확대로 매트릭스(§8.7) 강제**: 라우트 게이트 `requirePower`, 서비스에서 승격=admin전용·user→temp_user강등=admin+manager·manager는 user대상 temp_user강등만.
- **대상 파일**: `web/functions/src/domain/roles.ts`, `web/functions/src/domain/validation.ts`(문구), `web/functions/src/services/accounts.ts`(`setRole` 매트릭스 §6.2), `web/functions/src/routes/accounts.ts`(role 라우트 `requirePower`), `web/functions/src/__tests__/roles.test.ts`, `accounts.test.ts`
- **선행 조건**: 없음(Step 1과 대칭, 독립 검증)
- **구현 내용**: §3.4 전체; validateRole 에러 문구; **§6.2 setRole 매트릭스 재작성 + role 라우트 requirePower**; 테스트 위계표·TempUser 생성 허가/거부·역할변경 매트릭스.
- **검증 명령**: `cd web/functions && npm test -- roles accounts`
- **완료 기준**:
  - [관측] temp_user 파싱·isPower(false)·canManage 위계표·creatableRoles PASS; Admin/Manager가 TempUser 생성 허용, User 거부; **setRole 매트릭스: admin은 승격·강등 허용(admin지정/대상 제외), manager는 user→temp_user만 허용·그 외 403, admin지정 403, admin대상 403**.
  - [non-goal] 기존 user/manager/admin 판정·생성 게이트 불변; creatableRoles 불변.
  - [trigger] setRole은 requirePower 통과 + 매트릭스 충족 시에만 update.
- **롤백**: roles.ts/validation.ts/accounts.ts(services·routes) revert.
- [ ] 완료

### Step 7: 클라 사용량 서비스 + 셸 상태
- **Context Brief**: `IQrUsageService`(Core 인터페이스 + Http 구현, DI 팩토리 UseBackend 분기)와 셸의 `IsTempUserQrBlocked`(로그인 시 1회 조회, 로그아웃 무효화).
- **대상 파일**: `src/MCPhoto.Core/Accounts/IQrUsageService.cs`(신규), `src/MCPhoto.Http/HttpQrUsageService.cs`(신규), `src/MCPhoto.App/ServiceRegistration.cs`(등록), `src/MCPhoto.App/AppShellViewModel.cs`(상태·무효화), 대응 테스트
- **선행 조건**: Step 5(엔드포인트)
- **구현 내용**: §7.2·§7.5. 서버 미도달 시 fail-open(null→Unlimited 취급, 서버 최종 거부). Firebase(off) 구현은 Unlimited no-op(§12).
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj` + `dotnet test --filter FullyQualifiedName~QrUsage`
- **완료 기준**:
  - [관측] TempUser 로그인 시 셸이 상태 보관, `IsTempUserQrBlocked` 반영; 로그아웃 시 클리어; 비TempUser는 항상 false.
  - [non-goal] 게스트·User/Manager/Admin 흐름 불변.
  - [trigger] 조회는 로그인(CurrentUser 세팅) 시 1회.
- **롤백**: 신규 서비스·셸 상태 제거, DI 등록 revert.
- [ ] 완료

### Step 8: Effective QR 정책 단일 지점 + 런타임 흐름 치환(ResultViewModel/QrPopupViewModel)
- **Context Brief**: 사용자 제약 — TempUser 한도 초과의 QR OFF는 **로컬 ini(`AppSettings.EnableQrDelivery`)를 절대 변경하지 않고** 런타임 effective 오버라이드로만 처리한다(게스트 패턴 동형, 한도 해제 시 원복). 게스트와 달리 TempUser는 로그인 상태라 표시 off만으로 런타임을 못 막으므로, raw 설정 직접 참조를 **`QrEffectivePolicy.IsQrEnabled`(신규 순수 단일 지점)**로 통일해 촬영/업로드 흐름도 effective OFF한다. 서버 강제(Step 4)와 이중화.
- **대상 파일**: `src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`(신규 순수), `src/MCPhoto.App/ViewModels/ResultViewModel.cs`, `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs`, `src/MCPhoto.Http/HttpFirebaseClient.cs`(403 code 전파), `tests/MCPhoto.Tests/QrEffectivePolicyTests.cs`(신규) + 대응 VM 테스트
- **선행 조건**: Step 4(서버 거부), Step 7(셸 상태 `IsTempUserQrBlocked`)
- **구현 내용**: §7.1b·§7.4. `QrEffectivePolicy.IsQrEnabled(rawEnableQr, isLoggedIn, isTempUserBlocked)`; `ResultViewModel.Next`의 인라인 `settings.EnableQrDelivery && _shell.IsLoggedIn`을 이 호출로 치환(raw는 읽기만); QrPopup 403 code→§0 문구; MapToDomainException/BackendException code 전달 확인(기존 code 필드 `HttpBackendClient.cs:146`).
- **검증 명령**: `dotnet test --filter "FullyQualifiedName~QrEffectivePolicy|FullyQualifiedName~ResultViewModel|FullyQualifiedName~QrPopup"`
- **완료 기준**:
  - [관측] `IsQrEnabled` 진리표 PASS; 초과 TempUser는 Next→Done(Qr 미진입) **AND `Settings.Current.EnableQrDelivery`는 호출 전후 불변(true 유지)**; 정상 TempUser·User는 Qr 진입; 업로드 403 시 §0 정확 문구 + 로컬 보존.
  - [non-goal] **ini의 `EnableQrDelivery`/`SendPhoto`/`SendTimelapse`는 이 단계 어느 경로에서도 write되지 않는다**(effective는 계산값일 뿐 미저장); 게스트(로그인 아님)는 기존대로 Done, 카운트·조회 없음.
  - [trigger] QR 런타임 차단은 `IsQrEnabled==false`(미로그인 or TempUser초과)일 때만; 문구는 403 사유 code에 따라.
- **롤백**: 신규 정책 파일 삭제 + 두 VM revert(인라인 조합 복원).
- [ ] 완료

### Step 9: 설정 페이지 게이트(SettingsViewModel + XAML) — ini 원값 보존
- **Context Brief**: 게스트 3지점 패턴(LoadSettings 표시 off / SaveSettings 미기록 / XAML IsEnabled)을 TempUser 초과로 확장한다. **핵심 제약: `SaveSettings`가 `s.EnableQrDelivery`를 절대 덮어쓰지 않아** 한도 해제 시 raw ini 값이 원복돼야 한다(게스트 `if (!IsGuest)`와 동형으로 `&& !IsTempUserBlocked` 추가). 사유별 §0 정확 문구 노출.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, `src/MCPhoto.App/Views/SettingsView.xaml`, `tests/MCPhoto.Tests/SettingsViewModelTests.cs`
- **선행 조건**: Step 7(셸 상태)
- **구현 내용**: §7.3. `CanEditQr`(=`IsLoggedIn && !IsTempUserBlocked`)/`IsTempUser`/`QrLimitNotice`(셸 `TempUserQrReason` 파생); LoadSettings에 TempUser 초과 표시 off 블록; SaveSettings의 QR 3필드 write에 `&& !IsTempUserBlocked` 가드; XAML QR 토글 `IsEnabled="{Binding CanEditQr}"` + 문구 바인딩.
- **검증 명령**: `dotnet test --filter FullyQualifiedName~SettingsViewModel` + XamlResourceTests
- **완료 기준**:
  - [관측] TempUser 초과 시 QR 3토글 표시 off·disabled + §0 정확 문구(시간/횟수 사유별); **`TempUserBlocked_Save_Preserves_Ini_Qr` 테스트: 초과 TempUser로 저장해도 `IniSettingsService.Load().EnableQrDelivery`가 관리자 원값(true) 유지**; 정상 TempUser는 편집 가능·저장됨.
  - [non-goal] 게스트 게이트·User/Admin 설정 편집 불변; **ini의 운영자 QR 값 어떤 경우에도 클로버 없음**(초과 상태 저장 후에도 raw 보존).
  - [trigger] 표시 off·문구는 `IsTempUserBlocked`일 때만; 저장 미기록도 `IsTempUserBlocked`일 때만.
- **롤백**: SettingsViewModel/XAML revert.
- [ ] 완료

### Step 10: 계정 생성·역할 라벨·역할 변경 UI·Admin 한도 UI
- **Context Brief**: 계정 생성 콤보에 TempUser(라벨 "임시 유저") 자동 등장 확인, UserMgmt 역할 라벨, **역할 변경 콤보+Apply(§8.7 매트릭스)**, Admin 전역 한도 수정 섹션. 클라 역할 변경 콤보는 `RoleChangePolicy.AssignableRoles`(순수)로 필터하되 서버가 최종 강제.
- **대상 파일**: `src/MCPhoto.App/ViewModels/AccountViewModel.cs`, `UserMgmtViewModel.cs`, 대응 View, `src/MCPhoto.Core/.../RoleChangePolicy.cs`(신규 순수), 역할 라벨 헬퍼/컨버터, (한도 서비스 클라이언트), 테스트(`RoleChangePolicyTests` 신규)
- **선행 조건**: Step 1(CreatableRoles·MANAGE_RANK), Step 5(config 엔드포인트), Step 6(서버 setRole 매트릭스), Step 7(서비스)
- **구현 내용**: §7.6·§7.7·§9.4·§9.5. 역할 라벨 매핑; 생성 콤보 순서; **역할 변경 콤보+Apply(`RoleChangePolicy.AssignableRoles`로 필터 → `SetRoleAsync`)**; Admin 한도 GET/PATCH UI(Admin만).
- **검증 명령**: `dotnet build` + `dotnet test --filter "FullyQualifiedName~Account|FullyQualifiedName~UserMgmt|FullyQualifiedName~RoleChangePolicy"` + XamlResourceTests
- **완료 기준**:
  - [관측] Admin/Manager 생성 콤보에 "임시 유저" 노출; UserMgmt에 TempUser 라벨; **`RoleChangePolicy.AssignableRoles` 진리표 PASS(§8.7): admin→모든 비admin 대상 전부, manager+user대상→[user,temp_user], manager+temp_user/manager 대상→빈목록, admin대상→빈목록)**; 역할변경 콤보가 이 목록만 노출; Apply가 `SetRoleAsync` 호출; Admin이 한도 저장 시 서버 반영.
  - [non-goal] manager에게 승격·manager강등 콤보 미노출; admin 대상 역할변경 UI 미노출; 비Admin에게 한도 수정 UI 미노출; 기존 계정 생성 역할 목록 정합.
  - [trigger] 역할 변경은 Apply 클릭 시에만; 한도 저장은 Admin 저장 버튼 클릭 시에만.
- **롤백**: 관련 VM/View + RoleChangePolicy revert.
- [ ] 완료

### Step 11: 통합 회귀 + 문서 갱신
- **Context Brief**: 전체 빌드·테스트 green 확인, 분석 문서 갱신.
- **대상 파일**: (검증) 전체, `docs/analysis/*`(구조·인프라 변경 반영), 이 설계 문서 상태
- **선행 조건**: Step 1~10
- **구현 내용**: `dotnet test`(전체) + `cd web/functions && npm test` 전량 green; 역할·업로드 신원화·한도 추가를 분석 문서에 반영.
- **검증 명령**: `dotnet test` (전체) && `cd web/functions && npm test`
- **완료 기준**:
  - [관측] C#·jest 전체 PASS(신규 포함, 0 fail); 빌드 0 warning.
  - [non-goal] 기존 테스트 회귀 0.
  - [trigger] 없음.
- **롤백**: 해당 없음(검증 단계).
- [ ] 완료

---

## 14. 완결성 게이트 (self-check)

- [x] 검증된 사실(§1)/미검증 가정(§2) 분리, 가정마다 검증 단계 매핑.
- [x] 11개 단계 각 7필드(Context Brief/대상/선행/구현/검증/완료기준/롤백) 채움.
- [x] 완료 기준 관측 3문 형식(UI 단계 non-goal·trigger 포함).
- [x] 검증 명령 자동 실행 가능(dotnet/npm CLI).
- [x] 순수 로직 분리(evaluateQrGate, ManageRank, QrGateReason, **QrEffectivePolicy.IsQrEnabled**).
- [x] 이벤트 구독 해제 경로: 셸 사용량 상태는 기존 `CurrentUserChanged` 구독(이미 `Dispose`에서 해제 `AppShellViewModel.cs:417`)에 무효화만 추가 — 신규 구독 없음(누수 0).
- [x] 파일 인코딩 보존 명시(§12).
- [x] **사용자 제약(ini 불변) 반영**: 로컬 `AppSettings.EnableQrDelivery`는 어떤 경로에서도 write 안 함 — 한도 OFF는 `QrEffectivePolicy` 런타임 오버라이드만(§7.1·§7.1b·§7.4), SaveSettings 게이트로 원값 보존(§7.3-2·Step 9), 회귀 테스트로 고정(§11 `TempUserBlocked_Save_Preserves_Ini_Qr` + ResultViewModel ini 불변 단언).

---

## 15. 핵심 요약

1. **역할 서수 재배치는 위험** — `CanManage`가 유일하게 서수(`(int)`) 대소 비교에 의존한다. TempUser를 넣되 **위계 비교를 명시 랭크(switch)로 분리**해 서수 의존을 제거한다(C#·TS 대칭). 저장은 문자열(`temp_user`)이라 배치값 변경 자체는 무해.
2. **업로드 신원화가 아키텍처 핵심** — 업로드는 현재 익명(API 키만). TempUser 한도를 **서버가 강제**하려면 업로드에 계정 JWT를 실어야 한다(`optionalBearer`). 게스트는 익명 통과(무영향).
3. **과금 안전 = 서버 권위** — prepare 선검사(Storage URL 원천 차단) + commit 트랜잭션 재검사·카운트 증가. "성공 세션 1회 = commit 최초 성공"(sessionId 중복 409로 이중집계 원천 차단).
4. **클라는 표시·차단만 + 로컬 ini 절대 미변경(사용자 제약)** — 한도 OFF는 `AppSettings.EnableQrDelivery`를 건드리지 않고 **`QrEffectivePolicy.IsQrEnabled`(신규 단일 지점)**의 런타임 오버라이드로만 처리(게스트 패턴 동형, 한도 해제 시 원값 원복). 게스트는 QR 미진입으로 끝나지만 TempUser는 로그인 상태라 이 단일 지점이 촬영/업로드 흐름까지 effective OFF한다. 설정 화면은 게스트 3지점(표시 off/저장 미기록/disabled)을 확장하되 raw ini 보존. 문구는 §0 정확 문구, 시간 우선.
5. **레거시(UseBackend off)는 미강제** — TempUser는 백엔드 전용. Firebase 경로는 Unlimited no-op.
6. **역할 변경(setRole) 매트릭스(스펙 확대)** — 승격(랭크↑)=admin 전용, user→temp_user 강등=admin+manager, manager는 오직 user→temp_user만. admin 지정·admin 대상 변경 불가(유지). 라우트 `requirePower`, 세부는 서비스 강제. **비대칭 note**: manager는 user 생성은 되나 temp_user→user 승격은 불가(승격 admin 전용) — 사용자 인지, 추후 개선. 클라 콤보는 `RoleChangePolicy.AssignableRoles`로 필터, 서버 최종 강제.

---

## 16. 오픈이슈 — 사용자 승인 필요

| # | 이슈 | 권장안 | 승인 필요 사유 |
|---|------|--------|---------------|
| **O1** | Firestore 저장 문자열 = `"temp_user"`(snake_case) 확정 | `"temp_user"` | 한번 저장되면 계약 고정. `"tempuser"`/`"temp"` 등 대안 배제 확인 필요 |
| **O2** | 업로드에 **optionalBearer** 도입(익명→선택적 신원). 이것이 유일하게 "TempUser 한도를 서버가 강제"하는 실현 경로 | 도입 | 게스트 흐름·기존 업로드 계약에 미들웨어 추가 — 아키텍처 변경 승인 |
| **O3** | 서버 사용량 조회 실패(오프라인) 시 클라 **fail-open**(허용, 서버 최종 거부) vs fail-closed(막음) | fail-open | 과금 안전(서버 권위)과 UX 트레이드오프 — 정책 결정 |
| **O4** | UserMgmt 목록에 TempUser **잔여 사용량 표시** 여부(MVP는 라벨만) | MVP 라벨만, 사용량은 후속 | 목록 응답에 사용량 포함 = 추가 서버 조회/조인. 범위 결정 |
| **O5** | 역할 변경(`setRole`)은 **스펙 확대로 지원 확정**(§8.7 매트릭스). 남은 결정: user→temp_user **강등 시 사용량(qrUsedCount·시간 기준) 리셋 여부** | 리셋 안 함(`createdAt` 기준 유지, qrUsedCount 기존값/0) | 강등된 계정에 즉시 한도가 적용될지(생성시각 오래됐으면 바로 시간초과) vs 강등 시점부터 새로 시작할지 — 과금·UX 결정 |
| **O6** | 시간 한도 판정 기준시각 = **계정 `createdAt`**(생성 시점). "첫 QR 사용 시점"이 아님 | createdAt | 스펙 2 문언("createdAt부터")과 일치하나, 발급 후 미사용 대기 시간도 소진됨 — 의도 확인 |
| **O7** | Admin 한도 수정 UI 위치(AccountMode.Admin 도구 vs UserMgmt 화면) | 관리자 도구 섹션 | UX 배치 — 사용자 선호 확인 |

> **가장 승인이 시급한 것은 O2**(업로드 신원화). 이것 없이는 "서버가 TempUser 업로드를 거부"라는 스펙 6을
> 구현할 수 없다(현재 업로드는 계정을 모름). 대안은 sessionId를 계정과 서버가 연결하는 별도 등록 단계이나,
> optionalBearer가 기존 계약을 가장 적게 흔든다.
