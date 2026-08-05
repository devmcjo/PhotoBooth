import { authMethodLabel, type SessionUser } from "@domain/accounts/sessionUser";
import { roleLabel } from "@domain/roles/userRole";
import { STRINGS } from "@ui/strings";

/**
 * [내 정보] 표시 행 — 03 §13.1 (React 무관 · 순수)
 *
 * ⚠️ **출처는 로그인 응답의 `user` DTO 하나뿐**이다. 별도 "내 정보 조회" API가 없으므로
 *    (analysis/31 §10) 화면 진입이 서버를 조회하지 않는다.
 * ⚠️ 시각 서식은 **주입**한다 — 도메인이 아니지만 결정성을 위해 같은 규칙을 지킨다.
 */

export interface AccountInfoRow {
  readonly label: string;
  readonly value: string;
}

export interface AccountInfoDeps {
  /** ISO 8601 → 표시 문자열. 실패하면 빈 문자열을 돌려준다(호출측이 "알 수 없음"으로 접는다). */
  readonly formatDate: (iso: string) => string;
}

export function buildAccountInfoRows(
  user: SessionUser,
  deps: AccountInfoDeps,
): readonly AccountInfoRow[] {
  return [
    { label: STRINGS.account.id, value: user.id },
    { label: STRINGS.account.email, value: user.email ?? STRINGS.account.none },
    // 값은 도메인 `authMethodLabel`이 소유한다("Google SSO" / "알 수 없음" — 설계 §3.1).
    { label: STRINGS.account.authMethod, value: authMethodLabel(user) },
    { label: STRINGS.account.role, value: roleLabel(user.role) },
    { label: STRINGS.account.createdAt, value: formatCreatedAt(user.createdAt, deps) },
  ];
}

/** 빈 문자열·파싱 실패는 "알 수 없음"이다(서버가 `createdAt`을 비워 줄 수 있다). */
function formatCreatedAt(iso: string, deps: AccountInfoDeps): string {
  if (iso.trim().length === 0) return STRINGS.account.unknown;
  const formatted = deps.formatDate(iso);
  return formatted.trim().length === 0 ? STRINGS.account.unknown : formatted;
}

/**
 * 기본 서식 — 로컬 시각 `YYYY-MM-DD`. 파싱 실패는 **빈 문자열**이다(예외를 던지지 않는다).
 * `Intl`을 쓰지 않는 이유: 로케일·ICU 유무에 따라 결과가 흔들려 로그·문서와 대조하기 어렵다.
 */
export function formatIsoDate(iso: string): string {
  const time = Date.parse(iso);
  if (!Number.isFinite(time)) return "";
  const date = new Date(time);
  const pad = (value: number): string => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function defaultAccountInfoDeps(
  overrides: Partial<AccountInfoDeps> = {},
): AccountInfoDeps {
  return { formatDate: formatIsoDate, ...overrides };
}
