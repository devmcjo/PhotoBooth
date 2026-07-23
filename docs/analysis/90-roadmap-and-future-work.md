# 90 · 로드맵 & 향후 작업

| 항목 | 값 |
|------|-----|
| 문서 | 알려진 이슈·기술 부채·개선 예정·비범위 |
| 범위 | 미해결/대기 항목의 단일 집합소. 완료되면 이 문서에서 제거하고 해당 세부 문서로 반영 |
| 최종 업데이트 | 2026-07-23 |
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
| 만료 물리삭제는 인프라 의존 | `PurgeExpiredAsync` 코드 존재하나 앱에서 호출 안 함 → GCS Lifecycle/Firestore TTL 설정에 의존 | `Firebase/UploadService.cs` | 의도된 설계([50](./50-infra-gcp-lifecycle-and-ttl.md)). 인프라 미설정 시 미삭제 주의 |

## 2. 다음 착수 예정 (우선순위 큐 — 사용자 확정, 태스크 등록됨)

> 다음 개발 세션에서 이 순서로 진행. (2026-07-23 기준)

### #13 재촬영 (기능) — 설정 옵션 + 촬영 플로우
- **설정 옵션(계층)**:
  - 재촬영 사용 **토글**(상위).
  - (on일 때) **재촬영 횟수 제한 콤보 1~3**.
  - (on일 때) **컷별 재촬영 활성화 토글**.
- **동작 규칙**:
  - **전체 재촬영**: 세션 전체를 다시 촬영(횟수 제한까지). 기존 `CutSelect→Guide` 경로 재사용 가능.
  - **컷별 재촬영**: "컷별 재촬영 활성화"가 켜진 경우에만 제공. **각 컷 1회만**. 단 **전체 재촬영을 한 번이라도 한 세션에서는 컷별 재촬영 미제공**.
- **영향**: `AppSettings`(RetakeEnabled/RetakeLimit/PerCutRetake) + INI 매핑 + `SettingsView` + `CaptureViewModel`/`CutSelectViewModel` 플로우 + 테스트.

### #14 진단/상태 화면 (기능)
- 카메라(연결·선택 상태)·ffmpeg(`IsAvailable`·경로)·Firebase(`IsInitialized`·버킷) **헬스체크** + **로그 폴더 경로·열기**.
- 진입: **로그인 상태에서 설정 화면 내 버튼**(권장). 관리자 현장 트러블슈팅용. 로그 위치는 [70](./70-logging-and-troubleshooting.md) 참조.

### #15 카메라 장치 FriendlyName (보완)
- 현재 `"Camera {index}"`(DShow FriendlyName 미조회) → 실제 장치명 조회로 여러 대 구분. 의존성(`System.Management`/P-Invoke) 검토.

### #16 업로드 진행률/재시도 UX (보완)
- QR 업로드(특히 타임랩스) **진행률 표시 + 재시도**. `IProgress` 배선(`UploadService`→`QrPopupViewModel`).

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
