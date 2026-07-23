---
name: it10-server-key-distribution
description: it10 베타 서버 연동 — 키 번들은 publish.ps1 레벨(csproj 금지), 시드 폴백 유지 권장, 기본 프레임 이름 '_' 금지 규약
metadata:
  type: project
---

it10(베타/QA 서버 연동)의 핵심 결정: **서비스 계정 키는 publish.ps1 스크립트 레벨에서만 번들**한다
(`publish\MCPhoto\serviceAccountKey.json` 복사). csproj AfterTargets=Publish 방식은 기각.

**Why:** 키는 빌드 자산이 아니라 배포 판단이 필요한 비밀. csproj에 넣으면 IDE/CLI publish 직접 호출 시
의도치 않게 키가 포함된다. 스크립트 레벨이면 포함 여부·출처가 콘솔에 명시되고 `-NoServiceKey`로 제외 가능.
`FirebaseClient.DefaultKeyPath()`가 실행폴더를 1순위로 탐색하므로(exe 옆 키 자동 로드) 앱 코드 변경 불필요.

**How to apply:**
- 설계·WBS: `docs/design/wpf-it10-server-connectivity-design.md` / `wpf-it10-wbs.md`
- Admin SDK 키 배포 = exe 보유자 전원 DB admin 접근(앱 역할 게이트는 표면). 내부 베타 한정 수용,
  릴리즈 전 필수 백로그: 키 회전 + 클라이언트 SDK/보안 규칙 이전(B안).
- 미확정 결정(사용자 승인 대기): D1 오프라인 시드 devmcjo/1111 인메모리 폴백 유지 여부(권장: 유지+오프라인 배너),
  D2 publish 키 포함 기본값(권장: 기본 포함), D3 기본 프레임 이름 `_` 금지 데이터 규약(권장: 수용).
- 함정: `LocalFrameStore`의 공용/user 구분이 파일명 `_` 유무 규약 → 이름에 `_` 포함 기본(isDefault) 프레임은
  공용 목록·dedup에서 제외되어 매 진입 재다운로드(표시는 됨). 프레임 파일명 관련 설계 시 항상 이 규약 확인.

관련: [[mcphoto-settings-ini-infra]] (실행경로 우선 폴백 체인 관례 — 키 탐색도 동일 패턴)
