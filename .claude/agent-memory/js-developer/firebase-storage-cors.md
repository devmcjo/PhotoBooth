---
name: firebase-storage-cors
description: firebasestorage.googleapis.com은 버킷 CORS와 무관하게 ACAO:*를 반환한다 — 다운로드 GET에 버킷 CORS 설정이 불필요한 이유
metadata:
  type: project
---

# 다운로드 토큰 URL은 버킷 CORS 없이 브라우저 fetch 가 된다

**두 호스트는 CORS 동작이 다르다.** 2026-07-30 실측(무인증 curl, 403 응답):

| 호스트 | 용도 | `Access-Control-Allow-Origin` |
|---|---|---|
| `firebasestorage.googleapis.com/v0/b/{bucket}/o/…?alt=media&token=` | **다운로드**(웹 페이지가 쓰는 URL) | **`*`** (서비스 프론트엔드가 항상 반환. 버킷 CORS 구성과 **무관**) |
| `storage.googleapis.com/{bucket}/…` (V4 서명 URL) | **업로드 PUT** 전용 | **없음** (버킷 CORS 구성을 그대로 반영 — 현재 미설정) |

`Access-Control-Expose-Headers`에 `Content-Length`가 포함되므로 브라우저 JS 가 용량을 읽을 수 있다.

**Why:** it17 설계는 "버킷 CORS(GET) 미구성"을 자동 저장의 인프라 선행 조건(리스크 R1)으로 잡았으나,
실측 결과 **선행 조건이 아니었다**. 두 호스트를 혼동하면 불필요한 인프라 변경(`gsutil cors set`은 **전체 교체**라 위험)을 하게 된다.

**How to apply:**
- 웹에서 결과물 바이트를 `fetch`할 때 **버킷 CORS 설정을 요구하지 마라.** `web/cors.json`은 두지 않는다(의도적 부재).
- 버킷 CORS 가 실제로 필요해지는 유일한 시점은 **브라우저 업로드 PUT**(로드맵 B5)이다. 그때 PUT 규칙의 `origin`은 `*` 금지.
- **잔여**: 위 관측은 전부 **403**(무효 토큰)에서 얻었다. **200 응답 확인은 실토큰 스모크 잔여**다 — "확인 완료"로 말하지 않는다.
- `gcloud`·`gsutil`은 이 PC 에 **설치돼 있지 않다.** 버킷 구성 조회를 시도하지 말고, 위 형태의 `curl -H "Origin: …"` 프로브로 대체한다.
- **CORS 가 해소돼도 graceful degrade 를 제거하지 마라** — 인앱 브라우저(`download` 미동작)·구형 엔진·네트워크·비2xx(토큰 만료·TTL)·용량·사용자 활성화 만료가 남는다. 관련: [[web-public-verification]]

운영 문서: `web/OPS-cors.md`(불필요 판정 근거 + 컨틴전시 + B5 경고). 상세: `docs/analysis/20` §7C.
