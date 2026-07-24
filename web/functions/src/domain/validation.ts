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

/** 역할: 화이트리스트(user/manager/admin). */
export function validateRole(value: unknown): ValidationResult<UserRole> {
  if (!isUserRole(value)) return fail("역할이 올바르지 않습니다(user/manager/admin).");
  return ok(value);
}

/** retentionHours: 정수 1~72(firebase-contract §2.3). */
export function validateRetentionHours(value: unknown): ValidationResult<number> {
  if (typeof value !== "number" || !Number.isInteger(value))
    return fail("retentionHours는 정수여야 합니다.");
  if (value < 1 || value > 72)
    return fail("retentionHours는 1~72 범위여야 합니다.");
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
