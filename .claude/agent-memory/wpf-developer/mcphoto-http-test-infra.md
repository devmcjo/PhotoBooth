---
name: mcphoto-http-test-infra
description: MCPhoto HTTP 계층 단위 테스트 인프라(FakeHttpMessageHandler 등)와 XAML 회귀 안전망 위치
metadata:
  type: project
---

MCPhoto(E:\Study\photobooth) HTTP/XAML 테스트 인프라:

- HTTP 서비스 단위 테스트: `tests/MCPhoto.Tests/Http/FakeHttpMessageHandler.cs`(경로별 응답 스텁 + 요청 캡처),
  `TestHttpClientFactory.cs`(BaseAddress 주입). 패턴: `handler.WhenJson(method, pathContains, status, json)` →
  요청 본문·헤더(API키/Bearer)를 `handler.Requests[i]`로 검증. 실서버 호출 없음.
- XAML 회귀 안전망: `tests/MCPhoto.Tests/XamlResourceTests.cs`. 신규/수정 View의 StaticResource 키가
  테마에서 해석되는지 headless(창 미표시)로 검증. `Item1a_View_StaticResource_Keys_Resolve_In_Theme`의
  [InlineData]에 LoginGuestView/AccountView/PasswordResetView가 이미 등록됨 → 이 View들에 새 StaticResource를
  추가하면 자동 검증된다. App.xaml 컨버터 키(BoolToVis/InverseBool 등)는 appKeys 화이트리스트로 제외됨.

**Why:** build·일반 단위 테스트는 StaticResource 런타임 해석 실패(XamlParseException)를 못 잡는다.
**How to apply:** WPF View에 StaticResource를 추가하면 XamlResourceTests가 커버하는지 확인하고,
새 View면 [InlineData] 추가. HTTP DTO/메서드는 FakeHttpMessageHandler로 본문·상태코드 매핑을 단위 검증.

관련: [[iaccountservice-fakes]]
