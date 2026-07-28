---
name: it14-pin-gate-contract
description: it14 설정 진입 PIN 게이트 클라 계약 — PIN 4자리 확정, IAccountService fake 5곳 무회귀, 게이트 분기 위치
metadata:
  type: project
---

it14 설정 진입 PIN 게이트(SSO 계정 전용) 클라이언트 구현 계약.

**PIN 형식 = 정확히 4자리 숫자**(`^\d{4}$`). 설계 문서 O1은 6자리 "권장"이었으나 사용자 승인·서버 `validatePin`(web/functions/src/domain/validation.ts)이 **4자리로 확정**. 클라 검증(AccountViewModel.IsValidPin, PinPromptWindow)도 4자리.

**게이트 분기 위치**: `AppShellViewModel.OpenSettings`. `user.AuthMethod == AuthMethod.Sso` → PIN 게이트(HasPin이면 PromptVerify, 아니면 PromptSetup 강제설정), 그 외 → 기존 비번 게이트(`IPasswordPromptDialogService`, 무변경). 판정 데이터: 서버 `authMethod`/`hasPin` → `UserResponse`(AccountDtos) → `HttpAccountService.ToUser`(ParseAuthMethod: "sso"만 Sso, 그 외 Password 폴백) → `User.AuthMethod`/`HasPin` → `SessionContext.CurrentUser`.

**서버 HTTP 계약**(PinServerDev 구현, 확정): E1 `POST /accounts/me/pin/verify {pin}` → 200 `{ok:true}`|401 불일치|409 미설정. E2 `PUT /accounts/me/pin {newPin, currentPin?}` → 204|401 현재PIN불일치. E3 `PUT /accounts/:id/pin {newPin}` → 204|403 canManage위반|400 자기자신. `BackendJson`은 `JsonIgnoreCondition.Never`라 `currentPin:null`도 직렬화되지만 서버가 null을 최초 설정으로 정확히 처리.

**무회귀 관건**: `IAccountService`에 PIN 3메서드(VerifyPinAsync/SetOwnPinAsync/ResetPinAsync) 추가 시 **모든 fake 구현체 5곳**에 스텁 필수 — AccountViewModelTempUserTests, UserMgmtViewModelTests, AccountViewModelEmailTests, PasswordResetViewModelTests, LoginGuestViewModelTests. 레거시 `MCPhoto.Firebase.AccountService`는 NotSupportedException(SSO는 백엔드 전용). [[wpf-headless-window-test-pitfall]]로 PinPromptWindow는 정적 StaticResource 키 검증만.

**Why:** SSO 자동생성 계정은 sentinel 비번(아무도 모름)이라 비번 재확인이 원천 불가 → 설정 접근 유일 문이 PIN.
**How to apply:** it14 후속·회귀 시 PIN은 4자리 기준. IAccountService 시그니처 변경은 fake 5곳 동반 필수(컴파일 회귀 주범).
