---
name: mcphoto-settings-ini-infra
description: MCPhoto 재사용 가능한 INI/경로 인프라 + 설정 화면·오버레이 네비게이션 구조
metadata:
  type: project
---

MCPhoto는 설정 저장에 자체 구현 INI 인프라를 쓴다 — 신규 외부 설정(브랜딩 등)도 이걸 재사용할 것.

**Why:** 외부 의존성 없는 경량 파서로 이미 검증됨. 새 파서/포맷 도입은 불필요한 중복.

**How to apply:**
- `IniFile`(`MCPhoto.Core/Settings`): 범용 `Parse(text)`/`GetString/GetInt/GetBool/GetEnum`, 섹션·키 대소문자 무시, 손상 라인 무시(크래시 금지). 새 ini 설정은 이걸로 파싱.
- `SettingsPathResolver.DefaultCandidates(exeDir → %ProgramData%\MCPhoto → %LocalAppData%\MCPhoto)` + `ResolveWritable(candidates, canWrite)`: **실행경로 우선** 폴백 체인. 배포 친화적 파일 위치는 이 관례(실행파일 옆)를 따를 것. 읽기 전용 설정은 실행경로가 최선(권한 문제 무관).
- 한글 값 ini는 `Encoding.UTF8` 명시 읽기 권장(메모장 저장 인코딩 편차 대비).
- 설정 화면은 별도 Window가 아니라 **오버레이**(같은 창 내 `CurrentViewModel` 스왑). 상단 바 ⚙ → `AppShellViewModel.OpenSettings` → `NavigateToOverlayAsync(Settings)`, 복귀는 `ReturnFromOverlay()`(직전 화면·세션 보존). 촬영/QR에서는 상단 바 숨김 → 촬영 중 설정 진입 불가.
- 설정 저장은 `IniSettingsService.Save()`가 bool 반환(폴백 체인 실패 시 false) — 성공 오인 금지 원칙(실패 시 오류 토스트). `AppSettings.Clamp()`가 값 범위 강제.
- App 데이터 폴더 = `App.DataFolder`(%CommonApplicationData%\MCPhoto, 로그·세션). 실행경로 = `AppContext.BaseDirectory`.

**⚠️ MCPhoto.ini에 새 섹션을 설계할 때의 함정 (2026-08-10 it23에서 확인 → **it23에서 수정 완료**)**
- ~~`Save()`가 `[MCPhoto]` 외 섹션을 지운다~~ → **해소됨**(2026-08-12 재확인). 현재
  `IniSettingsService.Save()`는 폴백 루프 안에서 **경로별로 재조립**하고 `TryReadExisting(candidate)` +
  `AdoptMissingSections`로 그 경로의 외래 섹션(`[Test]` 등)을 보존한다. `MainWindow.OnClosing`이
  앱 종료마다 `Save()`를 부르는 것은 여전하지만 더 이상 유실을 만들지 않는다.
- 남는 규약: `[MCPhoto]` 안의 미매핑 키는 **계속 버린다** — 키 단위 병합은 오탈자·폐기 키를 되살린다.
  한 번 조립한 문자열을 폴백 경로에 재사용하면 1순위 파일의 섹션이 2순위 파일로 **이식**된다(경로별 재조립 필수).
- **활성 ini 경로는 "파일이 있는 곳"이 아니라 "쓰기 가능한 첫 후보"**다(`ResolveWritable`).
  사람이 편집한 ini가 앱이 읽는 ini가 아닐 수 있으므로, ini 기반 기능을 설계하면 **경로를 화면
  (진단)·로그에 노출**하는 항목을 함께 넣는다. `ISettingsService`에는 `IniPath`가 없고
  `IniSettingsService`에만 있다 → 인터페이스에 올릴 때 테스트 스텁 5곳이 함께 깨진다.
- 파서가 주석을 버리고 `ToString()`이 LF만 쓰므로, **ini에 설명 주석을 두는 설계는 성립하지 않는다**
  (첫 저장에 사라진다). 샘플·설명은 `docs/analysis/12`에 둔다.
- 설계 문서: `docs/design/wpf-it23-session-testmode-license-design.md` §B4

관련: [[camera-singleton-constraint]], [[source-file-encoding]]
