# MC포토 과금 제도 설계 (docs/billing)

MC포토의 **유료화(monetization) 전체 설계** 문서 세트입니다. 화폐(MC) · 지갑/원장 · QR 일일 정원 · 커스텀 프레임 과금 · 결제(PG·IAP) · 단일 세션 강제 · API/데이터 계약 · UI 문구 · 법규/회계 · 롤아웃 · 검증까지를 다룹니다.

| 항목 | 값 |
|------|-----|
| 문서 | 과금 제도 설계 세트(이 인덱스 + 본문 13문서) |
| 작성 | 2026-08-06 (신규) |
| 성격 | **설계 문서 — 코드 구현 금지.** 이 세트는 "무엇을 왜 그렇게 만들 것인가"만 정한다. 구현은 §[10](./10-rollout-testing-and-wbs.md)의 WBS 단계로 나눠 별 이터레이션에서 수행한다 |
| 대상 플랫폼 | **전 플랫폼 공통 규격**(Windows WPF · 웹 클라이언트 · iOS · Android). 플랫폼 고유 사항은 각 문서에서 "플랫폼 차이" 절로 분리 |
| 진실원 우선순위 | 실제 소스 > [`docs/analysis`](../analysis/README.md) > 이 세트 > [`docs/design`](../design/README.md)의 개별 이터레이션 문서 |
| 미결정 | **[11 · 미결정 사항](./11-open-decisions.md)이 단일 집합소**다. 이 세트의 가격·기간·정책 수치는 **전부 초안**이며 11번에서 확정 상태를 관리한다 |

> ⚠️ **이 문서 세트는 기존 동작을 바꾸는 제안을 포함한다.** 특히 ① 개인 커스텀 프레임의 **로컬 전용 → 서버 저장** 전환([04](./04-custom-frames-billing-and-lifecycle.md)), ② **로그아웃 시 로컬 프레임 삭제**(같은 문서), ③ **단일 세션 강제**([06](./06-single-session-enforcement.md))는 현행 계약([`analysis/40 §2.2`](../analysis/40-database-firestore-and-storage-schema.md)·[`analysis/60 §3.5`](../analysis/60-auth-accounts-and-roles.md))과 **정면으로 다르다**. 각 문서가 "현행 → 변경" 대조표로 그 경계를 명시한다.

> ✅ **이미 있는 것을 다시 만들지 않는다.** it13이 만든 **TempUser 무료 한도 엔진**(서버 권위 · prepare 선검사 + commit 트랜잭션 · effective 정책 단일 지점)이 유료 과금의 **구조적 원형**이다([`design/wpf-it13-temp-user-role-design.md`](../design/wpf-it13-temp-user-role-design.md)). 이 세트는 그 골격을 **확장**하며, 새 개념(지갑·원장·정원·세션)만 신설한다.

---

## 0. 읽는 순서

| 순서 | 문서 | 무엇을 결정하나 |
|:---:|------|-----------------|
| 1 | [00 · 범위 · 원칙 · 과금 모델](./00-scope-principles-and-model.md) | 무엇을 팔고 무엇을 공짜로 두는가, 누가 결제 주체인가, 절대 깨지 않을 불변식 12개 |
| 2 | [01 · 화폐(MC) · 카탈로그 · 가격](./01-currency-catalog-and-pricing.md) | MC 정의, 팩 가격표(제시안의 **단가 역전 지적 포함**), 소비 아이템 단가, 원가·마진 검산 |
| 3 | [02 · 지갑 · 원장 · 권리(entitlement)](./02-wallet-ledger-and-entitlements.md) | 잔액을 어디에 어떻게 두고 어떻게 틀리지 않게 바꾸는가(멱등·트랜잭션·감사) |
| 4 | [03 · QR 일일 정원](./03-qr-daily-quota.md) | 하루 N개 · 00시(KST) 리셋 · **첫날 일할 계산** · 무제한 금지 · 서버 강제 지점 |
| 5 | [04 · 커스텀 프레임 과금 · 수명](./04-custom-frames-billing-and-lifecycle.md) | 프레임 생성 과금, 서버 DB 저장 전환, 로그아웃 시 로컬 삭제, 기본 프레임 보존 |
| 6 | [05 · 결제 · 플랫폼 정책](./05-payments-and-platform-policies.md) | PG(웹·Windows) vs IAP(iOS·Android) 강제, 영수증 검증, 환불·취소, 크로스플랫폼 지갑 |
| 7 | [06 · 단일 세션 강제](./06-single-session-enforcement.md) | 같은 계정 동시 로그인 차단, 강제 로그아웃 팝업, 하트비트, **키오스크 다중 PC 함정** |
| 8 | [07 · API · 데이터 계약](./07-api-and-data-contract.md) | 신규·변경 엔드포인트 전수, Firestore 스키마·인덱스·규칙, 에러 코드 |
| 9 | [08 · UI/UX · 문구](./08-ui-ux-and-copy.md) | 화면별 명세와 **동결 문구 전수**(플랫폼 공통), 결제 흐름 UI, 잔량 표시 |
| 10 | [09 · 보안 · 어뷰징 · 법규 · 회계](./09-security-abuse-and-compliance.md) | 위협 모델 14종, 다중계정 남용, 전자상거래법·전금법·세무·약관 |
| 11 | [10 · 롤아웃 · 검증 · WBS](./10-rollout-testing-and-wbs.md) | 6단계 롤아웃(dry-run 포함), 테스트 전략, 수락 기준, 작업 분해 |
| 12 | [11 · 미결정 사항](./11-open-decisions.md) | **결정해야 착수 가능한 항목 24건**과 각각이 막고 있는 작업 |
| — | [12 · 용어집 · 계산 부록](./12-glossary-and-appendix.md) | 용어 정의, 프로레이션 전수표, 원가 계산 근거, 참조 링크 |

---

## 1. 한 장 요약 (설계의 뼈대)

```
[결제 채널]                         [서버 권위 영역]                    [클라이언트]
PG(웹·Windows) ─┐                ┌─ payments/{id} 상태머신
IAP(iOS·Android)─┼─ 검증 ────────>├─ wallets/{uid}.balanceMc  (잔액)
프로모·관리자지급 ┘                ├─ wallets/{uid}/entries/*  (원장·append-only)
                                  │
                                  ├─ entitlements/{uid}       (QR 일일 정원 플랜)
                                  ├─ usage/{uid}_{KST일자}    (오늘 사용량, lazy reset)
                                  └─ sessions 단일 세션(sid)  ─── 401 session_superseded
                                        │
        소비 지점(서버가 거부해야 실효)  │
        ① POST /uploads/prepare   ──────┤ 정원 선검사 → 403
        ② POST /uploads/commit    ──────┤ 트랜잭션(중복검사→정원재판정→문서생성→usage+1)
        ③ POST /frames (개인)     ──────┘ 프레임 생성권 차감 → 문서+Storage
                                                   │
                                        클라는 표시·1차 차단만(신뢰 금지)
```

**핵심 명제 4개**

| # | 명제 | 왜 |
|---|------|-----|
| 1 | **과금 안전은 서버가 요청을 거부함으로써만 성립한다.** 클라 UI 차단은 UX·1차 방어일 뿐이다 | it13이 이미 확립한 원칙([`wpf-it13` §0](../design/wpf-it13-temp-user-role-design.md)). 클라는 위조 가능 |
| 2 | **잔액은 파생값이 아니라 원장의 합과 일치해야 한다.** 모든 변동은 append-only 원장 + 멱등키 | 돈이 걸린 값은 "덮어쓰기"로 관리하면 분쟁 시 증명 불가 → [02](./02-wallet-ledger-and-entitlements.md) |
| 3 | **무제한은 어떤 상품에도 없다.** 최상위 플랜도 일 상한이 있고, 그 위에 전역 킬스위치가 있다 | 사용자 요구 + Storage egress·Firestore write가 실비용 → [03 §7](./03-qr-daily-quota.md) |
| 4 | **결제 주체는 촬영 손님이 아니라 계정 소유자(부스 운영자)다** | 키오스크 촬영 흐름에 결제 UI를 넣으면 안 되는 이유. B2C 개인 사용자는 별 프로파일로 분기 → [00 §4](./00-scope-principles-and-model.md) |

---

## 2. 사용자 요구사항 ↔ 문서 매핑 (요청 원문 대조)

| 요구 원문 | 어디서 설계되나 | 상태 |
|-----------|-----------------|------|
| 돈이 아니라 **아이템으로 구매**(아이템은 돈으로 구매) | [01 §1](./01-currency-catalog-and-pricing.md) 2계층 구조(현금→MC→아이템) | 설계됨 |
| 10MC=1,000원 … 100MC=8,500원 **대량 할인** | [01 §3](./01-currency-catalog-and-pricing.md) 가격표 + 검산 | ⚠️ **원안 유지 시 단가 비단조**(20·30MC 구간 할인 0%) — 대안표 제시, 확정은 [11 D-03](./11-open-decisions.md) |
| 고급 유저 **커스텀 프레임 생성 시 과금** | [04 §3](./04-custom-frames-billing-and-lifecycle.md) | 설계됨(단가 미확정 [11 D-06](./11-open-decisions.md)) |
| 커스텀 프레임 **DB 저장 필요** | [04 §2](./04-custom-frames-billing-and-lifecycle.md) — 서버 저장 전환(현행 로컬 전용 폐기) | 설계됨 |
| **로그아웃 시 로컬 커스텀 프레임 삭제**(타 계정 미제공) | [04 §5](./04-custom-frames-billing-and-lifecycle.md) — 접두 규약 기반 purge + **계정 id에 `_`가 있을 때의 오삭제 함정** 해소 | 설계됨 |
| **디폴트 프레임(매니저 이상 생성)은 유지** | [04 §5.2](./04-custom-frames-billing-and-lifecycle.md) — 공용은 접두 없음 → purge 대상 아님(현행 규약이 이미 분리) | 설계됨 |
| 일반 유저 이상 **QR 하루 제한(과금)** · 00시 초기화 | [03 §2](./03-qr-daily-quota.md)(정원 모델) · [03 §4](./03-qr-daily-quota.md)(KST 자정 lazy reset) | 설계됨 |
| 10/30/50/100/200개 ↔ 20/35/85/160/320MC | [01 §4](./01-currency-catalog-and-pricing.md) | ⚠️ **단가 역전 2건 발견**(30개=1.17MC/개 vs 50개=1.70MC/개) — 재설계표 제시, 확정 [11 D-04](./11-open-decisions.md) |
| 17:30 구매 → **17시로 계산, 첫날 ((24-17)/24)×N** | [03 §5](./03-qr-daily-quota.md) + [12 §2](./12-glossary-and-appendix.md) 전수표 | 설계됨(내림·올림 규칙까지 확정 초안) |
| **무제한 QR 금지**(DB 과금 우려) | [03 §7](./03-qr-daily-quota.md) 3중 상한(플랜 상한·계정 하드캡·전역 킬스위치) | 설계됨 |
| **같은 계정 다른 세션 로그인 차단** + 강제 로그아웃 **팝업 필수** | [06](./06-single-session-enforcement.md) 전체 | 설계됨(정책 3안 중 권장안 제시, 확정 [11 D-09](./11-open-decisions.md)) |
| "또는 여러 대의 Windows PC에서 같은 계정 로그인 차단" | [06 §3](./06-single-session-enforcement.md) 정책 S1/S2/S3(좌석제) 비교 | ⚠️ **현행 키오스크 운영과 충돌 가능**(한 계정으로 여러 부스) — [06 §3.4](./06-single-session-enforcement.md) |
| 다른 플랫폼(웹·iOS·Android) 개발 시 제대로 구현 가능하게 | 전 문서가 **플랫폼 중립 규격 + 플랫폼 차이 절** 구조. 특히 [05 §2](./05-payments-and-platform-policies.md)(IAP 강제)·[07](./07-api-and-data-contract.md)(와이어 계약) | 설계됨 |

---

## 3. 이 세트가 건드리는 기존 계약 (충돌 지도)

| 기존 계약 | 현행 | 이 세트의 변경 | 문서 |
|-----------|------|----------------|------|
| `POST /frames` | Bearer + **power** · `userId=null`·`isDefault=true` **강제**(`web/functions/src/routes/frames.ts:58-80`) | 개인 프레임 생성 경로 신설(`CanWriteFrames` + 과금 + `userId=principal.id`) | [04 §4](./04-custom-frames-billing-and-lifecycle.md)·[07 §4](./07-api-and-data-contract.md) |
| 개인 커스텀 프레임 저장소 | **로컬 파일 전용**(`LocalFrameStore`, DB 미저장 — [`analysis/40 §2.2`](../analysis/40-database-firestore-and-storage-schema.md) 하이브리드 it8 A2) | **서버 DB 정본 + 로컬 캐시** | [04 §2](./04-custom-frames-billing-and-lifecycle.md) |
| 로그아웃 시 로컬 데이터 | 삭제하지 않음(파일 잔존) | `{계정}_` 접두 파일 purge | [04 §5](./04-custom-frames-billing-and-lifecycle.md) |
| JWT | `{sub, role, iat, exp}` · 8시간 · 세션 레지스트리 없음([`analysis/31 §2.2`](../analysis/31-backend-api-reference.md)) | **`sid` 클레임 추가** + 서버 활성 세션 대조 | [06 §4](./06-single-session-enforcement.md) |
| QR 게이트 | TempUser 전용(계정 `createdAt` + `qrUsedCount`) | **역할 무관 정원 엔진**으로 일반화(무료 티어는 그 위의 한 플랜) | [03 §3](./03-qr-daily-quota.md) |
| `GET /accounts/me/qr-usage` | TempUser 판정 결과(`blocked/reason/remainingMs/remainingCount`) | **응답 확장**(하위 호환 유지 — 필드 추가만) | [07 §3.2](./07-api-and-data-contract.md) |
| 에러 코드 | `TEMP_USER_TIME_EXCEEDED`·`TEMP_USER_COUNT_EXCEEDED` | `QUOTA_EXHAUSTED`·`INSUFFICIENT_MC`·`SESSION_SUPERSEDED` 등 추가(기존 코드 **유지**) | [07 §2](./07-api-and-data-contract.md) |
| `users` 문서 | `qrUsedCount` 누적 카운터 | 누적 → **일자별 usage 문서**로 이관(기존 필드는 읽기 폴백으로 남김) | [07 §6](./07-api-and-data-contract.md) |

---

## 4. 이 세트가 **하지 않는** 것 (명시적 비범위)

| 비범위 | 이유 · 대안 |
|--------|-------------|
| 실제 PG사·법인 계약 진행 | 사업 액션(계약·심사 수 주 소요). 문서는 요구사항과 선택지만 정리 → [05 §7](./05-payments-and-platform-policies.md), [11 D-13](./11-open-decisions.md) |
| 법률 자문의 대체 | **선불전자지급수단 해당 여부는 변호사 확인 필수**. 문서는 쟁점과 안전한 기본값만 제시 → [09 §6](./09-security-abuse-and-compliance.md) |
| 인쇄·스티커 등 미개발 기능의 과금 | 기능 자체가 없다([`analysis/90 §2.1`](../analysis/90-roadmap-and-future-work.md)). 아이템 카탈로그는 **확장 가능한 형태**로만 설계 → [01 §6](./01-currency-catalog-and-pricing.md) |
| 광고 수익화·구독형 SaaS 요금제 | 사용자 요구 밖. 다만 MC 모델과 병존 가능한 여지만 기록 → [01 §7](./01-currency-catalog-and-pricing.md) |
| 사용량 통계 대시보드 | 서버에 통계 API가 없다([`analysis/31 §10`](../analysis/31-backend-api-reference.md)). 원장이 있으면 나중에 집계 가능하도록 스키마만 준비 → [02 §7](./02-wallet-ledger-and-entitlements.md) |
| 코드 구현·커밋 | 이 세트는 설계 전용. 구현 단계는 [10 §5](./10-rollout-testing-and-wbs.md) WBS |

---

## 5. 문서 등재 (후속 작업)

이 세트는 신규 폴더이므로 아직 기존 인덱스에 등재돼 있지 않다. **다른 세션의 파이프라인 작업과 충돌을 피하기 위해 인덱스 파일은 수정하지 않았다.** 등재할 때 아래 행을 그대로 쓰면 된다.

`docs/design/README.md` §0 표에 추가(**아래 블록의 상대 경로는 `docs/design/README.md` 기준**이다):

```markdown
| **과금·결제 제도를 만든다 / 바꾼다** | **[`docs/billing/`](../billing/README.md)** — 전용 문서 세트 13개(화폐·지갑/원장·QR 일일 정원·프레임 과금·PG/IAP·단일 세션·API 계약·UI 문구·법규·롤아웃). ⚠️ 개인 프레임 **로컬 전용 정책**([it15 프레임 UX](./wpf-it15-frame-ux-design.md))과 **로그아웃 세션 유지 규칙**([`analysis/60 §3.5`](../analysis/60-auth-accounts-and-roles.md))을 바꾸는 제안을 포함한다 |
```

`docs/analysis/90-roadmap-and-future-work.md` §2 큐에 추가:

```markdown
### #17 과금 제도(유료화) — 설계 완료 · 구현 대기
- 설계: `docs/billing/` 13문서(2026-08-06). 착수 전 **[11 · 미결정](../billing/11-open-decisions.md) 24건 중 차단 8건** 확정 필요.
- 1단계는 과금 없는 **지갑·원장 골격 + dry-run 계측**(가격을 실사용 데이터로 확정하기 위함, `docs/billing/10 §2`).
```
