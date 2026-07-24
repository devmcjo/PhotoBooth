/**
 * 프레임 서비스 — Firestore frameTemplates + Storage frames/{owner}/ 조작.
 * WPF `FrameRepository`(C#)의 서버 이식. 10개 제한·owner 경로·고아 방지 로직을 서버로 이전(설계 §5.3).
 *
 * 근거: src/MCPhoto.Firebase/FrameRepository.cs, src/MCPhoto.Core/Frames/IFrameRepository.cs
 */
import { randomUUID } from "node:crypto";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../firebase";
import { loadConfig } from "../config";
import { HttpError } from "../http/errors";
import { FrameTemplateDoc, FrameResponse } from "./dto";
import { createSignedUpload, deleteStoragePrefix, SignedUpload } from "./signing";
import { ImageSize, Slot } from "../domain/validation";

const COLLECTION = "frameTemplates";
const MAX_PER_USER = 10;

function toResponse(doc: FrameTemplateDoc): FrameResponse {
  return {
    id: doc.id,
    userId: doc.userId ?? null,
    isDefault: doc.isDefault,
    name: doc.name,
    imageUrl: doc.imageUrl,
    imageSize: doc.imageSize,
    slots: doc.slots,
    createdAt: doc.createdAt.toDate().toISOString(),
  };
}

/** 공용 기본 프레임(isDefault=true) 조회. 공개(게스트 가능). */
export async function getDefaultFrames(): Promise<FrameResponse[]> {
  const snap = await db().collection(COLLECTION).where("isDefault", "==", true).get();
  return snap.docs.map((d) => toResponse(d.data() as FrameTemplateDoc));
}

/** 특정 계정 소유 프레임 조회. 라우트에서 본인/파워 여부를 이미 검증. */
export async function getUserFrames(userId: string): Promise<FrameResponse[]> {
  const snap = await db().collection(COLLECTION).where("userId", "==", userId).get();
  return snap.docs.map((d) => toResponse(d.data() as FrameTemplateDoc));
}

export interface SaveFrameInput {
  name: string;
  isDefault: boolean;
  imageSize: ImageSize;
  slots: Slot[];
  /** 소유자 id. 공용 기본 프레임(파워 생성)은 null. */
  userId: string | null;
  /** 이미지 파일 확장자(항상 png — 프레임 규약). */
  contentType: string;
}

export interface SaveFrameResult {
  frame: FrameResponse;
  /** 클라가 이미지를 직접 PUT할 서명 URL + 필수 헤더. */
  upload: SignedUpload;
}

/**
 * 프레임 저장: 메타 검증 → 서명 PUT URL + 다운로드 토큰 URL 발급 → 문서 생성(imageUrl=다운로드URL).
 * 이미지 바이트는 클라가 서명 URL로 직접 PUT한다(설계 §5.4-A, 함수 비용 최소).
 *
 * 10개 제한(userId 있을 때만) 서버 재검증. 트레이드오프: 문서를 이미지 PUT 전에 생성하므로
 * PUT 실패 시 이미지 없는 문서가 남을 수 있다(현행 SaveAsync는 업로드 후 문서 생성). 프레임은
 * 웹 접근이 없고 재저장으로 덮어쓰기 가능하므로 수용(설계 §5.3 단일 POST /frames 계약 준수).
 */
export async function saveFrame(input: SaveFrameInput): Promise<SaveFrameResult> {
  const cfg = loadConfig();

  // 계정당 10개 제한(userId 있을 때만). 신규 생성이므로 초과 시 거부.
  if (input.userId) {
    const existing = await getUserFrames(input.userId);
    if (existing.length >= MAX_PER_USER) {
      throw HttpError.conflict(
        `프레임은 계정당 최대 ${MAX_PER_USER}개까지 저장할 수 있습니다.`
      );
    }
  }

  const frameId = randomUUID();
  const owner = input.userId ?? "default";
  const storagePath = `frames/${owner}/${frameId}.png`;

  const upload = await createSignedUpload(cfg.storageBucket, storagePath, input.contentType);

  const now = Timestamp.now();
  const doc: FrameTemplateDoc = {
    id: frameId,
    userId: input.userId,
    isDefault: input.isDefault,
    name: input.name,
    imageUrl: upload.downloadUrl,
    imageSize: input.imageSize,
    slots: input.slots,
    createdAt: now,
  };
  await db().collection(COLLECTION).doc(frameId).set(doc);

  return { frame: toResponse(doc), upload };
}

/**
 * 프레임 삭제: 문서 존재 확인 → owner 읽어 Storage 이미지 삭제 → 문서 삭제.
 * 반환=문서가 실제로 존재해 삭제됐는지(없으면 false, 현행 계약).
 * 근거: FrameRepository.DeleteAsync (FrameRepository.cs:76-104)
 */
export async function deleteFrame(frameId: string): Promise<boolean> {
  const cfg = loadConfig();
  const ref = db().collection(COLLECTION).doc(frameId);
  const snap = await ref.get();
  if (!snap.exists) return false;

  const doc = snap.data() as FrameTemplateDoc;
  const owner = doc.userId ? doc.userId : "default";
  // 고아 이미지 방지: 문서 삭제 전에 Storage 경로로 이미지 삭제(실패해도 문서는 삭제 진행).
  try {
    await deleteStoragePrefix(cfg.storageBucket, `frames/${owner}/${frameId}.png`);
  } catch {
    // Storage 삭제 실패는 문서 삭제를 막지 않는다(로그성 무시, 현행 동작).
  }
  await ref.delete();
  return true;
}

/**
 * 계정 소유 프레임 전부 삭제(계정 삭제 cascade). 문서 + Storage frames/{userId}/ 전체.
 * 근거: FrameRepository.DeleteAllByUserAsync (FrameRepository.cs:106-118)
 */
export async function deleteAllFramesByUser(userId: string): Promise<void> {
  const cfg = loadConfig();
  const snap = await db().collection(COLLECTION).where("userId", "==", userId).get();
  const batch = db().batch();
  snap.docs.forEach((d) => batch.delete(d.ref));
  await batch.commit();
  try {
    await deleteStoragePrefix(cfg.storageBucket, `frames/${userId}/`);
  } catch {
    // Storage 삭제 실패는 cascade를 막지 않는다(현행 동작).
  }
}
