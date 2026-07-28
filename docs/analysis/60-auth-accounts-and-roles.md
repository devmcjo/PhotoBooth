# 60 · 인증 · 계정 · 역할

| 항목 | 내용 |
| --- | --- |
| 문서 | 60-auth-accounts-and-roles.md |
| 범위 | MCPhoto의 계정 역할 위계(temp_user/user/advanced_user/manager/admin + 게스트), 권한 매트릭스, 로그인/로그아웃 흐름, 계정 저장소(Firestore users + 오프라인 시드 폴백)와 CRUD·cascade 삭제 |
| 최종 업데이트 | 2026-07-29 (it16 — §1·§2) |
| 관련 소스 경로 | `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Core/Models/UserRole.cs`, `src/MCPhoto.Core/Models/RoleChangePolicy.cs`, `src/MCPhoto.Core/Models/User.cs`, `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`, `src/MCPhoto.Firebase/AccountService.cs`, `src/MCPhoto.Firebase/Dto/UserDoc.cs`, `src/MCPhoto.App/SessionContext.cs`, `src/MCPhoto.App/AppShellViewModel.cs`, `src/MCPhoto.App/MainWindow.xaml`, `src/MCPhoto.App/ViewModels/{LoginGuestViewModel,AccountViewModel,UserMgmtViewModel,FrameSelectViewModel}.cs`, `web/functions/src/domain/roles.ts` |
| 갱신 규칙 | `UserRole` enum·`IsPower`/`CanWriteFrames`/`CanManage`/`CreatableRoles`/`CanCreate` 규칙, `RoleChangePolicy.AssignableRoles`(서버 `canSetRole`과 1:1), `IAccountService` 시그니처, 시드 계정 상수, 상단 바 팝오버 항목·가시성 바인딩(`MainWindow.xaml`), 세션 단일 소스(`SessionContext`)의 Login/Logout/Reset 계약이 바뀌면 이 문서를 갱신한다. |

관련 문서: [10 Exe 앱 아키텍처](./10-exe-app-architecture.md) · [30 Firebase 연동](./30-backend-firebase-integration.md) · [40 Firestore/Storage 스키마](./40-database-firestore-and-storage-schema.md) · [70 로깅/이슈 진단](./70-logging-and-troubleshooting.md) · 인덱스 [README](./README.md)

> ⚠️ 자격증명(it15 갱신, 사실): **비밀번호 개념은 폐지됐다.** 자격증명은 ① Google SSO(신원 — 서버가 id_token 검증) + ② `pinHash`(설정·계정 관리 진입 게이트, bcrypt 4자리 PIN) 두 가지뿐이며 `users` 문서의 `password`·`emailVerified` 필드는 삭제됐다(설계 `docs/design/wpf-it15-google-only-auth-design.md` §5.3). it15 이전의 "평문 비밀번호 저장·비교"는 **이력**이다. "웹 접근 전면 차단"이 여전히 `users` 컬렉션의 방어선이다([40 §5.1](./40-database-firestore-and-storage-schema.md#51-firestore-webfirestorerules)).

> ⚠️ 문서 동기화 상태(사실): **§1·§2는 it16 기준으로 최신**이다. **§3~§5는 it13~it15 변경(Google SSO 로그인·PIN·백엔드 프록시 경유 계정 CRUD)이 아직 반영되지 않은 구서술**이며, id/pw 로그인·`ChangePasswordAsync`·시드 비밀번호 서술은 현재 코드와 다르다. 해당 절을 근거로 삼기 전에 [40 §2.1](./40-database-firestore-and-storage-schema.md#21-users-문서-id--계정-id)과 `docs/design/wpf-it15-google-only-auth-design.md`를 확인한다([90 §1 "문서 동기화 지연"](./90-roadmap-and-future-work.md#1-알려진-이슈--기술-부채) 등재).

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
- 사용자 관리 액션 게이트: PIN 재설정(`UserMgmtViewModel.cs:52`, `:141`) — it16에서 `IsPower()` 항이 추가됐다([1.4](#14-역할-지정변경-매트릭스)).

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

`CanManage(actingRole, targetRole)`는 **자신과 같거나 낮은 위계**만 관리(삭제·PIN 재설정)할 수 있다는 규칙이며, 서수가 아닌 명시 랭크 `ManageRank`로 판정한다(`src/MCPhoto.Core/Models/UserRole.cs:99-116`). 예) manager는 admin을 관리할 수 없고, admin은 다른 admin도 관리할 수 있다.

> ⚠️ **이 판정만으로는 비power도 통과한다.** 같은 위계를 허용하므로 `temp_user`가 다른 `temp_user`를 "관리 가능"으로 계산한다. 따라서 관리 액션 게이트는 **`IsPower()`와 함께** 써야 한다. it16에서 PIN 재설정 경로(클라 `UserMgmtViewModel`, 서버 `PUT /accounts/:id/pin`)에 이 `IsPower()` 항을 보강했다([1.4](#14-역할-지정변경-매트릭스)). `CanManage`/`canManage` 자체의 의미는 **변경하지 않았다** — 계정 삭제(`deleteAccount`)와 공유되므로 "엄격히 높은 위계"로 좁히면 admin↔admin·manager↔manager 삭제가 회귀한다.

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
| 타 계정 PIN 재설정 | × | × | × | **×** | ○ | ○ | **it16 강화**: `IsPower() && CanManage(target)`(`UserMgmtViewModel.cs:52`, `:141`) + 서버 `PUT /accounts/:id/pin`에 `requirePower()` 추가 → 비power **403**. 종전에는 비power가 같은 위계의 남의 PIN을 재설정할 수 있었다([1.4](#14-역할-지정변경-매트릭스)) |
| 본인 PIN 변경 | × | ○ | ○ | ○ | ○ | ○ | `AccountViewModel.ChangePin` → `PUT /accounts/me/pin`. 자기 자신을 `:id` 경로로 부르면 400 |
| 역할 지정·변경 | × | × | × | × | △ | △ | 매트릭스는 [1.4](#14-역할-지정변경-매트릭스). **M**은 하위 3역할 대역(T·U·**A**) 안에서 자유 지정(승격 포함), **D**는 admin 제외 전부. 콤보 필터(`RoleChangePolicy.AssignableRoles`) + 커맨드 게이트(`UserMgmtViewModel.cs:173`) + 서버 `canSetRole` 최종 강제 |
| TempUser QR 한도(시간·횟수) 변경 | × | × | × | × | × | ○ | `CanEditTempUserLimits => Role == UserRole.Admin`(`ViewModels/AccountViewModel.cs:87`) + 서버 `requireAdmin`·범위 검증(it13) |
| 앱 종료(관리자) | × | × | × | × | ○ | ○ | "관리자 도구" 페이지의 `ExitApp`(`ViewModels/AccountViewModel.cs:227-228`); 도구 페이지 진입이 파워 전용 |
| 설정(앱 설정) 페이지 접근 | ○ | △ | △ | △ | △ | △ | 우상단 ⚙ 버튼은 상단 바 표시 상태면 누구나(`MainWindow.xaml:44-51`). **게스트는 무가드**, 로그인 사용자는 **PIN 게이트** 통과 필수(`AppShellViewModel.OpenSettings`, `:376-384`) |
| 설정 항목 편집 | △ | △ | ○ | ○ | ○ | ○ | 게스트는 일부 항목(거울모드·재촬영·필터·QR 전송·Firebase 항목) 편집 불가(it12 R1). **T**는 QR 한도 초과 시 QR 관련 편집만 차단(`CanEditQr = IsLoggedIn && !IsTempUserBlocked`, `ViewModels/SettingsViewModel.cs:78`). **A는 이 축에서 U와 동일** |

주의(사실/가정 구분):
- **사용자 관리 액션의 역할 위계**(사실): 삭제·PIN 재설정은 **행위자와 같거나 낮은 위계**의 계정에만 노출·허용된다(`UserRole.CanManage`, `RoleActionVisibilityConverter`). 예) manager는 admin 계정을 삭제/PIN 재설정할 수 없다. PIN 재설정은 it16부터 **여기에 `IsPower()`가 더해진다**([1.3](#13-canmanage--관리-위계-판정)). UI 미노출과 VM 명령 가드로 이중 방어하고 서버가 최종 강제한다.
- **프레임 권한은 `IsPower`가 아니라 `CanWriteFrames` 축**(사실, it16): 고급 유저는 프레임을 만들 수 있지만 계정 관리·공용 DB 등록은 못 한다. 두 축을 혼용하면 조용한 권한 오판이 된다([1.2](#12-canwriteframes--프레임-저작-권한-축-it16-신규)).
- **프레임 권한을 잃은 계정의 기존 프레임은 읽기 전용으로 남는다**(사실, it16 E4): 목록 노출·촬영 사용은 유지하고 편집·삭제 UI만 사라진다. 파일 삭제·소유권 이전·마이그레이션은 **하지 않는다**.
- **설정 페이지는 역할 게이트가 없다**(사실): `OpenSettings`에 역할 검사는 없고 **로그인 사용자에 대한 PIN 게이트**만 있다(it14·it15). 게스트는 여전히 무가드이며, 계정·관리자 기능은 `Account`/`UserMgmt`로 분리돼 있으므로 앱 설정 자체는 키오스크 운영자가 접근하는 열린 화면이라는 것이 코드상 현재 상태다.
- **운영 동선(it16)**: 기존 `user` 계정은 이번 변경으로 프레임 생성·편집 권한을 잃는다. 프레임을 만들어야 하는 계정은 **사용자 관리 → 역할 콤보 → "고급 유저" → 변경**으로 승격한다. manager도 이 승격을 할 수 있다([1.4](#14-역할-지정변경-매트릭스)).

---

## 3. 로그인 / 로그아웃 흐름

### 3.1 세션 단일 소스 = `SessionContext`

계정 진실 소스는 `SessionContext.CurrentUser`(private set) 하나이며, 진입점은 `Login`/`Logout`/`Reset(clearUser)`뿐이다(`src/MCPhoto.App/SessionContext.cs:13-14`, `:47-59`, `:65-80`). 계정 수명은 촬영 세션보다 상위(앱 사용 동안 유지)이며(`:7-8`), 변경 시 `CurrentUserChanged` 이벤트로 통지한다(`:17`, `:50`, `:58`). 상단 바는 이 이벤트를 구독해 자동 갱신한다(`src/MCPhoto.App/AppShellViewModel.cs:98`, `:101-109`).

상단 바 계정 상태는 미러 없이 세션에서 직접 읽는다(`src/MCPhoto.App/AppShellViewModel.cs:59-66`):
- `IsLoggedIn` = `CurrentUser != null`, `IsGuest` = `CurrentUser == null`, `IsPower` = `CurrentUser?.Role.IsPower() == true`.
- 상단 바 좌측 라벨 `AccountLabel` = 비로그인 시 "로그인", 로그인 시 계정 ID(`:66`).

### 3.2 상단 바 계정 버튼 동작

`OpenAccount` 커맨드(`src/MCPhoto.App/AppShellViewModel.cs:290-297`):

| 상태 | 좌상단 계정 버튼 클릭 결과 | 근거 |
| --- | --- | --- |
| 비로그인 | 로그인 페이지(오버레이 진입, `AppState.Login`) | `src/MCPhoto.App/AppShellViewModel.cs:296` |
| 로그인 | 계정 팝오버 토글(`IsAccountPopupOpen`) | `:293-294` |

팝오버 항목(`src/MCPhoto.App/MainWindow.xaml:53-78`):

| 항목 | 노출 조건 | 커맨드 → 이동 | 근거 |
| --- | --- | --- | --- |
| 비밀번호 변경 | 로그인 전원 | `OpenPasswordChangeCommand` → `Account(PasswordChange)` | `MainWindow.xaml:61-63`; `AppShellViewModel.cs:308-309` |
| 계정 생성 | `IsPower` | `OpenAccountCreateCommand` → `Account(AccountCreate)` | `MainWindow.xaml:65-68`; `AppShellViewModel.cs:312-313` |
| 관리자 도구 | `IsPower` | `OpenAdminToolsCommand` → `Account(Admin)` | `MainWindow.xaml:69-72`; `AppShellViewModel.cs:316-317` |
| 로그아웃 | 로그인 전원 | `LogoutCommand` | `MainWindow.xaml:73-75`; `AppShellViewModel.cs:327-333` |

`Account` 페이지는 진입 모드(`AccountMode`: PasswordChange/AccountCreate/Admin)로 한 VM이 UI를 분기한다(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:13-23`, `:43-53`). 진입 모드는 셸이 VM 생성 직후 주입한다(`src/MCPhoto.App/AppShellViewModel.cs:191-196`, `:300-305`).

### 3.3 로그인 실행

`LoginGuestViewModel.Login`(`src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs:32-56`):
1. `IAccountService.LoginAsync(id.Trim(), pw)` 호출(`:40`).
2. `null`이면 "아이디 또는 비밀번호가 올바르지 않습니다." 표시(`:41-45`).
3. 성공 시 `_shell.Session.Login(user)` → `CurrentUserChanged` 통지 → 상단 바 자동 갱신(`:46`).
4. `ReturnFromOverlay()`로 진입 직전 화면 복귀(`:48`). 예외 시 "네트워크를 확인해 주세요." + `LogWarning`(`:50-54`).

"게스트로 계속" 버튼은 **폐지**되었다(촬영 게스트 직행, `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs:10-11`).

### 3.4 로그아웃 / 세션 유지 규칙(중요)

| 트리거 | CurrentUser | 촬영 세션 데이터 | 근거 |
| --- | --- | --- | --- |
| 팝오버 "로그아웃" | 해제(Logout) | 폐기(ReturnHome) | `src/MCPhoto.App/AppShellViewModel.cs:327-333` |
| 사용자 취소(홈 버튼 등) | **유지** | 폐기 | `GoHome` → `ReturnHome("사용자 취소")` clearUser 미지정=false(`:276-277`, `:202-210`) |
| 촬영 완료 후 | **유지** | (다음 세션 전까지 보존) | 촬영 후 로그인 유지(it5 B8) — 로그아웃 트리거 없음 |
| 유휴 타임아웃 만료 | **유지(로그아웃 없음)** | 폐기 | `ReturnHome("유휴 타임아웃", clearUser: false)`(`src/MCPhoto.App/AppShellViewModel.cs:260`) |
| 유휴 경고 "메인 화면으로" | **유지** | 폐기 | `GoHomeFromIdle` → `clearUser: false`(`:348-352`) |
| 전역 예외 복구 | **유지** | 폐기 | `ReturnHome("전역 예외 복구")` clearUser=false(`src/MCPhoto.App/App.xaml.cs:117`) |

핵심(it8 A1, 사실): **유휴 타임아웃은 로그아웃하지 않는다.** 유휴는 2분 무동작 → 경고 팝업 + 10초 카운트다운 → 만료 시 홈 복귀이며(`src/MCPhoto.App/AppShellViewModel.cs:26-30`, `:232-262`), 어느 경로에서도 `clearUser`는 `false`다(`:260`, `:351`). 주석 "로그아웃 절대 금지(it8 A1)"(`:260`). `Reset`은 `clearUser=true`일 때만 `Logout`을 호출하므로(`src/MCPhoto.App/SessionContext.cs:65-80`), 유휴/취소 경로에서는 로그인이 보존된다.

> 참고: `clearUser=true`로 실제 로그아웃까지 하는 경로는 코드상 존재하지 않는다(모든 `ReturnHome`/`Reset` 호출이 기본 false 또는 명시 false). 명시적 "로그아웃" 버튼만 `SessionContext.Logout()`을 직접 호출한다(`src/MCPhoto.App/AppShellViewModel.cs:331`).

---

## 4. 계정 저장소

### 4.1 저장 위치·DTO

- 컬렉션: Firestore `users`, 문서 id = 계정 id(`src/MCPhoto.Firebase/AccountService.cs:16`, `:43`).
- DTO: `UserDoc { id, password(평문), role(문자열), createdAt }`(`src/MCPhoto.Firebase/Dto/UserDoc.cs:6-20`).
- 도메인 매핑: `ToUser`/`ParseRole`(`src/MCPhoto.Firebase/AccountService.cs:118-124`).

### 4.2 시드 계정(기본 관리자)

| 항목 | 값 | 근거 |
| --- | --- | --- |
| id | `devmcjo` | `src/MCPhoto.Firebase/AccountService.cs:17` |
| 비밀번호 | `1111` | `:18` |
| 역할 | `admin` | `:39`, `:110` |

- 온라인(Firebase 초기화됨): 앱 시작 시 `EnsureSeedAccountAsync`가 문서 없으면 생성하고 `"시드 계정 생성: {Id}"` 로그를 남긴다(`src/MCPhoto.Firebase/AccountService.cs:99-116`). 앱 부트스트랩에서 호출(`src/MCPhoto.App/App.xaml.cs:73`, `:79-91`; 실패 시 `"시드 계정 보장 실패(오프라인 가능)"` 경고).
- 오프라인/미초기화: `EnsureSeedAccountAsync`는 no-op이고(`src/MCPhoto.Firebase/AccountService.cs:101`), 대신 `LoginAsync`가 `devmcjo/1111`을 **인메모리 admin으로** 허용한다(`:35-40`). 그 외 계정은 오프라인에서 로그인 불가.

### 4.3 계정 CRUD(`IAccountService`)

`src/MCPhoto.Core/Accounts/IAccountService.cs` + 구현 `src/MCPhoto.Firebase/AccountService.cs`:

| 메서드 | 동작 | 미초기화(Db null) 처리 | 근거 |
| --- | --- | --- | --- |
| `LoginAsync(id, pw)` | 평문 비교, 성공 시 User·실패 시 null | 시드만 인메모리 허용, 그 외 null | `AccountService.cs:33-48` |
| `CreateAsync(id, pw, role, actingRole)` | 역할 게이트→중복 확인→문서 생성 | 게이트 통과 후 `EnsureDb` 예외 | `:50-66` |
| `ChangePasswordAsync(id, newPw)` | `password` 필드 업데이트 | `EnsureDb` 예외 | `:68-73` |
| `GetAllAsync()` | 전체 계정 목록 | **빈 목록 반환**(예외 아님) | `:75-80` |
| `DeleteAsync(id)` | cascade 프레임 삭제 후 계정 문서 삭제 | `EnsureDb` 예외(cascade 전 프레임 삭제는 미초기화 시 no-op) | `:82-90` |
| `SetRoleAsync(id, role)` | `role` 필드 업데이트 | `EnsureDb` 예외 | `:92-97` |
| `EnsureSeedAccountAsync()` | 시드 없으면 생성 | no-op | `:99-116` |

쓰기류는 `EnsureDb()`가 미초기화 시 `InvalidOperationException("Firebase 미초기화 — 계정 쓰기 불가(서비스 계정 키 필요).")`를 던진다(`src/MCPhoto.Firebase/AccountService.cs:126-130`). UI는 이를 잡아 사용자 메시지로 노출한다(예: `AccountViewModel.CreateAccount`의 `InvalidOperationException` 캐치, `src/MCPhoto.App/ViewModels/AccountViewModel.cs:159-163`).

### 4.4 계정 삭제 시 cascade(소유 프레임 동반 삭제)

`AccountService.DeleteAsync`가 계정 문서 삭제 **전에** `_frames.DeleteAllByUserAsync(id)`를 호출한다(`src/MCPhoto.Firebase/AccountService.cs:85-89`). 실패해도 계정 삭제는 진행하고 `"cascade 프레임 삭제 실패: {Id}"` 경고만 남긴다(`:87`).

`FrameRepository.DeleteAllByUserAsync`(`src/MCPhoto.Firebase/FrameRepository.cs:106-118`):
- Firestore `frameTemplates`에서 `userId == id` 문서 전부 삭제(개별 실패는 `"프레임 문서 삭제 실패"` 경고).
- Storage `frames/{userId}/` 프리픽스 전체 삭제(§F8 cascade, 실패 시 `"프레임 Storage 삭제 실패"` 경고).

UI 안내: `UserMgmtViewModel.DeleteUser`는 성공 시 "`{id}` 삭제됨(소유 프레임 포함)."을 표시하고, 자기 계정 삭제를 막는다(`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:56-72`).

### 4.5 미초기화 폴백 요약

| 소스 | Firebase 초기화됨 | 미초기화(키 없음/오프라인) |
| --- | --- | --- |
| 시드 계정 | Firestore에 upsert | `LoginAsync`가 인메모리 admin 제공 |
| 일반 계정 로그인 | Firestore 조회 | 불가(null) |
| 계정 목록(GetAll) | 조회 | 빈 목록 |
| 계정 쓰기(생성/변경/삭제/역할) | 수행 | `InvalidOperationException` |

`Db is null` 판정은 `FirebaseClient.Firestore`가 미초기화 시 null이라는 사실에 기반한다(`src/MCPhoto.Firebase/FirebaseClient.cs:26-30`, `:47-52`). Firebase 초기화 진단은 [70 로깅/이슈 진단 §6](./70-logging-and-troubleshooting.md#6-firebase-초기화-실패-진단)을 참조.

---

## 5. 향후 개선 여지(현재 비범위)

아래는 코드상 미구현이며, 현재 동작을 근거로 정리한 개선 후보다(일부 "가정" 표시).

| 항목 | 현재 상태(사실) | 개선 여지 |
| --- | --- | --- |
| 비밀번호 해싱 | 평문 저장·비교(`User.cs:11-12`, `AccountService.cs:46`) | 배포 시 해싱 필요 — 소스 주석이 "후순위"로 명시(`User.cs:11`) |
| 세션 만료 | 유휴는 홈 복귀만, 로그인은 무기한 유지(`AppShellViewModel.cs:260`) | (가정) 파워 계정 자동 로그아웃/타임아웃 정책 부재 |
| SSO / 외부 인증 | id/pw 단일 방식만(`IAccountService.LoginAsync`) | (가정) SSO·OAuth 미지원 |
| 로그인 시도 제한 | 시도 횟수 제한·잠금 없음(`LoginGuestViewModel.Login`) | (가정) 브루트포스 방어 부재 — 단, 키오스크·평문 전제라 우선순위 낮음 |
| 설정 페이지 권한 게이트 | 역할 검사 없음(`AppShellViewModel.cs:283-287`) | (가정) 운영자 전용 게이트 검토 여지 |
| ~~역할 강등/admin 위임~~ | **해결(it13 도입 · it16 완화)**: 사용자 관리 목록의 역할 콤보로 승격·강등을 모두 지정한다([1.4](#14-역할-지정변경-매트릭스)). admin 위임(→admin 지정)은 여전히 불가(**최종 1인** 규칙, 서버·클라 공통 거부) | admin 이관이 필요해지면 마이그레이션 스크립트 경로만 존재(HTTP API 불가) |
