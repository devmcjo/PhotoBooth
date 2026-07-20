; MC포토 인스톨러 (Inno Setup) — WBS Step 12
; 앱 + 기본 프레임(Frame/) + ffmpeg 번들. 데이터 폴더는 %ProgramData%\MCPhoto (쓰기 가능).
; ⚠️ 서비스 계정 키(*serviceaccount*.json 등)는 절대 포함하지 않는다 (architecture §6.4).
;
; 빌드 전제: dotnet publish -c Release 산출물을 SourceDir로 지정.
;   dotnet publish src/MCPhoto.App -c Release -r win-x64 --self-contained false -o publish
; 그 후: iscc installer\MCPhoto.iss

#define AppName "MC포토"
#define AppVersion "1.0.0"
#define AppPublisher "MCPhoto"
#define AppExeName "MCPhoto.exe"
; 게시 산출물 경로(상대). 필요 시 iscc /DPublishDir=... 로 override.
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\MCPhoto
DefaultGroupName=MC포토
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
; 64비트 앱
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=MCPhoto-Setup-{#AppVersion}
DisableProgramGroupPage=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; 앱 바이너리 + Frame/ + tools/ffmpeg (publish 산출물 전체)
; ⚠️ 서비스 계정 키는 Excludes로 이중 차단(빌드 산출물에 없어야 하지만 방어적)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; \
  Excludes: "*serviceaccount*.json,*service-account*.json,*firebase*credentials*.json,*firebase-adminsdk*.json,serviceAccountKey.json,*.pem,*.key"

[Dirs]
; 쓰기 가능한 데이터 폴더(설정·기본 프레임 캐시·로그). Program Files 회피.
Name: "{commonappdata}\MCPhoto"; Permissions: users-modify
Name: "{commonappdata}\MCPhoto\logs"; Permissions: users-modify
Name: "{commonappdata}\MCPhoto\cache"; Permissions: users-modify

[Icons]
Name: "{group}\MC포토"; Filename: "{app}\{#AppExeName}"
Name: "{group}\MC포토 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MC포토"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 생성"; GroupDescription: "추가 아이콘:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "MC포토 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 캐시는 정리, 사용자 설정/결과물은 보존
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\cache"
Type: filesandordirs; Name: "{commonappdata}\MCPhoto\logs"
