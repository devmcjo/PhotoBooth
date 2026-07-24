# MCPhoto — WPF 백엔드 프록시 마이그레이션 보안 재설계 (방향 B)

| 항목 | 값 |
|------|-----|
| 문서 성격 | **보안 재설계 설계 문서** — WPF 클라이언트에서 Firebase Admin 키를 제거하고, DB 접근 권한(Admin)을 내가 관리하는 백엔드(서버 경유)에만 두는 아키텍처 |
| 대상 | WPF(.NET 8) `MCPhoto.App`/`MCPhoto.Firebase`/`MCPhoto.Core` + 신규 백엔드(Cloud Functions) + 기존 웹 다운로드 페이지 |
| 방향 | **B (서버 경유) 확정** — 사용자 결정 |
| 작성일 | 2026-07-24 |
| 상태 | **설계 v1 (리뷰 대기)** — 코드/배포 미착수 |
| 근거 | 본 문서의 모든 "현재 동작"은 `파일:라인` 실측. 실측 파일 목록은 §11 |
| 후속 | 이 문서 확정 후 별도 WBS 블루프린트(`docs/templates/WBS_BLUEPRINT.md` 형식)로 구현 단계화 — 본 문서는 WBS 미포함(사용자 지시) |

> **표기 규칙**
> - `[CONFIRM]` : 사용자가 리뷰 시 조정 가능한 스택/기술 선택. 기본안을 제시하되 확정 아님.
> - `[USER-DECISION-REQUIRED]` : 설계자가 결정할 수 없는 순수 정책/운영 판단. 반드시 사용자 답변 필요.
> - `[CODE]` : 내가 코드로 작업 가능(WPF HTTP 계층 / Functions 소스).
> - `[CONSOLE]` : 사용자가 콘솔·CLI·배포에서 수동 수행(함수 배포·IAM·시크릿·규칙·키 폐기).
> - 근거는 `파일:라인`. **가정**은 소스 미확인 추정.

---

## 0. 요약 (Executive Summary)

### 0.1 현재의 치명적 문제 (실측)

- WPF는 `GoogleCredential.FromFile`로 **서비스 계정 키(serviceAccountKey.json)**를 로드해 `FirestoreDb`/`StorageClient`를 만든다(`src/MCPhoto.Firebase/FirebaseClient.cs:62-71`). 이는 **Admin 권한**이라 Firestore/Storage **보안 규칙을 완전히 우회**한다(`docs/analysis/30-backend-firebase-integration.md:85-86`).
- 이 키는 **publish 시 실행 폴더에 동봉**된다(`publish.ps1:89-122`, 출력 `publish\MCPhoto\serviceAccountKey.json`). 앱은 실행 폴더 키를 최우선 로드(`FirebaseClient.cs:103-121`).
- **결과: exe 폴더를 가진 사람은 누구나 DB/Storage 전권**을 가진다. `users` 컬렉션의 **평문 비밀번호 전량 유출**(`UserDoc.cs:12`, `AccountService.cs:50`), 임의 계정 생성/삭제/역할변경, 모든 결과물·프레임 읽기/쓰기/삭제가 가능하다. publish.ps1 주석도 "internal beta only, do NOT distribute externally"로 경고한다(`publish.ps1:24-25`).

### 0.2 목표 상태 (방향 B)

- **클라이언트(WPF)에는 Admin 키·시크릿이 0.** 공개 가능한 값(백엔드 base URL, 배포별 클라이언트 자격)만 보유.
- **DB 접근 권한(Admin)은 내가 관리하는 백엔드에만.** WPF는 Firestore/Storage에 직접 붙지 않고 **HTTPS 엔드포인트를 통해서만** 요청한다. 백엔드가 요청을 인증·인가·검증한 뒤 자신의 권한(ADC)으로 DB를 대신 조작한다.
- **핵심 이점**: `[CONFIRM]` **Cloud Functions 2nd gen**은 런타임에 **기본 서비스 계정(ADC, Application Default Credentials)**으로 Admin SDK를 초기화한다 → **키 파일 자체가 어디에도 존재하지 않는다.** 유출할 파일이 없다.

### 0.3 왜 이 마이그레이션이 저비용인가 (핵심 강점)

앱은 이미 **Firebase 접근을 5개 인터페이스로 추상화**해 `MCPhoto.Core`에만 의존한다(`docs/analysis/30-...:17-28`). UI/뷰모델은 이 인터페이스에만 의존한다.

| 계약(Core, 불변) | 현재 구현(Firebase 직결) | 목표 구현(HTTP) |
|---|---|---|
| `IFirebaseClient` | `FirebaseClient` (Admin SDK) | `HttpFirebaseClient` (신규) |
| `IAccountService` | `AccountService` (Firestore 직결) | `HttpAccountService` (신규) |
| `IFrameRepository` | `FrameRepository` (Firestore/Storage 직결) | `HttpFrameRepository` (신규) |
| `IUploadService` | `UploadService` (Client 오케스트레이션) | 대부분 재사용 가능(§5.4) |
| `IQrService` | `QrService` (QRCoder 순수, Firebase 무관) | **무변경** |

→ **구현체만 교체**하고 `ServiceRegistration.Register`(`src/MCPhoto.App/ServiceRegistration.cs:66-79`)의 DI 등록 3~4줄을 바꾸면 **UI·뷰모델·내비게이션 전부 무변경**이다. 인터페이스 시그니처는 그대로 유지한다(§5에서 각 메서드 매핑).

---

## 1. .NET 대상 · 스택 결정

### 1.1 WPF 클라이언트

- **.NET 8 유지** (현행 `net8.0-windows`, self-contained single-file, win-x64, `publish.ps1:61`). 변경 없음.
- HTTP 계층은 `System.Net.Http.HttpClient` + `System.Text.Json`(이미 프로젝트 관례, DTO 직렬화에 사용). 신규 외부 패키지 최소화.
- `IHttpClientFactory`(`Microsoft.Extensions.Http`) 도입 `[CONFIRM]` — 소켓 고갈 방지·핸들러 수명 관리·베이스 주소/헤더 중앙 구성. Generic Host를 이미 사용(`App.xaml.cs:45`)하므로 `services.AddHttpClient(...)` 한 줄로 등록 가능.

### 1.2 백엔드 (서버)

- `[CONFIRM]` **Google Cloud Functions 2nd gen + TypeScript** (Node.js 20 런타임).
  - **이유 1 (핵심)**: 런타임이 **기본 서비스 계정(ADC)**으로 `firebase-admin`을 초기화 → `initializeApp()` 한 줄, **키 파일 없음**. 이것이 방향 B의 근본 이점을 그대로 실현한다.
  - **이유 2**: 기존 프로젝트가 이미 Firebase(Firestore/Storage/Hosting)에 상주. 같은 프로젝트 안 Functions는 Firestore·Storage에 IAM만으로 접근. 별도 서버·VM·컨테이너 운영 불필요.
  - **이유 3**: 웹 팀(js-*)이 이미 Firebase JS SDK/Emulator에 익숙(`web/package.json`, `web/public/app.js`). TS Functions는 같은 생태계.
  - `[CONFIRM]` 대안: **Cloud Run(컨테이너, ASP.NET Core 또는 Node)** — 상시 인스턴스·긴 실행·복잡 로직에 유리하나 이 워크로드(짧은 CRUD/업로드 중개)엔 과함. **Firebase Auth로 이전**(§7 대안)은 인증만 대체할 뿐 "서버가 DB를 대신 조작" 목표엔 여전히 서버가 필요.
- 엔드포인트 형식 `[CONFIRM]`: **HTTPS `onRequest`(REST)** 기본안. 현재 인터페이스가 REST 동사에 자연히 매핑되고, WPF `HttpClient`에서 표준 처리 용이. (대안: **`onCall`(callable)** — Firebase Auth와 결합 시 토큰 검증 자동. 단 WPF는 JS callable 프로토콜을 수동 구현해야 해 이점 감소 → REST 권장.)
- 리전 `[USER-DECISION-REQUIRED]`: 함수 배포 리전(예: `asia-northeast3` 서울). 지연·요금에 영향. 현재 버킷/프로젝트는 `mcphoto-955fb`(§전역).

### 1.3 인증 모델 (클라 → 서버)

- `[CONFIRM]` **로그인 시 서버가 자격 검증 후 단기 토큰 발급 → 이후 호출은 `Authorization: Bearer <token>`.**
  - 로그인 엔드포인트가 `users`에서 계정을 확인(비번 **해시** 검증, §7)하고, 성공 시 **단기 JWT**(예: 15~60분, 역할·계정 ID 클레임 포함)를 발급. 서명 키는 서버 시크릿(§8).
  - WPF는 토큰을 **메모리에만** 보관(디스크 미저장 — 키오스크·재부팅 시 재로그인). 만료 시 재로그인 유도 `[CONFIRM]`(리프레시 토큰 도입 여부는 §7 결정).
  - **대안(트레이드오프) — 배포별 API 키**: 각 배포 exe에 서버가 발급한 API 키를 심어 모든 요청에 첨부. 장점: 로그인 안 한 게스트 흐름(기본 프레임 조회·결과물 업로드)도 인증됨. 단점: **키가 다시 클라이언트에 상주**(교체·폐기 관리 필요, exe 유출 시 그 키만 폐기). → **혼합안 권장**: 게스트 가능 엔드포인트는 "배포 API 키"로, 계정 조작 엔드포인트는 "로그인 JWT + 역할"로 이중화. §6.2 각 엔드포인트에 요구 인증을 태깅.

---

## 2. 현재 앱이 수행하는 DB/Storage 작업 전수 (실측)

아래는 WPF가 현재 직접(Admin 권한으로) 수행하는 **모든** Firestore/Storage 작업이다. §6에서 각각을 서버 엔드포인트로 매핑한다.

| # | 작업 | 현재 호출 지점 | 대상 | 인증 필요성 | 근거 |
|---|------|---------------|------|-------------|------|
| A1 | **로그인**(id/pw → User) | `AccountService.LoginAsync` | Firestore `users/{id}` get + 평문 비교 | 없음(자격 자체가 인증) | `AccountService.cs:33-52` |
| A2 | 계정 생성(역할 게이트) | `AccountService.CreateAsync` | `users/{id}` set | 파워(admin/manager), 역할 위계 | `AccountService.cs:54-70` |
| A3 | 비밀번호 변경(본인) | `AccountService.ChangePasswordAsync` | `users/{id}.password` update | 로그인 사용자 | `AccountService.cs:72-77` |
| A4 | 전체 계정 목록 | `AccountService.GetAllAsync` | `users` 전체 조회 | 파워 | `AccountService.cs:79-84` |
| A5 | 계정 삭제(+프레임 cascade) | `AccountService.DeleteAsync` | `users/{id}` delete + `DeleteAllByUserAsync` | 파워, 역할 위계 | `AccountService.cs:86-94` |
| A6 | 역할 지정(승격) | `AccountService.SetRoleAsync` | `users/{id}.role` update | admin | `AccountService.cs:96-101` |
| A7 | 시드 계정 보장 | `AccountService.EnsureSeedAccountAsync` | `users/{devmcjo}` get/set | 부트스트랩(서버 내부화) | `AccountService.cs:103-120`, `App.xaml.cs:74` |
| F1 | 기본 프레임 조회 | `FrameRepository.GetDefaultFramesAsync` | `frameTemplates` where isDefault | 없음(공개) | `FrameRepository.cs:30-35` |
| F2 | 사용자 프레임 조회 | `FrameRepository.GetUserFramesAsync` | `frameTemplates` where userId | 로그인 사용자(본인) | `FrameRepository.cs:37-42` |
| F3 | 프레임 저장(이미지 업로드+문서) | `FrameRepository.SaveAsync` | Storage `frames/{owner}/{id}.png` + `frameTemplates/{id}` set | 파워(공용 기본 프레임, §5.3) | `FrameRepository.cs:44-74` |
| F4 | 프레임 삭제(문서+이미지) | `FrameRepository.DeleteAsync` | `frameTemplates/{id}` + Storage prefix | 파워/소유자 | `FrameRepository.cs:76-104` |
| F5 | 사용자 프레임 전체 삭제 | `FrameRepository.DeleteAllByUserAsync` | where userId + Storage `frames/{userId}/` | cascade 내부 | `FrameRepository.cs:106-118` |
| P1 | 프레임 이미지 **다운로드** | (토큰 URL 직접 GET) | Storage 토큰 URL fetch | 토큰 자체가 capability | `FrameTemplateDoc.cs:21`, `UploadContract.cs:39-43` |
| U1 | 결과물 파일 업로드(+토큰) | `FirebaseClient.UploadFileAsync` | Storage `results/{sid}/…` PUT + 다운로드토큰 메타 | 게스트 포함(촬영자) | `FirebaseClient.cs:135-169` |
| U2 | resultSession 문서 생성 | `FirebaseClient.CreateResultSessionAsync` | `resultSessions/{sid}` set | 게스트 포함 | `FirebaseClient.cs:185-198` |
| U3 | 만료 세션 조회 | `FirebaseClient.QueryExpiredSessionsAsync` | `resultSessions` where expiresAt<now | 운영(현재 앱 미호출) | `FirebaseClient.cs:200-222` |
| U4 | resultSession 문서 삭제 | `FirebaseClient.DeleteResultSessionAsync` | `resultSessions/{sid}` delete | 운영 | `FirebaseClient.cs:224-228` |
| U5 | Storage prefix 삭제 | `FirebaseClient.DeleteStoragePrefixAsync` | Storage 객체 열거+삭제 | 운영/삭제 | `FirebaseClient.cs:171-183` |
| U6 | 만료 정리 오케스트레이션 | `UploadService.PurgeExpiredAsync` | U3+U5+U4 | 운영(현재 앱 미호출) | `UploadService.cs:100-122` |

**중요(실측)**: U3/U4/U6(만료 정리)는 **앱 런타임에서 호출되지 않는다**. 만료 삭제는 인프라(GCS Lifecycle age 3일 + Firestore 네이티브 TTL)가 담당한다(`docs/analysis/30-...:223-233`, `docs/analysis/40-...:177-187`). → 마이그레이션에서 **U3/U4/U5/U6은 클라이언트에서 제거 대상**(서버로 옮길 필요조차 없음, §5.1·§9).

---

## 3. 목표 아키텍처

### 3.1 컴포넌트 다이어그램

```
┌────────────────────────────┐        HTTPS (Bearer / API key)     ┌──────────────────────────────┐
│  WPF (MCPhoto.App/.Http)   │  ─────────────────────────────────▶ │  Cloud Functions 2nd gen (TS) │
│                            │                                     │  ADC = 기본 서비스계정(키 없음) │
│  UI/VM (무변경)            │                                     │  ┌──────────────────────────┐ │
│    │ 의존                  │                                     │  │ auth  (login, 토큰발급)   │ │
│    ▼                       │                                     │  │ accounts (CRUD/role)     │ │
│  MCPhoto.Core 인터페이스   │                                     │  │ frames (조회/저장/삭제)   │ │
│    (IAccountService 등)    │                                     │  │ uploads (서명URL/세션)    │ │
│    ▲ 구현                  │                                     │  └───────────┬──────────────┘ │
│  MCPhoto.Http (신규)       │                                     │              │ Admin SDK       │
│    HttpAccountService 등   │                                     │              ▼                 │
└────────────────────────────┘                                     │  Firestore + Cloud Storage    │
        │ 결과물/프레임 파일                                        └──────────────────────────────┘
        │ (서명 URL 직접 PUT/GET, §5.4)                                     ▲ (규칙: 클라 직접접근 차단)
        └──────────────────────────────────────────────────────────────────┘
                                                                    ┌──────────────────────────────┐
   모바일 다운로드(웹, 무변경) ──── resultSessions 단건 get + 토큰URL GET ─▶│ Firebase Hosting (웹 SPA)    │
                                                                    └──────────────────────────────┘
```

- **웹(모바일 다운로드) 경로는 이 마이그레이션에서 변경 없음**: 웹은 이미 공개 API 키 + 보안 규칙(`resultSessions` 단건 get)만 쓰고 Admin 권한이 없다(`web/public/app.js:191-232`, `web/firestore.rules`). 방향 B는 **WPF의 Admin 권한 제거**가 목적이므로 웹은 무영향.
- **핵심 불변식 유지**: 결과물/프레임 파일은 여전히 **다운로드 토큰 URL**로 접근 가능해야 웹·앱이 파일을 표시한다(`UploadContract.TokenDownloadUrl`, `firebase-contract.md §4.3`). 서버는 업로드 시 이 토큰 메타데이터를 심는 책임을 승계한다(§5.4).

### 3.2 프로젝트 구조 변경

| 항목 | 현재 | 목표 |
|------|------|------|
| `MCPhoto.Core` | 인터페이스·모델·`UploadContract`·`QrService` | **무변경**(계약 안정) |
| `MCPhoto.Firebase` | Admin SDK 구현 5종 | **단계적 제거**(phase 3에서 참조 해제, 어셈블리 삭제 또는 보존). `Google.Cloud.Firestore`/`Storage.V1` 참조 제거 대상 |
| `MCPhoto.Http` (신규) | — | HTTP 구현체(`HttpAccountService`/`HttpFrameRepository`/`HttpFirebaseClient` 등) + HTTP DTO + 인증 토큰 홀더 |
| `MCPhoto.App` | `ServiceRegistration`이 Firebase 등록 | HTTP 등록으로 전환(§5.5), `AppSettings`에 백엔드 URL/자격 추가(§8) |
| `functions/` (신규, 리포 루트 또는 web/) `[USER-DECISION-REQUIRED]` | — | Cloud Functions TS 소스(위치는 사용자 결정: 기존 `web/` 하위 vs 리포 루트 신규) |

---

## 4. 데이터 모델 (변경 없음, 서버 내부로 이동)

Firestore 스키마(`users`/`frameTemplates`/`resultSessions`)와 Storage 경로(`results/{sid}/…`, `frames/{owner}/…`)는 **그대로 유지**한다(`docs/analysis/40-...` 전체). 변경점은 **접근 주체**뿐:

- 현재: WPF Admin SDK가 스키마에 직접 쓴다.
- 목표: **서버 Admin SDK만** 스키마에 쓴다. WPF는 서버 엔드포인트의 JSON DTO만 안다.

**단, 보안 필수 변경 1건 (§7)**: `users.password` 평문 → **해시 필드**(`passwordHash` + `salt`/알고리즘 파라미터). 이는 서버가 저장/검증하므로 WPF DTO에는 비밀번호 해시가 절대 노출되지 않는다.

---

## 5. 클라이언트 교체 전략 (인터페이스별 HTTP 구현 매핑)

> 원칙: **`MCPhoto.Core` 인터페이스 시그니처는 바꾸지 않는다.** 각 메서드 본문만 "Firestore/Storage 직접 호출" → "`HttpClient`로 엔드포인트 호출 + JSON 역직렬화"로 대체. 예외 타입/메시지 계약도 최대한 보존(UI가 `InvalidOperationException`/`UnauthorizedAccessException`을 잡아 메시지를 노출하므로 — `AccountViewModel.cs`, §60 문서).

### 5.1 IFirebaseClient → HttpFirebaseClient `[CODE]`

현재 `IFirebaseClient`(`src/MCPhoto.Core/Upload/IFirebaseClient.cs`)는 저수준 Storage/Firestore 접근을 노출한다. HTTP 전환 시 **책임 재배치**:

| 멤버 | 현재 | 목표 |
|------|------|------|
| `bool IsInitialized` | 서비스 키 로드 여부 | **백엔드 도달 가능 여부**(base URL 설정됨 + 헬스체크/최근 성공). 오프라인 폴백 판정에 계속 사용(§9) |
| `string Bucket` | 버킷명(토큰 URL 조립용) | 서버가 URL을 조립하면 **불필요해질 수 있음**. 단 `UploadContract.TokenDownloadUrl` 클라 조립을 유지하려면 서버가 bucket을 응답에 포함(§5.4) |
| `UploadFileAsync(path, local, type, progress)` | Storage 직접 PUT + 토큰 메타 | **서명 URL 방식(권장, §5.4)**: 서버에서 서명 PUT URL 발급 → 클라가 `HttpClient.PutAsync`로 직접 업로드(진행률 유지) → 다운로드 토큰 URL은 서버가 응답 |
| `CreateResultSessionAsync(session)` | Firestore set | 서버 `POST /uploads/session` 호출 |
| `QueryExpiredSessionsAsync` / `DeleteResultSessionAsync` / `DeleteStoragePrefixAsync` | Firestore/Storage | **제거**(앱 미호출 U3/U4/U5, §2 note). 인터페이스에서 제거하거나 `NotSupportedException`. `[USER-DECISION-REQUIRED]`: 인터페이스 정리 범위(멤버 삭제 vs 유지+미지원) |

> **주의(설계 판단)**: `IFirebaseClient`는 "Firestore+Storage 저수준"이라는 추상화 누수가 있어 HTTP 전환 시 가장 어색하다. 두 가지 선택:
> - (a) **인터페이스 유지 + Http 구현**: 변경 최소, 단 `Bucket`/토큰 URL 개념이 클라에 남음.
> - (b) **`IFirebaseClient`를 `IUploadService` 뒤로 흡수**: `UploadService`가 서버 업로드 API를 직접 호출하고 `IFirebaseClient` 의존 제거. 더 깨끗하나 `UploadService` 리라이트 필요.
> `[CONFIRM]` **기본안 (a)** — phase 최소화. (b)는 후속 리팩터로 분리.

### 5.2 IAccountService → HttpAccountService `[CODE]`

| 메서드 | 엔드포인트(§6.2) | 요청 | 응답/예외 |
|--------|------------------|------|-----------|
| `LoginAsync(id, pw)` | `POST /auth/login` | `{id, password}` | 200 `{token, user{id,role,createdAt}}` → `User`(+토큰 홀더 저장). 401 → `null`(현행 계약: 실패 시 null, `AccountService.cs:44,50`) |
| `CreateAsync(id, pw, role, actingRole)` | `POST /accounts` | `{id, password, role}` (actingRole은 **서버가 토큰에서 도출**, 클라 전달 무시) | 201 `User`. 403 → `UnauthorizedAccessException`(현행 계약, `AccountService.cs:57-59`). 409 → `InvalidOperationException`(중복) |
| `ChangePasswordAsync(id, newPw)` | `PATCH /accounts/{id}/password` | `{newPassword}` | 204. 서버가 "본인 또는 파워" 인가 |
| `GetAllAsync()` | `GET /accounts` | — | 200 `User[]`. 미인가 시 서버 403(현행은 빈 배열 폴백 — §9.2) |
| `DeleteAsync(id)` | `DELETE /accounts/{id}` | — | 204. 서버가 cascade(프레임) 수행(§5.3) |
| `SetRoleAsync(id, role)` | `PATCH /accounts/{id}/role` | `{role}` | 204. admin 전용 서버 인가 |
| `EnsureSeedAccountAsync()` | **제거** | — | 시드는 **서버 배포 시 1회**(§7.3). 클라는 no-op 또는 인터페이스에서 제거 `[USER-DECISION-REQUIRED]` |

- **actingRole 신뢰 이전(보안 핵심)**: 현재는 클라가 `actingRole`을 전달하고 서비스가 검사(`AccountService.cs:57`). HTTP에서는 **클라 전달 actingRole을 무시하고 서버가 JWT의 역할 클레임으로 인가**한다(클라 위조 방어). 인터페이스 시그니처의 `actingRole` 파라미터는 하위호환으로 남기되 서버가 무시.

### 5.3 IFrameRepository → HttpFrameRepository `[CODE]`

| 메서드 | 엔드포인트 | 비고 |
|--------|-----------|------|
| `GetDefaultFramesAsync()` | `GET /frames/default` | 공개(게스트 가능). 응답에 `imageUrl`(토큰 URL) 포함 → 클라가 그 URL로 이미지 직접 GET(P1, 무변경) |
| `GetUserFramesAsync(userId)` | `GET /frames?userId={id}` | 서버가 토큰의 계정과 대조(본인만). **하이브리드 참고**: it8 A2로 user 커스텀 프레임은 이미 로컬 전용, DB엔 공용 기본만(`firebase-contract.md:69`, `docs/analysis/40-...:59`) → 이 엔드포인트는 하위호환 |
| `SaveAsync(frame, imageBytes)` | `POST /frames`(메타) + 서명URL 업로드(이미지) | 파워만(공용 기본 프레임 생성). 10개 제한은 **서버가 재검증**(`FrameRepository.cs:48-54`의 규칙을 서버로 이전). 이미지 업로드는 §5.4 서명 URL |
| `DeleteAsync(frameId)` | `DELETE /frames/{id}` | 서버가 owner 읽어 Storage 이미지 삭제(고아 방지 로직 서버 이전, `FrameRepository.cs:89-99`). bool 반환=존재했는지(404→false) |
| `DeleteAllByUserAsync(userId)` | (서버 내부, 계정 삭제 cascade에 포함) | 클라 직접 호출 불요. `DeleteAsync(account)`가 서버에서 cascade. 인터페이스는 유지하되 `[USER-DECISION-REQUIRED]` 클라 노출 필요 여부 |

### 5.4 IUploadService (거의 재사용) + 업로드 방식 결정 `[CODE]`

`UploadService.UploadResultAsync`(`src/MCPhoto.Firebase/UploadService.cs:24-89`)는 이미 **순수 오케스트레이션**이다: `IFirebaseClient.UploadFileAsync`로 파일 올리고, `UploadContract`로 URL 조립, `CreateResultSessionAsync`로 문서 생성. → **`IFirebaseClient` 구현만 HTTP로 바뀌면 `UploadService` 로직 대부분 그대로 동작.** 진행률(`IProgress<UploadProgress>`, `QrPopupViewModel.cs:82`)·최소1개 불변식(`UploadService.cs:37-38`)·off→null 의미론 전부 보존.

**업로드 파일 전송 방식 `[CONFIRM]` (트레이드오프):**

| 방식 | 흐름 | 장점 | 단점 |
|------|------|------|------|
| **A. 서명 URL(권장)** | 클라 `POST /uploads/prepare`(세션ID·파일목록) → 서버가 GCS **V4 서명 PUT URL** + 다운로드 토큰 발급 → 클라가 서명 URL로 파일 **직접 PUT**(진행률 유지) → 클라 `POST /uploads/commit`으로 resultSession 생성 | 파일 바이트가 함수를 경유하지 않음 → **함수 비용/시간/메모리 최소**, 진행률 자연 유지, 대용량 안전 | 서명 URL 발급·만료(짧게) 관리, 2왕복(prepare/commit) |
| **B. 서버 경유 스트리밍** | 클라가 파일을 함수로 멀티파트 POST → 함수가 Admin SDK로 Storage에 씀 | 단일 왕복, 서명 URL 불요 | 파일이 함수 대역폭·실행시간·메모리 소비 → **비용↑·타임랩스 mp4 대용량 시 타임아웃 위험**. 진행률은 클라→함수 구간만 |

→ `[CONFIRM]` **방식 A(서명 URL) 권장.** 특히 타임랩스 mp4(수 MB~수십 MB) 때문에 B는 함수 비용·타임아웃 리스크가 크다. 다운로드 토큰 URL은 **서버가 서명 URL 발급 시 함께 생성해 응답**(GCS 객체 메타 `firebaseStorageDownloadTokens`를 서버가 설정, 현재 `FirebaseClient.cs:146` 로직을 서버로 이전). 클라는 `UploadContract.TokenDownloadUrl` 조립을 유지하거나 서버가 완성 URL을 응답.

### 5.5 DI 등록 전환 `[CODE]`

`src/MCPhoto.App/ServiceRegistration.cs:66-79`를 다음으로 교체(개념):

```csharp
// 제거: FirebaseClient(Admin) 등록 3줄 (ServiceRegistration.cs:66-73)
// 추가:
services.AddHttpClient("backend", c =>
{
    c.BaseAddress = new Uri(sp.GetRequiredService<ISettingsService>().Current.BackendBaseUrl);
    // 배포 API 키 헤더(게스트 엔드포인트용) — §8
});
services.AddSingleton<IAuthTokenStore, AuthTokenStore>();          // JWT 메모리 보관 + Authorization 주입
services.AddSingleton<IFirebaseClient, HttpFirebaseClient>();
services.AddSingleton<IUploadService, UploadService>();            // 재사용(§5.4)
services.AddSingleton<IQrService, QrService>();                    // 무변경
services.AddSingleton<IFrameRepository, HttpFrameRepository>();
services.AddSingleton<IAccountService, HttpAccountService>();
```

> **주의(실측)**: 현재 `FrameRepository`/`AccountService`는 **구상 `FirebaseClient`**를 직접 주입받아 `internal Firestore`를 공유한다(`ServiceRegistration.cs:66-73`, `AccountService.cs:20-31`, `FrameRepository.cs:19-28`). HTTP 구현은 이 구상 의존을 끊고 `HttpClient`/`IAuthTokenStore`만 의존하도록 재작성한다(더 깨끗한 결합).

---

## 6. 엔드포인트 계약 (요청/응답 스키마 초안)

### 6.1 공통 규약

- 베이스: `https://{region}-{project}.cloudfunctions.net/{fn}` 또는 커스텀 도메인 `[USER-DECISION-REQUIRED]`.
- 인증 헤더: 계정 조작 = `Authorization: Bearer <JWT>`. 게스트 가능(공개 프레임/업로드) = 배포 API 키 헤더(예: `X-MCPhoto-Client`) `[CONFIRM]`.
- 콘텐츠: `application/json; charset=utf-8`. 에러는 `{ "error": { "code": "...", "message": "..." } }` 표준형 `[CONFIRM]`.
- HTTP 상태 ↔ 클라 예외 매핑(현행 계약 보존): 401=로그인 실패→`null`(login만), 403=`UnauthorizedAccessException`, 409=`InvalidOperationException`(중복), 400=입력검증 실패, 404=미존재, 5xx/네트워크=`InvalidOperationException` 또는 재시도.

### 6.2 엔드포인트 목록

| 엔드포인트 | 인증 | 요청 body | 응답 | 매핑(§2) | 태그 |
|-----------|------|-----------|------|----------|------|
| `POST /auth/login` | API키 | `{id, password}` | `{token, expiresIn, user}` \| 401 | A1 | `[CODE]`(fn+클라) |
| `POST /accounts` | Bearer(파워) | `{id, password, role}` | 201 `user` \| 403/409 | A2 | `[CODE]` |
| `GET /accounts` | Bearer(파워) | — | `user[]` | A4 | `[CODE]` |
| `PATCH /accounts/{id}/password` | Bearer(본인/파워) | `{newPassword}` | 204 | A3 | `[CODE]` |
| `DELETE /accounts/{id}` | Bearer(파워, 위계) | — | 204(+cascade) | A5,F5 | `[CODE]` |
| `PATCH /accounts/{id}/role` | Bearer(admin) | `{role}` | 204 | A6 | `[CODE]` |
| `GET /frames/default` | API키 | — | `frame[]`(imageUrl 포함) | F1 | `[CODE]` |
| `GET /frames?userId=` | Bearer(본인) | — | `frame[]` | F2 | `[CODE]` |
| `POST /frames` | Bearer(파워) | `{name, isDefault, imageSize, slots}` → 서명URL 응답 후 이미지 PUT | 201 `frame` | F3 | `[CODE]` |
| `DELETE /frames/{id}` | Bearer(파워) | — | 200 `{deleted:bool}` | F4 | `[CODE]` |
| `POST /uploads/prepare` | API키 | `{sessionId, files:[{kind, ext, contentType}]}` | `{uploads:[{kind, putUrl, downloadUrl}], bucket}` | U1 | `[CODE]` |
| `POST /uploads/commit` | API키 | `{sessionId, finalImageUrl?, timelapseUrl?, retentionHours, downloadPageUrl}` | 201 `resultSession` | U2 | `[CODE]` |
| `GET /health` | 없음 | — | 200 | (IsInitialized 판정) | `[CODE]` |

- `user` 스키마(응답): `{ id, role, createdAt }` — **비밀번호/해시는 절대 응답에 미포함**(현재 `User.Password`는 클라 도메인에 있으나 HTTP에서는 서버가 절대 반환 안 함). `[CONFIRM]`: 클라 `User.Password`는 로그인 응답에서 채우지 않고 빈 값 — UI가 password를 표시하지 않는지 확인 필요(§60 문서상 표시 없음).
- 시드(A7)·만료정리(U3/U4/U6)는 엔드포인트 미노출(§7.3·§9).

---

## 7. 인증·비밀번호 재설계

### 7.1 비밀번호 해싱 (평문 제거, 보안 필수)

- 현재: 평문 저장(`UserDoc.password`, `AccountService.cs:67`)·평문 비교(`AccountService.cs:50`). **웹 차단이 유일 방어선**이라 키 유출 시 전량 노출.
- 목표: **서버가 `bcrypt` 또는 `scrypt`로 해시** `[CONFIRM]`(Node `bcrypt`/`argon2` 라이브러리). Firestore `users` 문서: `password`(평문) 제거 → `passwordHash` + 알고리즘 파라미터.
- 로그인: 서버가 `users/{id}` 로드 → 해시 검증 → 성공 시 JWT 발급. **클라는 해시를 절대 보지 않는다.**
- **마이그레이션(기존 평문 계정)** `[USER-DECISION-REQUIRED]`:
  - (a) 배포 시 스크립트로 기존 평문 → 해시 일괄 변환(서버/콘솔 1회).
  - (b) 로그인 성공 시 lazy 재해싱(평문 필드 있으면 검증 후 해시로 교체·평문 삭제).
  - 베타 데이터가 시드(`devmcjo`)뿐이면 (a) 단순. 실제 계정 수 확인 필요.

### 7.2 토큰 발급·검증 `[CONFIRM]`

- 로그인 성공 → 서버가 **JWT**(claims: `sub`=id, `role`, `iat`, `exp`) 서명(HS256, 시크릿은 §8; 또는 RS256 키쌍). 만료 15~60분 `[USER-DECISION-REQUIRED]`.
- 이후 요청: `Authorization: Bearer`. 서버 미들웨어가 서명·만료·역할 검증.
- 리프레시 `[USER-DECISION-REQUIRED]`: (a) 리프레시 토큰 도입(장시간 세션) vs (b) 만료 시 재로그인(키오스크는 재로그인 부담 적음, 단순). 현재 앱은 "촬영 후 로그인 유지"(it5 B8, §60 문서 3.4)라 파워 계정이 오래 로그인 → 짧은 JWT면 재로그인 잦음 → 리프레시 또는 중간 만료(예: 8h) 검토.
- **대안(트레이드오프) — Firebase Authentication 이전**: `users` 자체 인증을 Firebase Auth(이메일/비번 또는 커스텀 토큰)로 이전하면 토큰 검증·비번 해싱을 Firebase가 대행. 장점: 검증·해싱 위임, 규칙에서 `request.auth` 사용. 단점: **기존 id/pw·역할(`users.role`) 모델 대수술**, 커스텀 클레임으로 역할 주입 필요, 게스트 흐름과의 결합 복잡. → 방향 B의 "서버가 DB 대신 조작" 목표엔 **자체 JWT가 더 직결**. Firebase Auth는 후속 옵션으로 병기.

### 7.3 시드 계정 서버 내부화

- 현재: 앱 시작 시 `EnsureSeedAccountAsync` fire-and-forget(`App.xaml.cs:74`). Admin 권한으로 시드 upsert.
- 목표: 시드는 **서버 배포 시 1회 부트스트랩**(배포 스크립트/함수 초기화 또는 콘솔에서 수동 1회). 클라는 관여 안 함. 오프라인 인메모리 시드(§9)는 별도 유지 검토.

---

## 8. 클라이언트 설정·시크릿

### 8.1 클라이언트가 보유하는 값 (공개 가능 only)

`AppSettings`(`src/MCPhoto.Core/Settings/AppSettings.cs`)에 추가 `[CODE]`:

| 신규 설정 | 예시 | 성격 |
|-----------|------|------|
| `BackendBaseUrl` | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net` | 공개(엔드포인트 주소) |
| `ClientApiKey` `[CONFIRM]` | 배포별 발급 문자열 | **반(半)비밀** — 게스트 엔드포인트 게이트. exe에 상주(트레이드오프, §1.3). 유출 시 서버에서 해당 키만 폐기 |

- **제거**: `StorageBucket`(`AppSettings.cs:121`)은 서버가 관리 → 클라 불요(또는 토큰 URL 조립 유지 시 서버 응답으로 대체). `HostingBaseUrl`(`AppSettings.cs:114`)은 downloadPageUrl 조립에 여전히 필요(웹 URL) → 유지.
- **serviceAccountKey.json 완전 제거**: `FirebaseClient` 키 로드 경로(`FirebaseClient.cs:47-121`)·`publish.ps1`의 키 동봉(`publish.ps1:89-122`) 전부 삭제(§10 phase).

### 8.2 서버 시크릿 (Cloud) `[CONSOLE]`

- JWT 서명 시크릿, (도입 시) 배포 API 키 목록 → **Google Secret Manager** 또는 Functions 환경 구성(`firebase functions:secrets:set`). **코드/리포에 하드코딩 금지.**
- Admin 자격은 **시크릿조차 아님** — ADC로 런타임 자동 주입(키 없음).

---

## 9. 오프라인·비용·지연

### 9.1 오프라인 동작 재설계

현재 오프라인 폴백(키 없음/미초기화 = `IsInitialized=false`): 기본 프레임 빈 배열·업로드 예외→로컬 보존·**시드 인메모리 로그인**(`AccountService.cs:36-44`, `docs/analysis/30-...:237-254`).

- HTTP 전환 후 "오프라인" = **백엔드 도달 불가**(네트워크/함수 다운). `IsInitialized`를 "백엔드 도달 가능"으로 재정의(§5.1).
- 폴백 유지: 업로드 실패 시 QR off·로컬 보존(`QrPopupViewModel.cs:100-111`)은 예외 경로 그대로 동작. 기본 프레임 조회 실패 시 로컬 캐시(it8 A2, `ILocalFrameStore`, `ServiceRegistration.cs:82-83`)로 폴백 — **이미 로컬 캐시 존재**하므로 프레임 게스트 흐름은 오프라인에도 동작.
- **오프라인 시드 로그인 처리** `[USER-DECISION-REQUIRED]`: 현재 `devmcjo/1111` 인메모리 admin 로그인(`AccountService.cs:38-42`)은 **비번 검증이 클라에**. HTTP 전환의 취지(클라에 인증 로직 0)와 상충. 선택:
  - (a) **제거** — 오프라인 시 관리 기능 불가(권장, 보안 일관). 게스트 촬영은 로컬 캐시로 계속 가능.
  - (b) 유지하되 관리 기능은 온라인 필수(로그인만 오프라인 허용) — 절충.
  - 현재 시드 비번 `1111`은 약함 — (a) 권장.

### 9.2 GetAllAsync 등 "빈 배열 폴백" 재검토

현재 미인가/오프라인 시 `GetAllAsync`가 빈 배열(`AccountService.cs:81`). HTTP에서 403(미인가)과 오프라인(네트워크)을 구분해야 UI 오해 없음 `[CONFIRM]`: 403은 예외로, 네트워크 실패는 빈 목록+안내.

### 9.3 비용 (Blaze)

- Functions 2nd gen: 호출·컴퓨트·아웃바운드 과금. **서명 URL 방식(§5.4-A)이면 파일 바이트가 함수를 안 거쳐 비용 최소.** 예상 호출: 촬영당 login(선택)+prepare+commit ≈ 2~3콜, 프레임 조회 캐시로 감소.
- Firestore: 현행과 동일(읽기/쓰기 카운트 변화 미미 — 서버가 대신 수행할 뿐).
- **Always Free 한도**(Functions 2M 호출/월 등) 내 소규모 키오스크는 $0 근접 예상 **가정** — 실제 트래픽으로 검증 필요.
- 이미 Storage=Blaze 필수(`firebase-contract.md:28`, `docs/analysis/30-...:73-77`)라 요금제 전환 부담 없음(이미 Blaze 전제).

### 9.4 지연

- 업로드가 login→prepare→PUT→commit 다단계 → 왕복 지연 증가. 서울 리전 배치로 완화. 진행률 UI(`QrPopupViewModel`)가 이미 있어 UX 흡수. 프레임 조회는 로컬 캐시(it8 A2)로 체감 지연 최소.

---

## 10. 마이그레이션 단계 (Phase) + 롤백

> 베타(현행 키 포함, `publish.ps1`)를 깨지 않고 점진 전환. **Phase는 개요만 — 상세 self-contained 단계는 확정 후 WBS로.**

| Phase | 내용 | 롤백 | 태그 |
|-------|------|------|------|
| **P0. 준비** | 백엔드 프로젝트/리전/시크릿 결정, `functions/` 스캐폴드, 로컬 Emulator로 Functions 개발 | 코드 미배포 상태 유지 | `[CONSOLE]`+`[CODE]` |
| **P1. 서버 구현** | §6 엔드포인트 전체 구현(Emulator 검증), 해시·JWT·서명URL. **아직 배포/전환 안 함** | 미배포 | `[CODE]` |
| **P2. 서버 배포(병행)** | Functions 배포. 기존 WPF(Admin)는 **그대로 동작**(서버와 공존, 데이터 동일 스키마). | 함수 삭제 | `[CONSOLE]` |
| **P3. 클라 HTTP 구현 + 스위치** | `MCPhoto.Http` 구현, DI를 HTTP로 전환(§5.5). **feature flag/설정로 Admin↔HTTP 토글** 가능하게 `[CONFIRM]` → 검증 후 HTTP 고정 | DI를 Firebase(Admin)로 되돌림 | `[CODE]` |
| **P4. 비번 해시 마이그레이션** | 기존 평문 → 해시(§7.1). 로그인 검증 전환 | 평문 검증 경로 복구(단기) | `[CONSOLE]`/`[CODE]` |
| **P5. 규칙 강화·직접접근 차단** | Firestore/Storage 규칙에서 **인증 클라이언트 write 경로도 제거**(웹은 이미 deny). 서버(Admin=규칙우회)만 씀 → 규칙은 전면 deny 유지로 충분. **직접 DB 접근이 서버뿐임을 확인** | 규칙 되돌림 | `[CONSOLE]` |
| **P6. 키 폐기(회전)** | publish에서 키 동봉 제거(`publish.ps1:89-122`), 배포된 exe 폴더 키 삭제, **서비스 계정 키 콘솔에서 회전/폐기**(유출 전제 폐기) | (되돌릴 수 없음 — P3 검증 완료 후에만) | `[CONSOLE]`+`[CODE]` |

- **되돌릴 수 없는 지점**: P6 키 폐기. P3 전환이 프로덕션에서 안정 검증된 뒤에만 P6 수행. 그 전까지 키는 살아있어 롤백 가능.
- **핵심 순서 불변식**: 클라 전환(P3) → 검증 → **그 다음** 키 폐기(P6). 키를 먼저 죽이면 현행 앱이 즉시 마비.

---

## 11. 보안·위협 모델 요약

| 위협 | 현재 | 목표(B) |
|------|------|---------|
| exe 폴더 유출 → DB 전권 | **발생**(Admin 키 동봉) | **차단**(클라에 Admin 키 0. 최악=배포 API 키 유출→해당 키만 폐기, DB는 서버 인가에 종속) |
| 평문 비밀번호 유출 | 키 유출 시 전량 노출 | 해시만 저장, 클라·응답에 미노출(§7.1) |
| actingRole 위조(권한 상승) | 클라 전달값 검사(서비스가 강제하나 클라가 Admin이면 무의미) | 서버가 JWT 역할로 인가, 클라 전달 무시(§5.2) |
| 토큰 열거(resultSessions) | 규칙 list deny(웹) | 무변경(웹 경로) |
| 무단 업로드/스팸 | Admin이면 무제한 | 서명 URL 만료·API키·(선택)레이트리밋으로 제한 |
| CORS | — | **WPF는 브라우저 아님 → CORS 불필요**. 단 **웹(js) 다운로드 페이지는 Functions 미호출**(Firestore SDK 직접) → Functions에 웹 CORS 불요. `[CONFIRM]` 관리 콘솔을 웹으로 만들 계획 없으면 CORS 미설정 |
| 입력 검증 | Admin 신뢰 | 서버가 전 필드 검증(id 형식·역할 화이트리스트·슬롯 1~6·10개 제한·파일 크기/타입) |
| 레이트리밋 `[USER-DECISION-REQUIRED]` | 없음 | 로그인 브루트포스 방어(§60 문서 5: 현재 시도제한 없음) — 서버 IP/계정별 제한 검토 |
| 다운로드 토큰 URL | capability(무변경) | 서버가 발급, 만료는 여전히 resultSessions TTL(파일 토큰 자체는 무기한 — 현행 유지) |

---

## 12. 파일 인코딩·관례 (구현 시 필수 준수)

- **기존 `.cs` 파일은 UTF-8 (BOM 없음)** — 실측 확인(`FirebaseClient.cs`/`ServiceRegistration.cs`/`UploadContract.cs` 선두 바이트 `75 73 69`, BOM 부재). 한글 주석 포함. 수정·신규 `.cs`는 **UTF-8 no BOM** 유지.
- 신규 `MCPhoto.Http` 어셈블리는 기존 프로젝트 스타일(nullable enable, file-scoped namespace, XML doc 한글 주석) 따를 것.
- TS Functions는 웹(`web/`) 관례(ESM, 버전 pin) 참조 — js-architect와 정합 `[USER-DECISION-REQUIRED]`(Functions 소스 위치·소유).

---

## 13. 미해결 결정 사항 집계

### 13.1 `[CONFIRM]` (사용자 리뷰 시 조정 — 기본안 있음)

1. 백엔드 = Cloud Functions 2nd gen + TS (vs Cloud Run) — §1.2
2. 엔드포인트 = REST `onRequest` (vs callable) — §1.2
3. 클라→서버 인증 = 로그인 JWT + (게스트용) 배포 API 키 혼합 — §1.3
4. 업로드 = 서명 URL 방식 A (vs 서버 스트리밍 B) — §5.4
5. `IFirebaseClient` 유지+Http 구현 (vs UploadService 흡수) — §5.1
6. 비번 해시 = bcrypt/argon2 — §7.1
7. `IHttpClientFactory` 도입 — §1.1
8. `ClientApiKey`를 AppSettings에 — §8.1
9. P3에 Admin↔HTTP feature flag — §10
10. 에러 응답 표준형·403/오프라인 구분 — §6.1·§9.2

### 13.2 `[USER-DECISION-REQUIRED]` (설계자가 못 정하는 순수 판단)

1. Functions 리전(서울?) — §1.2
2. Functions 소스 위치·소유(web/ 하위 vs 리포 루트, js팀 vs wpf팀) — §3.2·§12
3. JWT 만료·리프레시 토큰 도입 여부(키오스크 장시간 로그인) — §7.2
4. 기존 평문 계정 마이그레이션 방식 (일괄 vs lazy) / 실제 계정 수 — §7.1
5. 오프라인 시드 로그인 제거 vs 유지 — §9.1
6. `IFirebaseClient`/`IAccountService`의 미사용 멤버(U3/U4/U5, EnsureSeed, DeleteAllByUser) 인터페이스 정리 범위 — §5.1·§5.2·§5.3
7. 커스텀 도메인 사용 여부 — §6.1
8. 레이트리밋 정책 — §11

---

## 14. 완료 보고용 핵심 요약

- **엔드포인트**: auth/login, accounts(CRUD·password·role), frames(default·user·save·delete), uploads(prepare·commit), health — §6.2 (총 12개).
- **클라 교체 요지**: `MCPhoto.Core` 인터페이스 5종 **시그니처 불변**, `MCPhoto.Http` 신규 구현체가 `HttpClient`로 엔드포인트 호출, DI 3~4줄 교체(§5.5) → **UI/뷰모델/내비게이션 전부 무변경**. `UploadService`/`QrService`는 재사용.
- **CODE/CONSOLE 분리**: 함수 소스·WPF HTTP 계층=`[CODE]`. 함수 배포·IAM/ADC·시크릿·리전·규칙 강화·키 회전·평문 마이그레이션 실행=`[CONSOLE]` (런북은 WBS 단계화 시 명령·확인 포함 작성).
- **CONFIRM/USER-DECISION**: §13 (CONFIRM 10건, USER-DECISION 8건).
- **권장 단계 순서**: P0 준비 → P1 서버구현(Emulator) → P2 서버배포(공존) → P3 클라 HTTP 전환(+flag) → P4 비번 해시 → P5 규칙 강화 → **P6 키 폐기(최후·불가역)**. 키 폐기는 반드시 HTTP 전환 검증 후.

---

## 관련 문서

- `docs/design/firebase-contract.md` — WPF↔웹 계약(스키마·토큰 URL·규칙). 본 마이그레이션에서 **웹 경로·스키마 불변**.
- `docs/analysis/30-backend-firebase-integration.md` — 현재 Firebase 연동 실측(초기화·인증·업로드·폴백).
- `docs/analysis/40-database-firestore-and-storage-schema.md` — 스키마·경로·규칙·TTL 실측.
- `docs/analysis/60-auth-accounts-and-roles.md` — 역할 위계·권한 매트릭스·평문 비번 실측.
