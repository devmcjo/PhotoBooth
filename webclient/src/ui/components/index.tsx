import {
  useEffect,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ReactNode,
} from "react";
import type { AppState } from "@domain/navigation/appState";
import { shellStore, useShellStore, type ModalId, type Toast } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";
import styles from "./components.module.css";

/**
 * 공통 UI 컴포넌트 — 03 §1
 * 터치 타깃 48px · `aria` 규격 · 다크/라이트 · `prefers-reduced-motion`은 CSS가 담당한다.
 */

type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  readonly variant?: ButtonVariant;
}

export function Button({ variant = "secondary", className, ...rest }: ButtonProps) {
  const variantClass =
    variant === "primary" ? styles.primary : variant === "danger" ? styles.danger : variant === "ghost" ? styles.ghost : "";
  return (
    <button
      type="button"
      className={[styles.button, variantClass, className].filter(Boolean).join(" ")}
      {...rest}
    />
  );
}

export function Spinner({ label = STRINGS.common.loading }: { readonly label?: string }) {
  return (
    <div role="status" aria-live="polite" aria-label={label}>
      <div className={styles.spinner} />
    </div>
  );
}

export interface ModalProps {
  readonly id: ModalId;
  readonly title: string;
  readonly dismissible?: boolean;
  readonly children?: ReactNode;
  readonly actions?: ReactNode;
}

/**
 * 모달. 배경 클릭으로 닫지 않는다(오조작 방지 — 02 §10).
 * `dismissible`이면 `Esc`로 닫힌다(PIN 입력). 유휴 경고는 버튼만이다.
 */
export function Modal({ id, title, dismissible = true, children, actions }: ModalProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const previousFocus = useRef<Element | null>(null);

  useEffect(() => {
    previousFocus.current = document.activeElement;
    // 진입 시 첫 포커스를 모달 안으로 옮긴다(포커스 트랩의 기본).
    dialogRef.current?.focus();

    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === "Escape" && dismissible) {
        event.preventDefault();
        shellStore.getState().popModal(id);
      }
    };
    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
      // 닫을 때 이전 포커스를 복원한다.
      (previousFocus.current as HTMLElement | null)?.focus?.();
    };
  }, [id, dismissible]);

  return (
    <div className={styles.scrim}>
      <div
        ref={dialogRef}
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`modal-title-${id}`}
        tabIndex={-1}
      >
        <h2 id={`modal-title-${id}`} className={styles.dialogTitle}>
          {title}
        </h2>
        {children}
        {actions !== undefined && <div className={styles.dialogActions}>{actions}</div>}
      </div>
    </div>
  );
}

/** 토스트 1개. 색만으로 구분하지 않도록 접두 기호를 함께 쓴다(M4). */
function ToastItem({ toast }: { readonly toast: Toast }) {
  useEffect(() => {
    const timer = setTimeout(
      () => shellStore.getState().dismissToast(toast.id),
      toast.durationMs,
    );
    return () => clearTimeout(timer);
  }, [toast.id, toast.durationMs]);

  const kindClass =
    toast.kind === "success" ? styles.toastSuccess : toast.kind === "error" ? styles.toastError : "";
  const prefix = toast.kind === "success" ? "✓" : toast.kind === "error" ? "✕" : "ℹ";

  return (
    <div className={[styles.toast, kindClass].filter(Boolean).join(" ")}>
      <span aria-hidden="true">{prefix} </span>
      {toast.message}
    </div>
  );
}

export function ToastHost() {
  const toasts = useShellStore((s) => s.toasts);
  return (
    <div className={styles.toastHost} role="status" aria-live="polite">
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} />
      ))}
    </div>
  );
}

/** 계정 팝오버 항목(표현용). 목록·권한 판정은 `screens/account/accountMenu`가 소유한다. */
export interface TopBarMenuItem<T extends string> {
  readonly id: T;
  readonly label: string;
}

export interface TopBarProps<T extends string> {
  readonly title: string;
  readonly accountLabel: string;
  /** 현재 화면. 바뀌면 팝오버를 닫는다(화면을 떠났는데 메뉴가 떠 있으면 안 된다). */
  readonly screen: AppState;
  /** **빈 배열이면 팝오버를 열지 않고** `onAccount()`를 곧바로 부른다(게스트 → Login). */
  readonly accountMenuItems: readonly TopBarMenuItem<T>[];
  readonly onAccountMenuSelect: (id: T) => void;
  readonly onAccount: () => void;
  readonly onSettings: () => void;
}

/**
 * 상단바. `Capture`·`Qr`에서는 렌더하지 않는다(호출측이 판정 — 02 §4).
 *
 * 계정 버튼은 **팝오버 3항목**(계정 관리 · 관리자 도구 · 로그아웃 — 02 §5.1)을 연다.
 * ⚠️ 권한 판정을 여기서 하지 않는다 — 항목 배열을 그대로 렌더할 뿐이다(ACC-1과 같은 정신).
 * ⚠️ 셸 모달 스택을 쓰지 않는다(`ModalId`를 늘리지 않는다 — FR-8 정신).
 */
export function TopBar<T extends string>({
  title,
  accountLabel,
  screen,
  accountMenuItems,
  onAccountMenuSelect,
  onAccount,
  onSettings,
}: TopBarProps<T>) {
  const [open, setOpen] = useState(false);
  const popoverRef = useRef<HTMLDivElement>(null);

  // 화면이 바뀌면 닫는다.
  useEffect(() => {
    setOpen(false);
  }, [screen]);

  // 항목이 사라지면(로그아웃) 닫는다 — 빈 팝오버가 남지 않게.
  useEffect(() => {
    if (accountMenuItems.length === 0) setOpen(false);
  }, [accountMenuItems.length]);

  useEffect(() => {
    if (!open) return () => undefined;
    // 열릴 때 첫 항목으로 포커스를 옮긴다(키보드 조작 가능).
    popoverRef.current?.querySelector("button")?.focus();

    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOpen(false);
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open]);

  const handleAccountClick = (): void => {
    if (accountMenuItems.length === 0) {
      onAccount();
      return;
    }
    setOpen((current) => !current);
  };

  return (
    <header className={styles.topBar}>
      <p className={styles.topBarTitle}>{title}</p>
      <div className={styles.topBarActions}>
        <div className={styles.popoverAnchor}>
          <Button
            variant="ghost"
            onClick={handleAccountClick}
            aria-label={`계정: ${accountLabel}`}
            aria-haspopup={accountMenuItems.length > 0 ? "menu" : undefined}
            aria-expanded={accountMenuItems.length > 0 ? open : undefined}
          >
            {accountLabel}
          </Button>

          {open && accountMenuItems.length > 0 && (
            <>
              {/* 바깥 클릭 닫기. 투명 backdrop이라 시야를 가리지 않는다. */}
              <button
                type="button"
                className={styles.popoverBackdrop}
                aria-label={STRINGS.common.close}
                onClick={() => setOpen(false)}
              />
              <div ref={popoverRef} className={styles.popover} role="menu">
                {accountMenuItems.map((item) => (
                  <Button
                    key={item.id}
                    variant="ghost"
                    className={styles.popoverItem}
                    role="menuitem"
                    onClick={() => {
                      setOpen(false);
                      onAccountMenuSelect(item.id);
                    }}
                  >
                    {item.label}
                  </Button>
                ))}
              </div>
            </>
          )}
        </div>

        <Button variant="ghost" onClick={onSettings}>
          {STRINGS.common.settings}
        </Button>
      </div>
    </header>
  );
}

export interface BannerProps {
  readonly message: string;
  readonly actionLabel: string;
  readonly onAction: () => void;
}

/** 전체화면 이탈 배너 — 촬영 흐름을 중단하지 않는다(WD7). */
export function Banner({ message, actionLabel, onAction }: BannerProps) {
  return (
    <div className={styles.banner} role="status">
      <span>{message}</span>
      <Button variant="ghost" onClick={onAction}>
        {actionLabel}
      </Button>
    </div>
  );
}
