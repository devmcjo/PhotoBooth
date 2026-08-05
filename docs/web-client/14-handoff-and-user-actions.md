# 14 · 인수인계와 사용자 액션 (Handoff & User Actions)

| 항목 | 값 |
|------|-----|
| 문서 | **코드로는 끝낼 수 없는 작업**(콘솔·시크릿·배포)의 실행 절차와, 그 시점까지 완료된 구현 상태 |
| 대상 독자 | 저장소 소유자(콘솔·시크릿 권한 보유자) |
| 작성일 | 2026-07-31 작성 · **2026-08-01 갱신(Step 12 인증 완료 + A1~A5 전수 재감사)** |
| 브랜치 | `feature/web-client-foundation` |
| 상태 | **WBS Step 0~17 코드 완료 + 사용자 액션 A1~A5 재감사 완료(2026-08-01).** A2는 "완료" 기록이 **거짓이었다**(§3.5). 남은 것은 §10의 실측이다 |

> **왜 이 문서가 따로 있는가**: 코드로 끝낼 수 없는 작업(콘솔 UI·시크릿·공개 배포)의 절차를 남긴다.
> 아래 절차는 재구축·다른 프로젝트 이관·문제 발생 시 참조용으로 유지한다.
>
> 🛑 **2026-08-01 교훈 — "완료"라고 적는 것과 "검증했다"는 것은 다르다.**
> A2는 이 문서에 **✅ 완료**로 기록돼 있었지만 실제로는 `.env`에 플레이스홀더 문자열이 들어 있었고,
> 웹 로그인이 **100% 실패**하고 있었다. 아무도 몰랐던 이유는 **완료를 적을 때 "명령을 실행했다"만
> 확인하고 "값이 올바른가"·"실제로 동작하는가"를 확인하지 않았기 때문이다.**
> → **이제 각 절 끝의 "재감사" 표에 *무엇으로* 확인했는지를 함께 적는다.** 확인 수단이 없으면
> "완료"가 아니라 **"코드로 검증 불가 — 사람이 ○○로 확인해야 함"** 이라고 적는다.

---

## 1. 30초 요약

| # | 사용자 액션 | 상태(2026-08-01 재감사) | 무엇으로 확인했나 |
|---|-------------|--------------------------|-------------------|
| **A1** | Google Cloud Console에 **Web application** OAuth 클라이언트 생성 | **간접 확인**(콘솔 화면은 코드로 볼 수 없다) | 발급물이 존재하고 형식·유일성이 맞다: `webclient/.env.production.local`의 `VITE_GOOGLE_CLIENT_ID`가 **72자 · `.apps.googleusercontent.com` · desktop 값과 다름**. 리디렉트 URI 3개 등록 여부는 **사람이 콘솔에서 확인**해야 한다(§2.3) |
| **A2** | 웹 OAuth **시크릿·env 등록** + functions 재배포 | ⚠️ **"완료" 기록이 거짓이었다**(§3.5). 로컬 `.env`는 2026-08-01 교정됨 · **배포 반영 여부는 사람 확인 필요** | 로컬 값 = 웹 빌드 값과 **바이트 일치**(§3.5). 배포본은 §3.6 프로브로 정황만 확인 |
| **A3** | 웹 전용 **배포 게이트 키** 발급·등록 | **✅ 실측 재확인** | 배포된 서버에 **웹 키 → 200 / 임의 문자열 → 401**(§4.5를 2026-08-01 재실행) |
| **A4** | Storage 버킷 **CORS**(업로드 PUT) | **✅ 실측 재확인** | preflight 실측 재실행 — 허용 오리진에 `Allow-Methods: GET,PUT,HEAD` + `x-goog-meta-firebaseStorageDownloadTokens`(§5.3) |
| **A5** | kiosk 사이트 **첫 배포** + P1 무변경 확인 | **✅ 실측 재확인**(사이트·헤더). ⚠️ **서빙 중인 번들이 최신 코드인지는 별 문제**다 | `curl -sI`로 200 + CSP + nosniff + `Cache-Control: no-cache, max-age=0`(§6.4) |

- 재수행이 필요하면 A1~A3은 순서대로(A1의 산출물이 A2의 입력), A4·A5는 독립이다.
- **Step 15(프레임 편집기·피커·삭제)까지 코드가 완성됐다**(2026-08-01). 게스트 완주 경로(마일스톤 A) + Google SSO 로그인 + 설정 화면 + 프레임 카탈로그·대기 4국면·삭제 + **프레임 저작(생성·편집·서버 등록)** 이 구현돼 있고, 남은 실측은 §10.1~§10.6·**§10.8**·**§10.9**·**§10.10**이다.
  - ⚠️ 로컬 개발 로그인은 **`http://localhost:5173`** 에서만 성공한다(A1 등록 URI·서버 허용 목록과 완전 일치해야 한다). `vite.config.ts`가 `strictPort: true`로 포트를 고정한다.

---

## 2. A1 · Google OAuth 웹 클라이언트 생성

### 2.1 왜 새로 만드나

현재 등록된 것은 **Desktop app** 유형 1개(Windows 앱용)다. OAuth 클라이언트는 **유형이 다르면 공유할 수 없다** — 웹은 `https://` 리디렉트를 쓰고 Desktop은 loopback을 쓴다. 동의 화면은 프로젝트 단위라 **추가 작업이 없다**.

### 2.2 절차

1. [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → 프로젝트 `mcphoto-955fb`
2. **APIs & Services → Credentials → Create Credentials → OAuth client ID**
3. Application type: **Web application**
4. Name: `MCPhoto Web Kiosk`
5. **Authorized JavaScript origins**
   ```
   https://mcphoto-955fb-kiosk.web.app
   http://localhost:5173
   ```
6. **Authorized redirect URIs** — **완전 일치**해야 한다(문자 하나 다르면 Google이 거부한다)
   ```
   https://mcphoto-955fb-kiosk.web.app/oauth2callback
   https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback
   http://localhost:5173/oauth2callback
   ```
7. 발급된 **client_id**와 **client_secret**을 보관한다(A2·A5에서 쓴다)

| 주의 | 이유 |
|------|------|
| `firebaseapp.com` 도메인을 **빠뜨리지 않는다** | Hosting은 `web.app`·`firebaseapp.com` 두 도메인을 함께 서빙한다. 누락하면 그 도메인으로 접속한 기기에서만 로그인이 실패해 원인 파악이 어렵다 |
| Hosting preview channel 도메인 | 채널 URL에 해시가 붙어 고정할 수 없다. **개발은 `localhost`, 실기기 검증은 운영 사이트**를 쓴다 |
| `GOOGLE_ALLOWED_HD` | 도메인 제한을 쓰고 있다면 웹에도 **그대로 적용**된다(종류 무관 공통). 변경 불필요 |

### 2.3 재감사 (2026-08-01)

| 확인 항목 | 결과 | 근거 |
|---|---|---|
| 웹 client_id가 **존재**한다 | ✅ | `webclient/.env.production.local`의 `VITE_GOOGLE_CLIENT_ID`가 **72자** |
| **형식**이 맞다 | ✅ | `.apps.googleusercontent.com`으로 끝난다 |
| **Web application 유형**이다(= desktop 것을 재사용하지 않았다) | ✅ 간접 | 서버 env의 desktop `GOOGLE_OAUTH_CLIENT_ID`와 **값이 다르다**(SHA-256 앞 12자 대조로 확인). 유형 자체는 콘솔에서만 보인다 |
| **Authorized redirect URIs 3개**가 등록돼 있다 | ⚠️ **코드로 검증 불가** | 서버 `OAUTH_REDIRECT_ALLOWLIST`가 3개인 것은 확인했지만(§3.5), 그것은 **우리 서버의 목록**이지 Google 콘솔의 목록이 아니다. 어긋나 있으면 Google이 리디렉트 단계에서 거부한다 → **사람이 콘솔 화면에서 3줄을 눈으로 대조**하거나 **§10.6 V21-1(실 로그인 1회)** 로 종단 확인해야 한다 |
| **Authorized JavaScript origins 2개** | ⚠️ **코드로 검증 불가** | 동상. V21-1·V21-6로 확인한다 |

> 즉 A1은 **"발급물은 확실히 있고 형식·유일성도 맞다. 콘솔 등록 내용은 사람만 볼 수 있다"** 가 정확한 상태다.
> 종단 검증 1회(**§10.6 V21-1**)가 위 두 ⚠️를 한 번에 닫는다 — 그것이 A1의 진짜 완료 조건이다.

---

## 3. A2 · 웹 OAuth 시크릿·env 등록

> 🛑 **이 절의 "완료" 기록은 거짓이었다.** 무슨 일이 있었고 어떻게 검증했어야 했는지는 **§3.5**에 있다.
> 재수행할 때는 §3.3의 명령을 실행한 뒤 **반드시 §3.5의 검증 4단계**를 돌려라.

### 3.1 서버가 이미 준비된 것 (이 브랜치에서 완료)

`POST /auth/google`이 **종류별 OAuth 클라이언트**를 지원한다(`clientKind: "desktop" | "web"`, 미지정 = `desktop`). 계약 상세는 [analysis/31 §4.2](../analysis/31-backend-api-reference.md).

| 환경변수 | 종류 | 값 |
|----------|------|-----|
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | env (`functions/.env.mcphoto-955fb`) | A1의 client_id |
| `GOOGLE_OAUTH_CLIENT_SECRET_WEB` | **secret** | A1의 client_secret |
| `OAUTH_REDIRECT_ALLOWLIST` | env (같은 파일) | A1의 redirect URI 3개를 **CSV로** |

### 3.2 ⚠️ 배포 순서 주의 (먼저 읽을 것)

이 브랜치는 `src/index.ts`에 **`defineSecret("GOOGLE_OAUTH_CLIENT_SECRET_WEB")`을 추가했다.** 선언된 시크릿은 **배포 시점에 반드시 존재해야 한다** — 등록하지 않고 `firebase deploy --only functions`를 실행하면 **배포가 실패한다**.

이것은 기존 `GOOGLE_OAUTH_CLIENT_SECRET`과 **동일한 패턴**이다(그때도 SSO 미사용 상태에서 placeholder를 등록해 뒀다). SSO를 아직 안 쓸 계획이면 **placeholder라도 등록**해 두면 된다 — `GOOGLE_OAUTH_CLIENT_ID_WEB`(env)이 비어 있으면 웹 종류는 그냥 비활성이고(요청 시 501), **Windows(desktop) 경로는 영향받지 않는다**.

### 3.3 절차

> ⚠️ **`web/` 안에서 실행한다.** 프로젝트를 지정하는 `.firebaserc`가 거기에만 있어서,
> 다른 폴더(예: `webclient/`)에서 실행하면 *"No currently active project"* 로 실패한다.
> 아래 명령은 `--project`를 명시해 두었으므로 위치와 무관하게 동작한다(`firebase use --add`는 불필요).

```bash
cd web

# 1) 시크릿 등록(프롬프트에 client_secret 입력). 당장 SSO를 안 쓸 거면 placeholder라도 넣는다.
npx firebase functions:secrets:set GOOGLE_OAUTH_CLIENT_SECRET_WEB --project mcphoto-955fb

# 2) env 등록 — functions/.env.mcphoto-955fb 에 **두 줄 추가**
#    (이 저장소는 env가 둘로 나뉜다: .env = 공통, .env.<프로젝트id> = 프로젝트별.
#     기존 GOOGLE_OAUTH_CLIENT_ID(Desktop)가 프로젝트별 파일에 있으므로 웹 값도 같은 파일에 넣는다.
#     Firebase가 두 파일을 모두 읽어 배포한다. gitignore 대상이라 커밋되지 않는다.)
#
#    OAUTH_REDIRECT_ALLOWLIST 는 A1에 등록한 URI 3개와 **문자 하나까지 같아야** 한다(콤마 구분·공백 없음).
#    기존 GOOGLE_OAUTH_CLIENT_ID 는 지우지 않는다 — Windows(desktop) 로그인이 그 값을 쓴다.
#    (PowerShell 명령은 아래 참조)

# 3) 재배포(시크릿·env 변경은 재배포가 필요하다)
deploy-web.bat functions
```

**2)를 PowerShell로** — 편집기로 열어 두 줄을 붙여 넣어도 결과는 같다.

> 🛑 **아래 명령의 `<…>` 를 반드시 실제 값으로 치환하라.** 치환하지 않으면 배포는 **성공하고**
> 웹 로그인만 조용히 100% `invalid_client`로 실패한다 — **2026-08-01에 실제로 발생했다**
> (`GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>` 문자열이 그대로 배포됐다).
> 이제 `deploy-web.bat`이 배포 직전에 `npm run check:env`로 이 실수를 막는다.

```powershell
cd E:\Study\photobooth\web\functions
Add-Content .env.mcphoto-955fb "GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>"
Add-Content .env.mcphoto-955fb "OAUTH_REDIRECT_ALLOWLIST=https://mcphoto-955fb-kiosk.web.app/oauth2callback,https://mcphoto-955fb-kiosk.firebaseapp.com/oauth2callback,http://localhost:5173/oauth2callback"

# 확인(값은 보지 않고 키만)
Select-String -Path .env.mcphoto-955fb -Pattern '^[A-Z_]+' | ForEach-Object { ($_ -split '=')[0] }
```

| 값 | 무엇인가 |
|-----|----------|
| `GOOGLE_OAUTH_CLIENT_ID_WEB` | A1에서 만든 **Web application** 클라이언트의 client_id(`….apps.googleusercontent.com`). 비밀이 아니다. ⚠️ 값은 `webclient/.env.production.local`의 `VITE_GOOGLE_CLIENT_ID`와 **문자 단위로 같아야** 한다 |
| `OAUTH_REDIRECT_ALLOWLIST` | 위 문자열 그대로. 서버가 이 목록과 **완전 일치**하는 `redirectUri`만 통과시킨다([analysis/31 §4.2](../analysis/31-backend-api-reference.md)) |

### 3.4 검증

| 확인 | 방법 | 기대 |
|------|------|------|
| desktop 경로 무회귀 | Windows 앱으로 로그인 | 종전과 동일하게 성공 |
| 웹 종류 활성 | `POST /auth/google`에 `clientKind:"web"` + 임의 code | **501이 아니라 401**(구성은 됐고 code가 가짜라서 거부) |
| 허용목록 밖 거부 | `redirectUri`를 `https://evil.com/oauth2callback`로 | **400** |
| 서버 회귀 | `cd web/functions && npm test` | 전량 통과 |

> `clientKind`를 **미지정**하면 desktop으로 해석된다 — 배포된 Windows 클라이언트는 **무변경으로 계속 동작**한다.
>
> ⚠️ **위 "웹 종류 활성" 검사만으로는 2026-08-01 사고를 잡지 못했다.** 그때 배포본은 F2(501 분리) 이전
> 코드였고, 플레이스홀더 client_id로도 **똑같이 401**이 나왔기 때문이다. 지금은 F2가 들어가 있어
> `invalid_client`/`unauthorized_client`가 **501**로 갈라지지만, **그 판정은 배포된 뒤에만 유효하다.**
> 그래서 §3.5의 **로컬 값 검사**가 여전히 1차 방어선이다.

### 3.5 🛑 사고 기록 — "완료"로 적혀 있었으나 실제로는 플레이스홀더였다 (2026-08-01)

**무슨 일이 있었나**

이 문서에는 A2가 **"✅ 완료"** 로 기록돼 있었다. 그러나 서버 env의 실제 값은 다음과 같았다.

```
GOOGLE_OAUTH_CLIENT_ID_WEB = [len=17]  ← "<A1의 웹 client_id>" 문자열 그대로
```

§3.3의 예시 명령을 **`<…>`를 치환하지 않고 그대로 실행**했고, 그 결과가 그대로 배포됐다.

**왜 아무도 몰랐나 — 실패가 조용했다**

| 단계 | 그때 관측된 것 | 왜 신호가 없었나 |
|------|----------------|------------------|
| `Add-Content` 실행 | 오류 없음 | 셸은 문자열을 그대로 넣을 뿐이다 |
| `firebase deploy` | **성공** | 배포는 값의 의미를 검사하지 않는다 |
| 문서의 확인 명령 | `GOOGLE_OAUTH_CLIENT_ID_WEB` 키가 보임 | **키 이름만 출력하는 명령**이었다(§3.3의 `Select-String … ($_ -split '=')[0]`) — 값이 무엇이든 통과한다 |
| 손님 로그인 | "이 Google 계정으로는 로그인할 수 없습니다" | 서버가 Google의 `invalid_client`를 **401로 뭉갰다** → **구성 오류가 계정 문제로 보였다** |
| 진단 모달 | OAuth 관련 행이 **없었다** | 게이트 키만 "설정됨/미설정"을 보여 줬다 |

**어떻게 검증했어야 했나 (이제 이 4단계를 반드시 돌린다)**

```powershell
cd E:\Study\photobooth\web\functions

# 1) 길이 + 접미사 — 플레이스홀더(17자·꺾쇠)는 여기서 즉시 걸린다. 값은 출력하지 않는다.
node -e "const fs=require('fs');const p='.env.mcphoto-955fb';const kv=Object.fromEntries(fs.readFileSync(p,'utf8').split(/\r?\n/).filter(l=>l.includes('=')).map(l=>[l.slice(0,l.indexOf('=')).trim(),l.slice(l.indexOf('=')+1).trim()]));for(const k of ['GOOGLE_OAUTH_CLIENT_ID','GOOGLE_OAUTH_CLIENT_ID_WEB']){const v=kv[k]||'';console.log(k,'len='+v.length,'suffix_ok='+v.endsWith('.apps.googleusercontent.com'),'placeholder='+(v.includes('<')||v.includes('>')));}"

# 2) desktop 값과 다른가 + 웹 빌드 값과 같은가 — 값 대신 해시 앞 12자만 비교한다.
node -e "const fs=require('fs'),c=require('crypto');const rd=p=>Object.fromEntries(fs.readFileSync(p,'utf8').split(/\r?\n/).filter(l=>l.includes('=')).map(l=>[l.slice(0,l.indexOf('=')).trim(),l.slice(l.indexOf('=')+1).trim()]));const s=rd('.env.mcphoto-955fb'),w=rd('../../webclient/.env.production.local');const h=x=>c.createHash('sha256').update(x||'').digest('hex').slice(0,12);console.log('server web ',h(s.GOOGLE_OAUTH_CLIENT_ID_WEB));console.log('client web ',h(w.VITE_GOOGLE_CLIENT_ID));console.log('desktop    ',h(s.GOOGLE_OAUTH_CLIENT_ID));console.log('web==client?',s.GOOGLE_OAUTH_CLIENT_ID_WEB===w.VITE_GOOGLE_CLIENT_ID);console.log('web==desktop?',s.GOOGLE_OAUTH_CLIENT_ID_WEB===s.GOOGLE_OAUTH_CLIENT_ID);"

# 3) 배포 직전 게이트(이제 deploy-web.bat이 자동으로 돌린다)
npm run check:env

# 4) ★ 종단 검증 — 실 Google 계정으로 **한 번 로그인해 본다**(§10.6 V21-1).
#    1~3은 "형식이 맞다"까지만 보장한다. "Google이 이 client_id를 아는가"는 로그인 1회로만 확정된다.
```

기대값:

| 검사 | 정상 |
|---|---|
| 1) | `len=72` · `suffix_ok=true` · `placeholder=false` (두 키 모두) |
| 2) | `web==client? true` · `web==desktop? false` |
| 3) | `OK — 플레이스홀더 없음` |
| 4) | 상단 계정 라벨이 계정 id로 바뀌고 직전 화면으로 복귀 |

**재발 방지로 들어간 것**

| # | 무엇 | 어디 |
|---|------|------|
| 1 | 배포 직전 **플레이스홀더 게이트** — 값에 `<`·`>`가 있거나 필수 키가 비면 배포를 막는다 | `web/functions/scripts/check-env-placeholders.mjs` + `domain/envPlaceholder.ts`(순수 판정 · 테스트 있음). `deploy-web.bat`이 자동 실행 |
| 2 | **`invalid_client`/`unauthorized_client` → 501 분리** — 구성 오류가 더 이상 "계정 거부(401)"로 보이지 않는다 | `services/googleAuth.ts`(`isClientCredentialError`) + `routes/auth.ts`(`mapGoogleAuthError`) |
| 3 | **진단 모달의 [웹 OAuth 구성] 행** — 운영자가 화면에서 `설정됨 / 형식 오류(값 미치환 의심) / 미설정`을 본다(값은 노출하지 않는다) | `GET /health`의 `oauth` 필드(`domain/oauthStatus.ts`) → `diagnosticsPresenter.oauthRows` |
| 4 | **[마지막 로그인 실패] 행** — 사유 열거값 + 시각 | `diagnosticsPresenter` |

### 3.6 재감사 (2026-08-01)

| 확인 항목 | 결과 | 근거 |
|---|---|---|
| **로컬** `.env.mcphoto-955fb`의 웹 client_id | ✅ 교정됨 | 72자 · `.apps.googleusercontent.com` · 꺾쇠 없음 |
| 웹 빌드 값과 **바이트 일치** | ✅ | `GOOGLE_OAUTH_CLIENT_ID_WEB` ≡ `VITE_GOOGLE_CLIENT_ID`(해시 대조) |
| desktop 값과 **다름** | ✅ | 해시가 다르다 |
| `OAUTH_REDIRECT_ALLOWLIST` 3개 | ✅ | `…kiosk.web.app/oauth2callback` · `…kiosk.firebaseapp.com/oauth2callback` · `http://localhost:5173/oauth2callback` — §2.2 목록과 문자 단위 일치 |
| **배포본에 반영됐는가** | ⚠️ **사람 확인 필요** | 정황은 "반영됨"에 가깝다: 배포된 함수의 `deployedAt`(`2026-08-01T08:33:00.538Z`)이 로컬 `web/functions/lib/build-stamp.json`과 **ms 단위까지 같고**, 그 로컬 빌드에는 F2가 들어 있다. 그 상태에서 허용 redirectUri + 가짜 code 프로브가 **501이 아니라 401**을 돌려준다 → 배포 env의 web client_id는 플레이스홀더가 아니다. **다만 서버 로그·콘솔을 볼 수 없으므로 단정하지 않는다** — §10.6 V21-1(실 로그인 1회)로 확정하라 |
| `GOOGLE_OAUTH_CLIENT_SECRET_WEB`(Secret Manager) | ⚠️ **코드로 검증 불가** | 시크릿 값은 저장소에 없다. `npx firebase functions:secrets:access GOOGLE_OAUTH_CLIENT_SECRET_WEB --project mcphoto-955fb`로 **사람이** 확인하거나, V21-1로 종단 확인한다 |

---

## 4. A3 · 웹 전용 배포 게이트 키

### 4.1 왜 별 키인가

게이트 키는 **인증이 아니라 배포 식별자**다(WD10). 웹 앱의 키는 브라우저에 **공개될 것을 전제**하며, 유출되면 **그 키만 폐기**하고 웹을 재배포한다. 역할·과금 한도는 서버가 JWT로 강제하므로 키 공개로 권한이 새지 않는다.

### 4.2 기존 키를 어디서 얻는가 (먼저 읽을 것)

`CLIENT_API_KEYS`는 **하나가 아니라 CSV 목록**이다. 웹 키를 "추가"하는 것이므로 **기존 값을 먼저 읽어야** 한다.
값이 있는 곳은 두 군데인데 **진실원은 Secret Manager**다.

| 출처 | 무엇 | 신뢰도 |
|------|------|--------|
| **Secret Manager** (`CLIENT_API_KEYS`) | **배포된 서버가 실제로 쓰는 전체 목록** | ★ 진실원 |
| `backend-apikey.local` (저장소 루트, gitignore) | Windows exe에 빌드 시 심는 **키 1개** | 참고용(교차 확인) |

```powershell
# 현재 등록된 전체 값을 출력한다 — 이 명령이 "기존 키"의 답이다
cd E:\Study\photobooth\web
npx firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb
```

- 출력이 한 줄이면 키가 1개(Windows용), 콤마가 있으면 이미 여러 개다.
- `backend-apikey.local`의 값이 **그 목록 안에 있어야** 정상이다(없다면 exe와 서버가 어긋난 상태 — 그것부터 확인).
- 출력된 값은 반비밀이다. 화면 공유·이슈·채팅에 붙여 넣지 않는다.

### 4.3 절차 (PowerShell, 복사해서 그대로)

> 안전장치: `secrets:set`은 **새 버전을 만들 뿐**이고, 배포된 함수는 재배포 전까지 **기존 버전을 계속 쓴다.**
> 즉 3단계(재배포) 전에는 실서비스에 영향이 없다 — **2단계에서 값을 눈으로 확인한 뒤** 배포한다.

```powershell
cd E:\Study\photobooth\web

# 1) 현재 값 읽기 + 새 웹 키 생성 + 합치기
$existing = (npx firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb | Out-String).Trim()
$webKey   = (node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))").Trim()
$combined = "$existing,$webKey"

# 2) ★ 배포 전 확인 — 키 개수가 (기존 + 1)인지, 줄바꿈·공백이 섞이지 않았는지
"키 개수: " + ($combined -split ',').Count
"길이: " + $combined.Length
$combined -split ',' | ForEach-Object { "  - " + $_.Substring(0, 6) + "…(" + $_.Length + "자)" }

# 3) 확인됐으면 등록(긴 문자열 오타를 막으려 임시 파일로 넘긴다)
$tmp = Join-Path $env:TEMP "mcphoto-client-keys.txt"
Set-Content -Path $tmp -Value $combined -NoNewline -Encoding ascii
npx firebase functions:secrets:set CLIENT_API_KEYS --project mcphoto-955fb --data-file $tmp
Remove-Item $tmp

# 4) 재배포(이때부터 새 키가 유효해진다)
.\deploy-web.bat functions

# 5) 새 웹 키를 확인해 둔다 — A5의 .env.production.local 에 넣을 값이다
$webKey
```

| 단계에서 확인할 것 | 정상 |
|---|---|
| 2)의 "키 개수" | **기존 개수 + 1**. 1이 나오면 `$existing`이 비었다는 뜻이니 **중단**하고 4.2부터 다시 |
| 2)의 각 키 길이 | 모두 40자 이상. 한 자리 수가 있으면 줄바꿈이 섞인 것이다 |
| 3) 출력 | `Created a new secret version …` |

> ⚠️ **기존 키를 지우면 배포된 Windows 앱이 즉시 401로 죽는다.** 위 절차는 기존 값을 읽어 뒤에 덧붙이는 형태라 안전하다. 수동으로 입력할 때는 **기존 값 + 콤마 + 새 키** 전체를 넣어야 한다(새 키만 넣으면 기존 키가 사라진다).

### 4.4 잘못 등록했다면

재배포 전이면 **아무 일도 일어나지 않았다** — 올바른 값으로 3)을 다시 실행하면 된다(새 버전이 또 생기고 최신 것이 쓰인다).
이미 재배포해 Windows 앱이 401이 된다면, 4.2로 이전 값을 확인해 올바른 CSV로 다시 등록하고 재배포한다.
Secret Manager는 버전을 보관하므로 이전 값도 조회할 수 있다:

```powershell
npx firebase functions:secrets:get CLIENT_API_KEYS --project mcphoto-955fb          # 버전 목록
npx firebase functions:secrets:access CLIENT_API_KEYS@1 --project mcphoto-955fb     # 1번 버전 값
```

### 4.5 검증

재배포가 끝난 뒤 실행한다(배포 전에는 이전 키 목록이 살아 있어 의미가 없다).

```powershell
$url = "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/frames/default"

# 새 웹 키 → 200
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = $webKey } -SkipHttpErrorCheck).StatusCode

# 아무 문자열 → 401 (게이트가 실제로 동작하는지)
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = "bogus" } -SkipHttpErrorCheck).StatusCode

# 기존 Windows 키도 여전히 200인지 (회귀 확인 — 가장 중요하다)
$winKey = (Get-Content ..\backend-apikey.local -Raw).Trim()
(Invoke-WebRequest $url -Headers @{ "X-MCPhoto-Client" = $winKey } -SkipHttpErrorCheck).StatusCode
```

기대: **200 · 401 · 200**. 세 번째가 401이면 기존 키가 목록에서 빠진 것이니 4.4로 되돌린다.

> `$webKey` 변수가 사라졌다면 4.2 명령으로 목록을 다시 읽어 **마지막 키**를 쓴다.
> `-SkipHttpErrorCheck`는 PowerShell 7 이상에서 쓸 수 있다(없으면 401에서 예외가 난다).

> `GET /health`로는 키 유효성을 판정할 수 없다 — **키가 없거나 틀려도 200이다**(06 §2.1). 위처럼 `/frames/default`의 401 여부로 확인한다.

### 4.6 재감사 (2026-08-01)

**A2와 달리 A3은 실측으로 닫혔다** — 게이트 키는 배포된 서버에 물어보면 즉시 참/거짓이 갈리기 때문이다.

| 확인 항목 | 결과 | 근거(2026-08-01 재실행) |
|---|---|---|
| 웹 키가 **존재**하고 형식이 맞다 | ✅ | `VITE_BACKEND_API_KEY` **43자**(= 32바이트 base64url) · 꺾쇠 없음 |
| Windows 키와 **다르다**(§6.2 경고) | ✅ | `backend-apikey.local`(48자)과 값·길이 모두 다르다 |
| 배포된 서버가 웹 키를 **받아들인다** | ✅ **실측** | `GET /frames/default` + `X-MCPhoto-Client: <웹 키>` → **200** |
| 게이트가 **실제로 동작**한다 | ✅ **실측** | 같은 요청에 `X-MCPhoto-Client: bogus` → **401** |
| Windows 키 무회귀 | ⚠️ 미실행 | §4.5의 세 번째 명령(`backend-apikey.local`로 200)은 **Windows 키를 네트워크로 보내는 것**이라 이번 재감사에서는 돌리지 않았다. Windows 앱 로그인 1회로 대신 확인하는 편이 안전하다 |
| `CLIENT_API_KEYS` **전체 목록**의 내용 | ⚠️ **코드로 검증 불가** | Secret Manager 값은 저장소에 없다. 필요하면 §4.2 명령으로 **사람이** 확인한다. 다만 위 200/401 실측이 "웹 키가 목록에 들어 있다"는 결론에는 충분하다 |

---

## 5. A4 · Storage 버킷 CORS (업로드 PUT)

### 5.1 현재 상태

- **다운로드 GET은 CORS 구성이 불필요하다** — `firebasestorage.googleapis.com`이 항상 `Access-Control-Allow-Origin: *`를 반환한다(실측 기록: [`web/OPS-cors.md`](../../web/OPS-cors.md)). 따라서 **서버 프레임 이미지를 canvas로 합성하는 경로(WM2)는 이미 열려 있다.**
- **업로드 PUT은 구성이 필요하다** — 서명 URL의 호스트는 `storage.googleapis.com`이고 브라우저 PUT은 preflight를 보낸다.
- 이 PC에 **`gcloud`가 설치돼 있지 않다**(같은 실측 기록). → **Cloud Shell**을 쓰는 것이 가장 빠르다.

### 5.2 절차 (Cloud Shell)

[Cloud Shell 열기](https://console.cloud.google.com/?cloudshell=true) → 아래를 붙여넣는다.

```bash
cat > /tmp/cors.json <<'EOF'
[
  {
    "origin": [
      "https://mcphoto-955fb-kiosk.web.app",
      "https://mcphoto-955fb-kiosk.firebaseapp.com",
      "http://localhost:5173"
    ],
    "method": ["GET", "PUT", "HEAD"],
    "responseHeader": [
      "Content-Type",
      "x-goog-meta-firebaseStorageDownloadTokens",
      "x-goog-resumable"
    ],
    "maxAgeSeconds": 3600
  }
]
EOF

gcloud storage buckets update gs://mcphoto-955fb.firebasestorage.app --cors-file=/tmp/cors.json
gcloud storage buckets describe gs://mcphoto-955fb.firebasestorage.app --format="default(cors_config)"
```

| 항목 | 이유 |
|------|------|
| `responseHeader`에 `x-goog-meta-firebaseStorageDownloadTokens` | 서명 PUT의 `requiredHeaders`에 이 헤더가 들어 있다. 허용하지 않으면 preflight가 막혀 **PUT이 아예 안 나간다**(M14 파손) |
| `method`에 `PUT` | 업로드 본체 |
| `origin`에 `firebaseapp.com` | Hosting 두 번째 기본 도메인 |
| `localhost:5173` | 로컬 개발에서 업로드를 시험할 때 |

### 5.3 검증

**✅ 2026-07-31 적용·검증 완료.** preflight를 직접 쏘면 `gcloud` 없이도 확인할 수 있다(어느 PC에서든).

```powershell
# 허용 오리진 → Access-Control-* 헤더가 돌아온다
curl.exe -s -o NUL -D - -X OPTIONS `
  -H "Origin: https://mcphoto-955fb-kiosk.web.app" `
  -H "Access-Control-Request-Method: PUT" `
  -H "Access-Control-Request-Headers: content-type,x-goog-meta-firebasestoragedownloadtokens" `
  "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/cors-probe-nonexistent" |
  Select-String -Pattern "HTTP/|access-control"

# 허용 목록 밖 → Access-Control 헤더가 **없어야** 한다(차단)
curl.exe -s -o NUL -D - -X OPTIONS `
  -H "Origin: https://evil.example.com" -H "Access-Control-Request-Method: PUT" `
  "https://storage.googleapis.com/mcphoto-955fb.firebasestorage.app/cors-probe-nonexistent" |
  Select-String -Pattern "HTTP/|access-control"
```

실측 결과(허용 오리진):

```
Access-Control-Allow-Origin: https://mcphoto-955fb-kiosk.web.app
Access-Control-Allow-Methods: GET,PUT,HEAD
Access-Control-Allow-Headers: Content-Type,x-goog-meta-firebaseStorageDownloadTokens,x-goog-resumable
```

> 객체가 없어도(`cors-probe-nonexistent`) preflight는 **버킷 수준**에서 처리되므로 인증 없이 확인된다.
> M14가 요구하는 `x-goog-meta-firebaseStorageDownloadTokens`가 `Allow-Headers`에 있어야 서명 PUT이 통과한다.

Step 11 구현 후에는 브라우저 Network 탭에서 실제 `OPTIONS 204 → PUT 200`을 최종 확인한다.

### 5.4 재감사 (2026-08-01)

§5.3의 두 명령을 그대로 재실행했다. **버킷 구성이 유지되고 있다.**

| 확인 항목 | 결과 | 실측 |
|---|---|---|
| 허용 오리진 preflight | ✅ | `HTTP/1.1 200` + `Access-Control-Allow-Origin: https://mcphoto-955fb-kiosk.web.app` · `Allow-Methods: GET,PUT,HEAD` · `Allow-Headers: Content-Type,x-goog-meta-firebaseStorageDownloadTokens,x-goog-resumable` |
| M14 요구 헤더가 허용 목록에 있다 | ✅ | `x-goog-meta-firebaseStorageDownloadTokens` 포함 — 없으면 서명 PUT이 아예 안 나간다 |
| 허용목록 밖 차단 | ✅ | `Origin: https://evil.example.com` → `HTTP/1.1 200`이지만 **`Access-Control-*` 헤더가 하나도 없다**(= 브라우저가 차단) |
| 실제 `OPTIONS 204 → PUT 200` | ⚠️ **코드로 검증 불가** | 서명 URL은 서버가 발급하고 브라우저가 사용자 제스처로 올린다 → **§10.5 V20-1**(사람) |

> ⚠️ 두 번째 명령의 상태줄이 `200`인 것은 정상이다. **판정 기준은 상태 코드가 아니라 `Access-Control-*` 헤더의 유무**다 — GCS는 허용되지 않은 오리진에도 200을 주고 헤더만 뺀다.

---

## 6. A5 · kiosk 사이트 첫 배포

### 6.1 준비된 것 (이 브랜치에서 완료)

| 항목 | 상태 |
|------|------|
| kiosk 사이트(`mcphoto-955fb-kiosk`) | **생성 완료** |
| `.firebaserc` 타깃 2개(`default`·`kiosk`) | **등록 완료** |
| `web/firebase.json` hosting 배열화 + kiosk CSP·캐시 헤더 | **완료**(기존 default 블록 무변경) |
| `webclient/deploy.bat` | **완료**(빌드 + `--only hosting:kiosk`) |
| `web/deploy-web.bat` | **`hosting:default`로 고정**(§6.3) |

### 6.2 절차 (PowerShell)

```powershell
cd E:\Study\photobooth\webclient

# 1) 빌드 주입값 — webclient/.env.production.local (gitignore 대상, 커밋되지 않는다)
Set-Content .env.production.local -Encoding ascii -Value @(
  "VITE_BACKEND_API_KEY=<A3의 웹 키>",
  "VITE_GOOGLE_CLIENT_ID=<A1의 client_id>",
  "VITE_APP_VERSION=0.1.0"
)

# 값이 아니라 키 이름만 확인
Get-Content .env.production.local | ForEach-Object { ($_ -split '=')[0] }

# 2) 빌드 + 배포
.\deploy.bat
```

메모장으로 `webclient\.env.production.local`을 만들어 세 줄을 넣어도 결과는 같다.

| 값 | 어디서 | 비밀인가 |
|-----|--------|:--------:|
| `VITE_BACKEND_API_KEY` | A3에서 만든 **웹** 게이트 키(`$webKey`) | **아니다** — JS 번들에 그대로 들어가 브라우저에서 보인다(WD10). Windows 키를 넣지 않도록 주의 |
| `VITE_GOOGLE_CLIENT_ID` | A1의 client_id(`….apps.googleusercontent.com`) | 아니다 |
| `VITE_APP_VERSION` | 하단 캡션에 `v0.1.0`으로 표시된다 | — |

> ⚠️ **`VITE_*` 값은 전부 브라우저에 공개된다.** client_secret·JWT_SECRET·Windows 게이트 키를 여기에 넣으면 안 된다.
> 비밀은 서버(Secret Manager)에만 둔다.

### 6.3 ⚠️ 기존 P1 배포 스크립트가 바뀌었다

Hosting이 멀티사이트가 되면서 `firebase deploy --only hosting`은 **두 사이트를 동시에** 배포한다. `web/kiosk/`는 gitignore된 빌드 산출물이므로, 클론 직후·CI에서는 **배포가 실패하거나 낡은 빌드가 공개된다**. 그래서 `deploy-web.bat`의 배포 대상을 **`hosting:default`로 고정**했다(근거: [analysis/80 §6.5](../analysis/80-build-and-deployment.md)).

**한 번 실행해 P1 페이지가 그대로인지 확인해 주세요**:

```bash
cd web && deploy-web.bat hosting
```

스크립트가 배포 후 `public/`과 실서버 바이트를 대조한다(기존 검증 로직 그대로).

### 6.4 검증

```powershell
# curl.exe 를 쓴다 — PowerShell의 `curl` 은 Invoke-WebRequest 별칭이라 -sI 가 통하지 않고,
# grep 도 없다(Select-String 을 쓴다).
curl.exe -sI https://mcphoto-955fb-kiosk.web.app/ |
  Select-String -Pattern "HTTP/|content-security-policy|x-content-type-options|cache-control"

# P1 사이트 무변경
curl.exe -sI https://mcphoto-955fb.web.app/ | Select-Object -First 1
```

| 경로 | 기대 `Cache-Control` | 왜 |
|------|----------------------|-----|
| `/` · `/oauth2callback` · `/index.html` | **`no-cache, max-age=0`** | 재배포가 즉시 반영돼야 한다 |
| `/assets/**` | `public, max-age=31536000, immutable` | 파일명에 해시가 있어 영구 캐시가 안전하다 |

> ⚠️ **`/`가 `max-age=3600`으로 나오면 안 된다.** Hosting은 헤더를 **rewrite 이전의 요청 경로**로 매칭하므로
> `/index.html` 규칙만으로는 손님이 실제로 여는 `/`가 덮이지 않는다(2026-07-31 실측으로 발견해 `firebase.json`에
> `/`·`/oauth2callback` 규칙을 추가했다). 그대로 두면 재배포 후 최대 1시간 동안 옛 HTML이 서빙된다.

| 확인 | 기대 |
|------|------|
| kiosk 화면 | 브랜딩 타이틀 + 하단 캡션 **`v0.1.0`**(배포 채널·빌드 시각 문자열이 **없다** — it18) |
| 브라우저 콘솔 | **CSP 위반 0건** |
| P1 페이지 | 기존과 동일하게 동작(`?s=<유효토큰>`으로 확인) |

> 첫 배포에서는 더미 13화면 전이 UI가 보인다(Step 7부터 실제 화면으로 교체된다). **의도된 상태**다.

### 6.5 재감사 (2026-08-01)

| 확인 항목 | 결과 | 실측 |
|---|---|---|
| kiosk 사이트가 살아 있다 | ✅ | `GET https://mcphoto-955fb-kiosk.web.app/` → **200** |
| `/`의 캐시 헤더(§6.4의 함정) | ✅ | `Cache-Control: no-cache, max-age=0` — `max-age=3600`으로 되돌아가지 않았다 |
| CSP | ✅ | `default-src 'self'` … `frame-ancestors 'none'` 전량 존재 |
| nosniff | ✅ | `X-Content-Type-Options: nosniff` |
| **서빙 중인 번들이 최신 코드인가** | ⚠️ **A5의 범위 밖 · 별도 확인 필요** | A5는 "사이트를 만들고 한 번 배포했다"까지다. 그 뒤 `webclient/`가 계속 바뀌었으므로 **지금 공개된 JS가 최신이라는 보장은 없다** — 확인·갱신은 `webclient\deploy.bat` 재실행이다. 이 문서의 ✅는 "사이트·헤더가 정상"이라는 뜻이지 "최신 빌드가 서빙 중"이라는 뜻이 아니다 |
| P1(`mcphoto-955fb.web.app`) 무변경 | ⚠️ 미실행 | 이번 재감사에서는 kiosk만 확인했다. P1은 §6.4의 두 번째 명령으로 확인한다 |

### 6.6 배포 전 확인 (2026-08-01, 실사고 반영)

2026-08-01 실사용 이슈 4건 처리 중 실제로 있었던 일이다 — 다음 사람은 같은 구조에 걸리지 말 것.

- `deploy-web.bat`의 기본 타깃은 **`all`**(functions + hosting:default)이다. functions만 올릴 생각으로 그냥 돌리면 **P1 다운로드 페이지까지 함께 배포된다.**
- **배포 전 `git status`가 깨끗한지 반드시 확인한다.** 미커밋 워킹트리에서 배포하면 **커밋되지 않은(따라서 리뷰도 거치지 않은) 코드가 그대로 프로덕션에 올라간다** — 2026-08-01에 실제로 발생했다.
- `web/kiosk/`는 gitignore된 **로컬 스테이징 디렉터리**다. 누가 언제 무엇을 빌드해 뒀는지 알 수 없으므로, **배포 직전에는 항상 새로 빌드한다.**
- 배포 후 검증은 **"스탬프 일치"로 하지 않는다.** functions의 predeploy 훅이 배포할 때마다 `build-stamp.json`을 새로 찍으므로, 로컬 스탬프와 서버 `deployedAt`이 같은 것은 정상 배포라면 항상 성립하는 현상이지 "이 배포가 내가 의도한 코드다"의 증거가 아니다. 대신 **`GET /health`에 그 릴리스 고유의 필드가 있는지**로 확인한다(지금은 `oauth` 필드가 그 역할을 한다 — 있으면 그 릴리스 이후 코드가 배포된 것).

---

## 7. 지금까지 완료된 구현 (이 브랜치)

Step별 상세 산출물·검증·이탈 사항은 [11 · WBS](./11-wbs.md)의 각 Step 체크박스에 기록했다. 요약:

| Step | 산출물 | 검증 |
|------|--------|------|
| **1** 스캐폴드 | Vite 5 + React 18 + TS strict, `outDir=../web/kiosk`, `env.ts`(두 URL 정규화 방향 반대·빈 값 폴백), manifest·아이콘·branding.json, Hosting 멀티사이트 | `tsc` + 빌드 |
| **2** 도메인 + 벡터 | `src/domain/**` 28개 모듈(`MCPhoto.Core` 순수 로직 전량), `roundHalfToEven`, **`docs/spec-vectors/` 14파일 271케이스**, `SpecVectorTests.cs` | 웹 커버리지 **domain 100%**, **Windows·웹이 같은 벡터를 읽어 동시 통과**(트리거 확인 완료) |
| **3** 저장 계층 | OPFS **단일 Worker 경계**(3단 능력 판정), `settingsRepo`(알 수 없는 키 보존·게스트 값 보존·저장 boolean), IndexedDB 로그 링버퍼 + 마스킹, 브랜딩 800ms, 부트스트랩 1~6단계 | 79 테스트 |
| **4** 앱 셸 | **M1 배선**(구독 1곳이 모든 로그아웃 경로를 덮음), **M2 메모리 전용 토큰**, 전이·오버레이 복귀·`returnHome` 6단계, **실경과 유휴 감시**, 탭 hidden 취소(WM4), M16 복구, UI 기본 + 더미 13화면 | 36 테스트 |
| **5** HTTP | 예외 5종 매핑(`TEMP_USER_*` 403을 권한 오류와 분리), 게이트 키 전 호출·Bearer 3수준·100초 타임아웃, 서비스 6종, **`PUT /frames/{id}` 미구현**(정책) | 48 테스트 |
| **6** 카메라 | Worker에서 **프레임당 1회** 거울+중앙 크롭 → 프리뷰·스틸·타임랩스가 같은 결과 공유(WM1), Ready 게이트(가공 8프레임+500ms+fps>0, **8초 타임아웃**), `deviceId`→label→groupId→첫 장치 폴백, 카메라 테스트 모달 | 35 테스트 + **WM1 정적 검사** |
| **7** 촬영 | 컷 루프 a~f 순서(플래시 off는 **캡처 후**), **실경과 카운트다운**(WM3), [바로 촬영] 매 컷, 취소 시 부분 결과 없음(WM4), **컷 수 N 하드코딩 없음**(7·9 수용), 화면 5종(Home·FrameSelect 최소판·Guide·Capture·CutSelect) | 26 테스트 |
| **8** 합성 | 순수 RGBA 합성 코어(브라우저·테스트 동일 경로), BT.601 흑백·밝게·뷰티, INTER_AREA 축소, **골든 이미지 체계**(Windows 생성 → 웹 대조) | 4필터 허용 오차 통과 + 슬롯 0px |
| **서버** B1·B2·B4 | `redirectUri` 허용목록(완전 일치), audience 목록 + `clientKind`, 웹 게이트 키 배선 | 서버 316 테스트 |

### 7.1 전체 테스트 현황

```bash
cd webclient     && npx tsc --noEmit && npx vitest run     # 492 통과 (도메인 커버리지 100%)
cd web/functions && npm test                                # 316 통과
dotnet test tests/MCPhoto.Tests                             # 839 통과 (기존 823 무회귀 + 벡터 16)
```

### 7.2 구현 중 발견해 고친 결함 4건

| # | 무엇 | 왜 위험했나 |
|---|------|-------------|
| 0 | `requestVideoFrameCallback`을 TS 타입대로 필수로 다룸 | DOM lib은 필수라고 선언하지만 **Safari 15.4 미만에는 없다** — 타입을 믿고 분기를 빼면 그 기기에서 프레임 루프가 시작되지 않는다 → 옵셔널 타입으로 감싸 런타임 감지 유지 |
| 1 | `redirectUri` 검사 순서(loopback 우선) | 개발용 `http://localhost:5173/oauth2callback`이 loopback 규칙(경로 `/`만 허용)에 걸려 **허용목록에 등록해도 영구히 400**이었다 → 허용목록 우선으로 교체 |
| 2 | `isStorageLow`의 부동소수 오차 | `1 - 900/1000 = 0.0999…` 때문에 **정확히 임계값(여유 10%)이 경고로 넘어갔다** → 바이트 정수 비교 |
| 3 | `globalErrorHandler` 쿨다운 초기값 `0` | `now()`가 작은 값을 주는 시계에서 **첫 오류 복구가 쿨다운에 먹혔다**(화이트스크린 위험) → `-Infinity` |
| 4 | 로그 마스킹의 `code` 키 | OAuth 인가 코드용 마스킹이 **오류 코드까지 가려** 진단이 불가능했다 → 마스킹 유지 + 로그 필드를 `errorCode`로 분리 |

### 7.3 규격 해석을 확정한 것

**프레임 이름의 `_`** 는 문서 내 모순이 아니라 **스코프가 다른 두 규칙**이었다. 서버가 `POST /frames`의 이름에 `_`가 있으면 **400으로 거부**한다(`web/functions/src/domain/validation.ts`). 따라서:

- **서버 등록 경로**(power 신규 공용 프레임) → **하드 거부**(M15·E13이 맞다) = `validateFrameNameForServer`
- **로컬 전용 저장**(advanced_user 개인) → 서버를 거치지 않으므로 거부 사유가 없고, 공용 파일명 규약 충돌만 **비차단 경고** = `validateFrameName` + `underscoreWarning`

문서 정정은 필요하지 않다.

---

## 8. 다음 개발 단계

**Step 9(타임랩스 인코더)** 가 다음이며 **A1~A5를 기다리지 않는다.**

크리티컬 패스: `10 → 11`(★ 마일스톤 A = 게스트 촬영 완주). 이 경로는 **A4(CORS)·A5(배포)만** 필요하고 로그인 관련 A1~A3은 Step 12에서 필요해진다.

| 단계 | 선행 사용자 액션 |
|------|------------------|
| Step 9·10 | **없음** |
| Step 11(업로드·QR) | A4(CORS) · A5(배포, 실기기 검증용) |
| Step 12(로그인) 이후 | A1 · A2 · A3 |

---

## 9. 롤백

| 대상 | 방법 |
|------|------|
| 이 브랜치 전체 | `git checkout main` — `main`은 건드리지 않았다 |
| kiosk 사이트 | `npx firebase hosting:sites:delete mcphoto-955fb-kiosk` + `firebase.json`의 kiosk 블록·`.firebaserc` 타깃 제거 |
| 서버 OAuth 확장 | 해당 커밋 revert + `deploy-web.bat functions`. `defineSecret` 선언을 되돌리면 시크릿 등록도 불필요해진다 |
| 웹 게이트 키 | `CLIENT_API_KEYS`에서 그 키만 제거 + 재배포 |
| 버킷 CORS | 이전 구성으로 재적용(원래 **미설정** 상태였다) |

---

## 10. 실기기·브라우저 실측 (코드로 대체할 수 없는 검증)

단위 테스트는 **판정 규칙**을 고정하지만 **하드웨어 동작**은 고정하지 못한다. 아래는 사람이 눈으로 봐야 하는 항목이며, 자동화가 불가능한 것만 남겼다.

### 10.1 Step 6·7(카메라·촬영) 실측 — 지금 가능

> **수행 순서 → [16 실기기 절차서](./16-field-verification-runbook.md)의 `S1`**(V7은 `S7`·iPad). 이 절은 **항목 정의의 진실원**이다.

`npm run dev`(localhost는 보안 컨텍스트라 카메라가 열린다) 또는 A5 배포 후 실기기에서, 더미 화면의 **[카메라 테스트 열기]** 로 확인한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V1 | 프리뷰가 뜨고 부드럽다 | 24fps 이상. 하단 캡션에 실제 해상도·실측 fps 표시 | 실제 카메라 프레임이 필요하다 |
| V2 | **거울모드 on/off가 프리뷰에 즉시 반영** | 설정을 바꾸면 재시작 없이 좌우가 바뀐다 | 픽셀 결과 확인이 필요하다 |
| V3 | **모달을 닫으면 카메라 LED가 꺼진다** | 즉시 소등 | 하드웨어 표시등 |
| V4 | 카메라를 막은 환경(권한 거부·장치 없음)에서 **8초 내 실패 문구** | 무한 로딩이 아니다 | 권한 거부 조작 |
| V5 | 장치가 2개 이상이면 목록이 뜨고 전환된다 | 전환 시 정지 후 재시작된다 | 다중 카메라 하드웨어 |
| V6 | 셔터를 누르면 플래시가 번쩍이고 **"저장되지 않았습니다"** | 결과물이 남지 않는다 | 시각 확인 |
| V7 | iOS/iPadOS Safari에서 전체화면으로 전환되지 않는다 | 인라인 재생 유지(`playsinline`) | iOS 실기기 |
| **V14** | **6컷 세션 완주** — 홈 → 프레임 선택 → 가이드 → 촬영 | 카운트다운·플래시·300ms 간격을 지키며 6장 | 실제 프레임·타이밍 |
| **V15** | DevTools → OPFS에 `sessions/{id}/cut1..6.jpg` 생성 | 6개 파일 | 저장소 관측 |
| **V16** | **촬영 중 탭 전환** → 홈 복귀 + **부분 컷 미잔존** | OPFS 세션 폴더가 사라진다(WM4) | 탭 전환 조작 |
| **V17** | [바로 촬영]을 **매 컷** 눌러 세션을 짧게 완주 | 남은 카운트다운을 건너뛰고 즉시 촬영 | 연속 입력 |

> V2가 실패하면 **WM1 파손**이다(프리뷰만 반전되고 저장은 원본). 정적 검사가 `scaleX(-1)`을 막고 있으므로 발생 가능성은 낮지만, 이 항목이 실기기에서 확인해야 하는 이유는 "**저장 결과와 프리뷰가 같은가**"가 계약이기 때문이다 — Step 7·8이 완성되면 저장본과 대조한다.

### 10.2 Step 3·4 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S1`**(V13은 `S5`·배포본). 이 절은 **항목 정의의 진실원**이다.

| # | 확인 | 방법 |
|---|------|------|
| V8 | OPFS 잔재 정리 | DevTools → Application → OPFS에 더미 `sessions/x/` 폴더를 만들고 새로고침 → **사라진다**. `results/`·`frames/`는 남는다 |
| V9 | 설정 영속 | localStorage에 `mcphoto.settings.v1` 존재. 값을 손상시켜도 기본값으로 뜬다 |
| V10 | 13화면 전이 | 더미 화면 버튼으로 순회. 불법 전이는 버튼이 없다 |
| V11 | 유휴 경고 | 촬영 흐름 화면에서 2분 무동작 → 경고 → 10초 → 홈. **로그인이 유지된다** |
| V12 | 전체화면 배너 | ESC로 이탈 → 상단 배너 → [다시 전체화면으로] |
| V13 | 콘솔 CSP 위반 0건 | 배포본(A5)에서 확인. 로컬 dev 서버는 CSP가 적용되지 않는다 |

### 10.3 V18 · Step 9(타임랩스) 실기기 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S2`**(V18-4는 `S6`·`S7`, V18-6·V18-7은 `S5`). 이 절은 **항목 정의의 진실원**이다.

Step 9 구현이 끝났다(2026-07-31). **단위 테스트는 판정 규칙만 고정한다** — 브라우저 인코더가
실제로 재생 가능한 mp4를 만드는지는 코드로 증명할 수 없다. 아래 7건은 **사람이 눈으로 봐야 한다**.

`npm run dev`(localhost는 보안 컨텍스트라 카메라가 열린다) 또는 A5 배포본에서 **6컷 세션을 완주**한 뒤
`Result`에서 [다음]을 누른다. 진단 로그는 DevTools → Application → IndexedDB(`mcphoto-logs`)에서 본다
(운영 기기에서는 Step 16 진단 모달의 [로그 내보내기]).

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V18-1 | 생성된 mp4가 **재생되고 컨테이너가 정상 종료**됐다 | `<video>`로 끝까지 재생. `ffprobe`가 `moov`를 읽고 duration을 보고한다 | 실제 H.264 인코더 출력이 필요하다(가짜 인코더로는 컨테이너가 안 나온다) |
| V18-2 | 코덱이 `h264`, **오디오 트랙 0개**, 길이 10~15초(6컷 ~38초 세션) | `ffprobe -show_streams`에 video 1개뿐 | 동상 |
| V18-3 | **[바로 촬영]을 매 컷 눌러 ~5초로 줄인 세션**도 원속 ~5초 mp4가 나온다(`null`이 아니다) | 로그 `타임랩스 생성`의 `speedFactor: 1`, `durationSec ≈ 5` | 연속 입력 조작 |
| V18-4 | **모바일(iOS Safari 16.4+)·데스크톱 양쪽에서 재생**된다 | 두 기기 모두 재생 | 실기기 |
| V18-5 | 인코딩 소요 **≤6초**(A4), 촬영 중 프리뷰 **≥24fps 유지**(A5), `droppedSpool` 수치(A7) | 로그 `타임랩스 생성`의 `elapsedMs ≤ 6000`, `타임랩스 수집 종료`의 `droppedSpool`이 `spooled`에 비해 작다 | 하드웨어 성능 계측 |
| V18-6 | 인코더 미지원 브라우저(Firefox 등)에서 **촬영이 완주하고 타임랩스만 없다** | 로그 `타임랩스 미제공(브라우저 H.264 인코더 없음)` + 화면은 `Qr`/`Done`으로 정상 전이 | 브라우저별 `isConfigSupported` 결과 |
| V18-7 | 경로 A(`MediaRecorder`) 실제 동작(A6) | 로그 `타임랩스 인코더 경로 판정`의 `path` 확인. **지원 매트릭스상 도달하지 않으면 "미도달"로 기록해도 합격**이다([04 §7.3] 이유 ③) | 경로 A가 선택되는 브라우저를 인위적으로 만들 수 없다 |

> **진단에 남는 항목**(05 §7.2): `타임랩스 인코더 경로 판정`(`path`·`codecName`·`reason`) ·
> `타임랩스 수집 시작/종료`(`intervalMs`·`spooled`·`droppedSpool`·`decimations`·`elapsedSec`) ·
> `타임랩스 생성`(`spooled`·`selected`·`encodedFrames`·`droppedFrames`·`skippedFrames`·
> `speedFactor`·`durationSec`·`bytes`·`elapsedMs`) · 실패 시 `타임랩스 생성 실패` + `reason`.
>
> ⚠️ **`ffprobe`는 개발 PC에서 돌린다.** 브라우저에서 생성한 mp4를 내려받는 경로는 Step 10·11이
> 붙기 전까지 없으므로, DevTools 콘솔에서 `Blob`을 직접 저장하거나 Step 10 완료 후 확인한다.

### 10.4 V19 · Step 10(결과물 로컬 보관) 실기기 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S2`**(V19-5는 `S6`·`S7`). 이 절은 **항목 정의의 진실원**이다.

Step 10 구현이 끝났다(2026-07-31). 단위 테스트는 **순서·판정·경로 문자열**만 고정한다 —
브라우저가 실제로 파일을 남기는지, `usage` walk가 [다음] 체감을 해치지 않는지는 코드로 증명할 수 없다.

`npm run dev` 또는 A5 배포본에서 세션을 완주하고 `Result`에서 [다음]을 누른 뒤 확인한다.
OPFS는 DevTools → Application → Storage → **Origin Private File System**에서 본다(Chromium).

| # | 확인 | 기대 | 왜 자동화가 안 되나 |
|---|------|------|---------------------|
| V19-1 | **네트워크를 끊고**(DevTools → Network → Offline) 촬영을 완주하면 OPFS `results/mcphoto_YYMMDD_HHMM/final.jpg`가 **존재**한다(E8·A5) | 파일이 보이고 크기가 0이 아니다. 타임랩스가 있으면 `timelapse.mp4`도 같은 폴더에 있다 | 실제 OPFS 쓰기가 필요하다 |
| V19-2 | 로그 `결과물 로컬 보관`의 `status`가 `saved`이고 `elapsedMs`가 **[다음] 체감을 해치지 않는다**(A1 — `usage` 재귀 walk 포함) | `elapsedMs`가 수백 ms 이내. **300ms를 넘으면** 보존 정리(`enforceRetention`)를 `void`로 돌리고 `evicted`를 `-1`로 보고하도록 후속 조치한다([설계 §5.3]) | 하드웨어·저장소 성능 계측 |
| V19-3 | 데스크톱 Chromium에서 `Settings` 화면의 **[로컬 저장 폴더 선택]** 으로 폴더를 지정하면, 다음 촬영부터 **그 폴더에도 같은 파일이 생긴다** | 탐색기에서 `mcphoto_YYMMDD_HHMM/final.jpg` 확인. 로그 `folderCopy: "copied"` | `showDirectoryPicker`는 사용자 제스처가 필요하다 |
| V19-4 | 폴더 권한을 잃은 뒤(브라우저 재시작 등) 촬영해도 **흐름이 멈추지 않는다** | 로그 `folderCopy: "permission-required"`, 손님 화면에 토스트 **없음**, `Qr`/`Done`으로 정상 전이 | 권한 상실 상태를 인위적으로 만들 수 없다 |
| V19-5 | 폴더 저장 **미지원 브라우저**(Safari·Firefox·모바일)에서 [로컬 저장 폴더 선택] 버튼이 **렌더되지 않는다** | 버튼 부재 + 로그 `folderCopy: "unsupported"` | 브라우저별 기능 감지 결과 |
| V19-6 | OPFS 쓰기 실패를 유발하면(저장소 할당량 소진 등) **실패 토스트**가 뜨고 그래도 전이한다 | "저장 위치에 쓸 수 없습니다." + 화면은 계속 진행 | 할당량 소진 상황 재현 |

> **진단에 남는 항목**: `결과물 로컬 보관`(`status`·`folderName`·`finalSaved`·`timelapseSaved`·
> `hadTimelapse`·`folderCopy`·`folderCopyName`·`evicted`·`bytes`·`elapsedMs`) ·
> `결과물 로컬 보관 건너뜀`(`reason`) · `보관 결과물 정리`(`removed`·`keptCount`·`keptBytes`·`triggers`).
>
> ⚠️ **V18-1·V18-2(`ffprobe`)를 여기서 함께 처리할 수 있다.** V19-3으로 폴더를 지정해 두면
> 타임랩스 mp4가 실제 파일로 떨어져 개발 PC에서 바로 `ffprobe`를 돌릴 수 있다.

### 10.5 V20 · Step 11(업로드 3단계 · QR · 완료) 실기기 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S2`**(V20-4는 `S8`·폰, V20-5는 `S1`). 이 절은 **항목 정의의 진실원**이다.

Step 11 구현이 끝났다(2026-07-31). 단위 테스트는 **호출 순서·헤더 부착·판정·문구**만 고정한다 —
**브라우저가 실제로 서명 URL에 PUT을 성공시키는지**(OA-1)와 **폰이 QR을 읽는지**는 코드로 증명할 수 없다.

`npm run dev` 또는 A5 배포본에서 확인한다. ⚠️ **게스트는 `Qr`에 도달하지 않는다**(VF-11) —
V20-1~V20-3은 **실제로 Google 로그인한 뒤**(Step 12 완료로 지금 가능하다) `Qr`까지 가면 된다.
목으로 대체하려면 effective QR을 `true`로 고정하거나 세션 사용자를 주입한다.
**제품 코드에 게이트 우회 플래그를 남기지 않는다**(정적 grep이 0건을 고정한다).

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V20-1 | **서명 PUT이 브라우저에서 성공한다**(OA-1) | DevTools Network에 `OPTIONS 204` → `PUT 200`(또는 `204`). 로그 `서명 PUT 완료`의 `status`·`bytes`·`headerNames` | 실제 GCS 서명·버킷 CORS 왕복이 필요하다 | A4(CORS — 구성 완료) |
| V20-2 | **`requiredHeaders`가 전량 붙는다**(M14) | 요청 헤더에 `Content-Type`과 `x-goog-meta-firebaseStorageDownloadTokens`가 **둘 다** 있고, 인증 헤더는 **없다**. 로그 `headerNames`가 prepare 응답 키와 같다 | 실제 prepare 응답이 필요하다 | V20-1 |
| V20-3 | **진행률이 실제로 온다**(A2) | `lengthComputable: true`로 진행률이 0→100 증가한다(타임랩스처럼 큰 파일에서 확실히 보인다). 0에서 100으로 **점프하지 않는다** | 브라우저·네트워크 속도에 의존한다 | V20-1 |
| V20-4 | **폰으로 QR을 스캔해 다운로드 페이지가 열린다**(A4-가정) | 기본 카메라 앱이 인식 → P1 페이지 → 사진·영상 다운로드. **kiosk 도메인이 아니라 P1 도메인**이 열린다 | 물리 폰과 카메라가 필요하다 | ~~Step 12~~ **해소(2026-08-01)** → V21-5 |
| V20-5 | **[기기에 저장]이 실제 파일을 남긴다** | 다운로드 폴더에 `MCPhoto_{yyyyMMdd}_{HHmmss}.jpg` / `…_timelapse.mp4`. **파일명에 UUID가 없다.** 파일이 2개면 버튼도 2개 | 브라우저 다운로드 동작·사용자 제스처가 필요하다 | 없음(지금 가능) |

**실패 경로도 함께 본다**(코드로는 목으로만 검증했다):

| # | 확인 | 기대 |
|---|------|------|
| V20-6 | 네트워크를 끊고 `Qr`에 들어가면 | QR이 **뜨지 않고** "전송 실패 — 사진은 기기에 저장되었습니다."(로컬 저장 on) + [재시도]·[완료]가 보인다. 로그 `서명 PUT 실패`의 `hint`에 CORS 안내 |
| V20-7 | [재시도]를 누르면 | 진행률이 0에서 재시작하고 **새 세션 ID**로 prepare부터 다시 간다(로그 `업로드 대상 확정`의 `sameAsCaptureSession: false`). 409가 나지 않는다 |
| V20-8 | `Done`에서 6초 기다리면 | 자동으로 홈에 간다. **로그아웃되지 않는다**(상단 계정 라벨 유지 — M3). 탭을 숨겼다 6초 뒤 돌아오면 **즉시** 홈이다 |

> **진단에 남는 항목**: `업로드 대상 확정`(`uploadPhoto`·`uploadTimelapse`·`attempt`·`sameAsCaptureSession`) ·
> `업로드 prepare`(`kind`·`attempt`·`bucket`) · `서명 PUT 완료`(`kind`·`bytes`·`status`·`elapsedMs`·`headerNames`) ·
> `서명 PUT 실패`(`kind`·`failure`·`status`·`elapsedMs`·`headerNames`·`hint`) · `업로드 commit 완료`(`hasFinal`·`hasTimelapse`·`retentionHours`·`elapsedMs`) ·
> `업로드 실패`(`reason`·`attempt`·`elapsedMs`) · `QR 렌더`(`moduleCount`·`modulePx`·`canvasPx`).
>
> ⚠️ **서명 URL·다운로드 URL·세션 ID 원문은 로그에 없다**(전부 capability다 — analysis/41 §8).
> 그래서 진단만으로는 "어느 파일이 어디로 갔는지"를 알 수 없다 — 그 확인은 DevTools Network로 한다.

### 10.6 V21 · Step 12(Google SSO 리디렉트 · JWT) 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S2`**(V21-2·V21-6·V21-9·V21-10은 `S5`, V21-5는 `S8`). 이 절은 **항목 정의의 진실원**이다.

Step 12 구현이 끝났다(2026-08-01). 단위 테스트는 **URL 조립·판정 순서·오류 매핑·저장소 경계**만 고정한다 —
**실제 Google 인증이 끝까지 도는지**와 **배포본 CSP가 리디렉트를 막지 않는지**는 코드로 증명할 수 없다.

`npm run dev`(**포트 5173 고정** — `strictPort: true`) 또는 A5 배포본에서 확인한다.
⚠️ **로그인은 접속한 도메인 그대로 진행한다.** `web.app`으로 시작해 `firebaseapp.com`으로 돌아오면
`sessionStorage`가 오리진별이라 콜백이 **"취소되었습니다"** 로 끝난다(로그 `abortReason: "no-pending"`).

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V21-1 | **실 Google 계정으로 로그인 완주**(OA-5) | 상단 계정 라벨이 계정 id로 바뀌고 **직전 화면으로 복귀**한다. 주소창에 `code`·`state`가 **없다**. 로그 `로그인 성공`(`userId`·`role`·`expiresInSec`) | 실 Google 인증·실계정이 필요하다 | A1~A3 |
| V21-2 | **배포본(kiosk)에서 CSP 위반 0건** + 리디렉트가 막히지 않는다 | 콘솔에 CSP 오류 없음. `accounts.google.com`으로 이동 성공 | 배포 헤더가 붙은 환경이 필요하다(로컬 dev엔 CSP가 없다) | A5 |
| V21-3 | **`prompt=select_account`가 계정 선택 화면을 띄운다** | 브라우저에 Google 세션이 남아 있어도 **매번 계정 선택**이 뜬다(직전 손님 계정으로 자동 로그인되지 않는다) | 브라우저에 Google 세션이 남은 상태를 만들어야 한다 | V21-1 |
| V21-4 | **새로고침하면 게스트로 돌아간다**(C6) + **저장소에 토큰 0건**(E4) | DevTools Application에서 토큰 문자열 검색 0건(localStorage·sessionStorage·IndexedDB·쿠키). `mcphoto.oauth.pending.v1`도 콜백 후 **없다** | 브라우저 저장소 관측이 필요하다 | V21-1 |
| V21-5 | **로그인 상태에서 QR 완주 → 폰 스캔**(V20-4 선행 해소) | P1 다운로드 페이지가 열린다 | 물리 폰이 필요하다 | V21-1 |
| V21-6 | **`firebaseapp.com` 도메인으로 접속해도 로그인이 된다** | 두 도메인 모두 성공(A1에 둘 다 등록돼 있다) | 도메인별 접속이 필요하다 | A1 |

**실패 경로도 함께 본다**:

| # | 확인 | 기대 |
|---|------|------|
| V21-7 | Google 계정 선택 화면에서 **취소**하면 | `Login` 화면에 "Google 로그인이 취소되었습니다." + **[닫기]로 게스트 복귀 가능**. 로그 `Google 로그인 중단`(`abortReason: "provider-error"`) |
| V21-8 | 콜백 URL(`/oauth2callback`)에 **직접 접속**하면 | 같은 취소 문구. 로그 `abortReason: "no-pending"`. 스피너에 고착되지 않는다 |
| V21-9 | 서버 `OAUTH_REDIRECT_ALLOWLIST`가 어긋나 있으면 | 손님에겐 "네트워크를 확인해 주세요." + 로그 `서버가 redirectUri를 거부했다(B1 미적용 가능)`(`status: 400`·`errorCode`) |
| V21-10 | `VITE_GOOGLE_CLIENT_ID`를 비우고 빌드하면 | `Login`에 **버튼이 없고** "로그인이 구성되지 않았습니다…"만. **게스트 촬영은 정상 동작**한다 |

> **진단에 남는 항목**: `Google 로그인 리디렉트`(`returnTo`) · `Google 로그인 중단`(`abortReason`) ·
> `Google 로그인 교환 실패`(`failureReason`·`elapsedMs`) · `로그인 성공`(`userId`·`role`·`expiresInSec`) ·
> `서버가 redirectUri를 거부했다(B1 미적용 가능)`(`status`·`errorCode`) · `PKCE 생성 실패`(`reason`) ·
> `세션 만료 감지(401) — 세션 해제`(`path`) · `JWT 폐기`(`reason`).
>
> ⚠️ **`code`·`state`·`nonce`·`code_verifier`·토큰·authorize URL 전체·email은 로그에 없다**(비밀·개인정보).
> 그래서 "state가 왜 어긋났는지"는 진단으로 알 수 없다 — 그 확인은 DevTools로 한다.

> **PIN 1회 오입력이 로그아웃을 유발하지 않는다(E17)** 의 화면 관측은 Step 13 완료로 **지금 가능**해졌다 →
> **V22-3**(§10.8)로 이동. 코드 쪽 보장은 `verifyMyPin`·`setMyPin` 양쪽의 `unauthorized: "reject"`(정적 PIN-2) +
> `tests/unit/auth/sessionExpiry.test.ts`의 PIN 절이 고정한다.

### 10.7 아직 불가능한 실측

| 항목 | 필요 선행 |
|------|-----------|
| 10컷 세션 메모리(iOS 탭 생존) | Step 7 |
| 프레임 내보내기/가져오기 | **Step 16**(zip 번들 — `frameStore.saveLocal`은 Step 14에서 준비됨) |
| 앱 업데이트 확인 · [진단·상태] 모달 | **Step 16**(Service Worker · 진단 모달) |

> **해소됨**: "업로드 `OPTIONS 204 → PUT 200`"은 Step 11 완료로 **V20-1**로, "폰으로 QR 스캔 → 다운로드"는
> Step 12 완료로 **V21-5**로, "PIN 1회 오입력이 로그아웃을 유발하지 않는다(E17)"는 Step 13 완료로 **V22-3**으로 이동했다.

### 10.8 V22 · Step 13(진입 PIN 게이트 · 설정 화면) 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S2`**(V22-2는 `S1`, V22-4는 `S4`, V22-9·V22-11·V22-13은 `S6`). 이 절은 **항목 정의의 진실원**이다.

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 1297 통과). 아래는 **브라우저·실계정·실기기가 있어야만** 확인되는 항목이다.
> ⚠️ **추정으로 통과 처리하지 않았다.** V22-4는 **PIN이 설정되지 않은 실계정**이 있어야 한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V22-1 | 로그인 사용자가 [설정]을 누르면 **매번** PIN 모달이 뜬다 | 통과 → 설정 렌더. [닫기]로 나갔다 다시 들어가면 **또** 묻는다 | 실계정 로그인 + 브라우저 | V21-1 |
| V22-2 | **게스트**가 [설정]을 누르면 모달 없이 즉시 진입한다 | 네트워크 탭에 `accounts/me/pin*` 요청 **0건** | 브라우저 | — |
| V22-3 | **PIN 1회 오입력이 로그아웃을 유발하지 않는다**(E17 — Step 12에서 이월) | 상단 계정 라벨 **유지** + "PIN이 일치하지 않습니다. (1/5)" + 1.5초 키 비활성 | 실계정 + 화면 | V21-1 |
| V22-4 | **PIN 미설정 계정의 최초 설정 플로우**: 새 PIN 2회 → 저장 → **재확인 없이 진입**. 화면을 나갔다 다시 들어가면 **확인(verify) 모드** | 2회차에 401 데드락이 나지 않는다(`markPinSet` 검증 — A5) | PIN 없는 실계정이 필요하다 | V21-1 |
| V22-5 | **5회 실패 → 모달 닫힘 + 5분 잠금**, **탭을 닫았다 열어도 잠금 유지**(E16) | `localStorage["mcphoto.pinLock.v1"]`에 `{until,fails}` 존재. 재진입 시 모달 없이 "…{남은 시간} 후 다시 시도해 주세요." | 브라우저 저장소 관측 | V21-1 |
| V22-6 | 네트워크를 끊고 PIN 입력 | "확인할 수 없습니다. 네트워크를 확인하세요." + **실패 카운트 미증가** + **진입 차단**. 3회 시도해도 잠기지 않는다 | 오프라인 전환이 필요하다 | V21-1 |
| V22-7 | **게스트로 설정 저장 → 로그인 후 확인**(E23) | 거울모드·재촬영·필터 3종·QR 3종·URL 2종의 **운영자 값이 그대로**다. 게스트 화면에서는 이 항목이 OFF·비활성 + "로그인 필요" | 저장소 관측 + 계정 전환 | V21-1 |
| V22-8 | 컷 수 **8** 저장 → `Guide` 반영 · 컷 수 **자동**(sentinel 0) 저장 왕복 후 소멸하지 않음 | Guide에 "(자동)" 배지. 설정 재진입 시 "자동"이 선택돼 있다 | 실촬영 흐름 | — |
| V22-9 | 카메라 장치 선택·[재검색]·[카메라 테스트] · **권한 전 라벨이 "카메라 N"** | 장치 전환이 실제 프리뷰에 반영된다. 권한 허용 후 실제 장치명이 나온다 | 실 카메라 2대 | V1 |
| V22-10 | [보관된 결과물] 목록·용량·개별/전체 삭제 · 여유 10% 미만 경고 배지 | 삭제 후 목록·총량이 줄어든다. 전체 삭제는 **인라인 2단 확인**을 거친다 | 실제 촬영 결과물이 필요하다 | V19 |
| V22-11 | [폴더 선택]/[폴더 해제] · **미지원 브라우저(Safari·Firefox)에서 버튼 미노출** | Chromium에서만 보인다. 지정 즉시 저장되고 표시값이 폴더명으로 바뀐다 | 브라우저 3종 | — |
| V22-12 | [설정 내보내기] 파일이 열리고 **`BackendApiKey`가 없다** · [가져오기] 미리보기 → [적용] | 파일명 `mcphoto-settings-{YYMMDD_HHMM}.json`. `schemaVersion`을 99로 고치면 "더 새 버전의 설정 파일입니다." | 파일 다운로드·선택 | — |
| V22-13 | 온스크린 키패드 터치 타깃(48px) · `aria-live` 안내 | 스크린리더가 실패 사유("PIN이 일치하지 않습니다.")를 읽는다 | 실기기·보조기술 | — |

> **진단에 남는 항목**: `PIN 확인 통과`·`PIN 설정 완료`(`gateMode`) · `PIN 불일치`·`PIN 시도 소진`(`gateMode`·`failCount`) ·
> `PIN을 확인할 수 없습니다`(`gateMode`·`attemptOutcome`·`errorStatus`) · `PIN 연속 실패로 기기 잠금`(`lockUntil`·`failCount`) ·
> `PIN 게이트 거부`(`gateScreen`·`denyReason`) · `PIN 승인 폐기`(`discardReason`) · `PIN 모달이 표시되지 않았습니다`(`gateMode`) ·
> `제한된 설정 항목 편집 시도`(`settingKey`·`guest`) · `보관 결과물 삭제 실패`(`folderName`) · `설정 가져오기 적용`(`changeCount`).
>
> ⚠️ **PIN 값은 어디에도 없다**(정적 PIN-1이 고정한다). "무엇을 입력했는지"는 진단으로 알 수 없다 — 그것이 규격이다.

---

### 10.9 V23 · Step 14(프레임 저장소 · 프레임 선택 · it20 대기 국면) 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S1`·`S3`**(V23-5는 `S7`, V23-6은 `S9`). 이 절은 **항목 정의의 진실원**이다.

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 **1469** 통과). 아래는 **브라우저·실기기·실계정이 있어야만** 확인되는 항목이다.
> ⚠️ **추정으로 통과 처리하지 않았다.** V23-3·V23-8은 온라인 서버 프레임과 power 계정이 있어야 한다.

| # | 확인 | 기대 | 왜 자동화가 안 되나 | 선행 |
|---|------|------|---------------------|------|
| V23-1 | DevTools에서 **OPFS·IndexedDB를 비우고** Slow 3G로 `FrameSelect` 진입 | 진입 즉시 대기 오버레이 + `(n/m)` 카운터. **"빈 목록 + 활성 [다음]"이 한 프레임도 나타나지 않는다** | 저장소 초기화 + 네트워크 스로틀 | A5 |
| V23-2 | **오프라인 전환** 후 진입 | 안내 없이 조용히 목록 표시(`Ready`). *"…가져오지 못해…"* 문구가 **없다**(E20) | 오프라인 전환 | V23-1 |
| V23-3 | 온라인에서 **서버 프레임**을 골라 촬영·합성 | 합성 성공(canvas 오염 없음 — **WM2 · OA-2 종결**). 이 항목이 200 응답의 CORS-clean 여부(A-4)를 실증한다 | 실 서버 프레임 + 완주 | V21-1 |
| V23-4 | **두 번째 진입** | Network 패널에 프레임 이미지 요청 **0건**(이름 dedup) · `blob:` URL로 합성 성공(A-1) | Network 패널 관측 | V23-3 |
| V23-5 | **Safari(iOS 17)** 에서 목록 진입 | 썸네일이 보이고 콘솔 오류 0. `createImageBitmap` resize 미실효(A-3)여도 캔버스 폴백으로 카드가 정상이다 | 실 iOS 기기 | A5 |
| V23-6 | 프레임 20개 상태에서 **목록 왕복 10회** | 메모리 증가가 누적되지 않는다(썸네일 `close()` · object URL 경로당 1개 재사용 — A-2) | 메모리 프로파일러 | V23-3 |
| V23-7 | DevTools → Application → IndexedDB | `mcphoto`·`mcphoto-handles`·**`mcphoto-frames`** 3개가 각각 존재하고 **로그가 계속 쌓인다**(A-5 — blocked 없음) | 저장소 관측 | V23-1 |
| V23-8 | **power 계정**으로 서버 프레임 삭제(“서버에서도 제거” 체크) | 결과 문구가 4종 중 정확히 하나. 목록이 **오버레이 없이** 갱신된다(조용한 재스캔). 체크를 **끄면** 재스캔에서 카드가 되돌아온다(의도된 종전 동작 — K3) | manager/admin 실계정 | V21-1 |

> **진단에 남는 항목**: `기본 프레임 서버 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)` ·
> `기본 프레임 대기 중단 — 로컬 전용 폴백`(`reason`·`noProgressSec`·`totalSec`) · `개인 프레임 로드 실패(공용 목록은 유지)` ·
> `프레임 이미지 다운로드 실패(HTTP)`(`status`) · `프레임 이미지 다운로드 실패(네트워크 또는 CORS 차단 가능)` ·
> `createImageBitmap resize 옵션 미실효 — 캔버스 축소 폴백으로 전환`(`requested`·`got`) ·
> `프레임 이미지 파일이 없어 목록에서 제외`(`key`·`imageFile`) · `프레임 이미지 삭제 실패(파일이 남아 있음)` ·
> `서버 삭제 id 불일치 → 이름 매칭 재삭제`(`name`·`id`) · `서버 프레임 삭제 실패: 문서 미발견`(`name`·`triedId`) ·
> `공용 프레임 이름에 '_'가 있어 매 실행 재다운로드됩니다`(`name`) · `번들 프레임 로드`(`count`).

---

### 10.10 V24 · Step 15(프레임 편집기 · 피커 · 서버 등록) 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S1`·`S3`**(V24-2는 `S7`, V24-5는 `S6`·`S7`). 이 절은 **항목 정의의 진실원**이다.

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 **1655** 통과). 아래는 **브라우저·실기기·실계정이 있어야만** 확인되는 항목이다.
> ⚠️ **추정으로 통과 처리하지 않았다.** V24-3은 이 Step의 핵심 수락 조건(WYSIWYG 0px)이고, V24-4는 power 실계정이 필요하다.

| # | 확인 | 방법 | 기대 / 실패 시 조치 | 선행 |
|---|------|------|---------------------|------|
| **V24-1** | 대상 브라우저에서 PNG 재인코딩이 성공한다(A15-1) | JPG 5MB 로드 → 저장 → DevTools OPFS에서 `frames/*.png` 확인 | 저장된 파일이 PNG다. 실패 시 진단에서 `OffscreenCanvas convertToBlob 실패 — HTMLCanvasElement 폴백`을 확인해 폴백이 동작했는지 본다 | A5 |
| **V24-2** | EXIF 회전 JPG가 바로 선다(A15-2) | 아이폰으로 세로 촬영한 JPG를 [이미지 불러오기] | 미리보기가 눕지 않는다. 누우면 `imageOrientation: "from-image"` 전달 여부를 재확인 | 실기기 사진 |
| **V24-3** | **편집기 슬롯 위치 == 합성 결과 위치(0px)**(A15-3) | 프레임 저장 → 그 프레임으로 촬영 완주 → 결과 이미지와 편집기 화면 캡처를 겹쳐 비교 | 슬롯 4모서리가 일치한다. 어긋나면 `EditorTransform` 공유가 깨진 지점을 추적한다(표시·드래그·클램프가 같은 `transform`을 써야 한다) | V23-3 |
| **V24-4** | power 서버 등록 2단계가 실제로 성공한다(A15-4) | manager 계정 → 신규 생성 → [저장] → 오버레이 체크 **on** → 저장 | `POST /frames` 201 + 서명 PUT 200. Storage에 PNG가 생기고 **다른 기기에서 내려받힌다**. 403이면 `requiredHeaders` 전량 부착 여부와 버킷 CORS를 확인 | manager/admin 실계정 |
| **V24-5** | 태블릿 터치 드래그가 스크롤과 충돌하지 않는다(A15-5) | Android/iPad에서 슬롯을 끌어 본다 | 페이지가 스크롤되지 않고 슬롯만 움직인다. 충돌하면 `touch-action: none` 적용 범위를 확인 | 실기기 |
| ~~V24-6~~ | **해소됨(2026-08-01) — 실측 불필요.** 배율 범위는 **10~300**으로 확정했다. 규격 문서의 70~130은 커밋 `0a93b59`("슬롯 스케일 10~300%·직접입력")가 넓히기 전의 폐기값이었고, 진실원 우선순위(소스 > analysis)에 따라 문서 6곳을 소스에 맞췄다. **번호는 다른 문서가 참조하므로 재사용하지 않는다** | — | — | — |
| **V24-7** | 저장 취소 후 임시 파일이 남지 않는다 | 이미지 로드 → 슬롯 조작 → [취소] → DevTools OPFS `frames/` 확인 | 새 파일이 **0건**이다(디스크 쓰기는 저장 1회뿐 — 03 §11.7). 생기면 로더가 디스크에 쓰고 있지 않은지 본다 | A5 |
| **V24-8** | 서버 등록 실패 후 체크를 끄고 재저장하면 성공한다 | 오프라인에서 체크 on 저장(실패 확인) → **온라인 복귀 없이** 다시 [저장] → 체크 해제 → 저장 | 로컬 저장이 성공한다. 실패하면 원자성이 깨져 ⑦ 가드가 **자기 자신과 충돌**하고 있는 것이다(로컬에 이미 저장돼 버린 경우) | manager 실계정 |

> **진단에 남는 항목**: `프레임 서버 등록 실패(문서 생성)`(`reason`) · `프레임 서버 등록 실패(업로드 URL 없음)`(`orphanFrameId`) ·
> `프레임 서버 등록 실패(이미지 업로드)`(`orphanFrameId`·`failure`·`status`) · `고아 프레임 문서 정리 실패`(`orphanFrameId`) ·
> `프레임 로컬 저장 실패`(`registeredToServer`·`dbId`) · `이전 프레임 이미지 정리 실패(고아 파일이 남을 수 있음)`(`key`·`imageFile`) ·
> `프레임 이미지 디코드 실패` · `OffscreenCanvas convertToBlob 실패 — HTMLCanvasElement 폴백` ·
> `편집 대상 프레임 이미지 fetch 실패` · `편집 권한이 없는 프레임 진입 — 신규 생성으로 강등`(`frameId`·`role`) ·
> `피커 공용 목록 대기 중단 — 로컬 전용 폴백`(`reason`) · `스코프 프레임 이름 조회 실패(⑦ 가드 비활성)`(`scope`).
>
> ⚠️ **서명 URL·헤더 값은 로그에 없다**(analysis/41 §8). 헤더 **이름**만 남는다 — M14 진단의 단서다.

---

### 10.11 V25 · Step 16(계정 · 사용자 관리 · 진단 · PWA · 내보내기/가져오기) 실측 — 지금 가능

> **수행 순서 → [16](./16-field-verification-runbook.md)의 `S3`·`S5`**(V25-4는 `S7`에서도 재수행). 이 절은 **항목 정의의 진실원**이다.

> 코드는 완성돼 있다(2026-08-01, 웹 테스트 **1926** 통과 / 84파일). 아래는 **브라우저·실계정·Windows 앱이 있어야만**
> 확인되는 항목이다. ⚠️ **추정으로 통과 처리하지 않았다** — 특히 V25-1·V25-2는 Service Worker 실동작이고,
> V25-6은 설계 단계의 미검증 가정 A1(kiosk CSP `connect-src`가 `blob:`을 막는가)을 닫는 항목이다.

| # | 확인 | 방법 | 기대 / 실패 시 조치 | 선행 |
|---|------|------|---------------------|------|
| **V25-1** | **오프라인에서 앱이 로드된다** | 배포본 1회 방문(SW 설치 확인 — DevTools ▸ Application ▸ Service Workers) → Offline 체크 → 새로고침 | 셸이 렌더된다. 진단 [서버 연결]은 "도달 실패"다. 실패하면 Application ▸ Cache Storage에 `mcphoto-shell-*`가 있는지, `install` 이벤트가 `activated`까지 갔는지 본다 | 배포 |
| **V25-2** | **[지금 적용]이 새 SW를 활성화한다** | 재배포 → 기존 탭 유지 → 설정 ▸ [앱 업데이트 확인] → 진단 [앱] 섹션이 "업데이트 대기 중" → [지금 적용] | 새로고침 뒤 새 버전이 뜬다. **촬영 중(`Capture` 등)에는 버튼이 없다.** 대기 상태가 안 잡히면 `sw.js` 바이트가 실제로 바뀌었는지(자산 목록 인라인 — 15 §4 함정 14) 확인한다 | 배포 2회 |
| **V25-3** | 프레임 zip을 Windows `Frame\`에 풀면 인식된다 | 설정 ▸ [프레임 내보내기] → 압축 해제 → MC포토 `Frame\`에 복사 → 앱 실행 | 프레임 목록에 나타나고 슬롯 위치가 맞다. 개인 프레임은 `{계정}_{이름}.png`/`.slots` 쌍이다. 안 보이면 파일명에 `_`가 하나뿐인지(공용/개인 구분자) 확인한다 | Windows 앱 |
| **V25-4** | 탐색기로 다시 압축한 zip을 가져올 수 있다 | 위 폴더를 우클릭 ▸ [압축(ZIP) 폴더] → 설정 ▸ [프레임 가져오기] | 미리보기에 항목이 뜬다(deflate 해제 — A3). 미지원 브라우저면 **전용 안내**만 뜨고 앱이 죽지 않는다: "압축된 zip은 이 브라우저에서 읽을 수 없습니다…" | 동상 |
| **V25-5** | manager 실계정에서 [PIN] 미노출 · [삭제] 노출 | manager 로그인 → 계정 팝오버 ▸ [관리자 도구] ▸ [사용자 관리] | 다른 manager 행에 **[PIN]이 없고 [삭제]는 있다**(동급 삭제 허용 ↔ 동급 PIN 차단 — analysis/60 §1.3.1). 역할 콤보에 `admin`이 없고 자기 행에는 액션이 없다 | manager 실계정 |
| **V25-6** | **[선택 편집] 진입이 운영 CSP에서 성공한다(A1)** | 배포본에서 서버 공용 프레임 ▸ [선택 편집] | 이미지가 뜬다. 실패하면 콘솔의 CSP 위반 지시자를 확인한다 — `connect-src`에 `blob:`을 추가해 두었으므로(2026-08-01) 그래도 막히면 다른 지시자 문제다 | 배포 |
| **V25-7** | Lighthouse PWA 감사가 통과한다 | Chrome DevTools ▸ Lighthouse ▸ (Progressive Web App) | Installable + manifest·SW 항목 통과. 실패 항목은 `public/manifest.webmanifest`와 아이콘 3종을 먼저 본다 | 배포 |
| **V25-8** | **진단 모달에 게이트 키 값이 없다** | 진단 열기 → 화면 전문 검색 → [로그 내보내기]로 받은 `.log` 파일도 전문 검색 | `VITE_BACKEND_API_KEY` 값 문자열이 **0건**이다(화면은 "설정됨/거부됨/미설정"만 보여야 한다). 발견되면 즉시 DIAG-1 검사 대상 파일 목록을 넓힌다 | 배포 |

> **진단에 남는 항목**: `Service Worker 등록 실패`(`reason`) · `앱 업데이트 확인 실패`(`reason`) ·
> `촬영 중에는 앱 갱신을 적용하지 않는다` · `사용자 목록 조회 거부(권한 없음)` · `사용자 목록 조회 거부(서버 403)` ·
> `사용자 목록 조회 실패(네트워크)`(`reason`) · `계정 삭제`·`역할 변경`(`targetId`·`nextRole`) ·
> `타 계정 PIN 재설정`(`targetId`·`attemptOutcome`) · `전역 무료 한도 조회/저장 실패`(`reason`) ·
> `키오스크 종료` · `프레임 내보내기`(`exported`·`skipped`) · `프레임 가져오기 적용`(`imported`·`failed`) ·
> `zip deflate 해제 실패`(`reason`) · `클립보드 복사 실패`(`reason`) · `로그 내보내기 실패`.
>
> ⚠️ **PIN 값·게이트 키 값은 로그에 없다.** 남는 것은 `targetId`·`attemptOutcome`뿐이다(PIN-1·DIAG-2 정적 검사가 고정한다).
