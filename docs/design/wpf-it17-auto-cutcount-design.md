# it17 설계 — 촬영 컷 수 "자동" 모드 (슬롯 수 + 2, 최소 6)

> 프로젝트 루트: `C:\STUDY\PROJECT\PhotoBooth`
> 선행 커밋: `1da5f71` (docs: 멀티플랫폼 클라이언트 문서 계층 신설) — 테스트 베이스라인 **758 통과 / 0 실패**
> 입력: 사용자 피드백(아래 §0.1 원문), 현행 코드(§1 file:line 전수 확인)
> 산출물 소비자: `wpf-developer` — §11 WBS를 Step 순서대로 실행한다. `wpf-code-reviewer`가 §10 테스트 계획으로 검증한다.
> 서버(web/functions) 변경 **없음**. 클라 단독 이터레이션.

---

## §0 개요

### 0.1 요구사항 원문 (축약 금지)

> 실제 촬영 수가 6회로 설정했을 때, 프레임에 슬롯 수가 6개인게 아쉽다는 피드백이 있어. 그래서 촬영 횟수를 콤보박스 UI 상 "자동"이라는 항목을 만들고, 이 항목은 내부적으로 ini에 0이나 -1과 같은 수로 지정하면 될 것 같아.
> 자동일 경우 조건은 최소 촬영 수 6회고, 슬롯 수가 많은 경우 슬롯 수 +2 만큼 촬영 가능하도록 지정.

### 0.2 문제와 취지

현재 PRD는 "컷 수 최소 6 · 슬롯 최대 6"으로 단순화해 `촬영 컷 수 ≥ 슬롯 수`가 **항상 성립**하도록 만들었다
(`docs/prd/photobooth-prd.md:54`, `:289`). 그 부작용으로 **슬롯 6개 프레임 + 컷 수 6 설정**이면
`max(6, 6) = 6` → 6장을 찍어 6칸을 전부 채운다. 컷 선택 화면(`CutSelectView`)에서 사용자가 고를 여지가 **0**이 되어
"여유 촬영 후 선택"(PRD 옵션 b)이라는 기획 의도가 무력화된다.

"자동"은 이 여유분을 **슬롯 수에 연동**해 항상 확보한다: `실제 컷 수 = max(6, 슬롯 수 + 2)`.
슬롯 6개면 8장을 찍어 6칸을 고르므로 버릴 2장이 생긴다 — 선택의 재미가 복원된다.

### 0.3 5대 쟁점 판정 요약

| # | 쟁점 | 판정 | 근거 절 |
|---|------|------|---------|
| 1 | 슬롯 확정 시점 vs 컷 수 확정 시점 | **프레임(=슬롯 수)이 컷 수 확정보다 먼저 결정된다.** `FrameSelectViewModel.Next()`가 프레임을 고정한 직후 `CaptureSession.Begin(frame, settings.CutCount)`를 호출하고 그 다음 Guide로 전이한다. 따라서 "선택 가능한 프레임 집합의 최대 슬롯 수" 같은 우회는 **불필요**. 자동 해석은 `Begin` 내부(슬롯 수를 이미 손에 든 지점)에서 수행한다 | §3 |
| 2 | sentinel `0` vs `-1` | **`0` 채택**(`CutCountPolicy.AutoCutCount = 0`). `Clamp()`의 `ClosestFrom` 앞에 `IsAuto` 가드를 추가해 0이 6으로 덮어써지는 것을 막는다. `-1`은 sentinel이 **아니므로** 기존 규칙대로 6으로 보정된다(오타 방어) | §4 |
| 3 | `max(6, 슬롯+2)`가 만드는 7 | **파이프라인이 임의 정수 컷 수를 견딘다**(촬영 루프 `for cut=1..TotalCuts`, 컷 선택 `WrapPanel`, 합성은 선택분=슬롯 수만 사용). 따라서 `AllowedCutCounts`를 **확장하지 않는다** — 7은 자동에서만 파생되는 *실효값*이고 설정 옵션이 아니다. `AppSettings.CutCount`의 도메인은 `{0} ∪ {6,8,10}`으로 유지 | §5 |
| 4 | UI 표기 | 콤보 **최상단**에 "자동", 아래에 "6컷 / 8컷 / 10컷". `DisplayModeOption` 패턴(`DisplayMemberPath`/`SelectedValuePath`) 재사용. **"자동 (8회)" 표기는 채택하지 않음** — 설정 화면 시점에 프레임이 미선택이라 숫자를 알 수 없어 거짓 표기가 된다. 대신 ① 설정에 규칙 캡션, ② Guide 화면에 확정된 실제 컷 수 + "(자동)" 배지 | §6 |
| 5 | 소비 지점 영향 | `AppSettings.CutCount`의 앱 내 소비자는 **설정 화면과 `FrameSelectViewModel.Next()` 단 2곳**. 나머지(Guide/Capture)는 모두 이미 해석 완료된 `CaptureSession.CutCount`를 읽는다. 재촬영(`BeginFullRetake`)은 `CutCount`·`Frame`을 보존하므로 해석값이 유지된다 | §7 |

### 0.4 설계의 핵 — 단일 해석 지점(choke point)

```
AppSettings.CutCount        ← "의도"    : 0(자동) | 6 | 8 | 10   (ini 왕복 대상)
        │
        │  FrameSelectViewModel.Next() — 호출 코드 변경 없음
        ▼
CaptureSession.Begin(frame, cutCount)
        │  CutCountPolicy.Resolve(cutCount, frame.Slots.Count)   ← 유일한 해석 지점
        ▼
CaptureSession.CutCount     ← "실효값" : 6 | 7 | 8 | 10 | …      (Guide·Capture가 읽음)
```

`Begin`을 해석 지점으로 삼은 이유: **우회 경로가 원리적으로 생기지 않는다.** 촬영 세션은 `Begin` 없이 시작할 수 없고,
`Begin`은 프레임을 인자로 받으므로 슬롯 수가 항상 확정 상태다. 호출측(`FrameSelectViewModel.Next()`)은
지금처럼 `settings.CutCount`를 그대로 넘기면 되며 **한 줄도 바뀌지 않는다** — 미래에 새 호출 지점이 추가돼도
자동 해석을 빠뜨릴 수 없다(안전 규칙: "sentinel이 실효값 경로로 새지 않는다").

---

## §1 검증된 사실 (verified facts — 전부 코드 직접 확인)

| VF | 사실 | 근거 |
|----|------|------|
| VF-1 | `AppSettings.CutCount` 기본 6, `AllowedCutCounts = {6,8,10}` | `src/MCPhoto.Core/Settings/AppSettings.cs:36`, `:46` |
| VF-2 | `Clamp()`는 `CutCount`가 허용 집합에 없으면 `ClosestFrom(CutCount, AllowedCutCounts, 6)`로 **최근접 보정**. 동률이면 첫 값 유지(`d < bestDist` 엄격 비교) | `AppSettings.cs:159-160`, `:214-225` |
| VF-3 | `MinSlots=1` / `MaxSlots=6` 상수는 **선언만 되어 있고 코드에서 참조되지 않는다**(전 솔루션 grep 결과 정의 2줄뿐). 슬롯 개수 상한은 `SlotLayout`이 하드코딩 `Math.Clamp(slotCount,1,6)`·`IsValid`의 `< 1 or > 6`으로 강제 | `AppSettings.cs:41-42`, `SlotLayout.cs:25`, `:167` |
| VF-4 | `CaptureSession.Begin(frame, cutCount)`가 `CutCount = Math.Max(cutCount, frame.Slots.Count)`로 "컷 수 ≥ 슬롯 수" 불변을 강제 | `src/MCPhoto.Core/Capture/CaptureSession.cs:36-42` |
| VF-5 | 프레임 선택이 촬영보다 **먼저**다: `Next()`가 `Session.SelectedFrame` 고정 → `Capture.Begin(SelectedFrame, Settings.Current.CutCount)` → `NavigateAsync(AppState.Guide)` | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:205-212` |
| VF-6 | `AppSettings.CutCount`(설정 원값)를 읽는 곳은 **`SettingsViewModel` 2곳 + `FrameSelectViewModel.Next()` 1곳**뿐. Guide·Capture는 `Session.Capture.CutCount`(해석값)를 읽는다 | grep 전수: `SettingsViewModel.cs:195`·`:291`, `FrameSelectViewModel.cs:210` / `GuideViewModel.cs:23`, `CaptureViewModel.cs:62` |
| VF-7 | 촬영 루프는 `for (int cut = 1; cut <= TotalCuts; cut++)` — 임의 정수 N을 견딘다. `AddCut`은 `CutCount` 초과분 무시 | `CaptureViewModel.cs:139-162`, `CaptureSession.cs:45-49` |
| VF-8 | 컷 선택 화면의 썸네일 컨테이너는 `WrapPanel` — 컷 개수에 하드코딩된 열 수가 없다 | `src/MCPhoto.App/Views/CutSelectView.xaml:28-30` |
| VF-9 | 합성 입력은 `GetSelectedCuts()` = **선택된 슬롯 수만큼**. 촬영 컷 수와 무관 | `CaptureSession.cs:68-70`, `IsSelectionComplete`(`:34`) |
| VF-10 | 전체 재촬영(`BeginFullRetake`)은 `_cuts`·`_selection`만 비우고 **`CutCount`·`Frame`은 보존**. `Discard()`만 `CutCount = 0`으로 리셋 | `CaptureSession.cs:91-96`, `:99-106` |
| VF-11 | `IniFile.GetInt`는 파싱 실패 시 **호출측이 넘긴 fallback**(= 현재 메모리 값 6)을 돌려준다. 즉 손상 ini는 6이 되고 0이 되지 않는다. `NumberStyles.Integer`는 `AllowLeadingSign`을 포함하므로 `-1`도 정상 파싱된다 | `src/MCPhoto.Core/Settings/IniFile.cs:72-73`, `IniSettingsService.cs:138` |
| VF-12 | 콤보박스 "값+한글 라벨" 관례가 이미 존재: `DisplayModeOption(Value, Label)` record + `ToString()` override, XAML은 `DisplayMemberPath="Label" SelectedValuePath="Value" SelectedValue="{Binding …}"` | `SettingsViewModel.cs:106-110`, `:371-374`, `Views/SettingsView.xaml:222-224` |
| VF-13 | 순수 정책 클래스 관례가 이미 존재: `QrDeliveryPolicy`, `QrEffectivePolicy`, `DisplayApplyPolicy`(각각 전용 테스트 파일 보유). `AppSettings`가 정책 클래스를 호출하는 방향(`NormalizeQr` → `QrDeliveryPolicy.Normalize`) | `src/MCPhoto.Core/Settings/` 파일 목록, `AppSettings.cs:205-212`, `tests/MCPhoto.Tests/QrDeliveryPolicyTests.cs` |
| VF-14 | `CutCount`는 **게스트 편집 게이트 비대상**(게스트도 편집·저장 가능). `SaveSettings`에서 `if (!IsGuest)` 없이 무조건 기록 | `SettingsViewModel.cs:291`, `docs/design/wpf-it12-design.md:137` |
| VF-15 | 레이아웃 점프 방지 관례: 조건부 안내문은 `<Grid Height="22" Margin="0,2,0,6">`로 **높이를 상시 예약**하고 내부 `TextBlock`에 `Visibility` 바인딩 | `Views/SettingsView.xaml:207-214` |
| VF-16 | `BoolToVis` 컨버터·`Text.Caption` 스타일은 App 전역 리소스 → 모든 View에서 사용 가능(`GuideView.xaml:39`가 `Text.Caption` 사용 중) | `src/MCPhoto.App/App.xaml:21`, `Themes/Typography.xaml:63` |
| VF-17 | 대상 소스 7개 파일 전부 **UTF-8 no BOM**(`head -c3` = `namespace`/`using`/`<Use`) | `AppSettings.cs`, `CaptureSession.cs`, `SettingsViewModel.cs`, `SettingsView.xaml`, `GuideView.xaml`, `GuideViewModel.cs`, `SettingsTests.cs` |
| VF-18 | 테스트 베이스라인 **758 통과 / 0 실패** (`dotnet test MCPhoto.sln -c Debug --nologo`, 실측) | 명령 출력 |
| VF-19 | 기존 회귀 테스트 `CutCount_Snapped_To_Allowed`가 **7 → 6** 보정을 고정한다 | `tests/MCPhoto.Tests/SettingsTests.cs:225-231` |

---

## §2 미검증 가정 (open assumptions) — 검증 단계 매핑

| A | 가정 | 검증 단계 |
|---|------|-----------|
| A-1 | 슬롯 7개 이상인 프레임은 실제로 존재하지 않는다(에디터가 1~6으로 클램프, VF-3). 다만 **DB/로컬 파일에서 온 프레임의 슬롯 수는 로드 시 재검증되지 않는다** → 이론상 7개 이상이 들어올 수 있다 | Step 1의 `Resolve(0, 8) == 10` 테스트로 **동작이 정의됨**을 고정(크래시·0컷 없음). 상한 클램프는 도입하지 않는다(§12 R-3) |
| A-2 | 콤보에 이종 타입(`CutCountOption` record) 항목을 넣고 `SelectedValue`로 int를 바인딩하면 현재 스타일(`Width=140`)에서 라벨이 잘리지 않는다 | Step 5의 `XamlResourceTests` 통과 + Step 8 수동 실행 관측 |
| A-3 | `자동` 캡션 1줄(약 24자)이 좌열 폭에서 22px 한 줄에 들어간다 | Step 5에서 `TextTrimming="CharacterEllipsis"`를 붙여 넘칠 경우에도 레이아웃이 깨지지 않게 보장 + Step 8 관측 |
| A-4 | 기존 ini(`CutCount=6|8|10`)를 쓰는 운영 PC에서 이번 변경 후 동작이 종전과 100% 동일하다 | Step 2의 하위 호환 테스트 3종(6/8/10 왕복 · 7→6 보정 유지 · `-1`→6) |

---

## §3 쟁점 1 판정 — 슬롯 확정 시점 vs 컷 수 확정 시점

### 3.1 실제 플로우 (코드 기준)

```
Home ──▶ FrameSelect ──▶ Guide ──▶ Capture ──▶ CutSelect ──▶ Result
             │
             └─ Next() : FrameSelectViewModel.cs:205-212
                  ① Session.SelectedFrame = SelectedFrame        ← 슬롯 수 확정
                  ② Session.Capture.Begin(SelectedFrame, Settings.Current.CutCount)  ← 컷 수 확정
                  ③ NavigateAsync(AppState.Guide)
```

**판정: 슬롯 수 확정(①)이 컷 수 확정(②)보다 먼저다.** 두 사건이 같은 메서드 안에서 연속으로 일어나며,
②는 프레임 객체 자체를 인자로 받는다. 따라서 자동 규칙의 입력(`slotCount`)은 ② 시점에 **결손 없이 확정**되어 있다.

브리프가 대비책으로 제시한 "선택 가능한 프레임 집합의 최대 슬롯 수 사용"은 **불필요하며, 채택하지 않는다.**
그 방식은 실제 선택 프레임과 무관한 컷 수를 낳아(슬롯 2개 프레임에 8컷) 요구사항의 "+2 여유"를 왜곡한다.

### 3.2 해석 지점을 `Begin` 내부로 두는 결정

세 가지 후보를 검토했다.

| 후보 | 문제 |
|------|------|
| (a) `FrameSelectViewModel.Next()`에서 해석해 `Begin`에 실효값 전달 | 해석이 App 계층(UI)로 새어 Core 단위 테스트가 불가. 새 호출 지점이 추가되면 해석을 빠뜨릴 수 있다 |
| (b) `AppSettings`에 `EffectiveCutCount(int slotCount)` 메서드 추가 | 설정 객체가 프레임을 알아야 해 책임이 섞인다. `Clamp()`가 설정 원값을 다루는 것과 혼동 |
| **(c) `CaptureSession.Begin` 내부 + Core의 순수 정책 클래스** ✅ | 없음. `Begin`은 세션 시작의 유일한 관문이고 프레임을 이미 받는다. 정책은 `CutCountPolicy`(순수 static)로 분리해 UI 없이 테스트 가능 |

(c)를 채택한다. VF-13의 `QrDeliveryPolicy`/`DisplayApplyPolicy` 관례와 동형이다.

### 3.3 왜 `IsAutoCutCount`를 세션에 저장하는가

Guide 화면의 "(자동)" 배지는 `Settings.Current.CutCount == 0`을 다시 읽어서도 만들 수 있다. 그러나
**설정은 세션 도중 변경될 수 있다**(설정은 오버레이로 진입 가능). 세션이 어떤 의도로 시작됐는지는
세션이 기억해야 한다 — `CaptureSession.CutCount`가 이미 그 이유로 존재한다(VF-4). 따라서
`CaptureSession.IsAutoCutCount`를 `Begin`에서 함께 확정하고, Guide는 세션에서 읽는다.

---

## §4 쟁점 2 판정 — sentinel 값과 하위 호환

### 4.1 `0` 채택

```csharp
// src/MCPhoto.Core/Settings/CutCountPolicy.cs
public const int AutoCutCount = 0;
```

`0`과 `-1` 모두 기술적으로 동작한다(VF-11: `-1`도 ini 왕복 가능). `0`을 고른 이유:

1. **오타 방어 여지가 넓다.** `0`을 sentinel로 두면 `-1`은 여전히 "잘못된 값"이라 `ClosestFrom`이 6으로 보정한다
   (기존 규칙 유지). 반대로 `-1`을 sentinel로 두면 `0`이 무보정 통과 대상이 아니어서 6이 되는데, 운영자가
   "자동"을 의도해 `0`을 적었을 때 조용히 6이 되어 **의도와 다른 동작**이 된다. 요구사항 원문도 `0`을 먼저 언급했다.
2. **`0`은 사고로 생성될 수 없다.** ini 누락·손상·빈 값은 전부 fallback 6으로 귀결한다(VF-11). `CutCount=0`이라는
   명시적 입력 또는 UI에서 "자동" 선택만이 sentinel을 만든다.
3. **count 필드에 음수를 쓰지 않는다.** 외부 ini 편집기·후속 웹 클라이언트 이식 시 부호 처리 이슈를 남기지 않는다.

`CaptureSession.Discard()`가 `CutCount = 0`을 쓰는 것과 숫자가 겹치지만 **의미 충돌은 없다**:
`AppSettings.CutCount`(설정 의도)와 `CaptureSession.CutCount`(세션 실효값)는 다른 객체의 다른 개념이고,
sentinel은 `Resolve`를 통과하며 소멸해 세션 쪽으로 전파되지 않는다(§0.4). 이 점을 `Discard()` 주석에 명시한다.

### 4.2 `Clamp()` 정규화 경로

```csharp
// AppSettings.Clamp() — 변경 지점
// it17: 자동(sentinel)은 최근접 보정 대상이 아니다. 가드가 없으면 ClosestFrom(0, {6,8,10})이 0을 6으로
//       덮어써(VF-2) 저장 왕복 한 번에 "자동" 설정이 소멸한다.
if (!CutCountPolicy.IsAuto(CutCount) && Array.IndexOf(AllowedCutCounts, CutCount) < 0)
    CutCount = ClosestFrom(CutCount, AllowedCutCounts, 6);
```

`Clamp()`는 로드·저장 양쪽에서 불린다(`IniSettingsService.Load():52`, `Save():60`) — 가드 한 곳으로 양방향이 해결된다.

### 4.3 하위 호환 진리표

| ini 내용 | 종전 결과 | it17 결과 | 판정 |
|----------|-----------|-----------|------|
| 파일 없음 | 6 | 6 | 동일 ✅ |
| `CutCount=6` / `=8` / `=10` | 6 / 8 / 10 | 6 / 8 / 10 | 동일 ✅ |
| `CutCount=7` | 6 (VF-19 테스트가 고정) | 6 | 동일 ✅ |
| `CutCount=3` | 6 | 6 | 동일 ✅ |
| `CutCount=notanumber` | 6 (fallback, VF-11) | 6 | 동일 ✅ |
| `CutCount=-1` | 6 | 6 | 동일 ✅ (sentinel 아님) |
| `CutCount=0` | 6 | **0 = 자동** | 신규 동작 ⚠️ |

`0`을 명시적으로 적어 둔 기존 ini가 있을 가능성: **없다.** `Save()`가 항상 `Clamp()` 후 기록하므로 종전 코드가
쓴 값은 `{6,8,10}`뿐이고, 손으로 `0`을 적었더라도 첫 로드 시 6으로 보정된 뒤 다시 6으로 기록됐다.

`AllowedCutCounts`는 `{6,8,10}` 그대로 둔다 — sentinel을 이 배열에 넣으면 `Clamp` 가드가 필요 없어지는 대신
`ClosestFrom`이 `CutCount=3` 같은 오입력을 **6이 아니라 0(자동)**으로 보정해버린다(|3-0|=3 < |3-6|=3 → 첫 값 0 승리).
명백한 회귀이므로 배열은 건드리지 않는다.

---

## §5 쟁점 3 판정 — 계산식과 임의 정수 컷 수 내성

### 5.1 계산식

```
실제 컷 수 = 자동이면  max(AutoMinimum, 슬롯 수 + AutoMargin) = max(6, 슬롯 + 2)
             고정이면  max(설정값, 슬롯 수)                     ← 종전 VF-4 동작 그대로
```

| 슬롯 수 | 자동 결과 | 여유분 | 고정 6 결과(종전) |
|---------|-----------|--------|-------------------|
| 1 | 6 | +5 | 6 |
| 2 | 6 | +4 | 6 |
| 3 | 6 | +3 | 6 |
| 4 | 6 | +2 | 6 |
| 5 | **7** | +2 | 6 (여유 +1) |
| 6 | **8** | +2 | 6 (여유 **0** ← 피드백의 문제) |

슬롯 4개 이하에서는 최소 6이 이미 +2를 초과하므로 자동과 고정 6이 같다. 실질 차이는 **슬롯 5·6개 프레임**에서만 발생한다 —
정확히 피드백이 지적한 구간이다.

### 5.2 파이프라인의 임의 정수 내성 (7컷이 안전한가)

| 소비 지점 | 컷 수 의존 형태 | 7 허용? | 근거 |
|-----------|-----------------|---------|------|
| 촬영 루프 | `for (cut = 1; cut <= TotalCuts; cut++)` | ✅ | VF-7 |
| 컷 버퍼 상한 | `if (_cuts.Count < CutCount)` | ✅ | `CaptureSession.cs:47` |
| 촬영 완료 판정 | `_cuts.Count >= CutCount` | ✅ | `CaptureSession.cs:31` |
| 컷 선택 화면 배치 | `WrapPanel`(열 수 하드코딩 없음) | ✅ | VF-8 |
| 선택 상한 | `_selection.Count >= SlotCount` (컷 수 무관) | ✅ | `CaptureSession.cs:63` |
| 합성 입력 | `GetSelectedCuts()` = 슬롯 수만큼 | ✅ | VF-9 |
| 타임랩스/세션 녹화 | 세션 전체 녹화(컷 수 무관) | ✅ | `CaptureViewModel.cs:136-165` |
| Guide 표시 | `Text="{Binding CutCount}"` | ✅ | `GuideView.xaml:16` |
| 설정 콤보 | `AllowedCutCounts`만 노출 — **7은 옵션이 아님** | 해당 없음 | §5.3 |

**결론: 허용 집합 확장도, 자동 전용 우회 경로도 필요 없다.** 파이프라인은 이미 임의 정수 N을 처리한다.

### 5.3 `AllowedCutCounts`를 확장하지 않는 이유 (명시적 판정)

7을 `AllowedCutCounts`에 넣으면 ① 설정 콤보에 "7컷"이 수동 옵션으로 노출되어 요구사항 범위를 넘고,
② VF-19 회귀 테스트(`7 → 6` 보정)가 깨지며, ③ 자동 파생값(9, 11 등 슬롯 상한이 바뀔 때)을 매번 배열에 추가해야 하는
유지보수 부채가 생긴다. **설정 도메인(`{0} ∪ {6,8,10}`)과 실효값 도메인(임의 양의 정수)을 분리 유지**하는 것이
이 설계의 핵심 불변식이다.

### 5.4 정책 클래스 전문

```csharp
// src/MCPhoto.Core/Settings/CutCountPolicy.cs (신규) — UTF-8 no BOM
namespace MCPhoto.Core.Settings;

/// <summary>
/// 촬영 컷 수 정책(순수 함수 — UI·설정 인스턴스 무의존). (it17)
/// 설정값 <see cref="AppSettings.CutCount"/>는 "의도"만 담는다: 고정 컷 수(6/8/10) 또는
/// 자동(<see cref="AutoCutCount"/>). 실제 촬영 컷 수는 프레임 슬롯 수가 확정된 뒤
/// (<see cref="Capture.CaptureSession.Begin"/>) 이 클래스가 산출한다 — 유일한 해석 지점.
/// </summary>
public static class CutCountPolicy
{
    /// <summary>
    /// "자동" 모드 sentinel(ini에 그대로 기록된다). 0은 ini 누락·손상으로는 만들어질 수 없어
    /// (IniFile.GetInt가 fallback을 돌려줌) 명시적 의도만을 나타낸다. 설계 §4.1.
    /// </summary>
    public const int AutoCutCount = 0;

    /// <summary>자동 모드의 최소 촬영 컷 수(고정 기본값과 동일 — PRD "최소 6").</summary>
    public const int AutoMinimum = 6;

    /// <summary>자동 모드에서 슬롯 수에 더하는 여유분. 컷 선택의 여지를 확보한다(요구사항 §0.1).</summary>
    public const int AutoMargin = 2;

    /// <summary>설정값이 자동 모드인가. -1 등 다른 음수는 자동이 아니다(§4.1).</summary>
    public static bool IsAuto(int configured) => configured == AutoCutCount;

    /// <summary>
    /// 실제 촬영 컷 수 산출.
    /// 자동: max(<see cref="AutoMinimum"/>, 슬롯 + <see cref="AutoMargin"/>).
    /// 고정: max(설정값, 슬롯) — "컷 수 ≥ 슬롯 수" 불변 유지(종전 동작 그대로).
    /// slotCount가 음수/0(프레임 미확정)이면 0으로 취급 → 자동은 6, 고정은 설정값.
    /// </summary>
    public static int Resolve(int configured, int slotCount)
    {
        int slots = Math.Max(slotCount, 0);
        return IsAuto(configured)
            ? Math.Max(AutoMinimum, slots + AutoMargin)
            : Math.Max(configured, slots);
    }
}
```

> **의존 방향**: `AppSettings` → `CutCountPolicy` (단방향). `AutoCutCount`를 `AppSettings`에 두면
> `CutCountPolicy` → `AppSettings` 역방향 참조가 생겨 양방향이 된다. VF-13의 `AppSettings.NormalizeQr()` →
> `QrDeliveryPolicy` 관례와 같은 방향으로 맞춘다.

---

## §6 쟁점 4 판정 — UI 표기

### 6.1 콤보박스

- **항목 순서**: `자동` → `6컷` → `8컷` → `10컷`. "자동"이 최상단인 이유: 신규 권장 항목이며,
  숫자 항목 사이에 끼면 정렬 규칙이 없는 이물감이 생긴다.
- **라벨**: `자동` / `6컷` / `8컷` / `10컷`. 숫자 항목에 "컷" 단위를 붙이는 이유 — "자동"과 나란히 놓이면
  맨숫자는 단위가 모호해진다(카운트다운 콤보의 `3/6/8/10`은 라벨이 "컷당 카운트다운(초)"로 단위를 이미 말해준다).
- **바인딩**: VF-12의 `DisplayModeOption` 패턴을 그대로 재사용 —
  `DisplayMemberPath="Label" SelectedValuePath="Value" SelectedValue="{Binding CutCount}"`.
  `SelectedItem` 방식은 record 인스턴스 동일성에 의존해 로드 시 선택 복원이 어긋날 수 있으므로 쓰지 않는다.

### 6.2 "자동 (8회)" 표기를 채택하지 않는 이유 (명시적 판정)

설정 화면 진입 시점에는 프레임이 **선택되어 있지 않다**(`FrameSelect`는 설정과 무관한 별도 상태이며,
설정은 오버레이로 진입한다). 슬롯 수를 모르므로 괄호 안의 숫자는 **추측값**이 되어 실제 촬영 수와 어긋날 수 있다 —
"저장되었습니다" 오인과 같은 부류의 신뢰 훼손이다. 대신 두 지점으로 나눈다:

| 지점 | 표기 | 시점에 알 수 있는가 |
|------|------|---------------------|
| 설정(SettingsView) | 콤보 아래 캡션 `자동: 프레임 슬롯 수 + 2장 촬영(최소 6장)` — **규칙만** | ✅ 규칙은 상수 |
| 촬영 안내(GuideView) | `촬영 컷 수  8 컷 (자동)` — **확정된 실제 숫자** | ✅ 프레임 확정 후 |

### 6.3 레이아웃 점프 방지

캡션은 `자동` 선택 시에만 보인다. 콤보를 `자동 ↔ 6컷`으로 토글할 때 아래 항목들이 위아래로 밀리지 않도록,
VF-15의 관례대로 **높이 22px를 상시 예약**한 `Grid` 안에 캡션을 넣는다.

---

## §7 쟁점 5 판정 — 컷 수 노출·소비 지점 전수 영향 분석

### 7.1 `AppSettings.CutCount`(설정 원값) 소비자 — 전 3곳

| 위치 | 용도 | it17 영향 |
|------|------|-----------|
| `SettingsViewModel.cs:195` (`LoadSettings`) | ini → VM 프로퍼티 | **변경 없음**. sentinel 0을 그대로 실어 콤보가 "자동"을 선택 복원 |
| `SettingsViewModel.cs:291` (`SaveSettings`) | VM → ini. 게이트 없음(VF-14) | **변경 없음**. 0이 그대로 기록되고 `Clamp` 가드가 살려둔다 |
| `FrameSelectViewModel.cs:210` (`Next`) | `Capture.Begin(frame, cutCount)` 인자 | **변경 없음**(§0.4). `Begin`이 내부에서 해석 |

### 7.2 `CaptureSession.CutCount`(실효값) 소비자 — 전 2곳

| 위치 | 용도 | it17 영향 |
|------|------|-----------|
| `GuideViewModel.cs:23` | 안내 화면 표시 | 해석된 실제 숫자를 그대로 표시(자동이면 7·8이 뜬다). `IsAutoCutCount` 추가로 "(자동)" 배지 |
| `CaptureViewModel.cs:62` | `TotalCuts` → 촬영 루프 | **변경 없음**. 7컷도 그대로 순회(VF-7) |

### 7.3 그 밖의 화면 — 영향 없음 (확인 완료)

| 화면/로직 | 컷 수 의존 | 판정 |
|-----------|------------|------|
| `CutSelectViewModel` | `SlotCount`·`Selection`만 사용. 컷 수는 `Cuts.Count`(=실제 촬영분)로 자연 반영 | 코드 변경 **불필요**. 자동이면 썸네일이 7·8장 뜨고 선택 상한은 여전히 슬롯 수 |
| 재촬영(`BeginFullRetake`) | `CutCount`·`Frame` 보존(VF-10) | 재촬영해도 해석값 유지. **재해석 없음** — 프레임이 안 바뀌므로 재해석해도 같은 값이나, 보존이 더 명확하다 |
| `ResultViewModel` / 합성 / 업로드 / QR | 선택된 슬롯 수만큼의 이미지만 사용(VF-9) | 영향 없음 |
| 가이드 문구 `"촬영 중 [바로 촬영]으로…"` | 숫자 없음 | 영향 없음 |
| 게스트 편집 게이트(it12/it13) | `CutCount`는 게이트 비대상(VF-14) | **비목표**: 게스트도 "자동" 선택 가능. 게이트 확대는 이번 범위 아님 |
| 서버(`web/functions`) | `cutCount`는 PRD 문서상 개념일 뿐 세션 문서에 기록되지 않음 | 서버 변경 **없음** |

---

## §8 스레딩·안전·인코딩

- **스레딩**: 이번 변경은 전부 순수 계산·프로퍼티 대입이다. 새 이벤트 구독·타이머·백그라운드 작업이 **없다**
  → 구독 해제 경로 신설 없음, 누수 위험 0. `CaptureSession.Begin`은 이미 UI 스레드(`Next` 커맨드)에서 호출된다.
- **UI 스레드 규칙**: `CutCountPolicy.Resolve`는 O(1) 정수 연산 — 블로킹 없음.
- **ViewModel의 UI 타입 의존**: `IsAutoCutCount`는 `bool`로 노출하고 `Visibility` 변환은 XAML의 `BoolToVis`가 담당
  (기존 관례 유지). ViewModel은 `System.Windows` 타입을 새로 참조하지 않는다.
- **리소스 키**: 신규 리소스 키를 **추가하지 않는다**(기존 `Text.Caption`·`BoolToVis`·`Brush.Text.Muted` 재사용) → 키 충돌 0.
- **파일 인코딩**: 수정 대상 전 파일이 **UTF-8 no BOM**(VF-17). 신규 파일(`CutCountPolicy.cs`,
  `CutCountPolicyTests.cs`)도 **UTF-8 no BOM**으로 생성한다. 한글 주석이 깨지면 인코딩 사고이므로
  Step 별 검증에 `grep`으로 한글 주석 가독 확인을 포함한다.
- **DPI/테마**: 새 시각 요소는 기존 스타일(`Text.Caption`)을 그대로 쓰므로 별도 대응 불필요.
- **전역 예외**: 새 예외 발생 경로 없음(정수 연산만).
- **빌드 설정 전제**(`Directory.Build.props`): `ImplicitUsings=enable`(→ `using System;` 불필요),
  `Nullable=enable`, `LangVersion=12.0`(→ 컬렉션 초기화의 `new(...)` 타깃 타입 추론 사용 가능),
  `GenerateDocumentationFile=false`(→ XML `<see cref>` 해석 실패가 경고를 만들지 않는다),
  `TreatWarningsAsErrors=false`이지만 **변경 프로젝트 warning 0 원칙**을 지킨다.

---

## §9 파일별 역할 (변경 인벤토리)

| # | 파일 | 종류 | 변경 내용 | Step |
|---|------|------|-----------|------|
| 1 | `src/MCPhoto.Core/Settings/CutCountPolicy.cs` | **신규** | `AutoCutCount`/`AutoMinimum`/`AutoMargin` 상수 + `IsAuto`/`Resolve` 순수 함수 (§5.4 전문) | 1 |
| 2 | `src/MCPhoto.Core/Settings/AppSettings.cs` | 수정 | `Clamp()`의 `CutCount` 보정에 `!CutCountPolicy.IsAuto(...)` 가드. `CutCount` XML 주석에 자동 옵션 명기 | 2 |
| 3 | `src/MCPhoto.Core/Capture/CaptureSession.cs` | 수정 | `Begin`이 `CutCountPolicy.Resolve` 호출. `IsAutoCutCount` 프로퍼티 신설 + `Discard()`에서 리셋 | 3 |
| 4 | `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 수정 | `CutCountOptions` 타입 변경(`IReadOnlyList<CutCountOption>`) + 빌더, `_cutCount`에 `[NotifyPropertyChangedFor(nameof(IsAutoCutCount))]`, `IsAutoCutCount` 프로퍼티, `CutCountOption` record 추가 | 4 |
| 5 | `src/MCPhoto.App/Views/SettingsView.xaml` | 수정 | 컷 수 콤보에 `DisplayMemberPath`/`SelectedValuePath`/`SelectedValue` 적용 + 자동 규칙 캡션 행(높이 22 예약) | 5 |
| 6 | `src/MCPhoto.App/ViewModels/GuideViewModel.cs` | 수정 | `IsAutoCutCount` observable 프로퍼티 + `OnEnterAsync`에서 세션값 반영 | 6 |
| 7 | `src/MCPhoto.App/Views/GuideView.xaml` | 수정 | 컷 수 우측에 "(자동)" 배지 `TextBlock` | 6 |
| 8 | `tests/MCPhoto.Tests/CutCountPolicyTests.cs` | **신규** | `Resolve`/`IsAuto` 진리표 검증 | 1 |
| 9 | `tests/MCPhoto.Tests/SettingsTests.cs` | 수정 | sentinel 왕복·Clamp 가드·하위 호환 3종 추가 | 2 |
| 10 | `tests/MCPhoto.Tests/CaptureSessionTests.cs` | 수정 | 자동 해석·최소 6·재촬영 보존·Discard 리셋 | 3 |
| 11 | `tests/MCPhoto.Tests/SettingsViewModelTests.cs` | 수정 | 콤보 옵션 구성·`IsAutoCutCount` 통지·저장 왕복 | 4 |
| 12 | `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs` | 수정 | `Next()` → 세션에 해석값이 실리는지(엔드투엔드 배선) | 7 |
| 13 | `docs/analysis/12-exe-app-settings-and-config.md` | 문서 | `CutCount` 행에 자동 sentinel·보조 상수 갱신 | 8 |
| 14 | `docs/analysis/11-exe-app-features.md` | 문서 | §촬영 컷수 규칙에 자동 모드 반영 | 8 |
| 15 | `docs/analysis/13-client-behavior-spec.md` | 문서 | 설정 표·실제 촬영 컷 수 공식 갱신 | 8 |
| 16 | `docs/analysis/41-local-data-and-file-formats.md` | 문서 | ini `CutCount` 행 갱신 | 8 |
| 17 | `docs/prd/photobooth-prd.md` | 문서 | §10·§12 확정 사항에 자동 모드 추가(기존 결론 취소가 아니라 **확장**으로 기술) | 8 |

**변경 없음(명시)**: `FrameSelectViewModel.cs`, `CaptureViewModel.cs`, `CutSelectViewModel.cs`,
`IniSettingsService.cs`, `AppSettings.AllowedCutCounts`, `web/functions/**`.

---

## §10 테스트 계획

베이스라인 **758 통과**(VF-18). 아래 신규 **23건**을 더해 **781 이상**이 목표치다
(`[Theory]`는 InlineData 개수만큼 전개되므로 실제 집계는 더 늘어난다 — 하한만 검증한다).

### 10.1 `CutCountPolicyTests.cs` (신규 · 9건)

| # | 테스트 | 기대 |
|---|--------|------|
| T1 | `Resolve_Auto_By_SlotCount` `[Theory]` (1,6)(2,6)(3,6)(4,6)(5,7)(6,8) | §5.1 표와 일치 |
| T2 | `Resolve_Auto_Respects_Minimum` — `Resolve(0, 0)` | 6 |
| T3 | `Resolve_Auto_Guards_Negative_SlotCount` — `Resolve(0, -5)` | 6 (음수 미전파) |
| T4 | `Resolve_Auto_Handles_Oversized_Frame` — `Resolve(0, 8)` | 10 (A-1 동작 고정) |
| T5 | `Resolve_Fixed_Keeps_Legacy_Max` `[Theory]` (6,3→6)(6,6→6)(8,6→8)(10,6→10) | 종전 `Math.Max` 동작 동일 |
| T6 | `Resolve_Fixed_Never_Below_SlotCount` — `Resolve(6, 8)` | 8 (VF-4 불변 유지) |
| T7 | `IsAuto_Only_For_Zero` `[Theory]` (0→true)(-1→false)(6→false)(8→false)(10→false)(7→false) | §4.1 |
| T8 | `AutoCutCount_Is_Zero` | `CutCountPolicy.AutoCutCount == 0` (sentinel 고정 — 값이 바뀌면 ini 하위 호환이 깨진다) |
| T9 | `Auto_Constants_Match_Requirement` | `AutoMinimum == 6 && AutoMargin == 2` |

### 10.2 `SettingsTests.cs` 확장 (4건)

| # | 테스트 | 기대 |
|---|--------|------|
| T10 | `CutCount_Auto_Survives_Clamp` — `new AppSettings { CutCount = 0 }.Clamp()` | `CutCount == 0` (§4.2 가드) |
| T11 | `CutCount_Auto_RoundTrips_Through_Ini` — `Save()` 후 파일에 `CutCount=0` 포함 & 재로드 시 0 | sentinel 왕복 보존 |
| T12 | `CutCount_Negative_Snaps_To_Allowed` — `CutCount = -1` → `Clamp()` | 6 (자동이 **아님**, §4.1) |
| T13 | `CutCount_Legacy_Values_Unchanged` `[Theory]` (6→6)(8→8)(10→10)(7→6)(3→6) | A-4 하위 호환 |

**기존 테스트 불변 확인**: `Defaults_When_File_Missing`(6), `CutCount_Snapped_To_Allowed`(7→6),
`Save_Then_Load_RoundTrips`(8) 는 **수정하지 않는다**. 이 3건이 하위 호환의 회귀 방벽이다.

### 10.3 `CaptureSessionTests.cs` 확장 (6건)

| # | 테스트 | 기대 |
|---|--------|------|
| T14 | `Begin_Auto_Resolves_Slots_Plus_Two` — 슬롯 5 + `cutCount=0` | `CutCount == 7`, `IsAutoCutCount == true` |
| T15 | `Begin_Auto_Six_Slots_Gives_Eight` — 슬롯 6 + 0 | `CutCount == 8` (피드백 시나리오) |
| T16 | `Begin_Auto_Respects_Minimum` — 슬롯 3 + 0 | `CutCount == 6` |
| T17 | `Begin_Fixed_Sets_IsAuto_False` — 슬롯 6 + `cutCount=6` | `CutCount == 6`, `IsAutoCutCount == false` |
| T18 | `FullRetake_Preserves_Resolved_CutCount` — 슬롯 5 + 0 → `BeginFullRetake()` | `CutCount == 7` 유지, `IsAutoCutCount == true` 유지, `Cuts` 비워짐 |
| T19 | `Discard_Resets_IsAutoCutCount` — 슬롯 5 + 0 → `Discard()` | `CutCount == 0`, `IsAutoCutCount == false` |

**기존 테스트 불변**: `Begin_Sets_Frame_And_CutCount`(6), `CutCount_Never_Below_SlotCount`(6), `AddCut_Caps_At_CutCount`.

### 10.4 `SettingsViewModelTests.cs` 확장 (3건)

| # | 테스트 | 기대 |
|---|--------|------|
| T20 | `CutCountOptions_Auto_First_Then_Allowed` | `Count == 4`, `[0] == (0, "자동")`, `[1..3].Value == {6,8,10}` |
| T21 | `IsAutoCutCount_Tracks_CutCount` — `CutCount = 0` → true, `= 6` → false | `[NotifyPropertyChangedFor]` 배선 확인 |
| T22 | `Auto_CutCount_Saved_To_Ini` — VM `CutCount = 0` → `SaveSettingsCommand.Execute(null)` → 재로드 | `settings.Current.CutCount == 0` (게이트 비대상이라 게스트에서도 통과, VF-14) |

### 10.5 `FrameSelectViewModelTests.cs` 확장 (1건)

| # | 테스트 | 기대 |
|---|--------|------|
| T23 | `Next_With_Auto_Setting_Resolves_Session_CutCount` — ini `CutCount=0`, 슬롯 5개 프레임 선택 → `NextCommand` | `shell.Session.Capture.CutCount == 7`, `IsAutoCutCount == true` (설정 → 프레임 선택 → 세션 배선 엔드투엔드) |

### 10.6 XAML 회귀

`XamlResourceTests`가 `SettingsView`/`GuideView`의 `StaticResource` 해석을 headless로 검증한다(VF-16 리소스만 사용하므로
통과가 기대치). 새 리소스 키를 도입하지 않았음을 이 테스트가 간접 확인한다.

### 10.7 수동 확인 시나리오 (Step 8)

| # | 조작 | 기대 관측 |
|---|------|-----------|
| M1 | 설정 → 촬영 컷 수 콤보 열기 | `자동 / 6컷 / 8컷 / 10컷` 4항목, 라벨 잘림 없음 |
| M2 | `자동` 선택 | 콤보 아래 캡션 표시. 아래 항목들의 세로 위치 **불변**(§6.3) |
| M3 | `6컷` 선택 | 캡션만 사라지고 레이아웃 **불변** |
| M4 | `자동` 선택 → 저장 → 앱 재시작 → 설정 재진입 | 콤보가 `자동` 유지, `MCPhoto.ini`에 `CutCount=0` |
| M5 | `자동` 상태로 슬롯 6개 프레임 선택 → Guide | `촬영 컷 수 8 컷 (자동)`, `선택할 컷 6 장` |
| M6 | 그대로 촬영 진행 | 8회 촬영 → 컷 선택에 8장 → 6장 선택해야 [다음] 활성 |
| M7 | `자동` 상태로 슬롯 5개 프레임 선택 → Guide | `7 컷 (자동)` — 허용 집합에 없는 7이 정상 동작 |
| M8 | `6컷` 상태로 슬롯 6개 프레임 → Guide | `6 컷`, "(자동)" 배지 **없음** (종전 동작 회귀 없음) |

---

## §11 구현 WBS

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 Step은 **self-contained** — 대화 컨텍스트 없는 에이전트가 그 Step만 읽고 실행 가능.
> 공용 검증 명령:
> `dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q` → 경고 0 · 오류 0
> `dotnet test MCPhoto.sln -c Debug --nologo` → 전량 통과(**758 이상**, 최종 781 이상)
> **모든 신규/수정 파일은 UTF-8 no BOM 유지**(VF-17). BOM이 붙으면 리뷰에서 반려된다.

### Step 1: `CutCountPolicy` 순수 정책 클래스 신설
- **Context Brief**: MCPhoto(WPF 포토부스)의 촬영 컷 수는 `AppSettings.CutCount`(허용 {6,8,10}, 기본 6)로 설정한다.
  슬롯 6개 프레임 + 6컷이면 6장 찍어 6칸을 다 채워 컷 선택의 여지가 0이 되는 문제가 있다. 이를 해결하려
  "자동"(ini 값 `0`) 모드를 도입한다: 실제 컷 수 = `max(6, 슬롯 수 + 2)`. 이 Step은 그 계산을 담는
  **의존성 없는 순수 static 클래스**만 만든다(다른 코드는 아직 이 클래스를 호출하지 않는다).
  기존 관례: `src/MCPhoto.Core/Settings/`에 `QrDeliveryPolicy`, `DisplayApplyPolicy` 같은 순수 정책 클래스가
  이미 있고 각각 전용 테스트 파일을 가진다.
- **대상 파일**: `src/MCPhoto.Core/Settings/CutCountPolicy.cs`(신규),
  `tests/MCPhoto.Tests/CutCountPolicyTests.cs`(신규)
- **선행 조건**: 없음
- **구현 내용**: 설계 §5.4의 클래스 전문을 그대로 작성(상수 `AutoCutCount=0`/`AutoMinimum=6`/`AutoMargin=2`,
  `IsAuto`, `Resolve`). 테스트는 §10.1의 T1~T9를 xUnit `[Fact]`/`[Theory]`로 작성.
  `MCPhoto.Core`는 ImplicitUsings가 켜져 있어 `using System;`을 쓰지 않는다(기존 `AppSettings.cs`와 동일).
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~CutCountPolicyTests"
  ```
- **완료 기준**:
  - [관측] `CutCountPolicyTests` 9건 전부 통과. `Resolve(0,5)==7`, `Resolve(0,6)==8`, `Resolve(0,3)==6`,
    `Resolve(6,6)==6`, `IsAuto(-1)==false`가 출력으로 확인된다.
  - [non-goal] 이 Step에서는 **어떤 기존 파일도 수정하지 않는다** — `git status`에 신규 2파일만 나타난다.
    기존 758건 테스트 결과 불변.
  - [trigger] 없음(순수 함수 — 호출자 없이 테스트로만 구동).
- **롤백**: 신규 2파일 삭제(다른 Step과 완전 독립).
- [ ] 완료

### Step 2: `AppSettings.Clamp()`에 자동 sentinel 가드 추가
- **Context Brief**: `AppSettings.Clamp()`는 로드·저장 양쪽에서 호출되며(`IniSettingsService.Load()`/`Save()`),
  `CutCount`가 `AllowedCutCounts={6,8,10}`에 없으면 `ClosestFrom(CutCount, AllowedCutCounts, 6)`으로
  **가장 가까운 허용값**으로 덮어쓴다(`AppSettings.cs:159-160`). 자동 모드 sentinel은 `0`인데(Step 1의
  `CutCountPolicy.AutoCutCount`), 가드가 없으면 `ClosestFrom(0,{6,8,10})`이 0을 **6으로 덮어써**
  저장 왕복 한 번에 "자동" 설정이 소멸한다. 이 Step은 그 가드만 넣는다.
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`, `tests/MCPhoto.Tests/SettingsTests.cs`
- **선행 조건**: Step 1의 `CutCountPolicy`
- **구현 내용**:
  1. `Clamp()`의 `CutCount` 보정 2줄을 아래로 교체.
     ```csharp
     // it17: 자동(sentinel 0)은 최근접 보정 대상이 아니다. 가드가 없으면 ClosestFrom이 0을 6으로
     //       덮어써 저장 왕복 한 번에 "자동" 설정이 소멸한다. -1 등 다른 값은 종전대로 보정된다.
     if (!CutCountPolicy.IsAuto(CutCount) && Array.IndexOf(AllowedCutCounts, CutCount) < 0)
         CutCount = ClosestFrom(CutCount, AllowedCutCounts, 6);
     ```
  2. `CutCount` 프로퍼티 XML 주석을 갱신: `/// <summary>촬영 컷 수. 기본 6, 옵션 6/8/10(최소 6) 또는 자동(<see cref="CutCountPolicy.AutoCutCount"/>=0 → 실제 컷 수는 CaptureSession.Begin이 산출). (it17)</summary>`
  3. `AllowedCutCounts` 위 주석에 "자동 sentinel은 이 배열에 넣지 않는다(넣으면 CutCount=3 오입력이 6이 아니라 0으로 보정됨 — 설계 §4.3)" 한 줄 추가.
  4. `SettingsTests.cs`에 §10.2의 T10~T13 추가. **기존 테스트는 한 줄도 수정하지 않는다.**
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~SettingsTests"
  ```
- **완료 기준**:
  - [관측] `CutCount=0` → `Clamp()` 후 0 유지, ini 파일에 `CutCount=0` 기록, 재로드 시 0.
    `CutCount=-1`·`=7`·`=3` → 전부 6. `SettingsTests` 전량 통과.
  - [non-goal] `AllowedCutCounts` 배열 내용 **불변**({6,8,10}). 기존 테스트
    `Defaults_When_File_Missing`·`CutCount_Snapped_To_Allowed`·`Save_Then_Load_RoundTrips` **수정 없이** 통과.
    다른 설정 필드(CountdownSec/RetakeLimit/RetentionHours)의 Clamp 동작 불변.
  - [trigger] sentinel 보존은 `CutCount`가 정확히 0일 때만 — 음수·소수·문자열 입력은 종전 보정 경로를 탄다.
- **롤백**: `AppSettings.cs`의 가드 조건을 원복(`if (Array.IndexOf(...) < 0)`), 추가 테스트 4건 삭제.
- [ ] 완료

### Step 3: `CaptureSession.Begin`에서 자동 해석 (단일 해석 지점)
- **Context Brief**: 촬영 세션은 `CaptureSession.Begin(FrameTemplate frame, int cutCount)`로만 시작된다
  (`FrameSelectViewModel.Next()`가 프레임을 고정한 직후 `Settings.Current.CutCount`를 넘겨 호출).
  `Begin`은 프레임을 인자로 받으므로 **이 시점에 슬롯 수가 확정**되어 있다. 따라서 자동 sentinel(0)을
  실제 컷 수로 바꾸는 해석은 여기 한 곳에서만 일어나야 한다 — 호출측을 고치지 않으므로 미래에 새 호출 지점이
  추가돼도 해석을 빠뜨릴 수 없다. 종전 `Begin`은 `CutCount = Math.Max(cutCount, frame.Slots.Count)`였고,
  이 "컷 수 ≥ 슬롯 수" 불변은 고정 모드에서 **그대로 유지**해야 한다.
- **대상 파일**: `src/MCPhoto.Core/Capture/CaptureSession.cs`, `tests/MCPhoto.Tests/CaptureSessionTests.cs`
- **선행 조건**: Step 1의 `CutCountPolicy`
- **구현 내용**:
  1. `using MCPhoto.Core.Settings;`를 파일 상단에 추가(현재 `using MCPhoto.Core.Models;`만 있음).
  2. `CutCount` 프로퍼티 아래에 신설:
     ```csharp
     /// <summary>이 세션의 컷 수가 자동 모드로 산출됐는지(Guide 화면 "(자동)" 배지). 설정은 세션 중에도
     /// 바뀔 수 있으므로 세션이 시작 시점의 의도를 기억한다(설계 §3.3). (it17)</summary>
     public bool IsAutoCutCount { get; private set; }
     ```
  3. `Begin`을 교체:
     ```csharp
     public void Begin(FrameTemplate frame, int cutCount)
     {
         Frame = frame;
         // it17: cutCount는 "의도"(고정 6/8/10 또는 자동=CutCountPolicy.AutoCutCount).
         //       슬롯 수가 확정된 이 지점이 유일한 해석 지점이다(설계 §0.4).
         //       자동 = max(6, 슬롯+2) → 슬롯보다 여유분이 남아 컷 선택의 여지가 생긴다.
         //       고정 = max(설정, 슬롯) → 컷수 ≥ 슬롯 불변 유지(VF-5, 종전 동작 그대로).
         CutCount = CutCountPolicy.Resolve(cutCount, frame.Slots.Count);
         IsAutoCutCount = CutCountPolicy.IsAuto(cutCount);
         _cuts.Clear();
         _selection.Clear();
     }
     ```
  4. `Discard()`에 `IsAutoCutCount = false;` 추가 + 주석 `// CutCount=0은 여기선 "세션 없음"이라는 뜻이며 자동 sentinel과 무관하다(설계 §4.1).`
  5. `BeginFullRetake()`/`ResetForRetake()`는 **건드리지 않는다** — `CutCount`·`IsAutoCutCount` 보존이 의도된 동작.
  6. `CaptureSessionTests.cs`에 §10.3의 T14~T19 추가. **기존 3건은 수정하지 않는다.**
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~CaptureSessionTests"
  ```
- **완료 기준**:
  - [관측] 슬롯 6 + `cutCount=0` → `CutCount==8`·`IsAutoCutCount==true`. 슬롯 5 + 0 → 7. 슬롯 3 + 0 → 6.
    슬롯 6 + 6 → 6·`IsAutoCutCount==false`. `BeginFullRetake()` 후에도 7 유지. `Discard()` 후 0/false.
  - [non-goal] `FrameSelectViewModel.cs`는 **수정하지 않는다**(`git diff`에 나타나면 실패). 고정 모드
    `Begin` 결과가 종전과 비트 단위로 동일 — 기존 `Begin_Sets_Frame_And_CutCount`·`CutCount_Never_Below_SlotCount`
    ·`AddCut_Caps_At_CutCount`가 수정 없이 통과.
  - [trigger] 해석은 `Begin` 호출 시 1회만. 이후 `AddCut`/`ToggleSelection`/재촬영이 `CutCount`를 재계산하지 않는다.
- **롤백**: `Begin`을 `CutCount = Math.Max(cutCount, frame.Slots.Count);`로 원복, `IsAutoCutCount` 프로퍼티·테스트 6건 삭제.
- [ ] 완료

### Step 4: `SettingsViewModel` — 콤보 옵션에 "자동" 추가
- **Context Brief**: 설정 화면의 촬영 컷 수 콤보는 현재 `IReadOnlyList<int> CutCountOptions = AppSettings.AllowedCutCounts`를
  그대로 노출한다(`SettingsViewModel.cs:98`). 여기에 "자동"(값 0) 항목을 넣으려면 int 목록으로는 라벨을 표현할 수 없으므로,
  같은 파일에 이미 있는 `DisplayModeOption(DisplayMode Value, string Label)` record 관례(`:371-374`)를 따라
  `CutCountOption(int Value, string Label)`을 만든다. 프로퍼티 변경 통지는 CommunityToolkit.Mvvm의
  `[NotifyPropertyChangedFor]`를 쓴다(같은 파일 `:58-60` `SavedNotice`→`HasSavedNotice` 선례).
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, `tests/MCPhoto.Tests/SettingsViewModelTests.cs`
- **선행 조건**: Step 1의 `CutCountPolicy`
- **구현 내용**:
  1. `_cutCount` 필드에 속성 부착:
     ```csharp
     [ObservableProperty]
     [NotifyPropertyChangedFor(nameof(IsAutoCutCount))]
     private int _cutCount;
     ```
  2. `CutCountOptions`(`:97-98`)를 교체 + `IsAutoCutCount`·빌더 추가:
     ```csharp
     /// <summary>컷수 옵션(콤보 바인딩). "자동"(sentinel 0) 최상단 + 고정 6/8/10. (it17)</summary>
     public IReadOnlyList<CutCountOption> CutCountOptions { get; } = BuildCutCountOptions();

     /// <summary>자동 모드 선택 여부. 설정 화면의 규칙 캡션 노출 조건(실제 컷 수는 프레임 확정 후에만
     /// 알 수 있어 여기선 숫자를 표시하지 않는다 — 설계 §6.2). (it17)</summary>
     public bool IsAutoCutCount => CutCountPolicy.IsAuto(CutCount);

     private static CutCountOption[] BuildCutCountOptions()
     {
         var list = new List<CutCountOption>(AppSettings.AllowedCutCounts.Length + 1)
         {
             new(CutCountPolicy.AutoCutCount, "자동")
         };
         foreach (var n in AppSettings.AllowedCutCounts)
             list.Add(new CutCountOption(n, $"{n}컷"));
         return list.ToArray();
     }
     ```
  3. 파일 맨 아래 `DisplayModeOption` record **바로 아래**에 추가:
     ```csharp
     /// <summary>촬영 컷 수 콤보 항목(값 + 한글 라벨). Value=0은 자동(CutCountPolicy.AutoCutCount).
     /// ToString=라벨(닫힌 박스 폴백 대비). (it17)</summary>
     public sealed record CutCountOption(int Value, string Label)
     {
         public override string ToString() => Label;
     }
     ```
  4. `LoadSettings`/`SaveSettings`의 `CutCount` 대입 줄은 **건드리지 않는다**(sentinel이 그대로 왕복).
  5. `SettingsViewModelTests.cs`에 §10.4의 T20~T22 추가(기존 `MakeVm(...)` 헬퍼 재사용 — `IniSettingsService? settings` 인자로 임시 ini 경로 주입 가능).
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~SettingsViewModelTests"
  ```
- **완료 기준**:
  - [관측] `CutCountOptions.Count == 4`이고 `[0].Value == 0 && [0].Label == "자동"`, `[1..3].Value == {6,8,10}`.
    `CutCount = 0` 대입 시 `IsAutoCutCount` 변경 통지가 발생한다. `SaveSettingsCommand` 실행 후 ini의 `CutCount`가 0.
  - [non-goal] 카운트다운·재촬영 횟수 콤보의 옵션 타입·내용 **불변**(`IReadOnlyList<int>` 유지). 게스트 편집 게이트
    로직(`IsGuest` 분기) 변경 없음 — `CutCount`는 게이트 비대상이므로 게스트도 "자동"을 저장할 수 있다.
  - [trigger] 캡션 노출 판단은 `IsAutoCutCount`뿐 — 저장 여부와 무관하게 콤보 선택 즉시 반영된다.
- **롤백**: `CutCountOptions`를 `IReadOnlyList<int> = AppSettings.AllowedCutCounts`로 원복, `CutCountOption` record·`IsAutoCutCount`·`[NotifyPropertyChangedFor]`·테스트 3건 삭제. **Step 5를 함께 롤백해야 한다**(XAML이 새 타입에 의존).
- [ ] 완료

### Step 5: `SettingsView.xaml` — 콤보 바인딩 전환 + 자동 규칙 캡션
- **Context Brief**: 설정 화면 좌열 "촬영" 그룹의 첫 행이 촬영 컷 수 콤보다(`SettingsView.xaml:100-103`).
  현재 `ItemsSource="{Binding CutCountOptions}" SelectedItem="{Binding CutCount}"`로 int를 직접 다룬다.
  Step 4에서 `CutCountOptions`가 `CutCountOption(Value, Label)` record 목록으로 바뀌었으므로 바인딩을
  값/라벨 분리 방식으로 전환한다. 같은 파일 `:222-224`의 표시 모드 콤보가 정확히 이 패턴을 쓴다.
  또한 "자동" 선택 시에만 보이는 규칙 캡션을 추가하는데, 같은 파일 `:207-214`의 관례대로 **높이를 상시 예약**해
  자동↔고정 토글 시 아래 항목이 위아래로 밀리지 않게 한다.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`
- **선행 조건**: Step 4의 `CutCountOptions`(`CutCountOption` 목록) 및 `IsAutoCutCount`
- **구현 내용**:
  1. `:101-102`의 `ComboBox`를 교체:
     ```xml
     <ComboBox Grid.Column="1" HorizontalAlignment="Right" Width="140"
               ItemsSource="{Binding CutCountOptions}"
               DisplayMemberPath="Label" SelectedValuePath="Value"
               SelectedValue="{Binding CutCount}" />
     ```
     `SelectedItem` → `SelectedValue`로 바꾸는 이유: record 인스턴스 동일성에 의존하지 않아 ini 로드 시
     선택 복원이 확실하다.
  2. 컷 수 행의 `</Grid>` **직후**(카운트다운 행 `<Grid Style="{StaticResource SettingRow}">` 앞)에 삽입:
     ```xml
     <!-- it17: 자동 규칙 안내. 설정 시점엔 프레임이 미선택이라 실제 컷 수 숫자를 못 보여준다(설계 §6.2) → 규칙만 표기.
          실제 숫자는 촬영 안내(GuideView)에서 "(자동)" 배지와 함께 확정값으로 노출된다.
          높이 22를 상시 예약해 자동↔고정 토글 시 아래 항목이 밀리지 않게 한다(it9 카메라 안내 관례). -->
     <Grid Height="22" Margin="0,2,0,6">
         <TextBlock Text="자동: 프레임 슬롯 수 + 2장 촬영(최소 6장)"
                    Style="{StaticResource Text.Caption}" Foreground="{StaticResource Brush.Text.Muted}"
                    TextTrimming="CharacterEllipsis" VerticalAlignment="Center"
                    Visibility="{Binding IsAutoCutCount, Converter={StaticResource BoolToVis}}" />
     </Grid>
     ```
  3. **신규 리소스 키를 만들지 않는다** — `Text.Caption`(Themes/Typography.xaml), `Brush.Text.Muted`,
     `BoolToVis`(App.xaml)는 모두 기존 전역 리소스다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~XamlResourceTests"
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0·오류 0, `XamlResourceTests` 통과(StaticResource 미해결 `XamlParseException` 없음).
    `grep -c "SelectedValuePath" SettingsView.xaml` == 3 (카메라·표시모드·컷수).
  - [non-goal] 카운트다운·재촬영 횟수 콤보의 XAML **불변**. 자동이 아닐 때 캡션 `TextBlock`은 `Collapsed`이며
    감싸는 `Grid`의 22px는 유지되므로 아래 항목의 세로 위치가 콤보 선택에 따라 **변하지 않는다**.
    좁은 폭 1열 폴백(code-behind `OnTwoColSizeChanged`) 동작 불변.
  - [trigger] 캡션은 콤보에서 "자동"을 선택한 순간에만 나타난다 — 저장 버튼을 누르지 않아도 보이고,
    "6컷"으로 되돌리면 즉시 사라진다. 다른 어떤 액션도 캡션 상태를 바꾸지 않는다.
- **롤백**: `ComboBox`를 `SelectedItem="{Binding CutCount}"` + `ItemsSource`만으로 원복하고 캡션 `Grid` 삭제
  (Step 4와 함께 롤백해야 컴파일된다).
- [ ] 완료

### Step 6: 촬영 안내(Guide) 화면에 "(자동)" 배지
- **Context Brief**: 촬영 직전 안내 화면(`GuideView`/`GuideViewModel`)은 `Session.Capture.CutCount`(= 이미 해석된
  실제 컷 수)와 `SlotCount`를 표시한다. 자동 모드에서는 설정에 "6"이라고 적힌 적이 없는데 8이 뜨므로,
  왜 8인지 알 수 있게 "(자동)" 배지를 붙인다. 자동 여부는 세션이 기억한다(Step 3의
  `CaptureSession.IsAutoCutCount`) — 설정은 세션 도중에도 변경될 수 있으므로 설정을 다시 읽지 않는다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/GuideViewModel.cs`, `src/MCPhoto.App/Views/GuideView.xaml`
- **선행 조건**: Step 3의 `CaptureSession.IsAutoCutCount`
- **구현 내용**:
  1. `GuideViewModel.cs`: `_mirrorMode` 아래에 추가
     ```csharp
     /// <summary>이 세션의 컷 수가 자동 모드로 산출됐는지("(자동)" 배지). 설정이 아니라 세션에서 읽는다
     /// — 세션 시작 시점의 의도가 기준(설계 §3.3). (it17)</summary>
     [ObservableProperty] private bool _isAutoCutCount;
     ```
     `OnEnterAsync`의 `SlotCount = ...` 아래에 `IsAutoCutCount = _shell.Session.Capture.IsAutoCutCount;` 추가.
  2. `GuideView.xaml`: 컷 수 행(`:15-19`)의 `<TextBlock Text=" 컷" .../>` **뒤에** 추가
     ```xml
     <!-- it17: 자동 모드로 산출된 컷 수임을 알린다(고정 설정이면 미노출). -->
     <TextBlock Text=" (자동)" Style="{StaticResource Text.Caption}"
                Foreground="{StaticResource Brush.Text.Muted}" VerticalAlignment="Center"
                Visibility="{Binding IsAutoCutCount, Converter={StaticResource BoolToVis}}" />
     ```
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~XamlResourceTests"
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0·오류 0, `XamlResourceTests` 통과.
    `grep -n "IsAutoCutCount" GuideViewModel.cs GuideView.xaml` 이 각 파일에서 히트한다.
  - [non-goal] 카운트다운·선택할 컷·안내 문구·[촬영 시작]/[취소] 버튼의 텍스트·레이아웃 **불변**.
    고정 컷 수 세션에서는 배지가 `Collapsed`이며, 배지는 `StackPanel` 안 인라인이라 숨겨질 때
    행 높이가 변하지 않는다(다른 TextBlock이 높이를 결정).
  - [trigger] 배지는 `Begin`이 자동 sentinel을 받은 세션에서만 — 프레임 선택 화면의 [다음]을 누른 시점의
    설정값이 기준이다. Guide 진입 후 설정을 바꿔도(오버레이) 이번 세션 배지는 바뀌지 않는다.
- **롤백**: `GuideViewModel`의 프로퍼티·대입 1줄, `GuideView.xaml`의 `TextBlock` 삭제(Step 3과 독립적으로 롤백 가능).
- [ ] 완료

### Step 7: 엔드투엔드 배선 테스트 (설정 → 프레임 선택 → 세션)
- **Context Brief**: Step 2~6은 각 계층을 개별 검증했다. 이 Step은 **실제 사용 경로**가 이어졌는지 확인한다:
  ini에 `CutCount=0` → `SettingsService.Current.CutCount == 0` → `FrameSelectViewModel.Next()`가 그 값을
  `Capture.Begin`에 넘김 → 세션의 `CutCount`가 슬롯+2로 해석됨. `FrameSelectViewModel`은 이번 이터레이션에서
  **수정 대상이 아니므로**, 이 테스트는 "호출측 무변경으로도 자동이 동작한다"는 설계 핵심 주장을 고정한다.
  기존 `FrameSelectViewModelTests.cs`에 `StubRepo`/`StubLocalStore` 등 페이크 하네스가 이미 있다.
- **대상 파일**: `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`
- **선행 조건**: Step 2(Clamp 가드), Step 3(Begin 해석), Step 4(VM 옵션) 완료
- **구현 내용**: §10.5의 T23 추가.
  임시 ini에 `[MCPhoto]\nCutCount=0\n`을 쓰고 `IniSettingsService(iniPath: temp)`로 셸을 구성 →
  슬롯 5개 프레임을 `Frames`에 넣고 `SelectedFrame`으로 지정 → `NextCommand.ExecuteAsync(null)` →
  `shell.Session.Capture.CutCount == 7 && shell.Session.Capture.IsAutoCutCount`를 검증.
  기존 테스트가 쓰는 셸 생성 헬퍼를 재사용하고, 없으면 같은 파일의 기존 패턴대로 새 헬퍼를 만든다.
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameSelectViewModelTests"
  ```
- **완료 기준**:
  - [관측] 새 테스트가 `CutCount == 7`·`IsAutoCutCount == true`로 통과. `FrameSelectViewModelTests` 전량 통과.
  - [non-goal] `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`가 `git diff`에 **나타나지 않는다**
    (프로덕션 코드 무변경으로 통과해야 한다). 기존 프레임 생성·삭제·편집 권한 테스트 전부 불변.
  - [trigger] 해석은 `NextCommand` 실행 시점에만 — `SelectedFrame` 변경만으로는 세션 `CutCount`가 바뀌지 않는다.
- **롤백**: 추가 테스트 1건 삭제.
- [ ] 완료

### Step 8: 문서 갱신 + 전량 검증 + 수동 확인
- **Context Brief**: MCPhoto는 `docs/analysis/`에 exe 앱의 기능·설정 사양을 문서로 유지하며, 각 문서 상단에
  "기능·옵션이 바뀌면 해당 절을 갱신한다"는 갱신 규칙이 명시돼 있다(`11-exe-app-features.md:9`).
  자동 컷 수는 설정 옵션·촬영 규칙·ini 포맷을 모두 건드리므로 4개 분석 문서와 PRD를 갱신한다.
  PRD의 기존 결론(`§12 컷수 최소 6·슬롯 최대 6 → 제약 로직 불필요`)은 **취소가 아니라 확장**으로 기술한다 —
  그 결론은 지금도 유효하고, 자동 모드는 그 위에 "여유분 보장"을 덧붙이는 것이다.
- **대상 파일**: `docs/analysis/12-exe-app-settings-and-config.md`, `docs/analysis/11-exe-app-features.md`,
  `docs/analysis/13-client-behavior-spec.md`, `docs/analysis/41-local-data-and-file-formats.md`,
  `docs/prd/photobooth-prd.md`
- **선행 조건**: Step 1~7 전부 완료
- **구현 내용**:
  | 파일 | 갱신 지점 | 내용 |
  |------|-----------|------|
  | `12-…-settings.md:27` | `CutCount` 행 | 기본 6, 허용 `{6,8,10}` **또는 `0`=자동**; 0은 최근접 보정에서 제외 |
  | `12-…-settings.md:55` | 보조 상수 | `CutCountPolicy.AutoCutCount=0`, `AutoMinimum=6`, `AutoMargin=2` 추가 |
  | `12-…-settings.md:75` | `Clamp()` 설명 | CutCount 보정에 자동 sentinel 가드가 선행함을 명기 |
  | `11-…-features.md:117` | 컷수 규칙 | `실제 촬영 수 = 자동이면 max(6, 슬롯+2), 고정이면 max(설정, 슬롯)` |
  | `11-…-features.md:110` | Guide 화면 | `IsAutoCutCount` → "(자동)" 배지 표시 추가 |
  | `11-…-features.md:221` | 설정 항목 목록 | 컷 수 `(자동/6/8/10)` |
  | `13-…-spec.md:141`·`:173`·`:577`·`:578` | 컷 수 표·공식 | 위와 동일한 공식으로 갱신 |
  | `41-…-formats.md:35`·`:63` | ini `CutCount` 행·보조 상수 | 자동 sentinel 반영 |
  | `prd:49`·`:228`·`:287`·`:289` | 촬영 설정 표 | "자동(슬롯+2, 최소 6)" 옵션 추가. §12는 기존 결론 유지 + 확장으로 기술 |
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo
  ```
  이후 §10.7의 M1~M8을 수동 실행(앱 기동 → 설정 → 프레임 선택 → 촬영).
- **완료 기준**:
  - [관측] 전체 빌드 경고 0·오류 0, `dotnet test` **781건 이상 전량 통과 / 실패 0**.
    5개 문서에서 `grep -n "자동"`이 해당 절에 히트한다. §10.7의 M1~M8 관측 결과를 보고에 첨부.
  - [non-goal] 문서 갱신이 기존 확정 사항을 **삭제하지 않는다** — PRD §12의 "컷수 최소 6·슬롯 최대 6" 결론은
    문장이 남아 있고 자동 모드가 그 아래 추가된다. `web/`·`installer/`·`publish.ps1`은 **무변경**.
  - [trigger] 수동 확인 M4(재시작 후 "자동" 유지)는 앱을 **완전히 종료한 뒤 재기동**해야 유효하다 —
    설정 화면 재진입만으로는 ini 왕복을 검증하지 못한다.
- **롤백**: 문서 5개 변경 revert(코드와 독립 — 코드는 롤백하지 않는다).
- [ ] 완료

---

## §12 리스크와 근거

| # | 리스크 | 영향 | 근거·완화 |
|---|--------|------|-----------|
| **R-1** | `Clamp()` 가드를 빠뜨리면 "자동"이 저장 왕복 1회에 6으로 소멸한다 | 기능이 조용히 사라짐(사용자는 저장됐다고 믿는다 — 가장 위험) | `Clamp()`는 로드·저장 양쪽에서 호출된다(`IniSettingsService.cs:52`, `:60`). Step 2의 T10·T11이 **정확히 이 경로**를 고정한다. 가드는 조건 한 줄이므로 리뷰에서 육안 확인 가능 |
| **R-2** | 자동이 만드는 **7컷**이 어딘가에서 허용 집합 검사를 통과하지 못한다 | 촬영 실패·크래시 | §5.2에서 소비 지점 9곳을 전수 확인 — 컷 수를 집합으로 검사하는 코드는 **설정 콤보 외에 없다**. 실효값은 `AppSettings`를 거치지 않으므로 `Clamp`/`AllowedCutCounts`에 닿지 않는다(§0.4) |
| **R-3** | 슬롯 7개 이상 프레임(손상 파일·미래 서버 데이터)이 오면 자동이 9컷 이상을 만든다 | 촬영이 길어짐 | **종전에도 동일**했다: `Math.Max(6, 슬롯)`이 이미 슬롯 수만큼 찍었다(VF-4). 자동은 새 실패 모드를 만들지 않는다. 상한 클램프는 **의도적 비목표** — 넣으면 "컷 수 ≥ 슬롯 수" 불변(VF-4)이 깨져 빈 슬롯이 생기는 더 큰 회귀가 된다. A-1/T4가 동작을 정의만 해 둔다 |
| **R-4** | 콤보 항목 타입 변경(`int` → record)이 로드 시 선택 복원을 깨뜨린다 | 설정 진입마다 첫 항목("자동")이 잘못 선택되어 **운영자 설정이 바뀐다** | `SelectedItem`(인스턴스 동일성 의존)이 아니라 `SelectedValue`+`SelectedValuePath`를 쓴다(§6.1). 같은 파일의 표시 모드 콤보가 이 패턴으로 이미 검증됨(VF-12). T22가 저장 왕복을 고정 |
| **R-5** | 게스트가 "자동"을 저장해 운영자 설정을 덮어쓴다 | 운영자 의도와 다른 컷 수 | `CutCount`는 원래부터 게이트 비대상(VF-14) — 게스트가 6↔10을 바꾸는 것과 **동일한 기존 위험**이며 이번 변경이 확대하지 않는다. 게이트 확대는 별도 판단 사항(§12 이연) |
| **R-6** | 자동 상태에서 컷 수가 8이 되어 촬영 시간이 늘어난다(6초×8 = 48초 + 간격) | 회전율 하락 | 기능 취지 자체가 여유 촬영이므로 **수용**. 운영자는 고정 6컷을 선택해 종전 동작으로 되돌릴 수 있다(기본값이 여전히 6이므로 **업그레이드만으로는 아무것도 바뀌지 않는다**) |
| **R-7** | 캡션 추가로 설정 좌열이 22px 길어져 1열 폴백 시 스크롤이 늘어난다 | 미관 | `ScrollViewer`가 이미 있고(`SettingsView.xaml:68`) 카메라 안내가 같은 방식으로 22px를 예약 중(VF-15) — 관례 일치 |

### 이연 항목 (이번 범위 아님, 근거 명시)

| 항목 | 이연 이유 |
|------|-----------|
| `AutoMargin`을 설정 가능하게(여유분 조절) | 요구사항에 없음. 상수로 시작해 피드백 후 판단 |
| 슬롯 수 상한 재검증(로드 시 7개 이상 거부) | R-3 — 종전 동작과 동일하며 별 이슈로 다뤄야 한다(자동 도입과 무관) |
| `MinSlots`/`MaxSlots` 미사용 상수 정리 | VF-3. 이번 변경과 무관한 청소 작업 |
| 웹 클라이언트 이식 | `docs/design/multiplatform-client-architecture.md` 계층에서 별도 판정(설정 영속 자체가 미해결 — `05-cross-platform-client-guide.md:257` WR1) |
| `CutCount` 게스트 편집 게이트 확대 | R-5 — 기존 정책 변경이므로 사용자 판단 필요 |

---

## §13 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1, VF-1~19) / 미검증 가정(§2, A-1~4) 목록이 **분리**되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다 (A-1→Step 1, A-2→Step 5+8, A-3→Step 5+8, A-4→Step 2)
- [x] 8개 Step 전부에 7개 필수 필드가 채워져 있다 (Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식이다 — UI Step(4·5·6)은 non-goal·trigger를 모두 포함
- [x] 검증 명령이 자동 실행 가능한 CLI 형태다 (`dotnet build` / `dotnet test --filter`)
- [x] 브리프의 5대 쟁점 전부 판정 완료 (§0.3 요약표 → §3·§4·§5·§6·§7 본문)
- [x] 변경 대상 파일 목록과 각 파일의 변경 지점 명시 (§9, 17개 항목)
- [x] 테스트 계획 — `SettingsTests` 확장 포함 (§10, 신규 23건)
- [x] 리스크와 근거 명시 (§12, R-1~7 + 이연 6항목)
- [x] 파일 인코딩 보존 지시 명시 (§8, VF-17 — 전 파일 UTF-8 no BOM)
- [x] 신규 리소스 키 0개 → 키 충돌 위험 0 (§8)
- [x] 이벤트 구독 신설 0개 → 누수 위험 0 (§8)

### 구현 순서 요약

```
Step 1 (CutCountPolicy)  ─┬─▶ Step 2 (Clamp 가드)   ─┐
                          ├─▶ Step 3 (Begin 해석)   ─┼─▶ Step 7 (E2E) ─▶ Step 8 (문서+전량검증+수동)
                          └─▶ Step 4 (VM 옵션) ─▶ Step 5 (설정 XAML) ─┘
                              Step 3 ─▶ Step 6 (Guide VM+XAML) ────────┘
```

Step 2·3·4는 Step 1 이후 **병렬 가능**. Step 5는 Step 4 의존, Step 6은 Step 3 의존.

