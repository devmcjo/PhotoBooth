---
name: it20-frame-load-phase-invariant
description: FrameLoadPolicy.Finalize는 어떤 입력에서도 Loading을 반환하지 않아야 한다 — 반환하면 대기 오버레이가 영구 고착
metadata:
  type: project
---

`FrameLoadPolicy.Finalize`(it20, `src/MCPhoto.Core/Frames/FrameLoadPolicy.cs`)의 불변식:
**반환값에 `FrameLoadPhase.Loading`이 나오는 입력 조합이 하나도 없어야 한다.** `FrameLoadPolicyTests.Finalize_Never_Returns_Loading`이
32조합으로 이를 고정한다. quiet 갈래에서 `current`를 그대로 돌려주는 형태(`current == Failed ? Ready : current`)는
`current == Loading`에서 불변식을 깨므로 `current is Failed or Loading ? Ready : current`여야 한다.

**Why:** `FrameSelectViewModel.ReloadFramesAsync`의 `finally`가 이 함수로 국면을 닫는 것이 대기 오버레이의
유일한 해제 경로다. `Loading`이 반환되면 `FrameSelectView`의 전면 scrim 오버레이가 3행 전체를 덮은 채 남고,
상단 바 [홈]만 남는 사실상 기능 정지가 된다. `AppShellViewModel`이 `OnEnterAsync` 예외를 조용히 삼키므로
로그에도 원인이 남지 않는다.

**How to apply:** `Finalize`/`Classify`에 갈래를 추가하거나 `ReloadFramesAsync`의 `finally`를 손댈 때.
`Phase` 확정을 happy-path 말미로 되돌리지 않는다 — 그것이 이 설계가 리뷰에서 고친 원래 결함(C1)이다.
[[imwrite-atomic-replace-extension]]도 같은 이터레이션에서 나온 설계 코드 조각 결함이다.
