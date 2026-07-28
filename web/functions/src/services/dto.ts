/**
 * Firestore 문서 형태(DTO) — WPF `MCPhoto.Firebase.Dto.*`(C#)의 필드명과 정확히 정합.
 *
 * 저장 키는 camelCase(현행 [FirestoreProperty] 값). 웹·WPF 공통 계약이므로 변경 불가.
 * 근거: src/MCPhoto.Firebase/Dto/{UserDoc,FrameTemplateDoc,ResultSessionDoc}.cs
 *
 * 단, 보안 변경 1건(설계 §4·§7.1): users.password는 평문 대신 **bcrypt 해시**를 담는다.
 * 레거시 평문 문서는 로그인 시 지연 마이그레이션으로 해시로 교체된다(키 이름 password 유지).
 */
import { Timestamp } from "firebase-admin/firestore";

/** users/{id} */
export interface UserDoc {
  id: string;
  /** ⚠️ 신규: bcrypt 해시. 레거시: 평문(로그인 시 마이그레이션). 응답에는 절대 미포함. */
  password: string;
  role: string; // "temp_user" | "user" | "manager" | "admin"
  createdAt: Timestamp;
  /** item1a: 계정 이메일(소문자 정규화). 미수집/레거시 계정은 null(설계 §4.1). */
  email?: string | null;
  /** item1a: 이메일 소유 확인 여부. 생성 시 false, verify 성공 시 true(설계 §4.1). */
  emailVerified?: boolean;
  /**
   * it13: TempUser QR 전송 성공 세션 누적 수. commit 트랜잭션에서만 원자 증가(세션당 1).
   * 미설정=0(레거시/비TempUser). 시간 한도 기준은 createdAt(신규 필드 불요, 설계 §4.1).
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

/**
 * users/{id}/tokens/{tokenId} — 이메일 인증/재설정 토큰(설계 §4.2).
 * 평문 secret·code는 저장하지 않고 sha256 해시만 보관. 응답·로그에 절대 미노출.
 * 1회성: consumedAt 마킹(재사용 거부) — 소비 후 문서 삭제도 병행.
 */
export interface TokenDoc {
  /** 문서 ID(selector, 비밀 아님). */
  id: string;
  /** 용도. */
  purpose: "verify_email" | "password_reset";
  /** secret(verifier)의 sha256 해시. */
  secretHash: string;
  /** 6자리 코드의 sha256 해시. */
  codeHash: string;
  /** 이 토큰이 검증하려는 이메일(발송·대조 대상). */
  email: string;
  /** 생성 시각. */
  createdAt: Timestamp;
  /** 만료 시각(Firestore TTL 대상). */
  expiresAt: Timestamp;
  /** 소비 시각(1회성 마킹). null이면 미소비. */
  consumedAt: Timestamp | null;
  /** 코드 오입력 시도 횟수(§12 브루트포스 방어, 초과 시 무효화). */
  attempts: number;
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

/** 클라 응답용 User(비밀번호/해시·토큰 절대 미포함, 설계 §6.2·§8.5). */
export interface UserResponse {
  id: string;
  role: string;
  createdAt: string; // ISO8601
  /** item1a: 계정 이메일(없으면 null). 토큰·해시는 미포함이지만 email 자체는 노출 가능(§8.5). */
  email: string | null;
  /** item1a: 이메일 소유 확인 여부. */
  emailVerified: boolean;
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
