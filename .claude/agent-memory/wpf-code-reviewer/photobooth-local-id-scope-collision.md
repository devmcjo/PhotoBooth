---
name: photobooth-local-id-scope-collision
description: MCPhoto 프레임 리뷰 시 `local:` id가 "본인 소유"를 뜻한다는 전제가 공용 스코프 파일에서 깨진다 — 편집/삭제 권한 판정을 항상 3파일 교차로 확인
metadata:
  type: project
---

MCPhoto에서 프레임 권한을 리뷰할 때 `Id`의 `local:` 접두를 "본인이 만든 프레임"으로 읽으면 오판한다.
`LocalFrameStore.EnumerateFrames`는 `.slots`에 `#dbid`가 없으면 스코프와 무관하게 `Id = local:{파일명}`을 부여하고,
공용 스코프(`ownerName: null`)에서는 `UserId = null`이 된다. 그 결과:

- `FrameOrigin.Classify` = `UserLocal` → `FrameEditPolicy.CanEdit`이 `IsOwnedLocal(frame, userId)`를 요구하는데
  `UserId`가 null이라 **소유자 없는 편집 불가 프레임**이 된다(power 자신도 편집 못 함).
- `FrameDeleteVisibilityConverter`는 `local:` 접두만 보고 "본인 로컬"로 판단해
  **로그인한 모든 계정에 삭제 ✕를 노출**한다(역할 무관, 로컬 파일 삭제).

이 조합을 만드는 경로: ① 번들 프레임을 `Frame\`에 `.slots`와 함께 배치, ② **it15 F1 fork 저장**
(`Id=""` + `ownerName: null` → `#dbid` 미기록). it15부터 ②가 power 편집의 상시 결과라 노출 빈도가 크게 올랐다.

**Why:** it15 클라 리뷰(2026-07-28)에서 설계가 `CanEdit` 무변경(§2.1)을 명시한 채 fork에 `Id=""`를 지정해
두 규칙이 어긋났다. 세 파일(`FrameOrigin` / `LocalFrameStore.EnumerateFrames` / `FrameDeleteVisibilityConverter`)을
따로 읽으면 각각 정합해 보여서 교차 확인 없이는 드러나지 않는다.

**How to apply:** 프레임 저장/권한 변경 리뷰 시 "이 저장이 만드는 파일을 `LoadPublic`/`LoadUser`가 어떤
`Id`·`UserId`로 되돌리는가"를 먼저 계산하고, 그 값으로 `CanEdit`·`IsDeletable`·`FrameDeleteVis`를 각각 대입해 본다.
권한 축을 `UserId`로 바꾸는 것은 정책 변경이므로 리뷰어가 직접 수정하지 말고 설계 에스컬레이션 대상으로 보고한다.
관련: [[photobooth-settings-roundtrip-convention]]
