# 08 · 서버·인프라 선행 작업 (착수 전 필수)

| 항목 | 값 |
|------|-----|
| 문서 | 웹 클라이언트 코드를 쓰기 전에 처리해야 하는 **서버·GCP·Google Console 작업** |
| 근거 | `docs/analysis/05 §9`(블로커 B1~B8) · `docs/analysis/61 §2·§4`(제약과 확장 설계) · `docs/analysis/90 §7.2` |
| 서버 소스 | `web/functions/src/` — 아래 각 절에 파일·함수명 명시 |
| 배포 주의 | 실배포 IAM 함정은 `docs/analysis/80` 및 프로젝트 메모리 참조(compute 서비스 계정에 Editor + Token Creator 수동 부여 필요 이력) |
| 갱신 규칙 | 항목이 완료되면 상태를 갱신하고 `docs/analysis/90 §7.2`의 블로커 표도 함께 갱신한다 |

---

## 1. 작업 목록과 의존 관계

| # | 항목 | 블로커 | 없으면 못 하는 것 | 우선도 |
|---|------|:------:|-------------------|:------:|
| **P0-1** | Google Console에 **Web application** OAuth 클라이언트 생성 + 리디렉트 URI 등록 | — | 로그인 전부 | **P0** |
| **P0-2** | 서버 `redirectUri` 검증 **허용 목록화** | B1 | 로그인 전부(현재 https는 400) | **P0** |
| **P0-3** | 서버 audience **목록화** | B2 | 로그인 전부(웹 client_id 거부) | **P0** |
| **P0-4** | **웹 전용 게이트 키** 발급 | B4 | 업로드·공용 프레임 조회·로그인 | **P0** |
| **P0-5** | **Storage 버킷 CORS 구성** | B5 | **업로드 PUT**(실측상 필수 — `web/OPS-cors.md`). 프레임 GET 합성은 서비스 레벨 `ACAO:*`로 될 가능성이 높으나 200 검증 잔여(§5.1) | **P0** |
| **P0-6** | Hosting 멀티사이트 타깃 `kiosk` 생성 | — | 배포 | **P0** |
| P1-1 | 웹 게이트 키에 Origin 제한·rate limit | B4 후속 | (보안 강화) | P1 |
| P1-2 | 번들 기본 프레임 자산 준비 | — | 오프라인 폴백 품질 | P1 |
| — | ~~B3 client_secret 조건부 분기~~ | — | **웹에는 불필요**(Web application 유형은 secret이 있다) | — |
| — | ~~B6 타임랩스 서버 변환~~ | — | **불필요**(WD2로 클라이언트에서 해결) | — |

### 1.1 선행 작업 없이 진행 가능한 범위 (중요)

```
P0-5(CORS) + P0-6(Hosting) 만 완료  →  촬영 전 흐름 + 업로드 3단계 + QR 을 구현·검증 가능
                                        (프레임 선택·촬영·컷 선택·합성·필터·타임랩스·로컬 보관·업로드·QR)
P0-1~4 추가 완료                    →  로그인 · 프레임 저작 · 계정 · 사용자 관리 · PIN
```

WBS는 이 순서로 짜여 있다([11](./11-wbs.md)). **P0-1~4를 기다리며 손을 놓을 필요가 없다.**

> ⚠️ **업로드·QR의 "제품 동작" 검증에는 로그인이 필요하다.** 서버는 게스트(무토큰) 업로드를 허용하지만(`optionalBearer`), **클라이언트 정책상 게스트는 `Qr`에 도달하지 않는다**(effective QR off → `Result → Done`, [03 §8.1](./03-screens-spec.md)). 따라서 P0-5·P0-6만으로 검증할 수 있는 것은 **업로드 3단계·QR 렌더 경로 자체**(무토큰으로 prepare/PUT/commit이 CORS를 통과하고 QR이 그려지는지 — 개발 중 임시로 판정을 우회해 확인)이며, **화면 흐름을 통한 종단 검증은 P0-1~4 완료 후 로그인 상태에서** 수행한다([11 Step 11·12](./11-wbs.md)).

---

## 2. P0-6 · Hosting 멀티사이트

```bash
cd web
npx firebase hosting:sites:create mcphoto-955fb-kiosk
npx firebase target:apply hosting kiosk   mcphoto-955fb-kiosk
npx firebase target:apply hosting default mcphoto-955fb
```

그다음 `web/firebase.json`의 `hosting`을 배열로 바꾼다([01 §5.1](./01-tech-stack-and-structure.md)의 JSON 참조).

| 검증 | 방법 |
|------|------|
| P1 사이트 무변경 | `npx firebase deploy --only hosting:default` 후 `https://mcphoto-955fb.web.app/?s=<유효토큰>`이 기존과 동일하게 동작 |
| 앱 사이트 서빙 | `npx firebase deploy --only hosting:kiosk` 후 `https://mcphoto-955fb-kiosk.web.app/`가 200 |
| CSP 적용 | 브라우저 콘솔에 CSP 위반이 없고, 응답 헤더에 `Content-Security-Policy`가 있다 |

> ⚠️ `target:apply`를 **default에도 반드시** 실행한다. 하나만 지정하면 `firebase deploy --only hosting`이 지정된 타깃만 배포하거나 오류가 난다.

---

## 3. P0-1 · Google OAuth 클라이언트 (Web application)

### 3.1 Google Cloud Console 작업

1. **APIs & Services → Credentials → Create Credentials → OAuth client ID**
2. Application type: **Web application**
3. Name: `MCPhoto Web Kiosk`
4. **Authorized JavaScript origins**
   - `https://mcphoto-955fb-kiosk.web.app`
   - `http://localhost:5173` (개발)
5. **Authorized redirect URIs** — **완전 일치**해야 한다
   - `https://mcphoto-955fb-kiosk.web.app/oauth2callback`
   - `https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback` (Hosting 두 번째 기본 도메인 — 누락 시 이 도메인 접속 기기에서 로그인 실패)
   - `http://localhost:5173/oauth2callback` (개발)
   - (Hosting preview channel을 쓸 경우 그 도메인도 추가 — 채널 URL은 매번 달라지므로 **고정 채널명**을 쓴다: `firebase hosting:channel:deploy dev --expires 30d` → `https://mcphoto-955fb-kiosk--dev-<hash>.web.app`. 해시가 붙어 고정이 어려우므로 **개발은 localhost, 실기기 검증은 운영 사이트 또는 커스텀 도메인**을 쓰는 편이 낫다)
6. 발급된 **client_id**를 `VITE_GOOGLE_CLIENT_ID`로, **client_secret**을 서버 시크릿에 등록(§4.2).

| 주의 | 내용 |
|------|------|
| Desktop 클라이언트와 **공유 불가** | 현재 등록된 것은 Desktop app 유형 1개(Windows용)다. 유형이 다르면 별 클라이언트가 필요하다 |
| 동의 화면 | **기존 것을 공유**한다(프로젝트 단위). 추가 작업 없음 |
| `GOOGLE_ALLOWED_HD` | 도메인 제한을 쓰고 있다면 웹도 동일하게 적용된다(변경 불필요) |

---

## 4. 서버 코드 변경 (P0-2 · P0-3 · P0-4)

> 이 절은 **설계 제안**이다. 실제 구현 시 서버 이터레이션으로 별도 설계·리뷰·테스트를 거친다(`docs/analysis/61 §4`).

### 4.1 P0-2 · `redirectUri` 허용 목록화 (B1)

**현재 상태**: `web/functions/src/domain/validation.ts`의 `validateLoopbackRedirectUri`가 scheme `http` + host `127.0.0.1`/`localhost` + 경로 `/`만 허용하고, 쿼리·프래그먼트를 금지한다. → **웹의 `https://…/oauth2callback`은 400**이다.

**변경 방향**

```ts
// config.ts — 환경변수로 허용 목록을 주입 (CSV)
//   OAUTH_REDIRECT_ALLOWLIST=https://mcphoto-955fb-kiosk.web.app/oauth2callback,http://localhost:5173/oauth2callback
export function validateRedirectUri(raw: string, allowlist: string[]): string {
  const uri = String(raw ?? "").trim();
  if (uri.length === 0 || uri.length > 256) throw invalidArgument("redirectUri 형식이 올바르지 않습니다.");
  if (isLoopback(uri)) return validateLoopbackRedirectUri(uri);   // 기존 데스크톱 경로 — 그대로 유지
  if (allowlist.includes(uri)) return uri;                        // ★ 완전 일치만
  throw invalidArgument("redirectUri 형식이 올바르지 않습니다.");
}
```

| 필수 제약 | 이유 |
|-----------|------|
| **완전 일치(exact match)만** | prefix·정규식 매칭은 **open redirect / SSRF** 통로가 된다. `redirectUri`는 서버가 Google에 보내는 code 교환 요청에 그대로 실린다 |
| 기존 loopback 경로 **무변경** | 배포된 Windows 클라이언트가 계속 동작해야 한다 |
| 허용 목록은 **환경변수**로 | 코드 재배포 없이 도메인 추가 가능 |
| 길이 상한 유지 | 256자 |

**회귀 테스트 추가**(`web/functions/src/__tests__/validation.test.ts`)

- 데스크톱 loopback(`http://127.0.0.1:53412/`)이 여전히 통과한다
- 허용 목록의 https URI가 통과한다
- **허용 목록 밖의 https URI가 400으로 거부된다**
- prefix만 같은 URI(`https://mcphoto-955fb-kiosk.web.app.evil.com/oauth2callback`)가 **거부된다**
- 쿼리·프래그먼트가 붙은 허용 목록 URI가 거부된다(완전 일치이므로 자동)

### 4.2 P0-3 · audience 목록화 (B2)

**현재 상태**: `web/functions/src/services/googleAuth.ts`의 `assertPayloadAndExtractEmail`이 `payload.aud !== cfg.clientId`면 거부하고(`verifyIdToken`의 `audience`도 단일 `cfg.clientId`), code 교환도 단일 client_id/secret을 쓴다. SSO 활성 판정(`googleOAuthEnabled`)은 **`GOOGLE_OAUTH_CLIENT_ID`와 `GOOGLE_OAUTH_CLIENT_SECRET`이 둘 다 비어 있지 않은지**이며, id만 있고 secret이 없으면 **`loadConfig()`가 예외로 조기 실패**한다(`config.ts` — 오구성 배포 방지). 목록화 시 이 조기 실패 규칙도 "선택된 `clientKind`의 secret이 없으면 실패"로 함께 옮겨야 한다.

**변경 방향**

```ts
// config.ts
//   GOOGLE_OAUTH_CLIENTS=desktop:<id>,web:<id>            (kind:client_id)
//   GOOGLE_OAUTH_CLIENT_SECRET_WEB=<secret>               (Secret Manager)
// 요청에 clientKind를 명시 받는다 (리디렉트 형태 추론보다 명확 — analysis/61 §4.2)
//   POST /auth/google { code, codeVerifier, redirectUri, nonce?, clientKind?: "desktop"|"web" }
//   clientKind 미지정 → "desktop" (하위 호환)
```

| 변경점 | 내용 |
|--------|------|
| `verifyIdToken` | `audience`에 **client_id 배열**을 넘긴다 |
| 방어적 재확인 | `payload.aud`가 **목록에 포함**되는지로 바꾼다 |
| code 교환 | `clientKind`에 맞는 client_id/secret 쌍을 고른다(웹도 secret 사용 — **B3는 불필요**) |
| SSO 활성 판정 | "목록이 비어 있지 않은가"로 변경 |
| 하위 호환 | `clientKind` 미지정 = desktop. **기존 Windows 클라이언트는 무변경으로 계속 동작** |

**회귀 테스트 추가**(`web/functions/src/__tests__/googleAuth.test.ts`)

- 목록에 있는 두 client_id의 `aud`가 각각 통과한다
- **목록 밖 `aud`가 401로 거부된다**
- `clientKind: "web"`이 웹 secret으로 교환을 시도한다
- `clientKind` 미지정이 desktop 구성으로 동작한다(하위 호환)
- **`clientKind`가 화이트리스트 밖 문자열이면 400**(임의 값으로 구성을 고르지 못하게)
- `email_verified: false`가 여전히 거부된다
- **선택된 `clientKind`의 secret이 미설정이면 조기 실패**(현행 `config.ts`의 `hasId && !hasSecret` 가드와 동일한 성질 유지)

### 4.3 P0-4 · 웹 전용 게이트 키 (B4)

**현재 상태**: `CLIENT_API_KEYS`(Secret Manager, CSV)에 등록된 키만 `X-MCPhoto-Client`로 통과한다. Windows exe에는 빌드 시 주입된 키가 들어 있다.

**작업**

```bash
# 새 키 생성(예: 32바이트 난수 base64url)
node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"

# Secret Manager의 CLIENT_API_KEYS에 CSV로 추가 (기존 키 유지!)
#   <windows-key>,<web-key>
npx firebase functions:secrets:set CLIENT_API_KEYS
# → 프롬프트에 기존 값 + 콤마 + 새 키를 함께 입력한다

npx firebase deploy --only functions   # 시크릿 변경은 재배포가 필요하다
```

| 규칙 | 내용 |
|------|------|
| **기존 키를 지우지 않는다** | 배포된 Windows 클라이언트가 즉시 죽는다 |
| 웹 키는 **공개된다** | 게이트 키는 인증이 아니라 배포 식별자다. 역할·과금 한도는 서버가 JWT로 강제한다(WD10) |
| 유출 대응 | **그 키만 CSV에서 제거**하고 웹을 재배포하면 된다 |
| 저장 | 저장소에 커밋하지 않는다. `.env.production.local` 또는 CI 시크릿 |

**검증**: 웹 키로 `GET /frames/default`가 200, 임의 문자열 키는 **401**.

### 4.4 P1-1 · Origin 제한·rate limit (후속 권장)

| 방안 | 내용 | 비용 |
|------|------|------|
| Origin 검사 | 웹 키로 들어온 요청의 `Origin`이 허용 목록인지 확인(브라우저는 위조 불가, 비브라우저는 우회 가능) | 미들웨어 소량 |
| rate limit | 키·IP 단위 요청 한도(Cloud Armor 또는 함수 내 카운터) | 중간 |
| 판단 | **P0가 아니다.** 과금 안전은 이미 TempUser 한도(서버 트랜잭션)가 담보하고, 관리 API는 전부 Bearer 게이트다 | — |

---

## 5. P0-5 · Storage 버킷 CORS (B5) — 가장 흔한 실패 지점

> **선행 실측 문서**: [`web/OPS-cors.md`](../../web/OPS-cors.md)(2026-07-30, it17 다운로드 개선 작업에서 작성). 핵심 실측 2건 —
> ① **다운로드 호스트 `firebasestorage.googleapis.com`은 서비스 레벨에서 `Access-Control-Allow-Origin: *`를 항상 반환**한다(버킷 CORS 구성과 무관. 단 **403 응답에서 관측했고 200 응답은 미확인** — OPS-cors §1.5).
> ② **버킷 레벨 CORS는 현재 미설정**이며, 서명 PUT 호스트 `storage.googleapis.com`에는 Access-Control 헤더가 **전무**하다.
> → 결론: **P1 다운로드(GET)에는 버킷 CORS가 불필요**했지만, **웹 앱의 서명 URL PUT에는 여전히 필수**다. OPS-cors §3도 "향후 필요 시점 = 업로드 PUT(B5)"로 같은 결론이다.

### 5.1 무엇에 필요한가 (실측 반영)

| # | 용도 | 호스트 | 버킷 CORS 필요? |
|---|------|--------|-----------------|
| 1 | **서명 URL PUT** — 커스텀 헤더(`x-goog-meta-…`) 때문에 OPTIONS preflight 발생 | `storage.googleapis.com` | **필수**(실측: 이 호스트는 Access-Control 헤더 전무). 없으면 업로드 전부 실패(브라우저는 네트워크 오류로만 보인다) |
| 2 | **서버 프레임 이미지 GET** — canvas 합성용 CORS-clean 로드(WM2) | `firebasestorage.googleapis.com` | **불필요할 가능성이 높다**(서비스 레벨 `ACAO: *` 관측). 단 ⓐ `crossOrigin="anonymous"` 지정은 **여전히 필수**(속성 없이 그리면 CORS 헤더가 있어도 canvas가 오염된다) ⓑ 200 응답 검증이 잔여(§5.3 ②가 그 확정이다). 200에서 거부되면 아래 구성의 GET이 안전망 |

> ⚠️ 기존 문서(`analysis/05 §9 B5`)는 1번만 적고 있었고, 2번은 웹 촬영 설계에서 추가로 식별한 뒤 **OPS-cors 실측으로 "필수 → 검증 항목"으로 완화**된 것이다. 구성에는 GET을 계속 포함한다(불확실성 ⓑ의 안전망 + 비용 0).

### 5.2 CORS 구성 파일

```json
[
  {
    "origin": [
      "https://mcphoto-955fb-kiosk.web.app",
      "https://mcphoto-955fb-kiosk.firebaseapp.com",
      "http://localhost:5173"
    ],
    "method": ["GET", "HEAD", "PUT", "OPTIONS"],
    "responseHeader": [
      "Content-Type",
      "Content-Length",
      "x-goog-meta-firebaseStorageDownloadTokens",
      "x-goog-resumable",
      "ETag"
    ],
    "maxAgeSeconds": 3600
  }
]
```

적용:

```bash
gcloud storage buckets update gs://mcphoto-955fb.firebasestorage.app --cors-file=cors.json
# 확인
gcloud storage buckets describe gs://mcphoto-955fb.firebasestorage.app --format="default(cors_config)"
```

- 구성 파일은 **`web/cors.json`으로 신설**하고 커밋한다. `web/OPS-cors.md`는 "GET용으로는 불필요해 구성 파일을 두지 않는다"고 결정했는데, **PUT용으로 필요해진 시점이 지금**이다 — 적용 후 OPS-cors.md의 결론 표("설정 불필요" → "PUT용 구성 적용됨")를 함께 갱신한다.
- ⚠️ `--cors-file`은 **기존 구성을 병합하지 않고 전체 교체**한다(`web/OPS-cors.md §3`). 현재 버킷 CORS는 비어 있으므로 지울 것이 없지만, 이후 규칙을 추가할 때는 **파일에 기존 규칙 객체를 함께 담아** 적용한다.
- 이 PC에 `gcloud`가 없다는 실측 기록이 있다(OPS-cors §1) — 적용은 Cloud Shell 또는 gcloud 설치 후 수행한다.

| 항목 | 주의 |
|------|------|
| `responseHeader` | GCS에서 이 목록이 **`Access-Control-Allow-Headers`** 로 반영된다. `x-goog-meta-firebaseStorageDownloadTokens`가 빠지면 PUT preflight가 실패한다(M14와 직결) |
| `origin` | **와일드카드(`*`)를 쓰지 않는다.** 운영 도메인 + 개발 오리진만 |
| 커스텀 도메인 | 나중에 붙이면 목록에 추가해야 한다 |
| 전파 | 즉시~수분. 브라우저 preflight 캐시(`maxAgeSeconds`) 때문에 테스트 시 시크릿 창을 쓴다 |

### 5.3 검증 절차 (둘 다 확인해야 통과다)

**① 업로드 PUT**

```
1. 웹 앱(또는 임시 페이지)에서 POST /uploads/prepare 호출 → putUrl·requiredHeaders 획득
2. XHR PUT 실행
3. 개발자 도구 Network에서 OPTIONS(204) → PUT(200)을 확인
4. commit 후 다운로드 페이지에서 파일이 열리는지 확인
```

**② 프레임 이미지 canvas 합성**

```
1. GET /frames/default 로 imageUrl 획득
2. fetch(imageUrl, {mode:"cors"}) → createImageBitmap → OffscreenCanvas에 draw
3. canvas.convertToBlob() 호출 → 예외가 없으면 통과
   (SecurityError가 나면 CORS 미적용 — 이 검증이 핵심이다)
```

| 실패 증상 | 원인 |
|-----------|------|
| PUT이 `net::ERR_FAILED`·상태 0 | CORS 미구성 또는 `responseHeader` 누락 |
| `SecurityError: tainted canvas` | 프레임 이미지 GET에 CORS 헤더 없음(또는 `crossOrigin` 미지정) |
| PUT 403 | 서명 불일치 — `requiredHeaders` 누락·변형(CORS 문제가 아니다) |

---

## 6. P1-2 · 번들 기본 프레임 자산

오프라인·서버 미도달 시 목록이 비지 않으려면 번들 프레임이 필요하다(`analysis/13 §5` ③단계).

| 항목 | 규격 |
|------|------|
| 위치 | `webclient/public/frames/` |
| 형식 | `{이름}.png` + `{이름}.slots`(`analysis/41 §3.3` 포맷) |
| 이름 | **`_` 금지**, 파일시스템 금지문자 금지 |
| 최소 수량 | 1개 이상(권장 3~4개: 4컷·6컷·세로 스트립) |
| id | 로드 시 `bundle:{이름}` 접두를 붙인다(출처 판정 — 편집·삭제 불가) |
| `.slots` 없을 때 | **2×2 격자 자동 생성**(`analysis/13 §5` ③) |
| 최종 안전망 | 번들도 없으면 **코드 생성 fallback**(1200×1600 하양, 2×2 슬롯 4개, `analysis/14 §4.7`) |

> 현재 Windows 저장소에는 배포용 번들 프레임 PNG가 커밋돼 있지 않다(`Example/Frame.png` 등 예시 이미지만 존재). **디자인 자산을 새로 준비**하거나 fallback만으로 시작한다.

---

## 7. 완료 확인 체크리스트

| # | 확인 | 방법 |
|---|------|------|
| P0-1 | 웹 client_id 발급, 리디렉트 URI **완전 일치** 등록 | Console 화면 |
| P0-2 | 허용 목록의 https URI가 통과, **목록 밖은 400** | 서버 테스트 + 실제 로그인 |
| P0-2 | **데스크톱 loopback이 여전히 통과** | 서버 테스트 + Windows 앱 로그인 실측 |
| P0-3 | 웹 client_id의 `aud`가 통과, **목록 밖은 401** | 서버 테스트 |
| P0-4 | 웹 키로 `GET /frames/default` 200, 임의 키 401 | curl |
| P0-4 | **Windows 키가 여전히 유효** | Windows 앱 실측 |
| P0-5 | XHR PUT의 OPTIONS 204 → PUT 200 | 브라우저 Network |
| P0-5 | **`canvas.convertToBlob()`이 예외 없이 성공** | §5.3 ② |
| P0-6 | kiosk 사이트 200 + CSP 헤더 존재 + **P1 사이트 무변경** | curl / 브라우저 |

> **완료 후**: `docs/analysis/90 §7.2`의 블로커 표에서 해당 행의 상태를 갱신하고, `docs/analysis/61 §2`의 제약 표(C1·C2)에 "웹 해소됨"을 반영한다.
