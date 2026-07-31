---
name: it15-frame-local-only
description: it15 프레임 편집=로컬 전용 계약(fork 저장·#dbid 미기록·서버 PUT 미호출), DB insert는 팝업 체크 opt-in, F2는 fork 아님, FrameCatalogService가 절대 빈 목록을 안 주는 테스트 함정
metadata:
  type: project
---

it15부터 **프레임 편집은 해당 PC에서만 적용**된다(설계: `docs/design/wpf-it15-frame-ux-design.md`).

**Why:** 한 부스에서 편집한 프레임이 서버 기본 프레임을 바꿔 다른 부스까지 오염되는 것을 막는다.
it2의 "로컬만 / DB도 업데이트" 팝업이 이 정책과 정면 충돌해 클라이언트에서 전면 제거됐다.

**How to apply:**
- `FrameEditPolicy.RequiresFork(frame)`(출처가 `UserLocal`이 아니면 true)이 fork 판정의 유일한 축이다.
  fork 저장은 `FrameTemplate.Id = ""`로 `SaveLocal(ownerName: null)`을 호출한다 → `.slots`에 `#dbid`가
  기록되지 않아 로컬 사본이 `local:{파일명}`이 되고 서버 문서와 연결이 끊긴다. **이 Id="" 규약을 깨지 말 것.**
- 사본 이름은 `FrameNaming.NextCopyName`이 만든다. `_`는 `LocalFrameStore`의 공용/user 구분자라
  **새 이름에 절대 도입하지 않는다**(도입하면 `LoadPublic`에서 탈락해 목록에서 사라진다).
- 원본 이름이 `PublicFrameNames()`에 남아야 `FrameCatalogService`의 **이름 기준 dedup**이 유지되어
  DB 재다운로드가 발생하지 않는다. fork가 원본 파일을 건드리지 않는 이유가 이것이다.
- **서버 라우트 `PUT /frames/{id}`는 살아 있지만 앱은 호출하지 않는다**(운영/관리 전용).
  앱 동작을 근거로 이 라우트를 지우지 말 것 — `docs/design/firebase-contract.md` §2.2에 명시해 뒀다.
- 앱이 `frameTemplates`에 쓰는 유일한 경로는 **파워의 프레임 신규 생성**(`POST /frames`)이다.
  단 그 경로도 **무조건 insert가 아니다**: 저장 시 "서버에도 등록" 확인 팝업이 뜨고 체크(기본 off)했을 때만
  `SaveAsync`를 호출한다. 미체크면 로컬 공용만(`Id=""` → `#dbid` 미기록). 게이트는 `IsPower()` + 세션 `New`
  두 항이며 `CanWriteFrames()`로 바꾸면 DB 권한 없는 AdvancedUser에게 체크박스가 노출된다.
- **F2 "기존 프레임 불러오기"는 fork가 아니다**(사본 아님). `ApplyPickedFrame`은 세션을 `New`로 두고
  `FrameName`을 건드리지 않는다 → 사본 자동 네이밍이 없다. 원본 보호는 저장 전 **이름 충돌 가드**가
  담당한다(`EditOwnLocal`만 덮어쓰기 예외). fork(`NextCopyName`)는 F1 `LoadForEdit` 전용이다.

**테스트 함정**: `FrameCatalogService.GetDefaultFramesAsync()`는 **절대 빈 목록을 반환하지 않는다**
(로컬 공용 → DB → 번들 폴더 → 마지막에 `EnsureFallbackFrame()`이 항상 1개를 만든다).
따라서 "후보 0개" 분기는 단위 테스트로 도달 불가 → 실패 경로(`ILocalFrameStore.LoadPublic` 예외)로 검증해야 한다.
또한 이 폴백은 `App.DataFolder\cache\fallback_frame.png`를 **실제로 디스크에 쓴다**(기존 테스트들도 동일).

관련: [[wpf-headless-window-test-pitfall]], [[encoding-verify-method]]
