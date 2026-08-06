# 07 · API · 데이터 계약 (신규·변경 전수)

| 항목 | 내용 |
|------|------|
| 문서 | 과금 도입으로 **추가·변경되는 HTTP 와이어 계약과 Firestore 스키마 전수**. 새 클라이언트는 이 문서만 보고 구현할 수 있어야 한다 |
| 범위 | 엔드포인트(경로·게이트·요청/응답·상태코드), 에러 코드, 컬렉션·필드·인덱스·보안 규칙, 마이그레이션 |
| 최종 업데이트 | 2026-08-06 (신규) |
| 진실원 관계 | 현행 계약은 [`analysis/31`](../analysis/31-backend-api-reference.md)·[`analysis/40`](../analysis/40-database-firestore-and-storage-schema.md)이 정본이다. **이 문서는 그 위에 얹는 델타**이며, 구현이 끝나면 31·40번에 흡수한다 |

---

## 1. 기본 사항 (현행 유지)

| 항목 | 값 |
|------|-----|
| Base URL | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` |
| 헤더 | `X-MCPhoto-Client`(게이트 키) · `Authorization: Bearer {JWT}` — 둘 다 현행 규약 |
| 본문 | JSON, 상한 256KB |
| 에러 봉투 | `{ "error": { "code": "...", "message": "..." } }` — **불변**. 과금 코드도 같은 봉투를 쓴다 |
| 신규 라우터 | `/wallet` · `/items` · `/payments` · `/sessions` · `/admin` (5개 추가 → 총 11개) |
| 기존 라우터 변경 | `/auth`(로그인 세션) · `/accounts`(qr-usage 확장) · `/frames`(개인 생성) · `/uploads`(정원 게이트) · `/config`(billing 설정) |

### 1.1 게이트 추가

| 게이트 | 통과 조건 | 실패 | 비고 |
|--------|-----------|------|------|
| `requireFrameWrite` | `role ∈ {advanced_user, manager, admin}` | 403 `forbidden` "프레임 저작 권한이 필요합니다." | `CanWriteFrames`의 서버 대칭. **신규**(현재 서버에는 이 축이 없다 — [`analysis/60 §1.2`](../analysis/60-auth-accounts-and-roles.md)) |
| `requireActiveSession` | JWT `sid`가 `sessions/{uid}`의 활성 항목에 존재 | **401** `SESSION_SUPERSEDED` | `requireBearer` 뒤. `sid` 없는 구토큰은 전환 기간 통과 → [06 §4.2](./06-single-session-enforcement.md) |
| `requireBillingEnabled` | `config/billing.enabled == true` | 503 `service_unavailable` "과금 기능이 일시 중지되었습니다." | 구매 라우트에만. 소비 라우트는 킬스위치 시 무료 티어로 폴백(B12) |

---

## 2. 에러 코드 (추가) — 기존 코드는 전부 유지

| `code` | 상태 | 의미 | 클라이언트 처리 |
|--------|:----:|------|-----------------|
| `QUOTA_EXHAUSTED` | **403** | 오늘 QR 정원 소진(유료 기준) | "오늘 QR 전송을 모두 사용했습니다" + 리셋 시각 표시 |
| `NO_ENTITLEMENT` | **403** | QR 정원 플랜이 없다(한 번도 산 적 없거나 만료) | "QR 전송 권한이 없습니다"(B2B: 관리자 문의 / B2C: 구매 유도) |
| `HARD_CAP` | **403** | 계정 일 하드캡 도달 | "오늘 사용 가능한 최대 횟수에 도달했습니다" |
| `KILL_SWITCH` | **403** | 전역 차단 | "일시적으로 사용할 수 없습니다. 잠시 후 다시 시도해 주세요" |
| `INSUFFICIENT_MC` | **409** | 잔액 부족. `detail: {requiredMc, balanceMc}` | 부족 모달 + 충전 |
| `NO_FRAME_CREDIT` | **409** | 프레임 생성권 없음 | MC로 구매할지 확인 모달 |
| `PRICE_CHANGED` | **409** | 카탈로그 `version` 불일치. `detail: {currentVersion}` | 카탈로그 재조회 후 확인 모달 재표시 |
| `IN_PROGRESS` | **409** | 같은 멱등키 처리 중 | 1.5초 후 1회 재시도 |
| `IDEMPOTENCY_KEY_REUSE` | **400** | 같은 키·다른 본문 | 개발 오류(정상 흐름에 없어야 한다) |
| `TOO_MANY_GRANTS` | **409** | 활성 권리 20개 초과 | "보유 플랜이 너무 많습니다" |
| `WALLET_FROZEN` | **403** | 지갑 동결 | "고객센터에 문의해 주세요"(사유 비노출) |
| `SESSION_SUPERSEDED` | **401** | 세션이 다른 기기로 교체됨. `detail: {revokeReason, byDeviceLabel?}` | **강제 로그아웃 팝업**([06 §5.1](./06-single-session-enforcement.md)) |
| `SESSION_ACTIVE_ELSEWHERE` | **409** | 다른 기기 활성. `detail: {deviceLabel, platform, lastSeenAtMs}` | 확인 모달 → `force:true` |
| `RATE_LIMITED` | **429** | 요청 과다(프레임 이미지 교체·prepare 등) | "잠시 후 다시 시도" |
| `PAYMENT_VERIFY_FAILED` | **400** | 영수증 검증 실패 | "결제를 확인할 수 없습니다" |
| `PAYMENT_ALREADY_PROCESSED` | **200**(성공 취급) | 이미 지급된 결제 | 정상 완료로 처리(멱등) |

> ⚠️ **403의 세 얼굴**: 이제 403이 ① 권한 부족 ② 무료 한도(`TEMP_USER_*`) ③ **유료 정원**(`QUOTA_EXHAUSTED` 등)을 뜻한다. 클라이언트는 **반드시 `error.code`로 분기**한다. 기존 문서의 경고([`analysis/31 §3`](../analysis/31-backend-api-reference.md) "403의 두 얼굴")가 세 얼굴로 확장된다.

---

## 3. 엔드포인트

### 3.0 요약표

| 메서드·경로 | 게이트 | 성공 | 신규/변경 |
|-------------|--------|------|:---:|
| `GET /catalog` | apiKey | 200 | 신규 |
| `GET /wallet` | Bearer(+세션) | 200 | 신규 |
| `GET /wallet/entries` | Bearer(+세션) | 200 | 신규 |
| `GET /entitlements` | Bearer(+세션) | 200 | 신규 |
| `POST /items/purchase` | Bearer + 세션 + billingEnabled | **201** | 신규 |
| `POST /payments/orders` | Bearer + 세션 + billingEnabled | **201** | 신규 |
| `GET /payments/{paymentId}` | Bearer + 세션 | 200 | 신규 |
| `POST /payments/iap/verify` | Bearer + 세션 + billingEnabled | 200 | 신규 |
| `POST /payments/webhook/{provider}` | **서명 검증**(Bearer 없음) | 200 | 신규 |
| `POST /sessions/heartbeat` | Bearer + 세션 | 200 | 신규 |
| `POST /sessions/logout` | Bearer | **204** | 신규 |
| `GET /sessions` | Bearer | 200 | 신규 |
| `POST /frames/mine` | Bearer + frameWrite + 세션 | **201** | 신규 |
| `DELETE /frames/mine/{id}` | Bearer + frameWrite + 세션 | 200 | 신규 |
| `GET /accounts/me/qr-usage` | Bearer | 200 | **확장**(필드 추가) |
| `POST /auth/google` | apiKey | 200 / **409** | **확장**(세션) |
| `POST /uploads/prepare` | apiKey + optionalBearer(+세션) | 200 / 403 | **변경**(정원) |
| `POST /uploads/commit` | apiKey + optionalBearer(+세션) | 201 / 403 | **변경**(정원·차감) |
| `GET /config/billing` | Bearer | 200 | 신규 |
| `PATCH /config/billing` | Bearer + admin | 200 | 신규 |
| `GET /admin/wallets/{userId}` | Bearer + admin | 200 | 신규 |
| `POST /admin/wallets/{userId}/grant` | Bearer + admin | 201 | 신규 |
| `POST /admin/wallets/{userId}/adjust` | Bearer + admin | 201 | 신규 |
| `POST /admin/wallets/{userId}/freeze` | Bearer + admin | 204 | 신규 |
| `POST /admin/sessions/{userId}/revoke-all` | Bearer + admin | 204 | 신규 |
| `POST /admin/payments/{paymentId}/refund` | Bearer + admin | 200 | 신규 |

---

### 3.1 `GET /catalog` — 상품 카탈로그

**게이트**: apiKey (로그인 전에도 가격 표시 가능)

**쿼리**: `?platform=windows|web|ios|android` (필수 — 채널별 노출 필터)

**응답 200**
```json
{
  "packs": [
    { "packId": "mc_100", "mcAmount": 100, "priceKrw": 8500, "badge": "인기",
      "storeProductId": "mcphoto.mc.100", "version": 3, "sortOrder": 40 }
  ],
  "items": [
    { "itemId": "qr_d50_30d", "kind": "qr_plan", "title": "QR 하루 50개 · 30일",
      "desc": "매일 00시에 50개로 초기화됩니다. 구매 첫날은 남은 시간만큼 제공됩니다.",
      "priceMc": 270, "dailyMax": 50,
      "effect": { "dailyAllowance": 50, "durationDays": 30 },
      "version": 2, "sortOrder": 30 },
    { "itemId": "frame_create", "kind": "frame_create", "title": "커스텀 프레임 만들기",
      "priceMc": 5, "dailyMax": 10, "effect": { "frameCredits": 1 }, "version": 1, "sortOrder": 90 }
  ],
  "currency": "KRW",
  "mcBaseKrw": 100,
  "serverTimeMs": 1786000000000,
  "billingEnabled": true
}
```

| 규칙 | 내용 |
|------|------|
| `storeProductId` | 요청 `platform`에 해당하는 값만 내려간다(다른 플랫폼 id는 노출하지 않는다) |
| `version` | 구매 요청에 **그대로 실어 보낸다**. 불일치면 409 `PRICE_CHANGED` |
| `billingEnabled: false` | 클라는 구매 UI를 숨기고 "일시 중지" 안내 |
| 캐시 | 클라 메모리 캐시 10분 허용. 구매 직전 재조회 권장 |

---

### 3.2 `GET /accounts/me/qr-usage` — **확장**(하위 호환)

**게이트**: Bearer (본인 고정) — 현행 유지

**응답 200** (⬅ 기존 필드 / ⭐ 신규 필드)
```json
{
  "role": "user",                    
  "blocked": false,                  
  "reason": "ok",                    
  "remainingMs": 0,                  
  "remainingCount": 0,               
  "limits": { "qrHours": 48, "qrCount": 30 },

  "source": "plan",                  
  "dailyAllowance": 50,              
  "usedToday": 12,                   
  "remainingToday": 38,              
  "billingDay": "2026-08-06",        
  "resetAtMs": 1786032000000,        
  "creditsRemaining": 0,             
  "plans": [
    { "grantId": "…", "itemId": "qr_d50_30d", "dailyAllowance": 50,
      "isFirstDay": false, "expiresAtMs": 1788624000000 }
  ],
  "overage": { "enabled": false, "mcPerSession": 2 },
  "walletBalanceMc": 145,            
  "hardCapRemaining": 288            
}
```

| 필드 | 의미 |
|------|------|
| `source` | 다음 1건이 소비될 소스: `free`/`plan`/`credit`/`overage`/`none` |
| `dailyAllowance` | 오늘의 총 정원(첫날 프로레이션 반영) |
| `remainingToday` | **표시의 정본**. 무료·플랜·회수권·하드캡을 모두 반영한 최종 잔여 |
| `resetAtMs` | 다음 KST 00시(ms) — "내일 00시에 초기화" 문구 근거 |
| `plans[].isFirstDay` | 오늘이 그 플랜의 구매일인지(정원 축소 표시용) |
| `hardCapRemaining` | 계정 하드캡까지 남은 수 |

| 하위 호환 규칙 | 내용 |
|----------------|------|
| 기존 필드 의미 **불변** | `remainingCount`는 여전히 "무료 잔여"이고 non-TempUser는 0(=무제한 아님, 이제는 정원이 정본) |
| 구버전 클라 | 기존 필드만 읽어 동작한다. 정원 초과 시 `blocked: true`·`reason`을 **정원 사유로도 채워** 구버전이 차단을 인식할 수 있게 한다 → `reason` 확장값: `"quota"`(구버전은 `"ok"` 외를 차단으로 취급하므로 안전) |
| 실패 정책 | **fail-open**(현행 유지) |

---

### 3.3 결제

#### `POST /payments/orders` — PG 주문 생성

**게이트**: Bearer + 세션 + billingEnabled

**요청**
```json
{ "packId": "mc_100", "packVersion": 3, "channel": "pg",
  "platform": "windows", "idempotencyKey": "8f2c1a90-3d5e-4b17-9c22-0ab7de441f03" }
```

**응답 201**
```json
{ "paymentId": "pay_20260806_9f1c2a44", "amountKrw": 8500, "mcAmount": 100,
  "status": "created", "checkoutUrl": "https://mcphoto-955fb.web.app/pay?p=pay_…",
  "expiresAtMs": 1786001800000 }
```

| 오류 | 원인 |
|------|------|
| 409 `PRICE_CHANGED` | `packVersion` 불일치 |
| 403 `WALLET_FROZEN` | 동결 계정 |
| 400 `invalid_argument` | 팩 없음·채널 불일치 |

#### `GET /payments/{paymentId}` — 상태 조회(폴링)

**응답 200**: `{paymentId, status, amountKrw, mcAmount, grantedAtMs?, walletBalanceMc?}`
- 폴링 규칙: 2초 간격, 최대 60초. `granted`면 종료. 타임아웃은 실패로 단정하지 않는다.
- 본인 주문만 조회 가능(다른 계정 주문은 404 — 존재 여부를 노출하지 않는다).

#### `POST /payments/iap/verify` — 스토어 영수증 검증

**요청**
```json
{ "platform": "ios", "productId": "mcphoto.mc.100",
  "receipt": "…JWS 또는 purchaseToken…", "idempotencyKey": "…" }
```

**응답 200**: `{paymentId, status: "granted", mcAmount, walletBalanceMc}`

| 오류 | 원인 |
|------|------|
| 400 `PAYMENT_VERIFY_FAILED` | 스토어 검증 실패·위조 |
| 200 `PAYMENT_ALREADY_PROCESSED` 처리 | 같은 트랜잭션 재검증(멱등) → 200 + 현재 잔액 |
| 400 | `productId`가 카탈로그 매핑에 없음 |
| 403 | 샌드박스 영수증 + 운영 환경(지급 차단) |

#### `POST /payments/webhook/{provider}` — 결제 웹훅

| 항목 | 규격 |
|------|------|
| 게이트 | **Bearer·apiKey 없음.** provider별 **서명 검증**이 인증이다 |
| 응답 | 항상 **200**(재전송 폭주 방지). 처리 실패는 내부 큐·로그로 남기고 200 |
| 멱등 | provider 거래 id 기준([02 §4.3](./02-wallet-ledger-and-entitlements.md)) |
| 금액 대조 | provider 조회 API 재확인 필수(요청 본문 신뢰 금지) |
| 순서 | 역행 전이 무시(PM2) |

---

### 3.4 `POST /items/purchase` — 아이템(MC 소비) 구매

**게이트**: Bearer + 세션 + billingEnabled

**요청**
```json
{ "itemId": "qr_d50_30d", "itemVersion": 2, "quantity": 1, "idempotencyKey": "…" }
```

| 필드 | 검증 |
|------|------|
| `quantity` | 1~5(기본 1). 정원 플랜은 누적되므로 5개까지 허용, 활성 20개 상한 준수 |

**응답 201**
```json
{ "spentMc": 270, "walletBalanceMc": 145,
  "grants": [ { "grantId": "…", "kind": "qr_plan", "dailyAllowance": 50,
                "firstDayAllowance": 15, "startBillingDay": "2026-08-06",
                "expiresAtMs": 1788624000000 } ],
  "quota": { "dailyAllowance": 50, "remainingToday": 15, "billingDay": "2026-08-06" },
  "entitlements": { "activeQrDailyAllowance": 50, "frameCredits": 0 }
}
```

| 오류 | 원인 |
|------|------|
| 409 `INSUFFICIENT_MC` | 잔액 부족(`detail.requiredMc`) |
| 409 `PRICE_CHANGED` | 버전 불일치 |
| 409 `TOO_MANY_GRANTS` | 활성 20개 초과 |
| 409 `IN_PROGRESS` | 멱등 처리 중 |
| 403 `WALLET_FROZEN` / 503 | 동결 / 킬스위치 |

> ✅ **응답에 `quota`·`entitlements`를 함께 실어** 클라가 구매 직후 별 조회 없이 화면을 갱신한다([02 §8 C2](./02-wallet-ledger-and-entitlements.md)).

---

### 3.5 `POST /frames/mine` — 개인 프레임 생성(과금)

**게이트**: Bearer + `requireFrameWrite` + 세션 + billingEnabled

**요청**
```json
{ "name": "여름 4컷", "imageSize": {"width":1200,"height":1600},
  "slots": [ {"index":0,"x":80,"y":140,"width":480,"height":640} ],
  "useCredit": true, "itemVersion": 1, "idempotencyKey": "…", "migration": false }
```

| 필드 | 검증 |
|------|------|
| `name` | 1~100자, **`_` 금지**(기존 규칙 재사용) |
| `slots` | 1~6개, 기존 규칙 재사용 |
| `useCredit` | true=생성권 사용, false=MC 직접 차감. **자동 폴백 없음**([04 §4.3](./04-custom-frames-billing-and-lifecycle.md)) |
| `migration` | true면 **과금 면제**(레거시 이관 전용). 서버가 "로컬 이관"임을 신뢰할 수 없으므로 **면제 횟수를 계정당 상한(10)으로 제한**하고 원장에 `reason=migration`으로 기록 |

**서버 강제**: `userId = principal.id`, `isDefault = false`

**응답 201**
```json
{ "frame": { "...FrameResponse..." },
  "upload": { "putUrl": "...", "downloadUrl": "...",
              "requiredHeaders": { "Content-Type": "image/png",
                                   "x-goog-meta-firebaseStorageDownloadTokens": "…",
                                   "x-goog-content-length-range": "0,8388608" } },
  "billing": { "charged": "credit", "priceMc": 0, "walletBalanceMc": 145, "frameCredits": 2 } }
```

| 오류 | 원인 |
|------|------|
| 403 `forbidden` | `user` 이하(프레임 저작 권한 없음) |
| 409 `conflict` | 계정 10개 상한(**차감 전에 검사** — FR9) |
| 409 `NO_FRAME_CREDIT` / `INSUFFICIENT_MC` | 크레딧/잔액 부족 |
| 400 | 이름·슬롯·`imageSize` 검증 실패 |

#### `DELETE /frames/mine/{id}`

**응답 200**: `{deleted: true, refunded: {credit: 1} | null}`

| 규칙 | 내용 |
|------|------|
| 소유 검증 | `userId == principal.id`가 아니면 **403**(power도 남의 개인 프레임을 이 경로로 지우지 않는다) |
| 환급 | 생성 후 10분 이내 + 일 3회 이내 + **이미지 미확인**(Storage에 객체 없음)일 때만 → [04 §3.4](./04-custom-frames-billing-and-lifecycle.md) |
| Storage | 문서 삭제 **전에** owner를 읽어 경로 확정(기존 규약 유지, 고아 방지) |
| `deleted:false` | "문서를 찾지 못했다" — 성공이 아니다(기존 M4 규약 유지) |

---

### 3.6 관리자

| 경로 | 게이트 | 요청 | 비고 |
|------|--------|------|------|
| `GET /admin/wallets/{userId}` | admin | — | `{wallet, entitlements, recentEntries[20], integrityCheck: {ok, computedBalance}}` — **정합성 검증 결과 포함**([02 §6](./02-wallet-ledger-and-entitlements.md)) |
| `POST /admin/wallets/{userId}/grant` | admin | `{mcAmount, reason, memo, idempotencyKey}` | 1회 5,000MC 상한 |
| `POST /admin/wallets/{userId}/adjust` | admin | `{deltaMc, reason, memo, idempotencyKey}` | 음수 허용(회수). 잔액 음수 불가 |
| `POST /admin/wallets/{userId}/freeze` | admin | `{frozen: bool, reason}` | |
| `POST /admin/sessions/{userId}/revoke-all` | admin | — | 계정 탈취 대응 |
| `POST /admin/payments/{paymentId}/refund` | admin | `{amountKrw?, reason, idempotencyKey}` | 부분 환불 지원. PG API 호출 + 원장 `refund` |
| `GET /admin/wallets/{userId}/entries` | admin | `?limit=100&cursor=` | 원장 페이지네이션 |

**manager의 조회 권한**(선택 — [11 D-18](./11-open-decisions.md))

| 경로 | 규칙 |
|------|------|
| `GET /accounts/{id}/wallet-summary` | Bearer + power + `canManage(actor, target)` → 잔액·정원만(원장 미포함). **조정은 불가** |

---

## 4. `/uploads` 변경 상세

### 4.1 `POST /uploads/prepare`

| 항목 | 변경 |
|------|------|
| 게이트 | `requireApiKey` + `optionalBearer` + **(토큰 있으면) `requireActiveSession`** |
| 판정 | TempUser 전용 → **로그인 전원**(admin 예외). `evaluateQrQuota` |
| 오류 | 403 `QUOTA_EXHAUSTED`/`NO_ENTITLEMENT`/`HARD_CAP`/`KILL_SWITCH` **또는** 기존 `TEMP_USER_*`(무료 사유) |
| 게스트 | 통과(불변) |
| 차감 | 없음(불변) |
| 신규 rate limit | 계정당 prepare **분 20회**(초과 429 `RATE_LIMITED`) — commit 없이 파일만 올리는 우회 억제([09 §3.5](./09-security-abuse-and-compliance.md)) |

### 4.2 `POST /uploads/commit`

| 항목 | 변경 |
|------|------|
| 게이트 | 위와 동일 |
| 트랜잭션 대상 | TempUser만 → **로그인 전원** |
| 트랜잭션 내용 | [03 §6.2](./03-qr-daily-quota.md) 9단계 |
| 응답 확장 | 기존 필드 + `quota: {usedToday, remainingToday, source, billingDay}` ⭐ |
| 오류 | 기존(400/401/409) + 403 정원 코드 + 409 `INSUFFICIENT_MC`(오버리지) |
| 게스트 | 기존 비트랜잭션 경로(불변) |

> ⚠️ **응답 확장은 하위 호환이다**(필드 추가만). 구버전 클라는 `quota`를 무시한다.

---

## 5. Firestore 스키마 (신규 컬렉션)

| 컬렉션 | 문서 ID | 문서 수 규모 | TTL | 웹 접근 |
|--------|---------|--------------|-----|---------|
| `wallets` | `{userId}` | 계정 수 | × | **전면 차단** |
| `wallets/{uid}/entries` | `{seq}_{uuid8}` | 계정당 수십~수백 | × (보존 5년) | 전면 차단 |
| `entitlements` | `{userId}` | 계정 수 | × | 전면 차단 |
| `entitlements/{uid}/grants` | UUID | 계정당 ≤ 100 | × | 전면 차단 |
| `usage` | `{userId}_{yyyy-MM-dd}` | 계정×일수 | **90일** | 전면 차단 |
| `usage` (전역 샤드) | `_global_{yyyy-MM-dd}_{0..9}` | 일 10개 | 90일 | 전면 차단 |
| `payments` | `pay_{yyyyMMdd}_{uuid8}` | 결제 수 | × (보존 5년) | 전면 차단 |
| `sessions` | `{userId}` | 계정 수 | × | 전면 차단 |
| `idempotency` | `{scope}_{key}` | 요청 수(단기) | **24시간** | 전면 차단 |
| `catalog/packs`, `catalog/items` | id | ≤ 50 | × | 전면 차단(서버 경유 노출) |
| `config/billing` | 고정 1개 | 1 | × | 전면 차단 |
| `frameEvents/{uid}/events` | UUID | 계정당 ≤ 200 | 1년 | 전면 차단 |

### 5.1 보안 규칙 (`web/firestore.rules` 추가)

```
// 과금 컬렉션 전량 SDK 접근 차단 — 서버(Admin SDK)만 접근한다.
match /wallets/{uid}       { allow read, write: if false; }
match /wallets/{uid}/entries/{e} { allow read, write: if false; }
match /entitlements/{uid}  { allow read, write: if false; }
match /entitlements/{uid}/grants/{g} { allow read, write: if false; }
match /usage/{doc}         { allow read, write: if false; }
match /payments/{p}        { allow read, write: if false; }
match /sessions/{uid}      { allow read, write: if false; }
match /idempotency/{k}     { allow read, write: if false; }
match /catalog/{doc=**}    { allow read, write: if false; }
match /config/{doc}        { allow read, write: if false; }   // 기존 tempUserLimits와 동일
match /frameEvents/{uid}/{e} { allow read, write: if false; }
```

| 규칙 | 근거 |
|------|------|
| 전량 deny | 웹 클라이언트는 **백엔드 API만** 쓴다(현행 규약 — [`analysis/40 §5.3`](../analysis/40-database-firestore-and-storage-schema.md)). 잔액·결제를 SDK로 읽게 열면 규칙 실수 1개가 전 계정 금액 유출이 된다 |
| `{document=**}` 기본 deny 존재 | 이미 있으므로 명시 규칙은 **의도 문서화** 목적이 크다. 그래도 명시한다(리뷰 가능성) |

### 5.2 인덱스

| 쿼리 | 인덱스 | 비고 |
|------|--------|------|
| `wallets/{uid}/entries` `orderBy(seq desc) limit(20)` | 단일 필드(자동) | |
| `payments` `where(userId==) orderBy(createdAt desc)` | **복합 필요** | `userId asc, createdAt desc` |
| `payments` `where(status==) where(createdAt<)` (만료 정리) | 복합 | 배치 잡용 |
| `payments` `where(providerTxnId==)` | 단일(자동) | 멱등 조회 |
| `usage` 계정 시계열 | 문서 id 조회(쿼리 불필요) | id에 날짜 포함 |
| `frameTemplates` `where(userId==)` | 자동(기존 사용 중) | 변경 없음 |

### 5.3 `config/billing` 스키마

| 필드 | 타입 | 기본 | 의미 |
|------|------|------|------|
| `enabled` | bool | **false** | 과금 기능 마스터 스위치(구매 차단) |
| `dryRun` | bool | **true** | 차감 없이 "차감했을 것"만 기록([10 §2](./10-rollout-testing-and-wbs.md)) |
| `enforceQuota` | bool | false | 정원 초과 시 실제 거부 여부(false면 경고만) |
| `accountDailyHardCap` | int | 300 | 계정 일 하드캡 |
| `globalDailyCap` | int | 20000 | 전역 일 상한 |
| `killSwitch` | bool | false | 전역 차단(무료 티어로 폴백) |
| `overageDefaultEnabled` | bool | false | 오버리지 기본값 |
| `overageMcPerSession` | int | 2 | 오버리지 단가 |
| `frameCreatePriceMc` | int | 5 | 프레임 생성 MC(카탈로그와 이중화 — 카탈로그가 정본, 이 값은 폴백) |
| `singleSessionEnabled` | bool | false | 단일 세션 강제 |
| `sessionGraceSeconds` | int | 180 | 유예 |
| `heartbeatSeconds` | int | 90 | 클라 하트비트 주기(서버가 지시) |
| `purgeLocalUserFramesOnBoot` | bool | false | 부팅 purge 활성 |
| `migrationDeadlineMs` | int? | null | 레거시 프레임 이관 유예 종료 |
| `updatedAt`, `updatedBy` | | | 감사 |

> ✅ **하트비트 주기를 서버가 지시**하는 이유: 비용·부하에 따라 클라 재배포 없이 조절할 수 있다. 클라는 `heartbeatSeconds`를 `GET /config/billing` 또는 하트비트 응답에서 받아 반영한다(하한 30초·상한 600초로 클램프).

---

## 6. 마이그레이션 (기존 데이터 처리)

| # | 대상 | 조치 | 시점 |
|:--:|------|------|------|
| M1 | `users.qrUsedCount` | **유지**. 무료 티어 누적 카운터로 계속 사용. 새 `usage` 문서와 **혼동 금지**([03 §4.2](./03-qr-daily-quota.md)) | — |
| M2 | 기존 계정 지갑 | 생성하지 않는다(lazy). 첫 접근 시 생성 | 자동 |
| M3 | 기존 `user`+ 계정의 QR 무제한 | **이행 지급**: `qr_d30_30d` 상당 grant 1건 무상 부여(스크립트) | 시행일 전 |
| M4 | 기존 로컬 전용 개인 프레임 | 이관 유도 30일([04 §7](./04-custom-frames-billing-and-lifecycle.md)) | 시행 전 |
| M5 | 구버전 클라(`sid` 없는 토큰) | 세션 검증 스킵 → 전환 종료 후 강제 | 2개월 |
| M6 | `config/tempUserLimits` | **유지**(무료 티어 전역 한도). `config/billing`과 별 문서 | — |
| M7 | 카탈로그 초기 데이터 | 시드 스크립트(`web/functions/scripts/seed-catalog.mjs`) | 배포 시 |
| M8 | 기존 `POST /frames` 호출자(power 공용) | **무변경**. 개인 경로는 신설이라 회귀 없음 | — |

### 6.1 롤백 가능성

| 변경 | 롤백 방법 |
|------|-----------|
| 정원 강제 | `config/billing.enforceQuota = false` → 즉시 무제한(과금 전 상태) |
| 구매 | `enabled = false` → 구매 UI 숨김. 이미 산 권리는 유효 |
| 단일 세션 | `singleSessionEnabled = false` → 세션 대조 스킵 |
| 프레임 서버 저장 | ⚠️ **되돌리기 어렵다**(문서·Storage가 생성됨). 롤백은 "새 생성만 로컬로" 형태이며 이미 서버에 있는 프레임은 유지 |
| purge | `purgeLocalUserFramesOnBoot = false` |
| JWT `sid` | 무해(클레임 추가). 검증만 끄면 된다 |

> ✅ **플래그 기반 롤백이 가능한 설계**가 이 문서의 요구사항이다. 코드 배포 없이 되돌릴 수 있어야 상용 서비스에서 사고를 수습할 수 있다.

---

## 7. 서버 구성값 추가

| 키 | 출처 | 필수 | 용도 |
|----|------|:---:|------|
| `PG_PROVIDER` | env | PG 사용 시 | `portone`/`toss` 등 |
| `PG_API_SECRET` | Secret Manager | 동상 | 결제 조회·취소 API |
| `PG_WEBHOOK_SECRET` | Secret Manager | 동상 | 웹훅 서명 검증 |
| `APPLE_ISSUER_ID`·`APPLE_KEY_ID`·`APPLE_PRIVATE_KEY` | Secret Manager | iOS IAP 시 | App Store Server API |
| `APPLE_BUNDLE_ID` | env | 동상 | 영수증 대조 |
| `GOOGLE_PLAY_SA_KEY` | Secret Manager | Android IAP 시 | Play Developer API |
| `GOOGLE_PLAY_PACKAGE` | env | 동상 | 패키지명 대조 |
| `BILLING_TZ` | env | — | 기본 `Asia/Seoul`(변경 비권장) |
| `ADMIN_ALERT_WEBHOOK` | Secret Manager | — | 정합성 불일치·전역 상한 도달 알림 |

> ⚠️ 기존 규약대로 **필수값 누락 시 로드 시점 예외로 조기 실패**한다(오구성 배포 방지 — [`analysis/31 §9`](../analysis/31-backend-api-reference.md)). 단 **과금 시크릿은 `enabled=false`일 때 없어도 로드에 실패하지 않아야 한다**(과금 미사용 배포 지원) → 조건부 검증.
