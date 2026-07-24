---
name: settings-guest-edit-gate
description: 설정 편집 권한 게이트의 정본 메커니즘(3지점) — 게스트 편집 차단·ini 원값 보존·런타임 불변. it8/it10/it12 반복 확장.
metadata:
  type: project
---

MCPhoto 설정 화면의 "게스트 편집 차단" 게이트는 **VM 계층에만** 있는 3지점 패턴이다. 신규 설정을
게스트 전용 차단으로 만들 때 이 3지점을 그대로 확장한다(새 메커니즘 만들지 말 것).

**Why**: 관리자만 편집 가능한 설정을 늘려도, 촬영/필터 런타임은 `Settings.Current`(ini)를 읽으므로
관리자가 켜둔 값은 게스트 세션에서도 그대로 동작해야 한다. 게이트는 "편집 권한"만 제한하고 기능을 끄지 않는다.

**How to apply** — `SettingsViewModel`(src/MCPhoto.App/ViewModels)에서:
1. `LoadSettings`: `if (IsGuest) { <필드> = false; ... }` — 소스단 강제 off(표시 전용). `_normalizing` 구간 안.
2. `SaveSettings`: `if (!IsGuest) { s.<필드> = ...; }` — 게스트는 미기록 → ini 원값 보존(클로버 금지).
3. `SettingsView.xaml`: 컨트롤에 `IsEnabled="{Binding IsLoggedIn}"`.

- 게이트는 **`AppSettings` 모델에 없음** — 모델은 항상 전 필드 직렬화(Clone/INI). 그래서 모델·CutSelect
  라운드트립 테스트는 게이트 영향 밖.
- `IsLoggedIn`/`IsGuest`는 `_shell.IsLoggedIn` 기반, 설정 진입 중 불변 → INotifyPropertyChanged 불필요.
- 게이트 대상 이력: QR(EnableQrDelivery/SendPhoto/SendTimelapse)·Firebase(HostingBaseUrl/StorageBucket)
  [it8~], 거울모드·재촬영(RetakeEnabled/RetakeLimit)·필터3종(FilterGrayscale/Brightness/Beauty) [it12 R1].
- 회귀 테스트 정본: `Guest_Qr_Forced_Off`, `Guest_Save_Preserves_Ini_*`(SettingsViewModelTests). 신규
  게이트 추가 시 동형 테스트 복제. **주의**: 새로 게이트한 필드가 기존 "게스트도 저장" 테스트를 깨뜨릴 수
  있음(it12에서 `Retake_Settings_Save_And_Load_RoundTrip`가 로그인 세션으로 재작성됨).
- 게스트 hover 툴팁(it12 R3): 비활성 컨트롤은 툴팁 미표시 → `Toggle.Gated`(BasedOn Toggle) 로컬 스타일에
  `ToolTipService.ShowOnDisabled=True` + `DataTrigger Binding=IsGuest`로 게스트일 때만 ToolTip 설정.

관련: [[mcphoto-settings-ini-infra]]
