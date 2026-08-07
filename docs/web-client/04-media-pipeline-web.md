# 04 · 미디어 파이프라인 (웹 구현)

| 항목 | 값 |
|------|-----|
| 문서 | 카메라 획득 → 가공 → 스틸/타임랩스 분기 → 합성 → 필터까지의 **웹 구현 규격** |
| 규격 진실원 | **`docs/analysis/14-media-pipeline-spec.md`** — 좌표계·크롭 식·슬롯 기하·합성 순서·필터 파라미터·배속 산출은 그 문서가 진실원이다. 이 문서는 **"그 규격을 브라우저 API로 어떻게 만족시키는가"** 만 다룬다 |
| Windows 참조 | `src/MCPhoto.Capture/{OpenCvCameraService,CompositionService,Filters,TimelapseService,FfmpegRunner}.cs`, `src/MCPhoto.Core/Capture/{CropCalculator,PreviewReadiness,FfmpegArgs}.cs` |
| 갱신 규칙 | 알고리즘·파라미터가 바뀌면 `docs/analysis/14`를 먼저 고친다. 브라우저 API 대체 방식만 바뀌면 이 문서만 갱신 |

---

## 1. 전체 구조

```
                 getUserMedia (1080p·30fps 요청)
                          │
                    <video> (숨김, playsinline muted autoplay)
                          │  requestVideoFrameCallback (없으면 rAF)
                          ▼
              ┌─── VideoFrame / drawImage ───┐
              │   Worker: frameProcessor      │
              │   ① 거울 반전(설정 on)         │  ← 프레임당 1회만
              │   ② 중앙 크롭(목표 종횡비)      │  ← 프레임당 1회만
              └───────────────┬───────────────┘
                              │  가공된 프레임 1장(공유)
        ┌─────────────────────┼──────────────────────┬──────────────┐
        ▼                     ▼                      ▼              ▼
   프리뷰 렌더           스틸 캡처 요청           타임랩스 스풀러      fps 계산
 (canvas transferToImage) (구조 복제 → JPEG)     (간격마다 OPFS 기록)
```

**규격(`analysis/14 §2.2`)을 그대로 지킨다**: 가공은 프레임당 1회, 세 소비자가 결과를 공유한다. 소비자별로 따로 가공하면 성능 손실 + 프리뷰와 저장물 불일치가 생긴다(WM1).

### 1.1 Windows와의 구조 차이 (요약)

| 항목 | Windows | 웹 |
|------|---------|-----|
| 프레임 획득 | OpenCV `VideoCapture`(DirectShow) 전용 스레드 | `getUserMedia` + `requestVideoFrameCallback` |
| 가공 위치 | 전용 백그라운드 스레드 | **Worker + OffscreenCanvas**(메인 스레드 아님) |
| 세션 녹화 | ffmpeg에 rawvideo 파이프(`session.mp4`) | **하지 않는다** — 타임랩스를 직접 인코딩(WD2) |
| 타임랩스 | `session.mp4` → `setpts` 배속 재인코딩 | **촬영 중 OPFS 스풀(≤15fps) → 종료 시 실경과 기반 선별 → 30fps 타임라인 mp4 인코딩**(§7.2) |
| 합성·필터 | OpenCV | Canvas 2D + WebGL2 |
| 산출물 | `session.mp4` + `timelapse.mp4` + `final.{ext}` | `timelapse.mp4` + `final.{ext}` (`session.mp4` 없음) |

---

## 2. 카메라 획득

### 2.1 제약 **사다리** (2026-08-06 개정)

진실원 구현: **`adapters/camera/cameraConstraints.ts`**(순수) · 호출은 `cameraService.open()`.

```ts
// 넓은 것 → 좁은 것. 저장된 deviceId가 없으면 1·2를 건너뛴다.
1. device+1080p   { deviceId:{exact}, width:{ideal:1920}, height:{ideal:1080}, frameRate:{ideal:30} }
2. device+720p    { deviceId:{exact}, width:{ideal:1280}, height:{ideal:720} }
3. facing+1080p   { facingMode:{ideal:facing}, width:{ideal:1920}, height:{ideal:1080}, frameRate:{ideal:30} }
4. facing         { facingMode:{ideal:facing} }
5. any            { video: true }
```

> #### ⚠️ 왜 한 벌 제약을 폐기했는가 (모바일 실패의 직접 원인)
>
> 그 전까지 제약은 **한 벌**이었고 `frameRate: { min: 15 }`가 들어 있었다. `min`은 ideal이 아니라
> **강제 조건**이라, 저조도에서 15fps 아래로 내려가는 모드만 가진 안드로이드 기기가
> `OverconstrainedError`로 튕겼다. 그때 유일한 폴백이 `{ video: true }` 였고, 그러면
> **해상도와 `facingMode`가 통째로 사라져** 640×480 후면 카메라가 열렸다.
>
> Windows에는 이 문제가 없다. `cap.Set()`으로 1080p를 **요청만** 하고 장치가 거절하면 기본값으로
> 조용히 내려간다(`OpenCvCameraService.cs:88-92`). 사다리는 그 동작의 브라우저 재현이며,
> **의미 있는 것(장치 → 전후면)을 마지막까지 지킨다.**
>
> **`frameRate`에 `min`·`exact`를 되살리지 마라** — 정적 검사 **CAM-2**가 막는다.

| 규칙 | 내용 |
|------|------|
| `audio: false` 고정 | 타임랩스는 **무음**이 규격이다. 오디오 트랙을 얻으면 권한 요청 범위만 넓어진다 |
| 다음 칸으로 갈 조건 | `shouldTryNextStep(err.name)` — **권한 거부(`NotAllowedError`·`SecurityError`)에서는 즉시 중단**한다(제약을 낮춰도 결과가 같고, 손님만 기다린다). `NotReadableError`(점유)는 **계속 내려간다** — 해상도를 낮추면 열리는 기기가 실제로 있다 |
| 사유 확정 | **마지막 실패**로 확정한다. 마지막 칸이 `{video:true}`라서, 그것마저 실패한 이유가 손님에게 가장 정확하다 |
| 열린 칸 노출 | `cameraService.constraintStep()` → 진단 [적용된 제약] 행. "왜 해상도가 낮은가"를 현장에서 답한다 |
| 전/후면 힌트 | `webExtras.CameraFacing`이 3·4칸에 들어간다. **호출측이 `start({facing})`으로 넘긴다** — 정적 검사 **CAM-5**가 배선을 고정한다(설정 UI가 있는데 값이 전달되지 않던 상태를 막는다) |
| 실제 값 확인 | `track.getSettings()`의 `width/height/frameRate`를 **진단 화면에 표시**. 요청값과 다를 수 있다(WC2) |
| 장치 열기 실패 | **예외를 위로 던지지 않는다.** `false` 반환 → 상위가 `Failed` 상태로 안내(`analysis/14 §2.1`) |
| 시작 멱등성 | 이미 실행 중이면 무시하고 성공 반환. **다른 장치로 바꿀 때는 정지 후 재시작** |
| 단일 소유 | 카메라 어댑터는 **모듈 싱글턴 1개**. 실촬영·카메라 테스트 모달이 같은 인스턴스를 공유 |
| 정지 | `track.stop()` + `<video>.srcObject = null` + Worker에 정지 통지. **모달·화면 이탈에서 반드시 호출** |

### 2.2 `<video>` 요소 규격 (모바일 필수 조건)

```html
<video autoplay muted playsinline disablepictureinpicture style="display:none"></video>
```

| 속성 | 이유 |
|------|------|
| `playsinline` | **iOS에서 없으면 전체화면 재생으로 강제 전환**되어 파이프라인이 깨진다 |
| `muted` + `autoplay` | 자동재생 정책 통과 |
| 숨김 | 프리뷰는 **가공된 canvas**를 보여준다(WM1). `<video>`를 직접 보여주면 거울·크롭이 반영되지 않는다 |
| `play()` 호출 | `autoplay`가 실패할 수 있으므로 `play()`를 호출하고 rejection은 로그 후 재시도(사용자 제스처 컨텍스트에서) |

> #### ⚠️ 숨김은 `display:none`이 아니다 (2026-08-06 교정)
>
> 위 코드 블록의 `style="display:none"`은 **폐기됐다.** WebKit은 렌더링 트리에서 빠진 `<video>`에
> 대해 `requestVideoFrameCallback`을 발화하지 않는 경우가 있고, 그러면 프레임이 **한 장도 오지
> 않아** 8초 뒤 Ready 타임아웃이 난다. 화면에는 권한 문제와 구분되지 않는 실패 문구만 뜬다.
>
> 현재 구현(`createHiddenVideoElement`)은 **1×1 투명 고정 배치**다 — 렌더링 트리에 남겨 프레임
> 콜백이 돌게 하고, 시각·접근성에서는 제거한다(`aria-hidden="true"`).
>
> ```ts
> position: fixed; top: 0; left: 0; width: 1px; height: 1px;
> opacity: 0; pointer-events: none; z-index: -1;
> ```
>
> 정적 검사 **CAM-3**이 `display = "none"` 재발을 막는다.

### 2.3 OS·브라우저별 주의

| 플랫폼 | 주의 |
|--------|------|
| **iOS / iPadOS** | Safari(및 홈화면 PWA)에서 동작. `playsinline` 필수. 백그라운드 전환 시 트랙이 `muted`/중단될 수 있어 복귀 시 **스트림 상태 재확인**. 카메라 권한은 사이트 설정에서 "허용"으로 고정 권장 |
| **Android Chrome** | 정상. 후면 카메라가 여러 개인 기기가 많다 → 장치 목록에서 사용자가 고를 수 있어야 한다 |
| **Windows / macOS** | 정상. 외장 웹캠 다수 환경에서 `deviceId` 저장·매칭 폴백이 중요(WC3) |
| **모든 플랫폼** | **HTTPS(또는 localhost) 필수** — 보안 컨텍스트가 아니면 `mediaDevices`가 `undefined`다 |
| 권한 상태 | `navigator.permissions.query({name:"camera"})`는 지원이 고르지 않다 → **지원 시에만** 진단 표시에 사용하고, 로직은 `getUserMedia` 결과로 판정 |

#### 2.3.1 파이프라인이 의존하는 API의 iOS/iPadOS Safari 가용 시점 (지원 하한 근거)

[10 §6.1](./10-testing-and-acceptance.md)이 **Safari 17+**를 최소로 잡은 근거다. 각 항목은 **런타임 기능 감지**로 확인하고, 표는 기기 선정용이다.

| API | 파이프라인에서의 역할 | Safari 가용 | 없을 때의 폴백 |
|-----|----------------------|-------------|----------------|
| `getUserMedia` + `playsinline` | 카메라 획득 | 오래전부터 ○ | 없음(앱 사용 불가) |
| `requestVideoFrameCallback` | 프레임 도착 통지 | **15.4+** | `requestAnimationFrame` + `video.currentTime` 중복 스킵(§2.4) |
| `OffscreenCanvas`(**2D**) | Worker 가공 캔버스 | **16.4+** | ✅ **구현됨** → `adapters/camera/mainThreadProcessor.ts`(메인 스레드 가공 · "저성능 모드"로 진단 표시) |
| `transferControlToOffscreen` | 프리뷰 zero-copy 렌더 | 16.4+ | ✅ **구현됨** → Worker `transferToImageBitmap` → `previewFrame` 메시지 → 메인 `drawImage` |
| `OffscreenCanvas.getContext("webgl2")` | **뷰티 필터 Worker 경로** | **17+**(16.4는 2D만 — `getContext("webgl")`이 `null`) | `ImageData` CPU 폴백(§6.2) 또는 메인 스레드 WebGL2 |
| `createImageBitmap(video)` / `(blob)` | Worker 프레임 전달·합성 입력 | 15+ ○ | 없음(필수) |
| `createImageBitmap` **resize 옵션** | 썸네일·슬롯 축소 | **오래 미지원**(WebKit 이력) | **필수 폴백**: 작은 캔버스에 `drawImage` 또는 절반씩 단계 축소(§5.2) |
| `VideoFrame`(WebCodecs) | zero-copy 프레임 전달 | 16.4+ | `createImageBitmap(video)`(§2.4의 폴백 경로). ⚠️ **존재 검사로는 부족하다** — 1×1 캔버스 **실증 프로브** + 런타임 실패 시 **영구 강등**(§2.3.2) |
| `VideoEncoder`(WebCodecs) | 타임랩스 경로 B | 16.4+ | 경로 A(MediaRecorder mp4) → 경로 C(§7.3b) |

> **`OffscreenCanvas` 2D가 없으면 §1의 구조가 성립하지 않는다.** 기능 감지에서 실패하면 "가공을 메인 스레드에서 수행"으로 축소하되, 그 경로는 성능 예산([04 §8](#8-성능메모리-예산))을 만족하지 못할 수 있으므로 **진단에 "저성능 모드"로 표시**하고 실기기 검증 결과로 지원 여부를 판정한다.

#### 2.3.2 폴백 배선 상태와 판정 주체 (2026-08-06 신설)

> **이 절이 신설된 이유**: §2.3.1의 폴백 2개가 **문서에만 있고 코드에 없었다.**
> `isWorkerPipelineSupported()`는 정의만 되고 호출처가 0건이었고(dead code), `bindPreview`는
> 이관 실패 시 `false`만 돌려주고 끝났다. 결과:
> - `OffscreenCanvas` 없음 → `processed`가 한 번도 오지 않음 → 8초 타임아웃 → 사유 `unknown`
> - 이관 불가 → **상태는 Ready인데 화면은 검은색** (실패로 보고되지도 않는다)
>
> 데스크톱 Chrome은 모든 API가 있어 최적 경로만 타므로 이 결함이 드러나지 않았다.

| 판정 | 주체 | 실패 시 |
|------|------|---------|
| Worker 가공 가능? | `isWorkerPipelineSupported()` — `Worker` + `OffscreenCanvas` **생성 후 `getContext("2d") !== null`** 까지 확인 | `createMainThreadProcessor()` |
| `new Worker(...)` | `spawnFrameProcessor()`의 `try` | `createMainThreadProcessor()` |
| 가공기 생성 자체 | `cameraService.start()`의 `try` | `Failed("pipelineStalled")` — **예외를 위로 던지지 않는다** |
| 프리뷰 연결 방식 | **가공기**(`FrameProcessor.bindPreview(HTMLCanvasElement)`) | 이관 → 비트맵 → `false` |

**프리뷰 경로 4상태**(`FrameProcessor.previewMode()` · 진단 [프리뷰 경로] 행):

| 값 | 의미 |
|----|------|
| `transferred` | `transferControlToOffscreen()` 성공 — 권장 경로 |
| `bitmap` | 이관 불가 → Worker가 프레임마다 `transferToImageBitmap()`을 보내고 메인이 그린다 |
| `direct` | 메인 스레드 가공기가 화면 캔버스에 직접 그린다 |
| `none` | **화면이 검다** — 진단에서 `bad` tone |

⚠️ 비트맵 전송은 Worker `processFrame`의 **맨 마지막**이다. `transferToImageBitmap()`이 캔버스를
**비우므로**, 스틸·스풀보다 앞에 두면 같은 프레임의 컷과 타임랩스가 빈 이미지가 된다.

⚠️ **캔버스 재이관은 불가능하다.** 한 번 이관된 `<canvas>`는 메인에서 `getContext("2d")`조차
실패하므로, 카메라 재시작 시 `CameraPreview`가 **`key`를 증가시켜 DOM 노드를 갈아 끼운다**.
이것이 없으면 [다시 시도] 이후 화면이 영구히 검은색이 된다.

**프레임 전달 경로 3상태**(`FrameSource.transferMode()` · 진단 [프레임 전달] 행 · 2026-08-07 신설):

| 값 | 의미 | 진단 tone |
|----|------|-----------|
| `videoFrame` | `VideoFrame` **실증 프로브 통과** — zero-copy 경로 | `ok` |
| `imageBitmap` | `VideoFrame`이 애초에 없거나 프로브가 실패 — 정상 폴백 | `neutral` |
| `imageBitmapDemoted` | `VideoFrame`이 있었는데 **런타임에 깨져 강등** — 브라우저 결함 신호 · 성능 예산 재측정 대상 | `warn` |

카메라가 닫혀 있으면 `null`("알 수 없음")이다. **네 번째 상태처럼 보이지만 값이 아니다.**

⚠️ **존재 검사(`typeof VideoFrame !== "undefined"`)만으로는 부족하다.** 생성자는 있는데 생성이
실패하는 구현이 있고, 그러면 매 프레임 throw하며 가공 프레임이 **0장**이 된다(→ `pipelineStalled`).
`isWorkerPipelineSupported()`가 1×1 `OffscreenCanvas`를 실제로 만드는 것과 **같은 급**으로,
1×1 **캔버스**로 `VideoFrame`을 하나 만들어 본다. `<video>`로 프로브하면 재생 시작 전이라
지원 브라우저에서도 던져 **거짓 음성**이 되고 zero-copy 경로를 영구히 잃는다.

⚠️ **강등 전이는 단방향이며 상태는 모듈 레벨이다.** `new VideoFrame(...)`이 성공해도 Worker로의
`postMessage(..., {transfer})`가 실패할 수 있으므로(프로브로는 못 잡는다) 런타임 실패 1회에서
영구 강등한다. 되돌아가는 전이가 없어야 "프레임마다 재시도해서 매번 실패"가 구조적으로 불가능하고,
모듈 레벨이어야 카메라 재시작(`createVideoFrameSource()`가 매번 새로 불린다)을 넘어 유지된다.
강등 로그는 **1회만** 남긴다 — 초당 30회 경고가 로그 링버퍼를 태우면 안 된다.
전송에 실패해 남은 프레임은 `closeQuietly()`로 닫는다(성공한 프레임은 detach되어 `close()`가
던질 수 있으므로 삼킨다).

#### 2.3.3 Ready 타임아웃 · 재생 실패 사유 분리

| 조건 | 사유 | 진단 코드 상세 |
|------|------|----------------|
| Ready 타임아웃 · 가공 프레임 **0장** | `pipelineStalled` | `{가공경로}-{프리뷰경로}` (예: `main-none` · `worker-transferred`) |
| Ready 타임아웃 · 가공 프레임 **1장 이상** | `pipelineSlow` | `f{가공프레임수}` (예: `f3`) |
| `FrameSource.attach()`가 `{ok:false}` | `playbackBlocked` | `video.play()` rejection의 `name` (예: `NotAllowedError`) |

전에는 셋 다 `unknown`("권한과 연결을 확인해 주세요")이라 "권한 문제인지 브라우저 능력 문제인지"
현장에서 구분할 수 없었다. `pipelineStalled`의 상세가 특히 값이 크다 — `worker-transferred`면
rVFC/프레임 소스 쪽, `main-none`이면 폴백 경로에서 프리뷰까지 못 붙은 것이라 방향이 완전히 갈린다.

로그에는 `pipelineMode`·`previewMode`·`frameTransferMode`·`constraintStep`과 `failureCode`를
함께 남긴다. ⚠️ `failureCode`에는 예외 **메시지**를 절대 넣지 않는다(기기명·경로 혼입 — 03 §6.3).

### 2.4 프레임 획득 루프

```ts
// 우선: HTMLVideoElement.requestVideoFrameCallback (실제 프레임 도착 시점 통지)
// 폴백: requestAnimationFrame (프레임 중복 처리 가능 — mediaTime으로 중복 스킵)
```

| 규칙 | 내용 |
|------|------|
| 중복 프레임 | `rVFC`의 `mediaTime`(또는 `video.currentTime`)이 이전과 같으면 **처리 스킵** |
| Worker 전달 | `VideoFrame`(WebCodecs) 지원 시 `new VideoFrame(video)` → `postMessage(frame, [frame])` 전송(zero-copy) 후 **반드시 `frame.close()`** |
| Worker 전달(폴백) | `createImageBitmap(video)` → `postMessage(bitmap, [bitmap])`. Worker에서 `close()` |
| 프레임 스킵 | 이전 가공이 끝나지 않았으면 **최신 프레임으로 덮어쓰고 큐를 쌓지 않는다**(`analysis/14 §2.2`) |
| 리소스 | `VideoFrame`·`ImageBitmap`은 **GC 대상이 아니다.** `close()`를 빠뜨리면 수십 프레임 만에 메모리가 폭발한다 → 모든 경로에 `try/finally` |

---

## 3. Ready 게이트 (`analysis/14 §2.3`)

```
ready = (누적 프레임 수 >= 8) AND (대기 시작 후 경과 >= 500ms) AND (현재 fps > 0)
```

| 항목 | 웹 구현 |
|------|---------|
| 프레임 수 | Worker가 가공 완료한 프레임 카운트(획득 카운트가 아니라 **가공 완료** 기준) |
| 경과 | `performance.now()` 델타 |
| fps | 최근 1초 윈도우의 가공 완료 프레임 수 |
| 신호 | 전이 시 **1회만** 발행 |
| 타임아웃 | **8000ms** 초과 시 `Failed`(무한 로딩 금지) |
| 적용 범위 | 실촬영과 **카메라 테스트 모달이 동일 규칙** |
| 순수 함수 | 판정 자체는 `domain/capture/previewReadiness.ts`(Windows `PreviewReadiness.cs` 이식, 테스트 `PreviewReadinessTests.cs`) |

---

## 4. 거울 반전 + 중앙 크롭 (프레임당 1회)

### 4.1 순서와 구현

```ts
// Worker 안에서 OffscreenCanvas 1개를 재사용한다(매 프레임 새로 만들지 않는다)
const crop = centerCrop(srcW, srcH, targetAspect);      // domain 순수 함수
canvas.width = crop.w; canvas.height = crop.h;          // 크기 변할 때만 재설정
if (mirror) { ctx.setTransform(-1, 0, 0, 1, crop.w, 0); } else { ctx.setTransform(1,0,0,1,0,0); }
ctx.drawImage(frame, crop.x, crop.y, crop.w, crop.h, 0, 0, crop.w, crop.h);
```

| 규칙 | 내용 |
|------|------|
| **CSS `transform: scaleX(-1)` 금지** | 프리뷰만 반전되고 저장 픽셀은 원본이 되어 WYSIWYG가 깨진다(WM1, `analysis/14 §2.4` 규격 위반) |
| 반전은 canvas 변환으로 | 위 코드처럼 **가공 단계에서** 적용한다. 프리뷰·스틸·타임랩스가 그 결과를 공유하므로 자동으로 일치한다 |
| 런타임 토글 | 설정 저장 시 즉시 반영(Worker에 메시지) |
| `targetAspect` | **대표 슬롯의 종횡비**(현행: 첫 슬롯). 프레임 선택 시 Worker에 전달 |
| 크롭 식 | `analysis/14 §3`의 의사코드를 **정수 나눗셈까지 동일하게** 구현(§9 대응표) |

### 4.2 프리뷰 렌더

| 규칙 | 내용 |
|------|------|
| 경로 | Worker의 `OffscreenCanvas.transferToImageBitmap()` → 메인으로 전송 → 화면 canvas에 `drawImage` 후 `close()` |
| 더 빠른 경로 | 화면 canvas를 `transferControlToOffscreen()`으로 Worker에 넘기면 메인 스레드 왕복이 없다. **권장 기본값**(단 프리뷰 위에 오버레이를 겹칠 때는 별 레이어로 DOM 요소를 쓴다) |
| 렌더 스킵 | 이전 렌더가 안 끝났으면 최신 것만 그린다 |
| 버퍼 재사용 | 캔버스·비트맵을 매 프레임 새로 만들지 않는다(GC 압력) |
| 오버레이 | 플래시(하양)·카운트다운 숫자는 **canvas가 아니라 DOM 오버레이**로 그린다(합성 픽셀에 섞이면 안 된다) |

---

## 5. 스틸 캡처와 합성

### 5.1 스틸 캡처

| 규칙 | 내용 |
|------|------|
| 원자성 | 요청이 대기 중이면 **다음 가공 프레임에서 버퍼를 복제**해 완성한다(`analysis/14 §2.2`) |
| 복제 방법 | Worker에서 `canvas.transferToImageBitmap()`(새 비트맵) 또는 `convertToBlob({type:'image/jpeg', quality:0.95})` |
| 보관 | ① **OPFS `sessions/{id}/cut{i}.jpg`**(quality 0.95) ② 메인에 썸네일용 축소 `ImageBitmap` |
| 왜 즉시 JPEG인가 | 10컷 × 1080p RGBA를 메모리에 들면 iOS 탭이 죽는다(WR8) |
| 합성 입력 | 합성 시 OPFS의 JPEG를 순차로 `createImageBitmap`해서 쓰고 **바로 `close()`** |

### 5.2 합성 (`analysis/14 §5`)

```
compose(frame, cuts[], filter, outFormat):
  1. assert cuts.length === frame.slots.length            # 다르면 오류(M12)
  2. 배경 = 프레임 이미지 → 출력 캔버스 = 프레임 원본 해상도
  3. 슬롯을 index 오름차순 정렬
  4. for i: slot = slots[i], cut = cuts[i]
       slotRect = clampSlotToFrame(slot, frameW, frameH)   # 합성용 클램프(편집기용과 식이 다르다)
       filtered = applyFilter(cut, filter)                 # 컷 전체 일괄
       srcCrop  = centerCrop(filtered.w, filtered.h, slotRect.w / slotRect.h)
       scaled   = resize(filtered[srcCrop] → slotRect 크기, 축소에 강한 보간)
       배경[slotRect] = scaled                             # 덮어쓰기(알파 블렌딩 아님)
  5. 출력 인코딩
```

| 웹 구현 항목 | 방법 |
|--------------|------|
| 출력 캔버스 | `OffscreenCanvas(frameW, frameH)` (Worker에서 수행 — 메인 스레드 블로킹 금지) |
| 프레임 이미지 | 번들 프레임은 same-origin, **서버 프레임은 `crossOrigin="anonymous"` fetch 후 `createImageBitmap`**(WM2). 오염되면 `convertToBlob`이 예외 |
| 축소 보간 | `createImageBitmap(src, {resizeWidth, resizeHeight, resizeQuality: "high"})` — 축소에 강하다. **미지원·품질 미달 시 2단 폴백**: 절반씩 반복 축소(mipmap 방식) 후 `drawImage`. ⚠️ **resize 옵션은 WebKit에서 오래 미지원이었고, 미지원 시 옵션이 조용히 무시된다**(예외가 아니다) → **결과 `ImageBitmap`의 `width`가 요청한 `resizeWidth`와 같은지 1회 확인해 경로를 결정**하고 그 판정을 캐시한다 |
| `imageSmoothingQuality` | `drawImage` 사용 경로에서는 `ctx.imageSmoothingEnabled = true; ctx.imageSmoothingQuality = "high"` |
| 덮어쓰기 | `ctx.globalCompositeOperation = "source-over"` 기본값 + 슬롯 영역을 먼저 `clearRect`하지 않는다(불투명 이미지를 덮으므로 동일 결과) |
| 출력 인코딩 | JPG: `convertToBlob({type:"image/jpeg", quality:0.95})` / PNG: `{type:"image/png"}` |
| JPEG 품질 근거 | OpenCV `imwrite` 기본 JPEG 품질이 **95**다. 0.95로 맞춘다 |
| 프레임 이미지 부재 | **명확한 오류로 실패**한다(빈 배경으로 조용히 진행 금지) |

> **주의**: 프레임 PNG의 슬롯 영역은 비어 있어야 한다. 프레임이 컷 위로 올라오는 오버레이 디자인은 현재 파이프라인이 표현하지 않는다(`analysis/14 §5.3`).

---

## 6. 필터 (`analysis/14 §6`)

입력·출력 모두 3채널 8bit 등가. **원본을 변형하지 않고 새 버퍼를 만든다.** 컷 전체 일괄 적용.

| 필터 | 정확한 연산 | 웹 구현 | 픽셀 동일성 |
|------|-------------|---------|-------------|
| **원본** | 복사만 | — | 완전 동일 |
| **흑백** | `y = 0.299R + 0.587G + 0.114B` → 3채널 복원 | **직접 계산**(WebGL 셰이더 또는 `ImageData` 루프) | 반올림 오차 ±1 |
| **밝게** | `dst = saturate(src * 1.1 + 20)` | 동상 | ±1 |
| **뷰티** | ① bilateral(`d=7, σColor=40, σSpace=7`) ② `smooth*0.6 + src*0.4` ③ `alpha=1.03, beta=6` | **WebGL2 프래그먼트 셰이더**(7×7 joint bilateral) | 근사(§6.2) |

### 6.1 흑백에서 CSS filter를 쓰면 안 되는 이유

`ctx.filter = "grayscale(1)"`(및 CSS `filter`)은 **CSS Color 스펙의 계수**(BT.709 계열: 0.2126/0.7152/0.0722)를 쓴다. 규격은 **BT.601(0.299/0.587/0.114)** 이다. CSS filter를 쓰면 Windows 결과와 눈에 보이게 달라진다. **직접 계산할 것.**

### 6.2 뷰티 필터 구현 지침

```glsl
// 7×7 bilateral (d=7 → 반경 3), σColor=40(0~255 스케일), σSpace=7
// wSpace = exp(-(dx*dx+dy*dy) / (2*σSpace*σSpace))
// wColor = exp(-(||c - c0||^2) / (2*σColor*σColor))     // OpenCV는 채널별 차이의 제곱합 사용
// smooth = Σ(w*c) / Σ(w)
// out = clamp((smooth*0.6 + c0*0.4) * 1.03 + 6/255, 0, 1)
```

| 항목 | 규격 |
|------|------|
| 색 공간 | **sRGB 값 그대로**(선형화하지 않는다). OpenCV가 8bit 값에 직접 연산하므로 선형화하면 결과가 달라진다 |
| σ 스케일 | 셰이더에서 0~1 정규화 값을 쓰면 σColor를 **40/255**로 환산한다 |
| WebGL2 미지원 | `ImageData` CPU 폴백(느리지만 정확). 1080×1440 기준 예산 초과 시 **반해상도 계산 후 업샘플** 허용 |
| **Worker 안의 WebGL2** | `OffscreenCanvas.getContext("webgl2")`는 **Safari 17+**에서만 된다(16.4는 2D만). 기능 감지 후 ① Worker WebGL2 → ② 메인 스레드 WebGL2(가공 결과를 옮겨 처리) → ③ `ImageData` CPU 순으로 폴백한다 |
| 허용 오차 | 골든 이미지 비교에서 **평균 절대 오차 ≤ 3/255, 최대 차이 ≤ 12/255**([10 §4](./10-testing-and-acceptance.md)) |
| 파라미터 의도 | 픽셀 동일성이 불가하면 **"과하지 않은 소프트닝 60% 블렌드 + 미세 밝기 상승"** 의도를 유지한다(`analysis/14 §6` 주석) |

### 6.3 설정 토글의 의미

설정의 흑백/밝게/뷰티 토글은 **노출 여부만** 제어한다. 실제 적용은 결과 화면에서 사용자가 고른다. "원본"은 항상 목록에 있고 끌 수 없다.

---

## 7. 타임랩스 (WD2) — 웹의 가장 큰 구조 변경

### 7.1 무엇이 바뀌는가

| | Windows | 웹 |
|---|---------|-----|
| 1단계 | 세션 전체를 30fps로 `session.mp4` 녹화 | **없음** — 가공 프레임을 **OPFS에 스풀**(≤15fps JPEG, 인코딩 없음) |
| 2단계 | `computeSpeedFactor(sessionSeconds)`로 배속 산출 | **동일 함수를 종료 시점에 "실제" 경과로 적용**(예상값 아님 — §7.2) |
| 3단계 | ffmpeg `setpts`로 재인코딩 | **종료 시 스풀에서 균등 선별 → 30fps 타임라인 mp4 인코딩** |
| 산출물 | `timelapse.mp4`(H.264 무음) | **동일**(`timelapse.mp4`, H.264 무음) |

**업로드 계약은 완전히 동일하다**: `results/{sessionId}/timelapse.mp4`, `contentType: video/mp4`, `ext: mp4`. 서버는 이 파일이 어떻게 만들어졌는지 알 필요가 없다.

### 7.2 스풀(촬영 중) + 선별·인코딩(종료 시) — 실경과 기반

**촬영 중에는 인코딩하지 않는다.** 가공 프레임을 OPFS에 스풀만 하고, 촬영 종료 시([다음] 처리 1단계 "타임랩스 생성") **실제 세션 길이**로 선별·인코딩한다.

> **왜 고정 stride 실시간 인코딩이 아닌가**: stride를 예상 길이로 고정하면 손님이 [바로 촬영]을 매 컷 써서 세션이 예상의 1/5 이하로 줄었을 때 수집이 30프레임 하한에 미달해 **타임랩스가 통째로 사라진다.** Windows는 실제 녹화 길이로 배속을 계산하므로(≤15초면 N=1 원속) 같은 상황에서 정상 산출한다 — 스풀 방식이 그 동등성을 복원한다.

```
수집(촬영 중 — 가공 Worker):
  spoolInterval = 1/15 초                       # 수집 상한 15fps. JPEG(quality 0.8)로
  가공 프레임을 spoolInterval마다 OPFS sessions/{id}/tl/{n}.jpg 에 기록(opfsWriter 경유)
  스풀 상한 900장 — 도달 시 홀수 항 파일 삭제(절반 솎아내기) + spoolInterval 2배(반복 가능)

선별·인코딩(종료 시 — Result [다음] 1단계, Worker):
  actualSec    = 실제 촬영 경과(performance.now 델타)
  N            = computeSpeedFactor(actualSec)   # Windows FfmpegArgs와 동일 함수(도메인 이식)
  outputSec    = actualSec / N                   # ≤15초 세션이면 N=1 → 원속(Windows 동일)
  targetFrames = round(outputSec × 30)
  frames       = 스풀에서 균등 선별 min(targetFrames, 스풀 수)
  타임스탬프   = 출력 길이에 균등 배치(프레임 duration = outputSec / frames.length)
                 → 스풀이 부족하면 duration이 길어질 뿐 길이는 유지(실효 소스 fps ≤ 15)
  frames.length < 30 → null (1초 미만 영상 무의미)
```

| 규칙 | 내용 |
|------|------|
| 스풀 매체 | **OPFS**(메모리 아님 — WR8. 900장 × ~50KB ≈ 45MB, 세션 폴더와 함께 정리된다) |
| 출력 타임라인 | 컨테이너 30fps 타임라인. 소스 프레임이 부족하면 프레임 duration 연장(**실효 fps ≤ 15** — [12 §C2](./12-web-vs-windows-differences.md) 등재) |
| 세션 길이 측정 | `performance.now()` 실경과(§4.4 시퀀스 시작~마지막 컷). 진단·로그에 기록 |
| 배속 함수 재사용 | `domain/capture/timelapseSpeed.ts`(Windows `FfmpegArgs.ComputeSpeedFactor` 이식)를 **종료 시점에 실경과로** 적용 — 두 클라이언트의 길이 정책 드리프트 방지 |
| 인코딩 시점 | `Result` 진입 후 [다음] 1단계(기존 순서 그대로 — `analysis/13 §4.7`) |

### 7.3 인코더 경로 판정 (시작 시 1회, 결과를 진단에 표시)

```
경로 B: WebCodecs + JS MP4 muxer                      ← 1순위
   조건: "VideoEncoder" in self && (await VideoEncoder.isConfigSupported({codec:"avc1.42001E", ...})).supported
   방법: 스풀 JPEG → createImageBitmap → VideoFrame(균등 타임스탬프·duration) → encode → muxer → Blob(video/mp4)
   장점: 타임스탬프·품질 제어 정확. **Worker 안에서 완결**(VideoEncoder는 Worker에 노출된다)
경로 A: MediaRecorder(mp4)                            ← 2순위(예비)
   조건: MediaRecorder.isTypeSupported("video/mp4;codecs=avc1")
   방법: 메인 스레드의 HTMLCanvasElement.captureStream(0) + track.requestFrame() 을 stride마다 호출
   제약: ⚠️ MediaRecorder·captureStream은 **Worker에 없다**(§7.3a)
   장점: muxing 불필요
경로 C: 미지원
   → 타임랩스 미제공(timelapseUrl = null). 계약상 합법(analysis/14 §7.3)
```

| 판정 순서 | **B → A → C**(권장) |
|-----------|---------------------|
| 이유 ① 정확도 | 경로 B가 **타임스탬프를 직접 지정**하므로 30fps 정확도와 목표 길이를 보장한다. A는 `requestFrame` 타이밍과 레코더 내부 프레임레이트 추정에 의존해 길이가 흔들린다 |
| 이유 ② 구조 | B는 **Worker 안에서 완결**된다(스풀 읽기 → 인코딩 → muxing). A는 스풀 프레임을 메인 스레드 canvas에 재생하며 녹화해야 하므로(§7.3a) 스레딩 규약([04 §10](#10-스레딩리소스-규약-analysis14-9))의 예외가 된다 |
| 이유 ③ 지원 범위 | [10 §6.1](./10-testing-and-acceptance.md)의 **최소 버전 전부에서 B가 가능**하다(§7.3b). A가 유일한 대안이 되는 구간(WebCodecs 없고 MediaRecorder mp4 있는 Safari 14.1~16.3)은 지원 매트릭스 밖이다 → **A는 사실상 예비 경로**이며, 판정 순서를 B 우선으로 두면 실기기에서 A 경로가 선택되는 일은 거의 없다 |

#### 7.3a 경로 A의 구조적 제약 (구현 전 반드시 확인)

| 사실 | 결과 |
|------|------|
| `MediaRecorder`는 **Window 전용 인터페이스**다(Worker 전역에 없다) | 경로 A는 **메인 스레드에서만** 돌 수 있다 |
| `captureStream()`은 **`HTMLCanvasElement`의 메서드**다. **`OffscreenCanvas`에는 없다** | 가공 Worker의 OffscreenCanvas를 바로 녹화할 수 없다. 경로 A를 쓰려면 가공 결과를 **메인 스레드의 `<canvas>`에 그린 뒤** 그 캔버스를 `captureStream`한다 |
| 프리뷰를 `transferControlToOffscreen`으로 Worker에 넘겼다면 | 그 캔버스는 **메인에서 그릴 수 없다**(제어권이 이미 이전됨) → 경로 A용 캔버스를 **별도로** 두어야 한다 |

→ 경로 A를 구현할 때는 **종료 시** 스풀 프레임을 메인 스레드 전용(화면 밖) 캔버스에 순차로 그리며 프레임마다 `requestFrame()`을 호출한다(재생-녹화 방식). **경로 A는 [04 §10](#10-스레딩리소스-규약-analysis14-9) 표의 "타임랩스 인코딩 = Worker" 규약의 예외다.**

#### 7.3b 브라우저 지원 현실 (2026-07 기준 — 과신 금지)

| 기능 | Chrome/Edge | Safari(macOS·iOS·iPadOS) | Firefox |
|------|-------------|--------------------------|---------|
| WebCodecs `VideoEncoder`(경로 B) | 94+ ○ | **16.4+ ○** — WebCodecs의 **video 인터페이스**(`VideoEncoder`/`VideoDecoder`/`VideoFrame`)가 16.4에 도입됐다(audio 인터페이스는 한참 뒤). H.264 인코딩은 VideoToolbox 기반 | 133+ △ — **플랫폼 H.264 인코더 유무에 의존**. `isConfigSupported`로만 판정한다 |
| `avc1` 인코딩 실제 가용성 | ○(하드웨어 또는 SW 폴백) | ○ | △ |
| MediaRecorder `video/mp4;codecs=avc1`(경로 A) | 130+ ○(그 이전은 webm만) | **14.1+ / iOS 14.5+ ○**(Safari는 오래전부터 mp4가 기본 컨테이너) | **✕**(`isTypeSupported` false) |
| 결론 | B 사용 | B 사용(16.4+) | B가 되면 사용, 아니면 **경로 C** |

| 규칙 | 내용 |
|------|------|
| **버전 문자열로 판정하지 않는다** | 반드시 `VideoEncoder.isConfigSupported()` / `MediaRecorder.isTypeSupported()` **런타임 기능 감지**로만 판정한다. 위 표는 기기 선정·예상용이다 |
| `isConfigSupported`는 **비동기** | `await` 하고 `.supported === true`를 확인한다. 지원 여부와 별개로 `config`(해상도·비트레이트)에 따라 거절될 수 있으므로 **실제 사용할 config로 질의**한다 |
| Firefox가 C로 떨어질 수 있다 | 지원 등급 C(검증만)와 일치한다. 타임랩스 미제공은 계약상 합법이다 |

#### 7.3c 경로 B 인코더 설정·출력 규격

| 항목 | 규격 |
|------|------|
| **짝수 해상도 강제** | 인코딩 입력 크기 = `trunc(w/2)*2 × trunc(h/2)*2`(홀수 변은 1px 잘라낸다). **yuv420p(4:2:0)는 양변이 짝수여야 인코더가 열린다** — Windows에서 실제 발생한 결함이다(`FfmpegArgs.EvenDimensionCrop`, 커밋 fb3e99d: 1443×1080 크롭에서 libx264가 개시 거부 → 타임랩스 전체 실패. 프레임 종횡비에 따라 중앙 크롭 결과가 임의의 홀수가 된다). WebCodecs `VideoEncoder`도 H.264+4:2:0에서 같은 제약이 있으므로 **config의 width/height와 인코딩 직전 프레임 크기를 짝수로 클램프**한다. 스풀·프리뷰·스틸은 가공 해상도 그대로 둔다(Windows와 동일 — 출력 인코딩 단계에서만 처리해 WYSIWYG 무영향) |
| 코덱 문자열 | `avc1.42001E`(Baseline L3.0) 우선, 실패 시 `avc1.42E01E` → `avc1.4D001E`(Main) 순으로 시도 |
| 인코더 설정 | `{ codec, width, height, framerate: 30, bitrate: 하단 표, latencyMode: "quality", avc: { format: "avc" } }` |
| muxer | 순수 JS MP4 muxer(버전 핀 고정). **`moov` atom이 완성된 정상 종료를 보장**해야 한다(`analysis/14 §7.1`) |
| 오디오 | **없음**(트랙을 만들지 않는다) |
| 픽셀 포맷 | 인코더가 내부적으로 yuv420p를 쓴다(브라우저 기본). 명시 옵션은 없다 |

### 7.4 비트레이트 (CRF 20 상당 근사)

WebCodecs·MediaRecorder에는 CRF가 없으므로 해상도별 비트레이트로 근사한다.

| 가공 해상도(예) | 비트레이트 |
|-----------------|-----------|
| ≤ 640×854 | 2.5 Mbps |
| ~810×1080 (1080p의 3:4 크롭) | **5 Mbps** |
| ~1080×1440 | 8 Mbps |
| 그 이상 | `width × height × 30 × 0.12` bit/s로 산출 후 12 Mbps 상한 |

### 7.5 실패·정지 정책 (`analysis/14 §7.1`·§7.3)

| 상황 | 동작 |
|------|------|
| 인코더 초기화 실패 | 타임랩스만 건너뛰고 **촬영은 계속**(경고 로그) |
| 인코딩 중 오류 | 수집 중단 + `null` 반환. 예외를 상위로 전파하지 않는다 |
| 백프레셔 | `encoder.encodeQueueSize`가 임계(예: 8)를 넘으면 **프레임 드롭**(프리뷰 우선) |
| 정지 실패 | 타임아웃 후 강제 종료. 예외 전파 금지 |
| 화면 이탈 | **시퀀스 취소 → 인코더 정지 → 카메라 정지** 순서 준수 |

---

## 8. 성능·메모리 예산

측정은 실기기에서 한다([10 §6](./10-testing-and-acceptance.md)). 초과 시 그 기기는 지원 목록에서 제외하거나 해상도를 낮춘다.

| 항목 | 예산 | 초과 시 대응 |
|------|------|--------------|
| 프리뷰 지연(획득→화면) | **≤ 120ms** | Worker `transferControlToOffscreen` 경로로 전환 |
| 프리뷰 프레임레이트 | **≥ 24fps** | 가공 해상도 하향(1080p → 720p) |
| 스틸 캡처 소요 | ≤ 150ms | JPEG 품질·해상도 조정 |
| 합성(4슬롯, 1200×1600 출력) | **≤ 1.2s** | 필터를 WebGL2 경로로, 축소를 `createImageBitmap`으로 |
| 필터 변경 재합성 | ≤ 1.2s | 동상 |
| 타임랩스 인코딩(375프레임) | **≤ 6s** | 비트레이트·해상도 하향, stride 증가 |
| 세션 최대 메모리(10컷) | **≤ 250MB** | 컷을 즉시 OPFS로 내리고 `ImageBitmap`은 썸네일만 유지 |

### 8.1 메모리 규칙 (위반하면 iOS에서 탭이 죽는다)

| 규칙 | 내용 |
|------|------|
| `VideoFrame`·`ImageBitmap`은 **반드시 `close()`** | GC 대상이 아니다. `finally`에서 닫는다 |
| 컷 원본은 메모리에 쌓지 않는다 | 즉시 JPEG로 OPFS 기록 → 썸네일만 `ImageBitmap`(장변 480px) |
| 합성은 순차 | 슬롯별로 `createImageBitmap` → 그리기 → `close()` |
| canvas 재사용 | 프레임 가공 캔버스는 1개, 합성 캔버스는 1개. 크기 변할 때만 재할당 |
| `blob:` URL | 미리보기 URL은 교체 시 **이전 것을 `revokeObjectURL`** |
| 화면 이탈 | 화면의 모든 비트맵·URL을 해제한다(`onLeave`) |

---

## 9. 정수 연산 대응표 (픽셀 파손 방지 — 필수)

`analysis/14`의 의사코드는 **C#의 정수 나눗셈**을 전제한다. JS `/`는 실수 나눗셈이므로 **아래 표대로 명시 변환**해야 Windows와 픽셀이 일치한다.

> ⚠️ **반올림도 다르다 — `Math.round`를 그대로 쓰면 안 된다.** 규격의 `round(...)`는 C# `Math.Round(double)`이고 기본이 **은행가 반올림(half-to-even)** 이다(`Math.Round(66.5)=66`, `Math.Round(-1.5)=-2`). JS `Math.round`는 half-up(+∞ 방향)이라 `Math.round(66.5)=67`, `Math.round(-1.5)=-1`로 어긋난다. 중간값(.5)은 실제로 발생한다 — `scaleSlots`의 `cx - newW/2`, 3:4 크롭의 `srcH×0.75` 등. **`domain/mathCompat.ts`에 `roundHalfToEven(x)`를 두고 아래 표의 모든 반올림 셀에 그것만 쓴다.** 슬롯 위치 "0px 오차" 계약([10 §4.2](./10-testing-and-acceptance.md))이 이 함수에 걸려 있다.

| 규격 위치 | 규격 식 | JS 구현 |
|-----------|---------|---------|
| `14 §3` centerCrop | `cropW = round(srcH * targetAspect)` | `roundHalfToEven(srcH * targetAspect)` |
| `14 §3` | `x = (srcW - cropW) / 2` (정수 나눗셈) | **`Math.floor((srcW - cropW) / 2)`** |
| `14 §4.1` autoArrange | `marginX = max(20, frameW / 20)` (정수) | **`Math.max(20, Math.floor(frameW / 20))`** |
| `14 §4.1` | `gapX = max(12, frameW / 40)` | **`Math.max(12, Math.floor(frameW / 40))`** |
| `14 §4.1` | `cellW = (frameW - marginX*2 - gapX*(cols-1)) / cols` | **`Math.floor(...)`** |
| `14 §4.1` | `r = i / cols` (정수 나눗셈) | **`Math.floor(i / cols)`** |
| `14 §4.1` fitInCell | `w = round(h * targetAspect)` | `roundHalfToEven(...)` |
| `14 §4.1` fitInCell | `offX = (cellW - w) / 2` (정수) | **`Math.floor(...)`** |
| `14 §4.2` scaleSlots | `newW = max(1, round(s.width * factor))` | `Math.max(1, roundHalfToEven(...))` |
| `14 §4.2` | `cx = s.x + s.width / 2.0` (**부동소수**) | `s.x + s.width / 2` — **floor 금지** |
| `14 §4.2` | `newX = round(cx - newW / 2.0)` | `roundHalfToEven(cx - newW / 2)` — **.5가 흔한 지점**(cx·newW/2 각각 .5 가능) |
| `14 §4.5` compute | `scale = min(canvasW/frameW, canvasH/frameH)` (**부동소수**) | 그대로 |
| `14 §4.7` fallback 프레임 | `cellW = (1200 - 80*2 - 60) / 2` | `Math.floor(...)` = 490 |
| `14 §4.7` | `top = (1600 - (cellH*2 + gap)) / 2` | `Math.floor(...)` |

> **검증 방법**: 위 함수들의 입력→출력 벡터를 `docs/spec-vectors/*.json`에 추출해 **Windows 테스트와 웹 테스트가 같은 파일을 읽게** 한다([10 §3](./10-testing-and-acceptance.md)). 손으로 대조하지 말 것.

---

## 10. 스레딩·리소스 규약 (`analysis/14 §9`)

| 대상 | 웹 모델 | 해제 |
|------|---------|------|
| 프레임 획득 | 메인 스레드의 `rVFC` 콜백(가벼운 전달만) | 루프 취소 플래그 |
| 프레임 가공·분기 | **Worker 1개**(`frameProcessor.worker.ts`) | 정지 메시지 후 `terminate()`(타임아웃 300ms) |
| 프리뷰 렌더 | Worker의 OffscreenCanvas(권장) 또는 메인 canvas | 화면 이탈 시 구독 해제 |
| 타임랩스 인코딩 | **Worker**(가공 Worker와 동일 또는 별도) — **경로 B에 한정**. 경로 A(`MediaRecorder`)는 Window 전용이라 **메인 스레드**에서 돌고 전용 `<canvas>`가 필요하다(§7.3a) | `flush()` → `close()`, 실패 시 강제 종료. 경로 A는 `recorder.stop()` + `dataavailable` 대기 |
| 합성·필터 | **Worker**(UI 블로킹 금지) | 중간 버퍼 즉시 해제 |
| 런타임 설정(거울·종횡비·stride) | Worker에 메시지로 전달 | — |
| 스틸/인코딩 상태 전이 | Worker 내부 단일 스레드라 락 불필요 | — |

**화면 이탈 순서(고정)**: ① 촬영 시퀀스·카운트다운 취소 → ② 인코더 정지(예외 무시) → ③ 카메라 정지.

---

## 11. 이식 체크리스트

`analysis/14 §10`의 항목 + 웹 전용 항목.

- [ ] 프레임 가공을 **메인 스레드가 아닌 Worker**에서 한다
- [ ] 거울 반전·중앙 크롭이 **프레임당 1회**, 프리뷰·스틸·타임랩스가 그 결과를 공유한다
- [ ] **CSS로 반전하지 않는다**(WM1)
- [ ] 스틸 캡처가 버퍼를 **복제**한다(다음 프레임에 덮이지 않는다)
- [ ] Ready 게이트 3조건 + 8초 타임아웃이 있다
- [ ] `centerCrop`이 §9 표대로 **`Math.floor`/`roundHalfToEven`** 을 쓴다(JS `Math.round` 직접 사용 금지 — 은행가 반올림)
- [ ] `autoArrange`·`fitInCell`의 정수 나눗셈이 §9 표와 일치한다
- [ ] 슬롯 스케일이 **원본 기준**으로 계산된다
- [ ] 편집기 표시·드래그·클램프가 **하나의 좌표 변환**을 공유한다
- [ ] 합성이 슬롯 `index` 오름차순이고 컷/슬롯 개수 불일치를 오류로 처리한다
- [ ] 축소 보간이 **축소에 강한 방식**이다(`resizeQuality:"high"` 또는 단계 축소)
- [ ] 흑백이 **BT.601 계수를 직접 계산**한다(CSS filter 미사용)
- [ ] 밝게가 `alpha=1.1, beta=20`이다
- [ ] 뷰티가 파라미터 의도를 유지하고 허용 오차 내다
- [ ] 타임랩스가 **H.264 / mp4 / 무음 / 30fps**이고 목표 길이 10~15초다
- [ ] 인코더 경로를 **런타임 기능 감지**(`isConfigSupported`/`isTypeSupported`)로만 판정한다(버전 문자열·UA 판정 금지)
- [ ] 인코더 부재·실패가 예외가 아니라 `null`/스킵으로 처리된다
- [ ] `createImageBitmap` resize 옵션의 **실효 여부를 결과 크기로 검증**하고 폴백을 둔다(§5.2)
- [x] `OffscreenCanvas` 2D 부재 경로에 폴백이 있다(§2.3.1·§2.3.2) — `mainThreadProcessor.ts`
- [x] `transferControlToOffscreen` 부재·실패 경로에 폴백이 있다(§2.3.2) — 비트맵 채널
- [x] 제약 요청이 **사다리**이고 `frameRate.min`이 없다(§2.1 · CAM-2)
- [x] `<video>` 숨김이 `display:none`이 아니다(§2.2 · CAM-3)
- [ ] WebGL2 부재 경로 폴백(§6.2) — 합성은 현재 `ImageData` CPU 경로만 쓴다(WebGL2 미사용)
- [ ] 서버 프레임 이미지를 **CORS-clean**하게 로드한다(WM2)
- [ ] 모든 `VideoFrame`/`ImageBitmap`이 `close()`된다
- [ ] 화면 이탈 시 시퀀스 취소 → 인코더 정지 → 카메라 정지 순서를 지킨다
- [ ] 탭 hidden에서 촬영 시퀀스가 취소된다(WM4)
