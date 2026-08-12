; MCPhoto 인스톨러 (Inno Setup 6)
; 배포물 = MCPhoto.exe(자체 포함 단일 파일) + licenses\ + tools\ffmpeg. 이 셋뿐이다(§[Files] 참조).
; 데이터 폴더는 %ProgramData%\MCPhoto (쓰기 가능). 설정 파일은 앱이 최초 실행 시 스스로 만든다.
;
; 빌드 전제: publish.ps1 이 만든 자체 포함 단일 exe 산출물.
;   powershell -ExecutionPolicy Bypass -File publish.ps1      → publish\MCPhoto\
;   그 후: iscc installer\MCPhoto.iss
; publish.ps1 -Installer 를 쓰면 위 두 단계가 한 번에 실행된다.

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

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 생성"; GroupDescription: "추가 아이콘:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 캐시는 정리, 사용자 설정/결과물은 보존
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\cache"
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\logs"
