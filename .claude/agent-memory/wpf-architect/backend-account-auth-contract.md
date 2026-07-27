---
name: backend-account-auth-contract
description: 백엔드 계정/인증(auth·accounts) 계약의 안정적 구조 — 역할 강등·SSO·이메일 유일성·verify 흐름의 재설계 진입점
metadata:
  type: project
---

MCPhoto 백엔드(`web/functions/src`)의 계정/인증 계약은 아래 지점에 격리돼 있다. 계정·인증 기능 재설계 시 이 지점부터 읽는다(중복 탐색 방지).

- **역할 위계 게이트**: `domain/roles.ts`의 `canCreate`/`canManage`(클라 `UserRole.cs`와 미러). `CreatableRoles(User)=[]`이므로 **user는 자기 역할조차 생성 불가** → self-signup은 `createAccount`(canCreate 게이트)를 우회하는 전용 경로가 필요.
- **역할 변경 강등은 서버·HTTP 무변경으로 이미 가능**: `services/accounts.ts:setRole`은 `actor!==admin`·`role==='admin'`·`currentRole==='admin'`만 차단하고 나머지 role(user/manager)을 그대로 저장. 클라 `HttpAccountService.SetRoleAsync`도 role을 그대로 PATCH. → manager↔user 양방향은 **VM+XAML+컨버터만** 확장하면 됨(`UserMgmtViewModel.PromoteToManager`가 단방향인 게 유일 제약).
- **Google SSO**: `services/googleAuth.ts`가 code 교환+id_token 검증(email_verified 강제)을 격리. `services/accounts.ts:loginWithGoogleEmail`이 매핑(과거엔 검증계정만, 자동생성 없음). `config.ts`의 `GOOGLE_ALLOWED_HD`로 도메인 제한(빈값=열림). client_secret은 백엔드 전용.
- **이메일 인증/재설정**: `domain/tokens.ts`(순수: TTL 상수 `VERIFY_TTL_SECONDS`/`RESET_TTL_SECONDS`/`MAX_CODE_ATTEMPTS`, 만료 판정), `services/tokens.ts`(users/{id}/tokens 서브컬렉션 CRUD, selector.verifier + 6자리 코드, 만료 시 자동 삭제), `services/accounts.ts`(`ensureEmailUnique`·`markEmailVerified`·`requestPasswordReset`은 emailVerified=true만 발송). 라우트: `routes/auth.ts`(API키 게이트, 열거방지 202/일반화 401), `routes/accounts.ts`(Bearer+requirePower).
- **클라 계약**: `IAccountService`(Core) 1개 인터페이스에 로그인/CRUD/역할/SSO/이메일 전부. HTTP 구현 `MCPhoto.Http/HttpAccountService.cs`(온라인 전용), 레거시 `MCPhoto.Firebase/AccountService.cs`(SSO·이메일은 `NotSupportedException`, 오프라인 시드 devmcjo만). 401=자격문제는 예외 아닌 null/false로 신호(현행 계약).
- **로그인/재설정 UI 2단계 상태머신 참조 패턴**: `Views/PasswordResetView.xaml`(IsRequestStep/IsConfirmStep Visibility 스왑) — 로그인/가입 탭 재설계의 템플릿.

**Why**: it13(계정·인증 UX) 설계 시 이 계약 구조를 코드로 직접 확인함. 역할 강등이 서버 무변경이라는 점·self-signup이 canCreate 게이트에 막힌다는 점은 재설계 방향을 크게 좌우.
**How to apply**: 계정/인증 재설계는 위 지점만 읽고 시작. 설계 문서: `docs/design/wpf-auth-ux-and-account-rules-design.md`. 관련 [[firebase-access-abstraction]] [[settings-guest-edit-gate]].
