# 02 · 앱 셸과 내비게이션

| 항목 | 값 |
|------|-----|
| 문서 | 화면 상태머신·상단바·세션 홀더·유휴 감시·전체화면·가시성·전역 예외의 웹 구현 규격 |
| 규격 진실원 | `docs/analysis/13 §1·§2·§3·§7·§11`(동작) · `docs/analysis/61 §6`(JWT) |
| Windows 참조 | `src/MCPhoto.App/AppShellViewModel.cs`, `src/MCPhoto.Core/Navigation/{SessionStateMachine,IdleCountdown}.cs`, `src/MCPhoto.App/MainWindow.xaml` |
| 갱신 규칙 | 화면 추가·전이 규칙 변경 시 §2와 `docs/analysis/13 §1·§2`를 동시 갱신 |

---

## 1. 셸의 책임

Windows `AppShellViewModel`에 대응한다. **화면 하나가 아니라 앱 전체의 골격**이다.

| 책임 | 구현 위치 | 규격 |
|------|-----------|------|
| 화면 상태 보유·전이 판정 | `shell/shellStore.ts` + `domain/navigation/stateMachine.ts` | `analysis/13 §2` |
| 오버레이 복귀 지점 보존 | `shellStore` | `analysis/13 §2.3` |
| 홈 복귀(촬영 데이터 폐기) | `shellStore.returnHome()` | `analysis/13 §2.4` |
| 세션 사용자 단일 소스 + 변경 통지 | `shell/sessionStore.ts` | `analysis/13 §3.1` |
| JWT 홀더(메모리 전용) + 통지 구독 폐기 | `shell/authStore.ts` | M1·M2 |
| 유휴 감시 | `shell/idleWatchdog.ts` | `analysis/13 §7` |
| 전체화면 제어 | `shell/fullscreenController.ts` | WD7 |
| 가시성 변경 대응 | `adapters/platform/visibility.ts` | WM4 |
| 전역 예외 복구 | `shell/globalErrorHandler.ts` | M16 |
| 상단바·토스트·모달 스택 | `ui/` + `shellStore` | `analysis/13 §1` |
| TempUser 한도 상태 재평가 | `sessionStore` 구독 | `analysis/31 §4.4` |

---

## 2. 화면 상태와 전이

### 2.1 상태 13종

`analysis/13 §1`과 동일하다. **상단바 표시·유휴 감시 여부가 화면의 속성**이다.

| 화면 상태 | 상단바 | 유휴 감시 | 웹 추가 규칙 |
|-----------|:------:|:---------:|--------------|
| `Home` | 표시(홈 버튼만 숨김) | ✕ | 첫 제스처에서 전체화면·오디오·WakeLock 시도 |
| `Login` | 표시 | ✕ | 리디렉트로 페이지를 떠난다 → 복귀 지점을 `sessionStorage`에 저장([07 §2](./07-auth-and-permissions-web.md)) |
| `FrameSelect` | 표시 | **○** | |
| `Guide` | 표시 | **○** | [촬영 시작]에서 오디오 unlock 재확인 |
| `Capture` | **숨김** | **○** | 자체 [취소] 버튼 필수. hidden 전환 시 시퀀스 취소(WM4) |
| `CutSelect` | 표시 | **○** | |
| `Result` | 표시 | **○** | |
| `Qr` | **숨김** | **○** | 자체 [완료]/[재시도] 버튼 필수 |
| `Done` | 표시 | ✕ | 6초 후 자동 홈 |
| `FrameEditor` | 표시 | **✕**(의도) | |
| `Settings` | 표시(설정 버튼 숨김) | ✕ | |
| `Account` | 표시 | ✕ | 모드 2종(`Account` / `Admin`) |
| `UserMgmt` | 표시 | ✕ | |

> **`Capture`·`Qr`에서 상단바를 숨기는 규칙은 유지**하되 **자체 취소 버튼을 반드시 둔다.** 그러지 않으면 손님이 갇힌다(`analysis/13 §1` 주석).

### 2.2 정방향 전이표 (`analysis/13 §2.1` 그대로)

| From | 진행 가능 |
|------|-----------|
| `Home` | `FrameSelect`, `Login`, `Settings` |
| `Login` | `FrameSelect`, `FrameEditor`, `Settings` |
| `FrameSelect` | `Guide`, `FrameEditor` |
| `Guide` | `Capture` |
| `Capture` | `CutSelect` |
| `CutSelect` | `Result`, `Guide`(= 전체 재촬영) |
| `Result` | `Qr`, `Done` |
| `Qr` | `Done` |
| `Done` | `Home` |
| `Settings` | `Login`, `FrameEditor` |
| `FrameEditor` | `FrameSelect`, `Settings`, `Login` |
| `Account` | `UserMgmt` |
| `UserMgmt` | `Account` |

### 2.3 전이 판정 (순수 함수)

```ts
// domain/navigation/stateMachine.ts
export function canTransition(from: Screen, to: Screen): boolean {
  if (to === "Home" || to === "Settings" || to === "Login" || to === "Account") return true; // 오버레이성
  if (from === to) return false;
  return FORWARD[from]?.includes(to) ?? false;
}
```

| 규칙 | 내용 |
|------|------|
| 전이 실패 | **예외가 아니라 거부 + 경고 로그**(무인 안정성) |
| 전이 순서 | ① 이탈 화면 정리(`onLeave`) → ② 상태·화면 교체 → ③ 유휴 감시 갱신 → ④ 진입 화면 초기화(`onEnter`) |
| 각 단계 예외 | **삼켜 로그**하고 흐름을 계속한다 |
| `onEnter`/`onLeave` 비동기 | 진입 중 다시 전이 요청이 오면 **최신 요청만 유효**(이전 진입의 부작용을 취소 토큰으로 중단) |

### 2.4 오버레이 복귀 (`analysis/13 §2.3` — it19 개정 반영)

- `Settings`·`Login`·`Account`는 **진입 전 상태를 복귀 지점으로 보존**한다.
- **오버레이 화면 집합** = `Settings`·`Login`·`Account`·`UserMgmt`(`isOverlayScreen`). 진입 시 현재 상태가 이 집합에 **속하지 않을 때만** 복귀 지점을 저장한다.
  - 오버레이끼리의 전환이 복귀 지점을 덮어쓰면 [닫기]가 **자기 자신으로 복귀**해 무반응이 된다 — 계정 화면에서도 상단바 계정 메뉴가 열리므로(계정관리↔관리자도구 직전환) 실제로 발생한다(Windows it19 버그, 회귀 테스트 `AppShellOverlayReturnTests.cs`).
  - `UserMgmt`는 오버레이 진입 대상이 아니지만(관리자 도구의 하위 페이지) 같은 집합이다 — 복귀 지점이 되면 `Account`↔`UserMgmt`를 [닫기]로 벗어날 수 없다.
- **사용자 관리의 [뒤로]는 복귀 지점을 쓰지 않는다** — 관리자 도구(`Account`/Admin)로 **직행**하며 복귀 지점을 건드리지 않는다.
- 복귀는 **전이 검증 면제**(진입의 역방향은 항상 합법).
- **복귀 시 촬영 세션 데이터를 폐기하지 않는다.**

### 2.5 홈 복귀 (`analysis/13 §2.4`)

`returnHome(reason)`은 항상 아래 순서로 수행한다. **로그인은 유지한다**(M3).

```
1. 촬영 세션 데이터 폐기(컷·선택·프레임·재촬영 카운터·필터)
2. 세션 작업 공간 정리 — OPFS sessions/{id}/ 삭제 (실패 무시)
3. 카메라 정지 · 인코더 정지 (실패 무시)
4. 유휴 감시 정지
5. 화면 = Home
6. 로그: "홈 복귀: {reason}"
```

- `clearUser` 파라미터를 **만들지 않는다.** 로그아웃은 계정 메뉴의 `logout()` 한 곳만 수행한다(`analysis/13 §3.3`).

---

## 3. 라우팅 — 화면 상태를 URL에 싣지 않는다

| 경로 | 용도 |
|------|------|
| `/` | 앱 본체(모든 화면 상태를 이 경로에서 렌더) |
| `/oauth2callback` | Google 인가 코드 수신 전용([07 §2](./07-auth-and-permissions-web.md)) |
| 그 외 | Hosting rewrite로 `/`로 폴백 |

### 3.1 이유와 대응

| 문제 | 처리 |
|------|------|
| URL에 화면을 실으면 뒤로가기로 **촬영 중간 상태에 재진입**할 수 있다 | 화면 상태는 메모리에만 둔다 |
| 브라우저 뒤로가기 | `history.pushState`로 **더미 엔트리 1개**를 쌓고 `popstate`에서 **"현재 화면의 취소 동작"**(= 홈 복귀 또는 오버레이 복귀)으로 매핑한 뒤 다시 push. 브라우저를 떠나지 않게 한다 |
| 새로고침·탭 복구 | 항상 `Home`에서 시작한다. 진행 중이던 촬영 세션은 폐기하고 **OPFS 잔재를 정리**한다(부트스트랩 6단계). 로그인은 M2에 따라 해제된다 |
| 촬영 중 이탈 시도(`beforeunload`) | `Capture`·`Qr`·`FrameEditor` 상태에서만 `beforeunload`로 확인 프롬프트를 등록한다(브라우저가 문구는 자체 표시). 키오스크 모드에서는 사실상 발생하지 않지만 사고 방지용 |

---

## 4. 상단바 (`analysis/13 §3.2`, `analysis/60 §3.2`)

```
┌──────────────────────────────────────────────────────────────┐
│ [계정 라벨]        {브랜딩 앱 이름}         [홈] [⚙ 설정]     │
└──────────────────────────────────────────────────────────────┘
```

| 요소 | 규격 |
|------|------|
| 계정 라벨 | 비로그인 = **"로그인"**, 로그인 = **계정 id** |
| 계정 버튼 클릭 | 비로그인 → `Login` 오버레이 진입 / 로그인 → **계정 팝오버 토글** |
| 팝오버 항목 | **계정 관리**(로그인 전원) · **관리자 도구**(`isPower`만) · **로그아웃**(전원) — 3개 |
| 홈 버튼 | `Home` 화면에서 숨김 |
| 설정 버튼 | `Settings` 화면에서 숨김. **게스트 포함 누구나** 누를 수 있다(로그인 사용자는 PIN 게이트) |
| 버전 캡션 | 하단에 `v{version}` 흐린 캡션(**채널·빌드 시각 표기 없음** — it18), **로그인 무관 상시**, 클릭 비간섭 |
| 상태 미러 금지 | `isLoggedIn`·`isPower`는 **세션 스토어에서 직접 파생**한다. 별도 복사 상태를 두지 않는다(`analysis/60 §3.1`) |

### 4.1 TempUser 한도 배지

- 세션 사용자가 바뀔 때마다 재평가한다: 비로그인·비TempUser → 즉시 클리어, TempUser → `GET /accounts/me/qr-usage` **1회 조회(fire-and-forget)**.
- **조회 실패는 fail-open**(허용). 표시만 생략한다(M9).
- `role !== "temp_user"`인 응답의 `remaining*`은 0이지만 **무제한**을 뜻한다 — `role`을 먼저 보고 해석한다(`analysis/31 §4.4`).

---

## 5. 세션·인증 홀더 (혼동 금지)

| 홀더 | 내용 | 수명 | 파일 |
|------|------|------|------|
| **세션 컨텍스트** | `currentUser`(도메인 계정 — **`POST /auth/google` 응답의 `user` DTO 전체를 보관**. 별도 내 정보 조회 API가 없어(`analysis/31 §10`) 계정 화면 표시값의 유일한 출처다) — **화면·권한 판정의 유일한 근거** + 촬영 세션 데이터 | 앱 사용 동안 | `shell/sessionStore.ts` |
| **토큰 홀더** | JWT 문자열 | 페이지 수명, **메모리 전용** | `shell/authStore.ts` |

### 5.1 M1 배선 (가장 중요한 배선)

```ts
// shell/sessionStore.ts — currentUser 변경 진입점은 login / logout / expireSession / markPinSet 4개뿐
//   · expireSession(Step 12): 401 만료 전용. 촬영 데이터는 **유지**한다(§5.2 매트릭스).
//   · markPinSet(Step 13):    최초 PIN 설정 반영. **멱등이고 null을 만들지 않는다** → M1 구독 무영향.
//   규칙의 요지는 "진입점 개수"가 아니라 **currentUser 필드를 통해서만 바꾼다**는 것이다.
// ⚠️ 셀렉터 구독은 subscribeWithSelector 미들웨어가 있어야만 동작한다.
//    미들웨어 없이 subscribe(selector, listener)를 쓰면 Zustand가 두 번째 인자를 "조용히 무시"해
//    토큰 폐기가 한 번도 실행되지 않는다 — M1이 소리 없이 깨진다.
export const sessionStore = createStore(subscribeWithSelector((set) => ({ currentUser: null, ... })));

// shell/authStore.ts (초기화 시 1회)
sessionStore.subscribe(
  (s) => s.currentUser,
  (user) => { if (user === null) authStore.clearToken(); }   // ← 여기 한 곳이 전부를 덮는다
);
// 미들웨어를 쓰지 않는다면 반드시 prev 비교 형태로:
// sessionStore.subscribe((s, prev) => { if (s.currentUser === null && prev.currentUser !== null) authStore.clearToken(); });
```

- **배선이 실제로 동작하는지 단위 테스트로 고정한다**: `logout()` 호출 → `authStore` 토큰이 null (구독 자체가 끊긴 회귀를 잡는 유일한 방법이다).

| 규칙 | 이유 |
|------|------|
| 토큰 폐기를 **"로그아웃 버튼"에 걸지 않는다** | 게스트 전환 경로가 늘어도 한 곳이 전부를 덮는다(`analysis/61 §6.1`) |
| 업로드는 **선택적 Bearer** | 남은 토큰이 조용히 붙으면 게스트 촬영물이 직전 계정 소유로 기록되고 TempUser 무료 횟수까지 차감된다 |
| 검증 | E2E 테스트로 **"로그아웃 직후 업로드 요청에 `Authorization` 헤더가 없음"** 을 고정한다([10 §5](./10-testing-and-acceptance.md) E3 — 게스트는 정상 흐름에서 `Qr`에 도달하지 않으므로 effective QR을 목으로 켜서 업로드를 실행시켜 관측한다) |

### 5.2 로그아웃/유지 매트릭스 (`analysis/13 §3.3` 그대로)

| 트리거 | 현재 사용자 | 촬영 데이터 |
|--------|-------------|-------------|
| 계정 메뉴 "로그아웃" | **해제 + JWT 폐기** | 폐기 |
| 홈 버튼·각 화면 취소 | 유지 | 폐기 |
| 촬영 완료 후 자동 홈 복귀 | **유지** | 폐기 |
| **유휴 타임아웃 만료** | **유지(로그아웃 금지)** | 폐기 |
| 유휴 경고에서 "메인 화면으로" | 유지 | 폐기 |
| 전역 예외 복구 | 유지 | 폐기 |
| **JWT 만료 감지(Bearer 필수 호출 401 — 웹 고유)** | **해제 + JWT 폐기**([07 §4.3](./07-auth-and-permissions-web.md)) | **유지**(화면 유지 — 게스트로 계속) |
| **페이지 새로고침·탭 종료(웹 고유)** | **해제**(메모리 소실 — M2의 결과) | 폐기 |

---

## 6. 유휴 감시 (`analysis/13 §7`)

| 항목 | 값 |
|------|-----|
| 감시 대상 | `FrameSelect`, `Guide`, `Capture`, `CutSelect`, `Result`, `Qr` |
| 감시 제외 | `Home`, `Login`, `Settings`, `Account`, `UserMgmt`, **`FrameEditor`** |
| 무동작 판정 | **120초** |
| 경고 후 카운트다운 | **10초** |
| [이어서 진행하기] | 경고 해제 + 무동작 타이머 재시작. **현재 화면·로그인 유지** |
| [메인 화면으로] | 즉시 홈 복귀 |
| 카운트다운 0 | 홈 복귀. **로그아웃 금지** |
| 경고 표시 중 사용자 활동 | **무시**(버튼으로만 해제) |
| 활동 신호 | `window`의 `pointerdown`·`keydown`·`touchstart`·`wheel` (capture 단계, passive) |

### 6.1 웹 구현 규격 (WM3)

```ts
// 잘못된 구현: setInterval(tick, 1000) 카운터 누적 → 스로틀링에서 시간이 늘어난다
// 올바른 구현: 마지막 활동 시각을 저장하고 실경과를 계산한다
let lastActivityAt = performance.now();
function tick(now: number) {
  const idleMs = now - lastActivityAt;                       // ← 실경과
  ...
}
```

| 규칙 | 내용 |
|------|------|
| 시간 계산 | `performance.now()` 델타. tick 누적 금지 |
| tick 주기 | 250ms(`setInterval`) — 정확도는 실경과가 담보하므로 주기는 표시 갱신용일 뿐 |
| 카운트다운 표시 | `Math.ceil(남은ms / 1000)` |
| 탭 hidden 중 | 타이머는 계속 두되, **복귀 시 실경과로 즉시 재판정**한다(이미 만료됐으면 바로 홈 복귀) |
| 모달 위 표시 | 유휴 경고는 **모달 스택 최상단**에 올린다(다른 모달을 가려도 됨) |

### 6.2 유휴 경고와 화면 로컬 오버레이의 관계 (it20)

유휴 경고는 **셸이 소유**하고 화면 로컬 오버레이(프레임 준비 대기·삭제 확인·서버 등록 확인·카메라 초기화 대기)는 **화면이 소유**한다. 전역 busy 오버레이는 두지 않는다 — 대기는 각 화면의 고유 관심사다.

| 불변식 | 값 | 왜 |
|--------|-----|-----|
| **프레임 준비 총 대기 상한 < 유휴 무동작 판정** | 60초 **<** `IDLE_TIMEOUT_MS` 120초 | 손님이 대기 오버레이를 보는 중에 "자리를 비우셨나요?" 팝업이 겹치면 안 된다. 대기는 반드시 유휴 경고보다 **먼저** 끝난다 |

- 이 부등식은 **정적 테스트로 고정한다**(문서에만 두면 어느 한쪽 상수를 고칠 때 조용히 깨진다 — `15 §3.4` 관례).
- `Degraded`·`Failed` 국면을 손님이 방치하면 그때는 유휴 경고가 위에 겹쳐 홈으로 복귀시킨다 — **의도된 최종 탈출 경로**다.

---

## 7. 전체화면 제어 (WD7)

Windows의 표시 모드(`DisplayMode`/`WindowBounds`)를 대체한다. **설정 항목은 만들지 않는다.**

| 상황 | 동작 |
|------|------|
| 첫 사용자 제스처 | `documentElement.requestFullscreen({ navigationUI: "hide" })` 시도. 실패는 로그만(강제 불가) |
| 전체화면 진입 성공 시 | Chromium이면 `navigator.keyboard.lock(["Escape","F11"])` **best-effort** 시도(미지원·거부는 무시) |
| 전체화면 이탈 감지(`fullscreenchange`) | **상단 배너**: "전체화면이 해제되었습니다. [다시 전체화면으로]" — 탭하면 재요청. 촬영 흐름은 중단하지 않는다 |
| `Capture` 중 이탈 | 배너만 표시하고 시퀀스는 계속(탭이 hidden이 아닌 한). hidden이면 §8 |
| 화면 방향 | `screen.orientation.lock()`은 시도만(모바일 브라우저 상당수 미지원). 레이아웃은 **세로·가로 양쪽에서 동작**해야 한다([03 §1.2](./03-screens-spec.md)) |
| 진짜 락다운 | **브라우저 키오스크 모드가 담당**한다([09 §2](./09-kiosk-operations.md)). 앱은 이를 강제할 수 없음을 문서로 명시 |

---

## 8. 가시성·백그라운드 대응 (WM4 · WR3)

| 이벤트 | 화면 | 동작 |
|--------|------|------|
| `visibilitychange` → hidden | `Capture` | **촬영 시퀀스·카운트다운 취소 → 인코더 정지 → 카메라 정지 → 홈 복귀**. 사유 로그: "탭 비활성으로 촬영 취소" |
| `visibilitychange` → hidden | `Qr` 업로드 중 | 업로드는 계속 진행(중단하지 않는다). 진행률 갱신만 늦어질 수 있다 |
| `visibilitychange` → hidden | 그 외 | 아무 것도 하지 않는다 |
| `visibilitychange` → visible | 전부 | 유휴 타이머 재판정 + Wake Lock 재요청 + 전체화면 상태 재확인 |
| `pagehide` / `freeze` | 전부 | 카메라·인코더 정지(리소스 해제). 세션 데이터는 폐기 |

> **왜 촬영을 취소하는가**: 탭이 hidden이면 프레임 수신·타이머·인코딩이 모두 스로틀링된다. 그 상태로 계속 진행하면 컷이 비거나 타임랩스가 깨진 채 컷 선택으로 넘어간다. **부분 결과를 남기지 않고 취소하는 것이 안전측**이다.

---

## 9. 전역 예외 복구 (M16)

| 훅 | 처리 |
|----|------|
| `window.onerror` | 로그(Error) → 홈 복귀 → 토스트 "일시적인 오류가 발생했습니다." |
| `window.onunhandledrejection` | 동상 |
| React `ErrorBoundary` | 화면 트리 오류 시 셸만 남기고 **홈으로 리셋**(앱 전체 화이트스크린 금지) |
| 어댑터 내부 예외 | 어댑터가 삼켜 `false`/`null` 반환 + 로그. 상위는 상태로 표현 |
| 공통 규칙 | **로그인은 유지**한다. 촬영 데이터만 폐기 |

---

## 10. 토스트·모달 규격

| 항목 | 규격 |
|------|------|
| 토스트 | 성공(초록)·실패(빨강)·정보(중립) 3종. 4초 자동 소멸, 실패는 6초. **동시 최대 3개**(초과 시 오래된 것 제거) |
| 성공 오인 금지(M4) | 저장·삭제·업로드 실패는 **반드시 실패 토스트 또는 인라인 오류**로 표시. 색으로도 구분 |
| 모달 스택 | 배열로 관리. 최상단만 포커스 트랩. `Esc`로 닫히는 모달과 닫히지 않는 모달을 구분(PIN 입력은 `Esc` 취소 허용, 유휴 경고는 버튼만) |
| 모달 접근성 | `role="dialog"` `aria-modal="true"` + 진입 시 첫 포커스 지정 + 닫을 때 이전 포커스 복원 |
| 스크림 | 모달 배경 클릭으로 닫지 않는다(오조작 방지). 명시적 버튼만 |
