/**
 * Firestore 문서 형태(DTO) — 저장 키는 camelCase. 웹·WPF 공통 계약이므로 임의 변경 불가.
 *
 * it15: users 문서에서 **비밀번호 개념을 폐지**했다(`password`·`emailVerified` 삭제).
 * 자격증명은 Google SSO(신원) + `pinHash`(진입 게이트) 두 가지뿐이다(설계 §5.3).
 */
import { Timestamp } from "firebase-admin/firestore";

/** users/{id} — it15: 비밀번호 개념 폐지. 자격증명은 Google(신원) + pinHash(게이트) 뿐. */
export interface UserDoc {
  id: string;
  role: string; // "temp_user" | "user" | "advanced_user"(it16) | "manager" | "admin"
  createdAt: Timestamp;
  /** Google 계정 이메일(소문자 정규화). SSO 신원의 근거 — 항상 존재. */
  email: string;
  /** 인증 제공자(D2). 현재 "google" 고정. 추후 "kakao"|"apple" 확장. */
  authMethod: string;
  /**
   * 진입 PIN의 bcrypt 해시(password.ts 인프라 재사용). 미설정 시 필드 부재.
   * 응답에는 절대 미포함(hasPin 파생값으로만 노출, 설계 §5.3).
   */
  pinHash?: string | null;
  /**
   * it13: TempUser QR 전송 성공 세션 누적 수. commit 트랜잭션에서만 원자 증가(세션당 1).
   * 미설정=0(비TempUser). 시간 한도 기준은 createdAt.
   */
  qrUsedCount?: number;
}

/**
 * config/tempUserLimits — 전역 TempUser QR 한도(1쌍, Admin만 수정). 문서 부재 시 서버가 기본값 폴백.
 * 사용량(qrUsedCount)은 계정별(UserDoc), 한도는 전역(설계 §4.3).
 */
export interface TempUserLimitsDoc {
  /** 시간 한도(시간). 기본 48. */
  qrHours: number;
  /** 횟수 한도(성공 세션 수). 기본 30. */
  qrCount: number;
}

/** frameTemplates/{id} */
export interface FrameTemplateDoc {
  id: string;
  userId: string | null;
  isDefault: boolean;
  name: string;
  imageUrl: string;
  imageSize: { width: number; height: number };
  slots: Array<{ index: number; x: number; y: number; width: number; height: number }>;
  createdAt: Timestamp;
}

/** resultSessions/{id} */
export interface ResultSessionDoc {
  id: string;
  finalImageUrl: string | null;
  timelapseUrl: string | null;
  createdAt: Timestamp;
  expiresAt: Timestamp;
  downloadPageUrl: string;
}

/** 클라 응답용 User(해시 절대 미포함). 와이어 형식은 it15 설계 §9.1에서 동결. */
export interface UserResponse {
  id: string;
  role: string;
  createdAt: string; // ISO8601
  /** 계정 이메일(방어적으로 null 허용 — 마이그레이션 후에는 항상 존재). */
  email: string | null;
  /** 인증 제공자(D2). 저장값을 그대로 노출("google"). */
  authMethod: string;
  /** 진입 PIN 설정 여부(pinHash != null 파생). pinHash 원문은 절대 미노출(설계 §5.3). */
  hasPin: boolean;
}

/** 클라 응답용 Frame. */
export interface FrameResponse {
  id: string;
  userId: string | null;
  isDefault: boolean;
  name: string;
  imageUrl: string;
  imageSize: { width: number; height: number };
  slots: Array<{ index: number; x: number; y: number; width: number; height: number }>;
  createdAt: string; // ISO8601
}

/** 클라 응답용 ResultSession. */
export interface ResultSessionResponse {
  id: string;
  finalImageUrl: string | null;
  timelapseUrl: string | null;
  createdAt: string;
  expiresAt: string;
  downloadPageUrl: string;
}
