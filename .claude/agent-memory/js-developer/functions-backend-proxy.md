---
name: functions-backend-proxy
description: web/functions(Cloud Functions 2nd gen, TypeScript) 백엔드 프록시의 빌드·검증·Emulator 스모크 방법과 서명 URL Emulator 제약
metadata:
  type: project
---

WPF Admin 키 제거용 서버 경유 계층(설계 `docs/design/wpf-backend-proxy-migration-design.md`)의 서버 구현이 `web/functions/`에 있다. 기존 web Firebase 프로젝트(project=mcphoto-955fb)에 통합됨.

**구조**: 순수 도메인 로직 `src/domain/*`(roles/session/password/jwt/validation, C# UploadContract·UserRoleExtensions 이식) + `src/services/*`(Firestore/Storage 조작) + `src/routes/*`(Express) + `src/http/*`(auth 미들웨어·에러). 단일 함수 `api`(onRequest+Express)로 URL은 `.../api/{path}`.

**검증 명령**(cwd=`web/functions`):
- `npm run build`(tsc) / `npm run typecheck`(tsc --noEmit) / `npm run lint`(eslint) / `npm test`(jest, 순수 domain만)
- Emulator 스모크(cwd=`web`): `firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"` — Admin(규칙우회)으로 시드 후 실 HTTP 호출로 12엔드포인트 검증.

**핵심 제약 — 서명 URL은 Emulator에서 실패한다**: `file.getSignedUrl(v4)`는 Storage Emulator/ADC에서 `Cannot sign data without client_email`로 실패한다(배포는 런타임 SA가 IAM signBlob로 서명 → 사용자가 콘솔에서 Service Account Token Creator 역할 부여 필요). `signing.ts`는 `FIREBASE_STORAGE_EMULATOR_HOST` env가 있으면 서명을 우회해 Emulator 업로드 URL을 반환(배포엔 이 env 없음 → 항상 서명 경로). 프로덕션 서명 동작은 Emulator로 검증 불가 — 사용자 콘솔 배포 후 확인 몫.

**시크릿 규약**: JWT_SECRET·CLIENT_API_KEYS는 `defineSecret`(Secret Manager), 로컬은 `web/functions/.env`(gitignore). 키 파일 절대 없음(ADC 초기화). `.env`는 `web/functions/.gitignore:6`이 커버.

**Node engine 경고 무해**: package.json engines=20, 로컬 Node v25 → EBADENGINE 경고 뜨나 빌드/테스트 정상.
