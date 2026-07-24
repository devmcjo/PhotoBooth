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
  - `firebase functions:secrets:set SENDGRID_API_KEY`  (item1a — 코드에 **선언돼 있어 배포 시 반드시 존재해야 함**. 이메일을 아직 안 쓰면 임시값이라도 등록. 실제 발송은 §B1에서 활성화)
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

## B. 계정 기능 — 이메일/SSO 관련 콘솔 작업

### B1. 이메일 발송 공급자 (item1a 이메일 인증·비밀번호 찾기)
> 서버 코드는 완료(이메일 인증·재설정 엔드포인트, 토큰 발급/소비, 이메일 추상화). **개발 기본은 `log` sender로 실제 메일이 발송되지 않는다.** 프로덕션 실발송을 위해 아래를 수동 수행한다. 코드 근거: `web/functions/src/{config.ts, services/email.ts}`, 설계 `docs/design/wpf-accounts-email-verification-design.md §10·§11.2`.

- **B1-1. 이메일 공급자 계정·API 키** `[ ]`: SendGrid(권장) 계정 생성 → API 키 발급 → Functions secret 등록:
  `firebase functions:secrets:set SENDGRID_API_KEY` (web/ 디렉토리에서). **키를 리포/.env에 넣지 말 것.** (대안 SMTP/Firebase Extension 채택 시 별도 설정.)
- **B1-2. 발신 도메인·발신자 등록** `[ ]`: SendGrid Sender Authentication(도메인 SPF/DKIM 또는 Single Sender)으로 발신 주소 인증. 그 주소를 함수 env `EMAIL_FROM`(예: `no-reply@도메인`)에 설정.
- **B1-3. 공급자 활성화(콘솔만, 소스 수정 불요)** `[ ]`: 함수 env `EMAIL_PROVIDER=sendgrid` 설정(미설정/`log`면 개발용 로그 sender — 실제 메일 미발송). 프로덕션 실발송 전 `sendgrid`로 전환 + `SENDGRID_API_KEY`에 **실제 키** 등록(A1/B1-1).
  - ✅ 배포 배선은 **코드에 이미 반영됨**: `index.ts`가 `SENDGRID_API_KEY`를 `defineSecret` 선언 + `api` 함수 `secrets:` 배열에 포함(런타임 주입). 따라서 활성화는 **env 전환 + 시크릿 값**만으로 끝남(소스 편집 불필요). 단 선언된 시크릿이라 **모든 배포에서 존재해야** 하므로 log 모드여도 A1에서 임시값이라도 등록해 둘 것.
- **B1-4. (링크 방식 채택 시) 웹 verify/reset 페이지** `[ ]`: 이메일 링크 대상 `{hostingBaseUrl}/verify`·`/reset` 정적 페이지(js 팀). **코드 방식(앱 내 6자리 코드 입력)만이면 불요** — item1a 서버는 코드·링크 두 경로를 모두 지원하지만 클라 UI는 코드 경로 우선.
- **B1-5. (선택) 토큰 서브컬렉션 TTL 정책** `[ ]`: `users/{id}/tokens` 문서의 `expiresAt`에 Firestore 네이티브 TTL을 걸어 만료 토큰 자동 청소(resultSessions 방식). 서버는 confirm 시 만료를 코드로도 재확인하므로 TTL 미설정이어도 보안엔 무해(청소 목적).
- **B1-6. Firestore 규칙 점검** `[ ]`: `firestore.rules`의 catch-all `match /{document=**} { allow read, write: if false }`가 `users/{id}/tokens/**` 서브컬렉션까지 전면 deny함을 확인(현행 규칙 이미 충족 — 웹 접근 없음, Admin 서버만 접근). 규칙 변경 불필요.

### B2. Google OAuth 클라이언트 (item1b Google SSO)
> **서버 코드는 완료** — `/auth/google`(code 교환 + id_token 검증 + email 매핑 + JWT 재사용), 순수 검증(`domain/validation.ts`), Google 검증 격리(`services/googleAuth.ts`), config·시크릿 배선(`config.ts`·`index.ts`). **개발 기본은 client id/secret 미설정 → `/auth/google`가 501(비활성).** 실제 SSO를 켜려면 아래를 수동 수행한다. 코드 근거: `web/functions/src/{config.ts, index.ts, routes/auth.ts, services/googleAuth.ts, domain/validation.ts}`, 설계 `docs/design/wpf-google-sso-design.md §5·§8.2·§9.2`.
> **사전조건**: item1b는 백엔드 모드(`UseBackend=true`, §A5)에서만 동작 — A 섹션(백엔드 배포·전환)이 선행돼야 SSO 사용 가능.

- **B2-1. OAuth 동의 화면(OAuth consent screen) 구성** `[ ]`: Google Cloud 콘솔 → APIs & Services → OAuth consent screen. User Type(사내 Workspace면 Internal, 외부면 External), 앱 이름·지원 이메일·로고, scope `openid`·`email`·`profile` 추가. External이면 테스트 사용자 등록 또는 게시(verification) 필요.
- **B2-2. OAuth 2.0 클라이언트 ID 생성(Desktop app)** `[ ]`: Credentials → Create Credentials → OAuth client ID → **Application type: Desktop app**. 생성 후 **Client ID**와 **Client Secret** 확보.
  - ⚠️ Desktop 클라이언트는 loopback 리디렉션에서 **포트를 무시**하므로 별도 리디렉션 URI 등록이 불필요할 수 있으나, 콘솔이 요구하면 `http://127.0.0.1`·`http://localhost`(포트 없이) 등록. **Web application 유형이 아님에 주의**(Web은 정확한 URI·포트 매칭 요구).
- **B2-3. 백엔드 자격 등록** `[ ]`: `cd web/functions` →
  - `firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET` (B2-2의 Client Secret) — **백엔드 전용, 클라(WPF)에 넣지 않음.**
  - `GOOGLE_OAUTH_CLIENT_ID`(비밀 아님)는 함수 env/param에 설정. **코드/리포에 하드코딩 금지.**
  - ⚠️ `index.ts`에 `defineSecret("GOOGLE_OAUTH_CLIENT_SECRET")`가 **선언돼 있어 모든 배포에서 존재해야** 하므로, SSO를 아직 안 켜더라도 **최초 배포 전 임시값이라도 등록**(SENDGRID_API_KEY와 동일 주의, §A1). client id/secret이 모두 설정돼야 `/auth/google` 활성화(한쪽만 설정하면 서버가 오구성으로 조기 실패, 둘 다 비우면 501).
- **B2-4. 클라이언트 설정(배포 PC INI)** `[ ]` (클라 S4~S6 개발 후): 대상 PC `MCPhoto.ini` `[MCPhoto]`에 `GoogleClientId=<B2-2 Client ID>` 추가. **client secret은 클라에 넣지 않음**(백엔드 전용).
- **B2-5. (선택) 허용 도메인(hd) 제한** `[ ]`: 특정 Workspace 도메인만 허용하려면 함수 env `GOOGLE_ALLOWED_HD=<도메인>` 설정(서버가 id_token.hd와 대조). 미설정이면 email 매핑 화이트리스트로만 통제.
- **B2-6. 실왕복 스모크(배포/스테이징)** `[ ]`: 코드는 `OAuth2Client` mock 단위 테스트 + Emulator 형식/게이트 스모크로 완결됨(실 Google 왕복은 불가). B2 설정 후 스테이징에서 **운영자 계정 1건 선등록(email + emailVerified=true)** → SSO 로그인 → 화면 진입 수동 확인.

---

## C. (예정) 장치 연동
### C1. 카메라(DSLR)·프린터 장비 선정 `[ ]` (item3)
- 실제 하드웨어 연동은 특정 모델/SDK/연결방식(BT·WiFi) 의존 → **장비 선정·드라이버·SDK 조사 필요**. 이번엔 코드에 추상 클래스/옵션 자리만 만들며, 실제 연동은 장비 확정 후.
