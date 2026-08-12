---
name: photobooth-settings-roundtrip-convention
description: MCPhoto AppSettings 신규 필드는 반드시 4곳 동시 반영 — 리뷰 시 이 4곳을 항상 대조 검증
metadata:
  type: project
---

MCPhoto(WPF/.NET 10)에서 `AppSettings`에 설정 필드를 추가할 때는 반드시 **4곳**에 동시 반영해야 하며, 한 곳이라도 누락 시 저장/복원/편집취소 중 하나가 조용히 깨진다. 리뷰어는 신규 설정 필드가 나오면 항상 이 4곳을 1건씩 대조한다:

1. `AppSettings.cs` 필드 선언 (+ 옵션 배열은 `static readonly int[] AllowedXxx` 관례)
2. `AppSettings.Clamp()` — 범위 밖 값 보정 (`ClosestFrom(value, allowed, fallback)` = 절댓값 최소 거리, 동거리 시 배열 앞쪽 우선)
3. `AppSettings.Clone()` — 얕은 복제(편집 취소 대비). 누락 시 편집 후 취소해도 값이 남음
4. `IniSettingsService.ReadInto`/`WriteFrom` — `nameof(s.Xxx)` 키로 `GetBool/GetInt`·`SetBool/SetInt` 쌍

**Why:** 설계 문서 §1이 이 규칙을 "공통 설계 원칙"으로 명시. it11 #13 재촬영(RetakeEnabled/RetakeLimit) 리뷰에서 4곳 모두 정확히 반영됨을 확인.

**How to apply:** SettingsViewModel 미러(`[ObservableProperty]` + LoadSettings/SaveSettings 왕복)와 `IReadOnlyList<int> XxxOptions { get; } = AppSettings.AllowedXxx` 옵션 노출까지 합치면 VM/View 레이어 대조 지점은 총 6곳. 게스트 게이트 대상 여부도 확인 — QR/Firebase 설정만 `if(!IsGuest)`로 게이트, 촬영 옵션은 게스트도 저장 가능.

**`_settings.Save()` 호출 조건을 넓히는 변경의 안전 판정(it16 실측)**: `MainWindow.OnClosing`의 Save가
"창모드+Normal일 때만" → **무조건**으로 바뀌어도 안전했다. 근거 2개를 매번 재확인한다 —
① `_settings.Current.<필드>`를 **in-memory로만 변형하는 지점이 0**이다(`grep -rn "\.Current\.\w\+\s*="`).
특히 `EnableQrDelivery`는 TempUser 한도 초과 시에도 VM 미러만 false로 두고 ini 원값을 건드리지 않으므로
무조건 Save가 한도 상태를 영속시키지 않는다. ② `IniSettingsService.WriteFrom`은 내장 API 키를
`s.BackendApiKey != _embeddedApiKeyDefault`일 때만 기록하므로, ini가 없던 부스에서 새로 파일이 생겨도
**평문 키가 새지 않는다**. 이 두 조건 중 하나라도 깨지면 무조건 Save는 🟠 Major(설정 오염·시크릿 유출)다.
