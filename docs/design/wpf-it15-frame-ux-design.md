# WPF it15 — 프레임 화면 UX 설계 (F1 로컬 전용 안내 / F2 기존 프레임 불러오기)

| 항목 | 값 |
|------|-----|
| 문서 | it15 지시 5번(프레임 선택 페이지) = 브리프 §3.5 **F1 · F2** 설계 |
| 프로젝트 루트 | `C:\STUDY\PROJECT\PhotoBooth` |
| 작성 | wpf-architect (설계 전용 — 코드 미구현) |
| 입력 브리프 | it15 요구사항 브리프 §1-5 / §3.5 |
| 선행 설계 | `docs/design/wpf-frame-edit-completion-design.md`(item2: 역할×출처 편집 권한·DB 업데이트 팝업·diff), `wpf-it11-deferred-features-design.md`, `wpf-architecture.md` |
| 무회귀 기준선 | `dotnet build -c Release` = **경고 0 / 오류 0**, `dotnet test` = **675 / 675 통과** |

---

## §0. 개요

### 0.1 범위

이 문서는 it15 이터레이션 중 **프레임 화면 2건만** 다룬다.

| ID | 요구 | 한 줄 요약 |
|----|------|-----------|
| **F1** | "해당 PC에서만 적용됩니다." 안내 | 편집기 상단 **상시 배너** + it2 "DB도 업데이트" 경로 **클라이언트에서 제거** + DB/번들 출처 편집 시 **이름 분기(fork) 저장** |
| **F2** | "기존 프레임 불러오기" | "이미지 불러오기" 아래 버튼 → **편집기 내 오버레이 모달**(썸네일 그리드) → 선택 프레임의 이미지·슬롯을 **메모리 복사**로 새 편집 세션에 주입 |

**이 문서가 다루지 않는 것(다른 설계 문서 소관)**
- 인증/계정/DB 스키마/진단 페이지(브리프 지시 1~4)
- 레거시 Firebase 직결 경로 제거(브리프 D1) — 이 문서는 **제거 완료를 전제**로만 설계하고, 제거 작업 자체는 설계하지 않는다.

### 0.2 전제 (병렬 설계와의 경계)

- 브리프 확정 **D1**에 따라 이번 이터레이션에서 `MCPhoto.Firebase.FrameRepository`(직결)가 삭제되고 **`MCPhoto.Http.HttpFrameRepository`만 남는다**. 본 설계의 `IFrameRepository` 변경은 이 전제 위에서 기술한다.
- 브리프 §3.1에 따라 `User.Password` 필드가 삭제된다. **기존 프레임 테스트가 `Password`를 세팅**하므로(§1.9) 충돌 지점을 §2에 가정으로 등재하고 WBS Step에 조정 지시를 넣는다.

### 0.3 핵심 결정 요약

| # | 결정 | 근거(요약) |
|---|------|-----------|
| **F1-D1** ⟳ | 안내 노출 지점 = **FrameEditorView 상단 배너 단일 지점**. 진입 확인 팝업 신설 안 함. **노출 조건 = 기존 프레임 수정 세션에서만**(`Visibility = !IsCreateMode`) | 키오스크에서 클릭 단계 추가는 UX 저하. "편집 시 노출" 요구는 편집기 진입 즉시 보이는 배너로 충족. 단일 진실 원천. **⟳ 2026-07-28 정정**(§0.5) — 신규 생성은 기존 로직 그대로라 배너 문구가 거짓이 되어 조건부로 바뀜 |
| **F1-D2** | it2 "로컬만 / DB도 업데이트" 팝업 + `SaveToDb` 경로 **클라이언트에서 전면 제거** | 정책과 정면 충돌 — 팝업을 남기면 배너 문구가 거짓이 된다 |
| **F1-D3** | 서버 라우트 `PUT /frames/:id`는 **유지**(앱 미호출, 관리 전용으로 문서 표기) | 브리프 §3.3 제거 라우트 목록에 없음(사용자 확정 범위 밖) + 프레임 DB 갱신의 유일한 경로 + 제거 시 firebase-contract·jest 대규모 변경으로 it15 범위 팽창 |
| **F1-D4** | **DbDefault/Bundle 출처 편집 = fork 저장**(원본 파일 불변, `{원본이름} 사본` 신규 저장, `#dbid` 미기록). UserLocal 편집은 현행대로 같은 이름 덮어쓰기 | 사용자 원문 "**이름으로 구분지어서** db에서 다운로드 받는 것에 영향 최소화" = 편집본을 원본과 다른 이름으로 분리. 원본 이름이 로컬 공용 집합에 남으므로 `FrameCatalogService`의 이름 기준 dedup이 유지되어 **재다운로드가 발생하지 않는다** |
| **F1-D5** | 저장 **스코프**(공용 vs 개인)는 현행 역할 규칙 유지 — power=공용 `{이름}.png`, user=개인 `{계정}_{이름}.png` | 요구는 "서버 반영 금지"이지 "역할 정책 변경"이 아니다. 스코프까지 바꾸면 게스트 노출 프레임 운영 경로가 사라진다 |
| **F1-D6** ⟳ | 배너는 **정책 문구(문구 자체는 고정, 편집 세션에서만 노출)**, 저장 버튼 위 **`SaveScopeNotice` 캡션은 이번 저장의 실제 결과**를 동적 안내(**전 모드 상시**) | power **신규 생성**은 여전히 DB 등록(`SaveAsync`)이므로 배너만으로는 부정확해진다. 정책/결과를 분리해 정직성 확보. **⟳ 2026-07-28 정정** — 신규 생성 화면에서는 배너를 숨기고 `SaveScopeNotice`가 단독으로 결과를 안내한다(두 문장이 서로 모순되지 않게) |
| **F2-D1** | 선택 모달 = **새 `Window` 아님. FrameEditorView 내부 오버레이 Grid** | 프로젝트 관례(삭제 확인·DB 팝업 모두 `Brush.Scrim` 오버레이) + 키오스크 전체화면 + **테스트에서 Window를 new 할 수 없는 제약** 회피 |
| **F2-D2** | **다이얼로그 서비스 추상화 도입 안 함**(`IPinPromptDialogService` 패턴 미적용) | 그 패턴은 셸 밖에서 `Window`를 띄워야 할 때의 우회책. 여기선 편집기 VM 자신의 상태 프로퍼티로 충분 → 불필요한 인터페이스 증가 회피 |
| **F2-D3** | 썸네일 그리드는 **복제가 아니라 리소스 공유** — `Themes/Controls.xaml`에 `FrameCard.ItemContainer`(Style) · `FrameCard.Content`(DataTemplate) 추출, FrameSelect는 여기에 삭제 ✕ 버튼만 합성 | 두 화면의 카드 시각이 갈라지는 것을 막고, 삭제 커맨드 바인딩이 피커로 새지 않게 분리 |
| **F2-D4** | 이미지 복사는 **byte[] 메모리 복사만**. 임시 파일을 만들지 않는다 | "저장 전 취소 시 임시 파일 정리" 요구를 **임시 파일 부재**로 해소 — 정리 로직이 없으므로 정리 누락 버그도 없다 |
| **F2-D5** | 썸네일 `DecodePixelWidth` 최적화 **미도입**, `FilePathToImageConverter` **무수정** | it9에서 잡은 파일 잠금(OnLoad+IgnoreImageCache) 회귀를 재도입하지 않는다. 후보 프레임 수 상한이 낮아(공용 소수 + 계정당 최대 10) 실익 대비 위험이 크다 |
| **F2-D6** | 버튼 노출 = **생성 모드 전용**(`IsCreateMode`) | 사용자 원문이 "프레임 **생성** 시"로 한정. 편집 모드에서 불러오면 편집 대상 정체성(fork/덮어쓰기 판정)이 흔들려 저장 규칙이 모호해진다 |

### 0.4 인코딩·개행 규약 (구현 필수)

- `.cs` = **UTF-8 (BOM 없음)**, `.xaml` = 기존 파일 인코딩 유지. 신규 `.cs`도 no BOM.
- 검증: `.claude/agent-memory/wpf-developer/encoding-verify-method.md`
- 파일 수정 시 인코딩을 UTF-8 with BOM 등으로 바꾸지 말 것(불필요 diff·mojibake 위험).

### 0.5 요구사항 정정 이력 (2026-07-28, F1 배너 노출 조건)

**사용자 원문(정정)**: "F1 배너에 대해서 내가 로직을 바꾸려고 한거야. power 계정을 포함한 모든 계정에서
**기존 프레임 수정 시**에 '해당 PC에서만' 변경될거야. **신규 프레임 생성 시에는 기존 로직 그대로** 갈거야."

| 항목 | 최초 설계 | 정정 후 |
|---|---|---|
| 배너 노출 조건 | 역할·출처·생성/편집 모드 **무관 상시** (Visibility 바인딩 없음) | **기존 프레임 수정 세션에서만** — `Visibility={Binding IsCreateMode, Converter={StaticResource InverseBoolToVis}}` |
| 배너 문구 | 변경 없음 | **변경 없음**(현행 유지) |
| 저장 로직 | 편집=로컬 전용(fork/덮어쓰기), power 신규=DB 등록 | **변경 없음** — 최초 설계가 이미 정정된 요구와 일치 |
| `SaveScopeNotice` | 전 모드 상시 | **변경 없음**(전 모드 상시). 신규 생성 화면에서는 이것이 결과 안내의 단독 수단 |

**정정 사유**: 최초 설계의 상시 배너는 **power 신규 생성 화면에서 "해당 PC에서만 적용됩니다"라고 말하는데
실제 저장은 `IFrameRepository.SaveAsync`로 서버에 등록**되어 문구가 거짓이 되고, 같은 화면의
`SaveScopeNotice`("…공용 기본 프레임으로 서버에 등록됩니다")와 정면으로 모순됐다.
F1-D6가 "정책/결과 분리"로 이 모순을 완화하려 했으나, 한 화면에 상충하는 두 문장을 함께 두는 구조 자체가 문제였다.

**F2와의 상호작용**: F2 "기존 프레임 불러오기"로 카탈로그 프레임을 불러온 세션은 정체성이 "새 프레임"이고
(`ApplyPickedFrame`이 `_isEditing`을 건드리지 않음 → `IsCreateMode` 유지) 저장이 fork로 원본을 보존하므로
**배너는 계속 숨김이 맞다**. 이 세션의 안내는 `SaveScopeNotice`의 fork 문구("원본은 그대로 두고…")가 담당한다.

**레이아웃 주의**: 배너 행(`RowDefinition`)에 `MinHeight="88"`을 둔다 — 배너가 숨어 행 높이가 0이 되면
콘텐츠가 상단 바 오프셋(다른 화면의 상단 margin/padding 88과 동일 관례) 안으로 파고든다.

**회귀 방어**: `XamlResourceTests.FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode`(배너 Border의
Visibility 게이트 + 행 MinHeight를 소스 텍스트로 정적 검증) +
`FrameEditorViewModelTests.IsCreateMode_Gates_LocalOnly_Banner`(4가지 세션 형태의 `IsCreateMode` 값 고정).

---

## §1. 검증된 사실 (verified facts — 모두 코드 직접 확인)

### 1.1 프레임 출처 판정 (순수 함수, 이미 존재)

`src/MCPhoto.Core/Frames/FrameOrigin.cs:33-51`

- `Classify(frame)` 우선순위: `bundle:` → `fallback`/빈 Id → `local:` → 그 외 = `DbDefault`
- `IsOwnedLocal(frame, userId)` = `UserLocal` && `frame.UserId == userId`
- `IsDbDefault(frame)` = `DbDefault` && `frame.IsDefault`
- 열거형: `FrameOriginKind.{UserLocal, DbDefault, Bundle, Fallback}` (`FrameOrigin.cs:6-19`)

### 1.2 편집 권한 정책

`src/MCPhoto.Core/Frames/FrameEditPolicy.cs:15-32`

- `CanEdit(frame, role, userId)`: 게스트 불가 / `UserLocal`→본인만 / `DbDefault`→`role.IsPower()` / 번들·fallback 불가
- `RequiresDbUpdatePrompt(frame, role)` = `role.IsPower() && FrameOrigin.IsDbDefault(frame)` — **F1에서 제거 대상**

### 1.3 로컬 저장소 파일 규약

`src/MCPhoto.Core/Frames/LocalFrameStore.cs`

- 루트 = 실행 폴더 `Frame\` (번들 + 파워캐시 + user 공존), 생성자 `:19`
- 공용 = `{이름}.png` (접두 없음), user 전용 = `{계정}_{이름}.png` — `WriteFrame():27-53`
- `SaveLocal(frame, png, ownerName)`: `ownerName is null`이면 공용 + **`dbId = frame.Id` 기록**, 아니면 user 전용 + dbId 없음 (`:21-22`)
- `.slots` 포맷: `#imagesize=W,H` / `#dbid=...`(공용 캐시만) / `index,x,y,w,h` — `SerializeSlots():143-153`, `ParseSlots():156-192`
- `LoadPublic()`은 **파일명에 `_`가 없으면 공용**으로 판정 (`:59`, `:77`) — 이름 자체에 `_`가 있으면 공용 목록에서 탈락(§1.5 함정)
- `EnumerateFrames()`: `#dbid` 있으면 그 값이 `Id`, 없으면 `local:{파일명}` (`:118`)
- `EnsureFileNameSafe()`는 **파일시스템 금지문자만 거부**(`Path.GetInvalidFileNameChars()`), sanitize 없음 (`:134-140`). `_`·공백·괄호는 허용
- `DeleteLocal()`은 png 실제 소멸 여부로 성공을 정직 반환 (`:82-94`)

### 1.4 카탈로그 로딩 & 이름 기준 dedup

`src/MCPhoto.App/Services/FrameCatalogService.cs`

- `GetDefaultFramesAsync()`: ① `LoadPublic()` → ② DB `isDefault` 중 **`PublicFrameNames()`에 이름이 없는 것만** 다운로드·`CacheFromDb` → ③ 번들 폴더 → ④ fallback (`:50-98`)
- 동시 호출 직렬화용 `SemaphoreSlim _defaultFramesGate` (`:24`, `:53`, `:96`) — 시작 prefetch ↔ FrameSelect 진입 경합 방지
- `GetUserFramesAsync(userId)` = `_localStore.LoadUser(userId)` 만. **DB 미조회** (`:101-109`)
- 다운로드는 `File.Exists(url)`이면 로컬 읽기, `http`면 HTTP GET (`:147-153`)

> **F1-D4의 근거**: dedup 키가 **이름**이므로, 원본 이름의 공용 파일이 로컬에 남아 있는 한 그 프레임은 다시 다운로드되지 않는다. fork(다른 이름) 저장은 원본 파일을 건드리지 않으므로 재다운로드가 발생하지 않는다.

### 1.5 기본 프레임 이름 `_` 함정 (기존 규약)

`FrameCatalogService.cs:118-122` — 이름에 `_`가 포함된 DB 기본 프레임은 로컬 공용 규약(`_` = user 접두)과 충돌해 **`LoadPublic`/`PublicFrameNames`에서 제외 → 매 실행 재다운로드**된다. 현재는 경고 로그만 남긴다.
→ **fork 이름 생성 규칙은 `_`를 새로 도입하지 않는다**(§3.4).

### 1.6 편집기 VM 현행 구조

`src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`

- 상태: `_isEditing`/`_editingFrameId`/`_editingFrame`/`_suppressArrange` (`:29-37`)
- it2 diff 스냅샷: `_originalImageBytes`/`_originalSlots`/`_originalName` (`:34-36`) — **F1에서 제거 대상**
- DB 팝업 상태: `IsDbUpdatePromptVisible`/`DbUpdateNotice`/`DbUpdateNoticeIsError` (`:50-54`) — **제거 대상**
- `LoadImage(path)` (`:88-132`): 확장자·10MB 검사 → OpenCV 디코드 → `FrameImageValidator.ScaledSize`로 **장변 4000 초과 시 축소** → PNG 재인코딩 → `FrameWidth/Height` 세팅 → `ArrangeSlots()` (자동 배치로 기존 슬롯을 덮어씀)
- `LoadForEdit(frame)` (`:138-184`): `frame.ImageUrl`을 `File.ReadAllBytes`(로컬 PNG 전제) → `_suppressArrange`로 자동배치 억제 → `_baseSlots`에 원본 슬롯 로드 → `SlotScalePercent=100` → `ApplyScale()`
- `Save()` (`:268-335`): `RequiresDbUpdatePrompt`면 **팝업 띄우고 저장 보류**(`:280-286`) → power=`_repository.SaveAsync` + 로컬 캐시(`:293-308`), user=`_localStore.SaveLocal(ownerName: user.Id)`(`:309-321`)
- `SaveLocalOnly()`/`SaveToDb()`/`CancelDbUpdatePrompt()`/`BuildDbFrame()`/`EditingServerId()` (`:259-442`) — **전부 제거 대상**
- `GoToFrameSelectAsync()` (`:423-427`): 전환 실패가 저장 결과 안내를 뒤엎지 않도록 분리 — **유지·재사용**
- VM은 **Transient 등록**이라 진입마다 새 인스턴스(주석 `:27-28`, `ServiceRegistration.cs:217`)

### 1.7 편집기 View 현행 구조

`src/MCPhoto.App/Views/FrameEditorView.xaml`

- 2컬럼 Grid(`*` / `320`). 좌 = 캔버스 카드(`Margin="40,88,20,40"`), 우 = 컨트롤 패널(`Margin="0,88,40,40"`)
- "이미지 불러오기" 버튼 = **코드비하인드 `Click="OnLoadImage"`**(`:33-34`), 핸들러는 `FrameEditorView.xaml.cs:52-65`에서 `OpenFileDialog` 사용
- DB 업데이트 팝업 오버레이 = `Grid.ColumnSpan="2"` + `Brush.Scrim` + `IsDbUpdatePromptVisible` 바인딩 (`:89-116`) — **제거 대상**
- 코드비하인드는 슬롯 드래그 렌더링 전담(`EditorTransform` 순수 변환 사용, `FrameEditorView.xaml.cs:70-153`)

### 1.8 프레임 선택 View 썸네일 그리드

`src/MCPhoto.App/Views/FrameSelectView.xaml`

- `ListBox` + `WrapPanel` ItemsPanel(`:21-25`) → **UI 가상화 꺼짐**(현행 상태)
- `ItemContainerStyle`: `ListBoxItem` ControlTemplate, 선택 시 `Brush.Accent` 테두리 + `Shadow.Card` (`:27-50`)
- `ItemTemplate`: 200×280 Grid = `Image`(`FilePathToImage` 컨버터) + 슬롯 미리보기 `Viewbox`/`ItemsControl`/`Canvas` + 이름 바(`Brush.Scrim`) + 삭제 ✕ `Button`(`FrameDeleteVis` MultiBinding) (`:51-103`)
- **삭제 ✕ 버튼의 MultiBinding은 `DataContext.CanDeleteFrames`/`DataContext.IsPower`를 ListBox 조상에서 찾는다**(`:94-98`) — 이 템플릿을 그대로 피커에 재사용하면 피커 VM에 없는 프로퍼티를 참조하게 된다 → §4.3에서 분리 설계

### 1.9 테스트 현황 (변경 영향 범위)

| 파일 | 내용 | it15 영향 |
|------|------|-----------|
| `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs` | 287줄. DB 팝업/ diff 플로우 테스트 6개(`:167-285`) | **삭제 + 대체** |
| `tests/MCPhoto.Tests/FrameEditPolicyTests.cs` | 90줄. `RequiresDbUpdatePrompt` 4개(`:74-89`) | **4개 삭제** |
| `tests/MCPhoto.Tests/FrameDiffTests.cs` | 92줄 | **파일 삭제** |
| `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs` | 294줄. 삭제·편집권한 | 유지(스텁 시그니처만 조정) |
| `tests/MCPhoto.Tests/FrameOriginTests.cs` / `LocalFrameStoreTests.cs` / `SlotLayoutTests.cs` / `EditorTransformTests.cs` | 순수 로직 | 유지 + 보강 |
| `tests/MCPhoto.Tests/XamlResourceTests.cs` | 테마·View StaticResource 정적 검증 | **View 목록·appKeys 확장** |

- ⚠️ `FrameEditorViewModelTests.cs:92` 가 `new User { Id = "u1", Password = "pw", Role = role }` 로 **`User.Password`를 사용**한다. 브리프 §3.1에서 이 필드가 삭제되므로 병렬 작업과 충돌한다 → §2 A4.
- ⚠️ 테스트 스텁 `CapturingFrameRepository`(`:18-43`)·`FrameSelectViewModelTests.StubRepo`(`:15`)가 `IFrameRepository.UpdateAsync`/`SupportsUpdateById`를 구현한다 → 인터페이스 축소 시 함께 수정.

### 1.10 저장소 인터페이스 / HTTP 구현

- `src/MCPhoto.Core/Frames/IFrameRepository.cs:23` `bool SupportsUpdateById`, `:31` `UpdateAsync(frame, imageBytes, replaceImage, ct)` — **F1에서 인터페이스 축소 대상**
- `src/MCPhoto.Http/HttpFrameRepository.cs:100` `SupportsUpdateById => true`, `:107-130` `UpdateAsync` = `PUT frames/{id}` + (이미지 변경 시) 서명 URL PUT
- 서버 라우트: `web/functions/src/routes/frames.ts:85` `PUT /frames/{id}` (Bearer 파워) — **유지**(F1-D3)

### 1.11 다이얼로그 서비스 기존 패턴 (참조만, 채택 안 함)

- `ICameraTestDialogService`(`src/MCPhoto.App/Services/ICameraTestDialogService.cs`) — `Task ShowAsync(int)`
- `IPinPromptDialogService`(`.../IPinPromptDialogService.cs:11-25`) — `PromptVerify` / `PromptSetup`, 구현이 `new PinPromptWindow(...).ShowDialog()` (`PinPromptDialogService.cs:11-27`)
- 두 서비스 모두 **Window를 띄워야 하는 경우**의 VM↔Window 분리용. DI는 `ServiceRegistration.cs:41,45` Singleton.

### 1.12 XAML 리소스 구조 / 테스트 제약

- `Themes/Theme.xaml`: `Colors → Brushes → Typography → Metrics → Controls` 순 병합
- **각 ResourceDictionary는 독립 파싱 — 형제 딕셔너리 키를 `StaticResource`로 교차 참조할 수 없다.** 그래서 `Brushes.xaml`은 `Colors.xaml`을, `Controls.xaml`은 `Brushes/Metrics/Typography`를 **자체 MergedDictionaries로 재병합**한다(각 파일 선두 주석).
- `XamlResourceTests.Each_Theme_File_Resolves_Its_Own_StaticResource_References`(`:128-171`)가 이 규약을 정적으로 강제한다 — Theory InlineData = Colors/Brushes/Typography/Metrics/**Controls**.
- 공용 컨버터는 **`App.xaml`의 `Application.Resources` 직접 항목**(`FilePathToImage` 등) → 테마 딕셔너리 밖. `XamlResourceTests`는 `appKeys` allowlist로 제외하며, **이 allowlist가 3개 메서드에 중복 하드코딩**돼 있다(`:198-203`, `:241-246`, `:275-280`, `:307-312`).
- `.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`: **테스트에서 `Window`를 직접 `new` 하지 말 것**(Application 싱글턴/스레드 친화 충돌). → F2-D1의 근거.

### 1.13 파일 잠금 규약 (회귀 금지)

`src/MCPhoto.App/Converters/CommonConverters.cs:16-40` `FilePathToImageConverter`
- `CacheOption = OnLoad` + `CreateOptions = IgnoreImageCache` + `Freeze()` → 파일 핸들 즉시 해제
- `docs/analysis/90-roadmap-and-future-work.md` §1: 이 컨버터가 "프레임 로컬 삭제 안 됨"(png 파일 잠금) 이슈의 수정본(2026-07-23). **이 3가지 설정은 변경 금지.**

---

## §2. 미검증 가정 (open assumptions)

각 가정은 반드시 어느 Step에서 검증되는지 매핑한다.

| # | 가정 | 위험 | 검증 단계 |
|---|------|------|-----------|
| **A1** | `IFrameRepository`에서 `UpdateAsync`/`SupportsUpdateById`를 제거해도 **`MCPhoto.Http`·`MCPhoto.App` 외 구현체가 남아 있지 않다**(D1으로 `MCPhoto.Firebase.FrameRepository` 삭제 완료 전제) | D1이 아직 머지되지 않았으면 `MCPhoto.Firebase`가 인터페이스 미구현으로 **컴파일 에러** | **Step 2** — 착수 시 `grep -rn ": IFrameRepository" src/` 로 구현체 전수 확인 후, 남아 있으면 그 파일의 해당 멤버도 함께 제거 |
| **A2** | 서버 `PUT /frames/:id` 라우트를 유지해도 **it15의 다른 서버 변경(계정/인증 라우트 정리)과 충돌하지 않는다** | 병렬 에이전트가 라우트를 함께 지우면 문서 표기가 어긋남 | **Step 8** — 문서 동기화 시 `web/functions/src/routes/frames.ts:85` 존재 여부 재확인 |
| **A3** | `LoadImage()`가 장변 4000 초과 이미지를 축소할 때, F2로 불러온 **원본 슬롯 좌표를 같은 배율로 스케일**하면 시각적으로 원본과 동일한 배치가 재현된다 | 스케일 누락 시 슬롯이 프레임 밖으로 나가거나 어긋남 | **Step 5** — `ApplyPickedFrame` 단위 테스트(4000 초과 이미지 + 슬롯 좌표 검증) |
| **A4** | `User.Password` 제거(브리프 §3.1) 후에도 프레임 테스트가 컴파일된다 | `FrameEditorViewModelTests.cs:92`가 `Password = "pw"` 사용 → 병렬 작업 머지 순서에 따라 빌드 깨짐 | **Step 6** — 프레임 테스트 개편 시 `Password` 세팅 **선제 제거**(`new User { Id, Role }`만 사용). 필드가 아직 있어도 제거해서 문제 없음 |
| **A5** | `Themes/Controls.xaml`에 `xmlns:conv` + 컨버터 인스턴스(`FrameCard.FilePathToImage`)를 자체 정의해도 `XamlResourceTests.Each_Theme_File_Resolves_Its_Own_StaticResource_References("Controls.xaml")`가 통과한다 | 같은 딕셔너리 내 정의이므로 통과해야 하나 미실측 | **Step 4** — 해당 테스트 실행 |
| **A6** | `FrameSelectView`의 `ItemTemplate`을 `FrameCard.Content` + 삭제 버튼 합성으로 재구성해도 **삭제 ✕의 `RelativeSource AncestorType=ListBox` 바인딩이 계속 해석된다** | `ContentPresenter` 한 겹이 늘어도 논리 트리상 ListBox 조상은 유지되지만 미실측 | **Step 4** — `FrameSelectViewModelTests` 전량 통과 + XamlResourceTests |
| **A7** | 번들 프레임(`.jpg`/`.jpeg` 가능, `FrameCatalogService.cs:162-164`)을 F2로 불러올 때 `LoadImage` 경로(OpenCV 디코드 → PNG 재인코딩)가 정상 동작한다 | JPG 원본을 `StillImageConverter.FromPngBytes`에 직접 넣으면 실패하므로 **반드시 `LoadImage` 경유**해야 함 | **Step 5** — JPG 소스 프레임 불러오기 단위 테스트 |
| **A8** | 현행 `dotnet test` 675건 중 프레임 관련 삭제 10건(diff 9 + policy 4 + editor VM 6 = 실제 카운트는 Theory 전개 포함) / 신규 추가로 **최종 총계가 675 미만으로 떨어지지 않는다** | 커버리지 후퇴 | **Step 7** — 최종 테스트 총계를 기준선과 비교해 보고(감소 시 보강) |
| **A9** | fork 저장 시 power 스코프에서 새 이름에 `_`가 들어가지 않는다(§1.5 함정 회피) — 단 **원본 이름 자체에 `_`가 있으면** 사본 이름에도 그대로 남는다 | 그 경우 사본이 `LoadPublic`에서 탈락해 목록에 안 보임 | **Step 3** — `FrameNaming` 단위 테스트로 규칙 고정 + 저장 시 경고 상태 메시지 |

### 2.1 알려진 기존 동작 (이번 범위에서 바꾸지 않음)

- **user가 자기 로컬 프레임의 이름을 바꿔 저장하면 옛 파일이 잔존한다**(`SaveLocal`이 새 파일명으로 쓰기만 함, `LocalFrameStore.cs:43-48`). 기존 동작이며 it15 범위 밖 — 변경하지 않는다.
- **UI 가상화 없음**: `FrameSelectView`의 `WrapPanel` ItemsPanel이 `VirtualizingStackPanel`을 대체해 가상화가 꺼져 있다(`FrameSelectView.xaml:21-25`). 피커도 동일 구조를 쓰므로 가상화 없음. 후보 수 상한이 낮아 수용(F2-D5).
- `FrameEditPolicy.CanEdit`의 역할 규칙은 **변경하지 않는다**. user가 DB 기본 프레임을 "선택 편집"으로 여는 것은 여전히 불가하며, 대신 **F2 "기존 프레임 불러오기"로 누구나(로그인 사용자) 어떤 프레임이든 복사해 새로 만들 수 있다**.

---

## §3. F1 설계 — "해당 PC에서만 적용됩니다."

### 3.1 노출 지점과 최종 문구 (F1-D1 / F1-D6)

#### (a) 정책 배너 — `FrameEditorView` 상단, **기존 프레임 수정 세션에서만** 표시 (⟳ §0.5 정정)

> **"이 프레임 편집은 해당 PC에서만 적용됩니다. 서버의 기본 프레임은 변경되지 않으며, 다른 PC에는 반영되지 않습니다."**
>
> (문구 자체는 정정 전과 동일 — 바꾸지 않는다.)

- **노출 조건**: `Visibility="{Binding IsCreateMode, Converter={StaticResource InverseBoolToVis}}"`
  → **편집 세션(`LoadForEdit` 진입, `IsCreateMode == false`)에서만 노출**. 역할(power/user)·프레임 출처는 여전히 무관하다.
  - **신규 생성은 숨김**: power 신규는 `SaveAsync`로 서버에 등록되고 user 신규는 개인 로컬에 저장되는 **기존 로직 그대로**이므로,
    "해당 PC에서만"은 power 신규에서 거짓이 되고 `SaveScopeNotice`("서버에 등록됩니다")와 모순된다(§0.5).
  - **F2로 불러온 세션도 숨김**: `ApplyPickedFrame`은 `_isEditing`을 건드리지 않아 `IsCreateMode`가 유지되고,
    저장은 fork라 원본을 보존한다 → "수정"이 아니라 "새로 만들기"이므로 배너 대상이 아니다.
- 진입 확인 팝업은 **신설하지 않는다**. 클릭 단계가 늘면 키오스크 조작성이 떨어지고, 팝업은 한 번 닫으면 사라져 편집 도중 정책을 재확인할 수 없다. 편집 세션 상시 배너가 요구("기존 프레임 수정 시 노출")를 더 강하게 충족한다.
- 시각: `LoginGuestView.xaml:13-17`의 오프라인 배너와 동일 톤 재사용 —
  `Border Background={StaticResource Brush.Warning.Surface}` + `CornerRadius={StaticResource Radius.M}` + `TextBlock Style={StaticResource Text.Caption}` `Foreground={StaticResource Brush.Warning}` `TextWrapping=Wrap`.
- 레이아웃: 기존 2컬럼 `Grid`에 `RowDefinitions` 추가 →
  - Row 0 = 배너 `Grid.ColumnSpan="2"`, `Margin="40,88,40,12"`. **`RowDefinition Height="Auto" MinHeight="88"`**
    — 배너가 숨어 행 높이가 0이 되면 콘텐츠가 상단 바 오프셋(88) 안으로 파고들기 때문에 하한을 고정한다.
  - Row 1 = 기존 좌/우 콘텐츠. `Margin` 상단값 88 → **0**(88은 Row 0의 MinHeight가, 배너와의 간격 12는 배너 하단 margin이 흡수).
    결과적으로 콘텐츠 상단 위치는 편집 모드 = `88 + 배너높이 + 12`, 신규 모드 = `88`(정정 전 원본과 동일).
  - 기존 팝업 오버레이는 제거되므로 `Grid.RowSpan` 재조정 대상 없음. F2 피커 오버레이만 `Grid.RowSpan="2" Grid.ColumnSpan="2"`로 전체를 덮는다.

#### (b) 저장 스코프 캡션 — 저장 버튼 바로 위, 동적

VM 프로퍼티 `string SaveScopeNotice`(읽기 전용 유도값). **전 모드 상시 노출**한다 — 편집 세션에서는 배너가 **정책**을,
이 캡션이 **이번 저장의 실제 결과**를 말하고, 신규 생성 세션에서는 배너가 없으므로 **이 캡션이 결과 안내의 단독 수단**이 된다.

| 상황 | 문구 |
|------|------|
| power · 신규 생성(F2 불러오기 포함) | `저장 시 '{FrameName}'이(가) 공용 기본 프레임으로 서버에 등록됩니다.` |
| power · fork(DbDefault/Bundle 편집) | `원본은 그대로 두고 '{FrameName}'(으)로 이 PC의 공용 목록에 저장됩니다.` |
| power · 자기 로컬 편집(UserLocal) | `'{FrameName}'을(를) 이 PC에 덮어씁니다.` |
| user · 신규 생성/fork | `'{FrameName}'을(를) 내 프레임으로 이 PC에 저장합니다.` |
| user · 자기 로컬 편집 | `'{FrameName}'을(를) 이 PC에 덮어씁니다.` |

- 시각: `Text.Caption` + `Brush.Text.Muted`. `StatusMessage`(danger 톤) 바로 위에 배치.
- 갱신 트리거: `FrameName` 변경, 편집 세션 초기화(`LoadForEdit`/`ApplyPickedFrame`), 로그인 사용자 변경.
  CommunityToolkit.Mvvm의 `[NotifyPropertyChangedFor(nameof(SaveScopeNotice))]`를 `FrameName`에 부착하고, 세션 초기화 지점에서는 `OnPropertyChanged(nameof(SaveScopeNotice))` 명시 호출.

> **왜 배너 하나로 끝내지 않는가**: power **신규 생성**은 F1 이후에도 `IFrameRepository.SaveAsync`로 DB에 등록된다(공용 기본 프레임 배포의 유일한 경로이며, 사용자 요구는 "기존 프레임 수정"에 한정된다). 그래서 정책 배너는 **편집 세션에만** 띄우고(⟳ §0.5), 결과 안내는 이 캡션이 **전 모드에서** 담당한다 → 화면에 동시에 존재하는 문장은 항상 서로 모순되지 않는다.

> **비차단 경고 1건 추가**: 공용 스코프(power)에서 `FrameName`에 `_`가 포함되면 `LocalFrameStore.LoadPublic()`의
> `!name.Contains('_')` 필터(§1.5)에서 탈락해 저장은 되지만 목록에 보이지 않는다.
> 이 경고는 **`SaveScopeNotice` 뒤에 덧붙여** 저장 **전**에 노출한다 — 저장 직후 `StatusMessage`에 넣으면
> `GoToFrameSelectAsync()`의 화면 전환으로 읽을 기회가 없다(리뷰 라운드 1 지적).
> 문구: `⚠ 이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다.` (저장은 그대로 진행)

### 3.2 DB 업데이트 경로 처리 결정 (F1-D2 / F1-D3)

**결정: 클라이언트 경로는 전면 제거, 서버 라우트는 유지.**

#### 제거 대상 (클라이언트)

| 계층 | 대상 | 파일:줄 |
|------|------|---------|
| VM 상태 | `IsDbUpdatePromptVisible`, `DbUpdateNotice`, `DbUpdateNoticeIsError` | `FrameEditorViewModel.cs:50-54` |
| VM diff 스냅샷 | `_originalImageBytes`, `_originalSlots`, `_originalName` (+ `LoadForEdit`의 세팅 코드) | `:34-36`, `:146-151`, `:161` |
| VM 커맨드 | `SaveLocalOnly`, `SaveToDb`, `CancelDbUpdatePrompt` | `:340-431` |
| VM 헬퍼 | `BuildDbFrame()`, `EditingServerId()` | `:434-442`, `:259-266` |
| VM 분기 | `Save()`의 `RequiresDbUpdatePrompt` 보류 블록 | `:279-286` |
| View | DB 업데이트 확인 팝업 오버레이 Grid 전체 | `FrameEditorView.xaml:89-116` |
| Core 정책 | `FrameEditPolicy.RequiresDbUpdatePrompt` | `FrameEditPolicy.cs:27-32` |
| Core 순수함수 | `FrameDiff` 클래스 파일 전체(유일 호출자가 `SaveToDb`) | `src/MCPhoto.Core/Frames/FrameDiff.cs` |
| 저장소 계약 | `IFrameRepository.SupportsUpdateById`, `IFrameRepository.UpdateAsync` | `IFrameRepository.cs:23`, `:31` |
| 저장소 구현 | `HttpFrameRepository.SupportsUpdateById`, `UpdateAsync`(+ 전용 DTO/헬퍼 중 다른 사용처 없는 것) | `HttpFrameRepository.cs:99-130` |
| 테스트 | `FrameDiffTests.cs`(파일), `FrameEditPolicyTests.cs:74-89`, `FrameEditorViewModelTests.cs:156-285` | — |

> `GoToFrameSelectAsync()`(`:423-427`)는 **삭제하지 않는다** — `Save()`가 그대로 사용해 "저장 성공 후 화면 전환 실패가 저장 결과를 뒤엎지 않는" 기존 보호를 유지한다.
> `PutImageAsync`(`HttpFrameRepository.cs:167`)는 `SaveAsync`도 사용하므로 **유지**한다.

#### 유지 대상 (서버)

- `web/functions/src/routes/frames.ts` 의 `PUT /frames/{id}`(Bearer 파워)와 관련 jest 테스트는 **그대로 둔다.**
- 근거 3가지:
  1. 브리프 §3.3 "제거 라우트" 목록에 없다 → 사용자가 확정한 제거 범위 밖이며, 임의 확장은 금지된다.
  2. Firestore `frameTemplates` 문서를 갱신하는 **유일한 API 경로**다. 앱이 안 쓰더라도 운영(웹 콘솔/스크립트)에서 필요할 수 있으며, 지우면 복구 비용이 크다.
  3. 제거하면 `firebase-contract.md`·`firestore.rules`·jest 테스트까지 연쇄 변경되어 it15의 리스크가 커진다. **앱이 호출하지 않는 것만으로 F1 정책은 완전히 지켜진다**(클라이언트에 호출 코드가 0이 되므로).
- 대신 **문서에 사용처를 명시**한다: `docs/design/firebase-contract.md`의 `PUT /frames/{id}` 항목에 *"⚠️ it15부터 WPF 앱은 이 라우트를 호출하지 않는다(프레임 편집은 로컬 전용). 운영/관리 도구 전용."* 주석 추가.

### 3.3 저장 경로 재설계 (F1-D4 / F1-D5)

`Save()`는 팝업 분기 없이 **한 번에 끝난다**. 분기 축은 두 개뿐이다.

```
저장 스코프  : power → 공용(ownerName=null)      / user → 개인(ownerName=user.Id)
저장 방식    : fork   → 새 이름 신규 파일         / overwrite → 같은 이름 덮어쓰기
```

`fork` 여부는 **편집 세션의 출처**로 결정한다(신규 VM 필드 `FrameSourceKind _sessionSource`):

| 편집 세션 진입 경로 | `_sessionSource` | 저장 방식 |
|---|---|---|
| 신규 생성(빈 편집기) | `New` | 신규 파일 |
| `LoadForEdit`(UserLocal) | `EditOwnLocal` | **overwrite** (같은 이름) |
| `LoadForEdit`(DbDefault) | `ForkFromCatalog` | **fork** |
| `ApplyPickedFrame`(F2, 출처 무관) | `ForkFromCatalog` | **fork** |

- **fork 저장 시 `#dbid`를 기록하지 않는다.** power 경로에서도 `FrameTemplate.Id`를 **빈 문자열**로 두고 `SaveLocal(frame, png, ownerName: null)`을 호출하면 `LocalFrameStore.SaveLocal`(`:21-22`)이 `dbId = frame.Id` = `""` → `SerializeSlots`가 `#dbid` 줄을 생략한다(`:147-148`). 결과적으로 로컬 사본은 `local:{파일명}` id를 갖게 되어(`:118`) **서버 문서와 연결이 끊긴다** = 정책과 일치.
- **원본 불변 보장**: fork는 원본과 다른 파일명으로 쓰기만 하므로 원본 `.png`/`.slots`를 읽지도 지우지도 않는다.
- **DB 재다운로드 무영향**: 원본 이름이 `PublicFrameNames()`에 그대로 남아 있으므로 `GetDefaultFramesAsync`의 이름 dedup(`FrameCatalogService.cs:66`)이 계속 히트한다 → 재다운로드 없음. 사본은 DB에 대응 문서가 없어 dedup 대상 자체가 아니다.
- **power 신규 생성(`New`)은 기존대로 DB 등록**: `_repository.SaveAsync(frame, bytes)` → `_localStore.SaveLocal(saved, bytes, ownerName: null)`(이때는 `#dbid` 기록됨). 변경 없음.
  - 단, **F2로 불러온 세션은 `ForkFromCatalog`** 이므로 DB에 올라가지 않는다. F2는 "기존 프레임을 참고해 새 로컬 프레임 만들기"이며, 서버 배포는 빈 편집기에서 시작하는 신규 생성으로만 가능하다. 이 구분을 §3.1(b) 캡션이 사용자에게 알린다.

#### 저장 가드 (원본 덮어쓰기 방지)

fork 세션은 원본 이름 `_sourceName`을 기억한다. 저장 시:

- `_sessionSource == ForkFromCatalog` && `FrameName == _sourceName` && **저장 스코프가 공용(power)** 이면 →
  저장을 중단하고 `StatusMessage = "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요."`
- user 스코프는 파일명이 `{계정}_{이름}`이라 공용 원본과 물리적으로 겹치지 않으므로 **가드하지 않는다**(불필요한 제약 회피).

### 3.4 이름 분기 규칙 — 신규 순수 함수 `FrameNaming`

**신규 파일**: `src/MCPhoto.Core/Frames/FrameNaming.cs` (UTF-8 no BOM, file-scoped namespace, 한글 XML doc)

```csharp
namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 사본 이름 생성(순수). DB/번들 프레임을 로컬 편집·복사할 때 원본과 이름을 구분해
/// 원본 파일을 보존하고 FrameCatalogService의 이름 기준 dedup(재다운로드 방지)을 유지한다. (it15 F1-D4)
/// </summary>
public static class FrameNaming
{
    /// <summary>사본 접미 기본 토큰. 파일명 규약상 '_'는 쓰지 않는다(LocalFrameStore 공용/user 구분자).</summary>
    public const string CopySuffix = "사본";

    /// <summary>
    /// baseName 기준으로 existingNames와 충돌하지 않는 사본 이름을 만든다.
    /// "{base} 사본" → 충돌 시 "{base} 사본 2", "{base} 사본 3" … 99까지.
    /// baseName이 이미 "{X} 사본" / "{X} 사본 N" 형태면 X를 base로 되돌려 무한 누적을 막는다.
    /// 99까지 모두 충돌하면 "{base} 사본 {8자리 GUID}"를 반환한다(항상 이름을 돌려준다).
    /// </summary>
    public static string NextCopyName(string baseName, IEnumerable<string> existingNames);

    /// <summary>"{X} 사본" / "{X} 사본 N" 접미를 제거해 원형 이름을 얻는다(없으면 원문 그대로).</summary>
    public static string StripCopySuffix(string name);
}
```

**규칙 상세**
- 비교는 `StringComparer.Ordinal`(파일명 규약과 동일 — `LocalFrameStore`가 Ordinal 사용).
- `baseName`이 공백/`null`이면 `"새 프레임 사본"`을 base로 사용.
- 접미 파싱 정규식: `^(?<base>.*?)\s*사본(\s+(?<n>\d{1,2}))?$`
- **`_`를 새로 도입하지 않는다**(§1.5 함정). 다만 원본 이름에 이미 `_`가 있으면 사본에도 남는다 → 저장 시 power 스코프면 `StatusMessage`에 경고 추가:
  `"이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다."` (**비차단** — 저장은 진행)
- 파일시스템 금지문자 검사는 기존 `LocalFrameStore.EnsureFileNameSafe`가 담당(중복 구현 금지).

**호출 시점**: fork 세션 진입 시 1회(`LoadForEdit`의 DbDefault 분기 / `ApplyPickedFrame`)에 `FrameName`의 **제안값**으로 계산. 이후 사용자가 자유롭게 수정 가능하며 저장 시 재계산하지 않는다(§3.3 가드가 원본 충돌만 막는다).

**`existingNames` 소스** (스코프별):

| 스코프 | 소스 |
|--------|------|
| power(공용) | `_localStore.PublicFrameNames()` |
| user(개인) | `_localStore.LoadUser(user.Id).Select(f => f.Name)` |

### 3.5 `FrameEditPolicy` 변경 요약

```csharp
// 제거
public static bool RequiresDbUpdatePrompt(FrameTemplate frame, UserRole? role)

// 신규(fork 판정을 정책 계층으로 끌어올려 VM에서 분기 로직을 없앤다)
/// <summary>이 프레임을 편집·복사해 저장할 때 원본을 보존하고 새 이름으로 분기해야 하는지.
/// DbDefault·Bundle·Fallback(=카탈로그 유래) = true, UserLocal = false. (it15 F1-D4)</summary>
public static bool RequiresFork(FrameTemplate frame)
    => FrameOrigin.Classify(frame) != FrameOriginKind.UserLocal;
```

- `CanEdit`은 **무변경**(§2.1).
- `RequiresFork`는 `FrameOrigin`만 보므로 role 인자가 없다 — 역할 무관 규칙임을 시그니처로 표현.

### 3.6 F1 후 `Save()` 의사 코드 (구현 지침)

```
Save():
  user = shell.Session.CurrentUser;                if null → "로그인이 필요합니다." return
  if _imageBytes null || !SlotLayout.IsValid(...)  → "슬롯이 겹치거나 프레임을 벗어났습니다." return

  isPower  = user.Role.IsPower()
  isFork   = _sessionSource == ForkFromCatalog
  isNew    = _sessionSource == New

  // 원본 덮어쓰기 가드(공용 스코프 fork만)
  if isFork && isPower && FrameName == _sourceName → StatusMessage 경고; return

  try {
    StatusMessage = "저장 중..."
    if (isPower && isNew) {
        // 공용 기본 프레임 신규 등록(현행 유지) — DB + 로컬 캐시(#dbid 기록)
        var saved = await _repository.SaveAsync(new FrameTemplate{ Id="", UserId=null, IsDefault=true, ... }, _imageBytes)
        _localStore.SaveLocal(saved, _imageBytes, ownerName: null)
    } else if (isPower) {
        // power fork / power 자기 로컬 편집 → 로컬 공용만. Id="" 로 #dbid 미기록.
        _localStore.SaveLocal(new FrameTemplate{ Id="", UserId=null, IsDefault=true, ... }, _imageBytes, ownerName: null)
    } else {
        // user 전 케이스 → 개인 로컬
        _localStore.SaveLocal(new FrameTemplate{ UserId=user.Id, IsDefault=false, ... }, _imageBytes, ownerName: user.Id)
    }
    await GoToFrameSelectAsync();
  }
  catch (InvalidOperationException ex) { StatusMessage = ex.Message; }        // 10개 초과 등
  catch (IOException ex)               { StatusMessage = ex.Message; }        // 이름 금지문자
  catch (Exception ex)                 { log; StatusMessage = "저장에 실패했습니다."; }
```

- **UI 스레드 규칙**: `SaveAsync`는 `await`(HTTP I/O), `SaveLocal`은 동기 파일 쓰기지만 PNG 1장 수준이라 현행대로 UI 스레드에서 수행한다(기존 동작 유지, 변경 시 회귀 위험이 더 큼).
- `_localStore.SaveLocal`이 던지는 `IOException`(금지문자)을 지금은 일반 `Exception`으로 삼키는데, **사용자에게 이유를 알리도록 `IOException` catch를 추가**한다(정직한 실패 보고).

---

## §4. F2 설계 — "기존 프레임 불러오기"

### 4.1 버튼

`FrameEditorView.xaml` 우측 컨트롤 패널, **"이미지 불러오기" 바로 아래**:

```xml
<Button Content="이미지 불러오기" Click="OnLoadImage"
        Style="{StaticResource Button.Secondary}" HorizontalAlignment="Stretch" Margin="0,0,0,8" />
<Button Content="기존 프레임 불러오기" Command="{Binding OpenFramePickerCommand}"
        Visibility="{Binding IsCreateMode, Converter={StaticResource BoolToVis}}"
        Style="{StaticResource Button.Secondary}" HorizontalAlignment="Stretch" Margin="0,0,0,20" />
```

- 기존 "이미지 불러오기"의 하단 여백 `Margin="0,0,0,20"` → `"0,0,0,8"`로 줄이고, 새 버튼이 20 여백을 갖는다.
- **활성 조건**: `IsCreateMode`(= `_sessionSource != EditOwnLocal && _sessionSource != ForkFromCatalog` … 즉 `_isEditing == false`). 편집 모드에서는 `Collapsed`(F2-D6).
  - `IsCreateMode`는 `LoadForEdit` 호출 여부로 결정되며 편집 세션 도중 바뀌지 않는다. F2로 불러온 뒤에도 `_sessionSource`는 `ForkFromCatalog`가 되지만 **`IsCreateMode`는 true를 유지**(생성 흐름 중이므로 다른 프레임으로 다시 바꿀 수 있어야 한다).
  - 정리: `IsCreateMode = !_isEditing`. `ApplyPickedFrame`은 `_isEditing`을 건드리지 않는다.
- 로그인 게이트 불필요 — 편집기 진입 자체가 `FrameSelectViewModel.CreateFrame`의 `IsLoggedIn` 게이트를 통과한 뒤다(`FrameSelectViewModel.cs:199-203`).
- "이미지 불러오기"는 코드비하인드 `Click` 핸들러(`OpenFileDialog` 필요)지만, **"기존 프레임 불러오기"는 파일 대화상자를 쓰지 않으므로 `Command` 바인딩**으로 둔다(코드비하인드 증가 없음).

### 4.2 선택 모달 = 편집기 내부 오버레이 (F2-D1 / F2-D2)

**새 `Window`를 만들지 않는다.** `FrameEditorView.xaml` 최상위 `Grid`의 마지막 자식으로 오버레이를 둔다.

```xml
<!-- it15 F2: 기존 프레임 선택 모달(오버레이). 삭제 확인 팝업과 동일 패턴. -->
<Grid Grid.RowSpan="2" Grid.ColumnSpan="2" Background="{StaticResource Brush.Scrim}"
      Visibility="{Binding IsFramePickerVisible, Converter={StaticResource BoolToVis}}">
    <Border Style="{StaticResource Card}" Padding="24" Effect="{StaticResource Shadow.Pop}"
            HorizontalAlignment="Center" VerticalAlignment="Center"
            MinWidth="720" MaxWidth="1100" MaxHeight="620"
            Background="{StaticResource Brush.Bg}">
        <Grid DataContext="{Binding Picker}">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />  <!-- 제목 -->
                <RowDefinition Height="*" />     <!-- 썸네일 그리드 / 로딩 / 빈 목록 -->
                <RowDefinition Height="Auto" />  <!-- 버튼 -->
            </Grid.RowDefinitions>
            ...
        </Grid>
    </Border>
</Grid>
```

**선택 근거**
1. 프로젝트 관례가 오버레이다 — 삭제 확인 팝업(`FrameSelectView.xaml:107-129`), 기존 DB 업데이트 팝업(`FrameEditorView.xaml:91-116`) 모두 `Brush.Scrim` 오버레이.
2. 키오스크 전체화면·터치 환경에서 별도 `Window`는 Owner/포커스/DPI 처리가 늘고 조작성이 나쁘다.
3. **테스트 제약**: `Window`를 테스트에서 `new` 하면 `Application` 싱글턴·스레드 친화 충돌로 실패한다(`.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`). 오버레이면 모든 로직이 VM 프로퍼티/커맨드로 검증 가능하다.
4. 그래서 **`IPinPromptDialogService` 류의 다이얼로그 서비스 추상화를 도입하지 않는다**(F2-D2). 그 패턴은 "VM이 `Window`를 직접 참조하지 않게" 하려는 우회책이며, `Window`가 없으면 존재 이유도 없다. 인터페이스·구현·DI 등록 3개를 늘리지 않는 편이 응집도가 높다.
   - 반례 검토: 향후 프레임 피커를 **설정 화면 등 편집기 밖**에서도 열어야 하면 서비스 추상화가 필요해진다. 현재 요구는 편집기 전용이므로 **YAGNI**로 판단하고, 필요 시 `FramePickerViewModel`을 그대로 재사용해 오버레이 호스트만 추가하면 되도록 VM을 편집기와 독립적으로 설계한다(§4.4).

### 4.3 썸네일 그리드 재사용 (F2-D3)

**문제**: `FrameSelectView`의 `ItemTemplate`에는 삭제 ✕ 버튼이 박혀 있고, 그 `MultiBinding`이 `DataContext.CanDeleteFrames` / `DataContext.IsPower`를 ListBox 조상에서 찾는다(`FrameSelectView.xaml:94-98`). 이 템플릿을 피커에 그대로 쓰면 피커 VM에 없는 경로를 바인딩하게 된다.

**해결**: 카드의 **시각 본체만** 공유 리소스로 추출하고, 삭제 버튼은 FrameSelect 쪽에서 합성한다.

#### 추출 위치: `src/MCPhoto.App/Themes/Controls.xaml`

새 파일을 만들지 않는다. 근거 — `Controls.xaml`은 이미 "컨트롤 스타일·템플릿" 담당이고, **자체 MergedDictionaries로 Brushes/Metrics/Typography를 재병합**하고 있어(§1.12) 필요한 테마 키가 전부 해석된다. 별도 딕셔너리를 App.xaml에 병합하면 형제 딕셔너리 교차 참조 문제(it2 버그)를 다시 만들 위험이 있다.

추가할 것:

| 키 | 종류 | 내용 |
|----|------|------|
| `FrameCard.FilePathToImage` | `conv:FilePathToImageConverter` 인스턴스 | **Controls.xaml 자체 정의**. App.xaml의 `FilePathToImage`와 키를 달리해 충돌 회피. 같은 딕셔너리 안이라 `Each_Theme_File_Resolves_...` 테스트 통과(§2 A5) |
| `FrameCard.ItemContainer` | `Style` (TargetType=`ListBoxItem`) | 현행 `FrameSelectView.xaml:27-50`의 ControlTemplate을 그대로 이동(선택 시 `Brush.Accent` 테두리 + `Shadow.Card`) |
| `FrameCard.Content` | `DataTemplate` (x:Key, DataType 없음) | 200×280 카드 **본체만**: `Image`(→ `FrameCard.FilePathToImage`) + 슬롯 미리보기 `Viewbox`/`ItemsControl`/`Canvas` + 이름 바(`Brush.Scrim` + `Brush.OnAccent`) |

`Controls.xaml` 루트에 `xmlns:conv="clr-namespace:MCPhoto.App.Converters"` 추가 필요.

#### 소비 측

**피커(신규)** — 본체만 그대로:
```xml
<ListBox ItemsSource="{Binding Frames}" SelectedItem="{Binding SelectedFrame}"
         ItemContainerStyle="{StaticResource FrameCard.ItemContainer}"
         ItemTemplate="{StaticResource FrameCard.Content}"
         Background="Transparent" BorderThickness="0"
         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
         ScrollViewer.VerticalScrollBarVisibility="Auto">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>
```

**`FrameSelectView`(수정)** — 본체 + 삭제 버튼 합성:
```xml
<ListBox.ItemTemplate>
    <DataTemplate>
        <Grid Width="200" Height="280">
            <!-- 카드 본체는 공유 템플릿(FrameCard.Content) 재사용 — 피커와 시각 동일 -->
            <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource FrameCard.Content}" />
            <!-- 삭제 ✕는 이 화면 전용(현행 MultiBinding 그대로 유지) -->
            <Button Content="✕" ... />
        </Grid>
    </DataTemplate>
</ListBox.ItemTemplate>
```
- `ContentPresenter`가 한 겹 늘어도 **논리 트리상 `ListBox` 조상은 유지**되므로 삭제 버튼의 `RelativeSource AncestorType=ListBox` 바인딩은 그대로 동작한다(§2 A6에서 검증).
- 삭제 버튼 XAML(`:86-100`)은 **한 글자도 바꾸지 않는다** — 회귀 표면 최소화.

### 4.4 `FramePickerViewModel` (신규)

**신규 파일**: `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs`

```csharp
/// <summary>
/// "기존 프레임 불러오기" 선택 모달의 목록 VM. (it15 F2)
/// 편집기 오버레이의 DataContext. 확인/취소 커맨드는 소유자(FrameEditorViewModel)가 갖는다 —
/// 이벤트 구독이 0이라 구독 해제 경로가 필요 없다(누수 없음).
/// </summary>
public sealed partial class FramePickerViewModel : ObservableObject
{
    public ObservableCollection<FrameTemplate> Frames { get; } = new();
    [ObservableProperty] private FrameTemplate? _selectedFrame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _emptyNotice = string.Empty;

    /// <summary>선택 가능한 프레임이 있는지(확인 버튼 활성 조건).</summary>
    public bool HasSelection => SelectedFrame is not null;

    public FramePickerViewModel(FrameCatalogService catalog, ILogger<FramePickerViewModel>? logger = null);

    /// <summary>후보 목록 로드. 공용(번들+DB캐시+DB다운로드) + 로그인 계정 개인 로컬. UI 스레드 비블로킹(await).</summary>
    public async Task LoadAsync(string? userId, CancellationToken ct = default);

    /// <summary>모달을 닫을 때 상태 초기화(선택 해제·목록 비우기).</summary>
    public void Reset();
}
```

**설계 포인트**
- **이벤트 0개**: 확인/취소는 `FrameEditorViewModel`의 커맨드가 `Picker.SelectedFrame`을 읽는 방식. `PropertyChanged` 구독을 만들지 않으므로 **해제 경로가 불필요**하고 누수 위험이 0이다(핵심 안전 규칙 3).
- `HasSelection`은 `[NotifyPropertyChangedFor(nameof(HasSelection))]`를 `SelectedFrame`에 부착해 갱신.
- **UI 타입 의존 없음** — `Visibility`/`Brush`를 노출하지 않는다(테스트 가능성).
- DI: `services.AddTransient<FramePickerViewModel>();` (`ServiceRegistration.cs`의 VM 등록 구역, `FrameEditorViewModel` 등록 근처). `FrameEditorViewModel` 생성자에 주입.
  - `FrameEditorViewModel`이 Transient이므로 편집기 진입마다 새 Picker → 재진입 잔존 없음.
  - ⚠️ `FrameEditorViewModel` 생성자 시그니처 변경 → **모든 테스트의 `new FrameEditorViewModel(...)` 호출부 수정 필요**(`FrameEditorViewModelTests.cs:95`).

### 4.5 후보 범위와 역할 필터

**`LoadAsync(userId)` 구성** — `FrameSelectViewModel.ReloadFramesAsync`(`:63-84`)와 **동일한 소스**를 쓴다. 사용자가 "프레임 선택 페이지와 유사한 페이지"를 기대하므로 목록도 같아야 한다.

```
Frames = catalog.GetDefaultFramesAsync()            // 로컬 공용(번들 + DB 캐시) → DB isDefault 다운로드 병합 → 번들 → fallback
       + catalog.GetUserFramesAsync(userId)         // 로그인 계정 개인 로컬 (userId 있을 때만)
```

| 후보 | 포함 | 비고 |
|------|------|------|
| 번들(`bundle:`) | ✅ | 편집 권한은 없지만 **복사는 허용** — 원본을 수정하지 않으므로 안전 |
| 로컬 공용(파워 캐시·DB 캐시) | ✅ | |
| DB 다운로드분 | ✅ | `GetDefaultFramesAsync`가 캐시 후 반환하므로 목록 시점에 이미 로컬 파일 |
| 본인 개인 로컬(`{계정}_`) | ✅ | |
| 타인 개인 로컬 | ❌ | `LoadUser(ownerName)`가 접두로 필터 — 자동 제외 |
| fallback | ✅ | 코드 생성 프레임. 굳이 배제할 이유 없음(복사 가능) |

**역할별 필터: 불필요.**
근거 — (1) 불러오기는 **읽기 전용 복사**이며 원본 파일·서버 문서를 일절 수정하지 않는다. (2) 여기서 보이는 목록은 그 사용자가 이미 프레임 선택 화면에서 보는 목록과 **동일**하므로 새로운 정보 노출이 없다. (3) 편집기 진입 자체가 로그인 게이트다. → `FrameEditPolicy.CanEdit`을 피커에 적용하면 오히려 "번들 프레임을 참고해 새로 만들기"라는 F2의 핵심 사용례가 막힌다.

**빈 목록 처리**: `Frames.Count == 0`이면 `EmptyNotice = "불러올 수 있는 프레임이 없습니다."`, `ListBox` 대신 안내 텍스트 표시.

**로딩 중 UI 블로킹 방지**
- `GetDefaultFramesAsync`는 DB 조회 + 이미지 HTTP 다운로드를 포함할 수 있다 → 전 구간 `await`. `IsLoading=true` 동안 오버레이 내부에 "프레임 목록을 불러오는 중..." 텍스트 표시, `ListBox` 숨김.
- `FrameCatalogService`의 `_defaultFramesGate`(`:24`)가 시작 prefetch·FrameSelect 진입과 직렬화해 주므로 **중복 다운로드는 발생하지 않는다**.
- 취소 토큰: `OpenFramePicker`가 `CancellationTokenSource`를 만들고, `CancelPickFrame`/편집기 이탈 시 `Cancel()` → 로딩 중 취소해도 UI 스레드가 잡히지 않는다. `CTS`는 `FrameEditorViewModel`이 보유하고 재오픈 시 교체(이전 것 `Dispose`).
- **썸네일 디코드는 현행 `FilePathToImage` 컨버터 그대로**(F2-D5). `DecodePixelWidth`를 넣지 않는다 — `CacheOption=OnLoad` / `IgnoreImageCache` / `Freeze()` 3종은 it9 파일 잠금 수정의 본체이며(§1.13), 컨버터를 건드리면 "프레임 삭제 실패" 회귀 위험을 다시 만든다. 후보 수 상한(공용 소수 + 계정당 최대 10)에서 실익이 없다.
  - 성능 문제가 실제로 관측되면 `docs/analysis/90-roadmap-and-future-work.md` §1에 후속 과제로 등재한다(이번 범위 아님).

### 4.6 복사 규칙 — `ApplyPickedFrame` (원본 불변 보장)

`FrameEditorViewModel`에 신규 메서드. **디스크에 아무것도 쓰지 않는다.**

```
ApplyPickedFrame(FrameTemplate src):
  1) 이미지 유효성
     if src.ImageUrl 비어있음 || !File.Exists(src.ImageUrl)
        → StatusMessage = "선택한 프레임의 이미지를 찾을 수 없습니다."; return false

  2) 이미지 로드 (원본 파일은 읽기만 — LoadImage가 File 핸들을 열고 즉시 닫는다)
     if (!LoadImage(src.ImageUrl)) return false;
     // LoadImage 부작용: _imageBytes(PNG 재인코딩), FrameWidth/Height(장변 4000 초과 시 축소),
     //                   ArrangeSlots()로 슬롯 자동 배치(아래 3)에서 덮어씀)
     // ⚠️ 번들 프레임은 .jpg 가능 → 반드시 LoadImage 경유(OpenCV 디코드 → PNG 인코딩). 직접 ReadAllBytes 금지.

  3) 슬롯 복사 + 축소 배율 보정
     scale = (src.ImageSize.Width > 0) ? (double)FrameWidth / src.ImageSize.Width : 0
     if (src.Slots.Count > 0 && scale > 0):
         _suppressArrange = true
         SlotCount = Clamp(src.Slots.Count, 1, 6)
         _suppressArrange = false
         _baseSlots.Clear()
         foreach s in src.Slots:
             _baseSlots.Add(SlotLayout.ClampToFrame(new Slot{
                 Index=s.Index,
                 X =(int)Math.Round(s.X*scale),      Y =(int)Math.Round(s.Y*scale),
                 Width =(int)Math.Round(s.Width*scale), Height=(int)Math.Round(s.Height*scale)
             }, FrameWidth, FrameHeight))
         SlotScalePercent = 100
         ApplyScale()
     // src.ImageSize가 0(메타 없음)이면 2)의 자동 배치 결과를 그대로 사용

  4) 세션 정체성 — 항상 "새 프레임"
     _sessionSource = ForkFromCatalog
     _sourceName    = src.Name
     _isEditing     = false          // 편집 아님 → IsCreateMode 유지, EditorTitle 불변
     _editingFrame  = null; _editingFrameId = null

  5) 이름 제안
     FrameName = FrameNaming.NextCopyName(src.Name, ExistingNamesForCurrentScope())
     OnPropertyChanged(nameof(SaveScopeNotice))

  6) StatusMessage = string.Empty; return true
```

**원본 불변 보장 근거**
| 위험 | 차단 방식 |
|------|-----------|
| 원본 이미지 파일 수정 | `LoadImage`는 `Cv2.ImRead`로 **읽기만** 한다. 쓰기 API 미호출 |
| 원본 파일 잠금 | `Cv2.ImRead`는 읽고 닫는다. 썸네일 표시는 `FilePathToImage`(OnLoad+IgnoreImageCache)라 핸들 미보유 |
| 원본 `.slots` 수정 | `src.Slots`를 **값 복사**(새 `Slot` 인스턴스 생성)만 하고 `src` 객체에 쓰지 않는다 |
| 저장 시 원본 덮어쓰기 | `_sessionSource = ForkFromCatalog` → §3.3 fork 규칙(새 이름) + §3.3 저장 가드 |
| 임시 파일 잔존 | **임시 파일을 만들지 않는다**(F2-D4) — 이미지는 `_imageBytes`(메모리)에만 존재하고, 디스크 쓰기는 `Save()` 1회뿐 |

> **"저장 전 취소 시 임시 파일 정리" 요구는 임시 파일 부재로 자동 충족된다.** 취소(`CancelCommand`)·앱 종료·편집기 이탈 어느 경우에도 정리할 대상이 없다. 정리 로직이 없으므로 정리 누락 버그도 발생할 수 없다.

### 4.7 편집기 VM의 F2 커맨드

```csharp
[ObservableProperty] private bool _isFramePickerVisible;
public FramePickerViewModel Picker { get; }
public bool IsCreateMode => !_isEditing;

[RelayCommand] private async Task OpenFramePicker()
{
    _pickerCts?.Cancel(); _pickerCts?.Dispose();
    _pickerCts = new CancellationTokenSource();
    IsFramePickerVisible = true;
    await Picker.LoadAsync(_shell.Session.CurrentUser?.Id, _pickerCts.Token);
}

/// <summary>[불러오기]: 선택 프레임의 이미지·슬롯을 새 편집 세션으로 복사. 실패해도 모달만 닫고 편집 상태 보존.</summary>
[RelayCommand] private void ConfirmPickFrame()
{
    var src = Picker.SelectedFrame;
    IsFramePickerVisible = false;
    if (src is null) return;
    ApplyPickedFrame(src);   // 실패 시 StatusMessage로 안내
    Picker.Reset();
}

/// <summary>[취소]: 모달만 닫고 편집기 상태·디스크 모두 무변경.</summary>
[RelayCommand] private void CancelPickFrame()
{
    _pickerCts?.Cancel();
    IsFramePickerVisible = false;
    Picker.Reset();
}
```

- 취소 동작: **편집기 상태(이미지·슬롯·이름) 완전 무변경**, 디스크 변경 0, 진행 중 목록 로딩은 `CancellationToken`으로 중단.
- `_pickerCts`는 `FrameEditorViewModel`이 소유. VM이 Transient라 편집기 이탈 시 GC 대상이며, `Cancel()`이 호출되지 않은 채 버려져도 `Task`는 완료되고 참조가 끊긴다. `IDisposable` 구현까지는 하지 않는다(현행 VM들과 일관 — VM 생명주기 관리 인프라가 없어 `Dispose` 호출 지점이 없다).
- 오버레이 하단 버튼: `[불러오기]`(`Button.Primary`) / `[취소]`(`Button.Ghost`).

### 4.8 오버레이 바인딩 스코프 (구현 함정 주의)

§4.2 스케치처럼 오버레이 내부 `Grid`에 `DataContext="{Binding Picker}"`를 걸면 **그 아래 버튼의 `ConfirmPickFrameCommand`가 `FramePickerViewModel`에서 조회되어 조용히 실패**한다(커맨드가 편집기 VM에 있음). 실패한 커맨드 바인딩은 예외를 던지지 않고 버튼만 비활성 상태가 되므로 발견이 늦다.

**규칙**: `DataContext`를 넓게 바꾸지 말고, **목록 영역만** Picker로 좁힌다.

```xml
<Grid Grid.RowSpan="2" Grid.ColumnSpan="2" Background="{StaticResource Brush.Scrim}"
      Visibility="{Binding IsFramePickerVisible, Converter={StaticResource BoolToVis}}">
    <Border Style="{StaticResource Card}" ...>
        <Grid>   <!-- DataContext = FrameEditorViewModel 유지 -->
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" /><RowDefinition Height="*" /><RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Text="기존 프레임 불러오기" Style="{StaticResource Text.H2}" ... />

            <!-- 목록만 Picker 스코프 -->
            <ListBox Grid.Row="1" DataContext="{Binding Picker}"
                     ItemsSource="{Binding Frames}" SelectedItem="{Binding SelectedFrame}"
                     ItemContainerStyle="{StaticResource FrameCard.ItemContainer}"
                     ItemTemplate="{StaticResource FrameCard.Content}"
                     Visibility="{Binding IsLoading, Converter={StaticResource InverseBoolToVis}}" ... />
            <TextBlock Grid.Row="1" Text="프레임 목록을 불러오는 중..."
                       Visibility="{Binding Picker.IsLoading, Converter={StaticResource BoolToVis}}" ... />
            <TextBlock Grid.Row="1" Text="{Binding Picker.EmptyNotice}" ... />

            <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center">
                <Button Content="불러오기" Command="{Binding ConfirmPickFrameCommand}"
                        IsEnabled="{Binding Picker.HasSelection}"
                        Style="{StaticResource Button.Primary}" Margin="0,0,12,0" />
                <Button Content="취소" Command="{Binding CancelPickFrameCommand}"
                        Style="{StaticResource Button.Ghost}" />
            </StackPanel>
        </Grid>
    </Border>
</Grid>
```

- 사용 컨버터: `BoolToVis`, `InverseBoolToVis` — 둘 다 App.xaml에 이미 등록되어 있다(§1.12). **신규 컨버터 없음** → `XamlResourceTests`의 `appKeys` allowlist 갱신 불필요.
- 오버레이가 열려 있는 동안 뒤쪽 컨트롤이 여전히 클릭 가능하다는 문제는 기존 팝업들과 동일한 수준으로 둔다(`Brush.Scrim` Grid가 히트 테스트를 가로챈다 — `Background`가 지정된 `Grid`는 마우스 이벤트를 흡수).

---

## §5. 테스트 계획

### 5.1 검증 전략의 기본 제약

`Window`를 테스트에서 `new` 할 수 없다(`.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`: `Application`은 AppDomain당 1개 + 스레드 친화 → `XamlResourceTests`와 같은 프로세스에서 레이스로 실패). 따라서 검증을 두 축으로 나눈다.

1. **순수 로직 / VM 단위 테스트** — 상태·규칙·파일 결과를 xUnit으로 직접 검증. F2를 오버레이로 설계했기 때문에(F2-D1) 모달 로직 전체가 이 축에 들어온다.
2. **XAML 정적 키 검증** — 소스 XAML을 텍스트로 읽어 `{StaticResource key}`를 정규식 추출 → `pack://` 로드한 테마 딕셔너리에서 `Contains(key)` 확인. 창을 만들지 않는다.

실제 화면 육안 확인(배너 위치·모달 레이아웃)은 **사용자 액션**으로 남긴다(앱 실행 금지 제약).

### 5.2 삭제할 테스트

| 파일 / 범위 | 사유 |
|---|---|
| `tests/MCPhoto.Tests/FrameDiffTests.cs` (파일 전체) | `FrameDiff` 제거(§3.2) |
| `FrameEditPolicyTests.cs:74-89` — `RequiresDbUpdatePrompt_*` 4개 | 정책 함수 제거 |
| `FrameEditorViewModelTests.cs:156-285` — DB 팝업/ diff 플로우 6개 (`Power_Editing_Db_Default_Save_Shows_Prompt_And_Defers`, `SaveLocalOnly_*`, `SaveToDb_*` 4개, `CancelDbUpdatePrompt_*`) | DB 업데이트 경로 제거 |

### 5.3 신규 / 보강할 테스트

#### (A) `FrameNamingTests.cs` (신규, 순수 함수)

| 케이스 | 기대 |
|---|---|
| `NextCopyName("기본프레임", [])` | `"기본프레임 사본"` |
| `NextCopyName("기본프레임", ["기본프레임 사본"])` | `"기본프레임 사본 2"` |
| `NextCopyName("기본프레임", ["기본프레임 사본","기본프레임 사본 2"])` | `"기본프레임 사본 3"` |
| `NextCopyName("기본프레임 사본", [...])` | base가 `"기본프레임"`으로 되돌려져 접미 누적 없음 |
| `NextCopyName("기본프레임 사본 5", [])` | `"기본프레임 사본"` (누적 방지) |
| `NextCopyName("", [])` / `null` | `"새 프레임 사본"` |
| 1~99 전부 충돌 | `"{base} 사본 {8자}"` 형태 반환, 예외 없음 |
| 결과 이름에 `_` 미포함 (base에 `_`가 없을 때) | `Assert.DoesNotContain("_", result)` |
| `StripCopySuffix` 왕복 | `StripCopySuffix(NextCopyName(x, [])) == x` |

#### (B) `FrameEditPolicyTests.cs` 보강 — `RequiresFork`

| 입력 | 기대 |
|---|---|
| `DbDefault` 프레임 | `true` |
| `bundle:` 프레임 | `true` |
| `fallback` / 빈 Id | `true` |
| `local:` 프레임(소유자 무관) | `false` |

#### (C) `FrameEditorViewModelTests.cs` 개편

기존 스텁 조정:
- `CapturingFrameRepository`에서 `SupportsUpdateById` / `UpdateAsync` / `Updated` / `LastReplaceImage` **제거**(인터페이스 축소, §3.2).
- `MakeVm`에서 `new User { Id = "u1", Password = "pw", Role = role }` → **`new User { Id = "u1", Role = role }`** (§2 A4).
- `new FrameEditorViewModel(shell, repo, local)` → `new FrameEditorViewModel(shell, repo, local, picker)` (Picker 주입).
- `CapturingLocalStore`에 `PublicFrameNames`/`LoadUser` 반환값을 케이스별로 주입할 수 있도록 세터 추가(이름 충돌 시나리오용).

신규 케이스:

| # | 이름 | 검증 |
|---|---|---|
| C1 | `Power_Editing_Db_Default_Saves_Local_Only_With_Fork_Name` | `LoadForEdit(DbDefault)` → `FrameName == "공용프레임 사본"`, `Save` 후 `repo.Saved == null`(DB 미호출), `local.SavedOwner == null`(공용), `local.SavedFrame.Id == ""`(#dbid 미기록) |
| C2 | `User_Editing_Own_Local_Overwrites_Same_Name` | `LoadForEdit(UserLocal)` → `FrameName`이 원본과 동일, `local.SavedOwner == "u1"` |
| C3 | `Power_New_Frame_Still_Registers_To_Db` | 빈 편집기 + `LoadImage` → `Save` → `repo.Saved != null`, `IsDefault == true` (기존 동작 무회귀) |
| C4 | `Fork_Save_Blocked_When_Name_Equals_Source_In_Public_Scope` | fork 세션에서 `FrameName = 원본이름` → `Save` 후 `repo.Saved == null && local.SavedFrame == null`, `StatusMessage`에 "원본과 같은 이름" 포함 |
| C5 | `Db_Update_Members_Are_Gone` | (컴파일 레벨) 삭제된 커맨드가 없음을 보장 — 별도 테스트 대신 빌드로 커버. **테스트 작성 불필요** |
| C6 | `SaveScopeNotice_Reflects_Scope` | power/new, power/fork, user 각각에서 `SaveScopeNotice`에 기대 키워드 포함(`"서버에 등록"` / `"원본은 그대로"` / `"내 프레임"`) |
| C6b | `SaveScopeNotice_Warns_Before_Save_When_Public_Name_Has_Underscore` | 공용 스코프 이름에 `_` → 캡션에 경고 포함, 이름 정정 시 해제, user 스코프는 경고 없음, 저장은 비차단 |
| C6c | `IsCreateMode_Gates_LocalOnly_Banner` (⟳ §0.5) | 신규(power)=`true`(배너 숨김·`"서버에 등록"` 안내) / `LoadForEdit`(power DB기본)=`false` / `LoadForEdit`(user 본인 로컬)=`false` / `ApplyPickedFrame` 후=`true`(F2 세션은 계속 숨김) |
| C7 | `ApplyPickedFrame_Copies_Slots_And_Suggests_Copy_Name` | 4슬롯 소스 → `Slots.Count == 4`, 좌표가 원본과 일치(축소 없는 크기), `FrameName == "{원본} 사본"`, `_isEditing` 유지(=`IsCreateMode == true`) |
| C8 | `ApplyPickedFrame_Scales_Slots_When_Image_Downscaled` (**A3 검증**) | 장변 4000 초과 PNG + 원본 `ImageSize`/슬롯 → `FrameWidth == 4000`, 슬롯 좌표가 `scale` 비율로 축소되고 전부 `ClampToFrame` 범위 내 |
| C9 | `ApplyPickedFrame_Accepts_Jpeg_Source` (**A7 검증**) | `.jpg` 소스 파일 → `true` 반환, `FrameImage != null`(PNG 재인코딩 성공) |
| C10 | `ApplyPickedFrame_Does_Not_Modify_Source_File` | 소스 png 바이트 해시를 전후 비교 → 동일. `.slots` 파일 mtime 불변 |
| C11 | `ApplyPickedFrame_Missing_Image_Reports_Status` | `ImageUrl`이 없는 프레임 → `false`, `StatusMessage`에 "찾을 수 없습니다" |
| C12 | `CancelPickFrame_Leaves_Editor_Untouched` | 이미지 로드 후 피커 열고 선택 → 취소 → `FrameImage`/`Slots`/`FrameName` 전부 이전 값 유지, `IsFramePickerVisible == false` |
| C13 | `ConfirmPickFrame_With_No_Selection_Is_Noop` | 선택 없이 확인 → 편집기 무변경, 모달만 닫힘 |

#### (D) `FramePickerViewModelTests.cs` (신규)

| # | 이름 | 검증 |
|---|---|---|
| D1 | `LoadAsync_Includes_Public_And_Own_User_Frames` | 스텁 `FrameCatalogService` 소스로 공용 2 + 개인 1 → `Frames.Count == 3` |
| D2 | `LoadAsync_Without_UserId_Loads_Public_Only` | `userId=null` → 개인 프레임 미포함 |
| D3 | `LoadAsync_Toggles_IsLoading` | 호출 전/중/후 `IsLoading` 전이(TaskCompletionSource로 중간 관측) |
| D4 | `Empty_Result_Sets_EmptyNotice` | 소스가 비면 `EmptyNotice`가 비어 있지 않음 |
| D5 | `HasSelection_Follows_SelectedFrame` | `SelectedFrame` set/null → `HasSelection` + `PropertyChanged(nameof(HasSelection))` 발생 |
| D6 | `Reset_Clears_Selection_And_List` | `Reset()` 후 `Frames.Count == 0 && SelectedFrame == null` |
| D7 | `LoadAsync_Honors_CancellationToken` | 취소된 토큰 → 예외 전파 없이 종료, `IsLoading == false` |

> ⚠️ `FrameCatalogService`는 **클래스(인터페이스 아님)** 이고 생성자가 `IFrameRepository` + `ILocalFrameStore` + `downloadImage` 델리게이트를 받는다(`FrameCatalogService.cs:32-44`). 테스트는 기존 `FrameCatalogServiceTests.cs`와 동일하게 **스텁 repo/localStore를 주입한 실제 인스턴스**를 쓴다(신규 인터페이스 추출 금지 — 범위 확대).

#### (E) `LocalFrameStoreTests.cs` 보강 — 원본 불변

| # | 검증 |
|---|---|
| E1 | 공용 `{이름}.png` 존재 상태에서 `SaveLocal(사본프레임, bytes, ownerName: null)` → 원본 png 바이트 **불변**, 사본 파일 신규 생성, `LoadPublic()`이 2건 반환 |
| E2 | `Id == ""`로 `SaveLocal(..., ownerName: null)` → `.slots`에 **`#dbid` 줄 없음**, `LoadPublic()`이 그 프레임을 `local:{파일명}` id로 반환 (§3.3 핵심 전제) |
| E3 | `PublicFrameNames()`에 원본 이름이 계속 포함됨(재다운로드 방지 전제 고정) |

#### (F) `XamlResourceTests.cs` 확장

- 기존 Theory `Item1a_View_StaticResource_Keys_Resolve_In_Theme`의 `InlineData`에 **`"FrameEditorView.xaml"`, `"FrameSelectView.xaml"` 추가**.
  - 두 View가 새로 참조하는 `FrameCard.ItemContainer` / `FrameCard.Content`는 **테마(Controls.xaml)에 있으므로** `appKeys` allowlist 갱신이 **불필요**하다.
  - 두 View가 쓰는 App.xaml 컨버터(`BoolToVis`, `InverseBoolToVis`, `NullToVis`, `SlotAspectLabel`, `FrameDeleteVis`, `FilePathToImage`, `BoolToNoticeBrush`)는 이미 그 메서드의 `appKeys`(`:241-246`)에 전부 있다. **추가 검증 필요**: 실제 실행으로 확인(Step 4).
- 기존 Theory `Each_Theme_File_Resolves_Its_Own_StaticResource_References("Controls.xaml")`가 `FrameCard.FilePathToImage` 자체 정의를 검증한다(§2 A5).
- 신규 Fact `FrameCard_Shared_Resources_Exist_In_Theme`: 테마 로드 후 `FrameCard.ItemContainer`가 `Style`, `FrameCard.Content`가 `DataTemplate` 타입인지 확인.
- 신규 Fact `FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode`(⟳ §0.5): `FrameEditorView.xaml` 소스에서
  정책 배너 `Border`(`Brush.Warning.Surface`)를 추출해 **`IsCreateMode` + `InverseBoolToVis` Visibility 게이트**가
  붙어 있는지, 그리고 배너 행에 **`MinHeight="88"`**이 남아 있는지 정적 검증.
  VM 단위 테스트로는 XAML 바인딩 소실을 잡을 수 없으므로 이 축이 필요하다.

### 5.4 무회귀 기준

| 항목 | 기준 |
|------|------|
| `dotnet build -c Release` | **error 0 / warning 0** (기준선 유지) |
| `dotnet test` | 전량 통과. 총계는 삭제(≈19건: diff 9 + policy 4 + editor 6) / 추가(≈35건)로 **675 → 690 내외**. **675 미만으로 떨어지면 커버리지 후퇴로 간주하고 보강**(§2 A8) |
| 프레임 삭제 회귀 | `FrameSelectViewModelTests` 전량 통과 = 파일 잠금(§1.13) 회귀 없음 |
| `web/functions` | **변경 없음**(F1-D3). `tsc --noEmit` / jest는 다른 설계 문서 소관 |

---

## §6. 구현 WBS

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 Step은 **self-contained** — 대화 컨텍스트 없는 fresh 에이전트가 그 Step만 읽고 실행 가능해야 한다.
> 공통 규약(모든 Step): `.cs`는 **UTF-8 BOM 없음** 유지, `.xaml`은 기존 인코딩 유지, file-scoped namespace, 한글 XML doc 주석. 앱 실행(UI 기동) 금지.
> **원칙**: 각 Step은 그 Step만으로 `dotnet build -c Release`(error 0/warning 0) + `dotnet test`가 **녹색**이어야 한다. 그래서 계약을 바꾸는 Step은 깨지는 테스트 수정을 **같은 Step 안에서** 처리한다. 신규 커버리지 추가는 Step 8에 모은다.
>
> **실행 순서(번호 순이 아님 — 빌드 녹색 유지를 위한 의존 순서)**:
> `Step 1 → Step 3 → Step 2 → Step 4 → Step 5 → Step 6 → Step 7 → Step 8 → Step 9`
> 이유: `UpdateAsync`의 유일한 호출자가 `FrameEditorViewModel.SaveToDb`이므로, 호출자를 지우는 Step 3이 계약을 줄이는 Step 2보다 먼저여야 중간 상태에서도 빌드가 깨지지 않는다.
> 병렬 가능: Step 4(XAML 리소스 추출)는 Step 1~3과 독립적으로 진행 가능.

### Step 1: Core 순수 계층 — `FrameNaming` 신규 / `FrameEditPolicy` 교체 / `FrameDiff` 제거

- **Context Brief**: MCPhoto의 프레임 편집은 it15부터 **로컬 전용**이 된다. it2에서 만든 "로컬만 / DB도 업데이트" 팝업과 그 판정용 순수 함수(`FrameEditPolicy.RequiresDbUpdatePrompt`, `FrameDiff`)가 정책과 충돌하므로 제거하고, 대신 "DB/번들 유래 프레임을 편집하면 원본을 보존하고 새 이름으로 분기(fork)한다"는 규칙을 순수 함수로 도입한다. 프레임 이름은 `LocalFrameStore`의 파일명이 되며 `_`는 공용/개인 구분자라 새 이름에 도입하면 안 된다(`src/MCPhoto.Core/Frames/LocalFrameStore.cs:57-59`).
- **대상 파일**:
  - 신규 `src/MCPhoto.Core/Frames/FrameNaming.cs`
  - 수정 `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`
  - 삭제 `src/MCPhoto.Core/Frames/FrameDiff.cs`, `tests/MCPhoto.Tests/FrameDiffTests.cs`
  - 수정 `tests/MCPhoto.Tests/FrameEditPolicyTests.cs`
- **선행 조건**: 없음
- **구현 내용**:
  1. `FrameNaming` 추가 — 본 설계 §3.4의 시그니처·규칙 그대로(`CopySuffix = "사본"`, `NextCopyName`, `StripCopySuffix`, Ordinal 비교, 접미 정규식 `^(?<base>.*?)\s*사본(\s+(?<n>\d{1,2}))?$`, 1~99 초과 시 GUID 8자, 빈 이름 → `"새 프레임 사본"`).
  2. `FrameEditPolicy.RequiresDbUpdatePrompt` **삭제**, `RequiresFork(FrameTemplate) => FrameOrigin.Classify(frame) != FrameOriginKind.UserLocal` **추가**. `CanEdit`은 손대지 않는다.
  3. `FrameDiff.cs`와 `FrameDiffTests.cs` 삭제. 이 시점에 `FrameEditorViewModel.SaveToDb`가 `FrameDiff`를 참조하므로 **컴파일이 깨진다** → Step 3 전에 빌드를 녹색으로 만들 수 없다. 따라서 **Step 1과 Step 3을 하나의 커밋 단위로 수행**하거나, Step 1에서 `FrameDiff.cs` 삭제를 보류하고 Step 3에서 함께 지운다. **권장: Step 1에서는 `FrameDiff` 삭제를 보류하고 나머지만 수행, 삭제는 Step 3에서**.
  4. `FrameEditPolicyTests.cs:74-89`의 `RequiresDbUpdatePrompt_*` 4개 삭제, §5.3(B)의 `RequiresFork` 4케이스 추가.
- **검증 명령**:
  ```
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~FrameEditPolicyTests|FullyQualifiedName~FrameNamingTests|FullyQualifiedName~FrameOriginTests"
  ```
- **완료 기준**:
  - [관측] `FrameNaming.NextCopyName("기본프레임", ["기본프레임 사본"])`가 `"기본프레임 사본 2"`를 반환하고, `RequiresFork`가 bundle/fallback/DbDefault에 true·`local:`에 false를 반환한다(테스트 통과).
  - [non-goal] `FrameEditPolicy.CanEdit`의 동작이 바뀌지 않는다 — `FrameEditPolicyTests`의 `CanEdit` 계열 9개가 **수정 없이** 그대로 통과한다. `FrameOrigin`·`LocalFrameStore`·`SlotLayout` 무변경.
  - [trigger] 이 Step은 순수 함수만 다룬다 — UI·저장 경로·DI 등록은 이 Step에서 건드리지 않는다.
- **롤백**: `git checkout -- src/MCPhoto.Core/Frames/ tests/MCPhoto.Tests/FrameEditPolicyTests.cs` + 신규 파일 삭제. Step 2 이후와 독립.
- [ ] 완료

### Step 2: 저장소 계약 축소 — `IFrameRepository`에서 update-by-id 제거

- **Context Brief**: it15부터 WPF 앱은 프레임을 서버에 **업데이트하지 않는다**(편집은 로컬 전용). 클라이언트에 업데이트 호출 코드를 0으로 만들기 위해 인터페이스에서 관련 멤버를 뺀다. **서버 라우트 `PUT /frames/{id}`(`web/functions/src/routes/frames.ts:85`)는 유지한다** — 운영/관리 도구 전용으로 남기며 이 Step에서 서버는 손대지 않는다. 같은 이터레이션에서 레거시 Firebase 직결 경로(`MCPhoto.Firebase.FrameRepository`)가 병렬로 삭제되고 있으므로 구현체 전수 확인이 필요하다.
- **대상 파일**:
  - `src/MCPhoto.Core/Frames/IFrameRepository.cs`
  - `src/MCPhoto.Http/HttpFrameRepository.cs`
  - (조건부) `src/MCPhoto.Firebase/FrameRepository.cs` — 아직 존재하면 해당 멤버만 제거
  - `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`, `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`, `tests/MCPhoto.Tests/Http/**` 중 `IFrameRepository` 스텁 보유 파일
- **선행 조건**: 없음(Step 1과 병렬 가능)
- **구현 내용**:
  1. **먼저 구현체 전수 확인**: `grep -rn ": IFrameRepository" src/ tests/ --include=*.cs`. 결과에 나온 모든 타입에서 아래 멤버를 제거해야 한다.
  2. `IFrameRepository`에서 `bool SupportsUpdateById`(`:23`)와 `Task<FrameTemplate> UpdateAsync(...)`(`:31`) 제거(XML doc 포함).
  3. `HttpFrameRepository`에서 `SupportsUpdateById`(`:100`)·`UpdateAsync`(`:107-130`) 및 **이 메서드에서만 쓰는** 요청 DTO/헬퍼 제거. **`PutImageAsync`(`:167`)는 `SaveAsync`도 쓰므로 유지**한다.
  4. 테스트 스텁(`CapturingFrameRepository`, `StubRepo` 등)에서 같은 멤버와 그 관측 프로퍼티(`Updated`, `LastReplaceImage`) 제거.
  5. `FrameEditorViewModel.SaveToDb`가 아직 `_repository.UpdateAsync`를 호출하므로 **이 Step 단독으로는 빌드가 깨진다** → Step 3과 **연속 수행**하거나, Step 3을 먼저 하고 이 Step을 뒤에 둔다. **권장 순서: Step 3 → Step 2**.
- **검증 명령**:
  ```
  grep -rn "SupportsUpdateById\|UpdateAsync" src/ tests/ --include=*.cs   # 결과 0줄이어야 함
  dotnet build -c Release
  dotnet test
  ```
- **완료 기준**:
  - [관측] `grep -rn "SupportsUpdateById\|UpdateAsync" src/ tests/ --include=*.cs`가 **0줄**을 반환하고, `dotnet build -c Release`가 error 0 / warning 0으로 끝난다.
  - [non-goal] `web/functions/` 아래 파일이 **하나도 바뀌지 않는다**(`git status`에 `web/` 변경 없음). `IFrameRepository`의 나머지 멤버(`GetDefaultFramesAsync`/`GetUserFramesAsync`/`SaveAsync`/`DeleteAsync`/`DeleteAllByUserAsync`)와 `HttpFrameRepository.SaveAsync` 동작 무변경.
  - [trigger] 프레임 서버 등록은 **power의 신규 생성 저장**에서만 계속 발생한다(`SaveAsync`) — 편집 저장은 어떤 경로로도 서버를 호출하지 않는다.
- **롤백**: `git checkout -- src/MCPhoto.Core/Frames/IFrameRepository.cs src/MCPhoto.Http/HttpFrameRepository.cs tests/`
- [ ] 완료

### Step 3: `FrameEditorViewModel` F1 재작업 — DB 업데이트 경로 제거 + fork 저장

- **Context Brief**: `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`는 it2에서 "power가 DB 공용 기본 프레임을 편집·저장하면 '로컬만 / DB도 업데이트 / 취소' 팝업을 띄우는" 로직을 갖고 있다(`:279-286`, `:337-442`). it15 정책은 **"프레임 편집은 해당 PC에서만 적용"** 이므로 이 경로를 전부 제거하고, 대신 **DB/번들 유래 프레임을 편집하면 원본 파일을 보존하고 새 이름으로 분기(fork) 저장**하도록 바꾼다. 이렇게 하면 `FrameCatalogService`가 이름 기준으로 dedup 하므로(`src/MCPhoto.App/Services/FrameCatalogService.cs:66`) 원본 이름이 로컬에 남아 **DB 재다운로드가 발생하지 않는다**. 저장 스코프(power=공용 `{이름}.png` / user=개인 `{계정}_{이름}.png`)는 현행 그대로 유지한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`, 삭제 `src/MCPhoto.Core/Frames/FrameDiff.cs`, 수정 `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`
- **선행 조건**: Step 1(`FrameNaming`, `FrameEditPolicy.RequiresFork`)
- **구현 내용**:
  1. **제거**: `IsDbUpdatePromptVisible`/`DbUpdateNotice`/`DbUpdateNoticeIsError`(`:50-54`), `_originalImageBytes`/`_originalSlots`/`_originalName`(`:34-36` + `LoadForEdit`의 세팅 `:146-151`,`:161`), `SaveLocalOnly`/`SaveToDb`/`CancelDbUpdatePrompt`(`:340-431`), `BuildDbFrame`(`:434-442`), `EditingServerId`(`:259-266`), `Save()`의 팝업 분기(`:279-286`). `GoToFrameSelectAsync`(`:423-427`)는 **유지**. 이 시점에 `FrameDiff.cs` 삭제.
  2. **추가 상태**: `private enum FrameSessionSource { New, EditOwnLocal, ForkFromCatalog }` + `_sessionSource`(기본 `New`), `_sourceName`(fork 원본 이름), `public bool IsCreateMode => !_isEditing;`
  3. **`SaveScopeNotice`** 계산 프로퍼티 추가 — §3.1(b) 표의 5가지 문구. `FrameName`에 `[NotifyPropertyChangedFor(nameof(SaveScopeNotice))]` 부착 + 세션 초기화 지점에서 `OnPropertyChanged(nameof(SaveScopeNotice))`.
  4. **`LoadForEdit` 수정**: `FrameEditPolicy.RequiresFork(frame)`이면 `_sessionSource = ForkFromCatalog`, `_sourceName = frame.Name`, `FrameName = FrameNaming.NextCopyName(frame.Name, 현재 스코프 기존 이름들)`; 아니면 `_sessionSource = EditOwnLocal`, `FrameName = frame.Name`(현행). diff 스냅샷 코드는 삭제.
  5. **`Save()` 재작성**: §3.6 의사 코드 그대로. 저장 가드(fork + power 스코프 + `FrameName == _sourceName` → `StatusMessage = "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요."`, 저장 중단). power fork/power 로컬편집은 `FrameTemplate.Id = ""`로 `SaveLocal(..., ownerName: null)` → `#dbid` 미기록. `catch (IOException ex) { StatusMessage = ex.Message; }` 추가.
  6. **`_` 경고**: power 스코프 저장 성공 시 `FrameName.Contains('_')`이면 `StatusMessage`에 `"이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다."` 를 남긴다(**비차단** — 저장은 완료하고 화면 전환도 수행).
  7. **테스트 정리**: `FrameEditorViewModelTests.cs:156-285`의 DB 팝업 6개 삭제. `MakeVm`의 `Password = "pw"` 제거(§2 A4). 남은 3개(`SlotCountOptions_*`, `SlotCount_Change_*`, `User_Save_*`, `Power_Save_*`)는 통과 유지.
- **검증 명령**:
  ```
  grep -n "IsDbUpdatePromptVisible\|SaveToDb\|SaveLocalOnly\|FrameDiff\|RequiresDbUpdatePrompt" -r src/ tests/ --include=*.cs   # 0줄
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~FrameEditorViewModelTests|FullyQualifiedName~FrameSelectViewModelTests"
  ```
  ⚠️ `FrameEditorView.xaml`이 아직 `IsDbUpdatePromptVisible`을 바인딩하므로 **런타임 바인딩 경고**가 남는다(빌드 실패는 아님). Step 5에서 해소한다.
- **완료 기준**:
  - [관측] power가 DB 기본 프레임을 `LoadForEdit`한 뒤 `SaveCommand`를 실행하면 팝업 없이 즉시 저장되고, `repo.Saved == null`(DB 미호출) + `local.SavedFrame.Id == ""`(#dbid 미기록) + `FrameName`이 `"{원본이름} 사본"` 이다.
  - [non-goal] **power의 신규 생성 저장은 여전히 DB에 등록된다** — `Power_Save_Persists_To_Db_And_Local_Cache` 테스트가 수정 없이 통과한다. user 로컬 저장 경로(`{계정}_{이름}`), 슬롯 배치·스케일·드래그 로직(`ArrangeSlots`/`ApplyScale`/`UpdateSlot`), `LoadImage` 동작 무변경.
  - [trigger] fork 저장은 **편집 대상의 출처가 UserLocal이 아닐 때만** 발생한다 — 자기 로컬 프레임 편집은 이름 변경 없이 같은 파일을 덮어쓴다(negative case: `LoadForEdit(local: 프레임)` 후 `FrameName`에 `"사본"`이 붙지 않는다).
- **롤백**: `git checkout -- src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs src/MCPhoto.Core/Frames/FrameDiff.cs tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`
- [ ] 완료

### Step 4: 썸네일 카드 리소스 추출 (`Themes/Controls.xaml`) + `FrameSelectView` 재구성

- **Context Brief**: F2에서 편집기에 "기존 프레임 선택 모달"을 추가하는데, 그 썸네일 그리드는 `src/MCPhoto.App/Views/FrameSelectView.xaml`(`:17-104`)의 카드와 **시각이 같아야** 한다. 현재 그 카드 템플릿에는 삭제 ✕ 버튼이 박혀 있고 그 `MultiBinding`이 `DataContext.CanDeleteFrames`/`IsPower`를 ListBox 조상에서 찾으므로(`:94-98`), 그대로 재사용하면 피커 VM에 없는 경로를 바인딩하게 된다. 따라서 **카드 시각 본체만** 테마로 추출하고 삭제 버튼은 FrameSelect 쪽에 남긴다. ⚠️ WPF에서 `ResourceDictionary`는 독립 파싱이라 형제 딕셔너리 키를 `StaticResource`로 교차 참조할 수 없다 — 이 프로젝트는 각 테마 파일이 의존 딕셔너리를 **자체 재병합**하는 방식으로 이를 회피하며, `tests/MCPhoto.Tests/XamlResourceTests.cs:128-171`이 이를 정적으로 강제한다.
- **대상 파일**: `src/MCPhoto.App/Themes/Controls.xaml`, `src/MCPhoto.App/Views/FrameSelectView.xaml`
- **선행 조건**: 없음(Step 1~3과 병렬 가능)
- **구현 내용**:
  1. `Controls.xaml` 루트에 `xmlns:conv="clr-namespace:MCPhoto.App.Converters"` 추가.
  2. `Controls.xaml`에 3개 리소스 추가:
     - `<conv:FilePathToImageConverter x:Key="FrameCard.FilePathToImage" />` — **같은 딕셔너리 안에 정의**해야 자체 해석 테스트를 통과한다. App.xaml의 `FilePathToImage`와 키가 달라 충돌 없음.
     - `x:Key="FrameCard.ItemContainer"` `Style TargetType="ListBoxItem"` — `FrameSelectView.xaml:27-50`의 Setter/ControlTemplate을 **그대로 이동**.
     - `x:Key="FrameCard.Content"` `DataTemplate`(DataType 없음) — `FrameSelectView.xaml:53-84`의 200×280 Grid에서 **삭제 ✕ 버튼(`:85-100`)만 뺀** 나머지(Image + 슬롯 Viewbox + 이름 바). Image의 컨버터는 `{StaticResource FrameCard.FilePathToImage}`로 교체.
  3. `FrameSelectView.xaml` 수정: `ItemContainerStyle="{StaticResource FrameCard.ItemContainer}"` 지정하고 인라인 Style 제거. `ItemTemplate`은 `<Grid Width="200" Height="280">` 안에 `<ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource FrameCard.Content}" />` + **기존 삭제 ✕ 버튼 XAML을 한 글자도 바꾸지 않고** 그대로 배치.
- **검증 명령**:
  ```
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~XamlResourceTests|FullyQualifiedName~FrameSelectViewModelTests"
  ```
- **완료 기준**:
  - [관측] `XamlResourceTests.Each_Theme_File_Resolves_Its_Own_StaticResource_References("Controls.xaml")`가 통과하고, 테마 로드 시 `FrameCard.ItemContainer`가 `Style`·`FrameCard.Content`가 `DataTemplate`으로 해석된다.
  - [non-goal] `FrameSelectViewModelTests` 14개가 **수정 없이 전량 통과** — 삭제 ✕ 버튼의 `RelativeSource AncestorType=ListBox` 커맨드/가시성 바인딩이 `ContentPresenter` 한 겹 추가로 깨지지 않아야 한다(§2 A6). App.xaml의 기존 `FilePathToImage` 등록은 **제거하지 않는다**(다른 View가 사용).
  - [trigger] 카드 리소스는 `ItemContainerStyle`/`ItemTemplate` 지정으로만 적용된다 — 암시적(`x:Key` 없는) 스타일을 만들지 않는다(기존 다른 `ListBox`에 영향 금지).
- **롤백**: `git checkout -- src/MCPhoto.App/Themes/Controls.xaml src/MCPhoto.App/Views/FrameSelectView.xaml`
- [ ] 완료

### Step 5: `FrameEditorView` F1 UI — 상단 안내 배너 + DB 팝업 오버레이 제거 + 저장 캡션

- **Context Brief**: it15 요구 F1은 *"프레임 편집 시 어떤 계정으로 편집을 하든 '해당 PC에서만 적용됩니다.' 문구 노출"*이다. `src/MCPhoto.App/Views/FrameEditorView.xaml`은 현재 2컬럼 Grid(`*`/`320`)이고, 좌측 캔버스 카드와 우측 컨트롤 패널이 각각 상단 `Margin` 88(앱 셸 헤더 공간)을 갖는다. 여기에 화면 폭 전체 배너 행을 추가하고, it2에서 만든 DB 업데이트 확인 팝업 오버레이(`:89-116`)를 제거한다. 배너 시각은 `src/MCPhoto.App/Views/LoginGuestView.xaml:13-17`의 오프라인 경고 배너와 동일 톤을 재사용한다.
- **대상 파일**: `src/MCPhoto.App/Views/FrameEditorView.xaml`
- **선행 조건**: Step 3(VM에서 `IsDbUpdatePromptVisible` 등 제거, `SaveScopeNotice` 추가)
- **구현 내용**:
  1. 최상위 `Grid`에 `RowDefinitions` 추가: `<RowDefinition Height="Auto"/><RowDefinition Height="*"/>`.
  2. **Row 0 배너**(`Grid.ColumnSpan="2"`, `Margin="40,88,40,0"`):
     `Border Background="{StaticResource Brush.Warning.Surface}" CornerRadius="{StaticResource Radius.M}" Padding="16,10"` 안에
     `TextBlock Style="{StaticResource Text.Caption}" Foreground="{StaticResource Brush.Warning}" TextWrapping="Wrap"`
     Text = **`이 프레임 편집은 해당 PC에서만 적용됩니다. 서버의 기본 프레임은 변경되지 않으며, 다른 PC에는 반영되지 않습니다.`**
     `Visibility` 바인딩을 **붙이지 않는다**(역할·출처·모드 무관 항상 노출).
  3. 기존 좌측 `Border`(`:14`)와 우측 `StackPanel`(`:30`)에 `Grid.Row="1"` 지정, `Margin` 상단값 `88 → 12`로 변경.
  4. 저장 버튼 위(`StatusMessage` TextBlock `:80-81` 바로 위)에 `SaveScopeNotice` 캡션 추가:
     `TextBlock Text="{Binding SaveScopeNotice}" Style="{StaticResource Text.Caption}" Foreground="{StaticResource Brush.Text.Muted}" TextWrapping="Wrap" Margin="0,0,0,8"`.
  5. **DB 업데이트 확인 팝업 오버레이(`:89-116`) 전체 삭제.**
- **검증 명령**:
  ```
  grep -n "IsDbUpdatePromptVisible\|DbUpdateNotice\|SaveLocalOnlyCommand\|SaveToDbCommand" src/MCPhoto.App/Views/FrameEditorView.xaml   # 0줄
  grep -c "해당 PC에서만 적용됩니다" src/MCPhoto.App/Views/FrameEditorView.xaml                                                          # 1
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~XamlResourceTests"
  ```
- **완료 기준**:
  - [관측] `FrameEditorView.xaml`에 안내 문구가 정확히 1회 존재하고 DB 팝업 관련 바인딩이 0줄이며, `XamlResourceTests`가 참조 키를 모두 해석한다(`Brush.Warning.Surface`/`Brush.Warning`/`Radius.M`/`Text.Caption`/`Brush.Text.Muted`는 모두 테마에 존재).
  - [non-goal] 배너 추가로 **슬롯 편집 캔버스의 좌표 변환이 바뀌지 않는다** — `SlotCanvas`가 좌표계 기준이므로(`FrameEditorView.xaml.cs:70-73`) 레이아웃 행 추가가 드래그 정합에 영향을 주면 안 된다. `EditorTransformTests` 통과. 코드비하인드(`FrameEditorView.xaml.cs`)는 **수정하지 않는다**.
  - [trigger] 배너는 **조건 없이 항상** 표시된다 — 어떤 역할·프레임 출처·생성/편집 모드에서도 `Visibility` 트리거가 걸리지 않는다(negative case: XAML에 배너 `Border`의 `Visibility` 속성/바인딩이 존재하지 않을 것).
- **롤백**: `git checkout -- src/MCPhoto.App/Views/FrameEditorView.xaml`
- [ ] 완료

### Step 6: `FramePickerViewModel` 신규 + DI + `ApplyPickedFrame` + F2 커맨드

- **Context Brief**: it15 요구 F2는 *"프레임 생성 시 '이미지 불러오기' 밑에 '기존 프레임 불러오기' 버튼 → 파일 탐색기가 아니라 프레임 선택 페이지와 유사한 모달에서 기존 프레임을 골라 이미지+슬롯을 새 프레임 편집 세션으로 복사"* 다. 모달은 **새 `Window`가 아니라 편집기 내부 오버레이**로 만든다(테스트에서 `Window`를 `new` 할 수 없는 제약: `.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`). 이 Step은 VM/로직만 만들고 XAML은 Step 7에서 붙인다. 후보 목록은 `src/MCPhoto.App/Services/FrameCatalogService.cs`의 `GetDefaultFramesAsync()`(공용=번들+DB캐시+DB다운로드) + `GetUserFramesAsync(userId)`(개인 로컬)로, `FrameSelectViewModel.ReloadFramesAsync`(`:63-84`)와 동일하다.
- **대상 파일**: 신규 `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs`, 수정 `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`, `src/MCPhoto.App/ServiceRegistration.cs`, `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`(생성자 시그니처)
- **선행 조건**: Step 1(`FrameNaming`), Step 3(`_sessionSource`/`IsCreateMode`/`SaveScopeNotice`)
- **구현 내용**:
  1. `FramePickerViewModel` 생성 — §4.4 시그니처 그대로(`Frames`/`SelectedFrame`/`IsLoading`/`EmptyNotice`/`HasSelection`/`LoadAsync(userId, ct)`/`Reset()`). `ObservableObject` 상속, `FrameCatalogService` 주입. **이벤트를 정의하지도 구독하지도 않는다**(누수 방지). `System.Windows` 타입 미사용.
  2. `ServiceRegistration.cs`의 VM 등록 구역(`FrameEditorViewModel` 등록 근처, `:217`)에 `services.AddTransient<FramePickerViewModel>();` 추가.
  3. `FrameEditorViewModel` 생성자에 `FramePickerViewModel picker` 파라미터 추가(마지막 필수 인자, `logger`보다 앞) → `public FramePickerViewModel Picker { get; }`.
  4. `IsFramePickerVisible` + `OpenFramePicker`/`ConfirmPickFrame`/`CancelPickFrame` 커맨드 + `_pickerCts`(§4.7 코드 그대로).
  5. `ApplyPickedFrame(FrameTemplate src)` 구현 — §4.6 의사 코드 그대로. **반드시 `LoadImage(src.ImageUrl)`를 경유**(번들 `.jpg` 대응 + 장변 4000 축소) 후 슬롯을 `scale = FrameWidth / src.ImageSize.Width`로 보정하고 `SlotLayout.ClampToFrame` 적용. `_sessionSource = ForkFromCatalog`, `_sourceName = src.Name`, `_isEditing` **불변**, `FrameName = FrameNaming.NextCopyName(...)`. **임시 파일을 만들지 않는다.**
  6. 테스트의 `new FrameEditorViewModel(...)` 호출부에 picker 인자 추가.
- **검증 명령**:
  ```
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~FrameEditorViewModelTests|FullyQualifiedName~FrameCatalogServiceTests"
  ```
- **완료 기준**:
  - [관측] `ApplyPickedFrame(src)` 호출 후 `Slots.Count == src.Slots.Count`, `FrameName == "{src.Name} 사본"`, `IsCreateMode == true`가 성립하고, **소스 png 파일의 바이트가 호출 전후 동일**하다.
  - [non-goal] 디스크에 **새 파일이 하나도 생기지 않는다** — `ApplyPickedFrame` 전후로 `Frame\` 폴더 파일 목록이 동일하다(임시 파일 부재). `FilePathToImageConverter`(`src/MCPhoto.App/Converters/CommonConverters.cs:16-40`)는 **수정하지 않는다**(OnLoad/IgnoreImageCache/Freeze 유지 — 파일 잠금 회귀 금지).
  - [trigger] 편집 세션 교체는 **`ConfirmPickFrame`(불러오기 버튼)에서만** 일어난다 — `SelectedFrame` 변경만으로는 편집기 상태가 바뀌지 않고, `CancelPickFrame` 후에도 편집기 상태·디스크가 무변경이다.
- **롤백**: 신규 파일 삭제 + `git checkout -- src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs src/MCPhoto.App/ServiceRegistration.cs tests/`
- [ ] 완료

### Step 7: `FrameEditorView` F2 UI — "기존 프레임 불러오기" 버튼 + 선택 모달 오버레이

- **Context Brief**: Step 6에서 만든 `FramePickerViewModel`과 `OpenFramePicker`/`ConfirmPickFrame`/`CancelPickFrame` 커맨드를 화면에 붙인다. 모달은 새 `Window`가 아니라 `src/MCPhoto.App/Views/FrameEditorView.xaml` 최상위 `Grid`의 마지막 자식으로 두는 **오버레이**이며, 프로젝트의 기존 팝업 패턴(`src/MCPhoto.App/Views/FrameSelectView.xaml:107-129` 삭제 확인 오버레이)과 동일하게 `Brush.Scrim` 배경 Grid + 중앙 `Card` Border 구조를 쓴다. 썸네일 그리드는 Step 4에서 `Themes/Controls.xaml`로 추출한 `FrameCard.ItemContainer`/`FrameCard.Content`를 재사용한다.
- **대상 파일**: `src/MCPhoto.App/Views/FrameEditorView.xaml`
- **선행 조건**: Step 4(공유 카드 리소스), Step 5(배너·행 구조), Step 6(VM 커맨드)
- **구현 내용**:
  1. 우측 컨트롤 패널에서 "이미지 불러오기" 버튼(`:33-34`)의 `Margin`을 `"0,0,0,20"` → `"0,0,0,8"`로 바꾸고, **바로 아래**에 §4.1의 "기존 프레임 불러오기" 버튼 추가(`Command="{Binding OpenFramePickerCommand}"`, `Visibility="{Binding IsCreateMode, Converter={StaticResource BoolToVis}}"`, `Style="{StaticResource Button.Secondary}"`, `HorizontalAlignment="Stretch"`, `Margin="0,0,0,20"`).
  2. 최상위 `Grid`의 마지막 자식으로 피커 오버레이 추가 — **§4.8의 XAML 구조를 그대로** 따른다. 핵심: `Grid.RowSpan="2" Grid.ColumnSpan="2"`, `Visibility="{Binding IsFramePickerVisible, Converter={StaticResource BoolToVis}}"`, **`DataContext`는 `ListBox`에만 `{Binding Picker}`로 좁힌다**(버튼은 편집기 VM 스코프 유지 — 넓게 걸면 `ConfirmPickFrameCommand`가 조용히 실패한다).
  3. 오버레이 내부: 제목(`Text.H2`, "기존 프레임 불러오기"), 로딩 텍스트(`Picker.IsLoading`), 빈 목록 안내(`Picker.EmptyNotice`), 썸네일 `ListBox`(`WrapPanel` ItemsPanel, 세로 스크롤), 하단 `[불러오기]`(`Button.Primary`, `IsEnabled="{Binding Picker.HasSelection}"`) / `[취소]`(`Button.Ghost`).
  4. 사용 컨버터는 `BoolToVis`/`InverseBoolToVis`뿐 — **신규 컨버터를 만들지 않는다**(App.xaml·`XamlResourceTests` allowlist 변경 불필요).
- **검증 명령**:
  ```
  grep -c "OpenFramePickerCommand\|ConfirmPickFrameCommand\|CancelPickFrameCommand" src/MCPhoto.App/Views/FrameEditorView.xaml   # 3
  grep -n "FrameCard.ItemContainer\|FrameCard.Content" src/MCPhoto.App/Views/FrameEditorView.xaml                                 # 각 1줄
  dotnet build -c Release
  dotnet test --filter "FullyQualifiedName~XamlResourceTests"
  ```
- **완료 기준**:
  - [관측] `FrameEditorView.xaml`이 3개 피커 커맨드와 공유 카드 리소스 2개를 참조하고, `XamlResourceTests`가 이 View의 모든 테마 `StaticResource`를 해석한다(빌드 error 0 / warning 0).
  - [non-goal] **편집 모드에서는 버튼이 보이지 않는다** — `IsCreateMode` 바인딩이 걸려 있어야 하며(`grep`으로 확인), 오버레이가 닫힌 상태에서 기존 편집 UI(캔버스·슬롯 컨트롤·저장/취소)의 레이아웃·동작이 변하지 않는다. Step 5에서 추가한 안내 배너와 `SaveScopeNotice` 캡션은 그대로 남는다.
  - [trigger] 모달은 **"기존 프레임 불러오기" 버튼 클릭 시에만** 열리고(`OpenFramePickerCommand`), **"불러오기" 버튼 클릭 시에만** 편집 세션이 교체된다. 썸네일 선택(`SelectedItem` 변경)만으로는 아무 일도 일어나지 않는다.
- **롤백**: `git checkout -- src/MCPhoto.App/Views/FrameEditorView.xaml`
- [ ] 완료

### Step 8: 테스트 전면 개편 — 신규 커버리지 + `XamlResourceTests` 확장

- **Context Brief**: Step 1~7에서 프레임 편집이 로컬 전용(fork 저장)으로 바뀌고 "기존 프레임 불러오기" 모달이 추가됐다. 이 Step은 그 동작을 회귀 테스트로 고정한다. **WPF `Window`를 테스트에서 `new` 하면 안 된다** — `System.Windows.Application`은 AppDomain당 1개 + 스레드 친화라 `XamlResourceTests`와 같은 프로세스에서 레이스로 실패한다(`.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`). 모달을 오버레이로 설계했으므로 모든 로직이 VM 단위 테스트로 검증 가능하다. XAML은 소스 텍스트에서 `{StaticResource key}`를 정규식 추출해 테마 딕셔너리 `Contains(key)`로만 확인한다.
- **대상 파일**: 신규 `tests/MCPhoto.Tests/FrameNamingTests.cs`, `tests/MCPhoto.Tests/FramePickerViewModelTests.cs`; 수정 `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`, `FrameEditPolicyTests.cs`, `LocalFrameStoreTests.cs`, `XamlResourceTests.cs`
- **선행 조건**: Step 1~7 전부
- **구현 내용**: 본 설계 §5.3 표 그대로 구현한다.
  - (A) `FrameNamingTests` 9케이스 — Step 1에서 이미 만들었다면 누락분만 보강.
  - (B) `FrameEditPolicyTests`에 `RequiresFork` 4케이스.
  - (C) `FrameEditorViewModelTests`에 C1~C4, C6~C13 (C5는 빌드로 커버, 테스트 불필요). `CapturingLocalStore`에 `PublicFrameNames`/`LoadUser` 반환값 주입 세터 추가.
  - (D) `FramePickerViewModelTests` D1~D7. `FrameCatalogService`는 **인터페이스 추출 없이** 스텁 `IFrameRepository`/`ILocalFrameStore`를 주입한 실제 인스턴스를 사용(`FrameCatalogServiceTests.cs` 패턴 재사용).
  - (E) `LocalFrameStoreTests`에 E1~E3(원본 불변 / `#dbid` 미기록 / `PublicFrameNames` 유지).
  - (F) `XamlResourceTests.Item1a_View_StaticResource_Keys_Resolve_In_Theme`의 `InlineData`에 `"FrameEditorView.xaml"`, `"FrameSelectView.xaml"` 추가 + 신규 Fact `FrameCard_Shared_Resources_Exist_In_Theme`.
  - ⚠️ App.xaml에 **새 컨버터를 추가하지 않았으므로** `appKeys` allowlist(3곳 중복 하드코딩: `XamlResourceTests.cs:198-203`, `:241-246`, `:275-280`, `:307-312`)는 **갱신 불필요**. 만약 테스트가 "테마에 없는 StaticResource"로 실패하면 해당 키를 그 메서드의 `appKeys`에 추가한다.
- **검증 명령**:
  ```
  dotnet build -c Release
  dotnet test
  ```
- **완료 기준**:
  - [관측] `dotnet test` 전량 통과하고 총 테스트 수가 **기준선 675 이상**이다(§2 A8). C8(장변 4000 축소 시 슬롯 스케일)·C9(jpeg 소스)·C10(소스 파일 불변)이 통과해 §2 A3·A7·F2-D4 가정이 검증된다.
  - [non-goal] 기존 프레임 테스트(`FrameOriginTests`, `SlotLayoutTests`, `EditorTransformTests`, `FrameCatalogServiceTests`, `BundleFrameTests`, `DefaultFrameTests`, `FallbackFrameTests`, `FrameSelectViewModelTests`)가 **수정 없이** 통과한다. 테스트에서 `Window`를 `new` 하는 코드를 **한 줄도 추가하지 않는다**.
  - [trigger] 테스트는 `dotnet test` 실행으로만 검증한다 — 앱 UI 기동 없음.
- **롤백**: `git checkout -- tests/` + 신규 테스트 파일 삭제
- [ ] 완료

### Step 9: 문서 동기화

- **Context Brief**: it15 F1·F2로 프레임 편집 정책이 바뀌었다(서버 반영 없음, DB/번들 유래 편집은 fork). 앱은 더 이상 `PUT /frames/{id}`를 호출하지 않지만 **서버 라우트는 유지**되므로, 계약 문서에 "앱 미사용(관리 전용)"을 명시해 다음 작업자가 오해하지 않게 한다.
- **대상 파일**: `docs/design/firebase-contract.md`, `docs/analysis/11-exe-app-features.md`, `docs/analysis/90-roadmap-and-future-work.md`
- **선행 조건**: Step 1~8
- **구현 내용**:
  1. `firebase-contract.md`의 `PUT /frames/{id}` 항목에 주석 추가: *"⚠️ it15부터 WPF 앱은 호출하지 않는다(프레임 편집은 로컬 전용, `docs/design/wpf-it15-frame-ux-design.md` §3.2). 운영/관리 도구 전용 라우트."* 먼저 `grep -n "PUT /frames" web/functions/src/routes/frames.ts`로 라우트 존속을 재확인(§2 A2).
  2. `11-exe-app-features.md`의 프레임 편집·생성 절에 F1(로컬 전용 + 안내 배너 + fork 규칙)과 F2(기존 프레임 불러오기 모달)를 반영.
  3. `90-roadmap-and-future-work.md` §1에 후속 과제 1건 등재: *"프레임 피커 썸네일 가상화/`DecodePixelWidth` 미적용 — 후보 수가 늘면 오픈 지연 가능. `FilePathToImageConverter`의 OnLoad+IgnoreImageCache 규약을 깨지 않는 방식으로만 개선할 것."*
- **검증 명령**:
  ```
  grep -n "it15" docs/design/firebase-contract.md docs/analysis/11-exe-app-features.md docs/analysis/90-roadmap-and-future-work.md
  grep -n "PUT /frames" web/functions/src/routes/frames.ts
  ```
- **완료 기준**:
  - [관측] 세 문서 모두에 it15 프레임 변경 사항이 기재되어 있고, `web/functions/src/routes/frames.ts`에 `PUT /frames` 라우트가 여전히 존재한다(A2 검증).
  - [non-goal] **코드 파일이 하나도 바뀌지 않는다** — `git status`의 변경 목록이 `docs/` 하위로만 제한된다. `web/` 무변경.
  - [trigger] 문서 갱신은 Step 8의 `dotnet test` 전량 통과 이후에만 수행한다(구현이 확정된 뒤 기술).
- **롤백**: `git checkout -- docs/`
- [ ] 완료

---

## §7. 파일별 역할 (변경 인벤토리)

| 파일 | 구분 | 책임 |
|------|------|------|
| `src/MCPhoto.Core/Frames/FrameNaming.cs` | **신규** | 사본 이름 생성 순수 함수(§3.4) |
| `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` | 수정 | `RequiresDbUpdatePrompt` 삭제 / `RequiresFork` 추가. `CanEdit` 무변경 |
| `src/MCPhoto.Core/Frames/FrameDiff.cs` | **삭제** | 유일 호출자(`SaveToDb`) 제거 |
| `src/MCPhoto.Core/Frames/IFrameRepository.cs` | 수정 | `SupportsUpdateById`·`UpdateAsync` 제거 |
| `src/MCPhoto.Http/HttpFrameRepository.cs` | 수정 | 위 두 멤버 구현 제거(`PutImageAsync`·`SaveAsync`는 유지) |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | 수정 | DB 팝업·diff 제거 / `_sessionSource`·`_sourceName`·`SaveScopeNotice`·`IsCreateMode` / `Save()` 재작성 / `ApplyPickedFrame` / 피커 3커맨드 |
| `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs` | **신규** | 선택 모달 목록 VM(§4.4). 이벤트 0개 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | 수정 | F1 배너 행 / DB 팝업 오버레이 삭제 / `SaveScopeNotice` 캡션 / F2 버튼 + 피커 오버레이 |
| `src/MCPhoto.App/Views/FrameSelectView.xaml` | 수정 | 카드 시각을 공유 리소스로 교체(삭제 ✕ 버튼 XAML은 원문 유지) |
| `src/MCPhoto.App/Themes/Controls.xaml` | 수정 | `FrameCard.FilePathToImage` / `FrameCard.ItemContainer` / `FrameCard.Content` 추가 |
| `src/MCPhoto.App/ServiceRegistration.cs` | 수정 | `AddTransient<FramePickerViewModel>()` |
| `src/MCPhoto.App/Views/FrameEditorView.xaml.cs` | **무변경** | 슬롯 드래그 렌더링 전담 — 손대지 않는다 |
| `src/MCPhoto.App/Converters/CommonConverters.cs` | **무변경** | 파일 잠금 규약(OnLoad/IgnoreImageCache/Freeze) 보존 |
| `src/MCPhoto.Core/Frames/LocalFrameStore.cs` · `FrameOrigin.cs` · `SlotLayout.cs` | **무변경** | 파일명 규약·출처 판정·배치 로직 그대로 |
| `web/functions/**` | **무변경** | `PUT /frames/{id}` 유지(F1-D3) |
| 테스트 | 신규 2 / 수정 4 / 삭제 1 | §5.2·§5.3 |

## §8. 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1) / 미검증 가정(§2) 목록이 분리되어 있다 — §1은 전부 `파일:줄` 근거 포함
- [x] 모든 가정(A1~A9)에 검증 단계가 매핑되어 있다 — A1→S2, A2→S9, A3→S5(C8), A4→S6/S3, A5→S4, A6→S4, A7→S5(C9), A8→S7(=Step 8), A9→S3
- [x] 모든 Step(1~9)에 7개 필수 필드가 채워져 있다 (Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측 / non-goal / trigger)이다 — UI Step(5·7)은 negative case 포함
- [x] 검증 명령이 자동 실행 가능한 형태다 (`dotnet build -c Release`, `dotnet test --filter ...`, `grep`)
- [x] 이벤트 구독마다 해제 경로가 명시됐다 — **이번 설계는 신규 이벤트 구독이 0개**(§4.4: Picker는 이벤트를 정의·구독하지 않고, 확인/취소 커맨드를 소유자 VM에 둔다)
- [x] 리소스 키 충돌 없음 — 신규 키는 `FrameCard.*` 접두로 격리, App.xaml의 `FilePathToImage`와 별개
- [x] ViewModel이 UI 없이 테스트 가능 — `FramePickerViewModel`·`FrameEditorViewModel` 모두 `System.Windows` 타입을 노출하지 않으며, 모달을 오버레이로 설계해 `Window` 인스턴스화가 필요 없다
- [x] UI 스레드 블로킹 없음 — 목록 로딩은 전 구간 `await` + `CancellationToken`, 백그라운드에서 UI 요소 직접 갱신 없음
- [x] 인코딩 보존 항목이 명시됐다 (§0.4)

> **미해결로 남기는 항목**(사용자 결정 불필요, 후속 백로그): 피커 썸네일 UI 가상화·`DecodePixelWidth`(F2-D5, Step 9에서 `90-roadmap`에 등재).

