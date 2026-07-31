import { validateFrameNameForServer } from "@domain/frames/frameNaming";
import {
  frameSaveScope,
  requiresServerRegisterPrompt,
  validateFrameSave,
  type FrameSaveRejection,
  type FrameSessionSource,
} from "@domain/frames/frameSavePolicy";
import type { ImageSize, Slot } from "@domain/frames/types";
import type { UserRole } from "@domain/roles/userRole";
import type {
  CreateFrameRequest,
  CreateFrameResponse,
} from "@adapters/http/frameRepository";
import type { SignedPutOutcome, SignedPutRequest } from "@adapters/http/uploadGateway";
import type { SaveFrameInput } from "@adapters/storage/frameStore";
import { logger } from "@adapters/storage/logStore";
import { formatCount, frameSaveRejectionMessage, STRINGS } from "@ui/strings";

/**
 * 프레임 저장 파이프라인 — **순서가 규격이다** (03 §11.3·§11.4 · analysis/13 §6.3·§6.4)
 *
 * ⚠️ **첫 실행문이 검증 재실행**이다(FR-10). 진입점이 [저장] 버튼과 서버 등록 확인 오버레이
 *    **2개**이므로 여기서 다시 판정하지 않으면 오버레이 경로로 우회된다(fail-closed).
 * ⚠️ **원자성**: 서버 등록(문서 생성 · 이미지 PUT) 중 하나라도 실패하면 `saveLocal`에 **도달하지
 *    않는다**. 로컬만 저장해 두면 재시도 시 ⑦ 이름 충돌 가드가 **자기 자신과 충돌**해 저장이
 *    영구히 막힌다.
 * ⚠️ React를 import하지 않는다 — 순서·원자성이 node에서 통째로 검증된다.
 */

export interface FrameSaveDeps {
  /** 저장 스코프의 기존 이름(메타 전용 조회). 실패는 **빈 배열**(⑦ 비차단). */
  scopeNames(): Promise<readonly string[]>;
  /** 개인 프레임 개수. 실패는 0. */
  personalCount(): Promise<number>;
  /** `POST /frames`. 예외를 그대로 던진다(HTTP 계층 관례). */
  createServerFrame(request: CreateFrameRequest): Promise<CreateFrameResponse>;
  /** 서명 PUT. **던지지 않는다** — `SignedPutOutcome` 판별 유니온. */
  putImage(request: SignedPutRequest): Promise<SignedPutOutcome>;
  /** 고아 문서 정리(best-effort). 실패해도 사용자 문구를 바꾸지 않는다. */
  deleteServerFrame(id: string): Promise<boolean>;
  /** 로컬 저장. 실패는 `null`. */
  saveLocal(input: SaveFrameInput): Promise<{ readonly id: string } | null>;
  setStatus(message: string): void;
  /** 저장 성공 후 전이. */
  goToFrameSelect(): void;
}

export interface FrameSaveRequest {
  readonly role: UserRole | null;
  readonly userId: string | null;
  readonly sessionSource: FrameSessionSource;
  readonly name: string;
  readonly sourceName: string;
  readonly slots: readonly Slot[];
  readonly imageSize: ImageSize;
  /** PNG 바이트. `null`이면 ③에서 막힌다. */
  readonly png: Blob | null;
  readonly registerToServer: boolean;
}

export type FrameSaveOutcome =
  | { readonly status: "saved"; readonly registeredToServer: boolean }
  | { readonly status: "rejected"; readonly reason: FrameSaveRejection }
  | { readonly status: "server-failed"; readonly detail: string }
  | { readonly status: "local-failed" };

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 실패는 빈 배열 — ⑦이 조용히 꺼진다(비차단). 어댑터가 이미 삼키지만 2중 방어다. */
async function safeScopeNames(deps: FrameSaveDeps): Promise<readonly string[]> {
  try {
    return await deps.scopeNames();
  } catch (err) {
    logger.warn("저장 스코프 이름 조회 실패(⑦ 가드 비활성)", { reason: describe(err) });
    return [];
  }
}

async function safePersonalCount(deps: FrameSaveDeps): Promise<number> {
  try {
    return await deps.personalCount();
  } catch (err) {
    logger.warn("개인 프레임 개수 조회 실패(상한 판정 0으로 진행)", { reason: describe(err) });
    return 0;
  }
}

/** 서버 문서 정리(best-effort). 실패는 **로그만** — 사용자가 할 조치가 없다. */
async function cleanupServerFrame(deps: FrameSaveDeps, id: string): Promise<void> {
  try {
    const deleted = await deps.deleteServerFrame(id);
    if (!deleted) logger.warn("고아 프레임 문서 정리 실패(문서 미발견)", { orphanFrameId: id });
  } catch (err) {
    logger.warn("고아 프레임 문서 정리 실패", { orphanFrameId: id, reason: describe(err) });
  }
}

export async function runFrameSave(
  deps: FrameSaveDeps,
  request: FrameSaveRequest,
): Promise<FrameSaveOutcome> {
  // ① 검증 **재실행** — 진입점이 2개이므로 실제 저장 함수의 첫 줄에서 다시 한다(FR-10).
  const existingNames = await safeScopeNames(deps);
  const personalCount = await safePersonalCount(deps);
  const validation = validateFrameSave({
    role: request.role,
    sessionSource: request.sessionSource,
    hasImage: request.png !== null,
    slots: request.slots,
    frameWidth: request.imageSize.width,
    frameHeight: request.imageSize.height,
    name: request.name,
    sourceName: request.sourceName,
    existingNames,
    personalCount,
  });
  if (!validation.ok) {
    const reason = validation.reason ?? "invalid-slots";
    deps.setStatus(frameSaveRejectionMessage(reason));
    return { status: "rejected", reason };
  }

  // ② 지역 확정. `png`는 ③(hasImage)을 통과했으므로 비null이다.
  const png = request.png as Blob;
  const scope = frameSaveScope(request.role);
  // ⚠️ 오버레이 노출 축과 **같은 함수**를 다시 부른다(FR-11) — 파생값을 쓰면 두 축이 갈라진다.
  const register =
    requiresServerRegisterPrompt(request.role, request.sessionSource) && request.registerToServer;

  // ③
  deps.setStatus(STRINGS.frameEditor.saving);

  // ④ 서버 등록(2단계). 하나라도 실패하면 로컬 저장에 도달하지 않는다.
  let dbId: string | null = null;
  if (register) {
    // ④a `_` 하드 거부 — 서버가 400을 줄 값을 보내지 않는다(왕복 낭비·성공 오인 방지).
    if (!validateFrameNameForServer(request.name).ok) {
      deps.setStatus(STRINGS.frames.nameUnderscoreRejected);
      return { status: "server-failed", detail: "name-underscore" };
    }

    // ④b 문서 생성
    let created: CreateFrameResponse;
    try {
      created = await deps.createServerFrame({
        name: request.name,
        imageSize: request.imageSize,
        slots: request.slots,
        ext: "png",
        contentType: "image/png",
      });
    } catch (err) {
      const detail = describe(err);
      logger.error("프레임 서버 등록 실패(문서 생성)", { reason: detail });
      deps.setStatus(formatCount(STRINGS.frameEditor.registerFailed, detail));
      return { status: "server-failed", detail };
    }

    if (created.frame === null || created.frame.id.length === 0) {
      const detail = STRINGS.error.server;
      logger.error("프레임 서버 등록 실패(응답에 문서가 없음)");
      deps.setStatus(formatCount(STRINGS.frameEditor.registerFailed, detail));
      return { status: "server-failed", detail };
    }
    dbId = created.frame.id;

    // ④c 이미지 업로드 URL이 없으면 **실패로 본다** — 문서만 생기고 이미지가 없으면
    //     `GET /frames/default`가 그 프레임을 계속 내려줘 모든 키오스크에서 영구
    //     "불러올 수 없음" 카드가 된다.
    if (created.putUrl === null) {
      await cleanupServerFrame(deps, dbId);
      const detail = STRINGS.error.server;
      logger.error("프레임 서버 등록 실패(업로드 URL 없음)", { orphanFrameId: dbId });
      deps.setStatus(formatCount(STRINGS.frameEditor.registerFailed, detail));
      return { status: "server-failed", detail };
    }

    // ④d 서명 PUT — `requiredHeaders`는 순회해 전부 부착된다(M14 — `uploadGateway.put`).
    const put = await deps.putImage({
      url: created.putUrl,
      body: png,
      headers: created.requiredHeaders,
    });
    if (!put.ok) {
      await cleanupServerFrame(deps, dbId);
      const detail = `${put.failure}${put.status === null ? "" : ` (${put.status})`}`;
      logger.error("프레임 서버 등록 실패(이미지 업로드)", {
        orphanFrameId: dbId,
        failure: put.failure,
        status: put.status,
      });
      deps.setStatus(formatCount(STRINGS.frameEditor.registerFailed, detail));
      return { status: "server-failed", detail };
    }
  }

  // ⑤ 로컬 저장
  const saved = await deps.saveLocal({
    scope: scope === "public" ? "public" : "user",
    ownerId: scope === "public" ? null : request.userId,
    name: request.name,
    dbId: register ? dbId : null,
    imageSize: request.imageSize,
    slots: request.slots,
    bytes: png,
  });
  if (saved === null) {
    // ⚠️ 이때 서버 문서는 이미 만들어졌다(register 경로). **정리하지 않는다** — 선검증이 로컬 실패
    //    사유를 사전에 제거했으므로 여기 도달은 저장소 장애이고, 그 상황에서 네트워크 정리까지
    //    시도하면 사용자에게 두 개의 실패를 겹쳐 보여준다. 로그만 남기고 문구는 하나만 낸다.
    logger.error("프레임 로컬 저장 실패", { registeredToServer: register, dbId });
    deps.setStatus(STRINGS.frameEditor.saveLocalFailed);
    return { status: "local-failed" };
  }

  // ⑥
  deps.setStatus("");
  deps.goToFrameSelect();
  return { status: "saved", registeredToServer: register };
}
