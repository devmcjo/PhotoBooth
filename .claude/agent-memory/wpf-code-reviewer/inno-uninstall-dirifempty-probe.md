---
name: inno-uninstall-dirifempty-probe
description: Inno Setup [UninstallDelete] 의 dirifempty 는 설치 파일 삭제 후에 평가된다(실측). 손님 사진 보존 판정을 추측 대신 probe 인스톨러로 direct 근거화하는 레시피
metadata:
  type: reference
---

`installer/MCPhoto.iss`의 "제거가 `result\`(손님 사진)를 지우지 않는가" 판정은 **추측하지 말고 실측한다.**
Inno Setup 7로 실측한 결과(2026-08-12):

- `Type: dirifempty; Name: "{app}"` 는 **인스톨러가 설치한 파일이 지워진 뒤에 평가된다** → 남은 것이 없으면 `{app}` 자체가 사라진다.
  (uninstall 로그가 역순 처리라 dirifempty 가 무동작일 것이라는 흔한 추측은 **틀렸다.**)
- `{app}\result\...` 가 있으면 `{app}` 은 비어 있지 않아 **보존**되고, 같은 절의
  `Type: files`(ini) · `Type: filesandordirs`(Frame 캐시) 지시행은 정상 삭제된다.
- ⇒ "캐시는 지우고 자산은 남긴다" 원칙이 `.iss` 정적 구조만으로 성립한다.

**probe 레시피**(1~2분, 시스템 오염 없음):
`PrivilegesRequired=lowest` + `DefaultDirName={scratchpad}\probeapp` + `Compression=none` 인 최소 `.iss` 를
`ISCC.exe`로 컴파일 → `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` 로 설치 → 앱이 실행 중에 만드는 것들
(ini·Cache\·result\)을 손으로 만들어 두고 → `{app}\unins000.exe /VERYSILENT` 로 제거 → 무엇이 남았는지 관찰.
`result\` 있는 판/없는 판 **두 번** 돌려야 보존과 정리를 동시에 증명한다.
HKCU 언인스톨 키는 제거가 스스로 지운다(레지스트리 잔재 0).

⚠️ ISCC 경로는 버전 폴더를 하드코딩하지 말 것 — 이 머신은 `C:\Program Files\Inno Setup 7`(x86 트리 아님)이다.
`package.ps1` 이 폴더명 숫자로 최신을 고르는 이유가 이것이다(ISCC 7.0.2는 FileVersion 0.0.0.0을 보고한다).

관련: [[photobooth-settings-roundtrip-convention]]
