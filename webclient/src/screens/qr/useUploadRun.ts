import { useCallback, useEffect, useRef, useState } from "react";
import {
  defaultUploadRunDeps,
  runUpload,
  type UploadPhase,
} from "./uploadRunner";

/**
 * `Qr` 화면의 업로드 배선 — 03 §9.1
 *
 * ⚠️ **effect가 실행될 때마다 자기 `AbortController`를 만든다.** `<StrictMode>`는 개발 빌드에서
 *    effect를 2회 실행하는데, `runningRef` 같은 전역 잠금을 쓰면 두 번째 실행이 **영구히 스킵**된다.
 *    첫 실행은 cleanup에서 중단되고 두 번째가 정상 진행하는 것이 올바른 형태다.
 * ⚠️ cleanup의 `abort()`가 화면 이탈·[재시도] 시 진행 중 요청을 끊는다 —
 *    낭비 전송과 유령 commit(손님이 떠난 뒤 TempUser 카운트 소모)을 막는다.
 */

export interface UploadRun {
  readonly phase: UploadPhase;
  /** 0 = 최초, 1↑ = [재시도] 횟수. */
  readonly attempt: number;
  /** [재시도] — 새 세션 ID로 전 과정 재실행. 진행 중이면 이전 실행을 끊고 다시 시작한다. */
  retry(): void;
  /** [완료]로 떠나기 전 명시 중단. */
  cancel(): void;
}

export function useUploadRun(): UploadRun {
  const [attempt, setAttempt] = useState(0);
  const [phase, setPhase] = useState<UploadPhase>({ kind: "idle" });
  const controllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    controllerRef.current = controller;

    void runUpload({
      ...defaultUploadRunDeps(),
      attempt,
      signal: controller.signal,
      // 취소된 실행의 상태는 화면에 반영하지 않는다(언마운트 후 setState 금지).
      onPhase: (next) => {
        if (!controller.signal.aborted) setPhase(next);
      },
    });

    return () => controller.abort();
  }, [attempt]);

  const retry = useCallback(() => {
    setPhase({ kind: "idle" });
    setAttempt((value) => value + 1);
  }, []);

  const cancel = useCallback(() => {
    controllerRef.current?.abort();
  }, []);

  return { phase, attempt, retry, cancel };
}
