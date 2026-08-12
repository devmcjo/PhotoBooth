# 70 · 로깅 · 이슈 진단

| 항목 | 내용 |
| --- | --- |
| 문서 | 70-logging-and-troubleshooting.md |
| 범위 | 로그 파일 실제 위치·롤링·레벨, 세션 임시/결과 폴더 경로, 전역 예외 처리(크래시 대신 Home 복귀), 증상→의심 지점 매핑(소스만으로 진단), **백엔드 연결 실패 진단**, 인앱 진단·상태 화면(it11 #14) |
| 최종 업데이트 | 2026-07-29 (it15·it16 반영 — §4.2·§4.5·§4.6 근거 경로 교체, §5 색인 갱신, **§6 전면 재작성**: Firebase 초기화 → 백엔드 연결 진단) |
| 관련 소스 경로 | `src/MCPhoto.App/App.xaml.cs`, `src/MCPhoto.Core/Capture/SessionWorkspace.cs`, `src/MCPhoto.Core/LocalSave/LocalSaveService.cs`, `src/MCPhoto.Core/Settings/{IniSettingsService,SettingsPathResolver,AppSettings}.cs`, `src/MCPhoto.Capture/{OpenCvCameraService,FfmpegRunner,TimelapseService}.cs`, `src/MCPhoto.Core/Upload/UploadService.cs`, `src/MCPhoto.Http/{HttpBackendClient,HttpFirebaseClient,HttpFrameRepository,HttpQrUsageService}.cs`, `src/MCPhoto.App/ViewModels/{CaptureViewModel,QrPopupViewModel,FrameSelectViewModel,DiagnosticsViewModel}.cs` |
| 갱신 규칙 | Serilog 설정(경로·롤링·레벨, `App.xaml.cs`), `App.DataFolder` 정의, 전역 예외 핸들러, ffmpeg/설정/키 경로 탐색 순서, 각 서비스 로그 문자열이 바뀌면 이 문서를 갱신한다. 증상 매핑 표는 로그 키워드가 바뀔 때 함께 수정. |

관련 문서: [10 Exe 앱 아키텍처](./10-exe-app-architecture.md) · [30 백엔드 API 연동](./30-backend-firebase-integration.md) · [40 Firestore/Storage 스키마](./40-database-firestore-and-storage-schema.md) · [60 인증/계정/역할](./60-auth-accounts-and-roles.md) · 인덱스 [README](./README.md)

> ⚠️ **로그 경로·로그 문자열·PowerShell 명령은 Windows 데스크톱 구현 전용이다.** 다른 플랫폼 클라이언트는 자기 로그 위치·문자열을 갖는다.
>
> **다른 플랫폼에서 재사용할 수 있는 부분**: §4의 **증상 → 원인 매핑 논리**(카메라 미준비 / 업로드 실패 / 인코더 부재 / 설정 저장 실패 / 프레임 삭제 실패 / 만료 미삭제)와 §6.3의 **상태코드별 의미**, §6.4의 **미도달 시 파급 표**는 플랫폼 무관하다. 로그 규격(레벨·롤링·금지 항목)은 [41 §8](./41-local-data-and-file-formats.md), 상태코드 계약은 [31 §3](./31-backend-api-reference.md).

---

## 1. 로그 파일 실제 위치 (제일 먼저 볼 것)

### 1.1 경로

로그는 **`%CommonApplicationData%\MCPhoto\logs\`** 폴더에 일자별로 쌓인다. Windows 기본 확장:

```
C:\ProgramData\MCPhoto\logs\mcphoto-YYYYMMDD.log
```

- 데이터 폴더 정의: `App.DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto")`(`src/MCPhoto.App/App.xaml.cs:18-19`). `CommonApplicationData`는 Windows에서 `C:\ProgramData`다. Program Files를 피해 쓰기 가능한 위치를 고른 것(주석 `:17`).
- 로그 파일 베이스: `Path.Combine(DataFolder, "logs", "mcphoto-.log")`(`src/MCPhoto.App/App.xaml.cs:31`). Serilog의 일자 롤링이 `mcphoto-.log`의 `-` 뒤에 날짜를 끼워 `mcphoto-20260723.log` 형태로 만든다.

> `ProgramData`는 기본 숨김 폴더다. 탐색기 주소창에 `%ProgramData%\MCPhoto\logs` 또는 `C:\ProgramData\MCPhoto\logs`를 직접 입력하면 열린다.

> **앱 내에서 열기(it11 #14)**: 로그인 후 **설정 [고급] → [진단·상태]** 모달의 **[로그 폴더 열기]** 버튼으로 위 폴더를 바로 열 수 있다. 같은 모달에서 카메라·ffmpeg·**서버 연결**(백엔드 구성 여부·주소·버킷·게이트 키 설정 여부·로그인 계정) 상태도 함께 확인 가능([11](./11-exe-app-features.md) §17).

### 1.2 콘솔/탐색기에서 로그 폴더 여는 법(사용자 안내)

| 방법 | 명령 |
| --- | --- |
| PowerShell — 폴더 열기 | `explorer "$env:ProgramData\MCPhoto\logs"` |
| PowerShell — 최신 로그 열기 | `Get-ChildItem "$env:ProgramData\MCPhoto\logs\mcphoto-*.log" \| Sort-Object LastWriteTime -Descending \| Select-Object -First 1 \| ForEach-Object { notepad $_.FullName }` |
| PowerShell — 실시간 보기 | `Get-Content "$env:ProgramData\MCPhoto\logs\mcphoto-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50` |
| cmd | `explorer "%ProgramData%\MCPhoto\logs"` |
| 실행(Win+R) | `%ProgramData%\MCPhoto\logs` 붙여넣고 확인 |

### 1.3 롤링·보관·레벨

Serilog 설정: `src/MCPhoto.App/App.xaml.cs:32-35`

| 항목 | 값 | 근거 |
| --- | --- | --- |
| 최소 레벨 | `Information` (Information/Warning/Error/Fatal 기록, Debug/Verbose 제외) | `App.xaml.cs:33` |
| 롤링 주기 | `RollingInterval.Day` (하루 1파일) | `:34` |
| 보관 개수 | `retainedFileCountLimit: 14` (최근 14개 유지, 오래된 것 자동 삭제) | `:34` |
| 싱크 | 파일 전용(`WriteTo.File`) — 콘솔 싱크 없음 | `:34` |
| 종료 시 | `Log.CloseAndFlush()`(버퍼 플러시) | `:126` |

DI 로깅은 이 Serilog 로거를 유일 provider로 사용한다(`ClearProviders()` 후 `AddSerilog`, `src/MCPhoto.App/App.xaml.cs:48-52`). 따라서 각 서비스/VM의 `ILogger<T>._logger` 출력은 전부 위 파일로 모인다.

---

## 2. 세션 임시 폴더 · 로컬 결과물 폴더

### 2.1 세션 작업(임시) 폴더

| 항목 | 값 | 근거 |
| --- | --- | --- |
| 루트 | `C:\ProgramData\MCPhoto\sessions\` | `SessionWorkspace.SessionsRoot(DataFolder)`(`src/MCPhoto.Core/Capture/SessionWorkspace.cs:14-15`) |
| 개별 세션 | `sessions\{GUID}\` (`session.mp4` 등 임시 산출물) | `src/MCPhoto.App/ViewModels/CaptureViewModel.cs:90-93` |
| 생성 시점 | 촬영 진입(`CaptureViewModel.OnEnterAsync`, Ready 이후) | `CaptureViewModel.cs:89-92` |
| 정리(정상) | 세션 종료·리셋 시 개별 폴더 삭제 | `SessionContext.Reset` → `TryCleanupWorkFolder`(`src/MCPhoto.App/SessionContext.cs:75`, `:82-91`) |
| 정리(잔재) | 앱 시작 시 `sessions` 루트의 잔여 하위 항목 일괄 삭제(비정상 종료 대비) | `SessionWorkspace.CleanupOnStartup`(`SessionWorkspace.cs:21-38`), 호출·로그 `App.xaml.cs:40-41` (`"시작 시 sessions 잔재 {Count}건 정리"`) |

**중요**: `sessions`만 정리 대상이며 `result\`(로컬 저장분)·`logs\`는 절대 지우지 않는다(`SessionWorkspace.cs:7`, `:17-18`). 개별 삭제 실패(사용 중 파일 등)는 무시하고 최대한 정리한다(`:29`, `:34`).

### 2.2 로컬 저장 결과물 폴더

| 항목 | 값 | 근거 |
| --- | --- | --- |
| 기본 위치 | 설정 `LocalSavePath` 빈 값 시 **`%ProgramData%\MCPhoto\result\`**(it26 이관 — 종전 `{실행경로}\result\`) | 기본 `string.Empty`(`src/MCPhoto.Core/Settings/AppSettings.cs`), 해석 `LocalSavePathResolver.Resolve`(`src/MCPhoto.Core/LocalSave/LocalSavePathResolver.cs`), 호출 `ResultViewModel.Next` |
| 세션 폴더명 | `mcphoto_YYMMDD_HHMM` (예: `mcphoto_260720_1445`) | `LocalSaveService.SessionFolderName`(`src/MCPhoto.Core/LocalSave/LocalSaveService.cs:19-21`) |
| 파일 | `final.{jpg\|png}`, `timelapse.mp4`(있을 때만) | `LocalSaveService.SaveAsync`(`:43-54`) |
| 충돌 처리 | 동일 폴더 존재 시 `-2`, `-3`… 접미사 | `MakeUniqueFolder`(`:67-77`) |
| TTL | 없음(영구 보관) | 주석 `:8` "TTL 무관(영구)" |
| 실패 처리 | 경로 쓰기 불가 시 크래시 대신 `null` 반환 + `LogError("로컬 저장 실패: {Path}")` | `:59-64` |

`SaveLocalCopy` off거나 `localSavePath` 미설정이면 `"localSavePath 미설정 — 로컬 저장 건너뜀"`(`LocalSaveService.cs:33`) 후 저장 생략.

**it26 시작 시 진단 Warning 2건**(둘 다 파일을 만들거나 옮기지 않는다 — 사실만 남긴다, `App.LogWritablePathWarnings`):

| 로그 | 뜻 | 조치 |
| --- | --- | --- |
| `설정 파일이 설치 폴더에 있습니다: {Path} — 승격 실행 여부에 따라 설정이 갈릴 수 있습니다` | ini가 `%ProgramFiles%`(x86 포함) 하위다 = **승격 실행**으로 만들어진 파일이다. 비승격 실행은 그 위치에 못 써 `%ProgramData%`의 다른 ini를 읽는다 → "설정을 바꿨는데 반영되지 않는다"의 원인 | 실행 방식을 하나로 고정한다(설치본은 **비승격이 정상**). 진단 모달의 "설정 파일 경로" 행이 지금 쓰는 파일을 그대로 보여 준다 |
| `이전 버전이 설치 폴더에 저장한 결과물이 있습니다: {Old} — 새 저장 위치는 {New}입니다` | it26 이전 버전이 `{실행경로}\result`에 남긴 **손님 사진**이 있다. 앱은 그것을 옮기지도 지우지도 않는다(제거 시에도 보존) | 필요하면 **운영자가 수동 복사**한다. 앱이 손님 사진을 옮기는 코드를 갖지 않는 것이 유실 0의 근거다 |

폴더 열기 실패는 `폴더 열기 실패: {Path}` 또는 `폴더가 없어 열 수 없습니다: {Path}`(둘 다 Warning, `FolderOpener`) + 유휴 팝업 안 캡션으로 경로가 노출된다(수동 탐색 가능). 잠금 키오스크에서 `explorer.exe`가 정책으로 차단되는 경우가 대표적이다.

---

## 3. 전역 예외 처리 (크래시 대신 Home 복귀)

무인 키오스크라 미처리 예외로 죽지 않고 로그 남긴 뒤 홈으로 복귀하는 안전망이 있다. 세 핸들러는 `OnStartup`에서 등록(`src/MCPhoto.App/App.xaml.cs:57-60`).

| 예외 소스 | 핸들러 | 처리 | 로그 문자열 | 근거 |
| --- | --- | --- | --- | --- |
| UI 스레드(WPF Dispatcher) | `DispatcherUnhandledException` | `e.Handled=true`(크래시 방지) 후 Home 복귀 | `"UI 스레드 미처리 예외 — Home 복귀 시도"` (Error) | `App.xaml.cs:93-98` |
| AppDomain(비 UI 스레드) | `AppDomain.CurrentDomain.UnhandledException` | 로그만(복귀 없음 — 이미 치명적일 수 있음, `IsTerminating` 기록) | `"도메인 미처리 예외 (IsTerminating={Terminating})"` (Error) | `:100-103` |
| 관측 안 된 Task | `TaskScheduler.UnobservedTaskException` | `e.SetObserved()` 후 로그 | `"관측되지 않은 Task 예외"` (Error) | `:105-109` |

- Home 복귀는 `AppShellViewModel.ReturnHome("전역 예외 복구")` 경유(`App.xaml.cs:111-122`), 실패 시 `"Home 복귀 실패"` (Fatal) `:120`. 이 복귀는 **로그아웃하지 않는다**(clearUser 미지정=false; [60 §3.5](./60-auth-accounts-and-roles.md#35-로그아웃--세션-유지-규칙중요) 참조).
- 화면 진입/이탈 예외도 셸이 잡아 로그만 남기고 진행: `"화면 이탈 오류: {State}"`·`"화면 진입 오류: {State}"`(`src/MCPhoto.App/AppShellViewModel.cs:133`, `:143`).
- 유휴 만료 시 Home 복귀는 `"Home 복귀: {Reason} (clearUser={Clear})"`(Information, `AppShellViewModel.cs:204`).

핵심(사실): **UI 스레드 예외만 자동 복구**된다. AppDomain 예외는 로그만 남으므로, 앱이 실제로 죽었다면 `logs`에서 `"도메인 미처리 예외"`(특히 `IsTerminating=True`)를 우선 확인한다.

---

## 4. 이슈 위치 파악 가이드 (증상 → 의심 지점, 소스만으로)

로그에서 아래 "로그 키워드"를 grep하면 해당 서비스로 바로 좁혀진다. PowerShell 예: `Select-String -Path "$env:ProgramData\MCPhoto\logs\*.log" -Pattern "카메라"`.

### 4.1 카메라가 안 뜸 / 촬영 화면 로딩만 계속

| 확인 | 값 | 근거 |
| --- | --- | --- |
| 상태 열거형 | `CameraLoadState { Initializing, Ready, Failed }` | `src/MCPhoto.App/ViewModels/CaptureViewModel.cs:11-16` |
| 장치 열기 실패 로그 | `"카메라 장치 {Index} 열기 실패"` (Warning) | `src/MCPhoto.Capture/OpenCvCameraService.cs:82` |
| 촬영 화면 미연결 | `"촬영 화면: 카메라 미연결"` (Warning), 화면 "카메라를 찾을 수 없습니다." | `CaptureViewModel.cs:73-74` |
| 안정 프리뷰 타임아웃 | `"촬영 화면: 안정적 프리뷰 타임아웃"` (Warning), 8초 후(`CameraReadyTimeoutMs=8000`) | `CaptureViewModel.cs:26`, `:84` |
| 진단 포인트 | OpenCV `VideoCapture(deviceIndex, DSHOW)` 오픈 실패 또는 첫 안정 프레임 미수신. 설정 `CameraDevice` 인덱스 확인(`AppSettings.CameraDevice`, 기본 0) | `OpenCvCameraService.cs:79-86`; `AppSettings.cs:98-99` |

의심 파일: `OpenCvCameraService.cs`(장치 열기·프레임 루프) → `CaptureViewModel.cs`(Ready 게이트·타임아웃).

### 4.2 QR이 안 나옴 / 업로드 실패

| 확인 | 값 | 근거 |
| --- | --- | --- |
| 업로드 실패(우아 처리) | `"업로드/QR 실패 — 로컬 보존, 완료 진행 가능"` (Warning) | `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs:117` |
| **TempUser 한도 초과** | `"TempUser QR 한도 초과({Reason}) — 로컬 보존, 완료 진행 가능"` (Information). 화면엔 사유별 문구(시간/횟수) | `QrPopupViewModel.cs:101-112` |
| 백엔드 미구성 | `InvalidOperationException "Firebase 미초기화 — 업로드 불가(QR off/로컬 저장 완화 경로 사용)."` — 실제 의미는 **`BackendBaseUrl` 미설정**(예외 문구는 레거시 표현) | `src/MCPhoto.Core/Upload/UploadService.cs:32-33` |
| 백엔드 미도달 | `"백엔드 요청 실패(네트워크/타임아웃): {Method} {Url}"` (Warning) → `InvalidOperationException("백엔드에 연결할 수 없습니다.")` | `src/MCPhoto.Http/HttpBackendClient.cs:139-140` |
| 파일 PUT 실패 | `"파일 PUT 실패(네트워크)"` (Warning) 또는 `InvalidOperationException("파일 업로드에 실패했습니다(HTTP {code}).")` — 서명 URL 만료(15분)·권한 문제 의심 | `src/MCPhoto.Http/HttpFirebaseClient.cs:221`, `:227-229` |
| 전송 미디어 없음 | `InvalidOperationException "전송할 미디어가 없습니다…"`(앱)·서버도 commit에서 동일 불변식 강제 | `UploadService.cs:38-39`, `web/functions/src/services/uploads.ts:172-176` |
| 업로드 성공 로그 | `"업로드 완료: session={Token}, page={Url}"` (Information) | `UploadService.cs:87` |
| QR 진입 조건 | `EnableQrDelivery` on + (`SendPhoto`/`SendTimelapse` 중 최소 1개). 둘 다 off면 QR 자체 off | `AppSettings.NormalizeQr`; `QrPopupViewModel.cs` |
| QR 생성 | 업로드 성공 후에만 `QrService.GenerateQrPng(url)` | `QrPopupViewModel.cs:94-97`; `src/MCPhoto.Core/Upload/QrService.cs` |

의심 순서: 백엔드 구성·도달 여부([§6](#6-백엔드-연결-실패-진단)) → 게이트 키(`X-MCPhoto-Client`) 유효성(401) → 계정 한도(403) → `QrPopupViewModel`(비차단 실패 안내, [재시도] 제공). 업로드가 실패해도 흐름은 막지 않고 로컬 결과물은 보존된다(`QrPopupViewModel.cs:113-118`).

### 4.3 타임랩스가 없음 / 영상 미생성

| 확인 | 값 | 근거 |
| --- | --- | --- |
| ffmpeg 탐색 순서 | `{실행경로}\tools\ffmpeg\ffmpeg.exe` → `{실행경로}\ffmpeg.exe` → PATH의 `"ffmpeg"` | `FfmpegRunner.ResolveFfmpegPath`(`src/MCPhoto.Capture/FfmpegRunner.cs:35-48`) |
| ffmpeg 존재 여부 | `IsAvailable => File.Exists(_ffmpegPath)` | `FfmpegRunner.cs:30` |
| 타임랩스 미탑재 | `"ffmpeg 미탑재 — 타임랩스 생성 불가: {Path}"` (Warning) | `src/MCPhoto.Capture/TimelapseService.cs:28` |
| 녹화 시작 실패 | `"녹화 시작 실패(ffmpeg 미탑재 가능)"` (Warning) | `src/MCPhoto.Capture/OpenCvCameraService.cs:276` |
| ffmpeg 프로세스 실패 | `"ffmpeg 프로세스 시작 실패"` (Error) | `FfmpegRunner.cs:75` |
| ffmpeg 종료코드 실패 | `"ffmpeg 실패(exit={Code}): {Err}"` (Error) | `FfmpegRunner.cs` RunAsync |
| 타임랩스 생성 실패 | `"타임랩스 생성 실패"` (Warning) | `TimelapseService.cs:41` |

가장 흔한 원인(가정): 배포 패키지에 `tools\ffmpeg\ffmpeg.exe` 번들 누락. 실행 폴더에 ffmpeg가 있는지 먼저 확인.

### 4.4 설정이 저장 안 됨 / 값이 안 남음

| 확인 | 값 | 근거 |
| --- | --- | --- |
| INI 경로 폴백 체인 | `{실행경로}\MCPhoto.ini` → `{ProgramData}\MCPhoto\MCPhoto.ini` → `{LocalAppData}\MCPhoto\MCPhoto.ini` | `SettingsPathResolver.DefaultCandidates`(`src/MCPhoto.Core/Settings/SettingsPathResolver.cs:30-36`) |
| 쓰기 판정 | 디렉터리 생성 + 임시파일 쓰기/삭제 성공 여부 | `IniSettingsService.CanWrite`(`src/MCPhoto.Core/Settings/IniSettingsService.cs:95-108`) |
| 폴백 성공 로그 | `"설정 저장 경로 폴백 성공: {Path}"` (Information) | `IniSettingsService.cs:67` |
| 폴백 시도 실패 | `"설정 저장 경로 쓰기 실패(다음 폴백 시도): {Path}"` (Warning) | `:122` |
| 전부 실패 | `"설정 저장 실패(모든 폴백 경로 쓰기 불가)"` (Error) | `:74` |
| 로드 실패 | `"설정 로드 실패, 기본값 사용: {Path}"` (Warning) | `:42` |

진단: 저장 성공 시 어느 경로가 승격됐는지 `"폴백 성공"` 로그로 확인(`IniSettingsService.IniPath`). 실행 폴더가 쓰기 불가(Program Files 설치 등)면 자동으로 ProgramData/LocalAppData로 넘어간다. 값 손상 시에도 크래시 없이 기본값으로 진행하고 `Clamp()`로 허용 범위 보정(`AppSettings.cs:115-133`).

### 4.5 프레임 삭제가 안 됨

| 확인 | 값 | 근거 |
| --- | --- | --- |
| 삭제 가능 판정 | `bundle:`·`fallback`·빈 Id는 삭제 불가(출처 판정) | `FrameSelectViewModel.IsDeletable`(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:62`) |
| 삭제 UI 노출 | **it16**: `CanDeleteFrames = Role.CanWriteFrames()`(고급 유저 이상). `user`·`temp_user`·게스트는 미노출 | `:81`; [60 §1.2](./60-auth-accounts-and-roles.md) |
| 권한 게이트(커맨드) | `FrameEditPolicy.CanDelete(frame, role) && IsDeletable(frame)` 이중 판정 | `:109` |
| 로컬 삭제 | 항상 실행(PNG + `.slots`). 실패 시 `"로컬 프레임 삭제 실패: {Name} ({Path})"` (Warning) | `:141`; `src/MCPhoto.Core/Frames/LocalFrameStore.cs` |
| 서버 삭제 조건 | `"서버에서도 제거" 체크 && IsPower`(manager/admin). 서버도 `requirePower()`로 재검증 | `:115`; `web/functions/src/routes/frames.ts:120-132` |
| 서버 id 불일치 | `"서버 삭제 id 불일치 → 이름 매칭 재삭제: {Name} (id={Id})"` (Information) | `:170` |
| 서버 문서 없음 | `"서버 삭제 실패: 문서 미발견 name={Name} triedId={Id}"` (Warning) + `DeleteNotice`에 안내 | `:182-184` |
| 서버 삭제 실패 | `"프레임 서버 삭제 실패 id={Id}"` (Error) + `DeleteNotice`에 실패 노출(성공 오인 금지) | `:190-192` |

- 서버 삭제는 `DELETE /frames/{id}` → `{deleted:bool}`이며, **없는 문서를 지웠을 때 성공으로 오인하지 않는다**([30 §7](./30-backend-firebase-integration.md)).
- 진단: 로컬만 지워지고 서버(공용)에 남는 증상이면 → 파워 권한인지 + "서버에서도 제거" 체크했는지 + 로컬 id와 서버 문서 id 일치 여부를 로그로 확인. 403이면 권한, 404면 id 불일치다. 권한 규칙은 [60 §1.2·§2](./60-auth-accounts-and-roles.md).

### 4.6 만료 결과물이 안 지워짐 (TTL/Lifecycle)

| 확인 | 값 | 근거 |
| --- | --- | --- |
| 만료 기준 | `resultSessions.expiresAt`(`createdAt + retentionHours`, **서버가 commit 시 기록**) | `web/functions/src/services/uploads.ts:190-200`; `RetentionHours` 기본 24, 1~72(`AppSettings.cs`) |
| 앱 정리 코드 | `UploadService.PurgeExpiredAsync`는 Core에 남아 있으나 **앱 런타임 호출부 0**(테스트만) | `src/MCPhoto.Core/Upload/UploadService.cs:100-122` |
| HTTP 경로 지원 | 만료 조회·문서 삭제·Storage prefix 삭제는 **`NotSupportedException`** — 서버에 해당 엔드포인트가 없다 | `src/MCPhoto.Http/HttpFirebaseClient.cs:163-176` |
| 실제 정리 주체 | GCS Object Lifecycle(파일, `results/` age 3일) + Firestore 네이티브 TTL(문서, `expiresAt`) | `web/lifecycle.json`, `web/OPS-ttl.md`; [50](./50-infra-gcp-lifecycle-and-ttl.md) |

**중요(사실)**: 앱에도 서버에도 만료 정리를 구동하는 코드 경로가 없다. 결과물 자동 삭제는 **전적으로 인프라(Lifecycle·TTL)** 소관이다. 로컬 `result\` 폴더는 TTL 무관 영구 보관이므로([§2.2](#22-로컬-저장-결과물-폴더)) "안 지워짐"이 정상이다.

---

## 5. 로그 키워드 빠른 색인

| 키워드(로그) | 의미 | 파일 |
| --- | --- | --- |
| `백엔드 요청 실패(네트워크/타임아웃)` | 백엔드 미도달(모든 API 공통) | `HttpBackendClient.cs:139` |
| `백엔드 헬스 체크 실패` | `/health` 프로브 실패(진단 모달) | `HttpFirebaseClient.cs:66` |
| `파일 PUT 실패(네트워크)` / `프레임 이미지 PUT 실패(네트워크)` | 서명 URL 직접 업로드 실패 | `HttpFirebaseClient.cs:221`; `HttpFrameRepository.cs:157` |
| `QR 사용량 조회 실패 … fail-open` | TempUser 한도 조회 실패(허용 후 서버가 최종 거부) | `HttpQrUsageService.cs:40,46` |
| `TempUser QR 한도 초과` | 서버가 업로드 거부(403, 사유 time/count) | `QrPopupViewModel.cs:105` |
| `카메라 장치 … 열기 실패` / `촬영 화면: …` | 카메라 | `OpenCvCameraService.cs:82`; `CaptureViewModel.cs:74,84` |
| `ffmpeg 미탑재` / `ffmpeg 실패` / `녹화 시작 실패` | ffmpeg/타임랩스 | `TimelapseService.cs:28`; `FfmpegRunner.cs`; `OpenCvCameraService.cs:276` |
| `업로드 완료` / `업로드/QR 실패` | 업로드/QR | `UploadService.cs:87`; `QrPopupViewModel.cs:117` |
| `설정 저장 …` / `설정 로드 실패` | 설정 INI | `IniSettingsService.cs:42,67,74,122` |
| `서버 삭제 id 불일치` / `서버 삭제 실패` / `프레임 서버 삭제 실패` | 프레임 삭제 | `FrameSelectViewModel.cs:170,184,192` |
| `브랜딩 로드` / `브랜딩 설정 파일 없음` | 브랜딩(branding.ini) | `IniBrandingService.cs:35,50` |
| `Home 복귀:` / `미처리 예외` / `IsTerminating` | 전역 예외·복귀 | `AppShellViewModel.cs`; `App.xaml.cs:95,102,107` |
| `시작 시 sessions 잔재 … 정리` | 임시 폴더 정리 | `App.xaml.cs:41` |

> ⚠️ 폐지된 키워드(로그에 더 이상 나오지 않음): `Firebase 초기화 완료`·`서비스 계정 키 없음`·`Firebase 초기화 실패`·`시드 계정 생성`. 모두 it15에서 삭제된 `MCPhoto.Firebase`·시드 계정 경로의 것이다.

---

## 6. 백엔드 연결 실패 진단

로그인·QR·계정/프레임 서버 작업이 전부 안 되면 근본 원인은 대개 **백엔드 구성 누락 또는 미도달**이다.

> 이 절은 2026-07-29에 전면 재작성됐다. it15에서 `MCPhoto.Firebase`가 삭제돼 **서비스 계정 키·`FirebaseClient`·`IsInitialized`(키 로드 여부) 판정은 존재하지 않는다.** 앱은 백엔드 HTTPS API만 호출한다. 계정 관점의 동작은 [60 §4.5](./60-auth-accounts-and-roles.md#45-백엔드-미도달-시-동작-구-미초기화-폴백-재정의), 계약 전체는 [30](./30-backend-firebase-integration.md).

### 6.1 먼저 확인할 두 값

| 값 | 어디서 오나 | 없으면 |
| --- | --- | --- |
| `BackendBaseUrl` | 코드 내장 기본값(운영 프로젝트) → `MCPhoto.ini`로 오버라이드 | 빈 값이면 **미구성** — `IFirebaseClient.IsInitialized=false`가 되어 업로드가 즉시 예외. 다른 API는 BaseAddress 없이 상대 URL을 조립하지 못해 실패 |
| `BackendApiKey` | publish 시 exe 내장(`-p:BackendApiKeyDefault`) → `MCPhoto.ini`의 `BackendApiKey=`가 우선 | 모든 API가 **401**(`유효한 클라이언트 키가 필요합니다`) |

- 일반 빌드(`dotnet build`)에는 내장 키가 없다 — 개발 PC에서 백엔드를 쓰려면 `MCPhoto.ini`에 `BackendApiKey=`를 넣어야 한다([12 §1.1](./12-exe-app-settings-and-config.md), [80 §2](./80-build-and-deployment.md)).
- ⚠️ `UploadService`의 예외 문구는 아직 `"Firebase 미초기화 — 업로드 불가…"`다(레거시 표현). 실제 의미는 **`BackendBaseUrl` 미설정**이다(`UploadService.cs:32-33`).

### 6.2 인앱 진단 모달에서 볼 것

설정 [고급] → [진단·상태]의 **서버 연결** 섹션(`DiagnosticsViewModel`):

| 표시 | 의미 |
| --- | --- |
| 백엔드 구성 여부 | `BackendBaseUrl`이 설정됐는지(**도달 성공이 아니다**) |
| 백엔드 주소 | 실제 사용 중인 base URL(미설정이면 `(미설정)`) |
| 버킷 | 토큰 URL 조립용 버킷명 |
| 게이트 키 | **설정됨/미설정만** 표시 — 키 값 자체는 절대 노출하지 않는다 |
| 로그인 계정 | 현재 세션 계정(게스트면 비로그인) |

실도달 확인은 `/health` 프로브다 — 실패 시 `"백엔드 헬스 체크 실패"`(Warning) 로그가 남는다(`HttpFirebaseClient.cs:55-69`).

### 6.3 상태코드별 의미

| 코드 | 서버 판정 | 앱 동작 |
| --- | --- | --- |
| 401 | 게이트 키 무효 / Bearer 없음·만료·위조 | 로그인은 `null`(자격 실패), PIN 검증은 `false`, 그 외 `BackendLoginRequiredException(Expired=true)` |
| Bearer 미보유(전송 전) | — | `BackendLoginRequiredException(Expired=false)` |
| 403 | 권한 부족(power·admin·`canManage`) 또는 **TempUser QR 한도 초과** | `UnauthorizedAccessException` / 한도는 `QrLimitExceededException`(사유별 문구) |
| 404 | 대상 없음(문서·엔드포인트) | `InvalidOperationException` |
| 409 | 중복(세션 ID 재commit·**프레임 이름**·PIN 미설정) | `InvalidOperationException` |
| 501 | **Google SSO 미구성**(서버에 `GOOGLE_OAUTH_CLIENT_ID` 없음) | `GoogleSsoNotConfiguredException` — 자격 문제·네트워크와 구분됨 |
| 네트워크·타임아웃(100초) | — | `BackendUnavailableException("백엔드에 연결할 수 없습니다.")` + Warning 로그 |
| **서버 주소 미설정** | — (요청을 보내지 않음) | `BackendNotConfiguredException` + Warning 로그 |

근거: `HttpBackendClient.cs`(`SendCoreAsync`·`MapToDomainException`), `HttpAccountService.cs`, `HttpFirebaseClient.cs`.

> **로그에서 오프라인을 찾을 때**: 세 상태가 서로 다른 Warning을 남긴다 —
> `백엔드 주소 미설정:` / `백엔드 요청 실패(네트워크/타임아웃):` / `프레임 이미지 PUT 실패(네트워크)`.
> 사용자 화면 문구는 `BackendFailureMessage`가 조립하므로 **로그 문장과 화면 문장은 다르다**.
> 사용자가 "서버에 연결할 수 없어 저장하지 못했습니다"를 봤다고 신고하면 위 두 번째 Warning을 찾으면 된다.
>
> ⚠️ `PUT /accounts/me/pin`의 401만 예외적으로 일반 `UnauthorizedAccessException`이다(PIN 불일치와 토큰
> 만료가 같은 401이라 단정할 수 없다 — [31 §3.2](./31-backend-api-reference.md)).

### 6.4 미도달의 파급

| 기능 | 동작 | 근거 |
| --- | --- | --- |
| 로그인 | **불가**(오프라인 폴백 없음). 화면 유지 + "네트워크를 확인해 주세요" | [60 §4.5](./60-auth-accounts-and-roles.md) |
| 게스트 촬영·로컬 저장 | **정상 동작** | §2.2 |
| 계정 목록 | 예외 전파("사용자 목록을 불러올 수 없습니다.") — 빈 목록 폴백 없음 | `HttpAccountService.cs:75-88` |
| 프레임 목록 | 공용 프레임은 로컬 캐시·fallback으로 폴백([11 §3](./11-exe-app-features.md)) | `FrameCatalogService` |
| 업로드/QR | 예외 → QrPopup이 우아 처리(로컬 보존, [재시도]) | `QrPopupViewModel.cs:113-118` |
| PIN 게이트 | **fail-closed** — 진입 거부 | [60 §4.5](./60-auth-accounts-and-roles.md) |
| TempUser 한도 조회 | **fail-open** — 앱은 허용, 서버가 업로드에서 최종 거부 | `HttpQrUsageService.cs:37-48` |

### 6.5 진단 절차

1. `logs`에서 `"백엔드 요청 실패(네트워크/타임아웃)"`를 찾는다 → 있으면 네트워크·주소 문제.
2. 없고 401이면 게이트 키 문제 → 진단 모달의 "게이트 키" 표시와 `MCPhoto.ini`의 `BackendApiKey` 확인(publish 산출물인지, 일반 빌드인지).
3. 로그인만 실패하고 나머지는 되면 Google SSO 구성(501) 또는 계정 도메인 제한(401)을 의심한다.
4. 업로드만 실패하면 서명 URL PUT 단계(`"파일 PUT 실패(네트워크)"`·HTTP 4xx)와 TempUser 한도(403)를 구분한다.
