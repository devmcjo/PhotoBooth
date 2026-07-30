# MCPhoto — 버킷 CORS 판정 (다운로드 GET: **설정 불필요**)

| 항목 | 값 |
|------|-----|
| 결론 | **버킷 CORS 설정은 불필요하다**(2026-07-30 실측). 웹 다운로드 페이지의 자동 저장은 버킷 CORS 없이 동작한다 |
| 근거 | 다운로드 URL 호스트인 `firebasestorage.googleapis.com`은 **서비스 프론트엔드가 `Access-Control-Allow-Origin: *`를 항상 반환**하며, 이는 버킷 CORS 구성과 무관하다(§1 실측) |
| 이 프로젝트 | project=`mcphoto-955fb`, bucket=`mcphoto-955fb.firebasestorage.app` |
| 구성 파일 | **없다.** `web/cors.json`을 두지 않는다 — 적용할 필요가 없는 설정의 구성 파일은 오해를 만든다 |
| 향후 필요 시점 | **업로드 PUT**(로드맵 [B5](../docs/analysis/90-roadmap-and-future-work.md))에서만 필요하다. §3 참조 |
| 웹 코드 영향 | **없음.** 자동 저장의 폴백 경로는 CORS와 무관하게 **그대로 유지한다**(§4) |

> 이 문서는 "왜 버킷 CORS를 설정하지 않았는가"의 근거와, 만약 필요해질 경우의 절차를 남긴다.
> **지금 실행할 작업은 없다.**

---

## 1. 실측 (2026-07-30)

`gcloud`·`gsutil`이 이 PC에 설치되어 있지 않아 `buckets describe`로 버킷 구성을 조회할 수 없었다. 대신 무인증 read-only HTTP 프로브로 **실제 응답 헤더**를 관측했다 — 자동 저장이 의존하는 것은 버킷 구성 자체가 아니라 이 응답 헤더이므로, 이 관측이 조회를 대신한다.

### 1.1 다운로드 호스트 — `ACAO: *` 있음

```bash
curl -H "Origin: https://mcphoto-955fb.web.app" \
  "https://firebasestorage.googleapis.com/v0/b/mcphoto-955fb.firebasestorage.app/o/probe-nonexistent.jpg?alt=media"
```
```
HTTP/1.1 403 Forbidden
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Cache-Control, Content-Length, Content-Range, Date, Expires,
                               Server, Transfer-Encoding, X-Firebase-Storage-XSRF,
                               X-GUploader-UploadID, X-Google-Trace
```

`Origin: http://localhost:5000`(Emulator 검증 오리진)으로도 동일하게 `ACAO: *`가 반환됐다.

### 1.2 대조군 — GCS 직접 호스트는 CORS 헤더가 **없다**

```bash
curl -H "Origin: https://mcphoto-955fb.web.app" \
  "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/probe-nonexistent.jpg"
```
```
HTTP/1.1 403 Forbidden
     ← Access-Control-* 헤더 전무
```

OPTIONS 프리플라이트도 동일하다(200이지만 `Access-Control-*` 없음). 즉 **버킷 레벨 CORS는 미설정 상태**이고, 그럼에도 §1.1이 `ACAO: *`를 준다 → 두 레이어가 별개임이 확인된다.

### 1.3 다운로드 URL은 항상 §1.1의 호스트다

| 사실 | 근거 |
|---|---|
| 다운로드 URL 조립이 `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media&token=` 형식으로 고정 | `web/functions/src/domain/session.ts:73` |
| `commit`의 `assertUrlBelongsToSession`이 그 prefix를 강제 검증 | `web/functions/src/services/uploads.ts:135` |
| `storage.googleapis.com` V4 서명 URL은 **업로드 PUT 전용** | 다운로드 경로와 무관 |

→ 웹이 `fetch`하는 대상은 **언제나 CORS 헤더가 붙는 호스트**다.

### 1.4 부수 확인 — 용량 가드가 헤더를 읽을 수 있다

`Access-Control-Expose-Headers`에 **`Content-Length`가 포함**되어 있다(§1.1). 따라서 자동 저장의 150MB 용량 가드가 값을 읽을 수 있다. (읽지 못해도 가드는 `NaN` → 무동작으로 안전하게 설계돼 있다.)

### 1.5 ⚠️ 잔여 불확실성 — 403에서 관측, 200 확인은 스모크 잔여

**위 관측은 전부 `403 Forbidden` 응답에서 얻었다**(유효 토큰이 없어 실제 바이트를 받지 못했다).

- **200 응답(실제 바이트)에 같은 헤더가 붙는지는 확인되지 않았다.**
- 확정 방법: 실배포 페이지를 **유효 토큰**으로 열고 devtools > Network에서 토큰 URL 응답의 `access-control-allow-origin` 유무를 확인한다.
- 이 문서를 "확인 완료"로 읽어서는 안 된다. **"403에서 관측됨 / 200은 스모크 잔여"** 가 정확한 상태다.
- 만약 200에서 CORS가 거부되면 §2의 절차를 꺼낸다.

---

## 2. 만약 200 경로에서 CORS가 거부된다면 (컨틴전시 — 지금은 실행하지 않음)

§1.5가 뒤집혔을 때만 해당한다. **자동 저장이 실패해도 장애가 아니다**(§4) — 먼저 그 사실을 확인하고, 그래도 자동 저장을 성립시켜야 한다면 아래를 적용한다.

```bash
# 1) 현재 구성 조회 (storage.buckets.get 권한 필요, 무인증 조회 불가)
gcloud storage buckets describe gs://mcphoto-955fb.firebasestorage.app --format="json(cors_config)"

# 2) GET/HEAD 규칙을 파일로 만들어 적용 (⚠️ 전체 교체 — §3)
cat > /tmp/cors-get.json <<'JSON'
[
  {
    "origin": ["*"],
    "method": ["GET", "HEAD"],
    "responseHeader": ["Content-Type", "Content-Length"],
    "maxAgeSeconds": 3600
  }
]
JSON
gcloud storage buckets update gs://mcphoto-955fb.firebasestorage.app --cors-file=/tmp/cors-get.json
```

### `origin: ["*"]`를 쓰는 근거 (보안 판정)

다운로드 토큰 URL은 계약상 **그 자체가 capability**다. URL을 아는 주체는 이미 `curl`·서버·앱 등 **CORS가 개입하지 않는 모든 경로로 바이트를 읽을 수 있다.** CORS는 "브라우저 JS가 응답 바디를 읽는 것"만 통제하므로, **GET에 `*`를 허용해도 공격자에게 새로 주는 능력이 없다.**

반대로 오리진을 열거하면 Hosting 프리뷰 채널(`project--preview-xxxx.web.app`)·커스텀 도메인이 추가될 때마다 조용히 깨진다.

---

## 3. ⚠️ 향후 B5(업로드 PUT) 착수 시 — CORS 설정은 전체 교체다

버킷 CORS가 **실제로 필요해지는 유일한 시점은 브라우저 업로드 PUT**([90 §B5](../docs/analysis/90-roadmap-and-future-work.md))이다. 이때 반드시 알아야 할 것:

`gcloud storage buckets update --cors-file` / `gsutil cors set`은 기존 구성을 **덮어쓴다**(병합이 아니다). 현재 버킷 CORS가 비어 있으므로 지울 것은 없지만, 그 시점에 §2를 적용해 둔 상태라면 **GET 규칙을 지우지 않도록 규칙 객체를 추가**해야 한다.

**PUT 규칙의 `origin`은 절대 `*`로 두면 안 된다** — 읽기와 달리 쓰기는 실제로 보호가 필요하다.

```json
[
  { "origin": ["*"], "method": ["GET","HEAD"], "responseHeader": ["Content-Type","Content-Length"], "maxAgeSeconds": 3600 },
  { "origin": ["https://mcphoto-955fb.web.app"], "method": ["PUT"],
    "responseHeader": ["Content-Type","x-goog-meta-firebaseStorageDownloadTokens"], "maxAgeSeconds": 3600 }
]
```

---

## 4. 폴백 경로는 그대로 유지한다 (CORS 해소와 무관)

**CORS가 해소됐다는 이유로 graceful degrade를 제거하지 않는다.** `fetch` 실패 원인은 CORS만이 아니다.

| 여전히 남아 있는 실패 요인 | 설명 |
|---|---|
| 인앱 브라우저(카카오톡·인스타그램·네이버 앱) | `download` 지원이 엔진·앱 버전별로 갈린다. 기능 감지를 통과하고도 실제 저장이 안 될 수 있다 |
| 구형 엔진(iOS Safari < 13 등) | `download` 미지원 → 기능 감지에서 걸러진다 |
| 네트워크 실패·타임아웃 | 모바일 회선에서 흔하다 |
| 비2xx 응답 | 토큰 만료·파일 물리 삭제(TTL) 후의 403/404 |
| 용량 초과 | 계약을 벗어난 이상 크기(150MB 가드) |
| 사용자 활성화 만료 | `await` 이후 `a.click()`이 차단될 수 있다 |

실패 시 웹은 **첫 실패에서 자동 저장 능력을 내리고 종전 동작으로 되돌아간다**(설계 §3.3-D 전역 degrade).

| 관측 | 설명 |
|---|---|
| warn 토스트 | "자동 저장이 지원되지 않는 환경입니다. 원본을 열었으니 길게 눌러 저장해 주세요." |
| 원본으로 이동 | `location.assign(url)` — `<a target>` 없는 종전 동작과 **정확히 동일** |
| 수동 힌트 노출 | "저장이 안 되면 이미지를 길게 눌러(모바일)/우클릭(PC) 저장하세요." |
| 이후 클릭 | 개입 없이 즉시 원본으로 이동(재시도로 데이터를 낭비하지 않는다) |

즉 **최악의 경우가 종전 동작**이며 회귀가 없다.

---

## 5. 보안 규칙과의 관계 — 무관

- **`web/storage.rules`는 변경하지 않았다.** CORS는 Storage 보안 규칙과 **독립된 레이어**다.
- `results/`의 Storage SDK read는 계속 `false`로 닫혀 있다. 다운로드 토큰 URL은 애초에 보안 규칙을 우회하는 capability 경로다.
- 즉 CORS 상태가 어떻든 **규칙으로 막아 둔 열거·SDK 접근은 열리지 않는다.**
