# 프레임 신규 생성 재정의 + 서버 등록 확인 팝업 설계

- **대상**: MCPhoto WPF 클라이언트 (.NET 8, MVVM + CommunityToolkit.Mvvm, DI = Microsoft.Extensions.DependencyInjection)
- **브랜치**: `fix/windows-ui-tweak` (HEAD `f5225cc` 위에 증분 작업 — 새 브랜치 만들지 않음)
- **작성자**: wpf-architect / **구현자**: wpf-developer / **검증자**: wpf-code-reviewer
- **빌드 검증**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug` (현재 경고 0 / 오류 0 — 유지 필수)
- **테스트 검증**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`
- **선행 설계**: `docs/design/wpf-it15-frame-ux-design.md` (F1 로컬 전용 편집 · F2 기존 프레임 불러오기), `docs/design/wpf-it16-advanced-user-role-design.md` (역할 2축)

---

## 1. 요구사항 (사용자 원문 요약)

| # | 요건 | 현재 동작 | 목표 동작 |
|---|------|-----------|-----------|
| **R1** | "기존 프레임 불러오기(F2)로 만들 때는 **사본이 아니라** 그 프레임의 정보를 기본값으로 한 **새 프레임 생성**. 프레임 이름도 새로 등록할 수 있어야 한다" | `ApplyPickedFrame`이 세션을 `ForkFromCatalog`로 바꾸고 이름을 `"{원본} 사본"`으로 덮어써서 **F1 편집과 거의 동일**해진다 | 세션은 **신규 생성(`New`)** 유지, 이름은 사용자가 정한다(자동 "사본" 네이밍 제거) |
| **R2** | "프레임 만들기 저장 시, DB에 올릴 수 있는 계정(manager/admin)은 **서버에도 생성할지 체크박스가 있는 팝업**(삭제 팝업과 비슷한 것)을 띄우고, 체크된 경우에만 DB insert. 아니면 로컬만" | power + 신규 생성이면 **무조건** `_repository.SaveAsync` (DB insert) | 저장 버튼 → 확인 오버레이 → 체크 시에만 DB insert, 미체크면 로컬 공용만 |

**범위 밖(non-goal)**: F1 "선택 편집"(`LoadForEdit`) 경로의 fork/사본 동작, 서버 API 계약, `PUT /frames/{id}` 부활, 프레임 삭제 흐름, 웹(`web/`) 어느 것도 변경하지 않는다.

---

## 2. 검증된 사실 (verified facts)

코드를 직접 읽고 확인한 것만 기재한다.

| 사실 | 근거 |
|------|------|
| `ApplyPickedFrame`이 마지막에 `_sessionSource = ForkFromCatalog; _sourceName = src.Name; FrameName = FrameNaming.NextCopyName(...)`를 수행한다 | `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs:405-409` |
| `_sessionSource`(`New` / `EditOwnLocal` / `ForkFromCatalog`)가 저장 방식 판정의 유일한 축이다 | 같은 파일 `:34-48`, `:455-457` |
| `Save()`는 `isPower && isNew`일 때 **무조건** `SaveAsync` + `SaveLocal(ownerName:null)` | 같은 파일 `:471-485` |
| `isPower && !isNew`은 로컬 공용만 저장하며 `Id = string.Empty`로 `#dbid`를 기록하지 않는다 | 같은 파일 `:486-500`, `LocalFrameStore.cs:22,147-148` |
| 비power(AdvancedUser)는 전 케이스 개인 로컬 `{계정}_{이름}.png`, DB 미호출 | 같은 파일 `:501-513` |
| 원본 덮어쓰기 가드는 `isFork && isPower && FrameName == _sourceName` **한 가지뿐**이다 | 같은 파일 `:461-465` |
| `_localStore.SaveLocal`은 같은 이름의 기존 파일을 **경고 없이 덮어쓴다**(`File.WriteAllBytes`) | `src/MCPhoto.Core/Frames/LocalFrameStore.cs:42-48` |
| `LocalFrameStore.EnsureFileNameSafe`는 private static이며 빈 이름·금지문자에 `IOException`을 던진다 | 같은 파일 `:133-140` |
| `IsPower()` = `Manager or Admin`, `CanWriteFrames()` = `AdvancedUser or Manager or Admin` — **서로 대체 금지**(주석에 명시적 경고) | `src/MCPhoto.Core/Models/UserRole.cs:49-64` |
| `IsCreateMode => !_isEditing`이고 `ApplyPickedFrame`은 `_isEditing`을 건드리지 않는다 → F2 불러오기 후에도 create 모드 | `FrameEditorViewModel.cs:66,404` |
| "기존 프레임 불러오기" 버튼은 `IsCreateMode`로 게이트되어 **F1 편집 세션에서는 노출되지 않는다** → `ApplyPickedFrame`은 create 모드에서만 호출된다 | `src/MCPhoto.App/Views/FrameEditorView.xaml:65-67` |
| 삭제 확인 팝업 패턴 = `Grid`(RowSpan 전체 + `Brush.Scrim`) + `Visibility={Binding Is...Visible, Converter=BoolToVis}` + `Border Style=Card` + `CheckBox`(기본 off) + 확인/취소 | `src/MCPhoto.App/Views/FrameSelectView.xaml:57-80` |
| 삭제 팝업 VM 패턴: `RequestDelete`에서 `DeleteAlsoServer = false` 리셋 → `ConfirmDelete`에서 **닫히기 전에 값을 지역 변수로 확정** | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:104-132` |
| 편집기 피커 오버레이에 "DataContext를 오버레이 전체에 걸지 말 것" 경고 주석이 있고, 실제로 `DataContext`는 목록 `ListBox` **한 곳**에만 걸려 있다 | `FrameEditorView.xaml:131-133,151` |
| 저장/취소 버튼은 `ScrollViewer` 밖 하단 고정 바에 있다(HEAD `f5225cc` 리팩터링) | `FrameEditorView.xaml:122-128` |
| `ExistingNamesForCurrentScope()`가 power=`PublicFrameNames()`, 비power=`LoadUser(id).Select(Name)`를 반환하고 실패는 비차단(빈 집합) | `FrameEditorViewModel.cs:419-434` |
| 피커 후보 목록 = `GetDefaultFramesAsync`(로컬 공용+번들+DB캐시) + `GetUserFramesAsync`(본인 `{계정}_` 접두) → **모든 후보는 로컬 파일에 대응** | `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs:46-65`, `Services/FrameCatalogService.cs:50-109` |
| `FrameCatalogService`의 DB dedup 키는 **이름**이다(로컬에 같은 이름 파일이 있으면 재다운로드 안 함) | `FrameCatalogService.cs:58-69` |
| 서버 삭제는 `#dbid` 실패 시 **이름 매칭 폴백**이 있다 → `#dbid` 미기록이 삭제를 완전히 막지 않는다 | `FrameSelectViewModel.cs:152-173` |
| `SaveScopeNotice`의 power+New 문구는 `"저장 시 '{FrameName}'이(가) 공용 기본 프레임으로 서버에 등록됩니다."` | `FrameEditorViewModel.cs:83` |
| 기존 테스트가 이 문구를 문자열로 검증한다(`Assert.Contains("서버에 등록", ...)` 3곳) | `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs:265,276-277,289` |
| `XamlResourceTests`가 `FrameEditorView.xaml`의 StaticResource 키 전수 검사 + 배너 `IsCreateMode` 게이트 정적 검증을 수행한다 | `tests/MCPhoto.Tests/XamlResourceTests.cs:249,274-287` |
| 대상 소스/XAML 파일 4종 모두 **UTF-8 without BOM** | `head -c 3` 결과: `.cs`=`usi`, `.xaml`=`<Us` (BOM 없음) |
| `BoolToVis`(true→Visible), `InverseBoolToVis`, `NullToVis`(null→Visible)만 있고 **문자열 공백 → Visibility 컨버터는 없다** | `src/MCPhoto.App/App.xaml:21-36`, `Converters/CommonConverters.cs:43-80` |
| GUI exe 직접 실행이 환경 정책 훅으로 차단됨 → 시각 검증 불가, 빌드+단위 테스트만 가능 | 임무 브리프 |

## 3. 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| A1 | `FrameNaming`에 `IsFileNameSafe` 순수 함수를 추가하고 `LocalFrameStore.EnsureFileNameSafe`가 이를 위임해도 기존 `LocalFrameStoreTests`가 통과한다(동일 판정: 공백/금지문자) | Step 1 |
| A2 | `ApplyPickedFrame`이 `FrameName`을 건드리지 않아도 나머지 F2 동작(슬롯 스케일 복사·원본 불변·jpeg 경유)이 회귀하지 않는다 | Step 2 |
| A3 | 저장 검증(이름 충돌 가드)을 `ForkFromCatalog` 세션에도 적용해도 기존 F1 테스트 4종이 모두 통과한다(사본 이름은 생성 시점에 충돌 회피되어 있으므로) | Step 3 |
| A4 | `SaveCommand`를 "검증 + 팝업 표시"로 바꾸고 실제 저장을 `ConfirmServerRegisterCommand`로 옮겨도, 비power·F1 경로의 `SaveCommand.ExecuteAsync(null)` 호출 계약(즉시 저장)이 유지된다 | Step 4 |
| A5 | 새 오버레이가 기존 피커 오버레이와 z-order·히트테스트에서 충돌하지 않는다(동시 표시 불가 + 뒤에 선언된 것이 위) | Step 6 (정적) / 시각 확인은 사용자 확인 필요 |
| A6 | 새 팝업이 참조하는 StaticResource 키(`Brush.Scrim`·`Card`·`Shadow.Pop`·`Button.Primary`·`Button.Ghost`·`Text.H2`·`Text.Body`·`Text.Caption`·`Brush.Text.Muted`·`Brush.Bg`)가 테마에서 전부 해석된다 | Step 6 (`XamlResourceTests`) |
| A7 | `CheckBox`에 명시 스타일 없이도(삭제 팝업과 동일) 테마 암시 스타일로 렌더된다 | Step 6 정적 통과 + 시각은 사용자 확인 필요 |

---

## 4. 설계 쟁점 결론 (D1~D6)

### D1 — 이름 충돌 / 원본 덮어쓰기 방지 **(최우선: 데이터 손실)**

**문제**: R1로 F2 세션이 `New`가 되면 유일한 가드(`isFork && isPower && FrameName == _sourceName`, `:461`)가 발동하지 않는다.
power가 "프레임A"를 불러와 이름을 그대로 두고 저장하면 `SaveLocal`이 `Frame\프레임A.png` + `.slots`를 **경고 없이 덮어쓴다**(`LocalFrameStore.cs:46-48`).
체크박스까지 켜져 있으면 서버에도 동명 문서가 추가로 insert되어 로컬/서버가 동시에 오염된다.
같은 구멍은 F2 없이도 이미 존재한다(빈 편집기에서 기존 공용 프레임과 같은 이름을 타이핑 → 조용한 덮어쓰기).

**결론: 저장 전 차단 + 이름 변경 요구 (fail-closed). 자동 증분·경고 후 진행 모두 채택하지 않는다.**

판정 규칙(단문 1개로 표현 가능):

> **덮어쓰기가 세션의 의도인 경우(`_sessionSource == EditOwnLocal`)만 예외**로 하고, 그 밖의 세션(`New`, `ForkFromCatalog`)은 `FrameName`이 **현재 저장 스코프의 기존 이름 집합**에 있으면 저장을 거부한다.

- 스코프 집합은 기존 `ExistingNamesForCurrentScope()`를 그대로 재사용한다(power=`PublicFrameNames()`, 비power=`LoadUser(id)`). 조회 실패는 이미 비차단(빈 집합) — 가드가 조용히 꺼지는 최악을 막기 위해 **기존 `_sourceName` 가드는 삭제하지 않고 그대로 남긴다**(2중 방어).
- 비교는 `StringComparer.Ordinal` — `LocalFrameStore`의 파일명 규약(`StringComparer.Ordinal`, `PublicFrameNames`)과 동일 축.
- **가드 실행 순서 고정**: ① 권한 → ② 슬롯 유효성 → ③ `_sourceName` 가드(기존 문구 유지) → ④ 이름 안전성 → ⑤ 스코프 충돌 → ⑥ 팝업/저장.
  ③을 ⑤보다 먼저 두는 이유: 기존 테스트가 `Assert.Contains("원본과 같은 이름", vm.StatusMessage)`로 문구를 검증한다(`FrameEditorViewModelTests.cs:257`) — 순서를 바꾸면 메시지가 뒤바뀌어 회귀로 보인다.
- **F1 `ForkFromCatalog`에도 ⑤를 적용하는 근거**: 자동 생성된 사본 이름은 이미 스코프 충돌을 회피한 값이므로(`NextCopyName`) 이 가드는 **사용자가 직접 기존 이름을 타이핑한 경우에만** 발동한다. 그 경우 현행 동작은 "다른 공용 프레임을 조용히 파괴"이며, 이는 it15가 fork를 도입한 목적(원본 보존)과 정면으로 어긋난다 → 차단이 정합적이다. `LoadForEdit`의 **이름 제안·세션 판정 로직은 한 줄도 바꾸지 않는다**.
- **이름 안전성 선검증(④)도 함께 도입**: 현재는 `SaveAsync`(서버 insert) 성공 후 `SaveLocal`이 `IOException`(금지문자·빈 이름)으로 실패할 수 있어 **서버에만 문서가 남는 반쪽 상태**가 가능하다. 순수 함수로 미리 걸러 서버 호출 자체를 막는다(→ D6 원자성과 짝).

**한계(의도적 수용, 문서화)**
- **서버 측 동명 문서**는 검사하지 않는다. 이유: 서버 이름 조회는 네트워크 실패 시 저장 자체를 막게 되고(오프라인 저장 회귀), 로컬 dedup이 이름 기준이라 캐시가 생기면 재다운로드 문제도 없다. 서버 중복은 `SaveAsync`가 새 GUID를 부여하므로 데이터 파괴가 아니라 목록 중복이며, 삭제는 이름 매칭 폴백으로 처리 가능하다(`FrameSelectViewModel.cs:162-173`).
- `EditOwnLocal` 세션에서 **다른** 기존 개인 프레임 이름으로 개명 → 현행대로 덮어쓴다(F1 경로 불변 원칙 우선). 이 문서 §9의 "사용자 확인 필요"에 후속 후보로 남긴다.
- 이름 끝 공백 등 파일시스템이 정규화하는 변형으로 가드를 우회하는 경우는 다루지 않는다(원문 저장 규약 유지, `LocalFrameStore` §sanitize 없음).

### D2 — F2 불러오기 직후 제안할 이름

**결론: `ApplyPickedFrame`은 `FrameName`을 전혀 건드리지 않는다.** (원본 이름 그대로 채우지도, `NextCopyName` 사본도, 빈 값으로 비우지도 않는다.)

- 세션이 create 모드일 때만 호출되므로(사실 §2) `FrameName`은 기본값 `"새 프레임"`이거나 **사용자가 이미 타이핑한 값**이다. 후자를 보존하는 것이 "정보를 기본값으로 새로 만든다"는 R1 문구에 가장 가깝고, 사용자가 먼저 이름을 정한 뒤 이미지를 불러오는 순서도 지원한다.
- 원본 이름을 그대로 채우면 D1 가드에 의해 **저장 시 100% 차단되는 값**을 제안하는 셈이다(공용/번들 원본은 스코프 집합에 반드시 존재) → 채택 불가.
- 빈 값은 저장 버튼(`CanSave`)이 활성인데 `IOException`으로 실패하는 상태를 만들며, `SaveScopeNotice`가 `''`로 렌더된다 → 채택 불가. (`CanSave`에 이름 조건을 넣는 것은 슬롯 유효성 축을 오염시키므로 하지 않는다.)
- 대신 **무엇을 불러왔는지**를 명시하는 안내를 새로 노출한다: `PickedSourceNotice = "'{원본이름}'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요."` — 이름 입력 필드 바로 위, muted 캡션. 이것이 "사본이 아니라 새로 만드는 중"이라는 유일한 시각 신호다.
- `FrameNaming.NextCopyName`은 **F1 경로에서 계속 사용되므로 삭제하지 않는다**(`LoadForEdit:209`). `FrameNamingTests`는 그대로 유효하다.
- 이름 필드가 이미 `TwoWay`/`UpdateSourceTrigger=PropertyChanged`로 편집 가능하다(`FrameEditorView.xaml:70`) → R1의 "이름도 새로 등록"은 추가 UI 없이 충족되며, 자동 네이밍 제거가 실질 변경이다.

### D3 — 팝업 노출 조건

**결론: `IsPower() && _sessionSource == FrameSessionSource.New` 일 때만 노출한다.** (= DB insert 분기와 **완전히 동일한 조건**)

| 상황 | 세션 | 팝업 | 저장 결과 |
|------|------|------|-----------|
| power, 빈 편집기 신규 생성 | `New` | **표시** | 체크 on → DB insert + 로컬 공용(`#dbid` 기록) / off → 로컬 공용만(`Id=""`) |
| power, F2로 기존 프레임 불러온 뒤 저장 (R1 적용 후) | `New` | **표시** | 위와 동일 |
| power, F1 "선택 편집"(DB/번들 유래) | `ForkFromCatalog` | 없음 | 로컬 공용 fork (it15 로컬 전용 정책) |
| power, F1 "선택 편집"(본인 로컬) | `EditOwnLocal` | 없음 | 같은 이름 덮어쓰기 |
| AdvancedUser, 신규 생성/F2/F1 | 전부 | 없음 | 개인 로컬 `{계정}_{이름}.png`, DB 미호출 |
| user·temp_user (게이트 우회 시) | — | 없음 | `CanWriteFrames()` fail-closed 거부(기존 유지) |

- 판정 축을 `IsCreateMode`가 아니라 `_sessionSource == New`로 쓰는 이유: 팝업은 "DB insert를 할지 묻는 것"이고 DB insert 분기의 조건이 `isPower && isNew`다. 두 축이 갈라지면 "팝업은 떴는데 등록은 안 되는" 조용한 불일치가 생긴다. (현재 두 값은 동치이지만 **동치에 의존하지 않는다**.)
- 권한 축은 **`IsPower()`만** 사용한다. `CanWriteFrames()`(AdvancedUser 포함)로 대체하면 DB 권한이 없는 계정에 서버 등록 체크박스를 노출하게 된다 — `UserRole.cs:49-64`의 명시 경고 위반.
- it15가 편집 경로에서 의도적으로 없앤 팝업을 되살리지 않는다(편집=로컬 전용 정책 유지).

### D4 — 체크박스 기본값

**결론(확정): 기본 on (`DefaultRegisterToServer = true`).** 팝업을 열 때마다 이 기본값으로 리셋한다(직전 선택 잔존 금지).

> ⚠️ **설계 시 제안은 기본 off였으나, 사용자가 운영 판단으로 기본 on을 선택해 뒤집혔다.** 근거: manager/admin이 프레임을 **새로 만드는** 목적은 대개 공용 배포이므로, 매번 체크를 요구하면 정상 워크플로에 마찰이 생긴다. 아래 off 근거는 기각 이력으로 남긴다.
>
> 삭제 팝업(`DeleteAlsoServer`, 기본 off)과 값이 갈리는 것은 관례 위반이 아니다 — **축이 다르다**: 그쪽은 파괴적 행위라 opt-in, 이쪽은 생성 행위다. 대신 미체크 저장이 오인되지 않도록 팝업 캡션이 두 결과를 모두 명시한다(아래 고정 문구).
>
> 이 값을 다시 뒤집을 때 함께 움직이는 기대값: 테스트 **N1·N2·N4·N13**. (특히 **N2**는 "체크 off로 확인" 경로를 기본값에 의존해 표현하고 있었으므로 명시적 대입으로 고쳤다 — 기본값만 바꾸면 이 테스트가 깨진다.)

기각된 off 근거(이력):
1. **참조 패턴과 동일** — 사용자가 지목한 삭제 팝업의 `DeleteAlsoServer`도 기본 off이며 `RequestDelete`에서 매번 리셋한다(`FrameSelectViewModel.cs:111`).
2. **전역 부작용은 명시적 opt-in** — 서버 등록은 다른 PC들이 이후 내려받는 배포 행위다. 로컬 전용은 되돌리기 쉽고(파일 삭제) 서버 등록은 그렇지 않다.
3. **it15 정책 방향과 정합** — it15는 프레임 변경의 서버 반영을 의도적으로 축소했다.
4. **기존 워크플로 회귀는 "조용하지 않다"** — 팝업이 뜨는 것 자체가 변화의 고지다.

회귀 완화: 팝업 캡션은 **체크 상태에 무관하게 두 결과를 모두 적는 고정 문구**로 둔다(동적 문구를 위한 컨버터를 새로 만들지 않는다).
> `체크하면 서버에 공용 기본 프레임으로 등록되어 다른 PC에서도 내려받습니다. 체크하지 않으면 이 PC에만 저장됩니다.`

### D5 — `SaveScopeNotice` 문구 정합화

**결론: power + `New` 분기의 문구만 교체하고, 배너/캡션 역할 분리(it15 §3.1(b))와 `'_'` 경고 로직은 그대로 유지한다.**

| 대상 | 현재 | 변경 후 |
|------|------|---------|
| power + `New` | `저장 시 '{FrameName}'이(가) 공용 기본 프레임으로 서버에 등록됩니다.` | `저장 시 '{FrameName}'을(를) 이 PC의 공용 목록에 만듭니다. 서버 등록 여부는 저장할 때 선택합니다.` |
| power + `ForkFromCatalog` | (유지) `원본은 그대로 두고 '{FrameName}'(으)로 이 PC의 공용 목록에 저장됩니다.` | 변경 없음 |
| power + `EditOwnLocal` / 비power | (유지) | 변경 없음 |
| `'_'` 포함 경고 접미 | (유지) | 변경 없음 |

- 상단 배너(`Brush.Warning.Surface` Border, `IsCreateMode` 게이트)는 **정책**을 말하고 이 캡션은 **이번 저장의 결과**를 말한다 — 두 문장이 모두 참이어야 한다는 it15 설계 의도를 유지한다. 배너 XAML은 손대지 않는다(`XamlResourceTests.FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode`가 정적으로 고정하고 있다).
- 문구 변경으로 **기존 테스트 3개 단언이 깨진다**(`서버에 등록` 문자열) → §7 테스트 영향 표대로 갱신한다. 새 문구에는 `"서버에 등록"`이 아니라 `"서버 등록 여부는"`이 들어가므로 부분 문자열 단언을 반드시 교체해야 한다.
- 팝업 캡션(D4)과 이 캡션은 **역할이 다르다**: 캡션 = 저장 전 예고, 팝업 = 선택 시점의 결과 확정. 문장을 복제하지 않는다.

### D6 — DB insert 실패 시 로컬 저장 처리

**결론: 원자성 유지 — 체크 on에서 `SaveAsync`가 실패하면 로컬 저장도 하지 않고, 화면 전환 없이 편집기에 머물며 재시도 경로를 안내한다.** (현행 예외 처리와 동일한 "전부 실패" 의미론)

```
StatusMessage = $"서버 등록 실패: {ex.Message} 이 PC에만 저장하려면 '서버에도 등록'을 해제하고 다시 저장해 주세요.";
```

근거:
1. **부분 성공이 사용자를 막는다** — 로컬만 저장해두면 재시도 시 D1 스코프 충돌 가드가 "이미 같은 이름이 있습니다"로 저장을 거부한다(자기 자신과의 충돌). 예외 처리를 위해 가드를 느슨하게 하면 D1의 데이터 손실 방어가 뚫린다.
2. **작업 손실이 없다** — 편집 세션(이미지·슬롯·이름·배율)이 그대로 살아 있고 화면 전환도 하지 않으므로 사용자는 체크만 해제해 즉시 로컬 저장할 수 있다.
3. **현행 동작과 동일** — 지금도 `SaveAsync` 예외 시 `SaveLocal`에 도달하지 않고 `StatusMessage`만 남는다(`:483-484`, `:531-535`) → 회귀 위험 0.
4. `ex.Message`를 그대로 노출하는 것은 이 코드베이스의 기존 관례다(`FrameSelectViewModel.cs:190` `서버 삭제 실패: {ex.Message}`) — 10개 초과 같은 구체적 사유가 가려지지 않는다.

성공 경로 순서는 현행 유지: `SaveAsync` → 반환된 프레임으로 `SaveLocal(saved, ownerName:null)` → `#dbid` 기록 → `GoToFrameSelectAsync()`.
`SaveLocal`이 실패하는 경우(서버는 성공)는 D1의 **이름 안전성 선검증**으로 사전 제거한다.

---

## 5. 아키텍처 설계

### 5.1 계층·책임 (변경 없음)

| 계층 | 구성 | 이번 변경 |
|------|------|-----------|
| View | `Views/FrameEditorView.xaml` (+ `.cs` 코드비하인드) | 캡션 1개 + 오버레이 1개 추가. **코드비하인드 무변경** |
| ViewModel | `ViewModels/FrameEditorViewModel.cs` | 세션 판정·검증·팝업 상태·저장 파이프라인 |
| Core(순수) | `Core/Frames/FrameNaming.cs`, `LocalFrameStore.cs` | 파일명 안전성 순수 함수 1개 추출 |
| Service 추상화 | `IFrameRepository`, `ILocalFrameStore` | **인터페이스 무변경** (계약 안정) |

`System.Windows` 타입은 VM에 새로 들어오지 않는다(팝업 상태는 `bool`, 문구는 `string`) → 창 없이 단위 테스트 가능. it15 메모(모달은 새 `Window` 금지, 오버레이+VM 상태)를 그대로 따른다.

### 5.2 세션 상태 축

`FrameSessionSource` enum은 **값을 추가하지 않는다**. R1은 F2가 부여하는 값을 `ForkFromCatalog` → `New`로 바꾸는 것으로 끝난다.

```
빈 편집기 진입            → New                (기존)
F2 기존 프레임 불러오기    → New   ← ★변경 (기존: ForkFromCatalog)
F1 편집, 카탈로그 유래     → ForkFromCatalog     (불변)
F1 편집, 본인 로컬        → EditOwnLocal        (불변)
```

`_sourceName`은 **F2에서도 계속 기록**한다(불러온 원본 추적·안내 문구용). 단 원본 이름 가드는 `isFork &&` 조건이 앞에 있어 `New` 세션에서는 발동하지 않는다 → 그 자리는 D1의 스코프 충돌 가드가 대신 막는다.

### 5.3 저장 파이프라인 (핵심 재구성)

```
[저장 버튼] SaveCommand (AsyncRelayCommand, 기존 시그니처 유지)
   │
   ├─ TryValidateForSave(out error) ── false ─→ StatusMessage = error; return   (저장·팝업 없음)
   │      ① user null                → "로그인이 필요합니다."
   │      ② !CanWriteFrames()        → "프레임을 만들 권한이 없습니다."          (fail-closed 유지)
   │      ③ 이미지/슬롯 무효          → "슬롯이 겹치거나 프레임을 벗어났습니다."
   │      ④ isFork && isPower && name==_sourceName → "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요."
   │      ⑤ 이름 공백                → "프레임 이름을 입력해 주세요."
   │      ⑥ 금지문자                 → "이름에 사용할 수 없는 문자가 있습니다."
   │      ⑦ 세션 != EditOwnLocal && 스코프에 동명 존재 → "이미 같은 이름의 프레임이 있습니다. 다른 이름을 입력해 주세요."
   │
   ├─ isPower && _sessionSource==New ─→ RegisterToServer=DefaultRegisterToServer;
   │                                    IsServerRegisterConfirmVisible=true; return  (아직 저장 안 함)
   │
   └─ 그 외 ─→ await PersistAsync(registerToServer: false)

[팝업 저장] ConfirmServerRegisterCommand (AsyncRelayCommand)
   → bool alsoServer = RegisterToServer;          // 닫히기 전에 확정(삭제 팝업 관례)
     IsServerRegisterConfirmVisible = false; RegisterToServer = DefaultRegisterToServer;
     await PersistAsync(alsoServer)

[팝업 취소] CancelServerRegisterCommand (RelayCommand, 동기)
   → IsServerRegisterConfirmVisible = false; RegisterToServer = DefaultRegisterToServer;   // 저장·전환·디스크 무변경

PersistAsync(bool registerToServer)
   → TryValidateForSave 재실행(fail-closed, 진입점이 2개이므로) ── false ─→ StatusMessage; return
     StatusMessage = "저장 중...";
     ┌ isPower && isNew && registerToServer : SaveAsync → (실패 시 D6 안내 후 return) → SaveLocal(saved, null)
     ├ isPower                              : SaveLocal(Id="", null)        // 로컬 공용, #dbid 미기록
     └ 비power                              : SaveLocal(UserId=user.Id, ownerName=user.Id)
     StatusMessage = ""; await GoToFrameSelectAsync();
```

`SaveCommand`가 `AsyncRelayCommand`로 남으므로 기존 테스트 호출(`vm.SaveCommand.ExecuteAsync(null)`)과 XAML 바인딩(`SaveCommand`)이 그대로 유효하다.

### 5.4 ViewModel 멤버 명세 (`FrameEditorViewModel`)

**신규**

| 멤버 | 종류 | 초기값 | 책임 |
|------|------|--------|------|
| `IsServerRegisterConfirmVisible` | `[ObservableProperty] bool` | `false` | 서버 등록 확인 오버레이 표시 |
| `RegisterToServer` | `[ObservableProperty] bool` | `DefaultRegisterToServer`(=`true`) | 체크박스 상태(D4: 열 때마다 기본값으로 리셋) |
| `PickedSourceNotice` | `[ObservableProperty] string`, `[NotifyPropertyChangedFor(nameof(HasPickedSource))]` | `""` | F2로 불러온 원본 안내(D2) |
| `HasPickedSource` | `public bool` (계산) | — | `!string.IsNullOrEmpty(PickedSourceNotice)` — 캡션 Visibility 게이트 |
| `ConfirmServerRegisterCommand` | `[RelayCommand] async Task ConfirmServerRegister()` | — | 팝업 확인 → 실제 저장 |
| `CancelServerRegisterCommand` | `[RelayCommand] void CancelServerRegister()` | — | 팝업 취소(무변경) |
| `TryValidateForSave(out string error)` | `private bool` | — | 저장 전 7단 검증(§5.3) |
| `PersistAsync(bool registerToServer)` | `private async Task` | — | 실제 저장 분기 |
| `RequiresServerRegisterPrompt` | `private bool` (계산) | — | `IsPower() && _sessionSource == New` (XAML 미노출) |

**변경**

| 멤버 | 변경 내용 |
|------|-----------|
| `ApplyPickedFrame(FrameTemplate src)` | 마지막 3줄 교체: `_sessionSource = New` / `_sourceName = src.Name` 유지 / `FrameName` 대입 **삭제** / `PickedSourceNotice` 설정 / `OnPropertyChanged(nameof(SaveScopeNotice))` 유지 |
| `LoadImage(string path)` | 성공 시 `PickedSourceNotice = string.Empty` 추가(직접 이미지 교체 후 안내가 사실과 어긋나지 않게) |
| `SaveScopeNotice` | power+`New` 문구 교체(D5) |
| `Save()` | 본문을 검증 + 팝업 분기 + `PersistAsync` 위임으로 재구성 |
| `ExistingNamesForCurrentScope()` | **시그니처·본문 유지**. 호출자만 2곳(사본 이름 계산 + 새 충돌 가드)으로 늘어난다. XML 주석의 "사본 이름 충돌 검사용" 문구를 "사본 이름 계산·저장 전 충돌 검사용"으로 갱신 |

**불변(건드리지 않음)**: `LoadForEdit`, `_isEditing`/`IsCreateMode`, `FrameSessionSource` enum 정의, `OpenFramePicker`/`ConfirmPickFrame`/`CancelPickFrame`, `Picker`, 슬롯 배치·스케일 로직 전체, `GoToFrameSelectAsync`, `Cancel`.

### 5.5 Core 순수 함수 (신규 1개)

```csharp
// src/MCPhoto.Core/Frames/FrameNaming.cs
/// <summary>
/// 로컬 파일명으로 쓸 수 있는 이름인지(빈 값·공백·파일시스템 금지문자 없음). 순수.
/// LocalFrameStore.EnsureFileNameSafe의 판정과 동일 — 저장 전 선검증에 쓰기 위해 추출했다.
/// </summary>
public static bool IsFileNameSafe(string? name)
    => !string.IsNullOrWhiteSpace(name)
       && name!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
```

`LocalFrameStore.EnsureFileNameSafe`는 이 함수로 위임하되 **예외 메시지는 현행 그대로 유지**한다(빈 이름 / 금지문자 두 갈래를 구분해서 던지므로 위임 후에도 두 갈래 판정을 유지):

```csharp
private static void EnsureFileNameSafe(string value)
{
    if (string.IsNullOrWhiteSpace(value)) throw new IOException("이름이 비어 있습니다.");
    if (!FrameNaming.IsFileNameSafe(value)) throw new IOException($"이름에 사용할 수 없는 문자가 있습니다: {value}");
}
```

`FrameNaming.cs`에 `using System.IO;`가 필요하다(현재 `System.Text.RegularExpressions`만 있음).

### 5.6 View ↔ ViewModel 매핑 / 바인딩·명령 명세

| View | ViewModel | DataContext 설정 방식 |
|------|-----------|------------------------|
| `FrameEditorView` (UserControl) | `FrameEditorViewModel` (Transient) | `App.xaml`의 `DataTemplate DataType="{x:Type vm:FrameEditorViewModel}"` — ViewModel-first, 셸이 VM을 스왑 |
| 편집기 내 프레임 피커 오버레이(기존) | `FramePickerViewModel` | **목록 `ListBox`에만** `DataContext="{Binding Picker}"` |
| 서버 등록 확인 오버레이(신규) | `FrameEditorViewModel` (상속) | **DataContext 지정 없음** — §5.7 참조 |

신규/변경 바인딩:

| 요소 | 바인딩 | 모드/트리거 |
|------|--------|-------------|
| 불러온 원본 캡션 `TextBlock.Text` | `PickedSourceNotice` | OneWay |
| 같은 캡션 `Visibility` | `HasPickedSource` + `BoolToVis` | OneWay |
| 오버레이 루트 `Grid.Visibility` | `IsServerRegisterConfirmVisible` + `BoolToVis` | OneWay |
| 팝업 대상 이름 `TextBlock.Text` | `FrameName` | OneWay |
| 체크박스 `IsChecked` | `RegisterToServer` | TwoWay(기본) |
| 팝업 [저장] `Command` | `ConfirmServerRegisterCommand` | — |
| 팝업 [취소] `Command` | `CancelServerRegisterCommand` | — |

`SaveScopeNotice`는 이미 `FrameName` 변경 시 `[NotifyPropertyChangedFor]`로 갱신된다(`:54-55`) → 추가 배선 불필요.

### 5.7 팝업 XAML 설계 — DataContext 함정 회피 (필수 준수)

기존 피커 오버레이 주석이 경고하는 함정: **오버레이 루트에 `DataContext`를 걸면 확인/취소 커맨드가 편집기 VM에 있어 바인딩이 조용히 실패한다(예외 없이 버튼만 비활성)**.

새 팝업의 회피 규칙:

1. **오버레이 루트(`Grid`)·`Border`·내부 어느 요소에도 `DataContext`를 설정하지 않는다.** 모든 바인딩(`IsServerRegisterConfirmVisible`, `FrameName`, `RegisterToServer`, 두 커맨드)이 상속된 편집기 VM에서 해석된다.
2. 하위 VM이 필요한 요소가 **없다**(목록·컬렉션 없음, 스칼라 상태 4개뿐) → 스코프를 좁힐 이유 자체가 없다.
3. `RelativeSource`/`ElementName` 우회도 쓰지 않는다 — 상속 DataContext가 이미 정답이므로 우회는 의도를 흐린다.
4. 회귀 방지를 **정적 테스트로 고정**한다: `FrameEditorView.xaml` 전체에서 `DataContext=` 출현 횟수가 **정확히 1**이고, 그 1개가 `DataContext="{Binding Picker}"`(피커 `ListBox`)여야 한다. 누군가 새 오버레이에 DataContext를 추가하면 테스트가 실패하며 사유 주석이 원인을 알려준다.
5. XAML에 경고 주석을 **새 오버레이에도 복제**한다(피커와 동일 표현) — 코드 읽는 순서상 두 오버레이가 인접하므로 한쪽만 있으면 규칙이 국지적으로 보인다.

선언 위치·z-order: 기존 피커 오버레이 `Grid` **바로 뒤(형제, 마지막 자식)**. 같은 `Grid.RowSpan="2" Grid.ColumnSpan="2"`. 두 오버레이는 동시 표시되지 않지만(피커는 저장 전에 닫힌다) 뒤 선언 = 위 렌더로 결정성을 확보한다.

`Background="{StaticResource Brush.Scrim}"`가 히트테스트를 잡아 뒤쪽 컨트롤(이름 TextBox·저장 버튼)의 조작을 막는다 → 팝업이 열린 동안 검증 결과가 흔들리지 않는다(그래도 `PersistAsync`는 재검증한다 — fail-closed).

### 5.8 팝업 XAML 골격 (그대로 사용 가능)

`FrameEditorView.xaml`의 피커 오버레이 `</Grid>` 다음, 최상위 `</Grid>` 앞에 삽입한다.

```xml
        <!-- R2: 서버 등록 확인 오버레이(파워 + 신규 생성 저장 시에만 — 삭제 확인 팝업과 동일 패턴).
             ⚠️ DataContext를 이 오버레이의 어느 요소에도 걸지 말 것 — 확인/취소 커맨드와 상태가 모두
             편집기 VM에 있어서 DataContext를 걸면 커맨드 바인딩이 조용히 실패한다(버튼만 비활성). -->
        <Grid Grid.RowSpan="2" Grid.ColumnSpan="2" Background="{StaticResource Brush.Scrim}"
              Visibility="{Binding IsServerRegisterConfirmVisible, Converter={StaticResource BoolToVis}}">
            <Border Style="{StaticResource Card}" Padding="32" Effect="{StaticResource Shadow.Pop}"
                    HorizontalAlignment="Center" VerticalAlignment="Center" MinWidth="420"
                    Background="{StaticResource Brush.Bg}">
                <StackPanel>
                    <TextBlock Text="이 프레임을 저장하시겠습니까?" Style="{StaticResource Text.H2}"
                               HorizontalAlignment="Center" Margin="0,0,0,8" />
                    <TextBlock Text="{Binding FrameName}" Style="{StaticResource Text.Body}"
                               Foreground="{StaticResource Brush.Text.Muted}"
                               HorizontalAlignment="Center" TextTrimming="CharacterEllipsis"
                               MaxWidth="360" Margin="0,0,0,20" />
                    <!-- D4: 기본 on(신규 생성은 대개 공용 배포가 목적). 열 때마다 VM이 기본값으로 리셋한다. -->
                    <CheckBox Content="서버에도 등록(공용 기본 프레임)" IsChecked="{Binding RegisterToServer}"
                              HorizontalAlignment="Center" Margin="0,0,0,8" />
                    <TextBlock Text="체크하면 서버에 공용 기본 프레임으로 등록되어 다른 PC에서도 내려받습니다. 체크하지 않으면 이 PC에만 저장됩니다."
                               Style="{StaticResource Text.Caption}" Foreground="{StaticResource Brush.Text.Muted}"
                               TextWrapping="Wrap" TextAlignment="Center" MaxWidth="360" Margin="0,0,0,20" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                        <Button Content="저장" Command="{Binding ConfirmServerRegisterCommand}"
                                Style="{StaticResource Button.Primary}" Margin="0,0,12,0" />
                        <Button Content="취소" Command="{Binding CancelServerRegisterCommand}"
                                Style="{StaticResource Button.Ghost}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>
```

불러온 원본 캡션은 **"프레임 이름" 레이블 바로 위**에 삽입한다(음수 Margin 금지, 숨김 시 간격이 깔끔히 접힘):

```xml
                    <!-- R1: F2로 불러온 원본 안내. 사본이 아니라 새 프레임을 만드는 중임을 알린다. -->
                    <TextBlock Text="{Binding PickedSourceNotice}" Style="{StaticResource Text.Caption}"
                               Foreground="{StaticResource Brush.Text.Muted}" TextWrapping="Wrap"
                               Visibility="{Binding HasPickedSource, Converter={StaticResource BoolToVis}}"
                               Margin="0,0,0,8" />
                    <TextBlock Text="프레임 이름" Style="{StaticResource Text.Label}" Margin="0,0,0,4" />
```

사용 리소스 키는 전부 기존 테마/App.xaml 정의분이다(신규 키 0개 → 키 충돌 없음): `Brush.Scrim`, `Brush.Bg`, `Brush.Text.Muted`, `Card`, `Shadow.Pop`, `Text.H2`, `Text.Body`, `Text.Caption`, `Text.Label`, `Button.Primary`, `Button.Ghost`, `BoolToVis`(App.xaml).

### 5.9 스레딩 모델

| 구간 | 스레드 | 근거 |
|------|--------|------|
| `TryValidateForSave` (파일 열거 포함: `PublicFrameNames`/`LoadUser`) | UI 스레드 동기 | 기존 `LoadForEdit`/사본 이름 계산과 동일 경로(`Frame\` 폴더 1회 열거, 파일 수는 수십 단위). 새로운 블로킹 유형을 도입하지 않는다 |
| `_repository.SaveAsync` | `await` (I/O 완료 후 UI 컨텍스트 복귀) | 기존과 동일. `.Result`/`.Wait()` 사용 금지 |
| `_localStore.SaveLocal` | UI 스레드 동기 | 기존과 동일(PNG 1개 쓰기) |
| `GoToFrameSelectAsync` | `await` | 기존과 동일 |
| 팝업 상태 변경(`IsServerRegisterConfirmVisible`, `RegisterToServer`) | UI 스레드 | 커맨드 실행 컨텍스트 = UI. `Dispatcher` 호출 불필요 |

- 백그라운드 스레드에서 UI/바인딩 대상 갱신은 **없다**(새 `Task.Run` 도입 없음).
- `ConfigureAwait(false)`는 쓰지 않는다 — VM 계층이며 이어지는 코드가 바인딩 대상을 갱신한다.
- 이벤트 구독을 **새로 만들지 않는다** → 구독 해제 경로가 필요한 신규 지점 0개(누수 위험 없음). 기존 `_pickerCts`(CancellationTokenSource)의 생명주기도 건드리지 않는다.
- `IProgress<T>`/타이머 도입 없음.

### 5.10 F1(`LoadForEdit`) 경로 불변 보장 매트릭스

| 항목 | F1 동작 | 이번 변경의 영향 |
|------|---------|------------------|
| 진입 시 세션 판정(`RequiresFork` → `ForkFromCatalog`/`EditOwnLocal`) | 유지 | 코드 무변경 |
| 사본 이름 제안(`NextCopyName`) | 유지 | `FrameNaming.NextCopyName` 무변경, 호출부 무변경 |
| `_isEditing = true` / 배너 노출 | 유지 | XAML 배너 블록 무변경 |
| `EditorTitle = "프레임 편집"` | 유지 | 무변경 |
| 저장 시 DB 미호출(로컬 전용) | 유지 | `isPower && isNew`만 DB 분기 → F1은 `isNew=false`이므로 불가 |
| 서버 등록 팝업 | **노출되지 않음** | 게이트가 `_sessionSource == New` |
| 원본 이름 가드 | 유지(문구·순서 그대로) | 새 가드보다 **먼저** 평가 |
| 스코프 충돌 가드 | **새로 적용됨**(`ForkFromCatalog`) | 자동 제안 이름은 충돌 회피값이라 정상 흐름 무영향. 사용자가 기존 이름을 직접 타이핑한 경우에만 차단 = 데이터 손실 방어(D1) |
| `EditOwnLocal` 덮어쓰기 | 유지 | 충돌 가드에서 명시적 예외 |

---

## 6. 파일별 역할 · 변경 범위

| 파일(절대 경로) | 역할 | 변경 |
|-----------------|------|------|
| `C:\STUDY\PROJECT\PhotoBooth\src\MCPhoto.Core\Frames\FrameNaming.cs` | 프레임 이름 순수 유틸 | `IsFileNameSafe` 추가, `using System.IO;` 추가. 기존 `NextCopyName`/`StripCopySuffix` 무변경 |
| `C:\STUDY\PROJECT\PhotoBooth\src\MCPhoto.Core\Frames\LocalFrameStore.cs` | 로컬 파일 저장소 | `EnsureFileNameSafe`가 `FrameNaming.IsFileNameSafe`에 위임(메시지·동작 동일) |
| `C:\STUDY\PROJECT\PhotoBooth\src\MCPhoto.App\ViewModels\FrameEditorViewModel.cs` | 편집기 VM | §5.4 표대로 |
| `C:\STUDY\PROJECT\PhotoBooth\src\MCPhoto.App\Views\FrameEditorView.xaml` | 편집기 View | 캡션 1개 + 오버레이 1개 추가(§5.8). 배너·캔버스·컨트롤 패널·하단 고정 바 무변경 |
| `C:\STUDY\PROJECT\PhotoBooth\tests\MCPhoto.Tests\FrameEditorViewModelTests.cs` | VM 단위 테스트 | 기존 5개 갱신 + 신규 14개(§7.3 N1~N14) + `CapturingFrameRepository`에 `ThrowOnSave` 플래그 |
| `C:\STUDY\PROJECT\PhotoBooth\tests\MCPhoto.Tests\FrameNamingTests.cs` | 순수 유틸 테스트 | `IsFileNameSafe` 케이스 추가 |
| `C:\STUDY\PROJECT\PhotoBooth\tests\MCPhoto.Tests\XamlResourceTests.cs` | XAML 정적 안전망 | DataContext 함정 회귀 테스트 1개 추가 |

**변경하지 않는 파일(명시)**: `IFrameRepository.cs`, `ILocalFrameStore.cs`, `FrameEditPolicy.cs`, `FrameOrigin.cs`, `FramePickerViewModel.cs`, `FrameSelectViewModel.cs`, `FrameSelectView.xaml`, `FrameEditorView.xaml.cs`, `App.xaml`, `Themes/*.xaml`, `ServiceRegistration.cs`, `web/` 전체.

**인코딩 규칙(필수)**: 위 7개 파일 모두 현재 **UTF-8 without BOM**이다. 편집 시 BOM을 추가하지 않는다(한글 주석·문구 다수 → 인코딩 변경 시 diff 전체가 오염된다).

---

## 7. 테스트 영향 범위

### 7.1 기존 테스트 — 갱신 필수 (5개)

| 테스트 | 깨지는 이유 | 갱신 방법 |
|--------|-------------|-----------|
| `Power_Save_Persists_To_Db_And_Local_Cache` (`:169`) | power+New 저장이 팝업으로 바뀌어 `SaveCommand` 한 번으로는 DB에 안 간다 | `SaveCommand.ExecuteAsync` 후 `Assert.True(vm.IsServerRegisterConfirmVisible)` + `Assert.Null(repo.Saved)` → `vm.RegisterToServer = true;` → `await vm.ConfirmServerRegisterCommand.ExecuteAsync(null)` → 기존 단언 유지 |
| `SaveScopeNotice_Reflects_Scope` (`:260`) | power+New 문구 교체(D5) | `Assert.Contains("서버에 등록", powerNew...)` → `Assert.Contains("서버 등록 여부는", ...)`; advNew의 `DoesNotContain("서버에 등록")` → `DoesNotContain("서버 등록")` |
| `IsCreateMode_Gates_LocalOnly_Banner` (`:280`, 특히 `:289`) | 같은 문구 단언 | 위와 동일하게 교체(배너 게이트 단언 자체는 그대로) |
| `SaveScopeNotice_Warns_Before_Save_When_Public_Name_Has_Underscore` (`:313`) | 마지막 절이 Admin+New 즉시 저장을 기대 | `SaveCommand` 후 `await vm.ConfirmServerRegisterCommand.ExecuteAsync(null)`(체크 off) 추가 → `Assert.NotNull(local.SavedFrame)` 유지. `'_'` 경고 단언은 무변경 |
| `ApplyPickedFrame_Copies_Slots_And_Suggests_Copy_Name` (`:358`) | 이름 자동 사본 제거(D2) | 테스트명을 `ApplyPickedFrame_Copies_Slots_And_Keeps_Editable_Name`으로, `Assert.Equal("클래식 사본", vm.FrameName)` → `Assert.Equal("새 프레임", vm.FrameName)` + `Assert.True(vm.HasPickedSource)` + `Assert.Contains("클래식", vm.PickedSourceNotice)` |

### 7.2 기존 테스트 — 통과 유지 확인 (회귀 감시)

`AdvancedUser_Save_Persists_Locally_With_Six_Slots`, `NonWriter_Save_Is_Refused_Fail_Closed`, `Power_Editing_Db_Default_Saves_Local_Only_With_Fork_Name`, `AdvancedUser_Editing_Own_Local_Overwrites_Same_Name`, `Fork_Save_Blocked_When_Name_Equals_Source_In_Public_Scope`, `Fork_Name_Avoids_Existing_Names_In_Scope`, `ApplyPickedFrame_Scales_Slots_When_Image_Downscaled`, `ApplyPickedFrame_Accepts_Jpeg_Source`, `ApplyPickedFrame_Does_Not_Modify_Source_File`, `ApplyPickedFrame_Missing_Image_Reports_Status`, `CancelPickFrame_Leaves_Editor_Untouched`, `ConfirmPickFrame_With_No_Selection_Is_Noop`, `SlotCount*` 2개.
`FrameNamingTests`(기존 케이스), `FrameEditPolicyTests`, `LocalFrameStoreTests`, `FrameSelectViewModelTests`, `FramePickerViewModelTests`, `XamlResourceTests`(기존), `AppStateTests` — **무영향**(AppStateTests는 상태머신만 다룬다).

### 7.3 신규 테스트 (VM 14개 + Core 순수 함수 케이스 + XAML 정적 1개)

**`FrameEditorViewModelTests.cs`** — 픽스처 보강: `CapturingFrameRepository`에 `public bool ThrowOnSave { get; set; }` 추가, `SaveAsync`에서 `if (ThrowOnSave) throw new InvalidOperationException("서버 오류");`

| # | 테스트명 | 검증 |
|---|----------|------|
| N1 | `Power_Create_Save_Shows_Popup_And_Persists_Nothing` | Admin+New: `SaveCommand` → `IsServerRegisterConfirmVisible=true`, `RegisterToServer=true`(D4 기본 on), `repo.Saved`·`local.SavedFrame` 모두 null |
| N2 | `Power_Confirm_Without_Checkbox_Saves_Local_Public_Only` | `RegisterToServer=false` **명시 대입** 후 확인(기본값에 의존하지 않는다 — D4를 뒤집으면 곧바로 깨지는 지점이었다) → `repo.Saved` null, `local.SavedOwner` null(공용), `local.SavedFrame.Id == string.Empty`(#dbid 미기록), 팝업 닫힘 |
| N3 | `Power_Confirm_With_Checkbox_Registers_To_Server` | `RegisterToServer=true` 확인 → `repo.Saved` non-null(`IsDefault=true`, `UserId=null`), `local.SavedOwner` null, `Assert.Same(repo.Saved, local.SavedFrame)` |
| N4 | `Cancel_Server_Register_Persists_Nothing_And_Keeps_Editor` | 체크를 기본값과 다른 값으로 바꾼 뒤 취소 → 팝업 닫힘, `RegisterToServer==true`(기본값 복귀), repo/local null, `FrameName`·`Slots.Count`·`FrameImage` 불변 |
| N5 | `Picked_Frame_Session_Stays_New_And_Prompts_Server_Register` | `ApplyPickedFrame`(Admin) → `SaveCommand` → 팝업 노출(= 세션이 `New`임을 행동으로 증명) |
| N6 | `ApplyPickedFrame_Preserves_User_Typed_Name` | `FrameName="내작품"` 후 `ApplyPickedFrame` → `FrameName == "내작품"`(사본 접미 없음) |
| N7 | `Power_Save_Blocked_When_Name_Collides_With_Public_Frame` | `local.PublicNames.Add("프레임A")`, `FrameName="프레임A"` → 저장·팝업 모두 없음, `StatusMessage`에 `"이미 같은 이름"` |
| N8 | `AdvancedUser_Save_Blocked_When_Name_Collides_With_Own_Frame` | `local.UserFrames["u1"]`에 `Name="내프레임"` → `FrameName="내프레임"` 저장 차단 |
| N9 | `EditOwnLocal_Same_Name_Is_Exempt_From_Collision_Guard` | `local.UserFrames["u1"]`에 `내프레임` 존재 + `LoadForEdit`(본인 로컬 `내프레임`) → 저장 성공(`local.SavedFrame.Name == "내프레임"`) |
| N10 | `Server_Register_Failure_Persists_Nothing_And_Reports` | `repo.ThrowOnSave=true`, 체크 on 확인 → `local.SavedFrame` null, `StatusMessage`에 `"서버 등록 실패"` |
| N11 | `Save_Blocked_When_Name_Has_Invalid_Chars` | `FrameName="a/b"` → 차단, 팝업·저장 없음, `StatusMessage`에 `"사용할 수 없는 문자"` |
| N12 | `Save_Blocked_When_Name_Is_Blank` | `FrameName="   "` → 차단, `StatusMessage`에 `"이름을 입력"` |
| N13 | `RegisterToServer_Resets_On_Reopen` | 취소 → **그 뒤에** `RegisterToServer=false` 대입(취소의 리셋을 무력화) → 다시 `SaveCommand` → `RegisterToServer == true`. 이 순서여야 단언을 통과시키는 유일한 원인이 `Save()`의 재오픈 리셋이 된다(리뷰 지적 반영) |
| N14 | `Fork_Session_Blocked_When_Name_Collides_With_Other_Public_Frame` | `PublicNames.Add("다른공용")` + `LoadForEdit`(DB 기본) 후 `FrameName="다른공용"` → 차단(D1의 F1 확장분) |

**`FrameNamingTests.cs`**: `IsFileNameSafe` — `null`/`""`/`"   "` → false, `"a/b"`·`"a:b"`·`"a?b"` → false, `"기본프레임"`·`"내_프레임"` → true.

**`XamlResourceTests.cs`**: `FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm`
- `FrameEditorView.xaml` 텍스트에서 `Regex.Matches(text, @"DataContext=")` **count == 1** 이고 그 1개가 `DataContext="{Binding Picker}"`임을 단언(§5.7 함정 회귀 방지 — 실패 메시지에 사유를 적는다).
- `IsServerRegisterConfirmVisible`, `ConfirmServerRegisterCommand`, `CancelServerRegisterCommand`, `RegisterToServer`, `PickedSourceNotice`, `HasPickedSource` 문자열이 모두 존재함을 단언(바인딩 소실 정적 검출).
- 기존 `Item1a_View_StaticResource_Keys_Resolve_In_Theme("FrameEditorView.xaml")`이 새 오버레이의 StaticResource 키까지 자동 커버한다(추가 등록 불필요).

---

## 8. 구현 단계 (WBS 블루프린트)

공통 전제(모든 단계):
- 저장소 `C:\STUDY\PROJECT\PhotoBooth`, 브랜치 `fix/windows-ui-tweak`(새 브랜치 만들지 않음), 기준 커밋 `f5225cc`.
- 편집하는 모든 파일은 **UTF-8 without BOM** — BOM 추가 금지.
- 빌드 경고 0 / 오류 0을 유지한다.
- **GUI 실행이 환경 정책으로 차단**되어 있다 → 실행 기반 시각 검증을 시도하지 않는다. 빌드 + 단위 테스트로만 검증하고, 시각 항목은 §9에 남긴다.
- 단계별 커밋을 권장한다(롤백 단위 = 단계).

### Step 1: `FrameNaming.IsFileNameSafe` 추출 + `LocalFrameStore` 위임
- **Context Brief**: 프레임 저장 시 이름에 파일시스템 금지문자가 있으면 `LocalFrameStore`가 `IOException`을 던진다. 그런데 파워 계정 신규 생성 경로는 서버 insert(`SaveAsync`)를 **먼저** 하고 로컬 저장을 나중에 하므로, 잘못된 이름이면 "서버에는 등록됐지만 로컬에는 없는" 반쪽 상태가 만들어진다. 저장 전에 미리 걸러내려면 판정이 순수 함수로 노출돼 있어야 한다. 현재 판정은 `LocalFrameStore.EnsureFileNameSafe`(private static)에만 있다.
- **대상 파일**: `src\MCPhoto.Core\Frames\FrameNaming.cs`, `src\MCPhoto.Core\Frames\LocalFrameStore.cs`, `tests\MCPhoto.Tests\FrameNamingTests.cs`
- **선행 조건**: 없음
- **구현 내용**:
  1. `FrameNaming.cs`에 `using System.IO;` 추가 후 `public static bool IsFileNameSafe(string? name)` 추가 — `!string.IsNullOrWhiteSpace(name) && name!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0`. XML 주석에 "LocalFrameStore.EnsureFileNameSafe와 동일 판정, 저장 전 선검증용"을 명시.
  2. `LocalFrameStore.EnsureFileNameSafe`의 두 번째 조건을 `!FrameNaming.IsFileNameSafe(value)`로 교체. **예외 메시지 2종(`"이름이 비어 있습니다."`, `"이름에 사용할 수 없는 문자가 있습니다: {value}"`)과 던지는 조건 순서는 그대로 유지**.
  3. `FrameNamingTests.cs`에 `IsFileNameSafe` 케이스 추가: `null`/`""`/`"   "` → false, `"a/b"`·`"a:b"`·`"a?b"` → false, `"기본프레임"`·`"내_프레임"` → true.
- **검증 명령**:
  `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`
  `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameNamingTests|FullyQualifiedName~LocalFrameStoreTests"`
- **완료 기준**:
  - [관측] 위 두 명령이 경고 0/오류 0, 실패 0으로 끝나고 `IsFileNameSafe` 신규 케이스가 실행된다.
  - [non-goal] `NextCopyName`/`StripCopySuffix`의 동작과 `LocalFrameStore`의 예외 메시지·저장 경로 규약은 바뀌지 않는다(기존 `LocalFrameStoreTests` 전부 통과).
  - [trigger] 새 함수는 이 단계에서 **호출되지 않는다**(호출은 Step 3) — 순수 추가이므로 앱 동작 변화 0.
- **롤백**: 이 단계 커밋 revert. 다른 단계와 독립(호출자 없음).
- [ ] 완료

### Step 2: F2 "기존 프레임 불러오기"를 신규 생성 세션으로 (R1)
- **Context Brief**: 프레임 편집기의 [기존 프레임 불러오기](생성 모드 전용 버튼)는 선택한 프레임의 이미지·슬롯을 현재 세션으로 복사한다(`ApplyPickedFrame`). 현재는 마지막에 세션을 `ForkFromCatalog`로 바꾸고 이름을 `"{원본} 사본"`으로 덮어써서 "기존 프레임 편집"과 사실상 같아진다. 사용자 요구는 "사본이 아니라, 그 프레임 정보를 기본값으로 한 **새 프레임 생성**이고 이름도 새로 정한다"이다.
- **대상 파일**: `src\MCPhoto.App\ViewModels\FrameEditorViewModel.cs`, `tests\MCPhoto.Tests\FrameEditorViewModelTests.cs`
- **선행 조건**: 없음 (Step 1과 병렬 가능)
- **구현 내용**:
  1. `[ObservableProperty] private string _pickedSourceNotice = string.Empty;`에 `[NotifyPropertyChangedFor(nameof(HasPickedSource))]`를 붙여 추가하고, `public bool HasPickedSource => !string.IsNullOrEmpty(PickedSourceNotice);` 추가.
  2. `ApplyPickedFrame` 말미(현재 `:404-409`)를 다음으로 교체:
     - `_sessionSource = FrameSessionSource.New;` (기존 `ForkFromCatalog` 대체)
     - `_sourceName = src.Name;` (유지 — 추적용)
     - `FrameName = FrameNaming.NextCopyName(...)` **줄 삭제**(이름을 건드리지 않는다)
     - `PickedSourceNotice = $"'{src.Name}'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요.";`
     - `OnPropertyChanged(nameof(SaveScopeNotice));` 유지
     - 메서드 XML 주석의 "세션 정체성은 항상 새 프레임(fork)" 표현을 "세션 정체성 = 신규 생성(New) — 이름은 사용자가 정한다"로 갱신.
  3. `LoadImage` 성공 경로의 `StatusMessage = string.Empty;` 옆에 `PickedSourceNotice = string.Empty;` 추가(이미지를 새로 불러오면 안내가 사실과 어긋나므로).
  4. 테스트: `ApplyPickedFrame_Copies_Slots_And_Suggests_Copy_Name` → `ApplyPickedFrame_Copies_Slots_And_Keeps_Editable_Name`으로 개명하고 이름 단언을 `"새 프레임"`으로 교체 + `HasPickedSource`/`PickedSourceNotice` 단언 추가. 신규 N6(`ApplyPickedFrame_Preserves_User_Typed_Name`) 추가.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameEditorViewModelTests"`
- **완료 기준**:
  - [관측] `ApplyPickedFrame` 후 `FrameName`이 호출 전 값과 동일하고(기본 `"새 프레임"` 또는 사용자 입력값), `PickedSourceNotice`에 원본 이름이 포함된다.
  - [non-goal] 슬롯 좌표 배율 보정·원본 파일 불변·jpeg 경유 로드·`IsCreateMode`/`EditorTitle` 유지는 그대로다(`ApplyPickedFrame_*` 나머지 4개 테스트 전부 통과). `LoadForEdit`(F1)의 사본 이름 제안은 **변경 없음**(`Fork_Name_Avoids_Existing_Names_In_Scope` 통과).
  - [trigger] 이름/안내가 바뀌는 시점은 [불러오기] 확인(`ConfirmPickFrame`) **한 곳**뿐이다 — 모달을 열기만 하거나 취소하면 아무 것도 바뀌지 않는다(`CancelPickFrame_Leaves_Editor_Untouched` 통과).
- **롤백**: 이 단계 커밋 revert. Step 1과 독립.
- [ ] 완료

### Step 3: 저장 전 검증 일원화 + 이름 충돌 가드 (D1)
- **Context Brief**: 프레임 이름은 로컬 파일명(`Frame\{이름}.png` 공용 / `Frame\{계정}_{이름}.png` 개인)이 되고 `LocalFrameStore.SaveLocal`은 같은 이름 파일을 **경고 없이 덮어쓴다**. 지금은 "fork 세션 + 파워 + 이름이 원본과 동일"인 한 가지만 막고 있어서, Step 2로 F2 세션이 신규 생성이 되면 불러온 프레임 이름을 그대로 두고 저장할 때 원본 공용 프레임이 파괴된다. 저장 전 검증을 한 곳으로 모으고 스코프 충돌을 차단한다.
- **대상 파일**: `src\MCPhoto.App\ViewModels\FrameEditorViewModel.cs`, `tests\MCPhoto.Tests\FrameEditorViewModelTests.cs`
- **선행 조건**: Step 1(`FrameNaming.IsFileNameSafe`), Step 2(F2 세션이 `New`)
- **구현 내용**:
  1. `private bool TryValidateForSave(out string error)` 추가 — 순서 고정: ① 로그인 → ② `CanWriteFrames()` → ③ 이미지/슬롯 유효성 → ④ `isFork && isPower && FrameName == _sourceName`(**기존 문구 그대로**: `"원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요."`) → ⑤ `string.IsNullOrWhiteSpace(FrameName)` → `"프레임 이름을 입력해 주세요."` → ⑥ `!FrameNaming.IsFileNameSafe(FrameName)` → `"이름에 사용할 수 없는 문자가 있습니다."` → ⑦ `_sessionSource != FrameSessionSource.EditOwnLocal && ExistingNamesForCurrentScope().Contains(FrameName, StringComparer.Ordinal)` → `"이미 같은 이름의 프레임이 있습니다. 다른 이름을 입력해 주세요."`
  2. `Save()`가 자체 검증 대신 `TryValidateForSave`를 호출하고 실패 시 `StatusMessage = error; return;` (이 단계에서는 성공 시 기존 저장 본문을 그대로 실행 — 팝업 도입은 Step 4).
  3. `ExistingNamesForCurrentScope()` XML 주석을 "사본 이름 계산 + 저장 전 충돌 검사"로 갱신. **본문은 변경하지 않는다.**
  4. ④를 ⑦보다 먼저 두는 이유를 코드 주석으로 남긴다(기존 테스트가 원본 가드 문구를 검증하며, 조회 실패 시 ⑦이 조용히 꺼져도 ④가 남는 2중 방어).
  5. 테스트 신규 N7·N8·N9·N11·N12·N14 추가.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameEditorViewModelTests"`
- **완료 기준**:
  - [관측] 스코프에 동명 프레임이 있으면 `repo.Saved`·`local.SavedFrame`가 모두 null이고 `StatusMessage`에 `"이미 같은 이름"`이 남는다. 금지문자·빈 이름도 각각 고유 문구로 차단된다.
  - [non-goal] `EditOwnLocal` 세션의 같은 이름 덮어쓰기는 계속 허용되고(N9), F1 fork의 자동 사본 이름 저장도 계속 성공한다(`Power_Editing_Db_Default_Saves_Local_Only_With_Fork_Name` 통과). 원본 이름 가드 문구도 그대로다(`Fork_Save_Blocked_When_Name_Equals_Source_In_Public_Scope` 통과).
  - [trigger] 검증은 [저장] 커맨드 실행 시에만 수행된다 — 이름을 타이핑하는 동안에는 차단·경고가 발생하지 않고 `CanSave`(슬롯 유효성 축)도 바뀌지 않는다.
- **롤백**: 이 단계 커밋 revert(Step 1·2는 유지 가능 — 호출만 사라진다).
- [ ] 완료

### Step 4: 서버 등록 확인 팝업 VM (R2) — 저장 파이프라인 분리
- **Context Brief**: 프레임 편집기에서 파워 계정(manager/admin)이 신규 프레임을 저장하면 지금은 **무조건** 서버(DB)에 공용 기본 프레임으로 insert된다. 사용자 요구는 "저장 시 서버에도 만들지 묻는 체크박스 팝업(프레임 삭제 확인 팝업과 같은 형태)을 띄우고, 체크된 경우에만 DB insert, 아니면 로컬만"이다. 참고 패턴은 `FrameSelectViewModel`의 `RequestDelete`/`ConfirmDelete`/`CancelDelete` + `DeleteAlsoServer`(기본 off, 열 때마다 리셋, 닫히기 전에 값 확정)다.
- **대상 파일**: `src\MCPhoto.App\ViewModels\FrameEditorViewModel.cs`, `tests\MCPhoto.Tests\FrameEditorViewModelTests.cs`
- **선행 조건**: Step 3(`TryValidateForSave`)
- **구현 내용**:
  1. 상태 추가: `[ObservableProperty] private bool _isServerRegisterConfirmVisible;`, `[ObservableProperty] private bool _registerToServer;`, `private bool RequiresServerRegisterPrompt => _shell.Session.CurrentUser?.Role.IsPower() == true && _sessionSource == FrameSessionSource.New;`
     - 권한 축은 **`IsPower()`만** 사용한다(`CanWriteFrames()`로 대체 금지 — `UserRole.cs:49-64` 경고).
  2. 기존 `Save()`의 저장 본문을 `private async Task PersistAsync(bool registerToServer)`로 이동. 분기:
     - `isPower && isNew && registerToServer` → `SaveAsync` → `SaveLocal(saved, _imageBytes, ownerName: null)`
     - `isPower` (그 외) → `SaveLocal(Id=""인 공용 프레임, ownerName: null)` — 기존 `isPower && !isNew` 분기를 그대로 사용
     - 비power → 기존 개인 로컬 분기 그대로
     - 진입점이 2개이므로 `PersistAsync` 첫 줄에서 `TryValidateForSave`를 **재실행**(fail-closed)한다.
  3. `SaveAsync` 호출만 별도 try/catch로 감싸 실패 시 **로컬 저장·화면 전환 없이** 반환하고
     `StatusMessage = $"서버 등록 실패: {ex.Message} 이 PC에만 저장하려면 '서버에도 등록'을 해제하고 다시 저장해 주세요.";` + `_logger?.LogError(...)` (D6 원자성).
  4. `Save()`는 검증 통과 후: `RequiresServerRegisterPrompt`면 `RegisterToServer = DefaultRegisterToServer; IsServerRegisterConfirmVisible = true; return;`(저장하지 않음), 아니면 `await PersistAsync(false)`.
  5. `[RelayCommand] private async Task ConfirmServerRegister()`: `var alsoServer = RegisterToServer;` **먼저 확정** → `IsServerRegisterConfirmVisible = false; RegisterToServer = DefaultRegisterToServer;` → `await PersistAsync(alsoServer)`.
  6. `[RelayCommand] private void CancelServerRegister()`: `IsServerRegisterConfirmVisible = false; RegisterToServer = DefaultRegisterToServer;` (저장·전환·디스크 무변경).
  7. 픽스처: `CapturingFrameRepository`에 `public bool ThrowOnSave { get; set; }` 추가 + `SaveAsync`에서 throw.
  8. 테스트: 신규 N1·N2·N3·N4·N5·N10·N13 추가, 기존 `Power_Save_Persists_To_Db_And_Local_Cache`와 `SaveScopeNotice_Warns_Before_Save_When_Public_Name_Has_Underscore`를 §7.1 표대로 팝업 흐름으로 갱신.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug` 및 `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameEditorViewModelTests"`
- **완료 기준**:
  - [관측] Admin+신규 생성에서 `SaveCommand` 실행 후 `IsServerRegisterConfirmVisible=true`이고 저장은 일어나지 않는다. `RegisterToServer=false`로 확인하면 로컬 공용만(`repo.Saved==null`, `SavedOwner==null`, `Id==""`), `true`로 확인하면 DB+로컬 둘 다 기록된다.
  - [non-goal] AdvancedUser 저장과 F1 편집 저장(fork·덮어쓰기)에서는 팝업이 뜨지 않고 즉시 저장된다. `SaveCommand`는 계속 `AsyncRelayCommand`이며 XAML 바인딩 이름(`SaveCommand`)이 유지된다. 서버 등록 실패 시 로컬 파일도 만들어지지 않는다.
  - [trigger] DB insert는 "팝업의 체크박스가 켜진 상태에서 [저장] 클릭" 시에만 발생한다 — 팝업 [취소]나 체크 해제 상태 저장에서는 `SaveAsync`가 호출되지 않고, 체크 상태는 팝업을 다시 열 때마다 off로 초기화된다.
- **롤백**: 이 단계 커밋 revert(Step 1~3 유지 — 저장 흐름이 팝업 없는 기존 형태로 복귀).
- [ ] 완료

### Step 5: `SaveScopeNotice` 문구 정합화 (D5)
- **Context Brief**: 편집기 저장 버튼 위 캡션(`SaveScopeNotice`)은 "이번 저장의 실제 결과"를 알린다(상단 경고 배너는 "정책"을 알리며 역할이 분리되어 있다 — it15 설계). 파워 신규 생성 문구가 `"공용 기본 프레임으로 서버에 등록됩니다"`라고 단정하는데, Step 4 이후 서버 등록은 팝업 체크박스에 달려 있어 사실과 어긋난다.
- **대상 파일**: `src\MCPhoto.App\ViewModels\FrameEditorViewModel.cs`, `tests\MCPhoto.Tests\FrameEditorViewModelTests.cs`
- **선행 조건**: Step 4
- **구현 내용**:
  1. `SaveScopeNotice`의 `FrameSessionSource.New` 분기 문구를
     `$"저장 시 '{FrameName}'을(를) 이 PC의 공용 목록에 만듭니다. 서버 등록 여부는 저장할 때 선택합니다."`로 교체.
  2. `ForkFromCatalog`·`EditOwnLocal`·비power 분기와 `'_'` 경고 접미 로직은 **변경하지 않는다**.
  3. 상단 배너(XAML)와 이 캡션의 역할 분리를 설명하는 기존 XML 주석을 유지하되, "power 신규 생성은 DB 등록 경로다"라는 주석을 "서버 등록은 저장 시 확인 팝업에서 선택한다"로 갱신.
  4. 테스트: `SaveScopeNotice_Reflects_Scope`, `IsCreateMode_Gates_LocalOnly_Banner`의 `"서버에 등록"` 단언을 `"서버 등록 여부는"`으로 교체(advNew의 부정 단언은 `"서버 등록"`으로).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameEditorViewModelTests|FullyQualifiedName~XamlResourceTests"`
- **완료 기준**:
  - [관측] Admin+신규 세션의 `SaveScopeNotice`가 "이 PC의 공용 목록에 만듭니다 / 서버 등록 여부는 저장할 때 선택합니다"를 담고, `"서버에 등록됩니다"` 단정이 코드베이스에서 사라진다(`grep`으로 확인 가능).
  - [non-goal] fork·덮어쓰기·개인 저장 문구와 `'_'` 경고는 글자 그대로 유지된다. 상단 배너의 `IsCreateMode` 게이트 XAML은 손대지 않는다(`FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode` 통과).
  - [trigger] 캡션은 `FrameName` 변경·세션 판정 변경 시에만 갱신된다(기존 `[NotifyPropertyChangedFor]`/`OnPropertyChanged` 경로 그대로) — 새 알림 배선을 추가하지 않는다.
- **롤백**: 이 단계 커밋 revert(문구만 되돌아간다 — 기능 영향 없음).
- [ ] 완료

### Step 6: XAML — 불러온 원본 캡션 + 서버 등록 확인 오버레이
- **Context Brief**: 프레임 편집기 View는 `UserControl`이고 `App.xaml`의 `DataTemplate`으로 VM에 연결된다(ViewModel-first). 이 화면에는 이미 프레임 피커 오버레이가 있고, **오버레이 루트에 `DataContext`를 걸면 확인/취소 커맨드(편집기 VM 소유)가 조용히 바인딩 실패해 버튼만 비활성된다**는 경고 주석이 달려 있다. 이번에는 서버 등록 확인 오버레이(삭제 확인 팝업과 같은 형태)와 "불러온 원본" 캡션을 추가한다.
- **대상 파일**: `src\MCPhoto.App\Views\FrameEditorView.xaml`, `tests\MCPhoto.Tests\XamlResourceTests.cs`
- **선행 조건**: Step 2(`PickedSourceNotice`/`HasPickedSource`), Step 4(팝업 상태·커맨드)
- **구현 내용**:
  1. 설계 §5.8의 캡션 `TextBlock`을 "프레임 이름" 레이블 **바로 위**에 삽입(음수 Margin 금지).
  2. 설계 §5.8의 오버레이 `Grid`를 기존 피커 오버레이 `</Grid>` **다음**(최상위 `Grid`의 마지막 자식)에 삽입. `Grid.RowSpan="2" Grid.ColumnSpan="2"` 유지.
  3. **새 오버레이의 어떤 요소에도 `DataContext`를 설정하지 않는다.** 피커와 동일한 경고 주석을 새 오버레이 위에도 복제한다.
  4. 신규 StaticResource 키를 만들지 않는다(§5.8 목록은 모두 기존 키).
  5. `XamlResourceTests`에 `FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm` 추가: `DataContext=` 출현 수 == 1 && 그것이 `DataContext="{Binding Picker}"`, 그리고 `IsServerRegisterConfirmVisible`/`RegisterToServer`/`ConfirmServerRegisterCommand`/`CancelServerRegisterCommand`/`PickedSourceNotice`/`HasPickedSource` 문자열 존재 단언. 실패 메시지에 함정 사유를 적는다.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug` 및 `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResourceTests"`
- **완료 기준**:
  - [관측] XAML 빌드(경고 0) + `Item1a_View_StaticResource_Keys_Resolve_In_Theme("FrameEditorView.xaml")` 통과 + 신규 정적 테스트 통과(= 참조 리소스 전부 해석, DataContext 함정 없음, 6개 바인딩 문자열 존재).
  - [non-goal] 배너·편집 캔버스·컨트롤 패널·`ScrollViewer`·하단 고정 저장/취소 바(HEAD `f5225cc`의 레이아웃 수정)는 구조가 바뀌지 않는다. 코드비하인드(`FrameEditorView.xaml.cs`)는 **한 줄도 수정하지 않는다**(MVVM 순수성).
  - [trigger] 오버레이는 `IsServerRegisterConfirmVisible=true`일 때만 표시되고(그 값은 파워+신규 저장 클릭에서만 true), 캡션은 `HasPickedSource=true`(= F2 불러오기 성공)일 때만 표시된다.
- **롤백**: 이 단계 커밋 revert(VM은 남지만 UI 노출만 사라진다 — 파워 신규 저장이 팝업 대기 상태로 멈추므로 Step 4와 함께 revert할 것).
- [ ] 완료

### Step 7: 전체 회귀 검증 + 잔여 시각 확인 항목 정리
- **Context Brief**: 이 변경은 프레임 저장 경로(로컬 파일 + 서버 DB)를 건드리므로, 프레임 관련 전 테스트와 XAML 정적 안전망이 함께 통과해야 한다. 또한 GUI 실행이 환경 정책으로 차단되어 있어 시각 검증은 사용자에게 넘겨야 한다.
- **대상 파일**: (코드 변경 없음 — 필요 시 앞 단계 보정)
- **선행 조건**: Step 1~6
- **구현 내용**:
  1. 전체 빌드 + 전체 테스트 실행.
  2. `git diff --stat`으로 변경 파일이 §6 표의 7개를 넘지 않는지 확인.
  3. `grep -rn "서버에 등록됩니다" src/` 결과 0건 확인(D5 잔여 문구 없음).
  4. `grep -c "DataContext=" src/MCPhoto.App/Views/FrameEditorView.xaml` 결과 1 확인.
  5. §9 "사용자 확인 필요" 목록을 최종 보고에 그대로 전달.
- **검증 명령**:
  `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`
  `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`
- **완료 기준**:
  - [관측] 빌드 경고 0/오류 0, 전체 테스트 실패 0. 변경 파일이 7개 이내이고 위 grep 2건이 기대값(0건 / 1건)이다.
  - [non-goal] `web/`·서버 계약·다른 화면(`FrameSelectView`, `SettingsView` 등) 테스트가 새로 실패하지 않는다. 새 NuGet 패키지·프로젝트 참조가 추가되지 않는다.
  - [trigger] 시각 확인이 필요한 항목(오버레이 배치·체크박스 렌더·캡션 줄바꿈)은 실행 검증을 시도하지 않고 §9 목록으로 사용자에게 넘긴다.
- **롤백**: 실패 항목이 속한 단계만 개별 revert.
- [ ] 완료

---

## 9. 사용자 확인 필요 (실행 기반 시각 검증 — 이 환경에서 불가)

GUI exe 직접 실행이 정책 훅으로 차단되어 아래 항목은 빌드·단위 테스트로 증명할 수 없다. 사용자가 실기에서 확인해야 한다.

1. **팝업 시각 정합** — 서버 등록 확인 오버레이가 삭제 확인 팝업과 같은 톤(카드·그림자·스크림)으로 보이는지, `MinWidth="420"`에서 캡션 2줄이 자연스럽게 접히는지.
2. **체크박스 렌더** — 명시 스타일 없는 `CheckBox`가 테마 암시 스타일로 판독 가능하게 렌더되는지(삭제 팝업과 동일 조건, A7).
3. **오버레이 z-order/히트테스트** — 팝업이 열린 동안 뒤쪽 이름 `TextBox`·저장 버튼이 클릭되지 않는지, 피커 오버레이와 겹치는 경우가 없는지(A5).
4. **"불러온 원본" 캡션 위치** — 스크롤 패널 안에서 "프레임 이름" 레이블 위에 자연스럽게 붙는지, 숨김 시 간격이 남지 않는지.
5. **실서버 등록 왕복** — 체크 on 저장이 실제 Firestore/Storage에 공용 기본 프레임을 만들고, 다른 PC에서 내려받히는지(`#dbid` 기록 확인 포함).
6. **차단 문구 가독성** — 이름 충돌·금지문자 차단 시 `StatusMessage`(danger 색, 스크롤 패널 하단)가 실제로 눈에 들어오는지. 잘 안 보인다면 후속 이터레이션에서 팝업/인라인 경고로 승격 검토.
7. ~~**파워 워크플로 영향** — 기본 off(D4)가 실제 운영에 맞는지.~~ **해결됨**: 사용자가 기본 **on**을 선택해 `DefaultRegisterToServer = true`로 반영했다(D4 참조). 남은 확인 사항은 "기본 on 상태에서 로컬만 저장하려는 경우 체크 해제가 충분히 눈에 띄는지"다 — 반대 방향의 실수(의도치 않은 서버 배포)가 이제 더 비싸므로 팝업 캡션 가독성을 함께 봐야 한다.

> 구현 메모: `ExistingNamesForCurrentScope().Contains(FrameName, StringComparer.Ordinal)`은 `System.Linq`의
> `Enumerable.Contains(IEnumerable<T>, T, IEqualityComparer<T>)` 오버로드다. 이 프로젝트는 ImplicitUsings가
> 켜져 있어(같은 파일이 이미 `.Select`/`.ToList` 사용) 추가 `using`이 필요 없다.

---

## 10. 품질 자체 점검

- [x] 모든 View에 대응 ViewModel과 연결 방식이 명확 — 신규 View 없음, 기존 `DataTemplate` 매핑 유지(§5.6)
- [x] 바인딩·명령에 누락된 뷰모델 멤버 없음 — §5.4 표와 §5.6 표가 1:1 대응하며 §7.3의 정적 테스트가 문자열 수준에서 고정
- [x] 이벤트 구독마다 해제 경로 명시 — **신규 이벤트 구독 0개**(팝업은 VM 상태 bool). 기존 `_pickerCts` 생명주기 무변경 → 누수 위험 증가분 없음
- [x] UI 스레드/백그라운드 경계와 동기화 전략 명확 — §5.9. 신규 `Task.Run`·`Dispatcher` 호출 0, `.Result`/`.Wait()` 0
- [x] 리소스 키 체계에 충돌 없음 — **신규 리소스 키 0개**(§5.8 전부 기존 키), `XamlResourceTests`가 해석 여부를 자동 검증
- [x] DPI/테마 대응 — 하드코딩 색상·픽셀 폰트 없음, 전부 테마 토큰(`Brush.*`, `Text.*`, `Button.*`)과 상대 레이아웃
- [x] 전역 예외 처리·오류 표시 경로 반영 — 저장 예외는 기존 `InvalidOperationException`/`IOException`/`Exception` 3중 catch 유지 + 서버 등록 실패 전용 안내 추가(D6)
- [x] ViewModel이 UI 없이 테스트 가능 — 신규 멤버 전부 `bool`/`string`/`ICommand`, `System.Windows` 타입 유입 0, 신규 테스트 14개가 창 없이 실행
- [x] MVVM 순수성 — 코드비하인드 변경 0, 다이얼로그를 VM에서 직접 열지 않음(오버레이 + 상태 프로퍼티)
- [x] F1(`LoadForEdit`) 경로 불변 — §5.10 매트릭스로 항목별 보장
- [x] 인코딩 보존 명시 — §6(UTF-8 without BOM)
- [x] `wpf-developer`가 추가 질문 없이 구현 가능 — 문구·조건·순서·XAML 골격·테스트 기대값을 전부 확정값으로 기재

### 완결성 게이트 (WBS_BLUEPRINT §완결성 게이트)

- [x] 검증된 사실(§2) / 미검증 가정(§3) 목록이 분리되어 있다
- [x] 모든 가정(A1~A7)에 검증 단계가 매핑되어 있다 (A1→Step 1, A2→Step 2, A3→Step 3, A4→Step 4, A5→Step 6+§9, A6→Step 6, A7→Step 6+§9)
- [x] 7개 단계 전부에 7개 필수 필드가 채워져 있다
- [x] 모든 완료 기준이 관측/non-goal/trigger 3문 형식이며 UI 단계(Step 2·4·6)는 non-goal·trigger를 포함한다
- [x] 검증 명령이 자동 실행 가능한 CLI다 (`dotnet build` / `dotnet test --filter`)

### 구현 우선순위 (developer 진행 순서)

`Step 1` ∥ `Step 2` → `Step 3` → `Step 4` → `Step 5` → `Step 6` → `Step 7`
(Step 1과 Step 2는 상호 독립 — 병렬 가능. Step 3부터는 순차. Step 4·6은 함께 revert해야 하는 쌍이다.)
