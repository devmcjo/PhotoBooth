---
name: iscc-compile-verify
description: 인스톨러 정적 검증 실측 레시피 — ISCC는 Inno Setup 7 경로에만 있고 PATH에 없다. /O로 임시 출력에 컴파일하면 리포를 오염시키지 않는다
metadata:
  type: reference
---

`installer/MCPhoto.iss`를 실제로 컴파일해 검증하는 방법(2026-08-12 실측, it26):

- 실행 파일: **`C:\Program Files\Inno Setup 7\ISCC.exe`**. `where iscc`는 **실패한다**(PATH 미등록)이고
  `Inno Setup 6` 폴더는 이 머신에 **없다**(문서·주석의 "6·7 호환" 서술은 호환성 이야기일 뿐 설치 버전이 아니다).
- 컴파일에는 `publish\MCPhoto\MCPhoto.exe`가 **선행되어야 한다**(`.iss`가 exe 버전 리소스에서 `AppVersion`을 읽는다).
  publish 스테이징은 재사용되므로 **낡은 버전으로 컴파일될 수 있다**(실측: `Directory.Build.props` 1.2.1인데
  산출물은 `MCPhoto-Setup-1.1.19.exe`) — 버전을 근거로 쓸 일이 있으면 publish를 먼저 다시 돌린다([[publish-staging-is-reused]]).
- **출력을 리포에 남기지 마라**: 기본 출력은 `installer\Output\`(gitignore 대상이지만 160MB짜리 exe가 쌓인다).
  `ISCC.exe /O"<scratchpad 경로>" installer\MCPhoto.iss`로 임시 폴더에 뽑는다.
- PowerShell에서 `& 'C:\Program Files\Inno Setup 7\ISCC.exe' /O"..." '.\installer\MCPhoto.iss'` 형태로 호출한다
  (Bash 툴에서는 `/c/Program Files/...` 경로가 해석되지 않았다 — Git Bash 마운트에 없다).
- 컴파일 성공은 **`[Files]` 화이트리스트가 실제로 무엇을 담는지**도 보여 준다(Compressing 로그 = exe + licenses 5개 + ffmpeg).
  `[Dirs]`·`[UninstallDelete]`의 **부재 단정**(손님 사진 삭제 행이 없음)은 컴파일로 증명되지 않으므로
  `tests/MCPhoto.Tests/InstallerScriptTests.cs`가 지시 줄만 파싱해 잠근다(주석 줄에 같은 경로 문자열이 있어 문자열 검색은 오탐한다).
