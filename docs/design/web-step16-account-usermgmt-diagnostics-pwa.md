# 웹 클라이언트 Step 16 — 계정 · 사용자 관리 · 진단 · PWA (설계)

| 항목 | 값 |
|------|-----|
| 대상 | 키오스크 웹 클라이언트([`docs/web-client/`](../web-client/README.md)) **WBS Step 16** — 마지막 구현 Step |
| 규격 | [`03 §13·§14·§15.2`](../web-client/03-screens-spec.md) · [`07 §5·§6`](../web-client/07-auth-and-permissions-web.md) · [`01 §6`](../web-client/01-tech-stack-and-structure.md) · [`05 §2.5·§4.6·§7`](../web-client/05-storage-and-persistence.md) |
| 진실원 | [`analysis/60 §1·§2`](../analysis/60-auth-accounts-and-roles.md)(역할 매트릭스) · [`analysis/13 §9.2·§10`](../analysis/13-client-behavior-spec.md) · [`analysis/31 §4.3~§4.9`](../analysis/31-backend-api-reference.md) |
| 작성일 | 2026-08-01 |
| 선행 | Step 12(인증) · Step 13(PIN 게이트·설정) · Step 14·15(프레임) 완료 |
| 비범위 | Playwright E2E(Step 17) · 실기기 실측(V25 — 목록만 신설) |

> 이 문서는 **왜 그렇게 결정했는가**를 남긴다. "무엇이 어떻게 동작하는가"의 진실원은
> `docs/analysis`이고, 그보다 위는 **실제 소스**다([`design/README §4`](./README.md#4-문서-유효성-주의)).
> §3에 이번에 **소스를 따라 문서를 고친 판정 4건**을 남긴다 — Step 15에서 배율 규격(10~300)이
> 두 번 되돌려질 뻔한 전례가 있어, 판정을 코드 주석처럼 남기지 않으면 다음 세션이 되돌린다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (2026-08-01, 코드·문서를 직접 열어 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | **`rolePolicy.ts`라는 파일은 없다.** `isPower`·`canWriteFrames`·`canManage`·`canResetPin`은 전부 `src/domain/roles/userRole.ts`에 있다 | `webclient/src/domain/roles/userRole.ts:64-94`. `grep -rn rolePolicy src` → **0건**(문서 4곳에만 등장) |
| F2 | `assignableRoles(actor, current)`는 `src/domain/roles/roleChangePolicy.ts:23`. 반환 순서는 위계 오름차순 고정 | 동 파일 + `docs/spec-vectors/role-matrix.json`이 `assignableRoles`·`canManage`·`canResetPin` 3축을 교차 고정 |
| F3 | `accountService`에 6메서드가 있다: `list`·`verifyMyPin`·`setMyPin`·`deleteAccount`·`setRole`·`resetOtherPin` | `src/adapters/http/accountService.ts:16-26` |
| F4 | `healthService.probe()`가 **이미 2프로브다**(`GET /health` → `GET /frames/default`의 401 여부). `gateKeyValid`는 3상태(`true`/`false`/`null`) | `src/adapters/http/healthService.ts:44-70` |
| F5 | `GET /frames/default` 프로브는 `auth` 기본값 `"none"`이라 Bearer가 붙지 않는다 → 401이 와도 `unauthorized`가 `"reject"`로 해석되어 **로그아웃되지 않는다** | `backendClient.ts:88-89`, `:157` |
| F6 | `screens/settings/serverStatusPanel.ts`의 `loadServerStatus`·`describeServerStatus`가 이미 "구성됨 ≠ 도달 성공" 2행 분리와 게이트 키 3상태 표기를 구현했다 | 동 파일 `:62-104` |
| F7 | `getTimelapseService().encoderProbe()` → `lastEncoderProbe()`. `EncoderProbe = {path, codec, reason, probed[]}` | `adapters/encode/timelapseService.ts:263`, `encoderSupport.ts:21-29` |
| F8 | `OpfsClient.usage(path)`·`frameStore.usageBytes()`·`resultsStore.usage()`·`logStore.stats()`·`readStorageStatus()` 전부 존재하고 **실패해도 던지지 않는다** | `opfsClient.ts:127`, `frameStore.ts:478`, `resultsStore.ts:32`, `logStore.ts:119`, `persistStorage.ts:71` |
| F9 | `logStore.exportText()`가 `formatLogText`로 `.log` 텍스트를 만든다. `exportBlob(blob, fileName)`은 **던지지 않고 `false`** 를 돌려준다 | `logStore.ts:109`, `platform/fileExport.ts:42-76` |
| F10 | `settingsTransfer.ts`가 설정 내보내기/가져오기의 구조 선례다: **순수 판정 → 미리보기 → [적용]**, `deps` 주입, `defaultXxxDeps()`로 싱글턴을 **호출 시점에** 해석 | `screens/settings/settingsTransfer.ts` 전체 |
| F11 | `runPinAttempt(state, pin, deps)`에 **`currentPin` 슬롯이 이미 있고** 주석에 "Step 16의 PIN 변경이 값을 넣는 자리"라고 적혀 있다 | `screens/modals/pinPrompt/pinPromptRunner.ts:52-56`, `:120` |
| F12 | `ModalId = "cameraTest" \| "diagnostics" \| "pinPrompt" \| "idleWarning"`. `diagnostics`는 아직 `App.tsx`의 스텁 모달로 떨어진다 | `shell/shellStore.ts:32`, `App.tsx:128-145` |
| F13 | `<PinGate screen="Account">` 배선이 **이미 걸려 있고** 안쪽이 `DummyScreen`이다. `UserMgmt`는 감싸져 있지 않다 | `App.tsx:192-197`, `:198-199` |
| F14 | PIN 승인은 **화면이 바뀌면 폐기**된다(`installPinGateLifecycle`의 `state.screen !== previous.screen`) | `shell/pinGate.ts:317-319` |
| F15 | `canTransition`상 `Account → UserMgmt`·`UserMgmt → Account` 둘 다 합법이고, `isOverlayScreen`이 `UserMgmt`를 포함해 **복귀 지점을 덮어쓰지 않는다** | `domain/navigation/stateMachine.ts:22-24`, `:60-62` |
| F16 | `public/manifest.webmanifest`·`public/icons/icon-{192,512,512-maskable}.png`가 **이미 있다**. `index.html`이 manifest를 링크한다 | `webclient/public/`, `index.html:14` |
| F17 | `web/firebase.json`의 kiosk 타깃에 **`/sw.js` no-cache 헤더가 이미 있다** | `web/firebase.json:65-68` |
| F18 | kiosk CSP `connect-src`에 **`blob:`이 없다** | `web/firebase.json:42` |
| F19 | `public/frames/index.json`은 `[]`이고 `public/sounds/`는 **존재하지 않는다**(셔터음은 합성음 폴백) | `webclient/public/`, `platform/shutterSound.ts:16`, `:44-53` |
| F20 | 기존 워커 2개가 `/// <reference lib="webworker" />`로 타입을 얻는다(tsconfig `types`에 webworker 없음) | `opfsWriter.worker.ts:1`, `encode.worker.ts:1` |
| F21 | `tempUserLimitsService.get/update`는 **예외를 던진다**(HTTP 어댑터 계약). 범위 검증은 서버에만 있다 | `adapters/http/tempUserLimitsService.ts:36-57`, `analysis/31 §4.9`(qrHours 1~8760 / qrCount 1~100000) |
| F22 | 앱 어디에도 **로그아웃 진입점이 없다**. `sessionStore.logout()` 호출자 0건 | `grep -rn "logout" src` → 정의·주석뿐 |
| F23 | `STRINGS.kiosk.exit`("키오스크 종료")·`STRINGS.common.apply`("지금 적용")가 이미 카탈로그에 있다(미사용) | `ui/strings.ts:27`, `:358-360` |
| F24 | `domain/frames/slotLayout.ts:181 rescaleSlots(slots, factor, w, h)`가 "원본 이미지 크기 → 현재 프레임 크기" 좌표계 환산의 정본이다 | 동 파일 주석 |
| F25 | 정적 불변식 테스트의 형태(소스를 읽어 정규식 검사 + 주석 제거)는 `tests/unit/settings/settingsInvariants.test.ts`가 선례다. `PIN_FILES` 목록에 새 PIN 파일을 추가하는 것이 관례다 | 동 파일 `:20-53` |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| A1 | `fetch("blob:…")`가 kiosk CSP(`connect-src 'self'`)에서 통과하는지 **모른다**. 브라우저별로 갈린다는 보고가 있다. Step 15의 `fetchFrameImageBytes`가 이미 이 경로를 쓰고 있어 **운영에서만 나는 실패**가 될 수 있다 | **W9** — 이번 Step의 신규 코드는 blob fetch를 쓰지 않고 OPFS를 직접 읽는다(§9.2). 동시에 `connect-src`에 `blob:`을 **예방적으로 추가**하고(§9.2), 실기기 확인은 V25-6 |
| A2 | 별도 rollup 진입으로 만든 `sw.js`(iife)가 kiosk CSP `script-src 'self' 'wasm-unsafe-eval'`에서 등록되는지 | **W8** — `npx vite build && npx vite build --config vite.sw.config.ts` 산출물 확인 + V25-1(오프라인 로드) |
| A3 | Windows 탐색기로 다시 압축한 zip은 **deflate**다. `DecompressionStream("deflate-raw")`가 대상 브라우저에 있는지 | **W10** — 런타임 감지 + 미지원 시 전용 안내 문구(§9.3). 실기기 확인은 V25-4 |
| A4 | `navigator.permissions.query({name:"camera"})`가 대상 브라우저 전부에서 동작하는지(Firefox는 이름 미지원으로 throw) | **W7** — try/catch → `null` → "알 수 없음" 표기. 단위 테스트로 throw 경로 고정 |
| A5 | `clients.claim()` + 사용자 트리거 `skipWaiting()` 조합으로 [지금 적용]이 실제로 새 SW를 활성화하는지 | **W8** — `swUpdate` 단위 테스트(가짜 `ServiceWorkerContainer`) + V25-2 |
| A6 | 프레임 zip을 Windows `Frame\`에 풀었을 때 앱이 인식하는지(`{계정}_{이름}` 접두 규약) | **W9** — 파일명 조립은 순수 함수 + 단위 테스트. 실제 인식은 V25-3(Windows 앱 필요) |
| A7 | 현재 웹 테스트 총계가 **1655건**이라는 값(15 §7 기재) | **W1** — 첫 단계에서 `npx vitest run`을 돌려 기준선을 실측하고, 이후 단계가 증가만 확인한다 |

---

## 1. 이 Step이 푸는 문제 6개

| # | 문제 | 답 |
|---|------|-----|
| P1 | `Account`·`UserMgmt`가 더미다 — 앱에 **로그아웃 경로조차 없다**(F22) | §5·§6 + 계정 메뉴 팝오버 |
| P2 | 역할 게이트가 화면에 흩어지면 서버 매트릭스와 조용히 갈라진다 | §4 — 판정은 **도메인 순수 함수 2개**가 소유하고, 화면은 역할 문자열을 비교하지 않는다(정적 검사 ACC-1) |
| P3 | `canManage`(동급 허용)와 `canResetPin`(동급 차단)의 비대칭이 UI에서 뭉개지기 쉽다 | §4.2 — 행 정책을 **한 함수**가 만들고 `role-matrix.json` 벡터가 이미 고정한다 |
| P4 | 진단이 "구성됨"을 "도달 성공"으로 오독시키거나 **게이트 키 값을 노출**할 수 있다 | §7 — 기존 `serverStatusPanel` 재사용 + 정적 검사 DIAG-1 |
| P5 | SW가 API 응답·서명 URL을 캐시하거나 **촬영 중 앱을 갱신**하면 사고다 | §8 — 순수 라우팅 분류기가 **기본 bypass**, `skipWaiting`은 사용자 트리거 1경로뿐(정적 검사 SW-1·SW-2) |
| P6 | 프레임 zip이 Windows `Frame\`과 호환되지 않으면 내보내기가 무의미하다 | §9 — 파일명·`.slots` 조립을 순수 함수로 고정(기존 `serializeSlotsFile` 재사용) |

---

## 2. 계층 배치 (한눈에)

```
domain/ (순수 · node 테스트 · import 0)
  accounts/accountAdminPolicy.ts      ★신규  sortManagedUsers · buildUserRows · canOpenUserMgmt
                                             · canEditGlobalLimits · canExitKiosk
  accounts/tempUserLimitsPolicy.ts    ★신규  범위 상수 · parseLimitInput · validateTempUserLimits
  accounts/sessionUser.ts             수정   authMethodLabel 문구 정정(§3.1)
  auth/pinGatePolicy.ts               수정   pinGateGroup(screen) 추가(§5.5)
  roles/userRole.ts                   무변경  ← 벡터가 고정한다. 손대지 않는다
  roles/roleChangePolicy.ts           무변경

adapters/ (브라우저 격리 · 예외 미전파)
  storage/zipStore.ts                 ★신규  순수(import 0) store-zip 생성·파싱 + crc32
  storage/exportImport.ts             ★신규  프레임 zip 내보내기/가져오기 · 로그 .log
  storage/frameStore.ts               수정   readImageBytes(frame) 추가(§9.2)
  platform/swPolicy.ts                ★신규  순수(import 0) classifySwRequest · 캐시 판정
  platform/clipboard.ts               ★신규  copyText() — 실패는 false
  platform/appInstall.ts              ★신규  isStandaloneDisplay()

shell/
  swUpdate.ts                         ★신규  등록 · 대기 감지 · [지금 적용] · 상태 스토어
  accountModeIntent.ts                ★신규  계정 화면 진입 모드 인계(**비파괴 읽기**)
  pinGate.ts                          수정   게이트 키를 pinGateGroup으로(§5.5)

screens/ (React 무관 · node 테스트)
  account/accountMenu.ts              ★신규  buildAccountMenuItems(user)
  account/accountInfoRows.ts          ★신규  내 정보 표시 행
  account/pinChangeRunner.ts          ★신규  PIN 변경 1회 제출(runPinAttempt 재사용)
  account/adminLimitsForm.ts          ★신규  전역 한도 draft·검증·저장
  account/kioskExit.ts                ★신규  runKioskExit(deps)
  account/useAccountScreen.ts         ★신규  훅(얇게)
  userMgmt/userListRunner.ts          ★신규  목록 로드 → 판별 유니온
  userMgmt/userActions.ts             ★신규  삭제 · 역할 변경(액션 첫 줄 가드)
  userMgmt/pinResetRunner.ts          ★신규  타 계정 PIN 재설정
  userMgmt/useUserMgmtScreen.ts       ★신규  훅(얇게)
  modals/diagnostics/diagnosticsPresenter.ts ★신규  6섹션 조립(deps 주입)
  modals/diagnostics/DiagnosticsModal.tsx    ★신규
  settings/frameTransfer.ts           ★신규  프레임 내보내기/가져오기 액션 + 권한 가드

ui/
  views/AccountView.tsx · UserMgmtView.tsx           ★신규 (+ 각 .module.css)
  components/PinKeypad.tsx                           ★신규 (PinPromptModal에서 추출)
  components/fields.tsx                              수정  Select 추가
  components/index.tsx                               수정  TopBar에 계정 메뉴 팝오버
  strings.ts                                         수정  account/userMgmt/diagnostics/pwa/transfer

빌드
  src/sw.ts                           ★신규  얇은 래퍼(정책은 swPolicy)
  vite.precache.ts                    ★신규  순수 helper(collectPrecacheAssets · precacheBuildId)
  vite.config.ts                      수정   precache 매니페스트 플러그인
  vite.sw.config.ts                   ★신규  두 번째 rollup 진입(sw.js)
  package.json                        수정   build 스크립트 2단
  web/firebase.json                   수정   connect-src blob: · /precache-manifest.json no-cache
```

**계층 규칙 준수**: 새 도메인 2파일은 `import` 0(도메인 내부 상대 경로만) → `purity.test.ts`가 자동 포함한다.
`zipStore.ts`·`swPolicy.ts`는 **순수하지만 어댑터에 둔다** — `domain/index.ts`가 평면 `export *` 배럴이라
`crc32`·`cacheNameFor` 같은 일반명이 재수출 충돌을 만든다. `adapters/storage/logPolicy.ts`가 같은 선례다.

---

## 3. 진실원 판정 — 소스를 따라 문서를 고친 4건

> ⚠️ **다수결로 판정하지 않았다.** 우선순위는 **실제 소스 > `docs/analysis` > `docs/design`**이다
> ([design/README §4](./README.md#4-문서-유효성-주의)). 아래 판정을 되돌리려면 이 절을 먼저 읽어라.

### 3.1 로그인 방식 라벨: **"Google SSO"** 로 소스를 고친다 (문서가 옳다)

| 출처 | 값 |
|------|-----|
| Windows 소스 `src/MCPhoto.Core/Models/User.cs:43-47` | `AuthMethod.Google => "Google SSO"`, 그 외 `"알 수 없음"` |
| `analysis/13 §10.1`(:551) | `"google"` → **"Google SSO"**, 모르는 값은 "알 수 없음" |
| `analysis/60 §3.2` | `AuthMethod.ToLabel()`로 **"Google SSO"** |
| `web-client/03 §13.1` | `"google"` → **"Google SSO"** |
| **웹 소스** `domain/accounts/sessionUser.ts:50-59` | `"google"` → **"Google 계정"**, `"password"` → `"아이디/비밀번호"` |

**판정: 웹 소스를 고친다.** 근거 3가지.
1. 우선순위 규칙은 **관측 가능한 현행 동작**을 보호하기 위한 것이다. `authMethodLabel`은 **호출자가 0**이고
   `Account`가 더미라 **한 번도 렌더된 적이 없다** — 보호할 현행 동작이 없다.
2. 이것은 **소스 대 소스** 충돌이다(웹 헬퍼 vs Windows `AuthMethodExtensions.ToLabel`). 플랫폼 중립 규격은
   `analysis/13 §14` 문구 카탈로그이고, `01 §8`이 "문구는 카탈로그와 **1:1**"을 규약으로 못박는다.
3. `"password"` 분기는 **it15에서 폐지된 개념**이다(비밀번호 없음). 남겨 두면 서버가 그 값을 보낼 수 있다는
   오해를 만든다.

**조치**: `authMethodLabel`을 `"google" → "Google SSO"`, **그 외 전부 `"알 수 없음"`** 으로 바꾼다.
`"password"` 케이스는 **삭제**한다. 문구는 `STRINGS.account.authMethodGoogle`·`authMethodUnknown`으로 옮긴다
(도메인은 문자열 카탈로그를 갖지 않는 것이 이 저장소 관례가 아니므로 — `roleLabel`이 도메인에 있다 —
**`authMethodLabel`도 도메인에 그대로 두고 값만 고친다**. 카탈로그 중복을 만들지 않는다).

### 3.2 `rolePolicy.ts`는 **없다** — 문서 4곳을 `userRole.ts`로 고친다

`01 §2.2`(:90)·`03 §1.3`(:57)·`10 §5`(:49)·`15 §6`(:285)이 `domain/roles/rolePolicy.ts`를 가리키지만
**그 파일은 존재한 적이 없다**(F1). 실제 위치는 `domain/roles/userRole.ts`이고 테스트도
`tests/unit/domain/settingsAndRoles.test.ts`다(`rolePolicy.test.ts` 아님).

**판정: 소스가 사실.** 문서 4곳의 경로를 고친다. 이름을 소스 쪽에서 바꾸지 않는 이유 —
`userRole.ts`는 `docs/spec-vectors/role-matrix.json`이 교차 고정하는 파일이고, 파일명 변경은
Windows `Models/UserRole.cs`와의 1:1 대응(01 §2.2 매핑표)을 깨뜨린다.

### 3.3 계정 메뉴는 **웹에서 상단바 팝오버로 만든다** (02 §5.1 유지, `App.tsx` 수정)

`02 §5.1`(:150)은 팝오버 3항목(계정 관리 · 관리자 도구 · 로그아웃)을 규정하는데, 현행 `App.tsx:231-233`은
계정 버튼이 곧바로 `go("Account")`다 → **로그아웃 경로가 앱에 존재하지 않는다**(F22).

**판정: 문서가 옳고 소스가 미완이다.** "소스가 사실"은 *구현된 동작이 문서와 다를 때* 적용하는 규칙이지,
*아직 구현하지 않은 요구사항을 지우는* 근거가 아니다. Step 16이 마지막 구현 Step이므로 여기서 닫는다.

⚠️ **로그아웃을 `Account` 화면 안에 두지 않는 이유**: `Account`는 PIN 게이트 뒤다. PIN을 잊은 운영자가
로그아웃조차 못 하면 교대 시 계정이 그대로 남는다(PIN 분실은 앱 내 복구 불가 — 07 §6.5). 로그아웃은
**게이트 앞**에 있어야 한다.

### 3.4 진단 섹션은 **6개**다 (`analysis/13 §9.2`의 4섹션은 데스크톱 기준)

`analysis/13 §9.2`는 4섹션(카메라·인코더·서버·로그)이고 `web-client/03 §15.2`는 6섹션
(+ 개발자 문의 · 앱)이다. 후자는 it17 Windows 대응(개발자 문의)과 **웹 고유 항목**(PWA·SW 상태)을
더한 것이고, 4섹션의 상위집합이라 **모순이 아니다**. 6섹션을 구현하고 `analysis/13 §9.2`에
"웹은 6섹션(03 §15.2)" 참조 한 줄을 추가한다. 규격을 줄이지 않는다.

---

## 4. 역할 게이트 — 도메인이 판정을 소유한다

### 4.1 `domain/accounts/accountAdminPolicy.ts` (신규 · 순수)

```ts
import type { SessionUser } from "./sessionUser";
import type { UserRole } from "../roles/userRole";
import { canManage, canResetPin, hierarchyRank, isPower } from "../roles/userRole";
import { assignableRoles } from "../roles/roleChangePolicy";

/** 관리자 도구 진입(사용자 관리). analysis/60 §2 — power만. */
export function canOpenUserMgmt(role: UserRole | null): boolean {
  return role !== null && isPower(role);
}

/** 전역 TempUser 한도 편집. **admin만**(analysis/60 §2 · 31 §4.9 `requireAdmin`). */
export function canEditGlobalLimits(role: UserRole | null): boolean {
  return role === "admin";
}

/** [키오스크 종료]. Windows `ExitApp`이 관리자 도구 페이지(power 전용) 안에 있는 것과 같은 게이트. */
export function canExitKiosk(role: UserRole | null): boolean {
  return canOpenUserMgmt(role);
}

/**
 * 목록 정렬 — **역할 위계 내림차순, 동급은 가입일 오름차순**(03 §14).
 * ⚠️ `createdAt`은 서버가 빈 문자열로 줄 수 있다(`parseSessionUser`의 폴백). 빈 값은 **맨 뒤**로
 *    보낸다 — 문자열 비교로 두면 빈 값이 항상 "가장 먼저 가입"이 되어 admin 위로 올라간다.
 */
export function sortManagedUsers(users: readonly SessionUser[]): SessionUser[];

/** 행 1개의 능력. 화면은 이 객체만 보고 렌더한다. */
export interface UserRowPolicy {
  readonly user: SessionUser;
  readonly isSelf: boolean;
  /** power AND 동급 이하 AND 자기 아님 */
  readonly canDelete: boolean;
  /** power AND **엄격히 낮은 위계** AND 자기 아님 */
  readonly canResetPin: boolean;
  /** 빈 배열이면 콤보를 렌더하지 않는다(자기 행 포함) */
  readonly assignableRoles: readonly UserRole[];
}

export function buildUserRow(actor: SessionUser, target: SessionUser): UserRowPolicy;
export function buildUserRows(
  actor: SessionUser,
  users: readonly SessionUser[],
): readonly UserRowPolicy[];   // 내부에서 sortManagedUsers를 적용한다
```

구현 규칙(서버와 1:1 — `analysis/31 §4.5·§4.6·§4.7`):

```
isSelf          = actor.id === target.id
canDelete       = isPower(actor.role) && canManage(actor.role, target.role) && !isSelf
canResetPin     = canResetPin(actor.role, target.role) && !isSelf     // 함수 자체에 isPower가 들어 있다
assignableRoles = isSelf ? [] : assignableRoles(actor.role, target.role)
```

⚠️ `canDelete`에서 `isPower`를 **빼면 안 된다**. `canManage`는 동급을 허용하므로 `temp_user`가 다른
`temp_user`를 "관리 가능"으로 계산한다(`analysis/60 §1.3` 경고). 반대로 `canResetPin`에는 `isPower`가
**이미 포함**돼 있으므로 중복해서 `&& isPower(...)`를 붙이지 않는다 — 붙여도 결과는 같지만 두 축이
갈라졌을 때 어느 쪽이 진실원인지 모호해진다.

### 4.2 동급 허용/차단 비대칭 — 화면에 그대로 드러낸다

manager가 다른 manager 행을 볼 때 **[삭제]는 있고 [PIN]은 없다**. 이 비대칭은 버그로 보이기 쉬워
누군가 "일관성"을 이유로 고칠 위험이 있다. 방어 3중:

1. 판정은 `buildUserRow` 한 곳(위).
2. `docs/spec-vectors/role-matrix.json`이 `canManage`/`canResetPin`을 **양쪽 플랫폼에서** 고정한다(무변경).
3. `userMgmtPolicy.test.ts`에 **명시 케이스**를 남긴다:
   `manager → manager`: `canDelete === true && canResetPin === false` + 주석 `// ↔ analysis/60 §1.3.1`.

### 4.3 2중 방어 (M10) — 이 Step의 적용 형태

`03 §1.3`은 3중(렌더 · 커맨드 · 서버 403)을 말하고, 이 Step이 새로 쓰는 코드는 앞 2개다.

| 층 | 위치 | 형태 |
|----|------|------|
| ① 렌더 가드 | `AccountView`·`UserMgmtView` | `row.canDelete && <Button …>` — **역할 문자열을 비교하지 않는다** |
| ② 액션 가드 | `userActions.ts`·`pinResetRunner.ts`·`adminLimitsForm.ts`의 **첫 실행문** | 판정 실패 시 `logger.warn` + `forbidden` 반환. 서버 왕복 없음 |
| ③ 서버 강제 | `isForbidden(err)` → `STRINGS.error.forbidden` 토스트 + **목록 유지** | 403이 빈 목록으로 보이지 않는다 |

정적 불변식:

| # | 검사 | 왜 |
|---|------|-----|
| **ACC-1** | `ui/views/AccountView.tsx`·`UserMgmtView.tsx`·`screens/account/*`·`screens/userMgmt/*` 에 `/["'](manager\|admin\|advanced_user\|temp_user)["']/`·`/\.role\s*(===\|!==)/` **0건** | 화면이 역할 문자열을 비교하면 서버 매트릭스와 조용히 갈라진다 |
| **ACC-2** | `runDeleteAccount`·`runSetRole`·`runPinReset` 각 함수 본문에서 **`buildUserRow(`/`canResetPin(`가 `deps.` 첫 호출보다 먼저** 등장 | 액션 가드가 뒤로 밀리면 서버 왕복이 먼저 일어난다(FR-10 선례) |
| **ACC-3** | `screens/account/*`·`screens/userMgmt/*`에 `pushModal(` **0건** | PIN 재설정·삭제 확인은 화면 로컬 오버레이다(FR-5·FR-8 계열) |
| **ACC-4** | `App.tsx`의 `case "Account":`·`case "UserMgmt":` 블록 둘 다 `<PinGate` 포함 | 새 보호 화면에서 게이트 누락 방지 |

---

## 5. `Account` 화면 (03 §13)

### 5.1 모드 축 — **화면 로컬 상태 1개**

```ts
export type AccountMode = "account" | "admin";
```

진입 모드는 `shell/accountModeIntent.ts`가 인계한다.

```ts
// ⚠️ 비파괴 읽기다. 소비형으로 만들면 <StrictMode> 2회차가 기본값으로 떨어져
//    [관리자 도구]로 들어와도 [내 정보]가 열린다 (Step 15 frameEditorIntent와 동일 함정).
export function writeAccountModeIntent(mode: AccountMode): void;
export function readAccountModeIntent(): AccountMode;   // 기본 "account"
```

화면 안에도 **세그먼트 전환**(`[내 정보] [관리자 도구]`)을 둔다. `[관리자 도구]` 탭은
`canOpenUserMgmt(role) || canEditGlobalLimits(role)`일 때만 렌더한다(= power).

⚠️ **모드 전환에 `go()`를 쓰지 않는다.** it19가 경고하는 "오버레이 간 전환이 복귀 지점을 덮어써
[닫기]가 무반응이 되는" 실패가 구조적으로 불가능해진다(03 §13 인용 블록). `overlayReturnTo`는
`Account` 진입 1회에만 기록된다.

### 5.2 모드 `account` — 내 정보(읽기 전용)

`screens/account/accountInfoRows.ts`(순수):

```ts
export interface AccountInfoRow { readonly label: string; readonly value: string; }
export function buildAccountInfoRows(user: SessionUser, deps: {
  readonly formatDate: (iso: string) => string;   // 시각 서식은 주입(도메인이 아님 · 결정성)
}): readonly AccountInfoRow[];
```

| 행 | 값 | 폴백 |
|----|-----|------|
| 계정 id | `user.id` | — |
| 이메일 | `user.email` | `null` → `"—"` |
| 로그인 방식 | `authMethodLabel(user)` | 모르는 값 → **"알 수 없음"**(§3.1) |
| 역할 | `roleLabel(user.role)` | — |
| 가입일 | `formatDate(user.createdAt)` | 빈 문자열·파싱 실패 → `"알 수 없음"` |

- **출처는 로그인 응답의 `user` DTO 하나뿐**이다(내 정보 조회 API가 없다 — `analysis/31 §10`).
  화면 진입 시 서버를 조회하지 않는다.
- **비밀번호 변경·계정 생성 UI를 만들지 않는다**(03 §13.1).

### 5.3 PIN 변경 — 기존 러너를 그대로 쓴다

`screens/account/pinChangeRunner.ts`:

```ts
export type PinChangeStep = "current" | "next" | "confirm";

export type PinChangeResult =
  | { readonly kind: "ok" }
  | { readonly kind: "confirmMismatch" }     // 새 PIN 2회 불일치 — 서버 왕복 없음
  | { readonly kind: "invalidFormat" }
  | { readonly kind: "currentWrong" }        // 401
  | { readonly kind: "unavailable" };        // 네트워크·기타

export interface PinChangeDeps {
  readonly hasPin: boolean;
  readonly currentPin: string | undefined;   // hasPin이면 필수
  readonly newPin: string;
  readonly confirmPin: string;
  readonly setPin: (newPin: string, currentPin?: string) => Promise<void>;
  readonly markPinSet: () => void;
  readonly now: () => number;
  readonly lock: PinLockRepo;
}
export async function runPinChange(deps: PinChangeDeps): Promise<PinChangeResult>;
```

구현: 형식·일치 검사 후 **`runPinAttempt`(F11)에 `mode:"setup"` + `currentPin`** 을 위임한다.
`runPinAttempt`가 이미 ① `unauthorized:"reject"` 경유(로그아웃 방지 — PIN-2) ② `markPinSet` ③ 잠금
클리어 ④ **PIN 값을 로그에 담지 않음**을 보장한다. **여기서 서버 왕복을 새로 쓰지 마라.**

⚠️ `hasPin === false`인 계정은 애초에 PIN 게이트가 **최초 설정**을 강제하므로 `Account`에 도달한
시점에는 항상 `hasPin === true`다. 그래도 `hasPin` 분기를 남긴다 — 게이트가 `notRequired`로
빠지는 경로(게스트)는 `Account`에 도달할 수 없지만, 가정을 코드로 굳히지 않는다.

UI: 화면 로컬 오버레이(`OverlayDialog`) + `PinKeypad`. 3단계(현재 → 새 → 확인)를 **한 오버레이 안에서**
진행하고 각 단계 제목만 바꾼다(`PinPromptModal`과 같은 형태).

### 5.4 모드 `admin` — 관리자 도구

| 항목 | 게이트 | 동작 |
|------|--------|------|
| [사용자 관리] | `canOpenUserMgmt(role)` | `go("UserMgmt")` |
| 전역 무료 한도(`qrHours`·`qrCount`) | `canEditGlobalLimits(role)` | §5.4.1 |
| ~~[앱 종료]~~ | — | **만들지 않는다**(WD5). 정적 검사는 두지 않지만 완료 기준 non-goal에 넣는다 |
| [키오스크 종료] | `canExitKiosk(role)` | §5.4.2 |

#### 5.4.1 전역 무료 한도

`domain/accounts/tempUserLimitsPolicy.ts`(순수):

```ts
export const MIN_QR_HOURS = 1;      export const MAX_QR_HOURS = 8760;    // analysis/31 §4.9
export const MIN_QR_COUNT = 1;      export const MAX_QR_COUNT = 100000;

/** 텍스트 → 정수. 공백·부호 허용, 소수점·지수·빈 값은 null(slotsFile.tryParseInt와 같은 엄격도). */
export function parseLimitInput(raw: string): number | null;

export type LimitsRejection = "qrHours-range" | "qrCount-range" | "no-change";
export interface LimitsValidation { readonly ok: boolean; readonly reason?: LimitsRejection; }

/** 저장 전 검증. **서버가 400으로 거부할 요청을 보내지 않는다**. */
export function validateTempUserLimits(
  draft: { qrHours: number | null; qrCount: number | null },
  current: TempUserLimits,
): LimitsValidation;

/** 실제로 달라진 키만 담는다(서버는 "최소 1개"를 요구한다 — F21). */
export function buildLimitsPatch(
  draft: { qrHours: number | null; qrCount: number | null },
  current: TempUserLimits,
): Partial<TempUserLimits>;
```

`screens/account/adminLimitsForm.ts`:
- **첫 실행문이 `canEditGlobalLimits(actorRole)` 가드**(ACC-2).
- 조회는 `tempUserLimitsService.get()`. 실패는 `{kind:"failed"}`이며 **기본값으로 위장하지 않는다**
  (`DEFAULT_TEMP_USER_LIMITS`를 화면에 "현재 값"으로 보여주면 admin이 서버 값을 오독한다).
- 저장 성공 시 서버가 돌려준 **갱신된 전체 한도**로 draft를 재반영한다(설정 화면 §12.4 4단과 같은 규칙).

#### 5.4.2 [키오스크 종료] — 앱 종료 대체 (WD5)

```ts
// screens/account/kioskExit.ts
export interface KioskExitDeps {
  readonly role: UserRole | null;
  readonly exitFullscreen: () => Promise<void>;
  readonly logout: () => void;
  readonly returnHome: (reason: string) => Promise<void>;
  readonly toast: (kind: "info" | "error", message: string) => void;
}
export async function runKioskExit(deps: KioskExitDeps): Promise<boolean>;
```

순서가 규격이다: **가드 → 전체화면 해제 → 로그아웃 → 홈 복귀 → 안내 토스트**(03 §13.2).

⚠️ **탭을 스크립트로 닫을 수 없다.** `window.close()`는 스크립트가 연 창에서만 동작하고 키오스크의
첫 탭은 사용자가 연 것이다. **`window.close()`를 호출하지 않는다** — 조용히 실패해서 "버튼이 안 먹는다"가
된다. 대신 안내 토스트로 마무리한다:
`STRINGS.kiosk.exitNotice = "키오스크를 종료했습니다. 브라우저(또는 앱)를 직접 닫아 주세요."`

⚠️ 파괴적이므로 **인라인 2단 확인**이다(Step 13 [전체 삭제]·Step 15 삭제 오버레이와 같은 패턴).
새 셸 모달을 만들지 않는다(ACC-3).

### 5.5 PIN 게이트 그룹 — `Account ↔ UserMgmt`를 한 단위로 (07 §6.1)

**문제**: `installPinGateLifecycle`이 **화면이 바뀌면** 승인을 폐기한다(F14). `UserMgmt`를 그대로
`<PinGate>`로 감싸면 `Account → UserMgmt → [뒤로] → Account`에서 **PIN을 두 번 더 묻는다.**
07 §6.1은 "계정 관리 / 관리자 도구 = **진입 시 1회 판정**"이고, 03 §14의 [뒤로]는 `Account` 직행이다.

**답**: 게이트 키를 화면이 아니라 **그룹**으로 만든다.

```ts
// domain/auth/pinGatePolicy.ts (추가 · 순수)
import type { AppState } from "../navigation/appState";

/**
 * PIN 승인이 공유되는 단위. `UserMgmt`는 `Account`의 하위 페이지이므로 같은 그룹이다
 * (03 §14 [뒤로]는 Account 직행 · 07 §6.1 "진입 시 1회 판정").
 * ⚠️ `Settings`는 "매번 확인"이므로 자기 자신 그룹이다 — 여기에 묶지 마라.
 */
export function pinGateGroup(screen: AppState): AppState {
  return screen === "UserMgmt" ? "Account" : screen;
}
```

`shell/pinGate.ts` 3곳을 그룹 기준으로 바꾼다.

| 위치 | 변경 |
|------|------|
| `ensureScreenPinGate(screen)` | 스토어에 `pinGateGroup(screen)`을 기록 |
| `usePinGateStatus(screen)` | `s.screen === pinGateGroup(screen)` 비교 |
| `installPinGateLifecycle`의 화면 구독 | `pinGateGroup(state.screen) !== pinGateGroup(previous.screen)`일 때만 폐기 |

**안전성**: `UserMgmt`는 `canTransition`상 **`Account`에서만** 진입 가능하고(F15) 그 `Account`는 이미
게이트를 통과한 상태다. 그룹 공유가 새 우회로를 만들지 않는다. `Settings → Account`,
`Account → Settings`는 그룹이 달라 여전히 매번 판정한다.

`App.tsx`:

```tsx
case "Account":
  return <PinGate screen="Account"><AccountView /></PinGate>;
case "UserMgmt":
  return <PinGate screen="UserMgmt"><UserMgmtView /></PinGate>;
```

### 5.6 계정 메뉴 팝오버 (§3.3)

`screens/account/accountMenu.ts`(순수 · node 테스트):

```ts
export type AccountMenuItemId = "manage" | "adminTools" | "logout";
export interface AccountMenuItem { readonly id: AccountMenuItemId; readonly label: string; }

/** 게스트는 빈 배열(호출측이 곧바로 Login으로 보낸다). */
export function buildAccountMenuItems(user: SessionUser | null): readonly AccountMenuItem[];
```

- `manage`(로그인 전원) · `adminTools`(`canOpenUserMgmt`) · `logout`(로그인 전원) — 02 §5.1 그대로.
- `TopBar`는 `accountMenuItems`와 `onAccountMenuSelect(id)`를 **props로 받는다**. 권한 판정은 `App.tsx`가
  이 순수 함수로 하고 컴포넌트는 렌더만 한다(ACC-1과 같은 정신).
- 팝오버: 전체 화면 투명 backdrop 버튼(바깥 클릭 닫기) + `Esc` 닫기 + 열릴 때 첫 항목 포커스.
  `useEffect(() => setOpen(false), [screen])`로 화면 전환 시 닫는다. **셸 모달 스택을 쓰지 않는다**
  (`ModalId`를 늘리지 않는다 — FR-8 정신).
- 핸들러:
  | id | 동작 |
  |----|------|
  | `manage` | `writeAccountModeIntent("account")` → `go("Account")` |
  | `adminTools` | `writeAccountModeIntent("admin")` → `go("Account")` |
  | `logout` | `sessionStore.logout()` → `returnHome("로그아웃")` → 토스트 |

⚠️ 로그아웃은 **토큰을 직접 지우지 않는다.** `logout()`이 `currentUser`를 null로 만들면 M1 구독
(`installTokenLifecycle`)이 JWT를 폐기한다(02 §5.1 "토큰 폐기를 로그아웃 버튼에 걸지 않는다").

---

## 6. `UserMgmt` 화면 (03 §14)

### 6.1 목록 로드 — **실패를 빈 목록으로 위장하지 않는다**

```ts
// screens/userMgmt/userListRunner.ts
export type UserListView =
  | { readonly kind: "loading" }
  | { readonly kind: "ready"; readonly rows: readonly UserRowPolicy[]; readonly total: number }
  | { readonly kind: "failed"; readonly reason: "forbidden" | "network" | "unknown" }
  | { readonly kind: "cancelled" };

export interface UserListDeps {
  readonly actor: SessionUser | null;
  readonly list: () => Promise<SessionUser[]>;
}
export async function loadUserList(deps: UserListDeps, signal?: AbortSignal): Promise<UserListView>;
```

- **첫 실행문**: `if (deps.actor === null || !canOpenUserMgmt(deps.actor.role)) return {kind:"failed", reason:"forbidden"}`.
- `accountService.list()`는 **예외를 던진다**(F3 주석: "실패는 예외 — 빈 목록으로 표시하지 않는다").
  여기서 `isForbidden`/`NetworkError`로 접어 판별 유니온으로 만든다(`pinPromptRunner.toCallOutcome` 선례).
- 취소는 `serverStatusPanel.loadServerStatus`와 같은 **결과 폐기** 방식(`aborted()` 2회 검사).
  `accountService`에 `AbortSignal`을 뚫지 않는다 — 이 Step의 범위 밖이고, 목록 조회는 짧다.
- 실패 문구는 **규격 문자열**이다: `"사용자 목록을 불러올 수 없습니다."`(03 §14 · analysis/13 §10.3).
- 403은 실패 화면 + `STRINGS.error.forbidden` 토스트. **행이 0개인 화면과 시각적으로 달라야 한다.**

### 6.2 정렬·요약

- 정렬은 `sortManagedUsers`(§4.1). 컬럼 헤더에 **▼ 표기**를 두고 별도 안내 문구는 두지 않는다(it19).
  정렬은 **고정**이다 — 사용자가 바꾸는 기능을 만들지 않는다(03 §14는 정렬 규칙만 규정한다).
- 상단 인원 요약: `formatCount(STRINGS.userMgmt.total, rows.length)` → `"총 {n}명"`.

### 6.3 행 액션 3종

```ts
// screens/userMgmt/userActions.ts
export type UserActionResult =
  | { readonly kind: "ok" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "notFound" }
  | { readonly kind: "failed" };

export async function runDeleteAccount(deps: {
  actor: SessionUser; target: SessionUser;
  deleteAccount: (id: string) => Promise<void>;
}): Promise<UserActionResult>;

export async function runSetRole(deps: {
  actor: SessionUser; target: SessionUser; nextRole: UserRole;
  setRole: (id: string, role: UserRole) => Promise<void>;
}): Promise<UserActionResult | { readonly kind: "noop" }>;
```

| 액션 | 첫 줄 가드 | 특기사항 |
|------|-----------|----------|
| 삭제 | `buildUserRow(actor, target).canDelete` | **인라인 2단 확인**(ACC-3). 성공 문구: `"{id} 삭제됨(소유 프레임 포함)."` — cascade를 명시한다(03 §14) |
| 역할 변경 | `buildUserRow(...).assignableRoles.includes(nextRole)` | **no-op은 서버로 보내지 않는다**(`nextRole === target.role` → `{kind:"noop"}`). 성공 후 목록 재조회 |
| PIN 재설정 | `buildUserRow(...).canResetPin` | §6.3.1 |

성공한 삭제·역할 변경 뒤에는 **목록을 다시 조회**한다. 로컬 배열을 손으로 갱신하면 서버의
cascade·폴백(알 수 없는 역할 → `user`)과 화면이 갈라진다.

#### 6.3.1 타 계정 PIN 재설정

```ts
// screens/userMgmt/pinResetRunner.ts   ← PIN_FILES에 추가한다(PIN-1 자동 적용)
export type PinResetResult =
  | { readonly kind: "ok" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "invalidFormat" }
  | { readonly kind: "confirmMismatch" }
  | { readonly kind: "notFound" }
  | { readonly kind: "failed" };

export async function runPinReset(deps: {
  actor: SessionUser; target: SessionUser;
  first: string; second: string;
  resetOtherPin: (id: string, newPin: string) => Promise<void>;
}): Promise<PinResetResult>;
```

- 첫 줄 가드 → 형식(`isPinFormatValid`) → 2회 일치 → 서버 1회.
- ⚠️ **`resetOtherPin`에 `unauthorized:"reject"`를 넘기지 않는다**(정적 검사 **PIN-2b**가 0건을 고정).
  그 라우트의 401은 진짜 세션 만료뿐이다(권한 위반은 403).
- ⚠️ **PIN 값을 로그·반환값·에러 메시지에 절대 싣지 않는다.** 로그 컨텍스트는 `targetId`·`attemptOutcome`만.
  `pin`·`newPin`·`currentPin`은 마스킹 대상이라 담아도 무의미하고, 이름을 바꿔 우회하면 **진짜로 샌다**(PIN-1).
- UI: 화면 로컬 오버레이 + `PinKeypad` 2단계(새 PIN → 확인).

### 6.4 반응형 — 좁은 화면은 카드 리스트

가로 스크롤은 **금지**다(03 §1.2). 같은 데이터를 두 가지로 렌더한다.

- 넓은 화면(`min-width: 720px`): `<table>` — id · 이메일 · 역할 · 가입일 · 작업
- 좁은 화면: `<ul>` 카드 — 헤더(id + 역할 배지) / 이메일·가입일 / 액션 버튼 줄

⚠️ **두 마크업이 같은 `UserRowPolicy[]`를 받는다.** 렌더 가드를 한쪽에만 넣으면 좁은 화면에서
[PIN]이 되살아난다 → 행 렌더를 **한 컴포넌트**(`UserRowActions`)로 뽑아 표·카드가 공유한다.

### 6.5 [뒤로]

`go("Account")` **직행**이다(03 §14 — 복귀 지점을 쓰지 않는다). `writeAccountModeIntent("admin")`을
먼저 호출해 관리자 도구 모드로 돌아가게 한다.

---

## 7. 진단·상태 모달 (03 §15.2)

### 7.1 진입

설정 §5 고급에 `[진단·상태]` 버튼. **로그인 전용**(03 §15.2) → 렌더 가드 `!ctx.isGuest` +
`openDiagnostics()` 첫 줄 가드. `shellStore.pushModal({ id: "diagnostics", dismissible: true })`.
`App.tsx`의 `ModalStack`에 `case "diagnostics": return <DiagnosticsModal />;`를 추가하고
**스텁 default 분기를 지운다**(남은 미구현 모달이 0이 된다).

### 7.2 6섹션 데이터 소스

```ts
// screens/modals/diagnostics/diagnosticsPresenter.ts
export interface DiagnosticsSection {
  readonly id: "camera" | "encoder" | "server" | "logStorage" | "contact" | "app";
  readonly title: string;
  readonly rows: readonly { label: string; value: string; tone: "ok" | "warn" | "bad" | "neutral" }[];
}
export interface DiagnosticsSnapshot { readonly sections: readonly DiagnosticsSection[]; }

export interface DiagnosticsDeps { /* 아래 표의 소스를 전부 함수로 주입 */ }
export async function collectDiagnostics(
  deps: DiagnosticsDeps, signal?: AbortSignal,
): Promise<DiagnosticsSnapshot>;
```

| 섹션 | 행 | 소스(F4·F7·F8) |
|------|-----|-----------------|
| 카메라 | 장치 수 · 목록(`displayLabel(d,i)`) · 상태 · 획득 해상도 · 가공 해상도 · fps · 권한 | `listCameras()` · `getCameraService().state()/settings()/processedSize()/fps()` · `navigator.permissions.query({name:"camera"})` |
| 비디오 인코더 | 경로(`WebCodecs`/`MediaRecorder(mp4)`/**미지원**) · 코덱 · 사유 · 후보별 지원 여부 | `getTimelapseService().encoderProbe()` — `null`이면 `"아직 판정 전(촬영 후 표시)"` |
| 서버 연결 | 주소 · 구성 · **도달** · 게이트 키 · 서버 배포 시각 · **버킷** · **현재 계정** | `describeServerStatus(view)` **재사용**(F6) + `env.storageBucket` + `currentUser?.id ?? "게스트"` |
| 로그·저장소 | 로그 건수 · 기간(oldest~newest) · [로그 내보내기] · 영속 승인 · 사용량/할당량 · OPFS 세션 잔재 수 · 보관 결과물(세션 수·용량) · 프레임 캐시 용량 | `getLogStore()?.stats()` · `readStorageStatus()` · `getOpfsClient().list("sessions").length` · `getResultsStore().usage()` · `getFrameStore().usageBytes()` |
| 개발자 문의 | `devmcjo@gmail.com` + **[복사]** · Version(`env.appVersion`) · Build Date(`env.buildDate`) · Web Deploy Date(`probe.deployedAt`) | env + probe |
| 앱 | 버전 · Service Worker 상태 + **[지금 적용]** · PWA 설치 여부 | `swUpdate` 스토어 · `isStandaloneDisplay()` |

- `tone`은 **색 + 문자**를 함께 쓴다(색만으로 구분 금지 — 01 §8). `describeServerStatus`가 이미 문자
  기반이라 tone은 표시 보조일 뿐이다.
- **Build Date는 여기서만 노출한다**(하단 캡션 금지 — it18 · 05 §8.2). `versionCaption`은 손대지 않는다.
- 권한 조회: `try { … } catch { null }` + `typeof navigator.permissions?.query !== "function"` 런타임 감지
  (**타입을 믿지 않는다** — 15 §4 함정 #2). 값이 없으면 `"알 수 없음"`.

### 7.3 게이트 키 — 값을 절대 노출하지 않는다

- 표시는 `describeServerStatus`의 3상태(`설정됨`/`거부됨`/`미설정`)뿐이다.
- **"구성됨"과 "도달"은 별개 행**이다. `probe`가 `GET /health`(도달) → `GET /frames/default`(401 여부로
  키 유효성)를 순서대로 수행한다(F4). 도달 실패면 `gateKeyValid === null` → env 값의 **길이만** 보고
  `설정됨`/`미설정`을 표시한다.
- 정적 불변식 **DIAG-1**: `screens/modals/diagnostics/*`·`ui/views/*`에서 `backendApiKey`가 등장하는
  모든 줄은 `.length` 또는 `.trim()`을 포함해야 한다(= 값이 그대로 문자열에 들어가는 경로 0건).
- 로그 금지 키 검사(PIN-1 정규식)의 파일 목록에 진단 모듈을 추가한다 → `apiKey`·`token`·`pin` 컨텍스트 0건.

### 7.4 [로그 내보내기] — 실패해도 크래시 없이 로그만

```ts
// adapters/storage/exportImport.ts
export function logExportFileName(localTime: Date): string;   // mcphoto-log-{YYMMDD_HHMM}.log
export async function exportLogs(deps: {
  readonly exportText: () => Promise<string>;
  readonly write: (blob: Blob, fileName: string) => boolean;
  readonly now: () => Date;
}): Promise<boolean>;
```

- `exportText()`가 던져도 **`false`** 로 접는다(`try/catch`). `exportBlob`은 원래 던지지 않는다(F9).
- 파일명 규약은 `settingsExportFileName`(F10)과 **같은 서식 함수 모양**을 따른다(`{YYMMDD_HHMM}` 로컬 시각).
- 실패 시 `logger.warn("로그 내보내기 실패")` + 실패 토스트. **모달은 닫히지 않는다.**

### 7.5 자원 해제

| 자원 | 해제 |
|------|------|
| 진행 중인 프로브·수집 | `AbortController` — 언마운트에서 `abort()`, 결과는 `cancelled`로 폐기 |
| `swUpdate` 스토어 구독 | zustand `useStore` (자동) |
| 클립보드 | 없음 |
| 카메라 | **열지 않는다.** 진단은 `state()`/`settings()`를 **읽기만** 한다 — `start()`를 부르면 설정 화면에서 카메라 LED가 켜진다 |

---

## 8. PWA · Service Worker (01 §6)

### 8.1 빌드 파이프라인 — 의존성을 늘리지 않는다

`workbox`·`vite-plugin-pwa`를 **쓰지 않는다**. 이유: ① `THIRD-PARTY.md` 핀 고정·라이선스 검토 비용
② 생성 코드가 CSP·캐시 정책을 우리가 통제하지 못하는 형태로 넣는다 ③ 우리가 필요한 것은 셸 precache
하나뿐이다.

```
npm run build
  ① vite build                              → web/kiosk/  (index.html · assets/*  · precache-manifest.json)
  ② vite build --config vite.sw.config.ts   → web/kiosk/sw.js   (emptyOutDir:false · iife · 단일 파일)
```

- **`format: "iife"`** 로 만든다 → `navigator.serviceWorker.register("/sw.js")`(classic). 모듈 SW는
  Safari 16.4 미만에 없어서 **등록 자체가 실패**한다. 클래식이면 그 위험이 0이다.
- `vite.sw.config.ts`는 `resolve.alias`를 공유해(`vite.aliases.ts`) `@domain/frames/bundleManifest`를
  import할 수 있다(순수 모듈이라 런타임 의존이 0이다).
- `emptyOutDir: false` — ②가 ①의 산출물을 지우면 안 된다. 순서가 규격이다.

### 8.2 precache 매니페스트 — sw.js 바이트가 **자산 목록에 따라 변한다**

⚠️ **함정**: SW 업데이트는 브라우저가 `sw.js` **바이트 차이**로 감지한다. 자산 해시가 바뀌어도
`sw.js`가 동일하면 **영원히 갱신되지 않는다.** 그래서 자산 목록을 sw.js 안에 **인라인**한다.

```ts
// vite.precache.ts (순수 · 단위 테스트)
/** 번들 산출 파일명 → precache 대상. `.map`·`sw.js`·매니페스트 자신은 제외한다. */
export function collectPrecacheAssets(fileNames: readonly string[]): string[];   // "/assets/x-abc.js" 형태, 정렬
/** 자산 목록의 결정적 해시(FNV-1a 32bit, 8자리 hex). 같은 입력 → 같은 값. */
export function precacheBuildId(assets: readonly string[]): string;
```

- `vite.config.ts`에 인라인 플러그인 `precacheManifestPlugin()`:
  `generateBundle`에서 `this.emitFile({type:"asset", fileName:"precache-manifest.json", source: JSON.stringify({ buildId, assets })})`.
- `vite.sw.config.ts`는 **디스크에서** `web/kiosk/precache-manifest.json`을 읽어
  `define: { __MCPHOTO_PRECACHE__: JSON.stringify(manifest) }` 로 주입한다. 파일이 없으면
  `{buildId:"dev", assets:[]}`(dev 빌드 단독 실행 대비).
- `CACHE_NAME = "mcphoto-shell-" + __MCPHOTO_PRECACHE__.buildId`. **타임스탬프를 쓰지 않는다** —
  내용이 같은 재빌드가 캐시를 churn시키지 않는다.
- `web/firebase.json`에 `/precache-manifest.json` **no-cache** 헤더를 추가한다(옛 매니페스트가
  캐시되면 새 SW가 없는 자산을 precache하려다 실패한다).

### 8.3 라우팅 정책 — 순수 분류기, **기본은 bypass**

```ts
// adapters/platform/swPolicy.ts (순수 · import 0)
export type SwRoute =
  | { kind: "bypass" }      // SW가 손대지 않는다(respondWith 미호출)
  | { kind: "navigate" }    // network-first(3s) → 셸 폴백
  | { kind: "immutable" }   // cache-first (해시 자산)
  | { kind: "fresh" }       // network-first → 캐시 폴백
  | { kind: "static" };     // stale-while-revalidate

export function classifySwRequest(input: {
  readonly method: string; readonly mode: string;
  readonly url: string; readonly origin: string;
}): SwRoute;

export const PRECACHE_STABLE_URLS: readonly string[];
export function isCacheableResponse(status: number, type: string): boolean;
```

판정 순서가 계약이다:

```
1. method !== "GET"                                   → bypass
2. URL 파싱 실패                                       → bypass
3. url.origin !== origin                              → bypass   ← 백엔드·서명 PUT·Storage 전부 여기서 끝
4. pathname이 "/api/"로 시작하거나 "/uploads"를 포함    → bypass   ← 동일 출처 프록시가 생겨도 안전
5. mode === "navigate"                                → navigate
6. pathname이 "/assets/"로 시작                        → immutable
7. pathname ∈ {/branding.json, /manifest.webmanifest,
               /frames/index.json, /precache-manifest.json} → fresh
8. pathname이 "/icons/"·"/frames/"·"/sounds/"로 시작
   또는 "/favicon.ico"                                 → static
9. 그 외                                               → bypass   ← **기본 거부**
```

- **API 응답·서명 URL은 3·4에서 이미 걸러진다.** 알 수 없는 경로는 캐시하지 않는다.
- `isCacheableResponse(status, type)` = `status === 200 && (type === "basic" || type === "default")`.
  `opaque`는 절대 캐시하지 않는다(용량·오작동).
- `/branding.json`이 `fresh`인 이유: 운영자가 교체하는 파일이고 Hosting도 no-cache다(05 §8.1).

### 8.4 `src/sw.ts` — 얇은 래퍼

```ts
/// <reference lib="webworker" />                 // F20 — 기존 워커 2개와 같은 방식(tsc 통과 실적 있음)
declare const self: ServiceWorkerGlobalScope;     // 모듈 스코프 shadow — 파일이 모듈이라 충돌하지 않는다
/** 빌드 타임 주입(§8.2). `vite.sw.config.ts`의 `define`이 리터럴로 치환한다. */
declare const __MCPHOTO_PRECACHE__: { readonly buildId: string; readonly assets: readonly string[] };
import { classifySwRequest, isCacheableResponse, PRECACHE_STABLE_URLS } from "@adapters/platform/swPolicy";
```

⚠️ `__MCPHOTO_PRECACHE__` 선언은 **`src/sw.ts` 안에** 둔다. `vite-env.d.ts`(전역)에 두면 앱 번들에서도
쓸 수 있는 것처럼 보이는데 `vite.config.ts`는 그 이름을 `define`하지 않아 **런타임 `ReferenceError`** 가 된다.

| 이벤트 | 동작 |
|--------|------|
| `install` | `PRECACHE_STABLE_URLS` + `__MCPHOTO_PRECACHE__.assets` + `/frames/index.json`에서 파생한 번들 프레임 경로를 캐시한다. **`skipWaiting()`을 부르지 않는다.** |
| `activate` | `mcphoto-shell-` 접두의 **다른** 캐시 삭제 → `clients.claim()` |
| `fetch` | `classifySwRequest` → `bypass`면 `respondWith`를 **부르지 않는다** |
| `message` | `{type:"MCPHOTO_APPLY_UPDATE"}` 일 때만 `self.skipWaiting()` |

⚠️ **`cache.addAll`을 쓰지 마라.** 원자적이라 URL 하나가 404면 **install 전체가 실패**하고 SW가 영원히
설치되지 않는다. `public/sounds/shutter.wav`는 **지금 존재하지 않는다**(F19) — `addAll`이면 첫날부터
깨진다. 개별 `cache.add(url)`를 `Promise.allSettled`로 감싸고 실패는 무시한다.

⚠️ **`logger`를 import하지 마라**(정적 검사 SW-3). Worker/SW에는 로그 스토어가 붙지 않아 남긴 로그가
진단에 **도달하지 않는다**(15 §4 함정 #12). `console.*`도 금지다. 진단이 필요한 사실은 메인 스레드가
`registration` 상태에서 읽는다.

### 8.5 업데이트 정책 — `skipWaiting`은 사용자 트리거 1경로뿐

```ts
// shell/swUpdate.ts
export type SwStatus = "unsupported" | "disabled" | "registering" | "active" | "waiting" | "failed";
export interface SwState { readonly status: SwStatus; readonly buildId: string | null; }

export function installServiceWorker(container?: ServiceWorkerContainerLike): void;  // main.tsx 7단계
export function useSwState(): SwState;
export async function checkForUpdate(): Promise<boolean>;      // [앱 업데이트 확인]
export async function applyWaitingUpdate(): Promise<boolean>;  // [지금 적용]
```

| 규칙 | 구현 |
|------|------|
| `install`에서 `skipWaiting` **미사용** | SW-1이 `install` 리스너 블록에 `skipWaiting` 0건임을 고정 |
| 새 버전은 **다음 앱 시작**에 자동 적용 | 모든 탭이 닫히면 waiting → active |
| [지금 적용] | `registration.waiting.postMessage({type:"MCPHOTO_APPLY_UPDATE"})` → `controllerchange` 1회 수신 → `location.reload()` |
| **촬영 중 금지** | `isSessionActive(currentScreen())`이면 [지금 적용] **미노출**(+ 액션 첫 줄 가드). 안내: `"촬영이 끝난 뒤 적용할 수 있습니다."` |
| dev에서 등록 안 함 | `if (!import.meta.env.PROD) → status:"disabled"`. dev 서버에는 `/sw.js`가 없고, 남은 SW가 dev 자산을 가로채면 원인을 못 찾는다 |

⚠️ **[지금 적용]은 페이지를 새로 고친다 → 메모리 전용 JWT(M2)가 사라져 로그아웃된다.** 버튼 옆에
`"적용하면 앱이 새로 시작되고 로그인이 해제됩니다."`를 **상시 캡션**으로 둔다. 이 `location.reload()`가
`main.tsx`의 "리로드 금지" 규약(`main.tsx:30-31`)과 충돌하지 않는 이유: 그 규약은 **`main.tsx` 파일**의
암묵적 리다이렉트를 금지한 것이고, 여기는 `shell/swUpdate.ts`의 **사용자 명시 조작**이다. 같은 문장을
`swUpdate.ts` 상단 주석에 남긴다.

⚠️ `controllerchange`에 **1회 가드**를 둔다(`let reloaded = false`). 없으면 리로드 루프가 난다.

### 8.6 CSP·Hosting 변경 2줄

| 파일 | 변경 | 왜 |
|------|------|-----|
| `web/firebase.json` kiosk CSP | `connect-src` 에 **`blob:`** 추가 | A1 — Step 15의 `fetchFrameImageBytes`가 `blob:` URL을 `fetch`한다. 브라우저별로 `'self'`가 `blob:`을 덮는지 갈리고, 막히면 **[선택 편집]이 운영에서만 실패**한다. 같은 출처 blob 허용은 새 공격면을 만들지 않는다 |
| 동 파일 headers | `/precache-manifest.json` **no-cache** | §8.2 |

`worker-src 'self' blob:`은 이미 있어 SW 등록에 추가 변경이 없다(F17·`web/firebase.json:42`).

---

## 9. 내보내기 / 가져오기 (05 §2.5·§4.6·§7)

설정 §6 저장소·데이터 섹션에 버튼을 채운다. **설정 JSON은 Step 13에서 이미 완성**됐다(F10) — 손대지 않는다.

### 9.1 store-zip 코덱 (`adapters/storage/zipStore.ts` · 순수 · import 0)

```ts
export interface ZipInputEntry { readonly path: string; readonly bytes: Uint8Array; }
export interface ParsedZipEntry {
  readonly path: string;
  /** 0 = store(비압축), 8 = deflate. 그 외 method는 목록에서 제외된다. */
  readonly method: 0 | 8;
  /** method 8이면 **아직 압축된** 바이트다. 해제는 어댑터가 한다(브라우저 API). */
  readonly data: Uint8Array;
  readonly crc32: number;
  readonly uncompressedSize: number;
}

export function crc32(bytes: Uint8Array): number;
/** store(무압축) zip 생성. 로컬 헤더 + 중앙 디렉터리 + EOCD. UTF-8 파일명 플래그(bit 11) 설정. */
export function buildStoreZip(entries: readonly ZipInputEntry[]): Uint8Array;
/** EOCD → 중앙 디렉터리 → 로컬 헤더 순으로 읽는다. 손상 항목은 **건너뛰고 계속**(slotsFile와 동형). */
export function parseZipEntries(bytes: Uint8Array): ParsedZipEntry[];
```

- 압축하지 않는 이유: PNG는 이미 압축돼 있어 deflate 이득이 거의 없고, 무압축 writer는 의존성 0이다(05 §4.6).
- **읽기는 deflate도 받아야 한다** — 운영자가 Windows 탐색기로 다시 압축하면 method 8이 된다(A3).
- 디렉터리 엔트리(`path`가 `/`로 끝남)·`..`·절대경로는 파싱 단계에서 **버린다**(경로 조작 방어).

### 9.2 프레임 내보내기

```ts
// adapters/storage/exportImport.ts
export function frameZipFileName(localTime: Date): string;      // mcphoto-frames-{YYMMDD_HHMM}.zip
/** 공용 `{이름}.png` / 개인 `{계정}_{이름}.png` — Windows `Frame\` 규약(05 §4.3·§4.6). */
export function frameEntryBaseName(frame: FrameTemplate): string;
/** 같은 base가 겹치면 `-2`, `-3`… 을 붙인다. zip 안의 중복 경로를 만들지 않는다. */
export function dedupeEntryNames(baseNames: readonly string[]): string[];

export interface FrameExportOutcome {
  readonly ok: boolean;
  readonly exported: number;
  readonly skipped: number;      // 이미지 바이트를 못 읽은 프레임
}
export async function exportFrames(deps: FrameExportDeps): Promise<FrameExportOutcome>;
```

- 대상: `frameStore.listPublic()` + `frameStore.listPersonal(userId)`.
  **번들·fallback 프레임은 제외**한다(저장소에 없고, 재배포 자산이라 백업 대상이 아니다).
- `.slots` 본문은 **기존 `serializeSlotsFile`** 을 그대로 쓴다(`#imagesize` · `#dbid` 규약 · `\n` · UTF-8).
- **이미지 바이트는 OPFS에서 직접 읽는다.**

  ```ts
  // adapters/storage/frameStore.ts (추가)
  /** 목록의 템플릿 → 저장된 PNG 바이트. 없거나 실패면 null(예외 미전파). */
  readImageBytes(frame: FrameTemplate): Promise<Blob | null>;
  ```

  ⚠️ **`fetch(frame.imageUrl)`로 blob URL을 읽지 마라.** ① CSP `connect-src`가 `blob:`을 덮는지 확실치
  않다(A1) ② 이미 디스크에 있는 바이트를 메모리로 한 번 더 왕복시킨다. `frameStore`는 레코드의
  `imageFile`을 알고 있고 `opfs.readFile`은 **메인 스레드 읽기가 허용**된다(05 §3.1).
- 실패한 프레임은 **건너뛰고 개수를 보고**한다(M4 — 성공 오인 금지):
  `"{n}개를 내보냈습니다."` / 부분 실패면 `"{n}개를 내보냈고 {m}개는 이미지를 읽지 못했습니다."`
- 권한: 렌더·액션 모두 `!isGuest`. 게스트는 개인 프레임이 없고 공용 백업은 운영자 작업이다.

### 9.3 프레임 가져오기

```ts
export type FrameImportRejection =
  | "not-logged-in" | "no-write-permission" | "malformed-zip"
  | "no-entries" | "limit-reached" | "compression-unsupported";

export interface FrameImportCandidate {
  readonly name: string;                 // 저장될 이름(충돌 회피 적용 후)
  readonly sourceName: string;           // zip 안의 이름
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  readonly renamed: boolean;
}
export interface FrameImportPreview {
  readonly candidates: readonly FrameImportCandidate[];
  readonly warnings: readonly string[];
}
export type FrameImportPreviewResult =
  | { ok: true; preview: FrameImportPreview }
  | { ok: false; reason: FrameImportRejection };

export async function previewFrameImport(file: File, deps: …): Promise<FrameImportPreviewResult>;
export async function applyFrameImport(preview: FrameImportPreview, deps: …): Promise<{ imported: number; failed: number }>;
```

**설정 내보내기/가져오기와 같은 형태다: 파싱 → 미리보기 → [적용]**(F10). 즉시 덮어쓰지 않는다.

| 규칙 | 내용 |
|------|------|
| 권한 | `canWriteFrames(role)` — 렌더 가드 + `previewFrameImport` **첫 줄** 가드 |
| 저장 스코프 | **항상 개인**(`scope:"user"`, `ownerId = currentUser.id`). 공용 로컬 저장은 power 전용 + 서버 등록 축과 얽혀 있고(Step 15 §5), 10개 상한도 개인 기준이다(05 §4.8) |
| 이름 | zip 파일명의 확장자를 뗀 base. **자기 계정 접두(`{myId}_`)만 제거**한다 — 남의 접두는 이름의 일부일 수 있다 |
| 이름 검증 | `validateFrameName`(1~100자 + 금지문자). 실패 항목은 **건너뛰고 경고**에 담는다 |
| 충돌 | 기존 개인 프레임 이름과 겹치면 `nextCopyName(base, existing, uniqueSuffix)`(도메인) — `renamed: true` |
| 상한 | `exceedsLocalFrameLimit(현재 개수 + 이미 적용한 수)`를 **한 건마다** 재평가. 초과 시 그 지점에서 중단하고 개수를 보고 |
| 슬롯 좌표 | `.slots`의 `#imagesize`와 **디코딩한 PNG 실제 크기**가 다르면 `rescaleSlots(slots, png.width / slots.width, png.width, png.height)`(F24). 같으면 그대로 |
| `#dbid` | **버린다.** 개인 스코프 저장은 `dbId: null`이 규약이다(05 §4.4 — 서버 문서와 연결을 끊는다) |
| 압축 | method 8이면 `DecompressionStream("deflate-raw")`. **런타임 감지**하고 없으면 `compression-unsupported`: `"압축된 zip은 이 브라우저에서 읽을 수 없습니다. 압축 없이 저장한 zip을 사용해 주세요."` |
| 저장 | `frameStore.saveLocal({scope:"user", ownerId, name, dbId:null, imageSize, slots, bytes})` — **새 저장 경로를 만들지 않는다** |
| PNG 디코딩 | `createImageBitmap(blob)` → `width/height` 확인 후 **반드시 `close()`**(WR8). 디코딩 실패 항목은 건너뛴다 |

### 9.4 로그 내보내기

§7.4 참조. 진단 모달과 설정 §6 **양쪽**에 두지 않는다 — **진단 모달에만** 둔다(03 §15.2가 로그 섹션의
버튼으로 규정한다). 설정 §6에는 [프레임 내보내기]/[가져오기]와 [앱 업데이트 확인]만 추가한다.

---

## 10. 문구 카탈로그 추가 (`ui/strings.ts`)

`analysis/13 §14`에 대응 항목이 있는 문구는 **문자열 일치**로 넣는다(§ 표시).

```
account: { title, tabInfo, tabAdmin, id, email, authMethod, role, createdAt, unknown("알 수 없음"§),
           authMethodGoogle("Google SSO"§), changePin, pinCurrent, pinNew, pinConfirm,
           pinChanged("PIN을 변경했습니다."), pinCurrentWrong("현재 PIN이 올바르지 않습니다."§),
           adminTitle, openUserMgmt, globalLimits, qrHours, qrCount, limitsSaved, limitsRange,
           limitsLoadFailed("현재 한도를 불러올 수 없습니다."), logoutDone("로그아웃했습니다.") }

userMgmt: { title, total("총 {n}명"), colId, colEmail, colRole, colCreatedAt, colActions,
            resetPin("PIN"), loadFailed("사용자 목록을 불러올 수 없습니다."§),
            deleteConfirm("'{n}' 계정을 삭제할까요? 소유 프레임도 함께 삭제됩니다."),
            deleted("{n} 삭제됨(소유 프레임 포함)."§), roleChanged("역할을 변경했습니다."),
            pinResetTitle, pinResetDone("PIN을 재설정했습니다."), notFound("대상 계정을 찾을 수 없습니다."),
            back("뒤로") }

diagnostics: { title, sections: {camera, encoder, server, logStorage, contact, app},
               cameraCount, cameraPermission, cameraResolution, cameraFps, processedSize,
               encoderPath, encoderCodec, encoderNotProbed("아직 판정 전(촬영 후 표시)"),
               encoderNone("미지원"), bucket, currentAccount, guest("게스트"),
               logCount("{n}건"), logRange, exportLogs("로그 내보내기"),
               exportLogsFailed("로그를 내보내지 못했습니다."),
               sessionLeftovers, storedResults, frameCacheUsage,
               developer("개발자"), developerEmail("devmcjo@gmail.com"), copy("복사"),
               copied("복사했습니다."), copyFailed("복사할 수 없습니다. 주소를 길게 눌러 복사해 주세요."),
               version, buildDate, webDeployDate }

pwa: { swActive("최신 상태"), swWaiting("업데이트 대기 중"), swUnsupported("미지원"),
       swDisabled("개발 모드(등록 안 함)"), swFailed("등록 실패"),
       applyNow("지금 적용"), applyCaption("적용하면 앱이 새로 시작되고 로그인이 해제됩니다."),
       applyBlocked("촬영이 끝난 뒤 적용할 수 있습니다."),
       checkUpdate("앱 업데이트 확인"), upToDate("최신 버전입니다."),
       installed("설치됨"), notInstalled("브라우저에서 실행 중") }

transfer: { exportFrames("프레임 내보내기"), importFrames("프레임 가져오기"),
            exportedFrames("{n}개를 내보냈습니다."),
            exportedPartial("{n}개를 내보냈고 일부는 이미지를 읽지 못했습니다."),
            importPreviewTitle("가져올 프레임"), importRenamed("이름 변경됨"),
            importApply("지금 적용"), importCancel("가져오기 취소"),
            importDone("{n}개를 가져왔습니다."),
            malformedZip("zip 파일을 읽을 수 없습니다."),
            noEntries("가져올 프레임이 없습니다."),
            compressionUnsupported("압축된 zip은 이 브라우저에서 읽을 수 없습니다. 압축 없이 저장한 zip을 사용해 주세요."),
            noWritePermission("프레임을 가져올 권한이 없습니다.") }

kiosk: { exit(기존), exitConfirm("키오스크를 종료할까요? 로그아웃되고 처음 화면으로 돌아갑니다."),
         exitNotice("키오스크를 종료했습니다. 브라우저(또는 앱)를 직접 닫아 주세요.") }
```

⚠️ `STRINGS.common.apply`("지금 적용")·`STRINGS.kiosk.exit`가 **이미 있다**(F23) — 중복 키를 만들지 마라.

---

## 11. 파일별 역할과 시그니처 (요약)

### 11.1 도메인 (순수 · node 테스트 · `domain/index.ts` 배럴에 추가)

| 파일 | export |
|------|--------|
| `accounts/accountAdminPolicy.ts` | `canOpenUserMgmt` · `canEditGlobalLimits` · `canExitKiosk` · `sortManagedUsers` · `buildUserRow` · `buildUserRows` · `UserRowPolicy` |
| `accounts/tempUserLimitsPolicy.ts` | `MIN_QR_HOURS`·`MAX_QR_HOURS`·`MIN_QR_COUNT`·`MAX_QR_COUNT` · `parseLimitInput` · `validateTempUserLimits` · `buildLimitsPatch` · `LimitsRejection` |
| `accounts/sessionUser.ts`(수정) | `authMethodLabel` 값 정정(§3.1) |
| `auth/pinGatePolicy.ts`(수정) | `pinGateGroup` 추가 |

### 11.2 어댑터 (예외 미전파)

| 파일 | export |
|------|--------|
| `storage/zipStore.ts` | `crc32` · `buildStoreZip` · `parseZipEntries` · `ZipInputEntry` · `ParsedZipEntry` |
| `storage/exportImport.ts` | `frameZipFileName` · `frameEntryBaseName` · `dedupeEntryNames` · `exportFrames` · `previewFrameImport` · `applyFrameImport` · `logExportFileName` · `exportLogs` (+ 각 `defaultXxxDeps`) |
| `storage/frameStore.ts`(수정) | `readImageBytes(frame)` |
| `platform/swPolicy.ts` | `classifySwRequest` · `PRECACHE_STABLE_URLS` · `isCacheableResponse` · `SwRoute` |
| `platform/clipboard.ts` | `copyText(text): Promise<boolean>` — 미지원·거부는 `false` |
| `platform/appInstall.ts` | `isStandaloneDisplay(): boolean` |

### 11.3 셸

| 파일 | export |
|------|--------|
| `shell/swUpdate.ts` | `installServiceWorker` · `useSwState` · `swStateStore` · `checkForUpdate` · `applyWaitingUpdate` |
| `shell/accountModeIntent.ts` | `writeAccountModeIntent` · `readAccountModeIntent`(**비파괴**) · `AccountMode` |
| `shell/pinGate.ts`(수정) | 게이트 키를 `pinGateGroup`으로 |

### 11.4 화면 로직 (React 무관 · node 테스트)

`account/accountMenu.ts` · `account/accountInfoRows.ts` · `account/pinChangeRunner.ts` ·
`account/adminLimitsForm.ts` · `account/kioskExit.ts` ·
`userMgmt/userListRunner.ts` · `userMgmt/userActions.ts` · `userMgmt/pinResetRunner.ts` ·
`modals/diagnostics/diagnosticsPresenter.ts` · `settings/frameTransfer.ts`

### 11.5 UI · 배선

`ui/views/AccountView.tsx` · `ui/views/UserMgmtView.tsx` · `ui/components/PinKeypad.tsx` ·
`ui/components/fields.tsx`(+`Select`) · `ui/components/index.tsx`(TopBar 팝오버) ·
`screens/modals/diagnostics/DiagnosticsModal.tsx` · `App.tsx`(라우팅 3곳 · ModalStack) ·
`main.tsx`(부트스트랩 7단계) · `ui/views/SettingsView.tsx`(§5 진단 버튼 · §6 프레임 전송·업데이트 확인)

---

## 12. 데이터 흐름 시나리오

### 12.1 manager가 다른 manager 행을 본다 (핵심 관측)

```
TopBar 계정 버튼 → 팝오버 [관리자 도구]
  writeAccountModeIntent("admin") → go("Account")
  <PinGate screen="Account"> → ensureScreenPinGate → pinGateGroup("Account")="Account"
     hasPin → verify 모달 → 200 → granted
  AccountView(mode=admin) → [사용자 관리]  (canOpenUserMgmt(manager)=true)
     go("UserMgmt")
  <PinGate screen="UserMgmt"> → pinGateGroup("UserMgmt")="Account" → **이미 granted → 즉시 렌더**
  loadUserList → buildUserRows(actor=manager, users)
     대상 manager 행: canDelete=true (canManage 동급 허용)
                      canResetPin=false (엄격히 낮은 위계만)   ← [PIN] 미렌더
                      assignableRoles=[]  (manager 대상은 admin만 가능) ← 콤보 미렌더
     대상 admin 행:   canDelete=false, canResetPin=false, assignableRoles=[]
     자기 행:         전부 false / []
  [뒤로] → writeAccountModeIntent("admin") → go("Account") → 그룹 동일 → **PIN 재요구 없음**
```

### 12.2 오프라인에서 앱이 뜬다

```
1회차(온라인): register("/sw.js") → install
   PRECACHE_STABLE_URLS + __MCPHOTO_PRECACHE__.assets 개별 add(allSettled)
   → activate → 옛 캐시 삭제 → clients.claim()   ← 현재 탭도 즉시 제어된다
2회차(오프라인): navigate → network 실패 → caches.match("/index.html") → 셸 렌더
   /assets/*.js → immutable → 캐시 히트
   /branding.json → fresh → 네트워크 실패 → 캐시 폴백
   GET {backend}/health → **bypass**(cross-origin) → 실패 → 진단 "도달 실패"
```

### 12.3 새 배포가 나왔다

```
탭 유지 중 → registration.update()(설정 [앱 업데이트 확인]) 또는 브라우저 자동 검사
  → 새 sw.js 바이트 다름(자산 목록 인라인 — §8.2) → install → **waiting**
  → swStateStore.status = "waiting" → 진단 [앱] 섹션에 "업데이트 대기 중" + [지금 적용]
  → 촬영 중이면 [지금 적용] 미노출
  → 누르면 postMessage(MCPHOTO_APPLY_UPDATE) → skipWaiting → activate → controllerchange(1회) → reload
  → 누르지 않으면 모든 탭이 닫힌 뒤 다음 시작에 적용
```

---

## 13. 테스트 계획

| 파일 | 무엇을 고정하나 |
|------|-----------------|
| `tests/unit/domain/accounts.test.ts` | `sortManagedUsers`(빈 `createdAt` 맨 뒤 포함) · `buildUserRow` **전 역할 쌍**(manager→manager: delete○ / pin✕ 명시) · `canOpenUserMgmt`·`canEditGlobalLimits` · `parseLimitInput`·`validateTempUserLimits`·`buildLimitsPatch` · `pinGateGroup` · `authMethodLabel` 3케이스 |
| `tests/unit/accounts/accountMenu.test.ts` | 게스트 `[]` · user 2항목 · manager 3항목 |
| `tests/unit/accounts/pinChangeRunner.test.ts` | 확인 불일치는 **서버 왕복 0회** · 401 → `currentWrong` · 성공 시 `markPinSet` 1회 |
| `tests/unit/accounts/kioskExit.test.ts` | 비power → `false` + 부수효과 0 · 순서(fullscreen→logout→home→toast) · `window.close` **미호출** |
| `tests/unit/accounts/adminLimitsForm.test.ts` | 비admin 첫 줄 차단 · 범위 밖은 서버 미호출 · no-change는 미호출 · 응답값 재반영 |
| `tests/unit/userMgmt/userListRunner.test.ts` | 403 → `failed:"forbidden"`(**빈 배열 아님**) · 취소 → `cancelled` |
| `tests/unit/userMgmt/userActions.test.ts` | ACC-2 경로: 권한 없으면 `deps.deleteAccount` **미호출** · no-op 역할 변경 미전송 |
| `tests/unit/userMgmt/pinResetRunner.test.ts` | 동급 대상 차단(서버 미호출) · 자기 자신 차단 · 형식/불일치 · **PIN이 결과·로그에 없음** |
| `tests/unit/screens/diagnostics.test.ts` | 6섹션 생성 · `encoderProbe()` null → "아직 판정 전" · 권한 조회 throw → "알 수 없음" · **게이트 키 값 문자열이 어떤 행에도 없음**(주입한 키를 스냅샷 전체에서 검색) |
| `tests/unit/storage/swPolicy.test.ts` | 9단 판정 순서 전수 · cross-origin/POST/`/uploads` → bypass · 미지의 경로 → bypass · `isCacheableResponse` |
| `tests/unit/storage/precacheManifest.test.ts` | `collectPrecacheAssets`(`.map`·`sw.js` 제외 · 정렬) · `precacheBuildId` 결정성(같은 입력 = 같은 해시, 순서 무관) |
| `tests/unit/storage/zipStore.test.ts` | `buildStoreZip` → `parseZipEntries` 왕복 · crc32 알려진 벡터 · 손상 EOCD → `[]` · 디렉터리/`..` 항목 제외 |
| `tests/unit/storage/frameTransfer.test.ts` | 파일명 규약(공용/개인·중복 `-2`) · `.slots` 본문이 `serializeSlotsFile`와 동일 · 이미지 실패 건너뛰기 · 가져오기 권한/상한/충돌/`rescaleSlots` |
| `tests/unit/shell/swUpdate.test.ts` | dev → `disabled`(register 미호출) · waiting 감지 · [지금 적용]이 `postMessage` 1회 · `controllerchange` 2회여도 reload 1회 · 촬영 중 차단 |
| `tests/unit/accounts/accountInvariants.test.ts` | **ACC-1~4 · SW-1~3 · DIAG-1** (소스 정적 검사) |
| `tests/unit/settings/settingsInvariants.test.ts`(수정) | `PIN_FILES`에 `pinResetRunner.ts`·`pinChangeRunner.ts`·`PinKeypad.tsx` 추가 → PIN-1이 자동 확장 |

기준선은 **W1에서 실측**한다(A7). 이후 모든 단계에서 `npx vitest run`의 통과 수가 **증가만** 해야 한다.

---

## 14. `docs/web-client/15-implementation-conventions.md` 갱신 지침 (developer 수행)

> ⚠️ **다른 Step 서술을 stale로 만들지 마라.** §6의 Step 9~15 절은 **손대지 않는다.**
> 아래 3곳만 바꾼다.

### 14.1 §3.4 불변식 표 — 행 추가 (기존 행 수정 금지)

| 불변식 | 검사 |
|--------|------|
| **ACC-1** 계정·사용자 관리 화면에 역할 문자열 리터럴·`.role ===` 비교 0건 | `accountInvariants.test.ts` — 판정은 `accountAdminPolicy`가 소유한다. 화면이 비교하면 서버 매트릭스와 조용히 갈라진다 |
| **ACC-2** `runDeleteAccount`·`runSetRole`·`runPinReset`이 **첫 실행문에서** 도메인 판정을 부른다 | 동상(FR-10과 같은 등장 순서 검사) — 가드가 뒤로 밀리면 서버 왕복이 먼저 일어난다 |
| **ACC-3** `screens/account/*`·`screens/userMgmt/*`에 `pushModal(` 0건 | 동상 — PIN 재설정·삭제 확인·키오스크 종료는 전부 **화면 로컬 오버레이**다(FR-5·FR-8 계열) |
| **ACC-4** `App.tsx`의 `Account`·`UserMgmt` 케이스가 둘 다 `<PinGate`로 감싸져 있다 | 동상 — 새 보호 화면의 게이트 누락 방지 |
| **SW-1** `sw.ts`의 `install` 리스너에 `skipWaiting` 0건 + 파일 전체 등장 1회(`message` 핸들러) | 동상 — 자동 갱신이 되살아나면 **촬영 중 앱이 바뀐다** |
| **SW-2** `sw.ts`에서 `classifySwRequest(`가 `respondWith(`보다 먼저 등장 | 동상 — 분류를 건너뛴 `respondWith`는 API 응답까지 캐시한다 |
| **SW-3** `sw.ts`에 `logger` import·`console.` 0건 | 동상 — SW 로그는 진단에 도달하지 않는다(§4 함정 #12) |
| **DIAG-1** 진단·뷰 파일에서 `backendApiKey`가 등장하는 줄에 `.length`·`.trim()`이 반드시 있다 | 동상 — 게이트 키 **값**이 화면·로그에 새는 것을 막는다 |
| **PIN-1**(확장) `PIN_FILES`에 `pinResetRunner.ts`·`pinChangeRunner.ts`·`PinKeypad.tsx` 추가 | `settingsInvariants.test.ts` |

### 14.2 §4 함정 표 — 행 추가

| # | 함정 | 교훈 |
|---|------|------|
| 13 | `cache.addAll`은 **원자적**이라 URL 하나가 404면 SW install 전체가 실패한다 | 존재하지 않을 수 있는 자산(`/sounds/shutter.wav`)이 섞이면 첫날부터 깨진다 → 개별 `cache.add` + `allSettled` |
| 14 | `sw.js` 바이트가 같으면 브라우저가 **업데이트를 감지하지 않는다** | 자산 목록을 sw.js에 인라인해 내용이 바뀌면 파일도 바뀌게 만든다(빌드 타임스탬프는 no-op 재빌드를 churn시킨다) |
| 15 | PIN 승인이 **화면 단위**라 `Account ↔ UserMgmt` 왕복마다 PIN을 다시 물었다 | 승인 단위는 화면이 아니라 **그룹**이다(`pinGateGroup`). 하위 페이지를 새로 만들면 그룹에 넣는다 |
| 16 | `authMethodLabel`이 호출자 0인 채로 규격과 다른 문구("Google 계정")를 들고 있었다 | **렌더된 적 없는 헬퍼는 "현행 동작"이 아니다** — 우선순위 규칙(소스 > analysis)을 적용할 대상이 아니다 |

### 14.3 §7 "지금 상태 요약" — Step 17만 남은 상태로 교체

기존 표를 아래로 바꾸고, 마지막 문단도 함께 바꾼다.

```markdown
| 항목 | 값 |
|------|-----|
| 완료 | WBS Step 0~8 + 8.5 + 9 + 10 + 11(★마일스톤 A) + 12 + 13 + 14 + 15
        + **16**(계정·사용자 관리·진단 모달·PWA/SW·내보내기/가져오기) + 서버 B1·B2·B4 + 사용자 액션 A1~A5 |
| 테스트 | 웹 **{W11에서 실측한 수}**({파일 수}파일) · 서버 316 · Windows 938
          (후자 둘은 Step 12 시점 실측값. Step 13~16은 `docs/spec-vectors/`·서버·WPF 코드를 **무변경**이라 재실행 의무가 없다) |
| 브랜치 | `feature/web-client-foundation` |
| `main` | 머지 완료(2026-07-31, `e5efdfd`) |
| 미완 | **Step 17(E2E·실기기·수락)뿐**. 실측 V1~V25 |
```

마지막 문단 교체:

```markdown
**13개 화면이 전부 실물이다** — `App.tsx`의 `ScreenRouter`에 `DummyScreen`으로 남은 상태가 **0개**이고,
`ModalStack`의 미구현 스텁 분기도 사라졌다(셸 모달 4종 전부 실물). 남은 것은 **Step 17(E2E·실기기·수락)**뿐이다.
`DummyScreen` 함수 자체는 라우터의 `default` 분기 안전망으로만 남는다 — **여기에 기능 진입점을 두지 마라.**
```

### 14.4 §1 "30초 재개 절차" — 다음 Step 표

`| **Step 14 프레임 저장소·선택** | … |` 3행을 `| **Step 17 E2E·실기기** | Step 1~16 전부 + 실기기 3대(사람). Playwright 도입도 이 Step이다 |` 한 행으로 줄인다.

### 14.5 그 밖의 문서 갱신 (§3의 판정 반영)

| 문서 | 변경 |
|------|------|
| `web-client/01 §2.2`(:90) · `03 §1.3`(:57) · `10 §5`(:49) · `15 §6`(:285) | `roles/rolePolicy.ts` → **`roles/userRole.ts`**, `rolePolicy.test.ts` → `tests/unit/domain/settingsAndRoles.test.ts`(§3.2) |
| `analysis/13 §9.2` | "웹 클라이언트는 6섹션(개발자 문의·앱 추가) — `web-client/03 §15.2`" 한 줄 추가(§3.4) |
| `web-client/11-wbs.md` Step 16 | 체크박스 완료 + 산출물·검증 수치·설계 이탈 기재 |
| `web-client/14 §10.11` | **V25** 신설(아래) |
| `docs/design/README.md` §3.1 | 이 문서 등재(작성 시 함께 수행) |

**V25 · Step 16 실측(신설 — 브라우저·실계정·Windows 앱 필요)**

| # | 확인 | 방법 | 통과 기준 | 전제 |
|---|------|------|-----------|------|
| V25-1 | 오프라인에서 앱이 로드된다 | 배포본 1회 방문 → DevTools Offline → 새로고침 | 셸이 렌더된다. 진단 서버 섹션은 "도달 실패" | 배포 |
| V25-2 | [지금 적용]이 새 SW를 활성화한다 | 재배포 → 탭 유지 → 진단 [앱] "업데이트 대기 중" → [지금 적용] | 새로고침 뒤 새 버전. 촬영 중에는 버튼이 없다 | 배포 2회 |
| V25-3 | 프레임 zip을 Windows `Frame\`에 풀면 인식된다 | 내보내기 → 압축 해제 → MC포토 실행 | 프레임 목록에 나타나고 슬롯이 맞다 | Windows 앱 |
| V25-4 | 탐색기로 다시 압축한 zip을 가져올 수 있다 | 위 폴더를 우클릭 압축 → [프레임 가져오기] | 미리보기에 항목이 뜬다. 미지원 브라우저면 전용 안내 문구 | 동상 |
| V25-5 | manager 실계정에서 [PIN] 미노출·[삭제] 노출 | manager 로그인 → 사용자 관리 | 다른 manager 행에 [PIN] 없음·[삭제] 있음. 콤보에 `admin` 없음 | manager 실계정 |
| V25-6 | [선택 편집] 진입이 운영 CSP에서 성공한다(A1) | 배포본에서 서버 공용 프레임 [선택 편집] | 이미지가 뜬다. 실패 시 콘솔의 CSP 위반 지시자를 확인 | 배포 |
| V25-7 | Lighthouse PWA 감사가 통과한다 | Chrome DevTools → Lighthouse → PWA | Installable + manifest·SW 항목 통과 | 배포 |
| V25-8 | 진단 모달에 게이트 키 값이 없다 | 진단 열기 → 화면·`.log` 내보내기 파일 전문 검색 | 키 문자열이 **0건** | 배포 |

---

## 15. 구현 단계 (WBS 블루프린트)

> 형식은 [`docs/templates/WBS_BLUEPRINT.md`](../templates/WBS_BLUEPRINT.md). 각 단계는 **self-contained** —
> 이 문서의 해당 절만 읽고 실행할 수 있어야 한다.
> **공통 검증 명령**(모든 단계에서 실행): `cd E:\Study\photobooth\webclient`
> `npx tsc --noEmit` → `npx vitest run` → `npx vite build`

### W1: 도메인 판정 2파일 + 기준선 실측
- **Context Brief**: 계정·사용자 관리 화면이 쓸 권한 판정을 도메인 순수 함수로 만든다. `isPower`·`canManage`·`canResetPin`·`assignableRoles`는 **이미 `domain/roles/userRole.ts`·`roleChangePolicy.ts`에 있고 `docs/spec-vectors/role-matrix.json`이 고정한다 — 다시 만들지 말고 조합만 한다**(설계 §4.1). `canManage`는 동급 허용, `canResetPin`은 동급 차단이다.
- **대상 파일**: `src/domain/accounts/accountAdminPolicy.ts`(신규) · `src/domain/accounts/tempUserLimitsPolicy.ts`(신규) · `src/domain/index.ts` · `tests/unit/domain/accounts.test.ts`(신규)
- **선행 조건**: 없음
- **구현 내용**: §4.1·§5.4.1의 시그니처 그대로. `sortManagedUsers`는 위계 내림차순 → `createdAt` 오름차순이며 **빈 `createdAt`은 맨 뒤**. `buildUserRow`의 `canDelete`에 `isPower`를 **함께** 걸고 `canResetPin`에는 **중복해서 걸지 않는다**. 배럴에 `export *` 2줄 추가.
- **검증 명령**: `npx vitest run` (먼저 **현재 통과 수를 기록**한다 — A7 기준선) · `npx vitest run tests/unit/domain/purity.test.ts` · `npx vitest run tests/unit/domain/vectors.test.ts`
- **완료 기준**:
  - [관측] `buildUserRow(manager, manager)`가 `{canDelete:true, canResetPin:false, assignableRoles:[]}`를 낸다. `vectors.test.ts`(role-matrix 포함)가 그대로 통과한다.
  - [non-goal] `userRole.ts`·`roleChangePolicy.ts`·`docs/spec-vectors/*`가 **한 글자도 바뀌지 않는다**. 도메인 순수성 테스트가 새 파일을 자동 포함하고 통과한다.
  - [trigger] 판정은 함수 호출로만 — 모듈 로드 부작용 0.
- **롤백**: 신규 2파일 삭제 + 배럴 2줄 되돌림.
- [ ] 완료

### W2: PIN 게이트 그룹 (`Account ↔ UserMgmt`)
- **Context Brief**: PIN 승인은 지금 **화면 단위**라 `Account → UserMgmt → 뒤로`에서 PIN을 두 번 더 묻는다. 07 §6.1은 "계정 관리/관리자 도구 = 진입 시 1회 판정"이고 03 §14의 [뒤로]는 `Account` 직행이다. 승인 단위를 **그룹**으로 바꾼다(설계 §5.5). ⚠️ `Settings`는 "매번 확인"이므로 그룹에 넣지 않는다.
- **대상 파일**: `src/domain/auth/pinGatePolicy.ts` · `src/shell/pinGate.ts` · `tests/unit/settings/pinGatePolicy.test.ts` · `tests/unit/settings/pinGate.test.ts`
- **선행 조건**: 없음(W1과 병렬 가능)
- **구현 내용**: `pinGateGroup(screen)` 추가(`UserMgmt → Account`, 그 외 자기 자신). `ensureScreenPinGate`가 그룹을 저장, `usePinGateStatus`가 그룹으로 비교, `installPinGateLifecycle`의 화면 구독이 **그룹이 바뀔 때만** 폐기. ⚠️ `<PinGate>`의 effect에 cleanup을 추가하지 마라(StrictMode 함정 — 15 §6).
- **검증 명령**: `npx vitest run tests/unit/settings` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `Account`에서 granted인 상태로 화면을 `UserMgmt`로 바꿔도 `usePinGateStatus("UserMgmt")`가 `granted`다.
  - [non-goal] `Settings → Account`, `Account → Settings`에서는 여전히 승인이 폐기된다. `Settings` 자체의 "매번 확인"이 유지된다. PIN-3·PIN-4·PIN-5 정적 검사가 계속 통과한다.
  - [trigger] 승인 폐기는 **그룹 변경** 또는 `currentUser` 변경에서만.
- **롤백**: `pinGateGroup` 제거 + `pinGate.ts` 3곳 되돌림.
- [ ] 완료

### W3: 문구 카탈로그 + UI 프리미티브
- **Context Brief**: Step 16이 쓸 문구를 `ui/strings.ts` 한 곳에 모으고(01 §8 — `analysis/13 §14`와 1:1), 공용 컨트롤 2개를 준비한다. `PinKeypad`는 `PinPromptModal`에서 **표현만** 뽑아낸다 — 판정·서버 왕복·잠금 기록은 옮기지 않는다(PIN 경로 회귀 금지).
- **대상 파일**: `src/ui/strings.ts` · `src/ui/components/PinKeypad.tsx`(신규) · `src/ui/components/fields.tsx`(+`Select`) · `src/screens/modals/pinPrompt/PinPromptModal.tsx` · `src/ui/components/components.module.css`
- **선행 조건**: 없음
- **구현 내용**: §10의 키를 추가한다(⚠️ `common.apply`·`kiosk.exit`는 **이미 있다** — 중복 금지). `PinKeypad`는 `{value, disabled, onDigit, onBackspace, onSubmit, submitLabel}` props만 받는 표현 컴포넌트다. `Select`는 네이티브 `<select>`(최소 48px · `aria-label` · `disabled`) — 행마다 4버튼을 깔면 표가 무너지고, 사용자 관리는 손님이 아닌 운영자 화면이라 `ChoiceGroup`(터치 우선)의 근거가 적용되지 않는다.
- **검증 명령**: `npx vitest run tests/unit/settings` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `pinPromptRunner.test.ts`·`pinGate.test.ts`·`settingsInvariants.test.ts`가 전부 통과한다(PIN 경로 무회귀).
  - [non-goal] `PinPromptModal`의 상태·서버 호출·`resolvePinPrompt` 호출 지점이 **변하지 않는다**. `ChoiceGroup`·`Toggle`의 동작이 변하지 않는다.
  - [trigger] `PinKeypad`는 props 콜백으로만 동작 — 내부에 서버 호출·`logger` 없음.
- **롤백**: `PinKeypad` 인라인 복귀 + 문구 키 제거.
- [ ] 완료

### W4: `Account` 화면(내 정보 + PIN 변경) + 계정 메뉴 팝오버
- **Context Brief**: `Account`는 지금 `DummyScreen`이고(`App.tsx:192-197`) 앱에 **로그아웃 경로가 없다**. 내 정보는 로그인 응답 `user` DTO가 유일한 출처다(별도 조회 API 없음 — `analysis/31 §10`). PIN 변경은 **기존 `runPinAttempt`에 `currentPin`을 넘겨** 재사용한다(설계 §5.3) — 새 서버 왕복을 쓰면 `unauthorized:"reject"`가 빠져 PIN 1회 오입력이 로그아웃을 유발한다(E17).
- **대상 파일**: `src/shell/accountModeIntent.ts`(신규) · `src/screens/account/{accountMenu,accountInfoRows,pinChangeRunner,useAccountScreen}.ts`(신규) · `src/ui/views/AccountView.tsx`·`account.module.css`(신규) · `src/ui/components/index.tsx`(TopBar) · `src/App.tsx` · `src/domain/accounts/sessionUser.ts` · 테스트 3파일
- **선행 조건**: W1 · W2 · W3
- **구현 내용**: §5.1~§5.3·§5.6·§3.1. `authMethodLabel`을 `"google" → "Google SSO"` / 그 외 `"알 수 없음"`으로 고치고 `"password"` 케이스를 **삭제**한다(§3.1의 판정 근거를 주석으로 남긴다). `readAccountModeIntent()`는 **비파괴**. TopBar는 `accountMenuItems`·`onAccountMenuSelect`를 props로 받고 권한 판정은 `App.tsx`가 `buildAccountMenuItems`로 한다. `App.tsx`의 `case "Account"`를 `<PinGate screen="Account"><AccountView /></PinGate>`로 교체.
- **검증 명령**: `npx vitest run` · `npx tsc --noEmit` · `npx vite build`
- **완료 기준**:
  - [관측] 로그인 상태에서 계정 버튼을 누르면 팝오버 항목이 뜨고(user 2개 · manager 3개), [계정 관리]가 PIN 게이트를 지나 내 정보 5행을 보여준다. 로그인 방식이 **"Google SSO"** 다.
  - [non-goal] 게스트는 팝오버 없이 곧바로 `Login`으로 간다. **비밀번호 변경·계정 생성 UI가 없다.** `Account` 진입이 서버를 조회하지 않는다. 화면 전환 시 팝오버가 자동으로 닫힌다.
  - [trigger] 로그아웃은 팝오버 [로그아웃]에서만 — 토큰 폐기는 M1 구독이 하고 버튼이 직접 지우지 않는다.
- **롤백**: `case "Account"`를 `DummyScreen`으로 되돌리고 TopBar props 원복.
- [ ] 완료

### W5: `Account`/Admin 모드 — 사용자 관리 진입 · 전역 한도 · 키오스크 종료
- **Context Brief**: 관리자 도구는 `Account` 화면의 두 번째 모드다(별 화면이 아니다). 항목 3개의 게이트가 **서로 다르다**: 사용자 관리 진입=power · 전역 한도=**admin만** · 키오스크 종료=power. [앱 종료]는 **만들지 않는다**(WD5 — 브라우저 탭을 스크립트로 닫을 수 없다).
- **대상 파일**: `src/screens/account/{adminLimitsForm,kioskExit}.ts`(신규) · `src/screens/account/useAccountScreen.ts` · `src/ui/views/AccountView.tsx` · 테스트 2파일
- **선행 조건**: W4
- **구현 내용**: §5.4. `adminLimitsForm`·`kioskExit`의 **첫 실행문이 권한 가드**다. `tempUserLimitsService`는 예외를 던지므로 판별 유니온으로 접는다. 조회 실패를 `DEFAULT_TEMP_USER_LIMITS`로 위장하지 않는다. `runKioskExit`는 전체화면 해제 → 로그아웃 → 홈 복귀 → 안내 토스트 순서이며 **`window.close()`를 부르지 않는다**. 확인은 인라인 2단.
- **검증 명령**: `npx vitest run tests/unit/accounts` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] manager 로그인 시 [사용자 관리]·[키오스크 종료]는 보이고 **전역 한도 편집은 보이지 않는다**. admin은 셋 다 보인다. advanced_user는 관리자 도구 탭 자체가 없다.
  - [non-goal] **[앱 종료] 버튼이 없다**(WD5). `window.close` 문자열이 소스에 0건. 범위를 벗어난 한도 값은 서버로 전송되지 않는다. 한도 조회 실패가 기본값(48/30)으로 표시되지 않는다.
  - [trigger] 키오스크 종료는 2단 확인의 두 번째 클릭에서만. 한도 저장은 [저장] 클릭에서만(입력 중 즉시 반영 없음).
- **롤백**: Admin 탭 렌더 제거 + 신규 2파일 삭제.
- [ ] 완료

### W6: `UserMgmt` 화면
- **Context Brief**: power 전용 사용자 관리. **목록 조회 실패를 빈 목록으로 폴백하지 않는다**(03 §14). 행 액션 3종의 게이트가 다르다 — 삭제는 **동급 허용**, PIN 재설정은 **동급 차단**(`analysis/60 §1.3.1`). 판정은 W1의 `buildUserRows`가 소유하고 화면은 역할 문자열을 비교하지 않는다.
- **대상 파일**: `src/screens/userMgmt/{userListRunner,userActions,pinResetRunner,useUserMgmtScreen}.ts`(신규) · `src/ui/views/UserMgmtView.tsx`·`userMgmt.module.css`(신규) · `src/App.tsx` · 테스트 3파일 · `tests/unit/settings/settingsInvariants.test.ts`(PIN_FILES 확장)
- **선행 조건**: W1 · W2 · W3
- **구현 내용**: §6 전체. `App.tsx`의 `case "UserMgmt"`를 `<PinGate screen="UserMgmt"><UserMgmtView /></PinGate>`로 추가. 행 액션 UI는 표·카드가 **같은 컴포넌트**를 공유한다. 삭제·PIN 재설정 확인은 **화면 로컬 오버레이/인라인**이고 `pushModal`을 부르지 않는다. `resetOtherPin`에 `unauthorized:"reject"`를 넘기지 않는다(PIN-2b).
- **검증 명령**: `npx vitest run` · `npx tsc --noEmit` · `npx vite build`
- **완료 기준**:
  - [관측] manager 로그인 시 다른 manager 행에 **[PIN]이 없고 [삭제]는 있다**. admin 행에는 둘 다 없다. 역할 콤보에 `admin`이 없다. 자기 행에는 콤보·액션이 없다. 목록은 위계 내림차순·가입일 오름차순으로 정렬된다.
  - [non-goal] **목록 조회 실패가 빈 목록으로 표시되지 않는다**(오류 문구 `"사용자 목록을 불러올 수 없습니다."`). 403이 와도 화면이 비지 않는다. 좁은 화면 카드에서도 [PIN] 노출 규칙이 동일하다. 가로 스크롤이 생기지 않는다. PIN 값이 로그·반환값에 0건.
  - [trigger] 삭제는 인라인 2단 확인의 두 번째 클릭에서만. 역할 변경은 **값이 실제로 달라질 때만** 서버로 전송된다. 진입은 `Account`(power)에서만.
- **롤백**: `case "UserMgmt"`를 `DummyScreen`으로 되돌리고 신규 파일 삭제.
- [ ] 완료

### W7: 진단·상태 모달 + 설정 진입점
- **Context Brief**: `ModalId`에 `diagnostics`가 이미 있고 `App.tsx`의 스텁 분기로 떨어진다. 6섹션(카메라·인코더·서버·로그/저장소·개발자 문의·앱)을 채운다. **게이트 키는 "설정됨/미설정"만** 보여준다 — 값은 절대 노출하지 않는다. **"구성됨"은 "도달 성공"이 아니다** — `GET /health`와 `GET /frames/default`(401 여부) 두 프로브를 함께 수행하는 `healthService.probe()`가 이미 있다.
- **대상 파일**: `src/screens/modals/diagnostics/{diagnosticsPresenter.ts,DiagnosticsModal.tsx,diagnostics.module.css}`(신규) · `src/adapters/platform/{clipboard,appInstall}.ts`(신규) · `src/adapters/storage/exportImport.ts`(로그 부분만) · `src/ui/views/SettingsView.tsx` · `src/screens/settings/useSettingsScreen.ts` · `src/App.tsx` · 테스트 1~2파일
- **선행 조건**: W3
- **구현 내용**: §7. `describeServerStatus`·`loadServerStatus`를 **재사용**하고 버킷·현재 계정 행만 프레젠터에서 덧붙인다. 권한 조회는 런타임 감지 + try/catch → "알 수 없음". `encoderProbe()`가 `null`이면 "아직 판정 전". 카메라를 **열지 않는다**. 언마운트에서 `AbortController.abort()`. 설정 §5 고급에 `[진단·상태]`(로그인 전용 렌더 가드 + 액션 가드). `App.tsx` ModalStack에 `case "diagnostics"` 추가 + **스텁 default 분기 제거**.
- **검증 명령**: `npx vitest run tests/unit/screens/diagnostics.test.ts` · `npx vitest run` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 모달이 카메라·인코더·서버·저장소 상태를 표시하고 게이트 키 행에 **"설정됨"만** 보인다. "구성"과 "도달"이 **별 행**이다. [로그 내보내기]가 `.log` 파일을 만든다.
  - [non-goal] **게이트 키 값 문자열이 화면·로그에 0건**(테스트가 주입한 키를 스냅샷 전체에서 검색해 확인). 모달을 여는 것만으로 카메라 LED가 켜지지 않고 저장소 권한 창이 뜨지 않는다(`readStorageStatus` 사용). 로그 내보내기 실패가 모달을 닫거나 앱을 죽이지 않는다. 게스트에게 [진단·상태]가 보이지 않는다.
  - [trigger] 프로브는 모달 진입 1회 + [다시 확인]에서만. 클립보드 복사는 [복사] 클릭에서만.
- **롤백**: 모달 파일 삭제 + `App.tsx` 스텁 분기 복원 + 설정 버튼 제거.
- [ ] 완료

### W8: Service Worker · PWA · 업데이트 흐름
- **Context Brief**: manifest·아이콘은 **이미 있다**. 필요한 것은 SW다. 앱 셸만 캐시하고 **API 응답·서명 URL은 절대 캐시하지 않는다**. `skipWaiting`은 install에서 쓰지 않고 **[지금 적용] 메시지 경로에서만** 쓴다. `web/firebase.json`에 `/sw.js` no-cache 헤더가 이미 있다.
- **대상 파일**: `src/adapters/platform/swPolicy.ts`(신규) · `src/sw.ts`(신규) · `vite.precache.ts`(신규) · `vite.sw.config.ts`(신규) · `vite.config.ts` · `tsconfig.json`(include에 새 루트 파일 2개) · `package.json`(build 스크립트) · `src/shell/swUpdate.ts`(신규) · `src/main.tsx` · `src/ui/views/SettingsView.tsx`([앱 업데이트 확인]) · `src/screens/modals/diagnostics/*`(앱 섹션) · `web/firebase.json` · 테스트 3파일
- **선행 조건**: W7(진단 [앱] 섹션이 SW 상태를 표시한다)
- **구현 내용**: §8 전체. `classifySwRequest`는 9단 판정이며 **기본이 bypass**다. `sw.ts`는 `cache.addAll`을 쓰지 않고 개별 `cache.add` + `Promise.allSettled`(`/sounds/shutter.wav`가 없다). `logger` import·`console.` 0건. `installServiceWorker`는 `import.meta.env.PROD`에서만 등록한다. [지금 적용]은 촬영 중(`isSessionActive`)에는 미노출 + 액션 가드이며 `controllerchange` 1회 가드 후 `location.reload()`. `web/firebase.json`: `connect-src`에 `blob:` 추가 + `/precache-manifest.json` no-cache.
- **검증 명령**: `npx vitest run tests/unit/storage/swPolicy.test.ts tests/unit/storage/precacheManifest.test.ts tests/unit/shell/swUpdate.test.ts` · `npm run build` → `ls ../web/kiosk/sw.js ../web/kiosk/precache-manifest.json` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `web/kiosk/sw.js`가 단일 파일로 생성되고 자산 목록이 인라인돼 있다. 배포본에서 네트워크를 끊어도 앱이 로드된다. 진단 [앱] 섹션이 SW 상태를 표시한다.
  - [non-goal] **API 응답·서명 URL이 캐시되지 않는다**(cross-origin은 `respondWith` 미호출 — 단위 테스트로 고정). **SW가 촬영 중 앱을 갱신하지 않는다**(install에 `skipWaiting` 0건 · 촬영 중 [지금 적용] 미노출). dev 서버에서는 SW를 등록하지 않는다. `sw.js`에 `logger`·`console.` 0건.
  - [trigger] 갱신 적용은 **[지금 적용] 클릭** 또는 **모든 탭을 닫은 뒤 다음 시작**에만. 리로드는 `controllerchange` 1회에만.
- **롤백**: `sw.ts`·`vite.sw.config.ts` 삭제 + build 스크립트 1단 복귀 + `main.tsx` 등록 제거. **배포된 SW를 회수하려면** `sw.js`를 "모든 캐시를 지우고 `registration.unregister()`" 하는 kill-switch 버전으로 한 번 더 배포해야 한다 — 파일 삭제만으로는 이미 설치된 SW가 사라지지 않는다.
- [ ] 완료

### W9: store-zip 코덱 + 프레임 내보내기
- **Context Brief**: 프레임을 Windows `Frame\` 폴더에 그대로 풀 수 있는 zip으로 내보낸다. 공용은 `{이름}.png`+`{이름}.slots`, 개인은 `{계정}_{이름}.png`+`.slots`(05 §4.3·§4.6). `.slots` 본문은 기존 `serializeSlotsFile`이 정본이다. ⚠️ **이미지 바이트를 `fetch(blob:…)`로 읽지 마라** — OPFS에서 직접 읽는다.
- **대상 파일**: `src/adapters/storage/zipStore.ts`(신규) · `src/adapters/storage/exportImport.ts` · `src/adapters/storage/frameStore.ts`(`readImageBytes`) · `src/screens/settings/frameTransfer.ts`(신규) · `src/ui/views/SettingsView.tsx` · `src/screens/settings/useSettingsScreen.ts` · 테스트 2파일
- **선행 조건**: W3
- **구현 내용**: §9.1·§9.2. `zipStore.ts`는 **import 0**(순수). `frameStore.readImageBytes(frame)`는 레코드를 찾아 `opfs.readFile(record.imageFile)`을 부르고 실패는 `null`. 번들·fallback 프레임은 제외. 부분 실패를 개수로 정직하게 보고(M4). 렌더·액션 가드는 `!isGuest`.
- **검증 명령**: `npx vitest run tests/unit/storage/zipStore.test.ts tests/unit/storage/frameTransfer.test.ts` · `npx vitest run tests/unit/frames` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `buildStoreZip` → `parseZipEntries` 왕복이 동일 바이트를 낸다. 개인 프레임 항목명이 `{계정}_{이름}.png`다. 내보내기 후 토스트에 실제 개수가 나온다.
  - [non-goal] `frameStore.ts`에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory(`가 여전히 0건이다(FR-1). `exportImport.ts`에 `fetch(` **0건**. 실패한 프레임이 성공으로 집계되지 않는다.
  - [trigger] 내보내기는 [프레임 내보내기] 클릭에서만 — 화면 진입만으로 파일이 만들어지지 않는다.
- **롤백**: `zipStore.ts`·`frameTransfer.ts` 삭제 + 설정 버튼 제거 + `readImageBytes` 제거.
- [ ] 완료

### W10: 프레임 가져오기
- **Context Brief**: zip을 읽어 **개인 프레임으로만** 저장한다. 설정 가져오기와 같은 **파싱 → 미리보기 → [적용]** 형태다(즉시 덮어쓰지 않는다). 운영자가 Windows 탐색기로 다시 압축하면 deflate가 되므로 `DecompressionStream("deflate-raw")`를 런타임 감지해 지원한다.
- **대상 파일**: `src/adapters/storage/exportImport.ts` · `src/screens/settings/frameTransfer.ts` · `src/ui/views/SettingsView.tsx` · `src/screens/settings/useSettingsScreen.ts` · 테스트 1파일
- **선행 조건**: W9
- **구현 내용**: §9.3 표 전부. 권한은 `canWriteFrames(role)`(렌더 + `previewFrameImport` 첫 줄). 이름 충돌은 `nextCopyName`, 상한은 `exceedsLocalFrameLimit`를 **건마다** 재평가. `#imagesize`와 실제 PNG 크기가 다르면 `rescaleSlots`. `#dbid`는 버린다. `ImageBitmap`은 반드시 `close()`. 저장은 `frameStore.saveLocal`만 쓴다.
- **검증 명령**: `npx vitest run tests/unit/storage/frameTransfer.test.ts` · `npx vitest run` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] zip을 고르면 **미리보기 목록**이 뜨고 [적용] 후에만 저장된다. 이름 충돌 항목에 "이름 변경됨" 표시가 붙는다. 11번째 항목에서 상한 안내가 뜬다.
  - [non-goal] 파일 선택만으로 저장되지 않는다. **공용 스코프로 저장되지 않는다**(전부 `scope:"user"`). `dbId`가 기록되지 않는다. `canWriteFrames`가 false면 버튼이 없고 액션도 거부된다. 압축 미지원 브라우저에서 앱이 죽지 않고 전용 안내만 뜬다.
  - [trigger] 저장은 [지금 적용] 클릭에서만.
- **롤백**: 가져오기 버튼·함수 제거(내보내기는 유지).
- [ ] 완료

### W11: 정적 불변식 + 문서 갱신 + 최종 검증
- **Context Brief**: 이번 Step에서 만든 규약을 **테스트가 소스를 읽어** 고정하고(15 §3.4 관례), 문서 5종을 갱신한다. Step 16이 마지막 구현 Step이므로 `15 §7`을 **Step 17만 남은 상태**로 바꾼다.
- **대상 파일**: `tests/unit/accounts/accountInvariants.test.ts`(신규) · `tests/unit/settings/settingsInvariants.test.ts` · `docs/web-client/15-implementation-conventions.md` · `docs/web-client/11-wbs.md` · `docs/web-client/14-handoff-and-user-actions.md` · `docs/web-client/{01,03,10}` 경로 정정 · `docs/analysis/13-client-behavior-spec.md` §9.2 · `docs/design/README.md`
- **선행 조건**: W4~W10 전부
- **구현 내용**: §4.3·§7.3의 ACC-1~4 · SW-1~3 · DIAG-1을 구현하고, `PIN_FILES`에 3파일을 추가한다. 문서는 **§14의 지침 그대로** 수행한다(§14.1 표 행 추가 · §14.2 함정 13~16 · §14.3 §7 교체 · §14.4 재개 표 · §14.5 경로 정정 + V25 신설).
- **검증 명령**: `npx vitest run`(**W1 기준선보다 커야 한다**) · `npx tsc --noEmit` · `npm run build` · `git status`(의도한 파일만 변경)
- **완료 기준**:
  - [관측] 정적 불변식 9건이 전부 통과한다. 웹 테스트 총계가 W1 기준선보다 크다. `npm run build`가 `web/kiosk/{index.html,sw.js,precache-manifest.json}`을 만든다. `15 §7`이 "미완: Step 17뿐"으로 바뀐다.
  - [non-goal] `15 §6`의 Step 9~15 절이 **변경되지 않는다**(stale 유발 금지). `docs/spec-vectors/*`·`web/functions`·`src/MCPhoto.*`가 무변경. `App.tsx`에 `DummyScreen`으로 렌더되는 화면이 0개.
  - [trigger] 문서 갱신은 코드가 전부 녹색인 뒤에만 — 실측 수치를 추정으로 적지 않는다.
- **롤백**: 불변식 테스트 파일 삭제 + 문서 diff 되돌림.
- [ ] 완료

---

## 16. 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F25) / 미검증 가정(A1~A7)이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다 (A1→W9 · A2→W8 · A3→W10 · A4→W7 · A5→W8 · A6→W9 · A7→W1)
- [x] 11개 단계 전부에 7개 필수 필드가 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이고 UI 단계에 non-goal·trigger가 있다
- [x] 검증 명령이 자동 실행 가능한 CLI다
- [x] 단계 수 11개 (3~12 범위)

---

## 17. 이 Step의 비목표 (명시)

| 만들지 않는 것 | 왜 |
|----------------|-----|
| Playwright E2E · `playwright.config.ts` | **Step 17이 Playwright 도입 자체를 소유한다**(11-wbs Step 17) |
| [앱 종료] 버튼 | WD5 — 브라우저 탭을 스크립트로 닫을 수 없다. [키오스크 종료]가 대체다 |
| 계정 생성 · 비밀번호 변경 UI | it15에서 폐지(03 §13.1). 신규 계정은 SSO 최초 로그인 시 서버가 `temp_user`로 만든다 |
| 사용자 관리 목록의 검색·페이지네이션·정렬 변경 | 03 §14가 **고정 정렬**만 규정한다. 계정 수가 문제가 될 규모가 아니다 |
| 서버 프레임 이미지 SW 캐시 | 01 §6 — 프레임 캐시의 소유자는 **OPFS**다(05 §4). 두 곳이 캐시하면 dedup 규격이 갈라진다 |
| `workbox`·`vite-plugin-pwa` 도입 | §8.1 |
| 진단의 로그 **목록 뷰**(항목 나열) | 03 §15.2는 건수·기간 + 내보내기만 요구한다. 화면에 로그 본문을 띄우면 마스킹 그물을 한 번 더 검증해야 한다 |
| 계정별 PIN 잠금(서버) | DoS(07 §6.3). 기기 단위 잠금이 전부다 |
