import { useGoogleSignIn } from "@screens/login/useGoogleSignIn";
import { Button } from "@ui/components";
import { STRINGS } from "@ui/strings";
import styles from "./screens.module.css";

/**
 * `Login` 화면 — 03 §3 · 07 §3
 *
 * 버튼은 **[Google로 로그인] 1개**다. 비밀번호·회원가입·게스트 계속 버튼은 존재하지 않는다.
 *
 * ⚠️ `GoogleClientId`가 비면 버튼을 **통째로 숨기고** 정적 안내만 둔다. 이 상태에서도
 *    게스트 촬영은 정상 동작해야 한다.
 * ⚠️ **[닫기]는 항상 렌더한다** — 미구성·오류·리디렉트 중 어느 상태에서도 게스트 흐름으로
 *    복귀할 수 있어야 한다(03 §3 완료 기준).
 */
export function LoginView() {
  const { available, phase, notice, signIn, close } = useGoogleSignIn();
  const redirecting = phase === "redirecting";

  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>{STRINGS.login.title}</h1>

      {available ? (
        <Button variant="primary" disabled={redirecting} onClick={signIn}>
          {redirecting ? STRINGS.login.redirecting : STRINGS.login.google}
        </Button>
      ) : (
        <p className={styles.subtitle}>{STRINGS.login.errors.clientNotConfigured}</p>
      )}

      {/*
        영역을 **항상** 렌더한다 — `aria-live`는 요소가 이미 있어야 변경을 읽어 준다.
        오류가 없을 때는 빈 문자열이다.
      */}
      <p className={styles.note} aria-live="polite">
        {notice === null ? "" : STRINGS.login.errors[notice]}
      </p>

      <div className={styles.actions}>
        <Button variant="ghost" onClick={close}>
          {STRINGS.common.close}
        </Button>
      </div>
    </main>
  );
}
