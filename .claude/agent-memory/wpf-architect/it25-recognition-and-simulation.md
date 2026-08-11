---
name: it25-recognition-and-simulation
description: it25 — 장치 노출 기준="지원+인식"(설치 나열 반려)·인식 콤보 합성 행·[Test] 장치 시뮬은 DI 분기(전체 시뮬, 반쪽 금지)·모델 추가 3지점
metadata:
  type: project
---

it25(`docs/design/wpf-it25-external-camera-recognition-design.md`)는 it24 직후 사용자 피드백으로 방향을 수정했다. 후속 작업이 깨뜨리기 쉬운 결정:

- **장치 노출 기준(사용자 확정)**: "설치/존재하는 것 나열"이 아니라 **"이 앱이 지원하고 + 연결이 인식된 것만 나열"**. it24 프린터 열거가 이 기준으로 반려됐다(지원 항목 0인데 설치 프린터를 나열 = 오표시). 새 장치 종류를 노출할 때 이 기준을 먼저 적용할 것.
- **콤보 3개념 분리**: 지원 모델(레지스트리→별도 창 `SupportedCamerasWindow`) / 인식된 카메라(검색 S6, 설정 세션 수명 — 영속 금지) / 선택된 카메라(ini `ExternalCameraModel`, 기본값 `""`=선택안함). 저장 선택값이 미인식이면 **합성 행 "{모델} (미인식)"**으로 표시 유지 — 거짓 표시·클로버 동시 방지(문자 스펙 "선택안함만 노출"과의 승인된 편차, USER-DECISION 1).
- **Clamp/Resolve 비대칭(의도적)**: `AppSettings.Clamp`는 미지·빈 모델 Id → `""`(선택안함), `NikonExternalCamera`는 `Resolve` 폴백(빈→Default) **유지** — 검색은 선택 없어도 지원 모델을 프로브해야 하기 때문. 촬영/테스트 모달 게이트는 `Enabled && Find(모델)!=null`(선택안함=접촉 0+W7 토스트).
- **[Test] 장치 시뮬레이션**: `ExternalCamera=1`+`ExternalCameraType=0` → DI 등록 분기로 `SimulatedExternalCamera`(MCPhoto.Capture, OpenCV 합성 스틸) 등록. **반쪽 시뮬(연결만 성공) 금지** — 촬영마다 강등 배너를 재생하는 더 나쁜 상태. 장치 시뮬은 사용자 스코프가 아니라 **DI 스코프**(TM3 IsTestUser 비적용, "IsEnabled는 DI 등록에 허용" 계약 활용). 배너에 " · 외부 카메라 시뮬레이션({모델})" 접미 필수.
- **정수 Type 매핑은 명시 표**(-1=없음, 0=NikonD5300) — 레지스트리 인덱스 직결 금지(순서 변경 함정). **모델 추가 = 3지점**: 레지스트리 한 줄 + Type 매핑 한 줄 + analysis 12 §7 표 한 줄 (+ 법적 절차 1건은 it23 그대로).
- 레코드 스키마: `ExternalCameraModel(Id, Manufacturer, ProductName, Md3FileName)` + `DisplayName` 파생 — 별도 창 표(제조사·제품명)와 검색 키워드의 단일 원천.

관련: [[it24-device-discovery-honesty]], [[it23-external-camera-boundary]], [[settings-guest-edit-gate]]
