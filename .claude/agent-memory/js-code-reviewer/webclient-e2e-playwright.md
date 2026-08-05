---
name: webclient-e2e-playwright
description: webclient/tests/e2e Playwright 인프라의 안정적 사실 — 목 백엔드 동일 오리진 트릭, OAuth 하네스 방식, 실측으로 확인된 브라우저 한계 3건
metadata:
  type: project
---

`webclient/tests/e2e/**`(Step 17, `docs/design/web-step17-e2e-and-acceptance.md`)의 구조와,
리뷰 중 실제로 재현해 확인한 브라우저 한계 사실들.

## 목 백엔드는 반드시 동일 오리진

`playwright.config.ts`의 `webServer.env.VITE_BACKEND_BASE_URL`이 `http://localhost:5173/__mock-api`로
**같은 오리진**이다. 교차 오리진으로 바꾸면 `X-MCPhoto-Client`/`Authorization` 때문에 CORS
preflight(OPTIONS)가 발생하는데 Playwright의 `page.route`는 preflight를 가로채지 못한다.
이 패턴이 무너지면 전 시나리오가 실네트워크에 의존하게 된다.

## OAuth 모킹은 하네스 가로채기이지 백도어가 아니다

`fixtures/auth.ts`의 `fakeLogin`은 `src/`를 건드리지 않는다 — `page.route`로 Google
authorize 이동을 가로채 URL의 `state`·`redirect_uri`를 읽고 `route.abort()` 후
`page.goto(redirectUri + "?code=...&state=...")`로 콜백을 재현한다. `sessionStore.login()`은
`oauthCallbackRunner`를 통해 **실제로** 실행된다(AUTH-1 위반 없음). 이 방식이 성립하는 이유는
콜백 판정이 `state` 문자열 일치만 보고(`nonce`는 서버=목이 검증), pending이 `sessionStorage`에
남아 있기 때문이다. 코드 리뷰에서 "테스트가 세션을 직접 주입하는 백도어인지" 의심될 때
이 파일을 먼저 확인하면 된다.

## 실측으로 확인된 브라우저 한계 (2026-08 Chromium/Playwright 1.49.1 기준, 직접 재현함)

1. **Playwright 1.49 기본 headless(`chromium_headless_shell`)에는 `getUserMedia`가 없다**
   (`NotSupportedError: Not supported`). `channel: "chromium"`(새 headless 모드)을 쓰면 된다.
   `playwright.config.ts`의 `channel: "chromium"` 한 줄이 이 문제의 회피책이다 — 지우면 안 된다.
2. **CDP `Storage.overrideQuotaForOrigin(origin, 0)`은 OPFS 쓰기를 실패시키지 않는다.**
   quota를 0으로 override해도 `navigator.storage.estimate().quota`만 0이 되고, 2MiB
   `createWritable().write()`는 그대로 성공한다. "저장 실패를 재현하는 레버"로 쓸 수 없다 —
   이 프로젝트가 E6(저장 실패 토스트) E2E를 포기하고 실측(V19-6)으로 넘긴 근거다.
3. **Playwright WebKit(Windows 빌드)에는 `navigator.storage.getDirectory`와 `OffscreenCanvas`가
   아예 없다**(`typeof` 둘 다 `"undefined"`). 이것은 **이 테스트 빌드의 한계이지 실제 Safari의
   동작이 아니다**(Safari 17+에는 OPFS가 있다) — WebKit 프로젝트가 전부 통과해도 Safari 저장
   경로가 검증된 것으로 착각하면 안 된다.

이 세 가지는 재현 스크립트(`playwright-core`의 `chromium.launch`/`webkit.launch` + CDP 세션)로
직접 검증했다(2026-08-01, Step 17 1라운드 리뷰). 향후 유사한 "이 레버가 안 먹힌다"는 주장을
리뷰할 때는 근거 문서만 믿지 말고 이런 최소 재현 스크립트로 실측하는 편이 빠르고 확실하다.

## 문서 요약 줄 산술 오류 패턴

`docs/web-client/10-testing-and-acceptance.md` §5처럼 "자동 N · 부분 M · ..." 형태의 요약 문장은
실제 표 행을 세어 대조해야 한다 — 표 자체의 개별 판정(자동/부분/불가/재정의)은 다 맞아도 요약
합계 문장에서 오프바이원이 발생한 사례가 있었다(자동 16 vs 실제 17, 부분 6 vs 실제 7). 기능에
영향은 없지만 Minor로 지적할 가치가 있다.
