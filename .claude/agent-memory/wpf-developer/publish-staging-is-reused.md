---
name: publish-staging-is-reused
description: publish/MCPhoto는 릴리스마다 재사용되고 dotnet publish는 사라진 파일을 지우지 않는다 — 배포 파일을 개명하면 구·신 두 버전이 함께 실린다
metadata:
  type: project
---

`publish.ps1`은 `publish\MCPhoto`로 publish하며 **그 폴더를 비우지 않는다**(gitignore 대상이라 커밋에도 안 잡힌다). `dotnet publish`는 더 이상 생성되지 않는 파일을 삭제하지 않으므로, **배포 산출물 파일을 개명하면 구 파일이 그 자리에 그대로 남아 인스톨러에 함께 실린다**. 인스톨러는 `{#PublishDir}\*`를 `recursesubdirs`로 담고 `Excludes`는 서비스 계정 키 패턴뿐이다.

2026-08-11 it24에서 실측: `README.txt`→`NOTICE.txt` 개명 후 publish 폴더에 **양쪽이 공존**했고, 2026-08-06에 손으로 만든 `licenses.zip`(구 고지 4파일)도 남아 있었다. 법적 고지에서는 이것이 "서로 다른 말을 하는 문서 2종 배포"가 된다. `publish.ps1`에 `licenses` 폴더 선삭제를 넣어 해결했다(licenses 밖은 미해결).

⚠️ 그 폴더에는 지금도 `MCPhoto.ini`·`branding.ini`·`result/`·`MCPhoto.zip`·`MCPhoto (2).zip`·`tools.zip` 같은 잔재가 있고 **전부 인스톨러에 실린다**(`result/`는 남의 사진일 수 있다).

**Why:** 개명·삭제는 소스에서만 보면 완결로 보이지만 배포 산출물에서는 누적이다. 테스트는 `AppContext.BaseDirectory` 기준이라 `bin\Debug`만 깨끗하면 통과하고, `bin\Release`·`publish\`의 잔재는 아무도 보지 않는다.

**How to apply:** 배포되는 파일(고지·프레임·설정 샘플)을 개명·삭제했으면 소스만 고치고 끝내지 말고 ① `find . -name "<옛이름>"`으로 `bin\Debug`·`bin\Release`·`publish\`의 잔재를 확인 ② 지우고 ③ 재발 방지를 publish 스크립트에 넣는다. [[imwrite-atomic-replace-extension]]처럼 "빌드 출력에서만 드러나는" 계열이다.
