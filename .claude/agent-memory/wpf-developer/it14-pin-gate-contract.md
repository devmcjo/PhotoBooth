---
name: it14-pin-gate-contract
description: it14 설정 진입 PIN 게이트 클라 계약 — PIN 4자리 확정, IAccountService fake 5곳 무회귀, 게이트 분기 위치
metadata:
  type: project
---

it14 설정 진입 PIN 게이트(SSO 계정 전용) 클라이언트 구현 계약.

**PIN 형식 = 정확히 4자리 숫자**(`^\d{4}$`). 설계 문서 O1은 6자리 "권장"이었으나 사용자 승인·서버 `validatePin`(web/functions/src/domain/validation.ts)이 **4자리로 확정**. 클라 검증(AccountViewModel.IsValidPin, PinPromptWindow)도 4자리.

**⚠️ it15에서 이 분기는 폐기됐다 — 최신 상태는 [[it15-client-auth-contract]]를 보라.** (아래는 it14 시점 기록)

**게이트 분기 위치(it14 시점)**: `AppShellViewModel.OpenSettings`. `user.AuthMethod == AuthMethod.Sso` → PIN 게이트(HasPin이면 PromptVerify, 아니면 PromptSetup 강제설정), 그 외 → 비번 게이트(`IPasswordPromptDialogService`). 판정 데이터 경로: 서버 `authMethod`/`hasPin` → `UserResponse`(AccountDtos) → `HttpAccountService.ToUser` → `User.AuthMethod`/`HasPin` → `SessionContext.CurrentUser`. it15에서 비번 게이트·`AuthMethod.Sso`/`Password` 값·`IPasswordPromptDialogService`가 모두 삭제되고 PIN 단일 경로(`EnsurePinGateAsync`)로 통합됐다.

**서버 HTTP 계약**(PinServerDev 구현, 확정): E1 `POST /accounts/me/pin/verify {pin}` → 200 `{ok:true}`|401 불일치|409 미설정. E2 `PUT /accounts/me/pin {newPin, currentPin?}` → 204|401 현재PIN불일치. E3 `PUT /accounts/:id/pin {newPin}` → 204|403 canManage위반|400 자기자신. `BackendJson`은 `JsonIgnoreCondition.Never`라 `currentPin:null`도 직렬화되지만 서버가 null을 최초 설정으로 정확히 처리.

**무회귀 관건**: `IAccountService` 시그니처 변경 시 **모든 fake 구현체**에 스텁 동반 필수(컴파일 회귀 주범). it15 이후의 fake 목록은 [[it15-client-auth-contract]]에 있다(it14 시점 목록의 Email/PasswordReset 테스트는 삭제됨). [[wpf-headless-window-test-pitfall]]로 PinPromptWindow는 정적 StaticResource 키 검증만.

**Why:** SSO 자동생성 계정은 sentinel 비번(아무도 모름)이라 비번 재확인이 원천 불가 → 설정 접근 유일 문이 PIN. it15에서 모든 계정이 SSO가 되어 PIN이 **유일한** 게이트 자격증명이 됐다.
**How to apply:** PIN은 4자리 기준(불변). 게이트 위치·분기는 it15 메모리를 우선하라.
