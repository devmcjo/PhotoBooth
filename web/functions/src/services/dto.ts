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
  role: string; // "user" | "manager" | "admin"
  createdAt: Timestamp;
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

/** 클라 응답용 User(비밀번호/해시 절대 미포함, 설계 §6.2). */
export interface UserResponse {
  id: string;
  role: string;
  createdAt: string; // ISO8601
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
