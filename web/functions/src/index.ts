/**
 * Cloud Functions 2nd gen 진입점 (설계 §1.2).
 *
 * Admin SDK는 ADC(런타임 기본 서비스계정)로 초기화된다(firebase.ts) — 키 파일 없음.
 * 시크릿(JWT_SECRET, CLIENT_API_KEYS)은 Secret Manager로 선언·주입되고,
 * 일반 설정(STORAGE_BUCKET, HOSTING_BASE_URL, JWT_EXPIRES_IN_SECONDS)은 env/param.
 *
 * 로컬/Emulator는 functions/.env(gitignore)에서 값을 읽는다(firebase가 자동 로드).
 * 배포는 `firebase functions:secrets:set JWT_SECRET` 등으로 등록(사용자 콘솔 몫).
 */
import { setGlobalOptions } from "firebase-functions/v2";
import { onRequest } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import { createApp } from "./app";

// 시크릿 선언 — 배포 시 이 이름으로 Secret Manager에서 주입되어 process.env에 노출된다.
const JWT_SECRET = defineSecret("JWT_SECRET");
const CLIENT_API_KEYS = defineSecret("CLIENT_API_KEYS");
// item1b: Google OAuth client secret(백엔드 전용, code 교환에만 사용). 선언만 해 두면
// GOOGLE_OAUTH_CLIENT_ID(env)만 켜면 /auth/google 활성화(소스 재수정 불요). 단 선언된 시크릿이라
// 배포 시 반드시 존재해야 하므로, SSO 미사용이어도 최초 배포 전 임시값이라도 등록해 둔다.
// it15: SENDGRID_API_KEY 선언은 제거했다(이메일 인증·재설정 폐지 → 배포 전제조건 1개 감소).
const GOOGLE_OAUTH_CLIENT_SECRET = defineSecret("GOOGLE_OAUTH_CLIENT_SECRET");

// 리전은 배포 시 결정(설계 §1.2 USER-DECISION). 기본 서울.
setGlobalOptions({ region: "asia-northeast3", maxInstances: 10 });

/**
 * 단일 HTTPS 함수 `api`에 Express 앱을 얹는다. URL: `.../api/{path}`.
 * lazy 초기화: createApp()은 첫 요청 처리 시점(핸들러 내부 X, 모듈 로드 시 1회)에 조립.
 */
const app = createApp();

export const api = onRequest(
  {
    secrets: [JWT_SECRET, CLIENT_API_KEYS, GOOGLE_OAUTH_CLIENT_SECRET],
    // 서명 URL PUT은 클라가 직접 하므로 함수 메모리/타임아웃은 소규모로 충분.
    memory: "256MiB",
    timeoutSeconds: 60,
  },
  app
);
