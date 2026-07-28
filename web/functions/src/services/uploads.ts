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
import { AuthPrincipal } from "../domain/jwt";
import { evaluateQrGate, QrGateReason } from "../domain/tempUserLimit";
import { extToFormat, UploadFileSpec } from "../domain/validation";
import { HttpError } from "../http/errors";
import { loadTempUserLimits } from "./config";
import { ResultSessionDoc, ResultSessionResponse, UserDoc } from "./dto";
import { createSignedUpload } from "./signing";

const COLLECTION = "resultSessions";
const USERS_COLLECTION = "users";

/** 초과 사유(time/count)를 해당 403 HttpError로 매핑(설계 §5.2). "ok"는 호출측이 넘기지 않는다. */
function gateReasonToError(reason: Exclude<QrGateReason, "ok">): HttpError {
  return reason === "time"
    ? HttpError.tempUserTimeExceeded()
    : HttpError.tempUserCountExceeded();
}

/** principal이 TempUser인지(비로그인·User↑는 한도 미적용). */
function isTempUser(principal?: AuthPrincipal): boolean {
  return principal?.role === "temp_user";
}

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
 *
 * it13: principal이 TempUser면 한도를 선검사한다(초과 시 403). Storage 서명 URL을 아예 내주지 않아
 * 과금(직접 PUT)을 원천 차단하는 1차 방어(설계 §5.1). 게스트·User↑는 principal이 없거나 비TempUser라 통과.
 */
export async function prepareUpload(
  sessionId: string,
  files: UploadFileSpec[],
  principal?: AuthPrincipal
): Promise<PrepareResult> {
  const cfg = loadConfig();
  if (files.length === 0) {
    throw HttpError.invalid("업로드할 파일이 없습니다.");
  }

  if (isTempUser(principal)) {
    // 초과면 여기서 거부 → 서명 URL 미발급. commit에서 트랜잭션으로 최종 재검사(§5.1).
    await assertTempUserWithinLimit(principal!.id);
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

/**
 * TempUser 계정의 QR 한도 선검사(비트랜잭션, prepare용). 계정 문서 부재 시에도 한도 로직상 안전:
 * 문서 없음은 비정상이나 createdAt을 알 수 없으므로 거부하지 않고 통과(commit 트랜잭션이 최종 권위).
 * 초과면 사유별 403(설계 §5.1·§5.2). now·createdAt은 서버 UTC ms(§8.4).
 */
async function assertTempUserWithinLimit(userId: string): Promise<void> {
  const snap = await db().collection(USERS_COLLECTION).doc(userId).get();
  if (!snap.exists) return; // 계정 미상(비정상) — commit 트랜잭션에서 최종 판정.
  const user = snap.data() as UserDoc;
  const limits = await loadTempUserLimits();
  const createdAtMs = user.createdAt.toDate().getTime();
  const usedCount = typeof user.qrUsedCount === "number" ? user.qrUsedCount : 0;
  const gate = evaluateQrGate(Date.now(), createdAtMs, usedCount, limits);
  if (gate.blocked && gate.reason !== "ok") {
    throw gateReasonToError(gate.reason);
  }
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
 *
 * it13: principal이 TempUser면 트랜잭션으로 (한도 재판정 → 초과 시 403 → resultSession set + qrUsedCount +1)을
 * 원자화한다(설계 §5.1·§8.3). "성공 세션 1회 = commit 최초 성공"이며 sessionId 중복(409)이 이중집계를 차단한다.
 * 비TempUser·게스트는 카운트·한도 없이 기존 비트랜잭션 경로를 탄다.
 */
export async function commitUpload(
  input: CommitInput,
  principal?: AuthPrincipal
): Promise<ResultSessionResponse> {
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

  if (isTempUser(principal)) {
    await commitTempUserSession(doc, principal!.id);
  } else {
    // 게스트·User↑: 기존 경로(한도·카운트 없음). 중복 sessionId면 409.
    const ref = db().collection(COLLECTION).doc(input.sessionId);
    const existing = await ref.get();
    if (existing.exists) {
      throw HttpError.conflict(`이미 존재하는 세션입니다: ${input.sessionId}`);
    }
    await ref.set(doc);
  }

  return {
    id: doc.id,
    finalImageUrl: doc.finalImageUrl,
    timelapseUrl: doc.timelapseUrl,
    createdAt: createdAt.toDate().toISOString(),
    expiresAt: expiresAt.toDate().toISOString(),
    downloadPageUrl: doc.downloadPageUrl,
  };
}

/**
 * TempUser commit을 트랜잭션으로 원자화(설계 §8.3): resultSession 중복 검사(409) + 계정 한도 재판정(초과 시 403)
 * + resultSession 생성 + qrUsedCount +1을 한 트랜잭션에 묶는다. 동시 다중 세션이 마지막 1회를 두고 경합해도
 * Firestore 트랜잭션 직렬화로 한 건만 통과한다. increment는 read-modify-write(현재값 읽어 +1)로 수행.
 *
 * 한도 로드(loadTempUserLimits)는 트랜잭션 read 이전에 수행한다(config 문서는 users와 무관, 경합 대상 아님).
 */
async function commitTempUserSession(
  doc: ResultSessionDoc,
  userId: string
): Promise<void> {
  const limits = await loadTempUserLimits();
  const sessionRef = db().collection(COLLECTION).doc(doc.id);
  const userRef = db().collection(USERS_COLLECTION).doc(userId);

  await db().runTransaction(async (tx) => {
    // 이중집계 차단: 동일 sessionId 재commit은 409(카운트 미증가).
    const existing = await tx.get(sessionRef);
    if (existing.exists) {
      throw HttpError.conflict(`이미 존재하는 세션입니다: ${doc.id}`);
    }

    const userSnap = await tx.get(userRef);
    if (!userSnap.exists) {
      // 로그인 principal인데 계정 문서가 없음(비정상) — 신원 불명이라 거부(과금 안전).
      throw HttpError.unauthorized("계정을 찾을 수 없습니다.");
    }
    const user = userSnap.data() as UserDoc;
    const createdAtMs = user.createdAt.toDate().getTime();
    const usedCount = typeof user.qrUsedCount === "number" ? user.qrUsedCount : 0;

    // prepare~commit 사이 경과/동시 세션으로 한도가 소진됐을 수 있어 최종 재판정.
    const gate = evaluateQrGate(Date.now(), createdAtMs, usedCount, limits);
    if (gate.blocked && gate.reason !== "ok") {
      throw gateReasonToError(gate.reason);
    }

    // 통과 → 세션 생성 + 카운트 원자 증가(세션당 1, 파일 개수 무관).
    tx.set(sessionRef, doc);
    tx.update(userRef, { qrUsedCount: usedCount + 1 });
  });
}
