---
name: web-public-verification
description: web/public(정적 ESM, 린터·테스트 하네스 없음)을 검증하는 스텁 DOM 하네스 패턴과, 설계 문서의 rg 게이트가 주석과 충돌하는 함정
metadata:
  type: project
---

# `web/public` 검증 방법과 게이트 함정

## 설계 문서의 `rg` 게이트는 **주석까지 매치한다**

이 리포의 js-architect 설계는 검증 게이트를 `rg -n "innerHTML" web/public/app.js` → **무매치** 형태로 준다
(`window.open`·`crossorigin`·`navigator.share`·`mcphoto.jpg` 등 다수).

**금지 토큰을 "쓰지 않는다"고 설명하는 주석을 달면 그 게이트가 FAIL 한다.** it17 에서 4번 걸렸다.

**Why:** 게이트는 토큰 기반 grep 이고 주석/코드를 구분하지 않는다. 리뷰어가 그대로 돌리면 실패로 보인다.

**How to apply:** 금지 API 를 주석으로 언급할 때 **토큰을 우회 표기**한다 —
`window.open` → "새 창/팝업을 여는 방식", `innerHTML` → "HTML 문자열 주입 경로",
`navigator.share` → "Web Share API". 의도는 남고 게이트는 통과한다.
반대로 `location.assign`·`credentials:"omit"`처럼 **존재를 요구하는** 게이트는 주석 때문에 건수가 1 늘어난다(정상).

## 하네스가 없으므로 스텁 DOM 으로 검증한다

`web/public`에는 **JS 린터·번들러·단위 테스트가 없다.** 자동 게이트는 `node --check`(구문)와
`npm run test:rules`(Firestore/Storage 규칙, 페이지 로직 무관)뿐이다. `npm run build`·`tsc`·`eslint`는 **존재하지 않는다** —
검증 보고 시 "빌드 통과"라고 쓸 수 없다.

`app.js`는 gstatic CDN 을 import 해서 node 에서 직접 import 할 수 없다. **해결 패턴**(it17 에서 확립, 스크래치패드 전용):

1. 소스에서 섹션 주석(`// ---- 이름 ----`)을 마커로 **필요한 블록만 문자열 slice**.
2. `await import("data:text/javascript;base64," + ...)` 로 평가 + `export {...}` 를 덧붙여 내부 함수를 꺼낸다.
3. **함정 1 — 모듈 캐시**: data: URL 이 같으면 재사용되어 모듈 상태(`let autoDownloadEnabled` 등)가 시나리오 간 누출된다.
   → 시나리오마다 `// salt:${n}` 을 덧붙여 URL 을 달라지게 한다.
4. **함정 2 — 순서**: `window.addEventListener`(pagehide) 는 **모듈 평가 중**에 배선되므로 import **전에** 계측해야 잡힌다.
5. **함정 3 — 타이머**: 코드가 `window.setTimeout`을 쓰면 `globalThis.window.setTimeout`을 가짜로 주입해 수동으로 소진시킬 수 있다
   (`clearTimeout`은 bare 전역이라 따로 덮는다).
6. **함정 4 — slice 경계**: 종료 마커를 다음 섹션 이름으로 잡아야 한다. 새 섹션이 중간에 끼면 브라우저 전역을 요구하는
   코드까지 끌려와 깨진다(it17 에서 실제로 깨졌다).
7. node 24 의 `globalThis.navigator`는 **getter-only** → `Object.defineProperty` 로 덮어써야 한다.

**Why:** 브라우저 관측만으로는 상태 전이·해제 경로·폴백을 회귀 검사할 수 없다. 121개 검증을 이 방식으로 돌렸다.

**How to apply:** 하네스는 **스크래치패드에만** 둔다 — 리포에 커밋하면 도구 도입 결정(이연 항목)을 앞지른다.
보고 시 "리포지토리 자동 하네스 없음 + 일회성 스텁 검증"임을 명시한다.

## `hidden` 속성은 작성자 `display` 에 눌린다

`.btn{display:inline-flex}`·`.media__preview{display:block}`·`.state{display:flex}` 같은 작성자 규칙은
`hidden` 의 UA `display:none` 을 **이긴다**. 이 리포는 `.state[hidden]{display:none}` 으로 같은 버그를 이미 고친 이력이 있다.

**How to apply:** `hidden` 으로 토글하는 요소에 `display` 설정 클래스가 붙어 있으면 `[hidden]` 가드를 **반드시** 함께 넣는다.
관련: [[web-public-hidden-guard-audit]] 은 별도로 만들지 않았다 — 전수 결과는 `docs/analysis/20` §6.1 에 기록했다.
