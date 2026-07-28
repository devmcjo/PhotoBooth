---
name: functions-backend-conventions
description: MCPhoto Cloud Functions(web/functions) 백엔드 구현·검증의 비자명한 관례와 함정
metadata:
  type: project
---

MCPhoto 백엔드는 Cloud Functions 2nd gen + TypeScript + Express, 단일 함수 `api`에 라우터 마운트(실 URL `.../api/{path}`).

**검증 게이트(완료 전 반드시 통과)**:
- `npm run build`(tsc, 0) · `npm run typecheck`(tsc --noEmit, 0) · `npm run lint`(eslint, 0) · `npm test`(jest).
- **Why:** eslint가 `@typescript-eslint/no-explicit-any: error`, `eqeqeq: always`로 엄격. tsconfig strict + noUnusedLocals/Parameters.
- **How to apply:** 논리 단위마다 실행. `any` 금지 — `unknown` + 좁히기.

**jest 순수 테스트 경계**: `jest.config.cjs`가 `src/**/__tests__/**/*.test.ts`만 실행하고, 테스트는 `domain/*`·순수 서비스 로직만 대상. `services/accounts.ts` 등 `db()`(firebase-admin) 의존 코드는 `jest.mock("../firebase")` + `FakeFirestore`로 커버한다(accounts.test.ts 선례).
- **How to apply:** 새 서비스는 순수 검증부(예: payload assert)를 별도 export해 jest로 커버한다.

**⚠️ `smoke/smoke.mjs`는 it15 이후 실행 불가(2026-07 확인)**: 스크립트가 `POST /auth/login`·`POST /accounts`(id/password)를 호출하는데 it15에서 두 라우트가 **삭제**됐다(현존 인증 라우트는 `POST /auth/google` 하나). 마지막 갱신 커밋 `c0b9360`.
- **Why:** "DB 왕복은 Emulator 스모크로 검증"이라는 기존 관례가 지금은 성립하지 않는다. 스모크를 신뢰하고 계획을 세우면 실행 단계에서 막힌다.
- **How to apply:** 검증 계획에 스모크를 넣기 전에 `smoke.mjs`가 현행 라우트와 맞는지 먼저 확인한다. 라우트 미들웨어(게이트)는 스모크 없이도 `__tests__/authGates.test.ts` 패턴(Request/next 모킹 + 라우트 소스 구조 단정)으로 회귀를 고정할 수 있다.

**라우트 미들웨어 회귀 고정 패턴**: 서비스 레벨 테스트는 라우터를 통과하지 않아 **게이트 누락을 구조적으로 못 잡는다**(it16이 고친 PIN 라우트 결함이 정확히 이 사각지대였다). `authGates.test.ts`처럼 ① 미들웨어를 직접 호출해 역할별 상태코드를 단정하고 ② `fs.readFileSync`로 라우트 소스를 읽어 게이트 개수·라우트별 유무를 단정한다(주석 스트립 후 매칭 — 게이트를 설명하는 주석이 실제 호출로 오집계되면 거짓 통과한다).

**AppConfig 확장 시 email.test.ts도 수정**: `src/__tests__/email.test.ts`의 `makeConfig`가 `AppConfig` 전 필드를 명시 생성 → config에 필드 추가하면 이 헬퍼에 기본값을 넣어야 tsc 통과. 놓치기 쉬운 무회귀 함정.

**Emulator 스모크 실행**: `web/` 디렉토리 기준 `firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"`. Java 21·firebase CLI 존재. `.env`(gitignore)에서 값 로드.
- `defineSecret`으로 선언된 시크릿(JWT_SECRET·CLIENT_API_KEYS·SENDGRID_API_KEY·GOOGLE_OAUTH_CLIENT_SECRET)은 emulator가 **Secret Manager를 먼저 조회(403 경고 출력) 후 .env로 폴백** — 403 로그는 정상, 스모크는 PASS한다.

**google-auth-library**: firebase-admin 전이 의존으로 이미 node_modules에 존재(9.15.1). item1b에서 `dependencies`에 `^9.15.1` 명시 추가(전이 의존 소실 대비). `OAuth2Client.getToken({code, codeVerifier, redirect_uri})` + `verifyIdToken({idToken, audience})` → `LoginTicket.getPayload(): TokenPayload`(iss/aud/exp/email?/email_verified?/nonce?/hd?).

**시크릿 활성화 원칙(sendgrid/google 공통)**: 자격은 "사용 시에만 강제". config에서 관련 자격이 다 있을 때만 enabled=true, 부분 구성은 오구성으로 조기 throw. 미구성이면 라우트가 501(`HttpError.notImplemented`). `.env`엔 실값 미포함, `.env.example`에 키만 예시.
