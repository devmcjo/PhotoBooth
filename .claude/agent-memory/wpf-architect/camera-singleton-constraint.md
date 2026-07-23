---
name: camera-singleton-constraint
description: MCPhoto 카메라 서비스 아키텍처 제약 — Singleton 공유 + StartAsync 재시작 불가, Preview 데드코드
metadata:
  type: project
---

MCPhoto `ICameraService`(OpenCvCameraService)는 DI **Singleton**(`ServiceRegistration.cs`)이라 홈 프리뷰·촬영·(it9 신규)테스트 모달이 **단일 물리 카메라 인스턴스**를 공유한다.

**Why:** UVC 웹캠은 OS 레벨에서 단일 프로세스/핸들만 점유 가능 → 인스턴스 격리(각자 `new OpenCvCameraService`)는 동일 장치 동시 open 시 충돌 위험. Singleton이 정답.

**How to apply:**
- `StartAsync`는 `if (_running) return true`로 **이미 실행 중이면 deviceIndex 등 파라미터를 무시**한다. 장치 인덱스 변경/카메라 전환은 반드시 `StopAsync`(await, 스레드 join 최대 2s) → `StartAsync(새 인덱스)` 순서로 설계할 것.
- 프리뷰 렌더는 재사용 컴포넌트 `CameraFramePresenter(Image)` 의 `Attach(ICameraService)`/`Detach()`로 통일 — 새 프리뷰 화면 설계 시 이걸 재사용하고 View Unloaded/Window Closed에서 `Detach`(FrameReady 구독 해제, 누수 방지).
- 플래시는 하드웨어가 아니라 **화면 하양 오버레이**(`FlashActive` bool + 흰 Border). 스틸은 `CaptureStillAsync`(다음 프레임 1장), 저장은 호출자 책임(서비스는 저장 안 함) → 테스트/미리보기는 결과를 폐기하면 "저장 없는 촬영" 재현 가능.
- **`PreviewViewModel`/`PreviewView`는 DI 등록·XAML 존재하나 어떤 `AppState`에도 매핑 안 된 데드코드**(2026-07-23 it9 확인). 라이브 프리뷰로 카메라를 켜는 실사용 화면은 촬영(`CaptureView`)뿐. 카메라 점유 충돌 분석 시 이 사실이 전제.

관련: [[mcphoto-settings-ini-infra]]
