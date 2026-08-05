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
/** 진단 모달 [마지막 로그인 실패] 행의 값. 사유 열거형 + 시각뿐이다(email·token·code 없음 — AUTH-3). */
export interface LastLoginFailure {
  readonly reason: LoginFailureReason;
  readonly at: number;
}

export interface LoginState {
  readonly notice: LoginFailureReason | null;
  /**
   * 마지막 로그인 실패 흔적 — **진단 전용**. `notice`와 다른 축이다.
   *
   * ⚠️ `clear()`가 지우지 않는다: `notice`는 `Login` 화면이 마운트하면서 소비·소거하는데,
   *    진단 흔적이 화면을 여는 것만으로 사라지면 쓸모가 없다. **로그인 성공에서만** null이 된다.
   * ⚠️ 메모리 전용(M2 정신 — 저장소 API를 쓰지 않는다). 새로고침하면 사라진다.
   */
  readonly lastFailure: LastLoginFailure | null;
  /**
   * @param at 기록 시각(ms). **주입 가능**하게 열어 둔다(15 §3.2 — 시간은 주입한다).
   *   기존 `fail(reason)` 호출부를 고치지 않기 위해 기본값만 `Date.now()`다.
   */
  fail(reason: LoginFailureReason, at?: number): void;
  clear(): void;
  /** 로그인 성공 시 진단 흔적을 지운다. */
  clearLastFailure(): void;
}

export const loginStore = createStore<LoginState>()((set) => ({
  notice: null,
  lastFailure: null,

  fail(reason, at = Date.now()) {
    set({ notice: reason, lastFailure: { reason, at } });
  },

  clear() {
    // ⚠️ `lastFailure`는 건드리지 않는다(위 주석 참고).
    set({ notice: null });
  },

  clearLastFailure() {
    set({ lastFailure: null });
  },
}));

/** React 훅. */
export function useLoginStore<T>(selector: (state: LoginState) => T): T {
  return useStore(loginStore, selector);
}
