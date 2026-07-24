---
name: backend-functions-setup
description: web/functions 백엔드(Cloud Functions 2nd gen + TS) 리뷰 시 기계 검증·스모크 실행 방법과 저장소 관례
metadata:
  type: reference
---

MCPhoto 백엔드는 `E:\Study\photobooth\web\functions`(Cloud Functions 2nd gen + TypeScript + Express, 단일 함수 `api`에 라우터 마운트).

기계 검증(라운드 0):
- build-verify 스킬은 프로젝트 루트 `E:\Study\photobooth\.claude\build-verify\SKILL.md`에 있음(functions/.claude에는 agent-memory만).
- `cd web/functions` 후: `npm run build`(tsc), `npm run lint`(eslint), `npm test`(jest). typecheck는 `npm run typecheck`(tsc --noEmit).
- node_modules는 이미 설치돼 있는 것이 보통.

Emulator 스모크(direct 근거 확보용):
- `cd web/functions/..`(= web/) 에서 `firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"`.
- firebase CLI·java 필요(둘 다 설치돼 있음). `.env`(gitignore, 5키)가 있어야 함.
- Secret Manager 403 경고("Unable to access secret")는 Emulator가 .env 값을 대신 쓰므로 무해 — 스모크는 정상 통과.
- 종료코드 0 = 전 케이스 PASS. item1a 기준 64 케이스.

저장소 관례:
- TS 파일 개행은 **CRLF**가 관례(신규/기존 모두). BOM 없음(no-BOM). `.gitattributes` 없어 git이 CRLF↔LF 경고를 내지만 신규 결함 아님 — LF로 "정정"하지 말 것.
- `.env`는 functions/.gitignore에 차단(`!.env.example`만 예외). 시크릿 커밋 위생 양호.

관련: [[functions-secret-wiring]]
