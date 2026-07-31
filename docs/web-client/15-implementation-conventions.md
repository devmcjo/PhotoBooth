# 15 · 구현 관례와 재개 가이드 (Conventions & Resume Guide)

| 항목 | 값 |
|------|-----|
| 문서 | **다음 작업자(사람 또는 에이전트)가 Step 13부터 이어가기 위해 알아야 할 것** |
| 대상 | 이 폴더의 설계 문서를 읽었지만 **코드를 처음 보는** 사람 |
| 작성일 | 2026-07-31 작성 · **2026-08-01 갱신(Step 12 인증 완료 시점)** |
| 성격 | 설계 문서(00~14)가 "무엇을"이라면, 이 문서는 **"이 저장소에서는 어떻게"** 다 |

> 왜 필요한가: Step 1~8을 구현하며 굳어진 관례와, 실제로 밟은 함정들이 커밋 메시지에만 남아 있다.
> 새로 시작하는 세션이 커밋 11개를 다 읽지는 않는다. **여기 있는 것만 지키면 기존 코드와 어긋나지 않는다.**

---

## 1. 30초 재개 절차

```bash
cd webclient && npm ci
npx tsc --noEmit && npx vitest run     # 1926 통과(84파일)
cd ../web/functions && npm test         # 316 통과
cd ../.. && dotnet test tests/MCPhoto.Tests   # 938 통과
```

세 개가 다 녹색이면 재개 지점이 건강한 것이다. 그다음 **[11 · WBS](./11-wbs.md)의 체크박스**에서 다음 Step을 고른다(각 Step에 산출물·검증·이탈 사항이 기록돼 있다).

| 다음 | 선행 조건 |
|------|-----------|
| **Step 17 E2E·실기기** | Step 1~16 전부 + 실기기 3대(사람). Playwright 도입도 이 Step이다 |

구현 Step은 **전부 끝났다**. 남은 것은 E2E 자동화와 실기기 수락이다.

---

## 2. 계층 규칙 — 실제로 지켜지고 있는 형태

```
ui → screens → shell → domain ← adapters
```

| 규칙 | 강제 수단 |
|------|-----------|
| `src/domain`은 **아무것도 import하지 않는다**(도메인 내부 상대 경로만) | `tests/unit/domain/purity.test.ts`가 파일 단위로 검사 |
| 도메인은 `Date.now`·`Math.random`·브라우저 API·`console`을 **부르지 않는다** | 동상(정규식 검사) |
| **어댑터는 예외를 전파하지 않는다** — `false`/`null`을 돌려주고 상위가 상태로 표현 | 관례(리뷰) + 각 어댑터 테스트 |
| `console.*` 금지, **`logger.*`만** | 관례. `logStore`가 진단 화면·내보내기의 유일한 소스다 |

새 파일을 도메인에 넣으면 순수성 테스트가 자동으로 포함한다(glob). 브라우저 API가 필요하면 **어댑터**다.

---

## 3. 이 저장소의 테스트 전략 (따라야 하는 형태)

### 3.1 순수 코어 + 얇은 브라우저 래퍼

가장 중요한 패턴이다. `composeCore`가 대표 예다.

```
composeCore(RGBA 버퍼)      ← 픽셀 연산 전부. node에서 테스트된다.
compositor(ImageBitmap)     ← 디코딩·인코딩만. 브라우저 전용.
```

**왜**: 브라우저 API에 로직을 섞으면 node 테스트가 닿지 못하는 경로가 생기고, 그 경로에서 버그가 난다.
같은 이유로 `cameraService`는 `FrameSource`·`FrameProcessor` **인터페이스**에 의존해 Ready 게이트·타임아웃·정리 순서를 node에서 검증한다.

Step 9(인코더)도 같은 형태로 만든다: **프레임 선별·배속 계산·스풀 정책은 순수 함수**, `VideoEncoder`/`MediaRecorder` 호출만 어댑터.

### 3.2 시간·난수·지연은 주입한다

```ts
createCaptureSequence({ now: () => performance.now(), delay: (ms) => …, … })
```

`vi.useFakeTimers()`에 의존하는 대신 주입하면 **실경과 기반 로직(WM3)을 직접 검증**할 수 있다.
실제로 "delay가 요청보다 9배 오래 걸려도 1초면 끝난다"는 테스트가 이 방식으로 가능했다.

### 3.3 크로스 플랫폼 계약은 파일로 고정한다

| 무엇 | 파일 | 양쪽 검증자 |
|------|------|-------------|
| 순수 로직 값 | `docs/spec-vectors/*.json` (14파일 271케이스) | `SpecVectorTests.cs` ↔ `tests/unit/domain/vectors.test.ts` |
| 합성 픽셀 | `docs/spec-vectors/golden/` | `GoldenImageTests.cs` ↔ `tests/golden/golden.test.ts` |

**규칙**: 규격을 바꿀 때는 **벡터/골든 파일을 먼저 고친다** → 양쪽이 동시에 실패 → 양쪽을 고친다.
- 벡터 생성기(`webclient/scripts/genVectors.ts`)를 **다시 돌리지 않는다.** 웹 구현으로 기대값을 덮어써 교차 검증이 무력화된다.
- 골든은 파일을 지우고 `dotnet test --filter GoldenImageTests`를 돌리면 재생성된다(의도적으로 규격을 바꿨을 때만).

### 3.4 정적 검사로 고정한 불변식

문서에만 있으면 언젠가 깨진다. 아래는 테스트가 소스를 읽어 막고 있다.

| 불변식 | 검사 |
|--------|------|
| **WM1** CSS 반전 금지 | `src/` 전체에 `scaleX(-1)`·`rotateY(180deg)` 없음 + `CameraPreview`가 `<video>` 미렌더 |
| **M2** JWT 메모리 전용 | `authStore.ts` 소스에 저장소 API 문자열 0건 |
| **M2-a** `sessionStorage`는 `adapters/auth/oauthStateStore.ts` **한 파일에만** | `authInvariants.test.ts` — `src/` 전체 grep(주석 제거 후) |
| **M2-b** 인증 11파일에 `localStorage`·`indexedDB`·`document.cookie`·`persist(` 0건 | 동상 |
| **AUTH-1** `sessionStore.login(` 호출부는 **콜백 러너 1곳뿐** | 동상 — `devLogin` 류 세션 위조 헬퍼 재발 방지 |
| **AUTH-2** `clientKind`가 `"web"`으로 고정 | 동상 — 빠지면 서버가 desktop client_id로 교환해 반드시 실패한다 |
| **AUTH-3** 인증 파일의 `logger` 컨텍스트에 `code`·`state`·`nonce`·`codeVerifier`·`token`·`pin` 키 0건 | 동상 — 이 키는 마스킹 대상이라 진단이 무용해진다 |
| **AUTH-4** `App.tsx`에 `devLogin` 0건 | 동상 |
| **AUTH-5** authorize URL에 `prompt=select_account` 존재 | 동상 — 빠지면 손님이 직전 운영자 계정으로 원탭 로그인된다 |
| dev 포트 5173 + `strictPort: true` | 동상(`vite.config.ts` 소스) — Google Console·서버 허용 목록과 정합 |
| 도메인 순수성 | §2 |
| **유휴 상한** 총 대기 60초 < `IDLE_TIMEOUT_MS` 120초 | `shell.test.ts`(도메인 사본 = 셸 실제값 동기화까지) |
| MP4 muxer import는 **`encode.worker.ts` 하나뿐** | `timelapseService.test.ts` — 코어를 node 테스트 가능 상태로 고정 |
| Worker에서 도는 코어에 **로거 0건** | 동상. `logger`는 메인에만 붙어 Worker 로그는 진단에 도달하지 않는다 |
| `encode.worker.ts`는 **OPFS를 읽기만** 한다 | 동상(`createWritable`·`createSyncAccessHandle` 0건) |
| `resultSaver.ts`·`resultsStore.ts`가 **메인 스레드에서 OPFS를 직접 만지지 않는다**(VF-14) | `resultSaver.test.ts` — 소스에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory(` 0건 |
| `dirHandleRepo.ts`는 **OPFS를 건드리지 않는다** | 동상. ⚠️ 이 파일만 `createWritable`이 **허용**된다(대상이 사용자 디렉터리이고 Worker가 그 핸들에 닿을 수 없다) |
| 폴더 핸들 DB ≠ 로그 DB | 동상(`DIR_HANDLE_DB_NAME !== LOG_DB_NAME`) — 같은 DB 버전업의 영구 blocked 회피 |
| **PIN-1** PIN 6파일의 `logger` 컨텍스트에 `pin`·`newPin`·`currentPin`·`code`·`state`·`nonce`·`token` 0건 | `settingsInvariants.test.ts` — 마스킹돼 진단이 무용해지거나, 이름을 바꿔 우회하면 **PIN이 실제로 샌다** |
| **PIN-2** `verifyMyPin`·`setMyPin` **둘 다** `unauthorized: "reject"` | 동상 — 빠지면 PIN 1회 오입력·`currentPin` 불일치가 **로그아웃**을 유발한다(E17) |
| **PIN-3** `mcphoto.pinLock.v1` 문자열이 `pinLockRepo.ts` 한 파일에만 | 동상 — 두 곳이 쓰면 형식이 갈라져 기기 잠금이 조용히 무력화된다 |
| **PIN-4** `pinPrompt` 모달의 `pushModal`은 `shell/pinGate.ts` 한 곳뿐 | 동상 — 게이트를 우회해 모달만 띄우는 경로 차단 |
| **PIN-5** `shell/pinGate.ts`·`pinPromptRunner.ts`에 `localStorage` 0건 | 동상 — 잠금 저장의 소유자는 어댑터 하나다 |
| **SET-1** `SettingsView`·`settingsForm`에 `clampSettings(`·`closestFrom(`·`normalizeQrToggles(` 0건 | 동상 — 화면이 도메인을 우회해 보정하면 진실원(analysis/41 §2)이 둘이 되어 Windows와 값이 갈라진다 |
| **SET-2** `GUEST_LOCKED_KEYS` 전부가 `SettingsView`의 `badge("…")`·`locked("…")`를 지난다 | 동상 — 새 제한 키에서 렌더 가드만 빠지는 것을 잡는다 |
| **SET-3** `App.tsx`에 임시 진입점 문자열 0건 | 동상 — Step 6·10의 실측용 버튼이 되살아나 진입로가 둘이 되는 것을 막는다 |
| **SET-4** `shell/settingsStore.ts`에 `isGuest: false` 0건 | 동상 — 하드코딩이 재발하면 게스트 조작이 운영자 값을 덮는다 |
| **SET-5** `screens/settings/*`가 `settingsRepo.save`를 직접 부르지 않는다 | 동상 — 저장은 반드시 `settingsStore.save`를 지나야 clamp·게스트 제한이 걸린다 |
| **FR-1** `frameStore.ts`·`frameCatalog.ts`·`frameImageCache.ts`에 OPFS 직접 접근 0건(VF-14) | `frameInvariants.test.ts` — 메인 스레드에서 쓰면 iOS/iPadOS Safari에서 **전 저장 경로**가 실패한다 |
| **FR-2** `canDeleteFrame(` 호출이 **2인자**다 | 동상 — 소유자를 넘기면 power가 fork 저장한 *공용* 로컬 프레임(`userId=null`)의 삭제 능력이 회귀한다 |
| **FR-3** 프레임 DB ≠ 로그 DB ≠ 폴더 핸들 DB | 동상(`mcphoto-frames`/`mcphoto`/`mcphoto-handles`) — 같은 DB 버전업의 영구 blocked 회피 |
| **FR-5** `FrameSelectView.tsx`·**`FrameEditorView.tsx`** 에 `pushModal(`·`"confirmDelete"`·`"framePicker"` 0건 + 공용 모달 디렉터리 부재 | 동상 — 삭제 확인·불러오기·서버 등록 확인은 전부 **화면 로컬 오버레이**다(03 §790). Step 15가 셸 식별자를 지웠으므로 **영구 원칙**이다 |
| **FR-6** `compositor.ts`에 `mode: "cors"` 존재 | 동상 — 빠지면 서버 프레임을 그린 canvas가 오염돼 `convertToBlob`이 SecurityError를 던진다(WM2) |
| **FR-7** `frameLoadPolicy.ts`의 기존 export 8종 보존 | 동상 — `docs/spec-vectors/frame-load-policy.json` 52케이스가 Windows와 교차 고정하는 이름들이다 |
| **FR-8** `src/` 전체에 `"framePicker"`·`"confirmDelete"` 리터럴 0건 + `ModalId`가 셸 모달 4종 | 동상 — 식별자로 남으면 "나중에 셸 모달로 배선하는" 경로가 되어 같은 UI가 둘이 된다 |
| **FR-9** `frameRepository.ts`에 `method: "PUT"`·`replaceImage`·`updateFrame` 0건 | 동상 — 편집 저장은 로컬 전용이다(03 §11.2). 함수가 있는 것만으로 호출 경로가 생긴다 |
| **FR-10** `frameEditorSave.ts`에서 `validateFrameSave(`가 `deps.createServerFrame(`·`deps.saveLocal(`보다 **먼저** 등장 | 동상 — 진입점이 2개(저장 버튼·서버 등록 오버레이)라 재실행하지 않으면 오버레이 경로로 우회된다 |
| **FR-11** `requiresServerRegisterPrompt(` 호출이 **정확히 2곳**(`useFrameEditor.ts`·`frameEditorSave.ts`) | 동상 — 오버레이 노출 축과 등록 분기 축이 갈라지면 "오버레이는 떴는데 등록은 안 되는" 조용한 불일치가 생긴다 |
| **FR-12** `frameSavePolicy.ts`·`screens/frameEditor/*`에 `validateFrameName(` 0건, `isFileNameSafe(` 존재 | 동상 — 100자 제한이 묶인 판정을 저장 전 선검증에 쓰면 축이 어긋난다(03 §11.3 웹 주의) |
| **FR-13** `frameSavePolicy.ts`의 reason 리터럴 8개 등장 순서 고정(특히 **④ `same-as-source` < ⑦ `name-conflict`**) | 동상 — ⑦은 이름 열거 실패 시 조용히 꺼지므로 ④가 2중 방어로 남아야 한다 |
| **FR-14** `screens/frameEditor/*`·`FrameEditorView.tsx`에 `console.` 0건 | 동상 — 로깅 규약(`logStore`가 진단·내보내기의 유일한 소스) |
| **FR-15** `frameImageLoader.ts`에 `mode: "cors"` 존재 | 동상 — WM2 규약 복제. 없어지면 원격 프레임을 그린 canvas가 오염된다 |
| **VF-12** `fixFrameAndResolveCutCount(` 호출부가 **1곳**(`useFrameSelect.ts`) | 동상 — 해석 지점이 늘면 설정 변경이 진행 중 세션의 컷 수를 바꾼다(it17) |
| `frameRepository.getUserFrames(` 호출 0건 | 동상 — `auth:"required"`라 401이면 **프레임 목록을 여는 것만으로 로그아웃 토스트**가 뜬다 |
| **ACC-1** 계정·사용자 관리 화면에 역할 문자열 리터럴·`.role ===` 비교 0건 | `accountInvariants.test.ts` — 판정은 `accountAdminPolicy`가 소유한다. 화면이 비교하면 서버 매트릭스와 조용히 갈라진다 |
| **ACC-2** `runDeleteAccount`·`runSetRole`·`runPinReset`이 **첫 실행문에서** 도메인 판정을 부른다 | 동상(FR-10과 같은 등장 순서 검사) — 가드가 뒤로 밀리면 서버 왕복이 먼저 일어난다 |
| **ACC-3** `screens/account/*`·`screens/userMgmt/*`에 `pushModal(` 0건 | 동상 — PIN 재설정·삭제 확인·키오스크 종료는 전부 **화면 로컬 오버레이**다(FR-5·FR-8 계열) |
| **ACC-4** `App.tsx`의 `Account`·`UserMgmt` 케이스가 둘 다 `<PinGate`로 감싸져 있다 | 동상 — 새 보호 화면의 게이트 누락 방지 |
| **SW-1** `sw.ts`의 `install` 리스너에 `skipWaiting` 0건 + 파일 전체 등장 1회(`message` 핸들러) | 동상 — 자동 갱신이 되살아나면 **촬영 중 앱이 바뀐다** |
| **SW-2** `sw.ts`에서 `classifySwRequest(`가 `respondWith(`보다 먼저 등장 | 동상 — 분류를 건너뛴 `respondWith`는 API 응답까지 캐시한다 |
| **SW-3** `sw.ts`에 `logger` import·`console.` 0건 | 동상 — SW 로그는 진단에 도달하지 않는다(§4 함정 12) |
| **WD5** `src/` 전체에 `window.close` 0건 | 동상 — 스크립트가 열지 않은 탭은 닫을 수 없다. 부르면 조용히 실패해 "버튼이 안 먹는다"가 된다 |
| **DIAG-1** 진단·뷰·`serverStatusPanel`에서 `backendApiKey`가 등장하는 줄에 `.length`·`.trim()`이 반드시 있다 | 동상 — 게이트 키 **값**이 화면·로그로 새는 것을 막는다 |
| **PIN-1**(확장) `PIN_FILES`에 `pinChangeRunner.ts`·`pinResetRunner.ts`·`PinKeypad.tsx` 추가 | `settingsInvariants.test.ts` — Step 16의 새 PIN 경로 3종이 자동으로 검사에 들어온다 |
| `exportImport.ts`에 `fetch(` 0건 · `zipStore.ts`·`swPolicy.ts`는 **import 0** | `accountInvariants.test.ts` — 프레임 바이트는 OPFS에서 직접 읽고(A1 회피), 순수 코덱·분류기는 의존성을 갖지 않는다 |

새 불변식을 만들면 **같은 방식으로 고정**하는 것이 이 저장소의 관례다.

---

## 4. 실제로 밟은 함정 (다시 밟지 말 것)

| # | 함정 | 교훈 |
|---|------|------|
| 1 | 로그 마스킹 목록의 `code`가 **오류 코드까지 가렸다** | 진단값을 로그 컨텍스트에 담을 때 키 이름이 `code`·`token`·`state`·`nonce`·`pin`이면 `[masked]`가 된다 → `errorCode`처럼 이름을 구분한다 |
| 2 | TS DOM lib이 `requestVideoFrameCallback`을 **필수 멤버로 선언**한다 | Safari 15.4 미만에는 없다. **타입을 믿지 말고 런타임 감지**한다. `showDirectoryPicker`·`createSyncAccessHandle` 등도 같은 성질 |
| 3 | `1 - usage/quota < 0.1`이 **정확히 임계값을 경고로** 넘겼다 | 비율 비교는 부동소수 오차를 탄다 → 정수(바이트)끼리 비교 |
| 4 | 쿨다운 초기값 `0`이 **첫 이벤트를 먹었다** | "아직 한 번도 없음"의 초기값은 `0`이 아니라 `-Infinity` |
| 5 | 골든 픽스처가 슬롯보다 작아 **확대 경로**를 탔다 | 테스트 픽스처는 **실제 비율**을 따라야 한다(컷은 카메라 해상도 > 슬롯) |
| 6 | 플래시 off가 **두 경로에서 중복 통지**됐다 | 상태 토글은 멱등으로(현재 값과 같으면 no-op) |
| 7 | `redirectUri` 검사에서 loopback을 먼저 봐서 **허용목록의 localhost가 영구 400**이었다 | 검사 순서가 계약이다. 넓은 규칙보다 **명시 허용목록을 먼저** |
| 8 | `defineSecret` 추가가 **배포 전제조건**을 만든다 | 시크릿을 선언하면 등록 전 배포가 실패한다. 문서에 순서를 남긴다 |
| 9 | hosting 멀티사이트 전환이 **기존 배포 스크립트를 깨뜨렸다** | `--only hosting`은 전 타깃이다. 기존 스크립트를 `hosting:default`로 고정 |
| 10 | vitest는 `expect(actual, msg)`를 받지만 **jest는 아니다** | `web/functions`는 jest다. 두 프로젝트의 단언 문법이 다르다 |
| 11 | 가공 Worker의 스틸 슬롯은 **1개짜리 덮어쓰기**다 | 다른 소비자가 `requestStill`을 재사용하면 컷 요청이 소멸해 **세션이 홈으로 복귀한다**. 새 소비자는 **전용 채널**을 만든다(Step 9의 스풀 채널이 선례) |
| 12 | Worker에서 남긴 `logger` 로그는 **어디에도 도달하지 않는다** | `attachLogStore`는 메인 프로세스에만 붙는다. Worker는 사유를 **응답 페이로드로** 넘기고 메인이 기록한다 |
| 13 | `cache.addAll`은 **원자적**이라 URL 하나가 404면 SW install 전체가 실패한다 | 존재하지 않을 수 있는 자산(`/sounds/shutter.wav`)이 섞이면 첫날부터 깨진다 → 개별 `cache.add` + `Promise.allSettled` |
| 14 | `sw.js` 바이트가 같으면 브라우저가 **업데이트를 감지하지 않는다** | 자산 목록을 `sw.js`에 인라인해 내용이 바뀌면 파일도 바뀌게 만든다(빌드 타임스탬프는 no-op 재빌드를 churn시킨다) |
| 15 | PIN 승인이 **화면 단위**라 `Account ↔ UserMgmt` 왕복마다 PIN을 다시 물었다 | 승인 단위는 화면이 아니라 **그룹**이다(`pinGateGroup`). 하위 페이지를 새로 만들면 그룹에 넣는다 |
| 16 | `authMethodLabel`이 호출자 0인 채로 규격과 다른 문구("Google 계정")를 들고 있었다 | **렌더된 적 없는 헬퍼는 "현행 동작"이 아니다** — 우선순위 규칙(소스 > analysis)을 적용할 대상이 아니다 |

---

## 5. 커밋·문서 관례

- 커밋은 **기능 단위**로 나눈다(리뷰 단위). 메시지에 **"왜"** 를 쓴다 — 특히 나중에 "이거 왜 이렇게 했지?" 하고 되돌리기 쉬운 결정.
- Step을 끝내면 **[11 · WBS](./11-wbs.md)의 해당 체크박스**에 산출물·검증 수치·**설계 이탈**·남은 실측을 적는다. 이게 다음 세션의 진입점이다.
- 사람이 해야 하는 일이 생기면 **[14](./14-handoff-and-user-actions.md)** 에 절차·검증까지 적는다. "나중에 알려주면 되지"로 두면 잊힌다.
- `docs/analysis/*`는 **플랫폼 중립 규격**이다. 서버 계약·동작이 바뀌면 거기부터 고친다(예: `clientKind` 추가 → `analysis/31 §4.2`).

---

## 6. Step 9~16에서 미리 알아야 할 것

### Step 9 타임랩스 — **완료(2026-07-31)**. 뒤 Step이 알아야 할 것만 남긴다
- 의존성 `mp4-muxer@5.2.2`(MIT, 캐럿 없는 정확 핀)가 들어왔고 `webclient/THIRD-PARTY.md`가 신설됐다. **새 런타임 의존을 추가하면 거기에 먼저 적는다.**
- **결과 mp4는 `sessionStore`에 없다.** `getTimelapseService().current()`로 읽는다(`TimelapseResult`). 홈 복귀(`stopEncoder` 훅)에서 폐기된다 → Step 10·11은 [다음] 처리 안에서 소비해야 한다.
- 인코딩은 **`Result`의 [다음] 1단계**에서만 일어난다(`ResultView.goNext` → `finish()`, 멱등). 화면 진입만으로는 시작되지 않는다.
- 가공 Worker에 **전용 스풀 채널**이 생겼다(`configureSpool`/`onSpoolFrame`). ⚠️ **스틸 채널(`requestStill`)을 다른 용도로 재사용하지 마라** — 1개짜리 덮어쓰기 슬롯이라 컷 촬영 요청과 충돌하면 세션이 홈으로 복귀한다.
- 미지원은 **예외가 아니라 `null`**이다(`timelapseUrl=null`은 계약상 합법 — VF-6). `null`이어도 [다음]은 정상 전이한다.
- 진단용 `getTimelapseService().encoderProbe()`(= `lastEncoderProbe()`)가 준비돼 있다 → Step 16 진단 모달과 [12 C3] `Guide` 안내가 소비한다.
- **실측 V18 7건이 남아 있다**([14 §10.3](./14-handoff-and-user-actions.md)). 브라우저 실행이 필요해 자동화 불가.

### Step 10 로컬 보관 — **완료(2026-07-31)**. 뒤 Step이 알아야 할 것만 남긴다
- **[다음]의 순서는 `screens/result/resultNext.ts`가 소유한다.** `ResultView.goNext`는 `runResultNext(defaultResultNextDeps({ finalBlob }))` 한 줄이다. **여기서 순서를 다시 조립하지 마라** — `resultNext.test.ts`가 `["finishTimelapse","save","go"]`를 고정한다(M6-W).
  - ⚠️ **정정(2026-07-31, Step 11 구현 시)**: 이 자리에 "Step 11의 업로드가 `resultNext.ts`의 주석 블록 자리에 들어간다"고 적혀 있었으나 **틀렸다.** 업로드 3단계의 소유자는 **`Qr` 화면**이다([03 §8.1](./03-screens-spec.md)의 [다음] 순서에 업로드가 없고, [03 §9.1](./03-screens-spec.md)이 업로드를 `Qr` 진입 절차로 규정한다. Windows도 `QrPopupViewModel.OnEnterAsync`가 수행한다). `resultNext.test.ts`가 `resultNext.ts` 소스에 `uploads/prepare`·`uploads/commit`·`runUpload`가 0건임을 정적으로 고정한다.
  - `isTempUserBlocked`는 **실배선이 끝났다**(`shell/qrUsageStore.ts` — 아래 Step 11 절).
- `resultSaver.saveResultLocally()`는 **절대 throw하지 않는다.** 결과는 `ResultSaveOutcome.status`(`saved`/`partial`/`failed`/`skipped`)다. `partial`(타임랩스만 실패)에는 **토스트를 띄우지 않는다** — 손님이 할 조치가 없고 타임랩스 부재는 계약상 합법이다(VF-6).
- **`OpfsClient`에 `usage(path)`가 생겼다**(왕복 1회로 직속 자식별 용량). Step 14 프레임 캐시 용량·Step 16 진단이 그대로 재사용한다. 실패·미지원은 빈 결과이고, 그것은 "정리 불필요"로 해석되어 **삭제를 덜 하는 안전한 방향**이다.
- **`resultsStore`가 완성돼 있다**(`listFolders`/`usage`/`removeFolder`/`readFile`/`enforceRetention`). Step 13의 [보관된 결과물] 패널은 이 인터페이스에 **얹기만** 하면 된다. `removeFolder`는 `isResultFolderName` 게이트를 통과한 이름만 지운다.
- **`App.tsx`의 [로컬 저장 폴더 선택] 버튼은 임시 진입점이다** — Step 13에서 설정 화면으로 옮기고 `DummyScreen`에서 제거한다(Step 6의 [카메라 테스트]와 같은 처지).
- ⚠️ **Step 14는 `logStore`에 `onversionchange`를 걸기 전에 `mcphoto` DB 버전을 올리면 안 된다.** 로그 스토어가 앱 수명 내내 그 연결을 붙들고 있는데 `db.onversionchange` 핸들러가 없어(`logStore.ts:160-174`) 업그레이드가 **영구 blocked** 된다. Step 10이 폴더 핸들을 **별 DB(`mcphoto-handles` v1)** 에 둔 것이 이 이유다. `dirHandleRepo.openHandleDb()`가 올바른 형태의 예시다.
- 폴더명 규약(`mcphoto_YYMMDD_HHMM`, 충돌 `-2`…`-999` → 32 hex)은 `domain/results/resultNaming.ts`에 있고 **Windows `LocalSaveService`와 같은 값**을 낸다. 벡터 파일은 없고 웹 테스트가 같은 리터럴 + `// ↔ LocalSaveTests.cs:33` 주석으로 짝을 명시한다 — 규약을 바꾸면 **양쪽을 함께** 고친다.
- **실측 V19 6건이 남아 있다**([14 §10.4](./14-handoff-and-user-actions.md)). 브라우저 실행이 필요해 자동화 불가.

### Step 11 업로드·QR — **완료(2026-07-31)**. 뒤 Step이 알아야 할 것만 남긴다
- **업로드 3단계의 소유자는 `Qr` 화면이다**(`screens/qr/uploadRunner.ts`의 `runUpload`). `resultNext.ts`가 **아니다** — 위 Step 10 절의 정정 참고. `runUpload`는 React를 import하지 않아 node에서 통째로 검증된다(`runResultNext`와 같은 형태).
- **합성 Blob은 `sessionStore.finalImage`로 인계된다**(`{ blob, format }`). `useResultCompose`가 합성 성공마다 올리고 `discardCaptureData()`가 지운다. ⚠️ `format`을 **같이** 든다 — 설정이 나중에 바뀌어도 이미 만들어진 바이트와 `Content-Type` 선언이 어긋나면 안 된다. 타임랩스는 싱글턴 서비스에 있어 인계가 필요 없다.
- **`isTempUserBlocked`는 `shell/qrUsageStore.ts`가 공급한다.** 계정이 `temp_user`로 바뀔 때만 1회 fire-and-forget 조회 → 캐시 → **동기 판정**(Windows `AppShellViewModel`과 동형). ⚠️ **비동기로 바꾸지 마라** — [다음]이 네트워크를 기다려 손님이 최대 100초 멈춘다. 미조회·실패·비TempUser는 전부 `false`(fail-open — M9). `main.tsx`가 `installQrUsageLifecycle()`로 1회 설치한다.
- `uploadGateway.put()`이 **XHR**로 서명 PUT을 한다(진행률 — WM5). `requiredHeaders`는 `Object.entries` **순회로 전부** 붙인다(M14). ⚠️ **던지지 않는다** — 실패는 `SignedPutOutcome` 판별 유니온이다(15 §2). 정적 테스트가 소스에 자격 증명 조립·`fetch(`가 0건임을 고정한다.
- **M8**: 어느 파일이든 PUT이 실패하면 **commit을 부르지 않는다.** "사진만 commit"하면 P1이 `timelapseUrl: null`을 "옵션 꺼짐"으로 표시해 실패를 은폐한다.
- QR은 **ECC Q**(VF-13). `qrcode-generator@2.0.4`(MIT, 런타임 의존 0, 정확 핀)가 들어왔다 — `THIRD-PARTY.md` 참조. **canvas에 직접 그린다**(라이브러리의 `createImgTag`/`createSvgTag`는 HTML 문자열이라 쓰지 않는다).
- 진행률 가중치는 **활성 단계 균등**(이식된 `overallProgress`)이다. [06 §4.5](./06-backend-integration-web.md)가 "파일 크기 가중"이라고 쓰고 있었으나 **문서를 구현에 맞춰 정정**했다(표시값이고 계약이 아니다).
- `Done`은 **6초 실경과** 자동 홈이고 **로그아웃하지 않는다**(M3). `screens/done/doneAutoHome.ts`가 정리 함수 하나로 타이머 + `visibilitychange`를 걷는다.
- ⚠️ **`App.tsx`의 `ScreenRouter`에 `Result` 케이스가 빠져 있었다**(Step 8/10이 `ResultView`를 만들고 라우팅을 붙이지 않았다 — `Result`가 더미 화면으로 렌더됐다). Step 11에서 `Result`·`Qr`·`Done` 3케이스를 함께 붙였다.
- **실측 V20 5건이 남아 있다**([14 §10.5](./14-handoff-and-user-actions.md)). 브라우저·폰이 필요해 자동화 불가이며 **폰 스캔은 Step 12(로그인) 이후**다.

### Step 12 인증 — **완료(2026-08-01)**. 뒤 Step이 알아야 할 것만 남긴다
- **`sessionStore.expireSession()`이 생겼다.** 401 만료 전용이며 **촬영 데이터를 지우지 않는다**(`logout()`은 지운다 — `discardCaptureData()` 동반).
  [02 §5.2](./02-app-shell-and-navigation.md) 매트릭스가 만료 행의 촬영 데이터를 "유지"로 못박기 때문이다. `currentUser` 변경 진입점은 이제 **`login`/`logout`/`expireSession` 3개**이고,
  M1 구독은 "필드가 null이 되는 것"을 보므로 `installTokenLifecycle`은 **무수정**이다.
- ⚠️ **PIN 계열 호출에는 `unauthorized: "reject"` 를 반드시 넘긴다**(Step 13). `backendClient`의 기본값은
  "Bearer가 붙었으면 `expired`"라서, 그냥 두면 **PIN을 한 번 틀렸을 때 로그아웃**된다(E17 회귀). 지금은 `accountService.verifyMyPin` 한 곳에만 붙어 있다.
- **401 → 세션 해제는 `backendClient`의 401 분기 한 곳**이 소유한다(`shell/sessionExpiry.ts`의 `handleSessionExpired`, 멱등).
  화면·서비스에 `isUnauthorized(err)` 기반 세션 해제를 **추가하지 마라** — 두 곳이 되면 토스트가 2번 뜬다.
- **`sessionStorage`는 `adapters/auth/oauthStateStore.ts` 전용이다**(정적 테스트 M2-a가 다른 파일 사용을 0건으로 고정). 들어가는 값은 PKCE·state·nonce·returnTo뿐이고 콜백 즉시 삭제된다.
- **`sessionStore.login()` 호출은 `screens/oauthCallback/oauthCallbackRunner.ts` 1곳뿐**이라는 정적 테스트(AUTH-1)가 있다 → `devLogin` 류 세션 위조 헬퍼를 만들면 실패한다(그 헬퍼는 이 Step에서 삭제했다).
- **콜백은 화면 상태가 아니라 URL 경로다.** `APP_STATES`에 `OauthCallback`이 없고, `main.tsx`가 `classifyRoute`로 분기해 `ScreenRouter` **밖**에서 `OauthCallbackGate`를 렌더한다.
  콜백 소비는 **React 밖 동기 1회**(`captureOauthCallback`)라 `<StrictMode>` 이중 effect에 영향받지 않는다 — **`useEffect`로 옮기지 마라**(2회째가 "취소"로 성공 문구를 덮는다).
- **`Login` 화면이 실물이 됐다**(`ui/views/LoginView.tsx` ↔ `screens/login/useGoogleSignIn.ts`). 로직은 `runSignIn(deps)`로 분리돼 node에서 검증된다(훅은 테스트에서 호출할 수 없다).
- **dev 포트가 5173 + `strictPort: true`로 고정됐다.** Google Console·서버 허용 목록이 5173이라 포트가 밀리면 `redirect_uri_mismatch`로 조용히 실패한다 — **바꾸지 마라.**
- 토큰은 여전히 **메모리만**(M2)이고 새로고침 = 재로그인이 정상이다(C6).
- **실측 V21 10건이 남아 있다**([14 §10.6](./14-handoff-and-user-actions.md)). 실 Google 계정·배포 헤더·폰이 필요해 자동화 불가이며 **E17 화면 관측은 Step 13 이후**다.

### Step 13 PIN 게이트·설정 화면 — **완료(2026-08-01)**. 뒤 Step이 알아야 할 것만 남긴다
- **PIN 게이트는 네비게이션 가드가 아니라 `<PinGate>` 렌더 게이트다.** 통과하지 못하면 `SettingsView`가 **마운트되지 않는다**.
  `Settings`·`Account` 둘 다 감싸져 있고, OAuth 복귀(`returnTo="Settings"`)처럼 `go()`를 거치지 않는 경로까지 구조적으로 덮인다.
  **새 보호 화면이 생기면 `<PinGate screen="X">`로 감싸는 것만으로 끝난다** — 화면 안에 가드를 심지 마라.
- ⚠️ **`<PinGate>`의 effect에 cleanup을 두지 마라.** `<StrictMode>` 이중 effect가 1회차를 취소해 설정 화면에서 즉시 튕겨 나간다(Step 12와 동종 함정).
  "매번 확인"은 cleanup이 아니라 `installPinGateLifecycle()`의 **화면·`currentUser` 변경 구독**이 승인을 폐기해 성립한다. `ensureScreenPinGate`는 멱등이다.
- **모달 결과를 기다리는 채널이 생겼다**(`openPinPrompt` → `resolvePinPrompt`). `pushModal`은 여전히 fire-and-forget이므로,
  **결과를 기다려야 하는 모달을 새로 만들면 이 형태를 따른다**: pending 1개 · 해제 멱등 · 항상 `popModal` 동반 · **마운트 감시 타임아웃**(5초)으로 무한 스피너 차단.
  ⚠️ 그 모달 컴포넌트는 `Modal`의 내장 `Esc`(→ `popModal`)를 쓰면 안 된다 — 약속이 미해결로 남는다. 자체 keydown에서 resolver를 부른다.
- **`sessionStore.markPinSet()`이 생겼다**(`currentUser` 진입점 4번째). 멱등이고 **null을 만들지 않아** M1 구독에 영향이 없다. AUTH-1(`.login(` 1곳)에도 걸리지 않는다.
- **설정 저장은 `settingsStore.save(patch, { isGuest, webExtras? })` 하나다.** `reEnableQr()`·`saveWebExtras()`는 **제거됐다**(호출자 0 + `isGuest: false` 하드코딩 버그).
  `save`는 이제 **메모리 값에도 clamp를 적용**한다 — 저장소와 화면이 갈라지지 않게 하기 위함이고, 03 §12.4의 재반영 단계가 여기에 걸려 있다.
- **게스트 제한은 4중이다**: 렌더 가드(`SettingsView`) → 액션 가드(`changeSetting` 첫 줄) → **패치 제외(`buildSavePatch`)** → 저장소 `omitKeys`. **본체는 세 번째**다.
  새 제한 키는 `GUEST_LOCKED_KEYS`에 넣기만 하면 ②③④가 자동으로 따라오고, ①의 누락은 **SET-2**가 잡는다.
- **`domain/settings/settingsEditPolicy.ts`가 "무엇을 편집할 수 있는가"의 단일 판정처다**(`isSettingEditable`·`settingLockReason`·`displaySettingValue`·`omittedSaveKeys`).
  Step 16의 계정·사용자 관리 화면도 같은 형태(렌더 가드 + 액션 첫 줄 가드)를 쓴다.
- **[보관된 결과물] 패널은 `resultsStore` 위에 얹혀 있다**(`screens/settings/storedResultsPanel.ts`). Step 16 진단 모달이 같은 모듈을 재사용할 수 있다.
- **`persistStorage.readStorageStatus()`가 생겼다** — `requestPersistentStorage`와 달리 **요청하지 않고 조회만** 한다. 화면 표시에는 반드시 이쪽을 쓴다(그러지 않으면 화면을 여는 것만으로 권한 창이 뜬다).
- ⚠️ **`confirmDelete` 모달을 쓰지 않았다.** 전체 삭제는 인라인 2단 확인이다 — **그 모달은 끝내 만들어지지 않았다**(Step 15가 프레임 삭제까지 화면 로컬 오버레이로 확정하고 `ModalId`에서 식별자를 지웠다 — FR-8).
- **이월**: [프레임 내보내기]/[가져오기] → **Step 16**(`exportImport.ts`) · [앱 업데이트 확인]·[진단·상태] → Step 16. **설정 화면 섹션 6에 자리만 비어 있다.**
  - ⚠️ **2026-08-01 정정**: 종전 이 줄은 내보내기/가져오기를 "Step 15"로 적고 있었으나 **오기**다. WBS Step 16의 `src/adapters/storage/exportImport.ts`가 소유하며, Step 15의 명시적 비목표다(설계 §23.2).
- **실측 V22 13건이 남아 있다**([14 §10.8](./14-handoff-and-user-actions.md)). 브라우저·실계정·실기기가 필요해 자동화 불가이며, **V22-4는 PIN이 없는 실계정**이 있어야 한다.

### Step 14 프레임 저장소·프레임 선택 — **완료(2026-08-01)**. 뒤 Step이 알아야 할 것만 남긴다

- **프레임 메타는 별 DB `mcphoto-frames` v1이다**(`adapters/storage/frameStore.ts`). ⚠️ `mcphoto`(로그)를 **절대 v2로 올리지 마라** — 그 연결은 앱 수명 내내 열려 있고 `onversionchange`가 없어 업그레이드가 **영구 blocked** 된다. 연결은 트랜잭션 1회마다 열고 닫는다(`dirHandleRepo` 패턴). `05 §4.2` 문서를 이 결정으로 정정했다.
- **판정 계층은 Step 8.5 것 그대로다.** Step 14가 도메인에 추가한 것은 `isFrameListInteractive`(`frameLoadPolicy.ts`) + `frameStorePolicy.ts` + `bundleManifest.ts` **셋뿐**이고, `classifyFrameLoad`·`finalizeFrameLoad`·`nextFrameLoadDeadlineMs`·`frameLoadNotice`는 **한 글자도 바뀌지 않았다**(FR-7이 고정).
- **오버레이 고착은 구조적으로 불가능하다**: `finalizeFrameLoad`가 어떤 입력에서도 `Loading`을 반환하지 않고, `runFrameLoad`의 `finally`가 그 함수를 **무조건** 부른다. 새 로딩 경로를 만들면 이 형태를 그대로 따라라.
- **카탈로그는 모듈 싱글턴 단일 비행이다**(`getFrameCatalog()`). ⚠️ 인스턴스를 새로 만들면 중복 다운로드가 돌아온다. 취소는 **호출자별**이라 `<StrictMode>` 이중 effect도 중복을 만들지 않는다 — **공유 작업까지 죽이도록 바꾸지 마라.**
  - JS 고유 함정 둘이 주석으로 박혀 있다: **A** `inFlight ??= (async () => { try … finally … })()`는 동기 throw에서 정리가 대입보다 먼저 일어나 **해결된 promise가 영구히 남는다** → `finally`를 태스크 바깥에 두고 `if (inFlight === task)` 동일성 가드. **B** `Promise.race`의 패자는 영원히 pending이라 abort 리스너가 남는다 → 어느 쪽이 이기든 `removeEventListener`.
- ⚠️ **`loadCore`의 서버 조회 catch를 지우거나 rethrow로 바꾸지 마라.** 오프라인이 `Ready`(≠`Degraded`)인 성질이 그 catch 하나에 걸려 있다 — 바꾸면 오프라인 부스가 **매 진입마다** 안내를 띄운다(E20 회귀).
- ⚠️ **`frameRepository.getUserFrames`를 부르지 마라**(정적 검사가 0건을 고정). `auth:"required"`라 401 → `handleSessionExpired` → **프레임 목록을 여는 것만으로 로그아웃 토스트**다. 개인 프레임은 정책상 서버에 없다(`loadPersonal` = `frameStore.listPersonal`).
- ⚠️ **`canDeleteFrame(frame, role)`은 2인자다**(FR-2). `userId`를 넘기면 power가 fork 저장한 *공용* 로컬 프레임의 삭제 능력이 회귀한다.
- **`frameRepository.deleteFrame`이 `Promise<boolean>`으로 바뀌었다**(종전은 응답을 버렸다). `{deleted:false}`는 **성공이 아니다** — 호출부가 이름 매칭으로 재시도한다. 형태가 어긋난 응답도 `false`다.
- **`compositor.loadFrameImage`가 원격/로컬로 갈라진다**: `https?:`에만 `{mode:"cors", cache:"force-cache"}`를 준다. `blob:`(OPFS 유래)·상대 경로(번들)는 옵션 없이 fetch한다. **https 분기의 `mode:"cors"`를 없애면 WM2가 깨진다**(FR-6).
- **object URL의 소유자는 `frameImageCache` 하나다**(경로당 1개, 재사용). ⚠️ **화면 이탈에서 revoke하지 마라** — 선택 프레임의 URL이 `Result`의 합성까지 살아야 한다. 해제 시점은 **프레임 삭제**뿐이다.
- **썸네일 resize 옵션은 미지원 시 예외 없이 조용히 무시된다**(`frameThumbnails.ts`). 결과 `bitmap.width`를 확인해 폴백을 정하고 그 판정을 모듈에 캐시한다. `ImageBitmap`은 반드시 `close()`(WR8).
- **삭제 확인은 화면 로컬 오버레이다**(FR-5). Step 15가 공용 모달로 승격하지 **않기로 확정**했고 `ModalId`에서 식별자를 지웠다(FR-8) — 되살리지 마라.
- **Step 15가 그것들을 쓴다**: `frameStore.saveLocal(input)` · `countPersonal(userId)` · `exceedsLocalFrameLimit(count)`(`LOCAL_FRAME_LIMIT = 10`). `usageBytes()`는 아직 호출자가 없다(Step 16 진단). 편집 대상 인계 채널은 `shell/frameEditorIntent.ts`로 생겼다.
- **번들 프레임은 매니페스트 규약이다**(`public/frames/index.json`). 브라우저는 정적 디렉터리를 열거할 수 없어 Windows `Directory.EnumerateFiles`의 대응물이 없다. **자산은 아직 커밋하지 않았다**(빈 배열) — 실 PNG는 운영 자산 준비 시 추가한다.
- **prefetch는 `main.tsx`의 `startApp` 말미**다(`bootstrap()` 안이 아니다). 첫 페인트 뒤 1회, 결과 폐기, 실패 무시.
- **실측 V23 8건이 남아 있다**([14 §10.9](./14-handoff-and-user-actions.md)). 브라우저·Safari·실계정이 필요해 자동화 불가다.

### Step 15 프레임 편집기·피커·삭제 — **완료(2026-08-01)**. 뒤 Step이 알아야 할 것만 남긴다

- **세션 정체성 축 하나가 전부를 결정한다**(`domain/frames/frameSavePolicy.ts`의 `FrameSessionSource` = `New` / `EditOwnLocal` / `ForkFromCatalog`). 배너 노출·서버 등록 오버레이·등록 분기·저장 캡션이 전부 이 값에서 나온다. ⚠️ **`isCreateMode` 같은 파생값을 만들지 마라** — 두 축이 갈라지면 "오버레이는 떴는데 등록은 안 되는" 조용한 불일치가 생긴다(FR-11이 호출 2건을 고정).
- **"기존 프레임 불러오기 = 사본"은 폐기됐다**(2026-07-30 재정의). 피커로 불러온 세션도 `New`이고 power면 **서버 등록 대상**이다. 이름 자동 제안도 없다 — fork는 [선택 편집] 경로에만 남는다.
- **저장 전 검증은 순수 함수 하나가 소유한다**(`validateFrameSave`). 순서가 규격이고(④ `same-as-source` < ⑦ `name-conflict`) FR-13이 소스 등장 순서로 기계 검증한다. ⑤⑥은 **`isFileNameSafe`만** 쓴다(길이 무관 — FR-12). 진입점 2개가 모두 `runFrameSave`를 지나고 그 **첫 실행문이 재검증**이다(FR-10).
- **서버 등록은 원자적이다**: `POST /frames` → 서명 PUT 중 하나라도 실패하면 `saveLocal`에 **도달하지 않고** 서버 문서를 best-effort로 `DELETE`한다. ⚠️ 부분 성공을 허용하면 재시도가 ⑦ 가드와 **자기 자신과 충돌**해 저장이 영구히 막힌다.
- **편집기 스테이지는 canvas가 아니라 `<img>` + DOM 슬롯이다**(설계 이탈 ②). 표시·드래그·클램프가 `useFrameEditor`의 `transform` state **하나**를 공유한다. 측정은 `getBoundingClientRect()`만 쓴다(선언 크기 금지 — 03 §11.7).
- ⚠️ **[선택 편집] 진입은 이미지를 재인코딩하지 않는다**(`fetchFrameImageBytes`). `loadFrameImageFromUrl`을 쓰면 장변 4000 축소가 붙어 `frame.slots` 좌표계와 어긋나 **기존 슬롯이 전부 밀린다**. Windows `LoadForEdit`도 같은 이유로 파일을 그대로 읽는다.
- **미리보기 object URL의 소유자는 `previewUrl.ts` 홀더다** — `frameImageCache`가 **아니다**(저쪽은 해제 시점이 "프레임 삭제"뿐이다). 언마운트 cleanup에서 `dispose()` 한다.
- **`ModalId`가 셸 모달 4종으로 줄었다**(`cameraTest`·`diagnostics`·`pinPrompt`·`idleWarning`). 화면 로컬 오버레이 5종은 `ui/components/OverlayDialog.tsx`를 쓴다 — **`pushModal`을 부르지 않는다**(FR-5·FR-8).
- **인계 채널은 `shell/frameEditorIntent.ts`다.** ⚠️ `readFrameEditorIntent()`는 **비파괴**다 — 소비형으로 바꾸면 `<StrictMode>` 2회차가 `new`로 떨어져 편집 세션이 조용히 신규 생성이 된다(Step 12·13과 같은 함정).
- **배율 범위는 10~300이다**(`MIN_SCALE_PERCENT`/`MAX_SCALE_PERCENT` — Windows `FrameEditorViewModel.MinScale/MaxScale`과 동일). ⚠️ 규격 문서에 70~130이 오래 남아 있어 **두 번이나 되돌려질 뻔했다.** 커밋 `0a93b59`("슬롯 스케일 10~300%·직접입력")가 의도적으로 넓힌 값이고, 진실원 우선순위(**소스 > analysis > design**, `design/README §4`)상 소스가 사실이다. 문서 6곳을 2026-08-01에 맞췄다.
- ⚠️ **`canEditFrame`은 power가 공용 로컬로 저장한 프레임(`userId=null`)을 편집 불가로 판정한다.** Windows와 같은 동작이고 FR-2가 삭제 축을 고정하고 있으니 고치지 마라 — 우회로는 피커로 불러와 새 이름으로 저장하는 것이다.
- **기존 결함 2건을 함께 고쳤다**: `createFrame`의 `upload` 봉투(F-4 — 안 고치면 이미지가 영원히 안 올라간다) · `saveLocal` 덮어쓰기 고아 PNG(F-5 — 정리는 **새 레코드 기록 뒤**다).
- **실측 V24 8건이 남아 있다**([14 §10.10](./14-handoff-and-user-actions.md)). 브라우저·실계정·실기기가 필요해 자동화 불가다.

### Step 14~16
- 설정 저장은 `settingsStore.save(patch, { isGuest, webExtras? })`만 부르면 된다 — 게스트 제한 키 보존은 `settingsRepo`가 처리한다(Step 13 절 참고).
- 권한 게이트는 도메인에 다 있다(`userRole`·`roleChangePolicy`·**`accountAdminPolicy`**·`frameEditPolicy`·**`settingsEditPolicy`**). ⚠️ `rolePolicy.ts`라는 파일은 **존재한 적이 없다**(문서 4곳의 오기를 2026-08-01에 정정했다). 화면은 **렌더 가드 + 액션 첫 줄 가드** 2중으로 쓴다(M10).
- 프레임 이름 판정은 **세 축**이다: 서버 등록 = `validateFrameNameForServer`(`_` 하드 거부) / 로컬 저장 = `validateFrameName` + `underscoreWarning`(비차단) / **저장 전 선검증 = `isFileNameSafe`**(길이 무관, 빈 값·금지문자만).

---

## 7. 지금 상태 요약

| 항목 | 값 |
|------|-----|
| 완료 | WBS Step 0~8 + **8.5** + **9** + **10** + **11**(★마일스톤 A) + **12**(인증) + **13**(PIN 게이트·설정 화면) + **14**(프레임 저장소·선택 화면) + **15**(프레임 편집기·피커·삭제) + **16**(계정·사용자 관리·진단 모달·PWA/SW·내보내기/가져오기) + 서버 B1·B2·B4 + 사용자 액션 A1~A5 |
| 테스트 | 웹 **1926**(84파일, Step 16 실측) · 서버 **316** · Windows **938**(후자 둘은 Step 12 시점 실측값. Step 13~16은 `docs/spec-vectors/`·서버·WPF 코드를 **무변경**이라 재실행 의무가 없다) |
| 브랜치 | `feature/web-client-foundation` |
| `main` | 머지 완료(2026-07-31, `e5efdfd`) |
| 미완 | **Step 17(E2E·실기기·수락)뿐**. 실측 V1~V25 |

**13개 화면이 전부 실물이다** — `App.tsx`의 `ScreenRouter`에 `DummyScreen`으로 남은 상태가 **0개**이고,
`ModalStack`의 미구현 스텁 분기도 사라졌다(셸 모달 4종 전부 실물). 남은 것은 **Step 17(E2E·실기기·수락)**뿐이다.
`DummyScreen` 함수 자체는 라우터의 `default` 분기 안전망으로만 남는다 — **여기에 기능 진입점을 두지 마라.**
