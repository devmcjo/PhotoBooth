# MC포토 — WPF 앱 구현 WBS 블루프린트

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln` (.NET 8 WPF) 그린필드 구현 |
| 설계 근거 | `docs/design/wpf-architecture.md`, `docs/design/firebase-contract.md`, `docs/prd/photobooth-prd.md` v2.7 |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-20 |
| 빌드 검증 기준 | `dotnet build MCPhoto.sln -c Release` (error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. 대화 컨텍스트 없는 fresh 에이전트가 해당 Step만 읽고 실행할 수 있도록 작성했다.
> 빌드/검증은 IDE 비의존적으로 `dotnet` CLI 기준. (사용자 IDE는 VS2022이나 검증은 CLI로 통일)

---

## 검증된 사실 (verified facts)

- **VF-1**: 프로젝트는 그린필드 — `E:\Study\photobooth`에 소스 없음(`docs/`, `Example/`, `.claude/`만). (근거: `ls -la E:/Study/photobooth`)
- **VF-2**: 합성은 배경형(프레임=배경, 사진=슬롯 위). `Example/result_frame2.jpg`(4슬롯 2×2)로 실물 확인. (근거: 실물 이미지, PRD §9 #13)
- **VF-3**: 무료 Spark 요금제는 Cloud Storage 사용 불가(2026-02-03~). 파일 업로드는 Blaze 필수. Firestore는 Spark 무료. (근거: Firebase 공식 FAQ)
- **VF-4**: 웹캠 WYSIWYG 파이프라인 1순위 = OpenCvSharp4(Apache-2.0) raw 프레임 가공 + ffmpeg stdin. (근거: 라이브러리 조사)
- **VF-5**: 컷수(최소 6) ≥ 슬롯(최대 6) 항상 성립 → 비활성 로직 불필요. (근거: PRD §9 #12)
- **VF-6**: 웹은 읽기 전용 소비자, ResultSession 단건 get만. (근거: `firebase-contract.md` §0)

## 미검증 가정 (open assumptions)

- **OA-1**: .NET 8 SDK로 WPF 신규 프로젝트 빌드 가능 → **검증: Step 1**
- **OA-2**: INI 설정 저장이 쓰기 가능 위치에서 동작 → **검증: Step 2**
- **OA-3**: 대상 웹캠이 1080p@30fps 제공, OpenCvSharp로 프리뷰 실시간 획득, WriteableBitmap 30fps 렌더 → **검증: Step 3**
- **OA-4**: 전역 예외/유휴 타임아웃이 대기화면 복귀를 실제로 트리거 → **검증: Step 4**
- **OA-5**: 로컬 저장(세션 폴더)이 saveLocalCopy on/off·경로 변경에 정확히 동작 → **검증: Step 5**
- **OA-6**: ffmpeg stdin 파이프로 H.264 mp4 녹화 + setpts 배속 타임랩스 생성 → **검증: Step 6**
- **OA-7**: 슬롯 종횡비 크롭·배경형 합성이 `result_frame2.jpg` 수준으로 왜곡 없이 산출 → **검증: Step 7**
- **OA-8**: Firebase 업로드→ResultSession 생성→토큰 URL→QR이 실제 프로젝트에서 동작(Blaze 전환 필요) → **검증: Step 8**
- **OA-9**: 프레임 편집기의 슬롯 경계 제약·자동 배치가 프레임 밖 이탈/겹침을 방지 → **검증: Step 10**
- **OA-10**: 계정 CRUD·역할 권한·cascade 삭제가 정확히 동작 → **검증: Step 11**

> 모든 미검증 가정이 검증 Step에 매핑됨 (완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (솔루션 스캐폴드)
  ├─ Step 2 (설정/INI)         ─┐
  ├─ Step 3 (캡처 프리뷰)       ─┤
  ├─ Step 5 (로컬 저장)         ─┤ (2·3·5는 Step1 후 병렬 가능)
  └─ Step 4 (셸/상태머신/예외)  ─┘  ← Step 2 선호(설정 로드)
Step 3 → Step 6 (녹화/타임랩스)
Step 3 → Step 7 (촬영·크롭·합성)   ← Step 2(설정) 필요
Step 7 → Step 9 (촬영 플로우 화면 통합)  ← Step 4(셸)
Step 8 (Firebase 업로드+QR)        ← Step 7(결과물), Step 2(설정)
Step 10 (프레임 편집기)            ← Step 4(셸), Step 11(계정, 소유)
Step 11 (계정/권한/관리자)         ← Step 4(셸), Step 8(Firestore 인프라)
Step 12 (인스톨러/번들/통합 리허설) ← 전 단계
```

---

## Step 1: 솔루션 스캐폴드 & 프로젝트 구조

- **Context Brief**: 그린필드다. `E:\Study\photobooth`에 소스가 없다(`docs/`만 존재). .NET 8 WPF 솔루션과 프로젝트 골격을 만들고 MVVM/DI 기반을 세운다. 이후 모든 Step의 토대.
- **대상 파일**: `MCPhoto.sln`, `src/MCPhoto.App/MCPhoto.App.csproj`(+ `App.xaml`/`App.xaml.cs`/`MainWindow.xaml`), `src/MCPhoto.Core/MCPhoto.Core.csproj`, `src/MCPhoto.Capture/MCPhoto.Capture.csproj`, `src/MCPhoto.Firebase/MCPhoto.Firebase.csproj`, `tests/MCPhoto.Tests/MCPhoto.Tests.csproj`, `Directory.Build.props`(공통 TFM/Nullable/LangVersion), `.gitignore`.
- **선행 조건**: 없음.
- **구현 내용**:
  - `dotnet new sln`. `MCPhoto.App`=`net8.0-windows`(`UseWPF=true`), `Core`=`net8.0`, `Capture`=`net8.0-windows`, `Firebase`=`net8.0`, `Tests`=`net8.0`(xUnit).
  - NuGet: App에 `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging` + 파일 싱크. Capture에 `OpenCvSharp4.Windows`, `OpenCvSharp4.WpfExtensions`. App에 `QRCoder`. Firebase에 `FirebaseAdmin`(MVP 1차) + `Google.Cloud.Firestore`/`Google.Cloud.Storage.V1`.
  - `App.xaml.cs`에 DI 컨테이너(`ServiceCollection`) 조립 + `MainWindow`를 `AppShell`로 부트스트랩(빈 셸).
  - 서비스 인터페이스 골격(빈 시그니처)만 `Core`에 정의: `ICameraService`, `ICompositionService`, `ITimelapseService`, `IFrameRepository`, `IAccountService`, `IUploadService`, `IQrService`, `ILocalSaveService`, `ISettingsService`, `IIdleWatchdog`.
  - `.gitignore`에 **서비스 계정 키(`*serviceaccount*.json`, `*firebase*credentials*.json`)·`bin/`·`obj/` 제외** 명시(키 유출 방지, architecture §6.4).
- **검증 명령**: `dotnet build MCPhoto.sln -c Release` / `dotnet test`(빈 테스트 1개 통과).
- **완료 기준**:
  - [관측] `dotnet build -c Release` error 0, 프로젝트 warning 0. `dotnet run --project src/MCPhoto.App` 실행 시 빈 창(또는 fullscreen) 표시.
  - [non-goal] 이 단계에서 카메라·Firebase·화면 기능 **없음**(인터페이스 시그니처만, 구현은 throw NotImplemented 허용).
  - [trigger] 빌드는 `dotnet build` 실행 시에만. 앱 창은 `dotnet run` 시에만 표시.
- **롤백**: 생성 파일·솔루션 삭제(그린필드라 이전 상태 = 빈 소스).
- [ ] 완료

---

## Step 2: 설정(AppSettings) & INI 저장/복원

- **Context Brief**: 앱의 모든 관리자 설정(촬영 옵션·표시 모드·QR/로컬 저장·보관 시간·카메라 장치)은 로컬 INI 파일에 저장되어 재시작 시 복원된다(PRD §9 #38). 창모드는 마지막 크기·위치까지 복원. 이 설정은 이후 촬영·합성·업로드·화면이 모두 참조하는 공통 상태.
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`(모델), `src/MCPhoto.Core/Settings/ISettingsService.cs`, `src/MCPhoto.Firebase/`(무관), `src/MCPhoto.App/Services/IniSettingsService.cs`(구현), `tests/MCPhoto.Tests/SettingsTests.cs`.
- **선행 조건**: Step 1.
- **구현 내용**:
  - `AppSettings` 필드(PRD §6): cutCount(기본6, 6/8/10 최소6), countdownSec(기본6, 3/6/8/10), mirrorMode(기본on), flashMode(기본off), outputFormat(기본JPG, JPG/PNG), retentionHours(기본24, 1~72), displayMode(기본fullscreen), windowBounds, enableQrDelivery(기본on), saveLocalCopy(기본off), localSavePath(기본`{실행경로}\result\`), cameraDevice, hostingBaseUrl(firebase-contract §3.5).
  - `IniSettingsService`: `%ProgramData%\MCPhoto\MCPhoto.ini`(쓰기 가능) 우선, 없으면 실행 경로. 로드 시 누락 키는 기본값. 저장은 즉시 flush.
  - 값 범위 클램프(cutCount 최소6·slot 최대6은 촬영 설정 옵션 목록으로 제한, retentionHours 1~72 클램프).
- **검증 명령**: `dotnet test --filter SettingsTests` (기본값·저장→로드 라운드트립·범위 클램프 검증).
- **완료 기준**:
  - [관측] 설정 변경→`Save()`→INI 파일에 키 기록→새 인스턴스 `Load()` 시 값 유지. 파일 없을 때 전 항목 기본값 반환. retentionHours=100 저장 시 72로 클램프.
  - [non-goal] 잘못된 INI(손상/누락)여도 예외로 앱 크래시 **금지** — 누락 키는 기본값 폴백.
  - [trigger] 저장은 `Save()` 호출 시에만(입력 중 실시간 파일 쓰기 없음).
- **롤백**: Step2 커밋 revert. INI 파일 삭제 시 기본값 동작(Step1 상태로 무해 복귀).
- [ ] 완료

---

## Step 3: 캡처 파이프라인 — 프리뷰 (핵심 리스크)

- **Context Brief**: 웹캠 하나의 스트림에서 프리뷰+스틸+녹화를 동시 지원해야 한다(PRD §F1/F3). 이 Step은 그 토대인 **프리뷰**만 구현·검증한다. raw 프레임을 백그라운드 루프로 받아 거울반전·중앙크롭 가공 후 재사용 WriteableBitmap으로 30fps 표시(architecture §2). 스틸/녹화는 Step6·7에서 이 파이프라인에 분기로 얹는다.
- **대상 파일**: `src/MCPhoto.Capture/OpenCvCameraService.cs`(`ICameraService` 구현), `src/MCPhoto.Capture/CropCalculator.cs`(슬롯 종횡비 중앙 크롭 ROI 산출), `src/MCPhoto.App/Views/PreviewView.xaml`(+VM), `tests/MCPhoto.Tests/CropCalculatorTests.cs`.
- **선행 조건**: Step 1. (Step 2의 cameraDevice/mirrorMode 참조 선호)
- **구현 내용**:
  - `OpenCvCameraService`: 전용 백그라운드 스레드에서 `VideoCapture(deviceIndex, DSHOW)` → FourCC MJPG, 1920×1080 요청(실패 시 장치 기본 폴백). `Read` 루프.
  - 파이프라인: `if(mirror) Cv2.Flip(FlipMode.Y)` → `CropCalculator`로 대표 슬롯 종횡비 중앙 크롭 ROI 적용 → 가공 프레임 이벤트 발행(`FrameReady`).
  - 프리뷰: 가공 프레임을 `Dispatcher.Invoke`로 UI 스레드 마샬링 → **재사용 WriteableBitmap** `WritePixels`(매 프레임 새 BitmapSource 금지, architecture §2.3). DPI 96, `Stretch=None` 또는 Uniform, NearestNeighbor.
  - `CropCalculator`: (원본 W×H, 목표 종횡비) → 중앙 crop `Rect` 산출. 세로 슬롯이면 좌우 잘라냄, 가로면 상하. 왜곡 없이 crop만.
  - 프레임레이트 로깅(진단): 초당 렌더 프레임 수를 로그.
- **검증 명령**: `dotnet test --filter CropCalculatorTests` (크롭 ROI 수치 검증) + `dotnet run --project src/MCPhoto.App`로 프리뷰 육안 확인(웹캠 연결 시).
- **완료 기준**:
  - [관측] 프리뷰 화면에 웹캠 영상 실시간 표시. mirrorMode on 시 좌우반전, off 시 정방향(육안). 슬롯 종횡비로 중앙 크롭된 화면(왜곡 없음). 로그에 ~30fps 기록. CropCalculator 단위 테스트: 1920×1080 원본에 3:4 목표 시 좌우 크롭 Rect가 중앙 정렬·비율 정확.
  - [non-goal] 이 단계에서 스틸 저장·녹화·합성 **없음**. 카메라 미연결 시 예외로 크래시 **금지**(장치 없음 안내 후 진행).
  - [trigger] 캡처 시작은 프리뷰 화면 진입 시에만. 화면 이탈 시 캡처 스레드 정지(리소스 해제).
- **롤백**: Step3 커밋 revert. CameraService를 no-op 스텁으로 되돌림(Step1 상태).
- [ ] 완료

---

## Step 4: 앱 셸 — 상태 머신 · 화면 전이 · 예외/유휴 복구

- **Context Brief**: 키오스크 앱은 홈→프레임선택→촬영→선택→결과→QR→완료 상태를 전이하며, 무인 동작 중 예외나 유휴(60~90초 무동작) 시 대기화면으로 자동 복귀해야 한다(PRD §4/§8/§10). 이 Step은 화면 전이 뼈대와 안정성 안전망을 만든다(개별 화면 내용은 이후 Step에서 채움).
- **대상 파일**: `src/MCPhoto.Core/Navigation/AppState.cs`(enum: Home/Login/FrameSelect/Guide/Capture/CutSelect/Result/Qr/Done/Admin/UserMgmt/FrameEditor), `src/MCPhoto.App/AppShellViewModel.cs`(상태머신·네비게이션), `src/MCPhoto.App/AppShell.xaml`(방향별 레이아웃 컨테이너), `src/MCPhoto.App/Services/IdleWatchdog.cs`, `src/MCPhoto.App/App.xaml.cs`(전역 예외 핸들러), `tests/MCPhoto.Tests/AppStateTests.cs`.
- **선행 조건**: Step 1. (Step 2 설정 로드 선호 — displayMode/windowBounds 적용)
- **구현 내용**:
  - `AppShellViewModel`: 현재 상태·네비게이션 커맨드(전이 규칙은 PRD §4 흐름). 각 화면은 ContentControl + DataTemplate로 스왑.
  - 가로/세로 레이아웃 자동(화면 방향 감지 → 방향별 템플릿, architecture §4.3). 표시 모드: fullscreen/windowed(창모드 최소 1280×720, windowBounds 복원).
  - `IdleWatchdog`: 촬영/선택/편집 중 무동작 타이머(설정 60~90초) → 만료 시 진행 취소·임시데이터 폐기·Home 복귀 이벤트. 사용자 입력마다 리셋.
  - 전역 예외: `AppDomain.UnhandledException` + `DispatcherUnhandledException` → 로깅 후 Home 복귀(크래시 대신), 임시 데이터 정리.
  - 관리자 진입: 좌상단 3초 롱프레스 → 로그인 화면(Step11에서 실제 인증 연결, 여기선 전이만).
- **검증 명령**: `dotnet test --filter AppStateTests`(전이 규칙·불법 전이 거부) + `dotnet run`으로 화면 스왑·유휴 타임아웃 육안.
- **완료 기준**:
  - [관측] 각 상태 전이가 흐름대로 동작(Home→…→Done→Home). 유휴 타임아웃 만료 시 Home 복귀 + 진행 데이터 폐기. 강제 예외 발생 시 크래시 없이 Home 복귀(로그 기록). 좌상단 3초 롱프레스로 로그인 화면 진입.
  - [non-goal] 유휴 타이머는 사용자 입력이 있으면 리셋 — **입력 중 임의 Home 복귀 금지**. 정상 화면 전이 시 예외 핸들러가 오동작으로 Home 복귀시키지 **않음**.
  - [trigger] Home 복귀는 (a) 유휴 만료, (b) 예외, (c) 세션 완료, (d) 명시적 취소에만. 3초 미만 터치로는 관리자 진입 **안 됨**.
- **롤백**: Step4 커밋 revert. 셸을 Step1의 단일 빈 창으로 되돌림.
- [ ] 완료

---

## Step 5: 로컬 결과물 저장 (saveLocalCopy)

- **Context Brief**: 결과물(최종 이미지+타임랩스)을 QR 전송과 **독립**하게 로컬에 저장하는 옵션(PRD §F4, §9 #34). 기본 off. on이면 세션마다 `{localSavePath}\mcphoto_YYMMDD_HHMM\`에 저장. TTL 무관(영구). QR·로컬 둘 다 off면 미보존. 이 Step은 Blaze/Firebase 없이도 MVP 데모가 가능하게 하는 완화 경로(architecture §6.1)의 핵심.
- **대상 파일**: `src/MCPhoto.App/Services/LocalSaveService.cs`(`ILocalSaveService`), `tests/MCPhoto.Tests/LocalSaveTests.cs`.
- **선행 조건**: Step 1, Step 2(설정).
- **구현 내용**:
  - `saveLocalCopy` on 시: `{localSavePath}`(기본 `{실행경로}\result\`) 아래 `mcphoto_YYMMDD_HHMM\` 폴더 생성 → 최종 이미지(outputFormat)·타임랩스 mp4 저장.
  - `localSavePath` 변경 반영. 경로가 보호 위치/쓰기 불가면 오류 안내(크래시 금지).
  - 파일명 규약(로컬): `final.{jpg|png}`, `timelapse.mp4`(firebase-contract §4.2와 동일 규약 재사용).
- **검증 명령**: `dotnet test --filter LocalSaveTests`(on/off 분기·폴더 생성·경로 변경·중복 폴더 처리).
- **완료 기준**:
  - [관측] saveLocalCopy on + 더미 결과물 저장 호출 시 `{path}\mcphoto_YYMMDD_HHMM\final.jpg` 생성. off 시 파일 생성 **안 됨**. localSavePath 변경 시 새 경로에 저장.
  - [non-goal] 로컬 저장분은 TTL/삭제 루틴 대상 **아님**(이 서비스는 삭제 기능 없음). off일 때 어떤 파일도 남기지 않음.
  - [trigger] 저장은 세션 결과 확정(합성 완료) 후 saveLocalCopy=on일 때만.
- **롤백**: Step5 커밋 revert.
- [ ] 완료

---

## Step 6: 캡처 파이프라인 — 세션 녹화 & 타임랩스 (ffmpeg)

- **Context Brief**: 촬영 세션(첫 컷~마지막 컷) 전체를 녹화하고 배속 타임랩스 mp4(H.264, 무음, 10~15초)를 생성한다(PRD §F3). Step3의 가공 프레임(반전·크롭 반영)을 ffmpeg.exe stdin rawvideo 파이프로 흘려 인코딩한다(architecture §2.5). OpenCvSharp VideoWriter의 H.264 불안정 이슈를 회피하는 구조.
- **대상 파일**: `src/MCPhoto.Capture/FfmpegRunner.cs`, `src/MCPhoto.Capture/TimelapseService.cs`(`ITimelapseService`), `src/MCPhoto.Capture/OpenCvCameraService.cs`(녹화 분기 추가), `tools/ffmpeg/`(번들 바이너리 배치), `tests/MCPhoto.Tests/FfmpegArgsTests.cs`.
- **선행 조건**: Step 3(가공 프레임 파이프라인).
- **구현 내용**:
  - `FfmpegRunner`: `Process`(UseShellExecute=false, RedirectStandardInput=true, CreateNoWindow=true)로 ffmpeg 기동. 녹화 커맨드: `-f rawvideo -pixel_format bgr24 -video_size {W}x{H} -framerate 30 -i - -c:v libx264 -crf 20 -preset veryfast -pix_fmt yuv420p session.mp4`.
  - 녹화 중 CameraService가 가공 프레임 바이트를 `StandardInput.BaseStream`에 write. 종료 시 **stdin flush+Close** 후 `WaitForExit`(moov atom 완성).
  - `TimelapseService`: 세션 길이에서 배속 N 역산(목표 10~15초) → `-i session.mp4 -vf "setpts=(1/N)*PTS,fps=30" -an -c:v libx264 -crf 20 -pix_fmt yuv420p timelapse.mp4`.
  - ffmpeg 경로: 번들(`tools/ffmpeg/ffmpeg.exe`) 우선. 배속·해상도는 인자 조립 함수로 분리(테스트 대상).
- **검증 명령**: `dotnet test --filter FfmpegArgsTests`(인자 조립·N 역산 로직) + 통합: 더미 프레임 시퀀스 → session.mp4 생성 → 타임랩스 → `ffprobe`로 코덱(h264)·재생시간·무음 확인.
- **완료 기준**:
  - [관측] 프레임 시퀀스 녹화 시 재생 가능한 session.mp4(h264, yuv420p) 생성. 타임랩스 mp4 재생시간 10~15초·무음(오디오 스트림 없음, ffprobe 확인). 인자 조립 테스트 통과.
  - [non-goal] 타임랩스는 무음(`-an`)·H.264 고정 — 다른 코덱/오디오 **금지**. 녹화가 프리뷰 프레임레이트를 심각히 떨어뜨리지 않음(백프레셔 시 프레임 드롭 허용, 프리뷰 우선).
  - [trigger] 녹화 시작=세션 첫 컷 카운트다운 시작, 종료=마지막 컷 촬영 후. 타임랩스 생성=녹화 종료(stdin close) 후.
- **롤백**: Step6 커밋 revert. 녹화 분기 제거(Step3 프리뷰만 상태로 복귀).
- [ ] 완료

---

## Step 7: 촬영 로직 · 스틸 캡처 · 크롭 · 필터 · 배경형 합성

- **Context Brief**: 연속 N컷 촬영(컷별 카운트다운, [바로촬영], 플래시), 컷 선택(슬롯 수만큼), 필터(흑백·밝기·간단뷰티) 적용 후 배경형 합성으로 최종 이미지를 만든다(PRD §F1/F4). 캡처가 이미 슬롯 종횡비라 왜곡 없이 uniform 스케일 배치. 결과는 `Example/result_frame2.jpg` 수준이어야 한다.
- **대상 파일**: `src/MCPhoto.Capture/CompositionService.cs`(`ICompositionService`), `src/MCPhoto.Capture/Filters.cs`(흑백·밝기·뷰티), `src/MCPhoto.Core/Capture/CaptureSession.cs`(컷 버퍼·카운트다운·바로촬영), `tests/MCPhoto.Tests/CompositionTests.cs`.
- **선행 조건**: Step 3(가공 프레임), Step 2(outputFormat 등 설정). 프레임 모델은 Step10과 공유(Core에 FrameTemplate 모델 정의).
- **구현 내용**:
  - `CaptureSession`: cutCount만큼 컷별 countdownSec 카운트다운 → 자동 셔터. [바로촬영] 시 남은 카운트다운 스킵 즉시 셔터. 플래시 on 시 셔터 직전 하양 오버레이. 스틸은 가공 프레임 Clone을 메모리 컷 버퍼에 보관.
  - 컷 선택: 촬영분 중 **정확히 슬롯 수만큼** 선택(선택 순서=슬롯 순서, 빈 슬롯 없음, §9 #29). 재촬영=세션 전체.
  - `Filters`: 흑백(그레이스케일), 밝기(밝기+약한 대비), 간단뷰티(경량 소프트닝+톤, 얼굴인식無). 전체 컷 일괄.
  - `CompositionService`: 프레임 이미지=배경 → 슬롯 픽셀 영역에 (필터 적용된) 컷 배치. 캡처가 슬롯 종횡비라 uniform 스케일만(추가 크롭 없음, 슬롯 종횡비 상이 시 슬롯별 중앙 크롭 보정). 출력 해상도=프레임 원본. 포맷=outputFormat.
- **검증 명령**: `dotnet test --filter CompositionTests`(슬롯 배치 좌표·왜곡 없음·필터 적용·출력 크기) + 통합: 샘플 프레임(`Example/Frame.png` 유사)+더미 컷 → 합성 결과가 슬롯에 정렬되는지 육안(`result_frame2.jpg`와 대조).
- **완료 기준**:
  - [관측] N컷 촬영→슬롯 수만큼 선택→합성 시 각 컷이 해당 슬롯 픽셀 영역에 왜곡 없이 배치된 최종 이미지 생성(출력 크기=프레임 원본). 필터 토글 시 전체 컷에 반영. [바로촬영]으로 카운트다운 즉시 스킵.
  - [non-goal] 슬롯 수보다 많거나 적게 선택 **불가**(정확히 슬롯 수). 필터 미선택 시 원본 그대로(무변형). 프레임은 촬영 전 선택 고정 — 결과 화면에서 프레임 변경 **불가**.
  - [trigger] 셔터=카운트다운 만료 또는 [바로촬영]. 합성=[다음]/결과 확정 시. 플래시=flashMode=on일 때 셔터 직전만.
- **롤백**: Step7 커밋 revert.
- [ ] 완료

---

## Step 8: Firebase 업로드 · ResultSession 생성 · 다운로드 URL · QR

- **Context Brief**: QR 전송 on(enableQrDelivery) 시 최종 이미지+타임랩스를 Storage에 업로드하고 ResultSession 문서를 만들어 downloadPageUrl을 산출, QR로 표시한다(PRD §F5, `firebase-contract.md`). ⚠️ Storage는 Blaze 필수(VF-3). 업로드 성공 후에만 QR 노출(§10). 계약 문서의 스키마·경로·URL 규약을 **정확히** 준수해야 웹이 읽을 수 있다.
- **대상 파일**: `src/MCPhoto.Firebase/FirebaseClient.cs`(Admin SDK 초기화, 서비스 계정 로드), `src/MCPhoto.Firebase/UploadService.cs`(`IUploadService`), `src/MCPhoto.App/Services/QrService.cs`(`IQrService`, QRCoder), `tests/MCPhoto.Tests/UploadContractTests.cs`(경로·URL 조립 규약).
- **선행 조건**: Step 7(결과물), Step 2(hostingBaseUrl·retentionHours). **Firebase 프로젝트 Blaze 전환**(외부 준비).
- **구현 내용**:
  - `FirebaseClient`: MVP 1차 = FirebaseAdmin 서비스 계정(`%ProgramData%\MCPhoto\`의 보호된 키, git·인스톨러 제외). 인터페이스 추상화로 배포 시 규칙 준수 경로 교체 가능(architecture §6.4).
  - 업로드(firebase-contract §4): `results/{sessionId}/final.{jpg|png}`, `results/{sessionId}/timelapse.mp4`. 각 파일 다운로드 토큰 URL(`?alt=media&token=`) 획득.
  - ResultSession 문서(firebase-contract §2.3): id=UUIDv4 토큰(문서ID), finalImageUrl, timelapseUrl, createdAt, expiresAt=createdAt+retentionHours, downloadPageUrl(§3.5 규약 `{hostingBaseUrl}/?s={token}`).
  - QR: downloadPageUrl → QRCoder 비트맵 → 결과 화면 QR 팝업(BM⑤). "N시간 후 자동 삭제" 고지(retentionHours 반영).
  - 신뢰성(§10): 업로드 성공 후에만 QR 노출. 실패 시 재시도·오류 안내. 오프라인 시 "전송 불가" 안내(게스트+번들 모드).
  - enableQrDelivery off: 업로드·QR 생략 → 로컬 저장(Step5) 후 완료.
- **검증 명령**: `dotnet test --filter UploadContractTests`(Storage 경로·downloadPageUrl 조립·expiresAt 계산이 계약과 일치) + 통합(Blaze 프로젝트 연결 시): 실제 업로드→Firestore 문서 확인→토큰 URL 브라우저 접근→QR 스캔.
- **완료 기준**:
  - [관측] QR on + 온라인 시 업로드 성공 후 Firestore `resultSessions/{uuid}` 문서 생성(계약 필드 전부), Storage `results/{uuid}/` 파일 존재, downloadPageUrl이 `{hostingBaseUrl}/?s={uuid}` 형식, QR 팝업 표시. 토큰 URL을 브라우저로 열면 파일 다운로드됨.
  - [non-goal] QR off 시 업로드·문서 생성·QR **없음**(로컬 저장만). 업로드 실패 시 QR 노출 **금지**(성공 후에만). frames/·users 경로는 이 플로우에서 건드리지 않음.
  - [trigger] 업로드=합성 완료 + enableQrDelivery=on + 온라인. QR 표시=업로드 성공 콜백 후에만. 서비스 계정 키는 로컬 보호 위치에서만 로드(번들 금지).
- **롤백**: Step8 커밋 revert. enableQrDelivery를 off 고정으로 두면 앱은 로컬 저장 경로로 정상 동작(완화 경로).
- [ ] 완료

---

## Step 9: 촬영 플로우 화면 통합 (홈→…→완료)

- **Context Brief**: Step3~8의 서비스를 실제 화면(BM 흐름)으로 엮는다. 홈·로그인/게스트선택·프레임선택·촬영안내·촬영·컷선택·결과·QR팝업·완료(PRD §5, BM①~⑤). 유휴/예외 복구(Step4)와 통합된 한 세션의 end-to-end.
- **대상 파일**: `src/MCPhoto.App/Views/*.xaml`(HomeView, LoginGuestView, FrameSelectView, GuideView, CaptureView, CutSelectView, ResultView, QrPopupView, DoneView) + 대응 ViewModel.
- **선행 조건**: Step 4(셸), Step 7(촬영·합성), Step 8(업로드·QR), Step 3(프리뷰), Step 6(타임랩스).
- **구현 내용**:
  - 각 화면 XAML+VM을 상태머신(Step4)에 연결. 프레임은 **촬영 전 선택·이후 변경 불가**(§9 #28). 결과 화면=미리보기+필터(BM④, 프레임 전환 없음). QR 팝업=[홈으로]/[닫기](BM⑤).
  - 가로/세로 레이아웃 각 화면 반영. 진행 상태 표시(합성·영상·업로드 중).
  - 완료→대기 복귀(키오스크 1회 세션, §F8).
- **검증 명령**: `dotnet build -c Release` + `dotnet run`으로 게스트 end-to-end 1회(웹캠+온라인 or QR off 완화 경로) 수동 리허설.
- **완료 기준**:
  - [관측] 홈→게스트→프레임선택→안내→촬영(N컷)→컷선택(슬롯수)→결과(필터)→(QR on)QR팝업/(off)로컬저장→완료→홈 이 끊김 없이 진행. 각 화면 가로/세로 레이아웃 적용.
  - [non-goal] 결과 화면에서 프레임 변경 UI **없음**. 이메일 입력 단계 **없음**(§28). 세션 완료 후 이전 세션 데이터가 다음 세션에 **잔존 금지**.
  - [trigger] 화면 전이는 사용자 액션(버튼)·카운트다운·유휴 만료에만. QR 팝업은 업로드 성공 후에만.
- **롤백**: Step9 커밋 revert. 개별 화면을 플레이스홀더로 되돌림(Step4 셸 상태).
- [ ] 완료

---

## Step 10: 프레임 편집기 (슬롯 배치)

- **Context Brief**: 로그인 사용자가 프레임 이미지를 업로드(PNG/JPG/JPEG, 임의 크기·비율)하고 슬롯 1~6개 개수·위치·크기를 드래그로 조절해 저장한다(PRD §F2, `Example/Frame-slot-setting.png`). 편집 범위는 슬롯 배치만(텍스트/스티커/배경 제외, §9 #25). 슬롯이 프레임 밖으로 나가거나 겹치지 않도록 제약.
- **대상 파일**: `src/MCPhoto.App/Views/FrameEditorView.xaml`(+VM), `src/MCPhoto.Core/Frames/SlotLayout.cs`(자동 배치·경계 제약·겹침 검사), `src/MCPhoto.Firebase/FrameRepository.cs`(`IFrameRepository`, Firestore+Storage frames/), `tests/MCPhoto.Tests/SlotLayoutTests.cs`.
- **선행 조건**: Step 4(셸), Step 11(계정 소유 — userId), Step 8(Firebase 인프라). 프레임 모델은 Step7과 공유.
- **구현 내용**:
  - 이미지 업로드: 장변 4000px·10MB 제한(초과 리사이즈/거부). 원본 크기 imageSize 기록.
  - 자동 배치: 표준 비율=격자(4=2×2), 세로 스트립=1열(1×4/1×3)(§9 #15). 드래그로 수동 조절.
  - 제약: 슬롯 경계 클램프(프레임 밖 이탈 방지)·겹침 방지 가이드·스냅. 프레임 작으면 슬롯 축소 가이드.
  - 저장: FrameTemplate → Firestore `frameTemplates` + Storage `frames/{userId}/`(firebase-contract §2.2/§4.1). 계정당 최대 10개(초과 시 거부).
- **검증 명령**: `dotnet test --filter SlotLayoutTests`(자동 배치 좌표·경계 클램프·겹침 검사·10개 제한) + `dotnet run`으로 편집기 육안(업로드·드래그·저장).
- **완료 기준**:
  - [관측] 프레임 업로드 후 슬롯 개수 지정 시 자동 배치, 드래그로 위치·크기 조절, 저장 시 Firestore 문서+Storage 이미지 생성. 슬롯을 프레임 밖으로 드래그 시 경계에서 클램프. 11번째 프레임 저장 시도 시 거부.
  - [non-goal] 텍스트·스티커·배경 편집 UI **없음**(슬롯 배치만). 슬롯이 프레임 경계를 넘거나 서로 겹치도록 저장 **불가**. 게스트는 편집기 접근 **불가**(로그인 필요).
  - [trigger] 저장=명시적 [저장] 버튼. 자동 배치=슬롯 개수 변경 시. 업로드=이미지 선택 시(제한 검사 통과 후).
- **롤백**: Step10 커밋 revert.
- [ ] 완료

---

## Step 11: 계정 · 로그인 · 역할 · 관리자 모드 · 사용자 관리

- **Context Brief**: id/pw 로그인(인증 절차 없음), 역할(user/manager/admin), 로그인 계정=자기 인스턴스 관리자(앱 설정 관리), power 계정의 사용자 관리·공용 기본 프레임 관리, 계정 삭제 cascade(PRD §F7/F8). ⚠️ MVP 비밀번호 평문(웹 접근 차단은 보안 규칙이 담당, firebase-contract §5).
- **대상 파일**: `src/MCPhoto.Firebase/AccountService.cs`(`IAccountService`, Firestore users), `src/MCPhoto.App/Views/LoginView.xaml`(계정 허브), `AccountCreateView.xaml`, `AdminView.xaml`(설정·앱종료·기본프레임·사용자관리 진입), `UserMgmtView.xaml`(power), `tests/MCPhoto.Tests/AccountTests.cs`.
- **선행 조건**: Step 4(셸·롱프레스 진입), Step 8(Firestore 인프라), Step 2(AppSettings 관리 대상).
- **구현 내용**:
  - 로그인: id/pw로 `users` 조회(평문 비교, MVP). 게스트=비로그인(기본 프레임만). 시드 `devmcjo`/`1111`(admin) 없으면 최초 생성.
  - 역할 권한(§F8): user=자기 프레임+AppSettings 관리. manager=+사용자관리+공용 기본프레임. admin=+manager 지정.
  - 계정 허브(로그인 화면): 로그아웃·비밀번호 변경·관리자 모드(앱 설정). 사용자 관리는 power만(§9 #30).
  - 관리자 모드: AppSettings 전 항목 편집(로그인 계정 누구나)·앱 종료. 공용 기본 프레임(isDefault) 관리·사용자 관리(목록·삭제·pw 초기화)=power만.
  - cascade 삭제(§F8): 계정 삭제 시 소유 frameTemplates 문서 + Storage frames/{userId}/ 함께 삭제.
- **검증 명령**: `dotnet test --filter AccountTests`(로그인 성공/실패·역할 권한 게이트·cascade 삭제 대상 산출·pw 초기화) + `dotnet run`으로 롱프레스→로그인→관리자 모드 육안.
- **완료 기준**:
  - [관측] 올바른 id/pw 로그인 성공, 틀리면 실패. user 로그인 시 AppSettings 편집 가능·사용자관리 **불가**, power 로그인 시 사용자관리 가능. 계정 삭제 시 소유 프레임(문서+Storage) 함께 삭제. admin만 manager 지정 UI 노출.
  - [non-goal] user가 타 계정 관리·공용 기본 프레임 관리 **불가**. manager가 다른 manager 지정 **불가**(admin만). 3초 미만 롱프레스로 관리자 진입 **안 됨**. 게스트는 커스텀 프레임 접근 **불가**.
  - [trigger] 관리자 모드 진입=좌상단 3초 롱프레스+로그인 성공. 설정 저장=[저장]/[확인] 시(Step2 INI). 계정 삭제=power의 명시적 삭제 확인 시.
- **롤백**: Step11 커밋 revert.
- [ ] 완료

---

## Step 12: 번들 · 인스톨러(Inno Setup) · 오프라인 폴백 · 통합 리허설

- **Context Brief**: 배포 패키지(Inno Setup)로 앱+기본 프레임(`Frame/`)+ffmpeg를 번들하고, 쓰기 가능 데이터 폴더(`%ProgramData%\MCPhoto\`)·기본 프레임 우선순위(DB→번들→fallback)·오프라인 폴백(게스트+번들)을 통합 검증한다(PRD §9 #22, §10, §F2). ⚠️ 서비스 계정 키는 인스톨러에 **절대 포함 금지**(architecture §6.4).
- **대상 파일**: `installer/MCPhoto.iss`(Inno Setup 스크립트), `Frame/`(번들 기본 프레임), `tools/ffmpeg/ffmpeg.exe`, `src/MCPhoto.Core/Frames/DefaultFrameProvider.cs`(우선순위·fallback 생성), `tests/MCPhoto.Tests/DefaultFrameTests.cs`.
- **선행 조건**: 전 단계(특히 Step 8·10·11의 Firebase 경로, Step 2 데이터 폴더).
- **구현 내용**:
  - `DefaultFrameProvider`: ①DB isDefault 프레임 → ②설치 `Frame/` 번들 → ③fallback(하양·4슬롯·3:4 코드 생성)(§9 #11). 오프라인 시 ①불가→②/③.
  - Inno Setup: 앱 바이너리 + `Frame/` + `tools/ffmpeg/` 번들. 데이터 폴더 `%ProgramData%\MCPhoto\` 생성(쓰기 가능). `Program Files` 보호 위치엔 런타임 갱신 데이터 배치 금지.
  - 오프라인 폴백: Firestore 접근 불가 시 게스트+번들 프레임 모드(QR 전송 불가 안내, §32).
  - `.gitignore`·인스톨러 제외 목록에 서비스 계정 키 재확인.
  - 통합 리허설: (a) QR on/온라인 end-to-end, (b) QR off + 로컬 저장(Blaze 없이도 데모 가능한 완화 경로), (c) 오프라인 게스트 모드.
- **검증 명령**: `dotnet test --filter DefaultFrameTests`(우선순위·fallback 생성) + `iscc installer/MCPhoto.iss`(인스톨러 빌드) + 클린 머신/VM 설치 후 3개 리허설 시나리오 수동 실행.
- **완료 기준**:
  - [관측] 인스톨러 빌드 성공, 클린 설치 후 앱 실행. DB 기본 프레임 없을 때 번들 `Frame/` 사용, 둘 다 없을 때 fallback(하양·4슬롯·3:4) 생성. 오프라인 시 게스트+번들 모드 동작(QR 불가 안내). QR off+로컬저장 시 Firebase 없이 결과물 로컬 보존.
  - [non-goal] 설치 패키지에 서비스 계정 키·비밀 **미포함**(grep로 확인). 런타임 갱신 데이터가 `Program Files`(보호)에 쓰이지 **않음**(`%ProgramData%` 사용). fallback 프레임이 DB/번들 존재 시 **사용 안 됨**(우선순위 준수).
  - [trigger] fallback 생성=①②모두 부재 시에만. 오프라인 안내=Firestore 접근 실패 시에만.
- **롤백**: Step12 커밋 revert. 인스톨러 산출물 폐기(개발 빌드는 `dotnet run`으로 동작 유지).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실 / 미검증 가정 목록 분리됨
- [x] 모든 가정(OA-1~OA-10)에 검증 Step 매핑됨 (Step 1/2/3/4/5/6/7/8/10/11)
- [x] 모든 Step에 7개 필수 필드(Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백) 채워짐
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step(3/4/7/9/10/11)은 non-goal·trigger 포함
- [x] 검증 명령이 자동 실행 가능 형태(`dotnet build`/`dotnet test --filter`/`iscc`/`ffprobe`)

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally` / `committed` / `pushed` / `blocked`(사유 명시 필수)
