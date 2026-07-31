import { PIN_LENGTH } from "@domain/auth/pinGatePolicy";
import { Button } from "./index";
import { formatCount, STRINGS } from "@ui/strings";
import styles from "./components.module.css";

/**
 * PIN 입력 키패드 — **표현만** 하는 컴포넌트 (03 §15.3)
 *
 * `PinPromptModal`(진입 게이트)·`Account`(PIN 변경)·`UserMgmt`(타 계정 재설정)가 공유한다.
 *
 * ⚠️ **판정·서버 왕복·잠금 기록을 여기에 넣지 마라.** 그 셋은 각각 `pinPromptRunner`·
 *    `pinChangeRunner`·`pinResetRunner`(React 무관)가 소유한다 — jsdom이 없어 컴포넌트에
 *    들어간 규칙은 영원히 검증되지 않는다(15 §3.1).
 * ⚠️ `<input>`을 쓰지 않는다 — `autocomplete`·비밀번호 관리자 노출면 자체를 만들지 않는다.
 * ⚠️ 4자리가 차도 **자동 제출하지 않는다**. 오입력 1회가 곧 실패 카운트라 실수 여지를 줄인다.
 * ⚠️ PIN 값은 `value.length`로만 쓴다 — 문자열을 화면에 그리지 않는다.
 */

const KEYPAD_DIGITS = ["1", "2", "3", "4", "5", "6", "7", "8", "9"] as const;

export interface PinKeypadProps {
  /** 현재 입력 버퍼. 길이만 표시에 쓴다. */
  readonly value: string;
  readonly disabled?: boolean;
  readonly onDigit: (digit: string) => void;
  readonly onBackspace: () => void;
  readonly onSubmit: () => void;
  readonly submitLabel?: string;
}

export function PinKeypad({
  value,
  disabled = false,
  onDigit,
  onBackspace,
  onSubmit,
  submitLabel = STRINGS.pin.confirm,
}: PinKeypadProps) {
  return (
    <div className={styles.pinPad}>
      <div
        className={styles.pinIndicator}
        role="img"
        aria-label={formatCount(STRINGS.pin.indicator, value.length)}
      >
        {Array.from({ length: PIN_LENGTH }, (_, index) => (
          <span
            key={index}
            className={[styles.pinDot, index < value.length ? styles.pinDotFilled : ""]
              .filter(Boolean)
              .join(" ")}
          />
        ))}
      </div>

      <div className={styles.pinKeypad}>
        {KEYPAD_DIGITS.map((digit) => (
          <Button
            key={digit}
            className={styles.pinKey}
            aria-label={digit}
            disabled={disabled}
            onClick={() => onDigit(digit)}
          >
            {digit}
          </Button>
        ))}
        <Button
          className={[styles.pinKey, styles.pinKeyWide].join(" ")}
          aria-label={STRINGS.pin.backspace}
          disabled={disabled}
          onClick={() => onBackspace()}
        >
          {STRINGS.pin.backspace}
        </Button>
        <Button
          className={styles.pinKey}
          aria-label="0"
          disabled={disabled}
          onClick={() => onDigit("0")}
        >
          0
        </Button>
        <Button
          variant="primary"
          className={[styles.pinKey, styles.pinKeyWide].join(" ")}
          aria-label={submitLabel}
          disabled={disabled || value.length !== PIN_LENGTH}
          onClick={() => onSubmit()}
        >
          {submitLabel}
        </Button>
      </div>
    </div>
  );
}
