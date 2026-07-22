# MC포토 — 이터레이션 7 설계 (프레임 슬롯 버그 + QR 전송 세분화)

| 항목 | 값 |
|------|-----|
| 문서 | WPF+웹 이터레이션 7 설계 본문 |
| 작성일 | 2026-07-21 |
| 상태 | 초안 v1 (구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-7-frame-qr-granularity.md` |
| 계약 | `docs/design/firebase-contract.md`(ResultSession 필드 — 본 문서에 갱신안) |
| 상위 준거 | it2~it6, PRD v2.7 §9 |
| 구현 WBS | `docs/design/wpf-it7-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\`(WPF), `E:\Study\photobooth\web\`(웹). it2~it6 반영, Firebase 실배포 완료 |

> 이터레이션 7은 WPF+웹 교차. **P1 버그(B9 프레임 슬롯 개수 저장 안 됨)**, **P2 기능(F2 QR 전송 세분화 — 사진/타임랩스 개별 토글, F3 웹 미디어 부재 안내)**. 관리자 인증 전환은 별도 이터레이션(비범위).

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. 슬롯 직렬화·Save는 정상**: `FrameEditorViewModel.Save`가 `Slots.ToList()`로 저장(`FrameEditorViewModel.cs:137`), DTO 매핑 정상. 저장된 슬롯 수 = `Slots.Count`. → **B9는 저장 시점의 `Slots`가 이미 1개**라는 뜻(저장 로직 무결). (근거: `FrameEditorViewModel.cs`, 오케스트레이터 Firestore 실측)
- **VF-2. `SlotCount`는 기본 4, 변경 시 `ArrangeSlots` 재배치**: `[ObservableProperty] int _slotCount = 4`, `partial void OnSlotCountChanged(int) => ArrangeSlots()`. `ArrangeSlots`가 `SlotLayout.AutoArrange(SlotCount, ...)`로 `Slots`를 그 개수만큼 채움(`FrameEditorViewModel.cs:29,92-101,108`). → **`SlotCount`가 1로 바뀌면 `Slots`도 1개**가 된다. (근거: `FrameEditorViewModel.cs`)
- **VF-3. ComboBox가 `SelectedIndex` TwoWay + 인라인 아이템 6개**: `FrameEditorView.xaml`의 `<ComboBox SelectedIndex="{Binding SlotCount, Converter={StaticResource SlotCountIndex}}"><ComboBoxItem …>×6`. `ItemsSource`가 아니라 정적 `ComboBoxItem`. 바인딩 기본 TwoWay(`SelectedIndex`는 양방향 기본). (근거: `FrameEditorView.xaml:36-44`)
- **VF-4. `SlotCountIndexConverter`는 정상**: `Convert(count)=Clamp(count-1,0,5)`, `ConvertBack(index)=index+1`(`CommonConverters.cs:50-57`). count=4→index 3, index 0→count 1. 컨버터 자체 무결. (근거: `CommonConverters.cs`)
- **VF-5. it3에서 ComboBox에 커스텀 `ControlTemplate`이 들어갔다**: `Controls.xaml`의 암묵 `ComboBox` 스타일에 `ControlTemplate`(`ToggleButton`+`SelectionBoxItem` ContentPresenter+`Popup`+`ItemsPresenter`, `Controls.xaml:377-`). `SelectionBoxItem`을 `RelativeSource AncestorType=ComboBox`로 바인딩. → **커스텀 템플릿 하에서 `SelectedIndex` 초기화 타이밍이 기본 템플릿과 달라질 수 있다**(아이템 컨테이너 생성·selection 확정 순서). (근거: `Controls.xaml:348-440`)
- **VF-6. 드래그는 it4 절대 위치 방식으로 동작(EditorTransform)**: `FrameEditorView.xaml.cs`가 `EditorTransform`으로 F↔C 변환, 슬롯 rect 드래그. 슬롯이 1개뿐이면 드래그할 게 1개라 "슬롯 지정을 할 수 없다"는 체감. (근거: `FrameEditorView.xaml.cs`, `EditorTransform.cs`)
- **VF-7. `ResultSession.FinalImageUrl`은 non-null(string, 기본 ""), `TimelapseUrl`은 `string?`**: (`ResultSession.cs:12,15`). F2에서 사진도 꺼질 수 있으므로 `FinalImageUrl`도 nullable 필요. (근거: `ResultSession.cs`)
- **VF-8. `UploadService.UploadResultAsync`는 최종 이미지 항상 업로드, 타임랩스는 있을 때만**: `finalUrl`은 무조건 업로드(`UploadService.cs:39-42`), 타임랩스는 `timelapsePath` 존재 시만(`:45-51`). → F2는 **사진/타임랩스 각각 "전송 여부" 플래그**로 분기하도록 확장 필요. (근거: `UploadService.cs`)
- **VF-9. `ResultViewModel.Next`가 QR on 시 업로드, 로컬 저장은 QR 이전 수행**(it5): (`ResultViewModel.cs:104-129`). F2 하위 토글은 이 QR 경로 안에서 미디어 선택에 반영. (근거: `ResultViewModel.cs`)
- **VF-10. 웹 `renderSuccess`는 만료 판정 통과 후 호출**: `loadSession`이 문서 부재·expiresAt 만료를 먼저 걸러내고(`app.js` `showState("expired")`), 그 뒤 `renderSuccess(data)`(`app.js:196`). → **`renderSuccess` 안에서 URL null이면 이미 "미만료 doc"이므로 '옵션 꺼짐'으로 안전 해석 가능**. (근거: `web/public/app.js`)
- **VF-11. 웹은 URL null을 "실패"로 처리하고 만료 폴백에 포함**: `finalImageUrl` 없으면 photoError 표시 + `maybeFallbackToExpired`에서 photoFailed, `timelapseUrl` 없으면 영역 숨김 + videoFailed. 둘 다 실패면 만료 화면 폴백(`app.js:100-118,134-137,150-153`). → **F3은 "옵션 꺼짐"을 "로드 실패"와 구분**하고 폴백 계산에서 제외해야 한다. (근거: `web/public/app.js`)
- **VF-12. 웹은 읽기 전용 단건 조회(getDoc)만**: 컬렉션 쿼리·User·frameTemplates 접근 금지(계약 §0·§5). F3은 기존 `data`(ResultSession 문서) 필드만으로 판단. (근거: `web/public/app.js` 주석, `firebase-contract.md`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. B9는 커스텀 ComboBox 템플릿 하에서 `SelectedIndex` TwoWay가 초기화 시 0으로 흔들려 `SlotCount`를 1로 clobber하는 것이다** → `SelectedValue`/`SelectedItem`(값 기반) 바인딩 전환 + 초기화 순서 보정으로 근본 차단 → **검증: WBS Step 1**(개수 반영·저장 라운드트립 단위/STA 테스트 + 사용자 육안).
- **OA-2. QR 하위 토글(SendPhoto/SendTimelapse)을 INI에 추가·연동해도 기존 EnableQrDelivery 흐름과 정합한다** → **검증: Step 2**(SettingsTests 라운드트립·연동 규칙).
- **OA-3. `FinalImageUrl` nullable 전환 + 미디어 선택 업로드가 기존 업로드/계약 소비(웹)와 호환된다** → **검증: Step 3**(UploadContractTests).
- **OA-4. 웹이 "doc 존재+미만료+URL null=옵션 꺼짐" 추론으로 만료/실패와 구분 가능하다**(추가 플래그 없이) → **검증: Step 4**(Emulator/정적 로직 확인 + 사용자 육안).

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 근본 원인/현황(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **B9** 슬롯 개수 저장 안 됨 | 저장·컨버터 정상(VF-1·4), 커스텀 템플릿 하 SelectedIndex TwoWay가 SlotCount를 1로 clobber(VF-3·5) | `SelectedValue`(값 기반) 바인딩 전환 + 초기화 순서 보정, 개수·배치 저장 라운드트립 테스트 | §2, Step 1 |
| **F2** QR 세분화(WPF) | Upload은 이미지 항상·타임랩스 조건부(VF-8), 하위 토글 없음 | `SendPhoto`/`SendTimelapse` INI 토글 + 연동 규칙 + 미디어 선택 업로드 | §3, Step 2·3 |
| **F3** 웹 미디어 부재 안내 | URL null을 실패로 처리·만료 폴백 포함(VF-11) | "doc+미만료+URL null=옵션 꺼짐" 추론, 만료/실패와 구분 안내, 폴백 제외 | §4, Step 4 |
| **계약** ResultSession | FinalImageUrl non-null(VF-7) | FinalImageUrl nullable + 추론 규약 문서화(플래그 무추가) | §5 |

---

## 2. B9 — 프레임 슬롯 개수/배치 저장 (근본 원인 + 수정)

### 2.1 근본 원인 (코드 근거)

저장 경로는 무결하다(VF-1): `Save`가 `Slots.ToList()`를 저장하고 DTO 매핑도 정상. 문제는 **저장 시점에 `Slots`가 이미 1개**라는 것이고, 그 이유는 `SlotCount`가 4(기본)에서 **1로 바뀌었기 때문**이다(VF-2: `OnSlotCountChanged` → `ArrangeSlots` → `AutoArrange(1)`).

`SlotCount`를 1로 만드는 범인은 **ComboBox `SelectedIndex` TwoWay 바인딩 + it3 커스텀 `ControlTemplate`의 상호작용**이다(VF-3·5):
- `SelectedIndex="{Binding SlotCount, Converter=SlotCountIndex}"`는 양방향. 초기값 `SlotCount=4` → `Convert=3` → ComboBox `SelectedIndex=3` 기대.
- 그러나 커스텀 템플릿 하에서 ComboBox가 아이템 컨테이너(인라인 `ComboBoxItem` 6개)를 생성·selection을 확정하는 **초기 레이아웃 패스**에서, `SelectedIndex`가 일시적으로 **-1(미선택)→0(첫 아이템)** 으로 흔들릴 수 있다. WPF ComboBox는 `Items`가 준비되기 전/템플릿 적용 시점에 selection을 재평가하는데, 커스텀 템플릿(`Popup`+`ItemsPresenter`, deferred)에서 이 타이밍이 기본 템플릿과 달라진다.
- `SelectedIndex`가 0이 되는 순간 **TwoWay가 역방향으로 `ConvertBack(0)=1`을 `SlotCount`에 기록**(clobber) → `SlotCount=1` → `OnSlotCountChanged(1)` → `ArrangeSlots()` → `Slots` 1개. 사용자가 6을 골라도, 화면 진입/템플릿 적용 시 1로 되돌아간다. "슬롯 지정을 할 수 없다"는 이 clobber 때문.

> 확정은 앱 실행이 필요하므로 **미검증 가정(OA-1)**. 단, `SelectedIndex` TwoWay + 인라인 아이템 + 커스텀 템플릿 조합은 이 clobber의 잘 알려진 WPF 패턴이라 원인으로 강하게 지목된다.

### 2.2 수정 설계

**(1) 값 기반 바인딩으로 전환(핵심)**: `SelectedIndex`(위치 기반, 초기화에 취약) 대신 **`SelectedValue` + `SelectedValuePath`**(값 기반)로 바꾼다. 인라인 `ComboBoxItem` 대신 **`ItemsSource`로 `1~6` 정수 리스트**를 주고 `SelectedValue="{Binding SlotCount}"`(컨버터 불필요, 값 직접 매칭). 값 기반은 아이템 위치·초기화 타이밍과 무관하게 **값으로 selection을 확정**하므로 index 0 clobber가 발생하지 않는다.
- VM에 `IReadOnlyList<int> SlotCountOptions { get; } = new[]{1,2,3,4,5,6};` 노출(설정의 CutCountOptions 패턴과 동일).
- `<ComboBox ItemsSource="{Binding SlotCountOptions}" SelectedValue="{Binding SlotCount}" .../>` (SelectedValuePath 불요 — int 자체가 값).
- `SlotCountIndexConverter`는 이 경로에서 불필요(제거 또는 미사용). 다른 곳 참조 없으면 정리.

**(2) 초기화 순서 보정(방어)**: `SelectedValue` 전환만으로 대부분 해결되나, 방어적으로:
- `SlotCount` 초기값 세팅을 View 바인딩 확정 이후로(또는 VM 생성자에서 확정). `OnSlotCountChanged`가 `FrameWidth<=0`이면 `ArrangeSlots` skip(이미 있음, `:96`) — 이미지 로드 전 clobber로 인한 불필요 재배치 방지.
- **clobber 방지 가드**: `OnSlotCountChanged`에서 값이 유효 범위(1~6) 밖이거나 바인딩 초기화 중이면 무시하는 것은 과설계 — 값 기반 바인딩이면 불필요. 값 기반으로 근본 차단하고, 가드는 최소.

**(3) 슬롯 배치·저장 라운드트립 보장**: 개수뿐 아니라 위치·크기·종횡비(it4)·스케일(it5)이 저장·재현되는지 확인. `Save`가 `Slots`(개수·좌표) + `ImageSize`를 저장(기존). 재로딩은 이번 범위엔 편집 재개보다 **저장 정확성**이 핵심(Firestore에 6개 저장). 드래그 실동작은 슬롯이 6개로 유지되면 자연히 가능(VF-6).

### 2.3 검증 (headless)

- **단위(VM 레벨)**: `FrameEditorViewModel`에서 `SlotCount=6` 세팅 → `Slots.Count==6`(ArrangeSlots 반영). `SlotCount` 변경이 개수에 정확 반영. `SlotCountOptions` == {1..6}.
- **저장 라운드트립**: `Save` 호출 시 `IFrameRepository.SaveAsync`에 넘어가는 `FrameTemplate.Slots.Count == SlotCount`(목 repo로 캡처). 6 선택 → 6개 저장 검증.
- **(선택) STA 렌더 테스트**: ComboBox `SelectedValue` 바인딩이 초기화 후에도 `SlotCount`를 clobber하지 않는지 STA 스레드에서 컨트롤 로드 후 `SlotCount==초기값` 확인. STA 인프라 있으면 포함, 없으면 VM 단위 + 사용자 육안.
- **사용자 확인(육안)**: 편집기에서 6 선택 → 슬롯 6개 배치·드래그 가능 → 저장 → (재조회 시) 6개.

---

## 3. F2 — QR 전송 세분화 (사진/타임랩스 개별 토글)

### 3.1 설정 구조 (AppSettings + INI)

- **신규 필드**: `AppSettings.SendPhoto`(bool, 기본 true), `AppSettings.SendTimelapse`(bool, 기본 true). `EnableQrDelivery`(기존) 하위.
- **연동 규칙**(설계 §3.3에서 로직 순수 함수화):
  - QR on(`EnableQrDelivery=true`) → 하위 토글 유효. 기본 둘 다 on. 하나만 끄기 가능.
  - **둘 다 off → `EnableQrDelivery` 자동 off**(연동). QR 전송 자체가 무의미하므로.
  - QR off → 하위 토글 숨김/비활성(꺼진 것으로 취급하되 값은 보존 or 기본 복원 — 설계: **값 보존**, QR 재활성 시 이전 선택 복원. 단 표시상 QR off면 하위 무효).
- **INI 영속**: `IniSettingsService`의 `ReadInto`/`WriteFrom`에 `SendPhoto`/`SendTimelapse` 추가(기존 bool 항목과 동일 패턴). `Clamp`에 연동 규칙 반영(둘 다 off면 EnableQrDelivery=false 강제) — 또는 VM/서비스 레벨 연동(설계: **AppSettings에 정규화 메서드** `NormalizeQr()` — 둘 다 off면 EnableQrDelivery=false; 저장·로드 시 호출).

### 3.2 설정 UI (SettingsView)

- QR 전송 토글(기존) **아래에 사진/타임랩스 하위 토글**을 들여쓰기로. `EnableQrDelivery=true`일 때만 노출(Visibility 바인딩) — QR off면 하위 숨김.
- 하위 토글 변경 시 연동: 둘 다 off로 만들면 QR 토글도 off로(VM에서 `NormalizeQr` 반영 + 바인딩 갱신). QR 토글을 다시 on 하면 하위 기본 on(또는 보존값).
- 라이트 토큰·U7(it5) PC 밀도 유지. 하위 토글은 그룹 내 하위 레벨(들여쓰기·작은 라벨).

### 3.3 업로드 로직 (UploadService + ResultViewModel)

- **미디어 선택 업로드**: `UploadService.UploadResultAsync`에 **어떤 미디어를 업로드할지** 전달. 현재 시그니처는 `(finalImagePath, timelapsePath, retentionHours, hostingBaseUrl)`. 확장:
  - **방식 A(경로 null 활용)**: 사진 off면 `finalImagePath`를 빈/특수 취급, 타임랩스 off면 `timelapsePath=null`(기존 조건부 업로드 재활용). 사진도 조건부가 되도록 `UploadResultAsync`가 `finalImagePath`가 null/빈이면 finalUrl=null.
  - **방식 B(명시 플래그)**: `sendPhoto`/`sendTimelapse` bool 인자 추가.
  - **채택: 방식 A 확장** — `finalImagePath`를 nullable로, null이면 업로드 스킵·finalUrl=null. 호출자(`ResultViewModel`/`QrPopupViewModel`)가 `settings.SendPhoto`면 경로 전달, 아니면 null. 타임랩스는 `settings.SendTimelapse && 경로 존재`.
- **ResultSession URL**: 꺼진 미디어 URL=null. `FinalImageUrl`을 `string?`로(§5), 사진 off면 null. `TimelapseUrl`은 이미 nullable.
- **최소 1개 보장**: 연동 규칙상 둘 다 off면 QR 자체 off라 업로드 경로에 안 옴 → 업로드 시 최소 1개는 on(방어적 assert/로그).
- **QrPopup**: 업로드 성공 시 QR 생성(기존). it5의 실패 우아 처리 유지. QR 이미지는 downloadPageUrl 기준(미디어 부재와 무관하게 페이지 URL은 생성).

### 3.4 순수 로직 (테스트 대상)

- `QrDeliveryPolicy.Normalize(bool enableQr, bool sendPhoto, bool sendTimelapse)` → 정규화된 `(enableQr, sendPhoto, sendTimelapse)`: 둘 다 off면 enableQr=false. QR off면 하위는 표시상 무효(값 보존). 단위 테스트로 규칙 고정.

---

## 4. F3 — 웹 다운로드 페이지 미디어 부재 안내

### 4.1 판단 로직 (추론 방식 — 계약 변경 최소)

웹은 이미 만료 판정을 통과한 뒤 `renderSuccess(data)`를 호출한다(VF-10). 따라서 `renderSuccess` 안에서는 **문서가 존재하고 미만료임이 보장**된다. 이 맥락에서:
- **`data.finalImageUrl`이 null/부재 → "사진 전송 옵션 꺼짐"**(만료 아님, 실패 아님 — 의도적 제외).
- **`data.timelapseUrl`이 null/부재 → "타임랩스 전송 옵션 꺼짐"**.
- **로드 실패(onerror)**는 URL이 **있는데** 로드가 안 된 경우 → 기존 "실패" 처리(별개).
- **만료/문서 부재**는 `loadSession`이 `renderSuccess` 이전에 이미 만료 화면으로 처리(변경 없음).

→ **추가 플래그 없이 "doc 존재 + 미만료(renderSuccess 진입) + URL null = 옵션 꺼짐" 추론**(계약 변경 최소, PRD 권장 방식). ResultSession에 photoSent/timelapseSent 플래그 불필요.

### 4.2 웹 UI 변경 (app.js + index.html + styles.css)

- **사진 영역**: `data.finalImageUrl`이 falsy → 프리뷰·다운로드 숨기고 **"사진은 전송 옵션이 꺼져 있어 제공되지 않습니다"** 안내(신규 요소 `#photo-optout` 또는 기존 `#photo-error` 문구 분기). "옵션 꺼짐"과 "로드 실패"를 다른 문구·스타일로.
- **타임랩스 영역**: `data.timelapseUrl` falsy → 영역을 숨기지 말고(현재 숨김) **"타임랩스는 전송 옵션이 꺼져 있어 제공되지 않습니다"** 안내 표시. (F3 요구: 부재 시 안내.)
- **만료 폴백 제외**: `maybeFallbackToExpired`가 "옵션 꺼짐"을 실패로 세지 않도록 수정 — 옵션 꺼짐은 정상 성공 상태의 부분 부재이므로 만료 폴백 트리거에서 제외. (둘 다 옵션 꺼짐이면? 연동 규칙상 WPF에서 둘 다 off면 QR 자체 off라 문서가 안 만들어짐 → 이 케이스는 실제로 안 옴. 방어적으로 둘 다 null이면 안내 2개 표시하고 만료 폴백 안 함.)
- **index.html**: 사진/영상 섹션에 옵션 꺼짐 안내 요소 추가(기본 hidden). **styles.css**: `.state[hidden]` 규칙은 이미 즉시 수정 완료(요구 노트). 옵션 꺼짐 안내 스타일 추가.
- **읽기 전용 불변식 유지**: 기존 `data`(ResultSession 단건) 필드만 사용. 새 쿼리·API 없음(VF-12).

### 4.3 만료·실패와의 구분 (명확화)

| 상황 | 판단 | 화면 |
|---|---|---|
| 문서 부재/만료 | `loadSession`이 사전 차단 | 만료 화면(기존) |
| 미만료 + URL 있음 + 로드 성공 | 정상 | 성공(미디어 표시) |
| 미만료 + URL 있음 + 로드 실패(onerror) | 파일 접근 실패 | 성공 화면 내 "불러올 수 없음"(기존 실패 문구) |
| **미만료 + URL null(옵션 꺼짐)** | **의도적 제외(F3)** | **성공 화면 내 "전송 옵션 꺼짐" 안내** |

---

## 5. firebase-contract 갱신안 (ResultSession)

### 5.1 변경

- **`FinalImageUrl`을 nullable로**: 현재 계약·모델은 `finalImageUrl` 필수(non-null). F2로 사진 전송을 끌 수 있으므로 **`finalImageUrl: string | null`**(사진 off면 null). `timelapseUrl`은 이미 nullable.
- **의미론 추가**: "**미만료 문서에서 `finalImageUrl`/`timelapseUrl`이 null이면 해당 미디어는 전송 옵션이 꺼진 것**(의도적 제외)"을 계약에 명문화. 웹은 이를 만료/실패와 구분해 안내(§4).
- **플래그 무추가(추론 채택)**: `photoSent`/`timelapseSent` 같은 명시 플래그는 **추가하지 않는다**(계약 변경 최소, 추론으로 충분). 단 향후 명시성 필요 시 확장 여지로 기록.
- **최소 1개 불변식**: 미만료 ResultSession 문서는 `finalImageUrl`·`timelapseUrl` 중 **최소 1개는 non-null**(둘 다 null이면 QR 자체가 off라 문서 미생성 — WPF 연동 규칙). 웹은 방어적으로 둘 다 null도 처리(안내 2개, 만료 아님).

### 5.2 영향

- **WPF 쓰기**: `UploadService`가 켜진 미디어만 업로드하고 URL 세팅, 꺼진 쪽 null. `ResultSessionDoc`(Firestore DTO)도 nullable 반영.
- **웹 읽기**: §4 추론. Firestore null 필드는 JS에서 `undefined`/`null`로 오므로 falsy 체크 동일.
- **하위호환**: 기존 문서(둘 다 URL 있음)는 그대로 정상 표시. null은 신규 세분화 세션에만.

---

## 6. 파일 변경 요약

| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | SlotCount ComboBox: `SelectedIndex`+컨버터 → `ItemsSource`+`SelectedValue`(값 기반) | B9 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | `SlotCountOptions`(1~6) 노출, 초기화 순서 보정 | B9 |
| `src/MCPhoto.App/Converters/CommonConverters.cs` | `SlotCountIndexConverter` 미사용 정리(참조 제거 시) | B9 |
| `src/MCPhoto.Core/Settings/AppSettings.cs` | `SendPhoto`/`SendTimelapse` + `NormalizeQr()` 연동 | F2 |
| `src/MCPhoto.Core/Settings/IniSettingsService.cs` | 두 토글 INI read/write | F2 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`·`Views/SettingsView.xaml` | 하위 토글 UI(QR on일 때만)·연동 | F2 |
| `src/MCPhoto.Firebase/UploadService.cs` | `finalImagePath` nullable·미디어 선택 업로드, 꺼진 URL null | F2 |
| `src/MCPhoto.App/ViewModels/ResultViewModel.cs`·`QrPopupViewModel.cs` | SendPhoto/SendTimelapse 반영해 업로드 인자 구성 | F2 |
| `src/MCPhoto.Core/Models/ResultSession.cs`·`src/MCPhoto.Firebase/Dto/ResultSessionDoc.cs` | `FinalImageUrl` nullable | F2·계약 |
| `src/MCPhoto.Core/`(신규 `QrDeliveryPolicy`) | 연동 규칙 순수 함수 | F2 |
| `web/public/app.js` | URL null=옵션꺼짐 추론, 만료/실패 구분, 폴백 제외 | F3 |
| `web/public/index.html`·`styles.css` | 옵션 꺼짐 안내 요소·스타일 | F3 |
| `docs/design/firebase-contract.md` | ResultSession finalImageUrl nullable + 추론 규약 | 계약 |
| `tests/MCPhoto.Tests/` | `FrameEditorViewModel`(개수·저장), `SettingsTests`(토글·연동), `UploadContractTests`(미디어 null), `QrDeliveryPolicyTests` | B9·F2 |

---

## 7. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | `SelectedValue` 전환 후에도 초기화 clobber 잔존 | B9 미해결 | 값 기반은 위치 무관이라 근본 차단. VM 단위 + STA(가능 시)로 고정 | Step 1 |
| R2 | QR 하위 토글 연동(둘 다 off→QR off)이 UI 바인딩 순환 | 토글 튐 | `NormalizeQr` 단일 지점 정규화, 순수 함수 테스트, 바인딩 갱신 1방향 | Step 2 |
| R3 | `FinalImageUrl` nullable 전환이 기존 소비처(웹·업로드) 파손 | 표시 오류 | 웹 falsy 체크 이미 timelapse에 있음(동일 패턴), 하위호환(기존 문서 무영향) | Step 3·4 |
| R4 | 웹 "옵션 꺼짐"과 "로드 실패" 혼동 | 오안내 | renderSuccess=미만료 보장(VF-10) → URL null=옵션꺼짐, onerror=실패. 명확 분기(§4.3) | Step 4 |
| R5 | 둘 다 URL null 문서(비정상)에서 만료 폴백 오동작 | 만료 오표시 | 연동 규칙상 미발생(둘 다 off=QR off=문서 미생성), 방어적 안내 2개+폴백 제외 | Step 4 |
| R6 | STA 렌더 테스트 인프라 부재 | B9 headless 약함 | VM 단위(개수·저장)로 핵심 커버, STA는 선택. 육안 보완 | Step 1 |

---

## 8. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS 완료 기준은 전부 headless(build/test/grep/Emulator). 아래는 사용자 육안(각 Step trigger/non-goal로 분리).

1. **B9**: 편집기에서 슬롯 개수 6 선택 → 슬롯 6개 배치·드래그 가능 → 저장 → (재조회) 6개. 위치·크기·종횡비·스케일 반영.
2. **F2**: QR on 시 사진/타임랩스 하위 토글 노출, 하나 끄기·둘 다 끄면 QR 자동 off, QR off면 하위 숨김. 켠 미디어만 전송. 재시작 후 설정 복원.
3. **F3**: 옵션 꺼진 미디어의 다운로드 페이지에 "전송 옵션 꺼짐" 안내(만료·실패 화면과 구분). 켠 미디어는 정상 표시.

## 부록. 참고

- it3 ComboBox 커스텀 템플릿: `Controls.xaml:348-440`, `wpf-it3-design.md` §5
- it4 편집기 좌표·종횡비: `EditorTransform.cs`, `wpf-it4-design.md`
- it5 QR 실패 우아 처리·로컬 보존 순서: `wpf-it5-design.md` §2
- 웹 읽기 전용 계약: `firebase-contract.md` §0·§5, `web/public/app.js`
