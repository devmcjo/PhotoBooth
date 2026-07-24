# MCPhoto — item1a: 계정 이메일 인증 + 비밀번호 찾기(재설정) 설계

| 항목 | 값 |
|------|-----|
| 문서 성격 | **설계 문서**(코드/배포 미착수) — item1a 신규분(이메일 인증 + 비밀번호 찾기). pw 해시·레거시 평문 지연 마이그레이션은 P1에서 **이미 구현됨**(본 문서 비범위, `web/functions/src/domain/password.ts`, `services/accounts.ts:42-63`) |
| 대상 | 백엔드 `web/functions/`(P1 코드 완료·미배포) + 클라 `MCPhoto.Http`(P3 완료) + `MCPhoto.App` UI |
| 아키텍처 전제 | 방향 B(백엔드 경유) 확정. 온라인 전용. 계정/인증은 백엔드 중심, 클라는 HTTP. (`docs/design/wpf-backend-proxy-migration-design.md`) |
| 작성일 | 2026-07-24 |
| 상태 | **설계 v1 (리뷰 대기)** |
| 근거 | 모든 "현재 동작"은 `파일:라인` 실측. 실측 파일 목록은 §13 |
| 후속 | 확정 후 별도 WBS 블루프린트(`docs/templates/WBS_BLUEPRINT.md` 형식)로 단계화 |

> **표기 규칙**
> - `[CODE]` : 내가 코드로 작업(서버 엔드포인트·토큰 로직·클라 화면·이메일 인터페이스).
> - `[CONSOLE]` : 사용자가 콘솔·CLI·외부계정에서 수동 수행(이메일 공급자 자격·도메인·발신자). §11에 USER-ACTIONS §B1 추가 목록으로 정리.
> - `[CONFIRM]` : 설계자가 합리적 기본안으로 **확정**했으나 리뷰 시 조정 가능한 결정. 근거 명시.
> - `[USER-DECISION-REQUIRED]` : 설계자가 정할 수 없는 순수 제품/운영 판단. 사용자 답변 필요.
> - 근거는 `파일:라인`. **가정**은 소스 미확인 추정.

---

## 0. 요약 (Executive Summary)

### 0.1 무엇을 추가하는가

현행 계정 스키마는 `users = {id, password(해시), role, createdAt}`로 **email 필드가 없다**(`web/functions/src/services/dto.ts:13-19`, `src/MCPhoto.Core/Models/User.cs:7-17`). 이메일 인증·비밀번호 재설정은 계정에 검증된 이메일이 있어야 성립하므로, 본 설계는:

1. **스키마 확장**: `users`에 `email`·`emailVerified` 추가. 검증/재설정 토큰은 **별도 서브컬렉션**(`users/{id}/tokens/{tokenId}`)에 만료·1회성·해시 저장 형태로 보관(§4).
2. **이메일 인증 플로우**: 계정 생성 시 이메일 수집 → unverified 계정 생성 → 인증 메일 발송 → 링크/코드로 verify(§5).
3. **비밀번호 찾기(재설정)**: id 또는 email로 요청 → 재설정 토큰 메일 → 새 비번 설정(bcrypt 해시 저장). **이메일 검증된 계정만**(§6).
4. **기존 계정(email 없음) 처리**: 로그인은 유지되나 재설정 불가 → 파워/admin이 이메일 지정 + 본인 자발 등록 유도(§7).
5. **엔드포인트 확장**: `POST /accounts`에 `email` 추가, 신규 `/auth/verify-email/{request,confirm}`·`/auth/password-reset/{request,confirm}`·`/accounts/{id}/email`(§8). 클라 HTTP·화면(계정 생성 email 입력, "비밀번호 찾기" 화면, 인증 대기 UX)(§9).
6. **이메일 발송 추상화**: 백엔드에 `EmailSender` 인터페이스(공급자 무관) + 개발용 로그 sender. 실제 공급자는 USER-ACTIONS §B1로 미룸(§10).

### 0.2 핵심 정책 결정(요약)

| 결정 | 값 | 근거 |
|------|-----|------|
| unverified 계정 로그인 허용? | **허용(로그인 가능, 기능은 제한 안 함)** `[CONFIRM]` §5.5 | 키오스크 운영 계정은 즉시 사용돼야 함. verify는 "재설정 자격"을 여는 것이지 로그인 게이트가 아님 |
| 재설정 자격 | **`emailVerified=true` 계정만** §6.1 | 이메일 소유 미확인 계정 재설정은 계정 탈취 벡터 |
| 인증 방식 | **링크(토큰 URL) + 6자리 코드 병행** `[CONFIRM]` §5.3 | 링크는 이메일 클릭, 코드는 키오스크(브라우저 없는 PC)에서 수기 입력. 같은 토큰의 두 표현 |
| 토큰 저장 | 서브컬렉션 `users/{id}/tokens/{tokenId}`, **해시 저장**, 만료·1회성 §4.2 | 응답·DB 평문 노출 금지. resultSessions 토큰 규약과 동일 철학 |
| 열거 방지 | 존재하지 않는 id/email에도 **동일 202 응답** §6.2·§12 | 계정/이메일 존재 여부 노출 차단 |
| 이메일 공급자 | 개발=로그 sender(no-op). 프로덕션=**SendGrid 권장** `[CONFIRM]` §10.3 | Firebase 생태계 친화·무료 한도·Node SDK. 자격 등록은 `[CONSOLE]` |

---

## 1. .NET / 스택 컨텍스트 (변경 없음, 확장만)

- **클라**: .NET 8 WPF, CommunityToolkit.Mvvm, `Microsoft.Extensions.DependencyInjection`, `IHttpClientFactory`. 본 설계는 `MCPhoto.Http`·`MCPhoto.App`에 **파일 추가**만 하고 기존 인프라(세션 홀더·에러 매핑·PasswordBox code-behind 전달)를 재사용한다(`src/MCPhoto.Http/HttpBackendClient.cs`, `HttpAccountService.cs`).
- **서버**: Cloud Functions 2nd gen + TypeScript + Express, 단일 함수 `api`에 라우터 마운트(`web/functions/src/{index,app}.ts`). JWT(HS256), bcrypt, `domain/*` 순수함수 + jest 테스트 관례. 본 설계는 여기에 라우트·서비스·도메인 함수를 **추가**한다.
- **이메일 발송 신규 의존** `[CONFIRM]`: `@sendgrid/mail`(프로덕션 구현). 개발/테스트는 외부 의존 0(로그 sender). 인터페이스로 추상화해 공급자 교체가 1파일 교체로 끝나게 한다(§10).

---

## 2. 현행 계정 흐름 실측 (확장 지점 식별)

| # | 현재 동작 | 근거 | item1a 확장점 |
|---|-----------|------|---------------|
| C1 | 계정 생성: `{id, password, role}` → 해시 저장, email 없음 | `web/functions/src/routes/accounts.ts:30-46`, `services/accounts.ts:70-95` | `email` 필드 추가·검증·저장 + 인증 메일 발송(§5.2) |
| C2 | 로그인: 해시 검증 → JWT 발급. 실패 401→null | `routes/auth.ts:18-46`, `services/accounts.ts:42-63` | 정책상 무변경(unverified도 로그인 허용, §5.5). 응답에 `emailVerified` 포함 검토 |
| C3 | 비번 변경: 본인/파워, `{newPassword}` PATCH | `routes/accounts.ts:57-69`, `services/accounts.ts:114-128` | **무변경**(로그인 상태의 자발 변경). "비밀번호 찾기"(비로그인 재설정)와 별개 |
| C4 | 클라 `IAccountService.CreateAsync(id, pw, role, actingRole)` | `src/MCPhoto.Core/Accounts/IAccountService.cs:18` | 시그니처에 email 추가 필요(§9.1) — 인터페이스 변경 |
| C5 | 계정 생성 UI: id·pw·역할 입력(`AccountViewModel`) | `src/MCPhoto.App/ViewModels/AccountViewModel.cs:62-64,132-169` | email 입력 필드·바인딩 추가(§9.3) |
| C6 | 로그인 UI: id/pw, "취소"만(비밀번호 찾기 링크 없음) | `src/MCPhoto.App/Views/LoginGuestView.xaml:35-39` | "비밀번호 찾기" 링크·화면 추가(§9.4) |
| C7 | `users` 웹 접근 **전면 deny**(read/write false) | `docs/design/firebase-contract.md:52,182` | email·emailVerified·토큰 서브컬렉션 추가해도 **웹 규칙 무영향**(서버 Admin만 접근). 계약 변경 최소(§4.4) |
| C8 | 응답 DTO에 password/해시 절대 미포함 | `services/dto.ts:44-48`, `services/accounts.ts:23-29` | `email`·`emailVerified`는 응답 포함 가능, **토큰·해시는 절대 미포함**(§8.4) |

**핵심 관찰**: 계정 생성/로그인/비번변경/역할/삭제 경로는 이미 완성돼 있고, item1a는 그 위에 **email 수집·인증·재설정**을 얹는 **가산(加算) 변경**이다. 기존 계약(예외 매핑·JWT·역할 게이트)은 보존한다.

---

## 3. 목표 흐름 개요 (다이어그램)

### 3.1 이메일 인증 (계정 생성 시)

```
[파워 계정] 계정 생성(id, pw, role, email)
      │  POST /accounts  (Bearer, 파워)
      ▼
[서버] email 검증 → users/{id} 생성(emailVerified=false)
      │  → verify 토큰 생성(해시 저장, 만료 24h, users/{id}/tokens)
      │  → EmailSender.sendVerification(email, link, code)
      ▼
[대상 사용자] 메일 수신 → 링크 클릭 or 코드 6자리 입력
      │  POST /auth/verify-email/confirm  (API키)  {token}  또는 {id, code}
      ▼
[서버] 토큰 해시 대조·만료·1회성 확인 → emailVerified=true, 토큰 소비(삭제)
      ▼
[결과] 이제 이 계정은 "비밀번호 찾기" 자격을 가진다
```

### 3.2 비밀번호 찾기(재설정) — 비로그인 흐름

```
[사용자] 로그인 화면 "비밀번호 찾기" → id 또는 email 입력
      │  POST /auth/password-reset/request  (API키)  {idOrEmail}
      ▼
[서버] 계정 조회 → emailVerified=true면 reset 토큰 생성(해시, 만료 1h) → 메일 발송
      │  ★ 존재하지 않거나 unverified여도 **동일한 202 응답**(열거 방지, §12)
      ▼
[사용자] 메일의 링크/코드 + 새 비밀번호 입력
      │  POST /auth/password-reset/confirm  (API키)  {token|(idOrEmail,code)}, {newPassword}
      ▼
[서버] 토큰 대조·만료·1회성 → 새 비번 bcrypt 해시 저장, 토큰 소비, (선택)기존 세션 무효화 참고 §6.4
      ▼
[결과] 새 비밀번호로 로그인 가능
```

- 두 confirm 엔드포인트는 **API 키 게이트**만 요구(로그인 전 상태). Bearer 불요.

---

## 4. 스키마 변경

### 4.1 `users` 문서 확장

현행(`web/functions/src/services/dto.ts:13-19` `UserDoc`):

```
users/{id} = { id, password(bcrypt 해시), role, createdAt }
```

item1a 확장:

| 신규 필드 | 타입 | 기본/의미 |
|-----------|------|-----------|
| `email` | `string \| null` | 계정 이메일. 없는 기존 계정은 `null`(§7). 소문자 정규화 저장 |
| `emailVerified` | `bool` | 이메일 소유 확인 여부. 생성 시 `false`, verify 성공 시 `true`. 기존 계정은 `false`(사실상 null email이므로) |

- `email`은 `null` 허용(레거시·미수집 계정). **유일성**: 이메일 중복 허용 여부는 §4.5 결정.
- 응답 DTO(`UserResponse`)에 `email`·`emailVerified` **추가**(토큰·해시는 여전히 미포함, §8.4).
- Firestore 저장 키는 camelCase(`email`, `emailVerified`) — 현행 관례(`dto.ts:5`).

### 4.2 토큰 서브컬렉션 `users/{id}/tokens/{tokenId}`

검증/재설정 토큰을 **별도 서브컬렉션**에 둔다. `[CONFIRM]` 별도 필드가 아닌 서브컬렉션 선택 이유: (a) 토큰 다건 동시 존재 가능(재요청 시), (b) TTL로 개별 만료 자동청소 가능, (c) `users` 문서 자체가 비대해지지 않음, (d) 응답 매핑 시 실수로 토큰이 새어나갈 표면을 분리.

| 필드 | 타입 | 의미 |
|------|------|------|
| `id`(문서ID) | string | 토큰 조회 lookup 키. **비밀 아님**(URL·요청에 노출되는 selector). `{UUIDv4}` |
| `purpose` | string | `"verify_email"` \| `"password_reset"` |
| `secretHash` | string | **토큰 비밀값의 해시**(bcrypt 또는 sha256, §4.3). 평문 비밀은 저장 안 함 |
| `code` | string(해시) | 6자리 코드의 해시(키오스크 수기 입력 경로용, §5.3). 링크 전용이면 미사용 |
| `email` | string | 이 토큰이 검증하려는 이메일(verify 시 대조) |
| `createdAt` | timestamp | 생성 시각 |
| `expiresAt` | timestamp | 만료. verify=24h, reset=1h `[CONFIRM]` §5.4 |
| `consumedAt` | timestamp \| null | 사용(소비) 시각. 1회성 보장 — 소비 시 문서 삭제가 기본, 감사 필요 시 필드로 마킹 |

- **토큰 표현**: 클라/이메일에 전달되는 값 = `{tokenId}.{secret}` 형태(selector.verifier 패턴) `[CONFIRM]`. tokenId로 문서 O(1) 조회 후 `secret`을 `secretHash`와 상수시간 비교. 이는 Firestore 전체 스캔 없이 안전 조회 + 타이밍 공격 방지.
- **1회성**: confirm 성공 시 문서 **삭제**(또는 `consumedAt` 마킹 후 재사용 거부). 실패(만료·불일치)도 시도 횟수 카운트 검토(§12 레이트리밋).

### 4.3 토큰 해싱 방식 `[CONFIRM]`

- `secret`(및 `code`)은 **sha256**으로 해시 저장 권장. 근거: 비밀번호(bcrypt)와 달리 토큰은 (a) 고엔트로피(122비트 UUID/난수)라 브루트포스 불가 → 느린 해시 불요, (b) confirm 시 다수 대조 없이 tokenId로 단건 조회하므로 성능 부담 없음, (c) 표준 `crypto.createHash`로 외부 의존 0.
- 6자리 코드는 저엔트로피(10^6)라 **시도 횟수 제한 필수**(§12). 코드 경로를 열 경우 반드시 레이트리밋/락아웃과 함께.
- bcrypt 재사용도 가능하나(이미 의존 존재), 토큰엔 sha256이 적합.

### 4.4 Firestore 보안 규칙 영향 — **없음**

- `users`와 하위 서브컬렉션은 웹에서 **전면 deny**가 이미 원칙(`firebase-contract.md:52,182`). 서버는 Admin SDK(ADC)로 규칙을 우회하므로(`web/functions/src/firebase.ts`), 서브컬렉션 추가에 규칙 변경 불요. **웹 다운로드 페이지·js 팀 무영향**(계약 §5.1 `users/{uid}` deny가 `users/{id}/tokens/**`까지 자연 포함되도록 규칙이 `match /users/{uid}` 하위 전체 deny인지만 §11 CONSOLE 점검).
- **firebase-contract.md 갱신 필요**(§2.1 users 스키마에 email·emailVerified·tokens 서브컬렉션 주석 추가). 단 웹 접근 전면 deny는 불변이므로 계약의 소비자(js) 동작엔 영향 없음 — 스키마 문서화만.

### 4.5 이메일 유일성 `[USER-DECISION-REQUIRED]`

- 한 이메일을 여러 계정이 공유할 수 있는가? 두 선택:
  - (a) **유일성 강제**: 이메일당 1계정. 재설정 시 email→계정 매핑이 1:1로 단순. 단 유일성 인덱스(별도 `emailIndex` 컬렉션 또는 쿼리 후 검사)가 필요.
  - (b) **비강제**: 여러 계정이 같은 이메일 가능. 재설정 요청 시 email로 조회하면 여러 계정 → id 병기 필요.
- **기본안 (a) 유일성 강제** `[CONFIRM]`(단 사용자 확정 필요). 근거: 재설정 UX가 단순("이 이메일의 계정 비번 재설정"), 키오스크 운영 계정 수가 적어 인덱스 부담 미미. 구현: 생성/email 변경 시 `where email == x` 쿼리로 중복 검사(409). email이 검증 안 된 상태에선 느슨하게, verify 시점에 최종 강제 검토.

---

## 5. 이메일 인증 플로우 (상세)

### 5.1 진입점 — 계정 생성 시 이메일 수집

- 파워 계정이 다른 계정을 생성할 때 email을 함께 입력(`POST /accounts`에 `email` 추가, §8.1). email은 **선택 아님/필수 여부** 결정:
  - `[CONFIRM]` **email 필수(신규 계정)**. 근거: item1a의 목적이 "재설정 가능한 계정"이므로 신규 계정은 이메일을 가져야 의미가 있다. 단 email 없는 계정도 생성은 허용(하위호환·특수 운영)하되 **경고 표시**하는 절충도 가능 → 기본안: **필수, 단 서버는 null 허용(클라 UI가 강제)**. 최종 강제 수위는 §5.5 로그인 정책과 연동.
- 자기 계정 email 자발 등록: 로그인 사용자가 자기 email을 추가/변경 → `PATCH /accounts/{id}/email`(§8.3, §7).

### 5.2 인증 메일 발송

- 계정 생성(또는 email 등록/변경) 성공 직후 서버가 verify 토큰 생성 + `EmailSender.sendVerification`.
- **발송 실패는 계정 생성을 롤백하지 않는다** `[CONFIRM]`(가용성 우선, lazy 마이그레이션 철학과 정합 — `services/accounts.ts:52-58` 참고). 대신 "인증 메일 재발송" 경로 제공(§8.2 request 재호출). 발송 실패는 로그만.
- 메일 내용: 인증 링크(`{hostingBaseUrl}/verify?token={tokenId}.{secret}`) + 6자리 코드. 링크는 웹에서 confirm 호출하거나, 키오스크 사용자는 코드를 앱에 입력.

### 5.3 인증 방식 — 링크 + 코드 병행 `[CONFIRM]`

| 방식 | 흐름 | 용도 |
|------|------|------|
| **링크** | 이메일의 URL 클릭 → 웹 페이지가 `POST /auth/verify-email/confirm {token}` 호출 | 일반 이메일 클라이언트 |
| **코드** | 앱/웹에 6자리 코드 수기 입력 → `POST .../confirm {id, code}` | 키오스크 PC(브라우저 없이 앱에서 직접 인증) |

- 두 방식은 **같은 토큰 문서의 두 verifier**(secret=링크용, code=수기용). 어느 쪽이든 성공 시 동일하게 소비.
- **웹 verify 페이지**: `{hostingBaseUrl}/verify`는 신규 정적 페이지 필요 → **js 팀 협업 항목** `[USER-DECISION-REQUIRED]`(§9.5). 단 코드 방식만 우선 구현하면 웹 페이지 없이도 앱 내 인증 완결 가능(키오스크 시나리오엔 충분). **기본안: 코드 방식 우선 필수, 링크는 웹 페이지 준비되면 활성**.

### 5.4 토큰 만료 `[CONFIRM]`

- verify 토큰: **24시간**. reset 토큰: **1시간**. 근거: verify는 급하지 않고(계정은 이미 사용 가능), reset은 탈취 위험이 커 짧게. 만료 시 재요청.
- Firestore 네이티브 TTL 정책을 `expiresAt`에 걸어 만료 문서 자동 청소 검토(`[CONSOLE]`, resultSessions와 동일 방식). 서버는 confirm 시 항상 `expiresAt > now`를 코드로도 재확인(TTL은 지연 삭제이므로).

### 5.5 unverified 계정 로그인 정책 — **허용** `[CONFIRM]`

- **결정: verify 여부와 무관하게 로그인 허용**. verify는 "비밀번호 찾기 자격"을 여는 것이지 로그인 게이트가 아니다.
- 근거: (a) 키오스크 운영 계정은 생성 즉시 사용돼야 함, (b) 이메일 미도달·발송 실패 시 계정이 잠기면 운영 마비, (c) 현행 로그인 계약(`services/accounts.ts:42-63`)을 바꾸지 않아 리스크 최소.
- 로그인 응답에 `emailVerified`를 포함해 클라가 "이메일 인증을 완료하면 비밀번호 찾기를 쓸 수 있습니다" 안내 배너를 띄우는 정도의 넛지(nudge)만(§9.3).
- **대안(비범위)**: verify 필수 게이트는 향후 정책으로 열 수 있으나 item1a에선 제외.

---

## 6. 비밀번호 찾기(재설정) 플로우 (상세)

### 6.1 자격 — `emailVerified=true` 계정만

- 재설정 요청은 **이메일이 검증된 계정만** 실제 메일을 받는다. unverified·email 없는 계정은 요청을 받아도 **아무 메일도 보내지 않으나 응답은 동일**(202, 열거 방지 §12).
- 이는 계정 탈취 방어의 핵심: 공격자가 email을 나중에 붙여 재설정하는 경로를 차단(email 소유가 검증돼야만 그 email로 재설정 링크가 감).

### 6.2 요청 (`POST /auth/password-reset/request`)

- body: `{idOrEmail}`. 서버는 id 우선 조회, 없으면 email 조회.
- 계정 발견 + `emailVerified=true` + `email!=null`이면: reset 토큰 생성(1h) → `EmailSender.sendPasswordReset(email, link, code)`.
- **그 외 모든 경우(계정 없음/unverified/email 없음)**: 토큰·메일 없이 **동일한 202 Accepted**("입력하신 정보로 계정이 있으면 재설정 메일을 보냈습니다"). §12.

### 6.3 확인 (`POST /auth/password-reset/confirm`)

- body: `{token}` 또는 `{idOrEmail, code}` + `{newPassword}`.
- 서버: 토큰 문서 조회 → `purpose=="password_reset"` → `expiresAt>now` → secretHash/codeHash 상수시간 대조 → `consumedAt==null`.
- 성공: `newPassword`를 `validatePassword`로 검증 → bcrypt 해시(`hashPassword`, `domain/password.ts:19-21` 재사용) → `users/{id}.password` 업데이트 → 토큰 소비(삭제).
- 실패: 만료/불일치/소비됨 → 400 또는 401(열거 방지 위해 사유 최소화, §12).

### 6.4 세션 무효화 고려 `[USER-DECISION-REQUIRED]`

- 재설정 후 기존에 발급된 JWT를 무효화할 것인가? 현행 JWT는 **stateless(서버 저장 없음)**라 개별 폐기 불가(`domain/jwt.ts`). 선택:
  - (a) **무효화 안 함**(기본안): JWT는 만료(기본 8h)까지 유효. 재설정 시나리오상(비번 잊음=대개 로그아웃 상태) 실질 위험 낮음. 구현 0.
  - (b) `users`에 `tokenVersion`(또는 `passwordChangedAt`) 필드 추가 → JWT 클레임에 포함 → 미들웨어가 대조. 재설정 시 version 증가로 기존 토큰 무효화. 구현 비용 있음(JWT 발급·검증 변경).
- **기본안 (a)** `[CONFIRM]`. item1a 범위에선 stateless 유지. (b)는 보안 강화가 필요할 때 후속.

---

## 7. 기존 계정(email 없음) 처리

현행 시드(`devmcjo`) 및 기존 생성 계정은 email이 없다. 처리 방안(모두 병행):

1. **로그인 유지**: email 없어도 로그인은 정상(§5.5 정책과 동일). 재설정만 불가.
2. **파워/admin이 이메일 지정**: `PATCH /accounts/{id}/email`(§8.3)로 관리자가 대상 계정에 email을 넣을 수 있다. 이때도 verify 토큰이 그 email로 발송돼 소유 확인을 거쳐야 `emailVerified=true`가 된다(관리자가 넣었다고 자동 verified 아님 — 소유 확인 원칙 유지) `[CONFIRM]`.
3. **본인 자발 등록 유도**: 로그인 사용자가 자기 계정 email을 추가(같은 `PATCH /accounts/{id}/email`, 본인 경로). 계정 페이지에 "이메일 등록" UI(§9.3). email 없는 계정 로그인 시 넛지 배너.
4. **재설정 시 email 등록 유도**: "비밀번호 찾기"에서 id 입력했으나 email 없는 계정이면 — 열거 방지상 동일 202를 반환하므로 UI는 별도 분기 불가. 대신 로그인 화면에 "이메일이 없는 계정은 관리자에게 문의" 안내 문구 상시 표시로 대체 `[CONFIRM]`.

- 권한 규칙(재사용): `PATCH /accounts/{id}/email`은 **본인 또는 파워(위계)** — 비번 변경(`changePassword`, `services/accounts.ts:114-128`)의 `isSelf || canManage` 규칙 그대로 차용.

---

## 8. 엔드포인트 확장 (요청/응답 계약)

### 8.0 공통 규약(재사용)

- 에러 표준형 `{error:{code,message}}`, 상태코드 매핑은 현행 그대로(`web/functions/src/http/errors.ts`). 신규 열거 방지 응답은 **202 Accepted**(성공/실패 무구분) 신설.
- API 키 게이트(`requireApiKey`, `http/auth.ts:36-45`)를 재설정/verify confirm·request에 적용(로그인 전 상태이므로 Bearer 불가). 계정 email 변경은 Bearer.
- 입력 검증은 `domain/validation.ts`에 순수 함수 추가(§8.5).

### 8.1 `POST /accounts` — email 추가(기존 엔드포인트 확장)

| 항목 | 값 |
|------|-----|
| 인증 | Bearer(파워) — 현행 유지(`routes/accounts.ts:30-46`) |
| 요청 | `{id, password, role, email?}` — `email` 추가 |
| 처리 | email 검증(`validateEmail`) → 유일성 검사(§4.5) → `emailVerified=false`로 생성 → verify 토큰 + 메일 발송(§5.2) |
| 응답 | 201 `user{id, role, createdAt, email, emailVerified}` |
| 예외 | 400(email 형식), 409(id 중복 또는 email 중복 §4.5), 403(역할 게이트, 현행) |

### 8.2 이메일 인증

| 엔드포인트 | 인증 | 요청 | 응답 |
|-----------|------|------|------|
| `POST /auth/verify-email/request` | API키 | `{idOrEmail}` | 202(열거 방지 — 계정 있으면 재발송). 로그인 상태 본인 재발송은 Bearer 변형도 가능 `[CONFIRM]` |
| `POST /auth/verify-email/confirm` | API키 | `{token}` 또는 `{id, code}` | 200 `{verified:true}` \| 400/401(만료·불일치) |

- request는 "인증 메일 재발송" 겸용. 이미 verified면 no-op(202 동일).

### 8.3 계정 이메일 등록/변경

| 엔드포인트 | 인증 | 요청 | 응답 |
|-----------|------|------|------|
| `PATCH /accounts/{id}/email` | Bearer(본인/파워, 위계) | `{email}` | 204. email 검증·유일성 후 저장(**emailVerified=false로 리셋**) + verify 메일 발송 |

- email 변경 시 반드시 `emailVerified=false`로 되돌리고 새 email 소유 재확인(핵심 보안).

### 8.4 비밀번호 재설정

| 엔드포인트 | 인증 | 요청 | 응답 |
|-----------|------|------|------|
| `POST /auth/password-reset/request` | API키 | `{idOrEmail}` | **202**(항상 동일, 열거 방지 §12) |
| `POST /auth/password-reset/confirm` | API키 | `{token, newPassword}` 또는 `{idOrEmail, code, newPassword}` | 200 `{reset:true}` \| 400/401 |

### 8.5 응답/DTO 노출 규칙(엄수)

- `UserResponse`에 `email`(string|null)·`emailVerified`(bool) **추가**(`services/dto.ts:44-48`). `toResponse`(`services/accounts.ts:23-29`)가 매핑.
- **절대 응답 미포함**: `password`(해시), 토큰 문서 전체(`secretHash`/`code`/`secret`), 토큰 평문. 토큰 평문은 오직 이메일 본문에만 실린다.
- request 계열은 존재 여부를 응답 body/상태코드로 노출하지 않는다(§12).

### 8.6 신규 순수 검증 함수(`domain/validation.ts` 추가)

```
validateEmail(value): ValidationResult<string>
  - RFC 5322 간이 정규식 + 길이(≤254). 소문자 정규화 반환. 형식 위반 시 fail.
validateVerificationCode(value): ValidationResult<string>
  - 정확히 6자리 숫자.
```

- 기존 `validateAccountId`/`validatePassword`(`domain/validation.ts:19-33`) 옆에 추가. jest 테스트(`__tests__/validation.test.ts` 패턴) 동반(§12 완료기준).

### 8.7 라우터/서비스/도메인 파일 배치

| 파일 | 신규/수정 | 책임 |
|------|-----------|------|
| `web/functions/src/domain/tokens.ts` | 신규 `[CODE]` | 토큰 생성(`{tokenId}.{secret}` 조립)·해시(sha256)·상수시간 비교·만료 판정. **순수**(Firestore 무관), jest 테스트 |
| `web/functions/src/domain/validation.ts` | 수정 `[CODE]` | `validateEmail`·`validateVerificationCode` 추가 |
| `web/functions/src/services/email.ts` | 신규 `[CODE]` | `EmailSender` 인터페이스 + `LogEmailSender`(dev) + `SendGridEmailSender`(prod, 지연 로드) + 메일 본문 템플릿(§10) |
| `web/functions/src/services/tokens.ts` | 신규 `[CODE]` | 토큰 서브컬렉션 CRUD(발급·조회·소비), Firestore 접근. `domain/tokens.ts` 사용 |
| `web/functions/src/services/accounts.ts` | 수정 `[CODE]` | `createAccount`에 email 처리, `setEmail`·`verifyEmail`·`requestPasswordReset`·`confirmPasswordReset` 추가. `toResponse`에 email 필드 |
| `web/functions/src/routes/auth.ts` | 수정 `[CODE]` | `/verify-email/{request,confirm}`·`/password-reset/{request,confirm}` 추가 |
| `web/functions/src/routes/accounts.ts` | 수정 `[CODE]` | `POST /accounts`에 email, `PATCH /accounts/{id}/email` 추가 |
| `web/functions/src/config.ts` | 수정 `[CODE]` | 이메일 공급자 설정(`EMAIL_PROVIDER`, `EMAIL_FROM`, `SENDGRID_API_KEY` 시크릿) 로드 |

---

## 9. 클라이언트 (WPF) 변경

### 9.1 `IAccountService` 시그니처 확장

현행(`src/MCPhoto.Core/Accounts/IAccountService.cs:18`):

```csharp
Task<User> CreateAsync(string id, string password, UserRole role, UserRole actingRole, CancellationToken ct = default);
```

item1a:

```csharp
Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default);
// 신규:
Task SetEmailAsync(string id, string email, CancellationToken ct = default);
Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default);
Task ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken ct = default);   // 링크 경로
Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default); // 코드 경로
Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default);
Task ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default);          // 코드 경로(키오스크)
Task ConfirmEmailVerificationByTokenAsync(string token, CancellationToken ct = default);              // 링크 경로(웹에서 주로 사용)
```

- **인터페이스 변경 파급**: `HttpAccountService`(구현), 레거시 `AccountService`(Firebase, OFF 경로 — email 미지원이면 `NotSupportedException` 또는 no-op), 호출부(`AccountViewModel`). CreateAsync 시그니처 변경은 컴파일 파급이 있으므로 **오버로드 추가 vs 파라미터 추가**를 정한다 `[CONFIRM]`: 파라미터 추가(호출부 소수, 명시적) 권장. 레거시 `AccountService`는 온라인 전용 방향(§ 마이그레이션)상 곧 은퇴하므로 email 파라미터를 받되 무시하는 최소 대응.
- `User` 모델(`src/MCPhoto.Core/Models/User.cs`)에 `Email`(string?)·`EmailVerified`(bool) 추가.

### 9.2 `HttpAccountService` 구현(신규 메서드)

- 기존 패턴(`SendJsonAsync`/`SendNoContentAsync`, `MapToDomainException`, `bearer` 플래그)을 그대로 사용(`HttpBackendClient.cs:62-99,166-173`).
- request 계열은 202를 성공으로 처리(현행 `EnsureSuccessAsync`는 2xx 성공 — 202 자동 통과, `HttpBackendClient.cs:137`).
- 신규 DTO(`AccountDtos.cs`에 추가): `SetEmailRequest{Email}`, `PasswordResetRequest{IdOrEmail}`, `PasswordResetConfirmRequest{Token?, IdOrEmail?, Code?, NewPassword}`, `VerifyEmailRequest{IdOrEmail}`, `VerifyEmailConfirmRequest{Token?, Id?, Code?}`. `UserResponse`에 `Email`/`EmailVerified` 추가.

### 9.3 계정 페이지(`AccountViewModel`) — email 입력·인증 UX

- **계정 생성 모드**(`AccountMode.AccountCreate`, `AccountViewModel.cs:132-169`): `NewAccountEmail`(ObservableProperty) 추가. `CreateAsync` 호출에 email 전달. 이메일 형식 클라 사전검증(서버가 최종 검증).
- **비번 변경 모드**(`PasswordChange`)에 "이메일 등록/인증" 섹션 추가 `[CONFIRM]`: 본인 email 표시·등록(`SetEmailAsync`) + unverified면 "인증 코드 입력" 필드 + "인증 메일 재발송" 버튼(`RequestEmailVerificationAsync`/`ConfirmEmailVerificationAsync`). 또는 별도 `AccountMode.Email` 신설 `[USER-DECISION-REQUIRED]`(모드 폭증 vs 섹션 추가 — 기본안: PasswordChange 모드 하단 섹션).
- 넛지: 로그인 사용자 email 없음/unverified 시 배너("이메일을 인증하면 비밀번호를 잊어도 재설정할 수 있어요").

### 9.4 로그인 화면(`LoginGuestView`) — "비밀번호 찾기" + 신규 재설정 화면

- `LoginGuestView.xaml:38-39`(취소 버튼 옆)에 **"비밀번호 찾기"** 링크 버튼 추가(`Button.Ghost` 스타일 재사용, `LoginGuestView.xaml:38`).
- 신규 화면 **`AppState.PasswordReset`** + `PasswordResetViewModel` + `PasswordResetView`:
  - 1단계: idOrEmail 입력 → `RequestPasswordResetAsync` → "메일을 확인하세요"(항상 동일 안내, 열거 방지).
  - 2단계: 코드 6자리 + 새 비밀번호(PasswordBox 2회 확인, code-behind 전달 — `LoginGuestView.xaml.cs:24-28` 패턴 재사용) → `ConfirmPasswordResetByCodeAsync`.
  - 성공 시 로그인 화면 복귀(`ReturnFromOverlay`, `LoginGuestViewModel.cs:65` 패턴).
- 네비게이션: `AppState`에 `PasswordReset` 추가(`src/MCPhoto.Core/Navigation/AppState.cs`), 셸의 `CreateViewModel`에 케이스 추가(`AppShellViewModel.cs:188-202`), DI Transient 등록(`ServiceRegistration.cs:163-167` 패턴). 오버레이 진입(`NavigateToOverlayAsync`, `AppShellViewModel.cs:337` 패턴) 재사용.

### 9.5 웹 verify 페이지(링크 경로) `[USER-DECISION-REQUIRED]`

- 이메일 링크 클릭 대상 `{hostingBaseUrl}/verify` 정적 페이지는 **js 팀 산출물**. item1a 코드 범위에선 **코드 방식(앱 내 수기 입력)을 우선 필수**로 하고, 링크 방식은 서버 confirm 엔드포인트(`token` 경로)만 준비해두고 웹 페이지는 후속.
- 결정 필요: 링크 방식을 이번에 포함할지(js 협업), 코드 방식만으로 출시할지. **기본안: 코드 방식만 item1a, 링크 페이지는 별도 js 티켓**.

### 9.6 파일 인코딩·관례(엄수)

- 기존 `.cs`는 **UTF-8 no BOM**, TS는 웹 관례(ESM, 2-space) — 실측(`wpf-backend-proxy-migration-design.md:388`). 신규/수정 파일 동일 유지. file-scoped namespace·nullable enable·XML doc 한글 주석(C#), 한글 JSDoc(TS) 관례 따름.

---

## 10. 이메일 발송 추상화

### 10.1 인터페이스(`services/email.ts`) `[CODE]`

```typescript
export interface EmailSender {
  sendVerification(to: string, opts: { link: string; code: string; accountId: string }): Promise<void>;
  sendPasswordReset(to: string, opts: { link: string; code: string; accountId: string }): Promise<void>;
}
```

- 발송 실패는 예외로 던지되, **호출부(계정 생성/재설정 request)가 삼켜 로그만**(§5.2 — 가용성·열거방지). request 계열은 발송 실패해도 202.

### 10.2 개발용 구현 — `LogEmailSender` `[CODE]`

- 실제 발송 없이 `console.info`로 수신자·링크·코드를 로그(**Emulator/개발 전용**). 외부 의존 0. jest 테스트에서 mock 대체 용이.
- `config.EMAIL_PROVIDER`가 `"log"`(기본)면 이 구현 선택.

### 10.3 프로덕션 구현 — `SendGridEmailSender` `[CONFIRM]` `[CODE]`(자격은 `[CONSOLE]`)

- 공급자 선정 기본안: **SendGrid**. 근거: (a) Node SDK(`@sendgrid/mail`) 단순, (b) 무료 한도(일 100통) 키오스크 규모 충분, (c) Firebase 생태계에서 흔히 조합, (d) 발신 도메인 인증(SPF/DKIM) 표준 지원.
- **대안**: SMTP(nodemailer + 임의 SMTP) — 공급자 자유롭지만 자격/릴레이 설정 복잡. Firebase "Trigger Email" Extension — Firestore 컬렉션에 문서 쓰면 발송, 단 별도 Extension·SMTP 백엔드 필요. → 기본안 SendGrid, 최종 공급자는 사용자 판단 `[USER-DECISION-REQUIRED]`(자격 등록이 사용자 몫이므로).
- API 키는 `config`가 Secret Manager(`SENDGRID_API_KEY`)에서 로드. **코드/리포 하드코딩 금지**(`config.ts` 관례, §8.2). 지연 로드(`import` 동적)로 dev에서 패키지 미설치여도 무방하게.
- 발신자(`EMAIL_FROM`)·발신 도메인은 콘솔 등록값(`[CONSOLE]` §11).

### 10.4 메일 본문(간이 템플릿) `[CODE]`

- 한국어, 텍스트+간단 HTML. 인증: "MCPhoto 계정 이메일 인증 — 아래 코드를 입력하거나 링크를 누르세요. 코드: {code} / 링크: {link} (24시간 유효)". 재설정: 동일 형식(1시간 유효, "본인이 요청하지 않았다면 무시하세요").

---

## 11. `[CODE]` / `[CONSOLE]` 분리 + USER-ACTIONS §B1 추가 목록

### 11.1 `[CODE]` (내가 구현)

- 서버: `domain/tokens.ts`(신규)·`validation.ts`(email·code 검증 추가)·`services/{email,tokens}.ts`(신규)·`services/accounts.ts`(email·verify·reset)·`routes/{auth,accounts}.ts`(엔드포인트)·`config.ts`(이메일 설정 로드)·`services/dto.ts`(UserDoc·UserResponse email 필드). jest 테스트(tokens·validation·accounts email 경로) + Emulator 통합(LogEmailSender로 발송 검증).
- 클라: `IAccountService`(시그니처)·`User` 모델(Email·EmailVerified)·`HttpAccountService`(신규 메서드)·`AccountDtos.cs`(신규 DTO)·`AccountViewModel`(email 입력·인증 섹션)·`LoginGuestView`(찾기 링크)·`PasswordResetViewModel`/`PasswordResetView`(신규)·`AppState.PasswordReset`·`AppShellViewModel`(네비)·`ServiceRegistration`(DI 등록).
- 문서: `firebase-contract.md §2.1` users 스키마 갱신(email·emailVerified·tokens 서브컬렉션 — 웹 deny 불변 명시), `docs/analysis/60-auth-accounts-and-roles.md` 갱신(인증·재설정 플로우).

### 11.2 `[CONSOLE]` (사용자 수동 — USER-ACTIONS §B1에 추가)

> 아래를 `docs/USER-ACTIONS.md §B1`(현재 자리표시자, `USER-ACTIONS.md:73-74`)에 채운다:

- **B1-1. 이메일 공급자 계정·자격** `[ ]`: SendGrid(권장) 계정 생성 → API 키 발급 → `firebase functions:secrets:set SENDGRID_API_KEY`. (대안 SMTP 채택 시 호스트/포트/계정.)
- **B1-2. 발신 도메인·발신자 등록** `[ ]`: SendGrid Sender Authentication(도메인 SPF/DKIM 또는 Single Sender). 발신 주소를 함수 env `EMAIL_FROM`(예: `no-reply@도메인`)로 설정.
- **B1-3. 이메일 공급자 선택 설정** `[ ]`: 함수 env `EMAIL_PROVIDER=sendgrid`(미설정/`log`면 개발용 로그 sender — 실제 메일 미발송). 프로덕션 배포 전 반드시 `sendgrid`로.
- **B1-4. 링크 방식 채택 시 웹 verify 페이지** `[ ]` `[USER-DECISION-REQUIRED]`: `{hostingBaseUrl}/verify` 정적 페이지(js 팀) — 코드 방식만이면 불요.
- **B1-5. (선택) 토큰 서브컬렉션 TTL 정책** `[ ]`: `users/{id}/tokens`에 `expiresAt` Firestore 네이티브 TTL 걸어 만료 토큰 자동 청소(resultSessions 방식).
- **B1-6. Firestore 규칙 점검** `[ ]`: `match /users/{uid}` 하위 전체(서브컬렉션 포함) deny 유지 확인(웹 접근 없음). Admin 서버만 접근.

---

## 12. 보안 (엄수)

| 위협 | 방어 | 근거/구현 |
|------|------|-----------|
| 계정/이메일 열거 | request 계열(reset/verify)은 존재·상태 무관 **동일 202**. confirm 실패 사유 최소화 | §6.2, §8.4 |
| 토큰 추측 | secret=122비트 UUID/난수, **해시 저장**(sha256), 평문 미저장·미응답 | §4.2·§4.3 |
| 토큰 재사용 | 1회성(소비 시 삭제/consumedAt), confirm 시 `consumedAt==null` 확인 | §4.2 |
| 토큰 만료 | verify 24h·reset 1h, confirm 시 `expiresAt>now` 코드 재확인(TTL 지연 대비) | §5.4 |
| 타이밍 공격 | secret/code 대조는 **상수시간 비교**(`crypto.timingSafeEqual`) | `domain/tokens.ts` |
| 6자리 코드 브루트포스 | 저엔트로피(10^6) → **시도 횟수 제한 필수**(토큰당 5회 초과 시 무효화) `[CONFIRM]` | §12 레이트리밋 |
| 재설정 브루트포스/스팸 | request 레이트리밋(IP·계정별) `[USER-DECISION-REQUIRED]` 정책만 | 현행 시도제한 없음(`analysis/60:213`). 서버 인메모리 카운터 or 외부(향후) |
| email 변경 후 탈취 | email 변경 시 `emailVerified=false` 강제 + 새 소유 재확인 | §8.3 |
| 관리자 지정 email 자동신뢰 | 관리자가 넣은 email도 소유 확인(verify) 거쳐야 verified | §7-2 |
| 시크릿 커밋 | SendGrid 키·JWT는 Secret Manager only, `.env`는 gitignore(개발) | `config.ts`, §8.2 |
| 세션 무효화 | 기본 stateless 유지(§6.4). 강화 시 tokenVersion | §6.4 |

- **레이트리밋** `[USER-DECISION-REQUIRED]`: item1a에선 **코드 방식 토큰당 시도 5회 제한(코드 무효화)** 을 최소 구현으로 포함 `[CONFIRM]`. IP/계정별 요청 레이트리밋은 Cloud Functions 특성상 인메모리가 인스턴스 간 공유 안 됨 → 완전한 레이트리밋은 별도 저장소(Firestore 카운터) 필요 → 정책만 문서화, 구현은 후속 판단.

---

## 13. 실측 파일 목록 (근거)

**서버(P1 완료)**: `web/functions/src/{index,app,config,firebase}.ts`, `routes/{auth,accounts,frames}.ts`, `services/{accounts,dto}.ts`, `domain/{password,jwt,validation,roles}.ts`, `http/{auth,errors,async}.ts`, `__tests__/validation.test.ts`.
**클라(P3 완료)**: `src/MCPhoto.Http/{HttpAccountService,HttpBackendClient,BackendException}.cs`, `Dto/AccountDtos.cs`, `Session/IBackendSession.cs`, `src/MCPhoto.Core/Accounts/IAccountService.cs`, `src/MCPhoto.Core/Models/{User,UserRole}.cs`, `src/MCPhoto.Core/Navigation/AppState.cs`, `src/MCPhoto.App/ViewModels/{AccountViewModel,LoginGuestViewModel,UserMgmtViewModel}.cs`, `Views/{LoginGuestView.xaml,LoginGuestView.xaml.cs}`, `ServiceRegistration.cs`, `src/MCPhoto.Core/Settings/AppSettings.cs`.
**계약/문서**: `docs/design/{firebase-contract,wpf-backend-proxy-migration-design,backlog-post-backend-migration}.md`, `docs/analysis/60-auth-accounts-and-roles.md`, `docs/USER-ACTIONS.md`.

---

## 14. 미해결 결정 사항 집계

### 14.1 `[CONFIRM]` (기본안 확정 — 리뷰 시 조정 가능)

1. unverified 계정 **로그인 허용**(게이트 아님) — §5.5
2. 인증 **링크+코드 병행**, 단 코드 방식 우선 필수 — §5.3
3. 토큰 저장 = **서브컬렉션 + sha256 해시 + selector.verifier** — §4.2·§4.3
4. 만료: verify **24h** / reset **1h** — §5.4
5. 이메일 유일성 **강제**(기본안, 단 §14.2-1 확정 필요) — §4.5
6. 신규 계정 email **필수(클라 UI 강제, 서버 null 허용)** — §5.1
7. 이메일 공급자 **SendGrid**(기본안) — §10.3
8. 세션 무효화 **안 함**(stateless 유지) — §6.4
9. `CreateAsync` **파라미터 추가**(오버로드 아님) — §9.1
10. email 인증 UI = PasswordChange 모드 하단 섹션(신규 모드 아님) — §9.3
11. 코드 시도 **토큰당 5회 제한** 최소 구현 — §12

### 14.2 `[USER-DECISION-REQUIRED]` (순수 제품/운영 판단)

1. 이메일 **유일성 강제 여부**(1이메일=1계정?) — §4.5
2. **링크 방식(웹 verify 페이지)** item1a 포함 여부(js 협업) vs 코드 방식만 — §5.3·§9.5
3. 이메일 **공급자 최종 선택**(SendGrid/SMTP/Firebase Extension) — 자격 등록이 사용자 몫 — §10.3
4. **요청 레이트리밋** 정책 수위(Firestore 카운터 구현 여부) — §12
5. email 인증 UI를 **별도 AccountMode.Email**로 분리할지 — §9.3

---

## 15. 권장 구현 순서 (WBS 단계화 전 개요)

> 확정 후 각 단계를 `WBS_BLUEPRINT.md` 형식으로 self-contained 상세화. 각 단계는 독립 검증 가능·단일 리스크·PASS/FAIL 명확해야 한다.

1. **S1. 서버 도메인 순수 로직**: `domain/tokens.ts`(생성·해시·비교·만료) + `validation.ts`(email·code) + jest. — 검증: `npm test`(jest) PASS, 외부 의존 0.
2. **S2. 이메일 추상화**: `services/email.ts`(인터페이스 + LogEmailSender) + `config` 이메일 설정 로드. — 검증: jest(mock) + tsc.
3. **S3. 스키마·토큰 서비스**: `dto.ts`(UserDoc/UserResponse email) + `services/tokens.ts`(서브컬렉션 CRUD). — 검증: Emulator 통합(토큰 발급·소비·만료).
4. **S4. 서버 계정 로직 확장**: `services/accounts.ts`(createAccount email, setEmail, verifyEmail, request/confirm reset) + `routes/{auth,accounts}.ts`. — 검증: Emulator E2E(생성→코드로 verify→reset request→confirm→새 비번 로그인), 열거 방지(없는 email도 202) 확인.
5. **S5. 클라 계약·HTTP**: `IAccountService`·`User`·`AccountDtos`·`HttpAccountService`. — 검증: `dotnet build` 0경고, 기존 402 테스트 유지 + 신규 단위 테스트.
6. **S6. 클라 UI — 계정 생성 email**: `AccountViewModel`·`AccountView`(생성 모드 email 필드). — 검증: 빌드 + (관측)생성 시 email 전달. non-goal: 인증 완결. trigger: email 형식 오류 표시.
7. **S7. 클라 UI — 비밀번호 찾기 화면**: `AppState.PasswordReset`·`PasswordResetViewModel`/`View`·로그인 링크·DI·네비. — 검증: 빌드 + (관측)찾기→코드+새비번→복귀. non-goal: 실제 메일(dev는 로그). trigger: 코드 불일치 안내.
8. **S8. 클라 UI — 이메일 등록/인증 섹션**: PasswordChange 모드 하단(본인 email 등록·코드 인증·재발송). — 검증: 빌드 + (관측)등록→코드 verify→emailVerified 반영.
9. **S9. 문서 갱신**: `firebase-contract.md`·`analysis/60`·`USER-ACTIONS §B1`. — 검증: 링크·스키마 정합.

- **선행 관계**: S1→S3→S4(서버) 순차. S5는 S4 계약 확정 후. S6~S8은 S5 후 병렬 가능. S2는 S4 전. S9는 마지막.
- **프로덕션 이메일(SendGrid) 실발송**은 코드 완료 후 `[CONSOLE]` B1 자격 등록에 의존 — 코드는 LogEmailSender로 완결 검증하고, 실발송은 배포 시 공급자 설정으로 활성.

---

## 관련 문서

- `docs/design/wpf-backend-proxy-migration-design.md` — 방향 B 아키텍처(인증 모델·JWT·에러 매핑·DI flag). 본 문서는 그 위 가산 설계.
- `docs/design/firebase-contract.md` — WPF↔웹 계약. §2.1 users 스키마 갱신 대상(웹 deny 불변).
- `docs/analysis/60-auth-accounts-and-roles.md` — 역할 위계·권한 매트릭스·현행 인증. 인증·재설정 플로우 추가 대상.
- `docs/USER-ACTIONS.md` — §B1 이메일 공급자 콘솔 작업(본 설계 §11.2로 채움).
- `docs/design/backlog-post-backend-migration.md` — item1a 앵커(§1 계정 관련).
