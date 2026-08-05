/**
 * 로그 정책(순수) — WD6 · 05 §7 · analysis/41 §8
 *
 * 마스킹·보관 한도 판정은 부작용이 없어 단위 테스트로 고정한다.
 * IndexedDB 접근은 `logStore.ts`가 담당한다.
 */

export const LOG_LEVELS = ["info", "warn", "error", "fatal"] as const;
export type LogLevel = (typeof LOG_LEVELS)[number];

export interface LogEntry {
  /** epoch ms. */
  readonly ts: number;
  readonly level: LogLevel;
  readonly msg: string;
  readonly ctx?: Readonly<Record<string, unknown>>;
}

/** 보관 한도: 14일 **또는** 5,000건 중 먼저 걸리는 기준. */
export const LOG_MAX_AGE_MS = 14 * 24 * 60 * 60 * 1000;
export const LOG_MAX_ENTRIES = 5000;

/** 배치 flush 조건: 20건 또는 1초. 로깅이 촬영 성능을 깎지 않게 한다. */
export const LOG_FLUSH_COUNT = 20;
export const LOG_FLUSH_INTERVAL_MS = 1000;

/**
 * 로그에 절대 남기지 않는 컨텍스트 키(analysis/41 §8).
 * 비교는 **소문자·구분자 제거 후**라서 `apiKey`·`api_key`·`API-KEY`가 모두 걸린다.
 */
const FORBIDDEN_CTX_KEYS: readonly string[] = [
  "token",
  "accesstoken",
  "idtoken",
  "jwt",
  "authorization",
  "apikey",
  "backendapikey",
  "clientkey",
  "gatekey",
  "code",
  "authcode",
  "codeverifier",
  "state",
  "nonce",
  "pin",
  "newpin",
  "currentpin",
  "password",
  "secret",
  "clientsecret",
  "signedurl",
  "puturl",
  "downloadurl",
];

export const MASK = "[masked]";

function normalizeKey(key: string): string {
  return key.toLowerCase().replace(/[-_\s]/g, "");
}

/** 이 컨텍스트 키는 값을 기록하면 안 되는가. */
export function isForbiddenCtxKey(key: string): boolean {
  return FORBIDDEN_CTX_KEYS.includes(normalizeKey(key));
}

/** 메시지 본문에서 흔한 시크릿 패턴을 가린다(키 기반 마스킹의 그물을 빠져나온 것 대비). */
export function maskMessage(msg: string): string {
  return (
    msg
      // `Bearer eyJ...`
      .replace(/\bBearer\s+[A-Za-z0-9\-._~+/]+=*/gi, `Bearer ${MASK}`)
      // JWT 3분절
      .replace(/\beyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+/g, MASK)
      // 서명 URL의 쿼리(토큰이 들어 있다)
      .replace(/([?&](?:token|X-Goog-Signature|Signature|sig)=)[^&\s]+/gi, `$1${MASK}`)
  );
}

/**
 * 컨텍스트를 마스킹한다(재귀). 금지 키는 값을 `[masked]`로 바꾸고, 값이 문자열이면 패턴 마스킹도 적용한다.
 * 순환 참조가 있어도 죽지 않는다(로깅이 앱을 죽이면 안 된다).
 */
export function maskCtx(
  ctx: Readonly<Record<string, unknown>>,
  seen: WeakSet<object> = new WeakSet(),
): Record<string, unknown> {
  // 자기 자신을 먼저 등록한다 — 첫 자기 참조부터 `[circular]`로 끊긴다.
  seen.add(ctx);
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(ctx)) {
    if (isForbiddenCtxKey(key)) {
      result[key] = MASK;
      continue;
    }
    if (typeof value === "string") {
      result[key] = maskMessage(value);
      continue;
    }
    if (typeof value === "object" && value !== null) {
      if (seen.has(value)) {
        result[key] = "[circular]";
        continue;
      }
      seen.add(value);
      if (Array.isArray(value)) {
        result[key] = value.map((item) =>
          typeof item === "string"
            ? maskMessage(item)
            : typeof item === "object" && item !== null
              ? maskCtx(item as Record<string, unknown>, seen)
              : item,
        );
      } else {
        result[key] = maskCtx(value as Record<string, unknown>, seen);
      }
      continue;
    }
    result[key] = value;
  }
  return result;
}

/** 기록 직전 정규화: 메시지·컨텍스트 마스킹. */
export function sanitizeEntry(entry: LogEntry): LogEntry {
  return {
    ts: entry.ts,
    level: entry.level,
    msg: maskMessage(entry.msg),
    ...(entry.ctx === undefined ? {} : { ctx: maskCtx(entry.ctx) }),
  };
}

/**
 * 링버퍼 정리 — **오래된 것부터** 폐기한다.
 * @returns 남길 항목(입력 순서 유지). 입력은 시간 오름차순이어야 한다.
 */
export function pruneEntries(
  entries: readonly LogEntry[],
  now: number,
  limits: { maxAgeMs?: number; maxEntries?: number } = {},
): LogEntry[] {
  const maxAgeMs = limits.maxAgeMs ?? LOG_MAX_AGE_MS;
  const maxEntries = limits.maxEntries ?? LOG_MAX_ENTRIES;
  const cutoff = now - maxAgeMs;

  const fresh = entries.filter((e) => e.ts >= cutoff);
  return fresh.length > maxEntries ? fresh.slice(fresh.length - maxEntries) : fresh;
}

/** 배치 flush 시점 판정. */
export function shouldFlush(pendingCount: number, elapsedMs: number): boolean {
  if (pendingCount === 0) return false;
  return pendingCount >= LOG_FLUSH_COUNT || elapsedMs >= LOG_FLUSH_INTERVAL_MS;
}

/** 내보내기 텍스트 1줄. `mcphoto-log-{YYMMDD_HHMM}.log`의 한 행. */
export function formatLogLine(entry: LogEntry): string {
  const iso = new Date(entry.ts).toISOString();
  const ctx = entry.ctx === undefined ? "" : ` ${JSON.stringify(entry.ctx)}`;
  return `${iso} [${entry.level.toUpperCase()}] ${entry.msg}${ctx}`;
}

export function formatLogText(entries: readonly LogEntry[]): string {
  return entries.map(formatLogLine).join("\n") + (entries.length > 0 ? "\n" : "");
}
