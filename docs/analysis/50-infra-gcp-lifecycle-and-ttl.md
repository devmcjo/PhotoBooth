# 50 · 인프라 — GCP 수명주기·TTL·만료 정리

| 항목 | 내용 |
|------|------|
| 문서 | 결과물(Storage 파일)·세션 문서(Firestore)의 보관/삭제 설계와 **현재 확정 상태**(2026-07-23 반영) |
| 범위 | `web/lifecycle.json`, `web/OPS-ttl.md`, `web/storage.rules`, `web/firestore.rules`, WPF 만료 정리 코드(`UploadService.PurgeExpiredAsync`·`FirebaseClient.QueryExpiredSessionsAsync`). 웹 만료 판정은 [20 · 프론트엔드](./20-frontend-web-download-page.md) §5, 스키마는 [40 · 스키마](./40-database-firestore-and-storage-schema.md) |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 | `web/lifecycle.json`, `web/OPS-ttl.md`, `web/storage.rules`, `web/firestore.rules`, `web/firebase.json`, `src/MCPhoto.Firebase/UploadService.cs`, `src/MCPhoto.Firebase/FirebaseClient.cs`, `src/MCPhoto.Core/Settings/AppSettings.cs` |
| 갱신 규칙 | `web/lifecycle.json`·`web/OPS-ttl.md`·보안 규칙이나 삭제 주체 채택 상태가 바뀌면 표/명령/근거(`파일:라인`)를 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정. 명령의 실제 실행 여부(콘솔/CLI 적용)는 이 저장소에서 확인 불가 → **가정**.

## 0. 프로젝트 값

| 키 | 값 | 근거 |
|----|-----|------|
| project | `mcphoto-955fb` | `web/.firebaserc:3`, `web/public/firebase-config.js:8` |
| bucket | `mcphoto-955fb.firebasestorage.app` | `web/firebase.json:32`, `web/public/firebase-config.js:9` |
| HostingBaseUrl | `https://mcphoto-955fb.web.app` | `src/MCPhoto.Core/Settings/AppSettings.cs:103` |
| age(물리 삭제) | 3일 | `web/lifecycle.json:7` |
| 프리픽스 | `results/` | `web/lifecycle.json:8` |

---

## 1. 삭제 분담 — 3중 설계와 현재 확정 상태

보관/삭제는 세 가지 방식을 검토했고, 인프라 자동화 2종을 채택했다(`web/OPS-ttl.md:6,15-24`).

| 방식 | 주체 | 채택 여부 | 대상 | 근거 |
|------|------|-----------|------|------|
| GCS Lifecycle 규칙 | 인프라(자동) | **채택**(파일 삭제 주력) | `results/` 프리픽스 파일만(age 3일) | `web/lifecycle.json`, `web/OPS-ttl.md:19` |
| Firestore 네이티브 TTL | 인프라 | **채택**(문서 삭제) | `resultSessions` 만료 문서(`expiresAt` 기준) | `web/OPS-ttl.md:20,60-76` |
| WPF 앱 직접 삭제(`PurgeExpiredAsync`) | WPF | **코드 존재·미사용**(인프라로 대체) | `results/{sid}/` 파일 + `resultSessions/{sid}` 문서 함께 | `src/MCPhoto.Firebase/UploadService.cs:80-102`, `web/OPS-ttl.md:21` |
| 스케줄 Cloud Functions | 웹/인프라 | **미채택**(D-2) | — | `web/OPS-ttl.md:22` |

> **핵심**: 파일(GCS Lifecycle)과 문서(Firestore TTL)는 서로 다른 서비스라 각각 설정해야 한다. Lifecycle 은 Storage 파일만, TTL 은 Firestore 문서만 지운다. 둘을 함께 켜야 파일+문서가 모두 정리된다(`web/OPS-ttl.md:24,52-53,60-62`).

### 1.1 불변식 (어떤 삭제 주체든 준수, 계약 §6.3)

1. **`results/` 프리픽스만** 삭제 대상(`web/OPS-ttl.md:30`).
2. **`frames/`(프레임 이미지)와 WPF 로컬 저장분(`saveLocalCopy`)은 비대상**. 프레임은 TTL 비대상, 로컬 저장분은 Firebase 와 무관(`web/OPS-ttl.md:31`).
3. 가능하면 **문서 + Storage 파일을 함께** 정리해 고아(문서만/파일만 남음)를 최소화(`web/OPS-ttl.md:32`).

---

## 2. GCS Lifecycle (채택 — 파일 삭제)

`results/` 프리픽스에만 age 기반 Delete 규칙을 건다. `retentionHours` 최댓값(72h=3일)보다 여유를 둔 **age 3일**이다(`web/lifecycle.json`, `web/OPS-ttl.md:38`).

```json
// web/lifecycle.json
{
  "rule": [
    { "action": { "type": "Delete" },
      "condition": { "age": 3, "matchesPrefix": ["results/"] } }
  ]
}
```

| 필드 | 값 | 의미 | 근거 |
|------|-----|------|------|
| action.type | `Delete` | 객체 삭제 | `web/lifecycle.json:4` |
| condition.age | `3` | 생성 후 3일 경과 | `web/lifecycle.json:7` |
| condition.matchesPrefix | `["results/"]` | `results/` 한정, `frames/` 제외 | `web/lifecycle.json:8` |

- **Storage 파일만** 삭제하며 Firestore 문서는 지우지 못한다 → 문서 고아가 남는다(§3에서 TTL로 완화, `web/OPS-ttl.md:53`).
- ⚠️ `matchesPrefix`가 `["results/"]`인지 반드시 확인. `frames/`를 포함하면 프레임 이미지가 삭제되어 계약 불변식 위반(`web/OPS-ttl.md:52`).

### 2.1 적용 명령 (CLI)

```bash
# 현재 Lifecycle 조회
gsutil lifecycle get gs://mcphoto-955fb.firebasestorage.app

# web/lifecycle.json 적용(age 3일, results/ 한정 Delete)
gsutil lifecycle set web/lifecycle.json gs://mcphoto-955fb.firebasestorage.app

# gcloud CLI 대안
gcloud storage buckets update gs://mcphoto-955fb.firebasestorage.app --lifecycle-file=web/lifecycle.json
```

근거: `web/OPS-ttl.md:41-48`. 실제 적용 완료 여부는 이 저장소에서 확인 불가(**가정** — 미적용 시 명령 실행 필요).

---

## 3. Firestore 네이티브 TTL (채택 — 문서 삭제)

GCS Lifecycle 은 파일만 지우므로 `resultSessions` 문서가 고아로 남는다. 이를 없애기 위해 `resultSessions.expiresAt` 필드에 TTL 정책을 설정한다(`web/OPS-ttl.md:60-62`).

```bash
gcloud firestore fields ttls update expiresAt \
  --collection-group=resultSessions \
  --enable-ttl \
  --project=mcphoto-955fb
```

| 속성 | 값 | 근거 |
|------|-----|------|
| 컬렉션 그룹 | `resultSessions` | `web/OPS-ttl.md:66` |
| TTL 필드 | `expiresAt`(Firestore `Timestamp`) | `web/OPS-ttl.md:65`, `FirebaseClient.cs:159` |
| 삭제 시점 | 즉시 아님, 며칠 내 best-effort | `web/OPS-ttl.md:72` |

- 무료·서버리스·Functions 불요(`web/OPS-ttl.md:71`).
- 삭제가 늦어져도 웹은 이미 `expiresAt < now`로 접근 만료를 판정하므로 사용자에겐 만료로 보인다 → **정합성 문제 없음**(`web/OPS-ttl.md:72`, [20번](./20-frontend-web-download-page.md) §5).
- **권장이지 필수는 아니다**. 미채택 시에도 웹은 미디어 로드 실패 폴백으로 고아를 우아하게 처리한다(`web/OPS-ttl.md:73`, [20번](./20-frontend-web-download-page.md) §5.1).

---

## 4. WPF 직접 삭제 (`PurgeExpiredAsync`) — 코드 존재·미사용

WPF에 만료 세션 정리 코드가 구현되어 있으나 **현재 미사용**(인프라 2종으로 대체, `web/OPS-ttl.md:6,21`).

| 요소 | 동작 | 근거 |
|------|------|------|
| `UploadService.PurgeExpiredAsync` | 만료 세션마다 `DeleteStoragePrefixAsync("results/{id}/")` + `DeleteResultSessionAsync(id)`를 **함께** 호출(고아 최소화, 불변식 3) | `src/MCPhoto.Firebase/UploadService.cs:80-102` |
| `FirebaseClient.QueryExpiredSessionsAsync` | `resultSessions`에서 `WhereLessThan("expiresAt", now)` 쿼리로 만료 문서 조회 | `src/MCPhoto.Firebase/FirebaseClient.cs:165-187` |
| `DeleteStoragePrefixAsync` | 프리픽스 하위 객체 열거 후 개별 삭제(실패는 warning) | `FirebaseClient.cs:136-148` |

- 호출부는 인터페이스 정의(`src/MCPhoto.Core/Upload/IUploadService.cs:24`)와 구현(`UploadService.cs:80`)뿐이며, 실제 실행 트리거(스케줄러·기동 시 호출 등)는 리포지토리에서 확인되지 않는다 → **미사용**(`web/OPS-ttl.md:21` 명시와 일치).
- `IsInitialized=false`(서비스 계정 키 부재)면 `PurgeExpiredAsync`는 0을 반환하고 아무것도 하지 않는다(`UploadService.cs:82`).

---

## 5. 접근 만료(웹 차단) vs 물리 삭제 — 별개의 두 축

세션별 `retentionHours`(1~72h)와 물리 삭제(age 3일 / TTL)는 서로 다른 것을 통제한다. 이 구분이 설계의 핵심이다.

| 축 | 기준 | 무엇을 하나 | 근거 |
|----|------|-------------|------|
| 접근 만료(웹 차단) | `expiresAt = createdAt + retentionHours`(세션별 1~72h) | 웹이 `expiresAt < now`이면 다운로드 차단(만료 화면). 파일은 아직 존재할 수 있음 | `UploadContract.cs:43-44`, `AppSettings.cs:62`(기본 24h, 범위 1~72), [20번](./20-frontend-web-download-page.md) §5 |
| 물리 삭제(파일) | GCS Lifecycle age 3일(`results/`) | Storage 객체 실제 삭제. `retentionHours`와 무관하게 최댓값(72h) 이후 일괄 | `web/lifecycle.json:7-8`, `web/OPS-ttl.md:38` |
| 물리 삭제(문서) | Firestore TTL(`expiresAt`) | `resultSessions` 문서 실제 삭제(best-effort) | `web/OPS-ttl.md:60-72` |

- `retentionHours`는 세션별로 `expiresAt`에 정확히 반영되어 **접근 만료**를 세밀하게 제어한다. 반면 물리 삭제는 age 3일(Lifecycle)·TTL(문서)로 **일괄** 처리되며 세션별 시간을 따르지 않는다. 즉, retention 이 짧은 세션도 파일은 최대 3일까지 남을 수 있으나 웹 접근은 `expiresAt` 시점에 이미 차단된다.

### 5.1 보안 규칙과의 관계

토큰 URL(`?alt=media&token=...`)은 capability 이며 보안 규칙을 우회하므로 `results/`의 SDK read 를 닫아도 웹 다운로드는 동작한다. 규칙은 SDK 경로 열거/직접 접근만 차단한다(`web/storage.rules:6-8,16-19`). 물리 삭제 후에는 토큰 URL 자체가 404 → 웹은 미디어 로드 실패 폴백으로 만료 처리한다([20번](./20-frontend-web-download-page.md) §5.1).

- Firestore: `resultSessions` get allow / list deny / write deny, `users`·`frameTemplates` 전면 deny(`web/firestore.rules:16-38`).
- WPF 서비스 계정(Admin SDK)은 보안 규칙을 완전히 우회하므로 write:false 가 WPF 업로드/문서 생성을 막지 않는다(`web/firestore.rules:9-11`, `web/storage.rules:10-11`).

---

## 6. 비용

| 항목 | 비용 | 근거 요약 |
|------|------|-----------|
| GCS Lifecycle | **완전 무료** | Standard 스토리지 클래스는 조기 삭제 위약금 없음, Delete 작업 자체 무료. `web/OPS-ttl.md`의 채택 근거와 일치(과금 없음) |
| Firestore TTL 삭제 | **실질 0원** | 서버리스·Functions 불요, 삭제는 무료 할당량 내(`web/OPS-ttl.md:71`) |
| 스케줄 Cloud Functions | 미채택이라 비용 없음 | 채택 시 함수 호출·실행 과금 발생하므로 회피(D-2) |

> 비용 수치의 정확한 요율은 GCP 가격 정책에 따르며 시점별로 다를 수 있음 → 세부 요율은 **가정**. 설계 의도는 "인프라 자동화로 추가 과금 0에 수렴"이다.

---

## 7. 콘솔(GUI) 설정 경로 — 운영자용

| 대상 | 콘솔 경로 | 설정값 | 근거 |
|------|-----------|--------|------|
| GCS Lifecycle | Google Cloud Console > Cloud Storage > 버킷 선택 > "수명 주기" 탭 > 규칙 추가 | 작업=삭제, 조건=age 3일 + 접두어 `results/` | `web/OPS-ttl.md:55-56` |
| Firestore TTL | Firebase Console > Firestore Database > "TTL" 탭 > 정책 만들기 | 컬렉션 그룹=`resultSessions`, 타임스탬프 필드=`expiresAt` | `web/OPS-ttl.md:75-76` |

---

## 8. 웹의 TTL 관련 책임 (재확인)

- 웹은 삭제를 **수행하지 않는다**(`web/OPS-ttl.md:82`, `web/public/app.js:197-198`).
- 만료(`expiresAt < now`)·문서 부재(not-found)·파일 부재(미디어 로드 실패)를 판정해 **만료/부분 실패 안내만** 표시한다([20번](./20-frontend-web-download-page.md) §5, `web/OPS-ttl.md:83`).

---

## 9. 상호 참조

- 웹 만료 판정 로직·미디어 로드 실패 폴백: [20 · 프론트엔드](./20-frontend-web-download-page.md) §5.
- 업로드가 만드는 `results/{token}/` 경로·`resultSessions` 문서·`expiresAt` 필드: [30 · 백엔드](./30-backend-firebase-integration.md), [40 · 스키마](./40-database-firestore-and-storage-schema.md).
- 서비스 계정 키(git·인스톨러 미포함): [80 · 빌드/배포](./80-build-and-deployment.md), `src/MCPhoto.Firebase/FirebaseClient.cs:14-15,92-102`.
