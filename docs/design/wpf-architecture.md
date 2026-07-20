# MC포토 — WPF 앱 아키텍처 설계

| 항목 | 값 |
|------|-----|
| 문서 | WPF 클라이언트 아키텍처 설계 |
| 대상 PRD | `docs/prd/photobooth-prd.md` (초안 v2.7) |
| 작성일 | 2026-07-20 |
| 상태 | 초안 v1 (구현 착수 전) |
| 관련 문서 | `docs/design/wpf-wbs.md`(구현 WBS), `docs/design/firebase-contract.md`(파이프라인 간 계약) |

---

## 0. 검증된 사실 / 미검증 가정

> WBS 블루프린트 규칙에 따라 **직접 확인한 사실**과 **미검증 가정**을 분리한다. 모든 가정은 WBS의 어느 Step에서 검증되는지 매핑한다.

### 검증된 사실 (verified facts)

- **F-1. 프로젝트는 그린필드**: `E:\Study\photobooth`에 소스 코드 없음. `docs/`, `Example/`, `.claude/`, `LICENSE`, `README.md`만 존재. (근거: `ls -la E:/Study/photobooth` 출력)
- **F-2. 합성 모델은 배경형**: `Example/result_frame2.jpg`(최종물)와 `Example/Frame-slot-setting.png`(편집기)를 확인한 결과, 프레임 이미지가 배경이고 사진이 슬롯 사각형 영역 위에 얹히는 구조가 실물로 확인됨. 4슬롯 2×2 격자, 세로 슬롯. 프레임 상/하단에 타이틀·성경구절 등 장식이 이미 포함(in-app 편집 불필요). (근거: PRD §9 #13, 실물 이미지)
- **F-3. 무료(Spark) 요금제는 Cloud Storage 사용 불가**: 2026-02-03부로 Spark 프로젝트는 기본 버킷 포함 모든 Cloud Storage 접근이 차단됨(402/403). 사진·영상 파일 업로드는 **Blaze(종량제, 카드 등록) 필수**. 단 Always Free 한도 내에서 $0 청구 유지 가능. Firestore는 Spark에서 계속 무료(1GiB, 읽기 5만/쓰기 2만/삭제 2만 per day). (근거: Firebase 공식 FAQ `firebase.google.com/docs/storage/faqs-storage-changes-announced-sept-2024`, 조사 서브에이전트 1차 확인. PRD §10 "확인 필요" 항목의 확정 답)
- **F-4. 웹캠 WYSIWYG 파이프라인의 최적 조합**: raw 프레임을 콜백/루프로 받아 픽셀 가공(반전/크롭) 후 단일 프레임을 프리뷰·스틸·녹화 세 갈래로 분기하는 구조가 요구(§F1 WYSIWYG)에 부합. WinRT MediaCapture의 sink 기반 모델은 "가공 프레임 녹화"에 부적합. OpenCvSharp4(Apache-2.0)가 이 구조에 1순위. (근거: 웹캠 라이브러리 조사 서브에이전트, OpenCvSharp GitHub·NuGet)
- **F-5. Admin SDK/서비스 계정은 보안 규칙을 완전 우회**하며 데스크톱 번들 시 키 유출 = 프로젝트 전체 god-mode. 규칙 준수 클라이언트 경로는 .NET에 Firestore용 공식 SDK가 없어 REST + Firebase Auth ID 토큰으로 구현해야 함. (근거: Firebase 공식 문서 `rules-structure`, Firestore REST 문서, 조사 서브에이전트)
- **F-6. 캡처 컷수 ≥ 슬롯 수 항상 성립**: 촬영 컷수 최소 6, 슬롯 최대 6 제한으로 프레임 비활성/컷수 변동 로직 불필요. (근거: PRD §F1 "컷수·슬롯 관계", §9 #12)

### 미검증 가정 (open assumptions)

- **A-1. .NET 8 LTS로 신규 프로젝트 구성 가능** (Windows 개발/타깃 머신에 SDK 설치·복원 가능) → 검증: **WBS Step 1** (`dotnet build` 성공).
- **A-2. 대상 웹캠(UVC)이 1920×1080 @ 30fps MJPG를 제공**하고 OpenCvSharp `VideoCapture(DSHOW)`로 프리뷰+스틸을 실시간 획득 가능 → 검증: **WBS Step 3** (프리뷰 프레임 수신 확인).
- **A-3. ffmpeg.exe stdin rawvideo 파이프로 H.264 mp4 녹화 및 setpts 배속 타임랩스 생성 가능** → 검증: **WBS Step 6** (재생 가능한 mp4 산출).
- **A-4. 기보유 Firebase 프로젝트를 Blaze로 전환 가능**(결제 계정 등록 가능). 불가 시 Storage 사용 불가 → **QR 전송(F5) off 모드 + 로컬 저장만으로 MVP 데모 가능**해야 함 → 검증: **WBS Step 8**(업로드 실 연동) / 완화책은 Step 5(로컬 저장)에서 독립 검증.
- **A-5. QR 전송(F5)의 실제 앱-웹 연동은 MVP에서 "Admin SDK 직결(개인 사용 전제)"로 시작**하고, 배포 시 규칙 준수 경로로 전환한다(§6.4 참조) → 검증: **WBS Step 8**.
- **A-6. INI 파일 기반 설정 저장이 쓰기 가능 위치(`%ProgramData%\MCPhoto\` 또는 실행 경로)에서 동작** → 검증: **WBS Step 2**.
- **A-7. WriteableBitmap 재사용 방식으로 1080p 30fps 프리뷰가 대상 하드웨어에서 끊김 없이 렌더링** → 검증: **WBS Step 3**(프리뷰 육안 확인, 프레임레이트 로깅).

---

## 1. 아키텍처 개요

### 1.1 한 줄 요약

Windows 키오스크에서 동작하는 **단일 프로세스 WPF(.NET 8) MVVM 앱**. 웹캠 캡처 파이프라인(프리뷰+스틸+녹화 단일 스트림)을 코어로 두고, 프레임 합성·타임랩스 생성·Firebase 업로드·QR 생성을 서비스 계층으로 분리한다. 화면은 상태 머신(홈→프레임선택→촬영→선택→결과→QR→완료)으로 전이한다.

### 1.2 기술 스택 결정

| 영역 | 선택 | 근거 |
|------|------|------|
| 런타임 | **.NET 8 (LTS)** | 2026년 시점 WPF 지원 LTS. .NET 9는 STS(단기). 신규 프로젝트는 LTS 권장. `net8.0-windows` TFM. |
| UI 프레임워크 | **WPF** | PRD §7 확정. 애니메이션 UI·터치 적합. |
| MVVM 툴킷 | **CommunityToolkit.Mvvm** (MIT) | `ObservableObject`/`RelayCommand`/소스 제너레이터. 경량·공식(구 MVVM Light 후계). |
| DI 컨테이너 | **Microsoft.Extensions.DependencyInjection** | 표준. 서비스 수명 관리. |
| 웹캠 캡처 | **OpenCvSharp4.Windows + OpenCvSharp4.WpfExtensions** (Apache-2.0) | §F1 WYSIWYG 파이프라인에 최적. raw `Mat`→가공→프리뷰/스틸/녹화 분기. (F-4) |
| 영상 인코딩 | **ffmpeg.exe 번들 + stdin rawvideo 파이프** | 타임랩스 H.264 mp4. OpenCvSharp `VideoWriter`의 H.264 불안정 이슈 회피. (F-4) |
| 이미지 합성 | **OpenCvSharp**(크롭/합성) 또는 `System.Drawing`/WPF `RenderTargetBitmap` | 슬롯 배치 합성. 캡처가 이미 슬롯 종횡비이므로 uniform 스케일만. |
| Firebase 접근 | **FirebaseAdmin (서비스 계정) — MVP 1차** / 규칙 준수(REST+ID토큰) — 배포 시 전환 | §6.4 트레이드오프 참조. (F-5, A-5) |
| QR 생성 | **QRCoder** (MIT) | 순수 .NET, 의존성 없음. 다운로드 URL→QR 비트맵. |
| 설정 저장 | **INI 파일**(자체 파서 또는 경량 라이브러리) | PRD §9 #38 확정. |
| 로깅 | **Microsoft.Extensions.Logging** + 파일 싱크(Serilog 등) | 무인 동작 진단. |
| 인스톨러 | **Inno Setup** | PRD §9 #22 확정. 앱+`Frame/`+ffmpeg 번들. |

> **주의(라이선스)**: 배포 ffmpeg 바이너리에 libx264(GPL)가 포함되면 배포물에 GPL 의무가 붙을 수 있음. 배포 시 ffmpeg 빌드 라이선스 확인 필요(§9 리스크).

### 1.3 레이어 구조

```
┌─────────────────────────────────────────────────────────┐
│  Views (XAML)  — 화면 15종, 가로/세로 레이아웃 자동 전환    │
├─────────────────────────────────────────────────────────┤
│  ViewModels    — 화면별 VM + AppShellViewModel(상태머신)   │
├─────────────────────────────────────────────────────────┤
│  Services (인터페이스 + 구현)                              │
│   ICameraService      : 캡처 파이프라인(프리뷰/스틸/녹화)  │
│   ICompositionService : 필터+슬롯 합성 → 최종 이미지       │
│   ITimelapseService   : 세션 영상 → 배속 mp4 (ffmpeg)      │
│   IFrameRepository    : FrameTemplate CRUD (기본/커스텀)   │
│   IAccountService     : User 로그인/CRUD/역할              │
│   IUploadService      : Firebase 업로드 + 다운로드URL      │
│   IQrService          : URL → QR 이미지                    │
│   ILocalSaveService   : 로컬 결과물 저장(TTL 무관)         │
│   ISettingsService    : INI 로드/저장, 창 복원             │
│   IIdleWatchdog       : 유휴 타임아웃 → 대기화면 복귀      │
├─────────────────────────────────────────────────────────┤
│  Infrastructure                                          │
│   FirebaseClient(Admin SDK or REST), FfmpegRunner,       │
│   OpenCvCapture, IniStore, FileSystem                    │
├─────────────────────────────────────────────────────────┤
│  Backend (외부)  Firebase: Firestore + Storage + Hosting  │
│   ⇄ 계약: docs/design/firebase-contract.md               │
└─────────────────────────────────────────────────────────┘
```

### 1.4 프로젝트(어셈블리) 구성

```
MCPhoto.sln
├─ MCPhoto.App          (WPF, net8.0-windows)  — Views, ViewModels, App.xaml, DI 조립
├─ MCPhoto.Core         (net8.0)               — 도메인 모델, 서비스 인터페이스, 상태머신
├─ MCPhoto.Capture      (net8.0-windows)       — OpenCvSharp 캡처, ffmpeg 러너, 합성
├─ MCPhoto.Firebase     (net8.0)               — Firebase 접근 구현(Admin/REST 전환 가능)
└─ MCPhoto.Tests        (net8.0)               — 단위 테스트(합성/크롭/설정/토큰)
```

> 그린필드이므로 위 분리를 권장하되, MVP 속도를 위해 최소 `MCPhoto.App` + `MCPhoto.Core` 2개로 시작하고 캡처·Firebase를 폴더로 분리해도 무방(WBS Step 1에서 확정). 테스트 가능성을 위해 서비스는 반드시 인터페이스로 추상화한다.

---

## 2. 캡처 파이프라인 (핵심 리스크, §F1/F3)

### 2.1 요구 재확인

하나의 웹캠 스트림에서 **동시에**: (a) 라이브 프리뷰, (b) 정지 스틸 촬영, (c) 세션 전체 연속 녹화. 그리고 거울모드(좌우반전)·슬롯 종횡비 중앙 크롭을 **프리뷰=스틸=녹화 모두 동일 적용**(WYSIWYG).

### 2.2 파이프라인 설계 — "프레임의 주인" 구조

```
[웹캠] --VideoCapture 루프(백그라운드 스레드)--> Mat(원본 BGR)
   │
   ├─ (거울모드 on) Cv2.Flip(FlipMode.Y)
   ├─ 슬롯 종횡비로 중앙 크롭 (Rect ROI) → processedFrame(Mat)
   │
   ├─(a) 프리뷰: UI 스레드로 마샬링 → 재사용 WriteableBitmap.WritePixels
   ├─(b) 스틸:  셔터 시점 processedFrame.Clone() → 컷 버퍼에 보관(메모리) → 나중 Cv2.ImWrite
   └─(c) 녹화:  recording 중이면 processedFrame 바이트를 ffmpeg stdin으로 write
```

- **단일 가공 원칙**: 반전·크롭을 프레임당 **한 번만** 수행하고 그 결과를 세 소비자가 공유 → WYSIWYG 자동 보장.
- **크롭 ROI 계산**: 카메라 원본 종횡비와 선택 프레임의 대표 슬롯 종횡비를 비교, 중앙 기준 crop 사각형 산출(왜곡 없이 잘라내기). 슬롯들이 다른 종횡비면 대표 슬롯로 크롭 후 합성 시 슬롯별 중앙 크롭 보정(§F1).
- **캡처 해상도**: `VideoCapture.Set`으로 1920×1080 요청, FourCC MJPG 설정(UVC 1080p 확보에 종종 필요). 실패 시 장치 기본 해상도로 폴백.
- **거울모드 WYSIWYG**: on이면 프리뷰·스틸·녹화 모두 반전(§F1 확정). off면 모두 정방향. 반전 여부는 파이프라인 진입부 단일 분기.

### 2.3 스레딩 모델

- **캡처 스레드**(전용 백그라운드): `VideoCapture.Read` 루프. UI를 블로킹하지 않음.
- **UI 스레드**: 프리뷰 렌더링만 담당. `WriteableBitmap`은 `DispatcherObject`라 소유 스레드에서만 쓰기 → 가공은 캡처 스레드, 커밋은 `Dispatcher.Invoke`로 마샬링.
- **ffmpeg 파이프 write**: 캡처 스레드에서 동기 write(백프레셔 시 프레임 드롭 허용) 또는 바운디드 큐 + 인코더 스레드.
- **성능 목표**: 1080p 30fps 프리뷰(A-7에서 검증). WriteableBitmap **하나를 재사용**(매 프레임 새 BitmapSource 생성 금지). `Bgra32`/`Bgr32` 정렬 사용, `BackBufferStride` 고려.

### 2.4 스틸 촬영 & 카운트다운 & 바로촬영

- 연속 촬영: `cutCount`만큼 컷별 `countdownSec` 카운트다운 → 자동 셔터. 플래시 on이면 셔터 직전 화면 하양 오버레이 후 촬영.
- **[바로 촬영]**: 카운트다운 진행 중 남은 시간 스킵 즉시 셔터(매 컷 사용 가능, §9 #37). 카운트다운 타이머 취소 → 즉시 캡처 트리거.
- 스틸은 파일 즉시 저장이 아니라 **메모리 컷 버퍼**에 Clone 보관(선택 화면에서 슬롯 수만큼 선택 후 합성). 세션 종료·취소 시 폐기.

### 2.5 녹화 & 타임랩스 (§F3)

- 세션(첫 컷~마지막 컷) **전체** 녹화 = 가공 프레임(크롭·반전 반영)을 ffmpeg stdin으로 연속 write.
- ffmpeg 커맨드(녹화):
  `ffmpeg -f rawvideo -pixel_format bgr24 -video_size {W}x{H} -framerate 30 -i - -c:v libx264 -crf 20 -preset veryfast -pix_fmt yuv420p session.mp4`
- **stdin Close 필수**: 종료 시 stdin을 flush+close해야 mp4 moov atom 완성.
- 타임랩스(세션 후 배속):
  `ffmpeg -i session.mp4 -vf "setpts=(1/N)*PTS,fps=30" -an -c:v libx264 -crf 20 -pix_fmt yuv420p timelapse.mp4`
  - N = 배속 배율. **목표 길이 10~15초**가 되도록 세션 길이에서 N 역산(무음 `-an`).
  - 대안: 캡처 시 프레임 샘플링(N프레임당 1장 저장)으로 파일·비용 절감 — 완만한 배속엔 setpts, 극단 배속·긴 세션엔 샘플링. MVP는 setpts 방식으로 단순화.

### 2.6 카메라 장치 선택 (§AppSettings.cameraDevice)

- 다중 카메라 환경 대비: 장치 열거 → 설정에서 인덱스/이름 선택. `VideoCapture(index)`. 설정값을 INI에 저장.

---

## 3. 프레임 & 합성 (§F2/F4)

### 3.1 FrameTemplate 모델 (도메인)

```
FrameTemplate {
  Id: string
  UserId: string?          // 커스텀=소유자 id, 기본프레임=null
  IsDefault: bool          // 공용 기본 프레임
  Name: string
  ImageUrl: string         // Storage URL 또는 로컬 번들 경로
  ImageSize: { Width, Height }   // 등록 원본 픽셀
  Slots: Slot[]            // 1~6개
  CreatedAt: DateTime
}
Slot { Index, X, Y, Width, Height }   // 프레임 픽셀 좌표계, 프레임 내 제약
```

> Firestore 스키마·경로는 `firebase-contract.md` §2·§3에 확정. 로컬 도메인 모델은 이와 매핑된다.

### 3.2 기본 프레임 소스 우선순위 (§F2, §9 #11)

1. **DB 등록 기본 프레임**(isDefault=true, Firestore+Storage) 있으면 다운로드 사용(우선).
2. 없으면 설치 폴더 `Frame/` 번들 프레임.
3. 그것도 없으면 **fallback**: 하양 배경·슬롯 4개·3:4 비율 내장 프레임(코드 생성).

- 오프라인 시: 1번 불가 → 2·3번으로 게스트+번들 모드 동작(§8 네트워크).

### 3.3 프레임 편집기 (§F2, `Frame-slot-setting.png` 참조)

- 프레임 이미지 업로드(PNG/JPG/JPEG, 임의 크기·비율, 장변 4000px·10MB 제한 — 초과 시 리사이즈/거부).
- 슬롯 개수 1~6 지정 → **자동 배치**(표준=격자 4=2×2, 세로 스트립=1열) → 드래그로 위치·크기 조절.
- **제약/가이드**: 슬롯이 프레임 밖으로 나가거나 겹치지 않도록 경계 클램프·스냅. 프레임이 작으면 슬롯 축소 가이드.
- 편집 범위: **슬롯 배치만**(개수·위치·크기). 텍스트·스티커·배경 in-app 편집 제외(§9 #25). 장식은 업로드 프레임에 포함 전제.
- 저장: 계정당 최대 10개(Firestore FrameTemplate + Storage frames/{userId}/).

### 3.4 합성 (§F4, 배경형)

- 필터(선택, 전체 컷 일괄): 흑백(그레이스케일), 밝기(+약한 대비), 간단 뷰티(경량 소프트닝+톤, 얼굴인식 無).
- 합성: 프레임 이미지 = 배경 레이어 → 필터 적용된 선택 컷을 슬롯 픽셀 영역에 배치. 캡처가 이미 슬롯 종횡비라 **왜곡 없이 uniform 스케일**만(추가 크롭 없음). 슬롯 종횡비가 프레임 내 다르면 슬롯별 중앙 크롭 보정.
- 출력 해상도 = **프레임 원본 해상도** 기준(§F4).
- 출력 포맷 = `AppSettings.outputFormat`(기본 JPG, JPG/PNG).
- 컷→슬롯 매핑: 선택 순서대로 슬롯 채움, **정확히 슬롯 수만큼**(빈 슬롯 없음, §9 #29).

---

## 4. 화면 & 상태 머신 (§4/§5, BM 흐름)

### 4.1 상태 전이 (키오스크 세션)

```
[대기/홈]
  └(촬영하기)→ [로그인/게스트 선택]
        ├(게스트)→ [프레임 선택: 기본만]
        └(로그인)→ [로그인]→ [프레임 선택: 기본+커스텀]
  → [촬영 안내]         (타이머·방식·선택방식, BM②)
  → (촬영하기)→ [촬영/카운트다운]  (N컷, [바로촬영], 세션 녹화, BM 없음)
  → [컷 선택]           (슬롯 수만큼, BM③)
  → [결과]              (미리보기+필터, 프레임 고정, BM④) → 합성+타임랩스 생성
  → (QR on) 업로드(TTL)→ [QR 팝업]  (스캔=다운로드페이지, N시간 후 삭제 고지, BM⑤)
     (QR off) 로컬 저장(saveLocalCopy on 시)
  → [완료/감사] → [대기/홈]
```

- **유휴 타임아웃**: 촬영/선택/편집 중 60~90초 무동작 → 진행 취소·임시데이터 폐기·대기화면 복귀(§10, `IIdleWatchdog`).
- **예외 복구**: 무인 상정, 예외 발생 시 대기화면 자동 복귀(§8 안정성).
- **키오스크 세션**: 1회 세션 종료 후 자동 게스트 대기화면 복귀(§F8).

### 4.2 화면 목록 (PRD §5, 15종)

홈 / 로그인·게스트선택 / 로그인·계정허브 / 계정생성 / 프레임선택 / 프레임편집기 / 촬영안내 / 촬영준비·프리뷰 / 촬영(카운트다운) / 컷선택 / 결과 / QR팝업 / 완료 / 관리자모드 / 사용자관리(power).

### 4.3 가로/세로 레이아웃 자동 (§1, §9 #20)

- 화면 방향(가로/세로) 감지 → 방향별 레이아웃 자동. 프레임은 자기 비율대로 화면 중앙 정렬.
- 구현: `DataTemplate`/`VisualStateManager` 또는 방향별 UserControl 스왑. 반응형(창모드 최소 1280×720).
- 표시 모드: 기본 fullscreen, 창모드 옵션(테스트). 창모드 마지막 크기·위치 복원(`windowBounds`).

---

## 5. 계정·권한 (§F8)

### 5.1 역할

| 역할 | 권한 |
|------|------|
| `user` | 자기 프레임(최대 10) + **AppSettings 관리**(자기 인스턴스 관리자) |
| `manager` | user + 사용자 관리(타 계정 조회·삭제·pw 초기화) + 공용 기본 프레임 관리 |
| `admin` | manager + **manager 지정** (최종 1인) |

- 로그인은 선택(게스트=기본 프레임만). 인증 절차(이메일·OAuth·2FA) 전부 제외, id/pw만.
- 시드 계정: `devmcjo`/`1111`(admin) 사전 등록.
- 관리자 모드 진입: 좌상단 3초 롱프레스 → 로그인 → 로그인 계정이면 진입. 로그인 화면 = 계정 허브(로그아웃·비밀번호 변경·관리자 모드).
- **계정 삭제 cascade**: 소유 프레임(Firestore 문서 + Storage 이미지) 함께 삭제(§F8, firebase-contract §5).

### 5.2 ⚠️ MVP 보안 한계 (명시)

- 비밀번호 **평문 저장**(MVP, 개인 사용 전제). → `User` 컬렉션 노출 = 전체 계정 유출. **웹에서 User 접근 절대 차단**(firebase-contract 보안 규칙 필수).
- 외부 배포 시 최소 비밀번호 해싱·세션 보호 필요(후순위, §8).

---

## 6. Firebase 접근 전략 (핵심 결정)

### 6.1 요금제 현실 (F-3)

- **Cloud Storage는 Blaze 필수**(2026-02 시행). 영상·이미지 업로드 = Blaze 전환 필요. Always Free 한도 내 $0 유지 가능하나 카드 등록 강제.
- Firestore는 Spark 무료 유지.
- **완화책**: Blaze 전환 불가/미룸 시 → **QR 전송(F5) off + 로컬 저장(F4)** 조합으로 MVP 데모 가능하도록 설계(둘은 독립). 이 완화 경로가 A-4의 안전망.

### 6.2 접근 방식 결정

| 대상 | MVP 1차 (개인 사용) | 배포 시 전환 |
|------|--------------------|-------------|
| Firestore(User/Frame/Session) | **FirebaseAdmin(서비스 계정)** — 규칙 우회, 개발 최속 | 규칙 준수: Firebase Auth ID토큰 + Firestore REST |
| Storage(파일 업로드/삭제) | Admin SDK 또는 Google.Cloud.Storage.V1 | 규칙 준수 클라이언트 업로드 |
| 다운로드 URL | **Firebase 다운로드 토큰 URL** (`?alt=media&token=<uuid>`) | 동일 |

### 6.3 다운로드 URL 형태 (F-5, firebase-contract §4)

- **Firebase 다운로드 토큰 URL** 채택: `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{path}?alt=media&token={uuid}`.
- 이유: 웹 다운로드 페이지는 ResultSession 문서에서 URL 문자열만 읽어 브라우저가 직접 파일 fetch. 방문자 인증·Storage read 규칙 부여 불필요. 토큰은 추측 불가(§10 다운로드 URL 보안).
- 대안(서명 URL, ≤7일 만료)은 서비스 계정 서명 필요·QR 유효기간 제약 → MVP는 토큰 URL 우선.

### 6.4 ⚠️ 서비스 계정 키 트레이드오프 (반드시 인지)

- **위험**: 서비스 계정 JSON을 배포 바이너리에 번들 = 추출 시 프로젝트 전체 god-mode. Google 공식은 Admin SDK를 "신뢰 서버 전용"으로 명시.
- **MVP 판단**: 본인 PC 개인 사용 단계에서만 Admin SDK 직결 허용(A-5). **배포/공유 시 반드시 규칙 준수 경로로 전환**. 키는 절대 git·인스톨러에 포함 금지, 로컬 보호 위치(`%ProgramData%\MCPhoto\` 권한 제한)에서 로드.
- 인터페이스 `IUploadService`/`IFirebaseClient`를 추상화해 구현 교체가 코드 변경 최소로 되도록 설계(전환 비용 최소화).

### 6.5 TTL/만료 삭제 분담 (F-3, firebase-contract §6)

- 데스크톱 앱이 **직접 삭제** 1차(정밀 타이밍, Firestore 문서 + Storage 파일 동시): 앱 시작 시 또는 주기적으로 `expiresAt < now`인 ResultSession 스캔 → Storage `results/{sessionId}/` 삭제 + 문서 삭제.
- **GCS Lifecycle 규칙**(age 기반) 안전망: 앱이 못 지운 잔여물 자동 청소.
- **로컬 저장분은 TTL 무관**(§F4, 영구 보존).
- 스케줄 Cloud Functions는 Blaze 필요·상시 서버 부재로 MVP 제외. (웹 측 계약은 firebase-contract에 기술 — js-architect가 Functions 채택 여부 결정)

---

## 7. 설정 & 저장소 (§6 AppSettings, §9 #38)

- 모든 설정 로컬 **INI 파일** 저장(`%ProgramData%\MCPhoto\MCPhoto.ini` 또는 쓰기 가능 실행 경로). 재시작 시 복원.
- 항목: cutCount, countdownSec, mirrorMode, flashMode, outputFormat, retentionHours, displayMode, windowBounds, enableQrDelivery, saveLocalCopy, localSavePath, cameraDevice.
- 창모드 마지막 크기·위치(`windowBounds`) 복원.
- **데이터 폴더**: 런타임 갱신 데이터(기본 프레임 캐시·설정)는 `%ProgramData%\MCPhoto\`(쓰기 가능). `Program Files`(보호됨) 회피(§10 설치·배포).
- **로컬 결과물**: 기본 `{실행경로}\result\mcphoto_YYMMDD_HHMM\`(변경 가능 `localSavePath`), 사진+영상. TTL 무관.

---

## 8. 비기능 (§8, §10)

- **성능**: 촬영 응답 지연 최소, 합성·영상·업로드 중 진행 상태 표시.
- **안정성**: 무인 동작, 예외 시 대기화면 자동 복귀, 유휴 타임아웃 리셋.
- **개인정보**: 촬영 원본·중간 산출물은 세션 종료 시 로컬 삭제. 최종물은 saveLocalCopy on일 때만 보존. 업로드본은 retentionHours 후 자동 삭제.
- **네트워크**: 로그인·커스텀 프레임도 Firestore 의존 → 오프라인 시 게스트+번들 프레임 모드(QR 불가 안내). 업로드 실패 시 재시도·오류 안내, **업로드 성공 후에만 QR 노출**(§10).

---

## 9. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|--------|------|------|------|
| R1 | 웹캠이 1080p/30fps 미지원 | 프리뷰·녹화 품질 저하 | 장치 해상도 폴백, MJPG FourCC | WBS Step 3 |
| R2 | Blaze 전환 불가 | Storage 업로드 불가 = F5 마비 | QR off + 로컬 저장 완화 경로 | WBS Step 5/8 |
| R3 | 서비스 계정 키 유출 | 프로젝트 god-mode | 배포 시 규칙 준수 전환, 키 로컬 보호·git 제외 | WBS Step 8 |
| R4 | ffmpeg libx264 GPL 배포 의무 | 라이선스 위반 소지 | 배포 ffmpeg 빌드 라이선스 확인, LGPL 빌드 검토 | WBS Step 6/12 |
| R5 | WriteableBitmap 30fps 렌더 회귀(.NET 6+) | 프리뷰 끊김 | 대상 런타임 실측, NearestNeighbor·재사용 버퍼 | WBS Step 3 |
| R6 | 슬롯 종횡비 상이 프레임 크롭 오차 | 합성 어긋남 | 대표 슬롯 크롭 + 슬롯별 중앙 크롭 보정 | WBS Step 7 |
| R7 | 무인 예외로 앱 hang | 키오스크 다운 | 전역 예외 핸들러→대기 복귀, 로깅 | WBS Step 4 |

---

## 10. js-architect 인계 (파이프라인 간 계약)

- **계약 문서**: `docs/design/firebase-contract.md` — Firestore 스키마(User/FrameTemplate/ResultSession), Storage 경로(`results/{sessionId}/`, `frames/{userId}/`), 다운로드 페이지 URL·토큰 규칙, 보안 규칙 요구사항, TTL/expiresAt 의미론.
- **WPF가 담당**: 업로드(최종 이미지+타임랩스) → ResultSession 문서 생성 → downloadPageUrl 산출 → QR 생성·표시. 즉 웹은 **읽기 전용 소비자**.
- **웹이 담당(범위 밖, 계약만)**: 다운로드 페이지 UI, Hosting 배포, 보안 규칙 파일, (선택) TTL 정리 Functions.

---

## 부록 A. 캡처 파이프라인 골격 (참고)

```csharp
// 캡처 스레드
using var cap = new VideoCapture(deviceIndex, VideoCaptureApis.DSHOW);
cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M','J','P','G'));
cap.Set(VideoCaptureProperties.FrameWidth, 1920);
cap.Set(VideoCaptureProperties.FrameHeight, 1080);
using var frame = new Mat();
while (running && cap.Read(frame))
{
    if (mirror) Cv2.Flip(frame, frame, FlipMode.Y);       // 거울(WYSIWYG)
    using var processed = new Mat(frame, cropRoi).Clone(); // 슬롯 종횡비 중앙 크롭
    dispatcher.Invoke(() => WriteableBitmapConverter.ToWriteableBitmap(processed, _previewWb)); // 프리뷰
    if (recording) _ffmpegStdin.Write(processed.Data, 0, (int)(processed.Total()*processed.ElemSize())); // 녹화
    if (shutterRequested) _cutBuffer.Add(processed.Clone()); // 스틸
}
```

## 부록 B. 참고 출처

- OpenCvSharp: github.com/shimat/opencvsharp · nuget.org/packages/OpenCvSharp4.Windows
- WriteableBitmap: learn.microsoft.com/dotnet/api/system.windows.media.imaging.writeablebitmap
- ffmpeg 파이프/타임랩스: ffmpeg.org, setpts 필터
- Firebase Storage Spark 변경(핵심): firebase.google.com/docs/storage/faqs-storage-changes-announced-sept-2024
- Firestore REST: firebase.google.com/docs/firestore/use-rest-api
- QRCoder: github.com/codebude/QRCoder
