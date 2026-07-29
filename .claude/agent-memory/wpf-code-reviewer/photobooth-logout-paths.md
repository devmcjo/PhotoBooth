---
name: photobooth-logout-paths
description: MCPhoto의 실제 로그아웃 경로는 AppShellViewModel.Logout() 하나뿐 — Reset(clearUser:true)는 프로덕션 호출부가 0이다(it8 A1이 유휴 로그아웃을 금지)
metadata:
  type: project
---

MCPhoto에서 “로그아웃 경로”를 세는 리뷰를 할 때, `SessionContext.Reset(clearUser: true)`를 실경로로 세면 오판한다.

- `SessionContext.CurrentUser`는 `private set`이고 대입 지점은 `Login()`/`Logout()` **2곳뿐**,
  `Reset(clearUser)`는 `clearUser`일 때 `Logout()`에 위임한다 → `CurrentUserChanged` 구독 1곳이 전부를 덮는다.
- 그런데 **`clearUser: true`를 넘기는 프로덕션 호출부는 0개**다. `ReturnHome(reason, clearUser = false)`의
  전 호출부가 기본값 또는 명시 `false`다:
  - `AppShellViewModel.OnIdleCountdownTick` → `clearUser: false` + 주석 “로그아웃 절대 금지(it8 A1)”
  - `DoneViewModel`(세션 완료·완료 확인) → `clearUser: false` + “촬영 후 로그인 유지(it5 §4 B8)”
  - `App.TryReturnHome`(전역 예외 복구), 각 화면 Cancel → 전부 기본 false
- 따라서 현재 유일한 로그아웃은 계정 팝오버의 `AppShellViewModel.Logout()`이다.
  (상단 바는 `IsTopBarVisible`이 `Capture`·`Qr`에서 false라 **업로드 진행 중에는 로그아웃 버튼 자체가 없다**
  → “업로드 중 로그아웃으로 Bearer가 사라져 TempUser 과금이 누락된다”는 시나리오는 UI상 도달 불가.)

**Why:** 2026-07-29 로그아웃 JWT 리뷰에서 신규 코드 주석·테스트·개발자 메모리가 일제히 “유휴 타임아웃·세션 완료가
`Reset(clearUser:true)`를 탄다”고 서술했으나 전수 grep 결과 실호출이 0이었다. 구독 방식 자체는 여전히 옳지만
(경로가 늘어도 한 지점이 덮는다), 이 서술을 사실로 받아들이면 유휴/완료 시나리오를 헛검증하게 된다.

**How to apply:** 계정 수명·토큰 폐기 관련 리뷰에서 `grep -rn "clearUser: *true" src`를 먼저 돌려
그 시점의 실호출 여부를 확인하고, 0이면 “도달 불가 경로”로 명시한다. 관련: [[di-wiring-revert-experiment]]
