---
name: it15-client-auth-contract
description: it15 클라 인증 축소 — IAccountService 7메서드, AuthMethod=Google/Unknown, PIN 게이트 단일화, 테스트 fake 위치·함정
metadata:
  type: project
---

it15에서 클라이언트 인증이 **Google SSO + 4자리 PIN** 두 가지로 축소되고 레거시 Firebase 직결 경로(`MCPhoto.Firebase` 프로젝트)가 삭제됐다.

**계약 축소 결과**
- `IAccountService` = **7메서드**: `LoginWithGoogleAsync` / `GetAllAsync` `DeleteAsync` `SetRoleAsync` / `VerifyPinAsync` `SetOwnPinAsync` `ResetPinAsync`. 비번·회원가입·이메일 인증·시드·계정생성 전량 삭제.
- `AuthMethod` enum = **`Google` | `Unknown`** (구 `Password`/`Sso` 폐기). 파싱은 `AuthMethodExtensions.ParseAuthMethod`("google"만 Google, 그 외 Unknown — 조용한 오인 방지). 표기는 `.ToLabel()`("Google SSO"/"알 수 없음").
- `User`에서 `Password`·`EmailVerified` 삭제. `Role` 기본값 `TempUser`.
- `AppSettings.UseBackend` 폐지 → 백엔드 전용. `NormalizeBackend()`는 빈 base URL에서 **슬래시 보정만 스킵**하고 다른 설정을 되돌리지 않는다.
- `UploadService`는 `MCPhoto.Core.Upload`로 이관됨(레거시 어셈블리 삭제 시 유일한 무조건 등록 구현이라 이관 필수).

**PIN 게이트 단일화**: 진입 게이트는 `AppShellViewModel.EnsurePinGateAsync(User)` **public 단일 지점**. 설정 진입(`OpenSettings`)과 계정 관리 진입(`AccountViewModel.OnEnterAsync`)이 공유한다. 계정 관리는 **`HasPin == false`일 때만** 강제 설정하고, 이미 있으면 재확인하지 않는다. `IPasswordPromptDialogService`는 폐지.

**Why:** 서버가 `pinHash` 1개만 저장하므로 물리적으로 동일 PIN이다. 게이트를 두 곳에 복제하면 fail-closed 규약이 갈라진다.

**How to apply:**
- `IAccountService` 시그니처를 또 바꾸면 fake 구현 **5곳**을 동반 수정: `AccountViewModelPinTests`, `AccountViewModelTempUserTests`, `UserMgmtViewModelTests`, `LoginGuestViewModelTests`, `AppShellPinGateTests`. (it14 시절 목록과 다르다 — Email/PasswordReset 테스트는 삭제됨)
- ⚠️ **테스트에서 로그인 유저를 만들 때 `HasPin`을 신경 써라.** `HasPin=false` + `EmptyServiceProvider`면 `OnEnterAsync`가 fail-closed로 `ReturnFromOverlay()`를 타고 `GetRequiredService<HomeViewModel>()`에서 **예외**가 난다. PIN 무관 테스트는 `HasPin = true`로 두고, 게이트를 검증할 때만 `tests/MCPhoto.Tests/Fakes/MapServiceProvider.cs` + `FakePinPromptDialogService.cs`를 쓴다(둘 다 it15 신규 공용 fake).
- `PinPromptWindow`는 it15 §5.6 완화 2건 보유: 연속 5회 불일치 시 창 자동 닫힘 + 불일치마다 1.5초 입력 비활성. **서버 도달 시도만** 카운트(형식 오류·네트워크 오류는 미포함 — 정상 사용자 락아웃 방지).
- [[it14-pin-gate-contract]]의 "SSO→PIN/그외→비번" 분기 서술은 it15에서 무효다.
