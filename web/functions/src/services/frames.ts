/**
 * 프레임 서비스 — Firestore frameTemplates + Storage 조작.
 * WPF `FrameRepository`(C#)의 서버 이식. owner 경로·고아 방지 로직을 서버로 이전(설계 §5.3).
 *
 * ⚠️ Storage 경로는 `framePath()` 한 곳에서만 만든다. 개인 프레임은 `frames/users/{userId}/`로
 *    분리한다 — `frames/{userId}/`를 쓰면 계정 id가 "default"인 사용자(default@… 로 가입 가능)의
 *    프레임이 공용 경로에 섞이고, 최악의 경우 cascade 삭제가 공용 프레임을 지운다.
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

/**
 * 프레임 이미지 최대 바이트(8MB). 클라 `FrameImageValidator.MaxBytes`와 **같은 값이어야 한다** —
 * 어긋나면 사용자가 업로드 직전까지 갔다가 실패한다.
 *
 * ⚠️ 개수 상한을 폐지했으므로(설계 D-10) 이 값이 **유일한 총량 방어선**이다. 프레임은 TTL 비대상이라
 *    한 번 올라간 용량은 영구 비용이 된다.
 */
const FRAME_IMAGE_MAX_BYTES = 8 * 1024 * 1024;

/** 공용 기본 프레임의 Storage 폴더명(계정 id와 충돌하지 않도록 개인은 users/ 아래로 분리한다). */
const DEFAULT_OWNER_FOLDER = "default";

/**
 * 프레임 이미지 Storage 경로(단일 출처).
 * 개인 `frames/users/{userId}/{frameId}.png` · 공용 `frames/default/{frameId}.png`.
 */
function framePath(userId: string | null, frameId: string): string {
  return userId
    ? `frames/users/${userId}/${frameId}.png`
    : `frames/${DEFAULT_OWNER_FOLDER}/${frameId}.png`;
}

/** 계정 소유 프레임 전체의 Storage 접두(cascade 삭제용). `framePath`와 반드시 같은 규칙이어야 한다. */
function userFramesPrefix(userId: string): string {
  return `frames/users/${userId}/`;
}

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

export interface UpdateFrameInput {
  /** 업데이트 대상 문서 id(경로 파라미터). */
  frameId: string;
  name: string;
  imageSize: ImageSize;
  slots: Slot[];
  /** true면 이미지 바이트를 교체(서명 PUT URL 발급). false면 메타만 갱신(URL 없음). */
  replaceImage: boolean;
  /** 이미지 파일 확장자(항상 png — 프레임 규약). replaceImage=true일 때만 사용. */
  contentType: string;
}

export interface UpdateFrameResult {
  frame: FrameResponse;
  /**
   * 이미지 교체 시(replaceImage=true) 발급되는 서명 PUT URL + 필수 헤더.
   * 이미지 미변경(replaceImage=false)이면 undefined(클라는 이미지를 PUT하지 않는다).
   */
  upload?: SignedUpload;
}

/**
 * 프레임 저장: 메타 검증 → 서명 PUT URL + 다운로드 토큰 URL 발급 → 문서 생성(imageUrl=다운로드URL).
 * 이미지 바이트는 클라가 서명 URL로 직접 PUT한다(설계 §5.4-A, 함수 비용 최소).
 *
 * ⚠️ **개수 상한은 폐지됐다**(설계 D-10). 프레임은 1회성 구매 + 영구 사용이라 개수 제한과 모순이고,
 * 생성 과금이 이미 원가를 덮으며(보관 12.6원/년 vs 판매 500원), 유료라 오남용 유인도 없다.
 * 총량 방어는 **이미지 8MB 상한**(서명 조건, `signing.ts`) 하나뿐이므로 그것을 절대 빼지 말 것.
 *
 * 트레이드오프: 문서를 이미지 PUT 전에 생성하므로 PUT 실패 시 이미지 없는 문서가 남을 수 있다.
 * 프레임은 웹 접근이 없고 재저장으로 덮어쓰기 가능하므로 수용(설계 §5.3 계약 준수).
 */
export async function saveFrame(input: SaveFrameInput): Promise<SaveFrameResult> {
  const cfg = loadConfig();

  // 계정 내 이름 중복 거부(설계 D-17·S8). 클라도 사전 검증하지만 **PC 두 대에서 동시 생성**은
  // 서버만 막을 수 있다 — 통과시키면 로컬 캐시에서 한쪽이 다른 쪽을 덮어쓴다.
  if (input.userId) {
    const existing = await getUserFrames(input.userId);
    const dup = existing.some(
      (f) => f.name.trim().toLowerCase() === input.name.trim().toLowerCase()
    );
    if (dup) {
      throw HttpError.conflict(`같은 이름의 프레임이 이미 있습니다: ${input.name}`);
    }
  }

  const frameId = randomUUID();
  const storagePath = framePath(input.userId, frameId);

  const upload = await createSignedUpload(
    cfg.storageBucket, storagePath, input.contentType, FRAME_IMAGE_MAX_BYTES);

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
 * 기존 공용 기본 프레임 업데이트(같은 frameId 덮어쓰기, 설계 §3·§5.1 옵션 B).
 * WPF `FrameRepository.SaveAsync`가 `SetAsync(frame.Id)`로 하던 "같은 문서 덮어쓰기"를 HTTP로 노출.
 *
 * - 대상 문서가 없으면 404.
 * - 기본 프레임(isDefault=true, userId=null)만 업데이트 가능. user 소유 문서(userId!=null)는 거부(403).
 *   (user 커스텀 프레임은 it8 A2로 로컬 전용 — 서버에는 존재하지 않아야 하나, 레거시 문서 방어.)
 * - name·slots·imageSize만 갱신. isDefault·userId·createdAt·id는 보존(불변).
 * - replaceImage=true면 같은 Storage 경로(frames/default/{id}.png)에 새 서명 PUT URL 발급(덮어쓰기).
 *   기존 imageUrl(다운로드 토큰 URL)은 새 토큰으로 교체된다. replaceImage=false면 imageUrl 그대로 보존.
 *
 * 10개 제한은 기본 프레임(userId=null)에 미적용(신규 생성 아님, 카운트 무관).
 */
export async function updateFrame(input: UpdateFrameInput): Promise<UpdateFrameResult> {
  const cfg = loadConfig();
  const ref = db().collection(COLLECTION).doc(input.frameId);
  const snap = await ref.get();
  if (!snap.exists) {
    throw HttpError.notFound("프레임을 찾을 수 없습니다.");
  }

  const current = snap.data() as FrameTemplateDoc;
  // 공용 기본 프레임만 업데이트 대상(설계 §3: 기본프레임=null·isDefault=true 보존).
  if (current.userId !== null || current.isDefault !== true) {
    throw HttpError.forbidden("공용 기본 프레임만 업데이트할 수 있습니다.");
  }

  // 갱신 필드(name·slots·imageSize). 보존 필드(id·userId·isDefault·createdAt)는 current에서 유지.
  const updated: FrameTemplateDoc = {
    id: current.id,
    userId: null,
    isDefault: true,
    name: input.name,
    imageUrl: current.imageUrl,
    imageSize: input.imageSize,
    slots: input.slots,
    createdAt: current.createdAt,
  };

  let upload: SignedUpload | undefined;
  if (input.replaceImage) {
    // 이미지 교체: 같은 owner 경로(default)·같은 frameId 키에 덮어쓰기. 새 다운로드 토큰 URL로 갱신.
    const storagePath = `frames/default/${current.id}.png`;
    upload = await createSignedUpload(
      cfg.storageBucket, storagePath, input.contentType, FRAME_IMAGE_MAX_BYTES);
    updated.imageUrl = upload.downloadUrl;
  }

  await ref.set(updated);

  return { frame: toResponse(updated), upload };
}

/**
 * 프레임 삭제: 문서 존재 확인 → owner 읽어 Storage 이미지 삭제 → 문서 삭제.
 * 반환=문서가 실제로 존재해 삭제됐는지(없으면 false, 현행 계약).
 * 근거: FrameRepository.DeleteAsync (FrameRepository.cs:76-104)
 */
/**
 * 삭제 권한 판정을 위한 소유자 조회.
 * @returns 개인 프레임이면 소유 계정 id, 공용이면 `null`, **문서가 없으면 `undefined`**.
 */
export async function getFrameOwnerId(
  frameId: string
): Promise<string | null | undefined> {
  const snap = await db().collection(COLLECTION).doc(frameId).get();
  if (!snap.exists) return undefined;
  return (snap.data() as FrameTemplateDoc).userId ?? null;
}

export async function deleteFrame(frameId: string): Promise<boolean> {
  const cfg = loadConfig();
  const ref = db().collection(COLLECTION).doc(frameId);
  const snap = await ref.get();
  if (!snap.exists) return false;

  const doc = snap.data() as FrameTemplateDoc;
  // 고아 이미지 방지: 문서 삭제 전에 Storage 경로로 이미지 삭제(실패해도 문서는 삭제 진행).
  try {
    await deleteStoragePrefix(cfg.storageBucket, framePath(doc.userId, frameId));
  } catch {
    // Storage 삭제 실패는 문서 삭제를 막지 않는다(로그성 무시, 현행 동작).
  }
  await ref.delete();
  return true;
}

/**
 * 계정 소유 프레임 전부 삭제(계정 삭제 cascade). 문서 + Storage frames/users/{userId}/ 전체.
 * ⚠️ 접두는 `userFramesPrefix`를 쓴다 — `framePath`와 규칙이 어긋나면 이미지가 영구 잔존한다.
 * 근거: FrameRepository.DeleteAllByUserAsync (FrameRepository.cs:106-118)
 */
export async function deleteAllFramesByUser(userId: string): Promise<void> {
  const cfg = loadConfig();
  const snap = await db().collection(COLLECTION).where("userId", "==", userId).get();
  const batch = db().batch();
  snap.docs.forEach((d) => batch.delete(d.ref));
  await batch.commit();
  try {
    await deleteStoragePrefix(cfg.storageBucket, userFramesPrefix(userId));
  } catch {
    // Storage 삭제 실패는 cascade를 막지 않는다(현행 동작).
  }
}
