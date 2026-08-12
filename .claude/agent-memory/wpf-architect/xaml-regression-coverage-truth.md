---
name: xaml-regression-coverage-truth
description: MCPhoto XAML 회귀 테스트의 실제 커버리지 — Window도 소스 스캔으로 검증되므로 "UserControl이어야 한다"는 확장 추론은 사실과 다르다
metadata:
  type: project
---

XAML 표면 형태를 판정할 때 "headless에서 `Window`를 인스턴스화할 수 없다"를 **"그래서 UserControl로 만들어야 검증된다"로 확장하지 말 것.** 실제 커버리지는 세 층이고 층별로 대상이 다르다.

**Why:** it24가 라이선스 고지를 오버레이로 정한 근거가 "Window는 headless 인스턴스화 불가"였고, 그 문장이 이후 설계에서 "UserControl 우위"로 번역될 여지가 있다. 그러나 `XamlResourceTests`의 실제 방식은 **소스 텍스트에서 `StaticResource` 키를 정규식으로 추출해 테마 조회로만 검증**하는 것이고, 그 방식은 파일 종류를 가리지 않는다 — `DiagnosticsWindow.xaml`(Window)·`MainWindow.xaml`(Window)이 이미 그렇게 검증되고 있다. 근거를 잘못 확장하면 셸이 소유해야 하는 오버레이를 불필요하게 UserControl로 쪼개고 `DataContext` 배선만 늘린다.

**How to apply:**
- **전체 파싱 로드 테스트는 `Themes/*.xaml`에만 있다**(`pack://` URI로 개별 딕셔너리 로드 → 형제 교차 참조 사고를 잡는 안전망). Views/MainWindow는 이 층이 없다.
- **소스 스캔 층**(`ThemeKeysReferencedBy` + `LoadTheme().Contains(key)`)이 Views·MainWindow·DiagnosticsWindow를 모두 덮는다 → **신규 리소스 키 누락·오타는 Window에 넣어도 잡힌다.**
- **바인딩 오타는 정규식 테스트로만 잡힌다**(바인딩 실패는 예외 없이 조용한 빈 칸) → 새 오버레이를 넣으면 바인딩 이름 존재 검증 테스트를 함께 만든다.
- 따라서 표면 형태는 **소유권**으로 판정한다: 화면 전이와 무관하게 떠 있어야 하거나 여러 화면 위에 떠야 하면 **셸 소유 = `MainWindow.xaml` 오버레이**(토스트·유휴 경고·완료 팝업). 특정 화면에 종속되면 그 `UserControl` 안 오버레이(라이선스 고지·지원 목록).
- 여전히 유효한 금지: **새 `Window`**(인스턴스화 불가 + 전체화면 키오스크의 창 관리 문제) · **`Themes/`에 신규 키 추가**(병합 딕셔너리 교차 참조 사고 이력).

관련: [[frame-catalog-wait-and-overlay-contrast]], [[it26-writable-paths-and-completion-popup]]
