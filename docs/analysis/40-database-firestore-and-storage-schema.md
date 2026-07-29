# 40 · 데이터베이스(Firestore) / Storage 스키마

| 항목 | 내용 |
|------|------|
| 문서 | Firestore 컬렉션 스키마 + Cloud Storage 경로 규약 + 보안 규칙 + TTL/만료 계약 |
| 범위 | `web/functions/src/services/dto.ts`(Firestore 문서 형태), `web/functions/src/services/{accounts,frames,uploads}.ts`(문서 조립), `src/MCPhoto.Core/Models/*`·`UploadContract.cs`(앱 도메인·경로 규약), `web/firestore.rules`, `web/storage.rules`, `web/OPS-ttl.md`. 연동 흐름은 [30 · 백엔드 API 연동](./30-backend-firebase-integration.md) |
| 최종 업데이트 | 2026-07-29 (it15·it16 — §2.1 `users.role` + **근거 경로를 삭제된 `MCPhoto.Firebase`에서 서버(TS)·Core 기준으로 전면 교체**) |
| 관련 소스 | `web/functions/src/services/dto.ts`, `web/functions/src/services/{accounts,frames,uploads,signing}.ts`, `web/functions/src/domain/{session,roles}.ts`, `src/MCPhoto.Core/Models/{User,FrameTemplate,ResultSession,Slot,UserRole}.cs`, `src/MCPhoto.Core/Upload/UploadContract.cs`, `firestore.rules`, `storage.rules`, `OPS-ttl.md` |
| 갱신 규칙 | `dto.ts`의 필드명·타입, 서버의 문서 조립 로직, Storage 경로 조립(`UploadContract`↔`domain/session.ts`), 보안 규칙(`*.rules`)이 바뀌면 해당 표/근거(`파일:라인`)를 갱신. 연동 절차 변경은 30번 문서와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`.
>
> ⚠️ **문서를 쓰는 주체는 백엔드(Cloud Functions)다.** it15에서 앱의 Firestore/Storage 직결(`MCPhoto.Firebase`, `[FirestoreData]` DTO)이 폐지되어, 저장 키의 진실원은 `web/functions/src/services/dto.ts`의 TypeScript 인터페이스다(camelCase 고정, 웹·앱 공통 계약이라 임의 변경 불가).

---

## 1. Firestore 컬렉션 개요

| 컬렉션 | 문서 ID | 서버 DTO | 앱 도메인 모델 | 웹 접근 | 근거 |
|--------|---------|----------|----------------|---------|------|
| `users` | 계정 id | `UserDoc` | `User` | 전면 차단 | `services/dto.ts:10-28`, `services/accounts.ts`, `User.cs` |
| `frameTemplates` | 프레임 id(UUID) | `FrameTemplateDoc` | `FrameTemplate` | 전면 차단 | `services/dto.ts:42-51`, `services/frames.ts:103-120` |
| `resultSessions` | `{yyyyMMdd_HHmmss}_{UUIDv4}` 토큰 | `ResultSessionDoc` | `ResultSession` | 단건 get만 | `services/dto.ts:54-61`, `services/uploads.ts:190-212` |
| `config/tempUserLimits` | 고정 문서 1개 | `TempUserLimitsDoc` | `TempUserLimits` | 전면 차단 | `services/dto.ts:34-39`, `services/config.ts` |

> 경로는 모두 `web/functions/src/` 기준이다. `config/tempUserLimits`는 it13에서 추가된 전역 한도 문서로, 부재 시 서버가 기본값(48h/30회)으로 폴백한다([30 §6](./30-backend-firebase-integration.md)).

---

## 2. 컬렉션별 스키마

### 2.1 `users` (문서 ID = 계정 id)

문서 ID는 계정 id를 사용한다. id는 Google email의 local-part에서 파생하며 충돌 시 `-2`/`-3` suffix가 붙는다
(`web/functions/src/domain/accountId.ts`).

> **it15 갱신**: 비밀번호 개념 폐지. 자격증명은 ① Google SSO(신원, 서버가 id_token 검증) +
> ② `pinHash`(설정·계정 관리 진입 게이트) 두 가지뿐이다. `password`·`emailVerified` 필드는 삭제됐다.
> 계정 문서 조작은 전부 백엔드 API(Cloud Functions)를 거치며, 앱은 Firestore에 직접 접근하지 않는다.

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 계정 ID(문서 ID와 동일) | `services/dto.ts` `UserDoc` |
| `role` | string | `"temp_user"` / `"user"` / **`"advanced_user"`**(it16) / `"manager"` / `"admin"`. 신규 SSO 계정은 `temp_user` | `domain/roles.ts` |
| `createdAt` | timestamp | 생성 시각(UTC). TempUser 시간 한도의 기준점 | `services/dto.ts` |
| `email` | string | Google 계정 이메일(소문자 정규화). SSO 신원의 근거 — 항상 존재 | `services/accounts.ts` `loginWithGoogleEmail` |
| `authMethod` | string | 인증 제공자. 현재 `"google"` 고정 | `services/accounts.ts` `createGoogleAccount` |
| `pinHash` | string? | 진입 PIN(4자리)의 bcrypt 해시. 미설정 시 필드 부재. **응답 절대 미포함** | `services/accounts.ts` `setOwnPin` |
| `qrUsedCount` | int? | TempUser QR 전송 성공 세션 누적 수. 미설정=0 | `services/uploads.ts` commit |

- **부트스트랩**: HTTP API로는 admin을 지정할 수 없다(`canSetRole`). 최초 admin은 마이그레이션 스크립트
  `web/functions/scripts/migrate-google-only-accounts.mjs`가 만든다.
- **역할 위계(it16 갱신)**: `temp_user`(0) < `user`(1) < **`advanced_user`(2)** < `manager`(3) < `admin`(4).
  랭크는 `domain/roles.ts`의 `MANAGE_RANK`(C# `ManageRank`와 1:1)로 명시하며 문자열이 저장 계약이므로 배치값 변경은 무해하다.
  **스키마 변경은 없다** — `users.role`에 `advanced_user` 값이 추가될 수 있다는 것뿐이며 필드 추가·인덱스 변경·마이그레이션이 **불필요**하다
  (기존 문서는 전부 기존 4값 중 하나이고 그 의미가 바뀌지 않는다. `user`가 프레임 저작 권한을 잃는 것은 **클라 정책 변경**이며 문서 값 변경이 아니다).
- **`role` 쓰기 게이트**: `PATCH /accounts/:id/role`은 `requirePower()` + `canSetRole(actor, current, target)` 매트릭스를 통과해야 한다
  (manager는 하위 3역할 대역 `temp_user`·`user`·`advanced_user` 안에서 자유 지정, manager·admin 지정은 admin 전용, admin 대상·admin 지정은 누구도 불가).
  `PUT /accounts/:id/pin`(타 계정 PIN 재설정)도 it16부터 **`requirePower()`** 를 요구한다(비power 403).
  전수 표는 [60 §1.4](./60-auth-accounts-and-roles.md#14-역할-지정변경-매트릭스).
- **미지원 `role` 값**: `parseRole`/`ParseRole`이 `user`로 폴백한다. it16 이후 `user`는 프레임 쓰기 권한이 없어 **fail-closed 방향**이다.
- **클라 응답(`UserResponse`)**: `{id, role, createdAt(ISO8601), email, authMethod, hasPin}`.
  `hasPin`은 `pinHash != null` 파생값이며 해시 원문은 어떤 응답에도 실리지 않는다
  (와이어 형식은 `docs/design/wpf-it15-google-only-auth-design.md` §9.1에서 동결).

### 2.2 `frameTemplates` (문서 ID = 프레임 id)

문서 ID = 프레임 id. **서버가 `randomUUID()`로 부여**하며 클라가 정하지 않는다(`services/frames.ts:103,120`).

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 프레임 ID(문서 ID와 동일) | `services/dto.ts:43` |
| `userId` | string \| null | 소유 계정 id. **공용 기본 프레임은 null** | `services/dto.ts:44`, `FrameTemplate.cs` |
| `isDefault` | bool | 공용 기본 프레임 여부(true면 게스트 노출) | `services/dto.ts:45` |
| `name` | string | 프레임 이름 | `services/dto.ts:46` |
| `imageUrl` | string | 프레임 이미지 다운로드 토큰 URL(Storage `frames/{owner}/…`) — **서명 발급 시 만들어진 URL** | `services/dto.ts:47`, `services/frames.ts:107,115` |
| `imageSize` | map `{ width:number, height:number }` | 등록 원본 픽셀 크기 | `services/dto.ts:48` |
| `slots` | array&lt;map&gt; | `{ index, x, y, width, height }` 1~6개. 프레임 픽셀 좌표계 | `services/dto.ts:49`, `domain/validation.ts`, `Slot.cs` |
| `createdAt` | timestamp | 생성 시각(UTC, `Timestamp.now()`) | `services/dto.ts:50`, `services/frames.ts:109` |

- **서버가 소유·공개 여부를 강제한다**: `POST /frames`는 클라가 보낸 값과 무관하게 `userId=null`·`isDefault=true`로 고정한다(`routes/frames.ts:71-80`). 즉 신규 서버 문서는 **공용 기본 프레임뿐**이다.
- **문서 먼저, 이미지 나중**: 서명 URL 발급 → 문서 `set` → 클라가 이미지 PUT 순서라, PUT이 실패하면 이미지 없는 문서가 남을 수 있다(수용된 트레이드오프 — 프레임은 웹 접근이 없고 재저장으로 덮어쓰기 가능, `services/frames.ts:85-89`).
- **Slot 도메인 파생값**: `Slot.AspectRatio = Width/Height`는 앱의 계산 프로퍼티로 **저장되지 않는다**(`Slot.cs`).
- 계정당 최대 10개(`userId`가 있을 때만) 서버 재검증 — 초과 시 409(`services/frames.ts:93-101`).
- **하이브리드(it8 A2)**: 일반 사용자 커스텀 프레임은 **로컬 파일 전용**(`ILocalFrameStore`, `.png` + `.slots`)이며 DB에 올라가지 않는다. 서버의 `userId != null` 경로는 레거시 문서 방어용으로만 남아 있다(`services/frames.ts:130-131`). 로컬 저장 스키마는 이 문서 범위 밖.

### 2.3 `resultSessions` (문서 ID = `{yyyyMMdd_HHmmss}_{UUIDv4}` 토큰)

문서 ID = 세션 토큰 = URL 토큰 = Storage 폴더명. 형식 **`{yyyyMMdd_HHmmss}_{UUIDv4}`**(`UploadContract.NewSessionId`, 로컬 시간 prefix). 날짜_시간 prefix로 Storage `results/` 하위 폴더가 시각순 정렬·검색된다(사용자 요청). **순차 ID는 여전히 금지** — 뒤의 완전한 UUIDv4로 열거 방어(추측 불가) 유지, prefix는 시각 노출(저민감) 트레이드오프.

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 세션 ID = 문서 ID = URL 토큰 = 폴더명. `{yyyyMMdd_HHmmss}_{UUIDv4}` | `services/dto.ts:55`, `UploadContract.cs:25` |
| `finalImageUrl` | string \| null | 최종 이미지 토큰 URL. **사진 전송(SendPhoto) off면 null** | `services/dto.ts:56`, `services/uploads.ts:195` |
| `timelapseUrl` | string \| null | 타임랩스 토큰 URL. 옵션 off·생성 실패·미포함 시 null | `services/dto.ts:57`, `services/uploads.ts:196` |
| `createdAt` | timestamp | **서버 시각**(commit 시점) | `services/uploads.ts:190-191` |
| `expiresAt` | timestamp | `createdAt + retentionHours`(서버 계산). **자동 삭제 기준** | `services/uploads.ts:192`, `domain/session.ts` |
| `downloadPageUrl` | string | 모바일 다운로드 페이지 URL(QR 인코딩 대상) | `services/dto.ts:60`, `UploadContract.cs:49-53` |

- **시각의 진실원은 서버다**: 앱도 `ResultSession`에 `CreatedAt`/`ExpiresAt`을 채우지만 문서에 기록되는 값은 서버가 commit 시점에 다시 만든다. 앱이 보낸 것은 `retentionHours`(시간 차이)로 환산되어 전달된다(`HttpFirebaseClient.cs:126-128`, `services/uploads.ts:190-200`).
- **URL 위조 방어**: commit은 `finalImageUrl`·`timelapseUrl`이 **서버 버킷 + `results/{sessionId}/` 경로**를 가리키는지 검증한다. prepare 없이 임의 URL을 심을 수 없다(`services/uploads.ts:129-152`).
- **중복 방어**: 같은 `sessionId`로 다시 commit하면 409다(TempUser는 트랜잭션 안에서 검사 — 카운트 이중집계 차단, `services/uploads.ts:206-211,240-244`).

#### 미디어 URL null 의미론 (it7 F2)

| 상황 | 판정 근거 | 근거 |
|------|-----------|------|
| **전송 옵션 꺼짐** | 미만료 문서 + URL이 null (의도적 제외, 실패·만료 아님) | `ResultSession.cs`, `services/dto.ts:56-57` |
| 만료 | `expiresAt < now` 또는 문서 부재 | `firebase-contract.md:83`, §5 |
| 로드 실패 | URL 있는데 fetch 실패 | `firebase-contract.md:84` |

- **최소 1개 불변식**: 미만료 `resultSessions` 문서는 `finalImageUrl`·`timelapseUrl` 중 최소 1개가 non-null. 둘 다 off면 `QrDeliveryPolicy.Normalize`가 `enableQrDelivery`를 off로 정규화해 문서 자체가 생성되지 않으며(`QrDeliveryPolicy.cs`, `UploadService.cs:38-39`), **서버도 commit에서 같은 불변식을 강제한다**(`services/uploads.ts:169-176`). `photoSent`/`timelapseSent` 같은 명시 플래그는 추가하지 않음(계약 `firebase-contract.md:84-85`).

---

## 3. 앱 도메인 ↔ 와이어 ↔ Firestore 매핑

앱은 Firestore를 직접 읽고 쓰지 않는다. 매핑은 **2단**이다: 앱 도메인 ↔ JSON 응답(`*Response`) ↔ Firestore 문서(`*Doc`).

| 앱 도메인(Core) | 와이어(JSON) | Firestore 문서 | 변환 지점 |
|-----------------|--------------|----------------|-----------|
| `User` | `UserResponse` `{id, role, createdAt(ISO8601), email, authMethod, hasPin}` | `UserDoc`(+`pinHash`·`qrUsedCount`) | 앱: `HttpAccountService.ToUser`(`:182-194`) / 서버: `services/accounts.ts` |
| `FrameTemplate`/`ImageSize`/`Slot` | `FrameResponse`(`createdAt`만 ISO8601) | `FrameTemplateDoc` | 앱: `HttpFrameRepository.ToTemplate`(`:169-184`) / 서버: `services/frames.ts` `toResponse` |
| `ResultSession` | `ResultSessionResponse` | `ResultSessionDoc` | 앱: `HttpFirebaseClient.CreateResultSessionAsync`(`:124-150`) / 서버: `services/uploads.ts` `commitUpload` |
| `TempUserLimits` | `{qrHours, qrCount}` | `TempUserLimitsDoc` | 앱: `HttpTempUserLimitsService`(`:28-40`) / 서버: `services/config.ts` |

- **응답에서 제외되는 필드**: `pinHash`는 어떤 응답에도 실리지 않고 `hasPin`(bool)로만 파생 노출된다. `qrUsedCount`도 원본 대신 `/accounts/me/qr-usage`의 게이트 판정 결과로만 나간다(`services/dto.ts:18-27,63-74`).
- **타임스탬프 표현**: Firestore `Timestamp` → 응답 ISO8601 문자열 → 앱 `DateTime`(UTC). 앱은 파싱 실패 시 `DateTime.UtcNow`로 방어 폴백한다(`HttpAccountService.cs:196-204`).
- 저장 키는 camelCase로 고정되며 이것이 **웹이 읽는 계약**이다(`services/dto.ts:2`).

---

## 4. Cloud Storage 경로 규약

### 4.1 경로 분리

| 용도 | 경로 | 파일명 규칙 | TTL 대상 | 근거 |
|------|------|-------------|----------|------|
| 결과물(사진) | `results/{sessionId}/final.{jpg\|png}` | 확장자 = `AppSettings.OutputFormat` | **O** | `UploadContract.cs:28-29`, `domain/session.ts` |
| 결과물(타임랩스) | `results/{sessionId}/timelapse.mp4` | 항상 mp4(H.264 무음) | **O** | `UploadContract.cs:32-33`, `domain/session.ts` |
| 프레임 이미지 | `frames/{owner}/{frameId}.png` | `owner = userId ?? "default"`, 항상 png | **X**(비대상) | `services/frames.ts:104-105` |

- 경로 규약은 **앱과 서버 양쪽에 이식**되어 있다(`UploadContract` ↔ `domain/session.ts`). 실제 Storage 경로는 서버가 prepare에서 결정하므로 서버가 진실원이고, 앱 쪽 값은 토큰 URL 재조립용이다([30 §5.3](./30-backend-firebase-integration.md)).
- `{sessionId}` = `{yyyyMMdd_HHmmss}_{UUIDv4}`(§2.3) → **results/ 하위 세션 폴더가 시각순 정렬**되어 Storage 콘솔에서 찾기 쉽다(사용자 요청). 파일명(`final`/`timelapse`)은 고정.
- `results/`만 TTL/만료 삭제 대상, `frames/`는 비대상(`firebase-contract.md:141-144`). 삭제는 인프라(Lifecycle·TTL)가 `results/` prefix로 수행하며, `sessionId`가 곧 폴더명이라 ID 형식과 무관하게 정합한다([50](./50-infra-gcp-lifecycle-and-ttl.md)).

### 4.2 다운로드 토큰 URL 형식

**서버가** 서명 URL 발급 시 객체 메타데이터 `firebaseStorageDownloadTokens`에 UUID 토큰을 심도록 서명에 포함시키고(`services/signing.ts:14-16,63,81-91`), 클라는 PUT 시 그 헤더(`x-goog-meta-firebaseStorageDownloadTokens`)를 그대로 보내야 메타가 설정된다. URL 형식은 앱·서버 공통이다(`UploadContract.cs:39-43`, `domain/session.ts`):

```
https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}
```

- `{urlEncodedPath}`: `Uri.EscapeDataString`으로 인코딩(슬래시 → `%2F`). 예: `results%2F{sid}%2Ffinal.jpg`.
- `{downloadToken}`: 파일별 UUID. 이 토큰이 있어야 URL로 read 가능(그 자체가 capability).
- 웹은 이 URL을 브라우저가 직접 GET(img/video/a href). **Storage read 규칙·방문자 인증 불필요**(토큰 URL은 규칙 우회, `storage.rules:5-7`).

### 4.3 다운로드 페이지 URL(QR 인코딩 대상)

`UploadContract.DownloadPageUrl`(`UploadContract.cs:49-53`): `{hostingBaseUrl 트레일링슬래시제거}/?s={sessionId}` (쿼리형 확정, `firebase-contract.md:106`). 예: `https://mcphoto-955fb.web.app/?s={yyyyMMdd_HHmmss}_{uuid}`. **앱이 조립해 commit에 넘기고 서버가 문서에 저장**한다.

---

## 5. 보안 규칙 요약

### 5.1 Firestore (`web/firestore.rules`)

| 경로 | get | list | write | 의도 | 근거 |
|------|-----|------|-------|------|------|
| `users/{uid}` | deny | deny | deny | PIN 해시·역할·이메일 보호(전체 계정 유출 방지) | `firestore.rules:18-20` |
| `frameTemplates/{fid}` | deny | deny | deny | 웹 접근 없음(WPF 전용) | `firestore.rules:23-25` |
| `resultSessions/{sid}` | **allow** | **deny** | deny | 토큰 단건 get만. list 열면 토큰 열거 가능 → 금지 | `firestore.rules:30-34` |
| `{document=**}` | deny | deny | deny | 그 외 기본 차단 | `firestore.rules:37-39` |

> 위 줄번호는 `web/firestore.rules` 현행 기준(it15 주석 갱신 반영).

- 핵심: `resultSessions`는 `allow get: if true` + `allow list: if false`를 **분리**한다. `allow read`(get+list 통합) 금지(주석 `firestore.rules:29-30`).
- 이 규칙은 **SDK 경로(웹)** 에만 적용된다. 문서를 쓰는 주체인 **백엔드(Cloud Functions)** 는 Admin(ADC)으로 동작해 규칙을 우회한다. it15 이전엔 그 주체가 WPF(서비스 계정)였으나 지금은 서버뿐이며, **앱은 Firestore에 아예 접근하지 않는다**(주석 `firestore.rules:9-11`은 구 표현, [30 §3.3](./30-backend-firebase-integration.md)).

### 5.2 Storage (`web/storage.rules`)

| 경로 | read | write | 의도 | 근거 |
|------|------|-------|------|------|
| `results/{sessionId}/{fileName}` | deny | deny | SDK 경로 열거·직접 접근 차단(토큰 URL은 규칙 우회하여 동작) | `storage.rules:16-19` |
| `frames/{userId}/{fileName}` | deny | deny | 웹 접근 없음(WPF 전용) | `storage.rules:22-24` |
| `{allPaths=**}` | deny | deny | 그 외 기본 차단 | `storage.rules:27-29` |

- `results/` read를 deny해도 웹 다운로드는 정상(토큰 URL 직접 GET). 닫아두는 편이 SDK 경로 열거를 막아 안전(주석 `storage.rules:5-8`).

### 5.3 웹 공개 접근 범위 (정리)

- 웹은 **읽기 전용 소비자**: `resultSessions` 단건 get + 문서에 담긴 토큰 URL로 파일 직접 GET만. `users`·`frameTemplates`·Storage SDK read는 전부 차단(`firebase-contract.md:22`).

---

## 6. 만료(expiresAt) / TTL

### 6.1 개념

- `expiresAt = createdAt + retentionHours`. 앱은 `UploadContract.ComputeExpiresAt`(`:56-57`)로 계산해 표시하고, **문서에 기록되는 값은 서버가 commit 시 계산**한다(`services/uploads.ts:190-192`). `retentionHours`는 `AppSettings.RetentionHours`(기본 24, 범위 1~72)이며 서버도 `validateRetentionHours`로 재검증한다(`routes/uploads.ts:55-56`).
- 웹은 `expiresAt < now` 또는 문서 부재 시 만료 안내를 표시하고, 별도 `expired` 플래그는 두지 않는다(`firebase-contract.md:83`).

### 6.2 TTL 대상 vs 비대상

| 대상 | TTL 삭제 | 근거 |
|------|----------|------|
| `results/` (Storage 파일) | **O** | `OPS-ttl.md:19,30`, `firebase-contract.md:141` |
| `resultSessions` (Firestore 문서) | **O** | `OPS-ttl.md:20` |
| `frames/` (프레임 이미지) | **X** | `OPS-ttl.md:31`, `firebase-contract.md:142` |
| WPF 로컬 저장분(`SaveLocalCopy`) | **X**(Firebase 무관) | `OPS-ttl.md:31`, `firebase-contract.md:216` |

### 6.3 삭제 주체 (확정)

| 방식 | 대상 | 채택 | 근거 |
|------|------|------|------|
| GCS Lifecycle | `results/` 파일(age 3일) | **채택**(파일 주력) | `OPS-ttl.md:7,19` |
| Firestore 네이티브 TTL | `resultSessions` 문서(`expiresAt`) | **채택**(문서) | `OPS-ttl.md:20,60-69` |
| 앱 `PurgeExpiredAsync` | `results/{sid}/` + 문서 함께 | **코드 존재·미사용**. it15 이후 HTTP 경로가 만료 조회·삭제를 지원하지 않아 **실행해도 `NotSupportedException`** | `OPS-ttl.md:6,21`, `UploadService.cs:100-122`, `HttpFirebaseClient.cs:163-176` |
| 스케줄 Cloud Functions | — | **미채택**(D-2). 백엔드 라우터 6종에 만료 정리 엔드포인트 없음 | `OPS-ttl.md:22`, `web/functions/src/app.ts:27-32` |

- GCS Lifecycle는 파일만, Firestore TTL은 문서만 삭제하므로 **둘 다 켜야** 파일+문서가 모두 정리된다(`OPS-ttl.md:24`).
- 이 프로젝트 설정값: project `mcphoto-955fb`, bucket `mcphoto-955fb.firebasestorage.app`, age 3일, prefix `results/`(`OPS-ttl.md:7`).
- 연동/호출 관점은 [30번 §8](./30-backend-firebase-integration.md) 참조.

---

## 7. 계약 불변식

| # | 불변식 | 근거 |
|---|--------|------|
| 1 | TTL/만료 삭제는 **`results/`만** 대상 | `OPS-ttl.md:30`, `firebase-contract.md:232` |
| 2 | `frames/`·로컬 저장분은 **삭제 비대상** | `OPS-ttl.md:31` |
| 3 | 삭제는 **문서 + Storage 파일 함께** 정리(고아 최소화) — 현재는 Lifecycle(파일)+TTL(문서) 둘을 켜서 충족 | `OPS-ttl.md:32`, [50 §1.1](./50-infra-gcp-lifecycle-and-ttl.md) |
| 4 | 미만료 `resultSessions` 문서는 미디어 URL **최소 1개 non-null**(둘 다 off면 문서 미생성) — 앱·서버 양쪽에서 강제 | `UploadService.cs:38-39`, `services/uploads.ts:169-176` |
| 5 | 프레임 삭제 시 문서 삭제 **전에** owner를 읽어 Storage 경로 확정(고아 이미지 방지) | `services/frames.ts:184-192` |
| 6 | 계정 삭제 시 소유 프레임(Firestore 문서 + `frames/{userId}/`) cascade 삭제 — **서버가 수행**(클라 no-op) | `services/frames.ts:201-211`, `HttpFrameRepository.cs:117-126` |
| 7 | `resultSessions` 문서 ID·프레임 다운로드 토큰은 **추측 불가 UUID**(토큰은 서버가 `randomUUID()`로 발급) | `UploadContract.cs:12,25`, `services/signing.ts:63` |
| 8 | commit의 미디어 URL은 **서버 버킷 + 해당 세션 경로**여야 한다(prepare 없이 임의 URL 주입 차단) | `services/uploads.ts:129-152` |

---

## 관련 문서

- [30 · 백엔드 API 연동](./30-backend-firebase-integration.md) — 인증 게이트·업로드 3단계·프레임/계정 흐름·미도달 시 동작
- [60 · 인증·계정·역할](./60-auth-accounts-and-roles.md) — `users.role` 위계·권한 매트릭스·PIN 게이트
- [50 · 인프라 보관/만료](./50-infra-gcp-lifecycle-and-ttl.md) — Lifecycle·TTL 적용 절차
- 인덱스: [README](./README.md)
