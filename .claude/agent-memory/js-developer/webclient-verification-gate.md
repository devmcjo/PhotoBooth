---
name: webclient-verification-gate
description: webclient/ 검증 게이트에는 ESLint가 없다 — tsc+vitest+coverage 임계+vite build 4종이 전부이고, build 산출물은 ../web/kiosk로 나간다
metadata:
  type: project
---

`webclient/`(Vite + React + vitest)에는 **ESLint가 설정돼 있지 않다**. `package.json`에 lint 스크립트도
eslint 의존도 없다. 품질 게이트는 다음 4종이 전부다(작업 디렉터리 `E:\Study\photobooth\webclient`):

```
npx tsc --noEmit          # strict + noUnusedLocals/Parameters. 미사용 import를 여기서 잡는다
npx vitest run            # tests/**/*.test.ts(x). environment=node가 기본, jsdom은 파일 상단 주석 opt-in
npx vitest run --coverage # src/domain만 계측. 임계 95/95/95/90 — 도메인 파일 추가 시 분기까지 채워야 한다
npm run build             # ⚠️ Step 16부터 **2단**이다: vite build && vite build --config vite.sw.config.ts
                          #    산출물은 ../web/kiosk/ (배포 디렉터리, gitignore됨). 2단째가 sw.js를 만들고
                          #    1단이 남긴 precache-manifest.json을 읽으므로 **순서가 규격**이다.
                          #    ⚠️ `npx vite build`만 돌리면 **1단만** 나간다 — sw.js가 갱신되지 않는다.
npx playwright test       # 44건(chromium 29 + webkit 15) · ~3.7분. 5번째 게이트다
```

E2E 셀렉터는 **role·문자열 기반**이라 CSS/레이아웃을 전면 교체해도 깨지지 않는다
(2026-08-01 팔레트·컴포넌트 전량 개편에서 44건 무수정 통과). 반대로 **문구를 바꾸면 즉시 깨진다** —
`strings-catalog.spec.ts`가 카탈로그와 화면 텍스트를 대조한다.

**Why**: 기본 운영 지침이 "eslint 오류 0"을 요구하지만 이 프로젝트에는 실행할 린터가 없다. 모르면 매
세션 `.eslintrc` 를 찾다가 한 턴을 버린다. `tsc`의 `noUnusedLocals`가 사실상 그 역할을 대신한다.

**How to apply**: webclient 작업 완료 보고에 "eslint 0"을 쓰지 말고 "이 프로젝트에 린터 미설정"이라고
명시한다. 골든 이미지 테스트가 30초 이상 걸려 전체 `vitest run`이 ~36초다 — 논리 단위 검증은
파일을 지정해서 돌리고(`npx vitest run tests/unit/...`) 마지막에 한 번 전체를 돌린다.

관련: 저장소는 `core.autocrlf=true`(index=LF, worktree=CRLF)라 Write/Edit로 만든 파일이 CRLF여도
diff가 오염되지 않는다 — [[ops-scripts-and-encoding]] 참조.

⚠️ **`tsc`·`vitest`는 CSS를 파싱하지 않는다 — CSS 문법 오류는 `vite build`에서만 드러난다.**
가장 잘 밟는 것: CSS 주석에 마크다운 강조를 쓰다 `**16**/Bold` 처럼 **`*` 바로 뒤에 `/`** 가 오면
그 자리가 주석 끝이 되어 뒤 텍스트가 셀렉터로 파싱된다(`postcss: Unexpected '/'`).
CSS를 손대면 **논리 단위마다 `npx vite build`까지** 돌리고, 사전 점검은 `grep -rn '\*\*/' src --include=*.css`.

⚠️ **`tsc`는 소스에 섞인 NUL 바이트를 잡지 못한다.** 문자열 리터럴 안에 들어가면 유효한 TS라 컴파일이
통과하고 동작만 조용히 달라진다(2026-08-01 `zipStore.ts`에서 공백 한 칸이어야 할 문자열 리터럴이 U+0000으로 생성됐다).
신호는 `grep`이 그 파일에 대해 **"Binary file matches"** 를 내는 것이다. 대량 생성 파일을 만든 뒤
`node -e "fs.readFileSync(p).includes(0)"` 로 한 번 훑으면 즉시 드러난다.
