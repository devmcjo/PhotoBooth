/**
 * 업로드 서비스 — 서명 URL 발급(prepare) + resultSession 문서 생성(commit). 설계 §5.4-A·§6.2.
 * WPF `UploadService`/`FirebaseClient`(C#)의 서버 이식.
 *
 * 파일 바이트는 함수를 경유하지 않는다(클라가 서명 URL로 직접 PUT). 다운로드 토큰 URL은
 * prepare가 발급하고, commit이 그 URL로 resultSession을 만든다(최소 1개 불변식·expiresAt 강제).
 *
 * 근거: src/MCPhoto.Firebase/{UploadService,FirebaseClient}.cs, UploadContract.cs
 */
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../firebase";
import { loadConfig } from "../config";
import {
  computeExpiresAt,
  finalImagePath,
  timelapsePath,
} from "../domain/session";
import { extToFormat, UploadFileSpec } from "../domain/validation";
import { HttpError } from "../http/errors";
import { ResultSessionDoc, ResultSessionResponse } from "./dto";
import { createSignedUpload } from "./signing";

const COLLECTION = "resultSessions";

export interface PreparedUpload {
  kind: "final" | "timelapse";
  putUrl: string;
  downloadUrl: string;
  requiredHeaders: Record<string, string>;
}

export interface PrepareResult {
  uploads: PreparedUpload[];
  bucket: string;
}

/**
 * prepare: sessionId·파일 목록에 대해 서명 PUT URL + 다운로드 토큰 URL 발급.
 * 파일별 경로는 kind로 결정(final→results/{sid}/final.{ext}, timelapse→timelapse.mp4).
 */
export async function prepareUpload(
  sessionId: string,
  files: UploadFileSpec[]
): Promise<PrepareResult> {
  const cfg = loadConfig();
  if (files.length === 0) {
    throw HttpError.invalid("업로드할 파일이 없습니다.");
  }

  const seen = new Set<string>();
  const uploads: PreparedUpload[] = [];
  for (const f of files) {
    if (seen.has(f.kind)) {
      throw HttpError.invalid(`중복된 파일 종류입니다: ${f.kind}`);
    }
    seen.add(f.kind);

    const path =
      f.kind === "final"
        ? finalImagePath(sessionId, extToFormat(f.ext))
        : timelapsePath(sessionId);
    const signed = await createSignedUpload(cfg.storageBucket, path, f.contentType);
    uploads.push({
      kind: f.kind,
      putUrl: signed.putUrl,
      downloadUrl: signed.downloadUrl,
      requiredHeaders: signed.requiredHeaders,
    });
  }

  return { uploads, bucket: cfg.storageBucket };
}

export interface CommitInput {
  sessionId: string;
  finalImageUrl: string | null;
  timelapseUrl: string | null;
  retentionHours: number;
  downloadPageUrl: string;
}

/**
 * 넘겨받은 다운로드 URL이 서버 버킷·해당 sessionId 경로를 가리키는지 형식 검증(위조 방어).
 * prepare 없이 임의 URL을 commit에 심는 것을 막는다.
 */
function assertUrlBelongsToSession(
  url: string,
  bucket: string,
  sessionId: string,
  kind: "final" | "timelapse"
): void {
  const prefix = `https://firebasestorage.googleapis.com/v0/b/${bucket}/o/`;
  if (!url.startsWith(prefix)) {
    throw HttpError.invalid(`${kind} URL이 이 서버의 버킷을 가리키지 않습니다.`);
  }
  // 경로 부분(디코드)이 results/{sessionId}/ 하위인지 확인.
  const encoded = url.slice(prefix.length).split("?")[0];
  const decoded = decodeURIComponent(encoded);
  const expectedPrefix = `results/${sessionId}/`;
  const expectedName =
    kind === "final" ? `${expectedPrefix}final.` : `${expectedPrefix}timelapse.mp4`;
  if (kind === "final") {
    if (!decoded.startsWith(expectedName)) {
      throw HttpError.invalid("final URL 경로가 세션과 일치하지 않습니다.");
    }
  } else if (decoded !== expectedName) {
    throw HttpError.invalid("timelapse URL 경로가 세션과 일치하지 않습니다.");
  }
}

/**
 * commit: resultSession 문서 생성. 최소 1개 불변식(final·timelapse 중 하나는 non-null) 강제.
 * 문서 ID = sessionId. expiresAt = now + retentionHours. 중복 sessionId면 409.
 * 근거: UploadService.UploadResultAsync (UploadService.cs:24-89), 최소 1개 불변식(:37-38)
 */
export async function commitUpload(input: CommitInput): Promise<ResultSessionResponse> {
  const cfg = loadConfig();

  const hasFinal = typeof input.finalImageUrl === "string" && input.finalImageUrl.length > 0;
  const hasTimelapse =
    typeof input.timelapseUrl === "string" && input.timelapseUrl.length > 0;
  if (!hasFinal && !hasTimelapse) {
    throw HttpError.invalid(
      "전송할 미디어가 없습니다(사진·타임랩스 모두 없음). 최소 1개 필요."
    );
  }

  if (hasFinal) {
    assertUrlBelongsToSession(input.finalImageUrl!, cfg.storageBucket, input.sessionId, "final");
  }
  if (hasTimelapse) {
    assertUrlBelongsToSession(
      input.timelapseUrl!,
      cfg.storageBucket,
      input.sessionId,
      "timelapse"
    );
  }

  const ref = db().collection(COLLECTION).doc(input.sessionId);
  const existing = await ref.get();
  if (existing.exists) {
    throw HttpError.conflict(`이미 존재하는 세션입니다: ${input.sessionId}`);
  }

  const now = new Date();
  const createdAt = Timestamp.fromDate(now);
  const expiresAt = Timestamp.fromDate(computeExpiresAt(now, input.retentionHours));

  const doc: ResultSessionDoc = {
    id: input.sessionId,
    finalImageUrl: hasFinal ? input.finalImageUrl : null,
    timelapseUrl: hasTimelapse ? input.timelapseUrl : null,
    createdAt,
    expiresAt,
    downloadPageUrl: input.downloadPageUrl,
  };
  await ref.set(doc);

  return {
    id: doc.id,
    finalImageUrl: doc.finalImageUrl,
    timelapseUrl: doc.timelapseUrl,
    createdAt: createdAt.toDate().toISOString(),
    expiresAt: expiresAt.toDate().toISOString(),
    downloadPageUrl: doc.downloadPageUrl,
  };
}
