; MCPhoto 인스톨러 (Inno Setup 6)
; 배포물 = MCPhoto.exe(자체 포함 단일 파일) + licenses\ + tools\ffmpeg. 이 셋뿐이다(§[Files] 참조).
; 데이터 폴더는 %ProgramData%\MCPhoto (쓰기 가능). 설정 파일은 앱이 최초 실행 시 스스로 만든다.
; it26: 앱이 쓰는 것은 전부 이 데이터 폴더 아래다 — 설정(MCPhoto.ini) · 로그 · 캐시 · 세션 임시물 +
;   **촬영 결과물(result\)** 과 **프레임 캐시(Frame\)**. 설치 폴더는 읽기 전용 배포물이며, 앱은
;   {app}\Frame(운영자가 배치하는 번들) · {app}\branding.ini 를 읽기만 한다.
;   ⚠️ 승격 실행(관리자 권한)으로 띄우면 ini만은 {app}\MCPhoto.ini 에 생길 수 있다(경로 정책 1순위가 실행경로).
;
; 빌드: **package.bat**(또는 package.ps1) 하나로 publish → 인스톨러까지 실행된다.
;   publish.bat 은 테스트용(publish만)이고 인스톨러를 만들지 않는다 — 테스트 산출물이
;   배포물처럼 보이는 사고를 막기 위해 일부러 분리했다.
; 수동으로 컴파일할 수도 있다: iscc installer\MCPhoto.iss (publish 가 선행돼야 한다)
; Inno Setup 6·7 모두 호환된다(7이 현행). package.ps1 은 설치된 버전 중 가장 높은 것을 자동 선택한다.

; 표시 이름은 여기 한 곳에서만 정의한다({#AppName}로 참조 — 종전엔 [Icons]·[Run]에 하드코딩돼 있었다).
#define AppName "MCPhoto"
#define AppPublisher "MCPhoto"
#define AppExeName "MCPhoto.exe"

; 게시 산출물 경로(상대). publish.ps1 의 출력 위치와 일치해야 한다.
; ⚠️ 종전 값은 "..\publish" 였는데 publish.ps1 은 "publish\MCPhoto" 에 쓴다 —
;    그대로 두면 recursesubdirs 가 MCPhoto 폴더째 담아 {app}\MCPhoto\MCPhoto.exe 로 설치됐다.
#ifndef PublishDir
  #define PublishDir "..\publish\MCPhoto"
#endif

; ── 버전: 패키징할 exe의 버전 리소스에서 읽는다 ──
; 하드코딩(종전 "1.0.0")을 폐기한 이유: 앱은 Directory.Build.props 의 <Version> 을 유일 원천으로
; 삼는데(it18) 인스톨러가 따로 값을 들고 있어 1.1.19 앱이 1.0.0 로 설치·등록됐다.
; exe에서 직접 읽으면 **인스톨러가 자기가 담는 바이너리와 다른 버전을 말할 수 없다** —
; 값을 전달하는 방식(/D)보다 강한 보장이다. 필요 시 iscc /DAppVersion=x.y.z 로 덮어쓸 수 있다.
; exe가 없으면 컴파일이 실패한다 — publish 를 건너뛴 채 인스톨러를 만드는 사고를 막는 의도된 실패다.
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(AddBackslash(SourcePath) + PublishDir + "\" + AppExeName, "ProductVersion")
#endif

[Setup]
; ⚠️ AppId 는 이 제품의 영구 신원이다. 한번 배포하면 절대 바꾸지 않는다 —
;    바꾸면 기존 설치가 업그레이드로 인식되지 않고 나란히 설치된다.
;    (종전에는 AppId 가 없어 AppName 이 신원 역할을 했다. 출하 전에 고정해 둔다.)
AppId={{9303675E-B66D-4D2E-A722-169F9E8865BC}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\MCPhoto
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
; 64비트 앱
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=MCPhoto-Setup-{#AppVersion}
DisableProgramGroupPage=yes
; 설치 파일 자신의 버전 리소스(파일 속성에서 확인 가능). 앱 버전과 같은 값을 쓴다.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; ── 담는 것을 명시 열거한다(종전엔 publish 폴더 전체 `\*` + Excludes 였다) ──
; 왜 바꿨나: 블랙리스트 방식은 **새로 생긴 파일이 기본으로 포함**된다. publish 폴더는 재사용되므로
; 실행 흔적(MCPhoto.ini · result\ 촬영 결과물)과 개발용 파일(branding.ini.sample)이 쌓이는데,
; 그것들이 조용히 배포물에 실렸다. 화이트리스트면 **의도한 것만** 들어가고, 새 파일은 여기에
; 한 줄을 추가해야 비로소 포함된다.
; 배포 대상 3개(사용자 확정): exe · licenses\ · tools\
;   - MCPhoto.ini      : 앱이 최초 실행 시 생성한다 → 담지 않는다(테스트 섹션 잔재 유출도 함께 막힌다)
;   - branding.ini     : 담지 않는다(운영자가 필요할 때 배치)
;   - Frame\           : 담지 않는다 → 기본 프레임은 서버에서 내려받는다
;   - result\          : 촬영 결과물. 배포물에 들어갈 이유가 없다
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\*"; DestDir: "{app}\licenses"; \
  Flags: recursesubdirs createallsubdirs ignoreversion
; ffmpeg(GPLv3). licenses\ 의 고지와 **함께** 배포해야 한다 — 한쪽만 담으면 고지 의무를 어긴다.
Source: "{#PublishDir}\tools\*"; DestDir: "{app}\tools"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; \
  Excludes: "*serviceaccount*.json,*service-account*.json,*firebase*credentials*.json,*firebase-adminsdk*.json,serviceAccountKey.json,*.pem,*.key"

[Dirs]
; 쓰기 가능한 데이터 폴더(설정·기본 프레임 캐시·로그). Program Files 회피.
Name: "{commonappdata}\MCPhoto"; Permissions: users-modify
Name: "{commonappdata}\MCPhoto\logs"; Permissions: users-modify
Name: "{commonappdata}\MCPhoto\cache"; Permissions: users-modify
; it26: 결과물·프레임 캐시를 여기로 이관했으므로 두 폴더를 **명시 생성**한다.
;   상속에 의존하지 않는 이유: 비승격 첫 실행이 폴더 생성부터 실패하면 손님 사진이 조용히 저장되지 않는다
;   (LocalSaveService 는 예외 대신 null 을 반환한다 — 화면에 아무 안내도 나오지 않는다).
Name: "{commonappdata}\MCPhoto\result"; Permissions: users-modify
Name: "{commonappdata}\MCPhoto\Frame"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 생성"; GroupDescription: "추가 아이콘:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; ⚠️ 언인스톨러는 **자기가 설치한 파일만** 추적한다. 앱이 실행 중에 만든 것은 목록에 없어
;    그대로 남는다 — 실측(2026-08-12)에서 제거 후 `{app}` 에 아래가 남았다:
;      MCPhoto.ini  … 설정 파일(앱이 최초 실행 시 생성. 승격 실행이면 Program Files 에 쓴다)
;      Frame\       … 서버에서 내려받은 프레임(FrameCatalogService.BundleFolder = {exe}\Frame)
;    사용자 요구가 "Program Files·로그·레지스트리 모두 제거"이므로 명시 열거한다.
Type: files; Name: "{app}\MCPhoto.ini"
Type: files; Name: "{app}\branding.ini"
; 구 프레임 캐시(재취득 가능 — 서버에서 다시 내려받는다). it26 이후 앱은 여기에 쓰지 않고 읽기만 한다.
Type: filesandordirs; Name: "{app}\Frame"
; ⛔ `{app}\result` 는 **절대 지우지 않는다.** it26 이전 버전의 로컬 저장 기본 경로가 `{exe}\result` 였으므로
;    (LocalSavePath 가 비면 AppContext.BaseDirectory\result) 그 폴더에는 **손님 사진과 타임랩스**가 들어 있다.
;    이관 후에는 "구 버전이 남긴 손님 자산"이라 더 중요하다 — 앱도 그 폴더를 옮기거나 지우지 않는다
;    (시작 시 위치를 Warning 로그로 알려 주는 것이 전부다). 제거가 고객 자산을 지우는 것은 복구 불가한 사고다.
;    → result 가 있으면 아래 dirifempty 가 걸러 설치 폴더도 함께 보존된다(의도된 동작).
; 위를 지운 뒤 빈 설치 폴더까지 정리한다(dirifempty 는 비어 있을 때만 지우므로,
; 촬영 결과물이나 운영자가 따로 둔 파일이 있으면 보존된다 — 남의 파일을 지우지 않는다).
Type: dirifempty; Name: "{app}"

; 데이터 폴더: 로그·캐시·세션 임시물은 정리한다.
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\cache"
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\logs"
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\sessions"
Type: files; Name: "{commonappdata}\MCPhoto\MCPhoto.ini"
; it26: 프레임 캐시(신규 위치). **재취득 가능**하므로 캐시 원칙대로 지운다 — 서버에서 다시 내려받는다.
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\Frame"
; ⛔ `{commonappdata}\MCPhoto\result` 는 **절대 이 목록에 추가하지 않는다.** it26 이후 손님 사진의
;    기본 저장 위치이며, 로컬 사본은 QR 전송과 독립이라 서버에 없을 수도 있다(유일 사본일 수 있다).
;    "캐시는 지우고 자산은 남긴다"가 이 절의 원칙이다.
; ⚠️ `{commonappdata}\MCPhoto` 자체는 dirifempty 로만 지운다 — 촬영 결과물(result\)이
;    여기 있을 수 있고, 그것은 사용자 자산이라 제거가 임의로 지워서는 안 된다.
Type: dirifempty; Name: "{commonappdata}\MCPhoto"
