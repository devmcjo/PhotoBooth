import { useEffect, useState, type ReactNode } from "react";
import { Spinner } from "@ui/components";
import { STRINGS } from "@ui/strings";
import styles from "./screens.module.css";

/**
 * `/oauth2callback` 화면 — 07 §2.5
 *
 * ⚠️ **사용자 조작 요소가 0개다.** 버튼·링크를 두면 손님이 교환 중에 화면을 떠나
 * 세션이 반쯤 세워진 상태가 만들어진다. 200ms~2초 존재하는 부트스트랩 국면이라
 * `APP_STATES`에도 넣지 않는다(경로 분기는 `classifyRoute`가 담당한다 — 02 §3).
 */
export function OauthCallbackView() {
  return (
    <main className={styles.screen}>
      <Spinner label={STRINGS.login.processing} />
      <p className={styles.subtitle}>{STRINGS.login.processing}</p>
    </main>
  );
}

export interface OauthCallbackGateProps {
  /**
   * 콜백 처리 promise. **`main.tsx`가 이미 만들어 둔 것 하나**를 받는다 — 그래서 `<StrictMode>`가
   * effect를 두 번 붙여도 부수효과(저장소 소비·교환)는 1회다. 콜백 경로가 아니면 `null`.
   *
   * ⚠️ 이 promise는 **reject되지 않는다**(`main.tsx`가 흡수한다). 그래도 아래에서 거절 핸들러를
   *    붙여 두어 어떤 경우에도 스피너에 고착되지 않게 한다.
   */
  readonly pending: Promise<void> | null;
  readonly children: ReactNode;
}

/**
 * 콜백이 끝날 때까지 `<App>`을 **아예 마운트하지 않는다**(설계 §4.2).
 * 마운트하면 손님이 Home을 잠깐 보고 계정 라벨이 [로그인]→id로 튄다.
 */
export function OauthCallbackGate({ pending, children }: OauthCallbackGateProps) {
  const [done, setDone] = useState(pending === null);

  useEffect(() => {
    if (pending === null) {
      setDone(true);
      return;
    }
    // 언마운트 후 setState를 막는다(해제 경로).
    let alive = true;
    const finish = (): void => {
      if (alive) setDone(true);
    };
    void pending.then(finish, finish);
    return () => {
      alive = false;
    };
  }, [pending]);

  return done ? <>{children}</> : <OauthCallbackView />;
}
