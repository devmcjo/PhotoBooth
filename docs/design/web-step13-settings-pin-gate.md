# Step 13 · 진입 PIN 게이트 + 설정 화면 구현 설계

| 항목 | 값 |
|------|-----|
| 대상 | 키오스크 웹 클라이언트 `webclient/`(TypeScript 5.7 · React 18 · Vite 5 · Zustand 5) |
| 브랜치 | `feature/web-client-foundation` |
| WBS | [`docs/web-client/11-wbs.md`](../web-client/11-wbs.md) **Step 13** |
| 규격 진실원 | `docs/analysis/41 §2`(설정 키·기본값·범위) · `docs/analysis/61 §7`(PIN) · `docs/analysis/13 §8·§9.3`(화면·모달) |
| 웹 규격 | [03 §12·§15.3](../web-client/03-screens-spec.md) · [05 §2·§5.4·§5.5](../web-client/05-storage-and-persistence.md) · [06 §2.0](../web-client/06-backend-integration-web.md) · [07 §6](../web-client/07-auth-and-permissions-web.md) · [12 C9](../web-client/12-web-vs-windows-differences.md) |
| 관례 | [15 · 구현 관례](../web-client/15-implementation-conventions.md) — 계층·테스트 전략·정적 불변식·함정 |
| 성격 | **설계 문서(코드 구현 금지)**. 구현 WBS는 §9에 embed |
| 작성 | 2026-08-01 · js-architect |

> **한 줄 요약**: PIN 게이트를 **화면 진입 시점의 렌더 게이트**로 만들어 어떤 진입 경로도 빠뜨릴 수 없게 하고,
> 설정 화면은 **React 밖 순수 함수 5개 + 얇은 뷰**로 쪼개 node에서 전부 검증한다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (코드·문서를 직접 읽어 확인 — 2026-08-01)

| # | 사실 | 근거 |
|---|------|------|
| F1 | **`accountService`에 PIN 3종이 이미 있다**: `verifyMyPin(pin)` · `setMyPin(newPin, currentPin?)` · `resetOtherPin(id, newPin)`. 본문도 06 §2.0대로다(`{pin}` / `{newPin[, currentPin]}` / `{newPin}`) | `src/adapters/http/accountService.ts:39-85` |
| F2 | ⚠️ **`setMyPin`에는 `unauthorized: "reject"` 가 없다.** `verifyMyPin`에만 붙어 있다 → `backendClient`의 기본값이 "Bearer가 붙었으면 `expired`"라서 **PUT의 401(= `currentPin` 불일치 / 서버에 이미 PIN 있음)이 로그아웃을 유발한다**(E17 회귀의 PUT 판) | `accountService.ts:51-59` ↔ `backendClient.ts:154-159` |
| F3 | 서버 PIN 계약: 형식 `^\d{4}$`(`PIN_RE`), verify는 **200 / 401 불일치 / 409 미설정**, `PUT /me/pin`은 **204 / 401(currentPin 누락·불일치) / 400(형식)** | `web/functions/src/domain/validation.ts:55-60`, `web/functions/src/routes/accounts.ts:52-90` |
| F4 | `SessionUser.hasPin`이 이미 있고 로그인 응답에서 파싱된다. **하지만 `hasPin`을 갱신하는 경로가 없다** — `sessionStore`의 `currentUser` 변경 진입점은 `login`/`logout`/`expireSession` 3개뿐 | `domain/accounts/sessionUser.ts:17,40`, `shell/sessionStore.ts:100-112` |
| F5 | `settingsStore.save(patch, {isGuest})`가 QR 정규화 → `settingsRepo.save(..., {omitKeys: GUEST_LOCKED_KEYS})` → **게스트 제한 키를 메모리에서도 되돌린다**. `settingsRepo.save`는 기존 저장값 위에 병합해 **알 수 없는 키와 omit 키를 보존**한다 | `shell/settingsStore.ts:71-99`, `adapters/storage/settingsRepo.ts:158-190` |
| F6 | ⚠️ **`settingsStore.reEnableQr()`·`saveWebExtras()`는 호출자가 0곳**이고 둘 다 `isGuest: false` **하드코딩**이다. 게스트가 부르면 제한 키가 기록된다 | `settingsStore.ts:101-115` · `grep -rn "reEnableQr\|saveWebExtras" src tests` = 0건 |
| F7 | 설정 clamp·QR 정규화·게스트 제한 키 목록은 도메인에 완비돼 있다(`clampSettings`·`normalizeQrToggles`·`onQrReEnabled`·`GUEST_LOCKED_KEYS` 11개·`applyConnectionFallbacks`·`isAutoCutCount`) | `domain/settings/*.ts` |
| F8 | `resultsStore`가 완성돼 있다(`listFolders`/`usage`/`removeFolder`/`readFile`/`enforceRetention`). 전부 **예외를 전파하지 않고** 빈 값·`false`로 축소한다. `removeFolder`는 `isResultFolderName` 게이트를 통과한 이름만 지운다 | `adapters/storage/resultsStore.ts` |
| F9 | `App.tsx`의 `DummyScreen`에 **임시 진입점 2개**가 있다: [카메라 테스트 열기](모든 더미 화면) · [로컬 저장 폴더 선택](`screen === "Settings"` + `dirHandleRepo.isSupported()`). `pickLocalSaveFolder()`도 `App.tsx`에 있다 | `src/App.tsx:70-117` |
| F10 | `ModalId`에 **`"pinPrompt"` 가 이미 예약**돼 있고 `ModalStack`의 `default:` 가 "아직 구현되지 않았습니다" 스텁을 렌더한다. `Modal`은 `dismissible`이면 `Esc`로 `popModal`한다 | `shell/shellStore.ts:26-32`, `App.tsx:151-172`, `ui/components/index.tsx:54-96` |
| F11 | 로그 마스킹 금지 키에 `pin`·`newpin`·`currentpin`·`code`·`state`·`nonce`·`token`이 들어 있다(소문자·구분자 제거 후 비교) | `adapters/storage/logPolicy.ts:31-66` |
| F12 | 카메라 열거·매칭·라벨 폴백이 완비돼 있다: `listCameras()` · `matchDevice(devices, stored)`(deviceId→label→groupId→first) · `displayLabel(device, i)`("카메라 N") · `onDeviceChange(listener)` | `adapters/camera/deviceEnumerator.ts` |
| F13 | **테스트 환경은 node이고 jsdom·@testing-library가 없다.** `package.json`에 devDependency 0건 → **React 컴포넌트를 렌더하는 테스트를 쓸 수 없다** | `vitest.config.ts:9`, `package.json` |
| F14 | **Playwright가 설치돼 있지 않다**(`package.json`에 없음, `playwright.config.ts` 없음, `tests/e2e/` 없음). 11-wbs Step 13의 검증 명령 `npx playwright test e2e/pin.spec.ts`는 **현재 실행 불가**다 | `package.json`, `vitest.config.ts:11` |
| F15 | **baseline 실측(2026-08-01, 이 설계 작성 중 직접 실행)**: `npx vitest run` → **45파일 1051 통과**(문서 수치와 일치) | `npx vitest run` 출력 |
| F16 | `persistStorage`에는 **조회 전용 함수가 없다**. `requestPersistentStorage()`는 미승인 시 `persist()`를 **실제로 요청**한다(Firefox에서 프롬프트) | `adapters/platform/persistStorage.ts:35-63` |
| F17 | `healthService.probe()`가 `{configured, reachable, deployedAt, gateKeyValid, detail}`을 돌려준다(서버 연결 상태 표시에 그대로 쓸 수 있다) | `adapters/http/healthService.ts:18-33` |
| F18 | `exportBlob(blob, fileName)` · `canExportFile()` · `settingsRepo.exportJson()`이 이미 있다(설정 내보내기 1차 재료 완비) | `adapters/platform/fileExport.ts`, `settingsRepo.ts:192-205` |
| F19 | `domain/auth/*` 4파일은 **`domain/index.ts` 배럴에 없다**(명시 경로로만 import). `domain/settings/*`·`domain/results/*`는 배럴에 있다 | `domain/index.ts:34-49` |
| F20 | OAuth 콜백의 `returnTo`는 **`Settings`·`Account`를 포함한 4종으로 clamp**되고, `applyOauthCallbackOutcome`이 `go(returnTo)`로 **직행**한다 → **로그인 직후 설정 화면에 PIN 없이 도달하는 경로가 실재한다** | `screens/oauthCallback/oauthCallbackRunner.ts:162-173`, [07 §2.2 h](../web-client/07-auth-and-permissions-web.md) |
| F21 | `shellStore.pushModal`은 **동기 fire-and-forget**이다. 결과를 기다릴 채널이 없다 | `shellStore.ts:143-147` |
| F22 | 도메인 순수성 테스트가 `src/domain/**` 전체를 glob으로 검사한다 — `Date.now`·`localStorage`·`crypto`·`window` 등 금지 | `tests/unit/domain/purity.test.ts` |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 |
|---|------|------|
| A1 | `crypto`·jsdom 없이도 PIN 모달의 로직 전량을 node에서 검증할 수 있다(컴포넌트는 렌더·입력 버퍼만 남는다) | Step 13-3·13-4 (테스트 통과) |
| A2 | 게이트를 **렌더 시점**에 두어도 규격의 "매번 확인"이 성립한다(화면 이탈 시 승인 폐기) | Step 13-5 (`pinGate.test.ts`의 화면 변경 시 승인 폐기 케이스) |
| A3 | `localStorage` 잠금 레코드의 시계 왜곡(과거로 되돌린 시스템 시각)이 키오스크를 영구 잠그지 않는다 — `until - now > PIN_LOCK_MS`면 상한으로 clamp | Step 13-1 (`pinGatePolicy.test.ts`) |
| A4 | `settingsStore.save`에 `webExtras`를 합쳐도 기존 호출부(`bootstrap.test.ts:179`)가 깨지지 않는다(선택 필드 추가) | Step 13-6 (`npx tsc --noEmit` + 전체 vitest) |
| A5 | 서버 `PUT /accounts/me/pin`이 "PIN 미보유"라고 클라가 믿는 상태에서 실제로는 보유 중일 때 **401**을 준다(F3의 라우트 코드 독해 기준. 실 서버 왕복은 미검증) | Step 13-3(코드 경로) + **V22-4 실측** |
| A6 | 실기기에서 온스크린 키패드 터치 타깃(48px)과 `aria-live` 안내가 스크린리더에 정상 전달된다 | **V22 실측**(자동화 불가) |

---

## 1. 이 Step이 푸는 문제 3개

| 문제 | 왜 어려운가 | 이 설계의 답 |
|------|-------------|-------------|
| **P1. 게이트를 빠뜨리지 않기** | 설정 진입로가 ① 상단바 [설정] ② OAuth 복귀(`returnTo="Settings"` — F20) ③ `Settings→Login→Settings` 왕복 ④ 앞으로 생길 경로 로 계속 늘어난다. **호출부마다 게이트를 붙이면 반드시 하나가 빠진다**(analysis/61 §7.1이 명시적으로 경고) | 게이트를 **네비게이션이 아니라 화면 렌더에** 건다. `<PinGate>`를 통과하지 못하면 `SettingsView`가 **애초에 마운트되지 않는다** |
| **P2. 모달의 결과를 기다리기** | `pushModal`은 결과를 돌려주지 않고(F21), 모달은 `Esc`·`returnHome`·`clearModals`로 **밖에서 사라질 수 있다**. 약속이 미해결로 남으면 스피너 고착 | `shell/pinGate.ts`가 **1개짜리 pending 채널**을 소유한다. 해제는 멱등이고, 모달 언마운트·화면 변경·마운트 타임아웃 **3경로 전부**가 `cancelled`/`unavailable`로 닫는다(fail-closed) |
| **P3. 게스트가 운영자 값을 덮어쓰지 않기** | "OFF 표시 + 비활성"만으로는 부족하다. draft·patch·store·repo 어디 한 곳이라도 게스트 키를 흘리면 운영자 설정이 조용히 사라진다 | **4중 방어**: ① 렌더 가드(비활성) ② 액션 첫 줄 가드 ③ `buildSavePatch`가 제한 키를 **패치에서 제외** ④ `settingsRepo`의 `omitKeys`(기존) |

---

## 2. 계층 배치 (한눈에)

```
ui/views/PinGate.tsx ─────────────── 상태만 읽어 children/스피너/무렌더 선택
ui/views/SettingsView.tsx ────────── 섹션 렌더 · sticky 저장 바 · 컨트롤 disabled
ui/components/fields.tsx ─────────── SettingRow · Toggle · ChoiceGroup · TextField · Stepper
screens/modals/pinPrompt/
    PinPromptModal.tsx ──────────── 키패드 · 4칸 인디케이터 · aria-live (입력 버퍼만 소유)
    pinPromptRunner.ts ──────────── ★ React 무관. 1회 제출의 전 경로(node 테스트)
screens/settings/
    useSettingsScreen.ts ────────── draft 훅(얇음)
    settingsForm.ts ─────────────── ★ draft→patch·QR 재활성·저장 4단(node 테스트)
    storedResultsPanel.ts ───────── ★ 보관 결과물 목록·삭제(node 테스트)
    cameraDevicePanel.ts ────────── ★ 장치 목록·매칭·라벨(node 테스트)
    serverStatusPanel.ts ────────── ★ health probe + 취소(node 테스트)
    settingsTransfer.ts ─────────── ★ 내보내기/가져오기 조립(node 테스트)
shell/pinGate.ts ─────────────────── ★ 게이트 판정 · 프롬프트 채널 · 생명주기(node 테스트)
domain/auth/pinGatePolicy.ts ─────── ★ 형식·실패 상태머신·잠금·응답 분류(순수)
domain/settings/settingsEditPolicy.ts ★ 편집 가능 판정·미노출 키·QR 관련 키(순수)
domain/settings/settingsImport.ts ── ★ 가져오기 파일 파싱(순수)
domain/results/byteFormat.ts ─────── ★ 용량 표기(순수)
adapters/storage/pinLockRepo.ts ──── localStorage 잠금 레코드(예외 전파 금지)
```

★ = React·DOM을 import하지 않는 파일. **jsdom이 없으므로(F13) 판정 로직이 컴포넌트에 들어가면 영원히 검증되지 않는다.**

---

## 3. PIN 게이트 설계

### 3.1 왜 "네비게이션 가드"가 아니라 "렌더 게이트"인가 (핵심 판단)

Windows는 `AppShellViewModel.OpenSettings`가 **이동 전에** 게이트를 태운다(진입점이 1개이기 때문에 가능하다).
웹은 F20 때문에 진입점이 최소 2개이고, `shellStore.go()`는 **동기**라 `await`을 끼울 수 없다.

| 방식 | 게이트 누락 위험 | `go()` 계약 | OAuth 복귀 |
|------|------------------|-------------|------------|
| 호출부마다 `await ensurePinGate()` 후 `go()` | **높다**(새 호출부가 생길 때마다) | 유지 | 별도 처리 필요 |
| `go()` 자체를 async로 | 낮다 | **깨진다**(38곳 호출부·`canTransition` 테스트) | 처리됨 |
| **`<PinGate>` 렌더 게이트** ✅ | **구조적으로 0**(라우터를 지나야만 화면이 있다) | 유지 | **자동 처리** |

렌더 게이트의 유일한 비용은 "화면 전이가 먼저 일어나고 그 위에 모달이 뜬다"는 것인데,
게이트 미통과 동안 `SettingsView`가 **마운트조차 되지 않으므로 설정값이 화면에 노출되지 않는다**. 실패 시 `closeOverlay()`로 직전 화면에 되돌린다.

> **`Account`도 함께 감싼다.** 07 §6.1 표가 설정·계정 **양쪽**을 게이트 대상으로 규정하고, WBS Step 13의 [trigger]도
> "로그인 사용자의 **설정·계정** 진입에만"이다. `Account` 화면 자체는 Step 16이 만들지만(이번 Step은 더미 유지)
> **게이트 배선만 지금 넣는다** — 나중에 붙이면 "한 경로가 게이트를 빼먹는" 정확히 그 실패가 난다.

### 3.2 판정 함수 (`shell/pinGate.ts`)

07 §6.2 의사코드를 그대로 옮긴다.

```ts
export type PinGateDenial = "locked" | "unavailable" | "cancelled" | "exhausted";
export type PinGateResult =
  | { readonly kind: "notRequired" }               // 게스트 — 무가드
  | { readonly kind: "granted" }
  | { readonly kind: "denied"; readonly reason: PinGateDenial };

export interface PinGateDeps {
  readonly user: SessionUser | null;
  readonly now: () => number;
  readonly lock: PinLockRepo;
  /** `accountService`를 만들 수 있는가. false면 **fail-closed**. */
  readonly accountAvailable: boolean;
  /** 모달 채널. **절대 reject하지 않는다**(호출부가 try/catch로 한 번 더 막는다). */
  readonly openPrompt: (request: PinPromptRequest) => Promise<PinPromptOutcome>;
  readonly toast: (kind: "error" | "info", message: string) => void;
}

export async function ensurePinGate(deps: PinGateDeps): Promise<PinGateResult>;
```

```
ensurePinGate:
  1. user === null                     → notRequired            # 게스트 무가드(07 §6.1)
  2. lock.read(now) !== null           → toast(잠금 문구 + 남은 시간) → denied("locked")   # WD16
  3. !accountAvailable                 → logger.error → denied("unavailable")             # fail-closed
  4. mode = user.hasPin ? "verify" : "setup"
  5. try { outcome = await openPrompt({ mode }) } catch { → denied("unavailable") }        # fail-closed
  6. outcome.kind === "granted" ? granted : denied(outcome.kind)
```

**어떤 분기도 "확인 불가 → 통과"가 없다.** 2·3·5는 전부 denied다.

### 3.3 게이트 생명주기 스토어 — "매번"과 StrictMode를 동시에 만족시키기

`<PinGate>`의 `useEffect`에서 게이트를 시작하고 **cleanup에서 취소**하면, `<StrictMode>`의 이중 effect가
1회차를 즉시 취소해 사용자가 설정 화면에서 튕겨 나간다(Step 12에서 콜백 처리로 같은 함정을 밟았다 — 15 §6).

그래서 게이트 상태를 **React 밖 모듈 스토어**에 두고, **화면·사용자 변경**을 취소 신호로 삼는다.

```ts
interface PinGateState {
  readonly screen: AppState | null;   // 승인이 유효한 화면
  readonly userId: string | null;     // 승인이 유효한 사용자(게스트는 null)
  readonly status: "idle" | "checking" | "granted" | "denied";
}
export const pinGateStore: StoreApi<PinGateState>;
export function usePinGateStatus(screen: AppState): PinGateState["status"];

/** 멱등. 이미 같은 (screen, userId)로 checking·granted·denied면 **아무 일도 하지 않는다**. */
export function ensureScreenPinGate(screen: AppState): void;

/** `main.tsx`가 1회 설치. 화면 또는 `currentUser`가 바뀌면 승인을 폐기하고 열린 모달을 닫는다. */
export function installPinGateLifecycle(): () => void;
```

| 규격 | 구현 |
|------|------|
| **매번 확인**(07 §6.1) | 화면이 `Settings`를 떠나면 `status → idle`. 다시 들어오면 처음부터 다시 판정한다 |
| 로그아웃·세션 만료 | `currentUser` 변경 → `idle` + 열린 `pinPrompt` 모달을 `cancelled`로 닫는다 |
| StrictMode 이중 effect | `ensureScreenPinGate`가 멱등이고 **cleanup이 없다** → 2회차는 no-op |
| 거부 후 재진입 루프 | `status === "denied" && screen === state.screen`이면 재실행하지 않는다 |
| 거부 처리 | `ensurePinGate`가 denied를 돌려주는 **셸 안에서** `toast` + `shellStore.closeOverlay()`를 수행한다(컴포넌트가 아니라) |

### 3.4 모달 채널 (P2의 답)

```ts
export interface PinPromptRequest { readonly mode: "verify" | "setup" }
export type PinPromptOutcome =
  | { readonly kind: "granted" }
  | { readonly kind: "cancelled" }     // Esc · [닫기] · 언마운트
  | { readonly kind: "exhausted" }     // 5회 실패 → 잠금
  | { readonly kind: "unavailable" };  // 모달이 뜨지 못함

export function openPinPrompt(request: PinPromptRequest): Promise<PinPromptOutcome>;
export function currentPinPrompt(): PinPromptRequest | null;
export function usePinPrompt(): PinPromptRequest | null;
/** **멱등** — 이미 해제됐으면 무시한다. 두 경로가 동시에 닫아도 안전하다. */
export function resolvePinPrompt(outcome: PinPromptOutcome): void;
/** 모달이 실제로 마운트됐음을 알린다(마운트 감시 타이머 해제). */
export function notifyPinPromptMounted(): void;
```

- `openPinPrompt`는 `shellStore.pushModal({ id: "pinPrompt", dismissible: true })` 후 promise를 돌려준다.
- **마운트 감시**: `PIN_PROMPT_MOUNT_TIMEOUT_MS = 5000`. 그 안에 `notifyPinPromptMounted()`가 오지 않으면
  `unavailable`로 해제한다 — 게이트가 무한 스피너로 고착되는 대신 **우아하게 튕겨 나온다**.
  (렌더 트리가 깨졌거나 모달 스택이 다른 모달에 점유된 경우. 발생하면 로그 `PIN 모달이 표시되지 않았습니다`.)
- 해제 시 항상 `popModal("pinPrompt")`를 함께 수행한다(모달이 남지 않는다).
- 이미 pending이 있는데 `openPinPrompt`가 또 불리면 **기존 promise를 그대로 돌려준다**(모달 2중 오픈 금지).

### 3.5 시도 1회의 전 경로 (`screens/modals/pinPrompt/pinPromptRunner.ts`)

```ts
export type PinAttemptResult =
  | { readonly kind: "granted" }
  | { readonly kind: "retry"; readonly state: PinAttemptState; readonly message: PinMessageKey }
  | { readonly kind: "exhausted"; readonly state: PinAttemptState }
  | { readonly kind: "switchToSetup" }    // verify 중 409 — 서버에 PIN이 없다
  | { readonly kind: "switchToVerify" }   // setup 중 401 — 서버에 이미 PIN이 있다(A5)
  | { readonly kind: "unavailable"; readonly message: PinMessageKey };  // 실패 카운트 미가산

export async function runPinAttempt(
  state: PinAttemptState,
  pin: string,
  deps: PinAttemptDeps,
): Promise<PinAttemptResult>;
```

| 응답 | verify 모드 | setup 모드(currentPin 미전송) |
|------|-------------|-------------------------------|
| 2xx | `granted` | `granted` + **`markPinSet()`**(§3.6) |
| **401** | 불일치 → `applyPinFailure` → `retry`(1.5초 쿨다운) 또는 5회째면 `exhausted`(+잠금 기록) | **`switchToVerify`** — 서버에 이미 PIN이 있다는 뜻이다(06 §2.0: 보유 시 `currentPin` 필수). **실패로 세지 않는다** |
| **409** | **`switchToSetup`** — 최초 설정 플로우로 전환. **실패로 세지 않는다** | (발생하지 않음) |
| 400 | `unavailable`(형식 — 클라가 먼저 막으므로 발생하면 계약 불일치. 로그에 `errorStatus` 남김) | 동상 |
| 기타 4xx/5xx·네트워크 | **`unavailable`** + *"확인할 수 없습니다. 네트워크를 확인하세요."* · **실패 카운트 미가산 · 게이트 미개방** | 동상 |

- 401을 "만료"로 오해하지 않기 위해 **`verifyMyPin`·`setMyPin` 둘 다** `unauthorized: "reject"` 를 넘긴다(F2 수정).
- `TempUserLimitError`·`SsoNotConfiguredError`는 PIN 경로에 오지 않는다. `NotAuthenticatedError`(토큰 없음)는 `unavailable`로 접는다.
- ⚠️ **로그에 PIN을 남기지 않는다.** 컨텍스트 키는 `gateMode`·`failCount`·`attemptOutcome`·`errorStatus`만 쓴다.
  `pin`·`newPin`·`currentPin`·`code`·`state`·`token`은 마스킹 대상(F11)이라 담아도 무의미하고, 담으려 이름을 바꾸면 진짜로 샌다.

### 3.6 `hasPin` 갱신 — 왜 새 진입점이 **반드시** 필요한가

07 §6.2가 "성공 → 세션의 `hasPin = true` → true (재확인 요구하지 않음 — 데드락 방지)"라고 쓴다.
갱신하지 않으면 다음 진입에서 다시 **setup 모드**가 뜨고, 그때 서버는 이미 `pinHash`를 갖고 있으므로
`currentPin` 없는 PUT이 **401**이 된다(F3) → 운영자가 설정에 영구히 못 들어간다.

```ts
// shell/sessionStore.ts — 신설
/**
 * 최초 PIN 설정 성공 반영. **멱등**이고 `currentUser`를 null로 만들지 않는다 →
 * M1 구독("필드가 null이 되는 것")에 영향이 없다.
 */
markPinSet(): void {
  const user = get().currentUser;
  if (user === null || user.hasPin) return;
  set({ currentUser: { ...user, hasPin: true } });
}
```

- AUTH-1 정적 검사(`/\.login\s*\(/`)에 걸리지 않는다.
- `currentUser` 변경 진입점이 **4개**가 된다 → `sessionStore` 주석·[02 §5.1](../web-client/02-app-shell-and-navigation.md):174·[15 §6](../web-client/15-implementation-conventions.md)을 함께 고친다
  (02 §5.1의 현재 문구는 이미 stale하다 — `login / logout / resetUserForTest`라고 쓰여 있고 Step 12의 `expireSession`이 빠져 있다).

### 3.7 기기 단위 잠금 (WD16 · 12 C9)

| 항목 | 설계 |
|------|------|
| 키 | `localStorage["mcphoto.pinLock.v1"]` = `{ until: number, fails: number }` |
| 소유자 | **`adapters/storage/pinLockRepo.ts` 한 파일**(정적 불변식 PIN-3이 고정) |
| 판정 | `parsePinLockRecord(raw, now)` — 손상·만료(`until <= now`)는 `null`. **`until - now > PIN_LOCK_MS`면 `now + PIN_LOCK_MS`로 clamp**(A3 — 시스템 시각을 과거로 되돌린 기기가 영구 잠기는 것을 막는다) |
| 저장 실패 | `false` 반환. **잠금 없이 진행**한다(fail-open) — 잠금은 강화 장치이고, 세션 내 5회 제한은 여전히 살아 있다. 프라이빗 모드에서 설정 자체를 못 여는 것이 더 나쁘다 |
| 성공 시 | `clear()` — 카운터·잠금 초기화 |
| 계정 단위 잠금 | **만들지 않는다**(DoS — analysis/61 §7.3) |
| 안내 | *"PIN 입력이 일시적으로 차단되었습니다. {남은 시간} 후 다시 시도해 주세요."* |

`M2`(JWT 메모리 전용)와 충돌하지 않는다 — 저장하는 값은 **epoch ms와 정수 하나**이고 자격증명이 아니다.
`pinLockRepo.ts`를 `authInvariants.test.ts`의 `AUTH_FILES`(localStorage 0건 검사) **목록에 넣지 않는다**. 대신 PIN-3으로 별도 고정한다.

### 3.8 PIN 모달 UI (03 §15.3 · 07 §6.4)

```
┌───────────────────────────────────────┐
│  설정 진입 PIN을 입력하세요.            │   ← mode에 따라 제목 3종
│                                       │
│           ● ● ○ ○                     │   ← 4칸 마스킹(입력 자리 수만)
│                                       │
│   ┌───┐ ┌───┐ ┌───┐                   │
│   │ 1 │ │ 2 │ │ 3 │                   │   ← 온스크린 키패드(48px+)
│   │ 4 │ │ 5 │ │ 6 │                   │      각 키 aria-label
│   │ 7 │ │ 8 │ │ 9 │                   │
│   │지움│ │ 0 │ │확인│                  │
│   └───┘ └───┘ └───┘                   │
│  ⚠ PIN이 일치하지 않습니다. (2/5)      │   ← aria-live="assertive"
│                          [ 닫기 ]      │
└───────────────────────────────────────┘
```

| 항목 | 규격 |
|------|------|
| 모드 3종 | `verify`(확인) / `setup`(새 PIN) / `setupConfirm`(새 PIN 재입력 — `setup`의 2단계) |
| 입력 | 온스크린 키패드 + **물리 키보드 숫자 키**(`keydown`으로 0~9·Backspace·Enter). `<input>`을 쓰지 않아 `autocomplete` 노출면 자체가 없다 |
| 자동 제출 | 4자리가 차면 **자동 제출하지 않는다** — [확인]을 눌러야 한다(오입력 1회가 곧 카운트라서 실수 여지를 줄인다) |
| 쿨다운 | 불일치 후 **1.5초** 전 키 비활성. 남은 시간을 진행 표시로 보여준다 |
| 5회째 | 모달 닫힘 + 5분 잠금 + `exhausted` |
| 취소 | [닫기]·`Esc` → `cancelled`. 언마운트 cleanup도 `cancelled`(멱등이라 이중 해제 안전) |
| 접근성 | 키패드 각 키 `aria-label`, 인디케이터 `aria-label="입력 4자리 중 N자리"`, 오류 `role="alert"`/`aria-live="assertive"` |
| 보안 | 입력 버퍼는 컴포넌트 state에만. 제출 후 즉시 비운다. **어디에도 로깅하지 않는다** |

`setup` 2단계 불일치(*"두 번 입력한 PIN이 서로 다릅니다."*)는 **서버 왕복 없이** 처리하고 **실패 카운트에 세지 않는다**
(서버 PIN 불일치가 아니라 사용자의 오타다).

---

## 4. 설정 화면 설계

### 4.1 섹션 구성 (03 §12.1)

| # | 섹션 | 항목 | 게스트 |
|---|------|------|--------|
| 1 | **촬영** | 컷 수(**자동**/6/8/10 · sentinel `0`) · 카운트다운(3/6/8/10) · 거울모드🔒 · 플래시 · 셔터음 · 재촬영 사용🔒(+ 횟수 1/2/3🔒) | 🔒 3개 |
| 2 | **장치** | 카메라 장치 선택 + [재검색] + [카메라 테스트] · 전/후면 힌트 | — |
| 3 | **출력·전송** | 출력 포맷(JPG/PNG) · QR 전송🔒(+ 사진🔒 · 타임랩스🔒 · 보관 시간 1~72h) · 로컬 저장 · **[로컬 저장 폴더 선택]/[해제]** | 🔒 3개 |
| 4 | **필터** | 원본(**항상 on·비활성**) · 흑백🔒 · 밝게🔒 · 뷰티🔒 | 🔒 3개 |
| 5 | **고급** | 다운로드 페이지 Base URL🔒 · Storage 버킷🔒 · **서버 연결 상태(읽기 전용)** + [다시 확인] | 🔒 2개 |
| 6 | **저장소·데이터**(웹 전용) | 저장소 영속 상태 + 사용량/할당량 + [영속 요청] · **[보관된 결과물]** 패널 · [설정 내보내기]/[설정 가져오기] | — |

🔒 = `GUEST_LOCKED_KEYS` 11개 전부(거울모드·재촬영·재촬영횟수·필터 3종·QR 3종·HostingBaseUrl·StorageBucket).

**미노출 4항목**(`DisplayMode`·`WindowBounds`·`ExternalCameraEnabled`·`PhotoPrinterEnabled`)은 렌더하지 않되
draft에 포함하지 않음으로써 `settingsStore.save(patch)`의 병합이 **현재 값을 그대로 유지**한다(WD7·WD8).
표시 모드는 웹에 개념이 없다(12 C4).

**이번 Step에서 만들지 않는 웹 전용 항목**(03 §12.1이 나열하지만 선행 Step이 필요하다):

| 항목 | 이월 | 근거 |
|------|------|------|
| [프레임 내보내기]/[가져오기] | **Step 15** | 로컬 프레임 저장소가 Step 14에 생긴다 |
| [앱 업데이트 확인] | **Step 16** | Service Worker 등록이 부트스트랩 7단계(Step 16)다 |
| [진단·상태] 버튼(로그인 전용) | **Step 16** | 모달 본체가 Step 16이다. 지금 버튼만 넣으면 운영자에게 *"아직 구현되지 않았습니다"* 스텁(F10)이 노출된다 — 정직하지 않다 |

### 4.2 게스트 편집 제한 — 4중 방어 (M10 + analysis/41 §2.3)

```
① 렌더 가드     <Toggle disabled={locked} aria-describedby="guest-note-MirrorMode" />
                 + 옆에 상시 "로그인 필요" 배지
② 액션 가드     function change(key, value) {
                   if (!isSettingEditable(key, ctx)) { logger.warn("제한 항목 편집 시도", { settingKey: key }); return; }
                   ...
                 }
③ 패치 가드     buildSavePatch(draft, ctx) → 제한 키를 **패치에서 제외**
④ 저장소 가드   settingsStore.save(patch, { isGuest }) → settingsRepo의 omitKeys(기존 F5)
```

**게스트 표시 규칙**: 값은 **OFF로 표시**한다(03 §12.3). 하지만 **런타임 동작은 저장된 운영자 값 그대로**다.
→ 화면 전용 파생값 `displayValue(key, storedValue, ctx)`를 `settingsEditPolicy`에 둔다: 게스트 + 제한 키 + boolean이면 `false`.
저장 경로는 이 값을 절대 쓰지 않는다(③이 막는다).

**TempUser 한도 초과**: `isTempUserQrBlocked()`(기존 `shell/qrUsageStore`)가 true면 **QR 관련 4키만 추가 차단**한다
(`EnableQrDelivery`·`SendPhoto`·`SendTimelapse`·`RetentionHours` — 보관 시간은 03 §12.1에서 QR 전송의 하위 항목이다).
안내 문구를 QR 섹션에 붙인다. 값 표시는 **가리지 않는다**(운영자 값 그대로 — 게스트와 다르다).

### 4.3 저장 절차 (03 §12.4 — 순서가 규격)

```ts
export interface SaveSettingsDeps {
  readonly draft: SettingsDraft;
  readonly ctx: SettingsEditContext;                 // { isGuest, qrBlocked }
  readonly save: (patch, options) => boolean;        // settingsStore.save
  readonly readBack: () => AppSettingsValues;        // settingsStore 재조회
  readonly resetDraft: (values, webExtras) => void;
  readonly toast: (kind, message) => void;
}
export function saveSettings(deps: SaveSettingsDeps): { ok: boolean };
```

```
1. (웹에는 "창 기하 캡처" 단계가 없다 — WD7)
2. patch = buildSavePatch(draft, ctx)                        # 제한·미노출 키 제외
   ok    = save(patch, { isGuest, webExtras: draft.webExtras })   # 내부에서 clamp + QR 정규화
3. resetDraft(readBack())                                     # ★ 보정된 값을 화면에 재반영
4. 즉시 적용(웹에서는 카메라 장치 힌트뿐 — 카메라는 이 화면에서 돌지 않는다)
5. toast(ok ? "저장했습니다." : "저장 위치에 쓸 수 없습니다.")   # M4 — 성공 오인 금지
```

- **3번을 빼면** 컷 수 7 입력이 6으로 보정된 사실이 화면에 안 보이고, QR 정규화(사진·타임랩스 둘 다 off → QR off)가 감춰진다.
- `ok === false`여도 **draft를 되돌리지 않는다**(다시 시도할 수 있게). 단 `settingsStore`가 게스트 제한 키를 메모리에서 되돌리므로(F5) 3번의 재반영이 그것을 화면에 반영한다.
- 저장/닫기 바는 **스크롤 영역 밖 sticky**(`position: sticky; bottom: 0`)로 항상 노출한다.

### 4.4 QR 토글 연동 (analysis/41 §2.4 · 03 §12.5)

| 규칙 | 어디에 | 구현 |
|------|--------|------|
| **정규화**(QR on + 하위 둘 다 off → QR off, 하위 값 보존) | 로드·저장 — **이미 있다** | `clampSettings` / `settingsStore.save`(F7) |
| **재활성**(QR off → on 순간 하위 둘 다 on) | **화면 로직에만** | `applyQrToggle(draft, "EnableQrDelivery", true)` → `onQrReEnabled()` 적용 |
| 로드 중 억제 | 구조적으로 보장 | `applyQrToggle`은 **사용자 이벤트에서만** 호출된다. hydrate·resetDraft 경로는 이 함수를 지나지 않는다 |

⚠️ **`settingsStore.reEnableQr()`를 쓰지 않는다**(F6 — `isGuest: false` 하드코딩 + 토글 즉시 저장이라 sticky 저장 바 모델과 충돌).
호출자가 0곳인 죽은 코드이므로 **제거**한다(§7 이탈 ②).

### 4.5 카메라 장치 선택 (03 §12.6)

| 항목 | 구현 |
|------|------|
| 열거 | `listCameras()`를 **백그라운드**(effect)로. 실패는 빈 배열(F12) |
| 라벨 | `displayLabel(device, i)` — 빈 라벨이면 "카메라 N" + 안내 *"권한을 허용하면 장치 이름이 표시됩니다."* |
| 매칭 | `matchDevice(devices, { deviceId: values.CameraDevice, label: webExtras.CameraDeviceLabel, groupId: webExtras.CameraDeviceGroupId })` |
| 저장 | 선택 시 draft에 `CameraDevice`(deviceId) + `webExtras.{CameraDeviceLabel,CameraDeviceGroupId}` 3개를 함께 넣는다 |
| 목록 비었을 때 | 선택·[카메라 테스트] 비활성 + 안내 |
| `devicechange` | `onDeviceChange(listener)` 구독. **반환된 해제 함수를 effect cleanup에서 반드시 호출**한다 |
| [카메라 테스트] | `pushModal({ id: "cameraTest", dismissible: true })` — `App.tsx`의 임시 진입점(F9)을 여기로 옮긴다 |
| 전/후면 | `webExtras.CameraFacing` 선택(user/environment) |

### 4.6 [보관된 결과물] 패널 — **Step 10 이월분**

`resultsStore`(F8) 위에 얹기만 한다. **새 저장소 코드를 쓰지 않는다.**

```ts
export interface StoredResultsView {
  readonly loading: boolean;
  readonly totalBytes: number;
  readonly folders: readonly { readonly name: string; readonly bytes: number }[];
  readonly storageLow: boolean;   // isStorageLow(status) — 05 §5.4
}
export async function loadStoredResults(deps): Promise<StoredResultsView>;
export async function removeStoredResult(deps, name): Promise<boolean>;
export async function removeAllStoredResults(deps, names): Promise<{ removed: number; failed: number }>;
```

| 항목 | 규격 |
|------|------|
| 목록 | `usage()` 1회 왕복(폴더별 바이트 + 총량). 이름 오름차순 = **오래된 순**(0 패딩 규약) |
| 표시 | `formatBytes` · 폴더 수 · 총 용량 · 여유 10% 미만이면 **경고 배지**(05 §5.4) |
| 개별 삭제 | `removeFolder(name)` → false면 *"삭제하지 못했습니다."* (성공 오인 금지) |
| 전체 삭제 | **인라인 2단 확인**([전체 삭제] → *"정말 삭제할까요?"* [예]/[아니오]). `confirmDelete` 모달은 Step 15가 프레임 삭제용으로 소유하므로 **끌어다 쓰지 않는다** |
| 부분 실패 | `{ removed, failed }`를 **정직하게** 안내(*"N개를 삭제했고 M개는 실패했습니다."*) |
| 취소 | 화면 이탈 시 진행 중 조회를 버린다(`cancelled` 플래그) — 언마운트 후 setState 금지 |
| ②(사용자 폴더) | **정리 대상이 아니다** — 목록·삭제 모두 OPFS `results/`만 다룬다(05 §5.4) |

### 4.7 서버 연결 상태 (읽기 전용)

`healthService.probe()`(F17)를 섹션 마운트 시 1회 + [다시 확인] 버튼으로 호출.
표시: 구성 여부 / 주소(`env.backendBaseUrl`) / 버킷 / **게이트 키 "설정됨/미설정"만**(값 절대 미표시 — analysis/41 §2.5) / 도달 여부 / `deployedAt`.
⚠️ *"구성됨"은 "도달 성공"이 아니다* — 두 줄로 나눠 표시한다. `AbortController`로 언마운트 시 취소한다.

### 4.8 설정 내보내기 / 가져오기 (WD17 · 05 §2.5)

| 항목 | 구현 |
|------|------|
| 내보내기 | `settingsRepo.exportJson()` → `Blob` → `exportBlob(blob, "mcphoto-settings-{YYMMDD_HHMM}.json")`(F18). `BackendApiKey`는 애초에 없다 |
| 가져오기 | `<input type="file" accept="application/json">` → `text()` → **`parseSettingsFile`(도메인 순수)** → clamp → **변경 예정 키 미리보기 → [적용]**. 즉시 덮어쓰지 않는다 |
| 거부 | `schemaVersion`이 더 높으면 *"더 새 버전의 설정 파일입니다."* |
| 내구성 | 손상 값·알 수 없는 키는 무시하고 계속(예외 금지) |
| 게스트 | 가져오기 [적용]도 `buildSavePatch` → `save(..., {isGuest})`를 지나므로 **제한 키가 자동 제외**된다 |

---

## 5. 파일별 역할과 시그니처

### 5.1 도메인 (순수 · 브라우저 API 0건 · node 테스트)

#### `src/domain/auth/pinGatePolicy.ts` (신규)

> ⚠️ `domain/index.ts` 배럴에 **넣지 않는다** — `domain/auth/*` 4파일이 이미 배럴 밖이다(F19). 짧은 이름 충돌도 피한다.

```ts
export const PIN_LENGTH = 4;
export const MAX_PIN_FAILS = 5;
export const PIN_COOLDOWN_MS = 1_500;
export const PIN_LOCK_MS = 5 * 60 * 1_000;

export function isPinFormatValid(value: string): boolean;          // ^\d{4}$
export function pinInputsMatch(first: string, second: string): boolean;

/** 어댑터가 응답을 이 판별 유니온으로 접어 넘긴다(도메인은 HTTP를 모른다). */
export type PinCallOutcome =
  | { readonly kind: "ok" }
  | { readonly kind: "status"; readonly status: number }
  | { readonly kind: "network" };

export type PinVerifyClass = "granted" | "mismatch" | "unset" | "unavailable";
export function classifyPinVerify(outcome: PinCallOutcome): PinVerifyClass;

export type PinSetClass = "granted" | "mismatch" | "alreadySet" | "invalid" | "unavailable";
/** `sentCurrentPin=false`(최초 설정)에서의 401은 **불일치가 아니라 `alreadySet`** 이다(06 §2.0). */
export function classifyPinSet(outcome: PinCallOutcome, sentCurrentPin: boolean): PinSetClass;

export interface PinAttemptState {
  readonly fails: number;
  readonly cooldownUntilMs: number;
  readonly lockedUntilMs: number;
}
export function initialPinAttemptState(): PinAttemptState;
export function applyPinFailure(
  state: PinAttemptState, nowMs: number,
): { readonly state: PinAttemptState; readonly exhausted: boolean };
export function isPinInputBlocked(state: PinAttemptState, nowMs: number): boolean;
export function pinLockRemainingMs(lockedUntilMs: number, nowMs: number): number;
export function formatPinLockRemaining(remainingMs: number): string;   // "4분 32초" / "45초"

export interface PinLockRecord { readonly until: number; readonly fails: number }
/** 손상·만료는 null. `until - now > PIN_LOCK_MS`면 상한으로 clamp(A3 — 시계 왜곡 방어). */
export function parsePinLockRecord(raw: unknown, nowMs: number): PinLockRecord | null;
export function buildPinLockRecord(nowMs: number, fails: number): PinLockRecord;
```

#### `src/domain/settings/settingsEditPolicy.ts` (신규 · 배럴 등재)

```ts
/** UI가 렌더하지 않는 키. 값은 보존된다(WD7·WD8). */
export const SETTINGS_HIDDEN_KEYS: readonly (keyof AppSettingsValues)[] =
  ["DisplayMode", "WindowBounds", "ExternalCameraEnabled", "PhotoPrinterEnabled"];

/** TempUser 한도 초과 시 추가 차단(03 §12.1의 QR 전송 하위 묶음). */
export const QR_RELATED_KEYS: readonly (keyof AppSettingsValues)[] =
  ["EnableQrDelivery", "SendPhoto", "SendTimelapse", "RetentionHours"];

export interface SettingsEditContext { readonly isGuest: boolean; readonly qrBlocked: boolean }

export function isSettingEditable(key: keyof AppSettingsValues, ctx: SettingsEditContext): boolean;
export function settingLockReason(key, ctx): "guest" | "qrLimit" | null;
/** 게스트에게 보여줄 값. 제한 boolean 키는 **OFF로 표시**(03 §12.3). 저장에는 절대 쓰지 않는다. */
export function displaySettingValue<K extends keyof AppSettingsValues>(
  key: K, stored: AppSettingsValues[K], ctx: SettingsEditContext,
): AppSettingsValues[K];
/** 저장 패치에서 빼야 하는 키 전부(제한 + 미노출). */
export function omittedSaveKeys(ctx: SettingsEditContext): readonly (keyof AppSettingsValues)[];
```

#### `src/domain/settings/settingsImport.ts` (신규 · 배럴 등재)

```ts
export type SettingsImportResult =
  | { readonly ok: true; readonly values: Partial<AppSettingsValues>;
      readonly webExtras: Partial<WebExtras>; readonly warnings: readonly string[] }
  | { readonly ok: false; readonly reason: "tooNew" | "malformed" };
export function parseSettingsFile(raw: unknown, currentSchemaVersion: number): SettingsImportResult;
```

#### `src/domain/results/byteFormat.ts` (신규 · 배럴 등재)

```ts
/** "1.2 GB" / "340 MB" / "12 KB" / "0 B". 소수 1자리, 1024 기준. */
export function formatBytes(bytes: number): string;
```

### 5.2 어댑터 (브라우저 API 격리 · **예외를 전파하지 않는다**)

#### `src/adapters/storage/pinLockRepo.ts` (신규)

```ts
export const PIN_LOCK_STORAGE_KEY = "mcphoto.pinLock.v1";
export interface PinLockRepo {
  /** 유효한 잠금이면 레코드, 없거나 만료·손상·읽기 실패면 null. */
  read(nowMs: number): PinLockRecord | null;
  /** 실패는 false(잠금 없이 진행 — §3.7). */
  write(record: PinLockRecord): boolean;
  clear(): void;
}
export function createPinLockRepo(storage?: StorageLike): PinLockRepo;   // 기본 = 안전 localStorage
export function getPinLockRepo(): PinLockRepo;
export function setPinLockRepoForTests(repo: PinLockRepo | null): void;
```
`StorageLike`는 `settingsRepo`의 것을 **type-only import**로 재사용한다(런타임 결합 0).

#### `src/adapters/http/accountService.ts` (수정 — **F2 버그 수정**)

```ts
async setMyPin(newPin, currentPin) {
  await client.request<unknown>({
    method: "PUT",
    path: "accounts/me/pin",
    body: currentPin === undefined ? { newPin } : { newPin, currentPin },
    auth: "required",
    // ⚠️ 이 호출의 401은 **PIN 문제**(currentPin 누락·불일치 / 서버에 이미 PIN 있음)이지
    //    세션 만료가 아니다. 기본값(expired)에 맡기면 PIN을 한 번 틀렸을 때 로그아웃된다(E17).
    unauthorized: "reject",
  });
}
```
`resetOtherPin`(Step 16)은 **손대지 않는다** — 그 라우트의 PIN 실패는 403/400이고 401은 진짜 만료뿐이다(F3).

#### `src/adapters/platform/persistStorage.ts` (수정 — 조회 전용 추가)

```ts
/** **요청하지 않고** 현재 상태만 읽는다(설정 화면 표시용 — F16). */
export async function readStorageStatus(manager: StorageManagerLike | undefined): Promise<StorageStatus>;
```
[영속 요청] 버튼만 기존 `requestPersistentStorage`를 부른다(사용자 제스처).

### 5.3 셸

#### `src/shell/pinGate.ts` (신규) — §3.2~§3.4의 API 전부
#### `src/shell/sessionStore.ts` (수정) — `markPinSet()` 추가(§3.6)
#### `src/shell/settingsStore.ts` (수정)

```ts
save(
  patch: Partial<AppSettingsValues>,
  options: { readonly isGuest: boolean; readonly webExtras?: Partial<WebExtras> },
): boolean;
```
- `webExtras`를 같은 트랜잭션에 합친다 → **localStorage 쓰기 1회 · 성공/실패 boolean 1개**(M4 정직성).
- `reEnableQr()`·`saveWebExtras()` **제거**(F6 — 호출자 0, `isGuest` 하드코딩 버그).

### 5.4 화면 로직 (React 무관 · node 테스트)

| 파일 | 핵심 export |
|------|-------------|
| `screens/modals/pinPrompt/pinPromptRunner.ts` | `runPinAttempt(state, pin, deps)` (§3.5) |
| `screens/settings/settingsForm.ts` | `createDraft(values, webExtras)` · `applyQrToggle(draft, key, next)` · `changeSetting(draft, key, value, ctx)` · `buildSavePatch(draft, ctx)` · `saveSettings(deps)` |
| `screens/settings/storedResultsPanel.ts` | `loadStoredResults` · `removeStoredResult` · `removeAllStoredResults` (§4.6) |
| `screens/settings/cameraDevicePanel.ts` | `buildCameraOptions(devices)` · `resolveSelectedDevice(devices, stored)` · `selectCamera(draft, device)` |
| `screens/settings/serverStatusPanel.ts` | `loadServerStatus(deps, signal)` · `describeServerStatus(probe)` |
| `screens/settings/settingsTransfer.ts` | `buildExport(repo, now)` · `previewImport(text, current)` · `applyImport(preview, deps)` |
| `screens/settings/useSettingsScreen.ts` | `useSettingsScreen()` — draft·패널 상태를 묶는 **얇은** 훅 |

### 5.5 UI

| 파일 | 내용 |
|------|------|
| `ui/views/PinGate.tsx` | `<PinGate screen={...}>{children}</PinGate>` — `ensureScreenPinGate` 호출 + status에 따라 children/스피너/무렌더 |
| `ui/views/SettingsView.tsx` | 6섹션 + sticky 저장 바 + 게스트 배너 |
| `ui/views/settings.module.css` | 섹션·행·sticky 바·배지 |
| `ui/components/fields.tsx` | `SettingRow`(label + 설명 + 잠금 배지) · `Toggle` · `ChoiceGroup` · `TextField` · `NumberStepper` — 전부 48px 터치 타깃 + `aria-describedby` |
| `screens/modals/pinPrompt/PinPromptModal.tsx` | §3.8 UI. 입력 버퍼·쿨다운 타이머만 소유 |
| `screens/modals/pinPrompt/pinPrompt.module.css` | 키패드 그리드·인디케이터 |
| `ui/strings.ts` | `pin`·`settings` 절 추가 · `formatCount(template, value: number \| string)` 타입 확장 |

### 5.6 배선

| 파일 | 변경 |
|------|------|
| `src/App.tsx` | `ScreenRouter`: `case "Settings"` → `<PinGate screen="Settings"><SettingsView/></PinGate>`, `case "Account"` → `<PinGate screen="Account"><DummyScreen screen="Account"/></PinGate>`. `ModalStack`: `case "pinPrompt"` → `<PinPromptModal/>`. **`DummyScreen`의 임시 진입점 2개 + `pickLocalSaveFolder` 제거**(F9 — 설정 화면으로 이사) |
| `src/main.tsx` | `installShellHandlers()`에 `installPinGateLifecycle()` 추가 |
| `src/domain/index.ts` | `settings/settingsEditPolicy` · `settings/settingsImport` · `results/byteFormat` 등재(`auth/pinGatePolicy`는 **미등재** — F19) |

---

## 6. 데이터 흐름

### 6.1 설정 진입 (로그인 사용자 · PIN 보유)

```
[상단바 설정] 또는 [OAuth 복귀 returnTo=Settings]
   └─ shellStore.go("Settings")                      ← 동기. 게이트를 태우지 않는다
        └─ ScreenRouter → <PinGate screen="Settings">
             └─ ensureScreenPinGate("Settings")      ← 멱등
                  status = "checking" · 스피너 렌더
                  ensurePinGate({ user, lock, accountAvailable, openPrompt, ... })
                    ├─ lock.read(now) → null
                    ├─ openPinPrompt({ mode: "verify" })
                    │    └─ pushModal("pinPrompt") → <PinPromptModal/> 마운트
                    │         └─ notifyPinPromptMounted()
                    │         └─ [확인] → runPinAttempt(state, "1234", deps)
                    │              └─ accountService.verifyMyPin("1234")   ← unauthorized:"reject"
                    │                   200 → granted
                    │         └─ resolvePinPrompt({ kind: "granted" }) + popModal
                    └─ status = "granted"
             └─ <SettingsView/> 마운트
```

### 6.2 실패 5회 → 잠금

```
401 ×5 → applyPinFailure(state, now) → exhausted
      → lock.write(buildPinLockRecord(now, 5))     # localStorage["mcphoto.pinLock.v1"]
      → resolvePinPrompt({ kind: "exhausted" }) + popModal
      → ensurePinGate → denied("exhausted")
      → toast(error) + shellStore.closeOverlay()   # 직전 화면 복귀
──── 앱을 새로 열어도 ────
      → lock.read(now) !== null → denied("locked") + 남은 시간 안내   # 모달을 열지 않는다
```

### 6.3 최초 설정 (PIN 미보유)

```
hasPin=false → openPinPrompt({ mode: "setup" })
   새 PIN 4자리 → [확인] → mode="setupConfirm"
   재입력 불일치 → "두 번 입력한 PIN이 서로 다릅니다." (**실패 카운트 미가산 · 서버 왕복 없음**)
   일치 → setMyPin(newPin)            ← currentPin 없음(최초 설정)
        204 → sessionStore.markPinSet()  ← 이게 없으면 다음 진입에서 401 데드락(§3.6)
            → granted (재확인 요구하지 않음 — 07 §6.2)
        401 → switchToVerify + "이미 설정된 PIN이 있습니다. 기존 PIN을 입력해 주세요."(A5)
```

### 6.4 생명주기·해제 (누수 방지)

| 자원 | 획득 | 해제 |
|------|------|------|
| `pinPrompt` 모달 | `openPinPrompt` | `resolvePinPrompt`가 **항상** `popModal` 동반(멱등) |
| pending promise | `openPinPrompt` | 성공 · 취소 · 언마운트 · 화면 변경 · **마운트 타임아웃 5초** — 5경로 전부 해제 |
| 쿨다운 타이머 | 불일치 시 `setTimeout` | 컴포넌트 cleanup `clearTimeout` |
| 마운트 감시 타이머 | `openPinPrompt` | `notifyPinPromptMounted` 또는 해제 시 `clearTimeout` |
| `devicechange` 구독 | 설정 화면 effect | `onDeviceChange`의 반환 함수를 cleanup에서 호출 |
| health probe | 섹션 effect | `AbortController.abort()` |
| 보관 결과물 조회 | 패널 effect | `cancelled` 플래그(언마운트 후 setState 금지) |
| 게이트 생명주기 구독 | `installPinGateLifecycle()`(앱 1회) | 반환 해제 함수(테스트용) |
| 내보내기 Object URL | `exportBlob` 내부 | `exportBlob`이 이미 `revokeObjectURL` 처리(F18) |

---

## 7. 설계 이탈 (지시문·WBS·규격과 다른 6가지)

### 이탈 ① PIN 게이트를 `enterSettings()` 같은 **네비게이션 래퍼가 아니라 렌더 게이트**로 만든다
- **왜**: F20 — OAuth 복귀가 `Settings`로 직행하는 경로가 실재한다. 네비게이션 래퍼는 이 경로를 덮지 못한다.
  analysis/61 §7.1의 "판정을 한 곳에 모은다. 흩어지면 한 경로가 게이트를 빼먹는다"를 **구조로** 만족시키는 유일한 방법이다.
- **비용**: 화면 전이가 먼저 일어난다. 단 `SettingsView`가 마운트되지 않으므로 값 노출은 없다.

### 이탈 ② `settingsStore.reEnableQr()`·`saveWebExtras()`를 **제거**한다
- **왜**: 호출자 0곳(F6) + 둘 다 `isGuest: false` 하드코딩. `saveWebExtras`를 설정 화면이 쓰면 게스트가 카메라를 바꿀 때
  운영자의 QR·거울모드 값이 기록될 수 있다. `save(patch, { isGuest, webExtras })` 하나로 합치면 **isGuest 결정이 한 곳**이 되고 저장 왕복도 1회다.
- **대안(리뷰가 반대할 경우)**: 제거 대신 두 함수에 `{ isGuest }`를 필수 인자로 추가. 다만 저장 왕복 2회 문제는 남는다.

### 이탈 ③ `sessionStore`에 **4번째 `currentUser` 진입점 `markPinSet()`** 을 만든다
- **왜**: §3.6 — 없으면 최초 설정 후 다음 진입이 401 데드락이 된다. M1(“null이 되는 것”)에 영향이 없고 AUTH-1에도 걸리지 않는다.
- 02 §5.1:174의 stale한 주석(`login / logout / resetUserForTest`)을 **정확한 4개**로 함께 고친다.

### 이탈 ④ 11-wbs Step 13의 검증 명령에서 **Playwright를 뺀다**
- **왜**: F14 — Playwright가 설치돼 있지 않고 도입은 Step 17이다. 실행할 수 없는 명령을 완료 기준에 두면 "추정 통과"가 생긴다.
- **대체**: E16(5회 실패 → 5분 잠금)·E17(PIN 401이 로그아웃을 유발하지 않음)을 **node 단위 테스트로 등가 보장**하고,
  화면 관측은 **V22 실측**으로 [14 §10.8]에 등재한다(Step 12가 E3·E4에 쓴 방식과 같다).

### 이탈 ⑤ [진단·상태] 버튼·프레임 내보내기/가져오기·앱 업데이트 확인을 **이번 Step에서 만들지 않는다**
- **왜**: §4.1 표 — 각각 Step 16(모달·SW)·Step 15(프레임 저장소)가 선행이다. 지금 버튼만 만들면 운영자에게 스텁 문구가 노출된다.
- WBS Step 13 완료 기록과 15 §6의 Step 15·16 절에 **이월 사실을 명시**한다.

### 이탈 ⑥ [보관된 결과물]의 전체 삭제 확인을 `confirmDelete` 모달이 아니라 **인라인 2단 확인**으로 한다
- **왜**: `confirmDelete` 모달은 Step 15(프레임 삭제 — 03 §15.5의 "서버에서도 제거" 체크박스 포함)가 소유한다.
  지금 선점하면 Step 15가 남의 규격을 물려받은 모달을 재설계해야 한다.

---

## 8. 테스트 전략

### 8.1 단위 테스트 (신규 9파일 · `tests/unit/settings/`)

| 파일 | 고정하는 것 |
|------|-------------|
| `pinGatePolicy.test.ts` | 형식(`^\d{4}$` 경계: `"123"`·`"12345"`·`"12a4"`·전각숫자) · `classifyPinVerify`(200/401/409/500/네트워크) · `classifyPinSet`(401 × `sentCurrentPin` 두 축) · `applyPinFailure` 1~5회(5회째만 `exhausted`) · 쿨다운 경계(`now === cooldownUntil`은 **해제**) · 잠금 파싱(정상·만료·손상·문자열·`until` 미래 clamp) · `formatPinLockRemaining` |
| `pinLockRepo.test.ts` | 왕복 · 만료 자동 무효 · 손상 JSON → null · `setItem`이 던지는 저장소 → `false`(**throw 금지**) · `getItem`이 던져도 `null` |
| `pinPromptRunner.test.ts` | verify 200→granted / 401→retry+쿨다운 / 401×5→exhausted+**`lock.write` 1회** / 409→switchToSetup(**카운트 미가산**) / 네트워크→unavailable(**카운트 미가산**) · setup 204→granted+**`markPinSet` 호출** / setup 401→switchToVerify |
| `pinGate.test.ts` | 게스트→notRequired(**`openPrompt` 0회**) · 잠금 중→denied+토스트(**모달 0회**) · `accountAvailable=false`→denied(fail-closed) · `openPrompt`가 reject해도 denied · `hasPin`에 따른 모드 선택 · `ensureScreenPinGate` **2회 호출 시 게이트 1회**(StrictMode) · 화면 변경 시 승인 폐기(**"매번"**) · `currentUser` 변경 시 폐기 + 모달 취소 · 마운트 타임아웃 → unavailable |
| `settingsForm.test.ts` | **게스트 draft를 강제 변조해도 `buildSavePatch`에 제한 11키가 없다** · 미노출 4키가 패치에 없고 저장 후에도 값이 보존된다 · QR 재활성(off→on ⇒ 하위 2개 on) · QR 정규화가 저장 후 재반영으로 화면에 보인다 · 저장 실패(`save`→false) 시 **실패 토스트 + draft 유지**(M4) · TempUser `qrBlocked`에서 QR 4키만 잠기고 나머지는 편집 가능 |
| `settingsEditPolicy.test.ts` | `GUEST_LOCKED_KEYS` 11개 전부가 게스트에서 편집 불가 · 비게스트는 전부 가능 · `displaySettingValue`가 게스트 제한 boolean만 false로 접는다 · `omittedSaveKeys` 합집합 |
| `storedResultsPanel.test.ts` | 목록·총량·정렬(오래된 순) · 삭제 실패는 개수에 안 센다 · 전체 삭제 부분 실패 `{removed, failed}` · `resultsStore`가 빈 값을 줘도 크래시 없음 · `formatBytes` 경계(0·1023·1024·GB) |
| `settingsTransfer.test.ts` | 내보내기 파일명 형식 · `BackendApiKey` 0건 · `parseSettingsFile`(정상·`schemaVersion` 상위 거부·손상·알 수 없는 키 보존) · 게스트 [적용]이 제한 키를 쓰지 않는다 |
| `settingsInvariants.test.ts` | §8.2 정적 불변식 |

`cameraDevicePanel`·`serverStatusPanel`은 위 파일들에 절로 흡수하거나 필요 시 별 파일로 나눈다(라벨 폴백·매칭 순서·probe 취소).

### 8.2 정적 불변식 (15 §3.4 관례 — 소스를 읽어 검사)

| # | 불변식 | 깨지면 무슨 일이 |
|---|--------|------------------|
| **PIN-1** | PIN 관련 6파일(`domain/auth/pinGatePolicy.ts`·`adapters/storage/pinLockRepo.ts`·`shell/pinGate.ts`·`screens/modals/pinPrompt/*`·`screens/settings/settingsForm.ts`)의 `logger.*` 컨텍스트에 `pin`·`newPin`·`currentPin`·`code`·`state`·`nonce`·`token` 키 **0건** | 마스킹돼 진단이 무용해지거나, 이름을 바꿔 우회하면 **PIN이 실제로 샌다** |
| **PIN-2** | `accountService.ts`의 `verifyMyPin`·`setMyPin` **둘 다** `unauthorized: "reject"` 를 포함한다 | PIN 1회 오입력·`currentPin` 불일치가 **로그아웃**을 유발한다(E17) |
| **PIN-3** | `PIN_LOCK_STORAGE_KEY`(`mcphoto.pinLock.v1`) 문자열이 `src/` 전체에서 `pinLockRepo.ts` **한 파일**에만 나온다 | 잠금 레코드를 두 곳이 쓰면 형식이 갈라져 잠금이 조용히 무력화된다 |
| **PIN-4** | `src/` 전체에서 `pinPrompt` 모달을 `pushModal` 하는 코드가 `shell/pinGate.ts` **한 곳**뿐이다 | 게이트를 우회해 모달만 띄우는 경로가 생긴다 |
| **SET-1** | `ui/views/SettingsView.tsx`·`screens/settings/settingsForm.ts`에 `clampSettings(`·`closestFrom(`·`normalizeQrToggles(` 호출 **0건** | 화면이 도메인을 우회해 clamp하면 Windows와 값이 갈라진다(진실원 = analysis/41 §2) |
| **SET-2** | `GUEST_LOCKED_KEYS`의 모든 키가 `SettingsView`가 참조하는 잠금 표에 존재한다(런타임 단언) | 새 제한 키가 생겼을 때 렌더 가드만 빠진다 |
| **SET-3** | `App.tsx`에 `로컬 저장 폴더 선택`·`카메라 테스트 열기`·`pickLocalSaveFolder` **0건** | Step 10·6의 임시 진입점이 남아 진입로가 둘이 된다 |
| **SET-4** | `shell/settingsStore.ts`에 `isGuest: false` 리터럴 **0건**(호출자가 항상 판정해 넘긴다) | F6 같은 하드코딩이 재발해 게스트가 운영자 값을 덮는다 |

> 각 불변식은 **일시 변형으로 실패를 확인**한 뒤 되돌린다(Step 12가 4건에 대해 한 방식).

### 8.3 E2E (Step 17로 이월 — 시나리오만 확정)

| # | 시나리오 | 대체 보장(이번 Step) |
|---|----------|----------------------|
| E16 | 5회 실패 → 모달 닫힘 + **재시작 후에도 5분 차단** | `pinGatePolicy.test.ts` + `pinLockRepo.test.ts` + `pinGate.test.ts`(잠금 중 모달 0회) |
| E17 | PIN 1회 오입력이 로그아웃을 유발하지 않음 | 정적 PIN-2 + 기존 `tests/unit/auth/sessionExpiry.test.ts`의 PIN 절 |
| E23 | 게스트 저장 후 운영자 값 보존 | `settingsForm.test.ts` + `settingsRepo.test.ts`(기존 `omitKeys`) |

### 8.4 `docs/spec-vectors/`를 만들지 않는 이유

PIN 게이트·설정 화면의 신규 순수 로직은 **웹 전용 강화(WD16 기기 잠금)** 이거나 **웹 전용 UI 판정**이라 Windows에 대응 구현이 없다.
값이 갈라질 대상이 없는데 벡터를 만들면 "웹 구현으로 기대값을 덮어쓰는" 반(反)패턴이 된다(15 §3.3).
**설정 clamp·QR 정규화는 이미 `settings.json`·`qr-delivery.json` 벡터가 고정**하고 있고 이번 Step은 그 함수를 **호출만** 한다.
→ **`docs/spec-vectors/`는 무변경**이며, 따라서 `dotnet test`·`web/functions` 재실행 의무도 없다(서버·WPF 코드 무변경).

---

## 9. 구현 단계 (WBS 블루프린트)

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 단계는 **그 단계만 읽고 실행 가능**해야 한다.
> **검증된 사실** = §0.1(F1~F22, 근거 file:line). **미검증 가정** = §0.2(A1~A6, 검증 단계 매핑됨).
> 공통 전제: `cd E:\Study\photobooth\webclient`. baseline = **45파일 1051 통과**(F15). **git commit/push 금지.**

### Step 13-1: PIN 도메인 정책 (순수)
- **Context Brief**: PIN 형식·응답 분류·실패 상태머신·기기 잠금 레코드를 **순수 함수**로 만든다. 서버 계약은 `^\d{4}$`, verify는 200/401/409, PUT은 204/401/400이다(F3). 도메인은 `Date.now`·`localStorage`를 부를 수 없다(F22) — 시각은 인자로 받는다.
- **대상 파일**: `src/domain/auth/pinGatePolicy.ts`(신규) · `tests/unit/settings/pinGatePolicy.test.ts`(신규)
- **선행 조건**: 없음.
- **구현 내용**: §5.1의 시그니처 전부. `parsePinLockRecord`는 손상·만료를 `null`로 접고 `until - now > PIN_LOCK_MS`면 상한으로 clamp한다(A3). `classifyPinSet`은 `sentCurrentPin=false`의 401을 **`alreadySet`** 으로 분류한다(06 §2.0). ⚠️ `domain/index.ts`에 **등재하지 않는다**(F19).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/settings/pinGatePolicy.test.ts tests/unit/domain/purity.test.ts`
- **완료 기준**:
  - [관측] 신규 테스트 green이고 `purity.test.ts`가 새 파일을 포함한 채 green이다(도메인 순수성 유지).
  - [non-goal] 기존 1051건 무변경. `domain/index.ts` 무변경.
  - [trigger] 함수 호출 시에만 판정. 파일 로드 부작용 0.
- **롤백**: 신규 2파일 삭제.
- [ ] 완료

### Step 13-2: PIN 잠금 저장소 + `accountService` 401 수정
- **Context Brief**: `localStorage["mcphoto.pinLock.v1"]`에 기기 단위 5분 잠금을 둔다(WD16). 동시에 **F2 버그**를 고친다 — `setMyPin`에 `unauthorized: "reject"` 가 없어 `currentPin` 불일치 401이 로그아웃을 일으킨다(E17의 PUT 판). 어댑터는 **예외를 전파하지 않는다**(15 §2).
- **대상 파일**: `src/adapters/storage/pinLockRepo.ts`(신규) · `src/adapters/http/accountService.ts`(수정) · `tests/unit/settings/pinLockRepo.test.ts`(신규)
- **선행 조건**: Step 13-1.
- **구현 내용**: §5.2. `createPinLockRepo(storage?)`는 테스트가 가짜 저장소를 주입할 수 있게 한다(`oauthStateStore`·`settingsRepo` 선례). `setItem`/`getItem`이 던져도 `false`/`null`로 접는다. `setMyPin`에 `unauthorized: "reject"` + 사유 주석 추가. `resetOtherPin`은 **손대지 않는다**.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/settings tests/unit/auth`
- **완료 기준**:
  - [관측] `pinLockRepo.test.ts` green(만료·손상·throw 저장소 포함). 기존 `tests/unit/auth/*` 전부 green.
  - [non-goal] `verifyMyPin`·`list`·`deleteAccount`·`setRole`·`resetOtherPin`의 요청 형태 무변경. `AUTH_FILES` 목록 무변경(`pinLockRepo`를 넣지 않는다).
  - [trigger] 잠금 기록은 **5회 연속 불일치에만** 쓴다.
- **롤백**: 신규 2파일 삭제 + `accountService.ts` 1줄 revert.
- [ ] 완료

### Step 13-3: PIN 시도 러너 (React 무관)
- **Context Brief**: PIN 제출 1회의 **전 경로**를 React 밖에서 구현한다(F13 — jsdom이 없어 컴포넌트에 넣으면 검증 불가). 401/409의 의미가 모드마다 다르다(§3.5 표). 네트워크·기타 오류는 **실패 카운트에 세지 않고 게이트도 열지 않는다**.
- **대상 파일**: `src/screens/modals/pinPrompt/pinPromptRunner.ts`(신규) · `src/shell/sessionStore.ts`(수정 — `markPinSet()`) · `tests/unit/settings/pinPromptRunner.test.ts`(신규)
- **선행 조건**: Step 13-1, Step 13-2.
- **구현 내용**: §3.5·§3.6. `markPinSet()`은 멱등이고 `currentUser`를 null로 만들지 않는다. `BackendError`/`NetworkError`/`NotAuthenticatedError`를 `PinCallOutcome`으로 접어 도메인 분류기에 넘긴다. ⚠️ 로그 컨텍스트는 `gateMode`·`failCount`·`attemptOutcome`·`errorStatus`만 쓴다(F11).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/settings tests/unit/shell tests/unit/auth`
- **완료 기준**:
  - [관측] §8.1 `pinPromptRunner.test.ts`의 8경로 전부 green. `markPinSet` 호출이 setup 204에서만 일어난다.
  - [non-goal] `login`/`logout`/`expireSession` 동작 무변경. AUTH-1 정적 검사 green(`.login(` 호출부 1곳 유지).
  - [trigger] 서버 왕복은 [확인] 제출당 정확히 1회.
- **롤백**: 신규 2파일 삭제 + `sessionStore.ts`의 `markPinSet` 제거.
- [ ] 완료

### Step 13-4: 게이트 판정 + 모달 채널 + 생명주기 (셸)
- **Context Brief**: 07 §6.2 의사코드를 `ensurePinGate`로 옮기고, `pushModal`이 결과를 돌려주지 않는 문제(F21)를 **1개짜리 pending 채널**로 푼다. **어떤 실패도 게이트를 열지 않는다.** StrictMode 이중 effect가 게이트를 취소하지 않도록 상태를 React 밖 스토어에 둔다(§3.3).
- **대상 파일**: `src/shell/pinGate.ts`(신규) · `src/main.tsx`(수정 — `installPinGateLifecycle()`) · `tests/unit/settings/pinGate.test.ts`(신규)
- **선행 조건**: Step 13-3.
- **구현 내용**: §3.2~§3.4 전부. `resolvePinPrompt`는 **멱등**이고 항상 `popModal("pinPrompt")`를 동반한다. 마운트 감시 5초 → `unavailable`. `installPinGateLifecycle`은 `shellStore.screen`·`sessionStore.currentUser` 변경 시 승인을 폐기하고 열린 모달을 `cancelled`로 닫는다. denied 시 셸이 `toast` + `closeOverlay()`를 수행한다.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/settings tests/unit/shell`
- **완료 기준**:
  - [관측] §8.1 `pinGate.test.ts`의 9케이스 green — 특히 **게스트는 모달 0회**, **잠금 중 모달 0회**, **`accountAvailable=false`는 denied**, **`ensureScreenPinGate` 2회 호출에 게이트 1회**, **화면 변경 시 승인 폐기**.
  - [non-goal] 기존 모달(`cameraTest`·`idleWarning`) 동작 무변경. `shellStore.go`·`canTransition` 무변경.
  - [trigger] 게이트는 `ensureScreenPinGate(screen)`이 불릴 때만. 게스트에게는 어떤 경우에도 모달이 뜨지 않는다.
- **롤백**: 신규 2파일 삭제 + `main.tsx` 1줄 revert.
- [ ] 완료

### Step 13-5: PIN 모달 UI + 게이트 래퍼 (화면)
- **Context Brief**: 03 §15.3·07 §6.4의 모달을 만든다. **온스크린 키패드**(키오스크에 물리 키보드가 없다) + 4칸 마스킹 + 1.5초 쿨다운 + `aria-live` 안내. 컴포넌트는 **입력 버퍼와 타이머만** 소유하고 판정은 전부 Step 13-3·13-4가 한다.
- **대상 파일**: `src/screens/modals/pinPrompt/PinPromptModal.tsx`(신규) · `.../pinPrompt.module.css`(신규) · `src/ui/views/PinGate.tsx`(신규) · `src/ui/strings.ts`(수정 — `pin` 절 + `formatCount` 타입 확장) · `src/App.tsx`(수정 — `ModalStack`에 `pinPrompt`, `ScreenRouter`의 `Settings`·`Account`를 `<PinGate>`로 감쌈)
- **선행 조건**: Step 13-4.
- **구현 내용**: §3.8. `notifyPinPromptMounted()`를 마운트 effect 첫 줄에서 호출. 언마운트 cleanup에서 `resolvePinPrompt({kind:"cancelled"})`(멱등). 4자리가 차도 **자동 제출하지 않는다**. 물리 키보드 0~9·Backspace·Enter 지원. 입력 버퍼는 제출 후 즉시 비운다. **`Settings`는 아직 더미 화면이어도 무방하다**(Step 13-7이 교체).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run` · `npx vite build`
- **완료 기준**:
  - [관측] 빌드·타입체크·전체 테스트 green. 게이트를 통과하지 못하면 `Settings`/`Account`의 내용이 **렌더되지 않는다**(코드 경로로 확인 — 실제 화면 관측은 V22).
  - [non-goal] 게스트 진입은 모달 없이 즉시 렌더된다. `Esc`가 유휴 경고를 닫지 않는다(기존 `dismissible:false` 유지). 촬영 흐름 화면 무변경.
  - [trigger] 모달은 `pinGate`가 열 때만 나타난다(정적 PIN-4). 쿨다운 중에는 모든 키가 비활성.
- **롤백**: 신규 3파일 삭제 + `App.tsx`·`strings.ts` revert(설정은 다시 더미).
- [ ] 완료

### Step 13-6: 설정 도메인 정책 + 스토어 정리
- **Context Brief**: 편집 가능 판정·미노출 키·가져오기 파싱·용량 표기를 **순수 함수**로 만들고, `settingsStore`의 `isGuest` 하드코딩 죽은 코드 2개(F6)를 정리한다. **설정 키·기본값·범위·clamp는 `analysis/41 §2`가 진실원이고 이미 도메인에 있다(F7) — 다시 만들지 않는다.**
- **대상 파일**: `src/domain/settings/settingsEditPolicy.ts`(신규) · `src/domain/settings/settingsImport.ts`(신규) · `src/domain/results/byteFormat.ts`(신규) · `src/domain/index.ts`(수정 — 3개 등재) · `src/shell/settingsStore.ts`(수정) · `tests/unit/settings/settingsEditPolicy.test.ts`(신규)
- **선행 조건**: 없음(13-1~5와 독립 — 병렬 가능).
- **구현 내용**: §5.1·§5.3. `save(patch, { isGuest, webExtras? })`로 시그니처 확장(선택 필드라 기존 호출부 호환 — A4). `reEnableQr`·`saveWebExtras` 제거. `settingsEditPolicy`는 `GUEST_LOCKED_KEYS`(기존 상수)를 **재사용**하고 다시 정의하지 않는다.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] 전체 테스트 green(기존 `bootstrap.test.ts:179`의 `save({CountdownSec:10},{isGuest:false})` 포함 — A4 검증). `settingsEditPolicy.test.ts`가 게스트 제한 11키 전수를 확인한다.
  - [non-goal] `settingsRepo`·`clampSettings`·`normalizeQrToggles` **무변경**. `docs/spec-vectors/` 무변경.
  - [trigger] 판정 함수는 호출 시에만 동작. 저장은 `save()` 호출 시에만.
- **롤백**: 신규 4파일 삭제 + `settingsStore.ts`·`domain/index.ts` revert.
- [ ] 완료

### Step 13-7: 설정 화면 로직 (React 무관 5모듈)
- **Context Brief**: 설정 화면의 **판정·조립 전부**를 React 밖에 만든다. 저장 절차는 03 §12.4의 **순서가 규격**이다(저장 → **재반영** → 즉시 적용 → 정직한 토스트). 게스트 제한은 렌더 가드 외에 **패치에서 키를 빼는** 것이 본체다.
- **대상 파일**: `src/screens/settings/{settingsForm,storedResultsPanel,cameraDevicePanel,serverStatusPanel,settingsTransfer}.ts`(신규 5) · `tests/unit/settings/{settingsForm,storedResultsPanel,settingsTransfer}.test.ts`(신규 3) · `src/adapters/platform/persistStorage.ts`(수정 — `readStorageStatus`)
- **선행 조건**: Step 13-6.
- **구현 내용**: §4.3·§4.4·§4.5·§4.6·§4.7·§4.8, §5.4. **[보관된 결과물]은 `resultsStore`(F8) 위에 얹기만 한다** — 새 저장소 코드 금지. `serverStatusPanel`은 `AbortSignal`을 받는다. `settingsTransfer`는 `settingsRepo.exportJson()`·`exportBlob`(F18)을 재사용한다.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/settings tests/unit/storage`
- **완료 기준**:
  - [관측] §8.1의 3파일 green — 특히 **게스트 draft 변조 후에도 `buildSavePatch`에 제한 11키 0건**, **미노출 4키가 저장 왕복 후 보존**, **저장 실패 시 실패 토스트**.
  - [non-goal] `resultsStore`·`resultSaver`·`dirHandleRepo` **무변경**. OPFS 직접 접근 0건(VF-14).
  - [trigger] 서버 probe는 섹션 마운트·[다시 확인]에만. 결과물 삭제는 확인 후에만.
- **롤백**: 신규 8파일 삭제 + `persistStorage.ts` revert.
- [ ] 완료

### Step 13-8: 설정 화면 UI + 임시 진입점 이사
- **Context Brief**: 6섹션 + **하단 sticky 저장 바**를 렌더하고, `App.tsx`의 임시 진입점 2개(F9 — [카메라 테스트 열기]·[로컬 저장 폴더 선택])를 설정 화면으로 **옮기고 원본을 제거**한다. 컨트롤은 48px 터치 타깃 + 잠금 시 `disabled` + "로그인 필요" 배지.
- **대상 파일**: `src/ui/views/SettingsView.tsx`(신규) · `src/ui/views/settings.module.css`(신규) · `src/ui/components/fields.tsx`(신규) · `src/screens/settings/useSettingsScreen.ts`(신규) · `src/ui/strings.ts`(수정 — `settings` 절) · `src/App.tsx`(수정 — `SettingsView` 라우팅 + `DummyScreen` 임시 진입점·`pickLocalSaveFolder` 제거)
- **선행 조건**: Step 13-5, Step 13-7.
- **구현 내용**: §4.1의 6섹션. 각 변경 핸들러 첫 줄에 `isSettingEditable` 가드(M10 2중). 저장/닫기 바 `position: sticky; bottom: 0`. `devicechange` 구독 해제·probe abort·패널 취소 플래그를 **cleanup에서 전부** 건다(§6.4). ⚠️ [진단·상태]·프레임 내보내기·앱 업데이트 확인은 **만들지 않는다**(§7 이탈 ⑤ — 코드 주석으로 이월 Step 명시).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run` · `npx vite build`
- **완료 기준**:
  - [관측] 빌드 green. 게스트로 진입하면 제한 11항목이 **OFF·비활성 + "로그인 필요"** 이고, 로그인 사용자는 전부 편집 가능하다(코드 경로 + Step 13-9 정적 검사. 화면 관측은 V22).
  - [non-goal] `App.tsx`에 임시 진입점 문자열 0건(정적 SET-3). 촬영 흐름 화면·상단바·유휴 경고 무변경. jsdom·@testing-library **도입 없음**(F13).
  - [trigger] 저장은 [저장] 탭에만(자동 저장 없음). 카메라 테스트 모달은 [카메라 테스트] 탭에만. 폴더 선택은 사용자 제스처에만(`showDirectoryPicker` 제약).
- **롤백**: 신규 4파일 삭제 + `App.tsx`·`strings.ts` revert(더미 화면 + 임시 진입점 복구).
- [ ] 완료

### Step 13-9: 정적 불변식 + 문서 갱신 + 전체 검증
- **Context Brief**: 문서에만 있으면 깨지는 규칙 8건을 소스 검사로 고정하고(15 §3.4), WBS·관례·실측 문서를 갱신한다. **다른 Step 서술을 stale로 만들지 않는다.**
- **대상 파일**: `tests/unit/settings/settingsInvariants.test.ts`(신규) · `docs/web-client/11-wbs.md` · `docs/web-client/15-implementation-conventions.md` · `docs/web-client/14-handoff-and-user-actions.md` · `docs/web-client/02-app-shell-and-navigation.md`(:174 stale 정정) · `docs/design/README.md`(§3.1 등재)
- **선행 조건**: Step 13-1 ~ 13-8.
- **구현 내용**: §8.2의 PIN-1~4·SET-1~4. **각 불변식을 일시 변형해 실패를 확인한 뒤 되돌린다.** 문서: 11-wbs Step 13에 산출물·실측 수치·설계 이탈 6건·이월 3건·미검증 V22 기록(Step 12 형식) + **검증 명령에서 Playwright 제거**(이탈 ④). 15 §6에 Step 13 완료 절 추가 + §7 상태표 갱신(Step 14·15·16 서술은 **건드리지 않는다**). 14 §10.8에 **V22** 신설 + §10.7의 "E17 화면 관측은 Step 13 이후" 항목 해소.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run` · `npx vite build`
- **완료 기준**:
  - [관측] 전체 테스트 green이고 **1051보다 늘어난 수치를 실측으로 기록**한다. 불변식 8건 전부 "일시 변형 → 실패 확인 → 복구"를 마쳤다.
  - [non-goal] `docs/spec-vectors/` 무변경 → `dotnet test`·`web/functions npm test` **재실행 불요**(§8.4). 15 §6의 Step 9~12·14~16 서술 무변경.
  - [trigger] 문서 갱신은 이 단계에서만.
- **롤백**: 신규 1파일 삭제 + 문서 revert.
- [ ] 완료

### 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실 / 미검증 가정 분리 (§0.1 F1~F22 · §0.2 A1~A6)
- [x] 모든 가정에 검증 단계 매핑 (A1→13-3·13-4 / A2→13-4 / A3→13-1 / A4→13-6 / A5→13-3+V22 / A6→V22)
- [x] 9단계 전부 7필드(Context Brief · 대상 파일 · 선행 조건 · 구현 내용 · 검증 명령 · 완료 기준 · 롤백) 충족
- [x] 완료 기준이 전부 관측 3문(관측/non-goal/trigger) — UI 단계(13-5·13-8)에 non-goal·trigger 포함
- [x] 검증 명령이 전부 자동 실행 가능 (`npx tsc --noEmit` / `npx vitest run` / `npx vite build`) — **Playwright 의존 제거**(이탈 ④)
- [x] 각 단계가 self-contained (fresh 에이전트가 그 절만 읽고 실행 가능)
- [x] 브라우저·실계정이 필요한 항목은 **V22로 분리**했고 추정 통과가 없다 (§10)
- [x] 미해결 오픈이슈 없음 — §11의 2건은 **설계 판단으로 확정**했고 리뷰 반대 시의 대안까지 적었다

---

## 10. 남는 사용자 액션 (실측 · `14 §10.8`로 등재 예정 — **V22**)

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V22-1 | 로그인 사용자가 [설정]을 누르면 **매번** PIN 모달 | 모달 → 통과 → 설정 렌더. 닫았다 다시 열면 **또** 묻는다 | 실계정 로그인 + 브라우저 |
| V22-2 | 게스트가 [설정]을 누르면 **모달 없이** 즉시 진입 | 네트워크 탭에 PIN 요청 0건 | 브라우저 |
| V22-3 | **PIN 1회 오입력이 로그아웃을 유발하지 않는다**(E17 — Step 12에서 이월된 화면 관측) | 상단 계정 라벨 유지 + "PIN이 일치하지 않습니다." | 실계정 + 화면 |
| V22-4 | PIN 미설정 계정의 **최초 설정 플로우**(409/`hasPin=false`) → 설정 후 **재확인 없이 진입** → 다음 진입은 verify 모드(A5) | 2회차에 401 데드락이 나지 않는다 | PIN 없는 실계정 필요 |
| V22-5 | **5회 실패 → 모달 닫힘 + 5분 잠금**, **탭을 닫았다 열어도 잠금 유지**(E16) | `localStorage["mcphoto.pinLock.v1"]` 존재 + 안내에 남은 시간 | 브라우저 저장소 관측 |
| V22-6 | 네트워크 끊고 PIN 입력 → *"확인할 수 없습니다. 네트워크를 확인하세요."* + **실패 카운트 미증가** + **진입 차단** | 3회 시도해도 잠기지 않고 통과도 안 된다 | 오프라인 전환 필요 |
| V22-7 | **게스트로 설정 저장 → 로그인 후 확인** 시 운영자 값(거울모드·QR 등) 그대로(E23) | 저장 전후 `localStorage["mcphoto.settings.v1"]` 비교 | 저장소 관측 + 계정 전환 |
| V22-8 | 컷 수 **8** 저장 → `Guide`에 반영 · 컷 수 **자동**(sentinel 0) 저장 왕복 후 소멸하지 않음 | Guide "(자동)" 배지 | 실촬영 흐름 |
| V22-9 | 카메라 장치 선택·[재검색]·[카메라 테스트] · 권한 전 라벨이 "카메라 N" | 장치 전환이 실제로 반영된다 | 실 카메라 2대 |
| V22-10 | [보관된 결과물] 목록·용량·개별/전체 삭제 · 여유 10% 미만 경고 배지 | 삭제 후 목록·총량 감소 | 실제 촬영 결과물 필요 |
| V22-11 | [로컬 저장 폴더 선택]/[해제] · 미지원 브라우저(Safari·Firefox)에서 **버튼 미노출** | Chromium에서만 보인다 | 브라우저 3종 |
| V22-12 | [설정 내보내기] 파일이 열리고 `BackendApiKey`가 **없다** · [가져오기] 미리보기 → [적용] | 상위 `schemaVersion` 거부 문구 | 파일 다운로드·선택 |
| V22-13 | 온스크린 키패드 터치 타깃·`aria-live` 안내(A6) | 스크린리더가 실패 사유를 읽는다 | 실기기·보조기술 |

---

## 11. 설계 판단으로 확정한 2건 (리뷰가 반대하면 되돌릴 수 있게 근거를 남긴다)

| # | 쟁점 | 확정 | 되돌릴 경우 |
|---|------|------|-------------|
| **D1** | `Account`(아직 더미)에도 지금 게이트를 거는가 | **건다.** 07 §6.1 표와 WBS [trigger]가 설정·계정 양쪽을 규정한다. 나중에 붙이면 "한 경로가 게이트를 빼먹는" 실패가 정확히 재현된다 | `App.tsx`에서 `Account`의 `<PinGate>` 래핑 1줄 제거 → Step 16이 추가 |
| **D2** | 설정 내보내기/가져오기를 이번에 넣는가 | **넣는다.** 05 §2.5(WD17)가 규격이고 재료(`exportJson`·`exportBlob`)가 이미 있다(F18). 단 **Step 13-7의 독립 모듈**이라 잘라내기 쉽다 | `settingsTransfer.ts` + 섹션 6의 두 버튼 제거 → Step 16으로 이월 |

---

## 12. 요약

- **게이트는 렌더 시점에 건다.** `<PinGate>`를 통과하지 못하면 `SettingsView`가 마운트되지 않는다 → OAuth 복귀(F20)를 포함한 **모든 진입로**가 자동으로 덮인다.
- **fail-closed가 5경로 전부에서 성립한다**: 잠금 중 · 서비스 불가 · 모달 미마운트(5초) · 취소 · 5회 소진. "확인할 수 없으면 통과시키지 않는다."
- **`accountService`에 PIN 3종은 이미 있다**(F1). 다시 만들지 않는다. 다만 **`setMyPin`에 `unauthorized: "reject"` 가 빠져 있어**(F2) `currentPin` 불일치가 로그아웃을 유발한다 — 이번 Step에서 고치고 **정적 불변식 PIN-2로 고정**한다.
- **`hasPin` 갱신 경로가 없다**(F4). `markPinSet()`을 만들지 않으면 최초 설정 다음 진입이 **401 데드락**이 된다.
- **게스트 제한은 4중**(렌더·액션·패치·저장소)이고, 본체는 "패치에서 키를 빼는 것"이다. `settingsStore`의 죽은 코드 2개(F6)가 `isGuest: false`를 하드코딩하고 있어 정리한다.
- **[보관된 결과물]은 `resultsStore`(F8) 위에 얹기만 한다** — Step 10 이월분이며 새 저장소 코드를 쓰지 않는다.
- **jsdom·Playwright가 없다**(F13·F14). 그래서 판정 로직 전량이 React 밖에 있고, 화면 관측은 **V22 13건**으로 분리해 추정 통과를 만들지 않는다.
