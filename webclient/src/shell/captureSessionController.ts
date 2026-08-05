import { beginSession } from "@domain/capture/captureSession";
import type { FrameTemplate } from "@domain/frames/types";
import { newSessionId } from "@domain/upload/uploadContract";
import { getOpfsClient } from "@adapters/storage/opfsClient";
import {
  createSessionWorkspace,
  type SessionWorkspace,
} from "@adapters/storage/sessionWorkspace";
import { logger } from "@adapters/storage/logStore";
import { configureShell } from "./shellStore";
import { sessionStore, type CapturedCut } from "./sessionStore";

/**
 * 촬영 세션 수명 관리 — 화면들이 공유하는 진입점
 *
 * ⚠️ **컷 수 해석은 `FrameSelect`의 [다음] 1곳에서만** 한다(VF-12 · WD19).
 *    `Guide`·`Capture`·전체 재촬영은 세션에 기록된 값을 읽기만 한다 — 재해석하면
 *    설정을 중간에 바꿨을 때 진행 중인 세션의 컷 수가 바뀐다.
 *
 * ⚠️ 세션 ID·UUID·시각은 **어댑터에서** 만든다(도메인은 주입받는다 — 01 §8).
 */

let workspace: SessionWorkspace | null = null;

/**
 * 프레임 확정 + 컷 수 해석(**유일한 해석 지점**).
 * @param configuredCutCount 설정의 의도값(6/8/10 또는 0=자동)
 */
export function fixFrameAndResolveCutCount(
  frame: FrameTemplate,
  configuredCutCount: number,
): void {
  const session = beginSession<CapturedCut>(frame, configuredCutCount);
  sessionStore.getState().setSession(session);
  logger.info("프레임 확정·컷 수 해석", {
    frameId: frame.id,
    slots: frame.slots.length,
    configuredCutCount,
    resolvedCutCount: session.cutCount,
    isAutoCutCount: session.isAutoCutCount,
  });
}

/**
 * 세션 작업 공간 생성(OPFS `sessions/{id}/`). 촬영 시작 직전에 부른다.
 * 홈 복귀 시 이 폴더를 지우도록 셸 훅을 등록한다(02 §2.5 2단계).
 */
export function createWorkspace(now: Date, uuid: string): SessionWorkspace {
  const sessionId = newSessionId(now, uuid);
  sessionStore.getState().setSessionId(sessionId);
  workspace = createSessionWorkspace(getOpfsClient(), sessionId);

  // 홈 복귀·유휴 만료·탭 hidden 어느 경로로 끝나도 잔재가 남지 않게 한다(WM4).
  configureShell({
    cleanupWorkspace: async () => {
      const target = workspace;
      workspace = null;
      if (target === null) return;
      const removed = await target.discard();
      logger.info("세션 작업 공간 정리", { sessionId: target.sessionId, removed });
    },
  });

  logger.info("세션 작업 공간 생성", { sessionId });
  return workspace;
}

export function currentWorkspace(): SessionWorkspace | null {
  return workspace;
}

/** 어댑터 경계에서 시각·난수를 만든다. */
export function newSessionArgs(): { now: Date; uuid: string } {
  return { now: new Date(), uuid: crypto.randomUUID() };
}
