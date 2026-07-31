import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * 업로드 3단계의 서버 호출 — `POST /uploads/prepare` · `POST /uploads/commit` (analysis/31 §5)
 *
 * ② 서명 URL PUT은 **Step 11**이 XHR로 구현한다(진행률이 필요해 `fetch`를 쓸 수 없다 — WM5).
 *
 * ⚠️ 게이트는 `apiKey + optionalBearer`다 — **게스트(무토큰)도 서버가 허용**한다.
 *    단 클라이언트는 게스트에게 업로드를 시작하지 않는다(effective QR off — VF-11).
 *    여기서 `auth: "optional"`인 이유는 로그인 상태면 토큰을 붙여 TempUser 한도를 서버가 집계해야 하기 때문이다.
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

export interface UploadGateway {
  prepare(request: PrepareRequest): Promise<PrepareResponse>;
  commit(request: CommitRequest): Promise<CommitResponse>;
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

export function createUploadGateway(client: BackendClient = getBackendClient()): UploadGateway {
  return {
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
