---
name: design-doc-convention
description: docs/design 설계 문서 네이밍·WBS 배치 관례 — it13부터 WBS는 별 파일이 아니라 설계 문서 내 §절로 넣는다
metadata:
  type: project
---

`docs/design/` 관례:

- 파일명 = `{platform}-it{N}-{topic}-design.md` (예: `web-it17-download-share-design.md`, `wpf-it17-auto-cutcount-design.md`).
- **예외 — 키오스크 웹 클라이언트(`webclient/`)는 이터레이션이 아니라 WBS Step으로 진행**한다(`docs/web-client/11-wbs.md`). 그 단위의 설계 문서는 `web-step{N}-{topic}-design.md`를 쓴다(예: `web-step9-timelapse-encoder-design.md`). `web-it{N}-*`는 **다운로드 페이지(`web/`)** 쪽 문서다 — 두 "web"은 서로 다른 산출물이다.
  - ⚠️ Step 9·10·11은 `-design` 접미사가 있는데 **Step 12만 `web-step12-google-sso-auth.md`**(접미사 없음)다 — 팀리드가 파일명을 지정했다. 파일을 찾을 때 `web-step*` 로 glob한다.
- **이터레이션 번호는 플랫폼 간 공유**된다. 같은 it 번호에 `wpf-` 와 `web-` 문서가 병존할 수 있다(플랫폼 prefix가 구분자).
- **it10~it12는 `-wbs.md` 별 파일**을 뒀지만, **it13 이후는 설계 문서 안에 `## §N 구현 WBS` 절로 embed** 한다. 새 문서는 embed 방식을 따른다.
- 문서 구조: `§0 개요`(0.1 요구사항 원문 / 0.2 기술 스택 / 0.3 범위 경계 / 0.4 무회귀 하한) → `§1 검증된 사실(VF-n, 파일:라인)` → `§2 미검증 가정(OA-n → 검증 Step 매핑)` → 쟁점별 절 → `§테스트 계획` → `§구현 WBS` → `§리스크와 이연` → `부록 변경 파일 요약` + 완결성 게이트.
- 새 설계 문서를 추가하면 `docs/design/README.md` 의 **§0 라우팅 표**와 **해당 §3.x 플랫폼 절** 두 곳에 등재한다(README 자체 갱신 규칙).

**Why:** it17 설계 작성 시 관례를 역추적하는 데 여러 번의 조회가 필요했다. 진실원 우선순위는 `실제 소스 > docs/analysis > docs/design` 이며, 구현이 끝나면 `docs/analysis` 를 갱신하는 것이 마지막 WBS 단계로 관례화돼 있다.

**How to apply:** 설계 문서를 새로 만들 때 위 골격과 네이밍을 그대로 쓰고, WBS는 `docs/templates/WBS_BLUEPRINT.md` 형식(7필수 필드 + 관측 기반 3문 완료 기준)을 지킨다. 별 `-wbs.md` 파일을 만들지 않는다.
