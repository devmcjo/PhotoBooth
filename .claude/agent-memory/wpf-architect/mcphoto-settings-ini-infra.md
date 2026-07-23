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

관련: [[camera-singleton-constraint]]
