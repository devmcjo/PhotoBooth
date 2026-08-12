# it27 설계 — 앱 경로(`{exe}\Frame`) 사용 완전 제거 · `bundle:` 출처 범주 폐기

> 작성: wpf-architect · 2026-08-12
> 파이프라인: wpf-architect → wpf-developer → wpf-code-reviewer
> 상태: 설계 초안 (**rev2** — 전제 정정: **배포 이력 0**. 기존 설치 호환 장치의 근거가 소멸했고, 착수 전 블로킹 질문이 해소됐다 → §1.8·§2·§5.3)
> 선행 문서·이력: `wpf-it26-writable-paths-and-completion-popup-design.md`(직전 이터레이션 — **본 문서가 그 A-4·A-5 판정을 대체한다**, §0.2) · `wpf-frame-ownership-binding-design.md`(`.slots` v2 서명 포맷) · `wpf-it20-frame-download-waiting-design.md`(단일 비행·로컬 해석 우선순위) · `wpf-it15-frame-ux-design.md`(프레임 로컬 전용 정책)

## §0 개요

### 0.1 요구사항 원문 (사용자, 축약 금지)

> "앱경로에 있는 result/Frame 폴더는 이제 없어야 정상인거지? 사용하는 부분이 있어? **(없어야해. 완전한 이전)**"

승인:

> "설계 단계 거쳐서 4번까지 다 진행해"

전제 정정(사용자, rev2):

> "기존 설치 경로 app 경로에 있는 캐시들은 제거하지 않아도 돼. **아직 배포된 적이 없어.**"

**이 한 문장이 바꾼 것**: 현장에 설치된 인스턴스가 **0**이다(F25). 따라서 이 설계에서 "기존 설치를 다치지 않게" 하려던 장치와 그것을 확인하려던 질문이 **전부 사라진다**.

| rev1의 항목 | rev2 처분 |
|---|---|
| `legacyReadRoot`를 "기존 설치 호환용"으로 볼 여지 | **소멸** — 호환시킬 설치가 없다. 매개변수 제거 판정(A-1)의 근거가 하나 더 늘었다 |
| 인스톨러가 `{app}\Frame` 고아 캐시를 **정리해야 하는가** | **불요**(사용자 명시). 지시행은 회귀 방어로 남기고 **근거만 재규정**한다(§5.3) |
| it26 A-5의 근거("개인 프레임은 로컬 캐시가 유일 사본일 수 있다") | **성립하지 않는다** — 유일 사본을 가진 현장 PC가 존재하지 않는다(§0.2) |
| **UA-1**(착수 전 블로킹 질문: `.slots` 없는 png를 손으로 넣어 쓰는 운영 PC가 있는가) | **해소.** 운영 PC가 없다 → 블로킹 질문 0, 착수 가능(§11) |
| U2·U3(현장 자산 영향 가정) | **소멸**(§2) |

여기서 4번은 팀리드가 보고한 범위 목록의 4항 = **`bundle:` 출처 범주 처리**다. 즉 범위는 다음 4개다.

| # | 범위 | 절 |
|---|---|---|
| 1 | `LocalFrameStore`의 `legacyReadRoot`(= `{exe}\Frame` 읽기·삭제) 제거 | §3.1 |
| 2 | `FrameCatalogService.BundleFolder` · `LoadBundleFrames` 제거 | §3.2 |
| 3 | `MCPhoto.App.csproj`의 `Frame\` 출력 복사 제거 | §3.3 |
| 4 | **`bundle:` 출처 범주 처리** — 제거인가 폐기 보존인가 | **§4** |

`result`는 범위 밖이다. 앱 경로 참조가 **경고 로그 한 곳뿐**이고 읽기·쓰기가 없다(F5) — 이미 완전 이관됐다. 이번 이터레이션은 `App.xaml.cs`의 그 진단을 **건드리지 않는다**.

### 0.2 이 설계가 되돌리는 것 — it26 A-4·A-5

it26은 프레임 캐시를 `%ProgramData%\MCPhoto\Frame`로 옮기면서 `{exe}\Frame`을 **의도적으로 남겼다**. 두 판정이었다.

| it26 판정 | 내용 | it27 처분 |
|---|---|---|
| **A-4** | `{exe}\Frame`을 "운영자가 배치하는 **읽기 전용 번들**"로 유지(`FrameCatalogService.BundleFolder` 불변) | **폐기.** 번들 개념 자체를 없앤다 |
| **A-5** | 구 루트를 **읽기 소스로 상시 포함**(`legacyReadRoot`) — 이관 직후 자산이 목록에서 사라지지 않게 | **폐기.** ① 로컬 캐시는 재취득 가능하다(F12·F13) ② **배포 이력 0이라 보호할 현장 자산 자체가 없다**(F25) |

⚠️ **it26 문서는 수정하지 않는다.** 리포 폐기 관례에 따라 이력을 보존하고, "그 두 판정은 it27에서 사용자 지시로 뒤집혔다"는 사실은 **본 문서와 `docs/analysis/*`(현재 상태 문서)가 말한다**. `docs/design/*`은 작성 시점의 판단 기록이므로 소급 수정하지 않는다.

it26의 판정이 틀렸던 것이 아니다 — 당시엔 "구 설치본에 자산이 남아 있을 수 있다"(it26 U3, **미검증 가정**)를 방어했다. it27은 그 가정을 **반증했다**, 그것도 두 겹으로:

1. **논리적으로**: 로컬 프레임은 전부 서버에서 재취득 가능한 캐시다(F12) — 서버가 정본이므로 사라져도 다시 온다. 서버가 실제로 기본 프레임을 갖고 있음도 실측됐다(F14).
2. **사실로**: **배포된 인스턴스가 0이다**(F25). it26 U3가 물었던 "그런 PC가 실제로 있는가"의 답이 **없다**로 확정됐다. A-5는 존재하지 않는 대상을 방어하는 코드였다.

⭐ 특히 it26 A-5의 가장 강한 근거였던 **"개인 프레임은 로컬 캐시가 유일 사본일 수 있다"**가 성립하지 않는다 — 그런 사본을 가진 현장 PC가 없다. 남는 것은 개발·검증 머신의 캐시뿐이며, 그것은 개발자가 다시 만들 수 있는 재현 가능 자산이다(자산 등급이 다르다).

### 0.3 판정 요약

| 쟁점 | 판정 | 왜 |
|---|---|---|
| **A-1.** `legacyReadRoot` | **매개변수 자체를 제거**한다(호출부 null 처리가 아니다). ctor는 `LocalFrameStore(string rootFolder)` 단일 인자 (§3.1) | ① **호환시킬 설치가 없다**(F25) — 이 매개변수의 존재 이유가 소멸했다 ② 매개변수를 남기면 `{exe}\Frame`을 다시 꽂을 수 있는 문이 남는다 = "완전한 이전"의 반대 ③ 2루트 열거는 `Roots()`·`EnumerateRoots`·`DedupByName` 3중 분기를 만드는데, 이 코드는 **권한 판정이 아니라 I/O 경로**라 보존 가치(fail-closed)가 없다 |
| **A-2.** `DedupByName` | **함께 제거.** 단일 루트에서 이름 중복은 파일시스템이 이미 불가능하게 한다 (§3.1) | `LoadPublic`·`LoadUser`는 한 폴더의 `*.png`를 열거한다 → base name이 유일하다. dedup은 2루트 병합 전용 장치였다 |
| **A-3.** `BundleFolder`·`LoadBundleFrames` | **제거.** 딸린 `LoadOrGenerateSlots`·`GenerateGridSlots`·`ReadImageSize`도 **같이** 제거한다 — 호출자가 `LoadBundleFrames` 하나뿐임을 전수 확인했다 (§3.2) | 남기면 "격자 자동 배치"라는 부활 가능한 경로가 유령으로 남는다 |
| **A-4.** csproj `Frame\` 복사 | **제거.** ⚠️ 단, 이 항목은 **2026-07-23부터 이미 0개 파일을 복사하고 있다**(리포 루트 `Frame/`이 커밋 `694c502`에서 삭제됨) (§3.3) | 팀리드 브리프의 "리포 루트 `Frame/`(png 5개 + slots)"은 실제와 다르다. **삭제할 폴더가 없다** — 지우지 말라는 경고의 대상 자체가 부재한다(F8) |
| **B-1.** `bundle:` 출처 범주 | **폐기 표기로 보존한다**(제거하지 않는다). 없애는 것은 **생성 경로**뿐이다 — "범주는 남기고 공급을 끊는다" (§4.2) | ⭐ **제거는 정리가 아니라 권한 완화다.** `FrameOrigin.Classify`에서 `bundle:` 분기를 지우면 그 id는 `DbDefault`로 떨어지고 `FrameEditPolicy.CanDelete`가 **power에게 삭제를 허용한다**. 같은 반전이 컨버터·`IsDeletable`·`ConfirmDelete`·`DbIdsOf` 4곳에서 더 일어난다(§4.3). 전부 fail-closed 판정이다 |
| **B-2.** 캐시에 `bundle:` id 저장 이력 | **없다. 3중으로 확정된다**(§4.4) — ① v1 평문 `.slots`는 현재 코드가 통째로 배제하므로 어떤 id도 만들지 못한다 ② v2 이후 모든 쓰기 경로의 `dbId`는 서버 부여 문서 id다(전 이력 확인) ③ **배포 이력 0이라 현장 캐시 자체가 없다**(F25) — 남는 것은 개발 머신 캐시뿐 | ⚠️ **rev2 정정**: ③으로 "현장에 그 id가 있을 수 있다"는 잔여 위험이 사실상 소멸했다 → 그 근거로 B-1을 정당화할 수 없다. **B-1은 그 근거에 서 있지 않다**(§4.2 재정렬) — 서 있는 것은 **권한 반전**이다 |
| **B-3.** `DefaultFrameProvider.FrameSource.Bundle` | **보존 + 폐기 표기.** `SelectSource`도 남긴다 (§4.5) | 리포 관례(`UserRole.CreatableRoles` — it15로 프로덕션 호출자 0인데 삭제하지 않고 남겼다, `UserRole.cs:94-95`)를 따른다. 여기서 삭제하면 `DefaultFrameTests`의 우선순위 단정 3개를 지워야 하는데, 그것은 "단정을 지우지 말라"는 이번 지시에 정면으로 어긋난다 |
| **B-4.** 테스트 4+2개 파일 | **단정을 지우는 것은 2개 파일뿐이고, 그 둘은 "재작성 대상"이 아니라 "기능 소멸 대상"이다**(`LocalFrameStoreLegacyRootTests` 11개 · `BundleFrameTests` 1개). 나머지는 유지 또는 주석 정정 (§7.2) | ⚠️ **두 종류를 구분한다**: 사실이 바뀐 단정은 **재작성**하고(§7.4), **검증 대상 자체가 없어진** 단정은 **삭제**한다. 후자를 억지로 재작성하면 존재하지 않는 기능을 검증하는 테스트가 남는다. 삭제하는 12개의 고유 커버리지는 0임을 확인했다(§7.2 표) |
| **C-1.** 프로덕션 영향 | **없다 — 프로덕션이 존재하지 않는다**(F25). 게다가 인스톨러가 `Frame\`을 담지 않으므로(F16) 번들은 배포된 적도 없다 (§5.1) | rev1의 논증(캐시는 재취득 가능)은 그대로 유효하지만, rev2에서는 **더 단순해진다** — 영향을 받을 설치본이 0개다 |
| **C-2.** 개발 환경 영향 | **없다(이미 그 상태다).** csproj 복사가 2026-07-23부터 0개 매치이므로 `bin\...\Frame`은 존재하지 않는다 → 개발 환경의 `bundle:` 경로는 이미 도달 불가다 (§5.2) | 개발 편의를 위한 코드는 **만들지 않는다**. 다른 PC의 `%ProgramData%\MCPhoto\Frame`에서 png+`.slots` **쌍을 복사**하면 그대로 유효하다(서명이 경로·머신에 묶이지 않는다, F10) — 문서 안내로 충분하다 |
| **C-3.** 손으로 프레임 넣는 통로 | **사라진다. 이것이 이번 변경의 유일한 실질 기능 상실이다** (§5.2) | 번들 폴더는 `.slots` 없는 png도 인정하는 유일한 입구였다. 제거 후 신규 프레임은 **편집기 저장(서버 경유) 또는 유효한 `.slots` 쌍 복사**만 가능하다. ⚠️ rev2에서 **위험 등급이 내려간다** — 영향받는 것은 현장 부스가 아니라 개발·검증 머신뿐이다(F25). 그래도 상실 자체는 §10 R1·§11 F-1에 기록한다 |
| **C-4.** 인스톨러 `{app}\Frame` 삭제 행 | **동작 변경 불요.** 지시행(`MCPhoto.iss:110-111`)을 **그대로 남기되 근거를 재규정**한다 — "현장 고아 캐시 정리"가 아니라 **"회귀 방어 + 개발·검증 머신 정리"**다 (§5.3) | 사용자 명시("제거하지 않아도 돼")는 **정리가 불필요하다**는 뜻이고 **지시행을 지워라**는 뜻이 아니다. 지우면 ① `InstallerScriptTests.UninstallDelete_Removes_Frame_Caches`의 단정을 삭제해야 하고(이번 지시와 충돌) ② 인스톨러를 실제로 돌려 본 개발·검증 머신에 고아 폴더가 남는다. **유지 비용 0** |
| **C-5.** 인스톨러 주석 | **4곳 정정**(rev1의 3곳 + `result` 규약 근거 1곳) (§5.3) | 사라질 심볼(`FrameCatalogService.BundleFolder`)을 주석이 참조하고, "앱은 읽기만 한다"가 거짓이 되며, `result` 규약의 근거가 "현장 보호"에서 "회귀 방어"로 바뀐다. **근거가 틀린 채 남은 규약은 다음 사람이 지운다** |
| **C-6.** `{app}\result` 절대 삭제 금지 | **규약 유지. 단 성격이 바뀐다** — "현장 손님 사진 보호"에서 **"회귀 방어 + 개발·검증 머신 보호"**로 (§5.3) | 배포 0이라 현장 손님 사진은 없다. 그러나 ① 인스톨러를 검증한 개발 머신에는 `{app}\result`가 실재한다 ② 규약을 지우면 훗날 저장 경로를 `{app}` 쪽으로 되돌릴 때 **제거가 손님 사진을 지우는 사고가 되살아난다.** 비용 0의 방어이므로 남긴다 |
| **D-1.** UI 문구 | **변경 0건.** 사용자에게 보이는 "번들" 문구가 앱 전체에 **없다**(§4.6) | XAML의 "번들" 3건은 주석 1건 + 폰트 문맥 1건 + 라이선스 문맥 1건이다 |
| **D-2.** 신규 테마 리소스 키 | **0개** | UI 변경이 없다 |

---

## §1 검증된 사실 (verified facts — 전부 코드·git·파일시스템 직접 확인, 2026-08-12)

### 1.1 앱 경로 `Frame`을 접촉하는 지점 (전수)

| # | 사실 | 근거 |
|---|---|---|
| F1 | `LocalFrameStore`는 쓰기 가능한 루트(`%ProgramData%\MCPhoto\Frame`) + **읽기 전용 보조 루트**(`{exe}\Frame`) 2개를 갖는다. 보조 루트에는 **읽기·삭제만** 미치고 쓰기·개인 폴더 생성은 절대 하지 않는다 | `LocalFrameStore.cs:35-54,174-194` / `ServiceRegistration.cs:148-151` |
| F2 | 보조 루트가 실제로 관여하는 지점은 `Roots()`를 지나는 4개다: `LoadPublic` · `LoadUser` · `Inspect` · (경로 기반이라 자동으로) `DeleteLocal` | `LocalFrameStore.cs:92-107,137-150,121-133,181-194` |
| F3 | `FrameCatalogService.BundleFolder = Path.Combine(AppContext.BaseDirectory, "Frame")`이고, 유일한 소비자는 `LoadBundleFrames()`다. 그 호출부도 **1곳**(`ResolveLocalFrames`의 ③)이다 | `FrameCatalogService.cs:45,60,289,444-476` |
| F4 | `LoadBundleFrames`가 만드는 프레임의 id는 `$"bundle:{name}"`이고, `.slots`가 있으면 v1 평문 포맷(`index,x,y,w,h`)으로 읽고 없으면 `GenerateGridSlots`가 2×2 격자를 만든다. ⚠️ 이 v1 읽기는 `SlotsFileCodec`(v2 서명)과 **완전히 별개 코드**다 | `FrameCatalogService.cs:459-516` |
| F5 | `result`의 앱 경로 참조는 **경고 로그 전용**이다 — `Directory.Exists`로 존재만 확인해 위치를 로그로 알린다. 읽기·쓰기·이동·삭제가 없다 | `App.xaml.cs:115-123` |

### 1.2 `bundle:` 프레임이 실제로 생성될 수 있는 조건 (⭐ 핵심)

`LoadBundleFrames`는 `ResolveLocalFrames`의 **③단계**이므로 다음 3조건이 **동시에** 성립해야 호출·성공한다.

| 조건 | 근거 |
|---|---|
| ① `LocalFrameStore.LoadPublic()`이 **0개**를 돌려준다(`local.Count > 0`이면 ②에서 즉시 반환) | `FrameCatalogService.cs:281-294` |
| ② `{exe}\Frame`에 `.png`/`.jpg`/`.jpeg` 파일이 있다 | `FrameCatalogService.cs:447-453` |
| ③ 그 이미지에 **유효한 v2 `.slots`가 없다** — 있으면 `LocalFrameStore`가 이미 집어 ①이 깨진다 | `LocalFrameStore.cs:217-242` |

| # | 사실 | 근거 |
|---|---|---|
| F6 | 즉 `bundle:` 프레임은 "**서명된 `.slots`가 없는 이미지**"에서만 나온다 — 운영자가 손으로 png를 떨어뜨린 경우가 정확히 그것이다. it26이 말한 "운영자가 배치하는 읽기 전용 번들"의 실체다 | F1·F3·F4 조합 |
| F7 | 두 경로의 대상 파일 집합은 **겹치지 않는다**(it26 F13 재확인) — `LocalFrameStore`는 v2 서명 `.slots` 있는 png만, `LoadBundleFrames`는 그 나머지만 집는다 | `LocalFrameStore.cs:220-223` / `FrameCatalogService.cs:449-453` |

### 1.3 ⚠️ 리포 루트 `Frame/` — 존재하지 않는다 (팀리드 전제 정정)

| # | 사실 | 근거 |
|---|---|---|
| F8 | **리포 루트 `Frame/` 폴더는 없다.** git에 추적되는 파일이 0개이고(`git ls-files | grep '^Frame/'` → 빈 결과) `.gitignore`에도 항목이 없다. 작업 트리에도 없다 | `git ls-files` / `ls` 실측 |
| F9 | 이력: MVP 커밋 `c7d0720`에 `Frame/jport-camp.png` + `.slots` **2개 파일**로 추가됐고, **2026-07-23 커밋 `694c502`("불필요 TEMP 파일 제거")에서 삭제**됐다. 그 뒤로 다시 추가된 적이 없다 | `git log --diff-filter=A -- 'Frame/*'` / `git show --stat 694c502` |
| **F8·F9의 귀결** | `MCPhoto.App.csproj:121-123`의 `Include="...\..\Frame\**\*.*"` 글롭은 **2026-07-23부터 0개 파일을 매치한다**(MSBuild는 빈 글롭을 오류로 보지 않는다). 개발 빌드 출력에 `Frame` 폴더가 **생기지 않는다**(`src/MCPhoto.App/bin/Debug/net10.0-windows/Frame` 부재 실측) | 실측 |
| **⇒ 결정적 귀결** | **`bundle:` 프레임은 이 리포에서 2026-07-23 이후 한 번도 생성된 적이 없다.** 프로덕션은 인스톨러가 `Frame\`을 담지 않아(F16) 처음부터 부재였고, 개발 환경은 F9로 부재가 됐다 | F6 + F8·F9 + F16 |

> 팀리드 브리프의 "리포 루트 `Frame/`(png 5개 + slots)"과 "그 폴더를 삭제하지 마라"는 경고는 **실측과 다르다.** png 5개는 리포가 아니라 `publish/MCPhoto/Frame`에 있고(F15), 그것은 **실행 흔적**(서버 다운로드 캐시)이다. 삭제를 금할 대상 폴더는 애초에 존재하지 않으므로 이 위험은 소멸한다.

### 1.4 `.slots`·서명

| # | 사실 | 근거 |
|---|---|---|
| F10 | `.slots` 서명 키는 **소스 고정 상수**이며 머신·빌드에 묶이지 않는다(개발 빌드와 운영 빌드가 서로의 프레임을 읽을 수 있게 한 의도적 선택). 서명 payload에 파일 경로가 없다 | `FrameSigningKey.cs:13-15,28` / `SlotsFileCodec.cs:77-91` |
| F11 | v1 평문 `.slots`는 base64 디코딩 단계에서 걸러져 `NotEncoded`가 되고, `LocalFrameStore.Enumerate`가 `Ok`가 아닌 항목을 **통째로 건너뛴다** → **v1 파일은 어떤 `Id`도 만들지 못한다**. 회귀 테스트가 이 사실을 잠근다 | `SlotsFileCodec.cs:112-122` / `LocalFrameStore.cs:222-223` / `LocalFrameStoreTests.cs:108` |

### 1.5 프레임 정본·재취득 가능성

| # | 사실 | 근거 |
|---|---|---|
| F12 | **개인 프레임은 서버가 정본이다.** `SyncUserCache`가 "서버 정본에 없는 개인 캐시를 지운다"를 수행하고, 서버에만 있는 것은 내려받는다. 공용도 동일 규칙이다 | `FrameCatalogService.cs:336-345,350-377,198-235` |
| F13 | ⚠️ **단 서버 미도달 시**에는 `return local`로 **로컬 캐시가 유일 출처**가 된다(삭제 판정도 하지 않는다 — `FrameSyncPlan` 안전장치 1) | `FrameCatalogService.cs:330-334,180-183` |
| F14 | **서버가 기본 프레임을 갖고 있다(실측).** 인스톨러가 `Frame\`을 담지 않는데도 설치본 실행 후 `{app}\Frame`에 프레임 5개가 생겼다 — 서버 다운로드분이다 | 팀리드 실측 보고 + F15 |
| F15 | `publish/MCPhoto/Frame`의 5개(`HBD` · `j-port 1` · `j-port 2` · `j-port 3` · `test frame`)는 **전부 png+`.slots` 쌍**이다 → 하나도 `bundle:`이 아니다(F7에 의해 `LocalFrameStore`가 집는다) | 실측 |
| F16 | 인스톨러 `[Files]`는 화이트리스트이고 `Frame\`을 담지 않는다("기본 프레임은 서버에서 내려받는다"). `[Dirs]`가 `{commonappdata}\MCPhoto\Frame`을 `users-modify`로 만든다 | `installer/MCPhoto.iss:70,89` |
| F17 | 프레임이 0개면 `FallbackFrameRenderer`가 1200×1600 흰 배경 4슬롯 프레임을 **생성**한다(파일 쓰기 포함) → 프레임 목록이 완전히 비는 경로는 없다 | `FrameCatalogService.cs:296-298,528-547` |

### 1.6 인스톨러 — 구 캐시 삭제는 이미 구현됨

| # | 사실 | 근거 |
|---|---|---|
| F18 | 제거 시 `{app}\Frame`을 `filesandordirs`로 **이미 지운다**. 주석이 그 근거를 "구 프레임 캐시(재취득 가능)"로 명시한다 | `installer/MCPhoto.iss:110-111` |
| F19 | `InstallerScriptTests.UninstallDelete_Removes_Frame_Caches`가 구 위치·신 위치 **둘 다**의 삭제 행 존재를 단정한다 → it27에서 **테스트 변경 불요** | `InstallerScriptTests.cs:86-94` |
| F20 | `{app}\result`·`{commonappdata}\MCPhoto\result` 삭제 행의 **부재**를 단정하는 테스트가 있다(손님 사진 보호) | `InstallerScriptTests.cs:66-84` |
| F21 | ⚠️ `MCPhoto.iss`의 주석 **4곳**이 곧 거짓이 되거나 근거가 어긋난다: `:5-6`("앱은 `{app}\Frame`을 읽기만 한다") · `:104-107`(`FrameCatalogService.BundleFolder` 심볼 참조 — 그 심볼이 사라진다) · `:110`("it26 이후 앱은 여기에 쓰지 않고 읽기만 한다") · `:112-116`(`{app}\result` 규약의 근거를 "현장 손님 사진 보호"로 서술하는데 **배포 이력이 0이라 아직 참이 아니다** — F25) | 실독 |

### 1.7 코드 관례

| # | 사실 | 근거 |
|---|---|---|
| F22 | 리포는 **도달 불가여도 규약을 명시 열거로 남긴다**: `UserRole.CreatableRoles`는 it15의 계정 생성 폐지로 프로덕션 호출자가 0인데 삭제하지 않았다. 근거 주석: "훗날 되살아날 때 E3와 모순되는 규칙이 조용히 부활하는 것을 막는다" | `UserRole.cs:94-95` |
| F23 | 정적 검증 테스트 관례가 이미 3종 있다: 소스 스캔(`XamlResourceTests`), 스크립트 정적 검증(`InstallerScriptTests`), csproj/출력 스캔(`LicenseComplianceTests`) | 각 파일 |
| F24 | `.cs`는 UTF-8 **BOM 없음**(한글 주석 포함). XAML·`.iss`·문서는 기존 인코딩 유지 | agent-memory `source-file-encoding` |

### 1.8 ⭐ 배포 이력 0 (rev2 전제)

| # | 사실 | 근거 |
|---|---|---|
| **F25** | **이 제품은 아직 배포된 적이 없다.** 현장에 설치된 인스턴스가 0이다 | 사용자 확정(§0.1 rev2 정정) |
| F26 | 따라서 `{app}\Frame`·`{app}\result`·`{app}\MCPhoto.ini`가 실재하는 곳은 **인스톨러를 돌려 본 개발·검증 머신뿐**이다. 팀리드의 인스톨러 검증에서 실제로 생성됐다 | 팀리드 실측(`MCPhoto.iss:104` 주석의 "실측(2026-08-12)") + `publish/MCPhoto/{Frame,result,MCPhoto.ini}` 실재(F15) |
| F27 | 인스톨러 `AppId`(`{9303675E-…}`)의 **"한번 배포하면 절대 바꾸지 않는다" 규약은 기산점이 아직 오지 않았다** — 지금은 자유롭게 바꿀 수 있다. 주석은 이미 그 상태를 정확히 말한다("출하 **전에** 고정해 둔다") | `installer/MCPhoto.iss:38-41` |

> ⚠️ **F25가 자산 등급을 바꾼다.** rev1은 `{app}` 하위 데이터를 "현장 자산(복구 불가)"으로 다뤘다. rev2에서 그것은 **개발·검증 머신의 재현 가능 자산**이다. 이 등급 차이가 §2의 가정 2개를 소멸시키고 착수 전 블로킹 질문을 없앤다. **단, 등급이 내려갔다는 것이 "규약을 지워도 된다"는 뜻은 아니다**(§0.3 C-6).

---

## §2 ⚠️ 미검증 가정 (open assumptions)

| # | 가정 | 위험 | 검증 단계 |
|---|---|---|---|
| **U1** | 어딘가에 **`#dbid`가 `bundle:`로 시작하는 v2 `.slots`가 존재하지 않는다** | 존재하면 그 프레임은 목록에 오르고 `Bundle`로 분류된다 | **검증 불요 — 설계로 무해화됨.** §4.4의 3중 확정이 "없다"를 지지하고, 있어도 §4.2의 폐기 보존 판정이 종전과 동일한 거동(삭제 금지)을 보장한다. ⚠️ **rev2**: F25로 이 위험은 사실상 소멸했다 — **따라서 이 가정은 §4.2 판정의 근거가 아니다**(§4.2 재정렬) |
| ~~**U2**~~ | ~~`legacyReadRoot` 제거로 목록에서 사라지는 프레임이 있는 PC가 없다~~ | — | **rev2에서 소멸.** F25(배포 0)로 그런 PC가 존재하지 않는다. 개발·검증 머신에서는 서버 재취득 또는 §5.2 ②(쌍 복사)로 즉시 회복된다. ※ Step 3의 실행 관측은 **회귀 확인 목적으로 유지**한다 |
| ~~**U3**~~ | ~~`{app}\Frame`에 `.slots` 없는 png를 손으로 넣어 쓰는 운영 PC가 없다~~ | — | **rev2에서 소멸.** 운영 PC가 0개다(F25) → **착수 전 블로킹 질문(rev1 UA-1)이 해소됐다.** 개발 머신에 그런 파일이 있다면 그 개발자만 영향을 받고, 회복 절차는 §5.2에 있다 |
| **U4** | `dotnet test` 기준선이 **1496개 통과**이고, 이번 변경으로 삭제되는 테스트는 정확히 12개(`LocalFrameStoreLegacyRootTests` 11 + `BundleFrameTests` 1)다 | 기준선이 다르면 "신규 단정이 실제로 늘었는가"를 산술로 검증할 수 없다 | **Step 0(선행 측정)**: 변경 전 `dotnet test` 1회 실행해 실제 개수를 기록한다. 이후 각 단계의 완료 기준은 **절대 개수 대신 증감**으로 판정한다 |
| **U5** | `LoadOrGenerateSlots`·`GenerateGridSlots`·`ReadImageSize`의 호출자가 `LoadBundleFrames` **하나뿐**이다 | 다른 호출자가 있으면 함께 지울 수 없다 | **Step 2**(제거 후 빌드 오류 0으로 기계 증명 — 남은 호출자가 있으면 컴파일이 깨진다). 설계 시점 grep 결과는 호출자 각각 1곳(`FrameCatalogService.cs:457,467,500`)이며 전부 `LoadBundleFrames` 내부 또는 그 하위다 |

> **rev2 정리**: 살아 있는 가정은 **U4·U5 둘뿐**이고 둘 다 코드·명령으로 기계 검증된다(Step 0·Step 2). **사람에게 물어야 하는 블로킹 질문이 0개**다 → 착수 가능.
>
> 전 단계 완료 후에도 남는 미검증: 오프라인 첫 실행 부스의 UX(프레임 = fallback 1개). 이는 it27이 만든 것이 아니라 인스톨러 화이트리스트(F16, it24)가 이미 만든 상태이며 이번에 바꾸지 않는다.

---

## §3 A부 — 제거 범위

### 3.1 `legacyReadRoot` 제거 (`LocalFrameStore`)

#### 3.1.1 판정: 매개변수 자체를 없앤다

호출부만 `null`로 두는 안과 비교한다.

| 축 | (가) 매개변수 제거 ✅ 채택 | (나) 호출부만 null |
|---|---|---|
| **존재 이유** | 이 매개변수는 **"기존 설치 호환"만을 위한 것**이었다(it26 §3.4.3). 호환시킬 설치가 0이므로(F25) 존재 이유가 소멸했다 → 제거 | 존재 이유가 없는 매개변수를 남긴다 |
| "완전한 이전" 부합 | ○ — 앱 경로를 다시 꽂을 수 있는 표면이 없다 | ✕ — 한 줄로 되살아난다 |
| 코드 단순화 | `_legacyRoot`·`Roots()`·`EnumerateRoots`·`DedupByName` 4개 제거 | 0 (분기가 전부 남는다) |
| 보존 가치(fail-closed) | **없음** — I/O 경로이며 권한 판정이 아니다 | — |
| 테스트 영향 | `LocalFrameStoreLegacyRootTests` 11개 삭제(고유 커버리지 0, §7.2) | 전량 유지되지만 **거짓 안심**(도달 불가 코드를 검증한다) |
| 되돌리기 비용 | it26 설계 §3.4.3 + 본 문서가 규칙 전문을 보존 → 재구현 가능 | — |

#### 3.1.2 변경 후 형태

```
public LocalFrameStore(string rootFolder)      // 인자 1개
{
    _root = rootFolder;
}
```

제거 대상(파일: `src/MCPhoto.Core/Frames/LocalFrameStore.cs`):

| 지점 | 처분 |
|---|---|
| `:35-36` `_legacyRoot` 필드 | 삭제 |
| `:38-54` ctor의 `legacyReadRoot` 매개변수 + 동일 경로 판정 | 삭제(인자 1개 ctor로) |
| `:181-185` `Roots()` | 삭제 |
| `:187-194` `EnumerateRoots` | 삭제 → 호출부가 `Enumerate(folder, viewer)`를 직접 부른다 |
| `:196-207` `DedupByName` | 삭제(A-2) |
| `:92-96` `LoadPublic` | `Enumerate(_root, viewerEmail: null).Where(IsDefault).Select(frame)` → `ToList()` |
| `:98-107` `LoadUser` | `Enumerate(UserFolder(owner), viewerEmail: owner).Where(!IsDefault).Select(frame)` → `ToList()` |
| `:137-150` `Inspect` | `foreach (var root in Roots())` 루프 제거 → `_root` 1회 + `UserFolder(_root, owner)` 1회 |
| `:174-178` `UserFolder(root, email)` static 오버로드 | **유지**(`Inspect`가 쓴다). `UserFolder(email)` 인스턴스 버전도 유지 |
| `:117-120` `DeleteLocal`의 "구 루트도 지운다" 주석 | 문구 정정 — 경로 기반이라는 사실만 남긴다 |
| `:19-26` 클래스 주석의 it26 §3.4.3 단락 | it27 서술로 교체(§3.1.3) |

⚠️ **거동 불변식 3개**(리뷰어 체크포인트):

1. `LoadPublic`·`LoadUser`의 **반환 순서**는 `Directory.EnumerateFiles` 순서 그대로다(종전에도 새 루트가 먼저였으므로 새 루트만 남으면 동일 순서다).
2. `PublicFrameNames`·`UserFrameNames`는 `LoadPublic`/`LoadUser` 위에 얹혀 있어 **손대지 않는다**.
3. `DeleteLocal`은 `frame.ImageUrl` 절대경로 기반이라 **한 줄도 바뀌지 않는다** — 루트 개념과 무관하다.

#### 3.1.3 클래스 주석 교체 문구 (동결)

```
/// it27 §3.1 — 루트는 **하나**다. 종전의 읽기 전용 보조 루트({exe}\Frame)는 제거했다:
/// ① 그 폴백은 "이관 전 설치본이 남긴 캐시"를 위한 것이었는데 이 제품은 그 시점까지 배포된 적이
///    없어 보호할 대상이 존재하지 않았다 ② 로컬 프레임은 전부 서버에서 재취득 가능한 캐시다
///    (FrameCatalogService의 SyncPublicCache·SyncUserCache가 서버를 정본으로 다룬다)
/// ③ 앱 경로를 읽는 표면을 남기면 "앱 경로 완전 제거"가 성립하지 않는다.
/// 구 위치의 잔재는 인스톨러가 제거 시 정리한다(MCPhoto.iss [UninstallDelete] — 개발·검증 머신용).
```

### 3.2 `BundleFolder`·`LoadBundleFrames` 제거 (`FrameCatalogService`)

파일: `src/MCPhoto.App/Services/FrameCatalogService.cs`

| 지점 | 처분 |
|---|---|
| `:44-45` `BundleFolder` 프로퍼티 | 삭제 |
| `:60` ctor의 `BundleFolder = Path.Combine(AppContext.BaseDirectory, "Frame")` | 삭제 → ⭐ **이 파일에서 `AppContext.BaseDirectory` 참조가 0이 된다**(Step 2의 기계 검증 지점) |
| `:444-476` `LoadBundleFrames()` | 삭제 |
| `:478-502` `LoadOrGenerateSlots()` | 삭제(U5 — 호출자는 `LoadBundleFrames` 하나) |
| `:504-516` `GenerateGridSlots()` | 삭제(호출자는 `LoadOrGenerateSlots` 하나) |
| `:577-582` `ReadImageSize()` | 삭제(호출자는 `LoadBundleFrames` 하나) |
| `:286-294` `ResolveLocalFrames`의 ③ 블록 | 삭제 → 로컬 0개면 곧바로 fallback |
| `:11-12` 클래스 주석의 우선순위 서술 | "①서버 isDefault → ②로컬 캐시 → ③fallback"으로 교체 |
| `:65,87,151,182,196,265,275` 주석의 "번들" | 문구 정정(§4.6 표) |
| `:254-262` `DbIdsOf`의 `bundle:` 필터 | ⭐ **유지한다** — 제거하면 fail-safe가 깨진다(§4.3 ④) |
| `:435-442` `DefaultDownloadAsync`의 "번들/기존 캐시" 주석 | "로컬 캐시 파일 경로"로 정정. **코드는 불변**(로컬 경로 직접 읽기는 재캐시 경로에서 여전히 유효하다) |

변경 후 `ResolveLocalFrames`:

```
private IReadOnlyList<FrameTemplate> ResolveLocalFrames(IReadOnlyList<FrameTemplate>? preferLoaded)
{
    var local = preferLoaded ?? _localStore.LoadPublic();
    if (local.Count > 0) { ...기존 로그... return local; }

    // ② fallback(코드 생성) — it27: 종전 ②였던 번들 폴더 스캔은 폐기됐다(§3.2).
    _logger?.LogInformation("fallback 프레임 생성");
    return new[] { EnsureFallbackFrame() };
}
```

⚠️ **`EnsureFallbackFrame`·`MoveWithRetry`·`FallbackImagePath`는 손대지 않는다.** `FallbackImagePath`는 `App.DataFolder` 기반이라 앱 경로와 무관하다(`:61`).

### 3.3 csproj `Frame\` 복사 제거

파일: `src/MCPhoto.App/MCPhoto.App.csproj`

`:119-124`의 `<!-- 번들 기본 프레임(Frame/) ... -->` 주석 + `<ItemGroup>` **전체를 삭제**하고, 그 자리에 폐기 기록 한 줄을 남긴다(리포 관례: it18의 `bldinfo.ini` 폐기 주석 `:131-132`과 동형).

```xml
  <!-- it27: 번들 기본 프레임(리포 루트 Frame/ → 출력 Frame\) 복사 폐기.
       기본 프레임은 서버에서 내려받아 %ProgramData%\MCPhoto\Frame에 캐시한다 → 배포물에 담을 프레임이 없다.
       ⚠️ 리포 루트 Frame/ 폴더 자체는 2026-07-23(694c502)에 이미 삭제되어, 이 글롭은 그때부터
          0개 파일을 복사하고 있었다(F8·F9) → 이 제거는 빌드 산출물을 바꾸지 않는다. -->
```

**신규·삭제할 파일 없음**(리포 루트 `Frame/`은 존재하지 않는다 — F8).

### 3.4 리포 루트 `Frame/`의 성격 — 문서에 남길 서술

팀리드 브리프는 이 폴더를 "서버 시딩·디자인 원본일 수 있다"고 봤으나 **실재하지 않는다**(F8·F9). 따라서 성격 규정 대신 **이력**을 남긴다. 반영 위치는 `docs/analysis/80-build-and-deployment.md`(§8 표)이고 문구는 다음을 쓴다.

> 리포 루트 `Frame/`은 MVP 시절 번들 프레임 1개(`jport-camp`)를 담고 있었고 **2026-07-23(`694c502`)에 삭제**됐다. 그 뒤 `MCPhoto.App.csproj`의 복사 항목은 0개 파일을 매치했고, it27에서 그 항목도 제거했다. **기본 프레임의 유일한 출처는 서버다.**

---

## §4 B부 — ⭐ `bundle:` 출처 범주 처리 (이 설계의 핵심 판정)

### 4.1 문제

§3.2를 하면 `bundle:` id를 **만들 수 있는 코드가 0**이 된다. 그러면 그 범주를 판정하는 코드는 무엇이 되는가?

### 4.2 판정: 폐기 표기로 **보존**한다 — "범주는 남기고 공급을 끊는다"

| 축 | (가) 분기 제거 | (나) 폐기 표기 보존 ✅ **채택** |
|---|---|---|
| **권한 거동** | ⛔ **완화된다.** `Classify`의 `bundle:` 분기를 지우면 그 id는 `UserId` 없음 → `local:` 아님 → **`DbDefault`**로 떨어지고, `FrameEditPolicy.CanDelete(DbDefault, power) = true` → **power가 삭제할 수 있게 된다** | 불변(누구도 삭제 못 함) |
| **연쇄 반전 지점** | ⛔ **4곳 더**: 컨버터가 ✕를 노출, `IsDeletable`이 true, `ConfirmDelete`가 서버 DELETE 시도, `DbIdsOf`가 삭제 동기화 대상에 포함(§4.3) | 0곳 |
| **리포 관례 부합** | ✕ — `UserRole.CreatableRoles` 선례와 반대(F22) | ○ |
| **유지 비용** | — | 상수 1개 + enum 멤버 1개 + `switch` 1행 + 문자열 비교 4곳 ≈ **8줄** |
| **오해 위험** | — | "번들 기능이 있나 보다"는 오해 → **폐기 주석으로 차단**(문구 §4.7) |

**판정 근거를 한 문장으로**: `bundle:` 판정은 전부 **fail-closed**(모르는 출처는 손대지 않는다)이고, 제거는 그것을 **fail-open**으로 뒤집는다. 이번 작업의 목적은 앱 경로 사용을 끊는 것이며 권한 규칙을 느슨하게 하는 것이 아니다.

#### ⚠️ rev2 — 근거 재정렬 (배포 0이 무엇을 무너뜨렸는가)

rev1은 세 근거를 나란히 놓았다. **배포 이력 0(F25)이 그중 하나를 무너뜨렸다.** 정직하게 분리한다.

| 근거 | rev1 | rev2 |
|---|---|---|
| ⓐ **제거 = 권한 완화**(fail-closed → fail-open, 5지점 연쇄) | 결정적 | **결정적 — 무변화.** 배포 여부와 무관한 **정적 코드 성질**이다 |
| ⓑ 리포 관례(도달 불가여도 규약은 명시 열거로 남긴다, F22) | 보조 | **유지** |
| ⓒ "현장 캐시에 그 id가 있을 수 있다 — 증명은 소스 이력 논증일 뿐" | 보조 | ⛔ **무너졌다.** 현장 캐시가 0이다(F25). 남는 것은 개발 머신 캐시이고 그것은 개발자가 확인·재생성할 수 있다 → **방어적 보존의 근거로 쓸 수 없다** |

**판정은 그대로다** — ⓒ가 사라져도 ⓐ가 홀로 판정을 지탱한다. 반대로 말하면: **만약 ⓐ가 없었다면(예: `bundle:` 분기가 단순 표시용이었다면) rev2에서 판정을 제거로 뒤집었어야 한다.** 어떤 근거가 판정을 지탱하는지 적어 두지 않으면, 다음 사람이 무너진 ⓒ만 보고 "근거가 틀렸으니 지우자"로 갈 수 있다.

⭐ **판정을 지탱하는 것은 "그 id가 실재할 수 있다"가 아니라 "그 분기가 없으면 권한이 완화된다"다.** `FrameOrigin.cs`의 동결 주석(§4.7)이 정확히 이 문장을 담고 있어야 한다.

**따라서 제거하는 것은 생성 경로뿐이다**: `BundleFolder` · `LoadBundleFrames` · csproj 복사 (§3).

### 4.3 `bundle:` 판정 지점 전수표 (7곳)

| # | 지점 | 하는 일 | 제거 시 반전되는 결과 | 처분 |
|---|---|---|---|---|
| ① | `FrameOrigin.cs:29,44` `BundlePrefix` → `FrameOriginKind.Bundle` | 출처 분류 | `DbDefault`로 오분류 → 아래 ②③⑤가 연쇄 반전 | **유지** + 폐기 주석 |
| ② | `FrameEditPolicy.cs:32-37` `_ => false` | 삭제 불가 판정 | `role.IsPower()` 분기로 이동 → **power 삭제 허용** | **유지**(코드 무변경, 주석만 정정) |
| ③ | `CommonConverters.cs:245-246` | 삭제 ✕ 버튼 숨김 | power에게 ✕ **노출** | **유지** + 주석에 폐기 표기 |
| ④ | `FrameCatalogService.cs:259` `DbIdsOf`의 `bundle:` 제외 | 서버 대조 키에서 제외 | `bundle:x`가 대조 집합에 들어가고 서버 목록엔 없으므로 **`FrameSyncPlan`이 삭제 대상으로 잡는다** → 운영자가 배치한 파일을 앱이 지운다 | **유지**(⚠️ fail-safe — §3.2 표에 명시) |
| ⑤ | `FrameSelectViewModel.cs:113` `IsDeletable` | 삭제 후보 제외 | `true`가 되어 삭제 흐름 진입 | **유지** + 주석 정정 |
| ⑥ | `FrameSelectViewModel.cs:346` `hasServerDoc` | 서버 DELETE 대상 제외 | 존재하지 않는 문서에 **DELETE 요청** → 실패 안내 후 로컬도 안 지움(무해하지만 무의미한 네트워크·오류) | **유지** |
| ⑦ | `DefaultFrameProvider.cs:22,33` `FrameSource.Bundle` | 우선순위 결정(순수 함수) | fail-closed 아님. 단, 삭제하면 `DefaultFrameTests` 단정 3개를 지워야 한다 | **유지** + 폐기 주석(B-3) |

> ①~⑥은 **`bundle:` 접두를 아는 것 자체가 방어**다. ⑦만 성격이 다르며, 그것은 리포 관례(F22)와 "단정을 지우지 말라"는 지시로 보존한다.

### 4.4 캐시에 `bundle:` id가 저장된 이력 — **없다** (3중 확정)

지시받은 확인 항목이다. `LocalFrameStore.Write` / `SaveDefaultFrame` / `SaveUserFrame` 경로를 이력까지 추적했다.

**증명 ①(포맷 차단)** — v2 이전에 쓰인 `.slots`는 **어떤 id도 만들지 못한다**.
`.slots` v1은 평문 `index,x,y,w,h`였고, 현재 `SlotsFileCodec.Decode`는 base64 디코딩 실패를 `NotEncoded`로 돌린다(`:112-122`). `LocalFrameStore.Enumerate`는 `Ok`가 아닌 항목을 `continue`로 건너뛴다(`:222-223`) → **v1 파일은 프레임으로 인정되지 않으므로 `Id`도 없다**. 이 사실은 회귀 테스트 `LocalFrameStoreTests.Legacy_Plaintext_Slots_Is_Ignored`(`:108`)가 잠근다.

**증명 ②(쓰기 인자 전수)** — v2 이후 `dbId` 인자는 **항상 서버 부여 문서 id**다.
`git log -S 'SaveDefaultFrame'`로 v2 도입(`9db0fcc`) 이후 전 이력의 호출부를 확인했다. 시점을 통틀어 호출부는 4개뿐이고 인자는 다음과 같다.

| 호출부 | `dbId` 인자 | 출처 |
|---|---|---|
| `FrameCatalogService.TryCacheAsync` (`:416`) | `f.Id` | `_repository.GetDefaultFramesAsync()` 반환 = 서버 문서 id |
| `FrameCatalogService.TryCacheUserFrameAsync` (`:388`) | `f.Id` | `_repository.GetUserFramesAsync()` 반환 = 서버 문서 id |
| `FrameEditorViewModel.Save` 공용 (`:665`) | `saved.Id` | `_repository.SaveAsync()` 반환(`Id = string.Empty`로 보내 서버가 부여, `:641`) |
| `FrameEditorViewModel.Save` 개인 (`:703`) | `savedMine.Id` | `_repository.SaveMineAsync()` 반환(동일, `:681`) |

**`bundle:` 프레임이 `Write()`에 들어간 경로는 존재한 적이 없다.** "기존 프레임 불러오기"(`ApplyPickedFrame`)도 fork가 아니라 신규 생성이며 `Id = string.Empty`로 서버에 보낸다(`:415,641,681`).

**증명 ③(모집단 부재 — rev2)** — **배포 이력이 0이므로 현장 캐시 자체가 존재하지 않는다**(F25).
`.slots` 파일이 실재할 수 있는 곳은 인스톨러를 돌려 본 **개발·검증 머신**뿐이고, 그 머신의 캐시는 개발자가 직접 확인·삭제·재생성할 수 있다. 즉 증명 ①②가 다루던 "혹시 남아 있을 수 있는 파일"의 **모집단이 비어 있다**.

**보강 실측** — `publish/MCPhoto/Frame`의 5개 프레임은 전부 png+`.slots` 쌍이고(F15), 쌍이 있으면 `LocalFrameStore`가 집어 서버 `#dbid`를 Id로 쓴다(F7) → 그중 `bundle:`은 하나도 없다.

**결론**: 제거 후 `bundle:` id는 **어디에도 존재하지 않는다.** 이 결론은 rev2에서 더 강해졌다.

⚠️ **그래서 이 절은 §4.2 판정의 근거가 아니다.** rev1은 여기서 "그래도 v2 `.slots`는 서명만 맞으면 `#dbid`에 임의 문자열을 담을 수 있으니(`SlotsFileCodec.cs:182-186`) 방어를 남긴다"로 이었는데, 그 논거는 배포 0 앞에서 힘을 잃는다(§4.2 rev2 표의 ⓒ). **보존을 정당화하는 것은 §4.3의 권한 반전(ⓐ) 하나다.** 이 절의 역할은 "`bundle:` id를 실제로 만날 확률은 0에 가깝다"는 사실을 정직하게 기록하는 것이며, 그 사실이 판정을 바꾸지 않는 이유는 §4.2에 있다.

### 4.5 `DefaultFrameProvider` 처분 (B-3)

`FrameSource.Bundle`·`SelectSource(hasDbFrames, hasBundleFrames)`는 프로덕션 호출자가 **이미 0**이고(테스트만 참조) it27로 두 번째 인자가 영구히 `false`가 된다.

**판정: 시그니처·enum 그대로 두고 폐기 주석만 추가한다.** 근거: ① 리포 관례 F22 ② 삭제하면 `DefaultFrameTests`의 우선순위 단정 3개를 지워야 하는데 그것이 이번 지시("단정을 지우지 말라")와 정면 충돌 ③ 순수 함수라 유지 비용이 0이다.

주석 추가 문구(클래스 XML 주석 상단, 동결):

```
/// ⚠️ it27: 우선순위 ②(설치 폴더 Frame/ 번들)는 **폐기됐다** — 번들 스캔 코드를 제거해
///    hasBundleFrames가 참이 되는 경로가 없다(설계 it27 §3.2). 프로덕션 호출자는 it20 이후
///    이미 0이며(우선순위는 FrameCatalogService.ResolveLocalFrames가 직접 구현한다),
///    UserRole.CreatableRoles와 같은 이유로 열거를 남긴다: 훗날 번들 개념이 되살아날 때
///    폐기된 규칙이 조용히 부활하는 것을 막는다. **삭제 금지.**
```

### 4.6 "번들" 문구·표시 정리 목록

**사용자에게 보이는 문구 변경은 0건이다.** XAML 전체에서 "번들"은 3건뿐이며 성격이 각각 다르다.

| 위치 | 내용 | 처분 |
|---|---|---|
| `FrameSelectView.xaml:36` | 주석 "번들/fallback·게스트·user·temp_user 미노출" | 주석 문구 정정("`bundle:`(폐기)/fallback …") |
| `Themes/Typography.xaml:6` | "폰트는 시스템 안전 조합(한글 포함, **번들 불필요**)" | **무관 — 손대지 않는다**(폰트 파일 동봉 문맥) |
| `SettingsView.xaml:902-905` | `HasLicenseBundledComponents` / `LicenseBundledComponents` | **무관 — 손대지 않는다**(오픈소스 고지 문맥. it24 라이선스 표면이며 식별자 개명은 라이선스 테스트를 건드린다) |

C# 주석의 "번들" 정리 목록(문구만, 코드 무변경):

| 파일:줄 | 현재 서술 | 정정 방향 |
|---|---|---|
| `FrameCatalogService.cs:11-12` | "①DB isDefault → ②설치 Frame/ 번들 → ③fallback" | "①서버 isDefault → ②로컬 캐시 → ③fallback" |
| `FrameCatalogService.cs:65,151` | "로컬 공용(번들+파워캐시)" | "로컬 공용 캐시" |
| `FrameCatalogService.cs:87` | "로컬 스캔·번들 디코드·fallback 생성" | "로컬 스캔·fallback 생성" |
| `FrameCatalogService.cs:182` | "로컬/번들/fallback로 폴백" | "로컬 캐시/fallback로 폴백" |
| `FrameCatalogService.cs:196` | "`#dbid`가 없는 번들 프레임은 애초에 대상이 아니다" | "`#dbid`가 없는 로컬 전용(`local:`) 프레임은 대상이 아니다" ⚠️ 안전장치의 실제 수혜자로 정정 |
| `FrameCatalogService.cs:265,275` | "로컬 공용 → 번들 → fallback" | "로컬 공용 → fallback" |
| `ILocalFrameStore.cs:23,40` | "공용(번들·DB default 캐시)" / "번들 캐시·DB default 캐시" | "공용(서버 default 캐시·power 공용 생성분)" |
| `FrameSyncPlan.cs:28` | "번들 프레임(앱 동봉, dbid 없음)은 자동으로 보호된다" | "`#dbid`가 없는 프레임(서버 미동기 `local:`)은 자동으로 보호된다" |
| `FrameNaming.cs:7` | "DB/번들 프레임을 로컬 편집·복사할 때" | "서버 프레임을 불러와 새로 만들 때" |
| `FrameEditorViewModel.cs:381` | "번들 프레임은 .jpg일 수 있다 → 반드시 LoadImage 경유" | ⭐ **근거를 바꿔야 한다** — it27 이후 카탈로그 프레임은 항상 `.png`다(`LocalFrameStore.Write`가 `.png`로만 쓰고 `Enumerate`가 `*.png`만 읽는다). `.jpg` 대응이 여전히 필요한 이유는 **파일 열기 대화상자**(`FrameEditorView.xaml.cs:59` — `*.png;*.jpg;*.jpeg`)다. 주석을 그 근거로 교체하고 **코드는 불변** |
| `FrameEditPolicy.cs:14,36` | "번들/fallback·게스트=불가" | "`bundle:`(폐기된 출처)·fallback·게스트=불가" — 판정이 살아 있다는 사실을 명시 |
| `FrameOrigin.cs:14,23` | "설치 번들 자산(`bundle:` 접두)" | 폐기 주석 추가(§4.7) |
| `FramePickerViewModel.cs:20,62` | "공용=번들+DB캐시+DB다운로드" | "공용=로컬 캐시+서버 다운로드" |
| `FrameSelectViewModel.cs:109` | "번들(설치 자산)·fallback은 불가" | "`bundle:`(폐기된 출처)·fallback은 불가" |
| `FrameTemplate.cs:18` | "프레임 이미지 URL(Storage) 또는 로컬 번들 경로" | "… 또는 로컬 캐시 경로" |
| `FallbackFrameRenderer.cs:10` | "DB/번들 프레임이 모두 없을 때" | "서버·로컬 캐시 프레임이 모두 없을 때" |
| `ServiceRegistration.cs:141-151` | it26 §3.4 주석(② legacyReadRoot 설명 포함) | 전면 교체(§4.7) |
| `IFrameRepository.cs:32` | "DB/번들 유래 편집은 fork 저장으로 처리한다" | ⚠️ 이미 낡은 서술이다(fork/편집 자체가 D-16로 폐기). **"번들"만 고치지 말고 문장 전체를 현행에 맞춘다** — "프레임 수정 기능은 폐지됐고 재활용은 [기존 프레임 불러오기]의 신규 생성뿐이다" |

### 4.7 동결 주석 문구 (한 글자도 임의로 바꾸지 않는다)

**`FrameOrigin.cs` — `FrameOriginKind.Bundle` 멤버 위:**

```
/// <summary>
/// 폐기된 출처: 설치 번들 자산(`bundle:` 접두). (it27 §4.2)
/// <para>
/// ⚠️ <b>생성 경로는 제거됐다</b>(FrameCatalogService.LoadBundleFrames 폐기) — 이 값을 갖는
/// 프레임을 만드는 코드는 이제 없고, 실제로 그런 파일이 존재할 확률도 0에 가깝다.
/// <b>그래도 삭제하지 않는다</b>: 이 분기가 사라지면 `bundle:` id가 <see cref="DbDefault"/>로
/// 오분류되어 <see cref="FrameEditPolicy.CanDelete"/>가 <b>power에게 삭제를 허용한다</b>
/// (fail-closed → fail-open 반전). 같은 반전이 삭제 ✕ 컨버터 · FrameSelectViewModel.IsDeletable ·
/// ConfirmDelete의 hasServerDoc · FrameCatalogService.DbIdsOf에서 연쇄로 일어난다.
/// </para>
/// <para>
/// ⚠️ <b>보존 근거는 "그런 파일이 있을 수 있다"가 아니다</b> — 그것은 정적 코드 성질,
/// 즉 <b>분기를 지우면 권한 규칙이 느슨해진다</b>는 사실이다(설계 it27 §4.2). 배포·현장 상태와
/// 무관하므로 "이제 그런 파일 없으니 지우자"는 논거로는 이 판정을 뒤집을 수 없다.
/// UserRole.CreatableRoles와 같은 보존 근거다.
/// </para>
/// </summary>
```

**`ServiceRegistration.cs` — `ILocalFrameStore` 등록 위(it26 주석 교체):**

```
// it27 §3.1: 프레임 캐시 루트는 **%ProgramData%\MCPhoto\Frame 하나**다.
//   it26이 읽기 전용 보조 루트로 남겨 둔 {exe}\Frame은 제거했다 — 로컬 프레임은 전부 서버에서
//   재취득 가능한 캐시이므로(FrameCatalogService의 Sync*Cache가 서버를 정본으로 다룬다) 앱 경로를
//   읽을 이유가 없고, 남기면 "앱 경로 완전 제거"가 성립하지 않는다.
//   ⚠️ 그 보조 루트는 "이관 전 설치본이 남긴 캐시"를 위한 것이었는데, 이 제품은 그 시점까지
//      배포된 적이 없어 보호할 대상이 존재하지 않았다.
//   구 위치의 잔재는 인스톨러가 제거 시 정리한다(MCPhoto.iss [UninstallDelete] — 개발·검증 머신용).
```

---

## §5 C부 — 오프라인·기존 설치 영향

### 5.1 프로덕션은 변화가 없다 (⚠️ "기능이 줄어든다"는 오해 차단)

| 주장 | 근거 |
|---|---|
| 번들 프레임은 **이미 배포되지 않는다** | 인스톨러 `[Files]`가 화이트리스트이고 `Frame\`을 담지 않는다(F16, it24 결정) |
| 설치본의 `{app}\Frame`은 번들이 아니라 **서버 다운로드 캐시**다 | 실측 5개가 전부 png+`.slots` 쌍(F15) → `LocalFrameStore` 경로이며 `bundle:`이 아니다 |
| 그 캐시를 못 읽어도 **다시 받는다** | 서버가 기본 프레임을 갖고 있음이 실측됐다(F14). 새 루트에 재캐시된다 |
| 따라서 **손님이 보는 프레임 목록은 그대로다** | 위 3개의 귀결 |

**⭐ rev2에서 이 절은 논증이 필요 없어졌다.** `{app}\Frame`을 가진 설치가 존재하려면 먼저 배포가 있어야 하는데 **배포 이력이 0이다**(F25). 위 4행은 여전히 참이지만 이제는 "만약 배포했더라도 영향이 없었을 것"이라는 **조건부 안심**이고, 실제 상황은 "영향을 받을 설치가 없다"는 **무조건 안심**이다.

> ⛔ rev1이 유일한 예외로 꼽았던 것(운영자가 `{app}\Frame`에 `.slots` 없는 png를 손으로 넣어 쓰던 PC — rev1 U3)은 **운영 PC가 0이므로 소멸했다.** 남는 영향 범위는 인스톨러를 돌려 본 **개발·검증 머신**이고, 회복 절차는 §5.2에 있다. 따라서 **착수 전에 물어야 할 블로킹 질문이 없다.**

### 5.2 개발 환경 — 이미 그 상태이므로 변화가 없다

| 관측 | 근거 |
|---|---|
| 개발 빌드 출력에 `Frame` 폴더가 **없다** | 리포 루트 `Frame/`이 2026-07-23에 삭제되어 csproj 글롭이 0개 매치(F8·F9) + `bin/Debug/net10.0-windows/Frame` 부재 실측 |
| 따라서 개발 환경의 `bundle:` 경로는 **이미 도달 불가**다 | 위 + §1.2 조건 ② 불성립 |
| csproj 항목 제거는 **빌드 산출물을 한 바이트도 바꾸지 않는다** | 위의 귀결. Step 4의 완료 기준이 이것을 관측으로 확인한다 |

**서버 미도달 개발 환경에서 보이는 것**: `LocalFrameStore.LoadPublic()`이 0개 → `ResolveLocalFrames`가 `EnsureFallbackFrame()` → **"기본 프레임" 1개**(1200×1600 흰 배경, 2×2 4슬롯, `%ProgramData%\MCPhoto\cache\fallback_frame.png`에 생성). 촬영·합성은 정상 동작한다(F17).

**개발 편의 대안 판정: 코드를 만들지 않는다.** 문서 안내로 충분하며, 실용 경로는 3개다.

| 방법 | 절차 | 비고 |
|---|---|---|
| ① 서버에서 받기(정식) | 개발 계정으로 로그인 → 목록 열기 → `%ProgramData%\MCPhoto\Frame`에 캐시됨 | 권장 |
| ② 다른 PC에서 **쌍으로 복사** | `%ProgramData%\MCPhoto\Frame`의 `{이름}.png` + `{이름}.slots`를 **둘 다** 복사 | ⭐ **오프라인 개발의 정답.** 서명은 payload만 대상이고 키는 소스 고정 상수라 경로·머신이 달라도 유효하다(F10) |
| ③ fallback으로 검증 | 아무것도 넣지 않고 fallback 1개로 촬영 흐름 검증 | 프레임 렌더 자체를 볼 때 |

⚠️ **png만 넣는 것은 이제 통하지 않는다.** `.slots`가 없으면 `LocalFrameStore`가 프레임으로 인정하지 않고(`:220`), 그것을 집어 주던 유일한 경로가 `LoadBundleFrames`였다. **이것이 이번 변경의 유일한 실질 기능 상실이다**(§0.3 C-3) — §10 R1·§11 F-1에 기록한다. rev2에서 **영향 범위가 현장 부스가 아니라 개발·검증 머신으로 내려갔고**(F25), 위 ②가 실효 대안이므로 R1의 등급도 함께 내려간다.

### 5.3 `{app}\Frame` 고아 캐시 — 인스톨러 판정 (rev2 재작성)

#### 5.3.1 판정: **동작 변경 불요 · 지시행 유지 · 근거 재규정**

사용자 지시("기존 설치 경로 app 경로에 있는 캐시들은 **제거하지 않아도 돼**. 아직 배포된 적이 없어")를 정확히 읽는다.

| 읽기 | 채택 |
|---|---|
| "고아 캐시 정리를 **위한 작업을 새로 하지 않아도 된다**" | ✅ 이 뜻이다. it27은 인스톨러에 **정리 로직을 추가하지 않는다** |
| "이미 있는 삭제 지시행을 **지워라**" | ✕ 이 뜻이 아니다. 지우는 것은 요구되지 않았고, 지우면 손실이 생긴다(아래) |

**이미 있는 삭제 지시행을 남기는 이유 3개**:

1. **테스트가 그것을 단정한다.** `InstallerScriptTests.UninstallDelete_Removes_Frame_Caches`(`:86-94`)가 구·신 두 행의 존재를 요구한다. 지시행을 지우면 **그 단정을 삭제해야 하고**, 그것은 "단정을 지우지 말라"는 이번 작업의 원칙과 충돌한다.
2. **개발·검증 머신에는 그 폴더가 실재한다.** 인스톨러 검증 과정에서 생성됐다(F26). 지시행이 있으면 제거가 정리해 준다 — 배포 0이라는 사실은 그 머신들을 없애 주지 않는다.
3. **유지 비용이 0이다.** 없는 폴더를 지우는 `filesandordirs` 지시는 무동작이다.

#### 5.3.2 지시행 전수 — 하나도 바꾸지 않는다

| 지시행 | 상태 | rev2 판정 |
|---|---|---|
| `Type: filesandordirs; Name: "{app}\Frame"` (`:111`) | 존재 | **유지.** 근거만 "현장 고아 캐시 정리" → **"회귀 방어 + 개발·검증 머신 정리"**로 재규정 |
| `Type: filesandordirs; Name: "{commonappdata}\MCPhoto\Frame"` (`:127`) | 존재 | **유지**(신규 위치 캐시. 재취득 가능하므로 캐시 원칙대로 삭제) |
| `{app}\result` 삭제 행 | **부재**(의도) | ⛔ **부재 유지.** 성격은 "현장 손님 사진 보호" → **"회귀 방어 + 개발·검증 머신 보호"**(§0.3 C-6) |
| `{commonappdata}\MCPhoto\result` 삭제 행 | **부재**(의도) | ⛔ **부재 유지.** 동일 |
| `Type: dirifempty; Name: "{app}"` (`:119`) | 존재 | **유지**(`result`가 있으면 남는다 — 의도된 동작) |
| `[Dirs]`·`[Files]` 전체 | — | **무변경** |

#### 5.3.3 주석 정정 4곳 (⭐ "근거가 틀린 채 남은 규약은 다음 사람이 지운다")

| 파일:줄 | 현재 | 정정 |
|---|---|---|
| `installer/MCPhoto.iss:5-6` | "설치 폴더는 읽기 전용 배포물이며, 앱은 `{app}\Frame`(운영자가 배치하는 번들) · `{app}\branding.ini` 를 읽기만 한다." | "…앱은 `{app}\branding.ini` 를 읽기만 한다. **it27: `{app}\Frame`은 더 이상 읽지 않는다**(번들 개념 폐기)." |
| `installer/MCPhoto.iss:104-107` | "실측(2026-08-12)에서 제거 후 `{app}` 에 아래가 남았다: … `Frame\` … 서버에서 내려받은 프레임(**`FrameCatalogService.BundleFolder`** = `{exe}\Frame`)" | ⚠️ 사라질 심볼 참조를 제거하고 관측 주체를 명확히 — "`Frame\` … it27 이전 버전이 이 폴더에 남긴 프레임 캐시(**개발·검증 머신에서 관측**. 이 제품은 아직 배포된 적이 없어 현장 사례는 없다)" |
| `installer/MCPhoto.iss:110` | "구 프레임 캐시(재취득 가능 — 서버에서 다시 내려받는다). it26 이후 앱은 여기에 쓰지 않고 읽기만 한다." | "구 프레임 캐시(재취득 가능 — 서버에서 다시 내려받는다). **it27 이후 앱은 이 폴더를 읽지도 않는다**(고아). 배포 이력이 없어 현장 대상은 없고, **인스톨러를 돌려 본 개발·검증 머신을 정리하는 것**이 이 행의 실효다 — 무동작이어도 비용이 0이라 남긴다." |
| `installer/MCPhoto.iss:112-116` | "⛔ `{app}\result` 는 **절대 지우지 않는다.** … 그 폴더에는 **손님 사진과 타임랩스**가 들어 있다. … 제거가 고객 자산을 지우는 것은 복구 불가한 사고다." | ⭐ **규약은 유지하고 근거를 재규정한다** — "⛔ `{app}\result` 는 **절대 지우지 않는다.** ⚠️ 이 제품은 아직 배포된 적이 없어 **현장의 손님 사진을 지키는 의미는 아직 없다** — 지금 이 규약이 지키는 것은 ① 인스톨러를 검증한 개발·검증 머신의 `{app}\result` ② **회귀 방어**: 훗날 저장 경로를 `{app}` 쪽으로 되돌리는 변경이 오면 이 규약이 없는 순간 제거가 손님 사진을 지우는 사고가 된다. 출하 후에는 근거가 ①②에서 **현장 자산 보호**로 승격된다. 비용 0이므로 어느 단계에서도 지우지 않는다." |

⚠️ **`InstallerScriptTests`는 무변경이다.** 파서가 주석 줄을 제외하고 완전한 행 패턴만 매칭하므로(`InstallerScriptTests.cs:74-75`), 주석 정정은 단정에 영향이 없다. ⚠️ 위 4곳 모두 **`;`로 시작하는 주석 줄**이며, 지시행과 인접해 있으니 편집 시 행 경계를 반드시 확인한다.

---

## §6 파일별 변경 명세

### 6.1 신규 파일 (1개)

| 파일 | 역할 |
|---|---|
| `tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs` | it27 회귀 잠금. **부재를 단정**하는 정적 검증(리플렉션 + 소스·csproj 스캔). 리포 관례 F23 계승 |

### 6.2 수정 파일

| 파일 | 변경 | 절 |
|---|---|---|
| `src/MCPhoto.Core/Frames/LocalFrameStore.cs` | 보조 루트 제거(ctor 1인자화) + `Roots`·`EnumerateRoots`·`DedupByName` 삭제 + 클래스 주석 교체 | §3.1 |
| `src/MCPhoto.App/ServiceRegistration.cs` | `legacyReadRoot` 인자 제거 + 주석 교체 | §3.1·§4.7 |
| `src/MCPhoto.App/Services/FrameCatalogService.cs` | `BundleFolder`·`LoadBundleFrames`·`LoadOrGenerateSlots`·`GenerateGridSlots`·`ReadImageSize` 삭제 + `ResolveLocalFrames` ③ 제거 + 주석 다수 | §3.2·§4.6 |
| `src/MCPhoto.App/MCPhoto.App.csproj` | `Frame\` 복사 `ItemGroup` 삭제 + 폐기 주석 | §3.3 |
| `src/MCPhoto.Core/Frames/FrameOrigin.cs` | **코드 무변경.** `Bundle` 멤버·클래스 주석에 폐기 표기 | §4.7 |
| `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` | **코드 무변경.** 주석 문구 정정 | §4.6 |
| `src/MCPhoto.Core/Frames/DefaultFrameProvider.cs` | **코드 무변경.** 폐기 주석 추가 | §4.5 |
| `src/MCPhoto.Core/Frames/FrameSyncPlan.cs` | **코드 무변경.** 주석 정정(안전장치 수혜자) | §4.6 |
| `src/MCPhoto.Core/Frames/ILocalFrameStore.cs` | **코드 무변경.** 주석 정정 | §4.6 |
| `src/MCPhoto.Core/Frames/FrameNaming.cs` | **코드 무변경.** 주석 정정 | §4.6 |
| `src/MCPhoto.Core/Frames/IFrameRepository.cs` | **코드 무변경.** 낡은 fork 서술 포함 문장 교체 | §4.6 |
| `src/MCPhoto.Core/Models/FrameTemplate.cs` | **코드 무변경.** 주석 정정 | §4.6 |
| `src/MCPhoto.Capture/FallbackFrameRenderer.cs` | **코드 무변경.** 주석 정정 | §4.6 |
| `src/MCPhoto.App/Converters/CommonConverters.cs` | **코드 무변경.** 주석에 폐기 표기 | §4.3 ③ |
| `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs` | **코드 무변경.** 주석 정정(`:109`) | §4.6 |
| `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs` | **코드 무변경.** 주석 정정 | §4.6 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | **코드 무변경.** `:381` 주석의 `.jpg` 근거 교체 | §4.6 |
| `src/MCPhoto.App/Views/FrameSelectView.xaml` | XAML **주석** 1줄 문구 정정 | §4.6 |
| `installer/MCPhoto.iss` | **지시행 무변경**(삭제 행·부재 규약 전부 유지). 주석 **4곳** 정정 — 3곳은 사라질 심볼·거짓 서술, 1곳은 `{app}\result` 규약의 **근거 재규정** | §5.3 |

### 6.3 삭제 파일 (2개)

| 파일 | 근거 |
|---|---|
| `tests/MCPhoto.Tests/LocalFrameStoreLegacyRootTests.cs` | **검증 대상 기능(보조 루트)이 없어진다** — 재작성이 불가능한 종류다. 11개 단정 중 고유 커버리지 0(§7.2) |
| `tests/MCPhoto.Tests/BundleFrameTests.cs` | **검증 대상 자산(리포 루트 `Frame/`)이 2026-07-23에 이미 없어졌다**(F9). 그 뒤로 `Assert.True(true)` 스킵으로만 통과해 왔다. 실질 단정인 `SlotLayout.IsValid`는 `SlotLayoutTests`가 직접 커버한다 |

⚠️ **삭제와 재작성을 구분한다**(§0.3 B-4). 이 두 파일은 "사실이 바뀌어서" 사라지는 것이 아니라 **검증할 기능·자산 자체가 소멸해서** 사라진다 — 억지로 재작성하면 존재하지 않는 것을 검증하는 테스트가 남는다. 사실이 바뀐 단정(`bundle:` 관련 4파일)은 **삭제하지 않고 §7.4대로 유지·재작성**한다.

⚠️ **삭제 전 확인**: 위 두 파일 외에 `Frame`·`bundle` 관련 테스트를 지우지 않는다. 특히 `LocalFrameStoreTests.Legacy_Plaintext_Slots_Is_Ignored`(`:108`)는 §4.4 증명 ①의 기계적 근거이므로 **반드시 남긴다**.

---

## §7 테스트 전략 (전부 headless — `Window` 인스턴스화 금지)

### 7.1 신규 — `AppPathFrameRemovalTests`

리포 관례 F23을 따른 **정적 검증**이다. `{exe}\Frame`에 실제 파일을 만드는 테스트는 만들지 않는다 — 테스트 실행 폴더를 오염시키고 병렬 실행에서 서로를 깨뜨린다.

| # | 단정 | 방식 | 무엇을 막는가 |
|---|---|---|---|
| T1 | `typeof(LocalFrameStore)`의 public ctor가 **1개**이고 매개변수가 **1개**다 | 리플렉션(소스 스캔보다 강하다) | 보조 루트 매개변수 부활 |
| T2 | `FrameCatalogService.cs` 소스에 `BundleFolder`·`LoadBundleFrames` 식별자가 **없다** | 소스 스캔 | 번들 스캔 부활 |
| T3 | `FrameCatalogService.cs` 소스에 `AppContext.BaseDirectory`가 **없다** | 소스 스캔 | ⭐ 앱 경로 접촉의 재발(가장 넓게 막는 단정) |
| T4 | `ServiceRegistration.cs` 소스에 `legacyReadRoot` 문자열이 **없다** | 소스 스캔 | 배선 부활 |
| T5 | `MCPhoto.App.csproj`에 `Frame\**` 또는 `Link="Frame\` 복사 항목이 **없다** | csproj 텍스트 스캔(주석 줄 제외 — 폐기 주석에 "Frame"이 들어 있다) | 번들 복사 부활 |
| T6 | `FrameOrigin.cs`에 `"bundle:"` 상수가 **있다**(보존 확인) + `FrameOrigin.Classify(new FrameTemplate{Id="bundle:x"})`가 `FrameOriginKind.Bundle`이다 | 리터럴 스캔 + 실행 | ⭐ **폐기 보존 계약** — "안 쓰니까 지우자"는 미래의 정리로 §4.2 판정이 뒤집히는 것을 막는다 |
| T7 | `FrameEditPolicy.CanDelete(bundle:, Admin) == false` **그리고** `FrameSelectViewModel.IsDeletable(bundle:) == false` | 실행 | fail-closed → fail-open 반전 방어(§4.3 ②⑤). ※ T7은 기존 단정과 중복이지만 **it27 판정의 근거를 한 파일에 모아** 그 파일을 지우면 계약이 사라진다는 신호를 준다 |

리포 루트 탐색은 `InstallerScriptTests.FindRepoRoot()`(`:16`)와 같은 방식(상위로 올라가며 마커 탐색)을 재사용한다. 소스 파일이 없으면 **스킵이 아니라 실패**로 처리한다(파일 이동을 못 보고 지나치지 않게).

### 7.2 삭제 12개 단정의 고유 커버리지 — 0

| 삭제 단정 | 대체·처분 |
|---|---|
| `Public_Frame_Only_In_Legacy_Root_Is_Loaded` | 주제 소멸 |
| `Both_Roots_Are_Merged` | 주제 소멸 |
| `Name_Collision_Prefers_New_Root` | 주제 소멸 |
| `Writes_Never_Touch_Legacy_Root` | 주제 소멸 |
| `Missing_Legacy_Root_Is_Skipped` | 주제 소멸 |
| `Null_Legacy_Root_Behaves_Like_Before` | **새 기본 동작 그 자체** → `LocalFrameStoreTests`의 기존 단정 전부가 이것이다 |
| `Same_Path_For_Both_Roots_Does_Not_Duplicate` | 주제 소멸 |
| `User_Frames_From_Legacy_Root_Are_Merged` | 주제 소멸 |
| `User_Frame_Name_Collision_Prefers_New_Root` | 주제 소멸. 계정 내 이름 중복은 `FrameNaming.IsNameAvailable`이 막고 별도 테스트가 있다 |
| `DeleteLocal_Removes_Legacy_Cache_File` | ⭐ **본질은 "`ImageUrl`이 루트 밖을 가리켜도 지운다"** → `LocalFrameStoreTests`로 **이관**(§7.3 T8) |
| `Inspect_Covers_Both_Roots` | 단일 루트 버전은 `Inspect_Reports_Status_Including_Broken_Files`(`:203`)·`Inspect_Includes_Own_Frames_Only`(`:222`)가 이미 커버 |
| `BundleFrameTests.Bundle_Frames_Have_Valid_Slots` | 실질 단정(`SlotLayout.IsValid`)은 `SlotLayoutTests`가 직접 커버. 폴더 부재로 이미 무동작 |

### 7.3 이관 단정 (1개)

| # | 파일 | 단정 |
|---|---|---|
| T8 | `tests/MCPhoto.Tests/LocalFrameStoreTests.cs` | `Delete_Removes_Files_Outside_Root` — 루트 밖 임시 폴더에 png+`.slots`를 만들고 `ImageUrl`을 그 경로로 준 `FrameTemplate`을 `DeleteLocal`에 넘기면 **두 파일이 지워지고 `true`가 반환된다**. `DeleteLocal`이 루트에 갇히지 않는 현행 계약을 고정한다 |

### 7.4 기존 테스트 — 유지·주석 정정

| 파일 | 처분 |
|---|---|
| `FrameOriginTests.cs` | **단정 전부 유지**(`bundle:` 3곳: `:14,41,57`). 클래스 XML 주석에 "⚠️ it27: `bundle:`은 폐기된 출처이며 생성 경로가 없다. 이 단정들은 **그 id를 만나도 읽기 전용으로 판정한다**는 방어 계약이므로 지우지 않는다" 추가 |
| `FrameEditPolicyTests.cs` | **단정 전부 유지**(`Bundle()` 헬퍼 `:19`, 사용 4곳). 헬퍼 XML 주석에 폐기 표기 추가 — "생성 경로 없음, fail-closed 방어 계약" |
| `FrameSelectViewModelTests.cs:158` | **단정 유지.** 인라인 주석에 폐기 표기 추가 |
| `FrameEditorViewModelTests.cs:445,478,495,519,550,630` | ⭐ **성격이 다르다** — `ApplyPickedFrame`는 `Id`를 **읽지 않는다**(§4.4). 즉 이 6곳의 `bundle:*`는 단정 대상이 아니라 **임의 픽스처**다. 판정: **`Id` 값을 서버 문서 id 형태로 바꾼다**(예: `"srv-classic"`) — it27 이후 카탈로그가 줄 수 없는 id를 픽스처로 쓰면 "번들 프레임을 고르는 시나리오"라는 잘못된 인상을 준다. 단정은 하나도 바뀌지 않는다 |
| `FrameEditorViewModelTests.cs:543` 주석 | "번들 프레임은 .jpg일 수 있다" → "**파일 열기 대화상자**가 `.jpg`를 허용한다(`FrameEditorView.xaml.cs:59`)"로 근거 교체. 테스트 자체는 유지 |
| `DefaultFrameTests.cs` | **단정 전부 유지**(우선순위 3개 + fallback 스펙). 클래스 주석에 "②(번들)는 it27에서 폐기 — 순수 함수 열거만 이력으로 남긴다" 추가 |
| `InstallerScriptTests.cs` | **무변경**(F19). 주석 정정도 불요 — 단정 문구가 "구 위치·신 위치" 중립적이다 |
| `FrameCatalogServiceTests.cs` | `BundleFolder` 참조 없음(확인) → **무변경**. 단, Step 2 후 전량 통과 여부가 회귀 게이트다 |
| `LicenseComplianceTests.cs` | csproj를 스캔하지만 대상은 `licenses`·`ffmpeg`다 → **무변경**. ⚠️ Step 4에서 csproj를 편집할 때 라이선스 `ItemGroup`·`Target`을 건드리지 않는지 이 테스트가 감시한다 |

### 7.5 테스트 개수 산술 (U4)

```
기준선(팀리드 보고)         1496
− LocalFrameStoreLegacyRootTests  −11   (Fact 11개, Theory 없음)
− BundleFrameTests                 −1
+ AppPathFrameRemovalTests         +7   (T1~T7)
+ LocalFrameStoreTests T8          +1
─────────────────────────────────────
예상                            1492
```

⚠️ **완료 기준은 절대 개수가 아니라 증감으로 판정한다**(U4). Step 0에서 실제 기준선을 측정하고, 각 단계는 "기존 통과 테스트가 하나도 실패로 바뀌지 않는다 + 그 단계의 신규 단정이 전부 통과한다"로 본다.

---

## §8 문서 갱신 지점

**원칙**: `docs/design/*`은 작성 시점의 판단 기록이므로 **소급 수정하지 않는다**(it26 문서 · `wpf-wbs.md` 포함). `docs/analysis/*`는 **현재 상태 문서**이므로 갱신한다.

| 문서:줄 | 현재 서술 | 갱신 |
|---|---|---|
| `docs/analysis/10-exe-app-architecture.md:48-51` | "번들 자산(App 빌드 산출물)" 절 — "루트 `Frame/**`를 출력 `Frame/`으로 복사(번들 기본 프레임, `MCPhoto.App.csproj:50-55`)" | 그 줄 **삭제** + 폐기 이력 한 줄(§3.4 문구). ⚠️ 인용된 줄 번호(`:50-55`)는 이미 낡았다 — 실제는 `:121-123`이었다. 갱신 시 남은 항목들의 줄 번호도 재확인 |
| `docs/analysis/10-exe-app-architecture.md:296` | `FrameDeleteVisibilityConverter` 설명의 "번들" | "`bundle:`(폐기된 출처)"로 정정 — 판정이 남아 있다는 사실을 유지 |
| `docs/analysis/11-exe-app-features.md:56` | 목록 우선순위 4단계(③ 번들 폴더 이미지) | **3단계로 축소**: ① 로컬 공용 캐시 → ② 서버 `isDefault` 다운로드·캐시 → ③ fallback. 인용 줄 번호도 재확인 |
| `docs/analysis/11-exe-app-features.md:58` | "로컬 스캔·번들 디코드·fallback 생성은 `Task.Run` 경계 안" | "번들 디코드" 제거 |
| `docs/analysis/11-exe-app-features.md:85` | fork 규칙 서술 + `FrameEditPolicy.RequiresFork` 참조 | ⚠️ **이미 낡았다**(`RequiresFork`는 D-16으로 삭제됨). "번들"만 고치지 말고 문장을 현행에 맞춘다 |
| `docs/analysis/11-exe-app-features.md:91-92,98,100,111` | 후보 목록·출처 판정·권한 매트릭스의 "번들" | "`bundle:`(폐기된 출처 — 생성 경로 없음, 방어 판정만 남음)"으로 통일 |
| `docs/analysis/41-local-data-and-file-formats.md:155` | 저장 대상 표의 "번들 자산 프레임 ✕(설치물)" | 행 삭제 + 각주로 폐기 이력 |
| `docs/analysis/41-local-data-and-file-formats.md:198` | 레이아웃 예시 "공용(번들·DB default 캐시)" | "공용(서버 default 캐시·power 공용 생성분)" |
| `docs/analysis/41-local-data-and-file-formats.md:252` | id 접두 규약 "`bundle:` = 번들" | "`bundle:` = **폐기된 출처**(it27 — 생성 경로 없음, 판정만 fail-closed로 보존)" |
| `docs/analysis/41-local-data-and-file-formats.md:383` | 플랫폼 대응 표의 "번들 프레임(읽기 전용) `{실행경로}\Frame\`(운영자 배치 · `FrameCatalogService.BundleFolder`)" | ⭐ **행 삭제**(사라진 심볼 참조) |
| `docs/analysis/41-local-data-and-file-formats.md:393` | "실행 폴더는 … `{실행경로}\Frame`(운영자 배치 번들)·`branding.ini`를 읽기만 한다" | "`{실행경로}\Frame`"을 제외 — `branding.ini`·`licenses\`·`tools\`·`Assets\`만 남는다 |
| `docs/analysis/80-build-and-deployment.md:70` | 산출 파일 목록에 "`Frame\*`(png·slots)" | ⚠️ **이미 거짓이다**(F8·F9로 0개 복사). 항목 삭제 + 이력 각주 |
| `docs/analysis/80-build-and-deployment.md:136,140` | 복사 규칙 표의 "기본 프레임 `..\..\Frame\**\*.*` → 출력 `Frame\`" + 우선순위 ② 언급 | 행 삭제 + §3.4 문구 반영 |
| `docs/analysis/80-build-and-deployment.md:215` | "`Frame\` … `FrameCatalogService.BundleFolder`(`{exe}\Frame`)가 없으면 …" | 심볼 참조 제거 — "기본 프레임은 서버에서 내려받는다. 서버 미도달이면 로컬 캐시, 그것도 없으면 폴백 렌더러" |
| `docs/analysis/80-build-and-deployment.md:238` | it26 규약 문단의 "`{app}\Frame` = 운영자 배치 번들" | 목록에서 **제외** |
| `docs/analysis/05-cross-platform-client-guide.md:158,211` | "실행 폴더 `Frame\` 단일 디렉터리" / "실행폴더 `Frame\`은 읽기 전용 번들·구 캐시" | 이식 대상에서 제외 · 괄호 서술 삭제 |
| `docs/analysis/05-cross-platform-client-guide.md:407` | 체크리스트 "카탈로그 유래(공용 DB·번들·fallback)" | "번들" 제거 |
| `docs/analysis/13-client-behavior-spec.md:308-320` | 우선순위 4단계 코드블록 + 서술 | **3단계로 축소**(위 11번과 동일 규격) |
| `docs/analysis/13-client-behavior-spec.md:341` | 출처 표의 "번들 자산 \| id가 `bundle:` 접두 \| ✕" | 행 **유지**하되 "**폐기된 출처**(생성 경로 없음 — 판정은 fail-closed로 보존)"로 정정. ⭐ 규약 표에서 지우면 미래에 그 접두가 재사용될 수 있다 |
| `docs/analysis/13-client-behavior-spec.md:400` | fork 규칙의 "서버 공용·번들·fallback" | "번들" 제거 |
| `docs/analysis/13-client-behavior-spec.md:431` | "번들·fallback도 복사는 허용" | "fallback도 복사는 허용" |
| `docs/analysis/30-backend-firebase-integration.md:241` | "로컬 전용(`ILocalFrameStore`, **실행 폴더 `Frame\`**)" + `ServiceRegistration.cs:85-87` | ⚠️ 두 가지가 낡았다(위치는 `%ProgramData%`, 줄 번호도 다름). 경로·줄 번호 함께 정정 |
| `docs/analysis/60-auth-accounts-and-roles.md:178` | "프레임 편집" 행 + `FrameEditPolicy.CanEdit` 참조 | ⚠️ **이미 낡았다**(`CanEdit` 삭제됨). "번들"만 고치지 말고 행 전체를 현행에 맞춘다 |
| `docs/analysis/60-auth-accounts-and-roles.md:179` | 삭제 행의 "번들·fallback·빈 Id ×" | "`bundle:`(폐기)·fallback·빈 Id ×" |
| `docs/analysis/61-auth-platform-integration.md:327` | "로컬 캐시·번들·fallback으로 폴백" | "로컬 캐시·fallback으로 폴백" |
| `docs/analysis/70-logging-and-troubleshooting.md:288` | 동일 표현 | 동일 정정 |
| `README.md:49` | 디렉터리 트리의 "`Frame/  # 기본(번들) 프레임 이미지`" | ⭐ **행 삭제**(폴더가 실재하지 않는다 — F8) |

> ⚠️ **무관한 "번들"은 건드리지 않는다**: ffmpeg 번들(`00:93`, `05:152`, `80:5,9,13,62,88,108,112`, `70:162`) · 스토어 앱 번들/번들 ID(`05:137,215`, `41:354`, `61:106`, `90:181`) · 라이선스 고지(`41:477`) · 프론트엔드 번들러(`20:387`) · 폰트(`Typography.xaml:6`). **"프레임 번들"만 대상이다.**
>
> ⚠️ **rev2 주의 — "아직 배포된 적이 없다"를 `docs/analysis/*`에 쓰지 않는다.** 그 사실(F25)은 **곧 만료되는 상태**이므로 현재 상태 문서에 박아 두면 출하 순간 거짓이 된다. 이 사실이 들어가는 곳은 ① 본 설계 문서(시점 기록) ② `installer/MCPhoto.iss`의 주석(§5.3.3 — 규약의 근거를 설명해야 하고, 출하 후 승격됨을 함께 적는다) **두 곳뿐**이다.

---

## §9 실패·부재 경로 전수표 (크래시 금지)

| # | 상황 | 변경 후 거동 | 손님이 보는 것 |
|---|---|---|---|
| E1 | `%ProgramData%\MCPhoto\Frame` 폴더 부재(첫 실행) | `Enumerate`의 `Directory.Exists` 가드로 빈 목록 → 서버 조회 → 캐시 기록 시 `Directory.CreateDirectory` | 정상 목록 |
| E2 | 서버 미도달 + 로컬 캐시 있음 | `catch`가 흡수하고 로컬 캐시로 진행(F13) | 캐시된 프레임 |
| E3 | 서버 미도달 + 로컬 캐시 **없음** | `ResolveLocalFrames` → `EnsureFallbackFrame()` | **"기본 프레임" 1개**(흰 배경 4슬롯) |
| E4 | `{app}\Frame`에 파일이 남아 있음(**개발·검증 머신만** — F25·F26) | **아무도 읽지 않는다**(고아). 인스톨러 제거가 정리 | 영향 없음 |
| E5 | `{app}\Frame`에 `.slots` 없는 png를 손으로 넣어 쓰던 머신 | 그 프레임이 **사라진다**(§0.3 C-3). ⚠️ rev2: 현장 부스가 아니라 **개발·검증 머신에서만** 발생 가능하며, 회복은 §5.2 ②(쌍 복사) | 나머지 프레임 + 필요 시 fallback |
| E6 | 어딘가에 `#dbid=bundle:x`인 유효 v2 `.slots`가 실재(**확률 0에 가깝다** — §4.4) | 목록에 오르고 `Bundle`로 분류 → **삭제 불가, 서버 대조 제외**(종전과 동일) | 종전과 동일 |
| E7 | `%ProgramData%\MCPhoto\Frame`에 비승격 쓰기 실패 | it26에서 이미 해소된 경로(인스톨러 `[Dirs]` `users-modify`, F16). 실패 시 `_cacheFailedIds`가 이번 실행 재시도를 막고 Warning | fallback 또는 부분 목록 |
| E8 | `DeleteLocal`의 `ImageUrl`이 루트 밖을 가리킴 | 경로 기반이라 그대로 지운다(현행 계약, T8이 고정) | 정상 삭제 |

**신규 예외 경로 0개.** 이번 변경은 코드를 **제거**하는 것이므로 새 실패 모드를 만들지 않는다. 스레딩 모델도 불변이다 — 제거되는 `LoadBundleFrames`·`ReadImageSize`는 `Task.Run` 경계 안(`FrameCatalogService.cs:88,272`)에서만 호출됐고, 그 경계 자체는 유지된다. UI 스레드 접촉 지점 변화 없음.

---

## §10 리스크

| # | 리스크 | 완화 |
|---|---|---|
| **R1** | **손으로 프레임을 넣는 통로가 사라진다**(§0.3 C-3). 오프라인 부스에 특정 프레임을 미리 심을 방법이 없어진다. ⚠️ **rev2에서 등급 하향** — 배포 0이라 영향 범위가 개발·검증 머신뿐이다(F25) | ① 사용자 지시가 "완전한 이전"이므로 그대로 진행 ② 실용 대안은 **png+`.slots` 쌍 복사**(§5.2 ②)이며 서명이 머신에 묶이지 않아 실제로 동작한다(F10) ③ 출하 전에 필요해지면 "프레임 시딩 임포트"로 되살린다(§11 F-1) |
| **R2** | ⭐ **최상위 리스크(rev2에서 승격).** 미래의 "쓰지 않는 코드 정리"가 §4.2 판정을 뒤집어 `bundle:` 분기를 지운다 → **권한 완화.** 배포 0이 근거 ⓒ를 무너뜨렸으므로(§4.2 rev2 표) "이제 그런 파일 없으니 지우자"는 논거가 **더 그럴듯해 보인다** | ① T6·T7이 계약을 기계적으로 잠근다 ② `FrameOrigin.cs`의 동결 주석(§4.7)이 **"보존 근거는 파일 존재 가능성이 아니라 권한 반전이다"**를 코드 옆에 명시한다 — 이 문장이 빠지면 방어가 무의미하다 |
| **R2-b** | `{app}\result` 절대 삭제 금지 규약의 **근거가 틀린 채 남아** 다음 사람이 "배포도 안 했는데 뭘 지킨다는 거냐"로 지운다 | §5.3.3의 주석 정정 4번째 항목이 근거를 "회귀 방어 + 개발·검증 머신 보호 → 출하 후 현장 자산 보호로 승격"으로 재규정한다. `InstallerScriptTests.UninstallDelete_Never_Removes_Guest_Photos`가 부재를 기계적으로 잠근다 |
| **R3** | 주석만 고치는 파일이 13개여서 **코드를 실수로 건드릴** 확률이 높다 | 각 파일의 처분에 "**코드 무변경**"을 명시(§6.2). Step 1·5·6은 `dotnet test` 개수·통과가 **변하지 않는 것**이 완료 기준이다(거동 무변경의 기계 증명) |
| **R4** | csproj 편집이 라이선스 복사(`ItemGroup`/`Target`)를 훼손하면 **GPLv3 위반 상태**가 된다 | `LicenseComplianceTests`가 감시(§7.4). Step 4의 검증 명령에 그 테스트 클래스를 명시 |
| **R5** | 문서 갱신 대상 25행 중 **이미 낡은 서술이 5건 섞여 있다**(`10:51` 줄번호 · `11:85` `RequiresFork` · `41:383` 심볼 · `60:178` `CanEdit` · `80:70` 산출 목록) | §8 표에 각각 ⚠️로 표시. **"번들만 고치고 낡은 나머지를 남기지 말라"**를 Step 6의 완료 기준에 넣는다 |
| **R6** | `LocalFrameStore` ctor 시그니처 변경이 테스트 프로젝트 전반을 깨뜨린다 | 의도된 실패다 — 컴파일 오류가 곧 호출부 전수 목록이다. `new LocalFrameStore(root)` 1인자 형태는 기존 테스트가 이미 다수 쓰고 있다(`LocalFrameStoreTests.cs`, `LocalFrameStoreLegacyRootTests.cs:30`) |
| **R7** | 인코딩 사고(한글 주석 다수 편집) | `.cs`는 UTF-8 **BOM 없음** 유지, `.iss`·XAML·문서는 기존 인코딩 유지(F24). 한글이 깨지면 즉시 롤백 |

---

## §11 열린 질문 · 사용자 확인 사항 · 후속 후보

### 사용자 확인 사항 — **없다 (rev2에서 해소)**

rev1은 착수 전 블로킹 질문 1개(UA-1: `.slots` 없는 png를 손으로 넣어 쓰는 운영 PC가 있는가)를 두었다. **배포 이력 0(F25)이 그 질문을 해소했다** — 운영 PC가 존재하지 않는다. 살아 있는 가정 U4·U5는 둘 다 명령으로 기계 검증된다(§2). **따라서 §12를 즉시 착수할 수 있다.**

### ⭐ 부수 확인 — "배포 0"이 영향을 주는 다른 판정 (전수 점검 결과)

`src/`의 "기존 설치 / 구 버전 / 마이그레이션 / legacy" 장치를 전수 조사했다. **it27이 손대는 것은 `legacyReadRoot` 하나이고, 나머지는 근거가 약해졌을 뿐 이번 범위가 아니다.**

| # | 장치 | 근거가 "기존 설치"인가 | 배포 0의 영향 | it27 처분 |
|---|---|---|---|---|
| 1 | `LocalFrameStore`의 `legacyReadRoot` | 예 — 유일 근거 | **근거 완전 소멸** | ⭐ **이번에 제거**(§3.1) |
| 2 | `SettingsPathDiagnostics.cs:11` / `12-…md:147` — ini **실행경로 1순위 유지** 정책 | 근거 2개 중 1개 | 근거 ①("승격으로 운영해 온 기존 설치가 설정을 잃는다") = **소멸**. 근거 ②("개발 실행이 설치본 ini를 공유해 `[Test]` 인증 우회가 전파된다") = **유지**(개발 머신에서 실제 위험) | **이번 범위 밖 — 정책 유지.** 다리 하나로 서 있게 됐으니 주석에 그 사실을 반영하는 것이 정확하다 → §11 F-5 |
| 3 | `App.xaml.cs:96,115-123` — `{exe}\result` 경고 로그 | 예 — "구 버전이 남긴 손님 사진" | 현장 대상 **0** → **개발·검증 머신 진단으로 격하** | **이번 범위 밖**(팀리드 지시로 `result`는 비범위). F-3의 근거가 강해졌다 |
| 4 | `App.xaml.cs:93` 주석 "순서를 바꾸면 기존 설치가 설정을 잃는다" | 예 | 2번과 동일 | F-5 |
| 5 | `AppSettings.cs:50` — `CutCount` 기본값 변경 시 "기존 설치의 명시값 우선" | 예 | 현장 ini **0** → 개발 머신만 | 무해. ini 폴백 규약 자체는 배포와 무관하게 유효 |
| 6 | `ExternalCameraModels.cs:8` — 모델 Id 문자열 변경 금지("기존 설치의 저장값이 미지 Id가 된다") | 예 | **지금은 자유롭게 바꿀 수 있다** | 바꿀 이유 없음. 정보성 기록 |
| 7 | `FrameSigningKey.cs:18-19` — 키 변경 금지("기존 로컬 프레임이 전부 검증 실패") | 예 | 현장 프레임 **0** → **지금이 키를 바꿀 수 있는 마지막 시점**이다 | 바꿀 이유 없음(값에 문제가 없다). ⚠️ 출하 후에는 포맷 v3 + 재서명 마이그레이션이 필요해진다는 사실은 그대로 |
| 8 | `installer/MCPhoto.iss:38-41` — `AppId` GUID 영구 고정 | 예 — "한번 배포하면 절대 바꾸지 않는다" | **기산점 미도래.** 지금은 바꿀 수 있다 | 바꿀 이유 없음. ✅ **주석 정정도 불요** — `:40`이 이미 "출하 **전에** 고정해 둔다"로 현재 상태를 정확히 말한다 |
| 9 | `IniSettingsService.cs:202,206,213` — 키 부재 시 기본값 폴백("마이그레이션 불요") | 아니오 | 영향 없음 | 무변경 |
| 10 | `SlotsFileCodec.cs:112` + `LocalFrameStoreTests.Legacy_Plaintext_Slots_Is_Ignored` — v1 평문 배제 | 부분 | 현장 v1 파일 **0**. 그러나 이 코드는 **변조·손상 파일 거부**라는 보안 계약을 겸한다 | **테스트 유지**(§6.3 경고). 성격이 "구 포맷 호환"에서 "손상·변조 방어"로 정리된다 |

> **결론**: 배포 0은 **it27의 판정을 하나도 뒤집지 않는다** — 오히려 A-1·A-5를 더 강하게 정당화한다. 다만 위 2·3·4번은 **근거가 실제로 약해진 채 주석에 남아 있으므로**, 다음 사람이 "근거가 틀렸으니 정책도 틀렸다"로 오독할 수 있다. 이번 범위가 아니라 후속(F-5)으로 분리한다.

### 후속 후보 (이번 구현 대상 아님 — 기록만)

### 후속 후보 (이번 구현 대상 아님 — 기록만)

| # | 후보 | 근거 |
|---|---|---|
| **F-1** | **프레임 시딩 임포트** — 설정/관리자 화면에서 png를 골라 슬롯을 잡고 **로컬 전용으로** 저장(서버 등록 없이). R1이 실제 불편으로 드러나면 이것이 정답이다 | 번들 폴더가 하던 일을 **UI로 정식화**한다. 편집기가 이미 슬롯 편집·`.slots` 서명 저장을 전부 갖고 있어 신규 코드가 적다 |
| **F-2** | `DefaultFrameProvider.SelectSource`·`FrameSource` 전체 폐기(우선순위 서술을 `FrameCatalogService` 주석으로 단일화) | it20 이후 프로덕션 호출자 0이고 it27로 인자 하나가 영구 `false`다. 지금 하지 않는 이유는 §4.5 |
| **F-3** | `App.xaml.cs`의 `{exe}\result` 경고 로그 **지금 제거**(존속 근거 소멸) | it26이 "구 버전이 남긴 손님 사진 안내"로 넣었는데 **배포 0이라 안내할 대상이 없다**(§11 부수 확인 3번). 이것을 지우면 `result`의 앱 경로 참조가 0이 되어 "앱 경로 완전 제거"가 `result`까지 닫힌다. ⚠️ 이번 범위 밖(팀리드가 `result`를 비범위로 지정) — 다음 이터레이션의 1순위 후보 |
| **F-4** | `IFrameRepository.cs:32` · `docs/analysis/11:85` · `60:178`의 **fork/편집 관련 낡은 서술 일괄 정리** | it27이 "번들" 부분만 고치므로 나머지가 남는다. 별도 정리 이터레이션이 적절하다 |
| **F-5** | **"기존 설치" 근거 주석 일괄 재규정** — `SettingsPathDiagnostics.cs:11` · `App.xaml.cs:93` · `docs/analysis/12:147` · `AppSettings.cs:50` · `ExternalCameraModels.cs:8` · `FrameSigningKey.cs:18-19` | 배포 0으로 이 주석들의 근거가 약해졌거나 아직 발효되지 않았다(§11 부수 확인 2·4·5·6·7). 정책·값은 전부 유지하되 근거를 "출하 후 발효" 또는 "개발 머신 보호"로 정확히 적어야 다음 사람이 정책을 지우지 않는다. **출하 직전에 일괄 재검토하는 것이 가장 효율적**이다 |

---

## §12 WBS — 구현 단계

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 단계는 **self-contained** — 이 문서를 처음 읽는 에이전트가 그 단계만 보고 실행할 수 있다.
> 진행 상태 어휘: `inspected` / `changed locally` / `verified locally` / `committed` / `pushed` / `blocked`.

### Step 0: 기준선 측정 (선행, 코드 변경 0)

- **Context Brief**: MC포토(WPF .NET10)에서 앱 경로 `{exe}\Frame` 사용을 완전히 제거하는 작업이다. 이후 모든 단계가 "테스트가 몇 개 줄고 늘었는가"로 검증되므로, **변경 전 실제 개수**를 먼저 확정한다(설계 §2 U4 — 팀리드 보고 1496은 미검증 수치다).
- **대상 파일**: 없음(측정만).
- **선행 조건**: 없음. 작업 트리가 clean해야 한다(`git status`).
- **구현 내용**: `dotnet build`와 `dotnet test`를 각 1회 실행하고 ① 빌드 오류·경고 수 ② 테스트 통과·실패·건너뜀 수를 기록한다. 기존 알려진 경고는 `GoldenImageTests.cs:225`의 xUnit1031 1건이다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug
  ```
- **완료 기준**:
  - [관측] 빌드 오류 0 · 경고 1건(xUnit1031)만, 테스트 전량 통과. 두 수치를 이후 단계가 비교할 기준선으로 기록했다
  - [non-goal] 파일을 한 개도 수정하지 않았다(`git status`가 clean)
  - [trigger] 없음(측정 전용)
- **롤백**: 불필요(변경 없음).
- [ ] 완료

### Step 1: `bundle:` 출처 범주 폐기 표기 (거동 무변경)

- **Context Brief**: `bundle:` 접두는 "설치 번들 프레임"이라는 프레임 출처 범주이고, 여러 곳에서 **삭제 금지·서버 대조 제외** 판정에 쓰인다(설계 §4.3). 이후 단계에서 그 프레임을 **만드는** 코드를 지우지만, **판정은 남긴다** — 지우면 `bundle:` id가 `DbDefault`로 오분류되어 power에게 삭제가 허용되는 fail-open 반전이 일어난다(설계 §4.2). 이 단계는 그 보존 결정을 코드 주석과 테스트 계약으로 못박는다. **코드 거동은 한 줄도 바뀌지 않는다.**
- **대상 파일**: `src/MCPhoto.Core/Frames/FrameOrigin.cs`(주석) · `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`(주석) · `src/MCPhoto.Core/Frames/DefaultFrameProvider.cs`(주석) · `src/MCPhoto.App/Converters/CommonConverters.cs`(주석) · `tests/MCPhoto.Tests/FrameOriginTests.cs`(주석) · `tests/MCPhoto.Tests/FrameEditPolicyTests.cs`(주석) · `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`(주석) · `tests/MCPhoto.Tests/DefaultFrameTests.cs`(주석) · **신규** `tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs`(T6·T7만)
- **선행 조건**: Step 0의 기준선 수치.
- **구현 내용**:
  1. `FrameOrigin.cs`의 `FrameOriginKind.Bundle` 멤버 주석을 설계 §4.7의 **동결 문구 그대로** 교체한다. 클래스 주석의 "`bundle:`=번들(FrameCatalogService)"도 "폐기된 출처"로 정정한다.
  2. `FrameEditPolicy.cs:14,36`의 "번들/fallback" 표현을 "`bundle:`(폐기된 출처)·fallback"으로 정정한다(설계 §4.6).
  3. `DefaultFrameProvider.cs` 클래스 주석 상단에 설계 §4.5의 **동결 문구 그대로** 폐기 주석을 추가한다.
  4. `CommonConverters.cs:232`의 규칙 서술에 폐기 표기를 넣는다.
  5. 테스트 4파일의 클래스/인라인 주석에 폐기 표기 + "**이 단정은 방어 계약이므로 지우지 않는다**"를 명시한다(설계 §7.4).
  6. 신규 `AppPathFrameRemovalTests.cs`를 만들고 **T6·T7만** 넣는다(T1~T5는 아직 대상 코드가 남아 있어 실패한다). 클래스 XML 주석에 "it27 §4.2 폐기 보존 계약"을 적는다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~AppPathFrameRemovalTests|FullyQualifiedName~FrameOriginTests|FullyQualifiedName~FrameEditPolicyTests"
  dotnet test MCPhoto.sln -c Debug
  ```
- **완료 기준**:
  - [관측] 전체 테스트 통과 수가 Step 0 기준선 **+2**(T6·T7)이고, 실패 0이다
  - [non-goal] `git diff --stat`에서 **`.cs` 실행문 변경이 0줄**이다 — 주석·신규 테스트 파일만 바뀐다. `FrameOrigin.Classify`·`FrameEditPolicy.CanDelete`의 본문은 그대로다
  - [trigger] 없음(주석·테스트 추가 전용). 어떤 런타임 경로도 새로 활성화되지 않는다
- **롤백**: 이 단계 커밋 revert(이후 단계와 독립 — 판정 코드를 건드리지 않았으므로).
- [ ] 완료

### Step 2: `BundleFolder`·`LoadBundleFrames` 제거

- **Context Brief**: `FrameCatalogService`는 프레임 목록의 우선순위를 구현한다. 종전 ③단계는 `{exe}\Frame`(앱 실행 폴더)의 이미지를 스캔해 `bundle:{이름}` 프레임을 만들었다. 이 경로를 제거해 프레임 목록을 "로컬 캐시 → fallback" 2단계로 만든다. 딸린 헬퍼 3개(`LoadOrGenerateSlots`·`GenerateGridSlots`·`ReadImageSize`)의 호출자는 이 경로 안뿐이므로 함께 제거한다(설계 §3.2·U5).
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs` · `tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs`(T2·T3 추가) · `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`(픽스처 id·주석)
- **선행 조건**: Step 1(폐기 보존 계약이 먼저 잠겨야 이 제거가 판정 삭제로 오해되지 않는다). ⚠️ 사용자 확인은 **필요 없다** — 이 제품은 아직 배포된 적이 없어(설계 F25) 영향받을 운영 PC가 0이다. 개발·검증 머신의 `{app}\Frame`에 손으로 넣은 png가 있다면 그 개발자만 영향을 받고 회복 절차는 설계 §5.2에 있다.
- **구현 내용**:
  1. 설계 §3.2 표대로 `BundleFolder` 프로퍼티(`:44-45`)·ctor 대입(`:60`)·`LoadBundleFrames()`(`:444-476`)·`LoadOrGenerateSlots()`(`:478-502`)·`GenerateGridSlots()`(`:504-516`)·`ReadImageSize()`(`:577-582`)를 삭제한다.
  2. `ResolveLocalFrames`의 ③ 블록(`:286-294`)을 삭제해 로컬 0개면 곧바로 `EnsureFallbackFrame()`으로 간다. 설계 §3.2의 변경 후 코드 형태를 따른다.
  3. ⚠️ **`DbIdsOf`의 `bundle:` 필터(`:259`)는 남긴다** — 지우면 `FrameSyncPlan`이 그 id를 삭제 대상으로 잡는 fail-safe 반전이 일어난다(설계 §4.3 ④).
  4. `EnsureFallbackFrame`·`MoveWithRetry`·`FallbackImagePath`·단일 비행 구조는 **손대지 않는다**.
  5. 설계 §4.6 표의 이 파일 주석 8곳을 정정한다.
  6. `FrameEditorViewModelTests`의 `bundle:*` 픽스처 id 6곳(`:445,478,495,519,550,630`)을 서버 문서 id 형태로 바꾸고, `:543` 주석의 `.jpg` 근거를 파일 열기 대화상자로 교체한다(설계 §7.4). **단정은 하나도 바꾸지 않는다.**
  7. `AppPathFrameRemovalTests`에 T2(`BundleFolder`·`LoadBundleFrames` 식별자 부재)·T3(`AppContext.BaseDirectory` 부재)를 추가한다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~FrameCatalogService|FullyQualifiedName~AppPathFrameRemovalTests|FullyQualifiedName~FrameEditorViewModel"
  dotnet test MCPhoto.sln -c Debug
  ```
- **완료 기준**:
  - [관측] 빌드 오류 0 · 신규 경고 0. 전체 통과 수가 Step 1 대비 **+2**(T2·T3), 실패 0. `FrameCatalogService.cs`에 `AppContext.BaseDirectory` 문자열이 0회 등장(T3이 기계 확인)
  - [non-goal] `FrameCatalogServiceTests` 전량이 여전히 통과한다(단일 비행·진행 보고·fallback 생성 거동 불변). `DbIdsOf`의 `bundle:` 필터가 살아 있다. `FrameEditorViewModelTests`의 단정 개수·내용 불변
  - [trigger] 프레임 목록이 fallback으로 떨어지는 것은 **로컬 캐시 0개 + 서버 미도달**일 때뿐이다 — 캐시가 있으면 종전과 동일하게 그 캐시가 나온다
- **롤백**: 이 단계 커밋 revert(Step 3·4와 독립).
- [ ] 완료

### Step 3: `legacyReadRoot` 제거 (`LocalFrameStore` 단일 루트화)

- **Context Brief**: `LocalFrameStore`는 프레임 파일(png + 서명된 `.slots`)의 저장소다. it26이 캐시 루트를 `%ProgramData%\MCPhoto\Frame`로 옮기면서 구 루트 `{exe}\Frame`을 **읽기 전용 보조 루트**로 남겼는데(이관 전 설치본의 자산이 목록에서 사라지지 않게), it27은 그 폴백을 없앤다. 근거 2개: ① **이 제품은 아직 배포된 적이 없어 보호할 대상이 존재하지 않았다**(설계 F25) ② 로컬 프레임은 전부 서버에서 재취득 가능한 캐시다(설계 F12). 보조 루트를 지우면 2루트 병합 장치(`Roots`·`EnumerateRoots`·`DedupByName`)가 전부 불필요해진다.
- **대상 파일**: `src/MCPhoto.Core/Frames/LocalFrameStore.cs` · `src/MCPhoto.App/ServiceRegistration.cs` · **삭제** `tests/MCPhoto.Tests/LocalFrameStoreLegacyRootTests.cs` · `tests/MCPhoto.Tests/LocalFrameStoreTests.cs`(T8 추가) · `tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs`(T1·T4 추가)
- **선행 조건**: 없음(Step 2와 병렬 가능 — 다른 파일·다른 리스크).
- **구현 내용**:
  1. 설계 §3.1.2 표대로 `_legacyRoot` 필드·ctor 매개변수·`Roots()`·`EnumerateRoots`·`DedupByName`을 삭제하고, `LoadPublic`·`LoadUser`·`Inspect`를 단일 루트 형태로 고친다. ctor는 `LocalFrameStore(string rootFolder)` **1인자**가 된다.
  2. 클래스 주석의 it26 §3.4.3 단락을 설계 §3.1.3의 **동결 문구 그대로** 교체한다. `DeleteLocal`의 "구 루트도 지운다" 주석은 경로 기반 사실만 남긴다.
  3. `ServiceRegistration.cs:148-151`을 `new LocalFrameStore(System.IO.Path.Combine(App.DataFolder, "Frame"))`로 바꾸고 주석을 설계 §4.7의 **동결 문구 그대로** 교체한다.
  4. `LocalFrameStoreLegacyRootTests.cs`를 삭제한다. ⚠️ `LocalFrameStoreTests.Legacy_Plaintext_Slots_Is_Ignored`(`:108`)는 **다른 파일이며 반드시 남긴다**(설계 §4.4 증명 ①의 기계적 근거).
  5. `LocalFrameStoreTests`에 T8(`Delete_Removes_Files_Outside_Root` — 설계 §7.3)을 추가한다.
  6. `AppPathFrameRemovalTests`에 T1(ctor 리플렉션: public ctor 1개·매개변수 1개)·T4(`ServiceRegistration.cs`에 `legacyReadRoot` 부재)를 추가한다.
  7. `DeleteLocal`·`PublicFrameNames`·`UserFrameNames`·`Write`·`SaveDefaultFrame`·`SaveUserFrame`·`EnsureFileNameSafe`는 **손대지 않는다**.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~LocalFrameStore|FullyQualifiedName~AppPathFrameRemovalTests|FullyQualifiedName~ServiceRegistration"
  dotnet test MCPhoto.sln -c Debug
  ```
- **완료 기준**:
  - [관측] 빌드 오류 0. `typeof(LocalFrameStore)`의 public ctor가 1개이고 매개변수가 1개임을 T1이 확인. 전체 통과 수가 Step 2 대비 **−11 +3 = −8**(파일 삭제 11, T1·T4·T8 추가 3), 실패 0
  - [non-goal] `LocalFrameStoreTests`의 기존 단정 전량이 통과한다(공용/개인 분리 · 타인 프레임 비노출 · 서명 변조 거부 · v1 평문 무시 · 이름 검증 · `Inspect` 상태 보고 — 하나라도 깨지면 단일 루트화가 열거 규칙을 훼손했다는 뜻이다). 쓰기 경로 코드 무변경
  - [trigger] 프레임 로드는 `%ProgramData%\MCPhoto\Frame`(및 그 `users\{해시}\`)만 대상으로 한다 — 다른 어떤 폴더도 열거 트리거가 되지 않는다
- **롤백**: 이 단계 커밋 revert. 되돌릴 규칙 전문은 it26 설계 §3.4.3과 본 문서 §3.1에 보존돼 있다.
- [ ] 완료

### Step 4: csproj `Frame\` 복사 제거 + `BundleFrameTests` 삭제

- **Context Brief**: `MCPhoto.App.csproj`는 리포 루트 `Frame/**`를 빌드 출력 `Frame\`로 복사하는 항목을 갖고 있다. ⚠️ 그런데 **리포 루트 `Frame/` 폴더는 2026-07-23(커밋 `694c502`)에 삭제되어 존재하지 않는다** — 이 글롭은 그때부터 0개 파일을 매치해 왔다(설계 F8·F9). 따라서 이 제거는 **빌드 산출물을 바꾸지 않는다**. 같은 이유로 그 폴더의 자산 유효성을 검사하던 `BundleFrameTests`도 폴더 부재로 무동작 상태였다.
- **대상 파일**: `src/MCPhoto.App/MCPhoto.App.csproj` · **삭제** `tests/MCPhoto.Tests/BundleFrameTests.cs` · `tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs`(T5 추가)
- **선행 조건**: 없음(Step 2·3과 병렬 가능).
- **구현 내용**:
  1. `MCPhoto.App.csproj:119-124`의 프레임 복사 주석 + `ItemGroup`을 삭제하고, 설계 §3.3의 폐기 주석을 그 자리에 넣는다.
  2. ⛔ **다른 `ItemGroup`·`Target`은 한 줄도 건드리지 않는다** — 특히 `licenses`(`:89-101`)·`CopyLicensesToPublish`(`:105-117`)·ffmpeg(`:61-75`)·`branding.ini.sample`(`:127-129`). 라이선스 복사를 훼손하면 GPLv3 위반 상태가 된다.
  3. `BundleFrameTests.cs`를 삭제한다(설계 §6.3).
  4. `AppPathFrameRemovalTests`에 T5(csproj에 `Frame\**`/`Link="Frame\` 복사 항목 부재 — **주석 줄 제외** 후 판정. 폐기 주석에 "Frame"이 들어 있으므로 단순 `Contains`는 오탐한다)를 추가한다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~LicenseComplianceTests|FullyQualifiedName~AppPathFrameRemovalTests"
  dotnet test MCPhoto.sln -c Debug
  ```
  추가로 빌드 출력을 직접 확인한다(PowerShell): `Test-Path src\MCPhoto.App\bin\Debug\net10.0-windows\Frame` → `False`
- **완료 기준**:
  - [관측] 빌드 오류 0. 출력 폴더에 `Frame` 디렉터리가 없다. 전체 통과 수가 Step 3 대비 **−1 +1 = 0**(파일 삭제 1, T5 추가 1), 실패 0
  - [non-goal] `LicenseComplianceTests` 전량 통과 — `licenses\` 복사 규칙·`tools\ffmpeg` 복사 규칙·고지 4종이 그대로다. 출력 폴더의 `licenses\`·`tools\ffmpeg\ffmpeg.exe`·`branding.ini.sample`이 여전히 존재한다
  - [trigger] 없음(빌드 구성 변경 전용)
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 5: 인스톨러 주석 정정 (지시행 무변경 · 근거 재규정)

- **Context Brief**: `installer/MCPhoto.iss`(Inno Setup)는 제거 시 `{app}\Frame`을 이미 지운다 — it26이 넣었고 `InstallerScriptTests`가 잠근다(설계 F18·F19). **이 제품은 아직 배포된 적이 없으므로**(설계 F25) 그 삭제 행이 실제로 정리하는 대상은 **인스톨러를 돌려 본 개발·검증 머신**이다. 사용자도 "고아 캐시는 제거하지 않아도 된다"고 확인했다 → **인스톨러 동작을 바꾸지 않는다**(지시행 유지, 유지 비용 0). 이 단계가 하는 일은 **주석 4곳의 정정**이다: 3곳은 사라진 심볼(`FrameCatalogService.BundleFolder`) 참조와 "앱은 그 폴더를 읽는다"는 거짓 서술이고, 4번째는 `{app}\result` 절대 삭제 금지 규약의 **근거 재규정**이다 — 배포 0이라 "현장 손님 사진 보호"는 아직 참이 아니고, 실제 근거는 "개발·검증 머신 보호 + 회귀 방어"다. ⛔ **규약 자체는 절대 불변이다** — 근거가 틀린 채 남으면 다음 사람이 규약을 지우고, 훗날 저장 경로가 `{app}` 쪽으로 되돌아갈 때 제거가 손님 사진을 지우는 사고가 되살아난다.
- **대상 파일**: `installer/MCPhoto.iss`(**주석 줄만** — `;`로 시작하는 줄)
- **선행 조건**: Step 2·3(주석이 서술하는 코드 상태가 실제로 그렇게 되어 있어야 정정문이 참이 된다).
- **구현 내용**: 설계 §5.3.3 표의 **4행**(`:5-6` · `:104-107` · `:110` · `:112-116`)을 표의 정정 문구대로 고친다. **`[Setup]`·`[Files]`·`[Dirs]`·`[Icons]`·`[Tasks]`·`[Run]`·`[UninstallDelete]`의 지시행은 한 줄도 바꾸지 않는다** — 설계 §5.3.2의 지시행 전수표가 "무엇이 있어야 하고 무엇이 없어야 하는지"를 명시한다. 파일의 기존 인코딩·개행을 유지한다.
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~InstallerScriptTests"
  git diff -U0 installer/MCPhoto.iss
  ```
- **완료 기준**:
  - [관측] `InstallerScriptTests` 4개 전량 통과(`Dirs_Creates_Writable_Result_And_Frame_Folders` · `UninstallDelete_Never_Removes_Guest_Photos` · `UninstallDelete_Removes_Frame_Caches` · `Files_Whitelist_Still_Excludes_Runtime_Artifacts`). 전체 통과 수는 Step 4와 **동일**. `{app}\result` 규약 주석이 "회귀 방어 + 개발·검증 머신 보호, 출하 후 현장 자산 보호로 승격"을 말한다
  - [non-goal] `git diff -U0 installer/MCPhoto.iss`의 **추가·삭제 줄이 전부 `;`로 시작한다**(주석 외 변경 0). 특히 `Type: filesandordirs; Name: "{app}\Frame"` 행이 **그대로 있고**, `{app}\result`·`{commonappdata}\MCPhoto\result` 삭제 행은 **여전히 없다**. `AppId` GUID 불변
  - [trigger] 없음(주석 전용). 인스톨러를 다시 컴파일하지 않아도 되며, 컴파일하더라도 산출물 동작이 같다
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step 6: 나머지 주석 정정 + 문서 갱신 + 최종 전량 검증

- **Context Brief**: it27로 "프레임 번들"이라는 개념이 폐기됐다. 코드 주석과 `docs/analysis/*`(현재 상태 문서)에 그 개념이 25곳 이상 남아 있어 정리한다. ⚠️ `docs/design/*`과 `docs/design/wpf-wbs.md`는 작성 시점의 판단 기록이므로 **소급 수정하지 않는다**(리포 폐기 관례). ⚠️ ffmpeg 번들·스토어 앱 번들·라이선스 고지·폰트 문맥의 "번들"은 **무관하므로 건드리지 않는다**.
- **대상 파일**: 코드 주석 — `ILocalFrameStore.cs` · `FrameSyncPlan.cs` · `FrameNaming.cs` · `IFrameRepository.cs` · `FrameTemplate.cs` · `FallbackFrameRenderer.cs` · `FrameSelectViewModel.cs` · `FramePickerViewModel.cs` · `FrameEditorViewModel.cs` · `Views/FrameSelectView.xaml`. 문서 — `docs/analysis/{05,10,11,13,30,41,60,61,70,80}` · `README.md`
- **선행 조건**: Step 1~5 전부(문서가 서술하는 상태가 실제로 그렇게 되어 있어야 한다).
- **구현 내용**:
  1. 설계 §4.6 표의 잔여 코드 주석을 정정한다. **전부 주석·XML 문서 주석이며 실행문은 0줄 변경**이다.
  2. `Views/FrameSelectView.xaml:36`의 XAML 주석 문구를 정정한다(주석 외 변경 금지).
  3. 설계 §8 표의 문서 25행을 갱신한다. ⚠️ ⚠️표시가 붙은 5건은 **"번들"만 고치고 낡은 나머지를 남기지 말라** — 줄 번호 오류(`10:51`) · 삭제된 심볼(`11:85` `RequiresFork`, `41:383` `BundleFolder`, `60:178` `CanEdit`) · 거짓 산출 목록(`80:70`)을 함께 바로잡는다.
  4. `README.md:49`의 `Frame/` 트리 행을 삭제한다(폴더가 실재하지 않는다).
  5. 문서의 `파일:줄` 인용은 **이번 변경으로 이동한 것들이 있으므로 인용 전 실제 줄을 확인**한다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Debug
  dotnet test MCPhoto.sln -c Debug
  ```
  잔재 확인(Bash): `grep -rn "BundleFolder\|LoadBundleFrames\|legacyReadRoot" src/ tests/ installer/ docs/analysis/ README.md` → **결과 0줄**
  프레임 문맥 "번들" 잔재 확인: `grep -rn "번들" src/ docs/analysis/ README.md` 결과에서 프레임 문맥이 0건(ffmpeg·스토어·라이선스·폰트 문맥만 남는다)
- **완료 기준**:
  - [관측] 빌드 오류 0 · 경고 1건(기존 xUnit1031)만. `dotnet test` 전량 통과. 위 `grep`이 `BundleFolder`·`LoadBundleFrames`·`legacyReadRoot`를 **0건** 반환. `docs/analysis`·`README.md`에 프레임 문맥의 "번들" 서술이 남지 않았다
  - [non-goal] 통과 테스트 수가 Step 5와 **동일**하다(주석·문서만 바꿨으므로). `docs/design/*`(it26 문서·`wpf-wbs.md` 포함)에 **변경이 0건**이다 — 단, 본 it27 설계 문서 자신은 예외. ffmpeg·라이선스·스토어·폰트 문맥의 "번들" 표현이 그대로 남아 있다
  - [trigger] 없음(주석·문서 전용). 앱 거동에 영향을 주는 변경이 이 단계에 없다
- **롤백**: 이 단계 커밋 revert(코드 거동과 무관하므로 단독 revert 안전).
- [ ] 완료

### 완결성 게이트 확인 (architect 자체 검사)

- [x] 검증된 사실(§1 F1~F27) / 미검증 가정(§2 U1·U4·U5 — U2·U3은 rev2에서 소멸) 목록이 분리되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다 — U1=설계로 무해화(Step 1 계약) · ~~U2·U3=rev2 소멸(F25)~~ · U4=Step 0 · U5=Step 2
- [x] **사람에게 물어야 하는 블로킹 질문이 0개다**(rev1 UA-1은 F25로 해소) → 즉시 착수 가능
- [x] 모든 단계에 7개 필수 필드가 채워져 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이다(관측 / non-goal / trigger)
- [x] 검증 명령이 자동 실행 가능한 형태다(`dotnet build` · `dotnet test --filter` · `grep` · `Test-Path`)
- [x] 단계 수 7개(Step 0~6)로 3~12 범위 안이다
- [x] 각 단계가 독립 검증 가능하고 단일 리스크다(Step 2·3·4는 서로 병렬 가능)
