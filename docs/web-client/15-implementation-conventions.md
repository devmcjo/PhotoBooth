# 15 · 구현 관례와 재개 가이드 (Conventions & Resume Guide)

| 항목 | 값 |
|------|-----|
| 문서 | **다음 작업자(사람 또는 에이전트)가 Step 9부터 이어가기 위해 알아야 할 것** |
| 대상 | 이 폴더의 설계 문서를 읽었지만 **코드를 처음 보는** 사람 |
| 작성일 | 2026-07-31 (Step 1~8 완료 시점) |
| 성격 | 설계 문서(00~14)가 "무엇을"이라면, 이 문서는 **"이 저장소에서는 어떻게"** 다 |

> 왜 필요한가: Step 1~8을 구현하며 굳어진 관례와, 실제로 밟은 함정들이 커밋 메시지에만 남아 있다.
> 새로 시작하는 세션이 커밋 11개를 다 읽지는 않는다. **여기 있는 것만 지키면 기존 코드와 어긋나지 않는다.**

---

## 1. 30초 재개 절차

```bash
cd webclient && npm ci
npx tsc --noEmit && npx vitest run     # 645 통과(26파일)
cd ../web/functions && npm test         # 316 통과
cd ../.. && dotnet test tests/MCPhoto.Tests   # 938 통과
```

세 개가 다 녹색이면 재개 지점이 건강한 것이다. 그다음 **[11 · WBS](./11-wbs.md)의 체크박스**에서 다음 Step을 고른다(각 Step에 산출물·검증·이탈 사항이 기록돼 있다).

| 다음 | 선행 조건 |
|------|-----------|
| **Step 10 로컬 보관** | 없음 — Step 9의 `getTimelapseService().current()`가 타임랩스 입력이다 |
| **Step 11 업로드·QR** ★마일스톤 A | A4(버킷 CORS) — [14 §5](./14-handoff-and-user-actions.md) |
| Step 12~16 | A1·A2·A3(OAuth·시크릿·게이트 키) |
| Step 17 E2E·실기기 | 실기기 3대(사람) |

권장 분할: 10 / 11 / 12 / 13 / 14 / 15 / 16을 각각 한 세션. Step 13·15·16은 화면이 커서 한 세션을 다 쓴다.

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
| 도메인 순수성 | §2 |
| **유휴 상한** 총 대기 60초 < `IDLE_TIMEOUT_MS` 120초 | `shell.test.ts`(도메인 사본 = 셸 실제값 동기화까지) |
| MP4 muxer import는 **`encode.worker.ts` 하나뿐** | `timelapseService.test.ts` — 코어를 node 테스트 가능 상태로 고정 |
| Worker에서 도는 코어에 **로거 0건** | 동상. `logger`는 메인에만 붙어 Worker 로그는 진단에 도달하지 않는다 |
| `encode.worker.ts`는 **OPFS를 읽기만** 한다 | 동상(`createWritable`·`createSyncAccessHandle` 0건) |

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

### Step 10 로컬 보관
- `resultSaver`는 반드시 **`opfsWriter` Worker 경계**를 지나야 한다(`getOpfsClient()`). 메인에서 OPFS에 쓰면 iOS에서 전 저장이 실패한다.
- 순서가 불변식이다(**M6-W**): 합성 → **로컬 보관** → 업로드 분기. `useResultCompose`의 결과 Blob(`currentBlob()`)이 입력이다.

### Step 11 업로드·QR
- `uploadGateway.prepare/commit`은 이미 있다. **서명 PUT만** 남았고 **XHR로** 해야 한다(진행률 — WM5).
- `requiredHeaders`는 **객체를 순회**해 전부 붙인다(M14). 키를 골라 담으면 서명이 깨진다.
- 게스트는 `Qr`에 도달하지 않는다(VF-11). `ResultView.goNext()`가 이미 `isQrEffectivelyEnabled`로 분기한다 — **TempUser 한도만** `qrUsageService`로 채우면 된다(`isTempUserBlocked` 인자).
- QR은 **ECC Q**(Windows `QrService.cs`와 일치 — VF-13).

### Step 12 인증
- 서버는 준비됐다. 클라이언트는 **`clientKind: "web"`을 보내야 한다**(미지정은 desktop이라 웹 client_id로 교환되지 않는다).
- M1 배선(`installTokenLifecycle`)은 이미 설치돼 있고 테스트가 고정한다. **토큰 폐기를 로그아웃 버튼에 걸지 않는다.**
- PKCE·state·nonce는 `sessionStorage`, 토큰은 **메모리만**(M2).

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
| 완료 | WBS Step 0~8 + **8.5** + **9** + 서버 B1·B2·B4 + 사용자 액션 A1~A5 |
| 테스트 | 웹 **645**(26파일) · 서버 **316** · Windows **938**(Windows 수치는 Step 8.5 시점 실측. Step 9는 `docs/spec-vectors/`를 건드리지 않아 재측정 대상이 아니다) |
| 브랜치 | `feature/web-client-foundation` |
| `main` | **머지 완료**(2026-07-31, `e5efdfd` — it20 프레임 대기 UI · 프레임 불러오기 재정의 · 앱 아이콘 · ffmpeg 라이선스 검토 · v1.1.10). 충돌 없음, 세 스위트 전부 녹색 |
| 미완 | Step 10~17, 실측 V1~V18 |

**main 머지로 늘어난 작업**(코드는 무변경, 문서만 동기화됨 — 상세는 §6):

| 대상 | 늘어난 것 |
|------|-----------|
| Step 14 | `frameLoadPolicy`·`frameCatalogProgress` 도메인 이식 + 벡터 1파일 + 대기/실패 오버레이 + 단일 비행 로더 + 유휴 상한 불변식 테스트 |
| Step 15 | 불러오기 = 신규 생성으로 재정의, 서버 등록 확인 모달 신설, 저장 전 검증 7단, `isFileNameSafe` 분리 |
| Step 9 | 변화 없음. 참고로 **웹 타임랩스 경로에는 GPL 노출이 없다**(브라우저 내장 인코더 — `12 B14`) |

화면은 Home·FrameSelect(최소)·Guide·Capture·CutSelect·Result가 실물이고, 나머지는 전이 검증용 더미다(`App.tsx`의 `ScreenRouter`가 하나씩 교체하는 구조).
