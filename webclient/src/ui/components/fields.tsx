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

/**
 * on/off 스위치 — WPF `ToggleSwitch`(track 52×30 r15 · thumb 원 24 · 히트 56×48).
 *
 * ⚠️ **색만으로 구분하지 않는다**: 켜짐/꺼짐은 **thumb의 좌우 위치**로도 드러난다(M4).
 *    종전 구현은 "켜짐"/"꺼짐" 문자 버튼이었으나 WPF와 형태가 달라 스위치로 교체했다.
 * ⚠️ **`transition`을 넣지 마라** — WPF는 즉시 스냅이다. 넣으면 "다르게 보인다".
 */
export function Toggle({ label, checked, disabled = false, onChange }: ToggleProps) {
  return (
    <button
      type="button"
      className={styles.toggle}
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      <span className={styles.toggleTrack} aria-hidden="true">
        <span className={styles.toggleThumb} />
      </span>
    </button>
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
          /*
           * ⚠️ `variant`를 주지 않는다 — 세그먼트 표현(WPF `Button.Segment`)은 `.choice`가
           *    통째로 소유하고, 선택 상태는 `aria-pressed`로만 갈린다. `variant="primary"`를
           *    함께 주면 두 규칙이 같은 specificity로 충돌해 번들 순서에 결과가 좌우된다.
           */
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

export interface SelectProps<T extends string> {
  readonly label: string;
  readonly value: T;
  readonly options: readonly ChoiceOption<T>[];
  readonly disabled?: boolean;
  readonly onChange: (next: T) => void;
}

/**
 * 네이티브 `<select>`. **사용자 관리 표의 역할 변경 전용**이다.
 *
 * ⚠️ `ChoiceGroup`(터치 우선 버튼 나열)을 쓰지 않는 이유: 행마다 버튼 4개를 깔면 표가 무너지고,
 *    사용자 관리는 손님이 아니라 **운영자** 화면이라 터치 우선 근거가 적용되지 않는다.
 * ⚠️ 값 비교는 값 자체로 한다(인덱스가 아니다 — B9와 동종 함정).
 * ⚠️ 옵션이 비면 렌더하지 않는 것이 호출측 책임이다(빈 콤보는 조작 가능해 보인다).
 */
export function Select<T extends string>({
  label,
  value,
  options,
  disabled = false,
  onChange,
}: SelectProps<T>) {
  return (
    <select
      className={styles.select}
      aria-label={label}
      value={value}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value as T)}
    >
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
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
