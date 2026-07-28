/**
 * 입력 검증(순수 로직) — 서버가 전 필드를 신뢰 없이 검증한다(설계 §11).
 *
 * Admin 권한을 신뢰하던 클라 입력을 서버 경계에서 좁힌다: id 형식·역할 화이트리스트·슬롯 1~6·
 * imageSize·파일 kind/ext/contentType·retentionHours 범위. 실패는 400(입력검증)으로 매핑된다.
 */
import { isUserRole, UserRole } from "./roles";
import { OutputFormat } from "./session";

/** 검증 실패를 나타내는 결과형(유효하면 value, 아니면 error 메시지). */
export type ValidationResult<T> = { ok: true; value: T } | { ok: false; error: string };

const ok = <T>(value: T): ValidationResult<T> => ({ ok: true, value });
const fail = <T>(error: string): ValidationResult<T> => ({ ok: false, error });

/** 계정 id: 영숫자·`_`·`-`·`.`, 3~40자. 프레임 이름 규약과 별개(계정 id는 문서 ID). */
const ID_RE = /^[A-Za-z0-9._-]{3,40}$/;

export function validateAccountId(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("id는 문자열이어야 합니다.");
  const v = value.trim();
  if (!ID_RE.test(v))
    return fail("id 형식이 올바르지 않습니다(영숫자·. _ - 3~40자).");
  return ok(v);
}

/** 비밀번호: 1~200자 비어있지 않은 문자열(해싱 전 평문 길이만 방어적으로 제한). */
export function validatePassword(value: unknown): ValidationResult<string> {
  if (typeof value !== "string" || value.length === 0)
    return fail("비밀번호가 비어 있습니다.");
  if (value.length > 200) return fail("비밀번호가 너무 깁니다(최대 200자).");
  return ok(value);
}

/** 역할: 화이트리스트(temp_user/user/manager/admin). */
export function validateRole(value: unknown): ValidationResult<UserRole> {
  if (!isUserRole(value))
    return fail("역할이 올바르지 않습니다(temp_user/user/manager/admin).");
  return ok(value);
}

/**
 * 이메일: RFC 5322 간이 정규식 + 길이(≤254). 소문자 정규화 반환(설계 §8.6).
 * 엄밀한 RFC 파싱이 아니라 실무 방어 수준(공백·`@` 1개·도메인에 점 포함).
 */
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function validateEmail(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("이메일은 문자열이어야 합니다.");
  const v = value.trim().toLowerCase();
  if (v.length === 0) return fail("이메일이 비어 있습니다.");
  if (v.length > 254) return fail("이메일이 너무 깁니다(최대 254자).");
  if (!EMAIL_RE.test(v)) return fail("이메일 형식이 올바르지 않습니다.");
  return ok(v);
}

/** 인증/재설정 코드: 정확히 6자리 숫자(설계 §8.6). */
const CODE_RE = /^\d{6}$/;

export function validateVerificationCode(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("코드는 문자열이어야 합니다.");
  const v = value.trim();
  if (!CODE_RE.test(v)) return fail("코드는 6자리 숫자여야 합니다.");
  return ok(v);
}

// ─────────────────────────────────────────────────────────────────────────────
// item1b: Google SSO 입력 검증(순수) — /auth/google 요청 필드 경계 방어(설계 §5.2)
// ─────────────────────────────────────────────────────────────────────────────

/** authorization code: 비어있지 않은 문자열, 과길이 방어(≤2048). 형식은 Google이 정의하므로 길이만 방어. */
export function validateAuthCode(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("code는 문자열이어야 합니다.");
  const v = value.trim();
  if (v.length === 0) return fail("code가 비어 있습니다.");
  if (v.length > 2048) return fail("code가 너무 깁니다(최대 2048자).");
  return ok(v);
}

/** PKCE code_verifier: RFC 7636 — 43~128자, unreserved 문자 [A-Za-z0-9-._~]만. */
const CODE_VERIFIER_RE = /^[A-Za-z0-9\-._~]{43,128}$/;

export function validateCodeVerifier(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("codeVerifier는 문자열이어야 합니다.");
  const v = value.trim();
  if (!CODE_VERIFIER_RE.test(v))
    return fail("codeVerifier 형식이 올바르지 않습니다(RFC 7636: 43~128자 [A-Za-z0-9-._~]).");
  return ok(v);
}

/**
 * loopback redirect_uri: http://127.0.0.1:{port}/ 또는 http://localhost:{port}/ 형태만 허용.
 * SSRF/오용 차단(§4.2·§8) — 임의 host·scheme·경로를 code 교환에 넘기지 않기 위한 경계 방어.
 * - scheme은 반드시 http(loopback), host는 127.0.0.1 또는 localhost.
 * - 포트는 선택(OS 자동 할당). path는 "/" 또는 없음만 허용(추가 경로·쿼리·프래그먼트 거부).
 */
export function validateLoopbackRedirectUri(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("redirectUri는 문자열이어야 합니다.");
  const v = value.trim();
  if (v.length === 0) return fail("redirectUri가 비어 있습니다.");
  if (v.length > 256) return fail("redirectUri가 너무 깁니다(최대 256자).");

  let url: URL;
  try {
    url = new URL(v);
  } catch {
    return fail("redirectUri 형식이 올바르지 않습니다(URL 파싱 실패).");
  }
  if (url.protocol !== "http:")
    return fail("redirectUri는 http(loopback)만 허용됩니다.");
  if (url.hostname !== "127.0.0.1" && url.hostname !== "localhost")
    return fail("redirectUri host는 127.0.0.1 또는 localhost만 허용됩니다.");
  if (url.search.length > 0 || url.hash.length > 0)
    return fail("redirectUri에 쿼리·프래그먼트는 허용되지 않습니다.");
  if (url.pathname !== "/" && url.pathname !== "")
    return fail("redirectUri 경로는 '/'만 허용됩니다.");
  if (url.username.length > 0 || url.password.length > 0)
    return fail("redirectUri에 인증 정보는 허용되지 않습니다.");
  // 포트는 있으면 1~65535 범위(URL 파서가 이미 정수 문자열로 보장). 빈 포트도 허용(기본 80).
  if (url.port.length > 0) {
    const port = Number.parseInt(url.port, 10);
    if (!Number.isInteger(port) || port < 1 || port > 65535)
      return fail("redirectUri 포트가 올바르지 않습니다(1~65535).");
  }
  return ok(v);
}

/**
 * nonce: id_token replay 방어용 난수(§8.4). 있으면 검증, 없으면 생략(옵션).
 * base64url/hex 등 형식 무관하게 안전 문자·길이만 방어(≤256, [A-Za-z0-9-._~]).
 */
const NONCE_RE = /^[A-Za-z0-9\-._~]{1,256}$/;

export function validateNonce(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("nonce는 문자열이어야 합니다.");
  const v = value.trim();
  if (!NONCE_RE.test(v))
    return fail("nonce 형식이 올바르지 않습니다(1~256자 [A-Za-z0-9-._~]).");
  return ok(v);
}

/** retentionHours: 정수 1~72(firebase-contract §2.3). */
export function validateRetentionHours(value: unknown): ValidationResult<number> {
  if (typeof value !== "number" || !Number.isInteger(value))
    return fail("retentionHours는 정수여야 합니다.");
  if (value < 1 || value > 72)
    return fail("retentionHours는 1~72 범위여야 합니다.");
  return ok(value);
}

/** it13 전역 한도: qrHours 정수 1~8760(1시간~1년, 설계 §5.4). */
export function validateQrHours(value: unknown): ValidationResult<number> {
  if (typeof value !== "number" || !Number.isInteger(value))
    return fail("qrHours는 정수여야 합니다.");
  if (value < 1 || value > 8760)
    return fail("qrHours는 1~8760 범위여야 합니다.");
  return ok(value);
}

/** it13 전역 한도: qrCount 정수 1~100000(설계 §5.4). */
export function validateQrCount(value: unknown): ValidationResult<number> {
  if (typeof value !== "number" || !Number.isInteger(value))
    return fail("qrCount는 정수여야 합니다.");
  if (value < 1 || value > 100000)
    return fail("qrCount는 1~100000 범위여야 합니다.");
  return ok(value);
}

/** 슬롯 하나: index/x/y/width/height 모두 음이 아닌 정수, width·height>0. */
export interface Slot {
  index: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

function isNonNegInt(v: unknown): v is number {
  return typeof v === "number" && Number.isInteger(v) && v >= 0;
}

export function validateSlots(value: unknown): ValidationResult<Slot[]> {
  if (!Array.isArray(value)) return fail("slots는 배열이어야 합니다.");
  if (value.length < 1 || value.length > 6)
    return fail("슬롯은 1~6개여야 합니다.");
  const slots: Slot[] = [];
  for (let i = 0; i < value.length; i++) {
    const s = value[i] as Record<string, unknown>;
    if (typeof s !== "object" || s === null)
      return fail(`슬롯[${i}] 형식이 올바르지 않습니다.`);
    if (!isNonNegInt(s.index)) return fail(`슬롯[${i}].index가 올바르지 않습니다.`);
    if (!isNonNegInt(s.x)) return fail(`슬롯[${i}].x가 올바르지 않습니다.`);
    if (!isNonNegInt(s.y)) return fail(`슬롯[${i}].y가 올바르지 않습니다.`);
    if (!isNonNegInt(s.width) || s.width === 0)
      return fail(`슬롯[${i}].width가 올바르지 않습니다(>0).`);
    if (!isNonNegInt(s.height) || s.height === 0)
      return fail(`슬롯[${i}].height가 올바르지 않습니다(>0).`);
    slots.push({
      index: s.index,
      x: s.x,
      y: s.y,
      width: s.width,
      height: s.height,
    });
  }
  return ok(slots);
}

/** imageSize: {width>0, height>0} 정수. */
export interface ImageSize {
  width: number;
  height: number;
}

export function validateImageSize(value: unknown): ValidationResult<ImageSize> {
  if (typeof value !== "object" || value === null)
    return fail("imageSize 형식이 올바르지 않습니다.");
  const s = value as Record<string, unknown>;
  if (!isNonNegInt(s.width) || s.width === 0)
    return fail("imageSize.width가 올바르지 않습니다(>0).");
  if (!isNonNegInt(s.height) || s.height === 0)
    return fail("imageSize.height가 올바르지 않습니다(>0).");
  return ok({ width: s.width, height: s.height });
}

/** 프레임 이름: 1~100자, `_` 금지(it10 프레임이름 `_` 금지 규약). */
export function validateFrameName(value: unknown): ValidationResult<string> {
  if (typeof value !== "string") return fail("프레임 이름은 문자열이어야 합니다.");
  const v = value.trim();
  if (v.length === 0 || v.length > 100)
    return fail("프레임 이름은 1~100자여야 합니다.");
  if (v.includes("_"))
    return fail("프레임 이름에 '_'는 사용할 수 없습니다(저장 규약).");
  return ok(v);
}

/** 업로드 파일 종류(결과물). final=최종 이미지, timelapse=타임랩스 mp4. */
export type UploadKind = "final" | "timelapse";

/** kind별 허용 확장자·contentType 매핑(경로/메타 조립 시 이 값만 신뢰). */
const KIND_SPEC: Record<UploadKind, { exts: string[]; contentTypes: string[] }> = {
  final: {
    exts: ["jpg", "png"],
    contentTypes: ["image/jpeg", "image/png"],
  },
  timelapse: {
    exts: ["mp4"],
    contentTypes: ["video/mp4"],
  },
};

export interface UploadFileSpec {
  kind: UploadKind;
  ext: string;
  contentType: string;
}

export function validateUploadFile(value: unknown): ValidationResult<UploadFileSpec> {
  if (typeof value !== "object" || value === null)
    return fail("파일 항목 형식이 올바르지 않습니다.");
  const f = value as Record<string, unknown>;
  if (f.kind !== "final" && f.kind !== "timelapse")
    return fail("파일 kind는 final/timelapse여야 합니다.");
  const kind = f.kind as UploadKind;
  const spec = KIND_SPEC[kind];
  if (typeof f.ext !== "string" || !spec.exts.includes(f.ext))
    return fail(`${kind} 확장자가 올바르지 않습니다(${spec.exts.join("/")}).`);
  if (typeof f.contentType !== "string" || !spec.contentTypes.includes(f.contentType))
    return fail(`${kind} contentType이 올바르지 않습니다.`);
  return ok({ kind, ext: f.ext, contentType: f.contentType });
}

/** final ext → OutputFormat(경로 조립용). */
export function extToFormat(ext: string): OutputFormat {
  return ext === "png" ? "png" : "jpg";
}
