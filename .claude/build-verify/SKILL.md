---
name: build-verify
description: |
  WPF(.NET) 및 프론트엔드(JavaScript/TypeScript) 프로젝트의 변경사항을 7단계로 기계 검증하는 품질 게이트.
  빌드 → 경고 → 정적분석/타입·린트 → 테스트 → 안전 스캔 → 인코딩 → diff 리뷰를 순차 실행하고 READY/NOT READY 판정을 내립니다.
  이 스킬은 다음 상황에서 사용하세요:
  - 리뷰어 에이전트(*-code-reviewer)가 코드 리뷰 라운드를 시작하기 전 (필수 게이트)
  - developer 에이전트가 완료 선언 전 자가 검증할 때
  - "빌드 검증", "검증 게이트", "build verify" 등의 표현 사용 시
version: 2.0.0
---

# Build Verify — 7단계 기계 검증 게이트 (WPF/.NET · JS/TS)

코드 리뷰(주관 판단) 이전에 기계적으로 확인 가능한 항목을 전부 걸러내는 객관 게이트.
**모든 단계를 끝까지 실행한 뒤 결과를 한 번에 보고한다** (중간에 멈추지 않음).

> 이 프로젝트는 WPF(.NET) 위주이며 프론트엔드 JS/TS를 보조적으로 사용한다. 감지된 프로젝트 유형에 맞는 명령을 사용한다.

## 핵심 원칙

1. **Read-only + Build** — 소스 코드를 수정하지 않는다. 빌드/분석/스캔만 수행한다.
2. **Evidence-first** — 모든 판정은 명령 출력, 파일:줄번호 근거를 포함한다.
3. **No-skip** — 모든 단계를 실행하고 아래 판정 어휘로 기록한다. 단계 생략은 없다.

## 판정 어휘 (단계별 5상태)

| 상태 | 의미 |
|------|------|
| `PASS` | 실행했고 기준 충족 |
| `FAIL` | 실행했고 기준 미달 |
| `INCONCLUSIVE` | 실행했으나 출력이 모호해 판정 불가 — 재실행/원인 확인 후에도 남으면 FAIL로 취급 |
| `BLOCKED` | 환경·권한·장비 부재로 **실행 자체가 불가** (사유 명시 필수) |
| `N/A` | 이 프로젝트에 원래 해당 없음 (예: 테스트 프로젝트 부재) — 판정에서 제외 |

- `WARN`은 보조 표기 (PASS이지만 주의 사항 존재)
- 집계 우선순위: **BLOCKED > FAIL > INCONCLUSIVE > PASS**

**Overall 판정 규칙**:
- FAIL ≥ 1건 → **NOT READY**
- 핵심 단계(Step 2~4)에 BLOCKED ≥ 1건 → **NOT READY** — "실행 못 함"은 "통과"가 아니다
- INCONCLUSIVE가 재확인 후에도 남으면 → NOT READY
- WARN만 있으면 READY (경고 목록 첨부)

## Evidence 강도 (근거 등급)

각 단계의 판정에 근거 등급을 함께 기록한다:

| 등급 | 의미 | 예 |
|------|------|-----|
| `direct` | 런타임 관측·테스트 통과 | `dotnet test` 통과, 앱 실행 관측, DevTools 콘솔 무오류 |
| `semi_direct` | 빌드/정적분석/도구 출력이 주장을 지지 | `dotnet build` error 0, `tsc --noEmit` 0, `eslint` 0 |
| `indirect` | 코드 읽기·diff 추론만 (관측 없음) | "이 코드면 될 것이다" |
| `insufficient` | 지지 근거 없음 | — |

**indirect 이하 근거만으로 PASS를 선언하지 않는다.** 이 스킬의 모든 단계는 실행 가능한
명령이 있으므로, indirect 판정이 나왔다는 것은 명령을 실행하지 않았다는 뜻이다.

## 실행 절차

### Step 1: 프로젝트 감지

- `git diff --name-only` (+ `git status --porcelain`)로 변경 파일 목록 확보
- 빌드 시스템 판별:
  - `*.sln` / `*.csproj`(SDK-style) → **.NET** (`dotnet` CLI, WPF 포함)
  - 레거시 `*.csproj`(non-SDK) / `.NET Framework` → `msbuild`
  - `package.json` → **프론트엔드 JS/TS** (npm/pnpm/yarn 스크립트)
  - 두 유형이 공존하면 각각 실행 (WPF 앱 + 임베드 프론트엔드)
- 대상 프로젝트의 CLAUDE.md/문서/`package.json` scripts에 명시된 빌드 명령이 있으면 **그것을 우선 사용**

### Step 2: 빌드 (error 게이트)

- **.NET/WPF**: `dotnet build -c Release` (평소 구성이 Debug면 Debug도 추가 실행)
  - 레거시 .NET Framework: `msbuild <sln> /p:Configuration=Release /m`
  - 사전 복원 필요 시 `dotnet restore`
- **JS/TS**: `npm run build` (또는 `package.json`에 정의된 빌드 스크립트)
- **판정: error 0건 → PASS, 1건 이상 → FAIL** (에러 코드·메시지 요약 첨부)

### Step 3: 경고 게이트 (warning 0 원칙)

- 빌드 출력에서 warning 수 집계
- 변경 파일에서 발생한 warning은 **0건이어야 PASS**
- 변경하지 않은 파일의 기존 warning은 WARN으로 보고 (FAIL 아님)
- **.NET**: nullable 경고(CS86xx), 분석기 경고(CAxxxx/IDExxxx) 포함. `#pragma warning disable` **신규 추가** 여부를 diff에서 검사 → 발견 시 FAIL (근거 주석이 있는 경우 WARN)
- **JS/TS**: 번들러/컴파일 경고. `eslint-disable` **신규 추가** → 발견 시 FAIL (근거 주석 있으면 WARN)

### Step 4: 정적 분석 / 타입 · 린트

- **.NET**: Roslyn 분석기 경고(빌드에 포함), 필요 시 `dotnet format --verify-no-changes`로 스타일/포맷 확인
- **JS/TS**:
  - 타입 체크: `tsc --noEmit` (TS 프로젝트) — 오류 0
  - 린트: `eslint .` (또는 프로젝트 lint 스크립트) — 오류 0
- **판정: 신규 분석/타입/린트 오류 0건 → PASS**

### Step 5: 테스트

- **.NET**: 테스트 프로젝트가 있으면 `dotnet test` 실행
- **JS/TS**: `npm test` (vitest/jest 등)
- 테스트가 없으면 N/A + "테스트 부재" 명시
- **판정: 전체 통과 → PASS, 실패 1건 이상 → FAIL (통과/실패 수 첨부)**

### Step 6: 안전 스캔 (Grep 기반)

변경 파일 대상으로 검사:

| 항목 | 패턴/대상 | 판정 |
|------|-----------|------|
| 하드코딩 시크릿 | `(password\|secret\|api[_-]?key\|token\|connectionstring)\s*[=:]\s*["'][^"']+["']` | 발견 시 **FAIL** |
| 위험 역직렬화/실행 (.NET) | `BinaryFormatter`, `NetDataContractSerializer`, 신뢰 못 하는 입력의 역직렬화 | 발견 시 **FAIL/WARN** (맥락 판단) |
| XSS 위험 (JS/TS) | 신뢰 못 하는 데이터의 `innerHTML`/`insertAdjacentHTML`/`document.write`, `eval(`, `new Function(` | 발견 시 **FAIL** |
| UI 스레드 블로킹 (WPF) | `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` (UI 경로) | WARN (데드락 위험) |
| 디버그 잔재 | `Console.WriteLine`/`Debug.WriteLine`(임시), `console.log`, `debugger;` | WARN |
| TODO/FIXME/HACK 신규 추가 | `TODO\|FIXME\|HACK\|XXX` (diff 추가 줄만) | WARN |

### Step 7: 인코딩 + Diff 리뷰

**인코딩 보존 검사**:
- 변경된 각 소스 파일에 대해 원본(HEAD)과 현재 파일의 인코딩/개행 비교:
  ```bash
  # BOM 비교 (앞 3바이트) — .NET/XAML은 UTF-8 BOM, JS/TS는 대개 BOM 없음
  git show HEAD:<file> | head -c 3 | xxd
  head -c 3 <file> | xxd
  ```
- UTF-8 BOM ↔ BOM 없음 ↔ ANSI/EUC-KR 간 변경, 또는 개행(CRLF↔LF) 대량 변경 발견 시 → **FAIL** (한글 깨짐·diff 오염 원인)

**Diff 리뷰**:
- `git diff --stat`로 변경 규모 확인
- 의도치 않은 파일 포함 여부: 빌드 산출물(`bin/`, `obj/`, `dist/`, `build/`), 의존성(`node_modules/`), `.vs/`, `*.user` 등 → 발견 시 WARN
- 단일 파일 500줄+ 변경 → WARN (분리 검토 권고)
- `.csproj`/`package.json` 의존성 변경 시 의도치 않은 버전 변경 여부 확인

## 출력 형식

```
═══════════════════════════════════════
  BUILD VERIFICATION REPORT
═══════════════════════════════════════
  프로젝트: {프로젝트명}
  유형: {.NET/WPF | JS/TS Frontend | 혼합}
  빌드 시스템: {dotnet | msbuild | npm/pnpm}
  구성: {Configuration | build script}
  변경 파일: {N}개
───────────────────────────────────────
  1. Build          : ✓ PASS / ✗ FAIL (error N건)
  2. Warnings       : ✓ PASS / ✗ FAIL (신규 N건) / ⚠ WARN (기존 N건)
  3. Analysis/Type/Lint : ✓ PASS / ✗ FAIL / ⊘ BLOCKED (사유)
  4. Test           : ✓ PASS (N/N) / ✗ FAIL / ⊘ BLOCKED / − N/A (테스트 부재)
  5. SafetyScan     : ✓ PASS / ✗ FAIL / ⚠ WARN (N건)
  6. Encoding       : ✓ PASS / ✗ FAIL ({파일}: {원본}→{변경})
  7. DiffReview     : ✓ PASS / ⚠ WARN
───────────────────────────────────────
  Overall: ✓ READY / ✗ NOT READY
  근거: {FAIL/BLOCKED 항목 요약 + 파일:줄번호 + 단계별 근거 등급(direct/semi_direct)}
═══════════════════════════════════════
```

## 합리화 차단

| 합리화 | 실제 |
|--------|------|
| "warning은 원래 있던 거라 괜찮다" | 변경 파일의 warning과 기존 warning을 분리 집계해서 증명하라. 추측으로 PASS 주지 않는다. |
| "빌드는 developer가 이미 했을 것이다" | 직접 실행한 출력만 근거다. 전달받은 주장은 검증 대상이지 증거가 아니다. |
| "타입 오류는 런타임엔 문제없다" | `tsc --noEmit` 0이 게이트 조건이다. 타입 오류는 조용한 런타임 버그의 씨앗이다. |
| "인코딩은 눈으로 봐서 정상이다" | 바이트 수준 비교(BOM/개행)만 증거다. 에디터 표시는 신뢰하지 않는다. |
| "시간이 없으니 빌드만 하자" | 7단계 전부 실행이 이 스킬의 정의다. 실행 불가 단계는 BLOCKED로 정직하게 표기하고 사유를 남긴다. |
| "환경이 없어서 못 돌렸지만 코드는 맞을 것이다" | BLOCKED는 PASS가 아니다. 핵심 단계 BLOCKED면 Overall은 NOT READY이고, 그 사실을 숨기지 않는다. |

## 제약 조건

- 소스 코드 수정 금지 — 발견 사항은 보고만 한다 (수정은 developer의 책임)
- 빌드 명령을 추측으로 구성하지 않는다 — 대상 프로젝트 문서/`package.json` scripts/기존 빌드 스크립트 우선
- 보고서의 모든 FAIL에는 재현 가능한 명령과 출력 근거를 포함한다
