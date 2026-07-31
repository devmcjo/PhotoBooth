---
name: it15-frame-local-only-policy
description: it15 프레임 편집 로컬 전용 정책 — DB 업데이트 경로 제거, DB/번들 유래 편집은 fork 저장, PUT /frames/{id}는 서버에만 잔존
metadata:
  type: project
---

it15부터 **프레임 편집은 해당 PC 로컬 전용**이다. it2의 "로컬만 / DB도 업데이트" 팝업·`FrameDiff`·
`IFrameRepository.UpdateAsync`/`SupportsUpdateById`는 **클라이언트에서 전면 제거**하되, 서버 라우트
`PUT /frames/{id}`(`web/functions/src/routes/frames.ts`)는 **유지**한다(앱 미호출, 운영/관리 전용).

**Why:** 사용자 결정 — 서버 동기화·재업로드·타 PC 재다운로드의 복잡도를 피하려는 것. 서버 라우트까지
지우면 브리프가 확정한 제거 범위를 넘고 firebase-contract·jest가 연쇄 변경되어 이터레이션 리스크가 커진다.
클라이언트 호출이 0이면 정책은 완전히 지켜진다.

**How to apply:**
- 설계 문서: `docs/design/wpf-it15-frame-ux-design.md` (F1 §3 / F2 §4 / WBS §6, 9단계)
- **fork 규칙**: `FrameEditPolicy.RequiresFork(frame)` = 출처가 `UserLocal`이 아니면 true.
  DB/번들 유래 프레임을 편집·복사하면 원본 파일을 보존하고 `FrameNaming.NextCopyName`으로
  `{원본} 사본[ N]` 새 이름 저장 + `FrameTemplate.Id=""`로 `#dbid` 미기록.
  자기 로컬 프레임 편집은 종전대로 같은 이름 덮어쓰기.
- **재다운로드 무영향 근거**: `FrameCatalogService.GetDefaultFramesAsync`의 dedup 키가 **이름**이라
  원본 이름 파일이 로컬에 남아 있는 한 DB에서 다시 받지 않는다. 프레임 이름 규칙을 바꾸는 설계는
  반드시 이 dedup과 [[it10-server-key-distribution]]의 `_` 함정을 함께 검토할 것.
- **저장 스코프는 역할 유지**: power=공용 `{이름}.png`, user=개인 `{계정}_{이름}.png`.
  power의 **신규 생성만** 여전히 DB 등록(`SaveAsync`) — 배너(정책 고정)와 `SaveScopeNotice`(실제 결과)를
  분리해 두 문장 모두 참이 되게 했다.
- **후속 변경 예고(2026-07-30 설계 확정, 구현 대기)**: `docs/design/wpf-frame-create-from-existing-and-server-register-design.md`
  가 **F2 "기존 프레임 불러오기"를 fork/사본이 아닌 신규 생성(`New`)**으로 바꾸고(이름 자동 "사본" 제거 —
  사용자가 직접 입력), **power 신규 생성의 DB 등록을 저장 시 확인 팝업 체크박스(기본 off) opt-in**으로 바꾼다.
  F1 `LoadForEdit`의 fork·사본 규칙은 그대로다. 위의 "fork 규칙" 서술을 F2에 적용하지 말 것.
  덮어쓰기 방지는 `_sourceName` 가드 + **저장 스코프 동명 차단**(EditOwnLocal만 예외) 2중 방어로 설계했다.
- **모달은 오버레이로**: 새 `Window`를 만들면 테스트에서 인스턴스화가 불가하다
  (`.claude/agent-memory/wpf-developer/wpf-headless-window-test-pitfall.md`). 기존 팝업들과 같이
  `Brush.Scrim` Grid 오버레이 + VM 상태 프로퍼티로 설계하면 전 로직이 단위 테스트 가능하다.
  다이얼로그 서비스 추상화(`IPinPromptDialogService` 류)는 `Window`가 있을 때만 의미가 있다.
