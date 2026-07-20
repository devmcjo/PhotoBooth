# MC포토 — TTL 만료 정리 운영 절차

| 항목 | 값 |
|------|-----|
| 대상 | Firebase Storage `results/` 결과물 + `resultSessions` Firestore 문서의 만료 정리 |
| 결정 | 스케줄 Cloud Functions **미채택**(D-2). WPF 직접 삭제(1차) + GCS Lifecycle(안전망) + Firestore 네이티브 TTL(선택 권장) |
| 근거 | `web-architecture.md` §7, `firebase-contract.md` §6 |
| 웹 코드 영향 | **없음** — 웹은 삭제를 수행하지 않는다. 만료/부재를 판정해 안내만 한다(§3.3/§3.4). |

> 이 문서는 운영자(인프라)용 절차서다. 웹 페이지(`public/*`)는 이 절차와 무관하게 동작한다.

---

## 0. 삭제 분담 요약 (누가 지우는가)

| 방식 | 주체 | 채택 | 대상 |
|------|------|------|------|
| WPF 앱 직접 삭제 | WPF | **1차**(계약 확정) | `results/{sid}/` 파일 + `resultSessions/{sid}` 문서 함께 |
| GCS Lifecycle 규칙 | 인프라(자동) | **안전망**(계약 확정) | `results/` 프리픽스 파일만(age 기반) |
| Firestore 네이티브 TTL | 인프라 | **선택 권장** | `resultSessions` 만료 문서(고아 문서 축소) |
| 스케줄 Cloud Functions | 웹/인프라 | **미채택**(D-2) | — |

---

## 1. 불변식 (어떤 삭제 주체든 반드시 준수, 계약 §6.3)

1. **`results/` 프리픽스만** 삭제 대상이다.
2. **`frames/`(프레임 이미지)와 WPF 로컬 저장분(`saveLocalCopy`)은 삭제 대상이 아니다.** 프레임은 TTL 비대상이며, 로컬 저장분은 Firebase 와 무관하다.
3. 가능하면 **문서 + Storage 파일을 함께** 정리한다(문서만 남거나 파일만 남는 고아 상태 최소화).

---

## 2. GCS Lifecycle 규칙 적용 (안전망)

`results/` 프리픽스에만 age 기반 Delete 규칙을 건다. `retentionHours` 최댓값(72h = 3일)보다 여유를 둔 **age 3일** 예시를 `lifecycle.json`에 제공한다.

```bash
# 현재 버킷의 Lifecycle 설정 조회
gsutil lifecycle get gs://{BUCKET}

# lifecycle.json 을 적용 (web/lifecycle.json)
gsutil lifecycle set lifecycle.json gs://{BUCKET}
```

- `{BUCKET}`: 대상 Storage 버킷 이름(예: `{projectId}.firebasestorage.app`).
- ⚠️ **`results/` 한정, `frames/` 제외**: `lifecycle.json`의 `matchesPrefix`가 `["results/"]`인지 반드시 확인한다. `frames/`를 포함하면 프레임 이미지가 삭제되어 계약 불변식 위반이다.
- GCS Lifecycle 은 **Storage 파일만 삭제**하며 Firestore 문서는 지우지 못한다 → 문서 고아가 남을 수 있다(§3에서 완화).

### 콘솔 대안
Google Cloud Console > Cloud Storage > 버킷 선택 > "수명 주기" 탭 > 규칙 추가 > 작업=삭제, 조건=age 3일 + 접두어 `results/`.

---

## 3. Firestore 네이티브 TTL (권장, 선택)

WPF 직접 삭제가 정상 동작하면 고아 문서가 없다. 그러나 WPF 가 못 지운 경우 GCS Lifecycle 이 **파일만** 지워 문서 고아가 남을 수 있다. 이를 완화하려면 `resultSessions.expiresAt` 필드에 Firestore 네이티브 TTL 정책을 설정한다.

```bash
gcloud firestore fields ttls update expiresAt \
  --collection-group=resultSessions \
  --enable-ttl \
  --project={PROJECT_ID}
```

- 무료·서버리스·Functions 불요. 만료 문서를 자동 삭제해 고아 문서를 줄인다.
- 삭제는 즉시가 아니라 며칠 내 best-effort 다. 다만 웹은 이미 `expiresAt < now`로 만료를 판정하므로(§3.3), 문서가 늦게 지워져도 사용자에겐 만료로 보인다 → **정합성 문제 없음**.
- **권장이지 필수는 아니다.** 채택하지 않아도 웹은 §3.4 폴백(개별 미디어 로드 실패 → 부분 실패 표시, 둘 다 실패 시 만료 화면)으로 고아를 우아하게 처리한다.

### 콘솔 대안
Firebase Console > Firestore Database > "TTL" 탭 > 정책 만들기 > 컬렉션 그룹=`resultSessions`, 타임스탬프 필드=`expiresAt`.

---

## 4. 웹의 TTL 관련 책임

- 웹은 삭제를 **수행하지 않는다**.
- 만료(`expiresAt < now`)·문서 부재(not-found)·파일 부재(미디어 로드 실패)를 판정해 **만료/부분 실패 안내만** 표시한다(`web-architecture.md` §3.3/§3.4).
