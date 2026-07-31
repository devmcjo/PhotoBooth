import { createStore } from "zustand/vanilla";
import { subscribeWithSelector } from "zustand/middleware";
import { useStore } from "zustand";
import type { SessionUser } from "@domain/accounts/sessionUser";
import {
  createEmptySession,
  type CaptureSessionState,
} from "@domain/capture/captureSession";
import type { FilterKind } from "@domain/filters/filterParams";

/**
 * 세션 컨텍스트 — `currentUser` + 촬영 세션 데이터의 **단일 소스** (02 §5)
 *
 * ⚠️ **`subscribeWithSelector` 미들웨어가 필수다.** 없으면 `subscribe(selector, listener)`의
 *    두 번째 인자를 Zustand가 **조용히 무시**해서 M1(토큰 폐기) 구독이 한 번도 실행되지 않는다.
 *    `authStore.test.ts`가 이 배선을 기계적으로 고정한다.
 *
 * ⚠️ `currentUser` 변경 진입점은 **`login` / `logout`뿐**이다. 다른 경로를 만들면
 *    M1 구독이 덮지 못하는 사각이 생긴다.
 */

/** 컷 1개. 실제 픽셀은 OPFS에 있고 여기서는 참조만 든다(모바일 메모리 한계 — WR8). */
export interface CapturedCut {
  readonly index: number;
  /** OPFS 세션 폴더 기준 상대 경로(`cut1.jpg`). */
  readonly fileName: string;
  /** 썸네일. 컷 선택 그리드용. 폐기 시 `close()` 한다. */
  readonly thumbnail?: ImageBitmap;
}

export interface SessionState {
  readonly currentUser: SessionUser | null;
  readonly session: CaptureSessionState<CapturedCut>;
  /** 세션 ID(`{yyyyMMdd}_{HHmmss}_{uuid}` — M13). 촬영 시작 시 발급. */
  readonly sessionId: string | null;
  readonly selectedFilter: FilterKind;

  /** 로그인. **유일한 사용자 설정 경로**다. */
  login(user: SessionUser): void;
  /** 로그아웃. 구독이 토큰을 폐기한다(M1) — 여기서 토큰을 직접 지우지 않는다. */
  logout(): void;
  setSession(session: CaptureSessionState<CapturedCut>): void;
  setSessionId(sessionId: string | null): void;
  setFilter(filter: FilterKind): void;
  /**
   * 촬영 데이터만 폐기한다(로그인 유지 — M3).
   * 홈 복귀·유휴 만료·전역 예외 복구가 모두 이 경로를 쓴다.
   */
  discardCaptureData(): void;
}

function releaseThumbnails(session: CaptureSessionState<CapturedCut>): void {
  for (const cut of session.cuts) {
    // ImageBitmap을 닫지 않으면 10컷 세션을 반복할 때 모바일에서 메모리가 쌓인다(WR8).
    cut.thumbnail?.close();
  }
}

export const sessionStore = createStore<SessionState>()(
  subscribeWithSelector((set, get) => ({
    currentUser: null,
    session: createEmptySession<CapturedCut>(),
    sessionId: null,
    selectedFilter: "None",

    login(user) {
      set({ currentUser: user });
    },

    logout() {
      set({ currentUser: null });
      get().discardCaptureData();
    },

    setSession(session) {
      set({ session });
    },

    setSessionId(sessionId) {
      set({ sessionId });
    },

    setFilter(filter) {
      set({ selectedFilter: filter });
    },

    discardCaptureData() {
      releaseThumbnails(get().session);
      set({
        session: createEmptySession<CapturedCut>(),
        sessionId: null,
        selectedFilter: "None",
      });
    },
  })),
);

/** React 훅. */
export function useSessionStore<T>(selector: (state: SessionState) => T): T {
  return useStore(sessionStore, selector);
}

/** 현재 사용자(React 밖에서 읽는 경로). 게스트면 null. */
export function currentUser(): SessionUser | null {
  return sessionStore.getState().currentUser;
}

export function isLoggedIn(): boolean {
  return sessionStore.getState().currentUser !== null;
}
