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
npx tsc --noEmit && npx vitest run     # 492 통과
cd ../web/functions && npm test         # 316 통과
cd ../.. && dotnet test tests/MCPhoto.Tests   # 840 통과
```

세 개가 다 녹색이면 재개 지점이 건강한 것이다. 그다음 **[11 · WBS](./11-wbs.md)의 체크박스**에서 다음 Step을 고른다(각 Step에 산출물·검증·이탈 사항이 기록돼 있다).

| 다음 | 선행 조건 |
|------|-----------|
| Step 9 타임랩스 · 10 로컬 보관 | 없음 |
| **Step 11 업로드·QR** ★마일스톤 A | A4(버킷 CORS) — [14 §5](./14-handoff-and-user-actions.md) |
| Step 12~16 | A1·A2·A3(OAuth·시크릿·게이트 키) |
| Step 17 E2E·실기기 | 실기기 3대(사람) |

권장 분할: **9+10을 한 세션**, 11 / 12 / 13 / 14 / 15 / 16을 각각 한 세션. Step 13·15·16은 화면이 커서 한 세션을 다 쓴다.

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

---

## 5. 커밋·문서 관례

- 커밋은 **기능 단위**로 나눈다(리뷰 단위). 메시지에 **"왜"** 를 쓴다 — 특히 나중에 "이거 왜 이렇게 했지?" 하고 되돌리기 쉬운 결정.
- Step을 끝내면 **[11 · WBS](./11-wbs.md)의 해당 체크박스**에 산출물·검증 수치·**설계 이탈**·남은 실측을 적는다. 이게 다음 세션의 진입점이다.
- 사람이 해야 하는 일이 생기면 **[14](./14-handoff-and-user-actions.md)** 에 절차·검증까지 적는다. "나중에 알려주면 되지"로 두면 잊힌다.
- `docs/analysis/*`는 **플랫폼 중립 규격**이다. 서버 계약·동작이 바뀌면 거기부터 고친다(예: `clientKind` 추가 → `analysis/31 §4.2`).

---

## 6. Step 9~16에서 미리 알아야 할 것

### Step 9 타임랩스
- **의존성 추가 필요**: MP4 muxer(`mp4-muxer` 등). `01 §7`대로 **버전 핀 고정** + `webclient/THIRD-PARTY.md`에 라이선스 기록(상용화 요구).
- 경로 판정은 `VideoEncoder.isConfigSupported("avc1.42001E")` → `MediaRecorder.isTypeSupported("video/mp4;codecs=avc1")` → 미지원. **경로 A(MediaRecorder)는 메인 스레드 전용**이다(VF-15).
- 스풀은 이미 준비돼 있다: `sessionWorkspace.writeTimelapseFrame/listTimelapseFrames/removeTimelapseFrame`, 파일명은 0 패딩이라 **문자열 정렬 = 시간 정렬**.
- 수집 지점도 있다: `cameraService.onProcessedFrame(...)`.
- 미지원은 **예외가 아니라 `null`**이다(`timelapseUrl=null`은 계약상 합법 — VF-6).

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

### Step 13~16
- 설정 화면은 `settingsStore.save(patch, {isGuest})`만 부르면 된다 — 게스트 제한 키 보존은 `settingsRepo`가 처리한다.
- 권한 게이트는 도메인에 다 있다(`rolePolicy`·`roleChangePolicy`·`frameEditPolicy`). 화면은 **렌더 가드 + 액션 첫 줄 가드** 2중으로 쓴다(M10).
- 프레임 이름: 서버 등록 경로는 `validateFrameNameForServer`(`_` 하드 거부), 로컬 저장은 `validateFrameName` + `underscoreWarning`(비차단).

---

## 7. 지금 상태 요약

| 항목 | 값 |
|------|-----|
| 완료 | WBS Step 0~8 + 서버 B1·B2·B4 |
| 테스트 | 웹 **492** · 서버 **316** · Windows **840** |
| 브랜치 | `feature/web-client-foundation` (11 커밋, 푸시됨) |
| `main` | **무변경** |
| 미완 | Step 9~17, 사용자 액션 A1~A5, 실측 V1~V17 |

화면은 Home·FrameSelect(최소)·Guide·Capture·CutSelect·Result가 실물이고, 나머지는 전이 검증용 더미다(`App.tsx`의 `ScreenRouter`가 하나씩 교체하는 구조).
