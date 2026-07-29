# MC포토 (MCPhoto)

**키오스크형 셀프 포토부스** — WPF/.NET 8 데스크톱 앱에서 촬영·합성·타임랩스를 만들고, Firebase로 업로드해 **QR 코드**로 모바일에서 사진·영상을 내려받는 시스템입니다.

> 앱 표시명은 `branding.ini`로 바꿀 수 있습니다(기본값 "MC Photo"). 아래 [설정](#-설정) 참고.

---

## ✨ 주요 기능

| 영역 | 기능 |
|------|------|
| 촬영 | N컷 연속 촬영(6/8/10), 컷당 카운트다운, [바로 촬영], 플래시(화면 하양), 거울모드, 카메라 Ready 게이트 |
| 프레임 | 프레임 선택은 누구나, 생성·편집(슬롯 좌표/종횡비)·삭제는 **고급 유저 이상**. 공용(서버) 프레임 관리는 매니저 이상 |
| 후처리 | 필터(원본·흑백·밝게·뷰티, 결과 화면에서 실시간 반영), 컷 합성, 배속 타임랩스(ffmpeg) |
| 전달 | QR 전송(사진·타임랩스 개별 토글), 모바일 다운로드 페이지, 보관시간(1~72h) 후 자동 만료. 임시 유저는 QR 전송에 시간·횟수 한도(서버 판정) |
| 운영 | 로컬 저장, 유휴 감시(무동작 시 경고 후 홈 복귀, 로그아웃 없음), 카메라 테스트 모달, 진단 모달, 전체화면/창모드 |
| 계정 | 게스트 / **Google 로그인(SSO)**, 역할 5종(임시 유저·사용자·고급 유저·매니저·관리자), 설정·계정 관리 진입은 **PIN 게이트**, 관리자 도구(계정·프레임 관리) |

## 🧱 기술 스택

- **데스크톱**: .NET 8, WPF, MVVM([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)), DI(`Microsoft.Extensions.Hosting`), 로깅([Serilog](https://serilog.net/))
- **영상/이미지**: [OpenCvSharp4](https://github.com/shimat/opencvsharp)(카메라·필터·합성), [ffmpeg](https://ffmpeg.org/)(세션 녹화·타임랩스)
- **백엔드**: Firebase — Cloud Firestore, Cloud Storage. 앱은 **Cloud Functions(2nd gen, TypeScript) HTTPS API를 경유**하며, Admin 자격증명은 서버에만 둡니다(앱에 서비스 계정 키 없음). QR: [QRCoder](https://github.com/codebude/QRCoder)
- **인증**: Google SSO(시스템 브라우저 + loopback + PKCE) → 서버가 발급한 JWT로 API 호출
- **웹**: Firebase Hosting(바닐라 JS 다운로드 페이지) + Cloud Functions(백엔드 API)

## 📁 프로젝트 구조

```
photobooth/
├─ MCPhoto.sln
├─ src/
│  ├─ MCPhoto.Core/        # 도메인 모델·설정(INI)·브랜딩·내비게이션·계약(인터페이스)
│  ├─ MCPhoto.Capture/     # OpenCvSharp 카메라, ffmpeg 녹화/타임랩스, 합성, 필터
│  ├─ MCPhoto.Http/        # 백엔드(HTTPS API) 클라이언트 — 계정·프레임·업로드·설정
│  └─ MCPhoto.App/         # WPF UI(Views/ViewModels), DI 부트스트랩, 상태머신
├─ tests/MCPhoto.Tests/    # 단위·headless XAML 회귀 테스트 (721개)
├─ web/
│  ├─ public/              # 모바일 다운로드 페이지(바닐라 JS)
│  ├─ functions/           # 백엔드 API (Cloud Functions 2nd gen, TypeScript)
│  ├─ *.rules              # Firestore/Storage 보안 규칙
│  └─ deploy-web.bat       # 웹·Functions 배포 스크립트 (+ OPS-ttl.md 운영문서)
├─ installer/              # Inno Setup 스크립트 (MCPhoto.iss)
├─ tools/ffmpeg/           # 번들 ffmpeg.exe (타임랩스용)
├─ docs/                   # PRD·설계·분석 문서  ← 상세는 docs/analysis/ 참고
├─ publish.bat / publish.ps1  # 단일 EXE 배포 스크립트
└─ Frame/                  # 기본(번들) 프레임 이미지
```

## 🚀 빠른 시작

```bash
# 요구: .NET 8 SDK (Windows)
dotnet build MCPhoto.sln -c Debug        # 빌드
dotnet test  MCPhoto.sln                 # 테스트 (721개)
dotnet run  --project src/MCPhoto.App    # 실행
```

- 일반 빌드 산출물: `src/MCPhoto.App/bin/Debug/net8.0-windows/`
- **웹캠**이 없어도 앱은 실행됩니다(카메라 미연결 처리).
- **백엔드에 도달할 수 없으면**(오프라인 / 게이트 키 미설정) 로그인·업로드·QR은 실패하지만 **게스트 촬영과 로컬 저장은 계속 동작**합니다. 오프라인 로그인 폴백은 없습니다.
- 웹/백엔드 테스트: `web/functions`에서 `npm test`(Jest), `web`에서 `npm run test:rules`(Firestore·Storage 규칙 Emulator 테스트).

## 📦 배포 (단일 EXE)

베타 테스터에게 **`.exe` 하나**만 전달하려면:

```
publish.bat  더블클릭    (또는)  powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

- 출력: **`publish/MCPhoto/MCPhoto.exe`** (자체 포함 단일 파일 — 대상 PC에 .NET 설치 불필요)
- `tools/ffmpeg/ffmpeg.exe`가 함께 번들되어 **타임랩스**가 동작합니다.
- **백엔드 게이트 키**는 publish 시 exe에 내장됩니다 — 환경변수 `MCPHOTO_BACKEND_API_KEY` → 저장소 루트 `backend-apikey.local`(git-ignored) 순으로 먼저 찾은 값. 키를 못 찾아도 배포는 성공하지만, 그 exe는 대상 PC의 `MCPhoto.ini`에 `BackendApiKey=<key>`를 넣어야 백엔드 인증을 통과합니다.
- Firebase **서비스 계정 키는 번들되지 않습니다**(Admin 권한은 백엔드에만 존재).
- 설치 프로그램이 필요하면 [`installer/MCPhoto.iss`](installer/MCPhoto.iss)(Inno Setup), 웹·Functions 배포는 `web/deploy-web.bat`.
- 자세한 내용: [`docs/analysis/80-build-and-deployment.md`](docs/analysis/80-build-and-deployment.md)

## ⚙️ 설정

| 파일 | 위치(우선순위) | 용도 |
|------|----------------|------|
| `branding.ini` | 실행 폴더 → `%ProgramData%\MCPhoto\` | 앱 표시명 변경(`[Branding] AppName=...`), 없으면 "MC Photo". 샘플: `branding.ini.sample` |
| `MCPhoto.ini` | 실행 폴더 → `%ProgramData%\MCPhoto\` → `%LocalAppData%\MCPhoto\` | 촬영·필터·QR·표시모드·카메라 등 앱 설정 |
| `bldinfo.ini` | 실행 폴더 → `%ProgramData%\MCPhoto\` | 화면 표시용 빌드 정보(`Version`·`BuildDate`·`Site`), 없으면 `0.0.0` |

앱 화면의 **설정**에서도 대부분 항목을 편집·저장할 수 있습니다.

> 백엔드 주소(`BackendBaseUrl`)와 Google 클라이언트 ID(`GoogleClientId`)는 운영 프로젝트 값이 코드에 기본 내장되어 있어 보통 `MCPhoto.ini`에 적지 않아도 됩니다. 다른 백엔드/구글 프로젝트를 쓸 때만 해당 키로 덮어씁니다.

## 🩺 로그 / 문제 진단

- 로그: **`%ProgramData%\MCPhoto\logs\mcphoto-*.log`** (일자별, Serilog)
- 증상별 원인 위치·로그 키워드 매핑: [`docs/analysis/70-logging-and-troubleshooting.md`](docs/analysis/70-logging-and-troubleshooting.md)

## 📚 상세 문서

프로젝트 전체를 문서만으로 이해할 수 있도록 [`docs/analysis/`](docs/analysis/)에 영역별로 정리되어 있습니다.

| 문서 | 내용 |
|------|------|
| `00-overview-and-architecture.md` | 전체 아키텍처·컴포넌트·데이터 흐름 |
| `10~12-exe-app-*.md` | WPF 앱 구조 / 기능 상세 / 설정·구성 |
| `20-frontend-web-download-page.md` | 웹 다운로드 페이지 |
| `30-backend-firebase-integration.md` | Firebase 연동(초기화·업로드·QR) |
| `40-database-firestore-and-storage-schema.md` | Firestore/Storage 스키마·경로·규칙 |
| `50-infra-gcp-lifecycle-and-ttl.md` | GCP 인프라·보관/만료(Lifecycle·TTL) |
| `60-auth-accounts-and-roles.md` | 로그인·계정·역할 권한 |
| `70-logging-and-troubleshooting.md` | 로그 위치·이슈 진단 가이드 |
| `80-build-and-deployment.md` | 빌드·단일 EXE 배포 |
| `90-roadmap-and-future-work.md` | 추후 개발·미결정·비범위 |

이 밖에 제품 요구사항은 [`docs/prd/`](docs/prd/), 이터레이션별 설계·WBS·인터페이스 계약(WPF ↔ 백엔드 ↔ 웹)은 [`docs/design/`](docs/design/)에 있습니다.

> ⚠️ **기능을 추가/변경하면 `docs/analysis/`의 해당 문서도 함께 갱신**해 주세요.

---

_MC포토 · WPF(.NET 8) 셀프 포토부스 · 내부 프로젝트_
