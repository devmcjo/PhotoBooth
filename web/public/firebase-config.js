// Firebase 웹 앱 공개 구성값.
// 이 값들은 공개되어도 무방하다 — 유일한 방어선은 Firestore/Storage 보안 규칙이다(PRD §10).
// 배포 환경별로 이 파일만 교체한다(코드 수정 불요). 실값은 배포 시점에 확정(OA-1).
// 대상 Firebase 프로젝트 콘솔 > 프로젝트 설정 > 웹 앱 SDK 설정에서 복사한다.
export const firebaseConfig = {
  apiKey: "REPLACE_WITH_API_KEY",
  authDomain: "REPLACE_WITH_PROJECT.firebaseapp.com",
  projectId: "REPLACE_WITH_PROJECT_ID",
  storageBucket: "REPLACE_WITH_PROJECT.firebasestorage.app",
  appId: "REPLACE_WITH_APP_ID"
};
