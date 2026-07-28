# it16 설계 — AdvancedUser(고급 유저) 역할 추가 + 프레임 권한 재배분 + 설정 저장 창 위치 버그

> 프로젝트 루트: `C:\STUDY\PROJECT\PhotoBooth`
> 선행 커밋: it15 완료(`55dcf2b`까지) — Google SSO/PIN 전용, 백엔드 전용 앱, 프레임 편집 로컬 전용.
> 입력: it16 요구사항 브리프(E1~E6 확정), 현행 코드(아래 §1 file:line).
> 산출물 소비자: `js-developer`(서버) / `wpf-developer`(클라) — §9 WBS를 순서대로 실행한다.

---

## §0 개요

### 0.1 이번 이터레이션의 본질

| # | 갈래 | 내용 | 그룹 |
|---|------|------|------|
| 1 | 역할 위계 | `AdvancedUser`(서버 `advanced_user`, UI "고급 유저") 1개 추가. manager가 `temp_user`·`user`·`advanced_user` 사이를 자유 지정(E3). | **G1** |
| 2 | 프레임 권한 | it15까지의 **User 권한 로직을 AdvancedUser로 이동**. User·TempUser는 프레임 생성·편집·삭제 박탈(사용만 가능, E4). | **G2** |
| 3 | 버그 | 설정 "저장" 클릭 시 창모드 창이 옛 위치·크기로 점프. | **G3** |

- **기능 추가 없음(E6).** 위 3갈래 밖의 개선은 §10.3 로드맵에만 적고 구현하지 않는다.
- 3그룹은 서로 **파일 교집합이 없다** → 독립 빌드·테스트·커밋 가능(§9.5 참조).
- `bldinfo.ini`는 이 설계의 대상이 아니다(언급·수정 금지).

### 0.2 기술 스택(변경 없음)

- 클라이언트: .NET 8 WPF / CommunityToolkit.Mvvm / Microsoft.Extensions.DependencyInjection
- 서버: Cloud Functions 2nd gen / TypeScript 5.7 / Express 4 / jest 29
- 권한 판정은 **순수 함수(C# `UserRoleExtensions`·`RoleChangePolicy`·`FrameEditPolicy` ↔ TS `domain/roles.ts`)** 에 두고
  ViewModel·라우트는 그것을 호출만 한다(현행 구조 유지 — 창 없이·DB 없이 단위 테스트 가능).

### 0.3 무회귀 하한(it15 완료 시점 실측, 부모 보고)

| 검증 | 하한 |
|------|------|
| `dotnet build -c Release --no-incremental` | 경고 0 / 오류 0 |
| `dotnet test` | 613 / 613 통과 (이번 작업으로 **증가**해야 정상) |
| `web/functions`: `npm run typecheck` | 오류 0 |
| `web/functions`: `npm test` | 219 / 219 통과 (15 suites, 증가해야 정상) |
| 전체 스위트 5회 연속 무실패 | flake 0 |

### 0.4 확정 결정 요약(브리프 §2 — 재질의 금지)

E1 식별자 `advanced_user`/`AdvancedUser`/"고급 유저" · E2 위계 `TempUser<User<AdvancedUser<Manager<Admin`,
AdvancedUser = it15 User 권한 전체 · E3 manager는 advanced_user 이하 자유 지정, manager·admin 지정은 admin 전용 ·
E4 권한 상실 계정의 기존 로컬 프레임은 사용만(목록 노출 유지, 편집·삭제 불가) · E5 신규 SSO 계정은 temp_user 유지 ·
E6 기능 추가 없음.

---

## §1 검증된 사실 (verified facts — 코드 직접 확인)

### 1.1 역할 위계 구현

| # | 사실 | 근거 |
|---|------|------|
| F1 | C# `UserRole`은 `TempUser=0, User=1, Manager=2, Admin=3`이며 **서수를 위계 비교에 쓰지 않는다**. 위계는 `ManageRank(role)` switch가 담당하고, 주석에 "배치값은 가독성용"이라고 명시돼 있다. | `src/MCPhoto.Core/Models/UserRole.cs:4-20,75-82` |
| F2 | 저장·전송은 **전부 문자열**이다: `ToFirestoreValue()`(`temp_user`/`user`/`manager`/`admin`) ↔ `ParseRole()`. 미지원 값 폴백은 `UserRole.User`. | `UserRole.cs:25-41` |
| F3 | 클라이언트에서 역할이 와이어에 나가는 유일 지점은 `SetRoleAsync` → `role.ToFirestoreValue()`이고, 들어오는 유일 지점은 `UserRoleExtensions.ParseRole(dto.Role)`이다. **enum 서수를 직렬화하는 경로가 없다.** | `src/MCPhoto.Http/HttpAccountService.cs:105-111,188` |
| F4 | 코드베이스 전체에 `(int)role` 캐스팅·`Role >`/`Role <`/`CompareTo` 형태의 **서수 비교가 0건**이다(grep 결과 무매치). | grep `\(int\)\s*\w*[Rr]ole\|Role\.CompareTo\|Role >\|Role <` over `src/` → no matches |
| F5 | `IsPower()`는 `Manager or Admin`이며 "사용자 관리·공용 기본 프레임 관리" 축이다. | `UserRole.cs:44` |
| F6 | `CreatableRoles`/`CanCreate`(C#)와 `creatableRoles`/`canCreate`(TS)는 it15의 계정 생성 폐지 이후 **프로덕션 호출자가 0**이고 테스트만 참조한다. | grep: C# 정의 외 호출 없음(`UserRole.cs:60-69`) / TS는 `roles.ts:66-83`과 `__tests__/roles.test.ts`만 |
| F7 | 서버 위계는 `MANAGE_RANK`(temp0/user1/manager2/admin3) + `canManage`(같거나 낮음) + `canSetRole`(승격=admin 전용, manager는 `user→temp_user` 강등만). | `web/functions/src/domain/roles.ts:12-23,91-128` |
| F8 | 클라 `RoleChangePolicy.AssignableRoles`는 서버 `canSetRole`과 1:1 대칭이다(admin→[T,U,M] / manager+current=User→[U,T] / 그 외 빈 목록). | `src/MCPhoto.Core/Models/RoleChangePolicy.cs:18-27` |
| F9 | JWT의 `role` 클레임은 `isUserRole` 화이트리스트를 통과해야 한다 — **화이트리스트에 없는 역할의 토큰은 401**이 된다. | `web/functions/src/domain/jwt.ts:62-64`, `roles.ts:26-33` |
| F10 | `validateRole`은 `isUserRole` 화이트리스트를 쓰고, 실패 문구에 허용 목록을 **하드코딩**해 노출한다. | `web/functions/src/domain/validation.ts:27-32` |

### 1.2 프레임 권한 구현

| # | 사실 | 근거 |
|---|------|------|
| F11 | `FrameEditPolicy.CanEdit`: 게스트(role null) 불가 / `UserLocal`→`IsOwnedLocal(userId)` / `DbDefault`→`IsPower()` / 번들·fallback 불가. **역할이 "쓰기 권한"으로 걸러지는 단계가 없다** — 로그인 계정이면 본인 로컬은 무조건 편집 가능. | `src/MCPhoto.Core/Frames/FrameEditPolicy.cs:15-25` |
| F12 | `RequiresFork(frame)`는 출처만 본다(`UserLocal`이 아니면 true) — 역할 인자 없음. | `FrameEditPolicy.cs:32-33` |
| F13 | "프레임 만들기" 버튼은 **`IsLoggedIn`** 으로만 노출되고, `CreateFrame` 커맨드 가드도 `if (!IsLoggedIn) return`이다. | `src/MCPhoto.App/Views/FrameSelectView.xaml:87-89`, `ViewModels/FrameSelectViewModel.cs:199-203` |
| F14 | 삭제 ✕ 노출 = `FrameDeleteVis` MultiBinding[`CanDeleteFrames`, `IsPower`, `Id`]이며 `CanDeleteFrames`는 **로그인 여부**일 뿐이다. `local:` 접두면 로그인 전원 노출, 접두 없는 DB id면 power만. | `FrameSelectView.xaml:43-49`, `FrameSelectViewModel.cs:71`, `Converters/CommonConverters.cs:185-203` |
| F15 | `RequestDelete` 커맨드 가드는 `CanDeleteFrames && IsDeletable(frame)`뿐이다. `IsDeletable`은 역할을 보지 않으므로 **DB 공용 프레임에 대한 비power 삭제가 커맨드 레벨에서는 통과**한다(현재는 ✕ 버튼 미노출만이 방어). | `FrameSelectViewModel.cs:55-58,89-96` |
| F16 | 편집기 `Save()`에는 역할 가드가 없다(로그인·슬롯 유효성만 확인). 스코프 분기는 `IsPower()`로만 갈린다. | `ViewModels/FrameEditorViewModel.cs:442-462` |
| F17 | F2 "기존 프레임 불러오기"(`OpenFramePicker`)는 **편집기 내부에서만** 도달 가능하고, 편집기 진입 경로는 `FrameSelectViewModel.CreateFrame`/`EditFrame` → `AppShellViewModel.OpenFrameEditor` **2곳뿐**이다. `FramePickerViewModel`은 역할을 보지 않는다(userId만). | `FrameEditorViewModel.cs:328-337`, `AppShellViewModel.cs:275-280`, `FrameSelectViewModel.cs:197-211`, `ViewModels/FramePickerViewModel.cs:46-80` |
| F18 | power가 fork·로컬 저장한 **공용** 프레임은 디스크에서 다시 읽을 때 `Id = "local:{파일명}"`, `UserId = null`이 된다(공용은 `ownerId=null`). 따라서 `FrameOrigin.Classify`=`UserLocal`이지만 `IsOwnedLocal(userId)`는 **false**다. | `src/MCPhoto.Core/Frames/LocalFrameStore.cs:112-128`, `Frames/FrameOrigin.cs:43-47` |
| F19 | 서버 프레임 쓰기 라우트 `POST /frames`·`PUT /frames/:id`·`DELETE /frames/:id`는 **모두 `requirePower()`** 뒤에 있다. user 커스텀 프레임은 로컬 전용이라 서버에 쓰기 경로가 아예 없다. | `web/functions/src/routes/frames.ts:59-62,88-91,120-123`, `http/auth.ts:99-108` |

### 1.3 계정 라우트 게이트

| # | 사실 | 근거 |
|---|------|------|
| F20 | `DELETE /accounts/:id`와 `PATCH /accounts/:id/role`은 `requirePower()`가 붙어 있다. | `web/functions/src/routes/accounts.ts:94-96,110-112` |
| F21 | **`PUT /accounts/:id/pin`에는 `requirePower()`가 없다** — 로그인만 하면 진입하고 `canManage`(같거나 낮은 위계 허용)만 통과하면 된다. 즉 `temp_user`가 다른 `temp_user`의 PIN을, `user`가 다른 `user`의 PIN을 재설정할 수 있다. it15로 신규 계정이 전원 temp_user가 되며 이 모집단이 커졌다. | `routes/accounts.ts:124-142`, `services/accounts.ts:130-143`, `roles.ts:91-93` |
| F22 | `canManage`는 `deleteAccount`와 공유된다. 같은 위계 허용이라 **admin이 다른 admin을, manager가 다른 manager를 삭제**할 수 있고, 기존 테스트가 이 노출을 보장한다(`otherAdmin` 행에 삭제 버튼 노출). | `services/accounts.ts:149-160`, `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs:241,253` |
| F23 | 클라 `ResetPinAsync` 호출자는 `UserMgmtViewModel.ResetUserPin` 1곳뿐이며, 본인 PIN 변경은 별 경로(`PUT /accounts/me/pin`)다. | `ViewModels/UserMgmtViewModel.cs:132-148`, `routes/accounts.ts:71-91` |

### 1.4 창 위치 버그

| # | 사실 | 근거 |
|---|------|------|
| F24 | `SettingsViewModel.SaveSettings()`는 저장 성공 시 `_shell.RequestApplyDisplayMode()`를 호출한다. | `ViewModels/SettingsViewModel.cs:328-336` |
| F25 | 그 이벤트는 `MainWindow.ApplyDisplaySettings()`로 이어지고, 창모드 분기에서 `Width`/`Height`를 `s.WindowBounds`로 재적용하고 `HasPosition`이면 `Left`/`Top`까지 강제한다. | `src/MCPhoto.App/MainWindow.xaml.cs:24,34-63` |
| F26 | `WindowBounds`는 **`OnClosing`에서만** 갱신된다 → 세션 중 창을 옮기거나 리사이즈해도 ini에서 읽은 과거 값이 남아 있다. 저장 순간 그 과거 값으로 되돌아간다(= 신고된 버그). | `MainWindow.xaml.cs:65-78` |
| F27 | `ApplyDisplaySettings()`는 ① 시작 시 창 복원(ctor 호출)과 ② 런타임 표시모드 변경 적용(이벤트) **두 성격을 겸한다**. 호출자 구분 정보가 없다. | `MainWindow.xaml.cs:22-24` |
| F28 | `MainWindow`는 `_shell.DisplayModeApplyRequested`를 구독하지만 **해제 경로가 없다**. | `MainWindow.xaml.cs:24`, `OnClosing`(`:65-78`)에 `-=` 없음 |
| F29 | `RequestApplyDisplayMode`/`DisplayModeApplyRequested`에 대한 테스트가 **0건**이다. `WindowBounds`는 ini 왕복·Clamp 테스트만 있다. | grep over `tests/` → `SettingsTests.cs:58-61,303-311`만 |
| F30 | `AppShellViewModel`은 테스트에서 `new AppShellViewModel(new IdleWatchdog(), settings, EmptyServiceProvider, session)`로 직접 생성되며 `SettingsViewModel`도 창 없이 생성된다 → 셸 이벤트 발화 순서를 단위 테스트로 관측할 수 있다. | `tests/MCPhoto.Tests/SettingsViewModelTests.cs:58-82` |

### 1.5 테스트 인프라

| # | 사실 | 근거 |
|---|------|------|
| F31 | 서버 계정 테스트는 **서비스 레벨**(`setRole`/`resetOtherPin` 직접 호출 + `FakeFirestore` mock)이다 — Express 라우터를 통과시키지 않으므로 **라우트 미들웨어는 이 파일로 검증되지 않는다**. | `web/functions/src/__tests__/accounts.test.ts:1-72` |
| F32 | 미들웨어는 `Request`/`next`를 직접 모킹해 단위 테스트하는 선례가 있다(`optionalBearer`). 같은 방식으로 `requirePower()`를 역할별로 검증할 수 있다. | `web/functions/src/__tests__/optionalBearer.test.ts:23-41` |
| F33 | 기존 C# 테스트가 역할 매트릭스를 **정확한 배열 동등성**으로 검증한다(`Assert.Equal(new[]{TempUser,User,Manager}, ...)`) → 역할 1개 추가 시 이 단정들이 **반드시 깨진다**(의도된 변경, §8.1에서 갱신 대상 명시). | `tests/MCPhoto.Tests/RoleManagementTests.cs:68-119`, `UserMgmtViewModelTests.cs:104-120` |

---

## §2 미검증 가정 (open assumptions)

| # | 가정 | 왜 미검증인가 | 검증 단계 |
|---|------|---------------|-----------|
| A1 | enum 배치값(서수)을 재배치해도 런타임·데이터에 영향이 없다. F1~F4로 강한 근거가 있으나, **구현 시점의 코드**에 새 서수 의존이 없다는 보장은 grep을 다시 돌려야 얻는다. | 설계 시점 grep은 스냅샷이다. | **Step C1**(grep 게이트 포함) |
| A2 | Firestore `users` 컬렉션에 `role="advanced_user"` 문서가 아직 없다(신규 값이다). | 운영 DB를 조회하지 않았다(앱 실행·DB 접근 금지). | 검증 불필요 — 새 값이 아니라 **기존 값이어도** `parseRole`/`ParseRole`이 정의되므로 동작이 동일하다. 리스크는 §10 R4에 기재. |
| A3 | `dotnet test` 613개 중 §8.1에 열거한 것 외에 역할 배열을 단정하는 테스트가 없다. | 전 테스트 파일을 완독하지 않았다(F33은 grep 기반). | **Step C1**(테스트 실행이 곧 검증 — 실패 목록이 열거와 일치해야 한다) |
| A4 | `web/functions`의 `MANAGE_RANK` 값 재배치가 Firestore 인덱스·보안 규칙에 영향을 주지 않는다. | `firestore.rules`를 이번 조사 범위에 넣지 않았다. | **Step S1**(구현 전 `firestore.rules`에 role 문자열 하드코딩이 있는지 grep) |
| A5 | 창 기하 재적용을 생략해도 "전체화면 ↔ 창모드 즉시 전환"이 유지된다(§7 A안). 논리적으로는 모드 전환 시에만 기하를 적용하므로 유지되지만, **실제 창 동작은 앱 기동 없이는 확인할 수 없다**(앱 실행 금지). | UI 기동 금지 제약. | **Step W1**(순수 정책 단위 테스트로 "모드 전환 시 기하 적용 / 동일 모드면 무동작"을 증명) + §7.5의 **사용자 수동 확인 항목**으로 인계 |
| A6 | `IPinPromptDialogService`를 통한 PIN 재설정 UX는 이번 변경(power 게이트 추가)으로 바뀌지 않는다(사용자 관리 화면 자체가 power 전용 도달 경로). | 화면 도달 경로를 `AccountViewModel.IsPower`(`ViewModels/AccountViewModel.cs:90`)까지만 확인했고 전 경로를 추적하지 않았다. | **Step C2**(`UserMgmtViewModelTests`에 비power actor 케이스 추가 — 노출 0 확인) |

---

## §3 역할 위계·매트릭스 (G1)

### 3.1 enum 값(서수) 결정 — **위계 순으로 재배치한다**

**결정: `TempUser=0, User=1, AdvancedUser=2, Manager=3, Admin=4`** (Manager·Admin의 배치값이 2·3 → 3·4로 이동)

근거:

1. **서수는 저장·전송·비교 어디에도 쓰이지 않는다.** 저장·전송은 전부 `ToFirestoreValue()`/`ParseRole()` 문자열이고(F2·F3),
   위계 비교는 `ManageRank` switch이며(F1), 서수 캐스팅·대소 비교가 코드 전체에 0건이다(F4).
   it13이 "서수 아닌 명시적 `ManageRank`"로 바꾼 목적이 정확히 **이 재배치를 안전하게 만드는 것**이었고,
   `UserRole.cs:6-7` 주석이 "배치값은 가독성용으로 위계 순 명시 / 배치값 변경은 무해"라고 선언한다.
2. 뒤에 붙이면(`AdvancedUser=4`) 그 주석이 거짓이 되고, 파일을 읽는 사람이 **서수 순서 ≠ 위계 순서**라는
   함정을 매번 재확인해야 한다. 위계가 코드에 두 번(배치값·`ManageRank`) 나타나는데 둘이 어긋나는 상태를 만들지 않는다.
3. `default(UserRole)`은 두 방식 모두 `TempUser`(0)로 불변이다 — 기본값 의미가 바뀌지 않는다.

**안전 게이트(필수)**: Step C1에서 구현 전 아래 grep이 **무매치**임을 확인한다. 하나라도 매치되면 재배치를 포기하고
`AdvancedUser = 4` 추가(append)로 전환한다.

```
rg -n "\(int\)\s*\w*[Rr]ole|Role\.CompareTo|\bRole\s*[<>]=?|role\s*[<>]=?\s*UserRole" src/ tests/
rg -n "JsonSerializer|JsonConverter" src/ | rg -i "role"
```

### 3.2 문자열·라벨·랭크 매핑 (C# ↔ TS 동결)

| 항목 | TempUser | User | **AdvancedUser** | Manager | Admin |
|------|---|---|---|---|---|
| C# enum 배치값 | 0 | 1 | **2** | 3 | 4 |
| Firestore/와이어 문자열 | `temp_user` | `user` | **`advanced_user`** | `manager` | `admin` |
| `ManageRank` / `MANAGE_RANK` | 0 | 1 | **2** | 3 | 4 |
| `ToLabel()` (UI) | 임시 유저 | 사용자 | **고급 유저** | 매니저 | 관리자 |
| `IsPower()` / `isPower()` | ✕ | ✕ | **✕** | ○ | ○ |
| `CanWriteFrames()` (§4 신규) | ✕ | ✕ | **○** | ○ | ○ |

- **`IsPower`는 절대 확장하지 않는다.** AdvancedUser는 power가 아니다(계정 관리·공용 DB 프레임 관리 권한 없음).
  "프레임 쓰기 권한"은 §4에서 도입하는 **별개 축** `CanWriteFrames()`이며, 두 이름을 혼용하지 않는다.
- 폴백: `ParseRole`/`parseRole`의 미지원 값 → **`user`**(현행 유지). it16 이후 `user`는 프레임 쓰기 권한이 없으므로
  **폴백이 종전보다 더 안전해진다**(오탈자 문서가 프레임을 만들 수 없다). 폴백을 `temp_user`로 바꾸지 않는 이유는
  TempUser에는 QR 시간·횟수 한도가 붙어 오탈자 문서에 과금 제약을 부과하게 되기 때문이다(부작용 회피).

### 3.3 역할 지정 전수 표 (E3) — `canSetRole(actor, current, target)`

**규칙(서버·클라 동일)**

```
1) target == admin            → 거부 (최종 1인 규칙)
2) current == admin           → 거부 (admin 대상 변경 불가)
3) actor == admin             → 허용 (target ∈ {temp_user, user, advanced_user, manager})
4) actor == manager           → target ∈ {temp_user, user, advanced_user}
                                AND current ∈ {temp_user, user, advanced_user}
5) 그 외(advanced_user/user/temp_user actor) → 거부
```

**전수 표** (행 = actor + 대상의 현재 역할, 열 = 지정할 새 역할. T=temp_user, U=user, **A=advanced_user**, M=manager, D=admin)

| actor | current \ new | T | U | **A** | M | D |
|---|---|:-:|:-:|:-:|:-:|:-:|
| **admin** | T | ○ | ○ | **○** | ○ | ✕ |
| **admin** | U | ○ | ○ | **○** | ○ | ✕ |
| **admin** | **A** | ○ | ○ | **○** | ○ | ✕ |
| **admin** | M | ○ | ○ | **○** | ○ | ✕ |
| **admin** | D | ✕ | ✕ | ✕ | ✕ | ✕ |
| **manager** | T | ○ | ○ | **○** | ✕ | ✕ |
| **manager** | U | ○ | ○ | **○** | ✕ | ✕ |
| **manager** | **A** | ○ | ○ | **○** | ✕ | ✕ |
| **manager** | M | ✕ | ✕ | ✕ | ✕ | ✕ |
| **manager** | D | ✕ | ✕ | ✕ | ✕ | ✕ |
| **advanced_user** | 전부 | ✕ | ✕ | ✕ | ✕ | ✕ |
| **user** | 전부 | ✕ | ✕ | ✕ | ✕ | ✕ |
| **temp_user** | 전부 | ✕ | ✕ | ✕ | ✕ | ✕ |

**it13 대비 변경점(정확히 이것만)**

| 조합 | it13 | it16 | 성격 |
|------|------|------|------|
| manager: T→U, T→A, U→A | 거부(승격=admin 전용) | **허용** | E3 완화(승격 허용) |
| manager: A→U, A→T | (A 없음) | **허용** | 신규 |
| manager: U→T | 허용 | 허용 | 불변 |
| manager: *→M, M/D 대상 | 거부 | 거부 | 불변(manager·admin 지정은 admin 전용) |
| admin: 전부(admin 제외) | 허용 | 허용 | 불변 + A 추가 |
| 비power actor | 거부 | 거부 | 불변 |

**no-op(current == target) 처리**: manager의 `U→U`·`A→A`·`T→T`와 admin의 동일 조합은 규칙 3·4에 포함되어 **허용**된다
(멱등 write). it13 주석의 "명시 규칙에 없으면 거부"는 manager가 오직 `U→T` 하나만 갖던 시절의 부수 결과였고,
새 규칙에서는 하위 3역할 대역이 명시 규칙이므로 허용이 일관적이다. 클라이언트는 no-op을 서버로 보내지 않는다
(`UserMgmtViewModel.cs:162`의 `if (target == user.Role) return`).

### 3.4 `AssignableRoles`(C# 콤보 필터) — 서버와 1:1 대칭

```
AssignableRoles(actor, current):
  current == Admin                                 → []
  actor == Admin                                   → [TempUser, User, AdvancedUser, Manager]
  actor == Manager && current ∈ {T, U, AdvancedUser} → [TempUser, User, AdvancedUser]
  그 외                                             → []
```

- 반환 순서는 **위계 오름차순**(T → U → A → M)으로 고정한다 — 콤보 표시 순서가 곧 위계 순이 되고 테스트 단정이 안정된다.
- `current` 자신이 목록에 포함되는 것은 현행과 동일하다(UI가 무변경을 no-op으로 처리).
- 자기 계정 행은 지금처럼 `UserRowViewModel`에서 빈 목록으로 강제한다(`UserMgmtViewModel.cs:48`).

### 3.5 PIN 재설정 권한 정리 — **`canManage`는 그대로 두고 라우트에 `requirePower`를 추가한다**

서버 리뷰가 지적한 성질(F21): `PUT /accounts/:id/pin`이 로그인만 요구하고 `canManage`(같은 위계 허용)만 통과하면 되므로
`temp_user`가 다른 `temp_user`의 PIN을, `user`가 다른 `user`의 PIN을 재설정할 수 있다. it15로 신규 계정이 전원
temp_user가 되어 이 모집단이 커졌다 → **이번에 정리한다.**

**결정: `PUT /accounts/:id/pin`에 `requirePower()`를 추가한다. `canManage` 시그니처·의미는 건드리지 않는다.**

판정식(변경 후) = `isPower(actor) && canManage(actor.role, targetRole)` && `actor.id !== targetId`
(자기 자신은 현행대로 400 → 본인 경로 `PUT /accounts/me/pin` 사용, E2 유지).

근거:

1. **실제 결함은 라우트 게이트 누락**이다. 형제 라우트인 `DELETE /accounts/:id`·`PATCH /accounts/:id/role`은
   `requirePower()`를 갖는데(F20) PIN만 빠졌다. 즉 "같은 위계 관리 허용"이 아니라 **"비power가 남의 계정을 만질 수 있음"**
   이 문제의 본질이며, power 게이트 하나로 문제 모집단(temp_user·user·advanced_user 전원)이 통째로 차단된다.
2. **`canManage`를 "엄격히 높은 위계"로 좁히면 회귀가 발생한다.** `canManage`는 `deleteAccount`와 공유되고(F22),
   좁히면 admin이 다른 admin을, manager가 다른 manager를 삭제하던 기존 동작이 사라진다.
   이는 브리프가 요청한 범위(PIN 재설정 정리)를 넘고 기존 테스트(`UserMgmtViewModelTests.cs:241`)를 깨뜨린다.
3. **"대상보다 엄격히 높은 위계 OR power" 안은 새 구멍을 만든다.** AdvancedUser(랭크 2)가 user(1)·temp_user(0)보다
   높으므로 "엄격히 높은 위계" 조건만으로 **advanced_user가 남의 PIN을 재설정**할 수 있게 된다.
   AdvancedUser는 계정 관리 권한이 전혀 없어야 하므로(E2 — it15 User와 동일 권한) 이 안은 채택하지 않는다.
4. power 계정이 잃는 능력이 **0**이다(manager→{T,U,A,M}, admin→전원 유지).

클라이언트 대칭: `UserRowViewModel.CanResetPin = !isSelf && actorRole.IsPower() && actorRole.CanManage(user.Role)`
(현재는 `IsPower()` 항이 없다 — `UserMgmtViewModel.cs:50`). `ResetUserPin` 커맨드 가드에도 동일 조건을 넣어
UI 미노출·커맨드 가드·서버 3중 방어를 맞춘다. 사용자 관리 화면 자체가 power 전용 도달 경로이므로 실사용 UX 변화는 없다(A6).

### 3.6 `CreatableRoles`/`creatableRoles` 처리 — 목록에만 A를 추가한다

it15의 계정 생성 폐지로 프로덕션 호출자가 0이고 테스트만 참조한다(F6). **삭제하지 않고 목록만 갱신한다:**

```
admin   → [TempUser, User, AdvancedUser, Manager]
manager → [TempUser, User, AdvancedUser]
그 외    → []
```

근거: (a) 삭제는 이번 브리프 범위가 아니고 테스트 삭제까지 연쇄된다. (b) 남겨두면서 새 매트릭스와 어긋난 상태로
방치하면, 훗날 이 함수가 되살아날 때 E3와 모순되는 규칙이 조용히 부활한다. 목록 갱신은 비용이 거의 0이고 드리프트를 없앤다.
(c) `canCreate`의 의미(actor가 만들 수 있는 역할)는 E3의 지정 권한과 자연히 일치한다.

### 3.7 하위호환

| 상황 | 동작 | 근거·조치 |
|------|------|-----------|
| **구버전 앱** + 신버전 서버, 계정이 `advanced_user` | 구버전 `ParseRole`은 미지원 값을 `UserRole.User`로 폴백 → 프레임 생성·편집 버튼이 보이지만 **저장은 로컬**이므로 서버 무영향. 구버전에는 애초에 user에게 프레임 권한이 있었으므로 **동작이 곧 구버전의 정상 동작**이다. | `UserRole.cs:40`. 서버 강제 관점의 위험 0(프레임 쓰기 라우트는 power 전용, F19). |
| **구버전 앱**이 역할 콤보로 `advanced_user`를 보낼 가능성 | 없음 — 구버전 `AssignableRoles`에 A가 없다. | `RoleChangePolicy.cs:18-27` |
| **신버전 앱** + 구버전 서버(배포 시차) | `PATCH /accounts/:id/role`에 `advanced_user` 전송 → 구버전 `validateRole` 화이트리스트 실패로 **400**. 사용자에게는 "역할 변경에 실패했습니다"로 표시된다(현행 예외 처리 경로, `UserMgmtViewModel.cs:184-189`). | **배포 순서: 서버 먼저, 클라 나중**(§9.5에 명시). |
| Firestore 문서의 `role`이 예상 외 값(오탈자·구값·누락) | `parseRole`/`ParseRole` → `user`. it16 이후 `user`는 프레임 쓰기 권한이 없어 **fail-closed 방향**이다. | §3.2 폴백 결정 |
| `advanced_user` JWT를 구버전 서버가 검증 | `isUserRole` 실패 → 401. 신버전 서버에서는 화이트리스트에 추가되므로 정상(F9). | 위와 같은 배포 순서로 해소 |

---

## §4 프레임 권한 재배분 (G2)

### 4.1 목표 권한 표 (브리프 §3.1 확정)

| 역할 | 생성 | 편집 | 삭제 | 사용(촬영) | 목록 노출 |
|------|:-:|:-:|:-:|:-:|:-:|
| TempUser | ✕ | ✕ | ✕ | ○ | ○(본인 기존 프레임 포함) |
| User | ✕ | ✕ | ✕ | ○ | ○(본인 기존 프레임 포함) |
| **AdvancedUser** | ○(개인 로컬) | ○(본인 로컬) | ○(로컬 저장분) | ○ | ○ |
| Manager | ○(공용 + 신규는 DB 등록) | ○ | ○ | ○ | ○ |
| Admin | ○(공용 + 신규는 DB 등록) | ○ | ○ | ○ | ○ |

### 4.2 새 권한 축 — `CanWriteFrames()`

`src/MCPhoto.Core/Models/UserRole.cs`의 `UserRoleExtensions`에 추가한다.

```csharp
/// <summary>
/// 프레임 쓰기 권한(생성·편집·삭제). AdvancedUser 이상. (it16 E2)
/// ⚠️ IsPower()와 **별개 축**이다: IsPower=계정 관리·공용 DB 프레임 관리, CanWriteFrames=프레임 저작.
///    AdvancedUser는 CanWriteFrames=true, IsPower=false다. 두 판정을 서로 대체하지 않는다.
/// </summary>
public static bool CanWriteFrames(this UserRole role)
    => role is UserRole.AdvancedUser or UserRole.Manager or UserRole.Admin;
```

- **`ManageRank` 기반 부등식(`rank >= 2`)으로 쓰지 않는다.** 관리 위계와 저작 권한은 다른 축이며,
  훗날 관리 위계에 역할이 끼어들 때 저작 권한이 조용히 따라 움직이는 것을 막는다(명시 열거 유지 — it13이 서수를 버린 것과 같은 이유).
- 서버에는 이 축의 대응물이 **필요하지 않다**(§5.2 근거: 프레임 쓰기 라우트가 이미 power 전용).

### 4.3 역할을 보는 지점 전수 조사 (31개 지점)

**변경 = 9개**(신규 4 포함), **불변 = 22개**. `role` 인자를 받거나 `CurrentUser.Role`을 읽는 모든 지점을 열거한다.

#### (a) `src/MCPhoto.Core/Models/UserRole.cs`

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 1 | `IsPower()` (`:44`) | Manager or Admin | 동일 | – |
| 2 | **`CanWriteFrames()`** | (없음) | AdvancedUser or Manager or Admin | **신규** |

#### (b) `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 3 | `CanEdit` 게스트 차단 (`:18`) | `role is null` → false | 동일 | – |
| 4 | `CanEdit` **쓰기 권한 게이트** | (없음) | `!role.Value.CanWriteFrames()` → **false** | **변경** |
| 5 | `CanEdit` UserLocal 분기 (`:21`) | `IsOwnedLocal(frame, userId)` | 동일(게이트 통과 후) | – |
| 6 | `CanEdit` DbDefault 분기 (`:22`) | `IsPower()` | 동일 | – |
| 7 | `CanEdit` Bundle/Fallback (`:23`) | false | 동일 | – |
| 8 | `RequiresFork` (`:32-33`) | 출처만 판정(역할 무관) | 동일 | – |
| 9 | **`CanDelete(frame, role)`** | (없음 — VM·컨버터에 산재) | 아래 §4.4 | **신규** |

변경 후 `CanEdit`:

```csharp
public static bool CanEdit(FrameTemplate frame, UserRole? role, string? userId)
{
    if (role is null) return false;                       // 게스트
    if (!role.Value.CanWriteFrames()) return false;       // it16 E4: user·temp_user는 사용만(읽기 전용)

    return FrameOrigin.Classify(frame) switch
    {
        FrameOriginKind.UserLocal => FrameOrigin.IsOwnedLocal(frame, userId),
        FrameOriginKind.DbDefault => role.Value.IsPower(),
        _ => false
    };
}
```

#### (c) `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 10 | `IsLoggedIn` 프로퍼티 (`:26`, set `:70`) | 로그인 여부 | 그대로 유지(값·의미 불변) — **버튼 바인딩만 이전(#28)** | – |
| 11 | `CanDeleteFrames` (`:30`, set `:71`) | `user is not null` | **`user is not null && user.Role.CanWriteFrames()`** | **변경** |
| 12 | `IsPower` (`:31`, set `:72`) | `Role.IsPower()` | 동일 | – |
| 13 | **`CanCreateFrame`** | (없음) | `user is not null && user.Role.CanWriteFrames()` | **신규** |
| 14 | `IsDeletable(frame)` static (`:55-58`) | 번들·fallback·빈 Id 배제(역할 무관) | 동일(유지) | – |
| 15 | `RequestDelete` 가드 (`:92`) | `!CanDeleteFrames \|\| !IsDeletable(frame)` | **`!FrameEditPolicy.CanDelete(frame, role) \|\| !IsDeletable(frame)`** (§4.4) | **변경** |
| 16 | `ConfirmDelete` `alsoServer = DeleteAlsoServer && IsPower` (`:106`) | power만 서버 삭제 | 동일 | – |
| 17 | `CreateFrame` 가드 (`:201`) | `if (!IsLoggedIn) return` | **`if (!CanCreateFrame) return`** | **변경** |
| 18 | `EditFrame` 가드 (`:209`) | `!CanEdit(SelectedFrame)` | 동일(정책이 강화됨 — 자동 반영) | – |
| 19 | `CanEdit(f)` 헬퍼 (`:217-221`) | `FrameEditPolicy.CanEdit` 위임 | 동일 | – |
| 20 | `OnSelectedFrameChanged → CanEditSelected` (`:223-224`) | 동일 | 동일(값이 정책으로 변경) | – |

#### (d) `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 21 | `SaveScopeNotice`의 `isPower` (`:78`) | power=공용/DB 문구, 그 외=개인 문구 | 동일 — AdvancedUser는 비power 분기로 **it15 User와 같은 문구** | – |
| 22 | `ExistingNamesForCurrentScope` `IsPower()` (`:425`) | power=공용 목록 / 그 외=개인 목록 | 동일 | – |
| 23 | `Save()` `isPower` (`:452`) | 저장 스코프 분기 | 동일 | – |
| 24 | **`Save()` 쓰기 권한 fail-closed 가드** | (없음, F16) | `if (!user.Role.CanWriteFrames()) { StatusMessage = "프레임을 만들 권한이 없습니다."; return; }` | **변경** |
| 25 | `OpenFramePicker` (`:329`) | 역할 무관 | 동일 — §4.5에서 우회 불가 확인 | – |
| 26 | `IsCreateMode` (`:66`) | `!_isEditing` | 동일(역할 무관) | – |

#### (e) `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs`

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 27 | `LoadAsync(userId, ct)` (`:46-80`) | 역할 지점 **0**(userId만) | 동일 | – |

#### (f) XAML·컨버터

| # | 지점 | 현행 | it16 | 변경 |
|---|------|------|------|:-:|
| 28 | `FrameSelectView.xaml:88` "프레임 만들기" Visibility | `IsLoggedIn` | **`CanCreateFrame`** | **변경** |
| 29 | `FrameSelectView.xaml:43-49` 삭제 ✕ `FrameDeleteVis` MultiBinding | [`CanDeleteFrames`, `IsPower`, `Id`] | **바인딩·컨버터 코드 불변** — 입력 `CanDeleteFrames`의 의미가 #11로 강화되어 자동 반영 | – |
| 30 | `FrameSelectView.xaml:69` "서버에서도 제거" CheckBox `IsPower` | power만 | 동일 | – |
| 31 | `FrameSelectView.xaml:92` "선택 편집" `CanEditSelected` | 정책 결과 | 동일(값이 정책으로 변경) | – |
| – | `CommonConverters.cs:185-203` `FrameDeleteVisibilityConverter` | – | **코드 변경 없음** | – |
| – | `FrameEditorView.xaml:27,57` `IsCreateMode` 게이트 | – | 변경 없음 | – |

### 4.4 `FrameEditPolicy.CanDelete` — 삭제 규칙을 순수 함수로 승격

현재 삭제 판정은 **컨버터(가시성) + `IsDeletable`(출처) + `CanDeleteFrames`(로그인)** 3곳에 흩어져 있고,
커맨드 가드가 컨버터보다 느슨하다(F15 — 비power가 DB 공용 프레임의 로컬 파일을 지울 수 있는 커맨드 경로).
역할 재배분과 함께 판정을 한 곳으로 모은다.

```csharp
/// <summary>
/// 이 프레임을 현재 역할로 삭제(로컬 파일 제거)할 수 있는지. (it16 E4)
/// 게스트·쓰기 권한 없는 역할(user·temp_user) 불가. 로컬 저장분 = 가능, DB 공용 = power만,
/// 번들·fallback·빈 Id = 불가.
/// ⚠️ 소유자(userId)를 보지 않는다: power가 fork·저장한 **공용** 로컬 프레임은 UserId=null로 로드되므로
///    (LocalFrameStore.cs:112-128) IsOwnedLocal로 판정하면 현행 삭제 능력이 회귀한다.
///    타인 개인 프레임은 LoadUser의 `{계정}_` 접두 필터로 목록에 애초에 오르지 않는다.
/// </summary>
public static bool CanDelete(FrameTemplate frame, UserRole? role)
{
    if (role is null || !role.Value.CanWriteFrames()) return false;

    return FrameOrigin.Classify(frame) switch
    {
        FrameOriginKind.UserLocal => true,                 // 로컬 저장분(개인 `local:` / power 공용 fork)
        FrameOriginKind.DbDefault => role.Value.IsPower(), // 공용 DB 프레임은 power만
        _ => false                                         // 번들·fallback·빈 Id
    };
}
```

**현행 대비 차이 정리**

| 대상 | it15 (컨버터 기준 실동작) | it16 |
|------|--------------------------|------|
| 게스트 | ✕ | ✕ |
| temp_user·user, 모든 프레임 | 로컬 저장분 ○ / DB ✕(버튼) · 커맨드는 ○ | **전부 ✕** (E4) |
| advanced_user, 로컬 저장분 | (역할 없음) | ○ |
| advanced_user, DB 공용 | (역할 없음) | ✕ |
| power, 로컬 저장분 | ○ | ○ (변화 없음 — 위 ⚠️ 근거) |
| power, DB 공용 | ○ | ○ |
| 번들·fallback | ✕ | ✕ |

`RequestDelete`는 `FrameEditPolicy.CanDelete(frame, role)`와 `IsDeletable(frame)`를 **둘 다** 확인한다
(`IsDeletable`은 "이 프레임 파일을 지울 수 있는가"라는 출처 판정이라 남긴다 — 빈 Id 방어가 컨버터와 대칭).

### 4.5 it15 fork·F2 피커와의 맞물림 — **우회 경로 없음(점검 완료)**

브리프가 지목한 위험: "user가 F2로 기존 프레임을 불러와 새로 만드는 경로가 열려 있으면 생성 금지가 우회된다."

**점검 결과 — 우회 불가.** 근거 사슬(F17):

1. F2 버튼(`OpenFramePickerCommand`)은 `FrameEditorView.xaml:56-58`에만 존재한다 → **편집기 화면 안에서만** 눌린다.
2. 편집기(`AppState.FrameEditor`) 진입은 `AppShellViewModel.OpenFrameEditor` 1개 함수이고,
   그 호출자는 `FrameSelectViewModel.CreateFrame`(#17)과 `EditFrame`(#18) **2곳뿐**이다.
3. #17이 `CanCreateFrame`, #18이 `FrameEditPolicy.CanEdit`으로 막히면 user·temp_user는 **편집기에 도달하지 못하고**,
   따라서 F2 모달도 열 수 없다.
4. 3중 방어로 `Save()`에 fail-closed 가드(#24)를 둔다 → 미래에 다른 진입점이 생기더라도 **저장이 거부**된다.
5. fork 규칙(`RequiresFork`)·사본 이름(`FrameNaming.NextCopyName`)은 역할과 무관하므로 그대로 둔다.
   AdvancedUser는 비power 분기를 타서 it15 User와 **동일하게** 개인 스코프 `{계정}_{이름}.png`로 저장된다
   → 이름 기준 dedup(재다운로드 방지)·`'_'` 규약에 새 영향이 없다.

### 4.6 E4 — 권한을 잃은 계정의 기존 프레임 (읽기 전용)

| 항목 | 동작 | 구현 지점 |
|------|------|-----------|
| 목록 노출 | **유지**(숨기지 않는다) | `ReloadFramesAsync`의 `GetUserFramesAsync(user.Id)` 무변경 (`FrameSelectViewModel.cs:77-79`) |
| 촬영 사용 | 가능 | `Next` 커맨드 무변경 (`:188-195`) |
| 선택 편집 버튼 | **미노출** | `CanEditSelected` = `CanEdit` = false (#4) |
| 삭제 ✕ | **미노출** | `CanDeleteFrames`=false → 컨버터 첫 조건에서 Collapsed (#11) |
| 프레임 만들기 버튼 | **미노출** | `CanCreateFrame`=false (#13·#28) |
| 커맨드 직접 호출(키보드·자동화) | 거부 | `RequestDelete`(#15)·`CreateFrame`(#17)·`EditFrame`(#18)·`Save`(#24) 가드 |

**의도적으로 하지 않는 것**: 기존 프레임의 파일 삭제·마이그레이션·소유권 이전. E4는 "그대로 두고 읽기 전용"이며
E6(기능 추가 없음)에 따라 이관·정리 UI를 만들지 않는다.

---

## §5 서버 강제 (G1·G2 서버측)

### 5.1 HTTP 계약 동결표 (서버·클라 병렬 작업의 경계 — 상호 대기 없음)

| 항목 | 값 | 비고 |
|------|-----|------|
| 역할 문자열 | `temp_user` / `user` / **`advanced_user`** / `manager` / `admin` | 정확히 이 snake_case. 다른 표기 금지 |
| `MANAGE_RANK` | 0 / 1 / **2** / 3 / 4 | C# `ManageRank`와 동일 |
| `isPower` | manager, admin **만** | advanced_user 포함 금지 |
| `PATCH /accounts/:id/role` 요청 | `{ "role": "advanced_user" }` | 게이트: `requirePower` + `canSetRole`(§3.3) |
| 위 라우트 응답 | 성공 204 / 매트릭스 위반 403 / 미지원 역할 문자열 400 / 대상 없음 404 | 현행 매핑 유지 |
| `PUT /accounts/:id/pin` | **`requirePower()` 추가** → 비power는 **403** | 기존: 로그인 + `canManage`. 자기 자신 대상은 계속 400 |
| `POST /frames`, `PUT /frames/:id`, `DELETE /frames/:id` | **변경 없음.** advanced_user·user·temp_user 전부 **403** | §5.2 근거 |
| `GET /frames/default` (API키), `GET /frames?userId=` (Bearer) | 변경 없음 | 조회는 역할 무관(본인 or power) |
| `GET /accounts/me/qr-usage` | 변경 없음 — advanced_user는 `blocked:false, reason:"ok"`(비TempUser 분기) | `services/accounts.ts:306` |
| 클라 → 서버 `actingRole` 전달 | 없음(현행 유지) — 서버는 JWT의 role만 신뢰 | `routes/accounts.ts:4` |

**배포 순서: 서버 먼저 → 클라 나중**(§3.7 근거: 신클라 + 구서버는 400).

### 5.2 프레임 라우트는 **변경하지 않는다** (근거)

브리프 §3.3은 "`routes/frames.ts`의 권한 판정에 새 역할 반영, user·temp_user의 프레임 쓰기는 서버가 거부"를 요구한다.
**현행 코드가 이미 이 요구를 완전히 만족한다:**

- `POST /frames`(생성) `PUT /frames/:id`(수정) `DELETE /frames/:id`(삭제)가 모두 `requireBearer() + requirePower()` 뒤에 있다(F19).
- `requirePower`는 `isPower(role)`(manager/admin)만 통과시킨다(`http/auth.ts:99-108`).
- `isPower`에 advanced_user를 **넣지 않기로** 확정했으므로(§3.2), 변경 후에도 user·temp_user·advanced_user의
  프레임 쓰기는 전부 403이다.
- AdvancedUser의 프레임은 **개인 로컬 저장뿐**이므로 서버에 쓰기 요청 자체가 발생하지 않는다(it15 로컬 전용 정책).

따라서 **새 미들웨어를 도입하지 않는다.** `requirePower`의 의미(manager+admin)는 훼손하지 않으며,
"프레임 쓰기 권한"이라는 클라 측 축(`CanWriteFrames`)과 서버의 power 축을 서버 코드에서 섞지 않는다.

**대신 회귀 테스트로 이 성질을 못 박는다**(Step S3): `requirePower()`에 `advanced_user` principal을 넣으면 403이고,
`frames.ts`가 세 쓰기 라우트에 `requirePower()`를 유지하고 있음을 구조 검증(grep)한다.
— 근거를 코드가 아니라 테스트가 보장하게 만들어, 훗날 누군가 `isPower`에 advanced_user를 추가하면 즉시 실패하게 한다.

### 5.3 서버 변경 목록 (파일별)

| 파일 | 변경 |
|------|------|
| `web/functions/src/domain/roles.ts` | `UserRole` union에 `"advanced_user"` 추가 / `MANAGE_RANK` 재배치(§5.1) / `isUserRole` 화이트리스트 추가 / `parseRole`에 `case "advanced_user"` 추가 / `creatableRoles` 목록 갱신(§3.6) / **`canSetRole` 새 매트릭스**(§3.3) / `isPower` **불변** |
| `web/functions/src/domain/validation.ts` | `validateRole` 실패 문구의 하드코딩 목록에 `advanced_user` 추가(`:30`). 로직 변경 없음 |
| `web/functions/src/routes/accounts.ts` | `PUT /:id/pin`에 `requirePower()` 추가(`:126-128`) + 주석에 "power 전용" 명시 |
| `web/functions/src/services/accounts.ts` | **로직 변경 없음.** `setRole`의 403 사유 문구 분기는 현행 유지(`:176-185`) — admin 특수 케이스 문구가 여전히 정확하다 |
| `web/functions/src/routes/frames.ts` | **변경 없음**(§5.2) |
| `web/functions/src/http/auth.ts` | **변경 없음** — `requirePower`/`requireAdmin` 의미 보존 |

`canSetRole` 구현 형태(권장 — 매트릭스를 코드가 그대로 읽히게):

```ts
/** it16: 하위 3역할 대역(temp_user·user·advanced_user)은 manager가 자유 지정. manager·admin 지정은 admin 전용. */
const LOWER_BAND: readonly UserRole[] = ["temp_user", "user", "advanced_user"];

export function canSetRole(actorRole: UserRole, currentRole: UserRole, targetRole: UserRole): boolean {
  if (targetRole === "admin") return false;   // 최종 1인 규칙
  if (currentRole === "admin") return false;  // admin 대상 변경 불가
  if (actorRole === "admin") return true;     // target ∈ {temp_user,user,advanced_user,manager}
  if (actorRole === "manager")
    return LOWER_BAND.includes(currentRole) && LOWER_BAND.includes(targetRole);
  return false;                               // advanced_user/user/temp_user
}
```

### 5.4 Firestore 스키마 영향

- `users.role`에 `advanced_user` 값이 추가될 수 있다는 것 외에 **스키마 변경 없음**(필드 추가·인덱스 변경 없음).
- 마이그레이션 스크립트 **불필요** — 기존 문서는 전부 기존 4값 중 하나이며 그 의미가 바뀌지 않는다.
  (`user` 계정이 프레임 권한을 잃는 것은 **클라 정책 변경**이며 문서 값 변경이 아니다. 승격이 필요한 계정은
  관리자가 사용자 관리 화면에서 `advanced_user`로 지정한다 — 이것이 이번 이터레이션의 운영 동선이다.)
- `firestore.rules`에 역할 문자열이 하드코딩돼 있는지는 Step S1에서 grep으로 확인한다(가정 A4).

---

## §6 UI 파급 (G1·G2 클라측)

### 6.1 역할 라벨 노출 지점 — `ToLabel()` 1곳 수정으로 전부 커버

| 지점 | 경로 | 조치 |
|------|------|------|
| `UserRoleExtensions.ToLabel()` | `UserRole.cs:47-54` | **`AdvancedUser => "고급 유저"` 추가** (유일한 수정) |
| 사용자 관리 목록 "역할" 열 | `UserMgmtView.xaml:27` (`RoleLabel` 컨버터) | 자동 반영 |
| 사용자 관리 역할 변경 콤보 항목 | `UserMgmtView.xaml:40-44` | 자동 반영(`AssignableRoles`에 A 포함 — §3.4) |
| 계정 화면 역할 표기 | `ViewModels/AccountViewModel.cs:62` `RoleLabel` | 자동 반영 |
| 진단 모달 계정 요약 | `ViewModels/DiagnosticsViewModel.cs:86` | 자동 반영 |
| 역할 변경 성공 토스트 | `UserMgmtViewModel.cs:175` `target.ToLabel()` | 자동 반영 |
| `RoleLabelConverter` | `CommonConverters.cs:126-134` | 코드 변경 없음(주석의 라벨 열거만 갱신) |

`ToLabel`의 미지원 값 폴백은 `"사용자"` 그대로 둔다(현행).

### 6.2 사용자 관리 화면

| 항목 | 변경 |
|------|------|
| 역할 콤보 옵션 | `RoleChangePolicy.AssignableRoles`가 §3.4로 갱신 → manager actor도 하위 3역할 대역에서 콤보가 **노출**된다(현재는 `current==User`일 때만 노출) |
| `CanChangeRole` | 로직 불변(`AssignableRoles.Count > 0`) |
| `ApplyRoleChange` 1차 게이트 | 로직 불변(`AssignableRoles(...).Contains(target)`) — 새 매트릭스 자동 반영 |
| `CanResetPin` | **`!isSelf && actorRole.IsPower() && actorRole.CanManage(user.Role)`** (§3.5) |
| `ResetUserPin` 커맨드 가드 | `IsPower()` 항 추가(§3.5) — 문구는 기존 `"상위 역할 계정은 관리할 수 없습니다."` 재사용 |
| 삭제 버튼 `RoleActionVis` | **변경 없음**(`CanManage` 의미 불변) |
| XAML | **변경 없음** — 콤보·버튼 바인딩이 모두 VM 프로퍼티 경유 |

### 6.3 프레임 화면 (§4.3 (c)·(f) 반영)

| 항목 | 변경 |
|------|------|
| "프레임 만들기" 버튼 | `Visibility="{Binding CanCreateFrame, ...}"` (`FrameSelectView.xaml:88`) — 주석도 "AdvancedUser 이상"으로 갱신 |
| "선택 편집" 버튼 | 바인딩 불변(`CanEditSelected`), 값이 정책으로 변경 |
| 삭제 ✕ | 바인딩·컨버터 불변, `CanDeleteFrames` 의미 강화로 자동 반영 |
| 편집기 `Save()` | fail-closed 가드 추가(#24) — 도달 불가 경로의 3중 방어 |

### 6.4 설정 화면 — 변경 없음

- `SettingsViewModel.IsTempUser`(`:74`)는 `Role == UserRole.TempUser` 비교라 AdvancedUser에 영향 없음.
- QR 한도 게이트(`IsTempUserBlocked`·`CanEditQr`)는 TempUser 전용이며 AdvancedUser는 **User와 동일하게 무제한**이다(E2).
- 게스트 편집 게이트(`IsGuest` 3지점)도 역할과 무관하게 유지된다.

### 6.5 MVVM·누수 관점 점검

- 새로 추가되는 VM 멤버(`CanCreateFrame`)는 `[ObservableProperty]` bool이며 `ReloadFramesAsync`에서만 set된다
  (`CanDeleteFrames`·`IsPower`와 동일 패턴) → 통지 경로 동일, 이벤트 구독 없음 → **누수 없음**.
- 권한 판정은 모두 `MCPhoto.Core`의 순수 함수에 있어 `System.Windows` 타입 의존이 0이다 → **창 없이 단위 테스트 가능**.
- 새 컨버터·새 리소스 키를 도입하지 않는다 → 리소스 키 충돌 위험 0.

---

## §7 설정 저장 시 창모드 위치 변경 버그 (G3)

### 7.1 원인 요약 (브리프 §4.1 — 코드로 재확인)

저장 → `RequestApplyDisplayMode()`(F24) → `ApplyDisplaySettings()`(F25) → 창모드 분기가 `Width/Height`와
(`HasPosition`이면) `Left/Top`을 `s.WindowBounds`로 **재적용**한다. 그런데 `WindowBounds`는 `OnClosing`에서만
갱신되므로(F26) 세션 중 이동·리사이즈한 창이 **ini에서 읽은 과거 값으로 점프**한다.
근본 원인은 `ApplyDisplaySettings()`가 ① 시작 시 복원과 ② 런타임 모드 적용을 겸하는 것이다(F27).

### 7.2 채택안 — **A + B 조합** (A가 버그를 고치고, B가 재적용 대상 값을 신선하게 유지)

| 안 | 내용 | 채택 | 이유 |
|----|------|:-:|------|
| **A** | 런타임 적용은 **표시 모드가 실제로 바뀔 때만** 창에 손대고, 동일 모드 저장은 **완전 무동작**으로 만든다 | ○ | 점프의 직접 원인(불필요한 기하 재적용)을 제거한다. `WindowState=Normal` 강제도 함께 사라져 **최대화 상태로 저장해도 창이 원복되지 않는다**(같은 부류의 두 번째 점프까지 해결) |
| **B** | 저장 **직전에** 현재 창 기하를 `WindowBounds`에 반영한다 | ○ | A만 적용하면 `전체화면 → 창모드` 복귀 시 여전히 과거 값으로 돌아간다. B가 있으면 그 복귀가 "사용자가 마지막으로 두었던 자리"로 정확해지고, ini에도 현재 위치가 남는다(종료 없이도 위치 보존) |

**it9 후속 요구(전체화면 ↔ 창모드 즉시 전환)는 유지된다**: 모드가 바뀌는 저장에서는 지금과 동일하게
`WindowStyle`/`ResizeMode`/`WindowState`(+창모드면 기하)를 적용한다. A는 *모드가 같을 때만* 무동작이다.

### 7.3 순수 로직 분리 — `DisplayApplyPolicy` (신규, `MCPhoto.Core.Settings`)

테스트에서 `Window`를 new 할 수 없으므로(헤드리스 제약) 판단을 전부 순수 함수로 뽑는다.

```csharp
namespace MCPhoto.Core.Settings;

/// <summary>표시 모드 적용 시 창에 무엇을 할지. (it16 §7)</summary>
public enum DisplayApplyAction
{
    /// <summary>무동작 — 이미 같은 모드다. 창 스타일·상태·기하 전부 건드리지 않는다(위치 점프 방지).</summary>
    None,
    /// <summary>전체화면 적용(WindowStyle.None + NoResize + Maximized). 기하 미적용.</summary>
    Fullscreen,
    /// <summary>창모드 적용 + WindowBounds로 기하 복원(시작 복원, 전체화면→창모드 복귀).</summary>
    WindowedRestoreGeometry
}

/// <summary>
/// 표시 모드 적용 판정(순수). ① 시작 복원과 ② 런타임 모드 변경을 하나의 규칙으로 통합한다.
/// appliedMode=null이 "아직 한 번도 적용하지 않음"(=시작)이라는 유일한 신호다.
/// </summary>
public static class DisplayApplyPolicy
{
    public static DisplayApplyAction Decide(DisplayMode target, DisplayMode? appliedMode)
        => appliedMode == target
            ? DisplayApplyAction.None
            : target == DisplayMode.Fullscreen
                ? DisplayApplyAction.Fullscreen
                : DisplayApplyAction.WindowedRestoreGeometry;
}
```

**결정 표**

| appliedMode | target | 결과 | 의미 |
|---|---|---|---|
| `null`(시작) | Fullscreen | `Fullscreen` | 시작 시 전체화면 |
| `null`(시작) | Windowed | `WindowedRestoreGeometry` | 시작 시 ini 기하 복원(현행 동작 보존) |
| Windowed | Windowed | **`None`** | **버그 수정 지점** — 저장해도 창이 움직이지 않는다 |
| Fullscreen | Fullscreen | `None` | 무의미한 재적용 제거 |
| Windowed | Fullscreen | `Fullscreen` | it9 즉시 전환 |
| Fullscreen | Windowed | `WindowedRestoreGeometry` | it9 즉시 전환 + 크기·위치 복원 |

### 7.4 `MainWindow` 개조 (뷰 전용 코드 — 최소 변경)

```csharp
private DisplayMode? _appliedMode;   // null = 아직 적용 전(시작)

private void ApplyDisplaySettings()
{
    var s = _settings.Current;
    switch (DisplayApplyPolicy.Decide(s.DisplayMode, _appliedMode))
    {
        case DisplayApplyAction.None:
            return;                                  // 창 기하·상태 불변(위치 점프 방지)

        case DisplayApplyAction.Fullscreen:
            WindowStyle = WindowStyle.None;
            ResizeMode  = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            break;

        case DisplayApplyAction.WindowedRestoreGeometry:
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode  = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width  = s.WindowBounds.Width;
            Height = s.WindowBounds.Height;
            if (s.WindowBounds.HasPosition)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowBounds.Left;
                Top  = s.WindowBounds.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            break;
    }
    _appliedMode = s.DisplayMode;                     // 적용 성공 후에만 기록
}

/// <summary>현재 창 기하를 설정 객체에 반영(창모드 + Normal일 때만). 저장 직전·종료 시 공용.</summary>
private void CaptureWindowBounds(AppSettings s)
{
    if (_appliedMode != DisplayMode.Windowed || WindowState != WindowState.Normal) return;
    s.WindowBounds.Left   = Left;
    s.WindowBounds.Top    = Top;
    s.WindowBounds.Width  = Width;
    s.WindowBounds.Height = Height;
}
```

- `OnClosing`의 기하 저장 블록(`MainWindow.xaml.cs:68-75`)을 `CaptureWindowBounds(s)` 호출로 교체한다
  (판정 기준이 `s.DisplayMode` → `_appliedMode`로 바뀐다 — **실제로 창에 적용된 모드**가 더 정확한 기준이다).
- **이벤트 구독 해제**: `OnClosing`에서 `_shell.DisplayModeApplyRequested -= ApplyDisplaySettings;`와
  새 이벤트의 `-=`를 `_shell.Dispose()` **전에** 수행한다(F28 — 현재 해제 경로가 없다. 수명이 앱 전체라 실害는 없지만
  이번에 이 코드를 만지므로 규칙대로 닫는다).

### 7.5 B안 배선 — 저장 직전 기하 캡처

`SettingsViewModel`은 `Window`를 알 수 없으므로 기존 `DisplayModeApplyRequested`와 **동형의 셸 이벤트**를 하나 더 둔다.

```csharp
// AppShellViewModel
/// <summary>설정 저장 직전, 현재 창 기하를 AppSettings.WindowBounds에 반영하도록 셸 창에 요청. (it16 §7.5)</summary>
public event Action? WindowBoundsCaptureRequested;
public void RequestCaptureWindowBounds() => WindowBoundsCaptureRequested?.Invoke();
```

`SettingsViewModel.SaveSettings()` **첫 줄**에서 호출한다:

```csharp
private void SaveSettings()
{
    // it16 §7: 저장 직전 현재 창 기하를 반영 → ini에 실제 위치가 남고, 저장 후 재적용이 점프를 만들지 않는다.
    // ⚠️ 반드시 s.DisplayMode를 갱신하기 **전에** 호출한다(창은 아직 이전 모드로 떠 있다).
    _shell.RequestCaptureWindowBounds();

    var s = _settings.Current;
    ...
}
```

**순서가 계약이다**: 캡처 → VM 필드를 `s`에 복사(`s.DisplayMode` 갱신 포함) → `Save()` → `LoadSettings()` →
`RequestApplyDisplayMode()`. 캡처가 `s.DisplayMode` 갱신보다 뒤로 가면, 창모드→전체화면 저장 시
`_appliedMode`(=Windowed)와 새 설정이 어긋난 채 캡처되어 **직전 창 위치를 잃는다**. 이 순서는 §8.3 테스트로 고정한다.

- 저장 실패(ini 쓰기 불가) 시에도 캡처는 이미 수행됐다 → 메모리상 `WindowBounds`만 최신화되고 파일은 그대로.
  다음 저장·종료 시 반영되므로 무해하다(사용자에게 보이는 변화 없음).
- 게스트도 캡처된다(창 기하는 권한 게이트 대상이 아니다 — 현행 `OnClosing`도 로그인 여부를 보지 않는다).

### 7.6 검증 가능성과 수동 확인 항목

| 대상 | 검증 방법 |
|------|-----------|
| `DisplayApplyPolicy.Decide` 6가지 조합 | 순수 단위 테스트(§8.3) — `Window` 불필요 |
| 저장 시 셸 이벤트 발화 횟수·순서 | `AppShellViewModel` 이벤트 구독으로 관측(§8.3, F30) |
| `CaptureWindowBounds`·`ApplyDisplaySettings` 본문 | **단위 테스트 불가**(`Window` 인스턴스 필요) → 로직을 위 두 순수 지점으로 최대한 밀어냈다 |
| 실제 창 거동 | **사용자 수동 확인**(앱 실행 금지 제약, 가정 A5): ① 창모드에서 창을 옮긴 뒤 설정 저장 → 창이 움직이지 않는다 ② 창모드 → 전체화면 저장 → 즉시 전체화면 ③ 전체화면 → 창모드 저장 → 직전 위치·크기로 복귀 ④ 앱 재시작 → ①의 위치가 유지된다 |

---

## §8 테스트 계획

### 8.1 갱신이 **반드시 필요한** 기존 테스트 (역할 추가로 깨진다 — 의도된 변경)

| 파일:줄 | 테스트 | 왜 깨지는가 | 조치 |
|---------|--------|-------------|------|
| `RoleManagementTests.cs:67-74` | `CreatableRoles_Includes_TempUser_For_Power` | 배열 동등성 — admin/manager 목록에 `AdvancedUser` 추가 | 기대 배열 갱신(§3.6) |
| `RoleManagementTests.cs:86-94` | `AssignableRoles_Admin_Any_NonAdmin_Target_All_Except_Admin` | `all` 배열에 `AdvancedUser` 추가 | `all = [TempUser, User, AdvancedUser, Manager]` + `current=AdvancedUser` 행 추가 |
| `RoleManagementTests.cs:96-102` | `AssignableRoles_Manager_User_Target_Only_Demote_To_TempUser` | manager 결과가 `[User, TempUser]` → `[TempUser, User, AdvancedUser]`(오름차순) | 테스트명·기대값 교체(예: `..._Manager_Lower_Band_Free_Assign`) |
| `RoleManagementTests.cs:104-111` | `AssignableRoles_Empty_Cases` 중 `[Manager, TempUser]` | manager가 temp_user 대상도 지정 가능해져 **빈 목록이 아니다** | 해당 `InlineData` 제거 후 `[Manager, TempUser]`를 비어있지 않음 단정으로 이동 |
| `UserMgmtViewModelTests.cs:103-106` | `Admin_Rows_Offer_All_Except_Admin_And_Self` | `all` 배열 | 갱신 |
| `UserMgmtViewModelTests.cs:120-121` | `Manager_Rows_Only_User_Target_Offers_Demote` | manager가 `t1`(temp_user) 행도 콤보 노출 → `CanChangeRole` **true** | 테스트명·단정 교체 |
| `web/.../roles.test.ts:42-65` | `creatableRoles`·`canCreate` | 배열/판정 | 갱신 |
| `web/.../roles.test.ts` `canSetRole` 블록 | manager 관련 케이스 | 승격 허용으로 반전 | §3.3 표대로 갱신 |
| `web/.../accounts.test.ts:132-167` | `it13 setRole` 매트릭스 | `manager가 temp_user→user 승격 거부(403)`·`manager가 temp_user 대상 변경 거부` 등이 **허용**으로 반전 | §3.3 표대로 갱신(반전 케이스는 성공 단정으로 교체하고, 거부 케이스는 `→manager`·`manager/admin 대상`으로 재구성) |
| `web/.../validation.test.ts:29-32` | `validateRole` | (통과하지만) 새 값 커버리지 없음 | `validateRole("advanced_user").ok === true` 추가 |
| `FrameEditPolicyTests.cs:28-45` | `User_Can_Edit_Own_Local` 등 | **`User_Can_Edit_Own_Local`이 false로 반전**(E4) | `AdvancedUser_Can_Edit_Own_Local`로 이관 + `User_Cannot_Edit_Own_Local` 추가 |
| `FrameSelectViewModelTests.cs` | 삭제·편집 노출 케이스 | `CanDeleteFrames`·`CanEditSelected`가 역할에 의존하게 됨 | 역할별 케이스로 확장(§8.2) |

> **테스트 수는 늘어야 정상이다.** 줄어들면 사유를 보고한다(무회귀 기준 §0.3).

### 8.2 새로 추가할 C# 테스트

**(a) 역할 위계 — `RoleManagementTests.cs`**

1. `ToFirestoreValue`/`ParseRole` 라운드트립: `AdvancedUser ↔ "advanced_user"`.
2. `ParseRole("advanced_user")` == `AdvancedUser`, `ParseRole("advanceduser")` == `User`(폴백 유지).
3. `ToLabel(AdvancedUser)` == `"고급 유저"`.
4. `AdvancedUser.IsPower()` == **false** (회귀 방지 — power 축 오염 금지).
5. `CanWriteFrames()` 전 역할 표: TempUser·User=false / AdvancedUser·Manager·Admin=true.
6. `CanManage` 확장 행: `(AdvancedUser, User)`=true, `(User, AdvancedUser)`=false, `(Manager, AdvancedUser)`=true,
   `(AdvancedUser, AdvancedUser)`=true, `(AdvancedUser, Manager)`=false.
7. **`AssignableRoles` 전수 표 테스트**(§3.3의 25행을 `[Theory]` InlineData로 그대로 옮긴다):
   `actor × current × target` → 기대 bool. 표와 코드가 1:1임을 기계적으로 보장한다.
8. `RoleActionVis_Manage`에 AdvancedUser 행 추가.

**(b) 프레임 권한 — `FrameEditPolicyTests.cs`**

9. `CanEdit`: TempUser·User는 **본인 로컬도 false**(E4 핵심).
10. `CanEdit`: AdvancedUser는 본인 로컬 true / 타인 로컬 false / DbDefault false / 번들·fallback false
    (= it15 User 케이스 전량 이관).
11. `CanEdit`: power는 현행과 동일(DbDefault true, 본인 로컬 true, 번들 false).
12. `CanDelete` 매트릭스(§4.4 표 그대로): 게스트/temp_user/user 전부 false, AdvancedUser 로컬 true·DB false,
    power 로컬·DB true, 번들·fallback·빈 Id false.
13. `CanDelete`: `UserId=null`인 **공용 로컬** 프레임(power fork 산출물, F18)에 대해 power=true —
    소유자 판정으로 회귀하지 않았음을 고정한다.
14. `RequiresFork`는 역할 무관 유지(기존 테스트 그대로 통과 확인).

**(c) 화면 게이트 — `FrameSelectViewModelTests.cs`**

15. 역할별 `CanCreateFrame`: 게스트 false / TempUser false / User false / AdvancedUser true / Manager true / Admin true.
16. 역할별 `CanDeleteFrames`: 동일 표.
17. `CanEditSelected`: User 로그인 + 본인 로컬 프레임 선택 → **false**(버튼 미노출).
18. `RequestDelete(frame)`를 User 세션에서 직접 호출 → `IsDeleteConfirmVisible` **false**(커맨드 가드).
19. `CreateFrame()`를 User 세션에서 직접 호출 → 편집기로 전이하지 않음(`CurrentState` 불변).
20. **E4 목록 노출**: User 세션에서 `OnEnterAsync` 후 본인 로컬 프레임이 `Frames`에 **포함**된다(숨기지 않는다).

**(d) 편집기 — `FrameEditorViewModelTests.cs`**

21. User 세션에서 `SaveCommand` 실행 → 저장 미수행 + `StatusMessage`에 권한 안내(fail-closed, #24).
22. AdvancedUser 세션 저장 → **it15 User와 동일**하게 개인 스코프 저장(`ownerName={계정}`)이고 DB 미호출.
23. AdvancedUser의 `SaveScopeNotice`가 비power 문구("내 프레임으로 이 PC에 저장합니다")임을 확인.

**(e) 사용자 관리 — `UserMgmtViewModelTests.cs`**

24. manager actor + temp_user 행 → `CanChangeRole` true, 콤보 = `[TempUser, User, AdvancedUser]`.
25. manager actor + advanced_user 행 → 동일 콤보. manager 행·admin 행 → `CanChangeRole` false.
26. `CanResetPin`: AdvancedUser actor(가정상 화면 도달 시) → **false**(power 항 추가 검증, 가정 A6).
27. `ApplyRoleChange`로 `AdvancedUser` 지정 → `SetRoleAsync(id, AdvancedUser)` 호출 + 성공 토스트에 "고급 유저".

### 8.3 새로 추가할 창 위치 테스트 (`SettingsViewModelTests.cs` + 신규 `DisplayApplyPolicyTests.cs`)

28. `DisplayApplyPolicy.Decide` 6조합(§7.3 결정 표)을 `[Theory]`로 전수 검증.
29. `Decide(Windowed, Windowed)` == `None` — **버그 회귀 방지 단정**(이름에 의도를 남긴다).
30. `SaveSettings()` 성공 시 셸의 `WindowBoundsCaptureRequested`와 `DisplayModeApplyRequested`가 **각각 1회** 발화한다.
31. **순서 계약**: `WindowBoundsCaptureRequested` 핸들러 안에서 관측한 `_settings.Current.DisplayMode`가
    **저장 전 값**이다(= 캡처가 필드 복사보다 먼저다). VM의 `DisplayMode`를 반대 값으로 바꿔 두고 검증한다.
32. 저장 실패(쓰기 불가 경로) 시 `DisplayModeApplyRequested`는 발화하지 않는다(현행 동작 유지) —
    `WindowBoundsCaptureRequested`는 발화한다(무해, §7.5).

### 8.4 새로 추가할 서버 테스트

33. `roles.test.ts`: `isUserRole("advanced_user")` true / `parseRole` 라운드트립 / `isPower("advanced_user")` **false** /
    `canManage` 확장 행(advanced_user 랭크 2 검증).
34. `roles.test.ts`: **`canSetRole` 전수 표**(§3.3의 25조합 × 5 target을 `test.each`로) — C# 테스트 7과 대칭.
35. `accounts.test.ts`: manager actor로 `setRole` 승격 성공 케이스(`temp_user→advanced_user`, `user→advanced_user`),
    거부 케이스(`advanced_user→manager`, `manager` 대상), `advanced_user` 대상 강등 성공(`advanced_user→user`).
36. **신규 `authGates.test.ts`**(`optionalBearer.test.ts` 패턴, F32):
    `requirePower()`에 principal role별 주입 → temp_user·user·**advanced_user** 403 / manager·admin 통과 /
    principal 없음 401. → §5.2가 주장하는 "프레임 쓰기 서버 거부"의 실측 근거.
37. **구조 검증(grep 게이트, Step S3 완료 기준에 포함)**: `routes/frames.ts`에 `requirePower()` **3회**,
    `routes/accounts.ts`에 `requirePower()` **4회**(list·delete·role·**pin**) 존재.
38. `accounts.test.ts`: `resetOtherPin`은 서비스 레벨이라 power 게이트를 보지 않는다 →
    **서비스 단정은 그대로 두고**(canManage 계약 불변) 라우트 게이트는 36·37로 검증한다(중복 없이 역할 분리).

### 8.5 flake 방지

- 새 테스트는 **시간·파일시스템·네트워크에 의존하지 않는다**(전부 순수 함수 또는 in-memory VM).
- `FrameSelectViewModelTests`·`FrameEditorViewModelTests`가 임시 폴더를 쓰는 기존 패턴을 따르며
  테스트마다 GUID 폴더를 사용한다(현행 관례 유지).
- 최종 확인: `dotnet test` **5회 연속** 무실패(it15에서 flake 2건이 있었다).

---

## §9 구현 WBS

### 9.0 공통 전제 (모든 단계 공통 — 읽지 않고 시작하지 말 것)

- 루트: `C:\STUDY\PROJECT\PhotoBooth`. 솔루션 `MCPhoto.sln`. 서버 `web/functions`.
- **인코딩**: 기존·신규 `.cs`/`.xaml`/`.ts` 모두 **UTF-8 no BOM** 유지.
  검증: `head -c 3 <file> | od -An -tx1`이 `ef bb bf`가 아니어야 한다(`.claude/agent-memory/wpf-developer/encoding-verify-method.md`).
  개행은 `core.autocrlf=true`로 워킹카피 CRLF가 정상 — `git diff`가 실제 변경 줄만 보이면 통과.
- **git commit 금지**(부모가 그룹별로 커밋한다). `bldinfo.ini` 수정·언급 금지. 앱 실행(UI 기동) 금지.
- ⚠️ `.claude/agent-memory/wpf-developer/mcphoto-solution.md`는 **일부 낡았다**: 루트 경로 `E:\Study\photobooth`,
  `MCPhoto.Firebase` 프로젝트, `AppSettings.UseBackend`는 **현재 존재하지 않는다**(it15에서 제거).
  경로·구조는 **현행 코드**를 따른다.
- 검증 명령(클라):
  `dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q` → 경고 0·오류 0
  `dotnet test MCPhoto.sln -c Debug --nologo` → 전량 통과(613 이상)
- 검증 명령(서버, cwd=`web/functions`):
  `npm run typecheck` → 오류 0 / `npm test` → 전량 통과(219 이상) / `npm run lint` → 오류 0

### 9.1 그룹 요약 (부모의 기능별 커밋 단위)

| 그룹 | 단계 | 독립 빌드·테스트 통과 | 파일 교집합 |
|------|------|:-:|---|
| **G1 역할 위계** | S1, S2 (서버) / C1, C2 (클라) | ○ | 없음 |
| **G2 프레임 권한** | S3 (서버, 테스트만) / C3, C4 (클라) | ○ (단 C3·C4는 C1의 `CanWriteFrames` 필요 → **G1 이후**) | `UserRole.cs`만 G1과 공유(C1에서 이미 확정) |
| **G3 창 위치 버그** | W1, W2, W3 (클라) | ○ (G1·G2와 완전 독립) | 없음 |
| 공통 | D1 (문서) | ○ (코드 무변경) | – |

- **서버 ↔ 클라는 §5.1 동결표만 공유**하므로 완전 병렬이다. 상호 대기 없음.
- **G3는 언제든 단독 진행·단독 커밋 가능**하다(역할 작업과 파일이 겹치지 않는다).
- G2는 G1의 `CanWriteFrames`(C1)에 의존한다 → 커밋은 나눠도 **작업 순서는 C1 → C3 → C4**.
- 배포는 **서버 먼저**(§3.7).

---

### Step S1: 서버 역할 위계에 `advanced_user` 도입 + E3 매트릭스 (G1)

- **Context Brief**: 서버(`web/functions`, TS/Express)는 클라 UI와 무관하게 역할 인가를 재검증한다.
  현재 역할은 `temp_user/user/manager/admin` 4종이며 위계는 `MANAGE_RANK`(서수 아님), 역할 변경 판정은
  `canSetRole`이다. 여기에 `advanced_user`(위계 2위, temp_user·user보다 높고 manager보다 낮음)를 추가하고,
  "manager는 하위 3역할 대역(temp_user·user·advanced_user)을 자유 지정, manager·admin 지정은 admin 전용"으로
  매트릭스를 교체한다. `isPower`(manager+admin)는 **절대 확장하지 않는다** — advanced_user는 power가 아니다.
- **대상 파일**:
  - `web/functions/src/domain/roles.ts`
  - `web/functions/src/domain/validation.ts` (실패 문구만)
  - `web/functions/src/__tests__/roles.test.ts`
  - `web/functions/src/__tests__/validation.test.ts`
  - `web/functions/src/__tests__/accounts.test.ts` (setRole 매트릭스 케이스)
- **선행 조건**: 없음.
- **구현 내용**:
  1. 착수 전 `rg -n "temp_user|'user'|\"manager\"" firestore.rules firestore.indexes.json 2>/dev/null` 로
     역할 문자열 하드코딩 유무 확인(가정 A4). 있으면 그 파일도 대상에 추가하고 부모에 보고.
  2. `UserRole` union에 `"advanced_user"` 추가.
  3. `MANAGE_RANK` = `{temp_user:0, user:1, advanced_user:2, manager:3, admin:4}`.
  4. `isUserRole`에 `"advanced_user"` 추가, `parseRole`에 `case "advanced_user"` 추가(폴백은 `user` 유지).
  5. `creatableRoles`: admin→`["temp_user","user","advanced_user","manager"]`, manager→`["temp_user","user","advanced_user"]`.
  6. `canSetRole`을 설계 §5.3 코드 형태로 교체(`LOWER_BAND` 상수 사용). **`isPower`·`canManage`는 손대지 않는다.**
  7. `validateRole` 실패 문구에 `advanced_user` 추가(로직 불변).
  8. 테스트 갱신·추가: 설계 §8.1(해당 4파일) + §8.4의 33·34·35.
- **검증 명령**:
  ```
  cd web/functions && npm run typecheck && npm run lint && npm test
  rg -n "advanced_user" src/domain/roles.ts | wc -l        # 4 이상(union·rank·isUserRole·parseRole·creatable)
  rg -n "advanced_user" src/domain/roles.ts | rg "isPower" # 무매치여야 한다
  ```
- **완료 기준**:
  - [관측] `npm test` 전량 통과(219 초과), `typecheck`·`lint` 오류 0. `canSetRole` 전수 표 테스트(§8.4-34)가 §3.3 표와 1:1로 통과.
  - [non-goal] `isPower`에 `advanced_user`가 포함되지 않는다(grep 무매치). `canManage` 시그니처·본문 불변.
    `routes/frames.ts`·`http/auth.ts` 파일이 **변경되지 않는다**(`git diff --name-only`에 등장 금지).
  - [trigger] 역할 변경은 여전히 `PATCH /accounts/:id/role` 요청 시에만 발생하며, actor는 JWT의 role에서만 도출된다.
- **롤백**: 이 단계의 변경만 `git checkout -- web/functions/src`(다른 그룹 파일을 건드리지 않았으므로 안전).
- [ ] 완료

---

### Step S2: `PUT /accounts/:id/pin`에 power 게이트 추가 + 미들웨어 회귀 테스트 (G1)

- **Context Brief**: 타 계정 PIN 재설정 라우트만 `requirePower()`가 빠져 있어, 로그인만 하면 `canManage`
  (자신과 **같거나** 낮은 위계 허용)로 통과한다 → `temp_user`가 다른 `temp_user`의 PIN을 재설정할 수 있다.
  it15에서 신규 SSO 계정이 전원 temp_user가 되며 이 모집단이 커졌다. 형제 라우트
  (`DELETE /accounts/:id`, `PATCH /accounts/:id/role`)는 이미 `requirePower()`를 갖는다.
  **`canManage`의 의미는 바꾸지 않는다**(계정 삭제와 공유되며, 좁히면 admin↔admin·manager↔manager 삭제가 회귀한다).
- **대상 파일**:
  - `web/functions/src/routes/accounts.ts`
  - `web/functions/src/__tests__/authGates.test.ts` (**신규**)
- **선행 조건**: 없음(S1과 병렬 가능. 단 `authGates.test.ts`에서 `advanced_user` principal을 쓰려면 S1 선행 —
  S1 미완이면 해당 케이스만 마지막에 추가한다).
- **구현 내용**:
  1. `router.put("/:id/pin", requirePower(), asyncHandler(...))` — 기존 핸들러 본문·자기자신 400 분기·`resetOtherPin` 호출 유지.
  2. 주석 갱신: `// PUT /accounts/{id}/pin  (파워, 위계) — {newPin} → 타 계정 PIN 재설정(E3).`
  3. 신규 `authGates.test.ts`: `optionalBearer.test.ts`의 Request/next 모킹 패턴으로
     `requirePower()`를 역할별 검증(temp_user·user·**advanced_user** → 403, manager·admin → next() 통과,
     principal 없음 → 401) + `requireAdmin()`(admin만 통과) 회귀.
- **검증 명령**:
  ```
  cd web/functions && npm run typecheck && npm test
  rg -c "requirePower\(\)" src/routes/accounts.ts    # 4 (list, delete, role, pin)
  rg -c "requirePower\(\)" src/routes/frames.ts      # 3 (post, put, delete) — 이 단계에서 변하지 않아야 한다
  ```
- **완료 기준**:
  - [관측] `accounts.ts`의 `requirePower()` 호출이 4회이고 `authGates.test.ts`가 전 역할에 대해 통과한다.
  - [non-goal] `services/accounts.ts`의 `resetOtherPin`·`canManage` 본문이 불변이다. 본인 PIN 경로
    `PUT /accounts/me/pin`과 `POST /accounts/me/pin/verify`에는 power 게이트가 붙지 않는다(비power도 본인 PIN 설정 가능 — E2 유지).
  - [trigger] 403은 **비power 주체가 타 계정 PIN 재설정을 요청할 때만** 발생한다.
- **롤백**: `git checkout -- web/functions/src/routes/accounts.ts` + 신규 테스트 파일 삭제.
- [ ] 완료

---

### Step S3: 프레임 쓰기의 서버 거부를 회귀로 고정 (G2, 서버 코드 변경 0)

- **Context Brief**: it16은 user·temp_user의 프레임 생성·편집·삭제 권한을 제거한다. 서버 측에서는
  **이미 강제되고 있다** — `POST /frames`·`PUT /frames/:id`·`DELETE /frames/:id`가 모두 `requirePower()`
  뒤에 있고(manager/admin만 통과), advanced_user도 power가 아니므로 403이다. AdvancedUser의 프레임은
  개인 로컬 저장뿐이라 서버 쓰기 요청이 발생하지 않는다. 따라서 **새 미들웨어를 만들지 않고**,
  이 성질이 미래에 깨지지 않도록 테스트로 못 박는 것이 이 단계의 전부다.
- **대상 파일**: `web/functions/src/__tests__/authGates.test.ts` (S2에서 만든 파일에 프레임 관점 케이스·주석 추가)
- **선행 조건**: Step S2(파일 생성), Step S1(`advanced_user` 타입).
- **구현 내용**:
  1. `authGates.test.ts`에 describe 블록 추가: "프레임 쓰기 라우트 권한(it16 §5.2)" —
     `requirePower()`가 `advanced_user`를 403으로 막는다는 단정에 **"프레임 생성·수정·삭제는 power 전용"** 의도를 명시.
  2. 구조 회귀 단정: `frames.ts` 소스를 읽어 `requirePower()` 등장 3회를 확인하는 테스트를 추가한다
     (`fs.readFileSync(path.join(__dirname, "../routes/frames.ts"), "utf8")`의 매치 수 검사).
     — 라우터를 Express로 띄우지 않고도 게이트 제거를 잡아낸다.
- **검증 명령**:
  ```
  cd web/functions && npm test -- authGates
  git diff --name-only -- src/routes src/domain src/services   # 이 단계에서 비어 있어야 한다
  ```
- **완료 기준**:
  - [관측] `authGates` 스위트가 통과하고, `advanced_user`/`user`/`temp_user`에 대해 프레임 쓰기 게이트가 403임을 단정한다.
    `frames.ts`의 `requirePower()` 3회 구조 단정이 통과한다.
  - [non-goal] `routes/frames.ts`·`services/frames.ts`·`http/auth.ts`에 **어떤 변경도 없다**(git diff 비어 있음).
    새 미들웨어를 도입하지 않는다.
  - [trigger] 없음(테스트 전용 단계 — 런타임 동작 변화 0).
- **롤백**: 추가한 describe 블록 제거.
- [ ] 완료

---

### Step C1: `UserRole`에 `AdvancedUser` 추가 + 위계·매트릭스·`CanWriteFrames` (G1)

- **Context Brief**: C# 역할 enum은 `TempUser=0, User=1, Manager=2, Admin=3`이고 **위계 비교는 서수가 아니라
  `ManageRank` switch**가 담당한다(it13 결정). 저장·전송은 전부 `ToFirestoreValue()` 문자열이므로 배치값 변경이
  데이터에 영향을 주지 않는다. 여기에 `AdvancedUser`를 **위계 순 위치(값 2)** 로 끼워 넣고 Manager·Admin을 3·4로
  민다. 동시에 "프레임 쓰기 권한"이라는 **새 축** `CanWriteFrames()`를 추가한다(IsPower와 별개 — 절대 혼용 금지).
  역할 변경 매트릭스(`RoleChangePolicy`)는 서버 `canSetRole`과 1:1이어야 한다.
- **대상 파일**:
  - `src/MCPhoto.Core/Models/UserRole.cs`
  - `src/MCPhoto.Core/Models/RoleChangePolicy.cs`
  - `tests/MCPhoto.Tests/RoleManagementTests.cs`
  - `src/MCPhoto.App/Converters/CommonConverters.cs` (`RoleLabelConverter` **주석의 라벨 열거만**)
- **선행 조건**: 없음. (§5.1 동결표만 참조 — 서버 완료를 기다리지 않는다.)
- **구현 내용**:
  1. **안전 게이트(먼저 실행)**: 아래 grep이 무매치임을 확인한다. 매치가 있으면 재배치를 포기하고
     `AdvancedUser = 4`(append)로 전환하고 그 사실을 부모에 보고한다.
     ```
     rg -n "\(int\)\s*\w*[Rr]ole|Role\.CompareTo|\bRole\s*[<>]=?" src/ tests/
     ```
  2. `UserRole` enum: `TempUser=0, User=1, AdvancedUser=2, Manager=3, Admin=4`. XML 주석에
     "AdvancedUser = User 권한 + 프레임 생성·편집·삭제(개인 로컬)" 명시.
  3. `ToFirestoreValue`: `AdvancedUser => "advanced_user"`. `ParseRole`: `"advanced_user" => AdvancedUser`(폴백 `User` 유지).
  4. `ToLabel`: `AdvancedUser => "고급 유저"`(폴백 `"사용자"` 유지).
  5. `ManageRank`: `AdvancedUser => 2, Manager => 3, Admin => 4`.
  6. `CreatableRoles`: §3.6 목록.
  7. **`CanWriteFrames()` 추가** — 본문·주석은 설계 §4.2 코드 그대로(명시 열거, `ManageRank` 부등식 금지).
  8. `IsPower()`·`CanManage()`는 **변경 없음**.
  9. `RoleChangePolicy.AssignableRoles`를 §3.4 규칙으로 교체(반환은 위계 오름차순).
  10. 테스트: §8.1의 `RoleManagementTests` 4건 갱신 + §8.2의 1~8 추가(특히 **7의 전수 표 Theory**).
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo
  rg -n "AdvancedUser" src/MCPhoto.Core/Models/UserRole.cs        # enum·3개 매핑·CanWriteFrames = 5+ 매치
  rg -n "IsPower" src/MCPhoto.Core/Models/UserRole.cs             # 본문에 AdvancedUser 없음을 눈으로 확인
  head -c 3 src/MCPhoto.Core/Models/UserRole.cs | od -An -tx1     # ef bb bf 아님
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0·오류 0, `dotnet test` 전량 통과(613 초과). `AssignableRoles` 전수 표 Theory가 §3.3과 1:1 통과.
    `ToLabel(AdvancedUser)=="고급 유저"`, `ParseRole("advanced_user")==AdvancedUser` 라운드트립 통과.
  - [non-goal] `AdvancedUser.IsPower()`가 **false**(테스트로 고정). `CanManage` 결과 표가 기존 16조합에서 불변.
    프레임 관련 파일(`FrameEditPolicy.cs`·`FrameSelect*`·`FrameEditor*`)은 이 단계에서 **변경하지 않는다**.
  - [trigger] 역할 승격은 사용자 관리 화면의 "변경" 버튼(→`SetRoleAsync`)에서만 발생한다. 이 단계는 로직만 바꾸고
    새 진입점을 만들지 않는다.
- **롤백**: `git checkout -- src/MCPhoto.Core/Models tests/MCPhoto.Tests/RoleManagementTests.cs`
- [ ] 완료

---

### Step C2: 사용자 관리 — PIN 재설정 power 게이트 + 새 매트릭스 UI 반영 확인 (G1)

- **Context Brief**: 사용자 관리 화면(power 전용)에서 각 행의 역할 콤보는 `RoleChangePolicy.AssignableRoles`로
  필터되고, PIN 재설정 버튼은 `CanResetPin`(현재 `!isSelf && actorRole.CanManage(target)`)으로 노출된다.
  서버가 이번에 PIN 재설정 라우트에 power 게이트를 추가하므로(Step S2) 클라 판정도 대칭으로 맞춘다.
  콤보·라벨은 Step C1의 변경으로 자동 반영되므로 **테스트로 확인만** 한다(XAML 변경 없음).
- **대상 파일**:
  - `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`
  - `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs`
- **선행 조건**: Step C1(`AdvancedUser`·새 `AssignableRoles`).
- **구현 내용**:
  1. `UserRowViewModel.CanResetPin = !isSelf && actorRole.IsPower() && actorRole.CanManage(user.Role);`
  2. `ResetUserPin` 커맨드 가드에 `IsPower()` 항 추가:
     `if (!ActorRole.IsPower() || !ActorRole.CanManage(user.Role)) { StatusMessage = "상위 역할 계정은 관리할 수 없습니다."; return; }`
     (문구는 기존 것 재사용 — 새 리소스·새 문구를 만들지 않는다.)
  3. `DeleteUser` 가드는 **변경하지 않는다**(`CanManage`만 — 삭제 라우트는 종전대로 power+위계).
  4. 테스트: §8.1의 `UserMgmtViewModelTests` 2건 갱신 + §8.2의 24~27 추가.
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~UserMgmtViewModelTests"
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  git diff --name-only -- src/MCPhoto.App/Views                  # 비어 있어야 한다(XAML 무변경)
  ```
- **완료 기준**:
  - [관측] manager actor에서 temp_user·advanced_user 행의 콤보가 `[TempUser, User, AdvancedUser]`로 노출되고,
    manager·admin 행은 `CanChangeRole=false`. `AdvancedUser` 지정 시 `SetRoleAsync(id, AdvancedUser)` 호출 + 토스트에 "고급 유저".
  - [non-goal] 삭제 버튼 노출 규칙(`RoleActionVis`/`CanManage`)이 **불변**이다(admin→다른 admin 행에 삭제 버튼 유지).
    `UserMgmtView.xaml`은 변경되지 않는다.
  - [trigger] 역할 변경은 행의 "변경" 버튼 클릭 시에만 서버로 전송된다(콤보 선택만으로는 아무 일도 없다).
    PIN 재설정은 "PIN 재설정" 버튼 → 다이얼로그 2회 입력 완료 시에만 수행된다.
- **롤백**: `git checkout -- src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs tests/MCPhoto.Tests/UserMgmtViewModelTests.cs`
- [ ] 완료

---

### Step C3: 프레임 권한 정책 — `CanEdit` 쓰기 게이트 + `CanDelete` 신설 (G2)

- **Context Brief**: 프레임 편집 권한은 순수 함수 `FrameEditPolicy.CanEdit(frame, role, userId)`에 있고
  현재 "로그인 계정이면 본인 로컬은 무조건 편집 가능"이다. it16에서 user·temp_user는 프레임을 **사용만** 하도록
  바뀌므로(E4) 쓰기 권한 게이트를 정책 진입부에 넣는다. 삭제 판정은 지금 VM·컨버터에 흩어져 있고 커맨드 가드가
  컨버터보다 느슨하다 → `CanDelete`를 같은 정책 클래스에 신설해 한곳으로 모은다.
  ⚠️ `CanDelete`는 **소유자를 보지 않는다**: power가 fork 저장한 공용 프레임은 `UserId=null`로 로드되므로
  소유자 판정을 넣으면 기존 삭제 능력이 회귀한다(`LocalFrameStore.cs:112-128`).
- **대상 파일**:
  - `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`
  - `tests/MCPhoto.Tests/FrameEditPolicyTests.cs`
- **선행 조건**: Step C1(`CanWriteFrames`).
- **구현 내용**:
  1. `CanEdit`에 `if (!role.Value.CanWriteFrames()) return false;`를 게스트 체크 **직후** 삽입(설계 §4.3 코드 그대로).
  2. `CanDelete(FrameTemplate frame, UserRole? role)` 추가 — 설계 §4.4 코드·주석 그대로(소유자 미판정 이유 주석 필수).
  3. `RequiresFork`는 손대지 않는다.
  4. 테스트: §8.1의 `FrameEditPolicyTests` 반전 케이스 정리 + §8.2의 9~14 추가.
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameEditPolicyTests"
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  ```
- **완료 기준**:
  - [관측] `CanEdit(본인 로컬, User, 본인id) == false`, `CanEdit(본인 로컬, AdvancedUser, 본인id) == true`,
    `CanDelete(DbDefault, AdvancedUser) == false`, `CanDelete(local 공용(UserId=null), Manager) == true`가 통과.
  - [non-goal] power의 편집·삭제 가능 집합이 it15와 **완전히 동일**하다(DbDefault 편집 true, 번들·fallback false,
    타인 개인 로컬 편집 false). `RequiresFork`의 결과가 전 케이스에서 불변.
  - [trigger] 이 단계는 순수 함수만 바꾼다 — 화면 동작 변화는 Step C4에서 배선될 때 나타난다.
- **롤백**: `git checkout -- src/MCPhoto.Core/Frames/FrameEditPolicy.cs tests/MCPhoto.Tests/FrameEditPolicyTests.cs`
- [ ] 완료

---

### Step C4: 프레임 화면·편집기 게이트 배선 (G2)

- **Context Brief**: 프레임 선택 화면은 "프레임 만들기"를 `IsLoggedIn`으로만 노출하고, 삭제 ✕는
  `FrameDeleteVis` MultiBinding[`CanDeleteFrames`(=로그인 여부), `IsPower`, `Id`]로 판정한다.
  it16에서는 이 두 입력의 의미를 "프레임 쓰기 권한"으로 강화해 user·temp_user에게서 생성·편집·삭제 UI를 없앤다.
  **기존 프레임은 목록에 그대로 보이고 촬영에 사용 가능해야 한다(E4)** — 목록 로딩 코드는 건드리지 않는다.
  컨버터 코드와 MultiBinding 구조는 바꾸지 않는다(입력 의미만 강화).
- **대상 파일**:
  - `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`
  - `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`
  - `src/MCPhoto.App/Views/FrameSelectView.xaml` (버튼 1개의 Visibility 바인딩 + 주석)
  - `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`, `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`
- **선행 조건**: Step C3(`CanEdit` 게이트·`CanDelete`).
- **구현 내용**:
  1. `FrameSelectViewModel`: `[ObservableProperty] private bool _canCreateFrame;` 추가.
     `ReloadFramesAsync`에서 `CanCreateFrame = user is not null && user.Role.CanWriteFrames();`
     `CanDeleteFrames = user is not null && user.Role.CanWriteFrames();`(주석에 "쓰기 권한" 의미 명시).
     `IsPower`·`IsLoggedIn`·목록 로딩은 불변.
  2. `CreateFrame` 가드를 `if (!CanCreateFrame) return;`으로 교체.
  3. `RequestDelete` 가드를 `if (frame is null) return; var u = _shell.Session.CurrentUser;
     if (!FrameEditPolicy.CanDelete(frame, u?.Role) || !IsDeletable(frame)) return;`로 교체.
  4. `FrameSelectView.xaml:88`: `Visibility="{Binding CanCreateFrame, Converter={StaticResource BoolToVis}}"`.
     같은 줄 주변 주석을 "AdvancedUser 이상만 노출(it16)"로 갱신. **그 외 XAML 변경 금지.**
  5. `FrameEditorViewModel.Save()` 선두(로그인 확인 직후)에 fail-closed 가드:
     `if (!user.Role.CanWriteFrames()) { StatusMessage = "프레임을 만들 권한이 없습니다."; return; }`
  6. 테스트: §8.2의 15~23.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo
  rg -n "IsLoggedIn" src/MCPhoto.App/Views/FrameSelectView.xaml   # 무매치(버튼 바인딩 이전 완료)
  git diff --stat -- src/MCPhoto.App/Converters                    # 비어 있어야 한다
  ```
- **완료 기준**:
  - [관측] User 세션에서 `CanCreateFrame=false`·`CanDeleteFrames=false`·`CanEditSelected=false`이고,
    AdvancedUser 세션에서 셋 다 true. AdvancedUser 저장 결과가 개인 스코프(`{계정}_{이름}`)이며 DB 미호출.
  - [non-goal] User·TempUser 세션에서 **본인 기존 로컬 프레임이 `Frames` 목록에 그대로 존재**하고 `NextCommand`로
    촬영을 시작할 수 있다(E4 — 숨김·삭제 금지). `FrameDeleteVisibilityConverter`·`FrameEditorView.xaml`·
    `FramePickerViewModel`은 변경되지 않는다. power의 저장 스코프·DB 등록 동선이 it15와 동일.
  - [trigger] 편집기 진입은 "프레임 만들기"/"선택 편집" 버튼 클릭에서만 발생한다. 권한 없는 역할이
    커맨드를 직접 실행해도(테스트 18·19) 팝업이 열리지 않고 화면 전이가 없다.
- **롤백**: `git checkout -- src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs src/MCPhoto.App/Views/FrameSelectView.xaml` + 테스트 파일
- [ ] 완료

---

### Step W1: `DisplayApplyPolicy` 순수 정책 신설 (G3)

- **Context Brief**: 설정 화면에서 "저장"을 누르면 창모드 창이 옛 위치·크기로 점프하는 버그가 있다.
  원인은 `MainWindow.ApplyDisplaySettings()`가 ① 시작 시 창 복원과 ② 런타임 표시모드 적용을 겸하면서,
  런타임 적용 때도 `AppSettings.WindowBounds`(창을 **닫을 때만** 갱신되는 값)로 창 기하를 재적용하는 것이다.
  테스트에서 `Window`를 인스턴스화할 수 없으므로(헤드리스), "무엇을 할지" 판단을 순수 함수로 분리해
  단위 테스트 가능하게 만든다. 이 단계는 **정책과 테스트만** 만들고 아무도 호출하지 않는다(다음 단계에서 배선).
- **대상 파일**:
  - `src/MCPhoto.Core/Settings/DisplayApplyPolicy.cs` (**신규**)
  - `tests/MCPhoto.Tests/DisplayApplyPolicyTests.cs` (**신규**)
- **선행 조건**: 없음(G1·G2와 완전 독립).
- **구현 내용**:
  1. 설계 §7.3의 `DisplayApplyAction` enum + `DisplayApplyPolicy.Decide(DisplayMode target, DisplayMode? appliedMode)`를
     주석까지 그대로 작성한다. `MCPhoto.Core.Settings` 네임스페이스(같은 폴더의 `AppSettings.cs`와 동일).
  2. 테스트: §7.3 결정 표 6조합 `[Theory]` + §8.3의 29(`Decide(Windowed, Windowed)==None` 회귀 방지, 테스트명에
     "저장 시 창 위치 점프 방지" 의도를 남긴다).
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~DisplayApplyPolicyTests"
  head -c 3 src/MCPhoto.Core/Settings/DisplayApplyPolicy.cs | od -An -tx1   # ef bb bf 아님
  ```
- **완료 기준**:
  - [관측] 6조합 Theory 전량 통과. `Decide(Windowed, Windowed)`와 `Decide(Fullscreen, Fullscreen)`가 `None`,
    `Decide(Windowed, null)`이 `WindowedRestoreGeometry`, `Decide(Fullscreen, Windowed)`가 `Fullscreen`.
  - [non-goal] `MainWindow.xaml.cs`·`SettingsViewModel.cs`·`AppShellViewModel.cs`는 이 단계에서 **변경하지 않는다**
    (런타임 동작 변화 0 — 아직 호출자가 없다).
  - [trigger] 없음(순수 함수 추가 단계).
- **롤백**: 신규 두 파일 삭제.
- [ ] 완료

---

### Step W2: `MainWindow`가 정책을 사용하도록 개조 + 구독 해제 (G3, A안)

- **Context Brief**: `MainWindow.ApplyDisplaySettings()`는 ctor(시작)와 셸 이벤트(설정 저장 후) 두 곳에서 호출되며
  호출자를 구분하지 못한다. Step W1의 `DisplayApplyPolicy`로 "직전에 적용한 모드"를 기준으로 판단하게 바꿔,
  **모드가 같은 저장은 완전 무동작**(창 기하·상태 불변)으로 만든다. 전체화면 ↔ 창모드 전환은 지금처럼
  재시작 없이 즉시 반영돼야 한다(it9 후속 요구 — 절대 깨뜨리지 말 것).
  현재 `_shell.DisplayModeApplyRequested` 구독에 해제 경로가 없으므로 이번에 함께 닫는다.
- **대상 파일**: `src/MCPhoto.App/MainWindow.xaml.cs`
- **선행 조건**: Step W1(`DisplayApplyPolicy`).
- **구현 내용**:
  1. `private DisplayMode? _appliedMode;` 필드 추가.
  2. `ApplyDisplaySettings()`를 설계 §7.4 코드로 교체(switch 3분기, 성공 후 `_appliedMode = s.DisplayMode`).
     `WindowStartupLocation` 설정은 창모드 분기 안에 그대로 둔다(현행과 동일).
  3. `CaptureWindowBounds(AppSettings s)` private 메서드 추가(§7.4) — `_appliedMode`와 `WindowState`로 판정.
  4. `OnClosing`의 기하 저장 블록을 `CaptureWindowBounds(s)` 호출로 교체(그 뒤 `_ = _settings.Save();`는 유지).
  5. `OnClosing`에서 `_shell.Dispose()` **전에** `_shell.DisplayModeApplyRequested -= ApplyDisplaySettings;` 추가.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo
  rg -n "DisplayApplyPolicy|_appliedMode|-= ApplyDisplaySettings" src/MCPhoto.App/MainWindow.xaml.cs
  rg -n "WindowBounds" src/MCPhoto.App/MainWindow.xaml.cs   # 기하 접근이 Apply(복원)·Capture 두 곳에만
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0·오류 0, 전체 테스트 통과. `ApplyDisplaySettings`가 `DisplayApplyPolicy.Decide` 결과로 분기하고
    `None`에서 **즉시 return**하며, `OnClosing`에 `-=` 구독 해제가 존재한다.
  - [non-goal] 전체화면 ↔ 창모드 전환 코드 경로(스타일·ResizeMode·WindowState 설정과 창모드 기하 복원)는
    **모드가 바뀔 때 그대로 실행된다**(it9 후속 유지). `WindowBounds` 최소 크기 Clamp·ini 왕복 동작 불변.
  - [trigger] 창 기하 재적용은 **표시 모드가 실제로 달라지는 경우에만** 일어난다. 동일 모드 저장·반복 저장은
    창에 어떤 변화도 만들지 않는다. (실제 창 거동은 §7.6의 사용자 수동 확인 ①~④로 인계 — 앱 실행 금지)
- **롤백**: `git checkout -- src/MCPhoto.App/MainWindow.xaml.cs`(W1은 독립적으로 남아도 무해)
- [ ] 완료

---

### Step W3: 저장 직전 창 기하 캡처(B안) + 순서 계약 테스트 (G3)

- **Context Brief**: A안(Step W2)으로 동일 모드 저장의 점프는 사라지지만, `WindowBounds`가 여전히 "창을 닫을 때만"
  갱신되므로 `전체화면 → 창모드` 복귀 시에는 과거 위치로 돌아간다. 저장 직전에 현재 창 기하를 설정 객체에
  반영하면 그 복귀가 "사용자가 마지막에 두었던 자리"가 되고, ini에도 현재 위치가 남는다.
  `SettingsViewModel`은 `Window`를 알 수 없으므로 기존 `DisplayModeApplyRequested`와 동형의 셸 이벤트를 하나 추가한다.
  **호출 순서가 계약이다**: 캡처는 `s.DisplayMode`를 갱신하기 **전에** 일어나야 한다.
- **대상 파일**:
  - `src/MCPhoto.App/AppShellViewModel.cs` (이벤트 + 요청 메서드)
  - `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` (`SaveSettings` 첫 줄)
  - `src/MCPhoto.App/MainWindow.xaml.cs` (구독 + 해제)
  - `tests/MCPhoto.Tests/SettingsViewModelTests.cs`
- **선행 조건**: Step W2(`CaptureWindowBounds` 메서드).
- **구현 내용**:
  1. `AppShellViewModel`: `public event Action? WindowBoundsCaptureRequested;` +
     `public void RequestCaptureWindowBounds() => WindowBoundsCaptureRequested?.Invoke();`
     (기존 `DisplayModeApplyRequested` 바로 아래, 동일 스타일·주석).
  2. `MainWindow` ctor: `_shell.WindowBoundsCaptureRequested += OnCaptureWindowBounds;`
     여기서 `private void OnCaptureWindowBounds() => CaptureWindowBounds(_settings.Current);`
     `OnClosing`에서 `-=` 해제(W2에서 추가한 해제 블록에 한 줄 추가).
  3. `SettingsViewModel.SaveSettings()` **최상단**에 `_shell.RequestCaptureWindowBounds();` +
     설계 §7.5의 경고 주석(순서 이유) 삽입. 그 아래 기존 코드는 순서·내용 불변.
  4. 테스트: §8.3의 30·31·32.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~SettingsViewModelTests"
  dotnet test MCPhoto.sln -c Debug --nologo
  rg -n "RequestCaptureWindowBounds" src/MCPhoto.App | wc -l    # 3 (정의·발화·호출)
  rg -n "WindowBoundsCaptureRequested" src/MCPhoto.App          # 정의·구독·해제 3곳
  ```
- **완료 기준**:
  - [관측] 저장 성공 시 `WindowBoundsCaptureRequested`와 `DisplayModeApplyRequested`가 각각 1회 발화하고,
    캡처 핸들러 내부에서 관측한 `_settings.Current.DisplayMode`가 **저장 전 값**이다(순서 계약 테스트 31).
  - [non-goal] 저장 실패 시 `DisplayModeApplyRequested`는 발화하지 않는다(현행 동작 유지).
    저장되는 다른 설정 필드·게스트/TempUser 게이트(`!IsGuest`, `!IsTempUserBlocked` 가드)는 불변.
    새 이벤트에 대한 구독 해제가 존재한다(누수 없음).
  - [trigger] 기하 캡처는 **"저장" 버튼 클릭 시에만** 일어난다(설정 화면 진입·필드 편집·닫기에서는 발생하지 않는다).
    창이 창모드 + `WindowState.Normal`이 아니면 캡처는 아무 값도 쓰지 않는다.
- **롤백**: `git checkout -- src/MCPhoto.App/AppShellViewModel.cs src/MCPhoto.App/ViewModels/SettingsViewModel.cs src/MCPhoto.App/MainWindow.xaml.cs tests/MCPhoto.Tests/SettingsViewModelTests.cs`
  (W1·W2만 남아도 A안 단독으로 동작하며 버그는 이미 고쳐진 상태다)
- [ ] 완료

---

### Step D1: 문서 동기화 (공통, 코드 변경 0)

- **Context Brief**: 역할 1개 추가·프레임 권한 재배분·PIN 라우트 게이트는 분석 문서와 계약 문서에 역할 표로
  기재돼 있다. 코드와 문서가 어긋나면 다음 이터레이션이 잘못된 전제로 시작한다.
- **대상 파일**:
  - `docs/analysis/60-auth-accounts-and-roles.md` (역할 표·매트릭스·PIN 재설정 권한)
  - `docs/analysis/11-exe-app-features.md` (프레임 생성·편집 권한 서술)
  - `docs/analysis/40-database-firestore-and-storage-schema.md` (`users.role` 허용값)
  - `docs/design/firebase-contract.md` (역할 문자열·`PATCH /accounts/:id/role`·`PUT /accounts/:id/pin` 게이트)
  - `docs/analysis/90-roadmap-and-future-work.md` (§10.3 로드맵 항목 추가)
- **선행 조건**: Step S1·S2·C1~C4 완료(확정된 최종 동작을 기록).
- **구현 내용**: 각 문서의 역할 표에 `advanced_user`/"고급 유저" 행 추가, 프레임 권한 표를 §4.1로 교체,
  `PUT /accounts/:id/pin`을 "파워 전용"으로 정정, §10.3의 이연 항목을 로드맵에 추가.
  **문서만 수정한다** — 코드·테스트 변경 금지.
- **검증 명령**:
  ```
  rg -n "advanced_user|고급 유저" docs/analysis docs/design/firebase-contract.md | wc -l   # 5 이상
  git diff --name-only -- src tests web                                                   # 비어 있어야 한다
  ```
- **완료 기준**:
  - [관측] 위 5개 문서에 `advanced_user`(또는 "고급 유저") 서술이 존재하고, 프레임 권한 표가 §4.1과 일치한다.
  - [non-goal] `src/`·`tests/`·`web/`에 변경이 없다. `bldinfo.ini`는 건드리지 않는다.
  - [trigger] 없음(문서 단계).
- **롤백**: `git checkout -- docs/`
- [ ] 완료

### 9.5 커밋·배포 순서

| 순서 | 커밋 단위 | 포함 단계 | 비고 |
|---|---|---|---|
| 1 | 서버: 역할 위계 + PIN 게이트 | S1, S2, S3 | **먼저 배포**한다(구서버 + 신클라 조합은 400) |
| 2 | 클라: 역할 위계 | C1, C2 | 서버 배포 후 앱 배포 |
| 3 | 클라: 프레임 권한 | C3, C4 | C1 이후에만 작업 가능 |
| 4 | 클라: 창 위치 버그 | W1, W2, W3 | 위 3개와 완전 독립 — 언제든 단독 커밋 가능 |
| 5 | 문서 | D1 | 마지막 |

각 커밋 직전 `dotnet build -c Release --no-incremental`(경고 0) + `dotnet test`(전량) 통과를 확인한다.
클라 전체 완료 후 `dotnet test` **5회 연속** 무실패로 flake 0을 확인한다(§8.5).

### 9.6 완결성 게이트 (developer 전달 전 자체 검사 — 통과)

- [x] 검증된 사실(§1, F1~F33) / 미검증 가정(§2, A1~A6) 목록이 분리되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다 (A1→C1, A2→불필요(사유 기재), A3→C1, A4→S1, A5→W1+수동확인, A6→C2)
- [x] 11개 단계 전부 7개 필수 필드가 채워져 있다 (Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식이다 (UI 단계 C2·C4·W2·W3에 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능한 CLI다 (`dotnet build`/`dotnet test`/`npm test`/`rg`)

---

## §10 리스크와 이연 항목

### 10.1 리스크 표

| # | 리스크 | 영향 | 완화 |
|---|--------|------|------|
| R1 | enum 배치값 재배치가 미확인 서수 의존을 깨뜨린다 | 런타임 오동작(조용한 권한 오판) | Step C1의 **grep 게이트**(무매치 확인) + 매치 시 `AdvancedUser = 4` append로 전환. 근거 F1~F4로 사전 확률은 매우 낮다 |
| R2 | 기존 테스트가 대량 반전되어 **의도된 변경**과 **실제 회귀**를 구분하기 어렵다 | 회귀를 "예상된 실패"로 오인 | §8.1에 깨질 테스트를 **사전 확정 열거**했다. 실패 목록이 이 열거와 다르면 **회귀로 간주**하고 멈춘다(가정 A3 검증) |
| R3 | 서버·클라 배포 시차 | 신클라 + 구서버에서 역할 변경이 400 | §9.5 배포 순서(서버 먼저). 실패 시 UI는 기존 예외 경로로 "역할 변경에 실패했습니다"를 표시하고 목록을 원복한다(데이터 손상 없음) |
| R4 | 운영 DB에 이미 `advanced_user` 문서가 존재 | 없음 | `parseRole`/`ParseRole`이 정의되어 정상 처리된다. 반대로 배포 전이라면 그 값은 `user`로 폴백되어 fail-closed |
| R5 | **기존 `user` 계정이 프레임 생성·편집 권한을 잃는다**(설계 의도이지만 현장에서는 "기능이 사라졌다"로 체감) | 운영 문의 | 승격 동선을 운영자에게 안내해야 한다: 사용자 관리 → 해당 계정 역할 콤보 → **고급 유저** → 변경. manager도 승격할 수 있다(E3) |
| R6 | 창 기하 캡처가 `s.DisplayMode` 갱신보다 뒤로 가는 실수 | 창모드→전체화면 저장 시 직전 창 위치 상실 | §8.3 테스트 31(순서 계약)이 고정. 주석에도 경고를 남긴다 |
| R7 | A안으로 "동일 모드 저장 시 스타일 보정"이 사라진다 | 외부 요인으로 창 스타일이 변형된 경우 저장으로 복구 불가 | 실사례 없음(스타일을 바꾸는 다른 코드가 없다 — `ApplyDisplaySettings`가 유일 지점). 필요 시 앱 재시작으로 복구된다 |
| R8 | `CanDelete`가 소유자를 보지 않아 advanced_user가 **공용 로컬 프레임**(다른 power가 fork 저장한 것)을 지울 수 있다 | 공용 프레임 유실(로컬 파일만, 서버 문서는 불변) | **it15와 동일한 기존 성질**이다(당시 user도 가능했다). E2가 "AdvancedUser = it15 User 권한 전체"를 확정했으므로 이번에 좁히지 않는다 → §10.3 로드맵 |
| R9 | 테스트 flake(it15에서 2건) | 검증 신뢰도 | 새 테스트는 시간·파일·네트워크 비의존(§8.5) + 최종 5회 연속 실행 |

### 10.2 이 설계가 **의도적으로 하지 않는 것** (E6 준수)

- 역할별 프레임 개수 한도 차등, 프레임 소유권 이전·마이그레이션 UI, 승격 요청 워크플로우 → **만들지 않는다**.
- `requirePower` 외의 새 서버 미들웨어, 새 HTTP 엔드포인트, 새 Firestore 필드 → **만들지 않는다**.
- 새 컨버터·새 리소스 키·새 `Window`·새 다이얼로그 → **만들지 않는다**.
- `canManage` 의미 변경, 계정 삭제 권한 변경 → **하지 않는다**(§3.5 근거).
- 프레임 목록에서 권한 없는 계정의 기존 프레임 숨기기 → **하지 않는다**(E4가 노출 유지를 확정).

### 10.3 로드맵 이연 (Step D1에서 `docs/analysis/90-roadmap-and-future-work.md`에 기재)

| # | 항목 | 배경 |
|---|------|------|
| 1 | power가 fork 저장한 **공용 로컬 프레임을 다시 편집할 수 없다** | 공용 저장분은 `UserId=null`로 로드되어 `FrameEditPolicy.CanEdit`의 `UserLocal → IsOwnedLocal` 판정에서 탈락한다(F18). it15부터의 성질이며 이번 범위(역할 재배분)와 무관해 손대지 않았다 |
| 2 | 공용 로컬 프레임 **삭제를 power로 제한** | R8. 현재는 프레임 쓰기 권한이 있으면 공용 로컬 프레임을 지울 수 있다 |
| 3 | `CreatableRoles`/`canCreate` 데드코드 제거 | it15의 계정 생성 폐지로 프로덕션 호출자가 0(F6). 목록만 갱신해 드리프트를 막았다 |
| 4 | 서버 잔존 라우트 `PUT /frames/:id` 정리 여부 | it15 정책상 앱은 호출하지 않는다(운영/관리 전용으로 남김) |
| 5 | `MainWindow`의 표시모드·기하 책임을 서비스로 분리 | 현재는 코드비하인드에 남아 단위 테스트 불가 영역이 존재한다(§7.6). `IWindowGeometryService` 류 추상화는 별 이터레이션 과제 |
| 6 | 창 이동·리사이즈 시 실시간 `WindowBounds` 반영 | 현재는 "저장 시"와 "종료 시"에만 캡처한다. `LocationChanged`/`SizeChanged` 구독은 이벤트 해제 설계가 필요해 이번 범위 밖 |

---

## 부록 A. 변경 파일 요약

| 계층 | 파일 | 그룹 | 변경 |
|------|------|:-:|------|
| 서버 | `web/functions/src/domain/roles.ts` | G1 | 역할 추가·랭크·`canSetRole` 매트릭스 |
| 서버 | `web/functions/src/domain/validation.ts` | G1 | 실패 문구 |
| 서버 | `web/functions/src/routes/accounts.ts` | G1 | `PUT /:id/pin`에 `requirePower()` |
| 서버 | `web/functions/src/__tests__/{roles,validation,accounts}.test.ts` | G1 | 갱신 |
| 서버 | `web/functions/src/__tests__/authGates.test.ts` | G1·G2 | **신규** |
| 클라 | `src/MCPhoto.Core/Models/UserRole.cs` | G1 | enum·문자열·라벨·랭크·`CanWriteFrames` |
| 클라 | `src/MCPhoto.Core/Models/RoleChangePolicy.cs` | G1 | `AssignableRoles` 매트릭스 |
| 클라 | `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs` | G1 | `CanResetPin`·`ResetUserPin` power 항 |
| 클라 | `src/MCPhoto.App/Converters/CommonConverters.cs` | G1 | 주석만 |
| 클라 | `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` | G2 | `CanEdit` 게이트 + `CanDelete` 신설 |
| 클라 | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs` | G2 | `CanCreateFrame`·`CanDeleteFrames`·커맨드 가드 |
| 클라 | `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | G2 | `Save()` fail-closed 가드 |
| 클라 | `src/MCPhoto.App/Views/FrameSelectView.xaml` | G2 | 버튼 1개 Visibility 바인딩 |
| 클라 | `src/MCPhoto.Core/Settings/DisplayApplyPolicy.cs` | G3 | **신규**(순수 정책) |
| 클라 | `src/MCPhoto.App/MainWindow.xaml.cs` | G3 | 정책 적용·기하 캡처·구독 해제 |
| 클라 | `src/MCPhoto.App/AppShellViewModel.cs` | G3 | 캡처 요청 이벤트 |
| 클라 | `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | G3 | 저장 첫 줄 캡처 호출 |
| 테스트 | `tests/MCPhoto.Tests/{RoleManagement,UserMgmtViewModel,FrameEditPolicy,FrameSelectViewModel,FrameEditorViewModel,SettingsViewModel}Tests.cs` | 전 그룹 | 갱신·추가 |
| 테스트 | `tests/MCPhoto.Tests/DisplayApplyPolicyTests.cs` | G3 | **신규** |
| 문서 | `docs/analysis/{60,11,40,90}-*.md`, `docs/design/firebase-contract.md` | 공통 | 동기화 |

**변경하지 않는 파일(명시)**: `web/functions/src/routes/frames.ts`, `web/functions/src/http/auth.ts`,
`web/functions/src/services/{accounts,frames}.ts`, `src/MCPhoto.App/Views/{FrameEditorView,UserMgmtView}.xaml`,
`src/MCPhoto.App/ViewModels/FramePickerViewModel.cs`, `src/MCPhoto.Core/Frames/{FrameOrigin,FrameNaming,LocalFrameStore}.cs`,
`bldinfo.ini`.
