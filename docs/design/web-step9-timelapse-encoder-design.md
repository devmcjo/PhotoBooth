# 웹 클라이언트 Step 9 — 타임랩스 인코더 설계

| 항목 | 값 |
|------|-----|
| 대상 | `webclient/` (브랜치 `feature/web-client-foundation`) |
| WBS | [`docs/web-client/11-wbs.md`](../web-client/11-wbs.md) **Step 9: 타임랩스 인코더** |
| 규격 진실원 | [`docs/analysis/14-media-pipeline-spec.md §7`](../analysis/14-media-pipeline-spec.md) (플랫폼 중립) |
| 웹 구현 규격 | [`docs/web-client/04-media-pipeline-web.md §7`](../web-client/04-media-pipeline-web.md) |
| 관례 | [`docs/web-client/15-implementation-conventions.md`](../web-client/15-implementation-conventions.md) §2·§3·§6 |
| 작성일 | 2026-07-31 |
| 담당 파이프라인 | `js-architect`(본 문서) → `js-developer` → `js-code-reviewer` |

---

## 0. 한 문단 요약

촬영 중에는 **인코딩하지 않는다.** 가공 Worker가 ≤15fps로 JPEG를 뽑아 OPFS `sessions/{id}/tl/`에
스풀하고, 촬영이 끝나면 **실제 경과 시간**으로 `computeSpeedFactor`를 적용해 균등 선별한 뒤
`Result` 화면 [다음] 1단계에서 H.264/mp4(무음)로 인코딩한다. 경로는 런타임 감지로
**WebCodecs(Worker) → MediaRecorder(메인) → 미지원(`null`)** 순으로 고른다. 어떤 실패도 예외로
전파되지 않고 `null`이며, 촬영은 항상 완주한다.

---

## 1. 검증된 사실 (verified facts)

모두 이 저장소의 파일을 직접 열어 확인했다.

| # | 사실 | 근거 |
|---|------|------|
| F1 | 배속 함수는 이미 도메인에 있다. `computeSpeedFactor(sec)`, `expectedOutputSeconds(sec, n)`, `TARGET_MIN/MAX_SECONDS` | `webclient/src/domain/capture/timelapseSpeed.ts:8-27` |
| F2 | 스풀 API가 이미 있다. `writeTimelapseFrame(index, bytes)` / `listTimelapseFrames()` / `removeTimelapseFrame(name)`, 파일명은 `String(index).padStart(5,"0") + ".jpg"` → **문자열 정렬 = 시간 정렬** | `webclient/src/adapters/storage/sessionWorkspace.ts:18-62` |
| F3 | 가공 Worker에 "소비자 3: 타임랩스 샘플러는 Step 9가 이 통지에 붙는다" 주석이 실제로 있고, `processFrame`은 프리뷰·스틸 두 소비자만 처리한다 | `webclient/src/adapters/camera/frameProcessor.worker.ts:80-112` |
| F4 | **가공 Worker의 스틸 슬롯은 1개짜리 덮어쓰기다** — `pendingStill = {id, quality}`. 두 요청이 한 프레임 간격 안에 들어오면 **먼저 온 것이 사라지고 클라이언트가 5초 타임아웃 후 `null`** 을 돌려준다 | `frameProcessor.worker.ts:92-109,157-160` · `frameProcessorClient.ts:79-91`(`STILL_TIMEOUT_MS=5000`) |
| F5 | 컷 캡처 실패는 곧 세션 실패다. `captureStill()`이 `null`이면 컷이 빠지고, 컷 수 < 슬롯 수면 홈으로 복귀한다 | `captureSequence.ts:134-138` · `useCaptureRunner.ts:155-161` |
| F6 | 셸 훅에 `stopEncoder`가 **이미 예약**되어 있고 `returnHome`이 `cancelCaptureSequence → discardCaptureData → cleanupWorkspace → stopEncoder → stopCamera` 순으로 부른다 | `shellStore.ts:45-56,129-141` |
| F7 | `useCaptureRunner`에 "4. 타임랩스 프레임 수집은 Step 9가 여기에 붙는다" 자리표시가 있다 | `useCaptureRunner.ts:110-116` |
| F8 | OPFS **쓰기**는 전부 `opfsWriter` Worker를 지난다. **읽기(`getFile()`)는 Worker를 거치지 않는 것이 규칙**이다(`opfsClient.readFile`이 메인에서 직접 읽는다) | `opfsClient.ts:9-16,120-134` · `opfsWriter.worker.ts:5-11` |
| F9 | 기존 Worker(`frameProcessor`·`opfsWriter`)는 **logger를 쓰지 않는다.** `logger`는 `attachLogStore` 전이면 메인 프로세스 내 `earlyBuffer`에만 쌓이므로, Worker에서 부른 로그는 **영원히 진단에 나타나지 않는다** | `logStore.ts:245-257` · 두 worker 파일에 `logStore` import 0건 |
| F10 | 어댑터 DI 패턴의 선례가 있다 — `createCameraService({openStream, createFrameSource, createProcessor, now, readyTimeoutMs})` 로 node에서 하드웨어 없이 전부 검증된다 | `cameraService.ts:36-42` · `tests/unit/camera/cameraService.test.ts` |
| F11 | 순수성 테스트는 `src/domain/**/*.ts`를 **glob으로 자동 포함**하며 `performance.`·`Date.now`·`console.`·`window`·도메인 밖 import를 정규식으로 막는다 | `tests/unit/domain/purity.test.ts:15-96` |
| F12 | 반올림 규약: 규격의 `round(...)`는 **은행가 반올림**이며 `roundHalfToEven`을 쓴다. `Math.round` 직접 사용 금지 | `src/domain/mathCompat.ts:12-24` · `04 §9` |
| F13 | Windows의 짝수 클램프는 ffmpeg `crop=trunc(iw/2)*2:trunc(ih/2)*2`다. ffmpeg `crop`의 기본 원점은 `(in_w-out_w)/2`이고 1px 차에서는 정수 나눗셈으로 **0** → 실질 **좌상단 기준, 우/하 1px 절단** | `src/MCPhoto.Core/Capture/FfmpegArgs.cs:30`(주석 포함) |
| F14 | `mp4-muxer@5.2.2` = **MIT**, unpackedSize 155,878 B, ESM(`build/mp4-muxer.mjs`) + `.d.ts` 포함, 런타임 의존은 `@types/*` 2개뿐 | `npm view mp4-muxer version license dist.unpackedSize exports` 출력 |
| F15 | `mp4-muxer`는 **deprecated**다(`"This library is superseded by Mediabunny."`). 후속 `mediabunny@1.52.1`은 **MPL-2.0**이고 unpackedSize 9,954,165 B | `npm view mp4-muxer deprecated` · `npm view mediabunny license dist.unpackedSize` |
| F16 | `mp4-muxer` v5 API: `new Muxer({target: new ArrayBufferTarget(), video:{codec:'avc',width,height,frameRate?}, fastStart:'in-memory'|false|'fragmented'|{...}})`, `addVideoChunk(chunk, meta?, timestamp?, ...)`, `finalize()`, `target.buffer` | `https://unpkg.com/mp4-muxer@5.2.2/build/mp4-muxer.d.ts` |
| F17 | **`VideoOptions.frameRate`를 주면 "timestamps will be rounded according to this value"** | 같은 `.d.ts`, `VideoOptions.frameRate` JSDoc |
| F18 | 현재 baseline: `npx vitest run` = 19 test files / **530 tests**. `webclient/THIRD-PARTY.md`는 **존재하지 않는다** | 오케스트레이터 실측 · `ls webclient/THIRD-PARTY.md` → No such file |
| F19 | 진단 로그 항목 규격에 "타임랩스: 선택된 인코더 경로·stride·수집 프레임 수·출력 길이·바이트·실패 사유"가 이미 정의돼 있다 | `docs/web-client/05-storage-and-persistence.md:404` |
| F20 | `tsconfig.json`의 `include`에 `tests`가 있으므로 **테스트 파일도 `tsc --noEmit` 대상**이다. `FrameProcessor` 인터페이스를 넓히면 `tests/unit/camera/cameraService.test.ts`의 `FakeProcessor`가 컴파일 에러가 난다 | `webclient/tsconfig.json:"include"` · `cameraService.test.ts:98-131` |
| F21 | 문구 카탈로그(`src/ui/strings.ts`)에는 `result` 섹션이 없고, `STRINGS`를 검사하는 테스트도 없다 | `grep -rn "STRINGS" tests/` → 0건 |

---

## 2. 미검증 가정 (open assumptions)

| # | 가정 | 검증 |
|---|------|------|
| A1 | `mp4-muxer@5.2.2`가 Vite Worker 번들(`{type:"module"}`)에서 정상 번들된다 | **Step 9-5** (`npx vite build` 통과 + 산출물에 muxer 코드 포함) |
| A2 | `VideoEncoder`가 만든 `EncodedVideoChunk` + `meta.decoderConfig.description`(avcC)을 `addVideoChunk`가 그대로 받아 재생 가능한 mp4를 만든다 | **자동화 불가** — 브라우저 실행 필요. Step 9-9의 **사용자 액션 V18**로 남긴다 |
| A3 | Safari 17(iOS)에서 `VideoEncoder.isConfigSupported({codec:"avc1.42001E", ...})`가 `supported:true`를 준다 | **자동화 불가** — 실기기. V18 |
| A4 | 하드웨어 H.264 인코더가 375프레임을 [04 §8]의 **≤6초** 예산 안에 처리한다 | **자동화 불가** — 실기기 계측. V18 |
| A5 | 가공 Worker에서 15fps JPEG(q0.8) 인코딩을 추가해도 프리뷰가 **≥24fps**를 유지한다 | **자동화 불가** — 실기기. V18 |
| A6 | 경로 A(`MediaRecorder` mp4)에서 `canvas.captureStream(0)` + `requestFrame()`이 DOM에 붙지 않은 캔버스에서도 동작한다 | **자동화 불가**. 경로 A는 지원 매트릭스상 도달하지 않는 예비 경로다([04 §7.3] 이유 ③) — V18에서 "미도달"로 기록해도 합격 |
| A7 | OPFS 쓰기(`opfsWriter`)가 15 write/s를 따라간다 | 따라가지 못하면 **드롭 카운터**로 관측된다(설계상 안전측). 실기기 V18에서 `droppedSpool` 값 확인 |

> **A2~A7은 브라우저 실행이 필요하다. 추정으로 통과 처리하지 않는다.**
> Step 9-9에서 `docs/web-client/14-handoff-and-user-actions.md`에 **V18**로 등재하고,
> WBS Step 9 체크박스에 "미검증(사용자 액션 V18)"으로 남긴다.

---

## 3. 아키텍처 개관

```
[촬영 중 — 수집]
  frameProcessor.worker            메인 스레드                    opfsWriter.worker
  ─────────────────────            ──────────────                 ─────────────────
  가공 프레임(공유 캔버스)
    ├ 프리뷰
    ├ 스틸(pendingStill)
    └ 스풀: shouldSpoolFrame(도메인)
        → convertToBlob(jpeg 0.8)
        → postMessage(spoolFrame) ──▶ timelapseService.onSpoolFrame
                                        · index 채번(0 패딩)
                                        · 인플라이트 1개 제한(초과=드롭)
                                        · 900장 도달 → planDecimation(도메인)
                                          → removeTimelapseFrame ×N
                                          → 간격 ×2를 Worker에 재통지
                                        └ workspace.writeTimelapseFrame ──▶ OPFS 쓰기

[촬영 종료 — 실경과 확정]
  timelapseService.stopCollection()  →  elapsedSec = (now() - startedAt)/1000

[Result [다음] 1단계 — 선별 + 인코딩]
  timelapseEncoder.encodeTimelapse()            (메인)
    1. names = workspace.listTimelapseFrames()          (정렬됨)
    2. plan  = planTimelapse({...})                     (도메인 · 순수)
       └ null이면 여기서 끝. 결과 null
    3. path  = detectEncoderPath(config)                (런타임 감지)
    4-B. "webcodecs"  → encodeClient(spawn encode.worker)
             worker: OPFS **읽기** → createImageBitmap → OffscreenCanvas(짝수) →
                     VideoFrame(ts, dur) → VideoEncoder → mp4-muxer → ArrayBuffer
    4-A. "mediarecorder" → mediaRecorderMp4 (메인 전용, 실시간 재생-녹화)
    4-C. "none"       → null
    5. Blob(video/mp4) | null  + 진단 로그(logger)
```

### 3.1 계층 배치 규칙 (15 §2·§3.1 적용)

| 무엇 | 어디 | 왜 |
|------|------|-----|
| 배속·선별·타임스탬프·비트레이트·짝수 클램프·스풀 간격·솎아내기 계획 | **`src/domain`** (순수) | node에서 전부 검증된다. 규격이 바뀌면 여기 하나만 고친다 |
| `VideoEncoder`·`MediaRecorder`·`OffscreenCanvas`·`createImageBitmap`·OPFS 핸들 | **어댑터**(주입 가능한 포트 뒤) | 브라우저 전용. 포트로 뽑아 node에서 가짜를 주입한다 |
| 오케스트레이션(경로 판정 → 선별 → 인코딩 → `null` 계약 → 로그) | **`timelapseEncoder.ts`**(메인) | 로그는 메인에서만 살아남는다(F9) |

---

## 4. 의존성 결정 — MP4 muxer

### 4.1 결론: **`mp4-muxer@5.2.2`를 정확한 버전으로 핀 고정해 도입한다.**

**필요한 이유**(불필요 판단이 아닌 이유):

- WebCodecs `VideoEncoder`의 출력은 `EncodedVideoChunk`(raw H.264 NAL)다. **컨테이너가 아니다.**
  브라우저에는 "chunk를 mp4로 감싸는" 표준 API가 **없다**. 경로 B를 쓰려면 muxer가 반드시 필요하다.
- "경로 A(MediaRecorder)만 쓰면 muxer가 필요 없지 않은가"에 대한 답: **안 된다.**
  ① Firefox는 `video/mp4;codecs=avc1`을 지원하지 않는다(항상 `false`), Chrome은 130+에서야 생겼다
  ([04 §7.3b] 표) → 경로 A만 두면 다수 환경에서 타임랩스가 통째로 사라진다.
  ② 경로 A는 `requestFrame()` 도착 시각으로 타임스탬프가 정해져 **출력 길이가 흔들린다**.
  10~15초 목표([analysis/14 §7.2])를 보장할 수 없다.
  ③ 경로 A는 `MediaRecorder`·`captureStream`이 **Window 전용**이라 메인 스레드를 12초 이상 점유한다.
- 자체 muxer 구현은 배제한다: `moov`/`stts`/`stsz`/`stco`/`avcC`를 손으로 쓰면
  "컨테이너 정상 종료"([analysis/14 §7.1])를 우리가 보증해야 하는데, 실기기 검증 예산이 없다.

### 4.2 후보 비교

| 후보 | 버전 | 라이선스 | 크기(unpacked) | 판정 |
|------|------|----------|----------------|------|
| **`mp4-muxer`** | **5.2.2** | **MIT** | 155,878 B | **채택** |
| `mediabunny`(후속) | 1.52.1 | **MPL-2.0** | 9,954,165 B | **탈락** — MIT/Apache-2.0 계열이 아니고(파일 단위 카피레프트·수정 시 소스 공개 의무), 크기가 64배다 |
| 자체 구현 | — | — | — | 탈락(위 사유) |

### 4.3 deprecated 경고에 대한 판단 (리뷰어가 반드시 볼 지점)

`npm install` 시 `"This library is superseded by Mediabunny."` 경고가 뜬다(F15). 그럼에도 채택하는 근거:

1. **라이선스가 우리 기준을 만족하는 유일한 후보**다. 후속작은 MPL-2.0이라 상용 배포 정책상 배제된다.
2. 기능이 고정된 라이브러리다. 우리가 쓰는 표면은 `Muxer` + `ArrayBufferTarget` +
   `addVideoChunk` + `finalize` **4개뿐**이며, MP4 컨테이너 규격은 변하지 않는다.
3. **런타임 CDN 로드가 아니다**([01 §7]). 번들에 포함되고 `package-lock.json`으로 고정되므로
   패키지가 npm에서 사라져도 재현 빌드가 깨지지 않는다. MIT라 최악의 경우 **vendoring**이 합법이다.
4. 표면이 4개뿐이라 교체 비용이 `encode.worker.ts` 한 파일에 국한된다(§6.5의 `createMuxer` 포트).

### 4.4 설치와 기록

```powershell
cd E:\Study\photobooth\webclient
npm install --save-exact mp4-muxer@5.2.2
```

- `--save-exact` 필수. `package.json`에 **`"mp4-muxer": "5.2.2"`** (캐럿·틸드 금지 — [01 §7]).
- `package-lock.json`도 커밋 대상이다.

`webclient/THIRD-PARTY.md`를 **신규 생성**한다(현재 없음 — F18). 내용:

```markdown
# 서드파티 라이선스 고지 (MC포토 웹 클라이언트)

배포물(`web/kiosk/` 정적 번들)에 포함되는 서드파티 코드의 라이선스 목록이다.
상용 배포 시 이 문서를 함께 제공한다. 새 런타임 의존을 추가하면 **여기에 먼저 적는다**.

> 웹 클라이언트의 타임랩스 인코딩은 **브라우저 내장 인코더**(WebCodecs / MediaRecorder)를 쓴다.
> Windows 클라이언트와 달리 **ffmpeg(GPLv3) 노출이 없다**(`12 B14`).

| 패키지 | 버전 | 라이선스 | 용도 | 비고 |
|--------|------|----------|------|------|
| react | 18.3.1 | MIT | UI 렌더링 | — |
| react-dom | 18.3.1 | MIT | UI 렌더링 | — |
| zustand | 5.0.2 | MIT | 상태 관리 | — |
| mp4-muxer | 5.2.2 | MIT | WebCodecs 출력(H.264 chunk)을 MP4 컨테이너로 muxing | 상류에서 deprecated(후속 `mediabunny`는 MPL-2.0이라 미채택). MIT이므로 필요 시 vendoring 가능 |

개발 전용 의존(`devDependencies`)은 배포물에 포함되지 않으므로 목록에서 제외한다.

## 라이선스 전문
- MIT License 전문: 각 패키지의 `node_modules/{pkg}/LICENSE` 참조.
  배포 패키징 시 위 4개 패키지의 LICENSE 파일을 함께 동봉한다.
```

---

## 5. 도메인 순수 함수 명세

> 두 파일 모두 **도메인 밖을 import하지 않고**(`../mathCompat`·`./timelapseSpeed` 상대 경로만),
> `performance`·`Date`·`Math.random`·`console`·브라우저 전역을 **한 글자도 쓰지 않는다**.
> 새 파일은 `purity.test.ts`의 glob이 자동으로 잡는다(F11) — 별도 등록 불필요.
> 시간은 전부 **인자로 주입**받는다(15 §3.2).

### 5.1 `src/domain/capture/timelapsePlan.ts` (신규)

```ts
/**
 * 타임랩스 선별·인코딩 규격 — 04 §7.2·§7.3c·§7.4
 *
 * 촬영 중에는 인코딩하지 않고 OPFS에 스풀만 한다. 종료 시 **실제 경과**로 배속을 정하고
 * 스풀에서 균등 선별한다. Windows는 실제 녹화 길이로 배속을 계산하므로([바로 촬영] 다용
 * 세션도 원속 산출), 예상 길이로 stride를 고정하면 그 동등성이 깨진다.
 */
import { roundHalfToEven } from "../mathCompat";
import { computeSpeedFactor, expectedOutputSeconds } from "./timelapseSpeed";

/** 출력 컨테이너 타임라인 fps(04 §7.2). */
export const TIMELAPSE_OUTPUT_FPS = 30;
/** 이 수보다 적게 선별되면 1초 미만 영상이라 만들지 않는다(04 §7.2). */
export const TIMELAPSE_MIN_FRAMES = 30;
/** 백프레셔 임계 — `encodeQueueSize`가 이 값을 넘으면 드롭한다(04 §7.5). */
export const TIMELAPSE_ENCODE_QUEUE_LIMIT = 8;

/** 코덱 후보 — Baseline L3.0 우선, 실패 시 순서대로(04 §7.3c). */
export const TIMELAPSE_CODEC_CANDIDATES = ["avc1.42001E", "avc1.42E01E", "avc1.4D001E"] as const;

export interface TimelapsePlan {
  /** computeSpeedFactor(actualSeconds). */
  readonly speedFactor: number;
  /** 목표 출력 길이(초) = actualSeconds / speedFactor. */
  readonly outputSeconds: number;
  /** 30fps 기준 이상적 프레임 수(스풀이 부족하면 실제 선별 수가 이보다 적다). */
  readonly targetFrames: number;
  /** 선별한 스풀 배열 인덱스(오름차순·중복 없음). */
  readonly selectedIndices: readonly number[];
  /** 프레임 1장당 duration(μs). 스풀 부족 시 33333보다 길어진다. */
  readonly frameDurationUs: number;
  /** 프레임별 프레젠테이션 타임스탬프(μs). `selectedIndices`와 같은 길이·순서. */
  readonly timestampsUs: readonly number[];
}

export interface TimelapsePlanInput {
  /** 스풀에 남아 있는 프레임 수. */
  readonly spoolFrameCount: number;
  /** 촬영 시퀀스 시작~종료 **실경과**(초). */
  readonly actualSeconds: number;
  readonly outputFps?: number;
  readonly minFrames?: number;
}

/** 선별 계획. 만들 가치가 없으면 **`null`**(예외 아님 — VF-6). */
export function planTimelapse(input: TimelapsePlanInput): TimelapsePlan | null;

/**
 * `total`개에서 `count`개를 균등 선별한 인덱스.
 * `index_i = floor(i * total / count)` — `count <= total`이면 **strictly increasing**이라
 * 중복이 나오지 않는다. `count <= 0 || total <= 0`이면 빈 배열.
 */
export function evenlySample(total: number, count: number): number[];

/**
 * 04 §7.4 비트레이트 표. CRF 20 상당 근사.
 *   ≤640×854 → 2.5Mbps · ≤810×1080 → 5Mbps · ≤1080×1440 → 8Mbps
 *   그 이상 → w*h*30*0.12 (12Mbps 상한)
 */
export function timelapseBitrate(width: number, height: number): number;

/**
 * yuv420p(4:2:0)는 **양변이 짝수**여야 인코더가 열린다(04 §7.3c).
 * Windows `FfmpegArgs.EvenDimensionCrop`(`crop=trunc(iw/2)*2:trunc(ih/2)*2`)과 동일 식.
 * 1443×1080 → 1442×1080. 최소 2를 보장한다.
 */
export function evenDimensions(width: number, height: number): { width: number; height: number };
```

`planTimelapse` 본문 규격(그대로 구현할 것):

```
fps        = outputFps ?? TIMELAPSE_OUTPUT_FPS
minFrames  = minFrames ?? TIMELAPSE_MIN_FRAMES
if (!Number.isFinite(actualSeconds) || actualSeconds <= 0) return null
if (!Number.isFinite(spoolFrameCount) || spoolFrameCount <= 0) return null

speedFactor   = computeSpeedFactor(actualSeconds)                  // 도메인 재사용(F1)
outputSeconds = expectedOutputSeconds(actualSeconds, speedFactor)  // 도메인 재사용(F1)
targetFrames  = roundHalfToEven(outputSeconds * fps)               // Math.round 금지(F12)
count         = Math.min(targetFrames, spoolFrameCount)
if (count < minFrames) return null

selectedIndices = evenlySample(spoolFrameCount, count)
totalUs         = Math.round(outputSeconds * 1_000_000)
frameDurationUs = Math.max(1, Math.round(totalUs / count))
timestampsUs[i] = Math.round((i * totalUs) / count)                // 누적 드리프트 방지
```

> **`timestamp = i * 33333μs`(WBS 표현)과의 관계 — 리뷰어 주의**
> WBS Step 9 본문은 `timestamp = i * 33333μs`라고 쓰고, [04 §7.2]는
> `프레임 duration = outputSec / frames.length`라고 쓴다. **둘은 모순이 아니다.**
> 스풀이 충분해 `count === targetFrames`인 정상 경로에서는
> `totalUs/count = outputSeconds*1e6 / (outputSeconds*30) = 33333.3…μs`로 **동일**하다.
> 스풀이 부족한 경우에만 갈라지는데, 그때는 [04 §7.2]의 "duration이 길어질 뿐 길이는 유지"가
> 더 구체적인 규격이므로 그것을 따른다(실효 소스 fps ≤15 — [12 C2] 등재 사항).
> `i * 33333`을 그대로 쓰면 스풀 부족 시 출력이 **의도보다 짧아진다**.

> **여기에는 spec-vector를 추가하지 않는다.** Windows 대응 함수가 없다(Windows는 ffmpeg
> `setpts,fps=30` 필터가 처리한다). 교차 고정할 대상이 없으므로 `docs/spec-vectors/`를
> 건드리지 않으며, 따라서 **`dotnet test`를 돌릴 이유도 없다**(15 §3.3).
> `evenlySample`이 `roundHalfToEven`이 아니라 `floor`를 쓰는 것도 같은 이유다 —
> 크로스 플랫폼 계약이 없는 웹 전용 함수이고, `floor`라야 인덱스 중복이 원천 차단된다.

### 5.2 `src/domain/capture/timelapseSpool.ts` (신규)

```ts
/**
 * 촬영 중 스풀 수집 정책 — 04 §7.2
 *
 * 상태를 들고 있지 않는다(수집 간격 판정은 가공 Worker가, 파일 수 관리는 메인이 한다).
 * 두 곳이 같은 규칙을 쓰도록 규칙만 여기에 둔다.
 */

/** 수집 상한 15fps → 기본 간격 66.67ms(04 §7.2). */
export const TIMELAPSE_SPOOL_INTERVAL_MS = 1000 / 15;
/** 스풀 상한. 도달하면 절반 솎아내고 간격을 2배로 한다. */
export const TIMELAPSE_SPOOL_MAX_FRAMES = 900;
/** 솎아내기 배수. */
export const TIMELAPSE_SPOOL_DECIMATION_FACTOR = 2;

/**
 * 이번 가공 프레임을 스풀할 것인가.
 * @param lastCapturedAtMs 직전 스풀 시각. **아직 한 번도 없으면 `-Infinity`**를 넘긴다
 *        (0을 쓰면 첫 프레임을 먹는다 — 15 §4 함정 #4와 동종).
 */
export function shouldSpoolFrame(
  lastCapturedAtMs: number,
  nowMs: number,
  intervalMs: number,
): boolean;

export interface SpoolDecimationPlan {
  /** 삭제할 파일명(입력 배열의 홀수 인덱스). */
  readonly remove: readonly string[];
  /** 삭제 후 남는 수. */
  readonly keptCount: number;
}

/**
 * 상한 도달 시 **홀수 인덱스 항목을 버려** 시간 간격을 2배로 벌린다.
 * 남는 파일명은 **그대로 유지**한다(재번호 없음) — 0 패딩이라 문자열 정렬 = 시간 정렬이
 * 계속 성립하고, 이후 프레임은 증가하는 index로 계속 붙는다(F2).
 * 상한 미만이면 `null`.
 */
export function planDecimation(
  sortedNames: readonly string[],
  maxFrames?: number,
): SpoolDecimationPlan | null;

/** 솎아낸 뒤의 수집 간격. */
export function decimatedInterval(intervalMs: number): number;
```

### 5.3 `src/domain/index.ts` 추가

```ts
export * from "./capture/timelapsePlan";
export * from "./capture/timelapseSpool";
```
(`./capture/timelapseSpeed` 다음 줄에 붙인다.)

---

## 6. 어댑터 명세 (`src/adapters/encode/`)

### 6.0 공통 규약

| 규칙 | 내용 |
|------|------|
| 예외 전파 금지 | 모든 공개 함수는 `null`/`false`를 돌려준다(01 §2.1). `throw`는 프로그래밍 오류에만 |
| `console.*` 금지 | `logger.*`만. **단 Worker 안에서는 logger를 쓰지 않는다**(F9) — 사유 문자열을 응답에 담아 메인이 기록한다 |
| 리소스 | `ImageBitmap`·`VideoFrame`은 **`finally`에서 `close()`**. `MediaStreamTrack`은 `stop()` |
| 한국어 주석 | 기존 파일과 같은 밀도로 **"왜"** 를 남긴다 |

### 6.1 `encodeProtocol.ts` (신규)

```ts
/** 타임랩스 인코딩 Worker 메시지 프로토콜 — 04 §7.3 */

export type EncoderPath = "webcodecs" | "mediarecorder" | "none";

/** 실제로 인코더에 넘기는 설정. width/height는 **짝수 클램프 후** 값이다. */
export interface TimelapseEncodeConfig {
  readonly codec: string;      // "avc1.42001E" 등 — 판정에서 확정된 값
  readonly width: number;
  readonly height: number;
  readonly bitrate: number;
  readonly framerate: number;  // 30
}

/** Worker에 넘기는 인코딩 지시. 선별은 이미 끝나 있다(도메인이 계산). */
export interface EncodeJob {
  /** OPFS 절대 경로 — `sessions/{sessionId}/tl`. */
  readonly dirPath: string;
  /** 선별된 파일명(시간 오름차순). */
  readonly names: readonly string[];
  readonly timestampsUs: readonly number[];
  readonly frameDurationUs: number;
  readonly config: TimelapseEncodeConfig;
}

export interface EncodeStats {
  readonly encodedFrames: number;
  /** 백프레셔로 버린 프레임 수(04 §7.5). */
  readonly droppedFrames: number;
  /** 디코딩/로드 실패로 건너뛴 프레임 수. */
  readonly skippedFrames: number;
  readonly elapsedMs: number;
}

export type EncodeRequest =
  | { readonly type: "encode"; readonly id: number; readonly job: EncodeJob };

export type EncodeResponse =
  | { readonly type: "done"; readonly id: number; readonly buffer: ArrayBuffer; readonly stats: EncodeStats }
  | { readonly type: "failed"; readonly id: number; readonly reason: string; readonly stats: EncodeStats };

/** Worker 응답 하드 타임아웃. 04 §8 예산(375프레임 ≤6s)의 10배 여유. */
export const ENCODE_WORKER_TIMEOUT_MS = 60_000;
/** `encoder.flush()` 타임아웃 — 정지 실패는 강제 종료한다(04 §7.5). */
export const ENCODE_FLUSH_TIMEOUT_MS = 10_000;
```

### 6.2 `encoderSupport.ts` (신규) — 경로 판정

**판정 순서는 계약이다: B(WebCodecs) → A(MediaRecorder) → C(none).** 버전 문자열·UA로
판정하지 않는다. `isConfigSupported`는 **비동기**이며, **실제 사용할 config로** 질의한다([04 §7.3b]).

```ts
export interface EncoderProbe {
  readonly path: EncoderPath;
  /** 경로 B에서 채택된 코덱 문자열. A·C면 null. */
  readonly codec: string | null;
  /** 진단·로그용 판정 사유. */
  readonly reason: string;
  /** 후보별 질의 결과(진단 모달 — Step 16). */
  readonly probed: readonly { readonly codec: string; readonly supported: boolean }[];
}

export interface EncoderProbeDeps {
  /** 기본값: `globalThis.VideoEncoder`. 없으면 undefined. */
  readonly videoEncoder?: {
    isConfigSupported(config: VideoEncoderConfig): Promise<{ supported?: boolean }>;
  } | undefined;
  /** 기본값: `globalThis.MediaRecorder`. Worker 전역에는 **없다**(04 §7.3a). */
  readonly mediaRecorder?: { isTypeSupported(type: string): boolean } | undefined;
  /** 경로 B는 Worker에서만 돈다 → Worker가 없으면 B를 쓸 수 없다. 기본값 `typeof Worker !== "undefined"`. */
  readonly workerAvailable?: boolean;
}

export const MEDIARECORDER_MP4_MIME = "video/mp4;codecs=avc1";

/**
 * 경로 판정. **런타임 기능 감지만** 쓴다(함정 #2 — TS DOM lib은 있다고 선언한다).
 * @param size 짝수 클램프 **전** 가공 해상도. 내부에서 evenDimensions·timelapseBitrate를 적용해
 *             실제 config로 질의한다.
 */
export async function detectEncoderPath(
  size: { width: number; height: number },
  deps?: EncoderProbeDeps,
): Promise<EncoderProbe>;

/** 마지막 판정 결과(진단 모달 — Step 16이 읽는다). 없으면 null. */
export function lastEncoderProbe(): EncoderProbe | null;
```

판정 본문:

```
probed = []
if (deps.videoEncoder && workerAvailable) {
  for (codec of TIMELAPSE_CODEC_CANDIDATES) {
    try { r = await videoEncoder.isConfigSupported({ codec, width, height, bitrate, framerate: 30 }) }
    catch { r = { supported: false } }            // 던지는 구현이 있다 — 삼킨다
    probed.push({ codec, supported: r?.supported === true })
    if (r?.supported === true) return { path:"webcodecs", codec, reason:`WebCodecs ${codec}`, probed }
  }
}
if (deps.mediaRecorder?.isTypeSupported(MEDIARECORDER_MP4_MIME) === true)
  return { path:"mediarecorder", codec:null, reason:"MediaRecorder video/mp4;codecs=avc1", probed }
return { path:"none", codec:null, reason:"H.264 인코더 없음", probed }
```

- `workerAvailable === false`이면 **경로 B를 건너뛴다**(경로 B는 Worker 전용 구현이다 — §7).
- 결과를 모듈 변수에 캐시하고 `lastEncoderProbe()`로 노출한다(진단 E6 — [12 §E6]).
- **경로 A로 떨어져도 실패가 아니다.** `none`도 실패가 아니다(계약상 합법 — VF-6).

### 6.3 `webCodecsMp4.ts` (신규) — 경로 B 코어 (**모든 효과 주입**)

이 파일은 **브라우저 API를 하나도 직접 부르지 않는다.** `VideoEncoder`·`createImageBitmap`·
`OffscreenCanvas`·`mp4-muxer`·OPFS는 전부 포트로 들어온다 → **node에서 전량 검증 가능**하다.
그래서 이 파일은 `mp4-muxer`를 **import하지 않는다**(A1 위험을 `encode.worker.ts` 한 파일에 가둔다).

```ts
/** 인코더에 넣을 프레임 1장. 실제 타입은 브라우저의 `VideoFrame`. */
export interface EncodableFrame {
  close(): void;
}

export interface VideoEncoderLike {
  readonly encodeQueueSize: number;
  readonly state: string;                 // "unconfigured" | "configured" | "closed"
  configure(config: VideoEncoderConfig): void;
  encode(frame: EncodableFrame, options?: { keyFrame?: boolean }): void;
  flush(): Promise<void>;
  close(): void;
}

export interface Mp4MuxerLike {
  addVideoChunk(chunk: unknown, meta?: unknown): void;
  finalize(): void;
  /** `finalize()` 후의 완성 버퍼. */
  buffer(): ArrayBuffer;
}

export interface WebCodecsMp4Deps {
  /** 스풀 프레임 1장을 읽어온다. 부재·실패는 null. */
  readonly loadFrame: (name: string) => Promise<Blob | null>;
  /**
   * Blob → 인코딩 가능한 프레임. **짝수 크기로 맞춰 그리는 책임이 여기 있다.**
   * 실패는 null(예외 금지).
   */
  readonly createFrame: (
    blob: Blob,
    init: { timestampUs: number; durationUs: number; width: number; height: number },
  ) => Promise<EncodableFrame | null>;
  readonly createEncoder: (handlers: {
    output: (chunk: unknown, meta: unknown) => void;
    error: (reason: string) => void;
  }) => VideoEncoderLike;
  readonly createMuxer: (config: TimelapseEncodeConfig) => Mp4MuxerLike;
  /** 경과 계측. 기본 주입 필수(도메인 규칙과 동일하게 시간은 주입한다 — 15 §3.2). */
  readonly now: () => number;
  readonly flushTimeoutMs?: number;
}

export interface WebCodecsMp4Output {
  readonly buffer: ArrayBuffer;
  readonly stats: EncodeStats;
}

/** 성공하면 버퍼, 어떤 실패든 `{ ok:false, reason, stats }`. **절대 throw하지 않는다.** */
export async function encodeWithWebCodecs(
  job: EncodeJob,
  deps: WebCodecsMp4Deps,
): Promise<
  | { readonly ok: true; readonly output: WebCodecsMp4Output }
  | { readonly ok: false; readonly reason: string; readonly stats: EncodeStats }
>;
```

본문 규격:

```
startedAt = deps.now(); encoded = 0; dropped = 0; skipped = 0
muxer = createMuxer(config)                       // 실패(throw) → catch → ok:false
failure: string | null = null
encoder = createEncoder({
   output: (chunk, meta) => { try { muxer.addVideoChunk(chunk, meta) } catch (e) { failure ??= ... } },
   error:  (reason)      => { failure ??= reason },      // 비동기 오류 콜백
})
try {
  encoder.configure({ codec, width, height, bitrate, framerate: 30,
                      latencyMode: "quality", avc: { format: "avc" } })
} catch { → ok:false("인코더 설정 거부") }

for (i = 0; i < job.names.length; i++) {
  if (failure !== null) break
  blob = await loadFrame(names[i]);        if (blob === null) { skipped++; continue }
  frame = await createFrame(blob, { timestampUs: timestampsUs[i], durationUs: frameDurationUs,
                                    width, height })
  if (frame === null) { skipped++; continue }
  try {
    // 백프레셔(04 §7.5) — 큐가 밀리면 이 프레임을 버린다.
    // 드롭해도 **출력 길이는 유지된다**: 타임스탬프가 인덱스로 고정돼 있어
    // 직전 프레임의 duration이 그만큼 늘어날 뿐이다.
    if (encoder.encodeQueueSize > TIMELAPSE_ENCODE_QUEUE_LIMIT) { dropped++; continue }
    // 1초마다 키프레임 — 첫 프레임은 반드시 키프레임이어야 재생이 시작된다.
    encoder.encode(frame, { keyFrame: encoded % TIMELAPSE_OUTPUT_FPS === 0 })
    encoded++
  } catch (e) { failure ??= ...; }
  finally { frame.close() }     // VideoFrame은 GC 대상이 아니다(WR8)
}

if (encoded === 0) → ok:false("인코딩된 프레임이 없습니다")
await raceWithTimeout(encoder.flush(), flushTimeoutMs ?? ENCODE_FLUSH_TIMEOUT_MS)
   // 타임아웃 → ok:false("인코더 flush 타임아웃"). 어느 쪽이든 finally에서 close()
if (failure !== null) → ok:false(failure)
muxer.finalize()
buffer = muxer.buffer()
finally { if (encoder.state !== "closed") try { encoder.close() } catch {} }
```

> **`createMuxer`에 `frameRate`를 넘기지 않는다** — F17. `VideoOptions.frameRate`를 주면
> muxer가 타임스탬프를 그 격자로 반올림하는데, 스풀 부족 시 우리 간격은 33333μs가 아니다.
> 그 경우 여러 프레임이 같은 격자로 뭉개져 **컨테이너 길이가 망가진다**.

### 6.4 `mediaRecorderMp4.ts` (신규) — 경로 A 코어 (**메인 스레드 전용**)

`MediaRecorder`·`HTMLCanvasElement.captureStream`은 **Window 전용**이다([04 §7.3a]).
따라서 이 경로는 [04 §10]의 "타임랩스 인코딩 = Worker" 규약의 **명시된 예외**다.
또 `requestFrame()` 기반이라 **출력 길이만큼 실제 시간이 걸린다**(≤15초). 이 사실을 주석에 남긴다.

```ts
/** 캔버스+레코더 묶음. 브라우저 구현과 node 가짜가 같은 표면을 만족한다. */
export interface CanvasRecorderPort {
  /** 녹화 시작. */
  start(): void;
  /** JPEG 1장을 캔버스에 그리고 `track.requestFrame()`. 실패는 false. */
  pushFrame(blob: Blob): Promise<boolean>;
  /** 정지 후 mp4 Blob. 타임아웃·실패는 null. */
  stop(timeoutMs: number): Promise<Blob | null>;
  /** 트랙 stop + 참조 해제. **성공·실패 무관하게 반드시 호출**한다. */
  dispose(): void;
}

/** 브라우저 구현. 미지원·예외는 null(호출측이 경로 C로 축소). */
export function createCanvasRecorderPort(config: TimelapseEncodeConfig): CanvasRecorderPort | null;

export interface MediaRecorderMp4Deps {
  readonly loadFrame: (name: string) => Promise<Blob | null>;
  readonly createPort: (config: TimelapseEncodeConfig) => CanvasRecorderPort | null;
  readonly now: () => number;
  readonly delay: (ms: number) => Promise<void>;
  readonly stopTimeoutMs?: number;
}

export async function encodeWithMediaRecorder(
  job: EncodeJob,
  deps: MediaRecorderMp4Deps,
): Promise<
  | { readonly ok: true; readonly blob: Blob; readonly stats: EncodeStats }
  | { readonly ok: false; readonly reason: string; readonly stats: EncodeStats }
>;
```

본문 규격:

```
port = createPort(config);  if (port === null) → ok:false("캔버스 녹화를 시작할 수 없습니다")
try {
  port.start()
  startedAt = now()
  for (i in names) {
    blob = await loadFrame(names[i]); if (null) { skipped++; continue }
    if (!await port.pushFrame(blob)) { skipped++; continue }
    encoded++
    // **실경과 기준 페이싱**(WM3와 동종 — tick 누적은 탭 스로틀에서 어긋난다)
    target = startedAt + (timestampsUs[i+1] ?? timestampsUs[i] + frameDurationUs) / 1000
    wait = target - now();  if (wait > 0) await delay(wait)      // 뒤처졌으면 기다리지 않는다
  }
  if (encoded === 0) → ok:false(...)
  out = await port.stop(stopTimeoutMs ?? ENCODE_FLUSH_TIMEOUT_MS)
  return out === null ? ok:false("레코더 정지 실패") : ok:true(out)
} finally { port.dispose() }
```

`createCanvasRecorderPort` 브라우저 구현 지침:

- `document.createElement("canvas")`. **DOM에 붙이지 않는다**(레이아웃 비용 0).
- `canvas.width/height = config.width/height`(이미 짝수).
- `stream = canvas.captureStream(0)` → 자동 프레임 발행 없음. 각 프레임마다 `track.requestFrame()`.
- `new MediaRecorder(stream, { mimeType: MEDIARECORDER_MP4_MIME, videoBitsPerSecond: config.bitrate })`.
- `ondataavailable` 누적 → `onstop`에서 `new Blob(chunks, { type: "video/mp4" })`.
- `pushFrame`: `createImageBitmap(blob)` → `ctx.drawImage(bitmap, 0, 0)` → **`bitmap.close()`(finally)** → `requestFrame()`.
- `dispose`: `stream.getTracks().forEach(t => t.stop())` + 참조 null. **예외 삼킴.**

### 6.5 `encode.worker.ts` (신규) — 경로 B 실행 껍데기

```ts
/// <reference lib="webworker" />
```

**여기가 유일하게 `mp4-muxer`를 import하는 파일이다**(A1 격리).
브라우저 기본 구현을 만들어 `encodeWithWebCodecs`에 주입하고, 결과를 메인으로 되돌린다.
**logger를 쓰지 않는다**(F9) — 사유는 `failed.reason`으로 넘긴다.

```ts
import { Muxer, ArrayBufferTarget } from "mp4-muxer";
import { encodeWithWebCodecs, ... } from "./webCodecsMp4";
```

기본 포트 구현:

| 포트 | 구현 |
|------|------|
| `loadFrame` | `sessions/{id}/tl` **디렉터리 핸들을 1회만 연다**(경로를 매번 루트부터 걷지 않는다) → `dir.getFileHandle(name)` → `getFile()`. 실패는 null |
| `createFrame` | `createImageBitmap(blob)` → **재사용 `OffscreenCanvas(width,height)`** 에 `drawImage(bitmap, 0, 0)` → `bitmap.close()`(finally) → `new VideoFrame(canvas, { timestamp: timestampUs, duration: durationUs })`. 예외는 null |
| `createEncoder` | `new VideoEncoder({ output, error: (e) => handlers.error(e?.message ?? String(e)) })` |
| `createMuxer` | `new Muxer({ target: new ArrayBufferTarget(), video: { codec: "avc", width, height }, fastStart: "in-memory" })` — **`frameRate` 미지정**(F17). `buffer()`는 `target.buffer` |
| `now` | `performance.now()` |

- `drawImage(bitmap, 0, 0)`은 소스가 config보다 1px 클 때 **우/하단을 잘라낸다** — Windows의
  `crop=trunc(iw/2)*2:trunc(ih/2)*2`와 같은 결과다(F13).
- 완료: `postMessage({type:"done", id, buffer, stats}, { transfer: [buffer] })` — **버퍼 소유권 이전**.
- **OPFS 쓰기는 하지 않는다.** 읽기만 한다(F8 규칙 준수). 완성된 mp4는 메인이 받아서
  기존 `opfsWriter` 경로로 저장한다(Step 10 소관).

### 6.6 `encodeClient.ts` (신규) — Worker RPC

```ts
export interface EncodeClient {
  /** 1회성 인코딩. 실패·타임아웃·미지원은 null. */
  run(job: EncodeJob): Promise<{ blob: Blob; stats: EncodeStats } | { error: string; stats: EncodeStats | null }>;
  /** 진행 중인 작업을 즉시 끊는다(화면 이탈). 멱등. */
  abort(): void;
}

export function createEncodeClient(spawn?: () => WorkerLike): EncodeClient;
```

- **작업마다 Worker를 새로 띄우고 `finally`에서 `terminate()`** 한다. 하드웨어 인코더를 붙든 채
  대기하지 않기 위해서다(카메라 재진입 시 자원 경합). `frameProcessor`처럼 상주시키지 않는다.
- `ENCODE_WORKER_TIMEOUT_MS`(60s) 타임아웃 → `terminate()` → `{ error: "인코딩 타임아웃" }`.
- `spawn` 기본값: `new Worker(new URL("./encode.worker.ts", import.meta.url), { type:"module", name:"mcphoto-timelapse-encoder" })`.
  `typeof Worker === "undefined"`면 `run()`이 즉시 `{ error }`. (경로 판정에서 이미 걸러지지만 이중 방어.)
- `abort()`는 `terminate()` + 대기 중 Promise를 `{ error:"중단됨" }`으로 해소.

### 6.7 `timelapseEncoder.ts` (신규) — 오케스트레이터 (메인)

**로그를 남기는 유일한 지점**이다(F9·F19).

```ts
export interface TimelapseResult {
  readonly blob: Blob;
  readonly path: EncoderPath;          // "webcodecs" | "mediarecorder"
  readonly width: number;
  readonly height: number;
  readonly frameCount: number;         // 실제 인코딩된 프레임
  readonly durationSec: number;        // 계획된 출력 길이
  readonly speedFactor: number;
  readonly bytes: number;
  readonly elapsedMs: number;
}

export interface EncodeTimelapseInput {
  readonly workspace: SessionWorkspace;
  /** 촬영 시퀀스 실경과(초). */
  readonly actualSeconds: number;
  /** 스풀된 가공 프레임 크기(짝수 클램프 전). */
  readonly size: { width: number; height: number };
}

export interface EncodeTimelapseDeps {
  readonly detect?: typeof detectEncoderPath;
  readonly client?: EncodeClient;
  readonly runMediaRecorder?: typeof encodeWithMediaRecorder;
  readonly now?: () => number;
}

/** 어떤 실패·미지원도 `null`. **절대 throw하지 않는다**(VF-6 · 04 §7.5). */
export async function encodeTimelapse(
  input: EncodeTimelapseInput,
  deps?: EncodeTimelapseDeps,
): Promise<TimelapseResult | null>;
```

본문 규격:

```
1  names = await workspace.listTimelapseFrames()            // 정렬된 파일명(F2)
   if (names.length === 0) { logger.warn("타임랩스 스풀 프레임이 없습니다", {...}); return null }
2  plan = planTimelapse({ spoolFrameCount: names.length, actualSeconds })     // 도메인
   if (plan === null) {
     logger.warn("타임랩스를 만들지 않음(선별 프레임 부족)",
                 { spooled: names.length, actualSeconds: round1(actualSeconds),
                   minFrames: TIMELAPSE_MIN_FRAMES })
     return null }
3  even = evenDimensions(size.width, size.height)
   probe = await detect(size)
   logger.info("타임랩스 인코더 경로 판정",
               { path: probe.path, codecName: probe.codec, reason: probe.reason })
      // ⚠️ 키 이름 주의(15 §4 함정 #1): `code`·`token`·`state`·`nonce`·`pin`은 마스킹된다.
      //    코덱 문자열은 반드시 `codecName`으로 담는다.
   if (probe.path === "none") { logger.warn("타임랩스 미제공(브라우저 H.264 인코더 없음)"); return null }
4  config = { codec: probe.codec ?? "avc1.42001E", width: even.width, height: even.height,
              bitrate: timelapseBitrate(even.width, even.height), framerate: TIMELAPSE_OUTPUT_FPS }
   job = { dirPath: timelapseDirPath(workspace.sessionId), names: plan.selectedIndices.map(i => names[i]!),
           timestampsUs: plan.timestampsUs, frameDurationUs: plan.frameDurationUs, config }
5  path B → client.run(job)              (Worker)
   path A → runMediaRecorder(job, {...}) (메인, 실시간)
6  실패 → logger.warn("타임랩스 생성 실패", { path, reason, ...stats }); return null
   성공 → logger.info("타임랩스 생성", { path, codecName, width, height,
                                       spooled: names.length, selected: job.names.length,
                                       encodedFrames, droppedFrames, skippedFrames,
                                       speedFactor, durationSec, bytes, elapsedMs })
          return { blob, ... }
```

- **경로 B가 실패했다고 경로 A로 자동 재시도하지 않는다.** 판정은 앞에서 1회다.
  B 실패는 통상 인코더 자체의 문제이고, A는 최대 15초를 더 소비하며 결과 보장도 없다.
  [04 §7.5]의 "실패 → `null`" 규격을 그대로 따른다. (경로 A는 **판정 단계**에서만 선택된다.)

### 6.8 `timelapseService.ts` (신규) — 수집 수명 + 결과 보관 (모듈 싱글턴)

`cameraService`와 동일한 형태다: **팩토리(DI) + 싱글턴 + 테스트용 setter**.

```ts
export interface TimelapseStats {
  readonly collecting: boolean;
  readonly spooled: number;       // OPFS에 실제로 기록된 수(솎아내기 반영)
  readonly droppedSpool: number;  // 쓰기 지연·실패로 버린 수
  readonly decimations: number;
  readonly intervalMs: number;
  readonly elapsedSec: number | null;
  readonly size: { width: number; height: number } | null;
}

export interface TimelapseService {
  /** 촬영 시퀀스 **직전**에 호출. 이 시점부터 종료까지만 수집한다([trigger]). */
  startCollection(workspace: SessionWorkspace): void;
  /** 마지막 컷 직후 호출. 실경과를 확정한다. 멱등. */
  stopCollection(): void;
  /** 선별 + 인코딩. **멱등**(이미 만들었으면 그대로 돌려준다). 실패·미지원은 null. */
  finish(): Promise<TimelapseResult | null>;
  /** Step 10(로컬 보관)·Step 11(업로드)이 읽는다. */
  current(): TimelapseResult | null;
  /** 수집 중단 + 진행 중 인코딩 중단 + 결과 폐기. 셸 `stopEncoder` 훅이 부른다. */
  stop(): void;
  stats(): TimelapseStats;
  /** 진단(Step 16). */
  encoderProbe(): EncoderProbe | null;
}

export interface TimelapseServiceDeps {
  readonly camera?: Pick<CameraService, "configureTimelapseSpool" | "onTimelapseFrame" | "processedSize">;
  readonly encode?: typeof encodeTimelapse;
  readonly client?: EncodeClient;
  readonly now?: () => number;
}

export function createTimelapseService(deps?: TimelapseServiceDeps): TimelapseService;
export function getTimelapseService(): TimelapseService;
export function setTimelapseServiceForTests(service: TimelapseService | null): void;
```

동작 규격:

**`startCollection(workspace)`**
```
if (collecting) return                                  // 멱등(StrictMode 이중 마운트)
workspace 보관; spooled=0; nextIndex=0; droppedSpool=0; decimations=0
intervalMs = TIMELAPSE_SPOOL_INTERVAL_MS; startedAt = now(); elapsedSec = null; result = null
size = null
unsubscribe = camera.onTimelapseFrame(onSpoolFrame)
camera.configureTimelapseSpool({ enabled: true, intervalMs, quality: SPOOL_JPEG_QUALITY })
logger.info("타임랩스 수집 시작", { intervalMs: Math.round(intervalMs), maxFrames: TIMELAPSE_SPOOL_MAX_FRAMES })
```

**`onSpoolFrame({ blob, width, height })`**
```
if (!collecting || workspace === null) return
size = { width, height }             // ⚠️ Result 시점엔 카메라가 이미 꺼져 있다. 여기서 기억해야 한다.
if (writeInFlight) { droppedSpool++; return }   // 백프레셔: OPFS가 못 따라가면 최신 것도 버린다
writeInFlight = true
const index = nextIndex++
void workspace.writeTimelapseFrame(index, blob)
  .then(ok => { ok ? spooled++ : droppedSpool++ })
  .catch(() => { droppedSpool++ })                     // 어댑터는 던지지 않지만 이중 방어
  .finally(() => { writeInFlight = false; maybeDecimate() })
```

**`maybeDecimate()`** (재진입 금지 플래그 1개)
```
if (decimating || spooled < TIMELAPSE_SPOOL_MAX_FRAMES) return
decimating = true
names = await workspace.listTimelapseFrames()
plan = planDecimation(names)                           // 도메인
if (plan !== null) {
  for (name of plan.remove) if (await workspace.removeTimelapseFrame(name)) spooled--
  intervalMs = decimatedInterval(intervalMs); decimations++
  camera.configureTimelapseSpool({ enabled: true, intervalMs, quality: SPOOL_JPEG_QUALITY })
  logger.info("타임랩스 스풀 솎아내기", { removed: plan.remove.length, kept: spooled,
                                        intervalMs: Math.round(intervalMs) })
}
decimating = false
```

**`stopCollection()`**
```
if (!collecting) return
collecting = false
elapsedSec = (now() - startedAt) / 1000
unsubscribe?.(); unsubscribe = null
try { camera.configureTimelapseSpool({ enabled: false, intervalMs, quality: SPOOL_JPEG_QUALITY }) } catch {}
     // 카메라가 이미 멈춰 processor가 없을 수 있다 — 무해하게 넘긴다
logger.info("타임랩스 수집 종료", { spooled, droppedSpool, decimations,
                                   elapsedSec: round1(elapsedSec) })
```

**`finish()`**
```
if (result !== null) return result                     // 멱등([다음] 이중 클릭)
if (finishing !== null) return finishing               // 동시 호출 합류
if (collecting) stopCollection()
if (workspace === null || elapsedSec === null || size === null) {
  logger.warn("타임랩스 생성 건너뜀(수집 정보 없음)"); return null }
finishing = encode({ workspace, actualSeconds: elapsedSec, size })
             .then(r => { result = r; return r })
             .catch(() => null)                        // 절대 throw하지 않는다
             .finally(() => { finishing = null })
return finishing
```

**`stop()`** — 셸 `stopEncoder` 훅(F6)
```
stopCollection()
client.abort()
result = null; workspace = null; size = null; elapsedSec = null
```

> **결과 Blob은 `sessionStore`에 넣지 않는다.** `sessionStore`는 도메인 세션 상태이고,
> `discardCaptureData()`가 지우는 대상은 컷·프레임이다. mp4는 서비스가 들고 있다가
> `stop()`(홈 복귀)에서 폐기한다. Step 10·11은 `getTimelapseService().current()`로 읽는다.

---

## 7. Worker 경계 설계

| 작업 | 어디서 | 근거 |
|------|--------|------|
| 스풀 프레임 JPEG 생성 | **`frameProcessor.worker`**(기존 Worker) | 가공 캔버스가 거기 있다. 메인으로 픽셀을 옮기면 왕복 비용 + WYSIWYG 어긋남 |
| 스풀 파일 **쓰기** | 메인 → **`opfsWriter.worker`** | OPFS 쓰기 단일 경계(F8). 메인에서 직접 쓰면 iOS에서 전 저장이 실패한다 |
| 경로 B 인코딩(디코드·VideoFrame·인코드·mux) | **`encode.worker`**(신규, 1회성) | [04 §10] 규약. 375프레임 JPEG 디코드가 메인을 막으면 결과 화면이 얼어붙는다 |
| 스풀 파일 **읽기**(경로 B) | `encode.worker`가 직접 | 읽기는 Worker 경계를 요구하지 않는다(F8). 375회 postMessage를 없앤다 |
| 경로 A 인코딩 | **메인 스레드** | `MediaRecorder`·`captureStream`이 Window 전용([04 §7.3a]). **규약의 명시된 예외** |
| 경로 판정 | 메인(`detectEncoderPath`) | `MediaRecorder`는 Worker 전역에 없어 Worker에서는 A를 판정할 수 없다 |
| 로깅 | **메인만** | Worker의 `logger`는 진단에 도달하지 않는다(F9) |

**경로 A와 "경로 B는 Worker" 규약이 충돌하지 않는 이유**: 두 경로는 상호 배타이며 판정에서
하나만 선택된다. 경로 A가 선택되면 Worker를 아예 띄우지 않는다(`encodeClient.run`을 부르지 않음).

---

## 8. 기존 파일 수정 명세 (설계 이탈 · 사유 포함)

> WBS Step 9의 "대상 파일"은 `src/adapters/encode/*`와 `tests/unit/encode/*`만 열거한다.
> 아래는 **범위 확장**이며, 각각 사유를 남긴다. `js-code-reviewer`는 이 목록을 근거로 판단한다.

### 8.1 가공 Worker에 **스풀 채널 신설** (필수 — 최대 리스크 제거)

| 파일 | 변경 |
|------|------|
| `src/adapters/camera/frameProcessorProtocol.ts` | 요청에 `{ type:"configureSpool"; enabled; intervalMs; quality }` 추가, 응답에 `{ type:"spoolFrame"; blob; width; height }` 추가, `export const SPOOL_JPEG_QUALITY = 0.8;` 추가 |
| `src/adapters/camera/frameProcessor.worker.ts` | 상태 `spoolEnabled/spoolIntervalMs/spoolQuality/lastSpoolAtMs(-Infinity)`. `processFrame`의 "소비자 3" 주석 자리에 스풀 분기(스틸 분기 **뒤**). `configureSpool` 케이스에서 off일 때 `lastSpoolAtMs = -Infinity`로 리셋 |
| `src/adapters/camera/frameProcessorClient.ts` | `configureSpool()` 전달, `spoolFrame` 응답을 리스너에 브로드캐스트. `terminate()`에서 스풀 리스너 clear |
| `src/adapters/camera/cameraTypes.ts` | `FrameProcessor`에 `configureSpool(o)` · `onSpoolFrame(l): () => void` 추가 |
| `src/adapters/camera/cameraService.ts` | `configureTimelapseSpool(o)` · `onTimelapseFrame(l)` 위임 추가. 프로세서가 없으면 무해하게 no-op |
| `tests/unit/camera/cameraService.test.ts` | `FakeProcessor`에 새 멤버 2개 구현(**F20 — 안 하면 `tsc --noEmit` 실패**). 기존 단언은 건드리지 않는다 |

**왜 `captureStill(0.8)`을 15fps로 부르는 방식을 쓰지 않는가 (핵심 사유):**
가공 Worker의 스틸 슬롯은 **1개짜리 덮어쓰기**다(F4). 스풀러가 66ms마다 스틸을 요청하면
컷 촬영 요청과 한 프레임 간격(33ms) 안에서 충돌할 확률이 대략 **컷당 20~25% 수준**으로
추정된다(스풀 15회/s × 프레임 간격 33ms의 절반). 정확한 수치와 무관하게 충돌하면
**먼저 온 요청이 소멸**하고 클라이언트가 5초 뒤 `null`을 돌려주는데, 그것이 컷 요청이면
컷이 빠지고 → 컷 수 < 슬롯 수 → **세션이 홈으로 강제 복귀한다**(F5). 타임랩스 기능을 붙이다가
촬영 자체를 깨뜨리는 것은 [non-goal]("인코딩 실패가 촬영을 중단시키지 않는다")의 정면 위반이다.
전용 채널을 두면 두 소비자가 서로를 침범하지 않는다.

### 8.2 `src/adapters/storage/sessionWorkspace.ts` — 경로 헬퍼 export (추가만)

```ts
/** 타임랩스 스풀 디렉터리(OPFS 절대 경로). 인코더 Worker가 직접 읽는다. */
export function timelapseDirPath(sessionId: string): string {
  return `${OPFS_DIRS.sessions}/${sessionId}/tl`;
}
```
`createSessionWorkspace` 내부의 `const timelapseDir = ...`도 이 함수로 바꿔 **정의를 1곳**으로 만든다.
기존 동작·시그니처 변화 없음.

### 8.3 `src/screens/capture/useCaptureRunner.ts` — 수집 배선 (F7 자리표시 채움)

- 3단계 직후(자리표시 주석 위치): `const timelapse = getTimelapseService(); timelapse.startCollection(workspace);`
- `configureShell({ ... })`를 다음으로 확장:
  ```ts
  cancelCaptureSequence: () => { sequence.cancel(); timelapse.stopCollection(); },
  stopEncoder: () => timelapse.stop(),
  stopCamera: () => camera.stop(),
  ```
  **`cancelCaptureSequence`에서도 수집을 멈추는 이유**: `returnHome`은
  `cancelCaptureSequence → cleanupWorkspace(폴더 삭제) → stopEncoder` 순서다(F6).
  `stopEncoder`에서만 멈추면 폴더 삭제 **후** 스풀 쓰기가 도착해 `tl/`을 되살려
  잔재가 남는다. 첫 단계에서 끊으면 그 창이 닫힌다.
- `sequence.run(...)` 반환 직후(6단계): `timelapse.stopCollection();`
  — 예외·취소 경로도 덮이도록 **`try`의 `finally`** 에 둔다.
- effect cleanup: `camera.stop()` **앞**에 `timelapse.stopCollection();`

### 8.4 `src/ui/views/FlowViews.tsx` — `ResultView.goNext`를 [다음] 1단계로 확장

[03 §8.1]이 정한 순서의 **1단계가 타임랩스 생성/마무리**다. 여기서 부르지 않으면
Step 9 산출물이 실행되는 경로가 없어 [관측] 기준을 만족할 수 없다.

```tsx
const [finishing, setFinishing] = useState(false);

async function goNext(): Promise<void> {
  if (finishing) return;                       // 이중 클릭 방지
  setFinishing(true);
  try {
    // 03 §8.1 1단계 — 실패해도 계속한다(timelapseUrl=null은 계약상 합법 — VF-6).
    await getTimelapseService().finish();
  } finally {
    setFinishing(false);
  }
  // 대기 중 홈 복귀·유휴 만료가 일어났을 수 있다 — 그때는 전이하지 않는다.
  if (currentScreen() !== "Result") return;
  const qrOn = isQrEffectivelyEnabled(rawEnableQr, user !== null, false);
  shellStore.getState().go(qrOn ? "Qr" : "Done");
}
```
- 버튼: `disabled={result.composing || result.imageUrl === null || finishing}`,
  `onClick={() => void goNext()}`.
- `finishing`일 때 `<Spinner label={STRINGS.result.timelapseBusy} />` 표시.
- `src/ui/strings.ts`에 `result: { timelapseBusy: "타임랩스를 만드는 중입니다…" }` 추가
  (기존 `STRINGS`에 `result` 섹션 없음 — F21, 검사 테스트도 없음).

### 8.5 `src/domain/index.ts` — 신규 도메인 2개 export (§5.3)

### 8.6 하지 않는 것 (명시적 비목표)

- **로컬 보관·업로드 코드를 만들지 않는다**(Step 10·11). `current()`만 노출한다.
- **진단 모달을 만들지 않는다**(Step 16). `encoderProbe()` getter + `logger.info` 기록까지만.
- **`Guide` 화면의 "타임랩스 미제공" 안내를 만들지 않는다**([12 C3]). 문구가 [03 §5]에 아직
  확정되지 않았다. `lastEncoderProbe()`가 준비돼 있으므로 Step 13/16에서 붙인다.
- **`session.mp4`를 만들지 않는다**([non-goal] 그대로).
- `docs/spec-vectors/`를 건드리지 않는다 → **`dotnet test`를 돌릴 이유가 없다**(§5.1 주석).

---

## 9. 실패·`null` 계약과 로그 지점

| 상황 | 결과 | 로그(전부 메인 스레드) |
|------|------|------------------------|
| 브라우저에 H.264 인코더 없음 | `null` | `warn` "타임랩스 미제공(브라우저 H.264 인코더 없음)" |
| 스풀 0장 | `null` | `warn` "타임랩스 스풀 프레임이 없습니다" |
| 선별 < 30장 | `null` | `warn` "타임랩스를 만들지 않음(선별 프레임 부족)" + `spooled`·`actualSeconds` |
| 인코더 `configure` 거부 | `null` | `warn` "타임랩스 생성 실패" + `reason` |
| 인코딩 중 오류(`error` 콜백) | `null` | 동상 |
| `flush()` 타임아웃(10s) | `null` + `encoder.close()` 강제 | 동상 |
| Worker 무응답(60s) | `null` + `terminate()` | 동상 |
| 스풀 쓰기 실패 | 해당 프레임만 버림, 수집 계속 | 수집 종료 시 `droppedSpool` 집계 1회 |
| OPFS 자체 미지원 | 스풀이 전부 실패 → 0장 → `null` | 부트스트랩이 이미 경고함 |
| 화면 이탈 | `stop()` → 수집 중단 + `abort()` + 결과 폐기 | `info` "타임랩스 수집 종료" |
| 정상 | `TimelapseResult` | `info` "타임랩스 생성" + [05:404]의 전 항목 |

> **로그 키 이름 주의(15 §4 함정 #1)**: `code`·`token`·`state`·`nonce`·`pin`은 `[masked]`가 된다.
> 코덱 문자열은 반드시 **`codecName`** 으로 담는다. `path`·`reason`·`bytes`는 안전하다.

**어떤 경로로도 예외가 촬영·화면 전이를 막지 못한다** — `finish()`는 내부에 `.catch(() => null)`
안전망을 둔다.

---

## 10. 리소스 해제 표 (누수 방지 — 위반 시 iOS 탭이 죽는다)

| 자원 | 획득 | 해제 | 보증 위치 |
|------|------|------|-----------|
| 스풀 `Blob` | 가공 Worker `convertToBlob` | 메인이 OPFS 기록 후 참조 해제(GC) | — |
| `ImageBitmap`(디코드) | `createImageBitmap(blob)` | **`finally { bitmap.close() }`** | `encode.worker` `createFrame`, `mediaRecorderMp4` `pushFrame` |
| `VideoFrame` | `new VideoFrame(canvas, ...)` | **`finally { frame.close() }`** | `webCodecsMp4` 루프 |
| `VideoEncoder` | `createEncoder` | `flush()` 성공·실패 무관하게 `finally`에서 `close()` | `webCodecsMp4` |
| `OffscreenCanvas`(인코딩용) | 1개 생성 후 **재사용** | Worker `terminate()`와 함께 소멸 | `encode.worker` |
| `encode.worker` | 작업마다 spawn | **`finally { terminate() }`** + 60s 타임아웃 + `abort()` | `encodeClient` |
| `MediaStreamTrack`(경로 A) | `canvas.captureStream(0)` | `dispose()`에서 `track.stop()` | `mediaRecorderMp4` `finally` |
| 스풀 구독 | `camera.onTimelapseFrame` | `stopCollection()`에서 unsubscribe | `timelapseService` |
| 스풀 파일 | OPFS `tl/` | 세션 폴더 삭제로 함께 사라짐(`workspace.discard()`) | 기존 셸 훅 |
| 결과 mp4 Blob | 인코딩 | `stop()`에서 참조 해제. `blob:` URL은 **만들지 않는다**(Step 10 소관) | `timelapseService` |

---

## 11. 테스트 전략

**배치**: 새 테스트는 전부 `webclient/tests/unit/encode/`에 둔다(WBS 지정).
환경은 기본 `node`. DOM이 필요한 테스트는 만들지 않는다(전부 포트 주입으로 해결한다).

| 파일 | 대상 | 대표 케이스 |
|------|------|-------------|
| `timelapsePlan.test.ts` | 도메인 순수 | ① 38초 세션 → `speedFactor≈3.04`, `outputSeconds=12.5`, `targetFrames=375` ② **5초 세션(바로 촬영 다용) → `speedFactor=1`, 원속 5초, `null`이 아니다** ③ 12·15·30·60·120초 예시가 [analysis/14 §7.2] 표와 일치 ④ 스풀 부족(선별 40장, target 375) → `frameDurationUs > 33333`이고 **총 길이 = outputSeconds 유지** ⑤ 정상 케이스 `frameDurationUs === 33333`(±1) ⑥ 29장 → `null`, 30장 → 생성 ⑦ `actualSeconds<=0`·`spool 0` → `null` ⑧ `evenlySample` 중복 없음·단조 증가·경계(count===total, count=1) ⑨ `timelapseBitrate` 4구간 경계값 ⑩ `evenDimensions(1443,1080)==={1442,1080}`, 짝수 입력 불변, 1×1 → 2 하한 ⑪ `roundHalfToEven` 사용(`Math.round`와 갈리는 입력으로 고정) |
| `timelapseSpool.test.ts` | 도메인 순수 | ① `shouldSpoolFrame(-Infinity, 0, 66.7) === true`(**초기값 0이면 첫 프레임을 먹는다** — 함정 #4 회귀 고정) ② 간격 미만 false / 정확히 간격이면 true ③ `planDecimation(899)`=null, `900`→ remove 450·kept 450 ④ 삭제 후 남은 이름이 **정렬 순서를 유지**한다 ⑤ `decimatedInterval` |
| `encoderSupport.test.ts` | 판정 | ① B 우선(둘 다 지원 → `webcodecs`) ② B 미지원·A 지원 → `mediarecorder` ③ 둘 다 없음 → `none` ④ **`isConfigSupported`가 `{supported:false}`면 다음 코덱 후보로 넘어간다** ⑤ `isConfigSupported`가 **throw해도 예외가 새지 않는다** ⑥ `VideoEncoder`는 있지만 `Worker`가 없으면 B를 건너뛴다 ⑦ 질의 config에 **짝수 클램프된 width/height와 표 비트레이트**가 들어간다(인자 캡처로 검증) ⑧ `lastEncoderProbe()` 갱신 |
| `webCodecsMp4.test.ts` | 경로 B 코어(가짜 인코더·muxer·프레임) | ① 프레임 수·타임스탬프·duration이 job과 일치 ② **`encodeQueueSize`가 9면 그 프레임을 드롭**하고 8이면 인코딩한다(경계) ③ 드롭돼도 나머지 타임스탬프가 밀리지 않는다 ④ `keyFrame`이 30프레임마다 true, **첫 프레임 true** ⑤ 모든 `VideoFrame`에 `close()`가 불린다(드롭·예외 경로 포함) ⑥ `loadFrame`이 null → skip, 전량 null → `ok:false` ⑦ `error` 콜백 발생 → 루프 중단 + `ok:false` + `encoder.close()` ⑧ `flush()`가 영원히 pending → 타임아웃 후 `ok:false` + `close()` ⑨ `finalize()`가 **`flush()` 뒤에** 불린다(호출 순서 배열로 고정) ⑩ 어떤 경로에서도 **throw하지 않는다** |
| `mediaRecorderMp4.test.ts` | 경로 A 코어(가짜 포트·시계) | ① 프레임 순서대로 `pushFrame` ② 페이싱이 **실경과 기준**이다(`now`가 목표를 넘겼으면 `delay(0)`) ③ `createPort`가 null → `ok:false` ④ `stop()`이 null → `ok:false` ⑤ **성공·실패·예외 모든 경로에서 `dispose()`가 정확히 1회** |
| `timelapseService.test.ts` | 수집 수명 + 오케스트레이션(가짜 camera·workspace·encode) | ① `startCollection` → 스풀 on + 간격 66.67 통지 ② 프레임 도착이 `writeTimelapseFrame(0..n)`을 **0부터 순차**로 부른다 ③ 인플라이트 중 도착 → `droppedSpool++`, 쓰기 호출 없음 ④ 900 도달 → 450 삭제 + 간격 2배가 카메라에 재통지된다 ⑤ `stopCollection`이 실경과를 초로 확정하고 스풀을 off ⑥ **`stopCollection` 후 도착한 프레임은 기록하지 않는다**([trigger] 고정) ⑦ `finish()` 멱등(2회 호출 → `encode` 1회) ⑧ 동시 `finish()` 2건이 같은 Promise에 합류 ⑨ `encode`가 throw해도 `finish()`가 `null`을 돌려주고 던지지 않는다 ⑩ `stop()`이 결과를 폐기하고 `abort()`를 부른다 ⑪ 수집 없이 `finish()` → `null` ⑫ **`size`는 카메라가 아니라 마지막 스풀 프레임에서 온다**(카메라가 꺼진 뒤에도 인코딩 가능) |
| `timelapseEncoder.test.ts` | 오케스트레이터(가짜 detect·client·workspace) | ① 스풀 0 → `null` ② 선별 부족 → `null` ③ `path:"none"` → `null`, **client는 호출되지 않는다** ④ 경로 B 성공 → `TimelapseResult`의 `path/width/height/bytes/frameCount` ⑤ **경로 B 실패 시 경로 A로 재시도하지 않는다**(설계 결정 고정) ⑥ 경로 A 선택 시 Worker client를 부르지 않는다 ⑦ `job.names`가 **선별 인덱스 순서대로 매핑**된다 ⑧ `job.config.width/height`가 짝수다 ⑨ 어떤 경우에도 throw하지 않는다 |

**정적 불변식 테스트(15 §3.4 관례 — 신규 1건)**
`timelapseService.test.ts`에 붙인다:
- `src/adapters/encode/**` 중 **`encode.worker.ts`만** `mp4-muxer`를 import한다(소스 정규식 검사).
  → 코어를 node 테스트 가능 상태로 **기계적으로 고정**한다.
- `src/adapters/encode/webCodecsMp4.ts` 소스에 `logStore`·`logger.` 문자열이 **0건**이다
  (F9 — Worker에서 남긴 로그는 진단에 도달하지 않는다).

**원천적으로 자동화 불가 → "미검증(사용자 액션 V18)"으로 남기는 것** (추정 통과 금지):
1. 생성된 mp4가 실제로 **재생되고 `moov`가 정상**인가(`<video>` 재생 · `ffprobe`).
2. 코덱이 `h264`, **오디오 트랙 0개**, 길이 10~15초(6컷 ~38초 세션).
3. **[바로 촬영]로 ~5초로 줄인 세션이 원속 ~5초 mp4**를 만든다(`null` 아님).
4. 모바일(iOS Safari)·데스크톱 양쪽 재생.
5. 인코딩 소요 ≤6초(A4), 프리뷰 ≥24fps 유지(A5), `droppedSpool` 수치(A7).
6. 인코더 미지원 브라우저(Firefox 등)에서 **촬영 완주 + 타임랩스만 없음**.
7. 경로 A 실제 동작(A6) — 지원 매트릭스상 도달하지 않으면 "미도달"로 기록.

---

## 12. 완료 기준 매핑 (WBS Step 9 → 구현)

| WBS 기준 | 어떻게 만족하는가 | 검증 |
|----------|-------------------|------|
| [관측] 6컷 ~38초 세션 → 10~15초 mp4 | `computeSpeedFactor(38)=3.04` → `outputSeconds=12.5` → 375프레임 · 33333μs | 계획: `timelapsePlan.test.ts` ①. 실제 재생: **V18** |
| [관측] 컨테이너 정상 종료 | `flush()` → `finalize()` 순서 고정 + `fastStart:"in-memory"` | 순서: `webCodecsMp4.test.ts` ⑨. 실물: **V18** |
| [관측] **[바로 촬영] ~5초 세션도 원속 생성**(`null` 아님) | 실경과로 배속 산출(`N=1`) + 스풀 방식(고정 stride 아님) → 15fps×5s≈75장 ≥ 30 | `timelapsePlan.test.ts` ② + `timelapseService.test.ts` ⑤ |
| [관측] 오디오 트랙 없음 | 오디오 트랙을 **만들지 않는다**. muxer에 `audio` 미지정, `getUserMedia`도 `audio:false`(기존) | 코드 부재 + **V18**(`ffprobe`) |
| [관측] 진단에 선택된 경로 표시 | `logger.info("타임랩스 인코더 경로 판정", {path, codecName, reason})` + `encoderProbe()` getter | `encoderSupport.test.ts` ⑧. 모달 렌더는 Step 16 |
| [non-goal] `session.mp4` 없음 | 세션 녹화 코드가 존재하지 않는다 | 코드 부재 |
| [non-goal] 미지원 브라우저에서 촬영 완주, 타임랩스만 `null` | 판정 `none` → `finish()`가 `null`, `goNext()`는 계속 진행 | `timelapseEncoder.test.ts` ③ + **V18** |
| [non-goal] 인코딩 실패가 촬영을 중단시키지 않음 | 수집은 촬영과 분리(인코딩은 종료 후) + `finish()`에 `.catch(() => null)` | `timelapseService.test.ts` ⑨ |
| [trigger] 수집은 시퀀스 시작~종료 사이에만 | `startCollection`/`stopCollection`이 스풀 채널을 on/off. off 후 프레임은 무시 | `timelapseService.test.ts` ①⑤⑥ |
| [trigger] `stride` 프레임마다만 인코딩 | `shouldSpoolFrame(last, now, intervalMs)` — 시간 기반 stride(가변 fps에 강함), 상한 도달 시 2배 | `timelapseSpool.test.ts` ①②③ |

---

## 13. 구현 단계 (WBS 블루프린트 — `docs/templates/WBS_BLUEPRINT.md` 형식)

> 공통 검증 게이트(모든 단계 끝에서):
> ```powershell
> cd E:\Study\photobooth\webclient
> npx tsc --noEmit
> npx vitest run
> ```
> **테스트 수는 530에서 늘어나야 한다**(줄면 회귀). `dotnet test`는 이번 작업에서 불필요하다
> (`docs/spec-vectors/`를 건드리지 않는다 — §5.1).
> **`git commit`/`git push` 금지.**

### Step 9-1: `mp4-muxer` 도입 + `THIRD-PARTY.md` 신설
- **Context Brief**: WebCodecs `VideoEncoder`는 raw H.264 chunk만 내므로 MP4 컨테이너로 감쌀
  순수 JS muxer가 필요하다. 상용 배포 요구로 라이선스를 문서화해야 한다([01 §7]).
  `webclient/THIRD-PARTY.md`는 아직 없다.
- **대상 파일**: `webclient/package.json`, `webclient/package-lock.json`, `webclient/THIRD-PARTY.md`(신규)
- **선행 조건**: 없음
- **구현 내용**: `npm install --save-exact mp4-muxer@5.2.2` (캐럿·틸드 금지).
  `THIRD-PARTY.md`를 §4.4의 내용 그대로 생성(react/react-dom/zustand/mp4-muxer 4행 + deprecated 사유).
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\webclient
  Select-String -Path package.json -Pattern '"mp4-muxer": "5\.2\.2"'
  Test-Path THIRD-PARTY.md
  npx tsc --noEmit
  ```
- **완료 기준**:
  - [관측] `package.json` dependencies에 정확히 `"mp4-muxer": "5.2.2"`(범위 지정자 없음)가 있고
    `package-lock.json`이 갱신됐으며, `THIRD-PARTY.md`에 mp4-muxer MIT 행이 있다.
  - [non-goal] 기존 3개 의존(react/react-dom/zustand)의 버전이 바뀌지 않는다. `devDependencies` 무변경.
  - [trigger] 설치는 `--save-exact`로만 — `npm i mp4-muxer`(캐럿 부착)를 쓰지 않는다.
- **롤백**: `npm uninstall mp4-muxer` + `THIRD-PARTY.md` 삭제
- [ ] 완료

### Step 9-2: 도메인 순수 함수 2파일
- **Context Brief**: 배속·선별·타임스탬프·비트레이트·짝수 클램프·스풀 간격·솎아내기는 전부
  브라우저 없이 계산되는 규격이다. `src/domain`에 두면 `purity.test.ts`가 자동으로 순수성을
  강제하고 node에서 전량 검증된다. 배속 함수 `computeSpeedFactor`는 **이미 있다**
  (`src/domain/capture/timelapseSpeed.ts`) — 다시 만들지 말고 소비한다.
- **대상 파일**: `src/domain/capture/timelapsePlan.ts`(신규), `src/domain/capture/timelapseSpool.ts`(신규),
  `src/domain/index.ts`, `tests/unit/encode/timelapsePlan.test.ts`(신규), `tests/unit/encode/timelapseSpool.test.ts`(신규)
- **선행 조건**: 없음
- **구현 내용**: §5.1·§5.2의 시그니처와 본문 규격 그대로. 반올림은 **`roundHalfToEven`**(`Math.round` 금지).
  `evenlySample`만 `Math.floor`(사유는 §5.1 주석에 남긴다). 테스트는 §11 표의 케이스 전부.
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\webclient
  npx vitest run tests/unit/encode tests/unit/domain/purity.test.ts
  npx tsc --noEmit
  ```
- **완료 기준**:
  - [관측] `purity.test.ts`가 신규 2파일을 포함한 채 전량 통과하고, `timelapsePlan.test.ts`의
    38초→375프레임/12.5초와 5초→원속 케이스가 통과한다.
  - [non-goal] `timelapseSpeed.ts`를 수정하지 않는다. `docs/spec-vectors/` 무변경
    (Windows 대응 함수가 없어 교차 고정 대상이 아니다).
  - [trigger] 두 파일 모두 도메인 밖 import 0건 — `purity.test.ts`가 강제한다.
- **롤백**: 두 파일 + `index.ts` 2줄 삭제
- [ ] 완료

### Step 9-3: 프로토콜 + 경로 판정
- **Context Brief**: 인코더 경로는 **런타임 감지로만** 정한다. TS DOM lib은 없는 API도 있다고
  선언하므로(함정 #2) `typeof` 확인과 `await isConfigSupported()`가 유일한 근거다.
  순서는 **WebCodecs → MediaRecorder → none**이 계약이다([04 §7.3]).
- **대상 파일**: `src/adapters/encode/encodeProtocol.ts`(신규), `src/adapters/encode/encoderSupport.ts`(신규),
  `tests/unit/encode/encoderSupport.test.ts`(신규)
- **선행 조건**: Step 9-2(`timelapseBitrate`·`evenDimensions`·`TIMELAPSE_CODEC_CANDIDATES`)
- **구현 내용**: §6.1·§6.2 그대로. `isConfigSupported`가 **throw해도 삼킨다**.
  `MediaRecorder`가 전역에 없는 환경(Worker·node)에서도 안전해야 한다.
- **검증 명령**: `npx vitest run tests/unit/encode/encoderSupport.test.ts` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 가짜 전역으로 B/A/none 세 경로가 모두 재현되고, 코덱 후보 3개를 순서대로 질의하며,
    질의 config의 width/height가 짝수·bitrate가 [04 §7.4] 표값이다.
  - [non-goal] UA·버전 문자열 분기가 소스에 **0건**이다. 판정 실패가 예외로 새지 않는다.
  - [trigger] 경로 B는 `VideoEncoder` **그리고** `Worker`가 둘 다 있을 때만 선택된다.
- **롤백**: 두 파일 + 테스트 삭제
- [ ] 완료

### Step 9-4: 경로 B 코어(`webCodecsMp4.ts`) — 전부 주입, node 검증
- **Context Brief**: 15 §3.1의 "순수 코어 + 얇은 래퍼"를 인코딩에 적용한다. 이 파일은
  브라우저 API도 `mp4-muxer`도 import하지 않고 **전부 포트로 받는다**. 그래야 타임스탬프·
  백프레셔·리소스 해제·실패 경로를 node에서 검증할 수 있다.
- **대상 파일**: `src/adapters/encode/webCodecsMp4.ts`(신규), `tests/unit/encode/webCodecsMp4.test.ts`(신규)
- **선행 조건**: Step 9-2, 9-3
- **구현 내용**: §6.3의 시그니처·본문 규격 그대로. 백프레셔는
  `encodeQueueSize > TIMELAPSE_ENCODE_QUEUE_LIMIT` → 드롭. 모든 프레임 `finally { close() }`.
  `flush()` 타임아웃 후 강제 `close()`. **`logger`를 import하지 않는다**(Worker에서 도달 불가 — F9).
- **검증 명령**: `npx vitest run tests/unit/encode/webCodecsMp4.test.ts` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] §11의 ①~⑩ 케이스가 통과한다. 특히 큐 9→드롭·8→인코딩 경계와,
    드롭 후에도 남은 타임스탬프가 밀리지 않는 것, `flush()`→`finalize()` 순서.
  - [non-goal] 이 파일에 `mp4-muxer`·`logger`·`performance`·`document` 문자열이 **0건**이다.
    어떤 입력에도 예외를 던지지 않는다.
  - [trigger] 인코딩은 `createFrame`이 프레임을 만든 경우에만 — null이면 skip 카운트만 오른다.
- **롤백**: 파일 + 테스트 삭제
- [ ] 완료

### Step 9-5: 인코딩 Worker + RPC 클라이언트
- **Context Brief**: 375프레임 JPEG 디코드를 메인에서 하면 결과 화면이 수 초간 얼어붙는다
  ([04 §10] "타임랩스 인코딩 = Worker"). Worker는 OPFS를 **읽기만** 한다(쓰기는 `opfsWriter`
  전용 — 메인/타 Worker에서 쓰면 iOS에서 전 저장이 실패한다). Worker는 **로그를 남기지 않는다**.
- **대상 파일**: `src/adapters/encode/encode.worker.ts`(신규), `src/adapters/encode/encodeClient.ts`(신규),
  `src/adapters/storage/sessionWorkspace.ts`(수정 — `timelapseDirPath` export)
- **선행 조건**: Step 9-1, 9-4
- **구현 내용**: §6.5·§6.6·§8.2 그대로. Worker는 **작업당 1회성**이며 `finally`에서 `terminate()`.
  `createMuxer`에 **`frameRate`를 넘기지 않는다**(F17 — 타임스탬프가 격자로 반올림돼 길이가 깨진다).
  완료 시 `ArrayBuffer`를 **transfer**로 넘긴다.
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\webclient
  npx tsc --noEmit
  npx vite build          # A1 검증: Worker 번들에 mp4-muxer가 들어가는지
  npx vitest run
  ```
- **완료 기준**:
  - [관측] `npx vite build`가 성공하고 `web/kiosk/assets/`에 encode worker 청크가 생성된다
    (`Get-ChildItem ..\web\kiosk\assets -Filter *.js | Select-String -Pattern "moov" -List` 로
    muxer 코드가 번들에 들어갔음을 확인. 문자열이 잡히지 않으면 청크 파일 목록에 encode
    worker 이름이 있는지로 대체 확인한다 — 빌드 실패가 아니면 통과).
  - [non-goal] `encode.worker.ts`에 OPFS **쓰기**(`createWritable`·`createSyncAccessHandle`) 호출이
    0건이다. `logger` import 0건.
  - [trigger] Worker는 `encodeClient.run()` 호출 시에만 생성되고, 완료·실패·`abort()` 어느
    경로로도 `terminate()`된다.
- **롤백**: 두 파일 삭제 + `sessionWorkspace.ts`의 export 되돌리기
- [ ] 완료

### Step 9-6: 경로 A(`mediaRecorderMp4.ts`) — 메인 스레드 예비 경로
- **Context Brief**: `MediaRecorder`·`captureStream`은 Window 전용이라 Worker에 없다([04 §7.3a]).
  경로 A는 스풀 프레임을 화면 밖 캔버스에 **실시간으로 재생하며 녹화**하므로 출력 길이만큼
  실제 시간이 걸린다(≤15초). 지원 매트릭스상 도달하지 않는 예비 경로지만([04 §7.3] 이유 ③)
  WBS 대상 파일이므로 구현한다.
- **대상 파일**: `src/adapters/encode/mediaRecorderMp4.ts`(신규), `tests/unit/encode/mediaRecorderMp4.test.ts`(신규)
- **선행 조건**: Step 9-3
- **구현 내용**: §6.4 그대로. 포트(`CanvasRecorderPort`)로 캔버스·레코더를 감싸 코어를 node에서
  검증한다. 페이싱은 **실경과 기준**(tick 누적 금지 — WM3). `dispose()`는 `finally`에서 1회.
- **검증 명령**: `npx vitest run tests/unit/encode/mediaRecorderMp4.test.ts` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 가짜 포트·가짜 시계로 프레임 순서·실경과 페이싱·타임아웃·`dispose` 1회가 검증된다.
  - [non-goal] 브라우저 구현부(`createCanvasRecorderPort`)에서 예외가 새지 않는다(전부 null 반환).
    `document`에 캔버스를 **append하지 않는다**.
  - [trigger] 프레임 전진은 `pushFrame` 성공 시에만 — 실패하면 skip 카운트만 오르고 페이싱은 유지된다.
- **롤백**: 파일 + 테스트 삭제
- [ ] 완료

### Step 9-7: 가공 Worker 스풀 채널 (**설계 이탈 — 사유 필수 기록**)
- **Context Brief**: 스풀 프레임을 기존 `captureStill()`로 뽑으면 안 된다. 가공 Worker의
  스틸 슬롯은 **1개짜리 덮어쓰기**여서(`frameProcessor.worker.ts:157-160`) 컷 촬영 요청과
  충돌하면 컷이 사라지고 5초 타임아웃 뒤 `null`이 되어 **세션이 홈으로 강제 복귀한다**
  (`useCaptureRunner.ts:155-161`). 전용 스풀 채널을 신설해 두 소비자를 분리한다.
  `FrameProcessor` 인터페이스를 넓히므로 **기존 테스트의 `FakeProcessor`도 함께 고쳐야
  `tsc --noEmit`이 통과한다**(tsconfig `include`에 `tests` 포함).
- **대상 파일**: `src/adapters/camera/frameProcessorProtocol.ts`, `src/adapters/camera/frameProcessor.worker.ts`,
  `src/adapters/camera/frameProcessorClient.ts`, `src/adapters/camera/cameraTypes.ts`,
  `src/adapters/camera/cameraService.ts`, `tests/unit/camera/cameraService.test.ts`
- **선행 조건**: Step 9-2(`shouldSpoolFrame`)
- **구현 내용**: §8.1 표 그대로. Worker의 스풀 분기는 **스틸 분기 뒤**에 둔다(컷이 항상 우선).
  `lastSpoolAtMs` 초기값은 **`-Infinity`**(0을 쓰면 첫 프레임을 먹는다 — 함정 #4).
  `configureSpool({enabled:false})`는 `lastSpoolAtMs`를 리셋한다.
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\webclient
  npx tsc --noEmit
  npx vitest run tests/unit/camera
  ```
- **완료 기준**:
  - [관측] `tsc --noEmit` 통과(= `FakeProcessor`가 새 멤버를 구현했다)하고 기존
    `cameraService.test.ts` 케이스가 **하나도 줄지 않고** 통과한다.
  - [non-goal] `requestStill` 경로의 동작·타임아웃·품질(0.95)이 바뀌지 않는다.
    스풀이 off일 때 `convertToBlob` 추가 호출이 **0건**이다.
  - [trigger] 스풀 JPEG 생성은 `configureSpool({enabled:true})` 이후, 그리고
    `shouldSpoolFrame`이 true인 프레임에서만 일어난다.
- **롤백**: 6개 파일의 스풀 관련 추가분만 되돌린다(다른 Step과 독립)
- [ ] 완료

### Step 9-8: 오케스트레이터 + 서비스 + 화면 배선
- **Context Brief**: 수집은 `Capture` 진입 시 시작해 마지막 컷에서 끝나고, 인코딩은
  `Result` [다음] **1단계**에서 일어난다([03 §8.1]). 셸의 `stopEncoder` 훅은 Step 4에서
  이미 예약돼 있다. `returnHome`이 `cancelCaptureSequence → cleanupWorkspace(폴더 삭제) →
  stopEncoder` 순이므로 **수집 중단을 `cancelCaptureSequence`에서도** 해야 삭제 후 되살아나는
  잔재가 없다.
- **대상 파일**: `src/adapters/encode/timelapseEncoder.ts`(신규), `src/adapters/encode/timelapseService.ts`(신규),
  `src/screens/capture/useCaptureRunner.ts`, `src/ui/views/FlowViews.tsx`, `src/ui/strings.ts`,
  `tests/unit/encode/timelapseEncoder.test.ts`(신규), `tests/unit/encode/timelapseService.test.ts`(신규)
- **선행 조건**: Step 9-3 ~ 9-7 전부
- **구현 내용**: §6.7·§6.8·§8.3·§8.4 그대로. 로그는 **여기서만** 남기고 키 이름은
  `codecName`을 쓴다(`code`는 마스킹된다 — 함정 #1). 정적 불변식 테스트 2건(§11) 포함.
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth\webclient
  npx tsc --noEmit
  npx vitest run
  ```
- **완료 기준**:
  - [관측] `npx vitest run` 총 테스트 수가 **530보다 크고** 전부 통과한다. §11의
    `timelapseService`·`timelapseEncoder` 케이스가 모두 녹색이며, 정적 불변식 2건
    (`mp4-muxer` import는 worker 1파일만 · 코어에 logger 0건)이 통과한다.
  - [non-goal] 타임랩스가 `null`이어도 `goNext()`가 **정상적으로 `Qr`/`Done`으로 전이**한다.
    `sessionStore`에 타임랩스 관련 필드를 추가하지 않는다. 로컬 보관·업로드 코드를 만들지 않는다.
  - [trigger] 수집은 `startCollection()`~`stopCollection()` 사이에만. 인코딩은 **[다음] 클릭 시에만**
    (`Result` 진입만으로는 인코딩이 시작되지 않는다). 대기 중 홈 복귀가 일어나면 전이하지 않는다.
- **롤백**: `src/adapters/encode/` 삭제 + `useCaptureRunner`·`FlowViews`·`strings`의 추가분 되돌리기
  (타임랩스 미제공 상태로 정상 동작)
- [ ] 완료

### Step 9-9: 문서 반영 + 사용자 액션 등재
- **Context Brief**: Step을 끝내면 [11 §Step 9] 체크박스에 산출물·검증 수치·**설계 이탈**·
  남은 실측을 적는 것이 이 저장소의 관례다(15 §5). 브라우저 실행이 필요한 항목은
  추정으로 통과 처리하지 않고 [14]에 사용자 액션으로 등재한다.
- **대상 파일**: `docs/web-client/11-wbs.md`, `docs/web-client/14-handoff-and-user-actions.md`,
  `docs/web-client/15-implementation-conventions.md`(§7 상태 표 수치)
- **선행 조건**: Step 9-8
- **구현 내용**: Step 9 체크박스를 `- [x] 완료`로 바꾸고 다른 Step과 **같은 형식**으로
  ① 산출물 파일 목록 ② 검증 수치(테스트 파일 수/테스트 수) ③ **설계 이탈 3건**
  (가공 Worker 스풀 채널 신설 / `ResultView.goNext` [다음] 1단계 배선 / `timestampsUs`를
  `i*33333` 대신 `outputSeconds/count`로 산출) ④ 미검증 항목을 적는다.
  [14]에 **V18(타임랩스 실기기 실측)** 항목을 §11의 7개 체크리스트로 신설한다.
  [15] §7 "지금 상태 요약"의 웹 테스트 수를 실제 값으로 갱신한다.
- **검증 명령**:
  ```powershell
  cd E:\Study\photobooth
  Select-String -Path docs\web-client\11-wbs.md -Pattern "Step 9" -Context 0,40
  Select-String -Path docs\web-client\14-handoff-and-user-actions.md -Pattern "V18"
  ```
- **완료 기준**:
  - [관측] `11-wbs.md`의 Step 9 절에 체크 표시와 산출물·검증 수치·설계 이탈·미검증이 있고,
    `14`에 V18 절차가 있다.
  - [non-goal] 다른 Step의 체크박스·본문을 수정하지 않는다. `docs/analysis/*`(플랫폼 중립 규격)
    무변경 — 이번 작업은 규격을 바꾸지 않았다.
  - [trigger] 문서 갱신은 Step 9-8의 검증이 **실제로 통과한 뒤에만** — 예상 수치를 적지 않는다.
- **롤백**: 문서 3개 변경분 되돌리기
- [ ] 완료

---

## 14. 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F21) / 미검증 가정(A1~A7) 목록이 분리되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다 (A1→9-5, A2~A7→9-9의 V18 등재)
- [x] 모든 단계에 7개 필수 필드가 채워져 있다 (9-1 ~ 9-9, 총 9단계 — 3~12 범위)
- [x] 모든 완료 기준이 관측 기반 3문 형식이다 (UI를 건드리는 9-8은 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능한 형태다 (`npx tsc --noEmit` / `npx vitest run` / `npx vite build` / `Select-String`)

### 리뷰어가 특히 볼 지점

1. **`i * 33333μs` vs `outputSeconds/count`** — §5.1의 박스. WBS 문구와 [04 §7.2] 규격이
   갈리는 지점이고, 스풀 부족 시 전자를 쓰면 출력이 짧아진다.
2. **`mp4-muxer`에 `frameRate`를 넘기지 않는 것** — F17. 넘기면 위 1번이 무의미해진다.
3. **가공 Worker 스풀 채널 신설(§8.1)** — WBS 대상 파일 밖이다. 사유(F4·F5)가 타당한지.
4. **경로 B 실패 시 A로 재시도하지 않는 결정**(§6.7) — 의도된 단순화다.
5. **Worker에서 `logger`를 쓰지 않는 규칙**(F9) — 정적 테스트로 고정했다.
6. **`cancelCaptureSequence`에서도 수집을 멈추는 것**(§8.3) — `returnHome` 순서 때문이다.
