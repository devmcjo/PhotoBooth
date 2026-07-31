import { logger } from "@adapters/storage/logStore";
import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * 업로드 3단계의 서버 호출 — `POST /uploads/prepare` · 서명 PUT · `POST /uploads/commit`
 * (analysis/31 §5 · 06 §4)
 *
 * ② 서명 URL PUT은 **XHR**이다. `fetch`는 업로드 진행률을 제공하지 않는다(WM5).
 *
 * ⚠️ 게이트는 `apiKey + optionalBearer`다 — **게스트(무토큰)도 서버가 허용**한다.
 *    단 클라이언트는 게스트에게 업로드를 시작하지 않는다(effective QR off — VF-11).
 *    여기서 `auth: "optional"`인 이유는 로그인 상태면 토큰을 붙여 TempUser 한도를 서버가 집계해야 하기 때문이다.
 *
 * ⚠️ **서명 PUT에는 자격 증명을 붙이지 않는다.** 서명 URL 자체가 권한이라, 인증 헤더를 얹으면
 *    서명 검증이 깨지거나 preflight가 막힌다(31 §5.2). `uploadGateway.test.ts`가 소스를 읽어 고정한다.
 * ⚠️ **서명 URL·헤더 값·응답 본문을 로그에 남기지 않는다**(analysis/41 §8). URL 자체가 capability다.
 */

export type UploadKind = "final" | "timelapse";

export interface PrepareFileRequest {
  readonly kind: UploadKind;
  readonly ext: "jpg" | "png" | "mp4";
  readonly contentType: string;
}

export interface PrepareRequest {
  readonly sessionId: string;
  readonly files: readonly PrepareFileRequest[];
}

export interface PreparedUpload {
  readonly kind: UploadKind;
  /** GCS V4 서명 PUT URL. **TTL 15분.** */
  readonly putUrl: string;
  /** 업로드 후 읽기 URL. **commit에 이 값을 그대로 넘긴다.** */
  readonly downloadUrl: string;
  /**
   * PUT에 **전부 그대로** 부착해야 하는 헤더(M14).
   * 하나라도 빠지면 서명 불일치 또는 다운로드 토큰 미설정이 된다 —
   * **하드코딩하지 말고 이 객체를 순회**해서 붙인다.
   */
  readonly requiredHeaders: Readonly<Record<string, string>>;
}

export interface PrepareResponse {
  readonly uploads: readonly PreparedUpload[];
  readonly bucket: string;
}

export interface CommitRequest {
  readonly sessionId: string;
  /** prepare의 `downloadUrl`을 그대로. 없으면 null. */
  readonly finalImageUrl: string | null;
  readonly timelapseUrl: string | null;
  /** 정수 1~72. */
  readonly retentionHours: number;
  readonly downloadPageUrl: string;
}

export interface CommitResponse {
  readonly id: string;
  readonly finalImageUrl: string | null;
  readonly timelapseUrl: string | null;
  /** 서버가 commit 시점에 계산한다(클라이언트 시각은 쓰이지 않는다). */
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly downloadPageUrl: string;
}

// ───────────────────────────── ② 서명 PUT (XHR — WM5) ─────────────────────────────

/** `xhr.upload.onprogress` 1회분. */
export interface SignedPutProgress {
  readonly loaded: number;
  readonly total: number;
}

export interface SignedPutRequest {
  /** V4 서명 URL. **절대 로그에 남기지 않는다.** */
  readonly url: string;
  readonly body: Blob;
  /**
   * prepare가 준 `requiredHeaders` **그대로**. 이 객체를 **순회해 전부** 부착한다(M14).
   * 키를 골라 담거나 이름 대소문자를 바꾸면 서명 불일치(403) 또는 다운로드 토큰 미설정이 된다.
   */
  readonly headers: Readonly<Record<string, string>>;
  /** 진단 로그 라벨 전용(요청 내용에 영향을 주지 않는다). */
  readonly kind?: UploadKind;
  readonly onProgress?: (progress: SignedPutProgress) => void;
  readonly signal?: AbortSignal;
  readonly timeoutMs?: number;
}

export type SignedPutFailure = "http" | "network" | "timeout" | "aborted";

export type SignedPutOutcome =
  | {
      readonly ok: true;
      readonly status: number;
      readonly bytes: number;
      readonly elapsedMs: number;
    }
  | {
      readonly ok: false;
      readonly failure: SignedPutFailure;
      readonly status: number | null;
      readonly elapsedMs: number;
    };

/** 06 §4.2 — `backendClient`의 API 타임아웃과 같은 값. */
export const SIGNED_PUT_TIMEOUT_MS = 100_000;

/**
 * 브라우저는 CORS 차단과 순수 네트워크 실패를 **구분해 주지 않는다**(03 §9.3 · 08 §5).
 * 운영자가 원인을 좁힐 수 있도록 진단 힌트를 남긴다.
 */
const NETWORK_FAILURE_HINT = "네트워크 또는 CORS 차단 가능 — 업로드 구성(CORS) 확인 필요";

export interface UploadGateway {
  prepare(request: PrepareRequest): Promise<PrepareResponse>;
  commit(request: CommitRequest): Promise<CommitResponse>;
  /**
   * ② 서명 PUT. ⚠️ **던지지 않는다**(15 §2 — 어댑터는 예외를 전파하지 않는다).
   * 실패는 `SignedPutOutcome`의 판별 유니온으로 표현한다.
   */
  put(request: SignedPutRequest): Promise<SignedPutOutcome>;
}

export interface UploadGatewayDeps {
  /** 테스트 주입. 기본 `() => new XMLHttpRequest()`. */
  readonly createXhr?: () => XMLHttpRequest;
  readonly now?: () => number;
}

function parsePrepare(raw: unknown): PrepareResponse {
  const record = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  const uploads = Array.isArray(record.uploads) ? record.uploads : [];

  return {
    bucket: typeof record.bucket === "string" ? record.bucket : "",
    uploads: uploads
      .map((item): PreparedUpload | null => {
        if (typeof item !== "object" || item === null) return null;
        const upload = item as Record<string, unknown>;
        const kind = upload.kind;
        if (kind !== "final" && kind !== "timelapse") return null;
        if (typeof upload.putUrl !== "string" || typeof upload.downloadUrl !== "string") return null;
        const headers = upload.requiredHeaders;
        return {
          kind,
          putUrl: upload.putUrl,
          downloadUrl: upload.downloadUrl,
          // 응답 객체를 그대로 보존한다 — 키를 골라 담으면 M14가 깨진다.
          requiredHeaders:
            typeof headers === "object" && headers !== null
              ? (headers as Record<string, string>)
              : {},
        };
      })
      .filter((u): u is PreparedUpload => u !== null),
  };
}

/**
 * 서명 PUT 1건. 예외를 던지지 않고 결과를 판별 유니온으로 돌려준다.
 *
 * 해제 경로: `signal`의 abort 리스너는 settle 시 **반드시** 제거한다(누수 방지).
 * `settled` 플래그로 resolve는 정확히 1회다 — 여러 핸들러가 겹칠 수 있다.
 */
function signedPut(
  request: SignedPutRequest,
  createXhr: () => XMLHttpRequest,
  now: () => number,
): Promise<SignedPutOutcome> {
  const startedAt = now();
  const elapsed = (): number => Math.round(now() - startedAt);
  const headerNames = Object.keys(request.headers);

  return new Promise<SignedPutOutcome>((resolve) => {
    // 취소 후 낭비 전송을 하지 않는다 — send 자체를 하지 않는다.
    if (request.signal?.aborted === true) {
      resolve({ ok: false, failure: "aborted", status: null, elapsedMs: elapsed() });
      return;
    }

    let settled = false;
    let xhr: XMLHttpRequest;
    const onAbort = (): void => {
      xhr.abort();
    };

    const settle = (outcome: SignedPutOutcome): void => {
      if (settled) return;
      settled = true;
      request.signal?.removeEventListener("abort", onAbort);

      if (outcome.ok) {
        logger.info("서명 PUT 완료", {
          kind: request.kind ?? null,
          bytes: outcome.bytes,
          status: outcome.status,
          elapsedMs: outcome.elapsedMs,
          // 헤더 **이름**은 비밀이 아니고 M14 진단의 유일한 단서다. 값은 남기지 않는다.
          headerNames,
        });
      } else if (outcome.failure !== "aborted") {
        logger.error("서명 PUT 실패", {
          kind: request.kind ?? null,
          failure: outcome.failure,
          status: outcome.status,
          elapsedMs: outcome.elapsedMs,
          headerNames,
          ...(outcome.failure === "network" ? { hint: NETWORK_FAILURE_HINT } : {}),
        });
      }
      resolve(outcome);
    };

    try {
      xhr = createXhr();
      xhr.open("PUT", request.url, true);

      // ★ M14: 응답 객체를 **순회해 전부** 부착한다. 골라 담으면 서명이 깨진다.
      //   `open` 이후에 불러야 한다(이전 호출은 InvalidStateError).
      for (const [name, value] of Object.entries(request.headers)) {
        xhr.setRequestHeader(name, value);
      }
      xhr.timeout = request.timeoutMs ?? SIGNED_PUT_TIMEOUT_MS;

      const onProgress = request.onProgress;
      if (onProgress !== undefined) {
        // ⚠️ `xhr.onprogress`(다운로드)가 아니라 `xhr.upload.onprogress`다.
        xhr.upload.onprogress = (event: ProgressEvent): void => {
          if (event.lengthComputable) onProgress({ loaded: event.loaded, total: event.total });
        };
      }

      xhr.onload = (): void => {
        const status = xhr.status;
        if (status >= 200 && status < 300) {
          settle({ ok: true, status, bytes: request.body.size, elapsedMs: elapsed() });
        } else {
          settle({ ok: false, failure: "http", status, elapsedMs: elapsed() });
        }
      };
      xhr.onerror = (): void => {
        settle({ ok: false, failure: "network", status: null, elapsedMs: elapsed() });
      };
      xhr.ontimeout = (): void => {
        settle({ ok: false, failure: "timeout", status: null, elapsedMs: elapsed() });
      };
      xhr.onabort = (): void => {
        settle({ ok: false, failure: "aborted", status: null, elapsedMs: elapsed() });
      };

      request.signal?.addEventListener("abort", onAbort);
      xhr.send(request.body);
    } catch {
      // 어댑터는 던지지 않는다. `open`/`send`의 동기 예외도 상태로 축소한다.
      settle({ ok: false, failure: "network", status: null, elapsedMs: elapsed() });
    }
  });
}

export function createUploadGateway(
  client: BackendClient = getBackendClient(),
  deps: UploadGatewayDeps = {},
): UploadGateway {
  const createXhr = deps.createXhr ?? ((): XMLHttpRequest => new XMLHttpRequest());
  const now = deps.now ?? ((): number => Date.now());

  return {
    put(request) {
      return signedPut(request, createXhr, now);
    },

    async prepare(request) {
      return parsePrepare(
        await client.request<unknown>({
          method: "POST",
          path: "uploads/prepare",
          body: request,
          auth: "optional",
        }),
      );
    },

    async commit(request) {
      return client.request<CommitResponse>({
        method: "POST",
        path: "uploads/commit",
        body: request,
        auth: "optional",
      });
    },
  };
}
