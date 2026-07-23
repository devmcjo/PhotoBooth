# 20 · 프론트엔드 — 모바일 다운로드 웹 페이지 분석

| 항목 | 내용 |
|------|------|
| 문서 | 모바일 다운로드 웹 페이지(QR 링크 진입 → 사진·타임랩스 미리보기/다운로드) 분석 |
| 범위 | `web/public/*`(index.html·app.js·styles.css·firebase-config.js), `web/firebase.json`·`web/.firebaserc`·`web/package.json`. 인프라/삭제는 [50 · GCP 수명주기·TTL](./50-infra-gcp-lifecycle-and-ttl.md), 백엔드 계약은 [30 · Firebase 연동](./30-backend-firebase-integration.md)·[40 · 스키마](./40-database-firestore-and-storage-schema.md) |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 | `web/public/index.html`, `web/public/app.js`, `web/public/styles.css`, `web/public/firebase-config.js`, `web/firebase.json`, `web/package.json`, `docs/design/web-architecture.md`(근거) |
| 갱신 규칙 | `web/public/*` 또는 `web/firebase.json`의 상태 섹션·판정 로직·config·헤더가 바뀌면 해당 표/근거(`파일:라인`)를 갱신. 만료 판정 규칙 변경은 계약(`firebase-contract.md`)·40번 문서와 동시 갱신 |

> 표기 규칙: 근거는 `파일:라인`. **가정**으로 표시한 항목은 소스에서 직접 확인되지 않은 추정.

---

## 1. 페이지 목적과 불변식

WPF 포토부스가 결과물을 업로드하고 QR 코드를 화면에 띄우면, 사용자가 휴대폰으로 QR을 스캔해 이 웹 페이지로 진입한다. 페이지는 QR에 인코딩된 토큰으로 세션 문서를 단건 조회하여 **사진·타임랩스를 미리보기·다운로드**하게 한다.

웹은 **읽기 전용 소비자**라는 점이 핵심 불변식이다(`web/public/app.js:1-9`).

| 불변식 | 내용 | 근거 |
|--------|------|------|
| 읽기 전용 | `resultSessions` 단건 조회(`getDoc`)만. 컬렉션 쿼리·열거(list) API 절대 미사용 | `app.js:4-5`, `app.js:195` |
| 컬렉션 격리 | `users` / `frameTemplates` 는 절대 읽지 않음 | `app.js:6` |
| 파일 직접 GET | 문서의 `finalImageUrl`/`timelapseUrl`(다운로드 토큰 URL)을 DOM 속성에 직접 바인딩. Storage SDK / Auth 는 import 하지 않음 | `app.js:7-8`, `app.js:124-125`, `app.js:157-159` |
| URL 형식 | 쿼리형 `/?s={token}` | `app.js:9`, `app.js:41` |
| 삭제 없음 | 웹은 삭제를 **수행하지 않는다**. 만료/부재를 판정해 안내만 한다 | `app.js:197-198`, [50번](./50-infra-gcp-lifecycle-and-ttl.md) §4 |

토큰 URL(`?alt=media&token=...`)은 그 자체가 capability 이며 Storage 보안 규칙을 우회하므로, `results/`의 SDK read 를 닫아도(=`false`) 웹 다운로드는 정상 동작한다([50번](./50-infra-gcp-lifecycle-and-ttl.md) §5, `web/storage.rules:16-19`).

---

## 2. 상태 머신 (loading / success / expired / error)

`index.html`에 4개의 상태 섹션이 있고(`state-loading`·`state-success`·`state-expired`·`state-error`), 한 번에 하나만 노출한다(`app.js:28-35`).

| 상태 | 섹션 id | 진입 조건 | 화면 | 근거 |
|------|---------|-----------|------|------|
| loading | `state-loading` | 초기 진입·재시도 시작 | 스피너 + "결과물을 불러오는 중…" | `index.html:18-21`, `app.js:192` |
| success | `state-success` | 문서 존재 & 미만료 | 사진/영상 미리보기·다운로드·만료 고지 | `index.html:24-73`, `app.js:181` |
| expired | `state-expired` | 문서 부재 / `expiresAt < now` / 파싱 실패 / 미디어 전부 로드 실패 폴백 | ⏳ "보관 기간이 지나 만료되었습니다" | `index.html:76-83`, `app.js:100`, `app.js:199-224` |
| error | `state-error` | 토큰 없음/형식 오류 / Firebase 초기화 실패 / 네트워크·권한 예외 | ⚠️ "일시적인 오류" + 다시 시도 버튼 | `index.html:86-91`, `app.js:230,248,255` |

- `showState(name)`은 `STATES = ["loading","success","expired","error"]`를 순회하며 `el.hidden = s !== name`으로 토글한다(`app.js:28-35`).
- `.state[hidden] { display:none }` CSS 가 `display:flex`를 눌러 실제로 감추도록 명시되어 있다(과거엔 성공+만료+오류가 동시 노출되던 버그, `styles.css:91-96`).
- **재시도 대상은 error 상태뿐**이다. `#retry-btn`이 같은 토큰으로 `loadSession(token)`을 재실행한다(`app.js:260-261`, `app.js:90`). 만료/부재는 재시도 대상이 아니다.

### 2.1 상태 전이 흐름

```
init()                          (app.js:235)
 ├─ Firebase 초기화 실패 ──────→ error   (app.js:246-250)
 ├─ parseToken() == null ──────→ error   (app.js:252-257)
 └─ loadSession(token)          (app.js:263)
     ├─ showState("loading")                       (app.js:192)
     ├─ getDoc(...)  예외 ─────→ error   (catch)    (app.js:227-231)
     ├─ !snap.exists() ────────→ expired            (app.js:199-202)
     ├─ expiresAt 부재/파싱실패 → expired (fail-safe) (app.js:216-220)
     ├─ expiresAt < now ───────→ expired            (app.js:221-224)
     └─ renderSuccess(data) ───→ success            (app.js:226,181)
           └─ (present 미디어 전부 로드 실패) → expired 폴백 (app.js:99-101)
```

---

## 3. 토큰 파싱 (진입점)

- 쿼리스트링 `?s={token}`에서 토큰을 추출한다(`app.js:40-46`).
- 경량 검증만 수행한다: 빈 값이거나 200자 초과면 `null` 반환 → error(`app.js:44`, `app.js:252-256`).
- 엄격한 UUID 정규식은 강제하지 않는다 — Firestore not-found 가 무효 토큰을 걸러주기 때문(`app.js:38-39`, web-architecture.md §3.2).
- `module` 스크립트는 defer 되지만, `document.readyState`를 방어적으로 확인해 `init()`을 호출한다(`app.js:266-271`).

---

## 4. Firestore `resultSessions/{token}` 읽기 흐름

`loadSession(token)`이 코어 로직이다(`app.js:191-232`).

1. `showState("loading")` (`app.js:192`).
2. `getDoc(doc(db, "resultSessions", token))` — **단건 조회만**. 컬렉션 쿼리·열거는 계약상 list deny(`app.js:194-195`, `web/firestore.rules:30-31`).
3. 문서 부재(`!snap.exists()`) → `expired`. 무효 토큰과 삭제 문서를 **구분하지 않는다**(토큰 존재 노출 방지, `app.js:197-202`).
4. 존재하면 `snap.data()`로 필드를 읽어 만료 판정(§5) 후 `renderSuccess(data)`(§6).
5. 네트워크/권한 예외는 `catch`에서 `error` 상태로(재시도 제공, `app.js:227-231`).

읽는 필드는 `finalImageUrl`, `timelapseUrl`, `expiresAt`(3개)뿐이다. 다운로드 페이지 URL·생성 시각 등 다른 필드는 웹에서 읽지 않는다(스키마는 [40번](./40-database-firestore-and-storage-schema.md) 참조).

---

## 5. 만료 판정 (문서 부재 / expiresAt 경과 / 파싱 실패 fail-safe)

만료 판정은 세 갈래이며, 모두 **안전측(만료로 처리)**으로 기운다(`app.js:206-224`).

| 판정 | 조건 | 결과 | 근거 |
|------|------|------|------|
| 문서 부재 | `!snap.exists()` | expired | `app.js:199-202` |
| 필드 부재/파싱 실패 | `expiresAt` 없음, `toDate` 아님, 또는 Invalid Date | expired (fail-safe) | `app.js:209-220` |
| 시각 경과 | `expiresAt < new Date()` | expired | `app.js:221-224` |
| 미만료 | 위 어느 것도 아님 | `renderSuccess` (success) | `app.js:226` |

- **fail-safe 설계**: `expiresAt`을 판정할 수 없으면 성공 대신 만료로 처리한다. 보관 기간이 초과된 콘텐츠를 잘못 노출하지 않기 위함(`app.js:206-208,216-220`). `expiresAt`은 Firestore `Timestamp` → `toDate()`로 `Date` 변환, `Number.isNaN(getTime())`로 유효성 검사(`app.js:210-215`).
- **retentionHours 반영**: WPF가 `expiresAt = createdAt + retentionHours`(1~72h)로 문서를 만든다(`src/MCPhoto.Core/Upload/UploadContract.cs:43-44`, `src/MCPhoto.Core/Settings/AppSettings.cs:62`). 웹은 이 `expiresAt`으로 **접근 만료(웹 차단)**를 정확히 반영한다. 물리 파일 삭제(age 3일 등)와는 별개다([50번](./50-infra-gcp-lifecycle-and-ttl.md) §2·§3).

### 5.1 미디어 로드 실패 폴백 (물리 삭제 후 문서만 남은 경우)

문서는 미만료(존재+`expiresAt` 미경과)인데 Storage 파일이 이미 지워진 고아 상태를 위한 2차 방어다(`app.js:89-102`).

- `mediaState.photo/video`가 `{present, loadOk}`를 추적한다(`app.js:84-87`).
- `present=true`(URL 있음)인 미디어가 `onerror`로 `loadOk=false`가 되면 실패로 센다(`app.js:115-123`, `app.js:148-156`).
- `maybeFallbackToExpired()`: **present 인 미디어가 하나라도 있고, present 인 것이 모두 로드 실패**면 `expired`로 폴백한다(`app.js:91-102`).
- 옵션 꺼짐(`present=false`)은 실패가 아니므로 폴백 트리거에서 제외된다(§6).

---

## 6. 미디어 옵션 부재 vs 실패 구분 (it7 F3)

`renderSuccess`는 만료 판정을 통과한 뒤에만 호출된다. 따라서 URL 이 falsy 하면 "만료/실패"가 아니라 **"전송 옵션이 꺼진 것"(의도적 제외)**으로 해석한다(`app.js:63-66`, it7 F3, 계약 §5).

| 상태 | present | 화면 처리 | 근거 |
|------|---------|-----------|------|
| URL 있음 | true | 프리뷰·다운로드 버튼·힌트 노출, 옵션꺼짐 안내 숨김 | `app.js:105-126`(사진), `app.js:138-160`(영상) |
| URL null(옵션 꺼짐) | false | 프리뷰·다운로드·힌트·실패문구 숨기고, "전송 옵션이 꺼져 있어 제공되지 않습니다" 안내만 | `app.js:127-135`(사진), `app.js:161-170`(영상) |
| URL 있으나 로드 실패 | true, loadOk=false | 프리뷰 숨김, "불러올 수 없습니다(만료되었을 수 있습니다)" + 다운로드 비활성(`is-disabled`, href 제거) | `app.js:115-123`, `app.js:148-156` |

- 옵션꺼짐 안내(`.media__optout`)는 만료(빨강)·로드실패와 시각적으로 구분되는 **중립 정보 톤**(점선 테두리·회색)이다(`styles.css:184-194`, `index.html:43-45,67-69`).
- 영상 섹션은 옵션 꺼짐이어도 `#video-section`을 **숨기지 않고**(it7) 안내만 노출한다(`app.js:169`).
- 둘 다 옵션 꺼짐(계약상 미발생, 방어적)이면 성공 화면을 유지하며 안내 2개를 보여준다(`app.js:184`). WPF는 사진·타임랩스 중 최소 1개가 있어야 업로드하므로(`src/MCPhoto.Firebase/UploadService.cs:37-38`) 실제로는 발생하지 않는다.

---

## 7. 만료 시각 표기와 다운로드 폴백 안내

- 성공 화면 하단 `#expiry-notice`에 "이 사진·영상은 {만료 시각}에 만료됩니다."를 표시한다(`index.html:72`, `app.js:172-175`).
- `formatExpiry`는 `Intl.DateTimeFormat("ko-KR", ...)`로 **사용자 로컬 시간**으로 포맷하며, 실패 시 `toLocaleString()` 폴백(`app.js:48-61`).
- 다운로드 폴백 힌트(`#photo-hint`/`#video-hint`): "저장이 안 되면 이미지를 길게 눌러(모바일)/우클릭(PC) 저장하세요." — `<a download>`가 cross-origin(`firebasestorage.googleapis.com`)에서 전 브라우저가 무시하므로(MDN: same-origin + `blob:`/`data:` 전용) iOS 한정이 아닌 공통 안내다(`index.html:40-42,64-66`, `app.js:177-179`).

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

---

## 9. 로컬 검증 방법 (Emulator)

`app.js`는 호스트가 `localhost`/`127.0.0.1`/`0.0.0.0`일 때만 Firestore Emulator(`127.0.0.1:8080`)에 연결한다. 실배포 도메인(`*.web.app` 등)에서는 절대 트리거되지 않는다(프로덕션 안전, `app.js:21-24,240-244`).

| npm 스크립트 | 명령 | 용도 | 근거 |
|--------------|------|------|------|
| `serve` | `firebase emulators:start --only hosting` | 호스팅만 로컬 서빙 | `web/package.json:8` |
| `emulators` | `firebase emulators:start --only firestore,storage,hosting` | Firestore+Storage+Hosting 통합 | `web/package.json:9` |
| `test:rules` | `firebase emulators:exec --only firestore,storage "node tests/rules.test.js"` | 보안 규칙 Emulator 테스트(get allow/list deny 등) | `web/package.json:10`, web-architecture.md §6.3 |

- devDependencies: `firebase ^12.16.0`, `@firebase/rules-unit-testing ^5.0.1`(`web/package.json:12-15`). SDK 버전은 런타임 CDN pin(v12.16.0)과 일치한다.
- 규칙 테스트 시나리오: `resultSessions` get allow / list deny / write deny, `users`·`frameTemplates` 전면 deny(`web/firestore.rules:16-38`, web-architecture.md §6.3).

---

## 10. 스타일·접근성 요약 (styles.css)

- 모바일 우선 반응형: 기본 모바일 폭, `@media (min-width:768px)`로 데스크톱 확장(`styles.css:1-2,242-260`).
- 다크 모드: `prefers-color-scheme: dark` 팔레트 오버라이드(`styles.css:17-27`).
- 터치 타깃 하한 48px(`--touch`), 가로 스크롤 금지(`overflow-x:hidden`), `prefers-reduced-motion`에서 스피너 감속(`styles.css:14,37,136-140`).
- `aria-live="polite"`(로딩), `role="status"`(스피너), `aria-disabled`/`is-disabled`(로드 실패 다운로드 비활성)로 접근성 보강(`index.html:18-19`, `app.js:119-120`).

---

## 11. 상호 참조

- 업로드가 만드는 `resultSessions` 문서·`results/` Storage 경로·토큰 URL 조립: [30 · 백엔드 Firebase 연동](./30-backend-firebase-integration.md), [40 · DB/Storage 스키마](./40-database-firestore-and-storage-schema.md).
- 만료 문서·파일의 물리 삭제(웹은 미수행): [50 · GCP 수명주기·TTL](./50-infra-gcp-lifecycle-and-ttl.md).
- 보안 규칙(get/list 분리, results SDK read false): `web/firestore.rules`, `web/storage.rules` ([50번](./50-infra-gcp-lifecycle-and-ttl.md) §5).
