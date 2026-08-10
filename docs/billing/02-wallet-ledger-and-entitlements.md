# 02 · 지갑 · 원장 · 권리(entitlement)

| 항목 | 내용 |
|------|------|
| 문서 | MC 잔액을 어디에 두고, 어떻게 틀리지 않게 바꾸고, 어떻게 증명하는가 |
| 범위 | 지갑/원장 데이터 모델, 멱등, 트랜잭션 경계, 권리(정원 플랜) 모델, 정합성 감사, 동시성 |
| 최종 업데이트 | 2026-08-06 (신규) |
| 관련 소스(패턴 근거) | `web/functions/src/services/uploads.ts`(commit 트랜잭션 — 이중집계 차단 선례), `web/functions/src/services/accounts.ts`(create 경합 흡수 선례) |
| 관련 문서 | [00 §6](./00-scope-principles-and-model.md)(불변식) · [03](./03-qr-daily-quota.md)(정원 소비) · [07 §5](./07-api-and-data-contract.md)(스키마 전수) |

---

## 1. 왜 "잔액 필드 하나"로는 안 되는가

가장 단순한 설계는 `users.mcBalance` 필드 하나를 두고 증감하는 것이다. 이 설계가 실패하는 지점은 전부 **사후에** 드러난다.

| 실패 시나리오 | 잔액 필드만 있을 때 | 원장이 있을 때 |
|---------------|---------------------|----------------|
| 사용자: "10,000원 충전했는데 잔액이 20MC밖에 없어요" | 증명 불가. 로그를 뒤져야 하고 로그는 30일 뒤 사라진다 | 원장 조회 1회로 전 이력 제시 |
| 네트워크 재시도로 두 번 차감 | 조용히 발생하고 아무도 모른다 | 멱등키로 차단 + 중복 시도 기록 |
| 결제 웹훅이 3번 도착 | 3배 지급 | 동일 `paymentId`로 1회만 |
| 환불했는데 사용자가 이미 다 썼다 | 잔액이 음수가 되거나 회수 실패가 은폐된다 | `refund` 항목 + 부족분(`shortfall`) 명시 기록 |
| 관리자가 실수로 잔액을 덮어썼다 | 원래 값을 모른다 | 이전 항목들이 그대로 남아 재계산 가능 |
| 회계: 이번 달 MC 사용액(매출 인식분)이 얼마인가 | 계산 불가 | `spend` 합계 집계 |

> ✅ **결론**: 잔액은 **원장의 파생 캐시**다. 원장이 정본이다. 이것이 불변식 B1([00 §6](./00-scope-principles-and-model.md))의 이유다.

---

## 2. 데이터 모델

### 2.1 컬렉션 구조

```
wallets/{userId}                        지갑 요약(잔액 캐시 + 낙관적 버전)
wallets/{userId}/entries/{entryId}      원장 — append-only, 삭제·수정 금지
entitlements/{userId}                   활성 권리 요약(QR 정원 플랜 목록)
entitlements/{userId}/grants/{grantId}  권리 부여 이력(플랜 1건 = 문서 1개)
usage/{userId}_{billingDay}             일자별 사용량(lazy reset 대상) → 03 §4
idempotency/{scope}_{key}               멱등 기록(TTL 삭제 대상)
payments/{paymentId}                    결제 주문·상태(→ 05)
catalog/items/{itemId}, catalog/packs/{packId}   상품 카탈로그(→ 01 §6)
config/billing                          기능 플래그·킬스위치·전역 상한(→ 03 §7)
```

**왜 `users` 문서에 넣지 않는가**

| 이유 | 설명 |
|------|------|
| 경합 분리 | `users`는 인증·권한 read 경로다(모든 Bearer 요청). 여기에 지갑 write가 섞이면 트랜잭션 충돌 확률이 올라간다 |
| 권한 분리 | 지갑은 본인 + admin만 본다. 계정 목록(`GET /accounts`, power)에 잔액이 딸려 나가는 사고를 원천 차단 |
| cascade 분리 | 계정 삭제 시 지갑은 지우되 **원장은 보존**해야 한다(회계 5년). 서브컬렉션이 `users` 밑이면 실수로 함께 지운다 |

> ⚠️ **단, `users.qrUsedCount`(현행 TempUser 누적 카운터)는 남긴다.** 삭제하면 기존 무료 티어 판정이 깨진다. 마이그레이션 규약은 [07 §6](./07-api-and-data-contract.md).

### 2.2 `wallets/{userId}`

| 필드 | 타입 | 의미 | 주의 |
|------|------|------|------|
| `userId` | string | 계정 id(문서 ID와 동일) | |
| `balanceMc` | int ≥ 0 | 현재 잔액(**원장 파생 캐시**) | 음수 불가(B2) |
| `lifetimePurchasedMc` | int ≥ 0 | 누적 충전(유료) MC | 환불 계산·VIP 판정용 |
| `lifetimeGrantedMc` | int ≥ 0 | 누적 지급(무상) MC | 환불 대상 아님 표시용 |
| `lifetimeSpentMc` | int ≥ 0 | 누적 소비 MC | 매출 인식 참고 |
| `entrySeq` | int ≥ 0 | 원장 시퀀스(마지막 항목 번호) | 원장 순서 보장 + 누락 검출 |
| `updatedAt` | timestamp | 마지막 변동 | |
| `createdAt` | timestamp | 지갑 생성(최초 접근 시 lazy 생성) | |
| `frozen` | bool | 동결 여부(부정사용 조사·환불 분쟁 중) | true면 모든 `spend`·구매 거부 → [09 §4](./09-security-abuse-and-compliance.md) |
| `frozenReason` | string? | 동결 사유(관리자 입력) | 사용자에게는 일반화 문구만 노출 |

> **지갑은 lazy 생성**한다. 로그인만 한 계정에 문서를 만들지 않는다(문서 수·write 절약). 최초 `purchase`/`grant`/조회 시 생성한다. **조회 시 미존재는 잔액 0으로 응답**하고 문서를 만들지 않는다(read-only 경로에서 write 금지 — 서버리스 비용·권한 사고 방지).

### 2.3 `wallets/{userId}/entries/{entryId}` — 원장

| 필드 | 타입 | 의미 |
|------|------|------|
| `entryId` | string | `{seq를 12자리 zero-pad}_{uuid8}` — **시간순 정렬 가능** + 충돌 방지. 예: `000000000042_9f1c2a44` |
| `seq` | int | 지갑별 단조 증가 시퀀스(1부터). 트랜잭션에서 `wallets.entrySeq + 1` |
| `type` | enum | `purchase` \| `grant` \| `spend` \| `refund` \| `adjust` \| `expire` |
| `deltaMc` | int | 부호 있는 변동량. `purchase`/`grant`/`adjust(+)` > 0, `spend`/`refund`/`expire` < 0 |
| `balanceAfter` | int ≥ 0 | 이 항목 적용 후 잔액(감사·표시용, 재계산 검증 대상) |
| `reason` | string | 기계 판독용 사유 코드(예: `qr_plan_purchase`, `frame_create`, `pg_refund`, `admin_grant`) |
| `refType` | string? | 참조 대상 종류: `payment` \| `item` \| `frame` \| `session` \| `admin` |
| `refId` | string? | 참조 id(예: `paymentId`, `itemId`, `frameId`, `sessionId`) |
| `itemSnapshot` | map? | 아이템 구매 시 `{itemId, version, priceMc, title}` **스냅샷**. 카탈로그가 나중에 바뀌어도 이력이 정확하다 |
| `idempotencyKey` | string? | 이 항목을 만든 요청의 멱등키 |
| `actor` | map | `{type: "user"\|"admin"\|"system", id: string}` — 누가 일으켰나 |
| `platform` | string? | `windows` \| `web` \| `ios` \| `android` \| `server` |
| `memo` | string? | 관리자 조정 사유(사용자에게 노출하지 않음) |
| `createdAt` | timestamp | **서버 시각** |

**규약**

| # | 규약 | 이유 |
|---|------|------|
| L1 | **수정·삭제 금지.** 잘못된 항목은 반대 부호 `adjust`로 정정한다 | 감사 무결성. 회계 원칙(수정분개) |
| L2 | 항목 생성은 **항상 지갑 문서 갱신과 같은 트랜잭션**에서 | B1·B4 |
| L3 | `balanceAfter`는 계산해 저장한다(중복이지만 의도적) | 원장만 보고 잔액 궤적을 재현할 수 있어야 한다 |
| L4 | `type`은 6종으로 **고정**한다. 새 사유는 `reason` 문자열로 표현 | enum이 늘면 집계 쿼리·회계 매핑이 계속 깨진다 |
| L5 | 원장은 계정 삭제 시에도 **보존**한다(별 보관 컬렉션으로 이관 또는 `wallets` 문서만 삭제) | 전자상거래법상 대금결제 기록 5년 → [09 §7](./09-security-abuse-and-compliance.md) |

### 2.4 `type`별 의미와 발생 경로

| type | delta | 발생 경로 | 환불 대상 | 비고 |
|------|:---:|-----------|:---:|------|
| `purchase` | + | PG 결제 승인 / IAP 영수증 검증 성공 | ○ | `refId` = `paymentId` |
| `grant` | + | 관리자 지급, 프로모, 장애 보상 | × | 무상분. 환불 계산에서 제외 |
| `spend` | − | 아이템 구매(정원 플랜·프레임 생성권), 오버리지 1회 차감 | — | `refId` = `itemId`/`frameId`/`sessionId` |
| `refund` | − | 결제 취소·환불 승인 시 MC 회수 | — | 잔액 부족 시 §5.3 |
| `adjust` | ± | 관리자 수동 보정(오류 정정) | × | `memo` 필수, admin만 |
| `expire` | − | MC 유효기간 만료(현 정책은 **미사용** — 유효기간 없음) | — | 정책 변경 시 사용할 자리만 예약 |

---

## 3. 권리(entitlement) 모델 — 정원 플랜을 어떻게 표현하나

### 3.1 왜 잔액과 분리하나

QR 정원 플랜은 "MC가 줄어드는 것"이 아니라 **"하루 N개를 30일간 쓸 수 있는 상태"** 다. 잔액으로 표현하면(예: 1,500 크레딧 충전) 다음이 표현되지 않는다.

| 표현 불가 항목 | 권리 모델에서는 |
|----------------|-----------------|
| "하루 최대 N개"라는 상한 | `dailyAllowance` |
| "30일 후 만료" | `expiresAt` |
| "첫날은 일할 계산" | `firstDayAllowance` |
| 플랜 2개 중복 보유 시 정원 합산 | 활성 grant들의 `dailyAllowance` 합 |
| 환불 시 특정 플랜만 회수 | grant 단위 `revoked` |

### 3.2 `entitlements/{userId}/grants/{grantId}`

| 필드 | 타입 | 의미 |
|------|------|------|
| `grantId` | string | UUID |
| `kind` | `"qr_plan"` \| `"qr_credit"` \| `"frame_create"` | 권리 종류 |
| `itemId` / `itemVersion` | string / int | 구매한 카탈로그 아이템(스냅샷) |
| `dailyAllowance` | int ≥ 0 | 하루 정원(qr_plan). `qr_credit`은 0 |
| `firstDayAllowance` | int ≥ 0 | **구매 당일에만** 적용되는 정원(프로레이션 결과) → [03 §5](./03-qr-daily-quota.md) |
| `remainingCredits` | int? | `qr_credit`의 잔여 횟수(총량형) |
| `startedAt` | timestamp | 부여 시각(서버) |
| `startBillingDay` | string | `yyyy-MM-dd`(KST) — 프로레이션 판정 기준일 |
| `expiresAt` | timestamp | 만료 시각. `qr_plan`은 `startBillingDay + durationDays`의 **KST 24:00** |
| `revoked` | bool | 환불·부정으로 회수됨 |
| `revokedAt` / `revokeReason` | timestamp? / string? | |
| `ledgerEntryId` | string | 이 권리를 만든 원장 항목(역참조) |
| `platform` | string | 구매 플랫폼 |

### 3.3 `entitlements/{userId}` — 요약 문서 (읽기 최적화)

| 필드 | 타입 | 의미 |
|------|------|------|
| `activeQrDailyAllowance` | int | 활성 `qr_plan`의 `dailyAllowance` 합(만료·회수 제외) |
| `activeQrCredits` | int | 활성 `qr_credit`의 `remainingCredits` 합 |
| `frameCredits` | int ≥ 0 | 보유 프레임 생성권 수 |
| `nearestExpiryAt` | timestamp? | 가장 이른 만료(UI 안내용) |
| `recomputedAt` | timestamp | 요약 재계산 시각 |
| `activeGrantIds` | string[] | 활성 grant id 목록(상한 20, 초과 시 요약만 신뢰) |

> ⚠️ **요약은 캐시다.** 판정은 요약을 쓰되, **만료 판정은 항상 서버 시각과 `expiresAt`을 다시 비교**한다(요약이 낡아 만료된 플랜을 유효로 볼 수 있다). 요약 갱신은 ① grant 생성/회수 시 ② 만료가 지난 grant를 처음 발견한 요청에서 lazy 수행한다. **스케줄러로 매일 재계산하지 않는다**([00 §8](./00-scope-principles-and-model.md)의 lazy 원칙과 동일).

### 3.4 왜 `frameCredits`는 카운터인가

프레임 생성권은 정원·기간이 없는 **단순 소비형**이다. 그래서 grant 문서를 만들지 않고 요약 카운터 + 원장으로 충분하다.

| 흐름 | 원장 | 요약 |
|------|------|------|
| 생성권 구매(3개 묶음) | `spend -15MC`, `reason=frame_credit_purchase` | `frameCredits += 3` |
| 프레임 생성 성공 | (MC 변동 없음) 별 사용 기록 `frameUsage` 또는 `adjust 0` 금지 → **`frameCredits` 감소는 원장 대신 `entitlements` 트랜잭션 + `frameEvents` 서브컬렉션 기록** | `frameCredits -= 1` |

> ✅ **직접 차감(생성권 없이 즉시 MC 차감)도 허용**한다. 클라가 "생성권 없으면 MC로 바로 결제" 흐름을 쓸 수 있게 서버가 두 경로를 지원한다(`useCredit: true|false`). 상세 [04 §4.3](./04-custom-frames-billing-and-lifecycle.md).

---

## 4. 멱등성 (B3)

### 4.1 왜 필수인가

| 상황 | 멱등 없으면 |
|------|-------------|
| 클라가 응답을 못 받아 재시도 | 이중 차감/이중 지급 |
| PG·스토어 웹훅 재전송(정상 동작이다) | 다중 지급 |
| 사용자가 버튼을 두 번 누름 | 이중 구매 |
| Cloud Functions 재시도(2nd gen은 HTTP 함수라 자동 재시도 없음, 그러나 클라 재시도는 있다) | 동상 |

### 4.2 규격

| 항목 | 규격 |
|------|------|
| 전달 방식 | 요청 본문 `idempotencyKey`(문자열, `^[A-Za-z0-9._-]{8,64}$`). 헤더가 아닌 **본문**에 둔다 — 프록시·CORS 노출 헤더 관리를 늘리지 않기 위해 |
| 생성 주체 | **클라이언트**. 사용자 행위 1건당 1개 생성(UUIDv4 권장), 재시도 시 **같은 키 재사용** |
| 저장 | `idempotency/{scope}_{key}` — `scope` = `purchase`/`item_buy`/`frame_create`/`webhook` |
| 문서 내용 | `{scope, key, userId, requestHash, status: "in_progress"\|"done", responseSnapshot, createdAt, expiresAt}` |
| 유효기간 | **24시간**(Firestore TTL로 자동 삭제). 그 뒤 같은 키는 새 요청으로 취급 |
| 충돌 처리 | ① `done` → 저장된 응답 **그대로 반환**(200, 부작용 없음) ② `in_progress` → **409 `IN_PROGRESS`**(클라는 잠시 후 재시도) ③ 같은 키에 **다른 요청 본문**(`requestHash` 불일치) → **400 `IDEMPOTENCY_KEY_REUSE`** |
| 트랜잭션 | 멱등 문서 생성과 실제 차감은 **같은 트랜잭션**에서. 별 트랜잭션이면 "차감했는데 멱등 기록 실패" 창이 열린다 |

### 4.3 웹훅 멱등 (결제 채널)

| 채널 | 멱등 키 원천 |
|------|--------------|
| PG(국내) | PG의 거래 고유번호(`imp_uid`/`paymentKey` 등) |
| Apple | `transactionId`(JWS 서명 트랜잭션) |
| Google Play | `purchaseToken`(+ `orderId`) |

> ⚠️ **웹훅은 순서를 보장하지 않는다.** "환불 알림이 결제 알림보다 먼저" 도착할 수 있다. 상태 머신은 **역행 전이를 무시**해야 한다([05 §4](./05-payments-and-platform-policies.md)).

---

## 5. 트랜잭션 설계

### 5.1 기본 패턴 (it13 선례 재사용)

it13의 `commitTempUserSession`이 확립한 패턴을 그대로 쓴다(`web/functions/src/services/uploads.ts`):

```
runTransaction(tx => {
  1. (트랜잭션 밖) 변하지 않는 참조 데이터 로드 — 카탈로그·전역 설정
  2. tx.get(멱등 문서)      → 이미 done이면 저장 응답 반환(부작용 없음)
  3. tx.get(지갑)          → 잔액 확인
  4. tx.get(권리/사용량)    → 정원·만료 재판정
  5. 판정 실패 → throw (403/409) — 이 시점까지 write 없음
  6. tx.set/update(대상 문서들 + 원장 항목 + 지갑 + 멱등 done)
})
```

**규칙**

| # | 규칙 | 이유 |
|---|------|------|
| T1 | **모든 read를 모든 write보다 먼저** 한다 | Firestore 트랜잭션 제약 |
| T2 | 카탈로그·`config/billing`은 **트랜잭션 밖**에서 읽는다 | 경합 대상이 아니고, 트랜잭션 read 수를 늘리면 충돌·재시도 비용이 커진다(it13이 `loadTempUserLimits`를 밖에서 읽는 것과 동일) |
| T3 | 트랜잭션 안에서 **외부 호출(PG·스토어 API·Storage 서명) 금지** | 재시도 시 중복 외부 호출. 검증은 트랜잭션 **전에** 끝낸다 |
| T4 | 한 트랜잭션이 만지는 문서는 **6개 이하**로 유지(지갑·원장·권리요약·사용량·멱등·대상) | 충돌률·지연 관리 |
| T5 | 실패는 **부분 적용 없이** 전체 롤백 | Firestore 트랜잭션이 보장. 단 Storage 부작용은 트랜잭션 밖이므로 [04 §4.4](./04-custom-frames-billing-and-lifecycle.md)의 보상 규약을 따른다 |

### 5.2 동시성 — 무엇이 실제로 경합하나

| 경합 지점 | 빈도 | 대응 |
|-----------|------|------|
| 같은 계정의 QR commit 2건 동시 | B2B 부스에서 실제로 발생(2대 부스, 같은 계정) | 트랜잭션 직렬화로 한 건만 통과 → 정원 초과 방지. ⚠️ **세션 통제를 하지 않으므로**([06](./06-single-session-enforcement.md) 2026-08-10 폐기) **이 경합은 상시 발생한다.** 정원 초과를 막는 것은 이 트랜잭션 **하나뿐**이다(종전에는 불변식 B11이 이중 소비 자체를 막았으나 함께 폐기됐다 → [00 §6](./00-scope-principles-and-model.md)) |
| 지갑 문서 write 빈도 | 계정별 초당 1회 한계(Firestore 단일 문서) | 실사용에서 계정당 초당 1건 이상 구매·소비는 없다. 정원 소비는 지갑을 건드리지 않는다(§5.4) |
| 원장 append | 서브컬렉션이라 문서마다 다름 | 경합 없음 |
| 카탈로그 read | 매 구매 | 캐시(Functions 인스턴스 메모리 60초 TTL) 허용 — 가격 변경 시 `version` 검증이 안전망 |

### 5.3 환불 시 잔액 부족(shortfall) — 실무 최대 함정

사용자가 10,000원(100MC) 충전 → 90MC 소비 → 환불 요청. 회수해야 할 100MC 중 **10MC만 남아 있다.**

| 정책 | 처리 | 채택 |
|------|------|:---:|
| (a) 잔액 음수 허용 | `balanceMc = -90` | ✕ B2 위반 |
| (b) **회수 가능분만 회수 + 부족분 기록** | `refund -10MC`(잔액 0) + `shortfallMc: 90` 기록 → 환불 금액을 **미사용분에 비례**해 부분 환불 | **○** |
| (c) 환불 거부 | 소비자 분쟁·법적 리스크 | ✕ |

**채택안 (b)의 규격**

| 항목 | 규격 |
|------|------|
| 환불 가능 금액 | `min(잔액, 결제 MC) × 결제 시 1MC 단가` (예: 잔액 10MC × 85원 = 850원) |
| 원장 | `refund` 항목에 `{requestedMc: 100, revokedMc: 10, shortfallMc: 90}` 기록 |
| 사용된 권리 | 이미 소비된 정원·프레임은 **회수하지 않는다**(사용 완료). 미소비 활성 플랜은 `revoked: true` 처리 |
| 부분 환불 실행 | PG는 부분 취소 지원. IAP는 **부분 환불이 어렵다**(스토어 재량) → [05 §5.2](./05-payments-and-platform-policies.md) |
| 결과 고지 | "사용하신 90MC를 제외한 850원이 환불됩니다" 문구 필수 → [08 §7](./08-ui-ux-and-copy.md) |

### 5.4 소비 경로별 트랜잭션 대상 (성능 설계)

| 소비 | 지갑 write | 원장 write | 권리 write | 사용량 write | 비고 |
|------|:---:|:---:|:---:|:---:|------|
| MC 팩 구매(결제 승인) | ○ | ○ | × | × | 웹훅 경로 |
| 정원 플랜 구매 | ○ | ○ | ○(grant + 요약) | × | 사용자 행위 |
| 프레임 생성권 구매 | ○ | ○ | ○(요약 카운터) | × | |
| **QR 전송 1건(정원 내)** | **×** | **×** | × | ○ | ⚠️ **핫패스** — 지갑·원장을 건드리지 않는다 |
| QR 전송 1건(오버리지, MC 직접 차감) | ○ | ○ | × | ○ | 옵션 기능 |
| 프레임 생성 1건(생성권 사용) | × | × | ○(카운터 −1) | × | `frameEvents` 기록 |
| 프레임 생성 1건(MC 직접) | ○ | ○ | × | × | |

> ✅ **핫패스 설계 근거**: QR 전송은 가장 빈번한 소비다. 여기서 매번 원장 항목을 쓰면 ① write 비용 ② 지갑 문서 경합 ③ 원장 폭증(월 수천 건/계정)이 발생한다. 정원 소비는 **`usage` 문서의 카운터**로만 기록하고, 원장에는 **플랜 구매 시점 1회**만 남긴다. 감사에는 `usage` 일자별 문서 + `resultSessions` 문서가 증거로 남아 충분하다.

---

## 6. 정합성 감사 (B1 유지 장치)

| 장치 | 내용 | 주기 |
|------|------|------|
| **원장 재계산 검증** | `Σ entries.deltaMc == wallets.balanceMc`, `entries.seq`가 1..N 연속, 각 항목의 `balanceAfter`가 누적과 일치 | 관리자 도구에서 계정 단위 온디맨드 + 주 1회 배치(수동 실행 스크립트) |
| **시퀀스 누락 검출** | `wallets.entrySeq`와 원장 최대 `seq` 비교 | 위와 동일 |
| **권리 요약 재계산** | `activeQrDailyAllowance == Σ 활성 grants.dailyAllowance` | 요약 갱신 시마다 + 배치 |
| **결제 대조** | `payments`(paid) 합계 == 원장 `purchase` 합계 | 월 1회 정산 |
| **불일치 발견 시** | **자동 보정 금지.** 관리자 알림 + `adjust`로 수동 정정(사유 필수) | — |

> ⚠️ **자동 보정을 금지하는 이유**: 불일치는 버그의 증상이다. 자동으로 맞추면 버그가 은폐되고, 최악의 경우 **버그가 만든 잘못된 값에 잔액을 맞춘다**. 사람이 원인을 확인하고 정정한다.

---

## 7. 집계·통계 준비 (미래 확장)

현재 서버에 통계 API가 없다([`analysis/31 §10`](../analysis/31-backend-api-reference.md)). 나중에 만들 수 있도록 **스키마만** 준비한다.

| 준비 | 내용 |
|------|------|
| 원장에 `platform`·`reason`·`itemSnapshot` 보존 | 채널별·상품별 매출 집계 가능 |
| `usage/{userId}_{billingDay}` 일자 문서 | 일별 사용량 시계열이 자연히 축적됨 |
| `payments` 상태 머신 타임스탬프 | 결제 전환율·실패율 분석 |
| BigQuery export(Firestore 확장) | 필요 시 활성화. 이 세트 범위 밖 |

---

## 8. 클라이언트 측 규약 (전 플랫폼 공통)

| # | 규약 | 이유 |
|---|------|------|
| C1 | 잔액·정원은 **서버 응답만** 표시한다. 로컬에서 계산·예측하지 않는다 | B5. 낙관적 감소(optimistic)는 실패 시 사용자에게 잘못된 잔액을 보여 준다 |
| C2 | 구매·소비 후 **서버 응답의 최신 잔액으로 갱신**한다(별 조회 불필요) | 왕복 절감 + 표시 일관성 |
| C3 | 잔액·정원 조회는 **캐시 + 명시적 갱신 시점**에만: 로그인 직후, 구매 후, 상점 진입, QR 전송 후, 수동 새로고침 | 폴링 금지(Firestore read 비용) |
| C4 | 멱등키는 **사용자 행위 시작 시 1개 생성**하고 재시도에 재사용한다. 화면 재진입 시 새로 만든다 | B3 |
| C5 | 잔액 조회 실패는 **fail-open 표시**("확인할 수 없음") — 기능 차단은 서버가 한다 | it13 `qr-usage` 실패 정책과 동일(fail-open, 과금 안전은 업로드 거부가 담보) |
| C6 | 잔액을 로그에 남기지 않는다(금액은 개인정보에 준해 취급). 오류 로그에는 사유 코드만 | 웹 `authStore.ts`가 토큰을 로그에 남기지 않는 규약과 동일 |

---

## 9. 실패 모드 표 (설계 시 반드시 처리)

| # | 실패 | 서버 동작 | 클라 동작 |
|---|------|-----------|-----------|
| F1 | 잔액 부족 | 409 `INSUFFICIENT_MC` + `{required, balance}` | 부족 모달 + [충전] |
| F2 | 카탈로그 버전 불일치 | 409 `PRICE_CHANGED` | 카탈로그 재조회 후 확인 모달 재표시 |
| F3 | 멱등키 진행 중 | 409 `IN_PROGRESS` | 1.5초 후 1회 재시도 → 실패 시 "잠시 후 다시 시도" |
| F4 | 지갑 동결 | 403 `WALLET_FROZEN` | "고객센터 문의" 안내(사유 비노출) |
| F5 | 트랜잭션 충돌(Firestore 재시도 초과) | 500 `internal` | "잠시 후 다시 시도"(재시도 시 **같은 멱등키**) |
| F6 | 결제 성공 · 지급 실패(웹훅 처리 오류) | 웹훅 재시도 대상으로 남김 + 알림 | 결제 이력에 "처리 중" 표시, 10분 후에도 미지급이면 문의 안내 |
| F7 | 권리 만료 경계에서 소비 시도 | 403 `QUOTA_EXHAUSTED`(만료로 정원 0) | "플랜이 만료되었습니다" + 재구매 |
| F8 | 계정 삭제 중 소비 요청 | 404/403 | 로그아웃 처리 |
