# 40 · 데이터베이스(Firestore) / Storage 스키마

| 항목 | 내용 |
|------|------|
| 문서 | Firestore 컬렉션 스키마 + Cloud Storage 경로 규약 + 보안 규칙 + TTL/만료 계약 |
| 범위 | `src/MCPhoto.Firebase/Dto/*`, `src/MCPhoto.Core/Models/*`, `UploadContract.cs`, `web/firestore.rules`, `web/storage.rules`, `web/OPS-ttl.md`. 연동 흐름은 [30 · 백엔드 Firebase 연동](./30-backend-firebase-integration.md) |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 | `UserDoc.cs`, `FrameTemplateDoc.cs`, `ResultSessionDoc.cs`, `User.cs`, `FrameTemplate.cs`, `ResultSession.cs`, `Slot.cs`, `UserRole.cs`, `UploadContract.cs`, `firestore.rules`, `storage.rules`, `OPS-ttl.md` |
| 갱신 규칙 | DTO의 `[FirestoreProperty]` 필드명·타입, Storage 경로 조립(`UploadContract`), 보안 규칙(`*.rules`)이 바뀌면 해당 표/근거(`파일:라인`)를 갱신. 연동 절차 변경은 30번 문서와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. Firestore 필드명은 DTO의 `[FirestoreProperty("...")]` 속성값이 실제 저장 키다.

---

## 1. Firestore 컬렉션 개요

| 컬렉션 | 문서 ID | DTO | 도메인 모델 | 웹 접근 | 근거 |
|--------|---------|-----|-------------|---------|------|
| `users` | 계정 id | `UserDoc` | `User` | 전면 차단 | `AccountService.cs:16`, `UserDoc.cs:7`, `User.cs:7` |
| `frameTemplates` | 프레임 id | `FrameTemplateDoc` | `FrameTemplate` | 전면 차단 | `FrameRepository.cs:16`, `FrameTemplateDoc.cs:7`, `FrameTemplate.cs:6` |
| `resultSessions` | UUIDv4 토큰 | `ResultSessionDoc` | `ResultSession` | 단건 get만 | `FirebaseClient.cs:162`, `ResultSessionDoc.cs:9`, `ResultSession.cs:6` |

---

## 2. 컬렉션별 스키마

### 2.1 `users` (문서 ID = 계정 id)

문서 ID는 계정 id를 사용한다(`AccountService.cs:43,58` — `Document(id)`).

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 로그인 ID(문서 ID와 동일) | `UserDoc.cs:9-10` |
| `password` | string | ⚠️ **MVP 평문**. 노출 시 전체 계정 유출 → 웹 전면 차단이 방어선 | `UserDoc.cs:12-13`, `User.cs:11` |
| `role` | string | `"user"` / `"manager"` / `"admin"`. 기본 `"user"` | `UserDoc.cs:15-16`, `UserRole.cs:19-32` |
| `createdAt` | timestamp | 생성 시각(UTC) | `UserDoc.cs:18-19` |

- 시드 문서: `id=devmcjo`, `password=1111`, `role=admin`(`AccountService.cs:17-18,106-112`).
- **DTO↔도메인 매핑**(`AccountService.cs:118-124`): `role` 문자열 ↔ `UserRole` enum은 `UserRoleExtensions.ToFirestoreValue`/`ParseRole`로 변환(`UserRole.cs:19-32`). `User` 도메인은 추가로 `Password`를 보유(평문).

### 2.2 `frameTemplates` (문서 ID = 프레임 id)

문서 ID는 프레임 id(`FrameRepository.cs:72` — `Document(frame.Id)`). id는 저장 시 `Guid.NewGuid()`로 부여(`FrameRepository.cs:56-57`).

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 프레임 ID(문서 ID와 동일) | `FrameTemplateDoc.cs:9-10` |
| `userId` | string \| null | 소유 계정 id. **기본 프레임은 null** | `FrameTemplateDoc.cs:12-13`, `FrameTemplate.cs:11` |
| `isDefault` | bool | 공용 기본 프레임 여부(true면 게스트 노출) | `FrameTemplateDoc.cs:15-16`, `FrameTemplate.cs:14` |
| `name` | string | 프레임 이름 | `FrameTemplateDoc.cs:18-19` |
| `imageUrl` | string | 프레임 이미지 다운로드 토큰 URL(Storage `frames/{owner}/…`) | `FrameTemplateDoc.cs:21-22`, `FrameRepository.cs:67` |
| `imageSize` | map `{ width:int, height:int }` | 등록 원본 픽셀 크기 | `FrameTemplateDoc.cs:24-25`, `FrameRepository.cs:157` |
| `slots` | array&lt;map&gt; | `{ index, x, y, width, height }` (int) 1~6개. 프레임 픽셀 좌표계 | `FrameTemplateDoc.cs:27-28`, `FrameRepository.cs:158-161`, `Slot.cs:6` |
| `createdAt` | timestamp | 생성 시각(UTC) | `FrameTemplateDoc.cs:30-31` |

- **map/array 저장 방식**: `imageSize`·`slots`는 강타입이 아닌 `Dictionary<string,object>`/`List<Dictionary<string,object>>`로 저장된다(`FrameTemplateDoc.cs:25,28`). 읽을 때 `ToInt`로 long/int/double을 int로 정규화(`FrameRepository.cs:131-133,140-145,165-171`).
- **Slot 도메인 파생값**: `Slot.AspectRatio = Width/Height`는 계산 프로퍼티로 **저장되지 않음**(`Slot.cs:15`).
- 계정당 최대 10개(커스텀), `SaveAsync`에서 강제(`FrameRepository.cs:48-54`).
- **하이브리드(it8 A2, 가정 포함)**: 계약상 DB `frameTemplates`에는 공용 기본 프레임(isDefault=true, userId=null)만 신규 저장하고 user 커스텀은 로컬 파일 전용이다(`firebase-contract.md:69`). 따라서 신규 흐름에서 `userId != null` 문서는 생성되지 않을 것으로 계약이 규정하나, `FrameRepository.SaveAsync` 코드 자체는 여전히 userId 경로를 지원한다(하위호환). 로컬 저장 스키마(`.png` + `.slots`)는 이 문서 범위 밖(`LocalFrameStore.cs` 참조).

### 2.3 `resultSessions` (문서 ID = UUIDv4 토큰)

문서 ID = 세션 토큰 = URL 토큰(UUIDv4, 추측 불가). 순차 ID 금지(`ResultSession.cs:8`, `UploadContract.cs:11-12`).

| 필드(저장 키) | 타입 | 의미 | 근거 |
|---------------|------|------|------|
| `id` | string | 세션 ID = 문서 ID = URL 토큰(UUIDv4) | `ResultSessionDoc.cs:11-12` |
| `finalImageUrl` | string \| null | 최종 이미지 토큰 URL. **사진 전송(SendPhoto) off면 null** | `ResultSessionDoc.cs:14-15`, `ResultSession.cs:15` |
| `timelapseUrl` | string \| null | 타임랩스 토큰 URL. 옵션 off·생성 실패·미포함 시 null | `ResultSessionDoc.cs:17-18`, `ResultSession.cs:17` |
| `createdAt` | timestamp | 생성 시각(UTC) | `ResultSessionDoc.cs:20-21`, `UploadService.cs:64` |
| `expiresAt` | timestamp | `createdAt + retentionHours`. **자동 삭제 기준** | `ResultSessionDoc.cs:23-24`, `UploadContract.cs:43-44` |
| `downloadPageUrl` | string | 모바일 다운로드 페이지 URL(QR 인코딩 대상) | `ResultSessionDoc.cs:26-27`, `UploadContract.cs:36-40` |

- **DTO↔도메인 매핑**(`FirebaseClient.cs:153-161` 쓰기, `:174-184` 읽기): `DateTime`(UTC) ↔ `Timestamp` 변환. 나머지 필드는 1:1.

#### 미디어 URL null 의미론 (it7 F2)

| 상황 | 판정 근거 | 근거 |
|------|-----------|------|
| **전송 옵션 꺼짐** | 미만료 문서 + URL이 null (의도적 제외, 실패·만료 아님) | `ResultSession.cs:12-14`, `ResultSessionDoc.cs:15` |
| 만료 | `expiresAt < now` 또는 문서 부재 | `firebase-contract.md:83`, §5 |
| 로드 실패 | URL 있는데 fetch 실패 | `firebase-contract.md:84` |

- **최소 1개 불변식**: 미만료 `resultSessions` 문서는 `finalImageUrl`·`timelapseUrl` 중 최소 1개가 non-null. 둘 다 off면 `QrDeliveryPolicy.Normalize`가 `enableQrDelivery`를 off로 정규화해 문서 자체가 생성되지 않음(`QrDeliveryPolicy.cs:13-19`, `UploadService.cs:37-38`). `photoSent`/`timelapseSent` 같은 명시 플래그는 추가하지 않음(계약 `firebase-contract.md:84-85`).

---

## 3. 도메인 모델 ↔ DTO 매핑 요약

| 도메인(Core) | DTO(Firebase) | 변환기 | 근거 |
|--------------|---------------|--------|------|
| `User` | `UserDoc` | `AccountService.ToUser`, `role` 문자열↔enum | `AccountService.cs:118-124`, `UserRole.cs:19-32` |
| `FrameTemplate`/`ImageSize`/`Slot` | `FrameTemplateDoc` | `FrameRepository.ToTemplate`/`ToDoc`, map/array 수동 조립, `ToInt` 정규화 | `FrameRepository.cs:120-171` |
| `ResultSession` | `ResultSessionDoc` | `FirebaseClient.CreateResultSessionAsync`/`QueryExpiredSessionsAsync`, `DateTime`↔`Timestamp` | `FirebaseClient.cs:150-187` |

- DTO는 모두 `[FirestoreData]` 클래스이며 필드명이 소문자 카멜(camelCase) 저장 키로 고정된다. 이 키가 웹과의 계약(웹이 읽는 필드명)이다.

---

## 4. Cloud Storage 경로 규약

### 4.1 경로 분리

| 용도 | 경로 | 파일명 규칙 | TTL 대상 | 근거 |
|------|------|-------------|----------|------|
| 결과물(사진) | `results/{sessionId}/final.{jpg\|png}` | 확장자 = `AppSettings.OutputFormat` | **O** | `UploadContract.cs:15-16` |
| 결과물(타임랩스) | `results/{sessionId}/timelapse.mp4` | 항상 mp4(H.264 무음) | **O** | `UploadContract.cs:19-20` |
| 프레임 이미지 | `frames/{owner}/{frameId}.png` | `owner = userId ?? "default"`, 항상 png | **X**(비대상) | `FrameRepository.cs:59-61` |

- `results/`만 TTL/만료 삭제 대상, `frames/`는 비대상(`FrameRepository.cs:59`, `firebase-contract.md:141-144`).

### 4.2 다운로드 토큰 URL 형식

업로드 시 GCS 객체 메타데이터 `firebaseStorageDownloadTokens`에 UUID 토큰을 심고(`FirebaseClient.cs:20,119-128`), 그 토큰으로 URL을 조립한다(`UploadContract.cs:26-30`):

```
https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}
```

- `{urlEncodedPath}`: `Uri.EscapeDataString`으로 인코딩(슬래시 → `%2F`). 예: `results%2F{sid}%2Ffinal.jpg`.
- `{downloadToken}`: 파일별 UUID. 이 토큰이 있어야 URL로 read 가능(그 자체가 capability).
- 웹은 이 URL을 브라우저가 직접 GET(img/video/a href). **Storage read 규칙·방문자 인증 불필요**(토큰 URL은 규칙 우회, `storage.rules:5-7`).

### 4.3 다운로드 페이지 URL(QR 인코딩 대상)

`UploadContract.DownloadPageUrl`(`UploadContract.cs:36-40`): `{hostingBaseUrl 트레일링슬래시제거}/?s={token}` (쿼리형 확정, `firebase-contract.md:106`). 예: `https://mcphoto-955fb.web.app/?s={uuid}`.

---

## 5. 보안 규칙 요약

### 5.1 Firestore (`web/firestore.rules`)

| 경로 | get | list | write | 의도 | 근거 |
|------|-----|------|-------|------|------|
| `users/{uid}` | deny | deny | deny | 평문 pw 보호(전체 계정 유출 방지) | `firestore.rules:16-18` |
| `frameTemplates/{fid}` | deny | deny | deny | 웹 접근 없음(WPF 전용) | `firestore.rules:21-23` |
| `resultSessions/{sid}` | **allow** | **deny** | deny | 토큰 단건 get만. list 열면 토큰 열거 가능 → 금지 | `firestore.rules:28-32` |
| `{document=**}` | deny | deny | deny | 그 외 기본 차단 | `firestore.rules:35-37` |

- 핵심: `resultSessions`는 `allow get: if true` + `allow list: if false`를 **분리**한다. `allow read`(get+list 통합) 금지(주석 `firestore.rules:29-30`).
- WPF 서비스 계정(Admin SDK)은 이 규칙을 우회하므로 `write:false`가 WPF 문서 생성을 막지 않는다(주석 `firestore.rules:9-11`).

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

- `expiresAt = createdAt + retentionHours`(`UploadContract.cs:43-44`). `retentionHours`는 `AppSettings.RetentionHours`(기본 24, 범위 1~72, `AppSettings.cs:62`, `MinRetentionHours=1`/`MaxRetentionHours=72` `:39-40`).
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
| WPF `PurgeExpiredAsync` | `results/{sid}/` + 문서 함께 | **코드 존재·미사용**(인프라 대체) | `OPS-ttl.md:6,21`, `UploadService.cs:80` |
| 스케줄 Cloud Functions | — | **미채택**(D-2) | `OPS-ttl.md:22`, `firebase-contract.md:230` |

- GCS Lifecycle는 파일만, Firestore TTL은 문서만 삭제하므로 **둘 다 켜야** 파일+문서가 모두 정리된다(`OPS-ttl.md:24`).
- 이 프로젝트 설정값: project `mcphoto-955fb`, bucket `mcphoto-955fb.firebasestorage.app`, age 3일, prefix `results/`(`OPS-ttl.md:7`).
- 연동/호출 관점은 [30번 §8](./30-backend-firebase-integration.md) 참조.

---

## 7. 계약 불변식

| # | 불변식 | 근거 |
|---|--------|------|
| 1 | TTL/만료 삭제는 **`results/`만** 대상 | `OPS-ttl.md:30`, `firebase-contract.md:232` |
| 2 | `frames/`·로컬 저장분은 **삭제 비대상** | `OPS-ttl.md:31` |
| 3 | 삭제는 **문서 + Storage 파일 함께** 정리(고아 최소화) | `OPS-ttl.md:32`, `UploadService.cs:90-92` |
| 4 | 미만료 `resultSessions` 문서는 미디어 URL **최소 1개 non-null**(둘 다 off면 문서 미생성) | `firebase-contract.md:85`, `QrDeliveryPolicy.cs:13-19` |
| 5 | 프레임 삭제 시 문서 삭제 **전에** owner를 읽어 Storage 경로 확정(고아 이미지 방지) | `FrameRepository.cs:89-94` |
| 6 | 계정 삭제 시 소유 프레임(Firestore 문서 + `frames/{userId}/`) cascade 삭제 | `AccountService.cs:85-89`, `FrameRepository.cs:106-118` |
| 7 | `resultSessions` 문서 ID·프레임 다운로드 토큰은 **추측 불가 UUID** | `UploadContract.cs:11-12`, `FirebaseClient.cs:119` |

---

## 관련 문서

- [30 · 백엔드 — Firebase 연동](./30-backend-firebase-integration.md) — 초기화·인증·업로드/프레임/계정 흐름·폴백
- 인덱스: [README](./README.md)(타 담당)
