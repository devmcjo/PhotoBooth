---
name: functions-backend-proxy
description: web/functions(Cloud Functions 2nd gen, TypeScript) 백엔드 프록시의 빌드·검증·Emulator 스모크 방법과 서명 URL Emulator 제약
metadata:
  type: project
---

WPF Admin 키 제거용 서버 경유 계층(설계 `docs/design/wpf-backend-proxy-migration-design.md`)의 서버 구현이 `web/functions/`에 있다. 기존 web Firebase 프로젝트(project=mcphoto-955fb)에 통합됨.

**구조**: 순수 도메인 로직 `src/domain/*`(roles/session/password/jwt/validation, C# UploadContract·UserRoleExtensions 이식) + `src/services/*`(Firestore/Storage 조작) + `src/routes/*`(Express) + `src/http/*`(auth 미들웨어·에러). 단일 함수 `api`(onRequest+Express)로 URL은 `.../api/{path}`.

**검증 명령**(cwd=`web/functions`):
- `npm run build`(tsc) / `npm run typecheck`(tsc --noEmit) / `npm run lint`(eslint) / `npm test`(jest, 순수 domain만)
- Emulator 스모크(cwd=`web`): `firebase emulators:exec --only functions,firestore,storage --project mcphoto-955fb "node functions/smoke/smoke.mjs"` — Admin(규칙우회)으로 시드 후 실 HTTP 호출로 12엔드포인트 검증.

**핵심 제약 — 서명 URL은 Emulator에서 실패한다**: `file.getSignedUrl(v4)`는 Storage Emulator/ADC에서 `Cannot sign data without client_email`로 실패한다(배포는 런타임 SA가 IAM signBlob로 서명 → 사용자가 콘솔에서 Service Account Token Creator 역할 부여 필요). `signing.ts`는 `FIREBASE_STORAGE_EMULATOR_HOST` env가 있으면 서명을 우회해 Emulator 업로드 URL을 반환(배포엔 이 env 없음 → 항상 서명 경로). 프로덕션 서명 동작은 Emulator로 검증 불가 — 사용자 콘솔 배포 후 확인 몫.

**시크릿 규약**: JWT_SECRET·CLIENT_API_KEYS는 `defineSecret`(Secret Manager), 로컬은 `web/functions/.env`(gitignore). 키 파일 절대 없음(ADC 초기화). `.env`는 `web/functions/.gitignore:6`이 커버.

**Node engine 경고 무해**: package.json engines=20, 로컬 Node v25 → EBADENGINE 경고 뜨나 빌드/테스트 정상.

**테스트 관례**: 라우트는 supertest 미사용 — 서비스 함수를 직접 호출하고 `db()`는 `jest.mock("../firebase")`로 `FakeFirestore`(helpers/fakeFirestore.ts) 주입. 미들웨어는 Request/Response/next를 최소 모킹해 단위 테스트. 설계 문서의 엔드포인트·DTO·에러코드를 계약으로 그대로 따른다(클라 C#이 이에 맞춤).

**⚠️ FakeFirestore 제약(카운트 증가 설계에 직결)**: `FieldValue.increment` 미지원 — `set`/`update`가 shallow merge라 sentinel 객체를 그대로 저장한다. 따라서 원자 카운터는 **트랜잭션 내 read-modify-write**(현재값 읽어 `+1`)로 구현해야 fake로 검증 가능하고 실 Firestore와도 정합(트랜잭션은 read-modify-write). `runTransaction`은 격리 없이 store 즉시 반영(순차 테스트로 경합 근사). where는 `==`만 지원. **컬렉션 레벨 `.get()` 미지원** — `collection(x).get()`(전체 조회, 예: `listAccounts`)은 fake에서 TypeError. accounts.test.ts에서 `toResponse` 파생 응답 필드를 검증할 땐 `listAccounts` 대신 응답을 반환하는 경로(`createAccount`/`loginWithGoogleEmail`의 `.user`)로 확인한다.

**it14 설정 진입 PIN 게이트 서버 계약(구현 완료)**: `UserDoc.authMethod?("sso"|"password", 미설정=password 폴백)`·`pinHash?(bcrypt, password.ts 재사용)` 추가. `UserResponse.authMethod`(폴백 "password")·`hasPin`(pinHash 존재 파생, 원문 미노출). authMethod는 **생성 시점에만** 세팅: `createGoogleAccount`="sso", `createAccount`/`registerSelf`="password". `loginExistingGoogleAccount`는 미변경(비번 계정이 SSO email로 로그인해도 password 유지). 순수 `validatePin`(validation.ts, `^\d{4}$` **4자리**). 권한은 기존 `canManage` 재사용(PIN 전용 매트릭스 없음). 엔드포인트 3종(모두 requireBearer, `routes/accounts.ts`, me/pin*은 /:id/pin보다 먼저 등록): **E1** `POST /accounts/me/pin/verify {pin}`→200`{ok:true}`/401 불일치/409 미설정, **E2** `PUT /accounts/me/pin {newPin,currentPin?}`→204(기존 PIN 있으면 currentPin 확인 필수 401, 없으면 최초 설정), **E3** `PUT /accounts/:id/pin {newPin}`→204(canManage 위반 403, 자기자신 400, 대상없음 404). 서비스 `verifyPin`(VerifyPinResult 판별유니온)·`setOwnPin`·`resetOtherPin`. 클라 C#은 camelCase 직렬화(BackendJson.Options)로 이 계약에 정합.

**it13 TempUser 역할 서버 계약(구현 완료)**: 역할 문자열 `"temp_user"`(위계 temp_user<user<manager<admin, `isPower=false`). `roles.ts`는 서수 아닌 `MANAGE_RANK`로 canManage 판정(C# ManageRank 대칭). 업로드에 `optionalBearer()`(auth.ts) 미들웨어 — bearer 있으면 principal 주입, 없으면 게스트 통과, 무효면 401. 한도: 계정 `qrUsedCount`(UserDoc) + 전역 config `config/tempUserLimits`(부재 시 48h/30회 폴백, `services/config.ts`). 순수 판정 `evaluateQrGate`(domain/tempUserLimit.ts, 시간 우선). 엔드포인트: `GET /accounts/me/qr-usage`(requireBearer), `GET/PATCH /config/temp-user-limits`(PATCH requireAdmin). 초과 에러코드 403 `TEMP_USER_TIME_EXCEEDED`/`TEMP_USER_COUNT_EXCEEDED`(errors.ts, 문구는 설계 §0 고정). commit 성공=세션1회, sessionId 중복 409로 이중집계 차단.

**setRole 권한 매트릭스(순수 `canSetRole(actor,current,target)` in roles.ts, 서버 강제)**: 승격(랭크 상승)=admin 전용, user→temp_user 강등=admin+manager. Admin: target∈{temp_user,user,manager}(admin 지정/admin 대상 거부). Manager: 오직 current=user→target=temp_user만(그 외 no-op 포함 전부 403). role 라우트는 requirePower로 열되(user/temp_user 조기 차단) 세부는 setRole이 강제. `PATCH /accounts/:id/role`.
