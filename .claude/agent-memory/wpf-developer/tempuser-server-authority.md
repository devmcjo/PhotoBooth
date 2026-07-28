---
name: tempuser-server-authority
description: MCPhoto TempUser QR 한도 — 진실원은 서버(계정별), 클라는 비영속·비권위. 사용량 캐싱/영속·클라 카운트 증가 금지
metadata:
  type: project
---

TempUser(임시 유저) QR 전송 한도의 **강제·집계·판정 진실원은 100% 서버(계정별)**다. 클라는 절대 권위/집계 소스가 아니다.

**Why:** 사용자 명시 제약 — 다른 PC에서 로그인해 클라 로컬 카운트를 우회하는 것을 막아야 한다(과금 안전). 클라 로컬에 사용량을 영속하거나 신뢰하면 부스마다 카운트가 갈라져 우회가 생긴다. 실제 차단은 서버가 업로드 prepare/commit을 403(`TEMP_USER_TIME_EXCEEDED`/`TEMP_USER_COUNT_EXCEEDED`)으로 거부함으로써만 성립. 클라 게이트(effective QR)는 best-effort UX·1차 방어일 뿐.

**How to apply:** TempUser 사용량 관련 클라 작업 시:
- 사용량 카운트·경과시간을 **ini·디스크·`IBackendSession` 어디에도 영속하지 말 것.** 상태는 셸의 in-memory 필드(`AppShellViewModel._tempUserQrStatus`) 하나에만 두고, 로그인 변경마다 null 초기화 후 `IQrUsageService.GetStatusAsync()`로 **매번 서버 재조회**한다(다른 PC 로그인 시 그 셸이 계정별 서버 상태를 새로 봄).
- 클라에서 카운트를 **증가시키지 말 것.** `qrUsedCount` 증가는 서버 commit 트랜잭션에만 존재. `QrUsageStatus.RemainingTime/RemainingCount`는 서버 응답을 그대로 담기만 하고 클라 재계산 금지(시계 오차 회피).
- 서버 미도달 시 **fail-open**(게이트만 열림, 허용) — 서버가 업로드에서 최종 거부하므로 우회 불가. fail-closed로 바꿔 정상 사용자를 막지 말 것(O3 결정).
- effective QR 게이트([[wpf-headless-window-test-pitfall]]와 무관, `QrEffectivePolicy.IsQrEnabled` 단일 지점)와 설정 표시 off는 UX일 뿐 — 판정의 진실원 아님. ini(`AppSettings.EnableQrDelivery`)는 한도 초과여도 절대 write하지 않는다(한도 해제 시 원복 보장).
- "설정 진입 지연을 줄이려 사용량을 로컬 캐싱하자"는 최적화 유혹을 경계할 것 — 세션 중 1회 조회 캐시는 허용(설계 A5)이나 **디스크 영속은 금지**.
