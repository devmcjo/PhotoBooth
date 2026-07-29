# 90 · 로드맵 & 향후 작업

| 항목 | 값 |
|------|-----|
| 문서 | 알려진 이슈·기술 부채·개선 예정·비범위 |
| 범위 | 미해결/대기 항목의 단일 집합소. 완료되면 이 문서에서 제거하고 해당 세부 문서로 반영 |
| 최종 업데이트 | 2026-07-29 (it16 — 해결 2건 등재 + 이연 7건·비범위 5건 추가 / 60번 §3~§5 문서 동기화 해소 + 신규 잠복 1건 등재) |
| 갱신 규칙 | 이슈 발견·수정·범위 결정 시 즉시 이 문서 갱신. "상태" 컬럼 유지 |

---

## 1. 알려진 이슈 / 기술 부채

| 항목 | 현상 | 위치 | 상태 |
|------|------|------|------|
| ~~프레임 로컬 삭제 안 됨~~ | 썸네일 `Image`가 png 파일을 잠가 `File.Delete` 실패(예외 삼킴) → png 잔존 | `Views/FrameSelectView.xaml`, `Core/Frames/LocalFrameStore.cs`, `ViewModels/FrameSelectViewModel.cs` | **수정 완료(2026-07-23)**: `FilePathToImageConverter`(OnLoad+IgnoreImageCache)로 파일 잠금 해소 → 삭제 성공. `DeleteLocal`은 png 존재 여부로 정직 반환, `ConfirmDelete`가 실패 시 안내(성공 오인 금지) |
| ~~설정 진입 권한 게이트~~ | 게스트 QR/Firebase 소스단 off(ini 불변) + 로그인 시 비밀번호 가드 | `SettingsViewModel`, `SettingsView`, `ResultViewModel` | **완료(2026-07-23, 보완#1)** |
| 문서 동기화 지연 | 셔터음(#7)·권한게이트(보완#1)·설정 레이아웃이 11·12 세부 문서에 아직 미반영. ~~60번 §3~§5가 it13~it15 미반영~~ → **해소(2026-07-29)**. **추가 발견**: [70 §6 "Firebase 초기화 실패 진단"](./70-logging-and-troubleshooting.md#6-firebase-초기화-실패-진단)이 **삭제된 `MCPhoto.Firebase` 기준 구서술**(서비스 계정 키 탐색·`IsInitialized` 파급·시드 인메모리 admin)이라 현재 코드와 다르다 | `docs/analysis/11`, `docs/analysis/12`, `docs/analysis/70` §6 | **부분 해소**: [60](./60-auth-accounts-and-roles.md) §3~§5를 Google SSO 단일 경로·진입 PIN 게이트·백엔드 API 계정 저장소로 **전면 재작성 완료**(백엔드 미도달 시 동작이 구 "미초기화 폴백"을 대체, 시드 계정 절은 "폐지됨" 이력으로 정리). 11·12와 **70 §6은 대기** — 70 §6은 [60 §4.5](./60-auth-accounts-and-roles.md#45-백엔드-미도달-시-동작-구-미초기화-폴백-재정의)로 대체 서술 후 폐기/재작성 필요 |
| ~~비밀번호 평문 저장~~ | `users` 문서에 비밀번호 평문(MVP) | (삭제됨) `Firebase/AccountService.cs` | **해소(it15, 2026-07-28)**: 비밀번호 인증 자체가 폐지됐다. `users.password` 필드가 사라지고 자격증명은 bcrypt `pinHash` 하나만 남았으며, 평문을 다루던 `MCPhoto.Firebase.AccountService`는 프로젝트째 삭제됐다. 항목 소멸(해시/솔트 개선 불요) |
| 인스톨러 self-contained 불일치 | `installer/MCPhoto.iss` 주석은 `--self-contained false` 예시, 실제 `publish.ps1`은 `true`(단일 파일) | `installer/MCPhoto.iss`, `publish.ps1` | 확정 필요(배포 방식 통일) |
| ~~ffprobe 잔존~~ | `tools/ffmpeg/ffprobe.exe` 코드 미사용 | `tools/ffmpeg/` | **정리 완료(2026-07-23)**: 삭제 |
| ~~Preview 데드코드~~ | `PreviewView`/`PreviewViewModel` 미매핑 | — | **정리 완료(2026-07-23)**: 파일·DI 등록 제거 |
| 만료 물리삭제는 인프라 의존 | `PurgeExpiredAsync` 코드 존재하나 앱에서 호출 안 함 → GCS Lifecycle/Firestore TTL 설정에 의존 | `Core/Upload/UploadService.cs` | 의도된 설계([50](./50-infra-gcp-lifecycle-and-ttl.md)). 인프라 미설정 시 미삭제 주의 |
| 프레임 피커 썸네일 가상화 미적용 | "기존 프레임 불러오기" 모달(과 프레임 선택 화면)이 `WrapPanel` ItemsPanel로 **UI 가상화가 꺼져** 있고 `DecodePixelWidth`도 미적용 → 후보 수가 늘면 모달 오픈이 지연될 수 있다. 현재 상한(공용 소수 + 계정당 최대 10)에서는 수용 | `Views/FrameEditorView.xaml`, `Views/FrameSelectView.xaml`, `Themes/Controls.xaml`(`FrameCard.Content`) | 후속 과제(it15 F2-D5 수용). ⚠️ 개선 시 `FilePathToImageConverter`의 **OnLoad + IgnoreImageCache + Freeze** 3종 규약을 깨지 말 것 — 위 "프레임 로컬 삭제 안 됨" 파일 잠금 수정의 본체다 |
| ~~업로드 진행률 테스트 flaky~~ | `UploadServiceTests.Upload_Reports_Stage_Progress_In_Order`가 전체 스위트 실행 시 간헐 실패(단독 실행은 항상 통과, 4회 중 1회 관측). 원인은 **제품 코드가 아니라 테스트 단언**: `UploadService.MakeStageProgress`가 쓰는 `System.Progress<T>`는 캡처된 SynchronizationContext(테스트 환경엔 없음 → 스레드풀)로 콜백을 **비동기 게시**하므로, 파일 단위 보고가 동기 호출인 `Finalizing`보다 늦게 도착할 수 있다. `stages[^1] == Finalizing` 단언이 제품이 보장하지 않는 성질을 요구했고, 수집기 `CollectingProgress`도 `List<T>`를 락 없이 여러 스레드에서 변경했다 | `tests/MCPhoto.Tests/UploadServiceTests.cs`, `Core/Upload/UploadService.cs:95-98`(원인 지점, **무수정**) | **기존 잠복 결함 — it15에서 발견·테스트 수정으로 해소(2026-07-28)**: it15가 만든 회귀가 아니다(`UploadService`는 `MCPhoto.Firebase`→`MCPhoto.Core` 이관 시 본문 바이트 동일, 테스트도 `using` 1줄만 변경). 테스트 증가(610→613)로 스레드풀 경합이 늘며 드러났다. 조치 = **테스트만 수정**: 수집기를 `lock` + 스냅샷으로 스레드 안전화, `Finalizing`은 위치 대신 존재만 단언(`Assert.Contains`), 순서 단언은 **동기 보고만**(단계 시작 마커 `Fraction==0.0`) 골라 `[Photo, Timelapse]` + `Finalizing > Timelapse 시작`으로 재작성. ⚠️ 실제 앱에서 `Progress<T>`가 UI SynchronizationContext로 마샬링하는 것은 **의도된 올바른 동작**이므로 제품 코드는 건드리지 않았다. 전체 스위트 8회 연속 무실패 확인 |
| 프레임 편집 fork 시 옛 이름 파일 잔존 | 고급 유저가 자기 로컬 프레임 **이름을 바꿔 저장**하면 `SaveLocal`이 새 파일명으로만 쓰기 때문에 옛 `{계정}_{옛이름}.png`/`.slots`가 남는다(it15 이전부터의 기존 동작, 범위 밖으로 유지) | `Core/Frames/LocalFrameStore.cs` | 대기: 이름 변경 시 옛 파일 정리 여부 결정 필요(삭제 vs 유지) |
| ~~설정 저장 시 창모드 창이 옛 위치·크기로 점프~~ | 창모드에서 설정을 저장하면 창이 ini에 남아 있던 과거 `WindowBounds`로 점프하고, 최대화 상태로 저장하면 `WindowState=Normal` 강제로 원복됐다. 원인은 `ApplyDisplaySettings`가 ① 시작 복원과 ② 런타임 모드 변경을 겸하면서 **동일 모드 저장에도 기하를 재적용**한 것(`WindowBounds`는 창 닫을 때만 갱신됐다) | `App/MainWindow.xaml.cs`, `App/ViewModels/SettingsViewModel.cs`, `Core/Settings/DisplayApplyPolicy.cs` | **수정 완료(it16, 2026-07-29)**: 순수 정책 `DisplayApplyPolicy` 신설 + `_appliedMode` 도입으로 **모드가 실제로 바뀔 때만** 창에 손댄다(동일 모드 저장은 완전 무동작). 저장 직전 현재 창 기하를 캡처해 `WindowBounds`를 신선하게 유지한다. 전체화면 ↔ 창모드 즉시 전환(it9 후속)은 유지. 상세 [11 §16](./11-exe-app-features.md#16-표시-모드전체화면창모드) |
| ~~`PUT /accounts/:id/pin` power 게이트 누락~~ | 타 계정 PIN 재설정 라우트가 로그인 + `canManage`(같은 위계 허용)만 요구해 **`temp_user`가 다른 `temp_user`의 PIN을 재설정**할 수 있었다. 형제 라우트(`DELETE /accounts/:id`·`PATCH /accounts/:id/role`)에는 있던 `requirePower()`가 PIN에만 빠져 있었고, it15로 신규 SSO 계정이 전원 `temp_user`가 되며 모집단이 커졌다 | `web/functions/src/routes/accounts.ts`, `App/ViewModels/UserMgmtViewModel.cs` | **수정 완료(it16, 2026-07-29)**: 라우트에 `requirePower()` 추가(비power 403) + 클라 `CanResetPin`·커맨드 가드에 `IsPower()` 항 추가. `canManage` 자체는 **무변경**(`deleteAccount`와 공유 — 좁히면 admin↔admin 삭제가 회귀). 본인 PIN 변경(`PUT /accounts/me/pin`)은 영향 없음 |
| power가 fork 저장한 공용 로컬 프레임을 다시 편집할 수 없다 | 공용 스코프 저장분은 `UserId=null`로 로드되어 `FrameEditPolicy.CanEdit`의 `UserLocal → IsOwnedLocal` 판정에서 탈락한다. it15부터의 성질이며 it16 범위(역할 재배분)와 무관해 손대지 않았다 | `Core/Frames/FrameEditPolicy.cs`, `Core/Frames/LocalFrameStore.cs` | 대기: `DbDefault`처럼 power 우회를 둘지, 공용 로컬분에 소유자 메타를 남길지 결정 필요 |
| 공용 로컬 프레임 삭제가 소유자·power로 제한되지 않음 | `FrameEditPolicy.CanDelete`는 **소유자를 보지 않는다** — 프레임 쓰기 권한(고급 유저 이상)이면 다른 power가 fork 저장한 공용 로컬 프레임의 파일을 지울 수 있다(서버 문서는 불변). it15에서도 `user`가 가능했던 **기존 성질**이며, it16은 "고급 유저 = it15 user 권한 전체"를 확정했으므로 좁히지 않았다 | `Core/Frames/FrameEditPolicy.cs` | 대기: 좁히려면 공용 로컬 저장분에 소유자 식별 수단이 먼저 필요(위 항목과 한 덩어리) |
| `CreatableRoles`/`canCreate` 데드코드 | it15의 계정 생성 폐지로 프로덕션 호출자가 0(테스트만 참조). it16에서 목록만 새 역할 매트릭스와 맞춰 드리프트를 막았다 | `Core/Models/UserRole.cs`, `web/functions/src/domain/roles.ts` | 대기: 제거 시 관련 테스트까지 연쇄 — 계정 생성 재도입 가능성 판단 후 결정 |
| 서버 잔존 라우트 `PUT /frames/:id` | it15 정책상 앱은 호출하지 않는다(편집 저장은 로컬 전용). 운영/관리 전용으로 남겨 둔 상태 | `web/functions/src/routes/frames.ts` | 대기: 유지(운영 도구) vs 제거 결정 필요 |
| `MainWindow`의 표시모드·기하 책임이 코드비하인드에 남음 | it16에서 **판정**은 순수 정책(`DisplayApplyPolicy`)으로 뽑았지만 **적용**(`WindowStyle`/`WindowState`/`Left`·`Top`·`Width`·`Height`)은 여전히 `MainWindow` 코드비하인드라 단위 테스트 불가 영역이 남는다 | `App/MainWindow.xaml.cs` | 대기: `IWindowGeometryService` 류 추상화는 별 이터레이션 과제 |
| 창 이동·리사이즈 시 `WindowBounds` 실시간 반영 없음 | 캡처 시점은 **설정 저장 시**와 **종료 시** 두 곳뿐이다(it16에서 전자를 추가). 그 사이에 강제 종료되면 위치가 유실된다 | `App/MainWindow.xaml.cs` | 대기: `LocationChanged`/`SizeChanged` 구독은 이벤트 해제·디바운스 설계가 필요 |
| `FrameSelectViewModel.IsLoggedIn` 미사용 잔존 | it16에서 "프레임 만들기" 버튼 바인딩이 `IsLoggedIn` → `CanCreateFrame`으로 옮겨져, 이 프로퍼티는 **할당만 되고 소비처가 없다**(XAML·VM 어디에서도 읽지 않음) | `App/ViewModels/FrameSelectViewModel.cs:77` | 대기(리뷰 제안, 비차단): 제거 여부 결정 — 무해하지만 드리프트 신호 |
| 로그아웃이 JWT를 비우지 않음 | `IBackendSession.Clear()`의 **프로덕션 호출자가 0**이다 — 로그아웃은 `SessionContext.CurrentUser`만 해제하고 토큰 홀더는 그대로 둔다. 계정 라우트는 화면 진입 자체가 로그인을 요구해 UI로는 도달하지 않지만, **업로드는 "선택적 Bearer"** 라서 로그아웃 직후 **게스트 촬영의 `uploads/prepare`·`uploads/commit`에 직전 계정 JWT가 붙는다** → 서버가 그 계정 소유로 처리(TempUser면 `qrUsedCount`까지 증가). 토큰 자체 만료는 기본 8시간 | `App/AppShellViewModel.cs:446-453`, `Http/Session/BackendSession.cs:34-41`, `Http/HttpFirebaseClient.cs:96`·`:143` | 대기(2026-07-29 문서 재작성 중 발견, 미검증 잠복): `Logout`에서 `IBackendSession.Clear()` 호출 또는 `CurrentUserChanged` 구독으로 자동 소거. 상세 [60 §3.5](./60-auth-accounts-and-roles.md#35-로그아웃--세션-유지-규칙중요) |

## 2. 다음 착수 예정 (우선순위 큐)

> it11에서 대기 큐를 파이프라인(Opus 설계→개발→리뷰, Fable 최종검증)으로 진행(2026-07-24).
> **#14·#15·#16 완료, #13은 전체 재촬영만 완료.** 아래 컷별 재촬영만 남음.

### #13 컷별 재촬영 (기능) — **남은 부분, USER-DECISION 대기**
- it11에서 **전체 재촬영 + 설정(재촬영 토글·횟수 1~3)** 완료(`8f0d2fc`). 컷별 재촬영만 미구현.
- **보류 사유**: 컷별 재촬영 **버튼의 UI 배치·인터랙션**(썸네일 우하단 ↺ 오버레이 vs 별도 모드)이 사용자 결정 필요.
- 승인 시 한 덩어리로: `AppSettings.PerCutRetake` + "컷별 재촬영 활성화" 토글 + `CaptureSession` per-cut 카운터(`ReplaceCut`/`CanPerCutRetake`) + `SessionContext.RetakeTargetCut` + `SessionStateMachine`의 `CutSelect→Capture` 전이(**회귀 테스트 필수**) + `CaptureViewModel` 단일 컷 플로우 + 썸네일 ↺ 버튼. 규칙: **각 컷 1회만**, **전체 재촬영을 한 세션에선 컷별 미제공**. 설계 상세 [it11](../design/wpf-it11-deferred-features-design.md).

### ~~#14 진단/상태 화면~~ — **완료(it11, `eb465df`)**: 설정 [고급] 로그인 전용 버튼 → 모달(카메라/ffmpeg/Firebase 헬스체크 + 로그 폴더 열기).
### ~~#15 카메라 장치 FriendlyName~~ — **완료(it11, `10a8d02`)**: WMI best-effort 이름 + 인덱스 기준 동작·폴백.
### ~~#16 업로드 진행률/재시도 UX~~ — **완료(it11, `0532df0`)**: GCS 파일단위 진행률 배선 + 진행 바/재시도.

> **it11 세부 문서 동기화 대기(11·12)**: 재촬영·진단·카메라명·업로드 진행률을 세부 문서에 반영(§1 "문서 동기화 지연" 항목과 함께).

## 2.1 추후 개선 (장기 — 미룸, 사용자 "추후 개선" 확정)

- **사진 인쇄**(프린터 출력) — 포토부스 핵심이나 규모 큼.
- **다국어(한/영) UI i18n** — 브랜딩 이름 외 라벨 전환.
- **스티커/텍스트 오버레이** — 결과 꾸미기(필터 외 데코).
- **사용량 통계 대시보드**(관리자) — 일별 촬영 수·세션 로그.
- ~~**비밀번호 해시/솔트**~~ — **소멸(it15)**: 비밀번호 인증 폐지로 저장할 비밀번호가 없다(자격증명 = bcrypt `pinHash`).
- **PIN 시도 제한 강화** (2026-07-29 사용자 "좀 더 고민" 보류) — 현재 방어는 **클라 측뿐**이다(연속 5회 실패 시 창 닫힘 + 실패마다 1.5초 입력 비활성). 서버 `verifyPin`에는 시도 카운터가 없어 4자리(1만 조합)에 대한 온라인 브루트포스가 이론상 가능하다.
  - 사용자 아이디어: **5회 실패 시 5분 잠금**.
  - ⚠️ 설계 시 주의: **계정 단위 잠금은 DoS를 만든다** — 남의 PIN을 일부러 5회 틀려 그 계정을 잠글 수 있다. it15가 서버 잠금을 채택하지 않은 이유가 이것이다. 대안은 **기기(PIN 입력 창) 단위 잠금** 또는 IP 단위 rate limit(Cloud Armor 계층). 물리 접근이 전제인 키오스크에서는 기기 단위가 위협 모델에 더 맞는다.
  - 관련: it15 설계 §5.6(서버 잠금 미채택 근거), §12 R1.
- **로컬 결과물(`result\`) 보관/정리 정책** — 현재 무기한 영구 보관.
- **키오스크 모드 강화**(자동 시작·종료 차단) + **오프라인(네트워크 끊김) 감지 안내**.
- ~~**서비스 계정 키 관리 재설계(상용화 시)**~~ — **해소(it15, 2026-07-28)**: 서버 프록시 이전이 완료돼 앱이 `serviceAccountKey.json`을 전혀 쓰지 않는다. 레거시 Firebase 직결 경로(`MCPhoto.Firebase`)가 삭제됐고 모든 DB·Storage 접근은 백엔드 HTTPS API를 경유한다. "exe 폴더 보유자가 DB admin 접근 가능"하던 구조적 문제는 사라졌다(exe에 남은 것은 백엔드 게이트 키뿐이며, 그 키로는 서버가 강제하는 역할 권한을 넘지 못한다). `installer/MCPhoto.iss`의 키 파일 제외 목록과 `.gitignore` 항목은 **방어적으로 유지**한다(과거 산출물·개발 PC 잔존분 대비).

## 3. 비범위 / 향후 검토 (현재 명시적 제외)

| 항목 | 사유 |
|------|------|
| SSO / 외부 IdP 로그인 | it8 비범위로 명시 |
| 세션 만료(자동 로그아웃) | it8 비범위(유휴는 홈 복귀만, 로그아웃 없음) |
| 다국어 전면 지원(i18n) | it9 비범위(브랜딩 이름만 외부화) |
| 스케줄 Cloud Functions 정리 | 미채택(D-2) — GCS Lifecycle + Firestore TTL로 대체([50](./50-infra-gcp-lifecycle-and-ttl.md)) |
| 하드웨어 플래시 | 플래시는 화면 하양 오버레이로 구현(하드웨어 제어 없음) |
| QR 화면 다운로드 링크/코드 텍스트 병기 | 사용자 "해당 경우 없다" 판단으로 미채택 |
| 계정 저장소를 Realtime Database로 이전 | Firestore 유지(쿼리·TTL·보안규칙·일관성). RTDB 이전은 이점 상실로 비권장 |
| 역할별 프레임 개수 한도 차등 | it16 비범위 — 한도는 계정당 10개로 역할 무관 유지 |
| 프레임 소유권 이전·마이그레이션 UI | it16 비범위(E4) — 프레임 권한을 잃은 `user`·`temp_user`의 기존 프레임은 **그대로 두고 읽기 전용**(목록 노출·촬영 사용 유지, 편집·삭제만 불가). 파일 삭제·이관·정리 UI를 만들지 않는다 |
| 프레임 목록에서 권한 없는 계정의 기존 프레임 숨기기 | it16 비범위(E4가 노출 유지를 확정) — 숨기면 촬영에 쓰던 프레임이 사라져 체감 회귀가 된다 |
| 고급 유저 승격 요청 워크플로우 | it16 비범위 — 승격은 관리자·매니저가 사용자 관리 화면에서 직접 지정하는 수동 동선뿐([60 §1.4](./60-auth-accounts-and-roles.md#14-역할-지정변경-매트릭스)) |
| `advanced_user`용 서버측 프레임 권한 축 | it16 비범위 — 고급 유저의 프레임은 **개인 로컬 저장 전용**이라 서버 쓰기 요청이 발생하지 않는다. 프레임 쓰기 라우트는 계속 `requirePower()` 뒤에 두고 `isPower`를 확장하지 않는다(회귀 테스트 `web/functions/src/__tests__/authGates.test.ts`가 고정) |

## 4. 보관/만료 정합성 메모

- 세션별 `retentionHours`(1~72h)는 **접근 만료**(웹이 `expiresAt`로 차단)에 정확히 반영됨.
- **물리 파일 삭제**는 GCS Lifecycle **고정 age 3일** 기준이라 세션별 시각과 다름(설계상 허용). 정확한 시각 물리삭제가 필요해지면 `PurgeExpiredAsync` 연결 또는 age 조정 검토. 상세 [50](./50-infra-gcp-lifecycle-and-ttl.md).

## 5. 유지보수 규칙

- 기능 추가/변경 시 [11-exe-app-features](./11-exe-app-features.md) 등 해당 세부 문서를 함께 갱신.
- 이슈를 수정하면 위 표에서 제거하고, 해결 내용을 세부 문서에 반영.
- 이 문서는 "미해결 항목의 단일 진실"로 유지 — 여기 없으면 대기 항목이 없다는 뜻이어야 함.


## 6. 개발자 직접 작성

 - 개발자 문의와 같은 공간도 만들어주고 싶어. 그런데, 위치가 좀 애매해. 설정에 들어가기도 좀 그렇고...
 - 적당한 위치가 있다면 작성하면 좋겠어. (예를들면 현재 버전을 작성하는 곳을 앱 하단으로 지정했는데, 제거하고, 별도로 설정 안에 "버전 확인"과 같은 버튼을 만들고 모달을 띄울 때, 개발자 문의 라는 공간을 만들어도 될 것 같아.)
 - 내 개발자 이메일은 devmcjo@gmail.com 이니까 이부분도 참고해서 만들어주면 좋을 것 같아.

## 7. 향후 플랫폼 확장 — 웹 · Android (추후 논의)

> 2026-07-27 사용자 확정: **웹·Android 버전도 추후 개발 예정.** 지금은 기록만; 착수 시 재논의.

- 현재 Google SSO OAuth 클라이언트는 **Desktop app 유형 1개**(WPF 키오스크용, client_id `712395684881-l66o...apps.googleusercontent.com`, loopback+PKCE)뿐이다.
- 웹/Android는 **각각 별도 OAuth 클라이언트 유형**이 필요하다(공유 불가):
  - **웹**: "웹 애플리케이션" 유형 — 정확한 리디렉션 URI 등록 필요. 브라우저는 client_secret 은닉 불가라 PKCE/서버 교환 설계 필요.
  - **Android**: "Android" 유형 — 패키지명 + SHA-1 지문 등록.
- 백엔드(`/auth/google` code 교환·verifyIdToken)는 **audience(client_id) 다중 허용**으로 확장하면 재사용 가능(현재는 단일 `GOOGLE_OAUTH_CLIENT_ID`). 착수 시 config를 client_id 목록으로 일반화 검토.
- 동의 화면·이메일 인증·계정/역할 백엔드는 플랫폼 공통(재사용). UI/OAuth 리디렉션만 플랫폼별.
