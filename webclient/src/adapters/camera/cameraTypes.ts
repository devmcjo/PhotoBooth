/**
 * 카메라 파이프라인 공용 타입 — 04 §2
 *
 * 구조(04 §1): `<video>` → **Worker에서 프레임당 1회 가공**(거울 → 중앙 크롭) →
 * 프리뷰·스틸·타임랩스 3소비자가 **같은 결과를 공유**한다.
 * 이 공유가 WYSIWYG(WM1)의 근거다 — 소비자마다 따로 가공하면 반드시 어긋난다.
 */

/** 카메라 상태. 무한 로딩을 만들지 않기 위해 `Failed`가 명시적으로 존재한다. */
export type CameraState = "Idle" | "Starting" | "Ready" | "Failed";

/** 실제로 획득한 값. 요청값과 다를 수 있어 **진단 화면에 그대로 표시**한다(WC2). */
export interface CameraSettings {
  readonly deviceId: string | null;
  readonly label: string | null;
  /** 카메라 원본 해상도(크롭 전). */
  readonly width: number;
  readonly height: number;
  /** 트랙이 보고한 fps. 보고하지 않으면 null. */
  readonly frameRate: number | null;
}

/** 가공 결과 크기(크롭 후). 프리뷰·스틸이 쓰는 실제 픽셀 크기다. */
export interface ProcessedSize {
  readonly width: number;
  readonly height: number;
}

export interface CameraStartOptions {
  /** 저장된 장치. 없으면 `facingMode`로 요청한다. */
  readonly deviceId?: string | null;
  readonly facing?: "user" | "environment";
  /** 대표 슬롯의 종횡비(가로/세로). 0 이하면 크롭하지 않는다. */
  readonly targetAspect: number;
  readonly mirror: boolean;
}

/** 프레임 1개를 Worker로 넘기는 전달체. */
export type FramePayload = ImageBitmap | VideoFrame;

/** 타임랩스 스풀 프레임 1장(가공 결과 JPEG). 픽셀은 프리뷰·스틸과 같은 것이다. */
export interface SpoolFrame {
  readonly blob: Blob;
  readonly width: number;
  readonly height: number;
}

/** 스풀 채널 설정(04 §7.2). `enabled:false`면 Worker가 JPEG를 만들지 않는다. */
export interface SpoolOptions {
  readonly enabled: boolean;
  readonly intervalMs: number;
  readonly quality: number;
}

/**
 * `attach()` 결과 — 성패**와 이유**(2026-08-07 신설).
 *
 * 전에는 `boolean`이라 `video.play()`가 왜 reject됐는지(`err.name`)가 통째로 버려졌고,
 * 그래서 스트림 획득 성공 후의 재생 실패가 권한 실패와 같은 `unknown`으로 보고됐다.
 *
 * ⚠️ **호출측은 반드시 `result.ok`를 본다.** 이 타입은 객체라 `if (!result)`는 항상 거짓이고
 *    `tsc`가 그 실수를 잡지 못한다 — 재생 실패가 조용히 성공으로 처리된다.
 */
export type FrameSourceAttachResult =
  | { readonly ok: true }
  | { readonly ok: false; readonly errorName: string };

/**
 * 프레임을 Worker로 넘기는 실제 경로 — 진단 [프레임 전달] 행의 값(04 §2.3.2).
 *
 * - `videoFrame` — WebCodecs `VideoFrame` zero-copy(권장 경로)
 * - `imageBitmap` — `VideoFrame`이 **애초에 없거나 실증 프로브가 실패**해 처음부터 폴백(정상)
 * - `imageBitmapDemoted` — `VideoFrame`이 있었는데 **런타임에 깨져 강등**됐다(브라우저 결함
 *   신호 · 성능 예산 재측정 대상). 진단 tone이 `imageBitmap`과 갈리는 이유다 — 합치면
 *   "폴백 기기"와 "고장난 기기"를 현장에서 구분할 수 없다.
 */
export type FrameTransferMode = "videoFrame" | "imageBitmap" | "imageBitmapDemoted";

/**
 * `<video>` + 프레임 도착 루프. 브라우저 전용이라 인터페이스로 분리해
 * `cameraService`를 노드 환경에서 테스트할 수 있게 한다.
 */
export interface FrameSource {
  /**
   * 스트림을 붙이고 재생을 시작한다. 실패해도 **예외를 던지지 않는다**(01 §2.1) —
   * `{ok:false, errorName}`으로 이유를 돌려준다.
   */
  attach(stream: MediaStream): Promise<FrameSourceAttachResult>;
  /**
   * 프레임 도착 구독. 중복 프레임(`mediaTime` 동일)은 소스가 **걸러서** 넘긴다.
   * @returns 구독 해제 함수
   */
  onFrame(listener: (payload: FramePayload) => void): () => void;
  detach(): void;
  /** 원본 해상도(video 메타데이터). */
  size(): ProcessedSize;
  /** 현재 프레임 전달 경로(진단 표시). */
  transferMode(): FrameTransferMode;
}

/**
 * 가공 경로 — 04 §2.3.1.
 *
 * `OffscreenCanvas`가 없는 브라우저에서도 촬영이 되어야 하므로 **두 구현이 같은 인터페이스를
 * 만족**한다. 어느 것이 쓰였는지는 진단에 표시된다("저성능 모드").
 */
export type FrameProcessorMode = "worker" | "main";

/**
 * 프리뷰 연결 방식. 진단 표시용이며, **`none`이 곧 "화면이 검다"** 는 뜻이다.
 *
 * - `transferred` — `transferControlToOffscreen()` 성공(권장 경로 · zero-copy)
 * - `bitmap` — 이관이 불가해 Worker가 프레임마다 비트맵을 보내고 메인이 그린다(폴백)
 * - `direct` — 메인 스레드 가공기가 캔버스에 직접 그린다
 * - `none` — 아직 연결되지 않았다
 */
export type PreviewMode = "none" | "transferred" | "bitmap" | "direct";

/** 프레임 가공기(Worker 또는 메인 스레드). */
export interface FrameProcessor {
  /** 이 구현이 어느 경로인가(진단 표시). */
  readonly mode: FrameProcessorMode;
  configure(options: { targetAspect: number; mirror: boolean }): void;
  /** 프레임 1개 가공 요청. 이전 가공이 진행 중이면 **최신 것으로 덮어쓴다**(큐를 쌓지 않는다). */
  process(payload: FramePayload): void;
  /** 가공 완료 통지 구독. */
  onProcessed(listener: (size: ProcessedSize) => void): () => void;
  /** 다음 가공 프레임에서 스틸을 만든다(원자성 — 04 §5.1). */
  requestStill(quality: number): Promise<Blob | null>;
  /**
   * 화면 캔버스를 프리뷰 대상으로 붙인다.
   *
   * ⚠️ **`OffscreenCanvas`를 받지 않는다**(2026-08-06 변경). 이관 가능 여부는 구현이 판단해야
   *    한다 — 전에는 `cameraService`가 `transferControlToOffscreen()`을 직접 불렀고, 그것이
   *    실패하면 **폴백 없이 검은 화면**이 됐다. 지금은 실패를 구현 안에서 비트맵 경로로 흡수한다.
   * @returns 프리뷰가 실제로 연결됐는가. `false`면 화면에 아무것도 그려지지 않는다.
   */
  bindPreview(canvas: HTMLCanvasElement): boolean;
  /** 현재 프리뷰 연결 방식(진단 표시). */
  previewMode(): PreviewMode;
  /**
   * 타임랩스 스풀 채널 on/off(04 §7.2).
   * **스틸 채널과 분리돼 있다** — 스풀이 컷 촬영 요청을 덮어써 컷을 잃는 사고를 막는다.
   */
  configureSpool(options: SpoolOptions): void;
  /** 스풀 프레임 도착 구독. */
  onSpoolFrame(listener: (frame: SpoolFrame) => void): () => void;
  terminate(): void;
}

/** 카메라 상태 변화 구독자. */
export type CameraStateListener = (state: CameraState, detail?: string) => void;
