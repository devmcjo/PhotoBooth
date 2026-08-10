---
name: it24-device-discovery-honesty
description: it24 외부 장치 탐색 — "없습니다 vs 확인 불가" 명제 분리·준비도=shim IsOperational+파일·WMI 제네릭 이름 반증·System.Printing 동반 어셈블리
metadata:
  type: project
---

it24(`docs/design/wpf-it24-external-device-discovery-design.md`)는 장비·SDK 없이 설정 외부 장치 섹션을 재설계했다. 후속 작업이 깨뜨리기 쉬운 결정:

- **명제 분리 원칙(R1~R5)**: "연결 가능한 장치가 없습니다"(부재 단정)는 **부재를 판정할 능력이 있을 때만**. SDK 스택 미비면 "확인할 수 없습니다"+사유. 부재 단정도 완화형("찾지 못했습니다"). 장치 상태 문구를 추가할 때 이 절(§3)을 먼저 통과시킬 것.
- **준비도 판정은 파일 존재가 아니다**: md3를 배치해도 `MissingNikonSdkShim`이면 연결 불가 → `CheckReadiness()` = `shim.IsOperational` && 파일 프로브. 파일만 보고 "SDK 있음"으로 분기하면 거짓 상태표가 된다.
- **WMI USB 관찰은 양성 신호 전용**: Nikon 바디는 `Win32_PnPEntity`에서 PNPClass 'WPD'로 뜨되 이름이 제네릭 "MTP Portable Device"일 수 있음(MS Q&A 실사례) + 로컬 실측에서 비카메라 장치("새 볼륨")도 WPD로 열거됨. 매칭 미스는 정상 — 미관찰로 "없음"을 강화하지 말 것. D5300 실측(U1·U2)은 Step 9로 이월.
- **System.Printing은 WindowsDesktop 참조팩 동봉**(8.0.x 실측 — 패키지 추가 불요). 열거 실패(스풀러 중지)=P4 "확인 불가"와 성공 0대=P2 "설치된 프린터 없음"을 구조(`Succeeded`)로 분리. `IsOffline` 표시는 신뢰성 낮아 비목표. 스풀러 중지 실험은 장비 없이 가능.
- **프린터 (b) 범위**: 열거+선택+ini 저장(`PhotoPrinterName`, 빈 값=미선택, 목록 부재여도 보존)까지. 실인쇄 비목표 — W25 고지("인쇄 기능은 아직 제공되지 않습니다…")가 정직성의 조건. 저장 게이트는 외부 장치 7필드 단일 `CanEditExternalCamera` 블록으로 통일.
- **가시성**: 게스트 섹션 Collapsed 폐지 → 항상 표시+읽기 전용(GuestGateNote 재사용). QR 게이트의 "게스트 강제 off 표시"와 달리 **ini 원값 표시**(동작 게이트가 아니므로 off 표시가 오히려 거짓). 기존 "게스트 Collapsed" 테스트는 의도적으로 깨져 재작성 대상.
- **검색은 명시 버튼 + 순간 관찰**: it23 "설정 진입 무접촉" 유지, 토글 on 하위에만 버튼, 성공해도 즉시 `DisconnectAsync`(문구도 "연결 확인됨" — 현재형 "연결됨"은 거짓).

관련: [[it23-external-camera-boundary]], [[settings-guest-edit-gate]], [[design-doc-incremental-write]]
