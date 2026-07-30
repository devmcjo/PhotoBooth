---
name: web-cors-never-verified
description: 웹 다운로드 페이지의 Storage CORS는 no-cors 서브리소스만 검증됐다 — 문서의 "CORS 문제 없음"을 cors 모드 fetch 근거로 오독하지 말 것
metadata:
  type: project
---

`docs/design/web-architecture.md`(§OA-3, §WR2)와 `docs/design/web-wbs.md`(Step 4/8)는 "다운로드 토큰 URL을 모바일 브라우저가 CORS 오류 없이 직접 GET" 을 **검증 완료**로 읽히게 서술한다. 그러나 거기서 실제로 관측된 것은 `<img src>` / `<video src>` 의 **no-cors 서브리소스 로드**뿐이다. `fetch(url, {mode:'cors'})` 로 응답 바디를 읽는 것은 `Access-Control-Allow-Origin` 이 필요한 **전혀 다른 조건**이며, 이 프로젝트에서 한 번도 검증되지 않았다.

리포지토리에도 버킷 CORS 구성이 없다 — `web/` 에는 `lifecycle.json` 만 있고, `docs/analysis/90` 의 **B5(버킷 CORS)는 "대기"** 상태이며 그 기재 용도는 브라우저 **PUT**(업로드)이다. GET 용도의 판정은 어느 문서에도 없다.

**Why:** it17 설계(2026-07-30) 중 자동 다운로드 방식을 판정하면서 발견. `web-architecture.md` 를 믿고 "CORS는 이미 해결됨" 으로 전제하면, blob 경유 자동 저장이 배포 후에 전 플랫폼에서 조용히 실패한다.

**How to apply:** 브라우저 JS 가 Storage 바이트를 **읽어야** 하는 설계(blob 다운로드, canvas 처리, 해시 검증 등)를 할 때는 CORS를 **미검증 가정으로 다루고 검증 단계를 매핑**한다. 그리고 CORS 실패 시 기존 동작으로 degrade 하는 폴백을 함께 설계한다. 관련 산출물: [[web-it17-design]]. 참고로 토큰 URL 자체가 capability 이므로 GET CORS에 `origin:["*"]` 는 새 능력을 주지 않는다(PUT 은 반대 — 반드시 오리진 제한).
