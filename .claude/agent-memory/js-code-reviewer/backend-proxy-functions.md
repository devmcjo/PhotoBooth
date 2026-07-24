---
name: backend-proxy-functions
description: MCPhoto 백엔드 프록시(web/functions, Cloud Functions 2nd gen TS)의 검증 방법·구조·정합 기준
metadata:
  type: project
---

MCPhoto 백엔드 프록시(P1 서버)는 `web/functions/`에 있다. WPF에서 Firebase Admin 키를 제거하고 서버 경유(방향 B)로 DB를 조작하는 보안 재설계 산출물.

**Why:** WPF exe에 serviceAccountKey.json이 동봉돼 exe 유출=DB 전권·평문 비번 유출 위험. 서버(ADC, 키 파일 없음)만 Admin 권한을 갖게 이전.

**How to apply (검증 방법):**
- 준거 문서: `docs/design/wpf-backend-proxy-migration-design.md`(§6.2 엔드포인트 계약), `docs/design/firebase-contract.md`(스키마·토큰URL), C# 도메인 `src/MCPhoto.Firebase/*`·`src/MCPhoto.Core/*`(정합 대조).
- 기계 검증: `cd web/functions` 후 `npx tsc` / `npx tsc --noEmit` / `npx eslint "src/**/*.ts"` / `npx jest`(34 단위테스트). node_modules는 보통 이미 설치돼 있다.
- **Emulator 스모크(direct 근거)**: `cd web/functions && npm run build`로 lib 생성 후, `cd web` 에서 `firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"`. 36 케이스, exit 0 = PASS. firebase CLI(전역 15.x)와 Java(JDK 21) 필요 — 둘 다 이 환경에 설치돼 있음. GCLOUD_PROJECT=mcphoto-955fb.
- 인가 우회 검증 포인트: actingRole은 JWT에서만 도출(클라 바디 무시), canManage/canCreate/isPower가 C# UserRoleExtensions와 1:1. Emulator 스모크가 manager→manager생성/manager→admin삭제/admin→admin생성/역할지정 admin전용/세션URL위조를 모두 차단 실증.
- Emulator 우회(signing.ts): `FIREBASE_STORAGE_EMULATOR_HOST` 존재 시에만 서명 우회 → 프로덕션 무영향(배포 env엔 부재). `extensionHeaders`(x-goog-meta-firebaseStorageDownloadTokens)는 @google-cloud/storage v4 서명에 포함됨.
- 시크릿 위생: `.env`는 gitignore로 차단(실값 있으나 dev 전용), `.env.example`은 placeholder, `web/functions/` 전체가 untracked 상태였음(커밋 전). firebase.json functions.ignore가 .env/테스트를 배포 제외.
- 스택: Express on 단일 onRequest `api`(URL `.../api/{path}`), bcryptjs 2.4.3(cost10, $2a$), jsonwebtoken HS256(alg 화이트리스트로 none 방지), TS strict+no-explicit-any.
