# USER ACTIONS — 내가(운영자) 직접 해야 하는 작업

| 항목 | 값 |
|------|-----|
| 목적 | 코드로 자동화할 수 없는 **콘솔·배포·외부계정·하드웨어** 작업을 한곳에 모은 실행 런북. 기능 개발 시 발생하는 "사용자 수동 작업"을 계속 여기에 추가한다. |
| 대상 독자 | 프로젝트 소유자(DB 접근 권한 보유자) |
| 최종 업데이트 | 2026-07-24 |
| 관련 | 설계 `design/wpf-backend-proxy-migration-design.md` · 순차계획 `design/backlog-post-backend-migration.md` |

> 표기: `[ ]` 미완 / `[x]` 완료. **⚠️ 불가역** 표시 항목은 순서를 반드시 지킬 것.

---

## A. 백엔드 경유 마이그레이션 배포 (서비스 계정 키를 클라이언트에서 제거)

> 코드는 완료됨 — 서버 함수(P1, `web/functions/`)와 클라 HTTP 계층(P3, `MCPhoto.Http`, DI flag **기본 OFF**). 아래는 **배포·전환·키 폐기**로, 전부 콘솔/CLI 작업이다.
> **핵심 안전 불변식**: 서비스 계정 키 폐기(A7)는 **반드시 맨 마지막**, 클라 HTTP 전환(A5)이 프로덕션에서 검증된 뒤에만. 그 전까지는 현행 키 경로가 살아 있어 언제든 롤백 가능(앱의 `UseBackend=false`).

### A1. 시크릿 등록 `[ ]`
- `cd web/functions`
- 강한 랜덤 값으로:
  - `firebase functions:secrets:set JWT_SECRET`  (예: `openssl rand -base64 48`)
  - `firebase functions:secrets:set CLIENT_API_KEYS`  (배포별 클라 API 키, 콤마 구분 가능)
- 일반 설정(비밀 아님)은 함수 env/param: `STORAGE_BUCKET`, `HOSTING_BASE_URL`, `JWT_EXPIRES_IN_SECONDS`(기본 8h).
- ⚠️ `web/functions/.env`의 개발용 값(특히 `JWT_SECRET`)을 **프로덕션에 쓰지 말 것**. `.env`는 git 미추적(로컬 Emulator 전용).

### A2. IAM — 서명 URL 권한 `[ ]`
- 함수 런타임 서비스계정에 **Service Account Token Creator**(`iam.serviceAccounts.signBlob`) 부여.
- 없으면 프레임 저장·업로드 prepare의 **서명 PUT URL 발급이 프로덕션에서 실패**(Emulator로는 검증 불가한 지점).

### A3. 리전 확인 `[ ]`
- 현재 기본 `asia-northeast3`(서울) — `web/functions/src/index.ts`의 `setGlobalOptions`. 다르게 쓰려면 조정 후 배포.

### A4. 함수 배포 (현행 Admin 경로와 공존) `[ ]`
- `firebase deploy --only functions`
- 배포만으로는 앱 동작 불변(앱 `UseBackend=false`가 기본). 서버가 먼저 안정적으로 떠 있게 하는 단계.
- 배포 후 스모크: `GET {baseUrl}/api/health` 200, 로그인/프레임/업로드 1회 E2E(아래 A6 검증표).

### A5. 클라이언트 백엔드 전환 (feature flag ON) `[ ]`
- 대상 PC의 `MCPhoto.ini` `[MCPhoto]` 섹션에:
  - `UseBackend=true`
  - `BackendBaseUrl=https://<region>-<project>.cloudfunctions.net/api`  (라우트 마운트가 `/api`이므로 **끝에 `/api` 포함**)
  - `BackendApiKey=<A1에서 정한 CLIENT_API_KEYS 중 하나>`
- 빈 URL이면 앱이 자동으로 off로 되돌림(안전장치). 문제가 생기면 `UseBackend=false`로 즉시 롤백.

### A6. 배포 후 E2E 검증 `[ ]`
- [ ] `GET /api/health` 도달
- [ ] 로그인(manager/user) → JWT 수신, 화면 진입
- [ ] 프레임: 기본 프레임 조회 + (파워) 저장 시 서명 PUT 성공, 목록 반영
- [ ] 업로드: prepare→PUT→commit 후 **웹 다운로드 페이지(`/?s=...`)가 사진/타임랩스 표시**
- [ ] 서명 PUT의 `x-goog-meta-firebaseStorageDownloadTokens`가 서명과 일치(불일치 시 다운로드 실패)
- [ ] 서버/클라 `StorageBucket` 일치(불일치 시 commit 400)
- [ ] 역할 위계: manager로 admin 삭제/초기화/역할지정 시도 → 서버 403

### A7. ⚠️ 서비스 계정 키 폐기 (불가역, 최후) `[ ]`
- A5·A6가 프로덕션에서 안정 확인된 **후에만**.
- publish 산출물/PC에서 `serviceAccountKey.json` 제거 + **GCP IAM에서 해당 키 회전/삭제**.
- 이후 앱은 백엔드 경유로만 DB 접근 → 키는 함수 런타임(ADC)에만 존재, 배포물엔 없음(목표 달성).
- 되돌릴 수 없으므로, 롤백 필요성이 완전히 사라졌다고 판단될 때 수행.

### A8. 보안 규칙 강화 (P5) `[ ]`
- 서버(Admin=규칙 우회)만 DB에 쓰므로, Firestore/Storage 규칙은 현행 "직접 접근 deny(웹 다운로드 토큰 URL만 허용)" 유지로 충분. WPF 직접 접근 제거(A7) 후 규칙에 추가로 열어둔 경로가 없는지 최종 점검.

### A9. 기존 평문 비밀번호 계정 `[ ]`
- 코드가 **로그인 성공 시 자동으로 bcrypt 해시로 지연 마이그레이션**(별도 배치 불필요).
- 실제 계정 수가 적으면, 안전을 위해 로그인 유도 또는 비밀번호 초기화로 조기 전환 권장(선택).

---

## B. (예정) 계정 기능 — 이메일/SSO 관련 콘솔 작업
> item1a/1b 개발 착수 시 아래를 채운다(현재 코드 미착수 → 자리만).

### B1. 이메일 발송 공급자 `[ ]` (item1a 이메일 인증·비밀번호 찾기)
- 이메일 인증/재설정 메일 발송을 위한 공급자 설정 필요(예: SendGrid/SMTP/Firebase Extension). 발신 도메인·API 키 등록은 콘솔 작업. (공급자 선정은 개발 시 합리적 기본안 제시)

### B2. Google OAuth 클라이언트 `[ ]` (item1b Google SSO)
- Google Cloud 콘솔에서 **OAuth 2.0 클라이언트 ID/secret 생성**, 승인 리디렉션 URI·동의화면 설정. 자격증명을 백엔드 시크릿에 등록.

---

## C. (예정) 장치 연동
### C1. 카메라(DSLR)·프린터 장비 선정 `[ ]` (item3)
- 실제 하드웨어 연동은 특정 모델/SDK/연결방식(BT·WiFi) 의존 → **장비 선정·드라이버·SDK 조사 필요**. 이번엔 코드에 추상 클래스/옵션 자리만 만들며, 실제 연동은 장비 확정 후.
