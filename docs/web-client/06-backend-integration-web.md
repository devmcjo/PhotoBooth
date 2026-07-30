# 06 · 백엔드 연동 (웹)

| 항목 | 값 |
|------|-----|
| 문서 | HTTP 클라이언트 구조·헤더·엔드포인트 사용·에러 매핑·업로드 3단계의 웹 구현 |
| 규격 진실원 | **`docs/analysis/31-backend-api-reference.md`** — 경로·요청/응답 JSON·상태코드·에러 코드·검증 규칙은 그 문서가 진실원이다. **서버 소스**: `web/functions/src/routes/*.ts` |
| Windows 참조 | `src/MCPhoto.Http/{HttpBackendClient,HttpAccountService,HttpFrameRepository,HttpFirebaseClient,HttpQrUsageService,HttpTempUserLimitsService}.cs`, `src/MCPhoto.Core/Upload/{UploadService,UploadContract}.cs` |
| 갱신 규칙 | API가 바뀌면 `docs/analysis/31`을 먼저 고친다 |

---

## 1. 기본 사항

| 항목 | 값 |
|------|-----|
| Base URL | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` (설정 `BackendBaseUrl`, 트레일링 `/` 부여) |
| 서버 CORS | `cors({ origin: true })` — **모든 Origin 허용**. 브라우저에서 바로 호출 가능하며 **추가 서버 작업이 필요 없다**(`analysis/31 §1`) |
| 요청 본문 | JSON, **상한 256KB**. 파일 바이트는 API를 경유하지 않는다 |
| 타임아웃 | **100초**(`AbortController` + `setTimeout`) — Windows 값과 동일 |
| 재시도 | **자동 재시도를 하지 않는다.** 사용자 액션(재시도 버튼)으로만 |
| 에러 봉투 | `{"error":{"code":"...","message":"..."}}` 전 엔드포인트 공통 |

### 1.1 헤더

| 헤더 | 값 | 부착 규칙 |
|------|-----|-----------|
| `X-MCPhoto-Client` | 배포 게이트 키 | **값이 있으면 모든 백엔드 호출에 부착**(Windows와 동일한 관행이 안전하다, `analysis/31 §2`) |
| `Authorization` | `Bearer {JWT}` | **토큰이 있을 때만.** 업로드는 선택적 Bearer — 게스트면 붙이지 않는다(M1의 핵심) |
| `Content-Type` | `application/json` | 본문 있는 요청만 |

> ⚠️ **서명 URL PUT에는 위 두 헤더를 절대 붙이지 않는다**(`analysis/31 §5.2`). 붙이면 서명 검증이 깨지거나 CORS preflight가 실패한다.

### 1.2 클라이언트 구조

```
adapters/http/
  backendClient.ts        // fetch 래퍼: base URL 결합 · 헤더 · 타임아웃 · 에러 매핑 · 로깅
  accountService.ts       // /auth /accounts
  frameRepository.ts      // /frames
  uploadGateway.ts        // /uploads + 서명 PUT (XHR)
  qrUsageService.ts       // /accounts/me/qr-usage
  tempUserLimitsService.ts// /config/temp-user-limits
  healthService.ts        // /health
```

| 규칙 | 내용 |
|------|------|
| 단일 조립 지점 | URL·헤더·타임아웃·에러 매핑을 `backendClient` **한 곳**에서 처리한다(Windows `HttpBackendClient` 대응) |
| Bearer 필수 호출에 토큰이 없으면 | **요청을 보내지 않고** 즉시 실패: *"로그인이 필요합니다."*(`analysis/60 §4.5`) |
| Base URL이 비면 | 즉시 실패 + 진단에 "백엔드 미구성" 표시 |
| 로깅 | 메서드·경로·상태코드·`error.code`·소요 시간. **본문·토큰·URL 토큰은 남기지 않는다** |

---

## 2. 엔드포인트별 웹 사용

| 메서드·경로 | 게이트 | 웹에서 쓰는 화면 |
|-------------|--------|------------------|
| `GET /health` | — | 진단 모달 · 서버 연결 상태(설정 고급) |
| `POST /auth/google` | apiKey | `/oauth2callback` |
| `GET /accounts` | Bearer + power | `UserMgmt` |
| `GET /accounts/me/qr-usage` | Bearer | 상단바 배지 · 설정(TempUser 제한 판정) |
| `POST /accounts/me/pin/verify` | Bearer | PIN 모달(확인) |
| `PUT /accounts/me/pin` | Bearer | PIN 모달(최초 설정) · `Account` PIN 변경 |
| `DELETE /accounts/{id}` | Bearer + power | `UserMgmt` |
| `PATCH /accounts/{id}/role` | Bearer + power | `UserMgmt` |
| `PUT /accounts/{id}/pin` | Bearer + power | `UserMgmt`(타 계정 PIN 재설정) |
| `GET /config/temp-user-limits` | Bearer | `Account`(Admin) |
| `PATCH /config/temp-user-limits` | Bearer + admin | `Account`(Admin) |
| `GET /frames/default` | apiKey | `FrameSelect` · `FramePicker` |
| `GET /frames?userId=` | Bearer | (선택) 레거시 서버 개인 프레임 조회 — 보통 빈 배열 |
| `POST /frames` | Bearer + power | `FrameEditor` 공용 저장 |
| `PUT /frames/{id}` | Bearer + power | **호출하지 않는다**(편집은 로컬 전용 정책) |
| `DELETE /frames/{id}` | Bearer + power | 프레임 삭제(서버에서도 제거) |
| `POST /uploads/prepare` | apiKey + optionalBearer | `Qr` |
| `POST /uploads/commit` | apiKey + optionalBearer | `Qr` |

### 2.1 `GET /health`의 해석 주의

```jsonc
{ "status": "ok", "time": "…", "deployedAt": "…" }   // deployedAt은 유효 게이트 키일 때만 포함
```

- **키가 없거나 틀려도 200이다.** 헬스 응답으로 게이트 키 유효성을 **판정할 수 없다**.
- 게이트 키 유효성을 확정하려면 `GET /frames/default`(apiKey 게이트)의 **401 여부**로 확인한다. 진단 모달은 이 두 프로브를 함께 수행한다.
- "구성됨"과 "도달 성공"을 구분해 표시한다(`analysis/13 §9.2`).

---

## 3. 에러 매핑 (`analysis/31 §3`)

### 3.1 상태코드·코드 → 처리

| `code` | 상태 | 웹 처리 |
|--------|:----:|---------|
| `unauthorized` | 401 | **호출부가 결정**: 로그인 = "자격 실패" / PIN 검증 = "불일치" / 그 외 = "다시 로그인" 유도 |
| `forbidden` | 403 | "권한이 없습니다" 안내(우아 처리) |
| `not_found` | 404 | "대상을 찾을 수 없습니다" |
| `conflict` | 409 | **문맥별 분기 필수** — PIN 미설정(409)은 최초 설정 플로우로, 세션 재commit(409)은 이중집계 차단(정상), 프레임 10개 초과(409)는 상한 안내 |
| `invalid_argument` | 400 | 입력 오류 안내. 정상 흐름에서 발생하면 **구현 버그** |
| `not_implemented` | 501 | "로그인이 구성되지 않았습니다. 관리자에게 문의" — 자격 실패·네트워크와 **구분** |
| `internal` | 500 | "서버 오류" + 재시도 권장 |
| `TEMP_USER_TIME_EXCEEDED` | **403** | 고정 문구: *"무료 사용 시간이 지났습니다. 관리자에게 문의해주세요."* |
| `TEMP_USER_COUNT_EXCEEDED` | **403** | 고정 문구: *"무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요."* |

> **403의 두 얼굴**: 권한 부족과 무료 한도 초과가 같은 상태코드다. **반드시 `error.code`를 봐야** 구분된다.

### 3.2 네트워크 계층 실패 (상태코드 없음)

`fetch` rejection(연결 실패·DNS·타임아웃·**CORS 차단**)은 상태코드가 없다. 401/403과 **절대 섞지 않는다**.

| 판별 | 처리 |
|------|------|
| `AbortError`(타임아웃) | *"백엔드에 연결할 수 없습니다."* + 재시도 안내 |
| `TypeError: Failed to fetch` | 동상. **로그에 "네트워크 또는 CORS 차단 가능"** 을 남긴다(브라우저는 CORS 실패를 구분해 알려주지 않는다) |
| 오프라인 | 동상. 진단 화면의 서버 연결 상태가 실패로 표시된다 |

### 3.3 예외 타입 설계

```ts
class BackendError extends Error { status: number; code: string; }        // 서버 응답 있음
class NetworkError extends Error {}                                       // 응답 없음
class NotAuthenticatedError extends Error {}                              // 토큰 없이 Bearer 필수 호출
class TempUserLimitError extends BackendError { reason: "time" | "count" }
class SsoNotConfiguredError extends BackendError {}                       // 501
```
화면은 예외 타입으로 분기하고, 상태코드를 직접 비교하지 않는다.

---

## 4. 업로드 3단계 (`analysis/31 §5`) — 웹의 최대 난관

```
① POST /uploads/prepare   → 파일별 서명 PUT URL + 다운로드 URL + 필수 헤더
② PUT {putUrl}            → 파일 바이트 직접 전송 (백엔드 미경유)  ← CORS 필요
③ POST /uploads/commit    → resultSessions 문서 생성 → 다운로드 페이지 활성화
```

> **누가 이 경로를 타는가**: 서버 게이트는 `apiKey + optionalBearer`이므로 **무토큰(게스트) 업로드도 서버는 허용**한다. 그러나 **클라이언트는 effective QR이 on일 때만 업로드를 시작**하고 그것은 로그인 상태를 요구한다(`qrEffectivePolicy` — [03 §8.1](./03-screens-spec.md)) → **정상 흐름의 업로드에는 항상 Bearer가 붙는다.** `optionalBearer`에 기대는 무토큰 경로는 **남겨 두되(계약 유지) 정상 흐름에서 발생하지 않는다.** 그럼에도 M1 배선은 필수다 — 로그아웃 후 잔존 토큰이 붙으면 다른 계정의 한도가 차감된다.

### 4.1 ① prepare

```jsonc
// 요청 — 현행 Windows는 파일당 1회씩 호출한다. 웹도 같게 한다(진행률 단계 구분이 쉽다)
{ "sessionId": "20260730_111203_9f1c2a44-…",
  "files": [ { "kind": "final", "ext": "jpg", "contentType": "image/jpeg" } ] }
```

| 검증 | 값 |
|------|-----|
| `sessionId` | `^\d{8}_\d{6}_[0-9a-fA-F]{8}-…$` (M13) |
| `kind` | `"final"` 또는 `"timelapse"`(중복 금지) |
| `ext` / `contentType` | final → `jpg`\|`png` / `image/jpeg`\|`image/png`, timelapse → `mp4` / `video/mp4` |

| 응답 활용 | 내용 |
|-----------|------|
| `putUrl` | V4 서명 PUT URL, **TTL 15분** |
| `downloadUrl` | **commit에 그대로 넘긴다**(재조립 금지) |
| `requiredHeaders` | PUT에 **그대로 전부** 부착(M14) |
| `bucket` | 설정 `StorageBucket`을 이 값으로 갱신하면 URL 재조립이 서버와 일치한다 |

403 `TEMP_USER_*`이면 **서명 URL이 발급되지 않는다** → 즉시 실패 처리(사유별 문구).

### 4.2 ② 서명 PUT — XHR로 구현한다 (WM5)

```ts
function putSigned(url: string, blob: Blob, headers: Record<string,string>, onProgress: (loaded:number,total:number)=>void) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", url, true);
    for (const [k, v] of Object.entries(headers)) xhr.setRequestHeader(k, v);  // ← 응답 객체 그대로 순회
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded, e.total);
    xhr.onload  = () => (xhr.status >= 200 && xhr.status < 300) ? resolve() : reject(new NetworkError(`PUT ${xhr.status}`));
    xhr.onerror = () => reject(new NetworkError("PUT 실패(네트워크 또는 CORS)"));
    xhr.ontimeout = () => reject(new NetworkError("PUT 타임아웃"));
    xhr.timeout = 100_000;
    xhr.send(blob);
  });
}
```

| 규칙 | 내용 |
|------|------|
| **`fetch`를 쓰지 않는 이유** | `fetch`는 **업로드 진행률을 제공하지 않는다**. 진행률이 규격(`analysis/13 §4.8`)이므로 XHR을 쓴다 |
| 인증 헤더 | **붙이지 않는다.** 서명 URL 자체가 권한이다 |
| 헤더 하드코딩 금지 | `requiredHeaders`를 **순회**해 부착한다. 하나라도 빠지면 서명 불일치 403 또는 다운로드 토큰 미설정으로 파일 GET 불가(M14) |
| preflight | 커스텀 헤더(`x-goog-meta-…`) 때문에 **OPTIONS preflight가 발생**한다 → 버킷 CORS에 해당 헤더가 허용돼야 한다([08 §5](./08-server-and-infra-prerequisites.md)) |
| 실패 시 | **commit을 호출하지 않는다** |
| 유효 시간 | 15분. 사용자가 QR 화면에서 오래 머물다 재시도하면 만료될 수 있다 → **재시도는 prepare부터 다시**(§4.4) |

### 4.3 ③ commit

```jsonc
{ "sessionId": "…", "finalImageUrl": "…", "timelapseUrl": null,
  "retentionHours": 24, "downloadPageUrl": "https://mcphoto-955fb.web.app/?s=…" }
```

| 규칙 | 내용 |
|------|------|
| 최소 1개 불변식(M7) | 둘 다 null이면 400. **클라이언트가 먼저 막는다**(업로드 자체를 시작하지 않음) |
| URL 소속 검증 | 서버가 `finalImageUrl`·`timelapseUrl`이 자기 버킷 + `results/{sessionId}/` 경로인지 검증한다 → **prepare가 준 `downloadUrl`을 그대로 넘겨야 한다** |
| `retentionHours` | 정수 1~72. **만료 시각은 서버가 계산**한다(클라 시각을 보내지 않는다) |
| `downloadPageUrl` | `{HostingBaseUrl}/?s={sessionId}` — **P1 사이트 도메인**(kiosk 도메인 아님, [03 §16](./03-screens-spec.md) 경고) |
| 409 | 동일 `sessionId` 재commit — **이중집계 차단 장치이므로 정상 동작**이다 |

### 4.4 재시도 정책

| 항목 | 규격 |
|------|------|
| 방식 | **새 세션 ID로 전 과정 재실행**(가장 단순하고 이중집계도 없다 — `analysis/13 §4.8` 권장) |
| 진행률 | 0에서 재시작 |
| 이미 만든 결과물 | 재사용한다(재합성하지 않는다). **OPFS의 `final`·`timelapse`를 그대로 다시 올린다** |
| 세션 폴더 | 새 세션 ID로 폴더를 다시 만들지 않는다 — **업로드용 세션 ID와 OPFS 폴더명이 달라질 수 있음을 허용**하고 로그에 두 값을 남긴다 |
| 자동 재시도 | 하지 않는다 |

### 4.5 진행률 계산

| 항목 | 규격 |
|------|------|
| 단계 라벨 | "사진 업로드 중" → "영상 업로드 중" → "마무리 중" |
| 전체 % | 파일 크기 가중 합산(`domain/upload/uploadOrchestration.ts`의 순수 함수로 계산) |
| 초기 상태 | 진행률 불확정(indeterminate) |
| 콜백 순서 | **순서를 가정하지 않는다.** 단계 라벨을 순서 의존적으로 단언하지 말 것(`analysis/14 §9` 주석 — Windows에서 테스트 flakiness 원인이었다) |

---

## 5. 세션 ID·URL 조립 (`analysis/31 §7`) — 한 글자도 달라선 안 된다

```ts
// domain/upload/uploadContract.ts (순수 — 시각·UUID는 주입)
newSessionId(now: Date, uuid: string): string
  // `${yyyyMMdd}_${HHmmss}_${uuid}` — 로컬 시간 기준
finalPath(sessionId, ext): string        // results/{sessionId}/final.{ext}
timelapsePath(sessionId): string         // results/{sessionId}/timelapse.mp4
tokenUrl(bucket, path, token): string    // https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media&token={token}
downloadPageUrl(hostingBaseUrl, sessionId): string   // {base(트레일링 / 제거)}/?s={sessionId}
computeExpiresAt(createdAt, retentionHours): Date     // 표시용(서버가 진실원)
```

| 주의 | 내용 |
|------|------|
| 시각 | **로컬 시간**으로 만든다(서버는 형식만 검증) |
| UUID | `crypto.randomUUID()` — 보안 컨텍스트 필요 |
| 경로 인코딩 | 슬래시까지 퍼센트 인코딩(`encodeURIComponent`) → `results%2F…%2Ffinal.jpg` |
| 검증 | 정규식 단위 테스트 + `docs/spec-vectors`의 공유 벡터로 Windows와 대조 |

---

## 6. 서버 프레임 이미지 로드 (WM2 — 놓치면 합성이 전면 실패한다)

프레임 이미지는 `firebasestorage.googleapis.com`(다른 오리진)에 있다. 이를 canvas에 그려 합성하면 **canvas가 오염(tainted)** 되어 `convertToBlob`/`getImageData`가 **SecurityError**를 던진다.

```ts
// 올바른 로드 (둘 중 하나)
const res = await fetch(imageUrl, { mode: "cors" });        // 버킷 CORS 필요
const bitmap = await createImageBitmap(await res.blob());

// 또는
const img = new Image();
img.crossOrigin = "anonymous";                              // 버킷 CORS 필요
img.src = imageUrl;
```

| 규칙 | 내용 |
|------|------|
| 선행 조건 | `firebasestorage.googleapis.com`은 **서비스 레벨 `ACAO: *`가 실측**되어(403 기준, `web/OPS-cors.md`) 버킷 CORS 없이도 될 가능성이 높다. 단 **200 응답 검증이 잔여**이며 버킷 CORS 구성의 GET이 안전망이다([08 §5](./08-server-and-infra-prerequisites.md)). **`crossOrigin="anonymous"` 지정 자체는 어떤 경우에도 생략 불가**(속성 없이 그리면 CORS 헤더가 있어도 canvas 오염) |
| 캐시 | 받은 Blob을 **OPFS `frames/`에 저장**한다 → 이후는 same-origin이라 오염 문제가 없고 오프라인에서도 동작 |
| CORS 실패 시 | 그 프레임을 **목록에서 제외하지 않고**, 썸네일은 `<img>`로 보여주되(오염은 canvas에만 영향) **합성 불가**로 판정해 선택 시 안내: *"이 프레임을 불러올 수 없습니다."* + 로그에 "프레임 이미지 CORS 실패" |
| 이미지 없는 문서 | 서버는 문서를 먼저 만들고 이미지 PUT은 나중이라 **이미지 없는 문서가 존재할 수 있다**(`analysis/31 §4.10`) → 로드 실패를 **크래시 없이** 처리한다 |

---

## 7. 오프라인·미도달 시 동작 (`analysis/13 §11`)

| 기능 | 백엔드 미도달 시 |
|------|------------------|
| 로그인 | **불가**(오프라인 폴백 없음 — 만들면 보안 회귀) |
| 게스트 촬영·합성·로컬 저장 | **정상 동작** |
| 공용 프레임 목록 | **로컬 캐시 → 번들 → fallback 폴백** |
| 업로드·QR | 실패 → 우아 처리(로컬 보존 안내 + 재시도) |
| PIN 게이트 | **fail-closed**(진입 거부) |
| 무료 한도 조회 | **fail-open**(허용, 서버가 업로드에서 최종 거부) |
| 사용자 목록 | 오류 표시(**빈 목록 폴백 금지**) |

---

## 8. 체크리스트

- [ ] 게이트 키가 모든 백엔드 호출에 부착되고 **서명 PUT에는 부착되지 않는다**
- [ ] Bearer가 **토큰이 있을 때만** 부착되고, 로그아웃 직후에는 **붙지 않는다**(M1 — E2E로 고정)
- [ ] 업로드는 **effective QR on일 때만** 시작된다(게스트·TempUser 초과에서 `/uploads/*` 요청 0건)
- [ ] 401/403/404/409/501/네트워크 실패가 **각각 다른 안내**로 구분된다
- [ ] 403의 `error.code`로 권한 부족과 TempUser 한도를 구분한다
- [ ] `requiredHeaders`를 **순회해 전부** 부착한다(M14)
- [ ] 업로드 진행률이 **XHR**로 측정된다(WM5)
- [ ] `sessionId` 형식이 정규식을 만족한다(M13)
- [ ] `downloadPageUrl`이 **P1 사이트 도메인**을 가리킨다
- [ ] commit에 prepare가 준 `downloadUrl`을 **그대로** 넘긴다
- [ ] 최소 1개 불변식을 클라이언트가 먼저 막는다(M7)
- [ ] `PUT /frames/{id}`를 **호출하지 않는다**
- [ ] 서버 프레임 이미지를 **CORS-clean**하게 로드하고 OPFS에 캐시한다(WM2)
- [ ] 자동 재시도가 없고, 재시도는 **새 세션 ID로 전 과정 재실행**이다
- [ ] 요청/응답 본문·토큰·서명 URL이 로그에 남지 않는다
