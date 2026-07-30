# MC포토 — Firebase 인터페이스 계약 (파이프라인 간 계약 문서)

| 항목 | 값 |
|------|-----|
| 문서 성격 | **파이프라인 간 인터페이스 계약** — WPF(생산자) ↔ 웹 다운로드 페이지(소비자) |
| 목적 | js-architect가 **이 문서만 읽고** 웹 측(다운로드 페이지·Hosting·보안 규칙·TTL 정리)을 설계할 수 있도록 스키마·경로·URL·규칙·TTL을 확정 |
| 대상 PRD | `docs/prd/photobooth-prd.md` v2.7 (§6 데이터 모델, §7 백엔드, §10 보안, §9 #18/#33/#34) |
| 관련 | `docs/design/wpf-architecture.md` §6 (WPF 접근 전략) |
| 작성일 | 2026-07-20 |
| 상태 | 계약 v1 (확정) |

---

## 0. 역할 분담 (누가 무엇을 하는가)

| 주체 | 역할 | 접근 방식 |
|------|------|----------|
| **WPF 앱 (생산자)** | User·FrameTemplate CRUD, 파일 업로드(최종 이미지+타임랩스), ResultSession 문서 생성, downloadPageUrl 산출, QR 생성, 만료 삭제(1차) | 신뢰 클라이언트로 동작(서비스 계정 또는 규칙 준수 클라이언트) |
| **웹 다운로드 페이지 (소비자)** | QR 스캔 진입 → ResultSession **단건 read** → 사진·영상 프리뷰 + 다운로드 버튼 | Firebase JS SDK, **보안 규칙에 종속**(공개 API 키) |
| **Firebase** | Firestore(메타) + Storage(파일) + Hosting(웹 배포) | — |

> **핵심 불변식**: 웹은 **읽기 전용 소비자**다. 웹은 절대 User/FrameTemplate를 읽지 않고, ResultSession도 **토큰 ID로 단건 get만** 한다(list/query 금지). 파일은 문서에 담긴 URL로 브라우저가 직접 fetch한다.

---

## 1. 요금제 전제 (js-architect 필독)

- **Cloud Storage는 Blaze(종량제) 필수** — 2026-02-03부로 무료 Spark 요금제는 Storage 접근 불가(402/403). 사진·영상 파일은 Storage에 저장되므로 프로젝트가 **Blaze로 전환되어 있어야** 다운로드 페이지가 파일을 제공할 수 있다. (근거: `firebase.google.com/docs/storage/faqs-storage-changes-announced-sept-2024`)
  - Always Free 한도 내 $0 유지 가능(카드 등록만 강제).
  - 레거시 `*.appspot.com` 버킷: 무료 임계(5GB 저장/1GB day 다운로드 등). 신규 `*.firebasestorage.app` 버킷: 일부 리전 Always Free.
- **Firestore는 Spark 무료 유지**(1GiB, 읽기 5만/쓰기 2만/삭제 2만 per day, egress 10GiB/월).
- **Hosting**: Spark 무료(저장 1GB, 전송 10GB/월).
- **⚠️ QR 전송 off 완화 경로**: WPF는 `enableQrDelivery=off` 시 업로드·QR을 생략하고 로컬 저장만 한다. 이 경우 **ResultSession 문서·Storage 파일이 생성되지 않으며 웹 다운로드 페이지도 사용되지 않는다**. 웹 설계는 "F5 on일 때만 활성"임을 전제로 한다.

---

## 2. Firestore 컬렉션 스키마

> Firestore 문서 필드 타입 표기: `string`, `int`, `bool`, `timestamp`, `map`, `array<map>`.
> 컬렉션 ID는 아래 확정값을 사용한다(WPF·웹 공통).

### 2.1 `users` 컬렉션 (문서 ID = 계정 id 또는 자동 ID)

> **it15 갱신**: 비밀번호 개념 폐지. 자격증명은 ① Google SSO(신원) + ② `pinHash`(설정·계정 관리 진입 게이트)
> 두 가지뿐이다. `password`·`emailVerified` 필드는 삭제됐다(설계 `wpf-it15-google-only-auth-design.md` §5.3).

| 필드 | 타입 | 설명 |
|------|------|------|
| `id` | string | 계정 ID (문서 ID와 동일). Google email의 local-part에서 파생, 충돌 시 `-2`/`-3` suffix |
| `role` | string | `"temp_user"` / `"user"` / **`"advanced_user"`** / `"manager"` / `"admin"` (it16) |
| `createdAt` | timestamp | 생성 시각. TempUser 시간 한도의 기준점 |
| `email` | string | Google 계정 이메일(소문자 정규화). SSO 신원의 근거 — 항상 존재 |
| `authMethod` | string | 인증 제공자. 현재 `"google"` 고정(추후 `"kakao"`/`"apple"` 확장) |
| `pinHash` | string? | 진입 PIN(4자리)의 bcrypt 해시. 미설정 시 필드 부재. **응답에 절대 미포함** |
| `qrUsedCount` | int? | TempUser QR 전송 성공 세션 누적 수. 미설정=0 |

- **부트스트랩**: 신규 SSO 계정은 무조건 `role:"temp_user"`로 생성된다. 최초 admin(`devmcjo`)은
  마이그레이션 스크립트 `web/functions/scripts/migrate-google-only-accounts.mjs`가 만든다(HTTP API로는 admin 지정 불가).
- **웹 접근**: **전면 차단**(read/write 모두 deny). 웹은 users를 절대 읽지 않는다.

> **it16 갱신**: 역할 `"advanced_user"`(UI 표기 "고급 유저")가 추가됐다. 위계는
> `temp_user`(0) < `user`(1) < **`advanced_user`(2)** < `manager`(3) < `admin`(4)이며 랭크는 `domain/roles.ts`의
> `MANAGE_RANK`(C# `ManageRank`와 1:1)로 명시한다. **스키마·인덱스 변경 없음**(허용값 1개 추가, 마이그레이션 불필요).
>
> - **`isPower`는 확장되지 않았다** — power는 계속 `manager`·`admin`만이다. `advanced_user`는 프레임을 저작할 수 있으나
>   그 권한은 **클라 측 별개 축**(C# `CanWriteFrames`)이며 개인 로컬 저장 전용이다. 따라서 프레임 쓰기 라우트
>   (`POST /frames`, `PUT /frames/:id`, `DELETE /frames/:id`)는 계속 `requirePower()` 뒤에 있고 `advanced_user` 이하는 **403**이다.
> - **계정 라우트 게이트**: `PATCH /accounts/:id/role` = `requirePower()` + `canSetRole(actor, current, target)`
>   (manager는 하위 3역할 대역 `temp_user`·`user`·`advanced_user` 안에서 자유 지정, manager·admin 지정은 admin 전용,
>   admin 대상 변경·admin 지정은 누구도 불가). `DELETE /accounts/:id` = `requirePower()` + `canManage`.
>   **`PUT /accounts/:id/pin`(타 계정 PIN 재설정)은 `requirePower()` + `canResetPin`이다** — 판정식 =
>   `canResetPin(actor.role, targetRole) && actor.id !== targetId`이고 `canResetPin`은 **`isPower(actor) &&
>   MANAGE_RANK[target] < MANAGE_RANK[actor]`**(엄격 부등호 = **동급 차단**). 즉 manager는 다른 manager의 PIN을
>   재설정할 수 없고 **매니저 PIN은 admin만** 재설정한다(admin↔admin도 차단). 비power·동급·상위 대상은 **403**,
>   자기 자신 대상은 계속 **400**(본인은 `PUT /accounts/me/pin`).
>   `canManage` 자체의 의미는 **불변**이다(`deleteAccount`와 공유되므로 좁히면 admin↔admin·manager↔manager 삭제가 회귀)
>   → 삭제는 동급 허용, PIN 재설정만 동급 차단인 비대칭이 의도된 현재 상태다.
> - 미지원 `role` 문자열은 `parseRole`이 `user`로 폴백한다(it16 이후 `user`는 프레임 쓰기 권한이 없어 fail-closed 방향).
> - 상세: 설계 `docs/design/wpf-it16-advanced-user-role-design.md` §3·§5, 역할 전수 표 `docs/analysis/60-auth-accounts-and-roles.md` §1.4.

**클라 응답(`UserResponse`) 와이어 형식** — it15 설계 §9.1에서 동결:

```json
{ "id": "devmcjo", "role": "admin", "createdAt": "2025-11-02T08:31:00.000Z",
  "email": "devmcjo@gmail.com", "authMethod": "google", "hasPin": true }
```

`hasPin`은 `pinHash != null` 파생값이며 해시 원문은 어떤 응답에도 실리지 않는다.

### 2.2 `frameTemplates` 컬렉션 (문서 ID = 자동 ID)

| 필드 | 타입 | 설명 |
|------|------|------|
| `id` | string | 프레임 ID(문서 ID와 동일 권장) |
| `userId` | string \| null | 소유 계정 id. **기본 프레임은 `null`** |
| `isDefault` | bool | 공용 기본 프레임 여부(true면 게스트에게도 노출) |
| `name` | string | 프레임 이름 |
| `imageUrl` | string | 프레임 이미지 다운로드 URL (§4 토큰 URL 형식, Storage `frames/{userId}/…`) |
| `imageSize` | map `{ width:int, height:int }` | 등록 원본 픽셀 크기 |
| `slots` | array<map> | `{ index:int, x:int, y:int, width:int, height:int }` 1~6개. 프레임 픽셀 좌표계 |
| `createdAt` | timestamp | 생성 시각 |

- 계정당 최대 10개(커스텀). 기본 프레임(isDefault=true, userId=null)은 별개.
- **웹 접근**: **전면 차단**. 웹 다운로드 페이지는 프레임 목록을 다루지 않는다(§10 #33). *(프레임 관리는 WPF 전용)*
- **it15 F1 — 프레임 편집은 로컬 전용**: WPF 앱은 **`PUT /frames/{id}` 라우트를 호출하지 않는다**. 프레임 편집은 해당 PC에만 적용되며, DB/번들 유래 프레임을 편집하면 원본을 보존하고 `{원본이름} 사본`으로 로컬에 분기 저장한다(`docs/design/wpf-it15-frame-ux-design.md` §3.2). ⚠️ 서버 라우트(`web/functions/src/routes/frames.ts`)는 `frameTemplates` 문서를 갱신하는 유일한 API 경로로 **유지**되지만 **운영/관리 도구 전용**이다 — 앱 동작을 근거로 이 라우트를 제거하지 말 것. 앱이 `frameTemplates`에 쓰는 경우는 **파워의 프레임 신규 생성(`POST /frames`)뿐**이다.
- **it8 A2 저장 하이브리드**: `frameTemplates`(DB)에는 **공용 기본 프레임(isDefault=true, userId=null)만** 저장한다(파워=admin/manager가 생성). **일반 user 커스텀 프레임은 DB에 저장하지 않고 WPF 로컬 전용**(`%ProgramData%\MCPhoto\Frame\{계정}_{이름}.png` + `.slots`)이다. 따라서 `userId != null` 문서는 신규 생성되지 않는다(기존 문서는 하위호환 유지). 파워 프레임은 로컬에도 캐시(`Frame/default/{frameId}.png`)해 재다운로드를 피한다. `frameTemplates`는 웹 접근이 없어 이 변경은 웹·보안 규칙에 영향 없음. 계정당 10개 제한은 **user는 로컬 파일 수**로, 파워 DB 프레임은 별개.

### 2.3 `resultSessions` 컬렉션 (문서 ID = **추측 불가 토큰**, §3.3)

| 필드 | 타입 | 설명 |
|------|------|------|
| `id` | string | 세션 ID = 문서 ID = **URL 토큰** = Storage 폴더명. 형식 `{yyyyMMdd_HHmmss}_{UUIDv4}`(순차 ID 금지·UUID로 열거 방어 유지, §3.3) |
| `finalImageUrl` | string \| null | 프레임 포함 최종 이미지 URL (§4, Storage `results/{sessionId}/…`). **사진 전송 옵션(SendPhoto) off면 null**(it7 F2) |
| `timelapseUrl` | string \| null | 타임랩스 영상 URL. 타임랩스 전송 옵션 off·생성 실패·미포함 시 null |
| `createdAt` | timestamp | 생성 시각 |
| `expiresAt` | timestamp | `createdAt + retentionHours`(기본 24h, 범위 1~72h). **자동 삭제 기준** |
| `downloadPageUrl` | string | 모바일 다운로드 페이지 URL(QR 인코딩 대상, §3) |

- **웹 접근**: **토큰 ID 단건 get만 허용**. list/query **금지**(§10 #33). 웹은 URL의 토큰으로 `doc(resultSessions/{token})`을 get하고, 성공 시 `finalImageUrl`/`timelapseUrl`로 파일을 표시한다.
- **만료 처리**: 웹은 `expiresAt < now` 또는 문서 부재(삭제됨) 시 **만료 안내 페이지** 표시(§3.4). 문서에 별도 `expired` 플래그는 두지 않음 — `expiresAt` 비교 + 문서 존재 여부로 판단.
- **미디어 URL null 의미론(it7 F2, 추론 방식)**: **미만료 문서**에서 `finalImageUrl`/`timelapseUrl`이 null이면 해당 미디어는 **전송 옵션이 꺼진 것**(의도적 제외 — 만료·로드 실패가 아님). 웹은 이를 만료(문서 부재/expiresAt 초과)·로드 실패(URL 있는데 fetch 실패)와 **구분**해 "전송 옵션 꺼짐" 안내를 표시한다. `photoSent`/`timelapseSent` 같은 명시 플래그는 **추가하지 않는다**(계약 변경 최소, "doc 존재+미만료+URL null" 추론으로 충분).
- **최소 1개 불변식**: 미만료 `resultSessions` 문서는 `finalImageUrl`·`timelapseUrl` 중 **최소 1개는 non-null**이다. 둘 다 off면 WPF 연동 규칙(QrDeliveryPolicy)에 의해 `enableQrDelivery`가 off로 정규화되어 **문서 자체가 생성되지 않는다**. 웹은 방어적으로 둘 다 null인 경우도 처리(안내 2개 표시, 만료로 오판하지 않음).

---

## 3. 다운로드 페이지 URL & 토큰 규칙

### 3.1 URL 형식 (확정)

```
https://{hostingDomain}/d/{token}
```
또는 쿼리 방식(웹 라우팅 편의에 따라 js-architect 택1, WPF는 **아래 §3.5 규약으로 생성**):
```
https://{hostingDomain}/?s={token}
```

- `{hostingDomain}`: Firebase Hosting 도메인(예: `mcphoto-xxxx.web.app` 또는 커스텀 도메인). **WPF 설정값으로 주입**(`AppSettings`에 hosting base URL 보관 → 배포 환경별 교체).
- `{token}`: `resultSessions` 문서 ID = **`{yyyyMMdd_HHmmss}_{UUIDv4}`**(UUID로 추측 불가, §3.3). 이 토큰이 곧 세션 식별자이자 접근 열쇠.

> **js-architect 결정 사항**: 경로형(`/d/{token}`)이면 Hosting rewrite로 SPA에 라우팅, 쿼리형(`/?s={token}`)이면 단일 index에서 파싱. **어느 쪽이든 WPF는 §3.5의 조립 규칙으로 downloadPageUrl을 만든다.** js-architect는 택일 후 이 문서에 확정 표기를 남긴다(현재 기본안: **쿼리형 `/?s={token}`** — 단일 정적 페이지로 가장 단순).
>
> **확정: 쿼리형 `/?s={token}` (D-1, js-architect)** — WPF는 §3.5 쿼리형 조립 규칙(`{hostingBaseUrl} + "/?s=" + {token}`)을 그대로 사용한다.

### 3.2 QR 인코딩 대상

- QR에 인코딩되는 문자열 = `downloadPageUrl`(위 URL 전체). WPF가 QRCoder로 생성해 결과 화면 QR 팝업에 표시.

### 3.3 토큰 규칙

- **생성 주체**: WPF. 세션 ID = `{yyyyMMdd_HHmmss}_{UUIDv4}`(`UploadContract.NewSessionId`, 로컬 시간). 이 값이 곧 `resultSessions` 문서 ID · Storage `results/` 하위 폴더명 · URL 토큰이다. 앞의 날짜_시간은 **Storage에서 세션 폴더를 시각으로 정렬·검색**하기 위함(사용자 요청).
- **추측 불가(열거 방어)**: **순차 ID는 여전히 금지**. 날짜_시간은 **prefix일 뿐이고 뒤에 완전한 UUIDv4(122비트)가 붙으므로 열거 불가**는 그대로 유지된다(공격자는 UUID를 브루트포스해야 함 — 접두 시각으로 좁혀지지 않음). 트레이드오프: 토큰/URL에 **생성 시각이 노출**된다(포토부스 다운로드 링크 특성상 저민감 — 사용자 수용).
- **접근 제어**: 토큰을 아는 사람만 문서 get 가능(보안 규칙이 list를 막으므로 열거 불가). 즉 **토큰 = capability URL**.
- **자동삭제 정합**: 세션 ID가 폴더명·문서 ID·삭제 prefix를 모두 겸하므로, 만료 정리(`PurgeExpired`가 `results/{id}/` 삭제 + 문서 삭제)는 ID 형식 변경과 무관하게 그대로 동작한다.

### 3.4 만료·삭제 시 웹 동작

- `expiresAt < now`: 만료 안내 페이지("보관 기간이 지나 만료되었습니다").
- 문서 부재(삭제 완료): 동일하게 안내 페이지(get이 not-found).
- 파일 부재(문서는 있으나 Storage 파일 삭제됨): 이미지/영상 로드 실패 → 안내 페이지 또는 개별 실패 표시.

### 3.5 WPF의 downloadPageUrl 조립 규칙 (계약)

```
downloadPageUrl = {hostingBaseUrl} + "/?s=" + {token}      // 쿼리형(기본안)
downloadPageUrl = {hostingBaseUrl} + "/d/" + {token}       // 경로형(대안)
```
- `hostingBaseUrl`은 WPF `AppSettings`에 설정(트레일링 슬래시 제거 후 조립).
- 이 조립 결과를 `resultSessions.downloadPageUrl`에 그대로 저장하고 QR로 인코딩한다. **웹은 자신의 URL을 이 규약과 일치시킨다.**

---

## 4. Cloud Storage 경로 규칙 & 파일 URL

### 4.1 경로 분리 (확정, §7 · §9 #33)

| 용도 | 경로 | TTL 대상 | 웹 read |
|------|------|---------|---------|
| 결과물(최종 이미지·타임랩스) | `results/{sessionId}/…` | **O**(만료 삭제) | 토큰 URL로 fetch |
| 프레임 이미지 | `frames/{userId}/…` | **X**(비대상) | 접근 없음(WPF 전용) |

- 만료 정리 작업은 **`results/`만 스캔**한다. `frames/`는 건드리지 않는다.

### 4.2 결과물 파일명 규약 (계약)

```
results/{sessionId}/final.{jpg|png}       // 최종 합성 이미지 (outputFormat 반영)
results/{sessionId}/timelapse.mp4         // 타임랩스 (H.264, 무음)
```
- **`{sessionId}` = `{yyyyMMdd_HHmmss}_{UUIDv4}`(§3.3)** → results/ 하위 세션 폴더가 **시각순 정렬·검색**된다(사용자 요청). 파일명 자체는 고정(`final`/`timelapse`).
- 확장자: 이미지는 `AppSettings.outputFormat`(jpg/png). 영상은 항상 `mp4`.
- 웹은 파일명을 하드코딩하지 않고 **문서의 `finalImageUrl`/`timelapseUrl`을 사용**한다(파일명 변경 내성).

### 4.3 파일 다운로드 URL 형식 (확정)

**Firebase 다운로드 토큰 URL** 채택:
```
https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}
```
- `{urlEncodedPath}`: 예 `results%2F{sessionId}%2Ffinal.jpg` (슬래시 `%2F` 인코딩).
- `{downloadToken}`: 업로드 시 자동 생성되는 UUID(파일별). 이 토큰이 있어야 URL로 read 가능.
- **웹이 파일을 읽는 방식**: 브라우저가 이 URL을 직접 GET(img src / video src / a href). **방문자 인증·Storage read 규칙 불필요** — 토큰 URL 자체가 capability.
- **js-architect 참고**: 웹은 Storage SDK로 파일을 다시 조회할 필요 없다. 문서의 URL 문자열만 DOM에 바인딩하면 된다. (Storage 보안 규칙은 SDK 경로 read에만 적용되며, 토큰 URL 직접 GET에는 규칙이 개입하지 않는다.)

### 4.4 대안 URL (참고, MVP 미채택)

- GCS 서명 URL(V4, ≤7일 만료): 서비스 계정 서명 필요, QR 유효기간 제약 → MVP 미채택.
- 공개 read(allUsers): 버킷 전체 노출 위험 → 미채택.

---

## 5. 보안 규칙 요구사항 (js-architect가 작성할 규칙의 명세)

> 웹의 Firebase 설정(API 키)은 공개되므로 **보안 규칙이 유일한 방어선**(§10). js-architect는 아래 요구사항을 만족하는 `firestore.rules` / `storage.rules`를 작성한다.

### 5.1 Firestore 규칙 요구사항

```
users/{uid}          : read=false, write=false   // 전면 차단 (PIN 해시·역할·이메일 보호)
frameTemplates/{fid}  : read=false, write=false   // 웹 접근 없음 (WPF 전용)
resultSessions/{sid}  :
    get   = true      // 토큰 ID 단건 get만 허용
    list  = false     // 쿼리/열거 금지 (토큰 열거 방어)
    write = false     // 웹은 쓰기 없음 (WPF가 신뢰 경로로 생성)
```

- **핵심**: `resultSessions`는 `allow get: if true;` (단건) + `allow list: if false;` (쿼리 금지) 분리 규칙. `allow read`(get+list 통합) 사용 금지.
- WPF의 쓰기 경로:
  - MVP 1차: 서비스 계정(Admin SDK)로 규칙 우회 쓰기 → 규칙과 무관하게 문서 생성.
  - 배포 시: Firebase Auth ID 토큰 + 규칙에서 인증 계정 쓰기 허용(단 `resultSessions` write는 웹엔 여전히 deny, WPF 인증 계정만).
- 규칙은 WPF 쓰기를 막지 않도록 하되(서비스 계정은 규칙 우회, 인증 클라이언트는 별도 조건), **웹(비인증/공개)에는 위 표대로 제한**한다.

### 5.2 Storage 규칙 요구사항

```
results/{sessionId}/{file}  : read= (토큰 URL 직접 GET이므로 SDK read 규칙은 false로 두어도 무방)
                              write=false (웹 쓰기 없음)
frames/{userId}/{file}      : read=false, write=false (웹 접근 없음)
```

- 웹은 Storage SDK로 파일을 읽지 않고 **토큰 URL로 직접 GET**하므로, Storage read 규칙은 웹 다운로드에 영향을 주지 않는다(토큰 URL은 규칙 우회). 따라서 `results/` SDK read를 명시적으로 열 필요 없음 — **오히려 닫아두는 편이 안전**(SDK 경로 열거·직접 접근 차단).
- WPF 쓰기: 서비스 계정(우회) 또는 인증 클라이언트. 웹은 write 전면 deny.

### 5.3 규칙 검증 요구사항 (js-architect 산출물에 포함 권장)

- Firebase Emulator/규칙 테스트로: (a) 웹이 users/frameTemplates read 시도 → deny, (b) resultSessions list 쿼리 → deny, (c) resultSessions 단건 get(유효 토큰) → allow, (d) resultSessions write(웹) → deny.

---

## 6. TTL / expiresAt 의미론 & 만료 삭제 분담

### 6.1 expiresAt 의미

- `expiresAt = createdAt + retentionHours`. `retentionHours`는 WPF `AppSettings`(기본 24h, 범위 1~72h, 관리자 모드에서 변경).
- **자동 삭제 대상은 Firebase 업로드본만**(`results/` + 해당 `resultSessions` 문서). **로컬 저장분(WPF `saveLocalCopy`)은 삭제 대상 아님**(§9 #18/#34) — 이는 WPF 로컬 관심사로 웹·Firebase와 무관.

### 6.2 삭제 분담 (누가 지우는가)

| 방식 | 주체 | MVP 채택 | 비고 |
|------|------|---------|------|
| **WPF 앱 직접 삭제** | WPF | **1차 채택** | 앱 시작/주기적 `expiresAt<now` 스캔 → Storage `results/{sid}/` + 문서 삭제. 정밀 타이밍 |
| **GCS Lifecycle 규칙** | 인프라(자동) | **안전망 채택** | 버킷 age 기반 자동 청소. WPF가 못 지운 잔여물 대비. 단위 "일" |
| **스케줄 Cloud Functions** | 웹/인프라 | **선택**(js-architect 결정) | Blaze 필요. 상시 서버 부재 시 유용. WPF 직접 삭제로 충분하면 생략 가능 |
| **Firestore 네이티브 TTL** | 인프라 | 선택 | `expiresAt` 필드 TTL 정책. **문서만 삭제, Storage 파일 미삭제** → Storage 정리 별도 필요 |

### 6.3 js-architect 결정 포인트

- 웹 측이 TTL 정리를 책임질지(스케줄 Functions) 여부는 js-architect 재량. **WPF 직접 삭제 + GCS Lifecycle 안전망으로 MVP는 충분**하므로, Functions는 "WPF가 항상 켜져 있지 않은 운영 환경"을 대비할 때만 추가.
- **확정: 스케줄 Cloud Functions 미채택 (D-2, js-architect)** — WPF 직접 삭제(1차) + GCS Lifecycle(안전망)으로 MVP 충분. 웹은 삭제 미수행, 만료 안내만 담당(Firestore 네이티브 TTL은 선택 권장).
- 만약 Functions 스케줄러를 채택하면: `results/` 스캔(문서 `expiresAt<now`) → Storage 파일 + 문서 삭제. `frames/`는 절대 건드리지 않음.
- **불변식**: 어떤 삭제 주체든 (1) `results/`만 대상, (2) `frames/`·로컬 저장분 비대상, (3) 문서와 Storage 파일을 **함께** 정리(문서만 남거나 파일만 남는 고아 상태 최소화).

---

## 7. 계정 삭제 cascade (WPF 담당, 참고)

- WPF가 계정 삭제 시: 해당 `users` 문서 + 소유 `frameTemplates` 문서 + Storage `frames/{userId}/` 전체를 함께 삭제(§F8, §9 #30). **웹은 관여하지 않음**(계약상 참고 정보).

---

## 8. js-architect 체크리스트 (이 문서로 웹 설계 가능 여부)

- [x] Firestore 컬렉션 ID·필드·타입 확정 (`users`/`frameTemplates`/`resultSessions`) — §2
- [x] 웹이 읽는 대상 = `resultSessions` 단건 get, 필드 `finalImageUrl`/`timelapseUrl`/`expiresAt`/`downloadPageUrl` — §2.3
- [x] 다운로드 페이지 URL 형식·토큰 규칙(기본안 `/?s={token}`, UUIDv4) — §3
- [x] Storage 경로(`results/{sessionId}/`, `frames/{userId}/`)·파일명·다운로드 URL 형식(토큰 URL) — §4
- [x] 보안 규칙 요구사항(users/frames 차단, resultSessions get-only, list 금지) — §5
- [x] TTL/expiresAt 의미론·삭제 분담(WPF 1차 + Lifecycle 안전망, Functions 선택) — §6
- [x] 요금제 전제(Storage=Blaze 필수, F5 off 시 웹 미사용) — §1

> **미결(js-architect 확정 요청)**: URL 경로형 vs 쿼리형 택일(§3.1, 기본안 쿼리형) / TTL 정리에 스케줄 Functions 채택 여부(§6.3). 두 결정 모두 **WPF 코드 변경 없이** 웹 측에서 결정 가능(WPF는 §3.5 규약·§4 URL만 준수).
>
> **확정 완료 (js-architect)**: (1) URL = 쿼리형 `/?s={token}` (D-1, §3.1). (2) 스케줄 Functions 미채택 (D-2, §6.3). 두 결정 모두 WPF 코드·계약 스키마 변경 불요.
