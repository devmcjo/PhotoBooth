# 90 · 로드맵 & 향후 작업

| 항목 | 값 |
|------|-----|
| 문서 | 알려진 이슈·기술 부채·개선 예정·비범위 |
| 범위 | 미해결/대기 항목의 단일 집합소. 완료되면 이 문서에서 제거하고 해당 세부 문서로 반영 |
| 최종 업데이트 | 2026-07-28 |
| 갱신 규칙 | 이슈 발견·수정·범위 결정 시 즉시 이 문서 갱신. "상태" 컬럼 유지 |

---

## 1. 알려진 이슈 / 기술 부채

| 항목 | 현상 | 위치 | 상태 |
|------|------|------|------|
| ~~프레임 로컬 삭제 안 됨~~ | 썸네일 `Image`가 png 파일을 잠가 `File.Delete` 실패(예외 삼킴) → png 잔존 | `Views/FrameSelectView.xaml`, `Core/Frames/LocalFrameStore.cs`, `ViewModels/FrameSelectViewModel.cs` | **수정 완료(2026-07-23)**: `FilePathToImageConverter`(OnLoad+IgnoreImageCache)로 파일 잠금 해소 → 삭제 성공. `DeleteLocal`은 png 존재 여부로 정직 반환, `ConfirmDelete`가 실패 시 안내(성공 오인 금지) |
| ~~설정 진입 권한 게이트~~ | 게스트 QR/Firebase 소스단 off(ini 불변) + 로그인 시 비밀번호 가드 | `SettingsViewModel`, `SettingsView`, `ResultViewModel` | **완료(2026-07-23, 보완#1)** |
| 문서 동기화 지연 | 셔터음(#7)·권한게이트(보완#1)·설정 레이아웃이 11·12 세부 문서에 아직 미반영 | `docs/analysis/11`, `docs/analysis/12` | **대기**: 다음 기능 작업 시 함께 갱신 |
| 비밀번호 평문 저장 | `users` 문서에 비밀번호 평문(MVP) | `Firebase/AccountService.cs`, `web/firestore.rules` | 개선 예정(해시/솔트, 규칙 강화) |
| 인스톨러 self-contained 불일치 | `installer/MCPhoto.iss` 주석은 `--self-contained false` 예시, 실제 `publish.ps1`은 `true`(단일 파일) | `installer/MCPhoto.iss`, `publish.ps1` | 확정 필요(배포 방식 통일) |
| ~~ffprobe 잔존~~ | `tools/ffmpeg/ffprobe.exe` 코드 미사용 | `tools/ffmpeg/` | **정리 완료(2026-07-23)**: 삭제 |
| ~~Preview 데드코드~~ | `PreviewView`/`PreviewViewModel` 미매핑 | — | **정리 완료(2026-07-23)**: 파일·DI 등록 제거 |
| 만료 물리삭제는 인프라 의존 | `PurgeExpiredAsync` 코드 존재하나 앱에서 호출 안 함 → GCS Lifecycle/Firestore TTL 설정에 의존 | `Core/Upload/UploadService.cs` | 의도된 설계([50](./50-infra-gcp-lifecycle-and-ttl.md)). 인프라 미설정 시 미삭제 주의 |
| 프레임 피커 썸네일 가상화 미적용 | "기존 프레임 불러오기" 모달(과 프레임 선택 화면)이 `WrapPanel` ItemsPanel로 **UI 가상화가 꺼져** 있고 `DecodePixelWidth`도 미적용 → 후보 수가 늘면 모달 오픈이 지연될 수 있다. 현재 상한(공용 소수 + 계정당 최대 10)에서는 수용 | `Views/FrameEditorView.xaml`, `Views/FrameSelectView.xaml`, `Themes/Controls.xaml`(`FrameCard.Content`) | 후속 과제(it15 F2-D5 수용). ⚠️ 개선 시 `FilePathToImageConverter`의 **OnLoad + IgnoreImageCache + Freeze** 3종 규약을 깨지 말 것 — 위 "프레임 로컬 삭제 안 됨" 파일 잠금 수정의 본체다 |
| ~~업로드 진행률 테스트 flaky~~ | `UploadServiceTests.Upload_Reports_Stage_Progress_In_Order`가 전체 스위트 실행 시 간헐 실패(단독 실행은 항상 통과, 4회 중 1회 관측). 원인은 **제품 코드가 아니라 테스트 단언**: `UploadService.MakeStageProgress`가 쓰는 `System.Progress<T>`는 캡처된 SynchronizationContext(테스트 환경엔 없음 → 스레드풀)로 콜백을 **비동기 게시**하므로, 파일 단위 보고가 동기 호출인 `Finalizing`보다 늦게 도착할 수 있다. `stages[^1] == Finalizing` 단언이 제품이 보장하지 않는 성질을 요구했고, 수집기 `CollectingProgress`도 `List<T>`를 락 없이 여러 스레드에서 변경했다 | `tests/MCPhoto.Tests/UploadServiceTests.cs`, `Core/Upload/UploadService.cs:95-98`(원인 지점, **무수정**) | **기존 잠복 결함 — it15에서 발견·테스트 수정으로 해소(2026-07-28)**: it15가 만든 회귀가 아니다(`UploadService`는 `MCPhoto.Firebase`→`MCPhoto.Core` 이관 시 본문 바이트 동일, 테스트도 `using` 1줄만 변경). 테스트 증가(610→613)로 스레드풀 경합이 늘며 드러났다. 조치 = **테스트만 수정**: 수집기를 `lock` + 스냅샷으로 스레드 안전화, `Finalizing`은 위치 대신 존재만 단언(`Assert.Contains`), 순서 단언은 **동기 보고만**(단계 시작 마커 `Fraction==0.0`) 골라 `[Photo, Timelapse]` + `Finalizing > Timelapse 시작`으로 재작성. ⚠️ 실제 앱에서 `Progress<T>`가 UI SynchronizationContext로 마샬링하는 것은 **의도된 올바른 동작**이므로 제품 코드는 건드리지 않았다. 전체 스위트 8회 연속 무실패 확인 |
| 프레임 편집 fork 시 옛 이름 파일 잔존 | user가 자기 로컬 프레임 **이름을 바꿔 저장**하면 `SaveLocal`이 새 파일명으로만 쓰기 때문에 옛 `{계정}_{옛이름}.png`/`.slots`가 남는다(it15 이전부터의 기존 동작, 범위 밖으로 유지) | `Core/Frames/LocalFrameStore.cs` | 대기: 이름 변경 시 옛 파일 정리 여부 결정 필요(삭제 vs 유지) |

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
- **비밀번호 해시/솔트** — 현재 평문(MVP). 릴리즈 전 필수(위 1번 표와 동일 항목).
- **로컬 결과물(`result\`) 보관/정리 정책** — 현재 무기한 영구 보관.
- **키오스크 모드 강화**(자동 시작·종료 차단) + **오프라인(네트워크 끊김) 감지 안내**.
- **서비스 계정 키 관리 재설계(상용화 시)** — 현재 베타는 Admin 서비스 계정 키를 publish 산출물(`publish\MCPhoto\serviceAccountKey.json`)에 **기본 포함**한다(사내 관리 전제, 사용자 결정 2026-07-23). exe 폴더 보유자는 DB admin 접근 가능(앱 역할은 표면 게이트)이라 **외부 판매/배포 시엔 부적합** → 판매 가정 시 Firebase 클라이언트 SDK+보안 규칙 이전, 키 회전, 또는 서버 프록시 등으로 재설계 필요. 키 미포함 배포가 필요하면 `publish-nokey.bat`(또는 `-NoServiceKey`) 사용. 상세 [it10 설계](../design/wpf-it10-server-connectivity-design.md).

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
