---
name: it23-external-camera-boundary
description: it23 DSLR 연동 — SDK 무지 3층 경계(shim 1파일)·NikonSdkShim.cs 부재가 정상·노출값 문자열 저장·WYSIWYG 재정의(입구 정규화)
metadata:
  type: project
---

it23(Nikon D5300)은 **SDK·실물 없이** 설계됐다(`docs/design/wpf-it23-external-camera-nikon-design.md`). 후속 작업 시 깨지기 쉬운 결정:

- **경계 3층**: Core POCO 계약 → `MCPhoto.Devices.Nikon`의 `NikonExternalCamera`(오케스트레이션) → `INikonSdkShim`. MAID API 이름은 shim 구현 1파일에만 허용. **`NikonSdkShim.cs`는 SDK 도착 전까지 파일 자체가 없어야 정상**(빈 껍데기 금지 — 부재가 "미착수" 신호). 프로덕션 기본은 `MissingNikonSdkShim`.
- **노출값(셔터·조리개·ISO)은 ini에 인덱스가 아닌 표시 문자열**(`1/125`)로 저장, 빈 값=미지정. 인덱스는 모드·SDK 버전에 따라 표류. 적용은 ConnectAsync 직후 도메인 정확 일치만(근사 매칭 금지).
- **WYSIWYG 재정의**: 픽셀 동일성 대신 "프리뷰의 SW 규칙(거울·크롭·필터) 전부를 DSLR 스틸에 동일 적용". 수신 즉시 `Cv2.Flip`+`CropCalculator.CenterCrop`(웹캠과 같은 함수) 재사용 + 2400px 상한(24MP 메모리) → `CapturedStill` 변환 → 하류(컷선택·필터·합성) 무변경. 광학 차이(화각·색감)는 고지 문구로만.
- **세션 소스는 시작 시 1회 확정**, 컷 실패 시 "그 컷부터 끝까지 웹캠 강등"(컷별 재판정 금지 — 타임아웃 반복 대기 방지). 파이프라이닝(수신 중 다음 카운트다운) 비목표.
- 편집 게이트 신설: `UserRole.CanConfigureExternalCamera`(명시 열거, User 이상 — TempUser 제외). **동작은 ini 기준(게스트 세션에도 DSLR 적용)으로 해석** — USER-DECISION 잔존(§8.2).
- **배포(rev2 확정)**: Nikon SDK 라이선스 **제3자 사본**(canfieldsci.com PDF — 원문 대조 USER-ACTION 잔존)에 재배포 금지·단일 컴퓨터(supplementary license) 조항 → **미동봉이 기본 아키텍처**(임시 조치 아님). 리포 커밋 금지(`**/NikonSdk/` gitignore), 런타임 탐색(`{exe}\NikonSdk\`)+부재 강등이 곧 "배포 시 옵션 차단" 킬스위치(별도 플래그 신설 금지). ffmpeg(GPLv3=동봉+고지)와 **정반대** — 관례 복사 금지. shim 배선 근거는 SDK 동봉 문서만(md3 심볼 덤프·디스어셈블 = 리버스 엔지니어링 조항 위반 소지). 모델 추가 = 레지스트리 한 줄 + **법적 절차 1건**(모델별 SDK 약관 동의).

관련: [[camera-singleton-constraint]], [[settings-guest-edit-gate]], [[design-doc-incremental-write]]
