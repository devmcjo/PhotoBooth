import { createStore } from "zustand/vanilla";
import { subscribeWithSelector } from "zustand/middleware";
import { useStore } from "zustand";
import type { SessionUser } from "@domain/accounts/sessionUser";
import {
  createEmptySession,
  type CaptureSessionState,
} from "@domain/capture/captureSession";
import type { FilterKind } from "@domain/filters/filterParams";
import type { OutputFormat } from "@domain/settings/appSettings";

/**
 * 세션 컨텍스트 — `currentUser` + 촬영 세션 데이터의 **단일 소스** (02 §5)
 *
 * ⚠️ **`subscribeWithSelector` 미들웨어가 필수다.** 없으면 `subscribe(selector, listener)`의
 *    두 번째 인자를 Zustand가 **조용히 무시**해서 M1(토큰 폐기) 구독이 한 번도 실행되지 않는다.
 *    `authStore.test.ts`가 이 배선을 기계적으로 고정한다.
 *
 * ⚠️ `currentUser` 변경 진입점은 **`login` / `logout` / `expireSession`뿐**이다. 규칙의 요지는
 *    "진입점 개수"가 아니라 **`currentUser` 필드를 통해서만 바꾼다**는 것이다(02 §5.1) —
 *    그래야 M1 구독 한 곳이 모든 경로를 덮는다.
 */

/** 컷 1개. 실제 픽셀은 OPFS에 있고 여기서는 참조만 든다(모바일 메모리 한계 — WR8). */
export interface CapturedCut {
  readonly index: number;
  /** OPFS 세션 폴더 기준 상대 경로(`cut1.jpg`). */
  readonly fileName: string;
  /** 썸네일. 컷 선택 그리드용. 폐기 시 `close()` 한다. */
  readonly thumbnail?: ImageBitmap;
}

/**
 * 합성 결과물 인계분.
 *
 * `useResultCompose`는 Blob을 React ref에 들고 있어 `Result`가 언마운트되면 접근 경로가
 * 사라진다. 업로드는 `Qr` 화면이 하므로(03 §9.1) **합성 결과만** 세션 컨텍스트로 올린다.
 * (타임랩스는 싱글턴 서비스가 들고 있어 인계가 필요 없다.)
 */
export interface FinalImageArtifact {
  readonly blob: Blob;
  /**
   * 합성 시점의 출력 포맷. prepare의 `ext`·`contentType`은 **이 값**을 따라야 한다 —
   * `Result → Settings → Result` 왕복으로 설정이 바뀌어도 이미 만들어진 바이트와
   * `Content-Type` 선언이 어긋나지 않게 한다.
   */
  readonly format: OutputFormat;
}

export interface SessionState {
  readonly currentUser: SessionUser | null;
  readonly session: CaptureSessionState<CapturedCut>;
  /** 세션 ID(`{yyyyMMdd}_{HHmmss}_{uuid}` — M13). 촬영 시작 시 발급. */
  readonly sessionId: string | null;
  readonly selectedFilter: FilterKind;
  /** 합성 결과 인계분(`Qr` 화면의 업로드 입력). 합성 전·폐기 후에는 null. */
  readonly finalImage: FinalImageArtifact | null;

  /** 로그인. **유일한 사용자 설정 경로**다. */
  login(user: SessionUser): void;
  /** 로그아웃. 구독이 토큰을 폐기한다(M1) — 여기서 토큰을 직접 지우지 않는다. */
  logout(): void;
  /**
   * JWT 만료 감지(401) → **사용자만** 해제한다.
   *
   * ⚠️ **`discardCaptureData()`를 부르지 않는다.** [02 §5.2] 매트릭스가 "JWT 만료 감지" 행의
   *    촬영 데이터를 **유지**로 못박고, [07 §4.3]도 "촬영·합성·로컬 보관은 계속된다"고 쓴다.
   *    401이 가장 잘 나는 지점이 `Qr` 화면의 업로드인데 거기서 `finalImage`를 버리면
   *    [기기에 저장]까지 죽는다 — 결과물이 로컬에 남아 있음을 알려야 하는 규격과 정반대다.
   * ⚠️ 토큰은 여기서 지우지 않는다 — `currentUser`가 null이 되면 M1 구독이 폐기한다.
   */
  expireSession(): void;
  setSession(session: CaptureSessionState<CapturedCut>): void;
  setSessionId(sessionId: string | null): void;
  setFilter(filter: FilterKind): void;
  /** 합성 성공마다 교체한다. `Blob`은 `ImageBitmap`과 달리 명시 해제가 없다(GC). */
  setFinalImage(artifact: FinalImageArtifact | null): void;
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
    finalImage: null,

    login(user) {
      set({ currentUser: user });
    },

    logout() {
      set({ currentUser: null });
      get().discardCaptureData();
    },

    expireSession() {
      // 촬영 데이터는 그대로 둔다(02 §5.2) — 게스트가 되어 QR만 사라진다.
      set({ currentUser: null });
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

    setFinalImage(artifact) {
      set({ finalImage: artifact });
    },

    discardCaptureData() {
      releaseThumbnails(get().session);
      set({
        session: createEmptySession<CapturedCut>(),
        sessionId: null,
        selectedFilter: "None",
        // 인계분도 함께 버린다 — 다음 세션이 이전 사진을 올리면 안 된다.
        finalImage: null,
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
