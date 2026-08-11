---
name: combobox-selectedvalue-clobber
description: 항목 없는 ComboBox의 SelectedValue TwoWay 바인딩은 저장값을 null로 되쓴다 — 목록이 비는 상태에서 설정값을 지키려면 합성 행이 필요하다
metadata:
  type: feedback
---

`SelectedValue="{Binding Xxx}"` + `SelectedValuePath` 조합에서 **ItemsSource에 매칭 항목이 없으면**
WPF가 SelectedValue를 null로 되돌리고, TwoWay 바인딩이 그 null을 VM에 써 버린다. `IsEnabled=false`여도
바인딩은 평가되므로 콤보를 비활성화하는 것으로는 막히지 않는다.

**Why:** it24 프린터 선택(`PhotoPrinterName`)에서 발견. 스풀러가 멈췄거나 프린터가 잠시 꺼진 상태로
설정 화면에 들어와 [저장]을 누르면, 관리자가 맞춰 둔 프린터 이름이 조용히 사라진다. 이 저장소의
"게이트 대상 필드는 ini 원값을 클로버하지 않는다" 원칙(settings-guest-edit-gate 계열)과 정면 충돌한다.
같은 형태의 함정이 it7 B9(`SelectedIndex` 바인딩이 목록 채움 시점에 저장값을 0으로 덮음)와 짝을 이룬다.

**How to apply:**
- 목록이 동적으로 채워지고 저장값이 목록에 없을 수 있다면, **저장값 원문을 담은 합성 행을 목록에 넣어
  선택을 해석 가능하게 만든다**(표시명에 "(설치 확인 필요)" 류 접미를 붙여 실재 항목과 구분).
  it24 구현: `SettingsViewModel.ApplyPrinterEnumeration`.
- 합성 행 때문에 `Count > 0`이 "고를 수 있는 실물이 있다"는 뜻이 되지 않는다 → 콤보 활성 조건은
  개수 파생이 아니라 **열거 결과에서 따로 세운 플래그**(`HasPrinters`)로 둔다.
- 목록 교체 직후 저장값 스냅샷으로 선택을 복원하는 방어도 함께 둔다(교체 과정의 되쓰기 방어).
- headless 테스트로는 이 되쓰기가 재현되지 않는다(콤보가 없다) — 구조로 막고, 테스트는
  "합성 행이 존재하고 저장값이 보존된다"를 고정한다.

관련: [[it16-permission-axes]]
