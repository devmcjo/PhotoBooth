---
name: photobooth-backend-proxy-contract
description: MCPhoto 백엔드 프록시(방향 B) HTTP 계층 리뷰 시 서버(web/functions)와 클라(MCPhoto.Http)를 1:1 대조하는 계약 체크포인트
metadata:
  type: project
---

MCPhoto는 Firebase Admin 직결(레거시) → 백엔드 HTTPS API 프록시(방향 B)로 점진 전환 중이다. 클라 HTTP 계층 = `src/MCPhoto.Http/`, 서버 계약 = `web/functions/src/routes/*`·`services/dto.ts`. 리뷰 시 아래를 1건씩 대조한다.

**계약 정합 체크포인트(서버가 준거):**
- 에러 봉투 = `{error:{code,message}}`. code = unauthorized/forbidden/conflict/invalid_argument/not_found/internal (`http/errors.ts`).
- API 키 헤더 = `X-MCPhoto-Client`(공개/게스트 엔드포인트). 계정 조작 = JWT `Bearer`. 두 인증 독립.
- JSON = camelCase + case-insensitive + null 유지(`DefaultIgnoreCondition=Never`) — `finalImageUrl:null` 등 명시적 null이 계약(it7 F2).
- 업로드 = 서명 URL 방식 A: prepare(서명 PUT URL+다운로드 토큰 발급) → 클라 직접 PUT(진행률 유지) → commit(resultSession). requiredHeaders 분리 부착: `Content-Type`=콘텐츠 헤더, `x-goog-meta-firebaseStorageDownloadTokens`=요청 헤더.
- `UploadFileAsync`는 **토큰만** 반환하고 `UploadService`가 `UploadContract.TokenDownloadUrl(Bucket, path, token)`로 URL 재조립 → 서버 `tokenDownloadUrl`과 동일 형식·동일 Bucket이어야 정합. commit의 `assertUrlBelongsToSession`이 버킷·세션경로를 검증하므로 버킷 불일치 시 400.
- final ext/contentType 화이트리스트: 서버 KIND_SPEC = `jpg/png` + `image/jpeg`·`image/png`, timelapse=`mp4`+`video/mp4`. retentionHours 정수 1~72.

**안전 불변식(설계 §8.1):** DI feature flag `AppSettings.UseBackend` 기본 OFF=현행 Firebase 무회귀(팩토리 람다가 FirebaseClient 싱글턴 공유), ON=Http* 분기. 빈 URL이면 `NormalizeBackend`가 UseBackend=false로 폴백. **BaseAddress에 `/api/` 경로가 있으므로 상대 URL은 반드시 선행 슬래시 없이** 시작해야 경로 보존(Clamp가 트레일링 슬래시 강제와 짝). `IsInitialized`=base URL 설정 사실(실시간 헬스체크 게이팅 금지 — 일시 지연에 업로드 오차단 방지, 도달성은 호출 실패→상위 폴백).

**Why:** P3 리뷰(2026-07-24)에서 위 정합이 모두 정확했고 테스트 402개가 in-memory fake로 계약을 검증. 서버 미배포로 실 네트워크는 미검증 — 배포 후 서명 PUT E2E·버킷 일치·BaseUrl 경로를 반드시 확인.

**How to apply:** Http 계층 변경 리뷰 시 서버 라우트/dto.ts를 먼저 읽고 위 체크포인트를 대조. AppSettings 신규 백엔드 필드는 [[photobooth-settings-roundtrip-convention]] 4곳 관례도 함께 적용.
