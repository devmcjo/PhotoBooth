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
| 설정 페이지 역할 게이트 없음 | `OpenSettings`에 권한 가드 없음 — 게스트도 설정 진입 가능 | `AppShellViewModel.OpenSettings` | 검토 필요(키오스크 정책에 따라 파워 전용으로 제한할지) |
| 비밀번호 평문 저장 | `users` 문서에 비밀번호 평문(MVP) | `Firebase/AccountService.cs`, `web/firestore.rules` | 개선 예정(해시/솔트, 규칙 강화) |
| 인스톨러 self-contained 불일치 | `installer/MCPhoto.iss` 주석은 `--self-contained false` 예시, 실제 `publish.ps1`은 `true`(단일 파일) | `installer/MCPhoto.iss`, `publish.ps1` | 확정 필요(배포 방식 통일) |
| ffprobe 잔존 | `tools/ffmpeg/ffprobe.exe`(~101MB)가 리포에 있으나 코드 미사용·배포 제외 | `tools/ffmpeg/` | 정리 권장(리포에서 제거) |
| Preview 데드코드 | `PreviewView`/`PreviewViewModel`이 어떤 `AppState`에도 매핑 안 됨 | `Views/PreviewView.*`, `ViewModels/PreviewViewModel.cs` | 정리 권장(제거 or 활용) |
| 만료 물리삭제는 인프라 의존 | `PurgeExpiredAsync` 코드 존재하나 앱에서 호출 안 함 → GCS Lifecycle/Firestore TTL 설정에 의존 | `Firebase/UploadService.cs` | 의도된 설계([50](./50-infra-gcp-lifecycle-and-ttl.md)). 인프라 미설정 시 미삭제 주의 |

## 2. 개선 예정 (단기)

- **프레임 삭제 완전화**(위 1번) — 최우선.
- 카메라 장치 표시명: 현재 `"Camera {index}"`(DShow FriendlyName 미조회). 여러 대 구분 개선 여지(별도 이터레이션).
- 로컬 결과물(`result\`) 보관 정책: 현재 무기한 영구 보관 — 정리 옵션 검토.
- 설정 항목 권한 분리(파워 전용 vs 공용) 재검토.

## 3. 비범위 / 향후 검토 (현재 명시적 제외)

| 항목 | 사유 |
|------|------|
| SSO / 외부 IdP 로그인 | it8 비범위로 명시 |
| 세션 만료(자동 로그아웃) | it8 비범위(유휴는 홈 복귀만, 로그아웃 없음) |
| 다국어 전면 지원(i18n) | it9 비범위(브랜딩 이름만 외부화) |
| 스케줄 Cloud Functions 정리 | 미채택(D-2) — GCS Lifecycle + Firestore TTL로 대체([50](./50-infra-gcp-lifecycle-and-ttl.md)) |
| 하드웨어 플래시 | 플래시는 화면 하양 오버레이로 구현(하드웨어 제어 없음) |

## 4. 보관/만료 정합성 메모

- 세션별 `retentionHours`(1~72h)는 **접근 만료**(웹이 `expiresAt`로 차단)에 정확히 반영됨.
- **물리 파일 삭제**는 GCS Lifecycle **고정 age 3일** 기준이라 세션별 시각과 다름(설계상 허용). 정확한 시각 물리삭제가 필요해지면 `PurgeExpiredAsync` 연결 또는 age 조정 검토. 상세 [50](./50-infra-gcp-lifecycle-and-ttl.md).

## 5. 유지보수 규칙

- 기능 추가/변경 시 [11-exe-app-features](./11-exe-app-features.md) 등 해당 세부 문서를 함께 갱신.
- 이슈를 수정하면 위 표에서 제거하고, 해결 내용을 세부 문서에 반영.
- 이 문서는 "미해결 항목의 단일 진실"로 유지 — 여기 없으면 대기 항목이 없다는 뜻이어야 함.
