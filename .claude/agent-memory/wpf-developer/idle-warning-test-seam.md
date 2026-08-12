---
name: idle-warning-test-seam
description: 유휴 경고 팝업을 headless로 검증하는 방법 — ShowIdleWarning/HideIdleWarning은 internal 이음새이고, 테스트는 반드시 동기여야 한다(DispatcherTimer VerifyAccess)
metadata:
  type: project
---

`AppShellViewModel.ShowIdleWarning`/`HideIdleWarning`은 **`internal`**이다(`InternalsVisibleTo("MCPhoto.Tests")`) —
it26에서 유휴 팝업 상태(링크 가시성·실패 캡션)를 창 없이 검증하려고 접근성만 넓혔다(동작 변경 없음).

**Why:** 정상 경로는 `IIdleWatchdog.IdleTimeout` → `_dispatcher.BeginInvoke(ShowIdleWarning)`인데, 테스트 스레드에는
메시지 펌프가 없어 `BeginInvoke`가 **영원히 실행되지 않는다**. 페이크 워치독으로 이벤트를 쏴도 아무 일이 안 일어나므로
가시성 검증이 불가능하다. 그래서 `ShowIdleWarning`을 직접 호출한다.

**How to apply:**
- `ShowIdleWarning`은 `DispatcherTimer`를 만들어 `Start()`한다. 펌프가 없으면 Tick이 오지 않아 무해하지만,
  `Start()`는 **디스패처 스레드에서 호출돼야 한다**(`VerifyAccess`). 셸을 만든 스레드와 같은 스레드여야 하므로
  **테스트를 `async`로 만들지 마라** — `await` 뒤 컨티뉴에이션이 다른 풀 스레드로 옮겨가면 `InvalidOperationException`이다.
  it26의 `AppShellIdleFolderLinkTests`는 전부 동기 `[Fact]`이고, 그래서 실시간 대기가 0이다.
- 검증 후 `shell.Dispose()`를 부른다(`HideIdleWarning` + 토스트 타이머 정리 — 타이머 누수 없이 끝난다).
- 카운트다운 무간섭 같은 "값이 변하지 않는다" 단정은 `IdleCountdownRemaining`을 커맨드 실행 전후로 비교하면 된다
  (`IdleCountdown` 순수 클래스는 별도 테스트가 이미 있다).

관련: [[wpf-headless-window-test-pitfall]] · [[composition-root-testable]]
