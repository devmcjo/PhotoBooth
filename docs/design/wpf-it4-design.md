# MC포토 — 이터레이션 4 설계 (프레임 편집기 버그 + 설정 PC화)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 4 설계 본문 |
| 작성일 | 2026-07-21 |
| 상태 | 초안 v1 (구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-4-editor-and-settings.md` |
| 상위 준거 | `docs/design/wpf-it2-design.md`(라이트 A·토큰·상태머신), `docs/design/wpf-it3-design.md`(세션 단일소스·유휴 정책), PRD v2.7 §9 |
| 구현 WBS | `docs/design/wpf-it4-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\` (it2·it3 구현 반영 완료 상태) |

> 이터레이션 4는 사용자가 it3 빌드 테스트 중 발견한 **프레임 편집기 버그 3건(P1: B3·B4·B5)**과 **설정 페이지 PC화(P2: U6)**를 다룬다. 신규 기능 없음. it2 라이트 디자인 시스템·it3 세션 단일소스/유휴 정책 위에서 진행한다.

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. 편집기 좌표 변환은 code-behind `GetTransform()`에 있다**: `FrameEditorView.xaml.cs:66-82`가 `scale=Min(areaW/frameW, areaH/frameH)`, `offset=Margin + (area-disp)/2`로 프레임→화면 변환을 계산. `areaW/H`는 `FramePreview.ActualWidth/Height`(Image 컨트롤 크기). (근거: `FrameEditorView.xaml.cs`)
- **VF-2. `FramePreview`와 `SlotCanvas`는 같은 `Grid`(`EditorArea`) 셀에 겹쳐 있고 둘 다 Stretch로 셀 전체를 채운다**: XAML `EditorArea` Grid 안에 `Image`(`Stretch=Uniform`, Margin 없음)와 `Canvas`가 형제로 배치. Border `Padding=16`은 Grid(둘 공통)에 적용. (근거: `FrameEditorView.xaml:14-22`)
- **VF-3. `GetTransform`의 `Margin.Left/Top`은 항상 0이다**: XAML에서 `FramePreview`에 `Margin`이 없다(과거 `Margin=16`이 제거됨). code-behind 주석은 "Image margin 16 반영"이라 **주석과 실제가 불일치**하지만 `Margin.Left=0`이라 무해. (근거: `FrameEditorView.xaml:16`, `FrameEditorView.xaml.cs:79-80` 주석)
- **VF-4. `Image`의 `ActualWidth`는 그려진 이미지 픽셀 영역이 아니라 컨트롤 배치 영역(Grid 셀 크기)이다**: `Stretch=Uniform` + 기본 `HorizontalAlignment=Stretch`이면 Image 컨트롤은 셀 전체를 차지하고 내부에서 이미지가 레터박스된다. `GetTransform`은 이 셀 크기(`areaW/H`)로 `scale`과 중앙 오프셋(`(areaW-dispW)/2`)을 계산한다 → **레터박스 여백을 스스로 계산해 offset에 반영**한다. Canvas 원점도 같은 셀 좌상단이므로 이론상 정합. (근거: `FrameEditorView.xaml.cs:73-81`, WPF Image 레이아웃)
- **VF-5. 드래그 이동은 스케일만 쓰고 오프셋은 안 쓴다(델타 방식)**: `OnSlotMouseMove`가 `dxFrame=(pos.X - dragStart.X)/scale`로 **증분**만 계산해 `_origSlotX+dxFrame`에 적용(`FrameEditorView.xaml.cs:129-140`). 표시(`RedrawSlots`)는 `ox+slot.X*scale`로 **오프셋 사용**(`:103-104`). → 이동 계산과 표시 계산이 오프셋 사용에서 비대칭. (근거: `FrameEditorView.xaml.cs:103-104,132-140`)
- **VF-6. 클램프는 프레임 좌표계에서 `ClampToFrame`으로 수행**: `UpdateSlot`이 `SlotLayout.ClampToFrame(slot, frameW, frameH)` 호출 → `x=Clamp(x, 0, frameW-w)`, `y=Clamp(y, 0, frameH-h)`(`SlotLayout.cs:62-69`, `FrameEditorViewModel.cs:104-112`). **클램프 자체는 프레임 원본 좌표 기준으로 정확**하다(경계 수식은 올바름). (근거: `SlotLayout.cs:62-69`)
- **VF-7. 슬롯 크기 조절(리사이즈) UI가 없다**: `OnSlotMouseMove`는 위치(X/Y)만 바꾸고 `slot.Width/Height`는 그대로 넘긴다. **리사이즈 핸들·크기 변경 경로가 편집기에 없다**(자동 배치 크기 고정). 종횡비 선택도 없음. (근거: `FrameEditorView.xaml.cs:137-140`, `FrameEditorViewModel.cs` — 크기 조절 커맨드 부재)
- **VF-8. 슬롯 종횡비는 자동 배치 셀 크기로 고정**: `SlotLayout.AutoArrange`가 `cellW/cellH`를 프레임 크기·격자로 산출(`SlotLayout.cs:41-42`). 사용자가 비율을 못 고른다. 캡처는 `frame.Slots[0].AspectRatio`로 크롭(`CaptureViewModel.cs:50`) → 슬롯 종횡비가 캡처 크롭을 결정하므로 4:3/3:4/1:1 선택이 §F1과 직결. (근거: `SlotLayout.cs`, `CaptureViewModel.cs:50`)
- **VF-9. `FrameEditor`는 유휴 감시 대상이다**: `SessionStateMachine.IsSessionActive`가 `FrameEditor`를 포함(`SessionStateMachine.cs:42-49`). `AppShellViewModel.UpdateIdleWatch`가 세션 활성 시 `_idle.Start(IdleTimeoutSeconds=75)`. 유휴 만료 → `OnIdleTimeout` → `ReturnHome`(it3에서 유휴는 clearUser:true) → **홈 복귀 + 로그아웃**. 이것이 B5. (근거: `SessionStateMachine.cs:42-49`, `AppShellViewModel.cs`(it3), it3 §2.3)
- **VF-10. 유휴 리셋은 `MainWindow.OnAnyUserActivity`(PreviewMouseDown/KeyDown)가 담당**: 창 전체 프리뷰 이벤트가 `_shell.NotifyUserActivity()`→`_idle.Reset()`(`MainWindow.xaml.cs`). **마우스 다운/키다운은 리셋하지만, 드래그 중 마우스 이동(버튼 유지)·정지 상태는 리셋 안 됨** → 사용자가 슬롯 배치를 고민하며 75초간 클릭·키입력이 없으면 유휴 발동. (근거: `MainWindow.xaml.cs`, `IdleWatchdog.cs`)
- **VF-11. 설정 화면은 it3에서 이미 2열+그룹(U1) 적용됨**: `SettingsView.xaml`이 `RowLabel`(고정폭 240)·`GroupTitle`·`GroupDivider` 스타일로 촬영/출력·전송/장치·표시/고급 4그룹, `MaxWidth=720` 중앙 스택, 큰 터치 컨트롤(Toggle 56·ComboBox 140). → U6은 이걸 **PC 밀도로 재조정**하는 것. (근거: `SettingsView.xaml`)
- **VF-12. it3 세션 단일소스·Reset(clearUser) 정책 반영됨**: `AppShellViewModel`이 세션 계정 단일소스, 유휴·완료만 clearUser:true, 화면이동/오버레이는 로그인 보존. (근거: it3 §2, `wpf-it3-design.md`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. B3의 좌표 어긋남은 (a)Image `ActualWidth`가 첫 레이아웃/이미지 로드 타이밍에 확정 안 돼 잘못된 scale/offset을 쓰거나, (b)델타 이동과 오프셋 표시의 비대칭, (c)Canvas와 Image의 실제 배치 불일치 중 하나다** → 좌표 변환을 **순수 함수로 추출 + Image 대신 명시적 이미지 표시 영역 계산**으로 확정. 검증: **WBS Step 1**(변환 단위 테스트 + 사용자 육안).
- **OA-2. 슬롯 리사이즈·종횡비 선택을 추가해도 기존 자동 배치·저장·검증과 호환된다** → 검증: **WBS Step 2·3**(SlotLayout 테스트 + 빌드).
- **OA-3. 편집기를 유휴 예외 처리해도 무인 키오스크 보호가 과도하게 약화되지 않는다**(편집기는 로그인 필수라 무인 노출 위험 낮음) → 검증: **WBS Step 4**(상태머신/정책 테스트).
- **OA-4. 설정 PC 레이아웃(2열 그리드·조밀 행)이 라이트 토큰만으로 구성되고 키오스크 터치도 유지된다** → 검증: **WBS Step 5**(빌드 + 사용자 육안).

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 근본 원인(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **B3** 슬롯 좌표/경계 | 좌표 변환이 code-behind에 흩어짐 + Image `ActualWidth` 의존·표시/이동 비대칭(VF-1·4·5) | 좌표 변환을 **순수 함수(`EditorTransform`)로 추출**, 이미지 표시 영역을 명시 계산, 표시·이동·클램프 좌표계 통일 | §2, Step 1 |
| **B4** 종횡비 선택 | 리사이즈·비율 선택 없음, 셀 크기 고정(VF-7·8) | 종횡비(4:3/3:4/1:1) 선택 + 비율 유지 리사이즈, 슬롯 단위 적용 | §3, Step 2·3 |
| **B5** 편집 중 유휴 로그아웃 | `FrameEditor`가 유휴 대상 + 드래그가 리셋 안 됨(VF-9·10) | 편집기 유휴 정책 변경(제외 또는 활동 리셋 + 이탈 시 로그인 보존) | §4, Step 4 |
| **U6** 설정 PC화 | 모바일풍 큰 타깃·세로 나열(VF-11) | PC 밀도(조밀 행·표준 컨트롤·2열 그리드·카테고리) + 터치 유지 | §5, Step 5 |

---

## 2. B3 — 슬롯 좌표/경계 버그 (근본 원인 + 수정 설계)

### 2.1 좌표계 정의 (3개)

편집기에는 세 좌표계가 있다:
- **프레임 좌표(F)**: 프레임 원본 픽셀. 슬롯의 `X/Y/Width/Height`와 저장·클램프·캡처 크롭 기준(`Slot`은 F 좌표, VF-6). **진실의 좌표**.
- **캔버스 좌표(C)**: `SlotCanvas` 내부 좌표(Canvas.Left/Top). 슬롯 사각형을 그리는 좌표.
- **이미지 표시 영역(D)**: 화면에서 이미지가 실제로 그려지는 사각형(레터박스 여백 제외). C 좌표 안의 부분집합.

WYSIWYG는 **F ↔ C 변환이 D 기준으로 정확**해야 성립한다. 변환:
```
C.x = D.originX + F.x * scale
C.y = D.originY + F.y * scale
scale = min(D.width / frameW, D.height / frameH)   // Uniform
D.width  = frameW * scale,  D.height = frameH * scale
D.originX = (canvasW - D.width) / 2                 // 중앙 정렬 레터박스
D.originY = (canvasH - D.height) / 2
```
역변환(C→F): `F.x = (C.x - D.originX) / scale`.

### 2.2 근본 원인 (코드 근거)

현재 `GetTransform`(`FrameEditorView.xaml.cs:66-82`)은 위 수식을 **대체로 맞게** 구현했다(scale·중앙 오프셋 계산 존재). 그런데 다음 취약점들이 B3 증상("좌측 오버플로우, 우측 도달 불가")을 만든다:

1. **`areaW/H`를 `FramePreview.ActualWidth`로 잡는데, 이는 `SlotCanvas`의 크기와 다를 수 있다(VF-4).** `Image`와 `Canvas`가 같은 Grid 셀에 있어도 **각자의 `ActualWidth`는 자기 배치 결과**다. Image가 `Stretch=Uniform`이면 컨트롤은 셀을 채우나(Stretch 기본), Canvas도 셀을 채운다 — 보통 같지만, 레이아웃 타이밍(이미지 로드 직후 `ActualWidth`가 0 또는 미확정)일 때 `RedrawSlots`가 잘못된 스케일로 그린다(`OnLoadImage`가 `await Task.Yield()` 후 즉시 `RedrawSlots` 호출, `:59-61`). **Image의 `ActualWidth` 대신 `SlotCanvas`(슬롯을 실제로 그리는 컨테이너)의 `ActualWidth`를 기준**으로 D를 계산해야 C 좌표계와 정합한다.
2. **표시는 offset을 쓰고(`ox+slot.X*scale`, `:103`) 이동은 offset을 안 쓴다(델타, `:132-140`)** — 이동 자체는 델타라 offset 무관해 맞지만, **드래그 시작점 기준이 어긋나면**(예: 클램프로 좌표가 보정된 뒤 `_origSlotX`가 예전 값) 누적 오차가 생긴다. 또 `(int)` 캐스팅이 매 이동마다 절삭돼 좌측(음수 방향)으로 갈수록 오차가 누적될 수 있다.
3. **좌측 오버플로우**: 클램프는 `x>=0`을 보장하나(VF-6), **표시 좌표가 D.origin을 안 더하거나 잘못 더하면** 화면상 슬롯이 이미지 왼쪽 여백(레터박스)으로 삐져나와 보인다. 즉 F 좌표는 0 이상이어도 C 표시가 어긋나면 "이미지 밖"으로 보인다 → WYSIWYG 위반.
4. **우측 도달 불가**: `scale`이 실제보다 크게 잡히면(areaW가 실제 D보다 크면) 같은 F.x라도 화면에서 더 오른쪽으로 그려져, 사용자가 우측 끝에 놓기 전에 화면 경계(또는 클램프)에 막힌 것처럼 느낀다. 또는 클램프 상한 `frameW-w`가 정상이어도 **표시 스케일이 어긋나 시각적으로 우측 여백이 남는다**.

**결론**: 클램프 수식(F 좌표)은 올바르나, **F↔C 변환(특히 D 계산의 기준 컨테이너와 레이아웃 타이밍)이 부정확**해 화면 표시가 저장 좌표와 어긋나는 것이 B3의 핵심(WYSIWYG 파손). 이동 델타의 정수 절삭·클램프 후 origin 미갱신이 이를 악화.

### 2.3 수정 설계

**(1) 좌표 변환을 순수 함수로 추출 (테스트 가능)**
- `src/MCPhoto.Core/Frames/EditorTransform.cs`(신규, 순수 로직): 입력=`(canvasW, canvasH, frameW, frameH)`, 출력=`scale, originX, originY, dispW, dispH`. 그리고 `FrameToCanvas(fx,fy)`·`CanvasToFrame(cx,cy)`·`ClampSlotToDisplay` 헬퍼. **UI 비의존**이라 단위 테스트 가능.
- code-behind `GetTransform`은 이 순수 함수를 **`SlotCanvas.ActualWidth/Height`를 인자로** 호출하도록 변경(Image `ActualWidth` 의존 제거). 슬롯은 `SlotCanvas`에 그려지므로 캔버스 크기가 C 좌표계의 진실.
- **Image 표시 영역 = Canvas 표시 영역 일치 보장**: `FramePreview`와 `SlotCanvas`가 정확히 같은 사각형을 차지하도록 XAML에서 둘을 **동일 크기로 강제**(같은 Grid 셀 + 둘 다 `Stretch`/명시 정렬). 또는 이미지를 Canvas 배경(`ImageBrush`)이 아니라 별도 Image로 두되 **둘의 `ActualWidth`가 같음을 레이아웃으로 보장**(같은 셀·같은 정렬). 권장: **Canvas를 기준 컨테이너로 삼고, 이미지 D를 Canvas 크기로부터 계산**(Image의 실제 렌더 영역과 Canvas의 D가 같은 수식으로 산출되므로 정합).

**(2) 이동·클램프 좌표계 통일**
- 드래그 이동도 **절대 위치 방식으로 전환**: `CanvasToFrame(pos)`로 마우스의 프레임 좌표를 구하고, 드래그 시작 시 "슬롯 내 클릭 오프셋(그랩 포인트)"을 기억해 `newF = CanvasToFrame(pos) - grabOffsetF`. 델타 누적·정수 절삭 오차 제거. 최종에 `ClampToFrame`(F 좌표) 1회.
- 클램프 후 슬롯 좌표가 바뀌면 `_origSlot`류 상태를 갱신(다음 이동의 기준 일치). 절대 위치 방식이면 `_orig` 불필요(매 이동이 절대 계산).
- 표시(`RedrawSlots`)와 이동(`OnSlotMouseMove`)이 **동일한 `EditorTransform`을 사용** → 표시=저장 좌표 일치(WYSIWYG).

**(3) 레이아웃 타이밍**
- `SlotCanvas.SizeChanged`에서 `RedrawSlots`(이미 `SizeChanged` 구독 있음, `:28`). 이미지 로드 직후 `ActualWidth`가 0이면 다음 레이아웃 패스에서 재그리기(`SizeChanged`가 처리). `OnLoadImage`의 즉시 `RedrawSlots`는 유지하되 크기 0 가드.

### 2.4 검증 포인트 (headless)

- 단위(`EditorTransformTests` 신규): 
  - `Compute(canvasW=800, canvasH=600, frameW=1200, frameH=1600)` → `scale=600/1600=0.375`, `dispW=450`, `originX=(800-450)/2=175`, `originY=0`.
  - `FrameToCanvas(0,0)` == `(175, 0)`, `FrameToCanvas(1200,1600)` == `(625, 600)`(우하단 = 이미지 우하단).
  - `CanvasToFrame(175,0)` == `(0,0)`(왕복), 라운드트립 오차 < 1px.
  - 좌측 경계: `F.x=0` 슬롯이 `C.x=originX`(이미지 좌변)에 정확히 붙음(레터박스 여백으로 안 새어나감).
  - 우측 경계: `F.x=frameW-slotW` 슬롯의 우변이 `originX+dispW`(이미지 우변)에 정확히 도달.
- 사용자 확인(육안): 슬롯을 좌우상하 끝까지 이미지 경계에 정확히 붙일 수 있고 밖으로 안 나감. 화면 위치=저장 위치.

---

## 3. B4 — 슬롯 종횡비 선택 (4:3 / 3:4 / 1:1)

### 3.1 현재·목표

현재 슬롯 크기는 자동 배치 셀 크기로 고정, 리사이즈·비율 선택 없음(VF-7·8). 목표: **4:3 / 3:4 / 1:1** 종횡비 선택 + 비율 유지. 캡처 크롭이 `Slot.AspectRatio`를 따르므로(VF-8, §F1/§F36) 선택 비율이 결과물에 직결.

### 3.2 설계

- **편집기 옵션으로 종횡비 선택**(슬롯 단위보다 편집기 전역이 단순·일관): `FrameEditorViewModel`에 `SlotAspect`(enum `SlotAspect { Ratio4x3, Ratio3x4, Ratio1x1 }`) + `AspectOptions`. ComboBox/세그먼트로 선택.
  - 슬롯 단위 비율도 요구가 허용("슬롯별 또는 편집기 옵션")하나, MVP 단순성·캡처 크롭 일관성을 위해 **편집기 전역 1개 비율**을 기본 채택. 슬롯별은 미검증 가정으로 확장 여지만 둔다.
- **자동 배치에 비율 반영**: `SlotLayout.AutoArrange`에 `targetAspect` 인자 추가(선택). 셀을 격자로 나눈 뒤 **각 셀 안에서 targetAspect를 유지하는 최대 사각형**으로 슬롯 크기 산출(셀 중앙 정렬). 기존 시그니처는 유지하고 오버로드 또는 옵션 파라미터.
- **비율 유지 리사이즈**: 종횡비 변경 시 각 슬롯의 현재 크기를 비율에 맞게 재계산(폭 기준 또는 셀 맞춤). 드래그 이동은 위치만(§2), 크기는 종횡비+셀맞춤으로 결정 → **리사이즈 핸들 없이도 비율 선택으로 크기 결정** 가능(MVP). 
  - 선택 확장: 모서리 리사이즈 핸들 추가 시에도 `targetAspect` 유지(폭 변경→높이=폭/aspect). WBS에서 선택 항목.
- **경계 보장**: 비율 적용 후 슬롯이 프레임을 넘으면 `ClampToFrame` + 비율 유지 축소.
- **캡처 일관성**: 저장된 `Slot.Width/Height`가 선택 비율을 반영하므로 `CaptureViewModel`의 `frame.Slots[0].AspectRatio` 크롭이 자동으로 일치(코드 변경 불필요, VF-8).

### 3.3 검증 포인트 (headless)

- 단위(`SlotLayoutTests` 확장): `AutoArrange(4, W, H, Ratio4x3)` → 각 슬롯 `Width/Height ≈ 4/3`(±1px), 경계 내, 겹침 없음. `Ratio1x1` → 정사각. `Ratio3x4` → 세로. 비율 변경 후 `IsValid` true.
- 사용자 확인(육안): 4:3/3:4/1:1 선택 시 슬롯 모양이 바뀌고 비율 유지.

---

## 4. B5 — 편집 중 유휴 로그아웃/이탈 (정책 설계)

### 4.1 근본 원인

`FrameEditor`가 유휴 감시 대상(`IsSessionActive` 포함, VF-9)이고, 유휴 리셋은 마우스 다운/키다운만 반영(VF-10)한다. 슬롯 배치를 고민하며 75초간 클릭·키입력이 없으면 유휴 만료 → `ReturnHome`(it3에서 유휴는 `clearUser:true`) → **홈 복귀 + 로그아웃**. 편집기는 로그인 필수 화면인데 무인 키오스크 정책(다음 손님 위해 로그아웃)이 부적절하게 적용된다.

### 4.2 설계 (무인 보호와 편집 지속의 균형)

세 가지 조치를 조합한다:

1. **편집기를 유휴 감시에서 제외**: `SessionStateMachine.IsSessionActive`에서 `FrameEditor` **제거**. 편집기는 촬영 세션(무인 키오스크 대기 흐름)이 아니라 **로그인 사용자의 능동 작업**이므로 촬영용 유휴 타임아웃(대기화면 복귀) 대상이 아니다. → 편집 중 유휴로 인한 홈 복귀·로그아웃 원천 차단.
   - 근거: 유휴 타임아웃의 목적은 "손님이 촬영 중 이탈 시 다음 손님 위해 리셋"(PRD §10). 편집기는 관리/커스텀 작업이라 이 목적에 안 맞음.
2. **편집기 이탈 시 로그인 보존 보장**: 만약 다른 경로(예외 등)로 편집기를 벗어나도 `Reset(clearUser:false)`(it3 정책)라 로그인 유지. 유휴 제외(1)로 유휴 경로는 사라지고, 예외 복구는 it3대로 로그인 보존.
3. **(선택) 편집기 활동 리셋 보강**: 만약 무인 보호를 완전히 포기하기 부담되면, 편집기에 **더 긴 전용 타임아웃**(예: 5분)을 두고 드래그 이동(`MouseMove`)도 리셋에 포함. 단 이 경우에도 만료 시 **로그아웃 없이** 홈 복귀만(clearUser:false). **기본 설계는 (1)+(2)**(편집기 유휴 제외)로 단순화하고, (3)은 미검증 가정·확장으로 둔다.

**채택**: (1) 편집기 유휴 제외 + (2) 이탈 시 로그인 보존. 무인 노출 위험은 편집기가 로그인 필수·능동 작업이라 낮고, 필요 시 관리자가 명시적으로 나가거나 앱 종료.

### 4.3 상태머신·정책 영향

- `IsSessionActive`: `FrameEditor` 제거(나머지 촬영 흐름 상태는 유지). 유휴 감시는 FrameSelect~Qr에만.
- `AppShellViewModel.UpdateIdleWatch`는 변경 불필요(IsSessionActive가 false면 `_idle.Stop()`).
- it3의 `Reset(clearUser)` 정책 불변: 유휴·완료만 clearUser:true. 편집기는 유휴 대상이 아니게 되므로 유휴로 인한 clearUser:true가 편집기엔 적용 안 됨.

### 4.4 검증 포인트 (headless)

- 단위(`AppStateTests` 확장): `IsSessionActive(FrameEditor)` == **false**(변경 확인). `IsSessionActive(Capture/CutSelect/Result)` == true(회귀 없음).
- 사용자 확인(육안): 편집기에서 오래 두어도 홈 복귀·로그아웃 안 됨. 저장/취소로만 나감.

---

## 5. U6 — 설정 페이지 PC 친화화 (레이아웃 스펙)

### 5.1 현재·목표

it3에서 설정은 `MaxWidth=720` 중앙 스택 + 큰 터치 컨트롤(Toggle 56·행 간 14·라벨폭 240)로 "모바일풍"이다(VF-11). U6은 이를 **데스크톱 밀도**로 재조정하되 키오스크 터치도 유지, 라이트 토큰 유지.

### 5.2 PC 레이아웃 스펙

- **넓은 2열 그리드**: 카드를 화면 폭에 맞춰 넓히고(`MaxWidth` 720→1040), 한 카드 안에서 **항목을 2열로 배치**(좌열·우열에 각 라벨+컨트롤). 데스크톱은 가로 공간이 넓어 세로 스크롤을 줄인다. 짧은 항목(토글·콤보)은 2열, 긴 입력(경로·URL)은 1열 전폭.
- **조밀한 행**: 행 높이·간격 축소 — 행 간 `Space.M`(16)→`Space.S`(8), 컨트롤 표준 크기(콤보 높이 `Touch.Min` 48→데스크톱 표준 32~36, 단 **키오스크 터치 유지 위해 최소 히트 영역은 확보**: 컨트롤 시각 높이는 낮추되 클릭 타깃은 패딩으로 보완, 또는 `displayMode`/입력수단 무관하게 40 근처 절충).
  - 절충값: 행 높이 40, 콤보/텍스트 높이 36, 토글 시각 폭 44(현 56→축소). 마우스·터치 모두 무난한 중간 밀도.
- **카테고리 내비게이션(선택)**: 좌측에 카테고리 목록(촬영/출력·전송/장치·표시/고급/계정/관리자) + 우측에 해당 섹션. `ListBox`(카테고리) + `ContentControl`(섹션 스왑) 또는 앵커 스크롤. **MVP는 섹션 그룹 유지 + 2열 밀도**로 하고, 좌측 내비는 확장(미검증 가정)으로 둔다.
- **정렬**: 2열 그리드는 `Grid` `ColumnDefinitions`(`*`,`*`) + 각 열 내부 라벨-컨트롤. 라벨은 좌측, 컨트롤은 라벨 우측 또는 아래(밀도에 따라). 열 간 간격 `Space.L`.
- **컨트롤**: it3 ComboBox 커스텀 템플릿(라이트) 유지, 크기만 데스크톱 표준으로. Toggle은 유지하되 폭 축소. TextBox 높이 36.
- **라이트 토큰 유지**: 색·그림자·라운드는 it2 토큰 그대로. 밀도(간격·크기)만 조정.
- **터치 유지**: 키오스크에서도 조작 가능하도록 최소 히트 영역 40 이상, 항목 간 충분한 간격. displayMode(fullscreen=키오스크)일 때 밀도를 약간 키우는 반응형은 선택.

### 5.3 계정·관리자 섹션

- [계정]·[관리자] 섹션도 동일 PC 밀도 적용(입력 필드 폭·행 간격 조정). 관리자 계정 생성 폼은 2열(아이디/비번 나란히) 가능.

### 5.4 검증 포인트 (headless)

- 빌드 통과 + `grep`로 하드코딩 색 리터럴 0(토큰만), 2열 그리드(`ColumnDefinitions`) 존재, 전 설정 항목 바인딩 유지(누락 0).
- 사용자 확인(육안): PC에서 밀도·정렬이 데스크톱답고, 키오스크 터치도 조작 가능.

---

## 6. 파일 변경 요약

| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.Core/Frames/EditorTransform.cs`(신규) | F↔C 좌표 변환 순수 함수(scale·origin·왕복·클램프) | B3 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml.cs` | `GetTransform`→`EditorTransform` 사용(Canvas 기준), 절대 위치 드래그, 크기 0 가드 | B3 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | Image·Canvas 표시 영역 정합 보장(정렬), (B4)종횡비 선택 UI, (B4)리사이즈 핸들(선택) | B3·B4 |
| `src/MCPhoto.Core/Frames/SlotLayout.cs` | `AutoArrange` targetAspect 오버로드, 비율 유지 셀 맞춤 | B4 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | `SlotAspect`·`AspectOptions`·비율 변경 재배치, 리사이즈 반영 | B4 |
| `src/MCPhoto.Core/Navigation/SessionStateMachine.cs` | `IsSessionActive`에서 `FrameEditor` 제거 | B5 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | PC 밀도 2열 그리드·조밀 행·표준 컨트롤 크기(토큰 유지) | U6 |
| `tests/MCPhoto.Tests/` | `EditorTransformTests`(신규), `SlotLayoutTests`(종횡비), `AppStateTests`(FrameEditor 유휴 제외) | B3·B4·B5 |

---

## 7. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | 좌표 변환 리팩터가 기존 표시 회귀 | 슬롯 위치 어긋남 | 순수 함수 + 왕복 단위 테스트로 수식 고정, 표시·이동 동일 함수 사용 | Step 1 테스트 |
| R2 | Canvas `ActualWidth` 레이아웃 타이밍(0) | 첫 렌더 오배치 | 크기 0 가드 + `SizeChanged` 재그리기 | Step 1 육안 |
| R3 | 종횡비 리사이즈가 겹침·경계 위반 유발 | 저장 불가 | 비율 유지 축소 + `ClampToFrame` + `IsValid` 게이트 | Step 2·3 테스트 |
| R4 | 편집기 유휴 제외로 무인 노출(관리자 자리 비움) | 보안 약화 | 편집기는 로그인 필수·능동 작업이라 위험 낮음. 필요 시 (3)긴 타임아웃+로그인보존 확장 | Step 4 |
| R5 | U6 조밀화로 키오스크 터치 타깃 미달 | 터치 오조작 | 최소 히트 영역 40 유지, 절충 밀도 | Step 5 육안 |
| R6 | 2열 그리드가 세로 창(키오스크 세로)에서 깨짐 | 레이아웃 붕괴 | 좁은 폭에서 1열 폴백(반응형 트리거) | Step 5 육안 |

---

## 8. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS 완료 기준은 전부 headless(build/test/grep). 아래는 구현 후 사용자 육안 확인(각 Step trigger/non-goal로 분리).

1. **B3**: 슬롯을 이미지 좌·우·상·하 끝까지 경계에 정확히 붙일 수 있고 밖으로 안 나감. 화면 표시 위치 = 저장 위치(WYSIWYG). 레터박스 여백으로 안 삐져나감.
2. **B4**: 4:3 / 3:4 / 1:1 선택 시 슬롯 모양이 바뀌고 비율 유지. 캡처 결과가 선택 비율 반영.
3. **B5**: 편집기에서 오래 두어도 홈 복귀·로그아웃 안 됨. 저장/취소로만 이탈. 촬영 흐름의 유휴 타임아웃은 여전히 동작(회귀 없음).
4. **U6**: 설정이 PC에서 데스크톱 밀도(조밀·2열·표준 크기)로 자연스럽고, 키오스크 터치도 조작 가능. 라이트 톤 유지.

## 부록. 참고

- it2 토큰·라이트 팔레트: `docs/design/wpf-it2-design.md` §2
- it3 세션 단일소스·유휴 Reset(clearUser) 정책: `docs/design/wpf-it3-design.md` §2
- 슬롯·캡처 크롭 관계: `SlotLayout.cs`, `CaptureViewModel.cs:50`, PRD §F1/§F36
