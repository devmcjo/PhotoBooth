# web-fix (2026-08-07) · iOS Safari 카메라 실패 `unknown` — 사유 분리 · VideoFrame 실증 · 현장 진단 코드

| 항목 | 값 |
|------|-----|
| 문서 | 2026-08-07 iOS Safari 실패 건의 **원인 규명 수단 설계**(원인 자체는 아직 미상) |
| 증상 | iOS Safari(https · PWA 아님 · 인앱브라우저 아님)에서 촬영 시 **"카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요."** = 사유 `unknown` |
| 선행 | [`web-fix-20260806-mobile-camera-fallbacks.md`](./web-fix-20260806-mobile-camera-fallbacks.md) (커밋 `8e516a6`) |
| 범위 | (A) `unknown` 3경로 분리 · (B) `VideoFrame` 실증 검사 + 영구 강등 · (C) 기기에서 사유 꺼내기 |
| 작성 | 2026-08-07 · **설계만**(코드 미변경) |

> ⚠️ **문서 위치 정정**: 팀 리드 지시문은 산출물 경로를 `docs/analysis/`로, 선행 문서를
> `docs/analysis/web-fix-20260806-mobile-camera-fallbacks.md`로 적었으나 **선행 문서의 실제 경로는
> `docs/design/`** 이다(`docs/analysis/`에는 `NN-*.md` 규격서만 있다). "선행 문서 옆에 같은 형식으로"
> 라는 의도를 따라 `docs/design/`에 둔다.

---

## 0. 무엇을 알고 무엇을 모르는가

### 0.1 검증된 사실 (verified facts)

| # | 사실 | 근거 |
|---|------|------|
| **F-1** | 배포 번들에 커밋 `8e516a6`의 폴백이 실제로 포함돼 있다 | 팀 리드의 배포 번들 문자열 검사(`device+1080p`·`facing+1080p`·`사다리 전부 소진`·`pipelineStalled`) |
| **F-2** | 화면 문구 = `STRINGS.camera.errors.unknown` → 사유가 **`unknown`** 이다. `pipelineStalled`가 **아니다** | `strings.ts:229` · `CameraPreview.tsx:108` |
| **F-3** | `unknown`이 확정되는 경로는 코드상 **정확히 3개**다 | `cameraService.ts:259-265` · `364-371` · `374-393` |
| **F-4** | 사다리 소진 로그가 `err.name`을 **남기지 않는다**(`message`만) — 중간 단계 로그(`251-255`)는 `name`을 남긴다 | `cameraService.ts:263` |
| **F-5** | `VideoFrame` 판정은 **존재 검사뿐**이다. 대조군 `isWorkerPipelineSupported()`는 1×1을 실제로 만들어 `getContext("2d")`까지 확인한다 | `videoFrameSource.ts:57-60` vs `frameProcessorClient.ts:48-56` |
| **F-6** | `grab()`의 catch가 `VideoFrame` 실패를 삼키고 **`createImageBitmap` 폴백으로 내려가지 않는다** — 매 프레임 같은 실패를 반복한다 | `videoFrameSource.ts:94-107` |
| **F-7** | `new VideoFrame(video)`가 성공해도 `frameProcessorClient.process()`의 `postMessage(..., {transfer})`가 던지면 같은 catch로 흘러간다(전송 실패도 구분 불가) | `videoFrameSource.ts:96` → `cameraService.ts:362` → `frameProcessorClient.ts:135` |
| **F-8** | 진단 모달은 **로그인 전용**이다(렌더 가드 + 액션 가드 2중) | `SettingsView.tsx:402-407` · `useSettingsScreen.ts:349-356` |
| **F-9** | 클라이언트 로그는 IndexedDB(`mcphoto`/`logs`)에만 쌓이고 **원격 전송 경로가 없다** | `logStore.ts:156-158` · 전체 코드베이스에 로그 업로드 없음 |
| **F-10** | kiosk CSP `connect-src`에 **Cloud Functions origin이 이미 있다** — 서버 전송은 CSP 변경 없이 가능하다 | `web/firebase.json` kiosk 타깃: `connect-src 'self' blob: https://asia-northeast3-mcphoto-955fb.cloudfunctions.net …` |
| **F-11** | `cameraPermission.ts`는 `mediaDevices` 부재를 **이미 사전 검사**하고 `classifyCameraFailure("TypeError", …)`로 넘긴다 → 보안 컨텍스트면 현재 `unknown`이 된다 | `cameraPermission.ts:118-123` |
| **F-12** | "1회 실증 후 판정 캐시" 패턴의 선례가 이미 있다(`createImageBitmap` resize 옵션) | `frameThumbnails.ts:16-56` |
| **F-13** | `CameraFailureReason`은 `Record` 3벌(문구 키·retryable·진단 라벨)로 강제돼 사유를 늘리면 컴파일이 깨진다 | `cameraFailure.ts:70-77`·`89-97` · `diagnosticsPresenter.ts:126-133` |

### 0.2 미검증 가정 (open assumptions)

| # | 가정 | 검증 방법 |
|---|------|-----------|
| **U-1** | **이번 `unknown`이 3경로 중 어느 것인지 모른다.** 코드만으로는 좁힐 수 없다 | Step 6 배포 후 **실기기 재현 시 화면의 오류 코드**로 확정 |
| **U-2** | iOS Safari의 `getUserMedia`가 실제로 어떤 예외 이름을 던졌는지 모른다(`AbortError`/`InvalidStateError`/그 외) | 동상 — 코드의 `/{name}` 부분 |
| **U-3** | `AbortError`가 "다른 앱 점유"와 같은 성격이라는 전제 — **설계 리뷰 반영(2026-08-07): 이번 릴리스는 이 전제로 사유를 바꾸지 않는다**(§1.3) | 실기기에서 `unknown/AbortError`가 반복 관측되고 다른 앱을 닫아 복구되면 확인. 확인되면 Step 8 이후 **별도 1줄 커밋**으로 `inUse` 매핑을 추가한다(U-1·U-2와 같이 "관측 후 결정") |
| **U-4** | `<video>`가 렌더링 트리에 있는데도 iOS 17에서 rVFC가 안 돌 가능성 | 코드가 `pipelineStalled/*`로 찍히면 이 방향 — 이번 설계 범위 밖(별건) |

> ⚠️ **(B)는 이번 증상의 직접 원인이 아닐 가능성이 높다.** `VideoFrame` 경로가 매 프레임 터지면
> `processor.process()`가 한 번도 불리지 않아 가공 프레임이 **0장**이 되고, 그러면 Ready 타임아웃이
> `meter.total === 0`을 보고 **`pipelineStalled`** 를 확정한다(`cameraService.ts:379-380`). 화면에
> 뜬 것은 `unknown`이었으므로(F-2) (B)는 이번 건과 **다른 결함**이다. (B)는 별도로 발견된 실제
> 버그라서 함께 고치는 것이지, 이번 증상의 설명이 아니다.
>
> **원인 규명 수단은 (A)와 (C)다.** 지금 우리는 원인을 모른다. 이 설계의 목표는 원인을 맞히는 것이
> 아니라 **다음 발생 때 화면 한 줄로 확정되게** 만드는 것이다.

---

## 1. (A) `unknown` 3경로 분리

### 1.1 현재 — 성격이 완전히 다른 셋이 한 문구다

```
경로 ①  사다리 5칸 전부 소진 + classify default          → unknown   ← 장치를 못 열었다
경로 ②  스트림은 열렸는데 source.attach()가 false         → unknown   ← video.play()가 reject
경로 ③  Ready 타임아웃인데 meter.total > 0                → unknown   ← 프레임은 오는데 느리다
                                                              └ 화면: 전부 "권한과 연결을 확인해 주세요"
```

경로 ②·③은 **권한과 아무 관계가 없다.** 그런데 문구가 권한을 확인하라고 말한다 —
손님을 헛돌게 하고, 현장 운영자를 잘못된 방향(사이트 설정)으로 보낸다.

### 1.2 사유 3종 신설

`CameraFailureReason` 6종 → **9종**. `Record` 3벌이 컴파일로 누락을 막는다(F-13).

| 신설 사유 | 확정 지점 | 의미 | 손님 문구 | `isCameraRetryable` |
|-----------|-----------|------|-----------|:---:|
| **`playbackBlocked`** | `cameraService.ts:364-371`(경로 ②) | 스트림은 열렸으나 `video.play()`가 reject됐다 — **재생 시작 실패**. 권한·장치 문제가 아니다 | "카메라 영상을 시작하지 못했습니다. 화면을 한 번 누른 뒤 다시 시도해 주세요." | **`true`** |
| **`pipelineSlow`** | `cameraService.ts:374-393`(경로 ③) | 프레임은 도착하는데 8초 안에 Ready 게이트(8프레임·500ms·fps>0)를 못 넘었다 | "카메라 영상이 원활하지 않습니다. 다시 시도하거나 다른 브라우저에서 열어 주세요." | **`true`** |
| **`unsupportedBrowser`** | `classifyCameraFailure`의 `TypeError`(보안 컨텍스트일 때) | `navigator.mediaDevices`가 없다 — **인앱브라우저·구형 WebView**가 여기 걸린다 | "이 브라우저에서는 카메라를 사용할 수 없습니다. Safari·Chrome 등 기본 브라우저에서 열어 주세요." | **`false`** |

**문구 원칙 준수**(`strings.ts:222-228`): 세 문구 모두 **손님이 실제로 할 수 있는 조치만** 말한다.
`playbackBlocked`의 "화면을 한 번 누른 뒤"는 iOS 자동재생 정책에서 실효가 있는 유일한 조치다.

**`unsupportedBrowser`가 `false`인 이유**: 같은 브라우저에서 다시 눌러도 `mediaDevices`는 생기지
않는다. `permissionDenied`·`insecureContext`와 같은 부류다 — 헛도는 버튼을 만들지 않는다.

> 부수 효과: 선행 문서가 **R-3**(인앱브라우저 감지 + 외부 브라우저 안내)으로 미뤄 둔 것을
> **UA 분기 없이** 얻는다. F-11에 따라 Guide 화면의 권한 사전 요청 경로도 자동으로 개선된다
> (그 경로는 이미 `classifyCameraFailure("TypeError", …)`를 부르고 있다).

### 1.3 `classifyCameraFailure` 매핑 — `TypeError` 1건 추가, `AbortError`는 **보류**

```ts
case "TypeError":             // 신규 → unsupportedBrowser (insecure 판정 뒤이므로 안전)
  return "unsupportedBrowser";
```

- **`TypeError` → `unsupportedBrowser`**: `insecureContext`를 **먼저** 판정하는 기존 순서가
  유지되므로 http는 그대로 `insecureContext`다. 사다리의 어떤 칸도 빈 제약을 보내지 않으므로
  (`{audio:false, video:true}`가 최하단) 규격상 다른 `TypeError` 유발 조건이 없다 — 새 정적
  검사 **CAM-8**이 이 전제를 고정한다.
  ✅ **검증됨**(설계 리뷰): `cameraConstraints.ts`의 `FULL_HD`·`HD`·`FPS`는 전부 `ideal`이고,
  사다리 5칸 중 어느 칸도 `video`가 빈 값이 아니다 — CAM-8의 전제가 실제 코드와 일치한다.
  또한 `cameraService.ts`의 `openStream` 기본 구현(`navigator.mediaDevices.getUserMedia`)이
  `mediaDevices`가 없을 때 이 경로에서 실제로 `TypeError`를 던지는 것도 확인했다 — `TypeError`가
  `mediaDevices` 부재 **이외의 경로로 발생하는 사례가 코드베이스 안에 없다.**
  ⚠️ **잔여 위험(변경 없이 채택 유지)**: 규격을 어기는 브라우저(예: `getUserMedia`가 존재하는데
  내부 셈 버그로 무관한 이유의 `TypeError`를 던지는 고장난 인앱 WebView)는 여전히 오진될 수
  있다. 다만 이 경우에도 안내문 "Safari·Chrome 등 기본 브라우저에서 열어 주세요"는 그 셈을
  벗어나게 하는 실무적으로 유효한 조치이므로, 오진 비용이 낮다.

- **`AbortError` — 이번 릴리스에서는 매핑하지 않는다**(설계 리뷰 반영, 2026-08-07).
  최초 초안은 `AbortError → inUse`(다른 앱이 카메라를 점유 중)로 `NotReadableError`와 묶었으나,
  재검토 결과 보류한다. 근거:
  1. **규격상 근거가 약하다.** mediacapture-main은 `AbortError`를 `NotReadableError`(하드웨어
     읽기 실패)와 **명시적으로 분리**해 정의하는 잔여 범주다 — "다른 앱 점유"는 그 잔여 범주에
     들어갈 수 있는 한 사례일 뿐 유일한 원인이 아니다(탭 백그라운드 전환, 권한 플로우 중단 등도
     `AbortError`로 나올 수 있다). `NotReadableError`/`TrackStartError`의 기존 `inUse` 매핑은
     그대로 유지한다 — 이것은 규격상 근거가 분명하다.
  2. **이 설계 자신의 원칙과 어긋난다.** §0.2는 U-1·U-2·U-4를 "실기기 관측 **전**에는 행동을
     바꾸지 않고, Step 8에서 확정한 뒤에 결정한다"로 다룬다. `AbortError`만 예외로 두어 관측
     **전에** 손님 문구를 바꾸는 것은 그 원칙과 내부적으로 불일치한다. (실제로 이 불일치는 이미
     문서 안에 흔적이 있었다 — §2.3의 예시 문구·진단 캡션은 전부 `unknown/AbortError`를 써서
     암묵적으로 "AbortError는 아직 unknown"이라고 가정하고 있었다. 이번 수정은 그 예시들과
     §1.3의 매핑을 **일치시킨다**.)
  3. **보류해도 진단 목적은 100% 달성된다.** §2의 `detail` 메커니즘이 이미 `unknown/AbortError`
     형태로 원문 이름을 화면·진단에 노출한다 — 사유를 나누지 않아도 Step 8에서 U-3을 검증하는
     데 아무 지장이 없다. 사유를 나누는 것의 유일한 효과는 **손님에게 보이는 조치 문구**를
     바꾸는 것뿐이고, 그 문구가 틀릴 위험(U-3 미검증)을 지금 감수할 이유가 없다.

  실기기에서 `unknown/AbortError`가 반복 관측되고 "다른 앱을 닫으면 복구된다"가 확인되면,
  **Step 8 이후 별도의 1줄 커밋**으로 `classifyCameraFailure`에
  `case "AbortError": return "inUse";`를 추가한다 — 최초 초안이 이미 그 되돌리기 쉬움을
  근거로 들었던 것과 정확히 같은 이유로, 지금 넣지 않는 비용도 낮다.

### 1.4 `shouldTryNextStep`에 `TypeError` 추가

`mediaDevices`가 없으면 사다리 5칸을 내려가 봐야 5번 같은 `TypeError`가 난다.
`cameraConstraints.shouldTryNextStep`이 `"TypeError"`에서 **즉시 중단**하게 한다
(권한 거부와 같은 이유 — 제약을 낮춰도 결과가 같다).

### 1.5 경로 ②를 사유로 확정하려면 `attach()`가 이유를 돌려줘야 한다

지금 `FrameSource.attach(stream): Promise<boolean>`은 **성패만** 준다.
`videoFrameSource.ts:137-140`은 `err.message`만 로그로 남기고 `err.name`은 버린다.

```ts
// cameraTypes.ts
export type FrameSourceAttachResult =
  | { readonly ok: true }
  | { readonly ok: false; readonly errorName: string };

export interface FrameSource {
  /** 스트림을 붙이고 재생을 시작한다. 실패해도 **예외를 던지지 않는다**(01 §2.1). */
  attach(stream: MediaStream): Promise<FrameSourceAttachResult>;
  …
}
```

`videoFrameSource.attach`는 `video.play()` rejection의 `err.name`을 그대로 실어 보낸다
(iOS에서 실제로 나오는 것: `NotAllowedError` = 자동재생 정책, `AbortError` = 로드 중단).

---

## 2. (C) 실패 원인을 기기에서 꺼내기 — **진단 코드**

> (A)가 사유를 나눠도, 손님이 본 화면을 우리가 볼 수 없으면 여전히 규명이 안 된다.
> 지금 구조에서 원인 특정이 불가능한 것이 이번 사건의 근본 문제다(F-8·F-9).

### 2.1 실패 기록을 `{사유, 상세}` 쌍으로 바꾼다

```ts
// domain/capture/cameraFailure.ts (신규)
export interface CameraFailure {
  readonly reason: CameraFailureReason;
  /** 예외 이름 또는 경로 토큰. 새니타이즈를 통과하지 못하면 `null`. */
  readonly detail: string | null;
}

/** ⚠️ `CameraFailure`를 만드는 **유일한** 통로다(정적 검사 CAM-7). */
export function cameraFailure(reason: CameraFailureReason, rawDetail?: string | null): CameraFailure;

/**
 * 예외 → 사유+상세. **판정은 `classifyCameraFailure(err.name, secureContext)`에 그대로
 * 위임한다**(switch문을 여기 새로 만들지 않는다 — 두 판정처가 갈라지면 화면 문구와 진단
 * 사유가 어긋난다). 상세는 `err.name`이다(**`err.message`가 아니다**).
 */
export function classifyCameraFailureFrom(err: unknown, secureContext: boolean): CameraFailure;

/** 화면·진단에 노출할 짧은 코드. 상세가 없으면 사유만. */
export function formatCameraFailureCode(failure: CameraFailure): string;
//  → "unknown/AbortError" · "playbackBlocked/NotAllowedError" · "pipelineStalled/main-none"
```

**새니타이즈가 보안 경계다.**

```ts
const DETAIL_PATTERN = /^[A-Za-z0-9_.:+-]{1,32}$/;
```

- 공백·`@`·한글·32자 초과를 **전부 거부**하고 `null`로 접는다.
- 이메일(`@`), 토큰(길이·`/`·`=`), 게이트 키, 브라우저 예외 **메시지**(공백·한글 포함),
  카메라 `label`(공백 포함)이 이 관문을 통과할 수 없다.
- 통과 가능한 것은 사실상 **브라우저 예외 이름의 고정 어휘**와 우리가 만든 경로 토큰뿐이다.
- 기존 정적 검사 **DIAG-1**(게이트 키 값 미표시)·**AUTH-3**(로그인 실패에 email·token·code 금지)와
  같은 계열의 방어이며, 그 둘을 **깨지 않는다**(진단 코드는 계정·서버와 무관한 값이다).
- ⚠️ **CAM-7과 `DETAIL_PATTERN`의 역할은 다르다**(설계 리뷰 확인). CAM-7은 `cameraFailure(`
  바깥의 객체 리터럴 대입(예: `err.message`를 직접 담은 `{reason, detail}`)을 막는다 — *어떤
  통로로 만들어졌는지*만 고정한다. `cameraFailure(reason, rawDetail)`을 올바로 호출하면서
  `rawDetail`에 실수로 §2.2 표 밖의 값(예: 기기 `label`)을 넣는 실수는 CAM-7이 잡지 못한다 —
  **그 경우의 마지막 방어선은 `DETAIL_PATTERN`뿐이다**. §5.1의 새니타이즈 테스트가 이 경계를
  검증하는 이유가 여기 있다.

### 2.2 사유별 `detail`

| 사유 | detail | 예 |
|------|--------|-----|
| `permissionDenied` · `noDevice` · `inUse` · `unsupportedBrowser` · `unknown` | `getUserMedia` 예외 `name` | `inUse/NotReadableError` · `unknown/AbortError` |
| `insecureContext` | 없음(`null`) | `insecureContext` |
| `playbackBlocked` | `video.play()` rejection `name` | `playbackBlocked/NotAllowedError` |
| `pipelineStalled` | `{pipelineMode}-{previewMode}` | `pipelineStalled/main-none` |
| `pipelineSlow` | `f{가공프레임수}` | `pipelineSlow/f3` |

`pipelineStalled`의 상세가 특히 값이 크다 — `worker-transferred`면 rVFC/프레임 소스 쪽,
`main-none`이면 폴백 경로에서 프리뷰까지 못 붙은 것이라 방향이 완전히 갈린다.

### 2.3 어디에 보이는가

**① 실패 오버레이(게스트 포함 · 채택)** — `CameraPreview.tsx:105-116`

```
  카메라를 사용할 수 없습니다. 권한과 연결을 확인해 주세요.
              [ 다시 시도 ]
        오류 코드 unknown/AbortError        ← 신규 캡션(작은 글씨·고정폭)
```

손님에게는 의미 없는 문자열이지만 **현장 운영자·테스터가 읽어 보고할 수 있는 유일한 창구**다.
개인정보·비밀값이 원리적으로 섞일 수 없다(§2.1).

**② 진단 모달 [실패 사유] 행(채택)** — `diagnosticsPresenter.ts:172-181`

```
실패 사유   알 수 없음 · unknown/AbortError
```

**③ 로그 보강(채택)** — `cameraService.ts:263`이 `name`을 남기지 않던 것을 고친다(F-4).
사유 확정 3지점 모두 `failureCode`를 함께 남긴다.

### 2.4 채택하지 않은 것과 이유

| 후보 | 판정 | 이유 |
|------|------|------|
| 진단 모달의 로그인 전용 제약을 카메라 섹션에 한해 완화 | **채택 안 함** | 모달은 버킷·계정 id·서버 구성·로그 통계를 **한 화면에** 낸다. 카메라 섹션만 떼려면 새 모달 + 새 게이트가 필요하고 `03 §15.2`의 "로그인 전용" 규격을 바꿔야 한다. §2.3-①이 **같은 목적을 0의 노출 증가로** 달성한다 |
| 실패 로그 서버 전송 | **이번 범위 밖** | CSP는 이미 허용한다(F-10) — 막는 것은 기술이 아니다. ① 게스트가 부를 수 있는 **무인증 쓰기 엔드포인트**가 새로 생긴다(남용·과금), ② 개인정보 범위 판단이 필요하다(카메라 `label`에 기기명이 들어간다 — 새니타이즈 대상이 로그 전체로 넓어진다), ③ 이번 증상은 §2.3-①만으로 특정 가능하다. 필요해지면 **별도 설계**로 다룬다 |
| 오류 코드 [복사] 버튼 | **선택**(P2) | `adapters/platform/clipboard.copyText`가 이미 있어 5줄이면 되지만, 실패 화면에 버튼이 2개가 되어 [다시 시도]의 우선순위가 흐려진다. 코드가 짧아 눈으로 옮겨 적을 수 있다 |

---

## 3. (B) `VideoFrame` 실증 검사 + 영구 강등

> **다시 강조**: 이 결함이 터지면 화면에는 `pipelineStalled`가 뜬다. 이번에 관측된 `unknown`의
> 설명이 아니다(§0.2). 별개로 발견된 실제 버그이므로 함께 닫는다.

### 3.1 두 개의 다른 실패를 각각 막아야 한다

| 실패 | 지금 | 잡는 수단 |
|------|------|-----------|
| `new VideoFrame(...)` 생성자가 던진다 | 존재 검사만 통과 → 매 프레임 throw → warn만 | **사전 실증 프로브** |
| 생성은 되는데 **Worker로 transfer**가 실패한다(F-7) | 같은 catch로 흘러 warn만 | **런타임 영구 강등** |

프로브만으로는 두 번째를 못 잡고, 강등만으로는 첫 프레임을 낭비한다. **둘 다** 넣는다.

### 3.2 실증 프로브 — `isWorkerPipelineSupported()`와 같은 급으로

```ts
/** `VideoFrame`을 1×1 캔버스로 **실제로 하나 만들어** 본다(대조군: frameProcessorClient.ts:48-56). */
function probeVideoFrame(doc: Document): boolean {
  if (typeof VideoFrame === "undefined") return false;
  try {
    const canvas = doc.createElement("canvas");
    canvas.width = 1;
    canvas.height = 1;
    const frame = new VideoFrame(canvas, { timestamp: 0 });
    frame.close();          // ⚠️ GC 대상이 아니다 — 프로브도 반드시 닫는다
    return true;
  } catch {
    return false;
  }
}
```

⚠️ **`<video>`로 프로브하지 마라.** 재생 시작 전 `<video>`로 `VideoFrame`을 만들면 지원하는
브라우저에서도 던진다 → **거짓 음성**으로 zero-copy 경로를 영구히 잃는다. 캔버스가 유일하게
안전한 입력이다(캔버스 소스는 `timestamp` 필수).

### 3.3 3상태 · 단방향 전이

```
  unprobed ──probeVideoFrame() true──▶ videoFrame ──런타임 실패 1회──▶ imageBitmapDemoted
      │                                                                        │
      └──────────── false ────────▶ imageBitmap                     (되돌아가지 않는다)
```

- 상태는 **모듈 레벨 + 테스트 리셋 함수**로 둔다 — `frameThumbnails.ts:16-27`의 확립된 선례(F-12).
  카메라를 다시 열어도 강등이 유지돼야 한다(못 하던 기기가 갑자기 하게 되지 않는다).
- **되돌아가는 전이가 없다** = "프레임마다 재시도해서 매번 실패"가 구조적으로 불가능하다.
- 강등 로그는 **1회만** 남긴다(`warn`, `err.name` 포함). 초당 30회 로그가 링버퍼를 태우면 안 된다.

### 3.4 `grab()` 재구성 — 소유권 누수까지 닫는다

현재 `emit(new VideoFrame(video))`에서 `emit` 내부가 던지면 **만들어진 프레임이 닫히지 않는다**
(`VideoFrame`은 GC 대상이 아니다 — 04 §2.4).

```ts
async function grab(mediaTime: number): Promise<void> {
  if (converting) return;
  if (mediaTime === lastMediaTime) return;
  lastMediaTime = mediaTime;

  converting = true;
  try {
    if (videoFramePathUsable()) {
      let frame: VideoFrame | null = null;
      try {
        frame = new VideoFrame(video);
        emit(frame);
        frame = null;            // 정상: 소유권이 소비자에게 넘어갔다
      } catch (err) {
        closeQuietly(frame);     // 전송 실패로 남은 프레임을 닫는다(이중 close 방어)
        demoteVideoFramePath(err);
        return;                  // 이 프레임 1장은 버린다 — 다음 프레임부터 비트맵 경로
      }
    } else {
      emit(await createImageBitmap(video));
    }
  } catch (err) {
    // 트랙이 끊긴 직후 등. 루프를 죽이지 않는다.
    logger.warn("프레임 획득 실패", { reason: err instanceof Error ? err.message : String(err) });
  } finally {
    converting = false;
  }
}
```

`closeQuietly`는 `try { frame?.close(); } catch { /* 이미 detach됨 */ }` — transfer가 성공한
프레임은 detach되어 `close()`가 던질 수 있으므로 삼킨다.

### 3.5 강등을 진단에 보인다 — **보여야 한다**

`imageBitmap`(애초에 없음)과 `imageBitmapDemoted`(있었는데 깨짐)는 **성격이 다르다.**
전자는 정상 폴백, 후자는 브라우저 결함 신호이며 성능 예산 재측정 대상(선행 문서 R-6)이다.

```ts
// cameraTypes.ts
export type FrameTransferMode = "videoFrame" | "imageBitmap" | "imageBitmapDemoted";
```

| 진단 [프레임 전달] | 값 | tone |
|---|---|---|
| `videoFrame` | `VideoFrame(zero-copy)` | ok |
| `imageBitmap` | `ImageBitmap(폴백)` | neutral |
| `imageBitmapDemoted` | `ImageBitmap(강등)` | **warn** |

`FrameSource.transferMode()` → `cameraService.frameTransferMode()` → 진단 행.
라벨 매핑은 `Readonly<Record<FrameTransferMode, string>>`으로 두어 값을 늘리면 컴파일이 깨지게 한다
(`PREVIEW_MODE_LABEL`과 같은 형태).

---

## 4. 변경 파일 목록

| 파일 | 변경 |
|------|------|
| `webclient/src/domain/capture/cameraFailure.ts` | 사유 3종 추가 · `TypeError` 매핑(`AbortError`는 보류 — §1.3) · `CameraFailure` 타입 + `cameraFailure()`/`classifyCameraFailureFrom()`/`formatCameraFailureCode()`/새니타이즈 · `Record` 3벌 확장 |
| `webclient/src/adapters/camera/cameraConstraints.ts` | `shouldTryNextStep`에 `TypeError` 중단 추가 |
| `webclient/src/adapters/camera/cameraTypes.ts` | `FrameSourceAttachResult` · `FrameSource.attach` 반환형 변경 · `FrameSource.transferMode()` · `FrameTransferMode` |
| `webclient/src/adapters/camera/cameraService.ts` | `lastFailure: CameraFailure` 로 교체 · `failure()` 추가(`failureReason()`은 유지) · 경로 ②→`playbackBlocked` · 경로 ③→`pipelineSlow` · 사다리 소진 로그에 `name`·`failureCode` 추가 · `frameTransferMode()` 위임 |
| `webclient/src/adapters/camera/videoFrameSource.ts` | `probeVideoFrame` · 3상태 전이 + 영구 강등 · `grab()` 재구성 + `closeQuietly` · `attach` 반환형 변경(`play()` rejection `name` 전달) · `transferMode()` |
| `webclient/src/ui/strings.ts` | `camera.errors`에 3키 추가 · `camera.failureCodeLabel` · `diagnostics.frameTransfer*` 4키 |
| `webclient/src/ui/views/CameraPreview.tsx` | 실패 오버레이에 오류 코드 캡션 |
| `webclient/src/ui/views/cameraPreview.module.css` | `.failureCode`(caption 크기 · `--fg-muted` · 고정폭) |
| `webclient/src/screens/modals/diagnostics/diagnosticsPresenter.ts` | `CAMERA_FAILURE_LABEL` 3행 추가 · [실패 사유]에 코드 병기 · [프레임 전달] 행 신설 + deps |
| `webclient/src/screens/modals/diagnostics/DiagnosticsModal.tsx` | `frameTransferMode` deps 배선 |
| `docs/web-client/03-screens-spec.md` | §6.3 실패 사유 표 5행 → **9행**(신설 3 + 표에서 누락돼 있던 `pipelineStalled`) · 오류 코드 캡션 규격 |
| `docs/web-client/04-media-pipeline-web.md` | §2.3.1 `VideoFrame` 행에 "실증 프로브 + 영구 강등" 명시 · §2.3.2에 [프레임 전달] 4상태 표 · §2.3.3에 `pipelineSlow` 추가 |

> ⚠️ `03 §6.3`의 `unknown` 문구는 **바꾸지 않는다.** 사유가 좁아졌으니 "권한과 연결"이 덜 맞게
> 됐지만, 이제 `unknown`은 *정말로 모르는* 잔여 집합이라 가장 넓은 안내가 여전히 최선이다.
> 진짜 개선은 코드가 실측되어 매핑이 추가될 때 일어난다.

---

## 5. 테스트 전략

기존 `webclient/tests/unit/camera/`(3파일) 확장 + 도메인/진단 테스트 보강.

### 5.1 `tests/unit/domain/capture.test.ts` — 사유 카탈로그

- **기존 결함 동반 수정**: `ALL` 배열이 5종만 열거해 `pipelineStalled`가 **검증에서 빠져 있다**
  (`capture.test.ts:285-291`). 배열을 `CameraFailureReason` 9종 전부로 채운다.
  ⚠️ 배열을 손으로 유지하면 같은 누락이 반복된다 → **`Object.keys`로 `Record`에서 유도**하도록
  바꾼다(사유를 늘리면 테스트가 자동으로 커진다).
- `isCameraRetryable`: `unsupportedBrowser === false`, `playbackBlocked`·`pipelineSlow === true`.
- **새니타이즈**(신규): `sanitizeFailureDetail`이 이메일(`a@b.com`)·공백 포함 문자열·한글·
  33자 이상·JWT 형태를 전부 `null`로 접는지. 통과값은 `AbortError`·`main-none`·`f3`.
- `formatCameraFailureCode`: detail `null`이면 사유만, 있으면 `사유/상세`.

### 5.2 `tests/unit/camera/cameraFallback.test.ts` — 사유 확정 3경로

| 케이스 | 기대 |
|--------|------|
| 사다리 소진 + `AbortError` | `unknown` · 코드 `unknown/AbortError`(§1.3 — 매핑 보류. 기존 `capture.test.ts:270-273`의 `classifyCameraFailure("AbortError", true) === "unknown"` 단정과 그대로 일치해야 한다 — **이 단정을 고치지 않는다**) |
| 사다리 소진 + `InvalidStateError` | `unknown` · 코드 `unknown/InvalidStateError` |
| `mediaDevices` 없음(주입 `openStream`이 `TypeError` throw) | `unsupportedBrowser` · **`openStream` 호출 1회**(사다리 중단) |
| `attach`가 `{ok:false, errorName:"NotAllowedError"}` | **`playbackBlocked`** · 코드 `playbackBlocked/NotAllowedError` · 트랙 `stop()` 확인 |
| Ready 타임아웃 + 프레임 0 | `pipelineStalled` (기존 · 유지) |
| Ready 타임아웃 + 프레임 10장 | **`pipelineSlow`** ← **기존 292행이 `unknown`을 기대하므로 반드시 갱신** |
| 성공 후 `failure()` | `null` |

- `FakeSource.attach`(67행)·`FakeFrameSource.attach`(`cameraService.test.ts:79`) 두 곳의 시그니처를
  `FrameSourceAttachResult`로 갱신해야 한다 — 타입 변경의 실제 blast radius는 이 2파일뿐이다.

### 5.3 `tests/unit/camera/videoFrameSource.test.ts` (신규 파일)

기존 3파일 중 어디에도 `videoFrameSource`의 프레임 루프 테스트가 없다. 신설한다.

| 케이스 | 기대 |
|--------|------|
| 프로브 성공 → `transferMode() === "videoFrame"` | zero-copy 경로 |
| `VideoFrame` 미정의 → `"imageBitmap"` · `createImageBitmap` 호출 | 존재 검사 폴백 |
| 프로브가 던짐(생성자 있음, 생성 실패) → `"imageBitmap"` | **실증 검사가 존재 검사를 이긴다** |
| 소비자가 `process`에서 throw(전송 실패 흉내) | 1회 후 `"imageBitmapDemoted"` · **다음 프레임은 `createImageBitmap`** · `VideoFrame` 생성 시도 **0회** |
| 같은 상황에서 프레임 10장 | **강등 로그 1건**(반복 로그 금지) |
| 전송 실패 프레임 | `close()` 호출됨(누수 없음) |
| `attach`에서 `play()` reject | `{ok:false, errorName}` 반환 · 예외 전파 없음 |
| 강등 후 `detach`→`attach` 재시작 | 여전히 `"imageBitmapDemoted"`(전이 단방향) |

`VideoFrame`·`createImageBitmap`은 전역 스텁으로 주입한다(node 환경). 모듈 상태 리셋 함수를
`beforeEach`에서 호출한다(`resetThumbnailProbeForTests`와 같은 형태).

### 5.4 `tests/unit/adapters/cameraInvariants.test.ts` — 정적 불변식 3종 신설

| ID | 고정 대상 | 없었을 때 |
|----|-----------|-----------|
| **CAM-7** | `cameraService.ts`의 `lastFailure` 대입이 **`null` 또는 `cameraFailure(`** 뿐이다 | 객체 리터럴로 우회해 `err.message`가 화면 코드로 새어 나간다 |
| **CAM-8** | 사다리 모든 칸의 `video`가 truthy(빈 제약 없음) | `TypeError → unsupportedBrowser` 매핑이 오진이 된다 |
| **CAM-9** | `videoFrameSource.ts`가 `new VideoFrame(`을 부르기 전에 프로브 결과를 본다 + 강등 상태 변수가 존재한다 | 존재 검사만 하던 상태로 회귀(F-5·F-6) |

> ✅ **구현 컨벤션 명시(설계 리뷰 확인)**: CAM-7~9는 **CAM-1과 같은 방식**으로 구현한다 —
> `cameraInvariants.test.ts`의 기존 `stripComments()` 헬퍼로 주석·문자열을 먼저 걷어낸 뒤
> `src/`만 정규식으로 스캔한다. 이 컨벤션이 이미 실전에서 검증돼 있다: 주석 안의
> "`getUserMedia()` 호출이 필요하고"(`FlowViews.tsx:87`)가 `stripComments()` 덕분에 CAM-1을
> 오탐하지 않는다. 새 검사도 같은 이유로 주석·문자열 리터럴 안의 우연한 일치를 걸러야 한다.

기존 **CAM-1~CAM-6**·**DIAG-1**·**AUTH-3**은 전부 그대로 통과해야 한다.
특히 **CAM-1**(`getUserMedia(` 호출 파일 정확히 2개)에 주의 — 새 코드에 `getUserMedia` 호출을
추가하지 않는다. `typeof … .getUserMedia !== "function"` 형태의 **검사**는 정규식
`/getUserMedia\s*\(/`에 걸리지 않지만, 애초에 이번 변경은 새 호출을 만들지 않는다.

### 5.5 진단 표시

`tests/unit/…/diagnostics*` 계열에 [프레임 전달] 행 3값과 [실패 사유] 코드 병기를 추가한다.
`CAMERA_FAILURE_LABEL`이 `Record`이므로 사유 3종을 안 채우면 **타입 체크에서 먼저 걸린다**.

### 5.6 전체 검증 명령

```
cd webclient && npm run typecheck && npm test && npm run build
```

E2E는 카메라 실패 문구를 단정하지 않으므로(`tests/e2e/` 전수 확인) 영향 없다.

---

## 6. 구현 단계 (WBS)

> 형식: [`docs/templates/WBS_BLUEPRINT.md`](../templates/WBS_BLUEPRINT.md).
> 각 단계는 self-contained — fresh 에이전트가 그 단계만 읽고 실행 가능해야 한다.
> 검증된 사실 / 미검증 가정은 **§0.1 / §0.2**에 분리 기재돼 있다.
> 가정 매핑: **U-1·U-2 → Step 8**(배포 후 실기기 관측) ·
> **U-3 → Step 8에서 관측, 매핑 결정은 별도 후속 1줄 커밋**(설계 리뷰 반영 — §1.3) ·
> **U-4는 이번 범위 밖**(관측되면 별건으로 기표).

작업 디렉터리는 모두 `C:\STUDY\PROJECT\PhotoBooth\webclient` 기준이다.

---

### Step 1: 실패 사유 3종 + 분류 매핑 + 진단 코드 (도메인 순수층)

- **Context Brief**: `domain/capture/cameraFailure.ts`는 카메라 실패 사유의 **유일한 판정처**이며
  `Record` 3벌(문구 키·retryable·진단 라벨)로 사유 누락 시 컴파일이 깨지게 설계돼 있다.
  현재 `unknown`이 성격이 다른 3경로를 뭉개고 있어(§1.1) 현장에서 구분이 불가능하다.
  이 단계는 **순수 도메인만** 바꾼다. 어댑터·UI는 이후 단계다.
- **대상 파일**: `src/domain/capture/cameraFailure.ts`
- **선행 조건**: 없음
- **구현 내용**:
  1. `CameraFailureReason`에 `playbackBlocked`·`pipelineSlow`·`unsupportedBrowser` 추가(총 9종).
     각 사유 위에 **언제 확정되는지**를 주석으로 남긴다(기존 `pipelineStalled` 주석과 같은 밀도).
  2. `classifyCameraFailure`: `case "TypeError": → "unsupportedBrowser"`**만** 추가한다.
     **`AbortError`는 추가하지 않는다**(설계 리뷰 반영 — §1.3 · U-3 미검증. 기존 테스트
     `capture.test.ts:272`의 `classifyCameraFailure("AbortError", true) === "unknown"` 단정이
     이 단계 뒤에도 그대로 통과해야 한다). `insecureContext` 선판정은 **건드리지 않는다**
     (순서가 계약이다).
  3. `MESSAGE_KEY_BY_REASON`·`RETRYABLE_BY_REASON` 확장.
     `unsupportedBrowser: false`, `playbackBlocked: true`, `pipelineSlow: true`.
  4. 신규: `CameraFailure` 인터페이스, `DETAIL_PATTERN = /^[A-Za-z0-9_.:+-]{1,32}$/`,
     `sanitizeFailureDetail()`, `cameraFailure()`, `classifyCameraFailureFrom()`,
     `formatCameraFailureCode()`.
     ⚠️ `classifyCameraFailureFrom`은 `err.name`만 읽는다 — **`err.message`를 절대 읽지 않는다.**
     ⚠️ `classifyCameraFailureFrom`은 사유 판정을 `classifyCameraFailure(err.name, secureContext)`
     **호출로 위임한다** — switch문을 새로 만들지 않는다(두 판정처가 갈라지는 것을 막는다).
- **검증 명령**: `npm run typecheck` (이 시점에 `strings.ts`·`diagnosticsPresenter.ts`가
  `Record` 미충족으로 **에러가 나는 것이 정상이다** — Step 2·5가 채운다. 에러가
  그 2파일에만 국한되는지 확인한다)
- **완료 기준**:
  - [관측] `tsc --noEmit`의 에러가 `ui/strings.ts`와 `screens/modals/diagnostics/diagnosticsPresenter.ts`의
    `Record` 미충족 **2건 계열로만** 나온다 → 사유 확장이 의도한 컴파일 게이트가 실제로 작동함
  - [non-goal] `classifyCameraFailure(name, false)`는 여전히 무조건 `insecureContext`다
    (http 판정 순서 불변)
  - [trigger] 새 코드 생성은 `cameraFailure()`를 통해서만 — 다른 생성 경로를 만들지 않는다
- **롤백**: 이 단계 커밋 revert(이후 단계와 독립)
- [ ] 완료

---

### Step 2: 문구 3종 + 오류 코드 라벨

- **Context Brief**: Step 1이 사유를 3종 늘려 `STRINGS.camera.errors`의 `Record` 매핑이 깨진 상태다.
  손님 문구는 **손님이 실제로 할 수 있는 조치만** 말해야 한다는 원칙이 있다(`strings.ts:222-228`).
- **대상 파일**: `src/ui/strings.ts`
- **선행 조건**: Step 1
- **구현 내용**: `camera.errors`에 3키 추가(§1.2 표의 문구 그대로) + 각 키에 **왜 이 문구인지**
  주석. `camera.failureCodeLabel: "오류 코드"` 추가. `diagnostics`에
  `frameTransfer: "프레임 전달"`, `frameTransferVideoFrame: "VideoFrame(zero-copy)"`,
  `frameTransferBitmap: "ImageBitmap(폴백)"`, `frameTransferDemoted: "ImageBitmap(강등)"` 추가.
- **검증 명령**: `npm run typecheck && npx vitest run tests/unit/domain/capture.test.ts`
- **완료 기준**:
  - [관측] `strings.ts` 기인 타입 에러 0. 9종 사유 전부가 비어 있지 않은 문구로 매핑된다
  - [non-goal] 기존 6종 문구가 **한 글자도 바뀌지 않는다**(`03 §6.3` 규격 문구)
  - [trigger] 문구 노출은 `CameraPreview`의 `Failed` 상태에서만 — 다른 상태에서 렌더되지 않는다
- **롤백**: 이 단계 커밋 revert
- [ ] 완료

---

### Step 3: `FrameSource` 계약 변경 — attach 결과와 전달 경로

- **Context Brief**: `FrameSource.attach()`가 `boolean`만 돌려주어, `video.play()`가 왜 실패했는지
  (`err.name`)가 버려진다(`videoFrameSource.ts:137-140`). 그래서 스트림 획득 성공 후의 재생 실패가
  권한 실패와 같은 `unknown`으로 보고된다. 또 프레임 전달 경로(VideoFrame/ImageBitmap)를
  외부에서 읽을 방법이 없다.
- **대상 파일**: `src/adapters/camera/cameraTypes.ts`
- **선행 조건**: 없음(Step 1과 병렬 가능)
- **구현 내용**: `FrameSourceAttachResult` 판별 유니온 추가, `FrameSource.attach` 반환형 변경,
  `FrameTransferMode` 3값 유니온 + `FrameSource.transferMode()` 추가. 각 값의 의미를 주석으로 명시
  (특히 `imageBitmap`과 `imageBitmapDemoted`의 차이 — 진단에서 tone이 갈린다).
- **검증 명령**: `npm run typecheck` (구현체 3곳 — `videoFrameSource.ts`, 테스트 fake 2개 —
  에서 에러가 나는 것이 정상. **에러가 정확히 그 3파일에만** 나는지 확인)
- **완료 기준**:
  - [관측] 타입 에러가 `adapters/camera/videoFrameSource.ts` ·
    `tests/unit/camera/cameraFallback.test.ts` · `tests/unit/camera/cameraService.test.ts`
    **3파일에만** 발생 → 계약 변경의 blast radius가 설계대로 3곳이다
  - [non-goal] `FrameProcessor` 인터페이스는 **무변경**(가공기 계약은 이 변경과 무관)
  - [trigger] 없음(타입 전용 단계)
  - ⚠️ **함정(설계 리뷰 발견)**: `cameraService.ts:364-371`의 기존 코드 `if (!attached)`는
    이 단계만으로는 **타입 에러가 나지 않는다** — `attached`가 이제 객체이고 객체는 항상 참
    (truthy)이라 `!attached`는 항상 `false`로 평가된다(구문상 유효해 `tsc`가 못 잡는다).
    즉 이 단계를 단독으로 커밋해 배포하면 **재생 실패가 조용히 성공으로 처리**된다
    (Ready 타임아웃까지 8초 지연된 뒤에야 다른 사유로 실패한다). Step 5가 이 줄을
    `if (!result.ok)`로 고치기 전까지는 회귀 상태이므로, Step 5 없이 단독 배포하지 않는다.
- **롤백**: 이 단계 커밋 revert
- [ ] 완료

---

### Step 4: `videoFrameSource` — 실증 프로브 · 영구 강등 · 누수 차단

- **Context Brief**: `hasVideoFrame()`이 `typeof VideoFrame !== "undefined"` 존재 검사뿐이고
  (`videoFrameSource.ts:57-60`), `grab()`의 catch가 실패를 삼켜 **`createImageBitmap` 폴백으로 절대
  내려가지 않는다**(`87-108`). 대조군 `isWorkerPipelineSupported()`는 1×1 `OffscreenCanvas`를
  실제로 만들어 `getContext("2d")`까지 확인한다(`frameProcessorClient.ts:48-56`).
  ⚠️ 이 결함은 **이번에 관측된 `unknown`의 원인이 아니다** — 터지면 `pipelineStalled`가 뜬다.
  별개 버그로 닫는 것이다.
- **대상 파일**: `src/adapters/camera/videoFrameSource.ts`
- **선행 조건**: Step 3
- **구현 내용**: §3.2~§3.4 그대로.
  `probeVideoFrame(doc)`(**1×1 캔버스로 프로브 — `<video>`로 하면 거짓 음성**),
  모듈 레벨 3상태 + `resetVideoFramePathForTests()`,
  `demoteVideoFramePath(err)`(1회만 로그 · `err.name` 기록 · 단방향),
  `grab()` 재구성 + `closeQuietly()`,
  `attach()`가 `FrameSourceAttachResult` 반환(`play()` rejection의 `err.name` 전달),
  `transferMode()` 구현.
  ⚠️ `createHiddenVideoElement`의 1×1 투명 고정 배치는 **건드리지 않는다**(정적 검사 CAM-3).
- **검증 명령**: `npm run typecheck && npx vitest run tests/unit/adapters/cameraInvariants.test.ts`
- **완료 기준**:
  - [관측] `videoFrameSource.ts` 타입 에러 0. CAM-3(`display:none` 금지) 통과 유지
  - [non-goal] `VideoFrame`이 정상 동작하는 환경에서 전달 경로가 **강등되지 않는다** —
    성공 경로에 상태 변경이 없다
  - [trigger] 강등은 **런타임 실패 1회**에서만 일어난다. 프로브 실패는 강등이 아니라
    `imageBitmap`(처음부터 폴백)이다 — 두 상태를 섞지 않는다
- **롤백**: 이 단계 커밋 revert(Step 5와 독립 — Step 5는 `transferMode()` 부재 시 타입 에러로 드러난다)
- [ ] 완료

---

### Step 5: `cameraService` — 사유 확정 3경로 · 실패 기록 교체 · 로그 보강

- **Context Brief**: `unknown`이 확정되는 3지점(`cameraService.ts:259-265`·`364-371`·`374-393`)을
  각각 다른 사유로 가른다. 또 사다리 소진 로그(`263`)가 `err.name`을 남기지 않아
  **확정 단계의 예외 이름이 통째로 유실**된다(중간 단계 로그 `251-255`는 남긴다).
  ⚠️ `start()`는 **예외를 던지지 않는다**(01 §2.1) — 모든 실패는 `false`다.
  ⚠️ 카메라는 모듈 싱글턴 1개다(하드웨어 단일 소유).
- **대상 파일**: `src/adapters/camera/cameraService.ts`, `src/adapters/camera/cameraConstraints.ts`
- **선행 조건**: Step 1, Step 3, Step 4
- **구현 내용**:
  1. `lastFailureReason: CameraFailureReason | null` → `lastFailure: CameraFailure | null`.
     **대입은 `null` 또는 `cameraFailure(...)`/`classifyCameraFailureFrom(...)` 결과뿐**(CAM-7).
  2. `failure(): CameraFailure | null` 추가. `failureReason()`은 `failure()?.reason ?? null`로 유지
     (기존 호출처 `CameraPreview.tsx:39,57,61` · `DiagnosticsModal.tsx:85` 무변경).
  3. 경로 ②(`364-371`): 기존 `const attached = await source.attach(stream); if (!attached)`를
     `const result = await source.attach(stream); if (!result.ok)`로 바꾸고,
     `cameraFailure("playbackBlocked", result.errorName)`을 기록한다.
     ⚠️ **`if (!attached)`를 그대로 두면 안 된다**(Step 3의 함정) — 객체는 항상 참이라
     `tsc`가 이 실수를 잡지 못한다. §5.2의 "`attach`가 `{ok:false, ...}`" 테스트가 이 줄이
     실제로 고쳐졌는지 확인하는 유일한 안전망이다.
  4. 경로 ③(`374-393`): `meter.total === 0`이면 `pipelineStalled`
     (detail `` `${pipelineMode ?? "?"}-${previewMode}` ``), 아니면 **`pipelineSlow`**
     (detail `` `f${meter.total}` ``).
  5. 사다리 소진 로그(`260-264`)에 `name`과 `failureCode`를 추가한다. `message`는 유지해도 되나
     **`failureCode`에는 절대 넣지 않는다**.
  6. `frameTransferMode()` 접근자 추가(소스에 위임 · 닫혀 있으면 `null`).
  7. `cameraConstraints.shouldTryNextStep`에 `"TypeError"` 중단 추가 + 이유 주석.
- **검증 명령**: `npm run typecheck && npx vitest run tests/unit/camera/`
  (기존 `cameraFallback.test.ts:292`가 `unknown`을 기대하므로 **실패하는 것이 정상**이다 —
  Step 7에서 `pipelineSlow`로 갱신한다. 그 외 실패는 회귀다)
- **완료 기준**:
  - [관측] 타입 에러 0. `tests/unit/camera/`의 실패가 **`cameraFallback.test.ts`의
    "프레임은 오는데 조건 미달" 1건뿐**이다(의도된 기대값 변경)
  - [non-goal] `start()`가 어떤 경로에서도 **예외를 던지지 않는다**. 실패 시 트랙 `stop()`이
    유지된다(LED 잔존 없음). CAM-2(`frameRate` min/exact 금지)·CAM-4 통과 유지
  - [trigger] 사유 확정은 ① 사다리 소진 ② `attach` 실패 ③ Ready 타임아웃 3지점에서만.
    성공 경로에서 `lastFailure`는 `null`로 지워진다
- **롤백**: 이 단계 커밋 revert
- [ ] 완료

---

### Step 6: 화면·진단에 오류 코드 노출

- **Context Brief**: 진단 모달은 **로그인 전용**이라(`SettingsView.tsx:402-407`) 게스트 손님·테스터가
  열 수 없고, 클라이언트 로그는 기기 IndexedDB에만 쌓여 원격 전송 경로가 없다. 그래서 이번 사건에서
  현장 원인 특정이 원리적으로 불가능했다. **실패 화면의 짧은 코드가 유일한 창구**다.
  ⚠️ 코드에는 게이트 키·토큰·email·기기 label이 **원리적으로** 섞일 수 없어야 한다
  (기존 정적 검사 DIAG-1·AUTH-3와 같은 계열의 요구다). 방어선은 Step 1의 `DETAIL_PATTERN`이다.
- **대상 파일**: `src/ui/views/CameraPreview.tsx`, `src/ui/views/cameraPreview.module.css`,
  `src/screens/modals/diagnostics/diagnosticsPresenter.ts`,
  `src/screens/modals/diagnostics/DiagnosticsModal.tsx`
- **선행 조건**: Step 2, Step 5
- **구현 내용**:
  1. `CameraPreview`가 `getCameraService().failure()`를 상태로 들고(기존 `reason` state를 확장),
     `Failed` 오버레이 최하단에 `<p className={styles.failureCode}>{STRINGS.camera.failureCodeLabel} {code}</p>`.
     ⚠️ **JSX 텍스트 노드만 쓴다 — `innerHTML`/`dangerouslySetInnerHTML` 금지.**
  2. `.failureCode`: `var(--fs-caption)` · `var(--fg-muted)` · `font-variant-numeric: tabular-nums`
     (`.stats`와 같은 계열).
  3. `diagnosticsPresenter`: `CAMERA_FAILURE_LABEL`에 3행 추가,
     `failureReasonRow`가 `라벨 · 코드`로 표시, `frameTransferRow` 신설 +
     `DiagnosticsDeps.frameTransferMode` 추가, `DiagnosticsModal`에서 배선.
     ⚠️ **명세 구체화(설계 리뷰)**: `라벨 · 코드`를 표시하려면 `CameraFailureReason`만으로는
     부족하다 — `DiagnosticsDeps.cameraFailureReason: () => CameraFailureReason | null`을
     `DiagnosticsDeps.cameraFailure: () => CameraFailure | null`로 바꾸고(코드는 그 자리에서
     `formatCameraFailureCode`로 만든다), `failureReasonRow`의 파라미터 타입도 함께 바꾼다.
     기존 필드를 남겨두고 별 접근자를 병렬로 추가하지 않는다 — 두 값이 다른 시점의
     `lastFailure`를 읽어 사유와 코드가 어긋날 수 있다.
- **검증 명령**: `npm run typecheck && npm test`
- **완료 기준**:
  - [관측] `Failed` 상태에서 문구 아래 `오류 코드 <사유>/<상세>`가 렌더된다.
    진단 [실패 사유] 행에 같은 코드가, [프레임 전달] 행에 3값 중 하나가 표시된다
  - [non-goal] `Idle`·`Starting`·`Ready`에서는 코드 캡션이 **렌더되지 않는다**.
    게이트 키·계정 id·기기 label·예외 **메시지**가 코드에 나타나지 않는다.
    진단 모달의 **로그인 전용 게이트는 그대로 유지**된다(완화하지 않는다 — §2.4)
  - [trigger] 코드 표시는 카메라가 `Failed`로 전이했을 때만. 진단 모달은 여전히
    설정 [고급]의 버튼으로만 열린다
- **롤백**: 이 단계 커밋 revert(로직은 Step 5에 이미 있으므로 표시만 사라진다)
- [ ] 완료

---

### Step 7: 테스트 확장 + 정적 불변식 CAM-7·8·9

- **Context Brief**: 이번 변경의 회귀 위험은 ① 사유가 늘었는데 문구·retryable·진단 라벨 중 하나가
  누락 ② `err.message`가 화면 코드로 새는 것 ③ `VideoFrame`이 존재 검사로 되돌아가는 것이다.
  ①은 `Record`가 컴파일로 막지만 ②·③은 정적 검사가 필요하다.
  기존 `capture.test.ts:285-291`의 `ALL` 배열은 **`pipelineStalled`를 빠뜨린 상태**다 —
  손으로 유지하는 목록이라 같은 누락이 반복된다.
- **대상 파일**: `tests/unit/domain/capture.test.ts`,
  `tests/unit/camera/cameraFallback.test.ts`, `tests/unit/camera/cameraService.test.ts`,
  `tests/unit/camera/videoFrameSource.test.ts`(신규),
  `tests/unit/adapters/cameraInvariants.test.ts`
- **선행 조건**: Step 6
- **구현 내용**: §5.1~§5.5 전부.
  - `capture.test.ts`의 `ALL`을 **`Record`의 `Object.keys`에서 유도**하도록 바꾼다(수동 목록 폐기).
  - `cameraFallback.test.ts:292`의 기대값 `unknown` → `pipelineSlow`, `FakeSource.attach`(67행)를
    `FrameSourceAttachResult`로 갱신, §5.2 표의 신규 케이스 추가.
  - `cameraService.test.ts:79`의 `FakeFrameSource.attach`·`attachResult` 갱신.
  - `videoFrameSource.test.ts` 신설(§5.3 8케이스).
  - `cameraInvariants.test.ts`에 CAM-7·CAM-8·CAM-9 추가(§5.4).
- **검증 명령**: `npm run typecheck && npm test && npm run build`
- **완료 기준**:
  - [관측] vitest 전건 통과. 신규 케이스가 실제로 신규 코드를 태운다 —
    Step 4의 강등 로직을 임시로 되돌리면 `videoFrameSource.test.ts`가 **실패한다**
  - [non-goal] 기존 CAM-1~CAM-6 · DIAG-1 · AUTH-3가 **전부 통과 유지**된다.
    `getUserMedia(` 호출 파일 수가 여전히 **정확히 2개**다
  - [trigger] 없음(테스트 단계)
- **롤백**: 이 단계 커밋 revert(제품 코드 무변경)
- [ ] 완료

---

### Step 8: 문서 갱신 + 배포 · 실기기 관측(U-1·U-2·U-3 검증)

- **Context Brief**: 이 변경의 목적은 **원인을 고치는 것이 아니라 다음 발생 때 원인이 화면에
  드러나게** 하는 것이다. 배포 후 실기기에서 코드를 관측해야 §0.2의 가정 U-1·U-2·U-3이 닫힌다.
  선행 문서(2026-08-06)도 "고쳤다"가 아니라 "고칠 수 있는 것을 고쳤다"로 끝났고, 실기기 검증
  절차서(`docs/web-client/16`)의 iPad 세션 S7은 여전히 미수행이다.
- **대상 파일**: `docs/web-client/03-screens-spec.md`, `docs/web-client/04-media-pipeline-web.md`,
  이 문서(관측 결과 추가)
- **선행 조건**: Step 7
- **구현 내용**:
  1. `03 §6.3` 실패 사유 표를 **9행**으로 갱신(신설 3종 + 표에서 누락돼 있던 `pipelineStalled`) +
     오류 코드 캡션 규격 1행.
  2. `04 §2.3.1`의 `VideoFrame` 행에 "**실증 프로브 + 런타임 영구 강등**" 명시,
     `§2.3.2`에 [프레임 전달] 4상태 표, `§2.3.3`에 `pipelineSlow` 추가.
  3. 배포 후 iOS Safari에서 재현 → **화면의 오류 코드를 기록**하고 이 문서 §0.2 표를 갱신한다.
- **검증 명령**: `npm run build` 후 배포. 실기기에서 촬영 시도 → 실패 화면의 코드 기록.
  로그인 계정으로 진단 모달 → [실패 사유]·[프레임 전달]·[가공 경로]·[프리뷰 경로] 4행 캡처
- **완료 기준**:
  - [관측] 실패 시 화면에 `오류 코드 <사유>/<상세>`가 실제로 보이고, 그 값이 §1.2/§2.2 표의
    형태와 일치한다 → **U-1·U-2가 닫힌다**. 관측된 코드가 `unknown/AbortError`이면 §1.3에서
    보류한 U-3가 **관측만 닫히고 결정은 아직 열려 있다** — "다른 앱을 닫으면 복구되는가"까지
    확인해야 매핑을 추가할지 정할 수 있다(별도 후속 1줄 커밋 — 이 단계 범위 밖)
  - [non-goal] 정상 기기(데스크톱 Chrome)에서는 코드 캡션이 **한 번도 보이지 않는다**
    (실패가 없으므로). 촬영 성공 경로의 동작·성능이 바뀌지 않는다
  - [trigger] 관측은 실기기에서 촬영을 **직접 시도**했을 때만 얻어진다 —
    코드 리뷰나 빌드 통과로는 이 단계가 닫히지 않는다
- **롤백**: 문서 변경만 revert(제품 코드는 Step 1~7이 독립적으로 롤백 가능)
- [ ] 완료

---

## 7. 완결성 게이트 자체 검사

- [x] 검증된 사실(§0.1 F-1~F-13) / 미검증 가정(§0.2 U-1~U-4) 분리
- [x] 모든 가정에 검증 단계 매핑 — U-1·U-2 → Step 8(관측으로 확정), U-3 → Step 8(관측) +
      후속 1줄 커밋(매핑 결정 — 설계 리뷰 반영), U-4는 범위 밖으로 명시
- [x] 8단계 전부 7개 필수 필드(Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식. UI 단계(Step 2·6)에 non-goal·trigger 포함
- [x] 검증 명령이 자동 실행 가능(`npm run typecheck` / `npm test` / `npx vitest run <path>` / `npm run build`)
- [x] 단계 수 8개(3~12 범위)

## 8. 이 설계가 **닫지 못하는 것**

| # | 항목 | 상태 |
|---|------|------|
| **N-1** | **이번 iOS 실패의 원인** | **여전히 모른다.** 이 설계는 다음 발생 때 원인이 코드 한 줄로 드러나게 할 뿐이다 |
| **N-2** | 권한 프롬프트 중 `visibilitychange` hidden 억제(선행 R-1) | 미해결. 프롬프트 표시 순간 촬영이 취소되는 경로가 남아 있다 — iOS 후보 원인 중 하나이나 그 경로는 실패 문구가 아니라 홈 복귀로 나타난다 |
| **N-3** | `start()` 진행 중 `stop()`이 끼어드는 경합 | 미검토. `start()`에 disposed 체크가 없어 정지 뒤 스트림이 살아날 수 있다 — 별건 |
| **N-4** | `16 §S7` iPad 실기기 세션 | 여전히 미수행 |

---

## 9. 설계 리뷰 반영 (2026-08-07)

js-code-reviewer가 코드(`cameraService.ts`·`videoFrameSource.ts`·`cameraFailure.ts`·
`cameraPermission.ts`·`cameraConstraints.ts`·`frameProcessorClient.ts`·`mainThreadProcessor.ts`·
`diagnosticsPresenter.ts`·`cameraInvariants.test.ts`·`capture.test.ts`·
`cameraFallback.test.ts`·`cameraService.test.ts`·`web/firebase.json` 등)를 직접 읽고 대조해
검토했다. 아래는 그 결과다.

### 반영한 것

1. **`AbortError → inUse` 매핑을 보류했다**(§0.2 U-3, §1.3, §2.2 예시, §5.2, Step 1).
   설계 자신의 원칙(U-1·U-2·U-4는 관측 **전에는** 행동을 바꾸지 않는다)과 최초 초안이
   충돌했고, 실제로 §2.3의 예시·캡션(원래 167·203·212행)은 이미 전부 `unknown/AbortError`를
   쓰고 있어 §1.3의 `inUse` 매핑과 **문서 내부에서 서로 어긋나 있었다**. `detail` 메커니즘이
   진단 목적(U-3 검증)을 매핑 없이도 100% 달성하므로, 매핑은 실기기 확인 후 별도 1줄 커밋으로
   미룬다. `TypeError → unsupportedBrowser`는 `cameraConstraints.ts`를 직접 읽어 사다리
   5칸 모두 `video`가 truthy임을 확인했고 근거가 훨씬 강해 그대로 유지했다.
2. **Step 3의 타입-안전 함정을 명시했다**(Step 3, Step 5). `FrameSource.attach`의 반환형이
   `boolean → 객체`로 바뀌면 `cameraService.ts`의 기존 `if (!attached)`는 객체가 항상 참이라
   **타입 에러 없이** 항상 `false`로 평가된다 — 재생 실패가 조용히 성공 처리되는 회귀인데
   `tsc`가 못 잡는다. Step 3는 이 단계만 단독 배포하지 말라는 경고를, Step 5는 정확히
   `if (!result.ok)`로 바꾸라는 지시를 추가했다.
3. **`classifyCameraFailureFrom`이 `classifyCameraFailure`에 위임**하도록 명시했다(§2.1,
   Step 1) — 판정 로직을 두 곳에 복제하면 화면 문구와 진단 사유가 갈라질 수 있다.
4. **CAM-7과 `DETAIL_PATTERN`의 역할 차이를 명확히 했다**(§2.1) — CAM-7은 생성 통로만
   고정하고, 값 자체의 안전은 여전히 `DETAIL_PATTERN`(런타임 새니타이즈)이 담당한다는 것을
   명시해 향후 유지보수자가 CAM-7만으로 안전하다고 오해하지 않게 했다.
5. **`DiagnosticsDeps.cameraFailureReason` → `cameraFailure`로 개명·재설계를 명시했다**(Step 6)
   — [실패 사유] 행에 "라벨 · 코드"를 같이 보이려면 `CameraFailureReason` 하나로는 부족하다는
   점이 원문에 구체화돼 있지 않았다.
6. **CAM-7~9의 구현 컨벤션을 명시했다**(§5.4) — 기존 CAM-1이 `stripComments()` 후 `src/`만
   스캔하는 방식으로 이미 "주석 속 우연한 일치"(`FlowViews.tsx:87`의 주석)를 걸러내고 있음을
   확인했고, 새 검사도 같은 컨벤션을 따르게 했다.

### 검토했으나 바꾸지 않은 것(원래 설계가 옳다고 판단)

- **화면 오류 코드 노출(§2.3-①)**: `DETAIL_PATTERN`이 실질적 보안 경계로 충분하고(허용 문자
  집합이 공백·한글·`@`을 배제해 예외 메시지·이메일·기기 label이 통과할 수 없음을 직접
  확인), "진단 모달 로그인 전용 완화" 기각 논거도 `SettingsView.tsx:402-407`(렌더 가드)·
  실제 2중 게이트 구조와 일치했다. 변경 없음.
- **`VideoFrame` 영구 강등 설계(§3)**: `grab()` 재구성·`closeQuietly`·모듈 레벨 3상태를
  실제 코드 흐름(특히 `emit()`이 리스너 1개만 부르고 break하는 것, `createFrameSource()`가
  `start()`마다 새로 호출되는 것)을 따라가며 검증했다 — 모듈 레벨 배치가 아니면 강등이
  카메라 재시작마다 초기화된다는 설계의 전제가 실제로 맞았다. `mainThreadProcessor.ts`도
  `VideoFrame`/`ImageBitmap`을 try/finally로 닫는 같은 규약을 따르고 있어 `closeQuietly`의
  이중 close 방어가 두 가공기 경로 모두에서 안전했다. 변경 없음.
- **blast radius 주장**: `FrameSource.attach` 호출부가 정확히 `cameraService.ts` 1곳 +
  테스트 fake 2곳(`cameraFallback.test.ts:67`, `cameraService.test.ts:79`)뿐이고,
  `getUserMedia(` 실호출부가 정확히 2파일(`FlowViews.tsx`의 일치는 주석뿐)임을 grep으로
  직접 확인했다. 변경 없음.
- **WBS 규모(8단계, 11파일)**: 원인이 3경로 중 어느 것인지 모르는 상태이므로 한 경로만
  진단 가능하게 만들면 실기기에서 다른 경로가 걸렸을 때 다시 헛수고가 된다 — 3경로를
  동시에 다뤄야 하는 이유가 분명하다. `VideoFrame` 결함(B)을 별도 설계로 분리하는 대안도
  검토했으나, `FrameSource.attach` 반환형 변경이 (A)의 §1.5와 (B)의 §3.4 양쪽에서 같은
  파일(`videoFrameSource.ts`)을 건드려 분리해도 diff·리뷰 비용이 줄지 않는다. 변경 없음.
- **`TypeError` 매핑의 잔여 오진 위험**(비표준 브라우저 셈이 무관한 이유로 `TypeError`를
  던지는 경우): 실재하는 이론적 위험이나, 그 경우에도 안내문("다른 브라우저에서 열어
  주세요")이 우연히 유효한 회피책이 되어 비용이 낮다고 판단해 §1.3에 잔여 위험으로만
  기록하고 매핑은 유지했다.

원 설계의 핵심 골격(3경로 분리, `detail` 기반 진단 코드, `VideoFrame` 실증+영구 강등,
8단계 WBS)은 그대로 유지된다. 이번 리뷰는 손님에게 보이는 문구 1건의 리스크를 낮추고,
`tsc`가 잡지 못하는 타입 함정 1건을 문서에 못박고, 명세 모호성 2건을 구체화한 것이다.
