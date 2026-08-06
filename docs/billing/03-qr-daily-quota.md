# 03 · QR 일일 정원 (하루 제한 · 00시 리셋 · 첫날 일할)

| 항목 | 내용 |
|------|------|
| 문서 | QR 전송의 유료 정원 엔진 — 소비 단위, 리셋 경계, 프로레이션, 무료 티어 관계, 서버 강제 지점, 상한 3중화 |
| 범위 | 사용자 요구 "일반유저 이상에서 QR생성 시 하루 제한(과금) · 00시 초기화 · 첫날 일할 · 무제한 금지"의 전체 설계 |
| 최종 업데이트 | 2026-08-06 (신규) |
| 관련 소스 | `web/functions/src/domain/tempUserLimit.ts`(기존 순수 판정), `web/functions/src/services/uploads.ts`(prepare 선검사·commit 트랜잭션), `src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`(클라 effective 단일 지점), `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs:105-120`(한도 초과 우아 처리 선례) |
| 관련 문서 | [01 §4](./01-currency-catalog-and-pricing.md)(플랜 가격·기간 쟁점) · [02 §3](./02-wallet-ledger-and-entitlements.md)(권리 모델) · [07 §3.2](./07-api-and-data-contract.md)(와이어) · [08 §5](./08-ui-ux-and-copy.md)(문구) |

---

## 1. 소비 단위 — "QR 생성 1개"의 정의 (동결)

| 항목 | 정의 | 근거 |
|------|------|------|
| 1개 = | **`POST /uploads/commit` 최초 성공 1건**(= `resultSessions` 문서 1개 생성) | it13이 이미 확정한 단위. `services/uploads.ts`의 commit 트랜잭션 |
| 파일 수 무관 | 사진만 / 사진+타임랩스 모두 **1개** | prepare는 파일당 호출될 수 있으나 세션이 단위다 |
| 재시도 | 같은 `sessionId` 재commit은 **409**로 거부되어 이중집계되지 않는다 | `services/uploads.ts` 트랜잭션 내 중복 검사 |
| QR 이미지 재표시 | 소비 아님(로컬 렌더) | QR PNG 생성은 클라 연산 |
| 다운로드 페이지 열람 횟수 | 소비 아님(egress는 원가에 이미 반영 — [01 §5.1](./01-currency-catalog-and-pricing.md)) | 손님이 여러 번 열어도 정원 차감 없음 |
| 업로드 실패 | 소비 아님(commit 미도달) | 현행과 동일 — 실패 시 로컬 보존 + [재시도] |
| 프레임 이미지 업로드 | QR 정원과 **무관**(별 과금 → [04](./04-custom-frames-billing-and-lifecycle.md)) | |

> ✅ **"QR 생성"이라는 사용자 표현을 "전송 세션"으로 번역했다.** QR 코드 자체는 로컬에서 무한히 만들 수 있고 비용이 0이다. 비용은 **업로드·보관·다운로드**에서 발생하므로 과금 단위는 전송 세션이어야 한다. UI 문구는 사용자 언어("QR 전송")를 쓴다 → [08 §5](./08-ui-ux-and-copy.md).

---

## 2. 모델 — 정원(플랜) + 오버리지 하이브리드

### 2.1 3개 소비 소스와 우선순위

계정이 QR 1건을 쓸 때 서버는 **아래 순서로** 소스를 찾는다. 첫 번째로 가능한 소스에서 소비한다.

| 순위 | 소스 | 성질 | 소진 시 |
|:---:|------|------|---------|
| 1 | **무료 티어**(temp_user 전용: 시간 48h · 누적 30회) | 계정 생성 시각 기준. 유료 무관 | 다음 순위로 |
| 2 | **일일 정원**(활성 `qr_plan` grants 합) | 매일 00시(KST) 리셋 | 다음 순위로 |
| 3 | **회수권**(`qr_credit` 잔여) | 총량형, 유효기간 있음, 일 상한 적용 | 다음 순위로 |
| 4 | **오버리지**(MC 즉시 차감, 옵션) | 계정 설정으로 on/off. 기본 **off** | 거부(403 `QUOTA_EXHAUSTED`) |

> ⚠️ **왜 무료 티어를 1순위로 두는가**: temp_user가 유료 플랜을 사도 무료분이 남아 있으면 무료를 먼저 쓴다. 반대로 두면 사용자가 "무료 30회가 남았는데 돈이 빠져나갔다"고 항의한다. 소비자에게 유리한 순서가 분쟁을 줄인다.

> ⚠️ **오버리지 기본 off**: 켜져 있으면 손님이 촬영할 때마다 운영자 MC가 예고 없이 빠진다. **명시적 opt-in**만 허용하고, 켤 때 "정원을 초과하면 1건당 N MC가 차감됩니다" 확인을 받는다 → [08 §6.3](./08-ui-ux-and-copy.md).

### 2.2 판정 결과 계약 (순수 함수)

```
evaluateQrQuota(
  now: ms,                       // 서버 시각
  tz: "Asia/Seoul",
  account: { role, createdAtMs, legacyQrUsedCount },
  freeTier: { qrHours, qrCount },              // config/tempUserLimits (기존)
  grants: [{ kind, dailyAllowance, firstDayAllowance, remainingCredits, startBillingDay, expiresAt, revoked }],
  usage: { billingDay, usedCount, overageUsedMc },
  caps: { accountDailyHardCap, globalKillSwitch },
  overage: { enabled, mcPerSession }
) -> {
  allowed: boolean,
  source: "free" | "plan" | "credit" | "overage" | "none",
  reason: "ok" | "free_time" | "free_count" | "quota_exhausted" | "hard_cap" | "kill_switch" | "no_entitlement",
  dailyAllowance: int,           // 오늘의 총 정원(플랜 합 + 첫날 프로레이션 반영)
  usedToday: int,
  remainingToday: int,
  creditsRemaining: int,
  overageMcRequired: int,        // source=overage일 때 차감액
  nearestExpiryAt: ms | null,
  billingDay: "yyyy-MM-dd"
}
```

| 규칙 | 내용 |
|------|------|
| **순수 함수** | I/O·현재시각 조회 없음. `now`를 인자로 받는다(it13 `evaluateQrGate`와 동일 규약 — 테스트 가능성) |
| **위치** | 서버 `web/functions/src/domain/qrQuota.ts`(신규) + 클라 표시용 이식(선택). **판정 권위는 서버** |
| **기존 함수** | `evaluateQrGate`(it13)는 **무료 티어 판정 전용으로 좁혀** 이 함수 안에서 호출한다. 삭제하지 않는다(기존 테스트·`qr-usage` 응답 호환) |

---

## 3. 무료 티어와의 관계 (기존 시스템 흡수)

### 3.1 현행 무료 티어 (사실)

| 항목 | 값 | 근거 |
|------|-----|------|
| 적용 대상 | **temp_user만** | `services/uploads.ts` `isTempUser(principal)` 분기 |
| 시간 한도 | 계정 `createdAt` + `qrHours`(전역 기본 48h) | `config/tempUserLimits` |
| 횟수 한도 | `users.qrUsedCount` 누적 ≥ `qrCount`(기본 30) | 동상 |
| 관계 | 독립 OR — 먼저 소진되는 쪽이 차단. **둘 다 초과면 시간 우선** | it13 §8.1 |
| 비-temp_user | 한도·카운트 **없음**(무제한) | `services/uploads.ts` 비트랜잭션 경로 |

### 3.2 변경 — "user 이상 무제한"의 종료

| 대상 | 현행 | 과금 도입 후 |
|------|------|--------------|
| 게스트 | QR 불가 | 불가(불변) |
| temp_user | 48h/30회 무료 → 초과 시 차단 | 무료 소진 후 **유료 정원**으로 계속 가능 |
| **user 이상** | **무제한** | **정원 없으면 0** → 유료 플랜 필요 |

> ⚠️ **이것이 가장 큰 사용자 영향이다.** 기존 `user`/`advanced_user`/`manager`/`admin` 계정은 지금 무제한으로 QR을 쓴다. 과금을 켜는 순간 **정원 0이 되어 QR이 전면 중단**된다. 반드시 다음 완화를 함께 배포한다.

| 완화 | 내용 |
|------|------|
| **이행 지급(grandfathering)** | 시행일 기준 기존 계정에 무상 정원(예: `qr_d30_30d` 1개월분)을 `grant`로 지급 → [10 §3](./10-rollout-testing-and-wbs.md) |
| **사전 고지** | 앱 내 공지 + 이메일(가능한 계정) 2주 전 |
| **단계 시행** | dry-run → 경고 표시 → 실제 차단(3단계) |
| **admin 예외** | `admin` 계정은 정원 판정에서 제외(운영·검증용). 단 하드캡·킬스위치는 적용 → §7 |

> ⚠️ **미결정**: manager도 예외로 둘지 → [11 D-10](./11-open-decisions.md). 초안은 **admin만 예외**(manager는 고객사 운영자일 수 있으므로 과금 대상).

### 3.3 무료 + 유료 동시 보유 시 (temp_user)

| 상태 | 판정 |
|------|------|
| 무료 시간·횟수 모두 남음 | `source=free`. 유료 정원 **미소비** |
| 무료 시간 만료, 횟수 남음 | 무료 불가(시간 우선 규칙) → 유료 정원 소비 |
| 무료 소진, 유료 정원 남음 | `source=plan` |
| 둘 다 없음 | 403. `reason`은 **유료 기준**(`quota_exhausted`) — 유료 플랜을 가진 사용자에게 "무료 사용 시간이 지났습니다"를 보여 주면 오인된다 |

> ✅ **문구 분기가 여기서 결정된다**: `reason`이 `free_time`/`free_count`면 기존 동결 문구(it13)를, `quota_exhausted`면 신규 문구를 쓴다 → [08 §5.2](./08-ui-ux-and-copy.md).

### 3.4 플랜 중복 보유 (누적)

| 규칙 | 내용 | 이유 |
|------|------|------|
| 정원 합산 | 활성 `qr_plan`의 `dailyAllowance` **합**이 오늘 정원 | 성수기 증설 수요가 실재한다(주말에 하루 200개 필요) |
| 만료는 개별 | 각 grant가 자기 `expiresAt`에 개별 만료 → 정원이 계단식으로 줄어든다 | UI가 "N일 후 정원이 X개로 줄어듭니다"를 안내해야 한다 |
| 상한 | 합산 정원도 **계정 하드캡**(§7.2)을 넘지 못한다 | B7 |
| 동일 아이템 재구매 | 허용. 같은 `itemId`의 grant 2개가 공존 | 가격 단조성이 필수인 이유([01 §4.1](./01-currency-catalog-and-pricing.md)) |
| 활성 grant 수 상한 | **20개**. 초과 구매는 409 `TOO_MANY_GRANTS` | 요약 재계산 비용·판정 복잡도 방어 |

---

## 4. 리셋 경계 — 00시는 어디의 00시인가

### 4.1 시간대 (가장 중요한 결정)

| 항목 | 결정 | 이유 |
|------|------|------|
| 리셋 기준 시간대 | **`Asia/Seoul`(KST, UTC+9) 고정** | 국내 서비스. 한국은 **DST가 없어** 오프셋이 항상 +9 → 경계 계산이 단순하고 버그 여지가 없다 |
| 저장 시각 | **UTC**(Firestore Timestamp) | 기존 규약 유지([`analysis/40`](../analysis/40-database-firestore-and-storage-schema.md)) |
| 영업일 문자열 | `yyyy-MM-dd`(KST 환산) — `billingDay` | 문서 id·집계 키 |
| 클라 로컬 시각 | **판정에 쓰지 않는다**(B6) | 시계 조작으로 무한 리셋 가능 |
| 클라 표시 | 서버가 준 `billingDay`·`resetAtMs`를 표시 | "내일 00시에 초기화" 문구의 근거 |
| 해외 확장 | `users.timezone` 필드 추가로 계정별 시간대 지원(현재 미구현) | 지금 넣지 않는 이유: 검증 부담 대비 수요 없음. **함수 시그니처에 `tz`를 이미 받아 두어** 확장 시 호출부만 바꾸면 되게 한다 |

**KST 영업일 계산(순수)**

```
billingDayOf(nowMs, tz="Asia/Seoul") = format(nowMs + 9h, "yyyy-MM-dd")   // KST는 DST 없음
resetAtMsOf(billingDay) = parse(billingDay + "T00:00:00+09:00") + 24h
```

> ⚠️ **`Date.toLocaleDateString("ko-KR")` 류를 쓰지 않는다** — 런타임 ICU 데이터·로케일 설정에 의존해 서버 환경에 따라 값이 달라질 수 있다. **UTC ms에 9시간을 더해 UTC로 포맷**하는 산술만 쓴다(테스트 가능·환경 무관).

### 4.2 lazy reset — 스케줄러 없이 매일 리셋하기

| 방식 | 채택 | 이유 |
|------|:---:|------|
| 스케줄 함수가 매일 00시에 전 계정 usage를 0으로 write | ✕ | 계정 수만큼 write(비용), 실패 시 정합성 붕괴, 계정이 늘면 실행 시간 초과 |
| **일자별 문서 + 없으면 0으로 간주** | **○** | write 0, 판정은 순수 비교, 자연히 시계열이 남는다 |

`usage/{userId}_{billingDay}`

| 필드 | 타입 | 의미 |
|------|------|------|
| `userId`, `billingDay` | string | 복합 키 |
| `usedCount` | int ≥ 0 | 오늘 소비한 QR 전송 수 |
| `bySource` | map | `{free: n, plan: n, credit: n, overage: n}` — 감사·분석용 |
| `overageMcSpent` | int ≥ 0 | 오늘 오버리지로 차감된 MC |
| `firstAt` / `lastAt` | timestamp | 첫·마지막 소비 시각 |
| `expiresAt` | timestamp | `billingDay + 90일` → **Firestore TTL로 자동 삭제**(문서 폭증 방지) |

| 규칙 | 내용 |
|------|------|
| 문서 부재 = `usedCount 0` | 판정 시 `tx.get` 결과가 없으면 0으로 계산하고 소비 시 `set`(merge) |
| 문서 id에 날짜 포함 | 자정을 넘기면 **다른 문서**를 보게 되어 리셋이 자동 성립 |
| TTL 90일 | 분쟁 대응 기간 확보 후 삭제. 장기 집계가 필요하면 별 집계 문서로 롤업(범위 밖) |
| 기존 `users.qrUsedCount` | 무료 티어 **누적** 카운터로 계속 사용(일일 리셋 대상 아님) → 두 카운터의 의미가 다르므로 합치지 않는다 |

> ⚠️ **혼동 주의**: `users.qrUsedCount`는 **누적**(무료 30회 소진 판정), `usage.usedCount`는 **일일**(정원 판정). 이름이 비슷하므로 코드·문서에서 항상 "누적 무료 카운터" / "일일 사용량"으로 구분해 부른다.

### 4.3 자정 경계에서의 진행 중 세션

| 상황 | 처리 |
|------|------|
| 23:59:50에 prepare(선검사 통과) → 00:00:05에 commit | commit 시점의 `billingDay`(새 날)로 판정·기록한다. **소비는 commit 시점 기준** |
| 그래서 발생할 수 있는 일 | 어제 정원이 남아 선검사를 통과했는데, 오늘 정원이 0(플랜 만료)이면 commit이 403 → 로컬 보존 + 재시도 안내(기존 실패 경로로 흡수) |
| 반대 | 어제 정원이 소진돼 prepare가 403이면 사용자는 잠시 뒤(자정 후) 재시도로 성공 |
| 규칙 | **판정 시점 = 판정하는 그 순간의 서버 시각.** 세션에 날짜를 고정(pinning)하지 않는다 — 고정하면 자정 전 대량 prepare로 다음 날 정원을 미리 당겨 쓰는 우회가 생긴다 |

---

## 5. 첫날 프로레이션 (사용자 요구의 정밀화)

### 5.1 요구 원문과 해석

> "오후 5시30분에 구매 시 17시로 계산하여 첫날에는 ((24-17)/24)*N개 의 QR 생성 횟수 부여"

| 요소 | 해석 |
|------|------|
| "17시로 계산" | 구매 시각의 **시(hour)를 내림**(floor)한다. 17:30 → 17, 17:59 → 17 |
| `(24-17)/24` | 그날 남은 시간 비율 = `(24 - H) / 24` |
| "첫날에는 … 부여" | **구매 당일에만** 이 축소 정원을 적용하고, 다음 영업일부터 정원 N 전량 |
| 기준 시간대 | KST(§4.1) |

### 5.2 공식 (확정 초안)

```
firstDayAllowance(N, purchaseHourKst H) = clamp( ceil( N × (24 - H) / 24 ), 1, N )
```

| 요소 | 결정 | 이유 |
|------|:---:|------|
| 시 단위 내림 | `H = floor(KST hour)` | 사용자 요구 원문 그대로 |
| 개수 **올림**(`ceil`) | 소비자 유리 | 내림하면 "23시에 사면 0개"가 되어 **당일 사용 불가 상품**을 판 셈이 된다. 분쟁 소지 |
| 하한 1 | 최소 1개 보장 | 위와 동일. 23:59 구매도 1개는 쓸 수 있다 |
| 상한 N | 정원 초과 금지 | 00시 구매 시 `ceil(N × 24/24) = N` |
| 분 단위 무시 | 17:00과 17:59가 동일 | 요구 원문. 사용자에게 설명 가능("구매 시각의 시 기준") |

### 5.3 전수표 (N = 10 / 30 / 50 / 100 / 200)

| 구매 시각(KST) | H | 비율 (24−H)/24 | N=10 | N=30 | N=50 | N=100 | N=200 |
|---|:--:|---:|---:|---:|---:|---:|---:|
| 00:00~00:59 | 0 | 1.000 | 10 | 30 | 50 | 100 | 200 |
| 03:00~03:59 | 3 | 0.875 | 9 | 27 | 44 | 88 | 175 |
| 06:00~06:59 | 6 | 0.750 | 8 | 23 | 38 | 75 | 150 |
| 09:00~09:59 | 9 | 0.625 | 7 | 19 | 32 | 63 | 125 |
| 12:00~12:59 | 12 | 0.500 | 5 | 15 | 25 | 50 | 100 |
| 15:00~15:59 | 15 | 0.375 | 4 | 12 | 19 | 38 | 75 |
| **17:00~17:59** | **17** | **0.29167** | **3** | **9** | **15** | **30** | **59** |
| 20:00~20:59 | 20 | 0.16667 | 2 | 5 | 9 | 17 | 34 |
| 22:00~22:59 | 22 | 0.08333 | 1 | 3 | 5 | 9 | 17 |
| 23:00~23:59 | 23 | 0.04167 | 1 | 2 | 3 | 5 | 9 |

- 사용자 예시 검증: 17:30 구매, N=100 → `ceil(100 × 7/24) = ceil(29.17) = 30`. ✅ 요구와 일치(내림하면 29)
- 전 구간 최소 1개 보장 ✅

### 5.4 프로레이션의 3가지 함정 (반드시 처리)

| # | 함정 | 처리 |
|---|------|------|
| **P1** | **당일 재구매**: 17시에 Q50을 사고 20시에 Q50을 또 사면? | 각 grant가 **자기 구매 시각 기준**으로 `firstDayAllowance`를 갖는다. 오늘 정원 = 15 + 9 = 24. 내일부터 100. **합산은 grant별 계산의 합**이다 |
| **P2** | **이미 쓴 양과의 관계**: 오늘 무료로 5건 썼는데 17시에 Q50(첫날 15) 구매 | `usedCount`는 **소스별로 분리 집계**(`bySource`)한다. 플랜 소비는 0이므로 플랜 정원 15가 온전히 남는다. ⚠️ 단순 `usedCount ≥ dailyAllowance` 비교는 **틀린다** — 무료로 쓴 5건이 플랜 정원을 깎아먹는다 |
| **P3** | **만료일 계산**: 17시에 산 30일 플랜은 언제 끝나나 | `expiresAt = (startBillingDay + durationDays)의 KST 24:00`. 즉 **구매일을 1일로 세고 30일째의 자정**까지. 첫날이 축소 정원이므로 실질 사용량은 29일+α → 표시 문구에 "만료: 2026-09-05 24:00" 명시 |

> ✅ **P2가 핵심 구조 결정이다**: `usage.bySource`를 두는 이유가 이것이다. 소스별 사용량을 분리하지 않으면 프로레이션·무료 티어·오버리지가 서로를 잠식한다.

### 5.5 정원 판정식 (P2 반영)

```
오늘 플랜 정원  = Σ (grant가 오늘 활성) ? (grant.startBillingDay == today ? grant.firstDayAllowance : grant.dailyAllowance) : 0
오늘 플랜 잔여  = max(0, 오늘 플랜 정원 − usage.bySource.plan)
오늘 총 잔여    = 무료 잔여 + 오늘 플랜 잔여 + 회수권 잔여(일 상한 적용) + (오버리지 on ? ∞ 아님 → 하드캡까지 : 0)
표시용 remaining = min(오늘 총 잔여, 계정 하드캡 − usage.usedCount)
```

---

## 6. 서버 강제 지점 (과금 안전의 본체)

### 6.1 `POST /uploads/prepare` — 선검사 (차감 없음)

| 항목 | 현행(it13) | 변경 |
|------|-----------|------|
| 게이트 | `requireApiKey` + `optionalBearer` | 불변 |
| 대상 | **TempUser만** 선검사 | **로그인 전원** 선검사(admin 예외) |
| 판정 | `evaluateQrGate` | `evaluateQrQuota`(§2.2) |
| 초과 시 | 403 `TEMP_USER_*` — **서명 URL 미발급** | 403 `QUOTA_EXHAUSTED` 또는 기존 `TEMP_USER_*`(무료 사유일 때) — 미발급 불변 |
| 계정 문서 부재 | 거부하지 않고 통과(commit이 최종 권위) | 불변 |
| 게스트(무토큰) | 통과 | 불변 — **게스트는 클라가 QR에 진입하지 않는다**(`QrEffectivePolicy`) |
| 차감 | 없음 | 없음(불변) |

> ✅ **왜 prepare에서 차감하지 않는가**: prepare 후 PUT이 실패하거나 사용자가 취소할 수 있다. 차감하면 "쓰지 못한 1건"이 사라져 민원이 된다. 차감은 commit(성공 확정) 시점 1회다.

> ⚠️ **prepare 통과 후 15분 창**: 서명 URL TTL 15분 안에 여러 파일을 올릴 수 있다. 그래도 **commit이 1건**이므로 정원 소비는 1이다. 다만 정원이 0인 계정이 prepare를 반복 호출해 Storage에 파일만 올리는 우회는 가능하다(commit 없이) → 대응은 [09 §3.5](./09-security-abuse-and-compliance.md)(고아 파일 정리 + prepare rate limit).

### 6.2 `POST /uploads/commit` — 트랜잭션 (차감 지점)

**현행**: TempUser만 트랜잭션 경로(중복 검사 → 한도 재판정 → 문서 생성 → `qrUsedCount+1`).
**변경**: **로그인 계정 전원**이 트랜잭션 경로. 게스트만 기존 비트랜잭션 경로.

```
runTransaction(tx):
  1. tx.get(resultSessions/{sid})   → 존재하면 409 (이중집계 차단, 불변)
  2. tx.get(users/{uid})            → 없으면 401 (불변)
  3. tx.get(usage/{uid}_{today})    → 없으면 usedCount=0
  4. tx.get(entitlements/{uid})     → 요약(없으면 정원 0)
     (필요 시) tx.get(활성 grant 문서들)  ← 요약으로 부족할 때만, 상한 20
  5. evaluateQrQuota(...)           → allowed=false면 403(사유별 코드)
  6. source == overage 이면 tx.get(wallets/{uid}) → 잔액 부족이면 409 INSUFFICIENT_MC
  7. tx.set(resultSessions/{sid})   → 문서 생성
  8. tx.set(usage/{uid}_{today}, merge: usedCount+1, bySource.{source}+1, lastAt)
  9. source == free 이면 tx.update(users, qrUsedCount+1)      ← 기존 동작 보존
     source == credit 이면 tx.update(grant.remainingCredits-1)
     source == overage 이면 tx.update(wallets, balance-mc) + tx.set(원장 spend)
```

| 규칙 | 이유 |
|------|------|
| 정원 소비(1~5,7,8)는 **read 5개 이하** | 트랜잭션 충돌·지연 관리([02 §5.1 T4](./02-wallet-ledger-and-entitlements.md)) |
| 지갑·원장은 **오버리지일 때만** 만진다 | 핫패스 최적화([02 §5.4](./02-wallet-ledger-and-entitlements.md)) |
| 회수권 차감은 **grant 문서 직접 갱신** | 요약은 사후 재계산(lazy) |
| 기존 `qrUsedCount+1`은 **무료 소비일 때만** | 유료 소비가 무료 카운터를 소진시키면 안 된다 |
| 실패 시 전체 롤백 | 문서도 안 생기고 카운터도 안 오른다 → 사용자는 재시도 가능 |

### 6.3 `GET /accounts/me/qr-usage` — 상태 조회 (확장)

| 항목 | 규격 |
|------|------|
| 게이트 | Bearer(본인 고정) — 불변 |
| 하위 호환 | 기존 필드(`role`·`blocked`·`reason`·`remainingMs`·`remainingCount`·`limits`) **전부 유지**. 새 필드만 추가 |
| 추가 필드 | `dailyAllowance`·`usedToday`·`remainingToday`·`billingDay`·`resetAtMs`·`creditsRemaining`·`plans[]`(정원·만료)·`overage{enabled,mcPerSession}`·`source`(다음 소비가 어디서 나갈지) |
| 실패 정책 | **fail-open 표시**(기존과 동일). 차단은 업로드가 담보 |
| 호출 시점 | 로그인 직후 · 설정/상점 진입 · QR 전송 후 · 수동 새로고침. **폴링 금지** |

> ⚠️ **기존 필드 의미를 바꾸지 않는다**: `remainingCount`는 지금 "무료 잔여 횟수"이고 non-TempUser에는 0이다(그러나 무제한을 뜻한다 — [`analysis/31 §4.4`](../analysis/31-backend-api-reference.md)). 이 혼란스러운 의미를 **그대로 두고** 새 필드(`remainingToday`)를 정본으로 삼는다. 구버전 클라가 붙어도 오작동하지 않게 하는 것이 우선이다.

### 6.4 클라이언트 effective 정책 확장

현행 `QrEffectivePolicy.IsQrEnabled(rawEnableQr, isLoggedIn, isTempUserBlocked)`(`src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`)를 확장한다.

```csharp
// 확장 초안 — 인자 1개 추가(호출부는 셸이 합성해 전달)
public static bool IsQrEnabled(bool rawEnableQr, bool isLoggedIn, bool isQuotaBlocked)
```

| 규칙 | 내용 |
|------|------|
| 이름 변경 | `isTempUserBlocked` → `isQuotaBlocked`(무료·유료 통합 판정) |
| ini 불변 | **`AppSettings.EnableQrDelivery`를 절대 쓰지 않는다**(it13이 확립한 최상위 불변식). 런타임 오버라이드만 |
| 단일 지점 | `ResultViewModel.Next`가 유일한 조합 지점(불변) |
| 웹 대칭 | `qrUsageStore`의 `isTempUserBlocked` → `isQuotaBlocked`로 동일하게 이식 |
| 판정 실패 | 조회 실패 시 `isQuotaBlocked = false`(fail-open) — 서버가 최종 거부 |

---

## 7. 무제한 금지 — 3중 상한 (사용자 요구 "DB 과금 우려")

### 7.1 상한 3층

| 층 | 이름 | 값(초안) | 어디에 | 넘으면 |
|:--:|------|---------|--------|--------|
| 1 | 상품 상한 | 플랜별 `dailyAllowance`(최대 200) | `catalog/items.dailyMax` | 플랜을 더 살 수밖에 없음 |
| 2 | **계정 하드캡** | **일 300건** | `config/billing.accountDailyHardCap` | 403 `HARD_CAP`(플랜을 아무리 많이 사도 못 넘음) |
| 3 | **전역 킬스위치·전역 일 상한** | 전역 일 20,000건 / `enabled:false` | `config/billing` | 503 또는 403 `KILL_SWITCH` |

| 규칙 | 내용 |
|------|------|
| 카탈로그 검증 | `dailyMax` 없는 아이템, 또는 `dailyMax > accountDailyHardCap`인 아이템은 **판매 불가**(서버가 거부) |
| 하드캡 도달 시 문구 | "오늘 사용 가능한 최대 전송 횟수에 도달했습니다. 내일 00시에 초기화됩니다." → [08 §5.2](./08-ui-ux-and-copy.md) |
| 전역 상한 초과 | 관리자 알림 + 신규 전송 거부(진행 중 세션은 완료 허용). **폭주·공격 방어 최후 수단** |
| 전역 카운터 | `usage/_global_{billingDay}` 문서 1개에 increment. ⚠️ 초당 1 write 한계 → **샤딩 10개**(`_global_{day}_{0..9}`, 랜덤 분산 후 합산) |
| 킬스위치 켜면 | 유료·무료 **모두 차단**? 아니다 → **B12**: 무료 티어 한도로 복귀(과금 기능만 off) |

### 7.2 왜 계정 하드캡이 필요한가

플랜 누적(§3.4)을 허용하면 이론상 `qr_d200_30d` × 20개 = 하루 4,000건이 가능하다. 이는 ① 비용 폭주 ② 카드 도난·부정 결제 시 피해 확대 ③ Storage·egress 스파이크로 이어진다. **하드캡은 상품 설계와 무관한 안전장치**다.

| 지표 | 하드캡 300건/일의 의미 |
|------|------------------------|
| Storage 증가 | 300 × 9.5MB ≈ 2.9GB/일/계정 |
| egress | 300 × 12.4MB ≈ 3.7GB/일/계정 ≈ $0.45 |
| 비용 상한 | 계정당 약 620원/일 → 100계정이 동시에 한도를 채워도 일 6.2만원 수준으로 예측 가능 |

---

## 8. 정원 소진 UX 요약 (상세는 08)

| 상황 | 촬영 흐름(B2B) | 결과 화면 | 설정 화면 |
|------|----------------|-----------|-----------|
| 정원 남음 | 변화 없음 | QR 정상 | 잔여 표시 |
| 정원 소진(플랜 있음, 오늘 다 씀) | QR 화면 **미진입** → Done 직행(로컬 저장은 정상) | "오늘 QR 전송을 모두 사용했습니다" | 잔여 0 + 리셋 시각 |
| 플랜 없음/만료 | 동상 | "QR 전송 권한이 없습니다. 관리자에게 문의해주세요." | 구매 안내(PIN 게이트 뒤) |
| 무료 티어 소진(temp_user) | 동상 | **기존 동결 문구 유지**(it13) | 기존과 동일 |
| 오버리지 on + 잔액 부족 | 동상 | "MC 잔액이 부족합니다" | 충전 안내 |

> ✅ **핵심 UX 원칙**: 정원이 없다고 촬영을 막지 않는다. **촬영·로컬 저장은 항상 성공**하고 QR만 조용히 빠진다(현행 TempUser 초과 동작과 동일 — `ResultViewModel.Next`가 `Done`으로 직행). 손님을 세워 두고 결제하게 만들지 않는다.

---

## 9. 테스트 목록 (순수 함수 우선)

| # | 케이스 | 기대 |
|---|--------|------|
| Q1 | 프로레이션 전수(§5.3 표 50행) | 표와 정확히 일치 |
| Q2 | 23:59 구매 → 최소 1개 | `firstDayAllowance == 1` |
| Q3 | 00:00 정각 구매 → N | 상한 클램프 |
| Q4 | KST 자정 경계(23:59:59.999 / 00:00:00.000) | `billingDay`가 정확히 바뀐다 |
| Q5 | UTC 15:00(=KST 00:00) | 새 영업일 |
| Q6 | 당일 재구매 2건 합산(P1) | 첫날 정원 = 두 firstDay 합 |
| Q7 | 무료 5건 사용 후 플랜 구매(P2) | 플랜 정원 온전 |
| Q8 | 무료 시간·횟수 동시 초과 | `reason=free_time`(시간 우선, 기존 규칙 보존) |
| Q9 | 플랜 만료 당일 자정 직전/직후 | 직전 허용, 직후 거부 |
| Q10 | 정원 초과 + 오버리지 on + 잔액 충분 | `source=overage`, MC 차감 1회 |
| Q11 | 정원 초과 + 오버리지 on + 잔액 부족 | 409 `INSUFFICIENT_MC`, 차감 0 |
| Q12 | 하드캡 도달 | `reason=hard_cap`, 플랜 잔여 있어도 거부 |
| Q13 | 킬스위치 on | 유료 차단, 무료 티어는 동작(B12) |
| Q14 | 동시 commit 2건, 잔여 1 | 한 건만 201, 다른 건 403 |
| Q15 | 같은 sessionId 재commit | 409, 카운터 미증가 |
| Q16 | admin 계정 | 정원 무관 통과, 하드캡은 적용 |
| Q17 | 회수권 + 일 상한 | 상한 초과분은 다음 날로 |
| Q18 | grant 20개 초과 구매 | 409 `TOO_MANY_GRANTS` |
| Q19 | 요약 문서가 낡아 만료 플랜을 활성으로 표시 | 판정에서 `expiresAt` 재확인 → 거부 |
| Q20 | 클라가 위조한 `remainingToday`를 전송 | 서버가 무시(B5) |
