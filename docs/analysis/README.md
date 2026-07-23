# MC포토 분석 문서 (docs/analysis)

프로젝트의 **모든 서비스/영역을 문서만으로 이해**할 수 있도록 정리한 분석 문서 모음입니다. 파일명 접두 번호(00~90)로 영역이 구분됩니다.

| 항목 | 값 |
|------|-----|
| 최종 업데이트 | 2026-07-23 |
| 대상 | Exe 앱 / 프론트엔드(웹) / 백엔드(Firebase) / DB / 인프라 / 인증·역할 / 운영 |
| 갱신 규칙 | **기능·구성·인프라가 바뀌면 해당 번호 문서와 [00](./00-overview-and-architecture.md)/[90](./90-roadmap-and-future-work.md)를 함께 갱신** |

## 문서 지도

| # | 문서 | 영역 | 내용 |
|---|------|------|------|
| **00** | [overview-and-architecture](./00-overview-and-architecture.md) | 전체 | 시스템 조감·컴포넌트 맵·end-to-end 데이터 흐름·문서 지도 |
| **10** | [exe-app-architecture](./10-exe-app-architecture.md) | Exe 앱 | 솔루션 구조·MVVM·DI·상태머신·캡처 파이프라인·전역 예외 |
| **11** | [exe-app-features](./11-exe-app-features.md) | Exe 앱 | 기능 상세(촬영·프레임·필터·타임랩스·QR·유휴·설정·브랜딩 등) |
| **12** | [exe-app-settings-and-config](./12-exe-app-settings-and-config.md) | Exe 앱 | AppSettings 전 항목·기본값·INI 폴백·브랜딩·표시모드 |
| **20** | [frontend-web-download-page](./20-frontend-web-download-page.md) | 프론트엔드 | 웹 다운로드 페이지 상태머신·만료 판정·미디어 옵션 구분 |
| **30** | [backend-firebase-integration](./30-backend-firebase-integration.md) | 백엔드 | Firebase 초기화·서비스계정·업로드·프레임/계정·QR·오프라인 폴백 |
| **40** | [database-firestore-and-storage-schema](./40-database-firestore-and-storage-schema.md) | DB | Firestore 컬렉션·Storage 경로 규약·보안 규칙·계약 불변식 |
| **50** | [infra-gcp-lifecycle-and-ttl](./50-infra-gcp-lifecycle-and-ttl.md) | 인프라 | 보관/만료(GCS Lifecycle·Firestore TTL)·적용 명령·비용 |
| **60** | [auth-accounts-and-roles](./60-auth-accounts-and-roles.md) | 인증 | 역할(user/manager/admin)·권한 매트릭스·로그인 유지·계정 저장소 |
| **70** | [logging-and-troubleshooting](./70-logging-and-troubleshooting.md) | 운영 | **로그 위치**·세션/결과물 경로·증상별 원인 위치 매핑 |
| **80** | [build-and-deployment](./80-build-and-deployment.md) | 배포 | 빌드 경로·단일 EXE publish·ffmpeg 번들·인스톨러 |
| **90** | [roadmap-and-future-work](./90-roadmap-and-future-work.md) | 계획 | 알려진 이슈·기술 부채·개선 예정·비범위 |

## 처음 보는 사람을 위한 추천 순서

1. **[00 개요](./00-overview-and-architecture.md)** — 전체 그림부터.
2. 관심 영역으로: 데스크톱이면 **10 → 11 → 12**, 백엔드/DB면 **30 → 40**, 배포/운영이면 **80 → 70 → 50**.
3. 문제 진단이 목적이면 **[70 로깅·진단](./70-logging-and-troubleshooting.md)** 먼저 (로그 위치 포함).
4. 앞으로 할 일/미해결은 **[90 로드맵](./90-roadmap-and-future-work.md)**.

> 각 문서 최상단 메타 표에 "관련 소스 경로"와 "갱신 규칙"이 있습니다. 근거는 대부분 `파일:라인`으로 표기되어 있어 소스와 바로 대조할 수 있습니다.
