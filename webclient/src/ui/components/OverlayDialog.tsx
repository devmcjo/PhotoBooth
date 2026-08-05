import { useEffect, type ReactNode } from "react";
import styles from "./components.module.css";

/**
 * 화면 로컬 오버레이의 공통 껍데기 — 03 §790
 *
 * ⚠️ **셸 모달(`Modal`)이 아니다.** `pushModal`/`popModal`을 부르지 않으므로 유휴 경고(셸 모달)가
 *    언제나 이 위에 그려진다. 같은 화면의 오버레이는 동시에 뜨지 않는다(호출측이 상호배타
 *    단일 필드로 관리한다).
 * ⚠️ `Esc`는 **자체 keydown**으로 처리한다 — 셸 `Modal`의 내장 Esc는 `popModal`을 부르는데
 *    여기엔 스택 항목이 없어 엉뚱한 모달이 닫힌다.
 * ⚠️ 배경 클릭으로 닫지 않는다(오조작 방지 — 02 §10).
 */

export interface OverlayDialogProps {
  readonly title: string;
  /** `Esc`·[취소] 공통 처리. */
  readonly onCancel: () => void;
  /** 진입 포커스 대상의 DOM id. **파괴적 액션에 기본 포커스를 주지 않는다.** */
  readonly initialFocusId: string;
  readonly children?: ReactNode;
  readonly actions?: ReactNode;
  readonly className?: string;
}

export function OverlayDialog({
  title,
  onCancel,
  initialFocusId,
  children,
  actions,
  className,
}: OverlayDialogProps) {
  useEffect(() => {
    document.getElementById(initialFocusId)?.focus();

    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      onCancel();
    };
    document.addEventListener("keydown", onKeyDown);
    // cleanup 누락 0 — 오버레이가 닫힌 뒤에도 리스너가 남으면 Esc가 유령 동작을 한다.
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [initialFocusId, onCancel]);

  return (
    <div className={styles.overlayScrim}>
      <div
        className={[styles.overlayDialog, className].filter(Boolean).join(" ")}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <h2 className={styles.overlayTitle}>{title}</h2>
        {children}
        {actions !== undefined && <div className={styles.overlayActions}>{actions}</div>}
      </div>
    </div>
  );
}
