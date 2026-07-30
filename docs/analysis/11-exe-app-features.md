# 11 · Exe 앱 기능 상세

| 항목 | 내용 |
| --- | --- |
| 문서 | 11-exe-app-features.md |
| 범위 | MCPhoto Exe 앱의 전 사용자 기능(홈·로그인·프레임·촬영·재촬영·컷선택·결과·필터·타임랩스·QR·완료·유휴·설정·카메라테스트·진단·브랜딩·표시모드·버전표기) |
| 최종 업데이트 | 2026-07-29 (it16 — §3·§4·§11·§13·§16) |
| 관련 소스 경로 | `src/MCPhoto.App/ViewModels/**`, `src/MCPhoto.App/Views/**`, `src/MCPhoto.App/Services/**`, `src/MCPhoto.App/MainWindow.xaml.cs`, `src/MCPhoto.Core/Capture/**`, `src/MCPhoto.Core/Frames/**`, `src/MCPhoto.Core/LocalSave/**`, `src/MCPhoto.Core/Settings/**`, `src/MCPhoto.Core/Upload/**` |
| 갱신 규칙 | 기능(화면·플로우·옵션)을 추가/변경할 때 해당 절을 갱신한다. 특히 컷수/필터/QR 토글/**프레임 생성·편집·삭제 권한**/유휴 시간/**표시 모드 적용 규칙**이 바뀌면 반드시 반영. |

관련 문서: [10 아키텍처](./10-exe-app-architecture.md) · [12 설정/구성/브랜딩](./12-exe-app-settings-and-config.md) · 인덱스 [README](./README.md)

> 각 기능은 **목적 / 사용자 흐름 / 관련 화면·VM·서비스 / 핵심 규칙·옵션 / 근거 파일** 순으로 기술한다.

> ⚠️ **이 문서는 Windows 데스크톱 구현 참조다.** 화면·ViewModel·XAML 파일명은 현재 구현의 것이다.
>
> **다른 플랫폼 클라이언트를 만든다면 [13 · 클라이언트 동작 규격](./13-client-behavior-spec.md)이 진실원이다** — 같은 내용을 플랫폼 중립 어휘로, 타이밍 상수·검증 규칙·사용자 문구 카탈로그와 함께 정리해 두었다. 이 문서에서 얻을 것은 **엣지 케이스와 과거 결함 수정 이력**(왜 그렇게 되어 있는지)이다.
>
> 이 문서에서 **Windows 전용이라 이식 대상이 아닌 절**: §11(설정 INI 경로) · §16(표시 모드·창 기하) · §17의 로그 폴더 열기 · §18(외부 파일 버전 표기) · §15(외부 파일 브랜딩). 대응 규격은 [41](./41-local-data-and-file-formats.md)에 목적 단위로 있다.

---

## 1. 홈 · 촬영 시작

- **목적**: 대기(키오스크 idle) 화면에서 세션 시작.
- **흐름**: 홈 → [촬영하기] → 프레임 선택으로 **직행**(게스트 자동 진행, 로그인 선택 화면을 강제로 거치지 않음).
- **화면·VM**: `HomeView`(`HomeView.xaml`) · `HomeViewModel`.
- **규칙**: `Start`(`HomeViewModel.cs:17-22`)는 `Session.Reset(clearUser:false)`로 촬영 데이터만 초기화하고 **로그인은 보존**(로그인 사용자는 커스텀 프레임 사용) 후 `FrameSelect`로 전이. 홈 타이틀은 브랜딩 앱 이름(`HomeView.xaml:15`, `DynamicResource Branding.AppName`).
- **근거**: `HomeViewModel.cs`, `HomeView.xaml`.

## 2. 로그인 / 게스트 진입

- **목적**: 로그인 사용자만 커스텀 프레임 생성/사용·계정·관리자 기능 접근. 게스트는 촬영 직행.
- **흐름**: 상단바 좌측 "로그인" 또는 프레임 선택의 커스텀 유도 → 로그인 화면(id/pw) → 성공 시 **직전 화면으로 복귀**(오버레이 복귀). "게스트로 계속" 버튼은 폐지(홈 [촬영하기]가 곧 게스트 직행).
- **화면·VM**: `LoginGuestView` · `LoginGuestViewModel`. 서비스: `IAccountService`.
- **규칙**: `Login`(`LoginGuestViewModel.cs:32-56`)은 `IsBusy` 가드 → `accounts.LoginAsync(id.Trim(), pw)` → 성공 시 `Session.Login(user)`(단일 소스, `CurrentUserChanged` 통지로 상단바 자동 갱신) → `ReturnFromOverlay()`. 실패는 아이디/비번 오류 또는 네트워크 오류 메시지. 상단바 계정 버튼 로직(`AppShellViewModel.OpenAccount`, `:290-297`): 비로그인→로그인 오버레이, 로그인→계정 팝오버 토글.
- **근거**: `LoginGuestViewModel.cs`, `AppShellViewModel.cs:289-333`, `SessionContext.cs:46-59`.

## 3. 프레임 선택

- **목적**: 촬영 전 프레임 확정(이후 변경 불가). 게스트=공용만, 로그인=공용+본인 커스텀.
- **흐름**: `FrameSelect` 진입 시 목록 로드 → 카드 선택 → [다음]으로 Guide 진입. [프레임 만들기]로 에디터 진입(**프레임 쓰기 권한 필요** — 고급 유저 이상, it16). 카드 ✕로 삭제(§4).
- **화면·VM**: `FrameSelectView` · `FrameSelectViewModel`. 서비스: `FrameCatalogService`, `ILocalFrameStore`, `IFrameRepository`.
- **핵심 규칙**:
  - 진입(`OnEnterAsync` → `ReloadFramesAsync`, `FrameSelectViewModel.cs:70-93`): 공용 프레임(`catalog.GetDefaultFramesAsync`) + 로그인 시 본인 커스텀(`catalog.GetUserFramesAsync(user.Id)`) 로드, 첫 항목 자동 선택. **목록 로딩은 역할과 무관하다**(it16 E4) — 프레임 쓰기 권한이 없는 `user`·`temp_user`의 기존 프레임도 그대로 보이고 촬영에 쓸 수 있다(편집·삭제 UI만 사라진다).
  - 권한 플래그(같은 함수, `:80-82`): `CanCreateFrame`·`CanDeleteFrames` = `Role.CanWriteFrames()`(고급 유저 이상), `IsPower` = manager/admin. 두 축은 별개다(§4.1·§4.2).
  - 목록 우선순위(`FrameCatalogService.GetDefaultFramesAsync`, `FrameCatalogService.cs:45-84`): ① 로컬 공용(번들+파워캐시, 접두 없는 파일) → ② DB `isDefault` 중 **로컬에 이름 없는 것만** 다운로드·캐시(이름 dedup) → ③ 번들 폴더 이미지(slots 없으면 2×2 격자 자동) → ④ 코드 생성 fallback. 오프라인·백엔드 미도달 시 ②를 건너뛰고 ③④로 폴백.
  - [다음](`FrameSelectViewModel.cs:170-177`): 선택 프레임을 `Session.SelectedFrame`에 고정 + `Session.Capture.Begin(frame, Settings.CutCount)`.
- **근거**: `FrameSelectViewModel.cs`, `FrameCatalogService.cs`.

## 4. 프레임 생성 · 편집(에디터) · 삭제

### 4.1 생성·편집

- **목적**: 이미지 업로드 → 슬롯 배치(개수/종횡비/크기) → 저장. 편집 범위는 **슬롯 배치만**(텍스트/스티커/배경 제외).
- **흐름**: FrameSelect → [프레임 만들기] → 이미지 로드 → 슬롯 개수(1~6)·종횡비(4:3/3:4/1:1)·크기(70~130%) 지정 → 드래그로 이동 → [저장].
- **⚠️ 진입 권한(it16)**: 편집기에 도달할 수 있는 역할은 **고급 유저(`advanced_user`)·매니저·관리자**뿐이다. `user`·`temp_user`·게스트는 [프레임 만들기]·[선택 편집] 버튼이 **미노출**이고 커맨드도 거부되므로 편집기(및 그 안의 "기존 프레임 불러오기" 모달)에 들어올 수 없다. 저장 경로에도 fail-closed 가드가 있다(`FrameEditorViewModel.Save` — `CanWriteFrames` 미보유 시 *"프레임을 만들 권한이 없습니다."*).
- **화면·VM**: `FrameEditorView`(+ code-behind) · `FrameEditorViewModel`. 서비스: `IFrameRepository`, `ILocalFrameStore`.
- **핵심 규칙**:
  - 이미지 검증(`LoadImage`, `FrameEditorViewModel.cs:63-107`): PNG/JPG/JPEG만, 10MB 이하, 장변 4000 초과 시 축소, PNG로 재인코딩.
  - 자동 배치(`SlotLayout.AutoArrange`, `SlotLayout.cs:23-71`): 세로 스트립(aspect<0.6)=1열, 그 외 격자(4=2×2, 6=2×3 등). 각 셀 안에서 `targetAspect` 유지 최대 사각형 중앙 배치.
  - 크기 스케일(`OnSlotScalePercentChanged`, `:116-125` / `SlotLayout.ScaleSlots`, `:118-134`): 항상 원본 `_baseSlots` 기준으로 스케일(누적 오차 방지), 70~130 클램프, 중심 유지.
  - 드래그(`UpdateSlot`, `:147-172`): 경계 클램프 + `_baseSlots` 중심 동기화. **좌표 변환은 순수함수 `EditorTransform`**(`EditorTransform.cs`)로 표시·드래그·클램프가 동일 변환(Uniform 스케일 + 중앙 레터박스) → WYSIWYG. 캔버스 기준은 `SlotCanvas.ActualWidth/Height`(`FrameEditorView.xaml.cs:70-73`), 절대 위치 이동(그랩 오프셋, `:106-143`).
  - 저장 유효성(`SlotLayout.IsValid`, `:165-175`): 개수 1~6, 경계 내, 겹침 없음.
  - 저장(`Save`) **역할별 분기**:
    - **power**(admin/manager) **신규 생성**: 공용 기본 프레임 → DB(`isDefault=true, userId=null`) + 로컬 캐시(frameId 기반, 접두 없음). **공용 기본 프레임을 서버에 배포하는 유일한 경로**.
    - **advanced_user**(고급 유저, it16): 비power 분기를 타서 **로컬 전용**(DB 미저장), 개인 스코프 `{계정}_{이름}.png` 접두. 서버 쓰기 요청이 발생하지 않는다(프레임 쓰기 라우트는 `requirePower` 뒤라 애초에 403).
    - **user·temp_user**: 저장 경로에 도달하지 않는다(위 진입 권한 — it16 이전에는 `user`가 로컬 전용으로 저장할 수 있었다).
    - 10개 초과는 `InvalidOperationException`, 이름 금지문자는 `IOException` 메시지를 그대로 노출.
- **프레임 편집은 로컬 전용(it15 F1)**: 편집기 상단에 상시 배너 **"이 프레임 편집은 해당 PC에서만 적용됩니다. 서버의 기본 프레임은 변경되지 않으며, 다른 PC에는 반영되지 않습니다."** 를 표시한다(역할·출처·모드 무관, `Visibility` 바인딩 없음).
  - it2의 "로컬만 / DB도 업데이트" 확인 팝업과 `SaveToDb`·`FrameDiff`·`IFrameRepository.UpdateAsync`/`SupportsUpdateById`는 **클라이언트에서 전면 제거**됐다. 편집 저장은 어떤 경로로도 서버를 호출하지 않는다.
  - **fork 저장**(`FrameEditPolicy.RequiresFork` = 출처가 `UserLocal`이 아님): DB/번들/fallback 유래 프레임을 편집하면 원본 파일을 건드리지 않고 `FrameNaming.NextCopyName`이 만든 **`{원본이름} 사본`**(충돌 시 ` 2`, ` 3` … 99, 그 뒤 GUID 8자)으로 신규 저장한다. 이름 제안값은 진입 시 1회 계산되며 사용자가 수정할 수 있다.
  - fork 저장은 `FrameTemplate.Id = ""`로 `SaveLocal(ownerName: null)`을 호출해 **`.slots`에 `#dbid`를 기록하지 않는다** → 로컬 사본은 `local:{파일명}` id를 갖고 서버 문서와 연결이 끊긴다. 원본 이름이 `PublicFrameNames()`에 남으므로 `FrameCatalogService`의 이름 dedup이 유지되어 **DB 재다운로드가 발생하지 않는다**.
  - **원본 덮어쓰기 가드**: 공용 스코프(power) fork에서 이름을 원본과 같게 두면 저장을 중단하고 *"원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요."* 를 표시한다. user 스코프는 파일명이 `{계정}_{이름}`이라 공용 원본과 겹치지 않으므로 가드하지 않는다.
  - 저장 스코프(power=공용 `{이름}.png` / user=개인 `{계정}_{이름}.png`)는 **현행 유지**. 저장 버튼 위 `SaveScopeNotice` 캡션이 이번 저장의 실제 결과(서버 등록 / fork / 덮어쓰기 / 내 프레임)를 동적으로 안내한다 — 배너는 정책, 캡션은 결과.
  - power 스코프 저장 시 이름에 `_`가 있으면 *"이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다."* 를 **비차단** 경고로 남긴다(공용/user 구분자 충돌, `LocalFrameStore` 규약).
- **기존 프레임 불러오기(it15 F2)**: "이미지 불러오기" 바로 아래 **"기존 프레임 불러오기"** 버튼(`IsCreateMode` = 생성 모드 전용). 파일 탐색기가 아니라 편집기 내부 **오버레이 모달**(새 `Window` 아님)에서 프레임 선택 화면과 같은 썸네일 그리드로 고른다.
  - 목록 VM = `FramePickerViewModel`(Transient). 후보 = `FrameCatalogService.GetDefaultFramesAsync()`(공용: 번들 + DB 캐시 + DB 다운로드) + `GetUserFramesAsync(userId)`(본인 개인 로컬). 번들·fallback도 **복사는 허용**(원본을 수정하지 않으므로 안전) — 역할 필터 없음.
  - `ApplyPickedFrame`: 반드시 `LoadImage` 경유(번들 `.jpg` 대응 + 장변 4000 축소)로 이미지를 **읽기만** 하고, 슬롯은 `FrameWidth / src.ImageSize.Width` 배율로 보정해 새 `Slot` 인스턴스로 값 복사한다. **임시 파일을 만들지 않는다**(디스크 쓰기는 `Save()` 1회뿐) → "저장 전 취소 시 임시 파일 정리"가 임시 파일 부재로 자동 충족.
  - 세션 정체성은 항상 fork(`ForkFromCatalog`)이며 `_isEditing`은 건드리지 않는다 → 생성 흐름 유지(다른 프레임으로 다시 바꿀 수 있음). **F2로 불러온 세션은 power여도 DB에 등록되지 않는다**(서버 배포는 빈 편집기 신규 생성만).
  - [취소]는 모달만 닫고 편집기 상태·디스크 모두 무변경. 목록 로딩은 `CancellationToken`으로 중단한다.
  - 썸네일 카드는 `Themes/Controls.xaml`의 공유 리소스(`FrameCard.ItemContainer` / `FrameCard.Content` / `FrameCard.FilePathToImage`)로 프레임 선택 화면과 시각을 공유하며, 삭제 ✕ 버튼만 `FrameSelectView`가 합성한다.
- **편집 권한 규칙(역할×출처, item2 · it16 §4)**: 편집 진입·"선택 편집" 버튼 노출은 순수 함수 `FrameEditPolicy.CanEdit`가 게이트한다.
  - 출처 판정 `FrameOrigin.Classify`(`FrameOrigin.cs`): `local:`=본인 로컬 생성분, 접두 없는 실 DB id+`isDefault`=DB 공용 기본, `bundle:`=번들, `fallback`/빈 Id=코드 생성.
  - **1차 게이트(it16 신규)**: `role.CanWriteFrames()`(고급 유저 이상). 통과하지 못하면 출처를 보지 않고 즉시 불가.
  - **게스트**: 편집 불가(전부). **temp_user·user**: **전부 불가**(it16 E4 — 사용만). **advanced_user**: 본인 로컬 생성분만(`UserId==현재계정` 검증). **power**: 본인 로컬 + DB 공용 기본. **번들·fallback**: 누구도 불가.
  - `FrameSelectViewModel.CanEdit`는 이 순수 함수에 위임(기존 `local:` 무검증 결함 제거). 진입(`EditFrame`)·버튼(`CanEditSelected`) 이중 게이트 + 저장(`Save`) fail-closed 3중 방어.
  - ⚠️ **폐지(it15)**: it2의 power 기본 프레임 편집 저장 팝업(`IsDbUpdatePromptVisible` / `SaveLocalOnly` / `SaveToDb` / `CancelDbUpdatePrompt`)과 `FrameDiff`, `IFrameRepository.SupportsUpdateById`·`UpdateAsync`, `FrameEditPolicy.RequiresDbUpdatePrompt`는 모두 삭제됐다. 서버 라우트 `PUT /frames/{id}`는 남아 있지만 **앱은 호출하지 않는다**(운영/관리 도구 전용).
- **근거**: `FrameEditorViewModel.cs`, `FramePickerViewModel.cs`, `FrameEditorView.xaml`(+ code-behind), `FrameSelectView.xaml`, `Themes/Controls.xaml`, `FrameOrigin.cs`, `FrameEditPolicy.cs`, `FrameNaming.cs`, `IFrameRepository.cs`, `HttpFrameRepository.cs`, `EditorTransform.cs`, `SlotLayout.cs`, `SlotAspect.cs`. 설계: `docs/design/wpf-it15-frame-ux-design.md`.

### 4.2 삭제(역할별)

- **목적**: 로컬 항상 삭제 + 파워는 서버(DB+Storage) 동시 삭제 선택.
- **흐름**: 카드 ✕ → 확인 팝업(파워는 "서버에서도 제거" 체크) → [확인].
- **VM**: `FrameSelectViewModel` A3 영역(`:96-203`). 저장소: `ILocalFrameStore.DeleteLocal`, `IFrameRepository.DeleteAsync`.
- **핵심 규칙**:
  - **역할 판정(it16 신설)**: 순수 함수 `FrameEditPolicy.CanDelete(frame, role)` — ① `CanWriteFrames()`(고급 유저 이상) 없으면 불가, ② 로컬 저장분(`UserLocal`)=가능, ③ DB 공용(`DbDefault`)=**power만**, ④ 번들·fallback·빈 Id=불가. 종전에는 커맨드 가드(`CanDeleteFrames`=로그인 여부)가 컨버터보다 느슨해 비power가 DB 공용 프레임의 로컬 파일을 지울 수 있었고, it16이 이 판정을 한 곳으로 모으며 함께 막았다.
    - ⚠️ `CanDelete`는 **소유자(`userId`)를 보지 않는다**. power가 fork 저장한 *공용* 로컬 프레임은 `UserId=null`로 로드되므로 소유자 판정을 넣으면 기존 삭제 능력이 회귀한다. 타인의 개인 프레임은 `{계정}_` 접두 필터로 목록에 애초에 오르지 않는다.
  - 출처 기반 삭제 가능 판정(`IsDeletable`, `:62-65`)은 **그대로 유지**된다(빈 Id 방어 — 컨버터와 대칭). `RequestDelete`는 `CanDelete`와 `IsDeletable`을 **둘 다** 확인한다.
  - 노출 규칙(멀티 컨버터 `FrameDeleteVisibilityConverter`): 게스트·`user`·`temp_user` 미노출(`CanDeleteFrames`=false로 첫 조건에서 Collapsed), `local:`=쓰기 권한 있는 본인 로그인 시 노출, 공용/DB=파워만.
  - `ConfirmDelete`: 로컬 삭제 **항상** → 파워 & 체크 시 서버 삭제(`alsoServer = DeleteAlsoServer && IsPower`, `:123`).
  - 서버 삭제(`DeleteFromServerAsync`): 저장된 DB id(`#dbid`)로 시도 → 실패 시 **이름 매칭 재삭제**(공용 프레임 대비) → 결과를 사용자에게 명확히 안내(성공 오인 금지: 미발견/예외 시 오류 표시).
- **근거**: `FrameSelectViewModel.cs`, `FrameEditPolicy.cs`, `LocalFrameStore.cs`(접두 규칙·`#dbid` 메타), `CommonConverters.cs`(`FrameDeleteVisibilityConverter`), `Views/FrameSelectView.xaml`. 역할 축 정의는 [60 §1.2](./60-auth-accounts-and-roles.md#12-canwriteframes--프레임-저작-권한-축-it16-신규).

## 5. 가이드 → 촬영

### 5.1 가이드

- **목적**: 촬영 직전 컷수·카운트다운·거울모드 안내.
- **화면·VM**: `GuideView` · `GuideViewModel`(`GuideViewModel.cs`). 진입 시 설정에서 `CutCount`/`CountdownSec`/`SlotCount`/`MirrorMode` 표시(`:20-28`). [촬영 시작]→Capture, [취소]→홈.

### 5.2 촬영(N컷 연속)

- **목적**: N컷을 컷당 카운트다운 후 자동 셔터로 연속 촬영하며 세션 전체를 녹화.
- **화면·VM**: `CaptureView`(`CaptureView.xaml`) · `CaptureViewModel`. 서비스: `ICameraService`.
- **핵심 규칙·옵션**:
  - **컷수**: `Settings.CutCount`(6/8/10 중 하나, 기본 6). 실제 촬영 수 = `Capture.CutCount = max(설정컷, 슬롯수)`(`CaptureSession.Begin`, `CaptureSession.cs:35-41`).
  - **카메라 준비/Ready 게이트**(`OnEnterAsync`, `CaptureViewModel.cs:55-99`): `StartAsync(device, aspect, mirror)` → 실패 시 `CameraLoadState.Failed` + 안내. 성공 시 `WaitForStablePreviewAsync(8000ms)`로 안정 프리뷰(연속 8프레임+500ms+fps>0, `PreviewReadiness`) 대기 → 타임아웃 시 Failed(무한 로딩 방지). Ready 후에만 시퀀스 시작(로딩 오버레이는 `CaptureView.xaml:49-76`, 스피너).
  - **세션 폴더**: `sessions/{guid}` 생성, `session.mp4`·세션 시각 세팅(`:89-94`).
  - **컷당 카운트다운**: `CountdownAsync(CountdownSec)`(`:178-198`) — 1초 간격 감소.
  - **[바로 촬영]**: `ShootNow`(`:200-202`)가 카운트다운 CTS를 취소 → 남은 시간 스킵, **매 컷 사용 가능**(셔터 버튼 `CaptureView.xaml:32-37`). 세션은 계속.
  - **플래시**: `Settings.FlashMode` on이면 셔터 직전 화면 하양 오버레이 120ms(`:147-153`, 오버레이 `CaptureView.xaml:45-47`, `Brush.OnAccent` 흰 화면).
  - **거울모드**: `Settings.MirrorMode`를 `StartAsync`에 전달(프리뷰=저장 동일, 기본 on).
  - **시퀀스**(`RunCaptureSequenceAsync`, `:128-176`): 녹화 시작 → 컷별(카운트다운 → 플래시 → `CaptureStillAsync` → `Capture.AddCut` → 300ms 간격) → 녹화 종료 → CutSelect 전이. 취소/오류는 로그 후 홈.
  - **이탈**: `OnLeaveAsync`(`:207-213`)에서 세션/카운트다운 취소 + 녹화·카메라 정지.
- **근거**: `CaptureViewModel.cs`, `CaptureView.xaml`, `CaptureSession.cs`, `PreviewReadiness.cs`.

## 6. 세션 녹화 → 컷 선택 → 결과 합성

### 6.1 컷 선택

- **목적**: 촬영된 N컷 중 정확히 슬롯 수만큼 선택(선택 순서=슬롯 순서).
- **화면·VM**: `CutSelectView` · `CutSelectViewModel`. 상태: `CaptureSession`.
- **핵심 규칙**:
  - 진입(`OnEnterAsync`, `CutSelectViewModel.cs:26-44`): 컷 썸네일 생성(`StillImageConverter.ToBitmapSource`), 대표 슬롯 종횡비로 썸네일 컨테이너 비율 맞춤(WYSIWYG, 기본 3:4).
  - 토글(`ToggleCut`→`CaptureSession.ToggleSelection`, `CaptureSession.cs:51-65`): 이미 선택이면 해제, 아니면 추가(슬롯 수 초과 불가), 선택 순서 번호 갱신.
  - [다음]은 `IsSelectionComplete`(선택 수==슬롯 수)일 때만.
  - [재촬영] (**전체 재촬영, it11 #13**, `:91-97`): `RetakeEnabled` on일 때만 버튼 노출, `CanFullRetake`(=`FullRetakeCount < RetakeLimit`)면 활성. 클릭 시 `CaptureSession.BeginFullRetake`(컷·선택 폐기 + 카운터 증가, 프레임 유지) → Guide(세션 전체 재촬영). `RetakeLimit`(1~3) 도달 시 버튼 Disable + 커맨드 진입 이중 방어. **컷별 재촬영은 미구현**(버튼 UI 배치 USER-DECISION 대기, [90 로드맵](./90-roadmap-and-future-work.md) §2).
- **근거**: `CutSelectViewModel.cs`, `CaptureSession.cs`.

### 6.2 결과 합성

- **목적**: 선택 컷 + 프레임 + 필터로 최종 이미지 합성·미리보기.
- **화면·VM**: `ResultView` · `ResultViewModel`. 서비스: `ICompositionService`, `ITimelapseService`, `ILocalSaveService`, `ICameraService`.
- **핵심 규칙**:
  - 합성(`ComposePreviewAsync`, `ResultViewModel.cs:76-104`): 출력 포맷(`OutputFormat`)으로 `final.{ext}`, `composition.ComposeAsync(frame, selectedCuts, filter, outPath)`, 결과를 `Session.FinalImagePath` + `Preview`(`StillImageConverter.FromFile`)로 표시.
  - 프레임은 촬영 전 고정이라 변경 불가. 필터만 변경 가능(재합성).
- **근거**: `ResultViewModel.cs`, `ICompositionService.cs`.

## 7. 필터(원본/흑백/밝게/뷰티)

- **목적**: 결과물에 필터 적용. 설정 토글은 "**노출 여부**"만, 실제 적용은 결과 화면.
- **화면·VM**: `ResultView` 필터 버튼 · `ResultViewModel`. 종류 `FilterKind`(None/Grayscale/Brightness/Beauty).
- **핵심 규칙**:
  - 노출 목록(`BuildFilterOptions`, `ResultViewModel.cs:66-74`): **항상 원본(None)** + 설정에서 켜진 것(`FilterGrayscale`/`FilterBrightness`/`FilterBeauty`). 순수 로직이라 테스트 대상.
  - 필터 변경(`SetFilter`, `:106-114`): `Session.Filter` 갱신 후 **전체 컷 일괄 재합성**.
  - 프리뷰 즉시 반영: `StillImageConverter.FromFile`이 `IgnoreImageCache`로 같은 경로(`final.{ext}`) 재합성 시 WPF URI 캐시가 이전 이미지를 반환하는 문제를 방지(`StillImageConverter.cs:36-51`).
  - 필터 구현: `Filters.Apply`(Capture) — Grayscale(BGR2GRAY→GRAY2BGR), Brightness(alpha 1.1/beta 20), Beauty(bilateral + 블렌드 + 톤). 컷 전체 일괄(개별 영역 아님).
- **근거**: `ResultViewModel.cs`, `StillImageConverter.cs`, `SettingsView.xaml:220-255`(필터 노출 토글, 원본은 고정 체크·Disable).

## 8. 타임랩스 · QR 전송 · 로컬 저장 ([다음] 처리)

`ResultViewModel.Next`(`ResultViewModel.cs:116-159`)가 순차 처리: 타임랩스 생성 → 로컬 저장(옵션) → QR(옵션) 또는 완료.

### 8.1 타임랩스 생성(배속)

- **목적**: 세션 녹화본을 짧은 배속 영상으로.
- **규칙**: 녹화본 존재 시 `timelapse.mp4` 생성. 세션 길이(`OpenCvCameraService.LastSessionSeconds`)를 `TimelapseService.LastSessionSeconds`에 전달(`:132-134`) → `CreateTimelapseAsync`가 `FfmpegArgs.ComputeSpeedFactor`(목표 10~15초, ≤15초면 1배)로 배속 산출 → ffmpeg `setpts` 변환. ffmpeg 부재 시 null.
- **근거**: `ResultViewModel.cs:127-134`, `TimelapseService`(Capture), `FfmpegArgs.cs`.

### 8.2 로컬 저장

- **목적**: 결과물을 기기에 영구 보관(TTL 무관).
- **규칙**: `SaveLocalCopy` on이면 저장. 경로는 `LocalSavePath`, 빈 값이면 `{실행경로}\result`(`ResultViewModel.cs:138-144`). `LocalSaveService.SaveAsync`가 `{경로}\mcphoto_YYMMDD_HHMM\`(충돌 시 `-2`,`-3`…) 폴더에 `final.{ext}`·`timelapse.mp4` 복사, 쓰기 불가 시 예외 대신 null(크래시 금지, `LocalSaveService.cs`).
- **근거**: `ResultViewModel.cs:137-145`, `LocalSaveService.cs`.

### 8.3 QR 전송(사진/타임랩스 개별 토글)

- **목적**: 업로드 후 QR로 모바일 다운로드 페이지 제공.
- **규칙**:
  - `EnableQrDelivery` on → `Qr` 상태, off → `Done`(`ResultViewModel.cs:147-151`).
  - 개별 토글(`SendPhoto`/`SendTimelapse`): QR 팝업이 사진/타임랩스 경로를 옵션 기준으로만 전달(`QrPopupViewModel.cs:47-48`).
  - **off→on 재활성 규칙**: `EnableQrDelivery`가 false→true로 켜질 때 하위 토글 둘 다 강제 on(`QrDeliveryPolicy.OnReEnabled`→`SettingsViewModel.OnEnableQrDeliveryChanged`, `SettingsViewModel.cs:158-172`). 둘 다 off면 QR 자체 off로 정규화(`QrDeliveryPolicy.Normalize`).
- **근거**: `ResultViewModel.cs`, `QrPopupViewModel.cs`, `QrDeliveryPolicy.cs`, `SettingsViewModel.cs`.

## 9. QR 팝업 · 완료

### 9.1 QR 팝업

- **목적**: 업로드 **성공 후에만** QR 노출, 실패 시 우아 처리.
- **화면·VM**: `QrPopupView` · `QrPopupViewModel`. 서비스: `IUploadService`, `IQrService`.
- **핵심 규칙**(`OnEnterAsync`, `QrPopupViewModel.cs:40-91`):
  - 전송할 결과물(사진·타임랩스 옵션 기준)이 없으면 방어 안내.
  - `upload.UploadResultAsync(photo?, timelapse?, RetentionHours, HostingBaseUrl, progress?)` → 성공 시 `qr.GenerateQrPng(DownloadPageUrl, 12)` 노출 + "{N}시간 후 자동 삭제" 고지.
  - **업로드 진행률(it11 #16)**: 업로드 중 진행 바 + 단계 라벨(사진→타임랩스→마무리). GCS 파일 단위 바이트 진행률을 `IProgress<UploadProgress>`로 수신(`Progress<T>`는 UI 스레드 생성 → 마샬링 안전, 순수 `ComputeOverall`로 전체 %). 초기 `IsIndeterminate`.
  - **실패 시 우아 처리**(Storage 버킷 부재 등): 흐름을 막지 않는 비위협 안내, 결과물은 로컬 보존(QR 분기 이전 저장으로 손실 0), [완료]/[재시도](`Retry`) 제공(재시도 시 진행률·상태 0에서 재시작). 로컬 저장 여부에 따라 안내 문구 분기.
- **근거**: `QrPopupViewModel.cs`, `IUploadService`, `QrService.cs`.

### 9.2 완료

- **목적**: 감사 화면 후 자동 홈 복귀(1회 세션).
- **화면·VM**: `DoneView` · `DoneViewModel`.
- **규칙**: 진입 시 6초 타이머 후 자동 홈 복귀(`DoneViewModel.cs:16-27`). **로그아웃 없음**(`clearUser:false`, 촬영 후 로그인 유지, it5 B8). 촬영 데이터는 Reset이 항상 폐기. 로그아웃은 계정 메뉴 수동 또는 유휴 타임아웃만.
- **근거**: `DoneViewModel.cs`.

## 10. 유휴 감시(경고 팝업)

- **목적**: 무인 키오스크에서 방치 세션을 홈으로 회수(하지만 **로그아웃은 하지 않음**).
- **규칙**:
  - 세션 활성 상태(`IsSessionActive`)에서 **2분(120초) 무동작** → 경고 오버레이 + **10초 카운트다운**(`AppShellViewModel.cs:27-30`, `:236-262`).
  - [이어서 진행하기](`ContinueSession`, `:339-344`)=경고 해제+타이머 재시작(현재 화면·로그인 유지). [메인 화면으로](`GoHomeFromIdle`, `:348-352`)=즉시 홈. 카운트다운 0 → `ReturnHome(clearUser:false)`.
  - 경고 표시 중 사용자 활동은 무시(버튼으로만 해제, `NotifyUserActivity` `:216-220`).
  - **로그아웃 절대 금지**(`:260`, it8 A1). FrameEditor는 유휴 감시 제외(로그인 필수 능동작업).
- **근거**: `AppShellViewModel.cs`, `IdleWatchdog.cs`, `IdleCountdown.cs`, `MainWindow.xaml:81-103`.

## 11. 설정 화면

- **목적**: AppSettings 전 항목 편집(앱 설정만; 계정·관리자는 Account 페이지로 분리).
- **화면·VM**: `SettingsView`(`SettingsView.xaml`) · `SettingsViewModel`. 서비스: `ISettingsService`, `ICameraService`, `ICameraTestDialogService`, `IDiagnosticsDialogService`(it11 #14).
- **항목**(2열 그리드 + 그룹, `SettingsView.xaml`):
  - 촬영: 컷 수(6/8/10), 컷당 카운트다운(3/6/8/10), 거울모드, 플래시, **셔터음**, **재촬영 사용**(+on일 때 **횟수 제한 1~3**, it11 #13).
  - 장치·표시: 카메라 장치(ComboBox+↻재검색+테스트, **실제 장치명 표시** it11 #15), 표시 모드(전체화면/창모드).
  - 출력·전송: 출력 포맷(JPG/PNG), **QR 전송(+하위 사진/타임랩스 토글)**, **로컬 저장**, 로컬 저장 경로, 보관 시간(1~72h). (it12 R2: QR 전송·로컬 저장을 장치·표시 → 출력·전송으로 이동)
  - 필터: 원본(고정 on·Disable), 흑백/밝게/뷰티 노출 토글.
  - 고급: 다운로드 페이지 Base URL, Storage 버킷, **서버 연결 상태**(it10, 읽기전용), **[진단·상태] 버튼**(로그인 전용 → §17, it11 #14).
  - **로그인 전용 편집(it12 R1)**: 거울모드·재촬영(횟수 포함)·필터(흑백/밝게/뷰티)·QR 전송·다운로드 URL·Storage 버킷은 게스트에겐 OFF 표시·컨트롤 비활성 + 옆에 "로그인 필요" **인라인 노티 상시 표시**(it12 R3, hover 툴팁에서 개정 — 시인성). 런타임 동작은 ini(관리자값)대로 — 편집 권한만 제한.
- **설정 진입 시 상단 설정(⚙) 버튼 숨김**(자기 화면 재진입 방지, `IsSettings`). 취소/닫기 등 공용 버튼은 아웃라인 스타일(`Button.Ghost`)로 CTA와 정렬.
- **핵심 규칙**:
  - 카메라 열거(`RefreshCamerasAsync`, `SettingsViewModel.cs:90-113`): `EnumerateDevices()`를 `Task.Run` 백그라운드(수백 ms~초), 목록 비면 ComboBox/테스트 Disable + 안내, 저장 인덱스 없으면 첫 장치로 보정.
  - 저장(`SaveSettings`, `:283-348`): **① 현재 창 기하 캡처**(`RequestCaptureWindowBounds`) → ② 필드→AppSettings→`Save()`(내부 Clamp) → ③ `LoadSettings()`로 클램프값 재반영 → ④ 표시 모드 적용(`RequestApplyDisplayMode`). **성공/실패 정직 표시**(bool 반환, 실패 시 오류 토스트, 성공 오인 금지).
    - ⚠️ **순서 계약(it16)**: ①은 반드시 `s.DisplayMode`를 갱신하기 **전에** 실행한다(창은 아직 이전 모드로 떠 있다). 뒤바뀌면 창모드→전체화면 저장 시 직전 창 위치를 잃는다. 단위 테스트가 이 순서를 고정한다(`tests/MCPhoto.Tests/SettingsViewModelTests.cs`).
    - **창모드 창이 저장 시 옛 위치·크기로 점프하던 버그는 it16에서 수정**됐다 — 표시 모드가 그대로면 창에 아무 것도 하지 않는다(§16).
  - QR 연동 정규화: 하위 토글 둘 다 off→QR off, off→on 재활성 시 하위 둘 다 on(`:154-185`). 로드 중에는 `_normalizing`으로 억제.
  - 저장 바는 하단 sticky(스크롤 밖, `SettingsView.xaml:278-299`) — 저장/닫기 항상 노출.
- **근거**: `SettingsViewModel.cs`, `SettingsView.xaml`. 값·기본값·범위 상세는 [12 설정/구성](./12-exe-app-settings-and-config.md).

## 12. 카메라 테스트 모달

- **목적**: 선택 카메라로 **실촬영과 동일**한 프리뷰·플래시·셔터를 재현하되 **저장하지 않음**.
- **흐름**: 설정 → [테스트] → 모달(로딩→프리뷰) → [테스트 촬영]/[닫기].
- **화면·VM·서비스**: `CameraTestWindow` · `CameraTestViewModel` · `CameraTestDialogService`(Singleton).
- **핵심 규칙**:
  - 오픈(`CameraTestDialogService.ShowAsync`, `CameraTestDialogService.cs:28-44`): 창 먼저 표시(로딩 오버레이) → `Loaded`에서 `vm.StartAsync()` → `ShowDialog()`(모달) → 닫힌 뒤 `StopAsync()`(스레드 join 확실 해제).
  - 시작(`StartAsync`, `CameraTestViewModel.cs:45-75`): **`StopAsync→StartAsync(선택 인덱스)`**(StartAsync는 running이면 무시하므로 Stop 선행) + `WaitForStablePreviewAsync`(8초, 실촬영 동일 규칙).
  - 셔터(`ShootTest`, `:78-104`): 플래시 옵션 재현 + `CaptureStillAsync` **결과 폐기**(저장/합성 없음) + "저장되지 않았습니다" 안내.
  - VM은 Window/Application 미참조(`RequestClose` 이벤트로 창 닫기, `:34/106-107`).
- **근거**: `CameraTestViewModel.cs`, `CameraTestDialogService.cs`.

## 13. 계정 · 관리자 도구 · 사용자 관리

- **목적**: 계정 관리(본인 정보 · PIN 변경), 사용자 관리(power), 앱 종료(power).
- **화면·VM**: `AccountView`(단일 화면, 진입 모드 분기) · `AccountViewModel`; `UserMgmtView` · `UserMgmtViewModel`. 서비스: `IAccountService`.
- **핵심 규칙**:
  - 계정 페이지 모드(`AccountMode`, `AccountViewModel.cs:13-20`): **Account**(내 정보 + PIN 변경) / **Admin**(관리자 도구·전역 한도·앱 종료). 상단바 팝오버 항목이 지정(`AppShellViewModel.cs:431-437`). 진입 시 **PIN 게이트** 통과 필수(`AppShellViewModel.EnsurePinGateAsync` 공유 — 설정 진입과 동일 PIN·동일 다이얼로그).
  - PIN 변경(`ChangePin`, `AccountViewModel.cs:159-212`): `HasPin`이면 현재 PIN 확인 후 새 PIN 2회 일치, 미설정이면 최초 설정 → `PUT /accounts/me/pin`. ⚠️ **it15에서 비밀번호 개념이 폐지**되어 `ChangePassword`·`accounts.ChangePasswordAsync`는 존재하지 않는다.
  - ⚠️ **계정 생성 UI는 it15에서 폐지**됐다(팝오버 "계정 생성" 항목·`AccountMode.AccountCreate`·`CreateAccount` 모두 제거). 신규 계정은 Google SSO 최초 로그인 시 서버가 `temp_user`로 자동 생성한다. 순수 규칙 `CreatableRoles`/`CanCreate`는 남아 있으나 프로덕션 호출자가 없다([60 §1.5](./60-auth-accounts-and-roles.md#15-계정-생성-위계-게이트-it15-이후-비활성)).
  - TempUser QR 한도(시간·횟수) 편집은 **admin 전용**(`CanEditTempUserLimits`, `:87`) + 서버 `requireAdmin`(it13).
  - 사용자 관리(`UserMgmtViewModel`): 목록 로드, 삭제(cascade=프레임 문서+Storage; 자기 계정 삭제 방지), **타 계정 PIN 재설정**(관리자가 새 4자리 PIN을 2회 입력 — 고정값 아님), **역할 변경 콤보**. 뒤로=관리자 도구(Account) 복귀.
    - **삭제는 행위자와 같거나 낮은 위계에만 노출**(`UserRole.CanManage`·`RoleActionVis` — 예: manager는 admin 삭제 불가). UI 미노출 + 명령 가드 + 서버 최종 강제(403 우아 처리).
    - **PIN 재설정은 파워 전용 + 엄격히 낮은 위계**: `CanResetPin = !isSelf && actorRole.CanResetPin(target)`(`:70`, 커맨드 가드 `:204`) = `IsPower() && ManageRank(target) < ManageRank(actor)`. 즉 **매니저는 다른 매니저의 PIN을 재설정할 수 없고 관리자만 가능**하다(동급 차단). 서버 `PUT /accounts/:id/pin`은 `requirePower()` + `canResetPin`으로 동일 판정(위반 403). `CanManage`(삭제와 공유)는 동급 허용 그대로다.
    - **역할 변경(it13 도입 · it16 완화)**: 콤보 옵션은 `RoleChangePolicy.AssignableRoles(actor, current)`가 필터한다 — **admin**은 admin 제외 전부, **manager**는 하위 3역할 대역(임시 유저·사용자·**고급 유저**) 안에서 자유 지정(승격 포함). admin 지정은 누구도 불가(최종 1인), admin 대상 변경도 불가. 자기 계정 행은 콤보 미노출. 전수 표는 [60 §1.4](./60-auth-accounts-and-roles.md#14-역할-지정변경-매트릭스).
- **근거**: `AccountViewModel.cs`, `UserMgmtViewModel.cs`, `UserRole.cs`, `RoleChangePolicy.cs`, `MainWindow.xaml:53-75`. 역할·권한 상세는 [60](./60-auth-accounts-and-roles.md).

## 14. 홈 버튼 · 취소(전 화면)

- **목적**: 어느 화면에서든 홈 복귀·취소.
- **규칙**: 상단바 홈 버튼(홈 화면에선 숨김, `MainWindow.xaml:31-36`) → `GoHomeCommand`→`ReturnHome("사용자 취소")`(로그인 보존). 각 화면 [취소]도 `ReturnHome`. 촬영 데이터는 항상 폐기. 상단바는 Capture/Qr에서 숨김이라 그 화면은 자체 취소 버튼(`CaptureView.xaml:40-43`) 제공.
- **근거**: `AppShellViewModel.cs:276-277`, 각 VM `Cancel` 커맨드.

## 15. 앱 이름·소제목 브랜딩

- **목적**: 고객사별 앱 표시명 커스터마이즈.
- **규칙**: `App.OnStartup`이 `AppName`·`Subtitle`을 각각 `Resources["Branding.AppName"]`·`Resources["Branding.Subtitle"]`에 주입(창 생성 전) → `DynamicResource`로 창 제목·홈 타이틀(`HomeView.xaml`, AppName)·홈 소제목(`HomeView.xaml`, Subtitle) 반영. 기본값 AppName="MC Photo", Subtitle="self custom photobooth". 상세는 [12 설정/구성](./12-exe-app-settings-and-config.md) §브랜딩.
- **근거**: `App.xaml.cs`, `App.xaml`, `IniBrandingService.cs`, `HomeView.xaml`.

## 16. 표시 모드(전체화면/창모드)

- **목적**: 키오스크(전체화면) vs 개발/창(창모드) 전환.
- **규칙**: `MainWindow.ApplyDisplaySettings`(`MainWindow.xaml.cs:47-81`)가 `DisplayMode`에 따라 전체화면(WindowStyle None+NoResize+Maximized) 또는 창모드(SingleBorder+CanResize+저장된 `WindowBounds`/중앙)를 적용한다. 설정 저장 시 `AppShellViewModel.RequestApplyDisplayMode`→`DisplayModeApplyRequested` 이벤트→**재시작 없이 즉시 전환**(it9 후속) — 이 성질은 it16에서도 유지된다. 상세는 [12](./12-exe-app-settings-and-config.md) §표시 모드.
- **무엇을 할지는 순수 정책이 결정한다(it16)**: `DisplayApplyPolicy.Decide(target, appliedMode)`(`src/MCPhoto.Core/Settings/DisplayApplyPolicy.cs`)가 `None` / `Fullscreen` / `WindowedRestoreGeometry` 중 하나를 반환하고, `MainWindow`는 그 결과만 실행한다. 판정 기준은 설정값이 아니라 **실제로 창에 적용된 모드**(`_appliedMode`, `null`=아직 적용 전=시작)다.

  | `appliedMode` | 저장·시작 시 `target` | 결과 | 의미 |
  | --- | --- | --- | --- |
  | `null`(시작) | Fullscreen | `Fullscreen` | 시작 시 전체화면 |
  | `null`(시작) | Windowed | `WindowedRestoreGeometry` | 시작 시 ini 기하 복원 |
  | Windowed | Windowed | **`None`** | **버그 수정 지점** — 저장해도 창이 움직이지 않는다 |
  | Fullscreen | Fullscreen | `None` | 무의미한 재적용 제거 |
  | Windowed | Fullscreen | `Fullscreen` | 즉시 전환 |
  | Fullscreen | Windowed | `WindowedRestoreGeometry` | 즉시 전환 + 크기·위치 복원 |

- **`WindowBounds` 캡처 시점**: 종전에는 **창을 닫을 때만** 갱신됐다(`OnClosing`). it16부터 **설정 저장 직전에도** 캡처한다(`AppShellViewModel.WindowBoundsCaptureRequested` → `MainWindow.CaptureWindowBounds`) → ini에 현재 위치가 남고, 전체화면→창모드 복귀가 "사용자가 마지막에 두었던 자리"로 정확해진다. 캡처는 **창모드 + `WindowState.Normal`일 때만** 수행한다. 이벤트 구독 2건(`DisplayModeApplyRequested`·`WindowBoundsCaptureRequested`)은 `OnClosing`에서 `_shell.Dispose()` **전에** 해제한다(누수 방지).
- **해결된 버그(it16)**: 창모드에서 설정을 저장하면 창이 **ini에 남아 있던 과거 위치·크기로 점프**했고, 최대화 상태로 저장하면 `WindowState=Normal` 강제로 **원복**됐다. 원인은 `ApplyDisplaySettings`가 ① 시작 복원과 ② 런타임 모드 변경을 겸하면서 동일 모드 저장에도 기하를 재적용한 것이다. 위 정책 도입으로 **모드가 실제로 바뀔 때만** 창에 손대므로 두 증상이 함께 사라졌다.
  - 대가(의도): 동일 모드 저장 시의 "창 스타일 보정"이 없어졌다. 스타일을 바꾸는 다른 코드가 없어 실사례가 없고, 필요하면 앱 재시작으로 복구된다.
  - 창을 **이동·리사이즈하는 순간**의 실시간 반영은 여전히 하지 않는다(저장 시·종료 시에만 캡처) → [90 §1](./90-roadmap-and-future-work.md#1-알려진-이슈--기술-부채) 이연.
- **근거**: `MainWindow.xaml.cs`, `AppShellViewModel.cs`(`RequestApplyDisplayMode`·`RequestCaptureWindowBounds`), `Core/Settings/DisplayApplyPolicy.cs`, `tests/MCPhoto.Tests/DisplayApplyPolicyTests.cs`. 설계: `docs/design/wpf-it16-advanced-user-role-design.md` §7.

## 17. 진단·상태 화면 (it11 #14)

- **목적**: 관리자 현장 트러블슈팅 — 카메라·ffmpeg·Firebase 상태와 로그 폴더를 한눈에.
- **흐름**: 설정 [고급] → [진단·상태](로그인 전용, 게스트 Disable) → **모달**(별도 AppState 없음) → [로그 폴더 열기]/[닫기].
- **화면·VM·서비스**: `DiagnosticsWindow` · `DiagnosticsViewModel`(Transient — 진입마다 최신 상태) · `IDiagnosticsDialogService`(`CameraTestDialogService` 모달 패턴 재사용) · `ILogFolderService`.
- **표시 4섹션**: 카메라(연결 수·목록, `EnumerateDevices`), ffmpeg(`IsAvailable`·경로), **서버 연결**(백엔드 구성 여부 `IsBackendConfigured`·버킷·base URL·게이트 키 **설정됨/미설정**(값은 절대 미표시)·로그인 계정), 로그(경로 상시 표시 + 폴더 열기). 정상=성공색/이상=danger색 트리거. (it15 §6.6 — 종전 "Firebase(서비스 계정 키 경로)" 섹션을 대체)
- **로그 열기**: `explorer.exe`로 `%ProgramData%\MCPhoto\logs` 열기, 실패해도 크래시 없음(로깅). 경로 텍스트 상시 노출(수동 탐색 대체).
- **근거**: `DiagnosticsViewModel.cs`, `DiagnosticsWindow.xaml`, `DiagnosticsDialogService.cs`, `LogFolderService.cs`. 로그 위치 상세 [70](./70-logging-and-troubleshooting.md).

## 18. 앱 버전 표기 (it11, bldinfo.ini)

- **목적**: 실행 중 버전·배포 채널을 항상 확인.
- **규칙**: `bldinfo.ini`(`[General]` Version/BuildDate/Site)를 시작 시 로드(`IBuildInfoService`), `DisplayText`(예 `v1.0.0 · Beta`)를 **앱 하단 우측에 로그인 여부 무관 상시** 노출(흐린 캡션, 클릭 비간섭). 파일/키 부재 시 `v0.0.0` 폴백. (it12 R4: BuildDate는 표기에서 제외 — 업데이트 지연 시 오래된 앱으로 보일 위험. `BuildDate` 프로퍼티·ini 키·로드 로직은 유지)
- **근거**: `MainWindow.xaml`, `AppShellViewModel.cs`(`VersionText`), `IniBuildInfoService.cs`. 파일 규약·배포 상세 [12](./12-exe-app-settings-and-config.md) §6.
