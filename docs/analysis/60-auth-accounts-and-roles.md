# 60 · 인증 · 계정 · 역할

| 항목 | 내용 |
| --- | --- |
| 문서 | 60-auth-accounts-and-roles.md |
| 범위 | MCPhoto의 계정 역할 위계(temp_user/user/advanced_user/manager/admin + 게스트), 권한 매트릭스, Google SSO 로그인/로그아웃 흐름, 진입 PIN 게이트, 계정 저장소(백엔드 API 경유 `users` 컬렉션)와 CRUD·cascade 삭제 |
| 최종 업데이트 | 2026-07-29 (it16 — §1·§2 최신화 + **§3~§5 전면 재작성**: it13~it16 반영 / 로그아웃 JWT 폐기 수정 반영, 폐기된 USER-ACTIONS 링크 정리) |
| 관련 소스 경로 | `src/MCPhoto.Core/Accounts/{IAccountService,IGoogleSignInService,GoogleOAuthPkce,GoogleSsoNotConfiguredException}.cs`, `src/MCPhoto.Core/Models/{UserRole,RoleChangePolicy,User}.cs`, `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`, `src/MCPhoto.Http/HttpAccountService.cs`, `src/MCPhoto.Http/{HttpBackendClient.cs,Session/BackendSession.cs}`, `src/MCPhoto.App/{SessionContext,AppShellViewModel}.cs`, `src/MCPhoto.App/MainWindow.xaml`, `src/MCPhoto.App/Services/{GoogleSignInService,PinPromptDialogService}.cs`, `src/MCPhoto.App/Views/PinPromptWindow.xaml.cs`, `src/MCPhoto.App/ViewModels/{LoginGuestViewModel,AccountViewModel,UserMgmtViewModel,FrameSelectViewModel}.cs`, `web/functions/src/routes/{auth,accounts}.ts`, `web/functions/src/services/{accounts,googleAuth,dto}.ts`, `web/functions/src/domain/{roles,jwt,accountId}.ts` |
| 갱신 규칙 | `UserRole` enum·`IsPower`/`CanWriteFrames`/`CanManage`/`CreatableRoles`/`CanCreate` 규칙, `RoleChangePolicy.AssignableRoles`(서버 `canSetRole`과 1:1), `IAccountService` 시그니처(현재 7메서드), Google SSO 흐름(loopback+PKCE ↔ `POST /auth/google`)과 JWT 보관 위치, 진입 PIN 게이트(`AppShellViewModel.EnsurePinGateAsync`)의 호출부·fail-closed 규약, 상단 바 팝오버 항목·가시성 바인딩(`MainWindow.xaml`), 세션 단일 소스(`SessionContext`)의 Login/Logout/Reset 계약이 바뀌면 이 문서를 갱신한다. |

관련 문서: [10 Exe 앱 아키텍처](./10-exe-app-architecture.md) · [30 Firebase 연동](./30-backend-firebase-integration.md) · [40 Firestore/Storage 스키마](./40-database-firestore-and-storage-schema.md) · [70 로깅/이슈 진단](./70-logging-and-troubleshooting.md) · 인덱스 [README](./README.md)

> 🆕 **역할·권한 규칙(§1·§2)은 플랫폼 무관 공통 규격이므로 모든 클라이언트가 이 문서를 따른다.** 반면 §3의 로그인 **구현 방식**(loopback + 시스템 브라우저)은 데스크톱 전용이다 — iOS·iPadOS·Android·웹의 OAuth 흐름과 그에 필요한 **서버 확장**은 **[61 · 플랫폼별 인증 통합](./61-auth-platform-integration.md)** 에 있다. §3.4 PIN 게이트의 플랫폼 중립 규격도 61 §7에 정리돼 있다.

> ⚠️ 자격증명(it15 갱신, 사실): **비밀번호 개념은 폐지됐다.** 자격증명은 ① Google SSO(신원 — 서버가 id_token 검증) + ② `pinHash`(설정·계정 관리 진입 게이트, bcrypt 4자리 PIN) 두 가지뿐이며 `users` 문서의 `password`·`emailVerified` 필드는 삭제됐다(설계 `docs/design/wpf-it15-google-only-auth-design.md` §5.3). it15 이전의 "평문 비밀번호 저장·비교"는 **이력**이다. "웹 접근 전면 차단"이 여전히 `users` 컬렉션의 방어선이다([40 §5.1](./40-database-firestore-and-storage-schema.md#51-firestore-webfirestorerules)).

> ✅ 문서 동기화 상태(2026-07-29): **§1~§5 전부 it16 기준 최신**이다. §3~§5는 이날 it13~it16(Google SSO 단일 경로·진입 PIN·백엔드 프록시 경유 계정 CRUD·`advanced_user`)에 맞춰 **전면 재작성**됐다. 종전의 id/pw 로그인 흐름·`ChangePasswordAsync`·시드 계정 비밀번호·"SSO 미지원" 서술은 **이력**이며 아래 각 절에 "~에서 폐지"로 남겨 두었다.
>
> ℹ️ [70 §6](./70-logging-and-troubleshooting.md#6-백엔드-연결-실패-진단)도 2026-07-29에 "백엔드 연결 실패 진단"으로 재작성됐다(구 "Firebase 초기화 실패 진단" 폐기). 백엔드 미도달 시 계정 관점의 동작은 [§4.5](#45-백엔드-미도달-시-동작-구-미초기화-폴백-재정의), 로그 기반 절차는 70 §6이다.

---

## 1. 역할 종류와 위계

역할은 `UserRole` enum **5종**이며, 여기에 **비로그인 = 게스트**(enum 값 아님, `CurrentUser == null` 상태)가 더해진다.
it13이 최하위 `temp_user`를, **it16이 `advanced_user`(고급 유저)** 를 추가했다.

위계(관리 판정 기준): **TempUser(0) < User(1) < AdvancedUser(2) < Manager(3) < Admin(4)**

`UserRole` 정의: `src/MCPhoto.Core/Models/UserRole.cs:4-24`

| 역할 | enum 값 | Firestore 저장값 | UI 라벨 | 소스 설명(주석) | 근거 |
| --- | --- | --- | --- | --- | --- |
| 게스트 | (없음) | — | — | 비로그인 상태. `SessionContext.CurrentUser == null` | `src/MCPhoto.App/SessionContext.cs:13-14`, `src/MCPhoto.App/AppShellViewModel.cs:69` |
| temp_user | `UserRole.TempUser` | `"temp_user"` | 임시 유저 | "user와 동기능 + QR 전송만 시간·횟수 한도. 위계 최하위" | `src/MCPhoto.Core/Models/UserRole.cs:10-11` |
| user | `UserRole.User` | `"user"` | 사용자 | "AppSettings 관리. **it16부터 프레임은 사용만**(생성·편집·삭제 불가)" | `src/MCPhoto.Core/Models/UserRole.cs:13-14` |
| **advanced_user** | `UserRole.AdvancedUser` | `"advanced_user"` | **고급 유저** | "User 권한 + 프레임 생성·편집·삭제(개인 로컬). **power 아님**(계정 관리 권한 없음)" | `src/MCPhoto.Core/Models/UserRole.cs:16-17` |
| manager | `UserRole.Manager` | `"manager"` | 매니저 | "advanced_user + 사용자 관리 + 공용 기본 프레임 관리" | `src/MCPhoto.Core/Models/UserRole.cs:19-20` |
| admin | `UserRole.Admin` | `"admin"` | 관리자 | "manager + manager 지정(최종 1인)" | `src/MCPhoto.Core/Models/UserRole.cs:22-23` |

- 문자열 매핑은 `UserRoleExtensions.ToFirestoreValue`(`:29-37`)와 `ParseRole`(`:39-47`)이 담당하며, 알 수 없는 값은 `user`로 안전 폴백한다(`:36`, `:46`). it16 이후 `user`는 프레임 쓰기 권한이 없으므로 이 폴백은 **fail-closed 방향**이다.
- UI 라벨은 `ToLabel()`(`:67-75`) 한 곳이 담당하며, 사용자 관리 목록·역할 콤보·계정 화면·진단 모달·토스트가 모두 이 값을 쓴다(`RoleLabelConverter`, `src/MCPhoto.App/Converters/CommonConverters.cs`).
- **enum 배치값(서수)은 위계 순으로 명시**돼 있고, it16에서 `AdvancedUser=2`를 끼워 넣으며 Manager·Admin이 2·3 → **3·4로 이동**했다. 저장·전송은 전부 문자열이고 위계 비교는 `ManageRank` switch이므로 배치값 변경은 무해하다(`UserRole.cs:6-8` 주석, `:99-107`).
- 서버(TypeScript) 측 동일 계약: `web/functions/src/domain/roles.ts`의 `UserRole` union·`MANAGE_RANK`(0/1/2/3/4)·`isUserRole` 화이트리스트·`parseRole`이 C#과 1:1로 동결돼 있다.

### 1.1 `IsPower()` — "파워" 계정 개념

`IsPower()`는 **manager 또는 admin**을 뜻하며(`src/MCPhoto.Core/Models/UserRole.cs:54`), 사용자 관리·공용 기본 프레임(DB) 관리 권한의 게이트로 코드 전반에서 재사용된다.

```
public static bool IsPower(this UserRole role) => role is UserRole.Manager or UserRole.Admin;
```

> ⚠️ **it16: AdvancedUser는 power가 아니다.** `IsPower()`는 확장되지 않았다 — 고급 유저에게는 계정 관리 권한이 전혀 없다. 프레임 저작 권한은 [1.2](#12-canwriteframes--프레임-저작-권한-축-it16-신규)의 **별개 축**이며 두 판정을 서로 대체하지 않는다. 서버 `isPower()`(manager/admin)도 동일하게 불변이며, 그 뒤에 있는 프레임 쓰기 라우트가 열리지 않도록 회귀 테스트(`web/functions/src/__tests__/authGates.test.ts`)가 고정한다.

`IsPower()` 소비 지점:
- 상단 바 파워 여부: `AppShellViewModel.IsPower` → 팝오버의 "관리자 도구" 노출 제어(`src/MCPhoto.App/MainWindow.xaml:65-69`).
- 계정 페이지 파워 판정: `AccountViewModel.IsPower`(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:90`), 사용자 관리 진입 가드(`:222`).
- 프레임 화면 파워 판정: `FrameSelectViewModel.IsPower`(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:82`) → "서버에서도 제거" 옵션 노출·유효(`:123`), DB 공용 프레임 편집·삭제 허용(`FrameEditPolicy`).
- 사용자 관리 액션 게이트: PIN 재설정은 `IsPower()`를 **직접 부르지 않고** `CanResetPin()`(power 항 포함)에 위임한다(`UserMgmtViewModel.cs:70`, `:204`) — [1.3.1](#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계).

### 1.2 `CanWriteFrames()` — 프레임 저작 권한 축 (it16 신규)

프레임 **생성·편집·삭제** 권한을 `IsPower()`와 분리된 독립 축으로 도입했다(`src/MCPhoto.Core/Models/UserRole.cs:63-64`).

```
public static bool CanWriteFrames(this UserRole role)
    => role is UserRole.AdvancedUser or UserRole.Manager or UserRole.Admin;
```

| 역할 | `IsPower()` | `CanWriteFrames()` | 의미 |
| --- | :-: | :-: | --- |
| 게스트 | — | — | 프레임 사용만(공용 프레임) |
| temp_user | × | × | 프레임 **사용만**(본인 기존 프레임 포함) |
| user | × | × | 프레임 **사용만** (it16 변경) |
| **advanced_user** | **×** | **○** | 프레임 저작(개인 로컬) — 계정 관리 권한 없음 |
| manager | ○ | ○ | 프레임 저작 + 공용 DB 등록 + 계정 관리 |
| admin | ○ | ○ | manager + manager 지정 |

- **두 축을 혼용하지 않는다**: `IsPower` = 계정 관리·공용 DB 프레임 관리, `CanWriteFrames` = 프레임 저작. 소스 주석이 이 경계를 명시한다(`UserRole.cs:56-62`).
- `ManageRank` 부등식으로 구현하지 않고 **명시 열거**를 유지한다 — 관리 위계에 새 역할이 끼어들 때 저작 권한이 조용히 따라 움직이는 것을 막는다.
- 소비 지점: `FrameEditPolicy.CanEdit`/`CanDelete`(`src/MCPhoto.Core/Frames/FrameEditPolicy.cs`), `FrameSelectViewModel.CanCreateFrame`·`CanDeleteFrames`(`:80-81`), `FrameEditorViewModel.Save` fail-closed 가드. 화면·권한 상세는 [11 §4](./11-exe-app-features.md#4-프레임-생성--편집에디터--삭제).
- **서버 측 대응 축은 없다**(의도). advanced_user의 프레임은 개인 로컬 저장뿐이라 서버 쓰기 요청이 발생하지 않고, 세 프레임 쓰기 라우트(`POST /frames`, `PUT /frames/:id`, `DELETE /frames/:id`)는 계속 `requirePower()` 뒤에 있어 advanced_user 이하는 **403**이다(설계 `wpf-it16-advanced-user-role-design.md` §5.2).

### 1.3 `CanManage()` — 관리 위계 판정

`CanManage(actingRole, targetRole)`는 **자신과 같거나 낮은 위계**만 관리(삭제 등)할 수 있다는 규칙이며, 서수가 아닌 명시 랭크 `ManageRank`로 판정한다(`src/MCPhoto.Core/Models/UserRole.cs:120-121`). 예) manager는 admin을 관리할 수 없고, admin은 다른 admin도 관리할 수 있다.

> ⚠️ **이 판정만으로는 비power도 통과한다.** 같은 위계를 허용하므로 `temp_user`가 다른 `temp_user`를 "관리 가능"으로 계산한다. 따라서 관리 액션 게이트는 **`IsPower()`와 함께** 써야 한다(삭제) 또는 **`CanResetPin()`에 위임**한다(PIN 재설정 — power 항 포함, [1.3.1](#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계)). `CanManage`/`canManage` 자체의 의미는 **변경하지 않았다** — 계정 삭제(`deleteAccount`)와 공유되므로 "엄격히 높은 위계"로 좁히면 admin↔admin·manager↔manager 삭제가 회귀한다.

#### 1.3.1 `CanResetPin()` — PIN 재설정 전용 판정 (엄격히 낮은 위계)

타 계정 PIN 재설정(E3)만 **`CanManage`보다 한 칸 좁은** 별도 축을 쓴다(`src/MCPhoto.Core/Models/UserRole.cs:135-136` ↔ 서버 `web/functions/src/domain/roles.ts:128`, 1:1 대칭):

```
CanResetPin(acting, target) = IsPower(acting) && ManageRank(target) < ManageRank(acting)
```

- **동급 차단**이 핵심이다: `manager → manager` **×**(매니저 PIN은 **admin만** 재설정), `admin → admin` **×**(admin은 최종 1인이라 실사용 도달 없음). `admin → manager` ○, `manager → advanced_user/user/temp_user` ○.
- 근거: PIN은 설정·계정 관리 진입의 **유일한 자격증명**이므로, 동급 계정이 서로의 진입 자격을 갈아치울 수 있는 것은 과대 권한이다. 종전 판정 `IsPower() && CanManage(target)`은 `CanManage`가 동급을 허용해 매니저가 다른 매니저의 PIN을 바꿀 수 있었다.
- **`CanManage`는 좁히지 않았다**(삭제와 공유 — 좁히면 admin↔admin·manager↔manager 삭제가 회귀). 따라서 **매니저는 다른 매니저를 삭제할 수는 있으나 PIN은 재설정할 수 없다**는 비대칭이 남아 있다([5](#5-향후-개선-여지현재-비범위) 참조).
- 회귀 가드: `tests/MCPhoto.Tests/RoleManagementTests.cs`(`CanResetPin_Requires_Power_And_Strictly_Lower` + `CanManage_Still_Allows_Same_Rank_For_Delete`), `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs`(동급 매니저 행 미노출·커맨드 차단), `web/functions/src/__tests__/roles.test.ts`·`accounts.test.ts`(동급 403).

### 1.4 역할 지정·변경 매트릭스

역할 변경 권한은 순수 함수 한 쌍으로 표현되며 **서버가 최종 강제**한다:
클라 `RoleChangePolicy.AssignableRoles(actor, current)`(콤보 필터, `src/MCPhoto.Core/Models/RoleChangePolicy.cs`) ↔ 서버 `canSetRole(actor, current, target)`(`web/functions/src/domain/roles.ts`)이 1:1 대칭이다.

**규칙(it16)**

```
1) target == admin            → 거부 (최종 1인 규칙)
2) current == admin           → 거부 (admin 대상 변경 불가)
3) actor == admin             → 허용 (target ∈ {temp_user, user, advanced_user, manager})
4) actor == manager           → current·target 둘 다 하위 3역할 대역
                                {temp_user, user, advanced_user}일 때만 허용(승격 포함)
5) 그 외 actor(advanced_user·user·temp_user) → 거부
```

**전수 표** (행 = actor + 대상의 현재 역할, 열 = 지정할 새 역할. T=temp_user, U=user, **A=advanced_user**, M=manager, D=admin)

| actor | current \ new | T | U | **A** | M | D |
| --- | --- | :-: | :-: | :-: | :-: | :-: |
| **admin** | T | ○ | ○ | **○** | ○ | × |
| **admin** | U | ○ | ○ | **○** | ○ | × |
| **admin** | **A** | ○ | ○ | **○** | ○ | × |
| **admin** | M | ○ | ○ | **○** | ○ | × |
| **admin** | D | × | × | × | × | × |
| **manager** | T | ○ | ○ | **○** | × | × |
| **manager** | U | ○ | ○ | **○** | × | × |
| **manager** | **A** | ○ | ○ | **○** | × | × |
| **manager** | M | × | × | × | × | × |
| **manager** | D | × | × | × | × | × |
| advanced_user / user / temp_user | 전부 | × | × | × | × | × |

**it13 대비 변경점(it16)**

| 조합 | it13 | it16 |
| --- | --- | --- |
| manager: T→U, T→A, U→A | 거부(승격=admin 전용) | **허용** — 하위 3역할 대역은 manager가 자유 지정 |
| manager: A→U, A→T | (역할 없음) | **허용** |
| manager: U→T | 허용 | 허용(불변) |
| manager: *→M, manager/admin 대상 | 거부 | 거부(불변 — manager·admin 지정은 admin 전용) |
| admin: 전부(admin 제외) | 허용 | 허용 + `advanced_user` 추가 |
| 비power actor | 거부 | 거부(불변) |

- **no-op(current == target)은 허용**된다(멱등 write). 클라이언트는 무변경을 서버로 보내지 않는다(`UserMgmtViewModel`이 `target == user.Role`이면 return).
- `AssignableRoles` 반환 순서는 **위계 오름차순**(T → U → A → M)으로 고정한다 — 콤보 표시 순서가 곧 위계 순이다.
- 자기 계정 행은 콤보를 빈 목록으로 강제한다(`UserRowViewModel`, `UserMgmtViewModel.cs:50`).
- **PIN 재설정 권한(it16 정리)**: `PUT /accounts/:id/pin`에 **`requirePower()`를 추가**했다. 종전에는 로그인 + `canManage`만 통과하면 됐으므로 `temp_user`가 다른 `temp_user`의 PIN을 재설정할 수 있었다(it15로 신규 SSO 계정이 전원 temp_user가 되며 모집단이 커졌다). 형제 라우트인 `DELETE /accounts/:id`·`PATCH /accounts/:id/role`에는 있던 게이트가 PIN에만 빠져 있던 것이다. 판정식(변경 후) = `isPower(actor) && canManage(actor.role, targetRole) && actor.id !== targetId`(자기 자신 대상은 계속 **400** — 본인은 `PUT /accounts/me/pin` 사용). 클라 대칭: `CanResetPin = !isSelf && actorRole.IsPower() && actorRole.CanManage(user.Role)`(`UserMgmtViewModel.cs:52`)와 커맨드 가드(`:141`) → **UI 미노출 · 커맨드 가드 · 서버 403** 3중 방어. 사용자 관리 화면 자체가 power 전용 도달 경로라 실사용 UX 변화는 없다.

### 1.5 계정 생성 위계 게이트 (it15 이후 비활성)

`CreatableRoles()`/`CanCreate()`는 "누가 어떤 역할을 만들 수 있나"를 표현하는 순수 규칙이다(`src/MCPhoto.Core/Models/UserRole.cs:84-93`).

| 호출자(actingRole) | 생성 가능 역할 | 근거 |
| --- | --- | --- |
| admin | temp_user, user, **advanced_user**, manager | `src/MCPhoto.Core/Models/UserRole.cs:86` |
| manager | temp_user, user, **advanced_user** | `:87` |
| advanced_user / user / temp_user / 게스트 | (없음) | `:88` (그 외 → `Array.Empty`) |

- **admin → admin 생성 불가**("최종 1인" 규칙).
- ⚠️ **it15에서 계정 생성 경로(UI·HTTP 라우트)가 폐지**되어 이 두 함수의 프로덕션 호출자는 **0이다**(테스트만 참조). 신규 계정은 Google SSO 최초 로그인 시 서버가 `temp_user`로 자동 생성한다. it16에서 **삭제하지 않고 목록만 [1.4](#14-역할-지정변경-매트릭스) 매트릭스와 맞춰 두었다** — 훗날 이 함수가 되살아날 때 모순된 규칙이 조용히 부활하는 것을 막기 위함이다. 제거 여부는 [90 §1](./90-roadmap-and-future-work.md#1-알려진-이슈--기술-부채)에 이연 등재.

---

## 2. 권한 매트릭스(화면·기능별)

`○`=가능, `×`=불가/미노출, `△`=조건부. "게스트"는 비로그인. **T**=temp_user, **U**=user, **A**=advanced_user(고급 유저), **M**=manager, **D**=admin.

| 기능 / 화면 | 게스트 | T | U | **A** | M | D | 근거 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 촬영(프레임 선택→촬영→결과→QR) | ○ | ○ | ○ | ○ | ○ | ○ | 촬영 흐름은 로그인 요구 없음. 홈→프레임 선택 전이에 계정 조건 없음(`src/MCPhoto.Core/Navigation/SessionStateMachine.cs`); "게스트 직행" 설계(it2) |
| 기본(공용) 프레임 사용 | ○ | ○ | ○ | ○ | ○ | ○ | `FrameSelectViewModel.OnEnterAsync`가 항상 기본 프레임 로드 |
| 커스텀(본인) 프레임 사용 | × | ○ | ○ | ○ | ○ | ○ | 로그인 사용자만 본인 프레임 추가 로드(`GetUserFramesAsync(user.Id)`). **it16 E4**: 프레임 권한을 잃은 T·U의 기존 프레임도 **목록에 그대로 노출되고 촬영에 쓸 수 있다**(편집·삭제만 불가) |
| 프레임 생성(편집기 진입) | × | × | × | **○** | ○ | ○ | `CanCreateFrame = Role.CanWriteFrames()`(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:80`) + `CreateFrame` 커맨드 가드(`:218`) + XAML `Visibility`(`Views/FrameSelectView.xaml:88`). A는 개인 로컬만, M·D는 신규 생성분을 공용 DB에 등록 |
| 프레임 편집("선택 편집") | × | × | × | **○**(본인 로컬) | ○ | ○ | `FrameEditPolicy.CanEdit(frame, role, userId)` — `CanWriteFrames` 먼저 확인 후 출처 판정(`UserLocal`=본인 것, `DbDefault`=power만, 번들·fallback=불가). 저장 경로에도 fail-closed 가드(`FrameEditorViewModel.Save`) |
| 프레임 삭제 — 로컬(파일 제거) | × | × | × | **○** | ○ | ○ | `FrameEditPolicy.CanDelete(frame, role)`(it16 신설) — 로컬 저장분 ○, DB 공용은 power만, 번들·fallback·빈 Id ×. `CanDeleteFrames = Role.CanWriteFrames()`(`:81`) + 컨버터 + `RequestDelete` 가드 |
| 프레임 삭제 — 서버(공용·DB 문서+Storage) | × | × | × | × | ○ | ○ | "서버에서도 제거" 체크는 `IsPower`에서만 노출·유효(`alsoServer = DeleteAlsoServer && IsPower`, `FrameSelectViewModel.cs:123`). 서버 `DELETE /frames/:id`는 `requirePower()` |
| 계정 관리 페이지(내 정보 · PIN 변경) | × | ○ | ○ | ○ | ○ | ○ | 팝오버 "계정 관리"는 로그인 전원(`src/MCPhoto.App/MainWindow.xaml:62-64`). 진입 시 **PIN 게이트**(`AppShellViewModel.EnsurePinGateAsync` — 미설정이면 최초 설정 강제) |
| 관리자 도구(사용자 관리 진입) | × | × | × | **×** | ○ | ○ | 팝오버 "관리자 도구"는 `IsPower`에서만 노출(`MainWindow.xaml:65-69`); `OpenUserManagement`도 `IsPower` 가드(`ViewModels/AccountViewModel.cs:222`). **A는 계정 관리 권한 없음** |
| 사용자 목록 조회 | × | × | × | × | ○ | ○ | 사용자 관리 진입 자체가 파워 전용(위 항목); `GetAllAsync`는 "power 전용" |
| 사용자 삭제(cascade) | × | × | × | × | ○ | ○ | `UserMgmtViewModel.DeleteUser`(`:108-127`); 자기 계정 삭제 방지 + **자기와 같거나 낮은 위계만**(`ActorRole.CanManage(target)` — manager는 admin 삭제 불가). UI 미노출(`RoleActionVis`) + 명령 가드 + 서버 `requirePower` |
| 타 계정 PIN 재설정 | × | × | × | **×** | △ | ○ | `CanResetPin(target)` = power + **엄격히 낮은 위계**(`UserMgmtViewModel.cs:70`, `:204`) + 서버 `PUT /accounts/:id/pin`(`requirePower` + `canResetPin`). **M은 T·U·A만**(다른 M의 PIN은 재설정 불가 — **매니저 PIN은 D 전용**), **D는 M 이하 전부**(다른 D는 불가). [1.3.1](#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계) |
| 본인 PIN 변경 | × | ○ | ○ | ○ | ○ | ○ | `AccountViewModel.ChangePin` → `PUT /accounts/me/pin`. 자기 자신을 `:id` 경로로 부르면 400 |
| 역할 지정·변경 | × | × | × | × | △ | △ | 매트릭스는 [1.4](#14-역할-지정변경-매트릭스). **M**은 하위 3역할 대역(T·U·**A**) 안에서 자유 지정(승격 포함), **D**는 admin 제외 전부. 콤보 필터(`RoleChangePolicy.AssignableRoles`) + 커맨드 게이트(`UserMgmtViewModel.cs:173`) + 서버 `canSetRole` 최종 강제 |
| TempUser QR 한도(시간·횟수) 변경 | × | × | × | × | × | ○ | `CanEditTempUserLimits => Role == UserRole.Admin`(`ViewModels/AccountViewModel.cs:87`) + 서버 `requireAdmin`·범위 검증(it13) |
| 앱 종료(관리자) | × | × | × | × | ○ | ○ | "관리자 도구" 페이지의 `ExitApp`(`ViewModels/AccountViewModel.cs:227-228`); 도구 페이지 진입이 파워 전용 |
| 설정(앱 설정) 페이지 접근 | ○ | △ | △ | △ | △ | △ | 우상단 ⚙ 버튼은 상단 바 표시 상태면 누구나(`MainWindow.xaml:44-51`). **게스트는 무가드**, 로그인 사용자는 **PIN 게이트** 통과 필수(`AppShellViewModel.OpenSettings`, `:376-384`) |
| 설정 항목 편집 | △ | △ | ○ | ○ | ○ | ○ | 게스트는 일부 항목(거울모드·재촬영·필터·QR 전송·Firebase 항목) 편집 불가(it12 R1). **T**는 QR 한도 초과 시 QR 관련 편집만 차단(`CanEditQr = IsLoggedIn && !IsTempUserBlocked`, `ViewModels/SettingsViewModel.cs:78`). **A는 이 축에서 U와 동일** |

주의(사실/가정 구분):
- **사용자 관리 액션의 역할 위계**(사실): **삭제**는 행위자와 **같거나 낮은** 위계의 계정에만 노출·허용된다(`UserRole.CanManage`, `RoleActionVisibilityConverter` + `IsPower`). **PIN 재설정만 한 칸 좁다** — power + **엄격히 낮은** 위계(`UserRole.CanResetPin`)라서 매니저는 다른 매니저의 PIN을 재설정할 수 없다(관리자 전용, [1.3.1](#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계)). 두 액션 모두 manager는 admin 대상 불가. UI 미노출과 VM 명령 가드로 이중 방어하고 서버가 최종 강제한다.
- **프레임 권한은 `IsPower`가 아니라 `CanWriteFrames` 축**(사실, it16): 고급 유저는 프레임을 만들 수 있지만 계정 관리·공용 DB 등록은 못 한다. 두 축을 혼용하면 조용한 권한 오판이 된다([1.2](#12-canwriteframes--프레임-저작-권한-축-it16-신규)).
- **프레임 권한을 잃은 계정의 기존 프레임은 읽기 전용으로 남는다**(사실, it16 E4): 목록 노출·촬영 사용은 유지하고 편집·삭제 UI만 사라진다. 파일 삭제·소유권 이전·마이그레이션은 **하지 않는다**.
- **설정 페이지는 역할 게이트가 없다**(사실): `OpenSettings`에 역할 검사는 없고 **로그인 사용자에 대한 PIN 게이트**만 있다(it14·it15). 게스트는 여전히 무가드이며, 계정·관리자 기능은 `Account`/`UserMgmt`로 분리돼 있으므로 앱 설정 자체는 키오스크 운영자가 접근하는 열린 화면이라는 것이 코드상 현재 상태다.
- **운영 동선(it16)**: 기존 `user` 계정은 이번 변경으로 프레임 생성·편집 권한을 잃는다. 프레임을 만들어야 하는 계정은 **사용자 관리 → 역할 콤보 → "고급 유저" → 변경**으로 승격한다. manager도 이 승격을 할 수 있다([1.4](#14-역할-지정변경-매트릭스)).

---

## 3. 로그인 / 로그아웃 흐름

> **it15 전면 개편(사실)**: ID/PW 로그인·회원가입·이메일 인증·비밀번호 재설정이 **클라·서버 양쪽에서 전량 삭제**됐다.
> 남은 로그인 수단은 **Google SSO 하나**다(`src/MCPhoto.Core/Accounts/IAccountService.cs:11-21`,
> `web/functions/src/routes/auth.ts:1-7`). 제거된 HTTP 경로는 410 스텁 없이 404로 떨어진다(`auth.ts:6`).

### 3.1 세션 단일 소스와 토큰 보관 위치

계정 진실 소스는 두 개의 싱글턴으로 **역할이 나뉜다**. 혼동하지 않는다.

| 홀더 | 무엇을 들고 있나 | 수명 | 근거 |
| --- | --- | --- | --- |
| `SessionContext` | `CurrentUser`(도메인 `User`) — 화면·권한 판정의 유일한 근거 | 앱 사용 동안(촬영 세션보다 상위) | `src/MCPhoto.App/SessionContext.cs:11-14`, `:7-8` |
| `IBackendSession` | 로그인 JWT + 같은 `User` 참조 — HTTP `Authorization: Bearer` 조립용 | 앱 프로세스 수명(**메모리 전용, 디스크 미저장**) | `src/MCPhoto.Http/Session/IBackendSession.cs:6-9`, `Session/BackendSession.cs:9-42` |

- `SessionContext.CurrentUser`는 `private set`이고 진입점은 `Login`/`Logout`/`Reset(clearUser)`뿐이다(`SessionContext.cs:14`, `:46-51`, `:53-59`, `:61-80`). 변경 시 `CurrentUserChanged`로 통지한다(`:17`, `:50`, `:58`).
- 상단 바는 이 이벤트를 구독해 자동 갱신하고(`src/MCPhoto.App/AppShellViewModel.cs:140`, `:144-159`), 셸 `Dispose`에서 반드시 해제한다(`:477`).
- 상단 바 계정 상태는 **미러 없이** 세션에서 직접 읽는다(`AppShellViewModel.cs:66-70`): `IsLoggedIn` = `CurrentUser != null`, `IsGuest` = `CurrentUser == null`, `IsPower` = `CurrentUser?.Role.IsPower() == true`. 좌측 라벨 `AccountLabel`은 비로그인 "로그인", 로그인 시 계정 ID(`:89`).
- 계정이 바뀔 때마다 TempUser QR 사용량 상태를 재평가한다 — 로그아웃·비TempUser는 즉시 클리어, TempUser는 서버 1회 조회(fire-and-forget, 실패는 fail-open)(`AppShellViewModel.cs:152-158`, `:165-185`).

### 3.2 상단 바 계정 버튼 동작

`OpenAccount` 커맨드(`src/MCPhoto.App/AppShellViewModel.cs:413-421`):

| 상태 | 좌상단 계정 버튼 클릭 결과 | 근거 |
| --- | --- | --- |
| 비로그인 | 로그인 페이지(오버레이 진입, `AppState.Login`) | `AppShellViewModel.cs:420` |
| 로그인 | 계정 팝오버 토글(`IsAccountPopupOpen`) | `:417-418` |

팝오버 항목 — **it15에서 3항목으로 축소**됐다(`src/MCPhoto.App/MainWindow.xaml:54-75`):

| 항목 | 노출 조건 | 커맨드 → 이동 | 근거 |
| --- | --- | --- | --- |
| 계정 관리 | 로그인 전원 | `OpenAccountManageCommand` → `Account(Account)` | `MainWindow.xaml:62-64`; `AppShellViewModel.cs:431-433` |
| 관리자 도구 | `IsPower` | `OpenAdminToolsCommand` → `Account(Admin)` | `MainWindow.xaml:66-69`; `AppShellViewModel.cs:435-437` |
| 로그아웃 | 로그인 전원 | `LogoutCommand` | `MainWindow.xaml:70-72`; `AppShellViewModel.cs:446-453` |

- **폐지 이력**: "비밀번호 변경"·"계정 생성" 항목은 it15에서 사라졌다. `AccountMode` enum도 `PasswordChange`/`AccountCreate`가 제거돼 **`Account`/`Admin` 2값**만 남았다(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:13-20`).
- `Account` 페이지는 여전히 단일 상태 + 진입 모드로 UI를 분기하며(`AccountViewModel.cs:41-49`), 모드는 셸이 VM 생성 직후 주입한다(`AppShellViewModel.cs:267-272`, `:423-429`).
- `Account(Account)` 화면 구성: **① 내 계정 정보(읽기 전용)** = 아이디·이메일·로그인 방식·역할·가입일(`AccountViewModel.cs:53-66`, `Views/AccountView.xaml:13-49`) + **② PIN 변경**(`AccountViewModel.cs:158-212`). "로그인 방식"은 `AuthMethod.ToLabel()`로 **"Google SSO"**, 서버가 모르는 값을 보내면 **"알 수 없음"**(조용한 오인 방지, `src/MCPhoto.Core/Models/User.cs:36-48`).
- `Account(Admin)` 화면: 사용자 관리 진입(`IsPower` 가드, `AccountViewModel.cs:219-224`), 전역 TempUser 한도(admin 전용, `:87`, `:230-264`), 앱 종료(`:227-228`).
- 우상단 ⚙ 설정 버튼은 상단 바가 보이는 상태면 게스트 포함 누구나 누를 수 있고(`MainWindow.xaml:45-51`), 로그인 사용자만 PIN 게이트를 통과해야 한다([3.4](#34-진입-pin-게이트설정계정-관리)).

### 3.3 로그인 실행 — Google SSO 단일 경로

로그인 화면은 **"Google로 로그인" 버튼 하나**다. `AppSettings.GoogleClientId`가 비면(SSO opt-out, 브라우저 봉쇄 키오스크 배려) 버튼을 통째로 숨기고 "로그인이 구성되지 않았습니다" 안내만 남긴다
(`src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs:29-30`, `Views/LoginGuestView.xaml:20-36`, `src/MCPhoto.Core/Settings/AppSettings.cs:152`).

**흐름**(`LoginGuestViewModel.LoginWithGoogle`, `:46-88` — `IsBusy` 재진입 가드):

| # | 단계 | 수행 주체 | 근거 |
| --- | --- | --- | --- |
| 1 | PKCE `code_verifier`/`code_challenge`(S256)·`state`·`nonce` 생성 | 클라(순수 로직) | `src/MCPhoto.Core/Accounts/GoogleOAuthPkce.cs:23-35` |
| 2 | 빈 loopback 포트 확보 → `http://127.0.0.1:{port}/`(localhost→::1 회피) | 클라 | `Services/GoogleSignInService.cs:140-152`, `GoogleOAuthPkce.cs:41-46` |
| 3 | `HttpListener` 시작 → **시스템 기본 브라우저**로 authorize URL 오픈(`response_type=code`, `scope="openid email profile"`, `code_challenge_method=S256`) | 클라 | `GoogleSignInService.cs:58-75`, `:155-167`; `GoogleOAuthPkce.cs:14-17`, `:57-73` |
| 4 | 콜백 수신 → `state` 대조 → `{code, codeVerifier, redirectUri, nonce}` 반환 | 클라 | `GoogleSignInService.cs:89-120` |
| 5 | `POST /auth/google`(**API 키 헤더만, Bearer 없음** — 로그인 전 상태) | 클라 → 서버 | `src/MCPhoto.Http/HttpAccountService.cs:35-50`; `web/functions/src/routes/auth.ts:33-36` |
| 6 | 미구성 확인(501) → 입력 형식 검증(400) → `getToken`으로 code 교환 → `verifyIdToken` + payload 재확인(aud·iss·exp·`email_verified`·nonce·hd) → 검증된 email(소문자) | 서버 | `auth.ts:39-41`, `:44-57`, `:60-84`; `services/googleAuth.ts:1-13`, `:18` |
| 7 | 계정 매핑: email로 조회 → **있으면 그대로 로그인(DB write 없음)**, 없으면 **`temp_user`로 자동 생성** | 서버 | `services/accounts.ts:213-234`, `:240-249`, `:256-278` |
| 8 | JWT 발급(HS256, `sub`=계정 id, `role`) → `{token, expiresIn, user}` | 서버 | `auth.ts:96-106`; `domain/jwt.ts:28-41` |
| 9 | `IBackendSession.SignIn(token, user)` — JWT를 메모리에 보관 | 클라 | `HttpAccountService.cs:53-55` |
| 10 | `_shell.Session.Login(user)` → `CurrentUserChanged` → 상단 바 갱신 → `ReturnFromOverlay()`로 직전 화면 복귀 | 클라 | `LoginGuestViewModel.cs:72-73`; `AppShellViewModel.cs:240-246` |

**계정 자동 생성·매핑 규칙**(서버 권위, `web/functions/src/services/accounts.ts`):

- 계정 문서 id는 **email local-part에서 파생**한다(소문자화 + `[A-Za-z0-9._-]` 외 제거, 3~40자 보정, 충돌 시 `-2`/`-3`…, 전부 제거되면 `g-{uuid8}`) — `web/functions/src/domain/accountId.ts`.
- 신규 계정은 **무조건 `temp_user`**(`accounts.ts:264`). 승격은 관리자·매니저가 사용자 관리 화면에서 수동 지정한다([1.4](#14-역할-지정변경-매트릭스)).
- **기존 계정의 재로그인은 `role`·`authMethod`를 바꾸지 않는다**(읽기 전용 경로 — 승격된 계정이 재로그인으로 강등되지 않는다, `accounts.ts:236-249`).
- 동시 첫 로그인 경합은 `create`(문서 부재 시에만 성공) 실패 → 재조회 → 로그인으로 흡수한다(`:222-233`, `:270-276`).

**오류·취소 처리**(모두 인라인 문구, 화면 유지):

| 상황 | 코드 경로 | 화면 문구 |
| --- | --- | --- |
| 사용자 취소·타임아웃(3분)·`state` 불일치·인가 거부·code 없음 | `AcquireAuthorizationCodeAsync` → `null` | "Google 로그인이 취소되었습니다." (`LoginGuestViewModel.cs:55-60`) |
| Google 검증 실패(허용 도메인 밖·email 미검증 등) → 서버 **401**(열거 방지 일반화) | `LoginWithGoogleAsync` → `null` | "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요." (`:65-70`; `HttpAccountService.cs:57-62`) |
| 서버 SSO 미구성 → **501** | `GoogleSsoNotConfiguredException` | "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요." (`:75-80`; `HttpAccountService.cs:63-68`) |
| 네트워크·기타 오류 | 일반 `Exception` | "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요." (`:81-86`) |
| `GoogleClientId` 미설정 | 버튼 자체 미노출 | "로그인이 구성되지 않았습니다. 관리자에게 문의하세요."(정적 안내, `LoginGuestView.xaml:32-36`) |

- 리스너·CTS는 `try-finally`로 항상 정리한다(포트·핸들 누수 0, `GoogleSignInService.cs:128-133`). 토큰·code·verifier·state·nonce는 **로그에 남기지 않는다**(`:18` 주석, 서버는 `auth.ts:76-78`).
- [닫기] 버튼은 항상 노출돼 로그인 실패·미구성 상태에서도 게스트 촬영으로 복귀할 수 있다(`LoginGuestViewModel.cs:90-91`).
- **폐지 이력**: "게스트로 계속" 버튼은 it2에서 폐지(촬영 게스트 직행, `LoginGuestViewModel.cs:9-10`). id/pw 입력·회원가입·비밀번호 찾기 UI는 it15에서 폐지(`:11`).

### 3.4 진입 PIN 게이트(설정·계정 관리)

설정·계정 관리 진입 게이트는 **4자리 PIN 단일 경로**다(비밀번호 게이트는 it15에서 소멸). 판정은 `AppShellViewModel.EnsurePinGateAsync(User)` **한 곳**에 모여 있다(`src/MCPhoto.App/AppShellViewModel.cs:396-411`) — 두 진입로가 같은 메서드·같은 다이얼로그·같은 PIN(서버 `pinHash` 1개)을 쓴다.

| 진입로 | 호출 지점 | PIN 보유(`HasPin=true`) | PIN 미설정(`HasPin=false`) | 게스트 |
| --- | --- | --- | --- | --- |
| 설정(⚙) | `OpenSettings`(`:376-384`) | **매번 PIN 확인** | 최초 설정 강제 | **무가드**(현행 유지) |
| 계정 관리 | `AccountViewModel.OnEnterAsync`(`:109-116`) | 재확인 없이 진입 | 최초 설정 강제, 취소 시 직전 화면 복귀 | 도달 불가(로그인 전용) |

- **fail-closed**: `IAccountService`·`IPinPromptDialogService`가 미등록이면 `false`를 반환해 진입을 거부한다(`AppShellViewModel.cs:400`).
- 최초 설정 경로는 현재 PIN 확인 없이 `SetOwnPinAsync(uid, null, p)`를 호출하고, 성공 시 세션 로컬 `user.HasPin = true`로 전환한다(데드락 방지, `:405-409`).
- 다이얼로그 `PinPromptWindow`는 확인/최초설정 2모드이며, it15 §5.6 완화 2건을 보유한다: **연속 5회 불일치 시 창 자동 닫힘**(게이트 미통과)과 **불일치마다 1.5초 입력 비활성**(`src/MCPhoto.App/Views/PinPromptWindow.xaml.cs:23`, `:26`, `:117-127`).
- 네트워크·서버 오류(PIN 미설정 409 포함)는 **실패 횟수에 세지 않고** 게이트도 열지 않는다("확인할 수 없습니다. 네트워크를 확인하세요.", `PinPromptWindow.xaml.cs:129-133`) — 정상 사용자가 장애로 잠기지 않게 하면서 fail-closed를 유지한다.
- 서버는 **계정 잠금을 두지 않는다**(타인 계정 락아웃=DoS 도입 위험, `web/functions/src/services/accounts.ts:86` 주석). 브루트포스 완화는 위 클라 2건이 전부다 → [§5](#5-향후-개선-여지현재-비범위).

### 3.5 로그아웃 / 세션 유지 규칙(중요)

| 트리거 | `SessionContext.CurrentUser` | 촬영 세션 데이터 | 근거 |
| --- | --- | --- | --- |
| 팝오버 "로그아웃" | 해제(`Logout`) | 폐기(`ReturnHome`) | `src/MCPhoto.App/AppShellViewModel.cs:446-453` |
| 사용자 취소(홈 버튼 등) | **유지** | 폐기 | `GoHome` → `ReturnHome("사용자 취소")`, `clearUser` 기본 false(`:370-371`, `:296-304`) |
| 촬영 완료 후 | **유지** | (다음 세션 전까지 보존) | 촬영 후 로그인 유지(it5 B8) — 로그아웃 트리거 없음 |
| 유휴 타임아웃 만료 | **유지(로그아웃 없음)** | 폐기 | `ReturnHome("유휴 타임아웃", clearUser: false)`(`:354`) |
| 유휴 경고 "메인 화면으로" | **유지** | 폐기 | `GoHomeFromIdle` → `clearUser: false`(`:467-472`) |
| 전역 예외 복구 | **유지** | 폐기 | `ReturnHome("전역 예외 복구")` `clearUser`=false(`src/MCPhoto.App/App.xaml.cs:121-132`) |

핵심(it8 A1, 사실): **유휴 타임아웃은 로그아웃하지 않는다.** 유휴는 2분 무동작 → 경고 팝업 + 10초 카운트다운 → 만료 시 홈 복귀이며(`AppShellViewModel.cs:29-33`, `:330-356`), 어느 경로에서도 `clearUser`는 `false`다(`:354`, `:471`). 주석 "로그아웃 절대 금지(it8 A1)"(`:354`). `Reset`은 `clearUser=true`일 때만 `Logout`을 호출하므로(`SessionContext.cs:78-79`), 유휴/취소 경로에서는 로그인이 보존된다.

> 참고: `clearUser=true`로 실제 로그아웃까지 하는 경로는 코드상 존재하지 않는다(모든 `ReturnHome`/`Reset` 호출이 기본 false 또는 명시 false). 명시적 "로그아웃" 버튼만 `SessionContext.Logout()`을 직접 호출한다(`AppShellViewModel.cs:451`).

> ✅ **로그아웃은 JWT를 함께 폐기한다(2026-07-29 수정)**: `BackendSessionSynchronizer`가 `SessionContext.CurrentUserChanged`를 구독해 `CurrentUser == null`이 되는 **모든** 경로에서 `IBackendSession.Clear()`를 호출한다(`src/MCPhoto.App/Services/BackendSessionSynchronizer.cs:44-48`). 배선을 `Logout()` 한 곳이 아니라 통지 지점에 둔 이유는 게스트 전환 경로가 앞으로 늘어도 한 곳이 전부를 덮기 때문이다.
>
> 이 불변식이 없으면: 업로드는 **선택적 Bearer**라서(`HttpBackendClient.cs:74-85`, `HttpFirebaseClient.cs:96`, `:143`) 남은 토큰이 조용히 부착되고, 로그아웃 직후 게스트 촬영이 **직전 계정 소유로 기록**된다(TempUser면 `qrUsedCount`까지 차감). DI가 홀더를 동기화기 소유로 등록해 "토큰이 존재할 수 있는 모든 시점에 구독이 살아 있음"을 보장한다(`ServiceRegistration.cs`, [30 §3.1](./30-backend-firebase-integration.md)).

---

## 4. 계정 저장소

> **it15 구조 변경(사실)**: 레거시 Firebase 직결 경로(`MCPhoto.Firebase` 프로젝트)가 **삭제**됐다. 앱은 Firestore SDK를 전혀 쓰지 않고 **백엔드 HTTPS API(Cloud Functions)만** 호출한다. `AppSettings.UseBackend` 플래그와 `serviceAccountKey.json`도 함께 사라졌다(`src/MCPhoto.App/ServiceRegistration.cs:95-96` 주석; `src/MCPhoto.Http/HttpAccountService.cs:22`).

### 4.1 저장 위치·DTO

- 컬렉션: Firestore `users`, 문서 id = 계정 id(email local-part 파생, [3.3](#33-로그인-실행--google-sso-단일-경로)). 서버 상수 `COLLECTION`(`web/functions/src/services/accounts.ts:28`).
- 저장 문서 `UserDoc` = `{ id, role, createdAt, email, authMethod, pinHash?, qrUsedCount? }`(`web/functions/src/services/dto.ts:10-28`). **`password`·`emailVerified` 필드는 it15에서 삭제**됐다(`dto.ts:1-6`). 필드별 상세는 [40 §2.1](./40-database-firestore-and-storage-schema.md#21-users-문서-id--계정-id) — 여기서 중복하지 않는다.
- 와이어 응답 `UserResponse` = `{ id, role, createdAt(ISO8601), email, authMethod, hasPin }`(`dto.ts:63-73`). `hasPin`은 `pinHash != null` 파생값이며 **해시 원문은 어떤 응답에도 실리지 않는다**(`services/accounts.ts:31-45`).
- 클라 도메인 `User` = `{ Id, Role, CreatedAt, Email, AuthMethod, HasPin }`(`src/MCPhoto.Core/Models/User.cs:17-33`). 매핑은 `HttpAccountService.ToUser`(`:182-194`) 한 곳이며, `AuthMethod`는 `"google"`만 `Google`로 파싱하고 그 외는 **`Unknown`("알 수 없음")** 이다(`User.cs:39-40`) — 서버가 kakao·apple을 붙이기 전까지 조용한 오인을 막는다.

### 4.2 시드 계정(기본 관리자) — **it15에서 폐지됨**

| 시점 | 동작 |
| --- | --- |
| ~it14 | 시드 계정 `devmcjo`/비밀번호 `1111`(역할 admin). 온라인이면 시작 시 `EnsureSeedAccountAsync`가 문서를 upsert하고, 미초기화/오프라인이면 `LoginAsync`가 이를 **인메모리 admin**으로 허용했다 |
| **it15~(현재)** | **전부 삭제**. 비밀번호 자체가 없어져 시드 개념이 소멸했다. `EnsureSeedAccountAsync`·오프라인 시드 폴백·부트스트랩 호출부 모두 제거됐다 |

- 삭제 흔적은 앱 부트스트랩 주석에 남아 있다: "it15: 시드 계정 보장 삭제 — ID/PW 계정이 폐지되어 시드 개념 자체가 소멸"(`src/MCPhoto.App/App.xaml.cs:73-74`).
- **최초 admin 부트스트랩은 마이그레이션 스크립트가 담당한다**: `web/functions/scripts/migrate-google-only-accounts.mjs`(`--admin-email`/`--admin-id`, 기본값 `devmcjo@gmail.com` / `devmcjo` — `web/functions/src/domain/migration.ts:39-40`). HTTP API로는 admin을 지정할 수 없다(`canSetRole` 규칙 1, [1.4](#14-역할-지정변경-매트릭스)).
- 신규 계정은 Google SSO 최초 로그인 시 서버가 `temp_user`로 자동 생성한다 — "미리 만들어 두는 계정"은 존재하지 않는다.

### 4.3 계정 CRUD(`IAccountService`) — 7메서드

`src/MCPhoto.Core/Accounts/IAccountService.cs` + 유일 구현 `src/MCPhoto.Http/HttpAccountService.cs`. 모든 호출은 백엔드 경유이며, **역할 게이트는 서버가 JWT의 `role`로 재검증**한다(클라가 보낸 actingRole은 존재하지 않는다, `HttpAccountService.cs:21`).

| 메서드 | HTTP | 서버 게이트 | 클라 실패 매핑 | 근거 |
| --- | --- | --- | --- | --- |
| `LoginWithGoogleAsync(code, verifier, redirectUri, nonce?)` | `POST /auth/google` (API 키) | 없음(로그인 전) | 401→`null`, 501→`GoogleSsoNotConfiguredException`, 그 외→도메인 예외 | `HttpAccountService.cs:35-73`; `routes/auth.ts:33-108` |
| `GetAllAsync()` | `GET /accounts` (Bearer) | `requireBearer` + `requirePower` | 403→`UnauthorizedAccessException` (**빈 목록 폴백 없음**) | `:75-88`; `routes/accounts.ts:33-39` |
| `DeleteAsync(id)` | `DELETE /accounts/{id}` | `requirePower` + `canManage` + 자기 자신 403 | 403→`UnauthorizedAccessException`, 404→`InvalidOperationException` | `:90-103`; `routes/accounts.ts:94-106` |
| `SetRoleAsync(id, role)` | `PATCH /accounts/{id}/role` | `requirePower` + `canSetRole` 매트릭스 | 상동 | `:105-118`; `routes/accounts.ts:110-122` |
| `VerifyPinAsync(id, pin)` | `POST /accounts/me/pin/verify` | `requireBearer`(본인 `principal.id` 고정) | 401→`false`, 409(PIN 미설정)·네트워크→**예외 전파**(fail-open 금지) | `:122-143`; `routes/accounts.ts:54-69` |
| `SetOwnPinAsync(id, currentPin?, newPin)` | `PUT /accounts/me/pin` | `requireBearer`(본인). 기존 PIN 있으면 `currentPin` 확인 | 401(현재 PIN 불일치)·404→`InvalidOperationException`, 400→`ArgumentException` | `:145-162`; `routes/accounts.ts:73-91` |
| `ResetPinAsync(targetId, newPin)` | `PUT /accounts/{id}/pin` | `requirePower` + **`canResetPin`(엄격히 낮은 위계 — 동급 403)**, 자기 자신은 400 | 403→`UnauthorizedAccessException` | `:164-179`; `routes/accounts.ts:131-151` |

- **없는 메서드**(전부 it15 폐지): `LoginAsync`·`VerifyPasswordAsync`·`RegisterAsync`·`CreateAsync`·`ChangePasswordAsync`·`EnsureSeedAccountAsync`와 이메일 인증/재설정 계열. 서버 라우트도 함께 사라졌다(`routes/accounts.ts:5`, `routes/auth.ts:4-6`).
- `me/pin*` 라우트는 파라미터 라우트(`/:id/pin`)보다 **먼저** 등록해 `"me"`가 `:id`로 잡히지 않게 한다(`routes/accounts.ts:50`).
- HTTP 상태 → 예외 변환은 `HttpBackendClient.MapToDomainException` 한 곳(403→`UnauthorizedAccessException`, 409·404·그 외→`InvalidOperationException`, 400→`ArgumentException`, `:189-196`). **401은 호출부가 결정**한다(로그인만 `null`, PIN 확인은 `false`).
- Bearer 필수 호출인데 토큰이 없으면 요청 조립 단계에서 `UnauthorizedAccessException("로그인이 필요합니다(토큰 없음).")`로 즉시 거부한다(`HttpBackendClient.cs:109-113`).

### 4.4 계정 삭제 시 cascade(소유 프레임 동반 삭제)

cascade는 **서버가 수행**한다(it15 이후 클라는 `DELETE /accounts/{id}` 한 번만 호출, `HttpAccountService.cs:94-97`).

`deleteAccount`(`web/functions/src/services/accounts.ts:149-160`):
1. 대상 역할 조회(없으면 404) → `canManage(actor.role, targetRole)` 위반이면 **403**.
2. `deleteAllFramesByUser(targetId)` — 소유 프레임 정리.
3. `users/{id}` 문서 삭제.

`deleteAllFramesByUser`(`web/functions/src/services/frames.ts:204-215`):
- Firestore `frameTemplates`에서 `userId == id` 문서를 **배치 삭제**(`:206-209`).
- Storage `frames/{userId}/` 프리픽스 전체 삭제. **이 단계 실패만 무시**하고 진행한다(`:210-214`).

> ⚠️ **it15에서 실패 의미가 바뀌었다**(사실): 종전 클라 구현은 프레임 삭제가 실패해도 계정 삭제를 강행하고 경고만 남겼다. 현재는 **Firestore 배치 삭제가 실패하면 `deleteAccount` 전체가 예외로 중단**되어 계정 문서가 남는다 — "계정만 지워지고 프레임이 고아로 남는" 상태가 발생하지 않는 방향이다. Storage 삭제 실패만 종전과 같이 무시된다.

UI 안내: `UserMgmtViewModel.DeleteUser`는 자기 계정 삭제를 막고(`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:113`), `CanManage` 1차 가드를 건 뒤(`:115`), 성공 시 "`{id}` 삭제됨(소유 프레임 포함)."을 표시한다(`:120`).

### 4.5 백엔드 미도달 시 동작 (구 "미초기화 폴백" 재정의)

`Firebase 초기화됨/미초기화` 축은 **소멸했다** — `serviceAccountKey.json`·`FirebaseClient.Firestore is null` 판정·`AppSettings.UseBackend` 분기가 모두 사라졌기 때문이다. 현재의 축은 **백엔드 도달 가능/불가**다.

| 상황 | 판정 지점 | 동작 |
| --- | --- | --- |
| `BackendBaseUrl`이 빈 값 | `ServiceRegistration.cs:103-108`(비어 있으면 `BaseAddress` 미설정) | 상대 URL 요청을 조립할 수 없어 `InvalidOperationException`으로 즉시 실패 |
| 네트워크 오류·타임아웃(100초) | `HttpBackendClient.cs:136-141` | `InvalidOperationException("백엔드에 연결할 수 없습니다.")` |
| 미로그인 상태에서 Bearer 필수 호출 | `HttpBackendClient.cs:109-113` | `UnauthorizedAccessException("로그인이 필요합니다(토큰 없음).")` |
| 로그인 시도 중 서버 미도달 | `LoginGuestViewModel.cs:81-86` | "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요."(화면 유지) |
| 사용자 목록 조회 실패 | `UserMgmtViewModel.cs:100-104` | "사용자 목록을 불러올 수 없습니다."(**빈 목록 폴백 없음** — 종전 `GetAllAsync`의 빈 배열 폴백은 폐지) |
| PIN 게이트 확인 불가 | `PinPromptWindow.xaml.cs:129-133` + `EnsurePinGateAsync` | **fail-closed** — 진입 거부, 실패 횟수 미가산 |
| TempUser QR 한도 조회 실패 | `AppShellViewModel.cs:181-184` | **fail-open** — 앱은 허용하고 서버가 업로드 단계에서 최종 거부(한도 진실원은 서버) |
| 업로드(게스트 포함) | `HttpFirebaseClient.cs:96`, `:143` | 선택적 Bearer — 토큰 있으면 부착, 없으면 익명 통과. 실패는 `QrPopup`이 우아 처리(로컬 보존) |

- 로그인 자체가 불가능하므로 **오프라인에서는 어떤 계정으로도 로그인할 수 없다**(인메모리 폴백 없음). 게스트 촬영은 계속 가능하다.
- 진단은 [70 §1(로그 위치)](./70-logging-and-troubleshooting.md#1-로그-파일-실제-위치-제일-먼저-볼-것)·[§5(로그 키워드)](./70-logging-and-troubleshooting.md#5-로그-키워드-빠른-색인)를 쓴다. **70 §6은 삭제된 `MCPhoto.Firebase` 기준의 구서술**이라 근거로 삼지 않는다(문서 상단 경고 참조).

---

## 5. 향후 개선 여지(현재 비범위)

아래는 코드상 미구현이며, 현재 동작을 근거로 정리한 개선 후보다(일부 "가정" 표시). **해소된 항목은 목록에서 걷어내고 이력만 취소선으로 남긴다.** 착수 대기열은 [90 §1](./90-roadmap-and-future-work.md#1-알려진-이슈--기술-부채)이 단일 진실이므로 여기서 중복 나열하지 않는다.

| 항목 | 현재 상태(사실) | 개선 여지 |
| --- | --- | --- |
| PIN 서버 측 시도 제한 | 서버 잠금 **없음**(`services/accounts.ts:86` 주석 — 계정 단위 잠금은 타인 락아웃=DoS 도입). 완화는 클라 2건(5회 실패 시 창 닫힘 + 1.5초 쿨다운)뿐이며 앱을 다시 열면 카운터가 초기화된다 | 4자리 = 10,000 조합. 물리 접근자의 반복 시도는 여전히 가능 → IP/계정 단위 rate limit(Cloud Armor 등) 검토([it15 설계 §5.6](../design/wpf-it15-google-only-auth-design.md) R1) |
| admin PIN 분실 시 앱 내 복구 경로 | **없음**. 자기 자신 대상 PIN 재설정은 서버가 400으로 거부하고, 타 계정 재설정(E3)은 `canResetPin`상 admin을 대상으로 삼을 수 없다(상위도 **동급도** 불가 — 두 번째 admin이 있어도 서로 복구해 줄 수 없다) | 현재 유일한 복구는 CLI: `node web/functions/scripts/migrate-google-only-accounts.mjs --clear-pin <id> --apply`(firebase-admin 자격으로 해당 계정의 `pinHash` 필드를 지운다). 앱 내 복구(예: 두 번째 admin 승인)는 미구현 |
| ~~로그아웃 시 JWT 미소거~~ | **해소(2026-07-29)**: `BackendSessionSynchronizer`가 `CurrentUserChanged` 구독으로 게스트 전환 시 토큰을 폐기한다([3.5](#35-로그아웃--세션-유지-규칙중요)) | — |
| 삭제·PIN 재설정 위계 비대칭 | **삭제는 동급 허용**(`canManage` — manager가 다른 manager 계정을 삭제할 수 있다), **PIN 재설정만 동급 차단**(`canResetPin`). PIN 축만 좁힌 것은 `canManage`가 `deleteAccount`와 공유되기 때문이다([1.3.1](#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계)) | (가정) 동급 삭제도 과대 권한일 수 있다 → 삭제에 `canDeleteAccount`(엄격히 낮은 위계) 축을 도입하는 안. 착수 전 확인 필요: 마지막 admin 자기 삭제·매니저 정리 동선에 영향 |
| 세션 만료 | 유휴는 홈 복귀만, 로그인은 앱 수명 동안 유지(`AppShellViewModel.cs:354`). 서버 JWT는 기본 8시간 만료(`web/functions/src/config.ts:78`)이나 클라에 **갱신·만료 감지 경로가 없다** | (가정) 만료 후 첫 계정 조작이 401로 실패할 때 "다시 로그인" 유도 UX 부재. 파워 계정 자동 로그아웃 정책도 없음 |
| 설정 페이지 역할 게이트 | 역할 검사 없음 — 로그인 사용자는 PIN, 게스트는 무가드(`AppShellViewModel.cs:376-384`) | (가정) 운영자 전용 게이트 검토 여지. 키오스크 운영 동선상 현행 유지가 기본 |
| 인증 수단 확장 | `authMethod`는 `"google"` 고정. 클라 `ParseAuthMethod`는 그 외 값을 `Unknown`으로 떨어뜨린다(`User.cs:39-40`) | Kakao·Apple 추가 시 enum 값 + 매핑 1줄 + 서버 provider 검증이 필요. iOS·Android·웹 확장은 OAuth 클라이언트 유형이 별도이며 **서버 확장 3건(리디렉트 허용 목록·audience 목록·client_secret 조건부)이 선행**돼야 한다([61 §4](./61-auth-platform-integration.md), 블로커 목록 [90 §7.2](./90-roadmap-and-future-work.md)) |
| ~~비밀번호 해싱~~ | **소멸(it15)**: 비밀번호 인증이 폐지돼 저장할 비밀번호가 없다. 남은 자격증명 `pinHash`는 bcrypt(`web/functions/src/domain/password.ts`) | — |
| ~~SSO / 외부 인증 미지원~~ | **해결(it15)**: Google SSO가 **유일한** 로그인 수단이다([3.3](#33-로그인-실행--google-sso-단일-경로)) | 추가 IdP는 위 "인증 수단 확장" 항목 |
| ~~로그인 시도 제한(id/pw 브루트포스)~~ | **소멸(it15)**: id/pw 로그인 경로가 없다. Google이 인증 시도를 책임진다 | 게이트 브루트포스는 위 "PIN 서버 측 시도 제한" 항목으로 대체 |
| ~~역할 강등/admin 위임~~ | **해결(it13 도입 · it16 완화)**: 사용자 관리 목록의 역할 콤보로 승격·강등을 모두 지정한다([1.4](#14-역할-지정변경-매트릭스)). admin 위임(→admin 지정)은 여전히 불가(**최종 1인** 규칙, 서버·클라 공통 거부) | admin 이관이 필요해지면 마이그레이션 스크립트 경로만 존재(HTTP API 불가) |
