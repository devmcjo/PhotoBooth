---
name: wpf-headless-window-test-pitfall
description: MCPhoto 테스트에서 Window를 headless 인스턴스화하면 Application 싱글턴/스레드 친화 충돌로 실패 — 정적 키 검증으로 우회
metadata:
  type: feedback
---

WPF `Window`를 xUnit 테스트에서 직접 `new`하여 XAML 파싱을 검증하려 하지 말 것.

**Why:** `System.Windows.Application`은 AppDomain당 1개만 생성 가능하고 스레드 친화(thread affinity)를 가진다. 각 테스트가 자체 STA 스레드에서 `new Application()`을 시도하면 "같은 AppDomain에 두 개 이상의 Application 인스턴스를 만들 수 없습니다" 예외가 난다. 단독 실행 시엔 통과하나 `XamlResourceTests`(이미 Application 생성)와 같은 프로세스에서 함께 돌면 레이스로 실패한다. it11 #14 진단창 회귀 테스트에서 실제 발생.

**How to apply:** Window/View의 StaticResource·바인딩 대상 회귀를 잡고 싶으면 `XamlResourceTests` 패턴을 따르라 — 소스 XAML을 텍스트로 읽어 `{StaticResource key}`를 정규식 추출하고, `pack://` URI로 로드한 테마 딕셔너리에서 `Contains(key)`로 해석 가능 여부만 검증한다(STA 스레드 1개에서 Application 재사용, Window 인스턴스화 없음). App.xaml에 정의된 공용 컨버터 키(BoolToVis/InverseBool 등)와 Window 자체 정의 리소스(x:Key)는 테마 밖이므로 제외 집합으로 걸러야 한다. 실제 창 육안 확인은 사용자 액션으로 남긴다.

**⚠️ App.xaml에 새 컨버터를 추가하면 `XamlResourceTests.cs`의 `appKeys` allowlist를 갱신해야 한다.** 이 `appKeys` HashSet이 `XamlResourceTests.cs` 안에 **3곳(Diagnostics/Item1a/SettingsView 각 테스트 메서드)에 중복 하드코딩**돼 있다. tested View 목록(현재 PasswordResetView/AccountView/LoginGuestView/SettingsView/DiagnosticsWindow)이 참조하는 새 컨버터를 allowlist에 안 넣으면 "테마에 없는 StaticResource" 로 그 View 테스트가 **실패**한다(it13 `RoleLabel` 추가 시 AccountView.xaml에서 실제 발생). 새 컨버터가 tested View에서 쓰이면 해당 메서드의 appKeys에 키를 추가하라. tested 목록에 없는 View(예: UserMgmtView)는 무영향.
