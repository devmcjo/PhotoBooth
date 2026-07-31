import { useEffect } from "react";
import { startDoneAutoHome } from "@screens/done/doneAutoHome";
import { shellStore } from "@shell/shellStore";
import { Button } from "@ui/components";
import { STRINGS } from "@ui/strings";
import styles from "./screens.module.css";

/**
 * `Done` 화면 — 완료 (03 §10)
 *
 * 6초 **실경과** 후 자동으로 홈에 돌아간다. **로그아웃하지 않는다**(M3) —
 * 다음 손님이 아니라 같은 운영자가 이어서 쓰는 경우가 정상이다.
 */
export function DoneView({ appName }: { readonly appName: string }) {
  // 정리 함수 하나가 타이머와 `visibilitychange` 리스너를 함께 걷는다.
  useEffect(() => startDoneAutoHome(), []);

  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>{appName}</h1>
      <p className={styles.subtitle} aria-live="polite">
        {STRINGS.done.thanks}
      </p>
      <Button
        variant="primary"
        onClick={() => void shellStore.getState().returnHome("완료 화면에서 홈 선택")}
      >
        {STRINGS.done.goHome}
      </Button>
    </main>
  );
}
