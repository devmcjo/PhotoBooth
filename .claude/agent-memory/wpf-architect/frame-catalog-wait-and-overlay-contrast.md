---
name: frame-catalog-wait-and-overlay-contrast
description: 기본 프레임 로딩 — 세마포어 줄세우기가 진입 대기·문구 정체를 낳음(단일 비행이 정답) / 흰 배경 화면 오버레이는 CaptureView 패턴 복사 금지 / ControlTemplate 내 Freezable 애니메이션 함정
metadata:
  type: project
---

`FrameCatalogService.GetDefaultFramesAsync`의 `SemaphoreSlim` 게이트는 **DB 조회 + 전 이미지 다운로드
전 구간**을 감싼다. `App.OnStartup`의 prefetch가 ct 없이 이 게이트를 잡으므로, FrameSelect 진입은
prefetch가 끝날 때까지 `WaitAsync`에서 **줄 서서** 대기하고 앞 작업의 진행 상황을 알 수 없다.
저장소에 `Frame/` 번들 프레임이 없어(신규 설치본 0개) 최초 실행은 이 대기를 반드시 겪는다.
대기 상한도 없다 — HttpClient 타임아웃 100초 × 프레임 수.

**Why:** it20(대기 UI) 설계·리뷰에서 확인. 이 구조에서 **wall-clock 예산은 반드시 오진을 만든다** —
예산이 전부 줄 서기에 소모되어 "서버 프레임을 못 받았다"는 거짓 안내가 뜨고 손님이 fallback 흰 프레임으로
촬영한다. 줄 세우기를 **단일 비행(single-flight) + 진행 중계**로 바꾸면 원인 단계에서 사라진다: 나중
호출자는 진행 중 작업에 합류하고 최근 국면을 replay 받으며, 호출자별 취소는 `Task.WaitAsync(ct)`가
경계에서 담당한다(공유 작업은 계속 진행 → 캐시 워밍 유지). 이러면 서비스 내부 catch의 OCE 필터도
불필요해진다 — 공유 작업이 `CancellationToken.None`으로 돌기 때문.

**How to apply:**
- 프레임 로딩·캐시 관련 설계 시 게이트 점유 범위와 prefetch 상호작용을 **먼저** 확인한다.
  "줄 세우기 + wall-clock 예산" 조합은 금지. 상한은 **무진행(inactivity)** 으로 정의하고(진행 보고마다
  `CancelAfter` 재무장) 총 상한만 wall-clock으로 둔다. 총 상한은 `AppShellViewModel.IdleWarningSeconds`(120초)
  보다 짧게 — 대기 중에 유휴 경고가 겹치지 않게.
- 취소 후 로컬 폴백 경로는 **공유 작업/게이트에 합류하지 않아야** 한다 — 합류하면 방금 상한을 넘긴 그
  작업을 다시 기다려 상한이 무의미해진다. 게이트 없이 로컬을 읽어도 안전한 근거는
  `LocalFrameStore.WriteFrame`이 png → `.slots` 순으로 쓰고 로드가 `.slots` 없는 항목을 건너뛴다는 점.
- **로컬 해석은 읽기 전용이 아니다**: `EnsureFallbackFrame()`이 `Cv2.ImWrite`로 1200×1600 PNG를 **쓴다**.
  두 경로에서 동시 도달 가능하면 lock + 임시파일 `File.Move(overwrite)` 원자 교체가 필요하고, 임시 경로로
  렌더하면 `FallbackFrameRenderer`가 `ImageUrl`에 `.tmp`를 심으므로 교체 후 최종 경로로 정정해야 한다.
- `GetDefaultFramesAsync(CancellationToken ct = default)`에 진행 보고를 추가할 때 **오버로드 금지** —
  전 인자 기본값 오버로드 2개는 `App.xaml.cs`의 무인자 호출을 CS0121(모호 호출)로 깨뜨린다.
  `ct`를 1번 인자로 유지한 optional 파라미터 추가만 기존 호출부를 보존한다.
- **화면 VM의 로딩 상태는 `finally`에서 확정한다.** happy-path 말미 대입으로 두면 catch 안의 폴백이 던질 때
  예외가 `AppShellViewModel`의 "화면 진입 오류" catch에 삼켜져 전면 오버레이가 영구 고착된다(키오스크 기능 정지).
  폴백 호출도 try로 감싸 빈 목록으로 축퇴시켜야 실패 화면이 실제로 도달 가능해진다.
- **오버레이 대비 함정**: `CaptureView`는 `Brush.Scrim` 위에 `Brush.OnAccent`(흰 글자)를 쓰지만 그 화면
  배경이 어두워서 성립한다. `Brush.Bg`(=#FFFFFF) 배경 화면(FrameSelect·Settings 등)에서 같은 조합을 쓰면
  읽히지 않는다. 흰 배경 화면의 오버레이는 **scrim + 불투명 `Card`(Brush.Bg) + 기본 글자색**을 쓴다
  (`FrameSelectView.xaml` 삭제 확인 팝업이 이 관례).
- **ControlTemplate 안 Freezable 애니메이션 함정**: `ResourceDictionary`의 `ControlTemplate`에 인라인
  선언된 `RotateTransform` 등은 템플릿 Seal 시 동결될 수 있고, 속성 경로 애니메이션
  (`(UIElement.RenderTransform).(RotateTransform.Angle)`)은 런타임에 `Cannot animate … on an immutable
  object instance`를 던진다 → `DispatcherUnhandledException` → 홈 복귀. **`x:Name` + `Storyboard.TargetName`**
  이 표준 회피책이며 `CaptureView.xaml`에 동작 사례가 있다. 시작 트리거는 `Loaded`를 쓴다 —
  `IsVisible=True` 진입 트리거는 템플릿 적용 시점에 이미 true면 발화하지 않을 수 있다.
- 로컬 프레임 해석 경로(`LoadPublic`/`LoadBundleFrames`/fallback 생성)는 OpenCV 전체 디코드와
  PNG 생성을 포함한다 → UI 스레드 동기 실행 금지(`Task.Run` 필수).
- 테스트 이음새: `MCPhoto.App`은 `InternalsVisibleTo("MCPhoto.Tests")`를 갖고, MS.DI는 기본값 있는 미등록
  생성자 파라미터를 허용한다(`FrameCatalogService`의 `downloadImage` 선례) → VM에 `Func<...>? = null`
  선택 인자로 타임아웃·다운로드를 주입해 단위 테스트할 수 있다.
- 설계 문서: `docs/design/wpf-it20-frame-download-waiting-design.md` (rev2 = 리뷰 P2 반영본)

관련: [[it10-server-key-distribution]] (같은 서비스의 프레임 이름 `_` 규약 함정),
[[it15-frame-local-only-policy]] (프레임 저장·삭제 의미론), [[source-file-encoding]],
[[design-doc-incremental-write]]
