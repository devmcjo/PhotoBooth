---
name: it25-test-simulation-boundary
description: it25 — TestMode 외부카메라 시뮬레이션은 표시 표면 전용(TS1~TS4)·TestTypeCode 행내 매핑·인식 콤보 선택 분리·프린터는 스캐폴드 환원
metadata:
  type: project
---

it25 설계(`docs/design/wpf-it25-recognized-camera-and-test-simulation-design.md`)의 재사용 판정 4건.

**Why:** 장비·SDK 없이 UI를 확인하는 요구에서 최악의 실패는 시뮬레이션이 진짜처럼 동작하는 것(가짜 사진 생성·실기 회귀 오인)이었고, 저장값 클로버·인덱스 표류는 이 리포의 반복 사고 유형(it7 B9, it24 P5)이다.

**How to apply:**
- **시뮬레이션은 표시 전용**: `[Test] ExternalCamera`/`ExternalCameraType`는 SettingsViewModel 검색 시퀀스 1곳에서만 분기(TS1), 게이트는 `IsTestUser` 참조 동일성(TS2 — `IsEnabled` 단독 분기 금지), ini 자동 기록 금지(TS3), 결과에 W38 시뮬레이션 명시 라인 필수(TS4). `IExternalCamera` 데코레이터 방식은 촬영 경로를 오염시키므로 금지 — 촬영·테스트 모달은 W7/W10으로 정직 실패가 정답. 이 판정은 병행 산출된 전체 시뮬(합성 스틸) 설계와의 충돌에서 팀리드가 정본 채택으로 확정(가짜 사진의 서버 유출 + 게스트 세션에 IsTestUser 게이트 불가가 결정 근거, 정본 §0.2 인용 블록).
- **배너 접미(W40)는 테스트 계정 로그인 분기 한정**: `IsEnabled`나 키 값 단독으로 붙이면 실계정·로그아웃 상태(시뮬레이션 미적용)에 "시뮬레이션 중"이라는 거짓 배너가 된다 — B9.3 3분기 중 첫 분기 + `ExternalCamera=1`에서만.
- **int↔모델 매핑은 레지스트리 행 안에**: `ExternalCameraModel.TestTypeCode` 필드(배정 후 변경·재사용 금지, Id와 같은 지위). 배열 인덱스·별도 딕셔너리 금지 — "모델 추가 = 표 한 줄" 규약이 매핑까지 포괄해야 한다.
- **인식 콤보는 ini 미러와 분리**: 콤보 SelectedValue를 `ExternalCameraModel`에 직접 바인딩하면 빈 목록에서 WPF가 null 되쓰기로 저장값을 지운다. 별도 `RecognizedCameraSelection` + 명시 선택 시에만 미러 갱신. "인식됨" = S6(연결 확인)만 — WMI 감지(S3)는 저장 표면(콤보)에 올리지 않는다.
- **프린터는 의도된 스캐폴드로 환원**: 표면(VM 8멤버+하위 패널)은 제거하되 `IPrinterEnumerator`/`SystemPrinterEnumerator`+DI 등록+계약 테스트는 보존(주석으로 스캐폴드 표식). ini 2키는 `WriteFrom`에서 빼면 첫 저장에서 값이 소멸하므로 유지. it24 W24~W31 폐기, W32~W39 신설.

관련: [[it23-external-camera-boundary]] [[it24-device-discovery-honesty]] [[spec-deprecation-convention]]
