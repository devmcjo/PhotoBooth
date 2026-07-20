---
name: source-check
description: |
  프레임워크/플랫폼 API 결정을 훈련 데이터 기억이 아닌 공식 문서 근거로 강제하는 절차 (DETECT→FETCH→IMPLEMENT→CITE).
  .NET/WPF, C#, 브라우저 웹 플랫폼(DOM·JS/TS API)처럼 버전에 따라 권장 패턴과 deprecated 여부가 달라지는 도메인에서 구식 패턴 사용을 차단합니다.
  이 스킬은 다음 상황에서 사용하세요:
  - .NET/WPF/C# 또는 브라우저 API 선택, 시그니처, 권장 패턴을 결정할 때
  - deprecated 여부가 불확실한 API를 쓰려 할 때
  - "공식 문서 확인", "source check", "API 검증" 등의 표현 사용 시
version: 2.0.0
---

# Source Check — 기억이 아닌 공식 문서로 구현한다

훈련 데이터에는 구식 패턴이 "정답처럼" 들어 있다. .NET에서 `BinaryFormatter`는 수년간 표준이었으므로
자신 있게 떠오르지만, 현재는 보안상 사용이 금지·제거되는 방향이다. 웹에서 `document.write`나
동기 `XMLHttpRequest`도 마찬가지다. **자신감은 증거가 아니다. 검증하고, 인용한다.**

## 적용 대상 / 비대상

**적용**: API 선택·시그니처·권장 패턴이 버전/플랫폼에 의존하는 모든 결정
(.NET/WPF/C# API, NuGet 패키지 API, 브라우저 웹 플랫폼 API, JS/TS 언어·런타임 기능)

**비적용**: 버전 무관한 순수 로직(루프, 자료구조), 변수명 변경, 오탈자 수정

## 4단계 프로세스

```
DETECT ──→ FETCH ──→ IMPLEMENT ──→ CITE
 버전 확인   해당 페이지   문서대로 구현   근거 제시
            직접 fetch
```

### Step 1: DETECT — 대상 버전 확인

추측하지 않고 프로젝트에서 직접 읽는다:

| 항목 | 확인 위치 |
|------|----------|
| .NET 대상 프레임워크 | `.csproj`의 `<TargetFramework>` (net8.0-windows, net48 등) |
| C# 언어 버전 | `<LangVersion>` (없으면 TFM 기본값) |
| WPF/UI 스택 | `<UseWPF>`, 참조 패키지(CommunityToolkit.Mvvm, Prism 등) 버전 |
| NuGet 패키지 버전 | `.csproj` `PackageReference`, `packages.lock.json` |
| Node/브라우저 대상 | `package.json`(engines, browserslist), `tsconfig.json`(`target`/`lib`), WebView2면 Chromium 최신 |
| TypeScript 버전 | `package.json`의 `typescript` devDependency |

버전이 불명확하면 **추측하지 말고 사용자에게 확인**한다 — 버전이 올바른 패턴을 결정한다
(예: .NET Framework 4.8 vs .NET 8은 사용 가능한 API와 권장 패턴이 다르다).

### Step 2: FETCH — 공식 문서를 직접 가져온다

해당 기능의 **구체적 문서 페이지**를 fetch한다 (홈페이지·검색결과 전체가 아니라).

**근거 우선순위**:

| 순위 | 출처 | 예 |
|------|------|-----|
| 1 | 공식 레퍼런스 | .NET/WPF/C#: learn.microsoft.com의 해당 API 페이지 / 웹: developer.mozilla.org (MDN) |
| 2 | 공식 마이그레이션/변경 가이드 | .NET 릴리스 노트·breaking changes, C# 버전 기능 문서, MDN의 deprecated 표기 |
| 3 | 공식 샘플/사양 | dotnet/samples, WHATWG/W3C 사양, TypeScript 핸드북 |

**1차 근거로 인용 금지**: Stack Overflow, 블로그/튜토리얼, AI 생성 요약, **자신의 훈련 데이터** (그걸 검증하는 게 이 스킬의 목적이다)

fetch 후 deprecated 경고·마이그레이션 안내·브라우저 호환성 표가 있는지 반드시 확인한다.

### Step 3: IMPLEMENT — 문서대로 구현

- 시그니처는 문서의 것을 사용한다 (기억의 것이 아니라)
- 문서가 새 방식을 권장하면 새 방식, deprecated 표기가 있으면 사용하지 않는다
  - 예(.NET): `WebClient` → `HttpClient`(팩토리/수명 관리 포함), `BinaryFormatter` → 안전한 직렬화(`System.Text.Json`)
  - 예(웹): `document.write`/동기 XHR → `fetch`, deprecated DOM API → 표준 대체
- **문서와 기존 코드가 충돌하면 조용히 택일하지 않고 표면화한다**:

```
CONFLICT DETECTED:
기존 코드는 BinaryFormatter를 사용하지만,
현재 .NET 문서는 이를 보안상 사용 금지로 표기한다 (obsolete/removed 방향).
A) 신규 코드는 System.Text.Json 사용 (문서 일치)
B) 기존 관례 유지 (코드베이스 일치, 보안 리스크)
→ 어느 쪽으로 할까요?
```

### Step 4: CITE — 근거를 남긴다

- 비자명한 API 결정마다 전체 URL 인용 (코드 주석 또는 설계/보고 문서)
- 검증하지 못한 것은 정직하게 표기한다:

```
UNVERIFIED: 이 패턴의 공식 문서를 찾지 못했다.
훈련 데이터 기반이므로 구식일 수 있다. 프로덕션 적용 전 확인 필요.
```

면책 문구로 얼버무리는 것이 최악이다 — 검증해서 인용하거나, UNVERIFIED로 명시하거나 둘 중 하나다.

## 합리화 차단

| 합리화 | 실제 |
|--------|------|
| "이 API는 확실히 안다" | 자신감은 증거가 아니다. 훈련 데이터의 구식 패턴은 정답처럼 보이지만 현재 버전에서 깨지거나 obsolete다 |
| "문서 가져오기는 토큰 낭비다" | API를 환각하는 게 더 낭비다. 한 번의 fetch가 한 시간의 디버깅을 막는다 |
| "문서에 없을 것이다" | 문서에 없다는 사실 자체가 정보다 — 공식 권장 패턴이 아닐 수 있다 |
| "구식일 수 있다고 한 줄 적어두면 된다" | 면책은 도움이 안 된다. 검증·인용하거나 UNVERIFIED로 명시한다 |
| "간단한 작업이라 확인 불필요" | 잘못된 패턴의 간단한 코드는 템플릿이 되어 열 군데로 복사된다 |

## Red Flags (즉시 중단 신호)

- 버전(TFM/LangVersion/tsconfig target) 확인 없이 버전 의존적 코드 작성
- "아마", "제 기억으로는"으로 API를 설명 (인용 없이)
- deprecated/obsolete 대조 없이 API 선택
- Stack Overflow/블로그를 1차 근거로 인용
- 브라우저 호환성 표 확인 없이 최신 웹 API 사용 (WebView2 외 대상 포함 시)
- 검증 못 한 패턴을 UNVERIFIED 표기 없이 전달

## 완료 검증

- [ ] 대상 버전(.NET TFM / C# LangVersion / tsconfig target / 패키지 버전)을 프로젝트 파일에서 직접 확인했다
- [ ] 버전 의존 패턴마다 공식 문서(learn.microsoft.com / MDN)를 fetch했다
- [ ] deprecated/obsolete API 미사용 (마이그레이션 가이드·호환성 표 대조)
- [ ] 비자명한 결정에 전체 URL 인용 존재
- [ ] 문서-기존 코드 충돌은 사용자에게 표면화했다
- [ ] 검증 못 한 것은 UNVERIFIED로 명시했다
