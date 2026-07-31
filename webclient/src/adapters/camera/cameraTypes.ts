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

/**
 * `<video>` + 프레임 도착 루프. 브라우저 전용이라 인터페이스로 분리해
 * `cameraService`를 노드 환경에서 테스트할 수 있게 한다.
 */
export interface FrameSource {
  /** 스트림을 붙이고 재생을 시작한다. 실패 시 `false`(예외 전파 금지 — 01 §2.1). */
  attach(stream: MediaStream): Promise<boolean>;
  /**
   * 프레임 도착 구독. 중복 프레임(`mediaTime` 동일)은 소스가 **걸러서** 넘긴다.
   * @returns 구독 해제 함수
   */
  onFrame(listener: (payload: FramePayload) => void): () => void;
  detach(): void;
  /** 원본 해상도(video 메타데이터). */
  size(): ProcessedSize;
}

/** Worker 가공기. */
export interface FrameProcessor {
  configure(options: { targetAspect: number; mirror: boolean }): void;
  /** 프레임 1개 가공 요청. 이전 가공이 진행 중이면 **최신 것으로 덮어쓴다**(큐를 쌓지 않는다). */
  process(payload: FramePayload): void;
  /** 가공 완료 통지 구독. */
  onProcessed(listener: (size: ProcessedSize) => void): () => void;
  /** 다음 가공 프레임에서 스틸을 만든다(원자성 — 04 §5.1). */
  requestStill(quality: number): Promise<Blob | null>;
  /** 프리뷰 캔버스 제어권을 넘긴다(zero-copy 경로). 미지원이면 무시된다. */
  bindPreview(canvas: OffscreenCanvas): void;
  terminate(): void;
}

/** 카메라 상태 변화 구독자. */
export type CameraStateListener = (state: CameraState, detail?: string) => void;
