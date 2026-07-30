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
   프리뷰 렌더           스틸 캡처 요청           타임랩스 샘플러      fps 계산
 (canvas transferToImage) (구조 복제 → JPEG)     (간격마다 encode)
```

**규격(`analysis/14 §2.2`)을 그대로 지킨다**: 가공은 프레임당 1회, 세 소비자가 결과를 공유한다. 소비자별로 따로 가공하면 성능 손실 + 프리뷰와 저장물 불일치가 생긴다(WM1).

### 1.1 Windows와의 구조 차이 (요약)

| 항목 | Windows | 웹 |
|------|---------|-----|
| 프레임 획득 | OpenCV `VideoCapture`(DirectShow) 전용 스레드 | `getUserMedia` + `requestVideoFrameCallback` |
| 가공 위치 | 전용 백그라운드 스레드 | **Worker + OffscreenCanvas**(메인 스레드 아님) |
| 세션 녹화 | ffmpeg에 rawvideo 파이프(`session.mp4`) | **하지 않는다** — 타임랩스를 직접 인코딩(WD2) |
| 타임랩스 | `session.mp4` → `setpts` 배속 재인코딩 | **촬영 중 프레임 샘플링 → 30fps mp4 직접 인코딩** |
| 합성·필터 | OpenCV | Canvas 2D + WebGL2 |
| 산출물 | `session.mp4` + `timelapse.mp4` + `final.{ext}` | `timelapse.mp4` + `final.{ext}` (`session.mp4` 없음) |

---

## 2. 카메라 획득

### 2.1 제약 요청

```ts
const constraints: MediaStreamConstraints = {
  audio: false,                                  // 오디오는 전혀 쓰지 않는다(무음 규격)
  video: {
    width:  { ideal: 1920 },
    height: { ideal: 1080 },
    frameRate: { ideal: 30, min: 15 },
    ...(deviceId ? { deviceId: { exact: deviceId } } : { facingMode: { ideal: facing } }),
  },
};
```

| 규칙 | 내용 |
|------|------|
| `audio: false` 고정 | 타임랩스는 **무음**이 규격이다. 오디오 트랙을 얻으면 권한 요청 범위만 넓어진다 |
| `deviceId: exact` 실패 시 | `OverconstrainedError` → **제약 없이 재시도**(첫 장치) → 그래도 실패면 `Failed` |
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

### 2.3 OS·브라우저별 주의

| 플랫폼 | 주의 |
|--------|------|
| **iOS / iPadOS** | Safari(및 홈화면 PWA)에서 동작. `playsinline` 필수. 백그라운드 전환 시 트랙이 `muted`/중단될 수 있어 복귀 시 **스트림 상태 재확인**. 카메라 권한은 사이트 설정에서 "허용"으로 고정 권장 |
| **Android Chrome** | 정상. 후면 카메라가 여러 개인 기기가 많다 → 장치 목록에서 사용자가 고를 수 있어야 한다 |
| **Windows / macOS** | 정상. 외장 웹캠 다수 환경에서 `deviceId` 저장·매칭 폴백이 중요(WC3) |
| **모든 플랫폼** | **HTTPS(또는 localhost) 필수** — 보안 컨텍스트가 아니면 `mediaDevices`가 `undefined`다 |
| 권한 상태 | `navigator.permissions.query({name:"camera"})`는 지원이 고르지 않다 → **지원 시에만** 진단 표시에 사용하고, 로직은 `getUserMedia` 결과로 판정 |

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
| 축소 보간 | `createImageBitmap(src, {resizeWidth, resizeHeight, resizeQuality: "high"})` — 축소에 강하다. **미지원·품질 미달 시 2단 폴백**: 절반씩 반복 축소(mipmap 방식) 후 `drawImage` |
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
| 허용 오차 | 골든 이미지 비교에서 **평균 절대 오차 ≤ 3/255, 최대 차이 ≤ 12/255**([10 §4](./10-testing-and-acceptance.md)) |
| 파라미터 의도 | 픽셀 동일성이 불가하면 **"과하지 않은 소프트닝 60% 블렌드 + 미세 밝기 상승"** 의도를 유지한다(`analysis/14 §6` 주석) |

### 6.3 설정 토글의 의미

설정의 흑백/밝게/뷰티 토글은 **노출 여부만** 제어한다. 실제 적용은 결과 화면에서 사용자가 고른다. "원본"은 항상 목록에 있고 끌 수 없다.

---

## 7. 타임랩스 (WD2) — 웹의 가장 큰 구조 변경

### 7.1 무엇이 바뀌는가

| | Windows | 웹 |
|---|---------|-----|
| 1단계 | 세션 전체를 30fps로 `session.mp4` 녹화 | **없음** |
| 2단계 | `computeSpeedFactor(sessionSeconds)`로 배속 산출 | **동일 함수로 목표 길이 산출** |
| 3단계 | ffmpeg `setpts`로 재인코딩 | **촬영 중 샘플링한 프레임을 30fps로 직접 인코딩** |
| 산출물 | `timelapse.mp4`(H.264 무음) | **동일**(`timelapse.mp4`, H.264 무음) |

**업로드 계약은 완전히 동일하다**: `results/{sessionId}/timelapse.mp4`, `contentType: video/mp4`, `ext: mp4`. 서버는 이 파일이 어떻게 만들어졌는지 알 필요가 없다.

### 7.2 목표 길이와 샘플링 간격

목표는 `analysis/14 §7.2`와 같다: **결과 길이 10~15초(중앙값 12.5초)**.

```
목표 출력 프레임 수 TARGET_FRAMES = 12.5초 × 30fps = 375
예상 세션 길이(진입 시 계산):
  expectedSec ≈ N × (countdownSec + 0.12 + 0.30)      # N = 실제 촬영 컷 수
샘플링 간격(가공 프레임 기준):
  stride = max(1, round(expectedSec × 30 / TARGET_FRAMES))
```

| 규칙 | 내용 |
|------|------|
| 적응 보정 | 실제 세션이 예상보다 길어지면(사용자가 [바로 촬영]을 안 쓰거나 지연 발생) 수집 프레임이 375를 넘는다 → **375에 도달하면 stride를 2배로 늘리고 이미 넣은 프레임을 2개마다 1개 버리는 방식으로 균등 다운샘플**(인코딩 전 큐 단계에서 수행) |
| 하한 | 수집 프레임이 **30장(=1초) 미만**이면 타임랩스를 만들지 않고 `null`(너무 짧은 영상은 무의미) |
| 출력 fps | **30fps 고정**(타임스탬프 `i × 1/30` 초) |
| 세션 길이 측정 | `performance.now()` 기준 실경과. 진단·로그에 기록 |
| 배속 함수 재사용 | `domain/capture/timelapseSpeed.ts`(Windows `FfmpegArgs.ComputeSpeedFactor` 이식)를 **stride 산출과 검증에 사용**해 두 클라이언트의 목표 길이 정책이 드리프트하지 않게 한다 |

### 7.3 인코더 경로 판정 (시작 시 1회, 결과를 진단에 표시)

```
경로 A: MediaRecorder(mp4)
   조건: MediaRecorder.isTypeSupported("video/mp4;codecs=avc1")
   방법: 가공 canvas.captureStream(0) + track.requestFrame() 을 stride마다 호출
   장점: 구현 단순, muxing 불필요
경로 B: WebCodecs + JS MP4 muxer
   조건: "VideoEncoder" in window && await VideoEncoder.isConfigSupported({codec:"avc1.42001E", ...}).supported
   방법: VideoFrame(가공 결과, timestamp=i*33333μs) → encode → muxer → Blob(video/mp4)
   장점: 타임스탬프·품질 제어 정확
경로 C: 미지원
   → 타임랩스 미제공(timelapseUrl = null). 계약상 합법(analysis/14 §7.3)
```

| 판정 순서 | **B → A → C**(권장) |
|-----------|---------------------|
| 이유 | 경로 B가 **타임스탬프를 직접 지정**하므로 30fps 정확도와 목표 길이를 보장한다. A는 `requestFrame` 타이밍에 의존해 길이가 흔들릴 수 있다 |
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

| 규격 위치 | 규격 식 | JS 구현 |
|-----------|---------|---------|
| `14 §3` centerCrop | `cropW = round(srcH * targetAspect)` | `Math.round(srcH * targetAspect)` |
| `14 §3` | `x = (srcW - cropW) / 2` (정수 나눗셈) | **`Math.floor((srcW - cropW) / 2)`** |
| `14 §4.1` autoArrange | `marginX = max(20, frameW / 20)` (정수) | **`Math.max(20, Math.floor(frameW / 20))`** |
| `14 §4.1` | `gapX = max(12, frameW / 40)` | **`Math.max(12, Math.floor(frameW / 40))`** |
| `14 §4.1` | `cellW = (frameW - marginX*2 - gapX*(cols-1)) / cols` | **`Math.floor(...)`** |
| `14 §4.1` | `r = i / cols` (정수 나눗셈) | **`Math.floor(i / cols)`** |
| `14 §4.1` fitInCell | `w = round(h * targetAspect)` | `Math.round(...)` |
| `14 §4.1` fitInCell | `offX = (cellW - w) / 2` (정수) | **`Math.floor(...)`** |
| `14 §4.2` scaleSlots | `newW = max(1, round(s.width * factor))` | `Math.max(1, Math.round(...))` |
| `14 §4.2` | `cx = s.x + s.width / 2.0` (**부동소수**) | `s.x + s.width / 2` — **floor 금지** |
| `14 §4.2` | `newX = round(cx - newW / 2.0)` | `Math.round(cx - newW / 2)` |
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
| 타임랩스 인코딩 | **Worker**(가공 Worker와 동일 또는 별도) | `flush()` → `close()`, 실패 시 강제 종료 |
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
- [ ] `centerCrop`이 §9 표대로 **`Math.floor`/`Math.round`** 를 쓴다
- [ ] `autoArrange`·`fitInCell`의 정수 나눗셈이 §9 표와 일치한다
- [ ] 슬롯 스케일이 **원본 기준**으로 계산된다
- [ ] 편집기 표시·드래그·클램프가 **하나의 좌표 변환**을 공유한다
- [ ] 합성이 슬롯 `index` 오름차순이고 컷/슬롯 개수 불일치를 오류로 처리한다
- [ ] 축소 보간이 **축소에 강한 방식**이다(`resizeQuality:"high"` 또는 단계 축소)
- [ ] 흑백이 **BT.601 계수를 직접 계산**한다(CSS filter 미사용)
- [ ] 밝게가 `alpha=1.1, beta=20`이다
- [ ] 뷰티가 파라미터 의도를 유지하고 허용 오차 내다
- [ ] 타임랩스가 **H.264 / mp4 / 무음 / 30fps**이고 목표 길이 10~15초다
- [ ] 인코더 부재·실패가 예외가 아니라 `null`/스킵으로 처리된다
- [ ] 서버 프레임 이미지를 **CORS-clean**하게 로드한다(WM2)
- [ ] 모든 `VideoFrame`/`ImageBitmap`이 `close()`된다
- [ ] 화면 이탈 시 시퀀스 취소 → 인코더 정지 → 카메라 정지 순서를 지킨다
- [ ] 탭 hidden에서 촬영 시퀀스가 취소된다(WM4)
