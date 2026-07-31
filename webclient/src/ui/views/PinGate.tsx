import { useEffect, type ReactNode } from "react";
import type { AppState } from "@domain/navigation/appState";
import { ensureScreenPinGate, usePinGateStatus } from "@shell/pinGate";
import { Spinner } from "@ui/components";
import styles from "./screens.module.css";

/**
 * 진입 PIN 렌더 게이트 — 07 §6.1
 *
 * 통과하지 못하면 `children`이 **마운트조차 되지 않는다** → 설정값이 화면에 노출되지 않는다.
 * 네비게이션 래퍼가 아니라 렌더 게이트인 이유: OAuth 복귀(`returnTo="Settings"`)처럼
 * `go()`를 거치지 않는 진입로가 실재하고, 호출부마다 게이트를 붙이면 반드시 하나가 빠진다.
 *
 * ⚠️ **effect에 cleanup을 두지 마라.** `<StrictMode>`의 이중 effect가 1회차를 취소해
 *    사용자가 설정 화면에서 즉시 튕겨 나간다(15 §6). 승인 폐기는 `installPinGateLifecycle`이
 *    화면·사용자 변경을 구독해 처리한다 — 그것이 규격의 "매번 확인"이다.
 */
export function PinGate({
  screen,
  children,
}: {
  readonly screen: AppState;
  readonly children: ReactNode;
}) {
  const status = usePinGateStatus(screen);

  useEffect(() => {
    // 멱등이다. StrictMode 2회차는 no-op.
    ensureScreenPinGate(screen);
  }, [screen]);

  if (status === "granted") return <>{children}</>;

  if (status === "checking") {
    return (
      <main className={styles.screen}>
        <Spinner />
      </main>
    );
  }

  // idle(첫 프레임) · denied — 셸이 직전 화면으로 되돌린다. 여기서는 아무것도 그리지 않는다.
  return null;
}
