# 80 · 빌드·배포 분석

| 항목 | 내용 |
|------|------|
| 문서 | WPF 앱 빌드/게시(단일 파일 publish)·ffmpeg 번들·인스톨러 구성 분석 |
| 범위 | `src/MCPhoto.App/MCPhoto.App.csproj`, `publish.ps1`·`publish.bat`, `Directory.Build.props`(버전 원천), `installer/MCPhoto.iss`, `branding.ini.sample`, **백엔드 게이트 키 exe 내장**. 백엔드 계약은 [30](./30-backend-firebase-integration.md), 웹·Functions 배포는 `web/deploy-web.bat` |
| 최종 업데이트 | 2026-08-12 (.NET 10 이관 + 인스톨러 정비 — AppVersion exe 판독·PublishDir 정정·[Files] 화이트리스트·**package.bat 신설**로 패키징 분리) · 이전 2026-07-30 (it18 — `bldinfo.ini` 폐기, 버전은 `Directory.Build.props`의 `<Version>` 단일 원천) |
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
| TargetFramework | `net10.0-windows` | `MCPhoto.App.csproj:5` |
| AssemblyName | `MCPhoto`(→ `MCPhoto.exe`) | `MCPhoto.App.csproj:8` |
| 공통 속성 | `LangVersion 12.0`, `Nullable enable`, `ImplicitUsings enable`, `Deterministic true`, 회사=MCPhoto/**제품=MCPhoto**/`ko-KR` | `Directory.Build.props` |
| 버전 | `<Version>`이 원천 → `AssemblyVersion`·`FileVersion` = `$(Version).0`. `IncludeSourceRevisionInInformationalVersion=false`로 제품 버전에 git 해시가 붙지 않게 한다 | `Directory.Build.props` |
| 매니페스트 | `app.manifest` | `MCPhoto.App.csproj:7` |
| 참조 프로젝트 | `MCPhoto.Core`, `MCPhoto.Capture`, **`MCPhoto.Http`** | `MCPhoto.App.csproj:23-26` |
| 조건부 어셈블리 속성 | `BackendApiKeyDefault`가 주어지면 `AssemblyMetadata("MCPhoto.BackendApiKey")`로 바이너리에 박힘(§2.1) | `MCPhoto.App.csproj:36-43` |

`Directory.Build.props`는 전 프로젝트 공통이며 `TreatWarningsAsErrors=false`지만 `AnalysisLevel=latest`로 경고를 감시한다(`Directory.Build.props:7-9`).

### 1.1 일반 빌드 산출 경로

| 명령 | 출력 | 근거 |
|------|------|------|
| `dotnet build` | `src\MCPhoto.App\bin\Debug\net10.0-windows\` | `publish.ps1:16-17` |
| `dotnet build -c Release` | `src\MCPhoto.App\bin\Release\net10.0-windows\` | `publish.ps1:17` |

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
- 산출 파일 목록(확인됨): `MCPhoto.exe`, `tools\ffmpeg\ffmpeg.exe`, `Frame\*`(png·slots), `branding.ini.sample`. (it18: `bldinfo.ini` 제거 — 버전 정보는 exe 자신이 갖는다)

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

### 3.3 라이선스 고지 동봉 (필수 — 2026-08-06)

번들 ffmpeg 는 **GPLv3**(`--enable-gpl --enable-version3`)다. 재배포자로서 라이선스 전문·고지·대응 소스 안내를 배포물에 **반드시 동봉**해야 한다(GPLv3 §4·§6). ffmpeg 와 동일한 이중 배선을 쓴다.

| 경로 | 대상 빌드 | 방식 |
|------|-----------|------|
| `None` 항목 + `CopyToOutputDirectory` | 일반 빌드/실행 | 리포 루트 `licenses/**` → 출력 `licenses\` |
| `CopyLicensesToPublish` Target | 단일 파일 publish | `AfterTargets="Publish"`로 게시 완료 후 직접 Copy |

- 산출물 5개(it24 개명·추가 반영):
  - `FFmpeg-COPYING.GPLv3.txt` — GPLv3 전문. **원문 그대로**(674줄·LF·BOM 없음)이며 서식 통일 대상에서 제외한다.
  - `FFmpeg-NOTICE.txt` — 버전·저작권·configuration 전문·소스 URL 2곳·3년 서면 오퍼·추가제약 없음·상표 (종전 `FFmpeg-README.txt`).
  - `NOTICE.txt` — 고지 색인 (종전 `README.txt`).
  - `MCPhoto-LICENSE-MIT.txt` — 루트 `LICENSE`의 **링크 복사**(물리 사본을 두지 않는다).
  - `notice-manifest.json` — 앱 화면이 읽는 **요약 메타데이터**. 형식은 [41 §11](./41-local-data-and-file-formats.md).
- **인스톨러가 `licenses\`를 명시 항목으로 담는다**(§6.1). ⚠️ 2026-08-12 이전 서술("`{#PublishDir}\*`를 담으므로 자동 포함")은 **더 이상 참이 아니다** — `[Files]`가 화이트리스트로 바뀌면서 담을 것을 한 줄씩 열거하게 됐다. 결과(고지 5개가 설치본에 들어감)는 같지만 **경로가 다르다**: 이제 `licenses\*` 한 줄이 그 보장을 지고 있으므로, 그 줄을 지우면 고지가 조용히 사라진다. `notice-manifest.json`도 이 줄에 포함된다(확장자 제한 없음).
- ⚠️ **ffmpeg 를 계속 번들하는 한 이 폴더를 지우면 라이선스 위반이다.** `LicenseComplianceTests`가 "ffmpeg 복사 규칙이 살아 있으면 고지 4종(txt 3 + 매니페스트)이 있어야 한다"를 강제하고, 별도로 **출력 폴더 기준** 정합 테스트가 ① 매니페스트가 선언한 파일이 실제로 실리는지 ② 실린 `.txt`가 모두 선언되었는지 ③ 매니페스트의 버전·저작권·기준일이 txt 내용과 일치하는지를 검사한다. 반대로 ffmpeg 를 배포에서 빼면 그 시점에 의무가 소멸한다.
- 앱 내 고지는 설정 → 고급 → **[프로젝트 라이선스 고지]** (요약 카드 + 전문 2단 구조). 진단·상태 창에는 존재 여부 1줄만 남는다. 상세 [11 §19](./11-exe-app-features.md).
- 상세·후속 경로: [ffmpeg 라이선스·배포 설계](../design/wpf-ffmpeg-licensing-and-distribution-design.md).

---

## 4. 부가 리소스 복사 (Frame / branding)

| 리소스 | 소스 | 출력 | 근거 |
|--------|------|------|------|
| 기본 프레임 | `..\..\Frame\**\*.*`(리포 루트 `Frame/`) | 출력 `Frame\`(재귀 유지) | `MCPhoto.App.csproj:67-71` |
| 브랜딩 샘플 | `branding.ini.sample` | 출력 루트 | `MCPhoto.App.csproj:76` |
| 빌드 정보 | **동봉 파일 없음** (it18) | — | `Directory.Build.props`(`<Version>`), `AssemblyBuildInfoService.cs` |

- 프레임 복사는 프레임 소스 우선순위 ②(번들 기본 프레임)에 해당한다. publish 산출물 `Frame\`에 `jport-camp.png`·`jport-camp.slots`·테스트 프레임 등이 포함됨(확인됨).
- `branding.ini.sample`(it9 C3): 고객이 `branding.ini`로 리네임해 앱 표시 이름을 변경한다. UTF-8 저장 필수, 적용 지점은 창 제목·홈 화면 타이틀·홈 소제목, 미존재/빈 값이면 기본 **"MCPhoto" / "self custom photobooth"**. 샘플 내용은 `[Branding]`·`AppName`·`Subtitle` 3줄이다([12 §3](./12-exe-app-settings-and-config.md)).
- **버전 표기(it18)**: 동봉 파일이 없다. 앱 하단 버전은 **어셈블리 버전 리소스**에서, 진단 화면의 빌드 시각은 **exe `LastWriteTime`** 에서 읽는다. 릴리스 시 `Directory.Build.props`의 `<Version>` 한 줄만 올리면 exe 파일 속성의 버전과 앱 표기가 함께 바뀐다([12 §6](./12-exe-app-settings-and-config.md)).

---

## 5. publish 스크립트 사용법

| 파일 | 용도 | 근거 |
|------|------|------|
| `publish.bat` | 더블클릭 진입점. `powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1` 호출 후 키 대기 | `publish.bat:8,13,18` |
| `publish.ps1` | 실제 게시 로직(§2) | `publish.ps1` 전체 |

- 권장 사용: `publish.bat` 더블클릭. 또는 `powershell -ExecutionPolicy Bypass -File .\publish.ps1`.
- **실행 중 잠금 경고**: `Get-Process MCPhoto`로 앱 실행을 감지하면(출력 exe 잠김) 게시를 중단하고 "Close the app, then run again." 경고 후 return 한다(`publish.ps1:38-43`).
- 게시 후 산출된 exe의 **버전 리소스와 빌드 시각을 콘솔에 출력**한다(it18 — 복사할 파일이 없어졌으므로 확인용 출력으로 대체).
- 성공 시 산출물 파일별 크기(MB) 목록을 출력한다(`publish.ps1:93-95`).
- **인코딩 주의(배치 스크립트 공통 규칙)**: `publish.bat`·`publish.ps1`·`web/deploy-web.bat` 모두 의도적으로 **ASCII(영문)로 유지**한다. 한국어 Windows 의 cmd/PowerShell 5.1 에서 CP949/UTF-8 mojibake 를 피하기 위함(`publish.bat:6`).
  - ⚠️ **단순 표시 문제가 아니라 실행 사고로 이어진다.** cmd 는 배치 파일의 읽기 위치를 **바이트 오프셋**으로 추적하는데, `chcp` 로 코드페이지가 바뀐 상태에서 파일에 멀티바이트 문자가 있으면 오프셋이 문자 중간에 떨어져 **줄의 나머지가 명령으로 실행**된다. `REM` 주석도 예외가 아니다.
  - 2026-07-29 실제 사고: `deploy-web.bat` 의 한글 주석 2줄을 편집했더니 더블클릭 실행 시 주석 안의 `firebase functions:secrets:set` 이 **실제로 호출**됐다(인자 불일치로 거부되어 시크릿 변경은 없었음). 조치 = 파일 전체 ASCII 영문화.
  - 규칙: 배치 파일에는 **비ASCII 문자 금지** + **주석에 실행 가능한 명령 문자열 금지**. ASCII 전용이면 1바이트=1문자라 `chcp 65001`(CLI 의 UTF-8 출력용)을 써도 안전하다.

---

## 6. 인스톨러 (Inno Setup)

`installer/MCPhoto.iss`가 Inno Setup 스크립트다(WBS Step 12).

### 6.0 진입점 — 테스트용과 배포용을 분리한다

| 실행 | 하는 일 | 산출물 |
|---|---|---|
| `publish.bat` / `publish.ps1` | publish**만**. 인스톨러를 만들지 않는다 | `publish\MCPhoto\MCPhoto.exe` |
| **`package.bat` / `package.ps1`** | publish → Inno Setup 컴파일 | `installer\Output\MCPhoto-Setup-{버전}.exe` |
| `package.ps1 -SkipPublish` | 기존 publish 산출물을 그대로 패키징(`.iss`만 손볼 때) | 상동 |

- **왜 분리했나**: publish는 테스트용 내부 루프다. 패키징을 여기에 얹으면 테스트 산출물이 배포물처럼 보이는 사고가 난다. 배포 경로를 별도 진입점으로 두면 "이걸 실행하면 배포물이 나온다"가 명확해진다.
- **package는 기본적으로 다시 publish한다**: publish 출력 폴더는 재사용되므로, 거기 있는 것을 그냥 감싸면 **낡은 exe가 배포**될 수 있다(그런데 인스톨러 이름·버전은 그 exe에서 읽으므로 그럴듯해 보인다). 재빌드가 이 드리프트를 원천 제거한다. `.iss`만 반복 수정할 때만 `-SkipPublish`를 쓴다.
- **ISCC 탐색은 버전 폴더를 하드코딩하지 않는다**: Inno Setup 7(현행)은 6과 나란히 설치되고 32/64비트 에디션이 따로 있다. `Program Files` 양쪽 트리에서 `Inno Setup *` 폴더를 열거해 **파일 버전이 가장 높은 것**을 고르고, 없으면 PATH를 본다. 새 메이저가 나와도 이 스크립트를 고칠 필요가 없다.
  - ⚠️ 버전 비교는 `FileMajorPart`/`FileMinorPart`로 조립한다. `VersionInfo.FileVersionRaw`는 **PowerShell 7+ 전용 확장 속성**이고 `package.bat`은 Windows PowerShell 5.1을 부르므로, 그걸 쓰면 5.1에서 null끼리 비교해 조용히 잘못된 버전을 고른다.
- Inno Setup이 없으면 **명확한 오류로 끝난다**(설치 링크 안내 포함). publish 산출물은 그대로 쓸 수 있음을 함께 알린다.

| 항목 | 값 | 근거 |
|------|-----|------|
| `AppId`(제품 영구 신원) | `{9303675E-B66D-4D2E-A722-169F9E8865BC}` — **한번 배포하면 절대 변경 금지**(바꾸면 기존 설치가 업그레이드로 인식되지 않고 나란히 설치된다) | `MCPhoto.iss` `[Setup]` |
| AppName / Publisher | MCPhoto / MCPhoto | `MCPhoto.iss` `#define` |
| **AppVersion** | **하드코딩 폐기.** 패키징할 exe의 버전 리소스(`ProductVersion`)를 `GetStringFileInfo`로 읽는다 → `Directory.Build.props`의 `<Version>`이 유일 원천(§1)인 상태가 인스톨러까지 이어진다. `iscc /DAppVersion=x.y.z`로 override 가능 | `MCPhoto.iss` `#ifndef AppVersion` |
| 설치 경로 | `{autopf}\MCPhoto`(Program Files) | `MCPhoto.iss` `[Setup]` |
| 산출물 파일명 | `MCPhoto-Setup-{버전}` | `MCPhoto.iss` `OutputBaseFilename` |
| 설치 파일 버전 리소스 | `VersionInfoVersion`도 같은 값 — setup.exe 파일 속성에서 확인 가능 | `MCPhoto.iss` `[Setup]` |
| 압축 | lzma2 + SolidCompression | `MCPhoto.iss` `[Setup]` |
| 아키텍처 | x64compatible | `MCPhoto.iss` `[Setup]` |
| 언어 | 한국어(Korean.isl) | `MCPhoto.iss` `[Languages]` |
| 소스(`PublishDir`) | 기본 `..\publish\MCPhoto`(publish.ps1 출력 위치와 일치), `iscc /DPublishDir=...`로 override 가능 | `MCPhoto.iss` `#ifndef PublishDir` |

> ⚠️ **버전을 exe에서 읽는 이유**: 종전에는 `#define AppVersion "1.0.0"`이 하드코딩돼 있어 **1.1.19 앱이 1.0.0으로 설치·등록**됐다(제어판 표기·산출물 파일명 모두). 값을 전달하는 방식(`/D`)도 드리프트가 가능하지만, exe에서 직접 읽으면 **인스톨러가 자기가 담는 바이너리와 다른 버전을 말할 수 없다.** exe가 없으면 컴파일이 실패하는데, 이는 publish를 건너뛴 채 인스톨러를 만드는 사고를 막는 **의도된 실패**다.

> ⚠️ **종전 `PublishDir` 기본값이 틀려 있었다**: `..\publish`였는데 `publish.ps1`은 `publish\MCPhoto`에 쓴다. `recursesubdirs`가 `MCPhoto` 폴더째 담아 **`{app}\MCPhoto\MCPhoto.exe`로 설치**됐다(바로가기는 `{app}\MCPhoto.exe`를 가리켜 실행 실패).

### 6.1 산출물 구성 — 화이트리스트

`[Files]`는 **담을 것을 명시 열거한다.** 배포 대상은 **3개뿐**이다(사용자 확정, 2026-08-12):

| 담는 것 | 비고 |
|---|---|
| `MCPhoto.exe` | 자체 포함 단일 파일. 백엔드 게이트 키 내장(§2.1) |
| `licenses\` | 오픈소스 고지. **ffmpeg와 반드시 함께** 배포한다 — 한쪽만 담으면 GPLv3 고지 의무를 어긴다 |
| `tools\ffmpeg\ffmpeg.exe` | 타임랩스 인코딩 |

**담지 않는 것**과 그 이유:

| 제외 | 이유 |
|---|---|
| `MCPhoto.ini` | 앱이 최초 실행 시 스스로 만든다. 담지 않으면 `[Test]` 섹션 잔재가 배포물로 새는 경로도 함께 사라진다 |
| `branding.ini` | 운영자가 필요할 때 배치한다 |
| `Frame\` | 기본 프레임은 **서버에서 내려받는다**. `FrameCatalogService.BundleFolder`(`{exe}\Frame`)가 없으면 서버 목록·DB 캐시·폴백 렌더러로 대체된다 |
| `result\` | 촬영 결과물. 배포물에 들어갈 이유가 없다 |

> ⚠️ **왜 블랙리스트(`\*` + `Excludes`)에서 화이트리스트로 바꿨나**: publish 출력 폴더는 **재사용된다**(`publish.ps1`이 `licenses`만 지우고 나머지는 남긴다). 블랙리스트 방식에서는 그 폴더에 쌓인 실행 흔적(`MCPhoto.ini`·`result\`)과 개발용 파일이 **기본으로 포함**됐다. 실제로 `[Test] TestMode=1`이 남은 ini가 출력 폴더에 존재하는 것이 관측됐다(2026-08-12). 화이트리스트면 의도한 것만 들어가고, 새 파일은 `[Files]`에 한 줄을 추가해야 비로소 포함된다.

> ⚠️ **오프라인 첫 실행**: `Frame\`을 담지 않으므로 네트워크가 없는 키오스크의 최초 실행에서는 기본 프레임이 폴백 렌더러 산출물뿐이다. 서버에 한 번 닿으면 캐시된다.

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

## 6.5 Hosting 멀티사이트 (2026-07-30 — 웹 클라이언트 도입)

웹 클라이언트(`webclient/`, [docs/web-client](../web-client/README.md))가 추가되면서 Firebase Hosting이 **두 사이트**가 됐다. `web/firebase.json`의 `hosting`은 이제 **배열**이다.

| 타깃 | public | 내용 | 배포 명령 |
|------|--------|------|-----------|
| `default` | `web/public/` | P1 다운로드 페이지(현행) | `web/deploy-web.bat` (내부적으로 `--only hosting:default`) |
| `kiosk` | `web/kiosk/` | 웹 클라이언트 앱(빌드 산출물) | `webclient/deploy.bat` (내부적으로 `--only hosting:kiosk`) |

- `web/kiosk/`는 `webclient`의 `npm run build` 산출물이며 **`.gitignore` 대상**이다(커밋하지 않는다).
- **`deploy-web.bat`의 배포 대상을 `hosting:default`로 고정했다.** 그대로 `--only hosting`을 쓰면 두 사이트를 동시에 배포하는데, `web/kiosk/`가 없는 환경(클론 직후·CI)에서는 실패하거나 낡은 빌드를 공개한다.
- `.firebaserc`에 **두 타깃이 모두** 등록돼 있어야 한다(`firebase target:apply hosting default mcphoto-955fb` + `… kiosk mcphoto-955fb-kiosk`). 하나만 등록하면 나머지 타깃의 배포가 타깃 미해결로 거부된다.
- CSP·캐시 헤더는 사이트별로 독립이다. kiosk 사이트는 카메라·Worker·서명 URL PUT이 필요해 P1보다 넓은 CSP를 쓴다([web-client/01 §5.3](../web-client/01-tech-stack-and-structure.md)).

---

## 7. 상호 참조

- ffmpeg 를 쓰는 타임랩스·캡처 파이프라인: [10 · Exe 앱 아키텍처](./10-exe-app-architecture.md) §4.5·§4.8, 기능 관점은 [11](./11-exe-app-features.md).
- 백엔드 게이트 키·인증 계약: [30 · 백엔드 API 연동](./30-backend-firebase-integration.md) §2.1·§3.
- 앱 설정 파일(`MCPhoto.ini`·`branding.ini`)과 버전 표기: [12 · 설정/구성](./12-exe-app-settings-and-config.md).
- 웹·Functions 배포는 WPF 빌드와 별개 파이프라인(`web/deploy-web.bat`): [20 · 프론트엔드](./20-frontend-web-download-page.md) §9.
- 웹 클라이언트(키오스크 앱) 빌드·배포와 Hosting 멀티사이트: §6.5 + [web-client/01 §5](../web-client/01-tech-stack-and-structure.md).
- **공유 테스트 벡터** `docs/spec-vectors/*.json`: Windows(`tests/MCPhoto.Tests/SpecVectorTests.cs`)와 웹(`webclient/tests/unit/domain/vectors.test.ts`)이 **같은 파일**을 읽어 순수 로직 동일성을 고정한다. 규격을 바꿀 때는 벡터를 먼저 고쳐 양쪽을 동시에 실패시킨다([web-client/10 §3](../web-client/10-testing-and-acceptance.md)).
