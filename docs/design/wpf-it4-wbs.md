# MC포토 — 이터레이션 4 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln` (.NET 8 WPF) — 이터레이션 4(편집기 버그 3 + 설정 PC화) |
| 설계 근거 | `docs/design/wpf-it4-design.md`, `docs/prd/iteration-4-editor-and-settings.md`, `docs/design/wpf-it2-design.md`·`wpf-it3-design.md` |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-21 |
| 빌드 검증 기준 | `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it4-design.md`만 읽고 실행할 수 있게 작성했다.
> **모든 Step의 완료 기준은 headless(dotnet build/test·grep 정적확인)로만 판정한다.** UI 육안은 각 Step trigger/non-goal의 "사용자 확인 필요"로 분리, 전체 목록은 `wpf-it4-design.md` §8.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 `dotnet build`/`dotnet test`/`grep`만.
> 색·토큰은 라이트 테마 A(`wpf-it2-design.md` §2), 세션·유휴 정책은 it3(`wpf-it3-design.md` §2)을 따른다.

---

## 검증된 사실 (verified facts)

- **VF-1/2**: 편집기 좌표 변환이 code-behind `GetTransform()`에 있고 `FramePreview.ActualWidth`(Image) 기준. Image·Canvas는 같은 `EditorArea` Grid 셀에 겹침. (근거: `FrameEditorView.xaml.cs:66-82`, `FrameEditorView.xaml:14-22`)
- **VF-4/5**: `GetTransform`이 scale+중앙 오프셋을 계산하나 표시(`ox+slot.X*scale`)는 오프셋 사용, 이동(`dxFrame=(pos-start)/scale` 델타)은 오프셋 미사용 — 비대칭. `(int)` 절삭 누적. (근거: `FrameEditorView.xaml.cs:103-104,129-140`)
- **VF-6**: 클램프는 `SlotLayout.ClampToFrame`(F 좌표 `x=Clamp(x,0,frameW-w)`)로 수식 정확. (근거: `SlotLayout.cs:62-69`)
- **VF-7/8**: 슬롯 리사이즈·종횡비 선택 없음(자동 배치 셀 크기 고정). 캡처는 `Slot.AspectRatio`로 크롭. (근거: `FrameEditorView.xaml.cs:137-140`, `SlotLayout.cs:41-42`, `CaptureViewModel.cs:50`)
- **VF-9**: `SessionStateMachine.IsSessionActive`가 `FrameEditor` 포함 → 편집 중 유휴 발동 → it3 유휴 clearUser:true로 로그아웃+홈. (근거: `SessionStateMachine.cs:42-49`)
- **VF-10**: 유휴 리셋은 `MainWindow.OnAnyUserActivity`(PreviewMouseDown/KeyDown)만 — 드래그 이동·정지는 리셋 안 함. (근거: `MainWindow.xaml.cs`, `IdleWatchdog.cs`)
- **VF-11**: 설정은 it3에서 2열+그룹(U1) 적용됨(`RowLabel` 240·`GroupTitle`·`GroupDivider`, `MaxWidth=720`, Toggle 56·콤보 140). (근거: `SettingsView.xaml`)
- **VF-12**: it3 세션 단일소스·`Reset(clearUser)`(유휴·완료만 true) 반영됨. (근거: `wpf-it3-design.md`)
- **VF-13**: 기존 테스트 자산 — `SlotLayoutTests`·`AppStateTests` 존재. (근거: `tests/MCPhoto.Tests/`)

## 미검증 가정 (open assumptions)

- **OA-1**: B3는 좌표 변환의 기준 컨테이너(Image vs Canvas ActualWidth)·표시/이동 비대칭·레이아웃 타이밍 문제 → 순수 함수 추출+Canvas 기준+절대 위치로 확정 → **검증: Step 1**.
- **OA-2**: 종횡비 선택·리사이즈가 기존 배치·저장·검증과 호환 → **검증: Step 2·3**.
- **OA-3**: 편집기 유휴 제외가 무인 보호를 과도히 약화 안 함(로그인 필수 능동 작업) → **검증: Step 4**.
- **OA-4**: 설정 PC 밀도가 토큰만으로 구성·터치 유지 → **검증: Step 5**.

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (B3 좌표 변환 순수함수+편집기 적용)  ── 편집기 핵심 버그
Step 2 (B4 SlotLayout 종횡비 로직)          ── 순수 로직, Step1 독립
Step 3 (B4 편집기 종횡비 UI/VM)             ← Step 1(편집기), Step 2(로직)
Step 4 (B5 편집기 유휴 제외)                ── 순수 로직(SessionStateMachine), 독립
Step 5 (U6 설정 PC 밀도)                    ── SettingsView, 독립
```

- Step 1(B3)·Step 4(B5)가 P1 핵심. Step 1·2·4·5는 서로 독립(파일 다름), Step 3만 Step 1·2에 의존. Step 1과 Step 3은 같은 `FrameEditorView` 편집이므로 Step 1→3 순서.

---

## Step 1: B3 — 슬롯 좌표 변환 순수 함수 추출 + 편집기 적용 (WYSIWYG)

- **Context Brief**: 프레임 편집기에서 슬롯을 이미지에 맞게 못 놓는다 — 좌측은 이미지 밖으로 새고 우측은 끝까지 못 간다(B3). 원인은 화면(캔버스)↔프레임 원본 좌표 변환이 code-behind에 흩어져 있고 Image `ActualWidth` 의존·표시/이동 좌표 비대칭·레이아웃 타이밍으로 화면 표시가 저장 좌표와 어긋나기 때문(WYSIWYG 파손). 클램프 수식(프레임 좌표)은 정확하다. 좌표 변환을 순수 함수로 추출해 테스트하고, 편집기 표시·드래그·클램프가 동일 변환을 쓰게 한다(설계 §2).
- **대상 파일**: `src/MCPhoto.Core/Frames/EditorTransform.cs`(신규 순수 로직), `src/MCPhoto.App/Views/FrameEditorView.xaml.cs`(변환 사용·절대 위치 드래그), `src/MCPhoto.App/Views/FrameEditorView.xaml`(Image·Canvas 표시 영역 정합), `tests/MCPhoto.Tests/EditorTransformTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `EditorTransform`(순수, UI 비의존): `Compute(double canvasW, canvasH, int frameW, frameH)` → `(double scale, originX, originY, dispW, dispH)`. `scale=Min(canvasW/frameW, canvasH/frameH)`, `dispW=frameW*scale`, `originX=(canvasW-dispW)/2`(중앙 레터박스), originY 동일. `FrameToCanvas(fx,fy)`=`(originX+fx*scale, originY+fy*scale)`, `CanvasToFrame(cx,cy)`=`((cx-originX)/scale, (cy-originY)/scale)`. 크기 0/음수 가드(scale=0 반환).
  - `FrameEditorView.xaml.cs`: `GetTransform`을 `EditorTransform.Compute(SlotCanvas.ActualWidth, SlotCanvas.ActualHeight, FrameWidth, FrameHeight)` 사용으로 교체(Image `ActualWidth` 의존 제거 — 슬롯을 그리는 `SlotCanvas`가 C 좌표계 기준). `RedrawSlots`는 `FrameToCanvas`로 배치.
  - 드래그를 **절대 위치 방식**으로: `OnSlotMouseDown`에서 그랩 오프셋(마우스의 프레임 좌표 − 슬롯 X/Y) 기억. `OnSlotMouseMove`에서 `newF = CanvasToFrame(pos) - grabOffset` → `UpdateSlot`(클램프 F 좌표). 델타 누적·`(int)` 반복 절삭 제거.
  - `FrameEditorView.xaml`: `FramePreview`(Image)와 `SlotCanvas`가 정확히 같은 표시 영역을 차지하도록 정렬 통일(같은 Grid 셀 + 동일 Stretch/정렬). 이미지 D와 캔버스 D가 같은 수식으로 산출됨을 보장.
  - 크기 0 가드 + `SlotCanvas.SizeChanged` 재그리기(기존 `SizeChanged` 구독 유지).
  - 테스트(`EditorTransformTests`): 설계 §2.4의 수치 케이스 — `Compute(800,600,1200,1600)`→scale 0.375·originX 175·originY 0; `FrameToCanvas(0,0)`==(175,0); `FrameToCanvas(1200,1600)`==(625,600); 왕복 `CanvasToFrame(FrameToCanvas(f))≈f`(오차<1px); 좌·우 경계 슬롯이 이미지 좌·우변에 정확히 도달.
- **검증 명령**: `dotnet test --filter EditorTransformTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `FrameEditorView.xaml.cs`가 `EditorTransform` 사용·`FramePreview.ActualWidth` 미사용.
- **완료 기준**:
  - [관측] `EditorTransformTests` 통과(scale·origin·왕복·경계 수치). 빌드 통과. `GetTransform`이 `EditorTransform.Compute(SlotCanvas...)` 사용(grep), 드래그가 절대 위치(`CanvasToFrame`) 방식. 클램프는 여전히 `SlotLayout.ClampToFrame`(F 좌표).
  - [non-goal] 클램프 수식(`SlotLayout`)은 **변경하지 않는다**(이미 정확). 슬롯 크기 조절·종횡비는 이 Step 아님(Step 2·3). 저장 포맷(`Slot` F 좌표) 불변.
  - [trigger] 재그리기는 `SizeChanged`·`Slots` 변경·이미지 로드 시. 드래그 이동은 좌클릭 캡처 중에만.
  - [사용자 확인 필요] 슬롯을 이미지 좌·우·상·하 끝까지 정확히 붙임, 밖으로 안 나감, 화면=저장 위치(design §8-1).
- **롤백**: 이 Step 커밋 revert(`EditorTransform`·View·테스트 원복).
- [ ] 완료

---

## Step 2: B4 — SlotLayout 종횡비 로직 (4:3 / 3:4 / 1:1)

- **Context Brief**: 슬롯이 1:1 고정이라 다른 비율을 못 만든다(B4). 슬롯 종횡비 4:3/3:4/1:1을 선택해 비율 유지 배치할 수 있어야 한다. 캡처 크롭이 `Slot.AspectRatio`를 따르므로(§F1/§F36) 선택 비율이 결과물에 직결. 이 Step은 순수 배치 로직(`SlotLayout`)에 종횡비를 반영한다(설계 §3). UI는 Step 3.
- **대상 파일**: `src/MCPhoto.Core/Frames/SlotLayout.cs`(`AutoArrange` targetAspect 오버로드·비율 유지 셀 맞춤), `src/MCPhoto.Core/Frames/SlotAspect.cs`(신규 enum, 또는 Frames 네임스페이스), `tests/MCPhoto.Tests/SlotLayoutTests.cs`(확장).
- **선행 조건**: 없음(Step 1 독립).
- **구현 내용**:
  - enum `SlotAspect { Ratio4x3, Ratio3x4, Ratio1x1 }` + `double ToRatio()`(4/3, 3/4, 1.0).
  - `SlotLayout.AutoArrange(int slotCount, int frameW, int frameH, double targetAspect)` 오버로드: 격자 셀을 산출한 뒤 **각 셀 안에서 targetAspect를 유지하는 최대 사각형**으로 슬롯 크기 결정(셀 중앙 정렬). `cellAspect=cellW/cellH`와 비교해 폭 or 높이 제한. 기존 무인자 `AutoArrange`는 유지(하위호환, 내부적으로 셀 비율 그대로) 또는 기본 targetAspect.
  - 비율 적용 후 `ClampToFrame`로 경계 보장, 겹침 없음 유지.
  - (선택) 비율 유지 리사이즈 헬퍼: `ResizeKeepingAspect(slot, newWidth, targetAspect)` → 높이=newWidth/aspect.
  - 테스트(`SlotLayoutTests` 확장): `AutoArrange(4, 1200, 1600, 4.0/3)` → 각 슬롯 `Width/Height ≈ 4/3`(±1px), 경계 내, 겹침 없음, `IsValid` true. `1.0`(정사각), `3.0/4`(세로) 동일. 프레임보다 큰 비율 요청 시 축소 유지.
- **검증 명령**: `dotnet test --filter SlotLayoutTests`(종횡비 케이스 통과) + `dotnet build -c Release`(error 0, warning 0).
- **완료 기준**:
  - [관측] `SlotLayoutTests` 통과: 종횡비별 슬롯 `Width/Height` 비율이 목표±1px, 경계 내·겹침 없음·`IsValid` true. 빌드 통과. 무인자 `AutoArrange` 기존 테스트 회귀 없음.
  - [non-goal] 슬롯 개수 범위(1~6)·겹침 검사·`ClampToFrame` 수식은 **변경하지 않는다**. UI(편집기 종횡비 선택)는 이 Step 아님(Step 3).
  - [trigger] 종횡비 적용은 `AutoArrange(targetAspect)` 호출 시. 기본 배치는 무인자 오버로드.
  - [사용자 확인 필요] 없음(순수 로직, Step 3에서 UI 확인).
- **롤백**: 이 Step 커밋 revert(`SlotLayout`·enum·테스트 원복).
- [ ] 완료

---

## Step 3: B4 — 편집기 종횡비 선택 UI/VM

- **Context Brief**: Step 2의 종횡비 로직을 편집기 UI에 연결한다(B4). 사용자가 4:3/3:4/1:1을 선택하면 슬롯이 그 비율로 재배치되고 비율 유지된다(설계 §3.2). 편집기 전역 1개 비율(MVP).
- **대상 파일**: `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(`SlotAspect`·`AspectOptions`·비율 변경 재배치), `src/MCPhoto.App/Views/FrameEditorView.xaml`(종횡비 선택 ComboBox/세그먼트).
- **선행 조건**: Step 1(편집기 좌표), Step 2(종횡비 로직).
- **구현 내용**:
  - `FrameEditorViewModel`: `[ObservableProperty] SlotAspect _slotAspect = Ratio3x4`(기본 세로 3:4, `Example` 프레임 관례) + `AspectOptions`(3종). `OnSlotAspectChanged`·`OnSlotCountChanged`가 `ArrangeSlots()`에서 `SlotLayout.AutoArrange(SlotCount, FrameWidth, FrameHeight, SlotAspect.ToRatio())` 호출.
  - `FrameEditorView.xaml`: 컨트롤 패널에 "슬롯 비율" ComboBox 또는 세그먼트(4:3 / 3:4 / 1:1). 라이트 토큰(it2 Controls ComboBox/Segment 스타일 재사용).
  - 저장 시 `Slot.Width/Height`가 선택 비율 반영 → `CaptureViewModel` 크롭 자동 일치(코드 변경 불필요, VF-8).
  - (선택) 리사이즈 핸들 추가 시 targetAspect 유지 — WBS 선택, MVP는 비율 선택+자동 배치로 충분.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `FrameEditorViewModel`에 `SlotAspect`·`AspectOptions`, `FrameEditorView.xaml`에 비율 선택 컨트롤 + `AutoArrange` 경로.
- **완료 기준**:
  - [관측] 빌드 통과. `FrameEditorViewModel`에 `SlotAspect`/`AspectOptions` 노출 + 변경 시 `ArrangeSlots`가 targetAspect 적용(grep). `FrameEditorView`에 비율 선택 UI. 저장 시 슬롯이 선택 비율 반영.
  - [non-goal] 캡처 크롭 코드(`CaptureViewModel`)는 **변경하지 않는다**(Slot.AspectRatio 자동 반영). 슬롯 개수·좌표 변환(Step 1)은 불변.
  - [trigger] 재배치는 비율/개수 변경 시. 저장은 [저장] 버튼.
  - [사용자 확인 필요] 4:3/3:4/1:1 선택 시 슬롯 모양 변경·비율 유지·캡처 반영(design §8-2).
- **롤백**: 이 Step 커밋 revert(VM·View 원복 → 고정 비율).
- [ ] 완료

---

## Step 4: B5 — 편집기 유휴 타임아웃 제외

- **Context Brief**: 프레임 편집 중 오래 두면 자동 로그아웃되며 홈으로 나간다(B5). 원인은 `FrameEditor`가 유휴 감시 대상이고(VF-9) 드래그가 유휴 리셋에 안 잡혀(VF-10), 유휴 만료 시 it3 정책(유휴=clearUser:true)으로 로그아웃되기 때문. 편집기는 로그인 필수 능동 작업이라 촬영용 유휴 타임아웃(다음 손님 대기 복귀) 대상이 아니다. `IsSessionActive`에서 편집기를 제외한다(설계 §4).
- **대상 파일**: `src/MCPhoto.Core/Navigation/SessionStateMachine.cs`(`IsSessionActive`에서 `FrameEditor` 제거), `tests/MCPhoto.Tests/AppStateTests.cs`(확장).
- **선행 조건**: 없음(순수 로직).
- **구현 내용**:
  - `SessionStateMachine.IsSessionActive`: `FrameEditor`를 목록에서 **제거**(FrameSelect/Guide/Capture/CutSelect/Result/Qr만 유지). 주석에 "편집기는 로그인 필수 능동 작업이라 유휴 제외(it4 §4)".
  - `AppShellViewModel.UpdateIdleWatch`는 변경 불필요(IsSessionActive false면 `_idle.Stop`).
  - 이탈 시 로그인 보존은 it3 `Reset(clearUser:false)`가 이미 보장(유휴 경로 제거로 clearUser:true가 편집기에 안 옴).
  - 테스트(`AppStateTests` 확장): `IsSessionActive(FrameEditor)` == **false**. `IsSessionActive(Capture)`·`(CutSelect)`·`(Result)`·`(Qr)`·`(FrameSelect)`·`(Guide)` == true(회귀 없음). `IsSessionActive(Home)`·`(Settings)`·`(Login)` == false(유지).
- **검증 명령**: `dotnet test --filter AppStateTests`(FrameEditor 제외 + 회귀 케이스) + `dotnet build -c Release`(error 0, warning 0).
- **완료 기준**:
  - [관측] `AppStateTests` 통과: `IsSessionActive(FrameEditor)==false`, 촬영 흐름 상태들은 true 유지. 빌드 통과.
  - [non-goal] 촬영 흐름(FrameSelect~Qr)의 유휴 감시는 **변경하지 않는다**(무인 키오스크 보호 유지). it3 `Reset(clearUser)` 정책·전이표 불변. 유휴 타임아웃 값(75초) 불변.
  - [trigger] 유휴 감시 시작은 `IsSessionActive` true 상태 진입 시만(편집기는 이제 제외). 편집기 이탈은 저장/취소/명시 네비게이션.
  - [사용자 확인 필요] 편집기에서 오래 두어도 홈 복귀·로그아웃 안 됨, 촬영 흐름 유휴는 여전히 동작(design §8-3).
- **롤백**: 이 Step 커밋 revert(`IsSessionActive`에 FrameEditor 복원).
- [ ] 완료

---

## Step 5: U6 — 설정 페이지 PC 친화 밀도

- **Context Brief**: 설정 페이지가 너무 모바일 친화적(큰 터치 타깃·세로 나열)이다(U6). it3의 2열+그룹 레이아웃(VF-11)을 데스크톱 밀도(조밀 행·표준 컨트롤·2열 그리드·카테고리)로 재조정하되 키오스크 터치도 유지하고 라이트 토큰을 유지한다(설계 §5).
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(밀도·2열 그리드·컨트롤 크기).
- **선행 조건**: 없음(SettingsView 독립). VM·바인딩 무변경.
- **구현 내용**:
  - 카드 `MaxWidth` 720→1040(데스크톱 가로 활용). [앱 설정] 카드 내 항목을 **2열 그리드**(`ColumnDefinitions *,*`)로 — 짧은 항목(토글·콤보)은 좌/우 열 분산, 긴 입력(경로·URL)은 1열 전폭.
  - 조밀 행: 행 간 `Space.M`(16)→`Space.S`(8) 근처, 컨트롤 높이 데스크톱 표준(콤보/텍스트 36, 토글 시각 폭 44), **최소 히트 영역 40 유지**(키오스크 터치). 라벨폭 240→200 근처.
  - 카테고리 그룹(촬영/출력·전송/장치·표시/고급)은 유지하되 밀도 조정. (좌측 카테고리 내비는 선택/확장 — MVP는 섹션 유지.)
  - 반응형: 좁은 폭(세로 창)에서 2열→1열 폴백(`DataTrigger`/최소폭 기준, R6).
  - **라이트 토큰만 사용**(색·그림자·라운드 it2 그대로, 밀도만 조정). 하드코딩 색 금지.
  - it3 ComboBox 커스텀 템플릿·Toggle·`BoolToNoticeBrush`(저장 성공/실패 색) 유지.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 하드코딩 색 리터럴(`#`) 0, 2열 그리드(`ColumnDefinition`) 존재, 전 설정 항목 바인딩 유지(CutCount·MirrorMode·OutputFormat·StorageBucket 등).
- **완료 기준**:
  - [관측] 빌드 통과. `SettingsView.xaml`이 2열 그리드 + 조밀 밀도, 색·간격 토큰 참조(grep: `#RRGGBB` 0). 전 설정 항목(§it2 4.2)·계정·관리자 섹션 바인딩 유지(누락 0).
  - [non-goal] 설정 **VM·바인딩·커맨드·저장 로직은 변경하지 않는다**(레이아웃만). 항목 누락 없음. 섹션 조건부 표시(IsLoggedIn/IsPower) 불변. it3 저장 성공/실패 토스트 색 분기 유지.
  - [trigger] 저장은 [저장] 버튼만. 2열→1열 폴백은 좁은 폭에서만.
  - [사용자 확인 필요] PC 밀도·2열·표준 크기가 데스크톱답고 키오스크 터치도 조작 가능, 라이트 톤 유지(design §8-4).
- **롤백**: 이 Step 커밋 revert(`SettingsView.xaml` it3 상태로 복귀).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~13) / 미검증 가정(OA-1~4) 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→1, OA-2→2·3, OA-3→4, OA-4→5)
- [x] 모든 Step(1~5)에 7개 필수 필드
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step은 "사용자 확인 필요" 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`) — **앱 실행 없음**
- [x] B3 좌표 변환을 순수 함수로 추출해 단위 테스트(`EditorTransformTests`)로 headless 검증
- [x] UI 육안은 각 Step "사용자 확인 필요" + `wpf-it4-design.md` §8에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
