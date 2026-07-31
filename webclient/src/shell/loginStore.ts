import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import type { LoginFailureReason } from "@domain/auth/loginFailure";

/**
 * 로그인 오류 전달 — 07 §2.5 "실패 시 오류 문구를 `Login` 화면에 실어 전달한다"
 *
 * 왜 스토어인가: 실패는 **콜백 처리(React 밖·부트스트랩)** 에서도 발생하는데, 그때 `Login`
 * 화면은 아직 마운트되지 않았다. props로 전달할 경로가 없어 셸 상태로 둔다.
 *
 * ⚠️ 사유(진단축 6종)를 그대로 들고, 문구 접기(`loginFailureMessageKey`)는 **화면이** 한다 —
 *    스토어가 문구를 들면 400과 네트워크를 로그에서 가를 수 없게 된다.
 */
export interface LoginState {
  readonly notice: LoginFailureReason | null;
  fail(reason: LoginFailureReason): void;
  clear(): void;
}

export const loginStore = createStore<LoginState>()((set) => ({
  notice: null,

  fail(reason) {
    set({ notice: reason });
  },

  clear() {
    set({ notice: null });
  },
}));

/** React 훅. */
export function useLoginStore<T>(selector: (state: LoginState) => T): T {
  return useStore(loginStore, selector);
}
