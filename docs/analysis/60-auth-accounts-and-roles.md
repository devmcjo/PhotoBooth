# 60 · 인증 · 계정 · 역할

| 항목 | 내용 |
| --- | --- |
| 문서 | 60-auth-accounts-and-roles.md |
| 범위 | MCPhoto의 계정 역할 위계(user/manager/admin + 게스트), 권한 매트릭스, 로그인/로그아웃 흐름, 계정 저장소(Firestore users + 오프라인 시드 폴백)와 CRUD·cascade 삭제 |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 경로 | `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Core/Models/UserRole.cs`, `src/MCPhoto.Core/Models/User.cs`, `src/MCPhoto.Firebase/AccountService.cs`, `src/MCPhoto.Firebase/Dto/UserDoc.cs`, `src/MCPhoto.App/SessionContext.cs`, `src/MCPhoto.App/AppShellViewModel.cs`, `src/MCPhoto.App/MainWindow.xaml`, `src/MCPhoto.App/ViewModels/{LoginGuestViewModel,AccountViewModel,UserMgmtViewModel,FrameSelectViewModel}.cs` |
| 갱신 규칙 | `UserRole` enum·`IsPower`/`CreatableRoles`/`CanCreate` 규칙, `IAccountService` 시그니처, 시드 계정 상수(`SeedId`/`SeedPassword`), 상단 바 팝오버 항목·가시성 바인딩(`MainWindow.xaml`), 세션 단일 소스(`SessionContext`)의 Login/Logout/Reset 계약이 바뀌면 이 문서를 갱신한다. |

관련 문서: [10 Exe 앱 아키텍처](./10-exe-app-architecture.md) · [30 Firebase 연동](./30-backend-firebase-integration.md) · [40 Firestore/Storage 스키마](./40-database-firestore-and-storage-schema.md) · [70 로깅/이슈 진단](./70-logging-and-troubleshooting.md) · 인덱스 [README](./README.md)

> ⚠️ 보안 주의(사실): 비밀번호는 **MVP 평문 저장·평문 비교**다. `User.Password`는 평문이며(`src/MCPhoto.Core/Models/User.cs:11-12`), 로그인 비교도 평문(`src/MCPhoto.Firebase/AccountService.cs:46`), Firestore 문서에도 평문 저장(`src/MCPhoto.Firebase/Dto/UserDoc.cs:12`). 개인/키오스크 사용 전제이며 "웹 접근 전면 차단"이 방어선이라고 소스 주석이 명시한다(`src/MCPhoto.Core/Models/User.cs:4-5`).

---

## 1. 역할 종류와 위계

역할은 `UserRole` enum 3종이며, 여기에 **비로그인 = 게스트**(enum 값 아님, `CurrentUser == null` 상태)가 더해진다.

`UserRole` 정의: `src/MCPhoto.Core/Models/UserRole.cs:4-14`

| 역할 | enum 값 | Firestore 저장값 | 소스 설명(주석) | 근거 |
| --- | --- | --- | --- | --- |
| 게스트 | (없음) | — | 비로그인 상태. `SessionContext.CurrentUser == null` | `src/MCPhoto.App/SessionContext.cs:13-14`, `src/MCPhoto.App/AppShellViewModel.cs:62` |
| user | `UserRole.User` | `"user"` | "자기 프레임(최대 10) + AppSettings 관리" | `src/MCPhoto.Core/Models/UserRole.cs:6-7`, `:20` |
| manager | `UserRole.Manager` | `"manager"` | "user + 사용자 관리 + 공용 기본 프레임 관리" | `src/MCPhoto.Core/Models/UserRole.cs:9-10`, `:21` |
| admin | `UserRole.Admin` | `"admin"` | "manager + manager 지정(최종 1인)" | `src/MCPhoto.Core/Models/UserRole.cs:12-13`, `:22` |

문자열 매핑은 `UserRoleExtensions.ToFirestoreValue`(`:19-25`)와 `ParseRole`(`:27-32`)이 담당하며, 알 수 없는 값은 `user`로 안전 폴백한다(`:24`, `:31`).

### 1.1 `IsPower()` — "파워" 계정 개념

`IsPower()`는 **manager 또는 admin**을 뜻하며(`src/MCPhoto.Core/Models/UserRole.cs:35`), 사용자 관리·공용 기본 프레임 관리 권한의 게이트로 코드 전반에서 재사용된다.

```
public static bool IsPower(this UserRole role) => role is UserRole.Manager or UserRole.Admin;
```

`IsPower()` 소비 지점:
- 상단 바 파워 여부: `AppShellViewModel.IsPower`(`src/MCPhoto.App/AppShellViewModel.cs:63`) → 팝오버의 "계정 생성"·"관리자 도구" 노출 제어(`src/MCPhoto.App/MainWindow.xaml:67`, `:71`).
- 계정 페이지 파워 판정: `AccountViewModel.IsPower`(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:72`), 계정 생성 커맨드 진입 가드(`:136`).
- 프레임 화면 파워 판정: `FrameSelectViewModel.IsPower`(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:68`) → "서버에서도 제거" 옵션 노출·유효(`:102`).

### 1.2 계정 생성 위계 게이트

역할 위계는 "누가 어떤 역할을 만들 수 있나"로 실체화된다. `CreatableRoles()`/`CanCreate()`가 순수 규칙이다(`src/MCPhoto.Core/Models/UserRole.cs:41-50`).

| 호출자(actingRole) | 생성 가능 역할 | 근거 |
| --- | --- | --- |
| admin | user, manager | `src/MCPhoto.Core/Models/UserRole.cs:43` |
| manager | user | `:44` |
| user / 게스트 | (없음) | `:45` (그 외 → `Array.Empty`) |

- **admin → admin 생성 불가**("최종 1인" 규칙). 주석과 규칙 모두에서 admin은 자기 역할을 만들지 못한다(`src/MCPhoto.Core/Models/UserRole.cs:38-39`, `:43`).
- 이 게이트는 UI뿐 아니라 **서비스 계층에서도 강제**된다: `AccountService.CreateAsync`가 `actingRole.CanCreate(role)`을 먼저 검사하고 위반 시 `UnauthorizedAccessException`을 던진다(`src/MCPhoto.Firebase/AccountService.cs:53-55`). 주석: "호출자 신뢰 금지, 위반이 미초기화보다 우선"(`:52`).

---

## 2. 권한 매트릭스(화면·기능별)

`○`=가능, `×`=불가/미노출, `△`=조건부. "게스트"는 비로그인.

| 기능 / 화면 | 게스트 | user | manager | admin | 근거 |
| --- | --- | --- | --- | --- | --- |
| 촬영(프레임 선택→촬영→결과→QR) | ○ | ○ | ○ | ○ | 촬영 흐름은 로그인 요구 없음. 홈→프레임 선택 전이에 계정 조건 없음(`src/MCPhoto.Core/Navigation/SessionStateMachine.cs:14`); "게스트 직행" 설계(it2) |
| 기본(공용) 프레임 사용 | ○ | ○ | ○ | ○ | `FrameSelectViewModel.OnEnterAsync`가 항상 기본 프레임 로드(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:70-71`) |
| 커스텀(본인) 프레임 사용 | × | ○ | ○ | ○ | 로그인 사용자만 user 프레임 추가 로드(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:73-75`) |
| 프레임 생성/편집(편집기 진입) | × | ○ | ○ | ○ | `CreateFrame`은 `IsLoggedIn`일 때만(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:181-185`). 계정당 최대 10개(`src/MCPhoto.Firebase/FrameRepository.cs:17`, `:52-53`) |
| 프레임 삭제 — 로컬(본인 로컬 저장분) | × | ○ | ○ | ○ | 삭제 UI는 로그인 시만(`CanDeleteFrames = user is not null`, `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:67`); 번들/fallback은 삭제 불가(`IsDeletable`, `:54-57`) |
| 프레임 삭제 — 서버(공용·DB) | × | × | ○ | ○ | "서버에서도 제거" 체크는 `IsPower`에서만 노출·유효(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:33`, `:102`; `alsoServer = DeleteAlsoServer && IsPower`) |
| 계정 생성 | × | × | △(user만) | △(user·manager) | `CreateAccount` 파워 가드(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:136`) + 서비스 역할 게이트(`src/MCPhoto.Firebase/AccountService.cs:53-55`); 생성 가능 역할은 [1.2](#12-계정-생성-위계-게이트) |
| 비밀번호 변경(본인) | × | ○ | ○ | ○ | `ChangePassword`는 `CurrentUser` 있을 때만(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:102-103`); 팝오버 "비밀번호 변경"은 로그인 시 항상 노출(`src/MCPhoto.App/MainWindow.xaml:61-63`) |
| 관리자 도구(사용자 관리 페이지 진입) | × | × | ○ | ○ | 팝오버 "관리자 도구"는 `IsPower`에서만 노출(`src/MCPhoto.App/MainWindow.xaml:69-72`); `OpenUserManagement`도 `IsPower` 가드(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:175-177`) |
| 사용자 목록 조회 | × | × | ○ | ○ | 사용자 관리 진입 자체가 파워 전용(위 항목); `GetAllAsync`는 "power 전용" 주석(`src/MCPhoto.Core/Accounts/IAccountService.cs:23`) |
| 사용자 삭제(cascade) | × | × | ○ | ○ | `UserMgmtViewModel.DeleteUser`(`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:56-72`); 자기 계정 삭제 방지(`:60`) |
| 사용자 비밀번호 초기화("0000") | × | × | ○ | ○ | `ResetUserPassword`(`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:74-88`, 상수 `ResetPassword = "0000"` `:16`) |
| 역할 변경(manager로 승격) | × | × | × | ○ | `PromoteToManager`는 `IsAdmin`일 때만(`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs:92-94`); `IsAdmin`=`Role == Admin`(`:36`) |
| 앱 종료(관리자) | × | × | ○ | ○ | "관리자 도구" 페이지의 `ExitApp`(`src/MCPhoto.App/ViewModels/AccountViewModel.cs:180-181`); 도구 페이지 진입이 파워 전용 |
| 설정(앱 설정) 페이지 접근 | ○ | ○ | ○ | ○ | 우상단 ⚙ 버튼은 상단 바 표시 상태면 누구나(`src/MCPhoto.App/MainWindow.xaml:45-50`, `OpenSettings` 무가드 `src/MCPhoto.App/AppShellViewModel.cs:283-287`) |

주의(사실/가정 구분):
- **설정 페이지는 현재 권한 게이트가 없다**(사실): `OpenSettings`에 역할 검사가 없으며(`src/MCPhoto.App/AppShellViewModel.cs:283-287`), 상단 바 ⚙ 버튼도 가시성만 상태 기반(`IsTopBarVisible`)이고 역할 조건이 없다(`src/MCPhoto.App/MainWindow.xaml:45-50`). 계정·관리자 기능은 설정에서 분리되어 `Account`/`UserMgmt`로 이동했으므로(it5 C1/C2, `src/MCPhoto.Core/Navigation/AppState.cs:33`), 앱 설정 자체는 키오스크 운영자가 접근하는 열린 화면이라는 것이 코드상 현재 상태다.
- **역할 변경은 승격(→manager)만 존재**(사실): 강등(manager→user)이나 admin 지정 UI는 없다. `SetRoleAsync`는 임의 역할을 받지만(`src/MCPhoto.Core/Accounts/IAccountService.cs:30`, 구현 `src/MCPhoto.Firebase/AccountService.cs:92-97`), 호출부는 `PromoteToManager`(manager 고정)뿐이다.

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
| 역할 강등/admin 위임 | 승격(→manager)만 UI 존재(`UserMgmtViewModel.cs:92-106`) | `SetRoleAsync`는 임의 역할 지원하나 UI 미노출 |
