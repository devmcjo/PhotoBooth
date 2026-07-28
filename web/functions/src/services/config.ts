/**
 * 전역 TempUser 한도 config — Firestore `config/tempUserLimits` 문서 로드·저장(설계 §4.3·§5.4).
 *
 * 문서 부재 시 서버가 기본값(48h/30회)을 사용한다(loadTempUserLimits 폴백). Admin만 수정한다.
 * 순수 판정은 domain/tempUserLimit.ts(evaluateQrGate)가 담당하고, 여기서는 Firestore 조작만 한다.
 */
import { db } from "../firebase";
import {
  DEFAULT_TEMP_USER_LIMITS,
  TempUserLimits,
} from "../domain/tempUserLimit";
import { TempUserLimitsDoc } from "./dto";

const CONFIG_COLLECTION = "config";
const LIMITS_DOC_ID = "tempUserLimits";

/** qrHours 허용 범위(1시간 ~ 1년). PATCH·저장 시 강제. */
export const QR_HOURS_MIN = 1;
export const QR_HOURS_MAX = 8760;
/** qrCount 허용 범위. */
export const QR_COUNT_MIN = 1;
export const QR_COUNT_MAX = 100000;

/**
 * 전역 TempUser 한도 로드. 문서 부재 또는 필드 결손 시 기본값으로 폴백(설계 §4.3).
 * 잘못 저장된 값(비정수·범위 밖)은 방어적으로 기본값을 쓴다(오구성이 과금 전면 개방/차단이 되지 않도록).
 */
export async function loadTempUserLimits(): Promise<TempUserLimits> {
  const snap = await db().collection(CONFIG_COLLECTION).doc(LIMITS_DOC_ID).get();
  if (!snap.exists) return { ...DEFAULT_TEMP_USER_LIMITS };
  const doc = snap.data() as Partial<TempUserLimitsDoc> | undefined;
  const qrHours = sanitize(doc?.qrHours, DEFAULT_TEMP_USER_LIMITS.qrHours, QR_HOURS_MIN, QR_HOURS_MAX);
  const qrCount = sanitize(doc?.qrCount, DEFAULT_TEMP_USER_LIMITS.qrCount, QR_COUNT_MIN, QR_COUNT_MAX);
  return { qrHours, qrCount };
}

/** 저장값이 정수·범위 내면 사용, 아니면 기본값(방어적 폴백). */
function sanitize(value: unknown, fallback: number, min: number, max: number): number {
  if (typeof value !== "number" || !Number.isInteger(value)) return fallback;
  if (value < min || value > max) return fallback;
  return value;
}

/** 부분 갱신 입력(둘 다 선택). 미지정 필드는 기존값 유지. */
export interface TempUserLimitsPatch {
  qrHours?: number;
  qrCount?: number;
}

/**
 * 전역 한도 갱신(Admin 전용, 라우트가 권한 게이트). 현재값(또는 기본값)에 patch를 병합해 저장한다.
 * 범위 검증은 라우트에서 이미 수행되지만, 저장 직전 최종값을 반환해 응답에 쓴다.
 */
export async function setTempUserLimits(patch: TempUserLimitsPatch): Promise<TempUserLimits> {
  const current = await loadTempUserLimits();
  const next: TempUserLimits = {
    qrHours: patch.qrHours ?? current.qrHours,
    qrCount: patch.qrCount ?? current.qrCount,
  };
  const doc: TempUserLimitsDoc = { qrHours: next.qrHours, qrCount: next.qrCount };
  await db().collection(CONFIG_COLLECTION).doc(LIMITS_DOC_ID).set(doc);
  return next;
}
