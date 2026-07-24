# 11 · Exe 앱 기능 상세

| 항목 | 내용 |
| --- | --- |
| 문서 | 11-exe-app-features.md |
| 범위 | MCPhoto Exe 앱의 전 사용자 기능(홈·로그인·프레임·촬영·재촬영·컷선택·결과·필터·타임랩스·QR·완료·유휴·설정·카메라테스트·진단·브랜딩·표시모드·버전표기) |
| 최종 업데이트 | 2026-07-24 |
| 관련 소스 경로 | `src/MCPhoto.App/ViewModels/**`, `src/MCPhoto.App/Views/**`, `src/MCPhoto.App/Services/**`, `src/MCPhoto.Core/Capture/**`, `src/MCPhoto.Core/Frames/**`, `src/MCPhoto.Core/LocalSave/**`, `src/MCPhoto.Core/Upload/**` |
| 갱신 규칙 | 기능(화면·플로우·옵션)을 추가/변경할 때 해당 절을 갱신한다. 특히 컷수/필터/QR 토글/삭제 규칙/유휴 시간이 바뀌면 반드시 반영. |

관련 문서: [10 아키텍처](./10-exe-app-architecture.md) · [12 설정/구성/브랜딩](./12-exe-app-settings-and-config.md) · 인덱스 [README](./README.md)

> 각 기능은 **목적 / 사용자 흐름 / 관련 화면·VM·서비스 / 핵심 규칙·옵션 / 근거 파일** 순으로 기술한다.

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
- **흐름**: `FrameSelect` 진입 시 목록 로드 → 카드 선택 → [다음]으로 Guide 진입. [프레임 만들기]로 에디터 진입(로그인 필수). 카드 ✕로 삭제(§4).
- **화면·VM**: `FrameSelectView` · `FrameSelectViewModel`. 서비스: `FrameCatalogService`, `ILocalFrameStore`, `IFrameRepository`.
- **핵심 규칙**:
  - 진입(`OnEnterAsync`, `FrameSelectViewModel.cs:59-80`): 공용 프레임(`catalog.GetDefaultFramesAsync`) + 로그인 시 본인 커스텀(`catalog.GetUserFramesAsync(user.Id)`) 로드, 첫 항목 자동 선택.
  - 목록 우선순위(`FrameCatalogService.GetDefaultFramesAsync`, `FrameCatalogService.cs:45-84`): ① 로컬 공용(번들+파워캐시, 접두 없는 파일) → ② DB `isDefault` 중 **로컬에 이름 없는 것만** 다운로드·캐시(이름 dedup) → ③ 번들 폴더 이미지(slots 없으면 2×2 격자 자동) → ④ 코드 생성 fallback. 오프라인/DB 미초기화 시 ②이하로 폴백.
  - [다음](`FrameSelectViewModel.cs:170-177`): 선택 프레임을 `Session.SelectedFrame`에 고정 + `Session.Capture.Begin(frame, Settings.CutCount)`.
- **근거**: `FrameSelectViewModel.cs`, `FrameCatalogService.cs`.

## 4. 프레임 생성 · 편집(에디터) · 삭제

### 4.1 생성·편집

- **목적**: 이미지 업로드 → 슬롯 배치(개수/종횡비/크기) → 저장. 편집 범위는 **슬롯 배치만**(텍스트/스티커/배경 제외).
- **흐름**: FrameSelect/Settings/Login → [프레임 만들기] → 이미지 로드 → 슬롯 개수(1~6)·종횡비(4:3/3:4/1:1)·크기(70~130%) 지정 → 드래그로 이동 → [저장].
- **화면·VM**: `FrameEditorView`(+ code-behind) · `FrameEditorViewModel`. 서비스: `IFrameRepository`, `ILocalFrameStore`.
- **핵심 규칙**:
  - 이미지 검증(`LoadImage`, `FrameEditorViewModel.cs:63-107`): PNG/JPG/JPEG만, 10MB 이하, 장변 4000 초과 시 축소, PNG로 재인코딩.
  - 자동 배치(`SlotLayout.AutoArrange`, `SlotLayout.cs:23-71`): 세로 스트립(aspect<0.6)=1열, 그 외 격자(4=2×2, 6=2×3 등). 각 셀 안에서 `targetAspect` 유지 최대 사각형 중앙 배치.
  - 크기 스케일(`OnSlotScalePercentChanged`, `:116-125` / `SlotLayout.ScaleSlots`, `:118-134`): 항상 원본 `_baseSlots` 기준으로 스케일(누적 오차 방지), 70~130 클램프, 중심 유지.
  - 드래그(`UpdateSlot`, `:147-172`): 경계 클램프 + `_baseSlots` 중심 동기화. **좌표 변환은 순수함수 `EditorTransform`**(`EditorTransform.cs`)로 표시·드래그·클램프가 동일 변환(Uniform 스케일 + 중앙 레터박스) → WYSIWYG. 캔버스 기준은 `SlotCanvas.ActualWidth/Height`(`FrameEditorView.xaml.cs:70-73`), 절대 위치 이동(그랩 오프셋, `:106-143`).
  - 저장 유효성(`SlotLayout.IsValid`, `:165-175`): 개수 1~6, 경계 내, 겹침 없음.
  - 저장(`Save`) **역할별 분기**:
    - **power**(admin/manager) **신규 생성**: 공용 기본 프레임 → DB(`isDefault=true, userId=null`) + 로컬 캐시(frameId 기반, 접두 없음).
    - **user**: 로컬 전용(DB 미저장), `{계정}_{이름}.png` 접두.
    - 10개 초과 등은 `InvalidOperationException` 메시지 노출.
- **편집 권한 규칙(역할×출처, item2)**: 편집 진입·"선택 편집" 버튼 노출은 순수 함수 `FrameEditPolicy.CanEdit`가 게이트한다.
  - 출처 판정 `FrameOrigin.Classify`(`FrameOrigin.cs`): `local:`=본인 로컬 생성분, 접두 없는 실 DB id+`isDefault`=DB 공용 기본, `bundle:`=번들, `fallback`/빈 Id=코드 생성.
  - **게스트**: 편집 불가(전부). **user**: 본인 로컬 생성분만(`UserId==현재계정` 검증). **power**: 본인 로컬 + DB 공용 기본. **번들·fallback**: 누구도 불가.
  - `FrameSelectViewModel.CanEdit`는 이 순수 함수에 위임(기존 `local:` 무검증 결함 제거). 진입(`EditFrame`)·버튼(`CanEditSelected`) 이중 게이트.
- **power 기본 프레임 편집 저장 플로우(item2 §4)**: power가 DB 공용 기본 프레임을 편집·저장하면 확인 팝업(`IsDbUpdatePromptVisible`) 표시.
  - **[로컬에만 적용]**(`SaveLocalOnly`): DB 미호출, 로컬 공용 캐시만 갱신(`#dbid` 보존).
  - **[DB에도 업데이트]**(`SaveToDb`): `FrameDiff.Compare`(`FrameDiff.cs`, 이미지=SHA-256·슬롯=좌표 정수일치·이름)로 변경 판정 → 변경 있으면 같은 frameId `IFrameRepository.UpdateAsync`(레거시=`SetAsync` 덮어쓰기 / HTTP=`PUT /frames/{id}`) + 로컬 캐시, 이미지 변경 시에만 `replaceImage=true`. **변경 없으면 DB 미호출**(로컬만·"변경 없음" 안내).
  - **[취소]**: 팝업만 닫고 편집 유지(저장·이동 없음). 저장 실패 시 화면 유지 + 안내.
  - 업데이트 대상은 id·`userId(null)`·`isDefault(true)`·`createdAt` 보존, name·slots·imageSize만 갱신(서버 `updateFrame`와 정합).
- **저장소 update capability(item2 §5)**: `IFrameRepository.SupportsUpdateById`(레거시=true, HTTP=true) + `UpdateAsync(frame, imageBytes, replaceImage)`. **레거시·백엔드 양 모드 모두 완전 지원**(HTTP는 `PUT /frames/{id}` 파워 엔드포인트).
- **근거**: `FrameEditorViewModel.cs`, `FrameEditorView.xaml`(+ code-behind), `FrameOrigin.cs`, `FrameEditPolicy.cs`, `FrameDiff.cs`, `IFrameRepository.cs`, `FrameRepository.cs`, `HttpFrameRepository.cs`, `EditorTransform.cs`, `SlotLayout.cs`, `SlotAspect.cs`.

### 4.2 삭제(역할별)

- **목적**: 로컬 항상 삭제 + 파워는 서버(DB+Storage) 동시 삭제 선택.
- **흐름**: 카드 ✕ → 확인 팝업(파워는 "서버에서도 제거" 체크) → [확인].
- **VM**: `FrameSelectViewModel` A3 영역(`:82-168`). 저장소: `ILocalFrameStore.DeleteLocal`, `IFrameRepository.DeleteAsync`.
- **핵심 규칙**:
  - 삭제 가능 판정(`IsDeletable`, `:54-57`): 번들(`bundle:`)·fallback·빈 Id 불가, 그 외(user 로컬 `local:`, 파워 생성/캐시=실 DB id) 가능.
  - 노출 규칙(멀티 컨버터 `FrameDeleteVisibilityConverter`): 게스트 미노출, `local:`=본인 로그인 시 노출, 공용/DB=파워만.
  - `ConfirmDelete`(`:95-111`): 로컬 삭제 **항상** → 파워 & 체크 시 서버 삭제.
  - 서버 삭제(`DeleteFromServerAsync`, `:117-159`): 저장된 DB id(`#dbid`)로 시도 → 실패 시 **이름 매칭 재삭제**(공용 프레임 대비) → 결과를 사용자에게 명확히 안내(성공 오인 금지: 미발견/예외 시 오류 표시).
- **근거**: `FrameSelectViewModel.cs`, `LocalFrameStore.cs`(접두 규칙·`#dbid` 메타), `CommonConverters.cs`(`FrameDeleteVisibilityConverter`).

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
  - 저장(`SaveSettings`, `:188-226`): 필드→AppSettings→`Save()`(내부 Clamp) → `LoadSettings()`로 클램프값 재반영. **성공/실패 정직 표시**(bool 반환, 실패 시 오류 토스트, 성공 오인 금지) + 표시 모드 즉시 적용(`RequestApplyDisplayMode`).
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

- **목적**: 비밀번호 변경(본인), 계정 생성(power), 사용자 관리(power).
- **화면·VM**: `AccountView`(단일 화면, 진입 모드 분기) · `AccountViewModel`; `UserMgmtView` · `UserMgmtViewModel`. 서비스: `IAccountService`.
- **핵심 규칙**:
  - 계정 페이지 모드(`AccountMode`, `AccountViewModel.cs:12-23`): PasswordChange/AccountCreate/Admin. 상단바 팝오버 항목이 지정(`AppShellViewModel.cs:307-317`).
  - 비번 변경(`ChangePassword`, `AccountViewModel.cs:99-128`): 2회 확인, `accounts.ChangePasswordAsync`. PasswordBox는 바인딩 불가라 code-behind 전달.
  - 계정 생성(`CreateAccount`, `:132-169`): **역할 게이트** — acting이 power여야 하고, 생성 가능 역할은 `CreatableRoles`(admin→[User,Manager], manager→[User]; admin→admin 불가, `UserRole.cs:41-51`). 중복/권한/미초기화 예외 안내.
  - 사용자 관리(`UserMgmtViewModel`): 목록 로드, 삭제(cascade=프레임 문서+Storage; 자기/시드 삭제 방지), 비번 초기화("0000"), manager 지정(admin만). **관리 액션은 행위자와 같거나 낮은 역할에만 노출**(`UserRole.CanManage`·`RoleActionVis` — 예: manager는 admin 삭제/초기화 불가), manager 지정은 admin이 user 대상에만. UI 미노출 + 명령 가드 이중 방어. 뒤로=관리자 도구(Account) 복귀.
- **근거**: `AccountViewModel.cs`, `UserMgmtViewModel.cs`, `UserRole.cs`, `MainWindow.xaml:53-78`.

## 14. 홈 버튼 · 취소(전 화면)

- **목적**: 어느 화면에서든 홈 복귀·취소.
- **규칙**: 상단바 홈 버튼(홈 화면에선 숨김, `MainWindow.xaml:31-36`) → `GoHomeCommand`→`ReturnHome("사용자 취소")`(로그인 보존). 각 화면 [취소]도 `ReturnHome`. 촬영 데이터는 항상 폐기. 상단바는 Capture/Qr에서 숨김이라 그 화면은 자체 취소 버튼(`CaptureView.xaml:40-43`) 제공.
- **근거**: `AppShellViewModel.cs:276-277`, 각 VM `Cancel` 커맨드.

## 15. 앱 이름·소제목 브랜딩

- **목적**: 고객사별 앱 표시명 커스터마이즈.
- **규칙**: `App.OnStartup`이 `AppName`·`Subtitle`을 각각 `Resources["Branding.AppName"]`·`Resources["Branding.Subtitle"]`에 주입(창 생성 전) → `DynamicResource`로 창 제목·홈 타이틀(`HomeView.xaml`, AppName)·홈 소제목(`HomeView.xaml`, Subtitle) 반영. 기본값 AppName="MC포토", Subtitle="셀프 포토부스". 상세는 [12 설정/구성](./12-exe-app-settings-and-config.md) §브랜딩.
- **근거**: `App.xaml.cs`, `App.xaml`, `IniBrandingService.cs`, `HomeView.xaml`.

## 16. 표시 모드(전체화면/창모드)

- **목적**: 키오스크(전체화면) vs 개발/창(창모드) 전환.
- **규칙**: `MainWindow.ApplyDisplaySettings`(`MainWindow.xaml.cs:34-63`)가 `DisplayMode`에 따라 전체화면(WindowStyle None+Maximized) 또는 창모드(SingleBorder+저장된 WindowBounds/중앙) 적용. 설정 저장 시 `AppShellViewModel.RequestApplyDisplayMode`→`DisplayModeApplyRequested` 이벤트→**재시작 없이 즉시 재적용**(`MainWindow.xaml.cs:24`). 창 위치는 종료 시 저장(`OnClosing`, `:65-78`). 상세는 [12](./12-exe-app-settings-and-config.md) §표시 모드.
- **근거**: `MainWindow.xaml.cs`, `AppShellViewModel.cs:77-81`.

## 17. 진단·상태 화면 (it11 #14)

- **목적**: 관리자 현장 트러블슈팅 — 카메라·ffmpeg·Firebase 상태와 로그 폴더를 한눈에.
- **흐름**: 설정 [고급] → [진단·상태](로그인 전용, 게스트 Disable) → **모달**(별도 AppState 없음) → [로그 폴더 열기]/[닫기].
- **화면·VM·서비스**: `DiagnosticsWindow` · `DiagnosticsViewModel`(Transient — 진입마다 최신 상태) · `IDiagnosticsDialogService`(`CameraTestDialogService` 모달 패턴 재사용) · `ILogFolderService`.
- **표시 4섹션**: 카메라(연결 수·목록, `EnumerateDevices`), ffmpeg(`IsAvailable`·경로), Firebase(`IsInitialized`·버킷·키 후보 경로 존재여부 `KeyCandidatePaths`), 로그(경로 상시 표시 + 폴더 열기). 정상=성공색/이상=danger색 트리거.
- **로그 열기**: `explorer.exe`로 `%ProgramData%\MCPhoto\logs` 열기, 실패해도 크래시 없음(로깅). 경로 텍스트 상시 노출(수동 탐색 대체).
- **근거**: `DiagnosticsViewModel.cs`, `DiagnosticsWindow.xaml`, `DiagnosticsDialogService.cs`, `LogFolderService.cs`. 로그 위치 상세 [70](./70-logging-and-troubleshooting.md).

## 18. 앱 버전 표기 (it11, bldinfo.ini)

- **목적**: 실행 중 버전·배포 채널을 항상 확인.
- **규칙**: `bldinfo.ini`(`[General]` Version/BuildDate/Site)를 시작 시 로드(`IBuildInfoService`), `DisplayText`(예 `v1.0.0 · Beta`)를 **앱 하단 우측에 로그인 여부 무관 상시** 노출(흐린 캡션, 클릭 비간섭). 파일/키 부재 시 `v0.0.0` 폴백. (it12 R4: BuildDate는 표기에서 제외 — 업데이트 지연 시 오래된 앱으로 보일 위험. `BuildDate` 프로퍼티·ini 키·로드 로직은 유지)
- **근거**: `MainWindow.xaml`, `AppShellViewModel.cs`(`VersionText`), `IniBuildInfoService.cs`. 파일 규약·배포 상세 [12](./12-exe-app-settings-and-config.md) §6.
