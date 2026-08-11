---
name: it24-discovery-observation-seam
description: 장치 검색 상태표 테스트는 WMI 프로브 델리게이트 주입이 없으면 머신 구성에 좌우된다 — SettingsViewModel 마지막 선택 파라미터가 그 이음새
metadata:
  type: project
---

`SettingsViewModel`은 it24부터 생성자 마지막에 두 선택 파라미터를 받는다:
`IPrinterEnumerator? printers = null`, `Func<IReadOnlyList<string>>? probePortableDevices = null`.
후자를 주입하지 않으면 실제 WMI(`PortableDeviceProbe.TryGetPortableDeviceNames`)가 돌아가고,
이 머신에는 카메라가 아닌 WPD 장치("새 볼륨" 등)가 잡혀 **참고 라인(W23)·매칭 결과가 머신마다 달라진다.**

**Why:** 설계(§12)는 "판정은 순수 함수 뒤에 있으니 WMI 실 I/O는 테스트하지 않는다"고만 했는데,
VM 커맨드 테스트(T-D1)는 그 WMI를 실제로 지나간다 — 관측을 테스트가 지정할 수 없으면 상태 전수표
(S2~S6 문구·상세 라인)가 flaky해진다. 프로브 자체는 설계대로 static 2메서드로 남기고 VM에만 이음새를 뒀다.

**How to apply:**
- 검색 관련 VM 테스트는 **항상** `probePortableDevices`를 주입한다(기본 `() => Array.Empty<string>()`).
  선례: `tests/MCPhoto.Tests/SettingsViewModelDiscoveryTests.MakeVm`.
- S1(검색 중) 관측은 이 델리게이트를 `ManualResetEventSlim`으로 붙잡아 만든다 — `Task.Wait()`류를 쓰면
  xUnit1031 경고가 늘어 warning 0 게이트가 깨진다.
- 프린터 열거 진행 중 상태는 `FakePrinterEnumerator.OnEnumerate` 훅 안에서 스냅샷을 뜬다(페이크가 동기라
  호출자 쪽에서는 중간 상태가 보이지 않는다).
- 토글 off→on 트리거 열거를 기다릴 때는 `PrinterEnumerationTask`를 await 한다(`LicenseLoadTask` 선례).

관련: [[combobox-selectedvalue-clobber]] · [[wpf-headless-window-test-pitfall]]
