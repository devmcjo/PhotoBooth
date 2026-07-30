# it17 설계 — 웹 다운로드 페이지: 원클릭 자동 저장 + 링크 공유 + MCPhoto 네이밍

> 프로젝트 루트: `C:\STUDY\PROJECT\PhotoBooth`
> 대상: `web/public/*`(정적 HTML/CSS/Vanilla ESM). **백엔드(`web/functions`)·WPF 코드 변경 0. 인프라 변경 0**(버킷 CORS 는 §3.5 실측으로 불필요 판정).
> 입력: 사용자 요구 3건(§0.1), 현행 코드(§1 `file:line`).
> 산출물 소비자: `js-developer` — §10 WBS를 순서대로 실행한다. `js-code-reviewer` — §9 테스트 계획으로 검증한다.
> 동일 이터레이션 병행 문서: [`wpf-it17-auto-cutcount-design.md`](./wpf-it17-auto-cutcount-design.md) (파일 교집합 없음, 완전 병렬).

---

## §0 개요

### 0.1 요구사항 (사용자 원문 → 분해)

> QR코드 촬영해서 모바일에서 웹페이지에 다운로드 버튼 누르면, 사진이나 동영상의 원본을 보여주고, 해당 사진/동영상을 롱프레스해서 저장해야만하는데, 버튼 클릭 시 자동으로 로컬에 다운 받아지도록 해줘.
> 그리고, 상단에 공유 버튼도 생성해줘.(링크 복사되게 하고, 링크 복사되었다고 토스트 메시지 노출)
> 웹페이지에 MC포토라고 이름이 노출되는데, MCPhoto로 변경해줘.

| # | 갈래 | 요구 | 그룹 |
|---|------|------|------|
| 1 | 자동 저장 | 다운로드 버튼 1탭으로 로컬 저장. 현재는 원본이 열리기만 해서 롱프레스가 필요하다 | **G1** |
| 2 | 공유 버튼 | 상단에 공유 버튼. 현재 페이지 링크를 클립보드에 복사 + "복사되었습니다" 토스트 | **G2** |
| 3 | 네이밍 | 웹페이지 노출 문자열 "MC포토" → "MCPhoto" | **G3** |

- G1·G2·G3는 같은 3개 파일(`index.html`·`app.js`·`styles.css`)을 만지지만 **변경 지점이 서로 겹치지 않는다**(§8.1 라인 맵). 단계별 커밋 가능.
- G3는 **웹페이지 노출 문자열로 범위를 한정**한다. WPF·인스톨러·문서의 "MC포토"는 이번 범위가 아니다(발견 사항은 §7.3에 보고).

### 0.2 기술 스택 (변경 없음)

- 프레임워크·번들러·트랜스파일러 **없음**. 정적 HTML + CSS + Vanilla ESM(`<script type="module">`).
- Firebase JS SDK는 gstatic CDN 모듈러 import, 버전 고정 `v12.16.0`(`app.js:12-18`). **이번 작업으로 import를 추가하지 않는다** — Storage SDK·Auth·Analytics 모두 불필요.
- 새 런타임 의존성 0. 새 npm 패키지 0. 빌드 스텝 0.

### 0.3 범위 경계 (명시적 non-goal)

| 하지 않는 것 | 이유 |
|---|---|
| 진행률(%) UI | 대상 파일이 단위 MB(§3.7)라 "저장 중…" 라벨로 충분. 스트리밍 리더 도입은 diff를 키우고 리뷰 표면을 넓힌다 |
| `navigator.share` (Web Share API) | 사용자 요구는 **링크 복사**다. 파일 공유(`share({files})`)는 iOS 사진 앱 저장의 유일한 경로지만 공유 시트에서 사용자가 한 번 더 골라야 해 "자동"이 아니다 → §3.9 이연 |
| 사진 앱/갤러리 직접 저장 | 브라우저 권한 모델상 불가(§3.4 각주). `<a download>`는 파일 시스템(Files/Downloads)까지만 |
| 백엔드 신규 엔드포인트(attachment 서명 URL) | §3.2 Option A′ — CSP·API 키 노출·배포가 붙는다. 이연(§11.2) |
| `<img>`/`<video>`에 `crossorigin` 속성 추가 | **금지.** 프리뷰 로드를 CORS 요청으로 바꿔버린다. 현재 정상 동작하는 프리뷰(no-cors 서브리소스)를 CORS 응답 조건에 묶을 이유가 없다(하드 회귀 위험) |
| 폴백 상태의 `sessionStorage` 영속화 | CORS가 세션 중에 고쳐졌을 때 stale `off` 플래그가 자동 저장을 계속 억제한다. 인메모리 플래그로 한정 |
| 서비스 워커·오프라인 | 페이지 성격(일회성 capability URL)과 무관 |
| WPF·인스톨러·`docs/**`의 "MC포토" | 범위 외(§7.3 보고만) |

### 0.4 무회귀 하한 (현행 실측 기준)

| 검증 | 하한 | 근거 |
|------|------|------|
| `node --check web/public/app.js` | 오류 0 | ESM 구문 게이트(`web/package.json:6` `"type":"module"`) |
| `npm run test:rules` (cwd=`web`) | 전량 통과 | 이번 작업은 규칙을 건드리지 않는다 → **불변** |
| `rg -n "innerHTML" web/public/` | 무매치 | XSS 하한(§5.2) |
| `rg -n "MC포토" web/public/` | 무매치(작업 후) | G3 완료 조건 |
| 상태 머신 4종(loading/success/expired/error) | 전이 규칙 불변 | `app.js:28-35` — 이번 작업은 상태를 **추가하지 않는다** |

---

## §1 검증된 사실 (verified facts — 코드 직접 확인)

### 1.1 현행 다운로드 경로

| # | 사실 | 근거 |
|---|------|------|
| **VF-1** | 다운로드 버튼은 `<a>` 이며 토큰 URL을 `href`에 직접 바인딩한다. `download` 속성은 하드코딩된 상수(`mcphoto.jpg`/`mcphoto.mp4`)다 | `web/public/index.html:37,61`, `web/public/app.js:124-126,157-159` |
| **VF-2** | `<a download>`가 cross-origin에서 **전 브라우저가 무시한다**는 점은 코드에 이미 주석으로 명시돼 있고(iOS 한정이 아님), 그래서 "길게 눌러 저장" 힌트가 **상시 노출** 중이다 | `web/public/app.js:177-179`, `index.html:40-42,64-66` |
| **VF-3** | 프리뷰 실패 시(`onerror`) 버튼은 `href` 제거 + `aria-disabled` + `.is-disabled`로 비활성화된다 | `app.js:115-123,148-156`, `styles.css:226-231` |
| **VF-4** | 토큰 URL 형식은 `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={uuid}` (슬래시 `%2F`) | `web/functions/src/domain/session.ts:67-74` |
| **VF-5** | 이 URL은 **백엔드 `prepare`가 발급**하고 `commit`이 `assertUrlBelongsToSession`으로 버킷 prefix·세션 경로를 강제 검증한다. 웹은 URL 생성 주체가 아니며 형식을 바꿀 권한이 없다 | `web/functions/src/services/signing.ts:63-100`, `web/functions/src/services/uploads.ts:129-152` |
| **VF-6** | 최종 이미지 확장자는 `outputFormat`에 따라 `.jpg` **또는** `.png`인데, 페이지는 `download="mcphoto.jpg"`로 하드코딩되어 있다 → **현행 버그** | `web/functions/src/domain/session.ts:52-55`, `index.html:37`, `app.js:126` |
| **VF-7** | `?s=` 토큰 = `resultSessions` 문서 ID = sessionId = `{yyyyMMdd}_{HHmmss}_{UUIDv4}`. 즉 페이지는 **파일명에 쓸 촬영 시각을 이미 손에 들고 있다** | `web/functions/src/domain/session.ts:45-50`, `docs/design/firebase-contract.md:112,139,151` |
| **VF-8** | 토큰/URL에 생성 시각이 노출되는 것은 계약이 **이미 수용한 트레이드오프**다 → 파일명에 시각을 넣어도 신규 정보 노출이 아니다 | `docs/design/firebase-contract.md:152` |

### 1.2 현행 페이지 구조·보안 헤더

| # | 사실 | 근거 |
|---|------|------|
| **VF-9** | 현행 CSP `connect-src`에 `https://firebasestorage.googleapis.com`이 **이미 포함**되어 있다 → `fetch()` 추가에 CSP 변경 불요 | `web/firebase.json:12` |
| **VF-10** | `web/public`에 토스트·스낵바 류 컴포넌트가 **없다**. `.media__error`·`.media__optout`·`.notice`는 모두 정적 인라인 텍스트 블록이며 전이·타이머·live region이 없다 | `index.html` 전문(98줄), `styles.css:172-194,233-239` 전문 확인 |
| **VF-11** | 헤더는 `<h1>` 단일 자식이며 `text-align:center`다. 버튼을 넣을 슬롯이 없다 | `index.html:13-15`, `styles.css:59-69` |
| **VF-12** | 웹에 **노출되는** "MC포토"는 정확히 3곳이다. `manifest.json`·favicon·`og:`/`meta description` 태그는 **존재하지 않는다**(`<head>`는 charset·viewport·color-scheme·title뿐) | `index.html:3-10`(title 7), `index.html:14`(H1), `index.html:94`(footer) |
| **VF-13** | 웹은 읽기 전용 소비자다 — Storage SDK·Auth 미import, `resultSessions` 단건 `getDoc`만. `results/`의 Storage SDK read는 `false`로 닫혀 있고, 토큰 URL이 규칙을 우회해 동작한다 | `app.js:1-19,195`, `web/storage.rules:16-19` |
| **VF-14** | `web/public`에는 **JS 린터·번들러·단위 테스트 하네스가 없다**. 자동 게이트는 `node --check`(ESM 구문)와 `web/tests/rules.test.js`(Emulator 규칙 테스트)뿐이다 | `web/package.json:7-11`, `web/tests/` = `rules.test.js` 1개 |
| **VF-15** | Hosting 기본 도메인 = `https://mcphoto-955fb.web.app` (WPF `HostingBaseUrl` 기본값과 일치), project = `mcphoto-955fb`, bucket = `mcphoto-955fb.firebasestorage.app` | `src/MCPhoto.Core/Settings/AppSettings.cs:123`, `web/.firebaserc`, `web/firebase.json:32` |

### 1.3 용량·인프라

| # | 사실 | 근거 |
|---|------|------|
| **VF-16** | 타임랩스는 **최대 12.5초** 길이(배속 산출 표), 1080p·30fps·H.264 CRF20 → 실용상 단위 MB | `docs/analysis/14-media-pipeline-spec.md:339-342,366-370` |
| **VF-17** | 프레임 이미지 입력 상한 10MB·장변 4000px → 최종 합성 이미지도 같은 자릿수 | `docs/analysis/14-media-pipeline-spec.md:250-251,403` |
| **VF-18** | 리포지토리에 **버킷 CORS 구성 파일이 없다** — `web/`에는 `lifecycle.json`만 있다. 로드맵 **B5(버킷 CORS)는 "대기"** 상태이며, 기재된 용도는 브라우저 **PUT**(업로드)이다. (GET 판정은 §3.5 에서 **실측으로 확정**했다 — 버킷 설정 불필요) | `web/` 파일 목록, `docs/analysis/90-roadmap-and-future-work.md:133,156`, `docs/analysis/05-cross-platform-client-guide.md:325` |
| **VF-19** | 설계 문서(`web-architecture.md:434` WR2)는 "Firebase 토큰 URL은 공개 GET 허용(**기대**)"이라고 적혀 있다 — 즉 CORS GET은 **당시에도 미확인 가정**이었고, 검증된 것은 `<img>/<video>` 표시(no-cors)뿐이다 | `docs/design/web-architecture.md:35,434`, `docs/design/web-wbs.md:31,296-298` |

> ⚠️ **VF-19가 이 설계의 핵심 리스크다.** 지금까지 검증된 것은 `<img src>`/`<video src>`의 **no-cors 서브리소스 로드**다. `fetch()`는 **cors 모드**라서 응답에 `Access-Control-Allow-Origin`이 필요하고, 이는 전혀 다른 조건이다. §3.5·§3.8이 이 리스크를 다룬다.

---

## §2 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| ~~**OA-1**~~ | ~~버킷의 현재 CORS 구성 상태(GET 허용 여부)~~ | ✅ **해소(2026-07-30 실측, §3.5)** — 버킷 레벨 CORS는 **미설정**이지만, 다운로드 호스트가 서비스 레벨에서 `ACAO: *`를 반환하므로 **버킷 설정이 불필요**하다 |
| **OA-2** | `firebasestorage.googleapis.com` `alt=media` **200 응답**을 브라우저가 cors 모드 fetch로 읽을 수 있다 | **부분 해소** — 403 응답에서 `ACAO: *` 관측(§3.5). **200 확인은 Step W7 잔여** |
| **OA-3** | `blob:` URL을 `href`로 갖는 `<a download>` 클릭이 현행 CSP(`default-src 'self'`)에서 위반 없이 동작한다 | **Step W7** (콘솔 CSP 위반 0 관측) |
| **OA-4** | iOS Safari가 blob `<a download>`에 다운로드 확인 시트를 띄우고 Files 앱에 저장한다 | **Step W7** |
| **OA-5** | 인앱 브라우저(카카오톡·네이버 앱 등)에서의 동작. **불가해도 폴백 경로(§3.3-D)가 동작하면 PASS** | **Step W7** |
| **OA-6** | 응답에 `Content-Length`가 존재한다(용량 가드 발동 조건). 없어도 가드는 무동작으로 안전하게 설계한다 | **Step W7** |
| **OA-7** | 응답 `Content-Type`이 정확하다(파일명 확장자 **2차** 소스). URL 경로 확장자가 1차라 없어도 무영향 | **Step W7** |
| **OA-8** | `await` 이후의 `a.click()`이 사용자 활성화(user activation) 만료로 차단되지 않는다 | **Step W7** (차단되면 §3.3-D 폴백으로 degrade) |

> 모든 가정이 검증 단계에 매핑됨(완결성 게이트 통과). **OA-1~OA-8이 전부 실패해도 페이지는 현행 동작으로 degrade한다**(§3.3-D) — 이것이 이 설계의 안전 속성이다.

---

## §3 쟁점 1 — 모바일 자동 다운로드 (핵심 판정)

### 3.1 왜 지금은 안 되는가

```
사용자 탭 → <a href="https://firebasestorage.googleapis.com/...?alt=media&token=..." download="mcphoto.jpg">
                                    │
                    브라우저: href 가 cross-origin 이다
                                    │
                          → download 속성 무시
                                    │
                       → 그냥 그 URL 로 내비게이션
                                    │
              이미지: 브라우저에 표시됨 → 사용자가 롱프레스해야 저장
              영상  : 재생되거나 다운로드 (엔진·플랫폼별로 다름)
```

`download` 속성은 **same-origin** 또는 `blob:`/`data:` 스킴에서만 유효하다. 이 제약은 iOS 한정이 아니라 전 브라우저 공통이며, 코드 주석(`app.js:177-179`)에 이미 정확히 기술돼 있다.

따라서 자동 저장을 성립시키는 길은 원리적으로 두 개뿐이다.

1. **`href`를 same-origin/`blob:`으로 만든다** → 바이트를 JS로 가져와 `blob:` URL을 만든다 (⇒ CORS 필요)
2. **서버가 `Content-Disposition: attachment`를 보낸다** → 내비게이션 자체가 다운로드가 된다 (⇒ URL 발급 주체 변경 필요)

### 3.2 후보 비교

| | **Option A**<br>서명 URL `response-content-disposition` | **Option A′**<br>attachment 전용 백엔드 엔드포인트 | **Option B**<br>객체 메타 `contentDisposition` | **Option C**<br>fetch → Blob → `<a download>` |
|---|---|---|---|---|
| 웹 코드만으로 가능 | ✗ | ✗ | ✗ | **○** |
| 계약(`firebase-contract` §4.3) 변경 | **필요** — URL 형식 동결 해제 | 불필요(URL 추가) | 필요(prepare 서명 헤더 추가) | 불필요 |
| WPF 변경 | **필요** | 불필요 | **필요**(PUT 헤더 추가) | 불필요 |
| 백엔드 변경 | 필요 | **필요**(신규 라우트) | 필요 | 없음 |
| 인프라 선행 | 없음 | 없음 | 없음 | ~~버킷 CORS(GET)~~ → **없음**(§3.5 실측: 다운로드 호스트가 서비스 레벨 `ACAO: *`) |
| CSP 변경 | 없음 | **필요**(`connect-src` += Functions 도메인) | 없음 | 없음(VF-9) |
| 기존 진행 중 세션(최대 72h)에 소급 적용 | ✗ | ○ | ✗ | **○** |
| 프리뷰(inline)와 저장(attachment)이 같은 URL을 공유하는 문제 | URL 2개로 분리해야 함 | 없음(URL 분리됨) | **충돌** — 한 객체에 하나의 disposition | 없음 |
| 추가 전송량 | 없음 | 없음 | 없음 | **프리뷰 + 저장 = 2회** |

**판정: Option C 채택.** 결정 근거:

1. **Option A/B는 URL 발급 주체를 건드린다.** `finalImageUrl`/`timelapseUrl`은 백엔드 `prepare`가 만들고 `commit`이 `assertUrlBelongsToSession`으로 prefix까지 강제 검증한다(VF-5). 웹은 읽기 전용 소비자(VF-13)로서 **URL 형식을 바꿀 권한이 없다.** 요구는 명시적으로 웹페이지 범위였는데, A/B는 WPF + 백엔드 + 계약 문서 + iOS/Android 이식 규격(`docs/analysis/13 §12`)까지 파급된다.
2. **Option B는 프리뷰를 담보로 잡는다.** 한 객체에 disposition은 하나다. `attachment`를 심으면 같은 URL이 `<img src>`에도 쓰인다. 서브리소스 로드는 통상 `Content-Disposition`을 무시하므로 "아마 프리뷰는 그대로 뜬다"지만, **지금 정상 동작하는 프리뷰를 "아마"에 걸 이유가 없다.**
3. **Option A/B는 소급되지 않는다.** 업로드 시점에 결정되므로 이미 커밋된 세션(retention 최대 72h)은 옛 동작을 유지한다 → 롤아웃 기간 동안 사용자별로 동작이 갈린다.
4. **Option A′는 유효한 대안이지만 비용이 크다.** `uploads` 라우터는 `requireApiKey()` 뒤에 있어(`web/functions/src/routes/uploads.ts:18`) 공개 웹 페이지가 호출하려면 API 키를 번들에 노출하거나 무인증 공개 라우트를 신설해야 하고, CSP `connect-src`에 Functions 도메인을 추가해야 하며(`docs/analysis/20:15`가 경고하는 지점), 배포 대상이 Hosting에서 Functions로 넓어진다. → **이연**(§11.2). Option C가 CORS 때문에 실패로 판정되면 이때 꺼낸다.
5. **Option C는 순수 클라이언트 변경이고 즉시 소급된다.** 유일한 선행 조건으로 상정했던 버킷 CORS(GET)는 **§3.5 실측으로 불필요 판정** → 인프라 선행 조건이 **0**이다. 그럼에도 다른 원인(인앱 브라우저·구형 엔진·네트워크·비2xx)으로 실패하면 §3.3-D로 **현행 동작 그대로 degrade**한다.

> 참고: Option A의 `response-content-disposition`은 **GCS V4 서명 URL(`storage.googleapis.com`)의 쿼리 파라미터**다. 현재 저장되는 URL은 Firebase Storage v0 REST 엔드포인트(`firebasestorage.googleapis.com/v0/...?alt=media&token=`)이며, 이 엔드포인트는 객체에 저장된 `contentDisposition` 메타를 서빙하는 방식이다(= Option B). 즉 **현재 URL에 쿼리 파라미터를 덧붙이는 방식은 성립하지 않는다.** 다만 A를 기각하는 결정적 이유는 위 1~3항(계약 파급·프리뷰 충돌·비소급)이며, 이 엔드포인트 세부는 판정에 필요하지 않다.

### 3.3 채택안 D1 — 자동 저장 + graceful degrade

핵심 설계: **자동 저장은 "능력(capability)"으로 취급한다.** 능력이 있으면 쓰고, 없다고 판정되면 페이지 전체를 현행 동작으로 되돌린 뒤 다시 시도하지 않는다.

```
                       ┌─ (A) 기능 감지 ─┐
페이지 로드 ──────────→ │ 'download' in HTMLAnchorElement.prototype
                       │ && typeof URL.createObjectURL === 'function'
                       │ && typeof fetch === 'function'
                       └─────────┬───────┘
                    ✗ 미지원      │      ○ 지원
        autoDownloadEnabled=false │      autoDownloadEnabled=true
        수동 힌트 상시 노출        │      수동 힌트 숨김
        <a> 클릭 미개입           │      <a> 클릭 개입
        = 현행 동작 그대로         │
                                 ▼
                       ┌─ (B) 클릭 개입 ─┐
                       │ ev.preventDefault()
                       │ 버튼 busy("저장 중…", aria-busy, 재진입 차단)
                       │ fetch(url, {mode:'cors', credentials:'omit'})
                       └─────────┬───────┘
                       ○ 성공     │     ✗ 실패(CORS/네트워크/비2xx/용량초과)
                                 │
          ┌──────────────────────┘
          ▼ (C) 성공 경로                      ▼ (D) 실패 경로 = 전역 degrade
  Content-Length 가드 통과                 autoDownloadEnabled = false  ← 두 미디어 모두
  blob = await res.blob()                 수동 힌트 두 카드 모두 노출
  objectUrl = createObjectURL(blob)        토스트(warn) 노출
  임시 <a download={파일명}> 생성·click     location.assign(url)  ← 현행 동작 실행
  토스트("저장을 시작했습니다. …")            (이후 클릭은 개입 없는 순수 링크)
  수동 힌트 노출(저장 실패 대비)
  60s 후 revokeObjectURL
```

**(D) 실패 경로의 설계 근거 (중요)**

- 실패는 **결정론적·전역적**인 성격이 강하다. 환경이 원인이면(인앱 브라우저의 `download` 미동작, 구형 엔진, CSP 차단) 첫 클릭부터 마지막 클릭까지 전부 실패한다. 그러므로 **클릭마다 재시도하는 설계는 사용자 시간과 데이터를 낭비한다.** 첫 실패에서 능력을 내리고 페이지 전체를 현행 UX로 되돌린다.
- ⚠️ **CORS 는 더 이상 실패 요인이 아니다**(§3.5 실측으로 해소). 그러나 **이 폴백 경로는 그대로 유지한다** — 인앱 브라우저·구형 엔진·네트워크 실패·비2xx(토큰 만료·TTL 삭제)·용량 초과·사용자 활성화 만료가 여전히 남아 있다. CORS 해소를 이유로 degrade 를 제거하면 이 경로들이 **조용한 실패**가 된다.
- 폴백 내비게이션은 **`location.assign(url)`을 쓴다. `window.open`은 쓰지 않는다** — `await` 이후에는 사용자 활성화가 만료돼 팝업 차단에 걸린다. `location.assign`은 차단 대상이 아니며, `<a target>` 없는 현행 동작(같은 탭 내비게이션)과 **정확히 동일**하다.
- 즉 **최악의 경우가 현행 동작**이다. 회귀가 없다.

**재진입·상태 관리**

- 미디어별 `inflight` 플래그로 이중 클릭을 차단한다. busy 중 클릭은 `preventDefault()`만 하고 반환.
- busy 표현: `a.setAttribute('aria-busy','true')` + 라벨 텍스트를 "저장 중…"으로 교체 + `.is-busy` 클래스(포인터 이벤트 차단). 원래 라벨은 `dataset.idleLabel`에 보관해 복원한다.
- **`.is-disabled`(프리뷰 로드 실패)와 혼동하지 않는다.** `href`가 없으면(VF-3 경로) 핸들러는 즉시 반환한다.

**요청 형태 (preflight 회피 — 반드시 지킬 것)**

```js
fetch(url, { mode: 'cors', credentials: 'omit', signal: ac.signal })
```

- **커스텀 요청 헤더를 절대 추가하지 않는다.** GET + 안전 목록 헤더만 쓰면 simple request가 되어 `OPTIONS` preflight가 발생하지 않는다. 헤더를 하나라도 붙이면 preflight가 생기고, CORS 구성에 `OPTIONS`/`responseHeader`가 없으면 실패한다.
- `credentials: 'omit'`으로 `Access-Control-Allow-Credentials` 요구를 피한다.
- `cache` 옵션을 **지정하지 않는다**(브라우저 기본에 맡긴다). `no-store`로 강제 우회할 이유가 없다.

**용량 가드**

```js
const len = Number(res.headers.get('content-length'));
if (Number.isFinite(len) && len > MAX_AUTO_DOWNLOAD_BYTES) throw new Error('too-large');
```

`MAX_AUTO_DOWNLOAD_BYTES = 150 * 1024 * 1024`. `Content-Length`가 없으면 `NaN` → `Number.isFinite`가 false → **가드는 무동작**(OA-6 실패에 안전). 헤더는 응답 바디 이전에 도착하므로 이 검사는 `.blob()` **앞에서** 수행해 메모리 적재 자체를 막는다.

### 3.4 플랫폼 동작 매트릭스 (과장 없이)

CORS(OA-1/OA-2)가 성립한 경우:

| 플랫폼 | 자동 저장 | 저장 위치 | 사용자 조작 수 | 비고 |
|---|:-:|---|:-:|---|
| **Android Chrome** (현행 QR 스캔 기본 동선) | ○ | `Download/` + 다운로드 알림 | 1탭 | 갤러리(MediaStore) 인덱싱이 즉시가 아닐 수 있다 → 사용자가 "사진 앱에 없다"고 느낄 여지 |
| **iOS Safari 13+** | ○ | **Files 앱 > 다운로드** | 1탭 + iOS 다운로드 확인 시트 1탭 | **사진 앱(카메라 롤)에는 저장되지 않는다** — 아래 각주 |
| **iOS Safari < 13** | ✗ | — | 폴백 | `download` 속성 미지원 → §3.3-A 기능 감지에서 걸러진다 |
| **데스크톱 Chrome / Edge / Firefox / Safari** | ○ | 기본 다운로드 폴더 | 1클릭 | 이견 없음 |
| **인앱 브라우저**(카카오톡·인스타그램·네이버 앱 등) | **불확실** | — | 폴백 | `download` 지원이 엔진·앱 버전별로 갈린다. 감지에서 통과하고도 실제 저장이 안 될 수 있다 → **§3.3-D 폴백 + 수동 힌트가 최종 안전망** |

> **각주 — iOS에서 사진 앱 저장은 브라우저로 불가능하다.** `<a download>`는 파일 시스템(Files 앱)까지만 도달한다. 카메라 롤에 넣는 유일한 웹 경로는 `navigator.share({ files: [...] })`로 iOS 공유 시트를 띄우고 사용자가 "이미지 저장"을 고르는 것이며, 이는 **사용자 추가 선택이 필수**라 "버튼 클릭 시 자동"이라는 요구를 만족하지 못한다. 따라서 이번 범위에서 제외하고 §11.2에 이연한다. **"iOS에서도 사진 앱에 자동 저장된다"고 사용자에게 말해서는 안 된다.**

> **각주 — 인앱 브라우저를 무시할 수 없는 이유.** 카메라 앱 QR 스캔은 Safari/Chrome을 열지만, 링크가 카카오톡으로 재공유되면(이번에 추가하는 공유 버튼이 정확히 그 동선을 만든다) 수신자는 인앱 브라우저에서 페이지를 연다. 그래서 **수동 힌트를 삭제하지 않고 "첫 시도 후 노출"로 유지**한다(§3.6).

### 3.5 인프라 선행 조건 — **해소됨: 버킷 CORS 설정 불필요** (2026-07-30 실측)

> **갱신 이력**: 최초 작성 시 이 절은 "버킷 CORS(GET)가 미검증 선행 조건"이라고 판정했다(OA-1/OA-2).
> **2026-07-30 팀 리드 실측으로 이 판정이 뒤집혔다** — 아래가 확정 내용이다. 구 판정(선행 조건 필요 · `web/cors.json` 신설)은 폐기한다.

**판정: 버킷 CORS 설정은 필요하지 않다.** 다운로드 URL의 호스트인 `firebasestorage.googleapis.com`은 **서비스 프론트엔드가 `Access-Control-Allow-Origin: *`를 항상 반환**하며, 이는 **버킷 CORS 구성과 무관**하다.

**실측 근거**

| # | 요청 | 결과 |
|---|------|------|
| P1 | `curl -H "Origin: https://mcphoto-955fb.web.app" "https://firebasestorage.googleapis.com/v0/b/{bucket}/o/probe-nonexistent.jpg?alt=media"` | `403` + **`Access-Control-Allow-Origin: *`** + `Access-Control-Expose-Headers: …, Content-Length, Content-Range, …` |
| P2 | 동일 요청, `Origin: http://localhost:5000`(Emulator 오리진) | `403` + **`ACAO: *`** |
| P3 | **대조군** `curl -H "Origin: …" "https://storage.googleapis.com/{bucket}/probe-nonexistent.jpg"` | `403`, **`Access-Control-*` 헤더 전무** → 버킷 레벨 CORS는 **미설정** |

P3가 미설정인데도 P1/P2가 `ACAO: *`를 준다 → **두 레이어가 별개**임이 실측으로 확인된다.

**다운로드 URL은 항상 P1의 호스트다** — `web/functions/src/domain/session.ts:73`이 `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media&token=`로 조립하고, `web/functions/src/services/uploads.ts:135`의 `assertUrlBelongsToSession`이 그 prefix를 강제 검증한다. `storage.googleapis.com` V4 서명 URL은 **업로드 PUT 전용**이라 이번 경로와 무관하다.

**부수 확인**: `Access-Control-Expose-Headers`에 **`Content-Length`가 포함**되어 있다 → §3.3의 용량 가드가 값을 읽을 수 있다(읽지 못해도 `NaN` → 무동작으로 안전).

**⚠️ 잔여 불확실성 — 403에서 관측, 200 확인은 스모크 잔여**

위 관측은 전부 **`403 Forbidden`** 응답에서 얻었다(유효 토큰이 없어 실제 바이트를 받지 못했다). **200 응답에 같은 헤더가 붙는지는 확인되지 않았다.** 실토큰 스모크(Step W7)에서 devtools Network로 확정한다. **"확인 완료"로 기술하지 않는다.**

**결정 사항**

| 항목 | 결정 |
|---|---|
| `web/cors.json` | **만들지 않는다.** 적용할 필요가 없는 설정의 구성 파일은 오해를 만든다 |
| `gcloud`/`gsutil` 실행 | 불필요. 이 PC에 **설치돼 있지 않다**(`gcloud: NOT FOUND`) — 조회 시도에 시간을 쓰지 않는다. 위 실측이 그 자리를 대신한다 |
| `web/OPS-cors.md` | **유지한다** — "왜 설정하지 않았는가"의 근거 + 200 경로가 뒤집혔을 때의 컨틴전시 절차 + B5(PUT) 착수 시의 전체 교체 경고 |
| **graceful degrade(§3.3-D)** | **설계대로 그대로 유지한다.** CORS가 해소돼도 인앱 브라우저·구형 엔진·네트워크 실패·비2xx·용량 초과·사용자 활성화 만료 폴백은 여전히 필요하다. **"이제 필요 없다"고 판단해 제거하지 않는다** |

**향후 B5(업로드 PUT) 착수 시 주의 — CORS 설정은 전체 교체(replace)다.**
`gcloud storage buckets update --cors-file` / `gsutil cors set`은 기존 구성을 **덮어쓴다**(병합 아님). 현재 버킷 CORS가 비어 있으므로 지울 것은 없지만, PUT 규칙의 `origin`은 **절대 `*`로 두면 안 된다**(쓰기는 실제로 보호가 필요하다). 상세 형태는 `web/OPS-cors.md` §3에 있다.

**보안 규칙 영향: 없음.** `web/storage.rules`는 손대지 않는다. CORS는 Storage 보안 규칙과 독립된 레이어이며, 토큰 URL은 애초에 규칙을 우회한다(VF-13). `results/`의 SDK read는 계속 `false`로 닫아 둔다.

### 3.6 CSP 영향 — 변경 없음

| 필요 동작 | 관련 디렉티브 | 현행 값 | 판정 |
|---|---|---|---|
| `fetch(firebasestorage…)` | `connect-src` | `… https://firebasestorage.googleapis.com …` | **이미 허용**(VF-9) |
| `<img src>`/`<video src>` 토큰 URL | `img-src`/`media-src` | `… https://firebasestorage.googleapis.com` | 불변 |
| `<a href="blob:…" download>` 클릭 | (해당 fetch 디렉티브 없음) | — | **변경 불요**(OA-3에서 관측 확인) |
| 인라인 SVG 아이콘(공유 버튼) | — | 마크업이므로 CSP 무관 | 변경 불요 |
| 클립보드 API | — | CSP 무관 | 변경 불요 |

`blob:`을 프리뷰 소스로 쓰지 않으므로 `img-src`/`media-src`에 `blob:`을 넣을 이유가 없다. 만약 Step W7에서 CSP 위반이 콘솔에 보고되면, 메시지의 `effective-directive` 값을 읽어 **그 디렉티브에만** `blob:`을 추가한다(추측으로 여러 디렉티브를 열지 않는다).

### 3.7 메모리·용량 리스크 — 낮음 (근거 있음)

| 대상 | 상한 근거 | 예상 blob 크기 |
|---|---|---|
| 타임랩스 `timelapse.mp4` | **최대 12.5초**, 1080p, 30fps, H.264 CRF20 (VF-16) | 단위 MB(≈2~8MB) |
| 최종 이미지 `final.{jpg\|png}` | 프레임 입력 상한 10MB / 장변 4000px (VF-17) | 수 MB(PNG 최악 시 수십MB) |

- 동시 적재는 최대 1건이다(미디어별 `inflight` 플래그).
- 150MB 가드(§3.3)는 계약을 벗어난 이상 케이스에 대한 방어이며, 정상 범위에서는 발동하지 않는다.
- **알려진 비용: 전송량 2배**(프리뷰 로드 + 저장 fetch). 위 크기라면 모바일 데이터에서도 수용 가능하다. 이를 없애려면 `<img crossorigin="anonymous">` + canvas 재인코딩이 필요한데, ① 프리뷰를 CORS 의존으로 바꿔 하드 회귀 위험을 만들고 ② 재인코딩으로 화질이 열화되고 PNG/JPEG가 뒤바뀐다 → **기각**(§0.3).

### 3.8 리소스 생명주기 (해제 경로 — 누수 방지)

| 리소스 | 획득 | 해제 경로 |
|---|---|---|
| `AbortController` (fetch) | 클릭 시 미디어별 1개 | `finally`에서 추적 Set에서 제거. `pagehide`에서 남은 것 전부 `abort()` |
| `URL.createObjectURL` | 저장 성공 시 1개 | **`setTimeout(60s)` → `revokeObjectURL`** 이 유일한 해제 경로. 즉시 revoke하면 다운로드 시작 전에 blob이 사라질 수 있다(특히 iOS) |
| 위 60초 타이머 | 저장 성공 시 | id를 Set에 보관, `pagehide`에서 `clearTimeout` |
| 토스트 자동 숨김 타이머 | `showToast()` 호출 시 | 모듈 스코프 단일 변수. 재호출 시 **항상 먼저 `clearTimeout`**(중첩 토스트·타이머 누적 없음). `pagehide`에서도 clear |
| `<a>` 클릭 리스너 | 최초 1회 배선 | 페이지 수명과 동일(단일 페이지, `<a>`가 DOM에서 제거되지 않음) → 명시 해제 불요. **`renderSuccess`가 재호출될 수 있으므로 리스너 중복 배선을 막는다**(§8.3) |
| 임시 `<a>`(blob 클릭용) | 저장 성공 시 | `click()` 직후 `remove()` |
| 임시 `<textarea>`(레거시 복사) | 복사 폴백 시 | `finally`에서 `remove()` |

**`pagehide`에서 `revokeObjectURL`은 하지 않는다.** 문서가 파괴될 때 blob store도 함께 사라지므로 누수가 아니고, 반대로 다운로드가 진행 중일 때 revoke하면 일부 엔진에서 다운로드가 중단될 수 있다. 이 판단을 코드 주석에 남긴다.

**`renderSuccess` 재호출 안전성**: `#retry-btn`은 `loadSession`을 재실행하므로 `renderSuccess`가 여러 번 돌 수 있다(`app.js:261`). 다운로드 리스너는 `addEventListener`로 붙이므로 **중복 배선 시 fetch가 2회 발생**한다. → 리스너 배선을 `init()`(1회)으로 옮기거나 `dataset.bound` 가드를 둔다. §8.3에서 전자를 채택.

### 3.9 사용자에게 보여줄 문구 (과장 금지)

| 상황 | 문구 | 톤 |
|---|---|---|
| 저장 시작 성공 | `저장을 시작했습니다. 다운로드 목록을 확인해 주세요.` | 중립. "저장되었습니다"라고 단정하지 않는다 — 브라우저가 확인 시트를 띄울 수 있다 |

> **문구 갱신 이력 (2026-07-30, 팀 리드 지시)**: 최초 문구는 `저장을 시작했습니다.` 였다. iOS 에서 저장 위치가 **Files 앱 > 다운로드**라 손님이 파일을 찾지 못할 여지가 있어 안내 한 문장을 덧붙였다.
> - **UA 스니핑으로 플랫폼을 분기하지 않는다.** 이 문구는 iOS(Files 앱 > 다운로드)·Android(`Download/`)·데스크톱(다운로드 폴더) 모두에 맞는 **중립 표현**이라 분기가 불필요하다. 플랫폼 판별 코드는 인앱 브라우저에서 오판정하고 유지보수 부채가 된다.
> - "저장되었습니다"로 단정하지 않는 이 절의 원칙은 **그대로 유지**한다.
> - 리뷰어 주의: 이 문구 차이는 **의도된 갱신**이며 설계-구현 불일치가 아니다.
| 저장 실패 → 폴백 | `자동 저장이 지원되지 않는 환경입니다. 원본을 열었으니 길게 눌러 저장해 주세요.` | 원인을 사용자 탓으로 돌리지 않고 다음 행동을 지시 |
| 수동 힌트(첫 시도 후 상시) | `저장이 안 되면 이미지를 길게 눌러(모바일)/우클릭(PC) 저장하세요.` | **현행 문구 유지**(`index.html:40-42,64-66`) |

---

## §4 쟁점 2 — 공유 버튼 (링크 복사)

### 4.1 판정 요약

| 항목 | 결정 | 근거 |
|---|---|---|
| 기본 동작 | **클립보드에 링크 복사 + 토스트** | 사용자 요구 원문이 "링크 복사되게 하고, 링크 복사되었다고 토스트 메시지 노출" |
| `navigator.share` 사용 | **하지 않는다** | 요구가 링크 복사다. 공유 시트를 띄우면 사용자가 대상 앱을 한 번 더 골라야 하고, 복사 자체가 보장되지 않는다 → §11.2 이연 |
| `navigator.clipboard` 가용성 | Hosting은 HTTPS라 secure context ○. `localhost`도 secure context로 취급되어 Emulator 검증 ○ | — |
| 폴백 | 3단(§4.3) | 구형 브라우저·인앱 브라우저·`document.hasFocus()` 실패 등에서 조용히 실패하지 않게 |
| 복사할 URL | **`location.href`가 아니라 토큰으로 재조립한 canonical URL** | 유입 시 붙은 잡 파라미터(`utm_*`·`fbclid` 등)를 제거해 계약 형식(`{origin}{pathname}?s={token}`)으로 정규화 |
| 노출 조건 | **유효 토큰이 파싱된 경우 노출**(= `?s=` 없음 오류 상태에서만 숨김) | §4.5 |

### 4.2 복사할 URL 조립

```js
function canonicalShareUrl(token) {
  const u = new URL(location.href);
  u.search = '';
  u.hash = '';
  u.searchParams.set('s', token);   // 인코딩은 URLSearchParams 가 처리
  return u.toString();
}
```

계약 §3.5의 `{hostingBaseUrl}/?s={token}`과 동형이다. `location.origin`을 직접 문자열 연결하지 않고 `URL`을 쓰는 이유: pathname 보존(경로형 라우팅으로 바뀌어도 깨지지 않음) + 인코딩 위임.

### 4.3 복사 3단 폴백

```
1차: navigator.clipboard?.writeText(url)          (secure context + 권한)
       │ reject / 부재
       ▼
2차: 임시 <textarea> + select() + document.execCommand('copy')   (deprecated, 광범위 호환)
       │ false / throw
       ▼
3차: 토스트로 안내 — "링크 복사를 지원하지 않는 브라우저입니다. 주소창의 URL을 복사해 주세요."
```

3차가 유효한 이유: canonical URL은 정상 진입 시 주소창 URL과 동일하다. 따라서 추가 마크업 없이 실행 가능한 지시가 된다. **어떤 경로에서도 "아무 일도 일어나지 않음"은 없다.**

2차 구현 규약:
- `<textarea>`는 반드시 DOM에 붙어 있어야 `select()`가 동작한다. `position:fixed; top:-1000px; opacity:0`으로 화면 밖에 두고, `aria-hidden="true"`로 접근성 트리에서 제외한다. `display:none`은 **금지**(선택 불가).
- iOS 호환을 위해 `setSelectionRange(0, value.length)`를 함께 호출한다.
- `try/catch/finally`로 감싸고 `finally`에서 `remove()`한다.

### 4.4 버튼 마크업·접근성

```html
<header class="app__header">
  <h1 class="app__title">MCPhoto</h1>
  <button id="share-btn" type="button" class="btn btn--icon" title="링크 복사" hidden>
    <svg class="btn__icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">…</svg>
    <span>공유</span>
  </button>
</header>
```

- `type="button"` 필수(폼은 없지만 기본 `submit` 방지 관례).
- **`aria-label`을 쓰지 않는다.** 가시 라벨이 "공유"인데 `aria-label="링크 복사"`를 주면 WCAG 2.5.3(Label in Name) 위반이다. 보조 설명은 `title`로 제공하고, 스크린리더는 "공유 버튼"으로 읽는다.
- SVG는 **인라인**(외부 요청 0 → CSP·오프라인 무관), `aria-hidden="true" focusable="false"`.
- 터치 타깃: 기존 `--touch: 48px`를 재사용해 `min-height`/`min-width` 확보.

**헤더 레이아웃**: `text-align:center`(VF-11)를 유지하면서 우측에 버튼을 놓기 위해 **3열 그리드**를 쓴다.

```css
.app__header { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 8px; }
.app__title  { grid-column: 2; }
#share-btn   { grid-column: 3; justify-self: end; }
```

1열과 3열이 같은 `1fr`이므로 버튼 폭과 무관하게 **제목이 정확히 중앙에 남는다**(flex `space-between`은 제목이 좌측으로 밀린다).

### 4.5 노출 조건 판정

`?s=` 토큰이 파싱된 경우 노출한다(= 토큰 없음/형식 오류로 즉시 `error`인 경우만 숨김). loading·success·expired 상태에서는 모두 노출.

**만료 상태에서도 노출하는 것은 의도된 선택이다.** 만료된 링크를 공유하면 수신자에게 만료 안내가 보여 무의미하지만, ① 사용자 요구가 "상단에 공유 버튼"이고 ② 링크 자체는 유효한 URL이며 ③ 상태별로 숨기면 상태 머신과 결합도가 생긴다. **알고 수용하는 마이너**로 기록한다(§11.1 R4).

---

## §5 쟁점 3 — 토스트 컴포넌트

### 5.1 판정: 신규 최소 구현

**조사 결과 재사용할 것이 없다(VF-10).** `.media__error`·`.media__optout`·`.notice`는 모두 정적 인라인 텍스트로, 전이·자동 숨김·live region이 전혀 없다. → 단일 재사용 함수 `showToast(message, variant)`를 신설한다.

### 5.2 마크업 — live region은 페이지 로드 시점부터 존재해야 한다

```html
<!-- <body> 의 마지막 자식. .app 밖에 둔다(transform 조상 영향 차단) -->
<div id="toast" class="toast" role="status" aria-live="polite" hidden></div>
```

- **동적으로 생성하지 않는다.** live region을 DOM에 삽입하면서 동시에 텍스트를 넣으면 다수 스크린리더가 읽지 않는다. 빈 상태로 미리 존재해야 한다.
- **`textContent`만 쓴다. `innerHTML` 금지**(§0.4 하한 · 안전 규칙). 현재 토스트 메시지는 전부 코드 상수지만, 예외 메시지 등 외부 유래 문자열이 섞일 여지를 원천 차단한다.
- `role="status"` + `aria-live="polite"`: 사용자 조작을 방해하지 않고 알린다(`assertive` 아님).

### 5.3 스타일

```css
.toast {
  position: fixed; left: 50%;
  bottom: calc(24px + env(safe-area-inset-bottom, 0px));   /* iOS 홈 인디케이터 회피 */
  transform: translate(-50%, 12px);
  max-width: min(92vw, 420px);
  padding: 12px 16px; border-radius: 10px;
  background: rgba(26,26,26,.94); color: #fff;
  font-size: .9rem; text-align: center;
  opacity: 0; pointer-events: none; z-index: 100;
  transition: opacity .18s ease, transform .18s ease;
}
.toast[hidden] { display: none; }              /* .state[hidden] 선례와 동일한 명시 처리 */
.toast.is-visible { opacity: 1; transform: translate(-50%, 0); }
.toast--warn { background: rgba(192,57,43,.96); }
@media (prefers-reduced-motion: reduce) { .toast { transition: none; } }
```

- 다크 모드 오버라이드 불요: 어두운 반투명 배경 + 흰 글자는 두 테마에서 동일하게 읽힌다(`--primary` 등 토큰에 의존하지 않는다).
- `pointer-events: none`으로 하단 버튼 조작을 가리지 않는다.

### 5.4 표시 로직 (타이머 단일화)

```js
let toastTimer = 0;
const TOAST_VISIBLE_MS = 2600;
const TOAST_FADE_MS = 200;

function showToast(message, variant) {
  const el = document.getElementById('toast');
  if (!el) return;
  if (toastTimer) { clearTimeout(toastTimer); toastTimer = 0; }   // 재호출 시 항상 먼저 취소
  el.textContent = message;
  el.classList.toggle('toast--warn', variant === 'warn');
  el.hidden = false;
  // hidden 해제와 같은 프레임에 클래스를 붙이면 transition 이 생략된다 → 다음 프레임에 적용.
  requestAnimationFrame(() => el.classList.add('is-visible'));
  toastTimer = window.setTimeout(() => {
    el.classList.remove('is-visible');
    toastTimer = window.setTimeout(() => { el.hidden = true; toastTimer = 0; }, TOAST_FADE_MS);
  }, TOAST_VISIBLE_MS);
}
```

중첩 타이머의 id도 같은 `toastTimer` 변수에 담기므로, 재호출 시의 단일 `clearTimeout`이 **어느 단계의 타이머든 취소**한다. 타이머 누적·유령 토스트가 발생하지 않는다.

---

## §6 쟁점 4 — 다운로드 파일명

### 6.1 요구·현행 문제

- 현행: `download="mcphoto.jpg"` / `"mcphoto.mp4"` **하드코딩**(`index.html:37,61`, `app.js:126,159`).
- **버그(VF-6)**: `outputFormat=png`인 세션도 `mcphoto.jpg`로 이름 붙는다.
- 세션을 여러 번 저장하면 `mcphoto.jpg`, `mcphoto (1).jpg` … 로 쌓여 어느 촬영인지 알 수 없다.

### 6.2 설계 — 토큰의 시각 prefix를 쓴다

토큰 = sessionId = `{yyyyMMdd}_{HHmmss}_{UUIDv4}`(VF-7)이고, 시각 노출은 계약이 이미 수용한 트레이드오프다(VF-8). → 페이지는 **추가 조회 없이** 촬영 시각을 파일명에 넣을 수 있다.

```js
const TOKEN_STAMP_RE = /^(\d{8})_(\d{6})_/;
const ALLOWED_EXT = new Set(['jpg', 'jpeg', 'png', 'mp4']);

/** 1차: 토큰 URL 경로의 실제 확장자(results/{sid}/final.png → 'png'). */
function extFromTokenUrl(url) {
  try {
    const path = new URL(url).pathname;                 // /v0/b/{bucket}/o/results%2F…%2Ffinal.png
    const marker = path.lastIndexOf('/o/');
    if (marker < 0) return null;
    const decoded = decodeURIComponent(path.slice(marker + 3));
    const dot = decoded.lastIndexOf('.');
    if (dot < 0) return null;
    const ext = decoded.slice(dot + 1).toLowerCase();
    return ALLOWED_EXT.has(ext) ? ext : null;           // 화이트리스트 통과분만
  } catch { return null; }
}

/** 2차: 응답 Content-Type. */
function extFromMime(mime) {
  switch (String(mime || '').split(';')[0].trim().toLowerCase()) {
    case 'image/png': return 'png';
    case 'image/jpeg': return 'jpg';
    case 'video/mp4': return 'mp4';
    default: return null;
  }
}

/** 3차: 미디어 종류 기본값. */
function buildFileName(token, url, mime, kind) {
  const ext = extFromTokenUrl(url) || extFromMime(mime) || (kind === 'video' ? 'mp4' : 'jpg');
  const m = TOKEN_STAMP_RE.exec(String(token || ''));
  const stamp = m ? `_${m[1]}_${m[2]}` : '';
  const suffix = kind === 'video' ? '_timelapse' : '';
  return `MCPhoto${stamp}${suffix}.${ext}`;
}
```

| 입력 | 결과 |
|---|---|
| 사진, 토큰 `20260730_143022_a1b2…`, 경로 `final.jpg` | `MCPhoto_20260730_143022.jpg` |
| 사진, 같은 토큰, 경로 `final.png` | `MCPhoto_20260730_143022.png` |
| 영상, 같은 토큰 | `MCPhoto_20260730_143022_timelapse.mp4` |
| 토큰이 형식과 다름(방어) | `MCPhoto.jpg` / `MCPhoto_timelapse.mp4` |

**보안**: 파일명에 도달하는 값은 ① 정규식으로 캡처한 **숫자 8+6자리**와 ② 화이트리스트 확장자뿐이다. 경로 구분자·제어문자·`..`가 들어갈 경로가 없다. **토큰 원문(UUID 포함)을 파일명에 그대로 넣지 않는다.**

### 6.3 렌더 시점에도 정확한 이름을 심는다

`renderSuccess`는 URL을 이미 갖고 있으므로 `extFromTokenUrl`만으로 정확한 확장자를 알 수 있다. → 렌더 시점에 `a.setAttribute('download', buildFileName(token, url, null, kind))`로 심는다. `index.html`의 정적 `download` 속성도 `MCPhoto.jpg` / `MCPhoto_timelapse.mp4`로 정정한다(JS 미실행 시의 하한값).

---

## §7 쟁점 5 — "MC포토" → "MCPhoto"

### 7.1 변경 대상 (웹페이지 노출 문자열 — 전수)

| 파일:라인 | 현재 | 변경 후 | 노출 위치 |
|---|---|---|---|
| `web/public/index.html:7` | `<title>MC포토 다운로드</title>` | `<title>MCPhoto 다운로드</title>` | 브라우저 탭·공유 미리보기·북마크 |
| `web/public/index.html:14` | `<h1 class="app__title">MC포토</h1>` | `<h1 class="app__title">MCPhoto</h1>` | 페이지 헤더 |
| `web/public/index.html:94` | `<p>MC포토</p>` | `<p>MCPhoto</p>` | 페이지 푸터 |

**이 3곳이 전부다(VF-12).** `manifest.json`·favicon·`og:title`/`og:site_name`·`meta description`·`apple-mobile-web-app-title`은 **파일 자체가 존재하지 않거나 태그가 없다** → 추가 대상 없음(이번 범위에서 신설하지 않는다).

### 7.2 함께 정리하는 소스 주석 (노출 아님 — 드리프트 방지)

| 파일:라인 | 현재 | 변경 후 |
|---|---|---|
| `web/public/app.js:1` | `// MC포토 모바일 다운로드 페이지 진입 로직.` | `// MCPhoto 모바일 다운로드 페이지 진입 로직.` |
| `web/public/styles.css:1` | `/* MC포토 모바일 다운로드 페이지 — …` | `/* MCPhoto 모바일 다운로드 페이지 — …` |

노출 문자열이 아니지만 **이번에 편집하는 바로 그 파일들의 1행**이며, H1이 `MCPhoto`인데 파일 헤더가 `MC포토`로 남으면 즉각적인 드리프트 신호가 된다. 각 파일 1행, 리스크 0.

### 7.3 범위 외 — 발견 사항 보고

이번에 **변경하지 않는다.** 팀 리드 지시대로 범위는 웹페이지 노출 문자열이다.

| 위치 | 내용 | 노출 성격 | 비고 |
|---|---|---|---|
| `Directory.Build.props:13` | `<Product>MC포토</Product>` | **노출** — 파일 속성·작업 관리자·`AssemblyProduct` | 브랜딩 통일 시 여기가 진원지(`obj/**/*.AssemblyInfo.cs` 89건은 여기서 생성된 산출물이므로 별도 수정 불요) |
| `src/MCPhoto.App/Services/GoogleSignInService.cs:177` | OAuth 루프백 콜백 HTML의 `<title>MC포토</title>` | **노출** — 시스템 브라우저 탭 제목 | 웹 페이지지만 데스크톱 앱의 인증 콜백이다. 브랜딩 통일 시 함께 처리 권장 |
| `installer/MCPhoto.iss:9,23,48-50,56` | 인스톨러 표시명·시작 메뉴·바탕화면 아이콘 | **노출** | 범위 외(팀 리드 명시) |
| `web/package.json:5`, `web/firestore.rules:3`, `web/storage.rules:3`, `web/OPS-ttl.md:1`, `web/tests/rules.test.js:1`, `web/functions/**`(주석·설명 다수) | 비노출(주석·메타) | — | 범위 외 |
| `docs/**` (README·analysis·design 다수) | 문서 제목·본문 | 비노출 | 범위 외 |

> **권고**: 브랜딩을 전면 통일하려면 `Directory.Build.props:13`와 `GoogleSignInService.cs:177`이 실제 사용자 노출 지점이므로 별 이터레이션으로 묶는 것이 좋다. 이번 커밋에서 섞으면 웹 변경의 리뷰 표면이 WPF까지 번진다.

---

## §8 파일별 변경 명세

### 8.1 변경 지점 라인 맵 (그룹 간 교집합 확인)

| 파일 | G1 자동 저장 | G2 공유·토스트 | G3 네이밍 |
|---|---|---|---|
| `web/public/index.html` | `37,40-42,61,64-66` (download 속성·힌트 초기 hidden) | `13-15`(헤더), `96` 뒤(토스트 div) | `7,14,94` |
| `web/public/app.js` | 신규 §다운로드 블록, `105-135`·`138-170`(렌더 배선), `235-264`(init 배선) | 신규 §공유·§토스트 블록, `235-264`(init 배선) | `1` |
| `web/public/styles.css` | `196-231` 뒤(`.is-busy`) | `59-69`(헤더 그리드), 신규 `.btn--icon`·`.toast` | `1` |
| ~~`web/cors.json`~~ | **신설하지 않음** (§3.5 실측으로 불필요 판정) | — | — |
| `web/OPS-cors.md` (신규) | ○ (불필요 판정 근거 + 컨틴전시 + B5 경고) | — | — |

`init()` 배선(`app.js:235-264`)만 G1·G2가 공유한다 → **W5를 W3 뒤에 배치**해 충돌을 없앤다(§10 의존 그래프).

### 8.2 `web/public/index.html`

1. `:7` title, `:14` H1, `:94` footer → `MCPhoto` (G3)
2. `:13-15` 헤더에 `#share-btn` 추가(§4.4). 초기 `hidden`
3. `:37` `download="mcphoto.jpg"` → `download="MCPhoto.jpg"`, `:61` → `download="MCPhoto_timelapse.mp4"`
4. `:40-42`/`:64-66` 수동 힌트 `<p>`에 **`hidden` 속성 추가** — 초기 숨김, JS가 노출 시점을 결정(§3.3). ⚠️ **문구는 바꾸지 않는다**
5. `</main>` 뒤, `</body>` 앞에 토스트 div 추가(§5.2)

> ⚠️ **`hidden` 추가 시 주의**: `app.js:109,132,166` 등이 이미 `photoHint.hidden`을 명시적으로 제어한다. 현행 로직은 "URL 있으면 `hidden=false`"인데, 이 규칙을 **"자동 저장이 불가/실패로 판정될 때만 `hidden=false`"** 로 바꿔야 한다. 이 지점을 놓치면 힌트가 항상 다시 켜져 UI 개선 효과가 사라진다.

### 8.3 `web/public/app.js` — 모듈 구성

기존 파일의 섹션 주석 스타일(`// ---- 이름 ----`)을 그대로 따른다.

```
// ---- 상수 -----------------------------------------------------------
   MAX_AUTO_DOWNLOAD_BYTES / TOAST_VISIBLE_MS / TOAST_FADE_MS
   OBJECT_URL_TTL_MS(60_000) / ALLOWED_EXT / TOKEN_STAMP_RE

// ---- 토스트 ---------------------------------------------------------
   showToast(message, variant)                             §5.4

// ---- 링크 복사·공유 --------------------------------------------------
   canonicalShareUrl(token) / copyToClipboard(text) / legacyCopy(text)
   handleShareClick()                                      §4.2-4.3

// ---- 파일명 ---------------------------------------------------------
   extFromTokenUrl(url) / extFromMime(mime) / buildFileName(...)   §6.2

// ---- 자동 저장 -------------------------------------------------------
   supportsAutoDownload()          기능 감지                §3.3-A
   let autoDownloadEnabled                                  모듈 상태
   setBusy(anchor, busy) / revealManualHints()
   disableAutoDownload()           전역 degrade             §3.3-D
   triggerBlobDownload(blob, filename)                      §3.8
   handleDownloadClick(ev, kind)                            §3.3-B/C/D

// ---- 정리(해제) ------------------------------------------------------
   pagehide 리스너: abort 전부 + 타이머 clear (revoke 는 하지 않음)  §3.8

// ---- 기존: 상태 전이 / 토큰 파싱 / 만료 포맷 / 성공 렌더 / 로드 / 진입점 ----
```

**배선 위치 규약 (중복 배선 방지 — §3.8)**

- `#share-btn`·`#photo-download`·`#video-download`의 클릭 리스너는 **`init()`에서 1회만** 배선한다. `renderSuccess`에서 배선하면 `#retry-btn` 재시도 시 리스너가 누적되어 fetch가 중복 발생한다(현행 `app.js:261`이 `loadSession`을 재실행함).
- `renderSuccess`는 `href`·`download` 속성과 힌트 가시성만 갱신한다.
- 핸들러가 필요한 `token`은 `init()`이 클로저로 넘긴다(전역 변수 신설 없음).

**`handleDownloadClick` 계약**

| 조건 | 동작 |
|---|---|
| `href` 없음(프리뷰 로드 실패, VF-3) | 즉시 반환. `preventDefault()`도 불필요(`.is-disabled`가 `pointer-events:none`) |
| `autoDownloadEnabled === false` | **개입하지 않는다** — 기본 내비게이션 수행(현행 동작) |
| 해당 미디어 `inflight` | `preventDefault()` 후 반환 |
| 정상 | `preventDefault()` → busy → fetch → 가드 → blob → 임시 `<a>` click → 토스트 → 힌트 노출 → 60s revoke 예약 |
| `AbortError` | **아무 것도 하지 않는다**(페이지 이탈). 토스트·폴백 없음 |
| 그 외 예외 | `console.warn` → `disableAutoDownload()` → warn 토스트 → `location.assign(url)` |

### 8.4 `web/public/styles.css`

1. `:1` 헤더 주석 `MCPhoto` (G3)
2. `.app__header` → 3열 그리드(§4.4). `.app__title`의 `text-align:center`는 그리드에서 불필요해지지만 **제거하지 않는다**(그리드 미지원 폴백)
3. `.btn--icon` 신설: `width:auto; min-width:var(--touch); min-height:var(--touch); gap:6px; background:transparent; color:var(--text); border:1px solid var(--border); padding:8px 12px; font-size:.85rem`
   - ⚠️ `.btn`은 `width:100%`이므로 `.btn--icon`에서 `width:auto`로 반드시 덮어쓴다
   - ⚠️ `@media (min-width:768px)`의 `.btn { min-width:220px; align-self:flex-start }`가 헤더 버튼까지 늘린다 → `.btn--icon`에 `min-width:var(--touch)`를 **미디어 쿼리 안에서도** 유지하도록 셀렉터 특이도를 확인한다(`#state-error .btn` 선례와 동일한 함정)
4. `.btn__icon` : `width:1.05em; height:1.05em; fill:currentColor; flex:none`
5. `.btn[aria-busy="true"], .btn.is-busy` : `opacity:.7; pointer-events:none; cursor:progress`
6. `.toast` 일체(§5.3)

### 8.5 신규 파일

- ~~**`web/cors.json`**~~ — **신설하지 않는다**(§3.5). 버킷 CORS 설정이 불필요하다고 실측 판정됐으므로, 적용할 일이 없는 설정의 구성 파일을 리포지토리에 두면 오해를 만든다. 컨틴전시용 JSON 형태는 `OPS-cors.md` 본문에 인라인으로 남긴다.
- **`web/OPS-cors.md`** — 운영자 문서. `OPS-ttl.md`의 표 형식·톤을 따른다. 포함 항목: **불필요 판정 + 실측 근거(§3.5의 P1~P3)** / **403 관측·200 잔여 명시** / 200이 뒤집혔을 때의 컨틴전시 절차(JSON 인라인) / `origin:["*"]` 근거 / **B5(PUT) 착수 시 전체 교체 경고** / **폴백 경로를 유지하는 이유(CORS 해소와 무관한 실패 요인 목록)** / 보안 규칙 무관 명시.

---

## §9 테스트 계획

### 9.1 자동 게이트 (VF-14 — 하네스가 없으므로 이것이 상한)

| # | 명령 | 기대 |
|---|---|---|
| A1 | `node --check web/public/app.js` | 오류 0 (ESM 구문) |
| A2 | `rg -n "MC포토" web/public/` | **무매치** |
| A3 | `rg -n "innerHTML" web/public/app.js` | **무매치** |
| A4 | `rg -n "crossorigin\|crossOrigin" web/public/` | **무매치** (§0.3 금지 항목) |
| A5 | `rg -n "window\.open" web/public/app.js` | **무매치** (§3.3-D: `location.assign`만) |
| A6 | `rg -n "revokeObjectURL" web/public/app.js` | **1건 이상** (해제 경로 존재) |
| A7 | `rg -n "addEventListener" web/public/app.js` 위치 확인 | 다운로드·공유 리스너가 `renderSuccess` **밖**에 있다 |
| A8 | cwd=`web`: `npm run test:rules` | 전량 통과(**불변** — 규칙 미변경) |
| A9 | `git diff --name-only` | `web/functions/**`·`src/**`·`installer/**`가 **등장하지 않는다** |

### 9.2 로컬 Emulator 스모크 (Step W7 전반)

```
cd web && npm run emulators        # firestore + storage + hosting
```

1. Emulator UI(Firestore)에서 `resultSessions/{token}` 문서를 수동 생성한다.
   - 문서 ID = `20260730_143022_11111111-2222-3333-4444-555555555555` (계약 형식)
   - `expiresAt` = 현재 + 1시간 (Timestamp)
   - `finalImageUrl` = **실 프로덕션 세션의 토큰 URL**(운영자 제공) — 이래야 실제 CORS 응답을 관측할 수 있다
   - `timelapseUrl` = 동일 세션의 mp4 토큰 URL
2. `http://localhost:5000/?s={위 문서 ID}` 접속.

> ⚠️ 로컬 오리진은 `http://localhost:5000`이다. §3.5의 `origin:["*"]`를 적용했다면 통과한다. 만약 오리진 열거 방식으로 적용했다면 로컬에서는 **폴백 경로가 관측되는 것이 정상**이며, 그때는 실배포에서 재검증해야 한다(A/B 구분을 혼동하지 말 것).

| # | 시나리오 | 기대 관측 |
|---|---|---|
| L1 | 페이지 로드 | H1·탭 제목·푸터가 `MCPhoto`. 수동 힌트 **미노출**. 공유 버튼 노출 |
| L2 | 공유 버튼 클릭 | 토스트 "링크가 복사되었습니다" + 클립보드 내용이 `http://localhost:5000/?s={token}` |
| L3 | `?s=`에 `&utm_source=x` 추가 후 공유 | 복사된 URL에 `utm_source`가 **없다**(canonical 정규화) |
| L4 | `?s=` 없이 접속 | `error` 상태 + 공유 버튼 **미노출** |
| L5 | 사진 다운로드 클릭 (성공 경로 — §3.5 판정상 기본 기대) | 버튼이 "저장 중…"·`aria-busy` → 파일 저장. 파일명 `MCPhoto_20260730_143022.jpg`(또는 `.png`). 토스트 "저장을 시작했습니다. 다운로드 목록을 확인해 주세요.". 힌트 노출 |
| L6 | 사진 다운로드 클릭 (실패 경로 — 인앱 브라우저 등. 강제 관측은 devtools 오프라인/요청 차단으로) | warn 토스트 후 원본으로 내비게이션. 뒤로가기 후 재클릭 시 개입 없이 즉시 내비게이션 |
| L7 | 영상 다운로드 클릭 | 파일명 `MCPhoto_20260730_143022_timelapse.mp4` |
| L8 | 다운로드 중 버튼 연타 | fetch가 **1회만** 발생(Network 탭) |
| L9 | `finalImageUrl`을 깨진 URL로 바꿔 재로드 | 현행 동작 유지: 프리뷰 숨김 + "불러올 수 없습니다" + 버튼 `.is-disabled`. **클릭해도 fetch 없음** |
| L10 | `#retry-btn` 경유 재로드(에러 상태 유발 후) | 다운로드 클릭 시 fetch가 **1회만** 발생(리스너 중복 배선 회귀 검사) |
| L11 | 콘솔 전체 | **CSP 위반 0건**(OA-3) |
| L12 | 두 미디어 모두 `null`(옵션 꺼짐) | 현행 동작 유지: 옵션꺼짐 안내 2개, 힌트·버튼 숨김 |
| L13 | 다크 모드 강제(devtools) | 토스트·공유 버튼 가독성 확보 |
| L14 | 320px 폭 | 헤더 제목·공유 버튼이 겹치지 않고 가로 스크롤 없음 |
| L15 | 키보드만 조작(Tab/Enter) | 공유·다운로드 버튼에 포커스 링 + Enter로 동작. 토스트가 포커스를 훔치지 않음 |

### 9.3 실배포 플랫폼 매트릭스 (Step W7 후반 — 육안, 필수)

`https://mcphoto-955fb.web.app/?s={실제 토큰}`에서 §3.4 표를 그대로 채운다. **최소 커버리지: Android Chrome, iOS Safari, 데스크톱 1종.** 인앱 브라우저(카카오톡)는 폴백 동작 확인용.

| 플랫폼 | 관측 항목 |
|---|---|
| Android Chrome | 파일이 다운로드 알림/`Download/`에 나타남. 파일명 정확. 갤러리 인덱싱 여부를 **관측하여 기록**(예상과 다르면 §3.4를 정정) |
| iOS Safari | 다운로드 확인 시트 → Files 앱 > 다운로드에 저장. **사진 앱에 없음을 확인하고 기록**(설계 전제 검증) |
| iOS 인앱(카카오톡) | 자동 저장 성공/실패 무관. 실패 시 **폴백이 동작하고 힌트가 노출되는지**만 PASS 조건 |
| 데스크톱 | 저장 위치·파일명·확장자(PNG 세션이 있으면 `.png`로 저장되는지) |

### 9.4 리뷰어 체크리스트 (`js-code-reviewer`용)

- [ ] `fetch`에 커스텀 헤더가 없다(preflight 회피, §3.3)
- [ ] `credentials: 'omit'`이 지정돼 있다
- [ ] `Content-Length` 가드가 `.blob()` **앞**에 있다
- [ ] `revokeObjectURL`이 즉시가 아니라 지연 실행이다(§3.8)
- [ ] `pagehide`에서 `revokeObjectURL`을 **호출하지 않는다**(다운로드 중단 방지) — 주석으로 이유가 남아 있다
- [ ] `AbortError`가 폴백 경로로 새지 않는다
- [ ] 다운로드·공유 리스너가 `renderSuccess` 밖에서 1회만 배선된다
- [ ] `showToast`가 `textContent`만 쓴다
- [ ] `showToast` 재호출 시 이전 타이머를 반드시 `clearTimeout`한다
- [ ] 파일명에 정규식 캡처값·화이트리스트 확장자만 들어간다
- [ ] `location.assign` 폴백이며 `window.open`이 아니다
- [ ] `<img>`/`<video>`에 `crossorigin`이 추가되지 않았다
- [ ] 상태 머신 `STATES` 배열과 4개 섹션이 불변이다
- [ ] 힌트 가시성이 자동 저장 능력에만 종속되고, "URL 있음"에는 종속되지 않는다

---

## §10 구현 WBS

### 10.0 공통 전제 (모든 단계 공통 — 읽지 않고 시작하지 말 것)

- 루트: `C:\STUDY\PROJECT\PhotoBooth`. 대상은 `web/` 이하만이다.
- **인코딩: UTF-8 without BOM 유지**(기존 `web/public/*` 관례). 검증: `head -c 3 <file> | od -An -tx1` 이 `ef bb bf`가 **아니어야** 한다. 신규 파일도 동일.
- 개행: `core.autocrlf=true` 환경. `git diff`가 실제 변경 줄만 보이면 통과.
- **git commit 금지**(부모가 그룹별로 커밋한다). `bldinfo.ini` 수정·언급 금지.
- **`web/functions/**`·`src/**`·`installer/**`·`docs/**`(W8 제외)를 건드리지 않는다.** `git diff --name-only`로 매 단계 확인.
- 새 npm 패키지·새 CDN import·번들러 도입 **금지**.
- 배포(`firebase deploy`) 금지 — 배포는 사용자·운영자 판단이다. Step W7의 실배포 검증은 **배포 후에 수행**하며, 배포 자체는 부모에게 요청한다.
- 자동 게이트: `node --check web/public/app.js` + §9.1 A2~A9.

### 10.1 의존 그래프

```
W1 (브랜딩 MCPhoto)              ← 독립
W2 (토스트 컴포넌트)              ← 독립
W4 (파일명 유틸 + download 정정)  ← 독립
W6 (CORS 인프라 파일·런북)        ← 독립 (코드 변경 0)
W3 (공유 버튼)                   ← W2
W5 (자동 저장 코어)              ← W2, W4  (init 배선이 W3과 겹치므로 W3 뒤에 배치)
W7 (스모크 + 플랫폼 매트릭스)      ← W1~W6
W8 (문서 동기화)                 ← W7
```

병렬 가능: **W1·W2·W4·W6**은 서로 독립. 이후 **W3 → W5** 순차.

---

### Step W1: 웹페이지 노출 문자열 `MC포토` → `MCPhoto`

- **Context Brief**: `web/public`은 QR로 진입하는 모바일 결과물 다운로드 페이지(정적 HTML + Vanilla ESM, 번들러 없음)다. 이 페이지에 브랜드명이 `MC포토`로 노출되는데 `MCPhoto`로 바꿔야 한다. 노출 지점은 3곳뿐이고(`<title>`·H1·footer), `manifest.json`·favicon·`og:` 태그는 이 프로젝트에 **존재하지 않는다**(새로 만들지 않는다). WPF·인스톨러·문서의 `MC포토`는 **이번 범위가 아니다**.
- **대상 파일**: `web/public/index.html`, `web/public/app.js`, `web/public/styles.css`
- **선행 조건**: 없음.
- **구현 내용**:
  1. `index.html:7` `<title>MC포토 다운로드</title>` → `<title>MCPhoto 다운로드</title>`
  2. `index.html:14` `<h1 class="app__title">MC포토</h1>` → `MCPhoto`
  3. `index.html:94` `<p>MC포토</p>` → `<p>MCPhoto</p>`
  4. `app.js:1` 파일 헤더 주석 `// MC포토 모바일 …` → `// MCPhoto 모바일 …` (드리프트 방지, §7.2)
  5. `styles.css:1` 파일 헤더 주석 동일 처리
  6. `app.js` 본문의 `[mcphoto]` 로그 prefix·`mcphoto.jpg` 문자열은 **이 단계에서 건드리지 않는다**(전자는 불변, 후자는 W4 소관)
- **검증 명령**:
  ```
  rg -n "MC포토" web/public/                      # 무매치
  rg -n "MCPhoto" web/public/index.html           # 3건(title·h1·footer)
  node --check web/public/app.js                  # 오류 0
  git diff --name-only                            # web/public/ 3개 파일만
  head -c 3 web/public/index.html | od -An -tx1   # ef bb bf 아님
  ```
- **완료 기준**:
  - [관측] `rg -n "MC포토" web/public/` 무매치. 브라우저 탭 제목·페이지 헤더·푸터가 `MCPhoto`로 표시된다.
  - [non-goal] `web/functions/**`·`src/**`·`installer/**`·`docs/**`가 `git diff --name-only`에 **등장하지 않는다**. `app.js`의 `[mcphoto]` 콘솔 prefix·`firebase-config.js`·`storage.rules`·`firestore.rules`는 **불변**. 상태 머신·다운로드 동작은 **전혀 바뀌지 않는다**.
  - [trigger] 문자열 치환뿐이므로 사용자 액션 trigger 없음 — 페이지 로드 시 즉시 관측.
- **롤백**: `git checkout -- web/public/`
- [ ] 완료

---

### Step W2: 토스트 컴포넌트 신설 (재사용 `showToast`)

- **Context Brief**: 이 페이지에는 일시 알림(토스트/스낵바) 컴포넌트가 **없다**. 기존 `.media__error`·`.media__optout`·`.notice`는 정적 인라인 텍스트로 전이·자동 숨김·live region이 전혀 없어 재사용할 수 없다(조사 완료). 이후 단계(공유 버튼 W3, 자동 저장 W5)가 모두 이 함수를 쓴다. 프레임워크가 없으므로 순수 DOM으로 최소 구현한다. 접근성상 live region은 **페이지 로드 시점부터 DOM에 존재**해야 하므로 동적 생성하지 않는다.
- **대상 파일**: `web/public/index.html`, `web/public/styles.css`, `web/public/app.js`
- **선행 조건**: 없음 (W1과 파일은 겹치지만 라인이 다르다)
- **구현 내용**:
  1. `index.html`: `</main>` 뒤 `</body>` 앞에 `<div id="toast" class="toast" role="status" aria-live="polite" hidden></div>` 추가. **`.app` 내부가 아니라 body 직속**(transform 조상 영향 차단)
  2. `styles.css`: 설계 §5.3의 `.toast` / `.toast[hidden]` / `.toast.is-visible` / `.toast--warn` / `prefers-reduced-motion` 규칙을 파일 끝(데스크톱 미디어 쿼리 **앞**)에 추가
  3. `app.js`: `// ---- 토스트 ----` 섹션을 상수 정의 뒤에 추가하고 설계 §5.4의 `showToast(message, variant)`를 구현. `TOAST_VISIBLE_MS=2600`, `TOAST_FADE_MS=200`
  4. **`textContent`만 사용한다. `innerHTML` 금지.**
  5. `pagehide` 리스너에서 `toastTimer`를 `clearTimeout`한다(리스너를 이 단계에서 신설, W5가 여기에 abort 처리를 추가)
- **검증 명령**:
  ```
  node --check web/public/app.js
  rg -n "innerHTML" web/public/app.js                       # 무매치
  rg -n "aria-live=\"polite\"" web/public/index.html         # 2건(기존 state-loading + 신규 toast)
  rg -n "clearTimeout" web/public/app.js                     # 2건 이상
  cd web && npm run emulators
  # 브라우저 콘솔에서 수동 호출은 불가(모듈 스코프) → W3 에서 실동작 검증
  ```
- **완료 기준**:
  - [관측] `node --check` 통과. `#toast`가 DOM에 존재하고 초기 상태가 `hidden`이며 화면에 보이지 않는다. `styles.css`에 `.toast` 규칙 4종 + reduced-motion 규칙이 존재한다.
  - [non-goal] 페이지 초기 렌더가 **시각적으로 전혀 바뀌지 않는다**(토스트는 숨김 상태). 기존 4개 상태 섹션(`state-loading/success/expired/error`)의 마크업·`STATES` 배열·`showState` 로직이 **불변**. `.media__error`·`.media__optout`·`.notice` 규칙 **불변**.
  - [trigger] 토스트는 `showToast()` **호출 시에만** 노출된다. 페이지 로드·상태 전이만으로는 절대 나타나지 않는다.
- **롤백**: `git checkout -- web/public/`
- [ ] 완료

---

### Step W3: 상단 공유 버튼 (링크 복사 + 토스트)

- **Context Brief**: 헤더(`index.html:13-15`)는 현재 `<h1>` 하나뿐이고 `text-align:center`다. 여기에 "공유" 버튼을 추가해 **현재 페이지 링크를 클립보드에 복사**하고 토스트로 알린다. 사용자 요구는 링크 복사이므로 `navigator.share`(공유 시트)는 쓰지 않는다. 복사할 URL은 `location.href`가 아니라 토큰으로 재조립한 canonical URL(`{origin}{pathname}?s={token}`)이다 — 유입 시 붙은 추적 파라미터를 제거하기 위함. 클립보드 API는 secure context가 필요하므로 3단 폴백을 둔다. 제목을 중앙에 유지하려면 flex가 아니라 3열 그리드를 써야 한다.
- **대상 파일**: `web/public/index.html`, `web/public/styles.css`, `web/public/app.js`
- **선행 조건**: **Step W2**(`showToast`)
- **구현 내용**:
  1. `index.html:13-15` 헤더에 설계 §4.4의 `#share-btn` 추가. `type="button"`, `class="btn btn--icon"`, `title="링크 복사"`, 초기 `hidden`. 인라인 SVG(`aria-hidden="true" focusable="false"`) + `<span>공유</span>`. **`aria-label`을 쓰지 않는다**(WCAG 2.5.3)
  2. `styles.css`: `.app__header`를 `display:grid; grid-template-columns:1fr auto 1fr; align-items:center; gap:8px`로 변경. `.app__title{grid-column:2}`, `#share-btn{grid-column:3; justify-self:end}`. `.app__title`의 `text-align:center`는 **제거하지 않는다**
  3. `styles.css`: `.btn--icon`·`.btn__icon` 규칙 추가(§8.4-3/4). **`.btn`의 `width:100%`를 `width:auto`로 덮어쓸 것**. `@media (min-width:768px)`의 `.btn{min-width:220px}`가 이 버튼을 늘리지 않는지 확인하고, 늘어나면 해당 미디어 쿼리 안에 `.btn--icon{min-width:var(--touch); align-self:auto}`를 추가
  4. `app.js`: `// ---- 링크 복사·공유 ----` 섹션 추가 — `canonicalShareUrl(token)`(§4.2), `copyToClipboard(text)`(1차 `navigator.clipboard`), `legacyCopy(text)`(2차 `execCommand`, `position:fixed;top:-1000px;opacity:0` + `aria-hidden` + `setSelectionRange`, `finally`에서 `remove()`)
  5. `app.js` `init()`: 토큰 파싱 성공 후 `#share-btn.hidden = false` + 클릭 리스너 **1회** 배선. 성공 시 `showToast("링크가 복사되었습니다.")`, 3차 폴백 시 `showToast("링크 복사를 지원하지 않는 브라우저입니다. 주소창의 URL을 복사해 주세요.", "warn")`
  6. 토큰 없음/형식 오류(`app.js:252-257`)로 `error` 상태가 되는 경로에서는 버튼을 **노출하지 않는다**(초기 `hidden` 유지)
- **검증 명령**:
  ```
  node --check web/public/app.js
  rg -n "navigator.share" web/public/app.js         # 무매치(§0.3)
  rg -n "aria-label" web/public/index.html          # #share-btn 에 없음
  rg -n "execCommand" web/public/app.js             # 1건(2차 폴백)
  cd web && npm run emulators
  # 시나리오 L1·L2·L3·L4·L14·L15 (§9.2)
  ```
- **완료 기준**:
  - [관측] 유효 토큰 진입 시 헤더 우측에 "공유" 버튼이 보이고 제목은 여전히 중앙 정렬이다. 클릭하면 토스트 "링크가 복사되었습니다."가 뜨고, 붙여넣기 결과가 `{origin}{pathname}?s={token}`이다. `?s=…&utm_source=x`로 진입해도 복사된 URL에 `utm_source`가 **없다**. 320px 폭에서 제목과 버튼이 겹치지 않고 가로 스크롤이 없다.
  - [non-goal] `?s=` 없이 진입하면 버튼이 **노출되지 않는다**. 공유 클릭이 페이지 상태(loading/success/expired/error)를 **바꾸지 않는다**. 다운로드 버튼 동작·상태 머신·미디어 렌더가 **불변**. 공유 시트(`navigator.share`)가 **뜨지 않는다**. 데스크톱 폭에서 공유 버튼이 220px로 늘어나지 **않는다**.
  - [trigger] 복사·토스트는 `#share-btn` 클릭(또는 포커스 후 Enter/Space) **시에만** 발생한다. 페이지 로드·상태 전이만으로는 클립보드에 접근하지 않는다.
- **롤백**: `git checkout -- web/public/` (W2까지 되돌아가므로, 필요하면 W3 변경 라인만 수동 되돌림)
- [ ] 완료

---

### Step W4: 파일명 도출 유틸 + `download` 속성 정정

- **Context Brief**: 현재 다운로드 링크의 `download` 속성은 `mcphoto.jpg`/`mcphoto.mp4`로 하드코딩돼 있다. 그런데 최종 이미지 확장자는 설정 `outputFormat`에 따라 `.jpg` 또는 `.png`이므로 **PNG 세션이 `.jpg`로 이름 붙는 버그**가 있다. 또한 `?s=` 토큰이 곧 sessionId이고 형식이 `{yyyyMMdd}_{HHmmss}_{UUIDv4}`라서, 추가 조회 없이 촬영 시각을 파일명에 넣을 수 있다(토큰에 시각이 드러나는 것은 계약이 이미 수용한 사항). 이 단계는 **파일명 로직만** 만든다 — 자동 저장(W5)은 이 함수를 소비한다.
- **대상 파일**: `web/public/app.js`, `web/public/index.html`
- **선행 조건**: 없음
- **구현 내용**:
  1. `app.js`에 `// ---- 파일명 ----` 섹션 추가. 설계 §6.2의 `TOKEN_STAMP_RE`, `ALLOWED_EXT`, `extFromTokenUrl(url)`, `extFromMime(mime)`, `buildFileName(token, url, mime, kind)`를 그대로 구현
  2. `extFromTokenUrl`은 `try/catch`로 감싸고, 화이트리스트(`jpg`/`jpeg`/`png`/`mp4`)를 통과한 값만 반환한다. **토큰 원문(UUID 포함)을 파일명에 넣지 않는다** — 정규식 캡처한 숫자 8+6자리만 사용
  3. `renderSuccess`: 하드코딩된 `photoDownload.setAttribute("download","mcphoto.jpg")`(`app.js:126`)와 `videoDownload.setAttribute("download","mcphoto.mp4")`(`app.js:159`)를 `buildFileName(token, url, null, "photo"|"video")` 결과로 교체. 이를 위해 `renderSuccess(data)` 시그니처에 `token`을 추가하거나 `init()`에서 클로저로 주입한다(전역 변수 신설 금지)
  4. `index.html:37` `download="mcphoto.jpg"` → `download="MCPhoto.jpg"`, `:61` → `download="MCPhoto_timelapse.mp4"` (JS 미실행 시의 하한값)
- **검증 명령**:
  ```
  node --check web/public/app.js
  rg -n "mcphoto\.(jpg|mp4)" web/public/            # 무매치
  rg -n "MCPhoto\$\{|MCPhoto\`" web/public/app.js    # buildFileName 존재 확인
  cd web && npm run emulators
  # Emulator UI 로 finalImageUrl 을 …/final.png 로, 그리고 …/final.jpg 로 각각 심고
  # devtools Elements 에서 #photo-download 의 download 속성값을 확인
  ```
- **완료 기준**:
  - [관측] `finalImageUrl` 경로가 `final.png`인 문서에서 `#photo-download`의 `download` 속성이 `MCPhoto_{yyyyMMdd}_{HHmmss}.png`이고, `final.jpg`인 문서에서는 `.jpg`다. `#video-download`는 `MCPhoto_{yyyyMMdd}_{HHmmss}_timelapse.mp4`다. 토큰이 형식과 다르면 `MCPhoto.jpg`/`MCPhoto_timelapse.mp4`로 폴백한다.
  - [non-goal] `href` 값·프리뷰 `src`·상태 전이·힌트 가시성이 **전혀 바뀌지 않는다**. cross-origin이라 이 속성이 여전히 무시되는 것도 정상이다(이 단계는 W5의 준비다). 파일명에 `/`·`\`·`..`·공백·UUID가 **포함되지 않는다**.
  - [trigger] 파일명 계산은 `renderSuccess` 실행 시(= 문서 로드 성공 시)에만 일어난다.
- **롤백**: `git checkout -- web/public/app.js web/public/index.html`
- [ ] 완료

---

### Step W5: 자동 저장 코어 (fetch → Blob → `<a download>` + 전역 degrade)

- **Context Brief**: 다운로드 버튼은 토큰 URL(`https://firebasestorage.googleapis.com/...`)을 `href`에 직접 걸고 있고, `download` 속성은 **cross-origin이라 전 브라우저가 무시**한다. 그래서 지금은 원본이 열리기만 하고 사용자가 롱프레스로 저장해야 한다. 이를 고치려면 바이트를 `fetch`로 가져와 `blob:` URL을 만들고(= same-origin 스킴) 그 URL에 `download`를 걸어 프로그램적으로 클릭해야 한다. **버킷 CORS 는 §3.5 실측으로 불필요 판정됐다(인프라 선행 조건 0).** 그러나 **실패 시 현행 동작(원본 내비게이션 + 롱프레스 힌트)으로 되돌아가는 graceful degrade 는 이 단계의 절반으로 그대로 유지한다** — 인앱 브라우저의 `download` 미동작·구형 엔진·네트워크 실패·비2xx(토큰 만료·TTL 삭제)·용량 초과·사용자 활성화 만료가 남아 있다. 실패는 환경이 원인일 때 전역·결정론적이므로 첫 실패에서 능력을 내리고 재시도하지 않는다. 관련 설계: §3.3(흐름) / §3.8(해제 경로) / §8.3(배선 규약).
- **대상 파일**: `web/public/app.js`, `web/public/index.html`, `web/public/styles.css`
- **선행 조건**: **Step W2**(`showToast`), **Step W4**(`buildFileName`), **Step W3**(같은 `init()` 배선 블록을 만지므로 순서 유지)
- **구현 내용**:
  1. `index.html:40-42`·`:64-66`의 수동 힌트 `<p id="photo-hint">`·`<p id="video-hint">`에 **`hidden` 속성 추가**. **문구는 변경하지 않는다**
  2. `app.js` `renderSuccess`의 힌트 제어를 바꾼다: 현행 "URL 있으면 `hint.hidden=false`"(`:109`,`:145`)를 **"자동 저장이 불가/실패로 판정된 경우에만 노출"** 로 교체. URL 없음(옵션 꺼짐) 경로의 `hint.hidden = true`(`:132`,`:166`)는 그대로 유지
  3. `app.js` 상수: `MAX_AUTO_DOWNLOAD_BYTES = 150*1024*1024`, `OBJECT_URL_TTL_MS = 60_000`
  4. `// ---- 자동 저장 ----` 섹션 구현:
     - `supportsAutoDownload()`: `'download' in HTMLAnchorElement.prototype && typeof URL.createObjectURL === 'function' && typeof fetch === 'function'`
     - 모듈 상태 `let autoDownloadEnabled = supportsAutoDownload()`
     - `setBusy(anchor, busy)`: `aria-busy` + `.is-busy` + 라벨 ↔ `"저장 중…"` 교체(원본은 `dataset.idleLabel`)
     - `revealManualHints()`: `#photo-hint`·`#video-hint` 중 **해당 미디어가 present인 것만** `hidden=false`
     - `disableAutoDownload()`: `autoDownloadEnabled=false` + `revealManualHints()`
     - `triggerBlobDownload(blob, filename)`: `createObjectURL` → 임시 `<a>`(`display:none`, `rel="noopener"`) → `appendChild` → `click()` → `remove()` → `setTimeout(OBJECT_URL_TTL_MS)` 로 `revokeObjectURL` 예약. 타이머 id를 Set에 보관
     - `handleDownloadClick(ev, kind)`: §8.3 계약 표대로 구현. `fetch(url, {mode:'cors', credentials:'omit', signal})` — **커스텀 헤더 금지**. `res.ok` 검사 → `Content-Length` 가드(`.blob()` 앞) → `blob()` → `buildFileName(token, url, res.headers.get('content-type'), kind)` → `triggerBlobDownload` → `showToast("저장을 시작했습니다.")` → `revealManualHints()`
     - 실패: `AbortError`면 **무동작 반환**. 그 외는 `console.warn` → `disableAutoDownload()` → `showToast("자동 저장이 지원되지 않는 환경입니다. 원본을 열었으니 길게 눌러 저장해 주세요.", "warn")` → **`location.assign(url)`** (`window.open` 금지)
  5. `init()`: `#photo-download`·`#video-download`에 클릭 리스너를 **1회만** 배선(`renderSuccess` 안에서 배선하지 않는다 — `#retry-btn` 재시도 시 중복 배선되어 fetch가 2회 발생한다). `autoDownloadEnabled === false`면 배선하되 핸들러가 즉시 pass-through하고, 초기 힌트를 노출한다
  6. `pagehide` 리스너(W2에서 신설)에 in-flight `AbortController` 전부 `abort()` + revoke 예약 타이머 `clearTimeout` 추가. **`revokeObjectURL`은 호출하지 않는다** — 진행 중 다운로드 중단 위험. 이 판단을 주석으로 남긴다
  7. `styles.css`: `.btn[aria-busy="true"], .btn.is-busy { opacity:.7; pointer-events:none; cursor:progress }` 추가
  8. **금지 사항 재확인**: `<img>`/`<video>`에 `crossorigin` 추가 금지, `window.open` 금지, `innerHTML` 금지, `sessionStorage` 영속화 금지
- **검증 명령**:
  ```
  node --check web/public/app.js
  rg -n "crossorigin|crossOrigin" web/public/                 # 무매치
  rg -n "window\.open" web/public/app.js                       # 무매치
  rg -n "location\.assign" web/public/app.js                   # 1건
  rg -n "revokeObjectURL" web/public/app.js                    # 1건 이상
  rg -n "credentials: *'omit'|credentials: *\"omit\"" web/public/app.js   # 1건
  rg -n "addEventListener\(\"click\"" web/public/app.js        # renderSuccess 밖(줄번호 확인)
  cd web && npm run emulators
  # 시나리오 L5~L12 (§9.2). CORS 상태에 따라 L5(성공) 또는 L6(폴백) 중 하나가 관측된다 — 둘 다 PASS 조건
  ```
- **완료 기준**:
  - [관측] **CORS 허용 시**: 다운로드 버튼 1탭에 버튼이 "저장 중…"(`aria-busy="true"`)로 바뀌고, 파일이 로컬에 저장되며 파일명이 §6.2 표와 일치하고, 토스트 "저장을 시작했습니다."가 뜬 뒤 수동 힌트가 노출된다.
    **fetch 실패 시**(인앱 브라우저·네트워크·비2xx 등): warn 토스트가 뜨고 원본으로 내비게이션되며(= 현행 동작), 되돌아온 뒤 재클릭 시 fetch 없이 즉시 내비게이션된다. 두 미디어의 수동 힌트가 노출된다. **두 경우 모두 PASS다.**
    버튼 연타 시 Network 탭에 fetch가 **1회만** 기록된다. `#retry-btn`으로 재로드한 뒤 다운로드해도 fetch가 **1회만** 발생한다.
  - [non-goal] 프리뷰(`<img>`/`<video>`)의 로드 동작·`src`가 **전혀 바뀌지 않는다**(`crossorigin` 미추가 grep 무매치). 프리뷰 로드 실패(`.is-disabled`, `href` 제거) 상태에서 클릭해도 **fetch가 발생하지 않는다**. 두 미디어가 모두 옵션 꺼짐(`null`)일 때 힌트·버튼이 노출되지 **않는다**. 상태 머신 4종·만료 fail-safe 판정·`maybeFallbackToExpired` 로직이 **불변**. `web/storage.rules`·`web/firestore.rules`·`web/firebase.json`이 **변경되지 않는다**. 콘솔에 CSP 위반이 **0건**이다.
  - [trigger] fetch·blob 생성·저장·토스트는 다운로드 `<a>` 클릭 **시에만** 발생한다. 페이지 로드·상태 전이·프리뷰 로드만으로는 어떤 네트워크 요청도 추가되지 않는다. 힌트 노출은 ① 기능 감지 실패 또는 ② 첫 저장 시도(성공/실패 무관) **이후에만** 일어난다.
- **롤백**: `git checkout -- web/public/` (W1~W4를 유지해야 하면 W5 변경 라인만 수동 되돌림 — `git diff`로 §8.1 라인 맵의 G1 항목만 되돌린다)
- [ ] 완료

---

### Step W6: 버킷 CORS **불필요 판정** 기록 (코드 변경 0, 인프라 변경 0)

> **갱신 이력 (2026-07-30, 팀 리드 실측)**: 이 단계는 원래 "GET용 `web/cors.json` + 런북 신설"이었다. §3.5 실측으로 **버킷 CORS 설정이 불필요**하다고 판정되어, **`web/cors.json`을 만들지 않는** 것으로 변경했다.

- **Context Brief**: W5의 자동 저장은 브라우저가 `firebasestorage.googleapis.com`의 `alt=media` 응답을 **cors 모드 fetch로 읽을 수 있어야** 성립한다. 이는 `<img src>`(no-cors 서브리소스)와 다른 조건이다. **실측 결과 이 호스트는 서비스 프론트엔드가 `ACAO: *`를 항상 반환하며 버킷 CORS 구성과 무관하다**(§3.5 P1~P3) → 버킷 설정이 필요하지 않다. 이 단계는 그 **판정 근거와 잔여 불확실성**, 그리고 만약의 경우·향후 B5(PUT)를 위한 절차를 문서로 남긴다.
- **대상 파일**: `web/OPS-cors.md`(신규) — **`web/cors.json`은 만들지 않는다**
- **선행 조건**: 없음 (코드와 독립)
- **구현 내용**:
  1. **`web/cors.json`을 생성하지 않는다.** 적용할 일이 없는 설정의 구성 파일은 오해를 만든다. 컨틴전시용 JSON 형태는 `OPS-cors.md` 본문에 인라인으로 둔다
  2. `web/OPS-cors.md` 생성 — `web/OPS-ttl.md`의 표 형식·톤을 따른다. 필수 절:
     - **결론: 설정 불필요** + 실측 근거(§3.5 P1/P2/P3 — 다운로드 호스트 `ACAO: *`, GCS 직접 호스트 대조군 헤더 전무)
     - 다운로드 URL 이 항상 그 호스트임의 근거(`session.ts:73`·`uploads.ts:135`)
     - **⚠️ 403 에서 관측 / 200 확인은 스모크 잔여** — "확인 완료"로 쓰지 않는다
     - 200 이 뒤집혔을 때의 **컨틴전시 절차**(조회·적용 명령 + GET 규칙 JSON 인라인) + `origin:["*"]` 근거
     - **⚠️ B5(PUT) 착수 시 전체 교체(replace) 경고** + 2규칙 병기 형태. PUT 규칙의 `origin`은 `*` 금지
     - **폴백 경로를 유지하는 이유** — CORS 해소와 무관한 실패 요인 목록(인앱 브라우저·구형 엔진·네트워크·비2xx·용량·사용자 활성화)
     - 보안 규칙 무관 명시: `web/storage.rules`는 변경하지 않으며 `results/`의 SDK read는 계속 `false`
  3. **`gcloud`/`gsutil` 명령을 실행하지 않는다.** 이 PC 에 설치돼 있지 않고(`gcloud: NOT FOUND`), 조회가 불필요하다 — §3.5 의 HTTP 실측이 그 자리를 대신한다
- **검증 명령**:
  ```
  test ! -f web/cors.json && echo "cors.json 없음 OK"   # 신설하지 않았음
  rg -n "전체 교체|replace" web/OPS-cors.md              # B5 경고 절 존재
  rg -n "403" web/OPS-cors.md                           # 잔여 불확실성 명시
  git status --short -- web/                            # OPS-cors.md 만 신규
  ```
- **완료 기준**:
  - [관측] `web/cors.json`이 **존재하지 않는다**. `web/OPS-cors.md`에 불필요 판정·실측 근거·403/200 잔여 명시·컨틴전시 절차·B5 전체 교체 경고·폴백 유지 이유·보안 규칙 무관이 모두 있다.
  - [non-goal] `web/storage.rules`·`web/firestore.rules`·`web/firebase.json`·`web/lifecycle.json`이 **변경되지 않는다**. `web/public/**` 코드가 **변경되지 않는다**. `gcloud`/`gsutil` 명령이 **실행되지 않는다**. **실제 버킷 설정이 바뀌지 않는다.**
  - [trigger] 버킷 구성 변경은 §3.5 의 200 경로 판정이 뒤집혀 운영자가 컨틴전시를 직접 실행할 때만 일어난다. 이 단계는 문서까지다.
- **롤백**: `rm web/OPS-cors.md` (실제 버킷은 건드리지 않았으므로 인프라 롤백 불요)
- [x] 완료

---

### Step W7: Emulator 로컬 스모크 + 실배포 플랫폼 매트릭스 검증

- **Context Brief**: `web/public`에는 단위 테스트 하네스가 없다(자동 게이트는 `node --check`와 Emulator 규칙 테스트뿐). 따라서 W1~W6의 실제 동작은 브라우저 관측으로 검증한다. 특히 §2의 미검증 가정 OA-1~OA-8이 여기서 판정된다 — **자동 저장이 실제로 되는지, 안 되면 폴백이 제대로 동작하는지**가 핵심이다. 로컬 검증에는 실제 프로덕션 토큰 URL을 심은 Emulator 문서를 쓴다(그래야 진짜 CORS 응답을 관측할 수 있다). 실배포 검증은 배포 이후에 수행한다 — **배포는 이 단계에서 하지 않고 부모에게 요청한다.**
- **대상 파일**: 없음(검증 전용). 발견된 결함 수정은 해당 Step으로 되돌아가 처리한다
- **선행 조건**: **Step W1~W6 전부**
- **구현 내용**:
  1. §9.1의 자동 게이트 A1~A9를 전부 실행하고 결과를 기록한다
  2. `cd web && npm run emulators` 후 Emulator UI(Firestore)에서 `resultSessions/{token}` 문서를 §9.2 절차대로 생성한다. `finalImageUrl`/`timelapseUrl`에는 **운영자에게 받은 실제 프로덕션 토큰 URL**을 넣는다(없으면 부모에게 요청 — 없이는 OA-2를 판정할 수 없다)
  3. §9.2의 시나리오 **L1~L15를 전부 수행**하고 각각의 관측 결과를 기록한다. 특히 L11(CSP 위반 0)·L8/L10(fetch 1회)·L6(폴백 경로)
  4. devtools Network에서 토큰 URL 응답의 `access-control-allow-origin` 헤더 유무를 확인하고 **OA-1/OA-2의 판정 결과를 기록**한다
  5. 배포 후(부모 승인·수행) `https://mcphoto-955fb.web.app/?s={실제 토큰}`에서 §9.3의 플랫폼 매트릭스를 채운다. 최소 커버리지: **Android Chrome, iOS Safari, 데스크톱 1종**. 인앱 브라우저(카카오톡)는 폴백 확인용
  6. §3.4 표의 예상과 **다른 관측이 나오면 표를 정정**한다. 특히 Android 갤러리 인덱싱, iOS 사진 앱 미저장, iOS 다운로드 확인 시트 유무
  7. §9.4 리뷰어 체크리스트를 자체 점검한다
- **검증 명령**:
  ```
  # 자동 게이트 일괄
  node --check web/public/app.js
  rg -n "MC포토" web/public/ ; rg -n "innerHTML" web/public/app.js
  rg -n "crossorigin|crossOrigin" web/public/ ; rg -n "window\.open" web/public/app.js
  rg -n "revokeObjectURL" web/public/app.js
  cd web && npm run test:rules          # 규칙 무회귀
  git diff --name-only                  # web/ 밖이 없어야 한다
  # 로컬 스모크
  cd web && npm run emulators           # → http://localhost:5000/?s={token}
  ```
- **완료 기준**:
  - [관측] A1~A9 전부 통과(A2·A3·A4·A5 무매치, A8 규칙 테스트 전량 통과, A9에 `web/` 밖 파일 없음). L1~L15 전부 기대 결과와 일치. 콘솔 CSP 위반 0건. `access-control-allow-origin` 헤더의 유무가 문서에 기록되어 OA-1/OA-2가 **판정됨**(허용/차단 중 하나로 확정). §9.3 매트릭스의 3개 필수 플랫폼 칸이 실제 관측으로 채워짐.
  - [non-goal] 이 단계에서 `web/public/**` 코드를 **수정하지 않는다**(결함 발견 시 해당 Step으로 되돌아간다). `firebase deploy`를 **에이전트가 실행하지 않는다**. 실제 버킷 CORS를 에이전트가 **변경하지 않는다**. 규칙 테스트 결과가 W1 이전과 **동일**하다(증감 0).
  - [trigger] 실배포 매트릭스 검증은 **부모가 배포를 완료했다고 알린 뒤에만** 착수한다. 배포 전에는 로컬 스모크까지만 진행하고 `blocked` 사유를 명시해 보고한다.
- **롤백**: 검증 전용이므로 롤백 대상 없음. 결함 발견 시 해당 Step 롤백 절차를 따른다
- [ ] 완료

---

### Step W8: 문서 동기화 (코드 변경 0)

- **Context Brief**: 이 리포지토리는 "실제 소스 > `docs/analysis` > `docs/design`"의 진실원 우선순위를 갖고, 구현이 끝나면 `docs/analysis`를 갱신하는 규칙이 있다(`docs/design/README.md` §4, `docs/analysis/20` 갱신 규칙). W1~W7로 다운로드 페이지의 동작이 바뀌었으므로 분석 문서와 인덱스를 맞춘다. Step W7에서 판정된 OA-1/OA-2 결과(CORS 허용 여부)가 여기 기록의 핵심이다.
- **대상 파일**: `docs/analysis/20-frontend-web-download-page.md`, `docs/design/README.md`, `docs/analysis/90-roadmap-and-future-work.md`, `docs/analysis/50-infra-gcp-lifecycle-and-ttl.md`
- **선행 조건**: **Step W7**(관측 결과 없이는 쓸 수 없다)
- **구현 내용**:
  1. `docs/analysis/20`:
     - 최종 업데이트 날짜·관련 소스 목록 갱신(`web/OPS-cors.md` 추가. **`web/cors.json`은 신설하지 않으므로 넣지 않는다** — §3.5)
     - §7의 "다운로드 폴백 안내" 서술을 새 동작으로 교체 — `<a download>` cross-origin 무시라는 **사실은 유지**하되, blob 경유 자동 저장과 **실패 시 degrade** 규칙, 힌트가 "첫 시도 후 노출"로 바뀐 점을 기술
     - **§9.3 플랫폼 매트릭스를 실측값으로 옮겨 적는다**(iOS는 Files 앱 저장·사진 앱 미저장을 명시)
     - 공유 버튼·토스트·파일명 규칙 절 신설
     - 브랜딩이 `MCPhoto`임을 반영
  2. `docs/design/README.md` — **§0 라우팅 행과 §3.1 등재는 architect가 이미 완료했다.** 링크가 유효한지만 확인하고, 최종 업데이트 행만 갱신한다(중복 등재 금지)
  3. `docs/analysis/90` §B5 항목에 **"B5는 PUT(업로드) 전용이며, 다운로드 GET에는 버킷 CORS가 불필요하다고 it17에서 실측 판정됐다"** 를 명기해 혼동을 막는다(§3.5). 전체 교체 경고와 PUT `origin` `*` 금지도 함께 남긴다
  4. `docs/analysis/50` §5(보안 규칙·토큰 URL) 부근에 **CORS는 Storage 보안 규칙과 별개 레이어**라는 1~2문장 추가 + `OPS-cors.md` 링크
  5. **`docs/design/firebase-contract.md`는 변경하지 않는다** — URL 형식·토큰 규칙·스키마가 하나도 바뀌지 않았다. 변경 불요임을 커밋 메시지/보고에 명시
  6. `docs/analysis/13-client-behavior-spec.md` §12(소비자 클라이언트 플랫폼 중립 규격) 확인: 자동 저장은 **웹 전용 기법**(blob + `<a download>`)이므로 플랫폼 중립 규격에 넣지 않는다. 다만 "결과물 저장은 각 플랫폼의 네이티브 저장 API를 쓴다"는 취지가 이미 있으면 손대지 않는다 — **확인 후 무변경이면 그 사실을 보고**한다
- **검증 명령**:
  ```
  rg -n "web-it17-download-share-design" docs/design/README.md      # 2건 이상(§0 라우팅 + §3.1)
  rg -n "cors" docs/analysis/20-frontend-web-download-page.md       # 1건 이상
  git diff --name-only -- docs/design/firebase-contract.md          # 무출력(무변경)
  git diff --name-only                                              # docs/ 4개 파일만
  ```
- **완료 기준**:
  - [관측] `docs/analysis/20`에 새 다운로드 흐름·플랫폼 매트릭스 실측값·공유 버튼·토스트·파일명 규칙이 기재되고 근거가 `파일:라인` 형식이다. `docs/design/README.md`에서 이 설계 문서로 링크가 걸린다. `docs/analysis/90`에서 B5(PUT)와 it17 GET CORS가 구분된다.
  - [non-goal] `docs/design/firebase-contract.md`가 **변경되지 않는다**(계약 무변경). `web/**` 코드가 **변경되지 않는다**. `docs/analysis/31`(API 참조)·`docs/analysis/14`(미디어 파이프라인)가 **변경되지 않는다** — 백엔드·인코딩 규격이 바뀌지 않았다.
  - [trigger] 문서 갱신은 W7의 실제 관측 결과를 근거로만 작성한다. 관측하지 못한 플랫폼 칸은 **비워 두거나 "미검증"으로 표기**하고, 추정을 사실처럼 쓰지 않는다.
- **롤백**: `git checkout -- docs/`
- [ ] 완료

---

## §11 리스크와 이연 항목

### 11.1 리스크

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| ~~**R1**~~ | ~~버킷 CORS(GET)가 허용되지 않는다~~ → ✅ **해소**(§3.5 실측: 다운로드 호스트가 서비스 레벨 `ACAO: *`). **잔여**: 403 에서만 관측했고 200 확인은 스모크 잔여 | (해소 전 상정) 자동 저장이 전 플랫폼에서 실패 | 잔여분이 현실화해도 §3.3-D 전역 degrade 로 **현행 동작 그대로 유지**(회귀 없음) + `OPS-cors.md` §2 컨틴전시 절차 | W7(200 확인) |
| **R2** | 인앱 브라우저에서 기능 감지는 통과하지만 실제 저장이 안 된다(조용한 실패) | 사용자가 저장됐다고 착각 | 토스트를 "저장을 **시작**했습니다"로 표현(단정 금지) + 저장 시도 후 수동 힌트 상시 노출 | W7 (OA-5) |
| **R3** | `await` 이후 `a.click()`이 사용자 활성화 만료로 차단(OA-8) | 저장 미발생 | 예외로 잡히지 않을 수 있음 → R2와 동일 완화(힌트 노출). 관측되면 §3.4 표 정정 | W7 |
| **R4** | 만료 상태에서도 공유 버튼이 노출된다 | 수신자가 만료 페이지를 본다 | **의도된 수용**(§4.5). 상태 결합도를 만들지 않기로 결정 | — |
| **R5** | 전송량 2배(프리뷰 + 저장 fetch) | 모바일 데이터 소모 | 대상 파일이 단위 MB(§3.7). canvas 재인코딩·`crossorigin` 대안은 프리뷰를 위험에 노출해 기각 | — |
| **R6** | `renderSuccess` 재호출로 리스너 중복 배선 → fetch 2회 | 데이터 2배·중복 저장 | 배선을 `init()` 1회로 고정(§8.3) + 시나리오 L10 회귀 검사 | W5·W7 |
| **R7** | `.btn` 기본 규칙(`width:100%`, 데스크톱 `min-width:220px`)이 헤더 공유 버튼을 왜곡 | 데스크톱 레이아웃 깨짐 | `.btn--icon` 오버라이드 + 미디어 쿼리 내 특이도 확인(§8.4-3) | W3 (L14) |
| **R8** | 힌트 가시성 규칙 교체를 누락 | 힌트가 항상 노출되어 UI 개선 효과 소실 | §8.2 ⚠️ 주의 + W5 완료 기준 [trigger]에 명시 | W5 (L1) |
| **R9** | CSP가 blob 다운로드를 차단(OA-3) | 저장 실패 | 콘솔 위반 관측 시 보고된 `effective-directive`에만 `blob:` 추가(추측 확장 금지) | W7 (L11) |

### 11.2 이연 항목 (이번 범위 밖 — 결정 필요 시 별 이터레이션)

| 항목 | 왜 이연했는가 | 착수 조건 |
|---|---|---|
| **Option A′ — attachment 서명 URL 백엔드 엔드포인트** | CSP `connect-src` 확장 + API 키 노출 또는 무인증 공개 라우트 신설 + Functions 배포가 붙는다 | **R1이 현실화**(CORS 적용 불가 판정)될 때. Option C가 성립하면 불필요 |
| **`navigator.share({files})` — iOS 사진 앱 저장** | 공유 시트에서 사용자가 한 번 더 선택해야 해 "자동"이 아니다. 다만 iOS에서 카메라 롤에 넣는 **유일한** 웹 경로다 | "사진 앱에 저장되게 해달라"는 후속 요구가 나올 때. 보조 버튼("사진 앱에 저장")으로 병치하는 형태를 권장 |
| **다운로드 진행률(%) UI** | 대상 파일이 단위 MB라 라벨 상태로 충분 | 느린 회선 불만이 실제로 보고될 때 |
| **브랜딩 전면 통일(`Directory.Build.props`·OAuth 콜백 HTML·인스톨러)** | 팀 리드가 웹 범위로 한정. WPF까지 섞으면 리뷰 표면이 번진다 | §7.3 표를 근거로 별 이터레이션 |
| **버킷 CORS PUT 규칙(로드맵 B5)** | 웹 업로드(P2/P3) 미착수. **다운로드(GET)에는 불필요**(§3.5) | 웹 P3(공용 프레임 저장) 착수 시. `gsutil cors set`은 **전체 교체**이므로 그 시점의 기존 규칙을 지우지 말 것. PUT 규칙의 `origin`은 `*` 금지. 형태는 `web/OPS-cors.md` §3 |
| **`web/public` JS 린트·단위 테스트 하네스** | 이번 요구와 무관하고 도구 도입 결정이 필요(현재 번들러·린터 0) | 웹 코드가 지금보다 더 커질 때 |

---

## 부록 A. 변경 파일 요약

| 파일 | 종류 | 그룹 | 단계 |
|---|---|---|---|
| `web/public/index.html` | 수정 | G1·G2·G3 | W1, W2, W3, W4, W5 |
| `web/public/app.js` | 수정 | G1·G2·G3 | W1, W2, W3, W4, W5 |
| `web/public/styles.css` | 수정 | G1·G2·G3 | W1, W2, W3, W5 |
| ~~`web/cors.json`~~ | **신설 취소**(§3.5 불필요 판정) | — | W6 |
| `web/OPS-cors.md` | **신규** | G1(인프라 판정 기록) | W6 |
| `docs/analysis/20-frontend-web-download-page.md` | 수정 | 문서 | W8 |
| `docs/design/README.md` | 수정 | 문서 | W8 |
| `docs/analysis/90-roadmap-and-future-work.md` | 수정 | 문서 | W8 |
| `docs/analysis/50-infra-gcp-lifecycle-and-ttl.md` | 수정 | 문서 | W8 |

**변경하지 않는 파일 (명시)**: `web/functions/**`(백엔드 전체), `web/firebase.json`(CSP·헤더), `web/storage.rules`, `web/firestore.rules`, `web/firestore.indexes.json`, `web/lifecycle.json`, `web/package.json`, `web/tests/rules.test.js`, `web/public/firebase-config.js`, `src/**`(WPF 전체), `installer/**`, `docs/design/firebase-contract.md`, `docs/analysis/31`, `docs/analysis/14`, `bldinfo.ini`.

## 부록 B. 완결성 게이트 자체 검사

- [x] 검증된 사실(§1, VF-1~VF-19) / 미검증 가정(§2, OA-1~OA-8) 목록이 분리되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다 (OA-1→**해소됨**(§3.5 실측), OA-2→부분 해소(200 확인 W7 잔여), OA-3~OA-8→W7)
- [x] 모든 단계(W1~W8)에 7개 필수 필드가 채워져 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이다 (UI 단계 W1·W3·W5는 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능한 형태다 (`node --check`, `rg`, `npm run test:rules`, `git diff --name-only`)
- [x] 전체 단계 수 8개 (3~12 범위)
- [x] 모든 부수효과(fetch·AbortController·objectURL·타이머·이벤트 리스너)에 해제 경로가 명시되어 있다 (§3.8)
- [x] 비동기 흐름에 취소(`AbortController`)·오류(전역 degrade) 경로가 있다 (§3.3)
- [x] XSS 취약 지점이 처리되어 있다 (`textContent` 전용, 파일명 화이트리스트, `innerHTML` grep 게이트)
- [x] 권한 거부/오류 UI 상태가 반영되어 있다 (§3.3-D, §4.3 3단 폴백)
- [x] 최악의 경우가 **현행 동작**임이 보장된다 (회귀 없음)
