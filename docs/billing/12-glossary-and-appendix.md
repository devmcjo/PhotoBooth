# 12 · 용어집 · 계산 부록

| 항목 | 내용 |
|------|------|
| 문서 | 용어 정의(색인), 프로레이션 전수표(24시간 × 5정원), 원가 계산 근거, 판정 함수 의사코드, 참조 문서 지도 |
| 최종 업데이트 | 2026-08-06 (신규) |

---

## 1. 용어 색인

| 용어 | 한 줄 정의 | 상세 |
|------|-----------|------|
| MC | 서비스 내 전용 선불 크레딧(정수, 1MC ≈ 100원 기준) | [01 §2](./01-currency-catalog-and-pricing.md) |
| 지갑(wallet) | 계정별 MC 잔액 문서. 원장의 파생 캐시 | [02 §2.2](./02-wallet-ledger-and-entitlements.md) |
| 원장(ledger) | 잔액 변동의 append-only 기록. 정본 | [02 §2.3](./02-wallet-ledger-and-entitlements.md) |
| 권리(entitlement) | 기간·정원 형태의 사용 권리(플랜·회수권·생성권) | [02 §3](./02-wallet-ledger-and-entitlements.md) |
| grant | 권리 부여 1건(문서 1개) | [02 §3.2](./02-wallet-ledger-and-entitlements.md) |
| 정원(allowance) | 하루에 쓸 수 있는 최대 QR 전송 수 | [03 §2](./03-qr-daily-quota.md) |
| 프로레이션 | 구매 첫날 정원을 남은 시간 비율로 축소하는 계산 | [03 §5](./03-qr-daily-quota.md) · §2 |
| 영업일(billingDay) | `Asia/Seoul` 기준 `yyyy-MM-dd`. 리셋·집계의 유일 기준 | [03 §4.1](./03-qr-daily-quota.md) |
| lazy reset | 스케줄러 없이 "일자별 문서 부재 = 0"으로 리셋을 성립시키는 방식 | [03 §4.2](./03-qr-daily-quota.md) |
| 오버리지(overage) | 정원 초과분을 MC로 즉시 차감해 계속 쓰는 옵션 | [03 §2.1](./03-qr-daily-quota.md) |
| 하드캡 | 상품과 무관하게 계정에 걸리는 일 상한(안전장치) | [03 §7](./03-qr-daily-quota.md) |
| 킬스위치 | 전역 과금 차단 플래그. 켜도 무료 티어는 동작 | [03 §7](./03-qr-daily-quota.md) · B12 |
| 무료 티어 | temp_user 전용 48h/30회 무료 사용(기존 it13 기능) | [03 §3](./03-qr-daily-quota.md) |
| 소비 단위 | `POST /uploads/commit` 최초 성공 1건 = QR 1개 | [03 §1](./03-qr-daily-quota.md) |
| 멱등키 | 재시도를 1회 효과로 만드는 클라 생성 키 | [02 §4](./02-wallet-ledger-and-entitlements.md) |
| dry-run | 차감 없이 "차감했을 것"만 기록하는 계측 모드 | [10 §2](./10-rollout-testing-and-wbs.md) |
| 이행 지급 | 과금 시행 시 기존 계정에 주는 무상 플랜 | [10 §3](./10-rollout-testing-and-wbs.md) |
| 접두 규약 | 로컬 프레임 파일명 규칙(`{계정}_{이름}` = 개인, 접두 없음 = 공용) | [04 §1](./04-custom-frames-billing-and-lifecycle.md) |
| purge | 로그아웃 시 로컬 개인 프레임 삭제 | [04 §5](./04-custom-frames-billing-and-lifecycle.md) |
| 좌석(seat) | **다점포 운영자용 상품 축(정의 미확정).** 종전 "계정당 허용 동시 세션 수" 정의는 [06](./06-single-session-enforcement.md) 폐기와 함께 **무효**다 — 동시 세션을 셀 수 없다. 관찰된 다점포 사용 패턴은 유효하며 축 재설계가 필요하다 | [11 D-26](./11-open-decisions.md) |
| B2B-부스 / B2C-개인 | 결제 주체가 운영자인지 본인인지 구분하는 사용자 프로파일 | [00 §4](./00-scope-principles-and-model.md) |
| FIFO 소비 가정 | 환불 계산을 위해 "충전 순서대로 사용"으로 본다는 약관 규약 | [05 §6](./05-payments-and-platform-policies.md) |
| shortfall | 환불 시 회수하려는 MC가 잔액보다 많아 생기는 부족분 | [02 §5.3](./02-wallet-ledger-and-entitlements.md) |

---

## 2. 프로레이션 전수표 (24시간 × 5정원)

**공식**: `firstDayAllowance(N, H) = clamp(ceil(N × (24 − H) / 24), 1, N)`, `H` = KST 구매 시각의 시(내림)

| 구매 시각(KST) | H | (24−H)/24 | N=10 | N=30 | N=50 | N=100 | N=200 |
|---|:--:|---:|---:|---:|---:|---:|---:|
| 00:00~00:59 | 0 | 1.00000 | 10 | 30 | 50 | 100 | 200 |
| 01:00~01:59 | 1 | 0.95833 | 10 | 29 | 48 | 96 | 192 |
| 02:00~02:59 | 2 | 0.91667 | 10 | 28 | 46 | 92 | 184 |
| 03:00~03:59 | 3 | 0.87500 | 9 | 27 | 44 | 88 | 175 |
| 04:00~04:59 | 4 | 0.83333 | 9 | 25 | 42 | 84 | 167 |
| 05:00~05:59 | 5 | 0.79167 | 8 | 24 | 40 | 80 | 159 |
| 06:00~06:59 | 6 | 0.75000 | 8 | 23 | 38 | 75 | 150 |
| 07:00~07:59 | 7 | 0.70833 | 8 | 22 | 36 | 71 | 142 |
| 08:00~08:59 | 8 | 0.66667 | 7 | 20 | 34 | 67 | 134 |
| 09:00~09:59 | 9 | 0.62500 | 7 | 19 | 32 | 63 | 125 |
| 10:00~10:59 | 10 | 0.58333 | 6 | 18 | 30 | 59 | 117 |
| 11:00~11:59 | 11 | 0.54167 | 6 | 17 | 28 | 55 | 109 |
| 12:00~12:59 | 12 | 0.50000 | 5 | 15 | 25 | 50 | 100 |
| 13:00~13:59 | 13 | 0.45833 | 5 | 14 | 23 | 46 | 92 |
| 14:00~14:59 | 14 | 0.41667 | 5 | 13 | 21 | 42 | 84 |
| 15:00~15:59 | 15 | 0.37500 | 4 | 12 | 19 | 38 | 75 |
| 16:00~16:59 | 16 | 0.33333 | 4 | 10 | 17 | 34 | 67 |
| **17:00~17:59** | **17** | **0.29167** | **3** | **9** | **15** | **30** | **59** |
| 18:00~18:59 | 18 | 0.25000 | 3 | 8 | 13 | 25 | 50 |
| 19:00~19:59 | 19 | 0.20833 | 3 | 7 | 11 | 21 | 42 |
| 20:00~20:59 | 20 | 0.16667 | 2 | 5 | 9 | 17 | 34 |
| 21:00~21:59 | 21 | 0.12500 | 2 | 4 | 7 | 13 | 25 |
| 22:00~22:59 | 22 | 0.08333 | 1 | 3 | 5 | 9 | 17 |
| 23:00~23:59 | 23 | 0.04167 | 1 | 2 | 3 | 5 | 9 |

**검증 포인트**

| # | 항목 | 결과 |
|:--:|------|------|
| 1 | 사용자 예시(17:30, N=100) | `ceil(100 × 7/24) = ceil(29.17) = 30` ✅ |
| 2 | 전 구간 최소 1개 보장 | ✅(H=23, N=10 → 1) |
| 3 | 상한 초과 없음 | ✅(H=0 → N) |
| 4 | 단조 비증가(H 증가 → 정원 감소 또는 동일) | ✅ |
| 5 | 분 단위 무시 | 17:00과 17:59가 동일 |

> ⚠️ **이 표가 테스트 데이터다.** [10 §4.2 G-표](./10-rollout-testing-and-wbs.md)의 Q1이 이 120칸을 그대로 InlineData로 고정한다. 표를 바꾸면 테스트를 함께 바꿔야 하고, 반대로 코드가 표와 어긋나면 테스트가 실패한다.

---

## 3. 원가 계산 근거 (재계산 가능한 형태)

### 3.1 가정값 (변경 시 여기만 고친다)

| 기호 | 의미 | 가정값 |
|------|------|--------|
| `S_photo` | 최종 사진 크기 | 1.5 MB |
| `S_tl` | 타임랩스 크기 | 8.0 MB |
| `D` | 보관 일수 | 3일(GCS Lifecycle) |
| `V` | 평균 열람 횟수(다운로드) | 1.3회 |
| `P_store` | Storage 저장 단가 | $0.026 / GB·월 |
| `P_egress` | 다운로드 단가 | $0.12 / GB |
| `P_classA` | 쓰기 작업 | $0.05 / 10,000 |
| `P_read` | Firestore 읽기 | $0.06 / 100,000 |
| `P_write` | Firestore 쓰기 | $0.18 / 100,000 |
| `P_inv` | Functions 호출 | $0.40 / 1,000,000 |
| `FX` | 환율 | 1,400 원/USD |
| `K` | 안전계수(대형 파일·재시도·다회 열람) | 2.4 |

### 3.2 QR 전송 1건

```
저장     = (S_photo + S_tl)/1024 GB × (D/30) 월 × P_store
         = 9.5/1024 × 0.1 × 0.026            ≈ $0.0000241
egress   = (S_photo + S_tl) × V /1024 GB × P_egress
         = 9.5 × 1.3/1024 × 0.12             ≈ $0.001447
작업     = 2 PUT × P_classA/10^4 + 3 GET × 0.004/10^4 ≈ $0.0000112
Functions= 4 × P_inv/10^6                     ≈ $0.0000016
Firestore= 6 × P_read/10^5 + 3 × P_write/10^5 ≈ $0.0000090
─────────────────────────────────────────────────────
합계     ≈ $0.001493  → × FX ≈ 2.09원  → × K ≈ 5.0원
```

| 결론 | 값 |
|------|-----|
| 세션당 원가(안전계수 포함) | **약 5원** |
| 목표 마진 4배 시 최소 판매 단가 | **약 20원/건** |
| 지배 항목 | **egress(97%)** — 타임랩스 크기와 열람 횟수가 원가를 결정한다 |

> ✅ **함의**: 타임랩스 전송을 끄면 원가가 약 1/6로 떨어진다(사진만 1.5MB). 향후 "사진만 전송" 플랜을 더 싸게 파는 상품 분화가 가능하다(→ [01 §6.4](./01-currency-catalog-and-pricing.md) 확장 자리).

### 3.3 개인 프레임 1개 (연간)

```
저장   = 2/1024 GB × 12 월 × P_store           ≈ $0.000609
목록read = 60회/월 × 12 × P_read/10^5           ≈ $0.000432
다운로드 = 2MB × 3회/월 × 12 /1024 GB × P_egress ≈ $0.00844
────────────────────────────────────────────────
연간   ≈ $0.00948 → ≈ 13원/년
```

| 결론 | 값 |
|------|-----|
| 프레임 1개 연 원가 | 약 13원 |
| 생성권 5MC(500원) | 약 **38년분** 보관 비용 |
| 실질 가격 근거 | 원가가 아니라 **저작 도구 가치**([01 §5.3](./01-currency-catalog-and-pricing.md)) |

### 3.4 하드캡의 비용 상한

| 항목 | 계산 | 값 |
|------|------|-----|
| 계정 일 300건 | 300 × 5원 | **1,500원/일/계정** |
| (원가 지배분만) egress | 300 × 9.5MB × 1.3 = 3.7GB × $0.12 × FX | 약 620원/일 |
| 활성 100계정이 동시에 한도 소진 | 100 × 1,500원 | 15만원/일(예측 가능한 최악값) |

> ✅ **하드캡의 목적은 "예측 가능한 최악값"을 만드는 것**이다. 무제한이면 최악값이 무한이라 예산 경보가 무의미해진다.

---

## 4. 판정 함수 의사코드 (구현 시 시그니처 고정)

### 4.1 영업일 · 리셋 시각

```
billingDayOf(nowMs, tzOffsetHours = 9):
    return formatUtcDate(nowMs + tzOffsetHours * 3_600_000)   // "yyyy-MM-dd"

resetAtMsOf(billingDay, tzOffsetHours = 9):
    return parseUtcMidnight(billingDay) - tzOffsetHours*3_600_000 + 86_400_000

hourOfDayKst(nowMs, tzOffsetHours = 9):
    return floor(((nowMs + tzOffsetHours*3_600_000) mod 86_400_000) / 3_600_000)
```

> ⚠️ 로케일 API(`toLocaleDateString` 등)를 쓰지 않는다 — 런타임 ICU 의존으로 환경에 따라 값이 달라진다([03 §4.1](./03-qr-daily-quota.md)).

### 4.2 프로레이션

```
firstDayAllowance(N, hourKst):
    raw = ceil(N * (24 - hourKst) / 24)
    return min(max(raw, 1), N)
```

### 4.3 정원 판정 (요약 — 전체는 [03 §2.2](./03-qr-daily-quota.md))

```
evaluateQrQuota(now, tz, account, freeTier, grants, usage, caps, overage):
    if caps.globalKillSwitch:               return deny("kill_switch")
    if usage.usedCount >= caps.accountDailyHardCap: return deny("hard_cap")
    if account.role == admin:               return allow("free")        // 운영 예외

    today = billingDayOf(now, tz)
    used  = (usage.billingDay == today) ? usage : zeroUsage(today)      // lazy reset

    // 1) 무료 티어(temp_user 전용) — 기존 evaluateQrGate 재사용
    if account.role == temp_user:
        g = evaluateQrGate(now, account.createdAtMs, account.legacyQrUsedCount, freeTier)
        if not g.blocked: return allow("free")
        freeReason = g.reason                                            // time | count

    // 2) 일일 정원(활성 grants 합, 첫날은 firstDayAllowance)
    planAllowance = Σ over grants where active(g, now):
                      (g.startBillingDay == today) ? g.firstDayAllowance : g.dailyAllowance
    if used.bySource.plan < planAllowance:  return allow("plan")

    // 3) 회수권
    credits = Σ over grants where active && kind == qr_credit: g.remainingCredits
    if credits > 0 and used.usedCount < creditDailyMax:  return allow("credit")

    // 4) 오버리지
    if overage.enabled:                     return allow("overage", mc = overage.mcPerSession)

    // 거부 — 사유 결정
    if planAllowance > 0 or credits > 0:    return deny("quota_exhausted")
    if account.role == temp_user:           return deny(freeReason)      // free_time | free_count
    return deny("no_entitlement")
```

| 규칙 | 이유 |
|------|------|
| 하드캡·킬스위치를 **가장 먼저** 검사 | 안전장치가 상품 로직에 가려지지 않게 |
| admin 예외를 그 **다음**에 | 하드캡은 admin에게도 적용(B7) |
| `active(g, now)` 는 `!revoked && now < expiresAt` | 요약 캐시를 믿지 않고 재확인([02 §3.3](./02-wallet-ledger-and-entitlements.md)) |
| 사유 결정을 마지막에 모아서 | 문구 분기가 한 곳([08 §5.2](./08-ui-ux-and-copy.md)) |

### 4.4 파일 소유 판정 (프레임)

```
isOwnedLocalFile(fileName, accountId):
    prefix = accountId + "_"
    if not fileName.startsWith(prefix):     return false
    rest = fileName.substring(prefix.length)
    return rest.indexOf('_') < 0            // 프레임 이름에 '_'가 금지되므로 성립
```

### 4.5 단가 단조성 검사 (카탈로그 게이트)

```
assertMonotonicPricing(items):
    plans = items.filter(kind == "qr_plan").sortBy(effect.dailyAllowance asc)
    prev = +∞
    for p in plans:
        unit = p.priceMc / p.effect.dailyAllowance
        if unit > prev: fail("단가 역전: " + p.itemId)
        prev = unit
    // 같은 durationDays 그룹끼리만 비교한다(1일권과 30일권은 다른 축)
```

---

## 5. 참조 문서 지도

### 5.1 이 세트 내부

| 알고 싶은 것 | 문서 |
|--------------|------|
| 무엇을 유료로 하나 | [00 §3](./00-scope-principles-and-model.md) |
| 얼마에 파나 | [01](./01-currency-catalog-and-pricing.md) |
| 잔액을 어떻게 관리하나 | [02](./02-wallet-ledger-and-entitlements.md) |
| 하루 제한·리셋·첫날 계산 | [03](./03-qr-daily-quota.md) |
| 프레임 과금·DB 저장·로그아웃 삭제 | [04](./04-custom-frames-billing-and-lifecycle.md) |
| 결제·환불·스토어 정책 | [05](./05-payments-and-platform-policies.md) |
| 동시 로그인 차단 → **❌ 폐기(2026-08-10)**: 차단하지 않는다 | [06](./06-single-session-enforcement.md)(이력·폐기 근거) |
| API·스키마 | [07](./07-api-and-data-contract.md) |
| 화면·문구 | [08](./08-ui-ux-and-copy.md) |
| 위협·법규·회계 | [09](./09-security-abuse-and-compliance.md) |
| 언제 무엇을 만드나 | [10](./10-rollout-testing-and-wbs.md) |
| 무엇이 아직 안 정해졌나 | [11](./11-open-decisions.md) |

### 5.2 기존 프로젝트 문서

| 알고 싶은 것 | 문서 |
|--------------|------|
| 역할·권한 매트릭스(진실원) | [`analysis/60`](../analysis/60-auth-accounts-and-roles.md) |
| 현행 HTTP 와이어 계약(진실원) | [`analysis/31`](../analysis/31-backend-api-reference.md) |
| Firestore·Storage 스키마(진실원) | [`analysis/40`](../analysis/40-database-firestore-and-storage-schema.md) |
| 무료 한도 엔진의 설계 근거 | [`design/wpf-it13`](../design/wpf-it13-temp-user-role-design.md) |
| 프레임 저작 권한 축 | [`design/wpf-it16`](../design/wpf-it16-advanced-user-role-design.md) |
| 프레임 로컬 정책·사본 분기 | [`design/wpf-it15-frame-ux-design.md`](../design/wpf-it15-frame-ux-design.md) |
| 프레임 "불러오기 = 신규 생성" 재정의 | [`design/wpf-frame-create-from-existing-and-server-register-design.md`](../design/wpf-frame-create-from-existing-and-server-register-design.md) |
| 웹 클라이언트 구조·저장소 | [`web-client/`](../web-client/README.md) |
| 플랫폼별 인증(모바일 확장) | [`analysis/61`](../analysis/61-auth-platform-integration.md) |
| 미해결 항목(전사) | [`analysis/90`](../analysis/90-roadmap-and-future-work.md) |
| TTL·Lifecycle 운영 | [`analysis/50`](../analysis/50-infra-gcp-lifecycle-and-ttl.md) |

---

## 6. 변경 이력

| 날짜 | 내용 |
|------|------|
| 2026-08-06 | 세트 신규 작성(13문서). 사용자 제시안의 **단가 역전 2건**·**플랜 기간 미정으로 인한 30배 단가 편차**·**로컬 프레임 접두 규약 결함 2건**을 지적하고 대안 제시. 미결정 24건 등재 |
