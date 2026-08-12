---
name: deprecation-comment-vs-source-scan
description: 이 리포의 폐기 표기 주석은 사라진 심볼 이름을 일부러 남긴다 → 부재를 단정하는 소스/csproj 스캔 테스트는 주석 줄을 걷어낸 뒤 판정해야 자기 주석에 걸리지 않는다
metadata:
  type: project
---

이 리포의 폐기 관례는 **사라진 심볼의 이름을 주석에 그대로 남긴다**(`FrameCatalogService.LoadBundleFrames 폐기`처럼).
따라서 "그 식별자가 코드에 없다"를 단정하는 정적 스캔 테스트는 **주석 줄을 제거한 뒤** 판정해야 한다.
`.cs`는 `///`·`//`·`*` 로 시작하는 줄을 걸러내고, `.csproj`/XAML은 `<!-- ... -->`를 정규식(`Singleline`)으로 지운다.
같은 이유로 설계 문서가 "`grep <심볼>` 결과 0줄"을 완료 게이트로 적었더라도 **동결 폐기 주석은 그 게이트에 걸린다** —
그때 옳은 처분은 주석을 고치는 것이 아니라, "살아 있는 코드 참조 0"으로 게이트를 해석하고 잔여 히트를 근거와 함께 보고하는 것이다.

**Why:** it27에서 설계가 두 요구를 동시에 걸었다 — ① `FrameOrigin.cs`에 사라진 심볼을 명시하는 동결 주석을 넣어라
② `grep BundleFolder|LoadBundleFrames` 결과가 0줄이어야 한다. 주석을 무시하는 스캔을 쓰지 않으면 신규 회귀 테스트가
자기 리포의 폐기 주석 때문에 실패하고, 반대로 게이트에 맞추려 주석을 지우면 판정 보존의 근거가 코드에서 사라진다.

**How to apply:** 부재(absence)를 단정하는 테스트를 새로 만들 때 `CodeLinesOf(...)` 같은 주석 제거 헬퍼를 먼저 만든다
(`tests/MCPhoto.Tests/AppPathFrameRemovalTests.cs` 참고). 소스 파일을 못 찾으면 **스킵이 아니라 실패**로 처리해야
파일 이동·개명을 놓치지 않는다. 관련: [[ui-behind-state-antipattern]]
