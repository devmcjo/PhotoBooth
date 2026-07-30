# 20 · 프론트엔드 — 모바일 다운로드 웹 페이지 분석

| 항목 | 내용 |
|------|------|
| 문서 | 모바일 다운로드 웹 페이지(QR 링크 진입 → 사진·타임랩스 미리보기/다운로드) 분석 |
| 범위 | `web/public/*`(index.html·app.js·styles.css·firebase-config.js), `web/firebase.json`·`web/.firebaserc`·`web/package.json`. 인프라/삭제는 [50 · GCP 수명주기·TTL](./50-infra-gcp-lifecycle-and-ttl.md), 백엔드 계약은 [30 · Firebase 연동](./30-backend-firebase-integration.md)·[40 · 스키마](./40-database-firestore-and-storage-schema.md) |
| 최종 업데이트 | 2026-07-30 (it17: 자동 저장·공유 버튼·토스트·파일명·MCPhoto 브랜딩) |
| 관련 소스 | `web/public/index.html`, `web/public/app.js`, `web/public/styles.css`, `web/public/firebase-config.js`, `web/firebase.json`, `web/package.json`, `web/OPS-cors.md`, `docs/design/web-architecture.md`·[`web-it17-download-share-design.md`](../design/web-it17-download-share-design.md)(근거) |
| 갱신 규칙 | `web/public/*` 또는 `web/firebase.json`의 상태 섹션·판정 로직·config·헤더가 바뀌면 해당 표/근거(`파일:라인`)를 갱신. 만료 판정 규칙 변경은 계약(`firebase-contract.md`)·40번 문서와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정.

> ℹ️ 이 문서는 **소비자 클라이언트(P1)의 유일한 완성 구현**을 기술한다. 같은 프로파일을 iOS·Android·macOS 네이티브로 만들 때의 플랫폼 중립 규격은 **[13 §12](./13-client-behavior-spec.md)** 에 정리돼 있다(상태 4종·만료 fail-safe 판정·"옵션 꺼짐" vs "로드 실패" 구분). 이 문서의 Firebase JS SDK·CSP·Hosting 헤더 부분은 웹 전용이다.
>
> 🆕 **웹을 P1 외로 확장하려면**(관리 콘솔·프레임 저작 등) **[05 §7.4 웹 클라이언트 제약 상세](./05-cross-platform-client-guide.md#74-웹-클라이언트-제약-상세-브라우저에서-어려운-것)** 를 먼저 읽어야 한다. 브라우저에서 **계약·불변식을 만족할 수 없는 항목**(촬영 P2의 타임랩스·결과물 로컬 보관, 개인 프레임 저장)이 있어 범위를 별도로 판정해야 한다. 이 페이지에 관리 콘솔을 얹을 경우 **CSP `connect-src`에 백엔드 함수 도메인 추가**가 필요하며, 별 경로(`/admin`) + 별 CSP로 분리하는 것이 권장된다.

---

## 1. 페이지 목적과 불변식

WPF 포토부스가 결과물을 업로드하고 QR 코드를 화면에 띄우면, 사용자가 휴대폰으로 QR을 스캔해 이 웹 페이지로 진입한다. 페이지는 QR에 인코딩된 토큰으로 세션 문서를 단건 조회하여 **사진·타임랩스를 미리보기·다운로드**하게 한다.

웹은 **읽기 전용 소비자**라는 점이 핵심 불변식이다(`web/public/app.js:1-9`).

| 불변식 | 내용 | 근거 |
|--------|------|------|
| 읽기 전용 | `resultSessions` 단건 조회(`getDoc`)만. 컬렉션 쿼리·열거(list) API 절대 미사용 | `app.js:4-5`, `app.js:533` |
| 컬렉션 격리 | `users` / `frameTemplates` 는 절대 읽지 않음 | `app.js:6` |
| 파일 직접 GET | 문서의 `finalImageUrl`/`timelapseUrl`(다운로드 토큰 URL)을 DOM 속성에 직접 바인딩. Storage SDK / Auth 는 import 하지 않음 | `app.js:7-8`, `app.js:452-457`, `app.js:490-495` |
| URL 형식 | 쿼리형 `/?s={token}` | `app.js:9`, `app.js:366` |
| 삭제 없음 | 웹은 삭제를 **수행하지 않는다**. 만료/부재를 판정해 안내만 한다 | `app.js:535-536`, [50번](./50-infra-gcp-lifecycle-and-ttl.md) §4 |

토큰 URL(`?alt=media&token=...`)은 그 자체가 capability 이며 Storage 보안 규칙을 우회하므로, `results/`의 SDK read 를 닫아도(=`false`) 웹 다운로드는 정상 동작한다([50번](./50-infra-gcp-lifecycle-and-ttl.md) §5, `web/storage.rules:16-19`).

---

## 2. 상태 머신 (loading / success / expired / error)

`index.html`에 4개의 상태 섹션이 있고(`state-loading`·`state-success`·`state-expired`·`state-error`), 한 번에 하나만 노출한다(`app.js:351-360`). **it17 은 상태를 추가하지 않았다 — `STATES` 배열과 4개 섹션은 불변이다.**

| 상태 | 섹션 id | 진입 조건 | 화면 | 근거 |
|------|---------|-----------|------|------|
| loading | `state-loading` | 초기 진입·재시도 시작 | 스피너 + "결과물을 불러오는 중…" | `index.html:29-32`, `app.js:530` |
| success | `state-success` | 문서 존재 & 미만료 | 사진/영상 미리보기·다운로드·만료 고지 | `index.html:35-84`, `app.js:519` |
| expired | `state-expired` | 문서 부재 / `expiresAt < now` / 파싱 실패 / 미디어 전부 로드 실패 폴백 | ⏳ "보관 기간이 지나 만료되었습니다" | `index.html:87-94`, `app.js:426`, `app.js:537-561` |
| error | `state-error` | 토큰 없음/형식 오류 / Firebase 초기화 실패 / 네트워크·권한 예외 | ⚠️ "일시적인 오류" + 다시 시도 버튼 | `index.html:97-102`, `app.js:568,586,593` |

- `showState(name)`은 `STATES = ["loading","success","expired","error"]`를 순회하며 `el.hidden = s !== name`으로 토글한다(`app.js:351-360`).
- `.state[hidden] { display:none }` CSS 가 `display:flex`를 눌러 실제로 감추도록 명시되어 있다(과거엔 성공+만료+오류가 동시 노출되던 버그, `styles.css:104-109`). 같은 이유로 it17 의 `.toast[hidden]`도 명시 처리했다(§7B).
- **재시도 대상은 error 상태뿐**이다. `#retry-btn`이 같은 토큰으로 `loadSession(token)`을 재실행한다(`app.js:598-599`). 만료/부재는 재시도 대상이 아니다.
- ⚠️ `#retry-btn` 때문에 `renderSuccess`가 **여러 번 실행될 수 있다.** 그래서 it17 의 다운로드·공유 클릭 리스너는 `renderSuccess`가 아니라 `init()`에서 1회만 배선한다(§7.1).

### 2.1 상태 전이 흐름

```
init()                          (app.js:573)
 ├─ Firebase 초기화 실패 ──────→ error   (app.js:584-587)
 ├─ parseToken() == null ──────→ error   (app.js:590-594)
 ├─ #share-btn 노출 + 리스너 1회 배선     (app.js:604-611)   ← it17
 ├─ #photo/#video-download 리스너 1회 배선 (app.js:614-623)  ← it17
 └─ loadSession(token)          (app.js:629)
     ├─ showState("loading")                       (app.js:530)
     ├─ getDoc(...)  예외 ─────→ error   (catch)    (app.js:565-569)
     ├─ !snap.exists() ────────→ expired            (app.js:537-539)
     ├─ expiresAt 부재/파싱실패 → expired (fail-safe) (app.js:554-557)
     ├─ expiresAt < now ───────→ expired            (app.js:559-561)
     └─ renderSuccess(data, token) → success        (app.js:564,519)
           └─ (present 미디어 전부 로드 실패) → expired 폴백 (app.js:423-427)
```

---

## 3. 토큰 파싱 (진입점)

- 쿼리스트링 `?s={token}`에서 토큰을 추출한다(`app.js:365-371`).
- 경량 검증만 수행한다: 빈 값이거나 200자 초과면 `null` 반환 → error(`app.js:369`, `app.js:590-594`).
- 엄격한 UUID 정규식은 강제하지 않는다 — Firestore not-found 가 무효 토큰을 걸러주기 때문(`app.js:363-364`, web-architecture.md §3.2).
- `module` 스크립트는 defer 되지만, `document.readyState`를 방어적으로 확인해 `init()`을 호출한다(`app.js:632-637`).
- it17 이후 토큰은 **파일명의 시각 prefix**(§7.3)와 **공유 URL 재조립**(§7A)에도 쓰인다. 전역 변수를 만들지 않고 `renderSuccess(data, token)` 인자와 `init()` 클로저로 전달한다(`app.js:393`, `app.js:564`).

---

## 4. Firestore `resultSessions/{token}` 읽기 흐름

`loadSession(token)`이 코어 로직이다(`app.js:529-570`).

1. `showState("loading")` (`app.js:530`).
2. `getDoc(doc(db, "resultSessions", token))` — **단건 조회만**. 컬렉션 쿼리·열거는 계약상 list deny(`app.js:532-533`, `web/firestore.rules:30-31`).
3. 문서 부재(`!snap.exists()`) → `expired`. 무효 토큰과 삭제 문서를 **구분하지 않는다**(토큰 존재 노출 방지, `app.js:535-539`).
4. 존재하면 `snap.data()`로 필드를 읽어 만료 판정(§5) 후 `renderSuccess(data, token)`(§6).
5. 네트워크/권한 예외는 `catch`에서 `error` 상태로(재시도 제공, `app.js:565-569`).

읽는 필드는 `finalImageUrl`, `timelapseUrl`, `expiresAt`(3개)뿐이다. 다운로드 페이지 URL·생성 시각 등 다른 필드는 웹에서 읽지 않는다(스키마는 [40번](./40-database-firestore-and-storage-schema.md) 참조).

---

## 5. 만료 판정 (문서 부재 / expiresAt 경과 / 파싱 실패 fail-safe)

만료 판정은 세 갈래이며, 모두 **안전측(만료로 처리)**으로 기운다(`app.js:543-561`). **it17 은 이 판정을 건드리지 않았다.**

| 판정 | 조건 | 결과 | 근거 |
|------|------|------|------|
| 문서 부재 | `!snap.exists()` | expired | `app.js:537-539` |
| 필드 부재/파싱 실패 | `expiresAt` 없음, `toDate` 아님, 또는 Invalid Date | expired (fail-safe) | `app.js:547-557` |
| 시각 경과 | `expiresAt < new Date()` | expired | `app.js:559-561` |
| 미만료 | 위 어느 것도 아님 | `renderSuccess` (success) | `app.js:564` |

- **fail-safe 설계**: `expiresAt`을 판정할 수 없으면 성공 대신 만료로 처리한다. 보관 기간이 초과된 콘텐츠를 잘못 노출하지 않기 위함(`app.js:543-546,554-557`). `expiresAt`은 Firestore `Timestamp` → `toDate()`로 `Date` 변환, `Number.isNaN(getTime())`로 유효성 검사(`app.js:548-553`).
- **retentionHours 반영**: WPF가 `expiresAt = createdAt + retentionHours`(1~72h)로 문서를 만든다(`src/MCPhoto.Core/Upload/UploadContract.cs:43-44`, `src/MCPhoto.Core/Settings/AppSettings.cs:62`). 웹은 이 `expiresAt`으로 **접근 만료(웹 차단)**를 정확히 반영한다. 물리 파일 삭제(age 3일 등)와는 별개다([50번](./50-infra-gcp-lifecycle-and-ttl.md) §2·§3).

### 5.1 미디어 로드 실패 폴백 (물리 삭제 후 문서만 남은 경우)

문서는 미만료(존재+`expiresAt` 미경과)인데 Storage 파일이 이미 지워진 고아 상태를 위한 2차 방어다(`app.js:415-427`).

- `mediaState.photo/video`가 `{present, loadOk}`를 추적한다(`app.js:410-413`).
- `present=true`(URL 있음)인 미디어가 `onerror`로 `loadOk=false`가 되면 실패로 센다(`app.js:443-451`, `app.js:480-488`).
- `maybeFallbackToExpired()`: **present 인 미디어가 하나라도 있고, present 인 것이 모두 로드 실패**면 `expired`로 폴백한다(`app.js:417-428`).
- 옵션 꺼짐(`present=false`)은 실패가 아니므로 폴백 트리거에서 제외된다(§6).
- `onerror`는 `href`를 제거하고 `.is-disabled`를 붙인다. **it17 의 다운로드 핸들러는 `href`가 없으면 즉시 반환하므로 이 상태에서 `fetch`가 발생하지 않는다**(`app.js:275`).

---

## 6. 미디어 옵션 부재 vs 실패 구분 (it7 F3)

`renderSuccess`는 만료 판정을 통과한 뒤에만 호출된다. 따라서 URL 이 falsy 하면 "만료/실패"가 아니라 **"전송 옵션이 꺼진 것"(의도적 제외)**으로 해석한다(`app.js:388-391`, it7 F3, 계약 §5).

| 상태 | present | 화면 처리 | 근거 |
|------|---------|-----------|------|
| URL 있음 | true | 프리뷰·다운로드 버튼 노출, 옵션꺼짐 안내 숨김. **힌트는 자동 저장 능력에 따라**(§7.2) | `app.js:431-458`(사진), `app.js:469-497`(영상) |
| URL null(옵션 꺼짐) | false | 프리뷰·다운로드·힌트·실패문구 숨기고, "전송 옵션이 꺼져 있어 제공되지 않습니다" 안내만 | `app.js:459-467`(사진), `app.js:498-506`(영상) |
| URL 있으나 로드 실패 | true, loadOk=false | 프리뷰 숨김, "불러올 수 없습니다(만료되었을 수 있습니다)" + 다운로드 비활성(`is-disabled`, href 제거) | `app.js:443-451`, `app.js:480-488` |

- 옵션꺼짐 안내(`.media__optout`)는 만료(빨강)·로드실패와 시각적으로 구분되는 **중립 정보 톤**(점선 테두리·회색)이다(`styles.css:198-207`, `index.html:54-56,78-80`).
- 영상 섹션은 옵션 꺼짐이어도 `#video-section`을 **숨기지 않고**(it7) 안내만 노출한다(`app.js:505`).
- 둘 다 옵션 꺼짐(계약상 미발생, 방어적)이면 성공 화면을 유지하며 안내 2개를 보여준다(`app.js:522`). WPF는 사진·타임랩스 중 최소 1개가 있어야 업로드하고 서버도 commit에서 같은 불변식을 강제하므로(`src/MCPhoto.Core/Upload/UploadService.cs:38-39`, `web/functions/src/services/uploads.ts:169-176`) 실제로는 발생하지 않는다.
- **다운로드 `<a>`의 `hidden`이 곧 present 표현이다.** it17 의 `revealManualHints()`가 이 값을 읽어 present 인 미디어의 힌트만 노출한다 → 옵션 꺼짐 미디어의 힌트는 절대 노출되지 않는다(`app.js:236-246`). 이 판정은 **DOM 프로퍼티**(`anchor.hidden`)를 읽으므로 아래 CSS 결함과 무관하게 정확하다.

### 6.1 ⚠️ 잠재 결함 — `hidden`이 작성자 `display`에 눌리는 요소들 (it17 범위 외, 미수정)

`hidden` 속성의 UA 스타일 `display:none`은 **작성자 스타일에 밀린다**. 그래서 `display`를 설정하는 클래스를 가진 요소는 `hidden`을 걸어도 감춰지지 않는다. 이 리포지토리는 이미 같은 원인의 버그를 `.state[hidden]`으로 고친 이력이 있다(§2, `styles.css:104-116`). it17 은 `#share-btn`에 같은 가드를 넣었다(§7A).

`web/public` 전수 점검 결과, **가드가 없는데 `display`를 설정하는 클래스를 가진** 요소는 다음과 같다.

| 요소 | display 설정 | `hidden`을 거는 시점 | 영향 |
|---|---|---|---|
| `#photo-preview`·`#video-preview` | `.media__preview{display:block}` | 프리뷰 로드 실패(`onerror`) | §5.1 의 "프리뷰 숨김"이 적용되지 않아 깨진 이미지 자리가 남을 수 있다 |
| `#photo-download`·`#video-download` | `.btn{display:inline-flex}` | 전송 옵션 꺼짐(URL null) | §6 표의 "다운로드 숨김"이 적용되지 않아 옵션 꺼짐 미디어에도 버튼이 보일 수 있다 |
| `#video-section` | `.media{display:flex}` | 정적 `hidden`(초기) | **영향 없음** — JS 가 두 분기 모두에서 `hidden=false`로 만들고, 그 전에는 `#state-success`가 숨겨져 있다 |
| `#retry-btn` | `.btn{display:inline-flex}` | — | **영향 없음** — JS 가 `hidden`을 설정하지 않고, 부모 `#state-error`가 가드된다 |

- **it17 은 이 요소들을 건드리지 않았다**(신규 결함이 아니다). `revealManualHints()`의 present 판정은 **DOM 프로퍼티** `anchor.hidden`을 읽으므로 이 CSS 결함과 무관하게 정확하다.
- 원인은 CSS 캐스케이드 규칙과 `.state[hidden]` 선례에서 **추론**한 것이며 **브라우저 관측으로 확인하지 않았다**(§12 U8).
- 수정은 `.btn[hidden]`·`.media__preview[hidden]`에 `display:none` 두 줄이면 충분하지만, 옵션 꺼짐·로드 실패 경로의 **표시 동작 변경**이라 it17 범위를 넘는다 → **별건으로 처리 권고**.

---

## 7. 만료 시각 표기와 다운로드 동작 (it17 개편)

- 성공 화면 하단 `#expiry-notice`에 "이 사진·영상은 {만료 시각}에 만료됩니다."를 표시한다(`index.html:83`, `app.js:511-514`).
- `formatExpiry`는 `Intl.DateTimeFormat("ko-KR", ...)`로 **사용자 로컬 시간**으로 포맷하며, 실패 시 `toLocaleString()` 폴백(`app.js:373-386`).

### 7.1 자동 저장 (fetch → Blob → `<a download>`)

**`<a download>`는 cross-origin(`firebasestorage.googleapis.com`)에서 전 브라우저가 무시한다**(MDN: same-origin + `blob:`/`data:` 전용). 이 사실은 it17 이후에도 유효하다 — 그래서 바이트를 `fetch`로 가져와 `blob:` URL을 만든 뒤 그 URL에 `download`를 걸어 프로그램적으로 클릭한다(`app.js:186-330`).

| 단계 | 동작 | 근거 |
|------|------|------|
| (A) 기능 감지 | `'download' in HTMLAnchorElement.prototype && typeof URL.createObjectURL === 'function' && typeof fetch === 'function'` → 모듈 상태 `autoDownloadEnabled` | `app.js:197-205` |
| (B) 클릭 개입 | `preventDefault()`(첫 `await` 앞 동기 구간) → busy(`aria-busy`·`.is-busy`·라벨 "저장 중…") → `fetch(url,{mode:'cors',credentials:'omit',signal})` | `app.js:271-296`, `app.js:213-234` |
| 용량 가드 | `Content-Length > 150MB`면 중단. **`.blob()` 앞**에서 검사해 메모리 적재 자체를 막는다. 헤더 부재 시 `NaN`→무동작(안전) | `app.js:36`, `app.js:302-303` |
| (C) 성공 | `blob()` → `createObjectURL` → 임시 `<a download>` click·remove → 토스트 "저장을 시작했습니다. 다운로드 목록을 확인해 주세요." → 수동 힌트 노출 → **60초 후 `revokeObjectURL`** | `app.js:253-269`, `app.js:305-313` |
| (D) 실패 = 전역 degrade | `console.warn` → `autoDownloadEnabled=false` → 두 미디어 수동 힌트 노출 → warn 토스트 → **`location.assign(url)`**(= it17 이전 동작) | `app.js:314-329`, `app.js:248-251` |
| 취소 | `AbortError`는 **무동작 반환**(토스트·폴백 없음) — 페이지 이탈 시의 정상 경로 | `app.js:316` |

- **최악의 경우가 it17 이전 동작이다.** CORS·네트워크·비2xx·용량초과 어느 것으로 실패해도 원본으로 내비게이션되고 롱프레스 힌트가 노출된다 → **회귀가 없다**.
- 실패는 **결정론적·전역적**이므로(CORS 미구성이면 첫 클릭부터 전부 실패) 첫 실패에서 능력을 내리고 **재시도하지 않는다**. 이후 클릭은 개입 없이 기본 내비게이션이다(`app.js:279`).
- **커스텀 요청 헤더를 붙이지 않는다.** GET + 안전 목록 헤더만 쓰면 simple request 가 되어 `OPTIONS` preflight 가 발생하지 않는다(`app.js:294`).
- `<img>`/`<video>`에 `crossorigin`을 **추가하지 않았다** — 프리뷰 로드를 CORS 의존으로 바꾸면 CORS 미구성 시 프리뷰가 통째로 깨지는 하드 회귀가 된다. 대가는 전송량 2배(프리뷰 + 저장 fetch)이며, 대상이 단위 MB라 수용한다.
- **재진입 차단**: 미디어별 `inflight` Map + `.btn.is-busy{pointer-events:none}` 이중 방어 → 연타에도 `fetch`는 1회(`app.js:208`, `app.js:281-285`, `styles.css:247-252`).
- **리스너는 `init()`에서 1회만 배선한다.** `renderSuccess`는 `#retry-btn`으로 재호출될 수 있어 거기서 배선하면 `fetch`가 중복 발생한다(`app.js:614-623`).

#### 해제 경로 (누수 방지)

| 리소스 | 해제 | 근거 |
|---|---|---|
| `AbortController` | `finally`에서 `inflight`에서 제거 + `pagehide`에서 남은 것 전부 `abort()` | `app.js:326-329`, `app.js:341-342` |
| `blob:` objectURL | `setTimeout(60s)` → `revokeObjectURL` (**유일한** 해제 경로). 즉시 revoke 하면 다운로드 시작 전에 blob 이 사라질 수 있다(특히 iOS) | `app.js:264-269` |
| revoke 예약 타이머 | Set 에 보관, `pagehide`에서 `clearTimeout` | `app.js:210`, `app.js:347-348` |
| 토스트 타이머 | 모듈 단일 변수, 재호출 시 항상 먼저 `clearTimeout`, `pagehide`에서도 clear | `app.js:50-53`, `app.js:335-338` |

> **`pagehide`에서 `revokeObjectURL`은 호출하지 않는다.** 문서가 파괴되면 blob store 도 함께 사라져 누수가 아니고, 반대로 진행 중 다운로드를 일부 엔진에서 중단시킬 수 있다(`app.js:344-346`).

### 7.2 수동 힌트의 노출 조건 변경

힌트 문구는 그대로다("저장이 안 되면 이미지를 길게 눌러(모바일)/우클릭(PC) 저장하세요.", `index.html:51-53,75-77`). **노출 조건이 바뀌었다.**

| | it17 이전 | it17 이후 |
|---|---|---|
| 조건 | URL 이 있으면 **상시 노출** | ① 기능 감지 실패 또는 ② **첫 저장 시도 이후**(성공/실패 무관) |
| 구현 | `hint.hidden = false` | `hint.hidden = autoDownloadEnabled` (렌더 시) + `revealManualHints()` (시도 후) |
| 근거 | — | `index.html:51,75`(초기 `hidden`), `app.js:437,474`, `app.js:236-246` |

- 성공 후에도 힌트를 노출하는 이유: 인앱 브라우저에서 기능 감지는 통과하지만 실제 저장이 안 되는 **조용한 실패**가 가능하다. 토스트도 "저장을 **시작**했습니다"로 단정하지 않는다.
- `revealManualHints()`는 **present 인 미디어만** 대상이다. present 여부는 `renderSuccess`가 이미 다운로드 `<a>`의 `hidden`으로 표현해 두었으므로 별도 모듈 상태를 만들지 않는다(`app.js:236-246`).

### 7.3 다운로드 파일명

it17 이전에는 `download="mcphoto.jpg"`/`"mcphoto.mp4"` 하드코딩이었고, **`outputFormat=png` 세션도 `.jpg`로 이름 붙는 버그**가 있었다. 이제 3단으로 확장자를 도출한다(`app.js:140-184`).

| 순위 | 소스 | 비고 |
|---|---|---|
| 1차 | 토큰 URL 경로의 실제 확장자(`results/{sid}/final.png` → `png`) | `jpg`/`jpeg`/`png`/`mp4` **화이트리스트** 통과분만 |
| 2차 | 응답 `Content-Type` | `image/png`·`image/jpeg`·`video/mp4` |
| 3차 | 미디어 종류 기본값 | 사진 `jpg` / 영상 `mp4` |

시각 prefix 는 **토큰 자체**에서 얻는다 — 토큰 = sessionId = `{yyyyMMdd}_{HHmmss}_{UUIDv4}`이므로 추가 조회가 없다(계약 §3.5). 토큰에 시각이 드러나는 것은 계약이 이미 수용한 트레이드오프다.

| 입력 | 파일명 |
|---|---|
| 사진, 경로 `final.jpg` | `MCPhoto_20260730_143022.jpg` |
| 사진, 경로 `final.png` | `MCPhoto_20260730_143022.png` |
| 영상 | `MCPhoto_20260730_143022_timelapse.mp4` |
| 토큰이 계약 형식과 다름(방어) | `MCPhoto.jpg` / `MCPhoto_timelapse.mp4` |

- **보안**: 파일명에 도달하는 값은 ① 정규식으로 캡처한 숫자 8+6자리와 ② 화이트리스트 확장자뿐이다. 경로 구분자·제어문자·`..`가 들어갈 경로가 없고, **토큰 원문(UUID)은 넣지 않는다**(`app.js:145-146`).
- `index.html`의 정적 `download` 속성도 `MCPhoto.jpg`/`MCPhoto_timelapse.mp4`로 정정했다 — JS 미실행 시의 하한값(`index.html:48,72`).

---

## 7A. 공유 버튼 (링크 복사)

헤더 우측에 "공유" 버튼을 두어 **현재 페이지 링크를 클립보드에 복사**하고 토스트로 알린다(`index.html:15-25`, `app.js:68-138`).

| 항목 | 결정 | 근거 |
|---|---|---|
| 동작 | 링크 복사 + 토스트. **Web Share API(공유 시트)는 쓰지 않는다** — 대상 앱을 한 번 더 골라야 한다 | `app.js:63-64` |
| 복사할 URL | `location.href`가 아니라 **토큰으로 재조립한 canonical URL**(`{origin}{pathname}?s={token}`). 유입 시 붙은 `utm_*`·`fbclid` 등을 제거한다 | `app.js:71-84` |
| 노출 조건 | **유효 토큰이 파싱된 경우**에만(`?s=` 없음/형식 오류로 error 인 경우 숨김). loading·success·**expired 에서도 노출** | `app.js:604-611` |
| 접근성 | 가시 라벨이 "공유"이므로 접근성 라벨 속성을 덮어쓰지 않는다(WCAG 2.5.3 Label in Name). 보조 설명은 `title="링크 복사"`. 인라인 SVG 는 `aria-hidden="true" focusable="false"` | `index.html:15-24` |

**복사 3단 폴백** — 어떤 경로에서도 "아무 일도 일어나지 않음"은 없다(`app.js:86-138`).

1. `navigator.clipboard.writeText` (secure context 필요. Hosting 은 HTTPS, `localhost`도 secure context)
2. 임시 `<textarea>` + `select()` + `document.execCommand('copy')` — deprecated 지만 구형·인앱 브라우저의 유일한 경로. `position:fixed;top:-1000px;opacity:0` + `aria-hidden`(`display:none`은 선택 불가라 **금지**), `finally`에서 `remove()`
3. warn 토스트 "링크 복사를 지원하지 않는 브라우저입니다. 주소창의 URL을 복사해 주세요." — canonical URL 은 정상 진입 시 주소창 URL 과 같으므로 실행 가능한 지시가 된다

> **만료 상태에서도 공유 버튼이 노출되는 것은 의도된 수용**이다. 상태 머신과 결합도를 만들지 않기로 결정했다(설계 §4.5 / R4).

> ⚠️ **`#share-btn[hidden] { display:none }` 가 반드시 필요하다.** `.btn`의 `display:inline-flex`(작성자 스타일)가 `hidden` 속성의 UA `display:none`을 눌러 무시하기 때문이다 — `.state[hidden]`(§2)과 **동일 원인**이다. 이 규칙이 없으면 `?s=` 없이 진입해도 공유 버튼이 노출된다(`styles.css:79-90`).

**헤더 레이아웃**: `text-align:center`를 유지하면서 우측에 버튼을 놓기 위해 **3열 그리드**(`1fr auto 1fr`)를 쓴다. 1열과 3열이 같은 `1fr`이므로 버튼 폭과 무관하게 제목이 정확히 중앙에 남는다(flex `space-between`은 제목이 좌측으로 밀린다). `.app__title`의 `text-align:center`는 그리드 미지원 폴백으로 남겨 둔다(`styles.css:61-83`).

`.btn`의 `width:100%`와 데스크톱 `@media`의 `.btn{min-width:220px}`가 헤더 버튼을 늘리지 않도록 `.btn--icon`에서 덮어쓴다(같은 특이도이므로 **뒤에 배치**해야 한다, `styles.css:255-272`, `styles.css:329-333`).

---

## 7B. 토스트 (일시 알림)

기존 `.media__error`·`.media__optout`·`.notice`는 전이·자동 숨김·live region 이 없는 정적 텍스트라 재사용할 수 없어 최소 구현을 신설했다(`app.js:40-66`, `styles.css:284-321`).

| 규약 | 내용 | 근거 |
|---|---|---|
| live region 은 **미리 존재** | `#toast`가 `<body>` 직속 마지막 자식으로 로드 시점부터 DOM 에 있다. **동적 생성 금지** — 삽입과 동시에 텍스트를 넣으면 다수 스크린리더가 읽지 않는다 | `index.html:111` |
| `.app` 밖에 배치 | transform 조상 영향 차단 | `index.html:109-111` |
| `textContent` 전용 | HTML 문자열 주입 경로를 두지 않는다 | `app.js:42,54` |
| `role="status"` + `aria-live="polite"` | 조작을 방해하지 않고 알린다(`assertive` 아님) | `index.html:111` |
| 타이머 단일화 | 가시(2600ms)→페이드(200ms) 중첩 타이머 id 를 **같은 변수**에 담아, 재호출 시 단일 `clearTimeout`이 어느 단계든 취소한다 → 타이머 누적·유령 토스트 없음 | `app.js:43,50-66` |
| 스타일 | `position:fixed`, `bottom: calc(24px + env(safe-area-inset-bottom))`(iOS 홈 인디케이터 회피), `pointer-events:none`(하단 버튼 조작 안 가림), 어두운 반투명 배경 + 흰 글자라 **다크 모드 오버라이드 불요**, `prefers-reduced-motion`에서 transition 제거 | `styles.css:284-321` |
| `.toast[hidden]{display:none}` | `.state[hidden]` 선례와 동일한 명시 처리(`position:fixed`가 `hidden`을 눌러 무시되는 것 방지) | `styles.css:303-305` |

문구 3종: `저장을 시작했습니다. 다운로드 목록을 확인해 주세요.` / `링크가 복사되었습니다.` / warn 2종(자동 저장 폴백·복사 미지원).

> 성공 문구의 "다운로드 목록" 안내는 **플랫폼 중립 표현**이다(iOS=Files 앱 > 다운로드, Android=`Download/`, 데스크톱=다운로드 폴더). iOS 에서 저장 위치를 찾지 못하는 문제를 완화하기 위해 덧붙였고, **UA 스니핑으로 분기하지 않는다** — 인앱 브라우저에서 오판정하고 유지보수 부채가 된다. "저장되었습니다"로 **단정하지 않는** 원칙은 유지한다(브라우저 확인 시트가 뜰 수 있다).

---

## 7C. 버킷 CORS — **설정 불필요** (2026-07-30 실측 판정)

자동 저장은 브라우저가 `firebasestorage.googleapis.com`의 `alt=media` 응답을 **cors 모드 fetch 로 읽을 수 있어야** 성립한다. 이는 `<img src>`(no-cors 서브리소스)와 **다른 조건**이다.

**판정: 버킷 CORS 설정은 필요하지 않다.** 다운로드 URL 의 호스트는 **Firebase Storage 서비스 프론트엔드**이고, 이 호스트는 **버킷 CORS 구성과 무관하게 `Access-Control-Allow-Origin: *`를 항상 반환**한다.

**실측 근거** (무인증 read-only HTTP 프로브. 운영 문서: [`web/OPS-cors.md`](../../web/OPS-cors.md))

| # | 요청 | 결과 |
|---|---|---|
| P1 | `firebasestorage.googleapis.com/v0/b/{bucket}/o/…?alt=media` + `Origin: https://mcphoto-955fb.web.app` | `403` + **`ACAO: *`** + `Access-Control-Expose-Headers: …, Content-Length, Content-Range, …` |
| P2 | 동일, `Origin: http://localhost:5000`(Emulator 오리진) | `403` + **`ACAO: *`** |
| P3 | **대조군** `storage.googleapis.com/{bucket}/…` + 동일 `Origin` | `403`, **`Access-Control-*` 헤더 전무** → **버킷 레벨 CORS 미설정** |

P3 가 미설정인데도 P1/P2 가 `ACAO: *`를 준다 → **두 레이어가 별개**임이 실측으로 확인된다.

**다운로드 URL 은 항상 P1 의 호스트다**: `web/functions/src/domain/session.ts:73`이 `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media&token=` 형식으로 조립하고, `web/functions/src/services/uploads.ts:135`의 `assertUrlBelongsToSession`이 그 prefix 를 강제 검증한다. `storage.googleapis.com` V4 서명 URL 은 **업로드 PUT 전용**이라 이 경로와 무관하다.

**부수 확인**: `Access-Control-Expose-Headers`에 **`Content-Length` 포함**(P1) → §7.1 의 용량 가드가 값을 읽을 수 있다.

> ⚠️ **잔여 불확실성 — 403 에서 관측, 200 확인은 스모크 잔여.** 위 관측은 전부 `403 Forbidden`(유효 토큰 없음)에서 얻었다. **200 응답(실제 바이트)에 같은 헤더가 붙는지는 확인되지 않았다**(§12 U1). 실토큰 + 실배포 devtools 로 확정한다. **"확인 완료"가 아니다.**

**결정**: `web/cors.json`을 **두지 않는다** — 적용할 필요가 없는 설정의 구성 파일은 오해를 만든다. 200 경로가 뒤집혔을 때의 컨틴전시 절차(GET 규칙 JSON·`origin:["*"]` 근거 포함)와 B5(PUT) 착수 시의 **전체 교체 경고**는 [`web/OPS-cors.md`](../../web/OPS-cors.md)에 있다. `gcloud`/`gsutil`은 이 작업 환경에 설치돼 있지 않아 버킷 구성 조회를 수행하지 않았고, 위 HTTP 실측이 그 자리를 대신한다.

> **폴백 경로는 그대로 유지한다.** CORS 가 해소됐어도 §7.1 (D) 전역 degrade 를 제거하지 않는다 — 인앱 브라우저의 `download` 미동작·구형 엔진·네트워크 실패·비2xx(토큰 만료·TTL 삭제)·용량 초과·사용자 활성화 만료가 여전히 남아 있고, 제거하면 이 경로들이 **조용한 실패**가 된다.

**CSP 변경 불요**: `connect-src`에 이미 `https://firebasestorage.googleapis.com`이 있다(`web/firebase.json:12`, §8.1).

**보안 규칙 영향 없음**: CORS 는 Storage 보안 규칙과 독립된 레이어다. `web/storage.rules`는 변경하지 않았고 `results/`의 SDK read 는 계속 `false`다([50번](./50-infra-gcp-lifecycle-and-ttl.md) §5.2).

---

## 7D. 플랫폼 동작 매트릭스

CORS 는 §7C 실측으로 해소됐으므로 아래는 그 전제에서의 기대 동작이다. **"검증" 열은 실제 관측만 기록한다 — 추정을 사실로 적지 않는다.**

| 플랫폼 | 자동 저장 | 저장 위치 | 조작 수 | 검증 |
|---|:-:|---|:-:|---|
| Android Chrome (QR 스캔 기본 동선) | ○(기대) | `Download/` + 다운로드 알림 | 1탭 | **미검증** — 갤러리(MediaStore) 인덱싱이 즉시가 아닐 수 있어 "사진 앱에 없다"고 느낄 여지가 있다 |
| iOS Safari 13+ | ○(기대) | **Files 앱 > 다운로드** | 1탭 + iOS 다운로드 확인 시트 | **미검증** |
| iOS Safari < 13 | ✗ | — | 폴백 | `download` 미지원 → 기능 감지에서 걸러진다 |
| 데스크톱 Chrome/Edge/Firefox/Safari | ○(기대) | 기본 다운로드 폴더 | 1클릭 | **미검증** |
| 인앱 브라우저(카카오톡·인스타그램·네이버 앱) | **불확실** | — | 폴백 | **미검증** — 감지를 통과하고도 저장이 안 될 수 있다. 폴백 + 수동 힌트가 최종 안전망 |

> **iOS 에서 사진 앱(카메라 롤) 저장은 브라우저로 불가능하다.** `<a download>`는 파일 시스템(Files 앱)까지만 도달한다. 카메라 롤에 넣는 유일한 웹 경로는 `navigator.share({files})`로 공유 시트를 띄워 사용자가 "이미지 저장"을 고르는 것이며, **사용자 추가 선택이 필수**라 "버튼 클릭 시 자동"이라는 요구를 만족하지 못한다. **"iOS 에서도 사진 앱에 자동 저장된다"고 사용자에게 말해서는 안 된다.**
>
> **2026-07-30 사용자 결정**: 이 제약을 **인지하고 수락**했다 → 전 플랫폼 동일하게 자동 저장(`<a download>`)을 쓰고, **공유 시트 경로(`navigator.share({files}))`는 구현하지 않는다**(이연 유지). 대신 성공 토스트에 "다운로드 목록을 확인해 주세요."를 넣어 저장 위치를 안내한다(§7B).

> **인앱 브라우저를 무시할 수 없는 이유**: 카메라 QR 스캔은 Safari/Chrome 을 열지만, it17 이 추가한 공유 버튼으로 링크가 카카오톡에 재공유되면 수신자는 인앱 브라우저에서 페이지를 연다. 그래서 수동 힌트를 삭제하지 않고 "첫 시도 후 노출"로 유지한다.

---

## 8. Firebase 웹 SDK 설정

| 항목 | 값/위치 | 근거 |
|------|---------|------|
| config 파일 | `web/public/firebase-config.js` (배포 환경별로 이 파일만 교체) | `firebase-config.js:1-4` |
| projectId | `mcphoto-955fb` | `firebase-config.js:8` |
| storageBucket | `mcphoto-955fb.firebasestorage.app` | `firebase-config.js:9` |
| authDomain | `mcphoto-955fb.firebaseapp.com` | `firebase-config.js:7` |
| apiKey | 공개값(방어는 보안 규칙) | `firebase-config.js:2,6` |
| SDK 로드 | gstatic CDN 모듈러 import, **버전 고정 pin `v12.16.0`**(latest/무버전 금지, WR6) | `app.js:11-18` |
| import 범위 | `firebase-app`(initializeApp) + `firebase-firestore`(getFirestore·doc·getDoc·connectFirestoreEmulator)만. Storage SDK·Auth 미import | `app.js:12-18` |

> it17 은 **import 를 추가하지 않았다.** 자동 저장·공유·토스트는 전부 순수 브라우저 API(`fetch`·`URL.createObjectURL`·`navigator.clipboard`·DOM)로 구현했다 — 새 런타임 의존성 0, 새 npm 패키지 0, 빌드 스텝 0.

- 웹 config 의 `apiKey`는 공개되어도 무방하다 — 유일한 방어선은 Firestore/Storage 보안 규칙이다(`firebase-config.js:2`, PRD §10, `web/firestore.rules:5`).
- 실값 확정 시점: 배포 시점(OA-1). 콘솔 > 프로젝트 설정 > 웹 앱 SDK 설정에서 복사(`firebase-config.js:3-4`).

### 8.1 Hosting 헤더 (firebase.json)

`web/firebase.json`의 `hosting`이 SPA rewrite 와 보안 헤더를 정의한다.

| 헤더/설정 | 값 | 근거 |
|-----------|-----|------|
| public | `public` | `firebase.json:3` |
| rewrites | `** → /index.html`(SPA) | `firebase.json:5` |
| CSP | `script-src 'self' https://www.gstatic.com`; `img-src`·`media-src`에 `https://firebasestorage.googleapis.com`; `connect-src`에 firestore/googleapis; `object-src 'none'`; `frame-ancestors 'none'` | `firebase.json:11-13` |
| X-Content-Type-Options | `nosniff` | `firebase.json:14` |
| Cache-Control | `/index.html`=no-cache; `*.js\|css`=public,max-age=3600 | `firebase.json:18-24` |

CSP 는 §8의 실제 import·미디어 소스와 정확히 일치한다(gstatic 스크립트 + firebasestorage 이미지/영상). 이 정합성이 깨지면 SDK 로드·미디어 표시가 CSP 로 차단된다.

**it17 은 CSP 를 변경하지 않았다**(`web/firebase.json` 불변).

| it17 이 필요로 하는 동작 | 디렉티브 | 판정 |
|---|---|---|
| `fetch(firebasestorage…)` | `connect-src` | **이미 허용** — `https://firebasestorage.googleapis.com`이 들어 있다(`firebase.json:12`) |
| `<a href="blob:…" download>` 클릭 | 해당 fetch 디렉티브 없음 | 변경 불요. `blob:`을 프리뷰 소스로 쓰지 않으므로 `img-src`/`media-src`에 `blob:`을 넣을 이유가 없다 |
| 인라인 SVG 아이콘·클립보드 API | — | CSP 무관 |

> 만약 실배포 콘솔에 CSP 위반이 보고되면, 메시지의 `effective-directive` 값을 읽어 **그 디렉티브에만** `blob:`을 추가한다(추측으로 여러 디렉티브를 열지 않는다). **2026-07-30 기준 실브라우저 관측 미수행 — 미검증 항목**(§12).

---

## 9. 로컬 검증 방법 (Emulator)

`app.js`는 호스트가 `localhost`/`127.0.0.1`/`0.0.0.0`일 때만 Firestore Emulator(`127.0.0.1:8080`)에 연결한다. 실배포 도메인(`*.web.app` 등)에서는 절대 트리거되지 않는다(프로덕션 안전, `app.js:21-24,578-582`).

| npm 스크립트 | 명령 | 용도 | 근거 |
|--------------|------|------|------|
| `serve` | `firebase emulators:start --only hosting` | 호스팅만 로컬 서빙 | `web/package.json:8` |
| `emulators` | `firebase emulators:start --only firestore,storage,hosting` | Firestore+Storage+Hosting 통합 | `web/package.json:9` |
| `test:rules` | `firebase emulators:exec --only firestore,storage "node tests/rules.test.js"` | 보안 규칙 Emulator 테스트(get allow/list deny 등) | `web/package.json:10`, web-architecture.md §6.3 |

- devDependencies: `firebase ^12.16.0`, `@firebase/rules-unit-testing ^5.0.1`(`web/package.json:12-15`). SDK 버전은 런타임 CDN pin(v12.16.0)과 일치한다.
- 규칙 테스트 시나리오: `resultSessions` get allow / list deny / write deny, `users`·`frameTemplates` 전면 deny(`web/firestore.rules:16-38`, web-architecture.md §6.3).

### 9.1 자동 게이트의 한계

**`web/public`에는 JS 린터·번들러·단위 테스트 하네스가 없다.** 자동 게이트는 다음 둘뿐이다.

| 게이트 | 명령 | 범위 |
|---|---|---|
| ESM 구문 | `node --check web/public/app.js` | 구문 오류만 |
| 보안 규칙 | `npm run test:rules` (cwd=`web`) | Firestore/Storage 규칙 (7 시나리오). **페이지 로직은 검사하지 않는다** |

따라서 상태 전이·해제 경로·폴백 같은 **페이지 동작은 브라우저 관측으로 검증해야 한다**(§12). it17 검증 시에는 `app.js`의 신규 블록을 스텁 DOM 위에서 실행하는 일회성 하네스로 상태 전이·해제 경로·3단 폴백을 확인했으나, **이 하네스는 리포지토리에 커밋하지 않았다**(도구 도입 결정 필요 — 이연 항목).

---

## 10. 스타일·접근성 요약 (styles.css)

- 모바일 우선 반응형: 기본 모바일 폭, `@media (min-width:768px)`로 데스크톱 확장(`styles.css:1-2,323-346`).
- 다크 모드: `prefers-color-scheme: dark` 팔레트 오버라이드(`styles.css:17-27`). **토스트는 토큰에 의존하지 않는 어두운 반투명 배경이라 오버라이드가 없다**(§7B).
- 터치 타깃 하한 48px(`--touch`), 가로 스크롤 금지(`overflow-x:hidden`), `prefers-reduced-motion`에서 스피너 감속·**토스트 transition 제거**(`styles.css:14,37,149-153,316-320`).
- `aria-live="polite"`(로딩 + **토스트**), `role="status"`(스피너 + **토스트**), `aria-disabled`/`is-disabled`(로드 실패 다운로드 비활성), **`aria-busy`/`is-busy`(저장 진행 중)**로 접근성 보강(`index.html:29,111`, `app.js:447,225-227`).
- 헤더 3열 그리드로 제목 중앙 유지 + 우측 공유 버튼(§7A). 공유 버튼은 `--touch` 하한을 데스크톱 미디어 쿼리 안에서도 유지한다(`styles.css:342-345`).

---

## 12. 미검증 항목 (2026-07-30 시점)

> 아래는 **실브라우저·실기기 관측이 필요한데 아직 수행되지 않은** 항목이다. 추정으로 채우지 않는다.

| # | 항목 | 확정 방법 |
|---|---|---|
| U1 | 유효 토큰 **200 응답**에 `Access-Control-Allow-Origin`이 붙는가(§7C) | 실배포 + 유효 토큰으로 devtools Network 확인 |
| U2 | 현행 CSP 에서 `blob:` `<a download>` 클릭이 위반 없이 동작하는가 | 브라우저 콘솔 CSP 위반 0건 관측 |
| U3 | `await` 이후 `a.click()`이 사용자 활성화 만료로 차단되지 않는가 | 실기기 관측(차단되면 폴백으로 degrade — 조용한 실패는 힌트로 완화) |
| U4 | iOS Safari 다운로드 확인 시트 → Files 앱 저장, **사진 앱 미저장** | iOS 실기기 |
| U5 | Android Chrome 갤러리(MediaStore) 인덱싱 시점 | Android 실기기 |
| U6 | 인앱 브라우저(카카오톡 등) 동작 — 저장 성공/실패 무관, **폴백과 힌트가 동작하면 PASS** | 실기기 |
| U7 | 320px 폭·다크 모드·키보드 전용 조작의 육안 확인 | devtools + 실기기 |
| U8 | `hidden`이 눌리는 4개 요소가 실제로 보이는가(§6.1 잠재 결함) | `timelapseUrl=null` 세션 + 깨진 URL 세션으로 devtools computed style 의 `display` 확인 |

---

## 11. 상호 참조

- 업로드가 만드는 `resultSessions` 문서·`results/` Storage 경로·토큰 URL 조립: [30 · 백엔드 Firebase 연동](./30-backend-firebase-integration.md), [40 · DB/Storage 스키마](./40-database-firestore-and-storage-schema.md).
- 만료 문서·파일의 물리 삭제(웹은 미수행): [50 · GCP 수명주기·TTL](./50-infra-gcp-lifecycle-and-ttl.md).
- 보안 규칙(get/list 분리, results SDK read false): `web/firestore.rules`, `web/storage.rules` ([50번](./50-infra-gcp-lifecycle-and-ttl.md) §5).
- 버킷 CORS **불필요 판정**의 근거와 컨틴전시 절차(§7C): [`web/OPS-cors.md`](../../web/OPS-cors.md). **구성 파일(`web/cors.json`)은 두지 않는다.**
- it17 설계 근거(옵션 비교·리스크·이연 항목): [`web-it17-download-share-design.md`](../design/web-it17-download-share-design.md).

> **계약은 바뀌지 않았다.** it17 은 URL 형식·토큰 규칙·`resultSessions` 스키마를 하나도 건드리지 않았으므로 `docs/design/firebase-contract.md`·[31](./31-backend-api-reference.md)·[14](./14-media-pipeline-spec.md)는 변경 대상이 아니다. 자동 저장은 **웹 전용 기법**(blob + `<a download>`)이므로 [13 §12](./13-client-behavior-spec.md)의 플랫폼 중립 규격에도 넣지 않는다 — 네이티브 클라이언트는 각 플랫폼의 저장 API 를 쓴다.
