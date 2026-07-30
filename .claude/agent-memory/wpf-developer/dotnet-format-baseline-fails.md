---
name: dotnet-format-baseline-fails
description: MCPhoto는 dotnet format --verify-no-changes가 기준선부터 실패한다 — build-verify Step 4에서 게이트로 쓰지 말 것
metadata:
  type: feedback
---

`dotnet format MCPhoto.sln --verify-no-changes`는 **HEAD 상태에서도 실패**한다. 이걸 build-verify의
합격 조건으로 삼지 말고, 통과시키려고 파일을 재포맷하지도 말 것.

**Why:** 이 저장소의 관례는 `new() { Id = "x", UserId = "y" }` 같은 **객체 초기화자 한 줄 표기**인데
`dotnet format`은 이를 WHITESPACE 오류로 지적한다. it16 실측: 9개 파일이 지적되고 그중 6개는
당시 변경과 무관한 파일(`AssemblyInfo.cs`·`HttpFrameRepository.cs`·`LoginGuestViewModelTests.cs` 등)이었다.
지적 라인을 `git show HEAD:<file>`과 대조하면 동일 패턴이 이전부터 있다. 즉 신규 위반이 아니다.
재포맷하면 손대지 않은 파일까지 diff가 번져 리뷰·디버깅이 어려워진다(범위 밖 수정).

**How to apply:**
- 진짜 게이트는 `dotnet build -c Release --no-incremental` **경고 0 / 오류 0**과 `dotnet test` 전량 통과다.
  이 둘로 nullable·분석기 경고가 이미 걸러진다.
- build-verify Step 4에서 `dotnet format`을 돌렸다면 결과를 **WARN(기존 저장소 스타일)** 으로 보고한다.
- 신규 파일은 주변 파일의 표기를 따른다 — `dotnet format`이 원하는 형태로 맞추면 오히려 관례에서 벗어난다.
- ⚠️ 따라서 **"신규 위반 0건"을 목표로 삼지 말 것.** 관례(한 줄 초기화자)를 따른 신규 코드는 위반 수를
  **정상적으로 증가시킨다**. it17 실측: `FrameSelectViewModelTests.cs`가 10건 → 12건(신규 헬퍼의 한 줄
  초기화자 1개). 근거 제시 방법은 `git stash push -- <file>`로 HEAD 상태 카운트를 재고 **증가분이
  주변 관례와 동일한 패턴에서만 나왔음**을 지적 라인 번호로 보이는 것이다.

관련: [[encoding-verify-method]]
