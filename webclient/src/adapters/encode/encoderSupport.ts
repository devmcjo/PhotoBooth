import {
  evenDimensions,
  timelapseBitrate,
  TIMELAPSE_CODEC_CANDIDATES,
  TIMELAPSE_OUTPUT_FPS,
} from "@domain/capture/timelapsePlan";
import type { EncoderPath } from "./encodeProtocol";

/**
 * 타임랩스 인코더 경로 판정 — 04 §7.3
 *
 * **판정 순서가 계약이다: B(WebCodecs) → A(MediaRecorder) → C(none).**
 *
 * ⚠️ **버전 문자열·UA로 판정하지 않는다.** TS DOM lib은 브라우저에 없는 API도 있다고
 *    선언하므로(15 §4 함정 #2) `typeof` 확인과 `await isConfigSupported()`만이 근거다.
 * ⚠️ `isConfigSupported`는 **비동기**이며, 지원 여부는 config(해상도·비트레이트)에 따라
 *    달라진다 → **실제로 쓸 config 그대로** 질의한다.
 * ⚠️ 경로 A·C로 떨어지는 것은 실패가 아니다. 타임랩스 미제공은 계약상 합법이다(VF-6).
 */

export interface EncoderProbe {
  readonly path: EncoderPath;
  /** 경로 B에서 채택된 코덱 문자열. A·C면 null. */
  readonly codec: string | null;
  /** 진단·로그용 판정 사유. */
  readonly reason: string;
  /** 후보별 질의 결과(진단 모달 — Step 16이 읽는다). */
  readonly probed: readonly { readonly codec: string; readonly supported: boolean }[];
}

export interface EncoderProbeDeps {
  /** 기본값 `globalThis.VideoEncoder`. 없는 환경에서는 undefined다. */
  readonly videoEncoder?:
    | {
        isConfigSupported(config: VideoEncoderConfig): Promise<{ supported?: boolean }>;
      }
    | undefined;
  /** 기본값 `globalThis.MediaRecorder`. **Worker 전역에는 없다**(04 §7.3a). */
  readonly mediaRecorder?: { isTypeSupported(type: string): boolean } | undefined;
  /** 경로 B는 Worker 전용 구현이다 → Worker가 없으면 B를 쓸 수 없다. */
  readonly workerAvailable?: boolean;
}

export const MEDIARECORDER_MP4_MIME = "video/mp4;codecs=avc1";

/** 마지막 판정 결과. 진단 모달(Step 16)이 읽는다. */
let lastProbe: EncoderProbe | null = null;

/**
 * 경로 판정. 어떤 예외도 밖으로 새지 않는다.
 *
 * @param size 짝수 클램프 **전** 가공 해상도. 내부에서 `evenDimensions`·`timelapseBitrate`를
 *             적용해 실제 인코딩에 쓸 config로 질의한다.
 */
export async function detectEncoderPath(
  size: { width: number; height: number },
  deps: EncoderProbeDeps = {},
): Promise<EncoderProbe> {
  const videoEncoder = deps.videoEncoder ?? globalThis.VideoEncoder;
  const mediaRecorder = deps.mediaRecorder ?? globalThis.MediaRecorder;
  const workerAvailable = deps.workerAvailable ?? typeof Worker !== "undefined";

  const even = evenDimensions(size.width, size.height);
  const bitrate = timelapseBitrate(even.width, even.height);
  const probed: { codec: string; supported: boolean }[] = [];

  // ── 경로 B: WebCodecs(Worker 안에서 완결) ──
  if (videoEncoder !== undefined && workerAvailable) {
    for (const codec of TIMELAPSE_CODEC_CANDIDATES) {
      let supported = false;
      try {
        const result = await videoEncoder.isConfigSupported({
          codec,
          width: even.width,
          height: even.height,
          bitrate,
          framerate: TIMELAPSE_OUTPUT_FPS,
        });
        supported = result?.supported === true;
      } catch {
        // 지원하지 않는 config에 대해 던지는 구현이 있다 — 미지원으로 취급하고 다음 후보로 간다.
        supported = false;
      }
      probed.push({ codec, supported });
      if (supported) {
        return remember({
          path: "webcodecs",
          codec,
          reason: `WebCodecs ${codec}`,
          probed,
        });
      }
    }
  }

  // ── 경로 A: MediaRecorder(메인 스레드 전용 예비 경로) ──
  let mp4Supported = false;
  try {
    mp4Supported = mediaRecorder?.isTypeSupported(MEDIARECORDER_MP4_MIME) === true;
  } catch {
    mp4Supported = false;
  }
  if (mp4Supported) {
    return remember({
      path: "mediarecorder",
      codec: null,
      reason: `MediaRecorder ${MEDIARECORDER_MP4_MIME}`,
      probed,
    });
  }

  // ── 경로 C: 미지원 — 타임랩스만 없이 촬영을 완주한다 ──
  return remember({ path: "none", codec: null, reason: "H.264 인코더 없음", probed });
}

/** 마지막 판정 결과(진단 — 12 §E6). 아직 판정하지 않았으면 null. */
export function lastEncoderProbe(): EncoderProbe | null {
  return lastProbe;
}

function remember(probe: EncoderProbe): EncoderProbe {
  lastProbe = probe;
  return probe;
}
