---
name: functions-auth-security-invariants
description: MCPhoto Cloud Functions(web/functions) 인증/계정 서비스의 보안 불변식 — 리뷰 시 근거로 재사용
metadata:
  type: project
---

`web/functions` 인증(auth/accounts) 코드 리뷰에서 확인된, 코드만 봐선 놓치기 쉬운 보안 불변식.

**Why:** 사용자가 이 영역에 "보안 반복 검토"를 강하게 요청. 아래 불변식은 여러 함수에 흩어져 있어 재검증 비용이 크다.
**How to apply:** BE 계열(SSO·self-signup·이메일 유일성) 재리뷰 시 이 사실을 먼저 확인하면 라운드를 단축할 수 있다. 단, 코드가 바뀌었을 수 있으니 인용 전 해당 줄을 다시 읽어 검증할 것.

- **id/email 네임스페이스 분리**: `validateAccountId`는 `[A-Za-z0-9._-]{3,40}`(=`@` 불가), `validateEmail`은 `@` 필수. 따라서 `findByIdOrEmail`의 id-first 분기에 실제 email이 걸릴 일이 없어 id-shadowing 우회가 구조적으로 불가.
- **markEmailVerified 트랜잭션 원자성 근거**: `@google-cloud/firestore` 타입 정의상 `Transaction.get(Query)`는 "pessimistic lock on all returned documents"를 보장. 이게 "동시 2계정이 같은 email 인증" 경합을 막는 핵심. read(doc)→read(query)→write 순서(read-before-write) 준수 여부가 검증 포인트.
- **SSO 자동가입 게이트**: 자동생성은 `verifyGoogleCodeAndGetEmail`이 email을 반환할 때만 발생하고, 그 함수는 `payload.email_verified !== true`면 throw(googleAuth.ts). 미검증 email로는 loginWithGoogleEmail 진입 자체가 불가.
- **sentinel pw**: SSO 자동생성 계정 pw = `hashPassword(randomBytes(32))`. `login()`은 verifyPassword로만 매칭 → id/pw 로그인 영구 불가. 응답형 `UserResponse`(dto.ts)에 password 필드 없음 → 노출 경로 없음.
- **reset 라우팅 안전의 이유**: reset 토큰은 verified 계정에만 발급(`requestPasswordReset`가 emailVerified!==true면 no-op)되므로, findByIdOrEmail이 미인증 docs[0]을 골라도 그 계정엔 토큰이 없어 confirm이 false. 오라우팅이 인증 우회로 이어지지 않음.

관련: [[functions-verify-commands]]
