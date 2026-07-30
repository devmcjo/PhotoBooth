# MC포토 분석 문서 (docs/analysis)

프로젝트의 **모든 서비스/영역을 문서만으로 이해**할 수 있도록 정리한 분석 문서 모음입니다. 파일명 접두 번호로 영역이 구분됩니다.

| 항목 | 값 |
|------|-----|
| 최종 업데이트 | 2026-07-30 (**멀티플랫폼 클라이언트 문서 계층 추가** — 05·13·14·31·41·61 신규) |
| 대상 | 클라이언트(Windows 데스크톱 / 향후 iOS·iPadOS·Android·macOS·웹) / 백엔드 API(Cloud Functions) / DB / 인프라 / 인증·역할 / 운영 |
| 갱신 규칙 | **기능·구성·인프라가 바뀌면 해당 번호 문서와 [00](./00-overview-and-architecture.md)/[90](./90-roadmap-and-future-work.md)를 함께 갱신.** 플랫폼 중립 규격(05·13·14·31·41·61)이 바뀌면 Windows 구현 문서(10·11·12)도 동시 갱신 |

---

## 어느 문서부터 읽어야 하나

### 🆕 iOS · iPadOS · Android · macOS · 웹 클라이언트를 만든다면

**→ [05 · 멀티플랫폼 클라이언트 개발 가이드](./05-cross-platform-client-guide.md) 부터 읽으세요.**

용어 치환표, 클라이언트 프로파일(P1~P4), 기능×플랫폼 지원 매트릭스, **Windows 전용 항목 식별**, 반드시 지켜야 하는 불변식 16개, 착수 전 해결해야 하는 서버 변경(블로커)이 정리돼 있습니다. 그다음 프로파일에 맞는 규격 문서로 이동합니다.

### 🆕 **웹(브라우저) 클라이언트를 만든다면 → [`docs/web-client/`](../web-client/README.md)**

Windows 앱의 **전 화면을 웹으로 구현**하는 전용 문서 세트(15개)가 별 폴더에 있습니다. 범위·결정·기술 스택·화면 명세·미디어 파이프라인·저장 설계·서버 선행 작업·WBS·**Web↔Windows 차이 보고서**를 포함합니다.

> ⚠️ **[05 §7.4.5](./05-cross-platform-client-guide.md#745-그래서-웹-범위는)의 "웹 P2 촬영·개인 프레임 제외 권장" 판정은 2026-07-30 사용자 결정으로 대체됐습니다.** 막힌다고 판정했던 항목(타임랩스·결과물 로컬 보관·개인 프레임·로그)의 해결 방식은 [`docs/web-client/00-scope-and-decisions.md §3`](../web-client/00-scope-and-decisions.md)에 항목별로 재판정돼 있습니다. 05의 기능별 **사실 서술**(브라우저 API 제약)은 여전히 유효한 참고 자료입니다.

### Windows 데스크톱 앱을 유지·보수한다면

**→ [00 · 전체 개요](./00-overview-and-architecture.md) → [10](./10-exe-app-architecture.md) → [11](./11-exe-app-features.md) → [12](./12-exe-app-settings-and-config.md)**

### 백엔드·DB를 다룬다면

**→ [31 · 백엔드 API 참조](./31-backend-api-reference.md) → [40 · 스키마](./40-database-firestore-and-storage-schema.md) → [30 · 연동 설계 의도](./30-backend-firebase-integration.md)**

### 장애를 진단한다면

**→ [70 · 로깅·진단](./70-logging-and-troubleshooting.md)** (로그 위치 포함) → [50](./50-infra-gcp-lifecycle-and-ttl.md) / [80](./80-build-and-deployment.md)

---

## 문서 지도

### A. 진입·전체 조감

| # | 문서 | 내용 |
|---|------|------|
| **00** | [overview-and-architecture](./00-overview-and-architecture.md) | 시스템 조감·컴포넌트 맵·end-to-end 데이터 흐름·핵심 불변식 |
| **05** | [cross-platform-client-guide](./05-cross-platform-client-guide.md) | 🆕 **멀티플랫폼 진입 문서** — 용어 사전·프로파일·지원 매트릭스·Windows 전용 목록·불변식·서버 블로커 |

### B. 플랫폼 중립 규격 (모든 클라이언트의 진실원)

| # | 문서 | 내용 |
|---|------|------|
| **13** | [client-behavior-spec](./13-client-behavior-spec.md) | 🆕 화면·상태 전이·흐름·타이밍 상수·검증 규칙·사용자 문구 카탈로그 |
| **14** | [media-pipeline-spec](./14-media-pipeline-spec.md) | 🆕 카메라 획득·중앙 크롭·슬롯 기하·합성 순서·필터 파라미터·녹화/타임랩스 인코딩 |
| **31** | [backend-api-reference](./31-backend-api-reference.md) | 🆕 전 엔드포인트 요청/응답 JSON·헤더·상태코드·에러 코드·입력 검증 전수 |
| **41** | [local-data-and-file-formats](./41-local-data-and-file-formats.md) | 🆕 설정 키 전수·프레임 `.slots` 포맷·세션 작업 공간·결과물 보관·플랫폼별 저장 위치 |
| **61** | [auth-platform-integration](./61-auth-platform-integration.md) | 🆕 플랫폼별 OAuth 흐름·서버 제약과 필요 확장·JWT 규약·PIN 게이트 |

### C. 플랫폼 무관 공통 규격

| # | 문서 | 영역 | 내용 |
|---|------|------|------|
| **30** | [backend-firebase-integration](./30-backend-firebase-integration.md) | 백엔드 | 연동 **설계 의도**·인증 모델·업로드 3단계·TempUser 한도·미도달 시 동작 정책 |
| **40** | [database-firestore-and-storage-schema](./40-database-firestore-and-storage-schema.md) | DB | Firestore 컬렉션·Storage 경로 규약·보안 규칙·계약 불변식 |
| **50** | [infra-gcp-lifecycle-and-ttl](./50-infra-gcp-lifecycle-and-ttl.md) | 인프라 | 보관/만료(GCS Lifecycle·Firestore TTL)·적용 명령·비용 |
| **60** | [auth-accounts-and-roles](./60-auth-accounts-and-roles.md) | 인증 | 역할 5종·권한 매트릭스·역할 변경 매트릭스·계정 저장소 |
| **90** | [roadmap-and-future-work](./90-roadmap-and-future-work.md) | 계획 | 알려진 이슈·기술 부채·개선 예정·비범위 |

### D. 구현 참조 (특정 클라이언트)

| # | 문서 | 대상 | 내용 |
|---|------|------|------|
| **10** | [exe-app-architecture](./10-exe-app-architecture.md) | **Windows 전용** | 솔루션 구조·MVVM·DI·상태머신·캡처 파이프라인·전역 예외 |
| **11** | [exe-app-features](./11-exe-app-features.md) | **Windows 전용** | 기능 상세·엣지 케이스·결함 수정 이력 |
| **12** | [exe-app-settings-and-config](./12-exe-app-settings-and-config.md) | **Windows 전용** | INI 저장·경로 폴백·브랜딩·표시 모드·창 기하 |
| **20** | [frontend-web-download-page](./20-frontend-web-download-page.md) | **웹(P1)** | 다운로드 페이지 상태머신·만료 판정·미디어 옵션 구분 |
| **70** | [logging-and-troubleshooting](./70-logging-and-troubleshooting.md) | **Windows 전용** | 로그 위치·증상별 원인 매핑·백엔드 연결 진단 |
| **80** | [build-and-deployment](./80-build-and-deployment.md) | **Windows 전용** | 단일 EXE publish·ffmpeg 번들·게이트 키 주입·인스톨러 |

---

## 문서 계층 규칙

```
05  진입 · 무엇을 읽어야 하는지 결정
 │
 ├─ B. 플랫폼 중립 규격 (13 · 14 · 31 · 41 · 61)
 │     "어떤 플랫폼에서든 이렇게 동작해야 한다"     ← 새 클라이언트의 진실원
 │
 ├─ C. 공통 규격 (30 · 40 · 50 · 60 · 90)
 │     "서버·데이터·권한·인프라는 플랫폼과 무관하다"
 │
 └─ D. 구현 참조 (10 · 11 · 12 · 20 · 70 · 80)
       "현재 구현은 이렇게 했다"                    ← 예시·근거·이력
```

- **B와 D가 충돌하면 D(실제 소스)가 사실이고, B를 고쳐야 한다.** B는 소스에서 추출한 규격이므로 소스 변경 시 함께 갱신한다.
- D의 Windows 고유 어휘(`%ProgramData%`, `ffmpeg.exe`, WPF 클래스명 등)는 **이식 대상이 아니다**. 무엇을 이식해야 하는지는 [05 §5.1](./05-cross-platform-client-guide.md)에 목적 단위로 정리돼 있다.

> 각 문서 최상단 메타 표에 "범위"와 "갱신 규칙"이 있습니다. 근거는 대부분 `파일:라인`으로 표기되어 있어 소스와 바로 대조할 수 있습니다. 설계 문서 인덱스는 [`docs/design/README.md`](../design/README.md).
