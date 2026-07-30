# 80 · 빌드·배포 분석

| 항목 | 내용 |
|------|------|
| 문서 | WPF 앱 빌드/게시(단일 파일 publish)·ffmpeg 번들·인스톨러 구성 분석 |
| 범위 | `src/MCPhoto.App/MCPhoto.App.csproj`, `publish.ps1`·`publish.bat`, `Directory.Build.props`, `installer/MCPhoto.iss`, `branding.ini.sample`·`bldinfo.ini`, **백엔드 게이트 키 exe 내장**. 백엔드 계약은 [30](./30-backend-firebase-integration.md), 웹·Functions 배포는 `web/deploy-web.bat` |
| 최종 업데이트 | 2026-07-29 (it15 반영 — 참조 프로젝트 `MCPhoto.Firebase`→`MCPhoto.Http`, 게이트 키 내장 절차 추가, 서비스 계정 키 서술 정리) |
| 관련 소스 | `src/MCPhoto.App/MCPhoto.App.csproj`, `publish.ps1`, `publish.bat`, `Directory.Build.props`, `installer/MCPhoto.iss`, `src/MCPhoto.App/branding.ini.sample`, `tools/ffmpeg/ffmpeg.exe`, `publish/MCPhoto/`(산출물) |
| 갱신 규칙 | csproj 의 Target·복사 항목, publish 스크립트(게이트 키 주입 포함), iss 파일이 바뀌면 표/근거(`파일:라인`) 갱신. ffmpeg 경로/번들 방식 변경은 [10번](./10-exe-app-architecture.md) §4.5와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정.

> ⚠️ **이 문서는 Windows 데스크톱 배포 전용이다.** 단일 EXE publish·self-contained 런타임·ffmpeg 번들·Inno Setup 인스톨러는 다른 플랫폼에 이식할 대상이 아니다(스토어 배포·`.app` 공증·APK/AAB 서명 등 각자의 체계를 쓴다).
>
> **다른 플랫폼에서도 유효한 부분**: §2의 **배포 게이트 키를 빌드 시 바이너리에 주입하고 소스에 커밋하지 않는다**는 원칙이다. 플랫폼별 키 발급·주입·표시 금지 규약은 [41 §2.5](./41-local-data-and-file-formats.md), 키 검증 계약은 [31 §2](./31-backend-api-reference.md).

---

## 1. 빌드 구성 개요

| 요소 | 값 | 근거 |
|------|-----|------|
| 앱 프로젝트 | `src/MCPhoto.App/MCPhoto.App.csproj`(WinExe, WPF) | `MCPhoto.App.csproj:4-7` |
| TargetFramework | `net8.0-windows` | `MCPhoto.App.csproj:5` |
| AssemblyName | `MCPhoto`(→ `MCPhoto.exe`) | `MCPhoto.App.csproj:8` |
| 공통 속성 | `LangVersion 12.0`, `Nullable enable`, `ImplicitUsings enable`, `Deterministic true`, 회사=MCPhoto/제품=MC포토/`ko-KR` | `Directory.Build.props:4-17` |
| 매니페스트 | `app.manifest` | `MCPhoto.App.csproj:7` |
| 참조 프로젝트 | `MCPhoto.Core`, `MCPhoto.Capture`, **`MCPhoto.Http`** | `MCPhoto.App.csproj:23-26` |
| 조건부 어셈블리 속성 | `BackendApiKeyDefault`가 주어지면 `AssemblyMetadata("MCPhoto.BackendApiKey")`로 바이너리에 박힘(§2.1) | `MCPhoto.App.csproj:36-43` |

`Directory.Build.props`는 전 프로젝트 공통이며 `TreatWarningsAsErrors=false`지만 `AnalysisLevel=latest`로 경고를 감시한다(`Directory.Build.props:7-9`).

### 1.1 일반 빌드 산출 경로

| 명령 | 출력 | 근거 |
|------|------|------|
| `dotnet build` | `src\MCPhoto.App\bin\Debug\net8.0-windows\` | `publish.ps1:16-17` |
| `dotnet build -c Release` | `src\MCPhoto.App\bin\Release\net8.0-windows\` | `publish.ps1:17` |

일반 빌드에서는 프레임워크 의존(자체 .NET 설치 필요) 산출물이 나오며, ffmpeg·Frame·branding 은 출력 폴더 하위로 복사된다(§3, §4).

---

## 2. 단일 파일 publish

`publish.ps1`이 베타 배포용 단일 EXE 를 만든다(`publish.ps1:1-18`).

```powershell
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o $out
```

| 옵션 | 효과 | 근거 |
|------|------|------|
| `--self-contained true` | .NET 런타임 내장 → 대상 PC 에 .NET 설치 불요 | `publish.ps1:57` |
| `-r win-x64` | Windows x64 런타임 | `publish.ps1:57` |
| `PublishSingleFile=true` | 단일 실행 파일로 번들 | `publish.ps1:58` |
| `IncludeNativeLibrariesForSelfExtract=true` | 네이티브 라이브러리를 자기추출로 포함 | `publish.ps1:59` |
| `EnableCompressionInSingleFile=true` | 단일 파일 압축 | `publish.ps1:60` |
| `DebugType=none` | 디버그 심볼 제외 | `publish.ps1:61` |
| `-o publish\MCPhoto` | **고정 출력 경로**(항상 이 위치) | `publish.ps1:36,62` |
| `-p:BackendApiKeyDefault=<키>` | 키를 찾았을 때만 추가(§2.1) | `publish.ps1:64-65` |

- 출력은 항상 `publish\MCPhoto\MCPhoto.exe`(`publish.ps1:22,36`).
- 산출 파일 목록(확인됨): `MCPhoto.exe`, `tools\ffmpeg\ffmpeg.exe`, `Frame\*`(png·slots), `branding.ini.sample`, `bldinfo.ini`.

### 2.1 백엔드 게이트 키 exe 내장 (it15)

앱은 서비스 계정 키를 갖지 않는 대신, 백엔드의 게스트 엔드포인트를 통과할 **배포 게이트 키**(`X-MCPhoto-Client`)가 필요하다. publish 스크립트가 이 키를 exe에 심는다.

| 단계 | 동작 | 근거 |
|------|------|------|
| 1 | 키 소스 탐색: `$env:MCPHOTO_BACKEND_API_KEY` → 저장소 루트 `backend-apikey.local`(git-ignored). 먼저 찾은 값 사용 | `publish.ps1:45-54` |
| 2 | 찾았으면 `-p:BackendApiKeyDefault=<키>` 추가 → csproj가 `AssemblyMetadata("MCPhoto.BackendApiKey")`로 바이너리에 기록 | `publish.ps1:64-66`, `MCPhoto.App.csproj:36-43` |
| 3 | 못 찾아도 **publish는 성공**한다. 대신 경고 후 진행하며, 그 exe는 대상 PC의 `MCPhoto.ini`에 `BackendApiKey=<키>`가 있어야 백엔드 인증을 통과한다 | `publish.ps1:67-71` |
| 런타임 | `IniSettingsService`가 내장 키를 기본값으로 주입하고, INI 값이 있으면 그쪽이 우선. **INI에 다시 쓰지는 않는다**(평문 유출 방지) | `ServiceRegistration.cs:52-55,195-202` |

- 일반 빌드(`dotnet build`)에는 속성이 없어 내장 키도 없다 → 개발 PC는 INI 오버라이드가 필요하다.
- ⚠️ 내장 키는 디컴파일로 추출 가능한 **저가치·폐기 가능(revocable) 게이트 키**다. 실제 보안은 서버측(JWT + 역할 + 서버 전용 서비스 계정)이 담당한다(`publish.ps1:17-19`).

---

## 3. ffmpeg 번들 이슈와 해결

타임랩스 녹화·변환에 ffmpeg 가 **필수**다(`MCPhoto.App.csproj:28`). csproj 는 두 경로로 ffmpeg 를 다룬다.

| 경로 | 대상 빌드 | 방식 | 근거 |
|------|-----------|------|------|
| `None` 항목 + `CopyToOutputDirectory` | 일반 빌드/실행 | `tools/ffmpeg/ffmpeg.exe`를 출력 `tools\ffmpeg\`로 링크 복사, `Exists` 조건부 | `MCPhoto.App.csproj:34-39` |
| `CopyFfmpegToPublish` Target | 단일 파일 publish | `AfterTargets="Publish"`로 게시 완료 후 직접 Copy | `MCPhoto.App.csproj:43-48` |

### 3.1 문제와 해결

- 일반 빌드에서는 `None Include` + `CopyToOutputDirectory="PreserveNewest"`로 정상 복사된다(`MCPhoto.App.csproj:35-38`).
- 단일 파일 publish 에서는 **내부 임시 빌드에서 위 `None` 항목의 `Condition`이 뒤집혀** ffmpeg 가 누락된다(`MCPhoto.App.csproj:41-42`).
- 해결: `AfterTargets="Publish"` 시점에 메인 프로젝트 컨텍스트에서 `$(FfmpegSource)` → `$(PublishDir)tools\ffmpeg\ffmpeg.exe`로 직접 Copy 하는 Target 을 추가했다(`MCPhoto.App.csproj:43-48`). `SkipUnchangedFiles="true"`, 완료 메시지 출력.
- `FfmpegSource`는 `$(MSBuildProjectDirectory)\..\..\tools\ffmpeg\ffmpeg.exe`(리포 루트 `tools/ffmpeg/`)이며 존재할 때만 복사(부재 시 빌드 통과, `MCPhoto.App.csproj:29-31,43`).

### 3.2 ffprobe 제외

- 코드가 ffprobe 를 사용하지 않으므로 용량 절약을 위해 **제외**한다(`MCPhoto.App.csproj:28`).
- 리포 `tools/ffmpeg/`에는 `ffmpeg.exe`(~101MB)와 `ffprobe.exe`(~101MB)가 모두 있으나, csproj 는 `ffmpeg.exe`만 복사한다(`MCPhoto.App.csproj:30`). publish 산출물에도 `tools\ffmpeg\ffmpeg.exe`만 포함됨(확인됨, ffprobe 미포함).
- 타임랩스는 ffmpeg 에 의존하므로 번들 누락 시 타임랩스 기능이 동작하지 않는다(설계상 필수, `MCPhoto.App.csproj:28`).

---

## 4. 부가 리소스 복사 (Frame / branding)

| 리소스 | 소스 | 출력 | 근거 |
|--------|------|------|------|
| 기본 프레임 | `..\..\Frame\**\*.*`(리포 루트 `Frame/`) | 출력 `Frame\`(재귀 유지) | `MCPhoto.App.csproj:67-71` |
| 브랜딩 샘플 | `branding.ini.sample` | 출력 루트 | `MCPhoto.App.csproj:76` |
| 빌드 정보 | `bldinfo.ini` | 출력 루트 + publish 산출물(스크립트가 명시 복사) | `MCPhoto.App.csproj:82`, `publish.ps1:81-90` |

- 프레임 복사는 프레임 소스 우선순위 ②(번들 기본 프레임)에 해당한다. publish 산출물 `Frame\`에 `jport-camp.png`·`jport-camp.slots`·테스트 프레임 등이 포함됨(확인됨).
- `branding.ini.sample`(it9 C3): 고객이 `branding.ini`로 리네임해 앱 표시 이름을 변경한다. UTF-8 저장 필수, 적용 지점은 창 제목·홈 화면 타이틀·홈 소제목, 미존재/빈 값이면 기본 **"MC Photo" / "self custom photobooth"**. 샘플 내용은 `[Branding]`·`AppName`·`Subtitle` 3줄이다([12 §3](./12-exe-app-settings-and-config.md)).
- `bldinfo.ini`: 앱 하단 버전 표기용(`[General]` `Version`·`BuildDate`·`Site`). 원본이 없으면 publish는 경고만 하고 진행하며 앱은 폴백 `0.0.0`을 표시한다([12 §6](./12-exe-app-settings-and-config.md)).

---

## 5. publish 스크립트 사용법

| 파일 | 용도 | 근거 |
|------|------|------|
| `publish.bat` | 더블클릭 진입점. `powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1` 호출 후 키 대기 | `publish.bat:8,13,18` |
| `publish.ps1` | 실제 게시 로직(§2) | `publish.ps1` 전체 |

- 권장 사용: `publish.bat` 더블클릭. 또는 `powershell -ExecutionPolicy Bypass -File .\publish.ps1`.
- **실행 중 잠금 경고**: `Get-Process MCPhoto`로 앱 실행을 감지하면(출력 exe 잠김) 게시를 중단하고 "Close the app, then run again." 경고 후 return 한다(`publish.ps1:38-43`).
- 게시 후 `bldinfo.ini`를 명시 복사한다 — 단일 파일 publish가 csproj `None` 항목을 누락시킬 수 있기 때문(`publish.ps1:81-91`).
- 성공 시 산출물 파일별 크기(MB) 목록을 출력한다(`publish.ps1:93-95`).
- **인코딩 주의(배치 스크립트 공통 규칙)**: `publish.bat`·`publish.ps1`·`web/deploy-web.bat` 모두 의도적으로 **ASCII(영문)로 유지**한다. 한국어 Windows 의 cmd/PowerShell 5.1 에서 CP949/UTF-8 mojibake 를 피하기 위함(`publish.bat:6`).
  - ⚠️ **단순 표시 문제가 아니라 실행 사고로 이어진다.** cmd 는 배치 파일의 읽기 위치를 **바이트 오프셋**으로 추적하는데, `chcp` 로 코드페이지가 바뀐 상태에서 파일에 멀티바이트 문자가 있으면 오프셋이 문자 중간에 떨어져 **줄의 나머지가 명령으로 실행**된다. `REM` 주석도 예외가 아니다.
  - 2026-07-29 실제 사고: `deploy-web.bat` 의 한글 주석 2줄을 편집했더니 더블클릭 실행 시 주석 안의 `firebase functions:secrets:set` 이 **실제로 호출**됐다(인자 불일치로 거부되어 시크릿 변경은 없었음). 조치 = 파일 전체 ASCII 영문화.
  - 규칙: 배치 파일에는 **비ASCII 문자 금지** + **주석에 실행 가능한 명령 문자열 금지**. ASCII 전용이면 1바이트=1문자라 `chcp 65001`(CLI 의 UTF-8 출력용)을 써도 안전하다.

---

## 6. 인스톨러 (Inno Setup)

`installer/MCPhoto.iss`가 Inno Setup 스크립트다(WBS Step 12).

| 항목 | 값 | 근거 |
|------|-----|------|
| AppName / Version / Publisher | MC포토 / 1.0.0 / MCPhoto | `MCPhoto.iss:9-11,19-21` |
| 설치 경로 | `{autopf}\MCPhoto`(Program Files) | `MCPhoto.iss:22` |
| 산출물 파일명 | `MCPhoto-Setup-1.0.0` | `MCPhoto.iss:29` |
| 압축 | lzma2 + SolidCompression | `MCPhoto.iss:26-27` |
| 아키텍처 | x64compatible | `MCPhoto.iss:28` |
| 언어 | 한국어(Korean.isl) | `MCPhoto.iss:33` |
| 소스(`PublishDir`) | 기본 `..\publish`, `iscc /DPublishDir=...`로 override 가능 | `MCPhoto.iss:14-16` |

### 6.1 산출물 구성

`[Files]`에서 publish 산출물 전체(`{#PublishDir}\*`)를 `{app}`으로 재귀 복사한다(`MCPhoto.iss:38-39`). 결과적으로 설치본은:

- `MCPhoto.exe`(앱 바이너리 — 백엔드 게이트 키 내장)
- `tools\ffmpeg\ffmpeg.exe`(타임랩스용 번들)
- `Frame\`(기본 프레임)
- `branding.ini.sample`, `bldinfo.ini`

### 6.2 자격증명 차단 (보안)

- `[Files]`의 `Excludes`가 서비스 계정/자격증명 키를 차단한다: `*serviceaccount*.json`, `*service-account*.json`, `*firebase*credentials*.json`, `*firebase-adminsdk*.json`, `serviceAccountKey.json`, `*.pem`, `*.key`(`MCPhoto.iss:38-39`).
- **it15 이후 앱은 애초에 서비스 계정 키를 쓰지 않는다** — Admin 자격증명은 백엔드(Cloud Functions)에만 존재하고 앱은 배포 게이트 키 + 로그인 JWT로만 백엔드를 호출한다([30 §3](./30-backend-firebase-integration.md)). 따라서 이 `Excludes`는 과거 산출물·실수 유입에 대비한 **방어적 잔존 규칙**이며 제거하지 않고 유지한다.
- 앱이 실제로 지니는 반비밀은 **백엔드 게이트 키**(exe 내장, §2.1)뿐이며, 유출 시 서버에서 해당 키만 폐기하면 된다.

### 6.3 데이터 폴더·아이콘·언인스톨

| 섹션 | 내용 | 근거 |
|------|------|------|
| `[Dirs]` | `%ProgramData%\MCPhoto`(+`\logs`,`\cache`) 쓰기 가능(users-modify). Program Files 쓰기 회피 | `MCPhoto.iss:42-45` |
| `[Icons]` | 시작 메뉴·바탕화면(선택) 바로가기 | `MCPhoto.iss:47-50` |
| `[Tasks]` | `desktopicon`(바탕화면 바로가기 옵션) | `MCPhoto.iss:52-53` |
| `[Run]` | 설치 후 앱 실행(nowait, skipifsilent) | `MCPhoto.iss:55-56` |
| `[UninstallDelete]` | cache·logs 정리, 사용자 설정/결과물은 보존 | `MCPhoto.iss:58-61` |

> 참고: iss 헤더 주석(`MCPhoto.iss:6`)은 `--self-contained false` publish 예시를 든다. 반면 실제 `publish.ps1`은 `--self-contained true`(단일 파일)로 게시한다(`publish.ps1:33`). iss 는 `PublishDir`의 산출물 전체를 그대로 담으므로 어느 쪽이든 동작하나, 두 문서의 self-contained 값이 불일치한다 → 배포 시 어떤 publish 산출물을 iss 소스로 쓸지 확정 필요(**가정**: 단일 파일 산출물 기준).

---

## 7. 상호 참조

- ffmpeg 를 쓰는 타임랩스·캡처 파이프라인: [10 · Exe 앱 아키텍처](./10-exe-app-architecture.md) §4.5·§4.8, 기능 관점은 [11](./11-exe-app-features.md).
- 백엔드 게이트 키·인증 계약: [30 · 백엔드 API 연동](./30-backend-firebase-integration.md) §2.1·§3.
- 앱 설정 파일(`MCPhoto.ini`·`branding.ini`·`bldinfo.ini`): [12 · 설정/구성](./12-exe-app-settings-and-config.md).
- 웹·Functions 배포는 WPF 빌드와 별개 파이프라인(`web/deploy-web.bat`): [20 · 프론트엔드](./20-frontend-web-download-page.md) §9.
