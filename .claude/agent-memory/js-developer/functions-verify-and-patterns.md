---
name: functions-verify-and-patterns
description: web/functions(Cloud Functions TS) 검증 커맨드와 비자명한 구현 패턴(지연 import, 스모크 검증 전략)
metadata:
  type: reference
---

# web/functions 검증·구현 노하우

## 검증 커맨드 (web/functions 디렉토리 기준)
- `npm run build`(tsc) / `npm run typecheck`(tsc --noEmit) / `npm run lint`(eslint) / `npm test`(jest).
- jest는 `domain/*` 등 **순수 로직만** 대상(Admin SDK/네트워크 무의존). ts-jest + `tsconfig.test.json`(noUnusedLocals off).
- Emulator 스모크: **web/ 디렉토리에서** `firebase emulators:exec --only functions,firestore,storage "node functions/smoke/smoke.mjs"`.
  - 종료코드 0 = 전 케이스 PASS. HTTP 실호출로 엔드포인트 검증 + Admin SDK로 Firestore 직접 시드/조회.
  - 신규 엔드포인트 스모크는 `smoke/smoke.mjs`의 404 체크 직전 블록에 추가하는 관례.

## 스모크에서 해시 토큰 검증 전략
- 토큰은 sha256 해시만 저장돼 Admin으로도 평문(secret/code)을 알 수 없다.
- 해결: 스모크에서 **알려진 code의 sha256을 Admin으로 직접 심어**(`createHash('sha256')` = 서버 `domain/tokens.hashToken`과 동일) 코드 경로·만료·1회성·시도제한을 검증.

## SendGrid 지연 import (dev 미설치 허용)
- `@sendgrid/mail`은 dependencies에 없음(선택적). tsc가 정적 모듈 해석으로 실패하지 않도록,
  동적 import에 **모듈명을 변수로** 넘긴다: `const m="@sendgrid/mail"; await import(m)`.
  - `import("@sendgrid/mail")` 리터럴은 `TS2307 Cannot find module`로 build 실패 → 변수 우회가 정석.
  - commonjs 컴파일 결과가 `Promise.resolve(m).then(s=>require(s))`라 **호출 시점에만** require(모듈 로드 시 X).
- 공급자는 `config.emailProvider`(env `EMAIL_PROVIDER`, 기본 "log")로 선택. log=LogEmailSender(외부 의존 0, 콘솔에 code/link 출력).

## 이메일 인증/재설정 토큰 계약(item1a)
- 토큰 서브컬렉션 `users/{id}/tokens/{tokenId}`: selector.verifier 패턴(`{tokenId}.{secret}`), secretHash+codeHash(sha256), expiresAt, consumedAt, attempts.
- 열거 방지: request 계열(verify/reset)은 존재·상태 무관 **202**, 서비스 함수는 no-op으로 void 반환.
- 코드 경로 시도제한: `MAX_CODE_ATTEMPTS=5` 초과 시 토큰 삭제(§12).
