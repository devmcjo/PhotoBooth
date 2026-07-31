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
npx tsc --noEmit && npx vitest run     # 1051 통과(45파일)
cd ../web/functions && npm test         # 316 통과
cd ../.. && dotnet test tests/MCPhoto.Tests   # 938 통과
```

세 개가 다 녹색이면 재개 지점이 건강한 것이다. 그다음 **[11 · WBS](./11-wbs.md)의 체크박스**에서 다음 Step을 고른다(각 Step에 산출물·검증·이탈 사항이 기록돼 있다).

| 다음 | 선행 조건 |
|------|-----------|
| **Step 13 PIN + 설정 화면** | Step 12(계정 API·로그인) 완료 + Step 3(설정 저장). ⚠️ PIN 호출에는 **`unauthorized: "reject"`** 를 반드시 넘긴다(§6 Step 12 절) |
| Step 14~16 | 동상 |
| Step 17 E2E·실기기 | 실기기 3대(사람). Playwright 도입도 이 Step이다 |

권장 분할: 13 / 14 / 15 / 16을 각각 한 세션. Step 13·15·16은 화면이 커서 한 세션을 다 쓴다.

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

### Step 14 프레임 선택 — **it20 대기 국면이 절반이다**(2026-07-31 main 머지분)
- `analysis/13 §4.2`에 **로딩 4국면**(`Loading`/`Ready`/`Degraded`/`Failed`) 규격이 신설됐다. 웹 반영은 [03 §4.1](./03-screens-spec.md)·[06 §6.1](./06-backend-integration-web.md)·[02 §6.2](./02-app-shell-and-navigation.md).
- **판정 계층은 Step 8.5에서 이미 이식됐다**(다시 만들지 마라): `domain/frames/frameLoadPolicy.ts`(`classifyFrameLoad`·`finalizeFrameLoad`·`nextFrameLoadDeadlineMs`·`frameLoadNotice` + 상한 상수) · `domain/frames/frameCatalogProgress.ts`(`catalogProgressLabel`). 벡터 `docs/spec-vectors/frame-load-policy.json` 52케이스가 Windows와 교차 고정한다. **이번 Step은 그 위에 어댑터·화면만 얹는 것이다.**
  - 함수명이 WBS 약칭(`classify`/`finalize`)이 아니라 **한정형**이다 — `domain/index.ts`가 평면 `export *` 배럴이라 짧은 이름은 충돌한다.
  - `finalizeFrameLoad`는 어떤 입력에서도 `Loading`을 반환하지 않는다. 화면의 `finally`가 이 함수로 국면을 닫으면 오버레이 고착이 **구조적으로 불가능**하다.
- **웹이 Windows보다 이 규격이 급하다**: Windows는 "최초 실행 1회"지만 웹은 **신규 기기·시크릿 창·저장소 비우기마다** 첫 방문이다.
- 불변식 **총 대기 60초 < `IDLE_TIMEOUT_MS` 120초**는 `shell.test.ts`가 이미 고정하고 있다(§3.4).
- 상한 타이머는 `setTimeout` 누적이 아니라 **실경과 기준**으로 판정한다(WM3와 동종 — 탭 백그라운드 스로틀).
- 카탈로그 로더는 **단일 비행 + 진행 replay**다. 부트스트랩 prefetch와 화면 진입이 **한 작업을 공유**하고, [기다리지 않고 시작]의 취소는 **호출자별**이라 공유 작업은 계속 진행해 캐시를 완성한다.

### Step 15 프레임 편집기 — **불러오기 규격이 뒤집혔다**(2026-07-30 재정의)
- **"기존 프레임 불러오기 = 사본"은 폐기됐다.** 세션 정체성이 **신규 생성**이 되어 power가 불러온 세션도 **서버 등록 대상**이다. 이름 자동 제안(`{원본} 사본`)도 없어졌다 — fork는 [선택 편집] 경로에만 남는다.
- **서버 등록 확인 모달**이 신설됐다(모달 6종 → **7종**). 체크박스 **기본 on**, 노출 조건은 등록 분기와 동일 축, **원자성**(서버 실패 시 로컬도 저장 안 함).
- **저장 전 검증 7단의 순서가 규격이다**([03 §11.3](./03-screens-spec.md)). 진입점이 2개이므로 실제 저장 함수 첫 줄에서 **재실행**한다.
- ⚠️ **`isFileNameSafe`를 분리해야 한다**: Windows 판정은 "빈 값 + 금지문자"뿐인데 웹 `validateFrameName`은 **100자 제한이 묶여 있다**. 그대로 선검증에 쓰면 축이 어긋난다.

### Step 13~16
- 설정 화면은 `settingsStore.save(patch, {isGuest})`만 부르면 된다 — 게스트 제한 키 보존은 `settingsRepo`가 처리한다.
- 권한 게이트는 도메인에 다 있다(`rolePolicy`·`roleChangePolicy`·`frameEditPolicy`). 화면은 **렌더 가드 + 액션 첫 줄 가드** 2중으로 쓴다(M10).
- 프레임 이름 판정은 **세 축**이다: 서버 등록 = `validateFrameNameForServer`(`_` 하드 거부) / 로컬 저장 = `validateFrameName` + `underscoreWarning`(비차단) / **저장 전 선검증 = `isFileNameSafe`**(길이 무관, 빈 값·금지문자만).

---

## 7. 지금 상태 요약

| 항목 | 값 |
|------|-----|
| 완료 | WBS Step 0~8 + **8.5** + **9** + **10** + **11**(★마일스톤 A) + **12**(인증) + 서버 B1·B2·B4 + 사용자 액션 A1~A5 |
| 테스트 | 웹 **1051**(45파일) · 서버 **316** · Windows **938**(Step 12 시점 3스위트 전부 실측. Step 12는 `docs/spec-vectors/`를 건드리지 않았고 서버·WPF 코드도 무변경이다) |
| 브랜치 | `feature/web-client-foundation` |
| `main` | **머지 완료**(2026-07-31, `e5efdfd` — it20 프레임 대기 UI · 프레임 불러오기 재정의 · 앱 아이콘 · ffmpeg 라이선스 검토 · v1.1.10). 충돌 없음, 세 스위트 전부 녹색 |
| 미완 | Step 13~17, 실측 V1~V21 |

**main 머지로 늘어난 작업**(코드는 무변경, 문서만 동기화됨 — 상세는 §6):

| 대상 | 늘어난 것 |
|------|-----------|
| Step 14 | `frameLoadPolicy`·`frameCatalogProgress` 도메인 이식 + 벡터 1파일 + 대기/실패 오버레이 + 단일 비행 로더 + 유휴 상한 불변식 테스트 |
| Step 15 | 불러오기 = 신규 생성으로 재정의, 서버 등록 확인 모달 신설, 저장 전 검증 7단, `isFileNameSafe` 분리 |
| Step 9 | 변화 없음. 참고로 **웹 타임랩스 경로에는 GPL 노출이 없다**(브라우저 내장 인코더 — `12 B14`) |

화면은 Home·FrameSelect(최소)·Guide·Capture·CutSelect·Result·Qr·Done·**Login**이 실물이고, 나머지(Account·Settings·FrameEditor·UserMgmt)는 전이 검증용 더미다(`App.tsx`의 `ScreenRouter`가 하나씩 교체하는 구조). **촬영 흐름 + 로그인 화면은 이로써 전부 실물이다** — 남은 더미는 Step 13·15·16이 채운다.
