---
name: it16-window-geometry-contract
description: it16 창 기하 계약 — _appliedMode가 모드 변경 판정의 유일 기준, 저장 시 캡처는 s.DisplayMode 갱신보다 먼저
metadata:
  type: project
---

설정 "저장" 시 창모드 창이 옛 위치·크기로 점프하던 버그를 it16에서 고쳤다. 두 계약을 깨지 말 것.

**계약 1 — `MainWindow._appliedMode`가 "모드가 실제로 바뀌었는지"의 유일한 기준이다.**
`DisplayApplyPolicy.Decide(target, appliedMode)`(순수, `MCPhoto.Core.Settings`)가 `None`/`Fullscreen`/
`WindowedRestoreGeometry`를 반환하고, `None`이면 창 스타일·상태·기하를 **전부 건드리지 않는다**.
`appliedMode == null`이 "아직 한 번도 적용 안 함(=시작)"이라는 유일한 신호다.

**Why:** `ApplyDisplaySettings()`는 ① 시작 복원과 ② 런타임 모드 적용을 겸하는데 호출자를 구분할 수 없었다.
런타임 적용에서도 `AppSettings.WindowBounds`(창을 **닫을 때만** 갱신되던 값)로 기하를 재적용해
세션 중 옮긴 창이 과거 좌표로 점프했다. 부수 효과로 `WindowState=Normal` 강제도 사라져
**최대화 상태로 저장해도 창이 원복되지 않는다**.

**계약 2 — `SettingsViewModel.SaveSettings()`의 첫 줄이 `_shell.RequestCaptureWindowBounds()`다.**
순서: 캡처 → VM 필드를 `s`에 복사(`s.DisplayMode` 갱신 포함) → `Save()` → `LoadSettings()` →
`RequestApplyDisplayMode()`.

**Why:** 캡처가 `s.DisplayMode` 갱신보다 뒤로 가면, 창모드→전체화면 저장 시 `_appliedMode`(=Windowed)와
새 설정이 어긋난 채 캡처되어 **직전 창 위치를 잃는다**. `SettingsViewModelTests`의 순서 계약 테스트가 이걸 고정한다.

**How to apply:**
- `CaptureWindowBounds`의 판정 기준은 설정값(`s.DisplayMode`)이 아니라 `_appliedMode`다 — 저장 도중
  설정값이 먼저 바뀌어도 창의 실제 상태를 오판하지 않는다. `OnClosing`도 같은 메서드를 쓴다.
- 셸 이벤트(`DisplayModeApplyRequested`, `WindowBoundsCaptureRequested`) 구독은 **`OnClosing`에서
  `_shell.Dispose()` 전에** `-=` 해제한다. 핸들러는 람다가 아니라 메서드 그룹으로 둬야 해제가 가능하다.
- 전체화면 ↔ 창모드 **즉시 전환**(it9 후속)은 절대 깨뜨리지 말 것 — `Decide`가 `None`을 주는 것은
  *모드가 같을 때만*이다. 이 6조합은 `DisplayApplyPolicyTests`가 전수 고정한다.
- `MainWindow`/`CaptureWindowBounds` 본문은 `Window` 인스턴스가 필요해 단위 테스트 불가다
  ([[wpf-headless-window-test-pitfall]]). 실제 창 거동은 사용자 수동 확인 항목으로 인계한다.

관련: [[it16-permission-axes]], [[wpf-headless-window-test-pitfall]]
