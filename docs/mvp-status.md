# MC포토 MVP — 파이프라인 완료 상태 (2026-07-20)

## 결과 요약

| 파이프라인 | 설계 | 구현 | 리뷰 | 판정 |
|-----------|------|------|------|------|
| A — WPF 앱 | wpf-architect (fable) | wpf-developer (fable) | wpf-code-reviewer (opus) | **PASS** — Minor 4건+예방 1건 수정·재검증 완료 |
| B — 웹 다운로드 페이지 | js-architect (opus) | js-developer (opus) | js-code-reviewer (sonnet) | **PASS** — Minor 4건 수정·재검증 완료 |

- **WPF**: `dotnet build -c Release` 경고 0/오류 0, 테스트 106/106 통과. 소스 120파일(App/Core/Capture/Firebase + tests), `installer\MCPhoto.iss` 작성됨(iscc 미컴파일). 리뷰 Minor 4건(카메라 소유권·프레임 단건삭제 Storage 정리·StorageBucket 주입·주석)+Clone 예방 1건 수정, 재검증 PASS.
- **웹**: `web/` 15파일. Firebase Emulator 보안 규칙 테스트 7/7, headless 상태전이 통과. 실배포 미수행. 리뷰 Minor 4건(폴백 안내 전 브라우저 일반화·expiresAt fail-safe·finalImageUrl 폴백·CSP+nosniff) 수정, 재검증 PASS(CSP 기능 회귀 없음).
- 설계 문서: `docs/design/` (wpf-architecture, wpf-wbs, web-architecture[OA-4 보정], web-wbs, firebase-contract — D-1/D-2 확정 표기 반영).

## 사용자 액션 필요 항목

### 1. 최종 UI 검증 (사용자 승인 필요 — 승인 전 앱 실행 금지 정책)
개발 중 화면 노출 금지 정책으로 SKIP된 UI 관측 항목:
1. 프리뷰 실영상·거울반전·크롭 육안, 30fps 렌더 (웹캠 필요)
2. 화면 전환·유휴 타임아웃(75초)·좌상단 롱프레스 관리자 진입
3. [바로 촬영] 카운트다운 스킵 등 촬영 인터랙션
4. 홈→게스트→촬영→선택→결과→QR→완료 E2E 리허설 (웹캠 필요)
5. 프레임 편집기 업로드·슬롯 드래그
6. 관리자 모드(설정·사용자 관리)
7. 클린 머신 설치 리허설 (Inno Setup 컴파일 후)
- 웹 측: 실기기 QR 스캔 진입, iOS Safari 저장 동작, 실 토큰 URL CORS/미디어 표시
- ※ UI 실행 차단 훅: `.claude/settings.json` + `.claude/hooks/block-ui.ps1` — 검증 승인 시 제거/비활성화 필요

### 2. Firebase 관련
- **Blaze 전환**(필수 조건): Storage 업로드(QR 전송)는 Blaze 요금제 필요(2026-02부터 Spark에서 Storage 불가). 미전환 시 `enableQrDelivery=off` + `saveLocalCopy=on` 완화 경로로 동작.
- **서비스 계정 키**: `%ProgramData%\MCPhoto\serviceAccountKey.json` 배치 (없으면 오프라인 완화 경로).
- **웹 config 실값**: `web/public/firebase-config.js`, `web/.firebaserc` 플레이스홀더 교체.
- **Storage 버킷 주입**: 신규 프로젝트는 `*.firebasestorage.app` — `ServiceRegistration.cs`의 bucket 설정 확인 (WPF Minor 3).
- **실배포**: `firebase deploy --only hosting,firestore:rules,storage:rules` + 배포 도메인을 WPF `hostingBaseUrl`에 반영.

### 3. Minor 개선 8건 (리뷰 PASS와 별개, 배포 전 권장)
- 웹 4건: ①`<a download>` cross-origin 전 브라우저 미동작 → 폴백 안내 일반화(설계 보정 수반) ②expiresAt 부재 시 fail-safe ③finalImageUrl 부재 폴백 ④CSP 헤더
- WPF 4건: ①PreviewViewModel의 Singleton 카메라 Dispose 소유권 ②프레임 단건 삭제 시 Storage 고아 파일 ③Storage 버킷 기본값 ④잔여 주석
- 상세: 태스크 #19(웹), #20(WPF)

### 4. 기타
- **Inno Setup 설치** 후 `iscc installer\MCPhoto.iss` 컴파일 검증.
- **ffmpeg 라이선스**: 번들(gyan.dev essentials)은 libx264 포함 GPL 빌드 — 상업 배포 시 LGPL 빌드 검토.
- git 커밋: 지시 대기 (미커밋 상태).
