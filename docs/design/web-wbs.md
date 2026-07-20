# MC포토 — 모바일 다운로드 웹 구현 WBS 블루프린트

| 항목 | 값 |
|------|-----|
| 대상 | `web/` (정적 HTML/CSS + Vanilla JS + Firebase JS SDK) Hosting 프로젝트 그린필드 구현 |
| 설계 근거 | `docs/design/web-architecture.md`, `docs/design/firebase-contract.md`(1차 준거), `docs/prd/photobooth-prd.md` v2.7 |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-20 |
| 검증 도구 | `firebase` CLI(Hosting/Emulator), `node`(규칙 테스트), 브라우저(수동 스모크) |

> 각 Step은 self-contained다. 대화 컨텍스트 없는 fresh 에이전트가 해당 Step만 읽고 실행할 수 있도록 작성했다.
> 전제(계약): 웹은 **읽기 전용 소비자**다 — `resultSessions` 단건 get만, User/FrameTemplate 접근 금지, 파일은 문서에 담긴 **다운로드 토큰 URL 직접 GET**(Storage SDK 미사용). 이 불변식을 어기는 코드는 리뷰·규칙에서 실패한다.

---

## 검증된 사실 (verified facts)

- **VF-1**: 웹은 읽기 전용 소비자, `resultSessions` 단건 get만. User/FrameTemplate·list 쿼리 금지. (근거: `firebase-contract.md` §0/§5.1)
- **VF-2**: 파일은 문서의 `finalImageUrl`/`timelapseUrl`(다운로드 토큰 URL)을 DOM에 직접 바인딩. Storage SDK·인증 불요. (근거: `firebase-contract.md` §4.3/§5.2)
- **VF-3**: Firestore·Hosting은 Spark 무료, Storage만 Blaze(계약 전제, 웹은 파일을 URL로만 소비하므로 웹 배포 자체는 Spark로 가능). (근거: `firebase-contract.md` §1)
- **VF-4**: URL 형식 = 쿼리형 `/?s={token}`(결정 D-1). WPF는 `{hostingBaseUrl}/?s={token}`로 조립. (근거: `web-architecture.md` §5.1, `firebase-contract.md` §3.5)
- **VF-5**: 만료 판정 = `expiresAt < now` **또는** 문서 not-found. 별도 플래그 없음. (근거: `firebase-contract.md` §3.4)
- **VF-6**: 보안 규칙 = users/frames 전면 deny, resultSessions get allow·list deny·write deny. `allow read` 통합 금지. (근거: `firebase-contract.md` §5.1)
- **VF-7**: TTL 정리에 스케줄 Functions 미채택(결정 D-2). 웹은 삭제 미수행, 만료 안내만. (근거: `web-architecture.md` §7)
- **VF-8**: Firebase JS SDK는 gstatic CDN 모듈러 import(v12.x 계열). firebase-app + firebase-firestore만 사용. (근거: `web-architecture.md` §4.1)

## 미검증 가정 (open assumptions)

- **OA-1**: Firebase 웹 앱 config(apiKey 등)가 배포 시점에 확정·주입 가능(정적 firebase-config.js) → **검증: Step 2**
- **OA-2**: 배포된 보안 규칙이 resultSessions 단건 get allow·list deny, users/frames deny로 실동작 → **검증: Step 3**
- **OA-3**: WPF가 저장한 다운로드 토큰 URL을 모바일 브라우저가 CORS 오류 없이 직접 GET(표시·다운로드) → **검증: Step 4/8**
- **OA-4**: iOS Safari `<a download>` cross-origin 저장 가능 여부(불가 시 "길게 눌러 저장" 폴백 필요) → **검증: Step 5**
- **OA-5**: Hosting 배포 도메인 = WPF `AppSettings.hostingBaseUrl`(불일치 시 QR 오지시) → **검증: Step 8**

> 모든 미검증 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (Hosting 프로젝트 스캐폴드 + firebase.json)
  ├─ Step 2 (Firebase 초기화 + config 주입)         ← Step1
  ├─ Step 6 (보안 규칙 파일 작성)                    ← Step1 (규칙 파일은 페이지와 독립, 병렬 가능)
  └─ Step 4 (index.html 상태 마크업 + styles)        ← Step1
Step 2 → Step 3 (토큰 파싱 + 단건 get + 상태 전이)
Step 3 + Step 4 → Step 5 (성공 렌더: 미디어 프리뷰·다운로드·폴백)
Step 6 → Step 7 (Emulator 규칙 테스트)
Step 8 (TTL 정리 인프라 문서화: GCS Lifecycle + Firestore TTL 권장)  ← Step1 (독립)
Step 9 (배포 + end-to-end 스모크)                    ← 전 단계
```

- **병렬 가능**: Step 2·4·6·8은 Step 1 이후 서로 독립적으로 진행 가능.

---

## Step 1: Hosting 프로젝트 스캐폴드 & firebase.json

- **Context Brief**: 그린필드다. QR 스캔으로 열리는 단일 정적 다운로드 페이지를 Firebase Hosting에 배포하기 위한 프로젝트 골격을 만든다. 프레임워크·번들러 없음(PRD §9 #24). 이 Step은 디렉토리 구조와 `firebase.json`(Hosting·규칙·rewrite·캐시 헤더)을 세운다. 이후 모든 Step의 토대.
- **대상 파일**: `web/firebase.json`, `web/.firebaserc`, `web/firestore.indexes.json`, `web/public/index.html`(빈 골격), `web/.gitignore`(`node_modules/`, `.firebase/` 제외), `web/package.json`(규칙 테스트용 devDependencies 선언).
- **선행 조건**: 없음. (`firebase` CLI 설치 필요: `npm i -g firebase-tools` 또는 npx)
- **구현 내용**:
  - `web/` 디렉토리 생성. `firebase.json`을 `web-architecture.md` §8.1 그대로 작성:
    - `hosting.public = "public"`, `ignore`에 `firebase.json`·`**/.*`·`**/node_modules/**`.
    - `rewrites`: `[{ "source": "**", "destination": "/index.html" }]`(SPA 안전망, VF-4).
    - `headers`: `/index.html`은 `Cache-Control: no-cache, max-age=0`, `**/*.@(js|css)`는 `public, max-age=3600`.
    - `firestore.rules = "firestore.rules"`, `firestore.indexes = "firestore.indexes.json"`, `storage.rules = "storage.rules"`.
  - `firestore.indexes.json`: **빈 인덱스** `{"indexes":[],"fieldOverrides":[]}`(웹은 쿼리 안 함).
  - `.firebaserc`: 기본 프로젝트 alias(`{"projects":{"default":"<projectId>"}}`) — projectId는 배포 대상 프로젝트(OA-1, 값 미상 시 플레이스홀더 후 Step 9에서 확정).
  - `public/index.html`: `<!doctype html>` + viewport meta + 빈 body(Step 4에서 채움) — 배포 스모크용 최소 골격.
  - `package.json`: `devDependencies`에 `firebase-tools`, `@firebase/rules-unit-testing`(Step 7용) 선언. scripts에 `"serve": "firebase emulators:start --only hosting"`.
- **검증 명령**:
  - `cd web && npx firebase --version`(CLI 동작 확인)
  - `cd web && npx firebase emulators:start --only hosting` 후 `http://localhost:5000` 접속 → 빈 페이지 200 응답(수동), 또는 `curl -sSf http://localhost:5000/ >/dev/null`로 200 확인.
- **완료 기준**:
  - [관측] `firebase.json`이 유효 JSON이며 Hosting Emulator가 `public/index.html`을 200으로 서빙. 존재하지 않는 경로(`/foo`) 접근 시에도 rewrite로 index.html이 반환됨.
  - [non-goal] 이 단계에서 Firebase 초기화·토큰 파싱·규칙 로직 **없음**(빈 골격만). 실제 프로젝트 배포는 아직 안 함(로컬 Emulator만).
  - [trigger] 서빙은 `emulators:start` 실행 시에만. rewrite는 매칭 안 되는 경로 접근 시에만 index.html 반환.
- **롤백**: `web/` 디렉토리 삭제(그린필드라 이전 상태 = 없음).
- [ ] 완료

---

## Step 2: Firebase 초기화 & config 주입

- **Context Brief**: 웹 페이지가 Firestore에 접근하려면 Firebase JS SDK를 gstatic CDN에서 모듈러 import하고 공개 config로 초기화해야 한다(`web-architecture.md` §4). config(apiKey 등)는 **공개돼도 무방**하며 방어선은 보안 규칙이다(PRD §10). 이 Step은 초기화 배선만 만들고 실제 읽기는 Step 3에서 한다.
- **대상 파일**: `web/public/firebase-config.js`(공개 config, export), `web/public/app.js`(초기화 코드), `web/public/index.html`(`<script type="module" src="./app.js">` 연결).
- **선행 조건**: Step 1.
- **구현 내용**:
  - `firebase-config.js`: `export const firebaseConfig = { apiKey, authDomain, projectId, storageBucket, appId }`. 값은 대상 Firebase 프로젝트 웹 앱 설정에서 복사(OA-1). 미상 시 플레이스홀더 후 Step 9 전 교체.
  - `app.js`: gstatic CDN 모듈러 import — `initializeApp`(firebase-app.js), `getFirestore`(firebase-firestore.js). **버전은 고정 URL pin**(예: `https://www.gstatic.com/firebasejs/12.16.0/firebase-app.js`). latest/무버전 금지(VF-8, WR6).
    - `import { firebaseConfig } from "./firebase-config.js";`
    - `const app = initializeApp(firebaseConfig); const db = getFirestore(app);`
  - **firebase-storage·firebase-auth는 import하지 않는다**(VF-2 — 파일은 토큰 URL 직접 GET, 인증 없음).
  - 초기화 성공 여부를 콘솔 로그(진단)로 남기고, 실패 시 `#state-error`로 전환(Step 4 섹션 준비 전이면 콘솔 에러만).
- **검증 명령**:
  - `cd web && npx firebase emulators:start --only hosting` → 브라우저 devtools 콘솔에서 초기화 로그 확인, 네트워크 탭에 gstatic `firebase-app.js`/`firebase-firestore.js` 200 로드 확인.
  - `grep -n "firebase-storage\|firebase-auth\|getStorage\|getAuth" web/public/app.js` → **매치 0건**(금지 import 미포함 확인).
- **완료 기준**:
  - [관측] 페이지 로드 시 gstatic에서 firebase-app·firebase-firestore가 고정 버전 URL로 로드되고, `initializeApp`·`getFirestore`가 예외 없이 완료(콘솔 로그). firebase-config.js의 값이 반영됨.
  - [non-goal] firebase-storage/firebase-auth import·`getStorage`/`getAuth` 호출 **없음**(grep 0건). 이 단계에서 실제 문서 읽기·화면 렌더 **없음**.
  - [trigger] 초기화는 페이지 로드(module script 실행) 시에만.
- **롤백**: Step 2 변경 revert. app.js를 빈 파일로, index.html의 script 연결 제거(Step 1 상태).
- [ ] 완료

---

## Step 3: 토큰 파싱 · 단건 get · 상태 전이 코어

- **Context Brief**: QR로 진입한 URL은 쿼리형 `/?s={token}`이다(VF-4). 웹은 토큰을 파싱해 `resultSessions/{token}`을 **단건 get**하고(VF-1), 문서 존재·만료를 판정해 로딩/성공/만료/오류 상태로 전이한다(`web-architecture.md` §3). 이 Step은 상태 전이 **로직**을 만든다(화면 마크업은 Step 4, 미디어 렌더는 Step 5). `getDocs`/쿼리는 절대 쓰지 않는다(계약 §5.1 list deny).
- **대상 파일**: `web/public/app.js`(토큰 파싱·get·판정·상태 전이 함수).
- **선행 조건**: Step 2.
- **구현 내용**:
  - 토큰 파싱: `const token = new URLSearchParams(location.search).get("s");`
    - 토큰 없음/빈 문자열 → `showState("error")`(또는 안내) 후 종료.
    - 경량 형식 검증(길이·허용 문자) — 명백히 빈 값·과도한 길이만 차단(엄격 정규식 불요, `web-architecture.md` §3.2).
  - 단건 get: `import { doc, getDoc } from ".../firebase-firestore.js";`
    - `const snap = await getDoc(doc(db, "resultSessions", token));`
    - **`getDocs`/`query`/`where`/`collection` 절대 사용 금지**(VF-1/VF-6).
  - 판정(`web-architecture.md` §3.3):
    - 예외(네트워크/권한) catch → `showState("error")`(재시도 버튼).
    - `!snap.exists()` → `showState("expired")`(문서 부재 = 삭제/무효 토큰, VF-5).
    - `exists()` + `data.expiresAt.toDate() < new Date()` → `showState("expired")`.
    - `exists()` + 유효 → `renderSuccess(data)`(Step 5에서 구현, 여기선 호출부·데이터 전달까지).
  - `showState(name)`: 상태별 `<section>` 하나만 보이도록 토글(로딩/성공/만료/오류). 진입 직후 기본 = 로딩.
  - `expiresAt`은 Firestore `Timestamp` → `.toDate()` 변환 후 비교.
- **검증 명령**:
  - `grep -nE "getDocs|[^A-Za-z]query\(|where\(|collection\(" web/public/app.js` → **매치 0건**(쿼리 API 미사용 = 계약 준수 확인).
  - `grep -nE "getDoc\(|resultSessions" web/public/app.js` → 단건 get·컬렉션명 존재 확인.
  - Emulator + Firestore Emulator에 시드 문서 넣고 수동: 유효 토큰 → 성공 경로 진입 로그, 만료 토큰 → 만료, 없는 토큰 → 만료, `?s=` 없음 → 오류(콘솔/화면 상태 로그로 확인).
- **완료 기준**:
  - [관측] `?s={유효토큰}`으로 진입 시 성공 렌더 호출(데이터 전달 로그). `?s={만료토큰}`·`?s={없는토큰}`·`?s=`(빈값)·`?s` 누락 각각 만료 또는 오류 상태로 전이. get은 단건(`getDoc`)만 사용.
  - [non-goal] 컬렉션 쿼리(`getDocs`/`query`/`where`) 코드 **없음**(grep 0건). User/frameTemplates 읽기 **없음**. 만료·부재를 **구분해서 다른 안내를 하지 않음**(둘 다 만료 안내, 토큰 존재 노출 방지).
  - [trigger] get은 토큰이 유효 형식일 때만 1회 실행. 상태 전이는 get 결과·예외에만 반응(사용자 입력 없이 자동).
- **롤백**: Step 3 변경 revert. app.js를 Step 2(초기화만) 상태로.
- [ ] 완료

---

## Step 4: index.html 상태 마크업 & 모바일 우선 스타일

- **Context Brief**: 다운로드 페이지는 단일 index.html에 **5개 상태 섹션의 마크업을 모두 넣고** JS가 하나만 보이게 토글한다(SPA·라우터 불요, `web-architecture.md` §2). 모바일 사용 비중이 높으므로 모바일 우선 반응형(PRD §5/§8). 이 Step은 마크업 뼈대와 CSS를 만든다(미디어 바인딩·다운로드 동작은 Step 5).
- **대상 파일**: `web/public/index.html`, `web/public/styles.css`.
- **선행 조건**: Step 1. (Step 3의 `showState` id 규약과 일치 필요 — 아래 id 고정)
- **구현 내용**:
  - `index.html` `<body>`에 5개 `<section>`(초기엔 로딩만 표시, 나머지 `hidden`):
    - `#state-loading`: 스피너 + "결과물을 불러오는 중…".
    - `#state-success`: 사진 영역(`<img id="photo-preview">` + `<a id="photo-download">사진 다운로드</a>`), 영상 영역(`<video id="video-preview" controls playsinline>` + `<a id="video-download">영상 다운로드</a>`), 만료 고지(`#expiry-notice`). 영상 영역은 `#video-section`로 감싸 null 시 숨김(Step 5).
    - `#state-expired`: "보관 기간이 지나 만료되었습니다" + 재촬영 안내 문구.
    - `#state-error`: "일시적인 오류가 발생했습니다" + `<button id="retry-btn">다시 시도</button>`.
  - `<head>`: `<meta name="viewport" content="width=device-width, initial-scale=1">`, `<title>MC포토 다운로드</title>`, `<link rel="stylesheet" href="./styles.css">`, `<script type="module" src="./app.js" defer></script>`.
  - `styles.css`(모바일 우선): 기본 스타일은 모바일 폭 기준, `@media (min-width: 768px)`로 데스크톱 확장. 버튼 터치 타깃 최소 44×44px. 이미지·영상 `max-width:100%`. 세로 스크롤만(가로 스크롤 없음). 큰 대비·가독 폰트.
  - `hidden` 속성 또는 `.is-hidden` 클래스로 상태 토글(Step 3 `showState`와 규약 일치).
- **검증 명령**:
  - `cd web && npx firebase emulators:start --only hosting` → 브라우저에서 각 상태를 devtools로 강제 표시(`document.getElementById('state-success').hidden=false`)해 레이아웃 육안 확인. 모바일 뷰포트(devtools device toolbar 375px)에서 가로 스크롤 없음 확인.
  - `grep -nE "state-loading|state-success|state-expired|state-error|photo-preview|video-preview" web/public/index.html` → 필수 id 전부 존재.
- **완료 기준**:
  - [관측] 5개 상태 섹션이 index.html에 존재하고 기본은 로딩만 보임. 375px 모바일 뷰포트에서 가로 스크롤 없이 레이아웃 정상. 버튼 터치 타깃 44px 이상. 데스크톱(≥768px)에서도 깨지지 않음.
  - [non-goal] 이 단계에서 실제 미디어 URL 바인딩·다운로드 동작 **없음**(빈 img/video·placeholder). 상태 자동 전이 로직은 Step 3 소관(여기선 마크업만).
  - [trigger] 상태 전환은 Step 3의 `showState` 호출 시에만(마크업 자체는 정적).
- **롤백**: Step 4 변경 revert. index.html을 Step 1의 빈 골격으로, styles.css 삭제.
- [ ] 완료

---

## Step 5: 성공 렌더 — 미디어 프리뷰 · 다운로드 · 부분 실패 폴백

- **Context Brief**: 문서가 유효할 때 사진·영상을 표시하고 다운로드를 제공한다(`web-architecture.md` §2.2/§3.4). 파일은 문서의 `finalImageUrl`/`timelapseUrl`(다운로드 토큰 URL)을 `<img src>`/`<video src>`/`<a href>`에 **직접 바인딩**한다(VF-2, Storage SDK 미사용). timelapseUrl이 null이면 영상 영역을 숨긴다. 개별 미디어 로드 실패(고아 문서, VF-10)는 해당 영역만 실패 표시한다. iOS Safari의 cross-origin `<a download>` 제약(OA-4)에 폴백 안내를 준비한다.
- **대상 파일**: `web/public/app.js`(`renderSuccess(data)` 구현), `web/public/index.html`(폴백 안내 요소), `web/public/styles.css`(실패 상태 스타일).
- **선행 조건**: Step 3(성공 경로에서 `renderSuccess` 호출), Step 4(마크업 id).
- **구현 내용**:
  - `renderSuccess(data)`:
    - 사진: `photo-preview.src = data.finalImageUrl; photo-download.href = data.finalImageUrl; photo-download.setAttribute("download", "mcphoto.jpg");`(파일명은 표시용 힌트, 확장자는 outputFormat 무관하게 힌트여도 무방 — 실제는 서버 헤더 따름).
    - 영상: `data.timelapseUrl`이 truthy면 `video-preview.src`·`video-download.href` 설정, `#video-section` 표시. **null이면 `#video-section` 숨김**(계약 §2.3).
    - 만료 고지: `data.expiresAt.toDate()`를 사용자 로컬 시간으로 포맷해 `#expiry-notice`에 "이 사진·영상은 {시각}에 만료됩니다" 표시.
    - `showState("success")`.
  - 부분 실패(VF-10, §3.4): `photo-preview.onerror`/`video-preview.onerror` → 해당 영역에 "불러올 수 없음(만료되었을 수 있음)" 표시 + 다운로드 버튼 비활성. **둘 다 실패하면 `showState("expired")`로 폴백**.
  - iOS 폴백(OA-4): `<a download>`가 cross-origin에서 무시될 수 있으므로, 사진 영역에 "저장이 안 되면 이미지를 길게 눌러 저장하세요" 보조 안내를 상시(또는 iOS 감지 시) 노출. 영상은 기본 컨트롤의 저장 기능 안내.
  - 파일명 하드코딩 금지 원칙 유지: **URL은 문서에서만** 가져오고 경로를 조립하지 않는다(계약 §4.2).
- **검증 명령**:
  - `grep -nE "getStorage|ref\(|getDownloadURL|firebase-storage" web/public/app.js` → **매치 0건**(Storage SDK 미사용 = 계약 §4.3 준수).
  - Emulator + 시드 문서(실제 토큰 URL 또는 접근 가능한 이미지/영상 URL) 2종: (a) timelapseUrl 있음 → 사진·영상 둘 다 표시, (b) timelapseUrl=null → 영상 영역 숨김. 잘못된 URL 시드 → 개별 "불러올 수 없음" 표시(육안).
  - iOS Safari 실기/시뮬레이터에서 [사진 다운로드] 동작·폴백 안내 확인(OA-4).
- **완료 기준**:
  - [관측] 유효 문서 시 사진 표시+[사진 다운로드] 동작, timelapseUrl 있으면 영상 표시+[영상 다운로드], null이면 영상 영역 숨김. 만료 시각이 로컬 시간으로 표시. 잘못된 미디어 URL이면 해당 영역만 "불러올 수 없음", 둘 다 실패면 만료 화면.
  - [non-goal] Storage SDK(`getStorage`/`getDownloadURL`/`ref`) 사용 **없음**(grep 0건). 파일명·Storage 경로를 코드에서 조립하지 **않음**(문서 URL만). timelapseUrl null일 때 빈 영상 플레이어를 **노출하지 않음**.
  - [trigger] 렌더는 Step 3의 유효 판정 후 `renderSuccess` 호출 시에만. 부분 실패 표시는 미디어 `onerror` 이벤트 발생 시에만.
- **롤백**: Step 5 변경 revert. `renderSuccess`를 no-op으로(성공 시 빈 성공 화면).
- [ ] 완료

---

## Step 6: 보안 규칙 파일 작성 (firestore.rules / storage.rules)

- **Context Brief**: 웹 config는 공개되므로 **보안 규칙이 유일한 방어선**(PRD §10). 계약 §5.1/§5.2를 정확히 구현한다: users·frameTemplates 전면 차단, resultSessions는 단건 get만(list·write deny), Storage results는 SDK read 닫음(토큰 URL은 규칙 우회하여 동작)·frames 전면 차단. `allow read`(get+list 통합)를 쓰면 list가 열려 토큰 열거가 가능해지므로 **금지**한다.
- **대상 파일**: `web/firestore.rules`, `web/storage.rules`.
- **선행 조건**: Step 1(firebase.json이 이 파일들을 참조).
- **구현 내용**:
  - `firestore.rules`(`web-architecture.md` §6.1 그대로):
    - `rules_version = '2';`
    - `users/{uid}`: `allow read, write: if false;`
    - `frameTemplates/{fid}`: `allow read, write: if false;`
    - `resultSessions/{sid}`: `allow get: if true;` / `allow list: if false;` / `allow write: if false;` (**get·list 분리 필수, `allow read` 금지**).
    - catch-all `match /{document=**} { allow read, write: if false; }`.
  - `storage.rules`(`web-architecture.md` §6.2 그대로):
    - `results/{sessionId}/{fileName}`: `allow read: if false;`(SDK 경로 차단 — 토큰 URL은 규칙 우회) / `allow write: if false;`.
    - `frames/{userId}/{fileName}`: `allow read, write: if false;`.
    - catch-all deny.
  - 규칙 파일 상단 주석에 "WPF 서비스 계정(Admin SDK)은 규칙을 우회하므로 write:false가 WPF 생성을 막지 않음. 배포 시 WPF 규칙 준수 전환하면 인증 계정 write 조건 별도 추가"(계약 §5.1) 명시.
- **검증 명령**:
  - `cd web && npx firebase emulators:start --only firestore,storage` 기동 시 규칙 파싱 성공(문법 오류 없음) 로그 확인. 또는 `npx firebase deploy --only firestore:rules,storage:rules --dry-run`(문법 검증).
  - `grep -nE "allow read\b" web/firestore.rules` → resultSessions 항목에 `allow read`가 **없어야** 함(get/list 분리 확인). users/frames의 `allow read, write: if false`는 허용(전면 차단이므로 read 통합 무방).
- **완료 기준**:
  - [관측] 두 규칙 파일이 문법 오류 없이 파싱됨(Emulator 기동/ dry-run 통과). resultSessions에 `allow get`·`allow list: if false` 분리 존재, `resultSessions`에 통합 `allow read` **부재**.
  - [non-goal] resultSessions에 write allow·list allow **없음**. results/ Storage에 read allow **없음**(토큰 URL 우회로 다운로드는 정상). 규칙 밖 경로 기본 차단.
  - [trigger] 규칙 적용은 배포(Step 9) 시. 이 단계는 파일 작성·문법 검증까지.
- **롤백**: Step 6 변경 revert. 규칙 파일을 전면 deny(`allow read, write: if false` 단일)로 두면 웹 다운로드가 막히지만 안전(임시).
- [ ] 완료

---

## Step 7: Emulator 보안 규칙 테스트 (자동화)

- **Context Brief**: 규칙이 계약대로 동작하는지 코드로 검증한다(계약 §5.3). `@firebase/rules-unit-testing`으로 Firestore/Storage Emulator에 대해 웹(비인증) 관점의 allow/deny를 단정한다. 이 테스트는 규칙 회귀(특히 list가 실수로 열리는 것, WR3)를 막는 안전망이다.
- **대상 파일**: `web/tests/rules.test.js`, `web/package.json`(test script 추가).
- **선행 조건**: Step 6(규칙 파일).
- **구현 내용**:
  - `rules.test.js`(`@firebase/rules-unit-testing` `initializeTestEnvironment`):
    - 시드: Admin(규칙 우회) 컨텍스트로 `resultSessions/{validToken}` 1건, `users/{u}` 1건, `frameTemplates/{f}` 1건 생성.
    - 비인증 컨텍스트로 다음 단정(`web-architecture.md` §6.3 a~g):
      - (a) `getDoc(users/{u})` → `assertFails`
      - (b) `getDoc(frameTemplates/{f})` → `assertFails`
      - (c) `getDocs(collection(resultSessions))`(list) → `assertFails`
      - (d) `getDoc(resultSessions/{validToken})` → `assertSucceeds`
      - (e) `setDoc(resultSessions/{sid})`(write) → `assertFails`
      - (f) Storage `getBytes(ref(results/…))` SDK read → `assertFails`
      - (g) Storage `getBytes(ref(frames/…))` → `assertFails`
  - `package.json` scripts: `"test:rules": "firebase emulators:exec --only firestore,storage \"node tests/rules.test.js\""` (또는 vitest/mocha 러너 연동).
- **검증 명령**:
  - `cd web && npx firebase emulators:exec --only firestore,storage "node tests/rules.test.js"` → 전 케이스 PASS(비정상 종료 코드 0).
- **완료 기준**:
  - [관측] 7개 시나리오(a~g) 전부 기대대로 PASS: users/frames get deny, resultSessions list deny·write deny, resultSessions 단건 get allow, Storage results/frames SDK read deny. 테스트 프로세스 exit 0.
  - [non-goal] 테스트가 실제 배포 프로젝트가 아닌 **Emulator**에서만 동작(비용·부작용 없음). resultSessions 단건 get이 deny로 떨어지면 **실패**(웹 기능 자체가 막힘 — 회귀).
  - [trigger] 테스트는 `emulators:exec`/`test:rules` 실행 시에만.
- **롤백**: Step 7 변경 revert(테스트 파일 삭제). 규칙 자체(Step 6)는 유지.
- [ ] 완료

---

## Step 8: TTL 정리 인프라 문서화 (GCS Lifecycle + Firestore TTL 권장)

- **Context Brief**: 결정 D-2에 따라 웹은 스케줄 Functions로 TTL 정리를 **하지 않는다**(`web-architecture.md` §7). 삭제 1차는 WPF 직접 삭제, 안전망은 GCS Lifecycle이다(계약 §6.2). 웹 측 산출물로는 이 인프라 설정 절차를 **운영 문서화**하고, 고아 문서 최소화를 위한 Firestore 네이티브 TTL을 **권장(선택)**으로 안내한다. 웹 페이지 코드 변경은 없다.
- **대상 파일**: `web/OPS-ttl.md`(운영 절차 문서), `web/lifecycle.json`(GCS Lifecycle 설정 예시).
- **선행 조건**: Step 1(프로젝트 구조). (Blaze 전환은 계약 전제 — Storage용, 외부 준비)
- **구현 내용**:
  - `lifecycle.json`: `results/` 프리픽스에만 age 기반 삭제 규칙(예: age 3일 — retentionHours 최대 72h보다 여유). **`frames/`·로컬 저장분은 대상 아님**(계약 §6.3 불변식). 예:
    ```json
    { "rule": [ { "action": {"type":"Delete"},
                 "condition": {"age": 3, "matchesPrefix": ["results/"]} } ] }
    ```
  - `OPS-ttl.md`:
    - GCS Lifecycle 적용: `gsutil lifecycle set lifecycle.json gs://{bucket}` (또는 콘솔). **results/ 한정·frames/ 제외** 명시.
    - Firestore 네이티브 TTL(권장, 선택): 콘솔/`gcloud firestore fields ttls update expiresAt --collection-group=resultSessions --enable-ttl`로 `expiresAt` 필드 TTL 정책 설정 → 고아 문서 자동 축소(§7.3). 무료·서버리스·Functions 불요.
    - 불변식 재기재: (1) results/만, (2) frames/·로컬 비대상, (3) 문서+파일 함께 정리 지향.
    - 웹은 삭제 미수행 — 만료/부재를 `expiresAt<now`·not-found로 판정해 안내만(§3.3/§3.4).
- **검증 명령**:
  - `python -c "import json,sys; json.load(open('web/lifecycle.json'))"` → 유효 JSON(exit 0).
  - `grep -nE "matchesPrefix|results/" web/lifecycle.json` → results/ 프리픽스 한정 확인. `grep -n "frames" web/lifecycle.json` → **매치 0건**(frames 미포함 확인).
  - `OPS-ttl.md`에 GCS Lifecycle·Firestore TTL 절차·불변식이 모두 기재됐는지 육안.
- **완료 기준**:
  - [관측] `lifecycle.json`이 유효 JSON이며 `results/` 프리픽스에만 Delete 규칙, `frames` 미포함. `OPS-ttl.md`에 GCS Lifecycle 적용 명령·Firestore TTL 권장 절차·계약 §6.3 불변식이 기재됨.
  - [non-goal] 스케줄 Cloud Functions 코드·배포 **없음**(D-2). Lifecycle이 `frames/`·로컬 저장분을 대상으로 삼지 **않음**. 웹 페이지 코드(app.js 등) 변경 **없음**.
  - [trigger] Lifecycle/TTL 실제 적용은 운영자가 명령 실행 시(문서는 절차만 제공).
- **롤백**: Step 8 변경 revert(문서·예시 삭제). 웹 기능에 영향 없음.
- [ ] 완료

---

## Step 9: 배포 & end-to-end 스모크

- **Context Brief**: 규칙·페이지를 실제 Firebase 프로젝트(Spark 무료, Storage만 Blaze 전제)에 배포하고, WPF가 생성한(또는 시드한) 실제 ResultSession 토큰으로 전체 흐름을 검증한다. Hosting 배포 도메인이 WPF `hostingBaseUrl`과 일치해야 QR이 올바른 URL을 가리킨다(OA-5). 실제 다운로드 토큰 URL이 모바일 브라우저에서 CORS 없이 표시·다운로드되는지 확인한다(OA-3).
- **대상 파일**: (배포만 — 코드 변경 없음). `web/.firebaserc`·`web/public/firebase-config.js`의 프로젝트 값 확정.
- **선행 조건**: Step 1~8 전부. Firebase 프로젝트 접근 권한(로그인), 대상 프로젝트 Blaze 전환(Storage용, 계약 전제).
- **구현 내용**:
  - `firebase-config.js`·`.firebaserc`의 projectId·config를 실제 대상 프로젝트로 확정(OA-1).
  - 배포: `cd web && npx firebase deploy --only hosting,firestore:rules,storage:rules`.
  - 배포 도메인(`https://{project}.web.app`) 확인 → **WPF `AppSettings.hostingBaseUrl`과 대조**(OA-5). 불일치 시 WPF 설정 교체 필요 사항으로 보고(웹은 도메인 고정).
  - 스모크(실제 또는 시드 세션):
    - 유효 토큰 URL(`https://{domain}/?s={token}`)을 모바일 브라우저로 열기 → 사진·영상 표시, 다운로드 동작(OA-3/OA-4).
    - 만료/없는 토큰 → 만료 안내.
    - `?s=` 없음 → 오류/안내.
    - (선택) QR을 실제 스캔해 진입 경로 확인.
- **검증 명령**:
  - `cd web && npx firebase deploy --only hosting,firestore:rules,storage:rules` → 배포 성공(Deploy complete).
  - `curl -sS -o /dev/null -w "%{http_code}" "https://{project}.web.app/?s=nonexistent"` → 200(만료 화면도 200 페이지). (라우팅·서빙 확인)
  - 모바일 브라우저로 유효 토큰 URL 접속 → 사진 표시·다운로드 육안(OA-3/OA-4). devtools Network에서 firebasestorage 토큰 URL 200·CORS 오류 없음 확인.
- **완료 기준**:
  - [관측] 배포 완료 후 유효 토큰 URL에서 사진·영상 표시+다운로드 동작, 만료/없는 토큰은 만료 안내, `?s=` 누락은 오류/안내. 배포 도메인이 WPF hostingBaseUrl과 일치. 다운로드 토큰 URL이 CORS 오류 없이 로드됨.
  - [non-goal] 배포에 서비스 계정 키·비밀 **미포함**(웹은 공개 config만). users/frames가 웹에서 여전히 접근 **불가**(규칙 적용 확인 — Step 7이 사전 보증). QR off 운영에선 이 페이지가 호출되지 않음(범위 밖).
  - [trigger] 배포는 `firebase deploy` 실행 시. 페이지 동작은 유효 토큰 URL 접근 시.
- **롤백**: 직전 배포로 롤백(`firebase hosting:rollback`) 또는 규칙을 전면 deny로 재배포(안전). 코드 변경 없으므로 로컬 롤백 불요.
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~8) / 미검증 가정(OA-1~5) 목록 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→Step2, OA-2→Step3/7, OA-3→Step4/9, OA-4→Step5, OA-5→Step8/9)
- [x] 모든 Step(1~9)에 7개 필수 필드(Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백) 채워짐
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step(4/5)·규칙 Step(3/6/7)에 non-goal·trigger 포함
- [x] 검증 명령이 자동 실행 가능 형태(`firebase emulators`/`emulators:exec`/`deploy`/`grep`/`curl`/`node`)
- [x] 계약 불변식(단건 get·Storage SDK 미사용·get/list 분리·Functions 미채택)이 각 Step 검증에 grep/테스트로 강제됨

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally` / `committed` / `pushed` / `blocked`(사유 명시 필수)
