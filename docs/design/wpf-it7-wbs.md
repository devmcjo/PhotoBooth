# MC포토 — 이터레이션 7 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln`(WPF) + `web/`(다운로드 페이지) — 이터레이션 7(슬롯 버그 + QR 세분화) |
| 설계 근거 | `docs/design/wpf-it7-design.md`, `docs/prd/iteration-7-frame-qr-granularity.md`, `firebase-contract.md` |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-21 |
| 빌드 검증 | WPF: `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test`. 웹: 정적 확인 + (가능 시) Firestore Emulator |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it7-design.md`만 읽고 실행할 수 있게 작성했다.
> **완료 기준은 headless(dotnet build/test·grep·Emulator/정적)로만 판정.** UI 육안은 각 Step "사용자 확인 필요"로 분리, 전체는 `wpf-it7-design.md` §8.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 build/test/grep/Emulator만.
> 색·토큰=라이트 A(it2), QR 실패 처리·로컬 보존 순서=it5.

---

## 검증된 사실 (verified facts)

- **VF-1**: 슬롯 저장 무결(`Save`가 `Slots.ToList()`, DTO 정상) → B9는 저장 시점 `Slots`가 이미 1개. (근거: `FrameEditorViewModel.cs:137`, Firestore 실측)
- **VF-2**: `SlotCount` 기본 4, 변경 시 `OnSlotCountChanged`→`ArrangeSlots`→`AutoArrange(SlotCount)`가 `Slots`를 그 개수로. SlotCount=1이면 Slots 1개. (근거: `FrameEditorViewModel.cs:29,92-108`)
- **VF-3**: ComboBox `SelectedIndex="{Binding SlotCount, Converter=SlotCountIndex}"`(TwoWay) + 인라인 ComboBoxItem 6개. (근거: `FrameEditorView.xaml:36-44`)
- **VF-4**: `SlotCountIndexConverter` 정상(Convert=Clamp(count-1,0,5), ConvertBack=index+1). (근거: `CommonConverters.cs:50-57`)
- **VF-5**: it3 ComboBox 커스텀 `ControlTemplate`(ToggleButton+SelectionBoxItem+Popup+ItemsPresenter). 초기화 타이밍이 기본 템플릿과 다를 수 있음. (근거: `Controls.xaml:377-440`)
- **VF-7**: `ResultSession.FinalImageUrl` non-null(string ""), `TimelapseUrl` nullable. (근거: `ResultSession.cs:12,15`)
- **VF-8**: `UploadService`가 최종 이미지 항상·타임랩스 조건부 업로드. (근거: `UploadService.cs:39-51`)
- **VF-9**: `ResultViewModel.Next` QR on 시 업로드, 로컬 저장은 QR 이전(it5). (근거: `ResultViewModel.cs:104-129`)
- **VF-10**: 웹 `renderSuccess`는 만료 판정 통과 후 호출(문서 존재·미만료 보장). (근거: `web/public/app.js` loadSession)
- **VF-11**: 웹이 URL null을 실패로 처리·만료 폴백 포함(`maybeFallbackToExpired`). (근거: `web/public/app.js`)
- **VF-12**: 웹 읽기 전용 단건 getDoc만(컬렉션·User·frames 금지). (근거: `app.js` 주석, 계약 §5)
- **VF-13**: 기존 테스트 — `SettingsTests`·`UploadContractTests`·`SlotLayoutTests`·`AppStateTests` 등. (근거: `tests/MCPhoto.Tests/`)

## 미검증 가정 (open assumptions)

- **OA-1**: B9는 커스텀 템플릿 하 SelectedIndex TwoWay가 초기화 시 0으로 SlotCount clobber → `SelectedValue` 값 기반 전환으로 차단 → **검증: Step 1**.
- **OA-2**: QR 하위 토글 INI 추가·연동이 기존 흐름과 정합 → **검증: Step 2**.
- **OA-3**: FinalImageUrl nullable + 미디어 선택 업로드가 계약·웹과 호환 → **검증: Step 3**.
- **OA-4**: 웹 "doc+미만료+URL null=옵션 꺼짐" 추론이 만료/실패와 구분 → **검증: Step 4**.

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (B9 슬롯 개수 값기반 바인딩)   ── P1, 독립(WPF)
Step 2 (F2 설정: 하위 토글+연동+INI)  ── 독립(WPF)
Step 3 (F2 업로드+계약: 미디어 선택)  ← Step 2(설정값), 계약(ResultSession nullable)
Step 4 (F3 웹 안내)                   ← Step 3(계약 nullable 규약). 웹은 독립 실행이나 계약 정합 필요
```

- Step 1(B9)이 P1 최우선. Step 2·3은 F2(WPF), Step 4는 F3(웹). Step 3·4는 계약(finalImageUrl nullable) 공유 — 계약 갱신을 Step 3에서 확정하고 Step 4가 소비.

---

## Step 1: B9 — 슬롯 개수 값 기반 바인딩(SelectedValue) 전환

- **Context Brief**: 프레임 편집기에서 슬롯 개수 6을 골라도 실제 저장 문서엔 1개만 들어간다(B9). 저장·컨버터는 정상(VF-1·4)이고, 원인은 ComboBox `SelectedIndex` TwoWay 바인딩이 it3 커스텀 `ControlTemplate` 하 초기화 시 index 0으로 흔들려 `ConvertBack(0)=1`을 `SlotCount`에 역기록(clobber)하는 것(VF-2·3·5, OA-1). 위치 기반 `SelectedIndex`를 **값 기반 `SelectedValue`**로 바꿔 근본 차단한다(설계 §2).
- **대상 파일**: `src/MCPhoto.App/Views/FrameEditorView.xaml`(ComboBox 바인딩), `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(`SlotCountOptions`), `src/MCPhoto.App/Converters/CommonConverters.cs`(SlotCountIndexConverter 미사용 정리), `src/MCPhoto.App/App.xaml`(컨버터 리소스 정리 시), `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `FrameEditorViewModel`: `public IReadOnlyList<int> SlotCountOptions { get; } = new[]{1,2,3,4,5,6};`. `SlotCount`(기본 4) 유지.
  - `FrameEditorView.xaml`: `<ComboBox ItemsSource="{Binding SlotCountOptions}" SelectedValue="{Binding SlotCount}" .../>`(인라인 ComboBoxItem·SelectedIndex·SlotCountIndex 컨버터 제거). 값(int)이 곧 SelectedValue라 `SelectedValuePath` 불요. 표시 텍스트에 "개" 붙이려면 ItemTemplate(선택) 또는 그대로 숫자.
  - `SlotCountIndexConverter`: 다른 참조 없으면 `App.xaml` 리소스·클래스 정리(grep로 참조 0 확인 후). 남겨도 무해하나 혼동 방지 위해 정리 권장.
  - 초기화 순서 보정: `OnSlotCountChanged`의 `FrameWidth<=0` skip 가드 유지(이미지 로드 전 불필요 재배치 방지, 기존).
  - 테스트(`FrameEditorViewModelTests`): `SlotCount=6` → `Slots.Count==6`(ArrangeSlots는 FrameWidth>0 필요 → 테스트에서 이미지 로드 или FrameWidth/Height 세팅 후). `SlotCountOptions`=={1..6}. `Save` 호출 시 목 `IFrameRepository.SaveAsync`가 받는 `FrameTemplate.Slots.Count==6`(6 선택 시). SlotCount 1~6 각각 반영.
- **검증 명령**: `dotnet test --filter FrameEditorViewModelTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `FrameEditorView.xaml`에 `SelectedValue`·`SlotCountOptions`(SelectedIndex·SlotCountIndex 제거 확인).
- **완료 기준**:
  - [관측] `FrameEditorViewModelTests` 통과: SlotCount=6→Slots 6개, 저장 시 6개 전달, SlotCountOptions {1..6}. 빌드 통과. `FrameEditorView.xaml`이 `SelectedValue` 값 기반(grep: `SelectedIndex` 슬롯 바인딩·`SlotCountIndex` 참조 제거).
  - [non-goal] 슬롯 저장·DTO 매핑·드래그(it4)·종횡비(it4)·스케일(it5)은 **변경하지 않는다**(개수 clobber만 수정). `SlotLayout.AutoArrange` 로직 불변.
  - [trigger] `Slots` 재배치는 `SlotCount`·종횡비·스케일 변경 시. 저장은 [저장] 버튼. `SlotCount`는 ComboBox 선택 값으로만 바뀜(초기화 clobber 없음).
  - [사용자 확인 필요] 6 선택→6개 배치·드래그·저장→재조회 6개(design §8-1).
- **롤백**: 이 Step 커밋 revert(View·VM·컨버터·테스트 원복).
- [ ] 완료

---

## Step 2: F2 — QR 하위 토글(SendPhoto/SendTimelapse) 설정 + 연동 + INI

- **Context Brief**: QR 전송을 사진/타임랩스로 세분화한다(F2). `EnableQrDelivery` 하위에 `SendPhoto`·`SendTimelapse` 토글을 추가하고, 둘 다 off면 QR 자동 off로 연동한다(설계 §3.1·3.2). 이 Step은 설정 모델·INI·UI·연동 규칙(순수 함수). 업로드 반영은 Step 3.
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`(`SendPhoto`/`SendTimelapse`/`NormalizeQr`), `src/MCPhoto.Core/`(신규 `QrDeliveryPolicy.cs` 또는 AppSettings 내), `src/MCPhoto.Core/Settings/IniSettingsService.cs`(read/write), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`·`Views/SettingsView.xaml`(하위 토글 UI·연동), `tests/MCPhoto.Tests/SettingsTests.cs`·`QrDeliveryPolicyTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `AppSettings`: `bool SendPhoto = true`, `bool SendTimelapse = true`. `QrDeliveryPolicy.Normalize(enableQr, sendPhoto, sendTimelapse)` 순수 함수: 둘 다 off면 `enableQr=false` 반환. `AppSettings.NormalizeQr()`가 이를 적용(저장·로드 시 호출, `Clamp`에서 or 별도).
  - `IniSettingsService.ReadInto`/`WriteFrom`: `SendPhoto`/`SendTimelapse` 추가(기존 bool 패턴). 로드 후 `NormalizeQr` 적용.
  - `SettingsViewModel`: `SendPhoto`/`SendTimelapse` `[ObservableProperty]`. 변경 시 연동 — 둘 다 off로 만들면 `EnableQrDelivery=false`(NormalizeQr), QR on 시 하위 유효. `EnableQrDelivery` 변경 시 하위 토글 노출 갱신.
  - `SettingsView.xaml`: QR 전송 토글 아래 사진/타임랩스 하위 토글(들여쓰기), `Visibility={Binding EnableQrDelivery, Converter=BoolToVis}`(QR on일 때만). 라이트 토큰·U7 밀도.
  - 테스트: `QrDeliveryPolicyTests`(둘 다 off→enableQr false; 하나만 off→유지; 둘 다 on→유지). `SettingsTests`(SendPhoto/SendTimelapse INI 라운드트립, NormalizeQr 적용 로드).
- **검증 명령**: `dotnet test --filter QrDeliveryPolicyTests` + `dotnet test --filter SettingsTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `AppSettings`에 SendPhoto/SendTimelapse, `SettingsView.xaml`에 하위 토글.
- **완료 기준**:
  - [관측] `QrDeliveryPolicyTests`(연동 규칙)·`SettingsTests`(라운드트립·NormalizeQr) 통과. 빌드 통과. `AppSettings`에 두 토글·`NormalizeQr`, `SettingsView`에 하위 토글(QR on 조건부, grep).
  - [non-goal] 업로드 로직은 이 Step 아님(Step 3). 기존 `EnableQrDelivery` 단일 동작(하위 둘 다 on 기본)은 하위호환. INI 기존 키 불변.
  - [trigger] 하위 토글 노출은 `EnableQrDelivery=true`일 때만. 둘 다 off→QR off는 NormalizeQr 시. 저장은 [저장] 버튼.
  - [사용자 확인 필요] QR on 시 하위 토글 노출·하나 끄기·둘 다 off→QR off·QR off→하위 숨김·재시작 복원(design §8-2).
- **롤백**: 이 Step 커밋 revert(AppSettings·INI·SettingsVM/View·테스트 원복).
- [ ] 완료

---

## Step 3: F2 — 미디어 선택 업로드 + ResultSession nullable (계약 갱신)

- **Context Brief**: 켜진 미디어만 업로드하고 꺼진 미디어 URL은 null이 되게 한다(F2 §3.3·§5). `UploadService`가 사진/타임랩스를 각각 조건부 업로드하고, `ResultSession.FinalImageUrl`을 nullable로 바꿔 사진 off 시 null. firebase-contract를 갱신한다(설계 §3.3·§5).
- **대상 파일**: `src/MCPhoto.Core/Models/ResultSession.cs`(`FinalImageUrl` nullable), `src/MCPhoto.Firebase/Dto/ResultSessionDoc.cs`(nullable 반영), `src/MCPhoto.Firebase/UploadService.cs`(`finalImagePath` nullable·조건부), `src/MCPhoto.Core/Upload/IUploadService.cs`(시그니처), `src/MCPhoto.App/ViewModels/ResultViewModel.cs`·`QrPopupViewModel.cs`(SendPhoto/SendTimelapse 반영), `docs/design/firebase-contract.md`(갱신), `tests/MCPhoto.Tests/UploadContractTests.cs`(확장).
- **선행 조건**: Step 2(SendPhoto/SendTimelapse 설정값).
- **구현 내용**:
  - `ResultSession.FinalImageUrl`: `string` → `string?`(사진 off 시 null). `ResultSessionDoc`도 nullable 매핑.
  - `IUploadService.UploadResultAsync`/`UploadService`: `finalImagePath`를 `string?`로. null/빈이면 업로드 스킵·`FinalImageUrl=null`. 타임랩스는 기존 조건부(경로 존재 시). **최소 1개** 방어(둘 다 null이면 예외/로그 — 연동 규칙상 미발생).
  - 호출자(`ResultViewModel`/`QrPopupViewModel`): `settings.SendPhoto`면 `finalImagePath` 전달, 아니면 null. 타임랩스는 `settings.SendTimelapse && 경로 존재`면 전달, 아니면 null. it5 실패 우아 처리·로컬 보존 순서 유지.
  - `firebase-contract.md`: ResultSession `finalImageUrl: string|null` + "미만료 문서에서 URL null = 전송 옵션 꺼짐(의도적 제외)" 의미론 + "최소 1개 non-null" 불변식 명문화. photoSent/timelapseSent 플래그 무추가(추론 채택) 기록.
  - 테스트(`UploadContractTests` 확장): 사진만 on(finalImagePath 있음, timelapsePath null) → finalUrl non-null, timelapseUrl null. 타임랩스만 on → 반대. 경로 조립·토큰 URL 규약 유지.
- **검증 명령**: `dotnet test --filter UploadContractTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `ResultSession.FinalImageUrl` nullable(`string?`), `UploadService` finalImagePath 조건부.
- **완료 기준**:
  - [관측] `UploadContractTests` 통과: 사진만/타임랩스만 on 시 해당 URL만 non-null·나머지 null. 빌드 통과. `ResultSession.FinalImageUrl` nullable(grep). firebase-contract에 nullable·추론 규약 반영(grep).
  - [non-goal] 다운로드 토큰 URL 조립·경로 규약(`results/{token}/`)·expiresAt 계산은 **변경하지 않는다**. QR 성공 시 QR 생성(it5)·downloadPageUrl은 미디어 부재와 무관(항상 생성). 둘 다 off는 QR 자체 off(Step 2 연동)라 업로드 미진입.
  - [trigger] 업로드는 미디어별 on일 때만. URL null은 해당 미디어 off 시.
  - [사용자 확인 필요] 사진만/타임랩스만 켜고 촬영 → 켠 미디어만 전송(design §8-2).
- **롤백**: 이 Step 커밋 revert(ResultSession·UploadService·호출자·계약·테스트 원복).
- [ ] 완료

---

## Step 4: F3 — 웹 다운로드 페이지 미디어 부재 안내

- **Context Brief**: 옵션이 꺼져 URL이 null인 미디어에 대해 "전송 옵션 꺼짐" 안내를 표시하고, 만료·로드 실패와 구분한다(F3). 웹은 이미 만료를 사전 차단하고 `renderSuccess`를 호출하므로(VF-10), 그 안에서 URL null이면 "옵션 꺼짐"으로 안전 해석한다(설계 §4). 추가 플래그 없이 추론(계약 최소).
- **대상 파일**: `web/public/app.js`(URL null 분기·폴백 제외), `web/public/index.html`(옵션 꺼짐 안내 요소), `web/public/styles.css`(안내 스타일).
- **선행 조건**: Step 3(계약 finalImageUrl nullable 규약). 웹 실행은 독립.
- **구현 내용**:
  - `app.js` `renderSuccess`: `data.finalImageUrl` falsy → 사진 프리뷰·다운로드 숨기고 **"사진은 전송 옵션이 꺼져 있어 제공되지 않습니다"** 안내(신규 `#photo-optout` 또는 `#photo-error`를 옵션꺼짐/실패 문구 분기). `data.timelapseUrl` falsy → 영역 숨기지 말고 **"타임랩스는 전송 옵션이 꺼져 있어 제공되지 않습니다"** 안내.
  - **만료 폴백 제외**: `maybeFallbackToExpired`가 "옵션 꺼짐"(URL null)을 실패로 세지 않도록 — URL null은 정상 성공의 부분 부재. onerror(URL 있는데 로드 실패)만 실패로. 둘 다 URL null(비정상)이면 안내 2개 + 만료 폴백 안 함.
  - **구분(§4.3)**: URL null=옵션꺼짐 안내, onerror=로드실패 문구(기존), 만료/문서부재=만료화면(loadSession 기존, renderSuccess 이전).
  - `index.html`: 사진/영상 섹션에 옵션 꺼짐 안내 요소(기본 hidden). `styles.css`: 안내 스타일(만료·실패와 시각 구분).
  - 읽기 전용 불변식(VF-12) 유지 — `data` 필드만 사용, 새 API/쿼리 없음.
- **검증 명령**: 정적 확인(`grep`로 `app.js`에 finalImageUrl/timelapseUrl falsy 분기 + 옵션꺼짐 안내, 폴백 제외 로직) + (가능 시) Firestore Emulator에 finalImageUrl=null 문서 넣어 성공 화면에 안내 표시·만료 아님 확인. 정적 JS lint(있으면).
- **완료 기준**:
  - [관측] `app.js`가 URL null을 "옵션 꺼짐" 안내로 분기(만료/실패와 구분), `maybeFallbackToExpired`가 옵션꺼짐을 실패로 세지 않음(grep/코드 확인). `index.html`·`styles.css`에 안내 요소·스타일. (Emulator 가능 시) finalImageUrl=null 미만료 문서 → 성공 화면 + 사진 옵션꺼짐 안내, 만료 화면 아님.
  - [non-goal] 웹 **읽기 전용 불변식**(단건 getDoc, User/frames 금지) 변경 없음. 만료/문서부재 처리(loadSession) 불변. 정상 미디어(URL 있음) 표시 불변.
  - [trigger] 옵션꺼짐 안내는 renderSuccess(미만료 확정) 내 URL null일 때만. 로드 실패는 onerror. 만료는 사전 차단.
  - [사용자 확인 필요] 옵션 꺼진 미디어 페이지에 "전송 옵션 꺼짐" 안내(만료·실패와 구분), 켠 미디어 정상(design §8-3).
- **롤백**: 이 Step 커밋 revert(app.js·index.html·styles.css 원복).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~13) / 미검증 가정(OA-1~4) 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→1, OA-2→2, OA-3→3, OA-4→4)
- [x] 모든 Step(1~4)에 7개 필수 필드
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step은 "사용자 확인 필요" 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`/Emulator) — **앱 실행 없음**
- [x] 순수/로직(슬롯 개수·QR 연동 정책·미디어 URL null·웹 추론) 단위 테스트/정적 확인화
- [x] UI 육안은 각 Step "사용자 확인 필요" + `wpf-it7-design.md` §8에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
