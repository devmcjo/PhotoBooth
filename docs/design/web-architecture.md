# MC포토 — 모바일 다운로드 웹 아키텍처 설계

| 항목 | 값 |
|------|-----|
| 문서 | 모바일 다운로드 웹 페이지 + Firebase Hosting/보안 규칙/TTL 정리 설계 |
| 대상 PRD | `docs/prd/photobooth-prd.md` (초안 v2.7) — §3 F5, §5 모바일 다운로드 페이지, §8, §9 #24, §10 #33 |
| 1차 준거 | `docs/design/firebase-contract.md` (계약 v1, 확정) — **이 계약을 위반하는 설계 금지** |
| 관련 문서 | `docs/design/wpf-architecture.md`(생산자 측 맥락), `docs/design/web-wbs.md`(구현 WBS) |
| 작성일 | 2026-07-20 |
| 상태 | 초안 v1 (구현 착수 전) |

---

## 0. 검증된 사실 / 미검증 가정

> WBS 블루프린트 규칙에 따라 **직접 확인한 사실**과 **미검증 가정**을 분리한다. 모든 가정은 WBS(`web-wbs.md`)의 어느 Step에서 검증되는지 매핑한다.

### 검증된 사실 (verified facts)

- **VF-1. 웹은 읽기 전용 소비자다.** ResultSession을 **토큰 ID로 단건 get만** 하고, User/FrameTemplate는 절대 읽지 않으며, 파일은 문서에 담긴 다운로드 토큰 URL로 브라우저가 직접 fetch한다. (근거: `firebase-contract.md` §0 핵심 불변식, §2.3, §4.3)
- **VF-2. 파일 read에 Storage SDK·방문자 인증·Storage read 규칙이 불필요하다.** Firebase 다운로드 토큰 URL(`?alt=media&token=<uuid>`)은 그 자체가 capability이며 보안 규칙을 우회한다. 웹은 문서의 `finalImageUrl`/`timelapseUrl` 문자열을 `<img src>`/`<video src>`/`<a href>`에 바인딩만 하면 된다. (근거: `firebase-contract.md` §4.3, §5.2)
- **VF-3. Firestore·Hosting은 Spark(무료) 유지, Storage만 Blaze 필수.** Firestore 1GiB·읽기 5만/일, Hosting 저장 1GB·전송 10GB/월은 Spark 무료. Cloud Storage는 2026-02-03부로 Blaze 필수(파일 저장·다운로드 대상). (근거: `firebase-contract.md` §1)
- **VF-4. 다운로드 페이지 URL 기본안은 쿼리형 `/?s={token}`.** WPF는 `firebase-contract.md` §3.5 조립 규칙(`{hostingBaseUrl} + "/?s=" + {token}`)으로 downloadPageUrl을 생성해 문서에 저장하고 QR로 인코딩한다. 웹은 자신의 URL을 이 규약과 일치시켜야 한다. (근거: `firebase-contract.md` §3.1, §3.5)
- **VF-5. 토큰 = UUIDv4 = resultSessions 문서 ID = 접근 열쇠.** 122비트 엔트로피로 열거 방어. 보안 규칙이 list/query를 막으므로 토큰을 아는 사람만 단건 get 가능(capability URL). (근거: `firebase-contract.md` §3.3, §5.1)
- **VF-6. 만료 판정은 `expiresAt < now` 비교 + 문서 존재 여부**로 한다. 문서에 별도 `expired` 플래그는 없다. 문서 부재(삭제됨)도 만료와 동일하게 안내한다. (근거: `firebase-contract.md` §2.3, §3.4)
- **VF-7. 웹 스택은 정적 HTML/CSS + Vanilla JS + Firebase JS SDK.** 프레임워크 불필요, 단일 페이지로 충분. (근거: PRD §9 #24, §7)
- **VF-8. Firebase JS SDK는 gstatic CDN 모듈러 import로 로드 가능**(예: `https://www.gstatic.com/firebasejs/{ver}/firebase-app.js`, `firebase-firestore.js`). 2026-07 기준 최신 계열은 v12.x. (근거: Firebase 공식 문서 `firebase.google.com/docs/web/alt-setup`, 웹 검색 확인 2026-07)
- **VF-9. Firebase Hosting SPA rewrite(`"source":"**","destination":"/index.html"`)는 Spark 무료로 지원**된다. 경로형·쿼리형 어느 쪽이든 단일 index.html로 서빙 가능. (근거: Firebase 공식 문서·firebase.json rewrites, 웹 검색 확인 2026-07)
- **VF-10. GCS Lifecycle 규칙은 Storage 파일만 삭제하며 Firestore 문서는 지우지 못한다.** 따라서 Lifecycle 단독으로는 고아 문서가 남는다(문서 존재 + 파일 부재). 웹은 이 경우 파일 로드 실패로 처리해야 한다. (근거: `firebase-contract.md` §6.2, §6.3 불변식)

### 미검증 가정 (open assumptions)

- **OA-1. 대상 Firebase 프로젝트의 웹 앱 구성값(`apiKey`/`projectId`/`appId` 등)이 배포 시점에 확정되어 주입 가능**하다. MVP는 정적 `firebase-config.js` 파일로 주입한다(공개값이므로 노출 무방, 방어는 보안 규칙이 담당). → 검증: **web-wbs Step 2**(config 로드 후 Firestore 초기화 성공).
- **OA-2. `resultSessions` 컬렉션에 유효 토큰으로 단건 get 시 보안 규칙이 allow, list 쿼리는 deny**한다(규칙 배포 후 실제 동작). → 검증: **web-wbs Step 3**(Emulator 규칙 테스트: get allow / list deny / users·frames deny).
- **OA-3. WPF가 저장한 다운로드 토큰 URL을 모바일 브라우저가 CORS 오류 없이 직접 GET**해 이미지·영상을 표시·다운로드할 수 있다. Firebase 다운로드 토큰 URL은 공개 GET을 허용하므로 CORS 문제 없음이 기대되나 실 연동 미확인. → 검증: **web-wbs Step 4/8**(실제 토큰 URL을 img/video/a에 바인딩, 다운로드 동작).
- **OA-4. `<a download>` 속성은 cross-origin URL(firebasestorage.googleapis.com)에 대해 파일 저장을 트리거하지 못한다** — iOS 한정이 아니라 **전 브라우저 공통 제약**이다. MDN 근거: `download` 속성은 same-origin URL(및 `blob:`/`data:`)에서만 동작하며 cross-origin에서는 무시된다. 따라서 "길게 눌러(모바일)/우클릭(PC) 저장" 안내 폴백을 **상시** 노출한다(사진·영상 양쪽). → 검증: **web-wbs Step 5**(폴백 안내 상시 노출 확인). *(보정: 리뷰 반영, 2026-07-20 — 최초 "iOS Safari 한정" 기술을 cross-origin 전 브라우저 공통으로 정정.)*
- **OA-5. QR 전송(F5) off 운영에서는 웹 페이지가 아예 호출되지 않는다**(ResultSession·Storage 파일 미생성). 웹 설계는 "F5 on일 때만 활성"을 전제한다. → 검증 불요(계약 §1 전제, 웹 범위 밖). 리스크로만 인지.
- **OA-6. Hosting 배포 도메인(`{hostingDomain}`)이 WPF `AppSettings.hostingBaseUrl`과 동일 값으로 설정**된다(웹 배포 도메인 = WPF가 QR에 인코딩하는 base URL). 불일치 시 QR이 잘못된 곳을 가리킨다. → 검증: **web-wbs Step 8**(배포 도메인과 WPF 설정값 대조).

> 모든 미검증 가정이 검증 Step에 매핑됨(OA-5 제외 — 웹 범위 밖 전제). 완결성 게이트 통과.

---

## 1. 아키텍처 개요

### 1.1 한 줄 요약

QR 스캔으로 진입하는 **단일 정적 HTML 페이지**. URL 쿼리(`?s={token}`)에서 토큰을 파싱해 Firebase JS SDK로 `resultSessions/{token}` **단건 get** → 만료·존재 판정 후 사진·영상 프리뷰 + 다운로드 버튼을 표시한다. 프레임워크·번들러·서버 로직 없음. 방어선은 오직 Firestore/Storage **보안 규칙**이다.

### 1.2 설계 원칙 (계약 불변식 준수)

1. **읽기 전용 소비자** — 웹은 `resultSessions` 단건 get 외에 어떤 읽기/쓰기도 하지 않는다. User/FrameTemplate 접근 코드는 **작성하지 않는다**(VF-1).
2. **파일은 URL 문자열 바인딩만** — Storage SDK를 import하지 않는다. `finalImageUrl`/`timelapseUrl`을 DOM 속성에 직접 바인딩(VF-2). 파일명 하드코딩 금지(계약 §4.2).
3. **URL 규약 일치** — 쿼리형 `/?s={token}`. WPF §3.5 조립 규칙과 동일(VF-4).
4. **만료 = 비교 + 부재** — `expiresAt < now` 또는 문서 not-found 시 만료 안내(VF-6). 별도 플래그 없음.
5. **공개 config는 방어선이 아니다** — Firebase 설정값(apiKey 등)은 공개돼도 무방. 유일한 방어선은 보안 규칙(PRD §10).

### 1.3 기술 스택 결정

| 영역 | 선택 | 근거 |
|------|------|------|
| 마크업/스타일 | 정적 HTML5 + CSS(모바일 우선 반응형) | PRD §9 #24. 프레임워크 불필요 |
| 스크립트 | Vanilla JavaScript(ES modules, `<script type="module">`) | PRD §9 #24. 번들러 없이 gstatic CDN import |
| Firebase 접근 | **Firebase JS SDK v12.x (gstatic CDN 모듈러)** — `firebase-app` + `firebase-firestore`만 | VF-7·VF-8. Storage SDK는 import 안 함(VF-2). Auth 안 씀(익명 공개 get) |
| Firestore 읽기 API | `getDoc(doc(db, "resultSessions", token))` 단건 get | VF-1·VF-5. `getDocs`/쿼리 절대 사용 안 함 |
| 배포 | Firebase Hosting(Spark 무료) | VF-3·VF-9 |
| 라우팅 | 쿼리 파싱(단일 index.html) + Hosting SPA rewrite 안전망 | 결정 D-1(§5.1) |
| TTL 정리 | WPF 직접 삭제 + GCS Lifecycle 안전망(웹은 스케줄 Functions **미채택**) | 결정 D-2(§7) |

### 1.4 디렉토리 구조 (Hosting 배포 루트)

```
web/                              ← Hosting 프로젝트 루트(별도 리포 또는 리포 내 web/)
├─ firebase.json                  ← Hosting 설정(public 디렉토리·rewrite·헤더)
├─ .firebaserc                    ← 기본 프로젝트 alias
├─ firestore.rules                ← Firestore 보안 규칙(§6.1)
├─ storage.rules                  ← Storage 보안 규칙(§6.2)
├─ firestore.indexes.json         ← (빈 인덱스 — 웹은 쿼리 안 함, 단건 get만)
├─ public/                        ← Hosting public 디렉토리(배포 대상)
│  ├─ index.html                  ← 다운로드 페이지(단일 페이지, 모든 상태 포함)
│  ├─ firebase-config.js          ← Firebase 웹 앱 구성(공개값, OA-1)
│  ├─ app.js                      ← 진입 로직: 토큰 파싱→get→상태 렌더
│  ├─ styles.css                  ← 모바일 우선 반응형 스타일
│  └─ assets/                     ← 로고·아이콘 등(선택, self-contained)
└─ tests/
   └─ rules.test.js               ← Emulator 보안 규칙 테스트(§6.3)
```

> 단일 페이지이므로 `public/`은 최소 파일만 둔다. `firebase-config.js`를 `app.js`와 분리하는 이유는 배포 환경별 config 교체를 코드 수정 없이 하기 위함(OA-1).

---

## 2. 페이지 구조 (index.html)

단일 `index.html` 안에 **모든 상태의 마크업을 넣어두고**, JS가 상태에 따라 하나의 `<section>`만 보이게 토글한다(SPA·라우터 불필요). 모바일 우선 반응형.

### 2.1 상태별 섹션 (5종)

| 상태 | 섹션 id | 표시 조건 | 내용 |
|------|---------|-----------|------|
| 로딩 | `#state-loading` | 진입 직후~get 응답 전 | 스피너 + "결과물을 불러오는 중…" |
| 성공 | `#state-success` | 문서 존재 + `expiresAt >= now` | 사진 프리뷰+[사진 다운로드], 영상 프리뷰+[영상 다운로드], 만료 고지 문구 |
| 만료 | `#state-expired` | `expiresAt < now` **또는** 문서 not-found | "보관 기간이 지나 만료되었습니다" 안내(재촬영 안내 문구) |
| 오류 | `#state-error` | 토큰 부재/형식 오류, 네트워크·권한 오류(만료 아님) | "일시적인 오류가 발생했습니다. 다시 시도해 주세요" + [다시 시도] |
| 부분 실패 | `#state-success` 내 개별 처리 | 문서는 유효하나 특정 파일 로드 실패 | 해당 미디어만 "불러올 수 없음" 표시, 나머지는 정상(§3.4) |

### 2.2 성공 섹션 구성 (PRD §5 "모바일 다운로드 페이지")

- **사진 영역**: `<img>` 프리뷰(finalImageUrl) + [사진 다운로드] 버튼(`<a download>`) + **상시 폴백 안내**("길게 눌러/우클릭 저장", OA-4 — cross-origin 전 브라우저 공통).
- **영상 영역**: `<video controls playsinline>` 프리뷰(timelapseUrl) + [영상 다운로드] 버튼 + **상시 폴백 안내**(사진과 동등). **timelapseUrl이 null이면 영상 영역 전체를 숨긴다**(계약 §2.3: 생성 실패/미포함 시 null).
- **만료 고지**: "이 링크의 사진·영상은 {만료 시각}에 만료됩니다" — `expiresAt`을 사용자 로컬 시간으로 포맷해 표시(선택적으로 남은 시간).
- **접근성/모바일**: `viewport` meta, 큰 터치 타깃(버튼 최소 44×44px), `playsinline`(iOS 인라인 재생), `loading="eager"`(즉시 표시).

### 2.3 이메일 입력 없음 (PRD §28)

벤치마크 ⑥의 이메일 입력 단계는 **채택하지 않는다**. QR 스캔 즉시 다운로드 제공. 폼·수집 UI 없음.

---

## 3. 상태 흐름 (app.js)

### 3.1 진입 시퀀스

```
페이지 로드
  → (1) URL에서 토큰 파싱: new URLSearchParams(location.search).get("s")
        ├ 토큰 없음/빈 문자열 → #state-error(또는 안내) 표시, 종료
        └ 토큰 있음 → #state-loading 표시
  → (2) Firebase 초기화: initializeApp(firebaseConfig), getFirestore()
  → (3) 단건 get: getDoc(doc(db, "resultSessions", token))
        ├ 예외(네트워크/권한) → #state-error (재시도 버튼)
        ├ !snapshot.exists()  → #state-expired (문서 부재 = 삭제됨/무효 토큰, VF-6)
        └ exists()            → (4)로
  → (4) 만료 판정: data.expiresAt.toDate() < new Date()
        ├ 만료   → #state-expired
        └ 유효   → (5)로
  → (5) 성공 렌더: finalImageUrl → img, timelapseUrl(null 아니면) → video,
        expiresAt → 만료 고지. #state-success 표시
  → (6) 개별 미디어 로드 실패 핸들링: img.onerror / video.onerror →
        해당 영역만 "불러올 수 없음"(§3.4, VF-10 고아 문서 대응)
```

### 3.2 토큰 형식 검증 (경량)

- 토큰은 UUIDv4(VF-5). 형식 검증은 **경량**으로만(길이·허용 문자) — 엄격한 정규식 강제는 불필요(Firestore가 not-found로 걸러줌). 다만 명백히 빈 값·과도한 길이는 get 전에 차단해 불필요한 읽기·오류를 줄인다.

### 3.3 만료/부재 판정 상세 (계약 §3.4, VF-6)

| 조건 | 판정 | 화면 |
|------|------|------|
| `snapshot.exists() == false` | 문서 삭제됨 또는 무효 토큰 | 만료 안내(`#state-expired`) |
| `exists()` + `expiresAt < now` | 보관 기간 경과 | 만료 안내(`#state-expired`) |
| `exists()` + `expiresAt >= now` | 유효 | 성공(`#state-success`) |

> 무효 토큰과 삭제된 문서를 **구분하지 않고 동일 안내**한다(계약 §3.4). 토큰 열거 방어 관점에서도 "존재 여부"를 노출하지 않는 편이 안전.

### 3.4 파일 부재(고아) 대응 (VF-10, 계약 §3.4)

- WPF 직접 삭제 또는 GCS Lifecycle이 Storage 파일을 먼저 지우고 문서가 남은 순간(고아 문서)이 발생할 수 있다(Lifecycle은 문서를 못 지움).
- 이 경우 문서 get은 성공하지만 `<img>`/`<video>` 로드가 404로 실패한다. → **개별 미디어 영역만** "불러올 수 없음"으로 표시하고, 둘 다 실패하면 만료 안내로 폴백한다.
- 즉 성공 판정(문서 유효)과 미디어 로드 성공을 **분리**해 부분 실패를 우아하게 처리한다.

### 3.5 재시도 정책

- `#state-error`(네트워크/권한 예외)에서만 [다시 시도] 제공 → 같은 토큰으로 get 재실행.
- 만료(`#state-expired`)는 재시도 없음(영구 상태).

---

## 4. Firebase JS SDK 사용 방식

### 4.1 import 범위 (최소)

```js
// public/app.js (요지 — 실제 버전은 WBS Step 2에서 고정)
import { initializeApp } from "https://www.gstatic.com/firebasejs/12.x/firebase-app.js";
import { getFirestore, doc, getDoc }
  from "https://www.gstatic.com/firebasejs/12.x/firebase-firestore.js";
import { firebaseConfig } from "./firebase-config.js";
```

- **firebase-app + firebase-firestore만** import. **firebase-storage·firebase-auth는 import하지 않는다**(VF-2: 파일은 토큰 URL 직접 GET, 인증 없음).
- `<script type="module">`로 로드(deferred 기본).
- 버전은 gstatic 고정 버전 URL로 pin(예: `12.16.0`) — 무버전/latest 사용 금지(재현성). 실제 버전은 web-wbs Step 2에서 확정·고정.

### 4.2 읽기 API (단건 get 전용)

```js
const snap = await getDoc(doc(db, "resultSessions", token));
if (!snap.exists()) { showExpired(); return; }
const data = snap.data();  // finalImageUrl, timelapseUrl, expiresAt, ...
```

- **`getDocs`/`query`/`where`/`orderBy` 절대 사용 안 함**(VF-1, 계약 §5.1 list deny). 코드에 컬렉션 쿼리가 등장하면 계약 위반이자 규칙에 의해 실패한다.
- `expiresAt`은 Firestore `Timestamp` → `.toDate()`로 JS Date 변환 후 비교.

### 4.3 config 주입 (OA-1)

```js
// public/firebase-config.js — 공개값(방어는 보안 규칙이 담당)
export const firebaseConfig = {
  apiKey: "…", authDomain: "…", projectId: "…",
  storageBucket: "…", appId: "…"
};
```

- 이 값은 공개돼도 무방(PRD §10: apiKey는 비밀이 아님, 방어선은 규칙). 배포 환경별로 이 파일만 교체.

### 4.4 오프라인 지속성 미사용

- 웹은 1회성 진입(QR 스캔)이므로 Firestore 오프라인 캐시·실시간 리스너(`onSnapshot`) 불필요. **단발 `getDoc`만** 사용해 읽기 횟수·복잡도를 최소화한다(Spark 읽기 5만/일 한도 관점에서도 유리).

---

## 5. 라우팅 & URL 결정

### 5.1 결정 D-1: URL 형식 — **쿼리형 `/?s={token}` 채택**

| 방식 | 장점 | 단점 | 판정 |
|------|------|------|------|
| **쿼리형 `/?s={token}`** | 단일 index.html에서 `URLSearchParams`로 즉시 파싱. rewrite 없이도 동작(루트로 항상 들어옴). 계약 §3.1 **기본안**. 가장 단순 | URL이 쿼리 파라미터 노출(캡처 URL 특성상 무관) | **채택** |
| 경로형 `/d/{token}` | URL이 짧고 깔끔 | Hosting rewrite로 `/d/**`→`/index.html` 필요. 파싱은 `location.pathname` split | 미채택 |

**근거**: (1) 계약 기본안이며 WPF §3.5 조립 규칙(`{hostingBaseUrl}/?s={token}`)이 그대로 성립해 **WPF 코드 변경 불요**. (2) 단일 정적 페이지에서 rewrite 설정 없이 항상 루트로 진입하므로 가장 단순(PRD §9 #24 "단일 페이지로 충분" 부합). (3) QR로만 접근되는 capability URL이라 경로 미관 이점이 무의미.

- **결과**: WPF는 `downloadPageUrl = {hostingBaseUrl} + "/?s=" + {token}` 규약(계약 §3.5)을 그대로 사용. 웹은 `/?s=` 쿼리를 파싱한다. **계약 변경 없음**(계약이 이미 이 형식을 기본안으로 명시).

### 5.2 Hosting rewrite 안전망

- 쿼리형은 항상 `/`로 진입하므로 rewrite가 필수는 아니지만, 오타·잘못된 경로 접근(`/foo`) 시에도 index.html로 폴백하도록 SPA rewrite를 둔다(VF-9). 이는 404 화이트페이지 대신 우리 오류/만료 안내를 보여주기 위함.

```json
// firebase.json (요지)
{
  "hosting": {
    "public": "public",
    "ignore": ["firebase.json", "**/.*", "**/node_modules/**"],
    "rewrites": [ { "source": "**", "destination": "/index.html" } ]
  }
}
```

---

## 6. 보안 규칙 설계 (계약 §5 명세 준수)

> 웹 Firebase config(apiKey)는 공개되므로 **보안 규칙이 유일한 방어선**(PRD §10). 계약 §5.1/§5.2 요구사항을 정확히 구현한다.

### 6.1 Firestore 규칙 (`firestore.rules`)

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    // users: 전면 차단 (평문 pw 보호, 계약 §2.1/§5.1)
    match /users/{uid} {
      allow read, write: if false;
    }

    // frameTemplates: 전면 차단 (웹 접근 없음, WPF 전용, 계약 §2.2/§5.1)
    match /frameTemplates/{fid} {
      allow read, write: if false;
    }

    // resultSessions: 토큰 ID 단건 get만 허용, list/write 금지 (계약 §2.3/§5.1)
    match /resultSessions/{sid} {
      allow get:   if true;    // 단건 조회 허용 (토큰 = capability)
      allow list:  if false;   // 쿼리/열거 금지 (토큰 열거 방어) — 핵심
      allow write: if false;   // 웹 쓰기 없음 (WPF가 신뢰 경로로 생성)
    }

    // 그 외 모든 경로 기본 차단
    match /{document=**} {
      allow read, write: if false;
    }
  }
}
```

- **핵심(계약 §5.1)**: `resultSessions`는 `allow get`(단건)과 `allow list`(쿼리)를 **분리**한다. `allow read`(get+list 통합) **사용 금지** — read로 쓰면 list까지 열려 토큰 열거가 가능해진다.
- **WPF 쓰기 경로**: MVP 1차는 서비스 계정(Admin SDK)이 규칙을 **완전 우회**하므로 위 `write: if false`가 WPF 쓰기를 막지 않는다(계약 §5.1). 배포 시 WPF가 규칙 준수 클라이언트로 전환하면, 그때 인증 계정 write 조건을 별도 추가한다(웹=비인증엔 여전히 deny). **이 규칙 파일은 웹(공개) 관점의 최소 권한을 정의**하며, WPF 인증 쓰기 조건 추가는 배포 단계 과제로 남긴다(계약 §5.1 주석에 명시).

### 6.2 Storage 규칙 (`storage.rules`)

```
rules_version = '2';
service firebase.storage {
  match /b/{bucket}/o {

    // results/: 웹은 SDK read 안 함(토큰 URL 직접 GET). SDK 경로는 닫아둠 (계약 §5.2)
    match /results/{sessionId}/{fileName} {
      allow read:  if false;   // SDK 경로 열거·직접 접근 차단 (토큰 URL은 규칙 우회하여 동작)
      allow write: if false;   // 웹 쓰기 없음
    }

    // frames/: 웹 접근 전면 차단 (WPF 전용, 계약 §5.2)
    match /frames/{userId}/{fileName} {
      allow read, write: if false;
    }

    // 그 외 기본 차단
    match /{allPaths=**} {
      allow read, write: if false;
    }
  }
}
```

- **중요(계약 §5.2)**: 웹은 Storage SDK로 파일을 읽지 않고 **다운로드 토큰 URL로 직접 GET**한다. 토큰 URL은 보안 규칙을 우회하므로 `results/` SDK read를 **`false`로 닫아도 웹 다운로드는 정상 동작**한다. 오히려 닫아두는 편이 SDK 경로 열거·직접 접근을 막아 안전(계약 §5.2 권고).
- WPF 쓰기는 서비스 계정(우회) 또는 인증 클라이언트. 웹은 write 전면 deny.

### 6.3 규칙 검증 (Emulator 테스트, 계약 §5.3)

`tests/rules.test.js`에서 `@firebase/rules-unit-testing`으로 다음을 검증(web-wbs Step 3):

| # | 시나리오 | 기대 |
|---|----------|------|
| a | 웹(비인증)이 `users/{uid}` get | **deny** |
| b | 웹이 `frameTemplates/{fid}` get | **deny** |
| c | 웹이 `resultSessions` **list 쿼리** | **deny** |
| d | 웹이 `resultSessions/{validToken}` **단건 get** | **allow** |
| e | 웹이 `resultSessions/{sid}` write | **deny** |
| f | 웹이 Storage `results/…` SDK read | **deny**(정상 — 웹은 토큰 URL 사용) |
| g | 웹이 Storage `frames/…` read | **deny** |

- Emulator 실행: `firebase emulators:exec --only firestore,storage "node tests/rules.test.js"` 또는 테스트 러너 연동(WBS에 자동 실행 명령 명시).

---

## 7. TTL 만료 정리 방식 (결정 D-2)

### 7.1 결정 D-2: 스케줄 Cloud Functions — **미채택**

**판정: 채택하지 않는다.** WPF 직접 삭제(1차) + GCS Lifecycle(안전망)으로 MVP 충분. 웹 측은 TTL 정리에 관여하지 않는다.

**근거:**
1. **Blaze 강제 + 복잡도**: 스케줄 Functions(Cloud Scheduler + Pub/Sub 기반)는 Blaze 요금제와 Functions 배포·유지보수를 요구한다. Storage 때문에 이미 Blaze는 전환돼 있으나, Functions는 별도의 배포 파이프라인·콜드스타트·모니터링 부담을 추가한다.
2. **이득 부재**: 계약 §6.2가 WPF 직접 삭제를 1차 주체로 확정했고, GCS Lifecycle이 age 기반 안전망을 제공한다. Functions가 메우는 유일한 공백은 "WPF가 상시 켜져 있지 않은 운영 환경"인데, MVP는 개인 사용·단일 키오스크 전제라 이 공백이 실질 리스크가 아니다(계약 §6.3, WPF §6.5).
3. **웹 아키텍처 단순성 유지**: PRD §9 #24가 "정적 페이지·프레임워크 불필요"를 확정했다. Functions 도입은 웹 산출물에 서버 런타임을 끌어들여 이 원칙과 충돌한다.

**단, 잔여 리스크 1건 인지(§7.3)** — 문서만 남는 고아 문서(Firestore TTL 대비)는 웹이 파일 로드 실패로 우아하게 처리(§3.4)하므로 사용자 경험상 무해하다.

### 7.2 채택 방식 (계약 §6.2/§6.3 준수)

| 방식 | 주체 | 채택 | 웹 관여 |
|------|------|------|---------|
| WPF 직접 삭제 | WPF | 1차(계약 확정) | 없음 |
| GCS Lifecycle 규칙 | 인프라 | 안전망(계약 확정) | 없음(설정만 문서화) |
| 스케줄 Cloud Functions | 웹/인프라 | **미채택**(D-2) | — |
| Firestore 네이티브 TTL | 인프라 | **선택 권장**(§7.3) | 없음 |

- **불변식 준수(계약 §6.3)**: 어떤 삭제 주체든 (1) `results/`만 대상, (2) `frames/`·로컬 저장분 비대상, (3) 문서+파일 함께 정리. GCS Lifecycle 설정은 **`results/` 프리픽스에만** age 규칙을 걸고 `frames/`는 제외한다(WBS Step 6에 설정 절차 명시).

### 7.3 Firestore 네이티브 TTL 권장 (문서 고아 최소화)

- WPF 직접 삭제가 문서+파일을 함께 지우므로 정상 경로에선 고아가 없다. 그러나 WPF가 못 지운 경우 GCS Lifecycle이 **파일만** 지워 문서 고아가 남을 수 있다(VF-10).
- 이를 완화하려면 `resultSessions.expiresAt` 필드에 **Firestore 네이티브 TTL 정책**을 설정한다(콘솔/gcloud에서 필드 지정, 무료·서버리스, Functions 불요). 이는 만료 문서를 자동 삭제해 고아 문서를 줄인다. **웹 코드 변경 없음** — 규칙·SDK 사용과 무관한 인프라 설정.
- Firestore TTL은 삭제가 즉시가 아니라 며칠 내 best-effort지만, 웹은 이미 `expiresAt < now`로 만료를 판정하므로(§3.3) 문서가 늦게 지워져도 사용자에겐 만료로 보인다. **정합성 문제 없음**.
- 이 설정은 **권장이지 필수는 아니다**(WBS Step 6에 선택 절차로 기재). 채택하지 않아도 웹은 §3.4 폴백으로 고아를 처리한다.

### 7.4 웹의 TTL 관련 책임 요약

- 웹은 삭제를 **수행하지 않는다**. 만료된/삭제된 세션에 대해 **만료 안내를 정확히 표시**할 책임만 진다(§3.3/§3.4).
- 문서·GCS Lifecycle·Firestore TTL 설정 절차는 web-wbs에 운영 문서로 포함하되, 웹 페이지 코드와는 분리된 인프라 작업이다.

---

## 8. Hosting 구성 (`firebase.json` / 캐싱 / 헤더)

### 8.1 firebase.json

```json
{
  "hosting": {
    "public": "public",
    "ignore": ["firebase.json", "**/.*", "**/node_modules/**"],
    "rewrites": [ { "source": "**", "destination": "/index.html" } ],
    "headers": [
      {
        "source": "/index.html",
        "headers": [ { "key": "Cache-Control", "value": "no-cache, max-age=0" } ]
      },
      {
        "source": "**/*.@(js|css)",
        "headers": [ { "key": "Cache-Control", "value": "public, max-age=3600" } ]
      }
    ]
  },
  "firestore": { "rules": "firestore.rules", "indexes": "firestore.indexes.json" },
  "storage":   { "rules": "storage.rules" }
}
```

- **index.html no-cache**: 세션마다 최신 로직이 로드되도록(오래된 캐시로 인한 오류 방지). 캡처 URL은 1회성이라 페이지 캐시 이득이 작고, 만료 판정 정확성이 중요.
- **js/css 단기 캐시**: 정적 자산은 1시간 캐시(전송량 절감, Spark 10GB/월 관점).
- **firestore.indexes.json**: 웹은 쿼리를 안 하므로 **빈 인덱스**(`{"indexes":[],"fieldOverrides":[]}`). 복합 인덱스 불요.

### 8.2 배포 명령 (Spark 무료)

- `firebase deploy --only hosting,firestore:rules,storage:rules` — Hosting·규칙 함께 배포.
- Firestore/Hosting은 Spark 무료 범위(VF-3). Storage는 Blaze(이미 전환 전제, 계약 §1).
- 배포 도메인(`{project}.web.app`/`{project}.firebaseapp.com` 또는 커스텀)이 **WPF `hostingBaseUrl`과 일치**해야 QR이 올바른 곳을 가리킨다(OA-6, WBS Step 8 검증).

### 8.3 보안 헤더(선택, self-contained 강화)

- MVP 필수는 아니나, 인라인 자산·외부 의존 최소화를 위해 CSP·`X-Content-Type-Options: nosniff` 등을 `headers`에 추가 가능. Firebase JS SDK가 gstatic에서 로드되므로 CSP `script-src`에 `https://www.gstatic.com` 허용 필요. **MVP에선 과설정을 피하고 WBS Step 7(선택 강화)로 분리**.

---

## 9. 계약 문서(firebase-contract.md)와의 정합성

| 계약 항목 | 웹 설계 반영 | 비고 |
|-----------|-------------|------|
| §0 읽기 전용 소비자·단건 get | §1.2·§4.2 | 위반 없음 |
| §2.3 resultSessions 필드 사용 | §3.1·§4.2(finalImageUrl/timelapseUrl/expiresAt) | downloadPageUrl은 웹이 소비 안 함(WPF 산출·QR용) |
| §3.1/§3.5 URL 쿼리형 `/?s={token}` | §5.1 결정 D-1 | 계약 기본안 채택, **WPF 변경 불요** |
| §4.2 파일명 하드코딩 금지 | §1.2 원칙 2 | 문서 URL만 바인딩 |
| §4.3 다운로드 토큰 URL 직접 GET | §1.2·§2.2·§4.1 | Storage SDK 미사용 |
| §5.1 Firestore 규칙(get-only, list deny) | §6.1 | get/list 분리, read 통합 금지 |
| §5.2 Storage 규칙(results read 닫음, frames deny) | §6.2 | 토큰 URL 우회 활용 |
| §5.3 규칙 Emulator 검증 | §6.3 | a~g 시나리오 |
| §6.2/§6.3 TTL 분담·불변식 | §7 결정 D-2 | Functions 미채택, Lifecycle `results/`만·frames 제외 |

**계약 변경 요청: 없음.** 두 결정(D-1 쿼리형, D-2 Functions 미채택) 모두 계약이 웹 재량으로 남긴 미결 항목(계약 §8 "미결")을 계약 기본안·권고 범위 내에서 확정한 것으로, WPF 코드·계약 스키마 변경이 불필요하다.

---

## 10. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|--------|------|------|------|
| WR1 | iOS Safari `<a download>` cross-origin 미동작 | 사진·영상 저장 불편 | "길게 눌러 저장" 폴백 안내, video는 기본 컨트롤 저장 | WBS Step 5 (OA-4) |
| WR2 | 다운로드 토큰 URL CORS 오류 | 미디어 표시·다운로드 실패 | Firebase 토큰 URL은 공개 GET 허용(기대). 실패 시 Storage CORS 설정 문서화 | WBS Step 4/8 (OA-3) |
| WR3 | 보안 규칙 오작성으로 list 열림 | 토큰 열거 → 전 세션 노출 | get/list 분리·`read` 금지, Emulator 테스트 c(list deny) 필수 | WBS Step 3 (OA-2) |
| WR4 | 고아 문서(파일만 삭제됨) | 성공 화면에서 미디어 404 | §3.4 개별 미디어 실패 처리 + Firestore TTL 권장(§7.3) | WBS Step 4 (VF-10) |
| WR5 | Hosting 도메인 ≠ WPF hostingBaseUrl | QR이 잘못된 URL 지시 | 배포 도메인과 WPF 설정 대조 | WBS Step 8 (OA-6) |
| WR6 | SDK 버전 무핀 → 동작 변경 | 배포 후 예기치 않은 회귀 | gstatic 고정 버전 URL pin | WBS Step 2 |
| WR7 | Spark 읽기/전송 한도 초과 | 페이지 접근 차단 | 단발 getDoc·정적 자산 캐시·이미지 CDN 미경유(토큰 URL 직접) | 운영 모니터링 |

---

## 11. web-wbs 인계 요약

- **구현 산출물**: `web/public/{index.html, app.js, firebase-config.js, styles.css}`, `web/{firebase.json, firestore.rules, storage.rules, firestore.indexes.json, .firebaserc}`, `web/tests/rules.test.js`.
- **핵심 준수**: (1) `resultSessions` 단건 get만, (2) 파일은 문서 URL 직접 바인딩(Storage SDK 미사용), (3) 쿼리형 `/?s={token}`, (4) 규칙 get/list 분리, (5) Functions 미채택.
- **검증 자동화**: Emulator 규칙 테스트(§6.3), 로컬 서빙(`firebase serve`/`emulators:start hosting`), 배포 후 실 토큰 URL 스모크.
- 상세 단계는 `docs/design/web-wbs.md` 참조.
