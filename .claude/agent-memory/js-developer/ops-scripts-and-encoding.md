---
name: ops-scripts-and-encoding
description: web/functions의 일회성 운영 .mjs 스크립트를 jest로 검증하는 패턴과, 이 리포의 core.autocrlf=true로 인한 줄바꿈 경고 해석
metadata:
  type: project
---

# 운영 스크립트 테스트 패턴 · 줄바꿈 규약

## `.mjs` 운영 스크립트를 jest로 검증하는 방법

`scripts/*.mjs`(ESM)는 jest(ts-jest, CJS, `testMatch: **/__tests__/**/*.test.ts`)로 **직접 테스트할 수 없다.**
`.test.ts`가 CJS로 컴파일돼 동적 `import()`가 `require()`로 치환되고, jest VM에서 native dynamic import도 막힌다.

**해결 패턴**(`migrate-google-only-accounts.mjs`에서 확립):
1. "무엇을 바꿀지" 판정을 전부 **순수 TypeScript**로 `src/domain/<name>.ts`에 분리(Admin SDK·네트워크 무의존).
2. `src/__tests__/<name>.test.ts`가 그 모듈을 평범하게 import해 단위 검증.
3. `.mjs`는 컴파일 산출물을 `await import("../lib/domain/<name>.js")`로 가져온다.
   tsc CJS 출력은 `exports.foo = foo` 형태라 Node ESM의 cjs-module-lexer가 named export를 인식한다.
4. **`npm run build` 선행 필수** → 스크립트가 import 실패를 잡아 "먼저 `npm run build` 하세요"로 안내하고 exit 1.
   스크립트 상단 사용법 주석에도 build 단계를 명시한다.

**Why:** 운영 DB를 만지는 스크립트는 실행으로 검증할 수 없다. 판정 로직만 순수화하면 멱등성·dry-run 기본값 같은
"틀리면 데이터가 사라지는" 규칙을 회귀 테스트로 못박을 수 있다.

**How to apply:** 새 `scripts/*.mjs`를 만들 때 I/O(초기화·read·write·출력)만 mjs에 두고, 판정·계획·파싱은 domain으로.

## 줄바꿈: `core.autocrlf=true` (리포에 `.gitattributes` 없음)

작업 트리의 기존 `.ts`는 **CRLF**, 커밋된 blob은 **LF**다. Write 도구로 파일을 통째로 새로 쓰면 작업 트리가 LF가 되고
`git diff`가 `LF will be replaced by CRLF the next time Git touches it` 경고를 낸다.

**이 경고는 무해하다** — autocrlf가 커밋 시 LF로 정규화하므로 blob 내용은 동일하고, diff에도 줄바꿈 노이즈가 생기지 않는다
(`git diff --numstat`로 실제 변경 줄수만 잡히는지 확인하면 된다).

**How to apply:** 경고를 없애려고 파일을 CRLF로 되돌리지 말 것 — 오히려 불필요한 작업이다.
단 **BOM은 절대 금지**(`.ts`/`.mjs`/`.md` 모두 UTF-8 without BOM). 검사: `head -c 3 <file> | od -An -tx1`에 `ef bb bf`가 없어야 한다.

⚠️ 2026-08-01 정정(Step 12 구현 시 실측): **`Edit`는 원본 줄바꿈을 보존한다**(CRLF 파일은 CRLF로 남고 LF 파일은 LF로 남는다).
줄바꿈이 바뀌는 것은 **`Write`로 기존 파일을 통째로 덮어쓸 때**뿐이다(2026-07-31 메모의 "Edit도 LF로 바꾼다"는 관측은 틀렸다).
굳이 통일하고 싶으면 `node -e "...replace(/\r\n/g,'\n').replace(/\n/g,'\r\n')"` 일괄 변환이 안전하지만, **검증 가치는 없다**.

**작업 트리는 이미 파일별로 섞여 있다** — 확인 방법: `docs/web-client/`에서 `02`·`03`·`07`·`12`는 LF, `06`·`11`·`14`·`15`·`README`는 CRLF.
`webclient/src/`도 같다(`App.tsx` CRLF ↔ Step 11이 만든 `QrView.tsx` LF). 그러니 **어느 한쪽이 "정답"이 아니다** —
파일마다 있는 그대로 유지하면 되고, 섞여 있다는 사실 자체를 이상 징후로 보지 말 것.
`webclient/package.json`·`package-lock.json`은 애초에 **LF**다(npm이 관리) — 이 둘은 건드리지 말 것.

## `lib/`는 tsc가 청소하지 않는다

소스 파일을 **삭제**하면 `lib/`에 컴파일 산출물(`.js`/`.d.ts`/`.js.map`)이 그대로 남고, `firebase deploy`가
그 죽은 파일까지 업로드한다(참조가 없어 런타임 무해하나 노이즈). 모듈 삭제 시 대응하는 `lib/` 산출물도 지운다.
`lib/`는 gitignore 대상이라 커밋 diff에는 나타나지 않으므로 놓치기 쉽다.
