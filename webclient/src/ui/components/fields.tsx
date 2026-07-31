import { useId, type ReactNode } from "react";
import { Button } from "./index";
import styles from "./fields.module.css";

/**
 * 설정 폼 컨트롤 — 03 §12 · 01 §8(터치 48px · `aria-describedby` · 색만으로 구분 금지)
 *
 * ⚠️ 잠금 표시는 **`disabled` + 문자 배지**를 함께 쓴다. 색만으로 구분하면 저시력·흑백에서
 *    "왜 안 눌리는지"를 알 수 없다.
 */

export interface SettingRowProps {
  readonly label: string;
  readonly description?: string;
  /** "로그인 필요" 같은 잠금 사유. 없으면 배지를 렌더하지 않는다. */
  readonly lockBadge?: string | null;
  readonly htmlFor?: string;
  readonly children: ReactNode;
}

export function SettingRow({
  label,
  description,
  lockBadge,
  htmlFor,
  children,
}: SettingRowProps) {
  return (
    <div className={styles.row}>
      <div className={styles.rowLabel}>
        {htmlFor === undefined ? (
          <span className={styles.labelText}>{label}</span>
        ) : (
          <label className={styles.labelText} htmlFor={htmlFor}>
            {label}
          </label>
        )}
        {lockBadge !== undefined && lockBadge !== null && (
          <span className={styles.lockBadge}>{lockBadge}</span>
        )}
        {description !== undefined && <p className={styles.description}>{description}</p>}
      </div>
      <div className={styles.rowControl}>{children}</div>
    </div>
  );
}

export interface ToggleProps {
  readonly label: string;
  readonly checked: boolean;
  readonly disabled?: boolean;
  readonly onChange: (next: boolean) => void;
}

/** on/off 토글. `role="switch"` + 문자 표시(색만으로 구분하지 않는다). */
export function Toggle({ label, checked, disabled = false, onChange }: ToggleProps) {
  return (
    <Button
      className={styles.toggle}
      role="switch"
      aria-checked={checked}
      aria-label={label}
      variant={checked ? "primary" : "secondary"}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      {checked ? "켜짐" : "꺼짐"}
    </Button>
  );
}

export interface ChoiceOption<T extends string | number> {
  readonly value: T;
  readonly label: string;
}

export interface ChoiceGroupProps<T extends string | number> {
  readonly label: string;
  readonly value: T;
  readonly options: readonly ChoiceOption<T>[];
  readonly disabled?: boolean;
  readonly onChange: (next: T) => void;
}

/**
 * 몇 개 안 되는 선택지를 버튼으로 나열한다(키오스크 터치 우선 — 드롭다운은 조작이 어렵다).
 *
 * ⚠️ 값 비교는 **값 자체**로 한다(인덱스가 아니다). 컷 수 "자동"은 sentinel `0`이라
 *    인덱스 기반으로 다루면 목록이 바뀔 때 조용히 다른 값이 선택된다(B9와 동종 함정).
 */
export function ChoiceGroup<T extends string | number>({
  label,
  value,
  options,
  disabled = false,
  onChange,
}: ChoiceGroupProps<T>) {
  return (
    <div className={styles.choiceGroup} role="group" aria-label={label}>
      {options.map((option) => (
        <Button
          key={String(option.value)}
          className={styles.choice}
          variant={option.value === value ? "primary" : "secondary"}
          aria-pressed={option.value === value}
          disabled={disabled}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </Button>
      ))}
    </div>
  );
}

export interface TextFieldProps {
  readonly label: string;
  readonly value: string;
  readonly disabled?: boolean;
  readonly placeholder?: string;
  readonly onChange: (next: string) => void;
}

export function TextField({
  label,
  value,
  disabled = false,
  placeholder,
  onChange,
}: TextFieldProps) {
  const id = useId();
  return (
    <input
      id={id}
      className={styles.textField}
      type="text"
      aria-label={label}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

export interface NumberStepperProps {
  readonly label: string;
  readonly value: number;
  readonly min: number;
  readonly max: number;
  readonly disabled?: boolean;
  readonly onChange: (next: number) => void;
}

/**
 * 정수 스테퍼(보관 시간 1~72h).
 *
 * ⚠️ 여기서 범위를 "보정"하지 않는다 — 버튼이 범위를 넘지 않게 막을 뿐이고, 최종 clamp는
 *    도메인(`clampSettings`)이 한다. 화면이 보정하면 진실원이 둘이 된다(SET-1).
 */
export function NumberStepper({
  label,
  value,
  min,
  max,
  disabled = false,
  onChange,
}: NumberStepperProps) {
  return (
    <div className={styles.stepper} role="group" aria-label={label}>
      <Button
        className={styles.stepperButton}
        aria-label={`${label} 줄이기`}
        disabled={disabled || value <= min}
        onClick={() => onChange(value - 1)}
      >
        −
      </Button>
      <output className={styles.stepperValue} aria-live="polite">
        {value}
      </output>
      <Button
        className={styles.stepperButton}
        aria-label={`${label} 늘리기`}
        disabled={disabled || value >= max}
        onClick={() => onChange(value + 1)}
      >
        ＋
      </Button>
    </div>
  );
}
