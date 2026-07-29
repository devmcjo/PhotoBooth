---
name: composition-root-testable
description: MCPhoto.App은 InternalsVisibleTo로 ServiceRegistration을 테스트에 개방 — 배선 결함은 컨테이너를 조립해야만 재현된다
metadata:
  type: feedback
---

배선(wiring) 결함 — "어느 클래스에도 잘못된 코드가 없는데 아무도 호출하지 않아서 생기는 결함" — 은
**합성 루트를 실제로 조립하는 테스트**로만 재현·고정된다. MCPhoto.App은 이를 위해
`<InternalsVisibleTo Include="MCPhoto.Tests" />`를 갖는다(MCPhoto.Capture와 같은 선례).

**Why:** 클래스 단위 테스트는 "이 클래스가 시키는 대로 동작하는가"만 본다. `IBackendSession.Clear()`처럼
구현은 정확한데 **프로덕션 호출자가 0개**인 결함은 그 격자를 그대로 통과한다([[logout-token-invariant]]).
또한 수정과 함께 도입되는 새 타입(동기화기 등)을 테스트가 직접 new 하면 "프로덕션이 실제로 그렇게
배선돼 있다"는 것을 증명하지 못하고, 수정 **전에는 컴파일조차 안 돼** red→green을 보일 수 없다.
이미 존재하는 진입점(`ServiceRegistration.RegisterBackendServices`)을 통과시키면 두 문제가 동시에 풀린다.

**How to apply:** 배선 테스트 조립법(`tests/MCPhoto.Tests/Http/BackendSessionLogoutTests.cs` 참고)
- `ServiceCollection`에 `ISettingsService` 스텁 + 프로덕션과 동일한 `AddSingleton<SessionContext>()`를 넣고
  `ServiceRegistration.RegisterBackendServices(services)` 호출.
- 실서버 차단은 **`AddHttpClient` 뒤에** `services.AddSingleton<IHttpClientFactory>(_ => new TestHttpClientFactory(handler))`
  로 덮어쓴다(마지막 등록 승리). 명명 클라이언트 설정을 손댈 필요 없다.
- `BuildServiceProvider()`는 싱글턴을 즉시 생성하지 않으므로, **어떤 순서로 resolve 하는지가 곧 시나리오**다.
  구독이 resolve 시점에 걸리는 배선이라면 테스트도 로그인 전에 홀더를 resolve해야 한다.
- 컨테이너 `Dispose()`는 팩토리로 만든 `IDisposable` 싱글턴을 정리하므로, 구독 해제 검증에 그대로 쓸 수 있다.

관련: [[mcphoto-http-test-infra]], [[mcphoto-solution]]
