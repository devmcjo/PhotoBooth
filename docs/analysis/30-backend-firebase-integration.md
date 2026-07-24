# 30 · 백엔드 — Firebase 연동 분석

| 항목 | 내용 |
|------|------|
| 문서 | 백엔드 Firebase 연동(초기화·인증·업로드·프레임·계정·만료 정리) |
| 범위 | `src/MCPhoto.Firebase/*` 전체 + `src/MCPhoto.Core/Upload`·`Frames`·`Accounts` 계약 + DI 등록. 스키마 상세는 [40 · DB/Storage 스키마](./40-database-firestore-and-storage-schema.md) |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 | `FirebaseClient.cs`, `UploadService.cs`, `FrameRepository.cs`, `AccountService.cs`, `QrService.cs`, `UploadContract.cs`, `ServiceRegistration.cs`, `AppSettings.cs`, `QrPopupViewModel.cs`, `App.xaml.cs` |
| 갱신 규칙 | 위 소스 중 하나라도 시그니처·경로·초기화 절차가 바뀌면 해당 표/근거(`파일:라인`)를 갱신. 스키마 변경은 40번 문서와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정.

---

## 1. 구성 요소 개요

MC포토의 백엔드 접근은 `MCPhoto.Firebase` 어셈블리 한 곳에 격리되어 있고, `MCPhoto.Core`가 인터페이스(계약)만 노출한다. WPF 앱은 인터페이스에만 의존한다.

| 계약(Core) | 구현(Firebase) | 책임 | 근거 |
|------------|----------------|------|------|
| `IFirebaseClient` | `FirebaseClient` | 초기화, Storage 업로드/삭제, `resultSessions` CRUD·만료 쿼리 | `src/MCPhoto.Core/Upload/IFirebaseClient.cs:9`, `src/MCPhoto.Firebase/FirebaseClient.cs:18` |
| `IUploadService` | `UploadService` | 결과물 업로드 오케스트레이션 + `ResultSession` 문서 생성 + 만료 정리 | `src/MCPhoto.Core/Upload/IUploadService.cs:9`, `src/MCPhoto.Firebase/UploadService.cs:13` |
| `IFrameRepository` | `FrameRepository` | `frameTemplates` CRUD + `frames/` Storage 관리 | `src/MCPhoto.Core/Frames/IFrameRepository.cs:8`, `src/MCPhoto.Firebase/FrameRepository.cs:14` |
| `IAccountService` | `AccountService` | `users` 로그인/CRUD/역할/시드/cascade | `src/MCPhoto.Core/Accounts/IAccountService.cs:8`, `src/MCPhoto.Firebase/AccountService.cs:14` |
| `IQrService` | `QrService`(Core에 구현) | 다운로드 페이지 URL → QR PNG | `src/MCPhoto.Core/Upload/IQrService.cs:4`, `src/MCPhoto.Core/Upload/QrService.cs:8` |
| `UploadContract`(순수 로직) | — | Storage 경로·토큰 URL·downloadPageUrl·expiresAt 조립 | `src/MCPhoto.Core/Upload/UploadContract.cs:9` |

- `FrameRepository`·`AccountService`는 `FirebaseClient` **구상 인스턴스**를 직접 받아 내부에 노출된 `internal FirestoreDb? Firestore`/`internal StorageClient? Storage`를 공유한다(`FirebaseClient.cs:30,33`). DI에서 구상 타입과 인터페이스를 같은 싱글턴으로 묶는다(§7).
- `QrService`는 Firebase에 의존하지 않는 순수 QRCoder 래퍼로 `MCPhoto.Core`에 위치한다(`src/MCPhoto.Core/Upload/QrService.cs:8`). QRCoder `PngByteQRCode`(System.Drawing 불필요), ECC 레벨 Q, 기본 모듈 20px(`QrService.cs:10`).

---

## 2. 초기화 절차

`FirebaseClient` 생성자에서 동기적으로 초기화한다(`FirebaseClient.cs:38-85`). 실패해도 예외를 던지지 않고 `IsInitialized=false`로 안전하게 진행한다.

### 2.1 절차 순서

| 단계 | 동작 | 근거 |
|------|------|------|
| 1 | 서비스 계정 키 경로 결정: 생성자 인자 `serviceAccountKeyPath` 우선, null이면 `DefaultKeyPath()` | `FirebaseClient.cs:47` |
| 2 | 키 파일 부재 → 경고 로그 후 **조기 반환**(`IsInitialized`은 기본 false 유지) | `FirebaseClient.cs:48-52` |
| 3 | `GoogleCredential.FromFile(keyPath)` 로드 | `FirebaseClient.cs:54` |
| 4 | 프로젝트 ID 결정: 인자 `projectId` 우선, null이면 키 JSON의 `project_id`에서 추론(`ProjectIdFromKey`) | `FirebaseClient.cs:55,104-114` |
| 5 | `FirestoreDbBuilder`로 `FirestoreDb` 생성(ProjectId + Credential) | `FirebaseClient.cs:57-61` |
| 6 | `StorageClient.Create(credential)` 생성 | `FirebaseClient.cs:63` |
| 7 | 버킷 결정(§2.3) | `FirebaseClient.cs:64-76` |
| 8 | `IsInitialized=true`, 완료 로그 | `FirebaseClient.cs:77-78` |
| 예외 | 어느 단계든 예외 → 로그 후 `IsInitialized=false` | `FirebaseClient.cs:80-84` |

### 2.2 서비스 계정 키 탐색 (`DefaultKeyPath`)

파일명은 고정 `serviceAccountKey.json`. 탐색 경로는 **실행 폴더 전용**(단일 후보, `KeyCandidatePaths`):

| 순위 | 경로 | 근거 |
|------|------|------|
| 1 | `{실행경로}\serviceAccountKey.json` (`AppContext.BaseDirectory`) — publish가 동봉 | `FirebaseClient.cs`(`KeyCandidatePaths`) |

- 과거의 `%ProgramData%\MCPhoto\` 폴백 후보는 **제거됨**(사용자 결정). 이제 실행 폴더에만 둔다.
- 파일이 없으면 실행경로 문자열을 그대로 반환한다(존재하지 않음). 호출측(생성자)에서 `File.Exists` 실패로 미초기화 처리.
- 키는 비밀이다: `.gitignore`가 `serviceAccountKey.json`을 커버(`.gitignore:41`), 인스톨러 미포함(소스 주석).

### 2.3 Storage 버킷 결정

| 조건 | 결과 버킷 | 근거 |
|------|-----------|------|
| 인자 `bucket` 비어있지 않음 | 그 값 그대로 | `FirebaseClient.cs:64-67` |
| 인자 `bucket` 비어있음 | `{projectId}.appspot.com` (레거시 규약) + **경고 로그** | `FirebaseClient.cs:69-76` |

- 신규 Firebase 프로젝트 버킷은 `{project}.firebasestorage.app` 형태라, 미지정 시 유도값(`.appspot.com`)과 불일치해 업로드가 실패할 수 있음을 소스가 경고한다(`FirebaseClient.cs:70-75`).
- 실제 배포에서는 `AppSettings.StorageBucket` 기본값이 `mcphoto-955fb.firebasestorage.app`로 박혀 있어(`AppSettings.cs:110`) DI가 이 값을 주입한다(§7).

### 2.4 Blaze(종량제) 요금제 필요성

- Cloud Storage는 **2026-02-03부로 무료 Spark 요금제에서 접근 불가**(402/403). 사진·타임랩스·프레임 이미지가 모두 Storage에 저장되므로, 다운로드 페이지가 파일을 제공하려면 프로젝트가 **Blaze로 전환**되어 있어야 한다(근거: `docs/design/firebase-contract.md:28`).
- Firestore·Hosting은 Spark 무료 유지(`firebase-contract.md:31-32`).
- 소스 상 코드는 요금제를 검사하지 않는다. 버킷 접근이 거부되면 `UploadFileAsync`에서 예외가 나고, QR 팝업이 이를 "전송 실패 — 로컬 보존" 완화 경로로 처리한다(§3.3, `QrPopupViewModel.cs:78-89`).

---

## 3. 인증 모델

### 3.1 WPF(생산자) — 서비스 계정(Admin SDK)

- WPF는 `GoogleCredential.FromFile`로 로드한 **서비스 계정 자격증명**으로 `FirestoreDb`/`StorageClient`를 만든다(`FirebaseClient.cs:54-63`). 이는 관리자 권한이며 **Firestore/Storage 보안 규칙을 완전히 우회**한다.
- 결과: `firestore.rules`/`storage.rules`의 `write:false`가 **WPF 쓰기를 막지 않는다**(규칙 파일 주석 `web/firestore.rules:9-11`, `web/storage.rules:10-11`). WPF는 신뢰 경로로 문서·파일을 생성한다.
- 소스 주석은 이를 "MVP 1차 — 규칙 우회 쓰기"로 명시하고, 배포 시 규칙 준수 인증 클라이언트로 교체 가능하도록 인터페이스로 추상화했다고 밝힌다(`FirebaseClient.cs:14`, `IFirebaseClient.cs:6`).

### 3.2 웹(소비자) — 공개 API 키 + 보안 규칙

- 웹 다운로드 페이지는 공개 Firebase JS SDK config(apiKey 공개)로 접근하며 **보안 규칙이 유일한 방어선**이다(`web/firestore.rules:5`). 웹은 `resultSessions` 단건 get만 하고, 파일은 문서에 담긴 토큰 URL로 직접 GET한다(상세 규칙은 [40번 §4](./40-database-firestore-and-storage-schema.md)).

### 3.3 접근 방식 대비

| 주체 | 인증 | 보안 규칙 | 접근 범위 |
|------|------|-----------|-----------|
| WPF | 서비스 계정 JSON(Admin SDK) | **우회** | users/frameTemplates/resultSessions/Storage 전체 읽기·쓰기 |
| 웹 | 공개 API 키(비인증) | **종속** | `resultSessions` 단건 get + 토큰 URL 직접 GET만 |

---

## 4. 업로드 흐름 (`UploadService.UploadResultAsync`)

트리거는 결과 화면 이후 QR 팝업 진입 시 `QrPopupViewModel.OnEnterAsync`이다(`QrPopupViewModel.cs:40-91`).

### 4.1 사전 조건 및 미디어 선택

| 단계 | 동작 | 근거 |
|------|------|------|
| 진입 | QR off면 애초에 QR 상태로 오지 않음(ResultViewModel 분기) | `QrPopupViewModel.cs:45` |
| 미디어 선택 | `SendPhoto` on이면 최종 이미지 경로, `SendTimelapse` on이면 타임랩스 경로 전달(옵션 기준) | `QrPopupViewModel.cs:47-48` |
| 미초기화 가드 | `IsInitialized=false`면 예외("업로드 불가") | `UploadService.cs:31-32` |
| 존재 가드 | 각 미디어는 "옵션 on(경로 non-null) & `File.Exists`"일 때만 업로드 | `UploadService.cs:35-36` |
| 최소 1개 불변식 | 사진·타임랩스 모두 부재면 예외("전송할 미디어가 없습니다") | `UploadService.cs:37-38` |

### 4.2 업로드 및 문서 생성

| 순서 | 동작 | 경로/URL | 근거 |
|------|------|----------|------|
| 토큰 | `UploadContract.NewSessionToken()` = `Guid.NewGuid()` (UUIDv4). 세션 ID이자 문서 ID | — | `UploadService.cs:40`, `UploadContract.cs:12` |
| 1 | 사진 업로드(png→`image/png`, 그 외→`image/jpeg`) | `results/{sid}/final.{png\|jpg}` | `UploadService.cs:44-52`, `UploadContract.cs:15-16` |
| 2 | 타임랩스 업로드(`video/mp4`) | `results/{sid}/timelapse.mp4` | `UploadService.cs:57-61`, `UploadContract.cs:19-20` |
| — | 각 업로드는 파일별 **다운로드 토큰(UUID)** 부여 → 토큰 URL 조립 | 토큰 URL(§4.3) | `UploadService.cs:50-51,59-60` |
| 3 | `ResultSession` 조립 후 `CreateResultSessionAsync`로 `resultSessions/{sid}` 문서 생성 | Firestore | `UploadService.cs:64-74` |
| 반환 | `ResultSession`(FinalImageUrl/TimelapseUrl는 off면 null, downloadPageUrl 포함) | — | `UploadService.cs:65-77` |

- 사진이 off면 `FinalImageUrl=null`, 타임랩스가 off면 `TimelapseUrl=null`로 문서에 기록된다(`UploadService.cs:42-43,55`). null의 의미론은 [40번 §2.2](./40-database-firestore-and-storage-schema.md)에서 상세.

### 4.3 다운로드 토큰 URL 형식 (`FirebaseClient.UploadFileAsync` + `UploadContract`)

- 업로드 시 `GcsObject`의 메타데이터 `firebaseStorageDownloadTokens`에 `Guid.NewGuid()` 토큰을 심는다(`FirebaseClient.cs:119-128`). 이 메타데이터가 있어야 토큰 URL이 동작(소스 주석 `FirebaseClient.cs:126`).
- URL 조립(`UploadContract.TokenDownloadUrl`, `UploadContract.cs:26-30`):
  ```
  https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}
  ```
  경로는 `Uri.EscapeDataString`으로 인코딩(슬래시 → `%2F`).
- `downloadPageUrl` 조립(`UploadContract.DownloadPageUrl`, `UploadContract.cs:36-40`): `{hostingBaseUrl 트레일링슬래시제거}/?s={token}` (쿼리형). `hostingBaseUrl`은 `AppSettings.HostingBaseUrl`(기본 `https://mcphoto-955fb.web.app`, `AppSettings.cs:103`).

### 4.4 QR 생성 및 실패 처리

- 업로드 성공 후에만 QR 노출: `QrService.GenerateQrPng(result.DownloadPageUrl, 12)` → 이미지 바인딩(`QrPopupViewModel.cs:70-74`).
- 실패 시 우아 처리(it5 B6): 예외를 삼키지 않고 잡아 `UploadFailed=true`, QR 숨김, 로컬 보존 안내 표시(`QrPopupViewModel.cs:78-89`). 결과물은 QR 분기 이전에 로컬 저장되어 손실 0(주석 `QrPopupViewModel.cs:81`). `[재시도]` 제공(`QrPopupViewModel.cs:94-95`).

---

## 5. 프레임 저장/삭제 흐름 (`FrameRepository`)

컬렉션 `frameTemplates`, Storage 규약 `frames/{owner}/{frameId}.png`(`FrameRepository.cs:16,59-61`).

### 5.1 조회

| 메서드 | 쿼리 | 미초기화 시 | 근거 |
|--------|------|-------------|------|
| `GetDefaultFramesAsync` | `WhereEqualTo("isDefault", true)` | 빈 배열 | `FrameRepository.cs:30-35` |
| `GetUserFramesAsync(userId)` | `WhereEqualTo("userId", userId)` | 빈 배열 | `FrameRepository.cs:37-42` |

### 5.2 저장 (`SaveAsync`)

| 단계 | 동작 | 근거 |
|------|------|------|
| 가드 | `Db is null`이면 예외("프레임 저장 불가") | `FrameRepository.cs:46,173-177` |
| 10개 제한 | `UserId` 있을 때, 기존 개수 ≥ 10 이고 신규(id 불일치)면 예외 | `FrameRepository.cs:48-54` (`MaxPerUser=10`, `:17`) |
| ID 부여 | 비어있으면 `Guid.NewGuid()` | `FrameRepository.cs:56-57` |
| 이미지 업로드 | `owner = UserId ?? "default"` → `frames/{owner}/{id}.png`(`image/png`) → 임시파일 경유 업로드 → `ImageUrl`에 토큰 URL 기록 | `FrameRepository.cs:59-69` |
| 문서 생성 | `frameTemplates/{id}` SetAsync | `FrameRepository.cs:71-73` |

- `frames/`는 TTL 비대상(주석 `FrameRepository.cs:59`).
- **하이브리드 저장(it8 A2) 참고**: 계약상 `frameTemplates`(DB)에는 **공용 기본 프레임(isDefault=true, userId=null)만** 신규 저장하고, 일반 user 커스텀 프레임은 로컬 전용(`ILocalFrameStore`, 실행폴더 `Frame\`)에 저장한다(`firebase-contract.md:69`, `ServiceRegistration.cs:73-75`). 즉 `FrameRepository.SaveAsync`의 userId 경로는 하위호환용이며 신규 흐름은 로컬 저장소가 담당. 파워 프레임은 로컬에도 캐시(`ILocalFrameStore.CacheFromDb`, `LocalFrameStore.cs:24-25`). **로컬 저장소 상세는 이 문서 범위 밖(프레임 카탈로그/편집기 분석 참조).**

### 5.3 삭제 (`DeleteAsync`) — bool 반환

| 단계 | 동작 | 근거 |
|------|------|------|
| 가드 | `Db is null`이면 예외 | `FrameRepository.cs:78` |
| 존재 확인 | 문서 스냅샷 조회. **없으면 `false` 반환**(Firestore는 없는 문서 삭제가 no-op이라 성공 오인 방지) | `FrameRepository.cs:82-87` |
| Storage 삭제 | 문서에서 `owner` 읽어 `frames/{owner}/{frameId}.png` 프리픽스 삭제. 문서 삭제 **전에** 읽어 경로 확정(고아 이미지 방지) | `FrameRepository.cs:89-99` |
| 문서 삭제 | `DeleteAsync` 후 `true` 반환 | `FrameRepository.cs:101-103` |

- Storage 삭제 실패는 로그만 남기고 문서 삭제를 계속한다(`FrameRepository.cs:96-99`).

### 5.4 사용자 전체 프레임 삭제 (`DeleteAllByUserAsync`, cascade용)

- `WhereEqualTo("userId", userId)` 문서 전부 삭제 + Storage `frames/{userId}/` 프리픽스 전체 삭제(`FrameRepository.cs:106-118`). 미초기화 시 no-op(`:108`).

---

## 6. 계정 서비스 (`AccountService`)

컬렉션 `users`, 시드 `devmcjo`/`1111`/admin(`AccountService.cs:16-18`). **비밀번호 평문 비교/저장**(MVP, `AccountService.cs:46`, `User.cs:11`).

| 메서드 | 동작 | 미초기화 시 | 근거 |
|--------|------|-------------|------|
| `LoginAsync` | `users/{id}` get → 평문 pw 비교 | 시드 계정만 인메모리 허용 | `AccountService.cs:33-48` |
| `CreateAsync` | **권한 게이트 먼저**(`actingRole.CanCreate(role)`, 위반 시 `UnauthorizedAccessException`) → 중복 검사 → 문서 생성 | 게이트 통과 후 예외("쓰기 불가") | `AccountService.cs:50-66` |
| `ChangePasswordAsync` | `password` 필드 UpdateAsync | 예외 | `AccountService.cs:68-73` |
| `GetAllAsync` | 전체 컬렉션 조회 | 빈 배열 | `AccountService.cs:75-80` |
| `DeleteAsync` | **cascade: `_frames.DeleteAllByUserAsync` 먼저**(실패는 로그만) → `users/{id}` 삭제 | 예외 | `AccountService.cs:82-90` |
| `SetRoleAsync` | `role` 필드 UpdateAsync | 예외 | `AccountService.cs:92-97` |
| `EnsureSeedAccountAsync` | 시드 문서 없으면 생성 | no-op(로그인 시 인메모리 시드) | `AccountService.cs:99-116` |

- 권한 게이트 규칙(`UserRole.cs:41-50`): admin→{User,Manager}, manager→{User}, 그 외 없음. admin→admin 불가(최종 1인).
- `EnsureSeedAccountAsync`는 앱 시작 시 `App.OnStartup`에서 fire-and-forget 호출(`App.xaml.cs:73,79-91`). 실패해도 경고 로그만(오프라인 대비).
- 역할 문자열 매핑: `user`/`manager`/`admin`(`UserRole.cs:19-32`, DTO `UserDoc.Role` 기본 `"user"`, `UserDoc.cs:15-16`).

---

## 7. DI 등록 및 버킷 주입 (`ServiceRegistration`)

| 등록 | 방식 | 근거 |
|------|------|------|
| `FirebaseClient`(구상) | Singleton. `AppSettings.StorageBucket` 읽어 주입(빈 값이면 null → project_id 유도 + 경고) | `ServiceRegistration.cs:58-64` |
| `IFirebaseClient` | 위 구상 인스턴스를 그대로 반환(동일 싱글턴 공유) | `ServiceRegistration.cs:65` |
| `IUploadService`→`UploadService` | Singleton | `ServiceRegistration.cs:66` |
| `IQrService`→`QrService` | Singleton | `ServiceRegistration.cs:67` |
| `IFrameRepository`→`FrameRepository` | Singleton | `ServiceRegistration.cs:70` |
| `IAccountService`→`AccountService` | Singleton | `ServiceRegistration.cs:71` |
| `ILocalFrameStore`→`LocalFrameStore` | Singleton, 루트=`{BaseDirectory}\Frame` | `ServiceRegistration.cs:74-75` |

- 구상 `FirebaseClient`와 `IFirebaseClient`를 같은 싱글턴으로 묶어, `FrameRepository`·`AccountService`가 `internal Firestore`/`Storage`를 공유한다(주석 `ServiceRegistration.cs:56-57`).

---

## 8. 만료 정리 API — 존재하나 앱에서 미호출

| API | 위치 | 동작 | 앱 런타임 호출 |
|-----|------|------|----------------|
| `IUploadService.PurgeExpiredAsync` | `UploadService.cs:80-102` | 만료 세션 조회 → `results/{sid}/` Storage + `resultSessions/{sid}` 문서 함께 삭제 | **없음** |
| `IFirebaseClient.QueryExpiredSessionsAsync` | `FirebaseClient.cs:165-187` | `WhereLessThan("expiresAt", now)` 조회 | (PurgeExpiredAsync 내부에서만) |
| `IFirebaseClient.DeleteResultSessionAsync` | `FirebaseClient.cs:189-193` | 문서 단건 삭제 | (동상) |
| `IFirebaseClient.DeleteStoragePrefixAsync` | `FirebaseClient.cs:136-148` | 프리픽스 하위 객체 열거 후 삭제 | 프레임 삭제/PurgeExpired에서 사용 |

- 코드 전체 검색 결과, `PurgeExpiredAsync`·`QueryExpiredSessionsAsync`의 호출부는 **테스트(`tests/MCPhoto.Tests/*`)뿐**이며 앱(`MCPhoto.App`) 런타임 경로에는 없다.
- 운영은 **인프라로 대체**: GCS Lifecycle(파일, age 3일, `results/` 한정) + Firestore 네이티브 TTL(문서, `expiresAt`) 둘 다 채택. WPF 직접 삭제(`PurgeExpiredAsync`)는 "코드에 존재하나 미사용"으로 명시(`web/OPS-ttl.md:6,21`). 상세는 [40번 §5](./40-database-firestore-and-storage-schema.md).

---

## 9. 오프라인/미초기화 폴백 동작

`IsInitialized=false`(또는 `Firestore is null`)일 때 각 기능의 동작.

| 기능 | 폴백 동작 | 근거 |
|------|-----------|------|
| `UploadService.UploadResultAsync` | 예외("업로드 불가") → QR 팝업이 잡아 "전송 실패 — 로컬 보존" 안내 | `UploadService.cs:31-32`, `QrPopupViewModel.cs:78-89` |
| `UploadService.PurgeExpiredAsync` | `0` 반환(no-op) | `UploadService.cs:82` |
| `FrameRepository.GetDefault/GetUserFrames` | 빈 배열(오프라인 게스트+번들 모드) | `FrameRepository.cs:32,39` |
| `FrameRepository.SaveAsync` | 예외("프레임 저장 불가") | `FrameRepository.cs:173-177` |
| `FrameRepository.DeleteAllByUserAsync` | no-op | `FrameRepository.cs:108` |
| `AccountService.LoginAsync` | 시드 계정(`devmcjo`/`1111`/admin)만 인메모리 허용, 그 외 null | `AccountService.cs:36-41` |
| `AccountService.Create/ChangePw/Delete/SetRole` | 예외("쓰기 불가"). 단 Create는 권한 게이트가 미초기화보다 우선 검사 | `AccountService.cs:52-57,126-130` |
| `AccountService.GetAllAsync` | 빈 배열 | `AccountService.cs:77` |
| `AccountService.EnsureSeedAccountAsync` | no-op(로그인 시 인메모리 시드로 대체) | `AccountService.cs:101` |
| `QrService.GenerateQrPng` | Firebase 무관, 정상 동작(단 업로드 실패면 애초에 호출 안 됨) | `QrService.cs:10` |

- 핵심 설계 의도: 서비스 계정 키가 없어도 앱이 크래시 없이 "오프라인/게스트+번들+로컬 저장" 완화 경로로 동작(`FirebaseClient.cs:16`).

---

## 관련 문서

- [40 · 데이터베이스(Firestore)/Storage 스키마](./40-database-firestore-and-storage-schema.md) — 컬렉션 필드·경로 규약·보안 규칙·TTL 상세
- 인덱스: [README](./README.md)(타 담당)
