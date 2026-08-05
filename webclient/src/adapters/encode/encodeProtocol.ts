/**
 * 타임랩스 인코딩 Worker 메시지 프로토콜 — 04 §7.3
 *
 * 순수 타입·상수만 둔다. 메인·Worker 양쪽이 함께 import하므로 여기에 부수효과가 있으면
 * Worker 번들에 메인 코드가 딸려 들어간다.
 */

export type EncoderPath = "webcodecs" | "mediarecorder" | "none";

/** 실제로 인코더에 넘기는 설정. `width`/`height`는 **짝수 클램프 후** 값이다. */
export interface TimelapseEncodeConfig {
  /** `"avc1.42001E"` 등 — 경로 판정에서 확정된 값. */
  readonly codec: string;
  readonly width: number;
  readonly height: number;
  readonly bitrate: number;
  /** 출력 컨테이너 타임라인 fps(30). */
  readonly framerate: number;
}

/** Worker에 넘기는 인코딩 지시. 선별은 이미 끝나 있다(도메인이 계산했다). */
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
  /** 디코딩·로드 실패로 건너뛴 프레임 수. */
  readonly skippedFrames: number;
  readonly elapsedMs: number;
}

export type EncodeRequest = {
  readonly type: "encode";
  readonly id: number;
  readonly job: EncodeJob;
};

export type EncodeResponse =
  | {
      readonly type: "done";
      readonly id: number;
      readonly buffer: ArrayBuffer;
      readonly stats: EncodeStats;
    }
  | {
      readonly type: "failed";
      readonly id: number;
      /** 실패 사유. **Worker는 로그를 남기지 않으므로**(진단에 도달하지 않는다) 메인이 이것을 기록한다. */
      readonly reason: string;
      readonly stats: EncodeStats;
    };

/** Worker 응답 하드 타임아웃. 04 §8 예산(375프레임 ≤6s)의 10배 여유. */
export const ENCODE_WORKER_TIMEOUT_MS = 60_000;
/** `encoder.flush()` 타임아웃 — 정지 실패는 강제 종료한다(04 §7.5). */
export const ENCODE_FLUSH_TIMEOUT_MS = 10_000;
