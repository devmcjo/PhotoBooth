# 70 · 로깅 · 이슈 진단

| 항목 | 내용 |
| --- | --- |
| 문서 | 70-logging-and-troubleshooting.md |
| 범위 | 로그 파일 실제 위치·롤링·레벨, 세션 임시/결과 폴더 경로, 전역 예외 처리(크래시 대신 Home 복귀), 증상→의심 지점 매핑(소스만으로 진단), Firebase 초기화 실패 진단, 인앱 진단·상태 화면(it11 #14) |
| 최종 업데이트 | 2026-07-24 |
| 관련 소스 경로 | `src/MCPhoto.App/App.xaml.cs`, `src/MCPhoto.Core/Capture/SessionWorkspace.cs`, `src/MCPhoto.Core/LocalSave/LocalSaveService.cs`, `src/MCPhoto.Core/Settings/{IniSettingsService,SettingsPathResolver,AppSettings}.cs`, `src/MCPhoto.Capture/{OpenCvCameraService,FfmpegRunner,TimelapseService}.cs`, `src/MCPhoto.Firebase/{FirebaseClient,UploadService,FrameRepository}.cs`, `src/MCPhoto.App/ViewModels/{CaptureViewModel,QrPopupViewModel,FrameSelectViewModel}.cs` |
| 갱신 규칙 | Serilog 설정(경로·롤링·레벨, `App.xaml.cs`), `App.DataFolder` 정의, 전역 예외 핸들러, ffmpeg/설정/키 경로 탐색 순서, 각 서비스 로그 문자열이 바뀌면 이 문서를 갱신한다. 증상 매핑 표는 로그 키워드가 바뀔 때 함께 수정. |

관련 문서: [10 Exe 앱 아키텍처](./10-exe-app-architecture.md) · [30 Firebase 연동](./30-backend-firebase-integration.md) · [40 Firestore/Storage 스키마](./40-database-firestore-and-storage-schema.md) · [60 인증/계정/역할](./60-auth-accounts-and-roles.md) · 인덱스 [README](./README.md)

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

> **앱 내에서 열기(it11 #14)**: 로그인 후 **설정 [고급] → [진단·상태]** 모달의 **[로그 폴더 열기]** 버튼으로 위 폴더를 바로 열 수 있다. 같은 모달에서 카메라·ffmpeg·Firebase 상태도 함께 확인 가능([11](./11-exe-app-features.md) §17).

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
| 기본 위치 | 설정 `LocalSavePath` 빈 값 시 `{실행경로}\result\` | 기본 `string.Empty`(`src/MCPhoto.Core/Settings/AppSettings.cs:88`), 런타임 폴백 `Path.Combine(AppContext.BaseDirectory, "result")`(`src/MCPhoto.App/ViewModels/ResultViewModel.cs:141-143`) |
| 세션 폴더명 | `mcphoto_YYMMDD_HHMM` (예: `mcphoto_260720_1445`) | `LocalSaveService.SessionFolderName`(`src/MCPhoto.Core/LocalSave/LocalSaveService.cs:19-21`) |
| 파일 | `final.{jpg\|png}`, `timelapse.mp4`(있을 때만) | `LocalSaveService.SaveAsync`(`:43-54`) |
| 충돌 처리 | 동일 폴더 존재 시 `-2`, `-3`… 접미사 | `MakeUniqueFolder`(`:67-77`) |
| TTL | 없음(영구 보관) | 주석 `:8` "TTL 무관(영구)" |
| 실패 처리 | 경로 쓰기 불가 시 크래시 대신 `null` 반환 + `LogError("로컬 저장 실패: {Path}")` | `:59-64` |

`SaveLocalCopy` off거나 `localSavePath` 미설정이면 `"localSavePath 미설정 — 로컬 저장 건너뜀"`(`LocalSaveService.cs:33`) 후 저장 생략.

---

## 3. 전역 예외 처리 (크래시 대신 Home 복귀)

무인 키오스크라 미처리 예외로 죽지 않고 로그 남긴 뒤 홈으로 복귀하는 안전망이 있다. 세 핸들러는 `OnStartup`에서 등록(`src/MCPhoto.App/App.xaml.cs:57-60`).

| 예외 소스 | 핸들러 | 처리 | 로그 문자열 | 근거 |
| --- | --- | --- | --- | --- |
| UI 스레드(WPF Dispatcher) | `DispatcherUnhandledException` | `e.Handled=true`(크래시 방지) 후 Home 복귀 | `"UI 스레드 미처리 예외 — Home 복귀 시도"` (Error) | `App.xaml.cs:93-98` |
| AppDomain(비 UI 스레드) | `AppDomain.CurrentDomain.UnhandledException` | 로그만(복귀 없음 — 이미 치명적일 수 있음, `IsTerminating` 기록) | `"도메인 미처리 예외 (IsTerminating={Terminating})"` (Error) | `:100-103` |
| 관측 안 된 Task | `TaskScheduler.UnobservedTaskException` | `e.SetObserved()` 후 로그 | `"관측되지 않은 Task 예외"` (Error) | `:105-109` |

- Home 복귀는 `AppShellViewModel.ReturnHome("전역 예외 복구")` 경유(`App.xaml.cs:111-122`), 실패 시 `"Home 복귀 실패"` (Fatal) `:120`. 이 복귀는 **로그아웃하지 않는다**(clearUser 미지정=false; [60 §3.4](./60-auth-accounts-and-roles.md#34-로그아웃--세션-유지-규칙중요) 참조).
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
| 업로드 실패(우아 처리) | `"업로드/QR 실패 — 로컬 보존, 완료 진행 가능"` (Warning) | `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs:82` |
| Firebase 미초기화 | `InvalidOperationException "Firebase 미초기화 — 업로드 불가(QR off/로컬 저장 완화 경로 사용)."` | `src/MCPhoto.Firebase/UploadService.cs:31-32` |
| 전송 미디어 없음 | `InvalidOperationException "전송할 미디어가 없습니다…"` | `UploadService.cs:37-38` |
| 업로드 성공 로그 | `"업로드 완료: session={Token}, page={Url}"` (Information) | `UploadService.cs:76` |
| 버킷 불일치 경고 | `"Storage 버킷 미지정 — 레거시 규약 '{Bucket}'으로 유도함…"` (Warning) | `src/MCPhoto.Firebase/FirebaseClient.cs:73-76` |
| QR 진입 조건 | `EnableQrDelivery` on + (`SendPhoto`/`SendTimelapse` 중 최소 1개). 둘 다 off면 QR 자체 off | `AppSettings.NormalizeQr`(`AppSettings.cs:139-146`); `QrPopupViewModel.cs:45-54` |
| QR 생성 | 업로드 성공 후에만 `QrService.GenerateQrPng(url)` | `QrPopupViewModel.cs:70-72`; `src/MCPhoto.Core/Upload/QrService.cs` |

의심 순서: Firebase 초기화 여부([§6](#6-firebase-초기화-실패-진단)) → `StorageBucket` 실제 값 일치 → `QrPopupViewModel`(비차단 실패 안내, [재시도] 제공). 업로드가 실패해도 흐름은 막지 않고 로컬 결과물은 보존된다(`QrPopupViewModel.cs:80-88`).

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
| 삭제 가능 판정 | `bundle:`·`fallback` 접두는 삭제 불가, 그 외 로컬 저장분만 가능 | `FrameSelectViewModel.IsDeletable`(`src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:54-57`) |
| 삭제 UI 노출 | 로그인 시만(`CanDeleteFrames = user is not null`), 게스트 미노출 | `:67` |
| 로컬 삭제 | 항상 실행 `_localStore.DeleteLocal(frame)`(PNG + `.slots` 삭제, 실패 무시) | `:101`; `src/MCPhoto.Core/Frames/LocalFrameStore.cs:82-94` |
| 서버 삭제 조건 | `"서버에서도 제거" 체크 && IsPower`(파워 전용) | `:102`, `:109-110` |
| 서버 문서 없음 | `"프레임 서버 삭제: 문서 없음 id={Id}…"` (Warning) → 이름 매칭 재삭제 시도 | `src/MCPhoto.Firebase/FrameRepository.cs:85`; `FrameSelectViewModel.cs:127-138` |
| 서버 삭제 실패 안내 | `DeleteNotice`에 실패 노출(성공 오인 금지) + `"프레임 서버 삭제 실패 id={Id}"` (Error) | `FrameSelectViewModel.cs:147-157` |
| Storage 이미지 삭제 실패 | `"프레임 Storage 이미지 삭제 실패(문서는 계속 삭제): {Id}"` (Warning) | `FrameRepository.cs` |

진단: 로컬만 지워지고 서버(공용)에 남는 증상이면 → 파워 권한인지 + "서버에서도 제거" 체크했는지 + 로컬 id와 서버 문서 id 일치 여부(`local:` 접두/`#dbid` 매칭)를 로그로 확인. 파워 삭제 권한 규칙은 [60 §2](./60-auth-accounts-and-roles.md#2-권한-매트릭스화면기능별).

### 4.6 만료 결과물이 안 지워짐 (TTL/Lifecycle)

| 확인 | 값 | 근거 |
| --- | --- | --- |
| 만료 기준 | `resultSessions.expiresAt`(`CreatedAt + RetentionHours`) | `src/MCPhoto.Core/Models/ResultSession.cs:22-23`; `RetentionHours` 기본 24, 1~72(`AppSettings.cs:62`, `:123`) |
| 만료 쿼리 | `expiresAt < now` 문서 조회 | `FirebaseClient.QueryExpiredSessionsAsync`(`src/MCPhoto.Firebase/FirebaseClient.cs:165-187`) |
| 정리 서비스 | `UploadService.PurgeExpiredAsync`(Storage `results/{id}/` 파일 + DB 문서 함께 삭제) | `src/MCPhoto.Firebase/UploadService.cs:80-102` |
| 정리 성공/실패 로그 | `"만료 세션 {Count}건 정리"` (Information, `UploadService.cs:100`) / `"만료 세션 정리 실패: {Id}"` (Warning, `:97`) | `UploadService.cs:97`, `:100` |

**중요(사실)**: 앱 내부에 만료 정리를 자동 구동하는 스케줄러/lifecycle 서비스는 없다. `PurgeExpiredAsync`는 존재하나 **주기 호출 지점이 소스에 정의돼 있지 않다**(호출 스케줄 부재). 따라서 결과물 자동 삭제는 **웹/인프라 측(Firebase 함수·수명주기 규칙 등, 50번대 인프라 문서 소관)**에 의존한다는 것이 코드상 현재 상태다. 로컬 `result\` 폴더는 TTL 무관 영구 보관이므로([§2.2](#22-로컬-저장-결과물-폴더)) "안 지워짐"이 정상이다.

---

## 5. 로그 키워드 빠른 색인

| 키워드(로그) | 의미 | 파일 |
| --- | --- | --- |
| `Firebase 초기화 완료` / `서비스 계정 키 없음` / `Firebase 초기화 실패` | Firebase 상태 | `FirebaseClient.cs:78`, `:50`, `:82` |
| `시드 계정 생성` / `시드 계정 보장 실패` | 시드 admin | `AccountService.cs:114`; `App.xaml.cs:89` |
| `카메라 장치 … 열기 실패` / `촬영 화면: …` | 카메라 | `OpenCvCameraService.cs:82`; `CaptureViewModel.cs:74,84` |
| `ffmpeg 미탑재` / `ffmpeg 실패` / `녹화 시작 실패` | ffmpeg/타임랩스 | `TimelapseService.cs:28`; `FfmpegRunner.cs`; `OpenCvCameraService.cs:276` |
| `업로드 완료` / `업로드/QR 실패` | 업로드/QR | `UploadService.cs:76`; `QrPopupViewModel.cs:82` |
| `설정 저장 …` / `설정 로드 실패` | 설정 INI | `IniSettingsService.cs:42,67,74,122` |
| `프레임 서버 삭제 …` / `cascade 프레임 삭제 실패` | 프레임/계정 삭제 | `FrameRepository.cs`; `AccountService.cs:87` |
| `Home 복귀:` / `미처리 예외` / `IsTerminating` | 전역 예외·복귀 | `AppShellViewModel.cs:204`; `App.xaml.cs:95,102,107` |
| `시작 시 sessions 잔재 … 정리` | 임시 폴더 정리 | `App.xaml.cs:41` |

---

## 6. Firebase 초기화 실패 진단

QR·계정 쓰기·프레임 서버 삭제가 전부 안 되면 근본 원인은 대개 **서비스 계정 키 부재 → `IsInitialized=false`**다.

### 6.1 키 탐색 순서

`FirebaseClient.DefaultKeyPath()`(`src/MCPhoto.Firebase/FirebaseClient.cs:92-102`):

1. `{실행경로}\serviceAccountKey.json` (포터블 편의)
2. 없으면 `C:\ProgramData\MCPhoto\serviceAccountKey.json` (폴백)

둘 다 없으면 ProgramData 경로를 반환하되 파일이 없으므로 미초기화로 진행한다(`:88-89`, `:47-52`). 키는 비밀이라 git/인스톨러에 포함하지 않는다(주석 `:15`, `:91`).

### 6.2 초기화 결과별 로그

| 상황 | 로그 문자열 | 레벨 | 근거 |
| --- | --- | --- | --- |
| 키 파일 없음 | `"서비스 계정 키 없음 — Firebase 미초기화(QR off/오프라인 완화 경로): {Path}"` | Warning | `FirebaseClient.cs:50` |
| 초기화 성공 | `"Firebase 초기화 완료: project={Project}, bucket={Bucket}"` | Information | `:78` |
| 버킷 미지정(성공하나 위험) | `"Storage 버킷 미지정 — 레거시 규약 '{Bucket}'으로 유도함…"` | Warning | `:73-76` |
| 초기화 예외(키 손상·권한 등) | `"Firebase 초기화 실패 — 미초기화로 진행"` | Error | `:82` |

### 6.3 미초기화(`IsInitialized=false`)의 파급

| 기능 | 미초기화 시 동작 | 근거 |
| --- | --- | --- |
| 로그인(일반 계정) | 불가. 시드 `devmcjo/1111`만 인메모리 admin 허용 | `AccountService.cs:35-40` |
| 계정 쓰기(생성/변경/삭제) | `InvalidOperationException "Firebase 미초기화 — 계정 쓰기 불가…"` | `AccountService.cs:126-130` |
| 계정 목록 | 빈 목록 | `AccountService.cs:77` |
| 프레임 목록 | 빈 목록(번들·게스트 모드) | `FrameRepository.cs:32`, `:39` |
| 업로드/QR | `InvalidOperationException` → QrPopup이 우아 처리(로컬 보존) | `UploadService.cs:31-32`; `QrPopupViewModel.cs:82` |

진단 절차:
1. `logs`에서 `"서비스 계정 키 없음"` 또는 `"Firebase 초기화 실패"`를 찾는다. 있으면 키 문제 확정.
2. `{실행경로}\serviceAccountKey.json` 또는 `C:\ProgramData\MCPhoto\serviceAccountKey.json` 존재/유효성 확인.
3. 초기화는 됐는데 업로드만 실패하면 `"Storage 버킷 미지정"` 경고와 설정 `StorageBucket` 값(기본 `mcphoto-955fb.firebasestorage.app`, `AppSettings.cs:110`)이 실제 프로젝트 버킷과 일치하는지 확인. 신규 프로젝트는 `*.firebasestorage.app`, 레거시는 `*.appspot.com`이라 불일치 시 업로드 실패.

계정·역할 관점의 폴백 요약은 [60 §4.5](./60-auth-accounts-and-roles.md#45-미초기화-폴백-요약)를 참조.
