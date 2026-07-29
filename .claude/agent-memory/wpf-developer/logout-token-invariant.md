---
name: logout-token-invariant
description: 로그아웃 시 JWT 폐기 불변식 — IBackendSession은 BackendSessionSynchronizer가 소유해야 하며 단순 AddSingleton으로 되돌리면 결함이 조용히 재발한다
metadata:
  type: project
---

`SessionContext.CurrentUser == null`이면 `IBackendSession.Token`도 반드시 null이어야 한다. 이 동기화는
`ServiceRegistration.RegisterBackendServices`에서 **`BackendSessionSynchronizer`가 홀더를 소유·노출하는 형태**로
등록해 성립한다:

```
services.AddSingleton<BackendSessionSynchronizer>(sp => new(sp.GetRequiredService<SessionContext>(), new BackendSession()));
services.AddSingleton<IBackendSession>(sp => sp.GetRequiredService<BackendSessionSynchronizer>().Session);
```

**Why:** 원래는 `AddSingleton<IBackendSession, BackendSession>()` 한 줄이었고 `Clear()`의 프로덕션 호출자가
**0개**였다. 업로드는 선택적 Bearer(`SendJsonOptionalBearerAsync`)라 남은 토큰을 조용히 부착하므로,
운영자 로그아웃 후 게스트 촬영물이 직전 계정 소유로 기록되고 TempUser면 서버가 `qrUsedCount`까지
차감했다([[tempuser-server-authority]] 방어 훼손). 이 결함은 **어느 클래스에도 잘못된 코드가 없어서**
클래스 단위 테스트로는 잡히지 않는다 — 합성 루트를 실제로 조립해야만 재현된다.
"홀더를 동기화기가 소유"하는 모양은 *토큰이 존재할 수 있는 모든 시점에 구독이 살아있음*을 DI 그래프로
강제하기 위한 것이다(별도 eager 해석이 필요 없다). 단순화하려고 두 줄을 한 줄로 되돌리면 컴파일·테스트가
아니라 **런타임에서만** 조용히 깨진다.

**How to apply:**
- 등록 형태를 건드릴 때는 `tests/MCPhoto.Tests/Http/BackendSessionLogoutTests.cs`(8개)를 반드시 통과시킬 것.
- 로그인 전이(비-null)에서는 **아무것도 하지 말 것.** `HttpAccountService.LoginWithGoogleAsync`가
  `Session.SignIn`을 먼저 하고 `LoginGuestViewModel`이 그 뒤에 `SessionContext.Login`을 부르는 순서라,
  손대면 방금 받은 토큰을 지운다. 반대로 "항상 익명"으로 과교정하면 서버의 TempUser 한도 적용이 사라진다.
- `CurrentUser`를 비우는 지점은 `Logout()`·`Reset(clearUser: true)` 둘이고 후자는 전자에 위임한다.
  그래서 개별 호출부가 아니라 `CurrentUserChanged` 구독 한 곳에 둔다.
  ⚠️ **실호출 로그아웃은 `AppShellViewModel.Logout()` 하나뿐이다** — `clearUser: true`를 넘기는 프로덕션
  호출부는 0이다(유휴 타임아웃은 it8 A1로 로그아웃 금지 → `clearUser: false`). "경로가 여러 개라서"를
  근거로 쓰면 오판이다. 구독 방식의 진짜 근거는 "비우는 지점이 늘어도 한 곳이 덮는다"는 예방성이다.
- 남은 미세 틈: 없음(리뷰에서 실현 불가 확정). `HttpAccountService`의 `ToUser(...) ?? new User{...}` 폴백 때문에
  `SignIn`이 실행된 경로에서 user는 절대 null이 아니고, `SignIn`↔`Login` 사이에 예외 지점도 없다.
  남는 경우는 프로세스 종료뿐인데 토큰은 메모리 전용(디스크 비영속)이라 무해하다.

관련: [[composition-root-testable]], [[it15-client-auth-contract]]
