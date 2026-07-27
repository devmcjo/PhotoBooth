---
name: functions-verify-commands
description: web/functions 기계 검증(build/tsc/lint/test/smoke) 실행 방법과 Windows 툴링 함정
metadata:
  type: reference
---

`web/functions` 백엔드 리뷰의 라운드 0 기계 검증 실행법.

**How to apply:** build-verify 스킬 파일이 프로젝트/허브 어느 경로에도 없을 때(현재 그러함) 아래를 직접 실행.

- build(emit): `cd web/functions && npm run build` (= `tsc`)
- typecheck: `tsc --noEmit`
- lint: `npm run lint` (= `eslint "src/**/*.ts"`)
- unit test: `npm test` (jest)
- smoke(에뮬레이터): `cd web && firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"`

**Windows 툴링 함정 (중요):**
- **PowerShell 도구는 non-zero `$LASTEXITCODE`를 도구 실패로 처리해 stdout을 통째로 삼킨다.** tsc/jest 등 검증 명령은 **Bash 도구**로 실행하고 `2>&1 | tail`로 출력 확보할 것.
- `firebase emulators:exec "<cmd>"`의 자식 프로세스 stdout이 상위 리다이렉트로 안 잡히는 경우가 있다. smoke 스크립트 출력을 확실히 보려면 exec 안에서 `node functions/smoke/smoke.mjs 2>&1 | tee <파일>` 형태로 파일에 남긴 뒤 읽고, **리뷰 후 그 임시 tee 파일을 삭제**(워킹트리 오염 방지).
- smoke 하니스는 `결과: N passed, M failed`를 출력하고 fail>0이면 exit 1. 기대 수치: (2026-07 BE-1~4 시점) unit **120**, smoke **81**.
